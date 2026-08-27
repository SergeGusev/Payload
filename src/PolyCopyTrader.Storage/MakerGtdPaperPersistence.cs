using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public enum MakerGtdPaperMutationOutcome
{
    Applied,
    AlreadyApplied,
    NotEligible
}

public sealed record MakerGtdPaperFullFillRequest(
    string ExpectedExecutionSource,
    PaperOrder FilledOrder,
    PaperFill Fill,
    PaperPosition? ExpectedPosition,
    PaperPosition Position,
    StrategyMarketPaperRun EnteredRun);

public sealed record MakerGtdPaperExpiryRequest(
    string ExpectedExecutionSource,
    DateTimeOffset EvaluatedAtUtc,
    PaperOrder ExpiredOrder,
    StrategyMarketPaperRun SkippedRun);

public sealed record MakerGtdPaperMutationResult(
    MakerGtdPaperMutationOutcome Outcome,
    string ReasonCode,
    PaperOrder? PaperOrder = null,
    StrategyMarketPaperRun? StrategyRun = null,
    PaperFill? PaperFill = null,
    PaperPosition? PaperPosition = null,
    MakerGtdPaperMismatchDiagnostic? MismatchDiagnostic = null);

public sealed record MakerGtdPaperMismatchDiagnostic(
    string Stage,
    IReadOnlyList<MakerGtdPaperMismatchDetail> Mismatches);

public sealed record MakerGtdPaperMismatchDetail(
    string Predicate,
    string? CurrentValue = null,
    string? RequestedValue = null,
    string? CurrentUtc = null,
    string? RequestedUtc = null,
    long? CurrentUtcTicks = null,
    long? RequestedUtcTicks = null,
    long? CurrentPostgresMicroseconds = null,
    long? RequestedPostgresMicroseconds = null,
    string? CurrentSha256 = null,
    string? RequestedSha256 = null);

public static class MakerGtdPaperMismatchStages
{
    public const string LockedOrderIdentity = "locked_order_identity";
    public const string RequestedFilledOrderTransition = "requested_filled_order_transition";
}

public static class MakerGtdPaperMutationReasonCodes
{
    public const string FillApplied = "maker_gtd_paper_fill_applied";
    public const string FillAlreadyApplied = "maker_gtd_paper_fill_already_applied";
    public const string ExpiryApplied = "maker_gtd_paper_expiry_applied";
    public const string ExpiryAlreadyApplied = "maker_gtd_paper_expiry_already_applied";
    public const string OrderNotFound = "maker_gtd_paper_order_not_found";
    public const string StrategyRunNotFound = "maker_gtd_paper_strategy_run_not_found";
    public const string ExecutionSourceMismatch = "maker_gtd_paper_execution_source_mismatch";
    public const string OrderStateMismatch = "maker_gtd_paper_order_state_mismatch";
    public const string StrategyRunStateMismatch = "maker_gtd_paper_strategy_run_state_mismatch";
    public const string StrategyRunLinkMismatch = "maker_gtd_paper_strategy_run_link_mismatch";
    public const string ExistingFillConflict = "maker_gtd_paper_existing_fill_conflict";
    public const string FillTimestampOutsideOrderLifetime = "maker_gtd_paper_fill_timestamp_outside_order_lifetime";
    public const string FilledOrderShapeMismatch = "maker_gtd_paper_filled_order_shape_mismatch";
    public const string FillShapeMismatch = "maker_gtd_paper_fill_shape_mismatch";
    public const string EnteredRunShapeMismatch = "maker_gtd_paper_entered_run_shape_mismatch";
    public const string PositionConcurrencyConflict = "maker_gtd_paper_position_concurrency_conflict";
    public const string PositionShapeMismatch = "maker_gtd_paper_position_shape_mismatch";
    public const string ExpiredOrderShapeMismatch = "maker_gtd_paper_expired_order_shape_mismatch";
    public const string SkippedRunShapeMismatch = "maker_gtd_paper_skipped_run_shape_mismatch";
    public const string ExpiryNotReached = "maker_gtd_paper_expiry_not_reached";
}

internal static class MakerGtdPaperPersistenceTransitions
{
    internal static void Validate(MakerGtdPaperFullFillRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.FilledOrder);
        ArgumentNullException.ThrowIfNull(request.Fill);
        ArgumentNullException.ThrowIfNull(request.Position);
        ArgumentNullException.ThrowIfNull(request.EnteredRun);

        if (string.IsNullOrWhiteSpace(request.ExpectedExecutionSource))
        {
            throw new ArgumentException("Expected Maker-GTD execution source is required.", nameof(request));
        }

        if (request.FilledOrder.Id == Guid.Empty ||
            request.Fill.Id == Guid.Empty ||
            request.EnteredRun.Id == Guid.Empty)
        {
            throw new ArgumentException("Maker-GTD order, fill, and strategy-run identifiers are required.", nameof(request));
        }

        if (request.PositionMarkIsInvalid())
        {
            throw new ArgumentException("Maker-GTD caller-computed position contains invalid numeric values.", nameof(request));
        }

        ValidateJson(request.FilledOrder.RawDecisionJson, nameof(request));
    }

    internal static void Validate(MakerGtdPaperExpiryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExpiredOrder);
        ArgumentNullException.ThrowIfNull(request.SkippedRun);

        if (string.IsNullOrWhiteSpace(request.ExpectedExecutionSource))
        {
            throw new ArgumentException("Expected Maker-GTD execution source is required.", nameof(request));
        }

        if (request.ExpiredOrder.Id == Guid.Empty || request.SkippedRun.Id == Guid.Empty)
        {
            throw new ArgumentException("Maker-GTD order and strategy-run identifiers are required.", nameof(request));
        }

        if (request.EvaluatedAtUtc == default ||
            string.IsNullOrWhiteSpace(request.SkippedRun.SkipReason) ||
            string.IsNullOrWhiteSpace(request.SkippedRun.SkipDiagnosticsJson))
        {
            throw new ArgumentException(
                "Maker-GTD expiry time, skip reason, and diagnostics are required.",
                nameof(request));
        }

        ValidateJson(request.ExpiredOrder.RawDecisionJson, nameof(request));
        ValidateJson(request.SkippedRun.SkipDiagnosticsJson, nameof(request));
    }

    internal static string? GetFullFillIneligibilityReason(
        PaperOrder currentOrder,
        StrategyMarketPaperRun currentRun,
        IReadOnlyList<PaperFill> existingFills,
        PaperPosition? currentPosition,
        MakerGtdPaperFullFillRequest request)
    {
        if (!string.Equals(currentOrder.ExecutionSource, request.ExpectedExecutionSource, StringComparison.Ordinal))
        {
            return MakerGtdPaperMutationReasonCodes.ExecutionSourceMismatch;
        }

        if (!RunLinksOrder(currentRun, currentOrder))
        {
            return MakerGtdPaperMutationReasonCodes.StrategyRunLinkMismatch;
        }

        if (existingFills.Count != 0)
        {
            return MakerGtdPaperMutationReasonCodes.ExistingFillConflict;
        }

        if (currentOrder.Status != PaperOrderStatus.Pending)
        {
            return MakerGtdPaperMutationReasonCodes.OrderStateMismatch;
        }

        if (!string.Equals(currentRun.Status, StrategyMarketPaperRunStatuses.Resting, StringComparison.Ordinal))
        {
            return MakerGtdPaperMutationReasonCodes.StrategyRunStateMismatch;
        }

        if (request.Fill.FilledAtUtc <= currentOrder.CreatedAtUtc ||
            request.Fill.FilledAtUtc >= currentOrder.ExpiresAtUtc)
        {
            return MakerGtdPaperMutationReasonCodes.FillTimestampOutsideOrderLifetime;
        }

        if (!IsValidFilledOrderTransition(currentOrder, request.FilledOrder, request.Fill, request.ExpectedExecutionSource))
        {
            return MakerGtdPaperMutationReasonCodes.FilledOrderShapeMismatch;
        }

        if (!IsValidFullFill(currentOrder, request.Fill))
        {
            return MakerGtdPaperMutationReasonCodes.FillShapeMismatch;
        }

        if (!IsValidEnteredRunTransition(currentRun, request.EnteredRun, request.FilledOrder, request.Fill))
        {
            return MakerGtdPaperMutationReasonCodes.EnteredRunShapeMismatch;
        }

        if (!PositionEquivalent(currentPosition, request.ExpectedPosition))
        {
            return MakerGtdPaperMutationReasonCodes.PositionConcurrencyConflict;
        }

        if (!IsValidPositionTransition(currentOrder, request.Fill, currentPosition, request.Position))
        {
            return MakerGtdPaperMutationReasonCodes.PositionShapeMismatch;
        }

        return null;
    }

    internal static string? GetExpiryIneligibilityReason(
        PaperOrder currentOrder,
        StrategyMarketPaperRun currentRun,
        IReadOnlyList<PaperFill> existingFills,
        MakerGtdPaperExpiryRequest request)
    {
        if (!string.Equals(currentOrder.ExecutionSource, request.ExpectedExecutionSource, StringComparison.Ordinal))
        {
            return MakerGtdPaperMutationReasonCodes.ExecutionSourceMismatch;
        }

        if (!RunLinksOrder(currentRun, currentOrder))
        {
            return MakerGtdPaperMutationReasonCodes.StrategyRunLinkMismatch;
        }

        if (existingFills.Count != 0)
        {
            return MakerGtdPaperMutationReasonCodes.ExistingFillConflict;
        }

        if (currentOrder.Status != PaperOrderStatus.Pending)
        {
            return MakerGtdPaperMutationReasonCodes.OrderStateMismatch;
        }

        if (!string.Equals(currentRun.Status, StrategyMarketPaperRunStatuses.Resting, StringComparison.Ordinal))
        {
            return MakerGtdPaperMutationReasonCodes.StrategyRunStateMismatch;
        }

        if (request.EvaluatedAtUtc < currentOrder.ExpiresAtUtc)
        {
            return MakerGtdPaperMutationReasonCodes.ExpiryNotReached;
        }

        if (!IsValidExpiredOrderTransition(currentOrder, request.ExpiredOrder, request.ExpectedExecutionSource))
        {
            return MakerGtdPaperMutationReasonCodes.ExpiredOrderShapeMismatch;
        }

        if (!IsValidSkippedRunTransition(currentRun, request.SkippedRun, request.ExpiredOrder, request.EvaluatedAtUtc))
        {
            return MakerGtdPaperMutationReasonCodes.SkippedRunShapeMismatch;
        }

        return null;
    }

    internal static MakerGtdPaperMismatchDiagnostic CreateLockedOrderIdentityMismatchDiagnostic(
        PaperOrder current,
        PaperOrder initial)
    {
        var mismatches = new List<MakerGtdPaperMismatchDetail>();
        AddMismatch(
            mismatches,
            "copied_trader_wallet",
            !string.Equals(current.CopiedTraderWallet, initial.CopiedTraderWallet, StringComparison.Ordinal));
        AddMismatch(
            mismatches,
            "asset_id",
            !string.Equals(current.AssetId, initial.AssetId, StringComparison.Ordinal));
        return new MakerGtdPaperMismatchDiagnostic(
            MakerGtdPaperMismatchStages.LockedOrderIdentity,
            mismatches);
    }

    internal static MakerGtdPaperMismatchDiagnostic CreateFilledOrderTransitionMismatchDiagnostic(
        PaperOrder current,
        PaperOrder requested,
        PaperFill fill,
        string expectedExecutionSource)
    {
        var mismatches = new List<MakerGtdPaperMismatchDetail>();
        AddMismatch(mismatches, "requested_status_filled", requested.Status != PaperOrderStatus.Filled);
        AddMismatch(mismatches, "requested_filled_at_present", requested.FilledAtUtc is null);
        AddMismatch(
            mismatches,
            "requested_filled_at_matches_fill",
            requested.FilledAtUtc is { } filledAtUtc && !SameTimestamp(filledAtUtc, fill.FilledAtUtc));
        AddMismatch(mismatches, "requested_cancelled_at_null", requested.CancelledAtUtc is not null);
        AddMismatch(
            mismatches,
            "requested_execution_source",
            !string.Equals(requested.ExecutionSource, expectedExecutionSource, StringComparison.Ordinal));
        AddMismatch(mismatches, "id", current.Id != requested.Id);
        AddMismatch(mismatches, "signal_id", current.SignalId != requested.SignalId);
        AddMismatch(
            mismatches,
            "strategy_id",
            StrategyIds.Normalize(current.StrategyId) != StrategyIds.Normalize(requested.StrategyId));
        AddMismatch(
            mismatches,
            "copied_trader_wallet",
            !string.Equals(current.CopiedTraderWallet, requested.CopiedTraderWallet, StringComparison.Ordinal));
        AddMismatch(mismatches, "side", current.Side != requested.Side);
        AddMismatch(
            mismatches,
            "asset_id",
            !string.Equals(current.AssetId, requested.AssetId, StringComparison.Ordinal));
        AddMismatch(
            mismatches,
            "condition_id",
            !string.Equals(current.ConditionId, requested.ConditionId, StringComparison.Ordinal));
        AddMismatch(
            mismatches,
            "outcome",
            !string.Equals(current.Outcome, requested.Outcome, StringComparison.Ordinal));
        AddNumericMismatch(mismatches, "price", current.Price, requested.Price);
        AddNumericMismatch(mismatches, "size_shares", current.SizeShares, requested.SizeShares);
        AddNumericMismatch(mismatches, "notional_usd", current.NotionalUsd, requested.NotionalUsd);
        AddInitialTimestampMismatch(
            mismatches,
            "created_at_utc",
            current.CreatedAtUtc,
            requested.CreatedAtUtc);
        AddInitialTimestampMismatch(
            mismatches,
            "expires_at_utc",
            current.ExpiresAtUtc,
            requested.ExpiresAtUtc);
        if (!JsonEquivalent(current.RawDecisionJson, requested.RawDecisionJson))
        {
            mismatches.Add(new MakerGtdPaperMismatchDetail(
                "raw_decision_json",
                CurrentSha256: Sha256Fingerprint(current.RawDecisionJson),
                RequestedSha256: Sha256Fingerprint(requested.RawDecisionJson)));
        }
        AddMismatch(mismatches, "correlation_id", current.CorrelationId != requested.CorrelationId);
        return new MakerGtdPaperMismatchDiagnostic(
            MakerGtdPaperMismatchStages.RequestedFilledOrderTransition,
            mismatches);
    }

    internal static bool IsAlreadyApplied(
        PaperOrder currentOrder,
        StrategyMarketPaperRun currentRun,
        IReadOnlyList<PaperFill> existingFills,
        MakerGtdPaperFullFillRequest request)
    {
        return string.Equals(currentOrder.ExecutionSource, request.ExpectedExecutionSource, StringComparison.Ordinal) &&
            currentOrder.Status == PaperOrderStatus.Filled &&
            string.Equals(currentRun.Status, StrategyMarketPaperRunStatuses.Entered, StringComparison.Ordinal) &&
            RunLinksOrder(currentRun, currentOrder) &&
            OrderEquivalent(currentOrder, request.FilledOrder) &&
            RunEquivalent(currentRun, request.EnteredRun) &&
            existingFills.Count == 1 &&
            FillEquivalent(existingFills[0], request.Fill);
    }

    internal static bool IsAlreadyApplied(
        PaperOrder currentOrder,
        StrategyMarketPaperRun currentRun,
        IReadOnlyList<PaperFill> existingFills,
        MakerGtdPaperExpiryRequest request)
    {
        return string.Equals(currentOrder.ExecutionSource, request.ExpectedExecutionSource, StringComparison.Ordinal) &&
            currentOrder.Status == PaperOrderStatus.Expired &&
            string.Equals(currentRun.Status, StrategyMarketPaperRunStatuses.Skipped, StringComparison.Ordinal) &&
            RunLinksOrder(currentRun, currentOrder) &&
            existingFills.Count == 0 &&
            OrderEquivalent(currentOrder, request.ExpiredOrder) &&
            RunEquivalent(currentRun, request.SkippedRun);
    }

    internal static bool PositionEquivalent(PaperPosition? left, PaperPosition? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(left.AssetId, right.AssetId, StringComparison.Ordinal) &&
            string.Equals(left.ConditionId, right.ConditionId, StringComparison.Ordinal) &&
            string.Equals(left.Outcome, right.Outcome, StringComparison.Ordinal) &&
            string.Equals(left.CopiedTraderWallet, right.CopiedTraderWallet, StringComparison.Ordinal) &&
            left.SizeShares == right.SizeShares &&
            left.AveragePrice == right.AveragePrice &&
            left.EstimatedValueUsd == right.EstimatedValueUsd &&
            left.UnrealizedPnlUsd == right.UnrealizedPnlUsd &&
            SameTimestamp(left.UpdatedAtUtc, right.UpdatedAtUtc) &&
            left.FeeUsd == right.FeeUsd &&
            string.Equals(left.FeeAccountingStatus, right.FeeAccountingStatus, StringComparison.Ordinal) &&
            string.Equals(left.FeeLiquidityRole, right.FeeLiquidityRole, StringComparison.Ordinal) &&
            string.Equals(left.FeeCalculationSource, right.FeeCalculationSource, StringComparison.Ordinal) &&
            left.FeeRate == right.FeeRate &&
            left.FeeExponent == right.FeeExponent &&
            left.FeeTakerOnly == right.FeeTakerOnly &&
            SameNullableTimestamp(left.FeeCalculatedAtUtc, right.FeeCalculatedAtUtc) &&
            left.NetUnrealizedPnlUsd == right.NetUnrealizedPnlUsd;
    }

    private static bool IsValidFilledOrderTransition(
        PaperOrder current,
        PaperOrder requested,
        PaperFill fill,
        string expectedExecutionSource)
    {
        return requested.Status == PaperOrderStatus.Filled &&
            requested.FilledAtUtc is { } filledAtUtc &&
            SameTimestamp(filledAtUtc, fill.FilledAtUtc) &&
            requested.CancelledAtUtc is null &&
            string.Equals(requested.ExecutionSource, expectedExecutionSource, StringComparison.Ordinal) &&
            SameImmutableOrderShape(current, requested);
    }

    private static bool IsValidFullFill(PaperOrder order, PaperFill fill)
    {
        return order.Side == TradeSide.Buy &&
            fill.PaperOrderId == order.Id &&
            fill.Price == order.Price &&
            fill.SizeShares == order.SizeShares &&
            fill.Price is > 0m and < 1m &&
            fill.SizeShares > 0m &&
            fill.RealizedPnlUsd == 0m &&
            !string.IsNullOrWhiteSpace(fill.Evidence) &&
            FeeAccountingRules.ParseLiquidityRole(fill.FeeLiquidityRole) == FeeLiquidityRole.Maker;
    }

    private static bool IsValidEnteredRunTransition(
        StrategyMarketPaperRun current,
        StrategyMarketPaperRun requested,
        PaperOrder filledOrder,
        PaperFill fill)
    {
        return requested.Status == StrategyMarketPaperRunStatuses.Entered &&
            requested.PaperOrderId == filledOrder.Id &&
            requested.SignalId == filledOrder.SignalId &&
            requested.EntryPrice == fill.Price &&
            requested.StakeUsd == filledOrder.NotionalUsd &&
            requested.SizeShares == fill.SizeShares &&
            requested.EnteredAtUtc is { } enteredAtUtc &&
            SameTimestamp(enteredAtUtc, fill.FilledAtUtc) &&
            SameTimestamp(requested.UpdatedAtUtc, fill.FilledAtUtc) &&
            requested.SkipReason is null &&
            requested.SkipDiagnosticsJson is null &&
            requested.SettlementPrice is null &&
            requested.SettlementValueUsd is null &&
            requested.RealizedPnlUsd is null &&
            requested.SettledAtUtc is null &&
            requested.FeeUsd == fill.FeeUsd &&
            string.Equals(requested.FeeAccountingStatus, fill.FeeAccountingStatus, StringComparison.Ordinal) &&
            string.Equals(requested.FeeLiquidityRole, fill.FeeLiquidityRole, StringComparison.Ordinal) &&
            string.Equals(requested.FeeCalculationSource, fill.FeeCalculationSource, StringComparison.Ordinal) &&
            requested.FeeRate == fill.FeeRate &&
            requested.FeeExponent == fill.FeeExponent &&
            requested.FeeTakerOnly == fill.FeeTakerOnly &&
            SameNullableTimestamp(requested.FeeCalculatedAtUtc, fill.FeeCalculatedAtUtc) &&
            requested.NetRealizedPnlUsd == current.NetRealizedPnlUsd &&
            SameImmutableRunShape(current, requested);
    }

    private static bool IsValidPositionTransition(
        PaperOrder order,
        PaperFill fill,
        PaperPosition? current,
        PaperPosition requested)
    {
        var expectedSize = (current?.SizeShares ?? 0m) + fill.SizeShares;
        return string.Equals(requested.AssetId, order.AssetId, StringComparison.Ordinal) &&
            string.Equals(requested.ConditionId, order.ConditionId, StringComparison.Ordinal) &&
            string.Equals(requested.Outcome, order.Outcome, StringComparison.Ordinal) &&
            string.Equals(requested.CopiedTraderWallet, order.CopiedTraderWallet, StringComparison.Ordinal) &&
            requested.SizeShares == expectedSize &&
            requested.SizeShares > 0m &&
            requested.AveragePrice is > 0m and < 1m &&
            requested.EstimatedValueUsd >= 0m;
    }

    private static bool IsValidExpiredOrderTransition(
        PaperOrder current,
        PaperOrder requested,
        string expectedExecutionSource)
    {
        return requested.Status == PaperOrderStatus.Expired &&
            requested.FilledAtUtc is null &&
            requested.CancelledAtUtc is null &&
            string.Equals(requested.ExecutionSource, expectedExecutionSource, StringComparison.Ordinal) &&
            SameImmutableOrderShape(current, requested);
    }

    private static bool IsValidSkippedRunTransition(
        StrategyMarketPaperRun current,
        StrategyMarketPaperRun requested,
        PaperOrder expiredOrder,
        DateTimeOffset evaluatedAtUtc)
    {
        return requested.Status == StrategyMarketPaperRunStatuses.Skipped &&
            requested.PaperOrderId == expiredOrder.Id &&
            requested.SignalId == expiredOrder.SignalId &&
            requested.EntryPrice == current.EntryPrice &&
            requested.StakeUsd == current.StakeUsd &&
            requested.SizeShares == current.SizeShares &&
            requested.EnteredAtUtc is null &&
            requested.SettlementPrice is null &&
            requested.SettlementValueUsd is null &&
            requested.RealizedPnlUsd is null &&
            requested.SettledAtUtc is null &&
            !string.IsNullOrWhiteSpace(requested.SkipReason) &&
            !string.IsNullOrWhiteSpace(requested.SkipDiagnosticsJson) &&
            SameTimestamp(requested.UpdatedAtUtc, evaluatedAtUtc) &&
            requested.FeeUsd == current.FeeUsd &&
            string.Equals(requested.FeeAccountingStatus, current.FeeAccountingStatus, StringComparison.Ordinal) &&
            string.Equals(requested.FeeLiquidityRole, current.FeeLiquidityRole, StringComparison.Ordinal) &&
            string.Equals(requested.FeeCalculationSource, current.FeeCalculationSource, StringComparison.Ordinal) &&
            requested.FeeRate == current.FeeRate &&
            requested.FeeExponent == current.FeeExponent &&
            requested.FeeTakerOnly == current.FeeTakerOnly &&
            SameNullableTimestamp(requested.FeeCalculatedAtUtc, current.FeeCalculatedAtUtc) &&
            requested.NetRealizedPnlUsd == current.NetRealizedPnlUsd &&
            SameImmutableRunShape(current, requested);
    }

    private static bool RunLinksOrder(StrategyMarketPaperRun run, PaperOrder order)
    {
        return run.PaperOrderId == order.Id &&
            run.SignalId == order.SignalId &&
            StrategyIds.Normalize(run.StrategyId) == StrategyIds.Normalize(order.StrategyId) &&
            string.Equals(run.ConditionId, order.ConditionId, StringComparison.Ordinal) &&
            string.Equals(run.SelectedAssetId, order.AssetId, StringComparison.Ordinal) &&
            string.Equals(run.SelectedOutcome, order.Outcome, StringComparison.Ordinal);
    }

    private static bool SameImmutableOrderShape(PaperOrder left, PaperOrder right)
    {
        return left.Id == right.Id &&
            left.SignalId == right.SignalId &&
            StrategyIds.Normalize(left.StrategyId) == StrategyIds.Normalize(right.StrategyId) &&
            string.Equals(left.CopiedTraderWallet, right.CopiedTraderWallet, StringComparison.Ordinal) &&
            left.Side == right.Side &&
            string.Equals(left.AssetId, right.AssetId, StringComparison.Ordinal) &&
            string.Equals(left.ConditionId, right.ConditionId, StringComparison.Ordinal) &&
            string.Equals(left.Outcome, right.Outcome, StringComparison.Ordinal) &&
            left.Price == right.Price &&
            left.SizeShares == right.SizeShares &&
            left.NotionalUsd == right.NotionalUsd &&
            SameInitialOrderTimestamp(left.CreatedAtUtc, right.CreatedAtUtc) &&
            SameInitialOrderTimestamp(left.ExpiresAtUtc, right.ExpiresAtUtc) &&
            JsonEquivalent(left.RawDecisionJson, right.RawDecisionJson) &&
            left.CorrelationId == right.CorrelationId;
    }

    private static bool SameImmutableRunShape(StrategyMarketPaperRun left, StrategyMarketPaperRun right)
    {
        return left.Id == right.Id &&
            StrategyIds.Normalize(left.StrategyId) == StrategyIds.Normalize(right.StrategyId) &&
            string.Equals(left.MarketId, right.MarketId, StringComparison.Ordinal) &&
            string.Equals(left.ConditionId, right.ConditionId, StringComparison.Ordinal) &&
            string.Equals(left.MarketSlug, right.MarketSlug, StringComparison.Ordinal) &&
            string.Equals(left.MarketTitle, right.MarketTitle, StringComparison.Ordinal) &&
            string.Equals(left.Category, right.Category, StringComparison.Ordinal) &&
            SameNullableTimestamp(left.MarketStartUtc, right.MarketStartUtc) &&
            SameNullableTimestamp(left.MarketEndUtc, right.MarketEndUtc) &&
            SameTimestamp(left.DetectedAtUtc, right.DetectedAtUtc) &&
            SameTimestamp(left.EntryDueAtUtc, right.EntryDueAtUtc) &&
            string.Equals(left.SelectedAssetId, right.SelectedAssetId, StringComparison.Ordinal) &&
            string.Equals(left.SelectedOutcome, right.SelectedOutcome, StringComparison.Ordinal) &&
            left.SignalId == right.SignalId &&
            left.PaperOrderId == right.PaperOrderId &&
            SameTimestamp(left.CreatedAtUtc, right.CreatedAtUtc);
    }

    private static bool OrderEquivalent(PaperOrder left, PaperOrder right)
    {
        return SameImmutableOrderShape(left, right) &&
            left.Status == right.Status &&
            SameNullableTimestamp(left.FilledAtUtc, right.FilledAtUtc) &&
            SameNullableTimestamp(left.CancelledAtUtc, right.CancelledAtUtc) &&
            string.Equals(left.ExecutionSource, right.ExecutionSource, StringComparison.Ordinal);
    }

    private static bool FillEquivalent(PaperFill left, PaperFill right)
    {
        return left.Id == right.Id &&
            left.PaperOrderId == right.PaperOrderId &&
            left.Price == right.Price &&
            left.SizeShares == right.SizeShares &&
            SameTimestamp(left.FilledAtUtc, right.FilledAtUtc) &&
            string.Equals(left.Evidence, right.Evidence, StringComparison.Ordinal) &&
            left.RealizedPnlUsd == right.RealizedPnlUsd &&
            left.FeeUsd == right.FeeUsd &&
            string.Equals(left.FeeAccountingStatus, right.FeeAccountingStatus, StringComparison.Ordinal) &&
            string.Equals(left.FeeLiquidityRole, right.FeeLiquidityRole, StringComparison.Ordinal) &&
            string.Equals(left.FeeCalculationSource, right.FeeCalculationSource, StringComparison.Ordinal) &&
            left.FeeRate == right.FeeRate &&
            left.FeeExponent == right.FeeExponent &&
            left.FeeTakerOnly == right.FeeTakerOnly &&
            SameNullableTimestamp(left.FeeCalculatedAtUtc, right.FeeCalculatedAtUtc) &&
            left.NetRealizedPnlUsd == right.NetRealizedPnlUsd;
    }

    private static bool RunEquivalent(StrategyMarketPaperRun left, StrategyMarketPaperRun right)
    {
        return SameImmutableRunShape(left, right) &&
            string.Equals(left.Status, right.Status, StringComparison.Ordinal) &&
            left.EntryPrice == right.EntryPrice &&
            left.StakeUsd == right.StakeUsd &&
            left.SizeShares == right.SizeShares &&
            SameNullableTimestamp(left.EnteredAtUtc, right.EnteredAtUtc) &&
            left.SettlementPrice == right.SettlementPrice &&
            left.SettlementValueUsd == right.SettlementValueUsd &&
            left.RealizedPnlUsd == right.RealizedPnlUsd &&
            SameNullableTimestamp(left.SettledAtUtc, right.SettledAtUtc) &&
            string.Equals(left.SkipReason, right.SkipReason, StringComparison.Ordinal) &&
            JsonEquivalent(left.SkipDiagnosticsJson, right.SkipDiagnosticsJson) &&
            SameTimestamp(left.UpdatedAtUtc, right.UpdatedAtUtc) &&
            left.FeeUsd == right.FeeUsd &&
            string.Equals(left.FeeAccountingStatus, right.FeeAccountingStatus, StringComparison.Ordinal) &&
            string.Equals(left.FeeLiquidityRole, right.FeeLiquidityRole, StringComparison.Ordinal) &&
            string.Equals(left.FeeCalculationSource, right.FeeCalculationSource, StringComparison.Ordinal) &&
            left.FeeRate == right.FeeRate &&
            left.FeeExponent == right.FeeExponent &&
            left.FeeTakerOnly == right.FeeTakerOnly &&
            SameNullableTimestamp(left.FeeCalculatedAtUtc, right.FeeCalculatedAtUtc) &&
            left.NetRealizedPnlUsd == right.NetRealizedPnlUsd;
    }

    private static bool JsonEquivalent(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(left), JsonNode.Parse(right));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ValidateJson(string? json, string parameterName)
    {
        if (json is null)
        {
            return;
        }

        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Maker-GTD skip diagnostics must be valid JSON.", parameterName, exception);
        }
    }

    private static bool SameNullableTimestamp(DateTimeOffset? left, DateTimeOffset? right)
    {
        return left is null || right is null
            ? left is null && right is null
            : SameTimestamp(left.Value, right.Value);
    }

    private static bool SameTimestamp(DateTimeOffset left, DateTimeOffset right)
    {
        return left.UtcDateTime.Ticks / 10 == right.UtcDateTime.Ticks / 10;
    }

    private static bool SameInitialOrderTimestamp(DateTimeOffset left, DateTimeOffset right)
    {
        return ToJsonbRecordsetMicroseconds(left) == ToJsonbRecordsetMicroseconds(right);
    }

    private static long ToJsonbRecordsetMicroseconds(DateTimeOffset value)
    {
        const long ticksPerMicrosecond = 10;
        var utcTicks = value.UtcDateTime.Ticks;
        var wholeMicroseconds = utcTicks / ticksPerMicrosecond;
        return utcTicks % ticksPerMicrosecond >= ticksPerMicrosecond / 2
            ? wholeMicroseconds + 1
            : wholeMicroseconds;
    }

    private static void AddMismatch(
        ICollection<MakerGtdPaperMismatchDetail> mismatches,
        string predicate,
        bool failed)
    {
        if (failed)
        {
            mismatches.Add(new MakerGtdPaperMismatchDetail(predicate));
        }
    }

    private static void AddNumericMismatch(
        ICollection<MakerGtdPaperMismatchDetail> mismatches,
        string predicate,
        decimal current,
        decimal requested)
    {
        if (current != requested)
        {
            mismatches.Add(new MakerGtdPaperMismatchDetail(
                predicate,
                CurrentValue: current.ToString(CultureInfo.InvariantCulture),
                RequestedValue: requested.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static void AddInitialTimestampMismatch(
        ICollection<MakerGtdPaperMismatchDetail> mismatches,
        string predicate,
        DateTimeOffset current,
        DateTimeOffset requested)
    {
        if (!SameInitialOrderTimestamp(current, requested))
        {
            mismatches.Add(new MakerGtdPaperMismatchDetail(
                predicate,
                CurrentUtc: current.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                RequestedUtc: requested.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                CurrentUtcTicks: current.UtcDateTime.Ticks,
                RequestedUtcTicks: requested.UtcDateTime.Ticks,
                CurrentPostgresMicroseconds: ToJsonbRecordsetMicroseconds(current),
                RequestedPostgresMicroseconds: ToJsonbRecordsetMicroseconds(requested)));
        }
    }

    private static string Sha256Fingerprint(string? value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? "null");
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static bool PositionMarkIsInvalid(this MakerGtdPaperFullFillRequest request)
    {
        return request.Position.SizeShares <= 0m ||
            request.Position.AveragePrice is <= 0m or >= 1m ||
            request.Position.EstimatedValueUsd < 0m;
    }
}
