using System.Data;
using System.Globalization;
using Npgsql;

var outputPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? "outputs/live-bets-status-2026-06-16/result.txt";

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "LiveBetsStatus",
    Timeout = 10,
    CommandTimeout = 30
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();

await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
await ExecuteNonQueryAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '25s';
SET LOCAL lock_timeout = '500ms';
""");

await WriteLineAsync($"Live bets status captured at {DateTimeOffset.UtcNow:O}");
await WriteScalarAsync("Database", "SELECT current_database();");
await WriteScalarAsync("Server address", "SELECT inet_server_addr()::text;");
await WriteScalarAsync("Server time UTC", "SELECT to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"');");

await WriteSectionAsync("Service heartbeat");
await WriteRowsAsync("""
SELECT
    service_name,
    status,
    mode,
    version,
    to_char(started_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS started_utc,
    to_char(last_heartbeat_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS heartbeat_utc,
    round(extract(epoch FROM (now() - last_heartbeat_utc))::int, 0) AS heartbeat_age_seconds,
    current_loop,
    coalesce(nullif(left(last_error, 240), ''), '<none>') AS last_error
FROM service_heartbeats
ORDER BY last_heartbeat_utc DESC;
""");

await WriteSectionAsync("Live order recency");
await WriteRowsAsync("""
WITH heartbeat AS (
    SELECT max(started_at_utc) AS service_started_utc
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
)
SELECT
    count(*) AS total_live_orders,
    count(*) FILTER (WHERE created_at_utc >= now() - interval '5 minutes') AS created_last_5m,
    count(*) FILTER (WHERE created_at_utc >= now() - interval '15 minutes') AS created_last_15m,
    count(*) FILTER (WHERE created_at_utc >= now() - interval '60 minutes') AS created_last_60m,
    count(*) FILTER (WHERE created_at_utc >= now() - interval '6 hours') AS created_last_6h,
    count(*) FILTER (WHERE created_at_utc >= (SELECT service_started_utc FROM heartbeat)) AS created_since_service_start,
    count(*) FILTER (WHERE submitted_at_utc >= now() - interval '60 minutes') AS submitted_last_60m,
    count(*) FILTER (WHERE filled_size > 0 AND updated_at_utc >= now() - interval '60 minutes') AS filled_or_updated_last_60m,
    to_char(max(created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_created_utc,
    round(extract(epoch FROM (now() - max(created_at_utc)))::int, 0) AS latest_created_age_seconds,
    to_char(max(submitted_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_submitted_utc,
    to_char(max(updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_updated_utc,
    to_char((SELECT service_started_utc FROM heartbeat) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS service_started_utc
FROM live_orders;
""");

await WriteSectionAsync("Live order status windows");
await WriteRowsAsync("""
SELECT
    status,
    count(*) FILTER (WHERE created_at_utc >= now() - interval '15 minutes') AS created_last_15m,
    count(*) FILTER (WHERE created_at_utc >= now() - interval '60 minutes') AS created_last_60m,
    count(*) FILTER (WHERE created_at_utc >= now() - interval '6 hours') AS created_last_6h,
    count(*) AS total,
    to_char(max(created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_created_utc,
    to_char(max(updated_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_updated_utc
FROM live_orders
GROUP BY status
ORDER BY created_last_60m DESC, total DESC, status;
""");

await WriteSectionAsync("Live-enabled strategies");
await WriteRowsAsync("""
SELECT
    strategy.code,
    strategy.enabled,
    strategy.live_stakes,
    strategy.auto_live_paused,
    strategy.paused,
    strategy.live_stake_amount,
    strategy.live_available_balance,
    count(live_order.id) FILTER (WHERE live_order.created_at_utc >= now() - interval '60 minutes') AS live_orders_last_60m,
    count(live_order.id) FILTER (WHERE live_order.created_at_utc >= now() - interval '6 hours') AS live_orders_last_6h,
    to_char(max(live_order.created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_live_order_utc,
    coalesce(sum(live_order.realized_pnl_usd) FILTER (WHERE live_order.settled_at_utc >= now() - interval '6 hours'), 0) AS realized_pnl_last_6h
FROM strategies strategy
LEFT JOIN live_orders live_order ON live_order.strategy_id = strategy.id
WHERE strategy.live_stakes
GROUP BY
    strategy.code,
    strategy.enabled,
    strategy.live_stakes,
    strategy.auto_live_paused,
    strategy.paused,
    strategy.live_stake_amount,
    strategy.live_available_balance
ORDER BY live_orders_last_60m DESC, latest_live_order_utc DESC NULLS LAST, strategy.code;
""");

await WriteSectionAsync("Latest live orders");
await WriteRowsAsync("""
SELECT
    to_char(live_order.created_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS created_utc,
    to_char(live_order.submitted_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS submitted_utc,
    to_char(live_order.updated_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS updated_utc,
    strategy.code,
    live_order.status,
    live_order.response_status,
    live_order.outcome,
    live_order.price,
    live_order.size_shares,
    live_order.filled_size,
    live_order.remaining_size,
    live_order.notional_usd,
    left(live_order.validation_summary, 180) AS validation_summary
FROM live_orders live_order
JOIN strategies strategy ON strategy.id = live_order.strategy_id
ORDER BY live_order.created_at_utc DESC
LIMIT 25;
""");

await WriteSectionAsync("Paper live-shadow decisions");
await WriteRowsAsync("""
WITH heartbeat AS (
    SELECT max(started_at_utc) AS service_started_utc
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
)
SELECT
    count(*) FILTER (WHERE decision_created_at_utc >= now() - interval '15 minutes') AS decisions_last_15m,
    count(*) FILTER (WHERE decision_created_at_utc >= now() - interval '60 minutes') AS decisions_last_60m,
    count(*) FILTER (WHERE decision_created_at_utc >= now() - interval '6 hours') AS decisions_last_6h,
    count(*) FILTER (WHERE decision_created_at_utc >= (SELECT service_started_utc FROM heartbeat)) AS decisions_since_service_start,
    count(*) FILTER (WHERE decision_created_at_utc >= now() - interval '60 minutes' AND live_order_id IS NOT NULL) AS linked_live_orders_last_60m,
    to_char(max(decision_created_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_decision_utc
FROM paper_live_shadow_decisions;
""");

await tx.CommitAsync();

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    await command.ExecuteNonQueryAsync();
}

async Task WriteRowsAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    await using var reader = await command.ExecuteReaderAsync();
    for (var i = 0; i < reader.FieldCount; i++)
    {
        if (i > 0)
        {
            await writer.WriteAsync('\t');
        }

        await writer.WriteAsync(reader.GetName(i));
    }

    await writer.WriteLineAsync();
    while (await reader.ReadAsync())
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (i > 0)
            {
                await writer.WriteAsync('\t');
            }

            await writer.WriteAsync(FormatValue(reader.IsDBNull(i) ? null : reader.GetValue(i)));
        }

        await writer.WriteLineAsync();
    }
}

async Task WriteScalarAsync(string label, string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    var value = await command.ExecuteScalarAsync();
    await WriteLineAsync($"{label}\t{FormatValue(value)}");
}

async Task WriteSectionAsync(string title)
{
    await writer.WriteLineAsync();
    await writer.WriteLineAsync("## " + title);
}

Task WriteLineAsync(string line)
{
    return writer.WriteLineAsync(line);
}

static string FormatValue(object? value)
{
    return value switch
    {
        null or DBNull => "",
        DateTimeOffset dto => dto.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
        decimal d => d.ToString("0.########", CultureInfo.InvariantCulture),
        double d => d.ToString("0.########", CultureInfo.InvariantCulture),
        float f => f.ToString("0.########", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };
}
