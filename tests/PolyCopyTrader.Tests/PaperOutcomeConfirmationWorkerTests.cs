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
    public async Task IdleAdmissionAndQueuesAreCheckedBeforeEveryStage()
    {
        var activity = new ServiceActivityState();
        var entries = new EntryQueue(); var market = new MarketQueue(); var processor = new Processor();
        using var worker = new PaperOutcomeConfirmationWorker(NullLogger<PaperOutcomeConfirmationWorker>.Instance,
            activity, entries, market, processor);
        using (activity.EnterTradingCycle()) await worker.ProcessIdleGapAsync(default);
        entries.PendingBatches = 1; await worker.ProcessIdleGapAsync(default);
        entries.PendingBatches = 0; market.InFlight = 1; await worker.ProcessIdleGapAsync(default);
        Assert.Empty(processor.Calls);
        market.InFlight = 0; await worker.ProcessIdleGapAsync(default);
        Assert.Equal(["Claim"], processor.Calls);
        using (activity.EnterTradingCycle()) await worker.ProcessIdleGapAsync(default);
        Assert.Equal(["Claim"], processor.Calls);
        await worker.ProcessIdleGapAsync(default);
        market.InFlight = 1;
        for (var i = 0; i < 100; i++) await worker.ProcessIdleGapAsync(default);
        Assert.Equal(["Claim", "Lookup"], processor.Calls);
        market.InFlight = 0; await worker.ProcessIdleGapAsync(default);
        Assert.Equal(["Claim", "Lookup", "Apply"], processor.Calls);
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
        if (stage != "Claim") await worker.ProcessIdleGapAsync(default);
        if (stage == "Apply") await worker.ProcessIdleGapAsync(default);
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
            await worker.ProcessIdleGapAsync(default); // Retained state waits for a later idle gap.
            Assert.Equal(before, processor.Calls.Count);
        }
        finally { release.TrySetResult(); }
        while (processor.Calls.Count < 3) await worker.ProcessIdleGapAsync(default);
        Assert.Equal(["Claim", "Lookup", "Apply"], processor.Calls);
    }

    private sealed class Processor : IPaperOutcomeConfirmationProcessor
    {
        public List<string> Calls { get; } = [];
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
