namespace PolyCopyTrader.Storage;

public static class PostgresEthUp8LossDiffStrategySchemaMigration
{
    public const string Id = "0003-eth-up8-lossdiff-gated-children";

    public const string SemanticChecksum = "5e4effb4845ee9896f64f5ebbbed41eef13ecb2d6bc25b61caf0b0e417b6d267";

    public const string Sql = """
CREATE TEMP TABLE eth_up8_lossdiff_rollout_cutoff (
    started_at_utc timestamptz NOT NULL
) ON COMMIT DROP;

INSERT INTO eth_up8_lossdiff_rollout_cutoff (started_at_utc)
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
        'b7c50005-0000-4000-8229-000000000003'::uuid,
        'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_3_plus',
        'ETH 5m Up 8 bps Reference Average Premarket LossDiff 3+',
        'Copy each actual same-market entry of ETH Up or Down 5m Up 8 bps Reference Average Premarket only while its post-rollout consecutive-loss LossDiff is at least 3. Parent losses add one and a parent win resets the counter to zero. The copied child keeps the exact parent outcome, price constraints, amount, order type, and time-in-force.'
    ),
    (
        'b7c50005-0000-4000-8229-000000000016'::uuid,
        'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_16_plus_positive',
        'ETH 5m Up 8 bps Reference Average Premarket LossDiff 16+ Positive',
        'Copy each actual same-market entry of ETH Up or Down 5m Up 8 bps Reference Average Premarket only while its post-rollout nonnegative LossDiff is at least 16. Parent losses add one and a parent win subtracts one with a zero floor. The copied child keeps the exact parent outcome, price constraints, amount, order type, and time-in-force.'
    )
) AS seed(id, code, name, description)
CROSS JOIN eth_up8_lossdiff_rollout_cutoff cutoff
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
    'b7c50005-0000-4000-8137-000000000108'::uuid,
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
        'b7c50005-0000-4000-8230-000000000003'::uuid,
        'b7c50005-0000-4000-8229-000000000003'::uuid,
        'LossDiffReset'
    ),
    (
        'b7c50005-0000-4000-8230-000000000016'::uuid,
        'b7c50005-0000-4000-8229-000000000016'::uuid,
        'LossDiffPositive'
    )
) AS seed(id, child_strategy_id, mode)
CROSS JOIN eth_up8_lossdiff_rollout_cutoff cutoff
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
    'b7c50005-0000-4000-8137-000000000108'::uuid,
    seed.mode,
    seed.threshold,
    0,
    cutoff.started_at_utc,
    NULL,
    NULL,
    NULL,
    cutoff.started_at_utc
FROM (VALUES
    ('b7c50005-0000-4000-8229-000000000003'::uuid, 'LossDiffReset', 3),
    ('b7c50005-0000-4000-8229-000000000016'::uuid, 'LossDiffPositive', 16)
) AS seed(child_strategy_id, mode, threshold)
CROSS JOIN eth_up8_lossdiff_rollout_cutoff cutoff
ON CONFLICT (child_strategy_id) DO NOTHING;
""";
}
