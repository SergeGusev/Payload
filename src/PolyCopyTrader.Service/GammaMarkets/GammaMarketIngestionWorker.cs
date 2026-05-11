using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.GammaMarkets;

public sealed class GammaMarketIngestionWorker(
    ILogger<GammaMarketIngestionWorker> logger,
    GammaMarketIngestionOptions options,
    IGammaMarketIngestionProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Gamma active market ingestion is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        logger.LogInformation(
            "Gamma active market ingestion worker started. PollIntervalSeconds={PollIntervalSeconds} PageLimit={PageLimit}",
            options.PollIntervalSeconds,
            options.PageLimit);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.RefreshAsync(stoppingToken);
                logger.LogInformation(
                    "Gamma active market ingestion cycle completed. Pages={Pages} Fetched={Fetched} Upserted={Upserted} ReachedEmptyPage={ReachedEmptyPage} NextOffset={NextOffset}",
                    result.PagesFetched,
                    result.MarketsFetched,
                    result.MarketsUpserted,
                    result.ReachedEmptyPage,
                    result.NextOffset);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Gamma active market ingestion cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("Gamma active market ingestion worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "GammaMarketIngestionWorker", "RefreshActiveMarkets", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Gamma active market ingestion error.");
        }
    }
}
