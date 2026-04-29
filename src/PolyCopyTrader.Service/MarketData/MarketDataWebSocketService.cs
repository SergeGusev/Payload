using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.MarketData;

public sealed class MarketDataWebSocketService(
    ILogger<MarketDataWebSocketService> logger,
    BotOptions botOptions,
    MarketDataWebSocketOptions options,
    IRelevantMarketAssetProvider assetProvider,
    IMarketDataCache marketDataCache,
    IPaperTradingMarketDataUpdater paperTradingUpdater,
    IAppRepository repository) : BackgroundService
{
    private const string ComponentName = "PolymarketMarketWebSocket";
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private DateTimeOffset? lastMessageUtc;
    private DateTimeOffset? lastConnectedUtc;
    private DateTimeOffset? lastDisconnectedUtc;
    private int reconnectCount;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!botOptions.UseWebSockets || !options.Enabled)
        {
            await PublishStatusAsync(MarketDataConnectionState.Disabled, 0, "Market WebSocket is disabled.", stoppingToken);
            logger.LogInformation("Market WebSocket service is disabled.");
            return;
        }

        var reconnectDelay = TimeSpan.FromSeconds(options.ReconnectBaseDelaySeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var assetIds = await assetProvider.GetRelevantAssetIdsAsync(stoppingToken);
            if (assetIds.Count == 0)
            {
                marketDataCache.ReplaceSubscribedAssets([]);
                await PublishStatusAsync(MarketDataConnectionState.Idle, 0, null, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(options.SubscriptionRefreshSeconds), stoppingToken);
                continue;
            }

            try
            {
                await RunConnectionAsync(assetIds, stoppingToken);
                reconnectDelay = TimeSpan.FromSeconds(options.ReconnectBaseDelaySeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                reconnectCount++;
                lastDisconnectedUtc = DateTimeOffset.UtcNow;
                logger.LogWarning(ex, "Market WebSocket connection failed. Reconnecting in {ReconnectDelaySeconds} seconds.", reconnectDelay.TotalSeconds);
                await TryRecordApiErrorAsync("ConnectionLoop", ex.Message, stoppingToken);
                await PublishStatusAsync(MarketDataConnectionState.Reconnecting, marketDataCache.SubscribedAssetIds.Count, ex.Message, stoppingToken);
                await Task.Delay(reconnectDelay, stoppingToken);
                reconnectDelay = TimeSpan.FromSeconds(Math.Min(
                    reconnectDelay.TotalSeconds * 2,
                    options.ReconnectMaxDelaySeconds));
            }
        }
    }

    private async Task RunConnectionAsync(IReadOnlyCollection<string> initialAssetIds, CancellationToken cancellationToken)
    {
        await PublishStatusAsync(MarketDataConnectionState.Connecting, initialAssetIds.Count, null, cancellationToken);

        using var socket = new ClientWebSocket();
        using var sendLock = new SemaphoreSlim(1, 1);
        await socket.ConnectAsync(new Uri(options.MarketEndpointUrl), cancellationToken);

        lastConnectedUtc = DateTimeOffset.UtcNow;
        var subscribedAssetIds = initialAssetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await SendSubscriptionAsync(socket, subscribedAssetIds, sendLock, cancellationToken);
        marketDataCache.ReplaceSubscribedAssets(subscribedAssetIds);
        await PublishStatusAsync(MarketDataConnectionState.Connected, subscribedAssetIds.Count, null, cancellationToken);

        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = HeartbeatLoopAsync(socket, sendLock, connectionCts.Token);
        var refresh = SubscriptionRefreshLoopAsync(socket, subscribedAssetIds, sendLock, connectionCts.Token);

        try
        {
            await ReceiveLoopAsync(socket, connectionCts.Token);
        }
        finally
        {
            await connectionCts.CancelAsync();
            await SafeAwaitAsync(heartbeat);
            await SafeAwaitAsync(refresh);
            lastDisconnectedUtc = DateTimeOffset.UtcNow;
            await PublishStatusAsync(MarketDataConnectionState.Disconnected, subscribedAssetIds.Count, null, cancellationToken);
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var message = await ReceiveTextMessageAsync(socket, cancellationToken);
            if (message is null)
            {
                break;
            }

            await ProcessTextMessageAsync(message, cancellationToken);
        }
    }

    private async Task<string?> ReceiveTextMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[options.ReceiveBufferBytes];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(message.ToArray());
    }

    private async Task ProcessTextMessageAsync(string message, CancellationToken cancellationToken)
    {
        IReadOnlyList<MarketDataUpdate> updates;
        try
        {
            updates = PolymarketMarketDataWebSocketParser.ParseMarketMessage(message);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Market WebSocket message parsing failed.");
            await TryRecordApiErrorAsync("ParseMarketMessage", ex.Message, cancellationToken);
            return;
        }

        if (updates.Count == 0)
        {
            return;
        }

        lastMessageUtc = DateTimeOffset.UtcNow;
        foreach (var update in updates)
        {
            try
            {
                marketDataCache.ApplyUpdate(update);
                if (update.OrderBookSnapshot is not null)
                {
                    await repository.AddOrderBookSnapshotAsync(update.OrderBookSnapshot, cancellationToken);
                }

                await repository.AddMarketDataEventAsync(ToMarketDataEvent(update), cancellationToken);
                await paperTradingUpdater.ApplyUpdateAsync(update, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist or dispatch market data update {EventType}.", update.EventType);
                await TryRecordApiErrorAsync("ProcessMarketDataUpdate", ex.Message, cancellationToken);
            }
        }

        await PublishStatusAsync(MarketDataConnectionState.Connected, marketDataCache.SubscribedAssetIds.Count, null, cancellationToken);
    }

    private async Task HeartbeatLoopAsync(ClientWebSocket socket, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            await Task.Delay(TimeSpan.FromSeconds(options.HeartbeatSeconds), cancellationToken);
            await SendTextAsync(socket, "PING", sendLock, cancellationToken);
            await PublishStatusAsync(MarketDataConnectionState.Connected, marketDataCache.SubscribedAssetIds.Count, null, cancellationToken);
        }
    }

    private async Task SubscriptionRefreshLoopAsync(
        ClientWebSocket socket,
        HashSet<string> subscribedAssetIds,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            await Task.Delay(TimeSpan.FromSeconds(options.SubscriptionRefreshSeconds), cancellationToken);
            var desiredAssetIds = (await assetProvider.GetRelevantAssetIdsAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var toSubscribe = desiredAssetIds.Except(subscribedAssetIds, StringComparer.OrdinalIgnoreCase).ToArray();
            var toUnsubscribe = subscribedAssetIds.Except(desiredAssetIds, StringComparer.OrdinalIgnoreCase).ToArray();

            if (toSubscribe.Length > 0)
            {
                await SendSubscriptionUpdateAsync(socket, "subscribe", toSubscribe, sendLock, cancellationToken);
                foreach (var assetId in toSubscribe)
                {
                    subscribedAssetIds.Add(assetId);
                }
            }

            if (toUnsubscribe.Length > 0)
            {
                await SendSubscriptionUpdateAsync(socket, "unsubscribe", toUnsubscribe, sendLock, cancellationToken);
                foreach (var assetId in toUnsubscribe)
                {
                    subscribedAssetIds.Remove(assetId);
                }
            }

            marketDataCache.ReplaceSubscribedAssets(subscribedAssetIds);
            await PublishStatusAsync(
                subscribedAssetIds.Count == 0 ? MarketDataConnectionState.Idle : MarketDataConnectionState.Connected,
                subscribedAssetIds.Count,
                null,
                cancellationToken);

            if (subscribedAssetIds.Count == 0)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "No relevant assets", cancellationToken);
                return;
            }
        }
    }

    private async Task SendSubscriptionAsync(
        ClientWebSocket socket,
        IReadOnlyCollection<string> assetIds,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["assets_ids"] = assetIds,
            ["type"] = "market",
            ["custom_feature_enabled"] = true
        };

        await SendJsonAsync(socket, payload, sendLock, cancellationToken);
    }

    private async Task SendSubscriptionUpdateAsync(
        ClientWebSocket socket,
        string operation,
        IReadOnlyCollection<string> assetIds,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["assets_ids"] = assetIds,
            ["operation"] = operation
        };

        if (operation == "subscribe")
        {
            payload["custom_feature_enabled"] = true;
        }

        await SendJsonAsync(socket, payload, sendLock, cancellationToken);
    }

    private async Task SendJsonAsync(ClientWebSocket socket, object payload, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, jsonOptions);
        await SendTextAsync(socket, json, sendLock, cancellationToken);
    }

    private static async Task SendTextAsync(ClientWebSocket socket, string message, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task PublishStatusAsync(
        MarketDataConnectionState requestedState,
        int subscribedAssetsCount,
        string? lastError,
        CancellationToken cancellationToken)
    {
        var stale = lastMessageUtc is { } lastMessage &&
            DateTimeOffset.UtcNow - lastMessage > TimeSpan.FromSeconds(options.StaleAfterSeconds);
        var state = requestedState == MarketDataConnectionState.Connected && stale
            ? MarketDataConnectionState.Stale
            : requestedState;

        var status = new MarketDataStatusSnapshot(
            ComponentName,
            state,
            options.MarketEndpointUrl,
            subscribedAssetsCount,
            lastMessageUtc,
            lastConnectedUtc,
            lastDisconnectedUtc,
            reconnectCount,
            stale,
            lastError,
            DateTimeOffset.UtcNow);

        marketDataCache.UpdateStatus(status);

        try
        {
            await repository.UpsertMarketDataStatusAsync(status, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist market data WebSocket status.");
        }
    }

    private async Task TryRecordApiErrorAsync(string operation, string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), ComponentName, operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist market WebSocket API error.");
        }
    }

    private static MarketDataEvent ToMarketDataEvent(MarketDataUpdate update)
    {
        var message = update.EventType switch
        {
            MarketDataEventType.Book => "Order book snapshot received.",
            MarketDataEventType.PriceChange => "Price change received.",
            MarketDataEventType.LastTradePrice => "Last trade price received.",
            MarketDataEventType.BestBidAsk => "Best bid/ask received.",
            MarketDataEventType.MarketResolved => "Market resolved event received.",
            MarketDataEventType.TickSizeChange => "Tick size change received.",
            _ => $"Market data event received: {update.RawEventType}."
        };

        return new MarketDataEvent(
            Guid.NewGuid(),
            update.EventType,
            update.AssetId,
            update.ConditionId,
            message,
            DateTimeOffset.UtcNow);
    }

    private static async Task SafeAwaitAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }
}
