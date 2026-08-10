using System.Collections.Concurrent;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Service.MarketData;

public sealed class MarketDataCache(
    MarketDataWebSocketOptions options,
    string? confirmedSubscriptionSessionId = null) : IMarketDataCache
{
    private const string ComponentName = "PolymarketMarketWebSocket";
    private readonly ConcurrentDictionary<string, OrderBookSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();
    private HashSet<string> subscribedAssetIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> assetSubscriptionGenerations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ConfirmedAssetState> confirmedAssetSubscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> assignedAssetsByComponent =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string confirmedSubscriptionSessionId = string.IsNullOrWhiteSpace(confirmedSubscriptionSessionId)
        ? Guid.NewGuid().ToString("D")
        : confirmedSubscriptionSessionId.Trim();
    private long continuityGeneration;
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
            var nextAssetIds = assetIds
                .Where(assetId => !string.IsNullOrWhiteSpace(assetId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var removedAssetId in subscribedAssetIds.Except(nextAssetIds, StringComparer.OrdinalIgnoreCase))
            {
                assetSubscriptionGenerations[removedAssetId] =
                    GetAssetSubscriptionGenerationUnsafe(removedAssetId) + 1;
            }

            subscribedAssetIds = nextAssetIds;
        }
    }

    public long GetAssetSubscriptionGeneration(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return -1;
        }

        lock (sync)
        {
            return GetAssetSubscriptionGenerationUnsafe(assetId.Trim());
        }
    }

    public ConfirmedAssetSubscriptionSnapshot GetConfirmedAssetSubscription(string assetId)
    {
        var normalizedAssetId = assetId?.Trim() ?? string.Empty;
        if (normalizedAssetId.Length == 0)
        {
            return new ConfirmedAssetSubscriptionSnapshot(
                string.Empty,
                null,
                false,
                -1,
                confirmedSubscriptionSessionId,
                null);
        }

        lock (sync)
        {
            return confirmedAssetSubscriptions.TryGetValue(normalizedAssetId, out var state)
                ? new ConfirmedAssetSubscriptionSnapshot(
                    normalizedAssetId,
                    state.Component,
                    state.ConfirmedLive,
                    state.Generation,
                    confirmedSubscriptionSessionId,
                    state.ConfirmedAtUtc,
                    state.ConfirmationSourceTimestampUtc,
                    state.ConfirmationEventFingerprint)
                : new ConfirmedAssetSubscriptionSnapshot(
                    normalizedAssetId,
                    null,
                    false,
                    0,
                    confirmedSubscriptionSessionId,
                    null);
        }
    }

    public void AssignAssetSubscriptions(string component, IReadOnlyCollection<string> assetIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        ArgumentNullException.ThrowIfNull(assetIds);
        var normalizedComponent = component.Trim();
        var nextAssetIds = assetIds
            .Where(assetId => !string.IsNullOrWhiteSpace(assetId))
            .Select(assetId => assetId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (sync)
        {
            var previousAssetIds = assignedAssetsByComponent.TryGetValue(normalizedComponent, out var assigned)
                ? assigned
                : [];
            foreach (var removedAssetId in previousAssetIds.Except(nextAssetIds, StringComparer.OrdinalIgnoreCase))
            {
                if (confirmedAssetSubscriptions.TryGetValue(removedAssetId, out var state) &&
                    string.Equals(state.Component, normalizedComponent, StringComparison.OrdinalIgnoreCase))
                {
                    confirmedAssetSubscriptions[removedAssetId] = state with
                    {
                        Component = null,
                        ConfirmedLive = false,
                        ConfirmedAtUtc = null,
                        ConfirmationSourceTimestampUtc = null,
                        ConfirmationEventFingerprint = null,
                        Generation = state.Generation + (state.ConfirmedLive ? 1 : 0)
                    };
                }
            }

            foreach (var assetId in nextAssetIds)
            {
                if (!confirmedAssetSubscriptions.TryGetValue(assetId, out var state))
                {
                    confirmedAssetSubscriptions[assetId] = new ConfirmedAssetState(
                        normalizedComponent,
                        ConfirmedLive: false,
                        Generation: 0,
                        ConfirmedAtUtc: null,
                        ConfirmationSourceTimestampUtc: null,
                        ConfirmationEventFingerprint: null);
                    continue;
                }

                if (!string.Equals(state.Component, normalizedComponent, StringComparison.OrdinalIgnoreCase))
                {
                    confirmedAssetSubscriptions[assetId] = state with
                    {
                        Component = normalizedComponent,
                        ConfirmedLive = false,
                        ConfirmedAtUtc = null,
                        ConfirmationSourceTimestampUtc = null,
                        ConfirmationEventFingerprint = null,
                        Generation = state.Generation + (state.ConfirmedLive ? 1 : 0)
                    };
                }
            }

            assignedAssetsByComponent[normalizedComponent] = nextAssetIds;
        }
    }

    public void InvalidateAssetSubscriptions(string component)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            return;
        }

        lock (sync)
        {
            if (!assignedAssetsByComponent.TryGetValue(component.Trim(), out var assetIds))
            {
                return;
            }

            foreach (var assetId in assetIds)
            {
                if (confirmedAssetSubscriptions.TryGetValue(assetId, out var state) &&
                    state.ConfirmedLive &&
                    string.Equals(state.Component, component.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    confirmedAssetSubscriptions[assetId] = state with
                    {
                        ConfirmedLive = false,
                        ConfirmedAtUtc = null,
                        ConfirmationSourceTimestampUtc = null,
                        ConfirmationEventFingerprint = null,
                        Generation = state.Generation + 1
                    };
                }
            }
        }
    }

    public bool TryInvalidateAssetSubscription(
        ConfirmedAssetSubscriptionSnapshot expectedSubscription)
    {
        ArgumentNullException.ThrowIfNull(expectedSubscription);
        if (string.IsNullOrWhiteSpace(expectedSubscription.AssetId) ||
            !string.Equals(
                expectedSubscription.SessionId,
                confirmedSubscriptionSessionId,
                StringComparison.Ordinal))
        {
            return false;
        }

        lock (sync)
        {
            if (!confirmedAssetSubscriptions.TryGetValue(expectedSubscription.AssetId, out var state) ||
                !state.ConfirmedLive ||
                !string.Equals(state.Component, expectedSubscription.Component, StringComparison.Ordinal) ||
                state.Generation != expectedSubscription.Generation ||
                state.ConfirmedAtUtc != expectedSubscription.ConfirmedAtUtc ||
                state.ConfirmationSourceTimestampUtc !=
                    expectedSubscription.ConfirmationSourceTimestampUtc ||
                !string.Equals(
                    state.ConfirmationEventFingerprint,
                    expectedSubscription.ConfirmationEventFingerprint,
                    StringComparison.Ordinal))
            {
                return false;
            }

            confirmedAssetSubscriptions[expectedSubscription.AssetId] = state with
            {
                ConfirmedLive = false,
                ConfirmedAtUtc = null,
                ConfirmationSourceTimestampUtc = null,
                ConfirmationEventFingerprint = null,
                Generation = state.Generation + 1
            };
            return true;
        }
    }

    public void RemoveAssetSubscriptionComponent(string component)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            return;
        }

        var normalizedComponent = component.Trim();
        lock (sync)
        {
            if (!assignedAssetsByComponent.Remove(normalizedComponent, out var assetIds))
            {
                return;
            }

            foreach (var assetId in assetIds)
            {
                if (confirmedAssetSubscriptions.TryGetValue(assetId, out var state) &&
                    string.Equals(state.Component, normalizedComponent, StringComparison.OrdinalIgnoreCase))
                {
                    confirmedAssetSubscriptions[assetId] = state with
                    {
                        Component = null,
                        ConfirmedLive = false,
                        ConfirmedAtUtc = null,
                        ConfirmationSourceTimestampUtc = null,
                        ConfirmationEventFingerprint = null,
                        Generation = state.Generation + (state.ConfirmedLive ? 1 : 0)
                    };
                }
            }
        }
    }

    public bool ConfirmAssetSubscription(string component, string assetId)
    {
        return ConfirmAssetSubscription(component, assetId, DateTimeOffset.UtcNow);
    }

    public bool ConfirmAssetSubscription(
        string component,
        string assetId,
        DateTimeOffset confirmedAtUtc)
    {
        return ConfirmAssetSubscription(
            component,
            assetId,
            confirmedAtUtc,
            sourceTimestampUtc: null,
            eventFingerprint: null);
    }

    public bool ConfirmAssetSubscription(
        string component,
        string assetId,
        DateTimeOffset confirmedAtUtc,
        DateTimeOffset? sourceTimestampUtc,
        string? eventFingerprint)
    {
        if (string.IsNullOrWhiteSpace(component) || string.IsNullOrWhiteSpace(assetId))
        {
            return false;
        }

        var normalizedComponent = component.Trim();
        var normalizedAssetId = assetId.Trim();
        lock (sync)
        {
            if (!confirmedAssetSubscriptions.TryGetValue(normalizedAssetId, out var state) ||
                !string.Equals(state.Component, normalizedComponent, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            confirmedAssetSubscriptions[normalizedAssetId] = state.ConfirmedLive
                ? state
                : state with
                {
                    ConfirmedLive = true,
                    ConfirmedAtUtc = confirmedAtUtc.ToUniversalTime(),
                    ConfirmationSourceTimestampUtc = sourceTimestampUtc?.ToUniversalTime(),
                    ConfirmationEventFingerprint = string.IsNullOrWhiteSpace(eventFingerprint)
                        ? null
                        : eventFingerprint.Trim()
                };
            return true;
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
            var wasHealthy = IsHealthy(status);
            var isHealthy = IsHealthy(nextStatus);
            var disconnectedTimestampChanged =
                nextStatus.LastDisconnectedUtc is { } nextDisconnectedAtUtc &&
                nextDisconnectedAtUtc != status.LastDisconnectedUtc;
            if (wasHealthy &&
                (!isHealthy ||
                 nextStatus.ReconnectCount != status.ReconnectCount ||
                 disconnectedTimestampChanged))
            {
                continuityGeneration++;
            }

            status = nextStatus with { ContinuityGeneration = continuityGeneration };
        }
    }

    private long GetAssetSubscriptionGenerationUnsafe(string assetId)
    {
        return assetSubscriptionGenerations.TryGetValue(assetId, out var generation)
            ? generation
            : 0;
    }

    private static bool IsHealthy(MarketDataStatusSnapshot candidate)
    {
        return candidate.ConnectionState == MarketDataConnectionState.Connected && !candidate.Stale;
    }

    private sealed record ConfirmedAssetState(
        string? Component,
        bool ConfirmedLive,
        long Generation,
        DateTimeOffset? ConfirmedAtUtc,
        DateTimeOffset? ConfirmationSourceTimestampUtc,
        string? ConfirmationEventFingerprint);

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
