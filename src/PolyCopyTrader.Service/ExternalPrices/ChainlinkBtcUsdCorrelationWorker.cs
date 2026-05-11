using System.Globalization;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed class ChainlinkBtcUsdCorrelationWorker(
    ILogger<ChainlinkBtcUsdCorrelationWorker> logger,
    HttpClient httpClient,
    ChainlinkBtcUsdDiagnosticsOptions options,
    IBtcUsdReferencePriceClient btcUsdReferencePriceClient,
    IAppRepository repository) : BackgroundService
{
    private const string ComponentName = nameof(ChainlinkBtcUsdCorrelationWorker);
    private const string BenchmarkAttributeName = "benchmark";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Chainlink BTC/USD correlation diagnostics are disabled.");
            return;
        }

        logger.LogInformation(
            "Chainlink BTC/USD correlation diagnostics started. BaseUrl={BaseUrl} FeedId={FeedId} PollIntervalSeconds={PollIntervalSeconds} QueryWindow={QueryWindow}",
            options.BaseUrl,
            options.FeedId,
            options.PollIntervalSeconds,
            options.QueryWindow);

        var interval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CaptureOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Chainlink BTC/USD correlation diagnostics failed.");
                await TryRecordApiErrorAsync("CaptureCorrelationSample", ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("Chainlink BTC/USD correlation diagnostics stopped.");
    }

    private async Task CaptureOnceAsync(CancellationToken cancellationToken)
    {
        BtcUsdReferencePricePoint binance;
        try
        {
            binance = await btcUsdReferencePriceClient.GetBtcUsdPriceAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "Chainlink BTC/USD correlation diagnostics skipped because the Binance trade stream has no fresh price.");
            return;
        }

        var nodes = await FetchChainlinkNodesAsync(cancellationToken);
        if (nodes.Count == 0)
        {
            logger.LogWarning("Chainlink BTC/USD correlation diagnostics skipped because Chainlink returned no benchmark nodes.");
            return;
        }

        var nearest = nodes
            .OrderBy(node => Math.Abs((node.ValidAfterUtc - binance.SourceUpdatedAtUtc).TotalMilliseconds))
            .First();
        var nearestAge = Math.Abs((nearest.ValidAfterUtc - binance.SourceUpdatedAtUtc).TotalSeconds);
        if (nearestAge > options.MaxNearestAgeSeconds)
        {
            logger.LogWarning(
                "Chainlink BTC/USD correlation diagnostics skipped because nearest Chainlink point is too far from Binance sample. BinanceUtc={BinanceUtc} ChainlinkUtc={ChainlinkUtc} DeltaSeconds={DeltaSeconds} MaxNearestAgeSeconds={MaxNearestAgeSeconds}",
                binance.SourceUpdatedAtUtc,
                nearest.ValidAfterUtc,
                nearestAge,
                options.MaxNearestAgeSeconds);
            return;
        }

        var diffUsd = binance.PriceUsd - nearest.PriceUsd;
        var diffBps = nearest.PriceUsd == 0m ? 0m : diffUsd / nearest.PriceUsd * 10_000m;
        var sample = new BtcUsdReferenceCorrelationSample(
            Guid.NewGuid(),
            binance.PriceUsd,
            binance.SourceUpdatedAtUtc,
            binance.FetchedAtUtc,
            nearest.PriceUsd,
            nearest.ValidAfterUtc,
            Convert.ToDecimal((nearest.ValidAfterUtc - binance.SourceUpdatedAtUtc).TotalSeconds),
            diffUsd,
            diffBps,
            options.FeedId,
            options.QueryWindow,
            nearest.RawJson,
            DateTimeOffset.UtcNow);

        await repository.AddBtcUsdReferenceCorrelationSampleAsync(sample, cancellationToken);
        logger.LogInformation(
            "BTC/USD reference correlation sample stored. BinancePrice={BinancePrice} ChainlinkPrice={ChainlinkPrice} DiffUsd={DiffUsd} DiffBps={DiffBps} TimeDeltaSeconds={TimeDeltaSeconds}",
            sample.BinancePriceUsd,
            sample.ChainlinkPriceUsd,
            sample.PriceDiffUsd,
            sample.PriceDiffBps,
            sample.TimeDeltaSeconds);
    }

    private async Task<IReadOnlyList<ChainlinkNode>> FetchChainlinkNodesAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var response = await httpClient.GetAsync(BuildLiveDataUri(), timeout.Token);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Chainlink BTC/USD request failed with HTTP {(int)response.StatusCode} {response.StatusCode}: {TrimBody(body)}");
        }

        return ParseChainlinkNodes(body);
    }

    public static IReadOnlyList<ChainlinkNode> ParseChainlinkNodes(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("allStreamValuesGenerics", out var values) ||
            !values.TryGetProperty("nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<ChainlinkNode>();
        foreach (var node in nodes.EnumerateArray())
        {
            var attributeName = ReadString(node, "attributeName");
            if (!string.Equals(attributeName, BenchmarkAttributeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var valueText = ReadString(node, "valueNumeric");
            var validAfterText = ReadString(node, "validAfterTs");
            if (!decimal.TryParse(valueText, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) ||
                price <= 0m ||
                !DateTimeOffset.TryParse(validAfterText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var validAfterUtc))
            {
                continue;
            }

            result.Add(new ChainlinkNode(price, validAfterUtc.ToUniversalTime(), node.GetRawText()));
        }

        return result;
    }

    private Uri BuildLiveDataUri()
    {
        var baseUri = options.BaseUrl.TrimEnd('/');
        var query = string.Join(
            "&",
            "feedId=" + Uri.EscapeDataString(options.FeedId),
            "abiIndex=0",
            "queryWindow=" + Uri.EscapeDataString(options.QueryWindow),
            "attributeName=" + BenchmarkAttributeName);
        return new Uri(baseUri + "/api/live-data-engine-stream-data?" + query);
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), ComponentName, operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Chainlink BTC/USD diagnostics error.");
        }
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string TrimBody(string body)
    {
        var trimmed = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }

    public sealed record ChainlinkNode(decimal PriceUsd, DateTimeOffset ValidAfterUtc, string RawJson);
}
