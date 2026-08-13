using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Tests;

public sealed class MarketDataCacheTests
{
    [Fact]
    public void ApplyUpdate_AppliesPriceChangeDeltaWithoutDroppingDepth()
    {
        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        var now = DateTimeOffset.UtcNow;
        cache.ApplyUpdate(new MarketDataUpdate(
            MarketDataEventType.Book,
            "book",
            "asset-1",
            "condition-1",
            new OrderBookSnapshot(
                "asset-1",
                [new OrderBookLevel(0.49m, 20m), new OrderBookLevel(0.48m, 30m)],
                [new OrderBookLevel(0.52m, 25m), new OrderBookLevel(0.53m, 60m)],
                now,
                "condition-1"),
            0.49m,
            0.52m,
            null,
            null,
            TradeSide.Unknown,
            false,
            now));

        cache.ApplyUpdate(new MarketDataUpdate(
            MarketDataEventType.PriceChange,
            "price_change",
            "asset-1",
            "condition-1",
            null,
            0.49m,
            0.53m,
            0.52m,
            0m,
            TradeSide.Sell,
            false,
            now.AddMilliseconds(100)));

        var lookup = cache.GetOrderBook("asset-1", TimeSpan.FromSeconds(5));

        Assert.Equal(OrderBookCacheLookupStatus.Fresh, lookup.Status);
        Assert.NotNull(lookup.Snapshot);
        var snapshot = lookup.Snapshot;
        Assert.Equal(0.49m, snapshot.BestBid);
        Assert.Equal(0.53m, snapshot.BestAsk);
        Assert.DoesNotContain(snapshot.Asks, level => level.Price == 0.52m);
        Assert.Contains(snapshot.Asks, level => level is { Price: 0.53m, Size: 60m });
        Assert.Contains(snapshot.Bids, level => level is { Price: 0.48m, Size: 30m });
    }

    [Fact]
    public void ApplyUpdate_BestBidAskPreservesKnownExecutableDepth()
    {
        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        var now = DateTimeOffset.UtcNow;
        cache.ApplyUpdate(new MarketDataUpdate(
            MarketDataEventType.Book,
            "book",
            "asset-1",
            "condition-1",
            new OrderBookSnapshot(
                "asset-1",
                [new OrderBookLevel(0.49m, 20m), new OrderBookLevel(0.48m, 30m)],
                [new OrderBookLevel(0.52m, 25m), new OrderBookLevel(0.53m, 60m)],
                now,
                "condition-1"),
            0.49m,
            0.52m,
            null,
            null,
            TradeSide.Unknown,
            false,
            now));

        cache.ApplyUpdate(new MarketDataUpdate(
            MarketDataEventType.BestBidAsk,
            "best_bid_ask",
            "asset-1",
            "condition-1",
            null,
            0.49m,
            0.52m,
            null,
            null,
            TradeSide.Unknown,
            false,
            now.AddMilliseconds(100)));

        var lookup = cache.GetOrderBook("asset-1", TimeSpan.FromSeconds(5));
        Assert.NotNull(lookup.Snapshot);
        var snapshot = lookup.Snapshot;

        Assert.Contains(snapshot.Asks, level => level is { Price: 0.52m, Size: 25m });
        Assert.Contains(snapshot.Asks, level => level is { Price: 0.53m, Size: 60m });
        Assert.Contains(snapshot.Bids, level => level is { Price: 0.49m, Size: 20m });
    }
}
