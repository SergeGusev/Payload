using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class BtcUpDown5mStatisticsProcessorTests
{
    [Fact]
    public async Task ProcessAsync_StoresInsufficientHistoryTickAndQueuesObservation()
    {
        var repository = new TestAppRepository();
        var startUtc = DateTimeOffset.UtcNow.AddSeconds(-62);
        repository.PolymarketGammaMarkets.Add(CreateMarket(startUtc));
        AddStartPrice(repository, startUtc, 100m);
        AddHistoryRows(repository, 10, 8, 2);

        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        ApplyBook(cache, "up-token", 0.39m, 0.40m);
        ApplyBook(cache, "down-token", 0.59m, 0.60m);
        var processor = CreateProcessor(repository, cache, new FakeBtcUsdReferencePriceClient(100.12m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsScanned);
        Assert.Equal(1, result.TicksStored);
        Assert.Equal(1, result.SkippedInsufficientHistory);
        Assert.Equal(1, result.HistoryObservationsQueued);

        var tick = Assert.Single(repository.BtcUpDown5mStatisticsTicks);
        Assert.Equal("insufficient_history", tick.DecisionCode);
        Assert.False(tick.WouldBet);
        Assert.Equal(10m, tick.EffectiveCount);
        Assert.Equal(0.8m, tick.UpProbability);
        Assert.Equal(0.2m, tick.DownProbability);

        var observation = Assert.Single(repository.Btc5mHistoryLiveObservations);
        Assert.Equal(60, observation.Seconds);
        Assert.Equal(10, observation.Cents);
        Assert.False(observation.AppliedToHistory);
    }

    [Fact]
    public async Task ProcessAsync_RecordsWouldBetWhenProbabilityBeatsMarketPrice()
    {
        var repository = new TestAppRepository();
        var startUtc = DateTimeOffset.UtcNow.AddSeconds(-62);
        repository.PolymarketGammaMarkets.Add(CreateMarket(startUtc));
        AddStartPrice(repository, startUtc, 100m);
        AddHistoryRows(repository, 100, 80, 20);

        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        ApplyBook(cache, "up-token", 0.39m, 0.40m);
        ApplyBook(cache, "down-token", 0.59m, 0.60m);
        var processor = CreateProcessor(repository, cache, new FakeBtcUsdReferencePriceClient(100.12m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.WouldBet);
        Assert.Equal(0, result.SkippedInsufficientHistory);

        var tick = Assert.Single(repository.BtcUpDown5mStatisticsTicks);
        Assert.Equal("up_above_market", tick.DecisionCode);
        Assert.True(tick.WouldBet);
        Assert.Equal("Up", tick.RecommendedOutcome);
        Assert.Equal(0.8m, tick.UpProbability);
        Assert.Equal(0.40m, tick.UpMarketPrice);
        Assert.Equal(0.40m, tick.UpEdge);
    }

    [Fact]
    public async Task ProcessAsync_AppliesResolvedLiveObservationToHistory()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var startUtc = now.AddMinutes(-10);
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startUtc,
            active: false,
            closed: true,
            rawJson: """{"outcomes":["Up","Down"],"outcomePrices":["1","0"]}"""));
        repository.Btc5mHistoryRows.Add(new Btc5mHistoryRow(60, 10, 2, 1, 1));
        repository.Btc5mHistoryLiveObservations.Add(new Btc5mHistoryLiveObservation(
            Guid.NewGuid(),
            "market-1",
            "condition-1",
            "btc-updown-5m-" + startUtc.ToUnixTimeSeconds(),
            startUtc,
            startUtc.AddMinutes(5),
            startUtc.AddSeconds(62),
            60,
            10,
            100.12m,
            100m,
            0.12m,
            null,
            false,
            null,
            0,
            now.AddMinutes(-1),
            null,
            now.AddMinutes(-1),
            now.AddMinutes(-1)));
        var processor = CreateProcessor(
            repository,
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new FakeBtcUsdReferencePriceClient(100.50m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.HistoryObservationsApplied);
        var history = Assert.Single(repository.Btc5mHistoryRows);
        Assert.Equal(3, history.Count);
        Assert.Equal(2, history.UpCount);
        Assert.Equal(1, history.DownCount);
        Assert.True(Assert.Single(repository.Btc5mHistoryLiveObservations).AppliedToHistory);
    }

    private static BtcUpDown5mStatisticsProcessor CreateProcessor(
        TestAppRepository repository,
        MarketDataCache cache,
        FakeBtcUsdReferencePriceClient btcClient,
        FakeClobPublicClient? clobClient = null)
    {
        return new BtcUpDown5mStatisticsProcessor(
            NullLogger<BtcUpDown5mStatisticsProcessor>.Instance,
            new BtcUpDown5mStatisticsOptions
            {
                RestFallbackEnabled = false,
                MinHistorySupport = 20,
                MinimumEdge = 0m
            },
            repository,
            cache,
            clobClient ?? new FakeClobPublicClient(),
            btcClient,
            new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance, repository));
    }

    private static PolymarketGammaMarket CreateMarket(
        DateTimeOffset startUtc,
        bool active = true,
        bool closed = false,
        string rawJson = """{"outcomes":["Up","Down"],"outcomePrices":["0.50","0.50"]}""")
    {
        return new PolymarketGammaMarket(
            "market-1",
            "condition-1",
            "question-1",
            "btc-updown-5m-" + startUtc.ToUnixTimeSeconds(),
            "BTC Up or Down 5m",
            "event-1",
            null,
            null,
            "btc-up-or-down-5m",
            "Crypto",
            active,
            closed,
            false,
            false,
            true,
            true,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            startUtc,
            startUtc,
            startUtc,
            startUtc.AddMinutes(5),
            startUtc,
            ["Up", "Down"],
            ["up-token", "down-token"],
            rawJson,
            DateTimeOffset.UtcNow);
    }

    private static void AddStartPrice(TestAppRepository repository, DateTimeOffset startUtc, decimal price)
    {
        repository.BtcUpDown5mOddsTicks.Add(new BtcUpDown5mOddsTick(
            Guid.NewGuid(),
            "market-1",
            "condition-1",
            "btc-updown-5m-" + startUtc.ToUnixTimeSeconds(),
            startUtc,
            startUtc.AddMinutes(5),
            startUtc,
            0m,
            300m,
            price,
            startUtc,
            startUtc,
            price,
            0m,
            0m,
            "up-token",
            null,
            null,
            null,
            null,
            "missing",
            null,
            "test",
            null,
            "down-token",
            null,
            null,
            null,
            null,
            "missing",
            null,
            "test",
            null,
            "{}",
            startUtc));
    }

    private static void AddHistoryRows(TestAppRepository repository, int count, int upCount, int downCount)
    {
        repository.Btc5mHistoryRows.AddRange(
        [
            new Btc5mHistoryRow(60, 10, count, upCount, downCount),
            new Btc5mHistoryRow(65, 10, count, upCount, downCount),
            new Btc5mHistoryRow(60, 15, count, upCount, downCount),
            new Btc5mHistoryRow(65, 15, count, upCount, downCount)
        ]);
    }

    private static void ApplyBook(MarketDataCache cache, string assetId, decimal bid, decimal ask)
    {
        var book = new OrderBookSnapshot(
            assetId,
            [new OrderBookLevel(bid, 10m)],
            [new OrderBookLevel(ask, 10m)],
            DateTimeOffset.UtcNow,
            "condition-1");
        cache.ApplyUpdate(new MarketDataUpdate(
            MarketDataEventType.Book,
            "book",
            assetId,
            book.ConditionId,
            book,
            book.BestBid,
            book.BestAsk,
            null,
            null,
            TradeSide.Unknown,
            false,
            book.SnapshotAtUtc));
    }

    private sealed class FakeBtcUsdReferencePriceClient(decimal initialPriceUsd) : IBtcUsdReferencePriceClient
    {
        public decimal PriceUsd { get; set; } = initialPriceUsd;

        public Task<BtcUsdReferencePricePoint> GetBtcUsdPriceAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new BtcUsdReferencePricePoint(PriceUsd, now, now, "FakeBinance"));
        }
    }

    private sealed class FakeClobPublicClient : IPolymarketClobPublicClient
    {
        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OrderBookSnapshot?>(null);
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
