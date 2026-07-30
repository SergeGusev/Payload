namespace ReferenceAverageHistoryCorrectionPreview;

public enum StrategyFamily
{
    ReferenceAverage,
    OptimizedReferenceAverage,
    NativeLowEnterReferenceAverage,
    BpsConfirmedAverage,
    DiffConfirmedAverage
}

public enum StrategyLocation
{
    Direct,
    Indirect
}

public enum StrategyTrigger
{
    Up,
    Down,
    Neutral,
    Composite
}

public enum ReplayAction
{
    Retain,
    Remove,
    Unreplayable,
    InvariantError,
    Add,
    StillSkip
}

public enum SignalOutcome
{
    Up,
    Down,
    Skip
}

public sealed record StrategyDefinition(
    Guid Id,
    string Code,
    string Name,
    string Asset,
    StrategyFamily Family,
    StrategyLocation Location,
    StrategyTrigger Trigger,
    int CatalogThresholdBps,
    int ReferenceThresholdBps,
    string Kind,
    bool UsesLowEnterPrice);

public sealed record ReplayDecision(
    ReplayAction Action,
    string Reason,
    decimal? CurrentPriceUsd = null,
    decimal? MinimumAveragePriceUsd = null,
    string? MinimumAverageWindow = null,
    int? MinimumAverageWindowSeconds = null,
    decimal? MaximumAveragePriceUsd = null,
    string? MaximumAverageWindow = null,
    int? MaximumAverageWindowSeconds = null,
    decimal? ThresholdBps = null,
    decimal? MoveBelowMinimumBps = null,
    decimal? MoveAboveMaximumBps = null,
    string? RequiredWindow = null,
    decimal? HistoricalStakeMultiplier = null,
    decimal? AssumedFillPrice = null,
    SignalOutcome? LegacyV1Outcome = null,
    SignalOutcome? CorrectedV2Outcome = null)
{
    public static ReplayDecision Unreplayable(string reason) =>
        new(ReplayAction.Unreplayable, reason);

    public static ReplayDecision InvariantError(string reason) =>
        new(ReplayAction.InvariantError, reason);
}

internal sealed record SourceRow(
    string Scope,
    Guid RunId,
    Guid StrategyId,
    string MarketId,
    DateTimeOffset EntryDueAtUtc,
    DateTimeOffset? SettledAtUtc,
    string? RunOutcome,
    Guid? PaperOrderId,
    Guid? JoinedPaperOrderId,
    Guid? PaperOrderStrategyId,
    string? OrderOutcome,
    bool HasPositiveFill,
    string? RawDecisionJson,
    Guid? EvidenceRunId = null,
    DateTimeOffset? EvidenceEntryDueAtUtc = null,
    string? EvidenceRunStatus = null,
    string? EvidenceRunOutcome = null,
    Guid? EvidenceOrderId = null,
    Guid? EvidenceOrderStrategyId = null,
    string? EvidenceOrderOutcome = null,
    bool EvidenceHasPositiveFill = false,
    string? EvidenceRawDecisionJson = null);

internal sealed record QueryGroupResult(
    string Scope,
    string Asset,
    StrategyFamily Family,
    int StrategyCount,
    long RowCount);

internal sealed record DatabaseSnapshotMetadata(
    string Host,
    int Port,
    string Database,
    string ServerAddress,
    string ServerVersion,
    string TransactionIsolation,
    bool TransactionReadOnly,
    string TimeZone);

internal sealed record OutputFileEvidence(
    string FileName,
    long RowCount,
    string Sha256);
