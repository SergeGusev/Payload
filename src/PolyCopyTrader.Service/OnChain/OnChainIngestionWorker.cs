using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainIngestionWorker(
    ILogger<OnChainIngestionWorker> logger,
    OnChainIngestionOptions options,
    IOnChainIngestionProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !options.BackgroundSyncEnabled)
        {
            logger.LogInformation("Background on-chain ingestion is disabled.");
            return;
        }

        var idleDelay = TimeSpan.FromSeconds(options.BackgroundSyncIdleDelaySeconds);
        var baseErrorDelay = TimeSpan.FromSeconds(options.BackgroundErrorDelaySeconds);
        var maxErrorDelay = TimeSpan.FromSeconds(options.BackgroundMaxErrorDelaySeconds);
        var currentErrorDelay = baseErrorDelay;

        logger.LogInformation(
            "Background on-chain ingestion worker started. IdleDelaySeconds={IdleDelaySeconds} ErrorDelaySeconds={ErrorDelaySeconds} MaxErrorDelaySeconds={MaxErrorDelaySeconds} HistoricalBatchesPerCycle={HistoricalBatchesPerCycle}",
            options.BackgroundSyncIdleDelaySeconds,
            options.BackgroundErrorDelaySeconds,
            options.BackgroundMaxErrorDelaySeconds,
            options.BackgroundHistoricalBatchesPerCycle);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.RefreshBackgroundCycleAsync(stoppingToken);
                currentErrorDelay = baseErrorDelay;
                logger.LogInformation(
                    "Background on-chain ingestion cycle completed. Blocks={FromBlock}-{ToBlock} Logs={Logs} Fills={Fills}",
                    result.FromBlock,
                    result.ToBlock,
                    result.LogsFetched,
                    result.FillsStored);

                await Task.Delay(idleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Background on-chain ingestion cycle was cancelled. It will retry after {DelaySeconds} seconds.", idleDelay.TotalSeconds);
                currentErrorDelay = baseErrorDelay;
                await Task.Delay(idleDelay, stoppingToken);
            }
            catch (InvalidOperationException ex) when (IsAlreadyRunning(ex))
            {
                logger.LogInformation("Background on-chain ingestion skipped because another run is active.");
                await Task.Delay(idleDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background on-chain ingestion cycle failed. Retrying in {DelaySeconds} seconds.", currentErrorDelay.TotalSeconds);
                await TryRecordApiErrorAsync("BackgroundSync", ex.Message, stoppingToken);
                await Task.Delay(currentErrorDelay, stoppingToken);
                currentErrorDelay = NextErrorDelay(currentErrorDelay, maxErrorDelay);
            }
        }

        logger.LogInformation("Background on-chain ingestion worker stopped.");
    }

    private static bool IsAlreadyRunning(Exception ex)
    {
        return ex.Message.Contains("already running", StringComparison.OrdinalIgnoreCase);
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
                new ApiError(Guid.NewGuid(), "OnChainIngestionWorker", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist background on-chain ingestion error.");
        }
    }
}
