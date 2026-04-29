using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public interface IMarketDataCache
{
    IReadOnlyCollection<string> SubscribedAssetIds { get; }

    MarketDataStatusSnapshot Status { get; }

    void ReplaceSubscribedAssets(IReadOnlyCollection<string> assetIds);

    void ApplyUpdate(MarketDataUpdate update);

    bool TryGetFreshOrderBook(string assetId, TimeSpan maxAge, out OrderBookSnapshot snapshot);

    void UpdateStatus(MarketDataStatusSnapshot status);
}
