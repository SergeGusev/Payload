using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.AutoRedeem;

public interface IPolymarketAutoRedeemProcessor
{
    Task<PolymarketAutoRedeemCycleResult> ProcessAsync(CancellationToken cancellationToken);
}
