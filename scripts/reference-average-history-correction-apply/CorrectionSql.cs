namespace ReferenceAverageHistoryCorrectionApply;

internal static class CorrectionSql
{
    public const string AddIdCollisionSql = """
        SELECT (
            (SELECT count(*) FROM correction_adds target JOIN public.signals row ON row.id = target.signal_id) +
            (SELECT count(*) FROM correction_adds target JOIN public.paper_orders row ON row.id = target.order_id) +
            (SELECT count(*) FROM correction_adds target JOIN public.paper_fills row ON row.id = target.fill_id) +
            (SELECT count(*) FROM correction_adds target JOIN public.paper_positions row ON row.id = target.position_id) +
            (SELECT count(*) FROM correction_adds target
             JOIN public.paper_position_settlements row ON row.id = target.settlement_id)
        )::integer;
        """;

    public const string ApplySql = """
        DELETE FROM public.dashboard_projection_events event
        USING correction_target_strategies target
        WHERE event.strategy_id = target.id;

        UPDATE public.strategy_market_paper_runs strategy_run
        SET status = 'Skipped',
            selected_asset_id = NULL,
            selected_outcome = NULL,
            entry_price = NULL,
            stake_usd = target.restored_base_stake_usd,
            size_shares = NULL,
            signal_id = NULL,
            paper_order_id = NULL,
            entered_at_utc = NULL,
            settlement_price = NULL,
            settlement_value_usd = NULL,
            realized_pnl_usd = NULL,
            settled_at_utc = NULL,
            skip_reason = 'reference_average_history_correction_v2_would_skip',
            skip_diagnostics_json = jsonb_build_object(
                'provenance', 'reference_average_history_correction_v2',
                'modeled', false,
                'graph_manifest_sha256', @graph_manifest_sha256,
                'cutoff_utc', @cutoff_utc,
                'classifier_action', target.classifier_action,
                'classifier_reason', target.classifier_reason,
                'signal_preview_manifest_sha256', target.signal_preview_manifest_sha256,
                'replay_classifier_sha256', target.replay_classifier_sha256,
                'replay_evidence', target.replay_evidence_json,
                'replay_evidence_sha256', target.replay_evidence_sha256,
                'original_run_id', strategy_run.id::text,
                'original_paper_order_id', target.order_id::text,
                'original_signal_id', target.signal_id::text,
                'historical_base_stake_usd', target.restored_base_stake_usd,
                'historical_effective_stake_usd', target.historical_effective_stake_usd,
                'historical_target_notional_usd', target.historical_target_notional_usd,
                'historical_stake_sizing_source', target.historical_stake_sizing_source,
                'stake_sizing_proof_sha256', target.stake_sizing_proof_sha256,
                'original_graph_state_sha256', target.graph_state_sha256,
                'original_fill_set_sha256', target.fill_set_sha256),
            updated_at_utc = target.corrected_skipped_updated_at_utc
        FROM correction_main_removals target
        WHERE strategy_run.id = target.run_id;

        DELETE FROM public.strategy_market_paper_runs strategy_run
        USING correction_child_removals target
        WHERE strategy_run.id = target.run_id;

        DELETE FROM public.paper_position_settlements settlement_row
        USING correction_position_keys target
        WHERE settlement_row.copied_trader_wallet = target.copied_trader_wallet
          AND settlement_row.asset_id = target.asset_id;

        DELETE FROM public.paper_positions position_row
        USING correction_position_keys target
        WHERE position_row.copied_trader_wallet = target.copied_trader_wallet
          AND position_row.asset_id = target.asset_id;

        DELETE FROM public.paper_fills paper_fill
        USING correction_target_orders target
        WHERE paper_fill.paper_order_id = target.id;

        DELETE FROM public.signal_rejections rejection
        USING correction_target_signals target
        WHERE rejection.signal_id = target.id;

        DELETE FROM public.paper_orders paper_order
        USING correction_target_orders target
        WHERE paper_order.id = target.id;

        DELETE FROM public.signals signal
        USING correction_target_signals target
        WHERE signal.id = target.id;

        INSERT INTO public.signals (
            id, leader_trade_id, trader_wallet, condition_id, asset_id, outcome,
            leader_price, best_bid, best_ask, spread_abs, spread_pct, lag_seconds,
            score, accepted, decision, proposed_paper_price, proposed_size_shares,
            proposed_notional_usd, created_at_utc, raw_context_json)
        SELECT target.signal_id,
               (payload.value #>> '{signal,leader_trade_id}')::uuid,
               payload.value #>> '{signal,trader_wallet}',
               payload.value #>> '{signal,condition_id}',
               payload.value #>> '{signal,asset_id}',
               payload.value #>> '{signal,outcome}',
               (payload.value #>> '{signal,leader_price}')::numeric,
               (payload.value #>> '{signal,best_bid}')::numeric,
               (payload.value #>> '{signal,best_ask}')::numeric,
               (payload.value #>> '{signal,spread_abs}')::numeric,
               (payload.value #>> '{signal,spread_pct}')::numeric,
               (payload.value #>> '{signal,lag_seconds}')::integer,
               (payload.value #>> '{signal,score}')::integer,
               (payload.value #>> '{signal,accepted}')::boolean,
               payload.value #>> '{signal,decision}',
               (payload.value #>> '{signal,proposed_paper_price}')::numeric,
               (payload.value #>> '{signal,proposed_size_shares}')::numeric,
               (payload.value #>> '{signal,proposed_notional_usd}')::numeric,
               (payload.value #>> '{signal,created_at_utc}')::timestamptz,
               NULLIF(payload.value #> '{signal,raw_context_json}', 'null'::jsonb)
        FROM correction_adds target
        CROSS JOIN LATERAL (SELECT target.modeled_mutation_payload_json::jsonb AS value) payload;

        INSERT INTO public.paper_orders (
            id, signal_id, strategy_id, copied_trader_wallet, status, side,
            asset_id, condition_id, outcome, price, size_shares, notional_usd,
            created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc,
            raw_decision_json, correlation_id, execution_source)
        SELECT target.order_id,
               target.signal_id,
               (payload.value #>> '{paper_order,strategy_id}')::uuid,
               payload.value #>> '{paper_order,copied_trader_wallet}',
               payload.value #>> '{paper_order,status}',
               payload.value #>> '{paper_order,side}',
               payload.value #>> '{paper_order,asset_id}',
               payload.value #>> '{paper_order,condition_id}',
               payload.value #>> '{paper_order,outcome}',
               (payload.value #>> '{paper_order,price}')::numeric,
               (payload.value #>> '{paper_order,size_shares}')::numeric,
               (payload.value #>> '{paper_order,notional_usd}')::numeric,
               (payload.value #>> '{paper_order,created_at_utc}')::timestamptz,
               (payload.value #>> '{paper_order,expires_at_utc}')::timestamptz,
               (payload.value #>> '{paper_order,filled_at_utc}')::timestamptz,
               (payload.value #>> '{paper_order,cancelled_at_utc}')::timestamptz,
               (payload.value #>> '{paper_order,raw_decision_json}')::jsonb,
               (payload.value #>> '{paper_order,correlation_id}')::uuid,
               payload.value #>> '{paper_order,execution_source}'
        FROM correction_adds target
        CROSS JOIN LATERAL (SELECT target.modeled_mutation_payload_json::jsonb AS value) payload;

        INSERT INTO public.paper_fills (
            id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd)
        SELECT target.fill_id,
               target.order_id,
               (payload.value #>> '{paper_fill,price}')::numeric,
               (payload.value #>> '{paper_fill,size_shares}')::numeric,
               (payload.value #>> '{paper_fill,filled_at_utc}')::timestamptz,
               payload.value #>> '{paper_fill,evidence}',
               (payload.value #>> '{paper_fill,realized_pnl_usd}')::numeric
        FROM correction_adds target
        CROSS JOIN LATERAL (SELECT target.modeled_mutation_payload_json::jsonb AS value) payload;

        INSERT INTO public.paper_positions (
            id, copied_trader_wallet, asset_id, condition_id, outcome,
            size_shares, average_price, estimated_value_usd, unrealized_pnl_usd, updated_at_utc)
        SELECT target.position_id,
               payload.value #>> '{paper_position,copied_trader_wallet}',
               payload.value #>> '{paper_position,asset_id}',
               payload.value #>> '{paper_position,condition_id}',
               payload.value #>> '{paper_position,outcome}',
               (payload.value #>> '{paper_position,size_shares}')::numeric,
               (payload.value #>> '{paper_position,average_price}')::numeric,
               (payload.value #>> '{paper_position,estimated_value_usd}')::numeric,
               (payload.value #>> '{paper_position,unrealized_pnl_usd}')::numeric,
               (payload.value #>> '{paper_position,updated_at_utc}')::timestamptz
        FROM correction_adds target
        CROSS JOIN LATERAL (SELECT target.modeled_mutation_payload_json::jsonb AS value) payload;

        INSERT INTO public.paper_position_settlements (
            id, copied_trader_wallet, asset_id, condition_id, outcome,
            winning_asset_id, winning_outcome, category, settled_size_shares,
            average_price, cost_basis_usd, settlement_value_usd, realized_pnl_usd,
            won, settlement_source, settled_at_utc, created_at_utc)
        SELECT target.settlement_id,
               payload.value #>> '{paper_position_settlement,copied_trader_wallet}',
               payload.value #>> '{paper_position_settlement,asset_id}',
               payload.value #>> '{paper_position_settlement,condition_id}',
               payload.value #>> '{paper_position_settlement,outcome}',
               payload.value #>> '{paper_position_settlement,winning_asset_id}',
               payload.value #>> '{paper_position_settlement,winning_outcome}',
               payload.value #>> '{paper_position_settlement,category}',
               (payload.value #>> '{paper_position_settlement,settled_size_shares}')::numeric,
               (payload.value #>> '{paper_position_settlement,average_price}')::numeric,
               (payload.value #>> '{paper_position_settlement,cost_basis_usd}')::numeric,
               (payload.value #>> '{paper_position_settlement,settlement_value_usd}')::numeric,
               (payload.value #>> '{paper_position_settlement,realized_pnl_usd}')::numeric,
               (payload.value #>> '{paper_position_settlement,won}')::boolean,
               payload.value #>> '{paper_position_settlement,settlement_source}',
               (payload.value #>> '{paper_position_settlement,settled_at_utc}')::timestamptz,
               (payload.value #>> '{paper_position_settlement,created_at_utc}')::timestamptz
        FROM correction_adds target
        CROSS JOIN LATERAL (SELECT target.modeled_mutation_payload_json::jsonb AS value) payload;

        UPDATE public.strategy_market_paper_runs strategy_run
        SET status = payload.value #>> '{strategy_market_paper_run_update,status}',
            selected_asset_id = payload.value #>> '{strategy_market_paper_run_update,selected_asset_id}',
            selected_outcome = payload.value #>> '{strategy_market_paper_run_update,selected_outcome}',
            entry_price = (payload.value #>> '{strategy_market_paper_run_update,entry_price}')::numeric,
            stake_usd = (payload.value #>> '{strategy_market_paper_run_update,stake_usd}')::numeric,
            size_shares = (payload.value #>> '{strategy_market_paper_run_update,size_shares}')::numeric,
            signal_id = target.signal_id,
            paper_order_id = target.order_id,
            entered_at_utc = (payload.value #>> '{strategy_market_paper_run_update,entered_at_utc}')::timestamptz,
            settlement_price = (payload.value #>> '{strategy_market_paper_run_update,settlement_price}')::numeric,
            settlement_value_usd = (payload.value #>> '{strategy_market_paper_run_update,settlement_value_usd}')::numeric,
            realized_pnl_usd = (payload.value #>> '{strategy_market_paper_run_update,realized_pnl_usd}')::numeric,
            settled_at_utc = (payload.value #>> '{strategy_market_paper_run_update,settled_at_utc}')::timestamptz,
            skip_reason = payload.value #>> '{strategy_market_paper_run_update,skip_reason}',
            skip_diagnostics_json = NULLIF(
                payload.value #> '{strategy_market_paper_run_update,skip_diagnostics_json}', 'null'::jsonb),
            updated_at_utc = (payload.value #>> '{strategy_market_paper_run_update,updated_at_utc}')::timestamptz
        FROM correction_adds target
        CROSS JOIN LATERAL (SELECT target.modeled_mutation_payload_json::jsonb AS value) payload
        WHERE strategy_run.id = target.run_id;

        INSERT INTO public.dashboard_projection_reconciliation_queue (
            strategy_id, priority, reason, requested_at_utc, attempt_count,
            next_attempt_at_utc, last_error)
        SELECT id, 1000, 'reference_average_history_correction_v2',
               @applied_at_utc, 0, @applied_at_utc, NULL
        FROM correction_target_strategies
        ON CONFLICT (strategy_id) DO UPDATE SET
            priority = GREATEST(public.dashboard_projection_reconciliation_queue.priority, EXCLUDED.priority),
            reason = EXCLUDED.reason,
            requested_at_utc = EXCLUDED.requested_at_utc,
            attempt_count = 0,
            next_attempt_at_utc = EXCLUDED.next_attempt_at_utc,
            last_error = NULL;

        INSERT INTO public.paper_copied_trader_performance_refresh_queue (
            copied_trader_wallet, priority, requested_at_utc, source_kind)
        SELECT copied_trader_wallet, 100, @applied_at_utc,
               'reference_average_history_correction_v2'
        FROM correction_target_wallets
        ON CONFLICT (copied_trader_wallet) DO UPDATE SET
            priority = GREATEST(public.paper_copied_trader_performance_refresh_queue.priority, EXCLUDED.priority),
            requested_at_utc = EXCLUDED.requested_at_utc,
            source_kind = EXCLUDED.source_kind;

        UPDATE public.dashboard_projection_control
        SET initialized = false,
            status = 'PendingHistoryCorrectionBootstrap',
            reconciliation_cursor_strategy_id = NULL,
            bootstrap_started_at_utc = NULL,
            bootstrap_completed_at_utc = NULL,
            last_error = NULL,
            updated_at_utc = @applied_at_utc
        WHERE singleton_id = 1
          AND initialized = true
          AND calculation_version = 2
          AND status = 'Running'
          AND last_error IS NULL;
        """;

    public const string DeleteScopeSql = """
        DELETE FROM public.dashboard_projection_events event
        USING correction_target_strategies target
        WHERE event.strategy_id = target.id;

        DELETE FROM public.dashboard_projection_reconciliation_queue queue
        USING correction_target_strategies target
        WHERE queue.strategy_id = target.id;

        DELETE FROM public.paper_copied_trader_performance_refresh_queue queue
        USING correction_target_wallets target
        WHERE queue.copied_trader_wallet = target.copied_trader_wallet;

        DELETE FROM public.strategy_market_paper_runs strategy_run
        USING correction_target_runs target
        WHERE strategy_run.id = target.id;

        DELETE FROM public.paper_position_settlements settlement_row
        USING correction_position_keys target
        WHERE settlement_row.copied_trader_wallet = target.copied_trader_wallet
          AND settlement_row.asset_id = target.asset_id;

        DELETE FROM public.paper_positions position_row
        USING correction_position_keys target
        WHERE position_row.copied_trader_wallet = target.copied_trader_wallet
          AND position_row.asset_id = target.asset_id;

        DELETE FROM public.paper_fills paper_fill
        USING correction_target_orders target
        WHERE paper_fill.paper_order_id = target.id;

        DELETE FROM public.signal_rejections rejection
        USING correction_target_signals target
        WHERE rejection.signal_id = target.id;

        DELETE FROM public.paper_orders paper_order
        USING correction_target_orders target
        WHERE paper_order.id = target.id;

        DELETE FROM public.signals signal
        USING correction_target_signals target
        WHERE signal.id = target.id;
        """;

    public const string EnqueueReconciliationSql = """
        DELETE FROM public.dashboard_projection_events event
        USING correction_target_strategies target
        WHERE event.strategy_id = target.id;

        INSERT INTO public.dashboard_projection_reconciliation_queue (
            strategy_id, priority, reason, requested_at_utc, attempt_count,
            next_attempt_at_utc, last_error)
        SELECT id, 1000, 'reference_average_history_correction_v2_rollback',
               @applied_at_utc, 0, @applied_at_utc, NULL
        FROM correction_target_strategies
        ON CONFLICT (strategy_id) DO UPDATE SET
            priority = GREATEST(public.dashboard_projection_reconciliation_queue.priority, EXCLUDED.priority),
            reason = EXCLUDED.reason,
            requested_at_utc = EXCLUDED.requested_at_utc,
            attempt_count = 0,
            next_attempt_at_utc = EXCLUDED.next_attempt_at_utc,
            last_error = NULL;

        INSERT INTO public.paper_copied_trader_performance_refresh_queue (
            copied_trader_wallet, priority, requested_at_utc, source_kind)
        SELECT copied_trader_wallet, 100, @applied_at_utc,
               'reference_average_history_correction_v2_rollback'
        FROM correction_target_wallets
        ON CONFLICT (copied_trader_wallet) DO UPDATE SET
            priority = GREATEST(public.paper_copied_trader_performance_refresh_queue.priority, EXCLUDED.priority),
            requested_at_utc = EXCLUDED.requested_at_utc,
            source_kind = EXCLUDED.source_kind;

        UPDATE public.dashboard_projection_control
        SET initialized = false,
            status = 'PendingHistoryCorrectionRollbackBootstrap',
            reconciliation_cursor_strategy_id = NULL,
            bootstrap_started_at_utc = NULL,
            bootstrap_completed_at_utc = NULL,
            last_error = NULL,
            updated_at_utc = @applied_at_utc
        WHERE singleton_id = 1;
        """;

    public const string ZeroNewAffectedDecisionsSql = """
        SELECT (
            (SELECT count(*) FROM public.strategy_market_paper_runs strategy_run
             JOIN correction_target_strategies strategy ON strategy.id = strategy_run.strategy_id
             WHERE NOT EXISTS (SELECT 1 FROM correction_target_runs target WHERE target.id = strategy_run.id)
               AND (strategy_run.created_at_utc >= @mutation_timestamp_utc
                 OR strategy_run.updated_at_utc >= @mutation_timestamp_utc
                 OR strategy_run.entered_at_utc >= @mutation_timestamp_utc
                 OR strategy_run.settled_at_utc >= @mutation_timestamp_utc)) +
            (SELECT count(*) FROM public.paper_orders paper_order
             JOIN correction_target_strategies strategy ON strategy.id = paper_order.strategy_id
             WHERE NOT EXISTS (SELECT 1 FROM correction_target_orders target WHERE target.id = paper_order.id)
               AND (paper_order.created_at_utc >= @mutation_timestamp_utc
                 OR paper_order.filled_at_utc >= @mutation_timestamp_utc
                 OR paper_order.cancelled_at_utc >= @mutation_timestamp_utc)) +
            (SELECT count(*) FROM public.signals signal
             JOIN public.strategies strategy ON signal.decision = strategy.code || '_entry'
             JOIN correction_target_strategies target ON target.id = strategy.id
             WHERE NOT EXISTS (SELECT 1 FROM correction_target_signals id_row WHERE id_row.id = signal.id)
               AND signal.created_at_utc >= @mutation_timestamp_utc) +
            (SELECT count(*) FROM public.dry_run_orders dry_order
             JOIN correction_target_strategies strategy ON strategy.id = dry_order.strategy_id
             WHERE dry_order.created_at_utc >= @mutation_timestamp_utc) +
            (SELECT count(*) FROM public.live_orders live_order
             JOIN correction_target_strategies strategy ON strategy.id = live_order.strategy_id
             WHERE live_order.created_at_utc >= @mutation_timestamp_utc
                OR live_order.updated_at_utc >= @mutation_timestamp_utc) +
            (SELECT count(*) FROM public.paper_live_shadow_decisions shadow
             JOIN correction_target_strategies strategy ON strategy.id = shadow.strategy_id
             WHERE shadow.decision_created_at_utc >= @mutation_timestamp_utc
                OR shadow.updated_at_utc >= @mutation_timestamp_utc) +
            (SELECT count(*) FROM public.paper_live_shadow_discrepancies discrepancy
             JOIN correction_target_strategies strategy ON strategy.id = discrepancy.strategy_id
             WHERE discrepancy.created_at_utc >= @mutation_timestamp_utc)
        )::integer;
        """;

    public const string RefreshCopiedPerformanceSql = """
        DELETE FROM public.paper_copied_trader_performance performance
        USING correction_target_wallets target
        WHERE performance.copied_trader_wallet = target.copied_trader_wallet;

        WITH event_rows AS (
            SELECT paper_order.copied_trader_wallet,
                   COALESCE(NULLIF(gamma.category, ''), 'unknown') category,
                   1::integer orders_count,
                   CASE WHEN paper_order.status IN ('Filled','PartiallyFilled','PartiallyFilledExpired') THEN 1 ELSE 0 END::integer filled_orders_count,
                   0::integer buy_fills_count, 0::integer sell_fills_count,
                   0::integer open_positions_count, 0::integer settled_positions_count,
                   0::integer won_positions_count, 0::integer lost_positions_count,
                   0::numeric buy_cost_usd, 0::numeric sell_proceeds_usd,
                   0::numeric settlement_value_usd, 0::numeric realized_pnl_usd,
                   0::numeric unrealized_pnl_usd,
                   paper_order.created_at_utc first_order_utc,
                   paper_order.created_at_utc last_order_utc
            FROM public.paper_orders paper_order
            JOIN correction_target_wallets target
              ON target.copied_trader_wallet = paper_order.copied_trader_wallet
            LEFT JOIN LATERAL (
                SELECT market.category
                FROM public.polymarket_gamma_markets market
                WHERE market.condition_id = paper_order.condition_id
                ORDER BY market.fetched_at_utc DESC, market.market_id
                LIMIT 1) gamma ON true
            WHERE btrim(paper_order.copied_trader_wallet) <> ''

            UNION ALL
            SELECT paper_order.copied_trader_wallet,
                   COALESCE(NULLIF(gamma.category, ''), 'unknown'),
                   0, 0,
                   CASE WHEN paper_order.side = 'Buy' THEN 1 ELSE 0 END,
                   CASE WHEN paper_order.side = 'Sell' THEN 1 ELSE 0 END,
                   0, 0, 0, 0,
                   CASE WHEN paper_order.side = 'Buy' THEN paper_fill.price * paper_fill.size_shares ELSE 0 END,
                   CASE WHEN paper_order.side = 'Sell' THEN paper_fill.price * paper_fill.size_shares ELSE 0 END,
                   0, paper_fill.realized_pnl_usd, 0,
                   paper_order.created_at_utc, paper_order.created_at_utc
            FROM public.paper_fills paper_fill
            JOIN public.paper_orders paper_order ON paper_order.id = paper_fill.paper_order_id
            JOIN correction_target_wallets target
              ON target.copied_trader_wallet = paper_order.copied_trader_wallet
            LEFT JOIN LATERAL (
                SELECT market.category
                FROM public.polymarket_gamma_markets market
                WHERE market.condition_id = paper_order.condition_id
                ORDER BY market.fetched_at_utc DESC, market.market_id
                LIMIT 1) gamma ON true
            WHERE btrim(paper_order.copied_trader_wallet) <> ''

            UNION ALL
            SELECT paper_position.copied_trader_wallet,
                   COALESCE(NULLIF(gamma.category, ''), 'unknown'),
                   0, 0, 0, 0,
                   CASE WHEN paper_position.size_shares > 0 THEN 1 ELSE 0 END,
                   0, 0, 0, 0, 0, 0, 0,
                   paper_position.unrealized_pnl_usd, NULL::timestamptz, NULL::timestamptz
            FROM public.paper_positions paper_position
            JOIN correction_target_wallets target
              ON target.copied_trader_wallet = paper_position.copied_trader_wallet
            LEFT JOIN LATERAL (
                SELECT market.category
                FROM public.polymarket_gamma_markets market
                WHERE market.condition_id = paper_position.condition_id
                ORDER BY market.fetched_at_utc DESC, market.market_id
                LIMIT 1) gamma ON true
            WHERE btrim(paper_position.copied_trader_wallet) <> ''

            UNION ALL
            SELECT settlement.copied_trader_wallet,
                   COALESCE(NULLIF(settlement.category, ''), NULLIF(gamma.category, ''), 'unknown'),
                   0, 0, 0, 0, 0, 1,
                   CASE WHEN settlement.won THEN 1 ELSE 0 END,
                   CASE WHEN settlement.won THEN 0 ELSE 1 END,
                   0, 0, settlement.settlement_value_usd, settlement.realized_pnl_usd, 0,
                   NULL::timestamptz, NULL::timestamptz
            FROM public.paper_position_settlements settlement
            JOIN correction_target_wallets target
              ON target.copied_trader_wallet = settlement.copied_trader_wallet
            LEFT JOIN LATERAL (
                SELECT market.category
                FROM public.polymarket_gamma_markets market
                WHERE market.condition_id = settlement.condition_id
                ORDER BY market.fetched_at_utc DESC, market.market_id
                LIMIT 1) gamma ON true
            WHERE btrim(settlement.copied_trader_wallet) <> ''
        ), category_rows AS (
            SELECT copied_trader_wallet, category,
                   sum(orders_count)::integer orders_count,
                   sum(filled_orders_count)::integer filled_orders_count,
                   sum(buy_fills_count)::integer buy_fills_count,
                   sum(sell_fills_count)::integer sell_fills_count,
                   sum(open_positions_count)::integer open_positions_count,
                   sum(settled_positions_count)::integer settled_positions_count,
                   sum(won_positions_count)::integer won_positions_count,
                   sum(lost_positions_count)::integer lost_positions_count,
                   sum(buy_cost_usd) buy_cost_usd, sum(sell_proceeds_usd) sell_proceeds_usd,
                   sum(settlement_value_usd) settlement_value_usd,
                   sum(realized_pnl_usd) realized_pnl_usd,
                   sum(unrealized_pnl_usd) unrealized_pnl_usd,
                   min(first_order_utc) first_order_utc, max(last_order_utc) last_order_utc
            FROM event_rows GROUP BY copied_trader_wallet, category
        ), grouped AS (
            SELECT * FROM category_rows
            UNION ALL
            SELECT copied_trader_wallet, 'OVERALL',
                   sum(orders_count)::integer, sum(filled_orders_count)::integer,
                   sum(buy_fills_count)::integer, sum(sell_fills_count)::integer,
                   sum(open_positions_count)::integer, sum(settled_positions_count)::integer,
                   sum(won_positions_count)::integer, sum(lost_positions_count)::integer,
                   sum(buy_cost_usd), sum(sell_proceeds_usd), sum(settlement_value_usd),
                   sum(realized_pnl_usd), sum(unrealized_pnl_usd),
                   min(first_order_utc), max(last_order_utc)
            FROM category_rows GROUP BY copied_trader_wallet
        ), scored AS (
            SELECT *, realized_pnl_usd + unrealized_pnl_usd total_pnl_usd,
                   CASE WHEN buy_cost_usd = 0 THEN 0 ELSE
                       (realized_pnl_usd + unrealized_pnl_usd) / buy_cost_usd * 100 END roi_pct,
                   CASE WHEN settled_positions_count = 0 THEN 0 ELSE
                       won_positions_count::numeric / settled_positions_count * 100 END win_rate_pct
            FROM grouped
        )
        INSERT INTO public.paper_copied_trader_performance (
            copied_trader_wallet, category, orders_count, filled_orders_count, buy_fills_count,
            sell_fills_count, open_positions_count, settled_positions_count, won_positions_count,
            lost_positions_count, buy_cost_usd, sell_proceeds_usd, settlement_value_usd,
            realized_pnl_usd, unrealized_pnl_usd, total_pnl_usd, roi_pct, win_rate_pct,
            score, first_order_utc, last_order_utc, refreshed_at_utc)
        SELECT copied_trader_wallet, category, orders_count, filled_orders_count, buy_fills_count,
               sell_fills_count, open_positions_count, settled_positions_count, won_positions_count,
               lost_positions_count, buy_cost_usd, sell_proceeds_usd, settlement_value_usd,
               realized_pnl_usd, unrealized_pnl_usd, total_pnl_usd, roi_pct, win_rate_pct,
               greatest(0, least(100,
                   50 + greatest(-50, least(50, roi_pct)) * 0.35
                   + (win_rate_pct - 50) * 0.25
                   + greatest(-20, least(20, total_pnl_usd)) * 1.25
                   + least(settled_positions_count, 20) * 0.5
                   - lost_positions_count * 1.25 - open_positions_count * 0.1)),
               first_order_utc, last_order_utc, @refreshed_at_utc
        FROM scored;

        DELETE FROM public.paper_copied_trader_performance_refresh_queue queue
        USING correction_target_wallets target
        WHERE queue.copied_trader_wallet = target.copied_trader_wallet;

        SELECT count(*)::integer
        FROM public.paper_copied_trader_performance performance
        JOIN correction_target_wallets target
          ON target.copied_trader_wallet = performance.copied_trader_wallet
        WHERE performance.refreshed_at_utc = @refreshed_at_utc;
        """;

    public const string ImmutableAppliedVerificationSql = """
        SELECT (
            (SELECT count(*) FROM correction_main_removals target
             LEFT JOIN public.strategy_market_paper_runs strategy_run ON strategy_run.id = target.run_id
             WHERE strategy_run.id IS NULL OR strategy_run.status <> 'Skipped'
               OR strategy_run.stake_usd <> target.restored_base_stake_usd
               OR strategy_run.updated_at_utc <> target.corrected_skipped_updated_at_utc
                OR strategy_run.selected_asset_id IS NOT NULL OR strategy_run.selected_outcome IS NOT NULL
                OR strategy_run.entry_price IS NOT NULL OR strategy_run.size_shares IS NOT NULL
                OR strategy_run.signal_id IS NOT NULL OR strategy_run.paper_order_id IS NOT NULL
                OR strategy_run.entered_at_utc IS NOT NULL OR strategy_run.settlement_price IS NOT NULL
                OR strategy_run.settlement_value_usd IS NOT NULL OR strategy_run.realized_pnl_usd IS NOT NULL
                OR strategy_run.settled_at_utc IS NOT NULL
                OR strategy_run.skip_reason <> 'reference_average_history_correction_v2_would_skip'
                OR strategy_run.skip_diagnostics_json IS DISTINCT FROM jsonb_build_object(
                    'provenance', 'reference_average_history_correction_v2',
                    'modeled', false,
                    'graph_manifest_sha256', @graph_manifest_sha256,
                    'cutoff_utc', @cutoff_utc,
                    'classifier_action', target.classifier_action,
                    'classifier_reason', target.classifier_reason,
                    'signal_preview_manifest_sha256', target.signal_preview_manifest_sha256,
                    'replay_classifier_sha256', target.replay_classifier_sha256,
                    'replay_evidence', target.replay_evidence_json,
                    'replay_evidence_sha256', target.replay_evidence_sha256,
                    'original_run_id', strategy_run.id::text,
                    'original_paper_order_id', target.order_id::text,
                    'original_signal_id', target.signal_id::text,
                    'historical_base_stake_usd', target.restored_base_stake_usd,
                    'historical_effective_stake_usd', target.historical_effective_stake_usd,
                    'historical_target_notional_usd', target.historical_target_notional_usd,
                    'historical_stake_sizing_source', target.historical_stake_sizing_source,
                    'stake_sizing_proof_sha256', target.stake_sizing_proof_sha256,
                    'original_graph_state_sha256', target.graph_state_sha256,
                    'original_fill_set_sha256', target.fill_set_sha256)) +
            (SELECT count(*) FROM correction_child_removals target
             JOIN public.strategy_market_paper_runs strategy_run ON strategy_run.id = target.run_id) +
            (SELECT count(*) FROM correction_main_removals target
             JOIN public.paper_orders paper_order ON paper_order.id = target.order_id) +
            (SELECT count(*) FROM correction_child_removals target
             JOIN public.paper_orders paper_order ON paper_order.id = target.order_id) +
            (SELECT count(*) FROM correction_main_removals target
             JOIN public.signals signal ON signal.id = target.signal_id) +
            (SELECT count(*) FROM correction_child_removals target
             JOIN public.signals signal ON signal.id = target.signal_id) +
            (SELECT count(*) FROM public.paper_fills paper_fill
             WHERE EXISTS (SELECT 1 FROM correction_main_removals target WHERE target.order_id = paper_fill.paper_order_id)
                OR EXISTS (SELECT 1 FROM correction_child_removals target WHERE target.order_id = paper_fill.paper_order_id)) +
            (SELECT count(*) FROM correction_adds target
             LEFT JOIN public.strategy_market_paper_runs strategy_run ON strategy_run.id = target.run_id
             LEFT JOIN public.signals signal ON signal.id = target.signal_id
             LEFT JOIN public.paper_orders paper_order ON paper_order.id = target.order_id
             LEFT JOIN public.paper_fills paper_fill ON paper_fill.id = target.fill_id
             LEFT JOIN public.paper_positions position_row ON position_row.id = target.position_id
             LEFT JOIN public.paper_position_settlements settlement_row ON settlement_row.id = target.settlement_id
             WHERE strategy_run.id IS NULL OR signal.id IS NULL OR paper_order.id IS NULL OR paper_fill.id IS NULL
                OR position_row.id IS NULL OR settlement_row.id IS NULL
                OR strategy_run.status <> 'Settled'
                OR strategy_run.selected_asset_id <> target.selected_token_id
                OR strategy_run.selected_outcome <> target.selected_outcome
                OR strategy_run.entry_price <> target.assumed_fill_price
                OR strategy_run.stake_usd <> target.requested_notional_usd
                OR strategy_run.size_shares <> target.filled_size_shares
                OR strategy_run.signal_id <> target.signal_id OR strategy_run.paper_order_id <> target.order_id
                OR strategy_run.entered_at_utc <> target.modeled_entry_at_utc
                OR strategy_run.settlement_price <> target.settlement_price
                OR strategy_run.settlement_value_usd <> target.settlement_value_usd
                OR strategy_run.realized_pnl_usd <> target.realized_pnl_usd
                OR strategy_run.settled_at_utc <> target.modeled_settled_at_utc
                OR strategy_run.updated_at_utc <> target.modeled_settled_at_utc
                OR strategy_run.skip_reason IS NOT NULL OR strategy_run.skip_diagnostics_json IS NOT NULL
                OR signal.leader_trade_id IS NOT NULL
                OR signal.trader_wallet <> 'strategy:' || target.strategy_code
                OR signal.condition_id <> target.condition_id OR signal.asset_id <> target.selected_token_id
                OR signal.outcome <> target.selected_outcome OR signal.leader_price <> target.assumed_fill_price
                OR signal.best_bid IS NOT NULL OR signal.best_ask IS NOT NULL
                OR signal.spread_abs IS NOT NULL OR signal.spread_pct IS NOT NULL
                OR signal.lag_seconds IS NOT NULL OR signal.score <> 100 OR NOT signal.accepted
                OR signal.decision <> target.strategy_code || '_entry'
                OR signal.proposed_paper_price <> target.assumed_fill_price
                OR signal.proposed_size_shares <> target.filled_size_shares
                OR signal.proposed_notional_usd <> target.requested_notional_usd
                OR signal.created_at_utc <> target.modeled_entry_at_utc OR signal.raw_context_json IS NOT NULL
                OR paper_order.signal_id <> target.signal_id OR paper_order.strategy_id <> target.strategy_id
                OR paper_order.copied_trader_wallet <> 'strategy:' || target.strategy_code
                OR paper_order.status <> 'Filled' OR paper_order.side <> 'Buy'
                OR paper_order.asset_id <> target.selected_token_id
                OR paper_order.condition_id <> target.condition_id
                OR paper_order.outcome <> target.selected_outcome
                OR paper_order.price <> target.assumed_fill_price
                OR paper_order.size_shares <> target.filled_size_shares
                OR paper_order.notional_usd <> target.requested_notional_usd
                OR paper_order.created_at_utc <> target.modeled_entry_at_utc
                OR paper_order.expires_at_utc <> target.modeled_entry_at_utc
                OR paper_order.filled_at_utc <> target.modeled_entry_at_utc
                OR paper_order.cancelled_at_utc IS NOT NULL OR paper_order.correlation_id IS NOT NULL
                OR paper_order.execution_source <> 'btc_updown5m_fak_taker_paper'
                OR paper_order.raw_decision_json IS DISTINCT FROM target.modeled_raw_decision_json::jsonb
                OR paper_fill.price <> target.assumed_fill_price
                OR paper_fill.size_shares <> target.filled_size_shares
                OR paper_fill.filled_at_utc <> target.modeled_entry_at_utc
                OR paper_fill.evidence <> target.modeled_fill_evidence OR paper_fill.realized_pnl_usd <> 0
                OR position_row.copied_trader_wallet <> 'strategy:' || target.strategy_code
                OR position_row.asset_id <> target.selected_token_id
                OR position_row.condition_id <> target.condition_id OR position_row.outcome <> target.selected_outcome
                OR position_row.size_shares <> 0 OR position_row.average_price <> 0
                OR position_row.estimated_value_usd <> 0 OR position_row.unrealized_pnl_usd <> 0
                OR position_row.updated_at_utc <> target.modeled_settled_at_utc
                OR settlement_row.copied_trader_wallet <> 'strategy:' || target.strategy_code
                OR settlement_row.asset_id <> target.selected_token_id
                OR settlement_row.condition_id <> target.condition_id
                OR settlement_row.outcome <> target.selected_outcome
                OR settlement_row.winning_asset_id <> target.resolved_winning_token_id
                OR settlement_row.winning_outcome <> target.resolved_winning_outcome
                OR settlement_row.category <> target.settlement_category
                OR settlement_row.settled_size_shares <> target.filled_size_shares
                OR settlement_row.average_price <> target.assumed_fill_price
                OR settlement_row.cost_basis_usd <> target.requested_notional_usd
                OR settlement_row.settlement_value_usd <> target.settlement_value_usd
                OR settlement_row.realized_pnl_usd <> target.realized_pnl_usd
                OR settlement_row.won IS DISTINCT FROM target.won
                OR settlement_row.settlement_source <> 'ReferenceAverageHistoryCorrectionV2Modeled'
                OR settlement_row.settled_at_utc <> target.modeled_settled_at_utc
                OR settlement_row.created_at_utc <> target.modeled_settled_at_utc)
        )::integer;
        """;

    public const string AppliedVerificationSql = """
        SELECT (
            (SELECT count(*) FROM correction_main_removals target
             LEFT JOIN public.strategy_market_paper_runs strategy_run ON strategy_run.id = target.run_id
             WHERE strategy_run.id IS NULL OR strategy_run.status <> 'Skipped'
                OR strategy_run.stake_usd <> target.restored_base_stake_usd
                OR strategy_run.selected_asset_id IS NOT NULL OR strategy_run.selected_outcome IS NOT NULL
                OR strategy_run.entry_price IS NOT NULL OR strategy_run.size_shares IS NOT NULL
                OR strategy_run.signal_id IS NOT NULL OR strategy_run.paper_order_id IS NOT NULL
                OR strategy_run.entered_at_utc IS NOT NULL OR strategy_run.settlement_price IS NOT NULL
                OR strategy_run.settlement_value_usd IS NOT NULL OR strategy_run.realized_pnl_usd IS NOT NULL
                OR strategy_run.settled_at_utc IS NOT NULL
                OR strategy_run.skip_reason <> 'reference_average_history_correction_v2_would_skip'
                OR strategy_run.skip_diagnostics_json IS DISTINCT FROM jsonb_build_object(
                    'provenance', 'reference_average_history_correction_v2',
                    'modeled', false,
                    'graph_manifest_sha256', @graph_manifest_sha256,
                    'cutoff_utc', @cutoff_utc,
                    'classifier_action', target.classifier_action,
                    'classifier_reason', target.classifier_reason,
                    'signal_preview_manifest_sha256', target.signal_preview_manifest_sha256,
                    'replay_classifier_sha256', target.replay_classifier_sha256,
                    'replay_evidence', target.replay_evidence_json,
                    'replay_evidence_sha256', target.replay_evidence_sha256,
                    'original_run_id', strategy_run.id::text,
                    'original_paper_order_id', target.order_id::text,
                    'original_signal_id', target.signal_id::text,
                    'historical_base_stake_usd', target.restored_base_stake_usd,
                    'historical_effective_stake_usd', target.historical_effective_stake_usd,
                    'historical_target_notional_usd', target.historical_target_notional_usd,
                    'historical_stake_sizing_source', target.historical_stake_sizing_source,
                    'stake_sizing_proof_sha256', target.stake_sizing_proof_sha256,
                    'original_graph_state_sha256', target.graph_state_sha256,
                    'original_fill_set_sha256', target.fill_set_sha256)) +
            (SELECT count(*) FROM correction_child_removals target
             JOIN public.strategy_market_paper_runs strategy_run ON strategy_run.id = target.run_id) +
            (SELECT count(*) FROM correction_main_removals target
             JOIN public.paper_orders paper_order ON paper_order.id = target.order_id) +
            (SELECT count(*) FROM correction_child_removals target
             JOIN public.paper_orders paper_order ON paper_order.id = target.order_id) +
            (SELECT count(*) FROM correction_main_removals target
             JOIN public.signals signal ON signal.id = target.signal_id) +
            (SELECT count(*) FROM correction_child_removals target
             JOIN public.signals signal ON signal.id = target.signal_id) +
            (SELECT count(*) FROM correction_adds target
             LEFT JOIN public.strategy_market_paper_runs strategy_run ON strategy_run.id = target.run_id
             LEFT JOIN public.signals signal ON signal.id = target.signal_id
             LEFT JOIN public.paper_orders paper_order ON paper_order.id = target.order_id
             LEFT JOIN public.paper_fills paper_fill ON paper_fill.id = target.fill_id
             LEFT JOIN public.paper_positions position_row ON position_row.id = target.position_id
             LEFT JOIN public.paper_position_settlements settlement_row ON settlement_row.id = target.settlement_id
             WHERE strategy_run.id IS NULL OR signal.id IS NULL OR paper_order.id IS NULL OR paper_fill.id IS NULL
                OR position_row.id IS NULL OR settlement_row.id IS NULL
                OR strategy_run.status <> 'Settled'
                OR strategy_run.selected_asset_id <> target.selected_token_id
                OR strategy_run.selected_outcome <> target.selected_outcome
                OR strategy_run.entry_price <> target.assumed_fill_price
                OR strategy_run.stake_usd <> target.requested_notional_usd
                OR strategy_run.size_shares <> target.filled_size_shares
                OR strategy_run.signal_id <> target.signal_id OR strategy_run.paper_order_id <> target.order_id
                OR strategy_run.entered_at_utc <> target.modeled_entry_at_utc
                OR strategy_run.settlement_price <> target.settlement_price
                OR strategy_run.settlement_value_usd <> target.settlement_value_usd
                OR strategy_run.realized_pnl_usd <> target.realized_pnl_usd
                OR strategy_run.settled_at_utc <> target.modeled_settled_at_utc
                OR strategy_run.updated_at_utc <> target.modeled_settled_at_utc
                OR strategy_run.skip_reason IS NOT NULL OR strategy_run.skip_diagnostics_json IS NOT NULL
                OR signal.leader_trade_id IS NOT NULL
                OR signal.trader_wallet <> 'strategy:' || target.strategy_code
                OR signal.condition_id <> target.condition_id OR signal.asset_id <> target.selected_token_id
                OR signal.outcome <> target.selected_outcome OR signal.leader_price <> target.assumed_fill_price
                OR signal.best_bid IS NOT NULL OR signal.best_ask IS NOT NULL
                OR signal.spread_abs IS NOT NULL OR signal.spread_pct IS NOT NULL
                OR signal.lag_seconds IS NOT NULL OR signal.score <> 100 OR NOT signal.accepted
                OR signal.decision <> target.strategy_code || '_entry'
                OR signal.proposed_paper_price <> target.assumed_fill_price
                OR signal.proposed_size_shares <> target.filled_size_shares
                OR signal.proposed_notional_usd <> target.requested_notional_usd
                OR signal.created_at_utc <> target.modeled_entry_at_utc OR signal.raw_context_json IS NOT NULL
                OR paper_order.signal_id <> target.signal_id OR paper_order.strategy_id <> target.strategy_id
                OR paper_order.copied_trader_wallet <> 'strategy:' || target.strategy_code
                OR paper_order.status <> 'Filled' OR paper_order.side <> 'Buy'
                OR paper_order.asset_id <> target.selected_token_id
                OR paper_order.condition_id <> target.condition_id
                OR paper_order.outcome <> target.selected_outcome
                OR paper_order.price <> target.assumed_fill_price
                OR paper_order.size_shares <> target.filled_size_shares
                OR paper_order.notional_usd <> target.requested_notional_usd
                OR paper_order.created_at_utc <> target.modeled_entry_at_utc
                OR paper_order.expires_at_utc <> target.modeled_entry_at_utc
                OR paper_order.filled_at_utc <> target.modeled_entry_at_utc
                OR paper_order.cancelled_at_utc IS NOT NULL OR paper_order.correlation_id IS NOT NULL
                OR paper_order.execution_source <> 'btc_updown5m_fak_taker_paper'
                OR paper_order.raw_decision_json IS DISTINCT FROM target.modeled_raw_decision_json::jsonb
                OR paper_fill.price <> target.assumed_fill_price
                OR paper_fill.size_shares <> target.filled_size_shares
                OR paper_fill.filled_at_utc <> target.modeled_entry_at_utc
                OR paper_fill.evidence <> target.modeled_fill_evidence OR paper_fill.realized_pnl_usd <> 0
                OR position_row.copied_trader_wallet <> 'strategy:' || target.strategy_code
                OR position_row.asset_id <> target.selected_token_id
                OR position_row.condition_id <> target.condition_id OR position_row.outcome <> target.selected_outcome
                OR position_row.size_shares <> 0 OR position_row.average_price <> 0
                OR position_row.estimated_value_usd <> 0 OR position_row.unrealized_pnl_usd <> 0
                OR position_row.updated_at_utc <> target.modeled_settled_at_utc
                OR settlement_row.copied_trader_wallet <> 'strategy:' || target.strategy_code
                OR settlement_row.asset_id <> target.selected_token_id
                OR settlement_row.condition_id <> target.condition_id
                OR settlement_row.outcome <> target.selected_outcome
                OR settlement_row.winning_asset_id <> target.resolved_winning_token_id
                OR settlement_row.winning_outcome <> target.resolved_winning_outcome
                OR settlement_row.category <> target.settlement_category
                OR settlement_row.settled_size_shares <> target.filled_size_shares
                OR settlement_row.average_price <> target.assumed_fill_price
                OR settlement_row.cost_basis_usd <> target.requested_notional_usd
                OR settlement_row.settlement_value_usd <> target.settlement_value_usd
                OR settlement_row.realized_pnl_usd <> target.realized_pnl_usd
                OR settlement_row.won IS DISTINCT FROM target.won
                OR settlement_row.settlement_source <> 'ReferenceAverageHistoryCorrectionV2Modeled'
                OR settlement_row.settled_at_utc <> target.modeled_settled_at_utc
                OR settlement_row.created_at_utc <> target.modeled_settled_at_utc) +
            (SELECT count(*) FROM correction_target_strategies target
             LEFT JOIN public.dashboard_projection_reconciliation_queue queue ON queue.strategy_id = target.id
             WHERE queue.strategy_id IS NULL OR queue.reason <> 'reference_average_history_correction_v2') +
            (SELECT count(*) FROM correction_target_wallets target
             LEFT JOIN public.paper_copied_trader_performance_refresh_queue queue
               ON queue.copied_trader_wallet = target.copied_trader_wallet
             WHERE queue.copied_trader_wallet IS NULL
                OR queue.source_kind <> 'reference_average_history_correction_v2') +
            (SELECT count(*) FROM public.dashboard_projection_events event
             JOIN correction_target_strategies target ON target.id = event.strategy_id) +
            (SELECT count(*) FROM public.dashboard_projection_control control
             WHERE control.singleton_id <> 1
                OR control.initialized
                OR control.calculation_version <> 2
                OR control.status <> 'PendingHistoryCorrectionBootstrap'
                OR control.reconciliation_cursor_strategy_id IS NOT NULL
                OR control.bootstrap_started_at_utc IS NOT NULL
                OR control.bootstrap_completed_at_utc IS NOT NULL
                OR control.last_error IS NOT NULL) +
            (SELECT CASE WHEN count(*) = 1 THEN 0 ELSE 1 END
             FROM public.dashboard_projection_control)
        )::integer;
        """;
}
