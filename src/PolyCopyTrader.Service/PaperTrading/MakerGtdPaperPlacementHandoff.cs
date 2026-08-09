using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.PaperTrading;

public interface IMakerGtdPaperPlacementHandoff
{
    ValueTask<IMakerGtdPaperPlacementAdmission> EnterPlacementAdmissionAsync(
        string assetId,
        CancellationToken cancellationToken = default);

    ValueTask<IAsyncDisposable> EnterMarketDataAdmissionAsync(
        string assetId,
        CancellationToken cancellationToken = default);

    ValueTask<IAsyncDisposable> EnterMarketDataReceiptAsync(
        CancellationToken cancellationToken = default);

    ValueTask<IAsyncDisposable?> TryEnterExpiryAdmissionAsync(
        CancellationToken cancellationToken = default);

    IReadOnlySet<Guid> GetPendingOrderIds(string assetId);

    Task WaitForPublicationAsync(
        IReadOnlySet<Guid>? eligiblePaperOrderIds,
        CancellationToken cancellationToken = default);

    void MarkPublished(Guid paperOrderId);

    void MarkFailed(Guid paperOrderId);

    void TrackMakerGtdPaperOrder(Guid paperOrderId, string executionSource);

    void RecordMarketDataFailure(
        string? assetId,
        string? conditionId,
        DateTimeOffset receivedAtUtc,
        IReadOnlySet<Guid>? affectedPaperOrderIds,
        string failureCode);

    bool TryGetMarketDataFailure(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc,
        out MakerGtdPaperMarketDataFailure? failure);

    void ClearMarketDataFailures(Guid paperOrderId);
}

public sealed record MakerGtdPaperMarketDataFailure(
    string? AssetId,
    string? ConditionId,
    DateTimeOffset ReceivedAtUtc,
    string FailureCode);

public interface IMakerGtdPaperPlacementAdmission : IAsyncDisposable
{
    void ActivatePendingOrder(Guid paperOrderId, string executionSource);
}

public sealed class MakerGtdPaperPlacementHandoff : IMakerGtdPaperPlacementHandoff
{
    private const int AdmissionStripeCount = 256;
    private const int MaximumTrackedOrderFailures = 16384;
    private const int MaximumUnattributedFailures = 1024;
    private static readonly IReadOnlySet<Guid> EmptyOrderIds = new HashSet<Guid>();
    private readonly SemaphoreSlim[] admissionGates = Enumerable.Range(0, AdmissionStripeCount)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();
    private readonly object pendingSync = new();
    private readonly object failureSync = new();
    private readonly SemaphoreSlim receiptExpiryGate = new(1, 1);
    private readonly Dictionary<Guid, PendingOrder> pendingOrders = [];
    private readonly Dictionary<string, HashSet<Guid>> pendingOrderIdsByAsset = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, List<MakerGtdPaperMarketDataFailure>> failuresByOrderId = [];
    private readonly HashSet<Guid> knownMakerGtdPaperOrderIds = [];
    private readonly List<MakerGtdPaperMarketDataFailure> unattributedFailures = [];
    private DateTimeOffset? failureHistoryIncompleteFromUtc;
    private DateTimeOffset? failureHistoryIncompleteThroughUtc;
    private int activeMarketDataReceipts;
    private int trackedOrderFailureCount;

    public async ValueTask<IMakerGtdPaperPlacementAdmission> EnterPlacementAdmissionAsync(
        string assetId,
        CancellationToken cancellationToken = default)
    {
        var normalizedAssetId = NormalizeAssetId(assetId);
        var admissionGate = GetAdmissionGate(normalizedAssetId);
        await admissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new PlacementAdmission(this, normalizedAssetId, admissionGate);
    }

    private void ActivatePendingOrderUnderAdmission(
        Guid paperOrderId,
        string assetId,
        string executionSource)
    {
        if (paperOrderId == Guid.Empty)
        {
            throw new ArgumentException("Maker-GTD pending Paper order ID is required.", nameof(paperOrderId));
        }

        if (!MakerGtdPaperExecutionSources.IsSupported(executionSource))
        {
            throw new ArgumentException("Maker-GTD pending execution source is invalid.", nameof(executionSource));
        }

        lock (pendingSync)
        {
            if (pendingOrders.ContainsKey(paperOrderId))
            {
                throw new InvalidOperationException(
                    $"Maker-GTD pending Paper order '{paperOrderId:D}' is already active.");
            }

            var pendingOrder = new PendingOrder(
                paperOrderId,
                assetId,
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            pendingOrders.Add(paperOrderId, pendingOrder);
            if (!pendingOrderIdsByAsset.TryGetValue(assetId, out var assetOrderIds))
            {
                assetOrderIds = [];
                pendingOrderIdsByAsset.Add(assetId, assetOrderIds);
            }

            assetOrderIds.Add(paperOrderId);
            TrackMakerGtdPaperOrder(paperOrderId, executionSource);
        }
    }

    public async ValueTask<IAsyncDisposable> EnterMarketDataAdmissionAsync(
        string assetId,
        CancellationToken cancellationToken = default)
    {
        var normalizedAssetId = NormalizeAssetId(assetId);
        var admissionGate = GetAdmissionGate(normalizedAssetId);
        await admissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new AdmissionLease(admissionGate);
    }

    public async ValueTask<IAsyncDisposable> EnterMarketDataReceiptAsync(
        CancellationToken cancellationToken = default)
    {
        await receiptExpiryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Interlocked.Increment(ref activeMarketDataReceipts);
        }
        finally
        {
            receiptExpiryGate.Release();
        }

        return new MarketDataReceiptLease(this);
    }

    public async ValueTask<IAsyncDisposable?> TryEnterExpiryAdmissionAsync(
        CancellationToken cancellationToken = default)
    {
        await receiptExpiryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (activeMarketDataReceipts > 0)
        {
            receiptExpiryGate.Release();
            return null;
        }

        return new AdmissionLease(receiptExpiryGate);
    }

    public IReadOnlySet<Guid> GetPendingOrderIds(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return EmptyOrderIds;
        }

        var normalizedAssetId = assetId.Trim();
        lock (pendingSync)
        {
            return pendingOrderIdsByAsset.TryGetValue(normalizedAssetId, out var orderIds)
                ? orderIds.ToHashSet()
                : EmptyOrderIds;
        }
    }

    public async Task WaitForPublicationAsync(
        IReadOnlySet<Guid>? eligiblePaperOrderIds,
        CancellationToken cancellationToken = default)
    {
        if (eligiblePaperOrderIds is not { Count: > 0 })
        {
            return;
        }

        Task[] publicationTasks;
        lock (pendingSync)
        {
            publicationTasks = eligiblePaperOrderIds
                .Where(pendingOrders.ContainsKey)
                .Select(orderId => pendingOrders[orderId].Publication.Task)
                .Distinct()
                .ToArray();
        }

        if (publicationTasks.Length > 0)
        {
            await Task.WhenAll(publicationTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void MarkPublished(Guid paperOrderId)
    {
        CompletePendingOrder(paperOrderId);
    }

    public void MarkFailed(Guid paperOrderId)
    {
        CompletePendingOrder(paperOrderId);
        ClearMarketDataFailures(paperOrderId);
    }

    public void TrackMakerGtdPaperOrder(Guid paperOrderId, string executionSource)
    {
        if (paperOrderId == Guid.Empty ||
            !MakerGtdPaperExecutionSources.IsSupported(executionSource))
        {
            return;
        }

        lock (failureSync)
        {
            knownMakerGtdPaperOrderIds.Add(paperOrderId);
        }
    }

    public void RecordMarketDataFailure(
        string? assetId,
        string? conditionId,
        DateTimeOffset receivedAtUtc,
        IReadOnlySet<Guid>? affectedPaperOrderIds,
        string failureCode)
    {
        if (receivedAtUtc == default || string.IsNullOrWhiteSpace(failureCode))
        {
            return;
        }

        if (affectedPaperOrderIds is { Count: 0 })
        {
            return;
        }

        var normalizedAssetId = NormalizeOptionalIdentifier(assetId);
        var normalizedConditionId = NormalizeOptionalIdentifier(conditionId);
        if (affectedPaperOrderIds is null &&
            normalizedAssetId is null &&
            normalizedConditionId is null &&
            Volatile.Read(ref activeMarketDataReceipts) == 0)
        {
            return;
        }

        var failure = new MakerGtdPaperMarketDataFailure(
            normalizedAssetId,
            normalizedConditionId,
            receivedAtUtc,
            failureCode.Trim());
        lock (failureSync)
        {
            if (affectedPaperOrderIds is null)
            {
                AddUnattributedFailure(failure);
                return;
            }

            foreach (var paperOrderId in affectedPaperOrderIds.Where(id => id != Guid.Empty))
            {
                if (!knownMakerGtdPaperOrderIds.Contains(paperOrderId))
                {
                    continue;
                }

                if (!failuresByOrderId.TryGetValue(paperOrderId, out var orderFailures))
                {
                    if (trackedOrderFailureCount >= MaximumTrackedOrderFailures)
                    {
                        MarkFailureHistoryIncomplete(failure.ReceivedAtUtc, failure.ReceivedAtUtc);
                        continue;
                    }

                    orderFailures = [];
                    failuresByOrderId.Add(paperOrderId, orderFailures);
                }

                if (orderFailures.Contains(failure))
                {
                    continue;
                }

                if (trackedOrderFailureCount >= MaximumTrackedOrderFailures)
                {
                    MarkFailureHistoryIncomplete(failure.ReceivedAtUtc, failure.ReceivedAtUtc);
                    continue;
                }

                orderFailures.Add(failure);
                trackedOrderFailureCount++;
            }
        }
    }

    public bool TryGetMarketDataFailure(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc,
        out MakerGtdPaperMarketDataFailure? failure)
    {
        failure = null;
        if (paperOrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(assetId) ||
            string.IsNullOrWhiteSpace(conditionId) ||
            acceptedAfterUtc >= expiresBeforeUtc)
        {
            return false;
        }

        var normalizedAssetId = assetId.Trim();
        var normalizedConditionId = conditionId.Trim();
        lock (failureSync)
        {
            if (failuresByOrderId.TryGetValue(paperOrderId, out var orderFailures))
            {
                failure = orderFailures
                    .Where(candidate => FailureMatches(
                        candidate,
                        normalizedAssetId,
                        normalizedConditionId,
                        acceptedAfterUtc,
                        expiresBeforeUtc))
                    .OrderBy(candidate => candidate.ReceivedAtUtc)
                    .FirstOrDefault();
                if (failure is not null)
                {
                    return true;
                }
            }

            failure = unattributedFailures
                .Where(candidate => FailureMatches(
                    candidate,
                    normalizedAssetId,
                    normalizedConditionId,
                    acceptedAfterUtc,
                    expiresBeforeUtc))
                .OrderBy(candidate => candidate.ReceivedAtUtc)
                .FirstOrDefault();
            if (failure is not null)
            {
                return true;
            }

            if (failureHistoryIncompleteFromUtc is { } incompleteFromUtc &&
                failureHistoryIncompleteThroughUtc is { } incompleteThroughUtc &&
                incompleteFromUtc < expiresBeforeUtc &&
                incompleteThroughUtc > acceptedAfterUtc)
            {
                failure = new MakerGtdPaperMarketDataFailure(
                    null,
                    null,
                    incompleteFromUtc,
                    MakerGtdPaperExecutionContract.MarketDataFailureHistoryIncompleteCode);
                return true;
            }

            return false;
        }
    }

    public void ClearMarketDataFailures(Guid paperOrderId)
    {
        if (paperOrderId == Guid.Empty)
        {
            return;
        }

        lock (failureSync)
        {
            knownMakerGtdPaperOrderIds.Remove(paperOrderId);
            if (failuresByOrderId.Remove(paperOrderId, out var removedFailures))
            {
                trackedOrderFailureCount -= removedFailures.Count;
            }
        }
    }

    private void CompletePendingOrder(Guid paperOrderId)
    {
        PendingOrder? pendingOrder;
        lock (pendingSync)
        {
            if (!pendingOrders.Remove(paperOrderId, out pendingOrder))
            {
                return;
            }

            if (pendingOrderIdsByAsset.TryGetValue(pendingOrder.AssetId, out var assetOrderIds))
            {
                assetOrderIds.Remove(paperOrderId);
                if (assetOrderIds.Count == 0)
                {
                    pendingOrderIdsByAsset.Remove(pendingOrder.AssetId);
                }
            }
        }

        pendingOrder.Publication.TrySetResult(true);
    }

    private SemaphoreSlim GetAdmissionGate(string assetId)
    {
        var hash = StringComparer.Ordinal.GetHashCode(assetId) & int.MaxValue;
        return admissionGates[hash % admissionGates.Length];
    }

    private static string NormalizeAssetId(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            throw new ArgumentException("Maker-GTD asset ID is required.", nameof(assetId));
        }

        return assetId.Trim();
    }

    private async ValueTask CompleteMarketDataReceiptAsync()
    {
        await receiptExpiryGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (activeMarketDataReceipts > 0)
            {
                Interlocked.Decrement(ref activeMarketDataReceipts);
            }
        }
        finally
        {
            receiptExpiryGate.Release();
        }
    }

    private void AddUnattributedFailure(MakerGtdPaperMarketDataFailure failure)
    {
        if (unattributedFailures.Count >= MaximumUnattributedFailures)
        {
            MarkFailureHistoryIncomplete(
                unattributedFailures.Min(candidate => candidate.ReceivedAtUtc),
                unattributedFailures.Max(candidate => candidate.ReceivedAtUtc));
            unattributedFailures.Clear();
        }

        unattributedFailures.Add(failure);
    }

    private void MarkFailureHistoryIncomplete(
        DateTimeOffset fromUtc,
        DateTimeOffset throughUtc)
    {
        failureHistoryIncompleteFromUtc = failureHistoryIncompleteFromUtc is { } current
            ? DateTimeOffset.Compare(current, fromUtc) <= 0 ? current : fromUtc
            : fromUtc;
        failureHistoryIncompleteThroughUtc = failureHistoryIncompleteThroughUtc is { } currentThrough
            ? DateTimeOffset.Compare(currentThrough, throughUtc) >= 0 ? currentThrough : throughUtc
            : throughUtc;
    }

    private static bool FailureMatches(
        MakerGtdPaperMarketDataFailure failure,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc)
    {
        return failure.ReceivedAtUtc > acceptedAfterUtc &&
            failure.ReceivedAtUtc < expiresBeforeUtc &&
            (failure.AssetId is null || string.Equals(failure.AssetId, assetId, StringComparison.Ordinal)) &&
            (failure.ConditionId is null || string.Equals(failure.ConditionId, conditionId, StringComparison.Ordinal));
    }

    private static string? NormalizeOptionalIdentifier(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record PendingOrder(
        Guid PaperOrderId,
        string AssetId,
        TaskCompletionSource<bool> Publication);

    private sealed class AdmissionLease(SemaphoreSlim admissionGate) : IAsyncDisposable
    {
        private SemaphoreSlim? gate = admissionGate;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref gate, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MarketDataReceiptLease(MakerGtdPaperPlacementHandoff owner) : IAsyncDisposable
    {
        private MakerGtdPaperPlacementHandoff? currentOwner = owner;

        public ValueTask DisposeAsync()
        {
            var capturedOwner = Interlocked.Exchange(ref currentOwner, null);
            return capturedOwner is null
                ? ValueTask.CompletedTask
                : capturedOwner.CompleteMarketDataReceiptAsync();
        }
    }

    private sealed class PlacementAdmission(
        MakerGtdPaperPlacementHandoff owner,
        string assetId,
        SemaphoreSlim admissionGate) : IMakerGtdPaperPlacementAdmission
    {
        private readonly AdmissionLease lease = new(admissionGate);
        private int activated;

        public void ActivatePendingOrder(Guid paperOrderId, string executionSource)
        {
            if (Interlocked.Exchange(ref activated, 1) != 0)
            {
                throw new InvalidOperationException("Maker-GTD placement admission already activated an order.");
            }

            owner.ActivatePendingOrderUnderAdmission(paperOrderId, assetId, executionSource);
        }

        public ValueTask DisposeAsync()
        {
            return lease.DisposeAsync();
        }
    }
}

internal sealed class NoOpMakerGtdPaperPlacementHandoff : IMakerGtdPaperPlacementHandoff
{
    public static NoOpMakerGtdPaperPlacementHandoff Instance { get; } = new();

    private NoOpMakerGtdPaperPlacementHandoff()
    {
    }

    public ValueTask<IMakerGtdPaperPlacementAdmission> EnterPlacementAdmissionAsync(
        string assetId,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IMakerGtdPaperPlacementAdmission>(NoOpPlacementAdmission.Instance);
    }

    public ValueTask<IAsyncDisposable> EnterMarketDataAdmissionAsync(
        string assetId,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IAsyncDisposable>(NoOpLease.Instance);
    }

    public ValueTask<IAsyncDisposable> EnterMarketDataReceiptAsync(
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IAsyncDisposable>(NoOpLease.Instance);
    }

    public ValueTask<IAsyncDisposable?> TryEnterExpiryAdmissionAsync(
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IAsyncDisposable?>(NoOpLease.Instance);
    }

    public IReadOnlySet<Guid> GetPendingOrderIds(string assetId)
    {
        return new HashSet<Guid>();
    }

    public Task WaitForPublicationAsync(
        IReadOnlySet<Guid>? eligiblePaperOrderIds,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public void MarkPublished(Guid paperOrderId)
    {
    }

    public void MarkFailed(Guid paperOrderId)
    {
    }

    public void TrackMakerGtdPaperOrder(Guid paperOrderId, string executionSource)
    {
    }

    public void RecordMarketDataFailure(
        string? assetId,
        string? conditionId,
        DateTimeOffset receivedAtUtc,
        IReadOnlySet<Guid>? affectedPaperOrderIds,
        string failureCode)
    {
    }

    public bool TryGetMarketDataFailure(
        Guid paperOrderId,
        string assetId,
        string conditionId,
        DateTimeOffset acceptedAfterUtc,
        DateTimeOffset expiresBeforeUtc,
        out MakerGtdPaperMarketDataFailure? failure)
    {
        failure = null;
        return false;
    }

    public void ClearMarketDataFailures(Guid paperOrderId)
    {
    }

    private sealed class NoOpLease : IAsyncDisposable
    {
        public static NoOpLease Instance { get; } = new();

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpPlacementAdmission : IMakerGtdPaperPlacementAdmission
    {
        public static NoOpPlacementAdmission Instance { get; } = new();

        public void ActivatePendingOrder(Guid paperOrderId, string executionSource)
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
