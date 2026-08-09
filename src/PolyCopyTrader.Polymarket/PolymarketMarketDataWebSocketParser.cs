using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public static class PolymarketMarketDataWebSocketParser
{
    public static IReadOnlyList<MarketDataUpdate> ParseMarketMessage(string message)
    {
        return ParseMarketMessage(message, DateTimeOffset.UtcNow);
    }

    public static IReadOnlyList<MarketDataUpdate> ParseMarketMessage(
        string message,
        DateTimeOffset receivedAtUtc)
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
        var normalizedReceivedAtUtc = receivedAtUtc.ToUniversalTime();
        return json.RootElement.ValueKind switch
        {
            JsonValueKind.Array => ParseArray(json.RootElement, normalizedReceivedAtUtc),
            JsonValueKind.Object => ParseObject(json.RootElement, normalizedReceivedAtUtc),
            _ => []
        };
    }

    private static IReadOnlyList<MarketDataUpdate> ParseArray(
        JsonElement root,
        DateTimeOffset receivedAtUtc)
    {
        var updates = new List<MarketDataUpdate>();
        foreach (var item in root.EnumerateArray())
        {
            updates.AddRange(ParseObject(item, receivedAtUtc));
        }

        return updates;
    }

    private static IReadOnlyList<MarketDataUpdate> ParseObject(
        JsonElement root,
        DateTimeOffset receivedAtUtc)
    {
        if (!root.TryGetProperty("event_type", out var eventTypeProperty))
        {
            return [];
        }

        var rawEventType = eventTypeProperty.GetString() ?? string.Empty;
        return rawEventType switch
        {
            "book" => [ParseBook(root, rawEventType, receivedAtUtc)],
            "price_change" => ParsePriceChange(root, rawEventType, receivedAtUtc),
            "last_trade_price" => [ParseLastTradePrice(root, rawEventType, receivedAtUtc)],
            "best_bid_ask" => [ParseBestBidAsk(root, rawEventType, receivedAtUtc)],
            "tick_size_change" =>
                [ParseSimple(root, MarketDataEventType.TickSizeChange, rawEventType, receivedAtUtc)],
            "market_resolved" => ParseMarketResolved(root, rawEventType, receivedAtUtc),
            _ => [ParseSimple(root, MarketDataEventType.Unknown, rawEventType, receivedAtUtc)]
        };
    }

    private static MarketDataUpdate ParseBook(
        JsonElement root,
        string rawEventType,
        DateTimeOffset receivedAtUtc)
    {
        var timestamp = ParseTimestamp(GetString(root, "timestamp"), receivedAtUtc);
        var orderBook = PolymarketJsonParser.ParseOrderBook(root, receivedAtUtc) with
        {
            SnapshotAtUtc = timestamp.EffectiveTimestampUtc
        };
        var update = new MarketDataUpdate(
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
            timestamp.EffectiveTimestampUtc,
            RawJson: root.GetRawText());
        return AddEvidence(update, root, timestamp, GetSourceEventId(root));
    }

    private static IReadOnlyList<MarketDataUpdate> ParsePriceChange(
        JsonElement root,
        string rawEventType,
        DateTimeOffset receivedAtUtc)
    {
        var market = GetString(root, "market");
        var timestamp = ParseTimestamp(GetString(root, "timestamp"), receivedAtUtc);
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
            var orderBook = BuildTopOfBookSnapshot(
                assetId,
                market,
                bestBid,
                bestAsk,
                timestamp.EffectiveTimestampUtc);
            var update = new MarketDataUpdate(
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
                timestamp.EffectiveTimestampUtc,
                RawJson: change.GetRawText());
            updates.Add(AddEvidence(update, change, timestamp, GetSourceEventId(change)));
        }

        return updates;
    }

    private static MarketDataUpdate ParseLastTradePrice(
        JsonElement root,
        string rawEventType,
        DateTimeOffset receivedAtUtc)
    {
        var timestamp = ParseTimestamp(GetString(root, "timestamp"), receivedAtUtc);
        var transactionHash = GetString(root, "transaction_hash");
        var update = new MarketDataUpdate(
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
            timestamp.EffectiveTimestampUtc,
            transactionHash,
            root.GetRawText());
        return AddEvidence(update, root, timestamp, transactionHash ?? GetSourceEventId(root));
    }

    private static MarketDataUpdate ParseBestBidAsk(
        JsonElement root,
        string rawEventType,
        DateTimeOffset receivedAtUtc)
    {
        var assetId = GetString(root, "asset_id");
        var market = GetString(root, "market");
        var timestamp = ParseTimestamp(GetString(root, "timestamp"), receivedAtUtc);
        var bestBid = GetDecimalOrNull(root, "best_bid");
        var bestAsk = GetDecimalOrNull(root, "best_ask");
        var orderBook = BuildTopOfBookSnapshot(
            assetId,
            market,
            bestBid,
            bestAsk,
            timestamp.EffectiveTimestampUtc);
        var update = new MarketDataUpdate(
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
            timestamp.EffectiveTimestampUtc,
            RawJson: root.GetRawText());
        return AddEvidence(update, root, timestamp, GetSourceEventId(root));
    }

    private static IReadOnlyList<MarketDataUpdate> ParseMarketResolved(
        JsonElement root,
        string rawEventType,
        DateTimeOffset receivedAtUtc)
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
                    receivedAtUtc,
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
                receivedAtUtc,
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
        DateTimeOffset receivedAtUtc,
        string? assetId = null,
        bool marketResolved = false,
        string? winningAssetId = null,
        string? winningOutcome = null)
    {
        var timestamp = ParseTimestamp(GetString(root, "timestamp"), receivedAtUtc);
        var update = new MarketDataUpdate(
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
            timestamp.EffectiveTimestampUtc,
            RawJson: root.GetRawText(),
            WinningAssetId: winningAssetId,
            WinningOutcome: winningOutcome);
        return AddEvidence(update, root, timestamp, GetSourceEventId(root));
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

    private static MarketDataUpdate AddEvidence(
        MarketDataUpdate update,
        JsonElement identityElement,
        ParsedTimestamp timestamp,
        string? sourceEventId)
    {
        var normalizedSourceEventId = string.IsNullOrWhiteSpace(sourceEventId)
            ? null
            : sourceEventId.Trim();
        return update with
        {
            SourceTimestampUtc = timestamp.SourceTimestampUtc,
            TimestampQuality = timestamp.Quality,
            ReceivedAtUtc = timestamp.ReceivedAtUtc,
            SourceEventId = normalizedSourceEventId,
            EventFingerprint = BuildEventFingerprint(
                update,
                identityElement,
                timestamp,
                normalizedSourceEventId)
        };
    }

    private static string BuildEventFingerprint(
        MarketDataUpdate update,
        JsonElement identityElement,
        ParsedTimestamp timestamp,
        string? sourceEventId)
    {
        var sourceTimestamp = timestamp.SourceTimestampUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            ?? string.Empty;
        var canonicalPayload = JsonSerializer.Serialize(identityElement);
        var identity = string.Join(
            '\u001f',
            update.EventType.ToString(),
            NormalizeIdentityPart(update.RawEventType),
            NormalizeIdentityPart(update.AssetId),
            NormalizeIdentityPart(update.ConditionId),
            sourceTimestamp,
            timestamp.Quality.ToString(),
            NormalizeIdentityPart(sourceEventId),
            update.BestBid?.ToString("G29", CultureInfo.InvariantCulture) ?? string.Empty,
            update.BestAsk?.ToString("G29", CultureInfo.InvariantCulture) ?? string.Empty,
            update.Price?.ToString("G29", CultureInfo.InvariantCulture) ?? string.Empty,
            update.Size?.ToString("G29", CultureInfo.InvariantCulture) ?? string.Empty,
            update.Side.ToString(),
            update.MarketResolved.ToString(CultureInfo.InvariantCulture),
            NormalizeIdentityPart(update.WinningAssetId),
            NormalizeIdentityPart(update.WinningOutcome),
            canonicalPayload);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static ParsedTimestamp ParseTimestamp(
        string? value,
        DateTimeOffset receivedAtUtc)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            try
            {
                var sourceTimestampUtc = unix > 99_999_999_999
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                    : DateTimeOffset.FromUnixTimeSeconds(unix);
                return new ParsedTimestamp(
                    sourceTimestampUtc,
                    sourceTimestampUtc,
                    MarketDataTimestampQuality.VenueProvided,
                    receivedAtUtc);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        return new ParsedTimestamp(
            receivedAtUtc,
            SourceTimestampUtc: null,
            MarketDataTimestampQuality.ReceiveTimeFallback,
            receivedAtUtc);
    }

    private static string? GetSourceEventId(JsonElement element)
    {
        return FirstNonEmpty(
            GetString(element, "transaction_hash"),
            GetString(element, "hash"),
            GetString(element, "id"));
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string NormalizeIdentityPart(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
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

    private readonly record struct ParsedTimestamp(
        DateTimeOffset EffectiveTimestampUtc,
        DateTimeOffset? SourceTimestampUtc,
        MarketDataTimestampQuality Quality,
        DateTimeOffset ReceivedAtUtc);
}
