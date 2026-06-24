using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public static class MarketWebSocketFrameDiagnosticBuilder
{
    public const int MaxRawPayloadChars = 65_536;

    public static MarketWebSocketFrameDiagnostic Build(
        string component,
        string message,
        DateTimeOffset receivedAtUtc,
        IReadOnlyCollection<MarketDataUpdate>? parsedUpdates,
        bool parseSucceeded,
        string? parseError)
    {
        var payload = message ?? string.Empty;
        var trimmed = payload.Trim();
        var frameInfo = ExtractFrameInfo(trimmed);
        var rawPayload = payload.Length <= MaxRawPayloadChars
            ? payload
            : payload[..MaxRawPayloadChars];

        return new MarketWebSocketFrameDiagnostic(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(component) ? "UnknownMarketWebSocket" : component.Trim(),
            receivedAtUtc,
            frameInfo.FrameKind,
            payload.Length,
            ComputeSha256(payload),
            frameInfo.EventCount,
            SerializeJsonArray(frameInfo.EventTypes),
            SerializeJsonArray(frameInfo.AssetIds),
            SerializeJsonArray(frameInfo.MarketIds),
            payload.Contains("market_resolved", StringComparison.OrdinalIgnoreCase),
            payload.Contains("resolved", StringComparison.OrdinalIgnoreCase),
            parseSucceeded,
            parsedUpdates?.Count ?? 0,
            string.IsNullOrWhiteSpace(parseError) ? null : parseError,
            rawPayload,
            payload.Length > MaxRawPayloadChars,
            DateTimeOffset.UtcNow);
    }

    private static FrameInfo ExtractFrameInfo(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new FrameInfo("Empty", 0, [], [], []);
        }

        if (payload.Equals("PING", StringComparison.OrdinalIgnoreCase))
        {
            return new FrameInfo("Ping", 0, [], [], []);
        }

        if (payload.Equals("PONG", StringComparison.OrdinalIgnoreCase))
        {
            return new FrameInfo("Pong", 0, [], [], []);
        }

        try
        {
            using var json = JsonDocument.Parse(payload);
            var eventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var marketIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var eventCount = 0;

            foreach (var item in EnumerateEventObjects(json.RootElement))
            {
                if (TryGetString(item, "event_type", out var eventType))
                {
                    eventTypes.Add(eventType);
                    eventCount++;
                }

                AddAssetIds(item, assetIds);
                if (TryGetString(item, "market", out var marketId))
                {
                    marketIds.Add(marketId);
                }
            }

            return new FrameInfo(
                GetJsonFrameKind(json.RootElement),
                eventCount,
                ToSortedArray(eventTypes),
                ToSortedArray(assetIds),
                ToSortedArray(marketIds));
        }
        catch (JsonException)
        {
            return new FrameInfo("InvalidJson", 0, [], [], []);
        }
    }

    private static IEnumerable<JsonElement> EnumerateEventObjects(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            yield return root;
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                yield return item;
            }
        }
    }

    private static void AddAssetIds(JsonElement item, HashSet<string> assetIds)
    {
        if (TryGetString(item, "asset_id", out var assetId))
        {
            assetIds.Add(assetId);
        }

        foreach (var asset in GetStringArray(item, "assets_ids"))
        {
            assetIds.Add(asset);
        }

        if (!item.TryGetProperty("price_changes", out var priceChanges) ||
            priceChanges.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var priceChange in priceChanges.EnumerateArray())
        {
            if (priceChange.ValueKind == JsonValueKind.Object &&
                TryGetString(priceChange, "asset_id", out var nestedAssetId))
            {
                assetIds.Add(nestedAssetId);
            }
        }
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

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        value = property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetJsonFrameKind(JsonElement root)
    {
        return root.ValueKind switch
        {
            JsonValueKind.Object => "JsonObject",
            JsonValueKind.Array => "JsonArray",
            _ => "JsonScalar"
        };
    }

    private static string[] ToSortedArray(HashSet<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string SerializeJsonArray(IReadOnlyCollection<string> values)
    {
        return JsonSerializer.Serialize(values);
    }

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed record FrameInfo(
        string FrameKind,
        int EventCount,
        IReadOnlyCollection<string> EventTypes,
        IReadOnlyCollection<string> AssetIds,
        IReadOnlyCollection<string> MarketIds);
}
