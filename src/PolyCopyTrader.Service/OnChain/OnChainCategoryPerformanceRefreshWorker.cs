using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainCategoryPerformanceRefreshWorker(
    ILogger<OnChainCategoryPerformanceRefreshWorker> logger,
    OnChainIngestionOptions options,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !options.BackgroundCategoryPerformanceRefreshEnabled)
        {
            logger.LogInformation("Background on-chain category performance refresh is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(options.CategoryPerformanceRefreshIntervalSeconds);
        var baseErrorDelay = TimeSpan.FromSeconds(options.BackgroundErrorDelaySeconds);
        var maxErrorDelay = TimeSpan.FromSeconds(options.BackgroundMaxErrorDelaySeconds);
        var currentErrorDelay = baseErrorDelay;

        logger.LogInformation(
            "Background on-chain category performance refresh worker started. IntervalSeconds={IntervalSeconds} PairBatchSize={PairBatchSize} QueueSeedPairBatchSize={QueueSeedPairBatchSize}",
            options.CategoryPerformanceRefreshIntervalSeconds,
            options.CategoryPerformancePairBatchSize,
            options.CategoryPerformanceQueueSeedPairBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await repository.RefreshPolymarketOnChainWalletCategoryPerformanceAsync(
                    options.CategoryPerformancePairBatchSize,
                    options.CategoryPerformanceQueueSeedPairBatchSize,
                    stoppingToken);
                currentErrorDelay = baseErrorDelay;
                logger.LogInformation(
                    "Background on-chain category performance refresh cycle completed. PairsQueued={PairsQueued} PairsProcessed={PairsProcessed} PairsUpserted={PairsUpserted} QueueRemaining={QueueRemaining}",
                    result.PairsQueued,
                    result.PairsProcessed,
                    result.PairsUpserted,
                    result.QueueRemaining);

                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background on-chain category performance refresh cycle failed. Retrying in {DelaySeconds} seconds.", currentErrorDelay.TotalSeconds);
                await TryRecordApiErrorAsync("BackgroundCategoryPerformanceRefresh", ex.Message, stoppingToken);
                await Task.Delay(currentErrorDelay, stoppingToken);
                currentErrorDelay = NextErrorDelay(currentErrorDelay, maxErrorDelay);
            }
        }

        logger.LogInformation("Background on-chain category performance refresh worker stopped.");
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
                new ApiError(Guid.NewGuid(), "OnChainCategoryPerformanceRefreshWorker", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist background on-chain category performance refresh error.");
        }
    }
}
