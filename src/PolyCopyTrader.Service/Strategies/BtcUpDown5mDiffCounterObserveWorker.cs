using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class BtcUpDown5mDiffCounterObserveWorker(
    ILogger<BtcUpDown5mDiffCounterObserveWorker> logger,
    BotOptions botOptions,
    PaperTradingOptions paperTradingOptions,
    BtcUpDown5mStrategyOptions options,
    IBtcUpDown5mPaperStrategyProcessor processor,
    ServiceControlState controlState,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("BTC Up or Down 5m fast Diff observe worker is disabled.");
            return;
        }

        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            logger.LogInformation(
                "BTC Up or Down 5m fast Diff observe worker will not start. {Reason}",
                RuntimeModePolicy.PaperTradingDisabledReason(botOptions, paperTradingOptions));
            return;
        }

        var interval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        logger.LogInformation(
            "BTC Up or Down 5m fast Diff observe worker started. Mode={Mode} RunInLiveMode={RunInLiveMode} PollIntervalSeconds={PollIntervalSeconds}",
            botOptions.Mode,
            paperTradingOptions.RunInLiveMode,
            options.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                controlState.RecordLoop("BTC5mStrategy fast Diff observe cycle pending", null);
                var result = await processor.ProcessDiffCounterObserveAsync(stoppingToken);
                controlState.RecordLoop(
                    $"BTC5mStrategyFastDiffObserve Observed={result.MarketsObserved}; Skipped={result.RunsSkipped}",
                    null);
                if (result.MarketsObserved > 0 || result.RunsSkipped > 0)
                {
                    logger.LogInformation(
                        "BTC Up or Down 5m fast Diff observe cycle completed. Observed={Observed} Skipped={Skipped}",
                        result.MarketsObserved,
                        result.RunsSkipped);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                controlState.RecordLoop("BTC Up or Down 5m fast Diff observe cycle failed", ex.Message);
                logger.LogError(ex, "BTC Up or Down 5m fast Diff observe cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("BTC Up or Down 5m fast Diff observe worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(
                    Guid.NewGuid(),
                    nameof(BtcUpDown5mDiffCounterObserveWorker),
                    "Cycle",
                    message,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist BTC Up or Down 5m fast Diff observe worker error.");
        }
    }
}
