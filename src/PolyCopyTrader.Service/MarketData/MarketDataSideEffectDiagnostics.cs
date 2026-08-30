using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public static class MarketDataSideEffectDiagnosticSchema
{
    public const string Version = "maker_gtd_preflight_diagnostic_v1";
    public const string Available = "Available";
    public const string NotAvailable = "NotAvailable";
}

public static class MarketDataSideEffectPhases
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string RecordResolvedEvent = "RecordResolvedEvent";
    public const string RecordTradeTick = "RecordTradeTick";
    public const string PersistOrderBookSnapshot = "PersistOrderBookSnapshot";
    public const string PersistMarketDataEvent = "PersistMarketDataEvent";
    public const string ApplyPaperTradingUpdate = "ApplyPaperTradingUpdate";
    public const string WaitForPublication = "ApplyPaperTradingUpdate/WaitForPublication";
    public const string WaitForSerializationLock = "ApplyPaperTradingUpdate/WaitForSerializationLock";
    public const string LoadExposureSnapshot = "ApplyPaperTradingUpdate/LoadExposureSnapshot";
    public const string SettleMarketResolution = "ApplyPaperTradingUpdate/SettleMarketResolution";
    public const string ApplyMakerGtdPaperUpdate = "ApplyPaperTradingUpdate/ApplyMakerGtdPaperUpdate";
    public const string ApplyOrdinaryPaperUpdate = "ApplyPaperTradingUpdate/ApplyOrdinaryPaperUpdate";
    public const string UpdatePositionMarks = "ApplyPaperTradingUpdate/UpdatePositionMarks";
}

public sealed record MarketDataSideEffectExecutionTraceSnapshot(
    string Component,
    MarketDataEventType EventType,
    string? AssetId,
    string? ConditionId,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset EnqueuedAtUtc,
    DateTimeOffset? ProcessingStartedAtUtc,
    string Phase,
    DateTimeOffset PhaseEnteredAtUtc,
    DateTimeOffset CapturedAtUtc,
    double ReceivedAgeMilliseconds,
    double QueueAgeMilliseconds,
    double? ProcessingAgeMilliseconds,
    double PhaseAgeMilliseconds,
    string? Operation = null,
    string? SlowestPhase = null,
    string? SlowestOperation = null,
    double SlowestPhaseDurationMilliseconds = 0d);

public sealed class MarketDataSideEffectExecutionTrace
{
    private readonly object sync = new();
    private readonly string component;
    private readonly MarketDataEventType eventType;
    private readonly string? assetId;
    private readonly string? conditionId;
    private readonly DateTimeOffset receivedAtUtc;
    private readonly DateTimeOffset enqueuedAtUtc;
    private DateTimeOffset? processingStartedAtUtc;
    private string phase = MarketDataSideEffectPhases.Queued;
    private string? operation;
    private DateTimeOffset phaseEnteredAtUtc;
    private DateTimeOffset? processingCompletedAtUtc;
    private string? slowestPhase;
    private string? slowestOperation;
    private double slowestPhaseDurationMilliseconds;

    public MarketDataSideEffectExecutionTrace(
        string component,
        MarketDataEventType eventType,
        string? assetId,
        string? conditionId,
        DateTimeOffset receivedAtUtc,
        DateTimeOffset enqueuedAtUtc)
    {
        this.component = component;
        this.eventType = eventType;
        this.assetId = assetId;
        this.conditionId = conditionId;
        this.receivedAtUtc = receivedAtUtc;
        this.enqueuedAtUtc = enqueuedAtUtc;
        phaseEnteredAtUtc = enqueuedAtUtc;
    }

    public void MarkProcessingStarted(DateTimeOffset startedAtUtc)
    {
        lock (sync)
        {
            processingStartedAtUtc ??= startedAtUtc;
            phase = MarketDataSideEffectPhases.Processing;
            operation = null;
            phaseEnteredAtUtc = startedAtUtc;
        }
    }

    public void EnterPhase(string nextPhase, DateTimeOffset enteredAtUtc)
    {
        EnterPhase(nextPhase, nextOperation: null, enteredAtUtc);
    }

    public void EnterPhase(string nextPhase, string? nextOperation, DateTimeOffset enteredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nextPhase);
        lock (sync)
        {
            CompleteCurrentPhase(enteredAtUtc);
            phase = nextPhase;
            operation = string.IsNullOrWhiteSpace(nextOperation) ? null : nextOperation;
            phaseEnteredAtUtc = enteredAtUtc;
        }
    }

    public void MarkProcessingCompleted(DateTimeOffset completedAtUtc)
    {
        lock (sync)
        {
            if (processingCompletedAtUtc is not null)
            {
                return;
            }

            processingCompletedAtUtc = completedAtUtc;
            CompleteCurrentPhase(completedAtUtc);
        }
    }

    public MarketDataSideEffectExecutionTraceSnapshot Capture(DateTimeOffset capturedAtUtc)
    {
        lock (sync)
        {
            var effectiveCapturedAtUtc = processingCompletedAtUtc is { } completedAtUtc && completedAtUtc < capturedAtUtc
                ? completedAtUtc
                : capturedAtUtc;
            var queueEndUtc = processingStartedAtUtc ?? effectiveCapturedAtUtc;
            var currentPhaseDurationMilliseconds = NonNegativeMilliseconds(effectiveCapturedAtUtc - phaseEnteredAtUtc);
            var capturedSlowestPhase = slowestPhase;
            var capturedSlowestOperation = slowestOperation;
            var capturedSlowestDurationMilliseconds = slowestPhaseDurationMilliseconds;
            if (processingStartedAtUtc is not null &&
                currentPhaseDurationMilliseconds > capturedSlowestDurationMilliseconds)
            {
                capturedSlowestPhase = phase;
                capturedSlowestOperation = operation;
                capturedSlowestDurationMilliseconds = currentPhaseDurationMilliseconds;
            }

            return new MarketDataSideEffectExecutionTraceSnapshot(
                component,
                eventType,
                assetId,
                conditionId,
                receivedAtUtc,
                enqueuedAtUtc,
                processingStartedAtUtc,
                phase,
                phaseEnteredAtUtc,
                capturedAtUtc,
                NonNegativeMilliseconds(capturedAtUtc - receivedAtUtc),
                NonNegativeMilliseconds(queueEndUtc - enqueuedAtUtc),
                processingStartedAtUtc is { } processingStarted
                    ? NonNegativeMilliseconds(effectiveCapturedAtUtc - processingStarted)
                    : null,
                currentPhaseDurationMilliseconds,
                operation,
                capturedSlowestPhase,
                capturedSlowestOperation,
                capturedSlowestDurationMilliseconds);
        }
    }

    private void CompleteCurrentPhase(DateTimeOffset completedAtUtc)
    {
        if (processingStartedAtUtc is null || phase == MarketDataSideEffectPhases.Queued)
        {
            return;
        }

        var durationMilliseconds = NonNegativeMilliseconds(completedAtUtc - phaseEnteredAtUtc);
        if (durationMilliseconds <= slowestPhaseDurationMilliseconds)
        {
            return;
        }

        slowestPhase = phase;
        slowestOperation = operation;
        slowestPhaseDurationMilliseconds = durationMilliseconds;
    }

    private static double NonNegativeMilliseconds(TimeSpan duration)
    {
        return Math.Max(0d, duration.TotalMilliseconds);
    }
}

public sealed record MarketDataSideEffectPreflightSnapshot(
    string Availability,
    string? UnavailableReason,
    DateTimeOffset CapturedAtUtc,
    int MatchingOutstandingCount,
    int MatchingInFlightCount,
    int MatchingPendingCount,
    DateTimeOffset? OldestMatchingReceivedAtUtc,
    double? OldestMatchingReceivedAgeMilliseconds,
    DateTimeOffset? OldestMatchingEnqueuedAtUtc,
    double? OldestMatchingEnqueuedAgeMilliseconds,
    int TotalPendingUpdates,
    int TrackedAssets,
    string UpdateWorkerState,
    MarketDataSideEffectExecutionTraceSnapshot? InFlightUpdate)
{
    public static MarketDataSideEffectPreflightSnapshot NotAvailable(
        DateTimeOffset capturedAtUtc,
        string reason)
    {
        return new MarketDataSideEffectPreflightSnapshot(
            MarketDataSideEffectDiagnosticSchema.NotAvailable,
            reason,
            capturedAtUtc,
            0,
            0,
            0,
            null,
            null,
            null,
            null,
            0,
            0,
            MarketDataSideEffectDiagnosticSchema.NotAvailable,
            null);
    }

    public static double? AgeMilliseconds(DateTimeOffset capturedAtUtc, DateTimeOffset? startedAtUtc)
    {
        return startedAtUtc is { } started
            ? Math.Max(0d, (capturedAtUtc - started).TotalMilliseconds)
            : null;
    }
}
