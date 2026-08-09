using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class MakerGtdPaperPostOnlyAcceptanceEvaluatorTests
{
    private static readonly DateTimeOffset FrozenAtUtc =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_LimitBelowFreshBestAsk_AcceptsResting()
    {
        var evidence = BookEvidence(bestAsk: 0.51m);

        var result = MakerGtdPaperPostOnlyAcceptanceEvaluator.Evaluate(Intent(), evidence);

        Assert.True(result.Accepted);
        Assert.False(result.Rejected);
        Assert.Equal(MakerGtdPaperPostOnlyAcceptanceOutcome.AcceptedResting, result.Outcome);
        Assert.Equal(MakerGtdPaperPostOnlyAcceptanceReasonCodes.AcceptedResting, result.ReasonCode);
        Assert.Equal(0.51m, result.ObservedBestAsk);
        Assert.Equal(evidence.ReceivedAtUtc, result.AcceptedAtUtc);
    }

    [Theory]
    [InlineData("0.49")]
    [InlineData("0.50")]
    public void Evaluate_LimitAtOrAboveFreshBestAsk_RejectsWouldCross(string bestAskText)
    {
        var bestAsk = decimal.Parse(
            bestAskText,
            System.Globalization.CultureInfo.InvariantCulture);

        var result = MakerGtdPaperPostOnlyAcceptanceEvaluator.Evaluate(
            Intent(),
            BookEvidence(bestAsk));

        Assert.False(result.Accepted);
        Assert.True(result.Rejected);
        Assert.Equal(MakerGtdPaperPostOnlyAcceptanceOutcome.RejectedWouldCross, result.Outcome);
        Assert.Equal(
            MakerGtdPaperPostOnlyAcceptanceReasonCodes.WouldCrossAtAcceptance,
            result.ReasonCode);
        Assert.Equal(bestAsk, result.ObservedBestAsk);
        Assert.Null(result.AcceptedAtUtc);
    }

    [Fact]
    public void Evaluate_QuietBookWithPreFreezeSnapshotButNewReceipt_Accepts()
    {
        var result = MakerGtdPaperPostOnlyAcceptanceEvaluator.Evaluate(
            Intent(),
            BookEvidence(0.51m) with
            {
                SnapshotAtUtc = FrozenAtUtc.AddSeconds(-5),
                ReceivedAtUtc = FrozenAtUtc.AddMilliseconds(10),
                IsCurrent = true
            });

        Assert.True(result.Accepted);
    }

    [Fact]
    public void Evaluate_BookNotReceivedAfterFreeze_HasUnavailableEvidence()
    {
        var result = MakerGtdPaperPostOnlyAcceptanceEvaluator.Evaluate(
            Intent(),
            BookEvidence(0.51m) with { ReceivedAtUtc = FrozenAtUtc });

        AssertEvidenceUnavailable(
            result,
            MakerGtdPaperPostOnlyAcceptanceReasonCodes.NotReceivedAfterFreeze);
    }

    [Fact]
    public void Evaluate_StaleBook_HasUnavailableEvidence()
    {
        var result = MakerGtdPaperPostOnlyAcceptanceEvaluator.Evaluate(
            Intent(),
            BookEvidence(0.49m) with { IsCurrent = false });

        AssertEvidenceUnavailable(
            result,
            MakerGtdPaperPostOnlyAcceptanceReasonCodes.BookNotCurrent);
    }

    [Fact]
    public void Evaluate_DuplicateBook_HasUnavailableEvidence()
    {
        var result = MakerGtdPaperPostOnlyAcceptanceEvaluator.Evaluate(
            Intent(),
            BookEvidence(0.49m) with { IsDuplicateDelivery = true });

        AssertEvidenceUnavailable(
            result,
            MakerGtdPaperPostOnlyAcceptanceReasonCodes.DuplicateBookEvidence);
    }

    [Fact]
    public void Evaluate_NonAuthoritativeTimestamp_HasUnavailableEvidence()
    {
        var result = MakerGtdPaperPostOnlyAcceptanceEvaluator.Evaluate(
            Intent(),
            BookEvidence(0.49m) with { TimestampIsAuthoritative = false });

        AssertEvidenceUnavailable(
            result,
            MakerGtdPaperPostOnlyAcceptanceReasonCodes.TimestampNotAuthoritative);
    }

    [Theory]
    [InlineData("asset-2", "condition-1", "paper_post_only_book_asset_mismatch")]
    [InlineData("asset-1", "condition-2", "paper_post_only_book_condition_mismatch")]
    public void Evaluate_NonExactBook_HasUnavailableEvidence(
        string assetId,
        string conditionId,
        string expectedReasonCode)
    {
        var result = MakerGtdPaperPostOnlyAcceptanceEvaluator.Evaluate(
            Intent(),
            BookEvidence(0.49m) with
            {
                AssetId = assetId,
                ConditionId = conditionId
            });

        AssertEvidenceUnavailable(result, expectedReasonCode);
    }

    [Theory]
    [MemberData(nameof(InvalidBestAsks))]
    public void Evaluate_MissingOrInvalidBestAsk_HasUnavailableEvidence(decimal? bestAsk)
    {
        var result = MakerGtdPaperPostOnlyAcceptanceEvaluator.Evaluate(
            Intent(),
            BookEvidence(bestAsk));

        AssertEvidenceUnavailable(
            result,
            bestAsk is null
                ? MakerGtdPaperPostOnlyAcceptanceReasonCodes.MissingBestAsk
                : MakerGtdPaperPostOnlyAcceptanceReasonCodes.InvalidBestAsk);
    }

    [Fact]
    public void Evaluate_MissingSnapshotTimestamp_HasUnavailableEvidence()
    {
        var result = MakerGtdPaperPostOnlyAcceptanceEvaluator.Evaluate(
            Intent(),
            BookEvidence(0.51m) with { SnapshotAtUtc = default });

        AssertEvidenceUnavailable(
            result,
            MakerGtdPaperPostOnlyAcceptanceReasonCodes.InvalidBookTimestamp);
    }

    [Fact]
    public void Evaluate_InvalidFrozenIntent_FailsClosed()
    {
        var result = MakerGtdPaperPostOnlyAcceptanceEvaluator.Evaluate(
            Intent() with { SizeShares = 0m },
            BookEvidence(0.51m));

        Assert.False(result.Accepted);
        Assert.False(result.Rejected);
        Assert.Equal(MakerGtdPaperPostOnlyAcceptanceOutcome.InvalidIntent, result.Outcome);
        Assert.Equal(MakerGtdPaperPostOnlyAcceptanceReasonCodes.InvalidIntent, result.ReasonCode);
        Assert.Null(result.ObservedBestAsk);
        Assert.Null(result.AcceptedAtUtc);
    }

    public static TheoryData<decimal?> InvalidBestAsks => new()
    {
        null,
        0m,
        -0.01m,
        1m,
        1.01m
    };

    private static MakerGtdFrozenPostOnlyBuyIntent Intent()
    {
        return new MakerGtdFrozenPostOnlyBuyIntent(
            AssetId: "asset-1",
            ConditionId: "condition-1",
            LimitPrice: 0.50m,
            SizeShares: 12.50m,
            FrozenAtUtc);
    }

    private static MakerGtdPostOnlyBookEvidence BookEvidence(decimal? bestAsk)
    {
        return new MakerGtdPostOnlyBookEvidence(
            AssetId: "asset-1",
            ConditionId: "condition-1",
            BestAsk: bestAsk,
            SnapshotAtUtc: FrozenAtUtc.AddMilliseconds(5),
            ReceivedAtUtc: FrozenAtUtc.AddMilliseconds(10),
            TimestampIsAuthoritative: true,
            IsCurrent: true);
    }

    private static void AssertEvidenceUnavailable(
        MakerGtdPaperPostOnlyAcceptanceEvaluation result,
        string reasonCode)
    {
        Assert.False(result.Accepted);
        Assert.False(result.Rejected);
        Assert.Equal(
            MakerGtdPaperPostOnlyAcceptanceOutcome.EvidenceUnavailable,
            result.Outcome);
        Assert.Equal(reasonCode, result.ReasonCode);
        Assert.Null(result.ObservedBestAsk);
        Assert.Null(result.AcceptedAtUtc);
    }
}
