using System.Data;
using System.Globalization;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not configured.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    Host = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE") ?? "192.168.0.101",
    Timeout = 10,
    CommandTimeout = 60,
    ApplicationName = "BtcUpSimpleLiveDiagnostic"
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await ExecuteNonQueryAsync("SET default_transaction_read_only = on");
await ExecuteNonQueryAsync("SET statement_timeout = '30s'");

await SectionAsync("Database", """
SELECT
    now() AT TIME ZONE 'UTC' AS db_now_utc,
    current_database() AS database_name;
""");

await SectionAsync("Service heartbeat", """
SELECT
    service_name,
    status,
    mode,
    version,
    started_at_utc,
    last_heartbeat_utc,
    round(extract(epoch FROM ((now() AT TIME ZONE 'UTC') - (last_heartbeat_utc AT TIME ZONE 'UTC'))))::int AS heartbeat_age_seconds,
    current_loop,
    left(coalesce(last_error, ''), 300) AS last_error_prefix
FROM service_heartbeats
WHERE service_name = 'PolyCopyTrader.Service';
""");

await SectionAsync("Target strategy state", """
SELECT
    id,
    code,
    name,
    enabled,
    paused,
    paused_until_utc,
    live_stakes,
    auto_live_paused,
    live_enabled_at_utc,
    paper_stake_amount,
    live_stake_amount,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    live_available_balance,
    created_at_utc,
    updated_at_utc,
    left(description, 180) AS description_prefix,
    (enabled AND NOT paused AND live_stakes AND NOT auto_live_paused) AS effective_live
FROM strategies
WHERE code = 'btc_up_down_5m_up_simple';
""");

await SectionAsync("Simple strategy states", """
SELECT
    code,
    enabled,
    paused,
    live_stakes,
    auto_live_paused,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    live_enabled_at_utc,
    updated_at_utc
FROM strategies
WHERE code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$'
ORDER BY code;
""");

await SectionAsync("Current Live-enabled strategies", """
SELECT
    code,
    name,
    enabled,
    paused,
    live_stakes,
    auto_live_paused,
    live_stake_amount,
    live_available_balance,
    live_enabled_at_utc
FROM strategies
WHERE live_stakes
ORDER BY code;
""");

await SectionAsync("Target run summary - 24h", """
SELECT
    run.status,
    coalesce(run.skip_reason, '') AS skip_reason,
    run.selected_outcome,
    count(*) AS runs,
    count(*) FILTER (WHERE run.paper_order_id IS NULL) AS without_paper_order,
    count(*) FILTER (WHERE run.paper_order_id IS NOT NULL) AS with_paper_order,
    min(run.entry_due_at_utc) AS first_due_utc,
    max(run.entry_due_at_utc) AS latest_due_utc,
    max(run.updated_at_utc) AS latest_update_utc
FROM strategy_market_paper_runs run
JOIN strategies strategy ON strategy.id = run.strategy_id
WHERE strategy.code = 'btc_up_down_5m_up_simple'
  AND run.entry_due_at_utc >= now() - interval '24 hours'
GROUP BY run.status, coalesce(run.skip_reason, ''), run.selected_outcome
ORDER BY latest_update_utc DESC NULLS LAST, runs DESC;
""");

await SectionAsync("Target recent runs", """
SELECT
    run.status,
    run.skip_reason,
    run.selected_outcome,
    run.entry_price,
    run.stake_usd,
    run.size_shares,
    run.market_slug,
    run.entry_due_at_utc,
    run.entered_at_utc,
    run.paper_order_id IS NOT NULL AS has_paper_order,
    run.updated_at_utc,
    left(coalesce(run.skip_diagnostics_json::text, ''), 420) AS diagnostics_prefix
FROM strategy_market_paper_runs run
JOIN strategies strategy ON strategy.id = run.strategy_id
WHERE strategy.code = 'btc_up_down_5m_up_simple'
ORDER BY coalesce(run.updated_at_utc, run.entry_due_at_utc) DESC
LIMIT 36;
""");

await SectionAsync("Target paper orders - 24h summary", """
SELECT
    paper_order.status,
    paper_order.outcome,
    paper_order.execution_source,
    count(*) AS orders,
    min(paper_order.created_at_utc) AS first_created_utc,
    max(paper_order.created_at_utc) AS latest_created_utc,
    count(*) FILTER (WHERE coalesce(paper_order.raw_decision_json::text, '') LIKE '%paper_live_shadow_test%') AS with_shadow_json
FROM paper_orders paper_order
JOIN strategies strategy ON strategy.id = paper_order.strategy_id
WHERE strategy.code = 'btc_up_down_5m_up_simple'
  AND paper_order.created_at_utc >= now() - interval '24 hours'
GROUP BY paper_order.status, paper_order.outcome, paper_order.execution_source
ORDER BY latest_created_utc DESC NULLS LAST;
""");

await SectionAsync("Target recent paper orders", """
SELECT
    paper_order.status,
    paper_order.outcome,
    paper_order.price,
    paper_order.size_shares,
    paper_order.notional_usd,
    paper_order.execution_source,
    paper_order.correlation_id,
    paper_order.created_at_utc,
    paper_order.expires_at_utc,
    left(coalesce(paper_order.raw_decision_json::text, ''), 500) AS raw_decision_prefix
FROM paper_orders paper_order
JOIN strategies strategy ON strategy.id = paper_order.strategy_id
WHERE strategy.code = 'btc_up_down_5m_up_simple'
ORDER BY paper_order.created_at_utc DESC
LIMIT 24;
""");

await SectionAsync("Target shadow decisions - 24h", """
SELECT
    decision.status,
    decision.outcome,
    decision.post_only,
    decision.order_type,
    count(*) AS decisions,
    min(decision.decision_created_at_utc) AS first_decision_utc,
    max(decision.decision_created_at_utc) AS latest_decision_utc,
    count(*) FILTER (WHERE decision.paper_order_id IS NOT NULL) AS linked_paper_orders,
    count(*) FILTER (WHERE decision.live_order_id IS NOT NULL) AS linked_live_orders
FROM paper_live_shadow_decisions decision
JOIN strategies strategy ON strategy.id = decision.strategy_id
WHERE strategy.code = 'btc_up_down_5m_up_simple'
  AND decision.decision_created_at_utc >= now() - interval '24 hours'
GROUP BY decision.status, decision.outcome, decision.post_only, decision.order_type
ORDER BY latest_decision_utc DESC NULLS LAST;
""");

await SectionAsync("Target recent shadow decisions", """
SELECT
    decision.status,
    decision.outcome,
    decision.limit_price,
    decision.target_notional_usd,
    decision.requested_size_shares,
    decision.max_reserved_notional_usd,
    decision.post_only,
    decision.source,
    decision.market_id,
    decision.condition_id,
    decision.decision_created_at_utc,
    decision.submit_deadline_utc,
    decision.cancel_deadline_utc,
    decision.paper_order_id IS NOT NULL AS has_paper_order,
    decision.live_order_id IS NOT NULL AS has_live_order,
    decision.updated_at_utc
FROM paper_live_shadow_decisions decision
JOIN strategies strategy ON strategy.id = decision.strategy_id
WHERE strategy.code = 'btc_up_down_5m_up_simple'
ORDER BY decision.decision_created_at_utc DESC
LIMIT 24;
""");

await SectionAsync("Target live orders - 24h", """
SELECT
    live_order.status,
    live_order.outcome,
    live_order.price,
    live_order.size_shares,
    live_order.notional_usd,
    live_order.response_status,
    live_order.validation_summary,
    live_order.execution_source,
    live_order.post_only,
    live_order.correlation_id,
    live_order.created_at_utc,
    live_order.submitted_at_utc,
    live_order.updated_at_utc,
    left(coalesce(live_order.raw_response_json::text, ''), 360) AS raw_response_prefix
FROM live_orders live_order
JOIN strategies strategy ON strategy.id = live_order.strategy_id
WHERE strategy.code = 'btc_up_down_5m_up_simple'
  AND live_order.created_at_utc >= now() - interval '24 hours'
ORDER BY live_order.created_at_utc DESC
LIMIT 36;
""");

await SectionAsync("Target live orders all-time summary", """
SELECT
    count(*) AS live_orders,
    count(*) FILTER (WHERE live_order.execution_source = 'paper_live_shadow_test') AS live_shadow_orders,
    min(live_order.created_at_utc) AS first_created_utc,
    max(live_order.created_at_utc) AS latest_created_utc,
    count(*) FILTER (WHERE live_order.status IN ('PreflightRejected', 'Rejected', 'Error')) AS rejected_or_error,
    count(*) FILTER (WHERE live_order.status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested')) AS open_like
FROM live_orders live_order
JOIN strategies strategy ON strategy.id = live_order.strategy_id
WHERE strategy.code = 'btc_up_down_5m_up_simple';
""");

await SectionAsync("Target shadow discrepancies", """
SELECT
    discrepancy.classification,
    discrepancy.severity,
    discrepancy.created_at_utc,
    left(discrepancy.details, 500) AS details_prefix
FROM paper_live_shadow_discrepancies discrepancy
JOIN strategies strategy ON strategy.id = discrepancy.strategy_id
WHERE strategy.code = 'btc_up_down_5m_up_simple'
ORDER BY discrepancy.created_at_utc DESC
LIMIT 20;
""");

await SectionAsync("Recent PaperLiveShadow live events", """
SELECT
    action,
    status,
    created_at_utc,
    left(details, 600) AS details_prefix
FROM live_trading_events
WHERE created_at_utc >= now() - interval '24 hours'
  AND action LIKE '%PaperLiveShadow%'
ORDER BY created_at_utc DESC
LIMIT 60;
""");

await SectionAsync("Recent API errors", """
SELECT
    component,
    operation,
    created_at_utc,
    left(message, 420) AS message_prefix
FROM api_errors
WHERE created_at_utc >= now() - interval '6 hours'
ORDER BY created_at_utc DESC
LIMIT 40;
""");

await SectionAsync("Open live exposure by condition - all strategies", """
SELECT
    live_order.condition_id,
    string_agg(DISTINCT strategy.code, ', ' ORDER BY strategy.code) AS strategy_codes,
    string_agg(DISTINCT live_order.outcome, ', ' ORDER BY live_order.outcome) AS outcomes,
    count(*) AS open_orders,
    sum(live_order.notional_usd) AS notional_usd,
    max(live_order.created_at_utc) AS latest_created_utc
FROM live_orders live_order
JOIN strategies strategy ON strategy.id = live_order.strategy_id
WHERE live_order.status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested')
GROUP BY live_order.condition_id
ORDER BY latest_created_utc DESC
LIMIT 30;
""");

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

async Task SectionAsync(string title, string sql)
{
    Console.WriteLine($"== {title} ==");
    await PrintRowsAsync(sql);
    Console.WriteLine();
}

async Task PrintRowsAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
    var fieldCount = reader.FieldCount;
    var rows = new List<string[]>();
    while (await reader.ReadAsync())
    {
        var values = new string[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            values[i] = FormatValue(reader.GetValue(i));
        }

        rows.Add(values);
    }

    var headers = Enumerable.Range(0, fieldCount).Select(reader.GetName).ToArray();
    if (rows.Count == 0)
    {
        Console.WriteLine("(no rows)");
        return;
    }

    var widths = new int[fieldCount];
    for (var i = 0; i < fieldCount; i++)
    {
        widths[i] = headers[i].Length;
    }

    foreach (var row in rows)
    {
        for (var i = 0; i < fieldCount; i++)
        {
            widths[i] = Math.Min(90, Math.Max(widths[i], row[i].Length));
        }
    }

    Console.WriteLine(string.Join(" | ", headers.Select((header, i) => Pad(header, widths[i]))));
    Console.WriteLine(string.Join("-+-", widths.Select(width => new string('-', width))));
    foreach (var row in rows)
    {
        Console.WriteLine(string.Join(" | ", row.Select((value, i) => Pad(value, widths[i]))));
    }
}

static string FormatValue(object value)
{
    return value switch
    {
        null => "",
        DBNull => "",
        DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.########", CultureInfo.InvariantCulture),
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        float number => number.ToString("0.###", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString("D"),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}

static string Pad(string value, int width)
{
    var text = value.Length <= width ? value : value[..Math.Max(0, width - 3)] + "...";
    return text.PadRight(width);
}
