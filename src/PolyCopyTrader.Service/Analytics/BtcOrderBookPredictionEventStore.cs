using System.Globalization;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PolyCopyTrader.Service.Analytics;

public sealed class BtcOrderBookPredictionEventStore : IAsyncDisposable
{
    public const string IndexFileName = "events.index.json";
    private const int IndexSchemaVersion = 1;
    private const string Header =
        "schema_version,run_id,connection_id,receive_sequence,logical_sequence,event_type,exchange_event_utc,transact_utc,received_utc,received_stopwatch_ticks," +
        "book_update_id,trade_id,trade_index,bid,bid_qty,ask,ask_qty,trade_price,trade_qty,is_buyer_maker,previous_id,id_delta,status,detail_base64";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string outputDirectory;
    private readonly TimeSpan segmentDuration;
    private readonly List<BtcOrderBookPredictionEventSegment> segments = [];
    private FileStream? fileStream;
    private GZipStream? gzipStream;
    private StreamWriter? writer;
    private string? currentPartialPath;
    private string? currentFinalPath;
    private DateTimeOffset? currentFirstReceivedUtc;
    private DateTimeOffset? currentLastReceivedUtc;
    private long? currentFirstStopwatchTicks;
    private long currentEventCount;
    private int nextSegmentSequence = 1;
    private bool disposed;
    private bool completed;

    public BtcOrderBookPredictionEventStore(string outputDirectory, TimeSpan? segmentDuration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        this.outputDirectory = Path.GetFullPath(outputDirectory);
        this.segmentDuration = segmentDuration ?? TimeSpan.FromMinutes(5);
        if (this.segmentDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentDuration), "Segment duration must be positive.");
        }

        Directory.CreateDirectory(this.outputDirectory);
        IndexPath = Path.Combine(this.outputDirectory, IndexFileName);
        if (File.Exists(IndexPath) ||
            Directory.EnumerateFiles(this.outputDirectory, "events-*.csv.gz*").Any())
        {
            throw new IOException("Order-book prediction event output already exists in " + this.outputDirectory + ".");
        }
    }

    public string IndexPath { get; }

    public string PartialPath => IndexPath;

    public string FinalPath => IndexPath;

    public async ValueTask WriteAsync(
        BtcOrderBookPredictionRawEvent item,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (writer is not null && currentFirstStopwatchTicks is { } firstTicks &&
            Stopwatch.GetElapsedTime(firstTicks, item.ReceivedStopwatchTicks) >= segmentDuration)
        {
            await FinalizeCurrentSegmentAsync("in_progress", CancellationToken.None);
        }

        EnsureSegmentOpen(item.ReceivedUtc, item.ReceivedStopwatchTicks);
        await writer!.WriteLineAsync(Format(item).AsMemory(), cancellationToken);
        currentEventCount++;
        currentLastReceivedUtc = item.ReceivedUtc;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return writer is null ? Task.CompletedTask : writer.FlushAsync(cancellationToken);
    }

    public async Task<string> CompleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completed)
        {
            return IndexPath;
        }

        await FinalizeCurrentSegmentAsync("in_progress", CancellationToken.None);
        await WriteIndexAtomicAsync("completed", cancellationToken);
        completed = true;
        disposed = true;
        return IndexPath;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            await FinalizeCurrentSegmentAsync("interrupted", CancellationToken.None);
            await WriteIndexAtomicAsync("interrupted", CancellationToken.None);
        }
        finally
        {
            await DisposeCurrentStreamsAsync();
            disposed = true;
        }
    }

    public static BtcOrderBookPredictionEventIndex ReadIndex(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json = File.ReadAllText(path, Encoding.UTF8);
        BtcOrderBookPredictionEventIndex? index =
            JsonSerializer.Deserialize<BtcOrderBookPredictionEventIndex>(json, JsonOptions);
        if (index is null || index.SchemaVersion != IndexSchemaVersion)
        {
            throw new InvalidDataException("Unsupported BTC order-book prediction event index.");
        }

        ValidateIndex(index);
        return index;
    }

    public static IEnumerable<BtcOrderBookPredictionRawEvent> ReadEvents(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in ReadSingleSegment(path))
            {
                yield return item;
            }

            yield break;
        }

        BtcOrderBookPredictionEventIndex index = ReadIndex(path);
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        long totalEvents = 0;
        foreach (BtcOrderBookPredictionEventSegment segment in index.Segments.OrderBy(item => item.Sequence))
        {
            if (Path.IsPathFullyQualified(segment.FileName) ||
                !string.Equals(Path.GetFileName(segment.FileName), segment.FileName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Event index contains a non-local segment path.");
            }

            string segmentPath = Path.Combine(directory, segment.FileName);
            if (!File.Exists(segmentPath))
            {
                throw new InvalidDataException("Event segment is missing: " + segment.FileName);
            }

            string actualSha256 = ComputeSha256(segmentPath);
            if (!string.Equals(actualSha256, segment.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Event segment SHA-256 mismatch: " + segment.FileName);
            }

            long segmentEvents = 0;
            DateTimeOffset? firstReceivedUtc = null;
            DateTimeOffset? lastReceivedUtc = null;
            foreach (var item in ReadSingleSegment(segmentPath))
            {
                firstReceivedUtc ??= item.ReceivedUtc;
                lastReceivedUtc = item.ReceivedUtc;
                segmentEvents++;
                totalEvents++;
                yield return item;
            }

            if (segmentEvents != segment.EventCount ||
                firstReceivedUtc != segment.FirstReceivedUtc ||
                lastReceivedUtc != segment.LastReceivedUtc)
            {
                throw new InvalidDataException("Event segment metadata mismatch: " + segment.FileName);
            }
        }

        if (totalEvents != index.TotalEvents)
        {
            throw new InvalidDataException("Event index total count does not match segment contents.");
        }
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string Format(BtcOrderBookPredictionRawEvent item)
    {
        return string.Join(',',
            item.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            item.RunId,
            item.ConnectionId.ToString(CultureInfo.InvariantCulture),
            item.ReceiveSequence.ToString(CultureInfo.InvariantCulture),
            item.LogicalSequence.ToString(CultureInfo.InvariantCulture),
            item.EventType.ToString(),
            Date(item.ExchangeEventUtc),
            Date(item.TransactUtc),
            Date(item.ReceivedUtc),
            item.ReceivedStopwatchTicks.ToString(CultureInfo.InvariantCulture),
            Integer(item.BookUpdateId),
            Integer(item.TradeId),
            Integer(item.TradeIndex),
            Decimal(item.Bid),
            Decimal(item.BidQty),
            Decimal(item.Ask),
            Decimal(item.AskQty),
            Decimal(item.TradePrice),
            Decimal(item.TradeQty),
            Boolean(item.IsBuyerMaker),
            Integer(item.PreviousId),
            Integer(item.IdDelta),
            item.Status,
            EncodeDetail(item.Detail));
    }

    public static bool TryParse(
        string line,
        out BtcOrderBookPredictionRawEvent? item,
        out string? error)
    {
        item = null;
        error = null;
        string[] fields = line.Split(',');
        if (fields.Length != 24)
        {
            error = $"Expected 24 fields but found {fields.Length}.";
            return false;
        }

        try
        {
            item = new BtcOrderBookPredictionRawEvent(
                ParseInt(fields[0]),
                fields[1],
                ParseInt(fields[2]),
                ParseLong(fields[3]),
                ParseLong(fields[4]),
                Enum.Parse<BtcOrderBookPredictionEventType>(fields[5], ignoreCase: false),
                ParseDate(fields[6]),
                ParseDate(fields[7]),
                ParseDate(fields[8]) ?? throw new FormatException("received_utc is required."),
                ParseLong(fields[9]),
                ParseNullableLong(fields[10]),
                ParseNullableLong(fields[11]),
                ParseNullableInt(fields[12]),
                ParseNullableDecimal(fields[13]),
                ParseNullableDecimal(fields[14]),
                ParseNullableDecimal(fields[15]),
                ParseNullableDecimal(fields[16]),
                ParseNullableDecimal(fields[17]),
                ParseNullableDecimal(fields[18]),
                ParseNullableBoolean(fields[19]),
                ParseNullableLong(fields[20]),
                ParseNullableLong(fields[21]),
                fields[22],
                DecodeDetail(fields[23]));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    private void EnsureSegmentOpen(DateTimeOffset firstReceivedUtc, long firstStopwatchTicks)
    {
        if (writer is not null)
        {
            return;
        }

        int sequence = nextSegmentSequence++;
        string stem = "events-" + sequence.ToString("D6", CultureInfo.InvariantCulture) + ".csv.gz";
        currentPartialPath = Path.Combine(outputDirectory, stem + ".partial");
        currentFinalPath = Path.Combine(outputDirectory, stem);
        if (File.Exists(currentPartialPath) || File.Exists(currentFinalPath))
        {
            throw new IOException("Event segment already exists: " + stem);
        }

        fileStream = new FileStream(
            currentPartialPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        gzipStream = new GZipStream(fileStream, CompressionLevel.Fastest, leaveOpen: true);
        writer = new StreamWriter(
            gzipStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            64 * 1024,
            leaveOpen: true)
        {
            NewLine = "\n"
        };
        writer.WriteLine(Header);
        currentFirstReceivedUtc = firstReceivedUtc;
        currentLastReceivedUtc = firstReceivedUtc;
        currentFirstStopwatchTicks = firstStopwatchTicks;
        currentEventCount = 0;
    }

    private async Task FinalizeCurrentSegmentAsync(string indexStatus, CancellationToken cancellationToken)
    {
        if (writer is null)
        {
            return;
        }

        string partialPath = currentPartialPath!;
        string finalPath = currentFinalPath!;
        DateTimeOffset firstReceivedUtc = currentFirstReceivedUtc!.Value;
        DateTimeOffset lastReceivedUtc = currentLastReceivedUtc!.Value;
        long eventCount = currentEventCount;
        await DisposeCurrentStreamsAsync();
        File.Move(partialPath, finalPath, overwrite: false);
        var segment = new BtcOrderBookPredictionEventSegment(
            nextSegmentSequence - 1,
            Path.GetFileName(finalPath),
            eventCount,
            firstReceivedUtc,
            lastReceivedUtc,
            ComputeSha256(finalPath));
        segments.Add(segment);
        currentPartialPath = null;
        currentFinalPath = null;
        currentFirstReceivedUtc = null;
        currentLastReceivedUtc = null;
        currentFirstStopwatchTicks = null;
        currentEventCount = 0;
        await WriteIndexAtomicAsync(indexStatus, cancellationToken);
    }

    private async Task DisposeCurrentStreamsAsync()
    {
        StreamWriter? currentWriter = writer;
        GZipStream? currentGzip = gzipStream;
        FileStream? currentFile = fileStream;
        writer = null;
        gzipStream = null;
        fileStream = null;
        if (currentWriter is not null)
        {
            await currentWriter.DisposeAsync();
        }

        if (currentGzip is not null)
        {
            await currentGzip.DisposeAsync();
        }

        if (currentFile is not null)
        {
            await currentFile.DisposeAsync();
        }
    }

    private async Task WriteIndexAtomicAsync(string status, CancellationToken cancellationToken)
    {
        var index = new BtcOrderBookPredictionEventIndex(
            IndexSchemaVersion,
            status,
            checked((int)Math.Ceiling(segmentDuration.TotalSeconds)),
            segments.Sum(item => item.EventCount),
            DateTimeOffset.UtcNow,
            segments.ToArray());
        string partialPath = IndexPath + ".partial";
        await using (var stream = new FileStream(
            partialPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, index, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(partialPath, IndexPath, overwrite: true);
    }

    private static IEnumerable<BtcOrderBookPredictionRawEvent> ReadSingleSegment(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 64 * 1024);
        string? header = reader.ReadLine();
        if (!string.Equals(header, Header, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unsupported BTC order-book prediction event header.");
        }

        string? line;
        var lineNumber = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            if (!TryParse(line, out var item, out string? error) || item is null)
            {
                throw new InvalidDataException($"Invalid BTC order-book prediction event at line {lineNumber}: {error}");
            }

            yield return item;
        }
    }

    private static void ValidateIndex(BtcOrderBookPredictionEventIndex index)
    {
        if (index.SegmentDurationSeconds <= 0 || index.TotalEvents < 0)
        {
            throw new InvalidDataException("Event index contains invalid aggregate values.");
        }

        long total = 0;
        for (int offset = 0; offset < index.Segments.Count; offset++)
        {
            BtcOrderBookPredictionEventSegment segment = index.Segments[offset];
            if (segment.Sequence != offset + 1 || segment.EventCount <= 0 ||
                string.IsNullOrWhiteSpace(segment.Sha256))
            {
                throw new InvalidDataException("Event index contains invalid segment metadata.");
            }

            total = checked(total + segment.EventCount);
        }

        if (total != index.TotalEvents)
        {
            throw new InvalidDataException("Event index total count does not match segment metadata.");
        }
    }

    private static string Date(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Decimal(decimal? value) =>
        value?.ToString("G29", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Integer(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Integer(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Boolean(bool? value) =>
        value is null ? string.Empty : value.Value ? "true" : "false";

    private static string EncodeDetail(string? detail) =>
        string.IsNullOrEmpty(detail) ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(detail));

    private static string? DecodeDetail(string value) =>
        value.Length == 0 ? null : Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static DateTimeOffset? ParseDate(string value) =>
        value.Length == 0
            ? null
            : DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static int ParseInt(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static long ParseLong(string value) => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static int? ParseNullableInt(string value) => value.Length == 0 ? null : ParseInt(value);

    private static long? ParseNullableLong(string value) => value.Length == 0 ? null : ParseLong(value);

    private static decimal? ParseNullableDecimal(string value) =>
        value.Length == 0 ? null : decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static bool? ParseNullableBoolean(string value) =>
        value.Length == 0 ? null : bool.Parse(value);
}
