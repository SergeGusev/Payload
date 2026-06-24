using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 2;
}

var builder = new NpgsqlConnectionStringBuilder(connectionString);
var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
if (!string.IsNullOrWhiteSpace(hostOverride))
{
    builder.Host = hostOverride;
}

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();

await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
await ExecuteNonQueryAsync("SET LOCAL transaction_read_only = on; SET LOCAL statement_timeout = '120s'; SET LOCAL lock_timeout = '2s';");

Console.WriteLine($"verify_started_utc={DateTimeOffset.UtcNow:O}");
Console.WriteLine($"target_database={builder.Database}");
Console.WriteLine($"target_host={builder.Host}");
Console.WriteLine($"target_port={builder.Port}");

await PrintRowsAsync(
    """
    SELECT 'db_now_utc' AS name, (now() AT TIME ZONE 'UTC')::text AS value
    UNION ALL
    SELECT 'service_heartbeat',
        concat_ws('|',
            service_name,
            'status=' || status,
            'mode=' || mode,
            'version=' || coalesce(version, ''),
            'started=' || started_at_utc::text,
            'last=' || last_heartbeat_utc::text,
            'age=' || (now() - last_heartbeat_utc)::text,
            'loop=' || coalesce(current_loop, ''),
            'last_error=' || coalesce(last_error, ''))
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
    ORDER BY name;
    """);

Console.WriteLine("paper_counts");
await PrintRowsAsync(
    """
    SELECT 'live_orders' AS name, count(*)::text AS value FROM live_orders
    UNION ALL SELECT 'live_orders_with_paper_order_id', count(*)::text FROM live_orders WHERE paper_order_id IS NOT NULL
    UNION ALL SELECT 'live_orders_missing_signal', count(*)::text FROM live_orders WHERE signal_id IS NULL
    UNION ALL SELECT 'signals_total', count(*)::text FROM signals
    UNION ALL SELECT 'signals_with_live_order', count(DISTINCT signal_id)::text FROM live_orders WHERE signal_id IS NOT NULL
    UNION ALL SELECT 'signal_rejections', count(*)::text FROM signal_rejections
    UNION ALL SELECT 'paper_orders', count(*)::text FROM paper_orders
    UNION ALL SELECT 'paper_fills', count(*)::text FROM paper_fills
    UNION ALL SELECT 'strategy_market_paper_runs', count(*)::text FROM strategy_market_paper_runs
    UNION ALL SELECT 'paper_positions', count(*)::text FROM paper_positions
    UNION ALL SELECT 'paper_position_settlements', count(*)::text FROM paper_position_settlements
    UNION ALL SELECT 'paper_copied_trader_performance', count(*)::text FROM paper_copied_trader_performance
    UNION ALL SELECT 'paper_copied_leader_positions', count(*)::text FROM paper_copied_leader_positions
    UNION ALL SELECT 'paper_copied_leader_activity_events', count(*)::text FROM paper_copied_leader_activity_events
    UNION ALL SELECT 'polymarket_onchain_paper_signal_results', count(*)::text FROM polymarket_onchain_paper_signal_results
    UNION ALL SELECT 'paper_live_shadow_decisions', count(*)::text FROM paper_live_shadow_decisions
    UNION ALL SELECT 'paper_live_shadow_discrepancies', count(*)::text FROM paper_live_shadow_discrepancies
    UNION ALL SELECT 'strategies_with_paper_lost_counter', count(*)::text FROM strategies WHERE paper_lost_counter <> 0
    ORDER BY name;
    """);

Console.WriteLine("eth_premarket_cleanup");
await PrintRowsAsync(
    """
    SELECT 'old_no_suffix_eth_down_enabled', count(*)::text
    FROM strategies
    WHERE code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
      AND enabled
    UNION ALL
    SELECT 'old_no_suffix_eth_down_live_enabled', count(*)::text
    FROM strategies
    WHERE code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
      AND live_stakes
    UNION ALL
    SELECT 'reference_average_enabled', count(*)::text
    FROM strategies
    WHERE name ILIKE '% Reference Average Premarket'
      AND enabled
    UNION ALL
    SELECT 'strategy_names_with_fak', count(*)::text
    FROM strategies
    WHERE name ILIKE '%FAK%'
    UNION ALL
    SELECT 'reference_average_names_with_fak', count(*)::text
    FROM strategies
    WHERE name ILIKE '% Reference Average FAK Premarket%'
    UNION ALL
    SELECT 'reference_average_names_missing_phrase', count(*)::text
    FROM strategies
    WHERE description ILIKE '%largest full in-memory reference average%'
      AND code !~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
      AND name NOT ILIKE '%Reference Average%'
    ORDER BY 1;
    """);

Console.WriteLine("strategy_names_with_fak_details");
await PrintRowsAsync(
    """
    SELECT
        'strategy_name_with_fak_' || row_number() OVER (ORDER BY code)::text AS name,
        code || '|enabled=' || enabled::text || '|live=' || live_stakes::text || '|name=' || name AS value
    FROM strategies
    WHERE name ILIKE '%FAK%'
    ORDER BY code;
    """);

Console.WriteLine("prev_score_fak_strategies");
await PrintRowsAsync(
    """
    WITH target AS (
        SELECT *
        FROM strategies
        WHERE code IN (
            'btc_up_down_5m_prev_score_countertrend_fak',
            'btc_up_down_5m_prev_score_countertrend_fak_revert')
    ),
    counts AS (
        SELECT 'prev_score_fixed_price_rows' AS name, count(*)::text AS value
        FROM strategies
        WHERE code ~ '^btc_up_down_5m_prev_score_countertrend_[0-9]+$'
        UNION ALL
        SELECT 'prev_score_fak_rows', count(*)::text FROM target WHERE code = 'btc_up_down_5m_prev_score_countertrend_fak'
        UNION ALL
        SELECT 'prev_score_fak_revert_rows', count(*)::text FROM target WHERE code = 'btc_up_down_5m_prev_score_countertrend_fak_revert'
        UNION ALL
        SELECT 'prev_score_fak_enabled_rows', count(*)::text FROM target WHERE enabled
        UNION ALL
        SELECT 'prev_score_fak_live_rows', count(*)::text FROM target WHERE live_stakes
        UNION ALL
        SELECT 'prev_score_visible_names_with_fak', count(*)::text
        FROM target
        WHERE name ILIKE '%FAK%')
    SELECT name, value
    FROM counts
    UNION ALL
    SELECT
        'prev_score_strategy_' || row_number() OVER (ORDER BY code)::text AS name,
        code || '|id=' || id::text || '|enabled=' || enabled::text || '|live=' || live_stakes::text ||
            '|paper=' || paper_stake_amount::text || '|name=' || name AS value
    FROM target
    ORDER BY name;
    """);

Console.WriteLine("prev_score_fak_post_start_activity");
await PrintRowsAsync(
    """
    WITH heartbeat AS (
        SELECT started_at_utc
        FROM service_heartbeats
        WHERE service_name = 'PolyCopyTrader.Service'
    ),
    target AS (
        SELECT id, code
        FROM strategies
        WHERE code IN (
            'btc_up_down_5m_prev_score_countertrend_fak',
            'btc_up_down_5m_prev_score_countertrend_fak_revert')
    )
    SELECT
        'prev_score_fak_strategy_runs_since_start' AS name,
        count(*)::text AS value
    FROM strategy_market_paper_runs runs
    JOIN target ON target.id = runs.strategy_id
    CROSS JOIN heartbeat
    WHERE runs.created_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'prev_score_fak_paper_orders_since_start', count(*)::text
    FROM paper_orders orders
    JOIN target ON target.id = orders.strategy_id
    CROSS JOIN heartbeat
    WHERE orders.created_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'prev_score_fak_paper_fills_since_start', count(*)::text
    FROM paper_fills fills
    JOIN paper_orders orders ON orders.id = fills.paper_order_id
    JOIN target ON target.id = orders.strategy_id
    CROSS JOIN heartbeat
    WHERE fills.filled_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'prev_score_fak_live_orders_since_start', count(*)::text
    FROM live_orders orders
    JOIN target ON target.id = orders.strategy_id
    CROSS JOIN heartbeat
    WHERE orders.created_at_utc >= heartbeat.started_at_utc
    ORDER BY name;
    """);

Console.WriteLine("prev_score_fak_post_start_order_details");
await PrintRowsAsync(
    """
    WITH heartbeat AS (
        SELECT started_at_utc
        FROM service_heartbeats
        WHERE service_name = 'PolyCopyTrader.Service'
    ),
    target AS (
        SELECT id, code
        FROM strategies
        WHERE code IN (
            'btc_up_down_5m_prev_score_countertrend_fak',
            'btc_up_down_5m_prev_score_countertrend_fak_revert')
    ),
    recent AS (
        SELECT
            orders.created_at_utc,
            target.code,
            orders.status,
            orders.outcome,
            orders.price,
            orders.notional_usd,
            orders.execution_source,
            orders.raw_decision_json::jsonb AS raw_json
        FROM paper_orders orders
        JOIN target ON target.id = orders.strategy_id
        CROSS JOIN heartbeat
        WHERE orders.created_at_utc >= heartbeat.started_at_utc
        ORDER BY orders.created_at_utc DESC
        LIMIT 10
    )
    SELECT
        'prev_score_fak_order_' || row_number() OVER (ORDER BY created_at_utc DESC)::text AS name,
        code || '|created=' || created_at_utc::text ||
            '|status=' || status ||
            '|outcome=' || outcome ||
            '|price=' || price::text ||
            '|notional=' || notional_usd::text ||
            '|execution=' || coalesce(execution_source, '') ||
            '|decision=' || coalesce(raw_json ->> 'decision_source', '') ||
            '|order_mode=' || coalesce(raw_json ->> 'order_execution_mode', '') ||
            '|paper_mode=' || coalesce(raw_json ->> 'paper_order_execution_mode', '') ||
            '|bias=' || coalesce(raw_json ->> 'previous_bias', '') ||
            '|selected=' || coalesce(raw_json ->> 'selected_direction', '') ||
            '|pricing=' || coalesce(raw_json ->> 'opening_limit_price_mode', '') ||
            '|filled=' || coalesce(raw_json ->> 'paper_fak_filled_notional_usd', '') AS value
    FROM recent
    ORDER BY created_at_utc DESC;
    """);

Console.WriteLine("fak_only_live_checks");
await PrintRowsAsync(
    """
    WITH heartbeat AS (
        SELECT started_at_utc
        FROM service_heartbeats
        WHERE service_name = 'PolyCopyTrader.Service'
    ),
    post_start_live AS (
        SELECT live_orders.*
        FROM live_orders, heartbeat
        WHERE live_orders.created_at_utc >= heartbeat.started_at_utc
    ),
    post_start_shadow_paper AS (
        SELECT paper_orders.*
        FROM paper_orders, heartbeat
        WHERE paper_orders.created_at_utc >= heartbeat.started_at_utc
          AND paper_orders.raw_decision_json::text LIKE '%"paper_live_shadow_test":true%'
    )
    SELECT 'live_orders_since_start_total' AS name, count(*)::text AS value
    FROM post_start_live
    UNION ALL
    SELECT 'live_orders_since_start_non_fak', count(*)::text
    FROM post_start_live
    WHERE upper(coalesce(order_type, '')) <> 'FAK'
    UNION ALL
    SELECT 'live_orders_since_start_post_only_true', count(*)::text
    FROM post_start_live
    WHERE coalesce(post_only, false)
    UNION ALL
    SELECT 'live_orders_since_start_gtd_type', count(*)::text
    FROM post_start_live
    WHERE upper(coalesce(order_type, '')) = 'GTD'
    UNION ALL
    SELECT 'live_orders_since_start_fak_type', count(*)::text
    FROM post_start_live
    WHERE upper(coalesce(order_type, '')) = 'FAK'
    UNION ALL
    SELECT 'shadow_paper_since_start_total', count(*)::text
    FROM post_start_shadow_paper
    UNION ALL
    SELECT 'shadow_paper_since_start_live_type_non_fak', count(*)::text
    FROM post_start_shadow_paper
    WHERE raw_decision_json::jsonb ? 'live_order_type'
      AND upper(coalesce(raw_decision_json::jsonb ->> 'live_order_type', '')) <> 'FAK'
    UNION ALL
    SELECT 'shadow_paper_since_start_live_type_fak', count(*)::text
    FROM post_start_shadow_paper
    WHERE upper(coalesce(raw_decision_json::jsonb ->> 'live_order_type', '')) = 'FAK'
    ORDER BY name;
    """);

Console.WriteLine("live_order_types_since_start");
await PrintRowsAsync(
    """
    WITH heartbeat AS (
        SELECT started_at_utc
        FROM service_heartbeats
        WHERE service_name = 'PolyCopyTrader.Service'
    )
    SELECT
        'live_order_type_' || row_number() OVER (ORDER BY count(*) DESC)::text AS name,
        count(*)::text || '|order_type=' || coalesce(order_type, '') || '|post_only=' || coalesce(post_only::text, 'null') || '|status=' || status || '|response=' || coalesce(response_status, '') AS value
    FROM live_orders, heartbeat
    WHERE live_orders.created_at_utc >= heartbeat.started_at_utc
    GROUP BY order_type, post_only, status, response_status
    ORDER BY count(*) DESC;
    """);

Console.WriteLine("post_start_activity");
await PrintRowsAsync(
    """
    WITH heartbeat AS (
        SELECT started_at_utc
        FROM service_heartbeats
        WHERE service_name = 'PolyCopyTrader.Service'
    )
    SELECT 'live_orders_since_start' AS name, count(*)::text AS value
    FROM live_orders, heartbeat
    WHERE live_orders.created_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'paper_orders_since_start', count(*)::text
    FROM paper_orders, heartbeat
    WHERE paper_orders.created_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'paper_fills_since_start', count(*)::text
    FROM paper_fills, heartbeat
    WHERE paper_fills.filled_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'strategy_runs_since_start', count(*)::text
    FROM strategy_market_paper_runs, heartbeat
    WHERE strategy_market_paper_runs.created_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'signals_since_start', count(*)::text
    FROM signals, heartbeat
    WHERE signals.created_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'paper_order_time_range',
        coalesce(min(paper_orders.created_at_utc)::text, '') || '..' || coalesce(max(paper_orders.created_at_utc)::text, '')
    FROM paper_orders, heartbeat
    WHERE paper_orders.created_at_utc >= heartbeat.started_at_utc
    ORDER BY name;
    """);

Console.WriteLine("live_orders_since_start");
await PrintRowsAsync(
    """
    WITH heartbeat AS (
        SELECT started_at_utc
        FROM service_heartbeats
        WHERE service_name = 'PolyCopyTrader.Service'
    )
    SELECT
        'live_order_status_' || row_number() OVER (ORDER BY count(*) DESC)::text AS name,
        count(*)::text || '|status=' || status || '|response=' || response_status || '|cancel=' || cancel_status AS value
    FROM live_orders, heartbeat
    WHERE live_orders.created_at_utc >= heartbeat.started_at_utc
    GROUP BY status, response_status, cancel_status
    ORDER BY count(*) DESC;
    """);

Console.WriteLine("api_errors_since_start");
await PrintRowsAsync(
    """
    WITH heartbeat AS (
        SELECT started_at_utc
        FROM service_heartbeats
        WHERE service_name = 'PolyCopyTrader.Service'
    ),
    grouped AS (
        SELECT
            component,
            operation,
            left(regexp_replace(message, '\s+', ' ', 'g'), 180) AS message,
            count(*)::bigint AS rows,
            max(created_at_utc) AS latest
        FROM api_errors, heartbeat
        WHERE api_errors.created_at_utc >= heartbeat.started_at_utc
        GROUP BY component, operation, left(regexp_replace(message, '\s+', ' ', 'g'), 180)
        ORDER BY latest DESC
        LIMIT 20
    )
    SELECT
        'api_error_' || row_number() OVER (ORDER BY latest DESC)::text AS name,
        rows::text || '|' || component || '|' || operation || '|' || latest::text || '|' || message AS value
    FROM grouped
    ORDER BY latest DESC;
    """);

await transaction.CommitAsync();
Console.WriteLine($"verify_finished_utc={DateTimeOffset.UtcNow:O}");
return 0;

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    command.CommandTimeout = 120;
    await command.ExecuteNonQueryAsync();
}

async Task PrintRowsAsync(string sql)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    command.CommandTimeout = 120;

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"{reader.GetString(0)}={reader.GetString(1)}");
    }
}
