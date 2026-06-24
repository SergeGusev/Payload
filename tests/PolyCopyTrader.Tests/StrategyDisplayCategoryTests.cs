using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Tests;

public sealed class StrategyDisplayCategoryTests
{
    [Fact]
    public void SimpleStrategiesShareSingleDisplayCategory()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SimpleFixedOutcomeInstant)
            .ToArray();

        Assert.Equal(6, variants.Length);
        Assert.Equal(
            ["Simple"],
            variants
                .Select(variant => StrategyDisplayCategories.GetCategory(variant.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
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

        Assert.Equal(168, variants.Length);
        Assert.All(variants, variant => Assert.Contains(" Reference Average ", variant.Name, StringComparison.Ordinal));

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        AssertReferenceAverageFakPremarketCategory(categoryCounts, "BTC Up or Down 5m Reference Average Bps Premarket");
        AssertReferenceAverageFakPremarketCategory(categoryCounts, "ETH Up or Down 5m Reference Average Bps Premarket");
        AssertReferenceAverageFakPremarketCategory(categoryCounts, "SOL Up or Down 5m Reference Average Bps Premarket");
        Assert.Equal(3, categoryCounts.Count);
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
    public void DiffCounterTrendStrategiesHaveDedicatedDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend)
            .ToArray();

        Assert.Equal(456, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        AssertDiffCategory(categoryCounts, "BTC Up or Down 5m Diff Up");
        AssertDiffCategory(categoryCounts, "BTC Up or Down 5m Diff Down");
        AssertDiffCategory(categoryCounts, "ETH Up or Down 5m Diff Up");
        AssertDiffCategory(categoryCounts, "ETH Up or Down 5m Diff Down");
        AssertDiffCategory(categoryCounts, "SOL Up or Down 5m Diff Up");
        AssertDiffCategory(categoryCounts, "SOL Up or Down 5m Diff Down");
        AssertDiffCategory(categoryCounts, "BTC Up or Down 5m Diff Up Revert");
        AssertDiffCategory(categoryCounts, "BTC Up or Down 5m Diff Down Revert");
        AssertDiffCategory(categoryCounts, "ETH Up or Down 5m Diff Up Revert");
        AssertDiffCategory(categoryCounts, "ETH Up or Down 5m Diff Down Revert");
        AssertDiffCategory(categoryCounts, "SOL Up or Down 5m Diff Up Revert");
        AssertDiffCategory(categoryCounts, "SOL Up or Down 5m Diff Down Revert");
        Assert.Equal(12, categoryCounts.Count);
    }

    [Fact]
    public void DiffCounterTrendFakPremarketStrategiesHaveDedicatedDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket)
            .ToArray();

        var variant = Assert.Single(variants);

        Assert.Equal("ETH Up or Down 5m Down 3 Diff Premarket", variant.Name);
        Assert.Equal(
            "ETH Up or Down 5m Diff Down Premarket",
            StrategyDisplayCategories.GetCategory(variant.Name));
    }

    [Fact]
    public void AdjustedDiffCounterTrendStrategiesHaveDedicatedDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend)
            .ToArray();

        Assert.Equal(144, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        AssertAdjustedDiffCategory(categoryCounts, "BTC Up or Down 5m AdjustedDiff Up");
        AssertAdjustedDiffCategory(categoryCounts, "BTC Up or Down 5m AdjustedDiff Down");
        AssertAdjustedDiffCategory(categoryCounts, "ETH Up or Down 5m AdjustedDiff Up");
        AssertAdjustedDiffCategory(categoryCounts, "ETH Up or Down 5m AdjustedDiff Down");
        AssertAdjustedDiffCategory(categoryCounts, "SOL Up or Down 5m AdjustedDiff Up");
        AssertAdjustedDiffCategory(categoryCounts, "SOL Up or Down 5m AdjustedDiff Down");
        AssertAdjustedDiffCategory(categoryCounts, "BTC Up or Down 5m AdjustedDiff Up Revert");
        AssertAdjustedDiffCategory(categoryCounts, "BTC Up or Down 5m AdjustedDiff Down Revert");
        AssertAdjustedDiffCategory(categoryCounts, "ETH Up or Down 5m AdjustedDiff Up Revert");
        AssertAdjustedDiffCategory(categoryCounts, "ETH Up or Down 5m AdjustedDiff Down Revert");
        AssertAdjustedDiffCategory(categoryCounts, "SOL Up or Down 5m AdjustedDiff Up Revert");
        AssertAdjustedDiffCategory(categoryCounts, "SOL Up or Down 5m AdjustedDiff Down Revert");
        Assert.Equal(12, categoryCounts.Count);
    }

    [Fact]
    public void ShiftDiffCounterTrendStrategiesHaveDedicatedDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend)
            .ToArray();

        Assert.Equal(864, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        AssertShiftDiffCategory(categoryCounts, "BTC Up or Down 5m ShiftDiff 1");
        AssertShiftDiffCategory(categoryCounts, "BTC Up or Down 5m ShiftDiff 2");
        AssertShiftDiffCategory(categoryCounts, "BTC Up or Down 5m ShiftDiff 6");
        AssertShiftDiffCategory(categoryCounts, "ETH Up or Down 5m ShiftDiff 2");
        AssertShiftDiffCategory(categoryCounts, "SOL Up or Down 5m ShiftDiff 2");
        AssertShiftDiffCategory(categoryCounts, "SOL Up or Down 5m ShiftDiff 6");
        AssertShiftDiffCategory(categoryCounts, "BTC Up or Down 5m ShiftDiff 1 Revert");
        AssertShiftDiffCategory(categoryCounts, "BTC Up or Down 5m ShiftDiff 2 Revert");
        AssertShiftDiffCategory(categoryCounts, "BTC Up or Down 5m ShiftDiff 6 Revert");
        AssertShiftDiffCategory(categoryCounts, "ETH Up or Down 5m ShiftDiff 2 Revert");
        AssertShiftDiffCategory(categoryCounts, "SOL Up or Down 5m ShiftDiff 2 Revert");
        AssertShiftDiffCategory(categoryCounts, "SOL Up or Down 5m ShiftDiff 6 Revert");
        Assert.Equal(36, categoryCounts.Count);
    }

    [Theory]
    [InlineData("ETH Up or Down 5m Middle 100 47 bps Instant", "ETH Up or Down 5m Middle")]
    [InlineData("SOL Up or Down 5m Binance 42 bps Instant", "SOL Up or Down 5m Binance")]
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
    [InlineData("BTC Up or Down 5m Up Simple", "Simple")]
    [InlineData("ETH Up or Down 5m Down Simple", "Simple")]
    [InlineData("SOL Up or Down 5m Up Simple", "Simple")]
    [InlineData("BTC Up or Down 5m Up", "BTC Up or Down 5m Other")]
    [InlineData("BTC Up or Down 5m Down Maker 50", "BTC Up or Down 5m Other")]
    [InlineData("ETH Up or Down 5m Down 9 bps", "ETH Up or Down 5m Down Bps")]
    [InlineData("BTC Up or Down 5m Up 5 bps Reference Average Premarket", "BTC Up or Down 5m Reference Average Bps Premarket")]
    [InlineData("ETH Up or Down 5m Down 9 bps Premarket", "ETH Up or Down 5m Down Bps Premarket")]
    [InlineData("ETH Up or Down 5m Down 9 bps Reference Average Premarket", "ETH Up or Down 5m Reference Average Bps Premarket")]
    [InlineData("SOL Up or Down 5m Down 15 bps Reference Average Premarket", "SOL Up or Down 5m Reference Average Bps Premarket")]
    [InlineData("ETH Up or Down 5m Prev Score Countertrend", "ETH Up or Down 5m Countertrend")]
    [InlineData("SOL Up or Down 5m Prev Score Countertrend", "SOL Up or Down 5m Countertrend")]
    [InlineData("BTC Up or Down 5m Prev Score Countertrend Premarket", "BTC Up or Down 5m Countertrend Premarket")]
    [InlineData("ETH Up or Down 5m Prev Score Countertrend Premarket", "ETH Up or Down 5m Countertrend Premarket")]
    [InlineData("SOL Up or Down 5m Prev Score Countertrend Premarket", "SOL Up or Down 5m Countertrend Premarket")]
    [InlineData("ETH Up or Down 5m Down 40 bps Premarket -10s", "ETH Up or Down 5m Down Bps Premarket")]
    [InlineData("ETH Up or Down 5m Down 35 bps Premarket -5s", "ETH Up or Down 5m Down Bps Premarket")]
    [InlineData("ETH Up or Down 5m Down 3 Diff Premarket", "ETH Up or Down 5m Diff Down Premarket")]
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

    private static void AssertReferenceAverageFakPremarketCategory(IReadOnlyDictionary<string, int> categoryCounts, string category)
    {
        Assert.True(categoryCounts.TryGetValue(category, out var count), "Missing category: " + category);
        Assert.Equal(56, count);
    }

    private static void AssertDiffCategory(IReadOnlyDictionary<string, int> categoryCounts, string category)
    {
        Assert.True(categoryCounts.TryGetValue(category, out var count), "Missing category: " + category);
        Assert.Equal(38, count);
    }

    private static void AssertAdjustedDiffCategory(IReadOnlyDictionary<string, int> categoryCounts, string category)
    {
        Assert.True(categoryCounts.TryGetValue(category, out var count), "Missing category: " + category);
        Assert.Equal(12, count);
    }

    private static void AssertShiftDiffCategory(IReadOnlyDictionary<string, int> categoryCounts, string category)
    {
        Assert.True(categoryCounts.TryGetValue(category, out var count), "Missing category: " + category);
        Assert.Equal(24, count);
    }
}
