using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Tests;

public sealed class MarketDataSideEffectQueueTests
{
    [Fact]
    public async Task EnqueueUpdate_CoalescesReplaceableQuotesToLatestValue()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Assert.Equal(MarketDataSideEffectEnqueueOutcome.Enqueued, Enqueue(queue, Quote("blocker", 0.10m)));
            await handler.WaitForFirstUpdateAsync();

            Assert.Equal(MarketDataSideEffectEnqueueOutcome.Enqueued, Enqueue(queue, Quote("asset-1", 0.50m)));
            Assert.Equal(MarketDataSideEffectEnqueueOutcome.Coalesced, Enqueue(queue, Quote("asset-1", 0.51m)));
            Assert.Equal(MarketDataSideEffectEnqueueOutcome.Coalesced, Enqueue(queue, Quote("asset-1", 0.52m)));

            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);

            var processedAssetUpdates = handler.ProcessedUpdates
                .Where(item => item.Update.AssetId == "asset-1")
                .ToArray();
            Assert.Single(processedAssetUpdates);
            Assert.Equal(0.52m, processedAssetUpdates[0].Update.BestBid);
            var metrics = queue.GetMetrics();
            Assert.Equal(2, metrics.CoalescedUpdates);
            Assert.Equal(2, metrics.ProcessedUpdates);
            Assert.Equal(0, metrics.PendingUpdates);
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EnqueueUpdate_DoesNotCoalesceTradesResolutionsOrOpenOrderQuotes()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler);
        await queue.StartAsync(CancellationToken.None);
        var orderId = Guid.NewGuid();

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();

            Enqueue(queue, Trade("asset-1", 0.48m));
            Enqueue(queue, Trade("asset-1", 0.49m));
            Enqueue(queue, Quote("asset-1", 0.50m), new HashSet<Guid> { orderId });
            Enqueue(queue, Quote("asset-1", 0.51m), new HashSet<Guid> { orderId });
            Enqueue(queue, Resolved("asset-1"));

            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);

            var processed = handler.ProcessedUpdates
                .Where(item => item.Update.AssetId == "asset-1")
                .ToArray();
            Assert.Equal(5, processed.Length);
            Assert.Equal(
                [
                    MarketDataEventType.LastTradePrice,
                    MarketDataEventType.LastTradePrice,
                    MarketDataEventType.BestBidAsk,
                    MarketDataEventType.BestBidAsk,
                    MarketDataEventType.MarketResolved
                ],
                processed.Select(item => item.Update.EventType).ToArray());
            Assert.All(
                processed.Where(item => item.Update.EventType == MarketDataEventType.BestBidAsk),
                item => Assert.Contains(orderId, item.EligiblePaperOrderIds!));
            Assert.Equal(0, queue.GetMetrics().CoalescedUpdates);
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EnqueueUpdate_SoftLimitNeverDropsNonReplaceableUpdates()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler, maxPendingUpdatesPerAsset: 1);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();

            Assert.Equal(MarketDataSideEffectEnqueueOutcome.Enqueued, Enqueue(queue, Trade("asset-1", 0.48m)));
            Assert.Equal(MarketDataSideEffectEnqueueOutcome.Enqueued, Enqueue(queue, Trade("asset-1", 0.49m)));
            Assert.Equal(MarketDataSideEffectEnqueueOutcome.Enqueued, Enqueue(queue, Resolved("asset-1")));

            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);

            Assert.Equal(3, handler.ProcessedUpdates.Count(item => item.Update.AssetId == "asset-1"));
            Assert.True(queue.GetMetrics().UpdateSoftLimitOverflows >= 2);
            Assert.Equal(0, queue.GetMetrics().RejectedUpdates);
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EnqueueUpdate_DoesNotCoalesceWhenRawPersistenceIsEnabled()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler, persistMarketDataEvents: true);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();

            Assert.Equal(MarketDataSideEffectEnqueueOutcome.Enqueued, Enqueue(queue, Quote("asset-1", 0.50m)));
            Assert.Equal(MarketDataSideEffectEnqueueOutcome.Enqueued, Enqueue(queue, Quote("asset-1", 0.51m)));

            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);

            Assert.Equal(2, handler.ProcessedUpdates.Count(item => item.Update.AssetId == "asset-1"));
            Assert.Equal(0, queue.GetMetrics().CoalescedUpdates);
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EnqueueUpdate_DoesNotCoalesceWhenExposureSnapshotIsUnknown()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();

            Assert.Equal(
                MarketDataSideEffectEnqueueOutcome.Enqueued,
                queue.EnqueueUpdate("test", Quote("asset-1", 0.50m), null, DateTimeOffset.UtcNow, null));
            Assert.Equal(
                MarketDataSideEffectEnqueueOutcome.Enqueued,
                queue.EnqueueUpdate("test", Quote("asset-1", 0.51m), null, DateTimeOffset.UtcNow, null));

            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);

            Assert.Equal(2, handler.ProcessedUpdates.Count(item => item.Update.AssetId == "asset-1"));
            Assert.All(
                handler.ProcessedUpdates.Where(item => item.Update.AssetId == "asset-1"),
                item => Assert.Null(item.EligiblePaperOrderIds));
            Assert.Equal(0, queue.GetMetrics().CoalescedUpdates);
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EnqueueUpdate_DoesNotCoalesceQuotesAcrossResolutionBoundary()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();

            Enqueue(queue, Quote("asset-1", 0.50m));
            Enqueue(queue, Resolved("asset-1"));
            Enqueue(queue, Quote("asset-1", 0.51m));

            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);

            Assert.Equal(
                [
                    MarketDataEventType.BestBidAsk,
                    MarketDataEventType.MarketResolved,
                    MarketDataEventType.BestBidAsk
                ],
                handler.ProcessedUpdates
                    .Where(item => item.Update.AssetId == "asset-1")
                    .Select(item => item.Update.EventType)
                    .ToArray());
            Assert.Equal(0, queue.GetMetrics().CoalescedUpdates);
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EnqueueFrameDiagnostic_DropsRoutineSamplesBeforeImportantDiagnostics()
    {
        var handler = new ControlledHandler(blockFirstDiagnostic: true);
        var queue = CreateQueue(handler, diagnosticCapacity: 1);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Assert.Equal(
                MarketDataSideEffectEnqueueOutcome.Enqueued,
                queue.EnqueueFrameDiagnostic(Diagnostic("first"), important: false));
            await handler.WaitForFirstDiagnosticAsync();

            Assert.Equal(
                MarketDataSideEffectEnqueueOutcome.Enqueued,
                queue.EnqueueFrameDiagnostic(Diagnostic("routine-queued"), important: false));
            Assert.Equal(
                MarketDataSideEffectEnqueueOutcome.Dropped,
                queue.EnqueueFrameDiagnostic(Diagnostic("routine-dropped"), important: false));
            Assert.Equal(
                MarketDataSideEffectEnqueueOutcome.Enqueued,
                queue.EnqueueFrameDiagnostic(Diagnostic("important"), important: true));

            handler.ReleaseFirstDiagnostic();
            await queue.StopAsync(CancellationToken.None);

            Assert.Equal(["first", "important"], handler.ProcessedDiagnostics.Select(item => item.RawPayload).ToArray());
            Assert.Equal(2, queue.GetMetrics().DroppedDiagnostics);
            Assert.Equal(0, queue.GetMetrics().PendingDiagnostics);
        }
        finally
        {
            handler.ReleaseFirstDiagnostic();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StopAsync_DrainsAllAcceptedUpdates()
    {
        var handler = new ControlledHandler();
        var queue = CreateQueue(handler);
        await queue.StartAsync(CancellationToken.None);

        for (var index = 0; index < 20; index++)
        {
            Enqueue(queue, Trade($"asset-{index}", 0.40m + index / 100m));
        }

        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(20, handler.ProcessedUpdates.Count);
        Assert.Equal(0, queue.GetMetrics().PendingUpdates);
        Assert.Equal(
            MarketDataSideEffectEnqueueOutcome.Rejected,
            Enqueue(queue, Trade("after-stop", 0.90m)));
    }

    [Fact]
    public async Task EnqueueApiError_PersistsThroughDiagnosticWorker()
    {
        var handler = new ControlledHandler();
        var queue = CreateQueue(handler);
        await queue.StartAsync(CancellationToken.None);
        var apiError = new ApiError(
            Guid.NewGuid(),
            "test-component",
            "ParseMarketMessage",
            "invalid json",
            DateTimeOffset.UtcNow);

        Assert.Equal(MarketDataSideEffectEnqueueOutcome.Enqueued, queue.EnqueueApiError(apiError));
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(apiError, Assert.Single(handler.ProcessedApiErrors));
        Assert.Equal(1, queue.GetMetrics().ProcessedDiagnostics);
        Assert.Equal(0, queue.GetMetrics().PendingDiagnostics);
    }

    private static MarketDataSideEffectQueue CreateQueue(
        ControlledHandler handler,
        int maxPendingUpdatesPerAsset = 32,
        int diagnosticCapacity = 256,
        bool persistMarketDataEvents = false)
    {
        return new MarketDataSideEffectQueue(
            NullLogger<MarketDataSideEffectQueue>.Instance,
            new MarketDataWebSocketOptions
            {
                SideEffectMaxPendingUpdatesPerAsset = maxPendingUpdatesPerAsset,
                SideEffectDiagnosticQueueCapacity = diagnosticCapacity,
                SideEffectMetricsIntervalSeconds = 3_600,
                PersistMarketDataEvents = persistMarketDataEvents
            },
            handler,
            new TestAppRepository());
    }

    private static MarketDataSideEffectEnqueueOutcome Enqueue(
        IMarketDataSideEffectQueue queue,
        MarketDataUpdate update,
        IReadOnlySet<Guid>? eligiblePaperOrderIds = null)
    {
        return queue.EnqueueUpdate(
            "test-component",
            update,
            null,
            DateTimeOffset.UtcNow,
            eligiblePaperOrderIds ?? new HashSet<Guid>());
    }

    private static MarketDataUpdate Quote(string assetId, decimal bestBid)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var orderBook = new OrderBookSnapshot(
            assetId,
            [new OrderBookLevel(bestBid, 10m)],
            [new OrderBookLevel(bestBid + 0.02m, 10m)],
            timestamp,
            "condition-1");
        return new MarketDataUpdate(
            MarketDataEventType.BestBidAsk,
            "best_bid_ask",
            assetId,
            "condition-1",
            orderBook,
            bestBid,
            bestBid + 0.02m,
            null,
            null,
            TradeSide.Unknown,
            false,
            timestamp);
    }

    private static MarketDataUpdate Trade(string assetId, decimal price)
    {
        return new MarketDataUpdate(
            MarketDataEventType.LastTradePrice,
            "last_trade_price",
            assetId,
            "condition-1",
            null,
            null,
            null,
            price,
            10m,
            TradeSide.Buy,
            false,
            DateTimeOffset.UtcNow);
    }

    private static MarketDataUpdate Resolved(string assetId)
    {
        return new MarketDataUpdate(
            MarketDataEventType.MarketResolved,
            "market_resolved",
            assetId,
            "condition-1",
            null,
            null,
            null,
            null,
            null,
            TradeSide.Unknown,
            true,
            DateTimeOffset.UtcNow,
            WinningAssetId: assetId,
            WinningOutcome: "Yes");
    }

    private static MarketWebSocketFrameDiagnostic Diagnostic(string payload)
    {
        return new MarketWebSocketFrameDiagnostic(
            Guid.NewGuid(),
            "test-component",
            DateTimeOffset.UtcNow,
            "JsonObject",
            payload.Length,
            "hash",
            1,
            "[]",
            "[]",
            "[]",
            false,
            false,
            true,
            1,
            null,
            payload,
            false,
            DateTimeOffset.UtcNow);
    }

    private sealed class ControlledHandler(
        bool blockFirstUpdate = false,
        bool blockFirstDiagnostic = false) : IMarketDataSideEffectHandler
    {
        private readonly TaskCompletionSource<bool> firstUpdateStarted = NewCompletionSource();
        private readonly TaskCompletionSource<bool> releaseFirstUpdate = NewCompletionSource();
        private readonly TaskCompletionSource<bool> firstDiagnosticStarted = NewCompletionSource();
        private readonly TaskCompletionSource<bool> releaseFirstDiagnostic = NewCompletionSource();
        private int updateCalls;
        private int diagnosticCalls;

        public ConcurrentQueue<MarketDataSideEffectWorkItem> ProcessedUpdates { get; } = new();

        public ConcurrentQueue<MarketWebSocketFrameDiagnostic> ProcessedDiagnostics { get; } = new();

        public ConcurrentQueue<ApiError> ProcessedApiErrors { get; } = new();

        public async Task ProcessUpdateAsync(
            MarketDataSideEffectWorkItem workItem,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref updateCalls);
            if (blockFirstUpdate && call == 1)
            {
                firstUpdateStarted.TrySetResult(true);
                await releaseFirstUpdate.Task.WaitAsync(cancellationToken);
            }

            ProcessedUpdates.Enqueue(workItem);
        }

        public async Task PersistFrameDiagnosticAsync(
            MarketWebSocketFrameDiagnostic diagnostic,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref diagnosticCalls);
            if (blockFirstDiagnostic && call == 1)
            {
                firstDiagnosticStarted.TrySetResult(true);
                await releaseFirstDiagnostic.Task.WaitAsync(cancellationToken);
            }

            ProcessedDiagnostics.Enqueue(diagnostic);
        }

        public Task PersistApiErrorAsync(ApiError apiError, CancellationToken cancellationToken = default)
        {
            ProcessedApiErrors.Enqueue(apiError);
            return Task.CompletedTask;
        }

        public Task WaitForFirstUpdateAsync()
        {
            return firstUpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public Task WaitForFirstDiagnosticAsync()
        {
            return firstDiagnosticStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void ReleaseFirstUpdate()
        {
            releaseFirstUpdate.TrySetResult(true);
        }

        public void ReleaseFirstDiagnostic()
        {
            releaseFirstDiagnostic.TrySetResult(true);
        }

        private static TaskCompletionSource<bool> NewCompletionSource()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
