using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.OnChain;

public interface IOnChainSignalCandidateProcessor
{
    Task<OnChainSignalCandidateRefreshResult> RefreshAsync(CancellationToken cancellationToken = default);
}
