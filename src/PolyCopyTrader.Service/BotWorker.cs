using System.Reflection;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.LiveTrading;
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
    IPaperTradingProcessor paperTradingProcessor,
    ILiveTradingProcessor liveTradingProcessor,
    ServiceControlState controlState) : BackgroundService
{
    private readonly DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PolyCopyTrader service started in {Mode} mode.", botOptions.Mode);
        controlState.MarkRunning();

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentLoop = "Watchlist scan pending";
            var lastError = default(string);

            try
            {
                var scanStatus = controlState.ScanningPaused
                    ? new ScannerStatusSnapshot(
                        "WatchlistScanner",
                        null,
                        null,
                        null,
                        0,
                        0,
                        0,
                        "Paused",
                        DateTimeOffset.UtcNow)
                    : await watchlistScanner.ScanOnceAsync(stoppingToken);

                var signalResult = controlState.ScanningPaused
                    ? new SignalProcessingResult(0, 0, 0, 0)
                    : await signalProcessor.ProcessQueuedAsync(stoppingToken);

                var paperResult = controlState.PaperTradingPaused
                    ? new PaperTradingProcessingResult(0, 0, 0, 0)
                    : await paperTradingProcessor.ProcessOpenOrdersAsync(stoppingToken);

                var liveResult = await liveTradingProcessor.ProcessOpenOrdersAsync(stoppingToken);

                currentLoop =
                    $"Scanner={scanStatus.ScannerStatus}; TradesFetched={scanStatus.TradesFetched}; " +
                    $"NewTradesStored={scanStatus.NewTradesStored}; PositionsFetched={scanStatus.PositionsFetched}; " +
                    $"SignalsAccepted={signalResult.SignalsAccepted}; SignalsRejected={signalResult.SignalsRejected}; " +
                    $"PaperOrdersCreated={signalResult.PaperOrdersCreated}; PaperFilled={paperResult.OrdersFilled}; " +
                    $"PaperExpired={paperResult.OrdersExpired}; LiveOrdersSubmitted={signalResult.LiveOrdersSubmitted}; " +
                    $"LiveOpenChecked={liveResult.OpenOrdersChecked}; LiveCanceled={liveResult.OrdersCanceled}";

                logger.LogInformation(
                    "Watchlist scan completed. Status={ScannerStatus} TradesFetched={TradesFetched} NewTradesStored={NewTradesStored} PositionsFetched={PositionsFetched} SignalsAccepted={SignalsAccepted} SignalsRejected={SignalsRejected} PaperOrdersCreated={PaperOrdersCreated} PaperFilled={PaperFilled} PaperExpired={PaperExpired} LiveOrdersSubmitted={LiveOrdersSubmitted} LiveCanceled={LiveCanceled}",
                    scanStatus.ScannerStatus,
                    scanStatus.TradesFetched,
                    scanStatus.NewTradesStored,
                    scanStatus.PositionsFetched,
                    signalResult.SignalsAccepted,
                    signalResult.SignalsRejected,
                    signalResult.PaperOrdersCreated,
                    paperResult.OrdersFilled,
                    paperResult.OrdersExpired,
                    signalResult.LiveOrdersSubmitted,
                    liveResult.OrdersCanceled);
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
                controlState.MarkError(ex.Message);
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            controlState.RecordLoop(currentLoop, lastError);

            var heartbeat = new ServiceHeartbeat(
                "PolyCopyTrader.Service",
                controlState.Snapshot.RunState.ToString(),
                startedAtUtc,
                DateTimeOffset.UtcNow,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
                botOptions.Mode,
                currentLoop,
                lastError);

            try
            {
                await repository.UpsertServiceHeartbeatAsync(heartbeat, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Heartbeat persistence failed. Pausing scanning and paper trading.");
                controlState.PauseAll("BotWorker");
                controlState.MarkError("Heartbeat persistence failed: " + ex.Message);
            }

            logger.LogInformation(
                "Service heartbeat at {HeartbeatUtc}. Mode={Mode}",
                heartbeat.LastHeartbeatUtc,
                botOptions.Mode);

            await Task.Delay(TimeSpan.FromSeconds(botOptions.PollIntervalSeconds), stoppingToken);
        }

        controlState.MarkStopped();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        controlState.MarkStopping();
        await base.StopAsync(cancellationToken);
        controlState.MarkStopped();
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "BotWorker", "WatchlistScanLoop", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist API error from BotWorker.");
        }
    }
}
