namespace PolyCopyTrader.Service.Strategies;

public interface ICryptoUpDown5mResultPollingProcessor
{
    Task<CryptoUpDown5mResultPollingCycleResult> ProcessAsync(CancellationToken cancellationToken = default);
}

public sealed record CryptoUpDown5mResultPollingCycleResult(
    int MarketsScanned,
    int Candidates,
    int PollsSent,
    int ResultsFound,
    int TimedOut,
    int Errors);
