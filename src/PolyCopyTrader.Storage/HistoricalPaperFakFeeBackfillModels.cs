using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed record HistoricalPaperFakFeeBackfillCandidate(
    PaperOrder Order,
    PaperFill Fill);

public sealed record HistoricalPaperFakFeeBackfillStrategyRank(
    Guid StrategyId,
    string StrategyCode,
    decimal GrossRealizedPnlUsd);

public sealed record HistoricalPaperFakFeeBackfillCursor(
    Guid StrategyId,
    DateTimeOffset FilledAtUtc,
    Guid PaperOrderId,
    Guid FillId);

public sealed record HistoricalPaperFakFeeBackfillPage(
    IReadOnlyList<HistoricalPaperFakFeeBackfillCandidate> Candidates,
    HistoricalPaperFakFeeBackfillCursor? ContinuationCursor,
    bool ReachedEnd);

public sealed record HistoricalPaperFakFeeBackfillUpdate(
    HistoricalPaperFakFeeBackfillCandidate Expected,
    PaperFill EvaluatedFill);

public sealed record HistoricalPaperFakFeeBackfillBatchResult(
    int Requested = 0,
    int StructuralConflicts = 0,
    int AccountingConflicts = 0,
    int FullChainEligible = 0,
    int RunOnlyLegacyEligible = 0,
    int FillsUpdated = 0,
    int RunsUpdated = 0,
    int PositionsUpdated = 0,
    int SettlementsUpdated = 0,
    int FullChainAlreadyApplied = 0,
    int RunOnlyLegacyAlreadyApplied = 0,
    int DeferredByLockTimeout = 0,
    int DeferredByQueryCancel = 0)
{
    public int Eligible => FullChainEligible + RunOnlyLegacyEligible;

    public int AlreadyApplied => FullChainAlreadyApplied + RunOnlyLegacyAlreadyApplied;

    public int ItemConflicts => StructuralConflicts + AccountingConflicts;

    public int Deferred => DeferredByLockTimeout + DeferredByQueryCancel;

    public bool WholeBatchDeferred => Deferred > 0;
}
