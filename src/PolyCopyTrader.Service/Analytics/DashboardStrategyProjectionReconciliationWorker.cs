using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Analytics;

public sealed class DashboardStrategyProjectionReconciliationWorker(
    ILogger<DashboardStrategyProjectionReconciliationWorker> logger,
    IDashboardProjectionRepository projection) : BackgroundService
{
    private static readonly TimeSpan Cadence = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Cadence, stoppingToken);
                var control = await projection.GetControlStateAsync(stoppingToken);
                if (!control.Initialized ||
                    control.CalculationVersion != DashboardProjectionVersions.Current)
                {
                    continue;
                }

                var result = await projection.ReconcileNextStrategyAsync(stoppingToken);
                if (result.Error is not null)
                {
                    logger.LogWarning(
                        "Dashboard strategy projection background reconciliation deferred. StrategyId={StrategyId} Code={Code} DurationMs={DurationMs} Error={Error}",
                        result.StrategyId,
                        result.StrategyCode,
                        result.Duration.TotalMilliseconds,
                        result.Error);
                    continue;
                }

                if (!result.Reconciled)
                {
                    continue;
                }

                if (result.ValuesChanged)
                {
                    logger.LogWarning(
                        "Dashboard strategy projection background reconciliation repaired drift. StrategyId={StrategyId} Code={Code} DurationMs={DurationMs}",
                        result.StrategyId,
                        result.StrategyCode,
                        result.Duration.TotalMilliseconds);
                }
                else
                {
                    logger.LogInformation(
                        "Dashboard strategy projection background reconciliation completed. StrategyId={StrategyId} Code={Code} DurationMs={DurationMs}",
                        result.StrategyId,
                        result.StrategyCode,
                        result.Duration.TotalMilliseconds);
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
