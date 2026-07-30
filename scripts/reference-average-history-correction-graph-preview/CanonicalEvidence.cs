using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReferenceAverageHistoryCorrectionGraphPreview;

internal static class CanonicalEvidence
{
    public const int SchemaVersion = 2;
    public const string Algorithm = "SHA-256";
    public const string Serialization = "System.Text.Json UTF-8; fixed property order; decimals invariant strings; UTC timestamps O";

    public static string HashRawDecisionProof(string? projectedRawDecisionJson) =>
        string.IsNullOrWhiteSpace(projectedRawDecisionJson)
            ? string.Empty
            : HashBytes(Encoding.UTF8.GetBytes(projectedRawDecisionJson));

    public static string HashSql(string sql) => HashBytes(Encoding.UTF8.GetBytes(sql));

    public static string HashUtf8Text(string value) => HashBytes(Encoding.UTF8.GetBytes(value));

    public static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');

    public static string HashGraphOrder(GraphOrder item) => HashObject(new
    {
        schema_version = SchemaVersion,
        entity = "graph_order_mutation_scope",
        item.Scope,
        parent_main_run_id = Format.Guid(item.ParentMainRunId),
        run_id = item.RunId.ToString("D"),
        strategy_id = item.StrategyId.ToString("D"),
        item.StrategyCode,
        item.MarketId,
        item.ConditionId,
        entry_due_at_utc = Format.Timestamp(item.EntryDueAtUtc),
        item.RunStatus,
        run_outcome = item.RunOutcome ?? string.Empty,
        run_asset_id = item.RunAssetId ?? string.Empty,
        entry_price = Format.NullableDecimal(item.EntryPrice),
        stake_usd = Format.Decimal(item.StakeUsd),
        run_size_shares = Format.NullableDecimal(item.RunSizeShares),
        settlement_price = Format.NullableDecimal(item.SettlementPrice),
        settlement_value_usd = Format.NullableDecimal(item.SettlementValueUsd),
        run_realized_pnl_usd = Format.NullableDecimal(item.RunRealizedPnlUsd),
        settled_at_utc = Format.Timestamp(item.SettledAtUtc),
        paper_order_id = item.OrderId.ToString("D"),
        signal_id = item.SignalId.ToString("D"),
        item.OrderStatus,
        item.OrderSide,
        item.OrderOutcome,
        item.AssetId,
        item.CopiedTraderWallet,
        order_price = Format.Decimal(item.OrderPrice),
        order_size_shares = Format.Decimal(item.OrderSizeShares),
        order_notional_usd = Format.Decimal(item.OrderNotionalUsd),
        correlation_id = Format.Guid(item.CorrelationId),
        item.ExecutionSource,
        order_created_at_utc = Format.Timestamp(item.OrderCreatedAtUtc),
        run_signal_id = Format.Guid(item.RunSignalIdProof),
        run_paper_order_id = Format.Guid(item.RunPaperOrderIdProof),
        order_strategy_id = item.OrderStrategyIdProof.ToString("D"),
        signal_row_id = Format.Guid(item.SignalRowIdProof),
        signal_outcome = item.SignalOutcomeProof ?? string.Empty,
        signal_asset_id = item.SignalAssetIdProof ?? string.Empty,
        signal_condition_id = item.SignalConditionIdProof ?? string.Empty,
        signal_trader_wallet = item.SignalTraderWalletProof ?? string.Empty,
        signal_leader_price = Format.NullableDecimal(item.SignalLeaderPriceProof),
        signal_score = item.SignalScoreProof?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        signal_accepted = item.SignalAcceptedProof is { } accepted ? Format.Bool(accepted) : string.Empty,
        signal_decision = item.SignalDecisionProof ?? string.Empty,
        signal_proposed_paper_price = Format.NullableDecimal(item.SignalProposedPaperPriceProof),
        signal_proposed_size_shares = Format.NullableDecimal(item.SignalProposedSizeSharesProof),
        signal_proposed_notional_usd = Format.NullableDecimal(item.SignalProposedNotionalUsdProof),
        signal_created_at_utc = Format.Timestamp(item.SignalCreatedAtUtcProof),
        order_expires_at_utc = Format.Timestamp(item.OrderExpiresAtUtc),
        order_filled_at_utc = Format.Timestamp(item.OrderFilledAtUtc),
        order_cancelled_at_utc = Format.Timestamp(item.OrderCancelledAtUtc),
        item.RunFullRowSha256,
        item.OrderFullRowSha256,
        item.SignalFullRowSha256,
        item.StrategyNameProof,
        item.MarketSlugProof,
        run_category = item.RunCategoryProof ?? string.Empty,
        run_entered_at_utc = Format.Timestamp(item.RunEnteredAtUtcProof),
        run_created_at_utc = Format.Timestamp(item.RunCreatedAtUtcProof),
        run_updated_at_utc = Format.Timestamp(item.RunUpdatedAtUtcProof),
        market_end_utc = Format.Timestamp(item.MarketEndUtcProof),
        run_skip_reason = item.RunSkipReasonProof ?? string.Empty,
        item.RunSkipDiagnosticsIsNullProof,
        signal_leader_trade_id = Format.Guid(item.SignalLeaderTradeIdProof),
        signal_best_bid = Format.NullableDecimal(item.SignalBestBidProof),
        signal_best_ask = Format.NullableDecimal(item.SignalBestAskProof),
        signal_spread_abs = Format.NullableDecimal(item.SignalSpreadAbsProof),
        signal_spread_pct = Format.NullableDecimal(item.SignalSpreadPctProof),
        signal_lag_seconds = item.SignalLagSecondsProof?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        signal_raw_context_json = item.SignalRawContextJsonProof ?? string.Empty,
        item.SignalNullableShapeValidProof,
        item.OrderExecutionModeProof,
        item.RawDecisionProofSha256
    });

    public static string HashFillSet(IEnumerable<GraphFill> rows) => HashObject(new
    {
        schema_version = SchemaVersion,
        entity = "graph_fill_set_mutation_scope",
        rows = rows.OrderBy(item => item.OrderId)
            .ThenBy(item => item.FilledAtUtc)
            .ThenBy(item => item.FillId)
            .Select(item => new
            {
                item.Scope,
                parent_main_run_id = Format.Guid(item.ParentMainRunId),
                run_id = item.RunId.ToString("D"),
                paper_order_id = item.OrderId.ToString("D"),
                fill_id = item.FillId.ToString("D"),
                price = Format.Decimal(item.Price),
                size_shares = Format.Decimal(item.SizeShares),
                filled_at_utc = Format.Timestamp(item.FilledAtUtc),
                realized_pnl_usd = Format.Decimal(item.RealizedPnlUsd),
                item.Evidence,
                item.FullRowSha256
            })
    });

    public static string HashAddSource(AddSourceRow item) => HashObject(new
    {
        schema_version = SchemaVersion,
        entity = "add_source_run_mutation_scope",
        run_id = item.RunId.ToString("D"),
        strategy_id = item.StrategyId.ToString("D"),
        item.StrategyCode,
        item.MarketId,
        item.ConditionId,
        item.RunStatus,
        skip_reason = item.SkipReason ?? string.Empty,
        entry_due_at_utc = Format.Timestamp(item.EntryDueAtUtc),
        market_end_utc = Format.Timestamp(item.MarketEndUtc),
        stake_usd = Format.Decimal(item.StakeUsd),
        selected_asset_id = item.SelectedAssetId ?? string.Empty,
        selected_outcome = item.SelectedOutcome ?? string.Empty,
        entry_price = Format.NullableDecimal(item.EntryPrice),
        size_shares = Format.NullableDecimal(item.SizeShares),
        signal_id = Format.Guid(item.SignalId),
        paper_order_id = Format.Guid(item.PaperOrderId),
        entered_at_utc = Format.Timestamp(item.EnteredAtUtc),
        settlement_price = Format.NullableDecimal(item.SettlementPrice),
        settlement_value_usd = Format.NullableDecimal(item.SettlementValueUsd),
        realized_pnl_usd = Format.NullableDecimal(item.RealizedPnlUsd),
        settled_at_utc = Format.Timestamp(item.SettledAtUtc),
        skip_diagnostics_json = item.SkipDiagnosticsJson ?? string.Empty,
        item.MarketSlug,
        item.RunFullRowSha256,
        updated_at_utc = Format.Timestamp(item.UpdatedAtUtc),
        category = item.Category ?? string.Empty
    });

    private static string HashObject<T>(T value) =>
        HashBytes(JsonSerializer.SerializeToUtf8Bytes(value));

    private static string HashBytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
