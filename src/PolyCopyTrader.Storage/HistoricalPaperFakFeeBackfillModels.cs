using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed record HistoricalPaperFakFeeBackfillCandidate(
    PaperOrder Order,
    PaperFill Fill);

public sealed record HistoricalPaperFakFeeBackfillCursor(
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
    int Requested,
    int Eligible,
    int FillsUpdated,
    int RunsUpdated,
    int PositionsUpdated,
    int SettlementsUpdated,
    int AlreadyApplied,
    int ConflictsOrDeferred);
