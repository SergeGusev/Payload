using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    private const int DirectPaperSkipCompactionBatchSize = 25_000;

    private const string DirectPaperSkipBusinessBlockerCtes = """
candidate_strategy_keys AS MATERIALIZED (
    SELECT
        candidate.id,
        candidate.condition_id,
        candidate.updated_at_utc,
        strategy.live_enabled_at_utc,
        lower('strategy:' || strategy.code) AS copied_trader_wallet_key
    FROM candidate_batch candidate
    INNER JOIN public.strategies strategy ON strategy.id = candidate.strategy_id
),
blocker_hits AS (
    SELECT candidate_key.id
    FROM candidate_strategy_keys candidate_key
    WHERE candidate_key.live_enabled_at_utc IS NOT NULL
      AND candidate_key.updated_at_utc >= candidate_key.live_enabled_at_utc

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.paper_orders dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.dry_run_orders dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.live_orders dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.paper_live_shadow_decisions dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.market_id = candidate.market_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.paper_live_shadow_decisions dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate_key.id
    FROM candidate_strategy_keys candidate_key
    INNER JOIN public.paper_positions dependency
        ON lower(dependency.copied_trader_wallet) = candidate_key.copied_trader_wallet_key
       AND dependency.condition_id = candidate_key.condition_id

    UNION ALL

    SELECT DISTINCT candidate_key.id
    FROM candidate_strategy_keys candidate_key
    INNER JOIN public.paper_position_settlements dependency
        ON lower(dependency.copied_trader_wallet) = candidate_key.copied_trader_wallet_key
       AND dependency.condition_id = candidate_key.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.paper_copied_leader_positions dependency
        ON dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.paper_copied_leader_activity_events dependency
        ON dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.polymarket_onchain_paper_signal_results dependency
        ON dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.strategy_market_paper_skip_tombstones dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.market_id = candidate.market_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.strategy_skip_archive_market_identities market_identity
        ON market_identity.market_id = candidate.market_id COLLATE "C"
    INNER JOIN public.strategy_market_paper_skip_tombstones_v2 dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.market_identity_id = market_identity.market_identity_id
),
blocked_candidate_ids AS MATERIALIZED (
    SELECT DISTINCT blocker_hit.id
    FROM blocker_hits blocker_hit
)
""";

    private const string DirectStrategyRunInsertSql = """
WITH run_rows AS (
    SELECT *
    FROM jsonb_to_recordset(@RunsJson) AS run_row(
        id uuid,
        strategy_id uuid,
        market_id text,
        condition_id text,
        market_slug text,
        market_title text,
        category text,
        market_start_utc timestamptz,
        market_end_utc timestamptz,
        detected_at_utc timestamptz,
        entry_due_at_utc timestamptz,
        status text,
        selected_asset_id text,
        selected_outcome text,
        entry_price numeric,
        stake_usd numeric,
        size_shares numeric,
        signal_id uuid,
        paper_order_id uuid,
        entered_at_utc timestamptz,
        settlement_price numeric,
        settlement_value_usd numeric,
        realized_pnl_usd numeric,
        settled_at_utc timestamptz,
        skip_reason text,
        created_at_utc timestamptz,
        updated_at_utc timestamptz,
        skip_diagnostics_json text,
        fee_usd numeric,
        fee_accounting_status text,
        fee_liquidity_role text,
        fee_calculation_source text,
        fee_rate numeric,
        fee_exponent integer,
        fee_taker_only boolean,
        fee_calculated_at_utc timestamptz,
        net_realized_pnl_usd numeric
    )
)
INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, skip_diagnostics_json, created_at_utc, updated_at_utc,
    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd
)
SELECT
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, CAST(skip_diagnostics_json AS jsonb), created_at_utc, updated_at_utc,
    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd
FROM run_rows
WHERE NOT EXISTS (
    SELECT 1
    FROM strategy_market_paper_skip_tombstones tombstone
    WHERE tombstone.archived_run_id = run_rows.id
)
AND NOT EXISTS (
    SELECT 1
    FROM strategy_market_paper_skip_tombstones tombstone
    WHERE tombstone.strategy_id = run_rows.strategy_id
      AND tombstone.market_id = run_rows.market_id
)
AND NOT EXISTS (
    SELECT 1
    FROM strategy_market_paper_skip_tombstones_v2 tombstone
    WHERE tombstone.archived_run_id = run_rows.id
)
AND NOT EXISTS (
    SELECT 1
    FROM strategy_skip_archive_market_identities market_identity
    INNER JOIN strategy_market_paper_skip_tombstones_v2 tombstone
        ON tombstone.strategy_id = run_rows.strategy_id
       AND tombstone.market_identity_id = market_identity.market_identity_id
    WHERE market_identity.market_id = run_rows.market_id COLLATE "C"
)
ON CONFLICT (strategy_id, market_id) DO NOTHING
RETURNING id;
""";

    // This fragment is used only by the dormant v2 writer test seams in this
    // compatibility build. Product dispatch continues to call the v1 writers.
    // All callers hold the common exclusive retention gate, which serializes
    // exact dimension resolution without mutating immutable dimension rows.
    private const string CompactSkipArchiveV2DimensionCtes = """
v2_candidates AS MATERIALIZED (
    SELECT candidate.*
    FROM candidates candidate
    WHERE candidate.created_at_utc = candidate.detected_at_utc
),
inserted_market_identities AS (
    INSERT INTO public.strategy_skip_archive_market_identities (market_id)
    SELECT DISTINCT candidate.market_id COLLATE "C"
    FROM v2_candidates candidate
    WHERE NOT EXISTS (
        SELECT 1
        FROM public.strategy_skip_archive_market_identities existing
        WHERE existing.market_id = candidate.market_id COLLATE "C")
    RETURNING market_identity_id, market_id
),
resolved_market_identities AS MATERIALIZED (
    SELECT market_identity.market_identity_id, market_identity.market_id
    FROM public.strategy_skip_archive_market_identities market_identity
    INNER JOIN (
        SELECT DISTINCT candidate.market_id COLLATE "C" AS market_id
        FROM v2_candidates candidate
    ) candidate_market
        ON candidate_market.market_id = market_identity.market_id

    UNION ALL

    SELECT inserted.market_identity_id, inserted.market_id
    FROM inserted_market_identities inserted
),
inserted_metadata_versions AS (
    INSERT INTO public.strategy_skip_archive_market_metadata_versions (
        market_identity_id,
        condition_id,
        market_slug,
        market_title,
        category,
        market_start_utc,
        market_end_utc)
    SELECT DISTINCT
        market_identity.market_identity_id,
        candidate.condition_id COLLATE "C",
        candidate.market_slug COLLATE "C",
        candidate.market_title COLLATE "C",
        candidate.category COLLATE "C",
        candidate.market_start_utc,
        candidate.market_end_utc
    FROM v2_candidates candidate
    INNER JOIN resolved_market_identities market_identity
        ON market_identity.market_id = candidate.market_id COLLATE "C"
    WHERE NOT EXISTS (
        SELECT 1
        FROM public.strategy_skip_archive_market_metadata_versions existing
        WHERE existing.market_identity_id = market_identity.market_identity_id
          AND existing.condition_id = candidate.condition_id COLLATE "C"
          AND existing.market_slug = candidate.market_slug COLLATE "C"
          AND existing.market_title = candidate.market_title COLLATE "C"
          AND existing.category IS NOT DISTINCT FROM candidate.category COLLATE "C"
          AND existing.market_start_utc IS NOT DISTINCT FROM candidate.market_start_utc
          AND existing.market_end_utc IS NOT DISTINCT FROM candidate.market_end_utc)
    RETURNING
        metadata_version_id,
        market_identity_id,
        condition_id,
        market_slug,
        market_title,
        category,
        market_start_utc,
        market_end_utc
),
resolved_metadata_versions AS MATERIALIZED (
    SELECT DISTINCT
        metadata.metadata_version_id,
        metadata.market_identity_id,
        metadata.condition_id,
        metadata.market_slug,
        metadata.market_title,
        metadata.category,
        metadata.market_start_utc,
        metadata.market_end_utc
    FROM public.strategy_skip_archive_market_metadata_versions metadata
    INNER JOIN resolved_market_identities market_identity
        ON market_identity.market_identity_id = metadata.market_identity_id
    INNER JOIN v2_candidates candidate
        ON candidate.market_id COLLATE "C" = market_identity.market_id
       AND candidate.condition_id COLLATE "C" = metadata.condition_id
       AND candidate.market_slug COLLATE "C" = metadata.market_slug
       AND candidate.market_title COLLATE "C" = metadata.market_title
       AND candidate.category COLLATE "C" IS NOT DISTINCT FROM metadata.category
       AND candidate.market_start_utc IS NOT DISTINCT FROM metadata.market_start_utc
       AND candidate.market_end_utc IS NOT DISTINCT FROM metadata.market_end_utc

    UNION ALL

    SELECT
        inserted.metadata_version_id,
        inserted.market_identity_id,
        inserted.condition_id,
        inserted.market_slug,
        inserted.market_title,
        inserted.category,
        inserted.market_start_utc,
        inserted.market_end_utc
    FROM inserted_metadata_versions inserted
),
inserted_skip_reasons AS (
    INSERT INTO public.strategy_skip_archive_reasons (skip_reason)
    SELECT DISTINCT candidate.skip_reason COLLATE "C"
    FROM v2_candidates candidate
    WHERE NOT EXISTS (
        SELECT 1
        FROM public.strategy_skip_archive_reasons existing
        WHERE existing.skip_reason = candidate.skip_reason COLLATE "C")
    RETURNING skip_reason_id, skip_reason
),
resolved_skip_reasons AS MATERIALIZED (
    SELECT reason.skip_reason_id, reason.skip_reason
    FROM public.strategy_skip_archive_reasons reason
    INNER JOIN (
        SELECT DISTINCT candidate.skip_reason COLLATE "C" AS skip_reason
        FROM v2_candidates candidate
    ) candidate_reason
        ON candidate_reason.skip_reason COLLATE "C" = reason.skip_reason

    UNION ALL

    SELECT inserted.skip_reason_id, inserted.skip_reason
    FROM inserted_skip_reasons inserted
),
resolved_v2_candidates AS MATERIALIZED (
    SELECT
        candidate.*,
        market_identity.market_identity_id,
        metadata.metadata_version_id,
        reason.skip_reason_id
    FROM v2_candidates candidate
    INNER JOIN resolved_market_identities market_identity
        ON market_identity.market_id = candidate.market_id COLLATE "C"
    INNER JOIN resolved_metadata_versions metadata
        ON metadata.market_identity_id = market_identity.market_identity_id
       AND metadata.condition_id = candidate.condition_id COLLATE "C"
       AND metadata.market_slug = candidate.market_slug COLLATE "C"
       AND metadata.market_title = candidate.market_title COLLATE "C"
       AND metadata.category IS NOT DISTINCT FROM candidate.category COLLATE "C"
       AND metadata.market_start_utc IS NOT DISTINCT FROM candidate.market_start_utc
       AND metadata.market_end_utc IS NOT DISTINCT FROM candidate.market_end_utc
    INNER JOIN resolved_skip_reasons reason
        ON reason.skip_reason = candidate.skip_reason COLLATE "C"
)
""";

    private const string DirectNewPaperSkipArchiveSql = $$"""
WITH input_rows AS MATERIALIZED (
    SELECT
        input_row.run_json,
        input_row.ordinality
    FROM jsonb_array_elements(@RunsJson) WITH ORDINALITY
        AS input_row(run_json, ordinality)
),
candidate_batch AS MATERIALIZED (
    SELECT
        run_row.id,
        run_row.strategy_id,
        run_row.market_id,
        run_row.condition_id,
        run_row.market_slug,
        run_row.market_title,
        run_row.category,
        run_row.market_start_utc,
        run_row.market_end_utc,
        run_row.detected_at_utc,
        run_row.entry_due_at_utc,
        run_row.selected_asset_id,
        run_row.selected_outcome,
        run_row.stake_usd,
        run_row.skip_reason,
        run_row.created_at_utc,
        run_row.updated_at_utc,
        input_row.run_json,
        input_row.ordinality,
        strategy.live_enabled_at_utc,
        date_trunc('day', run_row.updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
            AS rollup_bucket_start_utc
    FROM input_rows input_row
    CROSS JOIN LATERAL jsonb_to_record(input_row.run_json) AS run_row(
        id uuid,
        strategy_id uuid,
        market_id text,
        condition_id text,
        market_slug text,
        market_title text,
        category text,
        market_start_utc timestamptz,
        market_end_utc timestamptz,
        detected_at_utc timestamptz,
        entry_due_at_utc timestamptz,
        status text,
        selected_asset_id text,
        selected_outcome text,
        entry_price numeric,
        stake_usd numeric,
        size_shares numeric,
        signal_id uuid,
        paper_order_id uuid,
        entered_at_utc timestamptz,
        settlement_price numeric,
        settlement_value_usd numeric,
        realized_pnl_usd numeric,
        settled_at_utc timestamptz,
        skip_reason text,
        created_at_utc timestamptz,
        updated_at_utc timestamptz,
        skip_diagnostics_json text,
        fee_usd numeric,
        fee_accounting_status text,
        fee_liquidity_role text,
        fee_calculation_source text,
        fee_rate numeric,
        fee_exponent integer,
        fee_taker_only boolean,
        fee_calculated_at_utc timestamptz,
        net_realized_pnl_usd numeric
    )
    INNER JOIN public.strategies strategy
        ON strategy.id = run_row.strategy_id
    WHERE run_row.status = 'Skipped'
      AND NOT COALESCE(strategy.live_stakes, false)
      AND NOT EXISTS (
          SELECT 1
          FROM public.strategy_live_retention_guards live_guard
          WHERE live_guard.strategy_id = run_row.strategy_id
      )
      AND NULLIF(btrim(COALESCE(run_row.skip_reason, '')), '') IS NOT NULL
      AND run_row.signal_id IS NULL
      AND run_row.paper_order_id IS NULL
      AND run_row.entered_at_utc IS NULL
      AND run_row.entry_price IS NULL
      AND run_row.size_shares IS NULL
      AND run_row.settlement_price IS NULL
      AND run_row.settlement_value_usd IS NULL
      AND run_row.realized_pnl_usd IS NULL
      AND run_row.settled_at_utc IS NULL
      AND run_row.skip_diagnostics_json IS NULL
      AND run_row.fee_usd = 0
      AND run_row.fee_accounting_status = 'LegacyUnknown'
      AND run_row.fee_liquidity_role = 'Unknown'
      AND run_row.fee_calculation_source = ''
      AND run_row.fee_rate IS NULL
      AND run_row.fee_exponent IS NULL
      AND run_row.fee_taker_only IS NULL
      AND run_row.fee_calculated_at_utc IS NULL
      AND run_row.net_realized_pnl_usd IS NULL
),
{{DirectPaperSkipBusinessBlockerCtes}},
candidates AS MATERIALIZED (
    SELECT DISTINCT ON (candidate.strategy_id, candidate.market_id)
        candidate.*
    FROM candidate_batch candidate
    LEFT JOIN blocked_candidate_ids blocked ON blocked.id = candidate.id
    WHERE blocked.id IS NULL
      AND NOT EXISTS (
          SELECT 1
          FROM public.strategy_market_paper_runs existing_run
          WHERE existing_run.id = candidate.id
             OR (existing_run.strategy_id = candidate.strategy_id
                 AND existing_run.market_id = candidate.market_id)
      )
    ORDER BY candidate.strategy_id, candidate.market_id, candidate.ordinality
),
tombstones AS (
    INSERT INTO public.strategy_market_paper_skip_tombstones (
        strategy_id,
        market_id,
        archived_run_id,
        archived_at_utc,
        archive_format_version,
        condition_id,
        market_slug,
        market_title,
        category,
        market_start_utc,
        market_end_utc,
        detected_at_utc,
        entry_due_at_utc,
        selected_asset_id,
        selected_outcome,
        stake_usd,
        skip_reason,
        run_created_at_utc,
        run_updated_at_utc,
        rollup_bucket_start_utc)
    SELECT
        candidate.strategy_id,
        candidate.market_id,
        candidate.id,
        clock_timestamp(),
        1,
        candidate.condition_id,
        candidate.market_slug,
        candidate.market_title,
        candidate.category,
        candidate.market_start_utc,
        candidate.market_end_utc,
        candidate.detected_at_utc,
        candidate.entry_due_at_utc,
        candidate.selected_asset_id,
        candidate.selected_outcome,
        candidate.stake_usd,
        candidate.skip_reason,
        candidate.created_at_utc,
        candidate.updated_at_utc,
        candidate.rollup_bucket_start_utc
    FROM candidates candidate
    ON CONFLICT (strategy_id, market_id) DO NOTHING
    RETURNING
        strategy_id,
        archived_run_id,
        rollup_bucket_start_utc,
        skip_reason,
        run_updated_at_utc
),
rollups AS (
    INSERT INTO public.strategy_paper_skip_rollups AS existing_rollup (
        strategy_id,
        bucket_start_utc,
        skip_reason,
        run_count,
        first_updated_at_utc,
        last_updated_at_utc,
        created_at_utc,
        updated_at_utc)
    SELECT
        tombstone.strategy_id,
        tombstone.rollup_bucket_start_utc,
        tombstone.skip_reason,
        count(*)::integer,
        min(tombstone.run_updated_at_utc),
        max(tombstone.run_updated_at_utc),
        clock_timestamp(),
        clock_timestamp()
    FROM tombstones tombstone
    GROUP BY
        tombstone.strategy_id,
        tombstone.rollup_bucket_start_utc,
        tombstone.skip_reason
    ON CONFLICT (strategy_id, bucket_start_utc, skip_reason) DO UPDATE SET
        run_count = existing_rollup.run_count + EXCLUDED.run_count,
        first_updated_at_utc = LEAST(
            existing_rollup.first_updated_at_utc,
            EXCLUDED.first_updated_at_utc),
        last_updated_at_utc = GREATEST(
            existing_rollup.last_updated_at_utc,
            EXCLUDED.last_updated_at_utc),
        updated_at_utc = clock_timestamp()
    RETURNING 1
),
projection_events AS (
    INSERT INTO public.dashboard_projection_events (
        source_kind,
        source_id,
        strategy_id,
        operation,
        old_payload,
        new_payload,
        transaction_id)
    SELECT
        'StrategyRun',
        tombstone.archived_run_id,
        tombstone.strategy_id,
        'Insert',
        NULL,
        public.dashboard_projection_run_payload(
            jsonb_populate_record(
                NULL::public.strategy_market_paper_runs,
                candidate.run_json),
            candidate.live_enabled_at_utc),
        pg_current_xact_id()
    FROM tombstones tombstone
    INNER JOIN candidates candidate
        ON candidate.id = tombstone.archived_run_id
    RETURNING source_id
),
queued AS (
    INSERT INTO public.dashboard_projection_reconciliation_queue AS existing_queue (
        strategy_id,
        priority,
        reason,
        requested_at_utc,
        attempt_count,
        next_attempt_at_utc,
        last_error)
    SELECT
        distinct_tombstone.strategy_id,
        50,
        'direct_paper_skip_compaction',
        clock_timestamp(),
        0,
        clock_timestamp(),
        NULL
    FROM (
        SELECT DISTINCT tombstone.strategy_id
        FROM tombstones tombstone
    ) distinct_tombstone
    ON CONFLICT (strategy_id) DO UPDATE SET
        priority = GREATEST(existing_queue.priority, EXCLUDED.priority),
        reason = EXCLUDED.reason,
        requested_at_utc = LEAST(
            existing_queue.requested_at_utc,
            EXCLUDED.requested_at_utc),
        next_attempt_at_utc = LEAST(
            existing_queue.next_attempt_at_utc,
            EXCLUDED.next_attempt_at_utc),
        last_error = NULL
    RETURNING 1
)
SELECT
    COALESCE(
        array_agg(tombstone.archived_run_id ORDER BY tombstone.archived_run_id),
        ARRAY[]::uuid[]),
    (SELECT count(*)::integer FROM candidates),
    (SELECT count(*)::integer FROM tombstones),
    (SELECT count(*)::integer FROM projection_events),
    (SELECT count(*)::integer FROM rollups),
    (SELECT count(*)::integer FROM queued)
FROM tombstones tombstone;
""";

    private const string DirectNewPaperSkipArchiveV2Sql = $$"""
WITH input_rows AS MATERIALIZED (
    SELECT input_row.run_json, input_row.ordinality
    FROM jsonb_array_elements(@RunsJson) WITH ORDINALITY
        AS input_row(run_json, ordinality)
),
candidate_batch AS MATERIALIZED (
    SELECT
        run_row.id,
        run_row.strategy_id,
        run_row.market_id,
        run_row.condition_id,
        run_row.market_slug,
        run_row.market_title,
        run_row.category,
        run_row.market_start_utc,
        run_row.market_end_utc,
        run_row.detected_at_utc,
        run_row.entry_due_at_utc,
        run_row.selected_asset_id,
        run_row.selected_outcome,
        run_row.stake_usd,
        run_row.skip_reason,
        run_row.created_at_utc,
        run_row.updated_at_utc,
        input_row.run_json,
        input_row.ordinality,
        strategy.live_enabled_at_utc,
        date_trunc('day', run_row.updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
            AS rollup_bucket_start_utc
    FROM input_rows input_row
    CROSS JOIN LATERAL jsonb_to_record(input_row.run_json) AS run_row(
        id uuid,
        strategy_id uuid,
        market_id text,
        condition_id text,
        market_slug text,
        market_title text,
        category text,
        market_start_utc timestamptz,
        market_end_utc timestamptz,
        detected_at_utc timestamptz,
        entry_due_at_utc timestamptz,
        status text,
        selected_asset_id text,
        selected_outcome text,
        entry_price numeric,
        stake_usd numeric,
        size_shares numeric,
        signal_id uuid,
        paper_order_id uuid,
        entered_at_utc timestamptz,
        settlement_price numeric,
        settlement_value_usd numeric,
        realized_pnl_usd numeric,
        settled_at_utc timestamptz,
        skip_reason text,
        created_at_utc timestamptz,
        updated_at_utc timestamptz,
        skip_diagnostics_json text,
        fee_usd numeric,
        fee_accounting_status text,
        fee_liquidity_role text,
        fee_calculation_source text,
        fee_rate numeric,
        fee_exponent integer,
        fee_taker_only boolean,
        fee_calculated_at_utc timestamptz,
        net_realized_pnl_usd numeric
    )
    INNER JOIN public.strategies strategy
        ON strategy.id = run_row.strategy_id
    WHERE run_row.status = 'Skipped'
      AND NOT COALESCE(strategy.live_stakes, false)
      AND NOT EXISTS (
          SELECT 1
          FROM public.strategy_live_retention_guards live_guard
          WHERE live_guard.strategy_id = run_row.strategy_id
      )
      AND NULLIF(btrim(COALESCE(run_row.skip_reason, '')), '') IS NOT NULL
      AND run_row.signal_id IS NULL
      AND run_row.paper_order_id IS NULL
      AND run_row.entered_at_utc IS NULL
      AND run_row.entry_price IS NULL
      AND run_row.size_shares IS NULL
      AND run_row.settlement_price IS NULL
      AND run_row.settlement_value_usd IS NULL
      AND run_row.realized_pnl_usd IS NULL
      AND run_row.settled_at_utc IS NULL
      AND run_row.skip_diagnostics_json IS NULL
      AND run_row.fee_usd = 0
      AND run_row.fee_accounting_status = 'LegacyUnknown'
      AND run_row.fee_liquidity_role = 'Unknown'
      AND run_row.fee_calculation_source = ''
      AND run_row.fee_rate IS NULL
      AND run_row.fee_exponent IS NULL
      AND run_row.fee_taker_only IS NULL
      AND run_row.fee_calculated_at_utc IS NULL
      AND run_row.net_realized_pnl_usd IS NULL
),
{{DirectPaperSkipBusinessBlockerCtes}},
candidates AS MATERIALIZED (
    SELECT DISTINCT ON (candidate.strategy_id, candidate.market_id)
        candidate.*
    FROM candidate_batch candidate
    LEFT JOIN blocked_candidate_ids blocked ON blocked.id = candidate.id
    WHERE blocked.id IS NULL
      AND NOT EXISTS (
          SELECT 1
          FROM public.strategy_market_paper_runs existing_run
          WHERE existing_run.id = candidate.id
             OR (existing_run.strategy_id = candidate.strategy_id
                 AND existing_run.market_id = candidate.market_id)
      )
      AND NOT EXISTS (
          SELECT 1
          FROM input_rows other_input
          WHERE other_input.ordinality <> candidate.ordinality
            AND (
                (other_input.run_json ->> 'id')::uuid = candidate.id
                OR (
                    (other_input.run_json ->> 'strategy_id')::uuid = candidate.strategy_id
                    AND other_input.run_json ->> 'market_id' = candidate.market_id
                )
            )
      )
    ORDER BY candidate.strategy_id, candidate.market_id, candidate.ordinality
),
{{CompactSkipArchiveV2DimensionCtes}},
tombstones AS (
    INSERT INTO public.strategy_market_paper_skip_tombstones_v2 (
        strategy_id,
        market_identity_id,
        metadata_version_id,
        archived_run_id,
        detected_at_utc,
        entry_due_at_utc,
        selected_asset_id,
        selected_outcome,
        stake_usd,
        skip_reason_id,
        run_updated_at_utc)
    SELECT
        candidate.strategy_id,
        candidate.market_identity_id,
        candidate.metadata_version_id,
        candidate.id,
        candidate.detected_at_utc,
        candidate.entry_due_at_utc,
        candidate.selected_asset_id,
        candidate.selected_outcome,
        candidate.stake_usd,
        candidate.skip_reason_id,
        candidate.updated_at_utc
    FROM resolved_v2_candidates candidate
    ON CONFLICT DO NOTHING
    RETURNING strategy_id, market_identity_id, archived_run_id, run_updated_at_utc
),
archived_candidates AS MATERIALIZED (
    SELECT candidate.*
    FROM tombstones tombstone
    INNER JOIN resolved_v2_candidates candidate
        ON candidate.id = tombstone.archived_run_id
       AND candidate.strategy_id = tombstone.strategy_id
       AND candidate.market_identity_id = tombstone.market_identity_id
),
rollups AS (
    INSERT INTO public.strategy_paper_skip_rollups AS existing_rollup (
        strategy_id,
        bucket_start_utc,
        skip_reason,
        run_count,
        first_updated_at_utc,
        last_updated_at_utc,
        created_at_utc,
        updated_at_utc)
    SELECT
        candidate.strategy_id,
        candidate.rollup_bucket_start_utc,
        candidate.skip_reason,
        count(*)::integer,
        min(candidate.updated_at_utc),
        max(candidate.updated_at_utc),
        clock_timestamp(),
        clock_timestamp()
    FROM archived_candidates candidate
    GROUP BY candidate.strategy_id, candidate.rollup_bucket_start_utc, candidate.skip_reason
    ON CONFLICT (strategy_id, bucket_start_utc, skip_reason) DO UPDATE SET
        run_count = existing_rollup.run_count + EXCLUDED.run_count,
        first_updated_at_utc = LEAST(
            existing_rollup.first_updated_at_utc,
            EXCLUDED.first_updated_at_utc),
        last_updated_at_utc = GREATEST(
            existing_rollup.last_updated_at_utc,
            EXCLUDED.last_updated_at_utc),
        updated_at_utc = clock_timestamp()
    RETURNING 1
),
projection_events AS (
    INSERT INTO public.dashboard_projection_events (
        source_kind,
        source_id,
        strategy_id,
        operation,
        old_payload,
        new_payload,
        transaction_id)
    SELECT
        'StrategyRun',
        candidate.id,
        candidate.strategy_id,
        'Insert',
        NULL,
        public.dashboard_projection_run_payload(
            jsonb_populate_record(
                NULL::public.strategy_market_paper_runs,
                candidate.run_json),
            candidate.live_enabled_at_utc),
        pg_current_xact_id()
    FROM archived_candidates candidate
    RETURNING source_id
),
queued AS (
    INSERT INTO public.dashboard_projection_reconciliation_queue AS existing_queue (
        strategy_id,
        priority,
        reason,
        requested_at_utc,
        attempt_count,
        next_attempt_at_utc,
        last_error)
    SELECT
        distinct_candidate.strategy_id,
        50,
        'direct_paper_skip_compaction',
        clock_timestamp(),
        0,
        clock_timestamp(),
        NULL
    FROM (
        SELECT DISTINCT candidate.strategy_id
        FROM archived_candidates candidate
    ) distinct_candidate
    ON CONFLICT (strategy_id) DO UPDATE SET
        priority = GREATEST(existing_queue.priority, EXCLUDED.priority),
        reason = EXCLUDED.reason,
        requested_at_utc = LEAST(
            existing_queue.requested_at_utc,
            EXCLUDED.requested_at_utc),
        next_attempt_at_utc = LEAST(
            existing_queue.next_attempt_at_utc,
            EXCLUDED.next_attempt_at_utc),
        last_error = NULL
    WHERE existing_queue.priority < EXCLUDED.priority
       OR existing_queue.reason IS DISTINCT FROM EXCLUDED.reason
       OR existing_queue.requested_at_utc > EXCLUDED.requested_at_utc
       OR existing_queue.next_attempt_at_utc > EXCLUDED.next_attempt_at_utc
       OR existing_queue.last_error IS NOT NULL
    RETURNING 1
)
SELECT
    COALESCE(
        array_agg(tombstone.archived_run_id ORDER BY tombstone.archived_run_id),
        ARRAY[]::uuid[]),
    (SELECT count(*)::integer FROM v2_candidates),
    (SELECT count(*)::integer FROM resolved_v2_candidates),
    (SELECT count(*)::integer FROM tombstones),
    (SELECT count(*)::integer FROM projection_events),
    (SELECT count(*)::integer FROM rollups),
    (SELECT count(*)::integer FROM queued)
FROM tombstones tombstone;
""";

    public Task<IReadOnlySet<Guid>> TryAddStrategyMarketPaperRunsAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        bool directPaperSkipCompactionEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!directPaperSkipCompactionEnabled || !ContainsSkippedRun(runs))
        {
            return TryAddStrategyMarketPaperRunsAsync(runs, cancellationToken);
        }

        if (StrategyRunRetentionCapabilities.CompactSkipArchiveV2ProductWritesSupported)
        {
            throw new InvalidOperationException(
                "Compact skipped-run archive v2 product writes are not supported by this compatibility build.");
        }

        return TryAddStrategyMarketPaperRunsWithDirectCompactionAsync(runs, cancellationToken);
    }

    public Task FinalizeStrategyMarketPaperRunAsync(
        StrategyMarketPaperRun run,
        bool directPaperSkipCompactionEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!directPaperSkipCompactionEnabled || !IsSkippedRun(run))
        {
            return UpdateStrategyMarketPaperRunAsync(run, cancellationToken);
        }

        if (StrategyRunRetentionCapabilities.CompactSkipArchiveV2ProductWritesSupported)
        {
            throw new InvalidOperationException(
                "Compact skipped-run archive v2 product writes are not supported by this compatibility build.");
        }

        return FinalizeStrategyMarketPaperRunsWithDirectCompactionAsync([run], cancellationToken);
    }

    public Task FinalizeStrategyMarketPaperRunsAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        bool directPaperSkipCompactionEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!directPaperSkipCompactionEnabled || !ContainsSkippedRun(runs))
        {
            return UpdateStrategyMarketPaperRunsAsync(runs, cancellationToken);
        }

        if (StrategyRunRetentionCapabilities.CompactSkipArchiveV2ProductWritesSupported)
        {
            throw new InvalidOperationException(
                "Compact skipped-run archive v2 product writes are not supported by this compatibility build.");
        }

        return FinalizeStrategyMarketPaperRunsWithDirectCompactionAsync(runs, cancellationToken);
    }

    internal async Task<IReadOnlySet<Guid>> TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await TryAddStrategyMarketPaperRunsWithDirectCompactionV2ForTestsAsync(
                runs,
                cancellationToken);
        }
        catch (PostgresException exception)
            when (IsCompactSkipArchiveV2DimensionCapacityException(exception))
        {
            return await TryAddStrategyMarketPaperRunsWithDirectCompactionAsync(
                runs,
                cancellationToken);
        }
    }

    internal async Task FinalizeStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await FinalizeStrategyMarketPaperRunsWithDirectCompactionV2ForTestsAsync(
                runs,
                cancellationToken);
        }
        catch (PostgresException exception)
            when (IsCompactSkipArchiveV2DimensionCapacityException(exception))
        {
            await FinalizeStrategyMarketPaperRunsWithDirectCompactionAsync(
                runs,
                cancellationToken);
        }
    }

    private async Task<IReadOnlySet<Guid>> TryAddStrategyMarketPaperRunsWithDirectCompactionV2ForTestsAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return new HashSet<Guid>();
        }

        EnsureUnambiguousDirectPaperSkipInput(runs);

        return await ExecuteWithExclusiveStrategyRunRetentionGateAsync(
            async (connection, transaction, token) =>
            {
                var insertedIds = new HashSet<Guid>();
                for (var offset = 0; offset < runs.Count; offset += StrategyMarketPaperRunInsertBatchSize)
                {
                    var count = Math.Min(StrategyMarketPaperRunInsertBatchSize, runs.Count - offset);
                    var batch = new StrategyMarketPaperRun[count];
                    var skippedIds = new HashSet<Guid>();
                    for (var index = 0; index < count; index++)
                    {
                        var run = runs[offset + index];
                        batch[index] = run with
                        {
                            StrategyId = StrategyIds.Normalize(run.StrategyId),
                            SkipDiagnosticsJson = GetPersistedSkipDiagnosticsJson(run)
                        };
                        if (IsSkippedRun(run))
                        {
                            skippedIds.Add(run.Id);
                        }
                    }

                    var archivedIds = await ArchiveNewDirectPaperSkippedRunsV2ForTestsAsync(
                        connection,
                        transaction,
                        batch,
                        token);
                    insertedIds.UnionWith(archivedIds);
                    var v1ArchivedIds = await ArchiveNewDirectPaperSkippedRunsAsync(
                        connection,
                        transaction,
                        batch,
                        token);
                    insertedIds.UnionWith(v1ArchivedIds);

                    await using var command = CreateCommand(connection, DirectStrategyRunInsertSql);
                    command.Transaction = transaction;
                    command.Parameters.Add("RunsJson", NpgsqlDbType.Jsonb).Value =
                        JsonSerializer.Serialize(batch, BulkInsertJsonOptions);
                    var insertedSkippedIds = new List<Guid>();
                    await using (var reader = await command.ExecuteReaderAsync(token))
                    {
                        while (await reader.ReadAsync(token))
                        {
                            var insertedId = reader.GetGuid(0);
                            insertedIds.Add(insertedId);
                            if (skippedIds.Contains(insertedId))
                            {
                                insertedSkippedIds.Add(insertedId);
                            }
                        }
                    }

                    await CompactDirectPaperSkippedRunsV2ForTestsAsync(
                        connection,
                        transaction,
                        insertedSkippedIds,
                        token);
                    await CompactDirectPaperSkippedRunsAsync(
                        connection,
                        transaction,
                        insertedSkippedIds,
                        token);
                }

                return (IReadOnlySet<Guid>)insertedIds;
            },
            cancellationToken);
    }

    private async Task FinalizeStrategyMarketPaperRunsWithDirectCompactionV2ForTestsAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return;
        }

        await ExecuteWithExclusiveStrategyRunRetentionGateAsync(
            async (connection, transaction, token) =>
            {
                await UpdateStrategyMarketPaperRunsBatchAsync(connection, transaction, runs, token);
                await CompactDirectPaperSkippedRunsV2ForTestsAsync(
                    connection,
                    transaction,
                    runs.Where(IsSkippedRun).Select(run => run.Id).Distinct().ToArray(),
                    token);
                await CompactDirectPaperSkippedRunsAsync(
                    connection,
                    transaction,
                    runs.Where(IsSkippedRun).Select(run => run.Id).Distinct().ToArray(),
                    token);
                return true;
            },
            cancellationToken);
    }

    private async Task<IReadOnlySet<Guid>> TryAddStrategyMarketPaperRunsWithDirectCompactionAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return new HashSet<Guid>();
        }

        EnsureUnambiguousDirectPaperSkipInput(runs);

        // Pure input work must not lengthen the global archive/dependency gate.
        // The same frozen JSON is consumed by both archive and native insert.
        var preparedBatches = new List<PreparedDirectRunBatch>();
        foreach (var inputBatch in runs.Chunk(StrategyMarketPaperRunInsertBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = inputBatch.Select(run => run with
            {
                StrategyId = StrategyIds.Normalize(run.StrategyId),
                SkipDiagnosticsJson = GetPersistedSkipDiagnosticsJson(run)
            }).ToArray();
            preparedBatches.Add(new PreparedDirectRunBatch(
                batch,
                batch.Where(IsSkippedRun).Select(run => run.Id).ToHashSet(),
                JsonSerializer.Serialize(batch, BulkInsertJsonOptions)));
        }

        return await ExecuteWithExclusiveStrategyRunRetentionGateAsync(
            async (connection, transaction, token) =>
            {
                var insertedIds = new HashSet<Guid>();
                foreach (var prepared in preparedBatches)
                {
                    var archivedIds = await ArchiveNewDirectPaperSkippedRunsAsync(
                        connection,
                        transaction,
                        prepared.Runs,
                        token,
                        prepared.Json);
                    insertedIds.UnionWith(archivedIds);

                    await using var command = CreateCommand(connection, DirectStrategyRunInsertSql);
                    command.Transaction = transaction;
                    command.Parameters.Add("RunsJson", NpgsqlDbType.Jsonb).Value = prepared.Json;
                    var insertedSkippedIds = new List<Guid>();
                    await using (var reader = await command.ExecuteReaderAsync(token))
                    {
                        while (await reader.ReadAsync(token))
                        {
                            var insertedId = reader.GetGuid(0);
                            insertedIds.Add(insertedId);
                            if (prepared.SkippedIds.Contains(insertedId))
                            {
                                insertedSkippedIds.Add(insertedId);
                            }
                        }
                    }

                    await CompactDirectPaperSkippedRunsAsync(
                        connection,
                        transaction,
                        insertedSkippedIds,
                        token);
                }

                return (IReadOnlySet<Guid>)insertedIds;
            },
            cancellationToken);
    }

    private sealed record PreparedDirectRunBatch(
        StrategyMarketPaperRun[] Runs, HashSet<Guid> SkippedIds, string Json);

    private static void EnsureUnambiguousDirectPaperSkipInput(
        IReadOnlyList<StrategyMarketPaperRun> runs)
    {
        var runIds = new HashSet<Guid>();
        var strategyMarketKeys = new HashSet<(Guid StrategyId, string MarketId)>();
        foreach (var run in runs)
        {
            var strategyId = StrategyIds.Normalize(run.StrategyId);
            if (!runIds.Add(run.Id) ||
                !strategyMarketKeys.Add((strategyId, run.MarketId)))
            {
                throw new InvalidOperationException(
                    "Direct Paper skipped-run compaction requires unique input run IDs " +
                    "and normalized strategy/market keys.");
            }
        }
    }

    private static bool IsCompactSkipArchiveV2DimensionCapacityException(
        PostgresException exception)
    {
        return exception.SqlState == "2200H";
    }

    private static async Task<IReadOnlySet<Guid>> ArchiveNewDirectPaperSkippedRunsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<StrategyMarketPaperRun> runs,
        CancellationToken cancellationToken,
        string? preparedRunsJson = null)
    {
        if (!runs.Any(IsSkippedRun))
        {
            return new HashSet<Guid>();
        }

        await using var command = CreateCommand(connection, DirectNewPaperSkipArchiveSql);
        command.Transaction = transaction;
        command.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
        command.Parameters.Add("RunsJson", NpgsqlDbType.Jsonb).Value =
            preparedRunsJson ?? JsonSerializer.Serialize(runs, BulkInsertJsonOptions);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Direct Paper skipped-run archive returned no batch result.");
        }

        var archivedIds = reader.GetFieldValue<Guid[]>(0).ToHashSet();
        var candidateCount = reader.GetInt32(1);
        var tombstoneCount = reader.GetInt32(2);
        var projectionEventCount = reader.GetInt32(3);
        await reader.DisposeAsync();

        if (candidateCount != tombstoneCount ||
            candidateCount != projectionEventCount ||
            candidateCount != archivedIds.Count)
        {
            throw new InvalidOperationException(
                $"Direct Paper skipped-run archive invariant failed: " +
                $"candidates={candidateCount}, tombstones={tombstoneCount}, " +
                $"projection_events={projectionEventCount}, ids={archivedIds.Count}.");
        }

        return archivedIds;
    }

    private static async Task<IReadOnlySet<Guid>> ArchiveNewDirectPaperSkippedRunsV2ForTestsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<StrategyMarketPaperRun> runs,
        CancellationToken cancellationToken)
    {
        if (!runs.Any(IsSkippedRun))
        {
            return new HashSet<Guid>();
        }

        await using var command = CreateCommand(connection, DirectNewPaperSkipArchiveV2Sql);
        command.Transaction = transaction;
        command.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
        command.Parameters.Add("RunsJson", NpgsqlDbType.Jsonb).Value =
            JsonSerializer.Serialize(runs, BulkInsertJsonOptions);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Direct Paper skipped-run v2 archive returned no batch result.");
        }

        var archivedIds = reader.GetFieldValue<Guid[]>(0).ToHashSet();
        var representableCount = reader.GetInt32(1);
        var resolvedCount = reader.GetInt32(2);
        var tombstoneCount = reader.GetInt32(3);
        var projectionEventCount = reader.GetInt32(4);
        await reader.DisposeAsync();

        if (representableCount != resolvedCount ||
            representableCount != tombstoneCount ||
            representableCount != projectionEventCount ||
            representableCount != archivedIds.Count)
        {
            throw new InvalidOperationException(
                $"Direct Paper skipped-run v2 archive invariant failed: " +
                $"representable={representableCount}, resolved={resolvedCount}, " +
                $"tombstones={tombstoneCount}, projection_events={projectionEventCount}, " +
                $"ids={archivedIds.Count}.");
        }

        return archivedIds;
    }

    private async Task FinalizeStrategyMarketPaperRunsWithDirectCompactionAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return;
        }

        var runsJson = PrepareStrategyRunsJson(runs);
        var skippedIds = runs.Where(IsSkippedRun).Select(run => run.Id).Distinct().ToArray();
        await ExecuteWithExclusiveStrategyRunRetentionGateAsync(
            async (connection, transaction, token) =>
            {
                await UpdateStrategyMarketPaperRunsBatchAsync(connection, transaction, runs, token, runsJson);
                await CompactDirectPaperSkippedRunsAsync(
                    connection,
                    transaction,
                    skippedIds,
                    token);
                return true;
            },
            cancellationToken);
    }

    private async Task AddPaperEntryPersistenceBatchWithDirectCompactionAsync(
        PaperEntryPersistenceBatch batch,
        CancellationToken cancellationToken)
    {
        if (StrategyRunRetentionCapabilities.CompactSkipArchiveV2ProductWritesSupported)
        {
            throw new InvalidOperationException(
                "Compact skipped-run archive v2 product writes are not supported by this compatibility build.");
        }

        // Freeze only pure input payloads here. All dependency checks and writes
        // remain inside the original atomic wallet-lock -> retention-gate scope.
        var signalsJson = PrepareSignalsJson(batch.Signals);
        var positionsJson = PreparePaperPositionsJson(batch.PaperPositions);
        var ordersJson = PreparePaperOrdersJson(batch.PaperOrders);
        var fillsJson = PreparePaperFillsJson(batch.PaperFills);
        var activationsJson = PrepareActivationsJson(batch.CopiedLeaderPositionActivations);
        var runsJson = PrepareStrategyRunsJson(batch.StrategyRuns);
        var skippedIds = batch.StrategyRuns.Where(IsSkippedRun).Select(run => run.Id).Distinct().ToArray();
        await ExecuteWithPaperPositionLocksAndExclusiveStrategyRunRetentionGateAsync(
            batch.PaperPositions,
            batch.PaperOrders.Select(order => order.CopiedTraderWallet).ToArray(),
            async (connection, transaction, token) =>
            {
                await AddSignalsBatchAsync(connection, transaction, batch.Signals, token, signalsJson);
                await UpsertPaperPositionsBatchAsync(connection, transaction, batch.PaperPositions, token, positionsJson);
                await AddPaperOrdersBatchAsync(connection, transaction, batch.PaperOrders, token, ordersJson);
                await AddPaperFillsBatchAsync(connection, transaction, batch.PaperFills, token, fillsJson);
                await ActivatePaperCopiedLeaderPositionsBatchAsync(
                    connection,
                    transaction,
                    batch.CopiedLeaderPositionActivations,
                    token,
                    activationsJson);
                await UpdateStrategyMarketPaperRunsBatchAsync(
                    connection,
                    transaction,
                    batch.StrategyRuns,
                    token,
                    runsJson);
                await CompactDirectPaperSkippedRunsAsync(
                    connection,
                    transaction,
                    skippedIds,
                    token);
                return true;
            },
            cancellationToken);
    }

    private async Task<T> ExecuteWithPaperPositionLocksAndExclusiveStrategyRunRetentionGateAsync<T>(
        IReadOnlyList<PaperPosition> positions,
        IReadOnlyCollection<string> additionalWallets,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var gateHeld = false;
        try
        {
            await using (var transaction = await connection.BeginTransactionAsync(
                             IsolationLevel.ReadCommitted,
                             cancellationToken))
            {
                // Normal Paper position/settlement writers take these locks before
                // the retention-shared trigger. Keep the same global lock order here
                // so direct compaction cannot deadlock with them under load.
                await LockPaperPositionKeysAsync(
                    connection,
                    transaction,
                    positions,
                    additionalWallets,
                    cancellationToken);

                await using (var gateCommand = CreateCommand(
                                 connection,
                                 "SELECT public.lock_strategy_run_retention_transfer();"))
                {
                    gateCommand.Transaction = transaction;
                    gateCommand.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
                    await gateCommand.ExecuteNonQueryAsync(cancellationToken);
                    gateHeld = true;
                }

                await using (var settingsCommand = CreateCommand(
                                 connection,
                                 "SET LOCAL polycopytrader.skip_run_retention_transfer = 'on';"))
                {
                    settingsCommand.Transaction = transaction;
                    await settingsCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                var result = await action(connection, transaction, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
        }
        finally
        {
            await ReleaseExclusiveStrategyRunRetentionGateAsync(connection, gateHeld);
        }
    }

    private static async Task CompactDirectPaperSkippedRunsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyCollection<Guid> candidateRunIds,
        CancellationToken cancellationToken)
    {
        if (candidateRunIds.Count == 0)
        {
            return;
        }

        foreach (var runIdBatch in candidateRunIds
                     .Distinct()
                     .Chunk(DirectPaperSkipCompactionBatchSize))
        {
            await using var command = CreateCommand(
                connection,
                $$"""
WITH candidate_batch AS MATERIALIZED (
    SELECT
        run.id,
        run.strategy_id,
        run.market_id,
        run.condition_id,
        run.market_slug,
        run.market_title,
        run.category,
        run.market_start_utc,
        run.market_end_utc,
        run.detected_at_utc,
        run.entry_due_at_utc,
        run.selected_asset_id,
        run.selected_outcome,
        run.stake_usd,
        run.skip_reason,
        run.created_at_utc,
        run.updated_at_utc,
        date_trunc('day', run.updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
            AS rollup_bucket_start_utc
    FROM public.strategy_market_paper_runs run
    WHERE run.id = ANY(@RunIds)
      AND run.status = 'Skipped'
      AND run.retention_scope = 'PaperOnly'
      AND NULLIF(btrim(COALESCE(run.skip_reason, '')), '') IS NOT NULL
      AND run.signal_id IS NULL
      AND run.paper_order_id IS NULL
      AND run.entered_at_utc IS NULL
      AND run.entry_price IS NULL
      AND run.size_shares IS NULL
      AND run.settlement_price IS NULL
      AND run.settlement_value_usd IS NULL
      AND run.realized_pnl_usd IS NULL
      AND run.settled_at_utc IS NULL
      AND run.skip_diagnostics_json IS NULL
      AND run.fee_usd = 0
      AND run.fee_accounting_status = 'LegacyUnknown'
      AND run.fee_liquidity_role = 'Unknown'
      AND run.fee_calculation_source = ''
      AND run.fee_rate IS NULL
      AND run.fee_exponent IS NULL
      AND run.fee_taker_only IS NULL
      AND run.fee_calculated_at_utc IS NULL
      AND run.net_realized_pnl_usd IS NULL
    ORDER BY run.strategy_id, run.market_id, run.id
    FOR UPDATE OF run
),
{{DirectPaperSkipBusinessBlockerCtes}},
candidates AS MATERIALIZED (
    SELECT candidate.*
    FROM candidate_batch candidate
    LEFT JOIN blocked_candidate_ids blocked ON blocked.id = candidate.id
    WHERE blocked.id IS NULL
    ORDER BY candidate.strategy_id, candidate.market_id, candidate.id
),
tombstones AS (
    INSERT INTO public.strategy_market_paper_skip_tombstones (
        strategy_id,
        market_id,
        archived_run_id,
        archived_at_utc,
        archive_format_version,
        condition_id,
        market_slug,
        market_title,
        category,
        market_start_utc,
        market_end_utc,
        detected_at_utc,
        entry_due_at_utc,
        selected_asset_id,
        selected_outcome,
        stake_usd,
        skip_reason,
        run_created_at_utc,
        run_updated_at_utc,
        rollup_bucket_start_utc)
    SELECT
        candidate.strategy_id,
        candidate.market_id,
        candidate.id,
        clock_timestamp(),
        1,
        candidate.condition_id,
        candidate.market_slug,
        candidate.market_title,
        candidate.category,
        candidate.market_start_utc,
        candidate.market_end_utc,
        candidate.detected_at_utc,
        candidate.entry_due_at_utc,
        candidate.selected_asset_id,
        candidate.selected_outcome,
        candidate.stake_usd,
        candidate.skip_reason,
        candidate.created_at_utc,
        candidate.updated_at_utc,
        candidate.rollup_bucket_start_utc
    FROM candidates candidate
    ON CONFLICT (strategy_id, market_id) DO NOTHING
    RETURNING
        strategy_id,
        archived_run_id,
        rollup_bucket_start_utc,
        skip_reason,
        run_updated_at_utc
),
rollups AS (
    INSERT INTO public.strategy_paper_skip_rollups AS existing_rollup (
        strategy_id,
        bucket_start_utc,
        skip_reason,
        run_count,
        first_updated_at_utc,
        last_updated_at_utc,
        created_at_utc,
        updated_at_utc)
    SELECT
        tombstone.strategy_id,
        tombstone.rollup_bucket_start_utc,
        tombstone.skip_reason,
        count(*)::integer,
        min(tombstone.run_updated_at_utc),
        max(tombstone.run_updated_at_utc),
        clock_timestamp(),
        clock_timestamp()
    FROM tombstones tombstone
    GROUP BY
        tombstone.strategy_id,
        tombstone.rollup_bucket_start_utc,
        tombstone.skip_reason
    ON CONFLICT (strategy_id, bucket_start_utc, skip_reason) DO UPDATE SET
        run_count = existing_rollup.run_count + EXCLUDED.run_count,
        first_updated_at_utc = LEAST(
            existing_rollup.first_updated_at_utc,
            EXCLUDED.first_updated_at_utc),
        last_updated_at_utc = GREATEST(
            existing_rollup.last_updated_at_utc,
            EXCLUDED.last_updated_at_utc),
        updated_at_utc = clock_timestamp()
    RETURNING 1
),
deleted AS (
    DELETE FROM public.strategy_market_paper_runs run
    USING tombstones tombstone
    WHERE run.id = tombstone.archived_run_id
    RETURNING run.id
),
queued AS (
    INSERT INTO public.dashboard_projection_reconciliation_queue AS existing_queue (
        strategy_id,
        priority,
        reason,
        requested_at_utc,
        attempt_count,
        next_attempt_at_utc,
        last_error)
    SELECT
        distinct_tombstone.strategy_id,
        50,
        'direct_paper_skip_compaction',
        clock_timestamp(),
        0,
        clock_timestamp(),
        NULL
    FROM (
        SELECT DISTINCT tombstone.strategy_id
        FROM tombstones tombstone
    ) distinct_tombstone
    ON CONFLICT (strategy_id) DO UPDATE SET
        priority = GREATEST(existing_queue.priority, EXCLUDED.priority),
        reason = EXCLUDED.reason,
        requested_at_utc = LEAST(
            existing_queue.requested_at_utc,
            EXCLUDED.requested_at_utc),
        next_attempt_at_utc = LEAST(
            existing_queue.next_attempt_at_utc,
            EXCLUDED.next_attempt_at_utc),
        last_error = NULL
    RETURNING 1
)
SELECT
    (SELECT count(*)::integer FROM candidates),
    (SELECT count(*)::integer FROM deleted),
    (SELECT count(*)::integer FROM rollups),
    (SELECT count(*)::integer FROM tombstones),
    (SELECT count(*)::integer FROM queued);
""");
            command.Transaction = transaction;
            command.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
            command.Parameters.Add("RunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = runIdBatch;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Direct Paper skipped-run compaction returned no batch result.");
            }

            var result = new StrategyRunRetentionBatchResult(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4));
            await reader.DisposeAsync();

            if (result.SelectedRows != result.DeletedRows ||
                result.SelectedRows != result.TombstonesChanged)
            {
                throw new InvalidOperationException(
                    $"Direct Paper skipped-run compaction invariant failed: " +
                    $"selected={result.SelectedRows}, deleted={result.DeletedRows}, " +
                    $"tombstones={result.TombstonesChanged}.");
            }
        }
    }

    private static async Task CompactDirectPaperSkippedRunsV2ForTestsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyCollection<Guid> candidateRunIds,
        CancellationToken cancellationToken)
    {
        if (candidateRunIds.Count == 0)
        {
            return;
        }

        foreach (var runIdBatch in candidateRunIds
                     .Distinct()
                     .Chunk(DirectPaperSkipCompactionBatchSize))
        {
            await using var command = CreateCommand(
                connection,
                $$"""
WITH candidate_batch AS MATERIALIZED (
    SELECT
        run.id,
        run.strategy_id,
        run.market_id,
        run.condition_id,
        run.market_slug,
        run.market_title,
        run.category,
        run.market_start_utc,
        run.market_end_utc,
        run.detected_at_utc,
        run.entry_due_at_utc,
        run.selected_asset_id,
        run.selected_outcome,
        run.stake_usd,
        run.skip_reason,
        run.created_at_utc,
        run.updated_at_utc,
        date_trunc('day', run.updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
            AS rollup_bucket_start_utc
    FROM public.strategy_market_paper_runs run
    WHERE run.id = ANY(@RunIds)
      AND run.status = 'Skipped'
      AND run.retention_scope = 'PaperOnly'
      AND NULLIF(btrim(COALESCE(run.skip_reason, '')), '') IS NOT NULL
      AND run.signal_id IS NULL
      AND run.paper_order_id IS NULL
      AND run.entered_at_utc IS NULL
      AND run.entry_price IS NULL
      AND run.size_shares IS NULL
      AND run.settlement_price IS NULL
      AND run.settlement_value_usd IS NULL
      AND run.realized_pnl_usd IS NULL
      AND run.settled_at_utc IS NULL
      AND run.skip_diagnostics_json IS NULL
      AND run.fee_usd = 0
      AND run.fee_accounting_status = 'LegacyUnknown'
      AND run.fee_liquidity_role = 'Unknown'
      AND run.fee_calculation_source = ''
      AND run.fee_rate IS NULL
      AND run.fee_exponent IS NULL
      AND run.fee_taker_only IS NULL
      AND run.fee_calculated_at_utc IS NULL
      AND run.net_realized_pnl_usd IS NULL
    ORDER BY run.strategy_id, run.market_id, run.id
    FOR UPDATE OF run
),
{{DirectPaperSkipBusinessBlockerCtes}},
candidates AS MATERIALIZED (
    SELECT candidate.*
    FROM candidate_batch candidate
    LEFT JOIN blocked_candidate_ids blocked ON blocked.id = candidate.id
    WHERE blocked.id IS NULL
    ORDER BY candidate.strategy_id, candidate.market_id, candidate.id
),
{{CompactSkipArchiveV2DimensionCtes}},
tombstones AS (
    INSERT INTO public.strategy_market_paper_skip_tombstones_v2 (
        strategy_id,
        market_identity_id,
        metadata_version_id,
        archived_run_id,
        detected_at_utc,
        entry_due_at_utc,
        selected_asset_id,
        selected_outcome,
        stake_usd,
        skip_reason_id,
        run_updated_at_utc)
    SELECT
        candidate.strategy_id,
        candidate.market_identity_id,
        candidate.metadata_version_id,
        candidate.id,
        candidate.detected_at_utc,
        candidate.entry_due_at_utc,
        candidate.selected_asset_id,
        candidate.selected_outcome,
        candidate.stake_usd,
        candidate.skip_reason_id,
        candidate.updated_at_utc
    FROM resolved_v2_candidates candidate
    ON CONFLICT DO NOTHING
    RETURNING strategy_id, market_identity_id, archived_run_id
),
archived_candidates AS MATERIALIZED (
    SELECT candidate.*
    FROM tombstones tombstone
    INNER JOIN resolved_v2_candidates candidate
        ON candidate.id = tombstone.archived_run_id
       AND candidate.strategy_id = tombstone.strategy_id
       AND candidate.market_identity_id = tombstone.market_identity_id
),
rollups AS (
    INSERT INTO public.strategy_paper_skip_rollups AS existing_rollup (
        strategy_id,
        bucket_start_utc,
        skip_reason,
        run_count,
        first_updated_at_utc,
        last_updated_at_utc,
        created_at_utc,
        updated_at_utc)
    SELECT
        candidate.strategy_id,
        candidate.rollup_bucket_start_utc,
        candidate.skip_reason,
        count(*)::integer,
        min(candidate.updated_at_utc),
        max(candidate.updated_at_utc),
        clock_timestamp(),
        clock_timestamp()
    FROM archived_candidates candidate
    GROUP BY candidate.strategy_id, candidate.rollup_bucket_start_utc, candidate.skip_reason
    ON CONFLICT (strategy_id, bucket_start_utc, skip_reason) DO UPDATE SET
        run_count = existing_rollup.run_count + EXCLUDED.run_count,
        first_updated_at_utc = LEAST(
            existing_rollup.first_updated_at_utc,
            EXCLUDED.first_updated_at_utc),
        last_updated_at_utc = GREATEST(
            existing_rollup.last_updated_at_utc,
            EXCLUDED.last_updated_at_utc),
        updated_at_utc = clock_timestamp()
    RETURNING 1
),
deleted AS (
    DELETE FROM public.strategy_market_paper_runs run
    USING archived_candidates candidate
    WHERE run.id = candidate.id
    RETURNING run.id
),
queued AS (
    INSERT INTO public.dashboard_projection_reconciliation_queue AS existing_queue (
        strategy_id,
        priority,
        reason,
        requested_at_utc,
        attempt_count,
        next_attempt_at_utc,
        last_error)
    SELECT
        distinct_candidate.strategy_id,
        50,
        'direct_paper_skip_compaction',
        clock_timestamp(),
        0,
        clock_timestamp(),
        NULL
    FROM (
        SELECT DISTINCT candidate.strategy_id
        FROM archived_candidates candidate
    ) distinct_candidate
    ON CONFLICT (strategy_id) DO UPDATE SET
        priority = GREATEST(existing_queue.priority, EXCLUDED.priority),
        reason = EXCLUDED.reason,
        requested_at_utc = LEAST(
            existing_queue.requested_at_utc,
            EXCLUDED.requested_at_utc),
        next_attempt_at_utc = LEAST(
            existing_queue.next_attempt_at_utc,
            EXCLUDED.next_attempt_at_utc),
        last_error = NULL
    WHERE existing_queue.priority < EXCLUDED.priority
       OR existing_queue.reason IS DISTINCT FROM EXCLUDED.reason
       OR existing_queue.requested_at_utc > EXCLUDED.requested_at_utc
       OR existing_queue.next_attempt_at_utc > EXCLUDED.next_attempt_at_utc
       OR existing_queue.last_error IS NOT NULL
    RETURNING 1
)
SELECT
    (SELECT count(*)::integer FROM v2_candidates),
    (SELECT count(*)::integer FROM resolved_v2_candidates),
    (SELECT count(*)::integer FROM archived_candidates),
    (SELECT count(*)::integer FROM deleted),
    (SELECT count(*)::integer FROM rollups),
    (SELECT count(*)::integer FROM queued);
""");
            command.Transaction = transaction;
            command.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
            command.Parameters.Add("RunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = runIdBatch;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "Direct Paper skipped-run v2 compaction returned no batch result.");
            }

            var representableCount = reader.GetInt32(0);
            var resolvedCount = reader.GetInt32(1);
            var tombstoneCount = reader.GetInt32(2);
            var deletedCount = reader.GetInt32(3);
            await reader.DisposeAsync();

            if (representableCount != resolvedCount ||
                representableCount != tombstoneCount ||
                representableCount != deletedCount)
            {
                throw new InvalidOperationException(
                    $"Direct Paper skipped-run v2 compaction invariant failed: " +
                    $"representable={representableCount}, resolved={resolvedCount}, " +
                    $"tombstones={tombstoneCount}, deleted={deletedCount}.");
            }
        }
    }

    private async Task<T> ExecuteWithExclusiveStrategyRunRetentionGateAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var gateHeld = false;
        try
        {
            await using (var gateCommand = CreateCommand(
                connection,
                "SELECT public.lock_strategy_run_retention_transfer();"))
            {
                gateCommand.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
                await gateCommand.ExecuteNonQueryAsync(cancellationToken);
                gateHeld = true;
            }

            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            await using (var settingsCommand = CreateCommand(
                connection,
                "SET LOCAL polycopytrader.skip_run_retention_transfer = 'on';"))
            {
                settingsCommand.Transaction = transaction;
                await settingsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var result = await action(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            await ReleaseExclusiveStrategyRunRetentionGateAsync(connection, gateHeld);
        }
    }

    private static async Task ReleaseExclusiveStrategyRunRetentionGateAsync(
        NpgsqlConnection connection,
        bool gateHeld)
    {
        if (!gateHeld)
        {
            NpgsqlConnection.ClearPool(connection);
            return;
        }

        try
        {
            await using var unlockCommand = CreateCommand(
                connection,
                "SELECT public.unlock_strategy_run_retention_transfer();");
            unlockCommand.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
            var unlocked = await unlockCommand.ExecuteScalarAsync(CancellationToken.None);
            if (unlocked is not true)
            {
                NpgsqlConnection.ClearPool(connection);
                throw new InvalidOperationException(
                    "Strategy-run retention session gate was not held during direct-compaction release.");
            }
        }
        catch
        {
            NpgsqlConnection.ClearPool(connection);
            throw;
        }
    }

    private static bool ContainsSkippedRun(IReadOnlyList<StrategyMarketPaperRun> runs)
    {
        return runs.Any(IsSkippedRun);
    }

    private static bool IsSkippedRun(StrategyMarketPaperRun run)
    {
        return string.Equals(
            run.Status,
            StrategyMarketPaperRunStatuses.Skipped,
            StringComparison.OrdinalIgnoreCase);
    }
}
