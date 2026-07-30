using System.Text;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public static class PostgresSchema
{
    public static readonly IReadOnlyList<string> RequiredTables =
    [
        "traders",
        "trader_rules",
        "trader_leaderboard_snapshots",
        "trader_discovery_candidates",
        "polymarket_category_mappings",
        "polymarket_data_api_traders",
        "polymarket_data_api_positions",
        "polymarket_data_api_wallet_performance",
        "polymarket_data_api_wallet_category_performance",
        "polymarket_data_api_wallet_category_ratings",
        "polymarket_gamma_markets",
        "leader_trades",
        "leader_positions",
        "markets",
        "order_book_snapshots",
        "signals",
        "signal_rejections",
        "strategies",
        "dashboard_strategy_performance_snapshots",
        "dashboard_strategy_recent_performance_snapshots",
        "dashboard_projection_control",
        "dashboard_projection_events",
        "dashboard_projection_reconciliation_queue",
        "dashboard_strategy_lifetime_projection_states",
        "dashboard_strategy_recent_projection_states",
        "dashboard_strategy_recent_projection_facts",
        "dashboard_strategy_position_projection_facts",
        "date_dependent_strategy_hourly_paper_pnl",
        "paper_orders",
        "paper_fills",
        "strategy_market_paper_runs",
        "strategy_live_retention_guards",
        "strategy_paper_skip_rollups",
        "strategy_market_paper_skip_tombstones",
        "strategy_child_parent_assignments",
        "paper_positions",
        "paper_position_settlements",
        "paper_copied_trader_performance",
        "paper_copied_trader_performance_refresh_queue",
        "paper_copied_trader_performance_refresh_inflight",
        "paper_copied_trader_performance_projection_control",
        "btc_usd_reference_correlation_samples",
        "crypto_reference_price_ticks",
        "btc_order_book_lag_diagnostic_events",
        "btc_up_down_5m_strategy_stage_timings",
        "btc_up_down_5m_odds_ticks",
        "btc_5m_history",
        "btc_5m_history_live_observations",
        "btc_up_down_5m_statistics_ticks",
        "btc_up_down_5m_arbitrage_scans",
        "btc_up_down_5m_result_streak_diagnostics",
        "crypto_up_down_5m_odds_ticks",
        "crypto_up_down_5m_diff_snapshots",
        "crypto_up_down_5m_diff_shift_progress_states",
        "crypto_up_down_5m_result_polling_observations",
        "crypto_up_down_5m_websocket_resolved_markets",
        "market_resolved_event_diagnostics",
        "market_websocket_frame_diagnostics",
        "paper_copied_leader_positions",
        "paper_copied_leader_activity_events",
        "dry_run_orders",
        "live_orders",
        "paper_live_shadow_decisions",
        "paper_live_shadow_discrepancies",
        "live_trading_events",
        "risk_events",
        "market_data_status",
        "market_data_events",
        "polymarket_websocket_trade_ticks",
        "pinned_market_assets",
        "daily_reports",
        "bot_settings",
        "service_command_audit",
        "api_errors",
        "polymarket_http_logs",
        "polymarket_onchain_logs",
        "polymarket_onchain_fills",
        "polymarket_onchain_trade_captures",
        "polymarket_onchain_paper_signal_results",
        "polymarket_onchain_wallet_fills",
        "polymarket_onchain_wallet_executions",
        "polymarket_onchain_token_metadata",
        "polymarket_onchain_token_metadata_refresh_queue",
        "polymarket_onchain_wallet_activity",
        "polymarket_onchain_wallet_activity_refresh_queue",
        "polymarket_onchain_wallet_positions",
        "polymarket_onchain_position_refresh_queue",
        "polymarket_onchain_wallet_performance",
        "polymarket_onchain_wallet_performance_refresh_queue",
        "polymarket_onchain_wallet_category_performance",
        "polymarket_onchain_wallet_category_performance_refresh_queue",
        "polymarket_onchain_signal_candidate_refresh_queue",
        "polymarket_onchain_signal_candidate_backfill_cursors",
        "polymarket_onchain_signal_candidates",
        "polymarket_onchain_signal_candidate_reasons",
        "polymarket_onchain_trade_details",
        "polymarket_onchain_participant_details",
        "polymarket_onchain_ingest_cursors",
        "polymarket_onchain_trade_capture_cursors",
        "scanner_status",
        "service_heartbeats"
    ];

    private const string BaseSchemaSql = """
CREATE TABLE IF NOT EXISTS traders (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    wallet text NOT NULL UNIQUE,
    enabled boolean NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS trader_rules (
    id uuid PRIMARY KEY,
    trader_wallet text NOT NULL,
    allowed_categories jsonb NOT NULL,
    max_lag_seconds integer NOT NULL,
    max_slippage_cents numeric(18,8) NOT NULL,
    max_spread_cents numeric(18,8) NOT NULL,
    max_spread_pct numeric(18,8) NOT NULL,
    min_leader_trade_usd numeric(18,8) NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS trader_leaderboard_snapshots (
    id uuid PRIMARY KEY,
    discovery_run_id uuid NOT NULL,
    category text NOT NULL,
    time_period text NOT NULL,
    wallet text NOT NULL,
    user_name text NOT NULL,
    x_username text NULL,
    verified_badge boolean NOT NULL,
    pnl_rank integer NULL,
    pnl_page_offset integer NULL,
    pnl_leaderboard_pnl numeric(28,8) NULL,
    pnl_leaderboard_volume numeric(28,8) NULL,
    pnl_snapshot_at_utc timestamptz NULL,
    volume_rank integer NULL,
    volume_page_offset integer NULL,
    volume_leaderboard_pnl numeric(28,8) NULL,
    volume_leaderboard_volume numeric(28,8) NULL,
    volume_snapshot_at_utc timestamptz NULL,
    updated_at_utc timestamptz NOT NULL
);

ALTER TABLE trader_leaderboard_snapshots ADD COLUMN IF NOT EXISTS pnl_rank integer NULL;
ALTER TABLE trader_leaderboard_snapshots ADD COLUMN IF NOT EXISTS pnl_page_offset integer NULL;
ALTER TABLE trader_leaderboard_snapshots ADD COLUMN IF NOT EXISTS pnl_leaderboard_pnl numeric(28,8) NULL;
ALTER TABLE trader_leaderboard_snapshots ADD COLUMN IF NOT EXISTS pnl_leaderboard_volume numeric(28,8) NULL;
ALTER TABLE trader_leaderboard_snapshots ADD COLUMN IF NOT EXISTS pnl_snapshot_at_utc timestamptz NULL;
ALTER TABLE trader_leaderboard_snapshots ADD COLUMN IF NOT EXISTS volume_rank integer NULL;
ALTER TABLE trader_leaderboard_snapshots ADD COLUMN IF NOT EXISTS volume_page_offset integer NULL;
ALTER TABLE trader_leaderboard_snapshots ADD COLUMN IF NOT EXISTS volume_leaderboard_pnl numeric(28,8) NULL;
ALTER TABLE trader_leaderboard_snapshots ADD COLUMN IF NOT EXISTS volume_leaderboard_volume numeric(28,8) NULL;
ALTER TABLE trader_leaderboard_snapshots ADD COLUMN IF NOT EXISTS volume_snapshot_at_utc timestamptz NULL;
ALTER TABLE trader_leaderboard_snapshots ADD COLUMN IF NOT EXISTS updated_at_utc timestamptz NULL;
UPDATE trader_leaderboard_snapshots
SET updated_at_utc = COALESCE(updated_at_utc, now());
ALTER TABLE trader_leaderboard_snapshots ALTER COLUMN updated_at_utc SET NOT NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'trader_leaderboard_snapshots'
          AND column_name = 'order_by'
    ) THEN
        UPDATE trader_leaderboard_snapshots
        SET pnl_rank = rank,
            pnl_page_offset = page_offset,
            pnl_leaderboard_pnl = leaderboard_pnl,
            pnl_leaderboard_volume = leaderboard_volume,
            pnl_snapshot_at_utc = snapshot_at_utc
        WHERE order_by = 'PNL'
          AND pnl_leaderboard_pnl IS NULL;

        UPDATE trader_leaderboard_snapshots
        SET volume_rank = rank,
            volume_page_offset = page_offset,
            volume_leaderboard_pnl = leaderboard_pnl,
            volume_leaderboard_volume = leaderboard_volume,
            volume_snapshot_at_utc = snapshot_at_utc
        WHERE order_by = 'VOL'
          AND volume_leaderboard_pnl IS NULL;

        DROP TABLE IF EXISTS trader_leaderboard_snapshot_keep;
        CREATE TEMP TABLE trader_leaderboard_snapshot_keep AS
        SELECT DISTINCT ON (category, time_period, wallet)
            id AS keep_id,
            category,
            time_period,
            wallet
        FROM trader_leaderboard_snapshots
        ORDER BY category, time_period, wallet, updated_at_utc DESC, id;

        UPDATE trader_leaderboard_snapshots target
        SET pnl_rank = pnl.pnl_rank,
            pnl_page_offset = pnl.pnl_page_offset,
            pnl_leaderboard_pnl = pnl.pnl_leaderboard_pnl,
            pnl_leaderboard_volume = pnl.pnl_leaderboard_volume,
            pnl_snapshot_at_utc = pnl.pnl_snapshot_at_utc
        FROM trader_leaderboard_snapshot_keep keep
        JOIN LATERAL (
            SELECT source.pnl_rank,
                   source.pnl_page_offset,
                   source.pnl_leaderboard_pnl,
                   source.pnl_leaderboard_volume,
                   source.pnl_snapshot_at_utc
            FROM trader_leaderboard_snapshots source
            WHERE source.category = keep.category
              AND source.time_period = keep.time_period
              AND source.wallet = keep.wallet
              AND source.pnl_leaderboard_pnl IS NOT NULL
            ORDER BY source.pnl_snapshot_at_utc DESC NULLS LAST, source.updated_at_utc DESC, source.id
            LIMIT 1
        ) pnl ON true
        WHERE target.id = keep.keep_id;

        UPDATE trader_leaderboard_snapshots target
        SET volume_rank = volume.volume_rank,
            volume_page_offset = volume.volume_page_offset,
            volume_leaderboard_pnl = volume.volume_leaderboard_pnl,
            volume_leaderboard_volume = volume.volume_leaderboard_volume,
            volume_snapshot_at_utc = volume.volume_snapshot_at_utc
        FROM trader_leaderboard_snapshot_keep keep
        JOIN LATERAL (
            SELECT source.volume_rank,
                   source.volume_page_offset,
                   source.volume_leaderboard_pnl,
                   source.volume_leaderboard_volume,
                   source.volume_snapshot_at_utc
            FROM trader_leaderboard_snapshots source
            WHERE source.category = keep.category
              AND source.time_period = keep.time_period
              AND source.wallet = keep.wallet
              AND source.volume_leaderboard_pnl IS NOT NULL
            ORDER BY source.volume_snapshot_at_utc DESC NULLS LAST, source.updated_at_utc DESC, source.id
            LIMIT 1
        ) volume ON true
        WHERE target.id = keep.keep_id;

        DELETE FROM trader_leaderboard_snapshots target
        USING trader_leaderboard_snapshot_keep keep
        WHERE target.category = keep.category
          AND target.time_period = keep.time_period
          AND target.wallet = keep.wallet
          AND target.id <> keep.keep_id;

        DROP TABLE IF EXISTS trader_leaderboard_snapshot_keep;
    END IF;
END $$;

DROP INDEX IF EXISTS ux_trader_leaderboard_snapshots_run_order_wallet;
DROP INDEX IF EXISTS ix_trader_leaderboard_snapshots_run;

ALTER TABLE trader_leaderboard_snapshots DROP COLUMN IF EXISTS order_by;
ALTER TABLE trader_leaderboard_snapshots DROP COLUMN IF EXISTS page_offset;
ALTER TABLE trader_leaderboard_snapshots DROP COLUMN IF EXISTS rank;
ALTER TABLE trader_leaderboard_snapshots DROP COLUMN IF EXISTS leaderboard_pnl;
ALTER TABLE trader_leaderboard_snapshots DROP COLUMN IF EXISTS leaderboard_volume;
ALTER TABLE trader_leaderboard_snapshots DROP COLUMN IF EXISTS snapshot_at_utc;

CREATE UNIQUE INDEX IF NOT EXISTS ux_trader_leaderboard_snapshots_current
ON trader_leaderboard_snapshots(category, time_period, wallet);

CREATE INDEX IF NOT EXISTS ix_trader_leaderboard_snapshots_pnl
ON trader_leaderboard_snapshots(category, time_period, pnl_leaderboard_pnl DESC);

CREATE INDEX IF NOT EXISTS ix_trader_leaderboard_snapshots_volume_loss
ON trader_leaderboard_snapshots(category, time_period, volume_leaderboard_pnl ASC, volume_leaderboard_volume DESC);

CREATE TABLE IF NOT EXISTS trader_discovery_candidates (
    id uuid PRIMARY KEY,
    discovery_type text NOT NULL,
    category text NOT NULL,
    time_period text NOT NULL,
    rank integer NULL,
    wallet text NOT NULL,
    user_name text NOT NULL,
    x_username text NULL,
    leaderboard_pnl numeric(28,8) NOT NULL,
    leaderboard_volume numeric(28,8) NOT NULL,
    all_time_pnl numeric(28,8) NULL,
    all_time_volume numeric(28,8) NULL,
    verified_badge boolean NOT NULL,
    trades_fetched integer NOT NULL,
    buy_trades integer NOT NULL,
    sell_trades integer NOT NULL,
    recent_trade_volume_usd numeric(28,8) NOT NULL,
    average_trade_usd numeric(28,8) NOT NULL,
    last_trade_utc timestamptz NULL,
    positions_fetched integer NOT NULL,
    open_position_value_usd numeric(28,8) NOT NULL,
    open_position_cash_pnl_usd numeric(28,8) NOT NULL,
    open_position_realized_pnl_usd numeric(28,8) NOT NULL,
    notes text NOT NULL,
    snapshot_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

ALTER TABLE trader_discovery_candidates ADD COLUMN IF NOT EXISTS all_time_pnl numeric(28,8) NULL;
ALTER TABLE trader_discovery_candidates ADD COLUMN IF NOT EXISTS all_time_volume numeric(28,8) NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_trader_discovery_current
ON trader_discovery_candidates(discovery_type, category, time_period, wallet);

CREATE INDEX IF NOT EXISTS ix_trader_discovery_rank
ON trader_discovery_candidates(discovery_type, category, time_period, leaderboard_pnl DESC);

CREATE TABLE IF NOT EXISTS polymarket_category_mappings (
    local_category text PRIMARY KEY,
    polymarket_leaderboard_category text NOT NULL,
    enabled boolean NOT NULL DEFAULT true,
    notes text NULL,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_polymarket_category_mappings_leaderboard_category
        CHECK (polymarket_leaderboard_category IN (
            'OVERALL',
            'POLITICS',
            'SPORTS',
            'CRYPTO',
            'CULTURE',
            'MENTIONS',
            'WEATHER',
            'ECONOMICS',
            'TECH',
            'FINANCE'
        ))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_polymarket_category_mappings_local_lower
ON polymarket_category_mappings (lower(local_category));

CREATE INDEX IF NOT EXISTS ix_polymarket_category_mappings_leaderboard
ON polymarket_category_mappings(polymarket_leaderboard_category);

INSERT INTO polymarket_category_mappings (
    local_category,
    polymarket_leaderboard_category,
    enabled,
    notes,
    created_at_utc,
    updated_at_utc
) VALUES
    ('Politics', 'POLITICS', true, 'Seed obvious mapping.', now(), now()),
    ('Sports', 'SPORTS', true, 'Seed obvious mapping.', now(), now()),
    ('Crypto', 'CRYPTO', true, 'Seed obvious mapping.', now(), now()),
    ('Culture', 'CULTURE', true, 'Seed obvious mapping.', now(), now()),
    ('Pop Culture', 'CULTURE', true, 'Seed obvious mapping.', now(), now()),
    ('Mentions', 'MENTIONS', true, 'Seed obvious mapping.', now(), now()),
    ('Weather', 'WEATHER', true, 'Seed obvious mapping.', now(), now()),
    ('Economics', 'ECONOMICS', true, 'Seed obvious mapping.', now(), now()),
    ('Tech', 'TECH', true, 'Seed obvious mapping.', now(), now()),
    ('Finance', 'FINANCE', true, 'Seed obvious mapping.', now(), now())
ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS polymarket_data_api_traders (
    wallet text PRIMARY KEY,
    name text NOT NULL,
    pseudonym text NULL,
    bio text NULL,
    profile_image text NULL,
    profile_image_optimized text NULL,
    first_seen_at_utc timestamptz NOT NULL,
    last_seen_at_utc timestamptz NOT NULL,
    last_global_seen_at_utc timestamptz NULL,
    last_full_sync_at_utc timestamptz NULL,
    last_incremental_sync_at_utc timestamptz NULL,
    last_trade_timestamp_utc timestamptz NULL,
    full_sync_completed boolean NOT NULL DEFAULT false,
    full_sync_trades_fetched integer NOT NULL DEFAULT 0,
    full_sync_trades_inserted integer NOT NULL DEFAULT 0,
    incremental_sync_count integer NOT NULL DEFAULT 0,
    polymarket_rating_refreshed_at_utc timestamptz NULL,
    polymarket_rating_next_refresh_at_utc timestamptz NOT NULL DEFAULT now(),
    polymarket_rating_refresh_attempts integer NOT NULL DEFAULT 0,
    polymarket_rating_last_error text NULL,
    updated_at_utc timestamptz NOT NULL
);

ALTER TABLE polymarket_data_api_traders
    ADD COLUMN IF NOT EXISTS polymarket_rating_refreshed_at_utc timestamptz NULL,
    ADD COLUMN IF NOT EXISTS polymarket_rating_next_refresh_at_utc timestamptz NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS polymarket_rating_refresh_attempts integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS polymarket_rating_last_error text NULL;

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_traders_last_seen
ON polymarket_data_api_traders(last_seen_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_traders_last_trade
ON polymarket_data_api_traders(last_trade_timestamp_utc DESC NULLS LAST);

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_traders_rating_next
ON polymarket_data_api_traders(polymarket_rating_next_refresh_at_utc, last_seen_at_utc DESC);

CREATE TABLE IF NOT EXISTS polymarket_data_api_positions (
    id uuid PRIMARY KEY,
    wallet text NOT NULL,
    position_status text NOT NULL,
    asset_id text NOT NULL,
    condition_id text NOT NULL,
    size numeric(28,8) NULL,
    avg_price numeric(18,8) NOT NULL,
    initial_value_usd numeric(28,8) NULL,
    current_value_usd numeric(28,8) NULL,
    cash_pnl_usd numeric(28,8) NULL,
    percent_pnl numeric(18,8) NULL,
    total_bought numeric(28,8) NOT NULL,
    realized_pnl_usd numeric(28,8) NOT NULL,
    percent_realized_pnl numeric(18,8) NULL,
    cur_price numeric(18,8) NOT NULL,
    timestamp_utc timestamptz NULL,
    market_title text NOT NULL,
    market_slug text NOT NULL,
    icon text NULL,
    event_id text NULL,
    event_slug text NULL,
    category text NULL,
    outcome text NOT NULL,
    outcome_index integer NULL,
    opposite_outcome text NULL,
    opposite_asset text NULL,
    end_date_utc timestamptz NULL,
    redeemable boolean NULL,
    mergeable boolean NULL,
    negative_risk boolean NULL,
    raw_json jsonb NOT NULL,
    fetched_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

ALTER TABLE polymarket_data_api_positions ADD COLUMN IF NOT EXISTS category text NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_polymarket_data_api_positions_wallet_status_asset
ON polymarket_data_api_positions(wallet, position_status, asset_id);

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_positions_wallet
ON polymarket_data_api_positions(wallet, position_status, updated_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_positions_condition
ON polymarket_data_api_positions(condition_id);

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_positions_category
ON polymarket_data_api_positions(category);

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_positions_timestamp
ON polymarket_data_api_positions(timestamp_utc DESC NULLS LAST);

CREATE TABLE IF NOT EXISTS polymarket_auto_redeem_attempts (
    id uuid PRIMARY KEY,
    wallet text NOT NULL,
    proxy_wallet text NULL,
    condition_id text NOT NULL,
    asset_id text NOT NULL,
    market_slug text NOT NULL,
    market_title text NOT NULL,
    outcome text NOT NULL,
    outcome_index integer NULL,
    redeemable_value_usd numeric(28,8) NULL,
    size numeric(28,8) NULL,
    status text NOT NULL,
    dry_run boolean NOT NULL,
    auto_submit_enabled boolean NOT NULL,
    target_contract text NOT NULL DEFAULT '',
    calldata text NOT NULL,
    collateral_token text NOT NULL,
    parent_collection_id text NOT NULL,
    index_sets_json jsonb NOT NULL,
    relayer_transaction_id text NULL,
    transaction_hash text NULL,
    last_error text NULL,
    detected_at_utc timestamptz NOT NULL,
    last_seen_at_utc timestamptz NOT NULL,
    submitted_at_utc timestamptz NULL,
    confirmed_at_utc timestamptz NULL,
    updated_at_utc timestamptz NOT NULL,
    raw_position_json jsonb NOT NULL
);

ALTER TABLE polymarket_auto_redeem_attempts ADD COLUMN IF NOT EXISTS target_contract text NOT NULL DEFAULT '';

CREATE UNIQUE INDEX IF NOT EXISTS ux_polymarket_auto_redeem_attempts_wallet_condition
ON polymarket_auto_redeem_attempts(wallet, condition_id);

CREATE INDEX IF NOT EXISTS ix_polymarket_auto_redeem_attempts_status_updated
ON polymarket_auto_redeem_attempts(status, updated_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_auto_redeem_attempts_detected
ON polymarket_auto_redeem_attempts(detected_at_utc DESC);

CREATE TABLE IF NOT EXISTS polymarket_data_api_wallet_performance (
    wallet text PRIMARY KEY,
    positions_count integer NOT NULL,
    open_positions integer NOT NULL,
    closed_positions integer NOT NULL,
    profitable_positions integer NOT NULL,
    losing_positions integer NOT NULL,
    markets_traded integer NOT NULL,
    outcomes_traded integer NOT NULL,
    volume_usd numeric(28,8) NOT NULL,
    open_initial_value_usd numeric(28,8) NOT NULL,
    open_current_value_usd numeric(28,8) NOT NULL,
    open_cash_pnl_usd numeric(28,8) NOT NULL,
    open_realized_pnl_usd numeric(28,8) NOT NULL,
    closed_cost_basis_usd numeric(28,8) NOT NULL,
    closed_realized_pnl_usd numeric(28,8) NOT NULL,
    total_cost_basis_usd numeric(28,8) NOT NULL,
    total_current_value_usd numeric(28,8) NOT NULL,
    total_pnl_usd numeric(28,8) NOT NULL,
    realized_pnl_usd numeric(28,8) NOT NULL,
    roi_pct numeric(18,8) NOT NULL,
    win_rate_pct numeric(18,8) NOT NULL,
    average_position_size_usd numeric(28,8) NOT NULL,
    score numeric(28,8) NOT NULL,
    sample_quality text NOT NULL,
    last_position_timestamp_utc timestamptz NULL,
    polymarket_positions_open_cash_pnl_usd numeric(28,8) NULL,
    polymarket_positions_open_realized_pnl_usd numeric(28,8) NULL,
    polymarket_positions_open_current_value_usd numeric(28,8) NULL,
    polymarket_positions_closed_realized_pnl_usd numeric(28,8) NULL,
    polymarket_positions_total_pnl_usd numeric(28,8) NULL,
    polymarket_positions_refreshed_at_utc timestamptz NULL,
    polymarket_leaderboard_pnl_usd numeric(28,8) NULL,
    polymarket_leaderboard_volume_usd numeric(28,8) NULL,
    polymarket_leaderboard_rank integer NULL,
    polymarket_leaderboard_category text NULL,
    polymarket_leaderboard_time_period text NULL,
    polymarket_leaderboard_refreshed_at_utc timestamptz NULL,
    refreshed_at_utc timestamptz NOT NULL
);

ALTER TABLE polymarket_data_api_wallet_performance
    ADD COLUMN IF NOT EXISTS polymarket_positions_open_cash_pnl_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_positions_open_realized_pnl_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_positions_open_current_value_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_positions_closed_realized_pnl_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_positions_total_pnl_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_positions_refreshed_at_utc timestamptz NULL,
    ADD COLUMN IF NOT EXISTS polymarket_leaderboard_pnl_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_leaderboard_volume_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_leaderboard_rank integer NULL,
    ADD COLUMN IF NOT EXISTS polymarket_leaderboard_category text NULL,
    ADD COLUMN IF NOT EXISTS polymarket_leaderboard_time_period text NULL,
    ADD COLUMN IF NOT EXISTS polymarket_leaderboard_refreshed_at_utc timestamptz NULL;

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_wallet_performance_score
ON polymarket_data_api_wallet_performance(score DESC, total_pnl_usd DESC, volume_usd DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_wallet_performance_pnl
ON polymarket_data_api_wallet_performance(total_pnl_usd DESC, volume_usd DESC);

CREATE TABLE IF NOT EXISTS polymarket_data_api_wallet_category_performance (
    wallet text NOT NULL,
    category text NOT NULL,
    positions_count integer NOT NULL,
    open_positions integer NOT NULL,
    closed_positions integer NOT NULL,
    profitable_positions integer NOT NULL,
    losing_positions integer NOT NULL,
    markets_traded integer NOT NULL,
    outcomes_traded integer NOT NULL,
    volume_usd numeric(28,8) NOT NULL,
    open_initial_value_usd numeric(28,8) NOT NULL,
    open_current_value_usd numeric(28,8) NOT NULL,
    open_cash_pnl_usd numeric(28,8) NOT NULL,
    open_realized_pnl_usd numeric(28,8) NOT NULL,
    closed_cost_basis_usd numeric(28,8) NOT NULL,
    closed_realized_pnl_usd numeric(28,8) NOT NULL,
    total_cost_basis_usd numeric(28,8) NOT NULL,
    total_current_value_usd numeric(28,8) NOT NULL,
    total_pnl_usd numeric(28,8) NOT NULL,
    realized_pnl_usd numeric(28,8) NOT NULL,
    roi_pct numeric(18,8) NOT NULL,
    win_rate_pct numeric(18,8) NOT NULL,
    average_position_size_usd numeric(28,8) NOT NULL,
    score numeric(28,8) NOT NULL,
    sample_quality text NOT NULL,
    last_position_timestamp_utc timestamptz NULL,
    polymarket_positions_open_cash_pnl_usd numeric(28,8) NULL,
    polymarket_positions_open_realized_pnl_usd numeric(28,8) NULL,
    polymarket_positions_open_current_value_usd numeric(28,8) NULL,
    polymarket_positions_closed_realized_pnl_usd numeric(28,8) NULL,
    polymarket_positions_total_pnl_usd numeric(28,8) NULL,
    polymarket_positions_refreshed_at_utc timestamptz NULL,
    polymarket_leaderboard_pnl_usd numeric(28,8) NULL,
    polymarket_leaderboard_volume_usd numeric(28,8) NULL,
    polymarket_leaderboard_rank integer NULL,
    polymarket_leaderboard_category text NULL,
    polymarket_leaderboard_time_period text NULL,
    polymarket_leaderboard_refreshed_at_utc timestamptz NULL,
    refreshed_at_utc timestamptz NOT NULL,
    PRIMARY KEY (wallet, category)
);

ALTER TABLE polymarket_data_api_wallet_category_performance
    ADD COLUMN IF NOT EXISTS polymarket_positions_open_cash_pnl_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_positions_open_realized_pnl_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_positions_open_current_value_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_positions_closed_realized_pnl_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_positions_total_pnl_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_positions_refreshed_at_utc timestamptz NULL,
    ADD COLUMN IF NOT EXISTS polymarket_leaderboard_pnl_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_leaderboard_volume_usd numeric(28,8) NULL,
    ADD COLUMN IF NOT EXISTS polymarket_leaderboard_rank integer NULL,
    ADD COLUMN IF NOT EXISTS polymarket_leaderboard_category text NULL,
    ADD COLUMN IF NOT EXISTS polymarket_leaderboard_time_period text NULL,
    ADD COLUMN IF NOT EXISTS polymarket_leaderboard_refreshed_at_utc timestamptz NULL;

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_wallet_category_performance_score
ON polymarket_data_api_wallet_category_performance(category, score DESC, total_pnl_usd DESC, volume_usd DESC);

CREATE TABLE IF NOT EXISTS polymarket_data_api_wallet_category_ratings (
    wallet text NOT NULL,
    local_category text NOT NULL,
    polymarket_category text NOT NULL,
    time_period text NOT NULL,
    order_by text NOT NULL,
    found boolean NOT NULL,
    leaderboard_rank integer NULL,
    user_name text NULL,
    x_username text NULL,
    profile_image text NULL,
    verified_badge boolean NOT NULL DEFAULT false,
    leaderboard_pnl_usd numeric(28,8) NULL,
    leaderboard_volume_usd numeric(28,8) NULL,
    leaderboard_pnl_to_volume_pct numeric(18,8) NULL,
    current_positions_count integer NOT NULL DEFAULT 0,
    current_positions_initial_value_usd numeric(28,8) NOT NULL DEFAULT 0,
    current_positions_current_value_usd numeric(28,8) NOT NULL DEFAULT 0,
    current_positions_cash_pnl_usd numeric(28,8) NOT NULL DEFAULT 0,
    current_positions_realized_pnl_usd numeric(28,8) NOT NULL DEFAULT 0,
    current_positions_total_pnl_usd numeric(28,8) NOT NULL DEFAULT 0,
    current_positions_percent_pnl numeric(18,8) NULL,
    current_positions_percent_realized_pnl numeric(18,8) NULL,
    closed_positions_count integer NOT NULL DEFAULT 0,
    closed_positions_cost_basis_usd numeric(28,8) NOT NULL DEFAULT 0,
    closed_positions_realized_pnl_usd numeric(28,8) NOT NULL DEFAULT 0,
    closed_positions_percent_realized_pnl numeric(18,8) NULL,
    positions_total_cost_basis_usd numeric(28,8) NOT NULL DEFAULT 0,
    positions_total_pnl_usd numeric(28,8) NOT NULL DEFAULT 0,
    positions_total_percent_pnl numeric(18,8) NULL,
    positions_refreshed_at_utc timestamptz NULL,
    raw_json jsonb NOT NULL,
    refreshed_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    PRIMARY KEY (wallet, local_category, polymarket_category, time_period, order_by)
);

ALTER TABLE polymarket_data_api_wallet_category_ratings
    ADD COLUMN IF NOT EXISTS leaderboard_pnl_to_volume_pct numeric(18,8) NULL,
    ADD COLUMN IF NOT EXISTS current_positions_count integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS current_positions_initial_value_usd numeric(28,8) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS current_positions_current_value_usd numeric(28,8) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS current_positions_cash_pnl_usd numeric(28,8) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS current_positions_realized_pnl_usd numeric(28,8) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS current_positions_total_pnl_usd numeric(28,8) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS current_positions_percent_pnl numeric(18,8) NULL,
    ADD COLUMN IF NOT EXISTS current_positions_percent_realized_pnl numeric(18,8) NULL,
    ADD COLUMN IF NOT EXISTS closed_positions_count integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS closed_positions_cost_basis_usd numeric(28,8) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS closed_positions_realized_pnl_usd numeric(28,8) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS closed_positions_percent_realized_pnl numeric(18,8) NULL,
    ADD COLUMN IF NOT EXISTS positions_total_cost_basis_usd numeric(28,8) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS positions_total_pnl_usd numeric(28,8) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS positions_total_percent_pnl numeric(18,8) NULL,
    ADD COLUMN IF NOT EXISTS positions_refreshed_at_utc timestamptz NULL;

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_wallet_category_ratings_category_pnl
ON polymarket_data_api_wallet_category_ratings(polymarket_category, time_period, order_by, leaderboard_pnl_usd DESC NULLS LAST, leaderboard_volume_usd DESC NULLS LAST);

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_wallet_category_ratings_leaderboard_ratio
ON polymarket_data_api_wallet_category_ratings(polymarket_category, time_period, order_by, leaderboard_pnl_to_volume_pct DESC NULLS LAST, leaderboard_volume_usd DESC NULLS LAST);

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_wallet_category_ratings_positions_pnl
ON polymarket_data_api_wallet_category_ratings(polymarket_category, time_period, order_by, positions_total_pnl_usd DESC, positions_total_cost_basis_usd DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_wallet_category_ratings_wallet
ON polymarket_data_api_wallet_category_ratings(wallet, refreshed_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_data_api_wallet_category_ratings_lookup
ON polymarket_data_api_wallet_category_ratings(
    lower(wallet),
    lower(local_category),
    lower(polymarket_category),
    time_period,
    order_by
);

CREATE TABLE IF NOT EXISTS polymarket_gamma_markets (
    market_id text PRIMARY KEY,
    condition_id text NOT NULL,
    question_id text NOT NULL,
    slug text NOT NULL,
    question text NOT NULL,
    event_id text NULL,
    event_slug text NULL,
    event_title text NULL,
    series_slug text NULL,
    category text NULL,
    active boolean NOT NULL,
    closed boolean NOT NULL,
    archived boolean NOT NULL,
    restricted boolean NOT NULL,
    accepting_orders boolean NOT NULL,
    enable_order_book boolean NOT NULL,
    negative_risk boolean NOT NULL,
    liquidity numeric(28,8) NULL,
    liquidity_clob numeric(28,8) NULL,
    volume numeric(28,8) NULL,
    volume_24hr numeric(28,8) NULL,
    best_bid numeric(18,8) NULL,
    best_ask numeric(18,8) NULL,
    spread numeric(18,8) NULL,
    last_trade_price numeric(18,8) NULL,
    order_min_size numeric(28,8) NULL,
    order_price_min_tick_size numeric(18,8) NULL,
    created_at_utc timestamptz NULL,
    updated_at_utc timestamptz NULL,
    start_date_utc timestamptz NULL,
    end_date_utc timestamptz NULL,
    event_start_time_utc timestamptz NULL,
    outcomes_json jsonb NOT NULL,
    clob_token_ids_json jsonb NOT NULL,
    raw_json jsonb NOT NULL,
    fetched_at_utc timestamptz NOT NULL
);

ALTER TABLE polymarket_gamma_markets ADD COLUMN IF NOT EXISTS last_trade_price numeric(18,8) NULL;
ALTER TABLE polymarket_gamma_markets ADD COLUMN IF NOT EXISTS order_min_size numeric(28,8) NULL;
ALTER TABLE polymarket_gamma_markets ADD COLUMN IF NOT EXISTS order_price_min_tick_size numeric(18,8) NULL;

CREATE INDEX IF NOT EXISTS ix_polymarket_gamma_markets_created
ON polymarket_gamma_markets(created_at_utc DESC NULLS LAST, market_id DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_gamma_markets_condition
ON polymarket_gamma_markets(condition_id);

CREATE INDEX IF NOT EXISTS ix_polymarket_gamma_markets_slug
ON polymarket_gamma_markets(slug);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_polymarket_gamma_markets_active_end_date
ON polymarket_gamma_markets(end_date_utc)
WHERE active AND NOT archived;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_polymarket_gamma_markets_active_event_start
ON polymarket_gamma_markets(event_start_time_utc)
WHERE active AND NOT archived;

CREATE INDEX IF NOT EXISTS ix_polymarket_gamma_markets_event
ON polymarket_gamma_markets(event_id);

CREATE INDEX IF NOT EXISTS ix_polymarket_gamma_markets_clob_token_ids
ON polymarket_gamma_markets USING gin(clob_token_ids_json);

CREATE TABLE IF NOT EXISTS leader_trades (
    id uuid PRIMARY KEY,
    trader_wallet text NOT NULL,
    trader_name text NOT NULL,
    condition_id text NOT NULL,
    asset_id text NOT NULL,
    market_slug text NOT NULL,
    market_title text NOT NULL,
    outcome text NOT NULL,
    side text NOT NULL,
    price numeric(18,8) NOT NULL,
    size numeric(28,8) NOT NULL,
    cash_value_usd numeric(28,8) NOT NULL,
    timestamp_utc timestamptz NOT NULL,
    transaction_hash text NULL,
    dedup_key text NOT NULL,
    raw_json jsonb NULL,
    created_at_utc timestamptz NOT NULL
);

ALTER TABLE leader_trades ADD COLUMN IF NOT EXISTS dedup_key text;
UPDATE leader_trades
SET dedup_key =
    CASE
        WHEN transaction_hash IS NOT NULL AND btrim(transaction_hash) <> '' THEN
            lower(concat(
                'wallet:', btrim(trader_wallet),
                '|tx:', btrim(transaction_hash),
                '|asset:', btrim(asset_id),
                '|side:', side,
                '|ts:', extract(epoch from timestamp_utc)::bigint
            ))
        ELSE
            lower(concat(
                'wallet:', btrim(trader_wallet),
                '|fallback|asset:', btrim(asset_id),
                '|side:', side,
                '|ts:', extract(epoch from timestamp_utc)::bigint,
                '|price:', price,
                '|size:', size
            ))
    END
WHERE dedup_key IS NULL OR dedup_key = '';
ALTER TABLE leader_trades ALTER COLUMN dedup_key SET NOT NULL;
DROP INDEX IF EXISTS ux_leader_trades_dedup;
CREATE UNIQUE INDEX IF NOT EXISTS ux_leader_trades_dedup
ON leader_trades(dedup_key);

CREATE TABLE IF NOT EXISTS leader_positions (
    id uuid PRIMARY KEY,
    trader_wallet text NOT NULL,
    condition_id text NOT NULL,
    asset_id text NOT NULL,
    outcome text NOT NULL,
    size numeric(28,8) NOT NULL,
    avg_price numeric(18,8) NOT NULL,
    initial_value numeric(28,8) NOT NULL,
    current_value numeric(28,8) NOT NULL,
    cash_pnl numeric(28,8) NOT NULL,
    percent_pnl numeric(18,8) NOT NULL,
    total_bought numeric(28,8) NOT NULL,
    realized_pnl numeric(28,8) NOT NULL,
    cur_price numeric(18,8) NOT NULL,
    title text NULL,
    market_slug text NULL,
    opposite_asset text NULL,
    end_date_utc timestamptz NULL,
    negative_risk boolean NOT NULL,
    snapshot_at_utc timestamptz NOT NULL,
    raw_json jsonb NULL
);

ALTER TABLE leader_positions ADD COLUMN IF NOT EXISTS initial_value numeric(28,8) NOT NULL DEFAULT 0;
ALTER TABLE leader_positions ADD COLUMN IF NOT EXISTS percent_pnl numeric(18,8) NOT NULL DEFAULT 0;
ALTER TABLE leader_positions ADD COLUMN IF NOT EXISTS total_bought numeric(28,8) NOT NULL DEFAULT 0;
ALTER TABLE leader_positions ADD COLUMN IF NOT EXISTS realized_pnl numeric(28,8) NOT NULL DEFAULT 0;
ALTER TABLE leader_positions ADD COLUMN IF NOT EXISTS title text NULL;
ALTER TABLE leader_positions ADD COLUMN IF NOT EXISTS market_slug text NULL;
ALTER TABLE leader_positions ADD COLUMN IF NOT EXISTS opposite_asset text NULL;
ALTER TABLE leader_positions ADD COLUMN IF NOT EXISTS end_date_utc timestamptz NULL;
ALTER TABLE leader_positions ADD COLUMN IF NOT EXISTS negative_risk boolean NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS markets (
    id uuid PRIMARY KEY,
    condition_id text NOT NULL UNIQUE,
    market_slug text NOT NULL,
    market_title text NOT NULL,
    category text NULL,
    end_date_utc timestamptz NULL,
    raw_json jsonb NULL,
    updated_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS order_book_snapshots (
    id uuid PRIMARY KEY,
    asset_id text NOT NULL,
    condition_id text NULL,
    best_bid numeric(18,8) NULL,
    best_ask numeric(18,8) NULL,
    spread_abs numeric(18,8) NULL,
    spread_pct numeric(18,8) NULL,
    raw_json jsonb NULL,
    snapshot_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_order_book_snapshots_asset_time
ON order_book_snapshots(asset_id, snapshot_at_utc DESC);

CREATE TABLE IF NOT EXISTS polymarket_websocket_trade_ticks (
    id uuid PRIMARY KEY,
    dedup_key text NOT NULL,
    asset_id text NOT NULL,
    condition_id text NULL,
    side text NOT NULL,
    price numeric(18,8) NULL,
    size numeric(28,8) NULL,
    trade_timestamp_utc timestamptz NOT NULL,
    transaction_hash text NULL,
    transaction_hash_present boolean NOT NULL,
    trader_match_status integer NOT NULL,
    trader_wallet text NULL,
    received_at_utc timestamptz NOT NULL,
    matched_at_utc timestamptz NULL,
    match_attempts integer NOT NULL,
    last_match_attempt_utc timestamptz NULL,
    last_match_error text NULL,
    matched_transaction_hash text NULL,
    match_details text NULL,
    raw_json jsonb NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

ALTER TABLE polymarket_websocket_trade_ticks ADD COLUMN IF NOT EXISTS dedup_key text NULL;
ALTER TABLE polymarket_websocket_trade_ticks ADD COLUMN IF NOT EXISTS transaction_hash_present boolean NOT NULL DEFAULT false;
ALTER TABLE polymarket_websocket_trade_ticks ADD COLUMN IF NOT EXISTS trader_match_status integer NOT NULL DEFAULT 1;
ALTER TABLE polymarket_websocket_trade_ticks ADD COLUMN IF NOT EXISTS trader_wallet text NULL;
ALTER TABLE polymarket_websocket_trade_ticks ADD COLUMN IF NOT EXISTS matched_at_utc timestamptz NULL;
ALTER TABLE polymarket_websocket_trade_ticks ADD COLUMN IF NOT EXISTS match_attempts integer NOT NULL DEFAULT 0;
ALTER TABLE polymarket_websocket_trade_ticks ADD COLUMN IF NOT EXISTS last_match_attempt_utc timestamptz NULL;
ALTER TABLE polymarket_websocket_trade_ticks ADD COLUMN IF NOT EXISTS last_match_error text NULL;
ALTER TABLE polymarket_websocket_trade_ticks ADD COLUMN IF NOT EXISTS matched_transaction_hash text NULL;
ALTER TABLE polymarket_websocket_trade_ticks ADD COLUMN IF NOT EXISTS match_details text NULL;
ALTER TABLE polymarket_websocket_trade_ticks ADD COLUMN IF NOT EXISTS updated_at_utc timestamptz NULL;
UPDATE polymarket_websocket_trade_ticks
SET dedup_key = COALESCE(
        NULLIF(dedup_key, ''),
        lower(concat(
            'fallback|condition:', COALESCE(condition_id, ''),
            '|asset:', asset_id,
            '|side:', side,
            '|ts:', extract(epoch from trade_timestamp_utc)::bigint,
            '|price:', COALESCE(price::text, ''),
            '|size:', COALESCE(size::text, '')
        ))),
    updated_at_utc = COALESCE(updated_at_utc, received_at_utc, now())
WHERE dedup_key IS NULL OR dedup_key = '' OR updated_at_utc IS NULL;
ALTER TABLE polymarket_websocket_trade_ticks ALTER COLUMN dedup_key SET NOT NULL;
ALTER TABLE polymarket_websocket_trade_ticks ALTER COLUMN updated_at_utc SET NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_polymarket_websocket_trade_ticks_dedup
ON polymarket_websocket_trade_ticks(dedup_key);

CREATE INDEX IF NOT EXISTS ix_polymarket_websocket_trade_ticks_received
ON polymarket_websocket_trade_ticks(received_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_websocket_trade_ticks_match_status
ON polymarket_websocket_trade_ticks(trader_match_status, received_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_websocket_trade_ticks_transaction_hash
ON polymarket_websocket_trade_ticks(transaction_hash);

CREATE TABLE IF NOT EXISTS signals (
    id uuid PRIMARY KEY,
    leader_trade_id uuid NULL REFERENCES leader_trades(id),
    trader_wallet text NOT NULL,
    condition_id text NOT NULL,
    asset_id text NOT NULL,
    outcome text NOT NULL,
    leader_price numeric(18,8) NOT NULL,
    best_bid numeric(18,8) NULL,
    best_ask numeric(18,8) NULL,
    spread_abs numeric(18,8) NULL,
    spread_pct numeric(18,8) NULL,
    lag_seconds integer NULL,
    score integer NOT NULL,
    accepted boolean NOT NULL DEFAULT false,
    decision text NOT NULL,
    proposed_paper_price numeric(18,8) NULL,
    proposed_size_shares numeric(28,8) NULL,
    proposed_notional_usd numeric(28,8) NULL,
    created_at_utc timestamptz NOT NULL,
    raw_context_json jsonb NULL
);

ALTER TABLE signals ADD COLUMN IF NOT EXISTS proposed_size_shares numeric(28,8) NULL;
ALTER TABLE signals ADD COLUMN IF NOT EXISTS proposed_notional_usd numeric(28,8) NULL;
ALTER TABLE signals ADD COLUMN IF NOT EXISTS accepted boolean NOT NULL DEFAULT false;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_signals_created_time
ON signals(created_at_utc DESC, id DESC);

CREATE TABLE IF NOT EXISTS signal_rejections (
    id uuid PRIMARY KEY,
    signal_id uuid NOT NULL REFERENCES signals(id),
    reason_code text NOT NULL,
    reason_details text NOT NULL,
    created_at_utc timestamptz NOT NULL
);

DO $$
BEGIN
    IF to_regclass('public.strategies') IS NULL
       AND to_regclass('public.copy_strategies') IS NOT NULL THEN
        ALTER TABLE public.copy_strategies RENAME TO strategies;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS strategies (
    id uuid PRIMARY KEY,
    code text NOT NULL UNIQUE,
    name text NOT NULL UNIQUE,
    description text NOT NULL DEFAULT '',
    enabled boolean NOT NULL DEFAULT true,
    live_stakes boolean NOT NULL DEFAULT false,
    auto_live_paused boolean NOT NULL DEFAULT false,
    auto_live_paused_at_utc timestamptz NULL,
    auto_live_pause_window_start_utc timestamptz NULL,
    paused boolean NOT NULL DEFAULT false,
    paused_until_utc timestamptz NULL,
    paper_stake_amount numeric(28,8) NOT NULL DEFAULT 1.00,
    live_stake_amount numeric(28,8) NOT NULL DEFAULT 1.00,
    paper_lost_coeff numeric(28,8) NOT NULL DEFAULT 1.00,
    live_lost_coeff numeric(28,8) NOT NULL DEFAULT 1.00,
    paper_lost_counter integer NOT NULL DEFAULT 0,
    live_lost_counter integer NOT NULL DEFAULT 0,
    live_available_balance numeric(28,8) NOT NULL DEFAULT 100.00,
    live_enabled_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_strategies_paper_stake_amount_positive CHECK (paper_stake_amount > 0),
    CONSTRAINT ck_strategies_live_stake_amount_positive CHECK (live_stake_amount > 0),
    CONSTRAINT ck_strategies_paper_lost_coeff_minimum CHECK (paper_lost_coeff >= 1),
    CONSTRAINT ck_strategies_live_lost_coeff_minimum CHECK (live_lost_coeff >= 1),
    CONSTRAINT ck_strategies_live_available_balance_nonnegative CHECK (live_available_balance >= 0),
    CONSTRAINT ck_strategies_live_available_balance_maximum CHECK (live_available_balance <= 100.00)
);

ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_stakes boolean NOT NULL DEFAULT false;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS auto_live_paused boolean NOT NULL DEFAULT false;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS auto_live_paused_at_utc timestamptz NULL;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS auto_live_pause_window_start_utc timestamptz NULL;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paused boolean NOT NULL DEFAULT false;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paused_until_utc timestamptz NULL;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paper_stake_amount numeric(28,8) NOT NULL DEFAULT 1.00;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_stake_amount numeric(28,8) NOT NULL DEFAULT 1.00;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paper_lost_coeff numeric(28,8) NOT NULL DEFAULT 1.00;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_lost_coeff numeric(28,8) NOT NULL DEFAULT 1.00;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paper_lost_counter integer NOT NULL DEFAULT 0;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_lost_counter integer NOT NULL DEFAULT 0;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_available_balance numeric(28,8) NOT NULL DEFAULT 100.00;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_enabled_at_utc timestamptz NULL;
ALTER TABLE strategies ALTER COLUMN live_stake_amount SET DEFAULT 1.00;
ALTER TABLE strategies ALTER COLUMN paper_lost_coeff SET DEFAULT 1.00;
ALTER TABLE strategies ALTER COLUMN live_lost_coeff SET DEFAULT 1.00;
ALTER TABLE strategies ALTER COLUMN paper_lost_counter SET DEFAULT 0;
ALTER TABLE strategies ALTER COLUMN live_lost_counter SET DEFAULT 0;
ALTER TABLE strategies DROP CONSTRAINT IF EXISTS ck_strategies_paper_lost_counter_nonnegative;
ALTER TABLE strategies DROP CONSTRAINT IF EXISTS ck_strategies_live_lost_counter_nonnegative;

UPDATE strategies
SET live_available_balance = 100.00,
    updated_at_utc = clock_timestamp()
WHERE live_available_balance > 100.00;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_strategies_paper_stake_amount_positive'
          AND conrelid = 'public.strategies'::regclass
    ) THEN
        ALTER TABLE strategies
            ADD CONSTRAINT ck_strategies_paper_stake_amount_positive CHECK (paper_stake_amount > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_strategies_live_stake_amount_positive'
          AND conrelid = 'public.strategies'::regclass
    ) THEN
        ALTER TABLE strategies
            ADD CONSTRAINT ck_strategies_live_stake_amount_positive CHECK (live_stake_amount > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_strategies_paper_lost_coeff_minimum'
          AND conrelid = 'public.strategies'::regclass
    ) THEN
        ALTER TABLE strategies
            ADD CONSTRAINT ck_strategies_paper_lost_coeff_minimum CHECK (paper_lost_coeff >= 1);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_strategies_live_lost_coeff_minimum'
          AND conrelid = 'public.strategies'::regclass
    ) THEN
        ALTER TABLE strategies
            ADD CONSTRAINT ck_strategies_live_lost_coeff_minimum CHECK (live_lost_coeff >= 1);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_strategies_live_available_balance_nonnegative'
          AND conrelid = 'public.strategies'::regclass
    ) THEN
        ALTER TABLE strategies
            ADD CONSTRAINT ck_strategies_live_available_balance_nonnegative CHECK (live_available_balance >= 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_strategies_live_available_balance_maximum'
          AND conrelid = 'public.strategies'::regclass
    ) THEN
        ALTER TABLE strategies
            ADD CONSTRAINT ck_strategies_live_available_balance_maximum CHECK (live_available_balance <= 100.00);
    END IF;
END $$;

UPDATE strategies
SET
    code = 'legacy_bps_seed_' || replace(id::text, '-', '_'),
    name = 'Legacy bps seed ' || id::text,
    updated_at_utc = now()
WHERE (
        code LIKE 'btc_up_down_5m_binance_bps_%'
     OR code LIKE 'sol_up_down_5m_binance_bps_%'
    )
  AND EXISTS (
        SELECT 1
        FROM strategies legacy_strategy
        WHERE legacy_strategy.code LIKE 'btc_up_down_5m_binance_bps_0_%'
           OR legacy_strategy.code LIKE 'sol_up_down_5m_binance_bps_0_%'
    );

INSERT INTO strategies (id, code, name, description, enabled, paper_stake_amount, created_at_utc, updated_at_utc)
WITH variants(direction_code, direction_name, opposite_direction_name, id_group) AS (
    VALUES
        ('up', 'Up', 'Down', '8031'),
        ('down', 'Down', 'Up', '8032')
),
thresholds(threshold_tenths) AS (
    SELECT generate_series(1, 50)
),
formatted AS (
    SELECT
        variants.direction_code,
        variants.direction_name,
        variants.opposite_direction_name,
        variants.id_group,
        thresholds.threshold_tenths,
        thresholds.threshold_tenths::text AS threshold_name,
        thresholds.threshold_tenths::text AS code_suffix
    FROM variants
    CROSS JOIN thresholds
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad((100 + threshold_tenths)::text, 12, '0'))::uuid,
    'btc_up_down_5m_' || direction_code || '_bps_' || code_suffix || '_instant',
    'BTC Up or Down 5m ' || direction_name || ' ' || threshold_name || ' bps Instant',
    'Immediately after BTC 5m market open, use the previous close-book result streak and archived Binance BTC start/end move gate; enter only when the cumulative streak move is at least ' || threshold_name || ' bps and the countertrend direction is ' || direction_name || '. If the countertrend direction is ' || opposite_direction_name || ', skip. Paper entry simulates a BUY FAK taker fill from current executable ask depth; available liquidity is taken immediately, any remainder is cancelled, and settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
FROM formatted
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, paper_stake_amount, created_at_utc, updated_at_utc)
WITH assets(asset_symbol, up_id_group, down_id_group) AS (
    VALUES
        ('ETH', '8079', '8080'),
        ('SOL', '8081', '8082')
),
variants(direction_code, direction_name, opposite_direction_name) AS (
    VALUES
        ('up', 'Up', 'Down'),
        ('down', 'Down', 'Up')
),
thresholds(threshold_tenths) AS (
    SELECT generate_series(1, 50)
),
formatted AS (
    SELECT
        assets.asset_symbol,
        variants.direction_code,
        variants.direction_name,
        variants.opposite_direction_name,
        CASE
            WHEN variants.direction_code = 'up' THEN assets.up_id_group
            ELSE assets.down_id_group
        END AS id_group,
        thresholds.threshold_tenths,
        thresholds.threshold_tenths::text AS threshold_name,
        thresholds.threshold_tenths::text AS code_suffix
    FROM assets
    CROSS JOIN variants
    CROSS JOIN thresholds
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad((100 + threshold_tenths)::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_' || direction_code || '_bps_' || code_suffix || '_instant',
    asset_symbol || ' Up or Down 5m ' || direction_name || ' ' || threshold_name || ' bps Instant',
    'Immediately after ' || asset_symbol || ' 5m market open, use the previous close-book result streak and archived Binance ' || asset_symbol || ' start/end move gate; enter only when the cumulative streak move is at least ' || threshold_name || ' bps and the countertrend direction is ' || direction_name || '. If the countertrend direction is ' || opposite_direction_name || ', skip. Paper entry simulates a BUY FAK taker fill from current executable ask depth; available liquidity is taken immediately, any remainder is cancelled, and settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
FROM formatted
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, live_stakes, paper_stake_amount, created_at_utc, updated_at_utc)
VALUES (
    'b7c50005-0000-4000-8130-000000000109',
    'eth_up_down_5m_down_bps_9_fak',
    'ETH Up or Down 5m Down 9 bps',
    'Immediately after ETH 5m market open, use the previous ETH 5m close-book result streak and archived Binance ETH start/end move gate; enter only when the cumulative streak move is at least 9 bps and the countertrend direction is Down. If the countertrend direction is Up, skip. Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.',
    false,
    false,
    1.00,
    now(),
    now()
)
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, live_stakes, paper_stake_amount, created_at_utc, updated_at_utc)
WITH legacy_eth_down_premarket_thresholds(threshold_tenths) AS (
    SELECT generate_series(1, 50)
),
formatted AS (
    SELECT
        threshold_tenths,
        threshold_tenths::text AS threshold_name,
        threshold_tenths::text AS code_suffix
    FROM legacy_eth_down_premarket_thresholds
)
SELECT
    ('b7c50005-0000-4000-8131-' || lpad((100 + threshold_tenths)::text, 12, '0'))::uuid,
    'eth_up_down_5m_down_bps_' || code_suffix || '_fak_premarket',
    'ETH Up or Down 5m Down ' || threshold_name || ' bps Premarket',
    'Thirty seconds before ETH 5m market open, infer the previous ETH 5m market result from archived Binance ETH start price versus the current reference price sampled 30 seconds before previous market close; enter only when the inferred countertrend direction is Down and the absolute move is at least ' || threshold_name || ' bps. If the inferred countertrend direction is Up, skip. Paper entry simulates the same taker BUY from executable ask depth using the current premarket order book and worst-price cap, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.',
    true,
    false,
    1.00,
    now(),
    now()
FROM formatted
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
WITH assets(asset_symbol, id_group) AS (
    VALUES
        ('BTC', '8213'),
        ('ETH', '8214'),
        ('SOL', '8215')
),
thresholds(threshold_value) AS (
    SELECT value
    FROM generate_series(1, 10) AS generated(value)
    UNION ALL
    SELECT value
    FROM generate_series(15, 100, 5) AS generated(value)
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad((100 + threshold_value)::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_low_enter_average_bps_' || threshold_value::text || '_fak_premarket',
    asset_symbol || ' Up or Down 5m ' || threshold_value::text || ' bps LowEnter Average Premarket',
    '30 seconds before ' || asset_symbol || ' 5m market open, compare the latest Binance ' || asset_symbol || '/USDT reference price with the envelope formed by the smallest and largest full in-memory reference averages across 24h, 12h, 6h, 3h, 90m, 45m, 20m, and 10m windows. If the current price is above the maximum boundary by at least ' || threshold_value::text || ' bps, BUY Down; if it is below the minimum boundary by at least ' || threshold_value::text || ' bps, BUY Up. Otherwise skip. Simulate a Paper FAK taker BUY with a maximum order price of 0.50, fill only immediately executable asks at or below that price, and cancel the remainder. Live execution is not supported for this Paper experiment.',
    true,
    false,
    1.00,
    1.00,
    100.00,
    false,
    NULL,
    false,
    NULL,
    NULL,
    NULL,
    1.00,
    1.00,
    0,
    0,
    now(),
    now()
FROM assets
CROSS JOIN thresholds
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
WITH families(id_group, code_marker, name_marker, description_suffix) AS (
    VALUES
        ('8216', '3hour_average', '3Hour Average', 'Paper entry simulates the same taker BUY, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.'),
        ('8217', '3hour_low_enter_average', '3Hour LowEnter Average', 'Simulate a Paper FAK taker BUY with a maximum order price of 0.50, fill only immediately executable asks at or below that price, and cancel the remainder. Live execution is not supported for this Paper experiment.')
),
thresholds(threshold_value) AS (
    SELECT value
    FROM generate_series(1, 10) AS generated(value)
    UNION ALL
    SELECT value
    FROM generate_series(15, 100, 5) AS generated(value)
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad((100 + threshold_value)::text, 12, '0'))::uuid,
    'eth_up_down_5m_' || code_marker || '_bps_' || threshold_value::text || '_fak_premarket',
    'ETH Up or Down 5m ' || threshold_value::text || ' bps ' || name_marker || ' Premarket',
    '30 seconds before ETH 5m market open, compare the latest Binance ETH/USDT reference price with the full in-memory 3h reference average only. If the current price is above that 3h average by at least ' || threshold_value::text || ' bps, BUY Down; if it is below that 3h average by at least ' || threshold_value::text || ' bps, BUY Up. Otherwise skip. ' || description_suffix,
    true,
    false,
    1.00,
    1.00,
    100.00,
    false,
    NULL,
    false,
    NULL,
    NULL,
    NULL,
    1.00,
    1.00,
    0,
    0,
    now(),
    now()
FROM families
CROSS JOIN thresholds
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
WITH families(id_group, code_trigger_prefix, name_trigger_prefix, trigger_name, target_outcome) AS (
    VALUES
        ('8209', 'up_', 'Up ', 'Up', 'Down'),
        ('8210', 'down_', 'Down ', 'Down', 'Up'),
        ('8211', '', '', NULL, NULL)
),
thresholds(threshold_value) AS (
    SELECT value
    FROM generate_series(1, 10) AS generated(value)
    UNION ALL
    SELECT value
    FROM generate_series(15, 100, 5) AS generated(value)
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad((100 + threshold_value)::text, 12, '0'))::uuid,
    'eth_up_down_5m_' || code_trigger_prefix || 'optimized_average_bps_' || threshold_value::text || '_fak_premarket',
    'ETH Up or Down 5m ' || name_trigger_prefix || threshold_value::text || ' bps Optimized Average Premarket',
    '30 seconds before ETH 5m market open, evaluate the latest Binance ETH/USDT reference price against the full in-memory reference averages across 24h, 12h, 6h, 3h, 90m, 45m, 20m, and 10m windows. ' ||
        CASE
            WHEN trigger_name = 'Up'
            THEN 'Use the largest full reference average as the maximum boundary. If the current price moves Up by at least ' || threshold_value::text || ' bps from that maximum boundary, BUY Down.'
            WHEN trigger_name = 'Down'
            THEN 'Use the smallest full reference average as the minimum boundary. If the current price moves Down by at least ' || threshold_value::text || ' bps from that minimum boundary, BUY Up.'
            ELSE 'Use the envelope formed by the smallest and largest full reference averages. If the current price is above the maximum boundary by at least ' || threshold_value::text || ' bps, BUY Down; if it is below the minimum boundary by at least ' || threshold_value::text || ' bps, BUY Up.'
        END ||
        CASE
            WHEN trigger_name = 'Up'
            THEN ' Enter only when the direction-relevant maximum boundary came from the 3h window; otherwise skip.'
            WHEN trigger_name = 'Down'
            THEN ' Enter only when the direction-relevant minimum boundary came from the 3h window; otherwise skip.'
            ELSE ' Enter only when the envelope boundary that triggered the signal came from the 3h window; otherwise skip.'
        END ||
        ' Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap. Live execution is not supported for this optimized Paper experiment.',
    true,
    false,
    1.00,
    1.00,
    100.00,
    false,
    NULL,
    false,
    NULL,
    NULL,
    NULL,
    1.00,
    1.00,
    0,
    0,
    now(),
    now()
FROM families
CROSS JOIN thresholds
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
WITH families(id_group, code_trigger_prefix, name_trigger_prefix, trigger_name, target_outcome) AS (
    VALUES
        ('8219', 'up_', 'Up ', 'Up', 'Down'),
        ('8212', 'down_', 'Down ', 'Down', 'Up'),
        ('8220', '', '', NULL, NULL)
),
thresholds(threshold_value) AS (
    SELECT value
    FROM generate_series(1, 10) AS generated(value)
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad((100 + threshold_value)::text, 12, '0'))::uuid,
    'btc_up_down_5m_' || code_trigger_prefix || 'optimized_average_bps_' || threshold_value::text || '_fak_premarket',
    'BTC Up or Down 5m ' || name_trigger_prefix || threshold_value::text || ' bps Optimized Average Premarket',
    '30 seconds before BTC 5m market open, evaluate the latest Binance BTC/USDT reference price against the full in-memory reference averages across 24h, 12h, 6h, 3h, 90m, 45m, 20m, and 10m windows. ' ||
        CASE
            WHEN trigger_name = 'Up'
            THEN 'Use the largest full reference average as the maximum boundary. If the current price moves Up by at least ' || threshold_value::text || ' bps from that maximum boundary, BUY Down.'
            WHEN trigger_name = 'Down'
            THEN 'Use the smallest full reference average as the minimum boundary. If the current price moves Down by at least ' || threshold_value::text || ' bps from that minimum boundary, BUY Up.'
            ELSE 'Use the envelope formed by the smallest and largest full reference averages. If the current price is above the maximum boundary by at least ' || threshold_value::text || ' bps, BUY Down; if it is below the minimum boundary by at least ' || threshold_value::text || ' bps, BUY Up.'
        END ||
        CASE
            WHEN trigger_name = 'Up'
            THEN ' Enter only when the direction-relevant maximum boundary came from the 3h window; otherwise skip.'
            WHEN trigger_name = 'Down'
            THEN ' Enter only when the direction-relevant minimum boundary came from the 3h window; otherwise skip.'
            ELSE ' Enter only when the envelope boundary that triggered the signal came from the 3h window; otherwise skip.'
        END ||
        ' Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap. Live execution is not supported for this optimized Paper experiment.',
    true,
    false,
    1.00,
    1.00,
    100.00,
    false,
    NULL,
    false,
    NULL,
    NULL,
    NULL,
    1.00,
    1.00,
    0,
    0,
    now(),
    now()
FROM families
CROSS JOIN thresholds
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
WITH families(id_group, code_trigger_prefix, name_trigger_prefix, trigger_name, target_outcome) AS (
    VALUES
        ('8221', 'up_', 'Up ', 'Up', 'Down'),
        ('8218', 'down_', 'Down ', 'Down', 'Up'),
        ('8222', '', '', NULL, NULL)
),
thresholds(threshold_value) AS (
    SELECT value
    FROM generate_series(1, 10) AS generated(value)
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad((100 + threshold_value)::text, 12, '0'))::uuid,
    'sol_up_down_5m_' || code_trigger_prefix || 'optimized_average_bps_' || threshold_value::text || '_fak_premarket',
    'SOL Up or Down 5m ' || name_trigger_prefix || threshold_value::text || ' bps Optimized Average Premarket',
    '30 seconds before SOL 5m market open, evaluate the latest Binance SOL/USDT reference price against the full in-memory reference averages across 24h, 12h, 6h, 3h, 90m, 45m, 20m, and 10m windows. ' ||
        CASE
            WHEN trigger_name = 'Up'
            THEN 'Use the largest full reference average as the maximum boundary. If the current price moves Up by at least ' || threshold_value::text || ' bps from that maximum boundary, BUY Down.'
            WHEN trigger_name = 'Down'
            THEN 'Use the smallest full reference average as the minimum boundary. If the current price moves Down by at least ' || threshold_value::text || ' bps from that minimum boundary, BUY Up.'
            ELSE 'Use the envelope formed by the smallest and largest full reference averages. If the current price is above the maximum boundary by at least ' || threshold_value::text || ' bps, BUY Down; if it is below the minimum boundary by at least ' || threshold_value::text || ' bps, BUY Up.'
        END ||
        CASE
            WHEN trigger_name = 'Up'
            THEN ' Enter only when the direction-relevant maximum boundary came from the 3h window; otherwise skip.'
            WHEN trigger_name = 'Down'
            THEN ' Enter only when the direction-relevant minimum boundary came from the 3h window; otherwise skip.'
            ELSE ' Enter only when the envelope boundary that triggered the signal came from the 3h window; otherwise skip.'
        END ||
        ' Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap. Live execution is not supported for this optimized Paper experiment.',
    true,
    false,
    1.00,
    1.00,
    100.00,
    false,
    NULL,
    false,
    NULL,
    NULL,
    NULL,
    1.00,
    1.00,
    0,
    0,
    now(),
    now()
FROM families
CROSS JOIN thresholds
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
WITH assets(asset_symbol, child_id_group, child_progress_id_group, child_roi_id_group, child_progress_roi_id_group) AS (
    VALUES
        ('BTC', '8185', '8188', '8194', '8197'),
        ('ETH', '8186', '8189', '8195', '8198'),
        ('SOL', '8187', '8190', '8196', '8199')
),
modes(mode_code, mode_name, mode_description, id_group_selector, metric_name) AS (
    VALUES
        ('child', 'Child', 'excluding strategies whose name contains Progress', 'child', 'positive paper PnL'),
        ('child_progress', 'Child Progress', 'including Progress strategies', 'child_progress', 'positive paper PnL'),
        ('child_roi', 'Child ROI', 'excluding strategies whose name contains Progress', 'child_roi', 'sample-adjusted paper ROI after minimum sample gates'),
        ('child_progress_roi', 'Child Progress ROI', 'including Progress strategies', 'child_progress_roi', 'sample-adjusted paper ROI after minimum sample gates')
),
lookbacks(lookback_hours) AS (
    SELECT value
    FROM generate_series(1, 24) AS generated(value)
),
formatted AS (
    SELECT
        assets.asset_symbol,
        modes.mode_code,
        modes.mode_name,
        modes.mode_description,
        modes.metric_name,
        CASE
            WHEN modes.id_group_selector = 'child' THEN assets.child_id_group
            WHEN modes.id_group_selector = 'child_progress' THEN assets.child_progress_id_group
            WHEN modes.id_group_selector = 'child_roi' THEN assets.child_roi_id_group
            ELSE assets.child_progress_roi_id_group
        END AS id_group,
        lookbacks.lookback_hours
    FROM assets
    CROSS JOIN modes
    CROSS JOIN lookbacks
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad(lookback_hours::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_' || lookback_hours::text || '_' || mode_code,
    asset_symbol || ' Up or Down 5m ' || lookback_hours::text || ' ' || mode_name,
    'After all market-opening entries and database writes are complete, select the enabled non-Child, non-Futures ' || asset_symbol || ' strategy with the highest ' || metric_name || ' over the last ' || lookback_hours::text || ' hour(s), ' || mode_description || '. While the parent link is active, copy each accepted parent entry in the same market, outcome, notional, and share size.',
    true,
    false,
    1.00,
    1.00,
    100.00,
    false,
    NULL,
    false,
    NULL,
    NULL,
    NULL,
    1.00,
    1.00,
    0,
    0,
    now(),
    now()
FROM formatted
WHERE NOT (
        asset_symbol = 'ETH'
        AND mode_code = 'child_progress'
        AND lookback_hours IN (1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 13, 14, 19, 21, 24)
    )
  AND NOT (
        asset_symbol = 'ETH'
        AND mode_code = 'child_progress_roi'
        AND lookback_hours IN (3, 5, 7, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24)
    )
  AND NOT (
        asset_symbol = 'SOL'
        AND mode_code = 'child_progress_roi'
        AND lookback_hours IN (4, 5, 6, 13, 14, 19, 21, 23)
    )
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
WITH assets(asset_symbol, standard_id_group, revert_id_group) AS (
    VALUES
        ('BTC', '8182', '8191'),
        ('ETH', '8183', '8192'),
        ('SOL', '8184', '8193')
),
thresholds(threshold_value) AS (
    SELECT value
    FROM (VALUES (1), (2), (3), (5), (8), (10), (15), (20)) AS generated(value)
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad((100 + threshold_value)::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_futures_basis_bps_' || threshold_value::text || mode_code || '_fak_premarket',
    asset_symbol || ' Up or Down 5m ' || threshold_value::text || ' bps Futures Basis' || mode_name || ' Premarket',
    'Thirty seconds before ' || asset_symbol || ' 5m market open, select the three live OKX linear USD fixed-expiry contracts with the closest distinct expiries at or after the target market end and compare each best bid/ask mid with the simultaneous OKX ' || asset_symbol || '-USD index. Apply the ' || threshold_value::text || ' bps threshold only to the nearest expiry and require both following expiries to confirm its nonzero basis sign. ' ||
        CASE
            WHEN mode_code = '_revert'
            THEN 'If the nearest futures mid is above the index by at least ' || threshold_value::text || ' bps and both confirmations are positive, BUY Down; if it is below the index by at least ' || threshold_value::text || ' bps and both confirmations are negative, BUY Up.'
            ELSE 'If the nearest futures mid is above the index by at least ' || threshold_value::text || ' bps and both confirmations are positive, BUY Up; if it is below the index by at least ' || threshold_value::text || ' bps and both confirmations are negative, BUY Down.'
        END ||
        ' Otherwise skip. Require all three fresh contracts and never substitute a perpetual contract. Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap, while Live-shadow remains disabled by default until manually enabled and normal live gates pass.',
    true,
    false,
    1.00,
    1.00,
    100.00,
    false,
    NULL,
    false,
    NULL,
    NULL,
    NULL,
    1.00,
    1.00,
    0,
    0,
    now(),
    now()
FROM assets
CROSS JOIN thresholds
CROSS JOIN LATERAL (
    VALUES
        ('', '', assets.standard_id_group),
        ('_revert', ' Revert', assets.revert_id_group)
) AS modes(mode_code, mode_name, id_group)
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
WITH assets(asset_symbol, up_id_group, down_id_group) AS (
    VALUES
        ('BTC', '8135', '8136'),
        ('ETH', '8137', '8140'),
        ('SOL', '8138', '8139')
),
variants(trigger_code, trigger_name, target_outcome) AS (
    VALUES
        ('up', 'Up', 'Down'),
        ('down', 'Down', 'Up')
),
thresholds(threshold_tenths) AS (
    SELECT value
    FROM generate_series(1, 10) AS generated(value)
    UNION ALL
    SELECT value
    FROM generate_series(15, 100, 5) AS generated(value)
),
formatted AS (
    SELECT
        assets.asset_symbol,
        variants.trigger_code,
        variants.trigger_name,
        variants.target_outcome,
        CASE
            WHEN variants.trigger_code = 'up' THEN assets.up_id_group
            ELSE assets.down_id_group
        END AS id_group,
        threshold_tenths,
        threshold_tenths::text AS threshold_name,
        threshold_tenths::text AS code_suffix
    FROM assets
    CROSS JOIN variants
    CROSS JOIN thresholds
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad((100 + threshold_tenths)::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_' || trigger_code ||
        CASE WHEN asset_symbol = 'ETH' AND trigger_code = 'down' THEN '_reference_average' ELSE '' END ||
        '_bps_' || code_suffix || '_fak_premarket',
    asset_symbol || ' Up or Down 5m ' || trigger_name || ' ' || threshold_name || ' bps Reference Average Premarket',
    CASE
        WHEN trigger_code = 'up'
        THEN 'Thirty seconds before ' || asset_symbol || ' 5m market open, compare the latest Binance ' || asset_symbol || '/USDT reference price with the largest full in-memory reference average across 24h, 12h, 6h, 3h, 90m, 45m, 20m, and 10m windows. If the current price moves Up by at least ' || threshold_name || ' bps from that maximum boundary, BUY Down from current premarket executable ask depth using the worst-price cap. Otherwise skip. Paper entry simulates the same taker BUY, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.'
        ELSE 'Thirty seconds before ' || asset_symbol || ' 5m market open, compare the latest Binance ' || asset_symbol || '/USDT reference price with the smallest full in-memory reference average across 24h, 12h, 6h, 3h, 90m, 45m, 20m, and 10m windows. If the current price moves Down by at least ' || threshold_name || ' bps from that minimum boundary, BUY Up from current premarket executable ask depth using the worst-price cap. Otherwise skip. Paper entry simulates the same taker BUY, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.'
    END,
    true,
    false,
    1.00,
    1.00,
    100.00,
    false,
    NULL,
    false,
    NULL,
    NULL,
    NULL,
    1.00,
    1.00,
    0,
    0,
    now(),
    now()
FROM formatted
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
WITH assets(asset_symbol, id_group) AS (
    VALUES
        ('BTC', '8178'),
        ('ETH', '8179'),
        ('SOL', '8180')
),
thresholds(threshold_tenths) AS (
    SELECT value
    FROM generate_series(1, 10) AS generated(value)
    UNION ALL
    SELECT value
    FROM generate_series(15, 100, 5) AS generated(value)
),
formatted AS (
    SELECT
        assets.asset_symbol,
        assets.id_group,
        threshold_tenths,
        threshold_tenths::text AS threshold_name,
        threshold_tenths::text AS code_suffix
    FROM assets
    CROSS JOIN thresholds
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad((100 + threshold_tenths)::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_reference_average_bps_' || code_suffix || '_fak_premarket',
    asset_symbol || ' Up or Down 5m ' || threshold_name || ' bps Reference Average Premarket',
    'Thirty seconds before ' || asset_symbol || ' 5m market open, compare the latest Binance ' || asset_symbol || '/USDT reference price with the envelope formed by the smallest and largest full in-memory reference averages across 24h, 12h, 6h, 3h, 90m, 45m, 20m, and 10m windows. If the current price is above the maximum boundary by at least ' || threshold_name || ' bps, BUY Down; if it is below the minimum boundary by at least ' || threshold_name || ' bps, BUY Up. Otherwise skip. Paper entry simulates the same taker BUY, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.',
    true,
    false,
    1.00,
    1.00,
    100.00,
    false,
    NULL,
    false,
    NULL,
    NULL,
    NULL,
    1.00,
    1.00,
    0,
    0,
    now(),
    now()
FROM formatted
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
WITH assets(asset_symbol, id_group) AS (
    VALUES
        ('BTC', '8206'),
        ('ETH', '8207'),
        ('SOL', '8208')
),
lookbacks(lookback_hours) AS (
    SELECT value
    FROM generate_series(1, 24) AS generated(value)
),
thresholds(threshold_bps) AS (
    SELECT value
    FROM generate_series(1, 5) AS generated(value)
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad((lookback_hours * 100 + threshold_bps)::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_' || lookback_hours::text || 'h_absolute_bps_' || threshold_bps::text || '_fak_premarket',
    asset_symbol || ' Up or Down 5m ' || lookback_hours::text || 'h ' || threshold_bps::text || ' bps Absolute Premarket',
    'Thirty seconds before ' || asset_symbol || ' 5m market open, read the full ' || lookback_hours::text || 'h rolling extrema window built from persisted ten-second Binance ' || asset_symbol || '/USDT reference-price samples observed before the fresh decision price. If the current price is at least ' || threshold_bps::text || ' bps above the historical maximum, BUY Down; if it is at least ' || threshold_bps::text || ' bps below the historical minimum, BUY Up; otherwise skip. Paper entry simulates a FAK taker BUY from executable ask depth using the guaranteed worst-price cap, while Live-shadow remains disabled by default until manually enabled and normal live gates pass.',
    true,
    false,
    1.00,
    1.00,
    100.00,
    false,
    NULL,
    false,
    NULL,
    NULL,
    NULL,
    1.00,
    1.00,
    0,
    0,
    now(),
    now()
FROM assets
CROSS JOIN lookbacks
CROSS JOIN thresholds
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

UPDATE strategies
SET name = replace(name, ' bps FAK Premarket', ' bps Premarket'),
    description = replace(replace(replace(description, ' FAK ', ' '), 'FAK ', ''), ' FAK', ''),
    updated_at_utc = now()
WHERE strategies.code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
    AND (strategies.name ILIKE '%FAK%' OR strategies.description ILIKE '%FAK%');

INSERT INTO strategies (id, code, name, description, enabled, live_stakes, paper_stake_amount, created_at_utc, updated_at_utc)
WITH specs(id_group, entry_delay_seconds, sample_seconds_before_end, threshold_tenths) AS (
    VALUES
        (8132, -10, 10, 40),
        (8132, -10, 10, 41),
        (8132, -10, 10, 42),
        (8133, -5, 5, 30),
        (8133, -5, 5, 31),
        (8133, -5, 5, 32),
        (8133, -5, 5, 33),
        (8133, -5, 5, 34),
        (8133, -5, 5, 35),
        (8133, -5, 5, 36),
        (8133, -5, 5, 37),
        (8133, -5, 5, 38)
),
formatted AS (
    SELECT
        id_group,
        entry_delay_seconds,
        sample_seconds_before_end,
        threshold_tenths,
        threshold_tenths::text AS threshold_name,
        threshold_tenths::text AS code_suffix,
        'm' || sample_seconds_before_end::text || 's' AS premarket_suffix
    FROM specs
)
SELECT
    ('b7c50005-0000-4000-' || id_group::text || '-' || lpad((100 + threshold_tenths)::text, 12, '0'))::uuid,
    'eth_up_down_5m_down_bps_' || code_suffix || '_fak_premarket_' || premarket_suffix,
    'ETH Up or Down 5m Down ' || threshold_name || ' bps Premarket -' || sample_seconds_before_end::text || 's',
    sample_seconds_before_end::text || ' seconds before ETH 5m market open, infer the previous ETH 5m market result from archived Binance ETH start price versus the current reference price sampled ' || sample_seconds_before_end::text || ' seconds before previous market close; enter only when the inferred countertrend direction is Down and the absolute move is at least ' || threshold_name || ' bps. If the inferred countertrend direction is Up, skip. Paper entry simulates the same taker BUY from executable ask depth using the current premarket order book and worst-price cap, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.',
    true,
    false,
    1.00,
    now(),
    now()
FROM formatted
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, live_stakes, paper_stake_amount, created_at_utc, updated_at_utc)
WITH assets(asset_symbol, up_id_group, down_id_group) AS (
    VALUES
        ('BTC', '8146', '8148'),
        ('ETH', '8144', '8134'),
        ('SOL', '8150', '8152')
),
variants(diff_code, diff_name, diff_expression, target_outcome, strategy_kind) AS (
    VALUES
        ('up', 'Up', 'UpCount - DownCount', 'Down', 'countertrend'),
        ('down', 'Down', 'DownCount - UpCount', 'Up', 'countertrend')
),
formatted AS (
    SELECT
        assets.asset_symbol,
        variants.diff_code,
        variants.diff_name,
        variants.diff_expression,
        variants.target_outcome,
        variants.strategy_kind,
        CASE
            WHEN variants.diff_code = 'up' THEN assets.up_id_group
            ELSE assets.down_id_group
        END AS id_group,
        thresholds.threshold_value
    FROM assets
    CROSS JOIN variants
    CROSS JOIN LATERAL generate_series(
        1,
        CASE
            WHEN assets.asset_symbol = 'BTC' AND variants.diff_code = 'down' THEN 30
            ELSE 10
        END) AS thresholds(threshold_value)
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad(threshold_value::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_' || diff_code || '_diff_' || threshold_value::text || '_fak_premarket',
    asset_symbol || ' Up or Down 5m ' || diff_name || ' ' || threshold_value::text || ' Diff Premarket',
    '30 seconds before ' || asset_symbol || ' 5m market open, use the in-memory UTC-day raw ' || diff_expression || ' counter reset at 00:00 UTC. Diff ' || strategy_kind || ' strategy: if the absolute Diff side is at least ' || threshold_value::text || ', BUY ' || target_outcome || ' from the current premarket executable ask depth using the worst-price cap. Otherwise skip. Paper entry simulates the same taker BUY, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.',
    true,
    false,
    1.00,
    now(),
    now()
FROM formatted
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, live_stakes, paper_stake_amount, created_at_utc, updated_at_utc)
WITH thresholds(threshold_value) AS (
    SELECT value
    FROM generate_series(1, 50) AS generated(value)
),
assets(asset_symbol, up_id_group, down_id_group) AS (
    VALUES
        ('BTC', '8154', '8155'),
        ('ETH', '8156', '8157'),
        ('SOL', '8158', '8159')
),
variants(diff_code, diff_name, diff_expression, target_outcome) AS (
    VALUES
        ('up', 'Up', 'UpCount - DownCount', 'Down'),
        ('down', 'Down', 'DownCount - UpCount', 'Up')
),
formatted AS (
    SELECT
        assets.asset_symbol,
        variants.diff_code,
        variants.diff_name,
        variants.diff_expression,
        variants.target_outcome,
        CASE
            WHEN variants.diff_code = 'up' THEN assets.up_id_group
            ELSE assets.down_id_group
        END AS id_group,
        thresholds.threshold_value
    FROM assets
    CROSS JOIN variants
    CROSS JOIN thresholds
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad(threshold_value::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_diff_' || threshold_value::text || '_' || diff_code || '_progress',
    asset_symbol || ' Up or Down 5m ' || threshold_value::text || ' Diff ' || diff_name || ' Progress',
    'Diff Progress strategy: count the in-memory UTC-day raw ' || diff_expression || ' counter reset at 00:00 UTC in both waiting and betting modes and backfilled from the current UTC day on service restart. When Diff is greater than ' || threshold_value::text || ', switch to betting mode and submit BUY FAK Paper entries on ' || target_outcome || '; the effective stake multiplier is min(Diff minus ' || threshold_value::text || ', 10). If the reset or later results return Diff to the threshold or below, the strategy switches back to waiting mode.',
    true,
    false,
    1.00,
    now(),
    now()
FROM formatted
WHERE NOT (
    asset_symbol = 'SOL'
    AND diff_code = 'up'
    AND threshold_value IN (1, 2)
)
  AND NOT (
    asset_symbol = 'ETH'
    AND diff_code = 'up'
    AND threshold_value IN (1, 2, 13, 14, 15, 16)
)
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, live_stakes, paper_stake_amount, created_at_utc, updated_at_utc)
WITH assets(asset_symbol, up_id_group, down_id_group) AS (
    VALUES
        ('BTC', '8160', '8161'),
        ('ETH', '8162', '8163'),
        ('SOL', '8164', '8165')
),
variants(diff_code, diff_name, diff_expression, target_outcome) AS (
    VALUES
        ('up', 'Up', 'UpCount - DownCount', 'Down'),
        ('down', 'Down', 'DownCount - UpCount', 'Up')
),
formatted AS (
    SELECT
        assets.asset_symbol,
        variants.diff_code,
        variants.diff_name,
        variants.diff_expression,
        variants.target_outcome,
        CASE
            WHEN variants.diff_code = 'up' THEN assets.up_id_group
            ELSE assets.down_id_group
        END AS id_group
    FROM assets
    CROSS JOIN variants
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-000000000001')::uuid,
    lower(asset_symbol) || '_up_down_5m_diff_' || diff_code || '_shift_progress',
    asset_symbol || ' Up or Down 5m Diff ' || diff_name || ' Shift Progress',
    'Diff Shift Progress strategy: use the persistent raw ' || diff_expression || ' counter and persistent Sum. Unit is this strategy paper_stake_amount. When Diff is greater than 0, each FAK Paper BUY on ' || target_outcome || ' uses multiplier Diff + 1 with the Diff instant max price cap; Diff 0 or below skips. When a previous bet wins, Sum increases by the filled stake; when it loses, Sum decreases by the filled stake. After each processed result, while Sum is greater than Unit and Diff is greater than 1, reduce Diff by 1 and subtract Unit from Sum.',
    true,
    false,
    1.00,
    now(),
    now()
FROM formatted
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, live_stakes, paper_stake_amount, created_at_utc, updated_at_utc)
WITH thresholds(threshold_value) AS (
    SELECT value
    FROM generate_series(1, 5) AS generated(value)
),
assets(asset_symbol, id_group) AS (
    VALUES
        ('BTC', '8166'),
        ('ETH', '8167'),
        ('SOL', '8168')
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad(threshold_value::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_' || threshold_value::text || '_diff_shift_progress_premarket',
    asset_symbol || ' Up or Down 5m ' || threshold_value::text || ' Diff Shift Progress Premarket',
    '30 seconds before ' || asset_symbol || ' 5m market open, use the persistent raw UpCount - DownCount counter and persistent Sum. Results before the latest 5-minute market use resolved market results; the latest market result is synthesized from the current ' || asset_symbol || ' reference price. When Diff is greater than 0, BUY Down; when Diff is less than 0, BUY Up; Diff 0 skips. Unit is this strategy paper_stake_amount, and each FAK Paper BUY uses multiplier abs(Diff) at the Diff instant max price cap. When abs(Diff) reaches ' || threshold_value::text || ', enter damping mode, reset Sum, then move Diff one step toward 0 each time Sum becomes greater than Unit. When Diff returns to 0, return to simple mode.',
    true,
    false,
    1.00,
    now(),
    now()
FROM assets
CROSS JOIN thresholds
WHERE NOT (
        asset_symbol = 'BTC'
        AND threshold_value IN (1, 2, 4, 5)
    )
  AND NOT (
        asset_symbol = 'ETH'
        AND threshold_value = 4
    )
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, live_stakes, paper_stake_amount, created_at_utc, updated_at_utc)
WITH limits(limit_value) AS (
    SELECT value
    FROM generate_series(1, 5) AS generated(value)
),
assets(asset_symbol, id_group) AS (
    VALUES
        ('BTC', '8169'),
        ('ETH', '8170'),
        ('SOL', '8171')
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad(limit_value::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_' || limit_value::text || '_diff_limit_progress_premarket',
    asset_symbol || ' Up or Down 5m ' || limit_value::text || ' Diff Limit Progress Premarket',
    '30 seconds before ' || asset_symbol || ' 5m market open, use persistent UTC-day UpCount, DownCount, and Sum. Counts reset at 00:00 UTC. Results before the latest 5-minute market use resolved market results; the latest market result is synthesized from the current ' || asset_symbol || ' reference price. Diff is UpCount - DownCount: Diff > 0 buys Down, Diff < 0 buys Up, and Diff 0 skips. Unit is this strategy paper_stake_amount, and each BUY FAK Paper entry uses multiplier min(abs(Diff), ' || limit_value::text || ') at the Diff instant max price cap.',
    true,
    false,
    1.00,
    now(),
    now()
FROM assets
CROSS JOIN limits
WHERE asset_symbol <> 'BTC'
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, live_stakes, paper_stake_amount, created_at_utc, updated_at_utc)
WITH limits(limit_value) AS (
    SELECT value
    FROM generate_series(1, 5) AS generated(value)
),
assets(asset_symbol, id_group) AS (
    VALUES
        ('BTC', '8172'),
        ('ETH', '8173'),
        ('SOL', '8174')
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad(limit_value::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_' || limit_value::text || '_diff_real_limit_progress_premarket',
    asset_symbol || ' Up or Down 5m ' || limit_value::text || ' Diff Real Limit Progress Premarket',
    '30 seconds before ' || asset_symbol || ' 5m market open, use persistent UTC-day UpCount, DownCount, and Sum. Counts reset at 00:00 UTC. Results before the latest 5-minute market use resolved market results; the latest market result is synthesized from the current ' || asset_symbol || ' reference price. Diff is UpCount - DownCount: Diff > 0 buys Down, Diff < 0 buys Up, and Diff 0 skips. UpCount and DownCount stop changing when the next result would move Diff outside [-' || limit_value::text || ', ' || limit_value::text || '], while opposite results can move Diff back inside the range. Unit is this strategy paper_stake_amount, and each BUY FAK Paper entry uses multiplier abs(Diff) at the Diff instant max price cap.',
    true,
    false,
    1.00,
    now(),
    now()
FROM assets
CROSS JOIN limits
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, live_stakes, paper_stake_amount, created_at_utc, updated_at_utc)
WITH thresholds(threshold_value) AS (
    SELECT value
    FROM generate_series(1, 10) AS generated(value)
    UNION ALL
    SELECT value
    FROM (VALUES (15), (20), (25), (30)) AS extra(value)
),
assets(asset_symbol, id_group) AS (
    VALUES
        ('BTC', '8175'),
        ('ETH', '8176'),
        ('SOL', '8177')
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad(threshold_value::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_' || threshold_value::text || '_diff_reference_average_premarket',
    asset_symbol || ' Up or Down 5m ' || threshold_value::text || ' Diff Reference Average Premarket',
    '30 seconds before ' || asset_symbol || ' 5m market open, compute the rolling 24-hour raw Diff = UpCount - DownCount without a UTC-day reset. Results before the latest 5-minute market use resolved market results; the latest market result is synthesized from the current ' || asset_symbol || ' reference price. Average Diff is calculated over full 24h, 12h, 6h, 3h, 90m, and 45m windows, then the average farthest from zero is selected. If current Diff minus that selected Average Diff is at least ' || threshold_value::text || ', BUY Down; if it is at most -' || threshold_value::text || ', BUY Up; otherwise skip. Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.',
    true,
    false,
    1.00,
    now(),
    now()
FROM assets
CROSS JOIN thresholds
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
WITH assets(asset_symbol, id_group, confirmation_diff_threshold) AS (
    VALUES
        ('BTC', '8200', 5),
        ('ETH', '8201', 3),
        ('SOL', '8202', 1)
),
thresholds(threshold_value) AS (
    SELECT value
    FROM generate_series(1, 10) AS generated(value)
    UNION ALL
    SELECT value
    FROM generate_series(15, 100, 5) AS generated(value)
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad((100 + threshold_value)::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_' || threshold_value::text || '_bps_confirmed_average_premarket',
    asset_symbol || ' Up or Down 5m ' || threshold_value::text || ' bps Confirmed Average Premarket',
    '30 seconds before ' || asset_symbol || ' 5m market open, evaluate the exact neutral ' || threshold_value::text || ' bps Reference Average signal and independently evaluate the exact ' || confirmation_diff_threshold::text || ' Diff Reference Average signal. Enter only when both signals are present and select the same outcome; otherwise skip. Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.',
    true,
    false,
    1.00,
    1.00,
    100.00,
    false,
    NULL,
    false,
    NULL,
    NULL,
    NULL,
    1.00,
    1.00,
    0,
    0,
    now(),
    now()
FROM assets
CROSS JOIN thresholds
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
WITH assets(asset_symbol, id_group, confirmation_bps_threshold) AS (
    VALUES
        ('BTC', '8203', 45),
        ('ETH', '8204', 5),
        ('SOL', '8205', 35)
),
thresholds(threshold_value) AS (
    SELECT value
    FROM generate_series(1, 10) AS generated(value)
    UNION ALL
    SELECT value
    FROM (VALUES (15), (20), (25), (30)) AS extra(value)
)
SELECT
    ('b7c50005-0000-4000-' || id_group || '-' || lpad(threshold_value::text, 12, '0'))::uuid,
    lower(asset_symbol) || '_up_down_5m_' || threshold_value::text || '_diff_confirmed_average_premarket',
    asset_symbol || ' Up or Down 5m ' || threshold_value::text || ' Diff Confirmed Average Premarket',
    '30 seconds before ' || asset_symbol || ' 5m market open, evaluate the exact ' || threshold_value::text || ' Diff Reference Average signal and independently evaluate the exact neutral ' || confirmation_bps_threshold::text || ' bps Reference Average signal. Enter only when both signals are present and select the same outcome; otherwise skip. Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.',
    true,
    false,
    1.00,
    1.00,
    100.00,
    false,
    NULL,
    false,
    NULL,
    NULL,
    NULL,
    1.00,
    1.00,
    0,
    0,
    now(),
    now()
FROM assets
CROSS JOIN thresholds
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

-- PreOpen fixed-direction strategy rows were physically removed from production
-- and are intentionally no longer seeded. Missing PreOpen strategy rows are
-- treated as deleted/disabled by the strategy-market-run insert guard below.

CREATE TABLE IF NOT EXISTS dashboard_strategy_performance_snapshots (
    strategy_id uuid PRIMARY KEY,
    code text NOT NULL,
    name text NOT NULL,
    enabled boolean NOT NULL,
    live_stakes boolean NOT NULL,
    auto_live_paused boolean NOT NULL DEFAULT false,
    paused boolean NOT NULL,
    paused_until_utc timestamptz NULL,
    paper_stake_amount numeric(28,8) NOT NULL,
    live_stake_amount numeric(28,8) NOT NULL,
    paper_lost_coeff numeric(28,8) NOT NULL,
    live_lost_coeff numeric(28,8) NOT NULL,
    paper_lost_counter integer NOT NULL,
    live_lost_counter integer NOT NULL,
    live_available_balance numeric(28,8) NOT NULL,
    orders_count integer NOT NULL,
    filled_orders_count integer NOT NULL,
    open_orders_count integer NOT NULL,
    open_positions_count integer NOT NULL,
    observed_runs_count integer NOT NULL,
    entered_runs_count integer NOT NULL,
    skipped_runs_count integer NOT NULL,
    paper_condition_skipped_runs_count integer NOT NULL,
    paper_not_accepted_runs_count integer NOT NULL,
    settled_runs_count integer NOT NULL,
    settled_positions_count integer NOT NULL,
    won_positions_count integer NOT NULL,
    lost_positions_count integer NOT NULL,
    stake_usd numeric(28,8) NOT NULL,
    realized_pnl_usd numeric(28,8) NOT NULL,
    unrealized_pnl_usd numeric(28,8) NOT NULL,
    total_pnl_usd numeric(28,8) NOT NULL,
    win_rate_pct numeric(28,8) NOT NULL,
    loss_rate_pct numeric(28,8) NOT NULL,
    avg_win_pnl_usd numeric(28,8) NOT NULL,
    avg_loss_pnl_usd numeric(28,8) NOT NULL,
    profit_factor numeric(28,8) NULL,
    expectancy_pnl_usd numeric(28,8) NOT NULL,
    roi_pct numeric(28,8) NOT NULL,
    closed_roi_pct numeric(28,8) NOT NULL,
    avg_entry_delay_seconds numeric(28,8) NOT NULL,
    max_entry_delay_seconds numeric(28,8) NOT NULL,
    avg_countertrend_score_bps numeric(28,8) NOT NULL,
    avg_countertrend_signal_bps numeric(28,8) NOT NULL,
    last_countertrend_signal_bps numeric(28,8) NULL,
    live_orders_count integer NOT NULL,
    live_filled_orders_count integer NOT NULL,
    live_open_orders_count integer NOT NULL,
    live_settled_orders_count integer NOT NULL,
    live_skipped_orders_count integer NOT NULL,
    live_condition_skipped_orders_count integer NOT NULL,
    live_technical_skipped_orders_count integer NOT NULL,
    live_ignored_orders_count integer NOT NULL,
    live_ignored_gtd_unfilled_count integer NOT NULL,
    live_ignored_cancelled_orders_count integer NOT NULL,
    live_ignored_rejected_orders_count integer NOT NULL,
    live_won_orders_count integer NOT NULL,
    live_lost_orders_count integer NOT NULL,
    live_stake_usd numeric(28,8) NOT NULL,
    live_realized_pnl_usd numeric(28,8) NOT NULL,
    live_win_rate_pct numeric(28,8) NOT NULL,
    live_loss_rate_pct numeric(28,8) NOT NULL,
    live_avg_win_pnl_usd numeric(28,8) NOT NULL,
    live_avg_loss_pnl_usd numeric(28,8) NOT NULL,
    live_profit_factor numeric(28,8) NULL,
    live_expectancy_pnl_usd numeric(28,8) NOT NULL,
    live_roi_pct numeric(28,8) NOT NULL,
    live_last_order_utc timestamptz NULL,
    live_last_settlement_utc timestamptz NULL,
    last_order_utc timestamptz NULL,
    last_run_utc timestamptz NULL,
    refreshed_at_utc timestamptz NOT NULL
);

ALTER TABLE dashboard_strategy_performance_snapshots
    ALTER COLUMN auto_live_paused SET DEFAULT false;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_strategy_performance_snapshots_code
ON dashboard_strategy_performance_snapshots (code);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_strategy_performance_snapshots_refreshed_at
ON dashboard_strategy_performance_snapshots (refreshed_at_utc DESC);

CREATE TABLE IF NOT EXISTS dashboard_strategy_recent_performance_snapshots (
    strategy_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    live_stakes boolean NOT NULL,
    window_label text NOT NULL,
    window_hours integer NOT NULL,
    window_start_utc timestamptz NOT NULL,
    window_end_utc timestamptz NOT NULL,
    orders_count integer NOT NULL,
    filled_orders_count integer NOT NULL,
    expired_orders_count integer NOT NULL,
    open_orders_count integer NOT NULL,
    entered_runs_count integer NOT NULL,
    skipped_runs_count integer NOT NULL,
    paper_condition_skipped_runs_count integer NOT NULL,
    paper_not_accepted_runs_count integer NOT NULL,
    settled_runs_count integer NOT NULL,
    won_runs_count integer NOT NULL,
    lost_runs_count integer NOT NULL,
    filled_cost_usd numeric(28,8) NOT NULL,
    realized_pnl_usd numeric(28,8) NOT NULL,
    avg_fill_price numeric(28,8) NOT NULL,
    avg_entry_delay_seconds numeric(28,8) NOT NULL,
    max_entry_delay_seconds numeric(28,8) NOT NULL,
    win_rate_pct numeric(28,8) NOT NULL,
    roi_pct numeric(28,8) NOT NULL,
    live_settled_orders_count integer NOT NULL,
    live_skipped_orders_count integer NOT NULL,
    live_condition_skipped_orders_count integer NOT NULL,
    live_technical_skipped_orders_count integer NOT NULL,
    live_ignored_orders_count integer NOT NULL,
    live_ignored_gtd_unfilled_count integer NOT NULL,
    live_ignored_cancelled_orders_count integer NOT NULL,
    live_ignored_rejected_orders_count integer NOT NULL,
    live_won_orders_count integer NOT NULL,
    live_lost_orders_count integer NOT NULL,
    live_realized_pnl_usd numeric(28,8) NOT NULL,
    live_roi_pct numeric(28,8) NOT NULL,
    top_skip_reason text NOT NULL,
    last_order_utc timestamptz NULL,
    last_run_utc timestamptz NULL,
    refreshed_at_utc timestamptz NOT NULL,
    PRIMARY KEY (strategy_id, window_label)
);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_strategy_recent_performance_snapshots_code
ON dashboard_strategy_recent_performance_snapshots (code);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_strategy_recent_performance_snapshots_window_hours
ON dashboard_strategy_recent_performance_snapshots (window_hours);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_strategy_recent_performance_snapshots_refreshed_at
ON dashboard_strategy_recent_performance_snapshots (refreshed_at_utc DESC);

CREATE TABLE IF NOT EXISTS date_dependent_strategy_hourly_paper_pnl (
    strategy_id uuid NOT NULL REFERENCES strategies(id) ON DELETE CASCADE,
    code text NOT NULL,
    name text NOT NULL,
    hour_utc integer NOT NULL CHECK (hour_utc >= 0 AND hour_utc <= 23),
    settled_runs_count integer NOT NULL,
    won_runs_count integer NOT NULL,
    lost_runs_count integer NOT NULL,
    stake_usd numeric(28,8) NOT NULL,
    realized_pnl_usd numeric(28,8) NOT NULL,
    avg_pnl_usd numeric(28,8) NOT NULL,
    first_entered_at_utc timestamptz NULL,
    last_entered_at_utc timestamptz NULL,
    refreshed_at_utc timestamptz NOT NULL,
    PRIMARY KEY (strategy_id, hour_utc)
);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_date_dependent_strategy_hourly_paper_pnl_code_hour
ON date_dependent_strategy_hourly_paper_pnl (code, hour_utc);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_date_dependent_strategy_hourly_paper_pnl_refreshed
ON date_dependent_strategy_hourly_paper_pnl (refreshed_at_utc DESC);

CREATE TABLE IF NOT EXISTS paper_orders (
    id uuid PRIMARY KEY,
    signal_id uuid NOT NULL,
    strategy_id uuid NOT NULL DEFAULT 'f0110a0d-1ead-4c00-8b01-000000000001' REFERENCES strategies(id),
    copied_trader_wallet text NOT NULL DEFAULT '',
    status text NOT NULL,
    side text NOT NULL,
    asset_id text NOT NULL,
    condition_id text NOT NULL,
    outcome text NOT NULL,
    price numeric(18,8) NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    notional_usd numeric(28,8) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    expires_at_utc timestamptz NOT NULL,
    filled_at_utc timestamptz NULL,
    cancelled_at_utc timestamptz NULL,
    raw_decision_json jsonb NULL,
    correlation_id uuid NULL,
    execution_source text NOT NULL DEFAULT ''
);

ALTER TABLE paper_orders ADD COLUMN IF NOT EXISTS strategy_id uuid NOT NULL DEFAULT 'f0110a0d-1ead-4c00-8b01-000000000001' REFERENCES strategies(id);
ALTER TABLE paper_orders ADD COLUMN IF NOT EXISTS copied_trader_wallet text NOT NULL DEFAULT '';
ALTER TABLE paper_orders ADD COLUMN IF NOT EXISTS outcome text NOT NULL DEFAULT '';
ALTER TABLE paper_orders ADD COLUMN IF NOT EXISTS correlation_id uuid NULL;
ALTER TABLE paper_orders ADD COLUMN IF NOT EXISTS execution_source text NOT NULL DEFAULT '';

UPDATE paper_orders order_row
SET copied_trader_wallet = signal.trader_wallet
FROM signals signal
WHERE order_row.signal_id = signal.id
  AND order_row.copied_trader_wallet = '';

CREATE INDEX IF NOT EXISTS ix_paper_orders_copied_wallet_time
ON paper_orders(copied_trader_wallet, created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_paper_orders_strategy_time
ON paper_orders(strategy_id, created_at_utc DESC);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_orders_created_time
ON paper_orders(created_at_utc DESC);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_orders_strategy_perf_cover
ON paper_orders(strategy_id, created_at_utc DESC)
INCLUDE (status, side, notional_usd);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_orders_countertrend_signal_perf
ON paper_orders(strategy_id, created_at_utc DESC)
WHERE raw_decision_json IS NOT NULL
  AND (raw_decision_json ? 'previous_score' OR raw_decision_json ? 'previous_score_bps');

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_orders_id_strategy_side_cover
ON paper_orders(id)
INCLUDE (strategy_id, side);

CREATE INDEX IF NOT EXISTS ix_paper_orders_correlation
ON paper_orders(correlation_id)
WHERE correlation_id IS NOT NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_orders_open_time_asset
ON paper_orders(created_at_utc DESC, asset_id)
WHERE status IN ('Pending', 'PartiallyFilled');

CREATE TABLE IF NOT EXISTS paper_fills (
    id uuid PRIMARY KEY,
    paper_order_id uuid NOT NULL REFERENCES paper_orders(id),
    price numeric(18,8) NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    filled_at_utc timestamptz NOT NULL,
    evidence text NOT NULL,
    realized_pnl_usd numeric(28,8) NOT NULL DEFAULT 0
);

ALTER TABLE paper_fills ADD COLUMN IF NOT EXISTS realized_pnl_usd numeric(28,8) NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS ix_paper_fills_order_time
ON paper_fills(paper_order_id, filled_at_utc ASC);

CREATE INDEX IF NOT EXISTS ix_paper_fills_filled_time_order
ON paper_fills(filled_at_utc, paper_order_id);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_fills_filled_perf_cover
ON paper_fills(filled_at_utc, paper_order_id)
INCLUDE (price, size_shares, realized_pnl_usd);

CREATE TABLE IF NOT EXISTS strategy_market_paper_runs (
    id uuid PRIMARY KEY,
    strategy_id uuid NOT NULL REFERENCES strategies(id),
    market_id text NOT NULL,
    condition_id text NOT NULL,
    market_slug text NOT NULL,
    market_title text NOT NULL,
    category text NULL,
    market_start_utc timestamptz NULL,
    market_end_utc timestamptz NULL,
    detected_at_utc timestamptz NOT NULL,
    entry_due_at_utc timestamptz NOT NULL,
    status text NOT NULL,
    selected_asset_id text NULL,
    selected_outcome text NULL,
    entry_price numeric(18,8) NULL,
    stake_usd numeric(28,8) NOT NULL,
    size_shares numeric(28,8) NULL,
    signal_id uuid NULL REFERENCES signals(id),
    paper_order_id uuid NULL REFERENCES paper_orders(id),
    entered_at_utc timestamptz NULL,
    settlement_price numeric(18,8) NULL,
    settlement_value_usd numeric(28,8) NULL,
    realized_pnl_usd numeric(28,8) NULL,
    settled_at_utc timestamptz NULL,
    skip_reason text NULL,
    skip_diagnostics_json jsonb NULL,
    retention_scope text NOT NULL DEFAULT 'Unknown',
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    UNIQUE (strategy_id, market_id)
);

ALTER TABLE strategy_market_paper_runs ADD COLUMN IF NOT EXISTS skip_diagnostics_json jsonb NULL;
ALTER TABLE strategy_market_paper_runs ADD COLUMN IF NOT EXISTS retention_scope text NOT NULL DEFAULT 'Unknown';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_strategy_market_paper_runs_retention_scope'
          AND conrelid = 'public.strategy_market_paper_runs'::regclass
    ) THEN
        ALTER TABLE strategy_market_paper_runs
            ADD CONSTRAINT ck_strategy_market_paper_runs_retention_scope
            CHECK (retention_scope IN ('Unknown', 'PaperOnly', 'LiveOrShadow')) NOT VALID;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_strategy_market_paper_runs_entry_due
ON strategy_market_paper_runs(strategy_id, status, entry_due_at_utc);

CREATE INDEX IF NOT EXISTS ix_strategy_market_paper_runs_status_entry_due
ON strategy_market_paper_runs(status, entry_due_at_utc, detected_at_utc);

CREATE INDEX IF NOT EXISTS ix_strategy_market_paper_runs_settlement_due
ON strategy_market_paper_runs(strategy_id, status, market_end_utc);

CREATE INDEX IF NOT EXISTS ix_strategy_market_paper_runs_status_market_end
ON strategy_market_paper_runs(status, market_end_utc, entered_at_utc, strategy_id);

CREATE INDEX IF NOT EXISTS ix_strategy_market_paper_runs_order
ON strategy_market_paper_runs(paper_order_id);

CREATE INDEX IF NOT EXISTS ix_strategy_market_paper_runs_strategy_entered
ON strategy_market_paper_runs(strategy_id, entered_at_utc)
WHERE entered_at_utc IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_strategy_market_paper_runs_strategy_updated
ON strategy_market_paper_runs(strategy_id, updated_at_utc);

CREATE INDEX IF NOT EXISTS ix_strategy_market_paper_runs_strategy_settled
ON strategy_market_paper_runs(strategy_id, settled_at_utc)
WHERE settled_at_utc IS NOT NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_strategy_market_paper_runs_updated_time_strategy
ON strategy_market_paper_runs(updated_at_utc, strategy_id);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_strategy_market_paper_runs_entered_time_strategy
ON strategy_market_paper_runs(entered_at_utc, strategy_id)
WHERE entered_at_utc IS NOT NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_strategy_market_paper_runs_settled_time_strategy
ON strategy_market_paper_runs(settled_at_utc, strategy_id)
WHERE settled_at_utc IS NOT NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_strategy_market_paper_runs_retention_candidates
ON strategy_market_paper_runs(updated_at_utc, market_end_utc, strategy_id, id)
WHERE status = 'Skipped' AND retention_scope = 'PaperOnly';

BEGIN;

LOCK TABLE public.strategies IN SHARE ROW EXCLUSIVE MODE;

CREATE TABLE IF NOT EXISTS strategy_live_retention_guards (
    strategy_id uuid PRIMARY KEY REFERENCES strategies(id) ON DELETE CASCADE,
    first_live_observed_at_utc timestamptz NOT NULL,
    last_live_observed_at_utc timestamptz NOT NULL
);

CREATE OR REPLACE FUNCTION public.record_strategy_live_retention_guard()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    was_live boolean := false;
    previous_live_enabled_at_utc timestamptz;
    observed_at_utc timestamptz;
BEGIN
    IF TG_OP = 'UPDATE' THEN
        was_live := OLD.live_stakes;
        previous_live_enabled_at_utc := OLD.live_enabled_at_utc;
    END IF;

    IF NEW.live_stakes OR was_live THEN
        observed_at_utc := COALESCE(
            NEW.live_enabled_at_utc,
            previous_live_enabled_at_utc,
            NEW.updated_at_utc,
            clock_timestamp());

        INSERT INTO public.strategy_live_retention_guards (
            strategy_id, first_live_observed_at_utc, last_live_observed_at_utc)
        VALUES (NEW.id, observed_at_utc, observed_at_utc)
        ON CONFLICT (strategy_id) DO UPDATE SET
            first_live_observed_at_utc = LEAST(
                strategy_live_retention_guards.first_live_observed_at_utc,
                EXCLUDED.first_live_observed_at_utc),
            last_live_observed_at_utc = GREATEST(
                strategy_live_retention_guards.last_live_observed_at_utc,
                EXCLUDED.last_live_observed_at_utc);
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_record_strategy_live_retention_guard ON public.strategies;
CREATE TRIGGER trg_record_strategy_live_retention_guard
AFTER INSERT OR UPDATE OF live_stakes, live_enabled_at_utc
ON public.strategies
FOR EACH ROW
EXECUTE FUNCTION public.record_strategy_live_retention_guard();

INSERT INTO strategy_live_retention_guards (
    strategy_id, first_live_observed_at_utc, last_live_observed_at_utc)
SELECT
    strategy.id,
    COALESCE(strategy.live_enabled_at_utc, strategy.updated_at_utc),
    COALESCE(strategy.live_enabled_at_utc, strategy.updated_at_utc)
FROM strategies strategy
WHERE strategy.live_stakes
ON CONFLICT (strategy_id) DO UPDATE SET
    first_live_observed_at_utc = LEAST(
        strategy_live_retention_guards.first_live_observed_at_utc,
        EXCLUDED.first_live_observed_at_utc),
    last_live_observed_at_utc = GREATEST(
        strategy_live_retention_guards.last_live_observed_at_utc,
        EXCLUDED.last_live_observed_at_utc);

COMMIT;

CREATE TABLE IF NOT EXISTS strategy_paper_skip_rollups (
    strategy_id uuid NOT NULL REFERENCES strategies(id) ON DELETE CASCADE,
    bucket_start_utc timestamptz NOT NULL,
    skip_reason text NOT NULL,
    run_count integer NOT NULL,
    first_updated_at_utc timestamptz NOT NULL,
    last_updated_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    PRIMARY KEY (strategy_id, bucket_start_utc, skip_reason),
    CONSTRAINT ck_strategy_paper_skip_rollups_positive_count CHECK (run_count > 0),
    CONSTRAINT ck_strategy_paper_skip_rollups_utc_day CHECK (
        bucket_start_utc = date_trunc('day', bucket_start_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC')
);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_strategy_paper_skip_rollups_strategy_last
ON strategy_paper_skip_rollups(strategy_id, last_updated_at_utc DESC);

CREATE TABLE IF NOT EXISTS strategy_market_paper_skip_tombstones (
    strategy_id uuid NOT NULL REFERENCES strategies(id) ON DELETE CASCADE,
    market_id text NOT NULL,
    archived_run_id uuid NOT NULL,
    archived_at_utc timestamptz NOT NULL,
    PRIMARY KEY (strategy_id, market_id)
);

CREATE OR REPLACE FUNCTION public.prevent_archived_strategy_market_paper_run_reinsert()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM public.strategy_market_paper_skip_tombstones tombstone
        WHERE tombstone.strategy_id = NEW.strategy_id
          AND tombstone.market_id = NEW.market_id
    ) THEN
        RETURN NULL;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_prevent_archived_strategy_market_paper_run_reinsert
ON public.strategy_market_paper_runs;
CREATE TRIGGER trg_prevent_archived_strategy_market_paper_run_reinsert
BEFORE INSERT ON public.strategy_market_paper_runs
FOR EACH ROW
EXECUTE FUNCTION public.prevent_archived_strategy_market_paper_run_reinsert();

CREATE OR REPLACE FUNCTION public.classify_strategy_market_paper_run_retention_scope()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    current_live_stakes boolean := false;
    has_live_retention_guard boolean := false;
BEGIN
    SELECT
        strategy.live_stakes,
        EXISTS (
            SELECT 1
            FROM public.strategy_live_retention_guards live_guard
            WHERE live_guard.strategy_id = NEW.strategy_id)
    INTO current_live_stakes, has_live_retention_guard
    FROM public.strategies strategy
    WHERE strategy.id = NEW.strategy_id;

    IF TG_OP = 'INSERT' THEN
        NEW.retention_scope := CASE
            WHEN COALESCE(current_live_stakes, false)
              OR COALESCE(has_live_retention_guard, false) THEN 'LiveOrShadow'
            ELSE 'PaperOnly'
        END;
    ELSIF OLD.retention_scope = 'LiveOrShadow'
       OR NEW.retention_scope = 'LiveOrShadow'
       OR COALESCE(current_live_stakes, false)
       OR COALESCE(has_live_retention_guard, false) THEN
        NEW.retention_scope := 'LiveOrShadow';
    ELSE
        NEW.retention_scope := OLD.retention_scope;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_classify_strategy_market_paper_run_retention_scope
ON public.strategy_market_paper_runs;
CREATE TRIGGER trg_classify_strategy_market_paper_run_retention_scope
BEFORE INSERT OR UPDATE OF status, signal_id, paper_order_id, retention_scope
ON public.strategy_market_paper_runs
FOR EACH ROW
EXECUTE FUNCTION public.classify_strategy_market_paper_run_retention_scope();

CREATE OR REPLACE FUNCTION public.promote_active_strategy_runs_to_live_scope()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.live_stakes AND NOT OLD.live_stakes THEN
        UPDATE public.strategy_market_paper_runs run
        SET retention_scope = 'LiveOrShadow'
        WHERE run.strategy_id = NEW.id
          AND run.retention_scope = 'PaperOnly'
          AND run.status IN ('Observed', 'Entered');
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_promote_active_strategy_runs_to_live_scope ON public.strategies;
CREATE TRIGGER trg_promote_active_strategy_runs_to_live_scope
AFTER UPDATE OF live_stakes ON public.strategies
FOR EACH ROW
WHEN (OLD.live_stakes IS DISTINCT FROM NEW.live_stakes)
EXECUTE FUNCTION public.promote_active_strategy_runs_to_live_scope();

CREATE TABLE IF NOT EXISTS strategy_child_parent_assignments (
    id uuid PRIMARY KEY,
    child_strategy_id uuid NOT NULL REFERENCES strategies(id),
    parent_strategy_id uuid NOT NULL REFERENCES strategies(id),
    asset_symbol text NOT NULL,
    lookback_hours integer NOT NULL,
    child_mode text NOT NULL,
    parent_pnl_usd numeric(28,8) NOT NULL,
    parent_roi_pct numeric(28,8) NOT NULL DEFAULT 0,
    assigned_at_utc timestamptz NOT NULL,
    ended_at_utc timestamptz NULL,
    updated_at_utc timestamptz NOT NULL
);

ALTER TABLE strategy_child_parent_assignments
ADD COLUMN IF NOT EXISTS parent_roi_pct numeric(28,8) NOT NULL DEFAULT 0;

CREATE UNIQUE INDEX IF NOT EXISTS ux_strategy_child_parent_assignments_active_child
ON strategy_child_parent_assignments(child_strategy_id)
WHERE ended_at_utc IS NULL;

CREATE INDEX IF NOT EXISTS ix_strategy_child_parent_assignments_active_parent
ON strategy_child_parent_assignments(parent_strategy_id, child_strategy_id)
WHERE ended_at_utc IS NULL;

CREATE INDEX IF NOT EXISTS ix_strategy_child_parent_assignments_child_time
ON strategy_child_parent_assignments(child_strategy_id, assigned_at_utc DESC);

CREATE OR REPLACE FUNCTION public.skip_deleted_strategy_market_paper_run()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM public.strategies strategy WHERE strategy.id = NEW.strategy_id)
       AND (
           lower(COALESCE(NEW.market_slug, '')) LIKE '%updown-15m%'
           OR lower(COALESCE(NEW.market_title, '')) LIKE '% up or down 15m %'
           OR lower(COALESCE(NEW.category, '')) LIKE '%preopen%'
       ) THEN
        RETURN NULL;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_skip_deleted_strategy_market_paper_run ON public.strategy_market_paper_runs;
DROP TRIGGER IF EXISTS trg_skip_deleted_15m_strategy_market_paper_run ON public.strategy_market_paper_runs;
DROP FUNCTION IF EXISTS public.skip_deleted_15m_strategy_market_paper_run();

CREATE TRIGGER trg_skip_deleted_strategy_market_paper_run
BEFORE INSERT ON public.strategy_market_paper_runs
FOR EACH ROW
EXECUTE FUNCTION public.skip_deleted_strategy_market_paper_run();

CREATE TABLE IF NOT EXISTS paper_positions (
    id uuid PRIMARY KEY,
    copied_trader_wallet text NOT NULL DEFAULT '',
    asset_id text NOT NULL,
    condition_id text NOT NULL,
    outcome text NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    average_price numeric(18,8) NOT NULL,
    estimated_value_usd numeric(28,8) NOT NULL,
    unrealized_pnl_usd numeric(28,8) NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

ALTER TABLE paper_positions ADD COLUMN IF NOT EXISTS copied_trader_wallet text NOT NULL DEFAULT '';

DROP INDEX IF EXISTS ux_paper_positions_asset;

CREATE UNIQUE INDEX IF NOT EXISTS ux_paper_positions_wallet_asset
ON paper_positions(copied_trader_wallet, asset_id);

CREATE INDEX IF NOT EXISTS ix_paper_positions_wallet_updated
ON paper_positions(copied_trader_wallet, updated_at_utc DESC);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_positions_updated_page_cover
ON paper_positions(updated_at_utc DESC, copied_trader_wallet, asset_id)
INCLUDE (condition_id, outcome, size_shares, average_price, estimated_value_usd, unrealized_pnl_usd);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_positions_open_updated_cover
ON paper_positions(updated_at_utc DESC, copied_trader_wallet, asset_id)
INCLUDE (condition_id, outcome, size_shares, average_price, estimated_value_usd, unrealized_pnl_usd)
WHERE size_shares > 0;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_positions_open_condition_lookup
ON paper_positions(lower(condition_id), updated_at_utc DESC, copied_trader_wallet, asset_id)
INCLUDE (condition_id, outcome, size_shares, average_price, estimated_value_usd, unrealized_pnl_usd)
WHERE size_shares > 0;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_positions_open_asset_lookup
ON paper_positions(lower(asset_id), updated_at_utc DESC, copied_trader_wallet, asset_id)
INCLUDE (condition_id, outcome, size_shares, average_price, estimated_value_usd, unrealized_pnl_usd)
WHERE size_shares > 0;

CREATE TABLE IF NOT EXISTS paper_position_settlements (
    id uuid PRIMARY KEY,
    copied_trader_wallet text NOT NULL,
    asset_id text NOT NULL,
    condition_id text NOT NULL,
    outcome text NOT NULL,
    winning_asset_id text NULL,
    winning_outcome text NOT NULL,
    category text NULL,
    settled_size_shares numeric(28,8) NOT NULL,
    average_price numeric(18,8) NOT NULL,
    cost_basis_usd numeric(28,8) NOT NULL,
    settlement_value_usd numeric(28,8) NOT NULL,
    realized_pnl_usd numeric(28,8) NOT NULL,
    won boolean NOT NULL,
    settlement_source text NOT NULL,
    settled_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_paper_position_settlements_wallet_asset
ON paper_position_settlements(copied_trader_wallet, asset_id);

CREATE INDEX IF NOT EXISTS ix_paper_position_settlements_wallet_time
ON paper_position_settlements(copied_trader_wallet, settled_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_paper_position_settlements_condition
ON paper_position_settlements(condition_id, settled_at_utc DESC);

CREATE TABLE IF NOT EXISTS paper_copied_trader_performance (
    copied_trader_wallet text NOT NULL,
    category text NOT NULL,
    orders_count integer NOT NULL,
    filled_orders_count integer NOT NULL,
    buy_fills_count integer NOT NULL,
    sell_fills_count integer NOT NULL,
    open_positions_count integer NOT NULL,
    settled_positions_count integer NOT NULL,
    won_positions_count integer NOT NULL,
    lost_positions_count integer NOT NULL,
    buy_cost_usd numeric(28,8) NOT NULL,
    sell_proceeds_usd numeric(28,8) NOT NULL,
    settlement_value_usd numeric(28,8) NOT NULL,
    realized_pnl_usd numeric(28,8) NOT NULL,
    unrealized_pnl_usd numeric(28,8) NOT NULL,
    total_pnl_usd numeric(28,8) NOT NULL,
    roi_pct numeric(18,8) NOT NULL,
    win_rate_pct numeric(18,8) NOT NULL,
    score numeric(28,8) NOT NULL,
    first_order_utc timestamptz NULL,
    last_order_utc timestamptz NULL,
    refreshed_at_utc timestamptz NOT NULL,
    PRIMARY KEY (copied_trader_wallet, category)
);

CREATE INDEX IF NOT EXISTS ix_paper_copied_trader_performance_score
ON paper_copied_trader_performance(category, score DESC, total_pnl_usd DESC);

CREATE TABLE IF NOT EXISTS btc_usd_reference_correlation_samples (
    id uuid PRIMARY KEY,
    binance_price_usd numeric(28,8) NOT NULL,
    binance_source_updated_at_utc timestamptz NOT NULL,
    binance_fetched_at_utc timestamptz NOT NULL,
    chainlink_price_usd numeric(28,8) NOT NULL,
    chainlink_valid_after_utc timestamptz NOT NULL,
    time_delta_seconds numeric(18,8) NOT NULL,
    price_diff_usd numeric(28,8) NOT NULL,
    price_diff_bps numeric(18,8) NOT NULL,
    chainlink_feed_id text NOT NULL,
    chainlink_query_window text NOT NULL,
    raw_json jsonb NOT NULL,
    created_at_utc timestamptz NOT NULL,
    UNIQUE (binance_source_updated_at_utc, chainlink_valid_after_utc)
);

CREATE INDEX IF NOT EXISTS ix_btc_usd_reference_correlation_samples_created
ON btc_usd_reference_correlation_samples(created_at_utc DESC);

CREATE TABLE IF NOT EXISTS crypto_reference_price_ticks (
    id uuid PRIMARY KEY,
    asset_symbol text NOT NULL,
    binance_symbol text NOT NULL,
    sampled_at_utc timestamptz NOT NULL,
    bucket_start_utc timestamptz NOT NULL,
    price_usd numeric(28,8) NOT NULL,
    source_updated_at_utc timestamptz NOT NULL,
    fetched_at_utc timestamptz NOT NULL,
    source text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT ux_crypto_reference_price_ticks_asset_bucket UNIQUE (asset_symbol, bucket_start_utc)
);

CREATE INDEX IF NOT EXISTS ix_crypto_reference_price_ticks_asset_sampled
ON crypto_reference_price_ticks(asset_symbol, sampled_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_crypto_reference_price_ticks_asset_bucket
ON crypto_reference_price_ticks(asset_symbol, bucket_start_utc DESC);

CREATE TABLE IF NOT EXISTS btc_order_book_lag_diagnostic_events (
    id uuid PRIMARY KEY,
    source text NOT NULL,
    event_type text NOT NULL,
    asset_id text NULL,
    condition_id text NULL,
    binance_symbol text NULL,
    binance_price_usd numeric(28,8) NULL,
    best_bid numeric(18,8) NULL,
    best_bid_size numeric(28,8) NULL,
    best_ask numeric(18,8) NULL,
    best_ask_size numeric(28,8) NULL,
    mid numeric(18,8) NULL,
    trade_price numeric(18,8) NULL,
    trade_size numeric(28,8) NULL,
    source_timestamp_utc timestamptz NULL,
    received_at_utc timestamptz NOT NULL,
    local_lag_ms numeric(18,8) NULL,
    raw_event_type text NOT NULL DEFAULT '',
    created_at_utc timestamptz NOT NULL
);

ALTER TABLE btc_order_book_lag_diagnostic_events ADD COLUMN IF NOT EXISTS best_bid_size numeric(28,8) NULL;
ALTER TABLE btc_order_book_lag_diagnostic_events ADD COLUMN IF NOT EXISTS best_ask_size numeric(28,8) NULL;

CREATE INDEX IF NOT EXISTS ix_btc_order_book_lag_events_received
ON btc_order_book_lag_diagnostic_events(received_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_btc_order_book_lag_events_source_received
ON btc_order_book_lag_diagnostic_events(source, received_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_btc_order_book_lag_events_asset_received
ON btc_order_book_lag_diagnostic_events(asset_id, received_at_utc DESC)
WHERE asset_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_btc_order_book_lag_events_condition_received
ON btc_order_book_lag_diagnostic_events(condition_id, received_at_utc DESC)
WHERE condition_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS btc_up_down_5m_strategy_stage_timings (
    id uuid PRIMARY KEY,
    cycle_id uuid NOT NULL,
    cycle_kind text NOT NULL,
    flow_name text NULL,
    stage_name text NOT NULL,
    detail text NULL,
    started_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NOT NULL,
    duration_ms bigint NOT NULL,
    variant_count integer NULL,
    run_count integer NULL,
    entries_placed integer NULL,
    runs_skipped integer NULL,
    runs_settled integer NULL,
    markets_observed integer NULL,
    earliest_entry_due_at_utc timestamptz NULL,
    latest_entry_due_at_utc timestamptz NULL,
    succeeded boolean NOT NULL,
    error_message text NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_strategy_stage_timings_started
ON btc_up_down_5m_strategy_stage_timings(started_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_strategy_stage_timings_cycle
ON btc_up_down_5m_strategy_stage_timings(cycle_id, started_at_utc);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_strategy_stage_timings_stage
ON btc_up_down_5m_strategy_stage_timings(stage_name, started_at_utc DESC);

CREATE TABLE IF NOT EXISTS btc_up_down_5m_odds_ticks (
    id uuid PRIMARY KEY,
    market_id text NOT NULL,
    condition_id text NOT NULL,
    market_slug text NOT NULL,
    market_start_utc timestamptz NOT NULL,
    market_end_utc timestamptz NOT NULL,
    sampled_at_utc timestamptz NOT NULL,
    seconds_after_start numeric(18,8) NOT NULL,
    seconds_to_close numeric(18,8) NOT NULL,
    binance_price_usd numeric(28,8) NOT NULL,
    binance_source_updated_at_utc timestamptz NOT NULL,
    binance_fetched_at_utc timestamptz NOT NULL,
    binance_start_price_usd numeric(28,8) NOT NULL,
    btc_move_from_start_usd numeric(28,8) NOT NULL,
    btc_move_from_start_bps numeric(18,8) NOT NULL,
    up_asset_id text NOT NULL,
    up_best_bid numeric(18,8) NULL,
    up_best_ask numeric(18,8) NULL,
    up_mid numeric(18,8) NULL,
    up_price_proxy numeric(18,8) NULL,
    up_price_proxy_kind text NOT NULL,
    up_last_trade_price numeric(18,8) NULL,
    up_book_source text NOT NULL,
    up_book_age_ms numeric(18,8) NULL,
    down_asset_id text NOT NULL,
    down_best_bid numeric(18,8) NULL,
    down_best_ask numeric(18,8) NULL,
    down_mid numeric(18,8) NULL,
    down_price_proxy numeric(18,8) NULL,
    down_price_proxy_kind text NOT NULL,
    down_last_trade_price numeric(18,8) NULL,
    down_book_source text NOT NULL,
    down_book_age_ms numeric(18,8) NULL,
    diagnostics_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_odds_ticks_market_time
ON btc_up_down_5m_odds_ticks(market_id, sampled_at_utc);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_odds_ticks_sampled
ON btc_up_down_5m_odds_ticks(sampled_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_odds_ticks_start
ON btc_up_down_5m_odds_ticks(market_start_utc, sampled_at_utc);

CREATE TABLE IF NOT EXISTS btc_5m_history (
    id bigserial PRIMARY KEY,
    seconds integer NOT NULL,
    cents integer NOT NULL,
    count integer NOT NULL DEFAULT 0,
    up_count integer NOT NULL DEFAULT 0,
    down_count integer NOT NULL DEFAULT 0,
    CONSTRAINT ux_btc_5m_history_seconds_cents UNIQUE (seconds, cents)
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'btc_5m_history'::regclass
          AND conname = 'ux_btc_5m_history_seconds_cents'
    ) THEN
        ALTER TABLE btc_5m_history
        ADD CONSTRAINT ux_btc_5m_history_seconds_cents UNIQUE (seconds, cents);
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS btc_5m_history_live_observations (
    id uuid PRIMARY KEY,
    market_id text NOT NULL,
    condition_id text NOT NULL,
    market_slug text NOT NULL,
    market_start_utc timestamptz NOT NULL,
    market_end_utc timestamptz NOT NULL,
    sampled_at_utc timestamptz NOT NULL,
    seconds integer NOT NULL,
    cents integer NOT NULL,
    binance_price_usd numeric(28,8) NOT NULL,
    binance_start_price_usd numeric(28,8) NOT NULL,
    btc_move_from_start_usd numeric(28,8) NOT NULL,
    result text NULL,
    applied_to_history boolean NOT NULL DEFAULT false,
    applied_at_utc timestamptz NULL,
    result_check_attempts integer NOT NULL DEFAULT 0,
    next_result_check_utc timestamptz NOT NULL DEFAULT now(),
    last_result_error text NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    UNIQUE (market_id, seconds)
);

CREATE INDEX IF NOT EXISTS ix_btc_5m_history_live_observations_due
ON btc_5m_history_live_observations(applied_to_history, next_result_check_utc, market_end_utc);

CREATE INDEX IF NOT EXISTS ix_btc_5m_history_live_observations_market
ON btc_5m_history_live_observations(market_id, sampled_at_utc);

CREATE TABLE IF NOT EXISTS btc_up_down_5m_statistics_ticks (
    id uuid PRIMARY KEY,
    market_id text NOT NULL,
    condition_id text NOT NULL,
    market_slug text NOT NULL,
    market_start_utc timestamptz NOT NULL,
    market_end_utc timestamptz NOT NULL,
    sampled_at_utc timestamptz NOT NULL,
    seconds_after_start numeric(18,8) NOT NULL,
    seconds_to_close numeric(18,8) NOT NULL,
    binance_price_usd numeric(28,8) NOT NULL,
    binance_source_updated_at_utc timestamptz NOT NULL,
    binance_fetched_at_utc timestamptz NOT NULL,
    binance_start_price_usd numeric(28,8) NULL,
    btc_move_from_start_usd numeric(28,8) NULL,
    btc_move_from_start_cents numeric(28,8) NULL,
    seconds_lower integer NULL,
    seconds_upper integer NULL,
    cents_lower integer NULL,
    cents_upper integer NULL,
    effective_count numeric(28,8) NULL,
    up_probability numeric(18,8) NULL,
    down_probability numeric(18,8) NULL,
    support_threshold integer NOT NULL,
    history_rows_found integer NOT NULL,
    missing_history_corners integer NOT NULL,
    interpolation_method text NOT NULL,
    up_asset_id text NOT NULL,
    up_market_price numeric(18,8) NULL,
    up_market_price_kind text NOT NULL,
    down_asset_id text NOT NULL,
    down_market_price numeric(18,8) NULL,
    down_market_price_kind text NOT NULL,
    up_edge numeric(18,8) NULL,
    down_edge numeric(18,8) NULL,
    decision_code text NOT NULL,
    recommended_outcome text NULL,
    would_bet boolean NOT NULL,
    diagnostics_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_statistics_ticks_sampled
ON btc_up_down_5m_statistics_ticks(sampled_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_statistics_ticks_market_time
ON btc_up_down_5m_statistics_ticks(market_id, sampled_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_statistics_ticks_decision
ON btc_up_down_5m_statistics_ticks(decision_code, sampled_at_utc DESC);

CREATE TABLE IF NOT EXISTS btc_up_down_5m_arbitrage_scans (
    id uuid PRIMARY KEY,
    market_id text NOT NULL,
    condition_id text NOT NULL,
    market_slug text NOT NULL,
    market_start_utc timestamptz NOT NULL,
    market_end_utc timestamptz NOT NULL,
    sampled_at_utc timestamptz NOT NULL,
    seconds_after_start numeric(18,8) NOT NULL,
    seconds_to_close numeric(18,8) NOT NULL,
    up_asset_id text NOT NULL,
    up_best_bid numeric(18,8) NULL,
    up_best_ask numeric(18,8) NULL,
    up_ask_depth_shares numeric(28,8) NULL,
    up_book_source text NOT NULL,
    up_book_age_ms numeric(18,8) NULL,
    down_asset_id text NOT NULL,
    down_best_bid numeric(18,8) NULL,
    down_best_ask numeric(18,8) NULL,
    down_ask_depth_shares numeric(28,8) NULL,
    down_book_source text NOT NULL,
    down_book_age_ms numeric(18,8) NULL,
    required_min_shares numeric(28,8) NOT NULL,
    max_common_executable_shares numeric(28,8) NOT NULL,
    best_executable_shares numeric(28,8) NULL,
    up_cost_usd numeric(28,8) NULL,
    down_cost_usd numeric(28,8) NULL,
    total_cost_usd numeric(28,8) NULL,
    guaranteed_payout_usd numeric(28,8) NULL,
    gross_profit_usd numeric(28,8) NULL,
    safety_buffer_usd numeric(28,8) NULL,
    net_profit_usd numeric(28,8) NULL,
    average_cost_per_share numeric(18,8) NULL,
    edge_per_share numeric(18,8) NULL,
    safety_buffer_per_share numeric(18,8) NOT NULL,
    min_net_profit_usd numeric(28,8) NOT NULL,
    decision_code text NOT NULL,
    would_arbitrage boolean NOT NULL,
    diagnostics_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_arbitrage_scans_sampled
ON btc_up_down_5m_arbitrage_scans(sampled_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_arbitrage_scans_market_time
ON btc_up_down_5m_arbitrage_scans(market_id, sampled_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_arbitrage_scans_decision
ON btc_up_down_5m_arbitrage_scans(would_arbitrage, decision_code, sampled_at_utc DESC);

CREATE TABLE IF NOT EXISTS btc_up_down_5m_result_streak_diagnostics (
    id uuid PRIMARY KEY,
    market_id text NOT NULL,
    condition_id text NOT NULL,
    market_slug text NOT NULL,
    market_start_utc timestamptz NOT NULL,
    market_end_utc timestamptz NULL,
    sampled_at_utc timestamptz NOT NULL,
    latest_previous_market_id text NULL,
    latest_previous_market_slug text NULL,
    latest_previous_market_start_utc timestamptz NULL,
    latest_previous_market_end_utc timestamptz NULL,
    streak_winning_outcome text NULL,
    base_selected_direction text NULL,
    selected_outcome text NULL,
    close_book_streak_result_count integer NOT NULL,
    cumulative_move_market_count integer NOT NULL,
    latest_move_bps numeric(28,12) NULL,
    latest_abs_move_bps numeric(28,12) NULL,
    cumulative_move_bps numeric(28,12) NULL,
    cumulative_abs_move_bps numeric(28,12) NULL,
    rejection_reason text NULL,
    streak_truncated_reason text NULL,
    diagnostics_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT ux_btc_up_down_5m_result_streak_diagnostics_market UNIQUE (market_id)
);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_result_streak_diagnostics_sampled
ON btc_up_down_5m_result_streak_diagnostics(sampled_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_btc_up_down_5m_result_streak_diagnostics_streak
ON btc_up_down_5m_result_streak_diagnostics(close_book_streak_result_count DESC, cumulative_abs_move_bps DESC);

CREATE TABLE IF NOT EXISTS crypto_up_down_5m_odds_ticks (
    id uuid PRIMARY KEY,
    asset_symbol text NOT NULL,
    binance_symbol text NOT NULL,
    market_id text NOT NULL,
    condition_id text NOT NULL,
    market_slug text NOT NULL,
    market_start_utc timestamptz NOT NULL,
    market_end_utc timestamptz NOT NULL,
    sampled_at_utc timestamptz NOT NULL,
    seconds_after_start numeric(18,8) NOT NULL,
    seconds_to_close numeric(18,8) NOT NULL,
    binance_price_usd numeric(28,8) NOT NULL,
    binance_source_updated_at_utc timestamptz NOT NULL,
    binance_fetched_at_utc timestamptz NOT NULL,
    binance_start_price_usd numeric(28,8) NOT NULL,
    asset_move_from_start_usd numeric(28,8) NOT NULL,
    asset_move_from_start_bps numeric(18,8) NOT NULL,
    up_asset_id text NOT NULL,
    up_best_bid numeric(18,8) NULL,
    up_best_ask numeric(18,8) NULL,
    up_mid numeric(18,8) NULL,
    up_price_proxy numeric(18,8) NULL,
    up_price_proxy_kind text NOT NULL,
    up_last_trade_price numeric(18,8) NULL,
    up_book_source text NOT NULL,
    up_book_age_ms numeric(18,8) NULL,
    down_asset_id text NOT NULL,
    down_best_bid numeric(18,8) NULL,
    down_best_ask numeric(18,8) NULL,
    down_mid numeric(18,8) NULL,
    down_price_proxy numeric(18,8) NULL,
    down_price_proxy_kind text NOT NULL,
    down_last_trade_price numeric(18,8) NULL,
    down_book_source text NOT NULL,
    down_book_age_ms numeric(18,8) NULL,
    diagnostics_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_odds_ticks_asset_market_time
ON crypto_up_down_5m_odds_ticks(asset_symbol, market_id, sampled_at_utc);

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_odds_ticks_sampled
ON crypto_up_down_5m_odds_ticks(sampled_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_odds_ticks_asset_start
ON crypto_up_down_5m_odds_ticks(asset_symbol, market_start_utc, sampled_at_utc);

CREATE TABLE IF NOT EXISTS crypto_up_down_5m_diff_snapshots (
    id uuid PRIMARY KEY,
    asset_symbol text NOT NULL,
    market_start_utc timestamptz NOT NULL,
    sampled_at_utc timestamptz NOT NULL,
    counter_start_market_start_utc timestamptz NULL,
    last_included_market_start_utc timestamptz NULL,
    high_water_market_start_utc timestamptz NULL,
    counter_initialized boolean NOT NULL,
    up_count integer NOT NULL,
    down_count integer NOT NULL,
    diff_count integer NOT NULL DEFAULT 0,
    diff integer NOT NULL,
    processed_market_count integer NOT NULL,
    history_fetch_failed_at_utc timestamptz NULL,
    history_fetch_retry_after_utc timestamptz NULL,
    history_fetch_error text NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT ux_crypto_up_down_5m_diff_snapshots_asset_market UNIQUE (asset_symbol, market_start_utc)
);

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_diff_snapshots_asset_market
ON crypto_up_down_5m_diff_snapshots(asset_symbol, market_start_utc);

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_diff_snapshots_asset_sampled
ON crypto_up_down_5m_diff_snapshots(asset_symbol, sampled_at_utc);

ALTER TABLE crypto_up_down_5m_diff_snapshots
    ADD COLUMN IF NOT EXISTS diff_count integer NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS crypto_up_down_5m_diff_shift_progress_states (
    strategy_id uuid PRIMARY KEY,
    asset_symbol text NOT NULL,
    trigger_outcome text NOT NULL,
    up_count integer NOT NULL DEFAULT 0,
    down_count integer NOT NULL DEFAULT 0,
    sum_amount numeric(28,8) NOT NULL DEFAULT 0,
    damping_active boolean NOT NULL DEFAULT false,
    damping_direction text NULL,
    last_processed_market_start_utc timestamptz NULL,
    pending_market_start_utc timestamptz NULL,
    pending_target_outcome text NULL,
    pending_stake_usd numeric(28,8) NULL,
    pending_created_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

ALTER TABLE crypto_up_down_5m_diff_shift_progress_states
    ADD COLUMN IF NOT EXISTS damping_active boolean NOT NULL DEFAULT false;

ALTER TABLE crypto_up_down_5m_diff_shift_progress_states
    ADD COLUMN IF NOT EXISTS damping_direction text NULL;

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_diff_shift_progress_states_asset
ON crypto_up_down_5m_diff_shift_progress_states(asset_symbol, trigger_outcome);

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_diff_shift_progress_states_pending
ON crypto_up_down_5m_diff_shift_progress_states(pending_market_start_utc)
WHERE pending_market_start_utc IS NOT NULL;

CREATE TABLE IF NOT EXISTS crypto_up_down_5m_result_polling_observations (
    id uuid PRIMARY KEY,
    asset_symbol text NOT NULL,
    market_id text NOT NULL,
    condition_id text NOT NULL,
    market_slug text NOT NULL,
    market_start_utc timestamptz NOT NULL,
    market_end_utc timestamptz NOT NULL,
    first_observed_ended_at_utc timestamptz NOT NULL,
    polling_started_at_utc timestamptz NOT NULL,
    last_poll_at_utc timestamptz NULL,
    poll_attempts integer NOT NULL,
    first_closed_at_utc timestamptz NULL,
    first_winner_at_utc timestamptz NULL,
    winning_outcome text NULL,
    closed_delay_seconds numeric(18,3) NULL,
    result_delay_seconds numeric(18,3) NULL,
    status text NOT NULL,
    last_response_status text NOT NULL,
    last_error text NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT ux_crypto_up_down_5m_result_polling_market UNIQUE (market_id),
    CONSTRAINT ux_crypto_up_down_5m_result_polling_asset_start UNIQUE (asset_symbol, market_start_utc)
);

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_result_polling_status
ON crypto_up_down_5m_result_polling_observations(status, market_end_utc);

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_result_polling_asset_market
ON crypto_up_down_5m_result_polling_observations(asset_symbol, market_start_utc);

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_result_polling_winner
ON crypto_up_down_5m_result_polling_observations(first_winner_at_utc)
WHERE first_winner_at_utc IS NOT NULL;

CREATE TABLE IF NOT EXISTS crypto_up_down_5m_websocket_resolved_markets (
    id uuid PRIMARY KEY,
    asset_symbol text NOT NULL,
    market_id text NOT NULL,
    condition_id text NOT NULL,
    market_slug text NOT NULL,
    market_start_utc timestamptz NOT NULL,
    market_end_utc timestamptz NOT NULL,
    winning_outcome text NOT NULL,
    winning_asset_id text NULL,
    event_timestamp_utc timestamptz NOT NULL,
    first_received_at_utc timestamptz NOT NULL,
    last_received_at_utc timestamptz NOT NULL,
    event_count integer NOT NULL,
    result_delay_seconds numeric(18,3) NOT NULL,
    source text NOT NULL,
    raw_event_type text NOT NULL,
    raw_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT ux_crypto_up_down_5m_websocket_resolved_asset_market UNIQUE (asset_symbol, market_start_utc)
);

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_websocket_resolved_asset_market
ON crypto_up_down_5m_websocket_resolved_markets(asset_symbol, market_start_utc);

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_websocket_resolved_condition
ON crypto_up_down_5m_websocket_resolved_markets(condition_id);

CREATE INDEX IF NOT EXISTS ix_crypto_up_down_5m_websocket_resolved_received
ON crypto_up_down_5m_websocket_resolved_markets(first_received_at_utc);

CREATE TABLE IF NOT EXISTS market_resolved_event_diagnostics (
    id uuid PRIMARY KEY,
    component text NOT NULL,
    raw_event_type text NOT NULL,
    asset_id text NULL,
    condition_id text NULL,
    winning_asset_id text NULL,
    winning_outcome text NULL,
    event_timestamp_utc timestamptz NOT NULL,
    received_at_utc timestamptz NOT NULL,
    active_snapshot_found boolean NOT NULL,
    snapshot_market_id text NULL,
    snapshot_condition_id text NULL,
    snapshot_market_slug text NULL,
    snapshot_asset_symbol text NULL,
    snapshot_market_start_utc timestamptz NULL,
    snapshot_is_crypto_up_down_5m boolean NOT NULL,
    recorder_action text NOT NULL,
    raw_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_market_resolved_event_diagnostics_received
ON market_resolved_event_diagnostics(received_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_market_resolved_event_diagnostics_asset_received
ON market_resolved_event_diagnostics(asset_id, received_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_market_resolved_event_diagnostics_action_received
ON market_resolved_event_diagnostics(recorder_action, received_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_market_resolved_event_diagnostics_snapshot_asset_start
ON market_resolved_event_diagnostics(snapshot_asset_symbol, snapshot_market_start_utc);

CREATE TABLE IF NOT EXISTS market_websocket_frame_diagnostics (
    id uuid PRIMARY KEY,
    component text NOT NULL,
    received_at_utc timestamptz NOT NULL,
    frame_kind text NOT NULL,
    payload_length_chars integer NOT NULL,
    payload_sha256 text NOT NULL,
    event_count integer NOT NULL,
    event_types_json jsonb NOT NULL DEFAULT '[]'::jsonb,
    asset_ids_json jsonb NOT NULL DEFAULT '[]'::jsonb,
    market_ids_json jsonb NOT NULL DEFAULT '[]'::jsonb,
    contains_market_resolved_text boolean NOT NULL,
    contains_resolved_text boolean NOT NULL,
    parse_succeeded boolean NOT NULL,
    parsed_update_count integer NOT NULL,
    parse_error text NULL,
    raw_payload text NOT NULL,
    raw_payload_truncated boolean NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_market_websocket_frame_diagnostics_component_received
ON market_websocket_frame_diagnostics(component, received_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_market_websocket_frame_diagnostics_received
ON market_websocket_frame_diagnostics(received_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_market_websocket_frame_diagnostics_resolved_text
ON market_websocket_frame_diagnostics(received_at_utc DESC)
WHERE contains_resolved_text;

CREATE INDEX IF NOT EXISTS ix_market_websocket_frame_diagnostics_event_types
ON market_websocket_frame_diagnostics USING gin(event_types_json);

CREATE TABLE IF NOT EXISTS paper_copied_leader_positions (
    id uuid PRIMARY KEY,
    entry_signal_id uuid NOT NULL,
    entry_paper_order_id uuid NOT NULL,
    copied_trader_wallet text NOT NULL,
    asset_id text NOT NULL,
    condition_id text NOT NULL,
    outcome text NOT NULL,
    entry_transaction_hash text NULL,
    entry_timestamp_utc timestamptz NOT NULL,
    leader_entry_price numeric(18,8) NOT NULL,
    leader_initial_size_shares numeric(28,8) NOT NULL,
    copied_initial_size_shares numeric(28,8) NOT NULL DEFAULT 0,
    leader_sold_size_shares numeric(28,8) NOT NULL DEFAULT 0,
    copied_exit_requested_size_shares numeric(28,8) NOT NULL DEFAULT 0,
    status text NOT NULL,
    last_activity_timestamp_utc timestamptz NULL,
    last_activity_transaction_hash text NULL,
    last_activity_sync_at_utc timestamptz NULL,
    next_activity_sync_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    UNIQUE (entry_paper_order_id)
);

CREATE INDEX IF NOT EXISTS ix_paper_copied_leader_positions_due
ON paper_copied_leader_positions(status, next_activity_sync_at_utc, copied_trader_wallet);

CREATE INDEX IF NOT EXISTS ix_paper_copied_leader_positions_wallet_asset
ON paper_copied_leader_positions(copied_trader_wallet, asset_id, status);

CREATE TABLE IF NOT EXISTS paper_copied_leader_activity_events (
    id uuid PRIMARY KEY,
    dedup_key text NOT NULL,
    copied_trader_wallet text NOT NULL,
    asset_id text NOT NULL,
    condition_id text NOT NULL,
    side text NOT NULL,
    price numeric(18,8) NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    usdc_size numeric(28,8) NOT NULL,
    transaction_hash text NULL,
    activity_timestamp_utc timestamptz NOT NULL,
    raw_json jsonb NOT NULL,
    observed_at_utc timestamptz NOT NULL,
    UNIQUE (dedup_key)
);

CREATE INDEX IF NOT EXISTS ix_paper_copied_leader_activity_events_wallet_asset_time
ON paper_copied_leader_activity_events(copied_trader_wallet, asset_id, activity_timestamp_utc DESC);

CREATE TABLE IF NOT EXISTS dry_run_orders (
    id uuid PRIMARY KEY,
    signal_id uuid NOT NULL,
    strategy_id uuid NOT NULL DEFAULT 'f0110a0d-1ead-4c00-8b01-000000000001' REFERENCES strategies(id),
    status text NOT NULL,
    side text NOT NULL,
    asset_id text NOT NULL,
    condition_id text NOT NULL,
    outcome text NOT NULL,
    price numeric(18,8) NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    notional_usd numeric(28,8) NOT NULL,
    order_type text NOT NULL,
    payload_json jsonb NOT NULL,
    validation_summary text NOT NULL,
    created_at_utc timestamptz NOT NULL
);

ALTER TABLE dry_run_orders ADD COLUMN IF NOT EXISTS strategy_id uuid NOT NULL DEFAULT 'f0110a0d-1ead-4c00-8b01-000000000001' REFERENCES strategies(id);

CREATE INDEX IF NOT EXISTS ix_dry_run_orders_created
ON dry_run_orders(created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_dry_run_orders_strategy_time
ON dry_run_orders(strategy_id, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS live_orders (
    id uuid PRIMARY KEY,
    signal_id uuid NOT NULL,
    strategy_id uuid NOT NULL DEFAULT 'f0110a0d-1ead-4c00-8b01-000000000001' REFERENCES strategies(id),
    status text NOT NULL,
    order_id text NULL,
    side text NOT NULL,
    asset_id text NOT NULL,
    condition_id text NOT NULL,
    outcome text NOT NULL,
    price numeric(18,8) NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    notional_usd numeric(28,8) NOT NULL,
    order_type text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    expires_at_utc timestamptz NOT NULL,
    submitted_at_utc timestamptz NULL,
    response_status text NOT NULL,
    filled_size numeric(28,8) NOT NULL,
    remaining_size numeric(28,8) NOT NULL,
    average_fill_price numeric(18,8) NULL,
    filled_notional_usd numeric(28,8) NOT NULL DEFAULT 0,
    cost_basis_usd numeric(28,8) NOT NULL DEFAULT 0,
    fee_usd numeric(28,8) NOT NULL DEFAULT 0,
    cancel_status text NOT NULL,
    raw_response_json jsonb NOT NULL,
    validation_summary text NOT NULL,
    balance_effect_applied boolean NOT NULL DEFAULT false,
    settlement_value_usd numeric(28,8) NULL,
    realized_pnl_usd numeric(28,8) NULL,
    settled_at_utc timestamptz NULL,
    winning_asset_id text NULL,
    winning_outcome text NULL,
    won boolean NULL,
    settlement_source text NOT NULL DEFAULT '',
    correlation_id uuid NULL,
    execution_source text NOT NULL DEFAULT '',
    post_only boolean NULL,
    paper_order_id uuid NULL REFERENCES paper_orders(id),
    updated_at_utc timestamptz NOT NULL
);

ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS strategy_id uuid NOT NULL DEFAULT 'f0110a0d-1ead-4c00-8b01-000000000001' REFERENCES strategies(id);
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS balance_effect_applied boolean NOT NULL DEFAULT false;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS average_fill_price numeric(18,8) NULL;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS filled_notional_usd numeric(28,8) NOT NULL DEFAULT 0;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS cost_basis_usd numeric(28,8) NOT NULL DEFAULT 0;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS fee_usd numeric(28,8) NOT NULL DEFAULT 0;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS settlement_value_usd numeric(28,8) NULL;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS realized_pnl_usd numeric(28,8) NULL;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS settled_at_utc timestamptz NULL;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS winning_asset_id text NULL;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS winning_outcome text NULL;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS won boolean NULL;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS settlement_source text NOT NULL DEFAULT '';
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS correlation_id uuid NULL;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS execution_source text NOT NULL DEFAULT '';
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS post_only boolean NULL;
ALTER TABLE live_orders ADD COLUMN IF NOT EXISTS paper_order_id uuid NULL REFERENCES paper_orders(id);

UPDATE live_orders
SET average_fill_price = COALESCE(average_fill_price, CASE WHEN filled_size > 0 THEN price ELSE NULL END),
    filled_notional_usd = CASE
        WHEN filled_notional_usd > 0 THEN filled_notional_usd
        WHEN filled_size > 0 THEN price * filled_size
        ELSE filled_notional_usd
    END,
    cost_basis_usd = CASE
        WHEN cost_basis_usd > 0 THEN cost_basis_usd
        WHEN filled_size > 0 THEN (price * filled_size) + fee_usd
        ELSE cost_basis_usd
    END,
    won = COALESCE(won, CASE
        WHEN settled_at_utc IS NULL OR realized_pnl_usd IS NULL THEN NULL
        WHEN COALESCE(settlement_value_usd, 0) > 0 THEN true
        ELSE false
    END),
    settlement_source = CASE
        WHEN settlement_source <> '' THEN settlement_source
        WHEN settled_at_utc IS NOT NULL THEN 'legacy_live_order_settlement'
        ELSE settlement_source
    END
WHERE (average_fill_price IS NULL AND filled_size > 0)
   OR (filled_notional_usd = 0 AND filled_size > 0)
   OR (cost_basis_usd = 0 AND filled_size > 0)
   OR (won IS NULL AND settled_at_utc IS NOT NULL)
   OR (settlement_source = '' AND settled_at_utc IS NOT NULL);

CREATE INDEX IF NOT EXISTS ix_live_orders_open
ON live_orders(status, created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_live_orders_order_id
ON live_orders(order_id);

CREATE INDEX IF NOT EXISTS ix_live_orders_strategy_time
ON live_orders(strategy_id, created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_live_orders_correlation
ON live_orders(correlation_id)
WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_live_orders_paper_order
ON live_orders(paper_order_id)
WHERE paper_order_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_live_orders_strategy_settlement
ON live_orders(strategy_id, settled_at_utc DESC)
WHERE settled_at_utc IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_live_orders_pending_balance_settlement
ON live_orders(status, balance_effect_applied, updated_at_utc)
WHERE status = 'Matched' AND balance_effect_applied = false;

CREATE TABLE IF NOT EXISTS paper_live_shadow_decisions (
    correlation_id uuid PRIMARY KEY,
    strategy_id uuid NOT NULL REFERENCES strategies(id),
    market_id text NOT NULL,
    condition_id text NOT NULL,
    asset_id text NOT NULL,
    outcome text NOT NULL,
    side text NOT NULL,
    limit_price numeric(18,8) NOT NULL,
    target_notional_usd numeric(28,8) NOT NULL,
    requested_size_shares numeric(28,8) NOT NULL,
    max_reserved_notional_usd numeric(28,8) NOT NULL,
    order_type text NOT NULL,
    post_only boolean NOT NULL,
    order_book_snapshot_json jsonb NOT NULL,
    quote_age_ms integer NULL,
    source text NOT NULL,
    quote_received_at_utc timestamptz NOT NULL,
    decision_created_at_utc timestamptz NOT NULL,
    market_start_utc timestamptz NULL,
    market_close_utc timestamptz NULL,
    submit_deadline_utc timestamptz NOT NULL,
    cancel_deadline_utc timestamptz NOT NULL,
    signal_id uuid NULL REFERENCES signals(id),
    paper_order_id uuid NULL REFERENCES paper_orders(id),
    live_order_id uuid NULL REFERENCES live_orders(id),
    status text NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_paper_live_shadow_decisions_strategy_time
ON paper_live_shadow_decisions(strategy_id, decision_created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_paper_live_shadow_decisions_status
ON paper_live_shadow_decisions(status, updated_at_utc DESC);

CREATE TABLE IF NOT EXISTS paper_live_shadow_discrepancies (
    id uuid PRIMARY KEY,
    correlation_id uuid NOT NULL,
    strategy_id uuid NOT NULL REFERENCES strategies(id),
    classification text NOT NULL,
    severity text NOT NULL,
    details text NOT NULL,
    raw_json jsonb NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_paper_live_shadow_discrepancies_strategy_time
ON paper_live_shadow_discrepancies(strategy_id, created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_paper_live_shadow_discrepancies_correlation
ON paper_live_shadow_discrepancies(correlation_id, created_at_utc DESC);

CREATE OR REPLACE FUNCTION public.promote_strategy_runs_for_live_order()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE public.strategy_market_paper_runs run
    SET retention_scope = 'LiveOrShadow'
    WHERE run.strategy_id = NEW.strategy_id
      AND run.retention_scope <> 'LiveOrShadow'
      AND (
          run.condition_id = NEW.condition_id
          OR (NEW.signal_id IS NOT NULL AND run.signal_id = NEW.signal_id)
          OR (NEW.paper_order_id IS NOT NULL AND run.paper_order_id = NEW.paper_order_id)
      );

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_promote_strategy_runs_for_live_order ON public.live_orders;
CREATE TRIGGER trg_promote_strategy_runs_for_live_order
AFTER INSERT OR UPDATE OF signal_id, paper_order_id, condition_id
ON public.live_orders
FOR EACH ROW
EXECUTE FUNCTION public.promote_strategy_runs_for_live_order();

CREATE OR REPLACE FUNCTION public.promote_strategy_runs_for_shadow_decision()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE public.strategy_market_paper_runs run
    SET retention_scope = 'LiveOrShadow'
    WHERE run.strategy_id = NEW.strategy_id
      AND run.retention_scope <> 'LiveOrShadow'
      AND (
          run.market_id = NEW.market_id
          OR run.condition_id = NEW.condition_id
          OR (NEW.signal_id IS NOT NULL AND run.signal_id = NEW.signal_id)
          OR (NEW.paper_order_id IS NOT NULL AND run.paper_order_id = NEW.paper_order_id)
      );

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_promote_strategy_runs_for_shadow_decision
ON public.paper_live_shadow_decisions;
CREATE TRIGGER trg_promote_strategy_runs_for_shadow_decision
AFTER INSERT OR UPDATE OF signal_id, paper_order_id, live_order_id, market_id, condition_id
ON public.paper_live_shadow_decisions
FOR EACH ROW
EXECUTE FUNCTION public.promote_strategy_runs_for_shadow_decision();

CREATE TABLE IF NOT EXISTS live_trading_events (
    id uuid PRIMARY KEY,
    action text NOT NULL,
    status text NOT NULL,
    details text NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_live_trading_events_created
ON live_trading_events(created_at_utc DESC);

CREATE TABLE IF NOT EXISTS risk_events (
    id uuid PRIMARY KEY,
    reason_code text NOT NULL,
    details text NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS market_data_status (
    component text PRIMARY KEY,
    connection_state text NOT NULL,
    endpoint text NOT NULL,
    subscribed_assets_count integer NOT NULL,
    last_message_utc timestamptz NULL,
    last_connected_utc timestamptz NULL,
    last_disconnected_utc timestamptz NULL,
    reconnect_count integer NOT NULL,
    stale boolean NOT NULL,
    last_error text NULL,
    updated_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS market_data_events (
    id uuid PRIMARY KEY,
    event_type text NOT NULL,
    asset_id text NULL,
    condition_id text NULL,
    message text NOT NULL,
    received_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_market_data_events_received
ON market_data_events(received_at_utc DESC);

CREATE TABLE IF NOT EXISTS pinned_market_assets (
    asset_id text PRIMARY KEY,
    note text NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS daily_reports (
    report_date date PRIMARY KEY,
    signals_observed integer NOT NULL,
    signals_accepted integer NOT NULL,
    signals_rejected integer NOT NULL,
    paper_orders_created integer NOT NULL,
    paper_fills integer NOT NULL,
    paper_expired_orders integer NOT NULL,
    paper_pnl numeric(28,8) NOT NULL,
    open_paper_exposure numeric(28,8) NOT NULL,
    top_rejection_reasons text NOT NULL,
    api_errors integer NOT NULL,
    generated_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS bot_settings (
    key text PRIMARY KEY,
    value text NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS service_command_audit (
    id uuid PRIMARY KEY,
    command text NOT NULL,
    source text NOT NULL,
    accepted boolean NOT NULL,
    message text NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS api_errors (
    id uuid PRIMARY KEY,
    component text NOT NULL,
    operation text NOT NULL,
    message text NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS polymarket_http_logs (
    id uuid PRIMARY KEY,
    component text NOT NULL,
    operation text NOT NULL,
    http_method text NOT NULL,
    request_url text NOT NULL,
    requested_at_utc timestamptz NOT NULL,
    response_at_utc timestamptz NULL,
    duration_ms bigint NOT NULL,
    attempt integer NOT NULL,
    status_code integer NULL,
    succeeded boolean NOT NULL,
    response_body text NOT NULL,
    error_message text NULL
);

CREATE INDEX IF NOT EXISTS ix_polymarket_http_logs_requested
ON polymarket_http_logs(requested_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_http_logs_operation
ON polymarket_http_logs(component, operation, requested_at_utc DESC);

CREATE TABLE IF NOT EXISTS polymarket_onchain_logs (
    id uuid PRIMARY KEY,
    contract_name text NOT NULL,
    contract_address text NOT NULL,
    exchange_version text NOT NULL,
    block_number bigint NOT NULL,
    block_hash text NOT NULL,
    transaction_hash text NOT NULL,
    transaction_index bigint NOT NULL,
    log_index bigint NOT NULL,
    topic0 text NOT NULL,
    topics_json jsonb NOT NULL,
    data text NOT NULL,
    removed boolean NOT NULL,
    observed_at_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_polymarket_onchain_logs_tx_log
ON polymarket_onchain_logs(transaction_hash, log_index);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_logs_contract_block
ON polymarket_onchain_logs(contract_address, block_number);

CREATE TABLE IF NOT EXISTS polymarket_onchain_fills (
    id uuid PRIMARY KEY,
    contract_name text NOT NULL,
    contract_address text NOT NULL,
    exchange_version text NOT NULL,
    block_number bigint NOT NULL,
    block_timestamp_utc timestamptz NOT NULL,
    transaction_hash text NOT NULL,
    log_index bigint NOT NULL,
    order_hash text NOT NULL,
    maker text NOT NULL,
    taker text NOT NULL,
    wallet text NOT NULL,
    side text NOT NULL,
    token_id text NOT NULL,
    maker_asset_id text NOT NULL,
    taker_asset_id text NOT NULL,
    maker_amount_raw text NOT NULL,
    taker_amount_raw text NOT NULL,
    maker_amount numeric(28,8) NOT NULL,
    taker_amount numeric(28,8) NOT NULL,
    price numeric(18,8) NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    notional_usd numeric(28,8) NOT NULL,
    fee_raw text NOT NULL,
    fee_amount numeric(28,8) NOT NULL,
    fee_asset_id text NOT NULL,
    builder text NULL,
    metadata text NULL,
    imported_at_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_polymarket_onchain_fills_tx_log
ON polymarket_onchain_fills(transaction_hash, log_index);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_fills_wallet_time
ON polymarket_onchain_fills(wallet, block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_fills_token_time
ON polymarket_onchain_fills(token_id, block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_fills_contract_block
ON polymarket_onchain_fills(contract_address, block_number);

CREATE TABLE IF NOT EXISTS polymarket_onchain_trade_captures (
    id uuid PRIMARY KEY,
    contract_name text NOT NULL,
    contract_address text NOT NULL,
    exchange_version text NOT NULL,
    block_number bigint NOT NULL,
    block_timestamp_utc timestamptz NOT NULL,
    block_hash text NOT NULL,
    transaction_hash text NOT NULL,
    transaction_index bigint NOT NULL,
    log_index bigint NOT NULL,
    order_hash text NOT NULL,
    maker text NOT NULL,
    taker text NOT NULL,
    wallet text NOT NULL,
    side text NOT NULL,
    token_id text NOT NULL,
    maker_asset_id text NOT NULL,
    taker_asset_id text NOT NULL,
    maker_amount_raw text NOT NULL,
    taker_amount_raw text NOT NULL,
    maker_amount numeric(28,8) NOT NULL,
    taker_amount numeric(28,8) NOT NULL,
    price numeric(28,12) NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    notional_usd numeric(28,8) NOT NULL,
    fee_raw text NOT NULL,
    fee_amount numeric(28,8) NOT NULL,
    fee_asset_id text NOT NULL,
    builder text NULL,
    metadata text NULL,
    raw_topics_json jsonb NOT NULL,
    raw_data text NOT NULL,
    removed boolean NOT NULL,
    observed_at_utc timestamptz NOT NULL,
    imported_at_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_polymarket_onchain_trade_captures_tx_log
ON polymarket_onchain_trade_captures(transaction_hash, log_index);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_trade_captures_contract_block
ON polymarket_onchain_trade_captures(contract_address, block_number, log_index);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_trade_captures_time
ON polymarket_onchain_trade_captures(block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_trade_captures_pending_order
ON polymarket_onchain_trade_captures(block_timestamp_utc, block_number, log_index)
WHERE NOT removed;

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_trade_captures_wallet_time
ON polymarket_onchain_trade_captures(wallet, block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_trade_captures_token_time
ON polymarket_onchain_trade_captures(token_id, block_timestamp_utc DESC);

CREATE TABLE IF NOT EXISTS polymarket_onchain_paper_signal_results (
    id uuid PRIMARY KEY,
    capture_id uuid NOT NULL,
    transaction_hash text NOT NULL,
    log_index bigint NOT NULL,
    participant_role text NOT NULL,
    copied_trader_wallet text NOT NULL,
    counterparty_wallet text NOT NULL,
    side text NOT NULL,
    token_id text NOT NULL,
    condition_id text NOT NULL,
    market_slug text NOT NULL,
    outcome text NOT NULL,
    local_category text NULL,
    polymarket_category text NULL,
    rating_found boolean NULL,
    leaderboard_rank integer NULL,
    leaderboard_pnl_usd numeric(28,8) NULL,
    leaderboard_volume_usd numeric(28,8) NULL,
    leaderboard_pnl_to_volume_pct numeric(18,8) NULL,
    signal_id uuid NULL,
    paper_order_id uuid NULL,
    status text NOT NULL,
    decision_code text NOT NULL,
    reason_details text NOT NULL,
    processed_at_utc timestamptz NOT NULL,
    UNIQUE (transaction_hash, log_index, participant_role)
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_paper_signal_results_wallet_time
ON polymarket_onchain_paper_signal_results(copied_trader_wallet, processed_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_paper_signal_results_status_time
ON polymarket_onchain_paper_signal_results(status, processed_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_paper_signal_results_signal
ON polymarket_onchain_paper_signal_results(signal_id);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_paper_signal_results_order
ON polymarket_onchain_paper_signal_results(paper_order_id);

CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_fills (
    source_fill_id uuid NOT NULL,
    contract_name text NOT NULL,
    contract_address text NOT NULL,
    exchange_version text NOT NULL,
    block_number bigint NOT NULL,
    block_timestamp_utc timestamptz NOT NULL,
    transaction_hash text NOT NULL,
    log_index bigint NOT NULL,
    order_hash text NOT NULL,
    role text NOT NULL,
    wallet text NOT NULL,
    counterparty text NOT NULL,
    side text NOT NULL,
    token_id text NOT NULL,
    price numeric(18,8) NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    notional_usd numeric(28,8) NOT NULL,
    fee_amount numeric(28,8) NOT NULL,
    fee_asset_id text NOT NULL,
    imported_at_utc timestamptz NOT NULL,
    PRIMARY KEY (transaction_hash, log_index, role)
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_fills_wallet_time
ON polymarket_onchain_wallet_fills(wallet, block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_fills_token_time
ON polymarket_onchain_wallet_fills(token_id, block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_fills_recent
ON polymarket_onchain_wallet_fills(block_timestamp_utc DESC, block_number DESC, log_index DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_fills_signal_candidate_backfill
ON polymarket_onchain_wallet_fills(block_timestamp_utc, block_number, log_index, role);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_fills_source_role
ON polymarket_onchain_wallet_fills(source_fill_id, role);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_fills_contract_block
ON polymarket_onchain_wallet_fills(contract_address, block_number);

CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_executions (
    contract_name text NOT NULL,
    contract_address text NOT NULL,
    exchange_version text NOT NULL,
    block_number bigint NOT NULL,
    block_timestamp_utc timestamptz NOT NULL,
    transaction_hash text NOT NULL,
    first_log_index bigint NOT NULL,
    last_log_index bigint NOT NULL,
    wallet text NOT NULL,
    side text NOT NULL,
    token_id text NOT NULL,
    fill_count integer NOT NULL,
    maker_fill_count integer NOT NULL,
    taker_fill_count integer NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    notional_usd numeric(28,8) NOT NULL,
    average_price numeric(18,8) NOT NULL,
    fees_usd numeric(28,8) NOT NULL,
    imported_at_utc timestamptz NOT NULL,
    PRIMARY KEY (contract_address, transaction_hash, wallet, side, token_id)
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_executions_wallet_time
ON polymarket_onchain_wallet_executions(wallet, block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_executions_token_time
ON polymarket_onchain_wallet_executions(token_id, block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_executions_recent
ON polymarket_onchain_wallet_executions(block_timestamp_utc DESC, block_number DESC, first_log_index DESC);

CREATE TABLE IF NOT EXISTS polymarket_onchain_token_metadata (
    token_id text PRIMARY KEY,
    condition_id text NOT NULL,
    market_id text NOT NULL,
    market_slug text NOT NULL,
    market_title text NOT NULL,
    outcome text NOT NULL,
    outcome_index integer NOT NULL,
    category text NULL,
    end_date_utc timestamptz NULL,
    active boolean NOT NULL,
    closed boolean NOT NULL,
    archived boolean NOT NULL,
    resolved boolean NOT NULL,
    winning_outcome text NULL,
    clob_token_ids_json jsonb NOT NULL,
    outcomes_json jsonb NOT NULL,
    lookup_succeeded boolean NOT NULL,
    lookup_error text NULL,
    raw_json jsonb NOT NULL,
    last_refreshed_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_token_metadata_condition
ON polymarket_onchain_token_metadata(condition_id);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_token_metadata_category
ON polymarket_onchain_token_metadata(category);

CREATE TABLE IF NOT EXISTS polymarket_onchain_token_metadata_refresh_queue (
    token_id text PRIMARY KEY,
    reason text NOT NULL,
    attempts integer NOT NULL DEFAULT 0,
    queued_at_utc timestamptz NOT NULL,
    last_attempted_at_utc timestamptz NULL,
    next_attempt_at_utc timestamptz NOT NULL,
    last_error text NULL
);

ALTER TABLE polymarket_onchain_token_metadata_refresh_queue ADD COLUMN IF NOT EXISTS attempts integer NOT NULL DEFAULT 0;
ALTER TABLE polymarket_onchain_token_metadata_refresh_queue ADD COLUMN IF NOT EXISTS last_attempted_at_utc timestamptz NULL;
ALTER TABLE polymarket_onchain_token_metadata_refresh_queue ADD COLUMN IF NOT EXISTS next_attempt_at_utc timestamptz NULL;
ALTER TABLE polymarket_onchain_token_metadata_refresh_queue ADD COLUMN IF NOT EXISTS last_error text NULL;
UPDATE polymarket_onchain_token_metadata_refresh_queue
SET next_attempt_at_utc = COALESCE(next_attempt_at_utc, queued_at_utc, now());
ALTER TABLE polymarket_onchain_token_metadata_refresh_queue ALTER COLUMN next_attempt_at_utc SET NOT NULL;

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_token_metadata_refresh_queue_next_attempt
ON polymarket_onchain_token_metadata_refresh_queue(next_attempt_at_utc, queued_at_utc);

INSERT INTO polymarket_onchain_token_metadata_refresh_queue (
    token_id, reason, attempts, queued_at_utc, next_attempt_at_utc
)
SELECT token_id, 'metadata_incomplete', 0, now(), now()
FROM polymarket_onchain_token_metadata
WHERE NOT lookup_succeeded
   OR NULLIF(category, '') IS NULL
ON CONFLICT (token_id) DO UPDATE SET
    reason = excluded.reason,
    queued_at_utc = LEAST(polymarket_onchain_token_metadata_refresh_queue.queued_at_utc, excluded.queued_at_utc),
    next_attempt_at_utc = LEAST(polymarket_onchain_token_metadata_refresh_queue.next_attempt_at_utc, excluded.next_attempt_at_utc);

CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_activity (
    wallet text PRIMARY KEY,
    executions integer NOT NULL,
    buy_executions integer NOT NULL,
    sell_executions integer NOT NULL,
    markets_traded integer NOT NULL,
    volume_usd numeric(28,8) NOT NULL,
    average_trade_usd numeric(28,8) NOT NULL,
    fees_usd numeric(28,8) NOT NULL,
    activity_score numeric(28,8) NOT NULL,
    first_trade_utc timestamptz NOT NULL,
    last_trade_utc timestamptz NOT NULL,
    refreshed_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_activity_score
ON polymarket_onchain_wallet_activity(activity_score DESC, volume_usd DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_activity_last_trade
ON polymarket_onchain_wallet_activity(last_trade_utc DESC);

CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_activity_refresh_queue (
    wallet text PRIMARY KEY,
    reason text NOT NULL,
    queued_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_activity_refresh_queue_queued
ON polymarket_onchain_wallet_activity_refresh_queue(queued_at_utc);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class cls
        JOIN pg_namespace ns ON ns.oid = cls.relnamespace
        WHERE ns.nspname = 'public'
          AND cls.relname = 'polymarket_onchain_wallet_positions'
          AND cls.relkind = 'v'
    ) THEN
        EXECUTE 'DROP VIEW public.polymarket_onchain_wallet_positions';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_positions (
    wallet text NOT NULL,
    token_id text NOT NULL,
    condition_id text NOT NULL,
    market_id text NOT NULL,
    market_slug text NOT NULL,
    market_title text NOT NULL,
    outcome text NOT NULL,
    category text NULL,
    lookup_succeeded boolean NOT NULL,
    market_resolved boolean NOT NULL,
    winning_outcome text NULL,
    executions integer NOT NULL,
    buy_executions integer NOT NULL,
    sell_executions integer NOT NULL,
    buy_shares numeric(28,8) NOT NULL,
    sell_shares numeric(28,8) NOT NULL,
    net_shares numeric(28,8) NOT NULL,
    buy_notional_usd numeric(28,8) NOT NULL,
    sell_notional_usd numeric(28,8) NOT NULL,
    net_cost_usd numeric(28,8) NOT NULL,
    absolute_net_cost_usd numeric(28,8) NOT NULL,
    fees_usd numeric(28,8) NOT NULL,
    average_buy_price numeric(18,8) NOT NULL,
    average_sell_price numeric(18,8) NOT NULL,
    volume_usd numeric(28,8) NOT NULL,
    resolved_pnl_usd numeric(28,8) NULL,
    position_status text NOT NULL,
    first_trade_utc timestamptz NOT NULL,
    last_trade_utc timestamptz NOT NULL,
    latest_execution_imported_at_utc timestamptz NOT NULL,
    metadata_refreshed_at_utc timestamptz NULL,
    refreshed_at_utc timestamptz NOT NULL,
    PRIMARY KEY (wallet, token_id)
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_positions_rank
ON polymarket_onchain_wallet_positions(absolute_net_cost_usd DESC, volume_usd DESC, last_trade_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_positions_token
ON polymarket_onchain_wallet_positions(token_id);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_positions_wallet
ON polymarket_onchain_wallet_positions(wallet, last_trade_utc DESC);

CREATE TABLE IF NOT EXISTS polymarket_onchain_position_refresh_queue (
    token_id text PRIMARY KEY,
    reason text NOT NULL,
    queued_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_position_refresh_queue_queued
ON polymarket_onchain_position_refresh_queue(queued_at_utc);

CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_performance (
    wallet text PRIMARY KEY,
    positions_count integer NOT NULL,
    open_positions integer NOT NULL,
    flat_positions integer NOT NULL,
    resolved_positions integer NOT NULL,
    profitable_resolved_positions integer NOT NULL,
    losing_resolved_positions integer NOT NULL,
    markets_traded integer NOT NULL,
    volume_usd numeric(28,8) NOT NULL,
    resolved_volume_usd numeric(28,8) NOT NULL,
    open_exposure_usd numeric(28,8) NOT NULL,
    resolved_cost_usd numeric(28,8) NOT NULL,
    resolved_pnl_usd numeric(28,8) NOT NULL,
    resolved_roi_pct numeric(18,8) NOT NULL,
    win_rate_pct numeric(18,8) NOT NULL,
    average_position_size_usd numeric(28,8) NOT NULL,
    score numeric(28,8) NOT NULL,
    sample_quality text NOT NULL,
    first_active_utc timestamptz NOT NULL,
    last_active_utc timestamptz NOT NULL,
    refreshed_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_performance_score
ON polymarket_onchain_wallet_performance(score DESC, resolved_pnl_usd DESC, volume_usd DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_performance_last_active
ON polymarket_onchain_wallet_performance(last_active_utc DESC);

CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_performance_refresh_queue (
    wallet text PRIMARY KEY,
    reason text NOT NULL,
    queued_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_performance_refresh_queue_queued
ON polymarket_onchain_wallet_performance_refresh_queue(queued_at_utc);

CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_category_performance (
    wallet text NOT NULL,
    category text NOT NULL,
    positions_count integer NOT NULL,
    open_positions integer NOT NULL,
    flat_positions integer NOT NULL,
    resolved_positions integer NOT NULL,
    profitable_resolved_positions integer NOT NULL,
    losing_resolved_positions integer NOT NULL,
    markets_traded integer NOT NULL,
    volume_usd numeric(28,8) NOT NULL,
    resolved_volume_usd numeric(28,8) NOT NULL,
    open_exposure_usd numeric(28,8) NOT NULL,
    resolved_cost_usd numeric(28,8) NOT NULL,
    resolved_pnl_usd numeric(28,8) NOT NULL,
    resolved_roi_pct numeric(18,8) NOT NULL,
    win_rate_pct numeric(18,8) NOT NULL,
    average_position_size_usd numeric(28,8) NOT NULL,
    score numeric(28,8) NOT NULL,
    sample_quality text NOT NULL,
    first_active_utc timestamptz NOT NULL,
    last_active_utc timestamptz NOT NULL,
    refreshed_at_utc timestamptz NOT NULL,
    PRIMARY KEY (wallet, category)
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_category_performance_category_score
ON polymarket_onchain_wallet_category_performance(category, score DESC, resolved_pnl_usd DESC, volume_usd DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_category_performance_wallet
ON polymarket_onchain_wallet_category_performance(wallet, category);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_category_performance_last_active
ON polymarket_onchain_wallet_category_performance(category, last_active_utc DESC);

CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_category_performance_refresh_queue (
    wallet text NOT NULL,
    category text NOT NULL,
    reason text NOT NULL,
    queued_at_utc timestamptz NOT NULL,
    PRIMARY KEY (wallet, category)
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_category_performance_refresh_queue_queued
ON polymarket_onchain_wallet_category_performance_refresh_queue(queued_at_utc);

CREATE TABLE IF NOT EXISTS polymarket_onchain_signal_candidate_refresh_queue (
    source_fill_id uuid NOT NULL,
    participant_role text NOT NULL,
    block_timestamp_utc timestamptz NOT NULL,
    block_number bigint NOT NULL,
    log_index bigint NOT NULL,
    queued_at_utc timestamptz NOT NULL,
    next_attempt_at_utc timestamptz NOT NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    last_error text NULL,
    PRIMARY KEY (source_fill_id, participant_role)
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_signal_candidate_refresh_queue_next_attempt
ON polymarket_onchain_signal_candidate_refresh_queue(next_attempt_at_utc, block_timestamp_utc, block_number, log_index, participant_role);

CREATE TABLE IF NOT EXISTS polymarket_onchain_signal_candidate_backfill_cursors (
    cursor_name text PRIMARY KEY,
    last_block_timestamp_utc timestamptz NULL,
    last_block_number bigint NULL,
    last_log_index bigint NULL,
    last_participant_role text NULL,
    completed boolean NOT NULL DEFAULT false,
    updated_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS polymarket_onchain_signal_candidates (
    id uuid PRIMARY KEY,
    source_fill_id uuid NOT NULL,
    contract_name text NOT NULL,
    contract_address text NOT NULL,
    exchange_version text NOT NULL,
    block_number bigint NOT NULL,
    block_timestamp_utc timestamptz NOT NULL,
    transaction_hash text NOT NULL,
    log_index bigint NOT NULL,
    order_hash text NOT NULL,
    participant_role text NOT NULL,
    wallet text NOT NULL,
    counterparty text NOT NULL,
    side text NOT NULL,
    token_id text NOT NULL,
    condition_id text NOT NULL,
    market_id text NOT NULL,
    market_slug text NOT NULL,
    market_title text NOT NULL,
    outcome text NOT NULL,
    category text NULL,
    lookup_succeeded boolean NOT NULL,
    market_active boolean NOT NULL,
    market_closed boolean NOT NULL,
    market_archived boolean NOT NULL,
    market_resolved boolean NOT NULL,
    winning_outcome text NULL,
    price numeric(18,8) NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    notional_usd numeric(28,8) NOT NULL,
    fee_amount numeric(28,8) NOT NULL,
    fee_asset_id text NOT NULL,
    leader_positions_count integer NULL,
    leader_resolved_positions integer NULL,
    leader_markets_traded integer NULL,
    leader_volume_usd numeric(28,8) NULL,
    leader_resolved_pnl_usd numeric(28,8) NULL,
    leader_resolved_roi_pct numeric(18,8) NULL,
    leader_win_rate_pct numeric(18,8) NULL,
    leader_category_score numeric(28,8) NULL,
    leader_sample_quality text NULL,
    leader_performance_refreshed_at_utc timestamptz NULL,
    decision_status text NOT NULL,
    decision_code text NOT NULL,
    candidate_score numeric(28,8) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    UNIQUE (source_fill_id, participant_role)
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_signal_candidates_updated
ON polymarket_onchain_signal_candidates(updated_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_signal_candidates_status_time
ON polymarket_onchain_signal_candidates(decision_status, block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_signal_candidates_wallet_category
ON polymarket_onchain_signal_candidates(wallet, category, block_timestamp_utc DESC);

CREATE TABLE IF NOT EXISTS polymarket_onchain_signal_candidate_reasons (
    id uuid PRIMARY KEY,
    candidate_id uuid NOT NULL REFERENCES polymarket_onchain_signal_candidates(id) ON DELETE CASCADE,
    reason_code text NOT NULL,
    reason_details text NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_signal_candidate_reasons_candidate
ON polymarket_onchain_signal_candidate_reasons(candidate_id, created_at_utc);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_signal_candidate_reasons_reason
ON polymarket_onchain_signal_candidate_reasons(reason_code, created_at_utc DESC);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class cls
        JOIN pg_namespace ns ON ns.oid = cls.relnamespace
        WHERE ns.nspname = 'public'
          AND cls.relname = 'polymarket_onchain_trade_details'
          AND cls.relkind = 'v'
    ) THEN
        EXECUTE 'DROP VIEW public.polymarket_onchain_trade_details';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS polymarket_onchain_trade_details (
    contract_name text NOT NULL,
    contract_address text NOT NULL,
    exchange_version text NOT NULL,
    block_number bigint NOT NULL,
    block_timestamp_utc timestamptz NOT NULL,
    transaction_hash text NOT NULL,
    log_index bigint NOT NULL,
    order_hash text NOT NULL,
    maker text NOT NULL,
    taker text NOT NULL,
    maker_side text NOT NULL,
    taker_side text NOT NULL,
    token_id text NOT NULL,
    maker_asset_id text NOT NULL,
    taker_asset_id text NOT NULL,
    maker_amount_raw text NOT NULL,
    taker_amount_raw text NOT NULL,
    maker_amount numeric(28,8) NOT NULL,
    taker_amount numeric(28,8) NOT NULL,
    price numeric(18,8) NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    notional_usd numeric(28,8) NOT NULL,
    fee_amount numeric(28,8) NOT NULL,
    fee_asset_id text NOT NULL,
    builder text NULL,
    order_metadata text NULL,
    condition_id text NOT NULL,
    market_id text NOT NULL,
    market_slug text NOT NULL,
    market_title text NOT NULL,
    outcome text NOT NULL,
    category text NULL,
    lookup_succeeded boolean NOT NULL,
    market_active boolean NOT NULL,
    market_closed boolean NOT NULL,
    market_archived boolean NOT NULL,
    market_resolved boolean NOT NULL,
    winning_outcome text NULL,
    imported_at_utc timestamptz NOT NULL,
    refreshed_at_utc timestamptz NOT NULL,
    PRIMARY KEY (transaction_hash, log_index)
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_trade_details_recent
ON polymarket_onchain_trade_details(block_timestamp_utc DESC, block_number DESC, log_index DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_trade_details_contract_block
ON polymarket_onchain_trade_details(contract_address, block_number);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_trade_details_maker_time
ON polymarket_onchain_trade_details(maker, block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_trade_details_taker_time
ON polymarket_onchain_trade_details(taker, block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_trade_details_token_time
ON polymarket_onchain_trade_details(token_id, block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_trade_details_market_time
ON polymarket_onchain_trade_details(market_slug, block_timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_trade_details_category_time
ON polymarket_onchain_trade_details(category, block_timestamp_utc DESC);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class cls
        JOIN pg_namespace ns ON ns.oid = cls.relnamespace
        WHERE ns.nspname = 'public'
          AND cls.relname = 'polymarket_onchain_participant_details'
          AND cls.relkind = 'v'
    ) THEN
        EXECUTE 'DROP VIEW public.polymarket_onchain_participant_details';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS polymarket_onchain_participant_details (
    wallet text PRIMARY KEY,
    executions integer NOT NULL,
    buy_executions integer NOT NULL,
    sell_executions integer NOT NULL,
    markets_traded integer NOT NULL,
    volume_usd numeric(28,8) NOT NULL,
    average_trade_usd numeric(28,8) NOT NULL,
    fees_usd numeric(28,8) NOT NULL,
    activity_score numeric(28,8) NOT NULL,
    positions_count integer NOT NULL,
    open_positions integer NOT NULL,
    flat_positions integer NOT NULL,
    resolved_positions integer NOT NULL,
    profitable_resolved_positions integer NOT NULL,
    losing_resolved_positions integer NOT NULL,
    open_exposure_usd numeric(28,8) NOT NULL,
    resolved_cost_usd numeric(28,8) NOT NULL,
    resolved_pnl_usd numeric(28,8) NOT NULL,
    resolved_roi_pct numeric(18,8) NOT NULL,
    win_rate_pct numeric(18,8) NOT NULL,
    average_position_size_usd numeric(28,8) NOT NULL,
    score numeric(28,8) NOT NULL,
    sample_quality text NOT NULL,
    first_trade_utc timestamptz NOT NULL,
    last_trade_utc timestamptz NOT NULL,
    activity_refreshed_at_utc timestamptz NOT NULL,
    performance_refreshed_at_utc timestamptz NULL,
    refreshed_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_participant_details_score
ON polymarket_onchain_participant_details(score DESC, volume_usd DESC, last_trade_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_participant_details_last_trade
ON polymarket_onchain_participant_details(last_trade_utc DESC);

CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_participant_details_volume
ON polymarket_onchain_participant_details(volume_usd DESC, executions DESC);

CREATE TABLE IF NOT EXISTS polymarket_onchain_ingest_cursors (
    contract_address text PRIMARY KEY,
    contract_name text NOT NULL,
    exchange_version text NOT NULL,
    from_block bigint NOT NULL,
    to_block bigint NOT NULL,
    logs_fetched integer NOT NULL,
    fills_stored integer NOT NULL,
    started_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS polymarket_onchain_trade_capture_cursors (
    contract_address text PRIMARY KEY,
    contract_name text NOT NULL,
    exchange_version text NOT NULL,
    next_block bigint NOT NULL,
    last_scanned_block bigint NOT NULL,
    last_target_block bigint NOT NULL,
    logs_fetched integer NOT NULL,
    captures_stored integer NOT NULL,
    started_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS scanner_status (
    scanner_name text PRIMARY KEY,
    status text NOT NULL,
    last_successful_scan_utc timestamptz NULL,
    last_error_utc timestamptz NULL,
    last_error_message text NULL,
    trades_fetched integer NOT NULL,
    new_trades_stored integer NOT NULL,
    positions_fetched integer NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS service_heartbeats (
    service_name text PRIMARY KEY,
    status text NOT NULL,
    started_at_utc timestamptz NOT NULL,
    last_heartbeat_utc timestamptz NOT NULL,
    version text NOT NULL,
    mode text NOT NULL,
    current_loop text NOT NULL,
    last_error text NULL
);

CREATE INDEX IF NOT EXISTS ix_strategy_market_paper_runs_signal
ON strategy_market_paper_runs(signal_id);

CREATE INDEX IF NOT EXISTS ix_signal_rejections_signal
ON signal_rejections(signal_id);

CREATE INDEX IF NOT EXISTS ix_paper_live_shadow_decisions_paper_order
ON paper_live_shadow_decisions(paper_order_id);

CREATE INDEX IF NOT EXISTS ix_paper_live_shadow_decisions_live_order
ON paper_live_shadow_decisions(live_order_id);

CREATE INDEX IF NOT EXISTS ix_paper_live_shadow_decisions_signal
ON paper_live_shadow_decisions(signal_id);

WITH first_live_events AS (
    SELECT
        event_row.strategy_id,
        min(event_row.event_at_utc) AS first_live_event_at_utc
    FROM (
        SELECT live_order.strategy_id, live_order.created_at_utc AS event_at_utc
        FROM live_orders live_order
        UNION ALL
        SELECT decision.strategy_id, decision.decision_created_at_utc AS event_at_utc
        FROM paper_live_shadow_decisions decision
    ) event_row
    GROUP BY event_row.strategy_id
)
UPDATE strategies strategy
SET live_enabled_at_utc = COALESCE(first_live_events.first_live_event_at_utc, clock_timestamp())
FROM first_live_events
WHERE strategy.id = first_live_events.strategy_id
  AND strategy.live_stakes
  AND strategy.live_enabled_at_utc IS NULL;

UPDATE strategies
SET live_enabled_at_utc = clock_timestamp()
WHERE live_stakes
  AND live_enabled_at_utc IS NULL;

UPDATE strategies
SET live_enabled_at_utc = NULL
WHERE NOT live_stakes
  AND live_enabled_at_utc IS NOT NULL;

CREATE TABLE IF NOT EXISTS schema_data_migrations (
    migration_key text PRIMARY KEY,
    applied_at_utc timestamptz NOT NULL,
    details text NOT NULL
);

DO $$
DECLARE
    migration_key_value text := '20260602_clear_auto_live_pause_by_default';
    cleared_strategy_count integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        UPDATE strategies
        SET auto_live_paused = false,
            auto_live_paused_at_utc = NULL,
            auto_live_pause_window_start_utc = NULL,
            updated_at_utc = clock_timestamp()
        WHERE auto_live_paused;
        GET DIAGNOSTICS cleared_strategy_count = ROW_COUNT;

        INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
        VALUES (
            migration_key_value,
            clock_timestamp(),
            'cleared_strategies=' || cleared_strategy_count::text
        );
    END IF;
END $$;

DO $$
DECLARE
    migration_key_value text := '20260605_backfill_auto_live_pause_anchors';
    updated_strategy_count integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        UPDATE strategies
        SET auto_live_paused_at_utc = COALESCE(auto_live_paused_at_utc, updated_at_utc),
            auto_live_pause_window_start_utc = COALESCE(auto_live_pause_window_start_utc, updated_at_utc - interval '12 hours')
        WHERE auto_live_paused
          AND (
              auto_live_paused_at_utc IS NULL
              OR auto_live_pause_window_start_utc IS NULL
          );
        GET DIAGNOSTICS updated_strategy_count = ROW_COUNT;

        INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
        VALUES (
            migration_key_value,
            clock_timestamp(),
            'updated_strategies=' || updated_strategy_count::text
        );
    END IF;
END $$;

DO $$
DECLARE
    migration_key_value text := '20260623_restore_eth_down_previous_result_premarket_enabled';
    restored_strategy_count integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        UPDATE strategies strategy
        SET enabled = true,
            updated_at_utc = clock_timestamp()
        WHERE strategy.code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'
          AND strategy.enabled IS DISTINCT FROM true;
        GET DIAGNOSTICS restored_strategy_count = ROW_COUNT;

        INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
        VALUES (
            migration_key_value,
            clock_timestamp(),
            'restored_enabled_strategies=' || restored_strategy_count::text
        );
    END IF;
END $$;

DO $$
DECLARE
    migration_key_value text := '20260522_rescale_updown_bps_history_reset';
    target_strategy_count integer := 0;
    deleted_shadow_discrepancies integer := 0;
    deleted_shadow_decisions integer := 0;
    deleted_live_orders integer := 0;
    deleted_strategy_runs integer := 0;
    deleted_paper_fills integer := 0;
    deleted_paper_orders integer := 0;
    deleted_signal_rejections integer := 0;
    deleted_signals integer := 0;
    deleted_paper_positions integer := 0;
    deleted_paper_position_settlements integer := 0;
    active_live_orders integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        DROP TABLE IF EXISTS tmp_bps_history_reset_strategies;
        DROP TABLE IF EXISTS tmp_bps_history_reset_paper_orders;
        DROP TABLE IF EXISTS tmp_bps_history_reset_live_orders;
        DROP TABLE IF EXISTS tmp_bps_history_reset_signals;

        CREATE TEMP TABLE tmp_bps_history_reset_strategies ON COMMIT DROP AS
        SELECT strategy.id
        FROM strategies strategy
        WHERE strategy.code LIKE 'btc_up_down_5m_binance_bps_%'
           OR strategy.code LIKE 'sol_up_down_5m_binance_bps_%';

        SELECT count(*)::integer
        INTO target_strategy_count
        FROM tmp_bps_history_reset_strategies;

        UPDATE strategies strategy
        SET live_stakes = false,
            auto_live_paused = false,
            auto_live_paused_at_utc = NULL,
            auto_live_pause_window_start_utc = NULL,
            live_enabled_at_utc = NULL,
            updated_at_utc = clock_timestamp()
        WHERE strategy.id IN (
            SELECT target.id
            FROM tmp_bps_history_reset_strategies target
        )
          AND (strategy.live_stakes OR strategy.auto_live_paused);

        CREATE TEMP TABLE tmp_bps_history_reset_paper_orders ON COMMIT DROP AS
        SELECT paper_order.id
        FROM paper_orders paper_order
        WHERE paper_order.strategy_id IN (
            SELECT target.id
            FROM tmp_bps_history_reset_strategies target
        );

        CREATE TEMP TABLE tmp_bps_history_reset_live_orders ON COMMIT DROP AS
        SELECT live_order.id
        FROM live_orders live_order
        WHERE live_order.strategy_id IN (
            SELECT target.id
            FROM tmp_bps_history_reset_strategies target
        );

        CREATE TEMP TABLE tmp_bps_history_reset_signals ON COMMIT DROP AS
        SELECT signal.id
        FROM signals signal
        WHERE signal.trader_wallet LIKE 'strategy:btc_up_down_5m_binance_bps_%'
           OR signal.trader_wallet LIKE 'strategy:sol_up_down_5m_binance_bps_%';

        SELECT count(*)::integer
        INTO active_live_orders
        FROM live_orders live_order
        WHERE live_order.id IN (
            SELECT target.id
            FROM tmp_bps_history_reset_live_orders target
        )
          AND live_order.status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested');

        IF active_live_orders = 0 THEN
            DELETE FROM paper_live_shadow_discrepancies discrepancy
            WHERE discrepancy.strategy_id IN (
                SELECT target.id
                FROM tmp_bps_history_reset_strategies target
            );
            GET DIAGNOSTICS deleted_shadow_discrepancies = ROW_COUNT;

            DELETE FROM paper_live_shadow_decisions decision
            WHERE decision.strategy_id IN (
                    SELECT target.id
                    FROM tmp_bps_history_reset_strategies target
                )
               OR decision.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_bps_history_reset_paper_orders target
                )
               OR decision.live_order_id IN (
                    SELECT target.id
                    FROM tmp_bps_history_reset_live_orders target
                )
               OR decision.signal_id IN (
                    SELECT target.id
                    FROM tmp_bps_history_reset_signals target
                );
            GET DIAGNOSTICS deleted_shadow_decisions = ROW_COUNT;

            DELETE FROM live_orders live_order
            WHERE live_order.id IN (
                SELECT target.id
                FROM tmp_bps_history_reset_live_orders target
            );
            GET DIAGNOSTICS deleted_live_orders = ROW_COUNT;

            DELETE FROM strategy_market_paper_runs run
            WHERE run.strategy_id IN (
                    SELECT target.id
                    FROM tmp_bps_history_reset_strategies target
                )
               OR run.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_bps_history_reset_paper_orders target
                )
               OR run.signal_id IN (
                    SELECT target.id
                    FROM tmp_bps_history_reset_signals target
                );
            GET DIAGNOSTICS deleted_strategy_runs = ROW_COUNT;

            DELETE FROM paper_fills fill
            WHERE fill.paper_order_id IN (
                SELECT target.id
                FROM tmp_bps_history_reset_paper_orders target
            );
            GET DIAGNOSTICS deleted_paper_fills = ROW_COUNT;

            DELETE FROM paper_orders paper_order
            WHERE paper_order.id IN (
                SELECT target.id
                FROM tmp_bps_history_reset_paper_orders target
            );
            GET DIAGNOSTICS deleted_paper_orders = ROW_COUNT;

            DELETE FROM signal_rejections rejection
            WHERE rejection.signal_id IN (
                SELECT target.id
                FROM tmp_bps_history_reset_signals target
            );
            GET DIAGNOSTICS deleted_signal_rejections = ROW_COUNT;

            DELETE FROM signals signal
            WHERE signal.id IN (
                SELECT target.id
                FROM tmp_bps_history_reset_signals target
            );
            GET DIAGNOSTICS deleted_signals = ROW_COUNT;

            DELETE FROM paper_positions paper_position
            WHERE paper_position.copied_trader_wallet LIKE 'strategy:btc_up_down_5m_binance_bps_%'
               OR paper_position.copied_trader_wallet LIKE 'strategy:sol_up_down_5m_binance_bps_%';
            GET DIAGNOSTICS deleted_paper_positions = ROW_COUNT;

            DELETE FROM paper_position_settlements settlement
            WHERE settlement.copied_trader_wallet LIKE 'strategy:btc_up_down_5m_binance_bps_%'
               OR settlement.copied_trader_wallet LIKE 'strategy:sol_up_down_5m_binance_bps_%';
            GET DIAGNOSTICS deleted_paper_position_settlements = ROW_COUNT;

            INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
            VALUES (
                migration_key_value,
                clock_timestamp(),
                'target_strategies=' || target_strategy_count::text ||
                ';paper_orders=' || deleted_paper_orders::text ||
                ';paper_fills=' || deleted_paper_fills::text ||
                ';strategy_runs=' || deleted_strategy_runs::text ||
                ';live_orders=' || deleted_live_orders::text ||
                ';shadow_decisions=' || deleted_shadow_decisions::text ||
                ';shadow_discrepancies=' || deleted_shadow_discrepancies::text ||
                ';signals=' || deleted_signals::text ||
                ';signal_rejections=' || deleted_signal_rejections::text ||
                ';paper_positions=' || deleted_paper_positions::text ||
                ';paper_position_settlements=' || deleted_paper_position_settlements::text
            );
        END IF;
    END IF;
END $$;

DO $$
DECLARE
    migration_key_value text := '20260522_rescale_middle_bps_history_reset';
    target_strategy_count integer := 0;
    deleted_shadow_discrepancies integer := 0;
    deleted_shadow_decisions integer := 0;
    deleted_live_orders integer := 0;
    deleted_strategy_runs integer := 0;
    deleted_paper_fills integer := 0;
    deleted_paper_orders integer := 0;
    deleted_signal_rejections integer := 0;
    deleted_signals integer := 0;
    deleted_paper_positions integer := 0;
    deleted_paper_position_settlements integer := 0;
    active_live_orders integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        DROP TABLE IF EXISTS tmp_middle_bps_history_reset_strategies;
        DROP TABLE IF EXISTS tmp_middle_bps_history_reset_paper_orders;
        DROP TABLE IF EXISTS tmp_middle_bps_history_reset_live_orders;
        DROP TABLE IF EXISTS tmp_middle_bps_history_reset_signals;

        CREATE TEMP TABLE tmp_middle_bps_history_reset_strategies ON COMMIT DROP AS
        SELECT strategy.id
        FROM strategies strategy
        WHERE strategy.code LIKE 'btc_up_down_5m_middle_%_bps_%';

        SELECT count(*)::integer
        INTO target_strategy_count
        FROM tmp_middle_bps_history_reset_strategies;

        UPDATE strategies strategy
        SET live_stakes = false,
            auto_live_paused = false,
            auto_live_paused_at_utc = NULL,
            auto_live_pause_window_start_utc = NULL,
            live_enabled_at_utc = NULL,
            updated_at_utc = clock_timestamp()
        WHERE strategy.id IN (
            SELECT target.id
            FROM tmp_middle_bps_history_reset_strategies target
        )
          AND (strategy.live_stakes OR strategy.auto_live_paused);

        CREATE TEMP TABLE tmp_middle_bps_history_reset_paper_orders ON COMMIT DROP AS
        SELECT paper_order.id
        FROM paper_orders paper_order
        WHERE paper_order.strategy_id IN (
            SELECT target.id
            FROM tmp_middle_bps_history_reset_strategies target
        );

        CREATE TEMP TABLE tmp_middle_bps_history_reset_live_orders ON COMMIT DROP AS
        SELECT live_order.id
        FROM live_orders live_order
        WHERE live_order.strategy_id IN (
            SELECT target.id
            FROM tmp_middle_bps_history_reset_strategies target
        );

        CREATE TEMP TABLE tmp_middle_bps_history_reset_signals ON COMMIT DROP AS
        SELECT signal.id
        FROM signals signal
        WHERE signal.trader_wallet LIKE 'strategy:btc_up_down_5m_middle_%_bps_%';

        SELECT count(*)::integer
        INTO active_live_orders
        FROM live_orders live_order
        WHERE live_order.id IN (
            SELECT target.id
            FROM tmp_middle_bps_history_reset_live_orders target
        )
          AND live_order.status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested');

        IF active_live_orders = 0 THEN
            DELETE FROM paper_live_shadow_discrepancies discrepancy
            WHERE discrepancy.strategy_id IN (
                SELECT target.id
                FROM tmp_middle_bps_history_reset_strategies target
            );
            GET DIAGNOSTICS deleted_shadow_discrepancies = ROW_COUNT;

            DELETE FROM paper_live_shadow_decisions decision
            WHERE decision.strategy_id IN (
                    SELECT target.id
                    FROM tmp_middle_bps_history_reset_strategies target
                )
               OR decision.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_middle_bps_history_reset_paper_orders target
                )
               OR decision.live_order_id IN (
                    SELECT target.id
                    FROM tmp_middle_bps_history_reset_live_orders target
                )
               OR decision.signal_id IN (
                    SELECT target.id
                    FROM tmp_middle_bps_history_reset_signals target
                );
            GET DIAGNOSTICS deleted_shadow_decisions = ROW_COUNT;

            DELETE FROM live_orders live_order
            WHERE live_order.id IN (
                SELECT target.id
                FROM tmp_middle_bps_history_reset_live_orders target
            );
            GET DIAGNOSTICS deleted_live_orders = ROW_COUNT;

            DELETE FROM strategy_market_paper_runs run
            WHERE run.strategy_id IN (
                    SELECT target.id
                    FROM tmp_middle_bps_history_reset_strategies target
                )
               OR run.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_middle_bps_history_reset_paper_orders target
                )
               OR run.signal_id IN (
                    SELECT target.id
                    FROM tmp_middle_bps_history_reset_signals target
                );
            GET DIAGNOSTICS deleted_strategy_runs = ROW_COUNT;

            DELETE FROM paper_fills fill
            WHERE fill.paper_order_id IN (
                SELECT target.id
                FROM tmp_middle_bps_history_reset_paper_orders target
            );
            GET DIAGNOSTICS deleted_paper_fills = ROW_COUNT;

            DELETE FROM paper_orders paper_order
            WHERE paper_order.id IN (
                SELECT target.id
                FROM tmp_middle_bps_history_reset_paper_orders target
            );
            GET DIAGNOSTICS deleted_paper_orders = ROW_COUNT;

            DELETE FROM signal_rejections rejection
            WHERE rejection.signal_id IN (
                SELECT target.id
                FROM tmp_middle_bps_history_reset_signals target
            );
            GET DIAGNOSTICS deleted_signal_rejections = ROW_COUNT;

            DELETE FROM signals signal
            WHERE signal.id IN (
                SELECT target.id
                FROM tmp_middle_bps_history_reset_signals target
            );
            GET DIAGNOSTICS deleted_signals = ROW_COUNT;

            DELETE FROM paper_positions paper_position
            WHERE paper_position.copied_trader_wallet LIKE 'strategy:btc_up_down_5m_middle_%_bps_%';
            GET DIAGNOSTICS deleted_paper_positions = ROW_COUNT;

            DELETE FROM paper_position_settlements settlement
            WHERE settlement.copied_trader_wallet LIKE 'strategy:btc_up_down_5m_middle_%_bps_%';
            GET DIAGNOSTICS deleted_paper_position_settlements = ROW_COUNT;

            INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
            VALUES (
                migration_key_value,
                clock_timestamp(),
                'target_strategies=' || target_strategy_count::text ||
                ';paper_orders=' || deleted_paper_orders::text ||
                ';paper_fills=' || deleted_paper_fills::text ||
                ';strategy_runs=' || deleted_strategy_runs::text ||
                ';live_orders=' || deleted_live_orders::text ||
                ';shadow_decisions=' || deleted_shadow_decisions::text ||
                ';shadow_discrepancies=' || deleted_shadow_discrepancies::text ||
                ';signals=' || deleted_signals::text ||
                ';signal_rejections=' || deleted_signal_rejections::text ||
                ';paper_positions=' || deleted_paper_positions::text ||
                ';paper_position_settlements=' || deleted_paper_position_settlements::text
            );
        END IF;
    END IF;
END $$;

DO $$
DECLARE
    migration_key_value text := '20260707_remove_eth_binance_bps_strategies';
    target_strategy_count integer := 0;
    deleted_shadow_discrepancies integer := 0;
    deleted_shadow_decisions integer := 0;
    deleted_live_orders integer := 0;
    deleted_strategy_runs integer := 0;
    deleted_paper_fills integer := 0;
    deleted_paper_orders integer := 0;
    deleted_signal_rejections integer := 0;
    deleted_signals integer := 0;
    deleted_paper_positions integer := 0;
    deleted_paper_position_settlements integer := 0;
    deleted_dashboard_snapshots integer := 0;
    deleted_dashboard_recent_snapshots integer := 0;
    deleted_strategies integer := 0;
    active_live_orders integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        DROP TABLE IF EXISTS tmp_eth_binance_bps_strategy_targets;
        DROP TABLE IF EXISTS tmp_eth_binance_bps_paper_orders;
        DROP TABLE IF EXISTS tmp_eth_binance_bps_live_orders;
        DROP TABLE IF EXISTS tmp_eth_binance_bps_signals;

        CREATE TEMP TABLE tmp_eth_binance_bps_strategy_targets ON COMMIT DROP AS
        SELECT strategy.id, strategy.code
        FROM strategies strategy
        WHERE strategy.code LIKE 'eth_up_down_5m_binance_bps_%'
           OR strategy.name ILIKE 'ETH Up or Down 5m Binance % bps%';

        SELECT count(*)::integer
        INTO target_strategy_count
        FROM tmp_eth_binance_bps_strategy_targets;

        CREATE TEMP TABLE tmp_eth_binance_bps_paper_orders ON COMMIT DROP AS
        SELECT paper_order.id
        FROM paper_orders paper_order
        WHERE paper_order.strategy_id IN (
                SELECT target.id
                FROM tmp_eth_binance_bps_strategy_targets target
            )
           OR paper_order.copied_trader_wallet IN (
                SELECT 'strategy:' || target.code
                FROM tmp_eth_binance_bps_strategy_targets target
            );

        CREATE TEMP TABLE tmp_eth_binance_bps_live_orders ON COMMIT DROP AS
        SELECT live_order.id
        FROM live_orders live_order
        WHERE live_order.strategy_id IN (
            SELECT target.id
            FROM tmp_eth_binance_bps_strategy_targets target
        );

        CREATE TEMP TABLE tmp_eth_binance_bps_signals ON COMMIT DROP AS
        SELECT signal.id
        FROM signals signal
        WHERE signal.trader_wallet IN (
            SELECT 'strategy:' || target.code
            FROM tmp_eth_binance_bps_strategy_targets target
        );

        SELECT count(*)::integer
        INTO active_live_orders
        FROM live_orders live_order
        WHERE live_order.id IN (
                SELECT target.id
                FROM tmp_eth_binance_bps_live_orders target
            )
          AND live_order.status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested');

        IF active_live_orders = 0 THEN
            DELETE FROM paper_live_shadow_discrepancies discrepancy
            WHERE discrepancy.strategy_id IN (
                SELECT target.id
                FROM tmp_eth_binance_bps_strategy_targets target
            );
            GET DIAGNOSTICS deleted_shadow_discrepancies = ROW_COUNT;

            DELETE FROM paper_live_shadow_decisions decision
            WHERE decision.strategy_id IN (
                    SELECT target.id
                    FROM tmp_eth_binance_bps_strategy_targets target
                )
               OR decision.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_eth_binance_bps_paper_orders target
                )
               OR decision.live_order_id IN (
                    SELECT target.id
                    FROM tmp_eth_binance_bps_live_orders target
                )
               OR decision.signal_id IN (
                    SELECT target.id
                    FROM tmp_eth_binance_bps_signals target
                );
            GET DIAGNOSTICS deleted_shadow_decisions = ROW_COUNT;

            DELETE FROM live_orders live_order
            WHERE live_order.id IN (
                SELECT target.id
                FROM tmp_eth_binance_bps_live_orders target
            );
            GET DIAGNOSTICS deleted_live_orders = ROW_COUNT;

            DELETE FROM strategy_market_paper_runs run
            WHERE run.strategy_id IN (
                    SELECT target.id
                    FROM tmp_eth_binance_bps_strategy_targets target
                )
               OR run.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_eth_binance_bps_paper_orders target
                )
               OR run.signal_id IN (
                    SELECT target.id
                    FROM tmp_eth_binance_bps_signals target
                );
            GET DIAGNOSTICS deleted_strategy_runs = ROW_COUNT;

            DELETE FROM paper_fills fill
            WHERE fill.paper_order_id IN (
                SELECT target.id
                FROM tmp_eth_binance_bps_paper_orders target
            );
            GET DIAGNOSTICS deleted_paper_fills = ROW_COUNT;

            DELETE FROM paper_orders paper_order
            WHERE paper_order.id IN (
                SELECT target.id
                FROM tmp_eth_binance_bps_paper_orders target
            );
            GET DIAGNOSTICS deleted_paper_orders = ROW_COUNT;

            DELETE FROM signal_rejections rejection
            WHERE rejection.signal_id IN (
                SELECT target.id
                FROM tmp_eth_binance_bps_signals target
            );
            GET DIAGNOSTICS deleted_signal_rejections = ROW_COUNT;

            DELETE FROM signals signal
            WHERE signal.id IN (
                SELECT target.id
                FROM tmp_eth_binance_bps_signals target
            );
            GET DIAGNOSTICS deleted_signals = ROW_COUNT;

            DELETE FROM paper_positions paper_position
            WHERE paper_position.copied_trader_wallet IN (
                SELECT 'strategy:' || target.code
                FROM tmp_eth_binance_bps_strategy_targets target
            );
            GET DIAGNOSTICS deleted_paper_positions = ROW_COUNT;

            DELETE FROM paper_position_settlements settlement
            WHERE settlement.copied_trader_wallet IN (
                SELECT 'strategy:' || target.code
                FROM tmp_eth_binance_bps_strategy_targets target
            );
            GET DIAGNOSTICS deleted_paper_position_settlements = ROW_COUNT;

            DELETE FROM dashboard_strategy_performance_snapshots snapshot
            WHERE snapshot.strategy_id IN (
                    SELECT target.id
                    FROM tmp_eth_binance_bps_strategy_targets target
                )
               OR snapshot.code IN (
                    SELECT target.code
                    FROM tmp_eth_binance_bps_strategy_targets target
                );
            GET DIAGNOSTICS deleted_dashboard_snapshots = ROW_COUNT;

            DELETE FROM dashboard_strategy_recent_performance_snapshots snapshot
            WHERE snapshot.strategy_id IN (
                    SELECT target.id
                    FROM tmp_eth_binance_bps_strategy_targets target
                )
               OR snapshot.code IN (
                    SELECT target.code
                    FROM tmp_eth_binance_bps_strategy_targets target
                );
            GET DIAGNOSTICS deleted_dashboard_recent_snapshots = ROW_COUNT;

            DELETE FROM strategies strategy
            WHERE strategy.id IN (
                SELECT target.id
                FROM tmp_eth_binance_bps_strategy_targets target
            );
            GET DIAGNOSTICS deleted_strategies = ROW_COUNT;

            INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
            VALUES (
                migration_key_value,
                clock_timestamp(),
                'target_strategies=' || target_strategy_count::text ||
                ';paper_orders=' || deleted_paper_orders::text ||
                ';paper_fills=' || deleted_paper_fills::text ||
                ';strategy_runs=' || deleted_strategy_runs::text ||
                ';live_orders=' || deleted_live_orders::text ||
                ';shadow_decisions=' || deleted_shadow_decisions::text ||
                ';shadow_discrepancies=' || deleted_shadow_discrepancies::text ||
                ';signals=' || deleted_signals::text ||
                ';signal_rejections=' || deleted_signal_rejections::text ||
                ';paper_positions=' || deleted_paper_positions::text ||
                ';paper_position_settlements=' || deleted_paper_position_settlements::text ||
                ';dashboard_snapshots=' || deleted_dashboard_snapshots::text ||
                ';dashboard_recent_snapshots=' || deleted_dashboard_recent_snapshots::text ||
                ';strategies=' || deleted_strategies::text
            );
        END IF;
    END IF;
END $$;

DO $$
DECLARE
    migration_key_value text := '20260709_remove_sol_binance_bps_strategies';
    target_strategy_count integer := 0;
    deleted_shadow_discrepancies integer := 0;
    deleted_shadow_decisions integer := 0;
    deleted_live_orders integer := 0;
    deleted_strategy_runs integer := 0;
    deleted_paper_fills integer := 0;
    deleted_paper_orders integer := 0;
    deleted_signal_rejections integer := 0;
    deleted_signals integer := 0;
    deleted_paper_positions integer := 0;
    deleted_paper_position_settlements integer := 0;
    deleted_dashboard_snapshots integer := 0;
    deleted_dashboard_recent_snapshots integer := 0;
    deleted_dry_run_orders integer := 0;
    deleted_date_dependent_hourly_pnl integer := 0;
    deleted_diff_shift_states integer := 0;
    deleted_child_assignments integer := 0;
    deleted_strategies integer := 0;
    active_live_orders integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        DROP TABLE IF EXISTS tmp_sol_binance_bps_strategy_targets;
        DROP TABLE IF EXISTS tmp_sol_binance_bps_paper_orders;
        DROP TABLE IF EXISTS tmp_sol_binance_bps_live_orders;
        DROP TABLE IF EXISTS tmp_sol_binance_bps_runs;
        DROP TABLE IF EXISTS tmp_sol_binance_bps_signals;

        CREATE TEMP TABLE tmp_sol_binance_bps_strategy_targets ON COMMIT DROP AS
        SELECT strategy.id, strategy.code
        FROM strategies strategy
        WHERE strategy.code LIKE 'sol_up_down_5m_binance_bps_%'
           OR strategy.name ILIKE 'SOL Up or Down 5m Binance % bps%';

        SELECT count(*)::integer
        INTO target_strategy_count
        FROM tmp_sol_binance_bps_strategy_targets;

        IF target_strategy_count = 0 THEN
            INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
            VALUES (
                migration_key_value,
                clock_timestamp(),
                'target_strategies=0' ||
                ';paper_orders=0' ||
                ';paper_fills=0' ||
                ';strategy_runs=0' ||
                ';live_orders=0' ||
                ';shadow_decisions=0' ||
                ';shadow_discrepancies=0' ||
                ';signals=0' ||
                ';signal_rejections=0' ||
                ';paper_positions=0' ||
                ';paper_position_settlements=0' ||
                ';dashboard_snapshots=0' ||
                ';dashboard_recent_snapshots=0' ||
                ';dry_run_orders=0' ||
                ';date_dependent_hourly_pnl=0' ||
                ';diff_shift_states=0' ||
                ';child_assignments=0' ||
                ';strategies=0'
            );
        ELSE
            CREATE TEMP TABLE tmp_sol_binance_bps_paper_orders ON COMMIT DROP AS
            SELECT paper_order.id, paper_order.signal_id
            FROM paper_orders paper_order
            WHERE paper_order.strategy_id IN (
                    SELECT target.id
                    FROM tmp_sol_binance_bps_strategy_targets target
                )
               OR paper_order.copied_trader_wallet IN (
                    SELECT 'strategy:' || target.code
                    FROM tmp_sol_binance_bps_strategy_targets target
                );

            CREATE TEMP TABLE tmp_sol_binance_bps_live_orders ON COMMIT DROP AS
            SELECT live_order.id
            FROM live_orders live_order
            WHERE live_order.strategy_id IN (
                SELECT target.id
                FROM tmp_sol_binance_bps_strategy_targets target
            );

            CREATE TEMP TABLE tmp_sol_binance_bps_runs ON COMMIT DROP AS
            SELECT run.id, run.paper_order_id, run.signal_id
            FROM strategy_market_paper_runs run
            WHERE run.strategy_id IN (
                SELECT target.id
                FROM tmp_sol_binance_bps_strategy_targets target
            );

            CREATE TEMP TABLE tmp_sol_binance_bps_signals(id uuid) ON COMMIT DROP;

            INSERT INTO tmp_sol_binance_bps_signals
            SELECT DISTINCT paper_order.signal_id
            FROM tmp_sol_binance_bps_paper_orders paper_order
            WHERE paper_order.signal_id IS NOT NULL
              AND paper_order.signal_id NOT IN (
                  SELECT signal.id
                  FROM tmp_sol_binance_bps_signals signal
              );

            INSERT INTO tmp_sol_binance_bps_signals
            SELECT DISTINCT run.signal_id
            FROM tmp_sol_binance_bps_runs run
            WHERE run.signal_id IS NOT NULL
              AND run.signal_id NOT IN (
                  SELECT signal.id
                  FROM tmp_sol_binance_bps_signals signal
              );

            SELECT count(*)::integer
            INTO active_live_orders
            FROM live_orders live_order
            WHERE live_order.id IN (
                    SELECT target.id
                    FROM tmp_sol_binance_bps_live_orders target
                )
              AND live_order.status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested');

            IF active_live_orders = 0 THEN
                DELETE FROM paper_live_shadow_discrepancies discrepancy
                WHERE discrepancy.strategy_id IN (
                    SELECT target.id
                    FROM tmp_sol_binance_bps_strategy_targets target
                );
                GET DIAGNOSTICS deleted_shadow_discrepancies = ROW_COUNT;

                DELETE FROM paper_live_shadow_decisions decision
                WHERE decision.strategy_id IN (
                        SELECT target.id
                        FROM tmp_sol_binance_bps_strategy_targets target
                    )
                   OR decision.paper_order_id IN (
                        SELECT target.id
                        FROM tmp_sol_binance_bps_paper_orders target
                    )
                   OR decision.live_order_id IN (
                        SELECT target.id
                        FROM tmp_sol_binance_bps_live_orders target
                    )
                   OR decision.signal_id IN (
                        SELECT target.id
                        FROM tmp_sol_binance_bps_signals target
                    );
                GET DIAGNOSTICS deleted_shadow_decisions = ROW_COUNT;

                DELETE FROM dry_run_orders dry_run_order
                WHERE dry_run_order.strategy_id IN (
                    SELECT target.id
                    FROM tmp_sol_binance_bps_strategy_targets target
                );
                GET DIAGNOSTICS deleted_dry_run_orders = ROW_COUNT;

                DELETE FROM date_dependent_strategy_hourly_paper_pnl hourly_pnl
                WHERE hourly_pnl.strategy_id IN (
                    SELECT target.id
                    FROM tmp_sol_binance_bps_strategy_targets target
                );
                GET DIAGNOSTICS deleted_date_dependent_hourly_pnl = ROW_COUNT;

                DELETE FROM crypto_up_down_5m_diff_shift_progress_states state
                WHERE state.strategy_id IN (
                    SELECT target.id
                    FROM tmp_sol_binance_bps_strategy_targets target
                );
                GET DIAGNOSTICS deleted_diff_shift_states = ROW_COUNT;

                DELETE FROM strategy_child_parent_assignments assignment
                WHERE assignment.child_strategy_id IN (
                        SELECT target.id
                        FROM tmp_sol_binance_bps_strategy_targets target
                    )
                   OR assignment.parent_strategy_id IN (
                        SELECT target.id
                        FROM tmp_sol_binance_bps_strategy_targets target
                    );
                GET DIAGNOSTICS deleted_child_assignments = ROW_COUNT;

                DELETE FROM dashboard_strategy_performance_snapshots snapshot
                WHERE snapshot.strategy_id IN (
                        SELECT target.id
                        FROM tmp_sol_binance_bps_strategy_targets target
                    )
                   OR snapshot.code IN (
                        SELECT target.code
                        FROM tmp_sol_binance_bps_strategy_targets target
                    );
                GET DIAGNOSTICS deleted_dashboard_snapshots = ROW_COUNT;

                DELETE FROM dashboard_strategy_recent_performance_snapshots snapshot
                WHERE snapshot.strategy_id IN (
                        SELECT target.id
                        FROM tmp_sol_binance_bps_strategy_targets target
                    )
                   OR snapshot.code IN (
                        SELECT target.code
                        FROM tmp_sol_binance_bps_strategy_targets target
                    );
                GET DIAGNOSTICS deleted_dashboard_recent_snapshots = ROW_COUNT;

                DELETE FROM live_orders live_order
                WHERE live_order.id IN (
                    SELECT target.id
                    FROM tmp_sol_binance_bps_live_orders target
                );
                GET DIAGNOSTICS deleted_live_orders = ROW_COUNT;

                DELETE FROM strategy_market_paper_runs run
                WHERE run.id IN (
                    SELECT target.id
                    FROM tmp_sol_binance_bps_runs target
                );
                GET DIAGNOSTICS deleted_strategy_runs = ROW_COUNT;

                DELETE FROM paper_fills fill
                WHERE fill.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_sol_binance_bps_paper_orders target
                );
                GET DIAGNOSTICS deleted_paper_fills = ROW_COUNT;

                DELETE FROM paper_orders paper_order
                WHERE paper_order.id IN (
                    SELECT target.id
                    FROM tmp_sol_binance_bps_paper_orders target
                );
                GET DIAGNOSTICS deleted_paper_orders = ROW_COUNT;

                DELETE FROM signal_rejections rejection
                WHERE rejection.signal_id IN (
                    SELECT target.id
                    FROM tmp_sol_binance_bps_signals target
                );
                GET DIAGNOSTICS deleted_signal_rejections = ROW_COUNT;

                DELETE FROM signals signal
                WHERE signal.id IN (
                    SELECT target.id
                    FROM tmp_sol_binance_bps_signals target
                );
                GET DIAGNOSTICS deleted_signals = ROW_COUNT;

                DELETE FROM paper_positions paper_position
                WHERE paper_position.copied_trader_wallet IN (
                    SELECT 'strategy:' || target.code
                    FROM tmp_sol_binance_bps_strategy_targets target
                );
                GET DIAGNOSTICS deleted_paper_positions = ROW_COUNT;

                DELETE FROM paper_position_settlements settlement
                WHERE settlement.copied_trader_wallet IN (
                    SELECT 'strategy:' || target.code
                    FROM tmp_sol_binance_bps_strategy_targets target
                );
                GET DIAGNOSTICS deleted_paper_position_settlements = ROW_COUNT;

                DELETE FROM strategies strategy
                WHERE strategy.id IN (
                        SELECT target.id
                        FROM tmp_sol_binance_bps_strategy_targets target
                    )
                   OR strategy.code IN (
                        SELECT target.code
                        FROM tmp_sol_binance_bps_strategy_targets target
                    );
                GET DIAGNOSTICS deleted_strategies = ROW_COUNT;

                INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
                VALUES (
                    migration_key_value,
                    clock_timestamp(),
                    'target_strategies=' || target_strategy_count::text ||
                    ';paper_orders=' || deleted_paper_orders::text ||
                    ';paper_fills=' || deleted_paper_fills::text ||
                    ';strategy_runs=' || deleted_strategy_runs::text ||
                    ';live_orders=' || deleted_live_orders::text ||
                    ';shadow_decisions=' || deleted_shadow_decisions::text ||
                    ';shadow_discrepancies=' || deleted_shadow_discrepancies::text ||
                    ';signals=' || deleted_signals::text ||
                    ';signal_rejections=' || deleted_signal_rejections::text ||
                    ';paper_positions=' || deleted_paper_positions::text ||
                    ';paper_position_settlements=' || deleted_paper_position_settlements::text ||
                    ';dashboard_snapshots=' || deleted_dashboard_snapshots::text ||
                    ';dashboard_recent_snapshots=' || deleted_dashboard_recent_snapshots::text ||
                    ';dry_run_orders=' || deleted_dry_run_orders::text ||
                    ';date_dependent_hourly_pnl=' || deleted_date_dependent_hourly_pnl::text ||
                    ';diff_shift_states=' || deleted_diff_shift_states::text ||
                    ';child_assignments=' || deleted_child_assignments::text ||
                    ';strategies=' || deleted_strategies::text
                );
            END IF;
        END IF;
    END IF;
END $$;

DO $$
DECLARE
    migration_key_value text := '20260707_remove_skip_strategies';
    target_strategy_count integer := 0;
    deleted_shadow_discrepancies integer := 0;
    deleted_shadow_decisions integer := 0;
    deleted_live_orders integer := 0;
    deleted_strategy_runs integer := 0;
    deleted_paper_fills integer := 0;
    deleted_paper_orders integer := 0;
    deleted_signal_rejections integer := 0;
    deleted_signals integer := 0;
    deleted_paper_positions integer := 0;
    deleted_paper_position_settlements integer := 0;
    deleted_dashboard_snapshots integer := 0;
    deleted_dashboard_recent_snapshots integer := 0;
    deleted_strategies integer := 0;
    active_live_orders integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        DROP TABLE IF EXISTS tmp_skip_strategy_targets;
        DROP TABLE IF EXISTS tmp_skip_strategy_paper_orders;
        DROP TABLE IF EXISTS tmp_skip_strategy_live_orders;
        DROP TABLE IF EXISTS tmp_skip_strategy_signals;

        CREATE TEMP TABLE tmp_skip_strategy_targets ON COMMIT DROP AS
        SELECT strategy.id, strategy.code
        FROM strategies strategy
        WHERE strategy.code LIKE '%\_up\_down\_5m\_skip\_%' ESCAPE '\'
           OR strategy.name ILIKE '% Up or Down 5m Skip %';

        SELECT count(*)::integer
        INTO target_strategy_count
        FROM tmp_skip_strategy_targets;

        CREATE TEMP TABLE tmp_skip_strategy_paper_orders ON COMMIT DROP AS
        SELECT paper_order.id
        FROM paper_orders paper_order
        WHERE paper_order.strategy_id IN (
                SELECT target.id
                FROM tmp_skip_strategy_targets target
            )
           OR paper_order.copied_trader_wallet IN (
                SELECT 'strategy:' || target.code
                FROM tmp_skip_strategy_targets target
            );

        CREATE TEMP TABLE tmp_skip_strategy_live_orders ON COMMIT DROP AS
        SELECT live_order.id
        FROM live_orders live_order
        WHERE live_order.strategy_id IN (
            SELECT target.id
            FROM tmp_skip_strategy_targets target
        );

        CREATE TEMP TABLE tmp_skip_strategy_signals ON COMMIT DROP AS
        SELECT signal.id
        FROM signals signal
        WHERE signal.trader_wallet IN (
            SELECT 'strategy:' || target.code
            FROM tmp_skip_strategy_targets target
        );

        SELECT count(*)::integer
        INTO active_live_orders
        FROM live_orders live_order
        WHERE live_order.id IN (
                SELECT target.id
                FROM tmp_skip_strategy_live_orders target
            )
          AND live_order.status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested');

        IF active_live_orders = 0 THEN
            DELETE FROM paper_live_shadow_discrepancies discrepancy
            WHERE discrepancy.strategy_id IN (
                SELECT target.id
                FROM tmp_skip_strategy_targets target
            );
            GET DIAGNOSTICS deleted_shadow_discrepancies = ROW_COUNT;

            DELETE FROM paper_live_shadow_decisions decision
            WHERE decision.strategy_id IN (
                    SELECT target.id
                    FROM tmp_skip_strategy_targets target
                )
               OR decision.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_skip_strategy_paper_orders target
                )
               OR decision.live_order_id IN (
                    SELECT target.id
                    FROM tmp_skip_strategy_live_orders target
                )
               OR decision.signal_id IN (
                    SELECT target.id
                    FROM tmp_skip_strategy_signals target
                );
            GET DIAGNOSTICS deleted_shadow_decisions = ROW_COUNT;

            DELETE FROM live_orders live_order
            WHERE live_order.id IN (
                SELECT target.id
                FROM tmp_skip_strategy_live_orders target
            );
            GET DIAGNOSTICS deleted_live_orders = ROW_COUNT;

            DELETE FROM strategy_market_paper_runs run
            WHERE run.strategy_id IN (
                    SELECT target.id
                    FROM tmp_skip_strategy_targets target
                )
               OR run.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_skip_strategy_paper_orders target
                )
               OR run.signal_id IN (
                    SELECT target.id
                    FROM tmp_skip_strategy_signals target
                );
            GET DIAGNOSTICS deleted_strategy_runs = ROW_COUNT;

            DELETE FROM paper_fills fill
            WHERE fill.paper_order_id IN (
                SELECT target.id
                FROM tmp_skip_strategy_paper_orders target
            );
            GET DIAGNOSTICS deleted_paper_fills = ROW_COUNT;

            DELETE FROM paper_orders paper_order
            WHERE paper_order.id IN (
                SELECT target.id
                FROM tmp_skip_strategy_paper_orders target
            );
            GET DIAGNOSTICS deleted_paper_orders = ROW_COUNT;

            DELETE FROM signal_rejections rejection
            WHERE rejection.signal_id IN (
                SELECT target.id
                FROM tmp_skip_strategy_signals target
            );
            GET DIAGNOSTICS deleted_signal_rejections = ROW_COUNT;

            DELETE FROM signals signal
            WHERE signal.id IN (
                SELECT target.id
                FROM tmp_skip_strategy_signals target
            );
            GET DIAGNOSTICS deleted_signals = ROW_COUNT;

            DELETE FROM paper_positions paper_position
            WHERE paper_position.copied_trader_wallet IN (
                SELECT 'strategy:' || target.code
                FROM tmp_skip_strategy_targets target
            );
            GET DIAGNOSTICS deleted_paper_positions = ROW_COUNT;

            DELETE FROM paper_position_settlements settlement
            WHERE settlement.copied_trader_wallet IN (
                SELECT 'strategy:' || target.code
                FROM tmp_skip_strategy_targets target
            );
            GET DIAGNOSTICS deleted_paper_position_settlements = ROW_COUNT;

            DELETE FROM dashboard_strategy_performance_snapshots snapshot
            WHERE snapshot.strategy_id IN (
                    SELECT target.id
                    FROM tmp_skip_strategy_targets target
                )
               OR snapshot.code IN (
                    SELECT target.code
                    FROM tmp_skip_strategy_targets target
                );
            GET DIAGNOSTICS deleted_dashboard_snapshots = ROW_COUNT;

            DELETE FROM dashboard_strategy_recent_performance_snapshots snapshot
            WHERE snapshot.strategy_id IN (
                    SELECT target.id
                    FROM tmp_skip_strategy_targets target
                )
               OR snapshot.code IN (
                    SELECT target.code
                    FROM tmp_skip_strategy_targets target
                );
            GET DIAGNOSTICS deleted_dashboard_recent_snapshots = ROW_COUNT;

            DELETE FROM strategies strategy
            WHERE strategy.id IN (
                SELECT target.id
                FROM tmp_skip_strategy_targets target
            );
            GET DIAGNOSTICS deleted_strategies = ROW_COUNT;

            INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
            VALUES (
                migration_key_value,
                clock_timestamp(),
                'target_strategies=' || target_strategy_count::text ||
                ';paper_orders=' || deleted_paper_orders::text ||
                ';paper_fills=' || deleted_paper_fills::text ||
                ';strategy_runs=' || deleted_strategy_runs::text ||
                ';live_orders=' || deleted_live_orders::text ||
                ';shadow_decisions=' || deleted_shadow_decisions::text ||
                ';shadow_discrepancies=' || deleted_shadow_discrepancies::text ||
                ';signals=' || deleted_signals::text ||
                ';signal_rejections=' || deleted_signal_rejections::text ||
                ';paper_positions=' || deleted_paper_positions::text ||
                ';paper_position_settlements=' || deleted_paper_position_settlements::text ||
                ';dashboard_snapshots=' || deleted_dashboard_snapshots::text ||
                ';dashboard_recent_snapshots=' || deleted_dashboard_recent_snapshots::text ||
                ';strategies=' || deleted_strategies::text
            );
        END IF;
    END IF;
END $$;

DO $$
DECLARE
    migration_key_value text := '20260708_remove_simple_strategies';
    target_strategy_count integer := 0;
    deleted_shadow_discrepancies integer := 0;
    deleted_shadow_decisions integer := 0;
    deleted_live_orders integer := 0;
    deleted_strategy_runs integer := 0;
    deleted_paper_fills integer := 0;
    deleted_paper_orders integer := 0;
    deleted_signal_rejections integer := 0;
    deleted_signals integer := 0;
    deleted_paper_positions integer := 0;
    deleted_paper_position_settlements integer := 0;
    deleted_dashboard_snapshots integer := 0;
    deleted_dashboard_recent_snapshots integer := 0;
    deleted_dry_run_orders integer := 0;
    deleted_date_dependent_hourly_pnl integer := 0;
    deleted_diff_shift_states integer := 0;
    deleted_strategies integer := 0;
    active_live_orders integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        DROP TABLE IF EXISTS tmp_simple_strategy_targets;
        DROP TABLE IF EXISTS tmp_simple_strategy_paper_orders;
        DROP TABLE IF EXISTS tmp_simple_strategy_live_orders;
        DROP TABLE IF EXISTS tmp_simple_strategy_runs;
        DROP TABLE IF EXISTS tmp_simple_strategy_signals;

        CREATE TEMP TABLE tmp_simple_strategy_targets(id, code) ON COMMIT DROP AS
        VALUES
            ('b7c50005-0000-4000-8121-000000000001'::uuid, 'btc_up_down_5m_up_simple'),
            ('b7c50005-0000-4000-8122-000000000001'::uuid, 'btc_up_down_5m_down_simple'),
            ('b7c50005-0000-4000-8123-000000000001'::uuid, 'eth_up_down_5m_up_simple'),
            ('b7c50005-0000-4000-8124-000000000001'::uuid, 'eth_up_down_5m_down_simple'),
            ('b7c50005-0000-4000-8125-000000000001'::uuid, 'sol_up_down_5m_up_simple'),
            ('b7c50005-0000-4000-8126-000000000001'::uuid, 'sol_up_down_5m_down_simple');

        SELECT count(*)::integer
        INTO target_strategy_count
        FROM strategies strategy
        WHERE strategy.id IN (
                SELECT target.id
                FROM tmp_simple_strategy_targets target
            )
           OR strategy.code IN (
                SELECT target.code
                FROM tmp_simple_strategy_targets target
            );

        CREATE TEMP TABLE tmp_simple_strategy_paper_orders ON COMMIT DROP AS
        SELECT paper_order.id, paper_order.signal_id
        FROM paper_orders paper_order
        WHERE paper_order.strategy_id IN (
                SELECT target.id
                FROM tmp_simple_strategy_targets target
            )
           OR paper_order.copied_trader_wallet IN (
                SELECT 'strategy:' || target.code
                FROM tmp_simple_strategy_targets target
            );

        CREATE TEMP TABLE tmp_simple_strategy_live_orders ON COMMIT DROP AS
        SELECT live_order.id
        FROM live_orders live_order
        WHERE live_order.strategy_id IN (
            SELECT target.id
            FROM tmp_simple_strategy_targets target
        );

        CREATE TEMP TABLE tmp_simple_strategy_runs ON COMMIT DROP AS
        SELECT run.id, run.paper_order_id, run.signal_id
        FROM strategy_market_paper_runs run
        WHERE run.strategy_id IN (
            SELECT target.id
            FROM tmp_simple_strategy_targets target
        );

        CREATE TEMP TABLE tmp_simple_strategy_signals ON COMMIT DROP AS
        SELECT signal.id
        FROM signals signal
        WHERE signal.trader_wallet IN (
            SELECT 'strategy:' || target.code
            FROM tmp_simple_strategy_targets target
        );

        INSERT INTO tmp_simple_strategy_signals
        SELECT DISTINCT paper_order.signal_id
        FROM tmp_simple_strategy_paper_orders paper_order
        WHERE paper_order.signal_id IS NOT NULL
          AND paper_order.signal_id NOT IN (
              SELECT signal.id
              FROM tmp_simple_strategy_signals signal
          );

        INSERT INTO tmp_simple_strategy_signals
        SELECT DISTINCT run.signal_id
        FROM tmp_simple_strategy_runs run
        WHERE run.signal_id IS NOT NULL
          AND run.signal_id NOT IN (
              SELECT signal.id
              FROM tmp_simple_strategy_signals signal
          );

        SELECT count(*)::integer
        INTO active_live_orders
        FROM live_orders live_order
        WHERE live_order.id IN (
                SELECT target.id
                FROM tmp_simple_strategy_live_orders target
            )
          AND live_order.status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested');

        IF active_live_orders = 0 THEN
            DELETE FROM paper_live_shadow_discrepancies discrepancy
            WHERE discrepancy.strategy_id IN (
                SELECT target.id
                FROM tmp_simple_strategy_targets target
            );
            GET DIAGNOSTICS deleted_shadow_discrepancies = ROW_COUNT;

            DELETE FROM paper_live_shadow_decisions decision
            WHERE decision.strategy_id IN (
                    SELECT target.id
                    FROM tmp_simple_strategy_targets target
                )
               OR decision.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_simple_strategy_paper_orders target
                )
               OR decision.live_order_id IN (
                    SELECT target.id
                    FROM tmp_simple_strategy_live_orders target
                )
               OR decision.signal_id IN (
                    SELECT target.id
                    FROM tmp_simple_strategy_signals target
                );
            GET DIAGNOSTICS deleted_shadow_decisions = ROW_COUNT;

            DELETE FROM dry_run_orders dry_run_order
            WHERE dry_run_order.strategy_id IN (
                SELECT target.id
                FROM tmp_simple_strategy_targets target
            );
            GET DIAGNOSTICS deleted_dry_run_orders = ROW_COUNT;

            DELETE FROM date_dependent_strategy_hourly_paper_pnl hourly_pnl
            WHERE hourly_pnl.strategy_id IN (
                SELECT target.id
                FROM tmp_simple_strategy_targets target
            );
            GET DIAGNOSTICS deleted_date_dependent_hourly_pnl = ROW_COUNT;

            DELETE FROM crypto_up_down_5m_diff_shift_progress_states state
            WHERE state.strategy_id IN (
                SELECT target.id
                FROM tmp_simple_strategy_targets target
            );
            GET DIAGNOSTICS deleted_diff_shift_states = ROW_COUNT;

            DELETE FROM dashboard_strategy_performance_snapshots snapshot
            WHERE snapshot.strategy_id IN (
                    SELECT target.id
                    FROM tmp_simple_strategy_targets target
                )
               OR snapshot.code IN (
                    SELECT target.code
                    FROM tmp_simple_strategy_targets target
                );
            GET DIAGNOSTICS deleted_dashboard_snapshots = ROW_COUNT;

            DELETE FROM dashboard_strategy_recent_performance_snapshots snapshot
            WHERE snapshot.strategy_id IN (
                    SELECT target.id
                    FROM tmp_simple_strategy_targets target
                )
               OR snapshot.code IN (
                    SELECT target.code
                    FROM tmp_simple_strategy_targets target
                );
            GET DIAGNOSTICS deleted_dashboard_recent_snapshots = ROW_COUNT;

            DELETE FROM live_orders live_order
            WHERE live_order.id IN (
                SELECT target.id
                FROM tmp_simple_strategy_live_orders target
            );
            GET DIAGNOSTICS deleted_live_orders = ROW_COUNT;

            DELETE FROM strategy_market_paper_runs run
            WHERE run.strategy_id IN (
                    SELECT target.id
                    FROM tmp_simple_strategy_targets target
                )
               OR run.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_simple_strategy_paper_orders target
                )
               OR run.signal_id IN (
                    SELECT target.id
                    FROM tmp_simple_strategy_signals target
                );
            GET DIAGNOSTICS deleted_strategy_runs = ROW_COUNT;

            DELETE FROM paper_fills fill
            WHERE fill.paper_order_id IN (
                SELECT target.id
                FROM tmp_simple_strategy_paper_orders target
            );
            GET DIAGNOSTICS deleted_paper_fills = ROW_COUNT;

            DELETE FROM paper_orders paper_order
            WHERE paper_order.id IN (
                SELECT target.id
                FROM tmp_simple_strategy_paper_orders target
            );
            GET DIAGNOSTICS deleted_paper_orders = ROW_COUNT;

            DELETE FROM signal_rejections rejection
            WHERE rejection.signal_id IN (
                SELECT target.id
                FROM tmp_simple_strategy_signals target
            );
            GET DIAGNOSTICS deleted_signal_rejections = ROW_COUNT;

            DELETE FROM signals signal
            WHERE signal.id IN (
                SELECT target.id
                FROM tmp_simple_strategy_signals target
            );
            GET DIAGNOSTICS deleted_signals = ROW_COUNT;

            DELETE FROM paper_positions paper_position
            WHERE paper_position.copied_trader_wallet IN (
                SELECT 'strategy:' || target.code
                FROM tmp_simple_strategy_targets target
            );
            GET DIAGNOSTICS deleted_paper_positions = ROW_COUNT;

            DELETE FROM paper_position_settlements settlement
            WHERE settlement.copied_trader_wallet IN (
                SELECT 'strategy:' || target.code
                FROM tmp_simple_strategy_targets target
            );
            GET DIAGNOSTICS deleted_paper_position_settlements = ROW_COUNT;

            DELETE FROM strategies strategy
            WHERE strategy.id IN (
                SELECT target.id
                FROM tmp_simple_strategy_targets target
            );
            GET DIAGNOSTICS deleted_strategies = ROW_COUNT;

            INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
            VALUES (
                migration_key_value,
                clock_timestamp(),
                'target_strategies=' || target_strategy_count::text ||
                ';paper_orders=' || deleted_paper_orders::text ||
                ';paper_fills=' || deleted_paper_fills::text ||
                ';strategy_runs=' || deleted_strategy_runs::text ||
                ';live_orders=' || deleted_live_orders::text ||
                ';shadow_decisions=' || deleted_shadow_decisions::text ||
                ';shadow_discrepancies=' || deleted_shadow_discrepancies::text ||
                ';signals=' || deleted_signals::text ||
                ';signal_rejections=' || deleted_signal_rejections::text ||
                ';paper_positions=' || deleted_paper_positions::text ||
                ';paper_position_settlements=' || deleted_paper_position_settlements::text ||
                ';dashboard_snapshots=' || deleted_dashboard_snapshots::text ||
                ';dashboard_recent_snapshots=' || deleted_dashboard_recent_snapshots::text ||
                ';dry_run_orders=' || deleted_dry_run_orders::text ||
                ';date_dependent_hourly_pnl=' || deleted_date_dependent_hourly_pnl::text ||
                ';diff_shift_states=' || deleted_diff_shift_states::text ||
                ';strategies=' || deleted_strategies::text
            );
        END IF;
    END IF;
END $$;

DO $$
DECLARE
    migration_key_value text := '20260709_remove_follow_leader_strategy';
    follow_leader_id uuid := 'f0110a0d-1ead-4c00-8b01-000000000001';
    target_strategy_count integer := 0;
    deleted_shadow_discrepancies integer := 0;
    deleted_shadow_decisions integer := 0;
    deleted_live_orders integer := 0;
    deleted_strategy_runs integer := 0;
    deleted_paper_fills integer := 0;
    deleted_paper_orders integer := 0;
    deleted_dashboard_snapshots integer := 0;
    deleted_dashboard_recent_snapshots integer := 0;
    deleted_dry_run_orders integer := 0;
    deleted_date_dependent_hourly_pnl integer := 0;
    deleted_diff_shift_states integer := 0;
    deleted_child_assignments integer := 0;
    deleted_paper_positions integer := 0;
    deleted_paper_position_settlements integer := 0;
    deleted_strategies integer := 0;
    active_live_orders integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        DROP TABLE IF EXISTS tmp_follow_leader_strategy_targets;
        DROP TABLE IF EXISTS tmp_follow_leader_paper_orders;
        DROP TABLE IF EXISTS tmp_follow_leader_live_orders;
        DROP TABLE IF EXISTS tmp_follow_leader_runs;

        CREATE TEMP TABLE tmp_follow_leader_strategy_targets(id, code) ON COMMIT DROP AS
        VALUES (follow_leader_id, 'follow_leader');

        SELECT count(*)::integer
        INTO target_strategy_count
        FROM strategies strategy
        WHERE strategy.id IN (
                SELECT target.id
                FROM tmp_follow_leader_strategy_targets target
            )
           OR strategy.code IN (
                SELECT target.code
                FROM tmp_follow_leader_strategy_targets target
            );

        IF target_strategy_count = 0 THEN
            INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
            VALUES (
                migration_key_value,
                clock_timestamp(),
                'target_strategies=0' ||
                ';paper_orders=0' ||
                ';paper_fills=0' ||
                ';strategy_runs=0' ||
                ';live_orders=0' ||
                ';shadow_decisions=0' ||
                ';shadow_discrepancies=0' ||
                ';paper_positions=0' ||
                ';paper_position_settlements=0' ||
                ';dashboard_snapshots=0' ||
                ';dashboard_recent_snapshots=0' ||
                ';dry_run_orders=0' ||
                ';date_dependent_hourly_pnl=0' ||
                ';diff_shift_states=0' ||
                ';child_assignments=0' ||
                ';strategies=0'
            );
        ELSE
        CREATE TEMP TABLE tmp_follow_leader_paper_orders ON COMMIT DROP AS
        SELECT paper_order.id
        FROM paper_orders paper_order
        WHERE paper_order.strategy_id IN (
            SELECT target.id
            FROM tmp_follow_leader_strategy_targets target
        );

        CREATE TEMP TABLE tmp_follow_leader_live_orders ON COMMIT DROP AS
        SELECT live_order.id
        FROM live_orders live_order
        WHERE live_order.strategy_id IN (
            SELECT target.id
            FROM tmp_follow_leader_strategy_targets target
        );

        CREATE TEMP TABLE tmp_follow_leader_runs ON COMMIT DROP AS
        SELECT run.id
        FROM strategy_market_paper_runs run
        WHERE run.strategy_id IN (
            SELECT target.id
            FROM tmp_follow_leader_strategy_targets target
        );

        SELECT count(*)::integer
        INTO active_live_orders
        FROM live_orders live_order
        WHERE live_order.id IN (
                SELECT target.id
                FROM tmp_follow_leader_live_orders target
            )
          AND live_order.status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested');

        IF active_live_orders = 0 THEN
            DELETE FROM paper_live_shadow_discrepancies discrepancy
            WHERE discrepancy.strategy_id IN (
                SELECT target.id
                FROM tmp_follow_leader_strategy_targets target
            );
            GET DIAGNOSTICS deleted_shadow_discrepancies = ROW_COUNT;

            DELETE FROM paper_live_shadow_decisions decision
            WHERE decision.strategy_id IN (
                    SELECT target.id
                    FROM tmp_follow_leader_strategy_targets target
                )
               OR decision.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_follow_leader_paper_orders target
                )
               OR decision.live_order_id IN (
                    SELECT target.id
                    FROM tmp_follow_leader_live_orders target
                );
            GET DIAGNOSTICS deleted_shadow_decisions = ROW_COUNT;

            DELETE FROM dry_run_orders dry_run_order
            WHERE dry_run_order.strategy_id IN (
                SELECT target.id
                FROM tmp_follow_leader_strategy_targets target
            );
            GET DIAGNOSTICS deleted_dry_run_orders = ROW_COUNT;

            DELETE FROM date_dependent_strategy_hourly_paper_pnl hourly_pnl
            WHERE hourly_pnl.strategy_id IN (
                SELECT target.id
                FROM tmp_follow_leader_strategy_targets target
            );
            GET DIAGNOSTICS deleted_date_dependent_hourly_pnl = ROW_COUNT;

            DELETE FROM crypto_up_down_5m_diff_shift_progress_states state
            WHERE state.strategy_id IN (
                SELECT target.id
                FROM tmp_follow_leader_strategy_targets target
            );
            GET DIAGNOSTICS deleted_diff_shift_states = ROW_COUNT;

            DELETE FROM strategy_child_parent_assignments assignment
            WHERE assignment.child_strategy_id IN (
                    SELECT target.id
                    FROM tmp_follow_leader_strategy_targets target
                )
               OR assignment.parent_strategy_id IN (
                    SELECT target.id
                    FROM tmp_follow_leader_strategy_targets target
                );
            GET DIAGNOSTICS deleted_child_assignments = ROW_COUNT;

            DELETE FROM dashboard_strategy_performance_snapshots snapshot
            WHERE snapshot.strategy_id IN (
                    SELECT target.id
                    FROM tmp_follow_leader_strategy_targets target
                )
               OR snapshot.code IN (
                    SELECT target.code
                    FROM tmp_follow_leader_strategy_targets target
                );
            GET DIAGNOSTICS deleted_dashboard_snapshots = ROW_COUNT;

            DELETE FROM dashboard_strategy_recent_performance_snapshots snapshot
            WHERE snapshot.strategy_id IN (
                    SELECT target.id
                    FROM tmp_follow_leader_strategy_targets target
                )
               OR snapshot.code IN (
                    SELECT target.code
                    FROM tmp_follow_leader_strategy_targets target
                );
            GET DIAGNOSTICS deleted_dashboard_recent_snapshots = ROW_COUNT;

            DELETE FROM live_orders live_order
            WHERE live_order.id IN (
                SELECT target.id
                FROM tmp_follow_leader_live_orders target
            );
            GET DIAGNOSTICS deleted_live_orders = ROW_COUNT;

            DELETE FROM strategy_market_paper_runs run
            WHERE run.id IN (
                SELECT target.id
                FROM tmp_follow_leader_runs target
            );
            GET DIAGNOSTICS deleted_strategy_runs = ROW_COUNT;

            DELETE FROM paper_fills fill
            WHERE fill.paper_order_id IN (
                SELECT target.id
                FROM tmp_follow_leader_paper_orders target
            );
            GET DIAGNOSTICS deleted_paper_fills = ROW_COUNT;

            DELETE FROM paper_orders paper_order
            WHERE paper_order.id IN (
                SELECT target.id
                FROM tmp_follow_leader_paper_orders target
            );
            GET DIAGNOSTICS deleted_paper_orders = ROW_COUNT;

            DELETE FROM paper_positions paper_position
            WHERE paper_position.copied_trader_wallet = 'strategy:follow_leader';
            GET DIAGNOSTICS deleted_paper_positions = ROW_COUNT;

            DELETE FROM paper_position_settlements settlement
            WHERE settlement.copied_trader_wallet = 'strategy:follow_leader';
            GET DIAGNOSTICS deleted_paper_position_settlements = ROW_COUNT;

            DELETE FROM strategies strategy
            WHERE strategy.id IN (
                    SELECT target.id
                    FROM tmp_follow_leader_strategy_targets target
                )
               OR strategy.code IN (
                    SELECT target.code
                    FROM tmp_follow_leader_strategy_targets target
                );
            GET DIAGNOSTICS deleted_strategies = ROW_COUNT;

            INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
            VALUES (
                migration_key_value,
                clock_timestamp(),
                'target_strategies=' || target_strategy_count::text ||
                ';paper_orders=' || deleted_paper_orders::text ||
                ';paper_fills=' || deleted_paper_fills::text ||
                ';strategy_runs=' || deleted_strategy_runs::text ||
                ';live_orders=' || deleted_live_orders::text ||
                ';shadow_decisions=' || deleted_shadow_decisions::text ||
                ';shadow_discrepancies=' || deleted_shadow_discrepancies::text ||
                ';paper_positions=' || deleted_paper_positions::text ||
                ';paper_position_settlements=' || deleted_paper_position_settlements::text ||
                ';dashboard_snapshots=' || deleted_dashboard_snapshots::text ||
                ';dashboard_recent_snapshots=' || deleted_dashboard_recent_snapshots::text ||
                ';dry_run_orders=' || deleted_dry_run_orders::text ||
                ';date_dependent_hourly_pnl=' || deleted_date_dependent_hourly_pnl::text ||
                ';diff_shift_states=' || deleted_diff_shift_states::text ||
                ';child_assignments=' || deleted_child_assignments::text ||
                ';strategies=' || deleted_strategies::text
            );
        END IF;
        END IF;
    END IF;
END $$;

DO $$
DECLARE
    migration_key_value text := '20260712_remove_eth_down_filtered_average_premarket_strategies';
    target_strategy_count integer := 0;
    deleted_shadow_discrepancies integer := 0;
    deleted_shadow_decisions integer := 0;
    deleted_live_orders integer := 0;
    deleted_strategy_runs integer := 0;
    deleted_paper_fills integer := 0;
    deleted_paper_orders integer := 0;
    deleted_signal_rejections integer := 0;
    deleted_signals integer := 0;
    deleted_paper_positions integer := 0;
    deleted_paper_position_settlements integer := 0;
    deleted_dashboard_snapshots integer := 0;
    deleted_dashboard_recent_snapshots integer := 0;
    deleted_dashboard_projection_events integer := 0;
    deleted_dashboard_projection_queue integer := 0;
    deleted_dashboard_lifetime_projection_states integer := 0;
    deleted_dashboard_position_projection_facts integer := 0;
    deleted_dashboard_recent_projection_facts integer := 0;
    deleted_dashboard_recent_projection_states integer := 0;
    deleted_dry_run_orders integer := 0;
    deleted_date_dependent_hourly_pnl integer := 0;
    deleted_diff_shift_states integer := 0;
    deleted_child_assignments integer := 0;
    deleted_strategies integer := 0;
    active_live_orders integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        DROP TABLE IF EXISTS tmp_eth_down_filtered_average_strategy_targets;
        DROP TABLE IF EXISTS tmp_eth_down_filtered_average_paper_orders;
        DROP TABLE IF EXISTS tmp_eth_down_filtered_average_live_orders;
        DROP TABLE IF EXISTS tmp_eth_down_filtered_average_runs;
        DROP TABLE IF EXISTS tmp_eth_down_filtered_average_signals;
        DROP TABLE IF EXISTS tmp_eth_down_filtered_average_positions;
        DROP TABLE IF EXISTS tmp_eth_down_filtered_average_position_settlements;

        CREATE TEMP TABLE tmp_eth_down_filtered_average_strategy_targets ON COMMIT DROP AS
        SELECT DISTINCT strategy.id, strategy.code
        FROM strategies strategy
        JOIN (
            VALUES
                ('b7c50005-0000-4000-8181-000000000101'::uuid, 'eth_up_down_5m_down_filtered_average_bps_1_fak_premarket'),
                ('b7c50005-0000-4000-8181-000000000102'::uuid, 'eth_up_down_5m_down_filtered_average_bps_2_fak_premarket'),
                ('b7c50005-0000-4000-8181-000000000103'::uuid, 'eth_up_down_5m_down_filtered_average_bps_3_fak_premarket'),
                ('b7c50005-0000-4000-8181-000000000104'::uuid, 'eth_up_down_5m_down_filtered_average_bps_4_fak_premarket'),
                ('b7c50005-0000-4000-8181-000000000105'::uuid, 'eth_up_down_5m_down_filtered_average_bps_5_fak_premarket'),
                ('b7c50005-0000-4000-8181-000000000106'::uuid, 'eth_up_down_5m_down_filtered_average_bps_6_fak_premarket'),
                ('b7c50005-0000-4000-8181-000000000107'::uuid, 'eth_up_down_5m_down_filtered_average_bps_7_fak_premarket'),
                ('b7c50005-0000-4000-8181-000000000108'::uuid, 'eth_up_down_5m_down_filtered_average_bps_8_fak_premarket'),
                ('b7c50005-0000-4000-8181-000000000109'::uuid, 'eth_up_down_5m_down_filtered_average_bps_9_fak_premarket'),
                ('b7c50005-0000-4000-8181-000000000110'::uuid, 'eth_up_down_5m_down_filtered_average_bps_10_fak_premarket')
        ) AS target(id, code)
            ON strategy.id = target.id
            OR strategy.code = target.code;

        SELECT count(*)::integer
        INTO target_strategy_count
        FROM tmp_eth_down_filtered_average_strategy_targets;

        IF target_strategy_count = 0 THEN
            INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
            VALUES (
                migration_key_value,
                clock_timestamp(),
                'target_strategies=0' ||
                ';paper_orders=0' ||
                ';paper_fills=0' ||
                ';strategy_runs=0' ||
                ';live_orders=0' ||
                ';shadow_decisions=0' ||
                ';shadow_discrepancies=0' ||
                ';signals=0' ||
                ';signal_rejections=0' ||
                ';paper_positions=0' ||
                ';paper_position_settlements=0' ||
                ';dashboard_snapshots=0' ||
                ';dashboard_recent_snapshots=0' ||
                ';dashboard_projection_events=0' ||
                ';dashboard_projection_queue=0' ||
                ';dashboard_lifetime_projection_states=0' ||
                ';dashboard_position_projection_facts=0' ||
                ';dashboard_recent_projection_facts=0' ||
                ';dashboard_recent_projection_states=0' ||
                ';dry_run_orders=0' ||
                ';date_dependent_hourly_pnl=0' ||
                ';diff_shift_states=0' ||
                ';child_assignments=0' ||
                ';strategies=0'
            );
        ELSE
            CREATE TEMP TABLE tmp_eth_down_filtered_average_paper_orders ON COMMIT DROP AS
            SELECT paper_order.id, paper_order.signal_id
            FROM paper_orders paper_order
            WHERE paper_order.strategy_id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_strategy_targets target
                )
               OR paper_order.copied_trader_wallet IN (
                    SELECT 'strategy:' || target.code
                    FROM tmp_eth_down_filtered_average_strategy_targets target
                );

            CREATE TEMP TABLE tmp_eth_down_filtered_average_live_orders ON COMMIT DROP AS
            SELECT live_order.id, live_order.signal_id
            FROM live_orders live_order
            WHERE live_order.strategy_id IN (
                SELECT target.id
                FROM tmp_eth_down_filtered_average_strategy_targets target
            );

            CREATE TEMP TABLE tmp_eth_down_filtered_average_runs ON COMMIT DROP AS
            SELECT run.id, run.paper_order_id, run.signal_id
            FROM strategy_market_paper_runs run
            WHERE run.strategy_id IN (
                SELECT target.id
                FROM tmp_eth_down_filtered_average_strategy_targets target
            );

            CREATE TEMP TABLE tmp_eth_down_filtered_average_signals ON COMMIT DROP AS
            SELECT DISTINCT signal.id
            FROM signals signal
            WHERE signal.id IN (
                    SELECT target.signal_id
                    FROM tmp_eth_down_filtered_average_paper_orders target
                )
               OR signal.id IN (
                    SELECT target.signal_id
                    FROM tmp_eth_down_filtered_average_live_orders target
                )
               OR signal.id IN (
                    SELECT target.signal_id
                    FROM tmp_eth_down_filtered_average_runs target
                )
               OR signal.trader_wallet IN (
                    SELECT 'strategy:' || target.code
                    FROM tmp_eth_down_filtered_average_strategy_targets target
                );

            CREATE TEMP TABLE tmp_eth_down_filtered_average_positions ON COMMIT DROP AS
            SELECT paper_position.id
            FROM paper_positions paper_position
            WHERE paper_position.copied_trader_wallet IN (
                SELECT 'strategy:' || target.code
                FROM tmp_eth_down_filtered_average_strategy_targets target
            );

            CREATE TEMP TABLE tmp_eth_down_filtered_average_position_settlements ON COMMIT DROP AS
            SELECT settlement.id
            FROM paper_position_settlements settlement
            WHERE settlement.copied_trader_wallet IN (
                SELECT 'strategy:' || target.code
                FROM tmp_eth_down_filtered_average_strategy_targets target
            );

            SELECT count(*)::integer
            INTO active_live_orders
            FROM live_orders live_order
            WHERE live_order.id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_live_orders target
                )
              AND live_order.status NOT IN (
                    'Cancelled',
                    'Rejected',
                    'Failed',
                    'Expired',
                    'Filled',
                    'Settled',
                    'Ignored',
                    'PreflightRejected'
                );

            IF active_live_orders > 0 THEN
                RAISE EXCEPTION 'Refusing to delete ETH Down Filtered Average Premarket strategies because % live orders are still active.', active_live_orders;
            ELSE
                IF to_regclass('public.dashboard_projection_events') IS NOT NULL THEN
                    DELETE FROM dashboard_projection_events event
                    WHERE event.strategy_id IN (
                            SELECT target.id
                            FROM tmp_eth_down_filtered_average_strategy_targets target
                        )
                       OR event.source_id IN (
                            SELECT target.id
                            FROM tmp_eth_down_filtered_average_strategy_targets target
                        )
                       OR event.source_id IN (
                            SELECT target.id
                            FROM tmp_eth_down_filtered_average_paper_orders target
                        )
                       OR event.source_id IN (
                            SELECT target.id
                            FROM tmp_eth_down_filtered_average_live_orders target
                        )
                       OR event.source_id IN (
                            SELECT target.id
                            FROM tmp_eth_down_filtered_average_positions target
                        )
                       OR event.source_id IN (
                            SELECT target.id
                            FROM tmp_eth_down_filtered_average_position_settlements target
                        );
                    GET DIAGNOSTICS deleted_dashboard_projection_events = ROW_COUNT;
                END IF;

                IF to_regclass('public.dashboard_projection_reconciliation_queue') IS NOT NULL THEN
                    DELETE FROM dashboard_projection_reconciliation_queue queue
                    WHERE queue.strategy_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    );
                    GET DIAGNOSTICS deleted_dashboard_projection_queue = ROW_COUNT;
                END IF;

                IF to_regclass('public.dashboard_strategy_lifetime_projection_states') IS NOT NULL THEN
                    DELETE FROM dashboard_strategy_lifetime_projection_states state
                    WHERE state.strategy_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    );
                    GET DIAGNOSTICS deleted_dashboard_lifetime_projection_states = ROW_COUNT;
                END IF;

                IF to_regclass('public.dashboard_strategy_position_projection_facts') IS NOT NULL THEN
                    DELETE FROM dashboard_strategy_position_projection_facts fact
                    WHERE fact.strategy_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    );
                    GET DIAGNOSTICS deleted_dashboard_position_projection_facts = ROW_COUNT;
                END IF;

                IF to_regclass('public.dashboard_strategy_recent_projection_facts') IS NOT NULL THEN
                    DELETE FROM dashboard_strategy_recent_projection_facts fact
                    WHERE fact.strategy_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    );
                    GET DIAGNOSTICS deleted_dashboard_recent_projection_facts = ROW_COUNT;
                END IF;

                IF to_regclass('public.dashboard_strategy_recent_projection_states') IS NOT NULL THEN
                    DELETE FROM dashboard_strategy_recent_projection_states state
                    WHERE state.strategy_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    );
                    GET DIAGNOSTICS deleted_dashboard_recent_projection_states = ROW_COUNT;
                END IF;

                DELETE FROM paper_live_shadow_discrepancies discrepancy
                WHERE discrepancy.strategy_id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_strategy_targets target
                );
                GET DIAGNOSTICS deleted_shadow_discrepancies = ROW_COUNT;

                DELETE FROM paper_live_shadow_decisions decision
                WHERE decision.strategy_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    )
                   OR decision.paper_order_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_paper_orders target
                    )
                   OR decision.live_order_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_live_orders target
                    )
                   OR decision.signal_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_signals target
                    );
                GET DIAGNOSTICS deleted_shadow_decisions = ROW_COUNT;

                DELETE FROM dry_run_orders dry_run_order
                WHERE dry_run_order.strategy_id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_strategy_targets target
                );
                GET DIAGNOSTICS deleted_dry_run_orders = ROW_COUNT;

                DELETE FROM date_dependent_strategy_hourly_paper_pnl hourly_pnl
                WHERE hourly_pnl.strategy_id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_strategy_targets target
                );
                GET DIAGNOSTICS deleted_date_dependent_hourly_pnl = ROW_COUNT;

                DELETE FROM crypto_up_down_5m_diff_shift_progress_states state
                WHERE state.strategy_id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_strategy_targets target
                );
                GET DIAGNOSTICS deleted_diff_shift_states = ROW_COUNT;

                DELETE FROM strategy_child_parent_assignments assignment
                WHERE assignment.child_strategy_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    )
                   OR assignment.parent_strategy_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    );
                GET DIAGNOSTICS deleted_child_assignments = ROW_COUNT;

                DELETE FROM dashboard_strategy_performance_snapshots snapshot
                WHERE snapshot.strategy_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    )
                   OR snapshot.code IN (
                        SELECT target.code
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    );
                GET DIAGNOSTICS deleted_dashboard_snapshots = ROW_COUNT;

                DELETE FROM dashboard_strategy_recent_performance_snapshots snapshot
                WHERE snapshot.strategy_id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    )
                   OR snapshot.code IN (
                        SELECT target.code
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    );
                GET DIAGNOSTICS deleted_dashboard_recent_snapshots = ROW_COUNT;

                DELETE FROM live_orders live_order
                WHERE live_order.id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_live_orders target
                );
                GET DIAGNOSTICS deleted_live_orders = ROW_COUNT;

                DELETE FROM strategy_market_paper_runs run
                WHERE run.id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_runs target
                );
                GET DIAGNOSTICS deleted_strategy_runs = ROW_COUNT;

                DELETE FROM paper_fills fill
                WHERE fill.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_paper_orders target
                );
                GET DIAGNOSTICS deleted_paper_fills = ROW_COUNT;

                DELETE FROM paper_orders paper_order
                WHERE paper_order.id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_paper_orders target
                );
                GET DIAGNOSTICS deleted_paper_orders = ROW_COUNT;

                DELETE FROM signal_rejections rejection
                WHERE rejection.signal_id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_signals target
                );
                GET DIAGNOSTICS deleted_signal_rejections = ROW_COUNT;

                DELETE FROM signals signal
                WHERE signal.id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_signals target
                );
                GET DIAGNOSTICS deleted_signals = ROW_COUNT;

                DELETE FROM paper_positions paper_position
                WHERE paper_position.id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_positions target
                );
                GET DIAGNOSTICS deleted_paper_positions = ROW_COUNT;

                DELETE FROM paper_position_settlements settlement
                WHERE settlement.id IN (
                    SELECT target.id
                    FROM tmp_eth_down_filtered_average_position_settlements target
                );
                GET DIAGNOSTICS deleted_paper_position_settlements = ROW_COUNT;

                DELETE FROM strategies strategy
                WHERE strategy.id IN (
                        SELECT target.id
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    )
                   OR strategy.code IN (
                        SELECT target.code
                        FROM tmp_eth_down_filtered_average_strategy_targets target
                    );
                GET DIAGNOSTICS deleted_strategies = ROW_COUNT;

                INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
                VALUES (
                    migration_key_value,
                    clock_timestamp(),
                    'target_strategies=' || target_strategy_count::text ||
                    ';paper_orders=' || deleted_paper_orders::text ||
                    ';paper_fills=' || deleted_paper_fills::text ||
                    ';strategy_runs=' || deleted_strategy_runs::text ||
                    ';live_orders=' || deleted_live_orders::text ||
                    ';shadow_decisions=' || deleted_shadow_decisions::text ||
                    ';shadow_discrepancies=' || deleted_shadow_discrepancies::text ||
                    ';signals=' || deleted_signals::text ||
                    ';signal_rejections=' || deleted_signal_rejections::text ||
                    ';paper_positions=' || deleted_paper_positions::text ||
                    ';paper_position_settlements=' || deleted_paper_position_settlements::text ||
                    ';dashboard_snapshots=' || deleted_dashboard_snapshots::text ||
                    ';dashboard_recent_snapshots=' || deleted_dashboard_recent_snapshots::text ||
                    ';dashboard_projection_events=' || deleted_dashboard_projection_events::text ||
                    ';dashboard_projection_queue=' || deleted_dashboard_projection_queue::text ||
                    ';dashboard_lifetime_projection_states=' || deleted_dashboard_lifetime_projection_states::text ||
                    ';dashboard_position_projection_facts=' || deleted_dashboard_position_projection_facts::text ||
                    ';dashboard_recent_projection_facts=' || deleted_dashboard_recent_projection_facts::text ||
                    ';dashboard_recent_projection_states=' || deleted_dashboard_recent_projection_states::text ||
                    ';dry_run_orders=' || deleted_dry_run_orders::text ||
                    ';date_dependent_hourly_pnl=' || deleted_date_dependent_hourly_pnl::text ||
                    ';diff_shift_states=' || deleted_diff_shift_states::text ||
                    ';child_assignments=' || deleted_child_assignments::text ||
                    ';strategies=' || deleted_strategies::text
                );
            END IF;
        END IF;
    END IF;
END $$;

DO $$
DECLARE
    migration_key_value text := '20260713_remove_hopeless_progress_strategies';
    allowlist_count integer := 0;
    target_strategy_count integer := 0;
    active_live_orders integer := 0;
    deleted_strategies integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        DROP TABLE IF EXISTS tmp_hopeless_progress_allowlist;
        DROP TABLE IF EXISTS tmp_hopeless_progress_targets;
        DROP TABLE IF EXISTS tmp_hopeless_progress_wallets;
        DROP TABLE IF EXISTS tmp_hopeless_progress_paper_orders;
        DROP TABLE IF EXISTS tmp_hopeless_progress_live_orders;
        DROP TABLE IF EXISTS tmp_hopeless_progress_runs;
        DROP TABLE IF EXISTS tmp_hopeless_progress_signals;
        DROP TABLE IF EXISTS tmp_hopeless_progress_positions;
        DROP TABLE IF EXISTS tmp_hopeless_progress_settlements;

        CREATE TEMP TABLE tmp_hopeless_progress_allowlist (
            id uuid PRIMARY KEY,
            code text UNIQUE NOT NULL
        ) ON COMMIT DROP;

        INSERT INTO tmp_hopeless_progress_allowlist (id, code)
        SELECT
            ('b7c50005-0000-4000-8169-' || lpad(value::text, 12, '0'))::uuid,
            'btc_up_down_5m_' || value::text || '_diff_limit_progress_premarket'
        FROM generate_series(1, 5) AS selected(value)
        UNION ALL
        SELECT
            ('b7c50005-0000-4000-8166-' || lpad(value::text, 12, '0'))::uuid,
            'btc_up_down_5m_' || value::text || '_diff_shift_progress_premarket'
        FROM unnest(ARRAY[1, 2, 4, 5]) AS selected(value)
        UNION ALL
        SELECT
            ('b7c50005-0000-4000-8189-' || lpad(value::text, 12, '0'))::uuid,
            'eth_up_down_5m_' || value::text || '_child_progress'
        FROM unnest(ARRAY[1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 13, 14, 19, 21, 24]) AS selected(value)
        UNION ALL
        SELECT
            ('b7c50005-0000-4000-8198-' || lpad(value::text, 12, '0'))::uuid,
            'eth_up_down_5m_' || value::text || '_child_progress_roi'
        FROM unnest(ARRAY[3, 5, 7, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24]) AS selected(value)
        UNION ALL
        SELECT
            'b7c50005-0000-4000-8167-000000000004'::uuid,
            'eth_up_down_5m_4_diff_shift_progress_premarket'
        UNION ALL
        SELECT
            ('b7c50005-0000-4000-8156-' || lpad(value::text, 12, '0'))::uuid,
            'eth_up_down_5m_diff_' || value::text || '_up_progress'
        FROM unnest(ARRAY[1, 2, 13, 14, 15, 16]) AS selected(value)
        UNION ALL
        SELECT
            ('b7c50005-0000-4000-8199-' || lpad(value::text, 12, '0'))::uuid,
            'sol_up_down_5m_' || value::text || '_child_progress_roi'
        FROM unnest(ARRAY[4, 5, 6, 13, 14, 19, 21, 23]) AS selected(value);

        SELECT count(*)::integer
        INTO allowlist_count
        FROM tmp_hopeless_progress_allowlist;

        IF allowlist_count <> 57 THEN
            RAISE EXCEPTION 'Refusing hopeless Progress cleanup because allowlist contains % rows instead of 57.', allowlist_count;
        END IF;

        IF EXISTS (
            SELECT 1
            FROM strategies strategy
            JOIN tmp_hopeless_progress_allowlist target
                ON strategy.id = target.id OR strategy.code = target.code
            WHERE strategy.id <> target.id OR strategy.code <> target.code
        ) THEN
            RAISE EXCEPTION 'Refusing hopeless Progress cleanup because a strategy id/code collision was found.';
        END IF;

        CREATE TEMP TABLE tmp_hopeless_progress_targets ON COMMIT DROP AS
        SELECT strategy.id, strategy.code
        FROM strategies strategy
        JOIN tmp_hopeless_progress_allowlist target
            ON strategy.id = target.id AND strategy.code = target.code;

        SELECT count(*)::integer
        INTO target_strategy_count
        FROM tmp_hopeless_progress_targets;

        IF target_strategy_count > 0 THEN
            UPDATE strategies strategy
            SET enabled = false,
                live_stakes = false,
                auto_live_paused = false,
                auto_live_paused_at_utc = NULL,
                auto_live_pause_window_start_utc = NULL,
                live_enabled_at_utc = NULL,
                updated_at_utc = clock_timestamp()
            WHERE strategy.id IN (
                SELECT target.id
                FROM tmp_hopeless_progress_targets target
            );

            CREATE TEMP TABLE tmp_hopeless_progress_wallets ON COMMIT DROP AS
            SELECT 'strategy:' || target.code AS wallet
            FROM tmp_hopeless_progress_targets target;

            CREATE TEMP TABLE tmp_hopeless_progress_paper_orders ON COMMIT DROP AS
            SELECT paper_order.id, paper_order.signal_id, paper_order.correlation_id
            FROM paper_orders paper_order
            WHERE paper_order.strategy_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                )
               OR paper_order.copied_trader_wallet IN (
                    SELECT target.wallet
                    FROM tmp_hopeless_progress_wallets target
                );

            CREATE TEMP TABLE tmp_hopeless_progress_live_orders ON COMMIT DROP AS
            SELECT live_order.id, live_order.signal_id, live_order.paper_order_id, live_order.correlation_id
            FROM live_orders live_order
            WHERE live_order.strategy_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                )
               OR live_order.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_paper_orders target
                );

            CREATE TEMP TABLE tmp_hopeless_progress_runs ON COMMIT DROP AS
            SELECT run.id, run.paper_order_id, run.signal_id
            FROM strategy_market_paper_runs run
            WHERE run.strategy_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                )
               OR run.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_paper_orders target
                );

            CREATE TEMP TABLE tmp_hopeless_progress_signals ON COMMIT DROP AS
            SELECT DISTINCT signal.id
            FROM signals signal
            WHERE signal.trader_wallet IN (
                    SELECT target.wallet
                    FROM tmp_hopeless_progress_wallets target
                )
               OR signal.id IN (
                    SELECT target.signal_id
                    FROM tmp_hopeless_progress_paper_orders target
                    WHERE target.signal_id IS NOT NULL
                )
               OR signal.id IN (
                    SELECT target.signal_id
                    FROM tmp_hopeless_progress_live_orders target
                    WHERE target.signal_id IS NOT NULL
                )
               OR signal.id IN (
                    SELECT target.signal_id
                    FROM tmp_hopeless_progress_runs target
                    WHERE target.signal_id IS NOT NULL
                );

            CREATE TEMP TABLE tmp_hopeless_progress_positions ON COMMIT DROP AS
            SELECT position.id
            FROM paper_positions position
            WHERE position.copied_trader_wallet IN (
                SELECT target.wallet
                FROM tmp_hopeless_progress_wallets target
            );

            CREATE TEMP TABLE tmp_hopeless_progress_settlements ON COMMIT DROP AS
            SELECT settlement.id
            FROM paper_position_settlements settlement
            WHERE settlement.copied_trader_wallet IN (
                SELECT target.wallet
                FROM tmp_hopeless_progress_wallets target
            );

            SELECT count(*)::integer
            INTO active_live_orders
            FROM live_orders live_order
            WHERE live_order.id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_live_orders target
                )
              AND live_order.settled_at_utc IS NULL
              AND (
                    lower(live_order.status) IN (
                        'created', 'queued', 'validated', 'submitted', 'open', 'live',
                        'unmatched', 'partiallymatched', 'pending', 'cancelrequested'
                    )
                    OR lower(live_order.cancel_status) IN ('requested', 'pending')
                    OR (
                        live_order.remaining_size > 0
                        AND lower(live_order.status) NOT IN (
                            'matched', 'rejected', 'preflightrejected', 'cancelled', 'cancelfailed'
                        )
                    )
                );

            IF active_live_orders > 0 THEN
                RAISE EXCEPTION 'Refusing hopeless Progress cleanup because % active Live orders still exist.', active_live_orders;
            END IF;

            IF to_regclass('public.dashboard_projection_events') IS NOT NULL THEN
                DELETE FROM dashboard_projection_events event
                WHERE event.strategy_id IN (
                        SELECT target.id
                        FROM tmp_hopeless_progress_targets target
                    )
                   OR event.source_id IN (
                        SELECT target.id
                        FROM tmp_hopeless_progress_paper_orders target
                    )
                   OR event.source_id IN (
                        SELECT target.id
                        FROM tmp_hopeless_progress_live_orders target
                    )
                   OR event.source_id IN (
                        SELECT target.id
                        FROM tmp_hopeless_progress_positions target
                    )
                   OR event.source_id IN (
                        SELECT target.id
                        FROM tmp_hopeless_progress_settlements target
                    );
            END IF;

            IF to_regclass('public.dashboard_projection_reconciliation_queue') IS NOT NULL THEN
                DELETE FROM dashboard_projection_reconciliation_queue queue
                WHERE queue.strategy_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                );
            END IF;

            IF to_regclass('public.dashboard_projection_control') IS NOT NULL THEN
                UPDATE dashboard_projection_control control
                SET reconciliation_cursor_strategy_id = NULL
                WHERE control.reconciliation_cursor_strategy_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                );
            END IF;

            DELETE FROM paper_live_shadow_discrepancies discrepancy
            WHERE discrepancy.strategy_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                )
               OR discrepancy.correlation_id IN (
                    SELECT target.correlation_id
                    FROM tmp_hopeless_progress_paper_orders target
                    WHERE target.correlation_id IS NOT NULL
                )
               OR discrepancy.correlation_id IN (
                    SELECT target.correlation_id
                    FROM tmp_hopeless_progress_live_orders target
                    WHERE target.correlation_id IS NOT NULL
                );

            DELETE FROM paper_live_shadow_decisions decision
            WHERE decision.strategy_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                )
               OR decision.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_paper_orders target
                )
               OR decision.live_order_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_live_orders target
                )
               OR decision.signal_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_signals target
                );

            DELETE FROM polymarket_onchain_paper_signal_results result
            WHERE result.copied_trader_wallet IN (
                    SELECT target.wallet
                    FROM tmp_hopeless_progress_wallets target
                )
               OR result.paper_order_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_paper_orders target
                )
               OR result.signal_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_signals target
                );

            DELETE FROM dry_run_orders dry_run_order
            WHERE dry_run_order.strategy_id IN (
                SELECT target.id
                FROM tmp_hopeless_progress_targets target
            );

            DELETE FROM date_dependent_strategy_hourly_paper_pnl hourly_pnl
            WHERE hourly_pnl.strategy_id IN (
                SELECT target.id
                FROM tmp_hopeless_progress_targets target
            );

            DELETE FROM crypto_up_down_5m_diff_shift_progress_states state
            WHERE state.strategy_id IN (
                SELECT target.id
                FROM tmp_hopeless_progress_targets target
            );

            DELETE FROM strategy_child_parent_assignments assignment
            WHERE assignment.child_strategy_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                )
               OR assignment.parent_strategy_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                );

            DELETE FROM dashboard_strategy_performance_snapshots snapshot
            WHERE snapshot.strategy_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                )
               OR snapshot.code IN (
                    SELECT target.code
                    FROM tmp_hopeless_progress_targets target
                );

            DELETE FROM dashboard_strategy_recent_performance_snapshots snapshot
            WHERE snapshot.strategy_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                )
               OR snapshot.code IN (
                    SELECT target.code
                    FROM tmp_hopeless_progress_targets target
                );

            DELETE FROM paper_copied_leader_activity_events activity
            WHERE activity.copied_trader_wallet IN (
                SELECT target.wallet
                FROM tmp_hopeless_progress_wallets target
            );

            DELETE FROM paper_copied_leader_positions copied_position
            WHERE copied_position.copied_trader_wallet IN (
                SELECT target.wallet
                FROM tmp_hopeless_progress_wallets target
            );

            DELETE FROM paper_copied_trader_performance performance
            WHERE performance.copied_trader_wallet IN (
                SELECT target.wallet
                FROM tmp_hopeless_progress_wallets target
            );

            DELETE FROM live_orders live_order
            WHERE live_order.id IN (
                SELECT target.id
                FROM tmp_hopeless_progress_live_orders target
            );

            DELETE FROM strategy_market_paper_runs run
            WHERE run.id IN (
                SELECT target.id
                FROM tmp_hopeless_progress_runs target
            );

            DELETE FROM paper_fills fill
            WHERE fill.paper_order_id IN (
                SELECT target.id
                FROM tmp_hopeless_progress_paper_orders target
            );

            DELETE FROM paper_position_settlements settlement
            WHERE settlement.id IN (
                SELECT target.id
                FROM tmp_hopeless_progress_settlements target
            );

            DELETE FROM paper_positions position
            WHERE position.id IN (
                SELECT target.id
                FROM tmp_hopeless_progress_positions target
            );

            DELETE FROM paper_orders paper_order
            WHERE paper_order.id IN (
                SELECT target.id
                FROM tmp_hopeless_progress_paper_orders target
            );

            DELETE FROM signal_rejections rejection
            WHERE rejection.signal_id IN (
                SELECT target.id
                FROM tmp_hopeless_progress_signals target
            );

            DELETE FROM signals signal
            WHERE signal.id IN (
                SELECT target.id
                FROM tmp_hopeless_progress_signals target
            );

            DELETE FROM strategies strategy
            WHERE strategy.id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                )
               OR strategy.code IN (
                    SELECT target.code
                    FROM tmp_hopeless_progress_targets target
                );
            GET DIAGNOSTICS deleted_strategies = ROW_COUNT;

            IF to_regclass('public.dashboard_projection_events') IS NOT NULL THEN
                DELETE FROM dashboard_projection_events event
                WHERE event.strategy_id IN (
                    SELECT target.id
                    FROM tmp_hopeless_progress_targets target
                );
            END IF;
        END IF;

        INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
        VALUES (
            migration_key_value,
            clock_timestamp(),
            'allowlist=57' ||
            ';target_strategies=' || target_strategy_count::text ||
            ';active_live_orders=' || active_live_orders::text ||
            ';deleted_strategies=' || deleted_strategies::text
        );
    END IF;
END $$;

DO $$
DECLARE
    migration_key_value text := '20260522_retire_middle_depth_2_5';
    retired_strategy_count integer := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_data_migrations migration
        WHERE migration.migration_key = migration_key_value
    ) THEN
        UPDATE strategies strategy
        SET enabled = false,
            live_stakes = false,
            auto_live_paused = false,
            auto_live_paused_at_utc = NULL,
            auto_live_pause_window_start_utc = NULL,
            live_enabled_at_utc = NULL,
            updated_at_utc = clock_timestamp()
        WHERE (
                strategy.code IN (
                    'btc_up_down_5m_middle_2',
                    'btc_up_down_5m_middle_3',
                    'btc_up_down_5m_middle_4',
                    'btc_up_down_5m_middle_5',
                    'btc_up_down_5m_middle_2_revert',
                    'btc_up_down_5m_middle_3_revert',
                    'btc_up_down_5m_middle_4_revert',
                    'btc_up_down_5m_middle_5_revert'
                )
                OR strategy.code LIKE 'btc_up_down_5m_middle_2_bps_%'
                OR strategy.code LIKE 'btc_up_down_5m_middle_3_bps_%'
                OR strategy.code LIKE 'btc_up_down_5m_middle_4_bps_%'
                OR strategy.code LIKE 'btc_up_down_5m_middle_5_bps_%'
                OR strategy.code LIKE 'btc_up_down_5m_middle_2_revert_bps_%'
                OR strategy.code LIKE 'btc_up_down_5m_middle_3_revert_bps_%'
                OR strategy.code LIKE 'btc_up_down_5m_middle_4_revert_bps_%'
                OR strategy.code LIKE 'btc_up_down_5m_middle_5_revert_bps_%'
            )
          AND (strategy.enabled OR strategy.live_stakes OR strategy.auto_live_paused);
        GET DIAGNOSTICS retired_strategy_count = ROW_COUNT;

        INSERT INTO schema_data_migrations (migration_key, applied_at_utc, details)
        VALUES (
            migration_key_value,
            clock_timestamp(),
            'retired_strategies=' || retired_strategy_count::text
        );
    END IF;
END $$;
""";

    private static string BuildLowerEnterPremarketStrategySeedSql()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.LowerEnterSourceStrategyId is not null)
            .ToArray();
        var sql = new StringBuilder(variants.Length * 640);
        sql.AppendLine("""
INSERT INTO strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    paused,
    paused_until_utc,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    live_enabled_at_utc,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    created_at_utc,
    updated_at_utc)
VALUES
""");

        for (var index = 0; index < variants.Length; index++)
        {
            var variant = variants[index];
            sql.Append("    (")
                .Append(ToSqlLiteral(variant.Id.ToString("D")))
                .Append(", ")
                .Append(ToSqlLiteral(variant.Code))
                .Append(", ")
                .Append(ToSqlLiteral(variant.Name))
                .Append(", ")
                .Append(ToSqlLiteral(variant.Description))
                .Append(", true, false, 1.00, 1.00, 100.00, false, NULL, false, NULL, NULL, NULL, 1.00, 1.00, 0, 0, now(), now())")
                .AppendLine(index == variants.Length - 1 ? string.Empty : ",");
        }

        sql.AppendLine("""
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;
""");
        return sql.ToString();
    }

    private static string ToSqlLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    public static string SchemaSql { get; } = string.Concat(
        BaseSchemaSql,
        Environment.NewLine,
        BuildLowerEnterPremarketStrategySeedSql(),
        Environment.NewLine,
        DashboardProjectionSchema.SchemaSql,
        Environment.NewLine,
        PaperCopiedTraderPerformanceProjectionSchema.SchemaSql);
}
