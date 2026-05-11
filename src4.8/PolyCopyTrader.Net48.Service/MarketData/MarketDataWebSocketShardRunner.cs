using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.MarketData;

public sealed class MarketDataWebSocketShardRunner(
    ILogger<MarketDataWebSocketShardRunner> logger,
    MarketDataWebSocketShardPlan plan,
    MarketDataWebSocketOptions options,
    PolymarketOptions polymarketOptions,
    IAppRepository repository,
    Func<string, string, DateTimeOffset, CancellationToken, Task> processTextMessageAsync,
    Action<MarketDataStatusSnapshot> onStatus)
{
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object stateGate = new();
    private readonly object assetGate = new();
    private HashSet<string> assetIds = plan.AssetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? runCts;
    private Task? runTask;
    private ClientWebSocket? currentSocket;
    private SemaphoreSlim? currentSendLock;
    private DateTimeOffset? lastMessageUtc;
    private DateTimeOffset? lastConnectedUtc;
    private DateTimeOffset? lastDisconnectedUtc;
    private DateTimeOffset? lastStatusPersistedUtc;
    private MarketDataConnectionState? lastPersistedState;
    private int? lastPersistedSubscribedAssetsCount;
    private string? lastPersistedError;
    private int reconnectCount;

    public string Component => plan.Component;

    public IReadOnlyList<string> AssetIds
    {
        get
        {
            lock (assetGate)
            {
                return assetIds
                    .OrderBy(assetId => assetId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public void Start(CancellationToken stoppingToken)
    {
        if (runTask is not null)
        {
            throw new InvalidOperationException($"Market WebSocket shard {Component} has already been started.");
        }

        runCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        runTask = Task.Run(() => ExecuteAsync(runCts.Token), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        if (runCts is null || runTask is null)
        {
            return;
        }

        await runCts.CancelAsync();
        GetCurrentSocket()?.Abort();
        await SafeAwaitAsync(runTask);
        runCts.Dispose();
        runCts = null;
        runTask = null;
    }

    public async Task UpdateAssetsAsync(IReadOnlyCollection<string> nextAssetIds, CancellationToken cancellationToken)
    {
        var next = nextAssetIds
            .Where(assetId => !string.IsNullOrWhiteSpace(assetId))
            .Select(assetId => assetId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] toSubscribe;
        string[] toUnsubscribe;
        lock (assetGate)
        {
            toSubscribe = next.Except(assetIds, StringComparer.OrdinalIgnoreCase).ToArray();
            toUnsubscribe = assetIds.Except(next, StringComparer.OrdinalIgnoreCase).ToArray();
            if (toSubscribe.Length == 0 && toUnsubscribe.Length == 0)
            {
                return;
            }

            assetIds = next;
        }

        var connection = GetCurrentConnection();
        if (connection.Socket?.State != WebSocketState.Open || connection.SendLock is null)
        {
            return;
        }

        try
        {
            foreach (var batch in ChunkAssetIds(toSubscribe))
            {
                await SendSubscriptionUpdateAsync(connection.Socket, "subscribe", batch, connection.SendLock, cancellationToken);
            }

            foreach (var batch in ChunkAssetIds(toUnsubscribe))
            {
                await SendSubscriptionUpdateAsync(connection.Socket, "unsubscribe", batch, connection.SendLock, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Market WebSocket shard {Component} dynamic subscription update failed; reconnecting shard.", Component);
            await TryRecordApiErrorAsync("SubscriptionUpdate", ex.Message, CancellationToken.None);
            connection.Socket.Abort();
        }
    }

    public bool ShouldRestart(DateTimeOffset now)
    {
        if (runTask is null || runTask.IsCompleted)
        {
            return true;
        }

        if (options.WatchdogStaleSeconds <= 0)
        {
            return false;
        }

        lock (stateGate)
        {
            if (currentSocket?.State != WebSocketState.Open)
            {
                return false;
            }

            var referenceUtc = lastMessageUtc ?? lastConnectedUtc;
            return referenceUtc is { } reference &&
                now - reference > TimeSpan.FromSeconds(options.WatchdogStaleSeconds);
        }
    }

    private async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var reconnectDelay = TimeSpan.FromSeconds(options.ReconnectBaseDelaySeconds);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                reconnectCount++;
                await PublishStatusAsync(MarketDataConnectionState.Reconnecting, "Connection closed.", cancellationToken);
                await Task.Delay(reconnectDelay, cancellationToken);
                reconnectDelay = NextReconnectDelay(reconnectDelay);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                reconnectCount++;
                SetDisconnectedUtc(DateTimeOffset.UtcNow);
                logger.LogWarning(
                    ex,
                    "Market WebSocket shard {Component} failed. Reconnecting in {ReconnectDelaySeconds} seconds.",
                    Component,
                    reconnectDelay.TotalSeconds);
                await TryRecordApiErrorAsync("ConnectionLoop", ex.Message, cancellationToken);
                await PublishStatusAsync(MarketDataConnectionState.Reconnecting, ex.Message, cancellationToken);
                await Task.Delay(reconnectDelay, cancellationToken);
                reconnectDelay = NextReconnectDelay(reconnectDelay);
            }
        }
    }

    private async Task RunConnectionAsync(CancellationToken cancellationToken)
    {
        await PublishStatusAsync(MarketDataConnectionState.Connecting, null, cancellationToken);

        using var socket = new ClientWebSocket();
        using var sendLock = new SemaphoreSlim(1, 1);
        SetCurrentConnection(socket, sendLock);
        var endpointUri = new Uri(options.MarketEndpointUrl);
        ConfigurePinnedCertificateValidation(socket, endpointUri);

        try
        {
            await socket.ConnectAsync(endpointUri, cancellationToken);
            SetConnectedUtc(DateTimeOffset.UtcNow);
            await SendInitialSubscriptionsAsync(socket, sendLock, cancellationToken);
            await PublishStatusAsync(MarketDataConnectionState.Connected, null, cancellationToken);

            using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var heartbeat = HeartbeatLoopAsync(socket, sendLock, connectionCts);

            try
            {
                await ReceiveLoopAsync(socket, connectionCts.Token);
            }
            finally
            {
                await connectionCts.CancelAsync();
                await SafeAwaitAsync(heartbeat);
            }
        }
        finally
        {
            SetCurrentConnection(null, null);
            SetDisconnectedUtc(DateTimeOffset.UtcNow);
            await PublishStatusAsync(MarketDataConnectionState.Disconnected, null, cancellationToken);
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

            var receivedAtUtc = DateTimeOffset.UtcNow;
            SetLastMessageUtc(receivedAtUtc);
            await processTextMessageAsync(Component, message, receivedAtUtc, cancellationToken);
            await PublishStatusAsync(MarketDataConnectionState.Connected, null, cancellationToken);
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

    private async Task HeartbeatLoopAsync(
        ClientWebSocket socket,
        SemaphoreSlim sendLock,
        CancellationTokenSource connectionCts)
    {
        while (!connectionCts.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            await Task.Delay(TimeSpan.FromSeconds(options.HeartbeatSeconds), connectionCts.Token);
            try
            {
                await SendTextAsync(socket, "PING", sendLock, connectionCts.Token);
                await PublishStatusAsync(MarketDataConnectionState.Connected, null, connectionCts.Token);
            }
            catch (OperationCanceledException) when (connectionCts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Market WebSocket shard {Component} heartbeat failed.", Component);
                await TryRecordApiErrorAsync("Heartbeat", ex.Message, CancellationToken.None);
                socket.Abort();
                await connectionCts.CancelAsync();
                break;
            }
        }
    }

    private async Task SendInitialSubscriptionsAsync(
        ClientWebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        var firstBatch = true;
        foreach (var batch in ChunkAssetIds(AssetIds))
        {
            if (firstBatch)
            {
                await SendSubscriptionAsync(socket, batch, sendLock, cancellationToken);
                firstBatch = false;
                continue;
            }

            await SendSubscriptionUpdateAsync(socket, "subscribe", batch, sendLock, cancellationToken);
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

    private IEnumerable<IReadOnlyCollection<string>> ChunkAssetIds(IReadOnlyCollection<string> assetIds)
    {
        foreach (var batch in assetIds.Chunk(options.SubscriptionBatchSize))
        {
            yield return batch;
        }
    }

    private async Task SendJsonAsync(ClientWebSocket socket, object payload, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, jsonOptions);
        await SendTextAsync(socket, json, sendLock, cancellationToken);
    }

    private static async Task SendTextAsync(
        ClientWebSocket socket,
        string message,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
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
        string? lastError,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var stateSnapshot = GetStateSnapshot();
        var stale = requestedState == MarketDataConnectionState.Connected &&
            IsProtocolStale(stateSnapshot.LastMessageUtc, stateSnapshot.LastConnectedUtc, now, options.StaleAfterSeconds);
        var state = stale ? MarketDataConnectionState.Stale : requestedState;

        var status = new MarketDataStatusSnapshot(
            Component,
            state,
            options.MarketEndpointUrl,
            AssetIds.Count,
            stateSnapshot.LastMessageUtc,
            stateSnapshot.LastConnectedUtc,
            stateSnapshot.LastDisconnectedUtc,
            reconnectCount,
            stale,
            lastError,
            now);

        onStatus(status);

        if (!ShouldPersistStatus(state, AssetIds.Count, lastError))
        {
            return;
        }

        try
        {
            await repository.UpsertMarketDataStatusAsync(status, cancellationToken);
            lastStatusPersistedUtc = DateTimeOffset.UtcNow;
            lastPersistedState = state;
            lastPersistedSubscribedAssetsCount = AssetIds.Count;
            lastPersistedError = lastError;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist market WebSocket shard {Component} status.", Component);
        }
    }

    private bool ShouldPersistStatus(
        MarketDataConnectionState state,
        int subscribedAssetsCount,
        string? lastError)
    {
        if (lastStatusPersistedUtc is null ||
            lastPersistedState != state ||
            lastPersistedSubscribedAssetsCount != subscribedAssetsCount ||
            !string.Equals(lastPersistedError, lastError, StringComparison.Ordinal))
        {
            return true;
        }

        return DateTimeOffset.UtcNow - lastStatusPersistedUtc.Value >=
            TimeSpan.FromSeconds(options.StatusPersistIntervalSeconds);
    }

    private async Task TryRecordApiErrorAsync(string operation, string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), Component, operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist market WebSocket shard {Component} API error.", Component);
        }
    }

    private void ConfigurePinnedCertificateValidation(ClientWebSocket socket, Uri endpointUri)
    {
        if (!PolymarketCertificatePinning.HasPins(polymarketOptions))
        {
            return;
        }

        logger.LogWarning(
            "Market WebSocket shard {Component} cannot apply certificate pinning on .NET Framework 4.8 ClientWebSocket for {Host}.",
            Component,
            endpointUri.Host);
    }

    private TimeSpan NextReconnectDelay(TimeSpan current)
    {
        return TimeSpan.FromSeconds(Math.Min(
            current.TotalSeconds * 2,
            options.ReconnectMaxDelaySeconds));
    }

    private static bool IsProtocolStale(
        DateTimeOffset? lastMessage,
        DateTimeOffset? lastConnected,
        DateTimeOffset now,
        int staleAfterSeconds)
    {
        var reference = lastMessage ?? lastConnected;
        return reference is { } referenceUtc &&
            now - referenceUtc > TimeSpan.FromSeconds(staleAfterSeconds);
    }

    private ClientWebSocket? GetCurrentSocket()
    {
        lock (stateGate)
        {
            return currentSocket;
        }
    }

    private (ClientWebSocket? Socket, SemaphoreSlim? SendLock) GetCurrentConnection()
    {
        lock (stateGate)
        {
            return (currentSocket, currentSendLock);
        }
    }

    private void SetCurrentConnection(ClientWebSocket? socket, SemaphoreSlim? sendLock)
    {
        lock (stateGate)
        {
            currentSocket = socket;
            currentSendLock = sendLock;
        }
    }

    private void SetConnectedUtc(DateTimeOffset connectedUtc)
    {
        lock (stateGate)
        {
            lastConnectedUtc = connectedUtc;
            lastMessageUtc = null;
        }
    }

    private void SetDisconnectedUtc(DateTimeOffset disconnectedUtc)
    {
        lock (stateGate)
        {
            lastDisconnectedUtc = disconnectedUtc;
        }
    }

    private void SetLastMessageUtc(DateTimeOffset messageUtc)
    {
        lock (stateGate)
        {
            lastMessageUtc = messageUtc;
        }
    }

    private StateSnapshot GetStateSnapshot()
    {
        lock (stateGate)
        {
            return new StateSnapshot(lastMessageUtc, lastConnectedUtc, lastDisconnectedUtc);
        }
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

    private sealed record StateSnapshot(
        DateTimeOffset? LastMessageUtc,
        DateTimeOffset? LastConnectedUtc,
        DateTimeOffset? LastDisconnectedUtc);
}
