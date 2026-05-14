using System.Net;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;

namespace PolyCopyTrader.Tests;

public sealed class PolymarketClientTests
{
    [Fact]
    public void Parser_ReadsTradesWithMakerFlagFields()
    {
        using var json = JsonDocument.Parse(SampleTradesJson);

        var trades = PolymarketJsonParser.ParseTrades(json.RootElement);

        var trade = Assert.Single(trades);
        Assert.Equal("0x56687bf447db6ffa42ffe2204a05edaa20f55839", trade.TraderWallet);
        Assert.Equal(TradeSide.Buy, trade.Side);
        Assert.Equal("12345678901234567890", trade.AssetId);
        Assert.Equal(0.74m, trade.Price);
        Assert.Equal(100m, trade.Size);
        Assert.Equal(74m, trade.CashValueUsd);
        Assert.Equal("0xabc", trade.TransactionHash);
    }

    [Fact]
    public void Parser_ReadsTypedDataApiTradeProfileFields()
    {
        using var json = JsonDocument.Parse(SampleTradesJson);

        var trades = PolymarketJsonParser.ParseDataApiTrades(json.RootElement);

        var trade = Assert.Single(trades);
        Assert.Equal("0x56687bf447db6ffa42ffe2204a05edaa20f55839", trade.TraderWallet);
        Assert.Equal("Gopfan", trade.TraderName);
        Assert.Equal("sample-event", trade.EventSlug);
        Assert.Equal("0xabc", trade.TransactionHash);
        Assert.Contains("proxyWallet", trade.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_ReadsPositions()
    {
        using var json = JsonDocument.Parse(SamplePositionsJson);

        var positions = PolymarketJsonParser.ParsePositions(json.RootElement);

        var position = Assert.Single(positions);
        Assert.Equal(100m, position.Size);
        Assert.Equal(0.74m, position.AvgPrice);
        Assert.Equal(0.81m, position.CurPrice);
        Assert.Equal("987654321", position.OppositeAsset);
        Assert.True(position.NegativeRisk);
    }

    [Fact]
    public void Parser_ReadsTypedCurrentPositions()
    {
        using var json = JsonDocument.Parse(SamplePositionsJson);

        var positions = PolymarketJsonParser.ParseDataApiCurrentPositions(json.RootElement);

        var position = Assert.Single(positions);
        Assert.Equal(PolymarketDataApiPositionStatus.Open, position.Status);
        Assert.Equal(74m, position.InitialValue);
        Assert.Equal(81m, position.CurrentValue);
        Assert.Equal(7m, position.CashPnl);
        Assert.Equal(1.25m, position.RealizedPnl);
        Assert.Equal("12345", position.EventId);
        Assert.Equal("Politics", position.Category);
        Assert.False(position.Redeemable);
        Assert.True(position.Mergeable);
        Assert.Contains("proxyWallet", position.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_ReadsTypedClosedPositions()
    {
        using var json = JsonDocument.Parse(SampleClosedPositionsJson);

        var positions = PolymarketJsonParser.ParseDataApiClosedPositions(json.RootElement);

        var position = Assert.Single(positions);
        Assert.Equal(PolymarketDataApiPositionStatus.Closed, position.Status);
        Assert.Null(position.Size);
        Assert.Equal(0.40m, position.AvgPrice);
        Assert.Equal(100m, position.TotalBought);
        Assert.Equal(8m, position.RealizedPnl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1710000300), position.TimestampUtc);
        Assert.Equal("Sports", position.Category);
        Assert.Equal("No", position.OppositeOutcome);
    }

    [Fact]
    public void Parser_ReadsOrderBookAndComputedSpread()
    {
        using var json = JsonDocument.Parse(SampleOrderBookJson);

        var orderBook = PolymarketJsonParser.ParseOrderBook(json.RootElement);

        Assert.Equal("12345678901234567890", orderBook.AssetId);
        Assert.Equal(0.45m, orderBook.BestBid);
        Assert.Equal(0.46m, orderBook.BestAsk);
        Assert.Equal(0.01m, orderBook.SpreadAbs);
        Assert.Equal(1m, orderBook.MinOrderSize);
        Assert.Equal(0.01m, orderBook.TickSize);
        Assert.Equal(0.45m, orderBook.LastTradePrice);
    }

    [Fact]
    public void Parser_ReadsLeaderboardAndGeoblock()
    {
        using var leaderboardJson = JsonDocument.Parse(SampleLeaderboardJson);
        using var geoblockJson = JsonDocument.Parse(SampleGeoblockJson);

        var leaderboard = PolymarketJsonParser.ParseLeaderboard(leaderboardJson.RootElement);
        var geo = PolymarketJsonParser.ParseGeoblock(geoblockJson.RootElement);

        var trader = Assert.Single(leaderboard);
        Assert.Equal(1, trader.Rank);
        Assert.Equal("Gopfan", trader.UserName);
        Assert.Equal(1234.5m, trader.Pnl);
        Assert.False(geo.Blocked);
        Assert.Equal("BG", geo.Country);
    }

    [Fact]
    public void Parser_ReadsGammaMarketTokenMetadata()
    {
        using var json = JsonDocument.Parse(SampleGammaMarketsJson);

        var metadata = PolymarketJsonParser.ParseGammaMarketTokenMetadata(
            json.RootElement,
            "12345678901234567890");

        Assert.Equal(2, metadata.Count);
        var yes = Assert.Single(metadata, item => item.TokenId == "12345678901234567890");
        Assert.Equal("Yes", yes.Outcome);
        Assert.Equal("0xcondition", yes.ConditionId);
        Assert.Equal("will-sample-event-happen", yes.MarketSlug);
        Assert.Equal("Politics", yes.Category);
        Assert.True(yes.Closed);
        Assert.True(yes.Resolved);
        Assert.Equal("Yes", yes.WinningOutcome);
        Assert.True(yes.LookupSucceeded);
    }

    [Fact]
    public void Parser_ReadsGammaActiveMarkets()
    {
        using var json = JsonDocument.Parse(SampleGammaActiveMarketsJson);

        var markets = PolymarketJsonParser.ParseGammaActiveMarkets(json.RootElement);

        var market = Assert.Single(markets);
        Assert.Equal("2002618", market.MarketId);
        Assert.Equal("0xcondition", market.ConditionId);
        Assert.Equal("question-1", market.QuestionId);
        Assert.Equal("israel-x-lebanon-diplomatic-meeting-by-may-31-2026", market.Slug);
        Assert.Equal("Israel x Lebanon diplomatic meeting by May 31, 2026?", market.Question);
        Assert.Equal("386820", market.EventId);
        Assert.Equal("israel-x-lebanon-diplomatic-meeting-by-341", market.EventSlug);
        Assert.Equal("Israel x Lebanon diplomatic meeting by...?", market.EventTitle);
        Assert.Equal("politics", market.SeriesSlug);
        Assert.Equal("Politics", market.Category);
        Assert.True(market.Active);
        Assert.False(market.Closed);
        Assert.True(market.AcceptingOrders);
        Assert.True(market.EnableOrderBook);
        Assert.True(market.NegativeRisk);
        Assert.Equal(123.45m, market.Liquidity);
        Assert.Equal(67.89m, market.LiquidityClob);
        Assert.Equal(1000m, market.Volume);
        Assert.Equal(10.5m, market.Volume24Hr);
        Assert.Equal(0.42m, market.BestBid);
        Assert.Equal(0.43m, market.BestAsk);
        Assert.Equal(0.01m, market.Spread);
        Assert.Equal(0.425m, market.LastTradePrice);
        Assert.Equal(5m, market.OrderMinSize);
        Assert.Equal(0.01m, market.OrderPriceMinTickSize);
        Assert.Equal(DateTimeOffset.Parse("2026-05-01T12:00:00Z"), market.CreatedAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-05-31T00:00:00Z"), market.EndDateUtc);
        Assert.Equal(["Yes", "No"], market.Outcomes);
        Assert.Equal(["12345678901234567890", "987654321"], market.ClobTokenIds);
        Assert.Contains("\"id\": \"2002618\"", market.RawJson, StringComparison.Ordinal);
        Assert.True(market.FetchedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void Parser_ReadsGammaMarketCategoryFromEventFallback()
    {
        using var json = JsonDocument.Parse(SampleGammaMarketsWithEventCategoryJson);

        var metadata = PolymarketJsonParser.ParseGammaMarketTokenMetadata(
            json.RootElement,
            "12345678901234567890");

        Assert.Equal(2, metadata.Count);
        Assert.All(metadata, item => Assert.Equal("Politics", item.Category));
    }

    [Theory]
    [InlineData(SampleGammaTennisEventJson, "Sports")]
    [InlineData(SampleGammaFinanceEventJson, "Finance")]
    [InlineData(SampleGammaPoliticsEventJson, "Politics")]
    public void Parser_InfersGammaEventCategoryFromTagsAndText(string jsonText, string expectedCategory)
    {
        using var json = JsonDocument.Parse(jsonText);

        var category = PolymarketJsonParser.ParseGammaEventCategory(json.RootElement);

        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public void Parser_ReadsGammaMarketEventId()
    {
        using var json = JsonDocument.Parse(SampleGammaMarketsWithoutCategoryJson);

        var eventId = PolymarketJsonParser.ParseGammaMarketEventId(json.RootElement[0]);

        Assert.Equal("386820", eventId);
    }

    [Fact]
    public async Task DataClient_SendsTakerOnlyFalseForUserTrades()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleTradesJson)
        });
        var client = new PolymarketDataApiClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink());

        var trades = await client.GetUserTradesAsync("0x56687bf447db6ffa42ffe2204a05edaa20f55839", takerOnly: false);

        Assert.Single(trades);
        Assert.Contains("takerOnly=false", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("user=0x56687bf447db6ffa42ffe2204a05edaa20f55839", handler.Requests.Single().RequestUri?.Query);
    }

    [Fact]
    public async Task DataClient_SendsMarketParameterForMarketTrades()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleTradesJson)
        });
        var client = new PolymarketDataApiClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink());

        var trades = await client.GetMarketTradesAsync("0xcondition", takerOnly: false, limit: 50, offset: 25);

        Assert.Single(trades);
        Assert.Contains("/trades", handler.Requests.Single().RequestUri?.AbsoluteUri);
        Assert.Contains("market=0xcondition", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("takerOnly=false", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("limit=50", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("offset=25", handler.Requests.Single().RequestUri?.Query);
    }

    [Fact]
    public async Task DataClient_SendsTimestampForGlobalDataApiTrades()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleTradesJson)
        });
        var client = new PolymarketDataApiClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink());

        var trades = await client.GetGlobalDataApiTradesAsync(takerOnly: false, limit: 1000, timestampCacheBuster: 123456789);

        Assert.Single(trades);
        Assert.Contains("/trades", handler.Requests.Single().RequestUri?.AbsoluteUri);
        Assert.Contains("takerOnly=false", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("limit=1000", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("timestamp=123456789", handler.Requests.Single().RequestUri?.Query);
    }

    [Fact]
    public async Task DataClient_SendsTimestampForPositionEndpoints()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(path.Contains("closed-positions", StringComparison.Ordinal)
                    ? SampleClosedPositionsJson
                    : SamplePositionsJson)
            };
        });
        var client = new PolymarketDataApiClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink());

        var current = await client.GetUserCurrentPositionsAsync(
            "0x56687bf447db6ffa42ffe2204a05edaa20f55839",
            limit: 500,
            offset: 25,
            timestampCacheBuster: 123);
        var closed = await client.GetUserClosedPositionsAsync(
            "0x56687bf447db6ffa42ffe2204a05edaa20f55839",
            limit: 50,
            offset: 100,
            timestampCacheBuster: 456);

        Assert.Single(current);
        Assert.Single(closed);
        Assert.Contains("/positions", handler.Requests[0].RequestUri?.AbsoluteUri);
        Assert.Contains("limit=500", handler.Requests[0].RequestUri?.Query);
        Assert.Contains("offset=25", handler.Requests[0].RequestUri?.Query);
        Assert.Contains("timestamp=123", handler.Requests[0].RequestUri?.Query);
        Assert.Contains("/closed-positions", handler.Requests[1].RequestUri?.AbsoluteUri);
        Assert.Contains("limit=50", handler.Requests[1].RequestUri?.Query);
        Assert.Contains("offset=100", handler.Requests[1].RequestUri?.Query);
        Assert.Contains("timestamp=456", handler.Requests[1].RequestUri?.Query);
    }

    [Fact]
    public async Task GammaClient_FetchesMarketByToken()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleGammaMarketsJson)
        });
        var client = new PolymarketGammaClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink());

        var metadata = await client.GetTokenMetadataAsync("12345678901234567890", closed: false);

        Assert.Equal(2, metadata.Count);
        Assert.Contains("/markets", handler.Requests.Single().RequestUri?.AbsoluteUri);
        Assert.Contains("clob_token_ids=12345678901234567890", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("closed=false", handler.Requests.Single().RequestUri?.Query);
    }

    [Fact]
    public async Task GammaClient_FetchesActiveMarketsPage()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleGammaActiveMarketsJson)
        });
        var client = new PolymarketGammaClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink());

        var markets = await client.GetActiveMarketsAsync(offset: 200);

        Assert.Single(markets);
        Assert.Contains("/markets", handler.Requests.Single().RequestUri?.AbsoluteUri);
        Assert.Contains("active=true", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("closed=false", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("limit=500", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("order=createdAt", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("ascending=false", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("offset=200", handler.Requests.Single().RequestUri?.Query);
    }

    [Fact]
    public async Task GammaClient_FetchesClosedMarketBySlug()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleGammaMarketsJson)
        });
        var client = new PolymarketGammaClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink());

        var market = await client.GetClosedMarketBySlugAsync("will-sample-event-happen");

        Assert.NotNull(market);
        Assert.Equal("will-sample-event-happen", market.Slug);
        Assert.True(market.Closed);
        Assert.Contains("/markets", handler.Requests.Single().RequestUri?.AbsoluteUri);
        Assert.Contains("slug=will-sample-event-happen", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("closed=true", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("limit=1", handler.Requests.Single().RequestUri?.Query);
    }

    [Fact]
    public async Task GammaClient_FetchesMarketByConditionId()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleGammaMarketsJson)
        });
        var client = new PolymarketGammaClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink());

        var metadata = await client.GetTokenMetadataByConditionIdAsync("0xcondition", "12345678901234567890", closed: false);

        Assert.Equal(2, metadata.Count);
        Assert.Contains("/markets", handler.Requests.Single().RequestUri?.AbsoluteUri);
        Assert.Contains("condition_ids=0xcondition", handler.Requests.Single().RequestUri?.Query);
        Assert.Contains("closed=false", handler.Requests.Single().RequestUri?.Query);
    }

    [Fact]
    public async Task GammaClient_FetchesEventCategory()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleGammaTennisEventJson)
        });
        var client = new PolymarketGammaClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink());

        var category = await client.GetEventCategoryAsync("406929");

        Assert.Equal("Sports", category);
        Assert.Contains("/events/406929", handler.Requests.Single().RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task ClobClient_ReadsOrderBookEndpoint()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleOrderBookJson)
        });
        var client = new PolymarketClobPublicClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink());

        var orderBook = await client.GetOrderBookAsync("12345678901234567890");

        Assert.NotNull(orderBook);
        Assert.Contains("/book", handler.Requests.Single().RequestUri?.AbsoluteUri);
        Assert.Contains("token_id=12345678901234567890", handler.Requests.Single().RequestUri?.Query);
    }

    [Fact]
    public async Task ClobClient_DoesNotRecordApiErrorForMissingOrderBook()
    {
        var sink = new CapturingApiErrorSink();
        var httpLogSink = new CapturingHttpLogSink();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error":"No orderbook exists for the requested token id"}""")
        });
        var client = new PolymarketClobPublicClient(
            new HttpClient(handler),
            TestOptions,
            sink,
            httpLogSink);

        await Assert.ThrowsAsync<PolymarketApiException>(() =>
            client.GetOrderBookAsync("12345678901234567890"));

        Assert.Empty(sink.Errors);
        var log = Assert.Single(httpLogSink.Entries);
        Assert.Equal("GetOrderBook", log.Operation);
        Assert.Equal(404, log.StatusCode);
        Assert.False(log.Succeeded);
    }

    [Fact]
    public async Task ClobClient_ReadsMarketByTokenEndpoint()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleClobMarketByTokenJson)
        });
        var client = new PolymarketClobPublicClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink());

        var market = await client.GetMarketByTokenAsync("12345678901234567890");

        Assert.NotNull(market);
        Assert.Equal("0xcondition", market.ConditionId);
        Assert.Contains("/markets-by-token/12345678901234567890", handler.Requests.Single().RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task Client_RecordsApiErrorOnHttpFailure()
    {
        var sink = new CapturingApiErrorSink();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("gateway failed")
        });
        var client = new PolymarketDataApiClient(new HttpClient(handler), TestOptions, sink);

        await Assert.ThrowsAsync<PolymarketApiException>(() =>
            client.GetUserPositionsAsync("0x56687bf447db6ffa42ffe2204a05edaa20f55839"));

        var error = Assert.Single(sink.Errors);
        Assert.Equal("PolymarketDataApiClient", error.Component);
        Assert.Equal("GetUserPositions", error.Operation);
    }

    [Fact]
    public async Task Client_RecordsPolymarketHttpLogOnSuccess()
    {
        var httpLogSink = new CapturingHttpLogSink();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleTradesJson)
        });
        var client = new PolymarketDataApiClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink(),
            httpLogSink);

        await client.GetUserTradesAsync("0x56687bf447db6ffa42ffe2204a05edaa20f55839", takerOnly: false);

        var entry = Assert.Single(httpLogSink.Entries);
        Assert.Equal("PolymarketDataApiClient", entry.Component);
        Assert.Equal("GetUserTrades", entry.Operation);
        Assert.Equal("GET", entry.HttpMethod);
        Assert.Contains("/trades", entry.RequestUrl, StringComparison.Ordinal);
        Assert.Contains("user=0x56687bf447db6ffa42ffe2204a05edaa20f55839", entry.RequestUrl, StringComparison.Ordinal);
        Assert.Contains("takerOnly=false", entry.RequestUrl, StringComparison.Ordinal);
        Assert.Contains("limit=100", entry.RequestUrl, StringComparison.Ordinal);
        Assert.Contains("offset=0", entry.RequestUrl, StringComparison.Ordinal);
        Assert.Equal(200, entry.StatusCode);
        Assert.True(entry.Succeeded);
        Assert.NotNull(entry.ResponseAtUtc);
        Assert.Contains("proxyWallet", entry.ResponseBody, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.Zero, entry.RequestedAtUtc.Offset);
    }

    [Fact]
    public async Task Client_RecordsPolymarketHttpLogOnFailure()
    {
        var httpLogSink = new CapturingHttpLogSink();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("gateway failed")
        });
        var client = new PolymarketDataApiClient(
            new HttpClient(handler),
            TestOptions,
            new CapturingApiErrorSink(),
            httpLogSink);

        await Assert.ThrowsAsync<PolymarketApiException>(() =>
            client.GetUserPositionsAsync("0x56687bf447db6ffa42ffe2204a05edaa20f55839"));

        var entry = Assert.Single(httpLogSink.Entries);
        Assert.Equal("GetUserPositions", entry.Operation);
        Assert.Equal(502, entry.StatusCode);
        Assert.False(entry.Succeeded);
        Assert.Contains("gateway failed", entry.ResponseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_RetriesHttp429ThenSucceeds()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("slow down") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleTradesJson) };
        });
        var client = new PolymarketDataApiClient(
            new HttpClient(handler),
            TestOptionsWithRetry,
            new CapturingApiErrorSink());

        var trades = await client.GetUserTradesAsync("0x56687bf447db6ffa42ffe2204a05edaa20f55839", takerOnly: false);

        Assert.Single(trades);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Client_RetriesHttp500ThenSucceeds()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("server failed") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SamplePositionsJson) };
        });
        var client = new PolymarketDataApiClient(
            new HttpClient(handler),
            TestOptionsWithRetry,
            new CapturingApiErrorSink());

        var positions = await client.GetUserPositionsAsync("0x56687bf447db6ffa42ffe2204a05edaa20f55839");

        Assert.Single(positions);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Client_RecordsApiErrorOnMalformedJson()
    {
        var sink = new CapturingApiErrorSink();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ not json")
        });
        var client = new PolymarketDataApiClient(new HttpClient(handler), TestOptions, sink);

        await Assert.ThrowsAsync<PolymarketApiException>(() =>
            client.GetUserTradesAsync("0x56687bf447db6ffa42ffe2204a05edaa20f55839", takerOnly: false));

        Assert.Single(sink.Errors);
    }

    private static PolymarketOptions TestOptions => new()
    {
        DataApiBaseUrl = "https://data-api.polymarket.com",
        ClobBaseUrl = "https://clob.polymarket.com",
        GeoblockUrl = "https://polymarket.com/api/geoblock",
        MaxRetries = 0,
        RetryBaseDelayMilliseconds = 0
    };

    private static PolymarketOptions TestOptionsWithRetry => new()
    {
        DataApiBaseUrl = "https://data-api.polymarket.com",
        ClobBaseUrl = "https://clob.polymarket.com",
        GeoblockUrl = "https://polymarket.com/api/geoblock",
        MaxRetries = 1,
        RetryBaseDelayMilliseconds = 0
    };

    private const string SampleTradesJson = """
[
  {
    "proxyWallet": "0x56687bf447db6ffa42ffe2204a05edaa20f55839",
    "side": "BUY",
    "asset": "12345678901234567890",
    "conditionId": "0xdd22472e552920b8438158ea7238bfadfa4f736aa4cee91a6b86c39ead110917",
    "size": 100,
    "price": 0.74,
    "timestamp": 1710000000,
    "title": "Will sample event happen?",
    "slug": "will-sample-event-happen",
    "eventSlug": "sample-event",
    "outcome": "Yes",
    "name": "Gopfan",
    "transactionHash": "0xabc"
  }
]
""";

    private const string SamplePositionsJson = """
[
  {
    "proxyWallet": "0x56687bf447db6ffa42ffe2204a05edaa20f55839",
    "asset": "12345678901234567890",
    "conditionId": "0xdd22472e552920b8438158ea7238bfadfa4f736aa4cee91a6b86c39ead110917",
    "size": 100,
    "avgPrice": 0.74,
    "initialValue": 74,
    "currentValue": 81,
    "cashPnl": 7,
    "percentPnl": 9.45,
    "totalBought": 100,
    "realizedPnl": 1.25,
    "percentRealizedPnl": 1.69,
    "curPrice": 0.81,
    "redeemable": false,
    "mergeable": true,
    "title": "Will sample event happen?",
    "slug": "will-sample-event-happen",
    "eventId": "12345",
    "eventSlug": "sample-event",
    "category": "Politics",
    "outcome": "Yes",
    "outcomeIndex": 0,
    "oppositeOutcome": "No",
    "oppositeAsset": "987654321",
    "endDate": "2026-09-01T00:00:00Z",
    "negativeRisk": true
  }
]
""";

    private const string SampleClosedPositionsJson = """
[
  {
    "proxyWallet": "0x56687bf447db6ffa42ffe2204a05edaa20f55839",
    "asset": "12345678901234567890",
    "conditionId": "0xdd22472e552920b8438158ea7238bfadfa4f736aa4cee91a6b86c39ead110917",
    "avgPrice": 0.40,
    "totalBought": 100,
    "realizedPnl": 8,
    "curPrice": 0,
    "timestamp": 1710000300,
    "title": "Will sample event happen?",
    "slug": "will-sample-event-happen",
    "icon": "https://example.com/icon.png",
    "eventSlug": "sample-event",
    "category": "Sports",
    "outcome": "Yes",
    "outcomeIndex": 0,
    "oppositeOutcome": "No",
    "oppositeAsset": "987654321",
    "endDate": "2026-09-01T00:00:00Z"
  }
]
""";

    private const string SampleOrderBookJson = """
{
  "market": "0xdd22472e552920b8438158ea7238bfadfa4f736aa4cee91a6b86c39ead110917",
  "asset_id": "12345678901234567890",
  "timestamp": "1710000000",
  "hash": "a1b2c3",
  "bids": [
    { "price": "0.45", "size": "100" },
    { "price": "0.44", "size": "200" }
  ],
  "asks": [
    { "price": "0.46", "size": "150" },
    { "price": "0.47", "size": "250" }
  ],
  "min_order_size": "1",
  "tick_size": "0.01",
  "neg_risk": false,
  "last_trade_price": "0.45"
}
""";

    private const string SampleLeaderboardJson = """
[
  {
    "rank": "1",
    "proxyWallet": "0x56687bf447db6ffa42ffe2204a05edaa20f55839",
    "userName": "Gopfan",
    "vol": 50000,
    "pnl": 1234.5,
    "profileImage": "https://example.com/profile.png",
    "xUsername": "gopfan",
    "verifiedBadge": true
  }
]
""";

    private const string SampleGeoblockJson = """
{
  "blocked": false,
  "ip": "127.0.0.1",
  "country": "BG",
  "region": "SOF"
}
""";

    private const string SampleGammaMarketsJson = """
[
  {
    "id": "123",
    "question": "Will sample event happen?",
    "conditionId": "0xcondition",
    "slug": "will-sample-event-happen",
    "category": "Politics",
    "endDate": "2026-09-01T00:00:00Z",
    "active": true,
    "closed": true,
    "archived": false,
    "outcomes": "[\"Yes\", \"No\"]",
    "outcomePrices": "[\"1\", \"0\"]",
    "clobTokenIds": "[\"12345678901234567890\", \"987654321\"]"
    }
]
""";

    private const string SampleGammaActiveMarketsJson = """
[
  {
    "id": "2002618",
    "questionID": "question-1",
    "question": "Israel x Lebanon diplomatic meeting by May 31, 2026?",
    "conditionId": "0xcondition",
    "slug": "israel-x-lebanon-diplomatic-meeting-by-may-31-2026",
    "category": "",
    "endDate": "2026-05-31T00:00:00Z",
    "createdAt": "2026-05-01T12:00:00Z",
    "updatedAt": "2026-05-01T12:30:00Z",
    "startDate": "2026-05-01T00:00:00Z",
    "eventStartTime": "2026-05-02T00:00:00Z",
    "active": true,
    "closed": false,
    "archived": false,
    "restricted": false,
    "acceptingOrders": true,
    "enableOrderBook": true,
    "negRisk": true,
    "liquidityNum": "123.45",
    "liquidityClob": 67.89,
    "volumeNum": "1000",
    "volume24hr": "10.5",
    "bestBid": "0.42",
    "bestAsk": "0.43",
    "spread": "0.01",
    "lastTradePrice": "0.425",
    "orderMinSize": "5",
    "orderPriceMinTickSize": "0.01",
    "outcomes": "[\"Yes\", \"No\"]",
    "clobTokenIds": "[\"12345678901234567890\", \"987654321\"]",
    "events": [
      {
        "id": "386820",
        "slug": "israel-x-lebanon-diplomatic-meeting-by-341",
        "title": "Israel x Lebanon diplomatic meeting by...?",
        "category": "Politics",
        "seriesSlug": "politics"
      }
    ]
  }
]
""";

    private const string SampleGammaMarketsWithoutCategoryJson = """
[
  {
    "id": "2002618",
    "question": "Israel x Lebanon diplomatic meeting by May 31, 2026?",
    "conditionId": "0xcondition",
    "slug": "israel-x-lebanon-diplomatic-meeting-by-may-31-2026",
    "category": "",
    "endDate": "2026-05-31T00:00:00Z",
    "active": true,
    "closed": true,
    "archived": false,
    "outcomes": "[\"Yes\", \"No\"]",
    "outcomePrices": "[\"1\", \"0\"]",
    "clobTokenIds": "[\"12345678901234567890\", \"987654321\"]",
    "events": [
      {
        "id": "386820",
        "slug": "israel-x-lebanon-diplomatic-meeting-by-341",
        "title": "Israel x Lebanon diplomatic meeting by...?",
        "category": ""
      }
    ]
  }
]
""";

    private const string SampleGammaMarketsWithEventCategoryJson = """
[
  {
    "id": "123",
    "question": "Will sample event happen?",
    "conditionId": "0xcondition",
    "slug": "will-sample-event-happen",
    "category": null,
    "endDate": "2026-09-01T00:00:00Z",
    "active": true,
    "closed": false,
    "archived": false,
    "outcomes": "[\"Yes\", \"No\"]",
    "outcomePrices": "[\"0.55\", \"0.45\"]",
    "clobTokenIds": "[\"12345678901234567890\", \"987654321\"]",
    "events": [
      {
        "id": "event-1",
        "category": "Politics",
        "categories": [
          {
            "label": "Elections",
            "slug": "elections"
          }
        ]
      }
    ]
  }
]
""";

    private const string SampleGammaTennisEventJson = """
{
  "id": "406929",
  "slug": "wta-mertens-eala-2026-04-24",
  "title": "Madrid Open: Elise Mertens vs Alexandra Eala",
  "category": "",
  "tags": [
    { "id": "864", "label": "Tennis", "slug": "tennis" },
    { "id": "1", "label": "Sports", "slug": "sports" },
    { "id": "100639", "label": "Games", "slug": "games" }
  ]
}
""";

    private const string SampleGammaFinanceEventJson = """
{
  "id": "106884",
  "slug": "fed-rate-cut-by-629",
  "title": "Fed rate cut by...?",
  "category": "",
  "tags": [
    { "id": "159", "label": "Fed", "slug": "fed" },
    { "id": "100328", "label": "Economy", "slug": "economy" },
    { "id": "120", "label": "Finance", "slug": "finance" }
  ]
}
""";

    private const string SampleGammaPoliticsEventJson = """
{
  "id": "386820",
  "slug": "israel-x-lebanon-diplomatic-meeting-by-341",
  "title": "Israel x Lebanon diplomatic meeting by...?",
  "category": "",
  "tags": [
    { "id": "78", "label": "Iran", "slug": "iran" },
    { "id": "849", "label": "Lebanon", "slug": "lebanon" },
    { "id": "104010", "label": "Iran Ceasefire", "slug": "diplomacy-ceasefire" },
    { "id": "180", "label": "Israel", "slug": "israel" }
  ]
}
""";

    private const string SampleClobMarketByTokenJson = """
{
  "condition_id": "0xcondition",
  "primary_token_id": "12345678901234567890",
  "secondary_token_id": "987654321"
}
""";

    private sealed class CapturingApiErrorSink : IPolymarketApiErrorSink
    {
        public List<ApiError> Errors { get; } = [];

        public Task RecordAsync(ApiError error, CancellationToken cancellationToken = default)
        {
            Errors.Add(error);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingHttpLogSink : IPolymarketHttpLogSink
    {
        public List<PolymarketHttpLogEntry> Entries { get; } = [];

        public Task RecordAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
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
