using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Analytics;

public sealed class DateDependentStrategyHourlyPaperPnlWorker(
    ILogger<DateDependentStrategyHourlyPaperPnlWorker> logger,
    IAppRepository repository) : BackgroundService
{
    private static readonly TimeSpan HourlyCadence = TimeSpan.FromHours(1);
    private static readonly TimeSpan RefreshOffset = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await DelayAsync(GetDelayUntilNextHourlyRefresh(DateTimeOffset.UtcNow), stoppingToken);
            await RefreshAsync(stoppingToken);
        }
    }

    internal static TimeSpan GetDelayUntilNextHourlyRefresh(DateTimeOffset nowUtc)
    {
        var utcNow = nowUtc.ToUniversalTime();
        var utcDateTime = utcNow.UtcDateTime;
        var boundaryTicks = utcDateTime.Ticks - utcDateTime.Ticks % HourlyCadence.Ticks;
        var candidate = new DateTimeOffset(new DateTime(boundaryTicks, DateTimeKind.Utc))
            .Add(HourlyCadence)
            .Add(RefreshOffset);

        while (candidate <= utcNow)
        {
            candidate += HourlyCadence;
        }

        return candidate - utcNow;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var strategyIds = StrategyIds.DateDependentStrategyVariants
            .Select(variant => variant.Id)
            .Distinct()
            .ToArray();
        if (strategyIds.Length == 0)
        {
            return;
        }

        try
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            var rowCount = await repository.RefreshDateDependentStrategyHourlyPaperPnlAsync(
                strategyIds,
                startedAtUtc,
                cancellationToken);
            logger.LogInformation(
                "Date-dependent strategy hourly Paper PnL refreshed. Strategies={StrategyCount} Rows={RowCount} DurationMs={DurationMs}",
                strategyIds.Length,
                rowCount,
                (DateTimeOffset.UtcNow - startedAtUtc).TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Date-dependent strategy hourly Paper PnL refresh failed.");
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
                    nameof(DateDependentStrategyHourlyPaperPnlWorker),
                    "RefreshHourlyPaperPnl",
                    message,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist date-dependent strategy hourly Paper PnL refresh error.");
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }
}
