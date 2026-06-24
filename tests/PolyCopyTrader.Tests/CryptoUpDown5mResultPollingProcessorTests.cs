using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class CryptoUpDown5mResultPollingProcessorTests
{
    [Fact]
    public async Task ProcessAsync_PollsEndedMarketAndStoresWinnerDelay()
    {
        var repository = new TestAppRepository();
        var startUtc = FloorToFiveMinutes(DateTimeOffset.UtcNow).AddMinutes(-10);
        var openMarket = CreateCryptoMarket("SOL", startUtc, closed: false, winningOutcome: null);
        var closedMarket = CreateCryptoMarket("SOL", startUtc, closed: true, winningOutcome: "Up");
        repository.PolymarketGammaMarkets.Add(openMarket);
        var gammaClient = new FakeGammaClient([closedMarket]);
        var processor = CreateProcessor(repository, gammaClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.Candidates);
        Assert.Equal(1, result.PollsSent);
        Assert.Equal(1, result.ResultsFound);
        Assert.Equal([openMarket.Slug], gammaClient.RequestedSlugs);
        var observation = Assert.Single(repository.CryptoUpDown5mResultPollingObservations);
        Assert.Equal("SOL", observation.AssetSymbol);
        Assert.Equal("Resolved", observation.Status);
        Assert.Equal("winner_found", observation.LastResponseStatus);
        Assert.Equal("Up", observation.WinningOutcome);
        Assert.Equal(1, observation.PollAttempts);
        Assert.NotNull(observation.FirstClosedAtUtc);
        Assert.NotNull(observation.FirstWinnerAtUtc);
        Assert.NotNull(observation.ResultDelaySeconds);
        Assert.True(observation.ResultDelaySeconds > 0m);
        Assert.Empty(repository.StrategyMarketPaperRuns);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_ReusesPendingObservationUntilWinnerAppears()
    {
        var repository = new TestAppRepository();
        var startUtc = FloorToFiveMinutes(DateTimeOffset.UtcNow).AddMinutes(-10);
        var openMarket = CreateCryptoMarket("BTC", startUtc, closed: false, winningOutcome: null);
        var closedMarket = CreateCryptoMarket("BTC", startUtc, closed: true, winningOutcome: "Down");
        repository.PolymarketGammaMarkets.Add(openMarket);
        var gammaClient = new FakeGammaClient([null, closedMarket]);
        var processor = CreateProcessor(repository, gammaClient);

        var first = await processor.ProcessAsync();
        var second = await processor.ProcessAsync();

        Assert.Equal(1, first.PollsSent);
        Assert.Equal(0, first.ResultsFound);
        Assert.Equal(1, second.PollsSent);
        Assert.Equal(1, second.ResultsFound);
        var observation = Assert.Single(repository.CryptoUpDown5mResultPollingObservations);
        Assert.Equal("BTC", observation.AssetSymbol);
        Assert.Equal("Resolved", observation.Status);
        Assert.Equal("Down", observation.WinningOutcome);
        Assert.Equal(2, observation.PollAttempts);
        Assert.Equal(2, gammaClient.RequestedSlugs.Count);
        Assert.Empty(repository.StrategyMarketPaperRuns);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_StoresProvisionalOrderBookResultAtSixtyForty()
    {
        var repository = new TestAppRepository();
        var startUtc = FloorToFiveMinutes(DateTimeOffset.UtcNow).AddMinutes(-5);
        var market = CreateCryptoMarket("ETH", startUtc, closed: false, winningOutcome: null);
        repository.PolymarketGammaMarkets.Add(market);
        var gammaClient = new FakeGammaClient([null]);
        var clobClient = new FakeClobPublicClient(
            new Dictionary<string, OrderBookSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [market.ClobTokenIds[0]] = OrderBook(market.ClobTokenIds[0], 0.60m, 0.63m),
                [market.ClobTokenIds[1]] = OrderBook(market.ClobTokenIds[1], 0.40m, 0.43m)
            });
        var processor = CreateProcessor(repository, gammaClient, clobClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.PollsSent);
        Assert.Equal(0, result.ResultsFound);
        var resolved = Assert.Single(repository.CryptoUpDown5mWebSocketResolvedMarkets);
        Assert.Equal("ETH", resolved.AssetSymbol);
        Assert.Equal(startUtc, resolved.MarketStartUtc);
        Assert.Equal("Up", resolved.WinningOutcome);
        Assert.Equal("TerminalOrderBook", resolved.Source);
        Assert.Equal("terminal_order_book_provisional", resolved.RawEventType);
        Assert.Contains("\"winner_bid_min\":0.60", resolved.RawJson, StringComparison.Ordinal);
        Assert.Contains("\"loser_ask_max\":0.40", resolved.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_StoresReferenceStartEndResultWithoutGammaPolling()
    {
        var repository = new TestAppRepository();
        var startUtc = FloorToFiveMinutes(DateTimeOffset.UtcNow).AddMinutes(-10);
        var market = CreateCryptoMarket("ETH", startUtc, closed: false, winningOutcome: null);
        repository.PolymarketGammaMarkets.Add(market);
        AddCryptoOddsTick(repository, market, "ETH", startUtc.AddSeconds(1), startPrice: 3200m, price: 3200m);
        AddCryptoOddsTick(repository, market, "ETH", startUtc.AddMinutes(5).AddSeconds(-1), startPrice: 3200m, price: 3204m);
        var gammaClient = new FakeGammaClient([null]);
        var processor = CreateProcessor(repository, gammaClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.Candidates);
        Assert.Equal(1, result.PollsSent);
        Assert.Equal(1, result.ResultsFound);
        Assert.Empty(gammaClient.RequestedSlugs);
        var observation = Assert.Single(repository.CryptoUpDown5mResultPollingObservations);
        Assert.Equal("Resolved", observation.Status);
        Assert.Equal("reference_start_end_result", observation.LastResponseStatus);
        Assert.Equal("Up", observation.WinningOutcome);
        var resolved = Assert.Single(repository.CryptoUpDown5mWebSocketResolvedMarkets);
        Assert.Equal("ETH", resolved.AssetSymbol);
        Assert.Equal(startUtc, resolved.MarketStartUtc);
        Assert.Equal("Up", resolved.WinningOutcome);
        Assert.Equal("ReferenceStartEnd", resolved.Source);
        Assert.Equal("reference_start_end", resolved.RawEventType);
        Assert.Contains("\"start_price_usd\":3200", resolved.RawJson, StringComparison.Ordinal);
        Assert.Contains("\"end_price_usd\":3204", resolved.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_StoresReferenceStartEndResultFromBtcOddsTicks()
    {
        var repository = new TestAppRepository();
        var startUtc = FloorToFiveMinutes(DateTimeOffset.UtcNow).AddMinutes(-10);
        var market = CreateCryptoMarket("BTC", startUtc, closed: false, winningOutcome: null);
        repository.PolymarketGammaMarkets.Add(market);
        AddBtcOddsTick(repository, market, startUtc.AddSeconds(1), startPrice: 65000m, price: 65000m);
        AddBtcOddsTick(repository, market, startUtc.AddMinutes(5).AddSeconds(-1), startPrice: 65000m, price: 64990m);
        var gammaClient = new FakeGammaClient([null]);
        var processor = CreateProcessor(repository, gammaClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.ResultsFound);
        Assert.Empty(gammaClient.RequestedSlugs);
        var resolved = Assert.Single(repository.CryptoUpDown5mWebSocketResolvedMarkets);
        Assert.Equal("BTC", resolved.AssetSymbol);
        Assert.Equal("Down", resolved.WinningOutcome);
        Assert.Equal("ReferenceStartEnd", resolved.Source);
    }

    private static CryptoUpDown5mResultPollingProcessor CreateProcessor(
        TestAppRepository repository,
        FakeGammaClient gammaClient,
        FakeClobPublicClient? clobClient = null)
    {
        return new CryptoUpDown5mResultPollingProcessor(
            NullLogger<CryptoUpDown5mResultPollingProcessor>.Instance,
            new CryptoUpDown5mResultPollingOptions
            {
                Enabled = true,
                AssetSymbols = ["BTC", "ETH", "SOL"],
                PollIntervalSeconds = 5,
                MaxMarketsPerCycle = 100,
                MaxMarketAgeMinutes = 60,
                MaxResultWaitMinutes = 20,
                ReferencePriceResultEnabled = true,
                ReferencePriceResultMaxEndAgeMilliseconds = 15_000,
                ReferencePriceResultMinSamples = 2,
                ProvisionalOrderBookResultEnabled = true,
                ProvisionalWinnerBidMin = 0.60m,
                ProvisionalLoserAskMax = 0.40m,
                ProvisionalMaxOrderBookAgeMilliseconds = 15_000,
                ProvisionalRestFallbackEnabled = true,
                ProvisionalRestRequestTimeoutSeconds = 3
            },
            repository,
            gammaClient,
            clobClient ?? new FakeClobPublicClient(new Dictionary<string, OrderBookSnapshot>(StringComparer.OrdinalIgnoreCase)),
            new MarketDataCache(new MarketDataWebSocketOptions()));
    }

    private static PolymarketGammaMarket CreateCryptoMarket(
        string assetSymbol,
        DateTimeOffset startUtc,
        bool closed,
        string? winningOutcome)
    {
        var normalized = assetSymbol.Trim().ToLowerInvariant();
        var marketId = normalized + "-" + startUtc.ToUnixTimeSeconds();
        var prices = winningOutcome switch
        {
            "Up" => """["1","0"]""",
            "Down" => """["0","1"]""",
            _ => """["0.5","0.5"]"""
        };

        return new PolymarketGammaMarket(
            MarketId: marketId,
            ConditionId: "condition-" + marketId,
            QuestionId: "question-" + marketId,
            Slug: normalized + "-updown-5m-" + startUtc.ToUnixTimeSeconds(),
            Question: assetSymbol.ToUpperInvariant() + " Up or Down 5m",
            EventId: "event-" + marketId,
            EventSlug: normalized + "-updown-5m-" + startUtc.ToUnixTimeSeconds(),
            EventTitle: assetSymbol.ToUpperInvariant() + " Up or Down 5m",
            SeriesSlug: normalized + "-up-or-down-5m",
            Category: "Crypto",
            Active: !closed,
            Closed: closed,
            Archived: false,
            Restricted: false,
            AcceptingOrders: !closed,
            EnableOrderBook: true,
            NegativeRisk: false,
            Liquidity: 100m,
            LiquidityClob: 100m,
            Volume: 1000m,
            Volume24Hr: 100m,
            BestBid: 0.49m,
            BestAsk: 0.51m,
            Spread: 0.02m,
            CreatedAtUtc: startUtc.AddMinutes(-1),
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            StartDateUtc: startUtc,
            EndDateUtc: startUtc.AddMinutes(5),
            EventStartTimeUtc: startUtc,
            Outcomes: ["Up", "Down"],
            ClobTokenIds: ["asset-up-" + marketId, "asset-down-" + marketId],
            RawJson: """{"outcomePrices":""" + prices + "}",
            FetchedAtUtc: DateTimeOffset.UtcNow,
            LastTradePrice: null,
            OrderMinSize: 5m,
            OrderPriceMinTickSize: 0.01m);
    }

    private static DateTimeOffset FloorToFiveMinutes(DateTimeOffset value)
    {
        var unixSeconds = value.ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds - (unixSeconds % 300));
    }

    private static OrderBookSnapshot OrderBook(string assetId, decimal bestBid, decimal bestAsk)
    {
        return new OrderBookSnapshot(
            assetId,
            [new OrderBookLevel(bestBid, 10m)],
            [new OrderBookLevel(bestAsk, 10m)],
            DateTimeOffset.UtcNow);
    }

    private static void AddCryptoOddsTick(
        TestAppRepository repository,
        PolymarketGammaMarket market,
        string assetSymbol,
        DateTimeOffset sampledAtUtc,
        decimal startPrice,
        decimal price)
    {
        repository.CryptoUpDown5mOddsTicks.Add(new CryptoUpDown5mOddsTick(
            Guid.NewGuid(),
            assetSymbol,
            assetSymbol.ToUpperInvariant() + "USDT",
            market.MarketId,
            market.ConditionId,
            market.Slug,
            market.StartDateUtc!.Value,
            market.EndDateUtc!.Value,
            sampledAtUtc,
            Convert.ToDecimal((sampledAtUtc - market.StartDateUtc.Value).TotalSeconds),
            Convert.ToDecimal((market.EndDateUtc.Value - sampledAtUtc).TotalSeconds),
            price,
            sampledAtUtc,
            sampledAtUtc,
            startPrice,
            price - startPrice,
            startPrice == 0m ? 0m : (price - startPrice) / startPrice * 10_000m,
            market.ClobTokenIds[0],
            null,
            null,
            null,
            null,
            "missing",
            null,
            "missing",
            null,
            market.ClobTokenIds[1],
            null,
            null,
            null,
            null,
            "missing",
            null,
            "missing",
            null,
            "{}",
            sampledAtUtc));
    }

    private static void AddBtcOddsTick(
        TestAppRepository repository,
        PolymarketGammaMarket market,
        DateTimeOffset sampledAtUtc,
        decimal startPrice,
        decimal price)
    {
        repository.BtcUpDown5mOddsTicks.Add(new BtcUpDown5mOddsTick(
            Guid.NewGuid(),
            market.MarketId,
            market.ConditionId,
            market.Slug,
            market.StartDateUtc!.Value,
            market.EndDateUtc!.Value,
            sampledAtUtc,
            Convert.ToDecimal((sampledAtUtc - market.StartDateUtc.Value).TotalSeconds),
            Convert.ToDecimal((market.EndDateUtc.Value - sampledAtUtc).TotalSeconds),
            price,
            sampledAtUtc,
            sampledAtUtc,
            startPrice,
            price - startPrice,
            startPrice == 0m ? 0m : (price - startPrice) / startPrice * 10_000m,
            market.ClobTokenIds[0],
            null,
            null,
            null,
            null,
            "missing",
            null,
            "missing",
            null,
            market.ClobTokenIds[1],
            null,
            null,
            null,
            null,
            "missing",
            null,
            "missing",
            null,
            "{}",
            sampledAtUtc));
    }

    private sealed class FakeGammaClient(IReadOnlyList<PolymarketGammaMarket?> closedMarketResponses) : IPolymarketGammaClient
    {
        private readonly Queue<PolymarketGammaMarket?> responses = new(closedMarketResponses);

        public List<string> RequestedSlugs { get; } = [];

        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(
            int limit = 500,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>([]);
        }

        public Task<PolymarketGammaMarket?> GetClosedMarketBySlugAsync(
            string slug,
            CancellationToken cancellationToken = default)
        {
            RequestedSlugs.Add(slug);
            return Task.FromResult(responses.Count == 0 ? null : responses.Dequeue());
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

    private sealed class FakeClobPublicClient(IReadOnlyDictionary<string, OrderBookSnapshot> books) : IPolymarketClobPublicClient
    {
        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(books.TryGetValue(assetId, out var book) ? book : null);
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(string tokenId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PolymarketClobMarketByToken?>(null);
        }
    }
}
