using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class BtcUpDown5mChildParentRefreshWorker(
    ILogger<BtcUpDown5mChildParentRefreshWorker> logger,
    BotOptions botOptions,
    PaperTradingOptions paperTradingOptions,
    BtcUpDown5mStrategyOptions options,
    IBtcUpDown5mPaperStrategyProcessor processor,
    ServiceControlState controlState,
    IAppRepository repository) : BackgroundService
{
    private static readonly TimeSpan MarketInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("BTC Up or Down 5m Child/Parent refresh worker is disabled.");
            return;
        }

        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            logger.LogInformation(
                "BTC Up or Down 5m Child/Parent refresh worker will not start. {Reason}",
                RuntimeModePolicy.PaperTradingDisabledReason(botOptions, paperTradingOptions));
            return;
        }

        logger.LogInformation(
            "BTC Up or Down 5m Child/Parent refresh worker started. Mode={Mode} RunInLiveMode={RunInLiveMode} DelayAfterMarketStartSeconds={DelayAfterMarketStartSeconds}",
            botOptions.Mode,
            paperTradingOptions.RunInLiveMode,
            options.ChildParentRefreshDelaySeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var nextRefreshUtc = GetNextRefreshUtc(nowUtc, options.ChildParentRefreshDelaySeconds);
            var delay = nextRefreshUtc - nowUtc;
            if (delay > TimeSpan.Zero)
            {
                controlState.RecordLoop(
                    $"BTC5mStrategy Child/Parent refresh scheduled at {nextRefreshUtc:O}",
                    null);
                await Task.Delay(delay, stoppingToken);
            }

            try
            {
                controlState.RecordLoop("BTC5mStrategy Child/Parent refresh pending", null);
                await processor.ProcessChildParentRefreshAsync(stoppingToken);
                controlState.RecordLoop("BTC5mStrategy Child/Parent refresh completed", null);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                controlState.RecordLoop("BTC5mStrategy Child/Parent refresh failed", ex.Message);
                logger.LogError(ex, "BTC Up or Down 5m Child/Parent refresh failed.");
                await TryRecordApiErrorAsync(ex.Message, CancellationToken.None);
            }
        }

        logger.LogInformation("BTC Up or Down 5m Child/Parent refresh worker stopped.");
    }

    internal static DateTimeOffset GetNextRefreshUtc(DateTimeOffset nowUtc, int delayAfterMarketStartSeconds)
    {
        var normalizedNowUtc = nowUtc.ToUniversalTime();
        var intervalSeconds = (long)MarketInterval.TotalSeconds;
        var currentBoundaryUnixSeconds = normalizedNowUtc.ToUnixTimeSeconds() / intervalSeconds * intervalSeconds;
        var candidateUtc = DateTimeOffset
            .FromUnixTimeSeconds(currentBoundaryUnixSeconds)
            .AddSeconds(delayAfterMarketStartSeconds);
        return candidateUtc < normalizedNowUtc
            ? candidateUtc.Add(MarketInterval)
            : candidateUtc;
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(
                    Guid.NewGuid(),
                    nameof(BtcUpDown5mChildParentRefreshWorker),
                    "Cycle",
                    message,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist BTC Up or Down 5m Child/Parent refresh worker error.");
        }
    }
}
