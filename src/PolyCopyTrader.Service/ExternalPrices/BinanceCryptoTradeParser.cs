using System.Globalization;
using System.Text.Json;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.ExternalPrices;

public static class BinanceCryptoTradeParser
{
    public const string SourceName = "BinanceCryptoTradeWebSocket";

    public static bool TryParse(
        ReadOnlySpan<byte> utf8Json,
        DateTimeOffset fetchedAtUtc,
        out CryptoReferencePricePoint? point,
        out string? error)
    {
        point = null;
        error = null;

        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray());
            var root = document.RootElement;
            var payload = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                ? data
                : root;

            var binanceSymbol = ReadString(payload, "s") ?? TryReadSymbolFromStream(root);
            if (string.IsNullOrWhiteSpace(binanceSymbol))
            {
                error = "Binance trade JSON did not include a symbol.";
                return false;
            }

            if (ReadString(payload, "p") is not { } priceText ||
                !decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var priceUsd) ||
                priceUsd <= 0m)
            {
                error = "Binance trade JSON did not include a positive price.";
                return false;
            }

            var sourceUpdatedAtUtc = ReadLong(payload, "T") is { } tradeTimeUnixMs
                ? DateTimeOffset.FromUnixTimeMilliseconds(tradeTimeUnixMs)
                : ReadLong(payload, "E") is { } eventTimeUnixMs
                    ? DateTimeOffset.FromUnixTimeMilliseconds(eventTimeUnixMs)
                    : fetchedAtUtc;

            var normalizedBinanceSymbol = binanceSymbol.Trim().ToUpperInvariant();
            point = new CryptoReferencePricePoint(
                ReadAssetSymbol(normalizedBinanceSymbol),
                normalizedBinanceSymbol,
                priceUsd,
                sourceUpdatedAtUtc,
                fetchedAtUtc,
                SourceName);
            return true;
        }
        catch (JsonException ex)
        {
            error = "Invalid Binance trade JSON: " + ex.Message;
            return false;
        }
    }

    private static string ReadAssetSymbol(string binanceSymbol)
    {
        const string quote = "USDT";
        return binanceSymbol.EndsWith(quote, StringComparison.OrdinalIgnoreCase)
            ? binanceSymbol[..^quote.Length]
            : binanceSymbol;
    }

    private static string? TryReadSymbolFromStream(JsonElement root)
    {
        var stream = ReadString(root, "stream");
        if (string.IsNullOrWhiteSpace(stream))
        {
            return null;
        }

        var separator = stream.IndexOf('@', StringComparison.Ordinal);
        return separator > 0 ? stream[..separator] : stream;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : null;
    }
}
