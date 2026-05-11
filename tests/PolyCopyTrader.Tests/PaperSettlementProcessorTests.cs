using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.PaperTrading;

namespace PolyCopyTrader.Tests;

public sealed class PaperSettlementProcessorTests
{
    [Fact]
    public async Task SettleMarketResolutionAsync_SettlesWinningAndLosingCopiedPositions()
    {
        var repository = new TestAppRepository();
        repository.PaperPositions.Add(new PaperPosition(
            "asset-yes",
            "condition-1",
            "Yes",
            5m,
            0.40m,
            2m,
            0m,
            DateTimeOffset.UtcNow,
            "0xleader"));
        repository.PaperPositions.Add(new PaperPosition(
            "asset-no",
            "condition-1",
            "No",
            5m,
            0.30m,
            1.5m,
            0m,
            DateTimeOffset.UtcNow,
            "0xleader"));
        var processor = new PaperSettlementProcessor(
            NullLogger<PaperSettlementProcessor>.Instance,
            new FakeGammaClient([]),
            new ExposureSnapshotCache(repository),
            repository);

        var result = await processor.SettleMarketResolutionAsync(
            "condition-1",
            null,
            "asset-yes",
            "Yes",
            "Politics",
            "UnitTest",
            DateTimeOffset.UtcNow);

        Assert.Equal(2, result.PositionsSettled);
        Assert.Equal(2, result.SettlementsInserted);
        Assert.All(repository.PaperPositions, position => Assert.Equal(0m, position.SizeShares));
        var yes = Assert.Single(repository.PaperPositionSettlements, item => item.AssetId == "asset-yes");
        Assert.True(yes.Won);
        Assert.Equal(5m, yes.SettlementValueUsd);
        Assert.Equal(3m, yes.RealizedPnlUsd);
        var no = Assert.Single(repository.PaperPositionSettlements, item => item.AssetId == "asset-no");
        Assert.False(no.Won);
        Assert.Equal(0m, no.SettlementValueUsd);
        Assert.Equal(-1.5m, no.RealizedPnlUsd);
    }

    [Fact]
    public async Task ProcessOpenPositionsAsync_UsesClosedGammaMetadataAndRefreshesCopiedTraderPerformance()
    {
        var repository = new TestAppRepository();
        repository.PaperOrders.Add(new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0xleader",
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            "asset-yes",
            "condition-1",
            "Yes",
            0.40m,
            5m,
            2m,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(5),
            DateTimeOffset.UtcNow.AddMinutes(-4)));
        repository.PaperFills.Add(new PaperFill(
            Guid.NewGuid(),
            repository.PaperOrders[0].Id,
            0.40m,
            5m,
            DateTimeOffset.UtcNow.AddMinutes(-4),
            "test"));
        repository.PaperPositions.Add(new PaperPosition(
            "asset-yes",
            "condition-1",
            "Yes",
            5m,
            0.40m,
            2m,
            0m,
            DateTimeOffset.UtcNow,
            "0xleader"));
        repository.PolymarketGammaMarkets.Add(GammaMarket("condition-1", "Politics"));
        var metadata = new[]
        {
            TokenMetadata("asset-yes", "condition-1", "Yes", "Yes", "Politics"),
            TokenMetadata("asset-no", "condition-1", "No", "Yes", "Politics")
        };
        var processor = new PaperSettlementProcessor(
            NullLogger<PaperSettlementProcessor>.Instance,
            new FakeGammaClient(metadata),
            new ExposureSnapshotCache(repository),
            repository);

        var result = await processor.ProcessOpenPositionsAsync();

        Assert.Equal(1, result.SettlementsInserted);
        var performance = Assert.Single(repository.PaperCopiedTraderPerformances, row => row.Category == "OVERALL");
        Assert.Equal("0xleader", performance.CopiedTraderWallet);
        Assert.Equal(1, performance.SettledPositionsCount);
        Assert.Equal(1, performance.WonPositionsCount);
        Assert.Equal(3m, performance.TotalPnlUsd);
        Assert.Equal(150m, performance.RoiPct);
    }

    private static PolymarketOnChainTokenMetadata TokenMetadata(
        string tokenId,
        string conditionId,
        string outcome,
        string winningOutcome,
        string category)
    {
        return new PolymarketOnChainTokenMetadata(
            tokenId,
            conditionId,
            "market-1",
            "market-slug",
            "Market title",
            outcome,
            outcome == "Yes" ? 0 : 1,
            category,
            DateTimeOffset.UtcNow,
            Active: false,
            Closed: true,
            Archived: false,
            Resolved: true,
            winningOutcome,
            ["asset-yes", "asset-no"],
            ["Yes", "No"],
            LookupSucceeded: true,
            LookupError: null,
            RawJson: "{}",
            LastRefreshedUtc: DateTimeOffset.UtcNow);
    }

    private static PolymarketGammaMarket GammaMarket(string conditionId, string category)
    {
        return new PolymarketGammaMarket(
            "market-1",
            conditionId,
            "question-1",
            "market-slug",
            "Market title",
            null,
            null,
            null,
            null,
            category,
            Active: false,
            Closed: true,
            Archived: false,
            Restricted: false,
            AcceptingOrders: false,
            EnableOrderBook: true,
            NegativeRisk: false,
            Liquidity: null,
            LiquidityClob: null,
            Volume: null,
            Volume24Hr: null,
            BestBid: null,
            BestAsk: null,
            Spread: null,
            CreatedAtUtc: null,
            UpdatedAtUtc: null,
            StartDateUtc: null,
            EndDateUtc: null,
            EventStartTimeUtc: null,
            Outcomes: ["Yes", "No"],
            ClobTokenIds: ["asset-yes", "asset-no"],
            RawJson: "{}",
            FetchedAtUtc: DateTimeOffset.UtcNow);
    }

    private sealed class FakeGammaClient(IReadOnlyList<PolymarketOnChainTokenMetadata> metadata) : IPolymarketGammaClient
    {
        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(
            int limit = 500,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>([]);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(
            string tokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>(
                metadata.Any(item => string.Equals(item.TokenId, tokenId, StringComparison.OrdinalIgnoreCase))
                    ? metadata
                    : []);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(
            string conditionId,
            string requestedTokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>(
                metadata.Any(item => string.Equals(item.ConditionId, conditionId, StringComparison.OrdinalIgnoreCase))
                    ? metadata
                    : []);
        }

        public Task<string?> GetEventCategoryAsync(string eventId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
