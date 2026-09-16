using System.Net;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed class OkxExpiryFuturesReferencePriceService : BackgroundService, IExpiryFuturesReferencePriceClient
{
    private const string HttpClientName = "OkxExpiryFuturesReference";
    private const int RetryJitterMinimumMilliseconds = 100;
    private const int RetryJitterMaximumMilliseconds = 250;
    private static readonly TimeSpan IncidentSummaryInterval = TimeSpan.FromSeconds(30);

    private readonly ILogger<OkxExpiryFuturesReferencePriceService> logger;
    private readonly OkxExpiryFuturesReferenceOptions options;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IAppRepository repository;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly Func<int, int, int> nextRandom;
    private readonly object sync = new();
    private readonly HashSet<string> enabledAssetSymbols;
    private readonly OkxSourceIncidentTracker incidentTracker = new(IncidentSummaryInterval);
    private IReadOnlyList<OkxExpiryFuturesInstrument> instruments = [];
    private IReadOnlyDictionary<string, OkxExpiryFuturesTicker> tickers =
        new Dictionary<string, OkxExpiryFuturesTicker>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OkxUsdIndexTicker> indicesByAsset = new(StringComparer.OrdinalIgnoreCase);

    public OkxExpiryFuturesReferencePriceService(
        ILogger<OkxExpiryFuturesReferencePriceService> logger,
        OkxExpiryFuturesReferenceOptions options,
        IHttpClientFactory httpClientFactory,
        IAppRepository repository)
        : this(
            logger,
            options,
            httpClientFactory,
            repository,
            static () => DateTimeOffset.UtcNow,
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
            static (minimum, exclusiveMaximum) => Random.Shared.Next(minimum, exclusiveMaximum))
    {
    }

    internal OkxExpiryFuturesReferencePriceService(
        ILogger<OkxExpiryFuturesReferencePriceService> logger,
        OkxExpiryFuturesReferenceOptions options,
        IHttpClientFactory httpClientFactory,
        IAppRepository repository,
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        Func<int, int, int> nextRandom)
    {
        this.logger = logger;
        this.options = options;
        this.httpClientFactory = httpClientFactory;
        this.repository = repository;
        this.utcNow = utcNow;
        this.delayAsync = delayAsync;
        this.nextRandom = nextRandom;
        enabledAssetSymbols = NormalizeSymbols(options.AssetSymbols);
    }

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

        var nowUtc = utcNow();
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
        var loops = new List<Task>
        {
            RunInstrumentPollingLoopAsync(stoppingToken),
            RunPollingLoopAsync(pollInterval, TryRefreshTickersAsync, stoppingToken)
        };
        loops.AddRange(enabledAssetSymbols
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .Select(assetSymbol => RunPollingLoopAsync(
                pollInterval,
                cancellationToken => TryRefreshIndexTickerAsync(assetSymbol, cancellationToken),
                stoppingToken)));

        try
        {
            await Task.WhenAll(loops);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        logger.LogInformation("OKX expiry futures reference price service stopped.");
    }

    internal async Task RunInstrumentPollingLoopAsync(CancellationToken cancellationToken)
    {
        var failedRefreshRetryInterval = TimeSpan.FromMilliseconds(Math.Max(1, options.PollIntervalMilliseconds));
        var successfulRefreshInterval = TimeSpan.FromSeconds(Math.Max(1, options.InstrumentRefreshIntervalSeconds));
        while (!cancellationToken.IsCancellationRequested)
        {
            var refreshed = await TryRefreshInstrumentsAsync(cancellationToken);
            await delayAsync(
                refreshed ? successfulRefreshInterval : failedRefreshRetryInterval,
                cancellationToken);
        }
    }

    private async Task RunPollingLoopAsync(
        TimeSpan interval,
        Func<CancellationToken, Task> refreshAsync,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await refreshAsync(cancellationToken);
            await delayAsync(interval, cancellationToken);
        }
    }

    internal async Task<bool> TryRefreshInstrumentsAsync(CancellationToken cancellationToken)
    {
        const string source = "instruments";
        const string operation = "FetchExpiryFuturesInstruments";
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
            }

            ReportRecovery(source, operation);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await ReportFailureAsync(source, operation, ex, cancellationToken);
            return false;
        }
    }

    internal async Task TryRefreshTickersAsync(CancellationToken cancellationToken)
    {
        const string source = "fixed-expiry-tickers";
        const string operation = "FetchExpiryFuturesTickers";
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

            var payload = await GetPayloadAsync("/api/v5/market/tickers?instType=FUTURES", cancellationToken);
            if (!OkxExpiryFuturesResponseParser.TryParseFuturesTickers(
                    payload,
                    DateTimeOffset.MinValue,
                    instrumentIds,
                    out var parsed,
                    out var error))
            {
                throw new InvalidOperationException(error ?? "OKX expiry futures tickers response could not be parsed.");
            }

            var fetchedAtUtc = utcNow();
            var timestamped = parsed.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with { FetchedAtUtc = fetchedAtUtc },
                StringComparer.OrdinalIgnoreCase);
            lock (sync)
            {
                tickers = timestamped;
            }

            ReportRecovery(source, operation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await ReportFailureAsync(source, operation, ex, cancellationToken);
        }
    }

    internal async Task TryRefreshIndexTickerAsync(string assetSymbol, CancellationToken cancellationToken)
    {
        var source = "index-" + NormalizeSymbol(assetSymbol);
        const string operation = "FetchUsdIndexTicker";
        try
        {
            var path = "/api/v5/market/index-tickers?instId=" + Uri.EscapeDataString(assetSymbol + "-USD");
            var payload = await GetPayloadAsync(path, cancellationToken);
            if (!OkxExpiryFuturesResponseParser.TryParseIndexTicker(
                    payload,
                    DateTimeOffset.MinValue,
                    assetSymbol,
                    out var parsed,
                    out var error) ||
                parsed is null)
            {
                throw new InvalidOperationException(error ?? $"OKX {assetSymbol}-USD index ticker response could not be parsed.");
            }

            var timestamped = parsed with { FetchedAtUtc = utcNow() };
            lock (sync)
            {
                indicesByAsset[timestamped.AssetSymbol] = timestamped;
            }

            ReportRecovery(source, operation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await ReportFailureAsync(source, operation, ex, cancellationToken);
        }
    }

    internal async Task<byte[]> GetPayloadAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await GetPayloadOnceAsync(pathAndQuery, cancellationToken);
            }
            catch (Exception ex) when (attempt == 0 && IsRetryable(ex, cancellationToken))
            {
                var jitterMilliseconds = nextRandom(
                    RetryJitterMinimumMilliseconds,
                    RetryJitterMaximumMilliseconds + 1);
                await delayAsync(TimeSpan.FromMilliseconds(jitterMilliseconds), cancellationToken);
            }
        }
    }

    private async Task<byte[]> GetPayloadOnceAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var uri = new Uri(options.RestBaseUrl.TrimEnd('/') + pathAndQuery);
        using var response = await client.GetAsync(uri, cancellationToken);
        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new OkxHttpStatusException(
                response.StatusCode,
                $"OKX public market data returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {DecodeBody(payload)}");
        }

        return payload;
    }

    private static bool IsRetryable(Exception exception, CancellationToken callerCancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            return !callerCancellationToken.IsCancellationRequested;
        }

        return exception is OkxHttpStatusException statusException &&
            (statusException.StatusCode == HttpStatusCode.TooManyRequests ||
             (int)statusException.StatusCode >= 500);
    }

    private async Task ReportFailureAsync(
        string source,
        string operation,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var report = incidentTracker.RecordFailure(source, utcNow());
        if (!report.ShouldReport)
        {
            return;
        }

        var message = report.IsFirst
            ? $"OKX source {source} failed. IncidentStartedAtUtc={report.StartedAtUtc:O}; FailureCount={report.FailureCount}; Error={exception.Message}"
            : $"OKX source {source} failure incident continues. IncidentStartedAtUtc={report.StartedAtUtc:O}; LastFailureAtUtc={report.LastFailureAtUtc:O}; FailureCount={report.FailureCount}; Error={exception.Message}";
        logger.LogWarning(
            exception,
            "OKX source refresh failed. Source={Source} Operation={Operation} IncidentStartedAtUtc={IncidentStartedAtUtc} LastFailureAtUtc={LastFailureAtUtc} FailureCount={FailureCount} IsFirstFailure={IsFirstFailure}",
            source,
            operation,
            report.StartedAtUtc,
            report.LastFailureAtUtc,
            report.FailureCount,
            report.IsFirst);
        await TryRecordApiErrorAsync(operation, message, cancellationToken);
    }

    private void ReportRecovery(string source, string operation)
    {
        var recovery = incidentTracker.RecordRecovery(source, utcNow());
        if (recovery is null)
        {
            return;
        }

        logger.LogInformation(
            "OKX source refresh recovered. Source={Source} Operation={Operation} IncidentStartedAtUtc={IncidentStartedAtUtc} RecoveredAtUtc={RecoveredAtUtc} IncidentDurationMilliseconds={IncidentDurationMilliseconds} FailureCount={FailureCount}",
            source,
            operation,
            recovery.StartedAtUtc,
            recovery.RecoveredAtUtc,
            recovery.Duration.TotalMilliseconds,
            recovery.FailureCount);
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
                new ApiError(Guid.NewGuid(), "OkxExpiryFuturesReferencePriceService", operation, message, utcNow()),
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

internal sealed class OkxHttpStatusException(HttpStatusCode statusCode, string message) : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

internal sealed class OkxSourceIncidentTracker(TimeSpan summaryInterval)
{
    private readonly object gate = new();
    private readonly Dictionary<string, IncidentState> incidents = new(StringComparer.OrdinalIgnoreCase);

    public OkxSourceFailureReport RecordFailure(string source, DateTimeOffset observedAtUtc)
    {
        lock (gate)
        {
            if (!incidents.TryGetValue(source, out var state))
            {
                state = new IncidentState(
                    observedAtUtc,
                    observedAtUtc,
                    FailureCount: 1,
                    NextSummaryAtUtc: observedAtUtc + summaryInterval);
                incidents[source] = state;
                return new OkxSourceFailureReport(
                    ShouldReport: true,
                    IsFirst: true,
                    state.StartedAtUtc,
                    state.LastFailureAtUtc,
                    state.FailureCount);
            }

            state = state with
            {
                LastFailureAtUtc = observedAtUtc,
                FailureCount = state.FailureCount + 1
            };
            var shouldReport = observedAtUtc >= state.NextSummaryAtUtc;
            if (shouldReport)
            {
                state = state with { NextSummaryAtUtc = observedAtUtc + summaryInterval };
            }

            incidents[source] = state;
            return new OkxSourceFailureReport(
                shouldReport,
                IsFirst: false,
                state.StartedAtUtc,
                state.LastFailureAtUtc,
                state.FailureCount);
        }
    }

    public OkxSourceRecoveryReport? RecordRecovery(string source, DateTimeOffset recoveredAtUtc)
    {
        lock (gate)
        {
            if (!incidents.Remove(source, out var state))
            {
                return null;
            }

            return new OkxSourceRecoveryReport(
                state.StartedAtUtc,
                recoveredAtUtc,
                recoveredAtUtc - state.StartedAtUtc,
                state.FailureCount);
        }
    }

    private sealed record IncidentState(
        DateTimeOffset StartedAtUtc,
        DateTimeOffset LastFailureAtUtc,
        long FailureCount,
        DateTimeOffset NextSummaryAtUtc);
}

internal sealed record OkxSourceFailureReport(
    bool ShouldReport,
    bool IsFirst,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastFailureAtUtc,
    long FailureCount);

internal sealed record OkxSourceRecoveryReport(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset RecoveredAtUtc,
    TimeSpan Duration,
    long FailureCount);
