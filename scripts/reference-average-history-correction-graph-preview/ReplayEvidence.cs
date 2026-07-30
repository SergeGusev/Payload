using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ReferenceAverageHistoryCorrectionGraphPreview;

internal enum ChildLinkDisposition
{
    Unrelated,
    Exact,
    InvariantError
}

internal sealed record ChildLinkResult(
    ChildLinkDisposition Disposition,
    Guid? ParentRunId,
    string Reason);

internal static class ChildLinkMatcher
{
    public static ChildOrderRunLinkValidation ValidateOrderRunLink(
        Guid orderId,
        Guid signalId,
        Guid strategyId,
        DateTimeOffset cutoffUtc,
        IReadOnlyList<ChildRunLinkEvidence> runLinks)
    {
        var candidates = runLinks.Where(item =>
                item.PaperOrderId == orderId || item.SignalId == signalId)
            .ToArray();
        var exact = candidates.Where(item =>
                item.PaperOrderId == orderId &&
                item.SignalId == signalId &&
                item.StrategyId == strategyId &&
                item.EntryDueAtUtc < cutoffUtc)
            .ToArray();
        var valid = candidates.Length == 1 && exact.Length == 1;
        return new ChildOrderRunLinkValidation(
            valid,
            candidates.Length,
            exact.Length,
            valid ? "exact_child_order_run_link" : "raw_linked_child_order_run_link_missing_partial_or_ambiguous");
    }

    public static ChildLinkResult Match(
        GraphOrder child,
        IReadOnlyDictionary<Guid, GraphOrder> mainByRun,
        IReadOnlyDictionary<Guid, GraphOrder> mainByOrder,
        IReadOnlyDictionary<Guid, GraphOrder> mainBySignal)
    {
        var reference = InspectParentReference(
            child.RawDecisionJson,
            mainByRun,
            mainByOrder,
            mainBySignal);
        if (reference.Disposition != ChildLinkDisposition.Exact)
        {
            return reference;
        }

        try
        {
            using var document = JsonDocument.Parse(child.RawDecisionJson!);
            var root = document.RootElement;
            var parentRunId = reference.ParentRunId!.Value;
            var parent = mainByRun[parentRunId];

            var rawMarketId = ReadRequiredString(root, "market_id");
            var rawParentStrategyId = ReadGuid(root, "parent_strategy_id");
            var rawParentStrategyCode = ReadRequiredString(root, "parent_strategy_code");
            var rawParentStrategyName = ReadRequiredString(root, "parent_strategy_name");
            var rawChildStrategyId = ReadGuid(root, "child_strategy_id");
            var rawChildStrategyCode = ReadRequiredString(root, "child_strategy_code");
            var rawConditionId = ReadRequiredString(root, "condition_id");
            var rawMarketSlug = ReadRequiredString(root, "market_slug");
            var rawOutcome = ReadRequiredString(root, "outcome");
            var rawAssetId = ReadRequiredString(root, "asset_id");
            var rawExecutionSource = ReadRequiredString(root, "execution_source");
            var rawCopiedAtUtc = ReadRequiredTimestamp(root, "copied_at_utc");
            var rawOrderPrice = ReadRequiredDecimal(root, "order_price");
            var rawEntryPrice = ReadRequiredDecimal(root, "entry_price");
            var rawStakeUsd = ReadRequiredDecimal(root, "stake_usd");
            var rawSizeShares = ReadRequiredDecimal(root, "size_shares");
            if (!string.Equals(child.MarketId, parent.MarketId, StringComparison.Ordinal) ||
                !string.Equals(rawMarketId, parent.MarketId, StringComparison.Ordinal) ||
                rawParentStrategyId != parent.StrategyId ||
                !string.Equals(rawParentStrategyCode, parent.StrategyCode, StringComparison.Ordinal) ||
                !string.Equals(rawParentStrategyName, parent.StrategyNameProof, StringComparison.Ordinal) ||
                rawChildStrategyId != child.StrategyId ||
                !string.Equals(rawChildStrategyCode, child.StrategyCode, StringComparison.Ordinal) ||
                !string.Equals(rawConditionId, child.ConditionId, StringComparison.Ordinal) ||
                !string.Equals(rawConditionId, parent.ConditionId, StringComparison.Ordinal) ||
                !string.Equals(rawMarketSlug, child.MarketSlugProof, StringComparison.Ordinal) ||
                !string.Equals(rawMarketSlug, parent.MarketSlugProof, StringComparison.Ordinal) ||
                !string.Equals(rawOutcome, child.OrderOutcome, StringComparison.Ordinal) ||
                !string.Equals(rawAssetId, child.AssetId, StringComparison.Ordinal) ||
                !string.Equals(rawExecutionSource, child.ExecutionSource, StringComparison.Ordinal) ||
                rawOrderPrice != child.OrderPrice ||
                rawEntryPrice != child.SignalProposedPaperPriceProof ||
                rawStakeUsd != child.OrderNotionalUsd ||
                rawSizeShares != child.OrderSizeShares ||
                !string.Equals(child.OrderOutcome, parent.OrderOutcome, StringComparison.Ordinal) ||
                !string.Equals(child.RunOutcome, parent.RunOutcome, StringComparison.Ordinal) ||
                !string.Equals(child.AssetId, parent.AssetId, StringComparison.Ordinal) ||
                child.EntryDueAtUtc != parent.EntryDueAtUtc ||
                rawCopiedAtUtc != child.OrderCreatedAtUtc ||
                rawCopiedAtUtc != child.SignalCreatedAtUtcProof ||
                rawCopiedAtUtc != child.RunEnteredAtUtcProof ||
                rawCopiedAtUtc != child.RunCreatedAtUtcProof)
            {
                return new ChildLinkResult(
                    ChildLinkDisposition.InvariantError,
                    parentRunId,
                    "child_parent_market_strategy_or_outcome_mismatch");
            }

            if (!string.Equals(
                    child.ExecutionSource,
                    CorrectionGraphInvariantValidator.ChildFakExecutionSource,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    parent.ExecutionSource,
                    CorrectionGraphInvariantValidator.MainFakExecutionSource,
                    StringComparison.Ordinal) ||
                child.EntryPrice != parent.EntryPrice ||
                child.StakeUsd != parent.StakeUsd ||
                child.RunSizeShares != parent.RunSizeShares ||
                child.OrderPrice != parent.OrderPrice ||
                child.OrderSizeShares != parent.OrderSizeShares ||
                child.OrderNotionalUsd != parent.OrderNotionalUsd)
            {
                return new ChildLinkResult(
                    ChildLinkDisposition.InvariantError,
                    parentRunId,
                    "child_parent_fak_monetary_or_provenance_mismatch");
            }

            return new ChildLinkResult(ChildLinkDisposition.Exact, parentRunId, "exact_child_parent_link");
        }
        catch (JsonException exception)
        {
            return new ChildLinkResult(
                ChildLinkDisposition.InvariantError,
                null,
                "child_raw_decision_invalid_json:" + exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return new ChildLinkResult(ChildLinkDisposition.InvariantError, null, exception.Message);
        }
    }

    public static ChildLinkResult InspectParentReference(
        string? rawDecisionJson,
        IReadOnlyDictionary<Guid, GraphOrder> mainByRun,
        IReadOnlyDictionary<Guid, GraphOrder> mainByOrder,
        IReadOnlyDictionary<Guid, GraphOrder> mainBySignal)
    {
        if (string.IsNullOrWhiteSpace(rawDecisionJson))
        {
            return new ChildLinkResult(ChildLinkDisposition.InvariantError, null, "child_raw_decision_missing");
        }

        try
        {
            using var document = JsonDocument.Parse(rawDecisionJson);
            var root = document.RootElement;
            var parentRun = ReadGuidCandidate(root, "parent_run_id");
            var parentOrder = ReadGuidCandidate(root, "parent_paper_order_id");
            var parentSignal = ReadGuidCandidate(root, "parent_signal_id");
            var parentRunId = parentRun.Value;
            var parentOrderId = parentOrder.Value;
            var parentSignalId = parentSignal.Value;
            var anyMatch = parentRunId is { } runId && mainByRun.ContainsKey(runId) ||
                           parentOrderId is { } orderId && mainByOrder.ContainsKey(orderId) ||
                           parentSignalId is { } signalId && mainBySignal.ContainsKey(signalId);
            if (!anyMatch)
            {
                return new ChildLinkResult(ChildLinkDisposition.Unrelated, null, "parent_not_in_main_removals");
            }

            if (!TryReadString(root, "pricing_mode", out var pricingMode) ||
                !string.Equals(pricingMode, CorrectionContract.ChildPricingMode, StringComparison.Ordinal))
            {
                return new ChildLinkResult(
                    ChildLinkDisposition.InvariantError,
                    parentRunId,
                    "linked_child_pricing_mode_missing_or_invalid");
            }

            if (parentRunId is null || parentOrderId is null || parentSignalId is null ||
                !parentRun.StrictDFormat || !parentOrder.StrictDFormat || !parentSignal.StrictDFormat)
            {
                return new ChildLinkResult(
                    ChildLinkDisposition.InvariantError,
                    parentRunId,
                    "child_parent_identifier_missing");
            }

            if (!mainByRun.TryGetValue(parentRunId.Value, out var parent) ||
                !mainByOrder.TryGetValue(parentOrderId.Value, out var orderParent) ||
                !mainBySignal.TryGetValue(parentSignalId.Value, out var signalParent) ||
                parent.RunId != orderParent.RunId ||
                parent.RunId != signalParent.RunId)
            {
                return new ChildLinkResult(
                    ChildLinkDisposition.InvariantError,
                    parentRunId,
                    "child_parent_identifiers_do_not_resolve_to_one_main_row");
            }

            return new ChildLinkResult(ChildLinkDisposition.Exact, parentRunId, "exact_parent_reference");
        }
        catch (JsonException exception)
        {
            return new ChildLinkResult(
                ChildLinkDisposition.InvariantError,
                null,
                "child_raw_decision_invalid_json:" + exception.Message);
        }
    }

    private static Guid? ReadGuid(JsonElement root, string property)
    {
        if (!TryReadString(root, property, out var raw))
        {
            return null;
        }

        if (!Guid.TryParseExact(raw, "D", out var parsed))
        {
            throw new InvalidDataException($"child_{property}_invalid");
        }

        return parsed;
    }

    private static GuidCandidate ReadGuidCandidate(JsonElement root, string property)
    {
        if (!TryReadString(root, property, out var raw))
        {
            return new GuidCandidate(null, false);
        }

        if (!Guid.TryParse(raw, out var parsed))
        {
            return new GuidCandidate(null, false);
        }

        return new GuidCandidate(parsed, Guid.TryParseExact(raw, "D", out _));
    }

    private static string ReadRequiredString(JsonElement root, string property) =>
        TryReadString(root, property, out var value)
            ? value
            : throw new InvalidDataException($"child_{property}_missing");

    private static decimal ReadRequiredDecimal(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var node) ||
            node.ValueKind != JsonValueKind.Number ||
            !node.TryGetDecimal(out var value))
        {
            throw new InvalidDataException($"child_{property}_missing_or_invalid");
        }

        return value;
    }

    private static DateTimeOffset ReadRequiredTimestamp(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var node) ||
            node.ValueKind != JsonValueKind.String ||
            !node.TryGetDateTimeOffset(out var value))
        {
            throw new InvalidDataException($"child_{property}_missing_or_invalid");
        }

        return value;
    }

    private static bool TryReadString(JsonElement root, string property, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = node.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private sealed record GuidCandidate(Guid? Value, bool StrictDFormat);
}

internal static class CorrectionGraphInvariantValidator
{
    public const string MainFakExecutionSource = "btc_updown5m_fak_taker_paper";
    public const string ChildFakExecutionSource = "btc_updown5m_child_mirror_fak_paper";

    public static GraphSemanticValidation ValidateRow(GraphOrder row)
    {
        var isMain = string.Equals(row.Scope, "Main", StringComparison.Ordinal);
        var isChild = string.Equals(row.Scope, "Child", StringComparison.Ordinal);
        if (!isMain && !isChild)
        {
            return Invalid("correction_graph_scope_invalid", $"scope={row.Scope}");
        }

        var exactFakProvenance = isMain
            ? string.Equals(row.ExecutionSource, MainFakExecutionSource, StringComparison.Ordinal) &&
              string.Equals(row.OrderExecutionModeProof, "FAK", StringComparison.Ordinal)
            : string.Equals(row.ExecutionSource, ChildFakExecutionSource, StringComparison.Ordinal) &&
              row.ParentMainRunId is not null;
        if (!exactFakProvenance)
        {
            return Invalid(
                "correction_graph_non_fak_provenance",
                $"scope={row.Scope};execution_source={row.ExecutionSource};" +
                $"order_execution_mode={row.OrderExecutionModeProof}");
        }

        if (!string.Equals(row.OrderStatus, "Filled", StringComparison.Ordinal) ||
            !string.Equals(row.OrderSide, "Buy", StringComparison.Ordinal) ||
            row.OrderFilledAtUtc is null ||
            row.OrderCancelledAtUtc is not null ||
            row.CorrelationId is not null ||
            row.OrderCreatedAtUtc != row.OrderExpiresAtUtc ||
            row.OrderCreatedAtUtc != row.OrderFilledAtUtc)
        {
            return Invalid(
                "correction_fak_order_state_or_timestamp_mismatch",
                $"status={row.OrderStatus};side={row.OrderSide};created={Format.Timestamp(row.OrderCreatedAtUtc)};" +
                $"expires={Format.Timestamp(row.OrderExpiresAtUtc)};filled={Format.Timestamp(row.OrderFilledAtUtc)};" +
                $"cancelled={Format.Timestamp(row.OrderCancelledAtUtc)};correlation={Format.Guid(row.CorrelationId)}");
        }

        var expectedWallet = "strategy:" + row.StrategyCode;
        var expectedDecision = row.StrategyCode + "_entry";
        if (!string.Equals(row.SignalTraderWalletProof, expectedWallet, StringComparison.Ordinal) ||
            !string.Equals(row.SignalTraderWalletProof, row.CopiedTraderWallet, StringComparison.Ordinal) ||
            row.SignalScoreProof != 100 ||
            row.SignalAcceptedProof != true ||
            !string.Equals(row.SignalDecisionProof, expectedDecision, StringComparison.Ordinal) ||
            row.SignalLeaderPriceProof != row.OrderPrice ||
            row.SignalProposedPaperPriceProof != row.OrderPrice ||
            row.SignalProposedSizeSharesProof != row.OrderSizeShares ||
            row.SignalProposedNotionalUsdProof != row.OrderNotionalUsd ||
            row.SignalCreatedAtUtcProof != row.OrderCreatedAtUtc ||
            !row.SignalNullableShapeValidProof ||
            row.SignalLeaderTradeIdProof is not null ||
            row.SignalBestBidProof is not null ||
            row.SignalBestAskProof is not null ||
            row.SignalSpreadAbsProof is not null ||
            row.SignalSpreadPctProof is not null ||
            row.SignalLagSecondsProof is not null ||
            row.SignalRawContextJsonProof is not null)
        {
            return Invalid(
                "correction_signal_shape_mismatch",
                $"wallet={row.SignalTraderWalletProof};score={row.SignalScoreProof};" +
                $"accepted={row.SignalAcceptedProof};decision={row.SignalDecisionProof};" +
                $"leader_price={Format.NullableDecimal(row.SignalLeaderPriceProof)};" +
                $"proposed_price={Format.NullableDecimal(row.SignalProposedPaperPriceProof)};" +
                $"proposed_size={Format.NullableDecimal(row.SignalProposedSizeSharesProof)};" +
                $"proposed_notional={Format.NullableDecimal(row.SignalProposedNotionalUsdProof)};" +
                $"created={Format.Timestamp(row.SignalCreatedAtUtcProof)};" +
                $"nullable_shape={row.SignalNullableShapeValidProof}");
        }

        if (row.RunEnteredAtUtcProof != row.OrderCreatedAtUtc ||
            row.RunSkipReasonProof is not null ||
            !row.RunSkipDiagnosticsIsNullProof ||
            row.MarketEndUtcProof is null ||
            row.SettledAtUtc is null ||
            row.MarketEndUtcProof > row.SettledAtUtc ||
            row.RunUpdatedAtUtcProof != row.SettledAtUtc)
        {
            return Invalid(
                "correction_fak_run_state_or_timestamp_mismatch",
                $"entered={Format.Timestamp(row.RunEnteredAtUtcProof)};order_created={Format.Timestamp(row.OrderCreatedAtUtc)};" +
                $"skip_reason={row.RunSkipReasonProof};skip_diagnostics_null={row.RunSkipDiagnosticsIsNullProof};" +
                $"market_end={Format.Timestamp(row.MarketEndUtcProof)};settled={Format.Timestamp(row.SettledAtUtc)};" +
                $"updated={Format.Timestamp(row.RunUpdatedAtUtcProof)}");
        }

        if (row.EntryPrice != row.OrderPrice ||
            row.RunSizeShares != row.OrderSizeShares ||
            row.StakeUsd != row.OrderNotionalUsd)
        {
            return Invalid(
                "correction_fak_run_order_monetary_mismatch",
                $"entry={Format.NullableDecimal(row.EntryPrice)};order_price={Format.Decimal(row.OrderPrice)};" +
                $"run_size={Format.NullableDecimal(row.RunSizeShares)};order_size={Format.Decimal(row.OrderSizeShares)};" +
                $"stake={Format.Decimal(row.StakeUsd)};order_notional={Format.Decimal(row.OrderNotionalUsd)}");
        }

        if (!CanonicalEvidence.IsSha256(row.RunFullRowSha256) ||
            !CanonicalEvidence.IsSha256(row.OrderFullRowSha256) ||
            !CanonicalEvidence.IsSha256(row.SignalFullRowSha256))
        {
            return Invalid(
                "full_row_sha256_missing_or_invalid",
                $"run={row.RunFullRowSha256};order={row.OrderFullRowSha256};signal={row.SignalFullRowSha256}");
        }

        return Valid("exact_correction_fak_row");
    }

    public static GraphSemanticValidation ValidateFillSet(
        GraphOrder row,
        IReadOnlyList<GraphFill> fills)
    {
        if (fills.Count != 1)
        {
            return Invalid("correction_fak_fill_count_not_one", $"fill_count={fills.Count}");
        }

        var fill = fills[0];
        var expectedSettlementValue = RoundScale8(
            row.OrderSizeShares * (row.SettlementPrice ?? decimal.MinValue));
        var expectedPnl = RoundScale8(expectedSettlementValue - row.OrderNotionalUsd);
        if (!CanonicalEvidence.IsSha256(fill.FullRowSha256) ||
            fill.Price != row.OrderPrice ||
            fill.SizeShares != row.OrderSizeShares ||
            fill.FilledAtUtc != row.OrderCreatedAtUtc ||
            fill.FilledAtUtc != row.OrderFilledAtUtc ||
            fill.RealizedPnlUsd != 0m ||
            RoundScale8(fill.Price * fill.SizeShares) != RoundScale8(row.OrderNotionalUsd) ||
            expectedSettlementValue != RoundScale8(row.SettlementValueUsd ?? decimal.MinValue) ||
            expectedPnl != RoundScale8(row.RunRealizedPnlUsd ?? decimal.MinValue) ||
            row.SettledAtUtc is null ||
            fill.FilledAtUtc > row.SettledAtUtc)
        {
            return Invalid(
                "correction_fak_fill_or_settlement_mismatch",
                $"fill_price={Format.Decimal(fill.Price)};order_price={Format.Decimal(row.OrderPrice)};" +
                $"fill_size={Format.Decimal(fill.SizeShares)};order_size={Format.Decimal(row.OrderSizeShares)};" +
                $"fill_at={Format.Timestamp(fill.FilledAtUtc)};order_filled_at={Format.Timestamp(row.OrderFilledAtUtc)};" +
                $"fill_realized_pnl={Format.Decimal(fill.RealizedPnlUsd)};" +
                $"settlement={Format.NullableDecimal(row.SettlementValueUsd)};" +
                $"run_pnl={Format.NullableDecimal(row.RunRealizedPnlUsd)}");
        }

        return Valid("exact_correction_fak_fill");
    }

    public static GraphSemanticValidation ValidateChildParentFillParity(
        GraphOrder child,
        GraphOrder parent,
        IReadOnlyList<GraphFill> childFills,
        IReadOnlyList<GraphFill> parentFills)
    {
        if (!string.Equals(child.ExecutionSource, ChildFakExecutionSource, StringComparison.Ordinal))
        {
            return Invalid("child_not_fak_mirror", child.ExecutionSource);
        }

        if (childFills.Count != 1 || parentFills.Count != 1)
        {
            return Invalid(
                "child_parent_fak_fill_count_mismatch",
                $"child={childFills.Count};parent={parentFills.Count}");
        }

        var childFill = childFills[0];
        var parentFill = parentFills[0];
        if (child.EntryPrice != parent.EntryPrice ||
            child.StakeUsd != parent.StakeUsd ||
            child.RunSizeShares != parent.RunSizeShares ||
            child.OrderPrice != parent.OrderPrice ||
            child.OrderSizeShares != parent.OrderSizeShares ||
            child.OrderNotionalUsd != parent.OrderNotionalUsd ||
            child.SettlementPrice != parent.SettlementPrice ||
            child.SettlementValueUsd != parent.SettlementValueUsd ||
            child.RunRealizedPnlUsd != parent.RunRealizedPnlUsd ||
            child.OrderCreatedAtUtc != parent.OrderCreatedAtUtc ||
            child.OrderExpiresAtUtc != parent.OrderExpiresAtUtc ||
            child.OrderFilledAtUtc != parent.OrderFilledAtUtc ||
            child.SignalCreatedAtUtcProof != parent.SignalCreatedAtUtcProof ||
            childFill.Price != parentFill.Price ||
            childFill.SizeShares != parentFill.SizeShares ||
            childFill.FilledAtUtc != parentFill.FilledAtUtc ||
            childFill.RealizedPnlUsd != parentFill.RealizedPnlUsd)
        {
            return Invalid(
                "child_parent_fak_fill_parity_mismatch",
                $"child_run={child.RunId:D};parent_run={parent.RunId:D}");
        }

        return Valid("exact_child_parent_fak_fill_parity");
    }

    private static decimal RoundScale8(decimal value) =>
        decimal.Round(value, 8, MidpointRounding.AwayFromZero);

    private static GraphSemanticValidation Valid(string reason) => new(true, reason, string.Empty);

    private static GraphSemanticValidation Invalid(string reason, string details) => new(false, reason, details);
}

internal static class PositionEvidenceValidator
{
    public static PositionSemanticValidation ValidateExclusiveKey(
        IReadOnlyList<GraphOrder> orders,
        IReadOnlyList<GraphFill> fills,
        IReadOnlyList<PositionRow> positions,
        IReadOnlyList<PositionSettlementRow> settlements)
    {
        if (orders.Count == 0)
        {
            return Invalid("position_graph_orders_missing", "orders=0");
        }

        if (positions.Count == 0 && settlements.Count == 0)
        {
            return new PositionSemanticValidation(true, "not_applicable_no_position_rows", "positions=0;settlements=0");
        }

        if (positions.Count != 1 || settlements.Count != 1)
        {
            return Invalid(
                "position_settlement_pair_missing_or_ambiguous",
                $"positions={positions.Count};settlements={settlements.Count}");
        }

        var orderIds = orders.Select(item => item.OrderId).ToHashSet();
        var keyFills = fills.ToArray();
        if (keyFills.Any(item => !orderIds.Contains(item.OrderId)))
        {
            return Invalid("position_fill_outside_exact_key_graph", $"fills={keyFills.Length}");
        }
        if (keyFills.Length == 0 || keyFills.Any(item => item.Price <= 0m || item.SizeShares <= 0m))
        {
            return Invalid("position_graph_fill_set_invalid", $"fills={keyFills.Length}");
        }

        var wallet = orders[0].CopiedTraderWallet;
        var assetId = orders[0].AssetId;
        var conditionId = orders[0].ConditionId;
        var outcome = orders[0].OrderOutcome;
        var category = orders[0].RunCategoryProof;
        if (orders.Any(item =>
                !string.Equals(item.CopiedTraderWallet, wallet, StringComparison.Ordinal) ||
                !string.Equals(item.AssetId, assetId, StringComparison.Ordinal) ||
                !string.Equals(item.ConditionId, conditionId, StringComparison.Ordinal) ||
                !string.Equals(item.OrderOutcome, outcome, StringComparison.Ordinal) ||
                !string.Equals(item.RunCategoryProof, category, StringComparison.Ordinal)))
        {
            return Invalid("position_graph_identity_not_uniform", $"orders={orders.Count}");
        }

        var position = positions[0];
        var settlement = settlements[0];
        var fillSize = RoundScale8(keyFills.Sum(item => item.SizeShares));
        var fillNotional = RoundScale8(keyFills.Sum(item => item.Price * item.SizeShares));
        var averagePrice = fillSize > 0m ? RoundScale8(fillNotional / fillSize) : 0m;
        var settlementPrices = orders
            .Select(item => item.SettlementPrice)
            .Distinct()
            .ToArray();
        if (settlementPrices.Length != 1 || settlementPrices[0] is not (0m or 1m))
        {
            return Invalid("position_graph_settlement_price_not_uniform_binary", "");
        }
        if (orders.Any(item => item.SettlementValueUsd is null || item.RunRealizedPnlUsd is null))
        {
            return Invalid("position_graph_run_settlement_arithmetic_missing", "");
        }

        var expectedWon = settlementPrices[0] == 1m;
        var expectedSettlementValue = expectedWon ? fillSize : 0m;
        var expectedPnl = RoundScale8(expectedSettlementValue - fillNotional);
        var runSettlementValue = RoundScale8(orders.Sum(item => item.SettlementValueUsd!.Value));
        var runPnl = RoundScale8(orders.Sum(item => item.RunRealizedPnlUsd!.Value));
        var expectedWinningOutcome = expectedWon
            ? outcome
            : string.Equals(outcome, "Up", StringComparison.OrdinalIgnoreCase)
                ? "Down"
                : string.Equals(outcome, "Down", StringComparison.OrdinalIgnoreCase)
                    ? "Up"
                    : string.Empty;
        var winnerOutcomeMatches = string.Equals(
            settlement.WinningOutcome,
            expectedWinningOutcome,
            StringComparison.OrdinalIgnoreCase);

        var valid =
            string.Equals(position.CopiedTraderWallet, wallet, StringComparison.Ordinal) &&
            string.Equals(position.AssetId, assetId, StringComparison.Ordinal) &&
            string.Equals(position.ConditionId, conditionId, StringComparison.Ordinal) &&
            string.Equals(position.Outcome, outcome, StringComparison.Ordinal) &&
            position.SizeShares == 0m &&
            position.AveragePrice == 0m &&
            position.EstimatedValueUsd == 0m &&
            position.UnrealizedPnlUsd == 0m &&
            CanonicalEvidence.IsSha256(position.FullRowSha256) &&
            string.Equals(settlement.CopiedTraderWallet, wallet, StringComparison.Ordinal) &&
            string.Equals(settlement.AssetId, assetId, StringComparison.Ordinal) &&
            string.Equals(settlement.ConditionId, conditionId, StringComparison.Ordinal) &&
            string.Equals(settlement.Outcome, outcome, StringComparison.Ordinal) &&
            string.Equals(settlement.Category, category, StringComparison.Ordinal) &&
            string.Equals(settlement.SettlementSource, "BtcUpDown5mGammaClosedMarket", StringComparison.Ordinal) &&
            settlement.CreatedAtUtc == settlement.SettledAtUtc &&
            position.UpdatedAtUtc == settlement.SettledAtUtc &&
            CanonicalEvidence.IsSha256(settlement.FullRowSha256) &&
            RoundScale8(settlement.SettledSizeShares) == fillSize &&
            RoundScale8(settlement.AveragePrice) == averagePrice &&
            RoundScale8(settlement.CostBasisUsd) == fillNotional &&
            RoundScale8(settlement.SettlementValueUsd) == expectedSettlementValue &&
            RoundScale8(settlement.RealizedPnlUsd) == expectedPnl &&
            settlement.Won == expectedWon &&
            !string.IsNullOrWhiteSpace(expectedWinningOutcome) &&
            winnerOutcomeMatches &&
            runSettlementValue == expectedSettlementValue &&
            runPnl == expectedPnl;
        return valid
            ? new PositionSemanticValidation(
                true,
                "exact_position_settlement_arithmetic",
                $"fill_size={Format.Decimal(fillSize)};cost={Format.Decimal(fillNotional)};" +
                $"settlement={Format.Decimal(expectedSettlementValue)};pnl={Format.Decimal(expectedPnl)}")
            : Invalid(
                "position_settlement_identity_or_arithmetic_mismatch",
                $"fill_size={Format.Decimal(fillSize)};fill_notional={Format.Decimal(fillNotional)};" +
                $"average={Format.Decimal(averagePrice)};expected_won={Format.Bool(expectedWon)};" +
                $"position_size={Format.Decimal(position.SizeShares)};position_average={Format.Decimal(position.AveragePrice)};" +
                $"settled_size={Format.Decimal(settlement.SettledSizeShares)};settlement_cost={Format.Decimal(settlement.CostBasisUsd)};" +
                $"settlement_value={Format.Decimal(settlement.SettlementValueUsd)};settlement_pnl={Format.Decimal(settlement.RealizedPnlUsd)};" +
                $"run_settlement={Format.Decimal(runSettlementValue)};run_pnl={Format.Decimal(runPnl)}");
    }

    private static PositionSemanticValidation Invalid(string reason, string details) =>
        new(false, reason, details);

    private static decimal RoundScale8(decimal value) =>
        decimal.Round(value, 8, MidpointRounding.AwayFromZero);
}

internal static class RemovalStakeEvidenceParser
{
    public static RemovalStakeEvidence Parse(GraphOrder order, string? projectedRawDecisionJson)
    {
        if (string.IsNullOrWhiteSpace(projectedRawDecisionJson))
        {
            throw new InvalidDataException("removal_stake_proof_missing");
        }

        using var document = JsonDocument.Parse(projectedRawDecisionJson);
        var root = document.RootElement;
        var baseStake = ReadPositiveDecimal(root, "paper_lost_base_stake_usd");
        var effectiveStake = ReadPositiveDecimal(root, "paper_lost_effective_stake_usd");
        var addStake = ReadNonNegativeDecimal(root, "paper_lost_add_stake_usd");
        var counterCoeff = ReadInt(root, "paper_lost_counter_coeff");
        var stakeMultiplier = ReadPositiveDecimal(root, "stake_multiplier");
        var targetNotional = ReadPositiveDecimal(root, "target_notional_usd");
        var targetSize = ReadPositiveDecimal(root, "target_size_shares");
        var sizingSource = ReadNonEmptyString(root, "stake_sizing_source");
        var executionMode = ReadNonEmptyString(root, "order_execution_mode");

        if (!string.Equals(executionMode, "FAK", StringComparison.Ordinal) ||
            !string.Equals(
                order.ExecutionSource,
                CorrectionGraphInvariantValidator.MainFakExecutionSource,
                StringComparison.Ordinal) ||
            !string.Equals(order.OrderStatus, "Filled", StringComparison.Ordinal))
        {
            throw new InvalidDataException("removal_exact_fak_dispatch_provenance_invalid");
        }

        if (counterCoeff is < 0 or > 2 ||
            addStake != baseStake * counterCoeff ||
            effectiveStake != baseStake + addStake ||
            stakeMultiplier != effectiveStake ||
            RoundScale8(targetNotional) < RoundScale8(order.OrderNotionalUsd) ||
            RoundScale8(targetSize) < RoundScale8(order.OrderSizeShares))
        {
            throw new InvalidDataException("removal_stake_proof_arithmetic_invalid");
        }

        if (string.Equals(executionMode, "FAK", StringComparison.Ordinal))
        {
            var filledNotional = ReadPositiveDecimal(root, "paper_fak_filled_notional_usd");
            var filledSize = ReadPositiveDecimal(root, "paper_fak_filled_size_shares");
            var averageFillPrice = ReadPositiveDecimal(root, "paper_fak_average_fill_price");
            var partial = ReadBoolean(root, "paper_fak_partial_fill");
            if (RoundScale8(filledNotional) != RoundScale8(order.OrderNotionalUsd) ||
                RoundScale8(filledSize) != RoundScale8(order.OrderSizeShares) ||
                RoundScale8(averageFillPrice) != RoundScale8(order.OrderPrice) ||
                partial != (RoundScale8(filledNotional) < RoundScale8(targetNotional)))
            {
                throw new InvalidDataException("removal_fak_stake_fill_proof_invalid");
            }
        }

        var bytes = Encoding.UTF8.GetBytes(projectedRawDecisionJson);
        return new RemovalStakeEvidence(
            baseStake,
            effectiveStake,
            targetNotional,
            sizingSource,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
    }

    private static decimal ReadPositiveDecimal(JsonElement root, string property)
    {
        var value = ReadDecimal(root, property);
        return value > 0m
            ? value
            : throw new InvalidDataException($"removal_{property}_not_positive");
    }

    private static decimal ReadNonNegativeDecimal(JsonElement root, string property)
    {
        var value = ReadDecimal(root, property);
        return value >= 0m
            ? value
            : throw new InvalidDataException($"removal_{property}_negative");
    }

    private static decimal ReadDecimal(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) &&
        node.ValueKind == JsonValueKind.Number &&
        node.TryGetDecimal(out var value)
            ? value
            : throw new InvalidDataException($"removal_{property}_missing_or_invalid");

    private static int ReadInt(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) &&
        node.ValueKind == JsonValueKind.Number &&
        node.TryGetInt32(out var value)
            ? value
            : throw new InvalidDataException($"removal_{property}_missing_or_invalid");

    private static bool ReadBoolean(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) &&
        node.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? node.GetBoolean()
            : throw new InvalidDataException($"removal_{property}_missing_or_invalid");

    private static string ReadNonEmptyString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) &&
        node.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(node.GetString())
            ? node.GetString()!
            : throw new InvalidDataException($"removal_{property}_missing_or_invalid");

    private static decimal RoundScale8(decimal value) =>
        decimal.Round(value, 8, MidpointRounding.AwayFromZero);
}

internal static class ModeledAddPayloadBuilder
{
    public const string IdNamespace = "02e29185-5f14-5f40-b5f7-8c584e8b22e8";
    public const string IdNameFormat =
        "reference-average-history-correction-v2/{graph_manifest_sha256}/{run_id:D}/{entity_kind}";
    public const string SettlementSource = "ReferenceAverageHistoryCorrectionV2Modeled";
    public const string FillEvidence =
        "ReferenceAverageHistoryCorrectionV2 modeled FAK fill under user-authorized assumed liquidity; " +
        "no historical order-book snapshot was asserted.";

    public static ModeledAddPayloadEvidence Build(
        SignalPreviewRow input,
        AddSourceRow run,
        string selectedTokenId,
        string winningOutcome,
        string winningTokenId,
        decimal fillPrice,
        decimal requestedNotionalUsd,
        decimal worstPriceTargetSizeShares,
        decimal filledSizeShares,
        decimal settlementPrice,
        decimal settlementValueUsd,
        decimal realizedPnlUsd,
        bool won,
        DateTimeOffset modeledEntryAtUtc,
        DateTimeOffset modeledSettledAtUtc,
        string modeledSettlementTimestampSource)
    {
        if (string.IsNullOrWhiteSpace(run.Category))
        {
            throw new InvalidDataException("add_source_run_category_missing_for_modeled_settlement");
        }

        var rawDecisionJson = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            pricing_mode = "history_correction_modeled_fak",
            order_execution_mode = "FAK",
            post_only = false,
            decision_source = "reference_average_history_correction_v2_modeled_add",
            algorithm_decision_source = "reference_price_average_envelope_bps_premarket_v2",
            correction_provenance = "pinned_replay_classifier_plus_user_authorized_liquidity_assumption",
            historical_orderbook_snapshot_asserted = false,
            liquidity_assumption = fillPrice switch
            {
                CorrectionContract.LowerEnterFillPrice => "sufficient_depth_for_full_fill_at_0.50",
                CorrectionContract.RegularFillPrice => "sufficient_depth_for_full_fill_at_0.52",
                _ => throw new InvalidDataException("modeled_add_fill_price_outside_user_authorized_premise")
            },
            source_skipped_run_id = run.RunId,
            source_run_full_row_sha256 = run.RunFullRowSha256,
            signal_preview_manifest_sha256 = CorrectionContract.RequiredInputManifestSha256,
            replay_classifier_sha256 = CorrectionContract.RequiredInputReplayClassifierSha256,
            replay_evidence_json = input.ReplayEvidenceJson,
            replay_evidence_sha256 = input.ReplayEvidenceSha256,
            strategy_code = input.StrategyCode,
            market_id = input.MarketId,
            condition_id = run.ConditionId,
            market_slug = run.MarketSlug,
            asset_id = selectedTokenId,
            outcome = "Up",
            assumed_fill_price = fillPrice,
            paper_fak_worst_price = CorrectionContract.FakWorstPrice,
            sizing_rounding = "ceil_usd_then_ceil_worst_price_shares_to_2_decimals",
            target_notional_usd = requestedNotionalUsd,
            target_size_shares = worstPriceTargetSizeShares,
            paper_fak_average_fill_price = fillPrice,
            paper_fak_filled_size_shares = filledSizeShares,
            paper_fak_filled_notional_usd = requestedNotionalUsd,
            paper_fak_partial_fill = false,
            modeled_entry_at_utc = modeledEntryAtUtc,
            modeled_settled_at_utc = modeledSettledAtUtc,
            modeled_settlement_timestamp_source = modeledSettlementTimestampSource
        });
        var rawDecisionSha256 = CanonicalEvidence.HashUtf8Text(rawDecisionJson);
        var idPolicy = new
        {
            algorithm = "UUIDv5/RFC4122-SHA1",
            namespace_id = IdNamespace,
            name_format = IdNameFormat,
            graph_manifest_sha256_is_bound_after_manifest_creation = true,
            entity_kinds = new[]
            {
                "signal", "paper_order", "paper_fill", "paper_position", "paper_position_settlement"
            }
        };
        var payloadJson = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            operation = "reference_average_history_correction_v2_modeled_add",
            deterministic_ids = idPolicy,
            source = new
            {
                run_id = run.RunId,
                run_full_row_sha256 = run.RunFullRowSha256,
                entry_timestamp_source = "exact_source_skipped_run_updated_at_utc",
                modeled_settlement_timestamp_source = modeledSettlementTimestampSource,
                historical_orderbook_snapshot_asserted = false
            },
            signal = new
            {
                id_kind = "signal",
                leader_trade_id = (Guid?)null,
                trader_wallet = "strategy:" + input.StrategyCode,
                condition_id = run.ConditionId,
                asset_id = selectedTokenId,
                outcome = "Up",
                leader_price = fillPrice,
                best_bid = (decimal?)null,
                best_ask = (decimal?)null,
                spread_abs = (decimal?)null,
                spread_pct = (decimal?)null,
                lag_seconds = (int?)null,
                score = 100,
                decision = input.StrategyCode + "_entry",
                accepted = true,
                proposed_paper_price = fillPrice,
                proposed_size_shares = filledSizeShares,
                proposed_notional_usd = requestedNotionalUsd,
                created_at_utc = modeledEntryAtUtc,
                raw_context_json = (string?)null
            },
            paper_order = new
            {
                id_kind = "paper_order",
                signal_id_kind = "signal",
                strategy_id = input.StrategyId,
                copied_trader_wallet = "strategy:" + input.StrategyCode,
                status = "Filled",
                side = "Buy",
                asset_id = selectedTokenId,
                condition_id = run.ConditionId,
                outcome = "Up",
                price = fillPrice,
                size_shares = filledSizeShares,
                notional_usd = requestedNotionalUsd,
                created_at_utc = modeledEntryAtUtc,
                expires_at_utc = modeledEntryAtUtc,
                filled_at_utc = modeledEntryAtUtc,
                cancelled_at_utc = (DateTimeOffset?)null,
                raw_decision_json = rawDecisionJson,
                raw_decision_sha256 = rawDecisionSha256,
                correlation_id = (Guid?)null,
                execution_source = CorrectionGraphInvariantValidator.MainFakExecutionSource
            },
            paper_fill = new
            {
                id_kind = "paper_fill",
                paper_order_id_kind = "paper_order",
                price = fillPrice,
                size_shares = filledSizeShares,
                filled_at_utc = modeledEntryAtUtc,
                evidence = FillEvidence,
                realized_pnl_usd = 0m
            },
            strategy_market_paper_run_update = new
            {
                id = run.RunId,
                status = "Settled",
                selected_asset_id = selectedTokenId,
                selected_outcome = "Up",
                entry_price = fillPrice,
                stake_usd = requestedNotionalUsd,
                size_shares = filledSizeShares,
                signal_id_kind = "signal",
                paper_order_id_kind = "paper_order",
                entered_at_utc = modeledEntryAtUtc,
                settlement_price = settlementPrice,
                settlement_value_usd = settlementValueUsd,
                realized_pnl_usd = realizedPnlUsd,
                settled_at_utc = modeledSettledAtUtc,
                skip_reason = (string?)null,
                skip_diagnostics_json = (string?)null,
                updated_at_utc = modeledSettledAtUtc,
                preserve_unlisted_source_columns = true
            },
            paper_position = new
            {
                id_kind = "paper_position",
                copied_trader_wallet = "strategy:" + input.StrategyCode,
                asset_id = selectedTokenId,
                condition_id = run.ConditionId,
                outcome = "Up",
                size_shares = 0m,
                average_price = 0m,
                estimated_value_usd = 0m,
                unrealized_pnl_usd = 0m,
                updated_at_utc = modeledSettledAtUtc
            },
            paper_position_settlement = new
            {
                id_kind = "paper_position_settlement",
                copied_trader_wallet = "strategy:" + input.StrategyCode,
                asset_id = selectedTokenId,
                condition_id = run.ConditionId,
                outcome = "Up",
                winning_asset_id = winningTokenId,
                winning_outcome = winningOutcome,
                category = run.Category,
                settled_size_shares = filledSizeShares,
                average_price = fillPrice,
                cost_basis_usd = requestedNotionalUsd,
                settlement_value_usd = settlementValueUsd,
                realized_pnl_usd = realizedPnlUsd,
                won,
                settlement_source = SettlementSource,
                settled_at_utc = modeledSettledAtUtc,
                created_at_utc = modeledSettledAtUtc
            }
        });
        return new ModeledAddPayloadEvidence(
            rawDecisionJson,
            rawDecisionSha256,
            FillEvidence,
            payloadJson,
            CanonicalEvidence.HashUtf8Text(payloadJson));
    }
}

internal static class AddFeasibilityCalculator
{
    private const string GammaResolutionSource = "official_gamma_api_live_market_by_id";
    private const string GammaProvenanceGroup = "PolymarketOfficialGammaApiLive";
    private const string RawWebSocketResolutionSource =
        "market_resolved_event_diagnostics:RecordedCryptoUpDown5mResult";
    private const string RawWebSocketProvenanceGroup = "PolymarketMarketDataWebSocket";
    private const string ArchivedTickSource = "archived_odds_ticks:BinanceStartEnd";
    private const string ArchivedTickProvenanceGroup = "BinanceArchivedReferenceTicks";

    public static decimal ReadHistoricalStakeMultiplier(string skipDiagnosticsJson)
    {
        using var document = JsonDocument.Parse(skipDiagnosticsJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("target_notional_usd", out var node) ||
            node.ValueKind != JsonValueKind.Number ||
            !node.TryGetDecimal(out var value) ||
            value <= 0m)
        {
            throw new InvalidDataException("skip_json_target_notional_usd_missing_or_invalid");
        }

        return value;
    }

    public static void ValidateHistoricalStakeSemantics(decimal savedRunStakeUsd, string? skipDiagnosticsJson)
    {
        if (savedRunStakeUsd <= 0m || string.IsNullOrWhiteSpace(skipDiagnosticsJson))
        {
            throw new InvalidDataException("saved_run_stake_or_skip_diagnostics_missing");
        }

        using var document = JsonDocument.Parse(skipDiagnosticsJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("decision_source", out var source) ||
            source.ValueKind != JsonValueKind.String ||
            !string.Equals(
                source.GetString(),
                CorrectionContract.LegacyReferenceDecisionSource,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("add_legacy_reference_decision_provenance_missing");
        }

        var effectiveStake = ReadHistoricalStakeMultiplier(skipDiagnosticsJson);
        var hasLostCounterBase = TryReadPositiveDecimal(root, "paper_lost_base_stake_usd", out var baseStake);
        var hasLostCounterEffective = TryReadPositiveDecimal(root, "paper_lost_effective_stake_usd", out var savedEffective);
        if (hasLostCounterBase != hasLostCounterEffective ||
            hasLostCounterBase && (baseStake != savedRunStakeUsd || savedEffective != effectiveStake) ||
            !hasLostCounterBase && effectiveStake != savedRunStakeUsd)
        {
            throw new InvalidDataException("saved_run_stake_disagrees_with_historical_stake_diagnostics");
        }
    }

    public static (string SelectedTokenId, IReadOnlyDictionary<string, string> OutcomeTokens) MapOutcomeTokens(
        GammaMarketEvidence gamma,
        string selectedOutcome)
    {
        var outcomes = ReadStringArray(gamma.OutcomesJson, "gamma_outcomes_json");
        var tokenIds = ReadStringArray(gamma.TokenIdsJson, "gamma_clob_token_ids_json");
        if (outcomes.Count != 2 || tokenIds.Count != 2 ||
            outcomes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != outcomes.Count ||
            tokenIds.Distinct(StringComparer.Ordinal).Count() != tokenIds.Count ||
            !outcomes.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["Up", "Down"]))
        {
            throw new InvalidDataException("gamma_outcome_token_mapping_is_not_exact_binary_up_down");
        }

        var map = outcomes
            .Select((outcome, index) => (outcome, tokenIds[index]))
            .ToDictionary(pair => pair.outcome, pair => pair.Item2, StringComparer.OrdinalIgnoreCase);
        if (!map.TryGetValue(selectedOutcome, out var selectedTokenId) || string.IsNullOrWhiteSpace(selectedTokenId))
        {
            throw new InvalidDataException("gamma_selected_outcome_token_missing");
        }

        return (selectedTokenId, map);
    }

    public static AddFeasibility Calculate(
        SignalPreviewRow input,
        AddSourceRow run,
        GammaMarketEvidence gamma,
        LiveGammaResolutionEvidence liveGamma,
        ValidatedMarketResolvedDiagnostics rawWebSocket,
        ArchivedReferenceResolution archivedTicks,
        ResolvedMarketLedgerEvidence ledger)
    {
        if (input.AssumedFillPrice is not { } fillPrice ||
            fillPrice is <= 0m or >= 1m ||
            string.IsNullOrWhiteSpace(run.SkipDiagnosticsJson) ||
            gamma.OrderMinSize is not > 0m)
        {
            throw new InvalidDataException("add_required_input_missing");
        }

        var historicalMultiplier = ReadHistoricalStakeMultiplier(run.SkipDiagnosticsJson);
        var mapping = MapOutcomeTokens(gamma, "Up");
        var liveMapping = MapOutcomeTokens(
            new GammaMarketEvidence(
                liveGamma.MarketId,
                liveGamma.ConditionId,
                liveGamma.OrderMinSize,
                liveGamma.OutcomesJson,
                liveGamma.TokenIdsJson),
            "Up");
        if (!string.Equals(liveGamma.MarketId, run.MarketId, StringComparison.Ordinal) ||
            !string.Equals(liveGamma.ConditionId, run.ConditionId, StringComparison.Ordinal) ||
            !string.Equals(liveGamma.MarketSlug, run.MarketSlug, StringComparison.Ordinal) ||
            !liveGamma.Closed ||
            liveGamma.OrderMinSize is { } liveMinSize && liveMinSize != gamma.OrderMinSize ||
            !string.Equals(liveMapping.SelectedTokenId, mapping.SelectedTokenId, StringComparison.Ordinal) ||
            mapping.OutcomeTokens.Any(pair =>
                !liveMapping.OutcomeTokens.TryGetValue(pair.Key, out var liveTokenId) ||
                !string.Equals(pair.Value, liveTokenId, StringComparison.Ordinal)) ||
            !string.Equals(archivedTicks.AssetSymbol, input.Asset, StringComparison.Ordinal) ||
            !string.Equals(archivedTicks.MarketId, run.MarketId, StringComparison.Ordinal) ||
            !string.Equals(archivedTicks.ConditionId, run.ConditionId, StringComparison.Ordinal) ||
            !string.Equals(rawWebSocket.MarketId, run.MarketId, StringComparison.Ordinal) ||
            !string.Equals(rawWebSocket.ConditionId, run.ConditionId, StringComparison.Ordinal) ||
            rawWebSocket.DiagnosticRowCount > 0 &&
            (!string.Equals(rawWebSocket.WinningOutcome, liveGamma.WinningOutcome, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(rawWebSocket.WinningTokenId, liveGamma.WinningTokenId, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("resolution_sources_or_selected_token_do_not_agree");
        }

        if (!mapping.OutcomeTokens.TryGetValue(liveGamma.WinningOutcome, out var winningTokenId) ||
            !string.Equals(liveGamma.WinningTokenId, winningTokenId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("resolved_winning_outcome_not_in_gamma_mapping");
        }

        var ledgerProvenance = ValidateResolvedLedger(
            input,
            run,
            liveGamma,
            mapping.OutcomeTokens,
            ledger);
        var ledgerWinningAssetAgrees = !string.IsNullOrWhiteSpace(ledger.WinningAssetId) &&
            string.Equals(ledger.WinningAssetId, winningTokenId, StringComparison.Ordinal);
        var archivedTicksAgree = string.Equals(
            liveGamma.WinningOutcome,
            archivedTicks.WinningOutcome,
            StringComparison.OrdinalIgnoreCase);
        var independentGroups = new HashSet<string>(StringComparer.Ordinal)
        {
            GammaProvenanceGroup,
            ledgerProvenance.ProvenanceGroup
        };
        if (!string.IsNullOrWhiteSpace(rawWebSocket.ProvenanceGroup))
        {
            independentGroups.Add(rawWebSocket.ProvenanceGroup);
        }
        if (archivedTicksAgree)
        {
            independentGroups.Add(ArchivedTickProvenanceGroup);
        }
        if (string.Equals(ledger.Source, "BinanceTimedClose", StringComparison.Ordinal) &&
            !archivedTicksAgree)
        {
            throw new InvalidDataException("binance_timed_close_ledger_disagrees_with_exact_archived_tick_replay");
        }
        if (independentGroups.Count < 2)
        {
            throw new InvalidDataException("resolution_sources_are_not_independent");
        }

        var rawWorstPriceNotional = gamma.OrderMinSize.Value *
            CorrectionContract.FakWorstPrice *
            CorrectionContract.MinimumStakeSafetyMultiplier *
            historicalMultiplier;
        var roundedWorstPriceNotional = decimal.Ceiling(rawWorstPriceNotional);
        var worstPriceTargetSize = RoundUp(roundedWorstPriceNotional / CorrectionContract.FakWorstPrice, 2);
        var requestedNotional = RoundScale8(worstPriceTargetSize * CorrectionContract.FakWorstPrice);
        var filledSize = RoundScale8(requestedNotional / fillPrice);
        var won = string.Equals(ledger.WinningOutcome, "Up", StringComparison.OrdinalIgnoreCase);
        var settlementPrice = won ? 1m : 0m;
        var settlementValue = won ? filledSize : 0m;
        var pnl = RoundScale8(settlementValue - requestedNotional);
        var modeledEntryAtUtc = run.UpdatedAtUtc;
        var modeledSettledAtUtc = run.MarketEndUtc > ledger.FirstReceivedAtUtc
            ? run.MarketEndUtc.Value
            : ledger.FirstReceivedAtUtc;
        if (modeledEntryAtUtc > modeledSettledAtUtc)
        {
            throw new InvalidDataException("modeled_entry_timestamp_is_after_modeled_settlement_timestamp");
        }
        const string modeledSettlementTimestampSource =
            "modeled_earliest_observable_resolution=max(market_end_utc,resolution_ledger_first_received_at_utc)";
        var modeledPayload = ModeledAddPayloadBuilder.Build(
            input,
            run,
            mapping.SelectedTokenId,
            ledger.WinningOutcome,
            winningTokenId,
            fillPrice,
            requestedNotional,
            worstPriceTargetSize,
            filledSize,
            settlementPrice,
            settlementValue,
            pnl,
            won,
            modeledEntryAtUtc,
            modeledSettledAtUtc,
            modeledSettlementTimestampSource);
        return new AddFeasibility(
            input.RunId,
            input.StrategyId,
            input.StrategyCode,
            input.MarketId,
            run.ConditionId,
            input.Asset,
            input.Kind,
            CanonicalEvidence.HashAddSource(run),
            run.RunFullRowSha256,
            modeledEntryAtUtc,
            modeledSettledAtUtc,
            modeledSettlementTimestampSource,
            run.Category!,
            modeledPayload.RawDecisionJson,
            modeledPayload.RawDecisionSha256,
            modeledPayload.FillEvidence,
            modeledPayload.PayloadJson,
            modeledPayload.PayloadSha256,
            fillPrice,
            historicalMultiplier,
            gamma.OrderMinSize.Value,
            "Up",
            mapping.SelectedTokenId,
            ledger.WinningOutcome,
            winningTokenId,
            ledgerProvenance.Source,
            ledgerProvenance.ProvenanceGroup,
            ledger.WinningAssetId ?? string.Empty,
            ledgerWinningAssetAgrees,
            ledger.RawEventType,
            ledger.RawSha256,
            ledger.RawBytes,
            ledger.EventTimestampUtc,
            ledgerProvenance.RawEventTimestampUtc,
            ledger.FirstReceivedAtUtc,
            ledger.LastReceivedAtUtc,
            ledgerProvenance.RawValidated,
            rawWebSocket.Source,
            rawWebSocket.ProvenanceGroup,
            rawWebSocket.DiagnosticRowCount,
            rawWebSocket.DistinctRawEventCount,
            archivedTicks.Source,
            archivedTicks.ProvenanceGroup,
            archivedTicks.SampleCount,
            archivedTicks.StartPriceUsd,
            archivedTicks.EndPriceUsd,
            archivedTicks.EndAgeMilliseconds,
            archivedTicks.WinningOutcome,
            archivedTicksAgree,
            GammaResolutionSource,
            GammaProvenanceGroup,
            liveGamma.RequestUrl,
            liveGamma.RawSha256,
            liveGamma.RawBytes,
            liveGamma.FetchedAtUtc,
            liveGamma.ResolutionSource ?? string.Empty,
            liveGamma.OrderMinSize,
            independentGroups.Count,
            rawWorstPriceNotional,
            roundedWorstPriceNotional,
            worstPriceTargetSize,
            requestedNotional,
            filledSize,
            won,
            settlementPrice,
            settlementValue,
            pnl,
            true,
            archivedTicksAgree
                ? "exact_signal_and_independent_authoritative_resolution_plus_agreeing_archived_binance;modeled_full_fill"
                : "exact_signal_and_independent_gamma_plus_market_resolution;archived_binance_disagrees_non_authoritatively;modeled_full_fill");
    }

    public static ValidatedMarketResolvedDiagnostics ValidateRawMarketResolvedDiagnostics(
        SignalPreviewRow input,
        AddSourceRow run,
        GammaMarketEvidence gamma,
        LiveGammaResolutionEvidence liveGamma,
        IReadOnlyList<MarketResolvedEventEvidence> diagnostics)
    {
        if (run.MarketEndUtc is null)
        {
            throw new InvalidDataException("raw_market_resolved_market_end_missing");
        }
        if (diagnostics.Count == 0)
        {
            return new ValidatedMarketResolvedDiagnostics(
                run.MarketId,
                run.ConditionId,
                string.Empty,
                string.Empty,
                0,
                0,
                "market_resolved_event_diagnostics:unavailable_no_matching_rows",
                string.Empty);
        }

        var historicalMapping = MapOutcomeTokens(gamma, liveGamma.WinningOutcome);
        var expectedTokens = historicalMapping.OutcomeTokens.Values.ToHashSet(StringComparer.Ordinal);
        var expectedStartUtc = run.MarketEndUtc.Value.AddMinutes(-5);
        foreach (var diagnostic in diagnostics)
        {
            if (string.IsNullOrWhiteSpace(diagnostic.Component) ||
                !string.Equals(diagnostic.RawEventType, "market_resolved", StringComparison.Ordinal) ||
                !expectedTokens.Contains(diagnostic.AssetId) ||
                !string.Equals(diagnostic.ConditionId, run.ConditionId, StringComparison.Ordinal) ||
                !string.Equals(diagnostic.WinningAssetId, liveGamma.WinningTokenId, StringComparison.Ordinal) ||
                IsRecognizedOutcomeConflict(diagnostic.WinningOutcome, liveGamma.WinningOutcome) ||
                !diagnostic.ActiveSnapshotFound ||
                !string.Equals(diagnostic.SnapshotMarketId, run.MarketId, StringComparison.Ordinal) ||
                !string.Equals(diagnostic.SnapshotConditionId, run.ConditionId, StringComparison.Ordinal) ||
                !string.Equals(diagnostic.SnapshotMarketSlug, run.MarketSlug, StringComparison.Ordinal) ||
                !string.Equals(diagnostic.SnapshotAssetSymbol, input.Asset, StringComparison.Ordinal) ||
                diagnostic.SnapshotMarketStartUtc != expectedStartUtc ||
                !diagnostic.SnapshotIsCryptoUpDown5m ||
                !string.Equals(
                    diagnostic.RecorderAction,
                    "RecordedCryptoUpDown5mResult",
                    StringComparison.Ordinal) ||
                diagnostic.EventTimestampUtc > diagnostic.ReceivedAtUtc ||
                diagnostic.CreatedAtUtc < diagnostic.ReceivedAtUtc)
            {
                throw new InvalidDataException(
                    "raw_market_resolved_diagnostic_identity_or_recorder_state_invalid:" + diagnostic.Id.ToString("D"));
            }

            ValidateRawMarketResolvedJson(diagnostic, run, liveGamma, expectedTokens);
        }

        return new ValidatedMarketResolvedDiagnostics(
            run.MarketId,
            run.ConditionId,
            liveGamma.WinningOutcome,
            liveGamma.WinningTokenId,
            diagnostics.Count,
            diagnostics.Select(item => item.RawSha256).Distinct(StringComparer.Ordinal).Count(),
            RawWebSocketResolutionSource,
            RawWebSocketProvenanceGroup);
    }

    public static ArchivedReferenceResolution ReplayArchivedReferenceTicks(
        string asset,
        string marketId,
        string conditionId,
        DateTimeOffset marketEndUtc,
        IReadOnlyList<ReferenceResolutionTick> samples)
    {
        var ordered = samples
            .OrderBy(item => item.SampledAtUtc)
            .ThenBy(item => item.CreatedAtUtc)
            .ToArray();
        if (ordered.Length < CorrectionContract.ResolutionMinimumSamples)
        {
            throw new InvalidDataException("archived_resolution_tick_sample_count_below_minimum");
        }
        if (ordered.Any(item =>
                !string.Equals(item.AssetSymbol, asset, StringComparison.Ordinal) ||
                !string.Equals(item.MarketId, marketId, StringComparison.Ordinal) ||
                !string.Equals(item.ConditionId, conditionId, StringComparison.Ordinal) ||
                item.MarketEndUtc != marketEndUtc ||
                item.BinancePriceUsd <= 0m ||
                item.BinanceStartPriceUsd <= 0m) ||
            ordered.Select(item => item.BinanceStartPriceUsd).Distinct().Count() != 1)
        {
            throw new InvalidDataException("archived_resolution_tick_identity_or_start_price_inconsistent");
        }

        var first = ordered[0];
        var end = ordered
            .Where(item => item.SampledAtUtc <= marketEndUtc.AddSeconds(1))
            .LastOrDefault() ?? throw new InvalidDataException("archived_resolution_close_tick_missing");
        var endAge = marketEndUtc - end.SampledAtUtc;
        if (endAge < TimeSpan.Zero)
        {
            endAge = TimeSpan.Zero;
        }
        var endAgeMilliseconds = decimal.Round(
            (decimal)endAge.TotalMilliseconds,
            3,
            MidpointRounding.AwayFromZero);
        if (endAgeMilliseconds > CorrectionContract.ResolutionMaximumEndAgeMilliseconds)
        {
            throw new InvalidDataException("archived_resolution_close_tick_stale");
        }
        var comparison = end.BinancePriceUsd.CompareTo(first.BinanceStartPriceUsd);
        if (comparison == 0)
        {
            throw new InvalidDataException("archived_resolution_price_tie");
        }

        return new ArchivedReferenceResolution(
            asset,
            marketId,
            conditionId,
            ordered.Length,
            first.BinanceStartPriceUsd,
            end.BinancePriceUsd,
            first.SampledAtUtc,
            end.SampledAtUtc,
            end.BinanceSourceUpdatedAtUtc,
            endAgeMilliseconds,
            comparison > 0 ? "Up" : "Down",
            ArchivedTickSource,
            ArchivedTickProvenanceGroup);
    }

    private static ResolutionProvenance ValidateResolvedLedger(
        SignalPreviewRow input,
        AddSourceRow run,
        LiveGammaResolutionEvidence liveGamma,
        IReadOnlyDictionary<string, string> outcomeTokens,
        ResolvedMarketLedgerEvidence ledger)
    {
        if (run.MarketEndUtc is null ||
            !string.Equals(ledger.AssetSymbol, input.Asset, StringComparison.Ordinal) ||
            !string.Equals(ledger.MarketId, run.MarketId, StringComparison.Ordinal) ||
            !string.Equals(ledger.ConditionId, run.ConditionId, StringComparison.Ordinal) ||
            !string.Equals(ledger.MarketSlug, run.MarketSlug, StringComparison.Ordinal) ||
            ledger.MarketStartUtc != run.MarketEndUtc.Value.AddMinutes(-5) ||
            ledger.MarketEndUtc != run.MarketEndUtc.Value ||
            !string.Equals(ledger.WinningOutcome, liveGamma.WinningOutcome, StringComparison.OrdinalIgnoreCase) ||
            ledger.EventCount < 1 ||
            ledger.FirstReceivedAtUtc > ledger.LastReceivedAtUtc ||
            ledger.EventTimestampUtc < ledger.MarketEndUtc ||
            ledger.EventTimestampUtc > ledger.LastReceivedAtUtc ||
            ledger.CreatedAtUtc > ledger.UpdatedAtUtc ||
            ledger.RawBytes <= 0 ||
            string.IsNullOrWhiteSpace(ledger.RawJson))
        {
            throw new InvalidDataException("resolved_market_ledger_identity_timing_or_outcome_invalid");
        }

        var expectedDelaySeconds = decimal.Round(
            Math.Max(0m, (decimal)(ledger.FirstReceivedAtUtc - ledger.MarketEndUtc).TotalSeconds),
            3,
            MidpointRounding.AwayFromZero);
        var canonicalBytes = Encoding.UTF8.GetBytes(ledger.RawJson);
        if (ledger.ResultDelaySeconds != expectedDelaySeconds ||
            canonicalBytes.LongLength != ledger.RawBytes ||
            !string.Equals(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(canonicalBytes)),
                ledger.RawSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("resolved_market_ledger_delay_or_raw_hash_invalid");
        }

        var normalized = ledger.Source.Trim();
        return normalized switch
        {
            "MarketWebSocket" => ValidateMarketWebSocketLedgerRaw(
                run,
                liveGamma,
                outcomeTokens,
                ledger),
            "BinanceTimedClose" => new ResolutionProvenance(
                "crypto_up_down_5m_websocket_resolved_markets:BinanceTimedClose",
                ArchivedTickProvenanceGroup,
                RawValidated: false,
                RawEventTimestampUtc: null),
            _ => throw new InvalidDataException("resolution_ledger_source_unknown:" + normalized)
        };
    }

    private static ResolutionProvenance ValidateMarketWebSocketLedgerRaw(
        AddSourceRow run,
        LiveGammaResolutionEvidence liveGamma,
        IReadOnlyDictionary<string, string> outcomeTokens,
        ResolvedMarketLedgerEvidence ledger)
    {
        if (!string.Equals(ledger.RawEventType, "market_resolved", StringComparison.Ordinal))
        {
            throw new InvalidDataException("market_websocket_ledger_raw_event_type_invalid");
        }

        using var document = JsonDocument.Parse(ledger.RawJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !TryReadExactString(root, "event_type", out var eventType) ||
            !string.Equals(eventType, "market_resolved", StringComparison.Ordinal) ||
            !TryReadScalarString(root, "id", out var marketId) ||
            !string.Equals(marketId, run.MarketId, StringComparison.Ordinal) ||
            !TryReadExactString(root, "market", out var conditionId) ||
            !string.Equals(conditionId, run.ConditionId, StringComparison.Ordinal) ||
            !TryReadExactString(root, "winning_asset_id", out var winningAssetId) ||
            !string.Equals(winningAssetId, liveGamma.WinningTokenId, StringComparison.Ordinal) ||
            !TryReadExactString(root, "winning_outcome", out var winningOutcome) ||
            !string.Equals(winningOutcome, liveGamma.WinningOutcome, StringComparison.OrdinalIgnoreCase) ||
            !TryReadUnixMilliseconds(root, "timestamp", out var eventTimestampUtc) ||
            eventTimestampUtc < ledger.MarketEndUtc ||
            eventTimestampUtc > ledger.LastReceivedAtUtc ||
            eventTimestampUtc > ledger.UpdatedAtUtc)
        {
            throw new InvalidDataException("market_websocket_ledger_raw_identity_winner_or_timestamp_invalid");
        }

        using var encodedAssets = ReadOptionalEncodedArray(root, "assets_ids");
        if (!root.TryGetProperty("assets_ids", out var assetsNode))
        {
            throw new InvalidDataException("market_websocket_ledger_raw_token_set_missing");
        }
        var assetsArray = encodedAssets?.RootElement ?? assetsNode;
        if (assetsArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("market_websocket_ledger_raw_token_set_invalid");
        }
        var rawTokens = assetsArray.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
            .ToArray();
        var expectedTokens = outcomeTokens.Values.ToHashSet(StringComparer.Ordinal);
        if (rawTokens.Any(string.IsNullOrWhiteSpace) ||
            rawTokens.Length != 2 ||
            rawTokens.Select(item => item!).ToHashSet(StringComparer.Ordinal).Count != 2 ||
            !rawTokens.Select(item => item!).ToHashSet(StringComparer.Ordinal).SetEquals(expectedTokens))
        {
            throw new InvalidDataException("market_websocket_ledger_raw_token_bijection_invalid");
        }

        return new ResolutionProvenance(
            "crypto_up_down_5m_websocket_resolved_markets:MarketWebSocket:validated_raw_json",
            "PolymarketMarketDataWebSocket",
            RawValidated: true,
            RawEventTimestampUtc: eventTimestampUtc);
    }

    private static IReadOnlyList<string> ReadStringArray(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(name + "_not_array");
        }

        var result = new List<string>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
            {
                throw new InvalidDataException(name + "_contains_non_string");
            }

            result.Add(element.GetString()!);
        }

        return result;
    }

    private static void ValidateRawMarketResolvedJson(
        MarketResolvedEventEvidence diagnostic,
        AddSourceRow run,
        LiveGammaResolutionEvidence liveGamma,
        IReadOnlySet<string> expectedTokens)
    {
        using var document = JsonDocument.Parse(diagnostic.RawJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !TryReadExactString(root, "event_type", out var eventType) ||
            !string.Equals(eventType, "market_resolved", StringComparison.Ordinal) ||
            !TryReadExactString(root, "id", out var marketId) ||
            !string.Equals(marketId, run.MarketId, StringComparison.Ordinal) ||
            !TryReadExactString(root, "market", out var conditionId) ||
            !string.Equals(conditionId, run.ConditionId, StringComparison.Ordinal) ||
            !TryReadExactString(root, "winning_asset_id", out var winningAssetId) ||
            !string.Equals(winningAssetId, liveGamma.WinningTokenId, StringComparison.Ordinal) ||
            !TryReadUnixMilliseconds(root, "timestamp", out var eventTimestampUtc) ||
            eventTimestampUtc != diagnostic.EventTimestampUtc)
        {
            throw new InvalidDataException(
                "raw_market_resolved_json_identity_or_winner_invalid:" + diagnostic.Id.ToString("D"));
        }

        var rawWinningOutcome = TryReadExactString(root, "winning_outcome", out var outcome)
            ? outcome
            : string.Empty;
        if (IsRecognizedOutcomeConflict(rawWinningOutcome, liveGamma.WinningOutcome))
        {
            throw new InvalidDataException(
                "raw_market_resolved_json_outcome_conflicts_with_winning_token:" + diagnostic.Id.ToString("D"));
        }

        using var encodedAssets = ReadOptionalEncodedArray(root, "assets_ids");
        if (!root.TryGetProperty("assets_ids", out var assetsNode))
        {
            throw new InvalidDataException(
                "raw_market_resolved_json_token_bijection_invalid:" + diagnostic.Id.ToString("D"));
        }
        var assetsArray = encodedAssets?.RootElement ?? assetsNode;
        if (assetsArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "raw_market_resolved_json_token_bijection_invalid:" + diagnostic.Id.ToString("D"));
        }
        var rawTokens = assetsArray.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
            .ToArray();
        if (rawTokens.Any(string.IsNullOrWhiteSpace) ||
            rawTokens.Length != 2 ||
            rawTokens.Select(item => item!).ToHashSet(StringComparer.Ordinal).Count != 2 ||
            !rawTokens.Select(item => item!).ToHashSet(StringComparer.Ordinal).SetEquals(expectedTokens))
        {
            throw new InvalidDataException(
                "raw_market_resolved_json_token_bijection_invalid:" + diagnostic.Id.ToString("D"));
        }
    }

    private static bool TryReadExactString(JsonElement root, string property, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        value = node.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadScalarString(JsonElement root, string property, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(property, out var node))
        {
            return false;
        }
        value = node.ValueKind switch
        {
            JsonValueKind.String => node.GetString() ?? string.Empty,
            JsonValueKind.Number => node.GetRawText(),
            _ => string.Empty
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private static JsonDocument? ReadOptionalEncodedArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        try
        {
            return JsonDocument.Parse(node.GetString() ?? string.Empty);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("raw_market_resolved_json_assets_ids_invalid", exception);
        }
    }

    private static bool IsRecognizedOutcomeConflict(string? rawOutcome, string expectedOutcome)
    {
        if (!string.Equals(rawOutcome, "Up", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rawOutcome, "Down", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return !string.Equals(rawOutcome, expectedOutcome, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadUnixMilliseconds(
        JsonElement root,
        string property,
        out DateTimeOffset timestampUtc)
    {
        timestampUtc = default;
        if (!root.TryGetProperty(property, out var node))
        {
            return false;
        }

        long milliseconds;
        if (node.ValueKind == JsonValueKind.String)
        {
            if (!long.TryParse(node.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out milliseconds))
            {
                return false;
            }
        }
        else if (node.ValueKind == JsonValueKind.Number && node.TryGetInt64(out var numeric))
        {
            milliseconds = numeric;
        }
        else
        {
            return false;
        }

        try
        {
            timestampUtc = milliseconds > 99_999_999_999L
                ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                : DateTimeOffset.FromUnixTimeSeconds(milliseconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryReadPositiveDecimal(JsonElement root, string property, out decimal value)
    {
        value = 0m;
        return root.TryGetProperty(property, out var node) &&
            node.ValueKind == JsonValueKind.Number &&
            node.TryGetDecimal(out value) &&
            value > 0m;
    }

    private static decimal RoundUp(decimal value, int decimals)
    {
        var factor = decimals switch
        {
            0 => 1m,
            1 => 10m,
            2 => 100m,
            _ => throw new ArgumentOutOfRangeException(nameof(decimals))
        };
        return decimal.Ceiling(value * factor) / factor;
    }

    private static decimal RoundScale8(decimal value) =>
        decimal.Round(value, 8, MidpointRounding.AwayFromZero);

    private sealed record ResolutionProvenance(
        string Source,
        string ProvenanceGroup,
        bool RawValidated,
        DateTimeOffset? RawEventTimestampUtc);
}
