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
        if (update.OrderBookSnapshot is null || string.IsNullOrWhiteSpace(update.OrderBookSnapshot.AssetId))
        {
            return;
        }

        snapshots.AddOrUpdate(update.OrderBookSnapshot.AssetId, update.OrderBookSnapshot, (_, existing) =>
            update.OrderBookSnapshot.SnapshotAtUtc >= existing.SnapshotAtUtc ? update.OrderBookSnapshot : existing);
    }

    public bool TryGetFreshOrderBook(string assetId, TimeSpan maxAge, out OrderBookSnapshot snapshot)
    {
        if (snapshots.TryGetValue(assetId, out var candidate) &&
            DateTimeOffset.UtcNow - candidate.SnapshotAtUtc <= maxAge)
        {
            snapshot = candidate;
            return true;
        }

        snapshot = default!;
        return false;
    }

    public void UpdateStatus(MarketDataStatusSnapshot nextStatus)
    {
        lock (sync)
        {
            status = nextStatus;
        }
    }
}
