using System.Globalization;

namespace ReferenceAverageHistoryCorrectionGraphPreview;

internal static class OutputRows
{
    public static IReadOnlyList<string> MainRemovalHeader { get; } =
    [
        "run_id", "strategy_id", "strategy_code", "market_id", "paper_order_id", "signal_id",
        "asset_id", "outcome", "copied_trader_wallet", "fill_count", "fill_size_shares",
        "fill_notional_usd", "fill_realized_pnl_usd", "run_realized_pnl_usd", "settled_at_utc",
        "corrected_skip_reason", "corrected_skipped_updated_at_utc", "restored_base_stake_usd",
        "historical_effective_stake_usd", "historical_target_notional_usd",
        "historical_stake_sizing_source", "stake_sizing_proof_sha256", "classifier_action",
        "classifier_reason", "signal_preview_manifest_sha256", "replay_classifier_sha256",
        "replay_evidence_json", "replay_evidence_sha256", "graph_state_sha256", "fill_set_sha256"
    ];

    public static IReadOnlyList<string> MainRemoval(MainRemovalSummary item) =>
    [
        item.RunId.ToString("D"), item.StrategyId.ToString("D"), item.StrategyCode, item.MarketId,
        item.OrderId.ToString("D"), item.SignalId.ToString("D"), item.AssetId, item.Outcome,
        item.CopiedTraderWallet, item.FillCount.ToString(CultureInfo.InvariantCulture),
        Format.Decimal(item.FillSizeShares), Format.Decimal(item.FillNotionalUsd),
        Format.Decimal(item.FillRealizedPnlUsd), Format.Decimal(item.RunRealizedPnlUsd),
        Format.Timestamp(item.SettledAtUtc), item.CorrectedSkipReason,
        Format.Timestamp(item.CorrectedSkippedUpdatedAtUtc), Format.Decimal(item.RestoredBaseStakeUsd),
        Format.Decimal(item.HistoricalEffectiveStakeUsd), Format.Decimal(item.HistoricalTargetNotionalUsd),
        item.HistoricalStakeSizingSource, item.StakeSizingProofSha256, item.ClassifierAction,
        item.ClassifierReason, item.SignalPreviewManifestSha256, item.ReplayClassifierSha256,
        item.ReplayEvidenceJson, item.ReplayEvidenceSha256, item.GraphStateSha256, item.FillSetSha256
    ];

    public static IReadOnlyList<string> ReconciliationTargetHeader { get; } =
    [
        "target_id", "table_name", "key_scope", "method_id", "required_action", "reason",
        "blocks_mutation", "target_contract_sha256"
    ];

    public static IReadOnlyList<string> ReconciliationTargetRow(ReconciliationTarget item) =>
    [
        item.TargetId, item.TableName, item.KeyScope, item.MethodId, item.RequiredAction, item.Reason,
        Format.Bool(item.BlocksMutation), item.TargetContractSha256
    ];

    public static IReadOnlyList<string> OperationFootprintHeader { get; } =
    [
        "scope", "table_name", "operation", "selector", "selector_identity_count", "snapshot_row_count",
        "snapshot_pg_column_size_bytes", "planned_direct_row_operations", "exact_snapshot_measurement",
        "evidence"
    ];

    public static IReadOnlyList<string> OperationFootprintCsvRow(OperationFootprintRow item) =>
    [
        item.Scope, item.TableName, item.Operation, item.Selector,
        item.SelectorIdentityCount.ToString(CultureInfo.InvariantCulture),
        item.SnapshotRowCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        item.SnapshotPgColumnSizeBytes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        item.PlannedDirectRowOperations.ToString(CultureInfo.InvariantCulture),
        Format.Bool(item.ExactSnapshotMeasurement), item.Evidence
    ];

    public static IReadOnlyList<string> GraphOrderHeader { get; } =
    [
        "scope", "parent_main_run_id", "run_id", "strategy_id", "strategy_code", "strategy_name_proof",
        "market_id", "market_slug_proof", "condition_id", "run_category_proof", "entry_due_at_utc",
        "run_status", "run_outcome", "run_asset_id", "entry_price", "stake_usd", "run_size_shares",
        "settlement_price", "settlement_value_usd", "run_realized_pnl_usd", "settled_at_utc",
        "run_entered_at_utc_proof", "run_created_at_utc_proof", "run_updated_at_utc_proof",
        "market_end_utc_proof", "run_skip_reason_proof", "run_skip_diagnostics_is_null_proof",
        "paper_order_id", "signal_id", "order_status", "order_side", "order_outcome", "order_asset_id",
        "copied_trader_wallet", "order_price", "order_size_shares", "order_notional_usd", "correlation_id",
        "execution_source", "order_created_at_utc", "order_expires_at_utc", "order_filled_at_utc",
        "order_cancelled_at_utc", "run_signal_id_proof", "run_paper_order_id_proof", "order_strategy_id_proof",
        "signal_row_id_proof", "signal_outcome_proof", "signal_asset_id_proof", "signal_condition_id_proof",
        "signal_trader_wallet_proof", "signal_leader_price_proof", "signal_score_proof", "signal_accepted_proof",
        "signal_decision_proof", "signal_proposed_paper_price_proof", "signal_proposed_size_shares_proof",
        "signal_proposed_notional_usd_proof", "signal_created_at_utc_proof", "signal_leader_trade_id_proof",
        "signal_best_bid_proof", "signal_best_ask_proof", "signal_spread_abs_proof", "signal_spread_pct_proof",
        "signal_lag_seconds_proof", "signal_raw_context_json_proof", "signal_nullable_shape_valid_proof",
        "order_execution_mode_proof", "raw_decision_proof_sha256", "run_full_row_sha256",
        "order_full_row_sha256", "signal_full_row_sha256", "graph_state_sha256"
    ];

    public static IReadOnlyList<string> GraphOrder(GraphOrder item) =>
    [
        item.Scope, Format.Guid(item.ParentMainRunId), item.RunId.ToString("D"), item.StrategyId.ToString("D"),
        item.StrategyCode, item.StrategyNameProof, item.MarketId, item.MarketSlugProof, item.ConditionId,
        item.RunCategoryProof ?? string.Empty, Format.Timestamp(item.EntryDueAtUtc), item.RunStatus,
        item.RunOutcome ?? string.Empty, item.RunAssetId ?? string.Empty, Format.NullableDecimal(item.EntryPrice),
        Format.Decimal(item.StakeUsd), Format.NullableDecimal(item.RunSizeShares),
        Format.NullableDecimal(item.SettlementPrice), Format.NullableDecimal(item.SettlementValueUsd),
        Format.NullableDecimal(item.RunRealizedPnlUsd), Format.Timestamp(item.SettledAtUtc),
        Format.Timestamp(item.RunEnteredAtUtcProof), Format.Timestamp(item.RunCreatedAtUtcProof),
        Format.Timestamp(item.RunUpdatedAtUtcProof), Format.Timestamp(item.MarketEndUtcProof),
        item.RunSkipReasonProof ?? string.Empty, Format.Bool(item.RunSkipDiagnosticsIsNullProof),
        item.OrderId.ToString("D"), item.SignalId.ToString("D"), item.OrderStatus, item.OrderSide,
        item.OrderOutcome, item.AssetId, item.CopiedTraderWallet, Format.Decimal(item.OrderPrice),
        Format.Decimal(item.OrderSizeShares), Format.Decimal(item.OrderNotionalUsd),
        Format.Guid(item.CorrelationId), item.ExecutionSource, Format.Timestamp(item.OrderCreatedAtUtc),
        Format.Timestamp(item.OrderExpiresAtUtc), Format.Timestamp(item.OrderFilledAtUtc),
        Format.Timestamp(item.OrderCancelledAtUtc), Format.Guid(item.RunSignalIdProof),
        Format.Guid(item.RunPaperOrderIdProof), item.OrderStrategyIdProof.ToString("D"),
        Format.Guid(item.SignalRowIdProof), item.SignalOutcomeProof ?? string.Empty,
        item.SignalAssetIdProof ?? string.Empty, item.SignalConditionIdProof ?? string.Empty,
        item.SignalTraderWalletProof ?? string.Empty, Format.NullableDecimal(item.SignalLeaderPriceProof),
        item.SignalScoreProof?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        item.SignalAcceptedProof is { } accepted ? Format.Bool(accepted) : string.Empty,
        item.SignalDecisionProof ?? string.Empty, Format.NullableDecimal(item.SignalProposedPaperPriceProof),
        Format.NullableDecimal(item.SignalProposedSizeSharesProof),
        Format.NullableDecimal(item.SignalProposedNotionalUsdProof), Format.Timestamp(item.SignalCreatedAtUtcProof),
        Format.Guid(item.SignalLeaderTradeIdProof), Format.NullableDecimal(item.SignalBestBidProof),
        Format.NullableDecimal(item.SignalBestAskProof), Format.NullableDecimal(item.SignalSpreadAbsProof),
        Format.NullableDecimal(item.SignalSpreadPctProof),
        item.SignalLagSecondsProof?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        item.SignalRawContextJsonProof ?? string.Empty, Format.Bool(item.SignalNullableShapeValidProof),
        item.OrderExecutionModeProof, item.RawDecisionProofSha256, item.RunFullRowSha256,
        item.OrderFullRowSha256, item.SignalFullRowSha256, CanonicalEvidence.HashGraphOrder(item)
    ];

    public static IReadOnlyList<string> AddHeader { get; } =
    [
        "run_id", "strategy_id", "strategy_code", "market_id", "condition_id", "asset", "kind",
        "add_source_state_sha256", "add_source_run_full_row_sha256", "modeled_entry_at_utc",
        "modeled_settled_at_utc", "modeled_settlement_timestamp_source", "settlement_category",
        "modeled_raw_decision_json", "modeled_raw_decision_sha256", "modeled_fill_evidence",
        "modeled_mutation_payload_json", "modeled_mutation_payload_sha256", "assumed_fill_price",
        "historical_stake_multiplier", "gamma_order_min_size", "selected_outcome", "selected_token_id",
        "resolved_winning_outcome", "resolved_winning_token_id", "resolution_ledger_source",
        "resolution_ledger_provenance_group", "resolution_ledger_winning_asset_id",
        "resolution_ledger_winning_asset_agrees_with_gamma", "resolution_ledger_raw_event_type",
        "resolution_ledger_raw_sha256", "resolution_ledger_raw_bytes",
        "resolution_ledger_event_timestamp_utc_non_authoritative", "resolution_ledger_raw_event_timestamp_utc",
        "resolution_ledger_first_received_at_utc", "resolution_ledger_last_received_at_utc",
        "resolution_ledger_raw_validated", "raw_websocket_resolution_source",
        "raw_websocket_resolution_provenance_group", "raw_websocket_diagnostic_row_count",
        "raw_websocket_distinct_event_count", "archived_tick_source", "archived_tick_provenance_group",
        "archived_tick_sample_count", "archived_tick_start_price_usd", "archived_tick_end_price_usd",
        "archived_tick_end_age_ms", "archived_tick_winning_outcome",
        "archived_tick_agrees_with_authoritative_winner", "gamma_resolution_source",
        "gamma_resolution_provenance_group", "gamma_request_url", "gamma_raw_sha256", "gamma_raw_bytes",
        "gamma_fetched_at_utc", "gamma_resolution_source_detail", "gamma_live_order_min_size",
        "agreeing_independent_resolution_source_count", "raw_worst_price_notional_usd",
        "rounded_worst_price_notional_usd", "worst_price_target_size_shares", "requested_notional_usd",
        "filled_size_shares", "won", "settlement_price", "settlement_value_usd", "realized_pnl_usd",
        "can_add", "reason"
    ];

    public static IReadOnlyList<string> Add(AddFeasibility item) =>
    [
        item.RunId.ToString("D"), item.StrategyId.ToString("D"), item.StrategyCode, item.MarketId,
        item.ConditionId, item.Asset, item.Kind, item.AddSourceStateSha256, item.AddSourceRunFullRowSha256,
        Format.Timestamp(item.ModeledEntryAtUtc), Format.Timestamp(item.ModeledSettledAtUtc),
        item.ModeledSettlementTimestampSource, item.SettlementCategory, item.ModeledRawDecisionJson,
        item.ModeledRawDecisionSha256, item.ModeledFillEvidence, item.ModeledMutationPayloadJson,
        item.ModeledMutationPayloadSha256, Format.Decimal(item.AssumedFillPrice),
        Format.Decimal(item.HistoricalStakeMultiplier), Format.Decimal(item.GammaOrderMinSize),
        item.SelectedOutcome, item.SelectedTokenId, item.ResolvedWinningOutcome, item.ResolvedWinningTokenId,
        item.ResolutionLedgerSource, item.ResolutionLedgerProvenanceGroup, item.ResolutionLedgerWinningAssetId,
        Format.Bool(item.ResolutionLedgerWinningAssetAgreesWithGamma), item.ResolutionLedgerRawEventType,
        item.ResolutionLedgerRawSha256, item.ResolutionLedgerRawBytes.ToString(CultureInfo.InvariantCulture),
        Format.Timestamp(item.ResolutionLedgerEventTimestampUtc),
        Format.Timestamp(item.ResolutionLedgerRawEventTimestampUtc),
        Format.Timestamp(item.ResolutionLedgerFirstReceivedAtUtc),
        Format.Timestamp(item.ResolutionLedgerLastReceivedAtUtc), Format.Bool(item.ResolutionLedgerRawValidated),
        item.RawWebSocketResolutionSource, item.RawWebSocketResolutionProvenanceGroup,
        item.RawWebSocketDiagnosticRowCount.ToString(CultureInfo.InvariantCulture),
        item.RawWebSocketDistinctEventCount.ToString(CultureInfo.InvariantCulture), item.ArchivedTickSource,
        item.ArchivedTickProvenanceGroup, item.ArchivedTickSampleCount.ToString(CultureInfo.InvariantCulture),
        Format.Decimal(item.ArchivedTickStartPriceUsd), Format.Decimal(item.ArchivedTickEndPriceUsd),
        Format.Decimal(item.ArchivedTickEndAgeMilliseconds), item.ArchivedTickWinningOutcome,
        Format.Bool(item.ArchivedTickAgreesWithAuthoritativeWinner), item.GammaResolutionSource,
        item.GammaResolutionProvenanceGroup, item.GammaRequestUrl, item.GammaRawSha256,
        item.GammaRawBytes.ToString(CultureInfo.InvariantCulture), Format.Timestamp(item.GammaFetchedAtUtc),
        item.GammaResolutionSourceDetail, Format.NullableDecimal(item.GammaLiveOrderMinSize),
        item.AgreeingIndependentResolutionSourceCount.ToString(CultureInfo.InvariantCulture),
        Format.Decimal(item.RawWorstPriceNotionalUsd), Format.Decimal(item.RoundedWorstPriceNotionalUsd),
        Format.Decimal(item.WorstPriceTargetSizeShares), Format.Decimal(item.RequestedNotionalUsd),
        Format.Decimal(item.FilledSizeShares), Format.Bool(item.Won), Format.Decimal(item.SettlementPrice),
        Format.Decimal(item.SettlementValueUsd), Format.Decimal(item.RealizedPnlUsd), Format.Bool(item.CanAdd),
        item.Reason
    ];
}
