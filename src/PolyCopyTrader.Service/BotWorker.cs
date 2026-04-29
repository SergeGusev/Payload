using System.Reflection;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service;

public sealed class BotWorker(
    ILogger<BotWorker> logger,
    BotOptions botOptions,
    IAppRepository repository) : BackgroundService
{
    private readonly DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PolyCopyTrader service started in scaffold mode.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var heartbeat = new ServiceHeartbeat(
                "PolyCopyTrader.Service",
                "Running",
                startedAtUtc,
                DateTimeOffset.UtcNow,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
                botOptions.Mode,
                "Heartbeat",
                null);

            await repository.UpsertServiceHeartbeatAsync(heartbeat, stoppingToken);

            logger.LogInformation(
                "Service heartbeat at {HeartbeatUtc}. Mode={Mode}",
                heartbeat.LastHeartbeatUtc,
                botOptions.Mode);

            await Task.Delay(TimeSpan.FromSeconds(botOptions.HeartbeatIntervalSeconds), stoppingToken);
        }
    }
}
