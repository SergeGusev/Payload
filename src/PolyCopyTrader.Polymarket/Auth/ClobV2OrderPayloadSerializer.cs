using System.Text.Json;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class ClobV2OrderPayloadSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public string Serialize(ClobV2Order order, string? signature)
    {
        return Serialize(order, signature, redactSignature: false);
    }

    public string SerializeRedacted(ClobV2Order order, string? signature)
    {
        return Serialize(order, signature, redactSignature: !string.IsNullOrWhiteSpace(signature));
    }

    private static string Serialize(ClobV2Order order, string? signature, bool redactSignature)
    {
        ArgumentNullException.ThrowIfNull(order);

        var value = new
        {
            order = new
            {
                salt = order.Salt,
                maker = order.Maker,
                signer = order.Signer,
                tokenId = order.TokenId,
                makerAmount = order.MakerAmount,
                takerAmount = order.TakerAmount,
                side = ClobV2OrderBuilder.SideToWire(order.Side),
                expiration = order.Expiration,
                signatureType = (int)order.SignatureType,
                timestamp = order.Timestamp,
                metadata = order.Metadata,
                builder = order.Builder,
                signature = redactSignature ? "[REDACTED]" : signature ?? string.Empty
            },
            owner = "[DRY_RUN_API_KEY]",
            orderType = order.OrderType.ToString(),
            deferExec = order.DeferExec,
            postOnly = order.PostOnly
        };

        return JsonSerializer.Serialize(value, JsonOptions);
    }
}
