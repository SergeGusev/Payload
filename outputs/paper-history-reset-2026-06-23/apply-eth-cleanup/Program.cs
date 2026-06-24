using System.Text.RegularExpressions;
using Npgsql;

if (!string.Equals(Environment.GetEnvironmentVariable("APPLY_ETH_PREMARKET_CLEANUP"), "YES", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Set APPLY_ETH_PREMARKET_CLEANUP=YES to apply production strategy cleanup.");
    return 2;
}

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 2;
}

var builder = new NpgsqlConnectionStringBuilder(connectionString);
var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
if (!string.IsNullOrWhiteSpace(hostOverride))
{
    builder.Host = hostOverride;
}

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();

await using var transaction = await connection.BeginTransactionAsync();
await ExecuteNonQueryAsync("SET LOCAL statement_timeout = '30s'; SET LOCAL lock_timeout = '2s';");

Console.WriteLine($"cleanup_started_utc={DateTimeOffset.UtcNow:O}");
Console.WriteLine($"target_database={builder.Database}");
Console.WriteLine($"target_host={builder.Host}");
Console.WriteLine($"target_port={builder.Port}");

Console.WriteLine("before_counts");
await PrintCountsAsync();

var nameUpdates = 0;
var targetRows = await LoadReferenceAverageRowsAsync();
foreach (var row in targetRows)
{
    var expectedName = BuildReferenceAverageName(row.Code);
    if (expectedName is null || string.Equals(row.Name, expectedName, StringComparison.Ordinal))
    {
        continue;
    }

    await using var update = connection.CreateCommand();
    update.Transaction = transaction;
    update.CommandText =
        """
        UPDATE strategies
        SET name = @Name,
            updated_at_utc = now()
        WHERE id = @Id
          AND name IS DISTINCT FROM @Name;
        """;
    update.Parameters.AddWithValue("Name", expectedName);
    update.Parameters.AddWithValue("Id", row.Id);
    nameUpdates += await update.ExecuteNonQueryAsync();
}

Console.WriteLine($"updated_reference_average_names={nameUpdates}");

var disabledOldRows = await ExecuteScalarLongAsync(
    """
    WITH updated AS (
        UPDATE strategies
        SET enabled = false,
            live_stakes = false,
            updated_at_utc = now()
        WHERE code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
          AND (enabled IS DISTINCT FROM false OR live_stakes IS DISTINCT FROM false)
        RETURNING 1
    )
    SELECT count(*)::bigint
    FROM updated;
    """);
Console.WriteLine($"disabled_old_no_suffix_eth_down_rows={disabledOldRows}");

Console.WriteLine("after_counts");
await PrintCountsAsync();

await transaction.CommitAsync();
Console.WriteLine($"cleanup_finished_utc={DateTimeOffset.UtcNow:O}");
return 0;

async Task<List<StrategyRow>> LoadReferenceAverageRowsAsync()
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT id, code, name
        FROM strategies
        WHERE description ILIKE '%largest full in-memory reference average%'
          AND code !~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
        ORDER BY code;
        """;

    var rows = new List<StrategyRow>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new StrategyRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2)));
    }

    return rows;
}

async Task PrintCountsAsync()
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT 'old_no_suffix_eth_down_enabled' AS name, count(*)::text AS value
        FROM strategies
        WHERE code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
          AND enabled
        UNION ALL
        SELECT 'old_no_suffix_eth_down_live_enabled', count(*)::text
        FROM strategies
        WHERE code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
          AND live_stakes
        UNION ALL
        SELECT 'reference_average_description_rows', count(*)::text
        FROM strategies
        WHERE description ILIKE '%largest full in-memory reference average%'
          AND code !~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
        UNION ALL
        SELECT 'reference_average_names_missing_phrase', count(*)::text
        FROM strategies
        WHERE description ILIKE '%largest full in-memory reference average%'
          AND code !~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
          AND name NOT ILIKE '%Reference Average%'
        ORDER BY name;
        """;

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"{reader.GetString(0)}={reader.GetString(1)}");
    }
}

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}

async Task<long> ExecuteScalarLongAsync(string sql)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    return Convert.ToInt64(await command.ExecuteScalarAsync());
}

static string? BuildReferenceAverageName(string code)
{
    var match = Regex.Match(
        code,
        "^(?<asset>btc|eth|sol)_up_down_5m_(?<trigger>up|down)(?:_reference_average)?_bps_(?<threshold>[0-9]+)_fak_premarket$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    if (!match.Success)
    {
        return null;
    }

    var asset = match.Groups["asset"].Value.ToUpperInvariant();
    var trigger = char.ToUpperInvariant(match.Groups["trigger"].Value[0]) + match.Groups["trigger"].Value[1..];
    var threshold = match.Groups["threshold"].Value;
    return $"{asset} Up or Down 5m {trigger} {threshold} bps Reference Average FAK Premarket";
}

internal sealed record StrategyRow(Guid Id, string Code, string Name);
