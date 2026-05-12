using System.Globalization;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Strategy;

public sealed class DefaultPaperTradingEngine : IPaperTradingEngine
{
    private const decimal FillEpsilon = 0.00000001m;

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
            signal.LeaderTrade.TraderWallet,
            PaperOrderStatus.Pending,
            signal.LeaderTrade.Side,
            signal.LeaderTrade.AssetId,
            signal.LeaderTrade.ConditionId,
            signal.LeaderTrade.Outcome,
            price,
            sizeShares,
            signal.ProposedNotionalUsd ?? price * sizeShares,
            signal.CreatedAtUtc,
            expiresAtUtc,
            StrategyId: StrategyIds.FollowLeader);
    }

    public PaperOrder ExpireIfNeeded(PaperOrder order, DateTimeOffset nowUtc)
    {
        if (order.Status is not (PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled))
        {
            return order;
        }

        if (order.ExpiresAtUtc >= nowUtc)
        {
            return order;
        }

        var expiredStatus = order.Status == PaperOrderStatus.PartiallyFilled
            ? PaperOrderStatus.PartiallyFilledExpired
            : PaperOrderStatus.Expired;

        return order with { Status = expiredStatus };
    }

    public PaperFill? TrySimulateFill(
        PaperOrder order,
        OrderBookSnapshot? orderBookSnapshot,
        LeaderTrade? observedTrade,
        DateTimeOffset nowUtc,
        decimal previouslyFilledShares = 0m)
    {
        if (order.Status is not (PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled))
        {
            return null;
        }

        var remainingShares = GetRemainingShares(order, previouslyFilledShares);
        if (remainingShares <= 0m)
        {
            return null;
        }

        if (order.Side == TradeSide.Buy)
        {
            return TrySimulateBuyFill(order, orderBookSnapshot, observedTrade, nowUtc, remainingShares);
        }

        if (order.Side == TradeSide.Sell)
        {
            return TrySimulateSellFill(order, orderBookSnapshot, observedTrade, nowUtc, remainingShares);
        }

        return null;
    }

    public PaperOrder ApplyFillStatus(PaperOrder order, PaperFill fill, decimal previouslyFilledShares = 0m)
    {
        var cumulativeFilledShares = Math.Min(order.SizeShares, Math.Max(0m, previouslyFilledShares) + fill.SizeShares);
        var status = cumulativeFilledShares >= order.SizeShares - FillEpsilon
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
            nowUtc,
            order.CopiedTraderWallet);
    }

    public PaperPosition ApplySellFill(
        PaperPosition currentPosition,
        PaperOrder order,
        PaperFill fill,
        decimal currentBid,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(currentPosition);

        if (order.Side != TradeSide.Sell)
        {
            throw new InvalidOperationException("Only SELL paper fills are supported for sell position accounting.");
        }

        if (!string.Equals(currentPosition.AssetId, order.AssetId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(currentPosition.CopiedTraderWallet, order.CopiedTraderWallet, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SELL paper fills require a matching copied-wallet paper position.");
        }

        if (currentBid < 0m)
        {
            throw new InvalidOperationException("Current bid must not be negative.");
        }

        if (fill.SizeShares > currentPosition.SizeShares)
        {
            throw new InvalidOperationException("SELL paper fill size exceeds the current paper position.");
        }

        var newSize = currentPosition.SizeShares - fill.SizeShares;
        var averagePrice = newSize <= 0m ? 0m : currentPosition.AveragePrice;
        var estimatedValue = newSize * currentBid;
        var unrealizedPnl = estimatedValue - (newSize * averagePrice);

        return currentPosition with
        {
            SizeShares = newSize,
            AveragePrice = averagePrice,
            EstimatedValueUsd = estimatedValue,
            UnrealizedPnlUsd = unrealizedPnl,
            UpdatedAtUtc = nowUtc
        };
    }

    private static PaperFill? TrySimulateBuyFill(
        PaperOrder order,
        OrderBookSnapshot? orderBookSnapshot,
        LeaderTrade? observedTrade,
        DateTimeOffset nowUtc,
        decimal remainingShares)
    {
        if (orderBookSnapshot is not null)
        {
            var depthFill = TrySimulateDepthFill(
                order,
                orderBookSnapshot.Asks
                    .Where(level => level is { Price: > 0m, Size: > 0m } && level.Price <= order.Price)
                    .OrderBy(level => level.Price),
                remainingShares,
                nowUtc,
                $"BalancedGtcDepth: BUY limit {Format(order.Price)} crossed ask depth");
            if (depthFill is not null)
            {
                return depthFill with
                {
                    Evidence = string.Concat(
                        depthFill.Evidence,
                        orderBookSnapshot.BestAsk is { } bestAsk
                            ? $" BestAsk={Format(bestAsk)}."
                            : " BestAsk=null.")
                };
            }
        }

        if (observedTrade is { Price: var tradePrice, Size: > 0m } && tradePrice <= order.Price)
        {
            var fillSize = Math.Min(remainingShares, observedTrade.Size);
            return new PaperFill(
                Guid.NewGuid(),
                order.Id,
                order.Price,
                fillSize,
                nowUtc,
                $"BalancedGtcTrade: BUY limit {Format(order.Price)} filled {Format(fillSize)} of remaining {Format(remainingShares)} from observed trade at {Format(tradePrice)}. FillPrice={Format(order.Price)}.");
        }

        return null;
    }

    private static PaperFill? TrySimulateSellFill(
        PaperOrder order,
        OrderBookSnapshot? orderBookSnapshot,
        LeaderTrade? observedTrade,
        DateTimeOffset nowUtc,
        decimal remainingShares)
    {
        if (orderBookSnapshot is not null)
        {
            var depthFill = TrySimulateDepthFill(
                order,
                orderBookSnapshot.Bids
                    .Where(level => level is { Price: > 0m, Size: > 0m } && level.Price >= order.Price)
                    .OrderByDescending(level => level.Price),
                remainingShares,
                nowUtc,
                $"BalancedGtcDepth: SELL limit {Format(order.Price)} crossed bid depth");
            if (depthFill is not null)
            {
                return depthFill with
                {
                    Evidence = string.Concat(
                        depthFill.Evidence,
                        orderBookSnapshot.BestBid is { } bestBid
                            ? $" BestBid={Format(bestBid)}."
                            : " BestBid=null.")
                };
            }
        }

        if (observedTrade is { Price: var tradePrice, Size: > 0m } && tradePrice >= order.Price)
        {
            var fillSize = Math.Min(remainingShares, observedTrade.Size);
            return new PaperFill(
                Guid.NewGuid(),
                order.Id,
                order.Price,
                fillSize,
                nowUtc,
                $"BalancedGtcTrade: SELL limit {Format(order.Price)} filled {Format(fillSize)} of remaining {Format(remainingShares)} from observed trade at {Format(tradePrice)}. FillPrice={Format(order.Price)}.");
        }

        return null;
    }

    private static PaperFill? TrySimulateDepthFill(
        PaperOrder order,
        IOrderedEnumerable<OrderBookLevel> levels,
        decimal remainingShares,
        DateTimeOffset nowUtc,
        string evidencePrefix)
    {
        var filledShares = 0m;
        var observedDepthNotional = 0m;
        var levelsUsed = 0;

        foreach (var level in levels)
        {
            if (filledShares >= remainingShares)
            {
                break;
            }

            var takeShares = Math.Min(remainingShares - filledShares, level.Size);
            if (takeShares <= 0m)
            {
                continue;
            }

            filledShares += takeShares;
            observedDepthNotional += takeShares * level.Price;
            levelsUsed++;
        }

        if (filledShares <= 0m)
        {
            return null;
        }

        var observedDepthVwap = observedDepthNotional / filledShares;
        var fillPrice = order.Price;
        var filledNotional = fillPrice * filledShares;
        var evidence = string.Concat(
            evidencePrefix,
            ". FilledShares=",
            Format(filledShares),
            " RemainingBeforeFill=",
            Format(remainingShares),
            " AvgFillPrice=",
            Format(fillPrice),
            " ObservedDepthVwap=",
            Format(observedDepthVwap),
            " FilledNotionalUsd=",
            Format(filledNotional),
            " LevelsUsed=",
            levelsUsed.ToString(CultureInfo.InvariantCulture),
            ".");

        return new PaperFill(Guid.NewGuid(), order.Id, fillPrice, filledShares, nowUtc, evidence);
    }

    private static decimal GetRemainingShares(PaperOrder order, decimal previouslyFilledShares)
    {
        return Math.Max(0m, order.SizeShares - Math.Max(0m, previouslyFilledShares));
    }

    private static string Format(decimal value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
