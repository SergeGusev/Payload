using System.Globalization;
using Npgsql;

const string TargetCte = """
WITH target(code, threshold) AS (
    VALUES
        ('btc_up_down_5m_up_diff_1_revert_instant', 1),
        ('btc_up_down_5m_up_diff_2_revert_instant', 2),
        ('btc_up_down_5m_up_diff_3_revert_instant', 3),
        ('btc_up_down_5m_up_diff_4_revert_instant', 4),
        ('btc_up_down_5m_up_diff_5_revert_instant', 5)
)
""";

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not configured.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    Host = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE") ?? "192.168.0.101",
    Timeout = 10,
    CommandTimeout = 120,
    ApplicationName = "DiffRevertThresholdDiagnostic"
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await ExecuteNonQueryAsync("SET default_transaction_read_only = on");
await ExecuteNonQueryAsync("SET statement_timeout = '15s'");

Console.WriteLine("== Database ==");
await PrintRowsAsync("""
SELECT
    now() AT TIME ZONE 'UTC' AS db_now_utc,
    current_database() AS database_name;
""");

Console.WriteLine();
Console.WriteLine("== Target strategy lifecycle counts ==");
await PrintRowsAsync(TargetCte + """
SELECT
    target.threshold,
    strategy.name,
    strategy.enabled,
    strategy.live_stakes,
    count(run.id)::integer AS runs_total,
    count(*) FILTER (WHERE run.status = 'PendingEntry')::integer AS pending_entry,
    count(*) FILTER (WHERE run.status = 'Observed')::integer AS observed,
    count(*) FILTER (WHERE run.status = 'Entered')::integer AS entered,
    count(*) FILTER (WHERE run.status = 'Skipped')::integer AS skipped,
    count(*) FILTER (WHERE run.status = 'Settled')::integer AS settled,
    count(*) FILTER (WHERE run.status = 'Settled' AND run.realized_pnl_usd > 0)::integer AS won,
    count(*) FILTER (WHERE run.status = 'Settled' AND run.realized_pnl_usd < 0)::integer AS lost,
    count(*) FILTER (WHERE run.status = 'Skipped' AND run.skip_reason = 'diff_counter_threshold_not_reached')::integer AS threshold_not_reached,
    count(*) FILTER (WHERE run.status = 'Skipped' AND run.skip_reason = 'gtd_limit_not_filled')::integer AS gtd_not_filled,
    count(run.paper_order_id)::integer AS linked_orders,
    min(run.created_at_utc) AS first_run_created_utc,
    max(run.updated_at_utc) AS last_run_updated_utc,
    min(run.settled_at_utc) FILTER (WHERE run.status = 'Settled') AS first_settled_utc,
    max(run.settled_at_utc) FILTER (WHERE run.status = 'Settled') AS last_settled_utc
FROM target
INNER JOIN strategies strategy ON strategy.code = target.code
LEFT JOIN strategy_market_paper_runs run ON run.strategy_id = strategy.id
GROUP BY target.threshold, strategy.name, strategy.enabled, strategy.live_stakes
ORDER BY target.threshold;
""");

Console.WriteLine();
Console.WriteLine("== Target paper order counts ==");
await PrintRowsAsync(TargetCte + """
SELECT
    target.threshold,
    count(order_row.id)::integer AS orders_total,
    count(*) FILTER (WHERE order_row.status = 'Pending')::integer AS pending,
    count(*) FILTER (WHERE order_row.status = 'Filled')::integer AS filled,
    count(*) FILTER (WHERE order_row.status = 'Expired')::integer AS expired,
    count(*) FILTER (WHERE order_row.status = 'PartiallyFilled')::integer AS partially_filled,
    count(*) FILTER (WHERE order_row.status = 'PartiallyFilledExpired')::integer AS partially_filled_expired,
    min(order_row.created_at_utc) AS first_order_utc,
    max(order_row.created_at_utc) AS last_order_utc,
    min(order_row.price) AS min_order_price,
    max(order_row.price) AS max_order_price,
    round(avg(order_row.price), 8) AS avg_order_price
FROM target
INNER JOIN strategies strategy ON strategy.code = target.code
LEFT JOIN paper_orders order_row ON order_row.strategy_id = strategy.id
GROUP BY target.threshold
ORDER BY target.threshold;
""");

Console.WriteLine();
Console.WriteLine("== Entered or settled raw decision effective diff ==");
await PrintRowsAsync(TargetCte + """
SELECT
    target.threshold,
    count(order_row.id)::integer AS orders_with_decision_json,
    min(NULLIF(order_row.raw_decision_json ->> 'effective_diff', '')::numeric) AS min_effective_diff,
    max(NULLIF(order_row.raw_decision_json ->> 'effective_diff', '')::numeric) AS max_effective_diff,
    round(avg(NULLIF(order_row.raw_decision_json ->> 'effective_diff', '')::numeric), 4) AS avg_effective_diff,
    count(*) FILTER (WHERE NULLIF(order_row.raw_decision_json ->> 'effective_diff', '')::numeric >= target.threshold)::integer AS orders_meeting_threshold,
    count(*) FILTER (WHERE NULLIF(order_row.raw_decision_json ->> 'effective_diff', '')::numeric < target.threshold)::integer AS orders_below_threshold,
    count(*) FILTER (WHERE run.status = 'Settled')::integer AS settled_orders,
    count(*) FILTER (WHERE run.status = 'Skipped' AND run.skip_reason = 'gtd_limit_not_filled')::integer AS gtd_not_filled_orders
FROM target
INNER JOIN strategies strategy ON strategy.code = target.code
LEFT JOIN strategy_market_paper_runs run ON run.strategy_id = strategy.id AND run.paper_order_id IS NOT NULL
LEFT JOIN paper_orders order_row ON order_row.id = run.paper_order_id
GROUP BY target.threshold
ORDER BY target.threshold;
""");

Console.WriteLine();
Console.WriteLine("== Skip reasons by target strategy ==");
await PrintRowsAsync(TargetCte + """
SELECT
    target.threshold,
    coalesce(run.skip_reason, '<null>') AS skip_reason,
    count(*)::integer AS runs,
    min(run.updated_at_utc) AS first_updated_utc,
    max(run.updated_at_utc) AS last_updated_utc
FROM target
INNER JOIN strategies strategy ON strategy.code = target.code
INNER JOIN strategy_market_paper_runs run ON run.strategy_id = strategy.id
WHERE run.status = 'Skipped'
GROUP BY target.threshold, coalesce(run.skip_reason, '<null>')
ORDER BY target.threshold, runs DESC, skip_reason;
""");

Console.WriteLine();
Console.WriteLine("== Settled market set equality ==");
await PrintRowsAsync(TargetCte + """
,
settled AS (
    SELECT
        target.threshold,
        run.market_start_utc
    FROM target
    INNER JOIN strategies strategy ON strategy.code = target.code
    INNER JOIN strategy_market_paper_runs run ON run.strategy_id = strategy.id
    WHERE run.status = 'Settled'
),
per_market AS (
    SELECT
        market_start_utc,
        bool_or(threshold = 1) AS t1,
        bool_or(threshold = 2) AS t2,
        bool_or(threshold = 3) AS t3,
        bool_or(threshold = 4) AS t4,
        bool_or(threshold = 5) AS t5
    FROM settled
    GROUP BY market_start_utc
)
SELECT
    count(*)::integer AS markets_with_any_settled,
    count(*) FILTER (WHERE t1 AND t2 AND t3 AND t4 AND t5)::integer AS markets_all_five_settled,
    count(*) FILTER (WHERE t1 AND NOT t5)::integer AS markets_t1_without_t5,
    count(*) FILTER (WHERE t5 AND NOT t1)::integer AS markets_t5_without_t1,
    min(market_start_utc) AS first_market_start_utc,
    max(market_start_utc) AS last_market_start_utc
FROM per_market;
""");

Console.WriteLine();
Console.WriteLine("== Recent market outcomes for target strategies ==");
await PrintRowsAsync(TargetCte + """
SELECT
    run.market_start_utc,
    string_agg(
        target.threshold::text || ':' || run.status ||
            coalesce('/' || nullif(run.skip_reason, ''), '') ||
            coalesce('/order=' || order_row.status, '') ||
            coalesce('/eff=' || (order_row.raw_decision_json ->> 'effective_diff'), ''),
        ', ' ORDER BY target.threshold) AS threshold_statuses
FROM target
INNER JOIN strategies strategy ON strategy.code = target.code
INNER JOIN strategy_market_paper_runs run ON run.strategy_id = strategy.id
LEFT JOIN paper_orders order_row ON order_row.id = run.paper_order_id
WHERE run.status <> 'Observed'
GROUP BY run.market_start_utc
ORDER BY run.market_start_utc DESC
LIMIT 40;
""");

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

async Task PrintRowsAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    var columnCount = reader.FieldCount;
    for (var i = 0; i < columnCount; i++)
    {
        if (i > 0)
        {
            Console.Write(" | ");
        }

        Console.Write(reader.GetName(i));
    }

    Console.WriteLine();

    var rows = 0;
    while (await reader.ReadAsync())
    {
        rows++;
        for (var i = 0; i < columnCount; i++)
        {
            if (i > 0)
            {
                Console.Write(" | ");
            }

            Console.Write(FormatValue(await reader.IsDBNullAsync(i) ? null : reader.GetValue(i)));
        }

        Console.WriteLine();
    }

    if (rows == 0)
    {
        Console.WriteLine("(no rows)");
    }
}

static string FormatValue(object? value)
{
    return value switch
    {
        null => "NULL",
        DateTime dateTime when dateTime.Kind == DateTimeKind.Utc => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
        decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
        double doubleValue => doubleValue.ToString(CultureInfo.InvariantCulture),
        float floatValue => floatValue.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}
