using System.Globalization;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not configured.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    Host = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE") ?? "192.168.0.101",
    Timeout = 10,
    CommandTimeout = 120
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await ExecuteNonQueryAsync("SET default_transaction_read_only = on");
await ExecuteNonQueryAsync("SET statement_timeout = '8s'");

Console.WriteLine("== Database ==");
await PrintRowsAsync("""
SELECT
    now() AT TIME ZONE 'UTC' AS db_now_utc,
    current_database() AS database_name;
""");

Console.WriteLine();
Console.WriteLine("== Service heartbeats ==");
await PrintRowsAsync("""
SELECT
    service_name,
    status,
    mode,
    version,
    started_at_utc,
    last_heartbeat_utc,
    round(extract(epoch FROM ((now() AT TIME ZONE 'UTC') - (last_heartbeat_utc AT TIME ZONE 'UTC'))))::int AS heartbeat_age_seconds,
    current_loop,
    left(coalesce(last_error, ''), 240) AS last_error_prefix
FROM service_heartbeats
ORDER BY service_name;
""");

Console.WriteLine();
Console.WriteLine("== Recent activity timestamps ==");
await PrintRowsAsync("""
SELECT 'strategy_market_paper_runs.updated' AS metric, max(updated_at_utc) AS last_utc FROM strategy_market_paper_runs
UNION ALL
SELECT 'paper_orders.created', max(created_at_utc) FROM paper_orders
UNION ALL
SELECT 'live_orders.updated', max(updated_at_utc) FROM live_orders
UNION ALL
SELECT 'crypto_diff_snapshots.sampled', max(sampled_at_utc) FROM crypto_up_down_5m_diff_snapshots
ORDER BY metric;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff strategy inventory ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT
        id,
        code,
        enabled,
        live_stakes,
        auto_live_paused,
        paper_stake_amount,
        regexp_match(code, '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_([1-6])_([1-9]|1[0-2])_instant$') AS parts
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    count(*) AS total_shift_diff,
    count(*) FILTER (WHERE enabled) AS enabled_count,
    count(*) FILTER (WHERE live_stakes) AS live_count,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused_count,
    count(*) FILTER (WHERE parts IS NULL) AS invalid_code_shape,
    min(paper_stake_amount) AS min_paper_stake,
    max(paper_stake_amount) AS max_paper_stake
FROM shift_strategies;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff strategy distribution ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT regexp_match(code, '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_([1-6])_([1-9]|1[0-2])_instant$') AS parts
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    upper(parts[1]) AS asset,
    parts[3]::int AS shift_n,
    count(*) AS strategies,
    count(*) FILTER (WHERE lower(parts[2]) = 'up') AS up_group,
    count(*) FILTER (WHERE lower(parts[2]) = 'down') AS down_group
FROM shift_strategies
WHERE parts IS NOT NULL
GROUP BY upper(parts[1]), parts[3]::int
ORDER BY asset, shift_n;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff paper order summary ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    count(*) AS paper_orders_total,
    count(*) FILTER (WHERE order_row.created_at_utc >= now() - interval '24 hours') AS paper_orders_24h,
    count(*) FILTER (WHERE order_row.created_at_utc >= now() - interval '6 hours') AS paper_orders_6h,
    count(*) FILTER (WHERE order_row.created_at_utc >= now() - interval '1 hour') AS paper_orders_1h,
    min(order_row.created_at_utc) AS first_order_created_utc,
    max(order_row.created_at_utc) AS last_order_created_utc
FROM paper_orders order_row
WHERE order_row.strategy_id IN (SELECT id FROM shift_strategies);
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff paper orders by status ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    order_row.status,
    count(*) AS orders,
    min(order_row.created_at_utc) AS first_created_utc,
    max(order_row.created_at_utc) AS last_created_utc
FROM paper_orders order_row
WHERE order_row.strategy_id IN (SELECT id FROM shift_strategies)
GROUP BY order_row.status
ORDER BY orders DESC, order_row.status;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff paper orders by asset and shift ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT
        id,
        regexp_match(code, '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_([1-6])_([1-9]|1[0-2])_instant$') AS parts
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    upper(strategy.parts[1]) AS asset,
    strategy.parts[3]::int AS shift_n,
    count(*) AS orders,
    count(*) FILTER (WHERE order_row.status = 'Filled') AS filled,
    count(*) FILTER (WHERE order_row.status = 'Expired') AS expired,
    count(*) FILTER (WHERE order_row.status = 'Pending') AS pending,
    min(order_row.created_at_utc) AS first_created_utc,
    max(order_row.created_at_utc) AS last_created_utc
FROM paper_orders order_row
INNER JOIN shift_strategies strategy ON strategy.id = order_row.strategy_id
WHERE strategy.parts IS NOT NULL
GROUP BY upper(strategy.parts[1]), strategy.parts[3]::int
ORDER BY asset, shift_n;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff live order summary ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    count(*) AS live_orders_total,
    count(*) FILTER (WHERE live_order.created_at_utc >= now() - interval '24 hours') AS live_orders_24h,
    min(live_order.created_at_utc) AS first_live_created_utc,
    max(live_order.created_at_utc) AS last_live_created_utc
FROM live_orders live_order
WHERE live_order.strategy_id IN (SELECT id FROM shift_strategies);
""");

Console.WriteLine();
Console.WriteLine("== Recent 24h ShiftDiff paper runs summary ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    count(*) AS runs_total,
    count(DISTINCT run.market_start_utc) AS distinct_markets,
    count(*) FILTER (WHERE run.status = 'PendingEntry') AS pending_entry,
    count(*) FILTER (WHERE run.status = 'Entered') AS entered,
    count(*) FILTER (WHERE run.status = 'Skipped') AS skipped,
    count(*) FILTER (WHERE run.status = 'Settled') AS settled,
    min(run.created_at_utc) AS first_run_created_utc,
    max(run.created_at_utc) AS last_run_created_utc,
    max(run.updated_at_utc) AS last_run_updated_utc
FROM strategy_market_paper_runs run
WHERE run.strategy_id IN (SELECT id FROM shift_strategies)
  AND run.updated_at_utc >= now() - interval '24 hours';
""");

Console.WriteLine();
Console.WriteLine("== Recent 24h ShiftDiff paper runs by status ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    run.status,
    count(*) AS runs,
    min(run.market_start_utc) AS first_market_start_utc,
    max(run.market_start_utc) AS last_market_start_utc,
    max(run.updated_at_utc) AS last_updated_utc
FROM strategy_market_paper_runs run
WHERE run.strategy_id IN (SELECT id FROM shift_strategies)
  AND run.updated_at_utc >= now() - interval '24 hours'
GROUP BY run.status
ORDER BY runs DESC, run.status;
""");

Console.WriteLine();
Console.WriteLine("== Recent 24h ShiftDiff markets ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    run.market_start_utc,
    count(*) AS runs,
    count(*) FILTER (WHERE run.status = 'PendingEntry') AS pending_entry,
    count(*) FILTER (WHERE run.status = 'Entered') AS entered,
    count(*) FILTER (WHERE run.status = 'Skipped') AS skipped,
    count(*) FILTER (WHERE run.status = 'Settled') AS settled,
    count(*) FILTER (WHERE run.paper_order_id IS NOT NULL) AS linked_paper_orders,
    max(run.updated_at_utc) AS last_updated_utc
FROM strategy_market_paper_runs run
WHERE run.strategy_id IN (SELECT id FROM shift_strategies)
  AND run.updated_at_utc >= now() - interval '24 hours'
GROUP BY run.market_start_utc
ORDER BY run.market_start_utc DESC NULLS LAST
LIMIT 12;
""");

Console.WriteLine();
Console.WriteLine("== Recent 24h ShiftDiff skip reasons ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    coalesce(run.skip_reason, '') AS skip_reason,
    count(*) AS runs,
    min(run.market_start_utc) AS first_market_start_utc,
    max(run.market_start_utc) AS last_market_start_utc
FROM strategy_market_paper_runs run
WHERE run.strategy_id IN (SELECT id FROM shift_strategies)
  AND run.updated_at_utc >= now() - interval '24 hours'
  AND run.status = 'Skipped'
GROUP BY coalesce(run.skip_reason, '')
ORDER BY runs DESC, skip_reason
LIMIT 20;
""");

Console.WriteLine();
Console.WriteLine("== Recent 24h threshold diagnostics ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
),
recent_runs AS MATERIALIZED (
    SELECT
        run.skip_reason,
        nullif(run.skip_diagnostics_json ->> 'threshold', '')::numeric AS threshold_value,
        nullif(run.skip_diagnostics_json ->> 'effective_diff', '')::numeric AS effective_diff,
        nullif(run.skip_diagnostics_json ->> 'raw_diff', '')::numeric AS raw_diff,
        nullif(run.skip_diagnostics_json ->> 'shift_diff_count', '')::int AS shift_n,
        nullif(run.skip_diagnostics_json ->> 'shift_diff_positive_adjustments', '')::int AS positive_adjustments,
        nullif(run.skip_diagnostics_json ->> 'shift_diff_negative_adjustments', '')::int AS negative_adjustments,
        run.skip_diagnostics_json ->> 'counter_target_market_result_received' AS target_received
    FROM strategy_market_paper_runs run
    WHERE run.strategy_id IN (SELECT id FROM shift_strategies)
      AND run.updated_at_utc >= now() - interval '24 hours'
      AND run.status = 'Skipped'
      AND run.skip_diagnostics_json IS NOT NULL
)
SELECT
    skip_reason,
    count(*) AS runs,
    count(*) FILTER (WHERE target_received = 'true') AS target_received_true,
    count(*) FILTER (WHERE target_received = 'false') AS target_received_false,
    min(effective_diff) AS min_effective_diff,
    max(effective_diff) AS max_effective_diff,
    max(threshold_value) AS max_threshold,
    max(raw_diff) AS max_raw_diff,
    min(raw_diff) AS min_raw_diff,
    max(positive_adjustments) AS max_positive_adjustments,
    max(negative_adjustments) AS max_negative_adjustments
FROM recent_runs
GROUP BY skip_reason
ORDER BY runs DESC, skip_reason
LIMIT 20;
""");

Console.WriteLine();
Console.WriteLine("== Recent ShiftDiff paper orders ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id, code, name
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
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
INNER JOIN shift_strategies strategy ON strategy.id = order_row.strategy_id
ORDER BY order_row.created_at_utc DESC
LIMIT 20;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff settled runs by strategy ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id, code, name
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
),
strategy_settled AS MATERIALIZED (
    SELECT
        strategy.id,
        strategy.code,
        count(*) FILTER (WHERE run.status = 'Settled') AS settled_runs,
        count(*) FILTER (WHERE run.status = 'Settled' AND coalesce(run.realized_pnl_usd, 0) > 0) AS won_runs,
        count(*) FILTER (WHERE run.status = 'Settled' AND coalesce(run.realized_pnl_usd, 0) < 0) AS lost_runs,
        coalesce(sum(run.realized_pnl_usd) FILTER (WHERE run.status = 'Settled'), 0) AS realized_pnl_usd,
        max(run.settled_at_utc) FILTER (WHERE run.status = 'Settled') AS last_settled_utc
    FROM shift_strategies strategy
    LEFT JOIN strategy_market_paper_runs run ON run.strategy_id = strategy.id
    GROUP BY strategy.id, strategy.code
)
SELECT
    count(*) FILTER (WHERE settled_runs > 0) AS strategies_with_settled,
    count(*) FILTER (WHERE settled_runs = 0) AS strategies_without_settled,
    sum(settled_runs) AS settled_runs,
    sum(won_runs) AS won_runs,
    sum(lost_runs) AS lost_runs,
    sum(realized_pnl_usd) AS realized_pnl_usd,
    max(last_settled_utc) AS last_settled_utc
FROM strategy_settled;
""");

Console.WriteLine();
Console.WriteLine("== Top ShiftDiff settled strategies ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id, code, name
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    strategy.code,
    count(*) AS settled_runs,
    count(*) FILTER (WHERE coalesce(run.realized_pnl_usd, 0) > 0) AS won_runs,
    count(*) FILTER (WHERE coalesce(run.realized_pnl_usd, 0) < 0) AS lost_runs,
    coalesce(sum(run.realized_pnl_usd), 0) AS realized_pnl_usd,
    max(run.settled_at_utc) AS last_settled_utc
FROM strategy_market_paper_runs run
INNER JOIN shift_strategies strategy ON strategy.id = run.strategy_id
WHERE run.status = 'Settled'
GROUP BY strategy.code
ORDER BY settled_runs DESC, strategy.code
LIMIT 20;
""");

Console.WriteLine();
Console.WriteLine("== Latest ShiftDiff settled runs ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id, code, name
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    strategy.code,
    run.market_start_utc,
    run.selected_outcome,
    run.entry_price,
    run.stake_usd,
    run.settlement_value_usd,
    run.realized_pnl_usd,
    run.settled_at_utc,
    run.paper_order_id
FROM strategy_market_paper_runs run
INNER JOIN shift_strategies strategy ON strategy.id = run.strategy_id
WHERE run.status = 'Settled'
ORDER BY run.settled_at_utc DESC
LIMIT 20;
""");

Console.WriteLine();
Console.WriteLine("== Dashboard global latest 100 paper orders composition ==");
await PrintRowsAsync("""
WITH latest_orders AS MATERIALIZED (
    SELECT
        order_row.id,
        order_row.strategy_id,
        order_row.status AS paper_order_status,
        order_row.created_at_utc
    FROM paper_orders order_row
    ORDER BY order_row.created_at_utc DESC
    LIMIT 100
)
SELECT
    count(*) AS orders,
    count(*) FILTER (WHERE strategy.code LIKE '%_shift_diff_%') AS shift_diff_orders,
    count(*) FILTER (WHERE strategy.code LIKE '%_shift_diff_%' AND run.status = 'Settled') AS shift_diff_with_settled_run,
    min(latest.created_at_utc) AS oldest_created_utc,
    max(latest.created_at_utc) AS newest_created_utc
FROM latest_orders latest
INNER JOIN strategies strategy ON strategy.id = latest.strategy_id
LEFT JOIN strategy_market_paper_runs run ON run.paper_order_id = latest.id;
""");

Console.WriteLine();
Console.WriteLine("== Latest ShiftDiff skipped runs ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id, code
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    strategy.code,
    run.market_start_utc,
    run.updated_at_utc,
    run.skip_reason,
    run.skip_diagnostics_json ->> 'counter_mode' AS counter_mode,
    run.skip_diagnostics_json ->> 'counter_target_market_result_received' AS target_received,
    run.skip_diagnostics_json ->> 'raw_diff' AS raw_diff,
    run.skip_diagnostics_json ->> 'effective_diff' AS effective_diff,
    run.skip_diagnostics_json ->> 'threshold' AS threshold,
    run.skip_diagnostics_json ->> 'shift_diff_count' AS shift_n,
    run.skip_diagnostics_json ->> 'shift_diff_positive_adjustments' AS positive_adjustments,
    run.skip_diagnostics_json ->> 'shift_diff_negative_adjustments' AS negative_adjustments
FROM strategy_market_paper_runs run
INNER JOIN shift_strategies strategy ON strategy.id = run.strategy_id
WHERE run.status = 'Skipped'
ORDER BY run.updated_at_utc DESC
LIMIT 20;
""");

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

async Task PrintRowsAsync(string sql)
{
    try
    {
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 10
        };
        await using var reader = await command.ExecuteReaderAsync();
        var fieldCount = reader.FieldCount;
        var hasRows = false;

        while (await reader.ReadAsync())
        {
            hasRows = true;
            for (var index = 0; index < fieldCount; index++)
            {
                if (index > 0)
                {
                    Console.Write(" | ");
                }

                Console.Write(reader.GetName(index));
                Console.Write('=');
                Console.Write(FormatValue(reader.IsDBNull(index) ? null : reader.GetValue(index)));
            }

            Console.WriteLine();
        }

        if (!hasRows)
        {
            Console.WriteLine("(no rows)");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("QUERY_FAILED=" + ex.GetType().Name + ": " + ex.Message);
    }
}

static string FormatValue(object? value)
{
    return value switch
    {
        null => "NULL",
        DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString(CultureInfo.InvariantCulture),
        float number => number.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
