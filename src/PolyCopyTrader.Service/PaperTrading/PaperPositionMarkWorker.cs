using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperPositionMarkWorker(
    ILogger<PaperPositionMarkWorker> logger,
    BotOptions botOptions,
    PaperTradingOptions paperTradingOptions,
    IPaperPositionMarkProcessor positionMarkProcessor,
    ServiceControlState controlState,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(paperTradingOptions.OpenOrderProcessingIntervalSeconds);
        logger.LogInformation(
            "Paper position-mark worker started. Mode={Mode} RunInLiveMode={RunInLiveMode} PositionMarkProcessingIntervalSeconds={PositionMarkProcessingIntervalSeconds}",
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
                    var positionsUpdated = await positionMarkProcessor.RefreshPositionMarksAsync(stoppingToken);
                    if (positionsUpdated > 0)
                    {
                        logger.LogInformation(
                            "Paper position-mark cycle completed. PositionsUpdated={PositionsUpdated}",
                            positionsUpdated);
                        controlState.RecordLoop(
                            $"PaperPositionMarks PositionsUpdated={positionsUpdated}",
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
                controlState.RecordLoop("Paper position-mark cycle failed", ex.Message);
                logger.LogError(ex, "Paper position-mark cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("Paper position-mark worker stopped.");
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "PaperPositionMarkWorker", "Cycle", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Paper position-mark worker error.");
        }
    }
}
