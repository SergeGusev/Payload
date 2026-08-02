using System.Globalization;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Analytics;

public sealed class DatabaseScanTelemetryState
{
    private readonly object sync = new();
    private readonly ScanPhaseState copiedSeed = new();
    private readonly ScanPhaseState copiedAggregation = new();
    private readonly ScanPhaseState dashboardBuild = new();
    private string? copiedRecordedAtUtc;
    private string? dashboardRecordedAtUtc;
    private string? dashboardStrategyCode;
    private string? dashboardLastPositiveStrategyCode;

    public void RecordCopiedPerformance(PaperCopiedTraderPerformanceRefreshResult result)
    {
        if (!result.LockAcquired)
        {
            return;
        }

        var recordedAtUtc = FormatTimestamp(DateTimeOffset.UtcNow);
        lock (sync)
        {
            copiedRecordedAtUtc = recordedAtUtc;
            copiedSeed.Record(
                result.PaperPositionsSeedSequentialScans,
                result.PaperPositionsSeedSequentialTuplesRead,
                recordedAtUtc);
            copiedAggregation.Record(
                result.PaperPositionsAggregationSequentialScans,
                result.PaperPositionsAggregationSequentialTuplesRead,
                recordedAtUtc);
        }
    }

    public void RecordDashboardReconciliation(DashboardProjectionReconciliationResult result)
    {
        if (!result.Reconciled &&
            (result.PaperPositionsBuildSequentialScans is null ||
             result.PaperPositionsBuildSequentialTuplesRead is null))
        {
            return;
        }

        var recordedAtUtc = FormatTimestamp(DateTimeOffset.UtcNow);
        var strategyCode = string.IsNullOrWhiteSpace(result.StrategyCode)
            ? "unknown"
            : result.StrategyCode.Trim();
        lock (sync)
        {
            dashboardRecordedAtUtc = recordedAtUtc;
            dashboardStrategyCode = strategyCode;
            if (dashboardBuild.Record(
                result.PaperPositionsBuildSequentialScans,
                result.PaperPositionsBuildSequentialTuplesRead,
                recordedAtUtc))
            {
                dashboardLastPositiveStrategyCode = strategyCode;
            }
        }
    }

    public string GetHeartbeatSummary()
    {
        lock (sync)
        {
            var copiedPerformance = copiedRecordedAtUtc is null
                ? "CopiedPerformance=pending"
                : FormattableString.Invariant(
                    $"CopiedPerformance@{copiedRecordedAtUtc}:Seed({copiedSeed.Format()}),Aggregate({copiedAggregation.Format()})");
            var dashboardReconciliation = dashboardRecordedAtUtc is null
                ? "DashboardReconciliation=pending"
                : FormattableString.Invariant(
                    $"DashboardReconciliation@{dashboardRecordedAtUtc}[latest={dashboardStrategyCode}]:Build({dashboardBuild.Format()}),lastPositiveStrategy={dashboardLastPositiveStrategyCode ?? "none"}");
            return "DBScanTelemetry " + copiedPerformance + "; " + dashboardReconciliation;
        }
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    private sealed class ScanPhaseState
    {
        private long? lastSequentialScans;
        private long? lastSequentialTuplesRead;
        private long totalSequentialScans;
        private long totalSequentialTuplesRead;
        private string? lastPositiveAtUtc;
        private long lastPositiveSequentialScans;
        private long lastPositiveSequentialTuplesRead;

        public bool Record(long? sequentialScans, long? sequentialTuplesRead, string recordedAtUtc)
        {
            lastSequentialScans = sequentialScans;
            lastSequentialTuplesRead = sequentialTuplesRead;
            if (sequentialScans is not long measuredScans ||
                sequentialTuplesRead is not long measuredTuplesRead)
            {
                return false;
            }

            totalSequentialScans += measuredScans;
            totalSequentialTuplesRead += measuredTuplesRead;
            if (measuredScans > 0 || measuredTuplesRead > 0)
            {
                lastPositiveAtUtc = recordedAtUtc;
                lastPositiveSequentialScans = measuredScans;
                lastPositiveSequentialTuplesRead = measuredTuplesRead;
                return true;
            }

            return false;
        }

        public string Format()
        {
            return FormattableString.Invariant(
                $"last={FormatValue(lastSequentialScans)}/{FormatValue(lastSequentialTuplesRead)},total={totalSequentialScans}/{totalSequentialTuplesRead},lastPositive={FormatLastPositive()}");
        }

        private string FormatLastPositive()
        {
            return lastPositiveAtUtc is null
                ? "none"
                : FormattableString.Invariant(
                    $"{lastPositiveAtUtc}@{lastPositiveSequentialScans}/{lastPositiveSequentialTuplesRead}");
        }

        private static string FormatValue(long? value)
        {
            return value?.ToString(CultureInfo.InvariantCulture) ?? "unmeasured";
        }
    }
}
