using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class StorageTests
{
    [Fact]
    public void NormalizeLiveOrderRawResponseJson_WrapsPlainTextBody()
    {
        var normalized = PostgresAppRepository.NormalizeLiveOrderRawResponseJson("service not ready");

        using var document = JsonDocument.Parse(normalized);
        Assert.Equal("service not ready", document.RootElement.GetProperty("raw").GetString());
    }

    [Fact]
    public void NormalizeLiveOrderRawResponseJson_PreservesJsonBody()
    {
        Assert.Equal("""{"status":"matched"}""", PostgresAppRepository.NormalizeLiveOrderRawResponseJson(""" {"status":"matched"} """));
        Assert.Equal("{}", PostgresAppRepository.NormalizeLiveOrderRawResponseJson(""));
    }

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
        Assert.Contains("paper_lost_coeff numeric(28,8) NOT NULL DEFAULT 1.00", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("live_lost_coeff numeric(28,8) NOT NULL DEFAULT 1.00", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("paper_lost_counter integer NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("live_lost_counter integer NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paper_lost_coeff", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_lost_coeff", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paper_lost_counter", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_lost_counter", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ck_strategies_paper_lost_coeff_minimum", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ck_strategies_live_lost_coeff_minimum", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS ck_strategies_paper_lost_counter_nonnegative", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS ck_strategies_live_lost_counter_nonnegative", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("CHECK (paper_lost_counter >= 0)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("CHECK (live_lost_counter >= 0)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("paused boolean NOT NULL DEFAULT false", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paused boolean NOT NULL DEFAULT false", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paused_until_utc timestamptz NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("auto_live_paused boolean NOT NULL DEFAULT false", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS auto_live_paused boolean NOT NULL DEFAULT false", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("live_enabled_at_utc timestamptz NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_enabled_at_utc timestamptz NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("first_live_events AS", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("20260602_clear_auto_live_pause_by_default", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("WHERE auto_live_paused", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("SET paused = false", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ALTER COLUMN live_stake_amount SET DEFAULT 1.00", PostgresSchema.SchemaSql, StringComparison.Ordinal);
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
        Assert.Contains("'btc_up_down_5m_middle_1_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_middle_' || depths.depth || '_bps_' || thresholds.threshold_digit", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_middle_' || depths.depth || '_bps_' || thresholds.threshold_digit || '_instant'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_middle_' || depths.depth || '_revert_bps_' || thresholds.threshold_digit", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_middle_' || depths.depth || '_revert_bps_' || thresholds.threshold_digit || '_instant'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'b7c50005-0000-4000-8029-' || lpad(((depths.depth * 100) + thresholds.threshold_digit)::text, 12, '0')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'b7c50005-0000-4000-8030-' || lpad(((depths.depth * 100) + thresholds.threshold_digit)::text, 12, '0')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 100)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_middle_1'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_middle_1_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_middle_1_bps_' || thresholds.threshold_digit", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_middle_1_bps_' || thresholds.threshold_digit || '_instant'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_middle_1_revert_bps_' || thresholds.threshold_digit", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_middle_1_revert_bps_' || thresholds.threshold_digit || '_instant'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8071', '8072', '8073', '8074')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8075', '8076', '8077', '8078')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("20260522_rescale_middle_bps_history_reset", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("20260522_retire_middle_depth_2_5", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_skip_1'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_skip_5'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_skip_1_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_skip_5_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_skip_' || depth_name", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_skip_bps_' || code_suffix", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_skip_bps_' || code_suffix || '_instant'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_up'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_down'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_up_maker'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_down_maker'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_statistics'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_5m_prev_score_countertrend_' || prices.price_cents", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'b7c50005-0000-4000-8025-' || lpad(prices.price_cents::text, 12, '0')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("generate_series(10, 90, 5)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("WITH intervals(interval_id, interval_code, interval_name, interval_description)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("generate_series(49, 10, -1)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_' || intervals.interval_code || '_preopen_'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'b7c50005-0000-4000-803' || intervals.interval_id", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'btc_up_down_' || intervals.interval_code || '_preopen_full_' || outcomes.outcome_code || '_' || prices.price_cents || '_sell'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'b7c50005-0000-4000-804' || intervals.interval_id", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("PreOpen Full ' || outcomes.outcome_name || ' ' || prices.price_cents || ' Sell'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
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
        Assert.Contains("BTC Up or Down 5m Up Maker", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Down Maker", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Up Maker 50", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Down Maker 50", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 60 Gamma Below 70", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 120 Gamma Below 65", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 150 Gamma Below 80", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m More 270 Gamma", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Middle 1", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Middle 1 Revert", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Middle ' || depths.depth || ' ' || thresholds.threshold_name || ' bps Instant", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Middle ' || depths.depth || ' Revert ' || thresholds.threshold_name || ' bps Instant", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Middle 5", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Middle 5 Revert", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Skip 5", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Skip 5 Revert", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("asset_symbol || ' Up or Down 5m Skip ' || depth_name", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("asset_symbol || ' Up or Down 5m Skip ' || threshold_name || ' bps Instant", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Up", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Down", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("BTC Up or Down 5m Statistics", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m',", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("strategy_id uuid NOT NULL DEFAULT 'f0110a0d-1ead-4c00-8b01-000000000001'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("REFERENCES strategies(id)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_strategy_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_strategy_perf_cover", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_id_strategy_side_cover", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_copied_wallet_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_created_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
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
        Assert.Contains("ix_strategy_market_paper_runs_status_market_end", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_strategy_entered", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_strategy_updated", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_strategy_settled", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_fills_filled_time_order", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_fills_filled_perf_cover", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_updated_time_strategy", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_entered_time_strategy", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_settled_time_strategy", PostgresSchema.SchemaSql, StringComparison.Ordinal);
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
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_5m_history", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("id bigserial PRIMARY KEY", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("seconds integer NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("cents integer NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("count integer NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("up_count integer NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("down_count integer NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT ux_btc_5m_history_seconds_cents UNIQUE (seconds, cents)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE btc_5m_history", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT ux_btc_5m_history_seconds_cents UNIQUE (seconds, cents)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_5m_history_live_observations", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (market_id, seconds)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_5m_history_live_observations_due", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_up_down_5m_statistics_ticks", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("effective_count numeric(28,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("decision_code text NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_up_down_5m_statistics_ticks_decision", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_up_down_5m_arbitrage_scans", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("net_profit_usd numeric(28,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("would_arbitrage boolean NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_up_down_5m_arbitrage_scans_decision", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_up_down_5m_result_streak_diagnostics", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("close_book_streak_result_count integer NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("cumulative_abs_move_bps numeric(28,12) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ux_btc_up_down_5m_result_streak_diagnostics_market", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_up_down_5m_result_streak_diagnostics_streak", PostgresSchema.SchemaSql, StringComparison.Ordinal);
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
    public void PostgresRepository_GetRecentPaperOrders_UsesCreatedTimeIndexAndLightweightProjection()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetRecentPaperOrdersAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("AddPaperFillAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("RecentPaperOrderSelectColumns", method, StringComparison.Ordinal);
        Assert.Contains("WHERE strategy_id = @StrategyId", method, StringComparison.Ordinal);
        Assert.Contains("ORDER BY created_at_utc DESC", method, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SELECT \" + PaperOrderSelectColumns", method, StringComparison.Ordinal);
        Assert.Contains("NULL::text", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_GetRecentLiveOrders_SupportsStrategyFilteredFirstPage()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetRecentLiveOrdersAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("AddLiveTradingEventAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("WHERE strategy_id = @StrategyId", method, StringComparison.Ordinal);
        Assert.Contains("ORDER BY created_at_utc DESC", method, StringComparison.Ordinal);
        Assert.Contains("LIMIT @Limit", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardDataService_LoadOrderRows_DoesNotRefreshStrategyPerformance()
    {
        var source = ReadDashboardDataServiceSource();
        var start = source.IndexOf("LoadOrderRowsAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("InvalidateStrategyPerformanceCache", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("GetOrderStrategyNamesById", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GetStrategyPerformanceAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestRepository_GetRecentOrders_FiltersByStrategyBeforeLimit()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var firstStrategy = StrategyIds.FollowLeader;
        var secondStrategy = StrategyIds.BtcUpDown5mBinanceBps2;
        repository.PaperOrders.Add(CreatePaperOrder(secondStrategy, now.AddMinutes(-1)));
        repository.PaperOrders.Add(CreatePaperOrder(firstStrategy, now.AddMinutes(-2)));
        repository.PaperOrders.Add(CreatePaperOrder(firstStrategy, now.AddMinutes(-3)));
        repository.LiveOrders.Add(CreateLiveOrder(secondStrategy, now.AddMinutes(-1)));
        repository.LiveOrders.Add(CreateLiveOrder(firstStrategy, now.AddMinutes(-2)));
        repository.LiveOrders.Add(CreateLiveOrder(firstStrategy, now.AddMinutes(-3)));

        var paperAll = await repository.GetRecentPaperOrdersAsync(1);
        var paperFiltered = await repository.GetRecentPaperOrdersAsync(1, strategyId: firstStrategy);
        var liveAll = await repository.GetRecentLiveOrdersAsync(1);
        var liveFiltered = await repository.GetRecentLiveOrdersAsync(1, strategyId: firstStrategy);

        Assert.Equal(secondStrategy, Assert.Single(paperAll).StrategyId);
        Assert.Equal(firstStrategy, Assert.Single(paperFiltered).StrategyId);
        Assert.Equal(secondStrategy, Assert.Single(liveAll).StrategyId);
        Assert.Equal(firstStrategy, Assert.Single(liveFiltered).StrategyId);
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
    public void PostgresRepository_StrategyAutoLivePauseSeparatesLivePauseFromPaperResume()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("UpdateStrategyAutoLivePauseFromRecentPnlAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("SetStrategyStakeAmountsAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("count(*)::integer AS settled_count", method, StringComparison.Ordinal);
        Assert.Contains("PauseFromLiveSettlements", method, StringComparison.Ordinal);
        Assert.Contains("ResumeFromPaperSettlements", method, StringComparison.Ordinal);
        Assert.Contains("FROM live_orders live_order", method, StringComparison.Ordinal);
        Assert.Contains("FROM strategy_market_paper_runs run", method, StringComparison.Ordinal);
        Assert.Contains("FROM paper_position_settlements settlement", method, StringComparison.Ordinal);
        Assert.Contains("AND (SELECT settled_count FROM selected_pnl) > 1", method, StringComparison.Ordinal);
        Assert.Contains("SET auto_live_paused = CASE", method, StringComparison.Ordinal);
        Assert.Contains("THEN true", method, StringComparison.Ordinal);
        Assert.Contains("THEN false", method, StringComparison.Ordinal);
        Assert.Contains("AS recent_settled_count", method, StringComparison.Ordinal);
        Assert.DoesNotContain("paused_until_utc", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_StrategyRecentPerformanceKeepsRawLiveFlagForDashboardFilter()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetStrategyRecentPerformanceAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("GetStrategyEnabledStatesAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains(
            "SELECT id, code, name, live_stakes, auto_live_paused, live_enabled_at_utc, live_stakes AND NOT auto_live_paused AS effective_live_stakes",
            method,
            StringComparison.Ordinal);
        Assert.Contains("sw.live_stakes,", method, StringComparison.Ordinal);
        Assert.Contains("CASE WHEN sw.effective_live_stakes", method, StringComparison.Ordinal);
        Assert.Contains("run.live_enabled_at_utc IS NOT NULL", method, StringComparison.Ordinal);
        Assert.Contains("run.updated_at_utc >= run.live_enabled_at_utc", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_SetStrategyLiveStakesMaintainsLiveEnabledBoundary()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("SetStrategyLiveStakesAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("SetStrategyPausedAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("live_enabled_at_utc = CASE", method, StringComparison.Ordinal);
        Assert.Contains("WHEN @LiveStakes AND NOT live_stakes THEN @UpdatedAtUtc", method, StringComparison.Ordinal);
        Assert.Contains("WHEN @LiveStakes AND live_enabled_at_utc IS NULL THEN @UpdatedAtUtc", method, StringComparison.Ordinal);
        Assert.Contains("WHEN NOT @LiveStakes THEN NULL", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_StrategyAutoLivePauseClearKeepsAllowlistedStrategiesPaused()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("ClearStrategyAutoLivePauseExceptAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("SetStrategyStakeAmountsAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("SET auto_live_paused = false", method, StringComparison.Ordinal);
        Assert.Contains("WHERE auto_live_paused", method, StringComparison.Ordinal);
        Assert.Contains("id <> ALL(@AllowlistedStrategyIds)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_SetStrategyStakeAmountsSavesLostCoefficients()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("SetStrategyStakeAmountsAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("SetStrategyLiveAvailableBalanceAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("paper_lost_coeff = @PaperLostCoeff", method, StringComparison.Ordinal);
        Assert.Contains("live_lost_coeff = @LiveLostCoeff", method, StringComparison.Ordinal);
        Assert.Contains("paper_lost_counter = @PaperLostCounter", method, StringComparison.Ordinal);
        Assert.Contains("live_lost_counter = @LiveLostCounter", method, StringComparison.Ordinal);
        Assert.Contains("AND @PaperLostCoeff >= 1", method, StringComparison.Ordinal);
        Assert.Contains("AND @LiveLostCoeff >= 1", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AND @PaperLostCounter >= 0", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AND @LiveLostCounter >= 0", method, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"PaperLostCoeff\", paperLostCoeff)", method, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"LiveLostCoeff\", liveLostCoeff)", method, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"PaperLostCounter\", paperLostCounter)", method, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"LiveLostCounter\", liveLostCounter)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_UpdateStrategyLostCounterAfterSettlementUpdatesSelectedModeOnly()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("UpdateStrategyLostCounterAfterSettlementAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("TryAddPaperCopiedLeaderPositionAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("paper_lost_counter = CASE", method, StringComparison.Ordinal);
        Assert.Contains("WHEN @IsLive THEN paper_lost_counter", method, StringComparison.Ordinal);
        Assert.Contains("WHEN NOT @CounterEnabled THEN 0", method, StringComparison.Ordinal);
        Assert.Contains("WHEN @Won THEN paper_lost_counter - 1", method, StringComparison.Ordinal);
        Assert.Contains("ELSE paper_lost_counter + 1", method, StringComparison.Ordinal);
        Assert.Contains("live_lost_counter = CASE", method, StringComparison.Ordinal);
        Assert.Contains("WHEN NOT @IsLive THEN live_lost_counter", method, StringComparison.Ordinal);
        Assert.Contains("WHEN @Won THEN live_lost_counter - 1", method, StringComparison.Ordinal);
        Assert.Contains("ELSE live_lost_counter + 1", method, StringComparison.Ordinal);
        Assert.Contains("RETURNING paper_lost_counter, live_lost_counter", method, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestRepository_StrategyAutoLivePausePausesOnlyFromRecentLivePnlNegative()
    {
        var now = DateTimeOffset.UtcNow;
        var strategyId = StrategyIds.BtcUpDown5mBinanceBps2;
        var repository = new TestAppRepository();
        repository.StrategySettings[strategyId] = StrategyRuntimeSettings.Default(strategyId) with
        {
            LiveStakes = true
        };
        await repository.AddLiveOrderAsync(CreateSettledLiveOrder(strategyId, now.AddMinutes(-10), -1m));
        await repository.AddLiveOrderAsync(CreateSettledLiveOrder(strategyId, now.AddMinutes(-5), -1m));

        var decision = await repository.UpdateStrategyAutoLivePauseFromRecentPnlAsync(
            strategyId,
            now.AddHours(-12),
            now,
            StrategyAutoLivePauseUpdateMode.PauseFromLiveSettlements,
            CancellationToken.None);

        Assert.True(decision.AutoLivePaused);
        Assert.False(decision.AutoLiveResumed);
        Assert.True(decision.AutoLivePauseChanged);
        Assert.Equal(2, decision.RecentSettledCount);
        Assert.Equal(-2m, decision.RecentPnlUsd);
        Assert.True(repository.StrategySettings[strategyId].LiveStakes);
        Assert.True(repository.StrategySettings[strategyId].AutoLivePaused);
        Assert.False(repository.StrategySettings[strategyId].Paused);
        Assert.Null(repository.StrategySettings[strategyId].PausedUntilUtc);
    }

    [Fact]
    public async Task TestRepository_StrategyAutoLivePauseDoesNotPauseFromPaperLoss()
    {
        var now = DateTimeOffset.UtcNow;
        var strategyId = StrategyIds.BtcUpDown5mBinanceBps2;
        var repository = new TestAppRepository();
        repository.StrategySettings[strategyId] = StrategyRuntimeSettings.Default(strategyId) with
        {
            LiveStakes = true
        };
        repository.StrategyMarketPaperRuns.Add(CreateSettledStrategyRun(strategyId, now.AddMinutes(-10), -1m));
        repository.StrategyMarketPaperRuns.Add(CreateSettledStrategyRun(strategyId, now.AddMinutes(-5), -1m));

        var decision = await repository.UpdateStrategyAutoLivePauseFromRecentPnlAsync(
            strategyId,
            now.AddHours(-12),
            now,
            StrategyAutoLivePauseUpdateMode.ResumeFromPaperSettlements,
            CancellationToken.None);

        Assert.False(decision.AutoLivePaused);
        Assert.False(decision.AutoLiveResumed);
        Assert.False(decision.AutoLivePauseChanged);
        Assert.Equal(2, decision.RecentSettledCount);
        Assert.Equal(-2m, decision.RecentPnlUsd);
        Assert.True(repository.StrategySettings[strategyId].LiveStakes);
        Assert.False(repository.StrategySettings[strategyId].AutoLivePaused);
    }

    [Fact]
    public async Task TestRepository_StrategyAutoLivePauseClearsOnlyFromRecentPaperPnlPositive()
    {
        var now = DateTimeOffset.UtcNow;
        var strategyId = StrategyIds.BtcUpDown5mBinanceBps2;
        var repository = new TestAppRepository();
        repository.StrategySettings[strategyId] = StrategyRuntimeSettings.Default(strategyId) with
        {
            LiveStakes = true,
            AutoLivePaused = true
        };
        repository.StrategyMarketPaperRuns.Add(CreateSettledStrategyRun(strategyId, now.AddMinutes(-10), 2m));

        var decision = await repository.UpdateStrategyAutoLivePauseFromRecentPnlAsync(
            strategyId,
            now.AddHours(-12),
            now,
            StrategyAutoLivePauseUpdateMode.ResumeFromPaperSettlements,
            CancellationToken.None);

        Assert.False(decision.AutoLivePaused);
        Assert.True(decision.AutoLiveResumed);
        Assert.True(decision.AutoLivePauseChanged);
        Assert.Equal(1, decision.RecentSettledCount);
        Assert.Equal(2m, decision.RecentPnlUsd);
        Assert.True(repository.StrategySettings[strategyId].LiveStakes);
        Assert.False(repository.StrategySettings[strategyId].AutoLivePaused);
    }

    [Fact]
    public async Task TestRepository_StrategyAutoLivePauseDoesNotClearFromLiveWin()
    {
        var now = DateTimeOffset.UtcNow;
        var strategyId = StrategyIds.BtcUpDown5mBinanceBps2;
        var repository = new TestAppRepository();
        repository.StrategySettings[strategyId] = StrategyRuntimeSettings.Default(strategyId) with
        {
            LiveStakes = true,
            AutoLivePaused = true
        };
        await repository.AddLiveOrderAsync(CreateSettledLiveOrder(strategyId, now.AddMinutes(-10), 2m));

        var decision = await repository.UpdateStrategyAutoLivePauseFromRecentPnlAsync(
            strategyId,
            now.AddHours(-12),
            now,
            StrategyAutoLivePauseUpdateMode.PauseFromLiveSettlements,
            CancellationToken.None);

        Assert.True(decision.AutoLivePaused);
        Assert.False(decision.AutoLiveResumed);
        Assert.False(decision.AutoLivePauseChanged);
        Assert.Equal(1, decision.RecentSettledCount);
        Assert.Equal(2m, decision.RecentPnlUsd);
        Assert.True(repository.StrategySettings[strategyId].LiveStakes);
        Assert.True(repository.StrategySettings[strategyId].AutoLivePaused);
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
    [InlineData("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_demo_concurrent ON demo_table(id)", "ix_demo_concurrent")]
    [InlineData("CREATE UNIQUE INDEX IF NOT EXISTS ux_demo ON demo_table(id)", "ux_demo")]
    [InlineData("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_demo_concurrent ON demo_table(id)", "ux_demo_concurrent")]
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

    private static StrategyMarketPaperRun CreateSettledStrategyRun(Guid strategyId, DateTimeOffset settledAtUtc, decimal realizedPnlUsd)
    {
        return new StrategyMarketPaperRun(
            Guid.NewGuid(),
            strategyId,
            "market-" + Guid.NewGuid().ToString("N"),
            "condition-" + Guid.NewGuid().ToString("N"),
            "btc-updown-5m-test",
            "Bitcoin Up or Down - test",
            "Crypto",
            settledAtUtc.AddMinutes(-5),
            settledAtUtc,
            settledAtUtc.AddMinutes(-5),
            settledAtUtc.AddMinutes(-4),
            StrategyMarketPaperRunStatuses.Settled,
            "asset-up",
            "Up",
            0.50m,
            1m,
            2m,
            Guid.NewGuid(),
            Guid.NewGuid(),
            settledAtUtc.AddMinutes(-3),
            SettlementPrice: realizedPnlUsd < 0m ? 0m : 1m,
            SettlementValueUsd: 1m + realizedPnlUsd,
            RealizedPnlUsd: realizedPnlUsd,
            SettledAtUtc: settledAtUtc,
            SkipReason: null,
            settledAtUtc.AddMinutes(-5),
            settledAtUtc);
    }

    private static PaperOrder CreatePaperOrder(Guid strategyId, DateTimeOffset createdAtUtc)
    {
        return new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0xleader",
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-" + Guid.NewGuid().ToString("N"),
            "condition-" + Guid.NewGuid().ToString("N"),
            "Up",
            0.50m,
            2m,
            1m,
            createdAtUtc,
            createdAtUtc.AddMinutes(10),
            StrategyId: strategyId);
    }

    private static LiveOrder CreateLiveOrder(Guid strategyId, DateTimeOffset createdAtUtc)
    {
        return new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Submitted,
            "0x" + Guid.NewGuid().ToString("N"),
            TradeSide.Buy,
            "asset-" + Guid.NewGuid().ToString("N"),
            "condition-" + Guid.NewGuid().ToString("N"),
            "Up",
            0.50m,
            2m,
            1m,
            "GTD",
            createdAtUtc,
            createdAtUtc.AddMinutes(10),
            createdAtUtc,
            "submitted",
            0m,
            2m,
            string.Empty,
            "{}",
            string.Empty,
            createdAtUtc,
            StrategyId: strategyId);
    }

    private static LiveOrder CreateSettledLiveOrder(Guid strategyId, DateTimeOffset settledAtUtc, decimal realizedPnlUsd)
    {
        var won = realizedPnlUsd > 0m;
        var costBasisUsd = 1m;
        var settlementValueUsd = costBasisUsd + realizedPnlUsd;
        return new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Matched,
            "0x" + Guid.NewGuid().ToString("N"),
            TradeSide.Buy,
            "asset-up",
            "condition-" + Guid.NewGuid().ToString("N"),
            "Up",
            0.50m,
            2m,
            costBasisUsd,
            "GTD",
            settledAtUtc.AddMinutes(-5),
            settledAtUtc,
            settledAtUtc.AddMinutes(-4),
            "matched",
            2m,
            0m,
            string.Empty,
            "{}",
            string.Empty,
            settledAtUtc,
            StrategyId: strategyId,
            BalanceEffectApplied: true,
            SettlementValueUsd: settlementValueUsd,
            RealizedPnlUsd: realizedPnlUsd,
            SettledAtUtc: settledAtUtc,
            WinningAssetId: won ? "asset-up" : "asset-down",
            WinningOutcome: won ? "Up" : "Down",
            AverageFillPrice: 0.50m,
            FilledNotionalUsd: costBasisUsd,
            CostBasisUsd: costBasisUsd,
            Won: won,
            SettlementSource: "test");
    }

    private static string ReadStorageRepositorySource()
    {
        return ReadRepositorySource("src", "PolyCopyTrader.Storage", "PostgresAppRepository.cs");
    }

    private static string ReadDashboardDataServiceSource()
    {
        return ReadRepositorySource("src", "PolyCopyTrader.Dashboard", "Services", "DashboardDataService.cs");
    }

    private static string ReadRepositorySource(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidateParts = new string[pathParts.Length + 1];
            candidateParts[0] = directory.FullName;
            Array.Copy(pathParts, 0, candidateParts, 1, pathParts.Length);
            var candidate = Path.Combine(candidateParts);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository source file from the test output directory.");
    }
}
