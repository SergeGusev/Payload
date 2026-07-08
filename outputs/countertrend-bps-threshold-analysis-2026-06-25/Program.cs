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

        Console.WriteLine($"analysis_started_utc={DateTimeOffset.UtcNow:O}");
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

        await PrintRowsAsync(connection, transaction, DataSummarySql);
        await PrintRowsAsync(connection, transaction, FamilySummarySql);
        await PrintRowsAsync(connection, transaction, ThresholdOverallSql);
        await PrintRowsAsync(connection, transaction, ThresholdByFamilySql);
        await PrintRowsAsync(connection, transaction, BucketByFamilySql);
        await PrintRowsAsync(connection, transaction, RecentWindowSql);

        await transaction.CommitAsync();

        Console.WriteLine($"analysis_finished_utc={DateTimeOffset.UtcNow:O}");
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

    private const string BaseCte = """
WITH heartbeat AS (
    SELECT started_at_utc
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
),
target AS (
    SELECT id, code, name
    FROM strategies
    WHERE code LIKE '%prev_score_countertrend%'
),
raw_runs AS (
    SELECT
        run.id AS run_id,
        run.strategy_id,
        target.code,
        target.name,
        upper(split_part(target.code, '_', 1)) AS asset,
        CASE
            WHEN target.code ~ '^btc_up_down_5m_prev_score_countertrend_[0-9]+$'
            THEN 'fixed_price_countertrend'
            WHEN target.code LIKE '%premarket%revert%'
            THEN 'premarket_revert'
            WHEN target.code LIKE '%premarket%'
            THEN 'premarket_countertrend'
            WHEN target.code LIKE '%revert%'
            THEN 'regular_revert'
            ELSE 'regular_countertrend'
        END AS family,
        run.market_start_utc,
        run.entered_at_utc,
        run.settled_at_utc,
        run.entry_price,
        run.stake_usd,
        run.realized_pnl_usd,
        paper_order.notional_usd,
        paper_order.status AS order_status,
        paper_order.raw_decision_json,
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
        END AS selected_signal_bps
    FROM strategy_market_paper_runs run
    JOIN target ON target.id = run.strategy_id
    JOIN paper_orders paper_order ON paper_order.id = run.paper_order_id
    WHERE run.status = 'Settled'
      AND run.settled_at_utc IS NOT NULL
      AND run.realized_pnl_usd IS NOT NULL
),
settled AS (
    SELECT
        *,
        round(COALESCE(selected_signal_bps, abs(score_bps)), 8)::numeric(28,8) AS signal_bps,
        COALESCE(NULLIF(stake_usd, 0), NULLIF(notional_usd, 0), 0) AS stake_base_usd,
        settled_at_utc >= (SELECT started_at_utc FROM heartbeat) AS since_service_start
    FROM raw_runs
    WHERE score_bps IS NOT NULL
),
thresholds AS (
    SELECT *
    FROM (VALUES
        (0::numeric),
        (1::numeric),
        (2::numeric),
        (3::numeric),
        (4::numeric),
        (5::numeric),
        (6::numeric),
        (8::numeric),
        (10::numeric),
        (12::numeric),
        (15::numeric),
        (20::numeric),
        (25::numeric)
    ) AS threshold(value)
),
windows AS (
    SELECT 'all_time' AS window_name, '-infinity'::timestamptz AS window_start_utc
    UNION ALL
    SELECT 'since_service_start', started_at_utc FROM heartbeat
    UNION ALL
    SELECT 'last_6h', now() - interval '6 hours'
    UNION ALL
    SELECT 'last_2h', now() - interval '2 hours'
)
""";

    private const string DataSummarySql = BaseCte + """
SELECT
    'data_summary_' || window_name AS name,
    'settled=' || count(*)::text ||
    '|won=' || count(*) FILTER (WHERE realized_pnl_usd > 0)::text ||
    '|lost=' || count(*) FILTER (WHERE realized_pnl_usd < 0)::text ||
    '|flat=' || count(*) FILTER (WHERE realized_pnl_usd = 0)::text ||
    '|win_rate=' || coalesce(round(count(*) FILTER (WHERE realized_pnl_usd > 0)::numeric * 100 / nullif(count(*), 0), 2)::text, '') ||
    '|pnl=' || coalesce(round(sum(realized_pnl_usd), 4)::text, '') ||
    '|stake=' || coalesce(round(sum(stake_base_usd), 4)::text, '') ||
    '|roi=' || coalesce(round(sum(realized_pnl_usd) * 100 / nullif(sum(stake_base_usd), 0), 2)::text, '') ||
    '|avg_signal=' || coalesce(round(avg(signal_bps), 4)::text, '') ||
    '|p50_signal=' || coalesce(round(percentile_cont(0.50) WITHIN GROUP (ORDER BY signal_bps)::numeric, 4)::text, '') ||
    '|p75_signal=' || coalesce(round(percentile_cont(0.75) WITHIN GROUP (ORDER BY signal_bps)::numeric, 4)::text, '') ||
    '|p90_signal=' || coalesce(round(percentile_cont(0.90) WITHIN GROUP (ORDER BY signal_bps)::numeric, 4)::text, '') ||
    '|max_signal=' || coalesce(round(max(signal_bps), 4)::text, '') AS value
FROM windows
LEFT JOIN settled ON settled.settled_at_utc >= windows.window_start_utc
GROUP BY window_name
ORDER BY window_name;
""";

    private const string FamilySummarySql = BaseCte + """
SELECT
    'family_summary_' || window_name || '_' || asset || '_' || family AS name,
    'settled=' || count(*)::text ||
    '|win_rate=' || coalesce(round(count(*) FILTER (WHERE realized_pnl_usd > 0)::numeric * 100 / nullif(count(*), 0), 2)::text, '') ||
    '|pnl=' || coalesce(round(sum(realized_pnl_usd), 4)::text, '') ||
    '|roi=' || coalesce(round(sum(realized_pnl_usd) * 100 / nullif(sum(stake_base_usd), 0), 2)::text, '') ||
    '|avg_signal=' || coalesce(round(avg(signal_bps), 4)::text, '') ||
    '|p75_signal=' || coalesce(round(percentile_cont(0.75) WITHIN GROUP (ORDER BY signal_bps)::numeric, 4)::text, '') ||
    '|last_settled=' || coalesce(max(settled_at_utc)::text, '') AS value
FROM windows
JOIN settled ON settled.settled_at_utc >= windows.window_start_utc
GROUP BY window_name, asset, family
HAVING count(*) >= 10
ORDER BY window_name, asset, family;
""";

    private const string ThresholdOverallSql = BaseCte + """
SELECT
    'threshold_overall_' || window_name || '_N' || replace(thresholds.value::text, '.', '_') AS name,
    'kept=' || count(settled.run_id)::text ||
    '|win_rate=' || coalesce(round(count(*) FILTER (WHERE settled.realized_pnl_usd > 0)::numeric * 100 / nullif(count(settled.run_id), 0), 2)::text, '') ||
    '|pnl=' || coalesce(round(sum(settled.realized_pnl_usd), 4)::text, '') ||
    '|stake=' || coalesce(round(sum(settled.stake_base_usd), 4)::text, '') ||
    '|roi=' || coalesce(round(sum(settled.realized_pnl_usd) * 100 / nullif(sum(settled.stake_base_usd), 0), 2)::text, '') ||
    '|avg_signal=' || coalesce(round(avg(settled.signal_bps), 4)::text, '') AS value
FROM windows
CROSS JOIN thresholds
LEFT JOIN settled ON settled.settled_at_utc >= windows.window_start_utc
    AND settled.signal_bps >= thresholds.value
GROUP BY window_name, thresholds.value
ORDER BY window_name, thresholds.value;
""";

    private const string ThresholdByFamilySql = BaseCte + """
SELECT
    'threshold_family_' || scope.window_name || '_' || scope.asset || '_' || scope.family || '_N' || replace(thresholds.value::text, '.', '_') AS name,
    'kept=' || count(settled.run_id)::text ||
    '|win_rate=' || coalesce(round(count(*) FILTER (WHERE settled.realized_pnl_usd > 0)::numeric * 100 / nullif(count(settled.run_id), 0), 2)::text, '') ||
    '|pnl=' || coalesce(round(sum(settled.realized_pnl_usd), 4)::text, '') ||
    '|roi=' || coalesce(round(sum(settled.realized_pnl_usd) * 100 / nullif(sum(settled.stake_base_usd), 0), 2)::text, '') ||
    '|avg_signal=' || coalesce(round(avg(settled.signal_bps), 4)::text, '') AS value
FROM (
    SELECT DISTINCT window_name, window_start_utc, asset, family
    FROM windows
    CROSS JOIN (SELECT DISTINCT asset, family FROM settled) families
) scope
CROSS JOIN thresholds
LEFT JOIN settled ON settled.settled_at_utc >= scope.window_start_utc
    AND settled.asset = scope.asset
    AND settled.family = scope.family
    AND settled.signal_bps >= thresholds.value
GROUP BY scope.window_name, scope.asset, scope.family, thresholds.value
HAVING count(settled.run_id) >= 8
ORDER BY scope.window_name, scope.asset, scope.family, thresholds.value;
""";

    private const string BucketByFamilySql = BaseCte + """
SELECT
    'bucket_' || window_name || '_' || asset || '_' || family || '_' || bucket AS name,
    'settled=' || count(*)::text ||
    '|win_rate=' || coalesce(round(count(*) FILTER (WHERE realized_pnl_usd > 0)::numeric * 100 / nullif(count(*), 0), 2)::text, '') ||
    '|pnl=' || coalesce(round(sum(realized_pnl_usd), 4)::text, '') ||
    '|roi=' || coalesce(round(sum(realized_pnl_usd) * 100 / nullif(sum(stake_base_usd), 0), 2)::text, '') ||
    '|avg_signal=' || coalesce(round(avg(signal_bps), 4)::text, '') AS value
FROM (
    SELECT
        windows.window_name,
        settled.*,
        CASE
            WHEN signal_bps < 2 THEN '00_02'
            WHEN signal_bps < 4 THEN '02_04'
            WHEN signal_bps < 6 THEN '04_06'
            WHEN signal_bps < 8 THEN '06_08'
            WHEN signal_bps < 10 THEN '08_10'
            WHEN signal_bps < 15 THEN '10_15'
            WHEN signal_bps < 20 THEN '15_20'
            ELSE '20_plus'
        END AS bucket
    FROM windows
    JOIN settled ON settled.settled_at_utc >= windows.window_start_utc
) bucketed
GROUP BY window_name, asset, family, bucket
HAVING count(*) >= 5
ORDER BY window_name, asset, family, bucket;
""";

    private const string RecentWindowSql = BaseCte + """
SELECT
    'recent_direction_check_' || window_name || '_' || asset || '_' || family AS name,
    'settled=' || count(*)::text ||
    '|up_score_rows=' || count(*) FILTER (WHERE score_bps > 0)::text ||
    '|down_score_rows=' || count(*) FILTER (WHERE score_bps < 0)::text ||
    '|up_score_win_rate=' || coalesce(round(count(*) FILTER (WHERE score_bps > 0 AND realized_pnl_usd > 0)::numeric * 100 / nullif(count(*) FILTER (WHERE score_bps > 0), 0), 2)::text, '') ||
    '|down_score_win_rate=' || coalesce(round(count(*) FILTER (WHERE score_bps < 0 AND realized_pnl_usd > 0)::numeric * 100 / nullif(count(*) FILTER (WHERE score_bps < 0), 0), 2)::text, '') ||
    '|up_score_pnl=' || coalesce(round(sum(realized_pnl_usd) FILTER (WHERE score_bps > 0), 4)::text, '') ||
    '|down_score_pnl=' || coalesce(round(sum(realized_pnl_usd) FILTER (WHERE score_bps < 0), 4)::text, '') AS value
FROM windows
JOIN settled ON settled.settled_at_utc >= windows.window_start_utc
GROUP BY window_name, asset, family
HAVING count(*) >= 10
ORDER BY window_name, asset, family;
""";
}
