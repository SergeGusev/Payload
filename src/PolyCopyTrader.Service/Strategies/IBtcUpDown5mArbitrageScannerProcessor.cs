namespace PolyCopyTrader.Service.Strategies;

public interface IBtcUpDown5mArbitrageScannerProcessor
{
    Task<BtcUpDown5mArbitrageScannerCycleResult> ProcessAsync(CancellationToken cancellationToken = default);
}

public sealed record BtcUpDown5mArbitrageScannerCycleResult(
    int MarketsScanned,
    int ScansStored,
    int Opportunities,
    int SkippedNoOutcomeTokens,
    int MissingOrderBooks,
    int InsufficientDepth,
    int NoOpportunity);
