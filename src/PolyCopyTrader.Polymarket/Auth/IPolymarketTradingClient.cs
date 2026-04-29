namespace PolyCopyTrader.Polymarket.Auth;

public interface IPolymarketTradingClient
{
    Task<ClobV2DryRunOrderResult> PrepareDryRunOrderAsync(
        ClobV2OrderRequest request,
        CancellationToken ct);

    Task<LiveOrderPlacementResult> PlaceLiveOrderAsync(
        ClobV2OrderRequest request,
        CancellationToken ct);

    Task<LiveOrderCancellationResult> CancelOrderAsync(
        string orderId,
        CancellationToken ct);

    Task<LiveOrderCancellationResult> CancelAllOrdersAsync(CancellationToken ct);

    Task<LiveOrderStatusResult?> GetLiveOrderStatusAsync(
        string orderId,
        CancellationToken ct);
}
