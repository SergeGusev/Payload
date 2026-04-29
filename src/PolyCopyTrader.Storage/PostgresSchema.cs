namespace PolyCopyTrader.Storage;

public static class PostgresSchema
{
    public static readonly IReadOnlyList<string> RequiredTables =
    [
        "traders",
        "trader_rules",
        "leader_trades",
        "leader_positions",
        "markets",
        "order_book_snapshots",
        "signals",
        "signal_rejections",
        "paper_orders",
        "paper_fills",
        "paper_positions",
        "risk_events",
        "bot_settings",
        "api_errors",
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
    raw_json jsonb NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_leader_trades_dedup
ON leader_trades(trader_wallet, COALESCE(transaction_hash, ''), asset_id, side, timestamp_utc);

CREATE TABLE IF NOT EXISTS leader_positions (
    id uuid PRIMARY KEY,
    trader_wallet text NOT NULL,
    condition_id text NOT NULL,
    asset_id text NOT NULL,
    outcome text NOT NULL,
    size numeric(28,8) NOT NULL,
    avg_price numeric(18,8) NOT NULL,
    current_value numeric(28,8) NOT NULL,
    cash_pnl numeric(28,8) NOT NULL,
    cur_price numeric(18,8) NOT NULL,
    snapshot_at_utc timestamptz NOT NULL,
    raw_json jsonb NULL
);

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
    decision text NOT NULL,
    proposed_paper_price numeric(18,8) NULL,
    created_at_utc timestamptz NOT NULL,
    raw_context_json jsonb NULL
);

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
    price numeric(18,8) NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    notional_usd numeric(28,8) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    expires_at_utc timestamptz NOT NULL,
    filled_at_utc timestamptz NULL,
    cancelled_at_utc timestamptz NULL,
    raw_decision_json jsonb NULL
);

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

CREATE TABLE IF NOT EXISTS risk_events (
    id uuid PRIMARY KEY,
    reason_code text NOT NULL,
    details text NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS bot_settings (
    key text PRIMARY KEY,
    value text NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS api_errors (
    id uuid PRIMARY KEY,
    component text NOT NULL,
    operation text NOT NULL,
    message text NOT NULL,
    created_at_utc timestamptz NOT NULL
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
