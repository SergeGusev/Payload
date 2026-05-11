using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.DataApiTraderActivity;

public sealed class DataApiTraderRatingRefreshWorker(
    ILogger<DataApiTraderRatingRefreshWorker> logger,
    DataApiTraderIngestionOptions options,
    IDataApiTraderActivityIngestionProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !options.RefreshPolymarketRatingsEnabled)
        {
            logger.LogInformation("Data API Polymarket-only rating refresh is disabled.");
            return;
        }

        var currentErrorDelay = TimeSpan.FromMilliseconds(options.ErrorDelayMilliseconds);
        logger.LogInformation(
            "Data API Polymarket-only rating refresh worker started. BatchSize={BatchSize} TimePeriod={TimePeriod} OrderBy={OrderBy} RefreshIntervalSeconds={RefreshIntervalSeconds}",
            options.SyncBatchSize,
            options.PolymarketRatingTimePeriod,
            options.PolymarketRatingOrderBy,
            options.PolymarketRatingRefreshIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.RefreshPolymarketRatingBatchAsync(stoppingToken);
                currentErrorDelay = TimeSpan.FromMilliseconds(options.ErrorDelayMilliseconds);
                logger.LogInformation(
                    "Data API Polymarket-only rating batch completed. TradersSelected={TradersSelected} Mappings={Mappings} WalletRefreshes={WalletRefreshes} WalletFailures={WalletFailures} RatingRows={RatingRows} CurrentPositions={CurrentPositions} ClosedPositions={ClosedPositions}",
                    result.TradersSelected,
                    result.MappingsLoaded,
                    result.WalletRefreshes,
                    result.WalletFailures,
                    result.RatingRowsUpserted,
                    result.CurrentPositionsFetched,
                    result.ClosedPositionsFetched);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Data API Polymarket-only rating batch failed. Retrying in {DelayMilliseconds} ms.", currentErrorDelay.TotalMilliseconds);
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
                await Task.Delay(currentErrorDelay, stoppingToken);
                currentErrorDelay = TimeSpan.FromMilliseconds(Math.Min(
                    options.MaxErrorDelayMilliseconds,
                    Math.Max(options.ErrorDelayMilliseconds, currentErrorDelay.TotalMilliseconds * 2)));
                continue;
            }

            if (options.SyncPollDelayMilliseconds > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(options.SyncPollDelayMilliseconds), stoppingToken);
            }
            else
            {
                await Task.Yield();
            }
        }

        logger.LogInformation("Data API Polymarket-only rating refresh worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "DataApiTraderRatingRefreshWorker", "RefreshPolymarketRatings", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Data API Polymarket-only rating worker error.");
        }
    }
}
