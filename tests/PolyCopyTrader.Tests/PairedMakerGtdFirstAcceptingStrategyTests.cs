using System.Text.RegularExpressions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class PairedMakerGtdFirstAcceptingStrategyTests
{
    private static readonly ExpectedLeg[] ExpectedLegs =
    [
        new("BTC", BtcUpDownFixedOutcome.Up, 101, 102, 0.50m),
        new("BTC", BtcUpDownFixedOutcome.Down, 102, 101, 0.49m),
        new("ETH", BtcUpDownFixedOutcome.Up, 201, 202, 0.50m),
        new("ETH", BtcUpDownFixedOutcome.Down, 202, 201, 0.49m),
        new("SOL", BtcUpDownFixedOutcome.Up, 301, 302, 0.50m),
        new("SOL", BtcUpDownFixedOutcome.Down, 302, 301, 0.49m)
    ];

    [Fact]
    public void StrategyIds_ContainThreeExactPairedMakerGtdFirstAcceptingPairs()
    {
        var variants = StrategyIds.PairedMakerGtdFirstAcceptingVariants.ToArray();

        Assert.Equal(6, variants.Length);
        Assert.Equal(6, variants.Select(variant => variant.Id).Distinct().Count());
        Assert.Equal(6, variants.Select(variant => variant.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.All(variants, variant => Assert.Contains(variant, StrategyIds.UpDown5mStrategyVariants));

        foreach (var expected in ExpectedLegs)
        {
            var outcomeName = expected.Outcome.ToString();
            var outcomeCode = outcomeName.ToLowerInvariant();
            var id = Id(expected.IdSuffix);
            var pairedId = Id(expected.PairedIdSuffix);
            var variant = Assert.Single(variants, candidate => candidate.Id == id);

            Assert.Equal(
                $"{expected.Asset.ToLowerInvariant()}_up_down_5m_{outcomeCode}_paired_maker_gtd_first_accepting",
                variant.Code);
            Assert.Equal(
                $"{expected.Asset} Up or Down 5m {outcomeName} Paired Maker GTD First Accepting",
                variant.Name);
            Assert.Equal(
                $"{expected.Asset} Up/Down 5m Paired Maker GTD First Accepting",
                variant.Category);
            Assert.Equal(BtcUpDown5mStrategyDirection.Dynamic, variant.Direction);
            Assert.Equal(StrategyIds.PairedMakerGtdNominalEntryDelaySeconds, variant.EntryDelaySeconds);
            Assert.Equal(BtcUpDown5mStrategyEntryTiming.FirstAcceptingOrders, variant.EntryTiming);
            Assert.Equal(BtcUpDown5mStrategyBehavior.PairedFixedOutcomeMakerGtdFirstAccepting, variant.Behavior);
            Assert.Equal(BtcUpDownMarketInterval.FiveMinutes, variant.MarketInterval);
            Assert.Equal(expected.Outcome, variant.FixedOutcome);
            Assert.Equal(expected.Asset, variant.ReferenceAssetSymbol);
            Assert.True(variant.PaperOnly);
            Assert.Null(variant.FixedLimitPrice);
            Assert.Null(variant.FakMaximumOrderPrice);
            Assert.Equal(expected.MaximumOrderPrice, variant.MakerMaximumOrderPrice);
            Assert.Equal(pairedId, variant.PairedStrategyId);
            Assert.Equal<Guid?>(id, StrategyIds.TryGetStrategyIdByCode(variant.Code));
            Assert.True(PairedMakerGtdPaperExecutionContract.IsApprovedStrategyVariant(variant));

            Assert.Contains("equal-share paired Maker GTD strategy", variant.Description, StringComparison.Ordinal);
            Assert.Contains("first observed market snapshot with acceptingOrders=true", variant.Description, StringComparison.Ordinal);
            Assert.Contains("nominally one day before market start", variant.Description, StringComparison.Ordinal);
            Assert.Contains("PostOnly GTD BUY", variant.Description, StringComparison.Ordinal);
            Assert.Contains("maximum-resting formula floor_to_tick(min(bestAsk - tick, cap))", variant.Description, StringComparison.Ordinal);
            Assert.Contains($"capped at {expected.MaximumOrderPrice:0.00}", variant.Description, StringComparison.Ordinal);
            Assert.Contains("same requested share quantity", variant.Description, StringComparison.Ordinal);
            Assert.Contains("submission is not atomic and there is no rollback", variant.Description, StringComparison.Ordinal);
            Assert.Contains("original effective expiry one minute before market end", variant.Description, StringComparison.Ordinal);
            Assert.Contains("ordinary Paper orders, PnL, win rate, and performance", variant.Description, StringComparison.Ordinal);
            Assert.Contains(StrategyIds.OptimisticTouchNoDepthPaperLabel, variant.Description, StringComparison.Ordinal);
            Assert.Contains("Maker rebates are not modeled and are not included in Paper PnL", variant.Description, StringComparison.Ordinal);
            Assert.Contains("Live submission is disabled", variant.Description, StringComparison.Ordinal);
        }

        foreach (var assetGroup in variants.GroupBy(variant => variant.ReferenceAssetSymbol, StringComparer.Ordinal))
        {
            var pair = assetGroup.ToArray();
            Assert.Equal(2, pair.Length);
            Assert.Equal(0.99m, pair.Sum(variant => variant.MakerMaximumOrderPrice ?? 0m));
            Assert.Equal(pair[1].Id, pair[0].PairedStrategyId);
            Assert.Equal(pair[0].Id, pair[1].PairedStrategyId);
        }

        Assert.Equal(StrategyIds.AllStrategyIds.Count, StrategyIds.AllStrategyIds.Distinct().Count());
        var representative = variants[0];
        Assert.False(PairedMakerGtdPaperExecutionContract.IsApprovedStrategyVariant(
            representative with { MakerMaximumOrderPrice = 0.49m }));
        Assert.False(PairedMakerGtdPaperExecutionContract.IsApprovedStrategyVariant(
            representative with { EntryTiming = BtcUpDown5mStrategyEntryTiming.MarketStartOffset }));
        Assert.False(PairedMakerGtdPaperExecutionContract.IsApprovedStrategyVariant(
            representative with { PairedStrategyId = Guid.NewGuid() }));
        Assert.All(
            StrategyIds.UpDown5mStrategyVariants.Where(variant =>
                variant.Behavior != BtcUpDown5mStrategyBehavior.PairedFixedOutcomeMakerGtdFirstAccepting),
            variant =>
            {
                Assert.Equal(BtcUpDown5mStrategyEntryTiming.MarketStartOffset, variant.EntryTiming);
                Assert.Null(variant.PairedStrategyId);
            });
    }

    [Fact]
    public void PostgresSchema_SeedsExactSixLegFamilyWithoutOverwritingRuntimeSettings()
    {
        var statement = Assert.Single(
            PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql),
            sql => sql.Contains("b7c50005-0000-4000-8224-", StringComparison.Ordinal));
        var normalizedStatement = statement.Replace("\r\n", "\n", StringComparison.Ordinal);
        var compactStatement = Regex.Replace(statement, @"\s+", " ");
        var conflictClause = statement[statement.IndexOf("ON CONFLICT", StringComparison.Ordinal)..];

        Assert.Contains("WITH legs(asset_symbol, outcome_name, outcome_code, id_suffix, paired_outcome_name, maximum_order_price)", statement, StringComparison.Ordinal);
        foreach (var expected in ExpectedLegs)
        {
            var outcomeName = expected.Outcome.ToString();
            var outcomeCode = outcomeName.ToLowerInvariant();
            var pairedOutcomeName = expected.Outcome == BtcUpDownFixedOutcome.Up ? "Down" : "Up";
            Assert.Contains(
                $"('{expected.Asset}', '{outcomeName}', '{outcomeCode}', {expected.IdSuffix}, " +
                $"'{pairedOutcomeName}', '{expected.MaximumOrderPrice:0.00}')",
                compactStatement,
                StringComparison.Ordinal);
        }

        Assert.Contains("_paired_maker_gtd_first_accepting'", statement, StringComparison.Ordinal);
        Assert.Contains("first observed market snapshot with acceptingOrders=true", statement, StringComparison.Ordinal);
        Assert.Contains("nominally one day before market start", statement, StringComparison.Ordinal);
        Assert.Contains("same requested share quantity", statement, StringComparison.Ordinal);
        Assert.Contains("submission is not atomic and there is no rollback", statement, StringComparison.Ordinal);
        Assert.Contains("original effective expiry one minute before market end", statement, StringComparison.Ordinal);
        Assert.Contains("ordinary Paper orders, PnL, win rate, and performance", statement, StringComparison.Ordinal);
        Assert.Contains(StrategyIds.OptimisticTouchNoDepthPaperLabel, statement, StringComparison.Ordinal);
        Assert.Contains("Maker rebates are not modeled and are not included in Paper PnL", statement, StringComparison.Ordinal);
        Assert.Contains("Live submission is disabled", statement, StringComparison.Ordinal);
        Assert.Contains("    true,\n    false,\n    1.00,", normalizedStatement, StringComparison.Ordinal);
        Assert.DoesNotContain("enabled =", conflictClause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("live_stakes =", conflictClause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paper_stake_amount =", conflictClause, StringComparison.OrdinalIgnoreCase);
    }

    private static Guid Id(int suffix)
    {
        return Guid.Parse($"b7c50005-0000-4000-8224-{suffix:000000000000}");
    }

    private sealed record ExpectedLeg(
        string Asset,
        BtcUpDownFixedOutcome Outcome,
        int IdSuffix,
        int PairedIdSuffix,
        decimal MaximumOrderPrice);
}
