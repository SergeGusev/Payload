using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public interface IMarketDataCache
{
    IReadOnlyCollection<string> SubscribedAssetIds { get; }

    MarketDataStatusSnapshot Status { get; }

    void ReplaceSubscribedAssets(IReadOnlyCollection<string> assetIds);

    void ApplyUpdate(MarketDataUpdate update);

    OrderBookCacheLookup GetOrderBook(string assetId, TimeSpan maxAge);

    bool TryGetFreshOrderBook(string assetId, TimeSpan maxAge, out OrderBookSnapshot snapshot);

    void UpdateStatus(MarketDataStatusSnapshot status);
}

public enum OrderBookCacheLookupStatus
{
    Fresh,
    Missing,
    Stale
}

public sealed record OrderBookCacheLookup(
    OrderBookCacheLookupStatus Status,
    OrderBookSnapshot? Snapshot,
    TimeSpan? Age);
