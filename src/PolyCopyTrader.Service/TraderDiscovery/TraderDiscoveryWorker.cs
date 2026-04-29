using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.TraderDiscovery;

public sealed class TraderDiscoveryWorker(
    ILogger<TraderDiscoveryWorker> logger,
    TraderDiscoveryOptions options,
    ITraderDiscoveryProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Trader discovery is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshOnceAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(options.RefreshIntervalMinutes), stoppingToken);
        }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await processor.RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Trader discovery refresh failed.");
            await TryRecordApiErrorAsync(ex.Message, cancellationToken);
        }
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "TraderDiscoveryWorker", "Refresh", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist trader discovery error.");
        }
    }
}
