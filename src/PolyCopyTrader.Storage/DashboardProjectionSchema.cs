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
    average_price numeric NOT NULL DEFAULT 0,
    fee_usd numeric NOT NULL DEFAULT 0,
    fee_accounting_status text NOT NULL DEFAULT 'LegacyUnknown',
    net_unrealized_pnl_usd numeric NULL,
    updated_at_utc timestamptz NOT NULL
);

ALTER TABLE dashboard_strategy_position_projection_facts
    ADD COLUMN IF NOT EXISTS average_price numeric NOT NULL DEFAULT 0;
ALTER TABLE dashboard_strategy_position_projection_facts
    ADD COLUMN IF NOT EXISTS fee_usd numeric NOT NULL DEFAULT 0;
ALTER TABLE dashboard_strategy_position_projection_facts
    ADD COLUMN IF NOT EXISTS fee_accounting_status text NOT NULL DEFAULT 'LegacyUnknown';
ALTER TABLE dashboard_strategy_position_projection_facts
    ADD COLUMN IF NOT EXISTS net_unrealized_pnl_usd numeric NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_position_projection_facts_strategy
ON dashboard_strategy_position_projection_facts (strategy_id, source_id);

-- Normal strategy-run/dependency writes share this gate and therefore remain
-- concurrent. Retention alone takes the matching exclusive session lock before
-- opening its SERIALIZABLE transaction, so its allowlist snapshot cannot miss
-- a dependency transaction that already passed the gate.
CREATE OR REPLACE FUNCTION public.lock_strategy_run_retention_dependency()
RETURNS void
LANGUAGE sql
VOLATILE
AS $function$
SELECT pg_advisory_xact_lock_shared(1346589778, 1);
$function$;

CREATE OR REPLACE FUNCTION public.lock_strategy_run_retention_transfer()
RETURNS void
LANGUAGE sql
VOLATILE
AS $function$
SELECT pg_advisory_lock(1346589778, 1);
$function$;

CREATE OR REPLACE FUNCTION public.unlock_strategy_run_retention_transfer()
RETURNS boolean
LANGUAGE sql
VOLATILE
AS $function$
SELECT pg_advisory_unlock(1346589778, 1);
$function$;

CREATE OR REPLACE FUNCTION public.lock_strategy_run_dependency_mapping_mutation()
RETURNS void
LANGUAGE sql
VOLATILE
AS $function$
SELECT pg_advisory_xact_lock(1346589778, 1);
$function$;

CREATE OR REPLACE FUNCTION public.coordinate_strategy_run_mutation_with_retention()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    IF current_setting('polycopytrader.skip_run_retention_transfer', true)
       IS DISTINCT FROM 'on' THEN
        PERFORM public.lock_strategy_run_retention_dependency();
    END IF;

    RETURN NULL;
END;
$function$;

DROP TRIGGER IF EXISTS trg_00_coordinate_strategy_run_mutation_with_retention
ON public.strategy_market_paper_runs;
CREATE TRIGGER trg_00_coordinate_strategy_run_mutation_with_retention
BEFORE INSERT OR UPDATE OR DELETE ON public.strategy_market_paper_runs
FOR EACH STATEMENT
EXECUTE FUNCTION public.coordinate_strategy_run_mutation_with_retention();

CREATE OR REPLACE FUNCTION public.suppress_dashboard_strategy_run_projection(
    target_run_id uuid)
RETURNS void
LANGUAGE plpgsql
AS $function$
DECLARE
    suppressed_ids text := current_setting(
        'polycopytrader.suppressed_run_projection_ids', true);
BEGIN
    IF suppressed_ids IS NULL OR suppressed_ids = '' THEN
        PERFORM set_config(
            'polycopytrader.suppressed_run_projection_ids',
            target_run_id::text,
            true);
    ELSIF NOT (
        target_run_id = ANY(string_to_array(suppressed_ids, ',')::uuid[])) THEN
        PERFORM set_config(
            'polycopytrader.suppressed_run_projection_ids',
            suppressed_ids || ',' || target_run_id::text,
            true);
    END IF;
END;
$function$;

CREATE OR REPLACE FUNCTION public.restore_archived_strategy_runs_for_dependency(
    target_strategy_id uuid,
    target_condition_id text,
    target_market_id text,
    match_market_or_condition boolean,
    promote_live_or_shadow boolean)
RETURNS integer
LANGUAGE plpgsql
AS $function$
DECLARE
    archived record;
    rollup_count integer;
    rollup_first_updated_at_utc timestamptz;
    rollup_last_updated_at_utc timestamptz;
    remaining_count integer;
    remaining_first_updated_at_utc timestamptz;
    remaining_last_updated_at_utc timestamptz;
    expected_first_updated_at_utc timestamptz;
    expected_last_updated_at_utc timestamptz;
    affected_rows integer;
    restored_count integer := 0;
BEGIN
    IF current_setting('transaction_isolation') <> 'read committed' THEN
        RAISE EXCEPTION
            'Late strategy-run dependency restoration requires READ COMMITTED; current isolation is %.',
            current_setting('transaction_isolation')
            USING ERRCODE = '0A000';
    END IF;

    PERFORM public.lock_strategy_run_retention_dependency();

    -- A legacy/incomplete tombstone does not contain enough data to prove that
    -- the dependency is unrelated or to reconstruct the raw run. Fail closed:
    -- for a strategy-scoped write, any such row for that strategy is a possible
    -- match; for a condition-only global write, any such row is a possible match.
    IF EXISTS (
        SELECT 1
        FROM public.strategy_market_paper_skip_tombstones tombstone
        WHERE tombstone.archive_format_version IS DISTINCT FROM 1
          AND (target_strategy_id IS NULL
               OR tombstone.strategy_id = target_strategy_id)
          AND (target_condition_id IS NOT NULL
               OR (target_market_id IS NOT NULL
                   AND tombstone.market_id = target_market_id))
    ) THEN
        RAISE EXCEPTION
            'Cannot safely persist a strategy-run dependency while a possible legacy/incomplete tombstone exists.'
            USING ERRCODE = '55000';
    END IF;

    FOR archived IN
        SELECT tombstone.*
        FROM public.strategy_market_paper_skip_tombstones tombstone
        WHERE tombstone.archive_format_version = 1
          AND (target_strategy_id IS NULL
               OR tombstone.strategy_id = target_strategy_id)
          AND (
              (
                  match_market_or_condition
                  AND (
                      (target_condition_id IS NOT NULL
                       AND tombstone.condition_id = target_condition_id)
                      OR (target_market_id IS NOT NULL
                          AND tombstone.market_id = target_market_id)
                  )
              )
              OR (
                  NOT match_market_or_condition
                  AND target_condition_id IS NOT NULL
                  AND tombstone.condition_id = target_condition_id
              )
          )
        ORDER BY tombstone.strategy_id, tombstone.market_id
        FOR UPDATE
    LOOP
        IF EXISTS (
            SELECT 1
            FROM public.strategy_market_paper_runs run
            WHERE run.id = archived.archived_run_id
               OR (run.strategy_id = archived.strategy_id
                   AND run.market_id = archived.market_id)
        ) THEN
            RAISE EXCEPTION
                'Cannot restore archived strategy run %: a raw row already exists.',
                archived.archived_run_id;
        END IF;

        SELECT
            rollup.run_count,
            rollup.first_updated_at_utc,
            rollup.last_updated_at_utc
        INTO
            rollup_count,
            rollup_first_updated_at_utc,
            rollup_last_updated_at_utc
        FROM public.strategy_paper_skip_rollups rollup
        WHERE rollup.strategy_id = archived.strategy_id
          AND rollup.bucket_start_utc = archived.rollup_bucket_start_utc
          AND rollup.skip_reason = archived.skip_reason
        FOR UPDATE;

        IF NOT FOUND THEN
            RAISE EXCEPTION
                'Cannot restore archived strategy run %: rollup group is missing.',
                archived.archived_run_id;
        END IF;

        SELECT
            count(*)::integer,
            min(tombstone.run_updated_at_utc),
            max(tombstone.run_updated_at_utc)
        INTO
            remaining_count,
            remaining_first_updated_at_utc,
            remaining_last_updated_at_utc
        FROM public.strategy_market_paper_skip_tombstones tombstone
        WHERE tombstone.archive_format_version = 1
          AND tombstone.strategy_id = archived.strategy_id
          AND tombstone.rollup_bucket_start_utc = archived.rollup_bucket_start_utc
          AND tombstone.skip_reason = archived.skip_reason
          AND tombstone.archived_run_id <> archived.archived_run_id;

        expected_first_updated_at_utc := CASE
            WHEN remaining_count = 0 THEN archived.run_updated_at_utc
            ELSE LEAST(archived.run_updated_at_utc, remaining_first_updated_at_utc)
        END;
        expected_last_updated_at_utc := CASE
            WHEN remaining_count = 0 THEN archived.run_updated_at_utc
            ELSE GREATEST(archived.run_updated_at_utc, remaining_last_updated_at_utc)
        END;

        IF rollup_count <> remaining_count + 1
           OR rollup_first_updated_at_utc IS DISTINCT FROM expected_first_updated_at_utc
           OR rollup_last_updated_at_utc IS DISTINCT FROM expected_last_updated_at_utc THEN
            RAISE EXCEPTION
                'Cannot restore archived strategy run %: rollup/archive invariant mismatch.',
                archived.archived_run_id;
        END IF;

        PERFORM public.suppress_dashboard_strategy_run_projection(
            archived.archived_run_id);

        DELETE FROM public.strategy_market_paper_skip_tombstones tombstone
        WHERE tombstone.strategy_id = archived.strategy_id
          AND tombstone.market_id = archived.market_id;
        GET DIAGNOSTICS affected_rows = ROW_COUNT;
        IF affected_rows <> 1 THEN
            RAISE EXCEPTION
                'Cannot restore archived strategy run %: tombstone delete changed % rows.',
                archived.archived_run_id,
                affected_rows;
        END IF;

        INSERT INTO public.strategy_market_paper_runs (
            id,
            strategy_id,
            market_id,
            condition_id,
            market_slug,
            market_title,
            category,
            market_start_utc,
            market_end_utc,
            detected_at_utc,
            entry_due_at_utc,
            status,
            selected_asset_id,
            selected_outcome,
            entry_price,
            stake_usd,
            size_shares,
            signal_id,
            paper_order_id,
            entered_at_utc,
            settlement_price,
            settlement_value_usd,
            realized_pnl_usd,
            settled_at_utc,
            skip_reason,
            skip_diagnostics_json,
            retention_scope,
            created_at_utc,
            updated_at_utc)
        VALUES (
            archived.archived_run_id,
            archived.strategy_id,
            archived.market_id,
            archived.condition_id,
            archived.market_slug,
            archived.market_title,
            archived.category,
            archived.market_start_utc,
            archived.market_end_utc,
            archived.detected_at_utc,
            archived.entry_due_at_utc,
            'Skipped',
            archived.selected_asset_id,
            archived.selected_outcome,
            NULL,
            archived.stake_usd,
            NULL,
            NULL,
            NULL,
            NULL,
            NULL,
            NULL,
            NULL,
            NULL,
            archived.skip_reason,
            NULL,
            'PaperOnly',
            archived.run_created_at_utc,
            archived.run_updated_at_utc);
        GET DIAGNOSTICS affected_rows = ROW_COUNT;
        IF affected_rows <> 1 THEN
            RAISE EXCEPTION
                'Cannot restore archived strategy run %: raw insert changed % rows.',
                archived.archived_run_id,
                affected_rows;
        END IF;

        IF promote_live_or_shadow THEN
            UPDATE public.strategy_market_paper_runs run
            SET retention_scope = 'LiveOrShadow'
            WHERE run.id = archived.archived_run_id;
        END IF;

        IF remaining_count = 0 THEN
            DELETE FROM public.strategy_paper_skip_rollups rollup
            WHERE rollup.strategy_id = archived.strategy_id
              AND rollup.bucket_start_utc = archived.rollup_bucket_start_utc
              AND rollup.skip_reason = archived.skip_reason;
        ELSE
            UPDATE public.strategy_paper_skip_rollups rollup
            SET run_count = remaining_count,
                first_updated_at_utc = remaining_first_updated_at_utc,
                last_updated_at_utc = remaining_last_updated_at_utc,
                updated_at_utc = clock_timestamp()
            WHERE rollup.strategy_id = archived.strategy_id
              AND rollup.bucket_start_utc = archived.rollup_bucket_start_utc
              AND rollup.skip_reason = archived.skip_reason;
        END IF;
        GET DIAGNOSTICS affected_rows = ROW_COUNT;
        IF affected_rows <> 1 THEN
            RAISE EXCEPTION
                'Cannot restore archived strategy run %: rollup reversal changed % rows.',
                archived.archived_run_id,
                affected_rows;
        END IF;

        INSERT INTO public.dashboard_projection_reconciliation_queue AS existing_queue (
            strategy_id,
            priority,
            reason,
            requested_at_utc,
            attempt_count,
            next_attempt_at_utc,
            last_error)
        VALUES (
            archived.strategy_id,
            100,
            'late_dependency_run_restore',
            clock_timestamp(),
            0,
            clock_timestamp(),
            NULL)
        ON CONFLICT (strategy_id) DO UPDATE SET
            priority = GREATEST(existing_queue.priority, EXCLUDED.priority),
            reason = EXCLUDED.reason,
            requested_at_utc = LEAST(
                existing_queue.requested_at_utc,
                EXCLUDED.requested_at_utc),
            next_attempt_at_utc = LEAST(
                existing_queue.next_attempt_at_utc,
                EXCLUDED.next_attempt_at_utc),
            last_error = NULL;

        restored_count := restored_count + 1;
    END LOOP;

    RETURN restored_count;
END;
$function$;

CREATE OR REPLACE FUNCTION public.restore_strategy_runs_after_dependency_write()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    new_row jsonb := to_jsonb(NEW);
    target_strategy_id uuid;
    target_condition_id text := NULLIF(new_row ->> 'condition_id', '');
    target_market_id text;
    target_wallet text;
    match_market_or_condition boolean := false;
    promote_live_or_shadow boolean := false;
BEGIN
    IF TG_TABLE_NAME IN ('paper_orders', 'dry_run_orders', 'live_orders',
                         'paper_live_shadow_decisions') THEN
        target_strategy_id := NULLIF(new_row ->> 'strategy_id', '')::uuid;
    ELSIF TG_TABLE_NAME IN ('paper_positions', 'paper_position_settlements') THEN
        -- Lock before resolving the mutable strategy code. A concurrent code
        -- change takes the exclusive side and must become visible first or
        -- recheck these rows after this transaction commits.
        PERFORM public.lock_strategy_run_retention_dependency();
        target_wallet := new_row ->> 'copied_trader_wallet';
        FOR target_strategy_id IN
            SELECT strategy.id
            FROM public.strategies strategy
            WHERE lower(target_wallet) = lower('strategy:' || strategy.code)
            ORDER BY strategy.id
        LOOP
            PERFORM public.restore_archived_strategy_runs_for_dependency(
                target_strategy_id,
                target_condition_id,
                NULL,
                false,
                false);
        END LOOP;

        -- Strategy codes are unique case-sensitively, while the historical
        -- position blocker is case-insensitive. Enumerating every match keeps
        -- restoration exactly aligned even if codes differ only by case.
        RETURN NEW;
    ELSIF TG_TABLE_NAME NOT IN (
        'paper_copied_leader_positions',
        'paper_copied_leader_activity_events',
        'polymarket_onchain_paper_signal_results') THEN
        RAISE EXCEPTION 'Unsupported late-dependency trigger table: %.', TG_TABLE_NAME;
    END IF;

    IF TG_TABLE_NAME = 'paper_live_shadow_decisions' THEN
        target_market_id := NULLIF(new_row ->> 'market_id', '');
        match_market_or_condition := true;
    END IF;

    promote_live_or_shadow := TG_TABLE_NAME IN (
        'live_orders', 'paper_live_shadow_decisions');

    PERFORM public.restore_archived_strategy_runs_for_dependency(
        target_strategy_id,
        target_condition_id,
        target_market_id,
        match_market_or_condition,
        promote_live_or_shadow);

    RETURN NEW;
END;
$function$;

DROP TRIGGER IF EXISTS trg_00_restore_strategy_runs_after_paper_order
ON public.paper_orders;
CREATE TRIGGER trg_00_restore_strategy_runs_after_paper_order
AFTER INSERT OR UPDATE OF strategy_id, condition_id ON public.paper_orders
FOR EACH ROW
EXECUTE FUNCTION public.restore_strategy_runs_after_dependency_write();

DROP TRIGGER IF EXISTS trg_00_restore_strategy_runs_after_dry_run_order
ON public.dry_run_orders;
CREATE TRIGGER trg_00_restore_strategy_runs_after_dry_run_order
AFTER INSERT OR UPDATE OF strategy_id, condition_id ON public.dry_run_orders
FOR EACH ROW
EXECUTE FUNCTION public.restore_strategy_runs_after_dependency_write();

DROP TRIGGER IF EXISTS trg_00_restore_strategy_runs_after_live_order
ON public.live_orders;
CREATE TRIGGER trg_00_restore_strategy_runs_after_live_order
AFTER INSERT OR UPDATE OF strategy_id, condition_id ON public.live_orders
FOR EACH ROW
EXECUTE FUNCTION public.restore_strategy_runs_after_dependency_write();

DROP TRIGGER IF EXISTS trg_00_restore_strategy_runs_after_shadow_decision
ON public.paper_live_shadow_decisions;
CREATE TRIGGER trg_00_restore_strategy_runs_after_shadow_decision
AFTER INSERT OR UPDATE OF strategy_id, market_id, condition_id
ON public.paper_live_shadow_decisions
FOR EACH ROW
EXECUTE FUNCTION public.restore_strategy_runs_after_dependency_write();

DROP TRIGGER IF EXISTS trg_00_restore_strategy_runs_after_paper_position
ON public.paper_positions;
CREATE TRIGGER trg_00_restore_strategy_runs_after_paper_position
AFTER INSERT OR UPDATE OF copied_trader_wallet, condition_id ON public.paper_positions
FOR EACH ROW
EXECUTE FUNCTION public.restore_strategy_runs_after_dependency_write();

DROP TRIGGER IF EXISTS trg_00_restore_strategy_runs_after_paper_settlement
ON public.paper_position_settlements;
CREATE TRIGGER trg_00_restore_strategy_runs_after_paper_settlement
AFTER INSERT OR UPDATE OF copied_trader_wallet, condition_id
ON public.paper_position_settlements
FOR EACH ROW
EXECUTE FUNCTION public.restore_strategy_runs_after_dependency_write();

DROP TRIGGER IF EXISTS trg_00_restore_strategy_runs_after_copied_position
ON public.paper_copied_leader_positions;
CREATE TRIGGER trg_00_restore_strategy_runs_after_copied_position
AFTER INSERT OR UPDATE OF condition_id ON public.paper_copied_leader_positions
FOR EACH ROW
EXECUTE FUNCTION public.restore_strategy_runs_after_dependency_write();

DROP TRIGGER IF EXISTS trg_00_restore_strategy_runs_after_copied_activity
ON public.paper_copied_leader_activity_events;
CREATE TRIGGER trg_00_restore_strategy_runs_after_copied_activity
AFTER INSERT OR UPDATE OF condition_id ON public.paper_copied_leader_activity_events
FOR EACH ROW
EXECUTE FUNCTION public.restore_strategy_runs_after_dependency_write();

DROP TRIGGER IF EXISTS trg_00_restore_strategy_runs_after_onchain_result
ON public.polymarket_onchain_paper_signal_results;
CREATE TRIGGER trg_00_restore_strategy_runs_after_onchain_result
AFTER INSERT OR UPDATE OF condition_id
ON public.polymarket_onchain_paper_signal_results
FOR EACH ROW
EXECUTE FUNCTION public.restore_strategy_runs_after_dependency_write();

CREATE OR REPLACE FUNCTION public.restore_strategy_runs_after_strategy_code_update()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    target_condition_id text;
BEGIN
    IF OLD.code IS NOT DISTINCT FROM NEW.code THEN
        RETURN NEW;
    END IF;

    -- The blocker contract resolves position ownership through the current
    -- strategy code. A code change is therefore itself a dependency mutation.
    -- Code changes and position/settlement writes must not both make their
    -- mapping decision against the other's old committed state.
    PERFORM public.lock_strategy_run_dependency_mapping_mutation();

    FOR target_condition_id IN
        SELECT position_row.condition_id
        FROM public.paper_positions position_row
        WHERE lower(position_row.copied_trader_wallet) =
              lower('strategy:' || NEW.code)
        UNION
        SELECT settlement.condition_id
        FROM public.paper_position_settlements settlement
        WHERE lower(settlement.copied_trader_wallet) =
              lower('strategy:' || NEW.code)
        ORDER BY 1
    LOOP
        PERFORM public.restore_archived_strategy_runs_for_dependency(
            NEW.id,
            target_condition_id,
            NULL,
            false,
            false);
    END LOOP;

    RETURN NEW;
END;
$function$;

DROP TRIGGER IF EXISTS trg_00_restore_strategy_runs_after_strategy_code_update
ON public.strategies;
CREATE TRIGGER trg_00_restore_strategy_runs_after_strategy_code_update
AFTER UPDATE OF code ON public.strategies
FOR EACH ROW
EXECUTE FUNCTION public.restore_strategy_runs_after_strategy_code_update();

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
    CASE WHEN (target_run).status = 'Skipped' AND EXISTS (
        SELECT 1
        FROM public.strategies strategy
        WHERE strategy.id = (target_run).strategy_id
          AND strategy.live_enabled_at_utc IS NOT NULL
          AND (target_run).updated_at_utc >= strategy.live_enabled_at_utc
    ) THEN 'live_skip_projection_dependency' END,
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
    'filled_at_utc', (row_value).filled_at_utc,
    'fee_usd', (row_value).fee_usd,
    'fee_accounting_status', (row_value).fee_accounting_status,
    'net_realized_pnl_usd', (row_value).net_realized_pnl_usd
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
    'live_enabled_at_utc', target_live_enabled_at_utc,
    'fee_usd', (row_value).fee_usd,
    'fee_accounting_status', (row_value).fee_accounting_status,
    'net_realized_pnl_usd', (row_value).net_realized_pnl_usd
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
    'unrealized_pnl_usd', (row_value).unrealized_pnl_usd,
    'average_price', (row_value).average_price,
    'fee_usd', (row_value).fee_usd,
    'fee_accounting_status', (row_value).fee_accounting_status,
    'net_unrealized_pnl_usd', (row_value).net_unrealized_pnl_usd
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
    'won', (row_value).won,
    'fee_usd', (row_value).fee_usd,
    'fee_accounting_status', (row_value).fee_accounting_status,
    'net_realized_pnl_usd', (row_value).net_realized_pnl_usd
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
    'updated_at_utc', (row_value).updated_at_utc,
    'fee_accounting_status', (row_value).fee_accounting_status,
    'net_realized_pnl_usd', (row_value).net_realized_pnl_usd
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
    IF TG_OP = 'UPDATE'
       AND OLD.paper_order_id IS NOT DISTINCT FROM NEW.paper_order_id
       AND OLD.price IS NOT DISTINCT FROM NEW.price
       AND OLD.size_shares IS NOT DISTINCT FROM NEW.size_shares
       AND OLD.realized_pnl_usd IS NOT DISTINCT FROM NEW.realized_pnl_usd
       AND OLD.filled_at_utc IS NOT DISTINCT FROM NEW.filled_at_utc THEN
        IF OLD.fee_usd IS DISTINCT FROM NEW.fee_usd
           OR OLD.fee_accounting_status IS DISTINCT FROM NEW.fee_accounting_status
           OR OLD.fee_liquidity_role IS DISTINCT FROM NEW.fee_liquidity_role
           OR OLD.fee_calculation_source IS DISTINCT FROM NEW.fee_calculation_source
           OR OLD.fee_rate IS DISTINCT FROM NEW.fee_rate
           OR OLD.fee_exponent IS DISTINCT FROM NEW.fee_exponent
           OR OLD.fee_taker_only IS DISTINCT FROM NEW.fee_taker_only
           OR OLD.fee_calculated_at_utc IS DISTINCT FROM NEW.fee_calculated_at_utc
           OR OLD.net_realized_pnl_usd IS DISTINCT FROM NEW.net_realized_pnl_usd THEN
            SELECT paper_order.strategy_id
            INTO new_strategy_id
            FROM paper_orders paper_order
            WHERE paper_order.id = NEW.paper_order_id;

            IF new_strategy_id IS NOT NULL THEN
                INSERT INTO public.dashboard_projection_reconciliation_queue AS existing_queue (
                    strategy_id,
                    priority,
                    reason,
                    requested_at_utc,
                    attempt_count,
                    next_attempt_at_utc,
                    last_error)
                VALUES (
                    new_strategy_id,
                    50,
                    'paper_fill_fee_accounting_changed',
                    clock_timestamp(),
                    0,
                    clock_timestamp(),
                    NULL)
                ON CONFLICT (strategy_id) DO UPDATE SET
                    priority = GREATEST(existing_queue.priority, EXCLUDED.priority),
                    reason = EXCLUDED.reason,
                    requested_at_utc = LEAST(
                        existing_queue.requested_at_utc,
                        EXCLUDED.requested_at_utc),
                    next_attempt_at_utc = LEAST(
                        existing_queue.next_attempt_at_utc,
                        EXCLUDED.next_attempt_at_utc),
                    last_error = NULL;
            END IF;
        END IF;
        RETURN NEW;
    END IF;

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

    IF TG_OP = 'UPDATE'
       AND OLD.strategy_id IS NOT DISTINCT FROM NEW.strategy_id
       AND OLD.status IS NOT DISTINCT FROM NEW.status
       AND OLD.stake_usd IS NOT DISTINCT FROM NEW.stake_usd
       AND OLD.paper_order_id IS NOT DISTINCT FROM NEW.paper_order_id
       AND OLD.entry_due_at_utc IS NOT DISTINCT FROM NEW.entry_due_at_utc
       AND OLD.entered_at_utc IS NOT DISTINCT FROM NEW.entered_at_utc
       AND OLD.realized_pnl_usd IS NOT DISTINCT FROM NEW.realized_pnl_usd
       AND OLD.settled_at_utc IS NOT DISTINCT FROM NEW.settled_at_utc
       AND OLD.skip_reason IS NOT DISTINCT FROM NEW.skip_reason
       AND OLD.updated_at_utc IS NOT DISTINCT FROM NEW.updated_at_utc THEN
        IF OLD.fee_usd IS DISTINCT FROM NEW.fee_usd
           OR OLD.fee_accounting_status IS DISTINCT FROM NEW.fee_accounting_status
           OR OLD.fee_liquidity_role IS DISTINCT FROM NEW.fee_liquidity_role
           OR OLD.fee_calculation_source IS DISTINCT FROM NEW.fee_calculation_source
           OR OLD.fee_rate IS DISTINCT FROM NEW.fee_rate
           OR OLD.fee_exponent IS DISTINCT FROM NEW.fee_exponent
           OR OLD.fee_taker_only IS DISTINCT FROM NEW.fee_taker_only
           OR OLD.fee_calculated_at_utc IS DISTINCT FROM NEW.fee_calculated_at_utc
           OR OLD.net_realized_pnl_usd IS DISTINCT FROM NEW.net_realized_pnl_usd THEN
            INSERT INTO public.dashboard_projection_reconciliation_queue AS existing_queue (
                strategy_id,
                priority,
                reason,
                requested_at_utc,
                attempt_count,
                next_attempt_at_utc,
                last_error)
            VALUES (
                NEW.strategy_id,
                50,
                'strategy_run_fee_accounting_changed',
                clock_timestamp(),
                0,
                clock_timestamp(),
                NULL)
            ON CONFLICT (strategy_id) DO UPDATE SET
                priority = GREATEST(existing_queue.priority, EXCLUDED.priority),
                reason = EXCLUDED.reason,
                requested_at_utc = LEAST(
                    existing_queue.requested_at_utc,
                    EXCLUDED.requested_at_utc),
                next_attempt_at_utc = LEAST(
                    existing_queue.next_attempt_at_utc,
                    EXCLUDED.next_attempt_at_utc),
                last_error = NULL;
        END IF;
        RETURN NEW;
    END IF;

    IF source_row_id = ANY(
        string_to_array(
            NULLIF(current_setting(
                'polycopytrader.suppressed_run_projection_ids', true), ''),
            ',')::uuid[]) THEN
        RETURN COALESCE(NEW, OLD);
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
       AND OLD.unrealized_pnl_usd IS NOT DISTINCT FROM NEW.unrealized_pnl_usd
       AND OLD.average_price IS NOT DISTINCT FROM NEW.average_price THEN
        IF OLD.fee_usd IS DISTINCT FROM NEW.fee_usd
           OR OLD.fee_accounting_status IS DISTINCT FROM NEW.fee_accounting_status
           OR OLD.fee_liquidity_role IS DISTINCT FROM NEW.fee_liquidity_role
           OR OLD.fee_calculation_source IS DISTINCT FROM NEW.fee_calculation_source
           OR OLD.fee_rate IS DISTINCT FROM NEW.fee_rate
           OR OLD.fee_exponent IS DISTINCT FROM NEW.fee_exponent
           OR OLD.fee_taker_only IS DISTINCT FROM NEW.fee_taker_only
           OR OLD.fee_calculated_at_utc IS DISTINCT FROM NEW.fee_calculated_at_utc
           OR OLD.net_unrealized_pnl_usd IS DISTINCT FROM NEW.net_unrealized_pnl_usd THEN
            IF target_strategy_id IS NOT NULL THEN
                INSERT INTO public.dashboard_projection_reconciliation_queue AS existing_queue (
                    strategy_id,
                    priority,
                    reason,
                    requested_at_utc,
                    attempt_count,
                    next_attempt_at_utc,
                    last_error)
                VALUES (
                    target_strategy_id,
                    50,
                    'paper_position_fee_accounting_changed',
                    clock_timestamp(),
                    0,
                    clock_timestamp(),
                    NULL)
                ON CONFLICT (strategy_id) DO UPDATE SET
                    priority = GREATEST(existing_queue.priority, EXCLUDED.priority),
                    reason = EXCLUDED.reason,
                    requested_at_utc = LEAST(
                        existing_queue.requested_at_utc,
                        EXCLUDED.requested_at_utc),
                    next_attempt_at_utc = LEAST(
                        existing_queue.next_attempt_at_utc,
                        EXCLUDED.next_attempt_at_utc),
                    last_error = NULL;
            END IF;
        END IF;
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
    IF TG_OP = 'UPDATE'
       AND OLD.copied_trader_wallet IS NOT DISTINCT FROM NEW.copied_trader_wallet
       AND OLD.cost_basis_usd IS NOT DISTINCT FROM NEW.cost_basis_usd
       AND OLD.realized_pnl_usd IS NOT DISTINCT FROM NEW.realized_pnl_usd
       AND OLD.won IS NOT DISTINCT FROM NEW.won THEN
        IF OLD.fee_usd IS DISTINCT FROM NEW.fee_usd
           OR OLD.fee_accounting_status IS DISTINCT FROM NEW.fee_accounting_status
           OR OLD.fee_liquidity_role IS DISTINCT FROM NEW.fee_liquidity_role
           OR OLD.fee_calculation_source IS DISTINCT FROM NEW.fee_calculation_source
           OR OLD.fee_rate IS DISTINCT FROM NEW.fee_rate
           OR OLD.fee_exponent IS DISTINCT FROM NEW.fee_exponent
           OR OLD.fee_taker_only IS DISTINCT FROM NEW.fee_taker_only
           OR OLD.fee_calculated_at_utc IS DISTINCT FROM NEW.fee_calculated_at_utc
           OR OLD.net_realized_pnl_usd IS DISTINCT FROM NEW.net_realized_pnl_usd THEN
            IF target_strategy_id IS NOT NULL THEN
                INSERT INTO public.dashboard_projection_reconciliation_queue AS existing_queue (
                    strategy_id,
                    priority,
                    reason,
                    requested_at_utc,
                    attempt_count,
                    next_attempt_at_utc,
                    last_error)
                VALUES (
                    target_strategy_id,
                    50,
                    'paper_settlement_fee_accounting_changed',
                    clock_timestamp(),
                    0,
                    clock_timestamp(),
                    NULL)
                ON CONFLICT (strategy_id) DO UPDATE SET
                    priority = GREATEST(existing_queue.priority, EXCLUDED.priority),
                    reason = EXCLUDED.reason,
                    requested_at_utc = LEAST(
                        existing_queue.requested_at_utc,
                        EXCLUDED.requested_at_utc),
                    next_attempt_at_utc = LEAST(
                        existing_queue.next_attempt_at_utc,
                        EXCLUDED.next_attempt_at_utc),
                    last_error = NULL;
            END IF;
        END IF;
        RETURN NEW;
    END IF;

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
