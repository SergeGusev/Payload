using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.AutoRedeem;

public sealed class PolymarketAutoRedeemWorker(
    ILogger<PolymarketAutoRedeemWorker> logger,
    PolymarketAutoRedeemOptions options,
    IPolymarketAutoRedeemProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Polymarket auto redeem worker is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        var baseErrorDelay = TimeSpan.FromSeconds(options.BackgroundErrorDelaySeconds);
        var maxErrorDelay = TimeSpan.FromSeconds(options.BackgroundMaxErrorDelaySeconds);
        var currentErrorDelay = baseErrorDelay;

        logger.LogInformation(
            "Polymarket auto redeem worker started. PollIntervalSeconds={PollIntervalSeconds} DryRun={DryRun} AutoSubmitEnabled={AutoSubmitEnabled}",
            options.PollIntervalSeconds,
            options.DryRun,
            options.AutoSubmitEnabled);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.ProcessAsync(stoppingToken);
                currentErrorDelay = baseErrorDelay;

                if (result.PositionsFetched > 0 ||
                    result.RedeemablePositions > 0 ||
                    result.AttemptsRecorded > 0 ||
                    result.Skipped > 0)
                {
                    logger.LogInformation(
                        "Polymarket auto redeem cycle completed. PositionsFetched={PositionsFetched} Redeemable={Redeemable} AttemptsRecorded={AttemptsRecorded} Skipped={Skipped} Submitted={Submitted}",
                        result.PositionsFetched,
                        result.RedeemablePositions,
                        result.AttemptsRecorded,
                        result.Skipped,
                        result.Submitted);
                }

                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Polymarket auto redeem cycle failed. Retrying in {DelaySeconds} seconds.", currentErrorDelay.TotalSeconds);
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
                await Task.Delay(currentErrorDelay, stoppingToken);
                currentErrorDelay = NextErrorDelay(currentErrorDelay, maxErrorDelay);
            }
        }

        logger.LogInformation("Polymarket auto redeem worker stopped.");
    }

    private static TimeSpan NextErrorDelay(TimeSpan current, TimeSpan max)
    {
        return TimeSpan.FromSeconds(Math.Min(current.TotalSeconds * 2, max.TotalSeconds));
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "PolymarketAutoRedeemWorker", "ProcessAutoRedeem", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Polymarket auto redeem API error.");
        }
    }
}
