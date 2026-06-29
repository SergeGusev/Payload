using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class ExposureSnapshotCache(IAppRepository repository) : IExposureSnapshotCache
{
    private readonly object sync = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private IReadOnlyList<PaperOrder> openPaperOrders = [];
    private IReadOnlyList<PaperPosition> paperPositions = [];
    private IReadOnlyList<LiveOrder> openLiveOrders = [];
    private DateTimeOffset loadedAtUtc = DateTimeOffset.MinValue;
    private bool initialized;

    public async Task<TradingExposureSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!initialized)
        {
            await RefreshAsync(cancellationToken);
        }

        lock (sync)
        {
            return new TradingExposureSnapshot(
                openPaperOrders.ToArray(),
                paperPositions.ToArray(),
                openLiveOrders.ToArray(),
                loadedAtUtc);
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

            lock (sync)
            {
                openPaperOrders = loadedOpenPaperOrders.ToArray();
                paperPositions = loadedPaperPositions.ToArray();
                openLiveOrders = loadedOpenLiveOrders.ToArray();
                loadedAtUtc = DateTimeOffset.UtcNow;
                initialized = true;
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
        lock (sync)
        {
            if (!initialized || orders.Count == 0)
            {
                return;
            }

            var openOrdersById = openPaperOrders.ToDictionary(order => order.Id);
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

            openPaperOrders = openOrdersById.Values
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToArray();
            loadedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void ApplyPaperPosition(PaperPosition position)
    {
        ApplyPaperPositions([position]);
    }

    public void ApplyPaperPositions(IReadOnlyCollection<PaperPosition> positions)
    {
        lock (sync)
        {
            if (!initialized || positions.Count == 0)
            {
                return;
            }

            var positionsByKey = paperPositions.ToDictionary(
                position => PaperPositionKey.From(position.CopiedTraderWallet, position.AssetId));
            foreach (var position in positions)
            {
                positionsByKey[PaperPositionKey.From(position.CopiedTraderWallet, position.AssetId)] = position;
            }

            paperPositions = positionsByKey.Values
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ToArray();
            loadedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void ApplyLiveOrder(LiveOrder order)
    {
        lock (sync)
        {
            if (!initialized)
            {
                return;
            }

            var orders = openLiveOrders
                .Where(item => item.Id != order.Id)
                .ToList();
            if (IsOpenLiveOrder(order))
            {
                orders.Add(order);
            }

            openLiveOrders = orders
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToArray();
            loadedAtUtc = DateTimeOffset.UtcNow;
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
