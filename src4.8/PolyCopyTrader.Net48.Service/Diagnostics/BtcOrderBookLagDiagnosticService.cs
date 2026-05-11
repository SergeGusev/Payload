using System.Collections.Concurrent;
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
    IAppRepository repository) : BackgroundService, IBtcOrderBookLagDiagnosticService
{
    private readonly ConcurrentQueue<BtcOrderBookLagDiagnosticEvent> queue = new();
    private int queuedCount;
    private long droppedCount;

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
            bestAsk,
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
            await FlushAsync(CancellationToken.None);
            logger.LogInformation("BTC/order-book lag diagnostics stopped.");
        }
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
}
