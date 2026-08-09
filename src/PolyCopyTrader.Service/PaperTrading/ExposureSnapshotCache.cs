using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class ExposureSnapshotCache(
    IAppRepository repository,
    IMakerGtdPaperPlacementHandoff? makerGtdPaperPlacementHandoff = null) : IExposureSnapshotCache
{
    private static readonly IReadOnlySet<Guid> EmptyPaperOrderIds = new HashSet<Guid>();
    private readonly object updateSync = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly IMakerGtdPaperPlacementHandoff makerGtdHandoff =
        makerGtdPaperPlacementHandoff ?? NoOpMakerGtdPaperPlacementHandoff.Instance;
    private readonly Dictionary<Guid, PaperOrder> paperOrderRefreshOverlay = [];
    private readonly Dictionary<PaperPositionKey, PaperPosition> paperPositionRefreshOverlay = [];
    private readonly Dictionary<Guid, LiveOrder> liveOrderRefreshOverlay = [];
    private Dictionary<PaperPositionKey, int> paperPositionIndexes = [];
    private Dictionary<string, HashSet<Guid>> openPaperOrderIdsByAsset = new(StringComparer.OrdinalIgnoreCase);
    private bool refreshInProgress;
    private TradingExposureSnapshot? snapshot;

    public async Task<TradingExposureSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var current = Volatile.Read(ref snapshot);
        if (current is null)
        {
            await EnsureInitializedAsync(cancellationToken);
            current = Volatile.Read(ref snapshot);
        }

        return current ?? new TradingExposureSnapshot([], [], [], DateTimeOffset.MinValue);
    }

    public PaperPosition? GetPaperPosition(string copiedTraderWallet, string assetId)
    {
        lock (updateSync)
        {
            var current = Volatile.Read(ref snapshot);
            if (current is null)
            {
                return null;
            }

            var key = PaperPositionKey.From(copiedTraderWallet, assetId);
            if (!paperPositionIndexes.TryGetValue(key, out var index) ||
                index < 0 ||
                index >= current.PaperPositions.Count)
            {
                return null;
            }

            return current.PaperPositions[index];
        }
    }

    public bool TryGetOpenPaperOrderIds(string assetId, out IReadOnlySet<Guid> orderIds)
    {
        lock (updateSync)
        {
            if (Volatile.Read(ref snapshot) is null)
            {
                orderIds = EmptyPaperOrderIds;
                return false;
            }

            orderIds = !string.IsNullOrWhiteSpace(assetId) &&
                openPaperOrderIdsByAsset.TryGetValue(assetId.Trim(), out var matchingOrderIds)
                    ? matchingOrderIds
                    : EmptyPaperOrderIds;
            return true;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            await RefreshCoreAsync(cancellationToken);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public void ApplyPaperOrder(PaperOrder order)
    {
        ApplyPaperOrders([order]);
    }

    public void ApplyPaperOrders(IReadOnlyCollection<PaperOrder> orders)
    {
        if (orders.Count == 0)
        {
            return;
        }

        TrackMakerGtdPaperOrders(orders);

        lock (updateSync)
        {
            if (refreshInProgress)
            {
                foreach (var order in orders)
                {
                    paperOrderRefreshOverlay[order.Id] = order;
                }
            }

            var current = Volatile.Read(ref snapshot);
            if (current is null)
            {
                return;
            }

            var updatedOpenOrders = MergeOpenPaperOrders(current.OpenPaperOrders, orders);
            openPaperOrderIdsByAsset = CreateOpenPaperOrderIdsByAsset(updatedOpenOrders);

            Volatile.Write(
                ref snapshot,
                current with
                {
                    OpenPaperOrders = updatedOpenOrders,
                    LoadedAtUtc = DateTimeOffset.UtcNow
                });
        }
    }

    public void ApplyPaperPosition(PaperPosition position)
    {
        ApplyPaperPositions([position]);
    }

    public void ApplyPaperPositions(IReadOnlyCollection<PaperPosition> positions)
    {
        if (positions.Count == 0)
        {
            return;
        }

        lock (updateSync)
        {
            if (refreshInProgress)
            {
                foreach (var position in positions)
                {
                    paperPositionRefreshOverlay[
                        PaperPositionKey.From(position.CopiedTraderWallet, position.AssetId)] = position;
                }
            }

            var current = Volatile.Read(ref snapshot);
            if (current is null)
            {
                return;
            }

            var updatedPositionArray = MergePaperPositions(current.PaperPositions, positions);
            paperPositionIndexes = CreatePaperPositionIndexes(updatedPositionArray);

            Volatile.Write(
                ref snapshot,
                current with
                {
                    PaperPositions = updatedPositionArray,
                    LoadedAtUtc = DateTimeOffset.UtcNow
                });
        }
    }

    public void ApplyLiveOrder(LiveOrder order)
    {
        lock (updateSync)
        {
            if (refreshInProgress)
            {
                liveOrderRefreshOverlay[order.Id] = order;
            }

            var current = Volatile.Read(ref snapshot);
            if (current is null)
            {
                return;
            }

            var updatedOpenLiveOrders = MergeOpenLiveOrders(current.OpenLiveOrders, [order]);

            Volatile.Write(
                ref snapshot,
                current with
                {
                    OpenLiveOrders = updatedOpenLiveOrders,
                    LoadedAtUtc = DateTimeOffset.UtcNow
                });
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref snapshot) is null)
            {
                await RefreshCoreAsync(cancellationToken);
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        lock (updateSync)
        {
            refreshInProgress = true;
            paperOrderRefreshOverlay.Clear();
            paperPositionRefreshOverlay.Clear();
            liveOrderRefreshOverlay.Clear();
        }

        try
        {
            var openPaperOrdersTask = repository.GetOpenPaperOrdersAsync(cancellationToken);
            var paperPositionsTask = repository.GetOpenPaperPositionsAsync(cancellationToken);
            var openLiveOrdersTask = repository.GetOpenLiveOrdersAsync(cancellationToken);

            await Task.WhenAll(openPaperOrdersTask, paperPositionsTask, openLiveOrdersTask);
            var loadedOpenPaperOrders = await openPaperOrdersTask;
            var loadedPaperPositions = await paperPositionsTask;
            var loadedOpenLiveOrders = await openLiveOrdersTask;

            lock (updateSync)
            {
                var refreshedOpenPaperOrders = MergeOpenPaperOrders(
                    loadedOpenPaperOrders,
                    paperOrderRefreshOverlay.Values);
                var refreshedPaperPositions = MergePaperPositions(
                    loadedPaperPositions,
                    paperPositionRefreshOverlay.Values);
                var refreshedOpenLiveOrders = MergeOpenLiveOrders(
                    loadedOpenLiveOrders,
                    liveOrderRefreshOverlay.Values);
                var refreshedSnapshot = new TradingExposureSnapshot(
                    refreshedOpenPaperOrders,
                    refreshedPaperPositions,
                    refreshedOpenLiveOrders,
                    DateTimeOffset.UtcNow);

                TrackMakerGtdPaperOrders(refreshedSnapshot.OpenPaperOrders);
                paperPositionIndexes = CreatePaperPositionIndexes(refreshedSnapshot.PaperPositions);
                openPaperOrderIdsByAsset = CreateOpenPaperOrderIdsByAsset(refreshedSnapshot.OpenPaperOrders);
                Volatile.Write(ref snapshot, refreshedSnapshot);
                ClearRefreshState();
            }
        }
        catch
        {
            lock (updateSync)
            {
                ClearRefreshState();
            }

            throw;
        }
    }

    private void ClearRefreshState()
    {
        refreshInProgress = false;
        paperOrderRefreshOverlay.Clear();
        paperPositionRefreshOverlay.Clear();
        liveOrderRefreshOverlay.Clear();
    }

    private static PaperOrder[] MergeOpenPaperOrders(
        IEnumerable<PaperOrder> currentOrders,
        IEnumerable<PaperOrder> updatedOrders)
    {
        var openOrdersById = currentOrders
            .Where(IsOpenPaperOrder)
            .ToDictionary(order => order.Id);
        foreach (var order in updatedOrders)
        {
            if (IsOpenPaperOrder(order))
            {
                openOrdersById[order.Id] = order;
            }
            else
            {
                openOrdersById.Remove(order.Id);
            }
        }

        return openOrdersById.Values
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToArray();
    }

    private static PaperPosition[] MergePaperPositions(
        IEnumerable<PaperPosition> currentPositions,
        IEnumerable<PaperPosition> updatedPositions)
    {
        var positionsByKey = new Dictionary<PaperPositionKey, PaperPosition>();
        foreach (var position in currentPositions)
        {
            if (position.SizeShares > 0m)
            {
                positionsByKey[PaperPositionKey.From(position.CopiedTraderWallet, position.AssetId)] = position;
            }
        }

        foreach (var position in updatedPositions)
        {
            var key = PaperPositionKey.From(position.CopiedTraderWallet, position.AssetId);
            if (position.SizeShares > 0m)
            {
                positionsByKey[key] = position;
            }
            else
            {
                positionsByKey.Remove(key);
            }
        }

        return positionsByKey.Values.ToArray();
    }

    private static LiveOrder[] MergeOpenLiveOrders(
        IEnumerable<LiveOrder> currentOrders,
        IEnumerable<LiveOrder> updatedOrders)
    {
        var openOrdersById = currentOrders
            .Where(IsOpenLiveOrder)
            .ToDictionary(order => order.Id);
        foreach (var order in updatedOrders)
        {
            if (IsOpenLiveOrder(order))
            {
                openOrdersById[order.Id] = order;
            }
            else
            {
                openOrdersById.Remove(order.Id);
            }
        }

        return openOrdersById.Values
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToArray();
    }

    private static bool IsOpenPaperOrder(PaperOrder order)
    {
        return order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled;
    }

    private static bool IsOpenLiveOrder(LiveOrder order)
    {
        return order.Status is LiveOrderStatus.Submitted
            or LiveOrderStatus.Live
            or LiveOrderStatus.Delayed
            or LiveOrderStatus.Unmatched
            or LiveOrderStatus.CancelRequested;
    }

    private void TrackMakerGtdPaperOrders(IEnumerable<PaperOrder> orders)
    {
        foreach (var order in orders.Where(MakerGtdPaperExecutionContract.IsMakerGtdOrder))
        {
            if (IsOpenPaperOrder(order))
            {
                makerGtdHandoff.TrackMakerGtdPaperOrder(order.Id, order.ExecutionSource);
            }
            else
            {
                makerGtdHandoff.ClearMarketDataFailures(order.Id);
            }
        }
    }

    private static Dictionary<PaperPositionKey, int> CreatePaperPositionIndexes(IReadOnlyList<PaperPosition> positions)
    {
        var indexes = new Dictionary<PaperPositionKey, int>(positions.Count);
        for (var i = 0; i < positions.Count; i++)
        {
            var position = positions[i];
            indexes[PaperPositionKey.From(position.CopiedTraderWallet, position.AssetId)] = i;
        }

        return indexes;
    }

    private static Dictionary<string, HashSet<Guid>> CreateOpenPaperOrderIdsByAsset(IEnumerable<PaperOrder> orders)
    {
        var orderIdsByAsset = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders.Where(IsOpenPaperOrder))
        {
            var assetId = order.AssetId.Trim();
            if (assetId.Length == 0)
            {
                continue;
            }

            if (!orderIdsByAsset.TryGetValue(assetId, out var orderIds))
            {
                orderIds = [];
                orderIdsByAsset[assetId] = orderIds;
            }

            orderIds.Add(order.Id);
        }

        return orderIdsByAsset;
    }

    private readonly record struct PaperPositionKey(string CopiedTraderWallet, string AssetId)
    {
        public static PaperPositionKey From(string copiedTraderWallet, string assetId)
        {
            return new PaperPositionKey(
                copiedTraderWallet.Trim().ToUpperInvariant(),
                assetId.Trim().ToUpperInvariant());
        }
    }
}
