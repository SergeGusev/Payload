using System.Data;
using Npgsql;

var outputPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? "outputs/background-task-audit-2026-06-15/result.txt";

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "BackgroundTaskAudit",
    Timeout = 10,
    CommandTimeout = 60
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();

await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await ExecuteNonQueryAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '20s';
SET LOCAL lock_timeout = '500ms';
""");

await WriteLineAsync($"Background task audit captured at {DateTimeOffset.UtcNow:O}");
await WriteScalarAsync("Database", "SELECT current_database();");
await WriteScalarAsync("Server address", "SELECT inet_server_addr()::text;");
await WriteScalarAsync("Server time UTC", "SELECT to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"');");

await WriteSectionAsync("Service heartbeat");
await WriteRowsAsync("""
SELECT
    service_name,
    status,
    mode,
    version,
    to_char(started_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS started_utc,
    to_char(last_heartbeat_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS heartbeat_utc,
    round(extract(epoch FROM (now() - last_heartbeat_utc))::int, 0) AS heartbeat_age_seconds,
    current_loop,
    coalesce(nullif(left(last_error, 220), ''), '<none>') AS last_error
FROM service_heartbeats
ORDER BY last_heartbeat_utc DESC;
""");

await WriteSectionAsync("Current strategy inventory");
await WriteRowsAsync("""
WITH grouped AS (
    SELECT
        CASE
            WHEN code = 'btc_up_down_5m_statistics' THEN 'legacy_statistics'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_middle_' THEN 'middle'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$' THEN 'simple'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_bps_[0-9]+(_instant)?$' THEN 'bps'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_adjusted_diff_[0-9]+(_[0-9]+)?(_revert)?_instant$' THEN 'adjusted_diff'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_[0-9]+(_[0-9]+)?(_revert)?_instant$' THEN 'shift_diff'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_diff_[0-9]+(_[0-9]+)?(_revert)?_instant$' THEN 'regular_diff'
            ELSE 'other'
        END AS strategy_group,
        enabled,
        live_stakes,
        auto_live_paused,
        paused
    FROM strategies
)
SELECT
    strategy_group,
    count(*) AS total,
    count(*) FILTER (WHERE enabled) AS enabled,
    count(*) FILTER (WHERE enabled AND NOT auto_live_paused AND NOT paused) AS enabled_not_paused,
    count(*) FILTER (WHERE live_stakes) AS live_stakes,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused,
    count(*) FILTER (WHERE paused) AS paused
FROM grouped
GROUP BY strategy_group
ORDER BY strategy_group;
""");

await WriteSectionAsync("Live strategies");
await WriteRowsAsync("""
SELECT
    code,
    enabled,
    live_stakes,
    auto_live_paused,
    paused,
    live_stake_amount,
    live_available_balance
FROM strategies
WHERE live_stakes
ORDER BY code;
""");

await WriteSectionAsync("Rows written or updated recently by background candidates");
await WriteRowsAsync("""
SELECT 'BtcOrderBookLagDiagnosticService' AS worker, 'btc_order_book_lag_diagnostic_events' AS table_name,
    count(*) FILTER (WHERE received_at_utc >= now() - interval '1 hour') AS last_1h,
    count(*) FILTER (WHERE received_at_utc >= now() - interval '6 hours') AS last_6h,
    count(*) AS last_24h,
    to_char(max(received_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_utc
FROM btc_order_book_lag_diagnostic_events
WHERE received_at_utc >= now() - interval '24 hours'
UNION ALL
SELECT 'BtcUpDown5mOddsArchiveWorker', 'btc_up_down_5m_odds_ticks',
    count(*) FILTER (WHERE sampled_at_utc >= now() - interval '1 hour'),
    count(*) FILTER (WHERE sampled_at_utc >= now() - interval '6 hours'),
    count(*),
    to_char(max(sampled_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM btc_up_down_5m_odds_ticks
WHERE sampled_at_utc >= now() - interval '24 hours'
UNION ALL
SELECT 'BtcUpDown5mStatisticsWorker', 'btc_up_down_5m_statistics_ticks',
    count(*) FILTER (WHERE sampled_at_utc >= now() - interval '1 hour'),
    count(*) FILTER (WHERE sampled_at_utc >= now() - interval '6 hours'),
    count(*),
    to_char(max(sampled_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM btc_up_down_5m_statistics_ticks
WHERE sampled_at_utc >= now() - interval '24 hours'
UNION ALL
SELECT 'BtcUpDown5mArbitrageScannerWorker', 'btc_up_down_5m_arbitrage_scans',
    count(*) FILTER (WHERE sampled_at_utc >= now() - interval '1 hour'),
    count(*) FILTER (WHERE sampled_at_utc >= now() - interval '6 hours'),
    count(*),
    to_char(max(sampled_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM btc_up_down_5m_arbitrage_scans
WHERE sampled_at_utc >= now() - interval '24 hours'
UNION ALL
SELECT 'CryptoUpDown5mOddsArchiveWorker', 'crypto_up_down_5m_odds_ticks',
    count(*) FILTER (WHERE sampled_at_utc >= now() - interval '1 hour'),
    count(*) FILTER (WHERE sampled_at_utc >= now() - interval '6 hours'),
    count(*),
    to_char(max(sampled_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM crypto_up_down_5m_odds_ticks
WHERE sampled_at_utc >= now() - interval '24 hours'
UNION ALL
SELECT 'CryptoUpDown5mResultPollingWorker', 'crypto_up_down_5m_result_polling_observations',
    count(*) FILTER (WHERE updated_at_utc >= now() - interval '1 hour'),
    count(*) FILTER (WHERE updated_at_utc >= now() - interval '6 hours'),
    count(*),
    to_char(max(updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM crypto_up_down_5m_result_polling_observations
WHERE updated_at_utc >= now() - interval '24 hours'
UNION ALL
SELECT 'BtcUpDown5mDiffCounterPaperStrategyWorker', 'crypto_up_down_5m_diff_snapshots',
    count(*) FILTER (WHERE updated_at_utc >= now() - interval '1 hour'),
    count(*) FILTER (WHERE updated_at_utc >= now() - interval '6 hours'),
    count(*),
    to_char(max(updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM crypto_up_down_5m_diff_snapshots
WHERE updated_at_utc >= now() - interval '24 hours'
UNION ALL
SELECT 'GammaMarketIngestionWorker', 'polymarket_gamma_markets',
    count(*) FILTER (WHERE fetched_at_utc >= now() - interval '1 hour'),
    count(*) FILTER (WHERE fetched_at_utc >= now() - interval '6 hours'),
    count(*),
    to_char(max(fetched_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM polymarket_gamma_markets
WHERE fetched_at_utc >= now() - interval '24 hours'
UNION ALL
SELECT 'BtcUpDown5mPaperStrategyWorker', 'strategy_market_paper_runs',
    count(*) FILTER (WHERE updated_at_utc >= now() - interval '1 hour'),
    count(*) FILTER (WHERE updated_at_utc >= now() - interval '6 hours'),
    count(*),
    to_char(max(updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM strategy_market_paper_runs
WHERE updated_at_utc >= now() - interval '24 hours'
UNION ALL
SELECT 'PaperTradingWorker', 'paper_orders',
    count(*) FILTER (WHERE created_at_utc >= now() - interval '1 hour'),
    count(*) FILTER (WHERE created_at_utc >= now() - interval '6 hours'),
    count(*),
    to_char(max(created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM paper_orders
WHERE created_at_utc >= now() - interval '24 hours'
UNION ALL
SELECT 'LiveTradingMaintenanceWorker', 'live_orders',
    count(*) FILTER (WHERE updated_at_utc >= now() - interval '1 hour'),
    count(*) FILTER (WHERE updated_at_utc >= now() - interval '6 hours'),
    count(*),
    to_char(max(updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM live_orders
WHERE updated_at_utc >= now() - interval '24 hours'
ORDER BY last_24h DESC, worker, table_name;
""");

await WriteSectionAsync("Lag diagnostic event sources");
await WriteRowsAsync("""
SELECT
    source,
    raw_event_type,
    count(*) FILTER (WHERE received_at_utc >= now() - interval '1 hour') AS last_1h,
    count(*) FILTER (WHERE received_at_utc >= now() - interval '6 hours') AS last_6h,
    count(*) FILTER (WHERE received_at_utc >= now() - interval '24 hours') AS last_24h,
    to_char(max(received_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_utc
FROM btc_order_book_lag_diagnostic_events
WHERE received_at_utc >= now() - interval '24 hours'
GROUP BY source, raw_event_type
ORDER BY last_24h DESC, source, raw_event_type;
""");

await WriteSectionAsync("Odds/statistics/arbitrage tables latest rows");
await WriteRowsAsync("""
SELECT 'btc_up_down_5m_odds_ticks' AS table_name, (SELECT reltuples::bigint FROM pg_class WHERE oid = 'btc_up_down_5m_odds_ticks'::regclass) AS estimated_total_rows, to_char(max(sampled_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_utc
FROM btc_up_down_5m_odds_ticks
UNION ALL
SELECT 'btc_up_down_5m_statistics_ticks', (SELECT reltuples::bigint FROM pg_class WHERE oid = 'btc_up_down_5m_statistics_ticks'::regclass), to_char(max(sampled_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM btc_up_down_5m_statistics_ticks
UNION ALL
SELECT 'btc_up_down_5m_arbitrage_scans', (SELECT reltuples::bigint FROM pg_class WHERE oid = 'btc_up_down_5m_arbitrage_scans'::regclass), to_char(max(sampled_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM btc_up_down_5m_arbitrage_scans
UNION ALL
SELECT 'crypto_up_down_5m_odds_ticks', (SELECT reltuples::bigint FROM pg_class WHERE oid = 'crypto_up_down_5m_odds_ticks'::regclass), to_char(max(sampled_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM crypto_up_down_5m_odds_ticks
UNION ALL
SELECT 'btc_order_book_lag_diagnostic_events', (SELECT reltuples::bigint FROM pg_class WHERE oid = 'btc_order_book_lag_diagnostic_events'::regclass), to_char(max(received_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
FROM btc_order_book_lag_diagnostic_events
ORDER BY estimated_total_rows DESC;
""");

await WriteSectionAsync("Legacy statistics strategy state");
await WriteRowsAsync("""
SELECT
    id,
    code,
    name,
    enabled,
    live_stakes,
    auto_live_paused,
    paused,
    updated_at_utc
FROM strategies
WHERE code = 'btc_up_down_5m_statistics';
""");

await tx.CommitAsync();

static string FormatValue(object? value)
{
    return value switch
    {
        null or DBNull => "",
        DateTimeOffset dto => dto.UtcDateTime.ToString("O"),
        DateTime dt => dt.ToUniversalTime().ToString("O"),
        decimal d => d.ToString("0.########"),
        double d => d.ToString("0.########"),
        float f => f.ToString("0.########"),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
    };
}

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

async Task WriteScalarAsync(string label, string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    var value = await command.ExecuteScalarAsync();
    await WriteLineAsync($"{label}: {FormatValue(value)}");
}

async Task WriteRowsAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();
    var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
    await WriteLineAsync(string.Join("\t", names));
    var rowCount = 0;
    while (await reader.ReadAsync())
    {
        rowCount++;
        var values = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            values[i] = FormatValue(reader.GetValue(i));
        }

        await WriteLineAsync(string.Join("\t", values));
    }

    if (rowCount == 0)
    {
        await WriteLineAsync("<no rows>");
    }
}

async Task WriteSectionAsync(string title)
{
    await WriteLineAsync("");
    await WriteLineAsync("== " + title + " ==");
}

async Task WriteLineAsync(string text)
{
    await writer.WriteLineAsync(text);
    await writer.FlushAsync();
}
