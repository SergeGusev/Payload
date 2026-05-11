using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.Strategies;

public interface IStrategyStateProvider
{
    Task<IReadOnlyDictionary<Guid, StrategyRuntimeSettings>> GetStrategySettingsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetEnabledStrategyIdsAsync(CancellationToken cancellationToken = default);

    async Task<bool> IsStrategyEnabledAsync(Guid strategyId, CancellationToken cancellationToken = default)
    {
        var enabledStrategyIds = await GetEnabledStrategyIdsAsync(cancellationToken);
        return enabledStrategyIds.Contains(StrategyIds.Normalize(strategyId));
    }

    async Task<StrategyRuntimeSettings> GetStrategySettingsAsync(Guid strategyId, CancellationToken cancellationToken = default)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        var settings = await GetStrategySettingsAsync(cancellationToken);
        return settings.TryGetValue(normalizedStrategyId, out var value)
            ? value
            : StrategyRuntimeSettings.Default(normalizedStrategyId);
    }
}
