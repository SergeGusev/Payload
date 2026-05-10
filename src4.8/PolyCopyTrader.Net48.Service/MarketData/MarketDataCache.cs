using System.Collections.Concurrent;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Service.MarketData;

public sealed class MarketDataCache(MarketDataWebSocketOptions options) : IMarketDataCache
{
    private const string ComponentName = "PolymarketMarketWebSocket";
    private readonly ConcurrentDictionary<string, OrderBookSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();
    private HashSet<string> subscribedAssetIds = new(StringComparer.OrdinalIgnoreCase);
    private MarketDataStatusSnapshot status = new(
        ComponentName,
        MarketDataConnectionState.Disabled,
        options.MarketEndpointUrl,
        0,
        null,
        null,
        null,
        0,
        false,
        null,
        DateTimeOffset.UtcNow);

    public IReadOnlyCollection<string> SubscribedAssetIds
    {
        get
        {
            lock (sync)
            {
                return subscribedAssetIds.ToArray();
            }
        }
    }

    public MarketDataStatusSnapshot Status
    {
        get
        {
            lock (sync)
            {
                return status;
            }
        }
    }

    public void ReplaceSubscribedAssets(IReadOnlyCollection<string> assetIds)
    {
        lock (sync)
        {
            subscribedAssetIds = assetIds
                .Where(assetId => !string.IsNullOrWhiteSpace(assetId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void ApplyUpdate(MarketDataUpdate update)
    {
        var assetId = update.OrderBookSnapshot?.AssetId ?? update.AssetId;
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return;
        }

        var initial = BuildInitialSnapshot(assetId, update);
        if (initial is null)
        {
            return;
        }

        snapshots.AddOrUpdate(assetId, initial, (_, existing) =>
            update.TimestampUtc >= existing.SnapshotAtUtc ? ApplyUpdateToExisting(existing, update) : existing);
    }

    public bool TryGetFreshOrderBook(string assetId, TimeSpan maxAge, out OrderBookSnapshot snapshot)
    {
        var lookup = GetOrderBook(assetId, maxAge);
        if (lookup.Status == OrderBookCacheLookupStatus.Fresh && lookup.Snapshot is not null)
        {
            snapshot = lookup.Snapshot;
            return true;
        }

        snapshot = default!;
        return false;
    }

    public OrderBookCacheLookup GetOrderBook(string assetId, TimeSpan maxAge)
    {
        if (!snapshots.TryGetValue(assetId, out var candidate))
        {
            return new OrderBookCacheLookup(OrderBookCacheLookupStatus.Missing, null, null);
        }

        var age = DateTimeOffset.UtcNow - candidate.SnapshotAtUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age <= maxAge
            ? new OrderBookCacheLookup(OrderBookCacheLookupStatus.Fresh, candidate, age)
            : new OrderBookCacheLookup(OrderBookCacheLookupStatus.Stale, candidate, age);
    }

    public void UpdateStatus(MarketDataStatusSnapshot nextStatus)
    {
        lock (sync)
        {
            status = nextStatus;
        }
    }

    private static OrderBookSnapshot? BuildInitialSnapshot(string assetId, MarketDataUpdate update)
    {
        if (update.EventType == MarketDataEventType.PriceChange)
        {
            var bids = new List<OrderBookLevel>();
            var asks = new List<OrderBookLevel>();
            if (update.BestBid is { } bid)
            {
                bids.Add(new OrderBookLevel(bid, 0m));
            }

            if (update.BestAsk is { } ask)
            {
                asks.Add(new OrderBookLevel(ask, 0m));
            }

            if (update.Price is { } price && update.Size is { } size && size > 0m)
            {
                if (update.Side == TradeSide.Buy)
                {
                    SetLevel(bids, price, size);
                }
                else if (update.Side == TradeSide.Sell)
                {
                    SetLevel(asks, price, size);
                }
            }

            ReconcileTopOfBook(bids, asks, update.BestBid, update.BestAsk);
            return new OrderBookSnapshot(
                assetId,
                NormalizeBids(bids),
                NormalizeAsks(asks),
                update.TimestampUtc,
                update.ConditionId);
        }

        if (update.OrderBookSnapshot is { } snapshot)
        {
            return snapshot.AssetId.Equals(assetId, StringComparison.OrdinalIgnoreCase)
                ? snapshot
                : snapshot with { AssetId = assetId };
        }

        if (update.EventType != MarketDataEventType.LastTradePrice)
        {
            return null;
        }

        return new OrderBookSnapshot(
            assetId,
            [],
            [],
            update.TimestampUtc,
            update.ConditionId,
            LastTradePrice: update.Price);
    }

    private static OrderBookSnapshot ApplyUpdateToExisting(OrderBookSnapshot existing, MarketDataUpdate update)
    {
        return update.EventType switch
        {
            MarketDataEventType.Book when update.OrderBookSnapshot is { } snapshot => MergeBookSnapshot(existing, snapshot),
            MarketDataEventType.PriceChange => ApplyPriceChange(existing, update),
            MarketDataEventType.BestBidAsk => ApplyBestBidAsk(existing, update),
            MarketDataEventType.LastTradePrice => existing with
            {
                SnapshotAtUtc = update.TimestampUtc,
                LastTradePrice = update.Price ?? existing.LastTradePrice
            },
            _ when update.OrderBookSnapshot is { } snapshot => MergeBookSnapshot(existing, snapshot),
            _ => existing
        };
    }

    private static OrderBookSnapshot MergeBookSnapshot(OrderBookSnapshot existing, OrderBookSnapshot snapshot)
    {
        return snapshot with
        {
            AssetId = string.IsNullOrWhiteSpace(snapshot.AssetId) ? existing.AssetId : snapshot.AssetId,
            ConditionId = snapshot.ConditionId ?? existing.ConditionId,
            MinOrderSize = snapshot.MinOrderSize ?? existing.MinOrderSize,
            TickSize = snapshot.TickSize ?? existing.TickSize,
            LastTradePrice = snapshot.LastTradePrice ?? existing.LastTradePrice
        };
    }

    private static OrderBookSnapshot ApplyPriceChange(OrderBookSnapshot existing, MarketDataUpdate update)
    {
        var bids = existing.Bids.ToList();
        var asks = existing.Asks.ToList();

        if (update.Price is { } price && update.Size is { } size)
        {
            if (update.Side == TradeSide.Buy)
            {
                SetLevel(bids, price, size);
            }
            else if (update.Side == TradeSide.Sell)
            {
                SetLevel(asks, price, size);
            }
        }

        ReconcileTopOfBook(bids, asks, update.BestBid, update.BestAsk);

        return existing with
        {
            Bids = NormalizeBids(bids),
            Asks = NormalizeAsks(asks),
            SnapshotAtUtc = update.TimestampUtc,
            ConditionId = update.ConditionId ?? existing.ConditionId
        };
    }

    private static OrderBookSnapshot ApplyBestBidAsk(OrderBookSnapshot existing, MarketDataUpdate update)
    {
        var bids = existing.Bids.ToList();
        var asks = existing.Asks.ToList();
        ReconcileTopOfBook(bids, asks, update.BestBid, update.BestAsk);

        return existing with
        {
            Bids = NormalizeBids(bids),
            Asks = NormalizeAsks(asks),
            SnapshotAtUtc = update.TimestampUtc,
            ConditionId = update.ConditionId ?? existing.ConditionId
        };
    }

    private static void SetLevel(List<OrderBookLevel> levels, decimal price, decimal size)
    {
        levels.RemoveAll(level => level.Price == price);
        if (size > 0m)
        {
            levels.Add(new OrderBookLevel(price, size));
        }
    }

    private static void ReconcileTopOfBook(
        List<OrderBookLevel> bids,
        List<OrderBookLevel> asks,
        decimal? bestBid,
        decimal? bestAsk)
    {
        if (bestBid is { } bid)
        {
            bids.RemoveAll(level => level.Price > bid);
            if (bids.All(level => level.Price != bid))
            {
                bids.Add(new OrderBookLevel(bid, 0m));
            }
        }

        if (bestAsk is { } ask)
        {
            asks.RemoveAll(level => level.Price < ask);
            if (asks.All(level => level.Price != ask))
            {
                asks.Add(new OrderBookLevel(ask, 0m));
            }
        }
    }

    private static IReadOnlyList<OrderBookLevel> NormalizeBids(IEnumerable<OrderBookLevel> levels)
    {
        return levels
            .GroupBy(level => level.Price)
            .Select(group => new OrderBookLevel(group.Key, group.Max(level => level.Size)))
            .OrderByDescending(level => level.Price)
            .ToArray();
    }

    private static IReadOnlyList<OrderBookLevel> NormalizeAsks(IEnumerable<OrderBookLevel> levels)
    {
        return levels
            .GroupBy(level => level.Price)
            .Select(group => new OrderBookLevel(group.Key, group.Max(level => level.Size)))
            .OrderBy(level => level.Price)
            .ToArray();
    }
}
