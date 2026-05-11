using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Tests;

public sealed class MarketDataWebSocketShardPlannerTests
{
    [Fact]
    public void BuildPlans_KeepsMarketOutcomesOnSameShard()
    {
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        registry.AddOrUpdateMarkets(
        [
            GammaMarketIngestionTests.CreateMarketForTests("market-1"),
            GammaMarketIngestionTests.CreateMarketForTests("market-2"),
            GammaMarketIngestionTests.CreateMarketForTests("market-3")
        ]);

        var plans = MarketDataWebSocketShardPlanner.BuildPlans(
            registry.GetAssetIds(),
            registry.GetSnapshots(),
            shardMaxAssets: 2,
            maxShardConnections: 0);

        Assert.Equal(3, plans.Count);
        foreach (var marketId in new[] { "market-1", "market-2", "market-3" })
        {
            var yes = "token-yes-" + marketId;
            var no = "token-no-" + marketId;
            var plan = Assert.Single(plans, candidate =>
                candidate.AssetIds.Contains(yes, StringComparer.OrdinalIgnoreCase));
            Assert.Contains(no, plan.AssetIds, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void BuildPlans_UsesMaxShardConnectionsAsSoftCap()
    {
        var assetIds = Enumerable.Range(0, 10)
            .Select(index => "asset-" + index.ToString("00"))
            .ToArray();

        var plans = MarketDataWebSocketShardPlanner.BuildPlans(
            assetIds,
            [],
            shardMaxAssets: 2,
            maxShardConnections: 3);

        Assert.Equal(3, plans.Count);
        Assert.Equal(10, plans.Sum(plan => plan.AssetIds.Count));
        Assert.All(plans, plan => Assert.InRange(plan.AssetIds.Count, 2, 4));
    }

    [Fact]
    public void BuildPlans_IncludesPinnedAssetsMissingFromRegistryAsSingletonGroups()
    {
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        registry.AddOrUpdateMarkets([GammaMarketIngestionTests.CreateMarketForTests("market-1")]);
        var desired = registry.GetAssetIds()
            .Concat(["pinned-asset"])
            .ToArray();

        var plans = MarketDataWebSocketShardPlanner.BuildPlans(
            desired,
            registry.GetSnapshots(),
            shardMaxAssets: 2,
            maxShardConnections: 0);

        Assert.Contains(plans, plan => plan.AssetIds.Contains("pinned-asset", StringComparer.OrdinalIgnoreCase));
        Assert.Equal(3, plans.Sum(plan => plan.AssetIds.Count));
    }

    [Fact]
    public void Allocator_KeepsExistingAssetsOnSameShardWhenNewPagesArrive()
    {
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        registry.AddOrUpdateMarkets(
        [
            GammaMarketIngestionTests.CreateMarketForTests("market-1"),
            GammaMarketIngestionTests.CreateMarketForTests("market-2")
        ]);
        var allocator = new MarketDataWebSocketShardAllocator(new MarketDataWebSocketOptionsAdapter(
            ShardMaxAssets: 6,
            MaxShardConnections: 4));

        var firstPlans = allocator.Reconcile(registry.GetAssetIds(), registry.GetSnapshots());
        var firstComponent = Assert.Single(firstPlans).Component;

        registry.AddOrUpdateMarkets([GammaMarketIngestionTests.CreateMarketForTests("market-3")]);
        var secondPlans = allocator.Reconcile(registry.GetAssetIds(), registry.GetSnapshots());

        var tokenYesMarket1Plan = Assert.Single(secondPlans, plan =>
            plan.AssetIds.Contains("token-yes-market-1", StringComparer.OrdinalIgnoreCase));
        Assert.Equal(firstComponent, tokenYesMarket1Plan.Component);
        Assert.Contains("token-yes-market-3", tokenYesMarket1Plan.AssetIds, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(6, tokenYesMarket1Plan.AssetIds.Count);
    }

    [Fact]
    public void Allocator_StartsNewShardWithoutMovingFullExistingShard()
    {
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        registry.AddOrUpdateMarkets(
        [
            GammaMarketIngestionTests.CreateMarketForTests("market-1"),
            GammaMarketIngestionTests.CreateMarketForTests("market-2")
        ]);
        var allocator = new MarketDataWebSocketShardAllocator(new MarketDataWebSocketOptionsAdapter(
            ShardMaxAssets: 4,
            MaxShardConnections: 4));

        var firstPlans = allocator.Reconcile(registry.GetAssetIds(), registry.GetSnapshots());
        var firstComponent = Assert.Single(firstPlans).Component;

        registry.AddOrUpdateMarkets([GammaMarketIngestionTests.CreateMarketForTests("market-3")]);
        var secondPlans = allocator.Reconcile(registry.GetAssetIds(), registry.GetSnapshots());

        Assert.Equal(2, secondPlans.Count);
        var originalShard = Assert.Single(secondPlans, plan => plan.Component == firstComponent);
        Assert.Contains("token-yes-market-1", originalShard.AssetIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-no-market-2", originalShard.AssetIds, StringComparer.OrdinalIgnoreCase);
        var newShard = Assert.Single(secondPlans, plan =>
            plan.AssetIds.Contains("token-yes-market-3", StringComparer.OrdinalIgnoreCase));
        Assert.NotEqual(firstComponent, newShard.Component);
        Assert.Contains("token-no-market-3", newShard.AssetIds, StringComparer.OrdinalIgnoreCase);
    }
}
