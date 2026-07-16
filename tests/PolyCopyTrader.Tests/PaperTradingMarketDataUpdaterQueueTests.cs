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

        await updater.ApplyUpdateAsync(
            BookUpdate(receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid>(),
            CancellationToken.None);

        var apiError = Assert.Single(repository.ApiErrors);
        Assert.Equal("PaperTradingMarketDataUpdater", apiError.Component);
        Assert.Equal("ApplyUpdate/UpdatePositionMarks", apiError.Operation);
        Assert.Contains("AssetId=asset-1", apiError.Message, StringComparison.Ordinal);
        Assert.Contains("EventType=Book", apiError.Message, StringComparison.Ordinal);
        Assert.Contains("Operation=IAppRepository.TryUpdatePaperPositionMarks", apiError.Message, StringComparison.Ordinal);
        Assert.Contains("simulated paper position mark update failure", apiError.Message, StringComparison.Ordinal);
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

    private static MarketDataUpdate BookUpdate(DateTimeOffset timestamp)
    {
        var orderBook = new OrderBookSnapshot(
            "asset-1",
            [new OrderBookLevel(0.40m, 10m)],
            [new OrderBookLevel(0.45m, 10m)],
            timestamp,
            "condition-1");
        return new MarketDataUpdate(
            MarketDataEventType.Book,
            "book",
            "asset-1",
            "condition-1",
            orderBook,
            0.40m,
            0.45m,
            null,
            null,
            TradeSide.Unknown,
            false,
            timestamp);
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
