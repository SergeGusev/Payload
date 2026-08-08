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
    public async Task Worker_DoesNotCallProcessorWhenDisabled()
    {
        var processor = new RecordingProcessor();
        var worker = new PaperFakFeeBackfillWorker(
            NullLogger<PaperFakFeeBackfillWorker>.Instance,
            new PaperFakFeeBackfillOptions { Enabled = false },
            processor,
            new StubPaperEntryPersistenceQueue(0),
            new StubMarketDataSideEffectQueue(0));

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, processor.Calls);
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
        Assert.Contains("AddHostedService<PaperFakFeeBackfillWorker>", source, StringComparison.Ordinal);
    }

    private static PaperFakFeeBackfillWorker CreateWorker(
        IPaperFakFeeBackfillProcessor processor,
        IPaperEntryPersistenceQueue paperQueue,
        IMarketDataSideEffectQueue marketDataQueue)
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
            marketDataQueue);
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

        public PaperFakFeeBackfillCycleResult Result { get; set; } =
            new(1, 1, 0, false, true, null);

        public Task<PaperFakFeeBackfillCycleResult> RunCycleAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubPaperEntryPersistenceQueue(int pendingBatches) : IPaperEntryPersistenceQueue
    {
        public int PendingBatches { get; } = pendingBatches;

        public ValueTask EnqueueAsync(
            PaperEntryPersistenceBatch batch,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubMarketDataSideEffectQueue(int pendingUpdates) : IMarketDataSideEffectQueue
    {
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
                PendingUpdates: pendingUpdates,
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
