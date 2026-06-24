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
    ApplicationName = "SimpleSkipDiagnostic"
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
Console.WriteLine("== Simple run status summary ==");
await PrintRowsAsync("""
SELECT
    strategy.code,
    count(*) AS runs,
    count(*) FILTER (WHERE run.status = 'Observed') AS observed,
    count(*) FILTER (WHERE run.status = 'Entered') AS entered,
    count(*) FILTER (WHERE run.status = 'Skipped') AS skipped,
    count(*) FILTER (WHERE run.status = 'Skipped' AND run.paper_order_id IS NULL) AS dashboard_paper_condition_skip,
    count(*) FILTER (WHERE run.status = 'Skipped' AND run.paper_order_id IS NOT NULL) AS paper_not_accepted,
    count(*) FILTER (WHERE run.status = 'Settled') AS settled,
    max(run.updated_at_utc) AS latest_run_update_utc
FROM strategies strategy
LEFT JOIN strategy_market_paper_runs run ON run.strategy_id = strategy.id
WHERE strategy.code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$'
GROUP BY strategy.code
ORDER BY strategy.code;
""");

Console.WriteLine();
Console.WriteLine("== Simple skipped reasons ==");
await PrintRowsAsync("""
SELECT
    strategy.code,
    coalesce(run.skip_reason, '(null)') AS skip_reason,
    count(*) AS runs,
    count(*) FILTER (WHERE run.paper_order_id IS NULL) AS no_paper_order,
    count(*) FILTER (WHERE run.paper_order_id IS NOT NULL) AS has_paper_order,
    min(run.updated_at_utc) AS first_update_utc,
    max(run.updated_at_utc) AS latest_update_utc
FROM strategies strategy
INNER JOIN strategy_market_paper_runs run ON run.strategy_id = strategy.id
WHERE strategy.code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$'
  AND run.status = 'Skipped'
GROUP BY strategy.code, coalesce(run.skip_reason, '(null)')
ORDER BY latest_update_utc DESC NULLS LAST, strategy.code, skip_reason;
""");

Console.WriteLine();
Console.WriteLine("== Recent Simple skipped runs ==");
await PrintRowsAsync("""
SELECT
    strategy.code,
    run.status,
    run.skip_reason,
    run.paper_order_id IS NULL AS no_paper_order,
    run.selected_outcome,
    run.entry_price,
    run.market_slug,
    run.entry_due_at_utc,
    run.updated_at_utc,
    left(coalesce(run.skip_diagnostics_json::text, ''), 360) AS diagnostics_prefix
FROM strategies strategy
INNER JOIN strategy_market_paper_runs run ON run.strategy_id = strategy.id
WHERE strategy.code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$'
  AND run.status = 'Skipped'
ORDER BY run.updated_at_utc DESC
LIMIT 36;
""");

Console.WriteLine();
Console.WriteLine("== Recent Simple orders ==");
await PrintRowsAsync("""
SELECT
    strategy.code,
    paper_order.status,
    paper_order.outcome,
    paper_order.price,
    paper_order.size_shares,
    paper_order.execution_source,
    paper_order.created_at_utc,
    left(coalesce(paper_order.raw_decision_json::text, ''), 300) AS raw_decision_prefix
FROM paper_orders paper_order
INNER JOIN strategies strategy ON strategy.id = paper_order.strategy_id
WHERE strategy.code ~ '^(btc|eth|sol)_up_down_5m_(up|down)_simple$'
ORDER BY paper_order.created_at_utc DESC
LIMIT 24;
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
