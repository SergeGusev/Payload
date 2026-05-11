using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.OnChain;

public interface IOnChainTradeCaptureProcessor
{
    Task<OnChainTradeCaptureResult> CaptureOnceAsync(CancellationToken cancellationToken = default);
}
