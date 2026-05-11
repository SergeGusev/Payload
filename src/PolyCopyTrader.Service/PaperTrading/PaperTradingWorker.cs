using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperTradingWorker(
    ILogger<PaperTradingWorker> logger,
    BotOptions botOptions,
    PaperTradingOptions paperTradingOptions,
    IPaperTradingProcessor paperTradingProcessor,
    ServiceControlState controlState,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(paperTradingOptions.OpenOrderProcessingIntervalSeconds);
        logger.LogInformation(
            "Paper trading open-order worker started. Mode={Mode} RunInLiveMode={RunInLiveMode} OpenOrderProcessingIntervalSeconds={OpenOrderProcessingIntervalSeconds}",
            botOptions.Mode,
            paperTradingOptions.RunInLiveMode,
            paperTradingOptions.OpenOrderProcessingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions) &&
                    !controlState.PaperTradingPaused)
                {
                    var result = await paperTradingProcessor.ProcessOpenOrdersAsync(stoppingToken);
                    if (result.OpenOrdersChecked > 0 ||
                        result.OrdersFilled > 0 ||
                        result.OrdersExpired > 0 ||
                        result.PositionsUpdated > 0)
                    {
                        logger.LogInformation(
                            "Paper open-order cycle completed. OpenOrdersChecked={OpenOrdersChecked} OrdersFilled={OrdersFilled} OrdersExpired={OrdersExpired} PositionsUpdated={PositionsUpdated}",
                            result.OpenOrdersChecked,
                            result.OrdersFilled,
                            result.OrdersExpired,
                            result.PositionsUpdated);
                    }

                    if (result.OrdersFilled > 0 ||
                        result.OrdersExpired > 0 ||
                        result.PositionsUpdated > 0)
                    {
                        controlState.RecordLoop(
                            $"PaperOpenOrders Checked={result.OpenOrdersChecked}; Filled={result.OrdersFilled}; Expired={result.OrdersExpired}; PositionsUpdated={result.PositionsUpdated}",
                            null);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                controlState.RecordLoop("Paper open-order cycle failed", ex.Message);
                logger.LogError(ex, "Paper open-order cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("Paper trading open-order worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "PaperTradingWorker", "Cycle", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist paper trading worker error.");
        }
    }
}
