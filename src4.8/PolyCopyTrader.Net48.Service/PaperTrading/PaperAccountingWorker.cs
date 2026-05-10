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
            "Paper accounting worker started. SettlementEnabled={SettlementEnabled} SettlementPollIntervalSeconds={SettlementPollIntervalSeconds} PerformanceRefreshSeconds={PerformanceRefreshSeconds}",
            paperTradingOptions.SettlementEnabled,
            paperTradingOptions.SettlementPollIntervalSeconds,
            paperTradingOptions.CopiedTraderPerformanceRefreshSeconds);

        var nextSettlementUtc = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions) &&
                    paperTradingOptions.SettlementEnabled &&
                    DateTimeOffset.UtcNow >= nextSettlementUtc)
                {
                    var result = await settlementProcessor.ProcessOpenPositionsAsync(stoppingToken);
                    nextSettlementUtc = DateTimeOffset.UtcNow.AddSeconds(paperTradingOptions.SettlementPollIntervalSeconds);
                    if (result.PositionsChecked > 0 || result.SettlementsInserted > 0)
                    {
                        logger.LogInformation(
                            "Paper settlement cycle completed. PositionsChecked={PositionsChecked} PositionsSettled={PositionsSettled} SettlementsInserted={SettlementsInserted} PerformanceRows={PerformanceRows}",
                            result.PositionsChecked,
                            result.PositionsSettled,
                            result.SettlementsInserted,
                            result.PerformanceRowsRefreshed);
                    }
                }
                else
                {
                    var rows = await repository.RefreshPaperCopiedTraderPerformanceAsync(stoppingToken);
                    logger.LogDebug("Paper copied-trader performance refreshed. Rows={Rows}", rows);
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

            await Task.Delay(TimeSpan.FromSeconds(paperTradingOptions.CopiedTraderPerformanceRefreshSeconds), stoppingToken);
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
