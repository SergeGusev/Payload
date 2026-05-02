using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainSignalCandidateWorker(
    ILogger<OnChainSignalCandidateWorker> logger,
    OnChainIngestionOptions options,
    IOnChainSignalCandidateProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !options.BackgroundSignalCandidateRefreshEnabled)
        {
            logger.LogInformation("Background on-chain signal candidate refresh is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(options.SignalCandidateRefreshIntervalSeconds);
        var baseErrorDelay = TimeSpan.FromSeconds(options.BackgroundErrorDelaySeconds);
        var maxErrorDelay = TimeSpan.FromSeconds(options.BackgroundMaxErrorDelaySeconds);
        var currentErrorDelay = baseErrorDelay;

        logger.LogInformation(
            "Background on-chain signal candidate worker started. IntervalSeconds={IntervalSeconds} BatchSize={BatchSize} QueueSeedBatchSize={QueueSeedBatchSize} RetryBatchSize={RetryBatchSize}",
            options.SignalCandidateRefreshIntervalSeconds,
            options.SignalCandidateBatchSize,
            options.SignalCandidateQueueSeedBatchSize,
            options.SignalCandidateRetryBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.RefreshAsync(stoppingToken);
                currentErrorDelay = baseErrorDelay;
                logger.LogInformation(
                    "Background on-chain signal candidate cycle completed. SourcesQueued={SourcesQueued} RetriesQueued={RetriesQueued} SourcesFetched={SourcesFetched} CandidatesUpserted={CandidatesUpserted} Accepted={Accepted} Rejected={Rejected} QueueRemaining={QueueRemaining}",
                    result.SourcesQueued,
                    result.RetriesQueued,
                    result.SourcesFetched,
                    result.CandidatesUpserted,
                    result.Accepted,
                    result.Rejected,
                    result.QueueRemaining);

                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background on-chain signal candidate cycle failed. Retrying in {DelaySeconds} seconds.", currentErrorDelay.TotalSeconds);
                await TryRecordApiErrorAsync("BackgroundSignalCandidateRefresh", ex.Message, stoppingToken);
                await Task.Delay(currentErrorDelay, stoppingToken);
                currentErrorDelay = NextErrorDelay(currentErrorDelay, maxErrorDelay);
            }
        }

        logger.LogInformation("Background on-chain signal candidate worker stopped.");
    }

    private static TimeSpan NextErrorDelay(TimeSpan current, TimeSpan max)
    {
        return TimeSpan.FromSeconds(Math.Min(current.TotalSeconds * 2, max.TotalSeconds));
    }

    private async Task TryRecordApiErrorAsync(string operation, string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "OnChainSignalCandidateWorker", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist background on-chain signal candidate error.");
        }
    }
}
