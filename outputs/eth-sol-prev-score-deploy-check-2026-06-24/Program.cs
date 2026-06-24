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

Console.WriteLine("eth_sol_prev_score_strategy_rows");
await PrintRowsAsync(
    """
    WITH target AS (
        SELECT *
        FROM strategies
        WHERE code IN (
            'eth_up_down_5m_prev_score_countertrend_fak',
            'sol_up_down_5m_prev_score_countertrend_fak')
    ),
    invalid AS (
        SELECT *
        FROM strategies
        WHERE code ~ '^(eth|sol)_up_down_5m_prev_score_countertrend_[0-9]+$'
           OR code IN (
               'eth_up_down_5m_prev_score_countertrend_fak_revert',
               'sol_up_down_5m_prev_score_countertrend_fak_revert')
    )
    SELECT 'target_rows' AS name, count(*)::text AS value FROM target
    UNION ALL SELECT 'target_enabled_rows', count(*)::text FROM target WHERE enabled
    UNION ALL SELECT 'target_live_rows', count(*)::text FROM target WHERE live_stakes
    UNION ALL SELECT 'unexpected_eth_sol_numbered_or_revert_rows', count(*)::text FROM invalid
    UNION ALL SELECT 'eth_row_exists', count(*)::text FROM target WHERE code = 'eth_up_down_5m_prev_score_countertrend_fak'
    UNION ALL SELECT 'sol_row_exists', count(*)::text FROM target WHERE code = 'sol_up_down_5m_prev_score_countertrend_fak'
    UNION ALL SELECT 'btc_fak_row_exists', count(*)::text FROM strategies WHERE code = 'btc_up_down_5m_prev_score_countertrend_fak'
    UNION ALL SELECT 'btc_fak_revert_row_exists', count(*)::text FROM strategies WHERE code = 'btc_up_down_5m_prev_score_countertrend_fak_revert'
    ORDER BY name;
    """);

Console.WriteLine("strategies_schema_columns");
await PrintRowsAsync(
    """
    SELECT
        'strategies_has_' || required.column_name AS name,
        (count(columns.column_name) > 0)::text AS value
    FROM (VALUES
        ('id'),
        ('code'),
        ('name'),
        ('description'),
        ('enabled'),
        ('live_stakes'),
        ('paper_stake_amount')
    ) AS required(column_name)
    LEFT JOIN information_schema.columns columns
        ON columns.table_schema = 'public'
       AND columns.table_name = 'strategies'
       AND columns.column_name = required.column_name
    GROUP BY required.column_name
    ORDER BY required.column_name;
    """);

Console.WriteLine("eth_sol_prev_score_strategy_details");
await PrintRowsAsync(
    """
    SELECT
        'strategy_' || row_number() OVER (ORDER BY code)::text AS name,
        code || '|id=' || id::text ||
            '|enabled=' || enabled::text ||
            '|live=' || live_stakes::text ||
            '|paper=' || paper_stake_amount::text AS value
    FROM strategies
    WHERE code IN (
        'eth_up_down_5m_prev_score_countertrend_fak',
        'sol_up_down_5m_prev_score_countertrend_fak',
        'btc_up_down_5m_prev_score_countertrend_fak',
        'btc_up_down_5m_prev_score_countertrend_fak_revert')
    ORDER BY code;
    """);

Console.WriteLine("post_start_activity_for_eth_sol_prev_score");
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
            'eth_up_down_5m_prev_score_countertrend_fak',
            'sol_up_down_5m_prev_score_countertrend_fak')
    )
    SELECT 'strategy_runs_since_start' AS name, count(*)::text AS value
    FROM strategy_market_paper_runs runs
    JOIN target ON target.id = runs.strategy_id
    CROSS JOIN heartbeat
    WHERE runs.created_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'paper_orders_since_start', count(*)::text
    FROM paper_orders orders
    JOIN target ON target.id = orders.strategy_id
    CROSS JOIN heartbeat
    WHERE orders.created_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'paper_fills_since_start', count(*)::text
    FROM paper_fills fills
    JOIN paper_orders orders ON orders.id = fills.paper_order_id
    JOIN target ON target.id = orders.strategy_id
    CROSS JOIN heartbeat
    WHERE fills.filled_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'live_orders_since_start', count(*)::text
    FROM live_orders orders
    JOIN target ON target.id = orders.strategy_id
    CROSS JOIN heartbeat
    WHERE orders.created_at_utc >= heartbeat.started_at_utc
    ORDER BY name;
    """);

Console.WriteLine("post_start_activity_by_strategy");
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
            'eth_up_down_5m_prev_score_countertrend_fak',
            'sol_up_down_5m_prev_score_countertrend_fak')
    ),
    runs AS (
        SELECT target.code, count(runs.id) AS rows, max(runs.created_at_utc) AS latest
        FROM target
        LEFT JOIN strategy_market_paper_runs runs
            ON runs.strategy_id = target.id
           AND runs.created_at_utc >= (SELECT started_at_utc FROM heartbeat)
        GROUP BY target.code
    ),
    orders AS (
        SELECT target.code, count(orders.id) AS rows, max(orders.created_at_utc) AS latest
        FROM target
        LEFT JOIN paper_orders orders
            ON orders.strategy_id = target.id
           AND orders.created_at_utc >= (SELECT started_at_utc FROM heartbeat)
        GROUP BY target.code
    )
    SELECT
        'activity_' || target.code AS name,
        'runs=' || runs.rows::text ||
            '|latest_run=' || coalesce(runs.latest::text, '') ||
            '|paper_orders=' || orders.rows::text ||
            '|latest_order=' || coalesce(orders.latest::text, '') AS value
    FROM target
    JOIN runs ON runs.code = target.code
    JOIN orders ON orders.code = target.code
    ORDER BY target.code;
    """);

Console.WriteLine("recent_eth_sol_prev_score_paper_orders");
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
            'eth_up_down_5m_prev_score_countertrend_fak',
            'sol_up_down_5m_prev_score_countertrend_fak')
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
        LIMIT 20
    )
    SELECT
        'paper_order_' || row_number() OVER (ORDER BY created_at_utc DESC)::text AS name,
        code || '|created=' || created_at_utc::text ||
            '|status=' || status ||
            '|outcome=' || outcome ||
            '|price=' || price::text ||
            '|notional=' || notional_usd::text ||
            '|execution=' || coalesce(execution_source, '') ||
            '|decision=' || coalesce(raw_json ->> 'decision_source', '') ||
            '|reference_asset=' || coalesce(raw_json ->> 'reference_asset_symbol', '') ||
            '|reference_binance=' || coalesce(raw_json ->> 'reference_binance_symbol', '') ||
            '|order_mode=' || coalesce(raw_json ->> 'order_execution_mode', '') ||
            '|paper_mode=' || coalesce(raw_json ->> 'paper_order_execution_mode', '') ||
            '|bias=' || coalesce(raw_json ->> 'previous_bias', '') ||
            '|selected=' || coalesce(raw_json ->> 'selected_direction', '') ||
            '|pricing=' || coalesce(raw_json ->> 'opening_limit_price_mode', '') ||
            '|filled=' || coalesce(raw_json ->> 'paper_fak_filled_notional_usd', '') AS value
    FROM recent
    ORDER BY created_at_utc DESC;
    """);

Console.WriteLine("recent_eth_sol_prev_score_runs");
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
            'eth_up_down_5m_prev_score_countertrend_fak',
            'sol_up_down_5m_prev_score_countertrend_fak')
    ),
    recent AS (
        SELECT
            runs.created_at_utc,
            target.code,
            runs.market_slug,
            runs.status,
            runs.skip_reason
        FROM strategy_market_paper_runs runs
        JOIN target ON target.id = runs.strategy_id
        CROSS JOIN heartbeat
        WHERE runs.created_at_utc >= heartbeat.started_at_utc
        ORDER BY runs.created_at_utc DESC
        LIMIT 30
    )
    SELECT
        'run_' || row_number() OVER (ORDER BY created_at_utc DESC)::text AS name,
        code || '|created=' || created_at_utc::text ||
            '|market=' || coalesce(market_slug, '') ||
            '|status=' || coalesce(status, '') ||
            '|reason=' || coalesce(skip_reason, '') AS value
    FROM recent
    ORDER BY created_at_utc DESC;
    """);

Console.WriteLine("live_order_safety_since_start");
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
            'eth_up_down_5m_prev_score_countertrend_fak',
            'sol_up_down_5m_prev_score_countertrend_fak',
            'btc_up_down_5m_prev_score_countertrend_fak',
            'btc_up_down_5m_prev_score_countertrend_fak_revert')
    )
    SELECT 'all_live_orders_since_start' AS name, count(*)::text AS value
    FROM live_orders orders
    CROSS JOIN heartbeat
    WHERE orders.created_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'target_prev_score_live_orders_since_start', count(*)::text
    FROM live_orders orders
    JOIN target ON target.id = orders.strategy_id
    CROSS JOIN heartbeat
    WHERE orders.created_at_utc >= heartbeat.started_at_utc
    UNION ALL
    SELECT 'target_prev_score_live_non_fak_since_start', count(*)::text
    FROM live_orders orders
    JOIN target ON target.id = orders.strategy_id
    CROSS JOIN heartbeat
    WHERE orders.created_at_utc >= heartbeat.started_at_utc
      AND upper(coalesce(order_type, '')) <> 'FAK'
    UNION ALL
    SELECT 'target_prev_score_live_post_only_since_start', count(*)::text
    FROM live_orders orders
    JOIN target ON target.id = orders.strategy_id
    CROSS JOIN heartbeat
    WHERE orders.created_at_utc >= heartbeat.started_at_utc
      AND coalesce(post_only, false)
    ORDER BY name;
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
