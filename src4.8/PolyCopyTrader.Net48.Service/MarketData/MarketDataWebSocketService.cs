using System.Collections.Concurrent;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Diagnostics;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.MarketData;

public sealed class MarketDataWebSocketService(
    ILogger<MarketDataWebSocketService> logger,
    ILoggerFactory loggerFactory,
    BotOptions botOptions,
    MarketDataWebSocketOptions options,
    PolymarketOptions polymarketOptions,
    IRelevantMarketAssetProvider assetProvider,
    IActiveMarketAssetSubscriptionRegistry activeMarketAssetSubscriptionRegistry,
    IMarketTradeTickDiagnosticService tradeTickDiagnosticService,
    IBtcOrderBookLagDiagnosticService btcOrderBookLagDiagnosticService,
    IMarketDataCache marketDataCache,
    IPaperTradingMarketDataUpdater paperTradingUpdater,
    IAppRepository repository) : BackgroundService
{
    private const string ComponentName = "PolymarketMarketWebSocket";
    private readonly ConcurrentDictionary<string, MarketDataWebSocketShardRunner> shardRunners = new(StringComparer.OrdinalIgnoreCase);
    private readonly MarketDataWebSocketShardAllocator shardAllocator = new(new MarketDataWebSocketOptionsAdapter(
        options.ShardMaxAssets,
        options.MaxShardConnections));
    private readonly object statusGate = new();
    private readonly Dictionary<string, MarketDataStatusSnapshot> shardStatuses = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? lastAggregateStatusPersistedUtc;
    private MarketDataConnectionState? lastPersistedAggregateState;
    private int? lastPersistedAggregateSubscribedAssetsCount;
    private string? lastPersistedAggregateError;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!botOptions.UseWebSockets || !options.Enabled)
        {
            marketDataCache.ReplaceSubscribedAssets([]);
            await PublishAggregateStatusAsync(MarketDataConnectionState.Disabled, 0, "Market WebSocket is disabled.", stoppingToken);
            logger.LogInformation("Market WebSocket service is disabled.");
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReconcileShardsAsync(stoppingToken);
                    await RestartUnhealthyShardsAsync(stoppingToken);
                    await PublishAggregateStatusAsync(stoppingToken);
                    await WaitForSupervisorWakeAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Market WebSocket supervisor cycle failed; retrying without stopping service.");
                    PublishSupervisorErrorStatus(ex);
                    await WaitForSupervisorRetryAsync(stoppingToken);
                }
            }
        }
        finally
        {
            try
            {
                await StopAllShardsAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Market WebSocket supervisor failed to stop all shards during shutdown.");
            }

            marketDataCache.ReplaceSubscribedAssets([]);
            try
            {
                await PublishAggregateStatusAsync(MarketDataConnectionState.Disconnected, 0, null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Market WebSocket supervisor failed to publish disconnected status during shutdown.");
            }
        }
    }

    private void PublishSupervisorErrorStatus(Exception exception)
    {
        var currentStatus = marketDataCache.Status;
        marketDataCache.UpdateStatus(currentStatus with
        {
            ConnectionState = MarketDataConnectionState.Reconnecting,
            Stale = true,
            LastError = exception.Message,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private async Task WaitForSupervisorRetryAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(GetSupervisorDelay(), cancellationToken);
    }

    private async Task ReconcileShardsAsync(CancellationToken cancellationToken)
    {
        var desiredAssetIds = await assetProvider.GetRelevantAssetIdsAsync(cancellationToken);
        if (desiredAssetIds.Count == 0)
        {
            marketDataCache.ReplaceSubscribedAssets([]);
            await StopAllShardsAsync(cancellationToken);
            await PublishAggregateStatusAsync(MarketDataConnectionState.Idle, 0, null, cancellationToken);
            return;
        }

        var plans = shardAllocator.Reconcile(
            desiredAssetIds,
            activeMarketAssetSubscriptionRegistry.GetSnapshots());
        var plannedAssetIds = plans.SelectMany(plan => plan.AssetIds).ToArray();
        marketDataCache.ReplaceSubscribedAssets(plannedAssetIds);

        var desiredComponents = plans
            .Select(plan => plan.Component)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var component in shardRunners.Keys.ToArray())
        {
            if (desiredComponents.Contains(component))
            {
                continue;
            }

            if (shardRunners.TryRemove(component, out var removed))
            {
                logger.LogInformation("Stopping removed market WebSocket shard {Component}.", component);
                await removed.StopAsync();
                RemoveShardStatus(component);
                await PublishShardRemovedStatusAsync(component, cancellationToken);
            }
        }

        foreach (var plan in plans)
        {
            if (shardRunners.TryGetValue(plan.Component, out var existing))
            {
                await existing.UpdateAssetsAsync(plan.AssetIds, cancellationToken);
                continue;
            }

            StartShard(plan, cancellationToken);
        }
    }

    private async Task RestartUnhealthyShardsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var runner in shardRunners.Values.ToArray())
        {
            if (!runner.ShouldRestart(now))
            {
                continue;
            }

            var plan = new MarketDataWebSocketShardPlan(
                0,
                runner.Component,
                runner.AssetIds);
            logger.LogWarning(
                "Restarting unhealthy market WebSocket shard {Component}; subscribed assets: {SubscribedAssetsCount}.",
                runner.Component,
                runner.AssetIds.Count);

            if (shardRunners.TryRemove(runner.Component, out var removed))
            {
                await removed.StopAsync();
                StartShard(plan, cancellationToken);
            }
        }
    }

    private void StartShard(MarketDataWebSocketShardPlan plan, CancellationToken stoppingToken)
    {
        var runner = new MarketDataWebSocketShardRunner(
            loggerFactory.CreateLogger<MarketDataWebSocketShardRunner>(),
            plan,
            options,
            polymarketOptions,
            repository,
            ProcessTextMessageAsync,
            OnShardStatus);

        if (!shardRunners.TryAdd(plan.Component, runner))
        {
            return;
        }

        logger.LogInformation(
            "Starting market WebSocket shard {Component} with {SubscribedAssetsCount} assets.",
            plan.Component,
            plan.AssetIds.Count);
        runner.Start(stoppingToken);
    }

    private async Task StopAllShardsAsync(CancellationToken cancellationToken)
    {
        foreach (var component in shardRunners.Keys.ToArray())
        {
            if (!shardRunners.TryRemove(component, out var runner))
            {
                continue;
            }

            await runner.StopAsync();
            RemoveShardStatus(component);
            await PublishShardRemovedStatusAsync(component, cancellationToken);
        }
    }

    private async Task WaitForSupervisorWakeAsync(CancellationToken cancellationToken)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(GetSupervisorDelay(), waitCts.Token);
        var changeTask = activeMarketAssetSubscriptionRegistry.WaitForChangeAsync(waitCts.Token);
        var completedTask = await Task.WhenAny(delayTask, changeTask);
        await waitCts.CancelAsync();
        try
        {
            await completedTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    private TimeSpan GetSupervisorDelay()
    {
        var delaySeconds = Math.Max(1, Math.Min(options.SubscriptionRefreshSeconds, options.WatchdogIntervalSeconds));
        return TimeSpan.FromSeconds(Math.Min(delaySeconds, 10));
    }

    private async Task ProcessTextMessageAsync(string component, string message, DateTimeOffset receivedAtUtc, CancellationToken cancellationToken)
    {
        IReadOnlyList<MarketDataUpdate> updates;
        try
        {
            updates = PolymarketMarketDataWebSocketParser.ParseMarketMessage(message);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Market WebSocket shard {Component} message parsing failed.", component);
            await TryRecordApiErrorAsync(component, "ParseMarketMessage", ex.Message, cancellationToken);
            return;
        }

        if (updates.Count == 0)
        {
            return;
        }

        foreach (var update in updates)
        {
            try
            {
                activeMarketAssetSubscriptionRegistry.ApplyMarketDataUpdate(update);
                marketDataCache.ApplyUpdate(update);
                btcOrderBookLagDiagnosticService.RecordPolymarketTopOfBook(update, receivedAtUtc);
                await tradeTickDiagnosticService.RecordAsync(update, cancellationToken);
                if (options.PersistOrderBookSnapshots && update.OrderBookSnapshot is not null)
                {
                    await repository.AddOrderBookSnapshotAsync(update.OrderBookSnapshot, cancellationToken);
                }

                if (options.PersistMarketDataEvents)
                {
                    await repository.AddMarketDataEventAsync(ToMarketDataEvent(update), cancellationToken);
                }

                await paperTradingUpdater.ApplyUpdateAsync(update, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist or dispatch market data update {EventType} from {Component}.", update.EventType, component);
                await TryRecordApiErrorAsync(component, "ProcessMarketDataUpdate", ex.Message, cancellationToken);
            }
        }
    }

    private void OnShardStatus(MarketDataStatusSnapshot status)
    {
        lock (statusGate)
        {
            shardStatuses[status.Component] = status;
        }
    }

    private void RemoveShardStatus(string component)
    {
        lock (statusGate)
        {
            shardStatuses.Remove(component);
        }
    }

    private async Task PublishShardRemovedStatusAsync(string component, CancellationToken cancellationToken)
    {
        var status = new MarketDataStatusSnapshot(
            component,
            MarketDataConnectionState.Disconnected,
            options.MarketEndpointUrl,
            0,
            null,
            null,
            DateTimeOffset.UtcNow,
            0,
            false,
            "Shard removed from current subscription plan.",
            DateTimeOffset.UtcNow);

        try
        {
            await repository.UpsertMarketDataStatusAsync(status, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist removed market WebSocket shard status for {Component}.", component);
        }
    }

    private async Task PublishAggregateStatusAsync(CancellationToken cancellationToken)
    {
        var statuses = GetShardStatuses();
        var subscribedAssetsCount = marketDataCache.SubscribedAssetIds.Count;
        var now = DateTimeOffset.UtcNow;
        var state = CalculateAggregateState(statuses, shardRunners.Count);
        var lastError = BuildAggregateError(statuses, shardRunners.Count);
        var status = new MarketDataStatusSnapshot(
            ComponentName,
            state,
            options.MarketEndpointUrl,
            subscribedAssetsCount,
            MaxDate(statuses.Select(shard => shard.LastMessageUtc)),
            MaxDate(statuses.Select(shard => shard.LastConnectedUtc)),
            MaxDate(statuses.Select(shard => shard.LastDisconnectedUtc)),
            statuses.Sum(shard => shard.ReconnectCount),
            state == MarketDataConnectionState.Stale || statuses.Any(shard => shard.Stale),
            lastError,
            now);

        marketDataCache.UpdateStatus(status);
        await PersistAggregateStatusIfNeededAsync(status, cancellationToken);
    }

    private async Task PublishAggregateStatusAsync(
        MarketDataConnectionState state,
        int subscribedAssetsCount,
        string? lastError,
        CancellationToken cancellationToken)
    {
        var status = new MarketDataStatusSnapshot(
            ComponentName,
            state,
            options.MarketEndpointUrl,
            subscribedAssetsCount,
            null,
            null,
            state == MarketDataConnectionState.Disconnected ? DateTimeOffset.UtcNow : null,
            0,
            false,
            lastError,
            DateTimeOffset.UtcNow);

        marketDataCache.UpdateStatus(status);
        await PersistAggregateStatusIfNeededAsync(status, cancellationToken);
    }

    private MarketDataStatusSnapshot[] GetShardStatuses()
    {
        lock (statusGate)
        {
            return shardStatuses.Values.ToArray();
        }
    }

    private static MarketDataConnectionState CalculateAggregateState(
        IReadOnlyCollection<MarketDataStatusSnapshot> statuses,
        int runnerCount)
    {
        if (runnerCount == 0)
        {
            return MarketDataConnectionState.Idle;
        }

        if (statuses.Count == 0)
        {
            return MarketDataConnectionState.Connecting;
        }

        var healthy = statuses.Count(status =>
            status.ConnectionState == MarketDataConnectionState.Connected && !status.Stale);
        var unhealthy = statuses.Count - healthy;
        if (healthy == runnerCount && unhealthy == 0)
        {
            return MarketDataConnectionState.Connected;
        }

        if (healthy > 0)
        {
            return MarketDataConnectionState.Stale;
        }

        if (statuses.Any(status => status.ConnectionState == MarketDataConnectionState.Connecting))
        {
            return MarketDataConnectionState.Connecting;
        }

        if (statuses.Any(status => status.ConnectionState == MarketDataConnectionState.Reconnecting))
        {
            return MarketDataConnectionState.Reconnecting;
        }

        if (statuses.Any(status => status.ConnectionState == MarketDataConnectionState.Error))
        {
            return MarketDataConnectionState.Error;
        }

        return MarketDataConnectionState.Disconnected;
    }

    private static string? BuildAggregateError(
        IReadOnlyCollection<MarketDataStatusSnapshot> statuses,
        int runnerCount)
    {
        if (runnerCount == 0)
        {
            return null;
        }

        var healthy = statuses.Count(status =>
            status.ConnectionState == MarketDataConnectionState.Connected && !status.Stale);
        var missing = Math.Max(0, runnerCount - statuses.Count);
        var unhealthy = runnerCount - healthy;
        if (unhealthy == 0)
        {
            return null;
        }

        var reconnecting = statuses.Count(status => status.ConnectionState == MarketDataConnectionState.Reconnecting);
        var stale = statuses.Count(status => status.Stale || status.ConnectionState == MarketDataConnectionState.Stale);
        var disconnected = statuses.Count(status => status.ConnectionState == MarketDataConnectionState.Disconnected);
        var error = statuses.Count(status => status.ConnectionState == MarketDataConnectionState.Error);
        var sampleError = statuses
            .Select(status => status.LastError)
            .FirstOrDefault(lastError => !string.IsNullOrWhiteSpace(lastError));

        return string.Join(
            "; ",
            new[]
            {
                $"healthy_shards={healthy}/{runnerCount}",
                $"missing_status_shards={missing}",
                $"reconnecting_shards={reconnecting}",
                $"stale_shards={stale}",
                $"disconnected_shards={disconnected}",
                $"error_shards={error}",
                string.IsNullOrWhiteSpace(sampleError) ? null : "sample_error=" + sampleError
            }.Where(part => part is not null));
    }

    private async Task PersistAggregateStatusIfNeededAsync(
        MarketDataStatusSnapshot status,
        CancellationToken cancellationToken)
    {
        if (!ShouldPersistAggregateStatus(status))
        {
            return;
        }

        try
        {
            await repository.UpsertMarketDataStatusAsync(status, cancellationToken);
            lastAggregateStatusPersistedUtc = DateTimeOffset.UtcNow;
            lastPersistedAggregateState = status.ConnectionState;
            lastPersistedAggregateSubscribedAssetsCount = status.SubscribedAssetsCount;
            lastPersistedAggregateError = status.LastError;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist aggregate market WebSocket status.");
        }
    }

    private bool ShouldPersistAggregateStatus(MarketDataStatusSnapshot status)
    {
        if (lastAggregateStatusPersistedUtc is null ||
            lastPersistedAggregateState != status.ConnectionState ||
            lastPersistedAggregateSubscribedAssetsCount != status.SubscribedAssetsCount ||
            !string.Equals(lastPersistedAggregateError, status.LastError, StringComparison.Ordinal))
        {
            return true;
        }

        return DateTimeOffset.UtcNow - lastAggregateStatusPersistedUtc.Value >=
            TimeSpan.FromSeconds(options.StatusPersistIntervalSeconds);
    }

    private async Task TryRecordApiErrorAsync(
        string component,
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), component, operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist market WebSocket API error for {Component}.", component);
        }
    }

    private static DateTimeOffset? MaxDate(IEnumerable<DateTimeOffset?> values)
    {
        return values
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Max() is { } max && max != default
            ? max
            : null;
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
}
