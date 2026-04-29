using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.TraderDiscovery;

public interface ITraderDiscoveryProcessor
{
    Task<IReadOnlyList<TraderDiscoveryCandidate>> RefreshAsync(CancellationToken cancellationToken = default);
}
