using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Analytics;

public sealed class DashboardStrategyProjectionReconciliationWorker(
    ILogger<DashboardStrategyProjectionReconciliationWorker> logger,
    IDashboardProjectionRepository projection,
    DashboardOptions options,
    TimeProvider? timeProvider = null,
    DatabaseScanTelemetryState? databaseScanTelemetryState = null) : BackgroundService
{
    private readonly TimeSpan cadence = TimeSpan.FromSeconds(
        options.ProjectionReconciliationIntervalSeconds);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Dashboard strategy projection reconciliation worker started. IntervalSeconds={IntervalSeconds} BatchSize=1",
            options.ProjectionReconciliationIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(cadence, clock, stoppingToken);
                var control = await projection.GetControlStateAsync(stoppingToken);
                if (!control.Initialized ||
                    control.CalculationVersion != DashboardProjectionVersions.Current)
                {
                    continue;
                }

                var result = await projection.ReconcileNextStrategyAsync(stoppingToken);
                databaseScanTelemetryState?.RecordDashboardReconciliation(result);
                if (result.Error is not null)
                {
                    logger.LogWarning(
                        "Dashboard strategy projection background reconciliation deferred. StrategyId={StrategyId} Code={Code} DurationMs={DurationMs} Error={Error} PaperPositionsBuildSequentialScans={PaperPositionsBuildSequentialScans} PaperPositionsBuildSequentialTuplesRead={PaperPositionsBuildSequentialTuplesRead}",
                        result.StrategyId,
                        result.StrategyCode,
                        result.Duration.TotalMilliseconds,
                        result.Error,
                        result.PaperPositionsBuildSequentialScans,
                        result.PaperPositionsBuildSequentialTuplesRead);
                    continue;
                }

                if (!result.Reconciled)
                {
                    continue;
                }

                if (result.ValuesChanged)
                {
                    logger.LogWarning(
                        "Dashboard strategy projection background reconciliation repaired drift. StrategyId={StrategyId} Code={Code} DurationMs={DurationMs} PaperPositionsBuildSequentialScans={PaperPositionsBuildSequentialScans} PaperPositionsBuildSequentialTuplesRead={PaperPositionsBuildSequentialTuplesRead}",
                        result.StrategyId,
                        result.StrategyCode,
                        result.Duration.TotalMilliseconds,
                        result.PaperPositionsBuildSequentialScans,
                        result.PaperPositionsBuildSequentialTuplesRead);
                }
                else
                {
                    logger.LogInformation(
                        "Dashboard strategy projection background reconciliation completed. StrategyId={StrategyId} Code={Code} DurationMs={DurationMs} PaperPositionsBuildSequentialScans={PaperPositionsBuildSequentialScans} PaperPositionsBuildSequentialTuplesRead={PaperPositionsBuildSequentialTuplesRead}",
                        result.StrategyId,
                        result.StrategyCode,
                        result.Duration.TotalMilliseconds,
                        result.PaperPositionsBuildSequentialScans,
                        result.PaperPositionsBuildSequentialTuplesRead);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dashboard strategy projection background reconciliation cycle failed.");
            }
        }
    }
}
