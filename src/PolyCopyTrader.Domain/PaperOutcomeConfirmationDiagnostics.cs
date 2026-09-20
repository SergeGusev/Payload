namespace PolyCopyTrader.Domain;

public enum PaperConfirmationStage
{
    IdleCheck, Claim, GammaToken, GammaCondition, ValidateOutcome, ApplyDatabase,
    DatabaseConnection, DatabaseTransaction, DatabaseTimeouts, HourlyLock, OrderLock,
    IdentityCheck, ReadinessCheck, ReadBefore, CorrectSettlement, CorrectRuns,
    LossDiffLock, CorrectEventsAndCounter, ReconcileLossDiff, RefreshHourly,
    RefreshWallet, ReadAfter, MarkConfirmed, Commit, Defer
}

public sealed record PaperConfirmationStageTiming(PaperConfirmationStage Stage, double DurationMs);
public sealed record PaperConfirmationTraceSnapshot(Guid AttemptId, Guid? PaperOrderId,
    DateTimeOffset StartedAtUtc, PaperConfirmationStage Stage, double StageAgeMs, double DurationMs,
    string Outcome, string Reason, string? ErrorType, string? SqlState, PaperConfirmationStage? ErrorStage,
    bool CommitStarted, bool CommitAcknowledged, IReadOnlyList<PaperConfirmationStageTiming> Stages);

/// <summary>One bounded attempt, without payloads or exception messages. No I/O in stage tracking.</summary>
public sealed class PaperOutcomeConfirmationTrace(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly object sync = new();
    private readonly Guid attemptId = Guid.NewGuid();
    private readonly List<PaperConfirmationStageTiming> stages = [];
    private readonly DateTimeOffset startedUtc = (timeProvider ?? TimeProvider.System).GetUtcNow();
    private readonly long started = (timeProvider ?? TimeProvider.System).GetTimestamp();
    private long stageStarted = (timeProvider ?? TimeProvider.System).GetTimestamp();
    private PaperConfirmationStage stage = PaperConfirmationStage.IdleCheck;
    private Guid? orderId;
    private string outcome = "Unknown", reason = "Unknown";
    private string? errorType, sqlState;
    private PaperConfirmationStage? errorStage;
    private bool commitStarted, commitAcknowledged;

    public void SetOrder(Guid id) { lock (sync) orderId = id; }
    public void Enter(PaperConfirmationStage next)
    {
        lock (sync)
        {
            var now = clock.GetTimestamp();
            stages.Add(new(stage, clock.GetElapsedTime(stageStarted, now).TotalMilliseconds));
            stage = next;
            stageStarted = now;
            if (next == PaperConfirmationStage.Commit) commitStarted = true;
        }
    }
    public void AcknowledgeCommit() { lock (sync) commitAcknowledged = true; }
    public void Complete(string result, string detail) { lock (sync) { outcome = result; reason = detail; } }
    public void Error(string type, string? state = null)
    {
        lock (sync)
        {
            if (errorType is not null) return;
            errorType = type; sqlState = state; errorStage = stage;
        }
    }
    public PaperConfirmationTraceSnapshot Snapshot()
    {
        lock (sync)
        {
            var now = clock.GetTimestamp();
            return new(attemptId, orderId, startedUtc, stage,
                clock.GetElapsedTime(stageStarted, now).TotalMilliseconds,
                clock.GetElapsedTime(started, now).TotalMilliseconds, outcome, reason,
                errorType, sqlState, errorStage, commitStarted, commitAcknowledged, stages.ToArray());
        }
    }
}
