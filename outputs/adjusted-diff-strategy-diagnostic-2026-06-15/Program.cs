using System.Data;
using Npgsql;
using NpgsqlTypes;

const string StrategyCode = "btc_up_down_5m_down_adjusted_diff_1_instant";

var outputPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? "outputs/adjusted-diff-strategy-diagnostic-2026-06-15/result.txt";

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "AdjustedDiffStrategyDiagnostic",
    Timeout = 10,
    CommandTimeout = 30
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

await WriteLineAsync($"AdjustedDiff strategy diagnostic captured at {DateTimeOffset.UtcNow:O}");
await WriteLineAsync($"StrategyCode: {StrategyCode}");
await WriteScalarAsync("Database", "SELECT current_database();");
await WriteScalarAsync("Server address", "SELECT inet_server_addr()::text;");
await WriteScalarAsync("Server time UTC", "SELECT to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"');");

await WriteSectionAsync("Strategy row");
await WriteRowsAsync("""
SELECT
    code,
    name,
    enabled,
    live_stakes,
    auto_live_paused,
    paused,
    paper_stake_amount,
    created_at_utc,
    updated_at_utc,
    description
FROM strategies
WHERE code = @strategy_code;
""", ("strategy_code", StrategyCode));

await WriteSectionAsync("Run summary");
await WriteRowsAsync("""
WITH target_strategy AS MATERIALIZED (
    SELECT id FROM strategies WHERE code = @strategy_code
),
runs AS MATERIALIZED (
    SELECT run.*
    FROM strategy_market_paper_runs run
    WHERE run.strategy_id IN (SELECT id FROM target_strategy)
)
SELECT
    'all_time' AS window_name,
    count(*) AS runs,
    count(DISTINCT market_start_utc) AS markets,
    count(*) FILTER (WHERE status = 'Observed') AS observed,
    count(*) FILTER (WHERE status = 'Entered') AS entered,
    count(*) FILTER (WHERE status = 'Settled') AS settled,
    count(*) FILTER (WHERE status = 'Skipped') AS skipped,
    count(*) FILTER (WHERE status = 'Skipped' AND paper_order_id IS NULL) AS paper_condition_skipped,
    count(*) FILTER (WHERE status = 'Skipped' AND paper_order_id IS NOT NULL) AS paper_not_accepted,
    count(*) FILTER (WHERE paper_order_id IS NOT NULL) AS paper_orders_linked,
    min(created_at_utc) AS first_run_utc,
    max(updated_at_utc) AS latest_run_update_utc
FROM runs
UNION ALL
SELECT
    'last_24h' AS window_name,
    count(*) AS runs,
    count(DISTINCT market_start_utc) AS markets,
    count(*) FILTER (WHERE status = 'Observed') AS observed,
    count(*) FILTER (WHERE status = 'Entered') AS entered,
    count(*) FILTER (WHERE status = 'Settled') AS settled,
    count(*) FILTER (WHERE status = 'Skipped') AS skipped,
    count(*) FILTER (WHERE status = 'Skipped' AND paper_order_id IS NULL) AS paper_condition_skipped,
    count(*) FILTER (WHERE status = 'Skipped' AND paper_order_id IS NOT NULL) AS paper_not_accepted,
    count(*) FILTER (WHERE paper_order_id IS NOT NULL) AS paper_orders_linked,
    min(created_at_utc) AS first_run_utc,
    max(updated_at_utc) AS latest_run_update_utc
FROM runs
WHERE updated_at_utc >= now() - interval '24 hours'
UNION ALL
SELECT
    'last_6h' AS window_name,
    count(*) AS runs,
    count(DISTINCT market_start_utc) AS markets,
    count(*) FILTER (WHERE status = 'Observed') AS observed,
    count(*) FILTER (WHERE status = 'Entered') AS entered,
    count(*) FILTER (WHERE status = 'Settled') AS settled,
    count(*) FILTER (WHERE status = 'Skipped') AS skipped,
    count(*) FILTER (WHERE status = 'Skipped' AND paper_order_id IS NULL) AS paper_condition_skipped,
    count(*) FILTER (WHERE status = 'Skipped' AND paper_order_id IS NOT NULL) AS paper_not_accepted,
    count(*) FILTER (WHERE paper_order_id IS NOT NULL) AS paper_orders_linked,
    min(created_at_utc) AS first_run_utc,
    max(updated_at_utc) AS latest_run_update_utc
FROM runs
WHERE updated_at_utc >= now() - interval '6 hours';
""", ("strategy_code", StrategyCode));

await WriteSectionAsync("Skip reasons");
await WriteRowsAsync("""
WITH target_strategy AS MATERIALIZED (
    SELECT id FROM strategies WHERE code = @strategy_code
)
SELECT
    CASE
        WHEN run.updated_at_utc >= now() - interval '24 hours' THEN 'last_24h'
        ELSE 'older'
    END AS age_bucket,
    COALESCE(run.skip_reason, '<none>') AS skip_reason,
    CASE WHEN run.paper_order_id IS NULL THEN 'condition_skip_no_order' ELSE 'not_accepted_order_linked' END AS dashboard_bucket,
    count(*) AS runs,
    min(run.market_start_utc) AS first_market_start_utc,
    max(run.market_start_utc) AS latest_market_start_utc,
    max(run.updated_at_utc) AS latest_update_utc
FROM strategy_market_paper_runs run
WHERE run.strategy_id IN (SELECT id FROM target_strategy)
  AND run.status = 'Skipped'
GROUP BY age_bucket, COALESCE(run.skip_reason, '<none>'), dashboard_bucket
ORDER BY age_bucket DESC, runs DESC, skip_reason, dashboard_bucket;
""", ("strategy_code", StrategyCode));

await WriteSectionAsync("Threshold skip diagnostics");
await WriteRowsAsync("""
WITH target_strategy AS MATERIALIZED (
    SELECT id FROM strategies WHERE code = @strategy_code
),
threshold_skips AS MATERIALIZED (
    SELECT
        run.updated_at_utc,
        nullif(run.skip_diagnostics_json ->> 'threshold', '')::numeric AS threshold_value,
        nullif(run.skip_diagnostics_json ->> 'effective_diff', '')::numeric AS effective_diff,
        nullif(run.skip_diagnostics_json ->> 'raw_diff', '')::numeric AS raw_diff,
        nullif(run.skip_diagnostics_json ->> 'trend_zero', '')::numeric AS trend_zero,
        nullif(run.skip_diagnostics_json ->> 'adjusted_diff', '')::numeric AS adjusted_diff,
        run.skip_diagnostics_json ->> 'trigger_side' AS trigger_side,
        run.skip_diagnostics_json ->> 'diff_counter_trigger_outcome' AS trigger_outcome,
        run.skip_diagnostics_json ->> 'counter_target_market_result_received' AS target_received,
        run.skip_diagnostics_json ->> 'counter_initialized' AS counter_initialized
    FROM strategy_market_paper_runs run
    WHERE run.strategy_id IN (SELECT id FROM target_strategy)
      AND run.status = 'Skipped'
      AND run.paper_order_id IS NULL
      AND run.skip_reason = 'diff_counter_threshold_not_reached'
)
SELECT
    CASE
        WHEN updated_at_utc >= now() - interval '24 hours' THEN 'last_24h'
        ELSE 'older'
    END AS age_bucket,
    count(*) AS skips,
    min(threshold_value) AS min_threshold,
    max(threshold_value) AS max_threshold,
    min(effective_diff) AS min_effective_diff,
    max(effective_diff) AS max_effective_diff,
    avg(effective_diff) AS avg_effective_diff,
    count(*) FILTER (WHERE effective_diff < 1) AS effective_lt_1,
    count(*) FILTER (WHERE effective_diff >= 1) AS effective_gte_1,
    min(raw_diff) AS min_raw_diff,
    max(raw_diff) AS max_raw_diff,
    avg(raw_diff) AS avg_raw_diff,
    min(trend_zero) AS min_trend_zero,
    max(trend_zero) AS max_trend_zero,
    avg(trend_zero) AS avg_trend_zero,
    min(adjusted_diff) AS min_adjusted_diff,
    max(adjusted_diff) AS max_adjusted_diff,
    avg(adjusted_diff) AS avg_adjusted_diff,
    count(*) FILTER (WHERE trigger_side = 'Down') AS trigger_side_down,
    count(*) FILTER (WHERE trigger_outcome = 'Down') AS trigger_outcome_down,
    count(*) FILTER (WHERE target_received = 'true') AS target_received_true,
    count(*) FILTER (WHERE target_received = 'false') AS target_received_false,
    count(*) FILTER (WHERE counter_initialized = 'true') AS counter_initialized_true,
    count(*) FILTER (WHERE counter_initialized = 'false') AS counter_initialized_false
FROM threshold_skips
GROUP BY age_bucket
ORDER BY age_bucket DESC;
""", ("strategy_code", StrategyCode));

await WriteSectionAsync("Latest condition skips");
await WriteRowsAsync("""
WITH target_strategy AS MATERIALIZED (
    SELECT id FROM strategies WHERE code = @strategy_code
)
SELECT
    run.market_start_utc,
    run.updated_at_utc,
    run.skip_reason,
    run.skip_diagnostics_json ->> 'counter_mode' AS counter_mode,
    run.skip_diagnostics_json ->> 'trigger_side' AS trigger_side,
    run.skip_diagnostics_json ->> 'threshold' AS threshold,
    run.skip_diagnostics_json ->> 'raw_diff' AS raw_diff,
    run.skip_diagnostics_json ->> 'trend_zero' AS trend_zero,
    run.skip_diagnostics_json ->> 'adjusted_diff' AS adjusted_diff,
    run.skip_diagnostics_json ->> 'effective_diff' AS effective_diff,
    run.skip_diagnostics_json ->> 'up_count' AS up_count,
    run.skip_diagnostics_json ->> 'down_count' AS down_count,
    run.skip_diagnostics_json ->> 'counter_target_market_result_received' AS target_received
FROM strategy_market_paper_runs run
WHERE run.strategy_id IN (SELECT id FROM target_strategy)
  AND run.status = 'Skipped'
  AND run.paper_order_id IS NULL
ORDER BY run.updated_at_utc DESC
LIMIT 30;
""", ("strategy_code", StrategyCode));

await WriteSectionAsync("Linked paper order summary");
await WriteRowsAsync("""
WITH target_strategy AS MATERIALIZED (
    SELECT id FROM strategies WHERE code = @strategy_code
),
linked AS MATERIALIZED (
    SELECT
        run.status AS run_status,
        run.skip_reason,
        run.updated_at_utc,
        order_row.status AS order_status,
        order_row.price,
        order_row.notional_usd,
        order_row.created_at_utc,
        order_row.expires_at_utc,
        order_row.filled_at_utc,
        order_row.raw_decision_json,
        nullif(order_row.raw_decision_json ->> 'instant_raw_limit_price', '')::numeric AS raw_limit_price,
        nullif(order_row.raw_decision_json ->> 'instant_limit_price', '')::numeric AS instant_limit_price,
        nullif(order_row.raw_decision_json ->> 'instant_max_buy_price', '')::numeric AS instant_max_buy_price,
        nullif(order_row.raw_decision_json ->> 'instant_best_ask', '')::numeric AS instant_best_ask,
        nullif(order_row.raw_decision_json ->> 'instant_executable_ask_shares', '')::numeric AS executable_ask_shares,
        nullif(order_row.raw_decision_json ->> 'instant_target_size_shares', '')::numeric AS target_size_shares,
        nullif(order_row.raw_decision_json ->> 'instant_executable_ask_vwap', '')::numeric AS executable_ask_vwap,
        order_row.raw_decision_json ->> 'instant_resting_at_max_price' AS resting_at_max
    FROM strategy_market_paper_runs run
    JOIN paper_orders order_row ON order_row.id = run.paper_order_id
    WHERE run.strategy_id IN (SELECT id FROM target_strategy)
)
SELECT
    CASE
        WHEN updated_at_utc >= now() - interval '24 hours' THEN 'last_24h'
        ELSE 'older'
    END AS age_bucket,
    run_status,
    COALESCE(skip_reason, '<none>') AS skip_reason,
    order_status,
    resting_at_max,
    count(*) AS orders,
    min(created_at_utc) AS first_order_utc,
    max(created_at_utc) AS latest_order_utc,
    min(price) AS min_order_price,
    max(price) AS max_order_price,
    avg(price) AS avg_order_price,
    min(raw_limit_price) AS min_raw_limit_price,
    max(raw_limit_price) AS max_raw_limit_price,
    avg(raw_limit_price) AS avg_raw_limit_price,
    min(instant_limit_price) AS min_instant_limit_price,
    max(instant_limit_price) AS max_instant_limit_price,
    min(instant_best_ask) AS min_best_ask,
    max(instant_best_ask) AS max_best_ask,
    avg(instant_best_ask) AS avg_best_ask,
    min(executable_ask_shares) AS min_executable_ask_shares,
    max(executable_ask_shares) AS max_executable_ask_shares,
    avg(executable_ask_shares) AS avg_executable_ask_shares,
    min(target_size_shares) AS min_target_size_shares,
    max(target_size_shares) AS max_target_size_shares,
    avg(target_size_shares) AS avg_target_size_shares
FROM linked
GROUP BY age_bucket, run_status, COALESCE(skip_reason, '<none>'), order_status, resting_at_max
ORDER BY age_bucket DESC, orders DESC, run_status, skip_reason, order_status, resting_at_max;
""", ("strategy_code", StrategyCode));

await WriteSectionAsync("Latest linked paper orders");
await WriteRowsAsync("""
WITH target_strategy AS MATERIALIZED (
    SELECT id FROM strategies WHERE code = @strategy_code
)
SELECT
    run.market_start_utc,
    run.status AS run_status,
    COALESCE(run.skip_reason, '<none>') AS skip_reason,
    order_row.status AS order_status,
    order_row.price,
    order_row.notional_usd,
    order_row.created_at_utc,
    order_row.expires_at_utc,
    order_row.filled_at_utc,
    order_row.raw_decision_json ->> 'instant_resting_at_max_price' AS resting_at_max,
    order_row.raw_decision_json ->> 'instant_raw_limit_price' AS raw_limit_price,
    order_row.raw_decision_json ->> 'instant_limit_price' AS instant_limit_price,
    order_row.raw_decision_json ->> 'instant_best_ask' AS best_ask,
    order_row.raw_decision_json ->> 'instant_executable_ask_shares' AS executable_ask_shares,
    order_row.raw_decision_json ->> 'instant_target_size_shares' AS target_size_shares,
    order_row.raw_decision_json ->> 'instant_executable_ask_vwap' AS executable_ask_vwap,
    order_row.raw_decision_json ->> 'effective_diff' AS effective_diff,
    order_row.raw_decision_json ->> 'raw_diff' AS raw_diff,
    order_row.raw_decision_json ->> 'trend_zero' AS trend_zero,
    order_row.raw_decision_json ->> 'adjusted_diff' AS adjusted_diff
FROM strategy_market_paper_runs run
JOIN paper_orders order_row ON order_row.id = run.paper_order_id
WHERE run.strategy_id IN (SELECT id FROM target_strategy)
ORDER BY order_row.created_at_utc DESC
LIMIT 40;
""", ("strategy_code", StrategyCode));

await tx.CommitAsync();
await WriteLineAsync("");
await WriteLineAsync($"AdjustedDiff strategy diagnostic finished at {DateTimeOffset.UtcNow:O}");

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    await command.ExecuteNonQueryAsync();
}

async Task WriteScalarAsync(string label, string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    var value = await command.ExecuteScalarAsync();
    await WriteLineAsync($"{label}: {value}");
}

async Task WriteRowsAsync(string sql, params (string Name, object Value)[] parameters)
{
    await using var command = new NpgsqlCommand(sql, connection, tx)
    {
        CommandTimeout = 30
    };

    foreach (var parameter in parameters)
    {
        var dbType = parameter.Value is DateTime ? NpgsqlDbType.TimestampTz : NpgsqlDbType.Text;
        command.Parameters.Add(parameter.Name, dbType).Value = parameter.Value;
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
