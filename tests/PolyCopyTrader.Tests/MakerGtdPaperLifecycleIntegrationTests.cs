using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class MakerGtdPaperLifecycleIntegrationTests
{
    [Fact]
    public async Task MarketDataUpdater_AuthoritativeLastTradeAtLimit_AppliesAtomicFullMakerFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                price: scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        var fill = Assert.Single(scenario.Repository.PaperFills);
        Assert.Equal(scenario.Order.Price, fill.Price);
        Assert.Equal(scenario.Order.SizeShares, fill.SizeShares);
        Assert.Equal(sourceTimestampUtc, fill.FilledAtUtc);
        Assert.Equal(FeeLiquidityRole.Maker.ToString(), fill.FeeLiquidityRole);
        Assert.Contains("\"source_timestamp_utc\"", fill.Evidence, StringComparison.Ordinal);
        Assert.Contains("\"received_at_utc\"", fill.Evidence, StringComparison.Ordinal);

        var filledOrder = Assert.Single(scenario.Repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, filledOrder.Status);
        Assert.Equal(sourceTimestampUtc, filledOrder.FilledAtUtc);
        var enteredRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, enteredRun.Status);
        Assert.Equal(sourceTimestampUtc, enteredRun.EnteredAtUtc);
        Assert.Equal(FeeLiquidityRole.Maker.ToString(), enteredRun.FeeLiquidityRole);
        var position = Assert.Single(scenario.Repository.PaperPositions);
        Assert.Equal(scenario.Order.SizeShares, position.SizeShares);
        Assert.Equal(scenario.Order.Price, position.AveragePrice);
    }

    [Fact]
    public async Task MarketDataUpdater_AcceptedFiveTicksBeforePersistedCreatedAt_FillsExactlyOnce()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(
            now,
            expiresAtUtc: now.AddMinutes(1),
            acceptedAtOffsetFromCreatedTicks: -5);
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);
        var update = LastTradeUpdate(
            scenario.Order,
            price: scenario.Order.Price,
            sourceTimestampUtc,
            receivedAtUtc);

        await updater.ApplyUpdateAsync(update, receivedAtUtc);
        await updater.ApplyUpdateAsync(update, receivedAtUtc);

        Assert.Single(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Filled, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Entered,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_OnePositionMarkConflict_RecomputesAndFillsExactlyOnce()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var initialPosition = new PaperPosition(
            scenario.Order.AssetId,
            scenario.Order.ConditionId,
            scenario.Order.Outcome,
            SizeShares: 2m,
            AveragePrice: 0.40m,
            EstimatedValueUsd: 0.80m,
            UnrealizedPnlUsd: 0m,
            UpdatedAtUtc: scenario.Order.CreatedAtUtc.AddSeconds(-1),
            CopiedTraderWallet: scenario.Order.CopiedTraderWallet);
        scenario.Repository.PaperPositions.Add(initialPosition);
        var mutationRequests = new List<MakerGtdPaperFullFillRequest>();
        scenario.Repository.BeforeTryApplyMakerGtdPaperFullFill = (request, attempt) =>
        {
            mutationRequests.Add(request);
            if (attempt != 1)
            {
                return;
            }

            var markedPosition = initialPosition with
            {
                EstimatedValueUsd = 1.20m,
                UnrealizedPnlUsd = 0.40m,
                UpdatedAtUtc = now
            };
            scenario.Repository.PaperPositions.Clear();
            scenario.Repository.PaperPositions.Add(markedPosition);
        };
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        Assert.Equal(2, scenario.Repository.MakerGtdPaperFullFillAttempts);
        Assert.Equal(2, mutationRequests.Count);
        Assert.Equal(mutationRequests[0].Fill.Id, mutationRequests[1].Fill.Id);
        Assert.Equal(mutationRequests[0].Fill.Evidence, mutationRequests[1].Fill.Evidence);
        Assert.Equal(mutationRequests[0].FilledOrder, mutationRequests[1].FilledOrder);
        Assert.Equal(mutationRequests[0].EnteredRun, mutationRequests[1].EnteredRun);
        Assert.Single(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Filled, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Entered,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
        var finalPosition = Assert.Single(scenario.Repository.PaperPositions);
        Assert.Equal(initialPosition.SizeShares + scenario.Order.SizeShares, finalPosition.SizeShares);
        Assert.Equal(7.20m, finalPosition.EstimatedValueUsd);
        Assert.Equal(1.40m, finalPosition.UnrealizedPnlUsd, 10);
        Assert.Equal(now, finalPosition.UpdatedAtUtc);
    }

    [Fact]
    public async Task MarketDataUpdater_NonPositionConflictReason_DoesNotRetry()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        scenario.Repository.BeforeTryApplyMakerGtdPaperFullFill = (_, attempt) =>
        {
            if (attempt != 1)
            {
                return;
            }

            scenario.Repository.PaperOrders.Clear();
            scenario.Repository.PaperOrders.Add(scenario.Order with
            {
                Status = PaperOrderStatus.Expired
            });
        };
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        Assert.Equal(1, scenario.Repository.MakerGtdPaperFullFillAttempts);
        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Expired, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Resting,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_AuthoritativeCurrentBestAskAtLimit_AppliesAtomicFullMakerFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);
        var update = new MarketDataUpdate(
            MarketDataEventType.BestBidAsk,
            "best_bid_ask",
            scenario.Order.AssetId,
            scenario.Order.ConditionId,
            OrderBookSnapshot: null,
            BestBid: scenario.Order.Price - 0.01m,
            BestAsk: scenario.Order.Price,
            Price: null,
            Size: null,
            TradeSide.Unknown,
            MarketResolved: false,
            TimestampUtc: sourceTimestampUtc,
            SourceTimestampUtc: sourceTimestampUtc,
            TimestampQuality: MarketDataTimestampQuality.VenueProvided,
            ReceivedAtUtc: receivedAtUtc,
            SourceEventId: "best-ask-event-1",
            EventFingerprint: "best-ask-event-fingerprint-1");

        await updater.ApplyUpdateAsync(update, receivedAtUtc);

        var fill = Assert.Single(scenario.Repository.PaperFills);
        Assert.Equal(scenario.Order.Price, fill.Price);
        Assert.Equal(scenario.Order.SizeShares, fill.SizeShares);
        Assert.Equal(FeeLiquidityRole.Maker.ToString(), fill.FeeLiquidityRole);
        Assert.Contains("BestAsk", fill.Evidence, StringComparison.Ordinal);
        Assert.Equal(PaperOrderStatus.Filled, Assert.Single(scenario.Repository.PaperOrders).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_NonCrossingTradeWithCrossedBook_NeverFallsThroughGenericFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);
        var crossedBook = new OrderBookSnapshot(
            scenario.Order.AssetId,
            [new OrderBookLevel(0.39m, 100m)],
            [new OrderBookLevel(0.40m, 100m)],
            sourceTimestampUtc,
            scenario.Order.ConditionId);
        var update = LastTradeUpdate(
            scenario.Order,
            price: scenario.Order.Price + 0.01m,
            sourceTimestampUtc,
            receivedAtUtc) with
        {
            OrderBookSnapshot = crossedBook,
            BestBid = crossedBook.BestBid,
            BestAsk = crossedBook.BestAsk
        };

        await updater.ApplyUpdateAsync(update, receivedAtUtc);

        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Empty(scenario.Repository.PaperPositions);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Resting,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_ReceiveTimeFallbackWithCrossedBook_FailsClosed()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var updater = CreateUpdater(scenario.Repository);
        var receivedAtUtc = now.AddMilliseconds(-10);
        var update = new MarketDataUpdate(
            MarketDataEventType.BestBidAsk,
            "best_bid_ask",
            scenario.Order.AssetId,
            scenario.Order.ConditionId,
            OrderBookSnapshot: null,
            BestBid: 0.39m,
            BestAsk: scenario.Order.Price,
            Price: null,
            Size: null,
            TradeSide.Unknown,
            MarketResolved: false,
            TimestampUtc: receivedAtUtc,
            SourceTimestampUtc: null,
            TimestampQuality: MarketDataTimestampQuality.ReceiveTimeFallback,
            ReceivedAtUtc: receivedAtUtc,
            SourceEventId: null,
            EventFingerprint: "fallback-event");

        await updater.ApplyUpdateAsync(update, receivedAtUtc);

        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_EventReceivedAtEffectiveExpiry_DoesNotFill()
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAtUtc = now.AddMilliseconds(50);
        var scenario = CreateScenario(now, expiresAtUtc);
        var updater = CreateUpdater(scenario.Repository);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc: now.AddMilliseconds(10),
                receivedAtUtc: expiresAtUtc),
            expiresAtUtc);

        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_SourceAndReceiptBeforeExpiry_StillFillsWhenProcessedAfterExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAtUtc = now.AddSeconds(-1);
        var scenario = CreateScenario(now, expiresAtUtc);
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = expiresAtUtc.AddMilliseconds(-20);
        var receivedAtUtc = expiresAtUtc.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        var fill = Assert.Single(scenario.Repository.PaperFills);
        Assert.Equal(sourceTimestampUtc, fill.FilledAtUtc);
        Assert.Equal(PaperOrderStatus.Filled, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Entered,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
    }

    [Fact]
    public async Task Processor_ContinuousSubscribedWebSocket_ExpiresUnfilledWithoutRestLookup()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var clobClient = new CountingClobClient();
        var processor = CreateProcessor(scenario.Repository, cache, clobClient);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(0, result.OrdersFilled);
        Assert.Equal(1, result.OrdersExpired);
        Assert.Equal(0, clobClient.OrderBookCalls);
        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Expired, Assert.Single(scenario.Repository.PaperOrders).Status);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, skippedRun.Status);
        Assert.Equal(MakerGtdPaperExecutionContract.ExpiredUnfilledReasonCode, skippedRun.SkipReason);
        Assert.Contains("continuous_market_websocket_evidence", skippedRun.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Processor_BlockedPositionMarkRefresh_DoesNotDelayMakerExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var markPosition = new PaperPosition(
            "asset-blocked-mark",
            "condition-blocked-mark",
            "Up",
            2m,
            0.40m,
            0.80m,
            0m,
            now.AddMinutes(-10),
            "strategy:blocked-mark");
        scenario.Repository.PaperPositions.Add(markPosition);
        var exposureCache = new ExposureSnapshotCache(scenario.Repository);
        await exposureCache.RefreshAsync();
        var clobClient = new CountingClobClient(markPosition.AssetId);
        var processor = CreateProcessor(
            scenario.Repository,
            CreateHealthyCache(scenario.Order, reconnectCount: 2),
            clobClient,
            exposureSnapshotCache: exposureCache);
        var markRefresh = processor.RefreshPositionMarksAsync();

        await clobClient.OrderBookRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var result = await processor.ProcessOpenOrdersAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, result.OrdersExpired);
            Assert.Equal(PaperOrderStatus.Expired, Assert.Single(scenario.Repository.PaperOrders).Status);
            var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
            Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, skippedRun.Status);
            Assert.Equal(MakerGtdPaperExecutionContract.ExpiredUnfilledReasonCode, skippedRun.SkipReason);
            Assert.False(markRefresh.IsCompleted);
            Assert.Equal(1, clobClient.OrderBookCalls);
        }
        finally
        {
            clobClient.ReleaseOrderBook();
        }

        Assert.Equal(0, await markRefresh.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Processor_AcceptedFiveTicksBeforePersistedCreatedAt_ExpiresOrdinarily()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(
            now,
            expiresAtUtc: now.AddSeconds(-1),
            acceptedAtOffsetFromCreatedTicks: -5);
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var clobClient = new CountingClobClient();
        var processor = CreateProcessor(scenario.Repository, cache, clobClient);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(0, result.OrdersFilled);
        Assert.Equal(1, result.OrdersExpired);
        Assert.Equal(0, clobClient.OrderBookCalls);
        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Expired, Assert.Single(scenario.Repository.PaperOrders).Status);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(MakerGtdPaperExecutionContract.ExpiredUnfilledReasonCode, skippedRun.SkipReason);
        Assert.Contains("continuous_market_websocket_evidence", skippedRun.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Processor_ReconnectAfterAcceptance_ExpiresAsEvidenceUnavailableWithoutRestLookup()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 3);
        var clobClient = new CountingClobClient();
        var processor = CreateProcessor(scenario.Repository, cache, clobClient);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersExpired);
        Assert.Equal(0, clobClient.OrderBookCalls);
        Assert.Empty(scenario.Repository.PaperFills);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode, skippedRun.SkipReason);
        Assert.Contains("reconnect_count_changed", skippedRun.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Processor_MakerOrderBeforeExpiry_NeverUsesRestOrGenericFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var clobClient = new CountingClobClient();
        var processor = CreateProcessor(scenario.Repository, cache, clobClient);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(0, result.OrdersFilled);
        Assert.Equal(0, result.OrdersExpired);
        Assert.Equal(0, clobClient.OrderBookCalls);
        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
    }

    [Fact]
    public async Task Processor_QueuedPreExpiryMakerUpdate_DefersExpiryUntilQueueDrains()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var clobClient = new CountingClobClient();
        var queue = new OutstandingMarketDataSideEffectQueue
        {
            HasOutstandingUpdate = true
        };
        var processor = CreateProcessor(scenario.Repository, cache, clobClient, queue);

        var deferredResult = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(0, deferredResult.OrdersExpired);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Resting,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
        Assert.Equal(0, clobClient.OrderBookCalls);
        Assert.Equal(1, queue.OutstandingChecks);

        queue.HasOutstandingUpdate = false;
        var expiredResult = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, expiredResult.OrdersExpired);
        Assert.Equal(PaperOrderStatus.Expired, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(0, clobClient.OrderBookCalls);
    }

    [Fact]
    public async Task Processor_ActiveFrameReceipt_DefersExpiryUntilReceiptAdmissionCompletes()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var clobClient = new CountingClobClient();
        var handoff = new MakerGtdPaperPlacementHandoff();
        var processor = CreateProcessor(
            scenario.Repository,
            cache,
            clobClient,
            makerGtdPaperPlacementHandoff: handoff);
        var receiptAdmission = await handoff.EnterMarketDataReceiptAsync();

        try
        {
            var deferredResult = await processor.ProcessOpenOrdersAsync();

            Assert.Equal(0, deferredResult.OrdersExpired);
            Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
            Assert.Equal(
                StrategyMarketPaperRunStatuses.Resting,
                Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
        }
        finally
        {
            await receiptAdmission.DisposeAsync();
        }

        var expiredResult = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, expiredResult.OrdersExpired);
        Assert.Equal(PaperOrderStatus.Expired, Assert.Single(scenario.Repository.PaperOrders).Status);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(MakerGtdPaperExecutionContract.ExpiredUnfilledReasonCode, skippedRun.SkipReason);
        Assert.Equal(0, clobClient.OrderBookCalls);
    }

    [Fact]
    public async Task Processor_MatchingDeliveryFailure_ExpiresAsEvidenceUnavailable()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var handoff = new MakerGtdPaperPlacementHandoff();
        handoff.TrackMakerGtdPaperOrder(
            scenario.Order.Id,
            MakerGtdPaperExecutionContract.ExecutionSource);
        handoff.RecordMarketDataFailure(
            scenario.Order.AssetId,
            scenario.Order.ConditionId,
            scenario.Order.ExpiresAtUtc.AddMilliseconds(-1),
            new HashSet<Guid> { scenario.Order.Id },
            MakerGtdPaperExecutionContract.MarketDataHandlerFailureCode);
        var processor = CreateProcessor(
            scenario.Repository,
            cache,
            new CountingClobClient(),
            makerGtdPaperPlacementHandoff: handoff);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersExpired);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode, skippedRun.SkipReason);
        Assert.Contains(
            MakerGtdPaperExecutionContract.MarketDataHandlerFailureCode,
            skippedRun.SkipDiagnosticsJson,
            StringComparison.Ordinal);
        Assert.False(handoff.TryGetMarketDataFailure(
            scenario.Order.Id,
            scenario.Order.AssetId,
            scenario.Order.ConditionId,
            scenario.Order.CreatedAtUtc,
            scenario.Order.ExpiresAtUtc,
            out _));
    }

    [Fact]
    public async Task MarketDataUpdater_FinalPositionConflict_PoisonsLaterExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var eventTimeUtc = now.AddMinutes(-1);
        var scenario = CreateScenario(eventTimeUtc, expiresAtUtc: now.AddSeconds(-1));
        scenario.Repository.BeforeTryApplyMakerGtdPaperFullFill = (request, attempt) =>
        {
            var conflictingPosition = new PaperPosition(
                scenario.Order.AssetId,
                scenario.Order.ConditionId,
                scenario.Order.Outcome,
                SizeShares: attempt,
                AveragePrice: 0.40m,
                EstimatedValueUsd: attempt * 0.49m,
                UnrealizedPnlUsd: attempt * 0.09m,
                UpdatedAtUtc: request.Position.UpdatedAtUtc.AddTicks(attempt),
                CopiedTraderWallet: scenario.Order.CopiedTraderWallet);
            scenario.Repository.PaperPositions.Clear();
            scenario.Repository.PaperPositions.Add(conflictingPosition);
        };
        var handoff = new MakerGtdPaperPlacementHandoff();
        var updater = CreateUpdater(scenario.Repository, handoff);
        var sourceTimestampUtc = eventTimeUtc.AddMilliseconds(-20);
        var receivedAtUtc = eventTimeUtc.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        Assert.Equal(3, scenario.Repository.MakerGtdPaperFullFillAttempts);
        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);

        var processor = CreateProcessor(
            scenario.Repository,
            CreateHealthyCache(scenario.Order, reconnectCount: 2),
            new CountingClobClient(),
            makerGtdPaperPlacementHandoff: handoff);
        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersExpired);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode, skippedRun.SkipReason);
        Assert.Contains(
            MakerGtdPaperExecutionContract.MarketDataApplyFailureCode,
            skippedRun.SkipDiagnosticsJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarketDataUpdater_LoadExposureFailure_PoisonsEligibleMakerExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var eventTimeUtc = now.AddMinutes(-1);
        var scenario = CreateScenario(eventTimeUtc, expiresAtUtc: now.AddSeconds(-1));
        var handoff = new MakerGtdPaperPlacementHandoff();
        handoff.TrackMakerGtdPaperOrder(
            scenario.Order.Id,
            MakerGtdPaperExecutionContract.ExecutionSource);
        var updater = CreateUpdater(
            scenario.Repository,
            handoff,
            new ThrowingExposureSnapshotCache());
        var sourceTimestampUtc = eventTimeUtc.AddMilliseconds(-20);
        var receivedAtUtc = eventTimeUtc.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid> { scenario.Order.Id });

        Assert.Equal(0, scenario.Repository.MakerGtdPaperFullFillAttempts);
        Assert.Empty(scenario.Repository.PaperFills);
        var processor = CreateProcessor(
            scenario.Repository,
            CreateHealthyCache(scenario.Order, reconnectCount: 2),
            new CountingClobClient(),
            makerGtdPaperPlacementHandoff: handoff);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersExpired);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode, skippedRun.SkipReason);
        Assert.Contains(
            MakerGtdPaperExecutionContract.MarketDataApplyFailureCode,
            skippedRun.SkipDiagnosticsJson,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing_status")]
    [InlineData("accepted_not_subscribed")]
    [InlineData("accepted_stale")]
    [InlineData("current_not_subscribed")]
    [InlineData("current_last_connected_after_order")]
    [InlineData("current_disconnect_after_order")]
    public void ContinuityEvaluator_MissingOrContradictoryEvidence_FailsClosed(string scenarioName)
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var order = scenario.Order;
        var cache = CreateHealthyCache(order, reconnectCount: 2);
        IReadOnlyCollection<string> subscribedAssetIds = cache.SubscribedAssetIds;
        var currentStatus = cache.Status;

        switch (scenarioName)
        {
            case "missing_status":
                order = order with { RawDecisionJson = "{\"maker_gtd\":{}}" };
                break;
            case "accepted_not_subscribed":
                order = order with
                {
                    RawDecisionJson = BuildRawDecisionJson(
                        order,
                        acceptedAtUtc: order.CreatedAtUtc.AddSeconds(1),
                        assetSubscribed: false)
                };
                break;
            case "accepted_stale":
                order = order with
                {
                    RawDecisionJson = BuildRawDecisionJson(
                        order,
                        acceptedAtUtc: order.CreatedAtUtc.AddSeconds(1),
                        acceptedStale: true)
                };
                break;
            case "current_not_subscribed":
                subscribedAssetIds = [];
                break;
            case "current_last_connected_after_order":
                currentStatus = currentStatus with
                {
                    LastConnectedUtc = order.CreatedAtUtc.AddTicks(1)
                };
                break;
            case "current_disconnect_after_order":
                currentStatus = currentStatus with
                {
                    LastDisconnectedUtc = order.CreatedAtUtc.AddTicks(1)
                };
                break;
        }

        var result = MakerGtdPaperContinuityEvaluator.Evaluate(
            order,
            currentStatus,
            subscribedAssetIds);

        Assert.False(result.Continuous);
        Assert.Equal(MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode, result.ReasonCode);
    }

    [Theory]
    [InlineData(-5L)]
    [InlineData(-4L)]
    [InlineData(-3L)]
    [InlineData(-2L)]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1L)]
    public void ContinuityEvaluator_AcceptedWithinLowerBoundOrLaterBeforeExpiry_RemainsContinuous(
        long acceptedAtOffsetFromCreatedTicks)
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(
            now,
            expiresAtUtc: now.AddMinutes(1),
            acceptedAtOffsetFromCreatedTicks: acceptedAtOffsetFromCreatedTicks);
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);

        var result = MakerGtdPaperContinuityEvaluator.Evaluate(
            scenario.Order,
            cache.Status,
            cache.SubscribedAssetIds);

        Assert.True(result.Continuous);
        Assert.Equal(MakerGtdPaperExecutionContract.ExpiredUnfilledReasonCode, result.ReasonCode);
        Assert.Equal("continuous_market_websocket_evidence", result.Detail);
    }

    [Fact]
    public void ContinuityEvaluator_AcceptedSixTicksBeforePersistedCreatedAt_FailsClosed()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(
            now,
            expiresAtUtc: now.AddMinutes(1),
            acceptedAtOffsetFromCreatedTicks: -6);
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);

        var result = MakerGtdPaperContinuityEvaluator.Evaluate(
            scenario.Order,
            cache.Status,
            cache.SubscribedAssetIds);

        Assert.False(result.Continuous);
        Assert.Equal(MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode, result.ReasonCode);
        Assert.Equal("order_lifetime_mismatch", result.Detail);
    }

    private static MakerScenario CreateScenario(
        DateTimeOffset now,
        DateTimeOffset expiresAtUtc,
        long? acceptedAtOffsetFromCreatedTicks = null)
    {
        var repository = new TestAppRepository();
        var createdAtUtc = now.AddMinutes(-2);
        var acceptedAtUtc = acceptedAtOffsetFromCreatedTicks is { } offsetTicks
            ? createdAtUtc.AddTicks(offsetTicks)
            : createdAtUtc.AddSeconds(1);
        var strategyId = Guid.NewGuid();
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "strategy:maker-gtd",
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-maker-gtd",
            "condition-maker-gtd",
            "Up",
            0.50m,
            10m,
            5m,
            createdAtUtc,
            expiresAtUtc,
            StrategyId: strategyId,
            ExecutionSource: MakerGtdPaperExecutionContract.ExecutionSource);
        order = order with
        {
            RawDecisionJson = BuildRawDecisionJson(order, acceptedAtUtc)
        };
        var run = new StrategyMarketPaperRun(
            Guid.NewGuid(),
            strategyId,
            "market-maker-gtd",
            order.ConditionId,
            "market-maker-gtd",
            "Maker GTD market",
            "Crypto",
            MarketStartUtc: createdAtUtc.AddMinutes(1),
            MarketEndUtc: expiresAtUtc.AddMinutes(1),
            DetectedAtUtc: createdAtUtc.AddSeconds(-1),
            EntryDueAtUtc: createdAtUtc,
            Status: StrategyMarketPaperRunStatuses.Resting,
            SelectedAssetId: order.AssetId,
            SelectedOutcome: order.Outcome,
            EntryPrice: order.Price,
            StakeUsd: order.NotionalUsd,
            SizeShares: order.SizeShares,
            SignalId: order.SignalId,
            PaperOrderId: order.Id,
            EnteredAtUtc: null,
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            CreatedAtUtc: createdAtUtc,
            UpdatedAtUtc: acceptedAtUtc);
        repository.PaperOrders.Add(order);
        repository.StrategyMarketPaperRuns.Add(run);
        return new MakerScenario(repository, order, run);
    }

    private static string BuildRawDecisionJson(
        PaperOrder order,
        DateTimeOffset acceptedAtUtc,
        bool assetSubscribed = true,
        bool acceptedStale = false)
    {
        return JsonSerializer.Serialize(new
        {
            maker_gtd = new
            {
                accepted_at_utc = acceptedAtUtc,
                effective_expires_at_utc = order.ExpiresAtUtc,
                attempts = Array.Empty<object>()
            },
            market_data_status_at_acceptance = new
            {
                connection_state = MarketDataConnectionState.Connected.ToString(),
                stale = acceptedStale,
                reconnect_count = 2,
                last_connected_utc = (DateTimeOffset?)order.CreatedAtUtc.AddMinutes(-1),
                last_disconnected_utc = (DateTimeOffset?)null,
                asset_subscribed = assetSubscribed,
                subscribed_assets_count = 1,
                accepted_at_utc = acceptedAtUtc
            }
        });
    }

    private static PaperTradingMarketDataUpdater CreateUpdater(
        TestAppRepository repository,
        IMakerGtdPaperPlacementHandoff? makerGtdPaperPlacementHandoff = null,
        IExposureSnapshotCache? exposureSnapshotCache = null)
    {
        return new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            exposureSnapshotCache ?? new ExposureSnapshotCache(repository, makerGtdPaperPlacementHandoff),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository,
            feeAccountingService: null,
            marketDataWebSocketOptions: new MarketDataWebSocketOptions { StaleAfterSeconds = 30 },
            makerGtdPaperPlacementHandoff: makerGtdPaperPlacementHandoff);
    }

    private static PaperTradingProcessor CreateProcessor(
        TestAppRepository repository,
        IMarketDataCache marketDataCache,
        CountingClobClient clobClient,
        IMarketDataSideEffectQueue? marketDataSideEffectQueue = null,
        IMakerGtdPaperPlacementHandoff? makerGtdPaperPlacementHandoff = null,
        IExposureSnapshotCache? exposureSnapshotCache = null)
    {
        var options = new MarketDataWebSocketOptions { StaleAfterSeconds = 30 };
        return new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            clobClient,
            marketDataCache,
            options,
            new PaperTradingOptions(),
            exposureSnapshotCache ?? new ExposureSnapshotCache(repository, makerGtdPaperPlacementHandoff),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository,
            feeAccountingService: null,
            marketDataSideEffectQueue: marketDataSideEffectQueue,
            makerGtdPaperPlacementHandoff: makerGtdPaperPlacementHandoff);
    }

    private static MarketDataCache CreateHealthyCache(PaperOrder order, int reconnectCount)
    {
        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        cache.ReplaceSubscribedAssets([order.AssetId]);
        cache.UpdateStatus(new MarketDataStatusSnapshot(
            "PolymarketMarketWebSocket",
            MarketDataConnectionState.Connected,
            "wss://example.test",
            SubscribedAssetsCount: 1,
            LastMessageUtc: order.ExpiresAtUtc,
            LastConnectedUtc: order.CreatedAtUtc.AddMinutes(-1),
            LastDisconnectedUtc: null,
            reconnectCount,
            Stale: false,
            LastError: null,
            UpdatedAtUtc: order.ExpiresAtUtc));
        return cache;
    }

    private static MarketDataUpdate LastTradeUpdate(
        PaperOrder order,
        decimal price,
        DateTimeOffset sourceTimestampUtc,
        DateTimeOffset receivedAtUtc)
    {
        return new MarketDataUpdate(
            MarketDataEventType.LastTradePrice,
            "last_trade_price",
            order.AssetId,
            order.ConditionId,
            OrderBookSnapshot: null,
            BestBid: 0.49m,
            BestAsk: 0.51m,
            Price: price,
            Size: 0m,
            TradeSide.Unknown,
            MarketResolved: false,
            TimestampUtc: sourceTimestampUtc,
            SourceTimestampUtc: sourceTimestampUtc,
            TimestampQuality: MarketDataTimestampQuality.VenueProvided,
            ReceivedAtUtc: receivedAtUtc,
            SourceEventId: "trade-event-1",
            EventFingerprint: "trade-event-fingerprint-1");
    }

    private sealed record MakerScenario(
        TestAppRepository Repository,
        PaperOrder Order,
        StrategyMarketPaperRun Run);

    private sealed class ThrowingExposureSnapshotCache : IExposureSnapshotCache
    {
        public Task<TradingExposureSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("simulated exposure snapshot failure");
        }

        public PaperPosition? GetPaperPosition(string copiedTraderWallet, string assetId) => null;

        public bool TryGetOpenPaperOrderIds(string assetId, out IReadOnlySet<Guid> orderIds)
        {
            orderIds = new HashSet<Guid>();
            return false;
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void ApplyPaperOrder(PaperOrder order)
        {
        }

        public void ApplyPaperOrders(IReadOnlyCollection<PaperOrder> orders)
        {
        }

        public void ApplyPaperPosition(PaperPosition position)
        {
        }

        public void ApplyPaperPositions(IReadOnlyCollection<PaperPosition> positions)
        {
        }

        public void ApplyLiveOrder(LiveOrder order)
        {
        }
    }

    private sealed class CountingClobClient(string? blockedAssetId = null) : IPolymarketClobPublicClient
    {
        private readonly TaskCompletionSource releaseOrderBook =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource OrderBookRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int OrderBookCalls { get; private set; }

        public async Task<OrderBookSnapshot?> GetOrderBookAsync(
            string assetId,
            CancellationToken cancellationToken = default)
        {
            OrderBookCalls++;
            if (string.Equals(assetId, blockedAssetId, StringComparison.Ordinal))
            {
                OrderBookRequested.TrySetResult();
                await releaseOrderBook.Task.WaitAsync(cancellationToken);
                return null;
            }

            throw new InvalidOperationException("Maker-GTD lifecycle must not fetch a REST order book.");
        }

        public void ReleaseOrderBook()
        {
            releaseOrderBook.TrySetResult();
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task<decimal?> GetMidpointAsync(
            string assetId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<decimal?> GetSpreadAsync(
            string assetId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(
            string tokenId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PolymarketClobMarketByToken?>(null);
        }
    }

    private sealed class OutstandingMarketDataSideEffectQueue : IMarketDataSideEffectQueue
    {
        public bool HasOutstandingUpdate { get; set; }

        public int OutstandingChecks { get; private set; }

        public MarketDataSideEffectEnqueueOutcome EnqueueUpdate(
            string component,
            MarketDataUpdate update,
            ActiveMarketAssetSnapshot? activeMarketSnapshot,
            DateTimeOffset receivedAtUtc,
            IReadOnlySet<Guid>? eligiblePaperOrderIds)
        {
            return MarketDataSideEffectEnqueueOutcome.Rejected;
        }

        public MarketDataSideEffectEnqueueOutcome EnqueueFrameDiagnostic(
            MarketWebSocketFrameDiagnostic diagnostic,
            bool important)
        {
            return MarketDataSideEffectEnqueueOutcome.Rejected;
        }

        public MarketDataSideEffectEnqueueOutcome EnqueueApiError(ApiError apiError)
        {
            return MarketDataSideEffectEnqueueOutcome.Rejected;
        }

        public MarketDataSideEffectQueueMetrics GetMetrics()
        {
            return new MarketDataSideEffectQueueMetrics(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        public bool HasOutstandingPaperOrderUpdate(
            Guid paperOrderId,
            string assetId,
            string conditionId,
            DateTimeOffset acceptedAfterUtc,
            DateTimeOffset expiresBeforeUtc)
        {
            OutstandingChecks++;
            return HasOutstandingUpdate;
        }
    }

    private sealed class NoOpPaperSettlementProcessor : IPaperSettlementProcessor
    {
        public Task<PaperSettlementProcessingResult> ProcessOpenPositionsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaperSettlementProcessingResult(0, 0, 0, 0));
        }

        public Task<PaperSettlementProcessingResult> SettleMarketResolutionAsync(
            string? conditionId,
            string? assetId,
            string? winningAssetId,
            string? winningOutcome,
            string? category,
            string settlementSource,
            DateTimeOffset settledAtUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaperSettlementProcessingResult(0, 0, 0, 0));
        }
    }
}
