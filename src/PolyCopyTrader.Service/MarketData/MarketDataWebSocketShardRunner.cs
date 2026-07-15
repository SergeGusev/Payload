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
    Func<string, string, DateTimeOffset, CancellationToken, Task<bool>> processTextMessageAsync,
    Action<MarketDataStatusSnapshot> onStatus)
{
    private static readonly object DisconnectDiagnosticDataKey = new();
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
    private int connectionAttemptCount;

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
            var observedAtUtc = DateTimeOffset.UtcNow;
            var exceptionDiagnostic = BuildCurrentExceptionDiagnostic(
                "SubscriptionUpdate",
                connection.Socket,
                ex,
                observedAtUtc);
            logger.LogWarning(
                "Market WebSocket shard {Component} dynamic subscription update failed; reconnecting shard. " +
                "Diagnostic: {ExceptionDiagnostic}",
                Component,
                exceptionDiagnostic);
            await TryRecordApiErrorAsync("SubscriptionUpdate", exceptionDiagnostic, CancellationToken.None);
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
        var reconnectBackoff = new MarketDataWebSocketReconnectBackoff(
            TimeSpan.FromSeconds(options.ReconnectBaseDelaySeconds),
            TimeSpan.FromSeconds(options.ReconnectMaxDelaySeconds));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var disconnectDiagnostic = await RunConnectionAsync(reconnectBackoff, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                reconnectCount++;
                if (!string.IsNullOrWhiteSpace(disconnectDiagnostic))
                {
                    logger.LogWarning(
                        "Market WebSocket shard {Component} connection closed. {DisconnectDiagnostic}",
                        Component,
                        disconnectDiagnostic);
                }

                await PublishStatusAsync(
                    MarketDataConnectionState.Reconnecting,
                    disconnectDiagnostic ?? "Connection closed.",
                    cancellationToken);
                await reconnectBackoff.DelayAndAdvanceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var reconnectCountBeforeIncrement = reconnectCount;
                reconnectCount++;
                SetDisconnectedUtc(DateTimeOffset.UtcNow);
                var disconnectDiagnostic = TryGetDisconnectDiagnostic(ex) ??
                    BuildFallbackExceptionDiagnostic(
                        ex,
                        DateTimeOffset.UtcNow,
                        reconnectCountBeforeIncrement);
                logger.LogWarning(
                    "Market WebSocket shard {Component} failed. Reconnecting in {ReconnectDelaySeconds} seconds. " +
                    "Diagnostic: {DisconnectDiagnostic}",
                    Component,
                    reconnectBackoff.CurrentDelay.TotalSeconds,
                    disconnectDiagnostic);
                await TryRecordApiErrorAsync("ConnectionLoop", disconnectDiagnostic, cancellationToken);
                await PublishStatusAsync(MarketDataConnectionState.Reconnecting, disconnectDiagnostic, cancellationToken);
                await reconnectBackoff.DelayAndAdvanceAsync(cancellationToken);
            }
        }
    }

    private async Task<string?> RunConnectionAsync(
        MarketDataWebSocketReconnectBackoff reconnectBackoff,
        CancellationToken cancellationToken)
    {
        await PublishStatusAsync(MarketDataConnectionState.Connecting, null, cancellationToken);

        using var socket = new ClientWebSocket();
        using var sendLock = new SemaphoreSlim(1, 1);
        SetCurrentConnection(socket, sendLock);
        Uri? endpointUri = null;
        var connectionAttempt = 0;
        var phase = "EndpointValidation";
        DateTimeOffset? connectedAtUtc = null;
        DateTimeOffset? receiveLoopObservedAtUtc = null;
        DateTimeOffset? failureObservedAtUtc = null;

        try
        {
            endpointUri = new Uri(options.MarketEndpointUrl);
            connectionAttempt = Interlocked.Increment(ref connectionAttemptCount);
            phase = "TlsConfiguration";
            ConfigurePinnedCertificateValidation(socket, endpointUri);
            phase = "Connect";
            await socket.ConnectAsync(endpointUri, cancellationToken);
            connectedAtUtc = DateTimeOffset.UtcNow;
            SetConnectedUtc(connectedAtUtc.Value);
            phase = "InitialSubscription";
            await SendInitialSubscriptionsAsync(socket, sendLock, cancellationToken);
            await PublishStatusAsync(MarketDataConnectionState.Connected, null, cancellationToken);

            using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var heartbeat = HeartbeatLoopAsync(socket, sendLock, connectionCts);
            MarketWebSocketCloseFrame? closeFrame;

            try
            {
                phase = "ReceiveLoop";
                try
                {
                    closeFrame = await ReceiveLoopAsync(socket, reconnectBackoff, connectionCts.Token);
                    receiveLoopObservedAtUtc = closeFrame?.ObservedAtUtc ?? DateTimeOffset.UtcNow;
                }
                catch
                {
                    failureObservedAtUtc = DateTimeOffset.UtcNow;
                    throw;
                }
            }
            finally
            {
                await connectionCts.CancelAsync();
                await SafeAwaitAsync(heartbeat);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            return BuildDisconnectDiagnostic(
                closeFrame is null ? "ReceiveLoopEnded" : "CloseFrame",
                phase,
                connectionAttempt,
                Volatile.Read(ref reconnectCount),
                endpointUri,
                socket,
                connectedAtUtc,
                closeFrame,
                exception: null,
                observedAtUtc: receiveLoopObservedAtUtc ?? DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var observedAtUtc = failureObservedAtUtc ?? DateTimeOffset.UtcNow;
            var diagnostic = BuildDisconnectDiagnostic(
                "Exception",
                phase,
                connectionAttempt,
                Volatile.Read(ref reconnectCount),
                endpointUri,
                socket,
                connectedAtUtc,
                closeFrame: null,
                exception: ex,
                observedAtUtc: observedAtUtc);
            TrySetDisconnectDiagnostic(ex, diagnostic);
            throw;
        }
        finally
        {
            SetCurrentConnection(null, null);
            SetDisconnectedUtc(DateTimeOffset.UtcNow);
            await PublishStatusAsync(MarketDataConnectionState.Disconnected, null, cancellationToken);
        }
    }

    private async Task<MarketWebSocketCloseFrame?> ReceiveLoopAsync(
        ClientWebSocket socket,
        MarketDataWebSocketReconnectBackoff reconnectBackoff,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var received = await ReceiveTextMessageAsync(socket, cancellationToken);
            if (received.CloseFrame is not null)
            {
                return received.CloseFrame;
            }

            var message = received.Text ?? string.Empty;
            DateTimeOffset receivedAtUtc = DateTimeOffset.UtcNow;
            SetLastMessageUtc(receivedAtUtc);
            await ProcessTextMessageAndResetBackoffAsync(
                processTextMessageAsync,
                Component,
                message,
                receivedAtUtc,
                reconnectBackoff,
                cancellationToken);
            await PublishStatusAsync(MarketDataConnectionState.Connected, null, cancellationToken);
        }

        return null;
    }

    internal static async Task ProcessTextMessageAndResetBackoffAsync(
        Func<string, string, DateTimeOffset, CancellationToken, Task<bool>> processTextMessageAsync,
        string component,
        string message,
        DateTimeOffset receivedAtUtc,
        MarketDataWebSocketReconnectBackoff reconnectBackoff,
        CancellationToken cancellationToken)
    {
        if (await processTextMessageAsync(component, message, receivedAtUtc, cancellationToken))
        {
            reconnectBackoff.ResetAfterProcessedFrame();
        }
    }

    private async Task<MarketWebSocketReceivedMessage> ReceiveTextMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[options.ReceiveBufferBytes];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return new MarketWebSocketReceivedMessage(
                    null,
                    new MarketWebSocketCloseFrame(
                        result.CloseStatus,
                        result.CloseStatusDescription,
                        DateTimeOffset.UtcNow));
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return new MarketWebSocketReceivedMessage(Encoding.UTF8.GetString(message.ToArray()), null);
    }

    private string BuildDisconnectDiagnostic(
        string reason,
        string phase,
        int connectionAttempt,
        int reconnectCountBeforeIncrement,
        Uri? endpointUri,
        ClientWebSocket? socket,
        DateTimeOffset? connectedAtUtc,
        MarketWebSocketCloseFrame? closeFrame,
        Exception? exception,
        DateTimeOffset observedAtUtc)
    {
        var stateSnapshot = GetStateSnapshot();
        var context = new MarketWebSocketDisconnectContext(
            reason,
            Component,
            endpointUri,
            phase,
            connectionAttempt,
            reconnectCountBeforeIncrement,
            AssetIds.Count,
            socket?.State ?? WebSocketState.None,
            closeFrame?.Status ?? socket?.CloseStatus,
            closeFrame?.Description ?? socket?.CloseStatusDescription,
            connectedAtUtc,
            connectedAtUtc is null ? null : stateSnapshot.LastMessageUtc,
            observedAtUtc);
        return MarketWebSocketDisconnectDiagnosticBuilder.Build(context, exception);
    }

    private string BuildCurrentExceptionDiagnostic(
        string phase,
        ClientWebSocket? socket,
        Exception exception,
        DateTimeOffset observedAtUtc)
    {
        var stateSnapshot = GetStateSnapshot();
        return BuildDisconnectDiagnostic(
            "Exception",
            phase,
            Volatile.Read(ref connectionAttemptCount),
            Volatile.Read(ref reconnectCount),
            GetDiagnosticEndpointUri(),
            socket,
            socket is null ? null : stateSnapshot.LastConnectedUtc,
            closeFrame: null,
            exception: exception,
            observedAtUtc: observedAtUtc);
    }

    private string BuildFallbackExceptionDiagnostic(
        Exception exception,
        DateTimeOffset observedAtUtc,
        int reconnectCountBeforeIncrement)
    {
        return BuildDisconnectDiagnostic(
            "Exception",
            "ConnectionLoopFallback",
            Volatile.Read(ref connectionAttemptCount),
            reconnectCountBeforeIncrement,
            GetDiagnosticEndpointUri(),
            socket: null,
            connectedAtUtc: null,
            closeFrame: null,
            exception: exception,
            observedAtUtc: observedAtUtc);
    }

    private Uri? GetDiagnosticEndpointUri()
    {
        return Uri.TryCreate(options.MarketEndpointUrl, UriKind.Absolute, out var endpointUri)
            ? endpointUri
            : null;
    }

    private string GetDiagnosticEndpointHost()
    {
        return GetDiagnosticEndpointUri()?.Host ?? "<invalid>";
    }

    private static string? TryGetDisconnectDiagnostic(Exception exception)
    {
        try
        {
            return exception.Data[DisconnectDiagnosticDataKey] as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void TrySetDisconnectDiagnostic(Exception exception, string diagnostic)
    {
        try
        {
            exception.Data[DisconnectDiagnosticDataKey] = diagnostic;
        }
        catch (Exception)
        {
            // A safe fallback is rebuilt by the outer connection loop when Exception.Data is immutable.
        }
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
                var observedAtUtc = DateTimeOffset.UtcNow;
                var exceptionDiagnostic = BuildCurrentExceptionDiagnostic(
                    "Heartbeat",
                    socket,
                    ex,
                    observedAtUtc);
                logger.LogWarning(
                    "Market WebSocket shard {Component} heartbeat failed. Diagnostic: {ExceptionDiagnostic}",
                    Component,
                    exceptionDiagnostic);
                await TryRecordApiErrorAsync("Heartbeat", exceptionDiagnostic, CancellationToken.None);
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
            GetDiagnosticEndpointHost(),
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

        socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, sslPolicyErrors) =>
        {
            var result = PolymarketCertificatePinning.ValidateServerCertificate(
                endpointUri,
                certificate,
                sslPolicyErrors,
                polymarketOptions);

            if (!result.Accepted)
            {
                logger.LogWarning(
                    "Market WebSocket shard {Component} TLS certificate rejected for {Host}: {Message}",
                    Component,
                    endpointUri.Host,
                    result.Message);
            }

            return result.Accepted;
        };
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

    private sealed record MarketWebSocketReceivedMessage(
        string? Text,
        MarketWebSocketCloseFrame? CloseFrame);

    private sealed record MarketWebSocketCloseFrame(
        WebSocketCloseStatus? Status,
        string? Description,
        DateTimeOffset ObservedAtUtc);
}

internal sealed class MarketDataWebSocketReconnectBackoff
{
    private readonly TimeSpan baseDelay;
    private readonly TimeSpan maxDelay;

    public MarketDataWebSocketReconnectBackoff(TimeSpan baseDelay, TimeSpan maxDelay)
    {
        if (baseDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDelay), "Reconnect base delay must be positive.");
        }

        if (maxDelay < baseDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay), "Reconnect maximum delay must not be less than the base delay.");
        }

        this.baseDelay = baseDelay;
        this.maxDelay = maxDelay;
        CurrentDelay = baseDelay;
    }

    public TimeSpan CurrentDelay { get; private set; }

    public async Task DelayAndAdvanceAsync(CancellationToken cancellationToken)
    {
        await DelayAndAdvanceAsync(
            static (delay, token) => Task.Delay(delay, token),
            cancellationToken);
    }

    internal async Task DelayAndAdvanceAsync(
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        CancellationToken cancellationToken)
    {
        await delayAsync(CurrentDelay, cancellationToken);
        CurrentDelay = CurrentDelay.Ticks > maxDelay.Ticks / 2
            ? maxDelay
            : TimeSpan.FromTicks(CurrentDelay.Ticks * 2);
    }

    public void ResetAfterProcessedFrame()
    {
        CurrentDelay = baseDelay;
    }
}
