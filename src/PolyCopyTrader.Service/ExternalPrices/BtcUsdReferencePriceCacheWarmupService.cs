using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed class BtcUsdReferencePriceCacheWarmupService(
    ILogger<BtcUsdReferencePriceCacheWarmupService> logger,
    BinanceBtcUsdReferenceOptions options,
    IBtcUsdReferencePriceCache cache,
    IAppRepository repository) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Binance BTC/USDT reference cache warm-up skipped because the reference stream is disabled.");
            return;
        }

        if (cache.Snapshot.SampleCount > 0)
        {
            logger.LogInformation("Binance BTC/USDT reference cache warm-up skipped because the cache already has samples.");
            return;
        }

        IReadOnlyList<BtcUsdReferencePricePoint> points;
        try
        {
            points = await repository.GetRecentBtcUsdReferencePricePointsAsync(options.WindowSize, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Binance BTC/USDT reference cache warm-up failed.");
            return;
        }

        foreach (var point in points.OrderBy(point => point.FetchedAtUtc).ThenBy(point => point.SourceUpdatedAtUtc))
        {
            cache.Add(point);
        }

        var snapshot = cache.Snapshot;
        logger.LogInformation(
            "Binance BTC/USDT reference cache warm-up loaded {LoadedSamples} historical sample(s). Samples={Samples} WindowSize={WindowSize} IsFullWindow={IsFullWindow} ArithmeticMeanUsd={ArithmeticMeanUsd}",
            points.Count,
            snapshot.SampleCount,
            snapshot.WindowSize,
            snapshot.IsFullWindow,
            snapshot.ArithmeticMeanUsd);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
