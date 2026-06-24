using System.Data;
using System.Globalization;
using Npgsql;

var outputPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? "outputs/orderbook-lag-stop-check-2026-06-15/result.txt";
var delaySeconds = ReadIntArg(args, "--delay-seconds", 75);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "OrderBookLagStopCheck",
    Timeout = 10,
    CommandTimeout = 30
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await WriteLineAsync($"Order-book lag stop check captured at {DateTimeOffset.UtcNow:O}");
await WriteLineAsync($"Delay seconds between snapshots: {delaySeconds.ToString(CultureInfo.InvariantCulture)}");
await WriteLineAsync("");

var first = await CaptureSnapshotAsync("snapshot_1");
await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
var second = await CaptureSnapshotAsync("snapshot_2");

await WriteSectionAsync("Comparison");
await WriteLineAsync($"total_delta\t{second.TotalRows - first.TotalRows}");
await WriteLineAsync($"rows_since_service_start_delta\t{second.RowsSinceServiceStart - first.RowsSinceServiceStart}");
await WriteLineAsync($"latest_received_changed\t{!Nullable.Equals(first.LatestReceivedUtc, second.LatestReceivedUtc)}");
await WriteLineAsync($"snapshot_1_latest_received_utc\t{FormatTimestamp(first.LatestReceivedUtc)}");
await WriteLineAsync($"snapshot_2_latest_received_utc\t{FormatTimestamp(second.LatestReceivedUtc)}");
await WriteLineAsync($"snapshot_2_service_started_utc\t{FormatTimestamp(second.ServiceStartedUtc)}");
await WriteLineAsync($"snapshot_2_rows_since_service_start\t{second.RowsSinceServiceStart}");
await WriteLineAsync("");
if (second.TotalRows == first.TotalRows &&
    second.RowsSinceServiceStart == 0 &&
    Nullable.Equals(first.LatestReceivedUtc, second.LatestReceivedUtc))
{
    await WriteLineAsync("verdict\tSTOPPED");
}
else
{
    await WriteLineAsync("verdict\tSTILL_WRITING_OR_RECENT_WRITES_PRESENT");
}

async Task<Snapshot> CaptureSnapshotAsync(string name)
{
    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync();

    await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    await ExecuteNonQueryAsync(connection, tx, """
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '20s';
SET LOCAL lock_timeout = '500ms';
""");

    await WriteSectionAsync(name);
    await WriteScalarAsync(connection, tx, "database", "SELECT current_database();");
    await WriteScalarAsync(connection, tx, "server_address", "SELECT inet_server_addr()::text;");
    await WriteScalarAsync(connection, tx, "server_time_utc", "SELECT to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"');");

    await WriteSubsectionAsync("service_heartbeat");
    await WriteRowsAsync(connection, tx, """
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

    await WriteSubsectionAsync("lag_table_summary");
    await WriteRowsAsync(connection, tx, """
WITH heartbeat AS (
    SELECT max(started_at_utc) AS started_at_utc
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
),
summary AS (
    SELECT
        count(*) AS total_rows,
        count(*) FILTER (WHERE received_at_utc >= now() - interval '1 minute') AS rows_last_1m,
        count(*) FILTER (WHERE received_at_utc >= now() - interval '5 minutes') AS rows_last_5m,
        count(*) FILTER (WHERE received_at_utc >= now() - interval '15 minutes') AS rows_last_15m,
        count(*) FILTER (WHERE received_at_utc >= now() - interval '60 minutes') AS rows_last_60m,
        count(*) FILTER (WHERE received_at_utc >= (SELECT started_at_utc FROM heartbeat)) AS rows_since_service_start,
        min(received_at_utc) AS earliest_received_utc,
        max(received_at_utc) AS latest_received_utc
    FROM btc_order_book_lag_diagnostic_events
)
SELECT
    total_rows,
    rows_last_1m,
    rows_last_5m,
    rows_last_15m,
    rows_last_60m,
    rows_since_service_start,
    to_char((SELECT started_at_utc FROM heartbeat) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS service_started_utc,
    to_char(earliest_received_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS earliest_received_utc,
    to_char(latest_received_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_received_utc,
    round(extract(epoch FROM (now() - latest_received_utc))::int, 0) AS latest_age_seconds
FROM summary;
""");

    await WriteSubsectionAsync("lag_sources_last_24h");
    await WriteRowsAsync(connection, tx, """
SELECT
    source,
    raw_event_type,
    count(*) FILTER (WHERE received_at_utc >= now() - interval '1 minute') AS rows_last_1m,
    count(*) FILTER (WHERE received_at_utc >= now() - interval '5 minutes') AS rows_last_5m,
    count(*) FILTER (WHERE received_at_utc >= now() - interval '15 minutes') AS rows_last_15m,
    count(*) FILTER (WHERE received_at_utc >= now() - interval '60 minutes') AS rows_last_60m,
    count(*) AS rows_last_24h,
    to_char(max(received_at_utc) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS latest_received_utc
FROM btc_order_book_lag_diagnostic_events
WHERE received_at_utc >= now() - interval '24 hours'
GROUP BY source, raw_event_type
ORDER BY rows_last_24h DESC, source, raw_event_type;
""");

    await WriteSubsectionAsync("latest_lag_rows");
    await WriteRowsAsync(connection, tx, """
SELECT
    to_char(received_at_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') AS received_utc,
    source,
    raw_event_type,
    asset_id,
    condition_id,
    binance_symbol
FROM btc_order_book_lag_diagnostic_events
ORDER BY received_at_utc DESC
LIMIT 10;
""");

    var snapshot = await ReadSnapshotAsync(connection, tx);
    await tx.CommitAsync();
    return snapshot;
}

async Task<Snapshot> ReadSnapshotAsync(NpgsqlConnection connection, NpgsqlTransaction tx)
{
    await using var command = new NpgsqlCommand("""
WITH heartbeat AS (
    SELECT max(started_at_utc) AS started_at_utc
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
)
SELECT
    (SELECT started_at_utc FROM heartbeat) AS service_started_utc,
    count(*)::bigint AS total_rows,
    count(*) FILTER (WHERE received_at_utc >= (SELECT started_at_utc FROM heartbeat))::bigint AS rows_since_service_start,
    max(received_at_utc) AS latest_received_utc
FROM btc_order_book_lag_diagnostic_events;
""", connection, tx);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return new Snapshot(null, 0, 0, null);
    }

    DateTimeOffset? serviceStartedUtc = reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0).ToUniversalTime();
    var totalRows = reader.GetInt64(1);
    var rowsSinceStart = reader.GetInt64(2);
    DateTimeOffset? latestReceivedUtc = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3).ToUniversalTime();
    return new Snapshot(serviceStartedUtc, totalRows, rowsSinceStart, latestReceivedUtc);
}

async Task ExecuteNonQueryAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    await command.ExecuteNonQueryAsync();
}

async Task WriteRowsAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string sql)
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

async Task WriteScalarAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string label, string sql)
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

async Task WriteSubsectionAsync(string title)
{
    await writer.WriteLineAsync();
    await writer.WriteLineAsync("# " + title);
}

Task WriteLineAsync(string line)
{
    return writer.WriteLineAsync(line);
}

static int ReadIntArg(string[] values, string name, int fallback)
{
    for (var i = 0; i < values.Length; i++)
    {
        var value = values[i];
        if (value.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value[(name.Length + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var inlineResult))
        {
            return Math.Max(0, inlineResult);
        }

        if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase) &&
            i + 1 < values.Length &&
            int.TryParse(values[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nextResult))
        {
            return Math.Max(0, nextResult);
        }
    }

    return fallback;
}

static string FormatTimestamp(DateTimeOffset? value)
{
    return value?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? "";
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

internal sealed record Snapshot(
    DateTimeOffset? ServiceStartedUtc,
    long TotalRows,
    long RowsSinceServiceStart,
    DateTimeOffset? LatestReceivedUtc);
