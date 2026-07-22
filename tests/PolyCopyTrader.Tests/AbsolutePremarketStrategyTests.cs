using System.Globalization;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class AbsolutePremarketStrategyTests
{
    [Fact]
    public void StrategyIds_ContainCompleteAbsolutePremarketGrid()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.LowerEnterSourceStrategyId is null)
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.AbsoluteBpsThresholdFakPremarket)
            .ToArray();

        Assert.Equal(360, variants.Length);
        Assert.Equal(360, variants.Select(variant => variant.Id).Distinct().Count());
        Assert.Equal(360, variants.Select(variant => variant.Code).Distinct(StringComparer.Ordinal).Count());

        foreach (var (assetSymbol, idGroup) in new[] { ("BTC", 8206), ("ETH", 8207), ("SOL", 8208) })
        {
            var assetVariants = variants
                .Where(variant => string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(120, assetVariants.Length);

            for (var lookbackHours = 1; lookbackHours <= 24; lookbackHours++)
            {
                for (var thresholdBps = 1; thresholdBps <= 5; thresholdBps++)
                {
                    var code = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{assetSymbol.ToLowerInvariant()}_up_down_5m_{lookbackHours}h_absolute_bps_{thresholdBps}_fak_premarket");
                    var variant = Assert.Single(assetVariants, item => item.Code == code);
                    Assert.Equal(
                        Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{lookbackHours * 100 + thresholdBps:000000000000}"),
                        variant.Id);
                    Assert.Equal(
                        $"{assetSymbol} Up or Down 5m {lookbackHours}h {thresholdBps} bps Absolute Premarket",
                        variant.Name);
                    Assert.Equal(BtcUpDown5mStrategyDirection.Dynamic, variant.Direction);
                    Assert.Equal(-30, variant.EntryDelaySeconds);
                    Assert.Equal(lookbackHours, variant.DecisionDepth);
                    Assert.Equal(thresholdBps, variant.DecisionThresholdBps);
                    Assert.Equal($"{assetSymbol} Up/Down 5m Absolute Premarket", variant.Category);
                    Assert.Null(variant.FixedOutcome);
                }
            }
        }
    }

    [Fact]
    public void StrategyDisplayCategories_CreateOneAbsoluteCategoryPerAsset()
    {
        var categories = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.LowerEnterSourceStrategyId is null)
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.AbsoluteBpsThresholdFakPremarket)
            .Select(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(category => category, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "BTC Up or Down 5m Absolute Premarket",
                "ETH Up or Down 5m Absolute Premarket",
                "SOL Up or Down 5m Absolute Premarket"
            },
            categories);
    }

    [Fact]
    public void PostgresSchema_SeedsAbsolutePremarketGridPaperOnly()
    {
        var statement = Assert.Single(
            PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql),
            sql => sql.Contains("'_up_down_5m_' || lookback_hours::text || 'h_absolute_bps_'", StringComparison.Ordinal));
        var conflictClause = statement[statement.IndexOf("ON CONFLICT", StringComparison.Ordinal)..];

        Assert.Contains("('BTC', '8206')", statement, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8207')", statement, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8208')", statement, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 24)", statement, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 5)", statement, StringComparison.Ordinal);
        Assert.Contains("lookback_hours * 100 + threshold_bps", statement, StringComparison.Ordinal);
        Assert.Contains("    true,\n    false,\n    1.00,", statement.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("enabled =", conflictClause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("live_stakes =", conflictClause, StringComparison.OrdinalIgnoreCase);
    }
}
