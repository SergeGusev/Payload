using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.ExternalPrices;

public interface IExpiryFuturesReferencePriceClient
{
    Task<IReadOnlyList<ExpiryFuturesReferencePricePoint>> GetNearestExpiryPricesAsync(
        string assetSymbol,
        DateTimeOffset targetMarketEndUtc,
        int requiredExpiryCount,
        CancellationToken cancellationToken = default);
}
