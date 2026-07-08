using System.Data;
using Npgsql;

internal static class Program
{
    private static async Task<int> Main()
    {
        var sampleCount = ReadInt("POLYCOPYTRADER_MONITOR_SAMPLES", 3);
        var intervalSeconds = ReadInt("POLYCOPYTRADER_MONITOR_INTERVAL_SECONDS", 120);
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

        Console.WriteLine($"monitor_started_utc={DateTimeOffset.UtcNow:O}");
        Console.WriteLine($"target_database={builder.Database}");
        Console.WriteLine($"target_host={builder.Host}");
        Console.WriteLine($"target_port={builder.Port}");
        Console.WriteLine($"samples={sampleCount}");
        Console.WriteLine($"interval_seconds={intervalSeconds}");

        for (var sample = 1; sample <= sampleCount; sample++)
        {
            Console.WriteLine();
            Console.WriteLine($"sample={sample}");
            await RunSampleAsync(builder.ConnectionString);

            if (sample < sampleCount)
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
            }
        }

        Console.WriteLine();
        Console.WriteLine($"monitor_finished_utc={DateTimeOffset.UtcNow:O}");
        return 0;
    }

static int ReadInt(string name, int defaultValue)
{
    var raw = Environment.GetEnvironmentVariable(name);
    return int.TryParse(raw, out var value) && value > 0 ? value : defaultValue;
}

static async Task RunSampleAsync(string connectionString)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    await ExecuteNonQueryAsync(connection, transaction, "SET LOCAL transaction_read_only = on; SET LOCAL statement_timeout = '120s'; SET LOCAL lock_timeout = '2s';");

    Console.WriteLine($"sample_started_utc={DateTimeOffset.UtcNow:O}");
    await PrintRowsAsync(connection, transaction, HeartbeatSql);
    await PrintRowsAsync(connection, transaction, StrategyRowsSql);
    await PrintRowsAsync(connection, transaction, BpsCoverageSql);
    await PrintRowsAsync(connection, transaction, BpsByStrategySql);
    await PrintRowsAsync(connection, transaction, RecentOrdersSql);
    await PrintRowsAsync(connection, transaction, RecentRunsSql);
    await PrintRowsAsync(connection, transaction, LiveSafetySql);
    await PrintRowsAsync(connection, transaction, ApiErrorsSql);
    Console.WriteLine($"sample_finished_utc={DateTimeOffset.UtcNow:O}");

    await transaction.CommitAsync();
}

static async Task ExecuteNonQueryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    command.CommandTimeout = 120;
    await command.ExecuteNonQueryAsync();
}

static async Task PrintRowsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
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

const string TargetStrategiesCte = """
WITH target AS (
    SELECT id, code, name, enabled, live_stakes
    FROM strategies
    WHERE code IN (
        'btc_up_down_5m_prev_score_countertrend_fak',
        'btc_up_down_5m_prev_score_countertrend_fak_premarket',
        'btc_up_down_5m_prev_score_countertrend_fak_revert',
        'eth_up_down_5m_prev_score_countertrend_fak',
        'eth_up_down_5m_prev_score_countertrend_fak_premarket',
        'sol_up_down_5m_prev_score_countertrend_fak',
        'sol_up_down_5m_prev_score_countertrend_fak_premarket')
       OR code ~ '^btc_up_down_5m_prev_score_countertrend_[0-9]+$'
),
heartbeat AS (
    SELECT started_at_utc
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
),
orders AS (
    SELECT
        paper_order.*,
        target.code,
        target.name,
        CASE
            WHEN jsonb_typeof(paper_order.raw_decision_json -> 'previous_score_bps') = 'number'
            THEN (paper_order.raw_decision_json ->> 'previous_score_bps')::numeric
            WHEN jsonb_typeof(paper_order.raw_decision_json -> 'previous_score') = 'number'
            THEN (paper_order.raw_decision_json ->> 'previous_score')::numeric * 10000.0
            ELSE NULL
        END AS derived_score_bps,
        CASE
            WHEN jsonb_typeof(paper_order.raw_decision_json -> 'selected_signal_bps') = 'number'
            THEN (paper_order.raw_decision_json ->> 'selected_signal_bps')::numeric
            ELSE NULL
        END AS selected_signal_bps,
        paper_order.raw_decision_json ? 'previous_score_bps' AS has_score_bps,
        paper_order.raw_decision_json ? 'selected_signal_bps' AS has_selected_signal_bps
    FROM paper_orders paper_order
    JOIN target ON target.id = paper_order.strategy_id
)
""";

const string HeartbeatSql = """
SELECT
    'heartbeat' AS name,
    concat_ws('|',
        'db_now_utc=' || (now() AT TIME ZONE 'UTC')::text,
        'status=' || coalesce(status, ''),
        'mode=' || coalesce(mode, ''),
        'version=' || coalesce(version, ''),
        'started=' || coalesce(started_at_utc::text, ''),
        'last=' || coalesce(last_heartbeat_utc::text, ''),
        'age=' || coalesce((now() - last_heartbeat_utc)::text, ''),
        'loop=' || coalesce(current_loop, ''),
        'last_error=' || coalesce(last_error, '')) AS value
FROM service_heartbeats
WHERE service_name = 'PolyCopyTrader.Service';
""";

const string StrategyRowsSql = TargetStrategiesCte + """
SELECT
    'strategy_rows' AS name,
    'rows=' || count(*)::text ||
    '|enabled=' || count(*) FILTER (WHERE enabled)::text ||
    '|live=' || count(*) FILTER (WHERE live_stakes)::text AS value
FROM target
UNION ALL
SELECT
    'strategy_' || code AS name,
    'enabled=' || enabled::text || '|live=' || live_stakes::text || '|name=' || name AS value
FROM target
ORDER BY name;
""";

const string BpsCoverageSql = TargetStrategiesCte + """
SELECT
    'bps_since_service_start' AS name,
    'orders=' || count(*)::text ||
    '|with_previous_score_bps=' || count(*) FILTER (WHERE has_score_bps)::text ||
    '|with_selected_signal_bps=' || count(*) FILTER (WHERE has_selected_signal_bps)::text ||
    '|legacy_derived=' || count(*) FILTER (WHERE NOT has_score_bps AND derived_score_bps IS NOT NULL)::text ||
    '|avg_score_bps=' || coalesce(round(avg(derived_score_bps), 4)::text, '') ||
    '|avg_signal_bps=' || coalesce(round(avg(coalesce(selected_signal_bps, abs(derived_score_bps))), 4)::text, '') ||
    '|last_order=' || coalesce(max(created_at_utc)::text, '') AS value
FROM orders, heartbeat
WHERE orders.created_at_utc >= heartbeat.started_at_utc
UNION ALL
SELECT
    'bps_last_hour',
    'orders=' || count(*)::text ||
    '|with_previous_score_bps=' || count(*) FILTER (WHERE has_score_bps)::text ||
    '|with_selected_signal_bps=' || count(*) FILTER (WHERE has_selected_signal_bps)::text ||
    '|avg_score_bps=' || coalesce(round(avg(derived_score_bps), 4)::text, '') ||
    '|avg_signal_bps=' || coalesce(round(avg(coalesce(selected_signal_bps, abs(derived_score_bps))), 4)::text, '') ||
    '|last_order=' || coalesce(max(created_at_utc)::text, '')
FROM orders
WHERE created_at_utc >= now() - interval '1 hour'
UNION ALL
SELECT
    'bps_all_time',
    'orders=' || count(*)::text ||
    '|with_previous_score_bps=' || count(*) FILTER (WHERE has_score_bps)::text ||
    '|with_selected_signal_bps=' || count(*) FILTER (WHERE has_selected_signal_bps)::text ||
    '|legacy_derived=' || count(*) FILTER (WHERE NOT has_score_bps AND derived_score_bps IS NOT NULL)::text ||
    '|avg_score_bps=' || coalesce(round(avg(derived_score_bps), 4)::text, '') ||
    '|avg_signal_bps=' || coalesce(round(avg(coalesce(selected_signal_bps, abs(derived_score_bps))), 4)::text, '') ||
    '|last_order=' || coalesce(max(created_at_utc)::text, '')
FROM orders
ORDER BY name;
""";

const string BpsByStrategySql = TargetStrategiesCte + """
SELECT
    'bps_strategy_' || target.code AS name,
    'orders_since_start=' || count(orders.id) FILTER (WHERE orders.created_at_utc >= heartbeat.started_at_utc)::text ||
    '|bps_since_start=' || count(orders.id) FILTER (WHERE orders.created_at_utc >= heartbeat.started_at_utc AND orders.has_score_bps)::text ||
    '|orders_1h=' || count(orders.id) FILTER (WHERE orders.created_at_utc >= now() - interval '1 hour')::text ||
    '|avg_score_1h=' || coalesce(round(avg(orders.derived_score_bps) FILTER (WHERE orders.created_at_utc >= now() - interval '1 hour'), 4)::text, '') ||
    '|avg_signal_1h=' || coalesce(round(avg(coalesce(orders.selected_signal_bps, abs(orders.derived_score_bps))) FILTER (WHERE orders.created_at_utc >= now() - interval '1 hour'), 4)::text, '') ||
    '|last_order=' || coalesce(max(orders.created_at_utc)::text, '') AS value
FROM target
CROSS JOIN heartbeat
LEFT JOIN orders ON orders.strategy_id = target.id
GROUP BY target.code
ORDER BY target.code;
""";

const string RecentOrdersSql = TargetStrategiesCte + """
SELECT
    'recent_order_' || row_number() OVER (ORDER BY created_at_utc DESC)::text AS name,
    code ||
    '|created=' || created_at_utc::text ||
    '|status=' || status ||
    '|outcome=' || outcome ||
    '|score_bps=' || coalesce(derived_score_bps::text, '') ||
    '|signal_bps=' || coalesce(coalesce(selected_signal_bps, abs(derived_score_bps))::text, '') ||
    '|has_new_bps=' || has_score_bps::text ||
    '|bias=' || coalesce(raw_decision_json ->> 'previous_bias', '') ||
    '|selected=' || coalesce(raw_decision_json ->> 'selected_direction', '') ||
    '|mode=' || coalesce(raw_decision_json ->> 'previous_score_direction_mode', '') ||
    '|source=' || coalesce(raw_decision_json ->> 'decision_source', '') AS value
FROM orders
WHERE created_at_utc >= now() - interval '6 hours'
ORDER BY created_at_utc DESC
LIMIT 20;
""";

const string RecentRunsSql = TargetStrategiesCte + """
SELECT
    'runs_since_start' AS name,
    'rows=' || count(*)::text ||
    '|entered=' || count(*) FILTER (WHERE run.status = 'Entered')::text ||
    '|skipped=' || count(*) FILTER (WHERE run.status = 'Skipped')::text ||
    '|settled=' || count(*) FILTER (WHERE run.status = 'Settled')::text ||
    '|latest=' || coalesce(max(run.created_at_utc)::text, '') AS value
FROM strategy_market_paper_runs run
JOIN target ON target.id = run.strategy_id
CROSS JOIN heartbeat
WHERE run.created_at_utc >= heartbeat.started_at_utc
UNION ALL
SELECT
    'runs_1h',
    'rows=' || count(*)::text ||
    '|entered=' || count(*) FILTER (WHERE run.status = 'Entered')::text ||
    '|skipped=' || count(*) FILTER (WHERE run.status = 'Skipped')::text ||
    '|settled=' || count(*) FILTER (WHERE run.status = 'Settled')::text ||
    '|latest=' || coalesce(max(run.created_at_utc)::text, '')
FROM strategy_market_paper_runs run
JOIN target ON target.id = run.strategy_id
WHERE run.created_at_utc >= now() - interval '1 hour'
UNION ALL
SELECT
    'recent_run_' || row_number() OVER (ORDER BY run.created_at_utc DESC)::text,
    target.code ||
    '|created=' || run.created_at_utc::text ||
    '|entry_due=' || coalesce(run.entry_due_at_utc::text, '') ||
    '|status=' || coalesce(run.status, '') ||
    '|outcome=' || coalesce(run.selected_outcome, '') ||
    '|skip=' || coalesce(run.skip_reason, '')
FROM strategy_market_paper_runs run
JOIN target ON target.id = run.strategy_id
WHERE run.created_at_utc >= now() - interval '2 hours'
ORDER BY name
LIMIT 30;
""";

const string LiveSafetySql = TargetStrategiesCte + """
SELECT
    'live_safety_since_start' AS name,
    'all_live_orders=' || (
        SELECT count(*)::text
        FROM live_orders live_order, heartbeat
        WHERE live_order.created_at_utc >= heartbeat.started_at_utc) ||
    '|target_live_orders=' || (
        SELECT count(*)::text
        FROM live_orders live_order
        JOIN target ON target.id = live_order.strategy_id
        CROSS JOIN heartbeat
        WHERE live_order.created_at_utc >= heartbeat.started_at_utc) ||
    '|target_open_live=' || (
        SELECT count(*)::text
        FROM live_orders live_order
        JOIN target ON target.id = live_order.strategy_id
        WHERE live_order.status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested')) AS value;
""";

const string ApiErrorsSql = """
WITH heartbeat AS (
    SELECT started_at_utc
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
),
grouped AS (
    SELECT
        component,
        operation,
        left(regexp_replace(message, '\s+', ' ', 'g'), 140) AS message,
        count(*)::bigint AS rows,
        max(created_at_utc) AS latest
    FROM api_errors, heartbeat
    WHERE api_errors.created_at_utc >= heartbeat.started_at_utc
    GROUP BY component, operation, left(regexp_replace(message, '\s+', ' ', 'g'), 140)
    ORDER BY latest DESC
    LIMIT 10
)
SELECT
    'api_error_' || row_number() OVER (ORDER BY latest DESC)::text AS name,
    rows::text || '|' || component || '|' || operation || '|' || latest::text || '|' || message AS value
FROM grouped
ORDER BY latest DESC;
""";
}
