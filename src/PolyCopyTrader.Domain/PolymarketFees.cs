namespace PolyCopyTrader.Domain;

public enum FeeAccountingStatus
{
    LegacyUnknown,
    CalculationUnavailable,
    Calculated,
    VenueReported,
    PartiallyCalculated
}

public enum FeeLiquidityRole
{
    Unknown,
    Maker,
    Taker
}

public sealed record PolymarketClobFeeSchedule(
    decimal? Rate,
    int? Exponent,
    bool? TakerOnly);

public sealed record PolymarketClobMarketInfo(
    string ConditionId,
    long? MakerBaseFeeBps,
    long? TakerBaseFeeBps,
    PolymarketClobFeeSchedule? FeeSchedule,
    string RawJson);

public sealed record PolymarketFeeCalculationResult(
    FeeAccountingStatus Status,
    decimal? FeeUsd,
    decimal? UnroundedFeeUsd,
    string CalculationSource,
    string? UnavailableReason);

public static class PolymarketFeeCalculationConstants
{
    public const int DecimalPlaces = 5;
    public const decimal MinimumNonZeroFeeUsd = 0.00001m;
    public const string FeeCurveCalculationSource =
        "polymarket-clob-v2-fd-shares-rate-price-curve-round5-away-from-zero-v1";
    public const string FeeFreeMarketCalculationSource =
        "polymarket-clob-v2-no-fd-no-base-fees-v1";
    public const string MarketInfoUnavailableCalculationSource =
        "polymarket-clob-market-info-unavailable-v1";
}

public static class PolymarketFeeCalculator
{
    public static PolymarketFeeCalculationResult CalculatePlatformFee(
        decimal shares,
        decimal price,
        FeeLiquidityRole liquidityRole,
        PolymarketClobMarketInfo marketInfo)
    {
        ArgumentNullException.ThrowIfNull(marketInfo);

        if (shares <= 0m)
        {
            return Unavailable("Filled shares must be greater than zero.");
        }

        if (price <= 0m || price >= 1m)
        {
            return Unavailable("Fill price must be greater than zero and less than one.");
        }

        if (marketInfo.FeeSchedule is null)
        {
            if (marketInfo.MakerBaseFeeBps == 0L &&
                marketInfo.TakerBaseFeeBps == 0L)
            {
                return Calculated(
                    0m,
                    0m,
                    PolymarketFeeCalculationConstants.FeeFreeMarketCalculationSource);
            }

            return Unavailable(
                "Fee schedule fd is absent and both base-fee fields are not explicitly zero.");
        }

        var schedule = marketInfo.FeeSchedule;
        if (schedule.Rate is null || schedule.Rate < 0m)
        {
            return Unavailable("Fee schedule rate r is missing or invalid.");
        }

        if (schedule.Exponent is null || schedule.Exponent < 0)
        {
            return Unavailable("Fee schedule exponent e is missing, non-integer, or invalid.");
        }

        if (schedule.TakerOnly is null)
        {
            return Unavailable("Fee schedule taker-only flag to is missing or invalid.");
        }

        if (schedule.Rate == 0m ||
            schedule.TakerOnly == true && liquidityRole == FeeLiquidityRole.Maker)
        {
            return Calculated(
                0m,
                0m,
                PolymarketFeeCalculationConstants.FeeCurveCalculationSource);
        }

        if (liquidityRole == FeeLiquidityRole.Unknown)
        {
            return Unavailable("Liquidity role is required for a non-zero fee schedule.");
        }

        try
        {
            var priceCurve = Pow(price * (1m - price), schedule.Exponent.Value);
            var unroundedFee = checked(shares * schedule.Rate.Value * priceCurve);
            var roundedFee = Math.Round(
                unroundedFee,
                PolymarketFeeCalculationConstants.DecimalPlaces,
                MidpointRounding.AwayFromZero);

            if (roundedFee < PolymarketFeeCalculationConstants.MinimumNonZeroFeeUsd)
            {
                roundedFee = 0m;
            }

            return Calculated(
                roundedFee,
                unroundedFee,
                PolymarketFeeCalculationConstants.FeeCurveCalculationSource);
        }
        catch (OverflowException)
        {
            return Unavailable("Fee calculation overflowed the supported decimal range.");
        }
    }

    private static decimal Pow(decimal value, int exponent)
    {
        var result = 1m;
        for (var index = 0; index < exponent; index++)
        {
            result = checked(result * value);
        }

        return result;
    }

    private static PolymarketFeeCalculationResult Calculated(
        decimal feeUsd,
        decimal unroundedFeeUsd,
        string calculationSource)
    {
        return new PolymarketFeeCalculationResult(
            FeeAccountingStatus.Calculated,
            feeUsd,
            unroundedFeeUsd,
            calculationSource,
            null);
    }

    private static PolymarketFeeCalculationResult Unavailable(string reason)
    {
        return new PolymarketFeeCalculationResult(
            FeeAccountingStatus.CalculationUnavailable,
            null,
            null,
            PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
            reason);
    }
}
