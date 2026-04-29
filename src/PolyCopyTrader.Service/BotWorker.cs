using System.Reflection;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Service.Signals;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service;

public sealed class BotWorker(
    ILogger<BotWorker> logger,
    BotOptions botOptions,
    IAppRepository repository,
    IWatchlistScanner watchlistScanner,
    ISignalProcessor signalProcessor,
    IPaperTradingProcessor paperTradingProcessor) : BackgroundService
{
    private readonly DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PolyCopyTrader service started in {Mode} mode.", botOptions.Mode);

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentLoop = "Watchlist scan pending";
            var lastError = default(string);

            try
            {
                var scanStatus = await watchlistScanner.ScanOnceAsync(stoppingToken);
                var signalResult = await signalProcessor.ProcessQueuedAsync(stoppingToken);
                var paperResult = await paperTradingProcessor.ProcessOpenOrdersAsync(stoppingToken);
                currentLoop =
                    $"Scanner={scanStatus.ScannerStatus}; TradesFetched={scanStatus.TradesFetched}; " +
                    $"NewTradesStored={scanStatus.NewTradesStored}; PositionsFetched={scanStatus.PositionsFetched}; " +
                    $"SignalsAccepted={signalResult.SignalsAccepted}; SignalsRejected={signalResult.SignalsRejected}; " +
                    $"PaperOrdersCreated={signalResult.PaperOrdersCreated}; PaperFilled={paperResult.OrdersFilled}; " +
                    $"PaperExpired={paperResult.OrdersExpired}";

                logger.LogInformation(
                    "Watchlist scan completed. Status={ScannerStatus} TradesFetched={TradesFetched} NewTradesStored={NewTradesStored} PositionsFetched={PositionsFetched} SignalsAccepted={SignalsAccepted} SignalsRejected={SignalsRejected} PaperOrdersCreated={PaperOrdersCreated} PaperFilled={PaperFilled} PaperExpired={PaperExpired}",
                    scanStatus.ScannerStatus,
                    scanStatus.TradesFetched,
                    scanStatus.NewTradesStored,
                    scanStatus.PositionsFetched,
                    signalResult.SignalsAccepted,
                    signalResult.SignalsRejected,
                    signalResult.PaperOrdersCreated,
                    paperResult.OrdersFilled,
                    paperResult.OrdersExpired);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                currentLoop = "Watchlist scan failed";
                lastError = ex.Message;
                logger.LogError(ex, "Watchlist scan loop failed.");
                await repository.AddApiErrorAsync(
                    new ApiError(Guid.NewGuid(), "BotWorker", "WatchlistScanLoop", ex.Message, DateTimeOffset.UtcNow),
                    stoppingToken);
            }

            var heartbeat = new ServiceHeartbeat(
                "PolyCopyTrader.Service",
                "Running",
                startedAtUtc,
                DateTimeOffset.UtcNow,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
                botOptions.Mode,
                currentLoop,
                lastError);

            await repository.UpsertServiceHeartbeatAsync(heartbeat, stoppingToken);

            logger.LogInformation(
                "Service heartbeat at {HeartbeatUtc}. Mode={Mode}",
                heartbeat.LastHeartbeatUtc,
                botOptions.Mode);

            await Task.Delay(TimeSpan.FromSeconds(botOptions.PollIntervalSeconds), stoppingToken);
        }
    }
}
