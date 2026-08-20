using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Tests;

public sealed class ChildMirrorStrategyCatalogTests
{
    [Fact]
    public void StrategyIds_IncludeChildMirrorVariantsForEachAssetAndLookback()
    {
        foreach (var assetSymbol in new[] { "BTC", "ETH", "SOL" })
        {
            var expectedChildProgressLookbacks = GetExpectedChildProgressLookbacks(assetSymbol);
            var expectedChildProgressRoiLookbacks = GetExpectedChildProgressRoiLookbacks(assetSymbol);
            var childVariants = StrategyIds.UpDown5mStrategyVariants
                .Where(variant =>
                    string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                    variant.Behavior == BtcUpDown5mStrategyBehavior.ChildMirror)
                .OrderBy(variant => variant.DecisionDepth)
                .ToArray();
            var childProgressVariants = StrategyIds.UpDown5mStrategyVariants
                .Where(variant =>
                    string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                    variant.Behavior == BtcUpDown5mStrategyBehavior.ChildProgressMirror)
                .OrderBy(variant => variant.DecisionDepth)
                .ToArray();
            var childRoiVariants = StrategyIds.UpDown5mStrategyVariants
                .Where(variant =>
                    string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                    variant.Behavior == BtcUpDown5mStrategyBehavior.ChildRoiMirror)
                .OrderBy(variant => variant.DecisionDepth)
                .ToArray();
            var childProgressRoiVariants = StrategyIds.UpDown5mStrategyVariants
                .Where(variant =>
                    string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                    variant.Behavior == BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror)
                .OrderBy(variant => variant.DecisionDepth)
                .ToArray();

            Assert.Equal(Enumerable.Range(1, 24), childVariants.Select(variant => variant.DecisionDepth));
            Assert.Equal(expectedChildProgressLookbacks, childProgressVariants.Select(variant => variant.DecisionDepth));
            Assert.Equal(Enumerable.Range(1, 24), childRoiVariants.Select(variant => variant.DecisionDepth));
            Assert.Equal(expectedChildProgressRoiLookbacks, childProgressRoiVariants.Select(variant => variant.DecisionDepth));
            Assert.All(childVariants, variant =>
            {
                Assert.EndsWith(" Child", variant.Name, StringComparison.Ordinal);
                Assert.DoesNotContain("Progress", variant.Name, StringComparison.Ordinal);
                Assert.Contains("non-Futures", variant.Description, StringComparison.Ordinal);
                Assert.Equal($"{assetSymbol} Up/Down 5m Child", variant.Category);
            });
            Assert.All(childProgressVariants, variant =>
            {
                Assert.EndsWith(" Child Progress", variant.Name, StringComparison.Ordinal);
                Assert.Contains("non-Futures", variant.Description, StringComparison.Ordinal);
                Assert.Equal($"{assetSymbol} Up/Down 5m Child Progress", variant.Category);
            });
            Assert.All(childRoiVariants, variant =>
            {
                Assert.EndsWith(" Child ROI", variant.Name, StringComparison.Ordinal);
                Assert.DoesNotContain("Progress", variant.Name, StringComparison.Ordinal);
                Assert.Contains("non-Futures", variant.Description, StringComparison.Ordinal);
                Assert.Equal($"{assetSymbol} Up/Down 5m Child ROI", variant.Category);
            });
            Assert.All(childProgressRoiVariants, variant =>
            {
                Assert.EndsWith(" Child Progress ROI", variant.Name, StringComparison.Ordinal);
                Assert.Contains("non-Futures", variant.Description, StringComparison.Ordinal);
                Assert.Equal($"{assetSymbol} Up/Down 5m Child Progress ROI", variant.Category);
            });
        }
    }

    [Fact]
    public void StrategyDisplayCategories_GroupsChildMirrorVariants()
    {
        Assert.Equal(
            "ETH Up or Down 5m Child",
            StrategyDisplayCategories.GetCategory("ETH Up or Down 5m 7 Child"));
        Assert.Equal(
            "SOL Up or Down 5m Child Progress",
            StrategyDisplayCategories.GetCategory("SOL Up or Down 5m 24 Child Progress"));
        Assert.Equal(
            "BTC Up or Down 5m Child ROI",
            StrategyDisplayCategories.GetCategory("BTC Up or Down 5m 3 Child ROI"));
        Assert.Equal(
            "ETH Up or Down 5m Child Progress ROI",
            StrategyDisplayCategories.GetCategory("ETH Up or Down 5m 12 Child Progress ROI"));
    }

    private static int[] GetExpectedChildProgressLookbacks(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => [],
            "ETH" => [],
            "SOL" => [15, 17, 19, 20, 21, 22, 23, 24],
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, null)
        };
    }

    private static int[] GetExpectedChildProgressRoiLookbacks(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => [4, 5, 6, 7, 8, 9, 22],
            "ETH" => [1, 20],
            "SOL" => [9],
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, null)
        };
    }
}
