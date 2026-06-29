using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class ExposureSnapshotCache(IAppRepository repository) : IExposureSnapshotCache
{
    private readonly object updateSync = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private Dictionary<PaperPositionKey, int> paperPositionIndexes = [];
    private TradingExposureSnapshot? snapshot;

    public async Task<TradingExposureSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var current = Volatile.Read(ref snapshot);
        if (current is null)
        {
            await RefreshAsync(cancellationToken);
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

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            var openPaperOrdersTask = repository.GetOpenPaperOrdersAsync(cancellationToken);
            var paperPositionsTask = repository.GetPaperPositionsAsync(cancellationToken);
            var openLiveOrdersTask = repository.GetOpenLiveOrdersAsync(cancellationToken);

            await Task.WhenAll(openPaperOrdersTask, paperPositionsTask, openLiveOrdersTask);
            var loadedOpenPaperOrders = await openPaperOrdersTask;
            var loadedPaperPositions = await paperPositionsTask;
            var loadedOpenLiveOrders = await openLiveOrdersTask;

            var refreshedSnapshot = new TradingExposureSnapshot(
                loadedOpenPaperOrders.ToArray(),
                loadedPaperPositions.ToArray(),
                loadedOpenLiveOrders.ToArray(),
                DateTimeOffset.UtcNow);
            lock (updateSync)
            {
                paperPositionIndexes = CreatePaperPositionIndexes(refreshedSnapshot.PaperPositions);
                Volatile.Write(ref snapshot, refreshedSnapshot);
            }
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

        lock (updateSync)
        {
            var current = Volatile.Read(ref snapshot);
            if (current is null)
            {
                return;
            }

            var openOrdersById = current.OpenPaperOrders.ToDictionary(order => order.Id);
            foreach (var order in orders)
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

            Volatile.Write(
                ref snapshot,
                current with
                {
                    OpenPaperOrders = openOrdersById.Values
                        .OrderByDescending(item => item.CreatedAtUtc)
                        .ToArray(),
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
            var current = Volatile.Read(ref snapshot);
            if (current is null)
            {
                return;
            }

            var currentPositions = current.PaperPositions as PaperPosition[] ?? current.PaperPositions.ToArray();
            if (paperPositionIndexes.Count == 0 && currentPositions.Length > 0)
            {
                paperPositionIndexes = CreatePaperPositionIndexes(currentPositions);
            }

            var updatesByKey = new Dictionary<PaperPositionKey, PaperPosition>(positions.Count);
            foreach (var position in positions)
            {
                updatesByKey[PaperPositionKey.From(position.CopiedTraderWallet, position.AssetId)] = position;
            }

            var newPositionCount = updatesByKey.Keys.Count(key => !paperPositionIndexes.ContainsKey(key));
            var updatedPositions = new PaperPosition[currentPositions.Length + newPositionCount];
            Array.Copy(currentPositions, updatedPositions, currentPositions.Length);

            var nextIndex = currentPositions.Length;
            foreach (var (key, position) in updatesByKey)
            {
                if (paperPositionIndexes.TryGetValue(key, out var index))
                {
                    updatedPositions[index] = position;
                    continue;
                }

                paperPositionIndexes[key] = nextIndex;
                updatedPositions[nextIndex] = position;
                nextIndex++;
            }

            Volatile.Write(
                ref snapshot,
                current with
                {
                    PaperPositions = updatedPositions,
                    LoadedAtUtc = DateTimeOffset.UtcNow
                });
        }
    }

    public void ApplyLiveOrder(LiveOrder order)
    {
        lock (updateSync)
        {
            var current = Volatile.Read(ref snapshot);
            if (current is null)
            {
                return;
            }

            var orders = current.OpenLiveOrders
                .Where(item => item.Id != order.Id)
                .ToList();
            if (IsOpenLiveOrder(order))
            {
                orders.Add(order);
            }

            Volatile.Write(
                ref snapshot,
                current with
                {
                    OpenLiveOrders = orders
                        .OrderByDescending(item => item.CreatedAtUtc)
                        .ToArray(),
                    LoadedAtUtc = DateTimeOffset.UtcNow
                });
        }
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
