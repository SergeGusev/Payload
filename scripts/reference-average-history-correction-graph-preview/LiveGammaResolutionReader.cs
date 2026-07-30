using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReferenceAverageHistoryCorrectionGraphPreview;

internal static class LiveGammaResolutionReader
{
    private const string BaseUrl = "https://gamma-api.polymarket.com/markets/";

    public static async Task<IReadOnlyDictionary<string, LiveGammaResolutionEvidence>> FetchAsync(
        IEnumerable<string> marketIds,
        CancellationToken cancellationToken)
    {
        var ids = marketIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
            "PolyCopyTraderReferenceHistoryPreview",
            "1.0"));
        using var concurrency = new SemaphoreSlim(4, 4);
        var tasks = ids.Select(async marketId =>
        {
            await concurrency.WaitAsync(cancellationToken);
            try
            {
                var url = BaseUrl + Uri.EscapeDataString(marketId);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Official Gamma request failed for market {marketId}: HTTP {(int)response.StatusCode}.");
                }
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                return Parse(marketId, url, bytes, DateTimeOffset.UtcNow);
            }
            finally
            {
                concurrency.Release();
            }
        }).ToArray();
        var rows = await Task.WhenAll(tasks);
        return rows.ToDictionary(item => item.MarketId, StringComparer.Ordinal);
    }

    internal static LiveGammaResolutionEvidence Parse(
        string requestedMarketId,
        string requestUrl,
        byte[] rawBytes,
        DateTimeOffset fetchedAtUtc)
    {
        using var document = JsonDocument.Parse(rawBytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Official Gamma market response is not an object.");
        }

        var marketId = ReadRequiredScalarString(root, "id");
        var conditionId = ReadRequiredString(root, "conditionId");
        var slug = ReadRequiredString(root, "slug");
        if (!string.Equals(marketId, requestedMarketId, StringComparison.Ordinal) ||
            !ReadRequiredBoolean(root, "closed"))
        {
            throw new InvalidDataException(
                $"Official Gamma response identity/closed state is invalid for market {requestedMarketId}.");
        }

        var outcomes = ReadStringArray(root, "outcomes");
        var tokenIds = ReadStringArray(root, "clobTokenIds");
        var outcomePrices = ReadDecimalArray(root, "outcomePrices");
        if (outcomes.Length != 2 || tokenIds.Length != 2 || outcomePrices.Length != 2 ||
            !outcomes.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["Up", "Down"]) ||
            tokenIds.Distinct(StringComparer.Ordinal).Count() != 2 ||
            outcomePrices.Count(price => price == 1m) != 1 ||
            outcomePrices.Count(price => price == 0m) != 1)
        {
            throw new InvalidDataException(
                $"Official Gamma response is not an exact binary 1/0 Up/Down resolution for market {requestedMarketId}.");
        }

        var winnerIndex = Array.FindIndex(outcomePrices, price => price == 1m);
        var orderMinSize = ReadOptionalDecimal(root, "orderMinSize");
        if (orderMinSize is <= 0m)
        {
            throw new InvalidDataException("Official Gamma orderMinSize is non-positive.");
        }

        var resolutionSource = ReadOptionalString(root, "resolutionSource");
        return new LiveGammaResolutionEvidence(
            marketId,
            conditionId,
            slug,
            true,
            JsonSerializer.Serialize(outcomes),
            JsonSerializer.Serialize(tokenIds),
            JsonSerializer.Serialize(outcomePrices),
            outcomes[winnerIndex],
            tokenIds[winnerIndex],
            orderMinSize,
            resolutionSource,
            requestUrl,
            Convert.ToHexString(SHA256.HashData(rawBytes)),
            rawBytes.LongLength,
            fetchedAtUtc.ToUniversalTime());
    }

    private static string[] ReadStringArray(JsonElement root, string property)
    {
        using var nested = ReadArrayDocument(root, property);
        var array = nested?.RootElement ?? root.GetProperty(property);
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Official Gamma {property} is not an array.");
        }
        return array.EnumerateArray().Select(item =>
            item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())
                ? item.GetString()!
                : throw new InvalidDataException($"Official Gamma {property} contains an invalid value.")).ToArray();
    }

    private static decimal[] ReadDecimalArray(JsonElement root, string property)
    {
        using var nested = ReadArrayDocument(root, property);
        var array = nested?.RootElement ?? root.GetProperty(property);
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Official Gamma {property} is not an array.");
        }
        return array.EnumerateArray().Select(item => ReadDecimal(item, property)).ToArray();
    }

    private static JsonDocument? ReadArrayDocument(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var node))
        {
            throw new InvalidDataException($"Official Gamma property {property} is missing.");
        }
        return node.ValueKind == JsonValueKind.String
            ? JsonDocument.Parse(node.GetString() ?? string.Empty)
            : null;
    }

    private static decimal ReadDecimal(JsonElement node, string property)
    {
        if (node.ValueKind == JsonValueKind.Number && node.TryGetDecimal(out var number))
        {
            return number;
        }
        if (node.ValueKind == JsonValueKind.String &&
            decimal.TryParse(node.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }
        throw new InvalidDataException($"Official Gamma {property} contains an invalid decimal.");
    }

    private static decimal? ReadOptionalDecimal(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) && node.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? ReadDecimal(node, property)
            : null;

    private static string ReadRequiredScalarString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var node))
        {
            throw new InvalidDataException($"Official Gamma property {property} is missing.");
        }
        return node.ValueKind switch
        {
            JsonValueKind.String when !string.IsNullOrWhiteSpace(node.GetString()) => node.GetString()!,
            JsonValueKind.Number => node.GetRawText(),
            _ => throw new InvalidDataException($"Official Gamma property {property} is invalid.")
        };
    }

    private static string ReadRequiredString(JsonElement root, string property) =>
        ReadOptionalString(root, property) ??
        throw new InvalidDataException($"Official Gamma property {property} is missing.");

    private static string? ReadOptionalString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(node.GetString())
            ? node.GetString()
            : null;

    private static bool ReadRequiredBoolean(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) && node.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? node.GetBoolean()
            : throw new InvalidDataException($"Official Gamma property {property} is invalid.");
}
