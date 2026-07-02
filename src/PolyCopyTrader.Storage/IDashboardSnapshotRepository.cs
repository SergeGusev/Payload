using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public interface IDashboardSnapshotRepository
{
    Task<IReadOnlyList<StrategyPerformance>> GetStrategyPerformanceSnapshotAsync(
        int limit = 25_000,
        CancellationToken cancellationToken = default);

    Task<int> UpsertStrategyPerformanceSnapshotAsync(
        IReadOnlyList<StrategyPerformance> strategies,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken = default);
}
