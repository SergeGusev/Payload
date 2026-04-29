using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.MarketData;

public sealed class RelevantMarketAssetProvider(
    MarketDataWebSocketOptions options,
    IAppRepository repository) : IRelevantMarketAssetProvider
{
    public async Task<IReadOnlyCollection<string>> GetRelevantAssetIdsAsync(CancellationToken cancellationToken = default)
    {
        var assetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assetId in options.PinnedAssetIds)
        {
            AddIfUsable(assetIds, assetId);
        }

        foreach (var pinnedAsset in await repository.GetPinnedMarketAssetsAsync(cancellationToken))
        {
            AddIfUsable(assetIds, pinnedAsset.AssetId);
        }

        foreach (var order in await repository.GetOpenPaperOrdersAsync(cancellationToken))
        {
            AddIfUsable(assetIds, order.AssetId);
        }

        foreach (var position in await repository.GetPaperPositionsAsync(cancellationToken))
        {
            AddIfUsable(assetIds, position.AssetId);
        }

        var strongSignalCutoff = DateTimeOffset.UtcNow.AddMinutes(-options.StrongSignalLookbackMinutes);
        foreach (var signal in await repository.GetRecentSignalsAsync(200, cancellationToken))
        {
            if (signal.CreatedAtUtc < strongSignalCutoff)
            {
                continue;
            }

            if (signal.Accepted || signal.Score >= options.StrongSignalMinimumScore)
            {
                AddIfUsable(assetIds, signal.AssetId);
            }
        }

        return assetIds.Take(options.MaxSubscribedAssets).ToArray();
    }

    private static void AddIfUsable(HashSet<string> assetIds, string? assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return;
        }

        var trimmed = assetId.Trim();
        if (trimmed.Equals("0", StringComparison.Ordinal) ||
            trimmed.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        assetIds.Add(trimmed);
    }
}
