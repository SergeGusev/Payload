using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperFakFeeBackfillEventRetentionWorker(
    ILogger<PaperFakFeeBackfillEventRetentionWorker> logger,
    IAppRepository repository) : BackgroundService
{
    internal const int RetentionHours = 24;
    internal const int CleanupIntervalMinutes = 10;
    internal const int CleanupBatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Historical Paper FAK fee backfill database-event retention worker started. " +
            "RetentionHours={RetentionHours} CleanupIntervalMinutes={CleanupIntervalMinutes} " +
            "CleanupBatchSize={CleanupBatchSize}",
            RetentionHours,
            CleanupIntervalMinutes,
            CleanupBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupCycleAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Historical Paper FAK fee backfill database-event retention cycle failed. " +
                    "File logging remains active.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(CleanupIntervalMinutes),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation(
            "Historical Paper FAK fee backfill database-event retention worker stopped.");
    }

    internal async Task<int> RunCleanupCycleAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var occurredBeforeUtc = nowUtc.ToUniversalTime().AddHours(-RetentionHours);
        var deleted = await repository.CleanupPaperFakFeeBackfillEventsAsync(
            occurredBeforeUtc,
            CleanupBatchSize,
            cancellationToken).ConfigureAwait(false);

        if (deleted > 0)
        {
            logger.LogInformation(
                "Historical Paper FAK fee backfill database-event retention deleted rows. " +
                "Deleted={Deleted} OccurredBeforeUtc={OccurredBeforeUtc:O}",
                deleted,
                occurredBeforeUtc);
        }

        return deleted;
    }
}
