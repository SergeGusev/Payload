namespace PolyCopyTrader.Service.Strategies;

public interface ICryptoUpDown5mResultPollingProcessor
{
    Task<CryptoUpDown5mResultPollingCycleResult> ProcessAsync(CancellationToken cancellationToken = default);

    Task<CryptoUpDown5mBinanceTimedCloseCycleResult> ProcessBinanceTimedCloseAsync(CancellationToken cancellationToken = default);
}

public sealed record CryptoUpDown5mResultPollingCycleResult(
    int MarketsScanned,
    int Candidates,
    int PollsSent,
    int ResultsFound,
    int TimedOut,
    int Errors);

public sealed record CryptoUpDown5mBinanceTimedCloseCycleResult(
    int MarketsScanned,
    int Candidates,
    int AlreadyResolved,
    int Resolved,
    int SkippedUncertain,
    int MissingStartPrice,
    int MissingClosePrice,
    int Errors);
