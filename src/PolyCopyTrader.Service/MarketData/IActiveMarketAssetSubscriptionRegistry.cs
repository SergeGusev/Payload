using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public sealed record ActiveMarketAssetRegistryUpdateResult(
    int Added,
    int Updated,
    int Removed,
    int TotalAssets)
{
    public int Changed => Added + Updated + Removed;
}

public interface IActiveMarketAssetSubscriptionRegistry
{
    ActiveMarketAssetRegistryUpdateResult AddOrUpdateMarkets(IReadOnlyCollection<PolymarketGammaMarket> markets);

    ActiveMarketAssetRegistryUpdateResult RetainAssets(IReadOnlyCollection<string> activeAssetIds);

    bool ApplyMarketDataUpdate(MarketDataUpdate update);

    IReadOnlyCollection<string> GetAssetIds();

    IReadOnlyCollection<ActiveMarketAssetSnapshot> GetSnapshots();

    bool TryGetSnapshot(string assetId, out ActiveMarketAssetSnapshot snapshot);

    Task WaitForChangeAsync(CancellationToken cancellationToken = default);
}
