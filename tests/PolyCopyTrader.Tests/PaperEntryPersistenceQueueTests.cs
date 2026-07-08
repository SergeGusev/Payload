using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class PaperEntryPersistenceQueueTests
{
    [Fact]
    public async Task StopAsync_WaitsForQueuedBatchToPersist()
    {
        var repository = new TestAppRepository
        {
            PaperEntryPersistenceBatchDelay = TimeSpan.FromMilliseconds(50)
        };
        var queue = CreateQueue(repository);
        var runId = Guid.NewGuid();

        await queue.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(CreateBatch(runId), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(1, repository.PaperEntryPersistenceBatchCalls);
        Assert.Equal(0, queue.PendingBatches);
        Assert.Contains(repository.StrategyMarketPaperRuns, run => run.Id == runId);
    }

    [Fact]
    public async Task StopAsync_RetriesUntilQueuedBatchPersists()
    {
        var repository = new TestAppRepository
        {
            PaperEntryPersistenceBatchFailuresToThrow = 1
        };
        var queue = CreateQueue(repository);
        var runId = Guid.NewGuid();

        await queue.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(CreateBatch(runId), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(2, repository.PaperEntryPersistenceBatchAttempts);
        Assert.Equal(1, repository.PaperEntryPersistenceBatchCalls);
        Assert.Equal(0, queue.PendingBatches);
        Assert.Contains(repository.StrategyMarketPaperRuns, run => run.Id == runId);
    }

    [Fact]
    public async Task EnqueueAsync_RejectsNewBatchesAfterStop()
    {
        var queue = CreateQueue(new TestAppRepository());

        await queue.StartAsync(CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await queue.EnqueueAsync(CreateBatch(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Writer_MaterializesPaperPositionsAfterBatchIsQueued()
    {
        var repository = new TestAppRepository();
        var queue = CreateQueue(repository);
        var now = DateTimeOffset.UtcNow;
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0xleader",
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Yes",
            0.60m,
            2m,
            1.20m,
            now,
            now,
            now);
        var fill = new PaperFill(
            Guid.NewGuid(),
            order.Id,
            0.60m,
            2m,
            now,
            "test fill");
        var batch = new PaperEntryPersistenceBatch(
            [],
            [order],
            [fill],
            [],
            [],
            [])
        {
            PaperPositionMaterializations =
            [
                new PaperPositionMaterialization(order, fill, 0.70m, now)
            ]
        };

        await queue.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(batch, CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(1, repository.PaperEntryPersistenceBatchCalls);
        var position = Assert.Single(repository.PaperPositions);
        Assert.Equal("asset-1", position.AssetId);
        Assert.Equal(2m, position.SizeShares);
        Assert.Equal(0.60m, position.AveragePrice);
        Assert.Equal(1.40m, position.EstimatedValueUsd);
    }

    private static PaperEntryPersistenceQueue CreateQueue(TestAppRepository repository)
    {
        var exposureCache = new ExposureSnapshotCache(repository);
        return new PaperEntryPersistenceQueue(
            NullLogger<PaperEntryPersistenceQueue>.Instance,
            repository,
            new DefaultPaperTradingEngine(),
            exposureCache);
    }

    private static PaperEntryPersistenceBatch CreateBatch(Guid runId)
    {
        var now = DateTimeOffset.UtcNow;
        return new PaperEntryPersistenceBatch(
            [],
            [],
            [],
            [],
            [],
            [
                new StrategyMarketPaperRun(
                    runId,
                    StrategyIds.BtcUpDown5mDownSimple,
                    "market-1",
                    "condition-1",
                    "btc-updown-5m-1",
                    "BTC Up or Down 5m",
                    "Crypto",
                    now.AddMinutes(-5),
                    now,
                    now.AddMinutes(-4),
                    now.AddSeconds(-1),
                    StrategyMarketPaperRunStatuses.Skipped,
                    null,
                    null,
                    null,
                    1m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "test_skip",
                    now.AddMinutes(-4),
                    now,
                    null)
            ]);
    }
}
