using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.Scanning;

public interface ILeaderTradeCandidateQueue
{
    Task EnqueueAsync(LeaderTrade trade, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaderTrade>> DrainAsync(int maxItems, CancellationToken cancellationToken = default);
}
