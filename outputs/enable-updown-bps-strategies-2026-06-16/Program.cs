using System.Data;
using System.Globalization;
using Npgsql;

const string UpDownBpsPattern = "^(btc|eth|sol)_up_down_5m_(up|down)_bps_[0-9]+_instant$";
const int BatchSize = 100;

var execute = args.Any(arg => string.Equals(arg, "--execute", StringComparison.OrdinalIgnoreCase));
var outputPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? (execute
        ? "outputs/enable-updown-bps-strategies-2026-06-16/execute-result.txt"
        : "outputs/enable-updown-bps-strategies-2026-06-16/dry-run-result.txt");

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = execute ? "EnableUpDownBpsStrategiesExecute" : "EnableUpDownBpsStrategiesDryRun",
    Timeout = 10,
    CommandTimeout = 60
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await WriteLineAsync($"Enable Up/Down Bps strategies started at {DateTimeOffset.UtcNow:O}");
await WriteLineAsync($"Mode: {(execute ? "EXECUTE" : "DRY-RUN")}");
await WriteLineAsync($"Host: {builder.Host}");
await WriteLineAsync($"Pattern: {UpDownBpsPattern}");
await WriteLineAsync($"BatchSize: {BatchSize.ToString(CultureInfo.InvariantCulture)}");
await WriteLineAsync("");

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();

await WriteScalarAsync("Database", "SELECT current_database();");
await WriteScalarAsync("Server address", "SELECT inet_server_addr()::text;");
await WriteScalarAsync("Server time UTC", "SELECT to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"');");

await WriteSectionAsync("Service heartbeat before");
await WriteServiceHeartbeatAsync();

await WriteSectionAsync("Target counts before");
await WriteTargetCountsAsync();

await WriteSectionAsync("Target counts by asset/direction before");
await WriteTargetBreakdownAsync();

await WriteSectionAsync("Disabled target sample before");
await WriteDisabledTargetSampleAsync();

var targetCount = await CountDisabledTargetsAsync();
await WriteLineAsync("");
await WriteLineAsync($"Disabled Up/Down Bps strategies targeted: {targetCount.ToString(CultureInfo.InvariantCulture)}");

var updatedTotal = 0;
if (execute)
{
    while (true)
    {
        var updated = await EnableBatchAsync(BatchSize);
        if (updated == 0)
        {
            break;
        }

        updatedTotal += updated;
        await WriteLineAsync($"Updated batch: {updated.ToString(CultureInfo.InvariantCulture)}; total updated: {updatedTotal.ToString(CultureInfo.InvariantCulture)}");
    }
}
else
{
    await WriteLineAsync("Dry-run only; no rows were updated.");
}

await WriteSectionAsync("Target counts after");
await WriteTargetCountsAsync();

await WriteSectionAsync("Target counts by asset/direction after");
await WriteTargetBreakdownAsync();

await WriteSectionAsync("Disabled target sample after");
await WriteDisabledTargetSampleAsync();

await WriteSectionAsync("Enabled strategy groups after");
await WriteRowsAsync("""
SELECT
    CASE
        WHEN code ~ '^(btc|eth|sol)_up_down_5m_middle_[0-9]+' THEN 'middle_n'
        WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$' THEN 'simple'
        WHEN code ~ @up_down_bps_pattern THEN 'up_down_bps'
        WHEN code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'other_up_down_5m'
        ELSE 'other'
    END AS strategy_group,
    count(*) AS enabled_count,
    count(*) FILTER (WHERE live_stakes) AS live_stakes_count,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused_count,
    count(*) FILTER (WHERE paused) AS paused_count
FROM strategies
WHERE enabled
GROUP BY strategy_group
ORDER BY enabled_count DESC, strategy_group;
""", ("up_down_bps_pattern", UpDownBpsPattern));

await WriteSectionAsync("Service heartbeat after");
await WriteServiceHeartbeatAsync();

await WriteLineAsync("");
await WriteLineAsync($"Rows updated: {updatedTotal.ToString(CultureInfo.InvariantCulture)}");
await WriteLineAsync($"Enable Up/Down Bps strategies finished at {DateTimeOffset.UtcNow:O}");

async Task<int> CountDisabledTargetsAsync()
{
    await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
    await ExecuteNonQueryAsync(tx, """
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '15s';
SET LOCAL lock_timeout = '500ms';
""");

    await using var command = new NpgsqlCommand(
        "SELECT count(*) FROM strategies WHERE code ~ @up_down_bps_pattern AND NOT enabled;",
        connection,
        tx);
    command.Parameters.AddWithValue("up_down_bps_pattern", UpDownBpsPattern);
    var count = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    await tx.CommitAsync();
    return count;
}

async Task<int> EnableBatchAsync(int limit)
{
    await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    await ExecuteNonQueryAsync(tx, """
SET LOCAL statement_timeout = '15s';
SET LOCAL lock_timeout = '500ms';
""");

    await using var command = new NpgsqlCommand("""
WITH batch AS (
    SELECT id
    FROM strategies
    WHERE code ~ @up_down_bps_pattern
      AND NOT enabled
    ORDER BY code
    LIMIT @limit
    FOR UPDATE SKIP LOCKED
)
UPDATE strategies strategy
SET enabled = true,
    updated_at_utc = clock_timestamp()
FROM batch
WHERE strategy.id = batch.id;
""", connection, tx);
    command.Parameters.AddWithValue("up_down_bps_pattern", UpDownBpsPattern);
    command.Parameters.AddWithValue("limit", limit);
    var updated = await command.ExecuteNonQueryAsync();
    await tx.CommitAsync();
    return updated;
}

async Task WriteTargetCountsAsync()
{
    await WriteRowsAsync("""
SELECT
    count(*) AS total,
    count(*) FILTER (WHERE enabled) AS enabled,
    count(*) FILTER (WHERE NOT enabled) AS disabled,
    count(*) FILTER (WHERE live_stakes) AS live_stakes,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused,
    count(*) FILTER (WHERE paused) AS paused
FROM strategies
WHERE code ~ @up_down_bps_pattern;
""", ("up_down_bps_pattern", UpDownBpsPattern));
}

async Task WriteTargetBreakdownAsync()
{
    await WriteRowsAsync("""
WITH target AS (
    SELECT
        (regexp_match(code, @up_down_bps_pattern))[1] AS asset,
        (regexp_match(code, @up_down_bps_pattern))[2] AS direction,
        (regexp_match(code, '_bps_([0-9]+)_instant$'))[1]::integer AS threshold,
        enabled,
        live_stakes,
        auto_live_paused,
        paused
    FROM strategies
    WHERE code ~ @up_down_bps_pattern
)
SELECT
    asset,
    direction,
    count(*) AS total,
    count(*) FILTER (WHERE enabled) AS enabled,
    count(*) FILTER (WHERE NOT enabled) AS disabled,
    count(*) FILTER (WHERE live_stakes) AS live_stakes,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused,
    count(*) FILTER (WHERE paused) AS paused,
    min(threshold) AS min_threshold,
    max(threshold) AS max_threshold
FROM target
GROUP BY asset, direction
ORDER BY asset, direction;
""", ("up_down_bps_pattern", UpDownBpsPattern));
}

async Task WriteDisabledTargetSampleAsync()
{
    await WriteRowsAsync("""
SELECT code, name, live_stakes, auto_live_paused, paused
FROM strategies
WHERE code ~ @up_down_bps_pattern
  AND NOT enabled
ORDER BY code
LIMIT 30;
""", ("up_down_bps_pattern", UpDownBpsPattern));
}

async Task WriteServiceHeartbeatAsync()
{
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
}

async Task ExecuteNonQueryAsync(NpgsqlTransaction tx, string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    await command.ExecuteNonQueryAsync();
}

async Task WriteScalarAsync(string label, string sql)
{
    await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
    await ExecuteNonQueryAsync(tx, """
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '15s';
SET LOCAL lock_timeout = '500ms';
""");

    await using var command = new NpgsqlCommand(sql, connection, tx);
    var value = await command.ExecuteScalarAsync();
    await tx.CommitAsync();
    await WriteLineAsync($"{label}: {value}");
}

async Task WriteRowsAsync(string sql, params (string Name, object Value)[] parameters)
{
    await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
    await ExecuteNonQueryAsync(tx, """
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '30s';
SET LOCAL lock_timeout = '500ms';
""");

    await using var command = new NpgsqlCommand(sql, connection, tx);
    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }

    var rowCount = 0;
    await using (var reader = await command.ExecuteReaderAsync())
    {
        var columnNames = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();
        await WriteLineAsync(string.Join('\t', columnNames));

        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
            {
                values[index] = reader.IsDBNull(index)
                    ? "<null>"
                    : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;
            }

            await WriteLineAsync(string.Join('\t', values));
            rowCount++;
        }
    }

    if (rowCount == 0)
    {
        await WriteLineAsync("<no rows>");
    }

    await tx.CommitAsync();
}

async Task WriteSectionAsync(string title)
{
    await WriteLineAsync("");
    await WriteLineAsync($"## {title}");
}

async Task WriteLineAsync(string line)
{
    Console.WriteLine(line);
    await writer.WriteLineAsync(line);
    await writer.FlushAsync();
}
