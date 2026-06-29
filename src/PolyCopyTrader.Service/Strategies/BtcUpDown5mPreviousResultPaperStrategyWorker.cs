using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class BtcUpDown5mPreviousResultPaperStrategyWorker(
    ILogger<BtcUpDown5mPreviousResultPaperStrategyWorker> logger,
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
            logger.LogInformation("BTC Up or Down 5m previous-result paper strategy worker is disabled.");
            return;
        }

        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            logger.LogInformation(
                "BTC Up or Down 5m previous-result paper strategy worker will not start. {Reason}",
                RuntimeModePolicy.PaperTradingDisabledReason(botOptions, paperTradingOptions));
            return;
        }

        var interval = TimeSpan.FromMilliseconds(options.DiffCounterFastPollIntervalMilliseconds);
        logger.LogInformation(
            "BTC Up or Down 5m previous-result due-entry paper strategy worker started. Mode={Mode} RunInLiveMode={RunInLiveMode} PollIntervalMilliseconds={PollIntervalMilliseconds}",
            botOptions.Mode,
            paperTradingOptions.RunInLiveMode,
            options.DiffCounterFastPollIntervalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                controlState.RecordLoop("BTC5mStrategy previous-result due-entry cycle pending", null);
                var result = await processor.ProcessPreviousResultFastDueEntriesAsync(stoppingToken);
                controlState.RecordLoop(
                    $"BTC5mStrategyPreviousResultDue Entries={result.EntriesPlaced}; Skipped={result.RunsSkipped}; Settled={result.RunsSettled}",
                    null);
                if (result.EntriesPlaced > 0 ||
                    result.RunsSkipped > 0 ||
                    result.RunsSettled > 0)
                {
                    logger.LogInformation(
                        "BTC Up or Down 5m previous-result due-entry paper strategy cycle completed. Entries={Entries} Skipped={Skipped} Settled={Settled}",
                        result.EntriesPlaced,
                        result.RunsSkipped,
                        result.RunsSettled);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                controlState.RecordLoop("BTC Up or Down 5m previous-result due-entry paper strategy cycle failed", ex.Message);
                logger.LogError(ex, "BTC Up or Down 5m previous-result due-entry paper strategy cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("BTC Up or Down 5m previous-result due-entry paper strategy worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(
                    Guid.NewGuid(),
                    nameof(BtcUpDown5mPreviousResultPaperStrategyWorker),
                    "Cycle",
                    message,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist BTC Up or Down 5m previous-result due-entry paper strategy worker error.");
        }
    }
}
