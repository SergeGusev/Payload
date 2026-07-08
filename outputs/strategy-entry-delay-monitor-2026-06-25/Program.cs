using System.Data;
using Npgsql;

internal static class Program
{
    private static async Task<int> Main()
    {
        var sampleCount = ReadInt("POLYCOPYTRADER_ENTRY_DELAY_SAMPLES", 2);
        var intervalSeconds = ReadInt("POLYCOPYTRADER_ENTRY_DELAY_INTERVAL_SECONDS", 60);
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

    private static int ReadInt(string name, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) && value > 0 ? value : defaultValue;
    }

    private static async Task RunSampleAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await ExecuteNonQueryAsync(connection, transaction, "SET LOCAL transaction_read_only = on; SET LOCAL statement_timeout = '120s'; SET LOCAL lock_timeout = '2s';");

        Console.WriteLine($"sample_started_utc={DateTimeOffset.UtcNow:O}");
        await PrintRowsAsync(connection, transaction, HeartbeatSql);
        await PrintRowsAsync(connection, transaction, ActivitySql);
        await PrintRowsAsync(connection, transaction, DelaySummarySql);
        await PrintRowsAsync(connection, transaction, WaveSummarySql);
        await PrintRowsAsync(connection, transaction, OverdueObservedSql);
        await PrintRowsAsync(connection, transaction, WindowSkipSql);
        await PrintRowsAsync(connection, transaction, WindowSkipDetailsSql);
        await PrintRowsAsync(connection, transaction, WorstEntriesSql);
        await PrintRowsAsync(connection, transaction, RecentStatusSql);
        await PrintRowsAsync(connection, transaction, LiveOrdersSql);
        Console.WriteLine($"sample_finished_utc={DateTimeOffset.UtcNow:O}");

        await transaction.CommitAsync();
    }

    private static async Task ExecuteNonQueryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PrintRowsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var value = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            Console.WriteLine($"{name}={value}");
        }
    }

    private const string HeartbeatSql = """
SELECT
    'heartbeat' AS name,
    'status=' || status ||
    '|mode=' || mode ||
    '|started=' || started_at_utc::text ||
    '|last=' || last_heartbeat_utc::text ||
    '|age_s=' || round(EXTRACT(EPOCH FROM (now() - last_heartbeat_utc))::numeric, 3)::text ||
    '|loop=' || current_loop ||
    '|last_error=' || COALESCE(left(regexp_replace(last_error, '\s+', ' ', 'g'), 180), '') AS value
FROM service_heartbeats
WHERE service_name = 'PolyCopyTrader.Service';
""";

    private const string ActivitySql = """
WITH bounds AS (
    SELECT now() AS now_utc
),
run_rows AS (
    SELECT
        run.status,
        run.created_at_utc,
        run.updated_at_utc,
        run.entry_due_at_utc,
        run.entered_at_utc
    FROM strategy_market_paper_runs run
),
paper_order_rows AS (
    SELECT created_at_utc
    FROM paper_orders
),
live_order_rows AS (
    SELECT created_at_utc
    FROM live_orders
)
SELECT
    'activity_runs' AS name,
    'last_created=' || COALESCE(max(created_at_utc)::text, '') ||
    '|last_updated=' || COALESCE(max(updated_at_utc)::text, '') ||
    '|last_entry_due=' || COALESCE(max(entry_due_at_utc)::text, '') ||
    '|last_entered=' || COALESCE(max(entered_at_utc)::text, '') ||
    '|created_15m=' || count(*) FILTER (WHERE created_at_utc >= (SELECT now_utc FROM bounds) - interval '15 minutes')::text ||
    '|created_60m=' || count(*) FILTER (WHERE created_at_utc >= (SELECT now_utc FROM bounds) - interval '60 minutes')::text ||
    '|created_120m=' || count(*) FILTER (WHERE created_at_utc >= (SELECT now_utc FROM bounds) - interval '120 minutes')::text ||
    '|created_12h=' || count(*) FILTER (WHERE created_at_utc >= (SELECT now_utc FROM bounds) - interval '12 hours')::text ||
    '|entered_12h=' || count(*) FILTER (WHERE entered_at_utc >= (SELECT now_utc FROM bounds) - interval '12 hours')::text
FROM run_rows
UNION ALL
SELECT
    'activity_orders',
    'paper_last_created=' || COALESCE((SELECT max(created_at_utc)::text FROM paper_order_rows), '') ||
    '|paper_created_120m=' || (SELECT count(*)::text FROM paper_order_rows WHERE created_at_utc >= (SELECT now_utc FROM bounds) - interval '120 minutes') ||
    '|paper_created_12h=' || (SELECT count(*)::text FROM paper_order_rows WHERE created_at_utc >= (SELECT now_utc FROM bounds) - interval '12 hours') ||
    '|live_last_created=' || COALESCE((SELECT max(created_at_utc)::text FROM live_order_rows), '') ||
    '|live_created_120m=' || (SELECT count(*)::text FROM live_order_rows WHERE created_at_utc >= (SELECT now_utc FROM bounds) - interval '120 minutes') ||
    '|live_created_12h=' || (SELECT count(*)::text FROM live_order_rows WHERE created_at_utc >= (SELECT now_utc FROM bounds) - interval '12 hours');
""";

    private const string DelaySummarySql = """
WITH windows(minutes, label) AS (
    VALUES (15, '15m'), (30, '30m'), (60, '60m'), (120, '120m')
),
entered AS (
    SELECT
        strategy.code,
        strategy.name,
        run.entry_due_at_utc,
        run.entered_at_utc,
        run.market_start_utc,
        run.market_end_utc,
        EXTRACT(EPOCH FROM (run.entered_at_utc - run.entry_due_at_utc)) AS signed_delay_s,
        GREATEST(0, EXTRACT(EPOCH FROM (run.entered_at_utc - run.entry_due_at_utc))) AS positive_delay_s,
        lower(COALESCE(strategy.code, '') || ' ' || COALESCE(strategy.name, '')) LIKE '%premarket%' AS is_premarket
    FROM strategy_market_paper_runs run
    INNER JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE run.entered_at_utc IS NOT NULL
      AND run.entry_due_at_utc >= now() - interval '130 minutes'
)
SELECT
    'delay_' || windows.label AS name,
    'entered=' || count(entered.entry_due_at_utc)::text ||
    '|avg_s=' || COALESCE(round(avg(entered.positive_delay_s)::numeric, 3)::text, '') ||
    '|p95_s=' || COALESCE(round((percentile_cont(0.95) WITHIN GROUP (ORDER BY entered.positive_delay_s::double precision))::numeric, 3)::text, '') ||
    '|max_s=' || COALESCE(round(max(entered.signed_delay_s)::numeric, 3)::text, '') ||
    '|early=' || count(*) FILTER (WHERE entered.signed_delay_s < 0)::text ||
    '|over5=' || count(*) FILTER (WHERE entered.signed_delay_s > 5)::text ||
    '|over10=' || count(*) FILTER (WHERE entered.signed_delay_s > 10)::text ||
    '|over30=' || count(*) FILTER (WHERE entered.signed_delay_s > 30)::text ||
    '|over60=' || count(*) FILTER (WHERE entered.signed_delay_s > 60)::text ||
    '|premarket_after_start=' || count(*) FILTER (
        WHERE entered.is_premarket
          AND entered.market_start_utc IS NOT NULL
          AND entered.entered_at_utc > entered.market_start_utc
    )::text ||
    '|after_market_end=' || count(*) FILTER (
        WHERE entered.market_end_utc IS NOT NULL
          AND entered.entered_at_utc > entered.market_end_utc
    )::text ||
    '|latest_entered=' || COALESCE(max(entered.entered_at_utc)::text, '') AS value
FROM windows
LEFT JOIN entered ON entered.entry_due_at_utc >= now() - (windows.minutes * interval '1 minute')
GROUP BY windows.minutes, windows.label
ORDER BY windows.minutes;
""";

    private const string WaveSummarySql = """
WITH entered AS (
    SELECT
        run.entry_due_at_utc,
        run.entered_at_utc,
        run.market_start_utc,
        run.market_end_utc,
        strategy.code,
        strategy.name,
        lower(COALESCE(strategy.code, '') || ' ' || COALESCE(strategy.name, '')) LIKE '%premarket%' AS is_premarket,
        EXTRACT(EPOCH FROM (run.entered_at_utc - run.entry_due_at_utc)) AS signed_delay_s
    FROM strategy_market_paper_runs run
    INNER JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE run.entered_at_utc IS NOT NULL
      AND run.entry_due_at_utc >= now() - interval '120 minutes'
),
waves AS (
    SELECT
        entry_due_at_utc,
        count(*) AS rows,
        min(entered_at_utc) AS first_entered,
        max(entered_at_utc) AS last_entered,
        EXTRACT(EPOCH FROM (max(entered_at_utc) - min(entered_at_utc))) AS span_s,
        max(signed_delay_s) AS max_delay_s,
        count(*) FILTER (WHERE signed_delay_s > 10) AS over10,
        count(*) FILTER (WHERE signed_delay_s > 30) AS over30,
        count(*) FILTER (WHERE signed_delay_s > 60) AS over60,
        count(*) FILTER (
            WHERE is_premarket
              AND market_start_utc IS NOT NULL
              AND entered_at_utc > market_start_utc
        ) AS premarket_after_start,
        count(*) FILTER (
            WHERE market_end_utc IS NOT NULL
              AND entered_at_utc > market_end_utc
        ) AS after_market_end,
        string_agg(code, ',' ORDER BY entered_at_utc) AS codes
    FROM entered
    GROUP BY entry_due_at_utc
)
SELECT
    'worst_wave_' || row_number() OVER (ORDER BY max_delay_s DESC, entry_due_at_utc DESC)::text AS name,
    'due=' || entry_due_at_utc::text ||
    '|rows=' || rows::text ||
    '|span_s=' || round(span_s::numeric, 3)::text ||
    '|max_delay_s=' || round(max_delay_s::numeric, 3)::text ||
    '|over10=' || over10::text ||
    '|over30=' || over30::text ||
    '|over60=' || over60::text ||
    '|premarket_after_start=' || premarket_after_start::text ||
    '|after_market_end=' || after_market_end::text ||
    '|first=' || first_entered::text ||
    '|last=' || last_entered::text ||
    '|codes=' || left(codes, 240) AS value
FROM waves
ORDER BY max_delay_s DESC, entry_due_at_utc DESC
LIMIT 12;
""";

    private const string OverdueObservedSql = """
WITH observed AS (
    SELECT
        strategy.code,
        strategy.name,
        run.entry_due_at_utc,
        run.created_at_utc,
        run.updated_at_utc,
        EXTRACT(EPOCH FROM (now() - run.entry_due_at_utc)) AS overdue_s
    FROM strategy_market_paper_runs run
    INNER JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE run.status = 'Observed'
      AND run.entry_due_at_utc >= now() - interval '120 minutes'
),
summary AS (
    SELECT
        count(*) FILTER (WHERE entry_due_at_utc < now() - interval '5 seconds') AS overdue_rows,
        round(max(overdue_s) FILTER (WHERE entry_due_at_utc < now() - interval '5 seconds')::numeric, 3) AS max_overdue_s,
        min(entry_due_at_utc) FILTER (WHERE entry_due_at_utc < now() - interval '5 seconds') AS oldest_due,
        count(*) FILTER (WHERE entry_due_at_utc BETWEEN now() AND now() + interval '10 minutes') AS upcoming_rows,
        min(entry_due_at_utc) FILTER (WHERE entry_due_at_utc >= now()) AS next_due,
        round(EXTRACT(EPOCH FROM (min(entry_due_at_utc) FILTER (WHERE entry_due_at_utc >= now()) - now()))::numeric, 3) AS next_due_in_s
    FROM observed
)
SELECT
    'observed_000_summary' AS name,
    'overdue=' || overdue_rows::text ||
    '|max_overdue_s=' || COALESCE(max_overdue_s::text, '') ||
    '|oldest_due=' || COALESCE(oldest_due::text, '') ||
    '|upcoming_10m=' || upcoming_rows::text ||
    '|next_due=' || COALESCE(next_due::text, '') ||
    '|next_due_in_s=' || COALESCE(next_due_in_s::text, '') AS value
FROM summary
UNION ALL
SELECT
    'observed_' || lpad(row_number() OVER (ORDER BY overdue_s DESC)::text, 3, '0') || '_overdue',
    'overdue_s=' || round(overdue_s::numeric, 3)::text ||
    '|due=' || entry_due_at_utc::text ||
    '|created=' || created_at_utc::text ||
    '|updated=' || updated_at_utc::text ||
    '|code=' || code ||
    '|name=' || left(name, 100)
FROM observed
WHERE entry_due_at_utc < now() - interval '5 seconds'
ORDER BY name
LIMIT 13;
""";

    private const string WindowSkipSql = """
WITH skipped AS (
    SELECT
        run.skip_reason,
        run.entry_due_at_utc,
        run.updated_at_utc,
        strategy.code
    FROM strategy_market_paper_runs run
    INNER JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE run.status = 'Skipped'
      AND run.entry_due_at_utc >= now() - interval '120 minutes'
),
window_skips AS (
    SELECT
        COALESCE(skip_reason, '') AS reason,
        count(*) AS rows,
        max(updated_at_utc) AS latest
    FROM skipped
    WHERE lower(COALESCE(skip_reason, '')) IN (
        'entry_due_already_passed',
        'entry_due_expired',
        'preopen_entry_window_elapsed'
    )
       OR lower(COALESCE(skip_reason, '')) LIKE '%already_passed%'
       OR lower(COALESCE(skip_reason, '')) LIKE '%expired%'
       OR lower(COALESCE(skip_reason, '')) LIKE '%elapsed%'
       OR lower(COALESCE(skip_reason, '')) LIKE '%late%'
    GROUP BY COALESCE(skip_reason, '')
),
top_skips AS (
    SELECT
        COALESCE(skip_reason, '') AS reason,
        count(*) AS rows,
        max(updated_at_utc) AS latest
    FROM skipped
    GROUP BY COALESCE(skip_reason, '')
),
top_skip_ranked AS (
    SELECT
        reason,
        rows,
        latest,
        row_number() OVER (ORDER BY rows DESC, reason) AS skip_rank
    FROM top_skips
)
SELECT
    'window_skips_120m' AS name,
    'rows=' || COALESCE(sum(rows), 0)::text ||
    '|reasons=' || COALESCE(string_agg(reason || ':' || rows::text || '@' || latest::text, '; ' ORDER BY rows DESC, reason), '') AS value
FROM window_skips
UNION ALL
SELECT
    'top_skip_' || skip_rank::text,
    'rows=' || rows::text ||
    '|latest=' || latest::text ||
    '|reason=' || reason
FROM top_skip_ranked
WHERE skip_rank <= 12
ORDER BY name;
""";

    private const string WorstEntriesSql = """
WITH entered AS (
    SELECT
        strategy.code,
        strategy.name,
        run.entry_due_at_utc,
        run.entered_at_utc,
        run.market_start_utc,
        run.market_end_utc,
        run.status,
        run.selected_outcome,
        run.entry_price,
        EXTRACT(EPOCH FROM (run.entered_at_utc - run.entry_due_at_utc)) AS signed_delay_s,
        lower(COALESCE(strategy.code, '') || ' ' || COALESCE(strategy.name, '')) LIKE '%premarket%' AS is_premarket
    FROM strategy_market_paper_runs run
    INNER JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE run.entered_at_utc IS NOT NULL
      AND run.entry_due_at_utc >= now() - interval '120 minutes'
)
SELECT
    'worst_entry_' || row_number() OVER (ORDER BY signed_delay_s DESC, entered_at_utc DESC)::text AS name,
    'delay_s=' || round(signed_delay_s::numeric, 3)::text ||
    '|due=' || entry_due_at_utc::text ||
    '|entered=' || entered_at_utc::text ||
    '|market_start=' || COALESCE(market_start_utc::text, '') ||
    '|market_end=' || COALESCE(market_end_utc::text, '') ||
    '|premarket_after_start=' || (is_premarket AND market_start_utc IS NOT NULL AND entered_at_utc > market_start_utc)::text ||
    '|after_market_end=' || (market_end_utc IS NOT NULL AND entered_at_utc > market_end_utc)::text ||
    '|outcome=' || COALESCE(selected_outcome, '') ||
    '|price=' || COALESCE(entry_price::text, '') ||
    '|code=' || code ||
    '|name=' || left(name, 100) AS value
FROM entered
ORDER BY signed_delay_s DESC, entered_at_utc DESC
LIMIT 12;
""";

    private const string WindowSkipDetailsSql = """
WITH skipped AS (
    SELECT
        strategy.code,
        strategy.name,
        run.skip_reason,
        run.entry_due_at_utc,
        run.updated_at_utc,
        run.market_start_utc,
        run.market_end_utc,
        EXTRACT(EPOCH FROM (run.updated_at_utc - run.entry_due_at_utc)) AS skip_delay_s
    FROM strategy_market_paper_runs run
    INNER JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE run.status = 'Skipped'
      AND run.entry_due_at_utc >= now() - interval '120 minutes'
      AND (
          lower(COALESCE(run.skip_reason, '')) IN (
              'entry_due_already_passed',
              'entry_due_expired',
              'preopen_entry_window_elapsed'
          )
          OR lower(COALESCE(run.skip_reason, '')) LIKE '%already_passed%'
          OR lower(COALESCE(run.skip_reason, '')) LIKE '%expired%'
          OR lower(COALESCE(run.skip_reason, '')) LIKE '%elapsed%'
          OR lower(COALESCE(run.skip_reason, '')) LIKE '%late%'
      )
),
by_code AS (
    SELECT
        code,
        name,
        skip_reason,
        count(*) AS rows,
        round(avg(skip_delay_s)::numeric, 3) AS avg_skip_delay_s,
        round(max(skip_delay_s)::numeric, 3) AS max_skip_delay_s,
        min(entry_due_at_utc) AS first_due,
        max(entry_due_at_utc) AS last_due,
        max(updated_at_utc) AS latest
    FROM skipped
    GROUP BY code, name, skip_reason
),
by_code_ranked AS (
    SELECT
        *,
        row_number() OVER (ORDER BY rows DESC, latest DESC, code) AS code_rank
    FROM by_code
),
recent AS (
    SELECT
        *,
        row_number() OVER (ORDER BY updated_at_utc DESC, code) AS recent_rank
    FROM skipped
)
SELECT
    'window_skip_code_' || code_rank::text AS name,
    'rows=' || rows::text ||
    '|avg_skip_delay_s=' || COALESCE(avg_skip_delay_s::text, '') ||
    '|max_skip_delay_s=' || COALESCE(max_skip_delay_s::text, '') ||
    '|first_due=' || first_due::text ||
    '|last_due=' || last_due::text ||
    '|latest=' || latest::text ||
    '|reason=' || skip_reason ||
    '|code=' || code ||
    '|name=' || left(name, 100) AS value
FROM by_code_ranked
WHERE code_rank <= 10
UNION ALL
SELECT
    'window_skip_recent_' || recent_rank::text,
    'skip_delay_s=' || round(skip_delay_s::numeric, 3)::text ||
    '|due=' || entry_due_at_utc::text ||
    '|updated=' || updated_at_utc::text ||
    '|market_start=' || COALESCE(market_start_utc::text, '') ||
    '|market_end=' || COALESCE(market_end_utc::text, '') ||
    '|reason=' || skip_reason ||
    '|code=' || code ||
    '|name=' || left(name, 100)
FROM recent
WHERE recent_rank <= 10
ORDER BY name;
""";

    private const string RecentStatusSql = """
WITH recent AS (
    SELECT
        run.status,
        run.skip_reason,
        run.entry_due_at_utc,
        run.created_at_utc,
        run.updated_at_utc,
        run.entered_at_utc
    FROM strategy_market_paper_runs run
    WHERE run.entry_due_at_utc >= now() - interval '120 minutes'
)
SELECT
    'status_' || status AS name,
    'rows=' || count(*)::text ||
    '|entered=' || count(*) FILTER (WHERE entered_at_utc IS NOT NULL)::text ||
    '|due_min=' || COALESCE(min(entry_due_at_utc)::text, '') ||
    '|due_max=' || COALESCE(max(entry_due_at_utc)::text, '') ||
    '|created_max=' || COALESCE(max(created_at_utc)::text, '') ||
    '|updated_max=' || COALESCE(max(updated_at_utc)::text, '') AS value
FROM recent
GROUP BY status
ORDER BY status;
""";

    private const string LiveOrdersSql = """
WITH recent_live AS (
    SELECT
        live_order.id,
        live_order.status,
        live_order.strategy_id,
        live_order.paper_order_id,
        live_order.created_at_utc,
        live_order.submitted_at_utc,
        live_order.response_status,
        strategy.code,
        run.entry_due_at_utc,
        EXTRACT(EPOCH FROM (live_order.created_at_utc - run.entry_due_at_utc)) AS delay_s
    FROM live_orders live_order
    INNER JOIN strategies strategy ON strategy.id = live_order.strategy_id
    LEFT JOIN strategy_market_paper_runs run ON run.paper_order_id = live_order.paper_order_id
    WHERE live_order.created_at_utc >= now() - interval '120 minutes'
),
summary AS (
    SELECT
        count(*) AS rows,
        count(*) FILTER (WHERE status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested')) AS open_rows,
        count(*) FILTER (WHERE status IN ('Rejected', 'Error', 'PreflightRejected')) AS problem_rows,
        count(*) FILTER (WHERE delay_s IS NOT NULL) AS matched_rows,
        round(avg(GREATEST(0, delay_s)) FILTER (WHERE delay_s IS NOT NULL)::numeric, 3) AS avg_delay_s,
        round(max(delay_s) FILTER (WHERE delay_s IS NOT NULL)::numeric, 3) AS max_delay_s,
        count(*) FILTER (WHERE delay_s > 30) AS over30,
        count(*) FILTER (WHERE delay_s > 60) AS over60,
        max(created_at_utc) AS latest
    FROM recent_live
)
SELECT
    'live_orders_120m' AS name,
    'rows=' || rows::text ||
    '|open=' || open_rows::text ||
    '|problem=' || problem_rows::text ||
    '|matched_to_run=' || matched_rows::text ||
    '|avg_delay_s=' || COALESCE(avg_delay_s::text, '') ||
    '|max_delay_s=' || COALESCE(max_delay_s::text, '') ||
    '|over30=' || over30::text ||
    '|over60=' || over60::text ||
    '|latest=' || COALESCE(latest::text, '') AS value
FROM summary
UNION ALL
SELECT
    'live_status_' || status,
    'rows=' || count(*)::text ||
    '|latest=' || max(created_at_utc)::text
FROM recent_live
GROUP BY status
ORDER BY name;
""";
}
