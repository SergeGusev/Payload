namespace PolyCopyTrader.Storage;

public static class DashboardProjectionSchema
{
    public const string SchemaSql = """
CREATE TABLE IF NOT EXISTS dashboard_projection_control (
    singleton_id smallint PRIMARY KEY DEFAULT 1,
    initialized boolean NOT NULL DEFAULT false,
    calculation_version integer NOT NULL DEFAULT 1,
    status text NOT NULL DEFAULT 'PendingBootstrap',
    reconciliation_cursor_strategy_id uuid NULL,
    bootstrap_started_at_utc timestamptz NULL,
    bootstrap_completed_at_utc timestamptz NULL,
    last_event_applied_at_utc timestamptz NULL,
    last_expiry_at_utc timestamptz NULL,
    last_reconciliation_at_utc timestamptz NULL,
    last_error text NULL,
    updated_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_dashboard_projection_control_singleton CHECK (singleton_id = 1)
);

INSERT INTO dashboard_projection_control (singleton_id)
VALUES (1)
ON CONFLICT (singleton_id) DO NOTHING;

CREATE TABLE IF NOT EXISTS dashboard_projection_events (
    id bigserial PRIMARY KEY,
    source_kind text NOT NULL,
    source_id uuid NOT NULL,
    strategy_id uuid NULL,
    operation text NOT NULL,
    old_payload jsonb NULL,
    new_payload jsonb NULL,
    transaction_id xid8 NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp()
);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_projection_events_strategy_id
ON dashboard_projection_events (strategy_id, id);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_projection_events_created_at
ON dashboard_projection_events (created_at_utc, id);

DELETE FROM dashboard_projection_events older
USING dashboard_projection_events newer
WHERE older.source_kind = 'PaperPosition'
  AND newer.source_kind = 'PaperPosition'
  AND older.source_id = newer.source_id
  AND older.id < newer.id;

CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_dashboard_projection_events_paper_position
ON dashboard_projection_events (source_kind, source_id)
WHERE source_kind = 'PaperPosition';

CREATE TABLE IF NOT EXISTS dashboard_projection_reconciliation_queue (
    strategy_id uuid PRIMARY KEY,
    priority integer NOT NULL DEFAULT 0,
    reason text NOT NULL,
    requested_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    attempt_count integer NOT NULL DEFAULT 0,
    next_attempt_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    last_error text NULL
);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_projection_reconciliation_queue_due
ON dashboard_projection_reconciliation_queue (priority DESC, next_attempt_at_utc, requested_at_utc);

CREATE TABLE IF NOT EXISTS dashboard_strategy_lifetime_projection_states (
    strategy_id uuid PRIMARY KEY REFERENCES strategies(id) ON DELETE CASCADE,
    state_json jsonb NOT NULL,
    projection_version bigint NOT NULL DEFAULT 0,
    last_event_id bigint NULL,
    last_reconciled_at_utc timestamptz NULL,
    updated_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS dashboard_strategy_recent_projection_states (
    strategy_id uuid NOT NULL REFERENCES strategies(id) ON DELETE CASCADE,
    window_hours integer NOT NULL,
    state_json jsonb NOT NULL,
    projection_version bigint NOT NULL DEFAULT 0,
    last_event_id bigint NULL,
    last_reconciled_at_utc timestamptz NULL,
    updated_at_utc timestamptz NOT NULL,
    PRIMARY KEY (strategy_id, window_hours),
    CONSTRAINT ck_dashboard_strategy_recent_projection_window CHECK (window_hours IN (1, 6, 24))
);

CREATE TABLE IF NOT EXISTS dashboard_strategy_recent_projection_facts (
    source_kind text NOT NULL,
    source_id uuid NOT NULL,
    fact_kind text NOT NULL,
    strategy_id uuid NOT NULL REFERENCES strategies(id) ON DELETE CASCADE,
    occurred_at_utc timestamptz NOT NULL,
    contribution_json jsonb NOT NULL,
    applied_1h boolean NOT NULL,
    applied_6h boolean NOT NULL,
    applied_24h boolean NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    PRIMARY KEY (source_kind, source_id, fact_kind)
);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_recent_projection_facts_strategy
ON dashboard_strategy_recent_projection_facts (strategy_id, occurred_at_utc DESC);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_recent_projection_facts_expire_1h
ON dashboard_strategy_recent_projection_facts (occurred_at_utc, strategy_id)
WHERE applied_1h;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_recent_projection_facts_expire_6h
ON dashboard_strategy_recent_projection_facts (occurred_at_utc, strategy_id)
WHERE applied_6h;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_recent_projection_facts_expire_24h
ON dashboard_strategy_recent_projection_facts (occurred_at_utc, strategy_id)
WHERE applied_24h;

CREATE TABLE IF NOT EXISTS dashboard_strategy_position_projection_facts (
    source_id uuid PRIMARY KEY,
    strategy_id uuid NOT NULL REFERENCES strategies(id) ON DELETE CASCADE,
    size_shares numeric NOT NULL,
    unrealized_pnl_usd numeric NOT NULL,
    updated_at_utc timestamptz NOT NULL
);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_position_projection_facts_strategy
ON dashboard_strategy_position_projection_facts (strategy_id, source_id);

CREATE OR REPLACE FUNCTION public.strategy_market_paper_run_retention_blockers(
    target_run public.strategy_market_paper_runs)
RETURNS text[]
LANGUAGE sql
STABLE
AS $function$
SELECT array_remove(ARRAY[
    CASE WHEN (target_run).retention_scope <> 'PaperOnly' THEN 'retention_scope' END,
    CASE WHEN (target_run).status <> 'Skipped' THEN 'non_skipped_status' END,
    CASE WHEN NULLIF(btrim(COALESCE((target_run).skip_reason, '')), '') IS NULL THEN 'missing_skip_reason' END,
    CASE WHEN (target_run).signal_id IS NOT NULL THEN 'signal_id' END,
    CASE WHEN (target_run).paper_order_id IS NOT NULL THEN 'paper_order_id' END,
    CASE WHEN (target_run).entered_at_utc IS NOT NULL THEN 'entered_at_utc' END,
    CASE WHEN (target_run).entry_price IS NOT NULL THEN 'entry_price' END,
    CASE WHEN (target_run).size_shares IS NOT NULL THEN 'size_shares' END,
    CASE WHEN (target_run).settlement_price IS NOT NULL THEN 'settlement_price' END,
    CASE WHEN (target_run).settlement_value_usd IS NOT NULL THEN 'settlement_value_usd' END,
    CASE WHEN (target_run).realized_pnl_usd IS NOT NULL THEN 'realized_pnl_usd' END,
    CASE WHEN (target_run).settled_at_utc IS NOT NULL THEN 'settled_at_utc' END,
    CASE WHEN (target_run).skip_diagnostics_json IS NOT NULL THEN 'skip_diagnostics_json' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.paper_orders paper_order
        WHERE paper_order.strategy_id = (target_run).strategy_id
          AND paper_order.condition_id = (target_run).condition_id
    ) THEN 'paper_order_dependency' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.dry_run_orders dry_order
        WHERE dry_order.strategy_id = (target_run).strategy_id
          AND dry_order.condition_id = (target_run).condition_id
    ) THEN 'dry_run_dependency' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.live_orders live_order
        WHERE live_order.strategy_id = (target_run).strategy_id
          AND live_order.condition_id = (target_run).condition_id
    ) THEN 'live_order_dependency' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.paper_live_shadow_decisions decision
        WHERE decision.strategy_id = (target_run).strategy_id
          AND (decision.market_id = (target_run).market_id
               OR decision.condition_id = (target_run).condition_id)
    ) THEN 'live_shadow_dependency' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.strategies strategy
        INNER JOIN public.paper_positions position_row
            ON lower(position_row.copied_trader_wallet) = lower('strategy:' || strategy.code)
           AND position_row.condition_id = (target_run).condition_id
        WHERE strategy.id = (target_run).strategy_id
    ) THEN 'paper_position_dependency' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.strategies strategy
        INNER JOIN public.paper_position_settlements settlement
            ON lower(settlement.copied_trader_wallet) = lower('strategy:' || strategy.code)
           AND settlement.condition_id = (target_run).condition_id
        WHERE strategy.id = (target_run).strategy_id
    ) THEN 'paper_settlement_dependency' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.paper_copied_leader_positions position_row
        WHERE position_row.condition_id = (target_run).condition_id
    ) THEN 'copied_leader_position_dependency' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.paper_copied_leader_activity_events activity
        WHERE activity.condition_id = (target_run).condition_id
    ) THEN 'copied_leader_activity_dependency' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.polymarket_onchain_paper_signal_results result_row
        WHERE result_row.condition_id = (target_run).condition_id
    ) THEN 'onchain_paper_dependency' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.strategy_market_paper_skip_tombstones tombstone
        WHERE tombstone.strategy_id = (target_run).strategy_id
          AND tombstone.market_id = (target_run).market_id
    ) THEN 'existing_tombstone' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.dashboard_projection_events projection_event
        WHERE projection_event.source_kind = 'StrategyRun'
          AND projection_event.source_id = (target_run).id
    ) THEN 'pending_projection_event' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.dashboard_strategy_recent_projection_facts fact
        WHERE fact.source_kind = 'StrategyRun'
          AND fact.source_id = (target_run).id
    ) THEN 'recent_projection_fact' END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM public.dashboard_projection_reconciliation_queue queue_row
        WHERE queue_row.strategy_id = (target_run).strategy_id
    ) THEN 'pending_projection_reconciliation' END
]::text[], NULL);
$function$;

CREATE OR REPLACE FUNCTION public.dashboard_projection_strategy_payload(row_value public.strategies)
RETURNS jsonb
LANGUAGE sql
IMMUTABLE
AS $function$
SELECT jsonb_build_object(
    'id', (row_value).id,
    'live_stakes', (row_value).live_stakes,
    'live_enabled_at_utc', (row_value).live_enabled_at_utc
);
$function$;

CREATE OR REPLACE FUNCTION public.dashboard_projection_paper_order_payload(row_value public.paper_orders)
RETURNS jsonb
LANGUAGE sql
IMMUTABLE
AS $function$
SELECT jsonb_build_object(
    'id', (row_value).id,
    'strategy_id', (row_value).strategy_id,
    'status', (row_value).status,
    'side', (row_value).side,
    'notional_usd', (row_value).notional_usd,
    'created_at_utc', (row_value).created_at_utc,
    'previous_score_bps', CASE
        WHEN jsonb_typeof((row_value).raw_decision_json -> 'previous_score_bps') = 'number'
        THEN round(((row_value).raw_decision_json ->> 'previous_score_bps')::numeric, 8)
        ELSE NULL
    END,
    'previous_score', CASE
        WHEN jsonb_typeof((row_value).raw_decision_json -> 'previous_score') = 'number'
        THEN round(((row_value).raw_decision_json ->> 'previous_score')::numeric, 12)
        ELSE NULL
    END,
    'selected_signal_bps', CASE
        WHEN jsonb_typeof((row_value).raw_decision_json -> 'selected_signal_bps') = 'number'
        THEN round(((row_value).raw_decision_json ->> 'selected_signal_bps')::numeric, 8)
        ELSE NULL
    END
);
$function$;

CREATE OR REPLACE FUNCTION public.dashboard_projection_paper_fill_payload(
    row_value public.paper_fills,
    target_strategy_id uuid,
    target_order_side text)
RETURNS jsonb
LANGUAGE sql
IMMUTABLE
AS $function$
SELECT jsonb_build_object(
    'id', (row_value).id,
    'strategy_id', target_strategy_id,
    'order_side', target_order_side,
    'price', (row_value).price,
    'size_shares', (row_value).size_shares,
    'realized_pnl_usd', (row_value).realized_pnl_usd,
    'filled_at_utc', (row_value).filled_at_utc
);
$function$;

CREATE OR REPLACE FUNCTION public.dashboard_projection_run_payload(
    row_value public.strategy_market_paper_runs,
    target_live_enabled_at_utc timestamptz)
RETURNS jsonb
LANGUAGE sql
IMMUTABLE
AS $function$
SELECT jsonb_build_object(
    'id', (row_value).id,
    'strategy_id', (row_value).strategy_id,
    'status', (row_value).status,
    'stake_usd', (row_value).stake_usd,
    'paper_order_id', (row_value).paper_order_id,
    'entry_due_at_utc', (row_value).entry_due_at_utc,
    'entered_at_utc', (row_value).entered_at_utc,
    'realized_pnl_usd', (row_value).realized_pnl_usd,
    'settled_at_utc', (row_value).settled_at_utc,
    'skip_reason', (row_value).skip_reason,
    'updated_at_utc', (row_value).updated_at_utc,
    'live_enabled_at_utc', target_live_enabled_at_utc
);
$function$;

CREATE OR REPLACE FUNCTION public.dashboard_projection_paper_position_payload(
    row_value public.paper_positions,
    target_strategy_id uuid)
RETURNS jsonb
LANGUAGE sql
IMMUTABLE
AS $function$
SELECT jsonb_build_object(
    'id', (row_value).id,
    'strategy_id', target_strategy_id,
    'size_shares', (row_value).size_shares,
    'unrealized_pnl_usd', (row_value).unrealized_pnl_usd
);
$function$;

CREATE OR REPLACE FUNCTION public.dashboard_projection_paper_settlement_payload(
    row_value public.paper_position_settlements,
    target_strategy_id uuid)
RETURNS jsonb
LANGUAGE sql
IMMUTABLE
AS $function$
SELECT jsonb_build_object(
    'id', (row_value).id,
    'strategy_id', target_strategy_id,
    'cost_basis_usd', (row_value).cost_basis_usd,
    'realized_pnl_usd', (row_value).realized_pnl_usd,
    'won', (row_value).won
);
$function$;

CREATE OR REPLACE FUNCTION public.dashboard_projection_live_order_payload(row_value public.live_orders)
RETURNS jsonb
LANGUAGE sql
IMMUTABLE
AS $function$
SELECT jsonb_build_object(
    'id', (row_value).id,
    'strategy_id', (row_value).strategy_id,
    'status', (row_value).status,
    'price', (row_value).price,
    'filled_size', (row_value).filled_size,
    'remaining_size', (row_value).remaining_size,
    'filled_notional_usd', (row_value).filled_notional_usd,
    'cost_basis_usd', (row_value).cost_basis_usd,
    'fee_usd', (row_value).fee_usd,
    'settlement_value_usd', (row_value).settlement_value_usd,
    'realized_pnl_usd', (row_value).realized_pnl_usd,
    'settled_at_utc', (row_value).settled_at_utc,
    'won', (row_value).won,
    'created_at_utc', (row_value).created_at_utc,
    'updated_at_utc', (row_value).updated_at_utc
);
$function$;

CREATE OR REPLACE FUNCTION public.queue_dashboard_strategy_projection_event()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    source_row_id uuid := COALESCE(NEW.id, OLD.id);
BEGIN
    INSERT INTO dashboard_projection_events (
        source_kind, source_id, strategy_id, operation, old_payload, new_payload, transaction_id)
    VALUES (
        'Strategy',
        source_row_id,
        source_row_id,
        initcap(lower(TG_OP)),
        CASE WHEN TG_OP = 'INSERT' THEN NULL ELSE public.dashboard_projection_strategy_payload(OLD) END,
        CASE WHEN TG_OP = 'DELETE' THEN NULL ELSE public.dashboard_projection_strategy_payload(NEW) END,
        pg_current_xact_id());
    RETURN COALESCE(NEW, OLD);
END;
$function$;

CREATE OR REPLACE FUNCTION public.queue_dashboard_paper_order_projection_event()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    source_row_id uuid := COALESCE(NEW.id, OLD.id);
    target_strategy_id uuid := COALESCE(NEW.strategy_id, OLD.strategy_id);
BEGIN
    INSERT INTO dashboard_projection_events (
        source_kind, source_id, strategy_id, operation, old_payload, new_payload, transaction_id)
    VALUES (
        'PaperOrder',
        source_row_id,
        target_strategy_id,
        initcap(lower(TG_OP)),
        CASE WHEN TG_OP = 'INSERT' THEN NULL ELSE public.dashboard_projection_paper_order_payload(OLD) END,
        CASE WHEN TG_OP = 'DELETE' THEN NULL ELSE public.dashboard_projection_paper_order_payload(NEW) END,
        pg_current_xact_id());
    RETURN COALESCE(NEW, OLD);
END;
$function$;

CREATE OR REPLACE FUNCTION public.queue_dashboard_paper_fill_projection_event()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    source_row_id uuid := COALESCE(NEW.id, OLD.id);
    old_strategy_id uuid;
    new_strategy_id uuid;
    old_order_side text;
    new_order_side text;
    target_strategy_id uuid;
BEGIN
    IF TG_OP <> 'INSERT' THEN
        SELECT paper_order.strategy_id, paper_order.side
        INTO old_strategy_id, old_order_side
        FROM paper_orders paper_order
        WHERE paper_order.id = OLD.paper_order_id;
    END IF;

    IF TG_OP <> 'DELETE' THEN
        SELECT paper_order.strategy_id, paper_order.side
        INTO new_strategy_id, new_order_side
        FROM paper_orders paper_order
        WHERE paper_order.id = NEW.paper_order_id;
    END IF;

    target_strategy_id := COALESCE(new_strategy_id, old_strategy_id);

    INSERT INTO dashboard_projection_events (
        source_kind, source_id, strategy_id, operation, old_payload, new_payload, transaction_id)
    VALUES (
        'PaperFill',
        source_row_id,
        target_strategy_id,
        initcap(lower(TG_OP)),
        CASE WHEN TG_OP = 'INSERT' OR old_strategy_id IS NULL THEN NULL
             ELSE public.dashboard_projection_paper_fill_payload(OLD, old_strategy_id, old_order_side) END,
        CASE WHEN TG_OP = 'DELETE' OR new_strategy_id IS NULL THEN NULL
             ELSE public.dashboard_projection_paper_fill_payload(NEW, new_strategy_id, new_order_side) END,
        pg_current_xact_id());
    RETURN COALESCE(NEW, OLD);
END;
$function$;

CREATE OR REPLACE FUNCTION public.queue_dashboard_run_projection_event()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    source_row_id uuid := COALESCE(NEW.id, OLD.id);
    target_strategy_id uuid := COALESCE(NEW.strategy_id, OLD.strategy_id);
    target_live_enabled_at_utc timestamptz;
BEGIN
    IF TG_OP = 'DELETE'
       AND current_setting('polycopytrader.skip_run_retention_transfer', true) = 'on' THEN
        RETURN OLD;
    END IF;

    SELECT strategy.live_enabled_at_utc
    INTO target_live_enabled_at_utc
    FROM strategies strategy
    WHERE strategy.id = target_strategy_id;

    INSERT INTO dashboard_projection_events (
        source_kind, source_id, strategy_id, operation, old_payload, new_payload, transaction_id)
    VALUES (
        'StrategyRun',
        source_row_id,
        target_strategy_id,
        initcap(lower(TG_OP)),
        CASE WHEN TG_OP = 'INSERT' THEN NULL
             ELSE public.dashboard_projection_run_payload(OLD, target_live_enabled_at_utc) END,
        CASE WHEN TG_OP = 'DELETE' THEN NULL
             ELSE public.dashboard_projection_run_payload(NEW, target_live_enabled_at_utc) END,
        pg_current_xact_id());
    RETURN COALESCE(NEW, OLD);
END;
$function$;

CREATE OR REPLACE FUNCTION public.dashboard_projection_strategy_id_for_wallet(target_wallet text)
RETURNS uuid
LANGUAGE sql
STABLE
AS $function$
SELECT CASE
    WHEN lower(COALESCE(target_wallet, '')) LIKE 'strategy:%' THEN (
        SELECT strategy.id
        FROM strategies strategy
        WHERE strategy.code = lower(substring(target_wallet from 10))
        LIMIT 1
    )
    ELSE (
        SELECT strategy.id
        FROM strategies strategy
        WHERE strategy.id = 'f0110a0d-1ead-4c00-8b01-000000000001'::uuid
        LIMIT 1
    )
END;
$function$;

CREATE OR REPLACE FUNCTION public.queue_dashboard_paper_position_projection_event()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    source_row_id uuid := COALESCE(NEW.id, OLD.id);
    old_strategy_id uuid := CASE WHEN TG_OP = 'INSERT' THEN NULL
        ELSE public.dashboard_projection_strategy_id_for_wallet(OLD.copied_trader_wallet) END;
    new_strategy_id uuid := CASE WHEN TG_OP = 'DELETE' THEN NULL
        ELSE public.dashboard_projection_strategy_id_for_wallet(NEW.copied_trader_wallet) END;
    target_strategy_id uuid := COALESCE(new_strategy_id, old_strategy_id);
BEGIN
    IF TG_OP = 'UPDATE'
       AND OLD.copied_trader_wallet IS NOT DISTINCT FROM NEW.copied_trader_wallet
       AND OLD.size_shares IS NOT DISTINCT FROM NEW.size_shares
       AND OLD.unrealized_pnl_usd IS NOT DISTINCT FROM NEW.unrealized_pnl_usd THEN
        RETURN NEW;
    END IF;

    INSERT INTO dashboard_projection_events (
        source_kind, source_id, strategy_id, operation, old_payload, new_payload, transaction_id)
    VALUES (
        'PaperPosition',
        source_row_id,
        target_strategy_id,
        initcap(lower(TG_OP)),
        CASE WHEN TG_OP = 'INSERT' OR old_strategy_id IS NULL THEN NULL
             ELSE public.dashboard_projection_paper_position_payload(OLD, old_strategy_id) END,
        CASE WHEN TG_OP = 'DELETE' OR new_strategy_id IS NULL THEN NULL
             ELSE public.dashboard_projection_paper_position_payload(NEW, new_strategy_id) END,
        pg_current_xact_id())
    ON CONFLICT (source_kind, source_id) WHERE source_kind = 'PaperPosition'
    DO UPDATE SET
        strategy_id = EXCLUDED.strategy_id,
        operation = EXCLUDED.operation,
        old_payload = dashboard_projection_events.old_payload,
        new_payload = EXCLUDED.new_payload,
        transaction_id = EXCLUDED.transaction_id,
        created_at_utc = clock_timestamp();
    RETURN COALESCE(NEW, OLD);
END;
$function$;

CREATE OR REPLACE FUNCTION public.queue_dashboard_paper_settlement_projection_event()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    source_row_id uuid := COALESCE(NEW.id, OLD.id);
    old_strategy_id uuid := CASE WHEN TG_OP = 'INSERT' THEN NULL
        ELSE public.dashboard_projection_strategy_id_for_wallet(OLD.copied_trader_wallet) END;
    new_strategy_id uuid := CASE WHEN TG_OP = 'DELETE' THEN NULL
        ELSE public.dashboard_projection_strategy_id_for_wallet(NEW.copied_trader_wallet) END;
    target_strategy_id uuid := COALESCE(new_strategy_id, old_strategy_id);
BEGIN
    INSERT INTO dashboard_projection_events (
        source_kind, source_id, strategy_id, operation, old_payload, new_payload, transaction_id)
    VALUES (
        'PaperSettlement',
        source_row_id,
        target_strategy_id,
        initcap(lower(TG_OP)),
        CASE WHEN TG_OP = 'INSERT' OR old_strategy_id IS NULL THEN NULL
             ELSE public.dashboard_projection_paper_settlement_payload(OLD, old_strategy_id) END,
        CASE WHEN TG_OP = 'DELETE' OR new_strategy_id IS NULL THEN NULL
             ELSE public.dashboard_projection_paper_settlement_payload(NEW, new_strategy_id) END,
        pg_current_xact_id());
    RETURN COALESCE(NEW, OLD);
END;
$function$;

CREATE OR REPLACE FUNCTION public.queue_dashboard_live_order_projection_event()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    source_row_id uuid := COALESCE(NEW.id, OLD.id);
    target_strategy_id uuid := COALESCE(NEW.strategy_id, OLD.strategy_id);
BEGIN
    INSERT INTO dashboard_projection_events (
        source_kind, source_id, strategy_id, operation, old_payload, new_payload, transaction_id)
    VALUES (
        'LiveOrder',
        source_row_id,
        target_strategy_id,
        initcap(lower(TG_OP)),
        CASE WHEN TG_OP = 'INSERT' THEN NULL ELSE public.dashboard_projection_live_order_payload(OLD) END,
        CASE WHEN TG_OP = 'DELETE' THEN NULL ELSE public.dashboard_projection_live_order_payload(NEW) END,
        pg_current_xact_id());
    RETURN COALESCE(NEW, OLD);
END;
$function$;

DROP TRIGGER IF EXISTS trg_dashboard_projection_strategy_lifecycle ON strategies;
CREATE TRIGGER trg_dashboard_projection_strategy_lifecycle
AFTER INSERT OR DELETE ON strategies
FOR EACH ROW EXECUTE FUNCTION public.queue_dashboard_strategy_projection_event();

DROP TRIGGER IF EXISTS trg_dashboard_projection_strategy_live_state ON strategies;
CREATE TRIGGER trg_dashboard_projection_strategy_live_state
AFTER UPDATE OF live_stakes, live_enabled_at_utc ON strategies
FOR EACH ROW
WHEN (OLD.live_stakes IS DISTINCT FROM NEW.live_stakes OR
      OLD.live_enabled_at_utc IS DISTINCT FROM NEW.live_enabled_at_utc)
EXECUTE FUNCTION public.queue_dashboard_strategy_projection_event();

DROP TRIGGER IF EXISTS trg_dashboard_projection_paper_order ON paper_orders;
CREATE TRIGGER trg_dashboard_projection_paper_order
AFTER INSERT OR UPDATE OR DELETE ON paper_orders
FOR EACH ROW EXECUTE FUNCTION public.queue_dashboard_paper_order_projection_event();

DROP TRIGGER IF EXISTS trg_dashboard_projection_paper_fill ON paper_fills;
CREATE TRIGGER trg_dashboard_projection_paper_fill
AFTER INSERT OR UPDATE OR DELETE ON paper_fills
FOR EACH ROW EXECUTE FUNCTION public.queue_dashboard_paper_fill_projection_event();

DROP TRIGGER IF EXISTS trg_dashboard_projection_strategy_run ON strategy_market_paper_runs;
CREATE TRIGGER trg_dashboard_projection_strategy_run
AFTER INSERT OR UPDATE OR DELETE ON strategy_market_paper_runs
FOR EACH ROW EXECUTE FUNCTION public.queue_dashboard_run_projection_event();

DROP TRIGGER IF EXISTS trg_dashboard_projection_paper_position ON paper_positions;
CREATE TRIGGER trg_dashboard_projection_paper_position
AFTER INSERT OR UPDATE OR DELETE ON paper_positions
FOR EACH ROW EXECUTE FUNCTION public.queue_dashboard_paper_position_projection_event();

DROP TRIGGER IF EXISTS trg_dashboard_projection_paper_settlement ON paper_position_settlements;
CREATE TRIGGER trg_dashboard_projection_paper_settlement
AFTER INSERT OR UPDATE OR DELETE ON paper_position_settlements
FOR EACH ROW EXECUTE FUNCTION public.queue_dashboard_paper_settlement_projection_event();

DROP TRIGGER IF EXISTS trg_dashboard_projection_live_order ON live_orders;
CREATE TRIGGER trg_dashboard_projection_live_order
AFTER INSERT OR UPDATE OR DELETE ON live_orders
FOR EACH ROW EXECUTE FUNCTION public.queue_dashboard_live_order_projection_event();
""";
}
