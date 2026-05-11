using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class CryptoUpDown5mOddsArchiveProcessorTests
{
    [Fact]
    public async Task ProcessAsync_StoresConfiguredCryptoOddsTicks()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket("ETH", now.AddMinutes(-1)));
        repository.PolymarketGammaMarkets.Add(CreateMarket("SOL", now.AddMinutes(-1)));
        repository.PolymarketGammaMarkets.Add(CreateMarket("BTC", now.AddMinutes(-1)));

        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        ApplyBook(cache, "ETH-up-token", 0.54m, 0.56m);
        ApplyBook(cache, "ETH-down-token", 0.44m, 0.46m);
        ApplyBook(cache, "SOL-up-token", 0.49m, 0.51m);
        ApplyBook(cache, "SOL-down-token", 0.49m, 0.51m);

        var processor = CreateProcessor(
            repository,
            cache,
            new FakeCryptoReferencePriceClient(("ETH", 3200m), ("SOL", 150m)));

        var result = await processor.ProcessAsync();

        Assert.Equal(2, result.MarketsScanned);
        Assert.Equal(2, result.TicksStored);
        Assert.Collection(
            repository.CryptoUpDown5mOddsTicks.OrderBy(tick => tick.AssetSymbol),
            eth =>
            {
                Assert.Equal("ETH", eth.AssetSymbol);
                Assert.Equal("ETHUSDT", eth.BinanceSymbol);
                Assert.Equal(3200m, eth.BinancePriceUsd);
                Assert.Equal(3200m, eth.BinanceStartPriceUsd);
                Assert.Equal(0.55m, eth.UpMid);
                Assert.Equal("websocket_cache", eth.UpBookSource);
            },
            sol =>
            {
                Assert.Equal("SOL", sol.AssetSymbol);
                Assert.Equal("SOLUSDT", sol.BinanceSymbol);
                Assert.Equal(150m, sol.BinancePriceUsd);
                Assert.Equal(0.50m, sol.UpMid);
            });
    }

    [Fact]
    public async Task ProcessAsync_ReusesFirstArchivedPriceAsMarketStartReference()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket("XRP", now.AddMinutes(-1)));

        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        ApplyBook(cache, "XRP-up-token", 0.54m, 0.56m);
        ApplyBook(cache, "XRP-down-token", 0.44m, 0.46m);

        var priceClient = new FakeCryptoReferencePriceClient(("XRP", 0.50m));
        var processor = CreateProcessor(repository, cache, priceClient);
        await processor.ProcessAsync();

        priceClient.SetPrice("XRP", 0.55m);
        await processor.ProcessAsync();

        Assert.Equal(2, repository.CryptoUpDown5mOddsTicks.Count);
        var latest = repository.CryptoUpDown5mOddsTicks.OrderByDescending(tick => tick.SampledAtUtc).First();
        Assert.Equal(0.50m, latest.BinanceStartPriceUsd);
        Assert.Equal(0.05m, latest.AssetMoveFromStartUsd);
        Assert.Equal(1000m, latest.AssetMoveFromStartBps);
    }

    [Fact]
    public void BinanceCryptoTradeParser_ParsesCombinedStreamMessage()
    {
        var fetchedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_100);
        var json = """
        {"stream":"ethusdt@trade","data":{"s":"ETHUSDT","p":"3201.25","T":1700000000000}}
        """u8;

        var parsed = BinanceCryptoTradeParser.TryParse(json, fetchedAt, out var point, out var error);

        Assert.True(parsed, error);
        Assert.NotNull(point);
        Assert.Equal("ETH", point.AssetSymbol);
        Assert.Equal("ETHUSDT", point.BinanceSymbol);
        Assert.Equal(3201.25m, point.PriceUsd);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000), point.SourceUpdatedAtUtc);
        Assert.Equal(fetchedAt, point.FetchedAtUtc);
    }

    private static CryptoUpDown5mOddsArchiveProcessor CreateProcessor(
        TestAppRepository repository,
        MarketDataCache cache,
        FakeCryptoReferencePriceClient priceClient,
        FakeClobPublicClient? clobClient = null)
    {
        return new CryptoUpDown5mOddsArchiveProcessor(
            NullLogger<CryptoUpDown5mOddsArchiveProcessor>.Instance,
            new CryptoUpDown5mOddsArchiveOptions { AssetSymbols = ["ETH", "SOL", "XRP"] },
            repository,
            cache,
            clobClient ?? new FakeClobPublicClient(),
            priceClient);
    }

    private static PolymarketGammaMarket CreateMarket(string assetSymbol, DateTimeOffset startUtc)
    {
        var normalized = assetSymbol.ToUpperInvariant();
        var slugPrefix = normalized.ToLowerInvariant();
        return new PolymarketGammaMarket(
            normalized + "-market",
            normalized + "-condition",
            normalized + "-question",
            slugPrefix + "-updown-5m-" + startUtc.ToUnixTimeSeconds(),
            normalized + " Up or Down 5m",
            normalized + "-event",
            null,
            null,
            slugPrefix + "-up-or-down-5m",
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
            [normalized + "-up-token", normalized + "-down-token"],
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

    private sealed class FakeCryptoReferencePriceClient : ICryptoReferencePriceClient
    {
        private readonly Dictionary<string, decimal> prices = new(StringComparer.OrdinalIgnoreCase);

        public FakeCryptoReferencePriceClient(params (string AssetSymbol, decimal PriceUsd)[] initialPrices)
        {
            foreach (var price in initialPrices)
            {
                SetPrice(price.AssetSymbol, price.PriceUsd);
            }
        }

        public void SetPrice(string assetSymbol, decimal priceUsd)
        {
            prices[assetSymbol] = priceUsd;
        }

        public Task<CryptoReferencePricePoint> GetPriceAsync(
            string assetSymbol,
            CancellationToken cancellationToken = default)
        {
            if (!prices.TryGetValue(assetSymbol, out var priceUsd))
            {
                throw new InvalidOperationException("No price configured.");
            }

            var now = DateTimeOffset.UtcNow;
            var normalized = assetSymbol.ToUpperInvariant();
            return Task.FromResult(new CryptoReferencePricePoint(
                normalized,
                normalized + "USDT",
                priceUsd,
                now,
                now,
                "FakeBinance"));
        }
    }

    private sealed class FakeClobPublicClient : IPolymarketClobPublicClient
    {
        public Dictionary<string, OrderBookSnapshot> Books { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
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
