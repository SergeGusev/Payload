using System.Data;
using Npgsql;

const string MiddlePattern = "^(btc|eth|sol)_up_down_5m_middle_[0-9]+(_revert)?(_bps_[0-9]+)?(_instant)?$";
const string OldMiddleOnePattern = "^(btc|eth|sol)_up_down_5m_middle_1(_revert)?(_bps_[0-9]+)?(_instant)?$";
const string SkipBpsPattern = "^(btc|eth|sol)_up_down_5m_skip_bps_[0-9]+(_instant)?$";
const string BtcBinanceBpsInstantPattern = "^btc_up_down_5m_binance_bps_[0-9]+_instant$";

var outputPath = args.Length > 0 ? args[0] : "outputs/middle-n-deploy-check-2026-06-15/result.txt";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "MiddleNDeployCheck"
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();

await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await ExecuteNonQueryAsync(connection, transaction, """
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '30s';
SET LOCAL lock_timeout = '500ms';
""");

await WriteLineAsync($"Middle N deploy check started at {DateTimeOffset.UtcNow:O}");
await WriteScalarAsync("Database", "SELECT current_database();");
await WriteScalarAsync("Server address", "SELECT inet_server_addr()::text;");
await WriteScalarAsync("Server time UTC", "SELECT to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"');");

await WriteSectionAsync("Service Heartbeats");
await WriteRowsAsync("""
SELECT
    service_name,
    status,
    mode,
    to_char(started_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS started_utc,
    to_char(last_heartbeat_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS heartbeat_utc,
    round(extract(epoch FROM (now() - last_heartbeat_utc))::numeric, 1) AS heartbeat_age_seconds,
    version,
    current_loop,
    COALESCE(NULLIF(last_error, ''), '<none>') AS last_error
FROM service_heartbeats
ORDER BY last_heartbeat_utc DESC;
""");

await WriteSectionAsync("Middle Strategy Catalog");
await WriteRowsAsync("""
SELECT
    count(*) AS total,
    count(*) FILTER (WHERE enabled) AS enabled,
    count(*) FILTER (WHERE NOT enabled) AS disabled,
    count(*) FILTER (WHERE live_stakes) AS live_stakes,
    to_char(min(created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS min_created_utc,
    to_char(max(updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS max_updated_utc
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

await WriteSectionAsync("Removed Strategy Guards");
await WriteRowsAsync("""
SELECT 'old_middle_1_codes' AS check_name, count(*) AS count
FROM strategies
WHERE code ~ @old_middle_one_pattern
UNION ALL
SELECT 'skip_bps_codes', count(*)
FROM strategies
WHERE code ~ @skip_bps_pattern
UNION ALL
SELECT 'btc_binance_bps_instant_codes', count(*)
FROM strategies
WHERE code ~ @btc_binance_bps_instant_pattern
ORDER BY check_name;
""",
    ("old_middle_one_pattern", OldMiddleOnePattern),
    ("skip_bps_pattern", SkipBpsPattern),
    ("btc_binance_bps_instant_pattern", BtcBinanceBpsInstantPattern));

await WriteSectionAsync("Sample Middle Runs After Cleanup/Deploy");
await WriteRowsAsync("""
WITH latest_heartbeat AS (
    SELECT max(started_at_utc) AS started_at_utc
    FROM service_heartbeats
),
sample_codes(code, n) AS (
    VALUES
        ('btc_up_down_5m_middle_100', 100),
        ('btc_up_down_5m_middle_90', 90),
        ('btc_up_down_5m_middle_80', 80),
        ('btc_up_down_5m_middle_70', 70),
        ('btc_up_down_5m_middle_60', 60),
        ('btc_up_down_5m_middle_50', 50),
        ('btc_up_down_5m_middle_40', 40),
        ('btc_up_down_5m_middle_30', 30),
        ('btc_up_down_5m_middle_20', 20),
        ('btc_up_down_5m_middle_10', 10)
)
SELECT
    sample_codes.n,
    sample_codes.code,
    strategy.enabled,
    count(run.id) AS total_runs_for_sample_strategy,
    count(run.id) FILTER (WHERE run.created_at_utc >= latest_heartbeat.started_at_utc) AS runs_since_service_start,
    to_char(max(run.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_run_created_utc
FROM sample_codes
LEFT JOIN strategies strategy
    ON strategy.code = sample_codes.code
CROSS JOIN latest_heartbeat
LEFT JOIN strategy_market_paper_runs run
    ON run.strategy_id = strategy.id
GROUP BY sample_codes.n, sample_codes.code, strategy.enabled
ORDER BY sample_codes.n DESC;
""");

await WriteRowsAsync("""
WITH latest_heartbeat AS (
    SELECT max(started_at_utc) AS started_at_utc
    FROM service_heartbeats
),
sample_codes(code) AS (
    VALUES
        ('btc_up_down_5m_middle_100'),
        ('btc_up_down_5m_middle_90'),
        ('btc_up_down_5m_middle_80'),
        ('btc_up_down_5m_middle_70'),
        ('btc_up_down_5m_middle_60'),
        ('btc_up_down_5m_middle_50'),
        ('btc_up_down_5m_middle_40'),
        ('btc_up_down_5m_middle_30'),
        ('btc_up_down_5m_middle_20'),
        ('btc_up_down_5m_middle_10')
)
SELECT
    to_char(run.created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc,
    strategy.code,
    run.market_slug,
    run.status,
    COALESCE(run.skip_reason, '<none>') AS skip_reason,
    COALESCE(run.selected_outcome, '<none>') AS selected_outcome
FROM strategy_market_paper_runs run
JOIN strategies strategy
    ON strategy.id = run.strategy_id
JOIN sample_codes
    ON sample_codes.code = strategy.code
CROSS JOIN latest_heartbeat
WHERE run.updated_at_utc >= latest_heartbeat.started_at_utc
ORDER BY run.created_at_utc DESC
LIMIT 30;
""");

await WriteSectionAsync("Recent API Errors");
await WriteRowsAsync("""
SELECT
    component,
    operation,
    count(*) AS count,
    to_char(max(created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_utc
FROM api_errors
WHERE created_at_utc >= now() - interval '30 minutes'
GROUP BY component, operation
ORDER BY latest_utc DESC, count DESC
LIMIT 20;
""");

await transaction.CommitAsync();
await WriteLineAsync($"Middle N deploy check finished at {DateTimeOffset.UtcNow:O}");

async Task ExecuteNonQueryAsync(NpgsqlConnection db, NpgsqlTransaction tx, string sql)
{
    await using var command = new NpgsqlCommand(sql, db, tx);
    await command.ExecuteNonQueryAsync();
}

async Task WriteScalarAsync(string label, string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    var value = await command.ExecuteScalarAsync();
    await WriteLineAsync($"{label}: {value}");
}

async Task WriteRowsAsync(string sql, params (string Name, object Value)[] parameters)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }

    await using var reader = await command.ExecuteReaderAsync();
    if (reader.FieldCount == 0)
    {
        await WriteLineAsync("<no columns>");
        return;
    }

    var columnNames = Enumerable.Range(0, reader.FieldCount)
        .Select(reader.GetName)
        .ToArray();

    await WriteLineAsync(string.Join("\t", columnNames));
    var rowCount = 0;
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

    if (rowCount == 0)
    {
        await WriteLineAsync("<no rows>");
    }
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
