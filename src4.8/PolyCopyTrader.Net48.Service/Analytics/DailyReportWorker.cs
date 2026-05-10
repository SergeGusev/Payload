using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Analytics;

public sealed class DailyReportWorker(
    ILogger<DailyReportWorker> logger,
    AnalyticsOptions analyticsOptions,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!analyticsOptions.DailyReportGenerationEnabled)
        {
            logger.LogInformation("Daily analytics report generation is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await GenerateAsync(DateOnly.FromDateTime(DateTime.UtcNow), stoppingToken);
            await GenerateAsync(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(analyticsOptions.DailyReportRefreshMinutes), stoppingToken);
        }
    }

    private async Task GenerateAsync(DateOnly reportDate, CancellationToken cancellationToken)
    {
        try
        {
            var report = await repository.BuildDailyReportAsync(reportDate, cancellationToken);
            await repository.UpsertDailyReportAsync(report, cancellationToken);
            logger.LogInformation(
                "Daily analytics report generated for {ReportDate}. Signals={SignalsObserved} Accepted={SignalsAccepted} Fills={PaperFills}",
                report.ReportDate,
                report.SignalsObserved,
                report.SignalsAccepted,
                report.PaperFills);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Daily analytics report generation failed for {ReportDate}.", reportDate);
            await TryRecordApiErrorAsync(reportDate, ex.Message, cancellationToken);
        }
    }

    private async Task TryRecordApiErrorAsync(DateOnly reportDate, string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "DailyReportWorker", $"Generate:{reportDate:yyyy-MM-dd}", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist daily report generation error.");
        }
    }
}
