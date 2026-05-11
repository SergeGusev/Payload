using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Diagnostics;

public interface IBtcOrderBookLagDiagnosticService
{
    void RecordBinanceTrade(BtcUsdReferencePricePoint point);

    void RecordPolymarketTopOfBook(MarketDataUpdate update, DateTimeOffset receivedAtUtc);
}

public sealed class BtcOrderBookLagDiagnosticService(
    ILogger<BtcOrderBookLagDiagnosticService> logger,
    BtcOrderBookLagDiagnosticsOptions options,
    IHttpClientFactory httpClientFactory,
    IAppRepository repository) : BackgroundService, IBtcOrderBookLagDiagnosticService
{
    private readonly ConcurrentQueue<BtcOrderBookLagDiagnosticEvent> queue = new();
    private int queuedCount;
    private long droppedCount;
    private DateTimeOffset lastBookTickerErrorLogUtc = DateTimeOffset.MinValue;

    public void RecordBinanceTrade(BtcUsdReferencePricePoint point)
    {
        if (!options.Enabled || !options.CaptureBinanceTrades)
        {
            return;
        }

        DateTimeOffset receivedAtUtc = point.FetchedAtUtc;
        Enqueue(new BtcOrderBookLagDiagnosticEvent(
            Guid.NewGuid(),
            "BinanceTrade",
            "Trade",
            null,
            null,
            "BTCUSDT",
            point.PriceUsd,
            null,
            null,
            null,
            null,
            null,
            point.PriceUsd,
            null,
            point.SourceUpdatedAtUtc,
            receivedAtUtc,
            ToMilliseconds(receivedAtUtc, point.SourceUpdatedAtUtc),
            point.Source,
            DateTimeOffset.UtcNow));
    }

    public void RecordPolymarketTopOfBook(MarketDataUpdate update, DateTimeOffset receivedAtUtc)
    {
        if (!options.Enabled || !options.CapturePolymarketTopOfBook)
        {
            return;
        }

        if (update.EventType is not (MarketDataEventType.BestBidAsk or MarketDataEventType.PriceChange or MarketDataEventType.Book))
        {
            return;
        }

        decimal? bestBid = update.BestBid ?? update.OrderBookSnapshot?.BestBid;
        decimal? bestAsk = update.BestAsk ?? update.OrderBookSnapshot?.BestAsk;
        if (bestBid is null && bestAsk is null)
        {
            return;
        }

        decimal? mid = bestBid is { } bid && bestAsk is { } ask ? (bid + ask) / 2m : null;
        decimal? bestBidSize = GetLevelSize(update.OrderBookSnapshot?.Bids, bestBid);
        decimal? bestAskSize = GetLevelSize(update.OrderBookSnapshot?.Asks, bestAsk);
        DateTimeOffset? sourceTimestampUtc = update.TimestampUtc == default ? null : update.TimestampUtc;
        Enqueue(new BtcOrderBookLagDiagnosticEvent(
            Guid.NewGuid(),
            "PolymarketTopOfBook",
            update.EventType.ToString(),
            update.AssetId,
            update.ConditionId,
            null,
            null,
            bestBid,
            bestBidSize,
            bestAsk,
            bestAskSize,
            mid,
            update.Price,
            update.Size,
            sourceTimestampUtc,
            receivedAtUtc,
            sourceTimestampUtc.HasValue ? ToMilliseconds(receivedAtUtc, sourceTimestampUtc.Value) : null,
            update.RawEventType,
            DateTimeOffset.UtcNow));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("BTC/order-book lag diagnostics are disabled.");
            return;
        }

        logger.LogInformation(
            "BTC/order-book lag diagnostics started. RetentionMinutes={RetentionMinutes} FlushIntervalMilliseconds={FlushIntervalMilliseconds} MaxBatchSize={MaxBatchSize}",
            options.RetentionMinutes,
            options.FlushIntervalMilliseconds,
            options.MaxBatchSize);

        DateTimeOffset nextCleanupUtc = DateTimeOffset.UtcNow.AddMinutes(options.CleanupIntervalMinutes);
        Task? bookTickerTask = options.CaptureBinanceBookTicker
            ? RunBinanceBookTickerLoopAsync(stoppingToken)
            : null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(options.FlushIntervalMilliseconds), stoppingToken);
                await FlushAsync(stoppingToken);

                if (DateTimeOffset.UtcNow >= nextCleanupUtc)
                {
                    await CleanupAsync(stoppingToken);
                    nextCleanupUtc = DateTimeOffset.UtcNow.AddMinutes(options.CleanupIntervalMinutes);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (bookTickerTask is not null)
            {
                await AwaitBackgroundTaskAsync(bookTickerTask);
            }

            await FlushAsync(CancellationToken.None);
            logger.LogInformation("BTC/order-book lag diagnostics stopped.");
        }
    }

    private async Task RunBinanceBookTickerLoopAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "BTC/order-book lag diagnostics Binance bookTicker polling started. Url={Url} PollIntervalMilliseconds={PollIntervalMilliseconds}",
            options.BinanceBookTickerUrl,
            options.BinanceBookTickerPollIntervalMilliseconds);

        HttpClient httpClient = httpClientFactory.CreateClient(nameof(BtcOrderBookLagDiagnosticService));
        httpClient.Timeout = TimeSpan.FromMilliseconds(options.BinanceBookTickerTimeoutMilliseconds);
        TimeSpan pollInterval = TimeSpan.FromMilliseconds(Math.Max(1, options.BinanceBookTickerPollIntervalMilliseconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using HttpResponseMessage response = await httpClient.GetAsync(options.BinanceBookTickerUrl, stoppingToken);
                DateTimeOffset receivedAtUtc = DateTimeOffset.UtcNow;
                string json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    LogBookTickerError($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
                else if (TryParseBinanceBookTicker(json, receivedAtUtc, out BtcOrderBookLagDiagnosticEvent? diagnosticEvent, out string? parseError) &&
                         diagnosticEvent is not null)
                {
                    Enqueue(diagnosticEvent);
                }
                else
                {
                    LogBookTickerError(parseError ?? "Unexpected bookTicker response.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogBookTickerError(ex.Message);
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("BTC/order-book lag diagnostics Binance bookTicker polling stopped.");
    }

    private void Enqueue(BtcOrderBookLagDiagnosticEvent diagnosticEvent)
    {
        int count = Interlocked.Increment(ref queuedCount);
        if (count > options.MaxQueueSize)
        {
            Interlocked.Decrement(ref queuedCount);
            Interlocked.Increment(ref droppedCount);
            return;
        }

        queue.Enqueue(diagnosticEvent);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        List<BtcOrderBookLagDiagnosticEvent> batch = DrainBatch();
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await repository.AddBtcOrderBookLagDiagnosticEventsAsync(batch, cancellationToken);
            long dropped = Interlocked.Exchange(ref droppedCount, 0);
            if (dropped > 0)
            {
                logger.LogWarning("BTC/order-book lag diagnostics dropped {DroppedEvents} events because the queue was full.", dropped);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Requeue(batch);
            throw;
        }
        catch (Exception ex)
        {
            Requeue(batch);
            logger.LogError(ex, "Failed to persist BTC/order-book lag diagnostic events.");
        }
    }

    private List<BtcOrderBookLagDiagnosticEvent> DrainBatch()
    {
        int maxBatchSize = Math.Max(1, options.MaxBatchSize);
        var batch = new List<BtcOrderBookLagDiagnosticEvent>(maxBatchSize);
        while (batch.Count < maxBatchSize && queue.TryDequeue(out BtcOrderBookLagDiagnosticEvent? diagnosticEvent))
        {
            Interlocked.Decrement(ref queuedCount);
            batch.Add(diagnosticEvent);
        }

        return batch;
    }

    private void Requeue(IReadOnlyList<BtcOrderBookLagDiagnosticEvent> batch)
    {
        foreach (BtcOrderBookLagDiagnosticEvent diagnosticEvent in batch)
        {
            Enqueue(diagnosticEvent);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset receivedBeforeUtc = DateTimeOffset.UtcNow.AddMinutes(-options.RetentionMinutes);
        try
        {
            int deleted = await repository.CleanupBtcOrderBookLagDiagnosticEventsAsync(
                receivedBeforeUtc,
                options.CleanupBatchSize,
                cancellationToken);
            if (deleted > 0)
            {
                logger.LogInformation("BTC/order-book lag diagnostics cleanup deleted {DeletedRows} rows.", deleted);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BTC/order-book lag diagnostics cleanup failed.");
        }
    }

    private static decimal ToMilliseconds(DateTimeOffset receivedAtUtc, DateTimeOffset sourceTimestampUtc)
    {
        return (decimal)(receivedAtUtc - sourceTimestampUtc).TotalMilliseconds;
    }

    private static decimal? GetLevelSize(IReadOnlyList<OrderBookLevel>? levels, decimal? price)
    {
        if (levels is null || price is null)
        {
            return null;
        }

        foreach (OrderBookLevel level in levels)
        {
            if (level.Price == price.Value)
            {
                return level.Size;
            }
        }

        return null;
    }

    private static bool TryParseBinanceBookTicker(
        string json,
        DateTimeOffset receivedAtUtc,
        out BtcOrderBookLagDiagnosticEvent? diagnosticEvent,
        out string? error)
    {
        diagnosticEvent = null;
        error = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string symbol = ReadString(root, "symbol") ?? "BTCUSDT";
            decimal? bidPrice = ReadDecimal(root, "bidPrice");
            decimal? bidQty = ReadDecimal(root, "bidQty");
            decimal? askPrice = ReadDecimal(root, "askPrice");
            decimal? askQty = ReadDecimal(root, "askQty");
            if (bidPrice is null && askPrice is null)
            {
                error = "Binance bookTicker response has no bidPrice or askPrice.";
                return false;
            }

            decimal? mid = bidPrice is { } bid && askPrice is { } ask ? (bid + ask) / 2m : null;
            diagnosticEvent = new BtcOrderBookLagDiagnosticEvent(
                Guid.NewGuid(),
                "BinanceBookTicker",
                "BookTicker",
                null,
                null,
                symbol,
                mid ?? bidPrice ?? askPrice,
                bidPrice,
                bidQty,
                askPrice,
                askQty,
                mid,
                null,
                null,
                null,
                receivedAtUtc,
                null,
                "REST",
                DateTimeOffset.UtcNow);
            return true;
        }
        catch (JsonException ex)
        {
            error = "Invalid Binance bookTicker JSON: " + ex.Message;
            return false;
        }
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static decimal? ReadDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out decimal number))
        {
            return number;
        }

        return property.ValueKind == JsonValueKind.String &&
               decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
    }

    private void LogBookTickerError(string message)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if ((now - lastBookTickerErrorLogUtc).TotalSeconds < 60)
        {
            return;
        }

        lastBookTickerErrorLogUtc = now;
        logger.LogWarning("BTC/order-book lag diagnostics Binance bookTicker polling failed: {ErrorMessage}", message);
    }

    private static async Task AwaitBackgroundTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
