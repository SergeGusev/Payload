using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Startup;

public sealed class StrategyAutoLivePauseAllowlistSynchronizer(
    ILogger<StrategyAutoLivePauseAllowlistSynchronizer> logger,
    LiveTradingOptions liveTradingOptions,
    IAppRepository repository)
{
    public async Task<int> SynchronizeAsync(DateTimeOffset updatedAtUtc, CancellationToken cancellationToken)
    {
        var allowlistedStrategyIds = StrategyAutoLivePausePolicy.GetEnabledStrategyIds(liveTradingOptions).ToArray();

        var cleared = await repository.ClearStrategyAutoLivePauseExceptAsync(
            allowlistedStrategyIds,
            updatedAtUtc,
            cancellationToken);

        if (cleared > 0)
        {
            logger.LogInformation(
                "Cleared stale Auto Live Pause state for {StrategyCount} strategies outside LiveTrading:AutoLivePauseStrategies.",
                cleared);
        }
        else
        {
            logger.LogInformation("No stale Auto Live Pause state found outside LiveTrading:AutoLivePauseStrategies.");
        }

        return cleared;
    }
}
