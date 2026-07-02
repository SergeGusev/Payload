using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Analytics;

public sealed class DashboardStrategyPerformanceSnapshotWorker(
    ILogger<DashboardStrategyPerformanceSnapshotWorker> logger,
    IAppRepository repository,
    IDashboardSnapshotRepository dashboardSnapshots) : BackgroundService
{
    private const int StrategySnapshotLimit = 25_000;
    private static readonly TimeSpan RefreshCadence = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MarketCadence = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan QuietSlotOffset = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastRefreshAttemptAtUtc = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            await DelayAsync(
                GetDelayUntilNextQuietSlot(DateTimeOffset.UtcNow, lastRefreshAttemptAtUtc),
                stoppingToken);
            lastRefreshAttemptAtUtc = DateTimeOffset.UtcNow;
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            var strategies = await repository.GetStrategyPerformanceAsync(StrategySnapshotLimit, cancellationToken);
            var strategyRowCount = await dashboardSnapshots.UpsertStrategyPerformanceSnapshotAsync(
                strategies,
                DateTimeOffset.UtcNow,
                cancellationToken);
            var recentStrategies = await repository.GetStrategyRecentPerformanceAsync(StrategySnapshotLimit, cancellationToken);
            var recentRowCount = await dashboardSnapshots.UpsertStrategyRecentPerformanceSnapshotAsync(
                recentStrategies,
                DateTimeOffset.UtcNow,
                cancellationToken);
            logger.LogInformation(
                "Dashboard strategy performance snapshots refreshed. StrategyRows={StrategyRowCount} RecentRows={RecentRowCount} DurationMs={DurationMs}",
                strategyRowCount,
                recentRowCount,
                (DateTimeOffset.UtcNow - startedAtUtc).TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dashboard strategy performance snapshot refresh failed.");
            await TryRecordApiErrorAsync(ex.Message, cancellationToken);
        }
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(
                    Guid.NewGuid(),
                    nameof(DashboardStrategyPerformanceSnapshotWorker),
                    "RefreshStrategyPerformanceSnapshots",
                    message,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist dashboard strategy performance snapshot refresh error.");
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static TimeSpan GetDelayUntilNextQuietSlot(
        DateTimeOffset nowUtc,
        DateTimeOffset lastRefreshAttemptAtUtc)
    {
        var earliestRefreshAtUtc = lastRefreshAttemptAtUtc == DateTimeOffset.MinValue
            ? nowUtc
            : Max(nowUtc, lastRefreshAttemptAtUtc + RefreshCadence);
        var nextQuietSlotUtc = GetNextQuietSlotAtOrAfter(earliestRefreshAtUtc);
        return nextQuietSlotUtc <= nowUtc ? TimeSpan.Zero : nextQuietSlotUtc - nowUtc;
    }

    private static DateTimeOffset GetNextQuietSlotAtOrAfter(DateTimeOffset timestampUtc)
    {
        var utcDateTime = timestampUtc.UtcDateTime;
        var boundaryTicks = utcDateTime.Ticks - utcDateTime.Ticks % MarketCadence.Ticks;
        var candidate = new DateTimeOffset(new DateTime(boundaryTicks, DateTimeKind.Utc)) + QuietSlotOffset;
        while (candidate < timestampUtc)
        {
            candidate += MarketCadence;
        }

        return candidate;
    }

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second)
    {
        return first >= second ? first : second;
    }
}
