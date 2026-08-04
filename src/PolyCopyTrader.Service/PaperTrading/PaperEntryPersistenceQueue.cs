using System.Threading.Channels;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.PaperTrading;

public interface IPaperEntryPersistenceQueue
{
    ValueTask EnqueueAsync(
        PaperEntryPersistenceBatch batch,
        CancellationToken cancellationToken = default);

    int PendingBatches { get; }
}

public sealed class PaperEntryPersistenceQueue(
    ILogger<PaperEntryPersistenceQueue> logger,
    IAppRepository repository,
    IPaperTradingEngine paperTradingEngine,
    IExposureSnapshotCache exposureCache) : IHostedService, IPaperEntryPersistenceQueue
{
    private const int MaxBatchesPerFlush = 64;
    private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromSeconds(1);

    private readonly Channel<PaperEntryPersistenceBatch> channel = Channel.CreateUnbounded<PaperEntryPersistenceBatch>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private readonly object lifecycleSync = new();
    private Task? writerTask;
    private volatile bool accepting;
    private int pendingBatches;

    public int PendingBatches => Volatile.Read(ref pendingBatches);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (lifecycleSync)
        {
            if (writerTask is not null)
            {
                return Task.CompletedTask;
            }

            accepting = true;
            writerTask = Task.Run(RunWriterAsync, CancellationToken.None);
        }

        logger.LogInformation("Paper entry persistence queue writer started.");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? completion;
        lock (lifecycleSync)
        {
            accepting = false;
            channel.Writer.TryComplete();
            completion = writerTask;
        }

        if (completion is null)
        {
            return;
        }

        logger.LogInformation(
            "Paper entry persistence queue is stopping. PendingBatches={PendingBatches}",
            PendingBatches);

        await completion.ConfigureAwait(false);
        logger.LogInformation("Paper entry persistence queue stopped.");
    }

    public ValueTask EnqueueAsync(
        PaperEntryPersistenceBatch batch,
        CancellationToken cancellationToken = default)
    {
        if (batch.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!accepting)
        {
            throw new InvalidOperationException("Paper entry persistence queue is stopping and no longer accepts new batches.");
        }

        Interlocked.Increment(ref pendingBatches);
        if (channel.Writer.TryWrite(batch))
        {
            return ValueTask.CompletedTask;
        }

        Interlocked.Decrement(ref pendingBatches);
        throw new InvalidOperationException("Paper entry persistence queue is closed.");
    }

    private async Task RunWriterAsync()
    {
        try
        {
            while (await channel.Reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (!channel.Reader.TryRead(out PaperEntryPersistenceBatch? firstBatch))
                {
                    continue;
                }

                var workItem = DrainAvailableBatches(firstBatch);
                await PersistWithRetryAsync(workItem).ConfigureAwait(false);
                Interlocked.Add(ref pendingBatches, -workItem.BatchCount);
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Paper entry persistence queue writer stopped unexpectedly.");
            throw;
        }
    }

    private PersistenceWorkItem DrainAvailableBatches(PaperEntryPersistenceBatch firstBatch)
    {
        var batches = new List<PaperEntryPersistenceBatch> { firstBatch };
        while (batches.Count < MaxBatchesPerFlush &&
               channel.Reader.TryRead(out PaperEntryPersistenceBatch? batch))
        {
            batches.Add(batch);
        }

        if (batches.Count == 1)
        {
            return new PersistenceWorkItem(firstBatch, 1);
        }

        return new PersistenceWorkItem(MergeBatches(batches), batches.Count);
    }

    private async Task PersistWithRetryAsync(PersistenceWorkItem workItem)
    {
        var failureCount = 0;
        while (true)
        {
            try
            {
                var materializedBatch = await PaperEntryPositionMaterializer.MaterializeAsync(
                    workItem.Batch,
                    paperTradingEngine,
                    exposureCache,
                    CancellationToken.None).ConfigureAwait(false);
                await repository.AddPaperEntryPersistenceBatchAsync(materializedBatch, CancellationToken.None).ConfigureAwait(false);
                exposureCache.ApplyPaperPositions(materializedBatch.PaperPositions);
                if (failureCount > 0)
                {
                    logger.LogInformation(
                        "Paper entry persistence queue recovered after {FailureCount} failed attempts. Batches={BatchCount} Runs={RunCount}",
                        failureCount,
                        workItem.BatchCount,
                        workItem.Batch.StrategyRuns.Count);
                }

                return;
            }
            catch (Exception ex)
            {
                failureCount++;
                logger.LogError(
                    ex,
                    "Failed to persist queued paper entry batch. The service will keep waiting and retrying. Attempt={Attempt} Batches={BatchCount} Runs={RunCount}",
                    failureCount,
                    workItem.BatchCount,
                    workItem.Batch.StrategyRuns.Count);
                await Task.Delay(FailureRetryDelay, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    internal static PaperEntryPersistenceBatch MergeBatches(
        IReadOnlyCollection<PaperEntryPersistenceBatch> batches)
    {
        if (batches.Count == 0)
        {
            throw new ArgumentException("At least one paper entry persistence batch is required.", nameof(batches));
        }

        var directPaperSkipCompactionValues = batches
            .Select(batch => batch.DirectPaperSkipCompactionEnabled)
            .Distinct()
            .ToArray();
        if (directPaperSkipCompactionValues.Length != 1)
        {
            throw new InvalidOperationException(
                "Cannot merge paper entry persistence batches with different direct Paper skip compaction modes.");
        }

        return new PaperEntryPersistenceBatch(
            batches.SelectMany(batch => batch.Signals).ToArray(),
            batches.SelectMany(batch => batch.PaperOrders).ToArray(),
            batches.SelectMany(batch => batch.PaperFills).ToArray(),
            batches.SelectMany(batch => batch.PaperPositions).ToArray(),
            batches.SelectMany(batch => batch.CopiedLeaderPositionActivations).ToArray(),
            batches.SelectMany(batch => batch.StrategyRuns).ToArray())
        {
            DirectPaperSkipCompactionEnabled = directPaperSkipCompactionValues[0],
            PaperPositionMaterializations = batches
                .SelectMany(batch => batch.PaperPositionMaterializations)
                .ToArray()
        };
    }

    private sealed record PersistenceWorkItem(
        PaperEntryPersistenceBatch Batch,
        int BatchCount);
}
