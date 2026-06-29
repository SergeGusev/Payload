using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class ExposureSnapshotCacheWarmupService(
    ILogger<ExposureSnapshotCacheWarmupService> logger,
    IExposureSnapshotCache exposureCache,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await exposureCache.RefreshAsync(stoppingToken);
            logger.LogInformation("Exposure snapshot cache warmed up.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Exposure snapshot cache warmup failed.");
            await TryRecordApiErrorAsync(ex.Message, stoppingToken);
        }
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(
                    Guid.NewGuid(),
                    nameof(ExposureSnapshotCacheWarmupService),
                    "Warmup",
                    message,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist exposure snapshot cache warmup error.");
        }
    }
}
