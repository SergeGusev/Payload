using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Strategy;

public static class TakerBuyFillEstimator
{
    private const decimal Epsilon = 0.00000001m;

    public static TakerBuyMinimumNotionalEstimate EstimateMinimumBuyNotional(
        OrderBookSnapshot orderBook,
        decimal maxAllowedPrice,
        decimal minOrderSize,
        decimal? maxSpreadAbs = null)
    {
        if (maxAllowedPrice <= 0m || maxAllowedPrice > 1m)
        {
            return TakerBuyMinimumNotionalEstimate.Reject("invalid_max_entry_price", maxAllowedPrice);
        }

        if (minOrderSize <= 0m)
        {
            return TakerBuyMinimumNotionalEstimate.Reject("invalid_min_order_size", maxAllowedPrice);
        }

        if (orderBook.BestAsk is null)
        {
            return TakerBuyMinimumNotionalEstimate.Reject(SignalReasonCodes.MissingBestAsk, maxAllowedPrice);
        }

        if (orderBook.BestAsk > maxAllowedPrice)
        {
            return TakerBuyMinimumNotionalEstimate.Reject(SignalReasonCodes.BestAskAboveMaxEntry, maxAllowedPrice);
        }

        if (maxSpreadAbs is { } spreadLimit &&
            orderBook.SpreadAbs is { } spread &&
            spread > spreadLimit)
        {
            return TakerBuyMinimumNotionalEstimate.Reject(SignalReasonCodes.SpreadTooWideAbs, maxAllowedPrice);
        }

        var remainingShares = minOrderSize;
        var totalCost = 0m;
        var filledShares = 0m;
        var levelsUsed = 0;

        foreach (var ask in orderBook.Asks
            .Where(level => level.Size > 0m && level.Price > 0m)
            .OrderBy(level => level.Price))
        {
            if (ask.Price > maxAllowedPrice)
            {
                break;
            }

            var takeShares = Math.Min(remainingShares, ask.Size);
            if (takeShares <= 0m)
            {
                continue;
            }

            filledShares += takeShares;
            totalCost += takeShares * ask.Price;
            remainingShares -= takeShares;
            levelsUsed++;

            if (remainingShares <= Epsilon)
            {
                break;
            }
        }

        if (filledShares <= 0m || totalCost <= 0m)
        {
            return TakerBuyMinimumNotionalEstimate.Reject(
                SignalReasonCodes.InsufficientLiquidityWithinSlippage,
                maxAllowedPrice);
        }

        if (remainingShares > Epsilon)
        {
            totalCost += remainingShares * maxAllowedPrice;
            filledShares += remainingShares;
        }

        return new TakerBuyMinimumNotionalEstimate(
            Available: true,
            RejectionReason: null,
            MinOrderSize: minOrderSize,
            NotionalUsd: totalCost,
            AveragePrice: totalCost / filledShares,
            MaxAllowedPrice: maxAllowedPrice,
            LevelsUsed: levelsUsed);
    }

    public static TakerBuyFillEstimate Estimate(
        OrderBookSnapshot orderBook,
        decimal targetNotionalUsd,
        decimal maxAllowedPrice,
        decimal? minOrderSize = null,
        decimal? maxSpreadAbs = null)
    {
        if (targetNotionalUsd <= 0m)
        {
            return TakerBuyFillEstimate.Reject("invalid_target_notional", maxAllowedPrice, orderBook);
        }

        if (maxAllowedPrice <= 0m || maxAllowedPrice > 1m)
        {
            return TakerBuyFillEstimate.Reject("invalid_max_entry_price", maxAllowedPrice, orderBook);
        }

        if (orderBook.BestAsk is null)
        {
            return TakerBuyFillEstimate.Reject(SignalReasonCodes.MissingBestAsk, maxAllowedPrice, orderBook);
        }

        if (orderBook.BestAsk > maxAllowedPrice)
        {
            return TakerBuyFillEstimate.Reject(SignalReasonCodes.BestAskAboveMaxEntry, maxAllowedPrice, orderBook);
        }

        if (maxSpreadAbs is { } spreadLimit &&
            orderBook.SpreadAbs is { } spread &&
            spread > spreadLimit)
        {
            return TakerBuyFillEstimate.Reject(SignalReasonCodes.SpreadTooWideAbs, maxAllowedPrice, orderBook);
        }

        var filledShares = 0m;
        var totalCost = 0m;
        var levelsUsed = 0;

        foreach (var ask in orderBook.Asks
            .Where(level => level.Size > 0m && level.Price > 0m)
            .OrderBy(level => level.Price))
        {
            if (ask.Price > maxAllowedPrice)
            {
                break;
            }

            var remainingNotional = targetNotionalUsd - totalCost;
            if (remainingNotional <= Epsilon)
            {
                break;
            }

            var availableNotional = ask.Price * ask.Size;
            var takeNotional = Math.Min(remainingNotional, availableNotional);
            if (takeNotional <= 0m)
            {
                continue;
            }

            var takeShares = takeNotional / ask.Price;
            filledShares += takeShares;
            totalCost += takeNotional;
            levelsUsed++;

            if (targetNotionalUsd - totalCost <= Epsilon)
            {
                totalCost = targetNotionalUsd;
                break;
            }
        }

        if (filledShares <= 0m || totalCost <= 0m)
        {
            return TakerBuyFillEstimate.Reject(
                SignalReasonCodes.InsufficientLiquidityWithinSlippage,
                maxAllowedPrice,
                orderBook,
                filledShares);
        }

        var averageFillPrice = totalCost / filledShares;
        var targetSizeShares = averageFillPrice > 0m
            ? targetNotionalUsd / averageFillPrice
            : 0m;
        var effectiveMinOrderSize = minOrderSize ?? orderBook.MinOrderSize;
        if (effectiveMinOrderSize is { } minimumSize &&
            minimumSize > 0m &&
            targetSizeShares + Epsilon < minimumSize)
        {
            return TakerBuyFillEstimate.Reject(
                SignalReasonCodes.OrderBelowMinSize,
                maxAllowedPrice,
                orderBook,
                targetSizeShares);
        }

        return new TakerBuyFillEstimate(
            Filled: true,
            RejectionReason: null,
            AverageFillPrice: averageFillPrice,
            SizeShares: filledShares,
            NotionalUsd: totalCost,
            TargetSizeShares: targetSizeShares,
            MaxAllowedPrice: maxAllowedPrice,
            BestBid: orderBook.BestBid,
            BestAsk: orderBook.BestAsk,
            SpreadAbs: orderBook.SpreadAbs,
            LevelsUsed: levelsUsed);
    }
}

public sealed record TakerBuyMinimumNotionalEstimate(
    bool Available,
    string? RejectionReason,
    decimal MinOrderSize,
    decimal NotionalUsd,
    decimal AveragePrice,
    decimal MaxAllowedPrice,
    int LevelsUsed)
{
    public static TakerBuyMinimumNotionalEstimate Reject(
        string reason,
        decimal maxAllowedPrice)
    {
        return new TakerBuyMinimumNotionalEstimate(
            Available: false,
            RejectionReason: reason,
            MinOrderSize: 0m,
            NotionalUsd: 0m,
            AveragePrice: 0m,
            MaxAllowedPrice: maxAllowedPrice,
            LevelsUsed: 0);
    }
}

public sealed record TakerBuyFillEstimate(
    bool Filled,
    string? RejectionReason,
    decimal AverageFillPrice,
    decimal SizeShares,
    decimal NotionalUsd,
    decimal TargetSizeShares,
    decimal MaxAllowedPrice,
    decimal? BestBid,
    decimal? BestAsk,
    decimal? SpreadAbs,
    int LevelsUsed)
{
    public static TakerBuyFillEstimate Reject(
        string reason,
        decimal maxAllowedPrice,
        OrderBookSnapshot orderBook,
        decimal targetSizeShares = 0m)
    {
        return new TakerBuyFillEstimate(
            Filled: false,
            RejectionReason: reason,
            AverageFillPrice: 0m,
            SizeShares: 0m,
            NotionalUsd: 0m,
            TargetSizeShares: targetSizeShares,
            MaxAllowedPrice: maxAllowedPrice,
            BestBid: orderBook.BestBid,
            BestAsk: orderBook.BestAsk,
            SpreadAbs: orderBook.SpreadAbs,
            LevelsUsed: 0);
    }
}
