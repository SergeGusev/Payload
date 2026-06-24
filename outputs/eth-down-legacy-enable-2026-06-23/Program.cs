using Npgsql;

if (!string.Equals(Environment.GetEnvironmentVariable("APPLY_ETH_DOWN_LEGACY_ENABLE"), "YES", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Set APPLY_ETH_DOWN_LEGACY_ENABLE=YES to enable old ETH Down Premarket strategies.");
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

Console.WriteLine($"enable_started_utc={DateTimeOffset.UtcNow:O}");
Console.WriteLine($"target_database={builder.Database}");
Console.WriteLine($"target_host={builder.Host}");
Console.WriteLine($"target_port={builder.Port}");

Console.WriteLine("before_counts");
await PrintCountsAsync();

await ExecuteNonQueryAsync(
    """
    CREATE TEMP TABLE tmp_eth_down_legacy_enable_targets ON COMMIT DROP AS
    SELECT id, code, enabled, live_stakes
    FROM strategies
    WHERE code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
    FOR UPDATE;
    """);

var updatedRows = await ExecuteScalarLongAsync(
    """
    WITH updated AS (
        UPDATE strategies strategy
        SET enabled = true,
            updated_at_utc = now()
        FROM tmp_eth_down_legacy_enable_targets target
        WHERE strategy.id = target.id
          AND strategy.enabled IS DISTINCT FROM true
        RETURNING strategy.id
    )
    SELECT count(*)::bigint
    FROM updated;
    """);
Console.WriteLine($"updated_enabled_rows={updatedRows}");

var liveStakesChangedRows = await ExecuteScalarLongAsync(
    """
    SELECT count(*)::bigint
    FROM tmp_eth_down_legacy_enable_targets target
    JOIN strategies strategy ON strategy.id = target.id
    WHERE strategy.live_stakes IS DISTINCT FROM target.live_stakes;
    """);
Console.WriteLine($"live_stakes_changed_rows={liveStakesChangedRows}");

if (liveStakesChangedRows != 0)
{
    await transaction.RollbackAsync();
    Console.Error.WriteLine("Rolled back because live_stakes changed unexpectedly.");
    return 3;
}

Console.WriteLine("after_counts");
await PrintCountsAsync();

await transaction.CommitAsync();
Console.WriteLine($"enable_finished_utc={DateTimeOffset.UtcNow:O}");
return 0;

async Task PrintCountsAsync()
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT 'target_total' AS name, count(*)::text AS value
        FROM strategies
        WHERE code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
        UNION ALL
        SELECT 'target_enabled', count(*)::text
        FROM strategies
        WHERE code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
          AND enabled
        UNION ALL
        SELECT 'target_disabled', count(*)::text
        FROM strategies
        WHERE code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
          AND NOT enabled
        UNION ALL
        SELECT 'target_live_stakes_true', count(*)::text
        FROM strategies
        WHERE code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
          AND live_stakes
        UNION ALL
        SELECT 'target_effective_live_true', count(*)::text
        FROM strategies
        WHERE code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
          AND enabled
          AND live_stakes
        UNION ALL
        SELECT 'target_names_with_fak', count(*)::text
        FROM strategies
        WHERE code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
          AND name ILIKE '%FAK%'
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
