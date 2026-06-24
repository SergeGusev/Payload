using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Tests;

public sealed class MiddleBpsThresholdCatalogTests
{
    [Fact]
    public void MiddleBpsThresholdsUseFiveBpsStep()
    {
        var expectedThresholds = Enumerable.Range(1, 20)
            .Select(value => (decimal)(value * 5))
            .ToArray();
        var depths = new[] { 100, 90, 80, 70, 60, 50, 40, 30, 20, 10 };

        AssertMiddleThresholds(StrategyIds.BtcUpDown5mVariants, "BTC", depths, expectedThresholds);
        AssertMiddleThresholds(StrategyIds.CryptoUpDown5mVariants, "ETH", depths, expectedThresholds);
        AssertMiddleThresholds(StrategyIds.CryptoUpDown5mVariants, "SOL", depths, expectedThresholds);

        Assert.DoesNotContain(StrategyIds.UpDown5mStrategyVariants, variant =>
            variant.Code.Contains("_middle_", StringComparison.Ordinal) &&
            variant.DecisionThresholdBps is > 0m &&
            variant.DecisionThresholdBps % 5m != 0m);
    }

    private static void AssertMiddleThresholds(
        IEnumerable<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol,
        IReadOnlyList<int> depths,
        decimal[] expectedThresholds)
    {
        foreach (var depth in depths)
        {
            Assert.Equal(
                expectedThresholds,
                variants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReference &&
                        variant.DecisionDepth == depth &&
                        variant.DecisionThresholdBps is > 0m)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());

            Assert.Equal(
                expectedThresholds,
                variants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceInstant &&
                        variant.DecisionDepth == depth &&
                        variant.DecisionThresholdBps is > 0m)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
        }
    }
}
