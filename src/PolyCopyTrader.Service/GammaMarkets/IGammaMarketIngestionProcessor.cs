using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.GammaMarkets;

public interface IGammaMarketIngestionProcessor
{
    Task<GammaMarketIngestionResult> RefreshAsync(CancellationToken cancellationToken = default);
}
