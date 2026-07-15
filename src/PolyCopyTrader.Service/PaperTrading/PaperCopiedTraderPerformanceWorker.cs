using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperCopiedTraderPerformanceWorker(
    ILogger<PaperCopiedTraderPerformanceWorker> logger,
    PaperTradingOptions options,
    IAppRepository repository) : BackgroundService
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
            "Paper copied-trader performance projection worker started. RefreshSeconds={RefreshSeconds} WalletBatchSize={WalletBatchSize} ReconciliationSeedWalletBatchSize={ReconciliationSeedWalletBatchSize}",
            options.CopiedTraderPerformanceRefreshSeconds,
            options.CopiedTraderPerformanceWalletBatchSize,
            options.CopiedTraderPerformanceReconciliationSeedWalletBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                    options.CopiedTraderPerformanceWalletBatchSize,
                    options.CopiedTraderPerformanceReconciliationSeedWalletBatchSize,
                    stoppingToken);

                if (!result.LockAcquired)
                {
                    logger.LogDebug("Paper copied-trader performance projection cycle skipped because another refresh owns the lock.");
                }
                else
                {
                    logger.LogInformation(
                        "Paper copied-trader performance projection cycle completed. WalletsSeeded={WalletsSeeded} WalletsProcessed={WalletsProcessed} PerformanceRowsWritten={PerformanceRowsWritten} QueueRemaining={QueueRemaining} ReconciliationCycleCompleted={ReconciliationCycleCompleted}",
                        result.WalletsSeeded,
                        result.WalletsProcessed,
                        result.PerformanceRowsWritten,
                        result.QueueRemaining,
                        result.ReconciliationCycleCompleted);
                }

                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Paper copied-trader performance projection cycle failed. Retrying in {DelaySeconds} seconds.",
                    interval.TotalSeconds);
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
                await Task.Delay(interval, stoppingToken);
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
