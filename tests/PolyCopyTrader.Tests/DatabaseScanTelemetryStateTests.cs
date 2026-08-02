using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.Analytics;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class DatabaseScanTelemetryStateTests
{
    [Fact]
    public void State_DistinguishesUnmeasuredCountersAndRetainsMeasuredPostBuildFailure()
    {
        var state = new DatabaseScanTelemetryState();
        state.RecordCopiedPerformance(new PaperCopiedTraderPerformanceRefreshResult(
            LockAcquired: false,
            WalletsSeeded: 0,
            WalletsProcessed: 0,
            PerformanceRowsWritten: 0,
            QueueRemaining: 0,
            ReconciliationCycleCompleted: false,
            PaperPositionsSeedSequentialScans: 9,
            PaperPositionsSeedSequentialTuplesRead: 9));
        Assert.Contains(
            "CopiedPerformance=pending",
            state.GetHeartbeatSummary(),
            StringComparison.Ordinal);

        state.RecordCopiedPerformance(new PaperCopiedTraderPerformanceRefreshResult(
            LockAcquired: true,
            WalletsSeeded: 0,
            WalletsProcessed: 0,
            PerformanceRowsWritten: 0,
            QueueRemaining: 0,
            ReconciliationCycleCompleted: false));
        state.RecordDashboardReconciliation(new DashboardProjectionReconciliationResult(
            Reconciled: false,
            StrategyId: Guid.NewGuid(),
            StrategyCode: "failed_after_build",
            Duration: TimeSpan.FromMilliseconds(10),
            ValuesChanged: false,
            Error: "simulated failure",
            PaperPositionsBuildSequentialScans: 2,
            PaperPositionsBuildSequentialTuplesRead: 3_704_610));

        var summary = state.GetHeartbeatSummary();

        Assert.Contains(
            "Seed(last=unmeasured/unmeasured,total=0/0,lastPositive=none)",
            summary,
            StringComparison.Ordinal);
        Assert.Contains(
            "Aggregate(last=unmeasured/unmeasured,total=0/0,lastPositive=none)",
            summary,
            StringComparison.Ordinal);
        Assert.Contains(
            "[latest=failed_after_build]:Build(last=2/3704610,total=2/3704610,lastPositive=",
            summary,
            StringComparison.Ordinal);
        Assert.Contains("@2/3704610),lastPositiveStrategy=failed_after_build", summary, StringComparison.Ordinal);

        state.RecordDashboardReconciliation(new DashboardProjectionReconciliationResult(
            Reconciled: true,
            StrategyId: Guid.NewGuid(),
            StrategyCode: "zero_after_build",
            Duration: TimeSpan.FromMilliseconds(7),
            ValuesChanged: false,
            Error: null,
            PaperPositionsBuildSequentialScans: 0,
            PaperPositionsBuildSequentialTuplesRead: 0));

        var afterZero = state.GetHeartbeatSummary();
        Assert.Contains(
            "[latest=zero_after_build]:Build(last=0/0,total=2/3704610,lastPositive=",
            afterZero,
            StringComparison.Ordinal);
        Assert.Contains(
            "@2/3704610),lastPositiveStrategy=failed_after_build",
            afterZero,
            StringComparison.Ordinal);

        state.RecordDashboardReconciliation(new DashboardProjectionReconciliationResult(
            Reconciled: false,
            StrategyId: Guid.NewGuid(),
            StrategyCode: "failed_before_build",
            Duration: TimeSpan.FromMilliseconds(5),
            ValuesChanged: false,
            Error: "simulated pre-build failure"));

        Assert.Contains(
            "[latest=zero_after_build]:Build(last=0/0,total=2/3704610,lastPositive=",
            state.GetHeartbeatSummary(),
            StringComparison.Ordinal);
    }
}
