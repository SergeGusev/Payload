namespace PolyCopyTrader.Domain;

public static class FakExecutionRules
{
    public const decimal PartialFillNotionalToleranceUsd = 0.000001m;

    public static bool IsPartialNotionalFill(
        decimal filledNotionalUsd,
        decimal targetNotionalUsd)
    {
        return filledNotionalUsd < targetNotionalUsd - PartialFillNotionalToleranceUsd;
    }
}
