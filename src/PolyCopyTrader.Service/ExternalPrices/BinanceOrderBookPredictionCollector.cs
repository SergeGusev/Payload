using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using PolyCopyTrader.Service.Analytics;

namespace PolyCopyTrader.Service.ExternalPrices;

public enum BinanceOrderBookPredictionSource
{
    Json,
    Sbe
}

public sealed record BinanceOrderBookPredictionCollectorOptions(
    BinanceOrderBookPredictionSource Source,
    string StreamUrl,
    string? SbeApiKey,
    TimeSpan Duration,
    int QueueCapacity,
    int ReconnectBaseDelayMilliseconds,
    int ReconnectMaxDelayMilliseconds,
    int ConnectTimeoutMilliseconds,
    int NoDataTimeoutMilliseconds);

public sealed class BinanceOrderBookPredictionCollector
{
    private const int EventSchemaVersion = 1;
    private readonly BtcOrderBookPredictionEventStore eventStore;
    private readonly string runId;
    private readonly BinanceOrderBookPredictionCollectorOptions options;
    private readonly Uri streamUri;
    private readonly Channel<BtcOrderBookPredictionRawEvent> channel;
    private long receiveSequence;
    private long logicalSequence;
    private long bookEvents;
    private long tradeEvents;
    private long controlEvents;
    private long decodeErrors;
    private long reconnects;
    private int queuedEvents;
    private int queueHighWaterMark;
    private bool qualityGapObserved;
    private bool everConnected;
    private long? previousBookId;
    private long? previousTradeId;

    public BinanceOrderBookPredictionCollector(
        BtcOrderBookPredictionEventStore eventStore,
        string runId,
        BinanceOrderBookPredictionCollectorOptions options)
    {
        this.eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        this.runId = string.IsNullOrWhiteSpace(runId) ? throw new ArgumentException("Run id is required.", nameof(runId)) : runId;
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        streamUri = BinanceOrderBookPredictionEndpointPolicy.Validate(options.Source, options.StreamUrl);
        if (options.Source == BinanceOrderBookPredictionSource.Sbe && string.IsNullOrWhiteSpace(options.SbeApiKey))
        {
            throw new ArgumentException("Binance SBE API key id is required for SBE collection.", nameof(options));
        }

        if (options.QueueCapacity <= 0 || options.Duration <= TimeSpan.Zero ||
            options.ConnectTimeoutMilliseconds <= 0 || options.NoDataTimeoutMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Collector capacities, duration, and timeouts must be positive.");
        }

        channel = Channel.CreateBounded<BtcOrderBookPredictionRawEvent>(new BoundedChannelOptions(options.QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public async Task<BtcOrderBookPredictionCollectionSummary> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        DateTimeOffset deadlineUtc = startedAtUtc + options.Duration;
        string? failureReason = null;
        var writerTask = WriteEventsAsync();
        using var durationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        durationCts.CancelAfter(options.Duration);

        try
        {
            await EnqueueControlAsync(0, "collector_started", options.Source.ToString(), durationCts.Token);
            int connectionId = 0;
            int reconnectDelayMilliseconds = Math.Max(1, options.ReconnectBaseDelayMilliseconds);
            while (!durationCts.IsCancellationRequested && DateTimeOffset.UtcNow < deadlineUtc)
            {
                connectionId++;
                if (connectionId > 1)
                {
                    reconnects++;
                }

                try
                {
                    await RunConnectionAsync(connectionId, durationCts.Token);
                    reconnectDelayMilliseconds = Math.Max(1, options.ReconnectBaseDelayMilliseconds);
                }
                catch (OperationCanceledException) when (durationCts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException or JsonException)
                {
                    qualityGapObserved = true;
                    await EnqueueControlAsync(connectionId, "connection_error", ex.GetType().Name + ": " + ex.Message, CancellationToken.None);
                }

                if (durationCts.IsCancellationRequested || DateTimeOffset.UtcNow >= deadlineUtc)
                {
                    break;
                }

                qualityGapObserved = true;
                try
                {
                    await Task.Delay(reconnectDelayMilliseconds, durationCts.Token);
                }
                catch (OperationCanceledException) when (durationCts.IsCancellationRequested)
                {
                    break;
                }

                reconnectDelayMilliseconds = Math.Min(
                    Math.Max(reconnectDelayMilliseconds * 2, reconnectDelayMilliseconds),
                    Math.Max(reconnectDelayMilliseconds, options.ReconnectMaxDelayMilliseconds));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                qualityGapObserved = true;
                failureReason = "cancelled_by_user";
                await EnqueueControlAsync(0, "collector_cancelled", null, CancellationToken.None);
            }
            else if (!everConnected || bookEvents == 0)
            {
                qualityGapObserved = true;
                failureReason = !everConnected ? "never_connected" : "no_book_events_received";
                await EnqueueControlAsync(0, "collector_failed", failureReason, CancellationToken.None);
            }
            else
            {
                await EnqueueControlAsync(0, "collector_completed", null, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            qualityGapObserved = true;
            failureReason = ex.GetType().Name + ": " + ex.Message;
            try
            {
                await EnqueueControlAsync(0, "collector_failed", failureReason, CancellationToken.None);
            }
            catch
            {
                // The original collection failure remains authoritative.
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            try
            {
                await writerTask;
            }
            catch (Exception ex)
            {
                qualityGapObserved = true;
                failureReason ??= "writer_failure: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
        string status = failureReason is not null
            ? "incomplete"
            : qualityGapObserved
                ? "completed_with_gaps"
                : "completed";
        return new BtcOrderBookPredictionCollectionSummary(
            runId,
            options.Source.ToString(),
            startedAtUtc,
            completedAtUtc,
            status,
            eventStore.PartialPath,
            bookEvents,
            tradeEvents,
            controlEvents,
            decodeErrors,
            reconnects,
            queueHighWaterMark,
            Stopwatch.Frequency,
            failureReason);
    }

    public static IReadOnlyList<BtcOrderBookPredictionRawEvent> DecodeJsonFrame(
        ReadOnlyMemory<byte> payload,
        string runId,
        int connectionId,
        long receiveSequence,
        ref long logicalSequence,
        DateTimeOffset receivedUtc,
        long receivedStopwatchTicks,
        ref long? previousBookId,
        ref long? previousTradeId)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        string? wrapperStream = ReadString(document.RootElement, "stream");
        JsonElement root = document.RootElement.TryGetProperty("data", out JsonElement data)
            ? data
            : document.RootElement;
        string? eventType = ReadString(root, "e");
        if (string.Equals(eventType, "serverShutdown", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new BtcOrderBookPredictionRawEvent(
                    EventSchemaVersion,
                    runId,
                    connectionId,
                    receiveSequence,
                    ++logicalSequence,
                    BtcOrderBookPredictionEventType.Control,
                    ReadUnixMilliseconds(root, "E"),
                    null,
                    receivedUtc,
                    receivedStopwatchTicks,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "server_shutdown",
                    null)
            ];
        }

        if (string.Equals(eventType, "trade", StringComparison.OrdinalIgnoreCase) || root.TryGetProperty("t", out _))
        {
            ValidateJsonIdentity(root, wrapperStream, "btcusdt@trade");
            long tradeId = ReadInt64(root, "t") ?? throw new JsonException("Binance trade has no trade id.");
            decimal price = ReadDecimal(root, "p") ?? throw new JsonException("Binance trade has no price.");
            decimal quantity = ReadDecimal(root, "q") ?? throw new JsonException("Binance trade has no quantity.");
            bool isBuyerMaker = ReadBoolean(root, "m") ?? throw new JsonException("Binance trade has no maker flag.");
            DateTimeOffset? eventUtc = ReadUnixMilliseconds(root, "E");
            DateTimeOffset? transactUtc = ReadUnixMilliseconds(root, "T");
            long? previousId = previousTradeId;
            previousTradeId = tradeId;
            return
            [
                new BtcOrderBookPredictionRawEvent(
                    EventSchemaVersion,
                    runId,
                    connectionId,
                    receiveSequence,
                    ++logicalSequence,
                    BtcOrderBookPredictionEventType.Trade,
                    eventUtc,
                    transactUtc,
                    receivedUtc,
                    receivedStopwatchTicks,
                    null,
                    tradeId,
                    0,
                    null,
                    null,
                    null,
                    null,
                    price,
                    quantity,
                    isBuyerMaker,
                    previousId,
                    previousId is null ? null : tradeId - previousId.Value,
                    "delivered",
                    null)
            ];
        }

        ValidateJsonIdentity(root, wrapperStream, "btcusdt@bookTicker");
        long bookUpdateId = ReadInt64(root, "u") ?? throw new JsonException("Binance bookTicker has no update id.");
        decimal bid = ReadDecimal(root, "b") ?? throw new JsonException("Binance bookTicker has no bid.");
        decimal bidQty = ReadDecimal(root, "B") ?? throw new JsonException("Binance bookTicker has no bid quantity.");
        decimal ask = ReadDecimal(root, "a") ?? throw new JsonException("Binance bookTicker has no ask.");
        decimal askQty = ReadDecimal(root, "A") ?? throw new JsonException("Binance bookTicker has no ask quantity.");
        long? previousBookUpdateId = previousBookId;
        previousBookId = bookUpdateId;
        return
        [
            new BtcOrderBookPredictionRawEvent(
                EventSchemaVersion,
                runId,
                connectionId,
                receiveSequence,
                ++logicalSequence,
                BtcOrderBookPredictionEventType.Book,
                null,
                null,
                receivedUtc,
                receivedStopwatchTicks,
                bookUpdateId,
                null,
                null,
                bid,
                bidQty,
                ask,
                askQty,
                null,
                null,
                null,
                previousBookUpdateId,
                previousBookUpdateId is null ? null : bookUpdateId - previousBookUpdateId.Value,
                "delivered",
                null)
        ];
    }

    public static IReadOnlyList<BtcOrderBookPredictionRawEvent> FlattenSbeEvent(
        BinanceSbeMarketDataEvent decoded,
        string runId,
        int connectionId,
        long receiveSequence,
        ref long logicalSequence,
        long receivedStopwatchTicks,
        ref long? previousBookId,
        ref long? previousTradeId)
    {
        if (!string.Equals(decoded.Symbol, "BTCUSDT", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Binance SBE symbol mismatch: expected BTCUSDT but received " + decoded.Symbol + ".");
        }

        if (decoded is BinanceSbeBestBidAskEvent book)
        {
            long? previousId = previousBookId;
            previousBookId = book.BookUpdateId;
            return
            [
                new BtcOrderBookPredictionRawEvent(
                    EventSchemaVersion,
                    runId,
                    connectionId,
                    receiveSequence,
                    ++logicalSequence,
                    BtcOrderBookPredictionEventType.Book,
                    book.EventTimeUtc,
                    null,
                    book.ReceivedAtUtc,
                    receivedStopwatchTicks,
                    book.BookUpdateId,
                    null,
                    null,
                    book.BidPrice,
                    book.BidQty,
                    book.AskPrice,
                    book.AskQty,
                    null,
                    null,
                    null,
                    previousId,
                    previousId is null ? null : book.BookUpdateId - previousId.Value,
                    "delivered",
                    null)
            ];
        }

        if (decoded is not BinanceSbeTradeEvent tradeEvent)
        {
            return [];
        }

        var result = new List<BtcOrderBookPredictionRawEvent>(tradeEvent.Trades.Count);
        for (int index = 0; index < tradeEvent.Trades.Count; index++)
        {
            BinanceSbeTrade trade = tradeEvent.Trades[index];
            long? previousId = previousTradeId;
            previousTradeId = trade.Id;
            result.Add(new BtcOrderBookPredictionRawEvent(
                EventSchemaVersion,
                runId,
                connectionId,
                receiveSequence,
                ++logicalSequence,
                BtcOrderBookPredictionEventType.Trade,
                tradeEvent.EventTimeUtc,
                tradeEvent.TransactTimeUtc,
                tradeEvent.ReceivedAtUtc,
                receivedStopwatchTicks,
                null,
                trade.Id,
                index,
                null,
                null,
                null,
                null,
                trade.Price,
                trade.Quantity,
                trade.IsBuyerMaker,
                previousId,
                previousId is null ? null : trade.Id - previousId.Value,
                "delivered",
                null));
        }

        return result;
    }

    private async Task RunConnectionAsync(int connectionId, CancellationToken cancellationToken)
    {
        await EnqueueControlAsync(connectionId, "connection_opening", streamUri.ToString(), cancellationToken);
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(30);
        if (options.Source == BinanceOrderBookPredictionSource.Sbe)
        {
            socket.Options.SetRequestHeader("X-MBX-APIKEY", options.SbeApiKey!);
        }

        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connectCts.CancelAfter(options.ConnectTimeoutMilliseconds);
            try
            {
                await socket.ConnectAsync(streamUri, connectCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException("Binance WebSocket connect timeout.");
            }
        }

        everConnected = true;
        await EnqueueControlAsync(connectionId, "connected", null, cancellationToken);

        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        DateTimeOffset? previousReceivedUtc = null;

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiveCts.CancelAfter(options.NoDataTimeoutMilliseconds);
            try
            {
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), receiveCts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        qualityGapObserved = true;
                        await EnqueueControlAsync(
                            connectionId,
                            "disconnected",
                            $"status={result.CloseStatus};description={result.CloseStatusDescription}",
                            CancellationToken.None);
                        return;
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException("Binance WebSocket produced no complete frame before the no-data timeout.");
            }

            DateTimeOffset receivedUtc = DateTimeOffset.UtcNow;
            long receivedStopwatchTicks = Stopwatch.GetTimestamp();
            long frameSequence = ++receiveSequence;
            if (previousReceivedUtc is { } previous && receivedUtc < previous)
            {
                qualityGapObserved = true;
                await EnqueueControlAsync(connectionId, "clock_regression", $"previous={previous:O};current={receivedUtc:O}", cancellationToken);
            }

            previousReceivedUtc = receivedUtc;
            try
            {
                IReadOnlyList<BtcOrderBookPredictionRawEvent> decodedEvents;
                if (options.Source == BinanceOrderBookPredictionSource.Json)
                {
                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        qualityGapObserved = true;
                        await EnqueueControlAsync(connectionId, "unexpected_frame_type", result.MessageType.ToString(), cancellationToken);
                        continue;
                    }

                    decodedEvents = DecodeJsonFrame(
                        message.ToArray(),
                        runId,
                        connectionId,
                        frameSequence,
                        ref logicalSequence,
                        receivedUtc,
                        receivedStopwatchTicks,
                        ref previousBookId,
                        ref previousTradeId);
                }
                else
                {
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string controlText = Encoding.UTF8.GetString(message.ToArray());
                        if (controlText.Contains("serverShutdown", StringComparison.OrdinalIgnoreCase))
                        {
                            qualityGapObserved = true;
                            await EnqueueControlAsync(connectionId, "server_shutdown", controlText, cancellationToken);
                            return;
                        }

                        await EnqueueControlAsync(connectionId, "sbe_text_control", $"length={message.Length}", cancellationToken);
                        continue;
                    }

                    if (result.MessageType != WebSocketMessageType.Binary)
                    {
                        qualityGapObserved = true;
                        await EnqueueControlAsync(connectionId, "unexpected_frame_type", result.MessageType.ToString(), cancellationToken);
                        continue;
                    }

                    if (!BinanceSbeMarketDataDecoder.TryDecode(message.ToArray(), receivedUtc, out var decoded, out string? error) ||
                        decoded is null)
                    {
                        decodeErrors++;
                        qualityGapObserved = true;
                        await EnqueueControlAsync(connectionId, "decode_error", error, cancellationToken);
                        continue;
                    }

                    decodedEvents = FlattenSbeEvent(
                        decoded,
                        runId,
                        connectionId,
                        frameSequence,
                        ref logicalSequence,
                        receivedStopwatchTicks,
                        ref previousBookId,
                        ref previousTradeId);
                }

                foreach (var item in decodedEvents)
                {
                    await EnqueueAsync(item, cancellationToken);
                    if (item.EventType == BtcOrderBookPredictionEventType.Control && item.Status == "server_shutdown")
                    {
                        qualityGapObserved = true;
                        return;
                    }

                    if (item.EventType == BtcOrderBookPredictionEventType.Book)
                    {
                        bookEvents++;
                    }
                    else if (item.EventType == BtcOrderBookPredictionEventType.Trade)
                    {
                        tradeEvents++;
                    }
                }
            }
            catch (JsonException ex)
            {
                decodeErrors++;
                qualityGapObserved = true;
                await EnqueueControlAsync(connectionId, "parse_error", ex.Message, cancellationToken);
            }
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            qualityGapObserved = true;
            await EnqueueControlAsync(
                connectionId,
                "disconnected",
                "socket_state=" + socket.State,
                CancellationToken.None);
        }
    }

    private async Task EnqueueControlAsync(
        int connectionId,
        string status,
        string? detail,
        CancellationToken cancellationToken)
    {
        DateTimeOffset receivedUtc = DateTimeOffset.UtcNow;
        var item = new BtcOrderBookPredictionRawEvent(
            EventSchemaVersion,
            runId,
            connectionId,
            receiveSequence,
            ++logicalSequence,
            BtcOrderBookPredictionEventType.Control,
            null,
            null,
            receivedUtc,
            Stopwatch.GetTimestamp(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            status,
            detail);
        await EnqueueAsync(item, cancellationToken);
        controlEvents++;
    }

    private async ValueTask EnqueueAsync(
        BtcOrderBookPredictionRawEvent item,
        CancellationToken cancellationToken)
    {
        int queued = Interlocked.Increment(ref queuedEvents);
        int observed;
        while (queued > (observed = Volatile.Read(ref queueHighWaterMark)) &&
               Interlocked.CompareExchange(ref queueHighWaterMark, queued, observed) != observed)
        {
        }

        long waitStarted = Stopwatch.GetTimestamp();
        try
        {
            if (!channel.Writer.TryWrite(item))
            {
                await channel.Writer.WriteAsync(item, cancellationToken);
            }
        }
        catch
        {
            Interlocked.Decrement(ref queuedEvents);
            throw;
        }

        double waitMilliseconds = Stopwatch.GetElapsedTime(waitStarted).TotalMilliseconds;
        if (waitMilliseconds >= 50d && item.Status != "local_backpressure")
        {
            qualityGapObserved = true;
            await EnqueueControlAsync(
                item.ConnectionId,
                "local_backpressure",
                "enqueue_wait_ms=" + waitMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                cancellationToken);
        }
    }

    private async Task WriteEventsAsync()
    {
        int sinceFlush = 0;
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync())
            {
                await eventStore.WriteAsync(item);
                Interlocked.Decrement(ref queuedEvents);
                sinceFlush++;
                if (sinceFlush >= 1_000)
                {
                    await eventStore.FlushAsync();
                    sinceFlush = 0;
                }
            }

            await eventStore.FlushAsync();
        }
        catch (Exception ex)
        {
            channel.Writer.TryComplete(ex);
            throw;
        }
    }

    private static string? ReadString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static void ValidateJsonIdentity(
        JsonElement root,
        string? wrapperStream,
        string expectedStream)
    {
        string? symbol = ReadString(root, "s");
        if (!string.Equals(symbol, "BTCUSDT", StringComparison.Ordinal))
        {
            throw new JsonException("Binance JSON symbol mismatch: expected BTCUSDT.");
        }

        if (!string.Equals(wrapperStream, expectedStream, StringComparison.Ordinal))
        {
            throw new JsonException("Binance combined-stream wrapper mismatch: expected " + expectedStream + ".");
        }
    }

    private static long? ReadInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static decimal? ReadDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static bool? ReadBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static DateTimeOffset? ReadUnixMilliseconds(JsonElement root, string name)
    {
        long? milliseconds = ReadInt64(root, name);
        return milliseconds is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(milliseconds.Value);
    }
}
