using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Polymarket;

public sealed class PolymarketHttpLogRetentionWorker(
    ILogger<PolymarketHttpLogRetentionWorker> logger,
    PolymarketHttpLoggingOptions options,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CleanupEnabled)
        {
            logger.LogInformation("Polymarket HTTP log retention cleanup is disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(options.CleanupIntervalMinutes);
        logger.LogInformation(
            "Polymarket HTTP log retention worker started. SuccessfulRetentionHours={SuccessfulRetentionHours} FailedRetentionDays={FailedRetentionDays} CleanupIntervalMinutes={CleanupIntervalMinutes} CleanupBatchSize={CleanupBatchSize} MaxBatchesPerCycle={MaxBatchesPerCycle}",
            options.SuccessfulRetentionHours,
            options.FailedRetentionDays,
            options.CleanupIntervalMinutes,
            options.CleanupBatchSize,
            options.CleanupMaxBatchesPerCycle);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Polymarket HTTP log retention cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("Polymarket HTTP log retention worker stopped.");
    }

    private async Task RunCleanupCycleAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var successfulBeforeUtc = now.AddHours(-options.SuccessfulRetentionHours);
        var failedBeforeUtc = now.AddDays(-options.FailedRetentionDays);

        var totalDeleted = 0;
        var totalSuccessfulDeleted = 0;
        var totalFailedDeleted = 0;

        for (var batch = 0; batch < options.CleanupMaxBatchesPerCycle; batch++)
        {
            var result = await repository.CleanupPolymarketHttpLogsAsync(
                successfulBeforeUtc,
                failedBeforeUtc,
                options.CleanupBatchSize,
                cancellationToken);

            if (result.DeletedRows == 0)
            {
                break;
            }

            totalDeleted += result.DeletedRows;
            totalSuccessfulDeleted += result.DeletedSuccessfulRows;
            totalFailedDeleted += result.DeletedFailedRows;

            if (result.DeletedRows < options.CleanupBatchSize)
            {
                break;
            }
        }

        if (totalDeleted > 0)
        {
            logger.LogInformation(
                "Polymarket HTTP log retention deleted rows. Deleted={Deleted} Successful={Successful} Failed={Failed}",
                totalDeleted,
                totalSuccessfulDeleted,
                totalFailedDeleted);
        }
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), nameof(PolymarketHttpLogRetentionWorker), "Cleanup", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Polymarket HTTP log retention error.");
        }
    }
}
