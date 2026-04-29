using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Strategy;

public sealed class DefaultPaperTradingEngine : IPaperTradingEngine
{
    public PaperOrder CreateOrder(Signal signal, decimal price, decimal sizeShares, DateTimeOffset expiresAtUtc)
    {
        if (!signal.Accepted)
        {
            throw new InvalidOperationException("Paper orders can only be created from accepted signals.");
        }

        if (price <= 0m || sizeShares <= 0m)
        {
            throw new InvalidOperationException("Paper order price and size must be greater than zero.");
        }

        return new PaperOrder(
            Guid.NewGuid(),
            signal.Id,
            PaperOrderStatus.Pending,
            signal.LeaderTrade.Side,
            signal.LeaderTrade.AssetId,
            signal.LeaderTrade.ConditionId,
            signal.LeaderTrade.Outcome,
            price,
            sizeShares,
            signal.ProposedNotionalUsd ?? price * sizeShares,
            signal.CreatedAtUtc,
            expiresAtUtc);
    }

    public PaperOrder ExpireIfNeeded(PaperOrder order, DateTimeOffset nowUtc)
    {
        if (order.Status is not (PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled))
        {
            return order;
        }

        return order.ExpiresAtUtc < nowUtc
            ? order with { Status = PaperOrderStatus.Expired }
            : order;
    }

    public PaperFill? TrySimulateFill(
        PaperOrder order,
        OrderBookSnapshot? orderBookSnapshot,
        LeaderTrade? observedTrade,
        DateTimeOffset nowUtc)
    {
        if (order.Status is not (PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled))
        {
            return null;
        }

        if (order.Side == TradeSide.Buy)
        {
            return TrySimulateBuyFill(order, orderBookSnapshot, observedTrade, nowUtc);
        }

        if (order.Side == TradeSide.Sell)
        {
            return TrySimulateSellFill(order, orderBookSnapshot, observedTrade, nowUtc);
        }

        return null;
    }

    public PaperOrder ApplyFillStatus(PaperOrder order, PaperFill fill)
    {
        var status = fill.SizeShares >= order.SizeShares
            ? PaperOrderStatus.Filled
            : PaperOrderStatus.PartiallyFilled;

        return order with
        {
            Status = status,
            FilledAtUtc = status == PaperOrderStatus.Filled ? fill.FilledAtUtc : null
        };
    }

    public PaperPosition ApplyBuyFill(
        PaperPosition? currentPosition,
        PaperOrder order,
        PaperFill fill,
        decimal currentBid,
        DateTimeOffset nowUtc)
    {
        if (order.Side != TradeSide.Buy)
        {
            throw new InvalidOperationException("Only BUY paper fills are supported for position accounting.");
        }

        if (currentBid < 0m)
        {
            throw new InvalidOperationException("Current bid must not be negative.");
        }

        var existingSize = currentPosition?.SizeShares ?? 0m;
        var existingCost = existingSize * (currentPosition?.AveragePrice ?? 0m);
        var fillCost = fill.Price * fill.SizeShares;
        var newSize = existingSize + fill.SizeShares;
        var averagePrice = newSize <= 0m ? 0m : (existingCost + fillCost) / newSize;
        var estimatedValue = newSize * currentBid;
        var unrealizedPnl = estimatedValue - (newSize * averagePrice);

        return new PaperPosition(
            order.AssetId,
            order.ConditionId,
            order.Outcome,
            newSize,
            averagePrice,
            estimatedValue,
            unrealizedPnl,
            nowUtc);
    }

    private static PaperFill? TrySimulateBuyFill(
        PaperOrder order,
        OrderBookSnapshot? orderBookSnapshot,
        LeaderTrade? observedTrade,
        DateTimeOffset nowUtc)
    {
        if (orderBookSnapshot?.BestAsk is { } bestAsk && bestAsk <= order.Price)
        {
            return FullFill(order, nowUtc, $"SimulatedApproximate: bestAsk {bestAsk} <= paper buy price {order.Price}.");
        }

        if (observedTrade is { Price: var tradePrice } && tradePrice <= order.Price)
        {
            return FullFill(order, nowUtc, $"SimulatedApproximate: observed trade {tradePrice} <= paper buy price {order.Price}.");
        }

        return null;
    }

    private static PaperFill? TrySimulateSellFill(
        PaperOrder order,
        OrderBookSnapshot? orderBookSnapshot,
        LeaderTrade? observedTrade,
        DateTimeOffset nowUtc)
    {
        if (orderBookSnapshot?.BestBid is { } bestBid && bestBid >= order.Price)
        {
            return FullFill(order, nowUtc, $"SimulatedApproximate: bestBid {bestBid} >= paper sell price {order.Price}.");
        }

        if (observedTrade is { Price: var tradePrice } && tradePrice >= order.Price)
        {
            return FullFill(order, nowUtc, $"SimulatedApproximate: observed trade {tradePrice} >= paper sell price {order.Price}.");
        }

        return null;
    }

    private static PaperFill FullFill(PaperOrder order, DateTimeOffset nowUtc, string evidence)
    {
        return new PaperFill(Guid.NewGuid(), order.Id, order.Price, order.SizeShares, nowUtc, evidence);
    }
}
