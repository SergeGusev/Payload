using System.Data;
using Npgsql;

var outputPath = args.Length > 0 ? args[0] : "outputs/enabled-strategy-count-2026-06-15/result.txt";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "EnabledStrategyCount"
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);

await ExecuteAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '15s';
SET LOCAL lock_timeout = '500ms';
""");

await WriteLineAsync($"Enabled strategy count captured at {DateTimeOffset.UtcNow:O}");
await WriteRowsAsync("""
SELECT
    count(*) AS total,
    count(*) FILTER (WHERE enabled) AS enabled,
    count(*) FILTER (WHERE NOT enabled) AS disabled,
    count(*) FILTER (WHERE live_stakes) AS live_stakes,
    count(*) FILTER (WHERE enabled AND live_stakes) AS enabled_live_stakes,
    count(*) FILTER (WHERE enabled AND paused) AS enabled_paused,
    count(*) FILTER (WHERE enabled AND auto_live_paused) AS enabled_auto_live_paused
FROM strategies;
""");

await WriteLineAsync("");
await WriteLineAsync("## Enabled by group");
await WriteRowsAsync("""
SELECT
    CASE
        WHEN code ~ '^(btc|eth|sol)_up_down_5m_middle_[0-9]+' THEN 'middle_n'
        WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$' THEN 'simple'
        WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_bps_[0-9]+(_instant)?$' THEN 'up_down_bps'
        WHEN code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'other_up_down_5m'
        ELSE 'other'
    END AS strategy_group,
    count(*) AS enabled_count,
    count(*) FILTER (WHERE live_stakes) AS live_stakes_count
FROM strategies
WHERE enabled
GROUP BY strategy_group
ORDER BY enabled_count DESC, strategy_group;
""");

await WriteLineAsync("");
await WriteLineAsync("## Service heartbeat");
await WriteRowsAsync("""
SELECT
    service_name,
    status,
    mode,
    to_char(started_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS started_utc,
    to_char(last_heartbeat_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS heartbeat_utc,
    round(extract(epoch FROM (now() - last_heartbeat_utc))::numeric, 1) AS heartbeat_age_seconds,
    COALESCE(NULLIF(last_error, ''), '<none>') AS last_error
FROM service_heartbeats
ORDER BY last_heartbeat_utc DESC
LIMIT 3;
""");

await transaction.CommitAsync();

async Task ExecuteAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    await command.ExecuteNonQueryAsync();
}

async Task WriteRowsAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    await using var reader = await command.ExecuteReaderAsync();
    var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
    await WriteLineAsync(string.Join("\t", names));

    var rows = 0;
    while (await reader.ReadAsync())
    {
        var values = new string[reader.FieldCount];
        for (var index = 0; index < reader.FieldCount; index++)
        {
            values[index] = reader.IsDBNull(index)
                ? "<null>"
                : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        await WriteLineAsync(string.Join("\t", values));
        rows++;
    }

    if (rows == 0)
    {
        await WriteLineAsync("<no rows>");
    }
}

async Task WriteLineAsync(string line)
{
    Console.WriteLine(line);
    await writer.WriteLineAsync(line);
    await writer.FlushAsync();
}
