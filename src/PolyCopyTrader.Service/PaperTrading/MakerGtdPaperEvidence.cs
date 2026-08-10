using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Service.PaperTrading;

internal sealed record MakerGtdPaperAcceptedMarketDataStatus(
    MarketDataConnectionState ConnectionState,
    bool Stale,
    int ReconnectCount,
    DateTimeOffset? LastConnectedUtc,
    DateTimeOffset? LastDisconnectedUtc,
    bool AssetSubscribed,
    bool? AssetConfirmedLive,
    string? AssetSubscriptionComponent,
    int SubscribedAssetsCount,
    long? ContinuityGeneration,
    long? AssetSubscriptionGeneration,
    string? AssetSubscriptionSessionId,
    DateTimeOffset AcceptedAtUtc);

internal sealed record MakerGtdPaperOrderEvidence(
    DateTimeOffset AcceptedAtUtc,
    DateTimeOffset EffectiveExpiresAtUtc,
    MakerGtdPaperAcceptedMarketDataStatus AcceptedMarketDataStatus);

internal sealed record MakerGtdPaperContinuityEvaluation(
    bool Continuous,
    string ReasonCode,
    string Detail);

internal static class MakerGtdPaperOrderEvidenceParser
{
    private const string MakerGtdProperty = "maker_gtd";
    private const string MarketDataStatusProperty = "market_data_status_at_acceptance";
    private const string PairedProperty = "pair";
    private const string FrozenIntentProperty = "frozen_intent";
    private const string FirstAcceptingObservationProperty = "first_accepting_observation";

    public static bool TryParse(
        PaperOrder order,
        out MakerGtdPaperOrderEvidence? evidence,
        out string failureDetail)
    {
        evidence = null;
        failureDetail = string.Empty;
        var referenceAverageVariant = StrategyIds.UpDown5mStrategyVariants
            .SingleOrDefault(variant =>
                variant.Id == order.StrategyId &&
                MakerGtdPaperExecutionContract.IsApprovedCurrentStrategyVariant(variant));
        var hasReferenceAverageSource = string.Equals(
            order.ExecutionSource,
            MakerGtdPaperExecutionContract.ExecutionSource,
            StringComparison.Ordinal);
        if (hasReferenceAverageSource != (referenceAverageVariant is not null))
        {
            failureDetail = hasReferenceAverageSource
                ? "reference_average_strategy_not_approved"
                : "reference_average_execution_source_mismatch";
            return false;
        }

        var pairedVariant = StrategyIds.PairedMakerGtdFirstAcceptingVariants
            .SingleOrDefault(variant => variant.Id == order.StrategyId);
        var hasPairedSource = string.Equals(
            order.ExecutionSource,
            PairedMakerGtdPaperExecutionContract.ExecutionSource,
            StringComparison.Ordinal);
        if (hasPairedSource != (pairedVariant is not null))
        {
            failureDetail = hasPairedSource
                ? "paired_strategy_not_approved"
                : "paired_execution_source_mismatch";
            return false;
        }

        if (!MakerGtdPaperExecutionContract.IsMakerGtdOrder(order))
        {
            failureDetail = "execution_source_mismatch";
            return false;
        }

        if (string.IsNullOrWhiteSpace(order.RawDecisionJson))
        {
            failureDetail = "raw_decision_json_missing";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(order.RawDecisionJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failureDetail = "raw_decision_json_not_object";
                return false;
            }

            if (!TryGetObject(root, MakerGtdProperty, out var makerGtd))
            {
                failureDetail = "maker_gtd_missing";
                return false;
            }

            if (!TryGetRequiredTimestamp(makerGtd, "accepted_at_utc", out var acceptedAtUtc) ||
                !TryGetRequiredTimestamp(
                    makerGtd,
                    "effective_expires_at_utc",
                    out var effectiveExpiresAtUtc))
            {
                failureDetail = "maker_gtd_lifetime_invalid";
                return false;
            }

            if (!TryGetObject(root, MarketDataStatusProperty, out var statusRoot) ||
                !TryGetRequiredEnum(
                    statusRoot,
                    "connection_state",
                    out MarketDataConnectionState connectionState) ||
                !TryGetRequiredBoolean(statusRoot, "stale", out var stale) ||
                !TryGetRequiredNonNegativeInt32(
                    statusRoot,
                    "reconnect_count",
                    out var reconnectCount) ||
                !TryGetOptionalTimestamp(
                    statusRoot,
                    "last_connected_utc",
                    out var lastConnectedUtc) ||
                !TryGetOptionalTimestamp(
                    statusRoot,
                    "last_disconnected_utc",
                    out var lastDisconnectedUtc) ||
                !TryGetRequiredBoolean(
                    statusRoot,
                    "asset_subscribed",
                    out var assetSubscribed) ||
                !TryGetOptionalBoolean(
                    statusRoot,
                    "asset_confirmed_live",
                    out var assetConfirmedLive) ||
                !TryGetOptionalString(
                    statusRoot,
                    "asset_subscription_component",
                    out var assetSubscriptionComponent) ||
                !TryGetRequiredNonNegativeInt32(
                    statusRoot,
                    "subscribed_assets_count",
                    out var subscribedAssetsCount) ||
                !TryGetOptionalNonNegativeInt64(
                    statusRoot,
                    "continuity_generation",
                    out var continuityGeneration) ||
                !TryGetOptionalNonNegativeInt64(
                    statusRoot,
                    "asset_subscription_generation",
                    out var assetSubscriptionGeneration) ||
                !TryGetOptionalString(
                    statusRoot,
                    "asset_subscription_session_id",
                    out var assetSubscriptionSessionId) ||
                !TryGetRequiredTimestamp(
                    statusRoot,
                    "accepted_at_utc",
                    out var statusAcceptedAtUtc))
            {
                failureDetail = "acceptance_market_data_status_invalid";
                return false;
            }

            if (!SameTimestamp(acceptedAtUtc, statusAcceptedAtUtc))
            {
                failureDetail = "acceptance_timestamp_mismatch";
                return false;
            }

            if (!SameTimestamp(effectiveExpiresAtUtc, order.ExpiresAtUtc) ||
                acceptedAtUtc < order.CreatedAtUtc ||
                acceptedAtUtc >= effectiveExpiresAtUtc)
            {
                failureDetail = "order_lifetime_mismatch";
                return false;
            }

            if (pairedVariant is not null &&
                !TryValidatePairedContract(
                    order,
                    root,
                    makerGtd,
                    statusRoot,
                    pairedVariant,
                    acceptedAtUtc,
                    effectiveExpiresAtUtc,
                    out failureDetail))
            {
                return false;
            }

            if (referenceAverageVariant is not null &&
                !TryValidateReferenceAverageContract(
                    order,
                    root,
                    makerGtd,
                    referenceAverageVariant,
                    acceptedAtUtc,
                    effectiveExpiresAtUtc,
                    out failureDetail))
            {
                return false;
            }

            evidence = new MakerGtdPaperOrderEvidence(
                acceptedAtUtc,
                effectiveExpiresAtUtc,
                new MakerGtdPaperAcceptedMarketDataStatus(
                    connectionState,
                    stale,
                    reconnectCount,
                    lastConnectedUtc,
                    lastDisconnectedUtc,
                    assetSubscribed,
                    assetConfirmedLive,
                    assetSubscriptionComponent,
                    subscribedAssetsCount,
                    continuityGeneration,
                    assetSubscriptionGeneration,
                    assetSubscriptionSessionId,
                    statusAcceptedAtUtc));
            return true;
        }
        catch (JsonException)
        {
            failureDetail = "raw_decision_json_invalid";
            return false;
        }
    }

    private static bool TryValidateReferenceAverageContract(
        PaperOrder order,
        JsonElement root,
        JsonElement makerGtd,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset acceptedAtUtc,
        DateTimeOffset effectiveExpiresAtUtc,
        out string failureDetail)
    {
        failureDetail = "reference_average_contract_invalid";
        if (!MakerGtdPaperExecutionContract.IsApprovedCurrentStrategyVariant(variant) ||
            variant.MakerMaximumOrderPrice != StrategyIds.ReferenceAverageMakerGtdMaximumOrderPrice ||
            order.Side != TradeSide.Buy ||
            order.Outcome is not ("Up" or "Down") ||
            string.IsNullOrWhiteSpace(order.AssetId) ||
            string.IsNullOrWhiteSpace(order.ConditionId) ||
            order.Price <= 0m ||
            order.Price > StrategyIds.ReferenceAverageMakerGtdMaximumOrderPrice ||
            order.SizeShares <= 0m ||
            order.NotionalUsd != order.Price * order.SizeShares ||
            !SameTimestamp(order.CreatedAtUtc, acceptedAtUtc))
        {
            failureDetail = "reference_average_order_catalog_mismatch";
            return false;
        }

        if (!TryGetRequiredString(root, "execution_source", out var rootExecutionSource) ||
            !string.Equals(
                rootExecutionSource,
                MakerGtdPaperExecutionContract.ExecutionSource,
                StringComparison.Ordinal) ||
            !TryGetRequiredBoolean(root, "paper_only", out var rootPaperOnly) ||
            !rootPaperOnly ||
            !TryGetRequiredBoolean(root, "post_only", out var rootPostOnly) ||
            !rootPostOnly ||
            !TryGetRequiredString(root, "order_type", out var rootOrderType) ||
            !string.Equals(rootOrderType, "GTD", StringComparison.Ordinal))
        {
            failureDetail = "reference_average_root_contract_mismatch";
            return false;
        }

        if (!TryGetRequiredString(makerGtd, "contract_version", out var contractVersion) ||
            !TryGetRequiredString(makerGtd, "price_formula", out var priceFormula) ||
            !IsApprovedReferenceAverageContractAndFormula(contractVersion, priceFormula) ||
            !TryGetRequiredString(makerGtd, "execution_source", out var makerExecutionSource) ||
            !string.Equals(
                makerExecutionSource,
                MakerGtdPaperExecutionContract.ExecutionSource,
                StringComparison.Ordinal) ||
            !TryGetRequiredBoolean(makerGtd, "paper_only", out var makerPaperOnly) ||
            !makerPaperOnly ||
            !TryGetRequiredBoolean(makerGtd, "post_only", out var makerPostOnly) ||
            !makerPostOnly ||
            !TryGetRequiredString(makerGtd, "order_type", out var makerOrderType) ||
            !string.Equals(makerOrderType, "GTD", StringComparison.Ordinal) ||
            !TryGetRequiredNonNegativeInt32(
                makerGtd,
                "maximum_placement_attempts",
                out var maximumPlacementAttempts) ||
            maximumPlacementAttempts != 10 ||
            !TryGetRequiredNonNegativeInt32(
                makerGtd,
                "attempts_completed",
                out var attemptsCompleted) ||
            attemptsCompleted is < 1 or > 10 ||
            !TryGetRequiredDecimal(
                makerGtd,
                "maximum_order_price",
                out var maximumOrderPrice) ||
            maximumOrderPrice != StrategyIds.ReferenceAverageMakerGtdMaximumOrderPrice ||
            !TryGetRequiredTimestamp(makerGtd, "market_start_utc", out var marketStartUtc) ||
            !TryGetRequiredTimestamp(makerGtd, "market_end_utc", out var marketEndUtc) ||
            !TryGetRequiredTimestamp(
                makerGtd,
                "clob_gtd_expiration_utc",
                out var clobGtdExpirationUtc) ||
            !SameTimestamp(marketEndUtc, clobGtdExpirationUtc) ||
            !SameTimestamp(marketEndUtc, marketStartUtc.AddMinutes(5)) ||
            !SameTimestamp(marketEndUtc, effectiveExpiresAtUtc.AddSeconds(60)))
        {
            failureDetail = "reference_average_maker_gtd_contract_mismatch";
            return false;
        }

        if (!TryGetObject(makerGtd, FrozenIntentProperty, out var frozenIntent) ||
            !TryGetRequiredGuid(frozenIntent, "strategy_id", out var intentStrategyId) ||
            intentStrategyId != order.StrategyId ||
            !TryGetRequiredString(frozenIntent, "condition_id", out var intentConditionId) ||
            !string.Equals(intentConditionId, order.ConditionId, StringComparison.Ordinal) ||
            !TryGetRequiredString(frozenIntent, "asset_id", out var intentAssetId) ||
            !string.Equals(intentAssetId, order.AssetId, StringComparison.Ordinal) ||
            !TryGetRequiredString(frozenIntent, "side", out var intentSide) ||
            !string.Equals(intentSide, TradeSide.Buy.ToString(), StringComparison.Ordinal) ||
            !TryGetRequiredBoolean(frozenIntent, "post_only", out var intentPostOnly) ||
            !intentPostOnly ||
            !TryGetRequiredString(frozenIntent, "order_type", out var intentOrderType) ||
            !string.Equals(intentOrderType, "GTD", StringComparison.Ordinal) ||
            !TryGetRequiredDecimal(
                frozenIntent,
                "maximum_order_price",
                out var intentMaximumOrderPrice) ||
            intentMaximumOrderPrice != maximumOrderPrice ||
            !TryGetRequiredDecimal(frozenIntent, "limit_price", out var intentLimitPrice) ||
            intentLimitPrice != order.Price ||
            !TryGetRequiredPositiveDecimal(
                frozenIntent,
                "target_size_shares",
                out var intentTargetSizeShares) ||
            intentTargetSizeShares != order.SizeShares ||
            !TryGetRequiredPositiveDecimal(
                frozenIntent,
                "target_notional_usd",
                out var intentTargetNotionalUsd) ||
            intentTargetNotionalUsd != order.NotionalUsd ||
            !TryGetRequiredPositiveDecimal(frozenIntent, "tick_size", out var tickSize) ||
            order.Price % tickSize != 0m ||
            !TryGetRequiredTimestamp(
                frozenIntent,
                "decision_snapshot_at_utc",
                out var decisionSnapshotAtUtc) ||
            !TryGetRequiredTimestamp(frozenIntent, "frozen_at_utc", out var frozenAtUtc) ||
            decisionSnapshotAtUtc > frozenAtUtc ||
            frozenAtUtc > acceptedAtUtc ||
            !TryGetRequiredTimestamp(
                frozenIntent,
                "effective_expires_at_utc",
                out var intentEffectiveExpiresAtUtc) ||
            !SameTimestamp(intentEffectiveExpiresAtUtc, effectiveExpiresAtUtc) ||
            !TryGetRequiredTimestamp(
                frozenIntent,
                "clob_gtd_expiration_utc",
                out var intentClobGtdExpirationUtc) ||
            !SameTimestamp(intentClobGtdExpirationUtc, clobGtdExpirationUtc))
        {
            failureDetail = "reference_average_frozen_intent_mismatch";
            return false;
        }

        if (!TryValidateReferenceAverageAcceptedAttempt(
                makerGtd,
                order,
                contractVersion,
                maximumOrderPrice,
                tickSize,
                attemptsCompleted,
                acceptedAtUtc))
        {
            failureDetail = "reference_average_attempt_evidence_mismatch";
            return false;
        }

        return true;
    }

    private static bool IsApprovedReferenceAverageContractAndFormula(
        string contractVersion,
        string priceFormula)
    {
        return string.Equals(
                   contractVersion,
                   MakerGtdPaperExecutionContract.LegacyContractVersion,
                   StringComparison.Ordinal) &&
               string.Equals(
                   priceFormula,
                   MakerGtdPaperExecutionContract.LegacyPriceFormula,
                   StringComparison.Ordinal) ||
            string.Equals(
                   contractVersion,
                   MakerGtdPaperExecutionContract.CurrentContractVersion,
                   StringComparison.Ordinal) &&
               string.Equals(
                   priceFormula,
                   MakerGtdPaperExecutionContract.CurrentPriceFormula,
                   StringComparison.Ordinal);
    }

    private static bool TryValidateReferenceAverageAcceptedAttempt(
        JsonElement makerGtd,
        PaperOrder order,
        string contractVersion,
        decimal maximumOrderPrice,
        decimal frozenTickSize,
        int attemptsCompleted,
        DateTimeOffset acceptedAtUtc)
    {
        if (!makerGtd.TryGetProperty("attempts", out var attempts) ||
            attempts.ValueKind != JsonValueKind.Array ||
            attempts.GetArrayLength() != attemptsCompleted)
        {
            return false;
        }

        var acceptedAttempt = attempts.EnumerateArray().Last();
        if (acceptedAttempt.ValueKind != JsonValueKind.Object ||
            !TryGetRequiredString(acceptedAttempt, "outcome", out var outcome) ||
            !string.Equals(outcome, "accepted_resting", StringComparison.Ordinal) ||
            !TryGetRequiredTimestamp(acceptedAttempt, "accepted_at_utc", out var attemptAcceptedAtUtc) ||
            !SameTimestamp(attemptAcceptedAtUtc, acceptedAtUtc) ||
            !TryGetRequiredDecimal(acceptedAttempt, "limit_price", out var attemptLimitPrice) ||
            attemptLimitPrice != order.Price ||
            !TryGetRequiredPositiveDecimal(acceptedAttempt, "tick_size", out var attemptTickSize) ||
            attemptTickSize != frozenTickSize ||
            !TryGetObject(acceptedAttempt, "s0", out var s0) ||
            !TryGetRequiredString(s0, "asset_id", out var s0AssetId) ||
            !string.Equals(s0AssetId, order.AssetId, StringComparison.Ordinal) ||
            !TryGetRequiredString(s0, "condition_id", out var s0ConditionId) ||
            !string.Equals(s0ConditionId, order.ConditionId, StringComparison.Ordinal) ||
            !TryGetRequiredBoolean(s0, "is_current", out var s0IsCurrent) ||
            !s0IsCurrent ||
            !TryGetRequiredBoolean(
                s0,
                "timestamp_is_authoritative",
                out var s0TimestampIsAuthoritative) ||
            !s0TimestampIsAuthoritative ||
            !TryGetRequiredPositiveDecimal(s0, "best_bid", out var s0BestBid) ||
            !TryGetRequiredPositiveDecimal(s0, "best_ask", out var s0BestAsk) ||
            !TryGetRequiredPositiveDecimal(s0, "tick_size", out var s0TickSize) ||
            s0TickSize != frozenTickSize ||
            !TryGetObject(acceptedAttempt, FrozenIntentProperty, out var attemptIntent) ||
            !TryGetRequiredGuid(attemptIntent, "strategy_id", out var attemptStrategyId) ||
            attemptStrategyId != order.StrategyId ||
            !TryGetRequiredDecimal(attemptIntent, "limit_price", out var attemptIntentPrice) ||
            attemptIntentPrice != order.Price ||
            !TryGetObject(acceptedAttempt, "s1", out var s1) ||
            !TryGetRequiredString(s1, "asset_id", out var s1AssetId) ||
            !string.Equals(s1AssetId, order.AssetId, StringComparison.Ordinal) ||
            !TryGetRequiredString(s1, "condition_id", out var s1ConditionId) ||
            !string.Equals(s1ConditionId, order.ConditionId, StringComparison.Ordinal) ||
            !TryGetRequiredBoolean(s1, "is_current", out var s1IsCurrent) ||
            !s1IsCurrent ||
            !TryGetRequiredBoolean(
                s1,
                "timestamp_is_authoritative",
                out var s1TimestampIsAuthoritative) ||
            !s1TimestampIsAuthoritative ||
            !TryGetRequiredPositiveDecimal(s1, "best_ask", out var s1BestAsk) ||
            order.Price >= s1BestAsk)
        {
            return false;
        }

        var rawExpectedLimit = string.Equals(
                contractVersion,
                MakerGtdPaperExecutionContract.LegacyContractVersion,
                StringComparison.Ordinal)
            ? Math.Min(
                Math.Min(s0BestBid + s0TickSize, s0BestAsk - s0TickSize),
                maximumOrderPrice)
            : Math.Min(s0BestAsk - s0TickSize, maximumOrderPrice);
        if (rawExpectedLimit <= 0m)
        {
            return false;
        }

        var expectedLimit = decimal.Floor(rawExpectedLimit / s0TickSize) * s0TickSize;
        return attemptLimitPrice == expectedLimit;
    }

    private static bool TryValidatePairedContract(
        PaperOrder order,
        JsonElement root,
        JsonElement makerGtd,
        JsonElement statusRoot,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset acceptedAtUtc,
        DateTimeOffset effectiveExpiresAtUtc,
        out string failureDetail)
    {
        failureDetail = "paired_contract_invalid";
        if (!PairedMakerGtdPaperExecutionContract.IsApprovedStrategyVariant(variant) ||
            variant.FixedOutcome is not { } fixedOutcome ||
            variant.PairedStrategyId is not { } pairedStrategyId ||
            variant.MakerMaximumOrderPrice is not { } maximumOrderPrice ||
            order.Side != TradeSide.Buy ||
            !string.Equals(order.Outcome, fixedOutcome.ToString(), StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(order.AssetId) ||
            string.IsNullOrWhiteSpace(order.ConditionId) ||
            order.Price <= 0m ||
            order.Price > maximumOrderPrice ||
            order.SizeShares <= 0m ||
            order.NotionalUsd != order.Price * order.SizeShares ||
            !SameTimestamp(order.CreatedAtUtc, acceptedAtUtc))
        {
            failureDetail = "paired_order_catalog_mismatch";
            return false;
        }

        if (!TryGetRequiredString(root, "execution_source", out var rootExecutionSource) ||
            !string.Equals(
                rootExecutionSource,
                PairedMakerGtdPaperExecutionContract.ExecutionSource,
                StringComparison.Ordinal) ||
            !TryGetRequiredString(root, "paper_model_label", out var paperModelLabel) ||
            !string.Equals(
                paperModelLabel,
                PairedMakerGtdPaperExecutionContract.MandatoryLabel,
                StringComparison.Ordinal) ||
            !TryGetRequiredBoolean(root, "paper_only", out var paperOnly) ||
            !paperOnly ||
            !TryGetRequiredBoolean(root, "post_only", out var rootPostOnly) ||
            !rootPostOnly ||
            !TryGetRequiredString(root, "order_type", out var rootOrderType) ||
            !string.Equals(rootOrderType, "GTD", StringComparison.Ordinal) ||
            !TryGetRequiredBoolean(root, "maker_rebate_modeled", out var makerRebateModeled) ||
            makerRebateModeled)
        {
            failureDetail = "paired_root_contract_mismatch";
            return false;
        }

        if (!TryGetObject(root, PairedProperty, out var pair) ||
            !TryGetRequiredString(pair, "pair_id", out var pairId) ||
            !string.Equals(
                pairId,
                $"{variant.ReferenceAssetSymbol}:{order.ConditionId}",
                StringComparison.Ordinal) ||
            !TryGetRequiredGuid(pair, "strategy_id", out var pairStrategyId) ||
            pairStrategyId != order.StrategyId ||
            !TryGetRequiredGuid(pair, "paired_strategy_id", out var actualPairedStrategyId) ||
            actualPairedStrategyId != pairedStrategyId ||
            !TryGetExactPairStrategyIds(
                pair,
                order.StrategyId,
                pairedStrategyId) ||
            !TryGetRequiredPositiveDecimal(
                pair,
                "common_requested_size_shares",
                out var commonRequestedSizeShares) ||
            commonRequestedSizeShares != order.SizeShares ||
            !TryGetRequiredTimestamp(
                pair,
                "common_size_frozen_at_utc",
                out var commonSizeFrozenAtUtc) ||
            commonSizeFrozenAtUtc > acceptedAtUtc ||
            !TryGetRequiredBoolean(pair, "atomic", out var atomic) ||
            atomic ||
            !TryGetRequiredBoolean(pair, "rollback", out var rollback) ||
            rollback)
        {
            failureDetail = "paired_linkage_mismatch";
            return false;
        }

        if (!TryGetRequiredString(makerGtd, "contract_version", out var contractVersion) ||
            !PairedMakerGtdPaperExecutionContract.IsSupportedContractVersion(contractVersion) ||
            !TryGetRequiredString(makerGtd, "execution_source", out var makerExecutionSource) ||
            !string.Equals(
                makerExecutionSource,
                PairedMakerGtdPaperExecutionContract.ExecutionSource,
                StringComparison.Ordinal) ||
            !TryGetRequiredBoolean(makerGtd, "paper_only", out var makerPaperOnly) ||
            !makerPaperOnly ||
            !TryGetRequiredBoolean(makerGtd, "post_only", out var makerPostOnly) ||
            !makerPostOnly ||
            !TryGetRequiredString(makerGtd, "order_type", out var makerOrderType) ||
            !string.Equals(makerOrderType, "GTD", StringComparison.Ordinal) ||
            !TryGetRequiredNonNegativeInt32(
                makerGtd,
                "maximum_placement_attempts",
                out var maximumPlacementAttempts) ||
            maximumPlacementAttempts != 10 ||
            !TryGetRequiredNonNegativeInt32(
                makerGtd,
                "attempts_completed",
                out var attemptsCompleted) ||
            attemptsCompleted is < 1 or > 10 ||
            !TryGetRequiredString(makerGtd, "price_formula", out var priceFormula) ||
            !string.Equals(
                priceFormula,
                PairedMakerGtdPaperExecutionContract.PriceFormula,
                StringComparison.Ordinal) ||
            !TryGetRequiredDecimal(
                makerGtd,
                "maximum_order_price",
                out var makerMaximumOrderPrice) ||
            makerMaximumOrderPrice != maximumOrderPrice ||
            !TryGetRequiredTimestamp(makerGtd, "market_start_utc", out var marketStartUtc) ||
            !TryGetRequiredTimestamp(makerGtd, "market_end_utc", out var marketEndUtc) ||
            !TryGetRequiredTimestamp(
                makerGtd,
                "clob_gtd_expiration_utc",
                out var clobGtdExpirationUtc) ||
            !SameTimestamp(marketEndUtc, marketStartUtc.AddMinutes(5)) ||
            !SameTimestamp(
                clobGtdExpirationUtc,
                effectiveExpiresAtUtc.AddSeconds(60)) ||
            marketStartUtc >= effectiveExpiresAtUtc ||
            acceptedAtUtc >= marketStartUtc)
        {
            failureDetail = "paired_maker_gtd_contract_mismatch";
            return false;
        }

        var usesMarketEndEffectiveExpiry =
            PairedMakerGtdPaperExecutionContract.UsesMarketEndEffectiveExpiry(contractVersion);
        var expirationContractMatches = usesMarketEndEffectiveExpiry
            ? SameTimestamp(effectiveExpiresAtUtc, marketEndUtc) &&
              SameTimestamp(
                  clobGtdExpirationUtc,
                  marketEndUtc.AddSeconds(MakerGtdBuyExecutionIntent.VenueEarlyExpirationSeconds))
            : SameTimestamp(clobGtdExpirationUtc, marketEndUtc) &&
              SameTimestamp(
                  effectiveExpiresAtUtc.AddSeconds(MakerGtdBuyExecutionIntent.VenueEarlyExpirationSeconds),
                  marketEndUtc);
        if (!expirationContractMatches)
        {
            failureDetail = "paired_maker_gtd_expiration_contract_mismatch";
            return false;
        }

        var usesGapRecoveryLifecycle =
            PairedMakerGtdPaperExecutionContract.UsesGapRecoveryLifecycle(contractVersion);
        var hasExactGapRecoveryPolicy =
            TryGetRequiredString(
                makerGtd,
                "gap_recovery_policy_version",
                out var gapRecoveryPolicyVersion) &&
            string.Equals(
                gapRecoveryPolicyVersion,
                PairedMakerGtdPaperExecutionContract.GapRecoveryLifecyclePolicyVersion,
                StringComparison.Ordinal) &&
            TryGetRequiredBoolean(
                makerGtd,
                "observation_gaps_backfilled",
                out var observationGapsBackfilled) &&
            !observationGapsBackfilled;
        if (usesGapRecoveryLifecycle != hasExactGapRecoveryPolicy ||
            !usesGapRecoveryLifecycle &&
            (makerGtd.TryGetProperty("gap_recovery_policy_version", out _) ||
             makerGtd.TryGetProperty("observation_gaps_backfilled", out _)))
        {
            failureDetail = "paired_lifecycle_contract_mismatch";
            return false;
        }

        if (!TryGetObject(makerGtd, FrozenIntentProperty, out var frozenIntent) ||
            !TryGetRequiredGuid(frozenIntent, "strategy_id", out var intentStrategyId) ||
            intentStrategyId != order.StrategyId ||
            !TryGetRequiredString(frozenIntent, "condition_id", out var intentConditionId) ||
            !string.Equals(intentConditionId, order.ConditionId, StringComparison.Ordinal) ||
            !TryGetRequiredString(frozenIntent, "asset_id", out var intentAssetId) ||
            !string.Equals(intentAssetId, order.AssetId, StringComparison.Ordinal) ||
            !TryGetRequiredString(frozenIntent, "side", out var intentSide) ||
            !string.Equals(intentSide, TradeSide.Buy.ToString(), StringComparison.Ordinal) ||
            !TryGetRequiredBoolean(frozenIntent, "post_only", out var intentPostOnly) ||
            !intentPostOnly ||
            !TryGetRequiredString(frozenIntent, "order_type", out var intentOrderType) ||
            !string.Equals(intentOrderType, "GTD", StringComparison.Ordinal) ||
            !TryGetRequiredDecimal(
                frozenIntent,
                "maximum_order_price",
                out var intentMaximumOrderPrice) ||
            intentMaximumOrderPrice != maximumOrderPrice ||
            !TryGetRequiredDecimal(frozenIntent, "limit_price", out var intentLimitPrice) ||
            intentLimitPrice != order.Price ||
            !TryGetRequiredPositiveDecimal(
                frozenIntent,
                "requested_notional_usd",
                out var intentRequestedNotionalUsd) ||
            intentRequestedNotionalUsd != order.NotionalUsd ||
            !TryGetRequiredPositiveDecimal(
                frozenIntent,
                "requested_size_shares",
                out var intentRequestedSizeShares) ||
            intentRequestedSizeShares != commonRequestedSizeShares ||
            !TryGetRequiredPositiveDecimal(
                frozenIntent,
                "target_size_shares",
                out var intentTargetSizeShares) ||
            intentTargetSizeShares != order.SizeShares ||
            !TryGetRequiredPositiveDecimal(
                frozenIntent,
                "target_notional_usd",
                out var intentTargetNotionalUsd) ||
            intentTargetNotionalUsd != order.NotionalUsd ||
            !TryGetRequiredPositiveDecimal(frozenIntent, "tick_size", out var tickSize) ||
            order.Price % tickSize != 0m ||
            !TryGetRequiredTimestamp(
                frozenIntent,
                "decision_snapshot_at_utc",
                out var decisionSnapshotAtUtc) ||
            !TryGetRequiredTimestamp(frozenIntent, "frozen_at_utc", out var frozenAtUtc) ||
            decisionSnapshotAtUtc > frozenAtUtc ||
            frozenAtUtc > acceptedAtUtc ||
            !TryGetRequiredTimestamp(
                frozenIntent,
                "effective_expires_at_utc",
                out var intentEffectiveExpiresAtUtc) ||
            !SameTimestamp(intentEffectiveExpiresAtUtc, effectiveExpiresAtUtc) ||
            !TryGetRequiredTimestamp(
                frozenIntent,
                "clob_gtd_expiration_utc",
                out var intentClobGtdExpirationUtc) ||
            !SameTimestamp(intentClobGtdExpirationUtc, clobGtdExpirationUtc))
        {
            failureDetail = "paired_frozen_intent_mismatch";
            return false;
        }

        var intentMinOrderSize = 0m;
        var intentNegativeRisk = false;
        if (!string.Equals(
                contractVersion,
                PairedMakerGtdPaperExecutionContract.LegacyContractVersion,
                StringComparison.Ordinal) &&
            (!TryGetRequiredPositiveDecimal(
                 frozenIntent,
                 "min_order_size",
                 out intentMinOrderSize) ||
             !TryGetRequiredBoolean(
                 frozenIntent,
                 "negative_risk",
                 out intentNegativeRisk)))
        {
            failureDetail = "paired_frozen_intent_mismatch";
            return false;
        }

        if (!TryValidatePairedAcceptedAttempt(
                makerGtd,
                order,
                contractVersion,
                maximumOrderPrice,
                tickSize,
                intentMinOrderSize,
                intentNegativeRisk,
                attemptsCompleted,
                frozenAtUtc,
                acceptedAtUtc))
        {
            failureDetail = "paired_attempt_evidence_mismatch";
            return false;
        }

        if (!TryGetObject(root, FirstAcceptingObservationProperty, out var observation) ||
            !TryGetRequiredString(observation, "phase", out var observationPhase) ||
            !string.Equals(
                observationPhase,
                "first_accepting_observed",
                StringComparison.Ordinal) ||
            !TryGetRequiredTimestamp(
                observation,
                "request_started_at_utc",
                out var requestStartedAtUtc) ||
            !TryGetRequiredTimestamp(
                observation,
                "response_completed_at_utc",
                out var responseCompletedAtUtc) ||
            !TryGetRequiredTimestamp(
                observation,
                "first_observed_accepting_at_utc",
                out var firstObservedAcceptingAtUtc) ||
            requestStartedAtUtc > responseCompletedAtUtc ||
            responseCompletedAtUtc > firstObservedAcceptingAtUtc ||
            firstObservedAcceptingAtUtc > acceptedAtUtc ||
            !TryGetRequiredString(observation, "condition_id", out var observationConditionId) ||
            !string.Equals(observationConditionId, order.ConditionId, StringComparison.Ordinal) ||
            !TryGetRequiredBoolean(observation, "accepting_orders", out var acceptingOrders) ||
            !acceptingOrders ||
            !TryGetRequiredStringArray(observation, "clob_token_ids", out var clobTokenIds) ||
            clobTokenIds.Count != 2 ||
            clobTokenIds.Distinct(StringComparer.Ordinal).Count() != 2 ||
            !clobTokenIds.Contains(order.AssetId, StringComparer.Ordinal))
        {
            failureDetail = "paired_first_accepting_evidence_mismatch";
            return false;
        }

        if (!TryGetRequiredNonNegativeInt64(
                statusRoot,
                "continuity_generation",
                out _) ||
            !TryGetRequiredBoolean(
                statusRoot,
                "asset_confirmed_live",
                out var assetConfirmedLive) ||
            !assetConfirmedLive ||
            !TryGetRequiredString(
                statusRoot,
                "asset_subscription_component",
                out _) ||
            !TryGetRequiredNonNegativeInt64(
                statusRoot,
                "asset_subscription_generation",
                out _) ||
            !TryGetRequiredString(
                statusRoot,
                "asset_subscription_session_id",
                out _))
        {
            failureDetail = "paired_continuity_evidence_missing";
            return false;
        }

        return true;
    }

    private static bool TryValidatePairedAcceptedAttempt(
        JsonElement makerGtd,
        PaperOrder order,
        string contractVersion,
        decimal maximumOrderPrice,
        decimal frozenTickSize,
        decimal intentMinOrderSize,
        bool intentNegativeRisk,
        int attemptsCompleted,
        DateTimeOffset frozenAtUtc,
        DateTimeOffset acceptedAtUtc)
    {
        if (!makerGtd.TryGetProperty("attempts", out var attempts) ||
            attempts.ValueKind != JsonValueKind.Array ||
            attempts.GetArrayLength() != attemptsCompleted)
        {
            return false;
        }

        var acceptedAttempt = attempts.EnumerateArray().Last();
        if (acceptedAttempt.ValueKind != JsonValueKind.Object ||
            !TryGetRequiredDecimal(acceptedAttempt, "limit_price", out var attemptLimitPrice) ||
            attemptLimitPrice != order.Price ||
            !TryGetRequiredPositiveDecimal(acceptedAttempt, "tick_size", out var attemptTickSize) ||
            attemptTickSize != frozenTickSize ||
            !TryGetRequiredString(
                acceptedAttempt,
                "acceptance_outcome",
                out var acceptanceOutcome) ||
            !string.Equals(acceptanceOutcome, "AcceptedResting", StringComparison.Ordinal) ||
            !TryGetRequiredTimestamp(
                acceptedAttempt,
                "accepted_at_utc",
                out var attemptAcceptedAtUtc) ||
            !SameTimestamp(attemptAcceptedAtUtc, acceptedAtUtc) ||
            !TryGetRequiredTimestamp(
                acceptedAttempt,
                "s1_received_at_utc",
                out var s1ReceivedAtUtc) ||
            s1ReceivedAtUtc <= frozenAtUtc ||
            s1ReceivedAtUtc > acceptedAtUtc ||
            !TryGetObject(acceptedAttempt, "s0", out var s0) ||
            !TryGetRequiredString(s0, "asset_id", out var s0AssetId) ||
            !string.Equals(s0AssetId, order.AssetId, StringComparison.Ordinal) ||
            !TryGetRequiredString(s0, "condition_id", out var s0ConditionId) ||
            !string.Equals(s0ConditionId, order.ConditionId, StringComparison.Ordinal) ||
            !TryGetRequiredBoolean(s0, "is_current", out var s0IsCurrent) ||
            !s0IsCurrent ||
            !TryGetRequiredBoolean(
                s0,
                "timestamp_is_authoritative",
                out var s0TimestampIsAuthoritative) ||
            !s0TimestampIsAuthoritative ||
            !TryGetRequiredPositiveDecimal(s0, "best_ask", out var s0BestAsk) ||
            !TryGetRequiredPositiveDecimal(s0, "tick_size", out var s0TickSize) ||
            s0TickSize != frozenTickSize ||
            !TryGetObject(acceptedAttempt, FrozenIntentProperty, out var attemptIntent) ||
            !TryGetRequiredGuid(attemptIntent, "strategy_id", out var attemptStrategyId) ||
            attemptStrategyId != order.StrategyId ||
            !TryGetRequiredString(attemptIntent, "asset_id", out var attemptAssetId) ||
            !string.Equals(attemptAssetId, order.AssetId, StringComparison.Ordinal) ||
            !TryGetRequiredString(attemptIntent, "condition_id", out var attemptConditionId) ||
            !string.Equals(attemptConditionId, order.ConditionId, StringComparison.Ordinal) ||
            !TryGetRequiredDecimal(attemptIntent, "limit_price", out var attemptIntentPrice) ||
            attemptIntentPrice != order.Price ||
            !TryGetObject(acceptedAttempt, "s1", out var s1) ||
            !TryGetRequiredString(s1, "asset_id", out var s1AssetId) ||
            !string.Equals(s1AssetId, order.AssetId, StringComparison.Ordinal) ||
            !TryGetRequiredString(s1, "condition_id", out var s1ConditionId) ||
            !string.Equals(s1ConditionId, order.ConditionId, StringComparison.Ordinal) ||
            !TryGetRequiredBoolean(s1, "is_current", out var s1IsCurrent) ||
            !s1IsCurrent ||
            !TryGetRequiredBoolean(
                s1,
                "timestamp_is_authoritative",
                out var s1TimestampIsAuthoritative) ||
            !s1TimestampIsAuthoritative ||
            !TryGetRequiredPositiveDecimal(s1, "best_ask", out var s1BestAsk) ||
            order.Price >= s1BestAsk)
        {
            return false;
        }

        var rawExpectedLimit = Math.Min(s0BestAsk - s0TickSize, maximumOrderPrice);
        if (rawExpectedLimit <= 0m)
        {
            return false;
        }

        var expectedLimit = decimal.Floor(rawExpectedLimit / s0TickSize) * s0TickSize;
        if (attemptLimitPrice != expectedLimit)
        {
            return false;
        }

        if (string.Equals(
                contractVersion,
                PairedMakerGtdPaperExecutionContract.LegacyContractVersion,
                StringComparison.Ordinal))
        {
            return !s0.TryGetProperty("freshness_basis", out _) &&
                !s1.TryGetProperty("freshness_basis", out _);
        }

        if (!TryGetRequiredPositiveDecimal(s0, "best_bid", out var s0BestBid) ||
            s0BestBid >= 1m ||
            s0BestAsk >= 1m ||
            s0BestBid >= s0BestAsk ||
            !TryGetRequiredPositiveDecimal(s0, "min_order_size", out var s0MinOrderSize) ||
            s0MinOrderSize != intentMinOrderSize ||
            !TryGetRequiredBoolean(s0, "negative_risk", out var s0NegativeRisk) ||
            s0NegativeRisk != intentNegativeRisk ||
            !TryGetRequiredPositiveDecimal(s1, "best_bid", out var s1BestBid) ||
            s1BestBid >= 1m ||
            s1BestAsk >= 1m ||
            s1BestBid >= s1BestAsk ||
            !TryGetRequiredPositiveDecimal(s1, "tick_size", out var s1TickSize) ||
            s1TickSize != frozenTickSize ||
            !TryGetRequiredPositiveDecimal(s1, "min_order_size", out var s1MinOrderSize) ||
            s1MinOrderSize != intentMinOrderSize ||
            !TryGetRequiredBoolean(s1, "negative_risk", out var s1NegativeRisk) ||
            s1NegativeRisk != intentNegativeRisk ||
            !TryGetRequiredPositiveDecimal(
                attemptIntent,
                "min_order_size",
                out var attemptIntentMinOrderSize) ||
            attemptIntentMinOrderSize != intentMinOrderSize ||
            !TryGetRequiredBoolean(
                attemptIntent,
                "negative_risk",
                out var attemptIntentNegativeRisk) ||
            attemptIntentNegativeRisk != intentNegativeRisk)
        {
            return false;
        }

        if (!TryValidatePairedDirectHttpFreshness(s0, out var s0Freshness) ||
            !TryValidatePairedDirectHttpFreshness(s1, out var s1Freshness))
        {
            return false;
        }

        return s0Freshness.EvaluatedAtUtc <= frozenAtUtc &&
            frozenAtUtc - s0Freshness.ReceivedAtUtc <= s0Freshness.MaximumAge &&
            s1Freshness.RequestStartedAtUtc >= frozenAtUtc &&
            s1Freshness.EvaluatedAtUtc <= acceptedAtUtc &&
            acceptedAtUtc - s1Freshness.ReceivedAtUtc <= s1Freshness.MaximumAge &&
            SameTimestamp(s1Freshness.ReceivedAtUtc, s1ReceivedAtUtc);
    }

    private static bool TryValidatePairedDirectHttpFreshness(
        JsonElement book,
        out PairedDirectHttpFreshnessEvidence evidence)
    {
        evidence = default;
        if (!TryGetRequiredString(book, "freshness_basis", out var freshnessBasis) ||
            !string.Equals(
                freshnessBasis,
                PairedMakerGtdPaperExecutionContract.DirectHttpReceiptFreshnessBasis,
                StringComparison.Ordinal) ||
            !TryGetRequiredTimestamp(book, "request_started_at_utc", out var requestStartedAtUtc) ||
            !TryGetRequiredTimestamp(book, "received_at_utc", out var receivedAtUtc) ||
            !TryGetRequiredTimestamp(book, "response_completed_at_utc", out var responseCompletedAtUtc) ||
            !TryGetRequiredTimestamp(book, "evaluated_at_utc", out var evaluatedAtUtc) ||
            !TryGetRequiredTimestamp(book, "source_timestamp_utc", out var sourceTimestampUtc) ||
            !TryGetRequiredNonNegativeInt64(book, "max_age_ms", out var maxAgeMs) ||
            maxAgeMs <= 0 ||
            maxAgeMs > PairedMakerGtdPaperExecutionContract.MaximumDirectHttpQuoteAgeMilliseconds ||
            !TryGetRequiredNonNegativeInt64(book, "age_ms", out var ageMs) ||
            !TryGetRequiredNonNegativeInt64(book, "receipt_age_ms", out var receiptAgeMs) ||
            !TryGetRequiredNonNegativeInt64(book, "request_duration_ms", out var requestDurationMs) ||
            !TryGetRequiredNonNegativeInt64(book, "source_age_ms", out var sourceAgeMs) ||
            requestStartedAtUtc > receivedAtUtc ||
            receivedAtUtc > responseCompletedAtUtc ||
            responseCompletedAtUtc > evaluatedAtUtc ||
            sourceTimestampUtc > receivedAtUtc)
        {
            return false;
        }

        var expectedReceiptAgeMs = CeilingMilliseconds(evaluatedAtUtc - receivedAtUtc);
        var expectedRequestDurationMs = CeilingMilliseconds(responseCompletedAtUtc - requestStartedAtUtc);
        var expectedSourceAgeMs = CeilingMilliseconds(evaluatedAtUtc - sourceTimestampUtc);
        var valid = ageMs == receiptAgeMs &&
            receiptAgeMs == expectedReceiptAgeMs &&
            requestDurationMs == expectedRequestDurationMs &&
            sourceAgeMs == expectedSourceAgeMs &&
            receiptAgeMs <= maxAgeMs &&
            requestDurationMs <= maxAgeMs;
        if (!valid)
        {
            return false;
        }

        evidence = new PairedDirectHttpFreshnessEvidence(
            requestStartedAtUtc,
            receivedAtUtc,
            evaluatedAtUtc,
            TimeSpan.FromMilliseconds(maxAgeMs));
        return true;
    }

    private static long CeilingMilliseconds(TimeSpan value)
    {
        return checked((long)Math.Ceiling(value.TotalMilliseconds));
    }

    private readonly record struct PairedDirectHttpFreshnessEvidence(
        DateTimeOffset RequestStartedAtUtc,
        DateTimeOffset ReceivedAtUtc,
        DateTimeOffset EvaluatedAtUtc,
        TimeSpan MaximumAge);

    private static bool TryGetObject(
        JsonElement root,
        string propertyName,
        out JsonElement value)
    {
        return root.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetRequiredTimestamp(
        JsonElement root,
        string propertyName,
        out DateTimeOffset value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.String &&
            element.TryGetDateTimeOffset(out value);
    }

    private static bool TryGetOptionalTimestamp(
        JsonElement root,
        string propertyName,
        out DateTimeOffset? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String ||
            !element.TryGetDateTimeOffset(out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetRequiredBoolean(
        JsonElement root,
        string propertyName,
        out bool value)
    {
        value = false;
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    private static bool TryGetOptionalBoolean(
        JsonElement root,
        string propertyName,
        out bool? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return true;
        }

        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    private static bool TryGetOptionalString(
        JsonElement root,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetRequiredString(
        JsonElement root,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetRequiredGuid(
        JsonElement root,
        string propertyName,
        out Guid value)
    {
        value = default;
        return TryGetRequiredString(root, propertyName, out var text) &&
            Guid.TryParseExact(text, "D", out value);
    }

    private static bool TryGetRequiredDecimal(
        JsonElement root,
        string propertyName,
        out decimal value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetDecimal(out value);
    }

    private static bool TryGetRequiredPositiveDecimal(
        JsonElement root,
        string propertyName,
        out decimal value)
    {
        return TryGetRequiredDecimal(root, propertyName, out value) && value > 0m;
    }

    private static bool TryGetRequiredNonNegativeInt64(
        JsonElement root,
        string propertyName,
        out long value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt64(out value) &&
            value >= 0;
    }

    private static bool TryGetRequiredStringArray(
        JsonElement root,
        string propertyName,
        out IReadOnlyList<string> values)
    {
        values = [];
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(item.GetString()))
            {
                return false;
            }

            parsed.Add(item.GetString()!);
        }

        values = parsed;
        return true;
    }

    private static bool TryGetExactPairStrategyIds(
        JsonElement pair,
        Guid strategyId,
        Guid pairedStrategyId)
    {
        if (!TryGetRequiredStringArray(pair, "pair_strategy_ids", out var rawIds) ||
            rawIds.Count != 2)
        {
            return false;
        }

        var parsedIds = new HashSet<Guid>();
        foreach (var rawId in rawIds)
        {
            if (!Guid.TryParseExact(rawId, "D", out var parsedId) ||
                !parsedIds.Add(parsedId))
            {
                return false;
            }
        }

        return parsedIds.SetEquals([strategyId, pairedStrategyId]);
    }

    private static bool TryGetRequiredNonNegativeInt32(
        JsonElement root,
        string propertyName,
        out int value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value) &&
            value >= 0;
    }

    private static bool TryGetOptionalNonNegativeInt64(
        JsonElement root,
        string propertyName,
        out long? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt64(out var parsed) ||
            parsed < 0)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetRequiredEnum<TEnum>(
        JsonElement root,
        string propertyName,
        out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        return root.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.String &&
            Enum.TryParse(element.GetString(), ignoreCase: false, out value) &&
            Enum.IsDefined(value);
    }

    private static bool SameTimestamp(DateTimeOffset left, DateTimeOffset right)
    {
        return left.UtcDateTime.Ticks / 10 == right.UtcDateTime.Ticks / 10;
    }
}

internal sealed record MakerGtdPaperObservationSegmentEvent(
    ConfirmedAssetSubscriptionSnapshot IngressSubscription,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset SourceTimestampUtc,
    string EventFingerprint);

internal static class MakerGtdPaperContinuityEvaluator
{
    public static MakerGtdPaperContinuityEvaluation Evaluate(
        PaperOrder order,
        MarketDataStatusSnapshot currentStatus,
        IReadOnlyCollection<string> currentSubscribedAssetIds,
        ConfirmedAssetSubscriptionSnapshot? currentConfirmedAssetSubscription = null,
        MakerGtdPaperObservationSegmentEvent? observationSegmentEvent = null)
    {
        if (!MakerGtdPaperOrderEvidenceParser.TryParse(
                order,
                out var orderEvidence,
                out var failureDetail) ||
            orderEvidence is null)
        {
            return Unavailable(failureDetail);
        }

        var acceptedStatus = orderEvidence.AcceptedMarketDataStatus;
        if (string.Equals(
                order.ExecutionSource,
                MakerGtdPaperExecutionSources.PairedFirstAccepting,
                StringComparison.Ordinal))
        {
            if (!acceptedStatus.AssetSubscribed ||
                acceptedStatus.SubscribedAssetsCount != 2 ||
                acceptedStatus.AssetConfirmedLive != true ||
                string.IsNullOrWhiteSpace(acceptedStatus.AssetSubscriptionComponent))
            {
                return Unavailable("asset_not_confirmed_live_at_acceptance");
            }

            if (currentConfirmedAssetSubscription is not { ConfirmedLive: true } currentConfirmed ||
                !string.Equals(currentConfirmed.AssetId, order.AssetId, StringComparison.Ordinal))
            {
                return Unavailable("asset_not_currently_confirmed_live");
            }

            if (acceptedStatus.AssetSubscriptionGeneration is not { } acceptedGeneration ||
                string.IsNullOrWhiteSpace(acceptedStatus.AssetSubscriptionSessionId) ||
                string.IsNullOrWhiteSpace(currentConfirmed.Component) ||
                currentConfirmed.Generation < 0 ||
                string.IsNullOrWhiteSpace(currentConfirmed.SessionId) ||
                currentConfirmed.ConfirmedAtUtc is not { } currentSegmentStartedAtUtc ||
                currentConfirmed.ConfirmationSourceTimestampUtc is not { } currentSegmentSourceTimestampUtc ||
                string.IsNullOrWhiteSpace(currentConfirmed.ConfirmationEventFingerprint))
            {
                return Unavailable("current_observation_segment_evidence_missing");
            }

            if (currentSegmentSourceTimestampUtc > currentSegmentStartedAtUtc ||
                currentSegmentSourceTimestampUtc >= order.ExpiresAtUtc)
            {
                return Unavailable("current_observation_segment_timestamp_invalid");
            }

            var sameAcceptedSegment =
                string.Equals(
                    currentConfirmed.Component,
                    acceptedStatus.AssetSubscriptionComponent,
                    StringComparison.Ordinal) &&
                currentConfirmed.Generation == acceptedGeneration &&
                string.Equals(
                    currentConfirmed.SessionId,
                    acceptedStatus.AssetSubscriptionSessionId,
                    StringComparison.Ordinal);
            if (observationSegmentEvent is null)
            {
                if (currentSegmentStartedAtUtc >= order.ExpiresAtUtc)
                {
                    return Unavailable("observation_segment_confirmed_after_expiry");
                }

                return new MakerGtdPaperContinuityEvaluation(
                    Continuous: true,
                    MakerGtdPaperExecutionContract.ExpiredUnfilledReasonCode,
                    sameAcceptedSegment
                        ? "continuous_confirmed_asset_subscription_evidence"
                        : "recovered_confirmed_asset_observation_segment_at_expiry");
            }

            var ingress = observationSegmentEvent.IngressSubscription;
            if (!ingress.ConfirmedLive ||
                !string.Equals(ingress.AssetId, order.AssetId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(ingress.Component) ||
                string.IsNullOrWhiteSpace(ingress.SessionId) ||
                ingress.ConfirmedAtUtc is not { } ingressSegmentStartedAtUtc ||
                ingress.ConfirmationSourceTimestampUtc is not { } confirmationSourceTimestampUtc ||
                string.IsNullOrWhiteSpace(ingress.ConfirmationEventFingerprint) ||
                !string.Equals(ingress.Component, currentConfirmed.Component, StringComparison.Ordinal) ||
                ingress.Generation != currentConfirmed.Generation ||
                !string.Equals(ingress.SessionId, currentConfirmed.SessionId, StringComparison.Ordinal) ||
                ingress.ConfirmedAtUtc != currentConfirmed.ConfirmedAtUtc ||
                ingress.ConfirmationSourceTimestampUtc != currentConfirmed.ConfirmationSourceTimestampUtc ||
                !string.Equals(
                    ingress.ConfirmationEventFingerprint,
                    currentConfirmed.ConfirmationEventFingerprint,
                    StringComparison.Ordinal))
            {
                return Unavailable("market_data_event_observation_segment_changed");
            }

            if (observationSegmentEvent.ReceivedAtUtc <= ingressSegmentStartedAtUtc ||
                observationSegmentEvent.SourceTimestampUtc <= ingressSegmentStartedAtUtc ||
                observationSegmentEvent.SourceTimestampUtc <= confirmationSourceTimestampUtc ||
                string.Equals(
                    observationSegmentEvent.EventFingerprint,
                    ingress.ConfirmationEventFingerprint,
                    StringComparison.Ordinal))
            {
                return Unavailable("market_data_event_not_after_recovery_fence");
            }

            return new MakerGtdPaperContinuityEvaluation(
                Continuous: true,
                MakerGtdPaperExecutionContract.ExpiredUnfilledReasonCode,
                sameAcceptedSegment
                    ? "continuous_confirmed_asset_subscription_evidence"
                    : "recovered_confirmed_asset_observation_segment");
        }

        if (acceptedStatus.ConnectionState != MarketDataConnectionState.Connected ||
            acceptedStatus.Stale)
        {
            return Unavailable("acceptance_connection_not_healthy");
        }

        if (!acceptedStatus.AssetSubscribed || acceptedStatus.SubscribedAssetsCount <= 0)
        {
            return Unavailable("asset_not_subscribed_at_acceptance");
        }

        if (!currentSubscribedAssetIds.Contains(order.AssetId, StringComparer.Ordinal))
        {
            return Unavailable("asset_not_currently_subscribed");
        }

        if (currentStatus.ConnectionState != MarketDataConnectionState.Connected ||
            currentStatus.Stale)
        {
            return Unavailable("current_connection_not_healthy");
        }

        if (currentStatus.ReconnectCount < 0 ||
            currentStatus.ReconnectCount != acceptedStatus.ReconnectCount)
        {
            return Unavailable("reconnect_count_changed");
        }

        if (acceptedStatus.LastConnectedUtc is not { } acceptedLastConnectedUtc ||
            acceptedLastConnectedUtc > order.CreatedAtUtc ||
            acceptedStatus.LastDisconnectedUtc is { } acceptedLastDisconnectedUtc &&
            acceptedLastDisconnectedUtc > order.CreatedAtUtc)
        {
            return Unavailable("acceptance_connection_timeline_invalid");
        }

        if (currentStatus.LastConnectedUtc is not { } currentLastConnectedUtc ||
            currentLastConnectedUtc > order.CreatedAtUtc ||
            currentStatus.LastDisconnectedUtc is { } currentLastDisconnectedUtc &&
            currentLastDisconnectedUtc > order.CreatedAtUtc)
        {
            return Unavailable("current_connection_timeline_changed");
        }

        return new MakerGtdPaperContinuityEvaluation(
            Continuous: true,
            MakerGtdPaperExecutionContract.ExpiredUnfilledReasonCode,
            "continuous_market_websocket_evidence");
    }

    private static MakerGtdPaperContinuityEvaluation Unavailable(string detail)
    {
        return new MakerGtdPaperContinuityEvaluation(
            Continuous: false,
            MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode,
            string.IsNullOrWhiteSpace(detail) ? "continuity_evidence_missing" : detail);
    }
}
