using System.Diagnostics;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperFakFeeBackfillWorker(
    ILogger<PaperFakFeeBackfillWorker> logger,
    PaperFakFeeBackfillOptions options,
    IPaperFakFeeBackfillProcessor processor,
    IPaperEntryPersistenceQueue paperEntryPersistenceQueue,
    IMarketDataSideEffectQueue marketDataSideEffectQueue,
    IPaperFakFeeBackfillEventRecorder? eventRecorder = null) : BackgroundService
{
    private bool deferNextCycleForForeground = true;
    private Guid? activeCycleId;
    private long activeCycleStartedTimestamp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!options.Enabled)
            {
                logger.LogInformation("Historical Paper FAK fee backfill is disabled.");
                await TryRecordEventAsync(
                    CreateWorkerEvent(
                        PaperFakFeeBackfillEventTypes.WorkerDisabled,
                        PaperFakFeeBackfillEventLevels.Information,
                        "Historical Paper FAK fee backfill is disabled."),
                    stoppingToken).ConfigureAwait(false);
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
            await TryRecordEventAsync(
                CreateWorkerEvent(
                    PaperFakFeeBackfillEventTypes.WorkerStarted,
                    PaperFakFeeBackfillEventLevels.Information,
                    "Historical Paper FAK fee backfill worker started."),
                stoppingToken).ConfigureAwait(false);

            if (!options.ApplyEnabled)
            {
                logger.LogWarning(
                    "Historical Paper FAK fee backfill is running in read-only preview mode; no historical rows will be updated.");
                await TryRecordEventAsync(
                    CreateWorkerEvent(
                        PaperFakFeeBackfillEventTypes.PreviewMode,
                        PaperFakFeeBackfillEventLevels.Warning,
                        "Historical Paper FAK fee backfill is running in read-only preview mode."),
                    stoppingToken).ConfigureAwait(false);
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
                    ClearActiveCycle();
                    break;
                }
                catch (Exception ex)
                {
                    var failedCycleId = activeCycleId;
                    var durationMilliseconds = GetActiveCycleDurationMilliseconds();
                    ClearActiveCycle();
                    delay = TimeSpan.FromSeconds(errorDelaySeconds);
                    logger.LogError(
                        ex,
                        "Historical Paper FAK fee backfill cycle failed. Retrying in {DelaySeconds} seconds.",
                        errorDelaySeconds);
                    await TryRecordEventAsync(
                        CreateWorkerEvent(
                            PaperFakFeeBackfillEventTypes.CycleFailed,
                            PaperFakFeeBackfillEventLevels.Error,
                            "Historical Paper FAK fee backfill cycle failed; a retry is scheduled.") with
                        {
                            CycleId = failedCycleId,
                            DelaySeconds = errorDelaySeconds,
                            DurationMilliseconds = durationMilliseconds,
                            ExceptionType = ex.GetType().FullName,
                            ExceptionMessage = ex.Message
                        },
                        stoppingToken).ConfigureAwait(false);
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
        }
        finally
        {
            ClearActiveCycle();
            logger.LogInformation("Historical Paper FAK fee backfill worker stopped.");
            await TryRecordEventAsync(
                CreateWorkerEvent(
                    PaperFakFeeBackfillEventTypes.WorkerStopped,
                    PaperFakFeeBackfillEventLevels.Information,
                    "Historical Paper FAK fee backfill worker stopped."),
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    internal async Task<PaperFakFeeBackfillWorkerCycleDisposition> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        var pendingPaperEntryBatches = paperEntryPersistenceQueue.PendingBatches;
        var marketDataQueueMetrics = marketDataSideEffectQueue.GetMetrics();
        var foregroundWorkPending = pendingPaperEntryBatches > 0 ||
            marketDataQueueMetrics.PendingUpdates > 0;
        if (foregroundWorkPending && deferNextCycleForForeground)
        {
            deferNextCycleForForeground = false;
            logger.LogDebug(
                "Historical Paper FAK fee backfill cycle deferred once for foreground queues. " +
                "PendingPaperEntryBatches={PendingPaperEntryBatches} PendingMarketDataUpdates={PendingMarketDataUpdates}",
                pendingPaperEntryBatches,
                marketDataQueueMetrics.PendingUpdates);
            await TryRecordEventAsync(
                CreateWorkerEvent(
                    PaperFakFeeBackfillEventTypes.ForegroundDeferred,
                    PaperFakFeeBackfillEventLevels.Information,
                    "Historical Paper FAK fee backfill cycle deferred once for foreground queues.") with
                {
                    PendingPaperEntryBatches = pendingPaperEntryBatches,
                    PendingMarketDataUpdates = marketDataQueueMetrics.PendingUpdates
                },
                cancellationToken).ConfigureAwait(false);
            return PaperFakFeeBackfillWorkerCycleDisposition.ForegroundWorkPending;
        }

        if (foregroundWorkPending)
        {
            logger.LogInformation(
                "Historical Paper FAK fee backfill is taking one bounded cycle after yielding to persistent " +
                "foreground queues. PendingPaperEntryBatches={PendingPaperEntryBatches} " +
                "PendingMarketDataUpdates={PendingMarketDataUpdates}",
                pendingPaperEntryBatches,
                marketDataQueueMetrics.PendingUpdates);
        }

        deferNextCycleForForeground = true;

        var cycleId = Guid.NewGuid();
        activeCycleId = cycleId;
        activeCycleStartedTimestamp = Stopwatch.GetTimestamp();
        await TryRecordEventAsync(
            CreateWorkerEvent(
                PaperFakFeeBackfillEventTypes.CycleStarted,
                PaperFakFeeBackfillEventLevels.Information,
                "Historical Paper FAK fee backfill cycle started.") with
            {
                CycleId = cycleId,
                PendingPaperEntryBatches = pendingPaperEntryBatches,
                PendingMarketDataUpdates = marketDataQueueMetrics.PendingUpdates
            },
            cancellationToken).ConfigureAwait(false);

        var result = await processor.RunCycleAsync(cycleId, cancellationToken).ConfigureAwait(false);
        ClearActiveCycle();
        return result.ReachedEnd
            ? PaperFakFeeBackfillWorkerCycleDisposition.SweepIdle
            : PaperFakFeeBackfillWorkerCycleDisposition.Processed;
    }

    private PaperFakFeeBackfillEvent CreateWorkerEvent(
        string eventType,
        string level,
        string message)
    {
        return new PaperFakFeeBackfillEvent
        {
            EventType = eventType,
            Level = level,
            Message = message,
            BackfillEnabled = options.Enabled,
            ApplyEnabled = options.ApplyEnabled,
            CutoffUtc = options.HistoricalCutoffUtc,
            BatchSize = options.BatchSize
        };
    }

    private async Task TryRecordEventAsync(
        PaperFakFeeBackfillEvent entry,
        CancellationToken cancellationToken)
    {
        if (eventRecorder is null)
        {
            return;
        }

        try
        {
            await eventRecorder.RecordAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Historical Paper FAK fee backfill database-event recorder failed unexpectedly. " +
                "EventType={EventType}. File logging remains active.",
                entry.EventType);
        }
    }

    private long? GetActiveCycleDurationMilliseconds()
    {
        if (activeCycleId is null || activeCycleStartedTimestamp == 0)
        {
            return null;
        }

        return (long)Stopwatch.GetElapsedTime(activeCycleStartedTimestamp).TotalMilliseconds;
    }

    private void ClearActiveCycle()
    {
        activeCycleId = null;
        activeCycleStartedTimestamp = 0;
    }
}

internal enum PaperFakFeeBackfillWorkerCycleDisposition
{
    Processed,
    ForegroundWorkPending,
    SweepIdle
}
