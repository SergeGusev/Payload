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
        lock (sync)
        {
            if (!initialized)
            {
                return;
            }

            var orders = openPaperOrders
                .Where(item => item.Id != order.Id)
                .ToList();
            if (IsOpenPaperOrder(order))
            {
                orders.Add(order);
            }

            openPaperOrders = orders
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToArray();
            loadedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void ApplyPaperPosition(PaperPosition position)
    {
        lock (sync)
        {
            if (!initialized)
            {
                return;
            }

            var positions = paperPositions
                .Where(item =>
                    !string.Equals(item.CopiedTraderWallet, position.CopiedTraderWallet, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(item.AssetId, position.AssetId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            positions.Add(position);

            paperPositions = positions
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
}
