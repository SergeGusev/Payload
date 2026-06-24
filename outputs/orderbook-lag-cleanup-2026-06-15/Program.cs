using System.Data;
using System.Globalization;
using Npgsql;

var execute = args.Any(arg => string.Equals(arg, "--execute", StringComparison.OrdinalIgnoreCase));
var outputPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? (execute
        ? "outputs/orderbook-lag-cleanup-2026-06-15/execute-result.txt"
        : "outputs/orderbook-lag-cleanup-2026-06-15/dry-run.txt");
var batchSize = ReadIntArg(args, "--batch-size", 1_000);
var sleepMilliseconds = ReadIntArg(args, "--sleep-ms", 250);
var maxBatches = ReadIntArg(args, "--max-batches", 10_000);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "OrderBookLagCleanup",
    Timeout = 10,
    CommandTimeout = 15
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var writer = new StreamWriter(outputPath, append: false);
await WriteLineAsync($"Order-book lag cleanup captured at {DateTimeOffset.UtcNow:O}");
await WriteLineAsync($"mode\t{(execute ? "EXECUTE" : "DRY_RUN")}");
await WriteLineAsync($"batch_size\t{batchSize}");
await WriteLineAsync($"sleep_ms\t{sleepMilliseconds}");
await WriteLineAsync($"max_batches\t{maxBatches}");
await WriteLineAsync("");

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();

var cutoff = await ReadServiceStartedAtUtcAsync(connection);
if (cutoff is null)
{
    throw new InvalidOperationException("PolyCopyTrader.Service heartbeat was not found.");
}

await WriteLineAsync($"service_started_utc\t{cutoff.Value.UtcDateTime:O}");

var before = await ReadSummaryAsync(connection, cutoff.Value);
await WriteSummaryAsync("before", before);

if (before.RowsSinceServiceStart > 0)
{
    await WriteLineAsync("abort_reason\tlag diagnostics still have rows at or after current service start");
    return;
}

if (!execute)
{
    await WriteLineAsync("dry_run_note\tNo rows were deleted. Rerun with --execute to delete rows older than service start.");
    return;
}

var totalDeleted = 0L;
var batches = 0;
while (batches < maxBatches)
{
    var safety = await ReadSummaryAsync(connection, cutoff.Value);
    if (safety.RowsSinceServiceStart > 0)
    {
        await WriteLineAsync($"abort_reason\tnew lag diagnostics appeared at or after service start; rows_since_service_start={safety.RowsSinceServiceStart}");
        break;
    }

    var deleted = await DeleteBatchAsync(connection, cutoff.Value, batchSize);
    if (deleted == 0)
    {
        await WriteLineAsync("delete_complete\ttrue");
        break;
    }

    batches++;
    totalDeleted += deleted;
    await WriteLineAsync($"batch\t{batches}\tdeleted\t{deleted}\ttotal_deleted\t{totalDeleted}");
    await writer.FlushAsync();

    if (sleepMilliseconds > 0)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(sleepMilliseconds));
    }
}

if (batches >= maxBatches)
{
    await WriteLineAsync($"stopped_reason\tmax_batches_reached_{maxBatches}");
}

var after = await ReadSummaryAsync(connection, cutoff.Value);
await WriteSummaryAsync("after", after);
await WriteLineAsync($"total_deleted\t{totalDeleted}");

async Task<DateTimeOffset?> ReadServiceStartedAtUtcAsync(NpgsqlConnection conn)
{
    await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    await ExecuteNonQueryAsync(conn, tx, """
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '10s';
SET LOCAL lock_timeout = '500ms';
""");

    await using var command = new NpgsqlCommand("""
SELECT max(started_at_utc)
FROM service_heartbeats
WHERE service_name = 'PolyCopyTrader.Service';
""", conn, tx);
    var value = await command.ExecuteScalarAsync();
    await tx.CommitAsync();
    return ToUtcDateTimeOffset(value);
}

async Task<Summary> ReadSummaryAsync(NpgsqlConnection conn, DateTimeOffset cutoffUtc)
{
    await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    await ExecuteNonQueryAsync(conn, tx, """
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '20s';
SET LOCAL lock_timeout = '500ms';
""");

    await using var command = new NpgsqlCommand("""
SELECT
    count(*)::bigint AS total_rows,
    count(*) FILTER (WHERE received_at_utc < @CutoffUtc)::bigint AS rows_before_cutoff,
    count(*) FILTER (WHERE received_at_utc >= @CutoffUtc)::bigint AS rows_since_service_start,
    min(received_at_utc) AS earliest_received_utc,
    max(received_at_utc) AS latest_received_utc
FROM btc_order_book_lag_diagnostic_events;
""", conn, tx);
    command.Parameters.AddWithValue("CutoffUtc", cutoffUtc);

    Summary result;
    await using (var reader = await command.ExecuteReaderAsync())
    {
        if (!await reader.ReadAsync())
        {
            result = new Summary(0, 0, 0, null, null);
        }
        else
        {
            result = new Summary(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : ToUtcDateTimeOffset(reader.GetValue(3)),
                reader.IsDBNull(4) ? null : ToUtcDateTimeOffset(reader.GetValue(4)));
        }
    }

    await tx.CommitAsync();
    return result;
}

async Task<int> DeleteBatchAsync(NpgsqlConnection conn, DateTimeOffset cutoffUtc, int limit)
{
    await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    await ExecuteNonQueryAsync(conn, tx, """
SET LOCAL statement_timeout = '5s';
SET LOCAL lock_timeout = '500ms';
""");

    await using var command = new NpgsqlCommand("""
WITH victim AS (
    SELECT ctid
    FROM btc_order_book_lag_diagnostic_events
    WHERE received_at_utc < @CutoffUtc
    ORDER BY received_at_utc
    LIMIT @Limit
)
DELETE FROM btc_order_book_lag_diagnostic_events target
USING victim
WHERE target.ctid = victim.ctid;
""", conn, tx);
    command.Parameters.AddWithValue("CutoffUtc", cutoffUtc);
    command.Parameters.AddWithValue("Limit", limit);
    var deleted = await command.ExecuteNonQueryAsync();
    await tx.CommitAsync();
    return deleted;
}

async Task ExecuteNonQueryAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
{
    await using var command = new NpgsqlCommand(sql, conn, tx);
    await command.ExecuteNonQueryAsync();
}

async Task WriteSummaryAsync(string label, Summary summary)
{
    await WriteLineAsync($"{label}_total_rows\t{summary.TotalRows}");
    await WriteLineAsync($"{label}_rows_before_cutoff\t{summary.RowsBeforeCutoff}");
    await WriteLineAsync($"{label}_rows_since_service_start\t{summary.RowsSinceServiceStart}");
    await WriteLineAsync($"{label}_earliest_received_utc\t{FormatTimestamp(summary.EarliestReceivedUtc)}");
    await WriteLineAsync($"{label}_latest_received_utc\t{FormatTimestamp(summary.LatestReceivedUtc)}");
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
            return Math.Max(1, inlineResult);
        }

        if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase) &&
            i + 1 < values.Length &&
            int.TryParse(values[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nextResult))
        {
            return Math.Max(1, nextResult);
        }
    }

    return fallback;
}

static string FormatTimestamp(DateTimeOffset? value)
{
    return value?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? "";
}

static DateTimeOffset? ToUtcDateTimeOffset(object? value)
{
    return value switch
    {
        null or DBNull => null,
        DateTimeOffset dto => dto.ToUniversalTime(),
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        _ => throw new InvalidCastException($"Unsupported timestamp type {value.GetType().FullName}.")
    };
}

internal sealed record Summary(
    long TotalRows,
    long RowsBeforeCutoff,
    long RowsSinceServiceStart,
    DateTimeOffset? EarliestReceivedUtc,
    DateTimeOffset? LatestReceivedUtc);
