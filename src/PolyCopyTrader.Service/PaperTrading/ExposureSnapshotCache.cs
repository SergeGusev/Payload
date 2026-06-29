using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class ExposureSnapshotCache(IAppRepository repository) : IExposureSnapshotCache
{
    private readonly object updateSync = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);
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

            var positionsByKey = current.PaperPositions.ToDictionary(
                position => PaperPositionKey.From(position.CopiedTraderWallet, position.AssetId));
            foreach (var position in positions)
            {
                positionsByKey[PaperPositionKey.From(position.CopiedTraderWallet, position.AssetId)] = position;
            }

            Volatile.Write(
                ref snapshot,
                current with
                {
                    PaperPositions = positionsByKey.Values
                        .OrderByDescending(item => item.UpdatedAtUtc)
                        .ToArray(),
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
