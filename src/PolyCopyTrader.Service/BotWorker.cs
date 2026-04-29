namespace PolyCopyTrader.Service;

public sealed class BotWorker(ILogger<BotWorker> logger) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PolyCopyTrader service started in scaffold mode.");

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "Service heartbeat at {HeartbeatUtc}. Mode={Mode}",
                DateTimeOffset.UtcNow,
                "ReadOnly");

            await Task.Delay(HeartbeatInterval, stoppingToken);
        }
    }
}
