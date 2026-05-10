using System.Net.WebSockets;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed class BinanceBtcUsdTradeStreamService(
    ILogger<BinanceBtcUsdTradeStreamService> logger,
    BinanceBtcUsdReferenceOptions options,
    IBtcUsdReferencePriceCache cache,
    IAppRepository repository) : BackgroundService, IBtcUsdReferencePriceClient
{
    private readonly object sync = new();
    private BtcUsdReferencePricePoint? latest;
    private DateTimeOffset nextSampleAtUtc = DateTimeOffset.MinValue;

    public Task<BtcUsdReferencePricePoint> GetBtcUsdPriceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BtcUsdReferencePricePoint? snapshot;
        lock (sync)
        {
            snapshot = latest;
        }

        if (snapshot is null)
        {
            throw new InvalidOperationException("Binance BTC/USDT trade stream has not received a price yet.");
        }

        var age = DateTimeOffset.UtcNow - snapshot.FetchedAtUtc;
        if (age > TimeSpan.FromSeconds(options.StaleAfterSeconds))
        {
            throw new InvalidOperationException(
                $"Binance BTC/USDT trade stream price is stale. AgeSeconds={age.TotalSeconds:0.###}; StaleAfterSeconds={options.StaleAfterSeconds}.");
        }

        return Task.FromResult(snapshot);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Binance BTC/USDT trade stream reference service is disabled.");
            return;
        }

        var reconnectDelay = TimeSpan.FromSeconds(options.ReconnectBaseDelaySeconds);
        var maxReconnectDelay = TimeSpan.FromSeconds(options.ReconnectMaxDelaySeconds);
        logger.LogInformation(
            "Binance BTC/USDT trade stream reference service started. StreamUrl={StreamUrl} SampleIntervalSeconds={SampleIntervalSeconds} WindowSize={WindowSize} StaleAfterSeconds={StaleAfterSeconds}",
            options.StreamUrl,
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
                logger.LogError(ex, "Binance BTC/USDT trade stream failed.");
                await TryRecordApiErrorAsync("StreamBtcUsdtTrades", ex.Message, stoppingToken);
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(reconnectDelay, stoppingToken);
                reconnectDelay = TimeSpan.FromSeconds(Math.Min(
                    maxReconnectDelay.TotalSeconds,
                    reconnectDelay.TotalSeconds * 2));
            }
        }

        logger.LogInformation("Binance BTC/USDT trade stream reference service stopped.");
    }

    private async Task RunSocketAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(options.StreamUrl), cancellationToken);
        logger.LogInformation("Binance BTC/USDT trade stream connected.");

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
                        "Binance BTC/USDT trade stream closed by server. Status={Status} Description={Description}",
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
        if (!BinanceBtcUsdTradeParser.TryParse(payload, fetchedAtUtc, out var point, out var error) ||
            point is null)
        {
            logger.LogWarning("Binance BTC/USDT trade message skipped. Reason={Reason}", error);
            return;
        }

        lock (sync)
        {
            latest = point;
        }

        if (fetchedAtUtc < nextSampleAtUtc)
        {
            return;
        }

        cache.Add(point);
        nextSampleAtUtc = fetchedAtUtc.AddSeconds(options.SampleIntervalSeconds);
        var snapshot = cache.Snapshot;
        logger.LogInformation(
            "Binance BTC/USDT reference price sampled. PriceUsd={PriceUsd} SourceUpdatedAtUtc={SourceUpdatedAtUtc} Samples={Samples} WindowSize={WindowSize} ArithmeticMeanUsd={ArithmeticMeanUsd}",
            point.PriceUsd,
            point.SourceUpdatedAtUtc,
            snapshot.SampleCount,
            snapshot.WindowSize,
            snapshot.ArithmeticMeanUsd);
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "BinanceBtcUsdTradeStreamService", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Binance BTC/USDT reference price error.");
        }
    }
}
