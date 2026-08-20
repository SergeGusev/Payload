using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

internal static class HistoricalGrossNetParityCalculationSources
{
    public const string Donor = "historical-gross-net-parity-donor-v1";
    public const string Fixed = "historical-gross-net-parity-fixed-0p0333-v1";
    public const string NonpositiveBasis = "historical-gross-net-parity-nonpositive-basis-v1";
}

internal enum HistoricalGrossNetDonorTier
{
    SameStrategy = 0,
    SameAssetExactFamily = 1,
    SameAssetSemantic = 2,
    OtherCryptoExactFamily = 3,
    AnyCrypto = 4,
    DegradedSameAsset = 5,
    Fixed = 6
}

internal sealed record HistoricalGrossNetDonorTarget(
    Guid StrategyId,
    string? ProvedCryptoAssetSymbol = null);

internal sealed record HistoricalGrossNetDonorAggregate(
    Guid StrategyId,
    HistoricalGrossNetExactDecimal ExactFeeNumerator,
    HistoricalGrossNetExactDecimal ExactBasisDenominator,
    int ExactDonorCount,
    bool IsExact = true,
    string? ProvedCryptoAssetSymbol = null,
    int? RawDonorCount = null,
    int? DeduplicatedDonorCount = null,
    string? MembershipHashV1 = null);

internal sealed record HistoricalGrossNetDonorMatch(
    HistoricalGrossNetDonorTier Tier,
    HistoricalGrossNetDonorAggregate? Donor,
    HistoricalGrossNetDonorDescriptor? TargetDescriptor,
    HistoricalGrossNetDonorDescriptor? DonorDescriptor,
    IReadOnlyList<string> ComparisonKey,
    IReadOnlyList<HistoricalGrossNetDonorSelectionRecordV1>? InspectedCandidates = null,
    string? SelectionHashV1 = null)
{
    public bool HasDonor => Donor is not null;

    public IReadOnlyList<HistoricalGrossNetDonorSelectionRecordV1> InspectedCandidateRecords =>
        InspectedCandidates ?? Array.Empty<HistoricalGrossNetDonorSelectionRecordV1>();

    public HistoricalGrossNetExactRational? Ratio => Donor is null
        ? null
        : new HistoricalGrossNetExactRational(
            Donor.ExactFeeNumerator,
            Donor.ExactBasisDenominator);
}

internal sealed record HistoricalGrossNetLinkedDescriptor(
    BtcUpDown5mStrategyBehavior Behavior,
    BtcUpDownMarketInterval MarketInterval,
    BtcUpDown5mStrategyDirection Direction,
    decimal? DecisionThresholdBps);

internal sealed record HistoricalGrossNetDonorFamily(
    BtcUpDown5mStrategyBehavior Behavior,
    BtcUpDownMarketInterval MarketInterval,
    BtcUpDown5mStrategyDirection Direction,
    HistoricalGrossNetEntryDelaySignClass EntryDelaySignClass,
    BtcUpDownPreOpenLifetimeMode PreOpenLifetimeMode,
    BtcUpDownFixedOutcome? FixedOutcome,
    BtcUpDownFixedOutcome? DiffCounterTriggerOutcome,
    int? RequiredReferenceAverageWindowSeconds,
    bool PaperOnly,
    bool HasMakerMinBestAskExclusive,
    bool HasFakMaximumOrderPrice,
    bool HasMakerMaximumOrderPrice,
    HistoricalGrossNetLinkedDescriptor? BaseSignal,
    HistoricalGrossNetLinkedDescriptor? ConfirmationSignal,
    HistoricalGrossNetLinkedDescriptor? LowerEnterSource);

internal sealed record HistoricalGrossNetDonorNumericVector(
    decimal? DecisionThresholdBps,
    int DecisionDepth,
    int EntryDelaySeconds,
    decimal? FixedLimitPrice,
    decimal? MakerMinBestAskExclusive,
    int ShiftDiffCount,
    decimal? FakMaximumOrderPrice,
    decimal? MakerMaximumOrderPrice);

internal sealed record HistoricalGrossNetDonorDescriptor(
    Guid StrategyId,
    string AssetSymbol,
    int MarketIntervalSeconds,
    HistoricalGrossNetDonorFamily Family,
    HistoricalGrossNetDonorNumericVector NumericVector);

internal enum HistoricalGrossNetEntryDelaySignClass
{
    Negative,
    Zero,
    Positive
}

internal readonly record struct HistoricalGrossNetDistance(bool IsPositiveInfinity, decimal Value)
    : IComparable<HistoricalGrossNetDistance>
{
    public static HistoricalGrossNetDistance Between(decimal? left, decimal? right)
    {
        if (left is null && right is null)
        {
            return new HistoricalGrossNetDistance(false, 0m);
        }

        if (left is null || right is null)
        {
            return new HistoricalGrossNetDistance(true, 0m);
        }

        return new HistoricalGrossNetDistance(false, Math.Abs(left.Value - right.Value));
    }

    public static HistoricalGrossNetDistance Between(int? left, int? right)
    {
        if (left is null && right is null)
        {
            return new HistoricalGrossNetDistance(false, 0m);
        }

        if (left is null || right is null)
        {
            return new HistoricalGrossNetDistance(true, 0m);
        }

        return new HistoricalGrossNetDistance(false, Math.Abs((decimal)left.Value - right.Value));
    }

    public int CompareTo(HistoricalGrossNetDistance other)
    {
        var infinityComparison = IsPositiveInfinity.CompareTo(other.IsPositiveInfinity);
        return infinityComparison != 0 ? infinityComparison : Value.CompareTo(other.Value);
    }

    public override string ToString() => IsPositiveInfinity
        ? "Infinity"
        : Value.ToString(CultureInfo.InvariantCulture);
}

internal sealed class HistoricalGrossNetDonorMatcher
{
    internal const string DistanceVersion = "HistoricalGrossNetDonorDistanceV1";

    private sealed record HistoricalGrossNetDonorCandidateDraft(
        Guid StrategyId,
        HistoricalGrossNetDonorTier Tier,
        HistoricalGrossNetDonorDescriptor? Descriptor,
        IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> DistanceComponents);

    private readonly IReadOnlyDictionary<Guid, HistoricalGrossNetDonorDescriptor> descriptors;

    public HistoricalGrossNetDonorMatcher(
        IReadOnlyList<BtcUpDown5mStrategyVariant>? catalog = null)
    {
        catalog ??= StrategyIds.UpDown5mStrategyVariants;
        ArgumentNullException.ThrowIfNull(catalog);

        var variantsById = new Dictionary<Guid, BtcUpDown5mStrategyVariant>();
        foreach (var variant in catalog)
        {
            ArgumentNullException.ThrowIfNull(variant);
            if (!variantsById.TryAdd(variant.Id, variant))
            {
                throw new InvalidOperationException(
                    $"{DistanceVersion} catalog contains duplicate strategy ID {CanonicalUuid(variant.Id)}.");
            }
        }

        descriptors = variantsById.Values.ToDictionary(
            variant => variant.Id,
            variant => CreateDescriptor(variant, variantsById));
    }

    public IReadOnlyList<HistoricalGrossNetDonorCandidateDescriptorV1> GetOrderedCandidates(
        HistoricalGrossNetDonorTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        descriptors.TryGetValue(target.StrategyId, out var targetDescriptor);
        var targetAsset = ResolveTargetAsset(target, targetDescriptor);
        var drafts = new List<HistoricalGrossNetDonorCandidateDraft>
        {
            CreateCandidateDraft(
                target.StrategyId,
                HistoricalGrossNetDonorTier.SameStrategy,
                targetDescriptor,
                [])
        };

        foreach (var donorDescriptor in descriptors.Values)
        {
            if (donorDescriptor.StrategyId == target.StrategyId)
            {
                continue;
            }

            HistoricalGrossNetDonorTier? tier = null;
            IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> components = [];
            if (targetDescriptor is not null)
            {
                if (string.Equals(donorDescriptor.AssetSymbol, targetAsset, StringComparison.Ordinal))
                {
                    if (donorDescriptor.Family == targetDescriptor.Family)
                    {
                        tier = HistoricalGrossNetDonorTier.SameAssetExactFamily;
                        components = CreateTier1DistanceComponents(targetDescriptor, donorDescriptor);
                    }
                    else
                    {
                        tier = HistoricalGrossNetDonorTier.SameAssetSemantic;
                        components = CreateTier2DistanceComponents(targetDescriptor, donorDescriptor);
                    }
                }
                else if (donorDescriptor.Family == targetDescriptor.Family)
                {
                    tier = HistoricalGrossNetDonorTier.OtherCryptoExactFamily;
                    components = CreateTier1DistanceComponents(targetDescriptor, donorDescriptor);
                }
                else
                {
                    tier = HistoricalGrossNetDonorTier.AnyCrypto;
                    components = CreateAggregateTieBreakPlaceholders();
                }
            }
            else if (targetAsset is not null)
            {
                tier = string.Equals(donorDescriptor.AssetSymbol, targetAsset, StringComparison.Ordinal)
                    ? HistoricalGrossNetDonorTier.DegradedSameAsset
                    : HistoricalGrossNetDonorTier.AnyCrypto;
                components = CreateAggregateTieBreakPlaceholders();
            }

            if (tier is not null)
            {
                drafts.Add(CreateCandidateDraft(
                    donorDescriptor.StrategyId,
                    tier.Value,
                    donorDescriptor,
                    components));
            }
        }

        drafts.Sort((left, right) => CompareCandidateDrafts(
            targetDescriptor,
            left,
            right,
            includeUuid: true));
        var result = new List<HistoricalGrossNetDonorCandidateDescriptorV1>(drafts.Count);
        var matcherOrder = -1;
        HistoricalGrossNetDonorCandidateDraft? previous = null;
        foreach (var draft in drafts)
        {
            if (previous is null || CompareCandidateDrafts(
                    targetDescriptor,
                    previous,
                    draft,
                    includeUuid: false) != 0)
            {
                matcherOrder++;
            }

            var canonicalKey =
                "tier-order:" + GetMatcherTierOrder(draft.Tier).ToString(CultureInfo.InvariantCulture) +
                ";components:" +
                HistoricalGrossNetDonorHashV1.EncodeDistanceComponentsKey(draft.DistanceComponents) +
                ";uuid:" + CanonicalUuid(draft.StrategyId);
            result.Add(new HistoricalGrossNetDonorCandidateDescriptorV1(
                draft.StrategyId,
                matcherOrder,
                new BigInteger((int)draft.Tier),
                draft.DistanceComponents,
                canonicalKey));
            previous = draft;
        }

        return new ReadOnlyCollection<HistoricalGrossNetDonorCandidateDescriptorV1>(result);
    }

    public HistoricalGrossNetDonorMatch Match(
        HistoricalGrossNetDonorTarget target,
        IReadOnlyCollection<HistoricalGrossNetDonorAggregate> donorAggregates)
    {
        ArgumentNullException.ThrowIfNull(target);
        return MatchCore(target, GetOrderedCandidates(target), donorAggregates, requireHashes: false);
    }

    public HistoricalGrossNetDonorMatch Match(
        HistoricalGrossNetDonorTarget target,
        IReadOnlyList<HistoricalGrossNetDonorCandidateDescriptorV1> completeCandidates,
        IReadOnlyCollection<HistoricalGrossNetDonorAggregate> donorAggregates)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(completeCandidates);
        ValidateCompleteCandidateList(target, completeCandidates);
        return MatchCore(target, completeCandidates, donorAggregates, requireHashes: true);
    }

    private HistoricalGrossNetDonorMatch MatchCore(
        HistoricalGrossNetDonorTarget target,
        IReadOnlyList<HistoricalGrossNetDonorCandidateDescriptorV1> completeCandidates,
        IReadOnlyCollection<HistoricalGrossNetDonorAggregate> donorAggregates,
        bool requireHashes)
    {
        ArgumentNullException.ThrowIfNull(donorAggregates);
        var candidateIds = completeCandidates.Select(candidate => candidate.StrategyId).ToHashSet();
        if (requireHashes)
        {
            var foreignDonor = donorAggregates.FirstOrDefault(donor => !candidateIds.Contains(donor.StrategyId));
            if (foreignDonor is not null)
            {
                throw new InvalidOperationException(
                    $"Donor aggregate {CanonicalUuid(foreignDonor.StrategyId)} is outside the complete candidate list.");
            }
        }

        var candidateDonorAggregates = requireHashes
            ? donorAggregates
            : donorAggregates.Where(donor => candidateIds.Contains(donor.StrategyId)).ToArray();
        var donors = ValidateAndFilterDonors(candidateDonorAggregates);
        var donorsByStrategy = donors.ToDictionary(donor => donor.StrategyId);
        var sharedSelection = requireHashes
            ? HistoricalGrossNetDonorSelectionV1.Evaluate(
                completeCandidates,
                CreateSharedSelectionAggregates(donors))
            : null;
        descriptors.TryGetValue(target.StrategyId, out var targetDescriptor);
        foreach (var tierGroup in completeCandidates.GroupBy(candidate => candidate.Tier))
        {
            var tier = (HistoricalGrossNetDonorTier)(int)tierGroup.Key;
            var tierCandidates = tierGroup.ToArray();
            var tierDonors = tierCandidates
                .Select(candidate => donorsByStrategy.GetValueOrDefault(candidate.StrategyId))
                .Where(donor => donor is not null)
                .Cast<HistoricalGrossNetDonorAggregate>()
                .ToArray();

            var selected = SelectTierDonor(tier, targetDescriptor, tierDonors);
            if (selected is not null)
            {
                ValidateSharedSelection(sharedSelection, selected.StrategyId, tier);
                descriptors.TryGetValue(selected.StrategyId, out var donorDescriptor);
                var comparisonKey = DescribeMatchKey(tier, targetDescriptor, selected, donorDescriptor);
                return CreateMatchWithSelection(
                    tier,
                    selected,
                    targetDescriptor,
                    donorDescriptor,
                    comparisonKey,
                    sharedSelection);
            }
        }

        ValidateSharedSelection(sharedSelection, null, HistoricalGrossNetDonorTier.Fixed);
        return CreateMatchWithSelection(
            HistoricalGrossNetDonorTier.Fixed,
            null,
            targetDescriptor,
            null,
            ["tier:fixed-0.0333"],
            sharedSelection);
    }

    private void ValidateCompleteCandidateList(
        HistoricalGrossNetDonorTarget target,
        IReadOnlyList<HistoricalGrossNetDonorCandidateDescriptorV1> candidates)
    {
        var expected = GetOrderedCandidates(target);
        if (expected.Count != candidates.Count)
        {
            throw new InvalidOperationException(
                $"The donor candidate list is incomplete: expected {expected.Count}, received {candidates.Count}.");
        }

        for (var index = 0; index < expected.Count; index++)
        {
            var expectedCandidate = expected[index];
            var actualCandidate = candidates[index] ?? throw new InvalidOperationException(
                $"Donor candidate {index} is null.");
            if (expectedCandidate.StrategyId != actualCandidate.StrategyId ||
                expectedCandidate.MatcherOrder != actualCandidate.MatcherOrder ||
                expectedCandidate.Tier != actualCandidate.Tier ||
                !string.Equals(
                    expectedCandidate.CanonicalMatcherOrderKey,
                    actualCandidate.CanonicalMatcherOrderKey,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    HistoricalGrossNetDonorHashV1.EncodeDistanceComponentsKey(
                        expectedCandidate.DistanceComponents),
                    HistoricalGrossNetDonorHashV1.EncodeDistanceComponentsKey(
                        actualCandidate.DistanceComponents),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Donor candidate {index} does not match {DistanceVersion}.");
            }
        }
    }

    private HistoricalGrossNetDonorAggregate? SelectTierDonor(
        HistoricalGrossNetDonorTier tier,
        HistoricalGrossNetDonorDescriptor? targetDescriptor,
        IReadOnlyCollection<HistoricalGrossNetDonorAggregate> donors) => tier switch
        {
            HistoricalGrossNetDonorTier.SameStrategy => donors.SingleOrDefault(),
            HistoricalGrossNetDonorTier.SameAssetExactFamily or
            HistoricalGrossNetDonorTier.OtherCryptoExactFamily when targetDescriptor is not null =>
                SelectBest(donors, (left, right) => CompareTier1(targetDescriptor, left, right)),
            HistoricalGrossNetDonorTier.SameAssetSemantic when targetDescriptor is not null =>
                SelectBest(donors, (left, right) => CompareTier2(targetDescriptor, left, right)),
            HistoricalGrossNetDonorTier.DegradedSameAsset or HistoricalGrossNetDonorTier.AnyCrypto =>
                SelectBest(donors, CompareTier4),
            HistoricalGrossNetDonorTier.SameAssetExactFamily or
            HistoricalGrossNetDonorTier.SameAssetSemantic or
            HistoricalGrossNetDonorTier.OtherCryptoExactFamily =>
                throw new InvalidOperationException($"Tier {tier} requires a complete target descriptor."),
            _ => throw new InvalidOperationException($"Candidate list contains unsupported tier {tier}.")
        };

    private IReadOnlyList<string> DescribeMatchKey(
        HistoricalGrossNetDonorTier tier,
        HistoricalGrossNetDonorDescriptor? targetDescriptor,
        HistoricalGrossNetDonorAggregate donor,
        HistoricalGrossNetDonorDescriptor? donorDescriptor) => tier switch
        {
            HistoricalGrossNetDonorTier.SameStrategy =>
                ["tier:0", "strategy:" + CanonicalUuid(donor.StrategyId)],
            HistoricalGrossNetDonorTier.SameAssetExactFamily or
            HistoricalGrossNetDonorTier.OtherCryptoExactFamily
                when targetDescriptor is not null && donorDescriptor is not null =>
                DescribeTier1Key(targetDescriptor, donor, donorDescriptor),
            HistoricalGrossNetDonorTier.SameAssetSemantic
                when targetDescriptor is not null && donorDescriptor is not null =>
                DescribeTier2Key(targetDescriptor, donor, donorDescriptor),
            HistoricalGrossNetDonorTier.DegradedSameAsset or HistoricalGrossNetDonorTier.AnyCrypto =>
                DescribeTier4Key(donor),
            _ => throw new InvalidOperationException(
                $"Cannot describe donor {CanonicalUuid(donor.StrategyId)} in tier {tier}.")
        };

    private static HistoricalGrossNetHashDecimalV1 ToHashDecimal(
        HistoricalGrossNetExactDecimal value) => new(value.Significand, value.Scale);

    private static IReadOnlyDictionary<Guid, HistoricalGrossNetDonorSelectionAggregateV1>
        CreateSharedSelectionAggregates(IReadOnlyCollection<HistoricalGrossNetDonorAggregate> donors) =>
        donors.ToDictionary(
            donor => donor.StrategyId,
            donor => new HistoricalGrossNetDonorSelectionAggregateV1(
                donor.StrategyId,
                new BigInteger(GetSelectedDonorCount(donor)),
                ToHashDecimal(donor.ExactBasisDenominator),
                ToHashDecimal(donor.ExactFeeNumerator),
                ToHashDecimal(donor.ExactBasisDenominator),
                RequireLowercaseSha256(donor.MembershipHashV1, donor.StrategyId)));

    private static void ValidateSharedSelection(
        HistoricalGrossNetDonorSelectionEvaluationV1? selection,
        Guid? expectedStrategyId,
        HistoricalGrossNetDonorTier expectedTier)
    {
        if (selection is null)
        {
            return;
        }

        BigInteger? expectedTierValue = expectedStrategyId is null
            ? null
            : new BigInteger((int)expectedTier);
        if (selection.SelectedStrategyId != expectedStrategyId || selection.SelectedTier != expectedTierValue)
        {
            throw new InvalidOperationException(
                "Shared donor selection ordering disagrees with HistoricalGrossNetDonorDistanceV1.");
        }
    }

    private static string RequireLowercaseSha256(string? value, Guid strategyId)
    {
        if (value is null ||
            value.Length != 64 ||
            value.Any(character =>
                !char.IsAsciiDigit(character) && character is not (>= 'a' and <= 'f')))
        {
            throw new InvalidOperationException(
                $"Exact donor aggregate {CanonicalUuid(strategyId)} lacks a lowercase MembershipHashV1.");
        }

        return value;
    }

    private static HistoricalGrossNetDonorCandidateDraft CreateCandidateDraft(
        Guid strategyId,
        HistoricalGrossNetDonorTier tier,
        HistoricalGrossNetDonorDescriptor? descriptor,
        IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> components) =>
        new(
            strategyId,
            tier,
            descriptor,
            new ReadOnlyCollection<HistoricalGrossNetDonorDistanceComponentV1>(components.ToArray()));

    private static int GetMatcherTierOrder(HistoricalGrossNetDonorTier tier) => tier switch
    {
        HistoricalGrossNetDonorTier.SameStrategy => 0,
        HistoricalGrossNetDonorTier.SameAssetExactFamily => 1,
        HistoricalGrossNetDonorTier.SameAssetSemantic => 2,
        HistoricalGrossNetDonorTier.OtherCryptoExactFamily => 3,
        HistoricalGrossNetDonorTier.DegradedSameAsset => 4,
        HistoricalGrossNetDonorTier.AnyCrypto => 5,
        HistoricalGrossNetDonorTier.Fixed => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null)
    };

    private int CompareCandidateDrafts(
        HistoricalGrossNetDonorDescriptor? targetDescriptor,
        HistoricalGrossNetDonorCandidateDraft left,
        HistoricalGrossNetDonorCandidateDraft right,
        bool includeUuid)
    {
        var comparison = GetMatcherTierOrder(left.Tier).CompareTo(GetMatcherTierOrder(right.Tier));
        if (comparison != 0)
        {
            return comparison;
        }

        if (left.Tier != right.Tier)
        {
            throw new InvalidOperationException("Two donor tiers share one matcher order.");
        }

        comparison = left.Tier switch
        {
            HistoricalGrossNetDonorTier.SameStrategy => 0,
            HistoricalGrossNetDonorTier.SameAssetExactFamily or
            HistoricalGrossNetDonorTier.OtherCryptoExactFamily
                when targetDescriptor is not null && left.Descriptor is not null && right.Descriptor is not null =>
                FirstNonzero(
                    CompareDistanceVectors(
                        CreateNumericDistanceVector(targetDescriptor.NumericVector, left.Descriptor.NumericVector),
                        CreateNumericDistanceVector(targetDescriptor.NumericVector, right.Descriptor.NumericVector)),
                    CompareNumericVectors(left.Descriptor.NumericVector, right.Descriptor.NumericVector)),
            HistoricalGrossNetDonorTier.SameAssetSemantic
                when targetDescriptor is not null && left.Descriptor is not null && right.Descriptor is not null =>
                FirstNonzero(
                    CompareTier2SemanticKey(targetDescriptor, left.Descriptor, right.Descriptor),
                    CompareDistanceVectors(
                        CreateNumericDistanceVector(targetDescriptor.NumericVector, left.Descriptor.NumericVector),
                        CreateNumericDistanceVector(targetDescriptor.NumericVector, right.Descriptor.NumericVector)),
                    CompareNumericVectors(left.Descriptor.NumericVector, right.Descriptor.NumericVector)),
            HistoricalGrossNetDonorTier.DegradedSameAsset or HistoricalGrossNetDonorTier.AnyCrypto => 0,
            _ => throw new InvalidOperationException($"Cannot order candidate tier {left.Tier}.")
        };
        if (comparison != 0 || !includeUuid)
        {
            return comparison;
        }

        return CompareUuid(left.StrategyId, right.StrategyId);
    }

    private static HistoricalGrossNetDonorMatch CreateMatchWithSelection(
        HistoricalGrossNetDonorTier tier,
        HistoricalGrossNetDonorAggregate? donor,
        HistoricalGrossNetDonorDescriptor? targetDescriptor,
        HistoricalGrossNetDonorDescriptor? donorDescriptor,
        IReadOnlyList<string> comparisonKey,
        HistoricalGrossNetDonorSelectionEvaluationV1? selection)
    {
        if (selection is null)
        {
            return CreateMatch(tier, donor, targetDescriptor, donorDescriptor, comparisonKey);
        }

        var frozen = new ReadOnlyCollection<HistoricalGrossNetDonorSelectionRecordV1>(
            selection.InspectedRecords.ToArray());
        return new HistoricalGrossNetDonorMatch(
            tier,
            donor,
            targetDescriptor,
            donorDescriptor,
            comparisonKey,
            frozen,
            selection.SelectionHashV1);
    }

    private static IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> CreateTier1DistanceComponents(
        HistoricalGrossNetDonorDescriptor target,
        HistoricalGrossNetDonorDescriptor donor) =>
        FreezeComponents(
        [
            .. CreateNumericDistanceComponents(target.NumericVector, donor.NumericVector),
            .. CreateAggregateTieBreakPlaceholders(),
            .. CreateDonorNumericVectorComponents(donor.NumericVector)
        ]);

    private static IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> CreateTier2DistanceComponents(
        HistoricalGrossNetDonorDescriptor target,
        HistoricalGrossNetDonorDescriptor donor)
    {
        var targetFamily = target.Family;
        var donorFamily = donor.Family;
        return FreezeComponents(
        [
            IntegerComponent("behaviorMismatch", Mismatch(targetFamily.Behavior, donorFamily.Behavior)),
            IntegerDistanceComponent(
                "marketIntervalDurationSecondsDistance",
                HistoricalGrossNetDistance.Between(target.MarketIntervalSeconds, donor.MarketIntervalSeconds)),
            IntegerComponent(
                "entryDelaySignClassMismatch",
                Mismatch(targetFamily.EntryDelaySignClass, donorFamily.EntryDelaySignClass)),
            IntegerDistanceComponent(
                "semanticEntryDelaySecondsDistance",
                HistoricalGrossNetDistance.Between(
                    target.NumericVector.EntryDelaySeconds,
                    donor.NumericVector.EntryDelaySeconds)),
            IntegerComponent("directionMismatch", Mismatch(targetFamily.Direction, donorFamily.Direction)),
            IntegerComponent(
                "preOpenLifetimeModeMismatch",
                Mismatch(targetFamily.PreOpenLifetimeMode, donorFamily.PreOpenLifetimeMode)),
            IntegerComponent("fixedOutcomeMismatch", Mismatch(targetFamily.FixedOutcome, donorFamily.FixedOutcome)),
            IntegerComponent(
                "diffCounterTriggerOutcomeMismatch",
                Mismatch(targetFamily.DiffCounterTriggerOutcome, donorFamily.DiffCounterTriggerOutcome)),
            IntegerComponent(
                "requiredReferenceAverageWindowNullMismatch",
                NullMismatch(
                    targetFamily.RequiredReferenceAverageWindowSeconds,
                    donorFamily.RequiredReferenceAverageWindowSeconds)),
            IntegerDistanceComponent(
                "requiredReferenceAverageWindowDurationSecondsDistance",
                HistoricalGrossNetDistance.Between(
                    targetFamily.RequiredReferenceAverageWindowSeconds,
                    donorFamily.RequiredReferenceAverageWindowSeconds)),
            IntegerComponent("paperOnlyMismatch", Mismatch(targetFamily.PaperOnly, donorFamily.PaperOnly)),
            IntegerComponent(
                "makerMinBestAskExclusivePresenceMismatch",
                Mismatch(
                    targetFamily.HasMakerMinBestAskExclusive,
                    donorFamily.HasMakerMinBestAskExclusive)),
            IntegerComponent(
                "fakMaximumOrderPricePresenceMismatch",
                Mismatch(targetFamily.HasFakMaximumOrderPrice, donorFamily.HasFakMaximumOrderPrice)),
            IntegerComponent(
                "makerMaximumOrderPricePresenceMismatch",
                Mismatch(targetFamily.HasMakerMaximumOrderPrice, donorFamily.HasMakerMaximumOrderPrice)),
            IntegerComponent("baseLinkDescriptorMismatch", Mismatch(targetFamily.BaseSignal, donorFamily.BaseSignal)),
            IntegerComponent(
                "confirmationLinkDescriptorMismatch",
                Mismatch(targetFamily.ConfirmationSignal, donorFamily.ConfirmationSignal)),
            IntegerComponent(
                "lowerEnterSourceLinkDescriptorMismatch",
                Mismatch(targetFamily.LowerEnterSource, donorFamily.LowerEnterSource)),
            .. CreateNumericDistanceComponents(target.NumericVector, donor.NumericVector),
            .. CreateAggregateTieBreakPlaceholders(),
            .. CreateDonorNumericVectorComponents(donor.NumericVector)
        ]);
    }

    private static IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> CreateNumericDistanceComponents(
        HistoricalGrossNetDonorNumericVector target,
        HistoricalGrossNetDonorNumericVector donor) =>
        FreezeComponents(
        [
            DecimalDistanceComponent(
                "decisionThresholdBpsDistance",
                HistoricalGrossNetDistance.Between(target.DecisionThresholdBps, donor.DecisionThresholdBps)),
            IntegerDistanceComponent(
                "decisionDepthDistance",
                HistoricalGrossNetDistance.Between(target.DecisionDepth, donor.DecisionDepth)),
            IntegerDistanceComponent(
                "entryDelaySecondsDistance",
                HistoricalGrossNetDistance.Between(target.EntryDelaySeconds, donor.EntryDelaySeconds)),
            DecimalDistanceComponent(
                "fixedLimitPriceDistance",
                HistoricalGrossNetDistance.Between(target.FixedLimitPrice, donor.FixedLimitPrice)),
            DecimalDistanceComponent(
                "makerMinBestAskExclusiveDistance",
                HistoricalGrossNetDistance.Between(
                    target.MakerMinBestAskExclusive,
                    donor.MakerMinBestAskExclusive)),
            IntegerDistanceComponent(
                "shiftDiffCountDistance",
                HistoricalGrossNetDistance.Between(target.ShiftDiffCount, donor.ShiftDiffCount)),
            DecimalDistanceComponent(
                "fakMaximumOrderPriceDistance",
                HistoricalGrossNetDistance.Between(target.FakMaximumOrderPrice, donor.FakMaximumOrderPrice)),
            DecimalDistanceComponent(
                "makerMaximumOrderPriceDistance",
                HistoricalGrossNetDistance.Between(
                    target.MakerMaximumOrderPrice,
                    donor.MakerMaximumOrderPrice))
        ]);

    private static IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> CreateAggregateTieBreakPlaceholders() =>
        FreezeComponents(
        [
            new HistoricalGrossNetDonorDistanceComponentV1(
                "negativeAggregateExactDonorStake",
                HistoricalGrossNetDonorHashValueV1.Null()),
            new HistoricalGrossNetDonorDistanceComponentV1(
                "negativeExactDonorCount",
                HistoricalGrossNetDonorHashValueV1.Null())
        ]);

    private static IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> CreateDonorNumericVectorComponents(
        HistoricalGrossNetDonorNumericVector donor) =>
        FreezeComponents(
        [
            NullableDecimalComponent("donorDecisionThresholdBps", donor.DecisionThresholdBps),
            IntegerComponent("donorDecisionDepth", donor.DecisionDepth),
            IntegerComponent("donorEntryDelaySeconds", donor.EntryDelaySeconds),
            NullableDecimalComponent("donorFixedLimitPrice", donor.FixedLimitPrice),
            NullableDecimalComponent("donorMakerMinBestAskExclusive", donor.MakerMinBestAskExclusive),
            IntegerComponent("donorShiftDiffCount", donor.ShiftDiffCount),
            NullableDecimalComponent("donorFakMaximumOrderPrice", donor.FakMaximumOrderPrice),
            NullableDecimalComponent("donorMakerMaximumOrderPrice", donor.MakerMaximumOrderPrice)
        ]);

    private static HistoricalGrossNetDonorDistanceComponentV1 IntegerComponent(string name, int value) =>
        new(name, HistoricalGrossNetDonorHashValueV1.Integer(new BigInteger(value)));

    private static HistoricalGrossNetDonorDistanceComponentV1 IntegerDistanceComponent(
        string name,
        HistoricalGrossNetDistance distance)
    {
        if (distance.IsPositiveInfinity)
        {
            return new HistoricalGrossNetDonorDistanceComponentV1(
                name,
                HistoricalGrossNetDonorHashValueV1.PositiveInfinity());
        }

        if (decimal.Truncate(distance.Value) != distance.Value)
        {
            throw new InvalidOperationException($"Integer distance '{name}' is not integral.");
        }

        return new HistoricalGrossNetDonorDistanceComponentV1(
            name,
            HistoricalGrossNetDonorHashValueV1.Integer(new BigInteger(distance.Value)));
    }

    private static HistoricalGrossNetDonorDistanceComponentV1 DecimalDistanceComponent(
        string name,
        HistoricalGrossNetDistance distance) =>
        new(
            name,
            distance.IsPositiveInfinity
                ? HistoricalGrossNetDonorHashValueV1.PositiveInfinity()
                : HistoricalGrossNetDonorHashValueV1.Decimal(distance.Value));

    private static HistoricalGrossNetDonorDistanceComponentV1 NullableDecimalComponent(
        string name,
        decimal? value) =>
        new(
            name,
            value is null
                ? HistoricalGrossNetDonorHashValueV1.Null()
                : HistoricalGrossNetDonorHashValueV1.Decimal(value.Value));

    private static IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> FreezeComponents(
        IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> components) =>
        new ReadOnlyCollection<HistoricalGrossNetDonorDistanceComponentV1>(components.ToArray());

    private IReadOnlyList<HistoricalGrossNetDonorAggregate> ValidateAndFilterDonors(
        IReadOnlyCollection<HistoricalGrossNetDonorAggregate> donorAggregates)
    {
        var strategyIds = new HashSet<Guid>();
        var exact = new List<HistoricalGrossNetDonorAggregate>(donorAggregates.Count);
        foreach (var donor in donorAggregates)
        {
            ArgumentNullException.ThrowIfNull(donor);
            if (!strategyIds.Add(donor.StrategyId))
            {
                throw new InvalidOperationException(
                    $"Donor aggregates contain duplicate strategy {CanonicalUuid(donor.StrategyId)}.");
            }

            if (!donor.IsExact)
            {
                continue;
            }

            if (donor.ExactDonorCount < 0)
            {
                throw new InvalidOperationException(
                    $"Exact donor aggregate for strategy {CanonicalUuid(donor.StrategyId)} has a negative count.");
            }

            var selectedDonorCount = GetSelectedDonorCount(donor);
            if (selectedDonorCount == 0)
            {
                if (donor.ExactBasisDenominator.CompareTo(HistoricalGrossNetExactDecimal.Zero) != 0 ||
                    donor.ExactFeeNumerator.CompareTo(HistoricalGrossNetExactDecimal.Zero) != 0)
                {
                    throw new InvalidOperationException(
                        $"Empty donor aggregate for strategy {CanonicalUuid(donor.StrategyId)} is not zero.");
                }

                continue;
            }

            if (
                donor.ExactBasisDenominator.CompareTo(HistoricalGrossNetExactDecimal.Zero) <= 0 ||
                donor.ExactFeeNumerator.CompareTo(HistoricalGrossNetExactDecimal.Zero) < 0)
            {
                throw new InvalidOperationException(
                    $"Exact donor aggregate for strategy {CanonicalUuid(donor.StrategyId)} is invalid.");
            }

            if (donor.RawDonorCount is < 0 ||
                donor.DeduplicatedDonorCount is < 0 ||
                donor.RawDonorCount is { } rawCount && rawCount < donor.ExactDonorCount ||
                donor.DeduplicatedDonorCount is { } deduplicatedCount &&
                deduplicatedCount > donor.ExactDonorCount)
            {
                throw new InvalidOperationException(
                    $"Donor counts for strategy {CanonicalUuid(donor.StrategyId)} are inconsistent.");
            }

            if (descriptors.TryGetValue(donor.StrategyId, out var descriptor) &&
                donor.ProvedCryptoAssetSymbol is { } suppliedAsset &&
                !string.Equals(
                    descriptor.AssetSymbol,
                    NormalizeProvedAsset(suppliedAsset),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Catalog and proved asset disagree for strategy {CanonicalUuid(donor.StrategyId)}.");
            }

            exact.Add(donor);
        }

        return exact;
    }

    private static int GetSelectedDonorCount(HistoricalGrossNetDonorAggregate donor) =>
        donor.DeduplicatedDonorCount ?? donor.ExactDonorCount;

    private string? ResolveTargetAsset(
        HistoricalGrossNetDonorTarget target,
        HistoricalGrossNetDonorDescriptor? targetDescriptor)
    {
        if (targetDescriptor is null)
        {
            return target.ProvedCryptoAssetSymbol is null
                ? null
                : NormalizeProvedAsset(target.ProvedCryptoAssetSymbol);
        }

        if (target.ProvedCryptoAssetSymbol is { } suppliedAsset &&
            !string.Equals(
                targetDescriptor.AssetSymbol,
                NormalizeProvedAsset(suppliedAsset),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Catalog and proved asset disagree for target {CanonicalUuid(target.StrategyId)}.");
        }

        return targetDescriptor.AssetSymbol;
    }

    private string? ResolveDonorAsset(HistoricalGrossNetDonorAggregate donor)
    {
        if (descriptors.TryGetValue(donor.StrategyId, out var descriptor))
        {
            return descriptor.AssetSymbol;
        }

        return donor.ProvedCryptoAssetSymbol is null
            ? null
            : NormalizeProvedAsset(donor.ProvedCryptoAssetSymbol);
    }

    private bool TryGetDescriptor(Guid strategyId, out HistoricalGrossNetDonorDescriptor descriptor) =>
        descriptors.TryGetValue(strategyId, out descriptor!);

    private int CompareTier1(
        HistoricalGrossNetDonorDescriptor target,
        HistoricalGrossNetDonorAggregate left,
        HistoricalGrossNetDonorAggregate right)
    {
        var leftDescriptor = descriptors[left.StrategyId];
        var rightDescriptor = descriptors[right.StrategyId];
        var comparison = CompareDistanceVectors(
            CreateNumericDistanceVector(target.NumericVector, leftDescriptor.NumericVector),
            CreateNumericDistanceVector(target.NumericVector, rightDescriptor.NumericVector));
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareAggregateTieBreak(left, right);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareNumericVectors(leftDescriptor.NumericVector, rightDescriptor.NumericVector);
        return comparison != 0 ? comparison : CompareUuid(left.StrategyId, right.StrategyId);
    }

    private int CompareTier2(
        HistoricalGrossNetDonorDescriptor target,
        HistoricalGrossNetDonorAggregate left,
        HistoricalGrossNetDonorAggregate right)
    {
        var leftDescriptor = descriptors[left.StrategyId];
        var rightDescriptor = descriptors[right.StrategyId];
        var comparison = CompareTier2SemanticKey(target, leftDescriptor, rightDescriptor);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareDistanceVectors(
            CreateNumericDistanceVector(target.NumericVector, leftDescriptor.NumericVector),
            CreateNumericDistanceVector(target.NumericVector, rightDescriptor.NumericVector));
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareAggregateTieBreak(left, right);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareNumericVectors(leftDescriptor.NumericVector, rightDescriptor.NumericVector);
        return comparison != 0 ? comparison : CompareUuid(left.StrategyId, right.StrategyId);
    }

    private static int CompareTier2SemanticKey(
        HistoricalGrossNetDonorDescriptor target,
        HistoricalGrossNetDonorDescriptor left,
        HistoricalGrossNetDonorDescriptor right)
    {
        var targetFamily = target.Family;
        var leftFamily = left.Family;
        var rightFamily = right.Family;

        return FirstNonzero(
            Mismatch(targetFamily.Behavior, leftFamily.Behavior)
                .CompareTo(Mismatch(targetFamily.Behavior, rightFamily.Behavior)),
            HistoricalGrossNetDistance.Between(target.MarketIntervalSeconds, left.MarketIntervalSeconds)
                .CompareTo(HistoricalGrossNetDistance.Between(
                    target.MarketIntervalSeconds,
                    right.MarketIntervalSeconds)),
            Mismatch(targetFamily.EntryDelaySignClass, leftFamily.EntryDelaySignClass)
                .CompareTo(Mismatch(targetFamily.EntryDelaySignClass, rightFamily.EntryDelaySignClass)),
            HistoricalGrossNetDistance.Between(
                    target.NumericVector.EntryDelaySeconds,
                    left.NumericVector.EntryDelaySeconds)
                .CompareTo(HistoricalGrossNetDistance.Between(
                    target.NumericVector.EntryDelaySeconds,
                    right.NumericVector.EntryDelaySeconds)),
            Mismatch(targetFamily.Direction, leftFamily.Direction)
                .CompareTo(Mismatch(targetFamily.Direction, rightFamily.Direction)),
            Mismatch(targetFamily.PreOpenLifetimeMode, leftFamily.PreOpenLifetimeMode)
                .CompareTo(Mismatch(targetFamily.PreOpenLifetimeMode, rightFamily.PreOpenLifetimeMode)),
            Mismatch(targetFamily.FixedOutcome, leftFamily.FixedOutcome)
                .CompareTo(Mismatch(targetFamily.FixedOutcome, rightFamily.FixedOutcome)),
            Mismatch(targetFamily.DiffCounterTriggerOutcome, leftFamily.DiffCounterTriggerOutcome)
                .CompareTo(Mismatch(targetFamily.DiffCounterTriggerOutcome, rightFamily.DiffCounterTriggerOutcome)),
            NullMismatch(
                    targetFamily.RequiredReferenceAverageWindowSeconds,
                    leftFamily.RequiredReferenceAverageWindowSeconds)
                .CompareTo(NullMismatch(
                    targetFamily.RequiredReferenceAverageWindowSeconds,
                    rightFamily.RequiredReferenceAverageWindowSeconds)),
            HistoricalGrossNetDistance.Between(
                    targetFamily.RequiredReferenceAverageWindowSeconds,
                    leftFamily.RequiredReferenceAverageWindowSeconds)
                .CompareTo(HistoricalGrossNetDistance.Between(
                    targetFamily.RequiredReferenceAverageWindowSeconds,
                    rightFamily.RequiredReferenceAverageWindowSeconds)),
            Mismatch(targetFamily.PaperOnly, leftFamily.PaperOnly)
                .CompareTo(Mismatch(targetFamily.PaperOnly, rightFamily.PaperOnly)),
            Mismatch(targetFamily.HasMakerMinBestAskExclusive, leftFamily.HasMakerMinBestAskExclusive)
                .CompareTo(Mismatch(targetFamily.HasMakerMinBestAskExclusive, rightFamily.HasMakerMinBestAskExclusive)),
            Mismatch(targetFamily.HasFakMaximumOrderPrice, leftFamily.HasFakMaximumOrderPrice)
                .CompareTo(Mismatch(targetFamily.HasFakMaximumOrderPrice, rightFamily.HasFakMaximumOrderPrice)),
            Mismatch(targetFamily.HasMakerMaximumOrderPrice, leftFamily.HasMakerMaximumOrderPrice)
                .CompareTo(Mismatch(targetFamily.HasMakerMaximumOrderPrice, rightFamily.HasMakerMaximumOrderPrice)),
            Mismatch(targetFamily.BaseSignal, leftFamily.BaseSignal)
                .CompareTo(Mismatch(targetFamily.BaseSignal, rightFamily.BaseSignal)),
            Mismatch(targetFamily.ConfirmationSignal, leftFamily.ConfirmationSignal)
                .CompareTo(Mismatch(targetFamily.ConfirmationSignal, rightFamily.ConfirmationSignal)),
            Mismatch(targetFamily.LowerEnterSource, leftFamily.LowerEnterSource)
                .CompareTo(Mismatch(targetFamily.LowerEnterSource, rightFamily.LowerEnterSource)));
    }

    private static int CompareTier4(
        HistoricalGrossNetDonorAggregate left,
        HistoricalGrossNetDonorAggregate right)
    {
        var comparison = CompareAggregateTieBreak(left, right);
        return comparison != 0 ? comparison : CompareUuid(left.StrategyId, right.StrategyId);
    }

    private static int CompareAggregateTieBreak(
        HistoricalGrossNetDonorAggregate left,
        HistoricalGrossNetDonorAggregate right)
    {
        var comparison = right.ExactBasisDenominator.CompareTo(left.ExactBasisDenominator);
        return comparison != 0
            ? comparison
            : GetSelectedDonorCount(right).CompareTo(GetSelectedDonorCount(left));
    }

    private static IReadOnlyList<HistoricalGrossNetDistance> CreateNumericDistanceVector(
        HistoricalGrossNetDonorNumericVector target,
        HistoricalGrossNetDonorNumericVector donor) =>
        [
            HistoricalGrossNetDistance.Between(target.DecisionThresholdBps, donor.DecisionThresholdBps),
            HistoricalGrossNetDistance.Between(target.DecisionDepth, donor.DecisionDepth),
            HistoricalGrossNetDistance.Between(target.EntryDelaySeconds, donor.EntryDelaySeconds),
            HistoricalGrossNetDistance.Between(target.FixedLimitPrice, donor.FixedLimitPrice),
            HistoricalGrossNetDistance.Between(target.MakerMinBestAskExclusive, donor.MakerMinBestAskExclusive),
            HistoricalGrossNetDistance.Between(target.ShiftDiffCount, donor.ShiftDiffCount),
            HistoricalGrossNetDistance.Between(target.FakMaximumOrderPrice, donor.FakMaximumOrderPrice),
            HistoricalGrossNetDistance.Between(target.MakerMaximumOrderPrice, donor.MakerMaximumOrderPrice)
        ];

    private static int CompareDistanceVectors(
        IReadOnlyList<HistoricalGrossNetDistance> left,
        IReadOnlyList<HistoricalGrossNetDistance> right)
    {
        for (var index = 0; index < left.Count; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static int CompareNumericVectors(
        HistoricalGrossNetDonorNumericVector left,
        HistoricalGrossNetDonorNumericVector right) =>
        FirstNonzero(
            CompareNullableAfterEqualDistance(left.DecisionThresholdBps, right.DecisionThresholdBps),
            left.DecisionDepth.CompareTo(right.DecisionDepth),
            left.EntryDelaySeconds.CompareTo(right.EntryDelaySeconds),
            CompareNullableAfterEqualDistance(left.FixedLimitPrice, right.FixedLimitPrice),
            CompareNullableAfterEqualDistance(left.MakerMinBestAskExclusive, right.MakerMinBestAskExclusive),
            left.ShiftDiffCount.CompareTo(right.ShiftDiffCount),
            CompareNullableAfterEqualDistance(left.FakMaximumOrderPrice, right.FakMaximumOrderPrice),
            CompareNullableAfterEqualDistance(left.MakerMaximumOrderPrice, right.MakerMaximumOrderPrice));

    private static int CompareNullableAfterEqualDistance(decimal? left, decimal? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null || right is null)
        {
            return left is null ? -1 : 1;
        }

        return left.Value.CompareTo(right.Value);
    }

    private static HistoricalGrossNetDonorAggregate? SelectBest(
        IEnumerable<HistoricalGrossNetDonorAggregate> candidates,
        Comparison<HistoricalGrossNetDonorAggregate> comparison)
    {
        HistoricalGrossNetDonorAggregate? best = null;
        foreach (var candidate in candidates)
        {
            if (best is null || comparison(candidate, best) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static HistoricalGrossNetDonorDescriptor CreateDescriptor(
        BtcUpDown5mStrategyVariant variant,
        IReadOnlyDictionary<Guid, BtcUpDown5mStrategyVariant> variantsById)
    {
        var asset = NormalizeCatalogAsset(variant.ReferenceAssetSymbol, variant.Id);
        var intervalSeconds = GetMarketIntervalSeconds(variant.MarketInterval);
        var requiredWindowSeconds = GetRequiredReferenceAverageWindowSeconds(
            variant.RequiredReferenceAverageWindow,
            variant.Id);
        var family = new HistoricalGrossNetDonorFamily(
            variant.Behavior,
            variant.MarketInterval,
            variant.Direction,
            GetEntryDelaySignClass(variant.EntryDelaySeconds),
            variant.PreOpenLifetimeMode,
            variant.FixedOutcome,
            variant.DiffCounterTriggerOutcome,
            requiredWindowSeconds,
            variant.PaperOnly,
            variant.MakerMinBestAskExclusive is not null,
            variant.FakMaximumOrderPrice is not null,
            variant.MakerMaximumOrderPrice is not null,
            ResolveLinkedDescriptor(variant.BaseSignalStrategyId, variantsById, variant.Id, "BaseSignal"),
            ResolveLinkedDescriptor(variant.ConfirmationSignalStrategyId, variantsById, variant.Id, "ConfirmationSignal"),
            ResolveLinkedDescriptor(variant.LowerEnterSourceStrategyId, variantsById, variant.Id, "LowerEnterSource"));
        var numeric = new HistoricalGrossNetDonorNumericVector(
            variant.DecisionThresholdBps,
            variant.DecisionDepth,
            variant.EntryDelaySeconds,
            variant.FixedLimitPrice,
            variant.MakerMinBestAskExclusive,
            variant.ShiftDiffCount,
            variant.FakMaximumOrderPrice,
            variant.MakerMaximumOrderPrice);
        return new HistoricalGrossNetDonorDescriptor(
            variant.Id,
            asset,
            intervalSeconds,
            family,
            numeric);
    }

    private static HistoricalGrossNetLinkedDescriptor? ResolveLinkedDescriptor(
        Guid? linkedStrategyId,
        IReadOnlyDictionary<Guid, BtcUpDown5mStrategyVariant> variantsById,
        Guid ownerStrategyId,
        string linkName)
    {
        if (linkedStrategyId is null)
        {
            return null;
        }

        if (!variantsById.TryGetValue(linkedStrategyId.Value, out var linked))
        {
            throw new InvalidOperationException(
                $"{DistanceVersion} cannot resolve {linkName} {CanonicalUuid(linkedStrategyId.Value)} " +
                $"for strategy {CanonicalUuid(ownerStrategyId)}.");
        }

        _ = GetMarketIntervalSeconds(linked.MarketInterval);
        return new HistoricalGrossNetLinkedDescriptor(
            linked.Behavior,
            linked.MarketInterval,
            linked.Direction,
            linked.DecisionThresholdBps);
    }

    private static string NormalizeCatalogAsset(string value, Guid strategyId)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{DistanceVersion} strategy {CanonicalUuid(strategyId)} has an invalid asset symbol.");
        }

        return value.ToUpperInvariant();
    }

    private static string NormalizeProvedAsset(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("A proved crypto asset symbol cannot be blank.");
        }

        return value.Trim().ToUpperInvariant();
    }

    private static int GetMarketIntervalSeconds(BtcUpDownMarketInterval interval) => interval switch
    {
        BtcUpDownMarketInterval.FiveMinutes => 300,
        BtcUpDownMarketInterval.FifteenMinutes => 900,
        BtcUpDownMarketInterval.OneHour => 3_600,
        BtcUpDownMarketInterval.FourHours => 14_400,
        _ => throw new InvalidOperationException(
            $"{DistanceVersion} does not map market interval '{interval}'.")
    };

    private static int? GetRequiredReferenceAverageWindowSeconds(string? value, Guid strategyId) => value switch
    {
        null => null,
        "3h" => 10_800,
        _ => throw new InvalidOperationException(
            $"{DistanceVersion} strategy {CanonicalUuid(strategyId)} has unsupported " +
            $"RequiredReferenceAverageWindow '{value}'.")
    };

    private static HistoricalGrossNetEntryDelaySignClass GetEntryDelaySignClass(int value) => value switch
    {
        < 0 => HistoricalGrossNetEntryDelaySignClass.Negative,
        0 => HistoricalGrossNetEntryDelaySignClass.Zero,
        _ => HistoricalGrossNetEntryDelaySignClass.Positive
    };

    private static HistoricalGrossNetDonorMatch CreateMatch(
        HistoricalGrossNetDonorTier tier,
        HistoricalGrossNetDonorAggregate? donor,
        HistoricalGrossNetDonorDescriptor? targetDescriptor,
        HistoricalGrossNetDonorDescriptor? donorDescriptor,
        IReadOnlyList<string> comparisonKey) =>
        new(tier, donor, targetDescriptor, donorDescriptor, comparisonKey);

    private static IReadOnlyList<string> DescribeTier1Key(
        HistoricalGrossNetDonorDescriptor target,
        HistoricalGrossNetDonorAggregate donor,
        HistoricalGrossNetDonorDescriptor donorDescriptor) =>
        [
            "distance:" + string.Join(",", CreateNumericDistanceVector(target.NumericVector, donorDescriptor.NumericVector)),
            "stake-desc:" + donor.ExactBasisDenominator,
            "count-desc:" + GetSelectedDonorCount(donor).ToString(CultureInfo.InvariantCulture),
            "numeric:" + DescribeNumericVector(donorDescriptor.NumericVector),
            "uuid:" + CanonicalUuid(donor.StrategyId)
        ];

    private static IReadOnlyList<string> DescribeTier2Key(
        HistoricalGrossNetDonorDescriptor target,
        HistoricalGrossNetDonorAggregate donor,
        HistoricalGrossNetDonorDescriptor donorDescriptor) =>
        [
            "semantic:" + DescribeTier2SemanticKey(target, donorDescriptor),
            .. DescribeTier1Key(target, donor, donorDescriptor)
        ];

    private static IReadOnlyList<string> DescribeTier4Key(HistoricalGrossNetDonorAggregate donor) =>
        [
            "stake-desc:" + donor.ExactBasisDenominator,
            "count-desc:" + GetSelectedDonorCount(donor).ToString(CultureInfo.InvariantCulture),
            "uuid:" + CanonicalUuid(donor.StrategyId)
        ];

    private static string DescribeTier2SemanticKey(
        HistoricalGrossNetDonorDescriptor target,
        HistoricalGrossNetDonorDescriptor donor)
    {
        var targetFamily = target.Family;
        var donorFamily = donor.Family;
        return string.Join(",",
            Mismatch(targetFamily.Behavior, donorFamily.Behavior),
            HistoricalGrossNetDistance.Between(target.MarketIntervalSeconds, donor.MarketIntervalSeconds),
            Mismatch(targetFamily.EntryDelaySignClass, donorFamily.EntryDelaySignClass),
            HistoricalGrossNetDistance.Between(
                target.NumericVector.EntryDelaySeconds,
                donor.NumericVector.EntryDelaySeconds),
            Mismatch(targetFamily.Direction, donorFamily.Direction),
            Mismatch(targetFamily.PreOpenLifetimeMode, donorFamily.PreOpenLifetimeMode),
            Mismatch(targetFamily.FixedOutcome, donorFamily.FixedOutcome),
            Mismatch(targetFamily.DiffCounterTriggerOutcome, donorFamily.DiffCounterTriggerOutcome),
            NullMismatch(targetFamily.RequiredReferenceAverageWindowSeconds, donorFamily.RequiredReferenceAverageWindowSeconds),
            HistoricalGrossNetDistance.Between(
                targetFamily.RequiredReferenceAverageWindowSeconds,
                donorFamily.RequiredReferenceAverageWindowSeconds),
            Mismatch(targetFamily.PaperOnly, donorFamily.PaperOnly),
            Mismatch(targetFamily.HasMakerMinBestAskExclusive, donorFamily.HasMakerMinBestAskExclusive),
            Mismatch(targetFamily.HasFakMaximumOrderPrice, donorFamily.HasFakMaximumOrderPrice),
            Mismatch(targetFamily.HasMakerMaximumOrderPrice, donorFamily.HasMakerMaximumOrderPrice),
            Mismatch(targetFamily.BaseSignal, donorFamily.BaseSignal),
            Mismatch(targetFamily.ConfirmationSignal, donorFamily.ConfirmationSignal),
            Mismatch(targetFamily.LowerEnterSource, donorFamily.LowerEnterSource));
    }

    private static string DescribeNumericVector(HistoricalGrossNetDonorNumericVector vector) =>
        string.Join(",",
            DescribeNullable(vector.DecisionThresholdBps),
            vector.DecisionDepth.ToString(CultureInfo.InvariantCulture),
            vector.EntryDelaySeconds.ToString(CultureInfo.InvariantCulture),
            DescribeNullable(vector.FixedLimitPrice),
            DescribeNullable(vector.MakerMinBestAskExclusive),
            vector.ShiftDiffCount.ToString(CultureInfo.InvariantCulture),
            DescribeNullable(vector.FakMaximumOrderPrice),
            DescribeNullable(vector.MakerMaximumOrderPrice));

    private static string DescribeNullable(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "NULL";

    private static int Mismatch<T>(T left, T right) => EqualityComparer<T>.Default.Equals(left, right) ? 0 : 1;

    private static int NullMismatch<T>(T? left, T? right) where T : struct =>
        left.HasValue == right.HasValue ? 0 : 1;

    private static int FirstNonzero(params int[] comparisons)
    {
        foreach (var comparison in comparisons)
        {
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static int CompareUuid(Guid left, Guid right) => string.Compare(
        CanonicalUuid(left),
        CanonicalUuid(right),
        StringComparison.Ordinal);

    private static string CanonicalUuid(Guid value) => value.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
}

internal readonly struct HistoricalGrossNetExactDecimal : IComparable<HistoricalGrossNetExactDecimal>
{
    public static readonly HistoricalGrossNetExactDecimal Zero = new(BigInteger.Zero, 0);

    public HistoricalGrossNetExactDecimal(BigInteger significand, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        if (scale > 1_024)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        Significand = significand;
        Scale = scale;
    }

    public BigInteger Significand { get; }

    public int Scale { get; }

    public static HistoricalGrossNetExactDecimal Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new FormatException("Exact decimal text cannot contain surrounding whitespace.");
        }

        var negative = value[0] == '-';
        var positive = value[0] == '+';
        var unsigned = negative || positive ? value[1..] : value;
        var parts = unsigned.Split('.');
        if (parts.Length > 2 || parts[0].Length == 0 || parts.Any(part => part.Any(character => !char.IsAsciiDigit(character))))
        {
            throw new FormatException($"'{value}' is not a plain exact decimal.");
        }

        var scale = parts.Length == 2 ? parts[1].Length : 0;
        var digits = parts.Length == 2 ? parts[0] + parts[1] : parts[0];
        if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var significand))
        {
            throw new FormatException($"'{value}' is not a plain exact decimal.");
        }

        return new HistoricalGrossNetExactDecimal(negative ? -significand : significand, scale);
    }

    public static HistoricalGrossNetExactDecimal FromDecimal(decimal value)
    {
        var bits = decimal.GetBits(value);
        var significand = (BigInteger)(uint)bits[0] |
                          ((BigInteger)(uint)bits[1] << 32) |
                          ((BigInteger)(uint)bits[2] << 64);
        if ((bits[3] & int.MinValue) != 0)
        {
            significand = -significand;
        }

        var scale = (bits[3] >> 16) & 0xff;
        return new HistoricalGrossNetExactDecimal(significand, scale);
    }

    public int CompareTo(HistoricalGrossNetExactDecimal other)
    {
        var scale = Math.Max(Scale, other.Scale);
        var left = Significand * Pow10(scale - Scale);
        var right = other.Significand * Pow10(scale - other.Scale);
        return left.CompareTo(right);
    }

    public HistoricalGrossNetExactDecimal Add(HistoricalGrossNetExactDecimal other)
    {
        var scale = Math.Max(Scale, other.Scale);
        return new HistoricalGrossNetExactDecimal(
            Significand * Pow10(scale - Scale) + other.Significand * Pow10(scale - other.Scale),
            scale);
    }

    public HistoricalGrossNetExactDecimal Subtract(HistoricalGrossNetExactDecimal other) =>
        Add(new HistoricalGrossNetExactDecimal(-other.Significand, other.Scale));

    public HistoricalGrossNetExactDecimal Multiply(HistoricalGrossNetExactDecimal other) =>
        new(Significand * other.Significand, checked(Scale + other.Scale));

    public HistoricalGrossNetExactDecimal RoundAwayFromZero(int targetScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetScale);
        if (Scale <= targetScale)
        {
            return new HistoricalGrossNetExactDecimal(
                Significand * Pow10(targetScale - Scale),
                targetScale);
        }

        var divisor = Pow10(Scale - targetScale);
        var quotient = BigInteger.DivRem(Significand, divisor, out var remainder);
        if (BigInteger.Abs(remainder) * 2 >= divisor)
        {
            quotient += Significand.Sign >= 0 ? BigInteger.One : -BigInteger.One;
        }

        return new HistoricalGrossNetExactDecimal(quotient, targetScale);
    }

    public HistoricalGrossNetExactDecimal RescaleExact(int targetScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetScale);
        if (Scale <= targetScale)
        {
            return new HistoricalGrossNetExactDecimal(
                Significand * Pow10(targetScale - Scale),
                targetScale);
        }

        var divisor = Pow10(Scale - targetScale);
        var quotient = BigInteger.DivRem(Significand, divisor, out var remainder);
        if (remainder != 0)
        {
            throw new InvalidOperationException(
                $"Exact decimal {this} cannot be represented at scale {targetScale}.");
        }

        return new HistoricalGrossNetExactDecimal(quotient, targetScale);
    }

    public override string ToString()
    {
        var absoluteDigits = BigInteger.Abs(Significand).ToString(CultureInfo.InvariantCulture);
        if (Scale == 0)
        {
            return Significand.Sign < 0 ? "-" + absoluteDigits : absoluteDigits;
        }

        absoluteDigits = absoluteDigits.PadLeft(Scale + 1, '0');
        var decimalIndex = absoluteDigits.Length - Scale;
        var result = absoluteDigits[..decimalIndex] + "." + absoluteDigits[decimalIndex..];
        return Significand.Sign < 0 ? "-" + result : result;
    }

    internal static BigInteger Pow10(int exponent) => exponent == 0
        ? BigInteger.One
        : BigInteger.Pow(10, exponent);
}

internal readonly record struct HistoricalGrossNetExactRational(
    HistoricalGrossNetExactDecimal Numerator,
    HistoricalGrossNetExactDecimal Denominator)
{
    public HistoricalGrossNetExactDecimal MultiplyAndRound8(HistoricalGrossNetExactDecimal value)
    {
        if (Numerator.CompareTo(HistoricalGrossNetExactDecimal.Zero) < 0 ||
            Denominator.CompareTo(HistoricalGrossNetExactDecimal.Zero) <= 0)
        {
            throw new InvalidOperationException("Historical donor rational must have N >= 0 and D > 0.");
        }

        var numerator = value.Significand * Numerator.Significand *
                        HistoricalGrossNetExactDecimal.Pow10(Denominator.Scale + 8);
        var denominator = Denominator.Significand *
                          HistoricalGrossNetExactDecimal.Pow10(value.Scale + Numerator.Scale);
        return new HistoricalGrossNetExactDecimal(
            RoundRationalAwayFromZero(numerator, denominator),
            8);
    }

    private static BigInteger RoundRationalAwayFromZero(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0)
        {
            throw new InvalidOperationException("Rational denominator must be positive.");
        }

        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (BigInteger.Abs(remainder) * 2 >= denominator)
        {
            quotient += numerator.Sign >= 0 ? BigInteger.One : -BigInteger.One;
        }

        return quotient;
    }
}

internal sealed record HistoricalGrossNetProvedFeeComponent(
    string AllocationIdentity,
    string CoverageIdentity,
    HistoricalGrossNetExactDecimal FeeUsd);

internal sealed record HistoricalGrossNetFeeEstimate(
    HistoricalGrossNetExactDecimal BaseEstimatedFee,
    HistoricalGrossNetExactDecimal ProvedComponentFloor,
    HistoricalGrossNetExactDecimal TotalFee,
    string CalculationSource)
{
    public HistoricalGrossNetExactDecimal ApplyToGross(HistoricalGrossNetExactDecimal gross) =>
        gross.Subtract(TotalFee).RescaleExact(8);
}

internal static class HistoricalGrossNetFeeEstimator
{
    private static readonly HistoricalGrossNetExactDecimal FixedCoefficient =
        HistoricalGrossNetExactDecimal.Parse("0.0333");

    public static HistoricalGrossNetFeeEstimate Calculate(
        HistoricalGrossNetExactDecimal basis,
        HistoricalGrossNetDonorMatch? match,
        IReadOnlyCollection<HistoricalGrossNetProvedFeeComponent> provedComponents)
    {
        var floor = CalculateComponentFloor(provedComponents);
        if (basis.CompareTo(HistoricalGrossNetExactDecimal.Zero) <= 0)
        {
            return new HistoricalGrossNetFeeEstimate(
                HistoricalGrossNetExactDecimal.Parse("0.00000000"),
                floor,
                floor,
                HistoricalGrossNetParityCalculationSources.NonpositiveBasis);
        }

        ArgumentNullException.ThrowIfNull(match);
        if (match.Donor is { } donor)
        {
            var ratio = new HistoricalGrossNetExactRational(
                donor.ExactFeeNumerator,
                donor.ExactBasisDenominator);
            var baseFee = ratio.MultiplyAndRound8(basis);
            return CreateEstimate(
                baseFee,
                floor,
                HistoricalGrossNetParityCalculationSources.Donor);
        }

        var fixedBaseFee = basis.Multiply(FixedCoefficient).RoundAwayFromZero(8);
        return CreateEstimate(
            fixedBaseFee,
            floor,
            HistoricalGrossNetParityCalculationSources.Fixed);
    }

    public static HistoricalGrossNetExactDecimal CalculateComponentFloor(
        IReadOnlyCollection<HistoricalGrossNetProvedFeeComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var allocations = new Dictionary<string, HistoricalGrossNetProvedFeeComponent>(StringComparer.Ordinal);
        var coverage = new Dictionary<string, string>(StringComparer.Ordinal);
        var total = HistoricalGrossNetExactDecimal.Parse("0.00000000");
        foreach (var component in components)
        {
            ArgumentNullException.ThrowIfNull(component);
            if (string.IsNullOrWhiteSpace(component.AllocationIdentity) ||
                string.IsNullOrWhiteSpace(component.CoverageIdentity) ||
                component.FeeUsd.CompareTo(HistoricalGrossNetExactDecimal.Zero) < 0)
            {
                throw new InvalidOperationException("A proved Fee component is invalid.");
            }

            var fee8 = component.FeeUsd.RescaleExact(8);
            if (allocations.TryGetValue(component.AllocationIdentity, out var existing))
            {
                if (!string.Equals(existing.CoverageIdentity, component.CoverageIdentity, StringComparison.Ordinal) ||
                    existing.FeeUsd.CompareTo(component.FeeUsd) != 0)
                {
                    throw new InvalidOperationException(
                        $"Conflicting evidence exists for allocation '{component.AllocationIdentity}'.");
                }

                continue;
            }

            if (coverage.TryGetValue(component.CoverageIdentity, out var coveredBy) &&
                !string.Equals(coveredBy, component.AllocationIdentity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Ambiguous Fee-component overlap exists for coverage '{component.CoverageIdentity}'.");
            }

            allocations.Add(component.AllocationIdentity, component);
            coverage[component.CoverageIdentity] = component.AllocationIdentity;
            total = total.Add(fee8);
        }

        return total.RescaleExact(8);
    }

    private static HistoricalGrossNetFeeEstimate CreateEstimate(
        HistoricalGrossNetExactDecimal baseFee,
        HistoricalGrossNetExactDecimal floor,
        string calculationSource)
    {
        var fee8 = baseFee.RescaleExact(8);
        var floor8 = floor.RescaleExact(8);
        var total = fee8.CompareTo(floor8) >= 0 ? fee8 : floor8;
        return new HistoricalGrossNetFeeEstimate(fee8, floor8, total, calculationSource);
    }
}
