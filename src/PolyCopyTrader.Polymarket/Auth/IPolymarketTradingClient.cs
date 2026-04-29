namespace PolyCopyTrader.Polymarket.Auth;

public interface IPolymarketTradingClient
{
    Task<ClobV2DryRunOrderResult> PrepareDryRunOrderAsync(
        ClobV2OrderRequest request,
        CancellationToken ct);
}
