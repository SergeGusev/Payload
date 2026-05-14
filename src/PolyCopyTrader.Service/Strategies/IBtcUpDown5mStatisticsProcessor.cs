namespace PolyCopyTrader.Service.Strategies;

public interface IBtcUpDown5mStatisticsProcessor
{
    Task<BtcUpDown5mStatisticsCycleResult> ProcessAsync(CancellationToken cancellationToken = default);
}

public sealed record BtcUpDown5mStatisticsCycleResult(
    int MarketsScanned,
    int TicksStored,
    int WouldBet,
    int SkippedNoFreshBtcPrice,
    int SkippedNoOutcomeTokens,
    int SkippedStartPriceMissing,
    int SkippedInsufficientHistory,
    int SkippedMarketPriceMissing,
    int SkippedNoEdge,
    int HistoryObservationsQueued,
    int HistoryObservationsApplied,
    int HistoryObservationsPendingResult);
