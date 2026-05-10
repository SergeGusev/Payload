using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.ExternalPrices;

public interface IBtcUsdReferencePriceCache
{
    void Add(BtcUsdReferencePricePoint point);

    BtcUsdReferencePriceSnapshot Snapshot { get; }
}
