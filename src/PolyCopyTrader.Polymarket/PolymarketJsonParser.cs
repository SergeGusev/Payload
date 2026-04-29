using System.Globalization;
using System.Text.Json;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public static class PolymarketJsonParser
{
    public static IReadOnlyList<TraderLeaderboardEntry> ParseLeaderboard(JsonElement root)
    {
        if (root.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Leaderboard response must be a JSON array.");
        }

        var entries = new List<TraderLeaderboardEntry>();
        foreach (var item in root.EnumerateArray())
        {
            entries.Add(new TraderLeaderboardEntry(
                GetIntOrNull(item, "rank"),
                GetString(item, "proxyWallet") ?? string.Empty,
                GetString(item, "userName") ?? string.Empty,
                GetDecimal(item, "vol"),
                GetDecimal(item, "pnl"),
                GetString(item, "profileImage"),
                GetString(item, "xUsername"),
                GetBool(item, "verifiedBadge")));
        }

        return entries;
    }

    public static IReadOnlyList<LeaderTrade> ParseTrades(JsonElement root)
    {
        if (root.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Trades response must be a JSON array.");
        }

        var trades = new List<LeaderTrade>();
        foreach (var item in root.EnumerateArray())
        {
            var price = GetDecimal(item, "price");
            var size = GetDecimal(item, "size");
            trades.Add(new LeaderTrade(
                GetString(item, "proxyWallet") ?? string.Empty,
                GetString(item, "name") ?? GetString(item, "pseudonym") ?? string.Empty,
                GetString(item, "conditionId") ?? string.Empty,
                GetString(item, "asset") ?? string.Empty,
                GetString(item, "slug") ?? string.Empty,
                GetString(item, "title") ?? string.Empty,
                GetString(item, "outcome") ?? string.Empty,
                ParseSide(GetString(item, "side")),
                price,
                size,
                price * size,
                FromUnixSeconds(GetLong(item, "timestamp")),
                GetString(item, "transactionHash")));
        }

        return trades;
    }

    public static IReadOnlyList<LeaderPosition> ParsePositions(JsonElement root)
    {
        if (root.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Positions response must be a JSON array.");
        }

        var positions = new List<LeaderPosition>();
        foreach (var item in root.EnumerateArray())
        {
            positions.Add(new LeaderPosition(
                GetString(item, "proxyWallet") ?? string.Empty,
                GetString(item, "conditionId") ?? string.Empty,
                GetString(item, "asset") ?? string.Empty,
                GetString(item, "outcome") ?? string.Empty,
                GetDecimal(item, "size"),
                GetDecimal(item, "avgPrice"),
                GetDecimal(item, "currentValue"),
                GetDecimal(item, "cashPnl"),
                GetDecimal(item, "curPrice"),
                DateTimeOffset.UtcNow,
                GetDecimal(item, "initialValue"),
                GetDecimal(item, "percentPnl"),
                GetDecimal(item, "totalBought"),
                GetDecimal(item, "realizedPnl"),
                GetString(item, "title"),
                GetString(item, "slug"),
                GetString(item, "oppositeAsset"),
                ParseDateTimeOffsetOrNull(GetString(item, "endDate")),
                GetBool(item, "negativeRisk")));
        }

        return positions;
    }

    public static OrderBookSnapshot ParseOrderBook(JsonElement root)
    {
        var assetId = GetString(root, "asset_id") ?? string.Empty;
        var timestamp = ParseOrderBookTimestamp(GetString(root, "timestamp"));
        return new OrderBookSnapshot(
            assetId,
            ParseLevels(root, "bids"),
            ParseLevels(root, "asks"),
            timestamp,
            GetString(root, "market"),
            GetDecimalOrNull(root, "min_order_size"),
            GetDecimalOrNull(root, "tick_size"),
            GetBool(root, "neg_risk"),
            GetDecimalOrNull(root, "last_trade_price"));
    }

    public static GeoblockStatus ParseGeoblock(JsonElement root)
    {
        return new GeoblockStatus(
            GetBool(root, "blocked"),
            GetString(root, "ip"),
            GetString(root, "country"),
            GetString(root, "region"));
    }

    public static decimal? ParseSingleDecimal(JsonElement root, string propertyName)
    {
        return GetDecimalOrNull(root, propertyName);
    }

    private static IReadOnlyList<OrderBookLevel> ParseLevels(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var levels) || levels.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<OrderBookLevel>();
        foreach (var level in levels.EnumerateArray())
        {
            result.Add(new OrderBookLevel(
                GetDecimal(level, "price"),
                GetDecimal(level, "size")));
        }

        return result;
    }

    private static TradeSide ParseSide(string? value)
    {
        return string.Equals(value, "BUY", StringComparison.OrdinalIgnoreCase)
            ? TradeSide.Buy
            : string.Equals(value, "SELL", StringComparison.OrdinalIgnoreCase)
                ? TradeSide.Sell
                : TradeSide.Unknown;
    }

    private static DateTimeOffset ParseOrderBookTimestamp(string? value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            return FromUnixMillisecondsOrSeconds(unix);
        }

        return DateTimeOffset.UtcNow;
    }

    private static DateTimeOffset FromUnixMillisecondsOrSeconds(long value)
    {
        return value > 99_999_999_999
            ? DateTimeOffset.FromUnixTimeMilliseconds(value)
            : DateTimeOffset.FromUnixTimeSeconds(value);
    }

    private static DateTimeOffset FromUnixSeconds(long value)
    {
        return value <= 0 ? DateTimeOffset.UnixEpoch : DateTimeOffset.FromUnixTimeSeconds(value);
    }

    private static DateTimeOffset? ParseDateTimeOffsetOrNull(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
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

    private static decimal GetDecimal(JsonElement element, string propertyName)
    {
        return GetDecimalOrNull(element, propertyName) ?? 0m;
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

    private static long GetLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0L;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
        {
            return number;
        }

        return long.TryParse(property.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0L;
    }

    private static int? GetIntOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(property.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(property.GetString(), out var parsed) && parsed,
            _ => false
        };
    }
}
