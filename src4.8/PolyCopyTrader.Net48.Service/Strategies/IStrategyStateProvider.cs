using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.Strategies;

public interface IStrategyStateProvider
{
    Task<IReadOnlyDictionary<Guid, StrategyRuntimeSettings>> GetStrategySettingsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetEnabledStrategyIdsAsync(CancellationToken cancellationToken = default);

    Task<bool> IsStrategyEnabledAsync(Guid strategyId, CancellationToken cancellationToken = default);

    Task<StrategyRuntimeSettings> GetStrategySettingsAsync(Guid strategyId, CancellationToken cancellationToken = default);
}
