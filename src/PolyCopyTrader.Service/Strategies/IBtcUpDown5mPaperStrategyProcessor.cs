namespace PolyCopyTrader.Service.Strategies;

public interface IBtcUpDown5mPaperStrategyProcessor
{
    Task<BtcUpDown5mPaperStrategyResult> ProcessAsync(CancellationToken cancellationToken = default);
}

public sealed record BtcUpDown5mPaperStrategyResult(
    int MarketsObserved,
    int EntriesPlaced,
    int RunsSkipped,
    int RunsSettled);
