using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;

namespace PolyCopyTrader.Tests;

public sealed class MarketDataSideEffectQueueTests
{
    [Fact]
    public void WebSocketClassification_SeparatesExactMakerFromOrdinaryPaperOrderIds()
    {
        var now = DateTimeOffset.UtcNow;
        var makerOrder = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0xmaker",
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Up",
            0.50m,
            10m,
            5m,
            now,
            now.AddMinutes(1),
            ExecutionSource: MakerGtdPaperExecutionContract.ExecutionSource);
        var ordinaryOrder = makerOrder with
        {
            Id = Guid.NewGuid(),
            ExecutionSource = "ordinary_paper"
        };
        var capturedOrderIds = new HashSet<Guid> { makerOrder.Id, ordinaryOrder.Id };

        var classification = MarketDataWebSocketService.ClassifyCapturedPaperOrderIds(
            "asset-1",
            capturedOrderIds,
            [makerOrder, ordinaryOrder]);

        Assert.Equal(capturedOrderIds, classification.AllPaperOrderIds);
        Assert.Equal(new HashSet<Guid> { ordinaryOrder.Id }, classification.OrdinaryPaperOrderIds);
        Assert.Equal(new HashSet<Guid> { makerOrder.Id }, classification.MakerGtdPaperOrderIds);
    }

    [Fact]
    public async Task EnqueueUpdate_DedicatedMakerLaneRunsWhileGeneralLaneIsBlocked()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var makerUpdater = new ControlledMakerUpdater(expectedMakerUpdates: 3);
        var queue = CreateQueue(handler, paperTradingMarketDataUpdater: makerUpdater);
        await queue.StartAsync(CancellationToken.None);
        var makerOrderId = Guid.NewGuid();

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();

            for (var index = 0; index < 3; index++)
            {
                var update = Quote("asset-1", 0.50m + index * 0.01m) with
                {
                    SourceEventId = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    EventFingerprint = $"maker-{index}"
                };
                Assert.Equal(
                    MarketDataSideEffectEnqueueOutcome.Enqueued,
                    queue.EnqueueUpdate(
                        "test-component",
                        update,
                        null,
                        DateTimeOffset.UtcNow,
                        new HashSet<Guid>(),
                        new HashSet<Guid> { makerOrderId }));
            }

            await makerUpdater.WaitForExpectedUpdatesAsync();

            Assert.DoesNotContain(handler.ProcessedUpdates, item => item.Update.AssetId == "asset-1");
            var metrics = queue.GetMetrics();
            Assert.Equal(3, metrics.EnqueuedMakerUpdates);
            Assert.Equal(3, metrics.ProcessedMakerUpdates);
            Assert.Equal(0, metrics.FailedMakerUpdates);
            Assert.Equal(2, metrics.CoalescedUpdates);
            Assert.Null(makerUpdater.SequenceFailure);
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EnqueueUpdate_DedicatedMakerLaneRetainsProductionScaleBurstInFifoOrder()
    {
        const int eventCount = 160_000;
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var makerUpdater = new ControlledMakerUpdater(
            eventCount,
            expectedEvidencePrefix: "maker-burst");
        var queue = CreateQueue(handler, paperTradingMarketDataUpdater: makerUpdater);
        await queue.StartAsync(CancellationToken.None);
        var makerOrderId = Guid.NewGuid();
        var makerOrderIds = new HashSet<Guid> { makerOrderId };
        var emptyOrdinaryOrderIds = new HashSet<Guid>();
        var receivedAtUtc = DateTimeOffset.UtcNow;
        var baseUpdate = Quote("asset-1", 0.50m);

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();

            for (var index = 0; index < eventCount; index++)
            {
                var update = baseUpdate with
                {
                    SourceEventId = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    EventFingerprint = $"maker-burst-{index}",
                    RawJson = $"{{\"sequence\":{index}}}"
                };
                var outcome = queue.EnqueueUpdate(
                    "test-component",
                    update,
                    null,
                    receivedAtUtc.AddTicks(index),
                    emptyOrdinaryOrderIds,
                    makerOrderIds);
                Assert.Equal(MarketDataSideEffectEnqueueOutcome.Enqueued, outcome);
            }

            Assert.Equal(eventCount, queue.GetMetrics().EnqueuedMakerUpdates);
            await queue.DrainOutstandingPaperOrderUpdatesAsync(
                makerOrderId,
                "asset-1",
                "condition-1",
                receivedAtUtc.AddTicks(-1),
                receivedAtUtc.AddTicks(eventCount + 1),
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
            await makerUpdater.WaitForExpectedUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(30));

            var metrics = queue.GetMetrics();
            Assert.Equal(eventCount, metrics.EnqueuedMakerUpdates);
            Assert.Equal(eventCount, metrics.ProcessedMakerUpdates);
            Assert.Equal(0, metrics.PendingMakerUpdates);
            Assert.Equal(0, metrics.FailedMakerUpdates);
            Assert.True(metrics.CoalescedUpdates >= eventCount - 1);
            Assert.Null(makerUpdater.SequenceFailure);
            Assert.Null(makerUpdater.EvidenceFailure);
            Assert.DoesNotContain(handler.ProcessedUpdates, item => item.Update.AssetId == "asset-1");
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EnqueueUpdate_DedicatedMakerFailureIsRecordedAndLaterEvidenceContinues()
    {
        var handler = new ControlledHandler();
        var makerUpdater = new ControlledMakerUpdater(expectedMakerUpdates: 2, failSequence: 0);
        var handoff = new MakerGtdPaperPlacementHandoff();
        var makerOrderId = Guid.NewGuid();
        handoff.TrackMakerGtdPaperOrder(
            makerOrderId,
            MakerGtdPaperExecutionContract.ExecutionSource);
        var queue = CreateQueue(
            handler,
            makerGtdPaperPlacementHandoff: handoff,
            paperTradingMarketDataUpdater: makerUpdater);
        await queue.StartAsync(CancellationToken.None);
        var receivedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            for (var index = 0; index < 2; index++)
            {
                var update = Quote("asset-1", 0.50m + index * 0.01m) with
                {
                    SourceEventId = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    EventFingerprint = $"maker-failure-{index}"
                };
                Assert.Equal(
                    MarketDataSideEffectEnqueueOutcome.Enqueued,
                    queue.EnqueueUpdate(
                        "test-component",
                        update,
                        null,
                        receivedAtUtc.AddTicks(index),
                        new HashSet<Guid>(),
                        new HashSet<Guid> { makerOrderId }));
            }

            await queue.DrainOutstandingPaperOrderUpdatesAsync(
                makerOrderId,
                "asset-1",
                "condition-1",
                receivedAtUtc.AddTicks(-1),
                receivedAtUtc.AddTicks(3),
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await makerUpdater.WaitForExpectedUpdatesAsync();

            var metrics = queue.GetMetrics();
            Assert.Equal(1, metrics.FailedMakerUpdates);
            Assert.Equal(1, metrics.ProcessedMakerUpdates);
            Assert.Equal(0, metrics.PendingMakerUpdates);
            Assert.Null(makerUpdater.SequenceFailure);
            Assert.True(handoff.TryGetMarketDataFailure(
                makerOrderId,
                "asset-1",
                "condition-1",
                receivedAtUtc.AddTicks(-1),
                receivedAtUtc.AddTicks(3),
                out var failure));
            Assert.Equal(
                MakerGtdPaperExecutionContract.MarketDataHandlerFailureCode,
                Assert.IsType<MakerGtdPaperMarketDataFailure>(failure).FailureCode);
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GetMetrics_DistinguishesBlockedMakerInFlightFromGeneralWork()
    {
        var handler = new ControlledHandler();
        var makerUpdater = new ControlledMakerUpdater(
            expectedMakerUpdates: 1,
            blockFirstUpdate: true);
        var queue = CreateQueue(handler, paperTradingMarketDataUpdater: makerUpdater);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Assert.Equal(
                MarketDataSideEffectEnqueueOutcome.Enqueued,
                queue.EnqueueUpdate(
                    "test-component",
                    Quote("asset-1", 0.50m) with { SourceEventId = "0" },
                    null,
                    DateTimeOffset.UtcNow,
                    new HashSet<Guid>(),
                    new HashSet<Guid> { Guid.NewGuid() }));
            await makerUpdater.WaitForFirstUpdateAsync();

            var metrics = queue.GetMetrics();
            Assert.Equal(0, metrics.PendingMakerUpdates);
            Assert.Equal(1, metrics.InFlightMakerUpdates);
            Assert.Equal(0, metrics.PendingGeneralUpdates);
            Assert.Equal(0, metrics.InFlightGeneralUpdates);
        }
        finally
        {
            makerUpdater.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

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
    public async Task HasOutstandingPaperOrderUpdate_DetectsMatchingPendingUpdate()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler);
        await queue.StartAsync(CancellationToken.None);
        var orderId = Guid.NewGuid();
        var acceptedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1);

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();
            Enqueue(
                queue,
                Quote("asset-maker", 0.49m),
                new HashSet<Guid> { orderId });

            Assert.True(queue.HasOutstandingPaperOrderUpdate(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc));
            var capturedAtUtc = DateTimeOffset.UtcNow;
            var snapshot = queue.GetPaperOrderPreflightSnapshot(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc,
                capturedAtUtc);
            Assert.Equal(MarketDataSideEffectDiagnosticSchema.Available, snapshot.Availability);
            Assert.Null(snapshot.UnavailableReason);
            Assert.Equal(1, snapshot.MatchingOutstandingCount);
            Assert.Equal(0, snapshot.MatchingInFlightCount);
            Assert.Equal(1, snapshot.MatchingPendingCount);
            Assert.NotNull(snapshot.OldestMatchingReceivedAtUtc);
            Assert.NotNull(snapshot.OldestMatchingReceivedAgeMilliseconds);
            Assert.NotNull(snapshot.OldestMatchingEnqueuedAtUtc);
            Assert.NotNull(snapshot.OldestMatchingEnqueuedAgeMilliseconds);
            Assert.Equal(1, snapshot.TotalPendingUpdates);
            Assert.Equal(1, snapshot.TrackedAssets);
            Assert.NotEqual(MarketDataSideEffectDiagnosticSchema.NotAvailable, snapshot.UpdateWorkerState);
            var inFlight = Assert.IsType<MarketDataSideEffectExecutionTraceSnapshot>(snapshot.InFlightUpdate);
            Assert.Equal("blocker", inFlight.AssetId);
            Assert.Equal(MarketDataSideEffectPhases.Processing, inFlight.Phase);
            Assert.NotNull(inFlight.ProcessingStartedAtUtc);
            Assert.True(inFlight.QueueAgeMilliseconds >= 0d);
            Assert.True(inFlight.ProcessingAgeMilliseconds >= 0d);
            Assert.True(inFlight.PhaseAgeMilliseconds >= 0d);
            Assert.False(queue.HasOutstandingPaperOrderUpdate(
                Guid.NewGuid(),
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc));
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }

        Assert.False(queue.HasOutstandingPaperOrderUpdate(
            orderId,
            "asset-maker",
            "condition-1",
            acceptedAtUtc,
            expiresAtUtc));
    }

    [Fact]
    public async Task HasOutstandingPaperOrderUpdate_RemainsTrueWhileUpdateIsInFlight()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler);
        await queue.StartAsync(CancellationToken.None);
        var orderId = Guid.NewGuid();
        var acceptedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1);

        try
        {
            Enqueue(
                queue,
                Quote("asset-maker", 0.49m),
                new HashSet<Guid> { orderId });
            await handler.WaitForFirstUpdateAsync();

            Assert.Equal(0, queue.GetMetrics().PendingUpdates);
            Assert.True(queue.HasOutstandingPaperOrderUpdate(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc));
            var snapshot = queue.GetPaperOrderPreflightSnapshot(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc,
                DateTimeOffset.UtcNow);
            Assert.Equal(1, snapshot.MatchingOutstandingCount);
            Assert.Equal(1, snapshot.MatchingInFlightCount);
            Assert.Equal(0, snapshot.MatchingPendingCount);
            var inFlight = Assert.IsType<MarketDataSideEffectExecutionTraceSnapshot>(snapshot.InFlightUpdate);
            Assert.Equal("asset-maker", inFlight.AssetId);
            Assert.Equal("condition-1", inFlight.ConditionId);
            Assert.Equal(MarketDataEventType.BestBidAsk, inFlight.EventType);
            Assert.Equal(MarketDataSideEffectPhases.Processing, inFlight.Phase);
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }

        Assert.False(queue.HasOutstandingPaperOrderUpdate(
            orderId,
            "asset-maker",
            "condition-1",
            acceptedAtUtc,
            expiresAtUtc));
    }

    [Fact]
    public async Task HandlerFailure_RecordsExactMakerOrderPoisonBeforeInFlightClears()
    {
        var handler = new ControlledHandler(throwOnUpdate: true);
        var handoff = new MakerGtdPaperPlacementHandoff();
        var queue = CreateQueue(handler, makerGtdPaperPlacementHandoff: handoff);
        var orderId = Guid.NewGuid();
        handoff.TrackMakerGtdPaperOrder(
            orderId,
            MakerGtdPaperExecutionContract.ExecutionSource);
        var receivedAtUtc = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        await queue.StartAsync(CancellationToken.None);

        queue.EnqueueUpdate(
            "test-component",
            Quote("asset-maker", 0.49m),
            null,
            receivedAtUtc,
            new HashSet<Guid> { orderId });
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(1, queue.GetMetrics().FailedUpdates);
        Assert.False(queue.HasOutstandingPaperOrderUpdate(
            orderId,
            "asset-maker",
            "condition-1",
            receivedAtUtc.AddSeconds(-1),
            receivedAtUtc.AddSeconds(1)));
        Assert.True(handoff.TryGetMarketDataFailure(
            orderId,
            "asset-maker",
            "condition-1",
            receivedAtUtc.AddSeconds(-1),
            receivedAtUtc.AddSeconds(1),
            out var failure));
        Assert.Equal(
            MakerGtdPaperExecutionContract.MarketDataHandlerFailureCode,
            Assert.IsType<MakerGtdPaperMarketDataFailure>(failure).FailureCode);
    }

    [Fact]
    public async Task DrainOutstandingPaperOrderUpdates_PrioritizesMatchingAssetOverUnrelatedBacklog()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler);
        var orderId = Guid.NewGuid();
        var acceptedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receivedAtUtc = acceptedAtUtc.AddSeconds(10);
        var expiresAtUtc = acceptedAtUtc.AddMinutes(1);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();
            EnqueueAt(queue, Quote("unrelated-1", 0.20m), receivedAtUtc, new HashSet<Guid>());
            EnqueueAt(queue, Quote("unrelated-2", 0.30m), receivedAtUtc, new HashSet<Guid>());
            EnqueueAt(queue, Quote("asset-maker", 0.49m), receivedAtUtc, new HashSet<Guid> { orderId });

            var drainTask = queue.DrainOutstandingPaperOrderUpdatesAsync(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc);
            Assert.False(drainTask.IsCompleted);

            handler.ReleaseFirstUpdate();
            await drainTask.WaitAsync(TimeSpan.FromSeconds(5));
            await queue.StopAsync(CancellationToken.None);

            var processedAssets = handler.ProcessedUpdates
                .Select(item => item.Update.AssetId!)
                .ToArray();
            Assert.Equal("blocker", processedAssets[0]);
            Assert.Equal("asset-maker", processedAssets[1]);
            Assert.Equal(["unrelated-1", "unrelated-2"], processedAssets[2..]);
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DrainOutstandingPaperOrderUpdates_PreservesSameAssetFifoBeforeMatchingItem()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler);
        var orderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var acceptedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receivedAtUtc = acceptedAtUtc.AddSeconds(10);
        var expiresAtUtc = acceptedAtUtc.AddMinutes(1);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();
            EnqueueAt(queue, Quote("asset-maker", 0.31m), receivedAtUtc, new HashSet<Guid> { otherOrderId });
            EnqueueAt(queue, Quote("asset-maker", 0.49m), receivedAtUtc.AddTicks(1), new HashSet<Guid> { orderId });
            EnqueueAt(queue, Quote("unrelated", 0.20m), receivedAtUtc, new HashSet<Guid>());

            var drainTask = queue.DrainOutstandingPaperOrderUpdatesAsync(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc);
            handler.ReleaseFirstUpdate();
            await drainTask.WaitAsync(TimeSpan.FromSeconds(5));
            await queue.StopAsync(CancellationToken.None);

            var makerBids = handler.ProcessedUpdates
                .Where(item => item.Update.AssetId == "asset-maker")
                .Select(item => item.Update.OrderBookSnapshot!.BestBid!.Value)
                .ToArray();
            Assert.Equal([0.31m, 0.49m], makerBids);
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DrainOutstandingPaperOrderUpdates_WaitsForMatchingInFlightItem()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler);
        var orderId = Guid.NewGuid();
        var acceptedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receivedAtUtc = acceptedAtUtc.AddSeconds(10);
        var expiresAtUtc = acceptedAtUtc.AddMinutes(1);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            EnqueueAt(queue, Quote("asset-maker", 0.49m), receivedAtUtc, new HashSet<Guid> { orderId });
            await handler.WaitForFirstUpdateAsync();
            var drainTask = queue.DrainOutstandingPaperOrderUpdatesAsync(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc);
            Assert.False(drainTask.IsCompleted);

            handler.ReleaseFirstUpdate();
            await drainTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(queue.HasOutstandingPaperOrderUpdate(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc));
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DrainOutstandingPaperOrderUpdates_CancellationRemovesWaiterAndLaterDrainSucceeds()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler);
        var orderId = Guid.NewGuid();
        var acceptedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receivedAtUtc = acceptedAtUtc.AddSeconds(10);
        var expiresAtUtc = acceptedAtUtc.AddMinutes(1);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();
            EnqueueAt(queue, Quote("asset-maker", 0.49m), receivedAtUtc, new HashSet<Guid> { orderId });
            using var cancellation = new CancellationTokenSource();
            var canceledDrain = queue.DrainOutstandingPaperOrderUpdatesAsync(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc,
                cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledDrain);

            var laterDrain = queue.DrainOutstandingPaperOrderUpdatesAsync(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc);
            handler.ReleaseFirstUpdate();
            await laterDrain.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DrainOutstandingPaperOrderUpdates_HandlerFailurePublishesPoisonBeforeCompletion()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true, throwOnUpdate: true);
        var handoff = new MakerGtdPaperPlacementHandoff();
        var queue = CreateQueue(handler, makerGtdPaperPlacementHandoff: handoff);
        var orderId = Guid.NewGuid();
        handoff.TrackMakerGtdPaperOrder(orderId, MakerGtdPaperExecutionContract.ExecutionSource);
        var acceptedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receivedAtUtc = acceptedAtUtc.AddSeconds(10);
        var expiresAtUtc = acceptedAtUtc.AddMinutes(1);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();
            EnqueueAt(queue, Quote("asset-maker", 0.49m), receivedAtUtc, new HashSet<Guid> { orderId });
            var drainTask = queue.DrainOutstandingPaperOrderUpdatesAsync(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc);

            handler.ReleaseFirstUpdate();
            await drainTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(handoff.TryGetMarketDataFailure(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc,
                out var failure));
            Assert.Equal(
                MakerGtdPaperExecutionContract.MarketDataHandlerFailureCode,
                Assert.IsType<MakerGtdPaperMarketDataFailure>(failure).FailureCode);
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DrainOutstandingPaperOrderUpdates_OneItemCompletesAllMatchingOrderWaitersExactlyOnce()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler);
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var acceptedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receivedAtUtc = acceptedAtUtc.AddSeconds(10);
        var expiresAtUtc = acceptedAtUtc.AddMinutes(1);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();
            EnqueueAt(
                queue,
                Quote("asset-maker", 0.49m),
                receivedAtUtc,
                new HashSet<Guid> { firstOrderId, secondOrderId });
            var firstDrain = queue.DrainOutstandingPaperOrderUpdatesAsync(
                firstOrderId, "asset-maker", "condition-1", acceptedAtUtc, expiresAtUtc);
            var secondDrain = queue.DrainOutstandingPaperOrderUpdatesAsync(
                secondOrderId, "asset-maker", "condition-1", acceptedAtUtc, expiresAtUtc);

            handler.ReleaseFirstUpdate();
            await Task.WhenAll(firstDrain, secondDrain).WaitAsync(TimeSpan.FromSeconds(5));
            await queue.StopAsync(CancellationToken.None);

            Assert.Equal(
                1,
                handler.ProcessedUpdates.Count(item => item.Update.AssetId == "asset-maker"));
        }
        finally
        {
            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DrainOutstandingPaperOrderUpdates_DoesNotWaitForPostExpiryItemAndStillProcessesItNormally()
    {
        var handler = new ControlledHandler(blockFirstUpdate: true);
        var queue = CreateQueue(handler);
        var orderId = Guid.NewGuid();
        var acceptedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var expiresAtUtc = acceptedAtUtc.AddSeconds(30);
        await queue.StartAsync(CancellationToken.None);

        try
        {
            Enqueue(queue, Quote("blocker", 0.10m));
            await handler.WaitForFirstUpdateAsync();
            EnqueueAt(
                queue,
                Quote("asset-maker", 0.49m),
                expiresAtUtc,
                new HashSet<Guid> { orderId });

            await queue.DrainOutstandingPaperOrderUpdatesAsync(
                orderId,
                "asset-maker",
                "condition-1",
                acceptedAtUtc,
                expiresAtUtc);
            Assert.True(queue.GetMetrics().PendingUpdates > 0);

            handler.ReleaseFirstUpdate();
            await queue.StopAsync(CancellationToken.None);
            Assert.Equal(
                1,
                handler.ProcessedUpdates.Count(item => item.Update.AssetId == "asset-maker"));
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
        bool persistMarketDataEvents = false,
        IMakerGtdPaperPlacementHandoff? makerGtdPaperPlacementHandoff = null,
        IPaperTradingMarketDataUpdater? paperTradingMarketDataUpdater = null)
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
            new TestAppRepository(),
            makerGtdPaperPlacementHandoff,
            paperTradingMarketDataUpdater);
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

    private static MarketDataSideEffectEnqueueOutcome EnqueueAt(
        IMarketDataSideEffectQueue queue,
        MarketDataUpdate update,
        DateTimeOffset receivedAtUtc,
        IReadOnlySet<Guid>? eligiblePaperOrderIds = null)
    {
        return queue.EnqueueUpdate(
            "test-component",
            update,
            null,
            receivedAtUtc,
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
        bool blockFirstDiagnostic = false,
        bool throwOnUpdate = false) : IMarketDataSideEffectHandler
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

            if (throwOnUpdate)
            {
                throw new InvalidOperationException("simulated market-data handler failure");
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

    private sealed class ControlledMakerUpdater(
        int expectedMakerUpdates,
        int? failSequence = null,
        bool blockFirstUpdate = false,
        string? expectedEvidencePrefix = null) : IPaperTradingMarketDataUpdater
    {
        private readonly TaskCompletionSource<bool> expectedUpdatesProcessed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> firstUpdateStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> releaseFirstUpdate = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int nextExpectedSequence;
        private int processedMakerUpdates;

        public string? SequenceFailure { get; private set; }

        public string? EvidenceFailure { get; private set; }

        public async Task ApplyMakerGtdUpdateAsync(
            MarketDataUpdate update,
            DateTimeOffset receivedAtUtc,
            IReadOnlySet<Guid> eligibleMakerGtdPaperOrderIds,
            CancellationToken cancellationToken = default,
            MarketDataSideEffectExecutionTrace? executionTrace = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expected = nextExpectedSequence++;
            if (expected == 0 && blockFirstUpdate)
            {
                firstUpdateStarted.TrySetResult(true);
                await releaseFirstUpdate.Task;
            }

            if (!int.TryParse(
                    update.SourceEventId,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var actual) ||
                actual != expected)
            {
                SequenceFailure ??= $"Expected sequence {expected}, received '{update.SourceEventId ?? "<null>"}'.";
            }

            if (expectedEvidencePrefix is not null &&
                (!string.Equals(
                    update.EventFingerprint,
                    $"{expectedEvidencePrefix}-{expected}",
                    StringComparison.Ordinal) ||
                 !string.Equals(
                    update.RawJson,
                    $"{{\"sequence\":{expected}}}",
                    StringComparison.Ordinal)))
            {
                EvidenceFailure ??=
                    $"Expected fingerprint/payload for sequence {expected}, received '{update.EventFingerprint ?? "<null>"}'/'{update.RawJson}'.";
            }

            if (Interlocked.Increment(ref processedMakerUpdates) == expectedMakerUpdates)
            {
                expectedUpdatesProcessed.TrySetResult(true);
            }

            if (actual == failSequence)
            {
                throw new InvalidOperationException("simulated dedicated Maker-GTD update failure");
            }

        }

        public Task ApplyUpdateAsync(
            MarketDataUpdate update,
            DateTimeOffset? receivedAtUtc = null,
            IReadOnlySet<Guid>? eligiblePaperOrderIds = null,
            CancellationToken cancellationToken = default,
            MarketDataSideEffectExecutionTrace? executionTrace = null)
        {
            return Task.CompletedTask;
        }

        public Task WaitForExpectedUpdatesAsync()
        {
            return expectedUpdatesProcessed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        public Task WaitForFirstUpdateAsync()
        {
            return firstUpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void ReleaseFirstUpdate()
        {
            releaseFirstUpdate.TrySetResult(true);
        }
    }
}
