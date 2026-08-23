using System.Diagnostics;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.MarketData;

internal sealed record MakerGtdPaperMarketDataQueueMetrics(
    int PendingUpdates,
    int InFlightUpdates,
    int TrackedAssets,
    long EnqueuedUpdates,
    long RejectedUpdates,
    long ProcessedUpdates,
    long FailedUpdates);

internal sealed class MakerGtdPaperMarketDataQueue(
    ILogger logger,
    MarketDataWebSocketOptions options,
    IPaperTradingMarketDataUpdater? updater,
    IAppRepository repository,
    IMakerGtdPaperPlacementHandoff makerGtdHandoff)
{
    private const string ComponentName = "MakerGtdPaperMarketDataQueue";
    private readonly object sync = new();
    private readonly Dictionary<string, PendingAssetUpdates> pendingUpdatesByAsset = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> readyAssetKeys = [];
    private readonly List<PaperOrderDrainRequest> drainRequests = [];
    private readonly SemaphoreSlim signal = new(0);
    private MarketDataSideEffectWorkItem? inFlightUpdate;
    private Task? workerTask;
    private bool accepting;
    private int pendingUpdateCount;
    private long enqueuedUpdates;
    private long rejectedUpdates;
    private long processedUpdates;
    private long failedUpdates;

    public Task StartAsync()
    {
        lock (sync)
        {
            if (workerTask is not null)
            {
                return Task.CompletedTask;
            }

            accepting = true;
            workerTask = Task.Run(RunWorkerAsync, CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAndDrainAsync()
    {
        Task? completion;
        lock (sync)
        {
            accepting = false;
            completion = workerTask;
        }

        signal.Release();
        if (completion is not null)
        {
            await completion.ConfigureAwait(false);
        }
    }

    public MarketDataSideEffectEnqueueOutcome Enqueue(MarketDataSideEffectWorkItem workItem)
    {
        if (updater is null ||
            workItem.EligiblePaperOrderIds is not { Count: > 0 } ||
            workItem.Update.EventType is not (
                MarketDataEventType.Book or
                MarketDataEventType.PriceChange or
                MarketDataEventType.LastTradePrice or
                MarketDataEventType.BestBidAsk))
        {
            Interlocked.Increment(ref rejectedUpdates);
            return MarketDataSideEffectEnqueueOutcome.Rejected;
        }

        var assetKey = GetAssetKey(workItem);
        var shouldSignal = false;
        lock (sync)
        {
            if (!accepting)
            {
                Interlocked.Increment(ref rejectedUpdates);
                return MarketDataSideEffectEnqueueOutcome.Rejected;
            }

            if (!pendingUpdatesByAsset.TryGetValue(assetKey, out var pending))
            {
                pending = new PendingAssetUpdates();
                pendingUpdatesByAsset.Add(assetKey, pending);
            }

            pending.Items.AddLast(workItem);
            pendingUpdateCount++;
            Interlocked.Increment(ref enqueuedUpdates);
            if (!pending.Scheduled)
            {
                pending.Scheduled = true;
                readyAssetKeys.AddLast(assetKey);
                shouldSignal = true;
            }
        }

        if (shouldSignal)
        {
            signal.Release();
        }

        return MarketDataSideEffectEnqueueOutcome.Enqueued;
    }

    public MakerGtdPaperMarketDataQueueMetrics GetMetrics()
    {
        lock (sync)
        {
            return new MakerGtdPaperMarketDataQueueMetrics(
                pendingUpdateCount,
                inFlightUpdate is null ? 0 : 1,
                pendingUpdatesByAsset.Count,
                Interlocked.Read(ref enqueuedUpdates),
                Interlocked.Read(ref rejectedUpdates),
                Interlocked.Read(ref processedUpdates),
                Interlocked.Read(ref failedUpdates));
        }
    }

    public bool HasOutstanding(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc)
    {
        if (!IsValidEvidenceRange(paperOrderId, assetId, conditionId, acceptedAfterUtc, expiresBeforeUtc))
        {
            return false;
        }

        lock (sync)
        {
            return CountOutstanding(
                paperOrderId,
                assetId,
                conditionId,
                acceptedAfterUtc,
                expiresBeforeUtc) > 0;
        }
    }

    public MarketDataSideEffectPreflightSnapshot GetPreflightSnapshot(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc,
        DateTimeOffset capturedAtUtc)
    {
        if (!IsValidEvidenceRange(paperOrderId, assetId, conditionId, acceptedAfterUtc, expiresBeforeUtc))
        {
            return MarketDataSideEffectPreflightSnapshot.NotAvailable(capturedAtUtc, "invalid_order_evidence");
        }

        lock (sync)
        {
            var matchingInFlight = IsOutstanding(
                inFlightUpdate,
                paperOrderId,
                assetId,
                conditionId,
                acceptedAfterUtc,
                expiresBeforeUtc)
                    ? 1
                    : 0;
            var matchingPendingCount = 0;
            DateTimeOffset? oldestReceivedAtUtc = matchingInFlight == 1 ? inFlightUpdate!.ReceivedAtUtc : null;
            DateTimeOffset? oldestEnqueuedAtUtc = matchingInFlight == 1 ? inFlightUpdate!.EnqueuedAtUtc : null;
            var assetKey = "asset:" + assetId.Trim();
            if (pendingUpdatesByAsset.TryGetValue(assetKey, out var pending))
            {
                foreach (var item in pending.Items)
                {
                    if (!IsOutstanding(item, paperOrderId, assetId, conditionId, acceptedAfterUtc, expiresBeforeUtc))
                    {
                        continue;
                    }

                    matchingPendingCount++;
                    oldestReceivedAtUtc = Oldest(oldestReceivedAtUtc, item.ReceivedAtUtc);
                    oldestEnqueuedAtUtc = Oldest(oldestEnqueuedAtUtc, item.EnqueuedAtUtc);
                }
            }

            return new MarketDataSideEffectPreflightSnapshot(
                MarketDataSideEffectDiagnosticSchema.Available,
                null,
                capturedAtUtc,
                matchingInFlight + matchingPendingCount,
                matchingInFlight,
                matchingPendingCount,
                oldestReceivedAtUtc,
                MarketDataSideEffectPreflightSnapshot.AgeMilliseconds(capturedAtUtc, oldestReceivedAtUtc),
                oldestEnqueuedAtUtc,
                MarketDataSideEffectPreflightSnapshot.AgeMilliseconds(capturedAtUtc, oldestEnqueuedAtUtc),
                pendingUpdateCount,
                pendingUpdatesByAsset.Count,
                workerTask?.Status.ToString() ?? MarketDataSideEffectDiagnosticSchema.NotAvailable,
                inFlightUpdate?.ExecutionTrace?.Capture(capturedAtUtc));
        }
    }

    public async Task DrainAsync(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc,
        CancellationToken cancellationToken)
    {
        if (!IsValidEvidenceRange(paperOrderId, assetId, conditionId, acceptedAfterUtc, expiresBeforeUtc))
        {
            return;
        }

        PaperOrderDrainRequest request;
        lock (sync)
        {
            if (CountOutstanding(paperOrderId, assetId, conditionId, acceptedAfterUtc, expiresBeforeUtc) == 0)
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
            drainRequests.Add(request);
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
            int remaining;
            lock (sync)
            {
                drainRequests.Remove(request);
                remaining = CountOutstanding(
                    paperOrderId,
                    assetId,
                    conditionId,
                    acceptedAfterUtc,
                    expiresBeforeUtc);
            }

            var duration = Stopwatch.GetElapsedTime(waitStarted);
            if (duration.TotalMilliseconds >= options.SideEffectSlowProcessingMilliseconds)
            {
                logger.LogWarning(
                    "Dedicated Maker-GTD evidence drain was slow. PaperOrderId={PaperOrderId} AssetId={AssetId} WaitDurationMs={WaitDurationMs} RemainingMatchingUpdates={RemainingMatchingUpdates} Canceled={Canceled}",
                    paperOrderId,
                    assetId,
                    duration.TotalMilliseconds,
                    remaining,
                    canceled);
            }
        }
    }

    private async Task RunWorkerAsync()
    {
        try
        {
            while (true)
            {
                await signal.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                if (!TryTake(out var workItem))
                {
                    lock (sync)
                    {
                        if (!accepting && pendingUpdateCount == 0)
                        {
                            return;
                        }
                    }

                    continue;
                }

                await ProcessAsync(workItem).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Dedicated Maker-GTD evidence worker stopped unexpectedly.");
            throw;
        }
    }

    private bool TryTake(out MarketDataSideEffectWorkItem workItem)
    {
        var shouldSignal = false;
        lock (sync)
        {
            while (readyAssetKeys.Count > 0)
            {
                var readyNode = FindPriorityReadyAssetNode() ?? readyAssetKeys.First!;
                var assetKey = readyNode.Value;
                readyAssetKeys.Remove(readyNode);
                if (!pendingUpdatesByAsset.TryGetValue(assetKey, out var pending) ||
                    pending.Items.First is not { } first)
                {
                    continue;
                }

                pending.Scheduled = false;
                workItem = first.Value;
                pending.Items.RemoveFirst();
                pendingUpdateCount--;
                inFlightUpdate = workItem;
                workItem.ExecutionTrace?.MarkProcessingStarted(DateTimeOffset.UtcNow);

                if (pending.Items.Count > 0)
                {
                    pending.Scheduled = true;
                    if (HasPendingDrainMatch(pending.Items))
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
                    signal.Release();
                }

                return true;
            }
        }

        workItem = default!;
        return false;
    }

    private async Task ProcessAsync(MarketDataSideEffectWorkItem workItem)
    {
        var processingStarted = Stopwatch.GetTimestamp();
        var queueDelay = DateTimeOffset.UtcNow - workItem.EnqueuedAtUtc;
        if (queueDelay < TimeSpan.Zero)
        {
            queueDelay = TimeSpan.Zero;
        }

        try
        {
            await updater!.ApplyMakerGtdUpdateAsync(
                workItem.Update,
                workItem.ReceivedAtUtc,
                workItem.EligiblePaperOrderIds!,
                CancellationToken.None,
                workItem.ExecutionTrace).ConfigureAwait(false);
            Interlocked.Increment(ref processedUpdates);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref failedUpdates);
            makerGtdHandoff.RecordMarketDataFailure(
                workItem.Update.AssetId,
                workItem.Update.ConditionId,
                workItem.ReceivedAtUtc,
                workItem.EligiblePaperOrderIds,
                MakerGtdPaperExecutionContract.MarketDataHandlerFailureCode);
            logger.LogError(
                ex,
                "Dedicated Maker-GTD evidence processing failed. EventType={EventType} AssetId={AssetId} QueueDelayMs={QueueDelayMs}",
                workItem.Update.EventType,
                workItem.Update.AssetId,
                queueDelay.TotalMilliseconds);
            await TryRecordApiErrorAsync(
                "ProcessMakerGtdUpdate",
                $"EventType={workItem.Update.EventType}; AssetId={workItem.Update.AssetId ?? "<null>"}; QueueDelayMs={queueDelay.TotalMilliseconds:F0}; Error={ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            CompleteInFlight(workItem);
        }

        var processingDuration = Stopwatch.GetElapsedTime(processingStarted);
        if (queueDelay.TotalMilliseconds >= options.SideEffectSlowProcessingMilliseconds ||
            processingDuration.TotalMilliseconds >= options.SideEffectSlowProcessingMilliseconds)
        {
            logger.LogWarning(
                "Dedicated Maker-GTD evidence processing was slow. EventType={EventType} AssetId={AssetId} QueueDelayMs={QueueDelayMs} ProcessingDurationMs={ProcessingDurationMs} PendingMakerUpdates={PendingMakerUpdates}",
                workItem.Update.EventType,
                workItem.Update.AssetId,
                queueDelay.TotalMilliseconds,
                processingDuration.TotalMilliseconds,
                GetMetrics().PendingUpdates);
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
            logger.LogError(ex, "Failed to persist dedicated Maker-GTD queue API error for {Operation}.", operation);
        }
    }

    private void CompleteInFlight(MarketDataSideEffectWorkItem workItem)
    {
        lock (sync)
        {
            if (ReferenceEquals(inFlightUpdate, workItem))
            {
                inFlightUpdate = null;
            }

            for (var index = drainRequests.Count - 1; index >= 0; index--)
            {
                var request = drainRequests[index];
                if (CountOutstanding(
                        request.PaperOrderId,
                        request.AssetId,
                        request.ConditionId,
                        request.AcceptedAfterUtc,
                        request.ExpiresBeforeUtc) == 0)
                {
                    drainRequests.RemoveAt(index);
                    request.Completion.TrySetResult(true);
                }
            }
        }
    }

    private LinkedListNode<string>? FindPriorityReadyAssetNode()
    {
        foreach (var request in drainRequests)
        {
            for (var node = readyAssetKeys.First; node is not null; node = node.Next)
            {
                if (pendingUpdatesByAsset.TryGetValue(node.Value, out var pending) &&
                    pending.Items.Any(item => IsOutstanding(item, request)))
                {
                    return node;
                }
            }
        }

        return null;
    }

    private bool HasPendingDrainMatch(LinkedList<MarketDataSideEffectWorkItem> items)
    {
        return drainRequests.Any(request => items.Any(item => IsOutstanding(item, request)));
    }

    private int CountOutstanding(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc)
    {
        var count = IsOutstanding(
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
            count += pending.Items.Count(item => IsOutstanding(
                item,
                paperOrderId,
                assetId,
                conditionId,
                acceptedAfterUtc,
                expiresBeforeUtc));
        }

        return count;
    }

    private static bool IsOutstanding(MarketDataSideEffectWorkItem item, PaperOrderDrainRequest request)
    {
        return IsOutstanding(
            item,
            request.PaperOrderId,
            request.AssetId,
            request.ConditionId,
            request.AcceptedAfterUtc,
            request.ExpiresBeforeUtc);
    }

    private static bool IsOutstanding(
        MarketDataSideEffectWorkItem? item,
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc)
    {
        return item is not null &&
            item.ReceivedAtUtc > acceptedAfterUtc &&
            item.ReceivedAtUtc < expiresBeforeUtc &&
            string.Equals(item.Update.AssetId, assetId, StringComparison.Ordinal) &&
            string.Equals(item.Update.ConditionId, conditionId, StringComparison.Ordinal) &&
            item.EligiblePaperOrderIds is { } ids &&
            ids.Contains(paperOrderId);
    }

    private static bool IsValidEvidenceRange(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc)
    {
        return paperOrderId != Guid.Empty &&
            !string.IsNullOrWhiteSpace(assetId) &&
            !string.IsNullOrWhiteSpace(conditionId) &&
            acceptedAfterUtc < expiresBeforeUtc;
    }

    private static DateTimeOffset Oldest(DateTimeOffset? current, DateTimeOffset candidate)
    {
        return current is null || candidate < current.Value ? candidate : current.Value;
    }

    private static string GetAssetKey(MarketDataSideEffectWorkItem workItem)
    {
        return !string.IsNullOrWhiteSpace(workItem.Update.AssetId)
            ? "asset:" + workItem.Update.AssetId.Trim()
            : "condition:" + (workItem.Update.ConditionId ?? workItem.Component).Trim();
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
}
