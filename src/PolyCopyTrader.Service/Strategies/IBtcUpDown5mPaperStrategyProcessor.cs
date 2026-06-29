namespace PolyCopyTrader.Service.Strategies;

public interface IBtcUpDown5mPaperStrategyProcessor
{
    Task<BtcUpDown5mPaperStrategyResult> ProcessAsync(CancellationToken cancellationToken = default);

    Task<BtcUpDown5mPaperStrategyResult> ProcessDueEntriesAsync(CancellationToken cancellationToken = default);

    Task<BtcUpDown5mPaperStrategyResult> ProcessDiffCounterFastDueEntriesAsync(CancellationToken cancellationToken = default);

    Task<BtcUpDown5mPaperStrategyResult> ProcessDiffCounterObserveAsync(CancellationToken cancellationToken = default);

    Task<BtcUpDown5mPaperStrategyResult> ProcessDiffCounterDueEntriesAsync(CancellationToken cancellationToken = default);

    Task<BtcUpDown5mPaperStrategyResult> ProcessPreviousResultFastDueEntriesAsync(CancellationToken cancellationToken = default);

    Task<BtcUpDown5mPaperStrategyResult> ProcessPreviousResultObserveAsync(CancellationToken cancellationToken = default);

    Task<BtcUpDown5mPaperStrategyResult> ProcessPreviousResultDueEntriesAsync(CancellationToken cancellationToken = default);
}

public sealed record BtcUpDown5mPaperStrategyResult(
    int MarketsObserved,
    int EntriesPlaced,
    int RunsSkipped,
    int RunsSettled);
