using System.Data;
using System.Globalization;
using Npgsql;

const string TargetStrategyCode = "eth_up_down_5m_down_bps_9_fak";

var outputDirectory = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? "outputs/eth-down-9-fak-monitor-2026-06-18";
var sinceArg = args.FirstOrDefault(arg => arg.StartsWith("--since=", StringComparison.OrdinalIgnoreCase));
var sinceUtc = sinceArg is null
    ? DateTimeOffset.UtcNow.AddHours(-2)
    : DateTimeOffset.Parse(sinceArg["--since=".Length..], CultureInfo.InvariantCulture).ToUniversalTime();
var labelArg = args.FirstOrDefault(arg => arg.StartsWith("--label=", StringComparison.OrdinalIgnoreCase));
var label = labelArg is null
    ? DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
    : SanitizeLabel(labelArg["--label=".Length..]);

Directory.CreateDirectory(outputDirectory);
var outputPath = Path.Combine(outputDirectory, $"snapshot-{label}.txt");

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "EthDown9FakMonitor",
    Timeout = 10,
    CommandTimeout = 45
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await ExecuteNonQueryAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '40s';
SET LOCAL lock_timeout = '500ms';
""");

await using var writer = new StreamWriter(outputPath, append: false);

await WriteLineAsync($"ETH Down 9 FAK monitor snapshot captured at {DateTimeOffset.UtcNow:O}");
await WriteLineAsync($"Since UTC: {sinceUtc:O}");
await WriteScalarAsync("database", "SELECT current_database();");
await WriteScalarAsync("server_address", "SELECT inet_server_addr()::text;");
await WriteScalarAsync("server_time_utc", "SELECT to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"');");

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
LIMIT 5;
""");

await WriteSectionAsync("Target strategy");
await WriteRowsAsync("""
SELECT
    id,
    code,
    name,
    enabled,
    live_stakes,
    auto_live_paused,
    paused,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    to_char(live_enabled_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS live_enabled_utc,
    to_char(updated_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS updated_utc
FROM strategies
WHERE code = @target_code;
""", ("target_code", TargetStrategyCode));

await WriteSectionAsync("Target summary since window");
await WriteRowsAsync("""
WITH target AS (
    SELECT id FROM strategies WHERE code = @target_code
),
run_agg AS (
    SELECT
        count(*)::integer AS runs,
        count(*) FILTER (WHERE status = 'Observed')::integer AS observed,
        count(*) FILTER (WHERE status = 'Entered')::integer AS entered,
        count(*) FILTER (WHERE status = 'Skipped')::integer AS skipped,
        count(*) FILTER (WHERE status = 'Settled')::integer AS settled,
        count(*) FILTER (WHERE status = 'Settled' AND COALESCE(realized_pnl_usd, 0) > 0)::integer AS paper_won,
        count(*) FILTER (WHERE status = 'Settled' AND COALESCE(realized_pnl_usd, 0) < 0)::integer AS paper_lost,
        COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE status = 'Settled'), 0) AS paper_realized,
        max(created_at_utc) AS latest_run_utc,
        max(updated_at_utc) AS latest_run_update_utc
    FROM strategy_market_paper_runs
    WHERE strategy_id = (SELECT id FROM target)
      AND created_at_utc >= @since_utc
),
live_agg AS (
    SELECT
        count(*)::integer AS live_orders,
        count(*) FILTER (WHERE filled_size > 0)::integer AS live_filled_orders,
        count(*) FILTER (WHERE status = 'Matched')::integer AS live_matched_orders,
        count(*) FILTER (WHERE status = 'Rejected')::integer AS live_rejected_orders,
        count(*) FILTER (WHERE status = 'PreflightRejected')::integer AS live_preflight_rejected_orders,
        count(*) FILTER (WHERE settled_at_utc IS NOT NULL)::integer AS live_settled_orders,
        COALESCE(sum(filled_size), 0) AS live_filled_size,
        COALESCE(sum(filled_notional_usd), 0) AS live_filled_notional,
        COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL), 0) AS live_realized,
        max(created_at_utc) AS latest_live_order_utc,
        max(updated_at_utc) AS latest_live_update_utc
    FROM live_orders
    WHERE strategy_id = (SELECT id FROM target)
      AND created_at_utc >= @since_utc
),
decision_agg AS (
    SELECT
        count(*)::integer AS shadow_decisions,
        count(*) FILTER (WHERE order_type = 'FAK')::integer AS fak_decisions,
        count(*) FILTER (WHERE status = 'LiveSubmitted')::integer AS live_submitted_decisions,
        count(*) FILTER (WHERE status = 'LiveRejected')::integer AS live_rejected_decisions,
        count(*) FILTER (WHERE status = 'LivePreflightRejected')::integer AS live_preflight_rejected_decisions,
        max(decision_created_at_utc) AS latest_decision_utc
    FROM paper_live_shadow_decisions
    WHERE strategy_id = (SELECT id FROM target)
      AND decision_created_at_utc >= @since_utc
)
SELECT
    run_agg.runs,
    run_agg.observed,
    run_agg.entered,
    run_agg.skipped,
    run_agg.settled,
    run_agg.paper_won,
    run_agg.paper_lost,
    run_agg.paper_realized,
    to_char(run_agg.latest_run_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_run_utc,
    to_char(run_agg.latest_run_update_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_run_update_utc,
    live_agg.live_orders,
    live_agg.live_filled_orders,
    live_agg.live_matched_orders,
    live_agg.live_rejected_orders,
    live_agg.live_preflight_rejected_orders,
    live_agg.live_settled_orders,
    live_agg.live_filled_size,
    live_agg.live_filled_notional,
    live_agg.live_realized,
    to_char(live_agg.latest_live_order_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_live_order_utc,
    to_char(live_agg.latest_live_update_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_live_update_utc,
    decision_agg.shadow_decisions,
    decision_agg.fak_decisions,
    decision_agg.live_submitted_decisions,
    decision_agg.live_rejected_decisions,
    decision_agg.live_preflight_rejected_decisions,
    to_char(decision_agg.latest_decision_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_decision_utc
FROM run_agg, live_agg, decision_agg;
""", ("target_code", TargetStrategyCode), ("since_utc", UtcDateTime(sinceUtc)));

await WriteSectionAsync("Run statuses since window");
await WriteRowsAsync("""
SELECT
    status,
    COALESCE(skip_reason, '<none>') AS skip_reason,
    count(*) AS count,
    to_char(max(created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_created_utc,
    to_char(max(updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_updated_utc
FROM strategy_market_paper_runs
WHERE strategy_id = (SELECT id FROM strategies WHERE code = @target_code)
  AND created_at_utc >= @since_utc
GROUP BY status, COALESCE(skip_reason, '<none>')
ORDER BY count DESC, status, skip_reason;
""", ("target_code", TargetStrategyCode), ("since_utc", UtcDateTime(sinceUtc)));

await WriteSectionAsync("Live order statuses since window");
await WriteRowsAsync("""
SELECT
    status,
    order_type,
    COALESCE(post_only::text, '<null>') AS post_only,
    response_status,
    cancel_status,
    count(*) AS count,
    COALESCE(sum(filled_size), 0) AS filled_size,
    COALESCE(sum(remaining_size), 0) AS remaining_size,
    COALESCE(sum(filled_notional_usd), 0) AS filled_notional_usd,
    COALESCE(sum(cost_basis_usd), 0) AS cost_basis_usd,
    COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL), 0) AS realized_pnl_usd,
    to_char(max(created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_created_utc,
    to_char(max(updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_updated_utc
FROM live_orders
WHERE strategy_id = (SELECT id FROM strategies WHERE code = @target_code)
  AND created_at_utc >= @since_utc
GROUP BY status, order_type, COALESCE(post_only::text, '<null>'), response_status, cancel_status
ORDER BY latest_created_utc DESC, count DESC;
""", ("target_code", TargetStrategyCode), ("since_utc", UtcDateTime(sinceUtc)));

await WriteSectionAsync("Shadow decisions since window");
await WriteRowsAsync("""
SELECT
    status,
    order_type,
    post_only,
    count(*) AS count,
    to_char(max(decision_created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_decision_utc,
    to_char(max(updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_updated_utc
FROM paper_live_shadow_decisions
WHERE strategy_id = (SELECT id FROM strategies WHERE code = @target_code)
  AND decision_created_at_utc >= @since_utc
GROUP BY status, order_type, post_only
ORDER BY latest_decision_utc DESC, count DESC;
""", ("target_code", TargetStrategyCode), ("since_utc", UtcDateTime(sinceUtc)));

await WriteSectionAsync("Recent runs");
await WriteRowsAsync("""
SELECT
    to_char(run.created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc,
    to_char(run.updated_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS updated_utc,
    run.market_slug,
    run.status,
    COALESCE(run.selected_outcome, '<none>') AS selected_outcome,
    COALESCE(run.entry_price::text, '<none>') AS entry_price,
    COALESCE(run.stake_usd::text, '<none>') AS stake_usd,
    COALESCE(run.size_shares::text, '<none>') AS size_shares,
    COALESCE(run.skip_reason, '<none>') AS skip_reason,
    COALESCE(paper_order.status, '<none>') AS paper_order_status,
    COALESCE(live_order.status, '<none>') AS live_order_status,
    COALESCE(live_order.order_type, '<none>') AS live_order_type,
    COALESCE(live_order.response_status, '<none>') AS live_response_status,
    COALESCE(live_order.filled_size::text, '<none>') AS live_filled_size,
    COALESCE(live_order.remaining_size::text, '<none>') AS live_remaining_size,
    COALESCE(live_order.average_fill_price::text, '<none>') AS live_avg_fill_price,
    COALESCE(live_order.validation_summary, '<none>') AS live_validation
FROM strategy_market_paper_runs run
LEFT JOIN paper_orders paper_order ON paper_order.id = run.paper_order_id
LEFT JOIN live_orders live_order ON live_order.paper_order_id = paper_order.id
WHERE run.strategy_id = (SELECT id FROM strategies WHERE code = @target_code)
ORDER BY run.created_at_utc DESC
LIMIT 25;
""", ("target_code", TargetStrategyCode));

await WriteSectionAsync("Recent live orders");
await WriteRowsAsync("""
SELECT
    to_char(created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc,
    to_char(submitted_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS submitted_utc,
    to_char(updated_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS updated_utc,
    status,
    order_type,
    COALESCE(post_only::text, '<null>') AS post_only,
    response_status,
    outcome,
    price,
    size_shares,
    notional_usd,
    filled_size,
    remaining_size,
    COALESCE(average_fill_price::text, '<none>') AS average_fill_price,
    filled_notional_usd,
    cost_basis_usd,
    COALESCE(realized_pnl_usd::text, '<none>') AS realized_pnl_usd,
    COALESCE(validation_summary, '<none>') AS validation_summary,
    id AS live_order_id
FROM live_orders
WHERE strategy_id = (SELECT id FROM strategies WHERE code = @target_code)
ORDER BY created_at_utc DESC
LIMIT 25;
""", ("target_code", TargetStrategyCode));

await WriteSectionAsync("Recent shadow decisions");
await WriteRowsAsync("""
SELECT
    to_char(decision_created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS decision_utc,
    to_char(updated_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS updated_utc,
    market_id,
    outcome,
    side,
    limit_price,
    target_notional_usd,
    requested_size_shares,
    max_reserved_notional_usd,
    order_type,
    post_only,
    status,
    COALESCE(live_order_id::text, '<none>') AS live_order_id,
    COALESCE(paper_order_id::text, '<none>') AS paper_order_id
FROM paper_live_shadow_decisions
WHERE strategy_id = (SELECT id FROM strategies WHERE code = @target_code)
ORDER BY decision_created_at_utc DESC
LIMIT 25;
""", ("target_code", TargetStrategyCode));

await WriteSectionAsync("Recent target API errors");
await WriteRowsAsync("""
SELECT
    to_char(created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc,
    component,
    operation,
    message
FROM api_errors
WHERE created_at_utc >= @since_utc
  AND (
      message ILIKE '%' || @target_code || '%'
      OR component ILIKE '%Live%'
      OR operation ILIKE '%Live%'
      OR operation ILIKE '%Order%'
      OR component ILIKE '%Clob%'
  )
ORDER BY created_at_utc DESC
LIMIT 30;
""", ("target_code", TargetStrategyCode), ("since_utc", UtcDateTime(sinceUtc)));

await WriteSectionAsync("Recent live trading events");
await WriteRowsAsync("""
SELECT
    to_char(created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc,
    action,
    status,
    details
FROM live_trading_events
WHERE created_at_utc >= @since_utc
ORDER BY created_at_utc DESC
LIMIT 30;
""", ("since_utc", UtcDateTime(sinceUtc)));

await WriteSectionAsync("Market data status");
await WriteRowsAsync("""
SELECT
    component,
    connection_state,
    subscribed_assets_count,
    to_char(last_message_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS last_message_utc,
    round(extract(epoch FROM (now() - last_message_utc))::numeric, 1) AS last_message_age_seconds,
    stale,
    COALESCE(last_error, '<none>') AS last_error,
    to_char(updated_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS updated_utc
FROM market_data_status
ORDER BY updated_at_utc DESC
LIMIT 10;
""");

await tx.CommitAsync();

Console.WriteLine($"snapshot={Path.GetFullPath(outputPath)}");
Console.WriteLine($"target={TargetStrategyCode}");
Console.WriteLine($"since_utc={sinceUtc:O}");

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

async Task WriteSectionAsync(string title)
{
    await writer.WriteLineAsync();
    await writer.WriteLineAsync("== " + title + " ==");
}

async Task WriteRowsAsync(string sql, params (string Name, object Value)[] parameters)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }

    await using var reader = await command.ExecuteReaderAsync();
    for (var index = 0; index < reader.FieldCount; index++)
    {
        if (index > 0)
        {
            await writer.WriteAsync('\t');
        }

        await writer.WriteAsync(reader.GetName(index));
    }

    await writer.WriteLineAsync();

    while (await reader.ReadAsync())
    {
        for (var index = 0; index < reader.FieldCount; index++)
        {
            if (index > 0)
            {
                await writer.WriteAsync('\t');
            }

            var value = reader.IsDBNull(index) ? "<null>" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture);
            await writer.WriteAsync(Tsv(value));
        }

        await writer.WriteLineAsync();
    }
}

async Task WriteLineAsync(string line)
{
    await writer.WriteLineAsync(line);
}

static DateTime UtcDateTime(DateTimeOffset value)
{
    return value.UtcDateTime;
}

static string SanitizeLabel(string value)
{
    var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray();
    return new string(chars);
}

static string Tsv(string? value)
{
    return (value ?? string.Empty)
        .Replace('\t', ' ')
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);
}
