using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class BtcUpDown5mOddsArchiveWorker(
    ILogger<BtcUpDown5mOddsArchiveWorker> logger,
    BtcUpDown5mOddsArchiveOptions options,
    IBtcUpDown5mOddsArchiveProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("BTC Up or Down 5m odds archive is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        logger.LogInformation(
            "BTC Up or Down 5m odds archive worker started. PollIntervalSeconds={PollIntervalSeconds} MaxMarketsPerCycle={MaxMarketsPerCycle} MaxOrderBookAgeMilliseconds={MaxOrderBookAgeMilliseconds} RestFallbackEnabled={RestFallbackEnabled}",
            options.PollIntervalSeconds,
            options.MaxMarketsPerCycle,
            options.MaxOrderBookAgeMilliseconds,
            options.RestFallbackEnabled);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.ProcessAsync(stoppingToken);
                if (result.TicksStored > 0 ||
                    result.SkippedNoFreshBtcPrice > 0 ||
                    result.SkippedNoOutcomeTokens > 0 ||
                    result.MissingBothBooks > 0)
                {
                    logger.LogInformation(
                        "BTC Up or Down 5m odds archive cycle completed. Markets={Markets} Stored={Stored} NoFreshBtc={NoFreshBtc} NoOutcomeTokens={NoOutcomeTokens} MissingBothBooks={MissingBothBooks}",
                        result.MarketsScanned,
                        result.TicksStored,
                        result.SkippedNoFreshBtcPrice,
                        result.SkippedNoOutcomeTokens,
                        result.MissingBothBooks);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BTC Up or Down 5m odds archive cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("BTC Up or Down 5m odds archive worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), nameof(BtcUpDown5mOddsArchiveWorker), "Cycle", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist BTC Up or Down 5m odds archive worker error.");
        }
    }
}
