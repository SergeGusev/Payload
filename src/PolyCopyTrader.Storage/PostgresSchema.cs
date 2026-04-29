namespace PolyCopyTrader.Storage;

public static class PostgresSchema
{
    public static readonly IReadOnlyList<string> RequiredTables =
    [
        "traders",
        "trader_rules",
        "trader_leaderboard_snapshots",
        "trader_discovery_candidates",
        "leader_trades",
        "leader_positions",
        "markets",
        "order_book_snapshots",
        "signals",
        "signal_rejections",
        "paper_orders",
        "paper_fills",
        "paper_positions",
        "dry_run_orders",
        "live_orders",
        "live_trading_events",
        "risk_events",
        "market_data_status",
        "market_data_events",
        "pinned_market_assets",
        "daily_reports",
        "bot_settings",
        "service_command_audit",
        "api_errors",
        "polymarket_http_logs",
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

CREATE TABLE IF NOT EXISTS paper_orders (
    id uuid PRIMARY KEY,
    signal_id uuid NOT NULL,
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
    raw_decision_json jsonb NULL
);

ALTER TABLE paper_orders ADD COLUMN IF NOT EXISTS outcome text NOT NULL DEFAULT '';

CREATE TABLE IF NOT EXISTS paper_fills (
    id uuid PRIMARY KEY,
    paper_order_id uuid NOT NULL REFERENCES paper_orders(id),
    price numeric(18,8) NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    filled_at_utc timestamptz NOT NULL,
    evidence text NOT NULL
);

CREATE TABLE IF NOT EXISTS paper_positions (
    id uuid PRIMARY KEY,
    asset_id text NOT NULL,
    condition_id text NOT NULL,
    outcome text NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    average_price numeric(18,8) NOT NULL,
    estimated_value_usd numeric(28,8) NOT NULL,
    unrealized_pnl_usd numeric(28,8) NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_paper_positions_asset
ON paper_positions(asset_id);

CREATE TABLE IF NOT EXISTS dry_run_orders (
    id uuid PRIMARY KEY,
    signal_id uuid NOT NULL,
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

CREATE INDEX IF NOT EXISTS ix_dry_run_orders_created
ON dry_run_orders(created_at_utc DESC);

CREATE TABLE IF NOT EXISTS live_orders (
    id uuid PRIMARY KEY,
    signal_id uuid NOT NULL,
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
    cancel_status text NOT NULL,
    raw_response_json jsonb NOT NULL,
    validation_summary text NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_live_orders_open
ON live_orders(status, created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_live_orders_order_id
ON live_orders(order_id);

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
