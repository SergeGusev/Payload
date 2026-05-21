using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class BtcUpDown5mArbitrageScannerProcessorTests
{
    [Fact]
    public async Task ProcessAsync_StoresCoveredArbitrageOpportunity()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(DateTimeOffset.UtcNow.AddMinutes(-1)));

        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        ApplyBook(cache, "up-token", [new OrderBookLevel(0.49m, 10m)]);
        ApplyBook(cache, "down-token", [new OrderBookLevel(0.49m, 10m)]);

        var processor = CreateProcessor(repository, cache);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsScanned);
        Assert.Equal(1, result.ScansStored);
        Assert.Equal(1, result.Opportunities);

        var scan = Assert.Single(repository.BtcUpDown5mArbitrageScans);
        Assert.True(scan.WouldArbitrage);
        Assert.Equal("covered_arbitrage", scan.DecisionCode);
        Assert.Equal(10m, scan.BestExecutableShares);
        Assert.Equal(9.8m, scan.TotalCostUsd);
        Assert.Equal(10m, scan.GuaranteedPayoutUsd);
        Assert.Equal(0.19m, scan.NetProfitUsd);
        Assert.Equal(0.02m, scan.EdgePerShare);
        Assert.Equal("websocket_cache", scan.UpBookSource);
        Assert.Equal("websocket_cache", scan.DownBookSource);
    }

    [Fact]
    public async Task ProcessAsync_StoresNoOpportunityWhenCoveredCostIsTooHigh()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(DateTimeOffset.UtcNow.AddMinutes(-1)));

        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        ApplyBook(cache, "up-token", [new OrderBookLevel(0.51m, 10m)]);
        ApplyBook(cache, "down-token", [new OrderBookLevel(0.50m, 10m)]);

        var processor = CreateProcessor(repository, cache);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.ScansStored);
        Assert.Equal(0, result.Opportunities);
        Assert.Equal(1, result.NoOpportunity);

        var scan = Assert.Single(repository.BtcUpDown5mArbitrageScans);
        Assert.False(scan.WouldArbitrage);
        Assert.Equal("no_covered_arbitrage", scan.DecisionCode);
        Assert.Equal(5m, scan.BestExecutableShares);
        Assert.Equal(-0.055m, scan.NetProfitUsd);
    }

    [Fact]
    public async Task ProcessAsync_UsesRestFallbackWhenCacheIsMissing()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(DateTimeOffset.UtcNow.AddMinutes(-1)));
        var clobClient = new FakeClobPublicClient
        {
            Books =
            {
                ["up-token"] = OrderBook("up-token", [new OrderBookLevel(0.49m, 10m)]),
                ["down-token"] = OrderBook("down-token", [new OrderBookLevel(0.49m, 10m)])
            }
        };

        var processor = CreateProcessor(repository, new MarketDataCache(new MarketDataWebSocketOptions()), clobClient: clobClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.Opportunities);
        Assert.Equal(2, clobClient.OrderBookCalls);
        var scan = Assert.Single(repository.BtcUpDown5mArbitrageScans);
        Assert.Equal("clob_rest", scan.UpBookSource);
        Assert.Equal("clob_rest", scan.DownBookSource);
    }

    [Fact]
    public async Task ProcessAsync_RecordsInsufficientDepth()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(DateTimeOffset.UtcNow.AddMinutes(-1)));

        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        ApplyBook(cache, "up-token", [new OrderBookLevel(0.49m, 2m)]);
        ApplyBook(cache, "down-token", [new OrderBookLevel(0.49m, 10m)]);

        var processor = CreateProcessor(repository, cache);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.InsufficientDepth);
        var scan = Assert.Single(repository.BtcUpDown5mArbitrageScans);
        Assert.Equal("insufficient_depth", scan.DecisionCode);
        Assert.Equal(5m, scan.RequiredMinShares);
        Assert.Equal(2m, scan.MaxCommonExecutableShares);
    }

    private static BtcUpDown5mArbitrageScannerProcessor CreateProcessor(
        TestAppRepository repository,
        MarketDataCache cache,
        BtcUpDown5mArbitrageScannerOptions? options = null,
        FakeClobPublicClient? clobClient = null)
    {
        return new BtcUpDown5mArbitrageScannerProcessor(
            NullLogger<BtcUpDown5mArbitrageScannerProcessor>.Instance,
            options ?? new BtcUpDown5mArbitrageScannerOptions(),
            repository,
            cache,
            clobClient ?? new FakeClobPublicClient());
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

    private static void ApplyBook(MarketDataCache cache, string assetId, IReadOnlyList<OrderBookLevel> asks)
    {
        var book = OrderBook(assetId, asks);
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

    private static OrderBookSnapshot OrderBook(string assetId, IReadOnlyList<OrderBookLevel> asks)
    {
        return new OrderBookSnapshot(
            assetId,
            [new OrderBookLevel(asks.Min(level => Math.Max(0.01m, level.Price - 0.02m)), 10m)],
            asks,
            DateTimeOffset.UtcNow,
            "condition-1",
            MinOrderSize: 5m,
            TickSize: 0.01m);
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
