using System.Text;
using System.Text.Json;
using System.IO;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class ClobV2OrderPayloadSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public string Serialize(ClobV2Order order, string? signature, string owner = "[DRY_RUN_API_KEY]")
    {
        return Serialize(order, signature, owner, redactSignature: false);
    }

    public string SerializeRedacted(ClobV2Order order, string? signature, string owner = "[DRY_RUN_API_KEY]")
    {
        return Serialize(order, signature, owner, redactSignature: !string.IsNullOrWhiteSpace(signature));
    }

    private static string Serialize(ClobV2Order order, string? signature, string owner, bool redactSignature)
    {
        Guard.NotNull(order, nameof(order));
        Guard.NotNullOrWhiteSpace(owner, nameof(owner));

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("order");
            writer.WriteStartObject();
            writer.WritePropertyName("salt");
            writer.WriteRawValue(order.Salt);
            writer.WriteString("maker", order.Maker);
            writer.WriteString("signer", order.Signer);
            writer.WriteString("tokenId", order.TokenId);
            writer.WriteString("makerAmount", order.MakerAmount);
            writer.WriteString("takerAmount", order.TakerAmount);
            writer.WriteString("side", ClobV2OrderBuilder.SideToWire(order.Side));
            writer.WriteString("expiration", order.Expiration);
            writer.WriteNumber("signatureType", (int)order.SignatureType);
            writer.WriteString("timestamp", order.Timestamp);
            writer.WriteString("metadata", order.Metadata);
            writer.WriteString("builder", order.Builder);
            writer.WriteString("signature", redactSignature ? "[REDACTED]" : signature ?? string.Empty);
            writer.WriteEndObject();
            writer.WriteString("owner", owner);
            writer.WriteString("orderType", order.OrderType.ToString());
            writer.WriteBoolean("deferExec", order.DeferExec);
            writer.WriteBoolean("postOnly", order.PostOnly);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
