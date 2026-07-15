using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperAccountingWorker(
    ILogger<PaperAccountingWorker> logger,
    BotOptions botOptions,
    PaperTradingOptions paperTradingOptions,
    IPaperSettlementProcessor settlementProcessor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Paper accounting worker started. SettlementEnabled={SettlementEnabled} SettlementPollIntervalSeconds={SettlementPollIntervalSeconds}",
            paperTradingOptions.SettlementEnabled,
            paperTradingOptions.SettlementPollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions) &&
                    paperTradingOptions.SettlementEnabled)
                {
                    var result = await settlementProcessor.ProcessOpenPositionsAsync(stoppingToken);
                    if (result.PositionsChecked > 0 || result.SettlementsInserted > 0)
                    {
                        logger.LogInformation(
                            "Paper settlement cycle completed. PositionsChecked={PositionsChecked} PositionsSettled={PositionsSettled} SettlementsInserted={SettlementsInserted}",
                            result.PositionsChecked,
                            result.PositionsSettled,
                            result.SettlementsInserted);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Paper accounting worker cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(paperTradingOptions.SettlementPollIntervalSeconds), stoppingToken);
        }
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "PaperAccountingWorker", "Cycle", message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist paper accounting API error.");
        }
    }
}
