using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public interface IPaperTradingMarketDataUpdater
{
    Task ApplyUpdateAsync(
        MarketDataUpdate update,
        DateTimeOffset? receivedAtUtc = null,
        IReadOnlySet<Guid>? eligiblePaperOrderIds = null,
        CancellationToken cancellationToken = default);
}
