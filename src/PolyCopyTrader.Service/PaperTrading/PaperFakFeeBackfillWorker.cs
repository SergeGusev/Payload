using System.Diagnostics;
using Microsoft.Extensions.Configuration;
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
    IPaperFakFeeBackfillEventRecorder? eventRecorder = null,
    HistoricalGrossNetParityOptions? historicalGrossNetParityOptions = null,
    IHistoricalGrossNetParityProcessor? historicalGrossNetParityProcessor = null,
    AppConfiguration? appConfiguration = null,
    IConfiguration? configuration = null,
    IServiceProvider? serviceProvider = null) : BackgroundService
{
    private readonly HistoricalGrossNetParityOptions parityOptions =
        historicalGrossNetParityOptions ??
        configuration?.GetSection("HistoricalGrossNetParity").Get<HistoricalGrossNetParityOptions>() ??
        appConfiguration?.HistoricalGrossNetParity ??
        new HistoricalGrossNetParityOptions { Enabled = false };
    private IHistoricalGrossNetParityProcessor? parityProcessor = historicalGrossNetParityProcessor;
    private bool legacySweepIdle;
    private bool paritySweepIdle;
    private HistoricalBackfillLane nextLane = HistoricalBackfillLane.Parity;
    private Guid? activeCycleId;
    private HistoricalBackfillLane? activeCycleLane;
    private long activeCycleStartedTimestamp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!options.Enabled && !parityOptions.Enabled)
            {
                logger.LogInformation("Historical accounting backfill coordinator is disabled.");
                await TryRecordEventAsync(
                    CreateWorkerEvent(
                        PaperFakFeeBackfillEventTypes.WorkerDisabled,
                        PaperFakFeeBackfillEventLevels.Information,
                        "Historical Paper FAK fee backfill is disabled."),
                    stoppingToken).ConfigureAwait(false);
                return;
            }

            if (options.Enabled || !parityOptions.Enabled)
            {
                logger.LogInformation(
                    "Historical Paper FAK fee backfill lane started. ApplyEnabled={ApplyEnabled} " +
                    "HistoricalCutoffUtc={HistoricalCutoffUtc:O} BatchSize={BatchSize}",
                    options.ApplyEnabled,
                    options.HistoricalCutoffUtc,
                    options.BatchSize);
                await TryRecordEventAsync(
                    CreateWorkerEvent(
                        PaperFakFeeBackfillEventTypes.WorkerStarted,
                        PaperFakFeeBackfillEventLevels.Information,
                        "Historical Paper FAK fee backfill worker started."),
                    stoppingToken).ConfigureAwait(false);

                if (!options.ApplyEnabled)
                {
                    logger.LogWarning(
                        "Historical Paper FAK fee backfill is running in read-only preview mode; " +
                        "no historical rows will be updated by the legacy lane.");
                    await TryRecordEventAsync(
                        CreateWorkerEvent(
                            PaperFakFeeBackfillEventTypes.PreviewMode,
                            PaperFakFeeBackfillEventLevels.Warning,
                            "Historical Paper FAK fee backfill is running in read-only preview mode."),
                        stoppingToken).ConfigureAwait(false);
                }
            }

            if (parityOptions.Enabled)
            {
                var parityErrors = AppOptionsValidator.ValidateHistoricalGrossNetParity(parityOptions);
                if (parityErrors.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Invalid HistoricalGrossNetParity configuration: " +
                        string.Join("; ", parityErrors));
                }

                _ = GetParityProcessor();

                logger.LogInformation(
                    "Historical Gross/Net parity lane started. HistoricalCutoffUtc={HistoricalCutoffUtc:O} " +
                    "BatchSize={BatchSize} LookupMaxAttempts={LookupMaxAttempts} CalculationVersion={CalculationVersion}",
                    parityOptions.HistoricalCutoffUtc,
                    parityOptions.BatchSize,
                    parityOptions.LookupMaxAttempts,
                    parityOptions.CalculationVersion);
            }

            try
            {
                var initialDelaySeconds = GetMinimumEnabledValue(
                    options.InitialDelaySeconds,
                    parityOptions.InitialDelaySeconds);
                if (initialDelaySeconds > 0)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(initialDelaySeconds),
                        stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var legacyErrorDelaySeconds = options.ErrorDelaySeconds;
            var parityErrorDelaySeconds = parityOptions.ErrorDelaySeconds;
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan delay;
                try
                {
                    var disposition = await RunCycleAsync(stoppingToken).ConfigureAwait(false);
                    legacyErrorDelaySeconds = options.ErrorDelaySeconds;
                    parityErrorDelaySeconds = parityOptions.ErrorDelaySeconds;
                    delay = disposition == PaperFakFeeBackfillWorkerCycleDisposition.SweepIdle
                        ? TimeSpan.FromSeconds(GetMinimumEnabledValue(
                            options.IdleDelaySeconds,
                            parityOptions.IdleDelaySeconds))
                        : TimeSpan.FromSeconds(GetMinimumEnabledValue(
                            options.CycleIntervalSeconds,
                            parityOptions.CycleIntervalSeconds));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    ClearActiveCycle();
                    break;
                }
                catch (Exception ex)
                {
                    var failedCycleId = activeCycleId;
                    var failedLane = activeCycleLane;
                    var durationMilliseconds = GetActiveCycleDurationMilliseconds();
                    var errorDelaySeconds = failedLane == HistoricalBackfillLane.Parity
                        ? parityErrorDelaySeconds
                        : legacyErrorDelaySeconds;
                    ClearActiveCycle();
                    delay = TimeSpan.FromSeconds(errorDelaySeconds);
                    logger.LogError(
                        ex,
                        "Historical accounting backfill {Lane} cycle failed. Retrying in {DelaySeconds} seconds.",
                        failedLane,
                        errorDelaySeconds);

                    if (failedLane == HistoricalBackfillLane.Legacy)
                    {
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
                        legacyErrorDelaySeconds = (int)Math.Min(
                            options.MaxErrorDelaySeconds,
                            (long)legacyErrorDelaySeconds * 2L);
                    }
                    else
                    {
                        parityErrorDelaySeconds = (int)Math.Min(
                            parityOptions.MaxErrorDelaySeconds,
                            (long)parityErrorDelaySeconds * 2L);
                    }
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
            logger.LogInformation("Historical accounting backfill coordinator stopped.");
            await TryRecordEventAsync(
                CreateWorkerEvent(
                    PaperFakFeeBackfillEventTypes.WorkerStopped,
                    PaperFakFeeBackfillEventLevels.Information,
                    "Historical accounting backfill coordinator stopped."),
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    internal async Task<PaperFakFeeBackfillWorkerCycleDisposition> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled && !parityOptions.Enabled)
        {
            return PaperFakFeeBackfillWorkerCycleDisposition.SweepIdle;
        }

        var pendingPaperEntryBatches = paperEntryPersistenceQueue.PendingBatches;
        var marketDataQueueMetrics = marketDataSideEffectQueue.GetMetrics();
        var foregroundWorkPending = pendingPaperEntryBatches > 0 ||
            marketDataQueueMetrics.PendingUpdates > 0;
        if (foregroundWorkPending)
        {
            logger.LogDebug(
                "Historical accounting backfill cycle deferred for foreground queues. " +
                "PendingPaperEntryBatches={PendingPaperEntryBatches} PendingMarketDataUpdates={PendingMarketDataUpdates}",
                pendingPaperEntryBatches,
                marketDataQueueMetrics.PendingUpdates);
            if (options.Enabled)
            {
                await TryRecordEventAsync(
                    CreateWorkerEvent(
                        PaperFakFeeBackfillEventTypes.ForegroundDeferred,
                        PaperFakFeeBackfillEventLevels.Information,
                        "Historical Paper FAK fee backfill cycle deferred for foreground queues.") with
                    {
                        PendingPaperEntryBatches = pendingPaperEntryBatches,
                        PendingMarketDataUpdates = marketDataQueueMetrics.PendingUpdates
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            return PaperFakFeeBackfillWorkerCycleDisposition.ForegroundWorkPending;
        }
        var lane = SelectNextLane();
        if (lane is null)
        {
            ResetSweepIdleState();
            return PaperFakFeeBackfillWorkerCycleDisposition.SweepIdle;
        }

        var cycleId = Guid.NewGuid();
        activeCycleId = cycleId;
        activeCycleLane = lane;
        activeCycleStartedTimestamp = Stopwatch.GetTimestamp();

        if (lane == HistoricalBackfillLane.Legacy)
        {
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
            legacySweepIdle = result.ReachedEnd;
        }
        else
        {
            var result = await GetParityProcessor()
                .RunCycleAsync(cycleId, cancellationToken)
                .ConfigureAwait(false);
            paritySweepIdle = result.State is
                HistoricalGrossNetParityCycleState.Idle or
                HistoricalGrossNetParityCycleState.Disabled;
        }

        ClearActiveCycle();
        if (AllEnabledLanesAreIdle())
        {
            ResetSweepIdleState();
            return PaperFakFeeBackfillWorkerCycleDisposition.SweepIdle;
        }

        return PaperFakFeeBackfillWorkerCycleDisposition.Processed;
    }

    private HistoricalBackfillLane? SelectNextLane()
    {
        var legacyReady = options.Enabled && !legacySweepIdle;
        var parityReady = parityOptions.Enabled && !paritySweepIdle;
        if (!legacyReady && !parityReady)
        {
            return null;
        }

        if (legacyReady && parityReady)
        {
            var selected = nextLane;
            nextLane = selected == HistoricalBackfillLane.Legacy
                ? HistoricalBackfillLane.Parity
                : HistoricalBackfillLane.Legacy;
            return selected;
        }

        return legacyReady ? HistoricalBackfillLane.Legacy : HistoricalBackfillLane.Parity;
    }

    private IHistoricalGrossNetParityProcessor GetParityProcessor()
    {
        return parityProcessor ??= serviceProvider is null
            ? throw new InvalidOperationException(
                "HistoricalGrossNetParity is enabled but no processor or service provider is available.")
            : HistoricalGrossNetParityProcessor.Create(serviceProvider, parityOptions);
    }

    private bool AllEnabledLanesAreIdle()
    {
        return (!options.Enabled || legacySweepIdle) &&
            (!parityOptions.Enabled || paritySweepIdle);
    }

    private void ResetSweepIdleState()
    {
        legacySweepIdle = false;
        paritySweepIdle = false;
    }

    private int GetMinimumEnabledValue(int legacyValue, int parityValue)
    {
        if (!options.Enabled)
        {
            return parityValue;
        }

        return !parityOptions.Enabled ? legacyValue : Math.Min(legacyValue, parityValue);
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
        activeCycleLane = null;
        activeCycleStartedTimestamp = 0;
    }
}

internal enum HistoricalBackfillLane
{
    Legacy,
    Parity
}

internal enum PaperFakFeeBackfillWorkerCycleDisposition
{
    Processed,
    ForegroundWorkPending,
    SweepIdle
}
