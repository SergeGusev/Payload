using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Tests;

public sealed class StrategyDisplayCategoryTests
{
    [Fact]
    public void FixedOutcomeBpsInstantStrategiesHaveDedicatedDisplayCategories()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant)
            .ToArray();

        Assert.Equal(300, variants.Length);

        var categoryCounts = variants
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        AssertFixedOutcomeCategory(categoryCounts, "BTC Up or Down 5m Up Bps");
        AssertFixedOutcomeCategory(categoryCounts, "BTC Up or Down 5m Down Bps");
        AssertFixedOutcomeCategory(categoryCounts, "ETH Up or Down 5m Up Bps");
        AssertFixedOutcomeCategory(categoryCounts, "ETH Up or Down 5m Down Bps");
        AssertFixedOutcomeCategory(categoryCounts, "SOL Up or Down 5m Up Bps");
        AssertFixedOutcomeCategory(categoryCounts, "SOL Up or Down 5m Down Bps");
        Assert.Equal(6, categoryCounts.Count);
    }

    [Theory]
    [InlineData("BTC Up or Down 5m Skip 7 bps Instant", "BTC Up or Down 5m Skip")]
    [InlineData("ETH Up or Down 5m Middle 1 47 bps Instant", "ETH Up or Down 5m Middle")]
    [InlineData("SOL Up or Down 5m Binance 42 bps Instant", "SOL Up or Down 5m Binance")]
    [InlineData("BTC Up or Down 5m Up", "BTC Up or Down 5m Other")]
    [InlineData("BTC Up or Down 5m Down Maker 50", "BTC Up or Down 5m Other")]
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
}
