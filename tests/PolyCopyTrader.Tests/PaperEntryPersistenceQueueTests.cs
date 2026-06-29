using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

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
        var queue = new PaperEntryPersistenceQueue(
            NullLogger<PaperEntryPersistenceQueue>.Instance,
            repository);
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
        var queue = new PaperEntryPersistenceQueue(
            NullLogger<PaperEntryPersistenceQueue>.Instance,
            repository);
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
        var queue = new PaperEntryPersistenceQueue(
            NullLogger<PaperEntryPersistenceQueue>.Instance,
            new TestAppRepository());

        await queue.StartAsync(CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await queue.EnqueueAsync(CreateBatch(Guid.NewGuid()), CancellationToken.None));
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
                    StrategyIds.BtcUpDown5mAlwaysDown,
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
