namespace PolyCopyTrader.Storage;

public static class HistoricalPaperNetFallbackConstants
{
    public const string CalculationSource = "strategy-settled-fee-stake-ratio-v1";
}

public sealed record HistoricalPaperNetRunCursor(
    Guid StrategyId,
    Guid RunId);

public sealed record HistoricalPaperAuthoritativeNetRepairBatchResult(
    int Candidates = 0,
    int RunsUpdated = 0,
    int CompareAndSetConflicts = 0,
    bool ReachedEnd = true,
    HistoricalPaperNetRunCursor? ContinuationCursor = null,
    int DeferredByLockTimeout = 0,
    int DeferredByQueryCancel = 0)
{
    public int Deferred => DeferredByLockTimeout + DeferredByQueryCancel;

    public bool WholeBatchDeferred => Deferred > 0;
}

public sealed record HistoricalPaperNetFallbackBatchResult(
    int Candidates = 0,
    int ExactDonorCount = 0,
    decimal ExactDonorFeeUsd = 0m,
    decimal ExactDonorStakeUsd = 0m,
    decimal? FeeToStakeRatio = null,
    int RunsUpdated = 0,
    int CompareAndSetConflicts = 0,
    bool DonorAvailable = false,
    bool ReachedEnd = true,
    HistoricalPaperNetRunCursor? ContinuationCursor = null,
    int DeferredByLockTimeout = 0,
    int DeferredByQueryCancel = 0)
{
    public int Deferred => DeferredByLockTimeout + DeferredByQueryCancel;

    public bool WholeBatchDeferred => Deferred > 0;
}
