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
        using var idle = activity.TryEnterIdle(QueuesEmpty, token, out var observation);
        diagnostics.ObserveIdle(observation, ReadQueues()); // Separate, timestamped queue observation.
        if (idle is null) return;
        var trace = idle.Trace = diagnostics.Begin();
        try { await processor.ProcessOneAsync(idle, idle.Token); }
        catch (OperationCanceledException) when (idle.Token.IsCancellationRequested)
        { trace.Complete("Canceled", idle.Cancellation.Reason); }
        catch (Exception ex)
        {
            trace.Error(ex.GetType().Name, (ex as Npgsql.PostgresException)?.SqlState);
            trace.Complete("Error", ex is Npgsql.NpgsqlException ? "DatabaseError" : "UnknownError");
        }
        finally { diagnostics.End(trace, idle.Cancellation); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Independent telemetry timer: a slow in-flight attempt cannot hide its stage.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        await Task.WhenAll(RunAsync(ProcessLoopAsync), RunAsync(SummaryLoopAsync));

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
