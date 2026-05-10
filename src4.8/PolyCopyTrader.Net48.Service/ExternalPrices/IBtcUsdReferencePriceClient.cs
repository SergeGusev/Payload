using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.ExternalPrices;

public interface IBtcUsdReferencePriceClient
{
    Task<BtcUsdReferencePricePoint> GetBtcUsdPriceAsync(CancellationToken cancellationToken = default);
}
