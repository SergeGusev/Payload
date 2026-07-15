using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class BtcUpDown5mDiffCounterPaperStrategyWorker(
    ILogger<BtcUpDown5mDiffCounterPaperStrategyWorker> logger,
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
            logger.LogInformation("BTC Up or Down 5m fast Diff due-entry paper strategy worker is disabled.");
            return;
        }

        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            logger.LogInformation(
                "BTC Up or Down 5m fast Diff due-entry paper strategy worker will not start. {Reason}",
                RuntimeModePolicy.PaperTradingDisabledReason(botOptions, paperTradingOptions));
            return;
        }

        var interval = TimeSpan.FromMilliseconds(options.DiffCounterFastPollIntervalMilliseconds);
        logger.LogInformation(
            "BTC Up or Down 5m fast Diff due-entry paper strategy worker started. Mode={Mode} RunInLiveMode={RunInLiveMode} PollIntervalMilliseconds={PollIntervalMilliseconds}",
            botOptions.Mode,
            paperTradingOptions.RunInLiveMode,
            options.DiffCounterFastPollIntervalMilliseconds);

        await FixedRateWorkerLoop.RunAsync(interval, async cancellationToken =>
        {
            try
            {
                controlState.RecordLoop("BTC5mStrategy fast Diff due-entry cycle pending", null);
                var result = await processor.ProcessDiffCounterFastDueEntriesAsync(cancellationToken);
                controlState.RecordLoop(
                    $"BTC5mStrategyFastDiffDue Entries={result.EntriesPlaced}; Skipped={result.RunsSkipped}",
                    null);
                if (result.EntriesPlaced > 0 || result.RunsSkipped > 0)
                {
                    logger.LogInformation(
                        "BTC Up or Down 5m fast Diff due-entry paper strategy cycle completed. Entries={Entries} Skipped={Skipped}",
                        result.EntriesPlaced,
                        result.RunsSkipped);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                controlState.RecordLoop("BTC Up or Down 5m fast Diff due-entry paper strategy cycle failed", ex.Message);
                logger.LogError(ex, "BTC Up or Down 5m fast Diff due-entry paper strategy cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, cancellationToken);
            }
        }, stoppingToken);

        logger.LogInformation("BTC Up or Down 5m fast Diff due-entry paper strategy worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(
                    Guid.NewGuid(),
                    nameof(BtcUpDown5mDiffCounterPaperStrategyWorker),
                    "Cycle",
                    message,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist BTC Up or Down 5m fast Diff due-entry paper strategy worker error.");
        }
    }
}
