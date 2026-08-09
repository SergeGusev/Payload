using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public interface IMarketDataCache
{
    IReadOnlyCollection<string> SubscribedAssetIds { get; }

    MarketDataStatusSnapshot Status { get; }

    long GetAssetSubscriptionGeneration(string assetId);

    ConfirmedAssetSubscriptionSnapshot GetConfirmedAssetSubscription(string assetId);

    void ReplaceSubscribedAssets(IReadOnlyCollection<string> assetIds);

    void AssignAssetSubscriptions(string component, IReadOnlyCollection<string> assetIds);

    void InvalidateAssetSubscriptions(string component);

    void RemoveAssetSubscriptionComponent(string component);

    bool ConfirmAssetSubscription(string component, string assetId);

    void ApplyUpdate(MarketDataUpdate update);

    OrderBookCacheLookup GetOrderBook(string assetId, TimeSpan maxAge);

    bool TryGetFreshOrderBook(string assetId, TimeSpan maxAge, out OrderBookSnapshot snapshot);

    void UpdateStatus(MarketDataStatusSnapshot status);
}

public sealed record ConfirmedAssetSubscriptionSnapshot(
    string AssetId,
    string? Component,
    bool ConfirmedLive,
    long Generation,
    string SessionId);

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
