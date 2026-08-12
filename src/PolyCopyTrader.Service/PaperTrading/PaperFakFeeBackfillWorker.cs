using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperFakFeeBackfillWorker(
    ILogger<PaperFakFeeBackfillWorker> logger,
    PaperFakFeeBackfillOptions options,
    IPaperFakFeeBackfillProcessor processor,
    IPaperEntryPersistenceQueue paperEntryPersistenceQueue,
    IMarketDataSideEffectQueue marketDataSideEffectQueue) : BackgroundService
{
    private bool deferNextCycleForMarketData = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Historical Paper FAK fee backfill is disabled.");
            return;
        }

        logger.LogInformation(
            "Historical Paper FAK fee backfill worker started. ApplyEnabled={ApplyEnabled} " +
            "HistoricalCutoffUtc={HistoricalCutoffUtc:O} BatchSize={BatchSize} " +
            "CycleIntervalSeconds={CycleIntervalSeconds} InitialDelaySeconds={InitialDelaySeconds} " +
            "IdleDelaySeconds={IdleDelaySeconds} ErrorDelaySeconds={ErrorDelaySeconds} " +
            "MaxErrorDelaySeconds={MaxErrorDelaySeconds}",
            options.ApplyEnabled,
            options.HistoricalCutoffUtc,
            options.BatchSize,
            options.CycleIntervalSeconds,
            options.InitialDelaySeconds,
            options.IdleDelaySeconds,
            options.ErrorDelaySeconds,
            options.MaxErrorDelaySeconds);

        if (!options.ApplyEnabled)
        {
            logger.LogWarning(
                "Historical Paper FAK fee backfill is running in read-only preview mode; no historical rows will be updated.");
        }

        try
        {
            if (options.InitialDelaySeconds > 0)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.InitialDelaySeconds),
                    stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        var errorDelaySeconds = options.ErrorDelaySeconds;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                var disposition = await RunCycleAsync(stoppingToken).ConfigureAwait(false);
                errorDelaySeconds = options.ErrorDelaySeconds;
                delay = disposition == PaperFakFeeBackfillWorkerCycleDisposition.SweepIdle
                    ? TimeSpan.FromSeconds(options.IdleDelaySeconds)
                    : TimeSpan.FromSeconds(options.CycleIntervalSeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                delay = TimeSpan.FromSeconds(errorDelaySeconds);
                logger.LogError(
                    ex,
                    "Historical Paper FAK fee backfill cycle failed. Retrying in {DelaySeconds} seconds.",
                    errorDelaySeconds);
                errorDelaySeconds = (int)Math.Min(
                    options.MaxErrorDelaySeconds,
                    (long)errorDelaySeconds * 2L);
            }

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Historical Paper FAK fee backfill worker stopped.");
    }

    internal async Task<PaperFakFeeBackfillWorkerCycleDisposition> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        var pendingPaperEntryBatches = paperEntryPersistenceQueue.PendingBatches;
        var marketDataQueueMetrics = marketDataSideEffectQueue.GetMetrics();
        if (pendingPaperEntryBatches > 0)
        {
            logger.LogDebug(
                "Historical Paper FAK fee backfill cycle deferred for foreground queues. " +
                "PendingPaperEntryBatches={PendingPaperEntryBatches} PendingMarketDataUpdates={PendingMarketDataUpdates}",
                pendingPaperEntryBatches,
                marketDataQueueMetrics.PendingUpdates);
            return PaperFakFeeBackfillWorkerCycleDisposition.ForegroundWorkPending;
        }

        if (marketDataQueueMetrics.PendingUpdates > 0 && deferNextCycleForMarketData)
        {
            deferNextCycleForMarketData = false;
            logger.LogDebug(
                "Historical Paper FAK fee backfill cycle deferred once for the market-data side-effect queue. " +
                "PendingMarketDataUpdates={PendingMarketDataUpdates}",
                marketDataQueueMetrics.PendingUpdates);
            return PaperFakFeeBackfillWorkerCycleDisposition.ForegroundWorkPending;
        }

        if (marketDataQueueMetrics.PendingUpdates > 0)
        {
            logger.LogInformation(
                "Historical Paper FAK fee backfill is taking one bounded cycle after yielding to a persistent " +
                "market-data side-effect backlog. PendingMarketDataUpdates={PendingMarketDataUpdates}",
                marketDataQueueMetrics.PendingUpdates);
        }

        deferNextCycleForMarketData = true;

        var result = await processor.RunCycleAsync(cancellationToken).ConfigureAwait(false);
        return result.ReachedEnd
            ? PaperFakFeeBackfillWorkerCycleDisposition.SweepIdle
            : PaperFakFeeBackfillWorkerCycleDisposition.Processed;
    }
}

internal enum PaperFakFeeBackfillWorkerCycleDisposition
{
    Processed,
    ForegroundWorkPending,
    SweepIdle
}
