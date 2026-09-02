using System.Diagnostics;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed class CryptoReferencePriceRetentionWorker(
    ILogger<CryptoReferencePriceRetentionWorker> logger,
    IAppRepository repository) : BackgroundService
{
    internal const int RetentionHours = 48;
    internal const int BatchSize = 1000;
    internal const int BatchDelaySeconds = 10;
    internal const int ErrorDelaySeconds = 60;

    private static readonly string[] AssetSymbols = ["BTC", "ETH", "SOL"];

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        RunAsync(() => DateTimeOffset.UtcNow, Task.Delay, stoppingToken);

    internal async Task RunAsync(
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Crypto reference price retention started. Assets=BTC,ETH,SOL RetentionHours={RetentionHours} " +
            "BatchSize={BatchSize} BatchDelaySeconds={BatchDelaySeconds}",
            RetentionHours, BatchSize, BatchDelaySeconds);

        var assetIndex = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var assetSymbol = AssetSymbols[assetIndex];
            var sampledBeforeUtc = utcNow().ToUniversalTime().AddHours(-RetentionHours);
            var started = Stopwatch.GetTimestamp();
            var delay = TimeSpan.FromSeconds(BatchDelaySeconds);
            try
            {
                var deleted = await repository.CleanupCryptoReferencePriceTicksAsync(
                    assetSymbol, sampledBeforeUtc, BatchSize, stoppingToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Crypto reference price retention batch completed. Asset={AssetSymbol} " +
                    "SampledBeforeUtc={SampledBeforeUtc:O} Deleted={Deleted} DurationMilliseconds={DurationMilliseconds}",
                    assetSymbol, sampledBeforeUtc, deleted, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                delay = TimeSpan.FromSeconds(ErrorDelaySeconds);
                logger.LogError(
                    ex,
                    "Crypto reference price retention batch failed. Asset={AssetSymbol} " +
                    "SampledBeforeUtc={SampledBeforeUtc:O} DurationMilliseconds={DurationMilliseconds} " +
                    "RetryDelaySeconds={RetryDelaySeconds}",
                    assetSymbol, sampledBeforeUtc, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    ErrorDelaySeconds);
            }

            assetIndex = (assetIndex + 1) % AssetSymbols.Length;
            try
            {
                // Delay after completion, so a slow batch never overlaps the next one.
                await delayAsync(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Crypto reference price retention stopped.");
    }
}
