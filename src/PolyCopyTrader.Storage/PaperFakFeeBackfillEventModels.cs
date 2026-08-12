namespace PolyCopyTrader.Storage;

public sealed record PaperFakFeeBackfillEvent
{
    public Guid Id { get; init; }

    public Guid WorkerInstanceId { get; init; }

    public Guid? SweepId { get; init; }

    public Guid? CycleId { get; init; }

    public long Sequence { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public string Level { get; init; } = string.Empty;

    public string EventType { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string BuildVersion { get; init; } = string.Empty;

    public string HostName { get; init; } = string.Empty;

    public int ProcessId { get; init; }

    public bool? BackfillEnabled { get; init; }

    public bool? ApplyEnabled { get; init; }

    public DateTimeOffset? CutoffUtc { get; init; }

    public int? BatchSize { get; init; }

    public int? PendingPaperEntryBatches { get; init; }

    public int? PendingMarketDataUpdates { get; init; }

    public int? DelaySeconds { get; init; }

    public Guid? StrategyId { get; init; }

    public string? StrategyCode { get; init; }

    public int? StrategyRank { get; init; }

    public int? StrategyCount { get; init; }

    public decimal? GrossRealizedPnlUsd { get; init; }

    public int? Candidates { get; init; }

    public int? EvaluatedForApply { get; init; }

    public int? TransientLookupUnavailable { get; init; }

    public int? Requested { get; init; }

    public int? Eligible { get; init; }

    public int? FullChainEligible { get; init; }

    public int? RunOnlyLegacyEligible { get; init; }

    public int? FillsUpdated { get; init; }

    public int? RunsUpdated { get; init; }

    public int? PositionsUpdated { get; init; }

    public int? SettlementsUpdated { get; init; }

    public int? FullChainAlreadyApplied { get; init; }

    public int? RunOnlyLegacyAlreadyApplied { get; init; }

    public int? AlreadyApplied { get; init; }

    public int? StructuralConflicts { get; init; }

    public int? AccountingConflicts { get; init; }

    public int? DeferredByLockTimeout { get; init; }

    public int? DeferredByQueryCancel { get; init; }

    public bool? ReachedStrategyEnd { get; init; }

    public bool? ReachedSweepEnd { get; init; }

    public long? DurationMilliseconds { get; init; }

    public string? ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }
}

public static class PaperFakFeeBackfillEventTypes
{
    public const string WorkerStarted = nameof(WorkerStarted);

    public const string WorkerDisabled = nameof(WorkerDisabled);

    public const string PreviewMode = nameof(PreviewMode);

    public const string ForegroundDeferred = nameof(ForegroundDeferred);

    public const string StrategyRankingFrozen = nameof(StrategyRankingFrozen);

    public const string CycleStarted = nameof(CycleStarted);

    public const string CycleContext = nameof(CycleContext);

    public const string CycleCompleted = nameof(CycleCompleted);

    public const string CycleFailed = nameof(CycleFailed);

    public const string WorkerStopped = nameof(WorkerStopped);
}

public static class PaperFakFeeBackfillEventLevels
{
    public const string Information = nameof(Information);

    public const string Warning = nameof(Warning);

    public const string Error = nameof(Error);
}
