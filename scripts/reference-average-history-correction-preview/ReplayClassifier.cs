using System.Text.Json;

namespace ReferenceAverageHistoryCorrectionPreview;

public static class ReplayClassifier
{
    public const string PotentialAddSkipReason = "optimized_average_required_window_not_selected";
    public const string OptimizedRequiredWindow = "3h";
    public const string LegacyDecisionSource = "reference_price_max_average_bps_premarket";
    public const string OptimizedHistoryBackfillMarker = "btc-sol-up-neutral-optimized-history-20260725-v1";
    public const string SolDownOptimizedHistoryBackfillId = "sol-down-optimized-history-20260725-selected3h-lower050-v1";
    public const decimal RegularAssumedFillPrice = 0.52m;
    public const decimal LowerEnterAssumedFillPrice = 0.50m;

    public static ReplayDecision ClassifyExistingEntry(
        StrategyDefinition strategy,
        string? runOutcome,
        string? orderOutcome,
        string? rawDecisionJson)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        if (string.IsNullOrWhiteSpace(runOutcome) || string.IsNullOrWhiteSpace(orderOutcome))
        {
            return ReplayDecision.Unreplayable("existing_outcome_missing");
        }

        if (!string.Equals(runOutcome, orderOutcome, StringComparison.OrdinalIgnoreCase))
        {
            return ReplayDecision.Unreplayable("run_order_outcome_mismatch");
        }

        if (!TryParseSignalOutcome(runOutcome, out var actualOutcome))
        {
            return ReplayDecision.Unreplayable("existing_outcome_not_up_or_down");
        }

        if (!TryExtractReferenceEvidence(
                strategy,
                rawDecisionJson,
                ReferenceEvidenceOptions.ExistingEntry,
                out var evidence,
                out var error))
        {
            return ReplayDecision.Unreplayable(error);
        }

        if (!HasExpectedLegacyIdentity(strategy, evidence, out var identityError))
        {
            return BuildDecision(ReplayAction.InvariantError, identityError, evidence);
        }

        var legacyV1Outcome = EvaluateLegacyV1(strategy, evidence, applyOptimizedWindowGate: true);
        if (legacyV1Outcome != actualOutcome)
        {
            return BuildDecision(
                ReplayAction.InvariantError,
                "legacy_v1_outcome_does_not_match_actual",
                evidence,
                legacyV1Outcome: legacyV1Outcome);
        }

        var correctedV2Outcome = EvaluateCorrectedV2(strategy, evidence);
        if (correctedV2Outcome == actualOutcome)
        {
            return BuildDecision(
                ReplayAction.Retain,
                actualOutcome == SignalOutcome.Down
                    ? "existing_down_unchanged_by_v2"
                    : "v2_outcome_matches_actual_up",
                evidence,
                legacyV1Outcome: legacyV1Outcome,
                correctedV2Outcome: correctedV2Outcome);
        }

        if (correctedV2Outcome == SignalOutcome.Skip && actualOutcome == SignalOutcome.Up)
        {
            return BuildDecision(
                ReplayAction.Remove,
                "v2_reference_signal_would_skip_actual_up",
                evidence,
                legacyV1Outcome: legacyV1Outcome,
                correctedV2Outcome: correctedV2Outcome);
        }

        return BuildDecision(
            ReplayAction.InvariantError,
            correctedV2Outcome == SignalOutcome.Skip
                ? "v2_unexpected_skip_for_actual_down"
                : "v2_opposite_outcome_from_actual",
            evidence,
            legacyV1Outcome: legacyV1Outcome,
            correctedV2Outcome: correctedV2Outcome);
    }

    public static ReplayDecision ClassifyPotentialAdd(
        StrategyDefinition strategy,
        string? skipReason,
        string? skipDiagnosticsJson)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        if (strategy.Family != StrategyFamily.OptimizedReferenceAverage ||
            strategy.Trigger is not (StrategyTrigger.Down or StrategyTrigger.Neutral))
        {
            return ReplayDecision.Unreplayable("strategy_outside_exact_optimized_down_neutral_cohort");
        }

        if (!string.Equals(skipReason, PotentialAddSkipReason, StringComparison.Ordinal))
        {
            return ReplayDecision.Unreplayable("legacy_skip_reason_mismatch");
        }

        if (!TryExtractReferenceEvidence(
                strategy,
                skipDiagnosticsJson,
                ReferenceEvidenceOptions.TargetDiagnostics,
                out var evidence,
                out var error))
        {
            return ReplayDecision.Unreplayable(error);
        }

        return ClassifyPotentialAddEvidence(strategy, skipReason, evidence, expectedOrdinaryOutcome: null);
    }

    public static ReplayDecision ClassifyPotentialAddFromOrdinaryReferenceEvidence(
        StrategyDefinition strategy,
        string? skipReason,
        string? ordinaryOutcome,
        string? ordinaryRawDecisionJson)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        if (!TryExtractReferenceEvidence(
                strategy,
                ordinaryRawDecisionJson,
                ReferenceEvidenceOptions.OrdinaryReferenceFallback,
                out var evidence,
                out var error))
        {
            return ReplayDecision.Unreplayable(error);
        }

        if (string.IsNullOrWhiteSpace(ordinaryOutcome) ||
            !TryParseSignalOutcome(ordinaryOutcome, out var expectedOrdinaryOutcome))
        {
            return ReplayDecision.Unreplayable("ordinary_reference_evidence_outcome_missing_or_invalid");
        }

        return ClassifyPotentialAddEvidence(strategy, skipReason, evidence, expectedOrdinaryOutcome);
    }

    private static ReplayDecision ClassifyPotentialAddEvidence(
        StrategyDefinition strategy,
        string? skipReason,
        ReferenceEvidence evidence,
        SignalOutcome? expectedOrdinaryOutcome)
    {
        if (strategy.Family != StrategyFamily.OptimizedReferenceAverage ||
            strategy.Trigger is not (StrategyTrigger.Down or StrategyTrigger.Neutral))
        {
            return ReplayDecision.Unreplayable("strategy_outside_exact_optimized_down_neutral_cohort");
        }

        if (!string.Equals(skipReason, PotentialAddSkipReason, StringComparison.Ordinal))
        {
            return ReplayDecision.Unreplayable("legacy_skip_reason_mismatch");
        }

        if (!HasExpectedLegacyIdentity(strategy, evidence, out var identityError))
        {
            return BuildDecision(ReplayAction.InvariantError, identityError, evidence);
        }

        if (!string.Equals(evidence.RequiredWindow, OptimizedRequiredWindow, StringComparison.OrdinalIgnoreCase))
        {
            return BuildDecision(
                ReplayAction.InvariantError,
                "optimized_required_window_is_not_3h",
                evidence);
        }

        var legacyUnderlyingOutcome = EvaluateLegacyV1(
            strategy,
            evidence,
            applyOptimizedWindowGate: false);
        if (expectedOrdinaryOutcome is { } expectedOutcome && legacyUnderlyingOutcome != expectedOutcome)
        {
            return BuildDecision(
                ReplayAction.InvariantError,
                "ordinary_reference_evidence_outcome_does_not_match_recomputed_v1",
                evidence,
                legacyV1Outcome: legacyUnderlyingOutcome);
        }

        var legacyV1Outcome = EvaluateLegacyV1(strategy, evidence, applyOptimizedWindowGate: true);
        if (legacyV1Outcome != SignalOutcome.Skip ||
            string.Equals(evidence.Maximum.Window, evidence.RequiredWindow, StringComparison.OrdinalIgnoreCase))
        {
            return BuildDecision(
                ReplayAction.InvariantError,
                "legacy_skip_reason_not_confirmed_by_recomputed_v1",
                evidence,
                legacyV1Outcome: legacyV1Outcome);
        }

        var correctedV2Outcome = EvaluateCorrectedV2(strategy, evidence);
        if (legacyUnderlyingOutcome == SignalOutcome.Down &&
            strategy.Trigger == StrategyTrigger.Neutral)
        {
            return correctedV2Outcome == SignalOutcome.Skip
                ? BuildDecision(
                    ReplayAction.StillSkip,
                    "legacy_upper_branch_window_gate_unchanged",
                    evidence,
                    legacyV1Outcome: legacyV1Outcome,
                    correctedV2Outcome: correctedV2Outcome)
                : BuildDecision(
                    ReplayAction.InvariantError,
                    "legacy_upper_branch_unexpected_v2_entry",
                    evidence,
                    legacyV1Outcome: legacyV1Outcome,
                    correctedV2Outcome: correctedV2Outcome);
        }

        if (legacyUnderlyingOutcome != SignalOutcome.Up)
        {
            return BuildDecision(
                ReplayAction.InvariantError,
                "legacy_skip_reason_not_confirmed_by_recomputed_v1",
                evidence,
                legacyV1Outcome: legacyV1Outcome,
                correctedV2Outcome: correctedV2Outcome);
        }

        if (correctedV2Outcome == SignalOutcome.Down)
        {
            return BuildDecision(
                ReplayAction.InvariantError,
                "v2_opposite_outcome_for_potential_add",
                evidence,
                legacyV1Outcome: legacyV1Outcome,
                correctedV2Outcome: correctedV2Outcome);
        }

        if (correctedV2Outcome == SignalOutcome.Skip)
        {
            var thresholdMet = evidence.MoveBelowMinimumBps <= -evidence.ThresholdBps;
            return BuildDecision(
                ReplayAction.StillSkip,
                thresholdMet
                    ? "v2_optimized_required_window_not_selected"
                    : "v2_minimum_threshold_not_met",
                evidence,
                legacyV1Outcome: legacyV1Outcome,
                correctedV2Outcome: correctedV2Outcome);
        }

        if (evidence.HistoricalStakeMultiplier is not > 0m)
        {
            return BuildDecision(
                ReplayAction.Unreplayable,
                "historical_stake_multiplier_missing_or_invalid_for_add",
                evidence,
                legacyV1Outcome: legacyV1Outcome,
                correctedV2Outcome: correctedV2Outcome);
        }

        return BuildDecision(
            ReplayAction.Add,
            "v2_minimum_threshold_and_required_window_met",
            evidence,
            strategy.UsesLowEnterPrice ? LowerEnterAssumedFillPrice : RegularAssumedFillPrice,
            legacyV1Outcome,
            correctedV2Outcome);
    }

    private static ReplayDecision BuildDecision(
        ReplayAction action,
        string reason,
        ReferenceEvidence evidence,
        decimal? assumedFillPrice = null,
        SignalOutcome? legacyV1Outcome = null,
        SignalOutcome? correctedV2Outcome = null)
    {
        return new ReplayDecision(
            action,
            reason,
            evidence.CurrentPriceUsd,
            evidence.Minimum.PriceUsd,
            evidence.Minimum.Window,
            evidence.Minimum.WindowSeconds,
            evidence.Maximum.PriceUsd,
            evidence.Maximum.Window,
            evidence.Maximum.WindowSeconds,
            evidence.ThresholdBps,
            evidence.MoveBelowMinimumBps,
            evidence.MoveAboveMaximumBps,
            evidence.RequiredWindow,
            evidence.HistoricalStakeMultiplier,
            assumedFillPrice,
            legacyV1Outcome,
            correctedV2Outcome);
    }

    private static bool TryExtractReferenceEvidence(
        StrategyDefinition strategy,
        string? rawJson,
        ReferenceEvidenceOptions options,
        out ReferenceEvidence evidence,
        out string error)
    {
        evidence = default!;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            error = "reference_decision_json_missing";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var referenceNode = document.RootElement;
            var nestedProperty = strategy.Family switch
            {
                StrategyFamily.BpsConfirmedAverage => "base_signal_decision",
                StrategyFamily.DiffConfirmedAverage => "confirmation_signal_decision",
                _ => null
            };
            if (nestedProperty is not null &&
                (!referenceNode.TryGetProperty(nestedProperty, out referenceNode) ||
                 referenceNode.ValueKind != JsonValueKind.Object))
            {
                error = $"{nestedProperty}_missing_or_not_object";
                return false;
            }

            if (!TryGetRequiredString(referenceNode, "decision_source", out var decisionSource))
            {
                error = "decision_source_missing_or_invalid";
                return false;
            }

            if (!TryGetRequiredString(referenceNode, "reference_asset_symbol", out var referenceAssetSymbol))
            {
                error = "reference_asset_symbol_missing_or_invalid";
                return false;
            }

            if (!TryGetRequiredString(
                    referenceNode,
                    "selected_reference_average_window",
                    out var legacySelectedWindow))
            {
                error = "legacy_selected_reference_average_window_missing_or_invalid";
                return false;
            }

            if (!TryGetPositiveDecimal(referenceNode, "current_price_usd", out var currentPriceUsd))
            {
                error = "current_price_usd_missing_or_invalid";
                return false;
            }

            if (!TryGetPositiveDecimal(
                    referenceNode,
                    "reference_average_min_move_from_middle_bps",
                    out var thresholdBps))
            {
                error = "reference_average_threshold_missing_or_invalid";
                return false;
            }

            if (!referenceNode.TryGetProperty("reference_averages", out var averagesNode) ||
                averagesNode.ValueKind != JsonValueKind.Array)
            {
                error = "reference_averages_missing_or_not_array";
                return false;
            }

            var validAverages = new List<ReferenceAverageEvidence>();
            foreach (var averageNode in averagesNode.EnumerateArray())
            {
                if (averageNode.ValueKind != JsonValueKind.Object ||
                    !averageNode.TryGetProperty("is_full_window", out var fullNode) ||
                    fullNode.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    error = "reference_average_item_full_window_missing_or_invalid";
                    return false;
                }

                if (!fullNode.GetBoolean())
                {
                    continue;
                }

                if (!averageNode.TryGetProperty("average_price_usd", out var priceNode) ||
                    priceNode.ValueKind != JsonValueKind.Number ||
                    !priceNode.TryGetDecimal(out var priceUsd))
                {
                    error = "full_reference_average_price_missing_or_invalid";
                    return false;
                }

                if (priceUsd <= 0m)
                {
                    continue;
                }

                if (!averageNode.TryGetProperty("window", out var windowNode) ||
                    windowNode.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(windowNode.GetString()) ||
                    !averageNode.TryGetProperty("window_seconds", out var secondsNode) ||
                    secondsNode.ValueKind != JsonValueKind.Number ||
                    !secondsNode.TryGetInt32(out var windowSeconds) ||
                    windowSeconds <= 0)
                {
                    error = "full_reference_average_identity_missing_or_invalid";
                    return false;
                }

                validAverages.Add(new ReferenceAverageEvidence(
                    windowNode.GetString()!,
                    windowSeconds,
                    priceUsd));
            }

            if (validAverages.Count == 0)
            {
                error = "full_positive_reference_average_missing";
                return false;
            }

            var minimum = validAverages
                .OrderBy(item => item.PriceUsd)
                .ThenByDescending(item => item.WindowSeconds)
                .ThenBy(item => item.Window, StringComparer.OrdinalIgnoreCase)
                .First();
            var maximum = validAverages
                .OrderByDescending(item => item.PriceUsd)
                .ThenByDescending(item => item.WindowSeconds)
                .ThenBy(item => item.Window, StringComparer.OrdinalIgnoreCase)
                .First();
            string? requiredWindow = null;
            var evidenceProvenance = options.Provenance;
            if (IsOptimized(strategy))
            {
                if (referenceNode.TryGetProperty("optimized_average_required_window", out var requiredNode) &&
                    requiredNode.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(requiredNode.GetString()))
                {
                    requiredWindow = requiredNode.GetString();
                }
                else if (!string.IsNullOrWhiteSpace(options.RequiredWindowOverride))
                {
                    requiredWindow = options.RequiredWindowOverride;
                }
                else if (options.AllowHistoricalRequiredWindowEvidence &&
                         TryResolveHistoricalOptimizedRequiredWindow(
                             strategy,
                             referenceNode,
                             legacySelectedWindow,
                             out var historicalRequiredWindow,
                             out var historicalProvenance))
                {
                    requiredWindow = historicalRequiredWindow;
                    evidenceProvenance = historicalProvenance;
                }
                else
                {
                    error = "optimized_required_window_missing_or_invalid";
                    return false;
                }
            }

            decimal? historicalStakeMultiplier = null;
            if (options.AllowHistoricalStakeMultiplierFromJson &&
                referenceNode.TryGetProperty("target_notional_usd", out var multiplierNode) &&
                multiplierNode.ValueKind == JsonValueKind.Number &&
                multiplierNode.TryGetDecimal(out var parsedMultiplier) &&
                parsedMultiplier > 0m)
            {
                historicalStakeMultiplier = parsedMultiplier;
            }

            var moveBelowMinimumBps = (currentPriceUsd - minimum.PriceUsd) / minimum.PriceUsd * 10_000m;
            var moveAboveMaximumBps = (currentPriceUsd - maximum.PriceUsd) / maximum.PriceUsd * 10_000m;
            evidence = new ReferenceEvidence(
                currentPriceUsd,
                thresholdBps,
                minimum,
                maximum,
                moveBelowMinimumBps,
                moveAboveMaximumBps,
                requiredWindow,
                decisionSource,
                referenceAssetSymbol,
                legacySelectedWindow,
                historicalStakeMultiplier,
                evidenceProvenance);
            error = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            error = "reference_decision_json_invalid";
            return false;
        }
    }

    private static bool TryResolveHistoricalOptimizedRequiredWindow(
        StrategyDefinition strategy,
        JsonElement referenceNode,
        string legacySelectedWindow,
        out string requiredWindow,
        out string provenance)
    {
        requiredWindow = string.Empty;
        provenance = string.Empty;
        if (!string.Equals(legacySelectedWindow, OptimizedRequiredWindow, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (referenceNode.TryGetProperty("codex_backfill", out var codexBackfillNode) &&
            codexBackfillNode.ValueKind == JsonValueKind.String &&
            string.Equals(
                codexBackfillNode.GetString(),
                OptimizedHistoryBackfillMarker,
                StringComparison.Ordinal))
        {
            requiredWindow = OptimizedRequiredWindow;
            provenance = "optimized_history_backfill_marker_3h";
            return true;
        }

        if (string.Equals(strategy.Asset, "SOL", StringComparison.OrdinalIgnoreCase) &&
            strategy.Trigger == StrategyTrigger.Down &&
            referenceNode.TryGetProperty("history_backfill", out var historyBackfillNode) &&
            historyBackfillNode.ValueKind == JsonValueKind.Object &&
            historyBackfillNode.TryGetProperty("backfill_id", out var backfillIdNode) &&
            backfillIdNode.ValueKind == JsonValueKind.String &&
            string.Equals(
                backfillIdNode.GetString(),
                SolDownOptimizedHistoryBackfillId,
                StringComparison.Ordinal))
        {
            requiredWindow = OptimizedRequiredWindow;
            provenance = "sol_down_optimized_history_backfill_3h";
            return true;
        }

        return false;
    }

    private static bool TryGetPositiveDecimal(JsonElement node, string propertyName, out decimal value)
    {
        value = default;
        return node.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDecimal(out value) &&
            value > 0m;
    }

    private static bool TryGetRequiredString(JsonElement node, string propertyName, out string value)
    {
        value = string.Empty;
        if (!node.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }

    private static bool HasExpectedLegacyIdentity(
        StrategyDefinition strategy,
        ReferenceEvidence evidence,
        out string error)
    {
        if (!string.Equals(evidence.DecisionSource, LegacyDecisionSource, StringComparison.Ordinal))
        {
            error = "decision_source_is_not_legacy_v1";
            return false;
        }

        if (!string.Equals(evidence.ReferenceAssetSymbol, strategy.Asset, StringComparison.OrdinalIgnoreCase))
        {
            error = "reference_asset_symbol_mismatch";
            return false;
        }

        if (strategy.Family != StrategyFamily.DiffConfirmedAverage &&
            evidence.ThresholdBps != strategy.ReferenceThresholdBps)
        {
            error = "reference_average_threshold_mismatch";
            return false;
        }

        if (!string.Equals(
                evidence.LegacySelectedWindow,
                evidence.Maximum.Window,
                StringComparison.OrdinalIgnoreCase))
        {
            error = "legacy_selected_window_does_not_match_recomputed_maximum";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static SignalOutcome EvaluateLegacyV1(
        StrategyDefinition strategy,
        ReferenceEvidence evidence,
        bool applyOptimizedWindowGate)
    {
        var move = evidence.MoveAboveMaximumBps;
        SignalOutcome outcome;
        if (strategy.Trigger == StrategyTrigger.Up)
        {
            outcome = move >= evidence.ThresholdBps ? SignalOutcome.Down : SignalOutcome.Skip;
        }
        else if (strategy.Trigger == StrategyTrigger.Down)
        {
            outcome = move <= -evidence.ThresholdBps ? SignalOutcome.Up : SignalOutcome.Skip;
        }
        else
        {
            outcome = move switch
            {
                var value when value >= evidence.ThresholdBps => SignalOutcome.Down,
                var value when value <= -evidence.ThresholdBps => SignalOutcome.Up,
                _ => SignalOutcome.Skip
            };
        }

        if (outcome != SignalOutcome.Skip &&
            applyOptimizedWindowGate &&
            IsOptimized(strategy) &&
            !string.Equals(
                evidence.Maximum.Window,
                evidence.RequiredWindow,
                StringComparison.OrdinalIgnoreCase))
        {
            return SignalOutcome.Skip;
        }

        return outcome;
    }

    private static SignalOutcome EvaluateCorrectedV2(
        StrategyDefinition strategy,
        ReferenceEvidence evidence)
    {
        SignalOutcome outcome;
        ReferenceAverageEvidence? selectedBoundary;
        if (strategy.Trigger == StrategyTrigger.Up)
        {
            outcome = evidence.MoveAboveMaximumBps >= evidence.ThresholdBps
                ? SignalOutcome.Down
                : SignalOutcome.Skip;
            selectedBoundary = outcome == SignalOutcome.Skip ? null : evidence.Maximum;
        }
        else if (strategy.Trigger == StrategyTrigger.Down)
        {
            outcome = evidence.MoveBelowMinimumBps <= -evidence.ThresholdBps
                ? SignalOutcome.Up
                : SignalOutcome.Skip;
            selectedBoundary = outcome == SignalOutcome.Skip ? null : evidence.Minimum;
        }
        else if (evidence.MoveAboveMaximumBps > 0m)
        {
            outcome = evidence.MoveAboveMaximumBps >= evidence.ThresholdBps
                ? SignalOutcome.Down
                : SignalOutcome.Skip;
            selectedBoundary = outcome == SignalOutcome.Skip ? null : evidence.Maximum;
        }
        else if (evidence.MoveBelowMinimumBps < 0m)
        {
            outcome = evidence.MoveBelowMinimumBps <= -evidence.ThresholdBps
                ? SignalOutcome.Up
                : SignalOutcome.Skip;
            selectedBoundary = outcome == SignalOutcome.Skip ? null : evidence.Minimum;
        }
        else
        {
            outcome = SignalOutcome.Skip;
            selectedBoundary = null;
        }

        if (outcome != SignalOutcome.Skip &&
            IsOptimized(strategy) &&
            !string.Equals(
                selectedBoundary!.Window,
                evidence.RequiredWindow,
                StringComparison.OrdinalIgnoreCase))
        {
            return SignalOutcome.Skip;
        }

        return outcome;
    }

    private static bool TryParseSignalOutcome(string value, out SignalOutcome outcome)
    {
        if (string.Equals(value, "Up", StringComparison.OrdinalIgnoreCase))
        {
            outcome = SignalOutcome.Up;
            return true;
        }

        if (string.Equals(value, "Down", StringComparison.OrdinalIgnoreCase))
        {
            outcome = SignalOutcome.Down;
            return true;
        }

        outcome = default;
        return false;
    }

    private static bool IsOptimized(StrategyDefinition strategy) =>
        strategy.Family == StrategyFamily.OptimizedReferenceAverage;

    private sealed record ReferenceEvidence(
        decimal CurrentPriceUsd,
        decimal ThresholdBps,
        ReferenceAverageEvidence Minimum,
        ReferenceAverageEvidence Maximum,
        decimal MoveBelowMinimumBps,
        decimal MoveAboveMaximumBps,
        string? RequiredWindow,
        string DecisionSource,
        string ReferenceAssetSymbol,
        string LegacySelectedWindow,
        decimal? HistoricalStakeMultiplier,
        string Provenance);

    private sealed record ReferenceAverageEvidence(
        string Window,
        int WindowSeconds,
        decimal PriceUsd);

    private sealed record ReferenceEvidenceOptions(
        string Provenance,
        string? RequiredWindowOverride,
        bool AllowHistoricalRequiredWindowEvidence,
        bool AllowHistoricalStakeMultiplierFromJson)
    {
        public static ReferenceEvidenceOptions ExistingEntry { get; } = new(
            "target_order_raw",
            RequiredWindowOverride: null,
            AllowHistoricalRequiredWindowEvidence: true,
            AllowHistoricalStakeMultiplierFromJson: true);

        public static ReferenceEvidenceOptions TargetDiagnostics { get; } = new(
            "target_skip_diagnostics",
            RequiredWindowOverride: null,
            AllowHistoricalRequiredWindowEvidence: false,
            AllowHistoricalStakeMultiplierFromJson: true);

        public static ReferenceEvidenceOptions OrdinaryReferenceFallback { get; } = new(
            "ordinary_reference_same_market_snapshot",
            RequiredWindowOverride: OptimizedRequiredWindow,
            AllowHistoricalRequiredWindowEvidence: false,
            AllowHistoricalStakeMultiplierFromJson: false);
    }
}
