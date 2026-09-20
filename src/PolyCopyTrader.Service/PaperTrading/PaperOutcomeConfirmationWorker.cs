using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperOutcomeConfirmationWorker(
    ILogger<PaperOutcomeConfirmationWorker> logger,
    ServiceActivityState activity,
    IPaperEntryPersistenceQueue entries,
    IMarketDataSideEffectQueue marketData,
    IPaperOutcomeConfirmationProcessor processor,
    TimeProvider? timeProvider = null,
    PaperConfirmationOptions? options = null) : BackgroundService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly PaperConfirmationOptions settings = options ?? new();
    private readonly PaperConfirmationLoadControl load = new(options ?? new());
    private readonly PaperOutcomeConfirmationDiagnostics diagnostics = new(logger, timeProvider);
    private int processing, slot;
    private PaperConfirmationProgress? previousProgress;
    private DateTimeOffset nextProgress;

    public void ObserveLoad()
    {
        var m = marketData.GetMetrics();
        load.Observe(clock.GetUtcNow(), entries.PendingBatches + m.PendingUpdates +
            m.PendingDiagnostics + m.PendingMakerUpdates,
            m.FailedUpdates + m.FailedMakerUpdates + m.FailedDiagnostics +
            m.RejectedUpdates + m.RejectedMakerUpdates + m.RejectedDiagnostics +
            m.UpdateSoftLimitOverflows + m.DiagnosticSoftLimitOverflows);
    }

    // Existing explicit callers keep this entry point; admission no longer requires Idle.
    public async Task ProcessIdleGapAsync(CancellationToken token)
    {
        _ = activity; // Other activity leases and their cancellation semantics are unchanged.
        if (!settings.Enabled || load.IsPaused(clock.GetUtcNow()) ||
            Interlocked.Exchange(ref processing, 1) != 0) return;
        try
        {
            var lane = slot == 2 ? PaperConfirmationLane.Archive : PaperConfirmationLane.Recent;
            slot = (slot + 1) % 3;
            var orders = await ClaimAsync(lane, token);
            if (orders.Count == 0)
            {
                lane = lane == PaperConfirmationLane.Recent ? PaperConfirmationLane.Archive : PaperConfirmationLane.Recent;
                orders = await ClaimAsync(lane, token);
            }
            var confirmed = 0; var corrected = 0; var deferred = 0; var cacheHits = 0; var httpCalls = 0;
            foreach (var group in orders.GroupBy(x => (x.ConditionId, x.AssetId, x.Outcome, x.StrategyId, x.CopiedTraderWallet)))
            {
                token.ThrowIfCancellationRequested();
                if (load.IsPaused(clock.GetUtcNow())) break;
                var order = group.First();
                var trace = diagnostics.Begin();
                trace.SetOrder(order.Id);
                var outcome = "Deferred"; var reason = "final_outcome_missing_or_identity_conflict";
                try
                {
                    PaperOutcomeConfirmation? confirmation;
                    using (var lookup = Budget(settings.GammaTimeoutSeconds, token))
                        confirmation = await processor.LookupAsync(order, trace, lookup.Token);
                    if (confirmation is not null)
                    {
                        trace.Enter(PaperConfirmationStage.ApplyDatabase);
                        using var apply = Budget(settings.ApplyTimeoutSeconds, token);
                        var confirmations = group.Select(x => confirmation with { PaperOrderId = x.Id }).ToArray();
                        var result = await processor.ApplyGroupAsync(confirmations, trace, apply.Token);
                        reason = result.Reason;
                        if (result.Confirmed)
                        {
                            confirmed += group.Count();
                            if (result.Corrected) corrected += group.Count();
                            outcome = result.Corrected ? "Corrected" : "Matched";
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    trace.Complete("Canceled", "ServiceStopping");
                    diagnostics.End(trace, new("ServiceStopping", null));
                    throw;
                }
                catch (Exception ex)
                {
                    trace.Error(ex.GetType().Name, (ex as Npgsql.PostgresException)?.SqlState);
                    var timeout = ex is OperationCanceledException or TimeoutException ||
                        ex is Npgsql.PostgresException { SqlState: "55P03" or "57014" };
                    outcome = timeout ? "Timeout" : "Error";
                    reason = timeout ? "StageTimeout" : ex is HttpRequestException ? "HttpError" : "DatabaseError";
                    if (timeout) load.Timeout(clock.GetUtcNow());
                }
                if (outcome is not ("Matched" or "Corrected"))
                {
                    deferred += group.Count();
                    try
                    {
                        trace.Enter(PaperConfirmationStage.Defer);
                        using var retry = Budget(settings.DatabaseTimeoutSeconds, token);
                        foreach (var candidate in group)
                            await processor.DeferAsync(candidate.Id, reason, retry.Token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        trace.Complete("Canceled", "ServiceStopping"); diagnostics.End(trace, new("ServiceStopping", null)); throw;
                    }
                    catch (Exception ex)
                    {
                        trace.Error(ex.GetType().Name, (ex as Npgsql.PostgresException)?.SqlState);
                        if (outcome == "Deferred")
                        {
                            outcome = ex is OperationCanceledException or TimeoutException ? "Timeout" : "Error";
                            reason = "DeferFailed";
                        }
                        load.Timeout(clock.GetUtcNow());
                    }
                }
                trace.Complete(outcome, reason);
                var lookupCounts = trace.Snapshot();
                cacheHits += lookupCounts.CacheHits; httpCalls += lookupCounts.HttpCalls;
                diagnostics.End(trace, new("Unknown", null));
            }
            if (orders.Count > 0 && deferred == 0 && confirmed == orders.Count) load.Succeeded();
            if (orders.Count > 0) logger.LogInformation(
                "Paper confirmation portion. Lane={Lane} Selected={Selected} Confirmed={Confirmed} Corrected={Corrected} Deferred={Deferred} BatchLimit={BatchLimit} Paused={Paused} CacheHits={CacheHits} HttpCalls={HttpCalls}",
                lane, orders.Count, confirmed, corrected, deferred, load.BatchSize, load.IsPaused(clock.GetUtcNow()),cacheHits,httpCalls);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            load.Timeout(clock.GetUtcNow());
            logger.LogWarning("Paper confirmation selection failed. ErrorType={ErrorType} SqlState={SqlState}",
                ex.GetType().Name, (ex as Npgsql.PostgresException)?.SqlState);
        }
        finally { Volatile.Write(ref processing, 0); }
    }

    private async Task<IReadOnlyList<PaperOrder>> ClaimAsync(PaperConfirmationLane lane, CancellationToken token)
    {
        var trace = diagnostics.Begin(); trace.Enter(PaperConfirmationStage.Claim);
        using var budget = Budget(settings.DatabaseTimeoutSeconds, token);
        try
        {
            var result = await processor.ClaimBatchAsync(lane, load.BatchSize, budget.Token);
            trace.Complete("NoCandidate", "SelectionCompleted");
            diagnostics.End(trace, new("Unknown", null));
            return result;
        }
        catch (Exception ex)
        {
            trace.Error(ex.GetType().Name,(ex as Npgsql.PostgresException)?.SqlState);
            trace.Complete(token.IsCancellationRequested ? "Canceled" : ex is OperationCanceledException ? "Timeout" : "Error",
                token.IsCancellationRequested ? "ServiceStopping" : "SelectionFailed");
            diagnostics.End(trace, new(token.IsCancellationRequested ? "ServiceStopping" : "Unknown", null));
            throw;
        }
    }

    private StageBudget Budget(int seconds, CancellationToken token) => new(seconds,clock,token);
    private sealed class StageBudget : IDisposable
    {
        private readonly CancellationTokenSource deadline, linked;
        public StageBudget(int seconds,TimeProvider clock,CancellationToken token)
        {
            deadline = new(TimeSpan.FromSeconds(seconds),clock);
            linked = CancellationTokenSource.CreateLinkedTokenSource(token,deadline.Token);
        }
        public CancellationToken Token => linked.Token;
        public void Dispose() { linked.Dispose(); deadline.Dispose(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        await Task.WhenAll(Run(ProcessAsync), Run(MonitorAsync));
        async Task Run(Func<CancellationToken, Task> run)
        {
            try { await run(lifetime.Token); }
            finally { await lifetime.CancelAsync(); }
        }
    }
    private async Task ProcessAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await ProcessIdleGapAsync(token);
            if (clock.GetUtcNow() >= nextProgress)
            {
                nextProgress = clock.GetUtcNow().AddSeconds(30);
                try
                {
                    using var budget = Budget(settings.DatabaseTimeoutSeconds, token);
                    var progress = await processor.GetProgressAsync(budget.Token);
                    var rates = progress is null ? null : PaperConfirmationRates.Between(previousProgress, progress);
                    logger.LogInformation("Paper confirmation durable all-history progress. Progress={@Progress} Rates={@Rates}", progress, rates);
                    if (progress is { Initialized: true }) previousProgress = progress;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogWarning("Paper confirmation progress unavailable. ErrorType={ErrorType}", ex.GetType().Name); }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(settings.BatchDelayMilliseconds), clock, token);
        }
    }
    private async Task MonitorAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await timer.WaitForNextTickAsync(token))
        {
            ObserveLoad();
            diagnostics.LogSummaryIfDue();
        }
    }
}
