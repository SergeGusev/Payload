using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed class OkxExpiryFuturesReferencePriceService(
    ILogger<OkxExpiryFuturesReferencePriceService> logger,
    OkxExpiryFuturesReferenceOptions options,
    IHttpClientFactory httpClientFactory,
    IAppRepository repository) : BackgroundService, IExpiryFuturesReferencePriceClient
{
    private const string HttpClientName = "OkxExpiryFuturesReference";
    private readonly object sync = new();
    private readonly HashSet<string> enabledAssetSymbols = NormalizeSymbols(options.AssetSymbols);
    private IReadOnlyList<OkxExpiryFuturesInstrument> instruments = [];
    private IReadOnlyDictionary<string, OkxExpiryFuturesTicker> tickers =
        new Dictionary<string, OkxExpiryFuturesTicker>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OkxUsdIndexTicker> indicesByAsset = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset instrumentsFetchedAtUtc = DateTimeOffset.MinValue;

    public Task<IReadOnlyList<ExpiryFuturesReferencePricePoint>> GetNearestExpiryPricesAsync(
        string assetSymbol,
        DateTimeOffset targetMarketEndUtc,
        int requiredExpiryCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredExpiryCount, 1);
        var normalizedAsset = NormalizeSymbol(assetSymbol);
        if (!enabledAssetSymbols.Contains(normalizedAsset))
        {
            throw new InvalidOperationException($"OKX expiry futures asset {normalizedAsset} is not enabled.");
        }

        IReadOnlyList<OkxExpiryFuturesInstrument> selectedInstruments;
        IReadOnlyList<OkxExpiryFuturesTicker?> selectedTickers;
        OkxUsdIndexTicker? indexTicker;
        lock (sync)
        {
            selectedInstruments = OkxExpiryFuturesResponseParser.SelectNearestExpiries(
                instruments,
                normalizedAsset,
                targetMarketEndUtc,
                requiredExpiryCount);
            selectedTickers = selectedInstruments
                .Select(instrument => tickers.TryGetValue(instrument.InstrumentId, out var matchedTicker)
                    ? matchedTicker
                    : null)
                .ToArray();
            indicesByAsset.TryGetValue(normalizedAsset, out indexTicker);
        }

        if (selectedInstruments.Count < requiredExpiryCount)
        {
            throw new InvalidOperationException(
                $"OKX has {selectedInstruments.Count} distinct live linear USD fixed-expiry {normalizedAsset} contract(s) at or after target market end {targetMarketEndUtc:O}; {requiredExpiryCount} required.");
        }

        if (indexTicker is null)
        {
            throw new InvalidOperationException($"OKX {normalizedAsset}-USD index ticker has not received a price yet.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        EnsureFresh("USD index ticker", indexTicker.FetchedAtUtc, indexTicker.SourceUpdatedAtUtc, nowUtc);
        var prices = new ExpiryFuturesReferencePricePoint[requiredExpiryCount];
        for (var index = 0; index < requiredExpiryCount; index++)
        {
            var instrument = selectedInstruments[index];
            var futuresTicker = selectedTickers[index] ?? throw new InvalidOperationException(
                $"OKX fixed-expiry instrument {instrument.InstrumentId} has no valid bid/ask ticker yet.");
            EnsureFresh(
                $"fixed-expiry ticker {instrument.InstrumentId}",
                futuresTicker.FetchedAtUtc,
                futuresTicker.SourceUpdatedAtUtc,
                nowUtc);
            var fetchedAtUtc = futuresTicker.FetchedAtUtc <= indexTicker.FetchedAtUtc
                ? futuresTicker.FetchedAtUtc
                : indexTicker.FetchedAtUtc;
            prices[index] = new ExpiryFuturesReferencePricePoint(
                normalizedAsset,
                instrument.InstrumentId,
                instrument.ExpiryAtUtc,
                futuresTicker.BidPriceUsd,
                futuresTicker.AskPriceUsd,
                futuresTicker.MidPriceUsd,
                indexTicker.IndexPriceUsd,
                futuresTicker.SourceUpdatedAtUtc,
                indexTicker.SourceUpdatedAtUtc,
                fetchedAtUtc,
                OkxExpiryFuturesResponseParser.SourceName);
        }

        return Task.FromResult<IReadOnlyList<ExpiryFuturesReferencePricePoint>>(prices);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("OKX expiry futures reference price service is disabled.");
            return;
        }

        if (enabledAssetSymbols.Count == 0)
        {
            logger.LogWarning("OKX expiry futures reference price service has no configured asset symbols.");
            return;
        }

        logger.LogInformation(
            "OKX expiry futures reference price service started. Assets={Assets} RestBaseUrl={RestBaseUrl} PollIntervalMilliseconds={PollIntervalMilliseconds} InstrumentRefreshIntervalSeconds={InstrumentRefreshIntervalSeconds} StaleAfterSeconds={StaleAfterSeconds}",
            string.Join(",", enabledAssetSymbols.OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)),
            options.RestBaseUrl,
            options.PollIntervalMilliseconds,
            options.InstrumentRefreshIntervalSeconds,
            options.StaleAfterSeconds);

        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(1, options.PollIntervalMilliseconds));
        var instrumentRefreshInterval = TimeSpan.FromSeconds(Math.Max(1, options.InstrumentRefreshIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow - instrumentsFetchedAtUtc >= instrumentRefreshInterval)
            {
                await TryRefreshInstrumentsAsync(stoppingToken);
            }

            var marketDataRefreshes = enabledAssetSymbols
                .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
                .Select(assetSymbol => TryRefreshIndexTickerAsync(assetSymbol, stoppingToken))
                .Prepend(TryRefreshTickersAsync(stoppingToken));
            await Task.WhenAll(marketDataRefreshes);

            await Task.Delay(pollInterval, stoppingToken);
        }

        logger.LogInformation("OKX expiry futures reference price service stopped.");
    }

    private async Task TryRefreshInstrumentsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var payload = await GetPayloadAsync("/api/v5/public/instruments?instType=FUTURES", cancellationToken);
            if (!OkxExpiryFuturesResponseParser.TryParseInstruments(
                    payload,
                    enabledAssetSymbols,
                    out var parsed,
                    out var error))
            {
                throw new InvalidOperationException(error ?? "OKX expiry futures instruments response could not be parsed.");
            }

            var missingAssets = enabledAssetSymbols
                .Where(asset => parsed.All(instrument => !string.Equals(instrument.AssetSymbol, asset, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(asset => asset, StringComparer.Ordinal)
                .ToArray();
            if (missingAssets.Length > 0)
            {
                throw new InvalidOperationException(
                    "OKX returned no live linear USD fixed-expiry contracts for configured assets: " + string.Join(",", missingAssets) + ".");
            }

            lock (sync)
            {
                instruments = parsed;
                instrumentsFetchedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OKX expiry futures instrument refresh failed.");
            await TryRecordApiErrorAsync("FetchExpiryFuturesInstruments", ex.Message, cancellationToken);
        }
    }

    private async Task TryRefreshTickersAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlySet<string> instrumentIds;
            lock (sync)
            {
                instrumentIds = instruments
                    .Select(instrument => instrument.InstrumentId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            if (instrumentIds.Count == 0)
            {
                return;
            }

            var fetchedAtUtc = DateTimeOffset.UtcNow;
            var payload = await GetPayloadAsync("/api/v5/market/tickers?instType=FUTURES", cancellationToken);
            if (!OkxExpiryFuturesResponseParser.TryParseFuturesTickers(
                    payload,
                    fetchedAtUtc,
                    instrumentIds,
                    out var parsed,
                    out var error))
            {
                throw new InvalidOperationException(error ?? "OKX expiry futures tickers response could not be parsed.");
            }

            lock (sync)
            {
                tickers = parsed;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OKX expiry futures ticker refresh failed.");
            await TryRecordApiErrorAsync("FetchExpiryFuturesTickers", ex.Message, cancellationToken);
        }
    }

    private async Task TryRefreshIndexTickerAsync(string assetSymbol, CancellationToken cancellationToken)
    {
        try
        {
            var fetchedAtUtc = DateTimeOffset.UtcNow;
            var path = "/api/v5/market/index-tickers?instId=" + Uri.EscapeDataString(assetSymbol + "-USD");
            var payload = await GetPayloadAsync(path, cancellationToken);
            if (!OkxExpiryFuturesResponseParser.TryParseIndexTicker(
                    payload,
                    fetchedAtUtc,
                    assetSymbol,
                    out var parsed,
                    out var error) ||
                parsed is null)
            {
                throw new InvalidOperationException(error ?? $"OKX {assetSymbol}-USD index ticker response could not be parsed.");
            }

            lock (sync)
            {
                indicesByAsset[parsed.AssetSymbol] = parsed;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OKX USD index ticker refresh failed. Asset={AssetSymbol}", assetSymbol);
            await TryRecordApiErrorAsync("FetchUsdIndexTicker", ex.Message, cancellationToken);
        }
    }

    private async Task<byte[]> GetPayloadAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var uri = new Uri(options.RestBaseUrl.TrimEnd('/') + pathAndQuery);
        using var response = await client.GetAsync(uri, cancellationToken);
        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OKX public market data returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {DecodeBody(payload)}");
        }

        return payload;
    }

    private void EnsureFresh(
        string label,
        DateTimeOffset fetchedAtUtc,
        DateTimeOffset sourceUpdatedAtUtc,
        DateTimeOffset nowUtc)
    {
        var oldestTimestamp = fetchedAtUtc <= sourceUpdatedAtUtc ? fetchedAtUtc : sourceUpdatedAtUtc;
        var age = nowUtc - oldestTimestamp;
        if (age > TimeSpan.FromSeconds(options.StaleAfterSeconds))
        {
            throw new InvalidOperationException(
                $"OKX {label} is stale. AgeSeconds={age.TotalSeconds:0.###}; StaleAfterSeconds={options.StaleAfterSeconds}.");
        }
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "OkxExpiryFuturesReferencePriceService", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist OKX expiry futures reference price error.");
        }
    }

    private static string DecodeBody(byte[] payload)
    {
        return payload.Length == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(payload);
    }

    private static HashSet<string> NormalizeSymbols(IEnumerable<string> symbols)
    {
        return symbols
            .Select(NormalizeSymbol)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeSymbol(string symbol)
    {
        return string.IsNullOrWhiteSpace(symbol)
            ? string.Empty
            : symbol.Trim().ToUpperInvariant();
    }
}
