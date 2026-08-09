using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Strategy;

/// <summary>
/// Applies the intentionally optimistic Paper TouchNoDepth model to one event
/// observed while a post-only BUY is already resting. Queue position, event
/// size, book depth, and aggressor side are deliberately outside this model.
/// </summary>
public static class MakerGtdTouchNoDepthEvaluator
{
    public static MakerGtdTouchNoDepthEvaluation Evaluate(
        MakerGtdRestingBuyOrder order,
        MakerGtdTouchNoDepthEvidence evidence)
    {
        if (!IsValidOrder(order))
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.InvalidOrder);
        }

        if (evidence.IsDuplicateEvent)
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.DuplicateEvent);
        }

        if (!string.Equals(evidence.AssetId, order.AssetId, StringComparison.Ordinal))
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.AssetMismatch);
        }

        if (!string.Equals(evidence.ConditionId, order.ConditionId, StringComparison.Ordinal))
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.ConditionMismatch);
        }

        if (!evidence.TimestampIsAuthoritative)
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.TimestampNotAuthoritative);
        }

        if (!evidence.IsCurrent)
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.StaleEvidence);
        }

        if (evidence.TimestampUtc <= order.AcceptedAtUtc)
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.NotAfterAcceptance);
        }

        if (evidence.TimestampUtc >= order.EffectiveExpiresAtUtc)
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.AtOrAfterEffectiveExpiry);
        }

        return evidence.EventType switch
        {
            MarketDataEventType.LastTradePrice => EvaluateLastTrade(order, evidence),
            MarketDataEventType.Book or
                MarketDataEventType.PriceChange or
                MarketDataEventType.BestBidAsk => EvaluateBestAsk(order, evidence),
            _ => MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.UnsupportedEvent)
        };
    }

    private static MakerGtdTouchNoDepthEvaluation EvaluateLastTrade(
        MakerGtdRestingBuyOrder order,
        MakerGtdTouchNoDepthEvidence evidence)
    {
        if (evidence.LastTradePrice is null)
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.MissingLastTradePrice);
        }

        var lastTradePrice = evidence.LastTradePrice.Value;
        if (!IsValidProbabilityPrice(lastTradePrice))
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.InvalidLastTradePrice);
        }

        if (lastTradePrice > order.LimitPrice)
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.LastTradeDidNotTouchLimit);
        }

        return MakerGtdTouchNoDepthEvaluation.FullFill(
            order.LimitPrice,
            order.RemainingShares,
            MakerGtdTouchNoDepthTrigger.LastTradePrice,
            MakerGtdTouchNoDepthReasonCodes.LastTradeTouchedLimit,
            lastTradePrice,
            evidence.TimestampUtc);
    }

    private static MakerGtdTouchNoDepthEvaluation EvaluateBestAsk(
        MakerGtdRestingBuyOrder order,
        MakerGtdTouchNoDepthEvidence evidence)
    {
        if (evidence.BestAsk is null)
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.MissingBestAsk);
        }

        var bestAsk = evidence.BestAsk.Value;
        if (!IsValidProbabilityPrice(bestAsk))
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.InvalidBestAsk);
        }

        if (bestAsk > order.LimitPrice)
        {
            return MakerGtdTouchNoDepthEvaluation.NoFill(
                MakerGtdTouchNoDepthReasonCodes.BestAskDidNotTouchLimit);
        }

        return MakerGtdTouchNoDepthEvaluation.FullFill(
            order.LimitPrice,
            order.RemainingShares,
            MakerGtdTouchNoDepthTrigger.BestAsk,
            MakerGtdTouchNoDepthReasonCodes.BestAskTouchedLimit,
            bestAsk,
            evidence.TimestampUtc);
    }

    private static bool IsValidOrder(MakerGtdRestingBuyOrder order)
    {
        return !string.IsNullOrWhiteSpace(order.AssetId) &&
            !string.IsNullOrWhiteSpace(order.ConditionId) &&
            IsValidProbabilityPrice(order.LimitPrice) &&
            order.RemainingShares > 0m &&
            order.EffectiveExpiresAtUtc > order.AcceptedAtUtc;
    }

    private static bool IsValidProbabilityPrice(decimal price)
    {
        return price is > 0m and < 1m;
    }
}

public sealed record MakerGtdRestingBuyOrder(
    string AssetId,
    string ConditionId,
    decimal LimitPrice,
    decimal RemainingShares,
    DateTimeOffset AcceptedAtUtc,
    DateTimeOffset EffectiveExpiresAtUtc);

public sealed record MakerGtdTouchNoDepthEvidence(
    MarketDataEventType EventType,
    string? AssetId,
    string? ConditionId,
    decimal? LastTradePrice,
    decimal? BestAsk,
    DateTimeOffset TimestampUtc,
    bool TimestampIsAuthoritative,
    bool IsCurrent,
    bool IsDuplicateEvent = false);

public enum MakerGtdTouchNoDepthOutcome
{
    NoFill,
    FullFill
}

public enum MakerGtdTouchNoDepthTrigger
{
    None,
    LastTradePrice,
    BestAsk
}

public sealed record MakerGtdTouchNoDepthEvaluation(
    MakerGtdTouchNoDepthOutcome Outcome,
    string ReasonCode,
    decimal FillPrice,
    decimal FillShares,
    MakerGtdTouchNoDepthTrigger Trigger,
    decimal? TriggerPrice,
    DateTimeOffset? TriggerTimestampUtc)
{
    public bool Filled => Outcome == MakerGtdTouchNoDepthOutcome.FullFill;

    public static MakerGtdTouchNoDepthEvaluation NoFill(string reasonCode)
    {
        return new MakerGtdTouchNoDepthEvaluation(
            MakerGtdTouchNoDepthOutcome.NoFill,
            reasonCode,
            FillPrice: 0m,
            FillShares: 0m,
            MakerGtdTouchNoDepthTrigger.None,
            TriggerPrice: null,
            TriggerTimestampUtc: null);
    }

    public static MakerGtdTouchNoDepthEvaluation FullFill(
        decimal fillPrice,
        decimal fillShares,
        MakerGtdTouchNoDepthTrigger trigger,
        string reasonCode,
        decimal triggerPrice,
        DateTimeOffset triggerTimestampUtc)
    {
        return new MakerGtdTouchNoDepthEvaluation(
            MakerGtdTouchNoDepthOutcome.FullFill,
            reasonCode,
            fillPrice,
            fillShares,
            trigger,
            triggerPrice,
            triggerTimestampUtc);
    }
}

public static class MakerGtdTouchNoDepthReasonCodes
{
    public const string InvalidOrder = "maker_gtd_touch_no_depth_invalid_resting_buy_order";
    public const string UnsupportedEvent = "maker_gtd_touch_no_depth_unsupported_event";
    public const string DuplicateEvent = "maker_gtd_touch_no_depth_duplicate_event";
    public const string AssetMismatch = "maker_gtd_touch_no_depth_asset_mismatch";
    public const string ConditionMismatch = "maker_gtd_touch_no_depth_condition_mismatch";
    public const string TimestampNotAuthoritative = "maker_gtd_touch_no_depth_timestamp_not_authoritative";
    public const string StaleEvidence = "maker_gtd_touch_no_depth_stale_evidence";
    public const string NotAfterAcceptance = "maker_gtd_touch_no_depth_not_after_acceptance";
    public const string AtOrAfterEffectiveExpiry = "maker_gtd_touch_no_depth_at_or_after_effective_expiry";
    public const string MissingLastTradePrice = "maker_gtd_touch_no_depth_missing_last_trade_price";
    public const string InvalidLastTradePrice = "maker_gtd_touch_no_depth_invalid_last_trade_price";
    public const string LastTradeDidNotTouchLimit = "maker_gtd_touch_no_depth_last_trade_above_limit";
    public const string LastTradeTouchedLimit = "maker_gtd_touch_no_depth_last_trade_touched_limit_full_fill";
    public const string MissingBestAsk = "maker_gtd_touch_no_depth_missing_best_ask";
    public const string InvalidBestAsk = "maker_gtd_touch_no_depth_invalid_best_ask";
    public const string BestAskDidNotTouchLimit = "maker_gtd_touch_no_depth_best_ask_above_limit";
    public const string BestAskTouchedLimit = "maker_gtd_touch_no_depth_best_ask_touched_limit_full_fill";
}
