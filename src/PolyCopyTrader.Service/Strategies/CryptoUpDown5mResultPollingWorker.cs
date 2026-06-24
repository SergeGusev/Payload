using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class CryptoUpDown5mResultPollingWorker(
    ILogger<CryptoUpDown5mResultPollingWorker> logger,
    CryptoUpDown5mResultPollingOptions options,
    ICryptoUpDown5mResultPollingProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Crypto Up or Down 5m result polling statistics worker is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        logger.LogInformation(
            "Crypto Up or Down 5m result polling statistics worker started. Assets={Assets} PollIntervalSeconds={PollIntervalSeconds} MaxMarketsPerCycle={MaxMarketsPerCycle} MaxMarketAgeMinutes={MaxMarketAgeMinutes} MaxResultWaitMinutes={MaxResultWaitMinutes}",
            string.Join(",", options.AssetSymbols),
            options.PollIntervalSeconds,
            options.MaxMarketsPerCycle,
            options.MaxMarketAgeMinutes,
            options.MaxResultWaitMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.ProcessAsync(stoppingToken);
                if (result.Candidates > 0 || result.ResultsFound > 0 || result.Errors > 0 || result.TimedOut > 0)
                {
                    logger.LogInformation(
                        "Crypto Up or Down 5m result polling cycle completed. MarketsScanned={MarketsScanned} Candidates={Candidates} PollsSent={PollsSent} ResultsFound={ResultsFound} TimedOut={TimedOut} Errors={Errors}",
                        result.MarketsScanned,
                        result.Candidates,
                        result.PollsSent,
                        result.ResultsFound,
                        result.TimedOut,
                        result.Errors);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Crypto Up or Down 5m result polling statistics cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("Crypto Up or Down 5m result polling statistics worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), nameof(CryptoUpDown5mResultPollingWorker), "Cycle", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Crypto Up or Down 5m result polling worker error.");
        }
    }
}
