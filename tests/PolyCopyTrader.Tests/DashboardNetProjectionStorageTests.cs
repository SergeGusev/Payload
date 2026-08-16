using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class DashboardNetProjectionStorageTests
{
    [Fact]
    public void LifetimeProjection_ComputesFeeInclusiveClosedOpenAndLiveNetRoi()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var strategyId = Guid.NewGuid();
        var state = new DashboardLifetimeProjectionState();
        Apply(state, new StrategyRunProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            StrategyMarketPaperRunStatuses.Settled,
            100m,
            Guid.NewGuid(),
            nowUtc.AddMinutes(-6),
            nowUtc.AddMinutes(-5),
            10m,
            nowUtc,
            null,
            nowUtc,
            null,
            2m,
            FeeAccountingStatus.Calculated.ToString(),
            8m));
        Apply(state, new PaperPositionProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            20m,
            4m,
            0.50m,
            1m,
            FeeAccountingStatus.VenueReported.ToString(),
            3m));
        Apply(state, new LiveOrderProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            LiveOrderStatus.Matched.ToString(),
            0.50m,
            100m,
            0m,
            50m,
            51m,
            1m,
            56m,
            5m,
            nowUtc,
            true,
            nowUtc.AddMinutes(-6),
            nowUtc,
            FeeAccountingStatus.Calculated.ToString(),
            4m));

        var result = DashboardProjectionCalculator.ToStrategyPerformance(
            CreateDescriptor(strategyId),
            state,
            nowUtc);

        Assert.Equal(10m, result.RealizedPnlUsd);
        Assert.Equal(4m, result.UnrealizedPnlUsd);
        Assert.Equal(8m, result.NetRealizedPnlUsd);
        Assert.Equal(3m, result.NetUnrealizedPnlUsd);
        Assert.Equal(11m, result.NetTotalPnlUsd);
        Assert.Equal(3m, result.AccountedFeeUsd);
        Assert.Equal(1, result.FeeAccountedSettledCount);
        Assert.Equal(1, result.FeeRequiredSettledCount);
        Assert.Equal(1, result.FeeAccountedOpenPositionCount);
        Assert.Equal(1, result.FeeRequiredOpenPositionCount);
        Assert.Equal(8m * 100m / 102m, result.NetClosedRoiPct);
        Assert.Equal(11m * 100m / 113m, result.NetRoiPct);
        Assert.Equal(4m, result.LiveNetRealizedPnlUsd);
        Assert.Equal(4m * 100m / 51m, result.LiveNetRoiPct);
        Assert.Equal(1m, result.LiveAccountedFeeUsd);
        Assert.Equal(1, result.LiveFeeAccountedSettledCount);
        Assert.Equal(1, result.LiveFeeRequiredSettledCount);
    }

    [Fact]
    public void LifetimeProjection_FallbackSellUsesGrossMinusNetAsTotalAllocatedFee()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var strategyId = Guid.NewGuid();
        var state = new DashboardLifetimeProjectionState();
        Apply(state, new PaperSettlementProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            50m,
            5m,
            true,
            1m,
            FeeAccountingStatus.Calculated.ToString(),
            4m));
        Apply(state, new PaperFillProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            "SELL",
            0.50m,
            20m,
            3m,
            nowUtc,
            0.50m,
            FeeAccountingStatus.Calculated.ToString(),
            1.50m));

        var result = DashboardProjectionCalculator.ToStrategyPerformance(
            CreateDescriptor(strategyId),
            state,
            nowUtc);

        Assert.Equal(8m, result.RealizedPnlUsd);
        Assert.Equal(5.50m, result.NetRealizedPnlUsd);
        Assert.Equal(2.50m, result.AccountedFeeUsd);
        Assert.Equal(2, result.FeeAccountedSettledCount);
        Assert.Equal(2, result.FeeRequiredSettledCount);
        Assert.Equal(5.50m * 100m / 59.50m, result.NetClosedRoiPct);
    }

    [Theory]
    [InlineData(2, 7)]
    [InlineData(-1, 11)]
    public void LifetimeProjection_InconsistentOrNegativeRunFeeRemainsIncomplete(
        decimal feeUsd,
        decimal netPnlUsd)
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var strategyId = Guid.NewGuid();
        var state = new DashboardLifetimeProjectionState();
        Apply(state, new StrategyRunProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            StrategyMarketPaperRunStatuses.Settled,
            100m,
            Guid.NewGuid(),
            nowUtc.AddMinutes(-6),
            nowUtc.AddMinutes(-5),
            10m,
            nowUtc,
            null,
            nowUtc,
            null,
            feeUsd,
            FeeAccountingStatus.Calculated.ToString(),
            netPnlUsd));

        var result = DashboardProjectionCalculator.ToStrategyPerformance(
            CreateDescriptor(strategyId),
            state,
            nowUtc);

        Assert.Null(result.NetRealizedPnlUsd);
        Assert.Null(result.NetClosedRoiPct);
        Assert.Equal(0m, result.AccountedFeeUsd);
        Assert.Equal(0, result.FeeAccountedSettledCount);
        Assert.Equal(1, result.FeeRequiredSettledCount);
    }

    [Theory]
    [InlineData(-0.50, 1.50)]
    [InlineData(2.00, 1.50)]
    public void LifetimeProjection_InvalidSellExitFeeRemainsIncomplete(
        decimal exitFeeUsd,
        decimal netPnlUsd)
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var strategyId = Guid.NewGuid();
        var state = new DashboardLifetimeProjectionState();
        Apply(state, new PaperFillProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            "SELL",
            0.50m,
            20m,
            3m,
            nowUtc,
            exitFeeUsd,
            FeeAccountingStatus.Calculated.ToString(),
            netPnlUsd));

        var result = DashboardProjectionCalculator.ToStrategyPerformance(
            CreateDescriptor(strategyId),
            state,
            nowUtc);

        Assert.Null(result.NetRealizedPnlUsd);
        Assert.Equal(0, result.FeeAccountedSettledCount);
        Assert.Equal(1, result.FeeRequiredSettledCount);
    }

    [Fact]
    public void LifetimeProjection_LegacyOpenPositionRemainsRequiredButIncomplete()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var strategyId = Guid.NewGuid();
        var state = new DashboardLifetimeProjectionState();
        Apply(state, new PaperPositionProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            20m,
            4m,
            0.50m,
            1m,
            FeeAccountingStatus.LegacyUnknown.ToString(),
            null));

        var result = DashboardProjectionCalculator.ToStrategyPerformance(
            CreateDescriptor(strategyId),
            state,
            nowUtc);

        Assert.Null(result.NetUnrealizedPnlUsd);
        Assert.Null(result.NetTotalPnlUsd);
        Assert.Null(result.NetRoiPct);
        Assert.Equal(0m, result.AccountedFeeUsd);
        Assert.Equal(0, result.FeeAccountedOpenPositionCount);
        Assert.Equal(1, result.FeeRequiredOpenPositionCount);
    }

    [Fact]
    public void RecentProjection_UsesFeeInclusiveSettledAndLiveDenominators()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var strategyId = Guid.NewGuid();
        var state = new DashboardRecentProjectionState();
        var run = new StrategyRunProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            StrategyMarketPaperRunStatuses.Settled,
            100m,
            Guid.NewGuid(),
            nowUtc.AddMinutes(-6),
            nowUtc.AddMinutes(-5),
            10m,
            nowUtc,
            null,
            nowUtc,
            null,
            2m,
            FeeAccountingStatus.Calculated.ToString(),
            8m);
        foreach (var fact in DashboardProjectionCalculator.GetRecentFacts(run))
        {
            DashboardProjectionCalculator.Apply(state, fact.Contribution, 1);
        }

        var live = new LiveOrderProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            LiveOrderStatus.Matched.ToString(),
            0.50m,
            100m,
            0m,
            50m,
            51m,
            1m,
            56m,
            5m,
            nowUtc,
            true,
            nowUtc.AddMinutes(-6),
            nowUtc,
            FeeAccountingStatus.Calculated.ToString(),
            4m);
        foreach (var fact in DashboardProjectionCalculator.GetRecentFacts(live))
        {
            DashboardProjectionCalculator.Apply(state, fact.Contribution, 1);
        }

        var result = DashboardProjectionCalculator.ToStrategyRecentPerformance(
            CreateDescriptor(strategyId),
            state,
            1,
            nowUtc);

        Assert.Equal(8m, result.NetRealizedPnlUsd);
        Assert.Equal(8m * 100m / 102m, result.NetRoiPct);
        Assert.Equal(2m, result.AccountedFeeUsd);
        Assert.Equal(1, result.FeeAccountedSettledCount);
        Assert.Equal(1, result.FeeRequiredSettledCount);
        Assert.Equal(4m, result.LiveNetRealizedPnlUsd);
        Assert.Equal(4m * 100m / 51m, result.LiveNetRoiPct);
    }

    [Fact]
    public void EmptyProjection_ReturnsExactZeroNetMetrics()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var strategy = CreateDescriptor(Guid.NewGuid());

        var lifetime = DashboardProjectionCalculator.ToStrategyPerformance(
            strategy,
            new DashboardLifetimeProjectionState(),
            nowUtc);
        var recent = DashboardProjectionCalculator.ToStrategyRecentPerformance(
            strategy,
            new DashboardRecentProjectionState(),
            1,
            nowUtc);

        Assert.Equal(0m, lifetime.NetRealizedPnlUsd);
        Assert.Equal(0m, lifetime.NetUnrealizedPnlUsd);
        Assert.Equal(0m, lifetime.NetTotalPnlUsd);
        Assert.Equal(0m, lifetime.NetRoiPct);
        Assert.Equal(0m, lifetime.NetClosedRoiPct);
        Assert.Equal(0m, lifetime.LiveNetRealizedPnlUsd);
        Assert.Equal(0m, lifetime.LiveNetRoiPct);
        Assert.Equal(0m, recent.NetRealizedPnlUsd);
        Assert.Equal(0m, recent.NetRoiPct);
        Assert.Equal(0m, recent.LiveNetRealizedPnlUsd);
        Assert.Equal(0m, recent.LiveNetRoiPct);
    }

    [Fact]
    public void LifetimeProjection_SkipRollupKeepsRunBranchAuthoritative()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var strategyId = Guid.NewGuid();
        var state = new DashboardLifetimeProjectionState();
        DashboardProjectionCalculator.Apply(
            state,
            DashboardProjectionCalculator.GetLifetimeContribution(
                new StrategyPaperSkipRollupProjectionPayload(
                    strategyId,
                    1,
                    nowUtc)),
            1);
        Apply(state, new PaperSettlementProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            50m,
            5m,
            true,
            1m,
            FeeAccountingStatus.LegacyUnknown.ToString(),
            null));
        Apply(state, new PaperFillProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            "SELL",
            0.50m,
            20m,
            3m,
            nowUtc,
            0.50m,
            FeeAccountingStatus.LegacyUnknown.ToString(),
            null));

        var result = DashboardProjectionCalculator.ToStrategyPerformance(
            CreateDescriptor(strategyId),
            state,
            nowUtc);

        Assert.Equal(0m, result.RealizedPnlUsd);
        Assert.Equal(0, result.FeeRequiredSettledCount);
        Assert.Equal(0m, result.NetRealizedPnlUsd);
    }

    [Fact]
    public void LifetimeProjection_ZeroSizePositionIsExcludedFromGrossAndNetCoverage()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var strategyId = Guid.NewGuid();
        var state = new DashboardLifetimeProjectionState();
        Apply(state, new PaperPositionProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            0m,
            99m,
            0.50m,
            0m,
            FeeAccountingStatus.LegacyUnknown.ToString(),
            null));

        var result = DashboardProjectionCalculator.ToStrategyPerformance(
            CreateDescriptor(strategyId),
            state,
            nowUtc);

        Assert.Equal(0m, result.UnrealizedPnlUsd);
        Assert.Equal(0, result.FeeRequiredOpenPositionCount);
        Assert.Equal(0m, result.NetUnrealizedPnlUsd);
    }

    [Fact]
    public void LifetimeProjection_UnsettledLiveOrderIsExcludedFromGrossAndNetCoverage()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var strategyId = Guid.NewGuid();
        var state = new DashboardLifetimeProjectionState();
        Apply(state, new LiveOrderProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            LiveOrderStatus.Matched.ToString(),
            0.50m,
            100m,
            100m,
            0m,
            0m,
            0m,
            null,
            null,
            null,
            null,
            nowUtc,
            nowUtc,
            FeeAccountingStatus.LegacyUnknown.ToString(),
            null));

        var result = DashboardProjectionCalculator.ToStrategyPerformance(
            CreateDescriptor(strategyId),
            state,
            nowUtc);

        Assert.Equal(0m, result.LiveRealizedPnlUsd);
        Assert.Equal(0, result.LiveFeeRequiredSettledCount);
        Assert.Equal(0m, result.LiveNetRealizedPnlUsd);
    }

    [Fact]
    public void LifetimeProjection_TerminalNetChangesNoGrossMetric()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var strategyId = Guid.NewGuid();
        var legacy = new DashboardLifetimeProjectionState();
        var calculated = new DashboardLifetimeProjectionState();
        var legacyRun = new StrategyRunProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            StrategyMarketPaperRunStatuses.Settled,
            100m,
            Guid.NewGuid(),
            nowUtc.AddMinutes(-6),
            nowUtc.AddMinutes(-5),
            10m,
            nowUtc,
            null,
            nowUtc,
            null,
            0m,
            FeeAccountingStatus.LegacyUnknown.ToString(),
            null);
        Apply(legacy, legacyRun);
        Apply(calculated, legacyRun with
        {
            FeeUsd = 2m,
            FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
            NetRealizedPnlUsd = 8m
        });

        var legacyResult = DashboardProjectionCalculator.ToStrategyPerformance(
            CreateDescriptor(strategyId),
            legacy,
            nowUtc);
        var calculatedResult = DashboardProjectionCalculator.ToStrategyPerformance(
            CreateDescriptor(strategyId),
            calculated,
            nowUtc);

        Assert.Equal(legacyResult.RealizedPnlUsd, calculatedResult.RealizedPnlUsd);
        Assert.Equal(legacyResult.TotalPnlUsd, calculatedResult.TotalPnlUsd);
        Assert.Equal(legacyResult.RoiPct, calculatedResult.RoiPct);
        Assert.Equal(legacyResult.ClosedRoiPct, calculatedResult.ClosedRoiPct);
    }

    [Fact]
    public void ProjectionRejectsFeeAccountedCountsAboveRequiredCounts()
    {
        Assert.Throws<InvalidOperationException>(() => DashboardProjectionCalculator.Apply(
            new DashboardLifetimeProjectionState(),
            new DashboardLifetimeContribution
            {
                RunFeeAccountedSettledCount = 1,
                RunFeeRequiredSettledCount = 0
            },
            1));
        Assert.Throws<InvalidOperationException>(() => DashboardProjectionCalculator.Apply(
            new DashboardRecentProjectionState(),
            new DashboardRecentContribution
            {
                FeeAccountedSettledCount = 1,
                FeeRequiredSettledCount = 0
            },
            1));
    }

    [Fact]
    public void StorageSchemaAndSnapshotRepository_PersistAllNetCoverageFields()
    {
        var schema = ReadSource("src", "PolyCopyTrader.Storage", "PostgresSchema.cs");
        var projectionSchema = ReadSource("src", "PolyCopyTrader.Storage", "DashboardProjectionSchema.cs");
        var snapshotRepository = ReadSource(
            "src", "PolyCopyTrader.Storage", "PostgresDashboardSnapshotRepository.cs");
        var bootstrap = ReadSource(
            "src", "PolyCopyTrader.Storage", "PostgresDashboardProjectionRepository.Bootstrap.cs");

        foreach (var column in new[]
        {
            "net_realized_pnl_usd",
            "net_unrealized_pnl_usd",
            "net_total_pnl_usd",
            "net_roi_pct",
            "net_closed_roi_pct",
            "accounted_fee_usd",
            "fee_accounted_settled_count",
            "fee_required_settled_count",
            "fee_accounted_open_position_count",
            "fee_required_open_position_count",
            "live_net_realized_pnl_usd",
            "live_net_roi_pct",
            "live_accounted_fee_usd",
            "live_fee_accounted_settled_count",
            "live_fee_required_settled_count"
        })
        {
            Assert.Contains(column, schema, StringComparison.Ordinal);
            Assert.Contains(column, snapshotRepository, StringComparison.Ordinal);
        }

        Assert.Contains("ADD COLUMN IF NOT EXISTS net_unrealized_pnl_usd", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("'fee_accounting_status', (row_value).fee_accounting_status", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("'paper_fill_fee_accounting_changed'", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("'paper_position_fee_accounting_changed'", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("'paper_settlement_fee_accounting_changed'", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("'strategy_run_fee_accounting_changed'", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("OLD.average_price IS NOT DISTINCT FROM NEW.average_price", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("SELECT paper_order.strategy_id", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("dashboard_projection_strategy_id_for_wallet", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (strategy_id) DO UPDATE SET", projectionSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM dashboard_projection_reconciliation_queue;", bootstrap, StringComparison.Ordinal);
        Assert.Equal(4, DashboardProjectionVersions.Current);
    }

    private static void Apply(DashboardLifetimeProjectionState state, StrategyRunProjectionPayload payload) =>
        DashboardProjectionCalculator.Apply(
            state,
            DashboardProjectionCalculator.GetLifetimeContribution(payload),
            1);

    private static void Apply(DashboardLifetimeProjectionState state, PaperPositionProjectionPayload payload) =>
        DashboardProjectionCalculator.Apply(
            state,
            DashboardProjectionCalculator.GetLifetimeContribution(payload),
            1);

    private static void Apply(DashboardLifetimeProjectionState state, PaperSettlementProjectionPayload payload) =>
        DashboardProjectionCalculator.Apply(
            state,
            DashboardProjectionCalculator.GetLifetimeContribution(payload),
            1);

    private static void Apply(DashboardLifetimeProjectionState state, PaperFillProjectionPayload payload) =>
        DashboardProjectionCalculator.Apply(
            state,
            DashboardProjectionCalculator.GetLifetimeContribution(payload),
            1);

    private static void Apply(DashboardLifetimeProjectionState state, LiveOrderProjectionPayload payload) =>
        DashboardProjectionCalculator.Apply(
            state,
            DashboardProjectionCalculator.GetLifetimeContribution(payload),
            1);

    private static DashboardStrategyDescriptor CreateDescriptor(Guid strategyId) => new(
        strategyId,
        "test_strategy",
        "Test strategy",
        true,
        true,
        false,
        null,
        1m,
        1m,
        1m,
        1m,
        0,
        0,
        100m,
        null);

    private static string ReadSource(params string[] segments)
    {
        var root = Environment.GetEnvironmentVariable("POLYCOPYTRADER_REPOSITORY_ROOT");
        var path = string.IsNullOrWhiteSpace(root)
            ? Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(segments))
            : Path.Combine(root, Path.Combine(segments));
        return File.ReadAllText(Path.GetFullPath(path));
    }
}
