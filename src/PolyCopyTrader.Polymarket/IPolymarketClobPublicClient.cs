using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public interface IPolymarketClobPublicClient
{
    Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default);

    Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default);

    Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default);

    Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default);
}
