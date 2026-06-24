using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using System.Globalization;
using System.Text.Json;

namespace PolyCopyTrader.Polymarket;

public sealed class PolymarketGammaClient : IPolymarketGammaClient
{
    private const int ClosedMarketsBySeriesPageLimit = 100;
    private const int ClosedMarketsBySeriesMaxPages = 500;
    private readonly PolymarketOptions options;
    private readonly PolymarketHttpClient client;

    public PolymarketGammaClient(
        HttpClient httpClient,
        PolymarketOptions options,
        IPolymarketApiErrorSink errorSink,
        IPolymarketHttpLogSink? httpLogSink = null)
    {
        this.options = options;
        httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        client = new PolymarketHttpClient(httpClient, options, errorSink, "PolymarketGammaClient", httpLogSink);
    }

    public async Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(
        int limit = 500,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.GammaBaseUrl,
                "/markets",
                new Dictionary<string, string?>
                {
                    ["active"] = "true",
                    ["closed"] = "false",
                    ["limit"] = limit.ToString(),
                    ["order"] = "createdAt",
                    ["ascending"] = "false",
                    ["offset"] = offset.ToString()
                }),
            "GetActiveMarkets",
            cancellationToken);

        return PolymarketJsonParser.ParseGammaActiveMarkets(json.RootElement);
    }

    public async Task<IReadOnlyList<PolymarketGammaMarket>> GetMarketsBySlugsAsync(
        IReadOnlyCollection<string> slugs,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slugs);

        var normalizedSlugs = slugs
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Select(slug => slug.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedSlugs.Length == 0)
        {
            return [];
        }

        var uri = BuildMarketsBySlugsUri(normalizedSlugs, activeOnly);
        using var json = await client.GetJsonDocumentAsync(new Uri(uri, UriKind.Absolute), "GetMarketsBySlugs", cancellationToken);
        return PolymarketJsonParser.ParseGammaActiveMarkets(json.RootElement);
    }

    public async Task<PolymarketGammaMarket?> GetClosedMarketBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = slug.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            return null;
        }

        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.GammaBaseUrl,
                "/markets",
                new Dictionary<string, string?>
                {
                    ["slug"] = normalizedSlug,
                    ["closed"] = "true",
                    ["limit"] = "1"
                }),
            "GetClosedMarketBySlug",
            cancellationToken);

        return PolymarketJsonParser.ParseGammaMarkets(json.RootElement)
            .FirstOrDefault(market => string.Equals(market.Slug, normalizedSlug, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<PolymarketGammaMarket>> GetClosedMarketsBySeriesSlugAsync(
        string seriesSlug,
        DateTimeOffset startTimeMinUtc,
        DateTimeOffset startTimeMaxUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedSeriesSlug = seriesSlug.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSeriesSlug) ||
            startTimeMaxUtc < startTimeMinUtc)
        {
            return [];
        }

        var markets = new List<PolymarketGammaMarket>();
        string? afterCursor = null;
        for (var page = 0; page < ClosedMarketsBySeriesMaxPages; page++)
        {
            var query = new Dictionary<string, string?>
            {
                ["series_slug"] = normalizedSeriesSlug,
                ["closed"] = "true",
                ["limit"] = ClosedMarketsBySeriesPageLimit.ToString(CultureInfo.InvariantCulture),
                ["order"] = "startTime",
                ["ascending"] = "true",
                ["start_time_min"] = FormatGammaUtc(startTimeMinUtc),
                ["start_time_max"] = FormatGammaUtc(startTimeMaxUtc)
            };
            if (!string.IsNullOrWhiteSpace(afterCursor))
            {
                query["after_cursor"] = afterCursor;
            }

            using var json = await client.GetJsonDocumentAsync(
                UriBuilderExtensions.WithPathAndQuery(
                    options.GammaBaseUrl,
                    "/events/keyset",
                    query),
                "GetClosedMarketsBySeriesSlug",
                cancellationToken);

            var pageMarkets = PolymarketJsonParser.ParseGammaEventMarkets(json.RootElement);
            if (pageMarkets.Count == 0)
            {
                break;
            }

            markets.AddRange(pageMarkets);
            afterCursor = TryGetNextCursor(json.RootElement);
            if (string.IsNullOrWhiteSpace(afterCursor))
            {
                break;
            }
        }

        return markets;
    }

    public async Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(
        string tokenId,
        bool closed,
        CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.GammaBaseUrl,
                "/markets",
                new Dictionary<string, string?>
                {
                    ["clob_token_ids"] = tokenId,
                    ["limit"] = "1",
                    ["closed"] = closed ? "true" : "false"
                }),
            closed ? "GetClosedMarketByToken" : "GetOpenMarketByToken",
            cancellationToken);

        return PolymarketJsonParser.ParseGammaMarketTokenMetadata(json.RootElement, tokenId);
    }

    public async Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(
        string conditionId,
        string requestedTokenId,
        bool closed,
        CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.GammaBaseUrl,
                "/markets",
                new Dictionary<string, string?>
                {
                    ["condition_ids"] = conditionId,
                    ["limit"] = "1",
                    ["closed"] = closed ? "true" : "false"
                }),
            closed ? "GetClosedMarketByCondition" : "GetOpenMarketByCondition",
            cancellationToken);

        return PolymarketJsonParser.ParseGammaMarketTokenMetadata(json.RootElement, requestedTokenId);
    }

    public async Task<string?> GetEventCategoryAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.GammaBaseUrl,
                "/events/" + Uri.EscapeDataString(eventId),
                new Dictionary<string, string?>()),
            "GetEvent",
            cancellationToken);

        return PolymarketJsonParser.ParseGammaEventCategory(json.RootElement);
    }

    private string BuildMarketsBySlugsUri(IReadOnlyList<string> slugs, bool activeOnly)
    {
        var normalizedBaseUrl = options.GammaBaseUrl.TrimEnd('/');
        var uri = normalizedBaseUrl +
            "/markets?limit=" +
            slugs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (activeOnly)
        {
            uri += "&active=true&closed=false";
        }

        foreach (var slug in slugs)
        {
            uri += "&slug=" + Uri.EscapeDataString(slug);
        }

        return uri;
    }

    private static string FormatGammaUtc(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string? TryGetNextCursor(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("next_cursor", out var nextCursor) ||
            nextCursor.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return nextCursor.GetString();
    }
}
