using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public interface IPaperTradingMarketDataUpdater
{
    Task ApplyUpdateAsync(
        MarketDataUpdate update,
        DateTimeOffset? receivedAtUtc = null,
        IReadOnlySet<Guid>? eligiblePaperOrderIds = null,
        CancellationToken cancellationToken = default);

    Task ApplyUpdateAsync(
        MarketDataUpdate update,
        DateTimeOffset? receivedAtUtc,
        IReadOnlySet<Guid>? eligiblePaperOrderIds,
        ConfirmedAssetSubscriptionSnapshot? confirmedAssetSubscription,
        CancellationToken cancellationToken) => ApplyUpdateAsync(
            update,
            receivedAtUtc,
            eligiblePaperOrderIds,
            cancellationToken);
}
