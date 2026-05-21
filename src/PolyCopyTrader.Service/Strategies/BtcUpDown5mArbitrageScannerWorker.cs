using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class BtcUpDown5mArbitrageScannerWorker(
    ILogger<BtcUpDown5mArbitrageScannerWorker> logger,
    BtcUpDown5mArbitrageScannerOptions options,
    IBtcUpDown5mArbitrageScannerProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("BTC Up or Down 5m arbitrage scanner is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        logger.LogInformation(
            "BTC Up or Down 5m arbitrage scanner started. PollIntervalSeconds={PollIntervalSeconds} MinExecutableShares={MinExecutableShares} MaxExecutableShares={MaxExecutableShares} SafetyBufferPerShare={SafetyBufferPerShare} MinNetProfitUsd={MinNetProfitUsd}",
            options.PollIntervalSeconds,
            options.MinExecutableShares,
            options.MaxExecutableShares,
            options.SafetyBufferPerShare,
            options.MinNetProfitUsd);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.ProcessAsync(stoppingToken);
                if (result.ScansStored > 0 ||
                    result.Opportunities > 0 ||
                    result.MissingOrderBooks > 0 ||
                    result.InsufficientDepth > 0)
                {
                    logger.LogInformation(
                        "BTC Up or Down 5m arbitrage scanner cycle completed. Markets={Markets} Stored={Stored} Opportunities={Opportunities} MissingBooks={MissingBooks} InsufficientDepth={InsufficientDepth} NoOpportunity={NoOpportunity}",
                        result.MarketsScanned,
                        result.ScansStored,
                        result.Opportunities,
                        result.MissingOrderBooks,
                        result.InsufficientDepth,
                        result.NoOpportunity);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BTC Up or Down 5m arbitrage scanner cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("BTC Up or Down 5m arbitrage scanner stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), nameof(BtcUpDown5mArbitrageScannerWorker), "Cycle", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to persist BTC Up or Down 5m arbitrage scanner worker error.");
        }
    }
}
