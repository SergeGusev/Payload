using System.Globalization;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class DisabledPolymarketTradingClient : IPolymarketTradingClient
{
    public Task<ClobV2DryRunOrderResult> PrepareDryRunOrderAsync(ClobV2OrderRequest request, CancellationToken ct)
    {
        var order = new ClobV2Order(
            request.Salt ?? "0",
            request.MakerAddress,
            request.SignerAddress,
            request.TokenId,
            "0",
            "0",
            request.Side,
            request.SignatureType,
            request.CreatedAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            request.Metadata ?? string.Empty,
            request.Builder ?? string.Empty,
            request.GtdExpirationUtc?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "0",
            request.OrderType,
            request.PostOnly,
            request.DeferExec,
            request.NegativeRisk);

        return Task.FromResult(new ClobV2DryRunOrderResult(
            DryRunOrderStatus.DryRunRejected,
            order,
            null,
            "{}",
            "{}",
            new[] { "Net48 trading client is disabled in this Paper/ReadOnly port." }));
    }

    public Task<LiveOrderPlacementResult> PlaceLiveOrderAsync(ClobV2OrderRequest request, CancellationToken ct)
    {
        return Task.FromResult(new LiveOrderPlacementResult(
            false,
            null,
            "disabled",
            "Net48 live order placement is disabled.",
            null,
            null,
            "{}",
            "{}"));
    }

    public Task<LiveOrderCancellationResult> CancelOrderAsync(string orderId, CancellationToken ct)
    {
        return Task.FromResult(new LiveOrderCancellationResult(
            false,
            Array.Empty<string>(),
            new Dictionary<string, string> { [orderId] = "Net48 trading client is disabled." },
            "{}",
            "Net48 trading client is disabled."));
    }

    public Task<LiveOrderCancellationResult> CancelAllOrdersAsync(CancellationToken ct)
    {
        return Task.FromResult(new LiveOrderCancellationResult(
            false,
            Array.Empty<string>(),
            new Dictionary<string, string>(),
            "{}",
            "Net48 trading client is disabled."));
    }

    public Task<LiveOrderStatusResult?> GetLiveOrderStatusAsync(string orderId, CancellationToken ct)
    {
        return Task.FromResult<LiveOrderStatusResult?>(null);
    }
}
