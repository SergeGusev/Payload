using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public static class DashboardProjectionVersions
{
    public const int Current = 2;
}

public static class DashboardProjectionSourceKinds
{
    public const string Strategy = "Strategy";
    public const string PaperOrder = "PaperOrder";
    public const string PaperFill = "PaperFill";
    public const string StrategyRun = "StrategyRun";
    public const string PaperPosition = "PaperPosition";
    public const string PaperSettlement = "PaperSettlement";
    public const string LiveOrder = "LiveOrder";
}

public static class DashboardProjectionOperations
{
    public const string Insert = "Insert";
    public const string Update = "Update";
    public const string Delete = "Delete";
}

public sealed record DashboardProjectionControlState(
    bool Initialized,
    int CalculationVersion,
    string Status,
    Guid? ReconciliationCursorStrategyId,
    DateTimeOffset? BootstrapStartedAtUtc,
    DateTimeOffset? BootstrapCompletedAtUtc,
    DateTimeOffset? LastEventAppliedAtUtc,
    DateTimeOffset? LastExpiryAtUtc,
    DateTimeOffset? LastReconciliationAtUtc,
    string? LastError);

public sealed record DashboardProjectionEvent(
    long Id,
    string SourceKind,
    Guid SourceId,
    Guid? StrategyId,
    string Operation,
    string? OldPayloadJson,
    string? NewPayloadJson,
    bool IncludedInBootstrapSnapshot,
    DateTimeOffset CreatedAtUtc);

public sealed record DashboardProjectionBatchResult(
    int EventsRead,
    int EventsApplied,
    int EventsDiscardedAsBootstrapped,
    int StrategiesUpdated,
    int ReconciliationsQueued);

public sealed record DashboardProjectionExpiryResult(
    int FactsExpired,
    int StrategiesUpdated);

public sealed record DashboardProjectionReconciliationResult(
    bool Reconciled,
    Guid? StrategyId,
    string? StrategyCode,
    TimeSpan Duration,
    bool ValuesChanged,
    string? Error);

public sealed record DashboardProjectionBootstrapResult(
    int Strategies,
    int RecentFacts,
    int RecentRows,
    int BootstrappedEventsDiscarded,
    TimeSpan Duration);

public sealed record DashboardStrategyDescriptor(
    Guid StrategyId,
    string Code,
    string Name,
    bool Enabled,
    bool LiveStakes,
    bool Paused,
    DateTimeOffset? PausedUntilUtc,
    decimal PaperStakeAmount,
    decimal LiveStakeAmount,
    decimal PaperLostCoeff,
    decimal LiveLostCoeff,
    int PaperLostCounter,
    int LiveLostCounter,
    decimal LiveAvailableBalance,
    DateTimeOffset? LiveEnabledAtUtc);

public sealed record StrategyProjectionPayload(
    Guid Id,
    bool LiveStakes,
    DateTimeOffset? LiveEnabledAtUtc);

public sealed record PaperOrderProjectionPayload(
    Guid Id,
    Guid StrategyId,
    string Status,
    string Side,
    decimal NotionalUsd,
    DateTimeOffset CreatedAtUtc,
    decimal? PreviousScoreBps,
    decimal? PreviousScore,
    decimal? SelectedSignalBps);

public sealed record PaperFillProjectionPayload(
    Guid Id,
    Guid StrategyId,
    string OrderSide,
    decimal Price,
    decimal SizeShares,
    decimal RealizedPnlUsd,
    DateTimeOffset FilledAtUtc);

public sealed record StrategyRunProjectionPayload(
    Guid Id,
    Guid StrategyId,
    string Status,
    decimal StakeUsd,
    Guid? PaperOrderId,
    DateTimeOffset EntryDueAtUtc,
    DateTimeOffset? EnteredAtUtc,
    decimal? RealizedPnlUsd,
    DateTimeOffset? SettledAtUtc,
    string? SkipReason,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LiveEnabledAtUtc);

public sealed record PaperPositionProjectionPayload(
    Guid Id,
    Guid StrategyId,
    decimal SizeShares,
    decimal UnrealizedPnlUsd);

public sealed record PaperSettlementProjectionPayload(
    Guid Id,
    Guid StrategyId,
    decimal CostBasisUsd,
    decimal RealizedPnlUsd,
    bool Won);

public sealed record LiveOrderProjectionPayload(
    Guid Id,
    Guid StrategyId,
    string Status,
    decimal Price,
    decimal FilledSize,
    decimal RemainingSize,
    decimal FilledNotionalUsd,
    decimal CostBasisUsd,
    decimal FeeUsd,
    decimal? SettlementValueUsd,
    decimal? RealizedPnlUsd,
    DateTimeOffset? SettledAtUtc,
    bool? Won,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public class DashboardLifetimeContribution
{
    public int OrdersCount { get; set; }
    public int FilledOrdersCount { get; set; }
    public int OpenOrdersCount { get; set; }
    public decimal BuyNotionalUsd { get; set; }
    public decimal CountertrendScoreSumBps { get; set; }
    public int CountertrendScoreCount { get; set; }
    public decimal CountertrendSignalSumBps { get; set; }
    public int CountertrendSignalCount { get; set; }
    public decimal? LastCountertrendSignalBps { get; set; }
    public DateTimeOffset? LastCountertrendSignalAtUtc { get; set; }
    public decimal FillRealizedPnlUsd { get; set; }
    public decimal FillClosedCostBasisUsd { get; set; }
    public int OpenPositionsCount { get; set; }
    public decimal UnrealizedPnlUsd { get; set; }
    public int SettlementCount { get; set; }
    public int SettlementWonCount { get; set; }
    public int SettlementLostCount { get; set; }
    public decimal SettlementCostBasisUsd { get; set; }
    public decimal SettlementRealizedPnlUsd { get; set; }
    public decimal SettlementWinPnlSumUsd { get; set; }
    public int SettlementWinCount { get; set; }
    public decimal SettlementLossPnlSumUsd { get; set; }
    public int SettlementLossCount { get; set; }
    public decimal SettlementPositivePnlUsd { get; set; }
    public decimal SettlementLossAbsPnlUsd { get; set; }
    public int RunsCount { get; set; }
    public int ObservedRunsCount { get; set; }
    public int EnteredRunsCount { get; set; }
    public int SkippedRunsCount { get; set; }
    public int PaperConditionSkippedRunsCount { get; set; }
    public int PaperNotAcceptedRunsCount { get; set; }
    public int SettledRunsCount { get; set; }
    public int RunWonCount { get; set; }
    public int RunLostCount { get; set; }
    public decimal RunSettledStakeUsd { get; set; }
    public decimal RunRealizedPnlUsd { get; set; }
    public decimal RunWinPnlSumUsd { get; set; }
    public int RunWinCount { get; set; }
    public decimal RunLossPnlSumUsd { get; set; }
    public int RunLossCount { get; set; }
    public decimal RunPositivePnlUsd { get; set; }
    public decimal RunLossAbsPnlUsd { get; set; }
    public decimal EntryDelayTotalSeconds { get; set; }
    public int EntryDelayCount { get; set; }
    public decimal MaxEntryDelaySeconds { get; set; }
    public int RunLiveConditionSkippedCount { get; set; }
    public int RunLiveTechnicalSkippedCount { get; set; }
    public int RunLiveIgnoredGtdCount { get; set; }
    public int LiveOrdersCount { get; set; }
    public int LiveFilledOrdersCount { get; set; }
    public int LiveOpenOrdersCount { get; set; }
    public int LiveSettledOrdersCount { get; set; }
    public int LiveTechnicalSkippedCount { get; set; }
    public int LiveIgnoredCancelledCount { get; set; }
    public int LiveIgnoredRejectedCount { get; set; }
    public int LiveWonCount { get; set; }
    public int LiveLostCount { get; set; }
    public decimal LiveStakeUsd { get; set; }
    public decimal LiveRealizedPnlUsd { get; set; }
    public decimal LiveWinPnlSumUsd { get; set; }
    public int LiveWinCount { get; set; }
    public decimal LiveLossPnlSumUsd { get; set; }
    public int LiveLossCount { get; set; }
    public decimal LivePositivePnlUsd { get; set; }
    public decimal LiveLossAbsPnlUsd { get; set; }
    public DateTimeOffset? LiveLastOrderUtc { get; set; }
    public DateTimeOffset? LiveLastSettlementUtc { get; set; }
    public DateTimeOffset? LastOrderUtc { get; set; }
    public DateTimeOffset? LastRunUtc { get; set; }
}

public sealed class DashboardLifetimeProjectionState : DashboardLifetimeContribution
{
    public long ProjectionVersion { get; set; }
    public long? LastEventId { get; set; }
    public DateTimeOffset? LastReconciledAtUtc { get; set; }
}

public class DashboardRecentContribution
{
    public int OrdersCount { get; set; }
    public int FilledOrdersCount { get; set; }
    public int ExpiredOrdersCount { get; set; }
    public int OpenOrdersCount { get; set; }
    public decimal FilledCostUsd { get; set; }
    public decimal FilledSizeShares { get; set; }
    public int EnteredRunsCount { get; set; }
    public int SkippedRunsCount { get; set; }
    public int PaperConditionSkippedRunsCount { get; set; }
    public int PaperNotAcceptedRunsCount { get; set; }
    public int RunLiveConditionSkippedCount { get; set; }
    public int RunLiveTechnicalSkippedCount { get; set; }
    public int RunLiveIgnoredGtdCount { get; set; }
    public int SettledRunsCount { get; set; }
    public int WonRunsCount { get; set; }
    public int LostRunsCount { get; set; }
    public decimal SettledStakeUsd { get; set; }
    public decimal RealizedPnlUsd { get; set; }
    public decimal EntryDelayTotalSeconds { get; set; }
    public int EntryDelayCount { get; set; }
    public decimal? EntryDelayCandidateSeconds { get; set; }
    public int LiveSettledOrdersCount { get; set; }
    public int LiveTechnicalSkippedCount { get; set; }
    public int LiveIgnoredCancelledCount { get; set; }
    public int LiveIgnoredRejectedCount { get; set; }
    public int LiveWonCount { get; set; }
    public int LiveLostCount { get; set; }
    public decimal LiveStakeUsd { get; set; }
    public decimal LiveRealizedPnlUsd { get; set; }
    public string? SkipReason { get; set; }
    public DateTimeOffset? LastOrderUtc { get; set; }
    public DateTimeOffset? LastRunUtc { get; set; }
}

public sealed class DashboardRecentProjectionState : DashboardRecentContribution
{
    public Dictionary<string, int> SkipReasonCounts { get; set; } = new(StringComparer.Ordinal);
    public decimal MaxEntryDelaySeconds { get; set; }
    public long ProjectionVersion { get; set; }
    public long? LastEventId { get; set; }
    public DateTimeOffset? LastReconciledAtUtc { get; set; }
}

public sealed record DashboardRecentProjectionFact(
    string SourceKind,
    Guid SourceId,
    string FactKind,
    Guid StrategyId,
    DateTimeOffset OccurredAtUtc,
    DashboardRecentContribution Contribution,
    bool Applied1Hour,
    bool Applied6Hours,
    bool Applied24Hours);

public sealed record DashboardRecentProjectionFactKey(
    string SourceKind,
    Guid SourceId,
    string FactKind);

public sealed record DashboardProjectionStateEnvelope<TState>(
    Guid StrategyId,
    TState State,
    long ProjectionVersion,
    long? LastEventId,
    DateTimeOffset? LastReconciledAtUtc)
    where TState : class;
