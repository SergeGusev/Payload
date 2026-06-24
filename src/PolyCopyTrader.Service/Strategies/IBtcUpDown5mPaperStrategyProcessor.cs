namespace PolyCopyTrader.Service.Strategies;

public interface IBtcUpDown5mPaperStrategyProcessor
{
    Task<BtcUpDown5mPaperStrategyResult> ProcessAsync(CancellationToken cancellationToken = default);

    Task<BtcUpDown5mPaperStrategyResult> ProcessDiffCounterDueEntriesAsync(CancellationToken cancellationToken = default);

    Task<BtcUpDown5mPaperStrategyResult> ProcessPreviousResultDueEntriesAsync(CancellationToken cancellationToken = default);
}

public sealed record BtcUpDown5mPaperStrategyResult(
    int MarketsObserved,
    int EntriesPlaced,
    int RunsSkipped,
    int RunsSettled);
