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
            "Binance crypto trade stream reference service started. Assets={Assets} StreamUrl={StreamUrl} StaleAfterSeconds={StaleAfterSeconds}",
            string.Join(",", enabledAssetSymbols.OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)),
            BuildStreamUrl(),
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
        }
    }

    private string BuildStreamUrl()
    {
        var streams = enabledAssetSymbols
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .Select(symbol => symbol.ToLowerInvariant() + "usdt@trade");
        var separator = options.CombinedStreamBaseUrl.IndexOf("?", StringComparison.Ordinal) >= 0 ? "&" : "?";
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
