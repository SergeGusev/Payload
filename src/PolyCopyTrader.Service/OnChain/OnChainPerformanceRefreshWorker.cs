using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainPerformanceRefreshWorker(
    ILogger<OnChainPerformanceRefreshWorker> logger,
    OnChainIngestionOptions options,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !options.BackgroundPerformanceRefreshEnabled)
        {
            logger.LogInformation("Background on-chain performance refresh is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(options.PerformanceRefreshIntervalSeconds);
        var baseErrorDelay = TimeSpan.FromSeconds(options.BackgroundErrorDelaySeconds);
        var maxErrorDelay = TimeSpan.FromSeconds(options.BackgroundMaxErrorDelaySeconds);
        var currentErrorDelay = baseErrorDelay;

        logger.LogInformation(
            "Background on-chain performance refresh worker started. IntervalSeconds={IntervalSeconds} WalletBatchSize={WalletBatchSize} QueueSeedWalletBatchSize={QueueSeedWalletBatchSize}",
            options.PerformanceRefreshIntervalSeconds,
            options.PerformanceRefreshWalletBatchSize,
            options.PerformanceRefreshQueueSeedWalletBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await repository.RefreshPolymarketOnChainWalletPerformanceAsync(
                    options.PerformanceRefreshWalletBatchSize,
                    options.PerformanceRefreshQueueSeedWalletBatchSize,
                    stoppingToken);
                currentErrorDelay = baseErrorDelay;
                logger.LogInformation(
                    "Background on-chain performance refresh cycle completed. WalletsQueued={WalletsQueued} WalletsProcessed={WalletsProcessed} WalletsUpserted={WalletsUpserted} QueueRemaining={QueueRemaining}",
                    result.WalletsQueued,
                    result.WalletsProcessed,
                    result.WalletsUpserted,
                    result.QueueRemaining);

                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background on-chain performance refresh cycle failed. Retrying in {DelaySeconds} seconds.", currentErrorDelay.TotalSeconds);
                await TryRecordApiErrorAsync("BackgroundPerformanceRefresh", ex.Message, stoppingToken);
                await Task.Delay(currentErrorDelay, stoppingToken);
                currentErrorDelay = NextErrorDelay(currentErrorDelay, maxErrorDelay);
            }
        }

        logger.LogInformation("Background on-chain performance refresh worker stopped.");
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
                new ApiError(Guid.NewGuid(), "OnChainPerformanceRefreshWorker", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist background on-chain performance refresh error.");
        }
    }
}
