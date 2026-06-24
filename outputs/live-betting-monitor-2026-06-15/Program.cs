using System.Data;
using Npgsql;
using NpgsqlTypes;

const string MiddlePattern = "^(btc|eth|sol)_up_down_5m_middle_[0-9]+(_revert)?(_bps_[0-9]+)?(_instant)?$";
const string TargetStrategyCode = "btc_up_down_5m_up_simple";

var outputPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? "outputs/live-betting-monitor-2026-06-15/snapshot.txt";
var sinceArg = args.FirstOrDefault(arg => arg.StartsWith("--since=", StringComparison.OrdinalIgnoreCase));
var sinceUtc = sinceArg is null
    ? DateTimeOffset.Parse("2026-06-15T05:25:12Z", System.Globalization.CultureInfo.InvariantCulture)
    : DateTimeOffset.Parse(sinceArg["--since=".Length..], System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "LiveBettingMonitor"
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();

await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await ExecuteNonQueryAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '30s';
SET LOCAL lock_timeout = '500ms';
""");

await WriteLineAsync($"Live betting monitor snapshot captured at {DateTimeOffset.UtcNow:O}");
await WriteLineAsync($"Since UTC: {sinceUtc:O}");
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

await WriteSectionAsync("Middle and enabled counts");
await WriteRowsAsync("""
SELECT
    count(*) FILTER (WHERE code ~ @middle_pattern) AS middle_total,
    count(*) FILTER (WHERE code ~ @middle_pattern AND enabled) AS middle_enabled,
    count(*) FILTER (WHERE code ~ @middle_pattern AND NOT enabled) AS middle_disabled,
    count(*) FILTER (WHERE enabled) AS total_enabled,
    count(*) FILTER (WHERE live_stakes) AS live_stakes,
    count(*) FILTER (WHERE enabled AND live_stakes) AS enabled_live_stakes
FROM strategies;
""", ("middle_pattern", MiddlePattern));

await WriteRowsAsync("""
SELECT
    CASE
        WHEN code ~ @middle_pattern THEN 'middle_n'
        WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$' THEN 'simple'
        WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_bps_[0-9]+(_instant)?$' THEN 'up_down_bps'
        WHEN code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'other_up_down_5m'
        ELSE 'other'
    END AS strategy_group,
    count(*) AS enabled_count,
    count(*) FILTER (WHERE live_stakes) AS live_stakes_count
FROM strategies
WHERE enabled
GROUP BY strategy_group
ORDER BY enabled_count DESC, strategy_group;
""", ("middle_pattern", MiddlePattern));

await WriteSectionAsync("Live strategy flags");
await WriteRowsAsync("""
SELECT
    code,
    enabled,
    live_stakes,
    auto_live_paused,
    paused,
    live_stake_amount,
    live_available_balance,
    to_char(live_enabled_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS live_enabled_utc,
    to_char(updated_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS updated_utc
FROM strategies
WHERE live_stakes
ORDER BY code;
""");

await WriteSectionAsync("Live runs since window");
await WriteRowsAsync("""
SELECT
    strategy.code,
    count(run.id) AS runs,
    count(run.id) FILTER (WHERE run.status = 'Observed') AS observed,
    count(run.id) FILTER (WHERE run.status = 'Entered') AS entered,
    count(run.id) FILTER (WHERE run.status = 'Skipped') AS skipped,
    count(run.id) FILTER (WHERE run.status = 'Settled') AS settled,
    to_char(min(run.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS first_run_utc,
    to_char(max(run.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_run_utc
FROM strategies strategy
LEFT JOIN strategy_market_paper_runs run
    ON run.strategy_id = strategy.id
   AND run.created_at_utc >= @since_utc
WHERE strategy.live_stakes
GROUP BY strategy.code
ORDER BY strategy.code;
""", ("since_utc", UtcDateTime(sinceUtc)));

await WriteSectionAsync("Live skip reasons since window");
await WriteRowsAsync("""
SELECT
    strategy.code,
    COALESCE(run.skip_reason, '<none>') AS skip_reason,
    count(*) AS count,
    to_char(max(run.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_utc
FROM strategy_market_paper_runs run
JOIN strategies strategy
    ON strategy.id = run.strategy_id
WHERE strategy.live_stakes
  AND run.created_at_utc >= @since_utc
  AND run.status = 'Skipped'
GROUP BY strategy.code, COALESCE(run.skip_reason, '<none>')
ORDER BY strategy.code, count DESC, skip_reason;
""", ("since_utc", UtcDateTime(sinceUtc)));

await WriteSectionAsync("Live order status since window");
await WriteRowsAsync("""
SELECT
    strategy.code,
    live_order.status,
    live_order.response_status,
    live_order.cancel_status,
    count(*) AS count,
    sum(live_order.filled_size) AS filled_size,
    sum(live_order.remaining_size) AS remaining_size,
    sum(live_order.filled_notional_usd) AS filled_notional_usd,
    to_char(max(live_order.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_utc
FROM live_orders live_order
JOIN strategies strategy
    ON strategy.id = live_order.strategy_id
WHERE strategy.live_stakes
  AND live_order.created_at_utc >= @since_utc
GROUP BY strategy.code, live_order.status, live_order.response_status, live_order.cancel_status
ORDER BY strategy.code, count DESC, live_order.status, live_order.response_status, live_order.cancel_status;
""", ("since_utc", UtcDateTime(sinceUtc)));

await WriteSectionAsync("Recent live runs since window");
await WriteRowsAsync("""
SELECT
    to_char(run.created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc,
    strategy.code,
    run.market_slug,
    run.status,
    COALESCE(run.selected_outcome, '<none>') AS selected_outcome,
    COALESCE(run.entry_price::text, '<none>') AS entry_price,
    COALESCE(run.skip_reason, '<none>') AS skip_reason,
    COALESCE(paper_order.status, '<none>') AS paper_order_status,
    COALESCE(live_order.status, '<none>') AS live_order_status,
    COALESCE(live_order.response_status, '<none>') AS live_response_status,
    COALESCE(live_order.cancel_status, '<none>') AS live_cancel_status,
    COALESCE(live_order.filled_size::text, '<none>') AS live_filled_size,
    COALESCE(live_order.remaining_size::text, '<none>') AS live_remaining_size
FROM strategy_market_paper_runs run
JOIN strategies strategy
    ON strategy.id = run.strategy_id
LEFT JOIN paper_orders paper_order
    ON paper_order.id = run.paper_order_id
LEFT JOIN live_orders live_order
    ON live_order.paper_order_id = paper_order.id
WHERE strategy.live_stakes
  AND run.created_at_utc >= @since_utc
ORDER BY run.created_at_utc DESC, strategy.code
LIMIT 80;
""", ("since_utc", UtcDateTime(sinceUtc)));

await WriteSectionAsync("Target BTC Up Simple latest runs");
await WriteRowsAsync("""
SELECT
    to_char(run.created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc,
    run.market_slug,
    run.status,
    COALESCE(run.selected_outcome, '<none>') AS selected_outcome,
    COALESCE(run.entry_price::text, '<none>') AS entry_price,
    COALESCE(run.skip_reason, '<none>') AS skip_reason,
    COALESCE(paper_order.status, '<none>') AS paper_order_status,
    COALESCE(live_order.status, '<none>') AS live_order_status,
    COALESCE(live_order.response_status, '<none>') AS live_response_status,
    COALESCE(live_order.filled_size::text, '<none>') AS live_filled_size,
    COALESCE(live_order.remaining_size::text, '<none>') AS live_remaining_size
FROM strategy_market_paper_runs run
JOIN strategies strategy
    ON strategy.id = run.strategy_id
LEFT JOIN paper_orders paper_order
    ON paper_order.id = run.paper_order_id
LEFT JOIN live_orders live_order
    ON live_order.paper_order_id = paper_order.id
WHERE strategy.code = @target_strategy_code
ORDER BY run.created_at_utc DESC
LIMIT 30;
""", ("target_strategy_code", TargetStrategyCode));

await WriteSectionAsync("Active observed due backlog");
await WriteRowsAsync("""
SELECT
    CASE
        WHEN strategy.live_stakes THEN 'enabled_live'
        WHEN strategy.code ~ @middle_pattern THEN 'enabled_middle'
        WHEN strategy.code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'enabled_other_up_down_5m'
        ELSE 'enabled_other'
    END AS strategy_group,
    count(*) AS observed_due_runs,
    count(*) FILTER (WHERE run.entry_due_at_utc < now() - interval '60 seconds') AS already_expired_by_grace,
    to_char(min(run.entry_due_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS earliest_due_utc,
    to_char(max(run.entry_due_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_due_utc
FROM strategy_market_paper_runs run
JOIN strategies strategy
    ON strategy.id = run.strategy_id
WHERE strategy.enabled
  AND run.status = 'Observed'
  AND run.entry_due_at_utc <= now()
GROUP BY strategy_group
ORDER BY observed_due_runs DESC, strategy_group;
""", ("middle_pattern", MiddlePattern));

await WriteSectionAsync("All observed due backlog");
await WriteRowsAsync("""
SELECT
    CASE
        WHEN NOT strategy.enabled AND strategy.code ~ @middle_pattern THEN 'disabled_middle'
        WHEN NOT strategy.enabled THEN 'disabled_other'
        WHEN strategy.live_stakes THEN 'enabled_live'
        WHEN strategy.code ~ @middle_pattern THEN 'enabled_middle'
        WHEN strategy.code ~ '^(btc|eth|sol)_up_down_5m_' THEN 'enabled_other_up_down_5m'
        ELSE 'enabled_other'
    END AS strategy_group,
    count(*) AS observed_due_runs,
    count(*) FILTER (WHERE run.entry_due_at_utc < now() - interval '60 seconds') AS already_expired_by_grace,
    to_char(min(run.entry_due_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS earliest_due_utc,
    to_char(max(run.entry_due_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_due_utc
FROM strategy_market_paper_runs run
JOIN strategies strategy
    ON strategy.id = run.strategy_id
WHERE run.status = 'Observed'
  AND run.entry_due_at_utc <= now()
GROUP BY strategy_group
ORDER BY observed_due_runs DESC, strategy_group;
""", ("middle_pattern", MiddlePattern));

await WriteSectionAsync("Recent API errors");
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
LIMIT 20;
""");

await tx.CommitAsync();
await WriteLineAsync("");
await WriteLineAsync($"Live betting monitor snapshot finished at {DateTimeOffset.UtcNow:O}");

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
        var dbType = parameter.Value is DateTime ? NpgsqlDbType.TimestampTz : NpgsqlDbType.Text;
        command.Parameters.Add(parameter.Name, dbType).Value = parameter.Value;
    }

    await using var reader = await command.ExecuteReaderAsync();
    var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
    await WriteLineAsync(string.Join("\t", names));

    var rows = 0;
    while (await reader.ReadAsync())
    {
        var values = new string[reader.FieldCount];
        for (var index = 0; index < reader.FieldCount; index++)
        {
            values[index] = reader.IsDBNull(index)
                ? "<null>"
                : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        await WriteLineAsync(string.Join("\t", values));
        rows++;
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

static DateTime UtcDateTime(DateTimeOffset value)
{
    return value.UtcDateTime;
}
