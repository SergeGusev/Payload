using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class PaperOutcomeConfirmationWorkerTests
{
    [Fact]
    public async Task BoundedPortionRunsBetweenAndDuringTradingWithoutIdleAdmission()
    {
        var activity = new ServiceActivityState();
        var entries = new EntryQueue { PendingBatches = 1 }; var processor = new Processor();
        using var worker = new PaperOutcomeConfirmationWorker(NullLogger<PaperOutcomeConfirmationWorker>.Instance,
            activity, entries, new MarketQueue(), processor);
        using (activity.EnterTradingCycle()) await worker.ProcessIdleGapAsync(default);
        Assert.Equal(["Claim", "Lookup", "Apply"], processor.Calls);
    }
    [Fact]
    public async Task NonemptyLanesReceiveTwoRecentPortionsThenOneArchive()
    {
        var processor=new Processor();
        using var worker=new PaperOutcomeConfirmationWorker(NullLogger<PaperOutcomeConfirmationWorker>.Instance,
            new ServiceActivityState(),new EntryQueue(),new MarketQueue(),processor);
        for(var i=0;i<6;i++)await worker.ProcessIdleGapAsync(default);
        Assert.Equal([PaperConfirmationLane.Recent,PaperConfirmationLane.Recent,PaperConfirmationLane.Archive,
            PaperConfirmationLane.Recent,PaperConfirmationLane.Recent,PaperConfirmationLane.Archive],processor.Lanes);
    }

    [Theory]
    [InlineData("Claim")]
    [InlineData("Lookup")]
    [InlineData("Apply")]
    public async Task ForegroundDoesNotWaitOrCancelAdmittedStage_AndNoStageOverlaps(string stage)
    {
        var activity = new ServiceActivityState(); var processor = new Processor();
        using var worker = new PaperOutcomeConfirmationWorker(NullLogger<PaperOutcomeConfirmationWorker>.Instance,
            activity, new EntryQueue(), new MarketQueue(), processor);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        processor.OnStep = async (name, token) =>
        {
            if (name != stage) return;
            started.SetResult(); await release.Task;
            Assert.False(token.IsCancellationRequested);
        };
        var running = worker.ProcessIdleGapAsync(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        try
        {
            using var foreground = await Task.Run(() => activity.EnterTradingCycle("Quote", MarketDataEventType.PriceChange))
                .WaitAsync(TimeSpan.FromSeconds(1));
            var before = processor.Calls.Count;
            await worker.ProcessIdleGapAsync(default); // Concurrent caller cannot duplicate current stage.
            Assert.Equal(before, processor.Calls.Count);
            release.SetResult(); await running.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(3, processor.Calls.Count);
        }
        finally { release.TrySetResult(); }
        while (processor.Calls.Count < 3) await worker.ProcessIdleGapAsync(default);
        Assert.Equal(["Claim", "Lookup", "Apply"], processor.Calls);
    }

    private sealed class Processor : IPaperOutcomeConfirmationProcessor
    {
        public List<PaperConfirmationLane> Lanes { get; } = [];
        public List<string> Calls { get; } = [];
        public async Task<IReadOnlyList<PaperOrder>> ClaimBatchAsync(PaperConfirmationLane lane, int limit, CancellationToken token)
        { Lanes.Add(lane); return [(await ClaimAsync(token))!]; }
        public Task<PaperOutcomeConfirmationResult> ApplyGroupAsync(IReadOnlyList<PaperOutcomeConfirmation> confirmations,
            PaperOutcomeConfirmationTrace trace, CancellationToken token) => ApplyAsync(confirmations[0], trace, token);
        public Func<string, CancellationToken, Task> OnStep = (_, _) => Task.CompletedTask;
        public async Task<PaperOrder?> ClaimAsync(CancellationToken token)
        { Calls.Add("Claim"); await OnStep("Claim", token); return PaperOutcomeConfirmationProcessorTests.Order(); }
        public async Task<PaperOutcomeConfirmation?> LookupAsync(PaperOrder order, PaperOutcomeConfirmationTrace trace, CancellationToken token)
        { Calls.Add("Lookup"); await OnStep("Lookup", token); return PaperOutcomeConfirmationProcessor.Resolve(order, [PaperOutcomeConfirmationProcessorTests.Metadata()], DateTimeOffset.UtcNow); }
        public async Task<PaperOutcomeConfirmationResult> ApplyAsync(PaperOutcomeConfirmation confirmation, PaperOutcomeConfirmationTrace trace, CancellationToken token)
        { Calls.Add("Apply"); await OnStep("Apply", token); return new(true, false, "confirmed"); }
        public Task DeferAsync(Guid id, string reason, CancellationToken token) => throw new NotSupportedException();
    }

    internal sealed class EntryQueue : IPaperEntryPersistenceQueue
    {
        public int PendingBatches { get; set; }
        public ValueTask EnqueueAsync(PaperEntryPersistenceBatch batch, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    internal sealed class MarketQueue : IMarketDataSideEffectQueue
    {
        public int InFlight;
        public MarketDataSideEffectQueueMetrics GetMetrics() => new(0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,InFlightGeneralUpdates:InFlight);
        public MarketDataSideEffectEnqueueOutcome EnqueueUpdate(string component, MarketDataUpdate update,
            ActiveMarketAssetSnapshot? activeMarketSnapshot, DateTimeOffset receivedAtUtc,
            IReadOnlySet<Guid>? eligiblePaperOrderIds) => MarketDataSideEffectEnqueueOutcome.Enqueued;
        public MarketDataSideEffectEnqueueOutcome EnqueueFrameDiagnostic(MarketWebSocketFrameDiagnostic diagnostic, bool important) => MarketDataSideEffectEnqueueOutcome.Enqueued;
        public MarketDataSideEffectEnqueueOutcome EnqueueApiError(ApiError apiError) => MarketDataSideEffectEnqueueOutcome.Enqueued;
    }
}
