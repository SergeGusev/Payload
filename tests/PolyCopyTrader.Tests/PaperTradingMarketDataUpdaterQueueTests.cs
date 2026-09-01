using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class PaperTradingMarketDataUpdaterQueueTests
{
    [Fact]
    public async Task ApplyUpdateAsync_AttributesSlowOrdinaryFeeAccountingToExactOperation()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0xleader",
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Yes",
            0.50m,
            10m,
            5m,
            now.AddMinutes(-1),
            now.AddMinutes(1));
        repository.PaperOrders.Add(order);
        var feeService = new BlockingFeeAccountingService();
        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository,
            feeAccountingService: feeService);
        var trace = CreateTrace(now);
        trace.MarkProcessingStarted(now);

        var applyTask = updater.ApplyUpdateAsync(
            BookUpdate(now),
            now,
            new HashSet<Guid> { order.Id },
            CancellationToken.None,
            trace);
        await feeService.WaitForPaperFillAsync();
        await Task.Delay(25);

        var activeSnapshot = trace.Capture(DateTimeOffset.UtcNow);
        Assert.Equal(MarketDataSideEffectPhases.ApplyOrdinaryPaperUpdate, activeSnapshot.Phase);
        Assert.Equal("IPolymarketFeeAccountingService.ApplyToPaperFill", activeSnapshot.Operation);

        feeService.ReleasePaperFill();
        await applyTask.WaitAsync(TimeSpan.FromSeconds(5));
        trace.MarkProcessingCompleted(DateTimeOffset.UtcNow);
        var completedSnapshot = trace.Capture(DateTimeOffset.UtcNow);
        Assert.Equal("IPolymarketFeeAccountingService.ApplyToPaperFill", completedSnapshot.SlowestOperation);
        Assert.True(completedSnapshot.SlowestPhaseDurationMilliseconds >= 10d);
    }

    [Fact]
    public async Task ApplyUpdateAsync_UsesReceiptOrderIdsAndReceiptTimeForDeferredFill()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var receivedAtUtc = now.AddSeconds(-2);
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0xleader",
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Yes",
            0.50m,
            10m,
            5m,
            now.AddMinutes(-1),
            now.AddSeconds(-1));
        repository.PaperOrders.Add(order);
        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);
        var update = BookUpdate(receivedAtUtc);

        await updater.ApplyUpdateAsync(
            update,
            receivedAtUtc,
            new HashSet<Guid>(),
            CancellationToken.None);

        Assert.Empty(repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(repository.PaperOrders).Status);

        await updater.ApplyUpdateAsync(
            update,
            receivedAtUtc,
            new HashSet<Guid> { order.Id },
            CancellationToken.None);

        Assert.Single(repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Filled, Assert.Single(repository.PaperOrders).Status);
    }

    [Fact]
    public async Task ApplyUpdateAsync_SkipsPaperLiveShadowOrderEvenWhenExecutable()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0xleader",
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Yes",
            0.99m,
            10m,
            9.90m,
            now.AddMinutes(-1),
            now.AddMinutes(1),
            ExecutionSource: "PAPER_LIVE_SHADOW_TEST");
        repository.PaperOrders.Add(order);
        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        await updater.ApplyUpdateAsync(
            BookUpdate(now),
            now,
            new HashSet<Guid> { order.Id },
            CancellationToken.None);

        Assert.Empty(repository.PaperFills);
        Assert.Empty(repository.PaperPositions);
        Assert.Equal(order, Assert.Single(repository.PaperOrders));
    }

    [Fact]
    public async Task ApplyUpdateAsync_RecordsAssetEventAndExactFailurePhase()
    {
        var repository = new TestAppRepository();
        var receivedAtUtc = DateTimeOffset.UtcNow;
        repository.PaperPositions.Add(new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            10m,
            0.50m,
            5m,
            0m,
            receivedAtUtc.AddMinutes(-1),
            "0xleader"));
        var exposureCache = new ExposureSnapshotCache(repository);
        await exposureCache.RefreshAsync();
        repository.ThrowOnUpsertPaperPosition = true;
        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            exposureCache,
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var trace = CreateTrace(receivedAtUtc);
        await updater.ApplyUpdateAsync(
            BookUpdate(receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid>(),
            CancellationToken.None,
            trace);

        var apiError = Assert.Single(repository.ApiErrors);
        Assert.Equal("PaperTradingMarketDataUpdater", apiError.Component);
        Assert.Equal("ApplyUpdate/UpdatePositionMarks", apiError.Operation);
        Assert.Contains("AssetId=asset-1", apiError.Message, StringComparison.Ordinal);
        Assert.Contains("EventType=Book", apiError.Message, StringComparison.Ordinal);
        Assert.Contains("Operation=IAppRepository.TryUpdatePaperPositionMarks", apiError.Message, StringComparison.Ordinal);
        Assert.Contains("simulated paper position mark update failure", apiError.Message, StringComparison.Ordinal);
        var traceSnapshot = trace.Capture(DateTimeOffset.UtcNow);
        Assert.Equal(MarketDataSideEffectPhases.UpdatePositionMarks, traceSnapshot.Phase);
        Assert.Equal("IAppRepository.TryUpdatePaperPositionMarks", traceSnapshot.Operation);
    }

    [Fact]
    public async Task ApplyUpdateAsync_ReportsPublicationWaitWithoutChangingTheWait()
    {
        var repository = new TestAppRepository();
        var handoff = new MakerGtdPaperPlacementHandoff();
        var orderId = Guid.NewGuid();
        await using (var admission = await handoff.EnterPlacementAdmissionAsync("asset-1"))
        {
            admission.ActivatePendingOrder(
                orderId,
                MakerGtdPaperExecutionContract.ExecutionSource);
        }

        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository,
            makerGtdPaperPlacementHandoff: handoff);
        var receivedAtUtc = DateTimeOffset.UtcNow;
        var trace = CreateTrace(receivedAtUtc);

        var applyTask = updater.ApplyUpdateAsync(
            BookUpdate(receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid> { orderId },
            CancellationToken.None,
            trace);
        await WaitForPhaseAsync(trace, MarketDataSideEffectPhases.WaitForPublication);

        Assert.False(applyTask.IsCompleted);
        handoff.MarkPublished(orderId);
        await applyTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            MarketDataSideEffectPhases.UpdatePositionMarks,
            trace.Capture(DateTimeOffset.UtcNow).Phase);
    }

    [Fact]
    public async Task ApplyMakerGtdUpdateAsync_WaitsForPublicationBeforeDedicatedProcessing()
    {
        var repository = new TestAppRepository();
        var handoff = new MakerGtdPaperPlacementHandoff();
        var orderId = Guid.NewGuid();
        await using (var admission = await handoff.EnterPlacementAdmissionAsync("asset-1"))
        {
            admission.ActivatePendingOrder(
                orderId,
                MakerGtdPaperExecutionContract.ExecutionSource);
        }

        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository,
            makerGtdPaperPlacementHandoff: handoff);
        var receivedAtUtc = DateTimeOffset.UtcNow;
        var trace = CreateTrace(receivedAtUtc);

        var applyTask = updater.ApplyMakerGtdUpdateAsync(
            BookUpdate(receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid> { orderId },
            CancellationToken.None,
            trace);
        await WaitForPhaseAsync(trace, MarketDataSideEffectPhases.WaitForPublication);

        Assert.False(applyTask.IsCompleted);
        handoff.MarkPublished(orderId);
        await applyTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(applyTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ApplyUpdateAsync_ReportsSerializationLockWaitWithoutChangingSerialization()
    {
        var repository = new TestAppRepository();
        var receivedAtUtc = DateTimeOffset.UtcNow;
        repository.PaperPositions.Add(new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            10m,
            0.50m,
            5m,
            0m,
            receivedAtUtc.AddMinutes(-1),
            "0xleader"));
        var firstMarkStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstMark = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var markCalls = 0;
        repository.BeforeTryUpdatePaperPositionMarksAsync = async () =>
        {
            if (Interlocked.Increment(ref markCalls) == 1)
            {
                firstMarkStarted.TrySetResult(true);
                await releaseFirstMark.Task;
            }
        };
        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);
        var firstTrace = CreateTrace(receivedAtUtc);
        var secondTrace = CreateTrace(receivedAtUtc.AddMilliseconds(1));

        var firstApply = updater.ApplyUpdateAsync(
            BookUpdate(receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid>(),
            CancellationToken.None,
            firstTrace);
        await firstMarkStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondApply = updater.ApplyUpdateAsync(
            BookUpdate(receivedAtUtc.AddMilliseconds(1), 0.39m),
            receivedAtUtc.AddMilliseconds(1),
            new HashSet<Guid>(),
            CancellationToken.None,
            secondTrace);
        await WaitForPhaseAsync(secondTrace, MarketDataSideEffectPhases.WaitForSerializationLock);

        Assert.False(secondApply.IsCompleted);
        releaseFirstMark.TrySetResult(true);
        await Task.WhenAll(firstApply, secondApply).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, markCalls);
        Assert.Equal(
            MarketDataSideEffectPhases.ApplyPositionMarkExposureCache,
            secondTrace.Capture(DateTimeOffset.UtcNow).Phase);
    }

    [Fact]
    public async Task ApplyMakerGtdUpdateAsync_DoesNotWaitForBlockedGeneralPositionMarkWork()
    {
        var repository = new TestAppRepository();
        var receivedAtUtc = DateTimeOffset.UtcNow;
        repository.PaperPositions.Add(new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            10m,
            0.50m,
            5m,
            0m,
            receivedAtUtc.AddMinutes(-1),
            "0xleader"));
        var generalMarkStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGeneralMark = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.BeforeTryUpdatePaperPositionMarksAsync = async () =>
        {
            generalMarkStarted.TrySetResult(true);
            await releaseGeneralMark.Task;
        };
        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var generalApply = updater.ApplyUpdateAsync(
            BookUpdate(receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid>(),
            CancellationToken.None);
        await generalMarkStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            await updater.ApplyMakerGtdUpdateAsync(
                BookUpdate(receivedAtUtc.AddMilliseconds(1)),
                receivedAtUtc.AddMilliseconds(1),
                new HashSet<Guid> { Guid.NewGuid() },
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(generalApply.IsCompleted);
            Assert.Empty(repository.PaperFills);
        }
        finally
        {
            releaseGeneralMark.TrySetResult(true);
            await generalApply.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ApplyUpdateAsync_ReportsMarketResolutionSettlementPhase()
    {
        var repository = new TestAppRepository();
        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);
        var receivedAtUtc = DateTimeOffset.UtcNow;
        var trace = CreateTrace(receivedAtUtc);
        var update = BookUpdate(receivedAtUtc) with { MarketResolved = true };

        await updater.ApplyUpdateAsync(
            update,
            receivedAtUtc,
            new HashSet<Guid>(),
            CancellationToken.None,
            trace);

        Assert.Equal(
            MarketDataSideEffectPhases.SettleMarketResolution,
            trace.Capture(DateTimeOffset.UtcNow).Phase);
    }

    [Fact]
    public void DiagnosticPhaseNames_AreStable()
    {
        Assert.Equal("Queued", MarketDataSideEffectPhases.Queued);
        Assert.Equal("Processing", MarketDataSideEffectPhases.Processing);
        Assert.Equal("RecordResolvedEvent", MarketDataSideEffectPhases.RecordResolvedEvent);
        Assert.Equal("RecordTradeTick", MarketDataSideEffectPhases.RecordTradeTick);
        Assert.Equal("PersistOrderBookSnapshot", MarketDataSideEffectPhases.PersistOrderBookSnapshot);
        Assert.Equal("PersistMarketDataEvent", MarketDataSideEffectPhases.PersistMarketDataEvent);
        Assert.Equal("ApplyPaperTradingUpdate", MarketDataSideEffectPhases.ApplyPaperTradingUpdate);
        Assert.Equal("ApplyPaperTradingUpdate/WaitForPublication", MarketDataSideEffectPhases.WaitForPublication);
        Assert.Equal("ApplyPaperTradingUpdate/WaitForSerializationLock", MarketDataSideEffectPhases.WaitForSerializationLock);
        Assert.Equal("ApplyPaperTradingUpdate/LoadExposureSnapshot", MarketDataSideEffectPhases.LoadExposureSnapshot);
        Assert.Equal("ApplyPaperTradingUpdate/SettleMarketResolution", MarketDataSideEffectPhases.SettleMarketResolution);
        Assert.Equal("ApplyPaperTradingUpdate/ApplyMakerGtdPaperUpdate", MarketDataSideEffectPhases.ApplyMakerGtdPaperUpdate);
        Assert.Equal("ApplyPaperTradingUpdate/ApplyOrdinaryPaperUpdate", MarketDataSideEffectPhases.ApplyOrdinaryPaperUpdate);
        Assert.Equal("ApplyPaperTradingUpdate/UpdatePositionMarks", MarketDataSideEffectPhases.UpdatePositionMarks);
        Assert.Equal(
            "ApplyPaperTradingUpdate/UpdatePositionMarks/ApplyExposureCache",
            MarketDataSideEffectPhases.ApplyPositionMarkExposureCache);
        Assert.Equal(
            "ApplyPaperTradingUpdate/UpdatePositionMarks/ExecuteCommand",
            MarketDataSideEffectPhases.PositionMarkPersistenceStage(
                PaperPositionMarkPersistenceStages.ExecuteCommand));
        Assert.Equal(
            "IAppRepository.TryUpdatePaperPositionMarks/ExecuteCommand",
            MarketDataSideEffectPhases.PositionMarkPersistenceOperation(
                PaperPositionMarkPersistenceStages.ExecuteCommand));
    }

    [Fact]
    public async Task ApplyUpdateAsync_BatchesPositionMarkPersistence()
    {
        var repository = new TestAppRepository();
        var receivedAtUtc = DateTimeOffset.UtcNow;
        repository.PaperPositions.AddRange(
        [
            new PaperPosition(
                "asset-1",
                "condition-1",
                "Yes",
                10m,
                0.50m,
                5m,
                0m,
                receivedAtUtc.AddMinutes(-1),
                "strategy:one"),
            new PaperPosition(
                "asset-1",
                "condition-1",
                "Yes",
                5m,
                0.30m,
                1.5m,
                0m,
                receivedAtUtc.AddMinutes(-1),
                "strategy:two")
        ]);
        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        await updater.ApplyUpdateAsync(
            BookUpdate(receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid>(),
            CancellationToken.None);

        Assert.Equal(1, repository.TryUpdatePaperPositionMarksBatchCalls);
        Assert.Equal(0, repository.UpsertPaperPositionsBatchCalls);
        Assert.Collection(
            repository.PaperPositions.OrderBy(position => position.CopiedTraderWallet),
            position =>
            {
                Assert.Equal(4m, position.EstimatedValueUsd);
                Assert.Equal(-1m, position.UnrealizedPnlUsd);
            },
            position =>
            {
                Assert.Equal(2m, position.EstimatedValueUsd);
                Assert.Equal(0.5m, position.UnrealizedPnlUsd);
            });
    }

    [Fact]
    public async Task ApplyUpdateAsync_SuppressedPositionMarksStillProcessesEligibleOrderEvidence()
    {
        var repository = new TestAppRepository();
        var receivedAtUtc = DateTimeOffset.UtcNow;
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0xentry",
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Yes",
            0.50m,
            10m,
            5m,
            receivedAtUtc.AddMinutes(-1),
            receivedAtUtc.AddMinutes(1));
        var unchangedMarkPosition = new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            10m,
            0.50m,
            5m,
            0m,
            receivedAtUtc.AddMinutes(-1),
            "0xmark");
        repository.PaperOrders.Add(order);
        repository.PaperPositions.Add(unchangedMarkPosition);
        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        await updater.ApplyUpdateAsync(
            BookUpdate(receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid> { order.Id },
            CancellationToken.None,
            executionTrace: null,
            persistPositionMarks: false);

        Assert.Single(repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Filled, Assert.Single(repository.PaperOrders).Status);
        Assert.Equal(0, repository.TryUpdatePaperPositionMarksBatchCalls);
        Assert.Contains(unchangedMarkPosition, repository.PaperPositions);
        Assert.Contains(
            repository.PaperPositions,
            position => position.CopiedTraderWallet == order.CopiedTraderWallet &&
                position.AssetId == order.AssetId &&
                position.SizeShares == order.SizeShares);
    }

    [Fact]
    public async Task ApplyUpdateAsync_DoesNotRestorePositionSettledBeforeConditionalMarkBatchWrites()
    {
        var repository = new TestAppRepository();
        var receivedAtUtc = new DateTimeOffset(2026, 7, 15, 7, 15, 0, TimeSpan.Zero);
        var position = new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            10m,
            0.50m,
            5m,
            0m,
            receivedAtUtc.AddMinutes(-1),
            "0xleader");
        repository.PaperPositions.Add(position);
        var exposureCache = new ExposureSnapshotCache(repository);
        await exposureCache.RefreshAsync();
        var markUpdateStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowMarkUpdate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.BeforeTryUpdatePaperPositionMarksAsync = async () =>
        {
            markUpdateStarted.TrySetResult(true);
            await allowMarkUpdate.Task;
        };
        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            exposureCache,
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var applyingUpdate = updater.ApplyUpdateAsync(
            BookUpdate(receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid>(),
            CancellationToken.None);
        await markUpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var settledAtUtc = receivedAtUtc.AddSeconds(1);
        var settledPosition = position with
        {
            SizeShares = 0m,
            AveragePrice = 0m,
            EstimatedValueUsd = 0m,
            UnrealizedPnlUsd = 0m,
            UpdatedAtUtc = settledAtUtc
        };
        var settlement = new PaperPositionSettlement(
            Guid.NewGuid(),
            position.CopiedTraderWallet,
            position.AssetId,
            position.ConditionId,
            position.Outcome,
            position.AssetId,
            position.Outcome,
            "Crypto",
            position.SizeShares,
            position.AveragePrice,
            position.SizeShares * position.AveragePrice,
            position.SizeShares,
            position.SizeShares - position.SizeShares * position.AveragePrice,
            true,
            "TestResolution",
            settledAtUtc,
            settledAtUtc);
        await repository.PersistPaperPositionSettlementBatchAsync(
            [new PaperPositionSettlementWrite(settlement, settledPosition)]);
        exposureCache.ApplyPaperPosition(settledPosition);

        allowMarkUpdate.TrySetResult(true);
        await applyingUpdate.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, repository.TryUpdatePaperPositionMarksBatchCalls);
        Assert.Equal(0, repository.UpsertPaperPositionsBatchCalls);
        Assert.Equal(settledPosition, Assert.Single(repository.PaperPositions));
        Assert.Single(repository.PaperPositionSettlements);
        Assert.Null(exposureCache.GetPaperPosition(position.CopiedTraderWallet, position.AssetId));
    }

    private static MarketDataUpdate BookUpdate(DateTimeOffset timestamp, decimal bestBid = 0.40m)
    {
        var orderBook = new OrderBookSnapshot(
            "asset-1",
            [new OrderBookLevel(bestBid, 10m)],
            [new OrderBookLevel(bestBid + 0.05m, 10m)],
            timestamp,
            "condition-1");
        return new MarketDataUpdate(
            MarketDataEventType.Book,
            "book",
            "asset-1",
            "condition-1",
            orderBook,
            bestBid,
            bestBid + 0.05m,
            null,
            null,
            TradeSide.Unknown,
            false,
            timestamp);
    }

    private static MarketDataSideEffectExecutionTrace CreateTrace(DateTimeOffset receivedAtUtc)
    {
        return new MarketDataSideEffectExecutionTrace(
            "test-component",
            MarketDataEventType.Book,
            "asset-1",
            "condition-1",
            receivedAtUtc,
            DateTimeOffset.UtcNow);
    }

    private static async Task WaitForPhaseAsync(
        MarketDataSideEffectExecutionTrace trace,
        string expectedPhase)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!string.Equals(
                   trace.Capture(DateTimeOffset.UtcNow).Phase,
                   expectedPhase,
                   StringComparison.Ordinal))
        {
            await Task.Delay(10, timeout.Token);
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

    private sealed class BlockingFeeAccountingService : IPolymarketFeeAccountingService
    {
        private readonly TaskCompletionSource<bool> paperFillStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> releasePaperFill = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<PaperFill> ApplyToPaperFillAsync(
            PaperOrder order,
            PaperFill fill,
            CancellationToken cancellationToken = default)
        {
            paperFillStarted.TrySetResult(true);
            await releasePaperFill.Task.WaitAsync(cancellationToken);
            return fill;
        }

        public Task<LiveOrder> ApplyToLiveOrderAsync(
            LiveOrder order,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PaperEntryPersistenceBatch> ApplyToEntryBatchAsync(
            PaperEntryPersistenceBatch batch,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task WaitForPaperFillAsync()
        {
            return paperFillStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void ReleasePaperFill()
        {
            releasePaperFill.TrySetResult(true);
        }
    }
}
