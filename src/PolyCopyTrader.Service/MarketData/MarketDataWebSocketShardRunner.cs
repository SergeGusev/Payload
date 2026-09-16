using System.Net.WebSockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.MarketData;

public sealed class MarketDataWebSocketShardRunner(
    ILogger<MarketDataWebSocketShardRunner> logger,
    MarketDataWebSocketShardPlan plan,
    MarketDataWebSocketOptions options,
    PolymarketOptions polymarketOptions,
    IAppRepository repository,
    Func<string, string, DateTimeOffset, CancellationToken, Task<bool>> processTextMessageAsync,
    Action<MarketDataStatusSnapshot> onStatus,
    IMakerGtdPaperPlacementHandoff? makerGtdPaperPlacementHandoff = null)
{
    private static readonly object DisconnectDiagnosticDataKey = new();
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object stateGate = new();
    private readonly IMakerGtdPaperPlacementHandoff makerGtdHandoff =
        makerGtdPaperPlacementHandoff ?? NoOpMakerGtdPaperPlacementHandoff.Instance;
    private readonly MarketDataWebSocketDesiredAssetSet desiredAssetSet = new(plan.AssetIds);
    private CancellationTokenSource? runCts;
    private Task? runTask;
    private MarketDataWebSocketConnectionGeneration? currentConnection;
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
        get => desiredAssetSet.Snapshot().AssetIds;
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
        if (!desiredAssetSet.Replace(next))
        {
            return;
        }

        var connection = GetCurrentConnection();
        if (connection?.Socket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            await connection.Subscriptions.ReconcileAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!connection.Subscriptions.IsActive || !IsCurrentConnection(connection))
        {
            return;
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
            if (currentConnection?.Socket.State != WebSocketState.Open)
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
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var subscriptions = new MarketDataWebSocketSubscriptionGeneration(
            desiredAssetSet,
            options.SubscriptionBatchSize,
            (batch, token) => SendSubscriptionAsync(socket, batch, sendLock, token),
            (operation, batch, token) => SendSubscriptionUpdateAsync(socket, operation, batch, sendLock, token),
            connectionCts.Token);
        var connection = new MarketDataWebSocketConnectionGeneration(socket, subscriptions);
        SetCurrentConnection(connection);
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
            await subscriptions.InitializeAsync(cancellationToken);

            var firstFrameReadiness = new MarketDataWebSocketFirstFrameReadiness();
            var receiveLoop = ReceiveLoopAsync(
                socket,
                reconnectBackoff,
                firstFrameReadiness.ObserveFrame,
                () => firstFrameReadiness.IsReady,
                connectionCts.Token);
            var heartbeat = Task.CompletedTask;
            MarketWebSocketCloseFrame? closeFrame = null;

            try
            {
                phase = "FirstFrameWait";
                try
                {
                    var readinessOutcome = await firstFrameReadiness.WaitAsync(
                        receiveLoop,
                        TimeSpan.FromSeconds(options.FirstFrameTimeoutSeconds),
                        socket.Abort,
                        cancellationToken);
                    if (readinessOutcome == MarketDataWebSocketFirstFrameWaitOutcome.TimedOut)
                    {
                        failureObservedAtUtc = DateTimeOffset.UtcNow;
                        throw new MarketDataWebSocketFirstFrameTimeoutException(options.FirstFrameTimeoutSeconds);
                    }

                    if (readinessOutcome == MarketDataWebSocketFirstFrameWaitOutcome.Ready)
                    {
                        await PublishStatusAsync(MarketDataConnectionState.Connected, null, cancellationToken);
                        heartbeat = HeartbeatLoopAsync(socket, sendLock, connectionCts);
                        phase = "ReceiveLoop";
                    }

                    closeFrame = await receiveLoop;
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
                await SafeAwaitAsync(receiveLoop);
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
            var firstFrameTimeout = ex as MarketDataWebSocketFirstFrameTimeoutException;
            var diagnostic = BuildDisconnectDiagnostic(
                firstFrameTimeout is null ? "Exception" : "FirstFrameTimeout",
                phase,
                connectionAttempt,
                Volatile.Read(ref reconnectCount),
                endpointUri,
                socket,
                connectedAtUtc,
                closeFrame: null,
                exception: ex,
                observedAtUtc: observedAtUtc);
            if (firstFrameTimeout is not null)
            {
                diagnostic =
                    $"{diagnostic}; FirstFrameTimeoutSeconds={firstFrameTimeout.TimeoutSeconds}; " +
                    $"FirstFrameSubscribedAssets={subscriptions.SentAssetCount}";
            }

            TrySetDisconnectDiagnostic(ex, diagnostic);
            throw;
        }
        finally
        {
            await connectionCts.CancelAsync();
            ClearCurrentConnection(connection);
            SetDisconnectedUtc(DateTimeOffset.UtcNow);
            await PublishStatusAsync(MarketDataConnectionState.Disconnected, null, cancellationToken);
        }
    }

    private async Task<MarketWebSocketCloseFrame?> ReceiveLoopAsync(
        ClientWebSocket socket,
        MarketDataWebSocketReconnectBackoff reconnectBackoff,
        Action<MarketWebSocketDispatchFrame> onFrameReceived,
        Func<bool> isFirstFrameReady,
        CancellationToken cancellationToken)
    {
        return await MarketDataWebSocketFrameHandoff.RunAsync(
            options.ReceiveDispatchQueueCapacity,
            TimeSpan.FromMilliseconds(options.SideEffectSlowProcessingMilliseconds),
            async token =>
            {
                if (socket.State != WebSocketState.Open)
                {
                    return new MarketWebSocketReceivedMessage(null, null);
                }

                return await ReceiveTextMessageAsync(socket, token);
            },
            async (frame, token) =>
            {
                await using (await makerGtdHandoff.EnterMarketDataReceiptAsync(token))
                {
                    await ProcessTextMessageAndResetBackoffAsync(
                        processTextMessageAsync,
                        Component,
                        frame.Text,
                        frame.ReceivedAtUtc,
                        reconnectBackoff,
                        token);
                }

                if (isFirstFrameReady())
                {
                    await PublishStatusAsync(MarketDataConnectionState.Connected, null, token);
                }
            },
            onFrameReceived,
            waitDuration => logger.LogWarning(
                "Market WebSocket shard {Component} receive dispatch channel was saturated. Capacity={ReceiveDispatchQueueCapacity} WaitDurationMilliseconds={WaitDurationMilliseconds}",
                Component,
                options.ReceiveDispatchQueueCapacity,
                waitDuration.TotalMilliseconds),
            socket.Abort,
            cancellationToken);
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

        var receivedAtUtc = DateTimeOffset.UtcNow;
        SetLastMessageUtc(receivedAtUtc);
        return new MarketWebSocketReceivedMessage(
            new MarketWebSocketDispatchFrame(
                Encoding.UTF8.GetString(message.ToArray()),
                receivedAtUtc),
            null);
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
            return currentConnection?.Socket;
        }
    }

    private MarketDataWebSocketConnectionGeneration? GetCurrentConnection()
    {
        lock (stateGate)
        {
            return currentConnection;
        }
    }

    private bool IsCurrentConnection(MarketDataWebSocketConnectionGeneration connection)
    {
        lock (stateGate)
        {
            return ReferenceEquals(currentConnection, connection);
        }
    }

    private void SetCurrentConnection(MarketDataWebSocketConnectionGeneration connection)
    {
        MarketDataWebSocketConnectionGeneration? previous;
        lock (stateGate)
        {
            previous = currentConnection;
            currentConnection = connection;
        }

        previous?.Subscriptions.Deactivate();
    }

    private void ClearCurrentConnection(MarketDataWebSocketConnectionGeneration connection)
    {
        var cleared = false;
        lock (stateGate)
        {
            if (ReferenceEquals(currentConnection, connection))
            {
                currentConnection = null;
                cleared = true;
            }
        }

        if (cleared)
        {
            connection.Subscriptions.Deactivate();
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

    private sealed record MarketDataWebSocketConnectionGeneration(
        ClientWebSocket Socket,
        MarketDataWebSocketSubscriptionGeneration Subscriptions);

}

internal sealed record MarketDataWebSocketDesiredAssetsSnapshot(
    long Revision,
    IReadOnlyList<string> AssetIds);

internal sealed class MarketDataWebSocketDesiredAssetSet(IEnumerable<string> initialAssetIds)
{
    private readonly object gate = new();
    private HashSet<string> assetIds = initialAssetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private long revision;

    public MarketDataWebSocketDesiredAssetsSnapshot Snapshot()
    {
        lock (gate)
        {
            return new MarketDataWebSocketDesiredAssetsSnapshot(
                revision,
                assetIds.OrderBy(assetId => assetId, StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }

    public bool Replace(IReadOnlyCollection<string> nextAssetIds)
    {
        var next = nextAssetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (gate)
        {
            if (assetIds.SetEquals(next))
            {
                return false;
            }

            assetIds = next;
            revision++;
            return true;
        }
    }

    public bool TryCompleteInitialization(long expectedRevision, Action complete)
    {
        lock (gate)
        {
            if (revision != expectedRevision)
            {
                return false;
            }

            complete();
            return true;
        }
    }
}

internal sealed class MarketDataWebSocketSubscriptionGeneration
{
    private readonly MarketDataWebSocketDesiredAssetSet desiredAssetSet;
    private readonly int batchSize;
    private readonly Func<IReadOnlyCollection<string>, CancellationToken, Task> sendInitialAsync;
    private readonly Func<string, IReadOnlyCollection<string>, CancellationToken, Task> sendUpdateAsync;
    private readonly CancellationTokenSource generationCts = new();
    private readonly CancellationToken generationToken;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly TaskCompletionSource initialSubscriptionCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<string> sentAssetIds = new(StringComparer.OrdinalIgnoreCase);
    private int initializationStarted;
    private int active = 1;
    private int sentAssetCount;
    private bool initialMessageSent;

    public MarketDataWebSocketSubscriptionGeneration(
        MarketDataWebSocketDesiredAssetSet desiredAssetSet,
        int batchSize,
        Func<IReadOnlyCollection<string>, CancellationToken, Task> sendInitialAsync,
        Func<string, IReadOnlyCollection<string>, CancellationToken, Task> sendUpdateAsync,
        CancellationToken generationToken)
    {
        this.desiredAssetSet = desiredAssetSet ?? throw new ArgumentNullException(nameof(desiredAssetSet));
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        this.batchSize = batchSize;
        this.sendInitialAsync = sendInitialAsync ?? throw new ArgumentNullException(nameof(sendInitialAsync));
        this.sendUpdateAsync = sendUpdateAsync ?? throw new ArgumentNullException(nameof(sendUpdateAsync));
        this.generationToken = generationToken;
    }

    public bool IsActive =>
        Volatile.Read(ref active) == 1 &&
        !generationCts.IsCancellationRequested &&
        !generationToken.IsCancellationRequested;

    public int SentAssetCount => Volatile.Read(ref sentAssetCount);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref initializationStarted, 1) != 0)
        {
            throw new InvalidOperationException("The WebSocket subscription generation has already been initialized.");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            generationCts.Token,
            generationToken);
        var lockTaken = false;
        try
        {
            await operationGate.WaitAsync(linkedCts.Token);
            lockTaken = true;
            while (true)
            {
                linkedCts.Token.ThrowIfCancellationRequested();
                var desired = desiredAssetSet.Snapshot();
                await ReconcileToSnapshotAsync(desired.AssetIds, linkedCts.Token);
                if (desiredAssetSet.TryCompleteInitialization(
                    desired.Revision,
                    () => initialSubscriptionCompleted.TrySetResult()))
                {
                    return;
                }
            }
        }
        catch
        {
            Deactivate();
            throw;
        }
        finally
        {
            if (lockTaken)
            {
                operationGate.Release();
            }
        }
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            generationCts.Token,
            generationToken);
        await initialSubscriptionCompleted.Task.WaitAsync(linkedCts.Token);
        await operationGate.WaitAsync(linkedCts.Token);
        try
        {
            while (true)
            {
                linkedCts.Token.ThrowIfCancellationRequested();
                var desired = desiredAssetSet.Snapshot();
                await ReconcileToSnapshotAsync(desired.AssetIds, linkedCts.Token);
                if (desiredAssetSet.Snapshot().Revision == desired.Revision)
                {
                    return;
                }
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    public void Deactivate()
    {
        if (Interlocked.Exchange(ref active, 0) == 0)
        {
            return;
        }

        generationCts.Cancel();
        initialSubscriptionCompleted.TrySetCanceled(generationCts.Token);
    }

    private async Task ReconcileToSnapshotAsync(
        IReadOnlyCollection<string> desiredAssetIds,
        CancellationToken cancellationToken)
    {
        var toSubscribe = desiredAssetIds
            .Except(sentAssetIds, StringComparer.OrdinalIgnoreCase)
            .OrderBy(assetId => assetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var toUnsubscribe = sentAssetIds
            .Except(desiredAssetIds, StringComparer.OrdinalIgnoreCase)
            .OrderBy(assetId => assetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var batch in toSubscribe.Chunk(batchSize))
        {
            if (!initialMessageSent)
            {
                await sendInitialAsync(batch, cancellationToken);
                initialMessageSent = true;
            }
            else
            {
                await sendUpdateAsync("subscribe", batch, cancellationToken);
            }

            sentAssetIds.UnionWith(batch);
            Volatile.Write(ref sentAssetCount, sentAssetIds.Count);
        }

        foreach (var batch in toUnsubscribe.Chunk(batchSize))
        {
            await sendUpdateAsync("unsubscribe", batch, cancellationToken);
            sentAssetIds.ExceptWith(batch);
            Volatile.Write(ref sentAssetCount, sentAssetIds.Count);
        }
    }
}

internal enum MarketDataWebSocketFirstFrameWaitOutcome
{
    Ready,
    ReceiveLoopCompleted,
    TimedOut
}

internal sealed class MarketDataWebSocketFirstFrameReadiness
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int state;

    public bool IsReady => Volatile.Read(ref state) == 1;

    internal bool TryClaimTimeout()
    {
        return Interlocked.CompareExchange(ref state, 2, 0) == 0;
    }

    public void ObserveFrame(MarketWebSocketDispatchFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Text.Length == 0 || Interlocked.CompareExchange(ref state, 1, 0) != 0)
        {
            return;
        }

        ready.TrySetResult();
    }

    public Task<MarketDataWebSocketFirstFrameWaitOutcome> WaitAsync(
        Task receiveLoop,
        TimeSpan timeout,
        Action onTimeout,
        CancellationToken cancellationToken)
    {
        return WaitAsync(receiveLoop, timeout, onTimeout, Task.Delay, cancellationToken);
    }

    internal async Task<MarketDataWebSocketFirstFrameWaitOutcome> WaitAsync(
        Task receiveLoop,
        TimeSpan timeout,
        Action onTimeout,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receiveLoop);
        ArgumentNullException.ThrowIfNull(onTimeout);
        ArgumentNullException.ThrowIfNull(delayAsync);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "First-frame timeout must be positive.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutTask = delayAsync(timeout, timeoutCts.Token);
        try
        {
            var completed = await Task.WhenAny(ready.Task, receiveLoop, timeoutTask);
            cancellationToken.ThrowIfCancellationRequested();

            if (ReferenceEquals(completed, ready.Task))
            {
                return MarketDataWebSocketFirstFrameWaitOutcome.Ready;
            }

            if (ReferenceEquals(completed, receiveLoop))
            {
                return MarketDataWebSocketFirstFrameWaitOutcome.ReceiveLoopCompleted;
            }

            await timeoutTask;
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryClaimTimeout())
            {
                return IsReady
                    ? MarketDataWebSocketFirstFrameWaitOutcome.Ready
                    : MarketDataWebSocketFirstFrameWaitOutcome.TimedOut;
            }

            onTimeout();
            return MarketDataWebSocketFirstFrameWaitOutcome.TimedOut;
        }
        finally
        {
            await timeoutCts.CancelAsync();
        }
    }
}

internal sealed class MarketDataWebSocketFirstFrameTimeoutException(int timeoutSeconds)
    : TimeoutException($"No complete non-empty inbound text frame was received within {timeoutSeconds} seconds.")
{
    public int TimeoutSeconds { get; } = timeoutSeconds;
}

internal sealed record MarketWebSocketDispatchFrame(
    string Text,
    DateTimeOffset ReceivedAtUtc);

internal sealed record MarketWebSocketReceivedMessage(
    MarketWebSocketDispatchFrame? Frame,
    MarketWebSocketCloseFrame? CloseFrame);

internal sealed record MarketWebSocketCloseFrame(
    WebSocketCloseStatus? Status,
    string? Description,
    DateTimeOffset ObservedAtUtc);

internal static class MarketDataWebSocketFrameHandoff
{
    public static async Task<MarketWebSocketCloseFrame?> RunAsync(
        int capacity,
        TimeSpan saturationThreshold,
        Func<CancellationToken, Task<MarketWebSocketReceivedMessage>> receiveAsync,
        Func<MarketWebSocketDispatchFrame, CancellationToken, Task> dispatchAsync,
        Action<MarketWebSocketDispatchFrame> onFrameReceived,
        Action<TimeSpan> onSaturation,
        Action onConsumerFailure,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        var channel = Channel.CreateBounded<MarketWebSocketDispatchFrame>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
        using var handoffCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = ProduceAsync(
            channel.Writer,
            saturationThreshold,
            receiveAsync,
            onFrameReceived,
            onSaturation,
            handoffCts.Token);
        var consumer = ConsumeAsync(channel.Reader, dispatchAsync, handoffCts.Token);

        var firstCompleted = await Task.WhenAny(producer, consumer);
        if (firstCompleted == consumer)
        {
            try
            {
                await consumer;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await handoffCts.CancelAsync();
                await SuppressAsync(producer);
                throw;
            }
            catch
            {
                onConsumerFailure();
                await handoffCts.CancelAsync();
                await SuppressAsync(producer);
                throw;
            }
        }

        MarketWebSocketCloseFrame? closeFrame;
        try
        {
            closeFrame = await producer;
        }
        catch
        {
            await handoffCts.CancelAsync();
            await SuppressAsync(consumer);
            throw;
        }

        try
        {
            await consumer;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            onConsumerFailure();
            throw;
        }

        return closeFrame;
    }

    private static async Task<MarketWebSocketCloseFrame?> ProduceAsync(
        ChannelWriter<MarketWebSocketDispatchFrame> writer,
        TimeSpan saturationThreshold,
        Func<CancellationToken, Task<MarketWebSocketReceivedMessage>> receiveAsync,
        Action<MarketWebSocketDispatchFrame> onFrameReceived,
        Action<TimeSpan> onSaturation,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var received = await receiveAsync(cancellationToken);
                if (received.CloseFrame is not null || received.Frame is null)
                {
                    return received.CloseFrame;
                }

                onFrameReceived(received.Frame);
                if (writer.TryWrite(received.Frame))
                {
                    continue;
                }

                var started = Stopwatch.GetTimestamp();
                try
                {
                    await writer.WriteAsync(received.Frame, cancellationToken);
                }
                finally
                {
                    var waitDuration = Stopwatch.GetElapsedTime(started);
                    if (waitDuration >= saturationThreshold)
                    {
                        onSaturation(waitDuration);
                    }
                }
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static async Task ConsumeAsync(
        ChannelReader<MarketWebSocketDispatchFrame> reader,
        Func<MarketWebSocketDispatchFrame, CancellationToken, Task> dispatchAsync,
        CancellationToken cancellationToken)
    {
        await foreach (var frame in reader.ReadAllAsync(cancellationToken))
        {
            await dispatchAsync(frame, cancellationToken);
        }
    }

    private static async Task SuppressAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }
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
