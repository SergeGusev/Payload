using PolyCopyTrader.Domain;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class PaperTradingEngineTests
{
    private readonly DefaultPaperTradingEngine engine = new();

    [Fact]
    public void CreateOrder_CreatesPendingOrderFromAcceptedSignal()
    {
        var signal = AcceptedSignal();
        var expiresAt = signal.CreatedAtUtc.AddMinutes(5);

        var order = engine.CreateOrder(signal, 0.74m, 10m, expiresAt);

        Assert.Equal(signal.Id, order.SignalId);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(TradeSide.Buy, order.Side);
        Assert.Equal("Yes", order.Outcome);
        Assert.Equal(7.40m, order.NotionalUsd);
        Assert.Equal(expiresAt, order.ExpiresAtUtc);
    }

    [Fact]
    public void ExpireIfNeeded_ExpiresPendingOrderAfterTtl()
    {
        var order = PendingOrder(DateTimeOffset.UtcNow.AddSeconds(-1));

        var expired = engine.ExpireIfNeeded(order, DateTimeOffset.UtcNow);

        Assert.Equal(PaperOrderStatus.Expired, expired.Status);
    }

    [Fact]
    public void TrySimulateFill_BuyFillsWhenBestAskCrossesPaperPrice()
    {
        var order = PendingOrder(DateTimeOffset.UtcNow.AddMinutes(5));
        var orderBook = OrderBook(bestBid: 0.73m, bestAsk: 0.74m);

        var fill = engine.TrySimulateFill(order, orderBook, observedTrade: null, DateTimeOffset.UtcNow);

        Assert.NotNull(fill);
        Assert.Equal(order.Price, fill.Price);
        Assert.Equal(order.SizeShares, fill.SizeShares);
        Assert.Contains("SimulatedApproximate", fill.Evidence);
    }

    [Fact]
    public void TrySimulateFill_BuyDoesNotFillWhenMarketDoesNotCross()
    {
        var order = PendingOrder(DateTimeOffset.UtcNow.AddMinutes(5));
        var orderBook = OrderBook(bestBid: 0.73m, bestAsk: 0.75m);

        var fill = engine.TrySimulateFill(order, orderBook, observedTrade: null, DateTimeOffset.UtcNow);

        Assert.Null(fill);
    }

    [Fact]
    public void ApplyBuyFill_UpdatesWeightedAverageAndUsesBidForPnl()
    {
        var now = DateTimeOffset.UtcNow;
        var order = PendingOrder(now.AddMinutes(5)) with
        {
            Price = 0.80m,
            SizeShares = 50m,
            NotionalUsd = 40m
        };
        var currentPosition = new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            100m,
            0.60m,
            70m,
            10m,
            now.AddMinutes(-1));
        var fill = new PaperFill(Guid.NewGuid(), order.Id, 0.80m, 50m, now, "SimulatedApproximate");

        var updated = engine.ApplyBuyFill(currentPosition, order, fill, currentBid: 0.70m, now);

        Assert.Equal(150m, updated.SizeShares);
        Assert.Equal(0.6666666666666666666666666667m, updated.AveragePrice);
        Assert.Equal(105m, updated.EstimatedValueUsd);
        Assert.Equal(5m, updated.UnrealizedPnlUsd);
    }

    private static Signal AcceptedSignal()
    {
        var trade = new LeaderTrade(
            "0x56687bf447db6ffa42ffe2204a05edaa20f55839",
            "Gopfan",
            "condition-1",
            "asset-1",
            "sample-market",
            "Will sample event happen?",
            "Yes",
            TradeSide.Buy,
            0.74m,
            100m,
            74m,
            DateTimeOffset.UtcNow,
            "0xabc");

        return new Signal(
            Guid.NewGuid(),
            trade,
            80,
            Accepted: true,
            "paper_order_small",
            [],
            0.74m,
            10m,
            7.40m,
            DateTimeOffset.UtcNow);
    }

    private static PaperOrder PendingOrder(DateTimeOffset expiresAt)
    {
        return new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Yes",
            0.74m,
            10m,
            7.40m,
            DateTimeOffset.UtcNow,
            expiresAt);
    }

    private static OrderBookSnapshot OrderBook(decimal bestBid, decimal bestAsk)
    {
        return new OrderBookSnapshot(
            "asset-1",
            [new OrderBookLevel(bestBid, 100m)],
            [new OrderBookLevel(bestAsk, 100m)],
            DateTimeOffset.UtcNow,
            "condition-1",
            TickSize: 0.01m);
    }
}
