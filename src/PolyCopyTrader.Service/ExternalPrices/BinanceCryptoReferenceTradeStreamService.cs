using System.Net.WebSockets;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed class BinanceCryptoReferenceTradeStreamService(
    ILogger<BinanceCryptoReferenceTradeStreamService> logger,
    BinanceCryptoReferenceOptions options,
    IAppRepository repository) : BackgroundService, ICryptoReferencePriceClient
{
    private readonly object sync = new();
    private readonly HashSet<string> enabledAssetSymbols = NormalizeSymbols(options.AssetSymbols);
    private readonly Dictionary<string, CryptoReferencePricePoint> latestByAsset = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<BtcUsdReferencePricePoint>> samplesByAsset = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> nextSampleAtUtcByAsset = new(StringComparer.OrdinalIgnoreCase);
    private readonly int windowSize = Math.Max(1, options.WindowSize);
    private readonly TimeSpan sampleInterval = TimeSpan.FromSeconds(Math.Max(1, options.SampleIntervalSeconds));

    public Task<CryptoReferencePricePoint> GetPriceAsync(
        string assetSymbol,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = NormalizeSymbol(assetSymbol);
        CryptoReferencePricePoint? snapshot;
        lock (sync)
        {
            latestByAsset.TryGetValue(normalized, out snapshot);
        }

        if (snapshot is null)
        {
            throw new InvalidOperationException($"Binance {normalized}/USDT trade stream has not received a price yet.");
        }

        var age = DateTimeOffset.UtcNow - snapshot.FetchedAtUtc;
        if (age > TimeSpan.FromSeconds(options.StaleAfterSeconds))
        {
            throw new InvalidOperationException(
                $"Binance {normalized}/USDT trade stream price is stale. AgeSeconds={age.TotalSeconds:0.###}; StaleAfterSeconds={options.StaleAfterSeconds}.");
        }

        return Task.FromResult(snapshot);
    }

    public BtcUsdReferencePriceSnapshot GetSnapshot(string assetSymbol)
    {
        var normalized = NormalizeSymbol(assetSymbol);
        lock (sync)
        {
            samplesByAsset.TryGetValue(normalized, out var samples);
            return CreateSnapshot(samples ?? []);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Binance crypto trade stream reference service is disabled.");
            return;
        }

        if (enabledAssetSymbols.Count == 0)
        {
            logger.LogWarning("Binance crypto trade stream reference service has no configured asset symbols.");
            return;
        }

        var reconnectDelay = TimeSpan.FromSeconds(options.ReconnectBaseDelaySeconds);
        var maxReconnectDelay = TimeSpan.FromSeconds(options.ReconnectMaxDelaySeconds);
        logger.LogInformation(
            "Binance crypto trade stream reference service started. Assets={Assets} StreamUrl={StreamUrl} SampleIntervalSeconds={SampleIntervalSeconds} WindowSize={WindowSize} StaleAfterSeconds={StaleAfterSeconds}",
            string.Join(",", enabledAssetSymbols.OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)),
            BuildStreamUrl(),
            options.SampleIntervalSeconds,
            options.WindowSize,
            options.StaleAfterSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSocketAsync(stoppingToken);
                reconnectDelay = TimeSpan.FromSeconds(options.ReconnectBaseDelaySeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Binance crypto trade stream failed.");
                await TryRecordApiErrorAsync("StreamCryptoTrades", ex.Message, stoppingToken);
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(reconnectDelay, stoppingToken);
                reconnectDelay = TimeSpan.FromSeconds(Math.Min(
                    maxReconnectDelay.TotalSeconds,
                    reconnectDelay.TotalSeconds * 2));
            }
        }

        logger.LogInformation("Binance crypto trade stream reference service stopped.");
    }

    private async Task RunSocketAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        var streamUrl = BuildStreamUrl();
        await socket.ConnectAsync(new Uri(streamUrl), cancellationToken);
        logger.LogInformation("Binance crypto trade stream connected. StreamUrl={StreamUrl}", streamUrl);

        var buffer = new byte[Math.Max(1_024, options.ReceiveBufferBytes)];
        using var message = new MemoryStream();

        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    logger.LogWarning(
                        "Binance crypto trade stream closed by server. Status={Status} Description={Description}",
                        result.CloseStatus,
                        result.CloseStatusDescription);
                    return;
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                ProcessMessage(message.ToArray());
            }
        }
    }

    private void ProcessMessage(byte[] payload)
    {
        var fetchedAtUtc = DateTimeOffset.UtcNow;
        if (!BinanceCryptoTradeParser.TryParse(payload, fetchedAtUtc, out var point, out var error) ||
            point is null)
        {
            logger.LogWarning("Binance crypto trade message skipped. Reason={Reason}", error);
            return;
        }

        if (!enabledAssetSymbols.Contains(point.AssetSymbol))
        {
            return;
        }

        lock (sync)
        {
            latestByAsset[point.AssetSymbol] = point;
            if (!nextSampleAtUtcByAsset.TryGetValue(point.AssetSymbol, out var nextSampleAtUtc) ||
                fetchedAtUtc >= nextSampleAtUtc)
            {
                AddSample(point);
                nextSampleAtUtcByAsset[point.AssetSymbol] = fetchedAtUtc.Add(sampleInterval);
                var snapshot = CreateSnapshot(samplesByAsset[point.AssetSymbol]);
                logger.LogInformation(
                    "Binance {AssetSymbol}/USDT reference price sampled. PriceUsd={PriceUsd} SourceUpdatedAtUtc={SourceUpdatedAtUtc} Samples={Samples} WindowSize={WindowSize} ArithmeticMeanUsd={ArithmeticMeanUsd}",
                    point.AssetSymbol,
                    point.PriceUsd,
                    point.SourceUpdatedAtUtc,
                    snapshot.SampleCount,
                    snapshot.WindowSize,
                    snapshot.ArithmeticMeanUsd);
            }
        }
    }

    private void AddSample(CryptoReferencePricePoint point)
    {
        if (!samplesByAsset.TryGetValue(point.AssetSymbol, out var samples))
        {
            samples = [];
            samplesByAsset[point.AssetSymbol] = samples;
        }

        samples.Add(new BtcUsdReferencePricePoint(
            point.PriceUsd,
            point.SourceUpdatedAtUtc,
            point.FetchedAtUtc,
            point.Source));
        var extra = samples.Count - windowSize;
        if (extra > 0)
        {
            samples.RemoveRange(0, extra);
        }
    }

    private BtcUsdReferencePriceSnapshot CreateSnapshot(IReadOnlyList<BtcUsdReferencePricePoint> samples)
    {
        var orderedSamples = samples
            .OrderByDescending(sample => sample.FetchedAtUtc)
            .ToArray();
        var latest = orderedSamples.FirstOrDefault();
        var mean = orderedSamples.Length == 0
            ? (decimal?)null
            : orderedSamples.Sum(sample => sample.PriceUsd) / orderedSamples.Length;

        return new BtcUsdReferencePriceSnapshot(
            BinanceCryptoTradeParser.SourceName,
            windowSize,
            orderedSamples.Length,
            orderedSamples.Length >= windowSize,
            mean,
            latest,
            orderedSamples,
            DateTimeOffset.UtcNow);
    }

    private string BuildStreamUrl()
    {
        var streams = enabledAssetSymbols
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .Select(symbol => symbol.ToLowerInvariant() + "usdt@trade");
        var separator = options.CombinedStreamBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return options.CombinedStreamBaseUrl.TrimEnd('/') + separator + "streams=" + string.Join("/", streams);
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "BinanceCryptoReferenceTradeStreamService", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Binance crypto reference price error.");
        }
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
        return symbol.Trim().ToUpperInvariant();
    }
}
