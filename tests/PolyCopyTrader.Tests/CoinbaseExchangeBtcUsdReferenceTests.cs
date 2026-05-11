using System.Net;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.ExternalPrices;

namespace PolyCopyTrader.Tests;

public sealed class CoinbaseExchangeBtcUsdReferenceTests
{
    [Fact]
    public void Cache_KeepsLatestWindowAndComputesArithmeticMean()
    {
        var cache = new BtcUsdReferencePriceCache(new CoinbaseExchangeOptions { WindowSize = 3 });
        var start = DateTimeOffset.Parse("2026-05-07T11:00:00Z");

        for (var index = 1; index <= 5; index++)
        {
            cache.Add(new BtcUsdReferencePricePoint(
                index,
                start.AddMinutes(index),
                start.AddMinutes(index),
                "CoinbaseExchange"));
        }

        var snapshot = cache.Snapshot;

        Assert.Equal(3, snapshot.WindowSize);
        Assert.Equal(3, snapshot.SampleCount);
        Assert.True(snapshot.IsFullWindow);
        Assert.Equal(4m, snapshot.ArithmeticMeanUsd);
        Assert.Equal(5m, snapshot.Latest?.PriceUsd);
        Assert.Equal([5m, 4m, 3m], snapshot.Samples.Select(sample => sample.PriceUsd));
    }

    [Fact]
    public void Cache_UsesConfiguredBinanceSourceName()
    {
        var cache = new BtcUsdReferencePriceCache(new BinanceBtcUsdReferenceOptions { WindowSize = 3 });

        cache.Add(new BtcUsdReferencePricePoint(
            80_000m,
            DateTimeOffset.Parse("2026-05-07T11:00:00Z"),
            DateTimeOffset.Parse("2026-05-07T11:00:00Z"),
            BinanceBtcUsdTradeParser.SourceName));

        Assert.Equal(BinanceBtcUsdTradeParser.SourceName, cache.Snapshot.Source);
    }

    [Fact]
    public void BinanceTradeParser_ReadsTradePriceAndTradeTimestamp()
    {
        var fetchedAtUtc = DateTimeOffset.Parse("2026-05-07T11:25:42Z");
        var json = """
            {"e":"trade","E":1778153142000,"s":"BTCUSDT","t":123,"p":"80943.89000000","q":"0.01","T":1778153142123,"m":false,"M":true}
            """u8;

        var parsed = BinanceBtcUsdTradeParser.TryParse(
            json,
            fetchedAtUtc,
            out var point,
            out var error);

        Assert.True(parsed, error);
        Assert.NotNull(point);
        Assert.Equal(80943.89000000m, point.PriceUsd);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1778153142123), point.SourceUpdatedAtUtc);
        Assert.Equal(fetchedAtUtc, point.FetchedAtUtc);
        Assert.Equal(BinanceBtcUsdTradeParser.SourceName, point.Source);
    }

    [Fact]
    public async Task Client_ReadsBtcUsdTickerAndSendsHeaders()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"trade_id":123,"price":"80943.89046462867","size":"0.01","time":"2026-05-07T11:25:42.123456Z","bid":"80943.89","ask":"80943.90","volume":"1234"}""")
        });
        var client = new CoinbaseExchangeBtcUsdClient(
            new HttpClient(handler),
            new CoinbaseExchangeOptions());

        var point = await client.GetBtcUsdPriceAsync();

        Assert.Equal(80943.89046462867m, point.PriceUsd);
        Assert.Equal(DateTimeOffset.Parse("2026-05-07T11:25:42.123456Z"), point.SourceUpdatedAtUtc);
        Assert.Equal("CoinbaseExchange", point.Source);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/products/BTC-USD/ticker", request.RequestUri?.AbsolutePath);
        Assert.Equal(string.Empty, request.RequestUri?.Query);
        Assert.True(request.Headers.TryGetValues("User-Agent", out var userAgentValues));
        Assert.Equal("PolyCopyTrader/1.0 BTC-USD-reference", string.Join(" ", userAgentValues));
        Assert.True(request.Headers.TryGetValues("Accept", out var acceptValues));
        Assert.Equal("application/json", Assert.Single(acceptValues));
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }
}
