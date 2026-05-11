using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class LeaderActivityExitWorker(
    ILogger<LeaderActivityExitWorker> logger,
    PaperTradingOptions options,
    ILeaderActivityExitProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.LeaderActivityExitTrackingEnabled)
        {
            logger.LogInformation("Leader activity exit tracking worker is disabled.");
            return;
        }

        var currentErrorDelay = TimeSpan.FromMilliseconds(options.LeaderActivityExitTrackingErrorDelayMilliseconds);
        logger.LogInformation(
            "Leader activity exit tracking worker started. PollDelayMilliseconds={PollDelayMilliseconds} BatchSize={BatchSize} ActivityLimit={ActivityLimit}",
            options.LeaderActivityExitTrackingPollDelayMilliseconds,
            options.LeaderActivityExitTrackingBatchSize,
            options.LeaderActivityExitTrackingActivityLimit);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.ProcessOnceAsync(stoppingToken);
                currentErrorDelay = TimeSpan.FromMilliseconds(options.LeaderActivityExitTrackingErrorDelayMilliseconds);
                if (result.PositionsSelected > 0 ||
                    result.ExitOrdersCreated > 0 ||
                    result.Errors > 0)
                {
                    logger.LogInformation(
                        "Leader activity exit tracking cycle completed. Positions={Positions} Wallets={Wallets} ActivityRows={ActivityRows} SellEvents={SellEvents} ExitOrders={ExitOrders} Duplicates={Duplicates} Errors={Errors}",
                        result.PositionsSelected,
                        result.WalletsChecked,
                        result.ActivityRowsFetched,
                        result.SellEventsMatched,
                        result.ExitOrdersCreated,
                        result.DuplicateEvents,
                        result.Errors);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Leader activity exit tracking cycle failed. Retrying in {DelayMilliseconds} ms.", currentErrorDelay.TotalMilliseconds);
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
                await Task.Delay(currentErrorDelay, stoppingToken);
                currentErrorDelay = TimeSpan.FromMilliseconds(Math.Min(
                    options.LeaderActivityExitTrackingMaxErrorDelayMilliseconds,
                    Math.Max(options.LeaderActivityExitTrackingErrorDelayMilliseconds, currentErrorDelay.TotalMilliseconds * 2)));
                continue;
            }

            if (options.LeaderActivityExitTrackingPollDelayMilliseconds > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(options.LeaderActivityExitTrackingPollDelayMilliseconds), stoppingToken);
            }
            else
            {
                await Task.Yield();
            }
        }

        logger.LogInformation("Leader activity exit tracking worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "LeaderActivityExitWorker", "ProcessLeaderActivityExits", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist leader activity exit worker error.");
        }
    }
}
