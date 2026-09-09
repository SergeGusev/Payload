using System.Text.Json;
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
    public async Task ApplyMakerGtdUpdateAsync_LoadsLinkedRunsOnceForAllMatchingOrders()
    {
        var repository = new TestAppRepository();
        var receivedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        var orders = Enumerable.Range(1, 3)
            .Select(index => AddMakerOrder(repository, receivedAtUtc, $"strategy:maker-{index}"))
            .ToArray();
        var updater = CreateMakerUpdater(repository);

        await updater.ApplyMakerGtdUpdateAsync(
            MakerBookUpdate(receivedAtUtc),
            receivedAtUtc,
            orders.Select(order => order.Id).ToHashSet(),
            CancellationToken.None);

        Assert.Equal(1, repository.MakerGtdLinkedRunLookupCalls);
        Assert.Equal(
            orders.Select(order => order.Id).Order(),
            Assert.Single(repository.MakerGtdLinkedRunLookupOrderIds));
        Assert.Equal(3, repository.PaperFills.Count);
        Assert.All(repository.PaperOrders, order => Assert.Equal(PaperOrderStatus.Filled, order.Status));
    }

    [Fact]
    public async Task ApplyMakerGtdUpdateAsync_BoundsIndependentWalletsAndWaitsBeforeNextEvent()
    {
        var repository = new TestAppRepository();
        var receivedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        var orders = Enumerable.Range(1, 6)
            .Select(index => AddMakerOrder(repository, receivedAtUtc, $"strategy:maker-{index}"))
            .ToArray();
        var feeService = new CoordinatedMakerFeeAccountingService();
        var updater = CreateMakerUpdater(repository, feeService);
        var trace = CreateTrace(receivedAtUtc);

        var firstApply = updater.ApplyMakerGtdUpdateAsync(
            MakerBookUpdate(receivedAtUtc),
            receivedAtUtc,
            orders.Select(order => order.Id).ToHashSet(),
            CancellationToken.None,
            trace);
        await feeService.WaitForStartedCountAsync(4);

        Assert.Equal(4, feeService.StartedCount);
        Assert.Equal(4, feeService.MaximumActiveCount);
        Assert.Equal(
            "PaperTradingMarketDataUpdater.ApplyMakerGtdWalletGroups(MaxConcurrency=4)",
            trace.Capture(DateTimeOffset.UtcNow).Operation);

        var secondReceivedAtUtc = receivedAtUtc.AddMilliseconds(1);
        var secondApply = updater.ApplyMakerGtdUpdateAsync(
            MakerBookUpdate(secondReceivedAtUtc),
            secondReceivedAtUtc,
            orders.Select(order => order.Id).ToHashSet(),
            CancellationToken.None);
        await Task.Delay(50);
        Assert.False(secondApply.IsCompleted);

        feeService.Release();
        await Task.WhenAll(firstApply, secondApply).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(6, feeService.StartedCount);
        Assert.Equal(4, feeService.MaximumActiveCount);
        Assert.Equal(1, repository.MakerGtdLinkedRunLookupCalls);
        Assert.Equal(6, repository.PaperFills.Count);
    }

    [Fact]
    public async Task ApplyMakerGtdUpdateAsync_SerializesOrdersWithinOneWallet()
    {
        var repository = new TestAppRepository();
        var receivedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        var firstWalletOrder = AddMakerOrder(repository, receivedAtUtc, "strategy:same-wallet");
        var secondWalletOrder = AddMakerOrder(repository, receivedAtUtc, "strategy:same-wallet");
        var independentOrder = AddMakerOrder(repository, receivedAtUtc, "strategy:independent");
        var orders = new[] { firstWalletOrder, secondWalletOrder, independentOrder };
        var feeService = new CoordinatedMakerFeeAccountingService();
        var updater = CreateMakerUpdater(repository, feeService);

        var apply = updater.ApplyMakerGtdUpdateAsync(
            MakerBookUpdate(receivedAtUtc),
            receivedAtUtc,
            orders.Select(order => order.Id).ToHashSet(),
            CancellationToken.None);
        await feeService.WaitForStartedCountAsync(2);
        await Task.Delay(50);

        var startedOrderIds = feeService.StartedOrderIds;
        Assert.Equal(2, startedOrderIds.Count);
        Assert.Equal(
            1,
            startedOrderIds.Count(orderId =>
                orderId == firstWalletOrder.Id || orderId == secondWalletOrder.Id));
        Assert.Contains(independentOrder.Id, startedOrderIds);

        feeService.Release();
        await apply.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, feeService.StartedCount);
        Assert.Equal(2, feeService.MaximumActiveCount);
        Assert.Equal(3, repository.PaperFills.Count);
        Assert.Equal(
            20m,
            Assert.Single(repository.PaperPositions, position =>
                string.Equals(
                    position.CopiedTraderWallet,
                    firstWalletOrder.CopiedTraderWallet,
                    StringComparison.Ordinal)).SizeShares);
    }

    [Fact]
    public async Task ApplyMakerGtdUpdateAsync_SerializesCacheAliasesWithoutMergingPersistedPositions()
    {
        var repository = new TestAppRepository();
        var receivedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        var lowerCaseOrder = AddMakerOrder(repository, receivedAtUtc, " strategy:case-wallet ");
        var upperCaseOrder = AddMakerOrder(repository, receivedAtUtc, "STRATEGY:CASE-WALLET");
        var feeService = new CoordinatedMakerFeeAccountingService();
        var exposureCache = new ExposureSnapshotCache(repository);
        await exposureCache.GetSnapshotAsync();
        var updater = CreateMakerUpdater(repository, feeService, exposureCache);

        var apply = updater.ApplyMakerGtdUpdateAsync(
            MakerBookUpdate(receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid> { lowerCaseOrder.Id, upperCaseOrder.Id },
            CancellationToken.None);
        await feeService.WaitForStartedCountAsync(1);
        await Task.Delay(50);

        Assert.Equal(1, feeService.StartedCount);
        Assert.Equal(1, feeService.MaximumActiveCount);

        feeService.Release();
        await apply.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, feeService.StartedCount);
        Assert.Equal(1, feeService.MaximumActiveCount);
        Assert.Equal(2, repository.PaperFills.Count);
        Assert.Equal(2, repository.PaperPositions.Count);
        Assert.All(repository.PaperPositions, position => Assert.Equal(10m, position.SizeShares));
        Assert.Contains(
            repository.PaperPositions,
            position => string.Equals(
                position.CopiedTraderWallet,
                lowerCaseOrder.CopiedTraderWallet,
                StringComparison.Ordinal));
        Assert.Contains(
            repository.PaperPositions,
            position => string.Equals(
                position.CopiedTraderWallet,
                upperCaseOrder.CopiedTraderWallet,
                StringComparison.Ordinal));
        var cachedPosition = Assert.Single((await exposureCache.GetSnapshotAsync()).PaperPositions);
        Assert.Equal(upperCaseOrder.CopiedTraderWallet, cachedPosition.CopiedTraderWallet);
        Assert.Equal(10m, cachedPosition.SizeShares);
    }

    [Fact]
    public async Task ApplyMakerGtdUpdateAsync_OneWalletFailureDoesNotSuppressIndependentWallet()
    {
        var repository = new TestAppRepository();
        var receivedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        var failedOrder = AddMakerOrder(repository, receivedAtUtc, "strategy:failed");
        var successfulOrder = AddMakerOrder(repository, receivedAtUtc, "strategy:successful");
        var updater = CreateMakerUpdater(
            repository,
            new SelectiveThrowingMakerFeeAccountingService(failedOrder.Id));

        await updater.ApplyMakerGtdUpdateAsync(
            MakerBookUpdate(receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid> { failedOrder.Id, successfulOrder.Id },
            CancellationToken.None);

        Assert.Equal(
            PaperOrderStatus.Pending,
            repository.PaperOrders.Single(order => order.Id == failedOrder.Id).Status);
        Assert.Equal(
            PaperOrderStatus.Filled,
            repository.PaperOrders.Single(order => order.Id == successfulOrder.Id).Status);
        Assert.Equal(successfulOrder.Id, Assert.Single(repository.PaperFills).PaperOrderId);
        var apiError = Assert.Single(repository.ApiErrors);
        Assert.Equal("ApplyMakerGtdUpdate/ApplyMakerGtdWalletGroups", apiError.Operation);
        Assert.Contains(failedOrder.Id.ToString(), apiError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyMakerGtdUpdateAsync_NoTouchDoesNotLoadLinkedRuns()
    {
        var repository = new TestAppRepository();
        var receivedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        var order = AddMakerOrder(repository, receivedAtUtc, "strategy:no-touch");
        var updater = CreateMakerUpdater(repository);

        await updater.ApplyMakerGtdUpdateAsync(
            MakerBookUpdate(receivedAtUtc, bestBid: 0.60m),
            receivedAtUtc,
            new HashSet<Guid> { order.Id },
            CancellationToken.None);

        Assert.Equal(0, repository.MakerGtdLinkedRunLookupCalls);
        Assert.Empty(repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(repository.PaperOrders).Status);
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

    private static MarketDataUpdate MakerBookUpdate(DateTimeOffset timestamp, decimal bestBid = 0.40m)
    {
        return BookUpdate(timestamp, bestBid) with
        {
            SourceTimestampUtc = timestamp,
            TimestampQuality = MarketDataTimestampQuality.VenueProvided,
            ReceivedAtUtc = timestamp,
            SourceEventId = Guid.NewGuid().ToString("N"),
            EventFingerprint = Guid.NewGuid().ToString("N")
        };
    }

    private static PaperOrder AddMakerOrder(
        TestAppRepository repository,
        DateTimeOffset receivedAtUtc,
        string copiedTraderWallet)
    {
        var createdAtUtc = receivedAtUtc.AddMinutes(-2);
        var acceptedAtUtc = createdAtUtc.AddSeconds(1);
        var strategyId = Guid.NewGuid();
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            copiedTraderWallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Yes",
            0.50m,
            10m,
            5m,
            createdAtUtc,
            receivedAtUtc.AddMinutes(1),
            StrategyId: strategyId,
            ExecutionSource: MakerGtdPaperExecutionContract.ExecutionSource);
        order = order with
        {
            RawDecisionJson = JsonSerializer.Serialize(new
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
                    stale = false,
                    reconnect_count = 1,
                    last_connected_utc = (DateTimeOffset?)createdAtUtc.AddMinutes(-1),
                    last_disconnected_utc = (DateTimeOffset?)null,
                    asset_subscribed = true,
                    subscribed_assets_count = 1,
                    accepted_at_utc = acceptedAtUtc
                }
            })
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
            MarketEndUtc: order.ExpiresAtUtc.AddMinutes(1),
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
        return order;
    }

    private static PaperTradingMarketDataUpdater CreateMakerUpdater(
        TestAppRepository repository,
        IPolymarketFeeAccountingService? feeAccountingService = null,
        IExposureSnapshotCache? exposureCache = null)
    {
        return new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            exposureCache ?? new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository,
            feeAccountingService,
            new MarketDataWebSocketOptions { StaleAfterSeconds = 30 });
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

    private sealed class CoordinatedMakerFeeAccountingService : IPolymarketFeeAccountingService
    {
        private readonly object sync = new();
        private readonly TaskCompletionSource<bool> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<Guid> startedOrderIds = [];
        private int activeCount;
        private int maximumActiveCount;

        public int StartedCount
        {
            get
            {
                lock (sync)
                {
                    return startedOrderIds.Count;
                }
            }
        }

        public int MaximumActiveCount
        {
            get
            {
                lock (sync)
                {
                    return maximumActiveCount;
                }
            }
        }

        public IReadOnlyList<Guid> StartedOrderIds
        {
            get
            {
                lock (sync)
                {
                    return startedOrderIds.ToArray();
                }
            }
        }

        public async Task<PaperFill> ApplyToPaperFillAsync(
            PaperOrder order,
            PaperFill fill,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                startedOrderIds.Add(order.Id);
                activeCount++;
                maximumActiveCount = Math.Max(maximumActiveCount, activeCount);
            }

            try
            {
                await release.Task.WaitAsync(cancellationToken);
                return fill;
            }
            finally
            {
                lock (sync)
                {
                    activeCount--;
                }
            }
        }

        public Task<LiveOrder> ApplyToLiveOrderAsync(
            LiveOrder order,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PaperEntryPersistenceBatch> ApplyToEntryBatchAsync(
            PaperEntryPersistenceBatch batch,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task WaitForStartedCountAsync(int expectedCount)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (StartedCount < expectedCount)
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        public void Release()
        {
            release.TrySetResult(true);
        }
    }

    private sealed class SelectiveThrowingMakerFeeAccountingService(Guid failedOrderId)
        : IPolymarketFeeAccountingService
    {
        public Task<PaperFill> ApplyToPaperFillAsync(
            PaperOrder order,
            PaperFill fill,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return order.Id == failedOrderId
                ? Task.FromException<PaperFill>(new InvalidOperationException("simulated independent wallet failure"))
                : Task.FromResult(fill);
        }

        public Task<LiveOrder> ApplyToLiveOrderAsync(
            LiveOrder order,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PaperEntryPersistenceBatch> ApplyToEntryBatchAsync(
            PaperEntryPersistenceBatch batch,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
