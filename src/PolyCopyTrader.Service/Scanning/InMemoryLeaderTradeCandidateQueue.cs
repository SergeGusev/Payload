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

    public Task<IReadOnlyList<LeaderTrade>> DrainAsync(int maxItems, CancellationToken cancellationToken = default)
    {
        var result = new List<LeaderTrade>();
        while (result.Count < maxItems && trades.TryDequeue(out var trade))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(trade);
        }

        return Task.FromResult<IReadOnlyList<LeaderTrade>>(result);
    }

    public IReadOnlyList<LeaderTrade> Snapshot()
    {
        return trades.ToArray();
    }
}
