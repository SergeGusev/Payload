using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket;

public sealed class PolymarketDataApiClient : IPolymarketDataApiClient
{
    private readonly PolymarketOptions options;
    private readonly PolymarketHttpClient client;

    public PolymarketDataApiClient(
        HttpClient httpClient,
        PolymarketOptions options,
        IPolymarketApiErrorSink errorSink,
        IPolymarketHttpLogSink? httpLogSink = null)
    {
        this.options = options;
        httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        client = new PolymarketHttpClient(httpClient, options, errorSink, "PolymarketDataApiClient", httpLogSink);
    }

    public async Task<IReadOnlyList<TraderLeaderboardEntry>> GetTraderLeaderboardAsync(
        string category = "OVERALL",
        string timePeriod = "DAY",
        string orderBy = "PNL",
        int limit = 25,
        int offset = 0,
        string? user = null,
        CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.DataApiBaseUrl,
                "/v1/leaderboard",
                new Dictionary<string, string?>
                {
                    ["category"] = category,
                    ["timePeriod"] = timePeriod,
                    ["orderBy"] = orderBy,
                    ["limit"] = limit.ToString(),
                    ["offset"] = offset.ToString(),
                    ["user"] = user
                }),
            "GetTraderLeaderboard",
            cancellationToken);

        return PolymarketJsonParser.ParseLeaderboard(json.RootElement);
    }

    public async Task<IReadOnlyList<LeaderTrade>> GetUserTradesAsync(
        string wallet,
        bool takerOnly,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        return (await GetUserDataApiTradesAsync(wallet, takerOnly, limit, offset, cancellationToken: cancellationToken))
            .Select(trade => trade.ToLeaderTrade())
            .ToArray();
    }

    public async Task<IReadOnlyList<PolymarketDataApiTrade>> GetUserDataApiTradesAsync(
        string wallet,
        bool takerOnly,
        int limit = 100,
        int offset = 0,
        long? timestampCacheBuster = null,
        CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.DataApiBaseUrl,
                "/trades",
                new Dictionary<string, string?>
                {
                    ["user"] = wallet,
                    ["takerOnly"] = takerOnly ? "true" : "false",
                    ["limit"] = limit.ToString(),
                    ["offset"] = offset.ToString(),
                    ["timestamp"] = timestampCacheBuster?.ToString()
                }),
            "GetUserTrades",
            cancellationToken);

        return PolymarketJsonParser.ParseDataApiTrades(json.RootElement);
    }

    public async Task<IReadOnlyList<PolymarketDataApiTrade>> GetGlobalDataApiTradesAsync(
        bool takerOnly,
        int limit = 100,
        int offset = 0,
        long? timestampCacheBuster = null,
        CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.DataApiBaseUrl,
                "/trades",
                new Dictionary<string, string?>
                {
                    ["takerOnly"] = takerOnly ? "true" : "false",
                    ["limit"] = limit.ToString(),
                    ["offset"] = offset.ToString(),
                    ["timestamp"] = timestampCacheBuster?.ToString()
                }),
            "GetGlobalTrades",
            cancellationToken);

        return PolymarketJsonParser.ParseDataApiTrades(json.RootElement);
    }

    public async Task<IReadOnlyList<PolymarketDataApiActivity>> GetUserActivityAsync(
        string wallet,
        int limit = 500,
        int offset = 0,
        string sortBy = "TIMESTAMP",
        string sortDirection = "DESC",
        long? timestampCacheBuster = null,
        CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.DataApiBaseUrl,
                "/activity",
                new Dictionary<string, string?>
                {
                    ["user"] = wallet,
                    ["limit"] = limit.ToString(),
                    ["offset"] = offset.ToString(),
                    ["sortBy"] = sortBy,
                    ["sortDirection"] = sortDirection,
                    ["timestamp"] = timestampCacheBuster?.ToString()
                }),
            "GetUserActivity",
            cancellationToken);

        return PolymarketJsonParser.ParseDataApiActivity(json.RootElement);
    }

    public async Task<IReadOnlyList<LeaderTrade>> GetMarketTradesAsync(
        string conditionId,
        bool takerOnly,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.DataApiBaseUrl,
                "/trades",
                new Dictionary<string, string?>
                {
                    ["market"] = conditionId,
                    ["takerOnly"] = takerOnly ? "true" : "false",
                    ["limit"] = limit.ToString(),
                    ["offset"] = offset.ToString()
                }),
            "GetMarketTrades",
            cancellationToken);

        return PolymarketJsonParser.ParseTrades(json.RootElement);
    }

    public async Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
        string wallet,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.DataApiBaseUrl,
                "/positions",
                new Dictionary<string, string?>
                {
                    ["user"] = wallet,
                    ["limit"] = limit.ToString(),
                    ["offset"] = offset.ToString()
                }),
            "GetUserPositions",
            cancellationToken);

        return PolymarketJsonParser.ParsePositions(json.RootElement);
    }

    public async Task<IReadOnlyList<PolymarketDataApiPosition>> GetUserCurrentPositionsAsync(
        string wallet,
        int limit = 500,
        int offset = 0,
        string sortBy = "CURRENT",
        string sortDirection = "DESC",
        long? timestampCacheBuster = null,
        CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.DataApiBaseUrl,
                "/positions",
                new Dictionary<string, string?>
                {
                    ["user"] = wallet,
                    ["limit"] = limit.ToString(),
                    ["offset"] = offset.ToString(),
                    ["sortBy"] = sortBy,
                    ["sortDirection"] = sortDirection,
                    ["timestamp"] = timestampCacheBuster?.ToString()
                }),
            "GetUserCurrentPositions",
            cancellationToken);

        return PolymarketJsonParser.ParseDataApiCurrentPositions(json.RootElement);
    }

    public async Task<IReadOnlyList<PolymarketDataApiPosition>> GetUserClosedPositionsAsync(
        string wallet,
        int limit = 50,
        int offset = 0,
        string sortBy = "TIMESTAMP",
        string sortDirection = "DESC",
        long? timestampCacheBuster = null,
        CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.DataApiBaseUrl,
                "/closed-positions",
                new Dictionary<string, string?>
                {
                    ["user"] = wallet,
                    ["limit"] = limit.ToString(),
                    ["offset"] = offset.ToString(),
                    ["sortBy"] = sortBy,
                    ["sortDirection"] = sortDirection,
                    ["timestamp"] = timestampCacheBuster?.ToString()
                }),
            "GetUserClosedPositions",
            cancellationToken);

        return PolymarketJsonParser.ParseDataApiClosedPositions(json.RootElement);
    }
}
