using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket;

public sealed class PolymarketGeoClient : IPolymarketGeoClient
{
    private readonly PolymarketOptions options;
    private readonly PolymarketHttpClient client;

    public PolymarketGeoClient(
        HttpClient httpClient,
        PolymarketOptions options,
        IPolymarketApiErrorSink errorSink,
        IPolymarketHttpLogSink? httpLogSink = null)
    {
        this.options = options;
        httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        client = new PolymarketHttpClient(httpClient, options, errorSink, "PolymarketGeoClient", httpLogSink);
    }

    public async Task<GeoblockStatus> GetGeoblockStatusAsync(CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            new Uri(options.GeoblockUrl),
            "GetGeoblockStatus",
            cancellationToken);

        return PolymarketJsonParser.ParseGeoblock(json.RootElement);
    }
}
