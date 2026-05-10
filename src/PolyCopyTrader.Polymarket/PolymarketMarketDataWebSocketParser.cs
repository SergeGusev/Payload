using System.Globalization;
using System.Text.Json;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public static class PolymarketMarketDataWebSocketParser
{
    public static IReadOnlyList<MarketDataUpdate> ParseMarketMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return [];
        }

        var trimmed = message.Trim();
        if (trimmed.Equals("PING", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("PONG", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        using var json = JsonDocument.Parse(trimmed);
        return json.RootElement.ValueKind switch
        {
            JsonValueKind.Array => ParseArray(json.RootElement),
            JsonValueKind.Object => ParseObject(json.RootElement),
            _ => []
        };
    }

    private static IReadOnlyList<MarketDataUpdate> ParseArray(JsonElement root)
    {
        var updates = new List<MarketDataUpdate>();
        foreach (var item in root.EnumerateArray())
        {
            updates.AddRange(ParseObject(item));
        }

        return updates;
    }

    private static IReadOnlyList<MarketDataUpdate> ParseObject(JsonElement root)
    {
        if (!root.TryGetProperty("event_type", out var eventTypeProperty))
        {
            return [];
        }

        var rawEventType = eventTypeProperty.GetString() ?? string.Empty;
        return rawEventType switch
        {
            "book" => [ParseBook(root, rawEventType)],
            "price_change" => ParsePriceChange(root, rawEventType),
            "last_trade_price" => [ParseLastTradePrice(root, rawEventType)],
            "best_bid_ask" => [ParseBestBidAsk(root, rawEventType)],
            "tick_size_change" => [ParseSimple(root, MarketDataEventType.TickSizeChange, rawEventType)],
            "market_resolved" => ParseMarketResolved(root, rawEventType),
            _ => [ParseSimple(root, MarketDataEventType.Unknown, rawEventType)]
        };
    }

    private static MarketDataUpdate ParseBook(JsonElement root, string rawEventType)
    {
        var orderBook = PolymarketJsonParser.ParseOrderBook(root);
        return new MarketDataUpdate(
            MarketDataEventType.Book,
            rawEventType,
            orderBook.AssetId,
            orderBook.ConditionId,
            orderBook,
            orderBook.BestBid,
            orderBook.BestAsk,
            null,
            null,
            TradeSide.Unknown,
            false,
            orderBook.SnapshotAtUtc,
            RawJson: root.GetRawText());
    }

    private static IReadOnlyList<MarketDataUpdate> ParsePriceChange(JsonElement root, string rawEventType)
    {
        var market = GetString(root, "market");
        var timestamp = ParseTimestamp(GetString(root, "timestamp"));
        if (!root.TryGetProperty("price_changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var updates = new List<MarketDataUpdate>();
        foreach (var change in changes.EnumerateArray())
        {
            var assetId = GetString(change, "asset_id");
            var bestBid = GetDecimalOrNull(change, "best_bid");
            var bestAsk = GetDecimalOrNull(change, "best_ask");
            var orderBook = BuildTopOfBookSnapshot(assetId, market, bestBid, bestAsk, timestamp);
            updates.Add(new MarketDataUpdate(
                MarketDataEventType.PriceChange,
                rawEventType,
                assetId,
                market,
                orderBook,
                bestBid,
                bestAsk,
                GetDecimalOrNull(change, "price"),
                GetDecimalOrNull(change, "size"),
                ParseSide(GetString(change, "side")),
                false,
                timestamp,
                RawJson: change.GetRawText()));
        }

        return updates;
    }

    private static MarketDataUpdate ParseLastTradePrice(JsonElement root, string rawEventType)
    {
        return new MarketDataUpdate(
            MarketDataEventType.LastTradePrice,
            rawEventType,
            GetString(root, "asset_id"),
            GetString(root, "market"),
            null,
            null,
            null,
            GetDecimalOrNull(root, "price"),
            GetDecimalOrNull(root, "size"),
            ParseSide(GetString(root, "side")),
            false,
            ParseTimestamp(GetString(root, "timestamp")),
            GetString(root, "transaction_hash"),
            root.GetRawText());
    }

    private static MarketDataUpdate ParseBestBidAsk(JsonElement root, string rawEventType)
    {
        var assetId = GetString(root, "asset_id");
        var market = GetString(root, "market");
        var timestamp = ParseTimestamp(GetString(root, "timestamp"));
        var bestBid = GetDecimalOrNull(root, "best_bid");
        var bestAsk = GetDecimalOrNull(root, "best_ask");
        var orderBook = BuildTopOfBookSnapshot(assetId, market, bestBid, bestAsk, timestamp);
        return new MarketDataUpdate(
            MarketDataEventType.BestBidAsk,
            rawEventType,
            assetId,
            market,
            orderBook,
            bestBid,
            bestAsk,
            null,
            null,
            TradeSide.Unknown,
            false,
            timestamp,
            RawJson: root.GetRawText());
    }

    private static IReadOnlyList<MarketDataUpdate> ParseMarketResolved(JsonElement root, string rawEventType)
    {
        var assetIds = GetStringArray(root, "assets_ids");
        var winningAssetId = GetString(root, "winning_asset_id");
        var winningOutcome = GetString(root, "winning_outcome");
        if (assetIds.Count == 0)
        {
            var assetId = GetString(root, "asset_id");
            if (!string.IsNullOrWhiteSpace(assetId))
            {
                assetIds = [assetId];
            }
        }

        if (assetIds.Count == 0)
        {
            return
            [
                ParseSimple(
                    root,
                    MarketDataEventType.MarketResolved,
                    rawEventType,
                    marketResolved: true,
                    winningAssetId: winningAssetId,
                    winningOutcome: winningOutcome)
            ];
        }

        return assetIds
            .Select(assetId => ParseSimple(
                root,
                MarketDataEventType.MarketResolved,
                rawEventType,
                assetId,
                marketResolved: true,
                winningAssetId: winningAssetId,
                winningOutcome: winningOutcome))
            .ToArray();
    }

    private static MarketDataUpdate ParseSimple(
        JsonElement root,
        MarketDataEventType eventType,
        string rawEventType,
        string? assetId = null,
        bool marketResolved = false,
        string? winningAssetId = null,
        string? winningOutcome = null)
    {
        return new MarketDataUpdate(
            eventType,
            rawEventType,
            assetId ?? GetString(root, "asset_id"),
            GetString(root, "market"),
            null,
            null,
            null,
            null,
            null,
            TradeSide.Unknown,
            marketResolved,
            ParseTimestamp(GetString(root, "timestamp")),
            RawJson: root.GetRawText(),
            WinningAssetId: winningAssetId,
            WinningOutcome: winningOutcome);
    }

    private static OrderBookSnapshot? BuildTopOfBookSnapshot(
        string? assetId,
        string? market,
        decimal? bestBid,
        decimal? bestAsk,
        DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(assetId) || (bestBid is null && bestAsk is null))
        {
            return null;
        }

        return new OrderBookSnapshot(
            assetId,
            bestBid is { } bid ? [new OrderBookLevel(bid, 0m)] : [],
            bestAsk is { } ask ? [new OrderBookLevel(ask, 0m)] : [],
            timestamp,
            market);
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            return unix > 99_999_999_999
                ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                : DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        return DateTimeOffset.UtcNow;
    }

    private static TradeSide ParseSide(string? value)
    {
        return string.Equals(value, "BUY", StringComparison.OrdinalIgnoreCase)
            ? TradeSide.Buy
            : string.Equals(value, "SELL", StringComparison.OrdinalIgnoreCase)
                ? TradeSide.Sell
                : TradeSide.Unknown;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            return property
                .EnumerateArray()
                .Select(item => item.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return [];
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            using var json = JsonDocument.Parse(value);
            return json.RootElement.ValueKind == JsonValueKind.Array
                ? json.RootElement
                    .EnumerateArray()
                    .Select(item => item.ToString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static decimal? GetDecimalOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.TryParse(property.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
