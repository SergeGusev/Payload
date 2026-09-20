using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperOutcomeConfirmationWorker(
    ILogger<PaperOutcomeConfirmationWorker> logger,
    ServiceActivityState activity,
    IPaperEntryPersistenceQueue entries,
    IMarketDataSideEffectQueue marketData,
    IPaperOutcomeConfirmationProcessor processor,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly PaperOutcomeConfirmationDiagnostics diagnostics = new(logger, timeProvider);
    private PendingAttempt? pending;
    private int processing;

    private enum Step { Claim, Lookup, Apply, Defer }
    private sealed class PendingAttempt(PaperOutcomeConfirmationTrace trace)
    {
        public PaperOutcomeConfirmationTrace Trace { get; } = trace;
        public Step Next { get; set; }
        public PaperOrder? Order { get; set; }
        public PaperOutcomeConfirmation? Confirmation { get; set; }
        public string DeferReason { get; set; } = "Unknown";
        public string Outcome { get; set; } = "Deferred";
        public string Reason { get; set; } = "Unknown";
    }

    private PaperConfirmationQueueSnapshot ReadQueues()
    {
        var metrics = marketData.GetMetrics();
        return new(clock.GetUtcNow(), entries.PendingBatches, metrics.PendingUpdates, metrics.PendingDiagnostics,
            metrics.PendingGeneralUpdates, metrics.InFlightGeneralUpdates, metrics.PendingMakerUpdates,
            metrics.InFlightMakerUpdates);
    }
    public bool QueuesEmpty()
    {
        var queues = ReadQueues();
        return queues.PendingBatches == 0 && queues.PendingUpdates == 0 &&
            queues.PendingDiagnostics == 0 && queues.PendingMaker == 0 &&
            queues.PendingGeneral == 0 && queues.InFlightGeneral == 0 && queues.InFlightMaker == 0;
    }

    public async Task ProcessIdleGapAsync(CancellationToken token)
    {
        // Also serialize explicit callers: no second candidate or stage can overlap.
        if (Interlocked.Exchange(ref processing, 1) != 0) return;
        try
        {
            using var idle = activity.TryEnterIdle(QueuesEmpty, token, out var observation,
                cancelOnForeground: false);
            diagnostics.ObserveIdle(observation, ReadQueues()); // Separate, timestamped queue observation.
            if (idle is null) return;
            var attempt = pending ??= new(diagnostics.Begin());
            var trace = attempt.Trace;
            var step = attempt.Next;
            using var deadline = new CancellationTokenSource(
                TimeSpan.FromSeconds(step == Step.Lookup ? 5 : 2), clock);
            using var stageStop = CancellationTokenSource.CreateLinkedTokenSource(idle.Token, deadline.Token);
            try
            {
                stageStop.Token.ThrowIfCancellationRequested();
                switch (step)
                {
                    case Step.Claim:
                        trace.Enter(PaperConfirmationStage.Claim);
                        attempt.Order = await processor.ClaimAsync(stageStop.Token);
                        if (attempt.Order is null) { Finish("NoCandidate", "QueueEmpty"); break; }
                        trace.SetOrder(attempt.Order.Id);
                        WaitFor(Step.Lookup, PaperConfirmationStage.WaitingForLookupIdle);
                        break;
                    case Step.Lookup:
                        trace.Enter(PaperConfirmationStage.GammaToken);
                        attempt.Confirmation = await processor.LookupAsync(attempt.Order!, trace, stageStop.Token);
                        if (attempt.Confirmation is null)
                            Defer("Deferred", "final_outcome_missing_or_identity_conflict",
                                "final_outcome_missing_or_identity_conflict");
                        else WaitFor(Step.Apply, PaperConfirmationStage.WaitingForApplyIdle);
                        break;
                    case Step.Apply:
                        trace.Enter(PaperConfirmationStage.ApplyDatabase);
                        var result = await processor.ApplyAsync(attempt.Confirmation!, trace, stageStop.Token);
                        if (result.Confirmed) Finish(result.Corrected ? "Corrected" : "Matched", result.Reason);
                        else Defer("Deferred", result.Reason, result.Reason);
                        break;
                    case Step.Defer:
                        trace.Enter(PaperConfirmationStage.Defer);
                        await processor.DeferAsync(attempt.Order!.Id, attempt.DeferReason, stageStop.Token);
                        Finish(attempt.Outcome, attempt.Reason);
                        break;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Finish("Canceled", "ServiceStopping");
            }
            catch (Exception ex)
            {
                trace.Error(ex.GetType().Name, (ex as Npgsql.PostgresException)?.SqlState);
                var timedOut = deadline.IsCancellationRequested;
                var reason = timedOut ? (step == Step.Lookup ? "GammaTimeout" : "DatabaseTimeout") :
                    ex is HttpRequestException ? "HttpError" : ex is Npgsql.NpgsqlException ? "DatabaseError" : "UnknownError";
                var outcome = timedOut ? "Timeout" : "Error";
                // Preserve the original persisted retry payload; logs retain only safe type/state fields.
                if (step == Step.Defer || attempt.Order is null) Finish(outcome, reason);
                else Defer(outcome, reason, ex.GetType().Name + ": " + ex.Message);
            }

            void WaitFor(Step next, PaperConfirmationStage waitingStage)
            {
                attempt.Next = next;
                trace.Complete("Waiting", "WaitingForIdle");
                trace.Enter(waitingStage); // Once per transition, never once per busy tick.
            }
            void Defer(string outcome, string reason, string persistedReason)
            {
                attempt.Confirmation = null;
                attempt.Outcome = outcome;
                attempt.Reason = reason;
                attempt.DeferReason = persistedReason;
                WaitFor(Step.Defer, PaperConfirmationStage.WaitingForDeferIdle);
            }
        }
        finally { Volatile.Write(ref processing, 0); }
    }

    private void Finish(string outcome, string reason)
    {
        var attempt = pending;
        if (attempt is null) return;
        pending = null;
        attempt.Trace.Complete(outcome, reason);
        diagnostics.End(attempt.Trace, new(outcome == "Canceled" ? reason : "Unknown", null));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Independent telemetry timer: a slow in-flight attempt cannot hide its stage.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        try { await Task.WhenAll(RunAsync(ProcessLoopAsync), RunAsync(SummaryLoopAsync)); }
        finally
        {
            // A retained result has no open resources; durable claim/retry state survives restart.
            Finish("Canceled", stoppingToken.IsCancellationRequested ? "ServiceStopping" : "WorkerStopping");
        }

        async Task RunAsync(Func<CancellationToken, Task> loop)
        {
            try { await loop(lifetime.Token); }
            finally { await lifetime.CancelAsync(); }
        }
    }
    private async Task ProcessLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await timer.WaitForNextTickAsync(token)) await ProcessIdleGapAsync(token);
    }
    private async Task SummaryLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        while (await timer.WaitForNextTickAsync(token)) diagnostics.LogSummaryIfDue();
    }
}
