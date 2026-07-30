namespace PolyCopyTrader.Storage;

public static class StrategyRunRetentionScopes
{
    public const string Unknown = "Unknown";
    public const string PaperOnly = "PaperOnly";
    public const string LiveOrShadow = "LiveOrShadow";
}

public sealed record StrategyRunRetentionPreview(
    IReadOnlyList<Guid> CandidateRunIds,
    int DistinctStrategies,
    DateTimeOffset? OldestUpdatedAtUtc,
    DateTimeOffset? NewestUpdatedAtUtc);

public sealed record StrategyRunRetentionSummary(
    long TotalCandidateRows,
    long DistinctStrategies,
    DateTimeOffset? OldestUpdatedAtUtc,
    DateTimeOffset? NewestUpdatedAtUtc,
    IReadOnlyList<Guid> SampleRunIds);

public sealed record StrategyRunRetentionBatchResult(
    int SelectedRows,
    int DeletedRows,
    int RollupRowsChanged,
    int TombstonesChanged,
    int StrategiesQueuedForReconciliation);
