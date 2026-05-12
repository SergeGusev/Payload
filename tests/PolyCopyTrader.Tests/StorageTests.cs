using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class StorageTests
{
    [Fact]
    public void PostgresSchema_ContainsRequiredTables()
    {
        foreach (var table in PostgresSchema.RequiredTables)
        {
            Assert.Contains($"CREATE TABLE IF NOT EXISTS {table}", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        }

        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_leader_trades_dedup", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS ix_polymarket_http_logs_requested", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_positions", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_trade_captures", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_trade_capture_cursors", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_paper_signal_results", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_paper_signal_results_wallet_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ux_polymarket_onchain_trade_captures_tx_log", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_trade_captures_contract_block", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_trade_captures_pending_order", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_onchain_position_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_onchain_token_metadata_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_token_metadata_refresh_queue_next_attempt", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_category_mappings", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ux_polymarket_category_mappings_local_lower", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('Politics', 'POLITICS'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_activity", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_onchain_wallet_activity_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_performance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_onchain_wallet_performance_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_category_performance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_onchain_wallet_category_performance_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_trade_details", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_trade_details_recent", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_participant_details", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_participant_details_score", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_signal_candidates", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_signal_candidate_reasons", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_signal_candidate_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_signal_candidate_backfill_cursors", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_signal_candidates_status_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_signal_candidate_refresh_queue_next_attempt", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_wallet_fills_signal_candidate_backfill", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_wallet_fills_source_role", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE IF NOT EXISTS polymarket_data_api_trades", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_gamma_markets", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_gamma_markets_created", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_gamma_markets_condition", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_gamma_markets_clob_token_ids", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("order_min_size numeric", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("order_price_min_tick_size numeric", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("last_trade_price numeric", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_websocket_trade_ticks", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("trader_match_status integer NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("transaction_hash_present boolean NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("WHERE dedup_key IS NULL OR dedup_key = '' OR updated_at_utc IS NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_polymarket_websocket_trade_ticks_dedup", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("copied_trader_wallet text NOT NULL DEFAULT ''", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS strategies", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE public.copy_strategies RENAME TO strategies", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("live_available_balance numeric(28,8) NOT NULL DEFAULT 100.00", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_available_balance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ck_strategies_live_available_balance_nonnegative", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'follow_leader'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_less_30'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_less_30_gamma'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_less_180_martin'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_270'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_30_below_55'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_60_below_60'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_60_below_55'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_120_below_70'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_150_below_65'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_270_below_65'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_270_below_60'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_less_120_below_20'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_less_120_below_30'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_less_90_below_20'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_less_60_below_20'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_90_below_70'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_90_below_65'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_60_gamma_below_70'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_120_gamma_below_65'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_150_gamma_below_80'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_more_270_gamma'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_middle_1'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_middle_5'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_middle_1_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_middle_5_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_skip_1'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_skip_5'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_skip_1_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_skip_5_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_up'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_down'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("WITH intervals(interval_id, interval_code, interval_name, interval_description)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("generate_series(49, 30, -1)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_' || intervals.interval_code || '_preopen_'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'b7c50005-0000-4000-803' || intervals.interval_id", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Less 30", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Less 30 Gamma", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Less 180 Martin", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 270", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 30 Below 55", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 60 Below 60", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 60 Below 55", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 120 Below 70", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 150 Below 65", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 270 Below 65", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 270 Below 60", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Less 120 Below 20", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Less 120 Below 30", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Less 90 Below 20", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Less 60 Below 20", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 90 Below 70", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 90 Below 65", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 60 Gamma Below 70", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 120 Gamma Below 65", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 150 Gamma Below 80", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 270 Gamma", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Middle 5", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Middle 5 Revert", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Skip 5", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Skip 5 Revert", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Up", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Down", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m',", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("strategy_id uuid NOT NULL DEFAULT 'f0110a0d-1ead-4c00-8b01-000000000001'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("REFERENCES strategies(id)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_strategy_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_copied_wallet_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("realized_pnl_usd numeric(28,8) NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_fills_order_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("balance_effect_applied boolean NOT NULL DEFAULT false", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("average_fill_price numeric(18,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("filled_notional_usd numeric(28,8) NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("cost_basis_usd numeric(28,8) NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("fee_usd numeric(28,8) NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("won boolean NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("settlement_source text NOT NULL DEFAULT ''", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("correlation_id uuid NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("execution_source text NOT NULL DEFAULT ''", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("post_only boolean NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("paper_order_id uuid NULL REFERENCES paper_orders(id)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_live_orders_strategy_settlement", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_live_orders_pending_balance_settlement", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_live_shadow_decisions", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_live_shadow_discrepancies", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_live_shadow_decisions_strategy_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_live_shadow_discrepancies_correlation", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS strategy_market_paper_runs", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (strategy_id, market_id)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_entry_due", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_settlement_due", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ux_paper_positions_wallet_asset", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_positions_wallet_updated", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_positions_total_pnl_usd numeric(28,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_leaderboard_pnl_usd numeric(28,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE polymarket_data_api_wallet_performance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE polymarket_data_api_wallet_category_performance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_rating_next_refresh_at_utc timestamptz NOT NULL DEFAULT now()", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_data_api_wallet_category_ratings", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_data_api_wallet_category_ratings_category_pnl", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_data_api_wallet_category_ratings_lookup", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("leaderboard_pnl_to_volume_pct numeric(18,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_data_api_wallet_category_ratings_leaderboard_ratio", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("current_positions_percent_pnl numeric(18,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("closed_positions_realized_pnl_usd numeric(28,8) NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("positions_total_percent_pnl numeric(18,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_data_api_wallet_category_ratings_positions_pnl", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_position_settlements", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ux_paper_position_settlements_wallet_asset", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_copied_trader_performance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_copied_trader_performance_score", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_usd_reference_correlation_samples", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_usd_reference_correlation_samples_created", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_up_down_5m_odds_ticks", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_up_down_5m_odds_ticks_market_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crypto_up_down_5m_odds_ticks", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_crypto_up_down_5m_odds_ticks_asset_market_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("up_price_proxy_kind text NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("binance_start_price_usd numeric(28,8) NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_copied_leader_positions", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_copied_leader_positions_due", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_copied_leader_positions_wallet_asset", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_copied_leader_activity_events", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (dedup_key)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_copied_leader_activity_events_wallet_asset_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_OnChainPaperSignalCandidateQuery_LimitsPendingBeforeMetadataJoins()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetPendingOnChainPaperSignalCandidatesAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("AddOnChainPaperSignalResultAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var pendingQuery = source[start..end];
        Assert.Contains("WITH pending_captures AS MATERIALIZED", pendingQuery, StringComparison.Ordinal);
        Assert.Contains("participants AS MATERIALIZED", pendingQuery, StringComparison.Ordinal);
        Assert.Contains("maker_processed.participant_role = 'Maker'", pendingQuery, StringComparison.Ordinal);
        Assert.Contains("taker_processed.participant_role = 'Taker'", pendingQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE processed.id IS NULL", pendingQuery, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_OnChainPaperSignalHotQuery_UsesInMemoryCaptures()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetOnChainPaperSignalCandidatesForCapturesAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("GetPolymarketOnChainSignalCandidateSourcesAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var hotQuery = source[start..end];
        Assert.Contains("jsonb_to_recordset(CAST(@CapturesJson AS jsonb))", hotQuery, StringComparison.Ordinal);
        Assert.Contains("WITH hot_captures AS MATERIALIZED", hotQuery, StringComparison.Ordinal);
        Assert.Contains("maker_processed.participant_role = 'Maker'", hotQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM polymarket_onchain_trade_captures capture", hotQuery, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_AcceptedOnChainPaperOrder_UsesSingleTransaction()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("AddAcceptedOnChainPaperOrderAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("GetOnChainPaperSignalCandidatesForCapturesAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("BeginTransactionAsync", method, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO signals", method, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_orders", method, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_copied_leader_positions", method, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO polymarket_onchain_paper_signal_results", method, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_GammaMarketUpsert_SkipsUnchangedPayloadRows()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("INSERT INTO polymarket_gamma_markets", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("AddPolymarketGammaMarketParameters", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var gammaUpsertSql = source[start..end];
        Assert.Contains("ON CONFLICT (market_id) DO UPDATE SET", gammaUpsertSql, StringComparison.Ordinal);
        Assert.Contains(
            "WHERE\\n    polymarket_gamma_markets.condition_id IS DISTINCT FROM excluded.condition_id",
            gammaUpsertSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "polymarket_gamma_markets.raw_json IS DISTINCT FROM excluded.raw_json",
            gammaUpsertSql,
            StringComparison.Ordinal);
        Assert.Contains("fetched_at_utc = excluded.fetched_at_utc\\nWHERE", gammaUpsertSql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "polymarket_gamma_markets.fetched_at_utc IS DISTINCT FROM excluded.fetched_at_utc",
            gammaUpsertSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_DataApiTraderUpsert_ThrottlesSeenOnlyRows()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("INSERT INTO polymarket_data_api_traders", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("GetPolymarketDataApiTradersForSyncAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var traderUpsertSql = source[start..end];
        Assert.Contains("ON CONFLICT (wallet) DO UPDATE SET", traderUpsertSql, StringComparison.Ordinal);
        Assert.Contains("updated_at_utc = excluded.updated_at_utc\\nWHERE", traderUpsertSql, StringComparison.Ordinal);
        Assert.Contains(
            "excluded.last_trade_timestamp_utc > polymarket_data_api_traders.last_trade_timestamp_utc",
            traderUpsertSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "polymarket_data_api_traders.last_seen_at_utc <= excluded.last_seen_at_utc - interval '5 minutes'",
            traderUpsertSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "polymarket_data_api_traders.last_global_seen_at_utc <= excluded.last_global_seen_at_utc - interval '5 minutes'",
            traderUpsertSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_StatusUpserts_ThrottleClockOnlyRows()
    {
        var source = ReadStorageRepositorySource();

        Assert.Contains(
            "market_data_status.updated_at_utc <= excluded.updated_at_utc - interval '60 seconds'",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "scanner_status.updated_at_utc <= excluded.updated_at_utc - interval '60 seconds'",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "service_heartbeats.last_heartbeat_utc <= excluded.last_heartbeat_utc - interval '60 seconds'",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_DataApiPerformance_PopulatesPolymarketPositionBenchmarks()
    {
        var source = ReadStorageRepositorySource();

        Assert.Contains(
            "UPDATE polymarket_data_api_wallet_performance",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "UPDATE polymarket_data_api_wallet_category_performance",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "polymarket_positions_total_pnl_usd = total_pnl_usd",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "polymarket_positions_refreshed_at_utc = refreshed_at_utc",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_CategoryMappingLookup_FindsMissingDataApiCategories()
    {
        var source = ReadStorageRepositorySource();

        Assert.Contains(
            "GetMissingPolymarketLeaderboardCategoryMappingsAsync",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "LEFT JOIN polymarket_category_mappings mapping",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "mapping.local_category IS NULL",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_PolymarketOnlyRatings_UsesDedicatedTableAndRefreshCursor()
    {
        var source = ReadStorageRepositorySource();

        Assert.Contains(
            "GetPolymarketDataApiTradersForRatingRefreshAsync",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "polymarket_rating_next_refresh_at_utc <= @DueBeforeUtc",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "INSERT INTO polymarket_data_api_wallet_category_ratings",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "current_positions_count",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "leaderboard_pnl_to_volume_pct",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "LeaderboardPnlToVolumePct",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "PositionsTotalPnlUsd",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "positions_refreshed_at_utc",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "MarkPolymarketDataApiTraderRatingRefreshedAsync",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_PaperCopiedTraderPerformance_UsesSettlementsAndDedicatedTable()
    {
        var source = ReadStorageRepositorySource();

        Assert.Contains("TryAddPaperPositionSettlementAsync", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_position_settlements", source, StringComparison.Ordinal);
        Assert.Contains("RefreshPaperCopiedTraderPerformanceAsync", source, StringComparison.Ordinal);
        Assert.Contains("AcquirePaperCopiedTraderPerformanceRefreshLockAsync", source, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM paper_copied_trader_performance;", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_copied_trader_performance", source, StringComparison.Ordinal);
        Assert.Contains("Task<PaperCopiedTraderPerformance?> GetPaperCopiedTraderPerformanceAsync", source, StringComparison.Ordinal);
        Assert.Contains("greatest(0, least(100", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH deleted AS", source, StringComparison.Ordinal);
        Assert.Contains("FROM paper_position_settlements ps", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE((SELECT sum(ps.realized_pnl_usd) FROM paper_position_settlements ps), 0) AS paper_pnl", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_PaperCopiedLeaderExitTracking_StoresLinksAndDedupedActivity()
    {
        var source = ReadStorageRepositorySource();

        Assert.Contains("TryAddPaperCopiedLeaderPositionAsync", source, StringComparison.Ordinal);
        Assert.Contains("ActivatePaperCopiedLeaderPositionAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetPaperCopiedLeaderPositionsForExitTrackingAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPaperCopiedLeaderExitAsync", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_copied_leader_positions", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_copied_leader_activity_events", source, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (dedup_key) DO NOTHING", source, StringComparison.Ordinal);
        Assert.Contains("UPDATE paper_copied_leader_positions", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO signals", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_orders", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchemaInitializer_SplitsSchemaSqlIntoDebuggableStatements()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);

        Assert.True(statements.Count > PostgresSchema.RequiredTables.Count);
        Assert.All(statements, statement => Assert.False(string.IsNullOrWhiteSpace(statement)));
        Assert.Contains(statements, statement =>
            statement.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_trade_details", StringComparison.Ordinal));
        Assert.Contains(statements, statement =>
            statement.StartsWith("DO $$", StringComparison.Ordinal) &&
            statement.Contains("DROP VIEW public.polymarket_onchain_trade_details", StringComparison.Ordinal));
    }

    [Fact]
    public void PostgresSchemaInitializer_KeepsDollarQuotedBlocksTogether()
    {
        const string sql = """
CREATE TABLE first_table (id integer);
DO $$
BEGIN
    EXECUTE 'CREATE TABLE second_table (id integer);';
END $$;
CREATE INDEX first_table_id_idx ON first_table(id);
""";

        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(sql);

        Assert.Equal(3, statements.Count);
        Assert.StartsWith("CREATE TABLE first_table", statements[0], StringComparison.Ordinal);
        Assert.StartsWith("DO $$", statements[1], StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE second_table", statements[1], StringComparison.Ordinal);
        Assert.StartsWith("CREATE INDEX first_table_id_idx", statements[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CREATE INDEX IF NOT EXISTS ix_demo ON demo_table(id)", "ix_demo")]
    [InlineData("CREATE UNIQUE INDEX IF NOT EXISTS ux_demo ON demo_table(id)", "ux_demo")]
    [InlineData("  create index if not exists ix_lower ON demo_table(id)", "ix_lower")]
    [InlineData("CREATE TABLE demo_table (id integer)", null)]
    public void PostgresSchemaInitializer_ReadsCreateIndexIfNotExistsName(string statement, string? expected)
    {
        var actual = PostgresSchemaInitializer.TryReadCreateIndexIfNotExistsName(statement);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConnectionFactory_RequiresConfiguredConnectionString()
    {
        var options = new StorageOptions
        {
            ConnectionString = string.Empty,
            ConnectionStringEnvironmentVariable = "POLYCOPYTRADER_TEST_MISSING_CONNECTION"
        };

        Assert.Throws<InvalidOperationException>(() => new PostgresConnectionFactory(options));
    }

    [Fact]
    public async Task NoOpRepository_IsSafeWhenDatabaseIsNotConfigured()
    {
        var repository = new NoOpAppRepository();
        var heartbeat = new ServiceHeartbeat(
            "PolyCopyTrader.Service",
            "Running",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "1.0.0",
            BotMode.ReadOnly,
            "Test",
            null);

        await repository.UpsertServiceHeartbeatAsync(heartbeat);
        await repository.AddPolymarketHttpLogAsync(new PolymarketHttpLogEntry(
            Guid.NewGuid(),
            "PolymarketDataApiClient",
            "GetUserTrades",
            "GET",
            "https://data-api.polymarket.com/trades",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            10,
            1,
            200,
            true,
            "{}",
            null));
        await repository.TryAddPolymarketWebSocketTradeTickAsync(new PolymarketWebSocketTradeTick(
            Guid.NewGuid(),
            "tick-1",
            "asset-1",
            "condition-1",
            TradeSide.Buy,
            0.45m,
            10m,
            DateTimeOffset.UtcNow,
            "0xabc",
            true,
            TradeTickTraderMatchStatus.NotFound,
            null,
            DateTimeOffset.UtcNow,
            null,
            0,
            null,
            null,
            null,
            null,
            "{}",
            DateTimeOffset.UtcNow));
        var heartbeats = await repository.GetServiceHeartbeatsAsync();
        var httpLogs = await repository.GetRecentPolymarketHttpLogsAsync();
        var ticks = await repository.GetRecentPolymarketWebSocketTradeTicksAsync();

        Assert.Empty(heartbeats);
        Assert.Empty(httpLogs);
        Assert.Empty(ticks);
    }

    [Fact]
    public async Task PostgresRepository_InitializesSchema_WhenTestConnectionIsConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new StorageOptions { ConnectionString = connectionString };
        var factory = new PostgresConnectionFactory(options);
        var initializer = new PostgresSchemaInitializer(factory);
        await initializer.InitializeAsync();

        var repository = new PostgresAppRepository(factory);
        var heartbeat = new ServiceHeartbeat(
            "PolyCopyTrader.Tests",
            "Running",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "1.0.0",
            BotMode.ReadOnly,
            "IntegrationTest",
            null);

        await repository.UpsertServiceHeartbeatAsync(heartbeat);
        var httpLog = new PolymarketHttpLogEntry(
            Guid.NewGuid(),
            "PolymarketDataApiClient",
            "GetTraderLeaderboard",
            "GET",
            "https://data-api.polymarket.com/v1/leaderboard",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            25,
            1,
            200,
            true,
            "[]",
            null);
        await repository.AddPolymarketHttpLogAsync(httpLog);

        var heartbeats = await repository.GetServiceHeartbeatsAsync();
        var httpLogs = await repository.GetRecentPolymarketHttpLogsAsync();

        Assert.Contains(heartbeats, item => item.ServiceName == "PolyCopyTrader.Tests");
        Assert.Contains(httpLogs, item => item.Id == httpLog.Id);
    }

    private static string ReadStorageRepositorySource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "PolyCopyTrader.Storage",
                "PostgresAppRepository.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate PostgresAppRepository.cs from the test output directory.");
    }
}
