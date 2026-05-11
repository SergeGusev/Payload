using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.OnChain;

public interface IOnChainPaperSignalProcessor
{
    Task<OnChainPaperSignalProcessingResult> ProcessOnceAsync(CancellationToken cancellationToken = default);

    Task<OnChainPaperSignalProcessingResult> ProcessCapturesAsync(
        IReadOnlyList<PolymarketOnChainTradeCapture> captures,
        CancellationToken cancellationToken = default);
}
