namespace PolyCopyTrader.Service.Strategies;

internal static class LostCounterStakeSizer
{
    public const int MaxLostCounterStakeCoeff = 2;

    public static LostCounterStakeAdjustment Calculate(
        decimal configuredCoeff,
        int lostCounter,
        decimal baseStakeUsd)
    {
        var normalizedConfiguredCoeff = Math.Max(1m, configuredCoeff);
        if (normalizedConfiguredCoeff <= 1m || baseStakeUsd <= 0m)
        {
            return LostCounterStakeAdjustment.Disabled(normalizedConfiguredCoeff, baseStakeUsd);
        }

        if (lostCounter <= 0)
        {
            return LostCounterStakeAdjustment.Disabled(normalizedConfiguredCoeff, baseStakeUsd, lostCounter);
        }

        var lostCounterCoeff = Math.Min(lostCounter, MaxLostCounterStakeCoeff);
        var addStakeUsd = baseStakeUsd * lostCounterCoeff;
        var effectiveStakeUsd = baseStakeUsd + addStakeUsd;
        return new LostCounterStakeAdjustment(
            normalizedConfiguredCoeff,
            lostCounter,
            lostCounterCoeff,
            baseStakeUsd,
            addStakeUsd,
            effectiveStakeUsd);
    }
}

internal sealed record LostCounterStakeAdjustment(
    decimal ConfiguredCoeff,
    int LostCounter,
    int LostCounterCoeff,
    decimal BaseStakeUsd,
    decimal AddStakeUsd,
    decimal EffectiveStakeUsd)
{
    public static LostCounterStakeAdjustment Disabled(
        decimal configuredCoeff,
        decimal baseStakeUsd,
        int lostCounter = 0)
    {
        return new LostCounterStakeAdjustment(
            configuredCoeff,
            LostCounter: lostCounter,
            LostCounterCoeff: 0,
            baseStakeUsd,
            AddStakeUsd: 0m,
            EffectiveStakeUsd: baseStakeUsd);
    }
}
