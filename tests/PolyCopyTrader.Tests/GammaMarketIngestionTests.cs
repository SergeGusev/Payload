using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.GammaMarkets;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Tests;

public sealed class GammaMarketIngestionTests
{
    [Fact]
    public async Task Refresh_WalksPagesUntilEmptyAndInsertsAllMarkets()
    {
        var gammaClient = new FakeGammaClient();
        gammaClient.Pages[0] = [CreateMarketForTests("market-3"), CreateMarketForTests("market-2")];
        gammaClient.Pages[2] = [CreateMarketForTests("market-1")];
        gammaClient.Pages[4] = [];
        var repository = new TestAppRepository();
        var processor = CreateProcessor(gammaClient, repository, pageLimit: 2);

        var result = await processor.RefreshAsync();

        Assert.Equal(new[] { 0, 2, 4 }, gammaClient.Requests.Select(request => request.Offset).ToArray());
        Assert.Equal(3, result.PagesFetched);
        Assert.Equal(3, result.MarketsFetched);
        Assert.Equal(3, result.MarketsUpserted);
        Assert.True(result.ReachedEmptyPage);
        Assert.Equal(new[] { "market-3", "market-2", "market-1" }, repository.PolymarketGammaMarkets.Select(market => market.MarketId).ToArray());
    }

    [Fact]
    public async Task Refresh_UpsertsExistingMarketsAndContinuesThroughAllPages()
    {
        var gammaClient = new FakeGammaClient();
        gammaClient.Pages[0] =
        [
            CreateMarketForTests("new-market"),
            CreateMarketForTests("existing-market") with { Question = "Updated existing market", Volume = 2000m },
            CreateMarketForTests("older-market")
        ];
        gammaClient.Pages[3] = [CreateMarketForTests("next-page-market")];
        gammaClient.Pages[6] = [];
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarketForTests("existing-market") with { Question = "Old existing market", Volume = 100m });
        var processor = CreateProcessor(gammaClient, repository, pageLimit: 3);

        var result = await processor.RefreshAsync();

        Assert.Equal(new[] { 0, 3, 6 }, gammaClient.Requests.Select(request => request.Offset).ToArray());
        Assert.Equal(3, result.PagesFetched);
        Assert.Equal(4, result.MarketsFetched);
        Assert.Equal(4, result.MarketsUpserted);
        Assert.True(result.ReachedEmptyPage);
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "new-market");
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "older-market");
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "next-page-market");
        var existing = Assert.Single(repository.PolymarketGammaMarkets, market => market.MarketId == "existing-market");
        Assert.Equal("Updated existing market", existing.Question);
        Assert.Equal(2000m, existing.Volume);
    }

    [Fact]
    public async Task Refresh_RegistersWebSocketAssetsBeforeDatabaseUpsert()
    {
        var gammaClient = new FakeGammaClient();
        gammaClient.Pages[0] = [CreateMarketForTests("market-1")];
        gammaClient.Pages[2] = [];
        var repository = new TestAppRepository();
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        var registryHadAssetBeforeUpsert = false;
        repository.BeforeUpsertPolymarketGammaMarket = market =>
        {
            registryHadAssetBeforeUpsert = registry.TryGetSnapshot("token-yes-" + market.MarketId, out var snapshot) &&
                snapshot.OrderMinSize == 5m &&
                snapshot.OrderPriceMinTickSize == 0.01m;
        };
        var processor = CreateProcessor(gammaClient, repository, pageLimit: 2, activeMarketAssetSubscriptionRegistry: registry);

        await processor.RefreshAsync();

        Assert.True(registryHadAssetBeforeUpsert);
        Assert.Contains("token-yes-market-1", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-no-market-1", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_BtcOnlyScope_RegistersOnlyBtcWebSocketAssetsButUpsertsAllMarkets()
    {
        var gammaClient = new FakeGammaClient();
        gammaClient.Pages[0] =
        [
            CreateMarketForTests("regular-market"),
            CreateBtcUpDown5mMarketForTests("btc-market")
        ];
        gammaClient.Pages[2] = [];
        var repository = new TestAppRepository();
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        registry.AddOrUpdateMarkets([CreateMarketForTests("stale-regular-market")]);
        var processor = CreateProcessor(
            gammaClient,
            repository,
            pageLimit: 2,
            activeMarketAssetSubscriptionRegistry: registry,
            marketDataWebSocketOptions: new MarketDataWebSocketOptions
            {
                SubscriptionScope = MarketDataWebSocketSubscriptionScope.BtcUpDown5mOnly
            });

        await processor.RefreshAsync();

        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "regular-market");
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "btc-market");
        Assert.DoesNotContain("token-yes-regular-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("token-yes-stale-regular-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-yes-btc-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-no-btc-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_RemovesAssetsMissingFromCompletedFullScan()
    {
        var gammaClient = new FakeGammaClient();
        gammaClient.Pages[0] = [CreateMarketForTests("current-market")];
        gammaClient.Pages[2] = [];
        var repository = new TestAppRepository();
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        registry.AddOrUpdateMarkets([CreateMarketForTests("stale-market")]);
        var processor = CreateProcessor(gammaClient, repository, pageLimit: 2, activeMarketAssetSubscriptionRegistry: registry);

        await processor.RefreshAsync();

        Assert.Contains("token-yes-current-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("token-yes-stale-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.False(registry.TryGetSnapshot("token-yes-stale-market", out _));
    }

    [Fact]
    public async Task Refresh_TreatsGammaMaxOffsetAsCompletedFullScan()
    {
        var gammaClient = new FakeGammaClient();
        gammaClient.Pages[0] = [CreateMarketForTests("current-market")];
        gammaClient.Exceptions[2] = new PolymarketApiException(
            "PolymarketGammaClient",
            "GetActiveMarkets",
            """GetActiveMarkets failed with HTTP 422 Unprocessable Entity. Body: {"type":"validation error","error":"offset exceeds maximum allowed for markets list queries"}""");
        var repository = new TestAppRepository();
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        registry.AddOrUpdateMarkets([CreateMarketForTests("stale-market")]);
        var processor = CreateProcessor(
            gammaClient,
            repository,
            pageLimit: 2,
            activeMarketAssetSubscriptionRegistry: registry);

        var result = await processor.RefreshAsync();

        Assert.Equal(new[] { 0, 2 }, gammaClient.Requests.Select(request => request.Offset).ToArray());
        Assert.Equal(1, result.PagesFetched);
        Assert.Equal(1, result.MarketsFetched);
        Assert.Equal(1, result.MarketsUpserted);
        Assert.True(result.ReachedEmptyPage);
        Assert.Equal(2, result.NextOffset);
        Assert.Contains("token-yes-current-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("token-yes-stale-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "current-market");
    }

    [Fact]
    public async Task Refresh_RethrowsUnexpectedGammaActiveMarketErrors()
    {
        var gammaClient = new FakeGammaClient();
        gammaClient.Exceptions[0] = new PolymarketApiException(
            "PolymarketGammaClient",
            "GetActiveMarkets",
            "GetActiveMarkets failed with HTTP 500 Internal Server Error. Body: gateway failed");
        var processor = CreateProcessor(gammaClient, new TestAppRepository(), pageLimit: 2);

        await Assert.ThrowsAsync<PolymarketApiException>(() => processor.RefreshAsync());
    }

    private static GammaMarketIngestionProcessor CreateProcessor(
        FakeGammaClient gammaClient,
        TestAppRepository repository,
        int pageLimit,
        IActiveMarketAssetSubscriptionRegistry? activeMarketAssetSubscriptionRegistry = null,
        MarketDataWebSocketOptions? marketDataWebSocketOptions = null)
    {
        return new GammaMarketIngestionProcessor(
            NullLogger<GammaMarketIngestionProcessor>.Instance,
            new GammaMarketIngestionOptions { PageLimit = pageLimit, PollIntervalSeconds = 10 },
            marketDataWebSocketOptions ?? new MarketDataWebSocketOptions(),
            gammaClient,
            activeMarketAssetSubscriptionRegistry ?? new ActiveMarketAssetSubscriptionRegistry(),
            repository);
    }

    public static PolymarketGammaMarket CreateMarketForTests(string id)
    {
        return new PolymarketGammaMarket(
            MarketId: id,
            ConditionId: "condition-" + id,
            QuestionId: "question-" + id,
            Slug: "slug-" + id,
            Question: "Question " + id,
            EventId: "event-" + id,
            EventSlug: "event-slug-" + id,
            EventTitle: "Event " + id,
            SeriesSlug: "series",
            Category: "Politics",
            Active: true,
            Closed: false,
            Archived: false,
            Restricted: false,
            AcceptingOrders: true,
            EnableOrderBook: true,
            NegativeRisk: false,
            Liquidity: 100m,
            LiquidityClob: 50m,
            Volume: 1000m,
            Volume24Hr: 10m,
            BestBid: 0.49m,
            BestAsk: 0.51m,
            Spread: 0.02m,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            StartDateUtc: DateTimeOffset.UtcNow,
            EndDateUtc: DateTimeOffset.UtcNow.AddDays(1),
            EventStartTimeUtc: DateTimeOffset.UtcNow,
            Outcomes: ["Yes", "No"],
            ClobTokenIds: ["token-yes-" + id, "token-no-" + id],
            RawJson: "{}",
            FetchedAtUtc: DateTimeOffset.UtcNow,
            LastTradePrice: 0.50m,
            OrderMinSize: 5m,
            OrderPriceMinTickSize: 0.01m);
    }

    public static PolymarketGammaMarket CreateBtcUpDown5mMarketForTests(string id)
    {
        return CreateMarketForTests(id) with
        {
            Slug = "btc-updown-5m-1778130600",
            EventSlug = "btc-updown-5m-1778130600",
            SeriesSlug = "btc-up-or-down-5m",
            Category = "Crypto"
        };
    }

    private sealed class FakeGammaClient : IPolymarketGammaClient
    {
        public Dictionary<int, IReadOnlyList<PolymarketGammaMarket>> Pages { get; } = [];

        public Dictionary<int, Exception> Exceptions { get; } = [];

        public List<(int Limit, int Offset)> Requests { get; } = [];

        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(
            int limit = 500,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((limit, offset));
            if (Exceptions.TryGetValue(offset, out var exception))
            {
                throw exception;
            }

            return Task.FromResult(Pages.TryGetValue(offset, out var markets) ? markets : []);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(
            string tokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>([]);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(
            string conditionId,
            string requestedTokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>([]);
        }

        public Task<string?> GetEventCategoryAsync(
            string eventId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
