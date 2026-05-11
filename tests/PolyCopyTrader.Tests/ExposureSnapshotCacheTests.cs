using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.PaperTrading;

namespace PolyCopyTrader.Tests;

public sealed class ExposureSnapshotCacheTests
{
    [Fact]
    public async Task GetSnapshotAsync_LoadsOpenExposureFromRepository()
    {
        var repository = new TestAppRepository();
        var openPaperOrder = PaperOrder(PaperOrderStatus.Pending);
        var filledPaperOrder = PaperOrder(PaperOrderStatus.Filled);
        var openLiveOrder = LiveOrder(LiveOrderStatus.Live);
        var cancelledLiveOrder = LiveOrder(LiveOrderStatus.Cancelled);
        repository.PaperOrders.AddRange([openPaperOrder, filledPaperOrder]);
        repository.PaperPositions.Add(PaperPosition(10m));
        repository.LiveOrders.AddRange([openLiveOrder, cancelledLiveOrder]);
        var cache = new ExposureSnapshotCache(repository);

        var snapshot = await cache.GetSnapshotAsync();

        Assert.Single(snapshot.OpenPaperOrders);
        Assert.Equal(openPaperOrder.Id, snapshot.OpenPaperOrders[0].Id);
        Assert.Single(snapshot.PaperPositions);
        Assert.Single(snapshot.OpenLiveOrders);
        Assert.Equal(openLiveOrder.Id, snapshot.OpenLiveOrders[0].Id);
    }

    [Fact]
    public async Task ApplyMethods_UpdateInitializedSnapshotInMemory()
    {
        var repository = new TestAppRepository();
        var openPaperOrder = PaperOrder(PaperOrderStatus.Pending);
        var openLiveOrder = LiveOrder(LiveOrderStatus.Live);
        repository.PaperOrders.Add(openPaperOrder);
        repository.PaperPositions.Add(PaperPosition(10m));
        repository.LiveOrders.Add(openLiveOrder);
        var cache = new ExposureSnapshotCache(repository);
        await cache.GetSnapshotAsync();

        cache.ApplyPaperOrder(openPaperOrder with { Status = PaperOrderStatus.Filled });
        cache.ApplyPaperPosition(PaperPosition(25m));
        cache.ApplyLiveOrder(openLiveOrder with { Status = LiveOrderStatus.Cancelled });

        var snapshot = await cache.GetSnapshotAsync();

        Assert.Empty(snapshot.OpenPaperOrders);
        Assert.Equal(25m, Assert.Single(snapshot.PaperPositions).SizeShares);
        Assert.Empty(snapshot.OpenLiveOrders);
    }

    private static PaperOrder PaperOrder(PaperOrderStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0xleader",
            status,
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Yes",
            0.50m,
            10m,
            5m,
            now,
            now.AddMinutes(5),
            status == PaperOrderStatus.Filled ? now : null);
    }

    private static PaperPosition PaperPosition(decimal sizeShares)
    {
        return new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            sizeShares,
            0.50m,
            sizeShares * 0.51m,
            sizeShares * 0.01m,
            DateTimeOffset.UtcNow,
            "0xleader");
    }

    private static LiveOrder LiveOrder(LiveOrderStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            status,
            "clob-order-1",
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Yes",
            0.50m,
            10m,
            5m,
            "GTD",
            now,
            now.AddMinutes(5),
            now,
            status.ToString(),
            0m,
            10m,
            string.Empty,
            "{}",
            string.Empty,
            now);
    }
}
