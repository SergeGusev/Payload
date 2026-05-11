using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.LiveTrading;

public sealed class LiveTradingMaintenanceWorker(
    ILogger<LiveTradingMaintenanceWorker> logger,
    LiveTradingOptions liveTradingOptions,
    ILiveTradingProcessor liveTradingProcessor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(liveTradingOptions.MaintenancePollIntervalSeconds);
        logger.LogInformation(
            "Live trading maintenance worker started. PollIntervalSeconds={PollIntervalSeconds}",
            liveTradingOptions.MaintenancePollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await liveTradingProcessor.ProcessOpenOrdersAsync(stoppingToken);
                if (result.OpenOrdersChecked > 0 ||
                    result.OrdersPolled > 0 ||
                    result.OrdersCanceled > 0 ||
                    result.BalanceSettlementsApplied > 0 ||
                    result.DataApiReconciledOrders > 0)
                {
                    logger.LogInformation(
                        "Live trading maintenance cycle completed. OpenChecked={OpenChecked} Polled={Polled} Canceled={Canceled} BalanceSettled={BalanceSettled} DataApiReconciled={DataApiReconciled}",
                        result.OpenOrdersChecked,
                        result.OrdersPolled,
                        result.OrdersCanceled,
                        result.BalanceSettlementsApplied,
                        result.DataApiReconciledOrders);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Live trading maintenance cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("Live trading maintenance worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "LiveTradingMaintenanceWorker", "ProcessOpenOrders", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist live trading maintenance API error.");
        }
    }
}
