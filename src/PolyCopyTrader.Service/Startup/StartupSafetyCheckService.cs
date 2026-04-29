using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Startup;

public sealed class StartupSafetyCheckService(
    ILogger<StartupSafetyCheckService> logger,
    IPolymarketGeoClient geoClient,
    ServiceControlState controlState,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var geoblock = await geoClient.GetGeoblockStatusAsync(stoppingToken);
            var status = geoblock.Blocked ? "Blocked" : "OK";
            var details =
                $"blocked={geoblock.Blocked}; ip={geoblock.Ip ?? "unknown"}; country={geoblock.Country ?? "unknown"}; region={geoblock.Region ?? "unknown"}";

            if (geoblock.Blocked)
            {
                controlState.PauseLiveTrading("StartupSafetyCheck");
                logger.LogWarning("Startup geoblock check blocked live trading. {Details}", details);
            }
            else
            {
                logger.LogInformation("Startup geoblock check passed. {Details}", details);
            }

            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(Guid.NewGuid(), "StartupGeoblockCheck", status, details, DateTimeOffset.UtcNow),
                stoppingToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            controlState.PauseLiveTrading("StartupSafetyCheck");
            logger.LogError(ex, "Startup geoblock check failed. Live trading paused.");
            await TryRecordAsync(ex.Message, stoppingToken);
        }
    }

    private async Task TryRecordAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(Guid.NewGuid(), "StartupGeoblockCheck", "Error", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist startup geoblock check failure.");
        }
    }
}
