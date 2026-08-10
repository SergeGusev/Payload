using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Tests;

public sealed class MarketDataCacheTests
{
    [Fact]
    public void UpdateStatus_RecoveredStaleGapAdvancesContinuityGeneration()
    {
        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        var now = DateTimeOffset.UtcNow;
        var healthy = new MarketDataStatusSnapshot(
            "PolymarketMarketWebSocket",
            MarketDataConnectionState.Connected,
            "wss://example.test",
            1,
            now,
            now.AddMinutes(-1),
            null,
            0,
            false,
            null,
            now);
        cache.UpdateStatus(healthy);

        cache.UpdateStatus(healthy with { Stale = true, UpdatedAtUtc = now.AddSeconds(1) });
        cache.UpdateStatus(healthy with { UpdatedAtUtc = now.AddSeconds(2) });

        Assert.Equal(1, cache.Status.ContinuityGeneration);
        Assert.False(cache.Status.Stale);
    }

    [Fact]
    public void ReplaceSubscribedAssets_RemoveAndReaddAdvancesOnlyRemovedAssetGeneration()
    {
        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        cache.ReplaceSubscribedAssets(["asset-1", "asset-2"]);

        cache.ReplaceSubscribedAssets(["asset-2"]);
        cache.ReplaceSubscribedAssets(["asset-1", "asset-2"]);

        Assert.Equal(1, cache.GetAssetSubscriptionGeneration("asset-1"));
        Assert.Equal(0, cache.GetAssetSubscriptionGeneration("asset-2"));
    }

    [Fact]
    public void ConfirmedSubscription_AssignedAssetRemainsPendingUntilOwningShardFrame()
    {
        var cache = new MarketDataCache(
            new MarketDataWebSocketOptions(),
            "market-data-test-session");
        var confirmedAtUtc = DateTimeOffset.UtcNow;
        var sourceTimestampUtc = confirmedAtUtc.AddMilliseconds(-1);

        cache.AssignAssetSubscriptions("shard-a", ["asset-1"]);
        var pending = cache.GetConfirmedAssetSubscription("asset-1");
        var wrongShardConfirmed = cache.ConfirmAssetSubscription("shard-b", "asset-1");
        var owningShardConfirmed = cache.ConfirmAssetSubscription(
            "shard-a",
            "asset-1",
            confirmedAtUtc,
            sourceTimestampUtc,
            "confirmation-frame-1");
        var confirmed = cache.GetConfirmedAssetSubscription("asset-1");

        Assert.False(pending.ConfirmedLive);
        Assert.Equal(0, pending.Generation);
        Assert.False(wrongShardConfirmed);
        Assert.True(owningShardConfirmed);
        Assert.True(confirmed.ConfirmedLive);
        Assert.Equal("shard-a", confirmed.Component);
        Assert.Equal(0, confirmed.Generation);
        Assert.Equal("market-data-test-session", pending.SessionId);
        Assert.Equal(pending.SessionId, confirmed.SessionId);
        Assert.Equal(confirmedAtUtc, confirmed.ConfirmedAtUtc);
        Assert.Equal(sourceTimestampUtc, confirmed.ConfirmationSourceTimestampUtc);
        Assert.Equal("confirmation-frame-1", confirmed.ConfirmationEventFingerprint);

        Assert.True(cache.ConfirmAssetSubscription(
            "shard-a",
            "asset-1",
            confirmedAtUtc.AddSeconds(1),
            sourceTimestampUtc.AddSeconds(1),
            "later-frame"));
        var repeated = cache.GetConfirmedAssetSubscription("asset-1");
        Assert.Equal(confirmed.ConfirmedAtUtc, repeated.ConfirmedAtUtc);
        Assert.Equal(
            confirmed.ConfirmationSourceTimestampUtc,
            repeated.ConfirmationSourceTimestampUtc);
        Assert.Equal(
            confirmed.ConfirmationEventFingerprint,
            repeated.ConfirmationEventFingerprint);
    }

    [Fact]
    public void ConfirmedSubscription_DisconnectThenFirstReconnectFrameAdvancesGeneration()
    {
        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        var firstConfirmedAtUtc = DateTimeOffset.UtcNow;
        var secondConfirmedAtUtc = firstConfirmedAtUtc.AddSeconds(1);
        cache.AssignAssetSubscriptions("shard-a", ["asset-1"]);
        Assert.True(cache.ConfirmAssetSubscription(
            "shard-a",
            "asset-1",
            firstConfirmedAtUtc,
            firstConfirmedAtUtc.AddMilliseconds(-1),
            "confirmation-frame-1"));
        var accepted = cache.GetConfirmedAssetSubscription("asset-1");

        cache.InvalidateAssetSubscriptions("shard-a");
        var disconnected = cache.GetConfirmedAssetSubscription("asset-1");
        Assert.True(cache.ConfirmAssetSubscription(
            "shard-a",
            "asset-1",
            secondConfirmedAtUtc,
            secondConfirmedAtUtc.AddMilliseconds(-1),
            "confirmation-frame-2"));
        var reconnected = cache.GetConfirmedAssetSubscription("asset-1");

        Assert.True(accepted.ConfirmedLive);
        Assert.Equal(0, accepted.Generation);
        Assert.False(disconnected.ConfirmedLive);
        Assert.Equal(1, disconnected.Generation);
        Assert.True(reconnected.ConfirmedLive);
        Assert.Equal(1, reconnected.Generation);
        Assert.Equal(secondConfirmedAtUtc, reconnected.ConfirmedAtUtc);
        Assert.Equal("confirmation-frame-2", reconnected.ConfirmationEventFingerprint);
    }

    [Fact]
    public void ConfirmedSubscription_UnrelatedShardDisconnectDoesNotInvalidateAsset()
    {
        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        cache.AssignAssetSubscriptions("shard-a", ["asset-1"]);
        cache.AssignAssetSubscriptions("shard-b", ["asset-2"]);
        Assert.True(cache.ConfirmAssetSubscription("shard-a", "asset-1"));
        Assert.True(cache.ConfirmAssetSubscription("shard-b", "asset-2"));

        cache.InvalidateAssetSubscriptions("shard-b");

        var unaffected = cache.GetConfirmedAssetSubscription("asset-1");
        Assert.True(unaffected.ConfirmedLive);
        Assert.Equal(0, unaffected.Generation);
    }

    [Fact]
    public void ConfirmedSubscription_LateFailureCannotInvalidateNewerSegment()
    {
        var cache = new MarketDataCache(
            new MarketDataWebSocketOptions(),
            "market-data-test-session");
        var now = DateTimeOffset.UtcNow;
        cache.AssignAssetSubscriptions("shard-a", ["asset-1"]);
        Assert.True(cache.ConfirmAssetSubscription(
            "shard-a",
            "asset-1",
            now,
            now.AddMilliseconds(-1),
            "segment-1"));
        var firstSegment = cache.GetConfirmedAssetSubscription("asset-1");
        Assert.True(cache.TryInvalidateAssetSubscription(firstSegment));
        Assert.True(cache.ConfirmAssetSubscription(
            "shard-a",
            "asset-1",
            now.AddSeconds(1),
            now.AddMilliseconds(999),
            "segment-2"));

        Assert.False(cache.TryInvalidateAssetSubscription(firstSegment));
        var current = cache.GetConfirmedAssetSubscription("asset-1");
        Assert.True(current.ConfirmedLive);
        Assert.Equal(1, current.Generation);
        Assert.Equal("segment-2", current.ConfirmationEventFingerprint);
    }

    [Fact]
    public void ConfirmedSubscription_AssetMoveRequiresNewOwningShardFence()
    {
        var cache = new MarketDataCache(new MarketDataWebSocketOptions(), "move-test-session");
        var now = DateTimeOffset.UtcNow;
        cache.AssignAssetSubscriptions("shard-a", ["asset-1"]);
        Assert.True(cache.ConfirmAssetSubscription(
            "shard-a",
            "asset-1",
            now,
            now.AddMilliseconds(-1),
            "shard-a-fence"));

        cache.AssignAssetSubscriptions("shard-b", ["asset-1"]);
        var movedPending = cache.GetConfirmedAssetSubscription("asset-1");
        Assert.False(movedPending.ConfirmedLive);
        Assert.Equal("shard-b", movedPending.Component);
        Assert.Equal(1, movedPending.Generation);
        Assert.Null(movedPending.ConfirmedAtUtc);
        Assert.True(cache.ConfirmAssetSubscription(
            "shard-b",
            "asset-1",
            now.AddSeconds(1),
            now.AddMilliseconds(999),
            "shard-b-fence"));

        var movedConfirmed = cache.GetConfirmedAssetSubscription("asset-1");
        Assert.True(movedConfirmed.ConfirmedLive);
        Assert.Equal("shard-b-fence", movedConfirmed.ConfirmationEventFingerprint);
    }

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
