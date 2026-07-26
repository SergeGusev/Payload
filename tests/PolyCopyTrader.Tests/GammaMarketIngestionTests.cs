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
    public async Task Refresh_CryptoOnlyScope_RegistersBtcEthSolWebSocketAssetsButUpsertsAllMarkets()
    {
        var gammaClient = new FakeGammaClient();
        gammaClient.Pages[0] =
        [
            CreateMarketForTests("regular-market"),
            CreateCryptoUpDown5mMarketForTests("BTC", "btc-market"),
            CreateCryptoUpDown5mMarketForTests("ETH", "eth-market"),
            CreateCryptoUpDown5mMarketForTests("SOL", "sol-market"),
            CreateCryptoUpDown5mMarketForTests("DOGE", "doge-market")
        ];
        gammaClient.Pages[5] = [];
        var repository = new TestAppRepository();
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        registry.AddOrUpdateMarkets([CreateMarketForTests("stale-regular-market")]);
        var processor = CreateProcessor(
            gammaClient,
            repository,
            pageLimit: 5,
            activeMarketAssetSubscriptionRegistry: registry,
            marketDataWebSocketOptions: new MarketDataWebSocketOptions
            {
                SubscriptionScope = MarketDataWebSocketSubscriptionScope.CryptoUpDown5mOnly
            });

        await processor.RefreshAsync();

        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "regular-market");
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "btc-market");
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "eth-market");
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "sol-market");
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "doge-market");
        Assert.DoesNotContain("token-yes-regular-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("token-yes-stale-regular-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("token-yes-doge-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-yes-btc-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-no-btc-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-yes-eth-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-no-eth-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-yes-sol-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-no-sol-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_CryptoUpDown5mPersistenceScope_UpsertsOnlyBtcEthSolFiveMinuteMarkets()
    {
        var gammaClient = new FakeGammaClient();
        gammaClient.Pages[0] =
        [
            CreateMarketForTests("regular-market"),
            CreateCryptoUpDown5mMarketForTests("BTC", "btc-5m-market"),
            CreateCryptoUpDown5mMarketForTests("ETH", "eth-5m-market"),
            CreateCryptoUpDown5mMarketForTests("SOL", "sol-5m-market"),
            CreateCryptoUpDown5mMarketForTests("DOGE", "doge-5m-market"),
            CreateCryptoUpDownMarketForTests("BTC", "15m", "btc-15m-market"),
            CreateCryptoUpDownMarketForTests("ETH", "15m", "eth-15m-market"),
            CreateCryptoUpDownMarketForTests("SOL", "15m", "sol-15m-market")
        ];
        gammaClient.Pages[8] = [];
        var repository = new TestAppRepository();
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        var processor = CreateProcessor(
            gammaClient,
            repository,
            pageLimit: 8,
            activeMarketAssetSubscriptionRegistry: registry,
            marketDataWebSocketOptions: new MarketDataWebSocketOptions
            {
                SubscriptionScope = MarketDataWebSocketSubscriptionScope.AllActiveMarkets
            },
            persistenceScope: GammaMarketPersistenceScope.CryptoUpDown5mOnly);

        var result = await processor.RefreshAsync();

        Assert.Equal(8, result.MarketsFetched);
        Assert.Equal(3, result.MarketsUpserted);
        Assert.Equal(
            new[] { "btc-5m-market", "eth-5m-market", "sol-5m-market" },
            repository.PolymarketGammaMarkets.Select(market => market.MarketId).OrderBy(id => id).ToArray());
        Assert.Contains("token-yes-regular-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-yes-doge-5m-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-yes-btc-15m-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_PrioritySync_UpsertsBtcFiveMinuteMarketsBeforeFullScan()
    {
        var gammaClient = new FakeGammaClient
        {
            MarketsBySlugs = [CreateBtcUpDown5mMarketForTests("priority-btc-market")]
        };
        gammaClient.Pages[0] = [];
        var repository = new TestAppRepository();
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        var processor = CreateProcessor(
            gammaClient,
            repository,
            pageLimit: 2,
            activeMarketAssetSubscriptionRegistry: registry,
            marketDataWebSocketOptions: new MarketDataWebSocketOptions
            {
                SubscriptionScope = MarketDataWebSocketSubscriptionScope.BtcUpDown5mOnly
            });

        var result = await processor.RefreshAsync();

        var slugRequest = Assert.Single(gammaClient.SlugRequests);
        Assert.Contains(slugRequest, slug => slug.StartsWith("btc-updown-5m-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(slugRequest, slug => slug.StartsWith("eth-updown-5m-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(slugRequest, slug => slug.StartsWith("sol-updown-5m-", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, result.MarketsFetched);
        Assert.Equal(1, result.MarketsUpserted);
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "priority-btc-market");
        Assert.Contains("token-yes-priority-btc-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-no-priority-btc-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_PrioritySync_UpsertsCryptoFiveMinuteMarketsBeforeFullScan()
    {
        var gammaClient = new FakeGammaClient
        {
            MarketsBySlugs =
            [
                CreateCryptoUpDown5mMarketForTests("BTC", "priority-btc-market"),
                CreateCryptoUpDown5mMarketForTests("ETH", "priority-eth-market"),
                CreateCryptoUpDown5mMarketForTests("SOL", "priority-sol-market"),
                CreateCryptoUpDown5mMarketForTests("DOGE", "priority-doge-market")
            ]
        };
        gammaClient.Pages[0] = [];
        var repository = new TestAppRepository();
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        var processor = CreateProcessor(
            gammaClient,
            repository,
            pageLimit: 2,
            activeMarketAssetSubscriptionRegistry: registry,
            marketDataWebSocketOptions: new MarketDataWebSocketOptions
            {
                SubscriptionScope = MarketDataWebSocketSubscriptionScope.CryptoUpDown5mOnly
            });

        var result = await processor.RefreshAsync();

        var slugRequest = Assert.Single(gammaClient.SlugRequests);
        Assert.Contains(slugRequest, slug => slug.StartsWith("btc-updown-5m-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(slugRequest, slug => slug.StartsWith("eth-updown-5m-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(slugRequest, slug => slug.StartsWith("sol-updown-5m-", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, result.MarketsFetched);
        Assert.Equal(3, result.MarketsUpserted);
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "priority-btc-market");
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "priority-eth-market");
        Assert.Contains(repository.PolymarketGammaMarkets, market => market.MarketId == "priority-sol-market");
        Assert.DoesNotContain(repository.PolymarketGammaMarkets, market => market.MarketId == "priority-doge-market");
        Assert.Contains("token-yes-priority-btc-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-yes-priority-eth-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-yes-priority-sol-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("token-yes-priority-doge-market", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
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

    [Theory]
    [InlineData("offset exceeds maximum allowed for markets list queries")]
    [InlineData("offset too large, use /markets/keyset for deeper pagination")]
    public async Task Refresh_TreatsGammaMaxOffsetAsCompletedFullScan(string errorText)
    {
        var gammaClient = new FakeGammaClient();
        gammaClient.Pages[0] = [CreateMarketForTests("current-market")];
        gammaClient.Exceptions[2] = new PolymarketApiException(
            "PolymarketGammaClient",
            "GetActiveMarkets",
            "GetActiveMarkets failed with HTTP 422 Unprocessable Entity. Body: " +
                "{\"type\":\"validation error\",\"error\":\"" + errorText + "\"}");
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
        MarketDataWebSocketOptions? marketDataWebSocketOptions = null,
        GammaMarketPersistenceScope persistenceScope = GammaMarketPersistenceScope.AllActiveMarkets)
    {
        return new GammaMarketIngestionProcessor(
            NullLogger<GammaMarketIngestionProcessor>.Instance,
            new GammaMarketIngestionOptions
            {
                PageLimit = pageLimit,
                PollIntervalSeconds = 10,
                PersistenceScope = persistenceScope
            },
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
        return CreateCryptoUpDown5mMarketForTests("BTC", id);
    }

    public static PolymarketGammaMarket CreateCryptoUpDown5mMarketForTests(string assetSymbol, string id)
    {
        return CreateCryptoUpDownMarketForTests(assetSymbol, "5m", id);
    }

    private static PolymarketGammaMarket CreateCryptoUpDownMarketForTests(
        string assetSymbol,
        string interval,
        string id)
    {
        var normalizedAssetSymbol = assetSymbol.ToLowerInvariant();
        return CreateMarketForTests(id) with
        {
            Slug = normalizedAssetSymbol + "-updown-" + interval + "-1778130600",
            EventSlug = normalizedAssetSymbol + "-updown-" + interval + "-1778130600",
            SeriesSlug = normalizedAssetSymbol + "-up-or-down-" + interval,
            Category = "Crypto"
        };
    }

    private sealed class FakeGammaClient : IPolymarketGammaClient
    {
        public Dictionary<int, IReadOnlyList<PolymarketGammaMarket>> Pages { get; } = [];

        public Dictionary<int, Exception> Exceptions { get; } = [];

        public List<(int Limit, int Offset)> Requests { get; } = [];

        public List<IReadOnlyList<string>> SlugRequests { get; } = [];

        public IReadOnlyList<PolymarketGammaMarket> MarketsBySlugs { get; init; } = [];

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

        public Task<IReadOnlyList<PolymarketGammaMarket>> GetMarketsBySlugsAsync(
            IReadOnlyCollection<string> slugs,
            bool activeOnly = true,
            CancellationToken cancellationToken = default)
        {
            SlugRequests.Add(slugs.ToArray());
            return Task.FromResult(MarketsBySlugs);
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
