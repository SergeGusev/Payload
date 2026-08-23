using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class PaperFakFeeBackfillWorkerTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public async Task RunCycle_DefersWhileForegroundPersistenceIsPending(
        int pendingPaperBatches,
        int pendingMarketUpdates)
    {
        var processor = new RecordingProcessor();
        var worker = CreateWorker(
            processor,
            new StubPaperEntryPersistenceQueue(pendingPaperBatches),
            new StubMarketDataSideEffectQueue(pendingMarketUpdates));

        var disposition = await worker.RunCycleAsync();

        Assert.Equal(PaperFakFeeBackfillWorkerCycleDisposition.ForegroundWorkPending, disposition);
        Assert.Equal(0, processor.Calls);
    }

    [Fact]
    public async Task RunCycle_AllowsOneBoundedBackfillTurnAfterYieldingToPersistentMarketData()
    {
        var processor = new RecordingProcessor();
        var eventRecorder = new RecordingEventRecorder();
        var worker = CreateWorker(
            processor,
            new StubPaperEntryPersistenceQueue(0),
            new StubMarketDataSideEffectQueue(1),
            eventRecorder);

        var first = await worker.RunCycleAsync();
        var second = await worker.RunCycleAsync();
        var third = await worker.RunCycleAsync();

        Assert.Equal(PaperFakFeeBackfillWorkerCycleDisposition.ForegroundWorkPending, first);
        Assert.Equal(PaperFakFeeBackfillWorkerCycleDisposition.Processed, second);
        Assert.Equal(PaperFakFeeBackfillWorkerCycleDisposition.ForegroundWorkPending, third);
        Assert.Equal(1, processor.Calls);
        var deferredEvents = eventRecorder.Events
            .Where(entry => entry.EventType == PaperFakFeeBackfillEventTypes.ForegroundDeferred)
            .ToArray();
        Assert.Equal(2, deferredEvents.Length);
        Assert.All(deferredEvents, entry => Assert.Equal(1, entry.PendingMarketDataUpdates));
        var cycleStarted = Assert.Single(
            eventRecorder.Events,
            entry => entry.EventType == PaperFakFeeBackfillEventTypes.CycleStarted);
        Assert.NotNull(cycleStarted.CycleId);
        Assert.Equal(Assert.Single(processor.CycleIds), cycleStarted.CycleId);
    }

    [Fact]
    public async Task RunCycle_AllowsOneBoundedBackfillTurnAfterYieldingToPersistentPaperEntries()
    {
        var processor = new RecordingProcessor();
        var paperQueue = new StubPaperEntryPersistenceQueue(1);
        var worker = CreateWorker(
            processor,
            paperQueue,
            new StubMarketDataSideEffectQueue(0));

        var first = await worker.RunCycleAsync();
        var second = await worker.RunCycleAsync();
        var third = await worker.RunCycleAsync();

        Assert.Equal(PaperFakFeeBackfillWorkerCycleDisposition.ForegroundWorkPending, first);
        Assert.Equal(PaperFakFeeBackfillWorkerCycleDisposition.Processed, second);
        Assert.Equal(PaperFakFeeBackfillWorkerCycleDisposition.ForegroundWorkPending, third);
        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task RunCycle_QueueIdleTurnRearmsForegroundYield()
    {
        var processor = new RecordingProcessor();
        var marketQueue = new StubMarketDataSideEffectQueue(1);
        var worker = CreateWorker(
            processor,
            new StubPaperEntryPersistenceQueue(0),
            marketQueue);

        Assert.Equal(
            PaperFakFeeBackfillWorkerCycleDisposition.ForegroundWorkPending,
            await worker.RunCycleAsync());
        marketQueue.PendingUpdates = 0;
        Assert.Equal(
            PaperFakFeeBackfillWorkerCycleDisposition.Processed,
            await worker.RunCycleAsync());
        marketQueue.PendingUpdates = 1;
        Assert.Equal(
            PaperFakFeeBackfillWorkerCycleDisposition.ForegroundWorkPending,
            await worker.RunCycleAsync());
        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task RunCycle_UsesIdleDispositionAtSweepEnd()
    {
        var processor = new RecordingProcessor
        {
            Result = new PaperFakFeeBackfillCycleResult(0, 0, 0, true, true, null)
        };
        var worker = CreateWorker(
            processor,
            new StubPaperEntryPersistenceQueue(0),
            new StubMarketDataSideEffectQueue(0));

        var disposition = await worker.RunCycleAsync();

        Assert.Equal(PaperFakFeeBackfillWorkerCycleDisposition.SweepIdle, disposition);
        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task RunCycle_ParitySweepCompletionStartsFreshExactPassBeforeLongLegacySweepEnds()
    {
        var legacy = new RecordingProcessor
        {
            Result = new PaperFakFeeBackfillCycleResult(1, 1, 0, false, true, null)
        };
        var parity = new RecordingParityProcessor(
            new HistoricalGrossNetParityCycleResult(
                HistoricalGrossNetParityCycleState.StrategyCompleted,
                false,
                HistoricalGrossNetParityProcessingPhase.Fallback,
                1,
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                "completed"),
            new HistoricalGrossNetParityCycleResult(
                HistoricalGrossNetParityCycleState.Idle,
                true,
                HistoricalGrossNetParityProcessingPhase.Exact,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                "fresh exact pass is empty"));
        var worker = new PaperFakFeeBackfillWorker(
            NullLogger<PaperFakFeeBackfillWorker>.Instance,
            new PaperFakFeeBackfillOptions { Enabled = true, ApplyEnabled = true },
            legacy,
            new StubPaperEntryPersistenceQueue(0),
            new StubMarketDataSideEffectQueue(0),
            historicalGrossNetParityOptions: new HistoricalGrossNetParityOptions { Enabled = true },
            historicalGrossNetParityProcessor: parity);

        Assert.Equal(PaperFakFeeBackfillWorkerCycleDisposition.Processed, await worker.RunCycleAsync());
        Assert.Equal(PaperFakFeeBackfillWorkerCycleDisposition.Processed, await worker.RunCycleAsync());
        Assert.Equal(PaperFakFeeBackfillWorkerCycleDisposition.Processed, await worker.RunCycleAsync());

        Assert.Equal(2, parity.CycleIds.Count);
        Assert.Equal(1, legacy.Calls);
    }

    [Fact]
    public async Task Worker_WhenDisabled_RecordsDisabledAndStoppedWithoutCallingProcessor()
    {
        var processor = new RecordingProcessor();
        var eventRecorder = new RecordingEventRecorder();
        var worker = new PaperFakFeeBackfillWorker(
            NullLogger<PaperFakFeeBackfillWorker>.Instance,
            new PaperFakFeeBackfillOptions { Enabled = false },
            processor,
            new StubPaperEntryPersistenceQueue(0),
            new StubMarketDataSideEffectQueue(0),
            eventRecorder);

        await worker.StartAsync(CancellationToken.None);
        Assert.Equal(
            PaperFakFeeBackfillEventTypes.WorkerDisabled,
            (await eventRecorder.FirstEvent.Task.WaitAsync(TimeSpan.FromSeconds(1))).EventType);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, processor.Calls);
        Assert.Equal(
            [
                PaperFakFeeBackfillEventTypes.WorkerDisabled,
                PaperFakFeeBackfillEventTypes.WorkerStopped
            ],
            eventRecorder.Events.Select(entry => entry.EventType).ToArray());
        Assert.All(eventRecorder.Events, entry =>
        {
            Assert.False(entry.BackfillEnabled);
            Assert.False(entry.ApplyEnabled);
        });
    }

    [Fact]
    public async Task Worker_WhenCycleFails_RecordsCorrelatedFailureAndStopsCleanly()
    {
        var processor = new RecordingProcessor
        {
            ExceptionToThrow = new InvalidOperationException("cycle test failure")
        };
        var eventRecorder = new RecordingEventRecorder();
        var worker = new PaperFakFeeBackfillWorker(
            NullLogger<PaperFakFeeBackfillWorker>.Instance,
            new PaperFakFeeBackfillOptions
            {
                Enabled = true,
                ApplyEnabled = true,
                InitialDelaySeconds = 0,
                ErrorDelaySeconds = 1,
                MaxErrorDelaySeconds = 1
            },
            processor,
            new StubPaperEntryPersistenceQueue(0),
            new StubMarketDataSideEffectQueue(0),
            eventRecorder);

        await worker.StartAsync(CancellationToken.None);
        var failed = await eventRecorder.FailedEvent.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        var started = Assert.Single(
            eventRecorder.Events,
            entry => entry.EventType == PaperFakFeeBackfillEventTypes.CycleStarted);
        Assert.Equal(started.CycleId, failed.CycleId);
        Assert.Equal(1, failed.DelaySeconds);
        Assert.Equal(typeof(InvalidOperationException).FullName, failed.ExceptionType);
        Assert.Equal("cycle test failure", failed.ExceptionMessage);
        Assert.Contains(
            eventRecorder.Events,
            entry => entry.EventType == PaperFakFeeBackfillEventTypes.WorkerStopped);
    }

    [Fact]
    public void Program_RegistersBackfillOptionsProcessorAndWorker()
    {
        var source = ReadRepositorySource(
            Path.Combine("src", "PolyCopyTrader.Service", "Program.cs"));

        Assert.Contains("AddSingleton(appConfiguration.PaperFakFeeBackfill)", source, StringComparison.Ordinal);
        Assert.Contains(
            "AddSingleton<IPaperFakFeeBackfillProcessor, PaperFakFeeBackfillProcessor>",
            source,
            StringComparison.Ordinal);
        Assert.Contains("IPaperFakFeeBackfillEventRecorder", source, StringComparison.Ordinal);
        Assert.Contains("RepositoryPaperFakFeeBackfillEventRecorder", source, StringComparison.Ordinal);
        Assert.Contains(
            "AddHostedService<PaperFakFeeBackfillEventRetentionWorker>",
            source,
            StringComparison.Ordinal);
        Assert.Contains("AddHostedService<PaperFakFeeBackfillWorker>", source, StringComparison.Ordinal);
    }

    private static PaperFakFeeBackfillWorker CreateWorker(
        IPaperFakFeeBackfillProcessor processor,
        IPaperEntryPersistenceQueue paperQueue,
        IMarketDataSideEffectQueue marketDataQueue,
        IPaperFakFeeBackfillEventRecorder? eventRecorder = null)
    {
        return new PaperFakFeeBackfillWorker(
            NullLogger<PaperFakFeeBackfillWorker>.Instance,
            new PaperFakFeeBackfillOptions
            {
                Enabled = true,
                ApplyEnabled = true
            },
            processor,
            paperQueue,
            marketDataQueue,
            eventRecorder);
    }

    private static string ReadRepositorySource(
        string relativePath,
        [CallerFilePath] string sourceFilePath = "")
    {
        var testProjectDirectory = Directory.GetParent(sourceFilePath)?.FullName ??
            throw new InvalidOperationException("The test source directory is unavailable.");
        var repositoryRoot = Path.GetFullPath(Path.Combine(testProjectDirectory, "..", ".."));
        var candidate = Path.Combine(repositoryRoot, relativePath);
        if (File.Exists(candidate))
        {
            return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }

    private sealed class RecordingProcessor : IPaperFakFeeBackfillProcessor
    {
        public int Calls { get; private set; }

        public List<Guid> CycleIds { get; } = [];

        public PaperFakFeeBackfillCycleResult Result { get; set; } =
            new(1, 1, 0, false, true, null);

        public Exception? ExceptionToThrow { get; set; }

        public Task<PaperFakFeeBackfillCycleResult> RunCycleAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (ExceptionToThrow is not null)
            {
                return Task.FromException<PaperFakFeeBackfillCycleResult>(ExceptionToThrow);
            }

            return Task.FromResult(Result);
        }

        public Task<PaperFakFeeBackfillCycleResult> RunCycleAsync(
            Guid cycleId,
            CancellationToken cancellationToken = default)
        {
            CycleIds.Add(cycleId);
            return RunCycleAsync(cancellationToken);
        }
    }

    private sealed class RecordingParityProcessor(
        params HistoricalGrossNetParityCycleResult[] results) : IHistoricalGrossNetParityProcessor
    {
        private readonly Queue<HistoricalGrossNetParityCycleResult> pending = new(results);

        public List<Guid> CycleIds { get; } = [];

        public Task<HistoricalGrossNetParityCycleResult> RunCycleAsync(
            Guid workerCycleId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CycleIds.Add(workerCycleId);
            return Task.FromResult(pending.Dequeue());
        }
    }

    private sealed class RecordingEventRecorder : IPaperFakFeeBackfillEventRecorder
    {
        public List<PaperFakFeeBackfillEvent> Events { get; } = [];

        public TaskCompletionSource<PaperFakFeeBackfillEvent> FirstEvent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<PaperFakFeeBackfillEvent> FailedEvent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RecordAsync(
            PaperFakFeeBackfillEvent entry,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(entry);
            FirstEvent.TrySetResult(entry);
            if (entry.EventType == PaperFakFeeBackfillEventTypes.CycleFailed)
            {
                FailedEvent.TrySetResult(entry);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubPaperEntryPersistenceQueue(int pendingBatches) : IPaperEntryPersistenceQueue
    {
        public int PendingBatches { get; set; } = pendingBatches;

        public ValueTask EnqueueAsync(
            PaperEntryPersistenceBatch batch,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubMarketDataSideEffectQueue(int pendingUpdates) : IMarketDataSideEffectQueue
    {
        public int PendingUpdates { get; set; } = pendingUpdates;

        public MarketDataSideEffectEnqueueOutcome EnqueueUpdate(
            string component,
            MarketDataUpdate update,
            ActiveMarketAssetSnapshot? activeMarketSnapshot,
            DateTimeOffset receivedAtUtc,
            IReadOnlySet<Guid>? eligiblePaperOrderIds)
        {
            throw new NotSupportedException();
        }

        public MarketDataSideEffectEnqueueOutcome EnqueueFrameDiagnostic(
            MarketWebSocketFrameDiagnostic diagnostic,
            bool important)
        {
            throw new NotSupportedException();
        }

        public MarketDataSideEffectEnqueueOutcome EnqueueApiError(ApiError apiError)
        {
            throw new NotSupportedException();
        }

        public MarketDataSideEffectQueueMetrics GetMetrics()
        {
            return new MarketDataSideEffectQueueMetrics(
                PendingUpdates,
                PendingDiagnostics: 0,
                TrackedAssets: 0,
                EnqueuedUpdates: 0,
                CoalescedUpdates: 0,
                UpdateSoftLimitOverflows: 0,
                RejectedUpdates: 0,
                ProcessedUpdates: 0,
                FailedUpdates: 0,
                EnqueuedDiagnostics: 0,
                DroppedDiagnostics: 0,
                DiagnosticSoftLimitOverflows: 0,
                RejectedDiagnostics: 0,
                ProcessedDiagnostics: 0,
                FailedDiagnostics: 0);
        }
    }
}
