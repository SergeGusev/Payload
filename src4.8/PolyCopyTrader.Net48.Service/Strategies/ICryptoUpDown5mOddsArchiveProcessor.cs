namespace PolyCopyTrader.Service.Strategies;

public interface ICryptoUpDown5mOddsArchiveProcessor
{
    Task<CryptoUpDown5mOddsArchiveCycleResult> ProcessAsync(CancellationToken cancellationToken = default);
}

public sealed record CryptoUpDown5mOddsArchiveCycleResult(
    int MarketsScanned,
    int TicksStored,
    int SkippedNoFreshPrice,
    int SkippedNoOutcomeTokens,
    int MissingBothBooks);
