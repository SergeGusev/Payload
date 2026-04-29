using System.Collections.Concurrent;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.Scanning;

public sealed class InMemoryLeaderTradeCandidateQueue : ILeaderTradeCandidateQueue
{
    private readonly ConcurrentQueue<LeaderTrade> trades = new();

    public Task EnqueueAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
    {
        trades.Enqueue(trade);
        return Task.CompletedTask;
    }

    public IReadOnlyList<LeaderTrade> Snapshot()
    {
        return trades.ToArray();
    }
}
