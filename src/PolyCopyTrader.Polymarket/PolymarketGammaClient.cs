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
}
