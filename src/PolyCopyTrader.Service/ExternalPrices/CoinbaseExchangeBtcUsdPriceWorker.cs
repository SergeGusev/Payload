using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed class CoinbaseExchangeBtcUsdPriceWorker(
    ILogger<CoinbaseExchangeBtcUsdPriceWorker> logger,
    CoinbaseExchangeOptions options,
    IBtcUsdReferencePriceClient client,
    IBtcUsdReferencePriceCache cache,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Coinbase Exchange BTC/USD reference price worker is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        logger.LogInformation(
            "Coinbase Exchange BTC/USD reference price worker started. ProductId={ProductId} PollIntervalSeconds={PollIntervalSeconds} WindowSize={WindowSize}",
            options.ProductId,
            options.PollIntervalSeconds,
            options.WindowSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var point = await client.GetBtcUsdPriceAsync(stoppingToken);
                cache.Add(point);

                var snapshot = cache.Snapshot;
                logger.LogInformation(
                    "Coinbase Exchange BTC/USD reference price sampled. PriceUsd={PriceUsd} SourceUpdatedAtUtc={SourceUpdatedAtUtc} Samples={Samples} WindowSize={WindowSize} ArithmeticMeanUsd={ArithmeticMeanUsd}",
                    point.PriceUsd,
                    point.SourceUpdatedAtUtc,
                    snapshot.SampleCount,
                    snapshot.WindowSize,
                    snapshot.ArithmeticMeanUsd);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Coinbase Exchange BTC/USD reference price fetch failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(pollInterval, stoppingToken);
        }

        logger.LogInformation("Coinbase Exchange BTC/USD reference price worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "CoinbaseExchangeBtcUsdPriceWorker", "FetchBtcUsdPrice", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Coinbase Exchange BTC/USD reference price error.");
        }
    }
}
