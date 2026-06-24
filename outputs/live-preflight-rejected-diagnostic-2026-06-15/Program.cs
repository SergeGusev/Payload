using System.Data;
using System.Text;
using Npgsql;

const string StrategyCode = "btc_up_down_5m_up_simple";

Console.OutputEncoding = Encoding.UTF8;

var outputPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath("outputs/live-preflight-rejected-diagnostic-2026-06-15/result.txt");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    Host = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE") ?? "192.168.0.101",
    Timeout = 10,
    CommandTimeout = 30,
    ApplicationName = "LivePreflightRejectedDiagnostic"
};

await using var writer = new StreamWriter(outputPath, append: false, Encoding.UTF8);
await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);

await ExecuteAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '30s';
SET LOCAL lock_timeout = '500ms';
""");

await WriteLineAsync($"Live preflight rejected diagnostic captured at {DateTimeOffset.UtcNow:O}");
await WriteLineAsync($"strategy_code={StrategyCode}");
await WriteLineAsync("");

await WriteLineAsync("## Status summary");
await WriteRowsAsync("""
SELECT
    live_order.status,
    live_order.response_status,
    count(*)::integer AS count,
    COALESCE(sum(live_order.filled_size), 0)::numeric AS filled_size,
    COALESCE(sum(live_order.remaining_size), 0)::numeric AS remaining_size,
    COALESCE(sum(live_order.filled_notional_usd), 0)::numeric AS filled_notional_usd,
    to_char(max(live_order.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_created_utc
FROM live_orders live_order
JOIN strategies strategy ON strategy.id = live_order.strategy_id
WHERE strategy.code = @strategy_code
GROUP BY live_order.status, live_order.response_status
ORDER BY max(live_order.created_at_utc) DESC, live_order.status;
""", ("strategy_code", StrategyCode));

await WriteLineAsync("");
await WriteLineAsync("## PreflightRejected validation summaries");
await WriteRowsAsync("""
SELECT
    COALESCE(NULLIF(live_order.validation_summary, ''), '<empty>') AS validation_summary,
    count(*)::integer AS count,
    to_char(max(live_order.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_created_utc
FROM live_orders live_order
JOIN strategies strategy ON strategy.id = live_order.strategy_id
WHERE strategy.code = @strategy_code
  AND live_order.status = 'PreflightRejected'
GROUP BY COALESCE(NULLIF(live_order.validation_summary, ''), '<empty>')
ORDER BY count(*) DESC, latest_created_utc DESC;
""", ("strategy_code", StrategyCode));

await WriteLineAsync("");
await WriteLineAsync("## Latest PreflightRejected orders");
await WriteRowsAsync("""
SELECT
    to_char(live_order.created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc,
    to_char(live_order.updated_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS updated_utc,
    live_order.response_status,
    live_order.outcome,
    live_order.price,
    live_order.size_shares,
    live_order.notional_usd,
    live_order.filled_size,
    live_order.remaining_size,
    COALESCE(run.market_slug, '<none>') AS market_slug,
    COALESCE(NULLIF(live_order.validation_summary, ''), '<empty>') AS validation_summary
FROM live_orders live_order
JOIN strategies strategy ON strategy.id = live_order.strategy_id
LEFT JOIN paper_orders paper_order ON paper_order.id = live_order.paper_order_id
LEFT JOIN strategy_market_paper_runs run ON run.paper_order_id = paper_order.id
WHERE strategy.code = @strategy_code
  AND live_order.status = 'PreflightRejected'
ORDER BY live_order.created_at_utc DESC
LIMIT 20;
""", ("strategy_code", StrategyCode));

await WriteLineAsync("");
await WriteLineAsync("## Recent Polymarket API errors");
await WriteRowsAsync("""
SELECT
    to_char(api_error.created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc,
    api_error.component,
    api_error.operation,
    left(api_error.message, 300) AS message
FROM api_errors api_error
WHERE api_error.created_at_utc >= now() - interval '60 minutes'
  AND api_error.component ILIKE '%Polymarket%'
ORDER BY api_error.created_at_utc DESC
LIMIT 30;
""");

await transaction.CommitAsync();
await writer.FlushAsync();
Console.WriteLine(outputPath);

async Task ExecuteAsync(string sql)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}

async Task WriteRowsAsync(string sql, params (string Name, object Value)[] parameters)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }

    await using var reader = await command.ExecuteReaderAsync();
    var fieldCount = reader.FieldCount;
    for (var index = 0; index < fieldCount; index++)
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
        for (var index = 0; index < fieldCount; index++)
        {
            if (index > 0)
            {
                await writer.WriteAsync('\t');
            }

            await writer.WriteAsync(reader.IsDBNull(index)
                ? "<null>"
                : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture));
        }

        await writer.WriteLineAsync();
    }
}

async Task WriteLineAsync(string line)
{
    await writer.WriteLineAsync(line);
}
