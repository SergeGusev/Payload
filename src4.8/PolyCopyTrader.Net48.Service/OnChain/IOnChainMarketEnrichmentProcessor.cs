using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.OnChain;

public interface IOnChainMarketEnrichmentProcessor
{
    Task<OnChainMarketEnrichmentResult> RefreshAsync(CancellationToken cancellationToken = default);
}
