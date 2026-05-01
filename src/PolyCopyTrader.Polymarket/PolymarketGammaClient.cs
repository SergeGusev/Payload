using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket;

public sealed class PolymarketGammaClient : IPolymarketGammaClient
{
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
}
