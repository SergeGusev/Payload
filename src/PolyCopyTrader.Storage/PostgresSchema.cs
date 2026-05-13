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
        "paper_orders",
        "paper_fills",
        "strategy_market_paper_runs",
        "paper_positions",
        "paper_position_settlements",
        "paper_copied_trader_performance",
        "btc_usd_reference_correlation_samples",
        "btc_order_book_lag_diagnostic_events",
        "btc_up_down_5m_odds_ticks",
        "crypto_up_down_5m_odds_ticks",
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

    public const string SchemaSql = """
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
    paper_stake_amount numeric(28,8) NOT NULL DEFAULT 1.00,
    live_stake_amount numeric(28,8) NOT NULL DEFAULT 1.00,
    live_available_balance numeric(28,8) NOT NULL DEFAULT 100.00,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_strategies_paper_stake_amount_positive CHECK (paper_stake_amount > 0),
    CONSTRAINT ck_strategies_live_stake_amount_positive CHECK (live_stake_amount > 0),
    CONSTRAINT ck_strategies_live_available_balance_nonnegative CHECK (live_available_balance >= 0)
);

ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_stakes boolean NOT NULL DEFAULT false;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paper_stake_amount numeric(28,8) NOT NULL DEFAULT 1.00;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_stake_amount numeric(28,8) NOT NULL DEFAULT 1.00;
ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_available_balance numeric(28,8) NOT NULL DEFAULT 100.00;

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
        WHERE conname = 'ck_strategies_live_available_balance_nonnegative'
          AND conrelid = 'public.strategies'::regclass
    ) THEN
        ALTER TABLE strategies
            ADD CONSTRAINT ck_strategies_live_available_balance_nonnegative CHECK (live_available_balance >= 0);
    END IF;
END $$;

INSERT INTO strategies (id, code, name, description, enabled, created_at_utc, updated_at_utc)
VALUES (
    'f0110a0d-1ead-4c00-8b01-000000000001',
    'follow_leader',
    'Follow leader',
    'Follow accepted signals from selected leader traders.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8001-000000000030',
    'btc_up_down_5m_less_30',
    'BTC Up or Down 5m Less 30',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 30 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8001-000000000060',
    'btc_up_down_5m_less_60',
    'BTC Up or Down 5m Less 60',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 60 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8021-000000060020',
    'btc_up_down_5m_less_60_below_20',
    'BTC Up or Down 5m Less 60 Below 20',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 60 seconds after window start using a GTD limit BUY at 0.20.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8001-000000000090',
    'btc_up_down_5m_less_90',
    'BTC Up or Down 5m Less 90',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 90 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8021-000000090020',
    'btc_up_down_5m_less_90_below_20',
    'BTC Up or Down 5m Less 90 Below 20',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 90 seconds after window start using a GTD limit BUY at 0.20.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8001-000000000120',
    'btc_up_down_5m_less_120',
    'BTC Up or Down 5m Less 120',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 120 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8021-000000120020',
    'btc_up_down_5m_less_120_below_20',
    'BTC Up or Down 5m Less 120 Below 20',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 120 seconds after window start using a GTD limit BUY at 0.20.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8021-000000120030',
    'btc_up_down_5m_less_120_below_30',
    'BTC Up or Down 5m Less 120 Below 30',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 120 seconds after window start using a GTD limit BUY at 0.30.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8001-000000000150',
    'btc_up_down_5m_less_150',
    'BTC Up or Down 5m Less 150',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 150 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8001-000000000180',
    'btc_up_down_5m_less_180',
    'BTC Up or Down 5m Less 180',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 180 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8003-000000000180',
    'btc_up_down_5m_less_180_martin',
    'BTC Less 180 Martin',
    'After BTC Less 180 loses three times in a row, bet on the lower-priced BTC 5m outcome 180 seconds after window start using the configured Paper stake multiplier progression until this strategy wins.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8001-000000000210',
    'btc_up_down_5m_less_210',
    'BTC Up or Down 5m Less 210',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 210 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8001-000000000240',
    'btc_up_down_5m_less_240',
    'BTC Up or Down 5m Less 240',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 240 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8001-000000000270',
    'btc_up_down_5m_less_270',
    'BTC Up or Down 5m Less 270',
    'Bet the configured Paper stake multiplier on the lower-priced BTC 5m outcome 270 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8002-000000000030',
    'btc_up_down_5m_more_30',
    'BTC Up or Down 5m More 30',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 30 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8020-000000030055',
    'btc_up_down_5m_more_30_below_55',
    'BTC Up or Down 5m More 30 Below 55',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 30 seconds after window start using a GTD limit BUY at 0.55.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8002-000000000060',
    'btc_up_down_5m_more_60',
    'BTC Up or Down 5m More 60',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 60 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8019-000000000060',
    'btc_up_down_5m_more_60_below_60',
    'BTC Up or Down 5m More 60 Below 60',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 60 seconds after window start using a GTD limit BUY at 0.60.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8019-000000000055',
    'btc_up_down_5m_more_60_below_55',
    'BTC Up or Down 5m More 60 Below 55',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 60 seconds after window start using a GTD limit BUY at 0.55.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8002-000000000090',
    'btc_up_down_5m_more_90',
    'BTC Up or Down 5m More 90',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 90 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8012-000000000070',
    'btc_up_down_5m_more_90_below_70',
    'BTC Up or Down 5m More 90 Below 70',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 90 seconds after window start using a GTD limit BUY at 0.70.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8012-000000000065',
    'btc_up_down_5m_more_90_below_65',
    'BTC Up or Down 5m More 90 Below 65',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 90 seconds after window start using a GTD limit BUY at 0.65.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8012-000000000060',
    'btc_up_down_5m_more_90_below_60',
    'BTC Up or Down 5m More 90 Below 60',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 90 seconds after window start using a GTD limit BUY at 0.60.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8012-000000000055',
    'btc_up_down_5m_more_90_below_55',
    'BTC Up or Down 5m More 90 Below 55',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 90 seconds after window start using a GTD limit BUY at 0.55.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8002-000000000120',
    'btc_up_down_5m_more_120',
    'BTC Up or Down 5m More 120',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 120 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8020-000000120070',
    'btc_up_down_5m_more_120_below_70',
    'BTC Up or Down 5m More 120 Below 70',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 120 seconds after window start using a GTD limit BUY at 0.70.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8002-000000000150',
    'btc_up_down_5m_more_150',
    'BTC Up or Down 5m More 150',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 150 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8020-000000150065',
    'btc_up_down_5m_more_150_below_65',
    'BTC Up or Down 5m More 150 Below 65',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 150 seconds after window start using a GTD limit BUY at 0.65.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8002-000000000180',
    'btc_up_down_5m_more_180',
    'BTC Up or Down 5m More 180',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 180 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8002-000000000210',
    'btc_up_down_5m_more_210',
    'BTC Up or Down 5m More 210',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 210 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8002-000000000240',
    'btc_up_down_5m_more_240',
    'BTC Up or Down 5m More 240',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 240 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8002-000000000270',
    'btc_up_down_5m_more_270',
    'BTC Up or Down 5m More 270',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 270 seconds after window start.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8020-000000270065',
    'btc_up_down_5m_more_270_below_65',
    'BTC Up or Down 5m More 270 Below 65',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 270 seconds after window start using a GTD limit BUY at 0.65.',
    true,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8020-000000270060',
    'btc_up_down_5m_more_270_below_60',
    'BTC Up or Down 5m More 270 Below 60',
    'Bet the configured Paper stake multiplier on the higher-priced BTC 5m outcome 270 seconds after window start using a GTD limit BUY at 0.60.',
    true,
    now(),
    now()
)
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, paper_stake_amount, created_at_utc, updated_at_utc)
VALUES (
    'b7c50005-0000-4000-8004-000000000030',
    'btc_up_down_5m_less_30_gamma',
    'BTC Up or Down 5m Less 30 Gamma',
    'Experimental comparison strategy: choose the lower-priced BTC 5m outcome from Gamma outcomePrices 30 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8004-000000000060',
    'btc_up_down_5m_less_60_gamma',
    'BTC Up or Down 5m Less 60 Gamma',
    'Experimental comparison strategy: choose the lower-priced BTC 5m outcome from Gamma outcomePrices 60 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8004-000000000090',
    'btc_up_down_5m_less_90_gamma',
    'BTC Up or Down 5m Less 90 Gamma',
    'Experimental comparison strategy: choose the lower-priced BTC 5m outcome from Gamma outcomePrices 90 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8004-000000000120',
    'btc_up_down_5m_less_120_gamma',
    'BTC Up or Down 5m Less 120 Gamma',
    'Experimental comparison strategy: choose the lower-priced BTC 5m outcome from Gamma outcomePrices 120 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8004-000000000150',
    'btc_up_down_5m_less_150_gamma',
    'BTC Up or Down 5m Less 150 Gamma',
    'Experimental comparison strategy: choose the lower-priced BTC 5m outcome from Gamma outcomePrices 150 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8004-000000000180',
    'btc_up_down_5m_less_180_gamma',
    'BTC Up or Down 5m Less 180 Gamma',
    'Experimental comparison strategy: choose the lower-priced BTC 5m outcome from Gamma outcomePrices 180 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8004-000000000210',
    'btc_up_down_5m_less_210_gamma',
    'BTC Up or Down 5m Less 210 Gamma',
    'Experimental comparison strategy: choose the lower-priced BTC 5m outcome from Gamma outcomePrices 210 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8004-000000000240',
    'btc_up_down_5m_less_240_gamma',
    'BTC Up or Down 5m Less 240 Gamma',
    'Experimental comparison strategy: choose the lower-priced BTC 5m outcome from Gamma outcomePrices 240 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8004-000000000270',
    'btc_up_down_5m_less_270_gamma',
    'BTC Up or Down 5m Less 270 Gamma',
    'Experimental comparison strategy: choose the lower-priced BTC 5m outcome from Gamma outcomePrices 270 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8005-000000000030',
    'btc_up_down_5m_more_30_gamma',
    'BTC Up or Down 5m More 30 Gamma',
    'Experimental comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 30 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8005-000000000060',
    'btc_up_down_5m_more_60_gamma',
    'BTC Up or Down 5m More 60 Gamma',
    'Experimental comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 60 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8005-000000000090',
    'btc_up_down_5m_more_90_gamma',
    'BTC Up or Down 5m More 90 Gamma',
    'Experimental comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 90 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8005-000000000120',
    'btc_up_down_5m_more_120_gamma',
    'BTC Up or Down 5m More 120 Gamma',
    'Experimental comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 120 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8005-000000000150',
    'btc_up_down_5m_more_150_gamma',
    'BTC Up or Down 5m More 150 Gamma',
    'Experimental comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 150 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8005-000000000180',
    'btc_up_down_5m_more_180_gamma',
    'BTC Up or Down 5m More 180 Gamma',
    'Experimental comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 180 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8005-000000000210',
    'btc_up_down_5m_more_210_gamma',
    'BTC Up or Down 5m More 210 Gamma',
    'Experimental comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 210 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8005-000000000240',
    'btc_up_down_5m_more_240_gamma',
    'BTC Up or Down 5m More 240 Gamma',
    'Experimental comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 240 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8005-000000000270',
    'btc_up_down_5m_more_270_gamma',
    'BTC Up or Down 5m More 270 Gamma',
    'Experimental comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 270 seconds after window start, then use taker Paper pricing for the selected asset.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8022-000000060070',
    'btc_up_down_5m_more_60_gamma_below_70',
    'BTC Up or Down 5m More 60 Gamma Below 70',
    'Experimental Paper-only comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 60 seconds after window start, then place a GTD limit BUY at 0.70.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8022-000000060080',
    'btc_up_down_5m_more_60_gamma_below_80',
    'BTC Up or Down 5m More 60 Gamma Below 80',
    'Experimental Paper-only comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 60 seconds after window start, then place a GTD limit BUY at 0.80.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8022-000000090070',
    'btc_up_down_5m_more_90_gamma_below_70',
    'BTC Up or Down 5m More 90 Gamma Below 70',
    'Experimental Paper-only comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 90 seconds after window start, then place a GTD limit BUY at 0.70.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8022-000000120065',
    'btc_up_down_5m_more_120_gamma_below_65',
    'BTC Up or Down 5m More 120 Gamma Below 65',
    'Experimental Paper-only comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 120 seconds after window start, then place a GTD limit BUY at 0.65.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8022-000000120070',
    'btc_up_down_5m_more_120_gamma_below_70',
    'BTC Up or Down 5m More 120 Gamma Below 70',
    'Experimental Paper-only comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 120 seconds after window start, then place a GTD limit BUY at 0.70.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8022-000000150070',
    'btc_up_down_5m_more_150_gamma_below_70',
    'BTC Up or Down 5m More 150 Gamma Below 70',
    'Experimental Paper-only comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 150 seconds after window start, then place a GTD limit BUY at 0.70.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8022-000000150080',
    'btc_up_down_5m_more_150_gamma_below_80',
    'BTC Up or Down 5m More 150 Gamma Below 80',
    'Experimental Paper-only comparison strategy: choose the higher-priced BTC 5m outcome from Gamma outcomePrices 150 seconds after window start, then place a GTD limit BUY at 0.80.',
    true,
    1.00,
    now(),
    now()
)
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, paper_stake_amount, created_at_utc, updated_at_utc)
VALUES (
    'b7c50005-0000-4000-8006-000000000001',
    'btc_up_down_5m_middle_1',
    'BTC Up or Down 5m Middle 1',
    'Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price against the cached arithmetic mean; above mean buys Down, below mean buys Up, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8006-000000000002',
    'btc_up_down_5m_middle_2',
    'BTC Up or Down 5m Middle 2',
    'Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price plus the latest 1 cached reference sample(s) against the cached arithmetic mean; above mean buys Down, below mean buys Up, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8006-000000000003',
    'btc_up_down_5m_middle_3',
    'BTC Up or Down 5m Middle 3',
    'Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price plus the latest 2 cached reference sample(s) against the cached arithmetic mean; above mean buys Down, below mean buys Up, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8006-000000000004',
    'btc_up_down_5m_middle_4',
    'BTC Up or Down 5m Middle 4',
    'Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price plus the latest 3 cached reference sample(s) against the cached arithmetic mean; above mean buys Down, below mean buys Up, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8006-000000000005',
    'btc_up_down_5m_middle_5',
    'BTC Up or Down 5m Middle 5',
    'Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price plus the latest 4 cached reference sample(s) against the cached arithmetic mean; above mean buys Down, below mean buys Up, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8009-000000000001',
    'btc_up_down_5m_middle_1_revert',
    'BTC Up or Down 5m Middle 1 Revert',
    'Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price against the cached arithmetic mean, then invert the standard Middle 1 decision; above mean buys Up, below mean buys Down, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8009-000000000002',
    'btc_up_down_5m_middle_2_revert',
    'BTC Up or Down 5m Middle 2 Revert',
    'Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price plus the latest 1 cached reference sample(s) against the cached arithmetic mean, then invert the standard Middle 2 decision; above mean buys Up, below mean buys Down, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8009-000000000003',
    'btc_up_down_5m_middle_3_revert',
    'BTC Up or Down 5m Middle 3 Revert',
    'Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price plus the latest 2 cached reference sample(s) against the cached arithmetic mean, then invert the standard Middle 3 decision; above mean buys Up, below mean buys Down, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8009-000000000004',
    'btc_up_down_5m_middle_4_revert',
    'BTC Up or Down 5m Middle 4 Revert',
    'Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price plus the latest 3 cached reference sample(s) against the cached arithmetic mean, then invert the standard Middle 4 decision; above mean buys Up, below mean buys Down, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8009-000000000005',
    'btc_up_down_5m_middle_5_revert',
    'BTC Up or Down 5m Middle 5 Revert',
    'Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price plus the latest 4 cached reference sample(s) against the cached arithmetic mean, then invert the standard Middle 5 decision; above mean buys Up, below mean buys Down, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8007-000000000001',
    'btc_up_down_5m_skip_1',
    'BTC Up or Down 5m Skip 1',
    'Immediately after BTC 5m market open, inspect the latest 1 settled BTC 5m market result(s); after consecutive Up results buy Down, after consecutive Down results buy Up, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8007-000000000002',
    'btc_up_down_5m_skip_2',
    'BTC Up or Down 5m Skip 2',
    'Immediately after BTC 5m market open, inspect the latest 2 settled BTC 5m market result(s); after consecutive Up results buy Down, after consecutive Down results buy Up, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8007-000000000003',
    'btc_up_down_5m_skip_3',
    'BTC Up or Down 5m Skip 3',
    'Immediately after BTC 5m market open, inspect the latest 3 settled BTC 5m market result(s); after consecutive Up results buy Down, after consecutive Down results buy Up, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8007-000000000004',
    'btc_up_down_5m_skip_4',
    'BTC Up or Down 5m Skip 4',
    'Immediately after BTC 5m market open, inspect the latest 4 settled BTC 5m market result(s); after consecutive Up results buy Down, after consecutive Down results buy Up, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8007-000000000005',
    'btc_up_down_5m_skip_5',
    'BTC Up or Down 5m Skip 5',
    'Immediately after BTC 5m market open, inspect the latest 5 settled BTC 5m market result(s); after consecutive Up results buy Down, after consecutive Down results buy Up, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8008-000000000001',
    'btc_up_down_5m_skip_1_revert',
    'BTC Up or Down 5m Skip 1 Revert',
    'Immediately after BTC 5m market open, inspect the latest 1 settled BTC 5m market result(s), then invert the standard Skip 1 decision; after consecutive Up results buy Up, after consecutive Down results buy Down, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8008-000000000002',
    'btc_up_down_5m_skip_2_revert',
    'BTC Up or Down 5m Skip 2 Revert',
    'Immediately after BTC 5m market open, inspect the latest 2 settled BTC 5m market result(s), then invert the standard Skip 2 decision; after consecutive Up results buy Up, after consecutive Down results buy Down, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8008-000000000003',
    'btc_up_down_5m_skip_3_revert',
    'BTC Up or Down 5m Skip 3 Revert',
    'Immediately after BTC 5m market open, inspect the latest 3 settled BTC 5m market result(s), then invert the standard Skip 3 decision; after consecutive Up results buy Up, after consecutive Down results buy Down, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8008-000000000004',
    'btc_up_down_5m_skip_4_revert',
    'BTC Up or Down 5m Skip 4 Revert',
    'Immediately after BTC 5m market open, inspect the latest 4 settled BTC 5m market result(s), then invert the standard Skip 4 decision; after consecutive Up results buy Up, after consecutive Down results buy Down, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8008-000000000005',
    'btc_up_down_5m_skip_5_revert',
    'BTC Up or Down 5m Skip 5 Revert',
    'Immediately after BTC 5m market open, inspect the latest 5 settled BTC 5m market result(s), then invert the standard Skip 5 decision; after consecutive Up results buy Up, after consecutive Down results buy Down, otherwise skip. Paper entry is an ordinary GTD Paper BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8010-000000000001',
    'btc_up_down_5m_up',
    'BTC Up or Down 5m Up',
    'After BTC 5m trading starts, always place an Up GTD limit BUY at 0.45 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8010-000000000002',
    'btc_up_down_5m_down',
    'BTC Up or Down 5m Down',
    'After BTC 5m trading starts, always place a Down GTD limit BUY at 0.45 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8011-000000000001',
    'btc_up_down_5m_binance',
    'BTC Up or Down 5m Binance',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; above start buys Up, below start buys Down, equal skips. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8013-000000000010',
    'btc_up_down_5m_binance_bps_0_1',
    'BTC Up or Down 5m Binance 0.1 bps',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least 0.1 bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8013-000000000020',
    'btc_up_down_5m_binance_bps_0_2',
    'BTC Up or Down 5m Binance 0.2 bps',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least 0.2 bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8013-000000000030',
    'btc_up_down_5m_binance_bps_0_3',
    'BTC Up or Down 5m Binance 0.3 bps',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least 0.3 bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8013-000000000040',
    'btc_up_down_5m_binance_bps_0_4',
    'BTC Up or Down 5m Binance 0.4 bps',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least 0.4 bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8013-000000000050',
    'btc_up_down_5m_binance_bps_0_5',
    'BTC Up or Down 5m Binance 0.5 bps',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least 0.5 bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8013-000000000060',
    'btc_up_down_5m_binance_bps_0_6',
    'BTC Up or Down 5m Binance 0.6 bps',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least 0.6 bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8013-000000000070',
    'btc_up_down_5m_binance_bps_0_7',
    'BTC Up or Down 5m Binance 0.7 bps',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least 0.7 bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8013-000000000080',
    'btc_up_down_5m_binance_bps_0_8',
    'BTC Up or Down 5m Binance 0.8 bps',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least 0.8 bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8013-000000000090',
    'btc_up_down_5m_binance_bps_0_9',
    'BTC Up or Down 5m Binance 0.9 bps',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least 0.9 bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8013-000000000001',
    'btc_up_down_5m_binance_bps_1',
    'BTC Up or Down 5m Binance 1 bps',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least 1 bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8013-000000000002',
    'btc_up_down_5m_binance_bps_2',
    'BTC Up or Down 5m Binance 2 bps',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least 2 bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8013-000000000005',
    'btc_up_down_5m_binance_bps_5',
    'BTC Up or Down 5m Binance 5 bps',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least 5 bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8011-000000000045',
    'btc_up_down_5m_binance_45',
    'BTC Up or Down 5m Binance 45',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; above start buys Up, below start buys Down, equal skips. Paper entry is a GTD limit BUY at fixed 0.45 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8011-000000000047',
    'btc_up_down_5m_binance_47',
    'BTC Up or Down 5m Binance 47',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; above start buys Up, below start buys Down, equal skips. Paper entry is a GTD limit BUY at fixed 0.47 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8011-000000000049',
    'btc_up_down_5m_binance_49',
    'BTC Up or Down 5m Binance 49',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; above start buys Up, below start buys Down, equal skips. Paper entry is a GTD limit BUY at fixed 0.49 until the configured BTC GTD deadline; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8011-000000000002',
    'btc_up_down_5m_binance_clever',
    'BTC Up or Down 5m Binance Clever',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference, estimate a fair target outcome price from recent odds archive samples with similar BTC move/time-to-close/book quality, and place a GTD limit BUY only below fair value with a safety margin.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8011-000000000101',
    'btc_up_down_5m_binance_clever_aggressive',
    'BTC Up or Down 5m Binance Clever Aggressive',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference, estimate a fair target outcome price from recent odds archive samples with similar BTC move/time-to-close/book quality, and place a GTD limit BUY only below fair value with a 0.01 safety margin.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8011-000000000105',
    'btc_up_down_5m_binance_clever_conservative',
    'BTC Up or Down 5m Binance Clever Conservative',
    'After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference, estimate a fair target outcome price from recent odds archive samples with similar BTC move/time-to-close/book quality, and place a GTD limit BUY only below fair value with a 0.05 safety margin.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8014-000000000002',
    'btc_up_down_5m_binance_edge_2',
    'BTC Up or Down 5m Binance Edge 2',
    'After BTC 5m trading starts, use the Binance start-relative direction, estimate fair value from the BTC odds archive, and place a GTD limit BUY only when the safe price is at least 0.02 below fair value.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8014-000000000004',
    'btc_up_down_5m_binance_edge_4',
    'BTC Up or Down 5m Binance Edge 4',
    'After BTC 5m trading starts, use the Binance start-relative direction, estimate fair value from the BTC odds archive, and place a GTD limit BUY only when the safe price is at least 0.04 below fair value.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8014-000000000006',
    'btc_up_down_5m_binance_edge_6',
    'BTC Up or Down 5m Binance Edge 6',
    'After BTC 5m trading starts, use the Binance start-relative direction, estimate fair value from the BTC odds archive, and place a GTD limit BUY only when the safe price is at least 0.06 below fair value.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8015-000000000015',
    'btc_up_down_5m_binance_15s',
    'BTC Up or Down 5m Binance 15s',
    'Wait 15 seconds after BTC 5m trading starts, then compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; above start buys Up, below start buys Down, equal skips. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8015-000000000030',
    'btc_up_down_5m_binance_30s',
    'BTC Up or Down 5m Binance 30s',
    'Wait 30 seconds after BTC 5m trading starts, then compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; above start buys Up, below start buys Down, equal skips. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8015-000000000045',
    'btc_up_down_5m_binance_45s',
    'BTC Up or Down 5m Binance 45s',
    'Wait 45 seconds after BTC 5m trading starts, then compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; above start buys Up, below start buys Down, equal skips. Paper entry is a GTD limit BUY capped at 0.50 until the configured BTC GTD deadline.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8016-000000000002',
    'btc_up_down_5m_ensemble_2_of_3',
    'BTC Up or Down 5m Ensemble 2 of 3',
    'Immediately after BTC 5m market open, vote between Binance start-relative, Middle 1, and Skip 1 signals. Enter only when at least two available votes select the same outcome. Paper entry is a GTD limit BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8017-000000000050',
    'btc_up_down_5m_dynamic_markov',
    'BTC Up or Down 5m Dynamic Markov',
    'Immediately after BTC 5m market open, estimate the next result from recent BTC 5m result transitions and enter only when the transition edge is strong enough. Paper entry is a GTD limit BUY with dynamic break-even pricing.',
    true,
    1.00,
    now(),
    now()
),
(
    'b7c50005-0000-4000-8018-000000000030',
    'btc_up_down_5m_strategy_selector',
    'BTC Up or Down 5m Strategy Selector',
    'Immediately after BTC 5m market open, choose the best positive-expectancy opening BTC strategy from recent settled Paper history, then reuse that strategy''s current direction signal for one GTD limit BUY.',
    true,
    1.00,
    now(),
    now()
)
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

INSERT INTO strategies (id, code, name, description, enabled, paper_stake_amount, created_at_utc, updated_at_utc)
WITH depths(depth, sample_description) AS (
    VALUES
        (1, 'the latest Binance BTC/USDT trade-stream price'),
        (2, 'the latest Binance BTC/USDT trade-stream price plus the latest 1 cached reference sample(s)'),
        (3, 'the latest Binance BTC/USDT trade-stream price plus the latest 2 cached reference sample(s)'),
        (4, 'the latest Binance BTC/USDT trade-stream price plus the latest 3 cached reference sample(s)'),
        (5, 'the latest Binance BTC/USDT trade-stream price plus the latest 4 cached reference sample(s)')
),
thresholds(threshold_digit, threshold_name) AS (
    VALUES
        (1, '0.1'),
        (2, '0.2'),
        (3, '0.3'),
        (4, '0.4'),
        (5, '0.5'),
        (6, '0.6'),
        (7, '0.7'),
        (8, '0.8'),
        (9, '0.9')
)
SELECT
    ('b7c50005-0000-4000-8023-' || lpad(((depths.depth * 100) + thresholds.threshold_digit)::text, 12, '0'))::uuid,
    'btc_up_down_5m_middle_' || depths.depth || '_bps_0_' || thresholds.threshold_digit,
    'BTC Up or Down 5m Middle ' || depths.depth || ' ' || thresholds.threshold_name || ' bps',
    'Immediately after BTC 5m market open, compare ' || depths.sample_description || ' against the cached arithmetic mean; above mean buys Down, below mean buys Up, otherwise skip. Enter only when every compared price is at least ' || thresholds.threshold_name || ' bps away from the mean. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
FROM depths
CROSS JOIN thresholds
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

WITH depths(depth, sample_description) AS (
    VALUES
        (1, 'the latest Binance BTC/USDT trade-stream price'),
        (2, 'the latest Binance BTC/USDT trade-stream price plus the latest 1 cached reference sample(s)'),
        (3, 'the latest Binance BTC/USDT trade-stream price plus the latest 2 cached reference sample(s)'),
        (4, 'the latest Binance BTC/USDT trade-stream price plus the latest 3 cached reference sample(s)'),
        (5, 'the latest Binance BTC/USDT trade-stream price plus the latest 4 cached reference sample(s)')
),
thresholds(threshold_digit, threshold_name) AS (
    VALUES
        (1, '0.1'),
        (2, '0.2'),
        (3, '0.3'),
        (4, '0.4'),
        (5, '0.5'),
        (6, '0.6'),
        (7, '0.7'),
        (8, '0.8'),
        (9, '0.9')
)
INSERT INTO strategies (id, code, name, description, enabled, paper_stake_amount, created_at_utc, updated_at_utc)
SELECT
    ('b7c50005-0000-4000-8024-' || lpad(((depths.depth * 100) + thresholds.threshold_digit)::text, 12, '0'))::uuid,
    'btc_up_down_5m_middle_' || depths.depth || '_revert_bps_0_' || thresholds.threshold_digit,
    'BTC Up or Down 5m Middle ' || depths.depth || ' Revert ' || thresholds.threshold_name || ' bps',
    'Immediately after BTC 5m market open, compare ' || depths.sample_description || ' against the cached arithmetic mean, then invert the standard Middle ' || depths.depth || ' decision; above mean buys Up, below mean buys Down, otherwise skip. Enter only when every compared price is at least ' || thresholds.threshold_name || ' bps away from the mean. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
FROM depths
CROSS JOIN thresholds
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

WITH intervals(interval_id, interval_code, interval_name, interval_description) AS (
    VALUES
        (1, '5m', '5m', '5-minute'),
        (2, '15m', '15m', '15-minute'),
        (3, '1h', '1h', 'hourly'),
        (4, '4h', '4h', '4-hour')
),
lifetimes(lifetime_id, lifetime_code, lifetime_name, lifetime_description) AS (
    VALUES
        (1, 'half', 'Half', 'until the half-period local cancel deadline'),
        (2, 'full', 'Full', 'until the market-end local safety deadline')
),
outcomes(outcome_id, outcome_code, outcome_name) AS (
    VALUES
        (1, 'up', 'Up'),
        (2, 'down', 'Down')
),
prices(price_cents) AS (
    SELECT generate_series(49, 10, -1)
)
INSERT INTO strategies (id, code, name, description, enabled, paper_stake_amount, created_at_utc, updated_at_utc)
SELECT
    ('b7c50005-0000-4000-803' || intervals.interval_id || '-0000000' || lifetimes.lifetime_id || outcomes.outcome_id || lpad(prices.price_cents::text, 3, '0'))::uuid,
    'btc_up_down_' || intervals.interval_code || '_preopen_' || lifetimes.lifetime_code || '_' || outcomes.outcome_code || '_' || prices.price_cents,
    'BTC Up or Down ' || intervals.interval_name || ' PreOpen ' || lifetimes.lifetime_name || ' ' || outcomes.outcome_name || ' ' || prices.price_cents,
    'Five minutes before the BTC ' || intervals.interval_description || ' market opens, always place a Paper GTD limit BUY on ' || outcomes.outcome_name || ' at ' || to_char((prices.price_cents::numeric / 100), 'FM0.00') || ' and keep it ' || lifetimes.lifetime_description || '; settlement uses only actually filled shares.',
    true,
    1.00,
    now(),
    now()
FROM intervals
CROSS JOIN lifetimes
CROSS JOIN outcomes
CROSS JOIN prices
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc;

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

CREATE INDEX IF NOT EXISTS ix_paper_orders_correlation
ON paper_orders(correlation_id)
WHERE correlation_id IS NOT NULL;

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
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    UNIQUE (strategy_id, market_id)
);

ALTER TABLE strategy_market_paper_runs ADD COLUMN IF NOT EXISTS skip_diagnostics_json jsonb NULL;

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
""";
}
