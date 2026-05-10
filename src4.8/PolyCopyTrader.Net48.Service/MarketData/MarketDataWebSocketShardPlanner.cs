using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public static class MarketDataWebSocketShardPlanner
{
    private const string ComponentName = "PolymarketMarketWebSocket";

    public static IReadOnlyList<MarketDataWebSocketShardPlan> BuildPlans(
        IReadOnlyCollection<string> desiredAssetIds,
        IReadOnlyCollection<ActiveMarketAssetSnapshot> snapshots,
        int shardMaxAssets,
        int maxShardConnections)
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
        if (desired.Count == 0)
        {
            return [];
        }

        var groups = BuildAssetGroups(desired, snapshots);
        var totalAssets = groups.Sum(group => group.AssetIds.Count);
        var effectiveShardMaxAssets = Math.Max(1, shardMaxAssets);
        if (maxShardConnections > 0)
        {
            effectiveShardMaxAssets = Math.Max(
                effectiveShardMaxAssets,
                (int)Math.Ceiling(totalAssets / (double)maxShardConnections));
        }

        var plans = new List<MarketDataWebSocketShardPlan>();
        var current = new List<string>();

        foreach (var group in groups)
        {
            if (current.Count > 0 && current.Count + group.AssetIds.Count > effectiveShardMaxAssets)
            {
                plans.Add(CreatePlan(plans.Count, current));
                current = [];
            }

            current.AddRange(group.AssetIds);
        }

        if (current.Count > 0)
        {
            plans.Add(CreatePlan(plans.Count, current));
        }

        return plans;
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

    private static MarketDataWebSocketShardPlan CreatePlan(int zeroBasedIndex, IReadOnlyList<string> assetIds)
    {
        return new MarketDataWebSocketShardPlan(
            zeroBasedIndex,
            $"{ComponentName}:shard-{zeroBasedIndex + 1:000}",
            assetIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(assetId => assetId, StringComparer.OrdinalIgnoreCase)
                .ToArray());
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
