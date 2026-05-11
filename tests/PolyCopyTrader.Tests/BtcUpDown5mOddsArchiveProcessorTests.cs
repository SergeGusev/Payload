using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class BtcUpDown5mOddsArchiveProcessorTests
{
    [Fact]
    public async Task ProcessAsync_StoresOddsTickFromFreshWebSocketCache()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(now.AddMinutes(-1)));

        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        ApplyBook(cache, "up-token", 0.54m, 0.56m);
        ApplyBook(cache, "down-token", 0.44m, 0.46m);

        var btcClient = new FakeBtcUsdReferencePriceClient(101m);
        var processor = CreateProcessor(repository, cache, btcClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsScanned);
        Assert.Equal(1, result.TicksStored);
        var tick = Assert.Single(repository.BtcUpDown5mOddsTicks);
        Assert.Equal("market-1", tick.MarketId);
        Assert.Equal(101m, tick.BinancePriceUsd);
        Assert.Equal(101m, tick.BinanceStartPriceUsd);
        Assert.Equal(0m, tick.BtcMoveFromStartUsd);
        Assert.Equal(0.55m, tick.UpMid);
        Assert.Equal(0.55m, tick.UpPriceProxy);
        Assert.Equal("mid", tick.UpPriceProxyKind);
        Assert.Equal("websocket_cache", tick.UpBookSource);
        Assert.Equal(0.45m, tick.DownMid);
    }

    [Fact]
    public async Task ProcessAsync_ReusesFirstArchivedBtcPriceAsMarketStartReference()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(now.AddMinutes(-1)));

        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        ApplyBook(cache, "up-token", 0.54m, 0.56m);
        ApplyBook(cache, "down-token", 0.44m, 0.46m);

        var btcClient = new FakeBtcUsdReferencePriceClient(101m);
        var processor = CreateProcessor(repository, cache, btcClient);
        await processor.ProcessAsync();

        btcClient.PriceUsd = 103m;
        await processor.ProcessAsync();

        Assert.Equal(2, repository.BtcUpDown5mOddsTicks.Count);
        var latest = repository.BtcUpDown5mOddsTicks.OrderByDescending(tick => tick.SampledAtUtc).First();
        Assert.Equal(101m, latest.BinanceStartPriceUsd);
        Assert.Equal(2m, latest.BtcMoveFromStartUsd);
        Assert.True(latest.BtcMoveFromStartBps > 198m);
    }

    [Fact]
    public async Task ProcessAsync_FallsBackToClobRestWhenCacheIsMissing()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(DateTimeOffset.UtcNow.AddMinutes(-1)));

        var clobClient = new FakeClobPublicClient
        {
            Books =
            {
                ["up-token"] = OrderBook("up-token", 0.60m, 0.62m),
                ["down-token"] = OrderBook("down-token", 0.38m, 0.40m)
            }
        };
        var processor = CreateProcessor(
            repository,
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new FakeBtcUsdReferencePriceClient(100m),
            clobClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.TicksStored);
        Assert.Equal(2, clobClient.OrderBookCalls);
        var tick = Assert.Single(repository.BtcUpDown5mOddsTicks);
        Assert.Equal("clob_rest", tick.UpBookSource);
        Assert.Equal(0.61m, tick.UpMid);
        Assert.Equal(0.39m, tick.DownMid);
    }

    private static BtcUpDown5mOddsArchiveProcessor CreateProcessor(
        TestAppRepository repository,
        MarketDataCache cache,
        FakeBtcUsdReferencePriceClient btcClient,
        FakeClobPublicClient? clobClient = null)
    {
        return new BtcUpDown5mOddsArchiveProcessor(
            NullLogger<BtcUpDown5mOddsArchiveProcessor>.Instance,
            new BtcUpDown5mOddsArchiveOptions(),
            repository,
            cache,
            clobClient ?? new FakeClobPublicClient(),
            btcClient);
    }

    private static PolymarketGammaMarket CreateMarket(DateTimeOffset startUtc)
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
            true,
            false,
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
            """{"outcomePrices":["0.50","0.50"]}""",
            DateTimeOffset.UtcNow);
    }

    private static void ApplyBook(MarketDataCache cache, string assetId, decimal bid, decimal ask)
    {
        var book = OrderBook(assetId, bid, ask);
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

    private static OrderBookSnapshot OrderBook(string assetId, decimal bid, decimal ask)
    {
        return new OrderBookSnapshot(
            assetId,
            [new OrderBookLevel(bid, 10m)],
            [new OrderBookLevel(ask, 10m)],
            DateTimeOffset.UtcNow,
            "condition-1");
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
        public Dictionary<string, OrderBookSnapshot> Books { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int OrderBookCalls { get; private set; }

        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            OrderBookCalls++;
            return Task.FromResult(Books.GetValueOrDefault(assetId));
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
