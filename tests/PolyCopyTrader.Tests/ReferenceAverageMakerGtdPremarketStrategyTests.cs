using System.Globalization;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class ReferenceAverageMakerGtdPremarketStrategyTests
{
    private static readonly int[] Thresholds =
        [.. Enumerable.Range(1, 10), .. Enumerable.Range(3, 18).Select(value => value * 5)];

    [Fact]
    public void StrategyIds_ContainExactEthNeutralReferenceAverageMakerGtdCloneGrid()
    {
        var makerVariants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdMakerGtdPremarket)
            .ToArray();

        Assert.Equal(Thresholds.Length, makerVariants.Length);
        Assert.Equal(Thresholds.Length, makerVariants.Select(variant => variant.Id).Distinct().Count());
        Assert.Equal(
            Thresholds.Length,
            makerVariants.Select(variant => variant.Code).Distinct(StringComparer.Ordinal).Count());

        foreach (var threshold in Thresholds)
        {
            var thresholdText = threshold.ToString(CultureInfo.InvariantCulture);
            var source = Assert.Single(StrategyIds.UpDown5mStrategyVariants, variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket &&
                string.Equals(variant.ReferenceAssetSymbol, "ETH", StringComparison.Ordinal) &&
                variant.FixedOutcome is null &&
                variant.DiffCounterTriggerOutcome is null &&
                variant.LowerEnterSourceStrategyId is null &&
                variant.DecisionThresholdBps == threshold);
            var maker = Assert.Single(makerVariants, variant => variant.DecisionThresholdBps == threshold);
            Assert.True(MakerGtdPaperExecutionContract.IsApprovedCurrentStrategyVariant(maker));

            Assert.Equal(
                Guid.Parse($"b7c50005-0000-4000-8223-{100 + threshold:000000000000}"),
                maker.Id);
            Assert.Equal(
                $"eth_up_down_5m_reference_average_bps_{thresholdText}_maker_gtd_premarket",
                maker.Code);
            Assert.Equal(
                $"ETH Up or Down 5m {thresholdText} bps Reference Average Maker GTD Premarket",
                maker.Name);
            Assert.Equal("ETH Up/Down 5m Bps Reference Average Maker GTD Premarket", maker.Category);
            Assert.Equal(BtcUpDown5mStrategyDirection.Dynamic, maker.Direction);
            Assert.Equal(-30, maker.EntryDelaySeconds);
            Assert.Equal(threshold, maker.DecisionDepth);
            Assert.Equal(threshold, maker.DecisionThresholdBps);
            Assert.Equal(BtcUpDownMarketInterval.FiveMinutes, maker.MarketInterval);
            Assert.Equal("ETH", maker.ReferenceAssetSymbol);
            Assert.Null(maker.FixedOutcome);
            Assert.Null(maker.DiffCounterTriggerOutcome);
            Assert.True(maker.PaperOnly);
            Assert.Null(maker.FakMaximumOrderPrice);
            Assert.Null(maker.LowerEnterSourceStrategyId);
            Assert.Equal(0.99m, maker.MakerMaximumOrderPrice);
            Assert.Contains("at most ten attempts", maker.Description, StringComparison.Ordinal);
            Assert.Contains("post-only GTD BUY", maker.Description, StringComparison.Ordinal);
            Assert.Contains("maximum-resting formula floor_to_tick(min(bestAsk - tick, cap))", maker.Description, StringComparison.Ordinal);
            Assert.Contains("for a tick-aligned venue book", maker.Description, StringComparison.Ordinal);
            Assert.Contains("capped at 0.99", maker.Description, StringComparison.Ordinal);
            Assert.Contains("expires one minute before market end", maker.Description, StringComparison.Ordinal);
            Assert.Contains("By explicit user approval", maker.Description, StringComparison.Ordinal);
            Assert.Contains("closed ordinary-Paper exception", maker.Description, StringComparison.Ordinal);
            Assert.Contains("TouchNoDepth", maker.Description, StringComparison.Ordinal);
            Assert.Contains("at or below the BUY limit", maker.Description, StringComparison.Ordinal);
            Assert.Contains("ordinary Paper orders, PnL, win rate, and performance", maker.Description, StringComparison.Ordinal);
            Assert.Contains(
                "optimistic TouchNoDepth Paper; not Live-equivalent; may overstate fills",
                maker.Description,
                StringComparison.Ordinal);
            Assert.Contains("Live submission is disabled", maker.Description, StringComparison.Ordinal);
            Assert.Contains("no alias, clone, descendant, future strategy", maker.Description, StringComparison.Ordinal);
            Assert.Contains(
                "different execution source, predicate mismatch, or changed execution semantic",
                maker.Description,
                StringComparison.Ordinal);

            Assert.Equal(source.Direction, maker.Direction);
            Assert.Equal(source.EntryDelaySeconds, maker.EntryDelaySeconds);
            Assert.Equal(source.DecisionDepth, maker.DecisionDepth);
            Assert.Equal(source.DecisionThresholdBps, maker.DecisionThresholdBps);
            Assert.Equal(source.MarketInterval, maker.MarketInterval);
            Assert.Equal(source.PreOpenLifetimeMode, maker.PreOpenLifetimeMode);
            Assert.Equal(source.FixedOutcome, maker.FixedOutcome);
            Assert.Equal(source.ReferenceAssetSymbol, maker.ReferenceAssetSymbol);
            Assert.Equal(source.MakerMinBestAskExclusive, maker.MakerMinBestAskExclusive);
            Assert.Equal(source.ShiftDiffCount, maker.ShiftDiffCount);
            Assert.Equal(source.DiffCounterTriggerOutcome, maker.DiffCounterTriggerOutcome);
            Assert.Equal(source.BaseSignalStrategyId, maker.BaseSignalStrategyId);
            Assert.Equal(source.ConfirmationSignalStrategyId, maker.ConfirmationSignalStrategyId);
            Assert.Equal(source.RequiredReferenceAverageWindow, maker.RequiredReferenceAverageWindow);
            Assert.Equal<Guid?>(maker.Id, StrategyIds.TryGetStrategyIdByCode(maker.Code));

            Assert.False(source.PaperOnly);
            Assert.Null(source.FakMaximumOrderPrice);
            Assert.Null(source.MakerMaximumOrderPrice);
            Assert.Equal(
                $"eth_up_down_5m_reference_average_bps_{thresholdText}_fak_premarket",
                source.Code);
            Assert.Equal(
                $"ETH Up or Down 5m {thresholdText} bps Reference Average Premarket",
                source.Name);
        }
    }

    [Fact]
    public void ExecutionContract_RejectsVariantOutsideExactClosedException()
    {
        var approved = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_reference_average_bps_9_maker_gtd_premarket");

        Assert.False(MakerGtdPaperExecutionContract.IsApprovedCurrentStrategyVariant(
            approved with { Id = Guid.NewGuid() }));
        Assert.False(MakerGtdPaperExecutionContract.IsApprovedCurrentStrategyVariant(
            approved with { ReferenceAssetSymbol = "BTC" }));
        Assert.False(MakerGtdPaperExecutionContract.IsApprovedCurrentStrategyVariant(
            approved with { MakerMaximumOrderPrice = 0.50m }));
        Assert.False(MakerGtdPaperExecutionContract.IsApprovedCurrentStrategyVariant(
            approved with { EntryDelaySeconds = -86_400 }));
        Assert.False(MakerGtdPaperExecutionContract.IsApprovedCurrentStrategyVariant(
            approved with { MarketInterval = BtcUpDownMarketInterval.FifteenMinutes }));
    }

    [Fact]
    public void StrategyDisplayCategories_KeepMakerGtdClonesSeparateFromFakSources()
    {
        var categories = StrategyIds.UpDown5mStrategyVariants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdMakerGtdPremarket)
            .GroupBy(variant => StrategyDisplayCategories.GetCategory(variant.Name))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var category = Assert.Single(categories);
        Assert.Equal("ETH Up or Down 5m Bps Reference Average Maker GTD Premarket", category.Key);
        Assert.Equal(Thresholds.Length, category.Value);
    }

    [Fact]
    public void PostgresSchema_SeedsExactEthMakerGtdGridWithoutOverwritingRuntimeSettings()
    {
        var statement = Assert.Single(
            PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql),
            sql => sql.Contains("'_maker_gtd_premarket'", StringComparison.Ordinal));
        var normalizedStatement = statement.Replace("\r\n", "\n", StringComparison.Ordinal);
        var conflictClause = statement[statement.IndexOf("ON CONFLICT", StringComparison.Ordinal)..];

        Assert.Contains("b7c50005-0000-4000-8223-", statement, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 10)", statement, StringComparison.Ordinal);
        Assert.Contains("generate_series(15, 100, 5)", statement, StringComparison.Ordinal);
        Assert.Contains("100 + threshold_bps", statement, StringComparison.Ordinal);
        Assert.Contains(
            "'eth_up_down_5m_reference_average_bps_' || threshold_bps::text || '_maker_gtd_premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "'ETH Up or Down 5m ' || threshold_bps::text || ' bps Reference Average Maker GTD Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains("at most ten attempts", statement, StringComparison.Ordinal);
        Assert.Contains("post-only GTD BUY", statement, StringComparison.Ordinal);
        Assert.Contains("maximum-resting formula floor_to_tick(min(bestAsk - tick, cap))", statement, StringComparison.Ordinal);
        Assert.Contains("for a tick-aligned venue book", statement, StringComparison.Ordinal);
        Assert.Contains("capped at 0.99", statement, StringComparison.Ordinal);
        Assert.Contains("expires one minute before market end", statement, StringComparison.Ordinal);
        Assert.Contains("By explicit user approval", statement, StringComparison.Ordinal);
        Assert.Contains("closed ordinary-Paper exception", statement, StringComparison.Ordinal);
        Assert.Contains("TouchNoDepth", statement, StringComparison.Ordinal);
        Assert.Contains("at or below the BUY limit", statement, StringComparison.Ordinal);
        Assert.Contains("ordinary Paper orders, PnL, win rate, and performance", statement, StringComparison.Ordinal);
        Assert.Contains(
            "optimistic TouchNoDepth Paper; not Live-equivalent; may overstate fills",
            statement,
            StringComparison.Ordinal);
        Assert.Contains("Live submission is disabled", statement, StringComparison.Ordinal);
        Assert.Contains("no alias, clone, descendant, future strategy", statement, StringComparison.Ordinal);
        Assert.Contains(
            "different execution source, predicate mismatch, or changed execution semantic",
            statement,
            StringComparison.Ordinal);
        Assert.Contains("    true,\n    false,\n    1.00,", normalizedStatement, StringComparison.Ordinal);
        Assert.DoesNotContain("enabled =", conflictClause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("live_stakes =", conflictClause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paper_stake_amount =", conflictClause, StringComparison.OrdinalIgnoreCase);
    }
}
