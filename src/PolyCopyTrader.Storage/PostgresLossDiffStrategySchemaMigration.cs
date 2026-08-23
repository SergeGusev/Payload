namespace PolyCopyTrader.Storage;

public static class PostgresLossDiffStrategySchemaMigration
{
    public const string Id = "0002-eth-lossdiff-gated-children";

    public const string SemanticChecksum = "ea4acf9b6a444fea242ba86807f2b3580b0d111c7370fa40cda56f0e0cfd3767";

    public const string Sql = """
CREATE TEMP TABLE lossdiff_rollout_cutoff (
    started_at_utc timestamptz NOT NULL
) ON COMMIT DROP;

INSERT INTO lossdiff_rollout_cutoff (started_at_utc)
VALUES (clock_timestamp());

INSERT INTO public.strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    paused,
    paused_until_utc,
    paper_stake_amount,
    live_stake_amount,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    live_available_balance,
    live_enabled_at_utc,
    created_at_utc,
    updated_at_utc
)
SELECT
    seed.id,
    seed.code,
    seed.name,
    seed.description,
    true,
    false,
    false,
    NULL,
    NULL,
    false,
    NULL,
    1.00,
    1.00,
    1.00,
    1.00,
    0,
    0,
    100.00,
    NULL,
    cutoff.started_at_utc,
    cutoff.started_at_utc
FROM (VALUES
    (
        'b7c50005-0000-4000-8225-000000000004'::uuid,
        'eth_up_down_5m_1_diff_confirmed_average_premarket_lossdiff_4_plus',
        'ETH 5m 1 Diff Confirmed Average Premarket LossDiff 4+',
        'Copy each actual same-market entry of ETH Up or Down 5m 1 Diff Confirmed Average Premarket only while its post-rollout consecutive-loss LossDiff is at least 4. Parent losses add one and a parent win resets the counter to zero. The copied child keeps the exact parent outcome, price constraints, amount, order type, and time-in-force.'
    ),
    (
        'b7c50005-0000-4000-8225-000000000013'::uuid,
        'eth_up_down_5m_1_diff_confirmed_average_premarket_lossdiff_13_plus_positive',
        'ETH 5m 1 Diff Confirmed Average Premarket LossDiff 13+ Positive',
        'Copy each actual same-market entry of ETH Up or Down 5m 1 Diff Confirmed Average Premarket only while its post-rollout nonnegative LossDiff is at least 13. Parent losses add one and a parent win subtracts one with a zero floor. The copied child keeps the exact parent outcome, price constraints, amount, order type, and time-in-force.'
    )
) AS seed(id, code, name, description)
CROSS JOIN lossdiff_rollout_cutoff cutoff
ON CONFLICT (id) DO UPDATE
SET code = EXCLUDED.code,
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    enabled = true,
    live_stakes = false,
    auto_live_paused = false,
    auto_live_paused_at_utc = NULL,
    auto_live_pause_window_start_utc = NULL,
    paused = false,
    paused_until_utc = NULL,
    live_enabled_at_utc = NULL,
    updated_at_utc = EXCLUDED.updated_at_utc;

CREATE TABLE public.strategy_loss_diff_states (
    child_strategy_id uuid PRIMARY KEY REFERENCES public.strategies(id),
    parent_strategy_id uuid NOT NULL REFERENCES public.strategies(id),
    mode text NOT NULL,
    threshold integer NOT NULL,
    current_value integer NOT NULL DEFAULT 0,
    started_at_utc timestamptz NOT NULL,
    last_parent_entered_at_utc timestamptz NULL,
    last_parent_run_id uuid NULL,
    last_reconciled_at_utc timestamptz NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_strategy_loss_diff_states_mode
        CHECK (mode IN ('LossDiffReset', 'LossDiffPositive')),
    CONSTRAINT ck_strategy_loss_diff_states_threshold_positive
        CHECK (threshold > 0),
    CONSTRAINT ck_strategy_loss_diff_states_current_nonnegative
        CHECK (current_value >= 0)
);

CREATE INDEX ix_strategy_loss_diff_states_parent
ON public.strategy_loss_diff_states(parent_strategy_id, child_strategy_id);

CREATE TABLE public.strategy_loss_diff_parent_events (
    child_strategy_id uuid NOT NULL REFERENCES public.strategy_loss_diff_states(child_strategy_id) ON DELETE CASCADE,
    parent_run_id uuid NOT NULL,
    parent_entered_at_utc timestamptz NOT NULL,
    parent_settled_at_utc timestamptz NOT NULL,
    won boolean NOT NULL,
    created_at_utc timestamptz NOT NULL,
    PRIMARY KEY (child_strategy_id, parent_run_id)
);

CREATE INDEX ix_strategy_loss_diff_parent_events_order
ON public.strategy_loss_diff_parent_events(child_strategy_id, parent_entered_at_utc, parent_run_id);

INSERT INTO public.strategy_child_parent_assignments (
    id,
    child_strategy_id,
    parent_strategy_id,
    asset_symbol,
    lookback_hours,
    child_mode,
    parent_pnl_usd,
    parent_roi_pct,
    assigned_at_utc,
    ended_at_utc,
    updated_at_utc
)
SELECT
    seed.id,
    seed.child_strategy_id,
    'b7c50005-0000-4000-8204-000000000001'::uuid,
    'ETH',
    0,
    seed.mode,
    0,
    0,
    cutoff.started_at_utc,
    NULL,
    cutoff.started_at_utc
FROM (VALUES
    (
        'b7c50005-0000-4000-8226-000000000004'::uuid,
        'b7c50005-0000-4000-8225-000000000004'::uuid,
        'LossDiffReset'
    ),
    (
        'b7c50005-0000-4000-8226-000000000013'::uuid,
        'b7c50005-0000-4000-8225-000000000013'::uuid,
        'LossDiffPositive'
    )
) AS seed(id, child_strategy_id, mode)
CROSS JOIN lossdiff_rollout_cutoff cutoff
ON CONFLICT (id) DO NOTHING;

INSERT INTO public.strategy_loss_diff_states (
    child_strategy_id,
    parent_strategy_id,
    mode,
    threshold,
    current_value,
    started_at_utc,
    last_parent_entered_at_utc,
    last_parent_run_id,
    last_reconciled_at_utc,
    updated_at_utc
)
SELECT
    seed.child_strategy_id,
    'b7c50005-0000-4000-8204-000000000001'::uuid,
    seed.mode,
    seed.threshold,
    0,
    cutoff.started_at_utc,
    NULL,
    NULL,
    NULL,
    cutoff.started_at_utc
FROM (VALUES
    ('b7c50005-0000-4000-8225-000000000004'::uuid, 'LossDiffReset', 4),
    ('b7c50005-0000-4000-8225-000000000013'::uuid, 'LossDiffPositive', 13)
) AS seed(child_strategy_id, mode, threshold)
CROSS JOIN lossdiff_rollout_cutoff cutoff
ON CONFLICT (child_strategy_id) DO NOTHING;
""";
}
