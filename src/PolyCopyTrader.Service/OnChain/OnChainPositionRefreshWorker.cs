using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainPositionRefreshWorker(
    ILogger<OnChainPositionRefreshWorker> logger,
    OnChainIngestionOptions options,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !options.BackgroundPositionRefreshEnabled)
        {
            logger.LogInformation("Background on-chain position refresh is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(options.PositionRefreshIntervalSeconds);
        var baseErrorDelay = TimeSpan.FromSeconds(options.BackgroundErrorDelaySeconds);
        var maxErrorDelay = TimeSpan.FromSeconds(options.BackgroundMaxErrorDelaySeconds);
        var currentErrorDelay = baseErrorDelay;

        logger.LogInformation(
            "Background on-chain position refresh worker started. IntervalSeconds={IntervalSeconds} TokenBatchSize={TokenBatchSize} QueueSeedTokenBatchSize={QueueSeedTokenBatchSize}",
            options.PositionRefreshIntervalSeconds,
            options.PositionRefreshTokenBatchSize,
            options.PositionRefreshQueueSeedTokenBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await repository.RefreshPolymarketOnChainWalletPositionsAsync(
                    options.PositionRefreshTokenBatchSize,
                    options.PositionRefreshQueueSeedTokenBatchSize,
                    stoppingToken);
                currentErrorDelay = baseErrorDelay;
                logger.LogInformation(
                    "Background on-chain position refresh cycle completed. TokensQueued={TokensQueued} TokensProcessed={TokensProcessed} PositionsUpserted={PositionsUpserted} QueueRemaining={QueueRemaining}",
                    result.TokensQueued,
                    result.TokensProcessed,
                    result.PositionsUpserted,
                    result.QueueRemaining);

                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background on-chain position refresh cycle failed. Retrying in {DelaySeconds} seconds.", currentErrorDelay.TotalSeconds);
                await TryRecordApiErrorAsync("BackgroundPositionRefresh", ex.Message, stoppingToken);
                await Task.Delay(currentErrorDelay, stoppingToken);
                currentErrorDelay = NextErrorDelay(currentErrorDelay, maxErrorDelay);
            }
        }

        logger.LogInformation("Background on-chain position refresh worker stopped.");
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
                new ApiError(Guid.NewGuid(), "OnChainPositionRefreshWorker", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist background on-chain position refresh error.");
        }
    }
}
