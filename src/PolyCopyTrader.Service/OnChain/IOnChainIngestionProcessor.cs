using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.OnChain;

public interface IOnChainIngestionProcessor
{
    Task<OnChainIngestionResult> RefreshLookbackAsync(CancellationToken cancellationToken = default);

    Task<OnChainIngestionResult> RefreshBackgroundCycleAsync(CancellationToken cancellationToken = default);

    bool RequestCancel();
}
