using System.Data;
using System.Globalization;
using Npgsql;

const string MiddlePattern = "^(btc|eth|sol)_up_down_5m_middle_[0-9]+(_revert)?(_bps_[0-9]+)?(_instant)?$";
const string UpDownBpsPattern = "^(btc|eth|sol)_up_down_5m_(up|down)_bps_[0-9]+_instant$";
const string SimplePattern = "^(btc|eth|sol)_up_down_5m_(up|down)_simple$";
const string DiffPattern = "^(btc|eth|sol)_up_down_5m_(up|down)_(diff|adjusted_diff)_[0-9]+(_revert)?_instant$";
const string ShiftDiffPattern = "^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_[0-9]+_[0-9]+(_revert)?_instant$";

var outputPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? "outputs/betting-throughput-check-2026-06-16/result.txt";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "BettingThroughputCheck",
    Timeout = 10,
    CommandTimeout = 90
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await ExecuteNonQueryAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '75s';
SET LOCAL lock_timeout = '500ms';
""");

await WriteLineAsync($"Betting throughput snapshot captured at {DateTimeOffset.UtcNow:O}");
await WriteLineAsync($"Host: {builder.Host}");
await WriteScalarAsync("Database", "SELECT current_database();");
await WriteScalarAsync("Server address", "SELECT inet_server_addr()::text;");
await WriteScalarAsync("Server time UTC", "SELECT to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"');");

await WriteSectionAsync("Service heartbeat");
await WriteRowsAsync("""
SELECT
    service_name,
    status,
    mode,
    to_char(started_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS started_utc,
    to_char(last_heartbeat_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS heartbeat_utc,
    round(extract(epoch FROM (now() - last_heartbeat_utc))::numeric, 1) AS heartbeat_age_seconds,
    version,
    current_loop,
    COALESCE(NULLIF(last_error, ''), '<none>') AS last_error
FROM service_heartbeats
ORDER BY last_heartbeat_utc DESC
LIMIT 3;
""");

await WriteSectionAsync("Enabled strategy inventory");
await WriteRowsAsync($$"""
WITH classified AS (
    SELECT
        enabled,
        live_stakes,
        auto_live_paused,
        paused,
        CASE
            WHEN live_stakes AND NOT auto_live_paused THEN 'live'
            WHEN code ~ @simple_pattern THEN 'simple_paper'
            WHEN code ~ @up_down_bps_pattern THEN 'up_down_bps_paper'
            WHEN code ~ @diff_pattern OR code ~ @shift_diff_pattern THEN 'diff_paper'
            WHEN code ~ @middle_pattern THEN 'middle'
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'other_up_down_5m'
            ELSE 'other'
        END AS strategy_group
    FROM strategies
    WHERE enabled
)
SELECT
    strategy_group,
    count(*) AS enabled_count,
    count(*) FILTER (WHERE live_stakes) AS live_stakes_count,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused_count,
    count(*) FILTER (WHERE paused) AS paused_count
FROM classified
GROUP BY strategy_group
ORDER BY enabled_count DESC, strategy_group;
""", PatternParameters());

await WriteSectionAsync("Current due backlog");
await WriteRowsAsync("""
WITH classified AS (
    SELECT
        run.entry_due_at_utc,
        CASE
            WHEN strategy.live_stakes AND NOT strategy.auto_live_paused THEN 'live'
            WHEN strategy.code ~ @simple_pattern THEN 'simple_paper'
            WHEN strategy.code ~ @up_down_bps_pattern THEN 'up_down_bps_paper'
            WHEN strategy.code ~ @diff_pattern OR strategy.code ~ @shift_diff_pattern THEN 'diff_paper'
            WHEN strategy.code ~ @middle_pattern THEN 'middle'
            WHEN strategy.code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'other_up_down_5m'
            ELSE 'other'
        END AS strategy_group
    FROM strategy_market_paper_runs run
    JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE strategy.enabled
      AND run.status = 'Observed'
      AND run.entry_due_at_utc <= now()
)
SELECT
    strategy_group,
    count(*) AS due_observed_runs,
    count(*) FILTER (WHERE entry_due_at_utc < now() - interval '60 seconds') AS expired_by_60s_grace,
    round(max(extract(epoch FROM (now() - entry_due_at_utc)))::numeric, 1) AS max_overdue_seconds,
    to_char(min(entry_due_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS earliest_due_utc,
    to_char(max(entry_due_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_due_utc
FROM classified
GROUP BY strategy_group
ORDER BY due_observed_runs DESC, strategy_group;
""", PatternParameters());

await WriteSectionAsync("Observed future queue next 10 minutes");
await WriteRowsAsync("""
WITH classified AS (
    SELECT
        run.entry_due_at_utc,
        CASE
            WHEN strategy.live_stakes AND NOT strategy.auto_live_paused THEN 'live'
            WHEN strategy.code ~ @simple_pattern THEN 'simple_paper'
            WHEN strategy.code ~ @up_down_bps_pattern THEN 'up_down_bps_paper'
            WHEN strategy.code ~ @diff_pattern OR strategy.code ~ @shift_diff_pattern THEN 'diff_paper'
            WHEN strategy.code ~ @middle_pattern THEN 'middle'
            WHEN strategy.code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'other_up_down_5m'
            ELSE 'other'
        END AS strategy_group
    FROM strategy_market_paper_runs run
    JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE strategy.enabled
      AND run.status = 'Observed'
      AND run.entry_due_at_utc > now()
      AND run.entry_due_at_utc <= now() + interval '10 minutes'
)
SELECT
    strategy_group,
    count(*) AS future_observed_runs,
    to_char(min(entry_due_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS earliest_due_utc,
    to_char(max(entry_due_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_due_utc
FROM classified
GROUP BY strategy_group
ORDER BY future_observed_runs DESC, strategy_group;
""", PatternParameters());

await WriteSectionAsync("Entry due expired by recent window");
await WriteRowsAsync("""
WITH windows(window_label, window_start_utc) AS (
    VALUES
        ('15m', now() - interval '15 minutes'),
        ('60m', now() - interval '60 minutes'),
        ('6h', now() - interval '6 hours'),
        ('since_bps_enable', '2026-06-16T06:15:05Z'::timestamptz)
),
classified AS (
    SELECT
        run.updated_at_utc,
        CASE
            WHEN strategy.live_stakes AND NOT strategy.auto_live_paused THEN 'live'
            WHEN strategy.code ~ @simple_pattern THEN 'simple_paper'
            WHEN strategy.code ~ @up_down_bps_pattern THEN 'up_down_bps_paper'
            WHEN strategy.code ~ @diff_pattern OR strategy.code ~ @shift_diff_pattern THEN 'diff_paper'
            WHEN strategy.code ~ @middle_pattern THEN 'middle'
            WHEN strategy.code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'other_up_down_5m'
            ELSE 'other'
        END AS strategy_group
    FROM strategy_market_paper_runs run
    JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE strategy.enabled
      AND run.status = 'Skipped'
      AND run.skip_reason = 'entry_due_expired'
      AND run.updated_at_utc >= now() - interval '6 hours'
)
SELECT
    windows.window_label,
    classified.strategy_group,
    count(*) AS entry_due_expired,
    to_char(max(classified.updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_updated_utc
FROM windows
JOIN classified ON classified.updated_at_utc >= windows.window_start_utc
GROUP BY windows.window_label, classified.strategy_group
ORDER BY
    CASE windows.window_label WHEN '15m' THEN 1 WHEN '60m' THEN 2 WHEN '6h' THEN 3 ELSE 4 END,
    entry_due_expired DESC,
    classified.strategy_group;
""", PatternParameters());

await WriteSectionAsync("Entry delays by recent window");
await WriteRowsAsync("""
WITH windows(window_label, window_start_utc) AS (
    VALUES
        ('15m', now() - interval '15 minutes'),
        ('60m', now() - interval '60 minutes'),
        ('6h', now() - interval '6 hours'),
        ('since_bps_enable', '2026-06-16T06:15:05Z'::timestamptz)
),
classified AS (
    SELECT
        run.entered_at_utc,
        GREATEST(0, extract(epoch FROM (run.entered_at_utc - run.entry_due_at_utc))) AS delay_seconds,
        CASE
            WHEN strategy.live_stakes AND NOT strategy.auto_live_paused THEN 'live'
            WHEN strategy.code ~ @simple_pattern THEN 'simple_paper'
            WHEN strategy.code ~ @up_down_bps_pattern THEN 'up_down_bps_paper'
            WHEN strategy.code ~ @diff_pattern OR strategy.code ~ @shift_diff_pattern THEN 'diff_paper'
            WHEN strategy.code ~ @middle_pattern THEN 'middle'
            WHEN strategy.code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'other_up_down_5m'
            ELSE 'other'
        END AS strategy_group
    FROM strategy_market_paper_runs run
    JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE strategy.enabled
      AND run.entered_at_utc IS NOT NULL
      AND run.entered_at_utc >= now() - interval '6 hours'
)
SELECT
    windows.window_label,
    classified.strategy_group,
    count(*) AS entered_runs,
    round(avg(classified.delay_seconds)::numeric, 2) AS avg_delay_seconds,
    round(percentile_cont(0.95) WITHIN GROUP (ORDER BY classified.delay_seconds)::numeric, 2) AS p95_delay_seconds,
    round(max(classified.delay_seconds)::numeric, 2) AS max_delay_seconds,
    to_char(max(classified.entered_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_entered_utc
FROM windows
JOIN classified ON classified.entered_at_utc >= windows.window_start_utc
GROUP BY windows.window_label, classified.strategy_group
ORDER BY
    CASE windows.window_label WHEN '15m' THEN 1 WHEN '60m' THEN 2 WHEN '6h' THEN 3 ELSE 4 END,
    classified.strategy_group;
""", PatternParameters());

await WriteSectionAsync("Run status by recent window");
await WriteRowsAsync("""
WITH windows(window_label, window_start_utc) AS (
    VALUES
        ('15m', now() - interval '15 minutes'),
        ('60m', now() - interval '60 minutes'),
        ('6h', now() - interval '6 hours'),
        ('since_bps_enable', '2026-06-16T06:15:05Z'::timestamptz)
),
classified AS (
    SELECT
        run.created_at_utc,
        run.status,
        run.skip_reason,
        CASE
            WHEN strategy.live_stakes AND NOT strategy.auto_live_paused THEN 'live'
            WHEN strategy.code ~ @simple_pattern THEN 'simple_paper'
            WHEN strategy.code ~ @up_down_bps_pattern THEN 'up_down_bps_paper'
            WHEN strategy.code ~ @diff_pattern OR strategy.code ~ @shift_diff_pattern THEN 'diff_paper'
            WHEN strategy.code ~ @middle_pattern THEN 'middle'
            WHEN strategy.code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'other_up_down_5m'
            ELSE 'other'
        END AS strategy_group
    FROM strategy_market_paper_runs run
    JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE strategy.enabled
      AND run.created_at_utc >= now() - interval '6 hours'
)
SELECT
    windows.window_label,
    classified.strategy_group,
    count(*) AS runs_created,
    count(*) FILTER (WHERE status = 'Observed') AS observed,
    count(*) FILTER (WHERE status = 'Entered') AS entered,
    count(*) FILTER (WHERE status = 'Skipped') AS skipped,
    count(*) FILTER (WHERE status = 'Settled') AS settled,
    count(*) FILTER (WHERE status = 'Skipped' AND skip_reason = 'entry_due_expired') AS entry_due_expired
FROM windows
JOIN classified ON classified.created_at_utc >= windows.window_start_utc
GROUP BY windows.window_label, classified.strategy_group
ORDER BY
    CASE windows.window_label WHEN '15m' THEN 1 WHEN '60m' THEN 2 WHEN '6h' THEN 3 ELSE 4 END,
    classified.strategy_group;
""", PatternParameters());

await WriteSectionAsync("Top skip reasons last 60 minutes");
await WriteRowsAsync("""
WITH classified AS (
    SELECT
        COALESCE(run.skip_reason, '<none>') AS skip_reason,
        run.updated_at_utc,
        CASE
            WHEN strategy.live_stakes AND NOT strategy.auto_live_paused THEN 'live'
            WHEN strategy.code ~ @simple_pattern THEN 'simple_paper'
            WHEN strategy.code ~ @up_down_bps_pattern THEN 'up_down_bps_paper'
            WHEN strategy.code ~ @diff_pattern OR strategy.code ~ @shift_diff_pattern THEN 'diff_paper'
            WHEN strategy.code ~ @middle_pattern THEN 'middle'
            WHEN strategy.code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'other_up_down_5m'
            ELSE 'other'
        END AS strategy_group
    FROM strategy_market_paper_runs run
    JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE strategy.enabled
      AND run.status = 'Skipped'
      AND run.updated_at_utc >= now() - interval '60 minutes'
)
SELECT
    strategy_group,
    skip_reason,
    count(*) AS count,
    to_char(max(updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_updated_utc
FROM classified
GROUP BY strategy_group, skip_reason
ORDER BY count DESC, strategy_group, skip_reason
LIMIT 30;
""", PatternParameters());

await WriteSectionAsync("Live order activity by recent window");
await WriteRowsAsync("""
WITH windows(window_label, window_start_utc) AS (
    VALUES
        ('15m', now() - interval '15 minutes'),
        ('60m', now() - interval '60 minutes'),
        ('6h', now() - interval '6 hours'),
        ('since_bps_enable', '2026-06-16T06:15:05Z'::timestamptz)
)
SELECT
    windows.window_label,
    count(*) AS live_orders,
    count(*) FILTER (WHERE live_order.status = 'Matched') AS matched,
    count(*) FILTER (WHERE live_order.status = 'PreflightRejected') AS preflight_rejected,
    count(*) FILTER (WHERE live_order.status IN ('Rejected', 'Failed', 'SubmitFailed')) AS rejected_or_failed,
    to_char(max(live_order.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_created_utc,
    to_char(max(live_order.updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_updated_utc
FROM windows
JOIN live_orders live_order ON live_order.created_at_utc >= windows.window_start_utc
JOIN strategies strategy ON strategy.id = live_order.strategy_id
WHERE strategy.live_stakes
GROUP BY windows.window_label
ORDER BY CASE windows.window_label WHEN '15m' THEN 1 WHEN '60m' THEN 2 WHEN '6h' THEN 3 ELSE 4 END;
""");

await WriteSectionAsync("Live strategy latest orders");
await WriteRowsAsync("""
SELECT
    strategy.code,
    to_char(max(live_order.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_live_created_utc,
    count(*) FILTER (WHERE live_order.created_at_utc >= now() - interval '60 minutes') AS live_orders_last_60m,
    count(*) FILTER (WHERE live_order.created_at_utc >= now() - interval '6 hours') AS live_orders_last_6h
FROM strategies strategy
LEFT JOIN live_orders live_order ON live_order.strategy_id = strategy.id
WHERE strategy.live_stakes
GROUP BY strategy.code
ORDER BY latest_live_created_utc DESC NULLS LAST, strategy.code;
""");

await WriteSectionAsync("Latest entry due expired samples last 6 hours");
await WriteRowsAsync("""
SELECT
    to_char(run.updated_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS skipped_utc,
    to_char(run.entry_due_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS entry_due_utc,
    strategy.code,
    run.market_slug,
    run.skip_reason,
    round(extract(epoch FROM (run.updated_at_utc - run.entry_due_at_utc))::numeric, 1) AS skipped_after_due_seconds
FROM strategy_market_paper_runs run
JOIN strategies strategy ON strategy.id = run.strategy_id
WHERE strategy.enabled
  AND run.status = 'Skipped'
  AND run.skip_reason = 'entry_due_expired'
  AND run.updated_at_utc >= now() - interval '6 hours'
ORDER BY run.updated_at_utc DESC
LIMIT 30;
""");

await WriteSectionAsync("Current due backlog samples");
await WriteRowsAsync("""
SELECT
    to_char(run.entry_due_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS entry_due_utc,
    round(extract(epoch FROM (now() - run.entry_due_at_utc))::numeric, 1) AS overdue_seconds,
    strategy.code,
    run.market_slug,
    run.status
FROM strategy_market_paper_runs run
JOIN strategies strategy ON strategy.id = run.strategy_id
WHERE strategy.enabled
  AND run.status = 'Observed'
  AND run.entry_due_at_utc <= now()
ORDER BY run.entry_due_at_utc ASC, strategy.live_stakes DESC, strategy.code
LIMIT 30;
""");

await WriteSectionAsync("Recent API errors last 30 minutes");
await WriteRowsAsync("""
SELECT
    component,
    operation,
    count(*) AS count,
    to_char(max(created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_utc
FROM api_errors
WHERE created_at_utc >= now() - interval '30 minutes'
GROUP BY component, operation
ORDER BY latest_utc DESC, count DESC
LIMIT 30;
""");

await tx.CommitAsync();
await WriteLineAsync("");
await WriteLineAsync($"Betting throughput snapshot finished at {DateTimeOffset.UtcNow:O}");

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
    await using var command = new NpgsqlCommand(sql, connection, tx);
    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }

    var rows = 0;
    await using (var reader = await command.ExecuteReaderAsync())
    {
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        await WriteLineAsync(string.Join('\t', names));

        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
            {
                values[index] = reader.IsDBNull(index)
                    ? "<null>"
                    : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;
            }

            await WriteLineAsync(string.Join('\t', values));
            rows++;
        }
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

static (string Name, object Value)[] PatternParameters()
{
    return
    [
        ("middle_pattern", MiddlePattern),
        ("up_down_bps_pattern", UpDownBpsPattern),
        ("simple_pattern", SimplePattern),
        ("diff_pattern", DiffPattern),
        ("shift_diff_pattern", ShiftDiffPattern)
    ];
}
