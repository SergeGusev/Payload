using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class BtcUpDown5mInstantOpeningLimitPriceTests
{
    [Fact]
    public void FixedOutcomeBpsInstantUsesUncappedEffectiveMaxPrice()
    {
        var options = new BtcUpDown5mStrategyOptions
        {
            InstantOpeningLimitMaxPrice = 0.65m,
            DiffCounterInstantMaxPrice = 0.75m
        };
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant)
            .ToArray();

        Assert.NotEmpty(variants);
        Assert.All(variants, variant =>
            Assert.Equal(1.00m, BtcUpDown5mPaperStrategyProcessor.GetEffectiveInstantOpeningLimitMaxPrice(variant, options)));
    }

    [Fact]
    public void OtherInstantFamiliesKeepConfiguredMaxPrices()
    {
        var options = new BtcUpDown5mStrategyOptions
        {
            InstantOpeningLimitMaxPrice = 0.65m,
            DiffCounterInstantMaxPrice = 0.75m
        };
        var simpleVariant = StrategyIds.UpDown5mStrategyVariants
            .First(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SimpleFixedOutcomeInstant);
        var middleVariant = StrategyIds.UpDown5mStrategyVariants
            .First(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceInstant);
        var diffVariant = StrategyIds.UpDown5mStrategyVariants
            .First(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend);

        Assert.Equal(0.50m, BtcUpDown5mPaperStrategyProcessor.GetEffectiveInstantOpeningLimitMaxPrice(simpleVariant, options));
        Assert.Equal(0.65m, BtcUpDown5mPaperStrategyProcessor.GetEffectiveInstantOpeningLimitMaxPrice(middleVariant, options));
        Assert.Equal(0.75m, BtcUpDown5mPaperStrategyProcessor.GetEffectiveInstantOpeningLimitMaxPrice(diffVariant, options));
    }
}
