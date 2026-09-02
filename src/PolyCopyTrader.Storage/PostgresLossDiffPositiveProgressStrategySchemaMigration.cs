namespace PolyCopyTrader.Storage;

public static class PostgresLossDiffPositiveProgressStrategySchemaMigration
{
    public const string Id = "0008-eth-lossdiff-positive-progress-34";
    public const string SemanticChecksum = "e224535a4b826e61410fbbe13b55f9a2db357109689ff7d6c66e66fa627daa82";

    public const string Sql = """
SET LOCAL statement_timeout = '15s';
SET LOCAL lock_timeout = '2s';

CREATE TEMP TABLE lossdiff_positive_progress_seed (
    child_id uuid PRIMARY KEY, assignment_id uuid UNIQUE NOT NULL,
    parent_id uuid NOT NULL, code text UNIQUE NOT NULL, name text NOT NULL, cap integer NOT NULL,
    started_at_utc timestamptz NOT NULL DEFAULT transaction_timestamp()
) ON COMMIT DROP;
INSERT INTO lossdiff_positive_progress_seed (child_id, assignment_id, parent_id, code, name, cap)
VALUES
    ('b7c50005-0000-4000-8236-000000000001'::uuid, 'b7c50005-0000-4000-8238-000000000001'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_1', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 1', 1),
    ('b7c50005-0000-4000-8236-000000000002'::uuid, 'b7c50005-0000-4000-8238-000000000002'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_2', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 2', 2),
    ('b7c50005-0000-4000-8236-000000000003'::uuid, 'b7c50005-0000-4000-8238-000000000003'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_3', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 3', 3),
    ('b7c50005-0000-4000-8236-000000000004'::uuid, 'b7c50005-0000-4000-8238-000000000004'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_4', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 4', 4),
    ('b7c50005-0000-4000-8236-000000000005'::uuid, 'b7c50005-0000-4000-8238-000000000005'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_5', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 5', 5),
    ('b7c50005-0000-4000-8236-000000000006'::uuid, 'b7c50005-0000-4000-8238-000000000006'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_6', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 6', 6),
    ('b7c50005-0000-4000-8236-000000000007'::uuid, 'b7c50005-0000-4000-8238-000000000007'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_7', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 7', 7),
    ('b7c50005-0000-4000-8236-000000000008'::uuid, 'b7c50005-0000-4000-8238-000000000008'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_8', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 8', 8),
    ('b7c50005-0000-4000-8236-000000000009'::uuid, 'b7c50005-0000-4000-8238-000000000009'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_9', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 9', 9),
    ('b7c50005-0000-4000-8236-000000000010'::uuid, 'b7c50005-0000-4000-8238-000000000010'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_10', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 10', 10),
    ('b7c50005-0000-4000-8236-000000000011'::uuid, 'b7c50005-0000-4000-8238-000000000011'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_11', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 11', 11),
    ('b7c50005-0000-4000-8236-000000000012'::uuid, 'b7c50005-0000-4000-8238-000000000012'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_12', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 12', 12),
    ('b7c50005-0000-4000-8236-000000000013'::uuid, 'b7c50005-0000-4000-8238-000000000013'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_13', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 13', 13),
    ('b7c50005-0000-4000-8236-000000000014'::uuid, 'b7c50005-0000-4000-8238-000000000014'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_14', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 14', 14),
    ('b7c50005-0000-4000-8236-000000000015'::uuid, 'b7c50005-0000-4000-8238-000000000015'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_15', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 15', 15),
    ('b7c50005-0000-4000-8236-000000000016'::uuid, 'b7c50005-0000-4000-8238-000000000016'::uuid, 'b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_16', 'ETH 5m Up 4 bps Reference Average Premarket LossDiff Positive Progress Cap 16', 16),
    ('b7c50005-0000-4000-8237-000000000001'::uuid, 'b7c50005-0000-4000-8239-000000000001'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_1', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 1', 1),
    ('b7c50005-0000-4000-8237-000000000002'::uuid, 'b7c50005-0000-4000-8239-000000000002'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_2', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 2', 2),
    ('b7c50005-0000-4000-8237-000000000003'::uuid, 'b7c50005-0000-4000-8239-000000000003'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_3', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 3', 3),
    ('b7c50005-0000-4000-8237-000000000004'::uuid, 'b7c50005-0000-4000-8239-000000000004'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_4', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 4', 4),
    ('b7c50005-0000-4000-8237-000000000005'::uuid, 'b7c50005-0000-4000-8239-000000000005'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_5', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 5', 5),
    ('b7c50005-0000-4000-8237-000000000006'::uuid, 'b7c50005-0000-4000-8239-000000000006'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_6', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 6', 6),
    ('b7c50005-0000-4000-8237-000000000007'::uuid, 'b7c50005-0000-4000-8239-000000000007'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_7', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 7', 7),
    ('b7c50005-0000-4000-8237-000000000008'::uuid, 'b7c50005-0000-4000-8239-000000000008'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_8', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 8', 8),
    ('b7c50005-0000-4000-8237-000000000009'::uuid, 'b7c50005-0000-4000-8239-000000000009'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_9', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 9', 9),
    ('b7c50005-0000-4000-8237-000000000010'::uuid, 'b7c50005-0000-4000-8239-000000000010'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_10', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 10', 10),
    ('b7c50005-0000-4000-8237-000000000011'::uuid, 'b7c50005-0000-4000-8239-000000000011'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_11', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 11', 11),
    ('b7c50005-0000-4000-8237-000000000012'::uuid, 'b7c50005-0000-4000-8239-000000000012'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_12', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 12', 12),
    ('b7c50005-0000-4000-8237-000000000013'::uuid, 'b7c50005-0000-4000-8239-000000000013'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_13', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 13', 13),
    ('b7c50005-0000-4000-8237-000000000014'::uuid, 'b7c50005-0000-4000-8239-000000000014'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_14', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 14', 14),
    ('b7c50005-0000-4000-8237-000000000015'::uuid, 'b7c50005-0000-4000-8239-000000000015'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_15', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 15', 15),
    ('b7c50005-0000-4000-8237-000000000016'::uuid, 'b7c50005-0000-4000-8239-000000000016'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_16', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 16', 16),
    ('b7c50005-0000-4000-8237-000000000017'::uuid, 'b7c50005-0000-4000-8239-000000000017'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_17', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 17', 17),
    ('b7c50005-0000-4000-8237-000000000018'::uuid, 'b7c50005-0000-4000-8239-000000000018'::uuid, 'b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket_lossdiff_positive_progress_cap_18', 'ETH 5m Up 8 bps Reference Average Premarket LossDiff Positive Progress Cap 18', 18);

DO $seed$
BEGIN
    IF (SELECT count(*) FROM public.strategies
        WHERE (id, code) IN (
            ('b7c50005-0000-4000-8137-000000000104'::uuid, 'eth_up_down_5m_up_bps_4_fak_premarket'),
            ('b7c50005-0000-4000-8137-000000000108'::uuid, 'eth_up_down_5m_up_bps_8_fak_premarket'))) <> 2 THEN
        RAISE EXCEPTION 'LossDiff Positive Progress exact parents missing or mismatched';
    END IF;
    IF EXISTS (
        SELECT 1 FROM lossdiff_positive_progress_seed x JOIN public.strategies s
          ON s.id=x.child_id OR s.code=x.code OR s.name=x.name
        WHERE s.id<>x.child_id OR s.code<>x.code OR s.name<>x.name
    ) OR EXISTS (
        SELECT 1 FROM lossdiff_positive_progress_seed x JOIN public.strategy_child_parent_assignments a
          ON a.id=x.assignment_id OR a.child_strategy_id=x.child_id
        WHERE a.id<>x.assignment_id OR a.child_strategy_id<>x.child_id OR a.parent_strategy_id<>x.parent_id
           OR a.child_mode<>'LossDiffPositive' OR a.asset_symbol<>'ETH' OR a.lookback_hours<>0
    ) OR EXISTS (
        SELECT 1 FROM lossdiff_positive_progress_seed x JOIN public.strategy_loss_diff_states s
          ON s.child_strategy_id=x.child_id
        WHERE s.parent_strategy_id<>x.parent_id OR s.mode<>'LossDiffPositive' OR s.threshold<>1
    ) THEN
        RAISE EXCEPTION 'LossDiff Positive Progress seed identity conflict';
    END IF;
END
$seed$;

INSERT INTO public.strategies (
    id, code, name, description, enabled, live_stakes, auto_live_paused, paused,
    paper_stake_amount, live_stake_amount, paper_lost_coeff, live_lost_coeff,
    paper_lost_counter, live_lost_counter, live_available_balance, live_enabled_at_utc,
    created_at_utc, updated_at_utc
)
SELECT child_id, code, name,
       'Post-rollout Positive LossDiff starts at zero. Same-market actual parent entry; requested notional = parent invested notional * min(LossDiff,' || cap || '). Zero skips. Wins subtract one with floor zero. Independent FAK depth execution and Net fee accounting; manual Live enablement independent of Paper parent.',
       true, false, false, false, 1, 1, 1, 1, 0, 0, 100, NULL, started_at_utc, started_at_utc
FROM lossdiff_positive_progress_seed
ON CONFLICT (id) DO NOTHING;

INSERT INTO public.strategy_child_parent_assignments (
    id, child_strategy_id, parent_strategy_id, asset_symbol, lookback_hours, child_mode,
    parent_pnl_usd, parent_roi_pct, assigned_at_utc, ended_at_utc, updated_at_utc
)
SELECT assignment_id, child_id, parent_id, 'ETH', 0, 'LossDiffPositive', 0, 0,
       started_at_utc, NULL, started_at_utc
FROM lossdiff_positive_progress_seed
ON CONFLICT (id) DO NOTHING;

INSERT INTO public.strategy_loss_diff_states (
    child_strategy_id, parent_strategy_id, mode, threshold, current_value, started_at_utc,
    last_parent_entered_at_utc, last_parent_run_id, last_reconciled_at_utc, updated_at_utc
)
SELECT child_id, parent_id, 'LossDiffPositive', 1, 0, started_at_utc, NULL, NULL, NULL, started_at_utc
FROM lossdiff_positive_progress_seed
ON CONFLICT (child_strategy_id) DO NOTHING;
""";
}
