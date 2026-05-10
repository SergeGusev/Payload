using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public sealed class MarketDataWebSocketShardAllocator(MarketDataWebSocketOptionsAdapter options)
{
    private const string ComponentName = "PolymarketMarketWebSocket";
    private readonly Dictionary<string, string> assetToComponent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> componentAssets = new(StringComparer.OrdinalIgnoreCase);
    private int nextShardIndex = 1;

    public IReadOnlyList<MarketDataWebSocketShardPlan> Reconcile(
        IReadOnlyCollection<string> desiredAssetIds,
        IReadOnlyCollection<ActiveMarketAssetSnapshot> snapshots)
    {
        if (desiredAssetIds is null)
        {
            throw new ArgumentNullException(nameof(desiredAssetIds));
        }

        if (snapshots is null)
        {
            throw new ArgumentNullException(nameof(snapshots));
        }

        var desired = desiredAssetIds
            .Select(NormalizeAssetId)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RemoveStaleAssets(desired);
        foreach (var group in BuildAssetGroups(desired, snapshots))
        {
            AddOrMoveGroup(group);
        }

        RemoveEmptyComponents();

        return componentAssets
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select((pair, index) => new MarketDataWebSocketShardPlan(
                index,
                pair.Key,
                pair.Value.OrderBy(assetId => assetId, StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();
    }

    private void RemoveStaleAssets(HashSet<string> desired)
    {
        foreach (var assetId in assetToComponent.Keys.ToArray())
        {
            if (desired.Contains(assetId))
            {
                continue;
            }

            if (assetToComponent.Remove(assetId, out var component) &&
                componentAssets.TryGetValue(component, out var assets))
            {
                assets.Remove(assetId);
            }
        }
    }

    private void AddOrMoveGroup(AssetGroup group)
    {
        var existingComponent = group.AssetIds
            .Select(assetId => assetToComponent.TryGetValue(assetId, out var component) ? component : null)
            .FirstOrDefault(component => component is not null);
        var targetComponent = existingComponent ?? ChooseComponent(group.AssetIds.Count);

        foreach (var assetId in group.AssetIds)
        {
            if (assetToComponent.TryGetValue(assetId, out var currentComponent) &&
                string.Equals(currentComponent, targetComponent, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (assetToComponent.TryGetValue(assetId, out currentComponent) &&
                componentAssets.TryGetValue(currentComponent, out var currentAssets))
            {
                currentAssets.Remove(assetId);
            }

            assetToComponent[assetId] = targetComponent;
            componentAssets[targetComponent].Add(assetId);
        }
    }

    private string ChooseComponent(int groupAssetCount)
    {
        var shardMaxAssets = Math.Max(1, options.ShardMaxAssets);
        var candidate = componentAssets
            .Where(pair => pair.Value.Count + groupAssetCount <= shardMaxAssets)
            .OrderBy(pair => pair.Value.Count)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key)
            .FirstOrDefault();
        if (candidate is not null)
        {
            return candidate;
        }

        if (options.MaxShardConnections <= 0 || componentAssets.Count < options.MaxShardConnections)
        {
            var component = $"{ComponentName}:shard-{nextShardIndex++:000}";
            componentAssets[component] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return component;
        }

        candidate = componentAssets
            .OrderBy(pair => pair.Value.Count)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key)
            .First();
        return candidate;
    }

    private void RemoveEmptyComponents()
    {
        foreach (var component in componentAssets
            .Where(pair => pair.Value.Count == 0)
            .Select(pair => pair.Key)
            .ToArray())
        {
            componentAssets.Remove(component);
        }
    }

    private static IReadOnlyList<AssetGroup> BuildAssetGroups(
        HashSet<string> desired,
        IReadOnlyCollection<ActiveMarketAssetSnapshot> snapshots)
    {
        var remaining = desired.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = new List<AssetGroup>();

        foreach (var group in snapshots
            .Where(snapshot => remaining.Contains(snapshot.AssetId))
            .GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var assetIds = group
                .Select(snapshot => NormalizeAssetId(snapshot.AssetId))
                .OfType<string>()
                .Where(remaining.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(assetId => assetId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (assetIds.Length == 0)
            {
                continue;
            }

            groups.Add(new AssetGroup(group.Key, assetIds));
            foreach (var assetId in assetIds)
            {
                remaining.Remove(assetId);
            }
        }

        groups.AddRange(remaining
            .OrderBy(assetId => assetId, StringComparer.OrdinalIgnoreCase)
            .Select(assetId => new AssetGroup("asset:" + assetId, [assetId])));

        return groups
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GroupKey(ActiveMarketAssetSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.ConditionId))
        {
            return "condition:" + snapshot.ConditionId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(snapshot.MarketId))
        {
            return "market:" + snapshot.MarketId.Trim();
        }

        return "asset:" + snapshot.AssetId.Trim();
    }

    private static string? NormalizeAssetId(string? assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return null;
        }

        var trimmed = assetId.Trim();
        return trimmed.Equals("0", StringComparison.Ordinal) ||
            trimmed.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    private sealed record AssetGroup(string Key, IReadOnlyList<string> AssetIds);
}

public sealed record MarketDataWebSocketOptionsAdapter(
    int ShardMaxAssets,
    int MaxShardConnections);
