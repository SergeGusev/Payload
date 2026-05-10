using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;

namespace PolyCopyTrader.Tests;

public sealed class MarketDataWebSocketParserTests
{
    [Fact]
    public void Parser_ReadsBookSnapshot()
    {
        var updates = PolymarketMarketDataWebSocketParser.ParseMarketMessage(BookJson);

        var update = Assert.Single(updates);
        Assert.Equal(MarketDataEventType.Book, update.EventType);
        Assert.Equal("65818619657568813474341868652308942079804919287380422192892211131408793125422", update.AssetId);
        Assert.Equal(0.50m, update.BestBid);
        Assert.Equal(0.52m, update.BestAsk);
        Assert.NotNull(update.OrderBookSnapshot);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_757_908_892_351), update.TimestampUtc);
    }

    [Fact]
    public void Parser_ReadsPriceChangeAsTopOfBookUpdate()
    {
        var updates = PolymarketMarketDataWebSocketParser.ParseMarketMessage(PriceChangeJson);

        var update = Assert.Single(updates);
        Assert.Equal(MarketDataEventType.PriceChange, update.EventType);
        Assert.Equal("71321045679252212594626385532706912750332728571942532289631379312455583992563", update.AssetId);
        Assert.Equal(0.50m, update.BestBid);
        Assert.Equal(1m, update.BestAsk);
        Assert.Equal(0.50m, update.Price);
        Assert.Equal(TradeSide.Buy, update.Side);
        Assert.NotNull(update.OrderBookSnapshot);
    }

    [Fact]
    public void Parser_ReadsLastTradePrice()
    {
        var updates = PolymarketMarketDataWebSocketParser.ParseMarketMessage(LastTradePriceJson);

        var update = Assert.Single(updates);
        Assert.Equal(MarketDataEventType.LastTradePrice, update.EventType);
        Assert.Equal("114122071509644379678018727908709560226618148003371446110114509806601493071694", update.AssetId);
        Assert.Equal(0.456m, update.Price);
        Assert.Equal(219.217767m, update.Size);
        Assert.Equal(TradeSide.Buy, update.Side);
        Assert.Equal("0xeeefffggghhh", update.TransactionHash);
        Assert.Contains("\"transaction_hash\"", update.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_ReadsArrayAndIgnoresPong()
    {
        var updates = PolymarketMarketDataWebSocketParser.ParseMarketMessage($"[{BestBidAskJson},{LastTradePriceJson}]");
        var pong = PolymarketMarketDataWebSocketParser.ParseMarketMessage("PONG");

        Assert.Equal(2, updates.Count);
        Assert.Equal(MarketDataEventType.BestBidAsk, updates[0].EventType);
        Assert.Equal(0.73m, updates[0].BestBid);
        Assert.Equal(0.77m, updates[0].BestAsk);
        Assert.Empty(pong);
    }

    [Fact]
    public void Parser_ReadsMarketResolvedForEachAsset()
    {
        var updates = PolymarketMarketDataWebSocketParser.ParseMarketMessage(MarketResolvedJson);

        Assert.Equal(2, updates.Count);
        Assert.All(updates, update =>
        {
            Assert.Equal(MarketDataEventType.MarketResolved, update.EventType);
            Assert.True(update.MarketResolved);
            Assert.Equal("0xcondition", update.ConditionId);
            Assert.Equal("token-yes", update.WinningAssetId);
            Assert.Equal("Yes", update.WinningOutcome);
        });
        Assert.Contains(updates, update => update.AssetId == "token-yes");
        Assert.Contains(updates, update => update.AssetId == "token-no");
    }

    private const string BookJson = """
{
  "event_type": "book",
  "asset_id": "65818619657568813474341868652308942079804919287380422192892211131408793125422",
  "market": "0xbd31dc8a20211944f6b70f31557f1001557b59905b7738480ca09bd4532f84af",
  "bids": [
    { "price": "0.48", "size": "30" },
    { "price": "0.49", "size": "20" },
    { "price": "0.50", "size": "15" }
  ],
  "asks": [
    { "price": "0.52", "size": "25" },
    { "price": "0.53", "size": "60" },
    { "price": "0.54", "size": "10" }
  ],
  "timestamp": "1757908892351",
  "hash": "0xabc123"
}
""";

    private const string PriceChangeJson = """
{
  "event_type": "price_change",
  "market": "0x5f65177b394277fd294cd75650044e32ba009a95022d88a0c1d565897d72f8f1",
  "price_changes": [
    {
      "asset_id": "71321045679252212594626385532706912750332728571942532289631379312455583992563",
      "price": "0.5",
      "size": "200",
      "side": "BUY",
      "hash": "56621a121a47ed9333273e21c83b660cff37ae50",
      "best_bid": "0.5",
      "best_ask": "1"
    }
  ],
  "timestamp": "1757908892351"
}
""";

    private const string LastTradePriceJson = """
{
  "event_type": "last_trade_price",
  "asset_id": "114122071509644379678018727908709560226618148003371446110114509806601493071694",
  "market": "0x6a67b9d828d53862160e470329ffea5246f338ecfffdf2cab45211ec578b0347",
  "price": "0.456",
  "size": "219.217767",
  "fee_rate_bps": "0",
  "side": "BUY",
  "timestamp": "1750428146322",
  "transaction_hash": "0xeeefffggghhh"
}
""";

    private const string BestBidAskJson = """
{
  "event_type": "best_bid_ask",
  "market": "0x0005c0d312de0be897668695bae9f32b624b4a1ae8b140c49f08447fcc74f442",
  "asset_id": "85354956062430465315924116860125388538595433819574542752031640332592237464430",
  "best_bid": "0.73",
  "best_ask": "0.77",
  "spread": "0.04",
  "timestamp": "1766789469958"
}
""";

    private const string MarketResolvedJson = """
{
  "event_type": "market_resolved",
  "id": "1031769",
  "market": "0xcondition",
  "assets_ids": [
    "token-yes",
    "token-no"
  ],
  "winning_asset_id": "token-yes",
  "winning_outcome": "Yes",
  "timestamp": "1766790415550"
}
""";
}
