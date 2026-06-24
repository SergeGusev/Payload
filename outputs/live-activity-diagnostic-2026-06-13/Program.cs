using System.Data;
using System.Globalization;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 2;
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    Host = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE") ?? "192.168.0.101",
    Timeout = 10,
    CommandTimeout = 60,
    ApplicationName = "LiveActivityDiagnostic"
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await ExecuteNonQueryAsync("SET TRANSACTION READ ONLY");
await ExecuteNonQueryAsync("SET LOCAL statement_timeout = '20s'");

Console.WriteLine("== Database now ==");
await PrintRowsAsync("""
SELECT
    now() AT TIME ZONE 'UTC' AS db_now_utc,
    current_database() AS database_name;
""");

Console.WriteLine();
Console.WriteLine("== Service heartbeat ==");
await PrintRowsAsync("""
SELECT
    service_name,
    status,
    mode,
    version,
    started_at_utc,
    last_heartbeat_utc,
    round(extract(epoch FROM ((now() AT TIME ZONE 'UTC') - (last_heartbeat_utc AT TIME ZONE 'UTC'))))::int AS heartbeat_age_seconds,
    current_loop,
    left(coalesce(last_error, ''), 240) AS last_error_prefix
FROM service_heartbeats
WHERE service_name = 'PolyCopyTrader.Service';
""");

Console.WriteLine();
Console.WriteLine("== Current Live-enabled strategies ==");
await PrintRowsAsync("""
SELECT
    count(*) AS live_strategy_count,
    count(*) FILTER (WHERE enabled) AS enabled_count,
    count(*) FILTER (WHERE paused) AS paused_count,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused_count,
    min(live_available_balance) AS min_live_balance,
    max(live_available_balance) AS max_live_balance,
    sum(live_available_balance) AS total_live_balance
FROM strategies
WHERE live_stakes;
""");

Console.WriteLine();
Console.WriteLine("== Current Live-enabled strategy rows ==");
await PrintRowsAsync("""
SELECT
    code,
    enabled,
    paused,
    auto_live_paused,
    live_stake_amount,
    live_available_balance,
    live_lost_counter,
    updated_at_utc
FROM strategies
WHERE live_stakes
ORDER BY code;
""");

Console.WriteLine();
Console.WriteLine("== Live orders for current Live strategies by window ==");
await PrintRowsAsync("""
WITH windows(label, window_start_utc, sort_order) AS (
    VALUES
        ('1h', now() - interval '1 hour', 1),
        ('6h', now() - interval '6 hours', 2),
        ('12h', now() - interval '12 hours', 3),
        ('24h', now() - interval '24 hours', 4),
        ('all', '-infinity'::timestamptz, 5)
)
SELECT
    w.label,
    count(*) FILTER (WHERE live_order.created_at_utc >= w.window_start_utc) AS created,
    count(*) FILTER (WHERE live_order.submitted_at_utc >= w.window_start_utc) AS submitted,
    count(*) FILTER (WHERE live_order.updated_at_utc >= w.window_start_utc) AS updated,
    count(*) FILTER (WHERE live_order.settled_at_utc >= w.window_start_utc) AS settled,
    count(*) FILTER (
        WHERE live_order.created_at_utc >= w.window_start_utc
          AND live_order.status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested')
    ) AS open_like_created,
    COALESCE(sum(live_order.notional_usd) FILTER (WHERE live_order.created_at_utc >= w.window_start_utc), 0)::numeric AS created_notional,
    COALESCE(sum(live_order.filled_notional_usd) FILTER (WHERE live_order.updated_at_utc >= w.window_start_utc), 0)::numeric AS updated_filled_notional,
    COALESCE(sum(live_order.realized_pnl_usd) FILTER (WHERE live_order.settled_at_utc >= w.window_start_utc), 0)::numeric AS settled_pnl
FROM windows w
CROSS JOIN live_orders live_order
INNER JOIN strategies strategy ON strategy.id = live_order.strategy_id
WHERE strategy.live_stakes
GROUP BY w.label, w.sort_order
ORDER BY w.sort_order;
""");

Console.WriteLine();
Console.WriteLine("== Live orders for current Live strategies by status, last 24h created/updated/settled ==");
await PrintRowsAsync("""
SELECT
    live_order.status,
    count(*) AS rows,
    count(*) FILTER (WHERE live_order.created_at_utc >= now() - interval '24 hours') AS created_24h,
    count(*) FILTER (WHERE live_order.updated_at_utc >= now() - interval '24 hours') AS updated_24h,
    count(*) FILTER (WHERE live_order.settled_at_utc >= now() - interval '24 hours') AS settled_24h,
    max(live_order.created_at_utc) AS latest_created_utc,
    max(live_order.submitted_at_utc) AS latest_submitted_utc,
    max(live_order.updated_at_utc) AS latest_updated_utc,
    max(live_order.settled_at_utc) AS latest_settled_utc,
    COALESCE(sum(live_order.realized_pnl_usd) FILTER (WHERE live_order.settled_at_utc >= now() - interval '24 hours'), 0)::numeric AS settled_pnl_24h
FROM live_orders live_order
INNER JOIN strategies strategy ON strategy.id = live_order.strategy_id
WHERE strategy.live_stakes
GROUP BY live_order.status
ORDER BY latest_created_utc DESC NULLS LAST, live_order.status;
""");

Console.WriteLine();
Console.WriteLine("== Latest live orders for current Live strategies ==");
await PrintRowsAsync("""
SELECT
    strategy.code,
    live_order.status,
    live_order.side,
    live_order.outcome,
    live_order.price,
    live_order.size_shares,
    live_order.notional_usd,
    live_order.filled_size,
    live_order.filled_notional_usd,
    live_order.realized_pnl_usd,
    live_order.created_at_utc,
    live_order.submitted_at_utc,
    live_order.updated_at_utc,
    live_order.settled_at_utc,
    live_order.execution_source,
    left(coalesce(live_order.validation_summary, ''), 200) AS validation_summary_prefix
FROM live_orders live_order
INNER JOIN strategies strategy ON strategy.id = live_order.strategy_id
WHERE strategy.live_stakes
ORDER BY live_order.created_at_utc DESC
LIMIT 20;
""");

Console.WriteLine();
Console.WriteLine("== Paper/live shadow decisions by window ==");
await PrintRowsAsync("""
WITH windows(label, window_start_utc, sort_order) AS (
    VALUES
        ('1h', now() - interval '1 hour', 1),
        ('6h', now() - interval '6 hours', 2),
        ('12h', now() - interval '12 hours', 3),
        ('24h', now() - interval '24 hours', 4)
)
SELECT
    w.label,
    count(*) FILTER (WHERE decision.decision_created_at_utc >= w.window_start_utc) AS decisions,
    count(*) FILTER (WHERE decision.decision_created_at_utc >= w.window_start_utc AND decision.live_order_id IS NOT NULL) AS linked_live_orders,
    count(*) FILTER (WHERE decision.decision_created_at_utc >= w.window_start_utc AND decision.status = 'Matched') AS matched_decisions,
    count(*) FILTER (WHERE decision.decision_created_at_utc >= w.window_start_utc AND decision.status = 'Submitted') AS submitted_decisions,
    count(*) FILTER (WHERE decision.decision_created_at_utc >= w.window_start_utc AND decision.status = 'Rejected') AS rejected_decisions,
    max(decision.decision_created_at_utc) FILTER (WHERE decision.decision_created_at_utc >= w.window_start_utc) AS latest_decision_utc
FROM windows w
CROSS JOIN paper_live_shadow_decisions decision
INNER JOIN strategies strategy ON strategy.id = decision.strategy_id
WHERE strategy.live_stakes
GROUP BY w.label, w.sort_order
ORDER BY w.sort_order;
""");

Console.WriteLine();
Console.WriteLine("== Recent paper/live shadow decision statuses ==");
await PrintRowsAsync("""
SELECT
    decision.status,
    decision.post_only,
    strategy.code,
    count(*) AS decisions,
    max(decision.decision_created_at_utc) AS latest_decision_utc,
    max(decision.updated_at_utc) AS latest_updated_utc
FROM paper_live_shadow_decisions decision
INNER JOIN strategies strategy ON strategy.id = decision.strategy_id
WHERE decision.decision_created_at_utc >= now() - interval '24 hours'
  AND strategy.live_stakes
GROUP BY decision.status, decision.post_only, strategy.code
ORDER BY latest_decision_utc DESC NULLS LAST, strategy.code;
""");

Console.WriteLine();
Console.WriteLine("== Paper strategy runs for current Live strategies, last 24h ==");
await PrintRowsAsync("""
SELECT
    strategy.code,
    run.status,
    coalesce(run.skip_reason, '') AS skip_reason,
    count(*) AS runs,
    max(run.entry_due_at_utc) AS latest_entry_due_utc,
    max(run.entered_at_utc) AS latest_entered_utc,
    max(run.updated_at_utc) AS latest_updated_utc,
    max(run.settled_at_utc) AS latest_settled_utc
FROM strategy_market_paper_runs run
INNER JOIN strategies strategy ON strategy.id = run.strategy_id
WHERE strategy.live_stakes
  AND run.updated_at_utc >= now() - interval '24 hours'
GROUP BY strategy.code, run.status, run.skip_reason
ORDER BY latest_updated_utc DESC NULLS LAST, strategy.code, run.status, skip_reason;
""");

Console.WriteLine();
Console.WriteLine("== Live trading events, last 24h ==");
await PrintRowsAsync("""
SELECT
    action,
    status,
    count(*) AS events,
    max(created_at_utc) AS latest_created_utc,
    left(coalesce(max(details), ''), 240) AS details_prefix
FROM live_trading_events
WHERE created_at_utc >= now() - interval '24 hours'
GROUP BY action, status
ORDER BY latest_created_utc DESC NULLS LAST, action, status;
""");

Console.WriteLine();
Console.WriteLine("== Paper/live shadow discrepancies, last 24h ==");
await PrintRowsAsync("""
SELECT
    classification,
    severity,
    count(*) AS discrepancies,
    max(created_at_utc) AS latest_created_utc,
    left(coalesce(max(details), ''), 240) AS details_prefix
FROM paper_live_shadow_discrepancies
WHERE created_at_utc >= now() - interval '24 hours'
GROUP BY classification, severity
ORDER BY latest_created_utc DESC NULLS LAST, classification, severity;
""");

Console.WriteLine();
Console.WriteLine("== API errors, last 24h ==");
await PrintRowsAsync("""
SELECT
    component,
    operation,
    count(*) AS errors,
    max(created_at_utc) AS latest_created_utc,
    left(coalesce(max(message), ''), 240) AS message_prefix
FROM api_errors
WHERE created_at_utc >= now() - interval '24 hours'
GROUP BY component, operation
ORDER BY latest_created_utc DESC NULLS LAST, component, operation;
""");

await transaction.CommitAsync();
return 0;

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}

async Task PrintRowsAsync(string sql)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
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
            widths[i] = Math.Min(80, Math.Max(widths[i], row[i].Length));
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
        DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.########", CultureInfo.InvariantCulture),
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        float number => number.ToString("0.###", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}

static string Pad(string value, int width)
{
    return value.Length > width
        ? string.Concat(value.AsSpan(0, Math.Max(0, width - 1)), "...")
        : value.PadRight(width);
}
