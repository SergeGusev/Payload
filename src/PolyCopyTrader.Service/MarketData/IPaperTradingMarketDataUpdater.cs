using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public interface IPaperTradingMarketDataUpdater
{
    Task ApplyUpdateAsync(MarketDataUpdate update, CancellationToken cancellationToken = default);
}
