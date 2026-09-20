using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperOutcomeConfirmationWorker(
    ILogger<PaperOutcomeConfirmationWorker> logger,
    ServiceActivityState activity,
    IPaperEntryPersistenceQueue entries,
    IMarketDataSideEffectQueue marketData,
    IPaperOutcomeConfirmationProcessor processor) : BackgroundService
{
    public bool QueuesEmpty()
    {
        var metrics = marketData.GetMetrics();
        return entries.PendingBatches == 0 && metrics.PendingUpdates == 0 &&
            metrics.PendingDiagnostics == 0 && metrics.PendingMakerUpdates == 0 &&
            metrics.PendingGeneralUpdates == 0 && metrics.InFlightGeneralUpdates == 0 &&
            metrics.InFlightMakerUpdates == 0;
    }

    public async Task ProcessIdleGapAsync(CancellationToken token)
    {
        using var idle = activity.TryEnterIdle(QueuesEmpty, token);
        if (idle is null) return;
        try { await processor.ProcessOneAsync(idle, idle.Token); }
        catch (OperationCanceledException) when (idle.Token.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Paper outcome confirmation failed; unconfirmed work will retry."); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ProcessIdleGapAsync(stoppingToken);
    }
}
