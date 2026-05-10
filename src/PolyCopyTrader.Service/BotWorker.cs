using System.Reflection;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Control;
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

                currentLoop =
                    $"Scanner={scanStatus.ScannerStatus}; TradesFetched={scanStatus.TradesFetched}; " +
                    $"NewTradesStored={scanStatus.NewTradesStored}; PositionsFetched={scanStatus.PositionsFetched}; " +
                    $"SignalsAccepted={signalResult.SignalsAccepted}; SignalsRejected={signalResult.SignalsRejected}; " +
                    $"PaperOrdersCreated={signalResult.PaperOrdersCreated}; LiveOrdersSubmitted={signalResult.LiveOrdersSubmitted}";

                logger.LogInformation(
                    "Watchlist scan completed. Status={ScannerStatus} TradesFetched={TradesFetched} NewTradesStored={NewTradesStored} PositionsFetched={PositionsFetched} SignalsAccepted={SignalsAccepted} SignalsRejected={SignalsRejected} PaperOrdersCreated={PaperOrdersCreated} LiveOrdersSubmitted={LiveOrdersSubmitted}",
                    scanStatus.ScannerStatus,
                    scanStatus.TradesFetched,
                    scanStatus.NewTradesStored,
                    scanStatus.PositionsFetched,
                    signalResult.SignalsAccepted,
                    signalResult.SignalsRejected,
                    signalResult.PaperOrdersCreated,
                    signalResult.LiveOrdersSubmitted);
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
                logger.LogError(ex, "Heartbeat persistence failed. Service will keep running and retry.");
                currentLoop = AppendHeartbeatPersistenceFailure(currentLoop, ex.Message);
                controlState.RecordLoop(currentLoop, lastError);
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

    private static string AppendHeartbeatPersistenceFailure(string currentLoop, string message)
    {
        const int maxMessageLength = 300;
        var trimmed = string.IsNullOrWhiteSpace(message) ? "unknown error" : message.Trim();
        if (trimmed.Length > maxMessageLength)
        {
            trimmed = trimmed[..maxMessageLength];
        }

        return currentLoop + "; Heartbeat persistence failed: " + trimmed;
    }
}
