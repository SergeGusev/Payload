using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.DataApiTraderActivity;

public sealed class DataApiTraderActivityIngestionWorker(
    ILogger<DataApiTraderActivityIngestionWorker> logger,
    DataApiTraderIngestionOptions options,
    IDataApiTraderActivityIngestionProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Data API trader activity ingestion is disabled.");
            return;
        }

        var currentErrorDelay = TimeSpan.FromMilliseconds(options.ErrorDelayMilliseconds);
        logger.LogInformation(
            "Data API trader discovery worker started. GlobalTradesLimit={GlobalTradesLimit} PollDelayMilliseconds={PollDelayMilliseconds} MaxTradersPerCycle={MaxTradersPerCycle}",
            options.GlobalTradesLimit,
            options.PollDelayMilliseconds,
            options.MaxTradersPerCycle);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.RefreshAsync(stoppingToken);
                currentErrorDelay = TimeSpan.FromMilliseconds(options.ErrorDelayMilliseconds);
                logger.LogInformation(
                    "Data API trader discovery cycle completed. GlobalFetched={GlobalFetched} UsableGlobal={UsableGlobal} UniqueTraders={UniqueTraders} TradersUpserted={TradersUpserted}",
                    result.GlobalTradesFetched,
                    result.UsableGlobalTrades,
                    result.UniqueTraders,
                    result.TradersUpserted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Data API trader discovery cycle failed. Retrying in {DelayMilliseconds} ms.", currentErrorDelay.TotalMilliseconds);
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
                await Task.Delay(currentErrorDelay, stoppingToken);
                currentErrorDelay = TimeSpan.FromMilliseconds(Math.Min(
                    options.MaxErrorDelayMilliseconds,
                    Math.Max(options.ErrorDelayMilliseconds, currentErrorDelay.TotalMilliseconds * 2)));
                continue;
            }

            if (options.PollDelayMilliseconds > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(options.PollDelayMilliseconds), stoppingToken);
            }
            else
            {
                await Task.Yield();
            }
        }

        logger.LogInformation("Data API trader discovery worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "DataApiTraderActivityIngestionWorker", "Refresh", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Data API trader activity worker error.");
        }
    }
}
