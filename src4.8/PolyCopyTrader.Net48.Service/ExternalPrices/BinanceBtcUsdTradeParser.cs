using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.ExternalPrices;

public static class BinanceBtcUsdTradeParser
{
    public const string SourceName = "BinanceTradeWebSocket";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public static bool TryParse(
        ReadOnlySpan<byte> utf8Json,
        DateTimeOffset fetchedAtUtc,
        out BtcUsdReferencePricePoint? point,
        out string? error)
    {
        point = null;
        error = null;

        BinanceTradeMessage? payload;
        try
        {
            payload = JsonSerializer.Deserialize<BinanceTradeMessage>(utf8Json, JsonOptions);
        }
        catch (JsonException ex)
        {
            error = "Invalid Binance trade JSON: " + ex.Message;
            return false;
        }

        if (payload is null)
        {
            error = "Binance trade JSON was empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.Price) ||
            !decimal.TryParse(payload.Price, NumberStyles.Number, CultureInfo.InvariantCulture, out var priceUsd) ||
            priceUsd <= 0m)
        {
            error = "Binance trade JSON did not include a positive price.";
            return false;
        }

        var sourceUpdatedAtUtc = payload.TradeTimeUnixMs is { } tradeTimeUnixMs
            ? DateTimeOffset.FromUnixTimeMilliseconds(tradeTimeUnixMs)
            : payload.EventTimeUnixMs is { } eventTimeUnixMs
                ? DateTimeOffset.FromUnixTimeMilliseconds(eventTimeUnixMs)
                : fetchedAtUtc;

        point = new BtcUsdReferencePricePoint(
            priceUsd,
            sourceUpdatedAtUtc,
            fetchedAtUtc,
            SourceName);
        return true;
    }

    private sealed class BinanceTradeMessage
    {
        [JsonPropertyName("p")]
        public string? Price { get; init; }

        [JsonPropertyName("T")]
        public long? TradeTimeUnixMs { get; init; }

        [JsonPropertyName("E")]
        public long? EventTimeUnixMs { get; init; }
    }
}
