namespace PolyCopyTrader.Strategy;

/// <summary>
/// Emulates only the market-dependent PostOnly acceptance decision for a frozen
/// Paper BUY intent. Account, authentication, transport, and venue availability
/// failures are deliberately outside this evaluator.
/// </summary>
public static class MakerGtdPaperPostOnlyAcceptanceEvaluator
{
    public static MakerGtdPaperPostOnlyAcceptanceEvaluation Evaluate(
        MakerGtdFrozenPostOnlyBuyIntent intent,
        MakerGtdPostOnlyBookEvidence evidence)
    {
        if (!IsValidIntent(intent))
        {
            return MakerGtdPaperPostOnlyAcceptanceEvaluation.InvalidIntent(
                MakerGtdPaperPostOnlyAcceptanceReasonCodes.InvalidIntent);
        }

        if (evidence.IsDuplicateDelivery)
        {
            return MakerGtdPaperPostOnlyAcceptanceEvaluation.EvidenceUnavailable(
                MakerGtdPaperPostOnlyAcceptanceReasonCodes.DuplicateBookEvidence);
        }

        if (!string.Equals(evidence.AssetId, intent.AssetId, StringComparison.Ordinal))
        {
            return MakerGtdPaperPostOnlyAcceptanceEvaluation.EvidenceUnavailable(
                MakerGtdPaperPostOnlyAcceptanceReasonCodes.AssetMismatch);
        }

        if (!string.Equals(evidence.ConditionId, intent.ConditionId, StringComparison.Ordinal))
        {
            return MakerGtdPaperPostOnlyAcceptanceEvaluation.EvidenceUnavailable(
                MakerGtdPaperPostOnlyAcceptanceReasonCodes.ConditionMismatch);
        }

        if (!evidence.TimestampIsAuthoritative)
        {
            return MakerGtdPaperPostOnlyAcceptanceEvaluation.EvidenceUnavailable(
                MakerGtdPaperPostOnlyAcceptanceReasonCodes.TimestampNotAuthoritative);
        }

        if (!evidence.IsCurrent)
        {
            return MakerGtdPaperPostOnlyAcceptanceEvaluation.EvidenceUnavailable(
                MakerGtdPaperPostOnlyAcceptanceReasonCodes.BookNotCurrent);
        }

        if (evidence.SnapshotAtUtc == default ||
            evidence.ReceivedAtUtc == default)
        {
            return MakerGtdPaperPostOnlyAcceptanceEvaluation.EvidenceUnavailable(
                MakerGtdPaperPostOnlyAcceptanceReasonCodes.InvalidBookTimestamp);
        }

        if (evidence.ReceivedAtUtc <= intent.FrozenAtUtc)
        {
            return MakerGtdPaperPostOnlyAcceptanceEvaluation.EvidenceUnavailable(
                MakerGtdPaperPostOnlyAcceptanceReasonCodes.NotReceivedAfterFreeze);
        }

        if (evidence.BestAsk is null)
        {
            return MakerGtdPaperPostOnlyAcceptanceEvaluation.EvidenceUnavailable(
                MakerGtdPaperPostOnlyAcceptanceReasonCodes.MissingBestAsk);
        }

        var bestAsk = evidence.BestAsk.Value;
        if (bestAsk is <= 0m or >= 1m)
        {
            return MakerGtdPaperPostOnlyAcceptanceEvaluation.EvidenceUnavailable(
                MakerGtdPaperPostOnlyAcceptanceReasonCodes.InvalidBestAsk);
        }

        if (intent.LimitPrice >= bestAsk)
        {
            return MakerGtdPaperPostOnlyAcceptanceEvaluation.RejectedWouldCross(
                MakerGtdPaperPostOnlyAcceptanceReasonCodes.WouldCrossAtAcceptance,
                bestAsk);
        }

        return MakerGtdPaperPostOnlyAcceptanceEvaluation.AcceptedResting(
            MakerGtdPaperPostOnlyAcceptanceReasonCodes.AcceptedResting,
            bestAsk,
            evidence.ReceivedAtUtc);
    }

    private static bool IsValidIntent(MakerGtdFrozenPostOnlyBuyIntent intent)
    {
        return !string.IsNullOrWhiteSpace(intent.AssetId) &&
            !string.IsNullOrWhiteSpace(intent.ConditionId) &&
            intent.LimitPrice is > 0m and < 1m &&
            intent.SizeShares > 0m &&
            intent.FrozenAtUtc != default;
    }
}

public sealed record MakerGtdFrozenPostOnlyBuyIntent(
    string AssetId,
    string ConditionId,
    decimal LimitPrice,
    decimal SizeShares,
    DateTimeOffset FrozenAtUtc);

public sealed record MakerGtdPostOnlyBookEvidence(
    string? AssetId,
    string? ConditionId,
    decimal? BestAsk,
    DateTimeOffset SnapshotAtUtc,
    DateTimeOffset ReceivedAtUtc,
    bool TimestampIsAuthoritative,
    bool IsCurrent,
    bool IsDuplicateDelivery = false);

public enum MakerGtdPaperPostOnlyAcceptanceOutcome
{
    InvalidIntent,
    EvidenceUnavailable,
    RejectedWouldCross,
    AcceptedResting
}

public sealed record MakerGtdPaperPostOnlyAcceptanceEvaluation(
    MakerGtdPaperPostOnlyAcceptanceOutcome Outcome,
    string ReasonCode,
    decimal? ObservedBestAsk,
    DateTimeOffset? AcceptedAtUtc)
{
    public bool Accepted => Outcome == MakerGtdPaperPostOnlyAcceptanceOutcome.AcceptedResting;

    public bool Rejected => Outcome == MakerGtdPaperPostOnlyAcceptanceOutcome.RejectedWouldCross;

    public static MakerGtdPaperPostOnlyAcceptanceEvaluation InvalidIntent(string reasonCode)
    {
        return new MakerGtdPaperPostOnlyAcceptanceEvaluation(
            MakerGtdPaperPostOnlyAcceptanceOutcome.InvalidIntent,
            reasonCode,
            ObservedBestAsk: null,
            AcceptedAtUtc: null);
    }

    public static MakerGtdPaperPostOnlyAcceptanceEvaluation EvidenceUnavailable(string reasonCode)
    {
        return new MakerGtdPaperPostOnlyAcceptanceEvaluation(
            MakerGtdPaperPostOnlyAcceptanceOutcome.EvidenceUnavailable,
            reasonCode,
            ObservedBestAsk: null,
            AcceptedAtUtc: null);
    }

    public static MakerGtdPaperPostOnlyAcceptanceEvaluation RejectedWouldCross(
        string reasonCode,
        decimal observedBestAsk)
    {
        return new MakerGtdPaperPostOnlyAcceptanceEvaluation(
            MakerGtdPaperPostOnlyAcceptanceOutcome.RejectedWouldCross,
            reasonCode,
            observedBestAsk,
            AcceptedAtUtc: null);
    }

    public static MakerGtdPaperPostOnlyAcceptanceEvaluation AcceptedResting(
        string reasonCode,
        decimal observedBestAsk,
        DateTimeOffset acceptedAtUtc)
    {
        return new MakerGtdPaperPostOnlyAcceptanceEvaluation(
            MakerGtdPaperPostOnlyAcceptanceOutcome.AcceptedResting,
            reasonCode,
            observedBestAsk,
            acceptedAtUtc);
    }
}

public static class MakerGtdPaperPostOnlyAcceptanceReasonCodes
{
    public const string InvalidIntent = "paper_post_only_invalid_frozen_buy_intent";
    public const string DuplicateBookEvidence = "paper_post_only_duplicate_book_evidence";
    public const string AssetMismatch = "paper_post_only_book_asset_mismatch";
    public const string ConditionMismatch = "paper_post_only_book_condition_mismatch";
    public const string TimestampNotAuthoritative = "paper_post_only_book_timestamp_not_authoritative";
    public const string BookNotCurrent = "paper_post_only_book_not_current";
    public const string NotReceivedAfterFreeze = "paper_post_only_book_not_received_after_freeze";
    public const string InvalidBookTimestamp = "paper_post_only_invalid_book_timestamp";
    public const string MissingBestAsk = "paper_post_only_missing_best_ask";
    public const string InvalidBestAsk = "paper_post_only_invalid_best_ask";
    public const string WouldCrossAtAcceptance = "paper_post_only_would_cross_at_acceptance";
    public const string AcceptedResting = "paper_post_only_accepted_resting";
}
