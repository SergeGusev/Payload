using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.DataApiTraderActivity;

public sealed class DataApiTraderActivitySyncWorker(
    ILogger<DataApiTraderActivitySyncWorker> logger,
    DataApiTraderIngestionOptions options,
    IDataApiTraderActivityIngestionProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Data API trader sync worker is disabled.");
            return;
        }

        var currentErrorDelay = TimeSpan.FromMilliseconds(options.ErrorDelayMilliseconds);
        logger.LogInformation(
            "Data API trader sync worker started. SyncBatchSize={SyncBatchSize} SyncPollDelayMilliseconds={SyncPollDelayMilliseconds} UserTradesLimit={UserTradesLimit} MaxUserHistoricalOffset={MaxUserHistoricalOffset} RefreshPositionsEnabled={RefreshPositionsEnabled} MaxPositionRefreshesPerCycle={MaxPositionRefreshesPerCycle}",
            options.SyncBatchSize,
            options.SyncPollDelayMilliseconds,
            options.UserTradesLimit,
            options.MaxUserHistoricalOffset,
            options.RefreshPositionsEnabled,
            options.MaxPositionRefreshesPerCycle);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.RefreshTraderSyncBatchAsync(stoppingToken);
                currentErrorDelay = TimeSpan.FromMilliseconds(options.ErrorDelayMilliseconds);
                logger.LogInformation(
                    "Data API trader sync batch completed. TradersSelected={TradersSelected} FullSyncs={FullSyncs} IncrementalSyncs={IncrementalSyncs} UserFetched={UserFetched} UserAdvanced={UserAdvanced} PositionRefreshes={PositionRefreshes} CurrentPositions={CurrentPositions} ClosedPositions={ClosedPositions} PositionsUpserted={PositionsUpserted} WalletPerformanceRows={WalletPerformanceRows} CategoryPerformanceRows={CategoryPerformanceRows} Failures={Failures} PositionFailures={PositionFailures}",
                    result.UniqueTraders,
                    result.FullSyncs,
                    result.IncrementalSyncs,
                    result.UserTradesFetched,
                    result.UserTradesAdvanced,
                    result.PositionRefreshes,
                    result.CurrentPositionsFetched,
                    result.ClosedPositionsFetched,
                    result.PositionsUpserted,
                    result.WalletPerformanceRowsUpserted,
                    result.CategoryPerformanceRowsUpserted,
                    result.TraderSyncFailures,
                    result.PositionRefreshFailures);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Data API trader sync batch failed. Retrying in {DelayMilliseconds} ms.", currentErrorDelay.TotalMilliseconds);
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

        logger.LogInformation("Data API trader sync worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "DataApiTraderActivitySyncWorker", "RefreshTraderSyncBatch", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Data API trader sync worker error.");
        }
    }
}
