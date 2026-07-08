using System.Data;
using Npgsql;

internal static class Program
{
    private static async Task<int> Main()
    {
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

        Console.WriteLine($"probe_started_utc={DateTimeOffset.UtcNow:O}");
        Console.WriteLine($"target_database={builder.Database}");
        Console.WriteLine($"target_host={builder.Host}");
        Console.WriteLine($"target_port={builder.Port}");

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "SET LOCAL transaction_read_only = on; SET LOCAL statement_timeout = '120s'; SET LOCAL lock_timeout = '2s';");

        await PrintRowsAsync(connection, transaction, HeartbeatSql);
        await PrintRowsAsync(connection, transaction, StrategySummarySql);
        await PrintRowsAsync(connection, transaction, CoverageSummarySql);
        await PrintRowsAsync(connection, transaction, PerStrategySql);
        await PrintRowsAsync(connection, transaction, RecentOrdersSql);
        await PrintRowsAsync(connection, transaction, RunSummarySql);
        await PrintRowsAsync(connection, transaction, LiveSafetySql);

        await transaction.CommitAsync();

        Console.WriteLine($"probe_finished_utc={DateTimeOffset.UtcNow:O}");
        return 0;
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PrintRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql)
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

    private const string TargetCte = """
WITH heartbeat AS (
    SELECT started_at_utc
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
),
target AS (
    SELECT id, code, name, enabled, live_stakes
    FROM strategies
    WHERE code LIKE '%prev_score_countertrend%'
),
orders AS (
    SELECT
        paper_order.id,
        paper_order.strategy_id,
        paper_order.created_at_utc,
        paper_order.status,
        paper_order.outcome,
        target.code,
        target.name,
        target.enabled,
        target.live_stakes,
        CASE
            WHEN jsonb_typeof(paper_order.raw_decision_json -> 'previous_score_bps') = 'number'
            THEN round((paper_order.raw_decision_json ->> 'previous_score_bps')::numeric, 8)::numeric(28,8)
            WHEN jsonb_typeof(paper_order.raw_decision_json -> 'previous_score') = 'number'
            THEN round((paper_order.raw_decision_json ->> 'previous_score')::numeric * 10000, 8)::numeric(28,8)
            ELSE NULL
        END AS score_bps,
        CASE
            WHEN jsonb_typeof(paper_order.raw_decision_json -> 'selected_signal_bps') = 'number'
            THEN round((paper_order.raw_decision_json ->> 'selected_signal_bps')::numeric, 8)::numeric(28,8)
            ELSE NULL
        END AS selected_signal_bps,
        paper_order.raw_decision_json ? 'previous_score_bps' AS has_score_bps,
        paper_order.raw_decision_json ? 'selected_signal_bps' AS has_selected_signal_bps,
        coalesce(paper_order.raw_decision_json ->> 'previous_score_direction_mode', '') AS direction_mode,
        coalesce(paper_order.raw_decision_json ->> 'decision_source', '') AS decision_source
    FROM paper_orders paper_order
    JOIN target ON target.id = paper_order.strategy_id
)
""";

    private const string HeartbeatSql = """
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

    private const string StrategySummarySql = TargetCte + """
SELECT
    'strategy_summary' AS name,
    'rows=' || count(*)::text ||
    '|enabled=' || count(*) FILTER (WHERE enabled)::text ||
    '|live=' || count(*) FILTER (WHERE live_stakes)::text AS value
FROM target
UNION ALL
SELECT
    'strategy_enabled_' || code,
    'enabled=' || enabled::text || '|live=' || live_stakes::text || '|name=' || name
FROM target
WHERE enabled OR live_stakes
ORDER BY name;
""";

    private const string CoverageSummarySql = TargetCte + """
SELECT
    'coverage_since_start' AS name,
    'orders=' || count(*)::text ||
    '|with_previous_score_bps=' || count(*) FILTER (WHERE has_score_bps)::text ||
    '|with_selected_signal_bps=' || count(*) FILTER (WHERE has_selected_signal_bps)::text ||
    '|legacy_derived=' || count(*) FILTER (WHERE NOT has_score_bps AND score_bps IS NOT NULL)::text ||
    '|avg_score_bps=' || coalesce(round(avg(score_bps), 4)::text, '') ||
    '|avg_signal_bps=' || coalesce(round(avg(coalesce(selected_signal_bps, abs(score_bps))), 4)::text, '') ||
    '|last_order=' || coalesce(max(created_at_utc)::text, '') AS value
FROM orders, heartbeat
WHERE orders.created_at_utc >= heartbeat.started_at_utc
UNION ALL
SELECT
    'coverage_last_hour',
    'orders=' || count(*)::text ||
    '|with_previous_score_bps=' || count(*) FILTER (WHERE has_score_bps)::text ||
    '|with_selected_signal_bps=' || count(*) FILTER (WHERE has_selected_signal_bps)::text ||
    '|avg_score_bps=' || coalesce(round(avg(score_bps), 4)::text, '') ||
    '|avg_signal_bps=' || coalesce(round(avg(coalesce(selected_signal_bps, abs(score_bps))), 4)::text, '') ||
    '|last_order=' || coalesce(max(created_at_utc)::text, '')
FROM orders
WHERE created_at_utc >= now() - interval '1 hour'
UNION ALL
SELECT
    'coverage_all_time',
    'orders=' || count(*)::text ||
    '|with_previous_score_bps=' || count(*) FILTER (WHERE has_score_bps)::text ||
    '|with_selected_signal_bps=' || count(*) FILTER (WHERE has_selected_signal_bps)::text ||
    '|legacy_derived=' || count(*) FILTER (WHERE NOT has_score_bps AND score_bps IS NOT NULL)::text ||
    '|avg_score_bps=' || coalesce(round(avg(score_bps), 4)::text, '') ||
    '|avg_signal_bps=' || coalesce(round(avg(coalesce(selected_signal_bps, abs(score_bps))), 4)::text, '') ||
    '|last_order=' || coalesce(max(created_at_utc)::text, '')
FROM orders
ORDER BY name;
""";

    private const string PerStrategySql = TargetCte + """
SELECT
    'per_strategy_' || target.code AS name,
    'enabled=' || target.enabled::text ||
    '|live=' || target.live_stakes::text ||
    '|orders_since_start=' || count(orders.id) FILTER (WHERE orders.created_at_utc >= heartbeat.started_at_utc)::text ||
    '|bps_since_start=' || count(orders.id) FILTER (WHERE orders.created_at_utc >= heartbeat.started_at_utc AND orders.has_score_bps)::text ||
    '|orders_1h=' || count(orders.id) FILTER (WHERE orders.created_at_utc >= now() - interval '1 hour')::text ||
    '|avg_signal_1h=' || coalesce(round(avg(coalesce(orders.selected_signal_bps, abs(orders.score_bps))) FILTER (WHERE orders.created_at_utc >= now() - interval '1 hour'), 4)::text, '') ||
    '|last_signal=' || coalesce((array_agg(coalesce(orders.selected_signal_bps, abs(orders.score_bps)) ORDER BY orders.created_at_utc DESC) FILTER (WHERE orders.score_bps IS NOT NULL))[1]::text, '') ||
    '|last_order=' || coalesce(max(orders.created_at_utc)::text, '') AS value
FROM target
CROSS JOIN heartbeat
LEFT JOIN orders ON orders.strategy_id = target.id
GROUP BY target.code, target.enabled, target.live_stakes
ORDER BY target.code;
""";

    private const string RecentOrdersSql = TargetCte + """
SELECT
    'recent_order_' || row_number() OVER (ORDER BY created_at_utc DESC)::text AS name,
    code ||
    '|created=' || created_at_utc::text ||
    '|status=' || status ||
    '|outcome=' || outcome ||
    '|score_bps=' || coalesce(score_bps::text, '') ||
    '|signal_bps=' || coalesce(coalesce(selected_signal_bps, abs(score_bps))::text, '') ||
    '|has_new_bps=' || has_score_bps::text ||
    '|mode=' || direction_mode ||
    '|source=' || decision_source AS value
FROM orders
WHERE created_at_utc >= now() - interval '1 hour'
ORDER BY created_at_utc DESC
LIMIT 20;
""";

    private const string RunSummarySql = TargetCte + """
SELECT
    'runs_since_start' AS name,
    'rows=' || count(*)::text ||
    '|entered=' || count(*) FILTER (WHERE run.status = 'Entered')::text ||
    '|skipped=' || count(*) FILTER (WHERE run.status = 'Skipped')::text ||
    '|settled=' || count(*) FILTER (WHERE run.status = 'Settled')::text ||
    '|observed=' || count(*) FILTER (WHERE run.status = 'Observed')::text ||
    '|latest=' || coalesce(max(run.created_at_utc)::text, '') AS value
FROM strategy_market_paper_runs run
JOIN target ON target.id = run.strategy_id
CROSS JOIN heartbeat
WHERE run.created_at_utc >= heartbeat.started_at_utc
UNION ALL
SELECT
    'runs_last_hour',
    'rows=' || count(*)::text ||
    '|entered=' || count(*) FILTER (WHERE run.status = 'Entered')::text ||
    '|skipped=' || count(*) FILTER (WHERE run.status = 'Skipped')::text ||
    '|settled=' || count(*) FILTER (WHERE run.status = 'Settled')::text ||
    '|observed=' || count(*) FILTER (WHERE run.status = 'Observed')::text ||
    '|latest=' || coalesce(max(run.created_at_utc)::text, '')
FROM strategy_market_paper_runs run
JOIN target ON target.id = run.strategy_id
WHERE run.created_at_utc >= now() - interval '1 hour'
ORDER BY name;
""";

    private const string LiveSafetySql = TargetCte + """
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
}
