using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public interface IPolymarketClobPublicClient
{
    Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default);

    Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default);

    Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default);

    Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default);

    Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(string tokenId, CancellationToken cancellationToken = default);

    Task<PolymarketClobMarketInfo> GetClobMarketInfoAsync(
        string conditionId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromException<PolymarketClobMarketInfo>(
            new NotSupportedException("This CLOB public-client implementation does not support market-info lookup."));
    }
}
