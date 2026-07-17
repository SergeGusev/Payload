using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Tests;

public sealed class StrategyDisplayCategoryTests
{
    [Theory]
    [InlineData("BTC Up or Down 5m 1 Child", "BTC")]
    [InlineData("eth up or down 5m Down 2 bps", "ETH")]
    [InlineData(" SOL Up or Down 5m 22 Child ", "SOL")]
    [InlineData("Follow leader", null)]
    [InlineData("BTCX Up or Down 5m", null)]
    [InlineData(null, null)]
    public void GetsStrategyAssetSymbol(string? strategyName, string? expectedAsset)
    {
        Assert.Equal(expectedAsset, StrategyDisplayCategories.GetAssetSymbol(strategyName));
    }

    [Fact]
    public void SimpleStrategiesAreNotRegistered()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SimpleFixedOutcomeInstant)
            .ToArray();

        Assert.Empty(variants);
    }

    [Fact]
    public void FixedOutcomeBpsInstantStrategiesHaveDedicatedDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant)
            .ToArray();

        Assert.Equal(600, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        AssertFixedOutcomeCategory(categoryCounts, "BTC Up or Down 5m Up Bps");
        AssertFixedOutcomeCategory(categoryCounts, "BTC Up or Down 5m Down Bps");
        AssertFixedOutcomeCategory(categoryCounts, "BTC Up or Down 15m Up Bps");
        AssertFixedOutcomeCategory(categoryCounts, "BTC Up or Down 15m Down Bps");
        AssertFixedOutcomeCategory(categoryCounts, "ETH Up or Down 5m Up Bps");
        AssertFixedOutcomeCategory(categoryCounts, "ETH Up or Down 5m Down Bps");
        AssertFixedOutcomeCategory(categoryCounts, "ETH Up or Down 15m Up Bps");
        AssertFixedOutcomeCategory(categoryCounts, "ETH Up or Down 15m Down Bps");
        AssertFixedOutcomeCategory(categoryCounts, "SOL Up or Down 5m Up Bps");
        AssertFixedOutcomeCategory(categoryCounts, "SOL Up or Down 5m Down Bps");
        AssertFixedOutcomeCategory(categoryCounts, "SOL Up or Down 15m Up Bps");
        AssertFixedOutcomeCategory(categoryCounts, "SOL Up or Down 15m Down Bps");
        Assert.Equal(12, categoryCounts.Count);
    }

    [Fact]
    public void ReferenceAverageFakPremarketStrategiesHaveDedicatedDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket)
            .ToArray();

        Assert.Equal(252, variants.Length);
        Assert.All(variants, variant => Assert.Contains(" Reference Average ", variant.Name, StringComparison.Ordinal));

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        AssertReferenceAverageFakPremarketCategories(categoryCounts, "BTC");
        AssertReferenceAverageFakPremarketCategories(categoryCounts, "ETH");
        AssertReferenceAverageFakPremarketCategories(categoryCounts, "SOL");
        Assert.Equal(9, categoryCounts.Count);
    }

    [Fact]
    public void FilteredAverageFakPremarketStrategiesAreNotRegistered()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.FilteredReferenceAverageBpsThresholdFakPremarket)
            .ToArray();

        Assert.Empty(variants);
    }

    [Fact]
    public void FuturesBasisPremarketStrategiesHaveDedicatedDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior is BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarket or
                BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarketRevert)
            .ToArray();

        Assert.Equal(48, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(8, categoryCounts["BTC Up or Down 5m Bps Futures Basis Premarket"]);
        Assert.Equal(8, categoryCounts["ETH Up or Down 5m Bps Futures Basis Premarket"]);
        Assert.Equal(8, categoryCounts["SOL Up or Down 5m Bps Futures Basis Premarket"]);
        Assert.Equal(8, categoryCounts["BTC Up or Down 5m Bps Futures Basis Revert Premarket"]);
        Assert.Equal(8, categoryCounts["ETH Up or Down 5m Bps Futures Basis Revert Premarket"]);
        Assert.Equal(8, categoryCounts["SOL Up or Down 5m Bps Futures Basis Revert Premarket"]);
        Assert.Equal(6, categoryCounts.Count);
    }

    [Fact]
    public void EthDownLegacyFakPremarketStrategiesKeepLegacyDisplayCategory()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket)
            .ToArray();

        Assert.Equal(62, variants.Length);
        Assert.Equal(
            ["ETH Up or Down 5m Down Bps Premarket"],
            variants
                .Select(variant => StrategyDisplayCategories.GetCategory(variant.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    [Fact]
    public void DiffCounterTrendStrategiesAreNotRegistered()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend)
            .ToArray();

        Assert.Empty(variants);
    }

    [Fact]
    public void DiffCounterTrendFakPremarketStrategiesHaveDedicatedDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket)
            .ToArray();

        Assert.Equal(80, variants.Length);
        Assert.Contains(variants, variant =>
            variant.Name == "BTC Up or Down 5m Up 3 Diff Premarket");
        Assert.Contains(variants, variant =>
            variant.Name == "ETH Up or Down 5m Up 3 Diff Premarket");
        Assert.Contains(variants, variant =>
            variant.Name == "ETH Up or Down 5m Down 3 Diff Premarket");
        Assert.Contains(variants, variant =>
            variant.Name == "SOL Up or Down 5m Up 3 Diff Premarket");
        Assert.DoesNotContain(variants, variant =>
            variant.Name.Contains(" Revert", StringComparison.Ordinal));

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(10, categoryCounts["BTC Up or Down 5m Diff Up Premarket"]);
        Assert.Equal(30, categoryCounts["BTC Up or Down 5m Diff Down Premarket"]);
        Assert.Equal(10, categoryCounts["ETH Up or Down 5m Diff Up Premarket"]);
        Assert.Equal(10, categoryCounts["ETH Up or Down 5m Diff Down Premarket"]);
        Assert.Equal(10, categoryCounts["SOL Up or Down 5m Diff Up Premarket"]);
        Assert.Equal(10, categoryCounts["SOL Up or Down 5m Diff Down Premarket"]);
        Assert.Equal(6, categoryCounts.Count);
    }

    [Fact]
    public void DiffProgressStrategiesUseExpectedDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffProgress)
            .ToArray();

        Assert.Equal(292, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(50, categoryCounts["BTC Up or Down 5m Diff Up Progress"]);
        Assert.Equal(50, categoryCounts["BTC Up or Down 5m Diff Down Progress"]);
        Assert.Equal(94, categoryCounts["ETH Up or Down 5m Diff Progress"]);
        Assert.Equal(98, categoryCounts["SOL Up or Down 5m Diff Progress"]);
        Assert.Equal(4, categoryCounts.Count);
    }

    [Fact]
    public void DiffShiftProgressStrategiesUseAssetDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffShiftProgress)
            .ToArray();

        Assert.Equal(16, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(2, categoryCounts["BTC Up or Down 5m Diff Shift Progress"]);
        Assert.Equal(2, categoryCounts["ETH Up or Down 5m Diff Shift Progress"]);
        Assert.Equal(2, categoryCounts["SOL Up or Down 5m Diff Shift Progress"]);
        Assert.Equal(1, categoryCounts["BTC Up or Down 5m Diff Shift Progress Premarket"]);
        Assert.Equal(4, categoryCounts["ETH Up or Down 5m Diff Shift Progress Premarket"]);
        Assert.Equal(5, categoryCounts["SOL Up or Down 5m Diff Shift Progress Premarket"]);
        Assert.Equal(6, categoryCounts.Count);
    }

    [Fact]
    public void DiffLimitProgressPremarketStrategiesUseAssetDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffLimitProgressPremarket)
            .ToArray();

        Assert.Equal(10, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(5, categoryCounts["ETH Up or Down 5m Diff Limit Progress"]);
        Assert.Equal(5, categoryCounts["SOL Up or Down 5m Diff Limit Progress"]);
        Assert.Equal(2, categoryCounts.Count);
    }

    [Fact]
    public void DiffRealLimitProgressPremarketStrategiesUseAssetDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffRealLimitProgressPremarket)
            .ToArray();

        Assert.Equal(15, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(5, categoryCounts["BTC Up or Down 5m Diff Real Limit Progress"]);
        Assert.Equal(5, categoryCounts["ETH Up or Down 5m Diff Real Limit Progress"]);
        Assert.Equal(5, categoryCounts["SOL Up or Down 5m Diff Real Limit Progress"]);
        Assert.Equal(3, categoryCounts.Count);
    }

    [Fact]
    public void DiffReferenceAveragePremarketStrategiesUseAssetDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket)
            .ToArray();

        Assert.Equal(42, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(14, categoryCounts["BTC Up or Down 5m Diff Reference Average Premarket"]);
        Assert.Equal(14, categoryCounts["ETH Up or Down 5m Diff Reference Average Premarket"]);
        Assert.Equal(14, categoryCounts["SOL Up or Down 5m Diff Reference Average Premarket"]);
        Assert.Equal(3, categoryCounts.Count);
    }

    [Fact]
    public void BpsConfirmedAveragePremarketStrategiesUseAssetDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BpsConfirmedAveragePremarket)
            .ToArray();

        Assert.Equal(84, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(28, categoryCounts["BTC Up or Down 5m Bps Confirmed Average Premarket"]);
        Assert.Equal(28, categoryCounts["ETH Up or Down 5m Bps Confirmed Average Premarket"]);
        Assert.Equal(28, categoryCounts["SOL Up or Down 5m Bps Confirmed Average Premarket"]);
        Assert.Equal(3, categoryCounts.Count);
    }

    [Fact]
    public void DiffConfirmedAveragePremarketStrategiesUseAssetDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffConfirmedAveragePremarket)
            .ToArray();

        Assert.Equal(42, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(14, categoryCounts["BTC Up or Down 5m Diff Confirmed Average Premarket"]);
        Assert.Equal(14, categoryCounts["ETH Up or Down 5m Diff Confirmed Average Premarket"]);
        Assert.Equal(14, categoryCounts["SOL Up or Down 5m Diff Confirmed Average Premarket"]);
        Assert.Equal(3, categoryCounts.Count);
    }

    [Fact]
    public void AdjustedDiffCounterTrendStrategiesAreNotRegistered()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend)
            .ToArray();

        Assert.Empty(variants);
    }

    [Fact]
    public void ShiftDiffCounterTrendStrategiesAreNotRegistered()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend)
            .ToArray();

        Assert.Empty(variants);
    }

    [Theory]
    [InlineData("ETH Up or Down 5m Middle 100 47 bps Instant", "ETH Up or Down 5m Middle")]
    [InlineData("BTC Up or Down 5m Up 5 Diff Instant", "BTC Up or Down 5m Diff Up")]
    [InlineData("SOL Up or Down 5m Down 150 Diff Instant", "SOL Up or Down 5m Diff Down")]
    [InlineData("SOL Up or Down 5m Down 150 Diff Revert Instant", "SOL Up or Down 5m Diff Down Revert")]
    [InlineData("BTC Up or Down 5m Up 20 AdjustedDiff Instant", "BTC Up or Down 5m AdjustedDiff Up")]
    [InlineData("BTC Up or Down 5m Up 20 AdjustedDiff Revert Instant", "BTC Up or Down 5m AdjustedDiff Up Revert")]
    [InlineData("SOL Up or Down 5m Down 15 AdjustedDiff Instant", "SOL Up or Down 5m AdjustedDiff Down")]
    [InlineData("BTC Up or Down 5m Up 2 1 ShiftDiff Instant", "BTC Up or Down 5m ShiftDiff 2")]
    [InlineData("BTC Up or Down 5m Up 2 2 ShiftDiff Instant", "BTC Up or Down 5m ShiftDiff 2")]
    [InlineData("BTC Up or Down 5m Up 2 4 ShiftDiff Instant", "BTC Up or Down 5m ShiftDiff 2")]
    [InlineData("ETH Up or Down 5m Down 2 12 ShiftDiff Instant", "ETH Up or Down 5m ShiftDiff 2")]
    [InlineData("ETH Up or Down 5m Down 2 12 ShiftDiff Revert Instant", "ETH Up or Down 5m ShiftDiff 2 Revert")]
    [InlineData("SOL Up or Down 5m Down 6 12 ShiftDiff Instant", "SOL Up or Down 5m ShiftDiff 6")]
    [InlineData("BTC Up or Down 5m Up Simple", "BTC Up or Down 5m Other")]
    [InlineData("ETH Up or Down 5m Down Simple", "ETH Up or Down 5m Other")]
    [InlineData("SOL Up or Down 5m Up Simple", "SOL Up or Down 5m Other")]
    [InlineData("BTC Up or Down 5m Legacy", "BTC Up or Down 5m Other")]
    [InlineData("ETH Up or Down 5m Down 9 bps", "ETH Up or Down 5m Down Bps")]
    [InlineData("BTC Up or Down 5m Up 5 bps Reference Average Premarket", "BTC Up or Down 5m Up Bps Reference Average Premarket")]
    [InlineData("BTC Up or Down 5m 5 bps Reference Average Premarket", "BTC Up or Down 5m Bps Reference Average Premarket")]
    [InlineData("ETH Up or Down 5m Down 9 bps Premarket", "ETH Up or Down 5m Down Bps Premarket")]
    [InlineData("ETH Up or Down 5m Down 9 bps Reference Average Premarket", "ETH Up or Down 5m Down Bps Reference Average Premarket")]
    [InlineData("ETH Up or Down 5m Down 9 bps Filtered Average Premarket", "ETH Up or Down 5m Down Bps Filtered Average Premarket")]
    [InlineData("ETH Up or Down 5m 9 bps Reference Average Premarket", "ETH Up or Down 5m Bps Reference Average Premarket")]
    [InlineData("ETH Up or Down 5m Up 9 bps Optimized Average Premarket", "ETH Up or Down 5m Up Bps Optimized Average Premarket")]
    [InlineData("ETH Up or Down 5m Down 9 bps Optimized Average Premarket", "ETH Up or Down 5m Down Bps Optimized Average Premarket")]
    [InlineData("ETH Up or Down 5m 9 bps Optimized Average Premarket", "ETH Up or Down 5m Bps Optimized Average Premarket")]
    [InlineData("SOL Up or Down 5m Up 15 bps Reference Average Premarket", "SOL Up or Down 5m Up Bps Reference Average Premarket")]
    [InlineData("SOL Up or Down 5m Down 15 bps Reference Average Premarket", "SOL Up or Down 5m Down Bps Reference Average Premarket")]
    [InlineData("SOL Up or Down 5m 15 bps Reference Average Premarket", "SOL Up or Down 5m Bps Reference Average Premarket")]
    [InlineData("ETH Up or Down 5m Prev Score Countertrend", "ETH Up or Down 5m Countertrend")]
    [InlineData("ETH Up or Down 5m Prev Score Countertrend Revert", "ETH Up or Down 5m Countertrend")]
    [InlineData("SOL Up or Down 5m Prev Score Countertrend", "SOL Up or Down 5m Countertrend")]
    [InlineData("SOL Up or Down 5m Prev Score Countertrend Revert", "SOL Up or Down 5m Countertrend")]
    [InlineData("BTC Up or Down 5m Prev Score Countertrend Premarket", "BTC Up or Down 5m Countertrend Premarket")]
    [InlineData("BTC Up or Down 5m Prev Score Countertrend Premarket Revert", "BTC Up or Down 5m Countertrend Premarket")]
    [InlineData("ETH Up or Down 5m Prev Score Countertrend Premarket", "ETH Up or Down 5m Countertrend Premarket")]
    [InlineData("ETH Up or Down 5m Prev Score Countertrend Premarket Revert", "ETH Up or Down 5m Countertrend Premarket")]
    [InlineData("SOL Up or Down 5m Prev Score Countertrend Premarket", "SOL Up or Down 5m Countertrend Premarket")]
    [InlineData("SOL Up or Down 5m Prev Score Countertrend Premarket Revert", "SOL Up or Down 5m Countertrend Premarket")]
    [InlineData("ETH Up or Down 5m Down 40 bps Premarket -10s", "ETH Up or Down 5m Down Bps Premarket")]
    [InlineData("ETH Up or Down 5m Down 35 bps Premarket -5s", "ETH Up or Down 5m Down Bps Premarket")]
    [InlineData("ETH Up or Down 5m Up 3 Diff Premarket", "ETH Up or Down 5m Diff Up Premarket")]
    [InlineData("ETH Up or Down 5m Up 3 Diff Revert Premarket", "ETH Up or Down 5m Diff Up Revert Premarket")]
    [InlineData("ETH Up or Down 5m Down 3 Diff Premarket", "ETH Up or Down 5m Diff Down Premarket")]
    [InlineData("ETH Up or Down 5m Down 3 Diff Revert Premarket", "ETH Up or Down 5m Diff Down Revert Premarket")]
    [InlineData("BTC Up or Down 5m Up 3 Diff Premarket", "BTC Up or Down 5m Diff Up Premarket")]
    [InlineData("BTC Up or Down 5m Down 3 Diff Revert Premarket", "BTC Up or Down 5m Diff Down Revert Premarket")]
    [InlineData("SOL Up or Down 5m Up 3 Diff Premarket", "SOL Up or Down 5m Diff Up Premarket")]
    [InlineData("SOL Up or Down 5m Down 3 Diff Revert Premarket", "SOL Up or Down 5m Diff Down Revert Premarket")]
    [InlineData("BTC Up or Down 5m 17 Diff Up Progress", "BTC Up or Down 5m Diff Up Progress")]
    [InlineData("BTC Up or Down 5m 17 Diff Down Progress", "BTC Up or Down 5m Diff Down Progress")]
    [InlineData("ETH Up or Down 5m 50 Diff Down Progress", "ETH Up or Down 5m Diff Progress")]
    [InlineData("SOL Up or Down 5m 3 Diff Up Progress", "SOL Up or Down 5m Diff Progress")]
    [InlineData("BTC Up or Down 5m Diff Up Shift Progress", "BTC Up or Down 5m Diff Shift Progress")]
    [InlineData("ETH Up or Down 5m Diff Down Shift Progress", "ETH Up or Down 5m Diff Shift Progress")]
    [InlineData("BTC Up or Down 5m 1 Diff Shift Progress Premarket", "BTC Up or Down 5m Diff Shift Progress Premarket")]
    [InlineData("ETH Up or Down 5m 3 Diff Shift Progress Premarket", "ETH Up or Down 5m Diff Shift Progress Premarket")]
    [InlineData("SOL Up or Down 5m 5 Diff Shift Progress Premarket", "SOL Up or Down 5m Diff Shift Progress Premarket")]
    [InlineData("BTC Up or Down 5m 1 Diff Limit Progress Premarket", "BTC Up or Down 5m Diff Limit Progress")]
    [InlineData("SOL Up or Down 5m 5 Diff Limit Progress Premarket", "SOL Up or Down 5m Diff Limit Progress")]
    [InlineData("BTC Up or Down 5m 1 Diff Real Limit Progress Premarket", "BTC Up or Down 5m Diff Real Limit Progress")]
    [InlineData("SOL Up or Down 5m 5 Diff Real Limit Progress Premarket", "SOL Up or Down 5m Diff Real Limit Progress")]
    [InlineData("BTC Up or Down 5m 1 Diff Reference Average Premarket", "BTC Up or Down 5m Diff Reference Average Premarket")]
    [InlineData("SOL Up or Down 5m 30 Diff Reference Average Premarket", "SOL Up or Down 5m Diff Reference Average Premarket")]
    [InlineData("BTC Up or Down 5m 10 bps Confirmed Average Premarket", "BTC Up or Down 5m Bps Confirmed Average Premarket")]
    [InlineData("ETH Up or Down 5m 3 Diff Confirmed Average Premarket", "ETH Up or Down 5m Diff Confirmed Average Premarket")]
    [InlineData("SOL Up or Down 5m 100 bps Confirmed Average Premarket", "SOL Up or Down 5m Bps Confirmed Average Premarket")]
    [InlineData("Follow leader", "Other")]
    public void PreservesExistingDisplayCategories(string strategyName, string expectedCategory)
    {
        Assert.Equal(expectedCategory, StrategyDisplayCategories.GetCategory(strategyName));
    }

    private static void AssertFixedOutcomeCategory(IReadOnlyDictionary<string, int> categoryCounts, string category)
    {
        Assert.True(categoryCounts.TryGetValue(category, out var count), "Missing category: " + category);
        Assert.Equal(50, count);
    }

    private static void AssertReferenceAverageFakPremarketCategories(IReadOnlyDictionary<string, int> categoryCounts, string assetSymbol)
    {
        AssertReferenceAverageFakPremarketCategory(categoryCounts, $"{assetSymbol} Up or Down 5m Down Bps Reference Average Premarket");
        AssertReferenceAverageFakPremarketCategory(categoryCounts, $"{assetSymbol} Up or Down 5m Bps Reference Average Premarket");
        AssertReferenceAverageFakPremarketCategory(categoryCounts, $"{assetSymbol} Up or Down 5m Up Bps Reference Average Premarket");
    }

    private static void AssertReferenceAverageFakPremarketCategory(IReadOnlyDictionary<string, int> categoryCounts, string category)
    {
        Assert.True(categoryCounts.TryGetValue(category, out var count), "Missing category: " + category);
        Assert.Equal(28, count);
    }

}
