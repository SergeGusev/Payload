using System.Data;
using Npgsql;
using NpgsqlTypes;

var outputPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? "outputs/diff-paper-activity-diagnostic-2026-06-15/result.txt";

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "DiffPaperActivityDiagnostic",
    Timeout = 10,
    CommandTimeout = 60
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();

await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
var savepointIndex = 0;
await ExecuteNonQueryAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '20s';
SET LOCAL lock_timeout = '500ms';
""");

await WriteLineAsync($"Diff Paper activity diagnostic captured at {DateTimeOffset.UtcNow:O}");
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
    coalesce(nullif(left(last_error, 240), ''), '<none>') AS last_error
FROM service_heartbeats
ORDER BY last_heartbeat_utc DESC;
""");

await WriteSectionAsync("Diff strategy inventory");
await WriteRowsAsync("""
WITH diff_strategies AS MATERIALIZED (
    SELECT
        id,
        code,
        enabled,
        live_stakes,
        auto_live_paused,
        paused,
        paper_stake_amount,
        CASE
            WHEN code LIKE '%_shift_diff_%' THEN 'shift_diff'
            WHEN code LIKE '%_adjusted_diff_%' THEN 'adjusted_diff'
            WHEN code LIKE '%_diff_%' THEN 'regular_diff'
            ELSE 'other'
        END AS diff_type,
        CASE WHEN code LIKE '%_revert_instant' THEN true ELSE false END AS is_revert,
        upper(split_part(code, '_', 1)) AS asset
    FROM strategies
    WHERE code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_(diff|adjusted_diff|shift_diff)_[0-9]+(_[0-9]+)?(_revert)?_instant$'
)
SELECT
    diff_type,
    asset,
    is_revert,
    count(*) AS strategies,
    count(*) FILTER (WHERE enabled) AS enabled,
    count(*) FILTER (WHERE live_stakes) AS live_stakes,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused,
    count(*) FILTER (WHERE paused) AS paused,
    min(paper_stake_amount) AS min_paper_stake,
    max(paper_stake_amount) AS max_paper_stake
FROM diff_strategies
GROUP BY diff_type, asset, is_revert
ORDER BY diff_type, asset, is_revert;
""");

await WriteSectionAsync("Diff paper orders by UTC day");
await WriteRowsAsync("""
WITH diff_strategies AS MATERIALIZED (
    SELECT
        id,
        CASE
            WHEN code LIKE '%_shift_diff_%' THEN 'shift_diff'
            WHEN code LIKE '%_adjusted_diff_%' THEN 'adjusted_diff'
            ELSE 'regular_diff'
        END AS diff_type
    FROM strategies
    WHERE code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_(diff|adjusted_diff|shift_diff)_[0-9]+(_[0-9]+)?(_revert)?_instant$'
)
SELECT
    to_char(date_trunc('day', order_row.created_at_utc AT TIME ZONE 'UTC'), 'YYYY-MM-DD') AS utc_day,
    count(*) AS orders,
    count(DISTINCT order_row.strategy_id) AS strategies_with_orders,
    count(*) FILTER (WHERE order_row.status = 'Filled') AS filled,
    count(*) FILTER (WHERE order_row.status = 'PartiallyFilled') AS partially_filled,
    count(*) FILTER (WHERE order_row.status = 'Expired') AS expired,
    count(*) FILTER (WHERE order_row.status = 'Pending') AS pending,
    sum(order_row.notional_usd) AS notional_usd,
    min(order_row.created_at_utc) AS first_order_utc,
    max(order_row.created_at_utc) AS last_order_utc
FROM paper_orders order_row
JOIN diff_strategies strategy ON strategy.id = order_row.strategy_id
WHERE order_row.created_at_utc >= date_trunc('day', (now() AT TIME ZONE 'UTC')) - interval '10 days'
GROUP BY date_trunc('day', order_row.created_at_utc AT TIME ZONE 'UTC')
ORDER BY utc_day;
""");

await WriteSectionAsync("Diff paper orders by UTC day and type");
await WriteRowsAsync("""
WITH diff_strategies AS MATERIALIZED (
    SELECT
        id,
        CASE
            WHEN code LIKE '%_shift_diff_%' THEN 'shift_diff'
            WHEN code LIKE '%_adjusted_diff_%' THEN 'adjusted_diff'
            ELSE 'regular_diff'
        END AS diff_type
    FROM strategies
    WHERE code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_(diff|adjusted_diff|shift_diff)_[0-9]+(_[0-9]+)?(_revert)?_instant$'
)
SELECT
    to_char(date_trunc('day', order_row.created_at_utc AT TIME ZONE 'UTC'), 'YYYY-MM-DD') AS utc_day,
    strategy.diff_type,
    count(*) AS orders,
    count(DISTINCT order_row.strategy_id) AS strategies_with_orders,
    count(*) FILTER (WHERE order_row.status = 'Filled') AS filled,
    count(*) FILTER (WHERE order_row.status = 'Expired') AS expired,
    count(*) FILTER (WHERE order_row.status = 'Pending') AS pending,
    sum(order_row.notional_usd) AS notional_usd,
    max(order_row.created_at_utc) AS last_order_utc
FROM paper_orders order_row
JOIN diff_strategies strategy ON strategy.id = order_row.strategy_id
WHERE order_row.created_at_utc >= date_trunc('day', (now() AT TIME ZONE 'UTC')) - interval '10 days'
GROUP BY date_trunc('day', order_row.created_at_utc AT TIME ZONE 'UTC'), strategy.diff_type
ORDER BY utc_day, strategy.diff_type;
""");

await WriteSectionAsync("Last 72h versus previous 72h paper orders");
await WriteRowsAsync("""
WITH diff_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_(diff|adjusted_diff|shift_diff)_[0-9]+(_[0-9]+)?(_revert)?_instant$'
),
orders AS MATERIALIZED (
    SELECT *
    FROM paper_orders order_row
    WHERE order_row.strategy_id IN (SELECT id FROM diff_strategies)
      AND order_row.created_at_utc >= now() - interval '144 hours'
)
SELECT
    CASE
        WHEN created_at_utc >= now() - interval '72 hours' THEN 'last_72h'
        ELSE 'previous_72h'
    END AS period_name,
    count(*) AS orders,
    count(DISTINCT strategy_id) AS strategies_with_orders,
    count(*) FILTER (WHERE status = 'Filled') AS filled,
    count(*) FILTER (WHERE status = 'Expired') AS expired,
    count(*) FILTER (WHERE status = 'Pending') AS pending,
    sum(notional_usd) AS notional_usd,
    min(created_at_utc) AS first_order_utc,
    max(created_at_utc) AS last_order_utc
FROM orders
GROUP BY period_name
ORDER BY period_name;
""");

await WriteSectionAsync("Diff snapshots by UTC day and asset");
await WriteRowsAsync("""
SELECT
    to_char(date_trunc('day', sampled_at_utc AT TIME ZONE 'UTC'), 'YYYY-MM-DD') AS utc_day,
    asset_symbol,
    count(*) AS snapshots,
    count(DISTINCT market_start_utc) AS distinct_markets,
    max(sampled_at_utc) AS latest_sampled_utc,
    max(last_included_market_start_utc) AS latest_included_market_utc,
    count(*) FILTER (WHERE counter_initialized) AS initialized,
    count(*) FILTER (WHERE history_fetch_failed_at_utc IS NOT NULL) AS history_fetch_failed
FROM crypto_up_down_5m_diff_snapshots
WHERE sampled_at_utc >= date_trunc('day', (now() AT TIME ZONE 'UTC')) - interval '10 days'
GROUP BY date_trunc('day', sampled_at_utc AT TIME ZONE 'UTC'), asset_symbol
ORDER BY utc_day, asset_symbol;
""");

await WriteSectionAsync("Result polling observations by market UTC day and asset");
await WriteRowsAsync("""
SELECT
    to_char(date_trunc('day', market_start_utc AT TIME ZONE 'UTC'), 'YYYY-MM-DD') AS utc_day,
    asset_symbol,
    status,
    count(*) AS observations,
    count(*) FILTER (WHERE winning_outcome IS NOT NULL) AS with_winner,
    max(updated_at_utc) AS latest_updated_utc,
    max(first_winner_at_utc) AS latest_winner_utc
FROM crypto_up_down_5m_result_polling_observations
WHERE market_start_utc >= date_trunc('day', (now() AT TIME ZONE 'UTC')) - interval '10 days'
GROUP BY date_trunc('day', market_start_utc AT TIME ZONE 'UTC'), asset_symbol, status
ORDER BY utc_day, asset_symbol, status;
""");

await WriteSectionAsync("Active Diff observed due backlog");
await WriteRowsAsync("""
WITH diff_strategies AS MATERIALIZED (
    SELECT
        id,
        code,
        enabled,
        live_stakes,
        CASE
            WHEN code LIKE '%_shift_diff_%' THEN 'shift_diff'
            WHEN code LIKE '%_adjusted_diff_%' THEN 'adjusted_diff'
            ELSE 'regular_diff'
        END AS diff_type
    FROM strategies
    WHERE code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_(diff|adjusted_diff|shift_diff)_[0-9]+(_[0-9]+)?(_revert)?_instant$'
)
SELECT
    strategy.diff_type,
    strategy.enabled,
    strategy.live_stakes,
    count(*) AS observed_due_runs,
    count(*) FILTER (WHERE run.entry_due_at_utc < now() - interval '60 seconds') AS already_expired_by_60s,
    min(run.entry_due_at_utc) AS earliest_due_utc,
    max(run.entry_due_at_utc) AS latest_due_utc
FROM strategy_market_paper_runs run
JOIN diff_strategies strategy ON strategy.id = run.strategy_id
WHERE run.status = 'Observed'
  AND run.entry_due_at_utc <= now()
GROUP BY strategy.diff_type, strategy.enabled, strategy.live_stakes
ORDER BY observed_due_runs DESC, strategy.diff_type;
""");

await WriteSectionAsync("Recent Diff worker and result errors");
await WriteRowsAsync("""
SELECT
    component,
    operation,
    count(*) AS errors,
    min(created_at_utc) AS first_error_utc,
    max(created_at_utc) AS latest_error_utc,
    left(max(message), 240) AS sample_message
FROM api_errors
WHERE created_at_utc >= now() - interval '72 hours'
  AND (
      component ILIKE '%Diff%'
      OR operation ILIKE '%Diff%'
      OR message ILIKE '%diff_counter%'
      OR component IN ('BtcUpDown5mDiffCounterPaperStrategyWorker')
      OR operation IN ('GetDiffCounterWebSocketResults', 'UpsertDiffCounterSnapshot')
      OR component ILIKE '%MarketWebSocket%'
  )
GROUP BY component, operation
ORDER BY latest_error_utc DESC, errors DESC
LIMIT 50;
""");

await WriteSectionAsync("Latest Diff paper orders");
await WriteRowsAsync("""
WITH diff_strategies AS MATERIALIZED (
    SELECT id, code
    FROM strategies
    WHERE code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_(diff|adjusted_diff|shift_diff)_[0-9]+(_[0-9]+)?(_revert)?_instant$'
)
SELECT
    strategy.code,
    order_row.status,
    order_row.outcome,
    order_row.price,
    order_row.notional_usd,
    order_row.created_at_utc,
    order_row.expires_at_utc,
    order_row.filled_at_utc
FROM paper_orders order_row
JOIN diff_strategies strategy ON strategy.id = order_row.strategy_id
ORDER BY order_row.created_at_utc DESC
LIMIT 30;
""");

await WriteSectionAsync("Latest Diff skipped runs");
await WriteRowsAsync("""
WITH diff_strategies AS MATERIALIZED (
    SELECT id, code
    FROM strategies
    WHERE code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_(diff|adjusted_diff|shift_diff)_[0-9]+(_[0-9]+)?(_revert)?_instant$'
)
SELECT
    strategy.code,
    run.market_start_utc,
    run.updated_at_utc,
    run.skip_reason,
    run.skip_diagnostics_json ->> 'counter_mode' AS counter_mode,
    run.skip_diagnostics_json ->> 'counter_initialized' AS counter_initialized,
    run.skip_diagnostics_json ->> 'counter_target_market_result_received' AS target_received,
    run.skip_diagnostics_json ->> 'raw_diff' AS raw_diff,
    run.skip_diagnostics_json ->> 'adjusted_diff' AS adjusted_diff,
    run.skip_diagnostics_json ->> 'effective_diff' AS effective_diff,
    run.skip_diagnostics_json ->> 'threshold' AS threshold,
    run.skip_diagnostics_json ->> 'shift_diff_count' AS shift_n
FROM strategy_market_paper_runs run
JOIN diff_strategies strategy ON strategy.id = run.strategy_id
WHERE run.status = 'Skipped'
ORDER BY run.updated_at_utc DESC
LIMIT 40;
""");

await tx.CommitAsync();
await WriteLineAsync("");
await WriteLineAsync($"Diff Paper activity diagnostic finished at {DateTimeOffset.UtcNow:O}");

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    await command.ExecuteNonQueryAsync();
}

async Task WriteScalarAsync(string label, string sql)
{
    var savepointName = "query_guard_" + (++savepointIndex).ToString(System.Globalization.CultureInfo.InvariantCulture);
    await ExecuteTransactionControlAsync($"SAVEPOINT {savepointName};");
    try
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        var value = await command.ExecuteScalarAsync();
        await ExecuteTransactionControlAsync($"RELEASE SAVEPOINT {savepointName};");
        await WriteLineAsync($"{label}: {value}");
    }
    catch (Exception ex)
    {
        await ExecuteTransactionControlAsync($"ROLLBACK TO SAVEPOINT {savepointName}; RELEASE SAVEPOINT {savepointName};");
        await WriteLineAsync($"{label}: QUERY_FAILED={ex.GetType().Name}: {ex.Message}");
    }
}

async Task WriteRowsAsync(string sql, params (string Name, object Value)[] parameters)
{
    var savepointName = "query_guard_" + (++savepointIndex).ToString(System.Globalization.CultureInfo.InvariantCulture);
    await ExecuteTransactionControlAsync($"SAVEPOINT {savepointName};");
    try
    {
        await using var command = new NpgsqlCommand(sql, connection, tx)
        {
            CommandTimeout = 25
        };

        foreach (var parameter in parameters)
        {
            var dbType = parameter.Value is DateTime ? NpgsqlDbType.TimestampTz : NpgsqlDbType.Text;
            command.Parameters.Add(parameter.Name, dbType).Value = parameter.Value;
        }

        {
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

        await ExecuteTransactionControlAsync($"RELEASE SAVEPOINT {savepointName};");
    }
    catch (Exception ex)
    {
        await ExecuteTransactionControlAsync($"ROLLBACK TO SAVEPOINT {savepointName}; RELEASE SAVEPOINT {savepointName};");
        await WriteLineAsync("QUERY_FAILED=" + ex.GetType().Name + ": " + ex.Message);
    }
}

async Task ExecuteTransactionControlAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    await command.ExecuteNonQueryAsync();
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
