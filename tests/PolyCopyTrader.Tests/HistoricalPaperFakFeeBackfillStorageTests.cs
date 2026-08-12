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
    public void ApplySql_UsesExactFullAndRunOnlyShapesWithoutSyntheticAccounting()
    {
        var sql = PostgresAppRepository.HistoricalPaperFakFeeBackfillApplySql;
        var fullChain = SliceSql(
            sql,
            "full_chain_structural AS MATERIALIZED (",
            "run_only_legacy_structural AS MATERIALIZED (");
        var runOnlyLegacy = SliceSql(
            sql,
            "run_only_legacy_structural AS MATERIALIZED (",
            "structural_chain AS MATERIALIZED (");
        var positionUpdates = SliceSql(
            sql,
            "position_updates AS (",
            "settlement_updates AS (");
        var settlementUpdates = sql[sql.IndexOf(
            "settlement_updates AS (",
            StringComparison.Ordinal)..];

        Assert.DoesNotContain("paper_order.notional_usd = run.stake_usd", sql, StringComparison.Ordinal);
        Assert.Contains("run.stake_usd = round(fill.price * fill.size_shares, 8)", sql, StringComparison.Ordinal);
        Assert.Contains("settlement.cost_basis_usd = round(", fullChain, StringComparison.Ordinal);
        Assert.Contains(
            "run_chain.expected_fill_price * run_chain.expected_fill_size",
            fullChain,
            StringComparison.Ordinal);
        Assert.Contains("FROM requested sibling_request", sql, StringComparison.Ordinal);
        Assert.Contains("FROM public.paper_orders sibling_order", sql, StringComparison.Ordinal);
        Assert.Contains("actual_fill_fee = 0", sql, StringComparison.Ordinal);
        Assert.Contains("FROM fill_updates", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN fill_updates", sql, StringComparison.Ordinal);
        Assert.Contains("'FullChain'::text AS chain_shape", fullChain, StringComparison.Ordinal);
        Assert.Matches(
            @"settlement\.settlement_source = 'BtcUpDown5mGammaClosedMarket'\s+AND settlement\.settled_at_utc = run_chain\.run_settled_at",
            fullChain);
        Assert.Matches(
            @"settlement\.settlement_source = 'MarketWebSocket'\s+AND settlement\.settled_at_utc <= run_chain\.run_settled_at",
            fullChain);
        Assert.Contains("'RunOnlyLegacy'::text AS chain_shape", runOnlyLegacy, StringComparison.Ordinal);
        Assert.Contains("NULL::uuid AS position_id", runOnlyLegacy, StringComparison.Ordinal);
        Assert.Contains("NULL::uuid AS settlement_id", runOnlyLegacy, StringComparison.Ordinal);
        Assert.Contains("run_chain.run_settlement_price IN (0, 1)", runOnlyLegacy, StringComparison.Ordinal);
        Assert.Matches(
            @"NOT EXISTS\s*\(\s*SELECT 1\s+FROM public\.paper_positions",
            runOnlyLegacy);
        Assert.Matches(
            @"NOT EXISTS\s*\(\s*SELECT 1\s+FROM public\.paper_position_settlements",
            runOnlyLegacy);
        Assert.Contains("WHERE eligible.chain_shape = 'FullChain'", positionUpdates, StringComparison.Ordinal);
        Assert.Contains("WHERE eligible.chain_shape = 'FullChain'", settlementUpdates, StringComparison.Ordinal);
        Assert.Contains("AS structural_conflicts", sql, StringComparison.Ordinal);
        Assert.Contains("AS accounting_conflicts", sql, StringComparison.Ordinal);
        Assert.Contains("AS deferred_by_lock_timeout", sql, StringComparison.Ordinal);
        Assert.Contains("AS deferred_by_query_cancel", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO public.paper_positions", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO public.paper_position_settlements", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"(?i)\bINSERT\s+INTO\b", sql);
        Assert.DoesNotMatch(@"(?m)^\s*realized_pnl_usd\s*=", sql);
        Assert.DoesNotContain("gross_realized_pnl_usd", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("updated_at_utc =", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BatchResult_SeparatesItemConflictsFromWholeBatchDeferrals()
    {
        var completed = new HistoricalPaperFakFeeBackfillBatchResult(
            Requested: 5,
            StructuralConflicts: 2,
            AccountingConflicts: 3);
        var deferred = new HistoricalPaperFakFeeBackfillBatchResult(
            Requested: 5,
            DeferredByLockTimeout: 2,
            DeferredByQueryCancel: 3);

        Assert.Equal(5, completed.ItemConflicts);
        Assert.Equal(0, completed.Deferred);
        Assert.False(completed.WholeBatchDeferred);
        Assert.Equal(0, deferred.ItemConflicts);
        Assert.Equal(5, deferred.Deferred);
        Assert.True(deferred.WholeBatchDeferred);
    }

    [Fact]
    public void ApplyRepository_MapsLockAndQueryDeferralsToSeparateDiagnostics()
    {
        var source = ReadRepositorySource();

        Assert.Contains("DeferredByLockTimeout: updates.Count", source, StringComparison.Ordinal);
        Assert.Contains("DeferredByQueryCancel: updates.Count", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConflictsOrDeferred", source, StringComparison.Ordinal);
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
        Assert.Contains("paper_order.strategy_id = @StrategyId", candidateMethod, StringComparison.Ordinal);
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
    public void CandidateQuery_MaterializesStrategyScopeBeforeChronologicalPage()
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
        var strategyOrders = SliceSql(
            candidateMethod,
            "WITH strategy_orders AS MATERIALIZED (",
            "candidate_keys AS MATERIALIZED (");
        var candidateKeys = SliceSql(
            candidateMethod,
            "candidate_keys AS MATERIALIZED (",
            "FROM candidate_keys candidate");

        Assert.Contains("FROM public.paper_orders paper_order", strategyOrders, StringComparison.Ordinal);
        Assert.Contains("paper_order.strategy_id = @StrategyId", strategyOrders, StringComparison.Ordinal);
        Assert.Contains("paper_order.side", strategyOrders, StringComparison.Ordinal);
        Assert.Contains("paper_order.execution_source IN", strategyOrders, StringComparison.Ordinal);
        Assert.Contains("FROM strategy_orders strategy_order", candidateKeys, StringComparison.Ordinal);
        Assert.Contains(
            "INNER JOIN public.paper_fills fill ON fill.paper_order_id = strategy_order.id",
            candidateKeys,
            StringComparison.Ordinal);
        Assert.Contains("fill.fee_accounting_status", candidateKeys, StringComparison.Ordinal);
        Assert.Contains("fill.filled_at_utc < @FilledBeforeUtc", candidateKeys, StringComparison.Ordinal);
        Assert.Contains("NOT @HasCursor", candidateKeys, StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY fill.filled_at_utc, fill.paper_order_id, fill.id",
            candidateKeys,
            StringComparison.Ordinal);
        Assert.Contains("LIMIT @FetchLimit", candidateKeys, StringComparison.Ordinal);
        Assert.Contains(
            "INNER JOIN public.paper_fills fill ON fill.id = candidate.fill_id",
            candidateMethod,
            StringComparison.Ordinal);
        Assert.Contains(
            "INNER JOIN public.paper_orders paper_order ON paper_order.id = candidate.paper_order_id",
            candidateMethod,
            StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY candidate.filled_at_utc, candidate.paper_order_id, candidate.fill_id",
            candidateMethod,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StrategyRankQuery_UsesDashboardGrossWithRawFallbackAndAvoidsGlobalFillScan()
    {
        var source = ReadRepositorySource();
        var start = source.IndexOf(
            "GetHistoricalPaperFakFeeBackfillStrategyRanksAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "GetHistoricalPaperFakFeeBackfillCandidatesAsync",
            start,
            StringComparison.Ordinal);
        var rankMethod = source[start..end];

        Assert.Contains("FROM public.strategies strategy", rankMethod, StringComparison.Ordinal);
        Assert.Contains(
            "LEFT JOIN public.dashboard_strategy_performance_snapshots performance",
            rankMethod,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHEN performance.strategy_id IS NOT NULL",
            rankMethod,
            StringComparison.Ordinal);
        Assert.Contains("THEN performance.realized_pnl_usd", rankMethod, StringComparison.Ordinal);
        Assert.Contains("CROSS JOIN LATERAL", rankMethod, StringComparison.Ordinal);
        Assert.Contains("FROM public.paper_orders source_order", rankMethod, StringComparison.Ordinal);
        Assert.Contains("source_order.side", rankMethod, StringComparison.Ordinal);
        Assert.Contains("source_order.execution_source IN", rankMethod, StringComparison.Ordinal);
        Assert.Contains("HistoricalPaperFakDirectSource", rankMethod, StringComparison.Ordinal);
        Assert.Contains("HistoricalPaperFakChildSource", rankMethod, StringComparison.Ordinal);
        Assert.Contains("LIMIT 1", rankMethod, StringComparison.Ordinal);
        Assert.Contains("public.strategy_market_paper_runs", rankMethod, StringComparison.Ordinal);
        Assert.Contains("public.strategy_paper_skip_rollups", rankMethod, StringComparison.Ordinal);
        Assert.Contains("SUM(COALESCE(run.realized_pnl_usd, 0))", rankMethod, StringComparison.Ordinal);
        Assert.Contains("SUM(fill_all.realized_pnl_usd)", rankMethod, StringComparison.Ordinal);
        Assert.Contains("SUM(settlement.realized_pnl_usd)", rankMethod, StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY gross_realized_pnl_usd DESC, strategy.id",
            rankMethod,
            StringComparison.Ordinal);
        Assert.DoesNotContain("fill.fee_accounting_status", rankMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("fill.filled_at_utc < @FilledBeforeUtc", rankMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy_orders AS MATERIALIZED", rankMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("performance.total_pnl_usd", rankMethod, StringComparison.Ordinal);
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

    private static string SliceSql(string sql, string startMarker, string endMarker)
    {
        var start = sql.IndexOf(startMarker, StringComparison.Ordinal);
        var end = sql.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            throw new InvalidOperationException(
                $"Historical Paper FAK fee backfill SQL markers were not found: {startMarker}, {endMarker}.");
        }

        return sql[start..end];
    }
}
