using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class BtcUpDown5mStatisticsWorker(
    ILogger<BtcUpDown5mStatisticsWorker> logger,
    BtcUpDown5mStatisticsOptions options,
    IBtcUpDown5mStatisticsProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("BTC Up or Down 5m Statistics strategy is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        logger.LogInformation(
            "BTC Up or Down 5m Statistics worker started. PollIntervalSeconds={PollIntervalSeconds} MinHistorySupport={MinHistorySupport} MinimumEdge={MinimumEdge}",
            options.PollIntervalSeconds,
            options.MinHistorySupport,
            options.MinimumEdge);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.ProcessAsync(stoppingToken);
                if (result.TicksStored > 0 ||
                    result.WouldBet > 0 ||
                    result.SkippedInsufficientHistory > 0 ||
                    result.HistoryObservationsApplied > 0)
                {
                    logger.LogInformation(
                        "BTC Up or Down 5m Statistics cycle completed. Markets={Markets} Stored={Stored} WouldBet={WouldBet} Insufficient={Insufficient} NoEdge={NoEdge} HistoryQueued={HistoryQueued} HistoryApplied={HistoryApplied} PendingResult={PendingResult}",
                        result.MarketsScanned,
                        result.TicksStored,
                        result.WouldBet,
                        result.SkippedInsufficientHistory,
                        result.SkippedNoEdge,
                        result.HistoryObservationsQueued,
                        result.HistoryObservationsApplied,
                        result.HistoryObservationsPendingResult);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BTC Up or Down 5m Statistics cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("BTC Up or Down 5m Statistics worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), nameof(BtcUpDown5mStatisticsWorker), "Cycle", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist BTC Up or Down 5m Statistics worker error.");
        }
    }
}
