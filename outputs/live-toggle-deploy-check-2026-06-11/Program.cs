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
    CommandTimeout = 30,
    ApplicationName = "LiveToggleDeployCheck"
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await ExecuteNonQueryAsync("SET default_transaction_read_only = on");
await ExecuteNonQueryAsync("SET statement_timeout = '10s'");

Console.WriteLine("== Database ==");
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
Console.WriteLine("== Live-toggle strategy seed state ==");
await PrintRowsAsync("""
WITH target AS MATERIALIZED (
    SELECT
        id,
        code,
        name,
        description,
        enabled,
        live_stakes,
        auto_live_paused,
        paper_stake_amount,
        live_stake_amount,
        live_available_balance,
        CASE
            WHEN code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$' THEN 'simple'
            WHEN code ~ '^btc_up_down_5m_(up|down)_maker(_50)?$' THEN 'maker'
            WHEN code LIKE '%_adjusted_diff_%' THEN 'adjusted_diff'
            WHEN code LIKE '%_shift_diff_%' THEN 'shift_diff'
            WHEN code LIKE '%_diff_%' THEN 'diff'
            ELSE 'other'
        END AS family
    FROM strategies
    WHERE code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$'
       OR code ~ '^btc_up_down_5m_(up|down)_maker(_50)?$'
       OR code LIKE '%_diff_%'
)
SELECT
    family,
    count(*) AS strategies,
    count(*) FILTER (WHERE enabled) AS enabled_count,
    count(*) FILTER (WHERE live_stakes) AS live_count,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused_count,
    count(*) FILTER (WHERE description LIKE 'Paper-only%') AS stale_paper_only_descriptions,
    min(paper_stake_amount) AS min_paper_stake,
    max(paper_stake_amount) AS max_paper_stake,
    min(live_stake_amount) AS min_live_stake,
    max(live_stake_amount) AS max_live_stake,
    min(live_available_balance) AS min_live_balance,
    max(live_available_balance) AS max_live_balance
FROM target
GROUP BY family
ORDER BY family;
""");

Console.WriteLine();
Console.WriteLine("== Simple rows ==");
await PrintRowsAsync("""
SELECT
    code,
    enabled,
    live_stakes,
    auto_live_paused,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    left(description, 120) AS description_prefix,
    updated_at_utc
FROM strategies
WHERE code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$'
ORDER BY code;
""");

Console.WriteLine();
Console.WriteLine("== Maker rows ==");
await PrintRowsAsync("""
SELECT
    code,
    enabled,
    live_stakes,
    auto_live_paused,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    left(description, 120) AS description_prefix,
    updated_at_utc
FROM strategies
WHERE code ~ '^btc_up_down_5m_(up|down)_maker(_50)?$'
ORDER BY code;
""");

Console.WriteLine();
Console.WriteLine("== Current Live-enabled strategies ==");
await PrintRowsAsync("""
SELECT
    code,
    name,
    enabled,
    live_stakes,
    auto_live_paused,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance
FROM strategies
WHERE live_stakes
ORDER BY code;
""");

Console.WriteLine();
Console.WriteLine("== All strategy live-toggle field health ==");
await PrintRowsAsync("""
SELECT
    count(*) AS strategies,
    count(*) FILTER (WHERE enabled) AS enabled_count,
    count(*) FILTER (WHERE live_stakes) AS live_enabled_count,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused_count,
    count(*) FILTER (WHERE paper_stake_amount IS NULL) AS null_paper_stake_count,
    count(*) FILTER (WHERE live_stake_amount IS NULL) AS null_live_stake_count,
    count(*) FILTER (WHERE live_available_balance IS NULL) AS null_live_balance_count,
    min(live_stake_amount) AS min_live_stake,
    max(live_stake_amount) AS max_live_stake,
    min(live_available_balance) AS min_live_balance,
    max(live_available_balance) AS max_live_balance
FROM strategies;
""");

Console.WriteLine();
Console.WriteLine("== Recent paper/live shadow decisions by family ==");
await PrintRowsAsync("""
WITH decision_rows AS MATERIALIZED (
    SELECT
        decision.status,
        decision.post_only,
        decision.source,
        decision.decision_created_at_utc,
        strategy.code,
        CASE
            WHEN strategy.code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$' THEN 'simple'
            WHEN strategy.code ~ '^btc_up_down_5m_(up|down)_maker(_50)?$' THEN 'maker'
            WHEN strategy.code LIKE '%_adjusted_diff_%' THEN 'adjusted_diff'
            WHEN strategy.code LIKE '%_shift_diff_%' THEN 'shift_diff'
            WHEN strategy.code LIKE '%_diff_%' THEN 'diff'
            ELSE 'other'
        END AS family
    FROM paper_live_shadow_decisions decision
    INNER JOIN strategies strategy ON strategy.id = decision.strategy_id
    WHERE decision.decision_created_at_utc >= now() - interval '3 hours'
)
SELECT
    family,
    status,
    post_only,
    count(*) AS decisions,
    min(decision_created_at_utc) AS first_decision_utc,
    max(decision_created_at_utc) AS latest_decision_utc
FROM decision_rows
GROUP BY family, status, post_only
ORDER BY latest_decision_utc DESC NULLS LAST, family, status, post_only;
""");

Console.WriteLine();
Console.WriteLine("== Recent paper/live shadow live orders by family ==");
await PrintRowsAsync("""
WITH order_rows AS MATERIALIZED (
    SELECT
        live_order.status,
        live_order.post_only,
        live_order.created_at_utc,
        live_order.validation_summary,
        strategy.code,
        CASE
            WHEN strategy.code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$' THEN 'simple'
            WHEN strategy.code ~ '^btc_up_down_5m_(up|down)_maker(_50)?$' THEN 'maker'
            WHEN strategy.code LIKE '%_adjusted_diff_%' THEN 'adjusted_diff'
            WHEN strategy.code LIKE '%_shift_diff_%' THEN 'shift_diff'
            WHEN strategy.code LIKE '%_diff_%' THEN 'diff'
            ELSE 'other'
        END AS family
    FROM live_orders live_order
    INNER JOIN strategies strategy ON strategy.id = live_order.strategy_id
    WHERE live_order.created_at_utc >= now() - interval '3 hours'
      AND live_order.execution_source = 'paper_live_shadow_test'
)
SELECT
    family,
    status,
    post_only,
    count(*) AS live_orders,
    max(created_at_utc) AS latest_created_utc,
    left(coalesce(max(validation_summary), ''), 200) AS validation_summary_prefix
FROM order_rows
GROUP BY family, status, post_only
ORDER BY latest_created_utc DESC NULLS LAST, family, status, post_only;
""");

Console.WriteLine();
Console.WriteLine("== Recent paper/live shadow discrepancies ==");
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
ORDER BY latest_created_utc DESC NULLS LAST;
""");

Console.WriteLine();
Console.WriteLine("== Recent live trading events ==");
await PrintRowsAsync("""
SELECT
    action,
    status,
    count(*) AS events,
    max(created_at_utc) AS latest_created_utc,
    left(coalesce(max(details), ''), 240) AS details_prefix
FROM live_trading_events
WHERE created_at_utc >= now() - interval '3 hours'
  AND (
      action ILIKE '%PaperLiveShadow%'
      OR action ILIKE '%StrategyLiveBalance%'
      OR action ILIKE '%Live%'
  )
GROUP BY action, status
ORDER BY latest_created_utc DESC NULLS LAST, action, status;
""");

Console.WriteLine();
Console.WriteLine("== Recent open live orders ==");
await PrintRowsAsync("""
SELECT
    strategy.code,
    live_order.status,
    live_order.outcome,
    live_order.price,
    live_order.size_shares,
    live_order.notional_usd,
    live_order.order_type,
    live_order.post_only,
    live_order.execution_source,
    live_order.created_at_utc,
    live_order.expires_at_utc,
    left(coalesce(live_order.validation_summary, ''), 200) AS validation_summary_prefix
FROM live_orders live_order
INNER JOIN strategies strategy ON strategy.id = live_order.strategy_id
WHERE live_order.status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested')
ORDER BY live_order.created_at_utc DESC
LIMIT 25;
""");

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
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
        DateTime dateTime => dateTime.Kind == DateTimeKind.Utc
            ? dateTime.ToString("O", CultureInfo.InvariantCulture)
            : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.########", CultureInfo.InvariantCulture),
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        float number => number.ToString("0.###", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}

static string Pad(string value, int width)
{
    var text = value.Length <= width ? value : value[..Math.Max(0, width - 3)] + "...";
    return text.PadRight(width);
}
