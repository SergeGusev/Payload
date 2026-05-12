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
        Assert.Equal(signal.LeaderTrade.TraderWallet, order.CopiedTraderWallet);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(TradeSide.Buy, order.Side);
        Assert.Equal("Yes", order.Outcome);
        Assert.Equal(7.40m, order.NotionalUsd);
        Assert.Equal(expiresAt, order.ExpiresAtUtc);
        Assert.Equal(StrategyIds.FollowLeader, order.StrategyId);
    }

    [Fact]
    public void ExpireIfNeeded_ExpiresPendingOrderAfterTtl()
    {
        var order = PendingOrder(DateTimeOffset.UtcNow.AddSeconds(-1));

        var expired = engine.ExpireIfNeeded(order, DateTimeOffset.UtcNow);

        Assert.Equal(PaperOrderStatus.Expired, expired.Status);
    }

    [Fact]
    public void ExpireIfNeeded_ClosesPartiallyFilledOrderRemainderAfterTtl()
    {
        var order = PendingOrder(DateTimeOffset.UtcNow.AddSeconds(-1)) with
        {
            Status = PaperOrderStatus.PartiallyFilled
        };

        var expired = engine.ExpireIfNeeded(order, DateTimeOffset.UtcNow);

        Assert.Equal(PaperOrderStatus.PartiallyFilledExpired, expired.Status);
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
        Assert.Contains("BalancedGtcDepth", fill.Evidence);
    }

    [Fact]
    public void TrySimulateFill_BuyUsesExecutableAskDepthForPartialFill()
    {
        var order = PendingOrder(DateTimeOffset.UtcNow.AddMinutes(5));
        var orderBook = OrderBook(
            [new OrderBookLevel(0.73m, 100m)],
            [new OrderBookLevel(0.73m, 4m), new OrderBookLevel(0.75m, 100m)]);

        var fill = engine.TrySimulateFill(order, orderBook, observedTrade: null, DateTimeOffset.UtcNow);

        Assert.NotNull(fill);
        Assert.Equal(order.Price, fill.Price);
        Assert.Equal(4m, fill.SizeShares);
        Assert.Contains("FilledShares=4", fill.Evidence);
        Assert.Contains("ObservedDepthVwap=0.73", fill.Evidence);
    }

    [Fact]
    public void TrySimulateFill_BuyUsesLimitPriceWhenAskDepthIsBetter()
    {
        var order = PendingOrder(DateTimeOffset.UtcNow.AddMinutes(5)) with
        {
            Price = 0.37m,
            NotionalUsd = 3.70m
        };
        var orderBook = OrderBook(
            [new OrderBookLevel(0.20m, 100m)],
            [new OrderBookLevel(0.21m, 100m)]);

        var fill = engine.TrySimulateFill(order, orderBook, observedTrade: null, DateTimeOffset.UtcNow);

        Assert.NotNull(fill);
        Assert.Equal(0.37m, fill.Price);
        Assert.Equal(order.SizeShares, fill.SizeShares);
        Assert.Contains("AvgFillPrice=0.37", fill.Evidence);
        Assert.Contains("ObservedDepthVwap=0.21", fill.Evidence);
    }

    [Fact]
    public void TrySimulateFill_BuyCapsFillToRemainingShares()
    {
        var order = PendingOrder(DateTimeOffset.UtcNow.AddMinutes(5));
        var orderBook = OrderBook(
            [new OrderBookLevel(0.73m, 100m)],
            [new OrderBookLevel(0.73m, 100m)]);

        var fill = engine.TrySimulateFill(order, orderBook, observedTrade: null, DateTimeOffset.UtcNow, previouslyFilledShares: 7m);
        var updatedOrder = engine.ApplyFillStatus(order, fill!, previouslyFilledShares: 7m);

        Assert.NotNull(fill);
        Assert.Equal(3m, fill.SizeShares);
        Assert.Equal(PaperOrderStatus.Filled, updatedOrder.Status);
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
    public void TrySimulateFill_SellFillsWhenBestBidCrossesPaperPrice()
    {
        var order = PendingOrder(DateTimeOffset.UtcNow.AddMinutes(5)) with
        {
            Side = TradeSide.Sell,
            Price = 0.74m
        };
        var orderBook = OrderBook(bestBid: 0.74m, bestAsk: 0.75m);

        var fill = engine.TrySimulateFill(order, orderBook, observedTrade: null, DateTimeOffset.UtcNow);

        Assert.NotNull(fill);
        Assert.Equal(order.Price, fill.Price);
        Assert.Equal(order.SizeShares, fill.SizeShares);
        Assert.Contains("BalancedGtcDepth", fill.Evidence);
    }

    [Fact]
    public void TrySimulateFill_SellUsesLimitPriceWhenBidDepthIsBetter()
    {
        var order = PendingOrder(DateTimeOffset.UtcNow.AddMinutes(5)) with
        {
            Side = TradeSide.Sell,
            Price = 0.37m,
            NotionalUsd = 3.70m
        };
        var orderBook = OrderBook(
            [new OrderBookLevel(0.50m, 100m)],
            [new OrderBookLevel(0.51m, 100m)]);

        var fill = engine.TrySimulateFill(order, orderBook, observedTrade: null, DateTimeOffset.UtcNow);

        Assert.NotNull(fill);
        Assert.Equal(0.37m, fill.Price);
        Assert.Equal(order.SizeShares, fill.SizeShares);
        Assert.Contains("AvgFillPrice=0.37", fill.Evidence);
        Assert.Contains("ObservedDepthVwap=0.5", fill.Evidence);
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
        Assert.Equal(order.CopiedTraderWallet, updated.CopiedTraderWallet);
    }

    [Fact]
    public void ApplySellFill_ReducesPositionAndUsesBidForRemainingPnl()
    {
        var now = DateTimeOffset.UtcNow;
        var order = PendingOrder(now.AddMinutes(5)) with
        {
            Side = TradeSide.Sell,
            Price = 0.80m,
            SizeShares = 40m,
            NotionalUsd = 32m
        };
        var currentPosition = new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            100m,
            0.60m,
            70m,
            10m,
            now.AddMinutes(-1),
            order.CopiedTraderWallet);
        var fill = new PaperFill(Guid.NewGuid(), order.Id, 0.80m, 40m, now, "SimulatedApproximate");

        var updated = engine.ApplySellFill(currentPosition, order, fill, currentBid: 0.70m, now);

        Assert.Equal(60m, updated.SizeShares);
        Assert.Equal(0.60m, updated.AveragePrice);
        Assert.Equal(42m, updated.EstimatedValueUsd);
        Assert.Equal(6m, updated.UnrealizedPnlUsd);
        Assert.Equal(order.CopiedTraderWallet, updated.CopiedTraderWallet);
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
            "0xleader",
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
        return OrderBook([new OrderBookLevel(bestBid, 100m)], [new OrderBookLevel(bestAsk, 100m)]);
    }

    private static OrderBookSnapshot OrderBook(
        IReadOnlyList<OrderBookLevel> bids,
        IReadOnlyList<OrderBookLevel> asks)
    {
        return new OrderBookSnapshot(
            "asset-1",
            bids,
            asks,
            DateTimeOffset.UtcNow,
            "condition-1",
            TickSize: 0.01m);
    }
}
