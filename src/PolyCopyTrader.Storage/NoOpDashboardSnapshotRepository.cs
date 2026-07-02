using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed class NoOpDashboardSnapshotRepository : IDashboardSnapshotRepository
{
    public Task<IReadOnlyList<StrategyPerformance>> GetStrategyPerformanceSnapshotAsync(
        int limit = 25_000,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyPerformance>>([]);
    }

    public Task<IReadOnlyList<StrategyRecentPerformance>> GetStrategyRecentPerformanceSnapshotAsync(
        int limit = 25_000,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyRecentPerformance>>([]);
    }

    public Task<int> UpsertStrategyPerformanceSnapshotAsync(
        IReadOnlyList<StrategyPerformance> strategies,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    public Task<int> UpsertStrategyRecentPerformanceSnapshotAsync(
        IReadOnlyList<StrategyRecentPerformance> strategies,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }
}
