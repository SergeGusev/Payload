using System.Data;
using System.Text.Json;
using Npgsql;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    public async Task<PaperOrder?> TryClaimPaperOutcomeConfirmationAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, $"""
            UPDATE paper_orders SET confirmation_next_attempt_at_utc=@Now + interval '1 minute',
                confirmation_evidence=jsonb_build_object('last_attempt', 'lookup_started', 'attempted_at_utc', @Now::timestamptz)
            WHERE id=(SELECT id FROM paper_orders WHERE NOT confirmed AND confirmation_next_attempt_at_utc <= @Now
                ORDER BY confirmation_next_attempt_at_utc, created_at_utc, id LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING {PaperOrderSelectColumns};
            """);
        command.Parameters.AddWithValue("Now", nowUtc.UtcDateTime);
        return await ReadOneAsync();
        async Task<PaperOrder?> ReadOneAsync()
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadPaperOrder(reader) : null;
        }
    }

    public async Task DeferPaperOutcomeConfirmationAsync(Guid orderId, DateTimeOffset nextAttemptUtc, string reason,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, """
            UPDATE paper_orders SET confirmation_next_attempt_at_utc = @Next,
                confirmation_evidence = jsonb_build_object('last_error', @Reason::text, 'attempted_at_utc', clock_timestamp())
            WHERE id = @Id AND NOT confirmed;
            """);
        command.Parameters.AddWithValue("Id", orderId);
        command.Parameters.AddWithValue("Next", nextAttemptUtc.UtcDateTime);
        command.Parameters.AddWithValue("Reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PaperOutcomeConfirmationResult> ConfirmPaperOutcomeAsync(PaperOutcomeConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        // No external request occurs under these locks. Preemption or a concurrent
        // financial writer rolls back the complete candidate, including derived state.
        await ExecuteAsync("SET LOCAL lock_timeout = '100ms'; SET LOCAL statement_timeout = '2s';");
        await using (var gate = Command("SELECT pg_try_advisory_xact_lock(hashtextextended('paper-outcome-hourly',0));"))
        {
            if (!(bool)(await gate.ExecuteScalarAsync(cancellationToken))!) return new(false, false, "hourly_refresh_active");
        }
        PaperOrder? order;
        await using (var command = Command($"SELECT {PaperOrderSelectColumns} FROM paper_orders WHERE id=@Id FOR UPDATE;"))
        {
            command.Parameters.AddWithValue("Id", confirmation.PaperOrderId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            order = await reader.ReadAsync(cancellationToken) ? ReadPaperOrder(reader) : null;
        }
        if (order is null) return new(false, false, "order_missing");
        if (order.Confirmed) return new(true, false, "already_confirmed");
        if (order.ConditionId != confirmation.ConditionId || order.AssetId != confirmation.AssetId ||
            order.Outcome != confirmation.Outcome) return new(false, false, "order_identity_changed");
        if (order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled)
            return new(false, false, "order_still_active");

        await using (var identity = ForOrder("""
            SELECT EXISTS(SELECT 1 FROM strategy_market_paper_runs WHERE paper_order_id IN
                (SELECT id FROM paper_orders WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset AND condition_id=@Condition AND strategy_id=@Strategy) AND
                (condition_id<>@Condition OR selected_asset_id IS DISTINCT FROM @Asset OR selected_outcome IS DISTINCT FROM @Outcome))
                OR EXISTS(SELECT 1 FROM paper_position_settlements WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset
                    AND (condition_id<>@Condition OR outcome<>@Outcome))
                OR EXISTS(SELECT 1 FROM live_orders WHERE paper_order_id=@Id
                    AND (condition_id<>@Condition OR asset_id<>@Asset OR outcome<>@Outcome));
            """))
        {
            identity.Parameters.AddWithValue("Outcome", order.Outcome);
            if ((bool)(await identity.ExecuteScalarAsync(cancellationToken))!) return new(false, false, "related_identity_conflict");
        }

        await using (var readiness = ForOrder("""
            SELECT EXISTS(SELECT 1 FROM strategy_market_paper_runs WHERE paper_order_id IN (SELECT id FROM paper_orders WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset AND condition_id=@Condition AND strategy_id=@Strategy) AND status IN ('Entered','Resting'))
                OR EXISTS(SELECT 1 FROM paper_positions WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset AND size_shares>0)
                OR EXISTS(SELECT 1 FROM live_orders WHERE paper_order_id=@Id AND
                    (status IN ('Submitted','Live','Delayed','Unmatched','CancelRequested','CancelFailed','Error')
                     OR (filled_size>0 AND settled_at_utc IS NULL)));
            """))
        {
            if ((bool)(await readiness.ExecuteScalarAsync(cancellationToken))!) return new(false, false, "financial_cycle_not_closed");
        }

        var won = order.AssetId == confirmation.WinningAssetId;
        var before = await ReadFinancialSnapshotAsync();
        var corrected = false;
        // Settlement represents remaining inventory for this wallet/token, not one
        // additional payout per order. Existing sell fills and their proceeds stay intact.
        await using (var settlements = ForOrder("""
            UPDATE paper_position_settlements s
            SET winning_asset_id=@Winner, winning_outcome=@WinningOutcome, won=@Won,
                settlement_value_usd=CASE WHEN s.won=@Won THEN s.settlement_value_usd WHEN @Won THEN s.settled_size_shares ELSE 0 END,
                realized_pnl_usd=CASE WHEN s.won=@Won THEN s.realized_pnl_usd
                    ELSE (CASE WHEN @Won THEN s.settled_size_shares ELSE 0 END)-s.cost_basis_usd END,
                net_realized_pnl_usd=CASE WHEN s.won=@Won THEN s.net_realized_pnl_usd WHEN s.net_realized_pnl_usd IS NULL THEN NULL
                    ELSE (CASE WHEN @Won THEN s.settled_size_shares ELSE 0 END)-s.cost_basis_usd-s.fee_usd END,
                settlement_source='GammaConfirmed'
            WHERE s.copied_trader_wallet=@Wallet AND s.asset_id=@Asset AND s.condition_id=@Condition
              AND (s.won IS DISTINCT FROM @Won OR s.winning_asset_id IS DISTINCT FROM @Winner
                   OR s.winning_outcome IS DISTINCT FROM @WinningOutcome)
              AND (EXISTS(SELECT 1 FROM paper_fills WHERE paper_order_id=@Id)
                   OR EXISTS(SELECT 1 FROM strategy_market_paper_runs WHERE paper_order_id IN (SELECT id FROM paper_orders WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset AND condition_id=@Condition AND strategy_id=@Strategy) AND status='Settled'));
            """))
        {
            AddOutcome(settlements);
            corrected = await settlements.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        await using (var runs = ForOrder("""
            WITH corrected AS (
                SELECT r.id, COALESCE(sold.proceeds,0) +
                    greatest(0,r.size_shares-COALESCE(sold.size,0)) * CASE WHEN @Won THEN 1 ELSE 0 END AS payout
                FROM strategy_market_paper_runs r
                LEFT JOIN LATERAL (
                    SELECT sum(least(f.size_shares,greatest(0,r.size_shares-f.preceding_size))) AS size,
                        sum(least(f.size_shares,greatest(0,r.size_shares-f.preceding_size))*f.price) AS proceeds
                    FROM (
                        SELECT f.size_shares,f.price,
                            COALESCE(sum(f.size_shares) OVER (ORDER BY o.created_at_utc,o.id,f.filled_at_utc,f.id
                                ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0) preceding_size
                        FROM paper_orders o JOIN paper_fills f ON f.paper_order_id=o.id
                        WHERE o.strategy_id=r.strategy_id AND o.copied_trader_wallet=@Wallet
                          AND o.asset_id=@Asset AND o.condition_id=@Condition AND o.side='Sell'
                          AND o.created_at_utc>=COALESCE(r.entered_at_utc,r.created_at_utc)
                    ) f
                ) sold ON true
                WHERE r.paper_order_id IN (SELECT id FROM paper_orders WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset AND condition_id=@Condition AND strategy_id=@Strategy) AND r.status='Settled'
                  AND r.settlement_price IS DISTINCT FROM CASE WHEN @Won THEN 1::numeric ELSE 0::numeric END
            )
            UPDATE strategy_market_paper_runs r SET settlement_price=CASE WHEN @Won THEN 1 ELSE 0 END,
                settlement_value_usd=c.payout, realized_pnl_usd=c.payout-r.stake_usd,
                net_realized_pnl_usd=CASE WHEN r.net_realized_pnl_usd IS NULL THEN NULL ELSE c.payout-r.stake_usd-r.fee_usd END,
                updated_at_utc=@Checked
            FROM corrected c WHERE r.id=c.id;
            """))
        {
            AddOutcome(runs);
            corrected |= await runs.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        if (corrected)
        {
            // Lock the same parent state rows as normal reconciliation before replacing
            // immutable-by-default event payloads for the corrected run.
            await using (var stateLock = ForOrder("SELECT child_strategy_id FROM strategy_loss_diff_states WHERE parent_strategy_id=@Strategy ORDER BY child_strategy_id FOR UPDATE;"))
                await stateLock.ExecuteNonQueryAsync(cancellationToken);
            await using (var events = ForOrder("""
                UPDATE strategy_loss_diff_parent_events e SET won=r.realized_pnl_usd>0
                FROM strategy_market_paper_runs r WHERE r.paper_order_id IN (SELECT id FROM paper_orders WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset AND condition_id=@Condition AND strategy_id=@Strategy) AND e.parent_run_id=r.id
                    AND r.realized_pnl_usd<>0 AND e.won IS DISTINCT FROM (r.realized_pnl_usd>0);
                DELETE FROM strategy_loss_diff_parent_events e USING strategy_market_paper_runs r
                    WHERE r.paper_order_id IN (SELECT id FROM paper_orders WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset AND condition_id=@Condition AND strategy_id=@Strategy) AND e.parent_run_id=r.id AND r.realized_pnl_usd=0;
                UPDATE strategies s SET paper_lost_counter=CASE WHEN s.paper_lost_coeff<=1 THEN 0 ELSE
                    (SELECT COALESCE(sum(CASE WHEN r.settlement_price=1 THEN -1 ELSE 1 END),0)::integer
                     FROM strategy_market_paper_runs r WHERE r.strategy_id=s.id AND r.status='Settled') END,
                    updated_at_utc=@Checked WHERE s.id=@Strategy;
                """)) await events.ExecuteNonQueryAsync(cancellationToken);
            await ReconcileStrategyLossDiffStatesAsync(connection, transaction, order.StrategyId, confirmation.CheckedAtUtc, cancellationToken);
            await RefreshConfirmedHourlyAsync();
            await RefreshConfirmedWalletAsync();
        }
        var after = await ReadFinancialSnapshotAsync();
        await using (var finish = ForOrder("""
            UPDATE paper_orders SET confirmed=true, confirmation_evidence=jsonb_build_object(
                'final_outcome',@Evidence::jsonb,'before',@Before::jsonb,'after',@After::jsonb,
                'corrected',@Corrected,'checked_at_utc',@Checked::timestamptz) WHERE id=@Id AND NOT confirmed;
            """))
        {
            finish.Parameters.AddWithValue("Evidence", confirmation.EvidenceJson);
            finish.Parameters.AddWithValue("Before", before);
            finish.Parameters.AddWithValue("After", after);
            finish.Parameters.AddWithValue("Corrected", corrected);
            await finish.ExecuteNonQueryAsync(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken);
        return new(true, corrected, "confirmed");

        NpgsqlCommand Command(string sql)
        {
            var command = CreateCommand(connection, sql);
            command.Transaction = transaction;
            command.CommandTimeout = 3;
            return command;
        }
        NpgsqlCommand ForOrder(string sql)
        {
            var command = Command(sql);
            command.Parameters.AddWithValue("Id", order.Id);
            command.Parameters.AddWithValue("Wallet", order.CopiedTraderWallet);
            command.Parameters.AddWithValue("Asset", order.AssetId);
            command.Parameters.AddWithValue("Condition", order.ConditionId);
            command.Parameters.AddWithValue("Strategy", order.StrategyId);
            command.Parameters.AddWithValue("Checked", confirmation.CheckedAtUtc.UtcDateTime);
            return command;
        }
        void AddOutcome(NpgsqlCommand command)
        {
            command.Parameters.AddWithValue("Winner", confirmation.WinningAssetId);
            command.Parameters.AddWithValue("WinningOutcome", confirmation.WinningOutcome);
            command.Parameters.AddWithValue("Won", won);
        }
        async Task ExecuteAsync(string sql)
        {
            await using var command = Command(sql);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        async Task<string> ReadFinancialSnapshotAsync()
        {
            await using var command = ForOrder("""
                SELECT jsonb_build_object(
                    'runs',(SELECT jsonb_agg(jsonb_build_object('id',r.id,'price',r.settlement_price,'value',r.settlement_value_usd,
                        'gross',r.realized_pnl_usd,'net',r.net_realized_pnl_usd,'settled_at_utc',r.settled_at_utc))
                        FROM strategy_market_paper_runs r WHERE paper_order_id IN (SELECT id FROM paper_orders WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset AND condition_id=@Condition AND strategy_id=@Strategy)),
                    'settlements',(SELECT jsonb_agg(jsonb_build_object('id',s.id,'winner',s.winning_asset_id,'outcome',s.winning_outcome,
                        'won',s.won,'value',s.settlement_value_usd,'gross',s.realized_pnl_usd,'net',s.net_realized_pnl_usd,'source',s.settlement_source))
                        FROM paper_position_settlements s WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset AND condition_id=@Condition))::text;
                """);
            return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        async Task RefreshConfirmedWalletAsync()
        {
            await ExecuteAsync("""
                CREATE TEMP TABLE temp_paper_copied_trader_performance_wallets(copied_trader_wallet text PRIMARY KEY) ON COMMIT DROP;
                """);
            await using (var select = ForOrder("""
                INSERT INTO temp_paper_copied_trader_performance_wallets VALUES (@Wallet);
                ANALYZE temp_paper_copied_trader_performance_wallets;
                DELETE FROM paper_copied_trader_performance WHERE copied_trader_wallet=@Wallet;
                """)) await select.ExecuteNonQueryAsync(cancellationToken);
            await ExecuteAsync(PaperConfirmationCopiedPerformanceSql);
        }
        async Task RefreshConfirmedHourlyAsync()
        {
            await using var command = ForOrder("""
                WITH totals AS (
                    SELECT extract(hour FROM entered_at_utc AT TIME ZONE 'UTC')::integer AS hour_utc,
                        count(*)::integer AS count, count(*) FILTER(WHERE realized_pnl_usd>0)::integer AS won,
                        count(*) FILTER(WHERE realized_pnl_usd<0)::integer AS lost, sum(stake_usd) AS stake,
                        sum(realized_pnl_usd) AS pnl, avg(realized_pnl_usd) AS average,
                        min(entered_at_utc) AS first_entry, max(entered_at_utc) AS last_entry
                    FROM strategy_market_paper_runs WHERE strategy_id=@Strategy AND status='Settled'
                        AND entered_at_utc IS NOT NULL AND realized_pnl_usd IS NOT NULL GROUP BY 1
                ) INSERT INTO date_dependent_strategy_hourly_paper_pnl AS h
                    (strategy_id,code,name,hour_utc,settled_runs_count,won_runs_count,lost_runs_count,stake_usd,
                     realized_pnl_usd,avg_pnl_usd,first_entered_at_utc,last_entered_at_utc,refreshed_at_utc)
                SELECT s.id,s.code,s.name,t.hour_utc,t.count,t.won,t.lost,t.stake,t.pnl,t.average,t.first_entry,t.last_entry,@Checked
                FROM totals t CROSS JOIN strategies s WHERE s.id=@Strategy AND (@DateDependent OR EXISTS(
                    SELECT 1 FROM date_dependent_strategy_hourly_paper_pnl old WHERE old.strategy_id=s.id AND old.hour_utc=t.hour_utc))
                ON CONFLICT(strategy_id,hour_utc) DO UPDATE SET
                    settled_runs_count=excluded.settled_runs_count,won_runs_count=excluded.won_runs_count,
                    lost_runs_count=excluded.lost_runs_count,stake_usd=excluded.stake_usd,
                    realized_pnl_usd=excluded.realized_pnl_usd,avg_pnl_usd=excluded.avg_pnl_usd,
                    first_entered_at_utc=excluded.first_entered_at_utc,last_entered_at_utc=excluded.last_entered_at_utc,
                    refreshed_at_utc=excluded.refreshed_at_utc;
                """);
            command.Parameters.AddWithValue("DateDependent", StrategyIds.DateDependentStrategyVariants.Any(x => x.Id == order.StrategyId));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private const string PaperConfirmationCopiedPerformanceSql = """
WITH selected_orders AS MATERIALIZED (
    SELECT
        po.id,
        po.copied_trader_wallet,
        po.condition_id,
        po.status,
        po.side,
        po.created_at_utc
    FROM paper_orders po
    JOIN temp_paper_copied_trader_performance_wallets selected
      ON selected.copied_trader_wallet = po.copied_trader_wallet
    WHERE po.copied_trader_wallet <> ''
),
selected_open_positions AS MATERIALIZED (
    SELECT
        pp.copied_trader_wallet,
        pp.condition_id,
        pp.unrealized_pnl_usd
    FROM paper_positions pp
    JOIN temp_paper_copied_trader_performance_wallets selected
      ON selected.copied_trader_wallet = pp.copied_trader_wallet
    WHERE pp.copied_trader_wallet <> ''
      AND pp.size_shares > 0
),
selected_settlements AS MATERIALIZED (
    SELECT
        ps.copied_trader_wallet,
        ps.condition_id,
        ps.category,
        ps.won,
        ps.settlement_value_usd,
        ps.realized_pnl_usd
    FROM paper_position_settlements ps
    JOIN temp_paper_copied_trader_performance_wallets selected
      ON selected.copied_trader_wallet = ps.copied_trader_wallet
    WHERE ps.copied_trader_wallet <> ''
),
required_condition_ids AS MATERIALIZED (
    SELECT condition_id FROM selected_orders
    UNION
    SELECT condition_id FROM selected_open_positions
    UNION
    SELECT condition_id
    FROM selected_settlements
    WHERE NULLIF(category, '') IS NULL
),
condition_categories AS MATERIALIZED (
    SELECT required.condition_id, latest.category
    FROM required_condition_ids required
    LEFT JOIN LATERAL (
        SELECT market.category
        FROM polymarket_gamma_markets market
        WHERE market.condition_id = required.condition_id
        ORDER BY market.fetched_at_utc DESC, market.market_id
        LIMIT 1
    ) latest ON true
),
source_metrics AS (
    SELECT
        orders.copied_trader_wallet,
        COALESCE(NULLIF(category.category, ''), 'unknown') AS category,
        COUNT(*)::integer AS orders_count,
        COUNT(*) FILTER (
            WHERE orders.status IN ('Filled', 'PartiallyFilled', 'PartiallyFilledExpired')
        )::integer AS filled_orders_count,
        COALESCE(SUM(CASE WHEN orders.side = 'Buy' THEN fills.fill_count ELSE 0 END), 0)::integer AS buy_fills_count,
        COALESCE(SUM(CASE WHEN orders.side = 'Sell' THEN fills.fill_count ELSE 0 END), 0)::integer AS sell_fills_count,
        0::integer AS open_positions_count,
        0::integer AS settled_positions_count,
        0::integer AS won_positions_count,
        0::integer AS lost_positions_count,
        COALESCE(SUM(CASE WHEN orders.side = 'Buy' THEN fills.notional_usd ELSE 0 END), 0) AS buy_cost_usd,
        COALESCE(SUM(CASE WHEN orders.side = 'Sell' THEN fills.notional_usd ELSE 0 END), 0) AS sell_proceeds_usd,
        0::numeric AS settlement_value_usd,
        COALESCE(SUM(fills.realized_pnl_usd), 0) AS realized_pnl_usd,
        0::numeric AS unrealized_pnl_usd,
        MIN(orders.created_at_utc) AS first_order_utc,
        MAX(orders.created_at_utc) AS last_order_utc
    FROM selected_orders orders
    LEFT JOIN condition_categories category
      ON category.condition_id = orders.condition_id
    LEFT JOIN LATERAL (
        SELECT
            COUNT(*) AS fill_count,
            COALESCE(SUM(fill.price * fill.size_shares), 0) AS notional_usd,
            COALESCE(SUM(fill.realized_pnl_usd), 0) AS realized_pnl_usd
        FROM paper_fills fill
        WHERE fill.paper_order_id = orders.id
        OFFSET 0
    ) fills ON true
    GROUP BY orders.copied_trader_wallet, COALESCE(NULLIF(category.category, ''), 'unknown')

    UNION ALL

    SELECT
        positions.copied_trader_wallet,
        COALESCE(NULLIF(category.category, ''), 'unknown') AS category,
        0, 0, 0, 0,
        COUNT(*)::integer,
        0, 0, 0,
        0, 0, 0, 0,
        COALESCE(SUM(positions.unrealized_pnl_usd), 0),
        NULL::timestamptz,
        NULL::timestamptz
    FROM selected_open_positions positions
    LEFT JOIN condition_categories category
      ON category.condition_id = positions.condition_id
    GROUP BY positions.copied_trader_wallet, COALESCE(NULLIF(category.category, ''), 'unknown')

    UNION ALL

    SELECT
        settlements.copied_trader_wallet,
        COALESCE(NULLIF(settlements.category, ''), NULLIF(category.category, ''), 'unknown') AS category,
        0, 0, 0, 0,
        0,
        COUNT(*)::integer,
        COUNT(*) FILTER (WHERE settlements.won)::integer,
        COUNT(*) FILTER (WHERE NOT settlements.won)::integer,
        0, 0,
        COALESCE(SUM(settlements.settlement_value_usd), 0),
        COALESCE(SUM(settlements.realized_pnl_usd), 0),
        0,
        NULL::timestamptz,
        NULL::timestamptz
    FROM selected_settlements settlements
    LEFT JOIN condition_categories category
      ON category.condition_id = settlements.condition_id
    GROUP BY
        settlements.copied_trader_wallet,
        COALESCE(NULLIF(settlements.category, ''), NULLIF(category.category, ''), 'unknown')
),
grouped AS (
    SELECT
           copied_trader_wallet,
           CASE WHEN GROUPING(category) = 1 THEN 'OVERALL' ELSE category END AS category,
           SUM(orders_count)::integer AS orders_count,
           SUM(filled_orders_count)::integer AS filled_orders_count,
           SUM(buy_fills_count)::integer AS buy_fills_count,
           SUM(sell_fills_count)::integer AS sell_fills_count,
           SUM(open_positions_count)::integer AS open_positions_count,
           SUM(settled_positions_count)::integer AS settled_positions_count,
           SUM(won_positions_count)::integer AS won_positions_count,
           SUM(lost_positions_count)::integer AS lost_positions_count,
           SUM(buy_cost_usd) AS buy_cost_usd,
           SUM(sell_proceeds_usd) AS sell_proceeds_usd,
           SUM(settlement_value_usd) AS settlement_value_usd,
           SUM(realized_pnl_usd) AS realized_pnl_usd,
           SUM(unrealized_pnl_usd) AS unrealized_pnl_usd,
           MIN(first_order_utc) AS first_order_utc,
           MAX(last_order_utc) AS last_order_utc
    FROM source_metrics
    GROUP BY GROUPING SETS (
        (copied_trader_wallet, category),
        (copied_trader_wallet)
    )
),
scored AS (
    SELECT *,
           realized_pnl_usd + unrealized_pnl_usd AS total_pnl_usd,
           CASE WHEN buy_cost_usd = 0 THEN 0 ELSE (realized_pnl_usd + unrealized_pnl_usd) / buy_cost_usd * 100 END AS roi_pct,
           CASE WHEN settled_positions_count = 0 THEN 0 ELSE won_positions_count::numeric / settled_positions_count * 100 END AS win_rate_pct
    FROM grouped
),
inserted AS (
    INSERT INTO paper_copied_trader_performance (
        copied_trader_wallet, category, orders_count, filled_orders_count, buy_fills_count,
        sell_fills_count, open_positions_count, settled_positions_count, won_positions_count,
        lost_positions_count, buy_cost_usd, sell_proceeds_usd, settlement_value_usd,
        realized_pnl_usd, unrealized_pnl_usd, total_pnl_usd, roi_pct, win_rate_pct,
        score, first_order_utc, last_order_utc, refreshed_at_utc
    )
    SELECT
        copied_trader_wallet,
        category,
        orders_count,
        filled_orders_count,
        buy_fills_count,
        sell_fills_count,
        open_positions_count,
        settled_positions_count,
        won_positions_count,
        lost_positions_count,
        buy_cost_usd,
        sell_proceeds_usd,
        settlement_value_usd,
        realized_pnl_usd,
        unrealized_pnl_usd,
        total_pnl_usd,
        roi_pct,
        win_rate_pct,
        greatest(0, least(100,
            50
            + greatest(-50, least(50, roi_pct)) * 0.35
            + (win_rate_pct - 50) * 0.25
            + greatest(-20, least(20, total_pnl_usd)) * 1.25
            + least(settled_positions_count, 20) * 0.5
            - lost_positions_count * 1.25
            - open_positions_count * 0.1
        )) AS score,
        first_order_utc,
        last_order_utc,
        now()
    FROM scored
    RETURNING 1
)
SELECT count(*)::integer FROM inserted;
""";
}
