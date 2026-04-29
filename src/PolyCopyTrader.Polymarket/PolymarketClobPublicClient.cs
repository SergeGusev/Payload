using System.Globalization;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket;

public sealed class PolymarketClobPublicClient : IPolymarketClobPublicClient
{
    private readonly PolymarketOptions options;
    private readonly PolymarketHttpClient client;

    public PolymarketClobPublicClient(
        HttpClient httpClient,
        PolymarketOptions options,
        IPolymarketApiErrorSink errorSink,
        IPolymarketHttpLogSink? httpLogSink = null)
    {
        this.options = options;
        httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        client = new PolymarketHttpClient(httpClient, options, errorSink, "PolymarketClobPublicClient", httpLogSink);
    }

    public async Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.ClobBaseUrl,
                "/book",
                new Dictionary<string, string?> { ["token_id"] = assetId }),
            "GetOrderBook",
            cancellationToken);

        return PolymarketJsonParser.ParseOrderBook(json.RootElement);
    }

    public async Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
    {
        var value = await client.GetStringAsync(
            UriBuilderExtensions.WithPathAndQuery(options.ClobBaseUrl, "/time", new Dictionary<string, string?>()),
            "GetServerTime",
            cancellationToken);

        return long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : DateTimeOffset.UtcNow;
    }

    public async Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.ClobBaseUrl,
                "/midpoint",
                new Dictionary<string, string?> { ["token_id"] = assetId }),
            "GetMidpoint",
            cancellationToken);

        return PolymarketJsonParser.ParseSingleDecimal(json.RootElement, "mid_price");
    }

    public async Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default)
    {
        using var json = await client.GetJsonDocumentAsync(
            UriBuilderExtensions.WithPathAndQuery(
                options.ClobBaseUrl,
                "/spread",
                new Dictionary<string, string?> { ["token_id"] = assetId }),
            "GetSpread",
            cancellationToken);

        return PolymarketJsonParser.ParseSingleDecimal(json.RootElement, "spread");
    }
}
