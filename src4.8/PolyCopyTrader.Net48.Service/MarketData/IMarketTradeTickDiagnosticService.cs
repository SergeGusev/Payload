using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public interface IMarketTradeTickDiagnosticService
{
    Task RecordAsync(MarketDataUpdate update, CancellationToken cancellationToken = default);
}
