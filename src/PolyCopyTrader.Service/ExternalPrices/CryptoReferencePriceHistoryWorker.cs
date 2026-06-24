using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed class CryptoReferencePriceHistoryWorker(
    ILogger<CryptoReferencePriceHistoryWorker> logger,
    CryptoReferencePriceHistoryOptions options,
    IAppRepository repository,
    IBtcUsdReferencePriceClient btcUsdReferencePriceClient,
    ICryptoReferencePriceClient cryptoReferencePriceClient,
    ICryptoReferencePriceAverageCache averageCache) : BackgroundService
{
    private readonly string[] assetSymbols = options.AssetSymbols
        .Select(NormalizeAssetSymbol)
        .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Crypto reference price history worker is disabled.");
            return;
        }

        if (assetSymbols.Length == 0)
        {
            logger.LogWarning("Crypto reference price history worker has no configured assets.");
            return;
        }

        await InitializeCacheAsync(stoppingToken);

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.WriteIntervalSeconds));
        logger.LogInformation(
            "Crypto reference price history worker started. Assets={Assets} WriteIntervalSeconds={WriteIntervalSeconds} StartupLookbackHours={StartupLookbackHours}",
            string.Join(",", assetSymbols),
            options.WriteIntervalSeconds,
            options.StartupLookbackHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleStartedAtUtc = DateTimeOffset.UtcNow;
            try
            {
                var stored = await WriteCurrentTicksAsync(stoppingToken);
                logger.LogDebug(
                    "Crypto reference price history cycle completed. Stored={Stored} Assets={Assets}",
                    stored,
                    assetSymbols.Length);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Crypto reference price history cycle failed.");
                await TryRecordApiErrorAsync("Cycle", ex.Message, stoppingToken);
            }

            var elapsed = DateTimeOffset.UtcNow - cycleStartedAtUtc;
            var delay = interval - elapsed;
            await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1), stoppingToken);
        }

        logger.LogInformation("Crypto reference price history worker stopped.");
    }

    private async Task InitializeCacheAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var startUtc = nowUtc.AddHours(-Math.Max(1, options.StartupLookbackHours));

        try
        {
            var ticks = await repository.GetCryptoReferencePriceTicksAsync(
                assetSymbols,
                startUtc,
                nowUtc,
                cancellationToken);
            averageCache.Reset(ticks, nowUtc);
            logger.LogInformation(
                "Crypto reference price averages initialized from PostgreSQL. Ticks={Ticks} Assets={Assets} StartUtc={StartUtc} EndUtc={EndUtc}",
                ticks.Count,
                string.Join(",", assetSymbols),
                startUtc,
                nowUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize crypto reference price averages from PostgreSQL.");
            averageCache.Reset([], DateTimeOffset.UtcNow);
            await TryRecordApiErrorAsync("InitializeCache", ex.Message, cancellationToken);
        }
    }

    private async Task<int> WriteCurrentTicksAsync(CancellationToken cancellationToken)
    {
        var stored = 0;
        foreach (var assetSymbol in assetSymbols)
        {
            try
            {
                var tick = await CreateCurrentTickAsync(assetSymbol, cancellationToken);
                await repository.UpsertCryptoReferencePriceTickAsync(tick, cancellationToken);
                averageCache.Add(tick, DateTimeOffset.UtcNow);
                stored++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Crypto reference price tick skipped. Asset={AssetSymbol}",
                    assetSymbol);
                await TryRecordApiErrorAsync("WriteCurrentTick:" + assetSymbol, ex.Message, cancellationToken);
            }
        }

        return stored;
    }

    private async Task<CryptoReferencePriceTick> CreateCurrentTickAsync(
        string assetSymbol,
        CancellationToken cancellationToken)
    {
        var sampledAtUtc = DateTimeOffset.UtcNow;
        if (string.Equals(assetSymbol, "BTC", StringComparison.OrdinalIgnoreCase))
        {
            var btcPrice = await btcUsdReferencePriceClient.GetBtcUsdPriceAsync(cancellationToken);
            return CreateTick(
                assetSymbol,
                "BTCUSDT",
                btcPrice.PriceUsd,
                btcPrice.SourceUpdatedAtUtc,
                btcPrice.FetchedAtUtc,
                btcPrice.Source,
                sampledAtUtc);
        }

        var cryptoPrice = await cryptoReferencePriceClient.GetPriceAsync(assetSymbol, cancellationToken);
        return CreateTick(
            NormalizeAssetSymbol(cryptoPrice.AssetSymbol),
            cryptoPrice.BinanceSymbol,
            cryptoPrice.PriceUsd,
            cryptoPrice.SourceUpdatedAtUtc,
            cryptoPrice.FetchedAtUtc,
            cryptoPrice.Source,
            sampledAtUtc);
    }

    private CryptoReferencePriceTick CreateTick(
        string assetSymbol,
        string binanceSymbol,
        decimal priceUsd,
        DateTimeOffset sourceUpdatedAtUtc,
        DateTimeOffset fetchedAtUtc,
        string source,
        DateTimeOffset sampledAtUtc)
    {
        var normalizedSampledAtUtc = sampledAtUtc.ToUniversalTime();
        return new CryptoReferencePriceTick(
            Guid.NewGuid(),
            NormalizeAssetSymbol(assetSymbol),
            NormalizeBinanceSymbol(binanceSymbol, assetSymbol),
            normalizedSampledAtUtc,
            FloorToBucket(normalizedSampledAtUtc, Math.Max(1, options.WriteIntervalSeconds)),
            priceUsd,
            sourceUpdatedAtUtc.ToUniversalTime(),
            fetchedAtUtc.ToUniversalTime(),
            source,
            normalizedSampledAtUtc);
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), nameof(CryptoReferencePriceHistoryWorker), operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist crypto reference price history worker error.");
        }
    }

    private static DateTimeOffset FloorToBucket(DateTimeOffset timestampUtc, int stepSeconds)
    {
        var unixSeconds = timestampUtc.ToUniversalTime().ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds - unixSeconds % stepSeconds);
    }

    private static string NormalizeAssetSymbol(string assetSymbol)
    {
        return assetSymbol.Trim().ToUpperInvariant();
    }

    private static string NormalizeBinanceSymbol(string binanceSymbol, string assetSymbol)
    {
        return string.IsNullOrWhiteSpace(binanceSymbol)
            ? NormalizeAssetSymbol(assetSymbol) + "USDT"
            : binanceSymbol.Trim().ToUpperInvariant();
    }
}
