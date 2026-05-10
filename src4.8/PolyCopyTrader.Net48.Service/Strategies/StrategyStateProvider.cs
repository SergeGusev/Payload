using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class StrategyStateProvider(
    ILogger<StrategyStateProvider> logger,
    IAppRepository repository) : IStrategyStateProvider
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private IReadOnlyDictionary<Guid, StrategyRuntimeSettings>? strategySettings;
    private IReadOnlyCollection<Guid>? enabledStrategyIds;
    private DateTimeOffset refreshedAtUtc;

    public async Task<IReadOnlyDictionary<Guid, StrategyRuntimeSettings>> GetStrategySettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRefreshedAsync(cancellationToken);
        return strategySettings ?? StrategyIds.AllStrategyIds.ToDictionary(StrategyIds.Normalize, StrategyRuntimeSettings.Default);
    }

    public async Task<IReadOnlyCollection<Guid>> GetEnabledStrategyIdsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRefreshedAsync(cancellationToken);
        return enabledStrategyIds ?? new HashSet<Guid>();
    }

    public async Task<bool> IsStrategyEnabledAsync(Guid strategyId, CancellationToken cancellationToken = default)
    {
        var currentEnabledStrategyIds = await GetEnabledStrategyIdsAsync(cancellationToken);
        return currentEnabledStrategyIds.Contains(StrategyIds.Normalize(strategyId));
    }

    public async Task<StrategyRuntimeSettings> GetStrategySettingsAsync(Guid strategyId, CancellationToken cancellationToken = default)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        var settings = await GetStrategySettingsAsync(cancellationToken);
        return settings.TryGetValue(normalizedStrategyId, out var value)
            ? value
            : StrategyRuntimeSettings.Default(normalizedStrategyId);
    }

    private async Task EnsureRefreshedAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (strategySettings is not null && enabledStrategyIds is not null && now - refreshedAtUtc < RefreshInterval)
        {
            return;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (strategySettings is not null && enabledStrategyIds is not null && now - refreshedAtUtc < RefreshInterval)
            {
                return;
            }

            try
            {
                var settings = await repository.GetStrategyRuntimeSettingsAsync(cancellationToken);
                strategySettings = settings.ToDictionary(
                    item => StrategyIds.Normalize(item.Key),
                    item => item.Value with { StrategyId = StrategyIds.Normalize(item.Key) });
                enabledStrategyIds = strategySettings
                    .Where(item => item.Value.Enabled)
                    .Select(item => StrategyIds.Normalize(item.Key))
                    .ToHashSet();
                refreshedAtUtc = now;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to refresh strategy runtime settings.");
                strategySettings ??= StrategyIds.AllStrategyIds.ToDictionary(StrategyIds.Normalize, StrategyRuntimeSettings.Default);
                enabledStrategyIds ??= new HashSet<Guid>();
                refreshedAtUtc = now;
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }
}
