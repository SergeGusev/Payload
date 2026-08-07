namespace PolyCopyTrader.Domain;

public static class FeeAccountingRules
{
    public static FeeAccountingStatus ParseStatus(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out FeeAccountingStatus status)
            ? status
            : FeeAccountingStatus.LegacyUnknown;
    }

    public static FeeLiquidityRole ParseLiquidityRole(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out FeeLiquidityRole role)
            ? role
            : FeeLiquidityRole.Unknown;
    }

    public static bool IsAccounted(string? value)
    {
        return IsAccounted(ParseStatus(value));
    }

    public static bool IsAccounted(FeeAccountingStatus status)
    {
        return status is FeeAccountingStatus.Calculated or FeeAccountingStatus.VenueReported;
    }

    public static FeeAccountingStatus Aggregate(IEnumerable<string?> statuses)
    {
        var values = statuses.Select(ParseStatus).ToArray();
        if (values.Length == 0)
        {
            return FeeAccountingStatus.LegacyUnknown;
        }

        if (values.All(status => status == FeeAccountingStatus.VenueReported))
        {
            return FeeAccountingStatus.VenueReported;
        }

        if (values.All(IsAccounted))
        {
            return FeeAccountingStatus.Calculated;
        }

        if (values.Any(IsAccounted) || values.Any(status => status == FeeAccountingStatus.PartiallyCalculated))
        {
            return FeeAccountingStatus.PartiallyCalculated;
        }

        return values.All(status => status == FeeAccountingStatus.LegacyUnknown)
            ? FeeAccountingStatus.LegacyUnknown
            : FeeAccountingStatus.CalculationUnavailable;
    }
}
