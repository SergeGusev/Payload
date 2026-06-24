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
    CommandTimeout = 120,
    ApplicationName = "DiffRevertDeployCheck"
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await ExecuteNonQueryAsync("SET default_transaction_read_only = on");
await ExecuteNonQueryAsync("SET statement_timeout = '10s'");

if (args.Length > 0 && string.Equals(args[0], "--market-status-only", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("== Database ==");
    await PrintRowsAsync("""
    SELECT
        now() AT TIME ZONE 'UTC' AS db_now_utc,
        current_database() AS database_name;
    """);

    Console.WriteLine();
    Console.WriteLine("== Service heartbeat ==");
    await PrintRowsAsync("""
    SELECT
        service_name,
        status,
        mode,
        last_heartbeat_utc,
        round(extract(epoch FROM ((now() AT TIME ZONE 'UTC') - (last_heartbeat_utc AT TIME ZONE 'UTC'))))::int AS heartbeat_age_seconds,
        current_loop,
        left(coalesce(last_error, ''), 240) AS last_error_prefix
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service';
    """);

    Console.WriteLine();
    Console.WriteLine("== Current market data status ==");
    await PrintRowsAsync("""
    SELECT
        component,
        connection_state,
        subscribed_assets_count,
        stale,
        last_message_utc,
        round(extract(epoch FROM (now() - last_message_utc)))::int AS last_message_age_seconds,
        last_connected_utc,
        reconnect_count,
        updated_at_utc,
        round(extract(epoch FROM (now() - updated_at_utc)))::int AS updated_age_seconds,
        left(coalesce(last_error, ''), 240) AS last_error_prefix
    FROM market_data_status
    WHERE component = 'PolymarketMarketWebSocket'
       OR component = 'PolymarketMarketWebSocket:crypto-updown-5m-critical'
       OR (
            component LIKE 'PolymarketMarketWebSocket:shard-%'
            AND updated_at_utc >= now() - interval '10 minutes'
       )
    ORDER BY component;
    """);

    return;
}

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
Console.WriteLine("== Market data status ==");
await PrintRowsAsync("""
SELECT
    component,
    connection_state,
    subscribed_assets_count,
    stale,
    last_message_utc,
    round(extract(epoch FROM (now() - last_message_utc)))::int AS last_message_age_seconds,
    last_connected_utc,
    reconnect_count,
    updated_at_utc,
    round(extract(epoch FROM (now() - updated_at_utc)))::int AS updated_age_seconds,
    left(coalesce(last_error, ''), 240) AS last_error_prefix
FROM market_data_status
ORDER BY component;
""");

Console.WriteLine();
Console.WriteLine("== Crypto result ledger freshness ==");
await PrintRowsAsync("""
SELECT
    asset_symbol,
    count(*) FILTER (WHERE updated_at_utc >= now() - interval '1 hour') AS rows_updated_1h,
    max(market_start_utc) AS latest_market_start_utc,
    max(last_received_at_utc) AS latest_received_utc,
    round(extract(epoch FROM (now() - max(last_received_at_utc))))::int AS latest_received_age_seconds,
    count(*) FILTER (WHERE source = 'MarketWebSocket') AS market_websocket_rows,
    count(*) FILTER (WHERE source = 'TerminalOrderBook') AS terminal_order_book_rows,
    count(*) FILTER (WHERE source = 'GammaClosedMarket') AS gamma_closed_rows
FROM crypto_up_down_5m_websocket_resolved_markets
WHERE updated_at_utc >= now() - interval '24 hours'
GROUP BY asset_symbol
ORDER BY asset_symbol;
""");

Console.WriteLine();
Console.WriteLine("== Crypto result polling recent status ==");
await PrintRowsAsync("""
SELECT
    asset_symbol,
    status,
    count(*) AS observations,
    max(market_start_utc) AS latest_market_start_utc,
    max(last_poll_at_utc) AS latest_poll_utc,
    max(first_winner_at_utc) AS latest_winner_utc,
    max(result_delay_seconds) AS max_result_delay_seconds,
    left(coalesce(max(last_error), ''), 240) AS last_error_prefix
FROM crypto_up_down_5m_result_polling_observations
WHERE updated_at_utc >= now() - interval '3 hours'
GROUP BY asset_symbol, status
ORDER BY asset_symbol, status;
""");

Console.WriteLine();
Console.WriteLine("== Diff snapshot freshness ==");
await PrintRowsAsync("""
SELECT
    asset_symbol,
    max(market_start_utc) AS latest_market_start_utc,
    max(sampled_at_utc) AS latest_sampled_utc,
    round(extract(epoch FROM (now() - max(sampled_at_utc))))::int AS latest_sampled_age_seconds,
    max(up_count) AS latest_up_count,
    max(down_count) AS latest_down_count,
    max(diff) AS latest_diff,
    bool_or(history_fetch_error IS NOT NULL) AS has_history_fetch_error,
    left(coalesce(max(history_fetch_error), ''), 240) AS history_fetch_error_prefix
FROM crypto_up_down_5m_diff_snapshots
WHERE sampled_at_utc >= now() - interval '3 hours'
GROUP BY asset_symbol
ORDER BY asset_symbol;
""");

Console.WriteLine();
Console.WriteLine("== Diff-family strategy inventory ==");
await PrintRowsAsync("""
WITH classified AS MATERIALIZED (
    SELECT
        id,
        code,
        enabled,
        live_stakes,
        auto_live_paused,
        paper_stake_amount,
        CASE
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_adjusted_diff_([1-9]|1[0-9]|20)_revert_instant$' THEN 'adjusted_diff'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_adjusted_diff_([1-9]|1[0-9]|20)_instant$' THEN 'adjusted_diff'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_([1-6])_([1-9]|1[0-2])_revert_instant$' THEN 'shift_diff'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_([1-6])_([1-9]|1[0-2])_instant$' THEN 'shift_diff'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_diff_([1-9]|[1-9][0-9]|1[0-4][0-9]|150)_revert_instant$' THEN 'diff'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_diff_([1-9]|[1-9][0-9]|1[0-4][0-9]|150)_instant$' THEN 'diff'
            ELSE 'unclassified'
        END AS family,
        code LIKE '%_revert_instant' AS is_revert
    FROM strategies
    WHERE code LIKE '%_diff_%'
)
SELECT
    family,
    is_revert,
    count(*) AS strategies,
    count(*) FILTER (WHERE enabled) AS enabled_count,
    count(*) FILTER (WHERE live_stakes) AS live_count,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused_count,
    min(paper_stake_amount) AS min_paper_stake,
    max(paper_stake_amount) AS max_paper_stake
FROM classified
GROUP BY family, is_revert
ORDER BY family, is_revert;
""");

Console.WriteLine();
Console.WriteLine("== Diff-family totals ==");
await PrintRowsAsync("""
WITH classified AS MATERIALIZED (
    SELECT
        id,
        code,
        enabled,
        live_stakes,
        CASE
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_adjusted_diff_([1-9]|1[0-9]|20)_revert_instant$' THEN 'adjusted_diff'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_adjusted_diff_([1-9]|1[0-9]|20)_instant$' THEN 'adjusted_diff'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_([1-6])_([1-9]|1[0-2])_revert_instant$' THEN 'shift_diff'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_([1-6])_([1-9]|1[0-2])_instant$' THEN 'shift_diff'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_diff_([1-9]|[1-9][0-9]|1[0-4][0-9]|150)_revert_instant$' THEN 'diff'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_diff_([1-9]|[1-9][0-9]|1[0-4][0-9]|150)_instant$' THEN 'diff'
            ELSE 'unclassified'
        END AS family,
        code LIKE '%_revert_instant' AS is_revert
    FROM strategies
    WHERE code LIKE '%_diff_%'
)
SELECT
    count(*) AS total_diff_family,
    count(*) FILTER (WHERE is_revert) AS revert_total,
    count(*) FILTER (WHERE NOT is_revert) AS parent_total,
    count(*) FILTER (WHERE family = 'unclassified') AS unclassified_total,
    count(*) FILTER (WHERE enabled) AS enabled_count,
    count(*) FILTER (WHERE live_stakes) AS live_count
FROM classified;
""");

Console.WriteLine();
Console.WriteLine("== Diff-family recent runs by family/revert/status ==");
await PrintRowsAsync("""
WITH diff_strategies AS MATERIALIZED (
    SELECT
        id,
        CASE
            WHEN code LIKE '%_adjusted_diff_%' THEN 'adjusted_diff'
            WHEN code LIKE '%_shift_diff_%' THEN 'shift_diff'
            ELSE 'diff'
        END AS family,
        code LIKE '%_revert_instant' AS is_revert
    FROM strategies
    WHERE code LIKE '%_diff_%'
)
SELECT
    strategy.family,
    strategy.is_revert,
    run.status,
    count(*) AS runs,
    count(DISTINCT run.market_start_utc) AS distinct_markets,
    max(run.market_start_utc) AS latest_market_start_utc,
    max(run.updated_at_utc) AS last_updated_utc
FROM strategy_market_paper_runs run
INNER JOIN diff_strategies strategy ON strategy.id = run.strategy_id
WHERE run.updated_at_utc >= now() - interval '1 hour'
GROUP BY strategy.family, strategy.is_revert, run.status
ORDER BY strategy.family, strategy.is_revert, runs DESC, run.status;
""");

Console.WriteLine();
Console.WriteLine("== Diff-family stale pending checks ==");
await PrintRowsAsync("""
WITH diff_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_diff_%'
),
overdue_pending AS (
    SELECT
        count(*) AS runs,
        min(run.entry_due_at_utc) AS oldest_utc
    FROM strategy_market_paper_runs run
    WHERE run.status = 'PendingEntry'
      AND run.entry_due_at_utc < now() - interval '5 minutes'
      AND run.updated_at_utc >= now() - interval '24 hours'
      AND run.strategy_id IN (SELECT id FROM diff_strategies)
),
overdue_entered AS (
    SELECT
        count(*) AS runs,
        min(run.market_end_utc) AS oldest_utc
    FROM strategy_market_paper_runs run
    WHERE run.status = 'Entered'
      AND run.market_end_utc < now() - interval '10 minutes'
      AND run.updated_at_utc >= now() - interval '24 hours'
      AND run.strategy_id IN (SELECT id FROM diff_strategies)
)
SELECT
    overdue_pending.runs AS overdue_pending_entry_runs,
    overdue_entered.runs AS overdue_entered_runs,
    overdue_pending.oldest_utc AS oldest_overdue_pending_entry_utc,
    overdue_entered.oldest_utc AS oldest_overdue_entered_market_end_utc
FROM overdue_pending
CROSS JOIN overdue_entered;
""");

Console.WriteLine();
Console.WriteLine("== Diff-family paper order health ==");
await PrintRowsAsync("""
WITH diff_strategies AS MATERIALIZED (
    SELECT
        id,
        CASE
            WHEN code LIKE '%_adjusted_diff_%' THEN 'adjusted_diff'
            WHEN code LIKE '%_shift_diff_%' THEN 'shift_diff'
            ELSE 'diff'
        END AS family,
        code LIKE '%_revert_instant' AS is_revert
    FROM strategies
    WHERE code LIKE '%_diff_%'
)
SELECT
    strategy.family,
    strategy.is_revert,
    order_row.status,
    count(*) AS orders,
    count(*) FILTER (WHERE order_row.status = 'Pending' AND order_row.expires_at_utc < now()) AS expired_but_pending,
    min(order_row.created_at_utc) AS first_created_utc,
    max(order_row.created_at_utc) AS last_created_utc,
    max(order_row.expires_at_utc) AS max_expires_utc
FROM paper_orders order_row
INNER JOIN diff_strategies strategy ON strategy.id = order_row.strategy_id
WHERE order_row.created_at_utc >= now() - interval '6 hours'
GROUP BY strategy.family, strategy.is_revert, order_row.status
ORDER BY strategy.family, strategy.is_revert, orders DESC, order_row.status;
""");

Console.WriteLine();
Console.WriteLine("== Diff-family live leakage check ==");
await PrintRowsAsync("""
WITH diff_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_diff_%'
)
SELECT
    count(*) AS live_orders_total,
    count(*) FILTER (WHERE live_order.created_at_utc >= now() - interval '24 hours') AS live_orders_24h,
    max(live_order.created_at_utc) AS last_live_created_utc
FROM live_orders live_order
WHERE live_order.strategy_id IN (SELECT id FROM diff_strategies);
""");

Console.WriteLine();
Console.WriteLine("== Diff-family distribution by asset ==");
await PrintRowsAsync("""
WITH classified AS MATERIALIZED (
    SELECT
        code,
        regexp_match(code, '^(btc|eth|sol)_up_down_5m_') AS asset_parts,
        CASE
            WHEN code LIKE '%_adjusted_diff_%' THEN 'adjusted_diff'
            WHEN code LIKE '%_shift_diff_%' THEN 'shift_diff'
            ELSE 'diff'
        END AS family,
        code LIKE '%_revert_instant' AS is_revert
    FROM strategies
    WHERE code LIKE '%_diff_%'
)
SELECT
    upper(asset_parts[1]) AS asset,
    family,
    is_revert,
    count(*) AS strategies
FROM classified
WHERE asset_parts IS NOT NULL
GROUP BY upper(asset_parts[1]), family, is_revert
ORDER BY asset, family, is_revert;
""");

Console.WriteLine();
Console.WriteLine("== Sample Revert strategy rows ==");
await PrintRowsAsync("""
SELECT
    code,
    name,
    enabled,
    live_stakes,
    paper_stake_amount
FROM strategies
WHERE code IN (
    'btc_up_down_5m_up_diff_2_revert_instant',
    'eth_up_down_5m_down_diff_150_revert_instant',
    'btc_up_down_5m_up_adjusted_diff_20_revert_instant',
    'sol_up_down_5m_down_adjusted_diff_15_revert_instant',
    'btc_up_down_5m_up_shift_diff_2_4_revert_instant',
    'eth_up_down_5m_down_shift_diff_2_12_revert_instant',
    'sol_up_down_5m_up_shift_diff_6_1_revert_instant'
)
ORDER BY code;
""");

Console.WriteLine();
Console.WriteLine("== Recent Revert runs summary ==");
await PrintRowsAsync("""
WITH revert_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_diff_%_revert_instant'
)
SELECT
    count(*) AS runs_24h,
    count(DISTINCT run.market_start_utc) AS distinct_markets_24h,
    count(*) FILTER (WHERE run.status = 'PendingEntry') AS pending_entry,
    count(*) FILTER (WHERE run.status = 'Entered') AS entered,
    count(*) FILTER (WHERE run.status = 'Skipped') AS skipped,
    count(*) FILTER (WHERE run.status = 'Settled') AS settled,
    count(*) FILTER (WHERE run.paper_order_id IS NOT NULL) AS linked_paper_orders,
    min(run.created_at_utc) AS first_run_created_utc,
    max(run.created_at_utc) AS last_run_created_utc,
    max(run.updated_at_utc) AS last_run_updated_utc
FROM strategy_market_paper_runs run
WHERE run.strategy_id IN (SELECT id FROM revert_strategies)
  AND run.updated_at_utc >= now() - interval '24 hours';
""");

Console.WriteLine();
Console.WriteLine("== Recent Revert runs by family and status ==");
await PrintRowsAsync("""
WITH revert_strategies AS MATERIALIZED (
    SELECT
        id,
        CASE
            WHEN code LIKE '%_adjusted_diff_%' THEN 'adjusted_diff'
            WHEN code LIKE '%_shift_diff_%' THEN 'shift_diff'
            ELSE 'diff'
        END AS family
    FROM strategies
    WHERE code LIKE '%_diff_%_revert_instant'
)
SELECT
    strategy.family,
    run.status,
    count(*) AS runs,
    max(run.updated_at_utc) AS last_updated_utc
FROM strategy_market_paper_runs run
INNER JOIN revert_strategies strategy ON strategy.id = run.strategy_id
WHERE run.updated_at_utc >= now() - interval '24 hours'
GROUP BY strategy.family, run.status
ORDER BY strategy.family, runs DESC, run.status;
""");

Console.WriteLine();
Console.WriteLine("== Recent Revert skip reasons ==");
await PrintRowsAsync("""
WITH revert_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_diff_%_revert_instant'
)
SELECT
    run.skip_reason,
    count(*) AS runs,
    max(run.updated_at_utc) AS last_updated_utc
FROM strategy_market_paper_runs run
WHERE run.strategy_id IN (SELECT id FROM revert_strategies)
  AND run.updated_at_utc >= now() - interval '24 hours'
  AND run.status = 'Skipped'
GROUP BY run.skip_reason
ORDER BY runs DESC, run.skip_reason
LIMIT 12;
""");

Console.WriteLine();
Console.WriteLine("== Revert paper order summary ==");
await PrintRowsAsync("""
WITH revert_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_diff_%_revert_instant'
)
SELECT
    count(*) AS paper_orders_total,
    count(*) FILTER (WHERE order_row.created_at_utc >= now() - interval '24 hours') AS paper_orders_24h,
    count(*) FILTER (WHERE order_row.created_at_utc >= now() - interval '6 hours') AS paper_orders_6h,
    count(*) FILTER (WHERE order_row.created_at_utc >= now() - interval '1 hour') AS paper_orders_1h,
    count(*) FILTER (WHERE order_row.raw_decision_json::text LIKE '%diff_counter_trigger_outcome%') AS with_trigger_outcome_json,
    min(order_row.created_at_utc) AS first_order_created_utc,
    max(order_row.created_at_utc) AS last_order_created_utc
FROM paper_orders order_row
WHERE order_row.strategy_id IN (SELECT id FROM revert_strategies);
""");

Console.WriteLine();
Console.WriteLine("== Revert paper orders by family/status ==");
await PrintRowsAsync("""
WITH revert_strategies AS MATERIALIZED (
    SELECT
        id,
        CASE
            WHEN code LIKE '%_adjusted_diff_%' THEN 'adjusted_diff'
            WHEN code LIKE '%_shift_diff_%' THEN 'shift_diff'
            ELSE 'diff'
        END AS family
    FROM strategies
    WHERE code LIKE '%_diff_%_revert_instant'
)
SELECT
    strategy.family,
    order_row.status,
    count(*) AS orders,
    max(order_row.created_at_utc) AS last_created_utc
FROM paper_orders order_row
INNER JOIN revert_strategies strategy ON strategy.id = order_row.strategy_id
GROUP BY strategy.family, order_row.status
ORDER BY strategy.family, orders DESC, order_row.status;
""");

Console.WriteLine();
Console.WriteLine("== Revert live order summary ==");
await PrintRowsAsync("""
WITH revert_strategies AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_diff_%_revert_instant'
)
SELECT
    count(*) AS live_orders_total,
    count(*) FILTER (WHERE live_order.created_at_utc >= now() - interval '24 hours') AS live_orders_24h,
    max(live_order.created_at_utc) AS last_live_created_utc
FROM live_orders live_order
WHERE live_order.strategy_id IN (SELECT id FROM revert_strategies);
""");

Console.WriteLine();
Console.WriteLine("== Latest Revert paper orders ==");
await PrintRowsAsync("""
WITH revert_strategies AS MATERIALIZED (
    SELECT code, name, id
    FROM strategies
    WHERE code LIKE '%_diff_%_revert_instant'
)
SELECT
    strategy.code,
    order_row.status,
    order_row.outcome,
    order_row.price,
    order_row.created_at_utc,
    order_row.filled_at_utc,
    order_row.raw_decision_json::text LIKE '%diff_counter_trigger_outcome%' AS has_trigger_outcome_json
FROM paper_orders order_row
INNER JOIN revert_strategies strategy ON strategy.id = order_row.strategy_id
ORDER BY order_row.created_at_utc DESC
LIMIT 12;
""");

Console.WriteLine();
Console.WriteLine("== Latest Revert decision fields ==");
await PrintRowsAsync("""
WITH revert_strategies AS MATERIALIZED (
    SELECT code, id
    FROM strategies
    WHERE code LIKE '%_diff_%_revert_instant'
)
SELECT
    strategy.code,
    order_row.outcome AS order_outcome,
    order_row.raw_decision_json::jsonb ->> 'trigger_side' AS trigger_side,
    order_row.raw_decision_json::jsonb ->> 'diff_counter_trigger_outcome' AS trigger_outcome,
    order_row.raw_decision_json::jsonb ->> 'selected_direction' AS selected_direction,
    order_row.raw_decision_json::jsonb ->> 'outcome' AS raw_outcome,
    order_row.raw_decision_json::jsonb ->> 'effective_diff' AS effective_diff,
    order_row.created_at_utc
FROM paper_orders order_row
INNER JOIN revert_strategies strategy ON strategy.id = order_row.strategy_id
ORDER BY order_row.created_at_utc DESC
LIMIT 12;
""");

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

async Task PrintRowsAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    var columnCount = reader.FieldCount;
    for (var i = 0; i < columnCount; i++)
    {
        if (i > 0)
        {
            Console.Write(" | ");
        }

        Console.Write(reader.GetName(i));
    }

    Console.WriteLine();

    var rows = 0;
    while (await reader.ReadAsync())
    {
        rows++;
        for (var i = 0; i < columnCount; i++)
        {
            if (i > 0)
            {
                Console.Write(" | ");
            }

            Console.Write(FormatValue(await reader.IsDBNullAsync(i) ? null : reader.GetValue(i)));
        }

        Console.WriteLine();
    }

    if (rows == 0)
    {
        Console.WriteLine("(no rows)");
    }
}

static string FormatValue(object? value)
{
    return value switch
    {
        null => "NULL",
        DateTime dateTime when dateTime.Kind == DateTimeKind.Utc => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
        decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
        double doubleValue => doubleValue.ToString(CultureInfo.InvariantCulture),
        float floatValue => floatValue.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}
