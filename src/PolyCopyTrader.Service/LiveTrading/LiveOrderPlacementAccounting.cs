using System.Globalization;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket.Auth;

namespace PolyCopyTrader.Service.LiveTrading;

public static class LiveOrderPlacementAccounting
{
    private const decimal TokenScale = 1_000_000m;
    private const decimal SizeTolerance = 0.000001m;

    public static LiveOrderFillSummary FromPlacementResult(
        TradeSide side,
        decimal limitPrice,
        decimal requestedSizeShares,
        LiveOrderStatus status,
        LiveOrderPlacementResult result)
    {
        if (!result.Success || status != LiveOrderStatus.Matched || requestedSizeShares <= 0m)
        {
            return Empty(requestedSizeShares);
        }

        if (TryCreateFillSummary(side, requestedSizeShares, result, out var summary))
        {
            return summary;
        }

        var fallbackNotional = limitPrice * requestedSizeShares;
        return new LiveOrderFillSummary(
            requestedSizeShares,
            0m,
            limitPrice,
            fallbackNotional,
            fallbackNotional);
    }

    private static LiveOrderFillSummary Empty(decimal requestedSizeShares)
    {
        return new LiveOrderFillSummary(
            0m,
            Math.Max(0m, requestedSizeShares),
            null,
            0m,
            0m);
    }

    private static bool TryCreateFillSummary(
        TradeSide side,
        decimal requestedSizeShares,
        LiveOrderPlacementResult result,
        out LiveOrderFillSummary summary)
    {
        summary = Empty(requestedSizeShares);
        if (!TryParseAmount(result.MakingAmount, out var makingAmount) ||
            !TryParseAmount(result.TakingAmount, out var takingAmount))
        {
            return false;
        }

        if (TryCreateCandidate(side, requestedSizeShares, makingAmount, takingAmount, out summary))
        {
            return true;
        }

        return TryCreateCandidate(
            side,
            requestedSizeShares,
            makingAmount / TokenScale,
            takingAmount / TokenScale,
            out summary);
    }

    private static bool TryCreateCandidate(
        TradeSide side,
        decimal requestedSizeShares,
        decimal makingAmount,
        decimal takingAmount,
        out LiveOrderFillSummary summary)
    {
        summary = Empty(requestedSizeShares);
        var filledSize = side == TradeSide.Buy ? takingAmount : makingAmount;
        var filledNotional = side == TradeSide.Buy ? makingAmount : takingAmount;
        if (filledSize <= 0m || filledNotional <= 0m)
        {
            return false;
        }

        if (filledSize > requestedSizeShares + SizeTolerance)
        {
            return false;
        }

        if (filledSize > requestedSizeShares)
        {
            filledSize = requestedSizeShares;
        }

        var averageFillPrice = filledNotional / filledSize;
        if (averageFillPrice <= 0m || averageFillPrice > 1m + SizeTolerance)
        {
            return false;
        }

        var remainingSize = Math.Max(0m, requestedSizeShares - filledSize);
        summary = new LiveOrderFillSummary(
            filledSize,
            remainingSize,
            averageFillPrice,
            filledNotional,
            filledNotional);
        return true;
    }

    private static bool TryParseAmount(string? value, out decimal amount)
    {
        amount = 0m;
        return !string.IsNullOrWhiteSpace(value) &&
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }
}

public sealed record LiveOrderFillSummary(
    decimal FilledSize,
    decimal RemainingSize,
    decimal? AverageFillPrice,
    decimal FilledNotionalUsd,
    decimal CostBasisUsd);
