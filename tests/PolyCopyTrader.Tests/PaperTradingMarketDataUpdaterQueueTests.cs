using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
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
        Assert.Contains("Operation=IAppRepository.UpsertPaperPosition", apiError.Message, StringComparison.Ordinal);
        Assert.Contains("simulated paper position upsert failure", apiError.Message, StringComparison.Ordinal);
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
