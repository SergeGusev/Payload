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

    bool TryInvalidateAssetSubscription(
        ConfirmedAssetSubscriptionSnapshot expectedSubscription) => false;

    void RemoveAssetSubscriptionComponent(string component);

    bool ConfirmAssetSubscription(string component, string assetId);

    bool ConfirmAssetSubscription(
        string component,
        string assetId,
        DateTimeOffset confirmedAtUtc) => ConfirmAssetSubscription(component, assetId);

    bool ConfirmAssetSubscription(
        string component,
        string assetId,
        DateTimeOffset confirmedAtUtc,
        DateTimeOffset? sourceTimestampUtc,
        string? eventFingerprint) => ConfirmAssetSubscription(component, assetId, confirmedAtUtc);

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
    string SessionId,
    DateTimeOffset? ConfirmedAtUtc = null,
    DateTimeOffset? ConfirmationSourceTimestampUtc = null,
    string? ConfirmationEventFingerprint = null);

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
