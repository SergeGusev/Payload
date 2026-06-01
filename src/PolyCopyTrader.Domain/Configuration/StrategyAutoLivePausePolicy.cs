using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Domain.Configuration;

public static class StrategyAutoLivePausePolicy
{
    public static bool IsEnabledForStrategy(LiveTradingOptions options, Guid strategyId)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        return GetEnabledStrategyIds(options).Contains(normalizedStrategyId);
    }

    public static HashSet<Guid> GetEnabledStrategyIds(LiveTradingOptions options)
    {
        var strategyIds = new HashSet<Guid>();
        foreach (var strategy in options.AutoLivePauseStrategies)
        {
            if (TryNormalizeStrategyIdentifier(strategy, out var strategyId))
            {
                strategyIds.Add(strategyId);
            }
        }

        return strategyIds;
    }

    public static bool TryNormalizeStrategyIdentifier(string? value, out Guid strategyId)
    {
        strategyId = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalizedValue = value.Trim();
        if (Guid.TryParse(normalizedValue, out var parsedStrategyId))
        {
            strategyId = StrategyIds.Normalize(parsedStrategyId);
            return true;
        }

        var knownStrategyId = StrategyIds.TryGetStrategyIdByCode(normalizedValue);
        if (knownStrategyId is null)
        {
            return false;
        }

        strategyId = StrategyIds.Normalize(knownStrategyId.Value);
        return true;
    }
}
