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
    public async Task IdleGapProcessesOneCandidate_AndForegroundOrQueuesPreventStarting()
    {
        var activity = new ServiceActivityState();
        var entries = new EntryQueue();
        var market = new MarketQueue();
        var processor = new Processor();
        var worker = new PaperOutcomeConfirmationWorker(NullLogger<PaperOutcomeConfirmationWorker>.Instance,
            activity, entries, market, processor);
        using (activity.EnterTradingCycle()) await worker.ProcessIdleGapAsync(default);
        entries.PendingBatches = 1;
        await worker.ProcessIdleGapAsync(default);
        entries.PendingBatches = 0;
        market.InFlight = 1;
        await worker.ProcessIdleGapAsync(default);
        Assert.Equal(0, processor.Calls);
        market.InFlight = 0;
        await worker.ProcessIdleGapAsync(default);
        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task ForegroundPreemptsRunningLookup_AndNextGapCanResume()
    {
        var activity = new ServiceActivityState();
        var processor = new Processor { WaitForCancellation = true };
        var worker = new PaperOutcomeConfirmationWorker(NullLogger<PaperOutcomeConfirmationWorker>.Instance,
            activity, new EntryQueue(), new MarketQueue(), processor);
        var attempt = worker.ProcessIdleGapAsync(default);
        await processor.Started.Task;
        using (activity.EnterTradingCycle()) await attempt.WaitAsync(TimeSpan.FromSeconds(2));
        processor.WaitForCancellation = false;
        await worker.ProcessIdleGapAsync(default);
        Assert.Equal(2, processor.Calls);
    }

    private sealed class Processor : IPaperOutcomeConfirmationProcessor
    {
        public int Calls;
        public bool WaitForCancellation;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ProcessOneAsync(ServiceActivityState.BackgroundLease idle, CancellationToken cancellationToken = default)
        {
            Calls++;
            Started.TrySetResult();
            if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class EntryQueue : IPaperEntryPersistenceQueue
    {
        public int PendingBatches { get; set; }
        public ValueTask EnqueueAsync(PaperEntryPersistenceBatch batch, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class MarketQueue : IMarketDataSideEffectQueue
    {
        public int InFlight;
        public MarketDataSideEffectQueueMetrics GetMetrics() => new(0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,InFlightGeneralUpdates:InFlight);
        public MarketDataSideEffectEnqueueOutcome EnqueueUpdate(string component, MarketDataUpdate update,
            ActiveMarketAssetSnapshot? activeMarketSnapshot, DateTimeOffset receivedAtUtc, IReadOnlySet<Guid>? eligiblePaperOrderIds)
            => MarketDataSideEffectEnqueueOutcome.Enqueued;
        public MarketDataSideEffectEnqueueOutcome EnqueueFrameDiagnostic(MarketWebSocketFrameDiagnostic diagnostic, bool important)
            => MarketDataSideEffectEnqueueOutcome.Enqueued;
        public MarketDataSideEffectEnqueueOutcome EnqueueApiError(ApiError apiError) => MarketDataSideEffectEnqueueOutcome.Enqueued;
    }
}
