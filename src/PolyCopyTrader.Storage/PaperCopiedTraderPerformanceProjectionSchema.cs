namespace PolyCopyTrader.Storage;

public static class PaperCopiedTraderPerformanceProjectionSchema
{
    public const string SchemaSql = """
CREATE TABLE IF NOT EXISTS paper_copied_trader_performance_refresh_queue (
    copied_trader_wallet text PRIMARY KEY,
    priority integer NOT NULL,
    requested_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    source_kind text NOT NULL,
    CONSTRAINT ck_paper_copied_trader_performance_refresh_queue_wallet
        CHECK (btrim(copied_trader_wallet) <> '')
);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_copied_trader_performance_refresh_queue_pick
ON paper_copied_trader_performance_refresh_queue (priority DESC, requested_at_utc, copied_trader_wallet);

CREATE TABLE IF NOT EXISTS paper_copied_trader_performance_refresh_inflight (
    copied_trader_wallet text PRIMARY KEY,
    priority integer NOT NULL,
    requested_at_utc timestamptz NOT NULL,
    source_kind text NOT NULL,
    work_kind text NOT NULL,
    claimed_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_paper_copied_trader_performance_refresh_inflight_wallet
        CHECK (btrim(copied_trader_wallet) <> ''),
    CONSTRAINT ck_paper_copied_trader_performance_refresh_inflight_work_kind
        CHECK (work_kind IN ('high_priority', 'reconciliation')),
    CONSTRAINT ck_paper_copied_trader_performance_refresh_inflight_priority
        CHECK (
            (work_kind = 'high_priority' AND priority > 0)
            OR (work_kind = 'reconciliation' AND priority <= 0)
        )
);

CREATE TABLE IF NOT EXISTS paper_copied_trader_performance_projection_control (
    singleton_id smallint PRIMARY KEY DEFAULT 1,
    reconciliation_cursor_wallet text NULL,
    reconciliation_cycle bigint NOT NULL DEFAULT 0,
    last_cycle_completed_at_utc timestamptz NULL,
    updated_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_paper_copied_trader_performance_projection_control_singleton CHECK (singleton_id = 1)
);

INSERT INTO paper_copied_trader_performance_projection_control (singleton_id)
VALUES (1)
ON CONFLICT (singleton_id) DO NOTHING;

CREATE OR REPLACE FUNCTION public.enqueue_paper_copied_trader_performance_wallet(
    target_wallet text,
    target_source_kind text)
RETURNS void
LANGUAGE plpgsql
AS $function$
BEGIN
    IF btrim(COALESCE(target_wallet, '')) = '' THEN
        RETURN;
    END IF;

    INSERT INTO paper_copied_trader_performance_refresh_queue (
        copied_trader_wallet,
        priority,
        requested_at_utc,
        source_kind)
    VALUES (
        target_wallet,
        100,
        clock_timestamp(),
        target_source_kind)
    ON CONFLICT (copied_trader_wallet) DO UPDATE SET
        priority = EXCLUDED.priority,
        requested_at_utc = EXCLUDED.requested_at_utc,
        source_kind = EXCLUDED.source_kind
    WHERE paper_copied_trader_performance_refresh_queue.priority < EXCLUDED.priority;
END;
$function$;

CREATE OR REPLACE FUNCTION public.queue_paper_copied_trader_performance_order()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    IF TG_OP <> 'INSERT' THEN
        PERFORM public.enqueue_paper_copied_trader_performance_wallet(
            OLD.copied_trader_wallet,
            'paper_order');
    END IF;

    IF TG_OP <> 'DELETE' THEN
        PERFORM public.enqueue_paper_copied_trader_performance_wallet(
            NEW.copied_trader_wallet,
            'paper_order');
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$function$;

CREATE OR REPLACE FUNCTION public.queue_paper_copied_trader_performance_fill()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    old_wallet text;
    new_wallet text;
BEGIN
    IF TG_OP = 'UPDATE'
       AND OLD.paper_order_id IS NOT DISTINCT FROM NEW.paper_order_id
       AND OLD.price IS NOT DISTINCT FROM NEW.price
       AND OLD.size_shares IS NOT DISTINCT FROM NEW.size_shares
       AND OLD.realized_pnl_usd IS NOT DISTINCT FROM NEW.realized_pnl_usd
       AND OLD.filled_at_utc IS NOT DISTINCT FROM NEW.filled_at_utc THEN
        RETURN NEW;
    END IF;

    IF TG_OP <> 'INSERT' THEN
        SELECT paper_order.copied_trader_wallet
        INTO old_wallet
        FROM paper_orders paper_order
        WHERE paper_order.id = OLD.paper_order_id;

        PERFORM public.enqueue_paper_copied_trader_performance_wallet(
            old_wallet,
            'paper_fill');
    END IF;

    IF TG_OP <> 'DELETE' THEN
        SELECT paper_order.copied_trader_wallet
        INTO new_wallet
        FROM paper_orders paper_order
        WHERE paper_order.id = NEW.paper_order_id;

        PERFORM public.enqueue_paper_copied_trader_performance_wallet(
            new_wallet,
            'paper_fill');
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$function$;

CREATE OR REPLACE FUNCTION public.queue_paper_copied_trader_performance_settlement()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    IF TG_OP = 'UPDATE'
       AND OLD.copied_trader_wallet IS NOT DISTINCT FROM NEW.copied_trader_wallet
       AND OLD.category IS NOT DISTINCT FROM NEW.category
       AND OLD.condition_id IS NOT DISTINCT FROM NEW.condition_id
       AND OLD.settlement_value_usd IS NOT DISTINCT FROM NEW.settlement_value_usd
       AND OLD.realized_pnl_usd IS NOT DISTINCT FROM NEW.realized_pnl_usd
       AND OLD.won IS NOT DISTINCT FROM NEW.won THEN
        RETURN NEW;
    END IF;

    IF TG_OP <> 'INSERT' THEN
        PERFORM public.enqueue_paper_copied_trader_performance_wallet(
            OLD.copied_trader_wallet,
            'paper_settlement');
    END IF;

    IF TG_OP <> 'DELETE' THEN
        PERFORM public.enqueue_paper_copied_trader_performance_wallet(
            NEW.copied_trader_wallet,
            'paper_settlement');
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$function$;

CREATE OR REPLACE FUNCTION public.queue_paper_copied_trader_performance_position_insert()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    INSERT INTO paper_copied_trader_performance_refresh_queue (
        copied_trader_wallet,
        priority,
        requested_at_utc,
        source_kind)
    SELECT
        wallets.copied_trader_wallet,
        100,
        clock_timestamp(),
        'paper_position'
    FROM (
        SELECT DISTINCT position_row.copied_trader_wallet
        FROM new_paper_positions position_row
        WHERE btrim(position_row.copied_trader_wallet) <> ''
    ) wallets
    ORDER BY wallets.copied_trader_wallet COLLATE "C"
    ON CONFLICT (copied_trader_wallet) DO UPDATE SET
        priority = EXCLUDED.priority,
        requested_at_utc = EXCLUDED.requested_at_utc,
        source_kind = EXCLUDED.source_kind
    WHERE paper_copied_trader_performance_refresh_queue.priority < EXCLUDED.priority;

    RETURN NULL;
END;
$function$;

CREATE OR REPLACE FUNCTION public.queue_paper_copied_trader_performance_position_update()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    INSERT INTO paper_copied_trader_performance_refresh_queue (
        copied_trader_wallet,
        priority,
        requested_at_utc,
        source_kind)
    WITH old_projection_values AS (
        SELECT
            id,
            copied_trader_wallet,
            condition_id,
            size_shares,
            unrealized_pnl_usd
        FROM old_paper_positions
    ),
    new_projection_values AS (
        SELECT
            id,
            copied_trader_wallet,
            condition_id,
            size_shares,
            unrealized_pnl_usd
        FROM new_paper_positions
    ),
    changed_projection_values AS (
        (SELECT * FROM old_projection_values EXCEPT SELECT * FROM new_projection_values)
        UNION
        (SELECT * FROM new_projection_values EXCEPT SELECT * FROM old_projection_values)
    )
    SELECT
        wallets.copied_trader_wallet,
        100,
        clock_timestamp(),
        'paper_position'
    FROM (
        SELECT DISTINCT changed.copied_trader_wallet
        FROM changed_projection_values changed
        WHERE btrim(changed.copied_trader_wallet) <> ''
    ) wallets
    ORDER BY wallets.copied_trader_wallet COLLATE "C"
    ON CONFLICT (copied_trader_wallet) DO UPDATE SET
        priority = EXCLUDED.priority,
        requested_at_utc = EXCLUDED.requested_at_utc,
        source_kind = EXCLUDED.source_kind
    WHERE paper_copied_trader_performance_refresh_queue.priority < EXCLUDED.priority;

    RETURN NULL;
END;
$function$;

CREATE OR REPLACE FUNCTION public.queue_paper_copied_trader_performance_position_delete()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    INSERT INTO paper_copied_trader_performance_refresh_queue (
        copied_trader_wallet,
        priority,
        requested_at_utc,
        source_kind)
    SELECT
        wallets.copied_trader_wallet,
        100,
        clock_timestamp(),
        'paper_position'
    FROM (
        SELECT DISTINCT position_row.copied_trader_wallet
        FROM old_paper_positions position_row
        WHERE btrim(position_row.copied_trader_wallet) <> ''
    ) wallets
    ORDER BY wallets.copied_trader_wallet COLLATE "C"
    ON CONFLICT (copied_trader_wallet) DO UPDATE SET
        priority = EXCLUDED.priority,
        requested_at_utc = EXCLUDED.requested_at_utc,
        source_kind = EXCLUDED.source_kind
    WHERE paper_copied_trader_performance_refresh_queue.priority < EXCLUDED.priority;

    RETURN NULL;
END;
$function$;

DROP TRIGGER IF EXISTS trg_paper_copied_trader_performance_order ON public.paper_orders;
CREATE TRIGGER trg_paper_copied_trader_performance_order
AFTER INSERT OR UPDATE OR DELETE ON public.paper_orders
FOR EACH ROW
EXECUTE FUNCTION public.queue_paper_copied_trader_performance_order();

DROP TRIGGER IF EXISTS trg_paper_copied_trader_performance_fill ON public.paper_fills;
CREATE TRIGGER trg_paper_copied_trader_performance_fill
AFTER INSERT OR UPDATE OR DELETE ON public.paper_fills
FOR EACH ROW
EXECUTE FUNCTION public.queue_paper_copied_trader_performance_fill();

DROP TRIGGER IF EXISTS trg_paper_copied_trader_performance_settlement ON public.paper_position_settlements;
CREATE TRIGGER trg_paper_copied_trader_performance_settlement
AFTER INSERT OR UPDATE OR DELETE ON public.paper_position_settlements
FOR EACH ROW
EXECUTE FUNCTION public.queue_paper_copied_trader_performance_settlement();

DROP TRIGGER IF EXISTS trg_paper_copied_trader_performance_position_insert ON public.paper_positions;
CREATE TRIGGER trg_paper_copied_trader_performance_position_insert
AFTER INSERT ON public.paper_positions
REFERENCING NEW TABLE AS new_paper_positions
FOR EACH STATEMENT
EXECUTE FUNCTION public.queue_paper_copied_trader_performance_position_insert();

DROP TRIGGER IF EXISTS trg_paper_copied_trader_performance_position_update ON public.paper_positions;
CREATE TRIGGER trg_paper_copied_trader_performance_position_update
AFTER UPDATE ON public.paper_positions
REFERENCING OLD TABLE AS old_paper_positions NEW TABLE AS new_paper_positions
FOR EACH STATEMENT
EXECUTE FUNCTION public.queue_paper_copied_trader_performance_position_update();

DROP TRIGGER IF EXISTS trg_paper_copied_trader_performance_position_delete ON public.paper_positions;
CREATE TRIGGER trg_paper_copied_trader_performance_position_delete
AFTER DELETE ON public.paper_positions
REFERENCING OLD TABLE AS old_paper_positions
FOR EACH STATEMENT
EXECUTE FUNCTION public.queue_paper_copied_trader_performance_position_delete();
""";
}
