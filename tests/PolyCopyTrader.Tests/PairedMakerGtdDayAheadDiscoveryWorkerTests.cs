using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Configuration;
using PolyCopyTrader.Service.GammaMarkets;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Tests;

public sealed class PairedMakerGtdDayAheadDiscoveryWorkerTests
{
    [Fact]
    public async Task RunCycle_RequestsExactLeadBandInBatchesAndRegistersBeforeFirstAcceptingProcessing()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 9, 16, 0, 0, TimeSpan.Zero);
        var marketStartUtc = nowUtc.AddHours(23);
        var market = CreateMarket("BTC", marketStartUtc, acceptingOrders: true);
        var gammaClient = new FakeGammaClient([market]);
        var repository = new TestAppRepository();
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        var firstAcceptingProcessor = new FakeFirstAcceptingProcessor
        {
            BeforeProcess = candidate =>
            {
                Assert.Contains(
                    repository.PolymarketGammaMarkets,
                    stored => stored.ConditionId == candidate.Market.ConditionId);
                Assert.Contains("token-up", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
                Assert.Contains("token-down", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
            }
        };
        var worker = CreateWorker(
            nowUtc,
            gammaClient,
            repository,
            registry,
            firstAcceptingProcessor);

        var firstResult = await worker.RunCycleAsync();
        var secondResult = await worker.RunCycleAsync();

        Assert.Equal(75, firstResult.SlugsRequested);
        Assert.Equal(2, firstResult.BatchesRequested);
        Assert.Equal([50, 25], gammaClient.Requests.Take(2).Select(request => request.Slugs.Count).ToArray());
        Assert.All(gammaClient.Requests, request => Assert.True(request.ActiveOnly));
        Assert.Equal(75, gammaClient.Requests[0].Slugs.Concat(gammaClient.Requests[1].Slugs).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(25, gammaClient.Requests[0].Slugs.Concat(gammaClient.Requests[1].Slugs).Count(slug => slug.StartsWith("btc-updown-5m-", StringComparison.Ordinal)));
        Assert.Equal(25, gammaClient.Requests[0].Slugs.Concat(gammaClient.Requests[1].Slugs).Count(slug => slug.StartsWith("eth-updown-5m-", StringComparison.Ordinal)));
        Assert.Equal(25, gammaClient.Requests[0].Slugs.Concat(gammaClient.Requests[1].Slugs).Count(slug => slug.StartsWith("sol-updown-5m-", StringComparison.Ordinal)));
        Assert.Equal(1, firstResult.MarketsFetched);
        Assert.Equal(1, firstResult.MarketsValidated);
        Assert.Equal(1, firstResult.MarketsUpserted);
        Assert.Equal(1, firstResult.FirstAcceptingMarketsProcessed);
        Assert.Equal(0, firstResult.InvalidMarketsSkipped);
        Assert.Equal(0, secondResult.FirstAcceptingMarketsProcessed);
        Assert.Equal(2, firstAcceptingProcessor.DueCalls);
        var candidate = Assert.Single(firstAcceptingProcessor.Candidates);
        Assert.Equal(nowUtc, candidate.RequestStartedAtUtc);
        Assert.Equal(nowUtc, candidate.ResponseCompletedAtUtc);
        Assert.Equal(nowUtc, candidate.FirstObservedAcceptingAtUtc);
    }

    [Fact]
    public async Task RunCycle_RejectsMarketWhoseExpiryDoesNotMatchItsExactFiveMinuteSlug()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 9, 16, 0, 0, TimeSpan.Zero);
        var malformedMarket = CreateMarket("ETH", nowUtc.AddHours(24), acceptingOrders: true) with
        {
            EndDateUtc = nowUtc.AddHours(24).AddMinutes(10)
        };
        var gammaClient = new FakeGammaClient([malformedMarket]);
        var repository = new TestAppRepository();
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        var firstAcceptingProcessor = new FakeFirstAcceptingProcessor();
        var worker = CreateWorker(
            nowUtc,
            gammaClient,
            repository,
            registry,
            firstAcceptingProcessor);

        var result = await worker.RunCycleAsync();

        Assert.Equal(1, result.MarketsFetched);
        Assert.Equal(0, result.MarketsValidated);
        Assert.Equal(0, result.MarketsUpserted);
        Assert.Equal(0, result.FirstAcceptingMarketsProcessed);
        Assert.Equal(1, result.InvalidMarketsSkipped);
        Assert.Empty(repository.PolymarketGammaMarkets);
        Assert.Empty(registry.GetAssetIds());
        Assert.Empty(firstAcceptingProcessor.Candidates);
        Assert.Equal(1, firstAcceptingProcessor.DueCalls);
    }

    [Fact]
    public async Task RunCycle_RejectsNonCanonicalOutcomeCasing()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 9, 16, 0, 0, TimeSpan.Zero);
        var malformedMarket = CreateMarket("SOL", nowUtc.AddHours(24), acceptingOrders: true) with
        {
            Outcomes = ["Up", "DOWN"]
        };
        var gammaClient = new FakeGammaClient([malformedMarket]);
        var repository = new TestAppRepository();
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        var firstAcceptingProcessor = new FakeFirstAcceptingProcessor();
        var worker = CreateWorker(
            nowUtc,
            gammaClient,
            repository,
            registry,
            firstAcceptingProcessor);

        var result = await worker.RunCycleAsync();

        Assert.Equal(1, result.MarketsFetched);
        Assert.Equal(0, result.MarketsValidated);
        Assert.Equal(1, result.InvalidMarketsSkipped);
        Assert.Empty(repository.PolymarketGammaMarkets);
        Assert.Empty(firstAcceptingProcessor.Candidates);
    }

    [Fact]
    public void Configuration_BindsAndValidatesDayAheadDiscoveryBoundaries()
    {
        Dictionary<string, string?> values = new()
        {
            ["PairedMakerGtdDayAheadDiscovery:Enabled"] = "false",
            ["PairedMakerGtdDayAheadDiscovery:PollIntervalSeconds"] = "7",
            ["PairedMakerGtdDayAheadDiscovery:MinimumLeadHours"] = "22",
            ["PairedMakerGtdDayAheadDiscovery:MaximumLeadHours"] = "26",
            ["PairedMakerGtdDayAheadDiscovery:GammaBatchSize"] = "40"
        };
        var configurationRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var configuration = AppConfigurationLoader.Load(configurationRoot);

        Assert.False(configuration.PairedMakerGtdDayAheadDiscovery.Enabled);
        Assert.Equal(7, configuration.PairedMakerGtdDayAheadDiscovery.PollIntervalSeconds);
        Assert.Equal(22, configuration.PairedMakerGtdDayAheadDiscovery.MinimumLeadHours);
        Assert.Equal(26, configuration.PairedMakerGtdDayAheadDiscovery.MaximumLeadHours);
        Assert.Equal(40, configuration.PairedMakerGtdDayAheadDiscovery.GammaBatchSize);
        Assert.Empty(AppOptionsValidator.Validate(configuration));

        var invalidConfiguration = new AppConfiguration
        {
            PairedMakerGtdDayAheadDiscovery = new PairedMakerGtdDayAheadDiscoveryOptions
            {
                PollIntervalSeconds = 0,
                MinimumLeadHours = 25,
                MaximumLeadHours = 23,
                GammaBatchSize = 51
            }
        };
        var errors = AppOptionsValidator.Validate(invalidConfiguration);
        Assert.Contains(errors, error => error.Contains("PollIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MaximumLeadHours", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("GammaBatchSize", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Execute_DoesNotDiscoverOutsidePaperRuntimePolicy()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 9, 16, 0, 0, TimeSpan.Zero);
        var gammaClient = new FakeGammaClient([]);
        var firstAcceptingProcessor = new FakeFirstAcceptingProcessor();
        var worker = new PairedMakerGtdDayAheadDiscoveryWorker(
            NullLogger<PairedMakerGtdDayAheadDiscoveryWorker>.Instance,
            new BotOptions { Mode = BotMode.ReadOnly },
            new PaperTradingOptions(),
            new PairedMakerGtdDayAheadDiscoveryOptions(),
            gammaClient,
            new ActiveMarketAssetSubscriptionRegistry(),
            new TestAppRepository(),
            firstAcceptingProcessor,
            new FixedTimeProvider(nowUtc));

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(gammaClient.Requests);
        Assert.Empty(firstAcceptingProcessor.Candidates);
        Assert.Equal(0, firstAcceptingProcessor.DueCalls);
    }

    private static PairedMakerGtdDayAheadDiscoveryWorker CreateWorker(
        DateTimeOffset nowUtc,
        IPolymarketGammaClient gammaClient,
        TestAppRepository repository,
        IActiveMarketAssetSubscriptionRegistry registry,
        IPairedMakerGtdFirstAcceptingProcessor firstAcceptingProcessor)
    {
        return new PairedMakerGtdDayAheadDiscoveryWorker(
            NullLogger<PairedMakerGtdDayAheadDiscoveryWorker>.Instance,
            new BotOptions { Mode = BotMode.Paper },
            new PaperTradingOptions(),
            new PairedMakerGtdDayAheadDiscoveryOptions(),
            gammaClient,
            registry,
            repository,
            firstAcceptingProcessor,
            new FixedTimeProvider(nowUtc));
    }

    private static PolymarketGammaMarket CreateMarket(
        string assetSymbol,
        DateTimeOffset marketStartUtc,
        bool acceptingOrders)
    {
        var slug = assetSymbol.ToLowerInvariant() + "-updown-5m-" + marketStartUtc.ToUnixTimeSeconds();
        return new PolymarketGammaMarket(
            MarketId: "market-" + assetSymbol.ToLowerInvariant(),
            ConditionId: "condition-" + assetSymbol.ToLowerInvariant(),
            QuestionId: "question-" + assetSymbol.ToLowerInvariant(),
            Slug: slug,
            Question: assetSymbol + " Up or Down",
            EventId: "event-" + assetSymbol.ToLowerInvariant(),
            EventSlug: slug,
            EventTitle: assetSymbol + " Up or Down",
            SeriesSlug: assetSymbol.ToLowerInvariant() + "-up-or-down-5m",
            Category: "Crypto",
            Active: true,
            Closed: false,
            Archived: false,
            Restricted: false,
            AcceptingOrders: acceptingOrders,
            EnableOrderBook: true,
            NegativeRisk: false,
            Liquidity: null,
            LiquidityClob: null,
            Volume: null,
            Volume24Hr: null,
            BestBid: 0.49m,
            BestAsk: 0.51m,
            Spread: 0.02m,
            CreatedAtUtc: marketStartUtc.AddDays(-1),
            UpdatedAtUtc: marketStartUtc.AddDays(-1),
            StartDateUtc: marketStartUtc,
            EndDateUtc: marketStartUtc.AddMinutes(5),
            EventStartTimeUtc: marketStartUtc,
            Outcomes: ["Up", "Down"],
            ClobTokenIds: ["token-up", "token-down"],
            RawJson: "{}",
            FetchedAtUtc: marketStartUtc.AddDays(-1),
            LastTradePrice: null,
            OrderMinSize: 5m,
            OrderPriceMinTickSize: 0.01m);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeFirstAcceptingProcessor : IPairedMakerGtdFirstAcceptingProcessor
    {
        public List<PairedMakerGtdFirstAcceptingCandidate> Candidates { get; } = [];

        public int DueCalls { get; private set; }

        public Action<PairedMakerGtdFirstAcceptingCandidate>? BeforeProcess { get; init; }

        public Task<PairedMakerGtdFirstAcceptingResult> ProcessFirstAcceptingMarketAsync(
            PairedMakerGtdFirstAcceptingCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            BeforeProcess?.Invoke(candidate);
            Candidates.Add(candidate);
            return Task.FromResult(new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1));
        }

        public Task<PairedMakerGtdFirstAcceptingResult> ProcessDueAsync(
            CancellationToken cancellationToken = default)
        {
            DueCalls++;
            return Task.FromResult(new PairedMakerGtdFirstAcceptingResult());
        }
    }

    private sealed class FakeGammaClient(IReadOnlyList<PolymarketGammaMarket> markets) : IPolymarketGammaClient
    {
        public List<(IReadOnlyList<string> Slugs, bool ActiveOnly)> Requests { get; } = [];

        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(
            int limit = 500,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>([]);
        }

        public Task<IReadOnlyList<PolymarketGammaMarket>> GetMarketsBySlugsAsync(
            IReadOnlyCollection<string> slugs,
            bool activeOnly = true,
            CancellationToken cancellationToken = default)
        {
            var request = slugs.ToArray();
            Requests.Add((request, activeOnly));
            return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>(
                markets.Where(market => request.Contains(market.Slug, StringComparer.OrdinalIgnoreCase)).ToArray());
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
