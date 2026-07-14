using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Analytics;

public sealed class DashboardStrategyPerformanceSnapshotWorker(
    ILogger<DashboardStrategyPerformanceSnapshotWorker> logger,
    DashboardOptions options,
    IDashboardProjectionRepository projection,
    IAppRepository repository) : BackgroundService
{
    private const int ExpiryBatchSize = 5_000;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ExpiryCadence = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextExpiryAtUtc = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var control = await projection.GetControlStateAsync(stoppingToken);
                if (!control.Initialized ||
                    control.CalculationVersion != DashboardProjectionVersions.Current)
                {
                    var bootstrap = await projection.BootstrapAsync(stoppingToken);
                    logger.LogInformation(
                        "Dashboard projection bootstrapped. Strategies={Strategies} RecentFacts={RecentFacts} RecentRows={RecentRows} DiscardedEvents={DiscardedEvents} DurationMs={DurationMs}",
                        bootstrap.Strategies,
                        bootstrap.RecentFacts,
                        bootstrap.RecentRows,
                        bootstrap.BootstrappedEventsDiscarded,
                        bootstrap.Duration.TotalMilliseconds);
                    nextExpiryAtUtc = DateTimeOffset.UtcNow + ExpiryCadence;
                    continue;
                }

                var eventBatch = await projection.ApplyPendingEventsAsync(
                    options.ProjectionEventBatchSize,
                    stoppingToken);
                if (eventBatch.EventsApplied > 0 || eventBatch.ReconciliationsQueued > 0)
                {
                    logger.LogInformation(
                        "Dashboard projection events applied. Read={EventsRead} Applied={EventsApplied} Strategies={StrategiesUpdated} ReconciliationsQueued={ReconciliationsQueued}",
                        eventBatch.EventsRead,
                        eventBatch.EventsApplied,
                        eventBatch.StrategiesUpdated,
                        eventBatch.ReconciliationsQueued);
                }

                if (eventBatch.ReconciliationsQueued > 0)
                {
                    await ReconcileOnceAsync(stoppingToken);
                    continue;
                }

                var nowUtc = DateTimeOffset.UtcNow;
                if (nowUtc >= nextExpiryAtUtc)
                {
                    var expiry = await projection.ExpireRecentFactsAsync(ExpiryBatchSize, stoppingToken);
                    if (expiry.FactsExpired > 0)
                    {
                        logger.LogInformation(
                            "Dashboard recent projection facts expired. Facts={FactsExpired} Strategies={StrategiesUpdated}",
                            expiry.FactsExpired,
                            expiry.StrategiesUpdated);
                    }

                    nextExpiryAtUtc = DateTimeOffset.UtcNow + ExpiryCadence;
                }

                if (eventBatch.EventsRead == 0)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dashboard incremental projection cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
                await projection.RecordFailureAsync(
                    "ProjectionCycle",
                    ex.Message,
                    stoppingToken);
                await Task.Delay(FailureDelay, stoppingToken);
            }
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        var result = await projection.ReconcileNextStrategyAsync(cancellationToken);
        if (result.Error is not null)
        {
            logger.LogWarning(
                "Dashboard strategy projection reconciliation deferred. StrategyId={StrategyId} Code={Code} DurationMs={DurationMs} Error={Error}",
                result.StrategyId,
                result.StrategyCode,
                result.Duration.TotalMilliseconds,
                result.Error);
            return;
        }

        if (!result.Reconciled)
        {
            return;
        }

        if (result.ValuesChanged)
        {
            logger.LogWarning(
                "Dashboard strategy projection drift repaired. StrategyId={StrategyId} Code={Code} DurationMs={DurationMs}",
                result.StrategyId,
                result.StrategyCode,
                result.Duration.TotalMilliseconds);
        }
        else
        {
            logger.LogInformation(
                "Dashboard strategy projection reconciled. StrategyId={StrategyId} Code={Code} DurationMs={DurationMs}",
                result.StrategyId,
                result.StrategyCode,
                result.Duration.TotalMilliseconds);
        }
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new PolyCopyTrader.Domain.ApiError(
                    Guid.NewGuid(),
                    nameof(DashboardStrategyPerformanceSnapshotWorker),
                    "IncrementalProjection",
                    message,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Dashboard projection error.");
        }
    }
}
