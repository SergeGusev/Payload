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
        IPolymarketApiErrorSink errorSink)
    {
        this.options = options;
        httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        client = new PolymarketHttpClient(httpClient, options, errorSink, "PolymarketDataApiClient");
    }

    public async Task<IReadOnlyList<TraderLeaderboardEntry>> GetTraderLeaderboardAsync(
        string category = "OVERALL",
        string timePeriod = "DAY",
        string orderBy = "PNL",
        int limit = 25,
        int offset = 0,
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
                    ["offset"] = offset.ToString()
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
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.DataApiBaseUrl,
                "/trades",
                new Dictionary<string, string?>
                {
                    ["user"] = wallet,
                    ["takerOnly"] = takerOnly ? "true" : "false",
                    ["limit"] = limit.ToString(),
                    ["offset"] = offset.ToString()
                }),
            "GetUserTrades",
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
}
