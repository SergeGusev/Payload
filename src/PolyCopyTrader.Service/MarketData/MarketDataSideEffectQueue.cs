using System.Diagnostics;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.MarketData;

public enum MarketDataSideEffectEnqueueOutcome
{
    Enqueued,
    Coalesced,
    Dropped,
    Rejected
}

public sealed record MarketDataSideEffectQueueMetrics(
    int PendingUpdates,
    int PendingDiagnostics,
    int TrackedAssets,
    long EnqueuedUpdates,
    long CoalescedUpdates,
    long UpdateSoftLimitOverflows,
    long RejectedUpdates,
    long ProcessedUpdates,
    long FailedUpdates,
    long EnqueuedDiagnostics,
    long DroppedDiagnostics,
    long DiagnosticSoftLimitOverflows,
    long RejectedDiagnostics,
    long ProcessedDiagnostics,
    long FailedDiagnostics);

public interface IMarketDataSideEffectQueue
{
    MarketDataSideEffectEnqueueOutcome EnqueueUpdate(
        string component,
        MarketDataUpdate update,
        ActiveMarketAssetSnapshot? activeMarketSnapshot,
        DateTimeOffset receivedAtUtc,
        IReadOnlySet<Guid>? eligiblePaperOrderIds);

    MarketDataSideEffectEnqueueOutcome EnqueueFrameDiagnostic(
        MarketWebSocketFrameDiagnostic diagnostic,
        bool important);

    MarketDataSideEffectEnqueueOutcome EnqueueApiError(ApiError apiError);

    MarketDataSideEffectQueueMetrics GetMetrics();

    bool HasOutstandingPaperOrderUpdate(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc)
    {
        return false;
    }

    Task DrainOutstandingPaperOrderUpdatesAsync(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class MarketDataSideEffectQueue(
    ILogger<MarketDataSideEffectQueue> logger,
    MarketDataWebSocketOptions options,
    IMarketDataSideEffectHandler handler,
    IAppRepository repository,
    IMakerGtdPaperPlacementHandoff? makerGtdPaperPlacementHandoff = null) : IHostedService, IMarketDataSideEffectQueue
{
    private const string ComponentName = "MarketDataSideEffectQueue";
    private static readonly IReadOnlySet<Guid> EmptyPaperOrderIds = new HashSet<Guid>();
    private readonly object lifecycleSync = new();
    private readonly object updateSync = new();
    private readonly object frameDiagnosticSync = new();
    private readonly IMakerGtdPaperPlacementHandoff makerGtdHandoff =
        makerGtdPaperPlacementHandoff ?? NoOpMakerGtdPaperPlacementHandoff.Instance;
    private readonly Dictionary<string, PendingAssetUpdates> pendingUpdatesByAsset = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> readyAssetKeys = [];
    private readonly List<PaperOrderDrainRequest> paperOrderDrainRequests = [];
    private readonly LinkedList<DiagnosticWorkItem> pendingDiagnostics = [];
    private readonly SemaphoreSlim updateSignal = new(0);
    private readonly SemaphoreSlim frameDiagnosticSignal = new(0);
    private Task? updateWorkerTask;
    private Task? frameDiagnosticWorkerTask;
    private Task? metricsWorkerTask;
    private Task? stopTask;
    private CancellationTokenSource? metricsCancellation;
    private MarketDataSideEffectWorkItem? inFlightUpdate;
    private volatile bool accepting;
    private int pendingUpdateCount;
    private int pendingFrameDiagnosticCount;
    private long enqueuedUpdates;
    private long coalescedUpdates;
    private long updateSoftLimitOverflows;
    private long rejectedUpdates;
    private long processedUpdates;
    private long failedUpdates;
    private long enqueuedFrameDiagnostics;
    private long droppedFrameDiagnostics;
    private long frameDiagnosticSoftLimitOverflows;
    private long rejectedFrameDiagnostics;
    private long processedFrameDiagnostics;
    private long failedFrameDiagnostics;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (lifecycleSync)
        {
            if (updateWorkerTask is not null)
            {
                return Task.CompletedTask;
            }

            accepting = true;
            metricsCancellation = new CancellationTokenSource();
            updateWorkerTask = Task.Run(RunUpdateWorkerAsync, CancellationToken.None);
            frameDiagnosticWorkerTask = Task.Run(RunFrameDiagnosticWorkerAsync, CancellationToken.None);
            metricsWorkerTask = Task.Run(
                () => RunMetricsWorkerAsync(metricsCancellation.Token),
                CancellationToken.None);
        }

        logger.LogInformation(
            "Market-data side-effect queue started. MaxPendingUpdatesPerAsset={MaxPendingUpdatesPerAsset} DiagnosticQueueCapacity={DiagnosticQueueCapacity}",
            options.SideEffectMaxPendingUpdatesPerAsset,
            options.SideEffectDiagnosticQueueCapacity);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Task? updateCompletion;
        Task? diagnosticCompletion;
        Task? metricsCompletion;
        CancellationTokenSource? metricsCts;
        lock (lifecycleSync)
        {
            if (stopTask is not null)
            {
                return stopTask;
            }

            accepting = false;
            updateCompletion = updateWorkerTask;
            diagnosticCompletion = frameDiagnosticWorkerTask;
            metricsCompletion = metricsWorkerTask;
            metricsCts = metricsCancellation;
            stopTask = updateCompletion is null || diagnosticCompletion is null
                ? Task.CompletedTask
                : DrainAndStopAsync(updateCompletion, diagnosticCompletion, metricsCompletion, metricsCts);
            return stopTask;
        }
    }

    private async Task DrainAndStopAsync(
        Task updateCompletion,
        Task diagnosticCompletion,
        Task? metricsCompletion,
        CancellationTokenSource? metricsCts)
    {
        var metrics = GetMetrics();
        logger.LogInformation(
            "Market-data side-effect queue is stopping and will drain. PendingUpdates={PendingUpdates} PendingDiagnostics={PendingDiagnostics}",
            metrics.PendingUpdates,
            metrics.PendingDiagnostics);

        updateSignal.Release();
        frameDiagnosticSignal.Release();
        await Task.WhenAll(updateCompletion, diagnosticCompletion).ConfigureAwait(false);

        if (metricsCts is not null)
        {
            await metricsCts.CancelAsync();
        }

        if (metricsCompletion is not null)
        {
            try
            {
                await metricsCompletion.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        metricsCts?.Dispose();
        logger.LogInformation("Market-data side-effect queue stopped after draining.");
    }

    public MarketDataSideEffectEnqueueOutcome EnqueueUpdate(
        string component,
        MarketDataUpdate update,
        ActiveMarketAssetSnapshot? activeMarketSnapshot,
        DateTimeOffset receivedAtUtc,
        IReadOnlySet<Guid>? eligiblePaperOrderIds)
    {
        var replaceable = IsReplaceable(update, eligiblePaperOrderIds);
        var workItem = new MarketDataSideEffectWorkItem(
            component,
            update,
            activeMarketSnapshot,
            receivedAtUtc,
            DateTimeOffset.UtcNow,
            eligiblePaperOrderIds is { Count: 0 } ? EmptyPaperOrderIds : eligiblePaperOrderIds,
            replaceable);
        var assetKey = GetAssetKey(component, update);
        var shouldSignal = false;
        MarketDataSideEffectEnqueueOutcome outcome;

        lock (updateSync)
        {
            if (!accepting)
            {
                Interlocked.Increment(ref rejectedUpdates);
                return MarketDataSideEffectEnqueueOutcome.Rejected;
            }

            if (!pendingUpdatesByAsset.TryGetValue(assetKey, out var pendingAssetUpdates))
            {
                pendingAssetUpdates = new PendingAssetUpdates();
                pendingUpdatesByAsset[assetKey] = pendingAssetUpdates;
            }

            var replaceableNode = replaceable
                ? FindReplaceableNodeAfterResolution(pendingAssetUpdates.Items)
                : null;
            if (replaceableNode is not null)
            {
                pendingAssetUpdates.Items.Remove(replaceableNode);
                pendingAssetUpdates.Items.AddLast(workItem);
                Interlocked.Increment(ref coalescedUpdates);
                outcome = MarketDataSideEffectEnqueueOutcome.Coalesced;
            }
            else
            {
                if (pendingAssetUpdates.Items.Count >= options.SideEffectMaxPendingUpdatesPerAsset)
                {
                    Interlocked.Increment(ref updateSoftLimitOverflows);
                }

                pendingAssetUpdates.Items.AddLast(workItem);
                pendingUpdateCount++;
                Interlocked.Increment(ref enqueuedUpdates);
                outcome = MarketDataSideEffectEnqueueOutcome.Enqueued;
            }

            if (!pendingAssetUpdates.Scheduled)
            {
                pendingAssetUpdates.Scheduled = true;
                readyAssetKeys.AddLast(assetKey);
                shouldSignal = true;
            }
        }

        if (shouldSignal)
        {
            updateSignal.Release();
        }

        return outcome;
    }

    public MarketDataSideEffectEnqueueOutcome EnqueueFrameDiagnostic(
        MarketWebSocketFrameDiagnostic diagnostic,
        bool important)
    {
        return EnqueueDiagnostic(new DiagnosticWorkItem(diagnostic, null, important));
    }

    public MarketDataSideEffectEnqueueOutcome EnqueueApiError(ApiError apiError)
    {
        return EnqueueDiagnostic(new DiagnosticWorkItem(null, apiError, Important: true));
    }

    private MarketDataSideEffectEnqueueOutcome EnqueueDiagnostic(DiagnosticWorkItem workItem)
    {
        var shouldSignal = false;
        lock (frameDiagnosticSync)
        {
            if (!accepting)
            {
                Interlocked.Increment(ref rejectedFrameDiagnostics);
                return MarketDataSideEffectEnqueueOutcome.Rejected;
            }

            if (pendingFrameDiagnosticCount >= options.SideEffectDiagnosticQueueCapacity)
            {
                if (!workItem.Important)
                {
                    Interlocked.Increment(ref droppedFrameDiagnostics);
                    return MarketDataSideEffectEnqueueOutcome.Dropped;
                }

                var ordinaryNode = FindFirstOrdinaryDiagnostic(pendingDiagnostics);
                if (ordinaryNode is not null)
                {
                    pendingDiagnostics.Remove(ordinaryNode);
                    pendingFrameDiagnosticCount--;
                    Interlocked.Increment(ref droppedFrameDiagnostics);
                }
                else
                {
                    Interlocked.Increment(ref frameDiagnosticSoftLimitOverflows);
                }
            }

            pendingDiagnostics.AddLast(workItem);
            pendingFrameDiagnosticCount++;
            Interlocked.Increment(ref enqueuedFrameDiagnostics);
            shouldSignal = true;
        }

        if (shouldSignal)
        {
            frameDiagnosticSignal.Release();
        }

        return MarketDataSideEffectEnqueueOutcome.Enqueued;
    }

    public MarketDataSideEffectQueueMetrics GetMetrics()
    {
        int pendingUpdates;
        int trackedAssets;
        lock (updateSync)
        {
            pendingUpdates = pendingUpdateCount;
            trackedAssets = pendingUpdatesByAsset.Count;
        }

        int pendingDiagnostics;
        lock (frameDiagnosticSync)
        {
            pendingDiagnostics = pendingFrameDiagnosticCount;
        }

        return new MarketDataSideEffectQueueMetrics(
            pendingUpdates,
            pendingDiagnostics,
            trackedAssets,
            Interlocked.Read(ref enqueuedUpdates),
            Interlocked.Read(ref coalescedUpdates),
            Interlocked.Read(ref updateSoftLimitOverflows),
            Interlocked.Read(ref rejectedUpdates),
            Interlocked.Read(ref processedUpdates),
            Interlocked.Read(ref failedUpdates),
            Interlocked.Read(ref enqueuedFrameDiagnostics),
            Interlocked.Read(ref droppedFrameDiagnostics),
            Interlocked.Read(ref frameDiagnosticSoftLimitOverflows),
            Interlocked.Read(ref rejectedFrameDiagnostics),
            Interlocked.Read(ref processedFrameDiagnostics),
            Interlocked.Read(ref failedFrameDiagnostics));
    }

    public bool HasOutstandingPaperOrderUpdate(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc)
    {
        if (paperOrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(assetId) ||
            string.IsNullOrWhiteSpace(conditionId) ||
            acceptedAfterUtc >= expiresBeforeUtc)
        {
            return false;
        }

        lock (updateSync)
        {
            return CountOutstandingPaperOrderUpdates(
                paperOrderId,
                assetId,
                conditionId,
                acceptedAfterUtc,
                expiresBeforeUtc) > 0;
        }
    }

    public async Task DrainOutstandingPaperOrderUpdatesAsync(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        if (paperOrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(assetId) ||
            string.IsNullOrWhiteSpace(conditionId) ||
            acceptedAfterUtc >= expiresBeforeUtc)
        {
            return;
        }

        PaperOrderDrainRequest request;
        lock (updateSync)
        {
            if (CountOutstandingPaperOrderUpdates(
                    paperOrderId,
                    assetId,
                    conditionId,
                    acceptedAfterUtc,
                    expiresBeforeUtc) == 0)
            {
                return;
            }

            request = new PaperOrderDrainRequest(
                paperOrderId,
                assetId,
                conditionId,
                acceptedAfterUtc,
                expiresBeforeUtc,
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            paperOrderDrainRequests.Add(request);
        }

        var waitStarted = Stopwatch.GetTimestamp();
        var canceled = false;
        try
        {
            await request.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            throw;
        }
        finally
        {
            int remainingMatches;
            lock (updateSync)
            {
                paperOrderDrainRequests.Remove(request);
                remainingMatches = CountOutstandingPaperOrderUpdates(
                    paperOrderId,
                    assetId,
                    conditionId,
                    acceptedAfterUtc,
                    expiresBeforeUtc);
            }

            var waitDuration = Stopwatch.GetElapsedTime(waitStarted);
            if (waitDuration.TotalMilliseconds >= options.SideEffectSlowProcessingMilliseconds)
            {
                logger.LogWarning(
                    "Maker-GTD Paper expiry side-effect drain was slow. PaperOrderId={PaperOrderId} AssetId={AssetId} WaitDurationMs={WaitDurationMs} RemainingMatchingUpdates={RemainingMatchingUpdates} Canceled={Canceled}",
                    paperOrderId,
                    assetId,
                    waitDuration.TotalMilliseconds,
                    remainingMatches,
                    canceled);
            }
        }
    }

    private async Task RunUpdateWorkerAsync()
    {
        try
        {
            while (true)
            {
                await updateSignal.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                if (!TryTakeUpdate(out var workItem))
                {
                    if (!accepting && GetPendingUpdateCount() == 0)
                    {
                        return;
                    }

                    continue;
                }

                await ProcessUpdateAsync(workItem).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Market-data side-effect update worker stopped unexpectedly.");
            throw;
        }
    }

    private async Task RunFrameDiagnosticWorkerAsync()
    {
        try
        {
            while (true)
            {
                await frameDiagnosticSignal.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                if (!TryTakeFrameDiagnostic(out var workItem))
                {
                    if (!accepting && GetPendingFrameDiagnosticCount() == 0)
                    {
                        return;
                    }

                    continue;
                }

                await ProcessDiagnosticAsync(workItem).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Market-data frame diagnostic worker stopped unexpectedly.");
            throw;
        }
    }

    private async Task RunMetricsWorkerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.SideEffectMetricsIntervalSeconds));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var metrics = GetMetrics();
            logger.LogInformation(
                "Market-data side-effect queue metrics. PendingUpdates={PendingUpdates} PendingDiagnostics={PendingDiagnostics} TrackedAssets={TrackedAssets} EnqueuedUpdates={EnqueuedUpdates} CoalescedUpdates={CoalescedUpdates} UpdateSoftLimitOverflows={UpdateSoftLimitOverflows} RejectedUpdates={RejectedUpdates} ProcessedUpdates={ProcessedUpdates} FailedUpdates={FailedUpdates} EnqueuedDiagnostics={EnqueuedDiagnostics} DroppedDiagnostics={DroppedDiagnostics} DiagnosticSoftLimitOverflows={DiagnosticSoftLimitOverflows} RejectedDiagnostics={RejectedDiagnostics} ProcessedDiagnostics={ProcessedDiagnostics} FailedDiagnostics={FailedDiagnostics}",
                metrics.PendingUpdates,
                metrics.PendingDiagnostics,
                metrics.TrackedAssets,
                metrics.EnqueuedUpdates,
                metrics.CoalescedUpdates,
                metrics.UpdateSoftLimitOverflows,
                metrics.RejectedUpdates,
                metrics.ProcessedUpdates,
                metrics.FailedUpdates,
                metrics.EnqueuedDiagnostics,
                metrics.DroppedDiagnostics,
                metrics.DiagnosticSoftLimitOverflows,
                metrics.RejectedDiagnostics,
                metrics.ProcessedDiagnostics,
                metrics.FailedDiagnostics);
        }
    }

    private bool TryTakeUpdate(out MarketDataSideEffectWorkItem workItem)
    {
        var shouldSignal = false;
        lock (updateSync)
        {
            while (readyAssetKeys.Count > 0)
            {
                var readyNode = FindPriorityReadyAssetNode() ?? readyAssetKeys.First!;
                var assetKey = readyNode.Value;
                readyAssetKeys.Remove(readyNode);
                if (!pendingUpdatesByAsset.TryGetValue(assetKey, out var pendingAssetUpdates) ||
                    pendingAssetUpdates.Items.First is not { } firstNode)
                {
                    continue;
                }

                pendingAssetUpdates.Scheduled = false;
                workItem = firstNode.Value;
                pendingAssetUpdates.Items.RemoveFirst();
                pendingUpdateCount--;
                inFlightUpdate = workItem;

                if (pendingAssetUpdates.Items.Count > 0)
                {
                    pendingAssetUpdates.Scheduled = true;
                    if (HasPendingDrainMatch(pendingAssetUpdates.Items))
                    {
                        readyAssetKeys.AddFirst(assetKey);
                    }
                    else
                    {
                        readyAssetKeys.AddLast(assetKey);
                    }

                    shouldSignal = true;
                }
                else
                {
                    pendingUpdatesByAsset.Remove(assetKey);
                }

                if (shouldSignal)
                {
                    updateSignal.Release();
                }

                return true;
            }
        }

        workItem = default!;
        return false;
    }

    private bool TryTakeFrameDiagnostic(out DiagnosticWorkItem workItem)
    {
        lock (frameDiagnosticSync)
        {
            if (pendingDiagnostics.First is not { } firstNode)
            {
                workItem = default!;
                return false;
            }

            workItem = firstNode.Value;
            pendingDiagnostics.RemoveFirst();
            pendingFrameDiagnosticCount--;
            return true;
        }
    }

    private async Task ProcessUpdateAsync(MarketDataSideEffectWorkItem workItem)
    {
        var processingStarted = Stopwatch.GetTimestamp();
        var queueDelay = DateTimeOffset.UtcNow - workItem.EnqueuedAtUtc;
        if (queueDelay < TimeSpan.Zero)
        {
            queueDelay = TimeSpan.Zero;
        }

        try
        {
            await handler.ProcessUpdateAsync(workItem, CancellationToken.None).ConfigureAwait(false);
            Interlocked.Increment(ref processedUpdates);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref failedUpdates);
            RecordMakerGtdMarketDataFailure(workItem);
            var phase = ex is MarketDataSideEffectPhaseException phaseException
                ? phaseException.Phase
                : "ProcessUpdate";
            logger.LogError(
                ex,
                "Queued market-data side effect failed. Component={Component} Phase={Phase} EventType={EventType} AssetId={AssetId} QueueDelayMs={QueueDelayMs}",
                workItem.Component,
                phase,
                workItem.Update.EventType,
                workItem.Update.AssetId,
                queueDelay.TotalMilliseconds);
            await TryRecordApiErrorAsync(
                $"ProcessUpdate/{phase}",
                $"Component={workItem.Component}; EventType={workItem.Update.EventType}; AssetId={workItem.Update.AssetId ?? "<null>"}; QueueDelayMs={queueDelay.TotalMilliseconds:F0}; Error={ex.Message}")
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteInFlightUpdate(workItem);
        }

        var processingDuration = Stopwatch.GetElapsedTime(processingStarted);
        if (queueDelay.TotalMilliseconds >= options.SideEffectSlowProcessingMilliseconds ||
            processingDuration.TotalMilliseconds >= options.SideEffectSlowProcessingMilliseconds)
        {
            logger.LogWarning(
                "Queued market-data side effect was slow. Component={Component} EventType={EventType} AssetId={AssetId} QueueDelayMs={QueueDelayMs} ProcessingDurationMs={ProcessingDurationMs} PendingUpdates={PendingUpdates}",
                workItem.Component,
                workItem.Update.EventType,
                workItem.Update.AssetId,
                queueDelay.TotalMilliseconds,
                processingDuration.TotalMilliseconds,
                GetPendingUpdateCount());
        }
    }

    private void RecordMakerGtdMarketDataFailure(MarketDataSideEffectWorkItem workItem)
    {
        if (workItem.Update.EventType is not (
                MarketDataEventType.Book or
                MarketDataEventType.PriceChange or
                MarketDataEventType.LastTradePrice or
                MarketDataEventType.BestBidAsk))
        {
            return;
        }

        makerGtdHandoff.RecordMarketDataFailure(
            workItem.Update.AssetId,
            workItem.Update.ConditionId,
            workItem.ReceivedAtUtc,
            workItem.EligiblePaperOrderIds,
            MakerGtdPaperExecutionContract.MarketDataHandlerFailureCode);
    }

    private async Task ProcessDiagnosticAsync(DiagnosticWorkItem workItem)
    {
        try
        {
            if (workItem.FrameDiagnostic is not null)
            {
                await handler.PersistFrameDiagnosticAsync(workItem.FrameDiagnostic, CancellationToken.None).ConfigureAwait(false);
            }
            else if (workItem.ApiError is not null)
            {
                await handler.PersistApiErrorAsync(workItem.ApiError, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException("Queued market-data diagnostic has no payload.");
            }

            Interlocked.Increment(ref processedFrameDiagnostics);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref failedFrameDiagnostics);
            var kind = workItem.FrameDiagnostic is not null ? "FrameDiagnostic" : "ApiError";
            logger.LogError(ex, "Failed to persist queued market-data diagnostic. Kind={Kind}", kind);
            await TryRecordApiErrorAsync($"Persist{kind}", ex.Message).ConfigureAwait(false);
        }
    }

    private async Task TryRecordApiErrorAsync(string operation, string message)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), ComponentName, operation, message, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist market-data side-effect queue API error for {Operation}.", operation);
        }
    }

    private int GetPendingUpdateCount()
    {
        lock (updateSync)
        {
            return pendingUpdateCount;
        }
    }

    private int GetPendingFrameDiagnosticCount()
    {
        lock (frameDiagnosticSync)
        {
            return pendingFrameDiagnosticCount;
        }
    }

    private void CompleteInFlightUpdate(MarketDataSideEffectWorkItem workItem)
    {
        lock (updateSync)
        {
            if (ReferenceEquals(inFlightUpdate, workItem))
            {
                inFlightUpdate = null;
            }

            for (var index = paperOrderDrainRequests.Count - 1; index >= 0; index--)
            {
                var request = paperOrderDrainRequests[index];
                if (CountOutstandingPaperOrderUpdates(
                        request.PaperOrderId,
                        request.AssetId,
                        request.ConditionId,
                        request.AcceptedAfterUtc,
                        request.ExpiresBeforeUtc) == 0)
                {
                    paperOrderDrainRequests.RemoveAt(index);
                    request.Completion.TrySetResult(true);
                }
            }
        }
    }

    private LinkedListNode<string>? FindPriorityReadyAssetNode()
    {
        foreach (var request in paperOrderDrainRequests)
        {
            for (var node = readyAssetKeys.First; node is not null; node = node.Next)
            {
                if (pendingUpdatesByAsset.TryGetValue(node.Value, out var pending) &&
                    pending.Items.Any(item => IsOutstandingPaperOrderUpdate(item, request)))
                {
                    return node;
                }
            }
        }

        return null;
    }

    private bool HasPendingDrainMatch(LinkedList<MarketDataSideEffectWorkItem> items)
    {
        return paperOrderDrainRequests.Any(request =>
            items.Any(item => IsOutstandingPaperOrderUpdate(item, request)));
    }

    private int CountOutstandingPaperOrderUpdates(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc)
    {
        var count = IsOutstandingPaperOrderUpdate(
            inFlightUpdate,
            paperOrderId,
            assetId,
            conditionId,
            acceptedAfterUtc,
            expiresBeforeUtc)
            ? 1
            : 0;

        var assetKey = "asset:" + assetId.Trim();
        if (pendingUpdatesByAsset.TryGetValue(assetKey, out var pending))
        {
            count += pending.Items.Count(item => IsOutstandingPaperOrderUpdate(
                item,
                paperOrderId,
                assetId,
                conditionId,
                acceptedAfterUtc,
                expiresBeforeUtc));
        }

        return count;
    }

    private static bool IsOutstandingPaperOrderUpdate(
        MarketDataSideEffectWorkItem? workItem,
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc)
    {
        if (workItem is null ||
            workItem.ReceivedAtUtc <= acceptedAfterUtc ||
            workItem.ReceivedAtUtc >= expiresBeforeUtc ||
            !string.Equals(workItem.Update.AssetId, assetId, StringComparison.Ordinal) ||
            !string.Equals(workItem.Update.ConditionId, conditionId, StringComparison.Ordinal) ||
            workItem.Update.EventType is not (
                MarketDataEventType.Book or
                MarketDataEventType.PriceChange or
                MarketDataEventType.LastTradePrice or
                MarketDataEventType.BestBidAsk))
        {
            return false;
        }

        return workItem.EligiblePaperOrderIds is null ||
            workItem.EligiblePaperOrderIds.Contains(paperOrderId);
    }

    private static bool IsOutstandingPaperOrderUpdate(
        MarketDataSideEffectWorkItem workItem,
        PaperOrderDrainRequest request)
    {
        return IsOutstandingPaperOrderUpdate(
            workItem,
            request.PaperOrderId,
            request.AssetId,
            request.ConditionId,
            request.AcceptedAfterUtc,
            request.ExpiresBeforeUtc);
    }

    private bool IsReplaceable(MarketDataUpdate update, IReadOnlySet<Guid>? eligiblePaperOrderIds)
    {
        return !options.PersistOrderBookSnapshots &&
            !options.PersistMarketDataEvents &&
            eligiblePaperOrderIds is { Count: 0 } &&
            !update.MarketResolved &&
            (update.EventType is MarketDataEventType.Book or MarketDataEventType.PriceChange or MarketDataEventType.BestBidAsk) &&
            update.OrderBookSnapshot?.BestBid is not null;
    }

    private static string GetAssetKey(string component, MarketDataUpdate update)
    {
        if (!string.IsNullOrWhiteSpace(update.AssetId))
        {
            return "asset:" + update.AssetId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(update.ConditionId))
        {
            return "condition:" + update.ConditionId.Trim();
        }

        return "component:" + component.Trim();
    }

    private static LinkedListNode<MarketDataSideEffectWorkItem>? FindReplaceableNodeAfterResolution(
        LinkedList<MarketDataSideEffectWorkItem> items)
    {
        for (var node = items.Last; node is not null; node = node.Previous)
        {
            if (node.Value.Update.MarketResolved || node.Value.Update.EventType == MarketDataEventType.MarketResolved)
            {
                return null;
            }

            if (node.Value.Replaceable)
            {
                return node;
            }
        }

        return null;
    }

    private static LinkedListNode<DiagnosticWorkItem>? FindFirstOrdinaryDiagnostic(
        LinkedList<DiagnosticWorkItem> items)
    {
        for (var node = items.First; node is not null; node = node.Next)
        {
            if (!node.Value.Important)
            {
                return node;
            }
        }

        return null;
    }

    private sealed class PendingAssetUpdates
    {
        public LinkedList<MarketDataSideEffectWorkItem> Items { get; } = [];

        public bool Scheduled { get; set; }
    }

    private sealed record PaperOrderDrainRequest(
        Guid PaperOrderId,
        string AssetId,
        string ConditionId,
        DateTimeOffset AcceptedAfterUtc,
        DateTimeOffset ExpiresBeforeUtc,
        TaskCompletionSource<bool> Completion);

    private sealed record DiagnosticWorkItem(
        MarketWebSocketFrameDiagnostic? FrameDiagnostic,
        ApiError? ApiError,
        bool Important);
}
