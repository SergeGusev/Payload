using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public interface IDashboardSnapshotRepository
{
    Task<PaperConfirmationProgress?> GetPaperConfirmationProgressAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<PaperConfirmationProgress?>(null);
    Task<IReadOnlyList<StrategyPerformance>> GetStrategyPerformanceSnapshotAsync(
        int limit = 25_000,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StrategyRecentPerformance>> GetStrategyRecentPerformanceSnapshotAsync(
        int limit = 25_000,
        CancellationToken cancellationToken = default);

    Task<int> UpsertStrategyPerformanceSnapshotAsync(
        IReadOnlyList<StrategyPerformance> strategies,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken = default);

    Task<int> UpsertStrategyRecentPerformanceSnapshotAsync(
        IReadOnlyList<StrategyRecentPerformance> strategies,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken = default);
}
