using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class CryptoUpDown5mBinanceTimedCloseWorker(
    ILogger<CryptoUpDown5mBinanceTimedCloseWorker> logger,
    CryptoUpDown5mResultPollingOptions options,
    ICryptoUpDown5mResultPollingProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !options.BinanceTimedCloseEnabled)
        {
            logger.LogInformation("Crypto Up or Down 5m Binance timed close worker is disabled.");
            return;
        }

        var interval = TimeSpan.FromMilliseconds(Math.Max(1, options.BinanceTimedClosePollIntervalMilliseconds));
        logger.LogInformation(
            "Crypto Up or Down 5m Binance timed close worker started. Assets={Assets} PollIntervalMilliseconds={PollIntervalMilliseconds} CloseDelayMilliseconds={CloseDelayMilliseconds} MaxCandidateAgeSeconds={MaxCandidateAgeSeconds} MaxPriceAgeMilliseconds={MaxPriceAgeMilliseconds} MinMoveBps={MinMoveBps}",
            string.Join(",", options.AssetSymbols),
            options.BinanceTimedClosePollIntervalMilliseconds,
            options.BinanceTimedCloseDelayMilliseconds,
            options.BinanceTimedCloseMaxCandidateAgeSeconds,
            options.BinanceTimedCloseMaxPriceAgeMilliseconds,
            options.BinanceTimedCloseMinMoveBps);

        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.ProcessBinanceTimedCloseAsync(stoppingToken);
                if (result.Candidates > 0 || result.Resolved > 0 || result.SkippedUncertain > 0 || result.Errors > 0)
                {
                    logger.LogInformation(
                        "Crypto Up or Down 5m Binance timed close cycle completed. MarketsScanned={MarketsScanned} Candidates={Candidates} AlreadyResolved={AlreadyResolved} Resolved={Resolved} SkippedUncertain={SkippedUncertain} MissingStartPrice={MissingStartPrice} MissingClosePrice={MissingClosePrice} Errors={Errors}",
                        result.MarketsScanned,
                        result.Candidates,
                        result.AlreadyResolved,
                        result.Resolved,
                        result.SkippedUncertain,
                        result.MissingStartPrice,
                        result.MissingClosePrice,
                        result.Errors);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Crypto Up or Down 5m Binance timed close cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }

        logger.LogInformation("Crypto Up or Down 5m Binance timed close worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), nameof(CryptoUpDown5mBinanceTimedCloseWorker), "Cycle", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Crypto Up or Down 5m Binance timed close worker error.");
        }
    }
}
