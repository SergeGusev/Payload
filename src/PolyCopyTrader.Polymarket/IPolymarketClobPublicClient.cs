using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public interface IPolymarketClobPublicClient
{
    Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default);

    Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default);
}
