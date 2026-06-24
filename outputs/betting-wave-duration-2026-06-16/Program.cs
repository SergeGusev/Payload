using System.Data;
using System.Globalization;
using Npgsql;

const string MiddlePattern = "^(btc|eth|sol)_up_down_5m_middle_[0-9]+(_revert)?(_bps_[0-9]+)?(_instant)?$";
const string UpDownBpsPattern = "^(btc|eth|sol)_up_down_5m_(up|down)_bps_[0-9]+_instant$";
const string SimplePattern = "^(btc|eth|sol)_up_down_5m_(up|down)_simple$";
const string DiffPattern = "^(btc|eth|sol)_up_down_5m_(up|down)_(diff|adjusted_diff)_[0-9]+(_revert)?_instant$";
const string ShiftDiffPattern = "^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_[0-9]+_[0-9]+(_revert)?_instant$";

var outputPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? "outputs/betting-wave-duration-2026-06-16/result.txt";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "BettingWaveDuration",
    Timeout = 10,
    CommandTimeout = 60
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await ExecuteNonQueryAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '45s';
SET LOCAL lock_timeout = '500ms';
""");

await WriteLineAsync($"Betting wave duration snapshot captured at {DateTimeOffset.UtcNow:O}");
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
    to_char(last_heartbeat_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS heartbeat_utc,
    round(extract(epoch FROM (now() - last_heartbeat_utc))::numeric, 1) AS heartbeat_age_seconds,
    COALESCE(NULLIF(last_error, ''), '<none>') AS last_error
FROM service_heartbeats
ORDER BY last_heartbeat_utc DESC
LIMIT 3;
""");

await WriteSectionAsync("Recent all-enabled waves");
await WriteRowsAsync("""
WITH classified AS (
    SELECT
        run.entry_due_at_utc,
        run.status,
        run.skip_reason,
        run.entered_at_utc,
        CASE
            WHEN run.entered_at_utc IS NOT NULL THEN run.entered_at_utc
            WHEN run.status = 'Skipped' THEN run.updated_at_utc
            ELSE NULL
        END AS processed_at_utc
    FROM strategy_market_paper_runs run
    JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE strategy.enabled
      AND run.entry_due_at_utc >= now() - interval '90 minutes'
      AND run.entry_due_at_utc <= now() - interval '60 seconds'
)
SELECT
    to_char(entry_due_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS entry_due_utc,
    count(*) AS runs,
    count(*) FILTER (WHERE status = 'Observed') AS still_observed,
    count(*) FILTER (WHERE processed_at_utc IS NOT NULL) AS processed,
    count(*) FILTER (WHERE entered_at_utc IS NOT NULL) AS entered_or_settled,
    count(*) FILTER (WHERE status = 'Skipped') AS skipped,
    count(*) FILTER (WHERE status = 'Skipped' AND skip_reason = 'entry_due_expired') AS entry_due_expired,
    round(max(extract(epoch FROM (processed_at_utc - entry_due_at_utc)))::numeric, 2) AS full_wave_seconds,
    round(max(extract(epoch FROM (entered_at_utc - entry_due_at_utc)))::numeric, 2) AS max_actual_entry_seconds,
    to_char(max(processed_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS last_processed_utc
FROM classified
GROUP BY entry_due_at_utc
ORDER BY entry_due_at_utc DESC
LIMIT 18;
""");

await WriteSectionAsync("Recent waves by group");
await WriteRowsAsync("""
WITH classified AS (
    SELECT
        run.entry_due_at_utc,
        run.status,
        run.skip_reason,
        run.entered_at_utc,
        CASE
            WHEN run.entered_at_utc IS NOT NULL THEN run.entered_at_utc
            WHEN run.status = 'Skipped' THEN run.updated_at_utc
            ELSE NULL
        END AS processed_at_utc,
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
      AND run.entry_due_at_utc >= now() - interval '90 minutes'
      AND run.entry_due_at_utc <= now() - interval '60 seconds'
)
SELECT
    to_char(entry_due_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS entry_due_utc,
    strategy_group,
    count(*) AS runs,
    count(*) FILTER (WHERE status = 'Observed') AS still_observed,
    count(*) FILTER (WHERE processed_at_utc IS NOT NULL) AS processed,
    count(*) FILTER (WHERE entered_at_utc IS NOT NULL) AS entered_or_settled,
    count(*) FILTER (WHERE status = 'Skipped') AS skipped,
    count(*) FILTER (WHERE status = 'Skipped' AND skip_reason = 'entry_due_expired') AS entry_due_expired,
    round(max(extract(epoch FROM (processed_at_utc - entry_due_at_utc)))::numeric, 2) AS full_group_seconds,
    round(max(extract(epoch FROM (entered_at_utc - entry_due_at_utc)))::numeric, 2) AS max_actual_entry_seconds,
    to_char(max(processed_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS last_processed_utc
FROM classified
GROUP BY entry_due_at_utc, strategy_group
ORDER BY entry_due_at_utc DESC,
    CASE strategy_group
        WHEN 'live' THEN 1
        WHEN 'simple_paper' THEN 2
        WHEN 'up_down_bps_paper' THEN 3
        WHEN 'diff_paper' THEN 4
        ELSE 5
    END
LIMIT 80;
""", PatternParameters());

await WriteSectionAsync("Completed wave duration summary");
await WriteRowsAsync("""
WITH classified AS (
    SELECT
        run.entry_due_at_utc,
        run.status,
        run.skip_reason,
        run.entered_at_utc,
        CASE
            WHEN run.entered_at_utc IS NOT NULL THEN run.entered_at_utc
            WHEN run.status = 'Skipped' THEN run.updated_at_utc
            ELSE NULL
        END AS processed_at_utc,
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
      AND run.entry_due_at_utc >= now() - interval '90 minutes'
      AND run.entry_due_at_utc <= now() - interval '60 seconds'
),
expanded AS (
    SELECT *, strategy_group AS rollup_group FROM classified
    UNION ALL
    SELECT *, 'all_enabled' AS rollup_group FROM classified
),
waves AS (
    SELECT
        entry_due_at_utc,
        rollup_group,
        count(*) AS runs,
        count(*) FILTER (WHERE status = 'Observed') AS observed,
        count(*) FILTER (WHERE status = 'Skipped' AND skip_reason = 'entry_due_expired') AS entry_due_expired,
        max(extract(epoch FROM (processed_at_utc - entry_due_at_utc))) AS full_wave_seconds,
        max(extract(epoch FROM (entered_at_utc - entry_due_at_utc))) AS max_actual_entry_seconds
    FROM expanded
    GROUP BY entry_due_at_utc, rollup_group
)
SELECT
    rollup_group,
    count(*) FILTER (WHERE observed = 0) AS completed_waves,
    min(runs) FILTER (WHERE observed = 0) AS min_runs_per_wave,
    max(runs) FILTER (WHERE observed = 0) AS max_runs_per_wave,
    round(avg(full_wave_seconds) FILTER (WHERE observed = 0)::numeric, 2) AS avg_full_wave_seconds,
    round(percentile_cont(0.50) WITHIN GROUP (ORDER BY full_wave_seconds) FILTER (WHERE observed = 0)::numeric, 2) AS p50_full_wave_seconds,
    round(percentile_cont(0.95) WITHIN GROUP (ORDER BY full_wave_seconds) FILTER (WHERE observed = 0)::numeric, 2) AS p95_full_wave_seconds,
    round(max(full_wave_seconds) FILTER (WHERE observed = 0)::numeric, 2) AS max_full_wave_seconds,
    round(avg(max_actual_entry_seconds) FILTER (WHERE observed = 0 AND max_actual_entry_seconds IS NOT NULL)::numeric, 2) AS avg_max_actual_entry_seconds,
    round(max(max_actual_entry_seconds) FILTER (WHERE observed = 0)::numeric, 2) AS max_actual_entry_seconds,
    sum(entry_due_expired) FILTER (WHERE observed = 0) AS entry_due_expired
FROM waves
GROUP BY rollup_group
ORDER BY
    CASE rollup_group
        WHEN 'all_enabled' THEN 0
        WHEN 'live' THEN 1
        WHEN 'simple_paper' THEN 2
        WHEN 'up_down_bps_paper' THEN 3
        WHEN 'diff_paper' THEN 4
        ELSE 5
    END,
    rollup_group;
""", PatternParameters());

await WriteSectionAsync("Completed wave duration summary without entry_due_expired");
await WriteRowsAsync("""
WITH classified AS (
    SELECT
        run.entry_due_at_utc,
        run.status,
        run.skip_reason,
        run.entered_at_utc,
        CASE
            WHEN run.entered_at_utc IS NOT NULL THEN run.entered_at_utc
            WHEN run.status = 'Skipped' THEN run.updated_at_utc
            ELSE NULL
        END AS processed_at_utc,
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
      AND run.entry_due_at_utc >= now() - interval '90 minutes'
      AND run.entry_due_at_utc <= now() - interval '60 seconds'
),
expanded AS (
    SELECT *, strategy_group AS rollup_group FROM classified
    UNION ALL
    SELECT *, 'all_enabled' AS rollup_group FROM classified
),
waves AS (
    SELECT
        entry_due_at_utc,
        rollup_group,
        count(*) AS runs,
        count(*) FILTER (WHERE status = 'Observed') AS observed,
        count(*) FILTER (WHERE status = 'Skipped' AND skip_reason = 'entry_due_expired') AS entry_due_expired,
        max(extract(epoch FROM (processed_at_utc - entry_due_at_utc))) AS full_wave_seconds,
        max(extract(epoch FROM (entered_at_utc - entry_due_at_utc))) AS max_actual_entry_seconds
    FROM expanded
    GROUP BY entry_due_at_utc, rollup_group
)
SELECT
    rollup_group,
    count(*) AS completed_clean_waves,
    min(runs) AS min_runs_per_wave,
    max(runs) AS max_runs_per_wave,
    round(avg(full_wave_seconds)::numeric, 2) AS avg_full_wave_seconds,
    round(percentile_cont(0.50) WITHIN GROUP (ORDER BY full_wave_seconds)::numeric, 2) AS p50_full_wave_seconds,
    round(percentile_cont(0.95) WITHIN GROUP (ORDER BY full_wave_seconds)::numeric, 2) AS p95_full_wave_seconds,
    round(max(full_wave_seconds)::numeric, 2) AS max_full_wave_seconds,
    round(avg(max_actual_entry_seconds) FILTER (WHERE max_actual_entry_seconds IS NOT NULL)::numeric, 2) AS avg_max_actual_entry_seconds,
    round(max(max_actual_entry_seconds)::numeric, 2) AS max_actual_entry_seconds
FROM waves
WHERE observed = 0
  AND entry_due_expired = 0
GROUP BY rollup_group
ORDER BY
    CASE rollup_group
        WHEN 'all_enabled' THEN 0
        WHEN 'live' THEN 1
        WHEN 'simple_paper' THEN 2
        WHEN 'up_down_bps_paper' THEN 3
        WHEN 'diff_paper' THEN 4
        ELSE 5
    END,
    rollup_group;
""", PatternParameters());

await WriteSectionAsync("Outlier wave details");
await WriteRowsAsync("""
WITH base AS (
    SELECT
        run.entry_due_at_utc,
        strategy.code,
        run.status,
        COALESCE(run.skip_reason, '<none>') AS skip_reason,
        run.entered_at_utc,
        CASE
            WHEN run.entered_at_utc IS NOT NULL THEN run.entered_at_utc
            WHEN run.status = 'Skipped' THEN run.updated_at_utc
            ELSE NULL
        END AS processed_at_utc,
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
      AND run.entry_due_at_utc >= now() - interval '90 minutes'
      AND run.entry_due_at_utc <= now() - interval '60 seconds'
),
outlier_waves AS (
    SELECT entry_due_at_utc
    FROM base
    GROUP BY entry_due_at_utc
    HAVING max(extract(epoch FROM (processed_at_utc - entry_due_at_utc))) > 120
        OR count(*) FILTER (WHERE status = 'Skipped' AND skip_reason = 'entry_due_expired') > 0
)
SELECT
    to_char(base.entry_due_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS entry_due_utc,
    base.strategy_group,
    base.code,
    base.status,
    base.skip_reason,
    count(*) AS count,
    round(max(extract(epoch FROM (base.processed_at_utc - base.entry_due_at_utc)))::numeric, 2) AS max_processed_after_due_seconds,
    round(max(extract(epoch FROM (base.entered_at_utc - base.entry_due_at_utc)))::numeric, 2) AS max_entered_after_due_seconds,
    to_char(max(base.processed_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_processed_utc
FROM base
JOIN outlier_waves ON outlier_waves.entry_due_at_utc = base.entry_due_at_utc
WHERE base.status = 'Skipped'
  AND (
      base.skip_reason = 'entry_due_expired'
      OR extract(epoch FROM (base.processed_at_utc - base.entry_due_at_utc)) > 120
  )
GROUP BY base.entry_due_at_utc, base.strategy_group, base.code, base.status, base.skip_reason
ORDER BY base.entry_due_at_utc DESC, max_processed_after_due_seconds DESC, base.code
LIMIT 80;
""", PatternParameters());

await tx.CommitAsync();
await WriteLineAsync("");
await WriteLineAsync($"Betting wave duration snapshot finished at {DateTimeOffset.UtcNow:O}");

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
