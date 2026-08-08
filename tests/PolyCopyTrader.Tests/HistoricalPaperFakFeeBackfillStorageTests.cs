using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;
using System.Runtime.CompilerServices;

namespace PolyCopyTrader.Tests;

public sealed class HistoricalPaperFakFeeBackfillStorageTests
{
    private const string HistoricalPrefix = "historical-current-paper-model-v1:";

    [Fact]
    public void Validation_AcceptsExactFeeFreeCalculatedShape()
    {
        var update = CreateUpdate() with
        {
            EvaluatedFill = CreateUpdate().EvaluatedFill with
            {
                FeeUsd = 0m,
                FeeCalculationSource = HistoricalPrefix +
                    PolymarketFeeCalculationConstants.FeeFreeMarketCalculationSource,
                FeeRate = null,
                FeeExponent = null,
                FeeTakerOnly = null
            }
        };

        PostgresAppRepository.ValidateHistoricalPaperFakFeeBackfillUpdates([update]);
    }

    [Fact]
    public void Validation_RejectsCalculatedFeeWithoutHistoricalProvenance()
    {
        var update = CreateUpdate() with
        {
            EvaluatedFill = CreateUpdate().EvaluatedFill with
            {
                FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource
            }
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            PostgresAppRepository.ValidateHistoricalPaperFakFeeBackfillUpdates([update]));

        Assert.Equal("updates", exception.ParamName);
    }

    [Fact]
    public void Validation_AcceptsStructuralCalculatorUnavailableAndRejectsLookupTransient()
    {
        var baseline = CreateUpdate();
        var structural = baseline with
        {
            EvaluatedFill = baseline.EvaluatedFill with
            {
                FeeUsd = 0m,
                FeeAccountingStatus = FeeAccountingStatus.CalculationUnavailable.ToString(),
                FeeCalculationSource = HistoricalPrefix +
                    PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
                FeeRate = null,
                FeeExponent = null,
                FeeTakerOnly = null
            }
        };
        PostgresAppRepository.ValidateHistoricalPaperFakFeeBackfillUpdates([structural]);

        var transient = structural with
        {
            EvaluatedFill = structural.EvaluatedFill with
            {
                FeeCalculationSource = HistoricalPrefix +
                    PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource
            }
        };
        Assert.Throws<ArgumentException>(() =>
            PostgresAppRepository.ValidateHistoricalPaperFakFeeBackfillUpdates([transient]));
    }

    [Fact]
    public void Validation_LeavesAnomalousLegacyFillForAtomicSqlConflictClassification()
    {
        var baseline = CreateUpdate();
        var anomalous = baseline with
        {
            Expected = baseline.Expected with
            {
                Order = baseline.Expected.Order with { Status = PaperOrderStatus.Pending },
                Fill = baseline.Expected.Fill with { FeeUsd = 0.01m }
            }
        };

        PostgresAppRepository.ValidateHistoricalPaperFakFeeBackfillUpdates([anomalous]);
    }

    [Fact]
    public void Validation_LeavesDuplicateKeysForAtomicSqlConflictClassification()
    {
        var update = CreateUpdate();

        PostgresAppRepository.ValidateHistoricalPaperFakFeeBackfillUpdates([update, update]);
    }

    [Fact]
    public void ApplySql_UsesFillChainAndEnforcesAllFourDependentUpdates()
    {
        var sql = PostgresAppRepository.HistoricalPaperFakFeeBackfillApplySql;

        Assert.DoesNotContain("paper_order.notional_usd = run.stake_usd", sql, StringComparison.Ordinal);
        Assert.Contains("run.stake_usd = round(fill.price * fill.size_shares, 8)", sql, StringComparison.Ordinal);
        Assert.Contains("settlement.cost_basis_usd = round(fill.price * fill.size_shares, 8)", sql, StringComparison.Ordinal);
        Assert.Contains("FROM requested sibling_request", sql, StringComparison.Ordinal);
        Assert.Contains("FROM public.paper_orders sibling_order", sql, StringComparison.Ordinal);
        Assert.Contains("actual_fill_fee = 0", sql, StringComparison.Ordinal);
        Assert.Contains("FROM fill_updates", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN fill_updates", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("updated_at_utc =", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateQuery_AllowsOnlyTwoPurePaperSourcesAndExcludesShadowAndGtd()
    {
        var source = ReadRepositorySource();
        var start = source.IndexOf(
            "GetHistoricalPaperFakFeeBackfillCandidatesAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "ApplyHistoricalPaperFakFeeBackfillBatchAsync",
            start,
            StringComparison.Ordinal);
        var candidateMethod = source[start..end];

        Assert.Contains("paper_order.execution_source IN", candidateMethod, StringComparison.Ordinal);
        Assert.Contains("HistoricalPaperFakDirectSource", candidateMethod, StringComparison.Ordinal);
        Assert.Contains("HistoricalPaperFakChildSource", candidateMethod, StringComparison.Ordinal);
        Assert.Contains("fill.fee_accounting_status", candidateMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("paper_order.status IN", candidateMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("fill.fee_usd = 0", candidateMethod, StringComparison.Ordinal);
        Assert.Contains(
            "private const string HistoricalPaperFakDirectSource = \"btc_updown5m_fak_taker_paper\";",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private const string HistoricalPaperFakChildSource = \"btc_updown5m_child_mirror_fak_paper\";",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("paper_live_shadow", candidateMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("btc_updown5m_gtd_limit", candidateMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_DeclaresLegacyCandidateIndexAndPermanentFeeOnlyGuards()
    {
        Assert.Contains(
            "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_fills_historical_fak_fee_backfill",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE fee_accounting_status = 'LegacyUnknown'",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_orders_historical_fak_wallet_asset",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ON paper_orders(copied_trader_wallet, asset_id)",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);

        var dashboard = DashboardProjectionSchema.SchemaSql;
        Assert.Contains("OLD.paper_order_id IS NOT DISTINCT FROM NEW.paper_order_id", dashboard, StringComparison.Ordinal);
        Assert.Contains("OLD.stake_usd IS NOT DISTINCT FROM NEW.stake_usd", dashboard, StringComparison.Ordinal);
        Assert.Contains("OLD.cost_basis_usd IS NOT DISTINCT FROM NEW.cost_basis_usd", dashboard, StringComparison.Ordinal);

        var copiedPerformance = PaperCopiedTraderPerformanceProjectionSchema.SchemaSql;
        Assert.Contains("OLD.paper_order_id IS NOT DISTINCT FROM NEW.paper_order_id", copiedPerformance, StringComparison.Ordinal);
        Assert.Contains("OLD.category IS NOT DISTINCT FROM NEW.category", copiedPerformance, StringComparison.Ordinal);
    }

    private static HistoricalPaperFakFeeBackfillUpdate CreateUpdate()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 7, 20, 0, 0, TimeSpan.Zero);
        var orderId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var order = new PaperOrder(
            orderId,
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            "strategy:test",
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            "asset",
            "condition",
            "Up",
            0.50m,
            2m,
            1m,
            nowUtc,
            nowUtc.AddSeconds(5),
            nowUtc,
            StrategyId: Guid.Parse("30000000-0000-0000-0000-000000000001"),
            ExecutionSource: "btc_updown5m_fak_taker_paper");
        var fill = new PaperFill(
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            orderId,
            0.50m,
            2m,
            nowUtc,
            "historical fill");
        var evaluated = fill with
        {
            FeeUsd = 0.035m,
            FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
            FeeLiquidityRole = FeeLiquidityRole.Taker.ToString(),
            FeeCalculationSource = HistoricalPrefix +
                PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
            FeeRate = 0.07m,
            FeeExponent = 1,
            FeeTakerOnly = true,
            FeeCalculatedAtUtc = nowUtc.AddHours(1)
        };
        return new HistoricalPaperFakFeeBackfillUpdate(
            new HistoricalPaperFakFeeBackfillCandidate(order, fill),
            evaluated);
    }

    private static string ReadRepositorySource([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)
            ?? throw new InvalidOperationException("The test source directory was not resolved.");
        var repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));
        return File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.HistoricalPaperFakFeeBackfill.cs"));
    }
}
