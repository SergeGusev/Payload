using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.PaperTrading;

namespace PolyCopyTrader.Tests;

public sealed class ExposureSnapshotCacheTests
{
    [Fact]
    public void TryGetOpenPaperOrderIds_ReturnsUnknownBeforeInitialLoad()
    {
        var cache = new ExposureSnapshotCache(new TestAppRepository());

        var initialized = cache.TryGetOpenPaperOrderIds("asset-1", out var orderIds);

        Assert.False(initialized);
        Assert.Empty(orderIds);
    }

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
        repository.PaperPositions.Add(PaperPosition(0m, assetId: "closed-asset"));
        repository.LiveOrders.AddRange([openLiveOrder, cancelledLiveOrder]);
        var cache = new ExposureSnapshotCache(repository);

        var snapshot = await cache.GetSnapshotAsync();

        Assert.Single(snapshot.OpenPaperOrders);
        Assert.Equal(openPaperOrder.Id, snapshot.OpenPaperOrders[0].Id);
        Assert.Single(snapshot.PaperPositions);
        Assert.Equal(1, repository.GetOpenPaperPositionsCalls);
        Assert.Equal(0, repository.GetPaperPositionsCalls);
        Assert.Single(snapshot.OpenLiveOrders);
        Assert.Equal(openLiveOrder.Id, snapshot.OpenLiveOrders[0].Id);
        Assert.True(cache.TryGetOpenPaperOrderIds("ASSET-1", out var openPaperOrderIds));
        Assert.Equal(new HashSet<Guid> { openPaperOrder.Id }, openPaperOrderIds);
    }

    [Fact]
    public async Task ApplyPaperPosition_RemovesClosedPositionFromSnapshot()
    {
        var repository = new TestAppRepository();
        repository.PaperPositions.Add(PaperPosition(10m));
        var cache = new ExposureSnapshotCache(repository);
        await cache.GetSnapshotAsync();

        cache.ApplyPaperPosition(PaperPosition(0m));

        var snapshot = await cache.GetSnapshotAsync();
        Assert.Empty(snapshot.PaperPositions);
        Assert.Null(cache.GetPaperPosition("0xleader", "asset-1"));
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
        Assert.True(cache.TryGetOpenPaperOrderIds("asset-1", out var openPaperOrderIds));
        Assert.Empty(openPaperOrderIds);
        Assert.Equal(25m, Assert.Single(snapshot.PaperPositions).SizeShares);
        Assert.Empty(snapshot.OpenLiveOrders);
    }

    [Fact]
    public async Task ApplyBulkMethods_UpdateInitializedSnapshotOncePerBatch()
    {
        var repository = new TestAppRepository();
        var firstOpenPaperOrder = PaperOrder(PaperOrderStatus.Pending);
        var secondOpenPaperOrder = PaperOrder(PaperOrderStatus.Pending);
        var closedPaperOrder = PaperOrder(PaperOrderStatus.Pending);
        repository.PaperOrders.Add(closedPaperOrder);
        repository.PaperPositions.Add(PaperPosition(10m));
        var cache = new ExposureSnapshotCache(repository);
        await cache.GetSnapshotAsync();

        cache.ApplyPaperOrders([
            firstOpenPaperOrder,
            secondOpenPaperOrder,
            closedPaperOrder with { Status = PaperOrderStatus.Filled }
        ]);
        cache.ApplyPaperPositions([
            PaperPosition(25m),
            PaperPosition(30m, assetId: "asset-2")
        ]);

        var snapshot = await cache.GetSnapshotAsync();

        Assert.Equal(2, snapshot.OpenPaperOrders.Count);
        Assert.Contains(snapshot.OpenPaperOrders, order => order.Id == firstOpenPaperOrder.Id);
        Assert.Contains(snapshot.OpenPaperOrders, order => order.Id == secondOpenPaperOrder.Id);
        Assert.DoesNotContain(snapshot.OpenPaperOrders, order => order.Id == closedPaperOrder.Id);
        Assert.True(cache.TryGetOpenPaperOrderIds("asset-1", out var openPaperOrderIds));
        Assert.Equal(
            new HashSet<Guid> { firstOpenPaperOrder.Id, secondOpenPaperOrder.Id },
            openPaperOrderIds);
        Assert.Equal(2, snapshot.PaperPositions.Count);
        Assert.Contains(snapshot.PaperPositions, position => position is { AssetId: "asset-1", SizeShares: 25m });
        Assert.Contains(snapshot.PaperPositions, position => position is { AssetId: "asset-2", SizeShares: 30m });
    }

    [Fact]
    public async Task ApplyPaperPositions_UpdatesLargeInitializedSnapshotByKey()
    {
        var repository = new TestAppRepository();
        for (var i = 0; i < 10_000; i++)
        {
            repository.PaperPositions.Add(PaperPosition(i + 1m, assetId: $"asset-{i}"));
        }

        var cache = new ExposureSnapshotCache(repository);
        await cache.GetSnapshotAsync();

        cache.ApplyPaperPositions([
            PaperPosition(42m, assetId: "asset-9999"),
            PaperPosition(7m, assetId: "asset-new")
        ]);

        var snapshot = await cache.GetSnapshotAsync();

        Assert.Equal(10_001, snapshot.PaperPositions.Count);
        Assert.Contains(snapshot.PaperPositions, position => position is { AssetId: "asset-9999", SizeShares: 42m });
        Assert.Contains(snapshot.PaperPositions, position => position is { AssetId: "asset-new", SizeShares: 7m });
        Assert.Single(snapshot.PaperPositions, position => position.AssetId == "asset-9999");
        Assert.Equal(42m, cache.GetPaperPosition("0xleader", "asset-9999")?.SizeShares);
        Assert.Equal(7m, cache.GetPaperPosition("0xleader", "asset-new")?.SizeShares);
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

    private static PaperPosition PaperPosition(decimal sizeShares, string assetId = "asset-1")
    {
        return new PaperPosition(
            assetId,
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
