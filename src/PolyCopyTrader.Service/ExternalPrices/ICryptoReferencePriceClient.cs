using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.ExternalPrices;

public interface ICryptoReferencePriceClient
{
    Task<CryptoReferencePricePoint> GetPriceAsync(
        string assetSymbol,
        CancellationToken cancellationToken = default);
}
