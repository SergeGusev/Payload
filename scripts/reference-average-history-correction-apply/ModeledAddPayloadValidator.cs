using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReferenceAverageHistoryCorrectionApply;

internal static class ModeledAddPayloadValidator
{
    internal const string IdNamespace = "02e29185-5f14-5f40-b5f7-8c584e8b22e8";
    internal const string IdNameFormat =
        "reference-average-history-correction-v2/{graph_manifest_sha256}/{run_id:D}/{entity_kind}";
    internal const string MainFakExecutionSource = "btc_updown5m_fak_taker_paper";
    internal const string SignalPreviewManifestSha256 =
        "19be8c1ea87bba18feeaec4791ea075c3649ec0276225bde9e85097a8bb8eacd";
    internal const string ReplayClassifierSha256 =
        "bfdf594e4deede640c08d9aa7c41ee34dd056b781a9adb2dc329b97fcf21fc08";
    internal const string SettlementSource = "ReferenceAverageHistoryCorrectionV2Modeled";
    internal const string FillEvidence =
        "ReferenceAverageHistoryCorrectionV2 modeled FAK fill under user-authorized assumed liquidity; " +
        "no historical order-book snapshot was asserted.";

    public static void Validate(AddCandidate add)
    {
        ValidateSizing(add);
        VerifyUtf8Sha256(add.ModeledRawDecisionJson, add.ModeledRawDecisionSha256,
            "modeled_raw_decision_json");
        VerifyUtf8Sha256(add.ModeledMutationPayloadJson, add.ModeledMutationPayloadSha256,
            "modeled_mutation_payload_json");

        using var rawDocument = ParseObject(add.ModeledRawDecisionJson, "modeled_raw_decision_json");
        ValidateRawDecision(rawDocument.RootElement, add);

        using var payloadDocument = ParseObject(add.ModeledMutationPayloadJson,
            "modeled_mutation_payload_json");
        ValidatePayload(payloadDocument.RootElement, add);
    }

    private static void ValidateSizing(AddCandidate add)
    {
        if (add.HistoricalStakeMultiplier <= 0m || add.GammaOrderMinSize <= 0m ||
            add.RawWorstPriceNotionalUsd <= 0m || add.RoundedWorstPriceNotionalUsd <= 0m ||
            add.WorstPriceTargetSizeShares <= 0m || add.RequestedNotionalUsd <= 0m ||
            add.FilledSizeShares <= 0m || add.AssumedFillPrice <= 0m)
        {
            throw Invalid("modeled sizing values must all be positive");
        }

        var rawWorstPriceNotional = add.GammaOrderMinSize * 0.99m * 1.10m *
                                    add.HistoricalStakeMultiplier;
        var roundedWorstPriceNotional = decimal.Ceiling(rawWorstPriceNotional);
        var worstPriceTargetSize = decimal.Ceiling(roundedWorstPriceNotional / 0.99m * 100m) / 100m;
        var requestedNotional = decimal.Round(worstPriceTargetSize * 0.99m, 8,
            MidpointRounding.AwayFromZero);
        var filledSize = decimal.Round(requestedNotional / add.AssumedFillPrice, 8,
            MidpointRounding.AwayFromZero);
        if (add.RawWorstPriceNotionalUsd != rawWorstPriceNotional ||
            add.RoundedWorstPriceNotionalUsd != roundedWorstPriceNotional ||
            add.WorstPriceTargetSizeShares != worstPriceTargetSize ||
            add.RequestedNotionalUsd != requestedNotional ||
            add.FilledSizeShares != filledSize)
        {
            throw Invalid("modeled sizing does not satisfy the exact 0.99 worst-price formula");
        }
    }

    private static void ValidateRawDecision(JsonElement root, AddCandidate add)
    {
        ExactInt(root, "schema_version", 1);
        ExactString(root, "pricing_mode", "history_correction_modeled_fak");
        ExactString(root, "order_execution_mode", "FAK");
        ExactBool(root, "post_only", false);
        ExactString(root, "decision_source", "reference_average_history_correction_v2_modeled_add");
        ExactString(root, "algorithm_decision_source",
            "reference_price_average_envelope_bps_premarket_v2");
        ExactString(root, "correction_provenance",
            "pinned_replay_classifier_plus_user_authorized_liquidity_assumption");
        ExactBool(root, "historical_orderbook_snapshot_asserted", false);
        ExactString(root, "liquidity_assumption", add.AssumedFillPrice == 0.50m
            ? "sufficient_depth_for_full_fill_at_0.50"
            : "sufficient_depth_for_full_fill_at_0.52");
        ExactGuid(root, "source_skipped_run_id", add.RunId);
        ExactString(root, "source_run_full_row_sha256", add.AddSourceRunFullRowSha256);
        ExactString(root, "signal_preview_manifest_sha256", SignalPreviewManifestSha256);
        ExactString(root, "replay_classifier_sha256", ReplayClassifierSha256);
        var replayEvidence = Required(root, "replay_evidence_json");
        if (replayEvidence.ValueKind != JsonValueKind.String || replayEvidence.GetString() is not { } replayEvidenceJson)
        {
            throw Invalid("raw_decision.replay_evidence_json is not a string");
        }
        var replayEvidenceSha = String(root, "replay_evidence_sha256");
        RequireSha256(replayEvidenceSha, "raw_decision.replay_evidence_sha256");
        var actualReplayEvidenceSha = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(replayEvidenceJson))).ToLowerInvariant();
        if (!actualReplayEvidenceSha.Equals(replayEvidenceSha, StringComparison.Ordinal))
        {
            throw Invalid("raw_decision.replay_evidence_json SHA-256 does not match replay_evidence_sha256");
        }
        ExactString(root, "strategy_code", add.StrategyCode);
        ExactString(root, "market_id", add.MarketId);
        ExactString(root, "condition_id", add.ConditionId);
        ExactString(root, "asset_id", add.SelectedTokenId);
        ExactString(root, "outcome", add.SelectedOutcome);
        ExactDecimal(root, "assumed_fill_price", add.AssumedFillPrice);
        ExactDecimal(root, "paper_fak_worst_price", 0.99m);
        ExactString(root, "sizing_rounding", "ceil_usd_then_ceil_worst_price_shares_to_2_decimals");
        ExactDecimal(root, "target_notional_usd", add.RequestedNotionalUsd);
        ExactDecimal(root, "target_size_shares", add.WorstPriceTargetSizeShares);
        ExactDecimal(root, "paper_fak_average_fill_price", add.AssumedFillPrice);
        ExactDecimal(root, "paper_fak_filled_size_shares", add.FilledSizeShares);
        ExactDecimal(root, "paper_fak_filled_notional_usd", add.RequestedNotionalUsd);
        ExactBool(root, "paper_fak_partial_fill", false);
        ExactTimestamp(root, "modeled_entry_at_utc", add.ModeledEntryAtUtc);
        ExactTimestamp(root, "modeled_settled_at_utc", add.ModeledSettledAtUtc);
        ExactString(root, "modeled_settlement_timestamp_source",
            add.ModeledSettlementTimestampSource);
    }

    private static void ValidatePayload(JsonElement root, AddCandidate add)
    {
        ExactInt(root, "schema_version", 1);
        ExactString(root, "operation", "reference_average_history_correction_v2_modeled_add");

        var ids = Object(root, "deterministic_ids");
        ExactString(ids, "algorithm", "UUIDv5/RFC4122-SHA1");
        ExactString(ids, "namespace_id", IdNamespace);
        ExactString(ids, "name_format", IdNameFormat);
        ExactBool(ids, "graph_manifest_sha256_is_bound_after_manifest_creation", true);
        var kinds = Array(ids, "entity_kinds").EnumerateArray().Select(node =>
            node.ValueKind == JsonValueKind.String ? node.GetString() : null).ToArray();
        var expectedKinds = new[]
        {
            "signal", "paper_order", "paper_fill", "paper_position", "paper_position_settlement"
        };
        if (!kinds.SequenceEqual(expectedKinds, StringComparer.Ordinal))
        {
            throw Invalid("payload.deterministic_ids.entity_kinds does not match the exact contract");
        }

        var source = Object(root, "source");
        ExactGuid(source, "run_id", add.RunId);
        ExactString(source, "run_full_row_sha256", add.AddSourceRunFullRowSha256);
        ExactString(source, "entry_timestamp_source", "exact_source_skipped_run_updated_at_utc");
        ExactString(source, "modeled_settlement_timestamp_source",
            add.ModeledSettlementTimestampSource);
        ExactBool(source, "historical_orderbook_snapshot_asserted", false);

        var signal = Object(root, "signal");
        ExactString(signal, "id_kind", "signal");
        ExactNull(signal, "leader_trade_id");
        ExactString(signal, "trader_wallet", "strategy:" + add.StrategyCode);
        ExactString(signal, "condition_id", add.ConditionId);
        ExactString(signal, "asset_id", add.SelectedTokenId);
        ExactString(signal, "outcome", add.SelectedOutcome);
        ExactDecimal(signal, "leader_price", add.AssumedFillPrice);
        ExactNull(signal, "best_bid");
        ExactNull(signal, "best_ask");
        ExactNull(signal, "spread_abs");
        ExactNull(signal, "spread_pct");
        ExactNull(signal, "lag_seconds");
        ExactInt(signal, "score", 100);
        ExactString(signal, "decision", add.StrategyCode + "_entry");
        ExactBool(signal, "accepted", true);
        ExactDecimal(signal, "proposed_paper_price", add.AssumedFillPrice);
        ExactDecimal(signal, "proposed_size_shares", add.FilledSizeShares);
        ExactDecimal(signal, "proposed_notional_usd", add.RequestedNotionalUsd);
        ExactTimestamp(signal, "created_at_utc", add.ModeledEntryAtUtc);
        ExactNull(signal, "raw_context_json");

        var order = Object(root, "paper_order");
        ExactString(order, "id_kind", "paper_order");
        ExactString(order, "signal_id_kind", "signal");
        ExactGuid(order, "strategy_id", add.StrategyId);
        ExactString(order, "copied_trader_wallet", "strategy:" + add.StrategyCode);
        ExactString(order, "status", "Filled");
        ExactString(order, "side", "Buy");
        ExactString(order, "asset_id", add.SelectedTokenId);
        ExactString(order, "condition_id", add.ConditionId);
        ExactString(order, "outcome", add.SelectedOutcome);
        ExactDecimal(order, "price", add.AssumedFillPrice);
        ExactDecimal(order, "size_shares", add.FilledSizeShares);
        ExactDecimal(order, "notional_usd", add.RequestedNotionalUsd);
        ExactTimestamp(order, "created_at_utc", add.ModeledEntryAtUtc);
        ExactTimestamp(order, "expires_at_utc", add.ModeledEntryAtUtc);
        ExactTimestamp(order, "filled_at_utc", add.ModeledEntryAtUtc);
        ExactNull(order, "cancelled_at_utc");
        ExactString(order, "raw_decision_json", add.ModeledRawDecisionJson);
        ExactString(order, "raw_decision_sha256", add.ModeledRawDecisionSha256);
        ExactNull(order, "correlation_id");
        ExactString(order, "execution_source", MainFakExecutionSource);

        var fill = Object(root, "paper_fill");
        ExactString(fill, "id_kind", "paper_fill");
        ExactString(fill, "paper_order_id_kind", "paper_order");
        ExactDecimal(fill, "price", add.AssumedFillPrice);
        ExactDecimal(fill, "size_shares", add.FilledSizeShares);
        ExactTimestamp(fill, "filled_at_utc", add.ModeledEntryAtUtc);
        ExactString(fill, "evidence", add.ModeledFillEvidence);
        ExactString(fill, "evidence", FillEvidence);
        ExactDecimal(fill, "realized_pnl_usd", 0m);

        var run = Object(root, "strategy_market_paper_run_update");
        ExactGuid(run, "id", add.RunId);
        ExactString(run, "status", "Settled");
        ExactString(run, "selected_asset_id", add.SelectedTokenId);
        ExactString(run, "selected_outcome", add.SelectedOutcome);
        ExactDecimal(run, "entry_price", add.AssumedFillPrice);
        ExactDecimal(run, "stake_usd", add.RequestedNotionalUsd);
        ExactDecimal(run, "size_shares", add.FilledSizeShares);
        ExactString(run, "signal_id_kind", "signal");
        ExactString(run, "paper_order_id_kind", "paper_order");
        ExactTimestamp(run, "entered_at_utc", add.ModeledEntryAtUtc);
        ExactDecimal(run, "settlement_price", add.SettlementPrice);
        ExactDecimal(run, "settlement_value_usd", add.SettlementValueUsd);
        ExactDecimal(run, "realized_pnl_usd", add.RealizedPnlUsd);
        ExactTimestamp(run, "settled_at_utc", add.ModeledSettledAtUtc);
        ExactNull(run, "skip_reason");
        ExactNull(run, "skip_diagnostics_json");
        ExactTimestamp(run, "updated_at_utc", add.ModeledSettledAtUtc);
        ExactBool(run, "preserve_unlisted_source_columns", true);

        var position = Object(root, "paper_position");
        ExactString(position, "id_kind", "paper_position");
        ExactString(position, "copied_trader_wallet", "strategy:" + add.StrategyCode);
        ExactString(position, "asset_id", add.SelectedTokenId);
        ExactString(position, "condition_id", add.ConditionId);
        ExactString(position, "outcome", add.SelectedOutcome);
        ExactDecimal(position, "size_shares", 0m);
        ExactDecimal(position, "average_price", 0m);
        ExactDecimal(position, "estimated_value_usd", 0m);
        ExactDecimal(position, "unrealized_pnl_usd", 0m);
        ExactTimestamp(position, "updated_at_utc", add.ModeledSettledAtUtc);

        var settlement = Object(root, "paper_position_settlement");
        ExactString(settlement, "id_kind", "paper_position_settlement");
        ExactString(settlement, "copied_trader_wallet", "strategy:" + add.StrategyCode);
        ExactString(settlement, "asset_id", add.SelectedTokenId);
        ExactString(settlement, "condition_id", add.ConditionId);
        ExactString(settlement, "outcome", add.SelectedOutcome);
        ExactString(settlement, "winning_asset_id", add.ResolvedWinningTokenId);
        ExactString(settlement, "winning_outcome", add.ResolvedWinningOutcome);
        ExactString(settlement, "category", add.SettlementCategory);
        ExactDecimal(settlement, "settled_size_shares", add.FilledSizeShares);
        ExactDecimal(settlement, "average_price", add.AssumedFillPrice);
        ExactDecimal(settlement, "cost_basis_usd", add.RequestedNotionalUsd);
        ExactDecimal(settlement, "settlement_value_usd", add.SettlementValueUsd);
        ExactDecimal(settlement, "realized_pnl_usd", add.RealizedPnlUsd);
        ExactBool(settlement, "won", add.Won);
        ExactString(settlement, "settlement_source", SettlementSource);
        ExactTimestamp(settlement, "settled_at_utc", add.ModeledSettledAtUtc);
        ExactTimestamp(settlement, "created_at_utc", add.ModeledSettledAtUtc);
    }

    private static JsonDocument ParseObject(string json, string field)
    {
        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw Invalid($"{field} root is not an object");
            }
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{field} is not valid JSON: {exception.Message}", exception);
        }
    }

    private static void VerifyUtf8Sha256(string text, string expected, string field)
    {
        RequireSha256(expected, field + "_sha256");
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.Ordinal))
        {
            throw Invalid($"{field} UTF-8 SHA-256 mismatch: expected {expected}, actual {actual}");
        }
    }

    private static JsonElement Required(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value)
            ? value
            : throw Invalid($"required property '{property}' is missing");

    private static JsonElement Object(JsonElement parent, string property)
    {
        var value = Required(parent, property);
        return value.ValueKind == JsonValueKind.Object
            ? value
            : throw Invalid($"property '{property}' is not an object");
    }

    private static JsonElement Array(JsonElement parent, string property)
    {
        var value = Required(parent, property);
        return value.ValueKind == JsonValueKind.Array
            ? value
            : throw Invalid($"property '{property}' is not an array");
    }

    private static string String(JsonElement parent, string property)
    {
        var value = Required(parent, property);
        return value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text
            : throw Invalid($"property '{property}' is not a string");
    }

    private static void ExactString(JsonElement parent, string property, string expected)
    {
        var actual = String(parent, property);
        if (!actual.Equals(expected, StringComparison.Ordinal))
        {
            throw Invalid($"property '{property}' mismatch: expected '{expected}', actual '{actual}'");
        }
    }

    private static void ExactGuid(JsonElement parent, string property, Guid expected)
    {
        var actual = String(parent, property);
        if (!Guid.TryParse(actual, out var parsed) || parsed != expected)
        {
            throw Invalid($"property '{property}' is not expected UUID {expected:D}");
        }
    }

    private static void ExactBool(JsonElement parent, string property, bool expected)
    {
        var value = Required(parent, property);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            value.GetBoolean() != expected)
        {
            throw Invalid($"property '{property}' is not expected boolean {expected}");
        }
    }

    private static void ExactNull(JsonElement parent, string property)
    {
        if (Required(parent, property).ValueKind != JsonValueKind.Null)
        {
            throw Invalid($"property '{property}' must be JSON null");
        }
    }

    private static void ExactInt(JsonElement parent, string property, int expected)
    {
        var value = Required(parent, property);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var actual) || actual != expected)
        {
            throw Invalid($"property '{property}' is not expected integer {expected}");
        }
    }

    private static void ExactDecimal(JsonElement parent, string property, decimal expected)
    {
        var value = Required(parent, property);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var actual) || actual != expected)
        {
            throw Invalid($"property '{property}' is not expected decimal {expected.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static void ExactTimestamp(JsonElement parent, string property, DateTimeOffset expected)
    {
        var text = String(parent, property);
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var actual) ||
            actual.ToUniversalTime() != expected.ToUniversalTime())
        {
            throw Invalid($"property '{property}' is not expected timestamp {expected:O}");
        }
    }

    private static void RequireSha256(string value, string field)
    {
        if (value.Length != 64 || value.Any(character =>
                !((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))))
        {
            throw Invalid($"{field} is not a lowercase SHA-256 hex digest");
        }
    }

    private static InvalidDataException Invalid(string message) =>
        new("Modeled add payload contract violation: " + message + ".");
}
