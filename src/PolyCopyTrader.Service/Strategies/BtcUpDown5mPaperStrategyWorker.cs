using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class BtcUpDown5mPaperStrategyWorker(
    ILogger<BtcUpDown5mPaperStrategyWorker> logger,
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
            logger.LogInformation("BTC Up or Down 5m paper strategy is disabled.");
            return;
        }

        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            logger.LogInformation(
                "BTC Up or Down 5m paper strategy will not start. {Reason}",
                RuntimeModePolicy.PaperTradingDisabledReason(botOptions, paperTradingOptions));
            return;
        }

        var interval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        var enabledVariantCount = options.EnabledVariantCodes is null || options.EnabledVariantCodes.Count == 0
            ? StrategyIds.UpDown5mStrategyVariants.Count
            : options.EnabledVariantCodes.Count;
        logger.LogInformation(
            "BTC Up or Down 5m paper strategy worker started. Mode={Mode} RunInLiveMode={RunInLiveMode} PollIntervalSeconds={PollIntervalSeconds} VariantCount={VariantCount} StakeUsd={StakeUsd} EntryGraceSeconds={EntryGraceSeconds} MaxConcurrentEntryDecisions={MaxConcurrentEntryDecisions}",
            botOptions.Mode,
            paperTradingOptions.RunInLiveMode,
            options.PollIntervalSeconds,
            enabledVariantCount,
            options.StakeUsd,
            options.EntryGraceSeconds,
            options.MaxConcurrentEntryDecisions);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                controlState.RecordLoop("BTC Up or Down 5m paper strategy cycle pending", null);
                var result = await processor.ProcessAsync(stoppingToken);
                controlState.RecordLoop(
                    $"BTC5mStrategy Observed={result.MarketsObserved}; Entries={result.EntriesPlaced}; Skipped={result.RunsSkipped}; Settled={result.RunsSettled}",
                    null);
                if (result.MarketsObserved > 0 ||
                    result.EntriesPlaced > 0 ||
                    result.RunsSkipped > 0 ||
                    result.RunsSettled > 0)
                {
                    logger.LogInformation(
                        "BTC Up or Down 5m paper strategy cycle completed. Observed={Observed} Entries={Entries} Skipped={Skipped} Settled={Settled}",
                        result.MarketsObserved,
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
                controlState.RecordLoop("BTC Up or Down 5m paper strategy cycle failed", ex.Message);
                logger.LogError(ex, "BTC Up or Down 5m paper strategy cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("BTC Up or Down 5m paper strategy worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "BtcUpDown5mPaperStrategyWorker", "Cycle", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist BTC Up or Down 5m paper strategy worker error.");
        }
    }
}
