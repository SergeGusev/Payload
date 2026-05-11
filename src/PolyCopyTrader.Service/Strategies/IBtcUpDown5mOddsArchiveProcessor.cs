namespace PolyCopyTrader.Service.Strategies;

public interface IBtcUpDown5mOddsArchiveProcessor
{
    Task<BtcUpDown5mOddsArchiveCycleResult> ProcessAsync(CancellationToken cancellationToken = default);
}

public sealed record BtcUpDown5mOddsArchiveCycleResult(
    int MarketsScanned,
    int TicksStored,
    int SkippedNoFreshBtcPrice,
    int SkippedNoOutcomeTokens,
    int MissingBothBooks);
