using System.Globalization;
using System.Numerics;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class HistoricalGrossNetDonorMatcherTests
{
    [Fact]
    public void CurrentCatalog_BuildsCompleteClosedV1Descriptors()
    {
        var matcher = new HistoricalGrossNetDonorMatcher();
        var target = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_3hour_average_bps_2_fak_premarket");

        var result = matcher.Match(new HistoricalGrossNetDonorTarget(target.Id), []);

        Assert.Equal(HistoricalGrossNetDonorTier.Fixed, result.Tier);
        Assert.NotNull(result.TargetDescriptor);
        Assert.Equal("ETH", result.TargetDescriptor.AssetSymbol);
        Assert.Equal(300, result.TargetDescriptor.MarketIntervalSeconds);
        Assert.Equal(10_800, result.TargetDescriptor.Family.RequiredReferenceAverageWindowSeconds);
    }

    [Fact]
    public void Descriptor_RejectsUnknownWindowAndUnresolvedLink()
    {
        var invalidWindow = Variant(1) with { RequiredReferenceAverageWindow = "6h" };
        var unresolvedLink = Variant(2) with { BaseSignalStrategyId = Id(999) };
        var invalidInterval = Variant(3) with { MarketInterval = (BtcUpDownMarketInterval)999 };

        Assert.Throws<InvalidOperationException>(() => new HistoricalGrossNetDonorMatcher([invalidWindow]));
        Assert.Throws<InvalidOperationException>(() => new HistoricalGrossNetDonorMatcher([unresolvedLink]));
        Assert.Throws<InvalidOperationException>(() => new HistoricalGrossNetDonorMatcher([invalidInterval]));
    }

    [Fact]
    public void Descriptor_UsesClosedIntervalsFamilyNumericFieldsAndTypedLinks()
    {
        var baseSignal = Variant(
            4,
            threshold: 1m,
            behavior: BtcUpDown5mStrategyBehavior.Standard,
            interval: BtcUpDownMarketInterval.FifteenMinutes) with
        {
            Direction = BtcUpDown5mStrategyDirection.Less
        };
        var confirmation = Variant(
            5,
            threshold: 3m,
            behavior: BtcUpDown5mStrategyBehavior.GammaOutcomeSelection,
            interval: BtcUpDownMarketInterval.OneHour) with
        {
            Direction = BtcUpDown5mStrategyDirection.More
        };
        var lower = Variant(
            6,
            threshold: null,
            behavior: BtcUpDown5mStrategyBehavior.AlwaysDown,
            interval: BtcUpDownMarketInterval.FourHours);
        var target = Variant(7, interval: BtcUpDownMarketInterval.FifteenMinutes) with
        {
            EntryDelaySeconds = 0,
            PreOpenLifetimeMode = BtcUpDownPreOpenLifetimeMode.FullPeriod,
            FixedOutcome = BtcUpDownFixedOutcome.Up,
            DiffCounterTriggerOutcome = BtcUpDownFixedOutcome.Down,
            RequiredReferenceAverageWindow = "3h",
            PaperOnly = true,
            FixedLimitPrice = 0.41m,
            MakerMinBestAskExclusive = 0.42m,
            ShiftDiffCount = 4,
            FakMaximumOrderPrice = 0.43m,
            MakerMaximumOrderPrice = 0.44m,
            BaseSignalStrategyId = baseSignal.Id,
            ConfirmationSignalStrategyId = confirmation.Id,
            LowerEnterSourceStrategyId = lower.Id
        };
        var matcher = new HistoricalGrossNetDonorMatcher([baseSignal, confirmation, lower, target]);

        var result = matcher.Match(new HistoricalGrossNetDonorTarget(target.Id), []);
        var descriptor = Assert.IsType<HistoricalGrossNetDonorDescriptor>(result.TargetDescriptor);

        Assert.Equal(900, descriptor.MarketIntervalSeconds);
        Assert.Equal(HistoricalGrossNetEntryDelaySignClass.Zero, descriptor.Family.EntryDelaySignClass);
        Assert.Equal(BtcUpDownPreOpenLifetimeMode.FullPeriod, descriptor.Family.PreOpenLifetimeMode);
        Assert.Equal(BtcUpDownFixedOutcome.Up, descriptor.Family.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Down, descriptor.Family.DiffCounterTriggerOutcome);
        Assert.Equal(10_800, descriptor.Family.RequiredReferenceAverageWindowSeconds);
        Assert.True(descriptor.Family.PaperOnly);
        Assert.True(descriptor.Family.HasMakerMinBestAskExclusive);
        Assert.True(descriptor.Family.HasFakMaximumOrderPrice);
        Assert.True(descriptor.Family.HasMakerMaximumOrderPrice);
        Assert.Equal(
            new HistoricalGrossNetLinkedDescriptor(
                baseSignal.Behavior,
                baseSignal.MarketInterval,
                baseSignal.Direction,
                baseSignal.DecisionThresholdBps),
            descriptor.Family.BaseSignal);
        Assert.Equal(
            new HistoricalGrossNetLinkedDescriptor(
                confirmation.Behavior,
                confirmation.MarketInterval,
                confirmation.Direction,
                confirmation.DecisionThresholdBps),
            descriptor.Family.ConfirmationSignal);
        Assert.Equal(
            new HistoricalGrossNetLinkedDescriptor(
                lower.Behavior,
                lower.MarketInterval,
                lower.Direction,
                lower.DecisionThresholdBps),
            descriptor.Family.LowerEnterSource);
        Assert.Equal(target.DecisionThresholdBps, descriptor.NumericVector.DecisionThresholdBps);
        Assert.Equal(target.DecisionDepth, descriptor.NumericVector.DecisionDepth);
        Assert.Equal(0, descriptor.NumericVector.EntryDelaySeconds);
        Assert.Equal(0.41m, descriptor.NumericVector.FixedLimitPrice);
        Assert.Equal(0.42m, descriptor.NumericVector.MakerMinBestAskExclusive);
        Assert.Equal(4, descriptor.NumericVector.ShiftDiffCount);
        Assert.Equal(0.43m, descriptor.NumericVector.FakMaximumOrderPrice);
        Assert.Equal(0.44m, descriptor.NumericVector.MakerMaximumOrderPrice);
    }

    [Fact]
    public void Tier0_UsesExactSameStrategyWithoutCatalogMetadata()
    {
        var customStrategyId = Id(900);
        var donor = Aggregate(customStrategyId, fee: "1.00", basis: "100.00", count: 3);
        var matcher = new HistoricalGrossNetDonorMatcher([]);

        var result = matcher.Match(new HistoricalGrossNetDonorTarget(customStrategyId), [donor]);

        Assert.Equal(HistoricalGrossNetDonorTier.SameStrategy, result.Tier);
        Assert.Same(donor, result.Donor);
        Assert.Null(result.TargetDescriptor);
    }

    [Fact]
    public void Tier1_EthTwoBpsUsesDistanceThenStakeCountAndLowerNumericVector()
    {
        var target = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_reference_average_bps_2_fak_premarket");
        var oneBps = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_reference_average_bps_1_fak_premarket");
        var threeBps = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_reference_average_bps_3_fak_premarket");
        var matcher = new HistoricalGrossNetDonorMatcher();

        var numericTie = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [
                Aggregate(threeBps.Id, "1", "100", 5),
                Aggregate(oneBps.Id, "1", "100", 5)
            ]);
        var stakeWins = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [
                Aggregate(oneBps.Id, "1", "100", 5),
                Aggregate(threeBps.Id, "2", "101", 1)
            ]);
        var countWins = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [
                Aggregate(oneBps.Id, "1", "100", 5),
                Aggregate(threeBps.Id, "1", "100", 6)
            ]);

        Assert.Equal(HistoricalGrossNetDonorTier.SameAssetExactFamily, numericTie.Tier);
        Assert.Equal(oneBps.Id, numericTie.Donor?.StrategyId);
        Assert.Equal(threeBps.Id, stakeWins.Donor?.StrategyId);
        Assert.Equal(threeBps.Id, countWins.Donor?.StrategyId);
    }

    [Fact]
    public void FamilyLinks_CompareTypedDescriptorInsteadOfUuidOrPresence()
    {
        var linkA = Variant(10, threshold: 2m);
        var linkEquivalentDifferentId = Variant(11, threshold: 2m);
        var linkDifferentThreshold = Variant(12, threshold: 3m);
        var target = Variant(20) with { BaseSignalStrategyId = linkA.Id };
        var equivalentDonor = Variant(21) with { BaseSignalStrategyId = linkEquivalentDifferentId.Id };
        var differentDonor = Variant(22) with { BaseSignalStrategyId = linkDifferentThreshold.Id };
        var matcher = new HistoricalGrossNetDonorMatcher(
            [linkA, linkEquivalentDifferentId, linkDifferentThreshold, target, equivalentDonor, differentDonor]);

        var equivalent = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [Aggregate(equivalentDonor.Id)]);
        var different = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [Aggregate(differentDonor.Id)]);

        Assert.Equal(HistoricalGrossNetDonorTier.SameAssetExactFamily, equivalent.Tier);
        Assert.Equal(HistoricalGrossNetDonorTier.SameAssetSemantic, different.Tier);
    }

    [Fact]
    public void NullableLink_NullVersusNonNullIsNotExactFamily()
    {
        var link = Variant(30);
        var target = Variant(31);
        var donor = Variant(32) with { ConfirmationSignalStrategyId = link.Id };
        var matcher = new HistoricalGrossNetDonorMatcher([link, target, donor]);

        var result = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [Aggregate(donor.Id)]);

        Assert.Equal(HistoricalGrossNetDonorTier.SameAssetSemantic, result.Tier);
    }

    [Fact]
    public void EveryLinkedSlot_UsesTypedEquivalenceAndClosedNullRule()
    {
        foreach (var slot in Enum.GetValues<LinkedSlot>())
        {
            var idBase = 200 + ((int)slot * 10);
            var originalLink = Variant(idBase, threshold: 2m);
            var equivalentLink = Variant(idBase + 1, threshold: 2m);
            var differentLink = Variant(idBase + 2, threshold: 3m);
            var target = WithLink(Variant(idBase + 3), slot, originalLink.Id);
            var equivalentDonor = WithLink(Variant(idBase + 4), slot, equivalentLink.Id);
            var differentDonor = WithLink(Variant(idBase + 5), slot, differentLink.Id);
            var nullDonor = WithLink(Variant(idBase + 6), slot, null);
            var nullTarget = WithLink(Variant(idBase + 7), slot, null);
            var secondNullDonor = WithLink(Variant(idBase + 8), slot, null);
            var matcher = new HistoricalGrossNetDonorMatcher(
                [
                    originalLink,
                    equivalentLink,
                    differentLink,
                    target,
                    equivalentDonor,
                    differentDonor,
                    nullDonor,
                    nullTarget,
                    secondNullDonor
                ]);

            var equivalent = matcher.Match(
                new HistoricalGrossNetDonorTarget(target.Id),
                [Aggregate(equivalentDonor.Id)]);
            var different = matcher.Match(
                new HistoricalGrossNetDonorTarget(target.Id),
                [Aggregate(differentDonor.Id)]);
            var oneNull = matcher.Match(
                new HistoricalGrossNetDonorTarget(target.Id),
                [Aggregate(nullDonor.Id)]);
            var bothNull = matcher.Match(
                new HistoricalGrossNetDonorTarget(nullTarget.Id),
                [Aggregate(secondNullDonor.Id)]);

            Assert.Equal(HistoricalGrossNetDonorTier.SameAssetExactFamily, equivalent.Tier);
            Assert.Equal(HistoricalGrossNetDonorTier.SameAssetSemantic, different.Tier);
            Assert.Equal(HistoricalGrossNetDonorTier.SameAssetSemantic, oneNull.Tier);
            Assert.Equal(HistoricalGrossNetDonorTier.SameAssetExactFamily, bothNull.Tier);
        }
    }

    [Fact]
    public void Tier1_NullableNumericDistanceUsesClosedInfinityRule()
    {
        var target = Variant(40) with { FixedLimitPrice = null };
        var nullDonor = Variant(41) with { FixedLimitPrice = null };
        var valuedDonor = Variant(42) with { FixedLimitPrice = 0.50m };
        var matcher = new HistoricalGrossNetDonorMatcher([target, nullDonor, valuedDonor]);

        var result = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [Aggregate(valuedDonor.Id, basis: "1000"), Aggregate(nullDonor.Id, basis: "1")]);

        Assert.Equal(HistoricalGrossNetDonorTier.SameAssetExactFamily, result.Tier);
        Assert.Equal(nullDonor.Id, result.Donor?.StrategyId);
    }

    [Fact]
    public void Tier2_UsesClosedSemanticFieldPriorityBeforeNumericDistanceOrStake()
    {
        var target = Variant(50, behavior: BtcUpDown5mStrategyBehavior.Standard);
        var behaviorMatchFarInterval = Variant(
            51,
            behavior: BtcUpDown5mStrategyBehavior.Standard,
            interval: BtcUpDownMarketInterval.FourHours);
        var behaviorMismatchExactInterval = Variant(
            52,
            behavior: BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);
        var matcher = new HistoricalGrossNetDonorMatcher(
            [target, behaviorMatchFarInterval, behaviorMismatchExactInterval]);

        var result = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [
                Aggregate(behaviorMatchFarInterval.Id, basis: "1"),
                Aggregate(behaviorMismatchExactInterval.Id, basis: "1000000")
            ]);

        Assert.Equal(HistoricalGrossNetDonorTier.SameAssetSemantic, result.Tier);
        Assert.Equal(behaviorMatchFarInterval.Id, result.Donor?.StrategyId);
    }

    [Fact]
    public void Tier2_UsesEverySemanticFieldInTheClosedLexicographicOrder()
    {
        var originalLink = Variant(120, threshold: 2m);
        var differentLink = Variant(121, threshold: 3m);
        var target = Variant(122, behavior: BtcUpDown5mStrategyBehavior.Standard) with
        {
            EntryDelaySeconds = -30,
            Direction = BtcUpDown5mStrategyDirection.Dynamic,
            PreOpenLifetimeMode = BtcUpDownPreOpenLifetimeMode.FullPeriod,
            FixedOutcome = BtcUpDownFixedOutcome.Up,
            DiffCounterTriggerOutcome = BtcUpDownFixedOutcome.Down,
            RequiredReferenceAverageWindow = "3h",
            PaperOnly = true,
            MakerMinBestAskExclusive = 0.41m,
            FakMaximumOrderPrice = 0.42m,
            MakerMaximumOrderPrice = 0.43m,
            BaseSignalStrategyId = originalLink.Id,
            ConfirmationSignalStrategyId = originalLink.Id,
            LowerEnterSourceStrategyId = originalLink.Id
        };
        var semanticFallback = target with { LowerEnterSourceStrategyId = differentLink.Id };
        var candidates = new[]
        {
            semanticFallback with { Id = Id(123), Behavior = BtcUpDown5mStrategyBehavior.GammaOutcomeSelection },
            semanticFallback with { Id = Id(124), MarketInterval = BtcUpDownMarketInterval.FifteenMinutes },
            semanticFallback with { Id = Id(125), EntryDelaySeconds = 30 },
            semanticFallback with { Id = Id(126), EntryDelaySeconds = -31 },
            semanticFallback with { Id = Id(127), Direction = BtcUpDown5mStrategyDirection.Less },
            semanticFallback with { Id = Id(128), PreOpenLifetimeMode = BtcUpDownPreOpenLifetimeMode.HalfPeriod },
            semanticFallback with { Id = Id(129), FixedOutcome = BtcUpDownFixedOutcome.Down },
            semanticFallback with { Id = Id(130), DiffCounterTriggerOutcome = BtcUpDownFixedOutcome.Up },
            semanticFallback with { Id = Id(131), RequiredReferenceAverageWindow = null },
            semanticFallback with { Id = Id(132), PaperOnly = false },
            semanticFallback with { Id = Id(133), MakerMinBestAskExclusive = null },
            semanticFallback with { Id = Id(134), FakMaximumOrderPrice = null },
            semanticFallback with { Id = Id(135), MakerMaximumOrderPrice = null },
            semanticFallback with { Id = Id(136), BaseSignalStrategyId = differentLink.Id },
            semanticFallback with { Id = Id(137), ConfirmationSignalStrategyId = differentLink.Id },
            semanticFallback with { Id = Id(138) }
        };
        var matcher = new HistoricalGrossNetDonorMatcher(
            [originalLink, differentLink, target, .. candidates]);

        for (var prefixLength = 1; prefixLength <= candidates.Length; prefixLength++)
        {
            var result = matcher.Match(
                new HistoricalGrossNetDonorTarget(target.Id),
                candidates
                    .Take(prefixLength)
                    .Select(candidate => Aggregate(candidate.Id, basis: "1000000"))
                    .ToArray());

            Assert.Equal(HistoricalGrossNetDonorTier.SameAssetSemantic, result.Tier);
            Assert.Equal(candidates[prefixLength - 1].Id, result.Donor?.StrategyId);
        }
    }

    [Fact]
    public void Tier3_ExactFamilyOnOtherAssetPrecedesAnyCryptoStake()
    {
        var target = Variant(60, asset: "ETH");
        var sameFamilyBtc = Variant(61, asset: "BTC");
        var unrelatedSol = Variant(
            62,
            asset: "SOL",
            behavior: BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);
        var matcher = new HistoricalGrossNetDonorMatcher([target, sameFamilyBtc, unrelatedSol]);

        var result = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [Aggregate(unrelatedSol.Id, basis: "1000000"), Aggregate(sameFamilyBtc.Id, basis: "1")]);

        Assert.Equal(HistoricalGrossNetDonorTier.OtherCryptoExactFamily, result.Tier);
        Assert.Equal(sameFamilyBtc.Id, result.Donor?.StrategyId);
    }

    [Fact]
    public void Tier4_UsesStakeThenCountThenCanonicalUuid()
    {
        var target = Variant(70, asset: "ETH", behavior: BtcUpDown5mStrategyBehavior.Standard);
        var low = Variant(71, asset: "BTC", behavior: BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);
        var highLowCount = Variant(72, asset: "SOL", behavior: BtcUpDown5mStrategyBehavior.MiddleReference);
        var highHighCountA = Variant(73, asset: "BTC", behavior: BtcUpDown5mStrategyBehavior.AlwaysUp);
        var highHighCountB = Variant(74, asset: "SOL", behavior: BtcUpDown5mStrategyBehavior.AlwaysDown);
        var matcher = new HistoricalGrossNetDonorMatcher(
            [target, low, highLowCount, highHighCountA, highHighCountB]);
        var expectedUuidWinner = new[] { highHighCountA.Id, highHighCountB.Id }
            .OrderBy(id => id.ToString("D", CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .First();

        var result = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [
                Aggregate(low.Id, basis: "99", count: 100),
                Aggregate(highLowCount.Id, basis: "100", count: 1),
                Aggregate(highHighCountB.Id, basis: "100", count: 2),
                Aggregate(highHighCountA.Id, basis: "100", count: 2)
            ]);

        Assert.Equal(HistoricalGrossNetDonorTier.AnyCrypto, result.Tier);
        Assert.Equal(expectedUuidWinner, result.Donor?.StrategyId);
    }

    [Fact]
    public void NumericVectorAndUuidTieBreaksApplyAcrossTiersOneTwoAndThree()
    {
        var tier1Target = Variant(300, threshold: 2m);
        var tier1Low = Variant(301, threshold: 1m);
        var tier1High = Variant(302, threshold: 3m);
        var tier1CloneA = Variant(303, threshold: 2m);
        var tier1CloneB = Variant(304, threshold: 2m);
        AssertNumericAndUuidTieBreaks(
            new HistoricalGrossNetDonorMatcher(
                [tier1Target, tier1Low, tier1High, tier1CloneA, tier1CloneB]),
            tier1Target,
            tier1Low,
            tier1High,
            tier1CloneA,
            tier1CloneB,
            HistoricalGrossNetDonorTier.SameAssetExactFamily);

        var tier2Target = Variant(310, threshold: 2m, behavior: BtcUpDown5mStrategyBehavior.Standard);
        var tier2Low = Variant(311, threshold: 1m, behavior: BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);
        var tier2High = Variant(312, threshold: 3m, behavior: BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);
        var tier2CloneA = Variant(313, threshold: 2m, behavior: BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);
        var tier2CloneB = Variant(314, threshold: 2m, behavior: BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);
        AssertNumericAndUuidTieBreaks(
            new HistoricalGrossNetDonorMatcher(
                [tier2Target, tier2Low, tier2High, tier2CloneA, tier2CloneB]),
            tier2Target,
            tier2Low,
            tier2High,
            tier2CloneA,
            tier2CloneB,
            HistoricalGrossNetDonorTier.SameAssetSemantic);

        var tier3Target = Variant(320, asset: "ETH", threshold: 2m);
        var tier3Low = Variant(321, asset: "BTC", threshold: 1m);
        var tier3High = Variant(322, asset: "SOL", threshold: 3m);
        var tier3CloneA = Variant(323, asset: "BTC", threshold: 2m);
        var tier3CloneB = Variant(324, asset: "SOL", threshold: 2m);
        AssertNumericAndUuidTieBreaks(
            new HistoricalGrossNetDonorMatcher(
                [tier3Target, tier3Low, tier3High, tier3CloneA, tier3CloneB]),
            tier3Target,
            tier3Low,
            tier3High,
            tier3CloneA,
            tier3CloneB,
            HistoricalGrossNetDonorTier.OtherCryptoExactFamily);
    }

    [Fact]
    public void Match_IsInvariantToEveryPermutationOfTheDonorInput()
    {
        var target = Variant(330, asset: "ETH", threshold: 2m);
        var tier1Low = Variant(331, asset: "ETH", threshold: 1m);
        var tier1High = Variant(332, asset: "ETH", threshold: 3m);
        var tier2 = Variant(
            333,
            asset: "ETH",
            threshold: 2m,
            behavior: BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);
        var tier3 = Variant(334, asset: "BTC", threshold: 2m);
        var matcher = new HistoricalGrossNetDonorMatcher([target, tier1Low, tier1High, tier2, tier3]);
        var donors = new[]
        {
            Aggregate(tier1High.Id, basis: "100"),
            Aggregate(tier2.Id, basis: "1000000"),
            Aggregate(tier3.Id, basis: "1000000"),
            Aggregate(tier1Low.Id, basis: "100")
        };
        var expected = matcher.Match(new HistoricalGrossNetDonorTarget(target.Id), donors);
        var permutationCount = 0;

        foreach (var permutation in Permutations(donors))
        {
            var actual = matcher.Match(new HistoricalGrossNetDonorTarget(target.Id), permutation);

            Assert.Equal(expected.Tier, actual.Tier);
            Assert.Equal(expected.Donor?.StrategyId, actual.Donor?.StrategyId);
            Assert.Equal(expected.ComparisonKey.ToArray(), actual.ComparisonKey.ToArray());
            permutationCount++;
        }

        Assert.Equal(24, permutationCount);
    }

    [Fact]
    public void NoncatalogTarget_UsesDegradedSameAssetThenTier4_AndUnknownAssetUsesFixed()
    {
        var ethDonor = Variant(80, asset: "ETH");
        var btcDonor = Variant(81, asset: "BTC");
        var matcher = new HistoricalGrossNetDonorMatcher([ethDonor, btcDonor]);
        var customTargetId = Id(800);
        var donors = new[]
        {
            Aggregate(btcDonor.Id, basis: "1000"),
            Aggregate(ethDonor.Id, basis: "1")
        };

        var knownAsset = matcher.Match(
            new HistoricalGrossNetDonorTarget(customTargetId, "eth"),
            donors);
        var unknownAsset = matcher.Match(
            new HistoricalGrossNetDonorTarget(customTargetId),
            donors);

        Assert.Equal(HistoricalGrossNetDonorTier.DegradedSameAsset, knownAsset.Tier);
        Assert.Equal(ethDonor.Id, knownAsset.Donor?.StrategyId);
        Assert.Equal(HistoricalGrossNetDonorTier.Fixed, unknownAsset.Tier);
        Assert.Null(unknownAsset.Donor);
    }

    [Fact]
    public void EstimatedAggregatesNeverDonate()
    {
        var target = Variant(90);
        var donor = Variant(91, asset: "BTC");
        var matcher = new HistoricalGrossNetDonorMatcher([target, donor]);

        var result = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [Aggregate(donor.Id) with { IsExact = false }]);

        Assert.Equal(HistoricalGrossNetDonorTier.Fixed, result.Tier);
    }

    [Fact]
    public void ExactRational_RetainsScaleAndRoundsOnlyFinalFeeAwayFromZero()
    {
        var donor = Aggregate(Id(100), fee: "1.00000000", basis: "3.00000000");
        var match = DonorMatch(donor);

        var scale9 = HistoricalGrossNetFeeEstimator.Calculate(
            HistoricalGrossNetExactDecimal.Parse("0.000000015"),
            match,
            []);
        var scale16 = HistoricalGrossNetFeeEstimator.Calculate(
            HistoricalGrossNetExactDecimal.Parse("1.0000000050000000"),
            DonorMatch(Aggregate(Id(101), fee: "1", basis: "1")),
            []);

        Assert.Equal("0.00000001", scale9.TotalFee.ToString());
        Assert.Equal("1.00000001", scale16.TotalFee.ToString());
        Assert.Equal(HistoricalGrossNetParityCalculationSources.Donor, scale9.CalculationSource);
    }

    [Theory]
    [InlineData("1.234567891", "123.4500", "7.000000", "21.77248659")]
    [InlineData("0.1234567890123456", "1.234567890", "37.000000000000", "0.00411935")]
    public void ExactRational_DifferentNumeratorDenominatorScalesPreserveScaleNineToSixteenBasis(
        string basisText,
        string numeratorText,
        string denominatorText,
        string expected)
    {
        var basis = HistoricalGrossNetExactDecimal.Parse(basisText);
        var numerator = HistoricalGrossNetExactDecimal.Parse(numeratorText);
        var denominator = HistoricalGrossNetExactDecimal.Parse(denominatorText);
        var ratio = new HistoricalGrossNetExactRational(numerator, denominator);

        var result = ratio.MultiplyAndRound8(basis);

        Assert.InRange(basis.Scale, 9, 16);
        Assert.NotEqual(numerator.Scale, denominator.Scale);
        Assert.Equal(expected, result.ToString());
    }

    [Theory]
    [InlineData("10", "6.67000000")]
    [InlineData("-10", "-13.33000000")]
    public void FixedCoefficient_UsesBasisAndProducesApprovedExamples(string gross, string expectedNet)
    {
        var estimate = HistoricalGrossNetFeeEstimator.Calculate(
            HistoricalGrossNetExactDecimal.Parse("100"),
            FixedMatch(),
            []);

        Assert.Equal("3.33000000", estimate.TotalFee.ToString());
        Assert.Equal(expectedNet, estimate.ApplyToGross(HistoricalGrossNetExactDecimal.Parse(gross)).ToString());
        Assert.Equal(HistoricalGrossNetParityCalculationSources.Fixed, estimate.CalculationSource);
    }

    [Fact]
    public void ComponentFloor_DeduplicatesSameAllocationAndSumsDistinctSlicesOfOneCharge()
    {
        var components = new[]
        {
            Component("allocation-a", "coverage-a", "1.00000000"),
            Component("allocation-a", "coverage-a", "1.00000000"),
            Component("allocation-b", "coverage-b", "2.00000000")
        };

        var estimate = HistoricalGrossNetFeeEstimator.Calculate(
            HistoricalGrossNetExactDecimal.Parse("10"),
            FixedMatch(),
            components);

        Assert.Equal("0.33300000", estimate.BaseEstimatedFee.ToString());
        Assert.Equal("3.00000000", estimate.ProvedComponentFloor.ToString());
        Assert.Equal("3.00000000", estimate.TotalFee.ToString());
    }

    [Fact]
    public void ComponentFloor_RejectsAmbiguousOverlap()
    {
        var components = new[]
        {
            Component("allocation-a", "same-coverage", "1"),
            Component("allocation-b", "same-coverage", "1")
        };

        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetFeeEstimator.CalculateComponentFloor(components));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void NonpositiveBasis_BypassesDonorAndFixedButKeepsComponentFloor(string basis)
    {
        var estimate = HistoricalGrossNetFeeEstimator.Calculate(
            HistoricalGrossNetExactDecimal.Parse(basis),
            null,
            [Component("allocation", "coverage", "0.25000000")]);

        Assert.Equal("0.00000000", estimate.BaseEstimatedFee.ToString());
        Assert.Equal("0.25000000", estimate.TotalFee.ToString());
        Assert.Equal(HistoricalGrossNetParityCalculationSources.NonpositiveBasis, estimate.CalculationSource);
    }

    [Fact]
    public void ExactDecimal_AddMultiplyAndNegativeMidpointAreDeterministic()
    {
        var product = HistoricalGrossNetExactDecimal.Parse("0.12345678")
            .Multiply(HistoricalGrossNetExactDecimal.Parse("0.87654321"));
        var sum = HistoricalGrossNetExactDecimal.Parse("1.20")
            .Add(HistoricalGrossNetExactDecimal.Parse("0.003"));
        var roundedNegative = HistoricalGrossNetExactDecimal.Parse("-0.000000005")
            .RoundAwayFromZero(8);

        Assert.Equal(16, product.Scale);
        Assert.Equal("0.1082152022374638", product.ToString());
        Assert.Equal("1.203", sum.ToString());
        Assert.Equal("-0.00000001", roundedNegative.ToString());
    }

    [Fact]
    public void DonorHashV1_EmptyMembershipHasKnownAnswer()
    {
        var hash = HistoricalGrossNetDonorHashV1.ComputeMembershipHash([]);

        Assert.Equal(
            "194f4e918f294e79bf58c58affe58bb285f458b6b774b276bd53e4230277fc74",
            hash);
    }

    [Fact]
    public void DonorHashV1_ComponentEvidenceGraphKnownAnswersArePermutationInvariantAndAggregateOnly()
    {
        var componentRecords = ComponentEvidenceKnownAnswerRecords();
        var componentHash = HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash(componentRecords);
        var reversedComponentHash = HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash(
            componentRecords.Reverse());
        var membership = MembershipKnownAnswerRecords(componentHash);

        var forward = HistoricalGrossNetDonorHashV1.ComputeMembershipHash(membership);
        var reverse = HistoricalGrossNetDonorHashV1.ComputeMembershipHash(membership.Reverse());

        Assert.Equal(
            "15aa292181bb089791370c70a82a7205589a07caa3eb2d18d5c2b80bae2c8b56",
            componentHash);
        Assert.Equal(componentHash, reversedComponentHash);
        Assert.Equal(
            componentHash,
            HistoricalGrossNetDonorHashV1.ComputeComponentAllocationHash(componentRecords));
        Assert.Equal(
            "5a7967d8f5c0dc1bd5c25ef2fe572d7292fa9fa0268fb6c193118680fdb23d43",
            forward);
        Assert.Equal(forward, reverse);

        var entryAllocation = Assert.Single(componentRecords.Where(record =>
            record.RecordKind == HistoricalGrossNetComponentEvidenceRecordKind.EffectiveAllocation &&
            record.AllocationId == "paper-entry-allocation:sell-é"));
        var entryMovement = Assert.Single(componentRecords.Where(record =>
            record.RecordKind == HistoricalGrossNetComponentEvidenceRecordKind.PoolMovement));
        var buyCharges = componentRecords.Where(record =>
                record.RecordKind == HistoricalGrossNetComponentEvidenceRecordKind.SourceCharge &&
                record.SourceChargeId!.Contains(":buy-", StringComparison.Ordinal))
            .ToArray();
        var entryEdges = componentRecords.Where(record =>
                record.RecordKind == HistoricalGrossNetComponentEvidenceRecordKind.CoverageEdge &&
                record.AllocationId == entryAllocation.AllocationId)
            .ToArray();

        Assert.Equal(HashDecimal("1.23456789"), entryAllocation.Amount);
        Assert.Equal(HashDecimal("-0.00000001"), entryMovement.PoolRoundingResidual);
        Assert.Equal(2, buyCharges.Length);
        Assert.Equal(2, entryEdges.Length);
        Assert.All(buyCharges, charge =>
        {
            Assert.Null(charge.AllocationId);
            Assert.Null(charge.PoolAllocatedRaw);
            Assert.Null(charge.RemainingPool);
            Assert.Null(charge.PoolDecrement);
            Assert.Null(charge.PoolRoundingResidual);
        });
        Assert.All(entryEdges, edge =>
        {
            Assert.Null(edge.Amount);
            Assert.Null(edge.EvidenceVersion);
            Assert.Null(edge.PoolAllocatedRaw);
            Assert.Null(edge.RemainingPool);
            Assert.Null(edge.PoolDecrement);
            Assert.Null(edge.PoolRoundingResidual);
        });
    }

    [Fact]
    public void DonorHashV1_StreamingComponentBuilderMatchesMaterializedKnownAnswer()
    {
        var records = ComponentEvidenceKnownAnswerRecords();
        var ordered = records
            .Select(record => (Record: record, Encoded: HistoricalGrossNetDonorHashV1.EncodeComponentEvidence(record)))
            .OrderBy(
                value => value.Encoded,
                Comparer<HistoricalGrossNetDonorHashV1.EncodedComponentEvidence>.Create(
                    HistoricalGrossNetDonorHashV1.CompareComponentRecords))
            .Select(value => value.Record)
            .ToArray();

        using var builder = HistoricalGrossNetDonorHashV1.CreateComponentEvidenceHashBuilder(
            checked((uint)ordered.Length));
        foreach (var record in ordered)
        {
            builder.Append(record);
        }

        Assert.Equal(
            HistoricalGrossNetDonorHashV1.ComputeComponentAllocationHash(records),
            builder.Complete());
    }

    [Fact]
    public void DonorHashV1_ComponentEvidenceGraphRejectsClosedDomainAndPerChargeResidualSplit()
    {
        var undefinedKind = new HistoricalGrossNetComponentEvidenceRecordV1(
            (HistoricalGrossNetComponentEvidenceRecordKind)999,
            null, null, null, null, null, null, null, null);
        var foreignField = HistoricalGrossNetComponentEvidenceRecordV1.SourceCharge(
            "charge",
            HashDecimal("1")) with
        {
            AllocationId = "allocation"
        };
        var perChargeResidual = HistoricalGrossNetComponentEvidenceRecordV1.SourceCharge(
            "charge",
            HashDecimal("1")) with
        {
            PoolRoundingResidual = HashDecimal("0.00000001")
        };
        var negativeAmount = HistoricalGrossNetComponentEvidenceRecordV1.EffectiveAllocation(
            "allocation",
            HashDecimal("-0.00000001"));

        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash([undefinedKind]));
        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash([foreignField]));
        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash([perChargeResidual]));
        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash([negativeAmount]));
    }

    [Fact]
    public void DonorHashV1_ComponentEvidenceGraphPreservesDecimalScaleAndUnicodeBytes()
    {
        static IReadOnlyList<HistoricalGrossNetComponentEvidenceRecordV1> DirectGraph(
            string allocationId,
            HistoricalGrossNetHashDecimalV1 amount,
            string evidenceVersion) =>
        [
            HistoricalGrossNetComponentEvidenceRecordV1.EffectiveAllocation(
                allocationId,
                amount,
                evidenceVersion),
            HistoricalGrossNetComponentEvidenceRecordV1.SourceCharge(
                "charge",
                amount,
                evidenceVersion),
            HistoricalGrossNetComponentEvidenceRecordV1.CoverageEdge(allocationId, "charge")
        ];

        var scaleOne = DirectGraph("allocation", HashDecimal("1.0"), "v1");
        var scaleTwo = DirectGraph("allocation", HashDecimal("1.00"), "v1");
        var composed = DirectGraph("allocation-é", HashDecimal("1.0"), "é");
        var decomposed = DirectGraph("allocation-e\u0301", HashDecimal("1.0"), "e\u0301");

        Assert.NotEqual(
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash(scaleOne),
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash(scaleTwo));
        Assert.NotEqual(
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash(composed),
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash(decomposed));
    }

    [Fact]
    public void DonorHashV1_ComponentEvidenceGraphRejectsDuplicateMissingOverlapAndUnexplainedAmount()
    {
        var records = ComponentEvidenceKnownAnswerRecords();
        var duplicateAllocation = HistoricalGrossNetComponentEvidenceRecordV1.EffectiveAllocation(
            "paper-exit-allocation:sell-7",
            HashDecimal("0.05000000"));
        var duplicateCharge = HistoricalGrossNetComponentEvidenceRecordV1.SourceCharge(
            "paper-fill:buy-2:entry",
            HashDecimal("0.70000001"),
            "buy-v1");
        var duplicateEdge = HistoricalGrossNetComponentEvidenceRecordV1.CoverageEdge(
            "paper-entry-allocation:sell-é",
            "paper-fill:buy-2:entry");
        var missingEndpoint = HistoricalGrossNetComponentEvidenceRecordV1.CoverageEdge(
            "missing-allocation",
            "paper-fill:buy-2:entry");
        var overlappingEdge = HistoricalGrossNetComponentEvidenceRecordV1.CoverageEdge(
            "paper-exit-allocation:sell-7",
            "paper-fill:buy-2:entry");
        var unexplainedExit = records.Select(record =>
                record.RecordKind == HistoricalGrossNetComponentEvidenceRecordKind.EffectiveAllocation &&
                record.AllocationId == "paper-exit-allocation:sell-7"
                    ? record with { Amount = HashDecimal("0.06000000") }
                    : record)
            .ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash([.. records, duplicateAllocation]));
        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash([.. records, duplicateCharge]));
        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash([.. records, duplicateEdge]));
        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash([.. records, missingEndpoint]));
        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash([.. records, overlappingEdge]));
        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash(unexplainedExit));
    }

    [Fact]
    public void ComponentGraphV1_RejectsUncoveredChargeAndDuplicateAllocationAcrossComponents()
    {
        var chargeA = new HistoricalGrossNetParitySourceChargeV1("charge-a", 0.10m, "evidence-a", "{}");
        var chargeB = new HistoricalGrossNetParitySourceChargeV1("charge-b", 0.20m, "evidence-b", "{}");
        var edgeA = new HistoricalGrossNetParityChargeCoverageEdgeV1(
            "charge-a", "pool", "allocation", "edge-a", "{}");

        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetParityComponentGraphV1.Create(
                "allocation",
                0.30m,
                [chargeA, chargeB],
                [edgeA]));

        var directA = HistoricalGrossNetParityComponentGraphV1.Create(
            "duplicate-allocation",
            0.10m,
            [chargeA],
            [new HistoricalGrossNetParityChargeCoverageEdgeV1(
                "charge-a", "pool-a", "duplicate-allocation", "edge-a", "{}")] );
        var directB = HistoricalGrossNetParityComponentGraphV1.Create(
            "duplicate-allocation",
            0.20m,
            [chargeB],
            [new HistoricalGrossNetParityChargeCoverageEdgeV1(
                "charge-b", "pool-b", "duplicate-allocation", "edge-b", "{}")] );

        Assert.Throws<InvalidOperationException>(() =>
            HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash([directA, directB]));
    }

    [Fact]
    public void DonorHashV1_TimestampConvertsToUtcAndTruncatesToPostgresMicroseconds()
    {
        var componentHash = "15aa292181bb089791370c70a82a7205589a07caa3eb2d18d5c2b80bae2c8b56";
        var records = MembershipKnownAnswerRecords(componentHash);
        var first = records[0];
        var sameUtcMicrosecond = first with
        {
            CalculatedAt = DateTimeOffset.ParseExact(
                "2026-08-14T09:34:56.1234569+00:00",
                "O",
                CultureInfo.InvariantCulture)
        };

        Assert.Equal(
            HistoricalGrossNetDonorHashV1.ComputeMembershipHash(records),
            HistoricalGrossNetDonorHashV1.ComputeMembershipHash([sameUtcMicrosecond, records[1]]));
    }

    [Fact]
    public void DonorHashV1_DeclaredSourceIdDomainAndUnicodeBytesAreNotNormalized()
    {
        var componentHash = "194f4e918f294e79bf58c58affe58bb285f458b6b774b276bd53e4230277fc74";
        var template = MembershipKnownAnswerRecords(componentHash)[1];
        var sourceGuid = Id(7);
        var uuidDomain = template with { SourceId = HistoricalGrossNetDonorSourceIdV1.FromUuid(sourceGuid) };
        var stringDomain = template with
        {
            SourceId = HistoricalGrossNetDonorSourceIdV1.FromString(
                sourceGuid.ToString("D", CultureInfo.InvariantCulture))
        };
        var composed = template with { CalculationSource = "é" };
        var decomposed = template with { CalculationSource = "e\u0301" };

        Assert.NotEqual(
            HistoricalGrossNetDonorHashV1.ComputeMembershipHash([uuidDomain]),
            HistoricalGrossNetDonorHashV1.ComputeMembershipHash([stringDomain]));
        Assert.NotEqual(
            HistoricalGrossNetDonorHashV1.ComputeMembershipHash([composed]),
            HistoricalGrossNetDonorHashV1.ComputeMembershipHash([decomposed]));
    }

    [Fact]
    public void DonorHashV1_SelectionKnownAnswerCoversEveryTypedBoundaryAndMatcherOrder()
    {
        var nonempty = new HistoricalGrossNetDonorSelectionRecordV1(
            Id(2),
            MatcherOrder: 0,
            Tier: BigInteger.One,
            DistanceComponents:
            [
                new("finite", HistoricalGrossNetDonorHashValueV1.Decimal(HashDecimal("1.25"))),
                new("flag", HistoricalGrossNetDonorHashValueV1.Boolean(false)),
                new("enum", HistoricalGrossNetDonorHashValueV1.Enum("SameAssetExactFamily"))
            ],
            ExactDonorCount: new BigInteger(2),
            AggregateStake: HashDecimal("100.000"),
            N: HashDecimal("1.25"),
            D: HashDecimal("100.000"),
            MembershipHash: "5a7967d8f5c0dc1bd5c25ef2fe572d7292fa9fa0268fb6c193118680fdb23d43");
        var empty = new HistoricalGrossNetDonorSelectionRecordV1(
            Id(1),
            MatcherOrder: 1,
            Tier: new BigInteger(2),
            DistanceComponents:
            [
                new("nullable", HistoricalGrossNetDonorHashValueV1.Null()),
                new("distance", HistoricalGrossNetDonorHashValueV1.PositiveInfinity()),
                new("label", HistoricalGrossNetDonorHashValueV1.String("é"))
            ],
            ExactDonorCount: BigInteger.Zero,
            AggregateStake: HashDecimal("0"),
            N: HashDecimal("0"),
            D: HashDecimal("0"),
            MembershipHash: "194f4e918f294e79bf58c58affe58bb285f458b6b774b276bd53e4230277fc74");

        var forward = HistoricalGrossNetDonorHashV1.ComputeSelectionHash([nonempty, empty]);
        var reverse = HistoricalGrossNetDonorHashV1.ComputeSelectionHash([empty, nonempty]);

        Assert.Equal(
            "4ce2a051cc09b990ba68949a27f2ebc888a7ca4d9c340afa9a4819ab2e660b05",
            forward);
        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void OrderedCandidates_AreCompleteImmutableAndCarryTypedDistanceComponents()
    {
        var target = Variant(400, threshold: 2m) with { FixedLimitPrice = null };
        var near = Variant(401, threshold: 3m) with { FixedLimitPrice = 0.51m };
        var semantic = Variant(
            402,
            threshold: 2m,
            behavior: BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);
        var otherAsset = Variant(403, asset: "BTC", threshold: 2m);
        var matcher = new HistoricalGrossNetDonorMatcher([target, near, semantic, otherAsset]);

        var candidates = matcher.GetOrderedCandidates(new HistoricalGrossNetDonorTarget(target.Id));

        Assert.Equal(4, candidates.Count);
        Assert.Equal(target.Id, candidates[0].StrategyId);
        Assert.Equal((int)HistoricalGrossNetDonorTier.SameStrategy, (int)candidates[0].Tier);
        var nearCandidate = Assert.Single(candidates.Where(candidate => candidate.StrategyId == near.Id));
        Assert.Contains(
            nearCandidate.DistanceComponents,
            component => component.Name == "fixedLimitPriceDistance" &&
                component.Value.Kind == HistoricalGrossNetDonorHashValueKind.PositiveInfinity);
        Assert.Contains(
            nearCandidate.DistanceComponents,
            component => component.Name == "negativeAggregateExactDonorStake" &&
                component.Value.Kind == HistoricalGrossNetDonorHashValueKind.Null);
        Assert.Equal(
            new[]
            {
                "decisionThresholdBpsDistance",
                "decisionDepthDistance",
                "entryDelaySecondsDistance",
                "fixedLimitPriceDistance",
                "makerMinBestAskExclusiveDistance",
                "shiftDiffCountDistance",
                "fakMaximumOrderPriceDistance",
                "makerMaximumOrderPriceDistance",
                "negativeAggregateExactDonorStake",
                "negativeExactDonorCount",
                "donorDecisionThresholdBps",
                "donorDecisionDepth",
                "donorEntryDelaySeconds",
                "donorFixedLimitPrice",
                "donorMakerMinBestAskExclusive",
                "donorShiftDiffCount",
                "donorFakMaximumOrderPrice",
                "donorMakerMaximumOrderPrice"
            },
            nearCandidate.DistanceComponents.Select(component => component.Name).ToArray());
        var semanticCandidate = Assert.Single(candidates.Where(candidate => candidate.StrategyId == semantic.Id));
        Assert.Equal(
            new[]
            {
                "behaviorMismatch",
                "marketIntervalDurationSecondsDistance",
                "entryDelaySignClassMismatch",
                "semanticEntryDelaySecondsDistance",
                "directionMismatch",
                "preOpenLifetimeModeMismatch",
                "fixedOutcomeMismatch",
                "diffCounterTriggerOutcomeMismatch",
                "requiredReferenceAverageWindowNullMismatch",
                "requiredReferenceAverageWindowDurationSecondsDistance",
                "paperOnlyMismatch",
                "makerMinBestAskExclusivePresenceMismatch",
                "fakMaximumOrderPricePresenceMismatch",
                "makerMaximumOrderPricePresenceMismatch",
                "baseLinkDescriptorMismatch",
                "confirmationLinkDescriptorMismatch",
                "lowerEnterSourceLinkDescriptorMismatch"
            },
            semanticCandidate.DistanceComponents.Take(17).Select(component => component.Name).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(nearCandidate.CanonicalMatcherOrderKey));
        var list = Assert.IsAssignableFrom<IList<HistoricalGrossNetDonorCandidateDescriptorV1>>(candidates);
        Assert.Throws<NotSupportedException>(() => list.Add(candidates[0]));
    }

    [Fact]
    public void OrderedCandidates_NoncatalogTargetUsesDegradedSameAssetBeforeAnyCrypto()
    {
        var eth = Variant(410, asset: "ETH");
        var btc = Variant(411, asset: "BTC");
        var matcher = new HistoricalGrossNetDonorMatcher([btc, eth]);
        var targetId = Id(419);

        var knownAsset = matcher.GetOrderedCandidates(new HistoricalGrossNetDonorTarget(targetId, "eth"));
        var unknownAsset = matcher.GetOrderedCandidates(new HistoricalGrossNetDonorTarget(targetId));
        var strict = matcher.Match(
            new HistoricalGrossNetDonorTarget(targetId, "eth"),
            knownAsset,
            [
                Aggregate(eth.Id, basis: "1") with
                {
                    MembershipHashV1 =
                        "f1bfc87dfa5b69122a60d2699bf99fc9923eed43344f879629fd48364192631b"
                },
                Aggregate(btc.Id, basis: "1000000") with
                {
                    MembershipHashV1 =
                        "b6647a8635151067568a48ce1cfd81e118598b67938a18b94dd8e9c0ea4c71cc"
                }
            ]);

        Assert.Equal(
            new[]
            {
                HistoricalGrossNetDonorTier.SameStrategy,
                HistoricalGrossNetDonorTier.DegradedSameAsset,
                HistoricalGrossNetDonorTier.AnyCrypto
            },
            knownAsset.Select(candidate => (HistoricalGrossNetDonorTier)(int)candidate.Tier).ToArray());
        Assert.Equal(HistoricalGrossNetDonorTier.DegradedSameAsset, strict.Tier);
        Assert.Equal(eth.Id, strict.Donor?.StrategyId);
        Assert.Equal(targetId, Assert.Single(unknownAsset).StrategyId);
    }

    [Fact]
    public void StrictMatch_RequiresCompleteCandidatesAndHashesEveryInspectedEmptyAndWinnerCandidate()
    {
        var target = Variant(420, threshold: 2m);
        var low = Variant(421, threshold: 1m);
        var high = Variant(422, threshold: 3m);
        var other = Variant(
            423,
            asset: "BTC",
            behavior: BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);
        var matcher = new HistoricalGrossNetDonorMatcher([target, low, high, other]);
        var targetKey = new HistoricalGrossNetDonorTarget(target.Id);
        var candidates = matcher.GetOrderedCandidates(targetKey);
        var lowAggregate = Aggregate(low.Id, fee: "1", basis: "100", count: 1) with
        {
            MembershipHashV1 = "b6647a8635151067568a48ce1cfd81e118598b67938a18b94dd8e9c0ea4c71cc"
        };
        var highAggregate = Aggregate(high.Id, fee: "2", basis: "101", count: 2) with
        {
            MembershipHashV1 = "f1bfc87dfa5b69122a60d2699bf99fc9923eed43344f879629fd48364192631b"
        };

        var forward = matcher.Match(targetKey, candidates, [lowAggregate, highAggregate]);
        var reverse = matcher.Match(targetKey, candidates, [highAggregate, lowAggregate]);

        Assert.Equal(high.Id, forward.Donor?.StrategyId);
        Assert.Equal(3, forward.InspectedCandidateRecords.Count);
        Assert.DoesNotContain(
            forward.InspectedCandidateRecords,
            record => record.CandidateStrategyId == other.Id);
        Assert.All(
            forward.InspectedCandidateRecords.SelectMany(record => record.DistanceComponents)
                .Where(component => component.Name is
                    "negativeAggregateExactDonorStake" or "negativeExactDonorCount"),
            component => Assert.NotEqual(HistoricalGrossNetDonorHashValueKind.Null, component.Value.Kind));
        Assert.Equal(forward.SelectionHashV1, reverse.SelectionHashV1);
        Assert.Equal(
            HistoricalGrossNetDonorHashV1.ComputeSelectionHash(forward.InspectedCandidateRecords),
            forward.SelectionHashV1);
        Assert.Matches("^[0-9a-f]{64}$", Assert.IsType<string>(forward.SelectionHashV1));
        Assert.Throws<InvalidOperationException>(() =>
            matcher.Match(targetKey, candidates.Skip(1).ToArray(), [lowAggregate, highAggregate]));
        Assert.Throws<InvalidOperationException>(() =>
            matcher.Match(targetKey, candidates, [lowAggregate, Aggregate(Id(429))]));
    }

    [Fact]
    public void StrictMatch_UsesFixedOnlyAfterHashingEveryEmptyCandidateTier()
    {
        var target = Variant(430, asset: "ETH", threshold: 2m);
        var tier1 = Variant(431, asset: "ETH", threshold: 3m);
        var tier2 = Variant(
            432,
            asset: "ETH",
            behavior: BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);
        var tier3 = Variant(433, asset: "BTC", threshold: 2m);
        var tier4 = Variant(
            434,
            asset: "BTC",
            behavior: BtcUpDown5mStrategyBehavior.AlwaysUp);
        var matcher = new HistoricalGrossNetDonorMatcher([target, tier1, tier2, tier3, tier4]);
        var targetKey = new HistoricalGrossNetDonorTarget(target.Id);
        var candidates = matcher.GetOrderedCandidates(targetKey);

        var result = matcher.Match(targetKey, candidates, []);

        Assert.Equal(HistoricalGrossNetDonorTier.Fixed, result.Tier);
        Assert.Equal(candidates.Count, result.InspectedCandidateRecords.Count);
        Assert.All(result.InspectedCandidateRecords, record =>
        {
            Assert.Equal(BigInteger.Zero, record.ExactDonorCount);
            Assert.Equal(
                "194f4e918f294e79bf58c58affe58bb285f458b6b774b276bd53e4230277fc74",
                record.MembershipHash);
        });
        Assert.Matches("^[0-9a-f]{64}$", Assert.IsType<string>(result.SelectionHashV1));
        Assert.Throws<InvalidOperationException>(() =>
            matcher.Match(targetKey, candidates.Reverse().ToArray(), []));
    }

    [Fact]
    public void StrictMatch_AcceptsRawExactAndSelectedCountReduction()
    {
        var target = Variant(435);
        var matcher = new HistoricalGrossNetDonorMatcher([target]);
        var targetKey = new HistoricalGrossNetDonorTarget(target.Id);
        var candidates = matcher.GetOrderedCandidates(targetKey);
        var aggregate = new HistoricalGrossNetDonorAggregate(
            target.Id,
            HistoricalGrossNetExactDecimal.Parse("0.35"),
            HistoricalGrossNetExactDecimal.Parse("10"),
            ExactDonorCount: 2,
            RawDonorCount: 3,
            DeduplicatedDonorCount: 1,
            MembershipHashV1:
                "194f4e918f294e79bf58c58affe58bb285f458b6b774b276bd53e4230277fc74");

        var match = matcher.Match(targetKey, candidates, [aggregate]);

        Assert.Equal(target.Id, match.Donor?.StrategyId);
        Assert.Equal(BigInteger.One, Assert.Single(match.InspectedCandidateRecords).ExactDonorCount);
        Assert.Throws<InvalidOperationException>(() =>
            matcher.Match(targetKey, candidates, [aggregate with { RawDonorCount = 1 }]));
        Assert.Throws<InvalidOperationException>(() =>
            matcher.Match(targetKey, candidates, [aggregate with { DeduplicatedDonorCount = 3 }]));
    }

    private static IReadOnlyList<HistoricalGrossNetComponentEvidenceRecordV1>
        ComponentEvidenceKnownAnswerRecords() =>
        [
            HistoricalGrossNetComponentEvidenceRecordV1.EffectiveAllocation(
                "paper-entry-allocation:sell-é",
                HashDecimal("1.23456789"),
                "entry-v1"),
            HistoricalGrossNetComponentEvidenceRecordV1.SourceCharge(
                "paper-fill:buy-2:entry",
                HashDecimal("0.70000001"),
                "buy-v1"),
            HistoricalGrossNetComponentEvidenceRecordV1.SourceCharge(
                "paper-fill:buy-10:entry",
                HashDecimal("0.53456790"),
                "é"),
            HistoricalGrossNetComponentEvidenceRecordV1.CoverageEdge(
                "paper-entry-allocation:sell-é",
                "paper-fill:buy-2:entry"),
            HistoricalGrossNetComponentEvidenceRecordV1.CoverageEdge(
                "paper-entry-allocation:sell-é",
                "paper-fill:buy-10:entry"),
            HistoricalGrossNetComponentEvidenceRecordV1.PoolMovement(
                "paper-entry-allocation:sell-é",
                HashDecimal("1.234567895"),
                HashDecimal("0.76543211"),
                HashDecimal("1.23456790"),
                HashDecimal("-0.00000001")),
            HistoricalGrossNetComponentEvidenceRecordV1.EffectiveAllocation(
                "paper-exit-allocation:sell-7",
                HashDecimal("0.05000000")),
            HistoricalGrossNetComponentEvidenceRecordV1.SourceCharge(
                "paper-fill:sell-7:exit",
                HashDecimal("0.05000000"),
                "venue-v1"),
            HistoricalGrossNetComponentEvidenceRecordV1.CoverageEdge(
                "paper-exit-allocation:sell-7",
                "paper-fill:sell-7:exit")
        ];

    private static IReadOnlyList<HistoricalGrossNetDonorMembershipRecordV1> MembershipKnownAnswerRecords(
        string componentHash)
    {
        var calculatedAt = DateTimeOffset.ParseExact(
            "2026-08-14T12:34:56.1234567+03:00",
            "O",
            CultureInfo.InvariantCulture);
        return
        [
            new HistoricalGrossNetDonorMembershipRecordV1(
                EconomicDedupKey: "econ-prefix",
                SourceKind: HistoricalGrossNetParitySourceKind.PaperSellFill,
                SourceId: HistoricalGrossNetDonorSourceIdV1.FromString("legacy-ä"),
                AllocationId: null,
                RepresentationPrecedence: new BigInteger(300),
                ContributionKind: HistoricalGrossNetParityDonorContributionKind.ClosedRealized,
                Gross: HashDecimal("-25.00"),
                Basis: HashDecimal("100.000"),
                Fee: HashDecimal("1.25"),
                Net: HashDecimal("-26.25"),
                Status: "Calculated",
                CalculationSource: "точный-v1",
                EvidenceVersion: null,
                LiquidityRole: "Taker",
                FeeRate: HashDecimal("0.0200"),
                FeeExponent: new BigInteger(-2),
                FeeTakerOnly: true,
                CalculatedAt: calculatedAt,
                ComponentAllocationHash: componentHash),
            new HistoricalGrossNetDonorMembershipRecordV1(
                EconomicDedupKey: "econ-prefix",
                SourceKind: HistoricalGrossNetParitySourceKind.PaperSellFill,
                SourceId: HistoricalGrossNetDonorSourceIdV1.FromString("legacy-äx"),
                AllocationId: "alloc",
                RepresentationPrecedence: new BigInteger(100),
                ContributionKind: HistoricalGrossNetParityDonorContributionKind.ClosedRealized,
                Gross: HashDecimal("100"),
                Basis: HashDecimal("1000"),
                Fee: HashDecimal("1"),
                Net: HashDecimal("99"),
                Status: "VenueReported",
                CalculationSource: "source",
                EvidenceVersion: "v2",
                LiquidityRole: "Maker",
                FeeRate: null,
                FeeExponent: null,
                FeeTakerOnly: false,
                CalculatedAt: null,
                ComponentAllocationHash:
                    "194f4e918f294e79bf58c58affe58bb285f458b6b774b276bd53e4230277fc74")
        ];
    }

    private static HistoricalGrossNetHashDecimalV1 HashDecimal(string value)
    {
        var exact = HistoricalGrossNetExactDecimal.Parse(value);
        return new HistoricalGrossNetHashDecimalV1(exact.Significand, exact.Scale);
    }

    private static HistoricalGrossNetDonorAggregate Aggregate(
        Guid strategyId,
        string fee = "1",
        string basis = "100",
        int count = 1) =>
        new(
            strategyId,
            HistoricalGrossNetExactDecimal.Parse(fee),
            HistoricalGrossNetExactDecimal.Parse(basis),
            count);

    private static HistoricalGrossNetDonorMatch DonorMatch(HistoricalGrossNetDonorAggregate donor) =>
        new(
            HistoricalGrossNetDonorTier.SameStrategy,
            donor,
            null,
            null,
            []);

    private static HistoricalGrossNetDonorMatch FixedMatch() =>
        new(
            HistoricalGrossNetDonorTier.Fixed,
            null,
            null,
            null,
            []);

    private static HistoricalGrossNetProvedFeeComponent Component(
        string allocation,
        string coverage,
        string fee) =>
        new(allocation, coverage, HistoricalGrossNetExactDecimal.Parse(fee));

    private static void AssertNumericAndUuidTieBreaks(
        HistoricalGrossNetDonorMatcher matcher,
        BtcUpDown5mStrategyVariant target,
        BtcUpDown5mStrategyVariant lowNumeric,
        BtcUpDown5mStrategyVariant highNumeric,
        BtcUpDown5mStrategyVariant cloneA,
        BtcUpDown5mStrategyVariant cloneB,
        HistoricalGrossNetDonorTier expectedTier)
    {
        var numeric = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [Aggregate(highNumeric.Id), Aggregate(lowNumeric.Id)]);
        var uuid = matcher.Match(
            new HistoricalGrossNetDonorTarget(target.Id),
            [Aggregate(cloneB.Id), Aggregate(cloneA.Id)]);
        var expectedUuid = new[] { cloneA.Id, cloneB.Id }
            .OrderBy(id => id.ToString("D", CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .First();

        Assert.Equal(expectedTier, numeric.Tier);
        Assert.Equal(lowNumeric.Id, numeric.Donor?.StrategyId);
        Assert.Equal(expectedTier, uuid.Tier);
        Assert.Equal(expectedUuid, uuid.Donor?.StrategyId);
    }

    private static IEnumerable<IReadOnlyList<T>> Permutations<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0)
        {
            yield return Array.Empty<T>();
            yield break;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var remaining = items
                .Where((_, remainingIndex) => remainingIndex != index)
                .ToArray();
            foreach (var suffix in Permutations(remaining))
            {
                yield return [items[index], .. suffix];
            }
        }
    }

    private static BtcUpDown5mStrategyVariant WithLink(
        BtcUpDown5mStrategyVariant variant,
        LinkedSlot slot,
        Guid? linkedStrategyId) => slot switch
        {
            LinkedSlot.Base => variant with { BaseSignalStrategyId = linkedStrategyId },
            LinkedSlot.Confirmation => variant with { ConfirmationSignalStrategyId = linkedStrategyId },
            LinkedSlot.Lower => variant with { LowerEnterSourceStrategyId = linkedStrategyId },
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
        };

    private static BtcUpDown5mStrategyVariant Variant(
        int id,
        string asset = "ETH",
        decimal? threshold = 2m,
        BtcUpDown5mStrategyBehavior behavior = BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket,
        BtcUpDownMarketInterval interval = BtcUpDownMarketInterval.FiveMinutes) =>
        new(
            Id(id),
            "strategy_" + id.ToString(CultureInfo.InvariantCulture),
            "Strategy " + id.ToString(CultureInfo.InvariantCulture),
            "Description " + id.ToString(CultureInfo.InvariantCulture),
            BtcUpDown5mStrategyDirection.Dynamic,
            -30,
            behavior,
            threshold is null ? 0 : decimal.ToInt32(threshold.Value),
            threshold,
            interval,
            ReferenceAssetSymbol: asset);

    private static Guid Id(int value) => Guid.Parse(
        $"00000000-0000-0000-0000-{value.ToString("000000000000", CultureInfo.InvariantCulture)}");

    private enum LinkedSlot
    {
        Base,
        Confirmation,
        Lower
    }
}
