using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class ExposureSnapshotCacheWarmupService(
    ILogger<ExposureSnapshotCacheWarmupService> logger,
    IExposureSnapshotCache exposureCache,
    IAppRepository repository) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await exposureCache.GetSnapshotAsync(cancellationToken);
            logger.LogInformation("Exposure snapshot cache warmed up.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Exposure snapshot cache warmup failed.");
            await TryRecordApiErrorAsync(ex.Message, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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
