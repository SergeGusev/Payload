using System.Net.WebSockets;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Diagnostics;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed class BinanceBtcUsdTradeStreamService(
    ILogger<BinanceBtcUsdTradeStreamService> logger,
    BinanceBtcUsdReferenceOptions options,
    IBtcUsdReferencePriceCache cache,
    IBtcOrderBookLagDiagnosticService btcOrderBookLagDiagnosticService,
    IAppRepository repository) : BackgroundService, IBtcUsdReferencePriceClient
{
    private readonly object sync = new();
    private readonly TimeProvider businessClock = TimeProvider.System;
    private BtcUsdReferencePricePoint? latest;
    private WebSocket? activeSocket;
    private CancellationToken connectionCancellationToken;
    private long connectionGeneration;
    private long latestGeneration;
    private DateTimeOffset nextSampleAtUtc = DateTimeOffset.MinValue;

    internal BinanceBtcUsdTradeStreamService(
        ILogger<BinanceBtcUsdTradeStreamService> logger,
        BinanceBtcUsdReferenceOptions options,
        IBtcUsdReferencePriceCache cache,
        IBtcOrderBookLagDiagnosticService btcOrderBookLagDiagnosticService,
        IAppRepository repository,
        TimeProvider businessClock)
        : this(logger, options, cache, btcOrderBookLagDiagnosticService, repository)
    {
        this.businessClock = businessClock;
    }

    public Task<BtcUsdReferencePricePoint> GetBtcUsdPriceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            var snapshot = latest;
            if (snapshot is null)
            {
                throw new InvalidOperationException("Binance BTC/USDT trade stream has not received a price yet.");
            }

            if (!IsConnectionAvailableUnderLock(latestGeneration))
            {
                throw new InvalidOperationException(
                    "Binance BTC/USDT trade stream price is unavailable. An open connection and a BTC trade from that connection are required.");
            }

            return Task.FromResult(snapshot);
        }
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
            "Binance BTC/USDT trade stream reference service started. StreamUrl={StreamUrl} SampleIntervalSeconds={SampleIntervalSeconds} WindowSize={WindowSize} PriceAgeExpiryEnabled=false KeepAliveIntervalSeconds=10 KeepAliveTimeoutSeconds=10",
            options.StreamUrl,
            options.SampleIntervalSeconds,
            options.WindowSize);

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
        using var socket = CreateSocket();
        var generation = BeginConnection(socket, cancellationToken);
        try
        {
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
                        EndConnection(generation);
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
                    ProcessMessage(message.ToArray(), generation);
                }
            }
        }
        finally
        {
            EndConnection(generation);
        }
    }

    internal static ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(10);
        return socket;
    }

    internal long BeginConnection(WebSocket socket, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            activeSocket = socket;
            connectionCancellationToken = cancellationToken;
            return ++connectionGeneration;
        }
    }

    internal void EndConnection(long generation)
    {
        lock (sync)
        {
            if (generation == connectionGeneration)
            {
                activeSocket = null;
            }
        }
    }

    private bool IsConnectionAvailableUnderLock(long generation)
    {
        return generation == connectionGeneration &&
               activeSocket?.State == WebSocketState.Open &&
               !connectionCancellationToken.IsCancellationRequested;
    }

    internal void ProcessMessage(byte[] payload, long generation)
    {
        var fetchedAtUtc = businessClock.GetUtcNow();
        if (!BinanceBtcUsdTradeParser.TryParse(payload, fetchedAtUtc, out var point, out var error) ||
            point is null)
        {
            logger.LogWarning("Binance BTC/USDT trade message skipped. Reason={Reason}", error);
            return;
        }

        lock (sync)
        {
            if (!IsConnectionAvailableUnderLock(generation))
            {
                return;
            }

            latest = point;
            latestGeneration = generation;
        }

        btcOrderBookLagDiagnosticService.RecordBinanceTrade(point);

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
