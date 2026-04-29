using PolyCopyTrader.Domain;
using PolyCopyTrader.Service;

namespace PolyCopyTrader.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void TraderProfile_DefaultsToEnabled()
    {
        var trader = new TraderProfile("Gopfan", "0xPLACEHOLDER");

        Assert.True(trader.Enabled);
    }

    [Fact]
    public void OrderBookSnapshot_ComputesBestPrices()
    {
        var orderBook = new OrderBookSnapshot(
            "asset-1",
            [new OrderBookLevel(0.73m, 100m), new OrderBookLevel(0.72m, 200m)],
            [new OrderBookLevel(0.75m, 100m), new OrderBookLevel(0.76m, 200m)],
            DateTimeOffset.UtcNow);

        Assert.Equal(0.73m, orderBook.BestBid);
        Assert.Equal(0.75m, orderBook.BestAsk);
        Assert.Equal(0.02m, orderBook.SpreadAbs);
    }

    [Fact]
    public void Service_HasBotWorker()
    {
        Assert.Equal("BotWorker", typeof(BotWorker).Name);
    }
}
