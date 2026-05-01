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

    public static PolymarketClobMarketByToken? ParseClobMarketByToken(JsonElement root)
    {
        if (root.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var conditionId = GetString(root, "condition_id") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(conditionId))
        {
            return null;
        }

        return new PolymarketClobMarketByToken(
            conditionId,
            GetString(root, "primary_token_id") ?? string.Empty,
            GetString(root, "secondary_token_id") ?? string.Empty);
    }

    public static IReadOnlyList<PolymarketOnChainTokenMetadata> ParseGammaMarketTokenMetadata(
        JsonElement root,
        string requestedTokenId)
    {
        if (root.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Gamma markets response must be a JSON array.");
        }

        var market = root.EnumerateArray().FirstOrDefault();
        if (market.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        var tokenIds = ParseStringArray(market, "clobTokenIds");
        if (!tokenIds.Contains(requestedTokenId, StringComparer.OrdinalIgnoreCase))
        {
            return [];
        }

        var outcomes = ParseStringArray(market, "outcomes");
        var outcomePrices = ParseDecimalArray(market, "outcomePrices");
        var winningOutcome = GetWinningOutcome(outcomes, outcomePrices, GetBool(market, "closed"));
        var category = GetGammaMarketCategory(market);
        var refreshedAt = DateTimeOffset.UtcNow;
        var rawJson = market.GetRawText();

        return tokenIds.Select((tokenId, index) => new PolymarketOnChainTokenMetadata(
            tokenId,
            GetString(market, "conditionId") ?? string.Empty,
            GetString(market, "id") ?? string.Empty,
            GetString(market, "slug") ?? string.Empty,
            GetString(market, "question") ?? string.Empty,
            index < outcomes.Count ? outcomes[index] : string.Empty,
            index,
            category,
            ParseDateTimeOffsetOrNull(GetString(market, "endDate")),
            GetBool(market, "active"),
            GetBool(market, "closed"),
            GetBool(market, "archived"),
            GetBool(market, "closed"),
            winningOutcome,
            tokenIds,
            outcomes,
            true,
            null,
            rawJson,
            refreshedAt)).ToArray();
    }

    private static string? GetGammaMarketCategory(JsonElement market)
    {
        return FirstNonEmpty(
            GetString(market, "category"),
            GetFirstNestedString(market, "events", "category"),
            GetFirstNestedString(market, "events", "subcategory"),
            GetFirstNestedArrayString(market, "events", "categories", "label", "name", "slug"),
            GetFirstNestedArrayString(market, "series", "categories", "label", "name", "slug"),
            GetFirstArrayString(market, "categories", "label", "name", "slug"),
            InferGammaCategory(market));
    }

    public static string? ParseGammaMarketEventId(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawJson);
        return ParseGammaMarketEventId(document.RootElement);
    }

    public static string? ParseGammaMarketEventId(JsonElement market)
    {
        return GetFirstNestedString(market, "events", "id");
    }

    public static string? ParseGammaEventCategory(JsonElement eventRoot)
    {
        if (eventRoot.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return FirstNonEmpty(
            GetString(eventRoot, "category"),
            GetString(eventRoot, "subcategory"),
            GetFirstArrayString(eventRoot, "categories", "label", "name", "slug"),
            InferGammaCategory(eventRoot));
    }

    private static string? InferGammaCategory(JsonElement root)
    {
        var signals = new List<string>();
        AddGammaCategorySignals(root, signals);

        if (signals.Count == 0)
        {
            return null;
        }

        var normalizedSignals = signals
            .Select(NormalizeCategorySignal)
            .Where(signal => signal.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedSignals.Length == 0)
        {
            return null;
        }

        if (AnyExactSignal(normalizedSignals, "sports"))
        {
            return "Sports";
        }

        if (AnyExactSignal(normalizedSignals, "crypto"))
        {
            return "Crypto";
        }

        if (AnyExactSignal(normalizedSignals, "finance"))
        {
            return "Finance";
        }

        if (AnyExactSignal(normalizedSignals, "politics", "elections", "us politics", "global politics"))
        {
            return "Politics";
        }

        if (AnyExactSignal(normalizedSignals, "climate and weather", "weather"))
        {
            return "Weather";
        }

        if (AnyExactSignal(normalizedSignals, "ai", "artificial intelligence"))
        {
            return "AI";
        }

        if (AnyExactSignal(normalizedSignals, "science"))
        {
            return "Science";
        }

        if (AnyExactSignal(normalizedSignals, "pop culture"))
        {
            return "Pop Culture";
        }

        var text = " " + string.Join(' ', normalizedSignals) + " ";
        if (ContainsAnyCategoryTerm(text, "bitcoin", "btc", "ethereum", "eth", "solana", "crypto", "usdc", "blockchain"))
        {
            return "Crypto";
        }

        if (ContainsAnyCategoryTerm(
            text,
            "election",
            "president",
            "trump",
            "biden",
            "senate",
            "congress",
            "government",
            "minister",
            "ukraine",
            "russia",
            "china",
            "taiwan",
            "israel",
            "iran",
            "lebanon",
            "gaza",
            "diplomacy",
            "ceasefire",
            "nato",
            "tariff",
            "war"))
        {
            return "Politics";
        }

        if (ContainsAnyCategoryTerm(
            text,
            "sports",
            "tennis",
            "wta",
            "atp",
            "nba",
            "nfl",
            "mlb",
            "nhl",
            "soccer",
            "football",
            "epl",
            "uefa",
            "fifa",
            "ufc",
            "boxing",
            "golf",
            "cricket",
            "formula 1",
            "f1",
            "madrid open"))
        {
            return "Sports";
        }

        if (ContainsAnyCategoryTerm(
            text,
            "fed",
            "fomc",
            "rate cut",
            "interest rate",
            "inflation",
            "economy",
            "economic",
            "finance",
            "treasury",
            "recession",
            "nasdaq",
            "stock",
            "oil"))
        {
            return "Finance";
        }

        if (ContainsAnyCategoryTerm(text, "openai", "chatgpt", "artificial intelligence", " ai "))
        {
            return "AI";
        }

        if (ContainsAnyCategoryTerm(text, "weather", "climate", "hurricane", "temperature", "rainfall", "snow"))
        {
            return "Weather";
        }

        if (ContainsAnyCategoryTerm(text, "space", "nasa", "science", "covid", "health", "disease"))
        {
            return "Science";
        }

        if (ContainsAnyCategoryTerm(
            text,
            "movie",
            "album",
            "music",
            "grammy",
            "oscars",
            "box office",
            "taylor swift",
            "gta",
            "video game"))
        {
            return "Pop Culture";
        }

        return null;
    }

    private static bool AnyExactSignal(IReadOnlyCollection<string> signals, params string[] values)
    {
        return values.Any(value => signals.Contains(value));
    }

    private static bool ContainsAnyCategoryTerm(string text, params string[] terms)
    {
        return terms.Any(term =>
        {
            var normalized = NormalizeCategorySignal(term);
            return normalized.Length > 0 && text.Contains(" " + normalized + " ", StringComparison.Ordinal);
        });
    }

    private static string NormalizeCategorySignal(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static void AddGammaCategorySignals(JsonElement element, List<string> signals)
    {
        AddPropertySignals(
            element,
            signals,
            "category",
            "subcategory",
            "slug",
            "ticker",
            "title",
            "question",
            "description",
            "groupItemTitle",
            "resolutionSource");

        AddArrayItemSignals(element, signals, "tags", "label", "name", "slug");
        AddArrayItemSignals(element, signals, "categories", "label", "name", "slug");
        AddArrayItemSignals(element, signals, "series", "title", "slug", "category", "subcategory");

        if (element.TryGetProperty("eventMetadata", out var eventMetadata) && eventMetadata.ValueKind == JsonValueKind.Object)
        {
            AddPropertySignals(eventMetadata, signals, "context_description");
        }

        if (!element.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var eventItem in events.EnumerateArray())
        {
            AddGammaCategorySignals(eventItem, signals);
        }
    }

    private static void AddPropertySignals(JsonElement element, List<string> signals, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = GetString(element, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                signals.Add(value);
            }
        }
    }

    private static void AddArrayItemSignals(
        JsonElement element,
        List<string> signals,
        string arrayPropertyName,
        params string[] propertyNames)
    {
        if (!element.TryGetProperty(arrayPropertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in array.EnumerateArray())
        {
            AddPropertySignals(item, signals, propertyNames);
        }
    }

    public static PolymarketOnChainTokenMetadata BuildMissingTokenMetadata(string tokenId, string reason)
    {
        return new PolymarketOnChainTokenMetadata(
            tokenId,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            -1,
            null,
            null,
            false,
            false,
            false,
            false,
            null,
            [],
            [],
            false,
            reason,
            "{}",
            DateTimeOffset.UtcNow);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? GetFirstNestedString(JsonElement element, string arrayPropertyName, string propertyName)
    {
        if (!element.TryGetProperty(arrayPropertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in array.EnumerateArray())
        {
            var value = GetString(item, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetFirstArrayString(JsonElement element, string arrayPropertyName, params string[] propertyNames)
    {
        if (!element.TryGetProperty(arrayPropertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return GetFirstArrayItemString(array, propertyNames);
    }

    private static string? GetFirstNestedArrayString(
        JsonElement element,
        string outerArrayPropertyName,
        string nestedArrayPropertyName,
        params string[] propertyNames)
    {
        if (!element.TryGetProperty(outerArrayPropertyName, out var outerArray) || outerArray.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var outerItem in outerArray.EnumerateArray())
        {
            var value = GetFirstArrayString(outerItem, nestedArrayPropertyName, propertyNames);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetFirstArrayItemString(JsonElement array, params string[] propertyNames)
    {
        foreach (var item in array.EnumerateArray())
        {
            foreach (var propertyName in propertyNames)
            {
                var value = GetString(item, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
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

    private static IReadOnlyList<string> ParseStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            return property.EnumerateArray().Select(item => item.ToString()).ToArray();
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
                ? json.RootElement.EnumerateArray().Select(item => item.ToString()).ToArray()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<decimal> ParseDecimalArray(JsonElement element, string propertyName)
    {
        return ParseStringArray(element, propertyName)
            .Select(value => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : (decimal?)null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
    }

    private static string? GetWinningOutcome(
        IReadOnlyList<string> outcomes,
        IReadOnlyList<decimal> outcomePrices,
        bool closed)
    {
        if (!closed || outcomes.Count == 0 || outcomePrices.Count != outcomes.Count)
        {
            return null;
        }

        var winners = outcomePrices
            .Select((price, index) => new { price, index })
            .Where(item => item.price >= 0.999m)
            .ToArray();

        return winners.Length == 1 ? outcomes[winners[0].index] : null;
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
