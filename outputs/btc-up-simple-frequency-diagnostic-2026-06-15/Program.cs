using System.Data;
using Npgsql;

const string StrategyCode = "btc_up_down_5m_up_simple";

var outputPath = args.Length > 0 ? args[0] : "outputs/btc-up-simple-frequency-diagnostic-2026-06-15/result.txt";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "BtcUpSimpleFrequencyDiagnostic"
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);

await ExecuteAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '30s';
SET LOCAL lock_timeout = '500ms';
""");

await WriteLineAsync($"BTC Up Simple frequency diagnostic captured at {DateTimeOffset.UtcNow:O}");
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

await WriteLineAsync("");
await WriteLineAsync("## Strategy flags");
await WriteRowsAsync("""
SELECT
    id,
    code,
    enabled,
    live_stakes,
    auto_live_paused,
    paused,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    to_char(live_enabled_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS live_enabled_utc,
    to_char(updated_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS updated_utc
FROM strategies
WHERE code = @strategy_code;
""", ("strategy_code", StrategyCode));

await WriteLineAsync("");
await WriteLineAsync("## Run status by window");
await WriteRowsAsync("""
WITH target AS (
    SELECT id, live_enabled_at_utc
    FROM strategies
    WHERE code = @strategy_code
),
windows(label, starts_at) AS (
    SELECT 'since_live_enabled', live_enabled_at_utc FROM target
    UNION ALL SELECT 'last_24h', now() - interval '24 hours'
    UNION ALL SELECT 'last_12h', now() - interval '12 hours'
    UNION ALL SELECT 'last_6h', now() - interval '6 hours'
    UNION ALL SELECT 'since_service_start', (SELECT max(started_at_utc) FROM service_heartbeats WHERE service_name = 'PolyCopyTrader.Service')
)
SELECT
    windows.label,
    count(run.id) AS runs,
    count(run.id) FILTER (WHERE run.status = 'Observed') AS observed,
    count(run.id) FILTER (WHERE run.status = 'Entered') AS entered,
    count(run.id) FILTER (WHERE run.status = 'Settled') AS settled,
    count(run.id) FILTER (WHERE run.status = 'Skipped') AS skipped,
    to_char(min(run.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS first_run_utc,
    to_char(max(run.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS last_run_utc
FROM windows
CROSS JOIN target
LEFT JOIN strategy_market_paper_runs run
    ON run.strategy_id = target.id
   AND run.created_at_utc >= windows.starts_at
GROUP BY windows.label
ORDER BY CASE windows.label
    WHEN 'since_live_enabled' THEN 1
    WHEN 'last_24h' THEN 2
    WHEN 'last_12h' THEN 3
    WHEN 'last_6h' THEN 4
    ELSE 5
END;
""", ("strategy_code", StrategyCode));

await WriteLineAsync("");
await WriteLineAsync("## Skip reasons since live enabled");
await WriteRowsAsync("""
WITH target AS (
    SELECT id, live_enabled_at_utc
    FROM strategies
    WHERE code = @strategy_code
)
SELECT
    COALESCE(run.skip_reason, '<none>') AS skip_reason,
    count(*) AS count,
    to_char(max(run.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_utc
FROM strategy_market_paper_runs run
JOIN target
    ON target.id = run.strategy_id
WHERE run.created_at_utc >= target.live_enabled_at_utc
  AND run.status = 'Skipped'
GROUP BY COALESCE(run.skip_reason, '<none>')
ORDER BY count DESC, skip_reason;
""", ("strategy_code", StrategyCode));

await WriteLineAsync("");
await WriteLineAsync("## Paper order status since live enabled");
await WriteRowsAsync("""
WITH target AS (
    SELECT id, live_enabled_at_utc
    FROM strategies
    WHERE code = @strategy_code
)
SELECT
    order_row.status,
    count(*) AS count,
    count(*) FILTER (WHERE order_row.price = 0.50) AS price_050,
    count(*) FILTER (WHERE order_row.price < 0.50) AS price_below_050,
    count(*) FILTER (WHERE order_row.raw_decision_json::text LIKE '%instant_resting_at_max_price%true%') AS resting_at_050,
    to_char(max(order_row.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_utc
FROM paper_orders order_row
JOIN target
    ON target.id = order_row.strategy_id
WHERE order_row.created_at_utc >= target.live_enabled_at_utc
GROUP BY order_row.status
ORDER BY count DESC, order_row.status;
""", ("strategy_code", StrategyCode));

await WriteLineAsync("");
await WriteLineAsync("## Live order status since live enabled");
await WriteRowsAsync("""
WITH target AS (
    SELECT id, live_enabled_at_utc
    FROM strategies
    WHERE code = @strategy_code
)
SELECT
    live_order.status,
    live_order.response_status,
    live_order.cancel_status,
    count(*) AS count,
    count(*) FILTER (WHERE live_order.price = 0.50) AS price_050,
    count(*) FILTER (WHERE live_order.price < 0.50) AS price_below_050,
    sum(live_order.filled_size) AS filled_size,
    sum(live_order.remaining_size) AS remaining_size,
    sum(live_order.filled_notional_usd) AS filled_notional_usd,
    to_char(max(live_order.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_utc
FROM live_orders live_order
JOIN target
    ON target.id = live_order.strategy_id
WHERE live_order.created_at_utc >= target.live_enabled_at_utc
GROUP BY live_order.status, live_order.response_status, live_order.cancel_status
ORDER BY count DESC, live_order.status, live_order.response_status, live_order.cancel_status;
""", ("strategy_code", StrategyCode));

await WriteLineAsync("");
await WriteLineAsync("## Live shadow decisions since live enabled");
await WriteRowsAsync("""
WITH target AS (
    SELECT id, live_enabled_at_utc
    FROM strategies
    WHERE code = @strategy_code
)
SELECT
    decision.status,
    count(*) AS count,
    to_char(max(decision.decision_created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_utc
FROM paper_live_shadow_decisions decision
JOIN target
    ON target.id = decision.strategy_id
WHERE decision.decision_created_at_utc >= target.live_enabled_at_utc
GROUP BY decision.status
ORDER BY count DESC, decision.status;
""", ("strategy_code", StrategyCode));

await WriteLineAsync("");
await WriteLineAsync("## Recent runs");
await WriteRowsAsync("""
WITH target AS (
    SELECT id
    FROM strategies
    WHERE code = @strategy_code
)
SELECT
    to_char(run.created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc,
    run.market_slug,
    run.status,
    COALESCE(run.selected_outcome, '<none>') AS selected_outcome,
    COALESCE(run.entry_price::text, '<none>') AS entry_price,
    COALESCE(run.skip_reason, '<none>') AS skip_reason,
    COALESCE(order_row.status, '<none>') AS paper_order_status,
    COALESCE(live_order.status, '<none>') AS live_order_status,
    COALESCE(live_order.response_status, '<none>') AS live_response_status,
    COALESCE(live_order.cancel_status, '<none>') AS live_cancel_status
FROM strategy_market_paper_runs run
JOIN target
    ON target.id = run.strategy_id
LEFT JOIN paper_orders order_row
    ON order_row.id = run.paper_order_id
LEFT JOIN live_orders live_order
    ON live_order.paper_order_id = order_row.id
ORDER BY run.created_at_utc DESC
LIMIT 40;
""", ("strategy_code", StrategyCode));

await WriteLineAsync("");
await WriteLineAsync("## Current observed due backlog");
await WriteRowsAsync("""
SELECT
    CASE
        WHEN strategy.code = @strategy_code THEN 'target_btc_up_simple'
        WHEN strategy.code ~ '^(btc|eth|sol)_up_down_5m_middle_[0-9]+' THEN 'middle_n'
        WHEN strategy.live_stakes THEN 'other_live'
        WHEN strategy.code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'other_up_down_5m'
        ELSE 'other'
    END AS strategy_group,
    count(*) AS observed_due_runs,
    count(*) FILTER (WHERE run.entry_due_at_utc < now() - interval '60 seconds') AS already_expired_by_grace,
    to_char(min(run.entry_due_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS earliest_due_utc,
    to_char(max(run.entry_due_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_due_utc
FROM strategy_market_paper_runs run
JOIN strategies strategy
    ON strategy.id = run.strategy_id
WHERE run.status = 'Observed'
  AND run.entry_due_at_utc <= now()
GROUP BY strategy_group
ORDER BY observed_due_runs DESC, strategy_group;
""", ("strategy_code", StrategyCode));

await WriteLineAsync("");
await WriteLineAsync("## Oldest observed due runs");
await WriteRowsAsync("""
SELECT
    to_char(run.entry_due_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS entry_due_utc,
    strategy.code,
    run.market_slug,
    to_char(run.created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc
FROM strategy_market_paper_runs run
JOIN strategies strategy
    ON strategy.id = run.strategy_id
WHERE run.status = 'Observed'
  AND run.entry_due_at_utc <= now()
ORDER BY run.entry_due_at_utc ASC, strategy.live_stakes DESC, run.created_at_utc ASC
LIMIT 30;
""");

await WriteLineAsync("");
await WriteLineAsync("## Recent paper decision JSON excerpts");
await WriteRowsAsync("""
WITH target AS (
    SELECT id
    FROM strategies
    WHERE code = @strategy_code
)
SELECT
    to_char(order_row.created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc,
    order_row.status,
    order_row.price,
    left(order_row.raw_decision_json::text, 1200) AS raw_decision_excerpt
FROM paper_orders order_row
JOIN target
    ON target.id = order_row.strategy_id
ORDER BY order_row.created_at_utc DESC
LIMIT 6;
""", ("strategy_code", StrategyCode));

await transaction.CommitAsync();

async Task ExecuteAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    await command.ExecuteNonQueryAsync();
}

async Task WriteRowsAsync(string sql, params (string Name, object Value)[] parameters)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }

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
