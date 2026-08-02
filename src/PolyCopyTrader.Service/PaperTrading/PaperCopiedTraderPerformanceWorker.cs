using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Analytics;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperCopiedTraderPerformanceWorker(
    ILogger<PaperCopiedTraderPerformanceWorker> logger,
    PaperTradingOptions options,
    IAppRepository repository,
    DatabaseScanTelemetryState? databaseScanTelemetryState = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CopiedTraderPerformanceProjectionEnabled)
        {
            logger.LogInformation("Paper copied-trader performance projection is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(options.CopiedTraderPerformanceRefreshSeconds);
        logger.LogInformation(
            "Paper copied-trader performance projection worker started. RefreshSeconds={RefreshSeconds} HighPriorityWalletBatchSize={HighPriorityWalletBatchSize} ReconciliationWalletBatchSize={ReconciliationWalletBatchSize} ReconciliationSeedWalletBatchSize={ReconciliationSeedWalletBatchSize} MaximumWalletsPerCycle={MaximumWalletsPerCycle}",
            options.CopiedTraderPerformanceRefreshSeconds,
            options.CopiedTraderPerformanceWalletBatchSize,
            options.CopiedTraderPerformanceReconciliationWalletBatchSize,
            options.CopiedTraderPerformanceReconciliationSeedWalletBatchSize,
            options.CopiedTraderPerformanceWalletBatchSize +
                options.CopiedTraderPerformanceReconciliationWalletBatchSize);

        using var cadenceTimer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                    options.CopiedTraderPerformanceWalletBatchSize,
                    options.CopiedTraderPerformanceReconciliationWalletBatchSize,
                    options.CopiedTraderPerformanceReconciliationSeedWalletBatchSize,
                    stoppingToken);

                if (!result.LockAcquired)
                {
                    logger.LogDebug("Paper copied-trader performance projection cycle skipped because another refresh owns the lock.");
                }
                else
                {
                    databaseScanTelemetryState?.RecordCopiedPerformance(result);
                    logger.LogInformation(
                        "Paper copied-trader performance projection cycle completed. WalletsSeeded={WalletsSeeded} HighPriorityWalletsProcessed={HighPriorityWalletsProcessed} ReconciliationWalletsProcessed={ReconciliationWalletsProcessed} WalletsProcessed={WalletsProcessed} PerformanceRowsWritten={PerformanceRowsWritten} HighPriorityQueueRemaining={HighPriorityQueueRemaining} ReconciliationQueueRemaining={ReconciliationQueueRemaining} QueueRemaining={QueueRemaining} ReconciliationCycleCompleted={ReconciliationCycleCompleted} PaperPositionsSeedSequentialScans={PaperPositionsSeedSequentialScans} PaperPositionsSeedSequentialTuplesRead={PaperPositionsSeedSequentialTuplesRead} PaperPositionsAggregationSequentialScans={PaperPositionsAggregationSequentialScans} PaperPositionsAggregationSequentialTuplesRead={PaperPositionsAggregationSequentialTuplesRead}",
                        result.WalletsSeeded,
                        result.HighPriorityWalletsProcessed,
                        result.ReconciliationWalletsProcessed,
                        result.WalletsProcessed,
                        result.PerformanceRowsWritten,
                        result.HighPriorityQueueRemaining,
                        result.ReconciliationQueueRemaining,
                        result.QueueRemaining,
                        result.ReconciliationCycleCompleted,
                        result.PaperPositionsSeedSequentialScans,
                        result.PaperPositionsSeedSequentialTuplesRead,
                        result.PaperPositionsAggregationSequentialScans,
                        result.PaperPositionsAggregationSequentialTuplesRead);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Paper copied-trader performance projection cycle failed. Retrying on the next fixed cadence tick in at most {DelaySeconds} seconds.",
                    interval.TotalSeconds);
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            try
            {
                if (!await cadenceTimer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Paper copied-trader performance projection worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(
                    Guid.NewGuid(),
                    "PaperCopiedTraderPerformanceWorker",
                    "ProjectionCycle",
                    message,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist paper copied-trader performance projection error.");
        }
    }
}
