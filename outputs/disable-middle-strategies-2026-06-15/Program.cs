using System.Data;
using Npgsql;

const string MiddlePattern = "^(btc|eth|sol)_up_down_5m_middle_[0-9]+(_revert)?(_bps_[0-9]+)?(_instant)?$";
const int BatchSize = 500;

var execute = args.Any(arg => string.Equals(arg, "--execute", StringComparison.OrdinalIgnoreCase));
var outputPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? (execute
        ? "outputs/disable-middle-strategies-2026-06-15/execute-result.txt"
        : "outputs/disable-middle-strategies-2026-06-15/dry-run-result.txt");

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = execute ? "DisableMiddleStrategiesExecute" : "DisableMiddleStrategiesDryRun"
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await WriteLineAsync($"Disable Middle strategies started at {DateTimeOffset.UtcNow:O}");
await WriteLineAsync($"Mode: {(execute ? "EXECUTE" : "DRY-RUN")}");
await WriteLineAsync($"Host: {builder.Host}");
await WriteLineAsync($"Pattern: {MiddlePattern}");
await WriteLineAsync("");

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();

await WriteScalarAsync("Database", "SELECT current_database();");
await WriteScalarAsync("Server address", "SELECT inet_server_addr()::text;");
await WriteScalarAsync("Server time UTC", "SELECT to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"');");

await WriteSectionAsync("Service heartbeat before");
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

await WriteSectionAsync("Middle strategy counts before");
await WriteMiddleCountsAsync();

var targetCount = await CountEnabledMiddleAsync();
await WriteLineAsync("");
await WriteLineAsync($"Enabled Middle strategies targeted: {targetCount}");

var updatedTotal = 0;
if (execute)
{
    while (true)
    {
        var updated = await DisableBatchAsync(BatchSize);
        if (updated == 0)
        {
            break;
        }

        updatedTotal += updated;
        await WriteLineAsync($"Updated batch: {updated}; total updated: {updatedTotal}");
    }
}
else
{
    await WriteLineAsync("Dry-run only; no rows were updated.");
}

await WriteSectionAsync("Middle strategy counts after");
await WriteMiddleCountsAsync();

await WriteSectionAsync("Enabled strategy groups after");
await WriteRowsAsync("""
SELECT
    CASE
        WHEN code ~ @middle_pattern THEN 'middle_n'
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
""", ("middle_pattern", MiddlePattern));

await WriteSectionAsync("Service heartbeat after");
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

await WriteLineAsync("");
await WriteLineAsync($"Rows updated: {updatedTotal}");
await WriteLineAsync($"Disable Middle strategies finished at {DateTimeOffset.UtcNow:O}");

async Task<int> CountEnabledMiddleAsync()
{
    await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
    await ExecuteNonQueryAsync(tx, """
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '15s';
SET LOCAL lock_timeout = '500ms';
""");

    await using var command = new NpgsqlCommand(
        "SELECT count(*) FROM strategies WHERE code ~ @middle_pattern AND enabled;",
        connection,
        tx);
    command.Parameters.AddWithValue("middle_pattern", MiddlePattern);
    var count = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    await tx.CommitAsync();
    return count;
}

async Task<int> DisableBatchAsync(int limit)
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
    WHERE code ~ @middle_pattern
      AND enabled
    ORDER BY code
    LIMIT @limit
    FOR UPDATE SKIP LOCKED
)
UPDATE strategies strategy
SET enabled = false,
    updated_at_utc = clock_timestamp()
FROM batch
WHERE strategy.id = batch.id;
""", connection, tx);
    command.Parameters.AddWithValue("middle_pattern", MiddlePattern);
    command.Parameters.AddWithValue("limit", limit);
    var updated = await command.ExecuteNonQueryAsync();
    await tx.CommitAsync();
    return updated;
}

async Task WriteMiddleCountsAsync()
{
    await WriteRowsAsync("""
SELECT
    count(*) AS total,
    count(*) FILTER (WHERE enabled) AS enabled,
    count(*) FILTER (WHERE NOT enabled) AS disabled,
    count(*) FILTER (WHERE live_stakes) AS live_stakes,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused
FROM strategies
WHERE code ~ @middle_pattern;
""", ("middle_pattern", MiddlePattern));

    await WriteRowsAsync("""
WITH middle AS (
    SELECT
        (regexp_match(code, '_middle_([0-9]+)'))[1]::integer AS n,
        enabled
    FROM strategies
    WHERE code ~ @middle_pattern
)
SELECT
    n,
    count(*) AS total,
    count(*) FILTER (WHERE enabled) AS enabled,
    count(*) FILTER (WHERE NOT enabled) AS disabled
FROM middle
GROUP BY n
ORDER BY n DESC;
""", ("middle_pattern", MiddlePattern));
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
    {
        await using var reader = await command.ExecuteReaderAsync();
        var columnNames = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();
        await WriteLineAsync(string.Join("\t", columnNames));

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
