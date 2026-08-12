using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    private const int HistoricalPaperFakFeeBackfillMaxPageSize = 500;
    private const int HistoricalPaperFakFeeBackfillCommandTimeoutSeconds = 10;
    private const string HistoricalPaperFakDirectSource = "btc_updown5m_fak_taker_paper";
    private const string HistoricalPaperFakChildSource = "btc_updown5m_child_mirror_fak_paper";
    private const string HistoricalPaperFakFeeCalculationSourcePrefix =
        "historical-current-paper-model-v1:";

    public async Task<IReadOnlyList<HistoricalPaperFakFeeBackfillStrategyRank>>
        GetHistoricalPaperFakFeeBackfillStrategyRanksAsync(
            DateTimeOffset filledBeforeUtc,
            CancellationToken cancellationToken = default)
    {
        // Rank from the materialized lifetime Gross shown by the Dashboard. Exact
        // LegacyUnknown/cutoff eligibility stays in the strategy-bound page query;
        // joining it here forces a multi-million-row fill/order scan before every
        // sweep. The raw accounting formula remains a fail-safe for a source
        // strategy whose Dashboard snapshot has not been materialized yet.
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, $$"""
SELECT
    strategy.id,
    strategy.code,
    CASE
        WHEN performance.strategy_id IS NOT NULL
        THEN performance.realized_pnl_usd
        WHEN EXISTS (
            SELECT 1
            FROM public.strategy_market_paper_runs run_presence
            WHERE run_presence.strategy_id = strategy.id)
          OR EXISTS (
            SELECT 1
            FROM public.strategy_paper_skip_rollups rollup_presence
            WHERE rollup_presence.strategy_id = strategy.id)
        THEN COALESCE((
            SELECT SUM(COALESCE(run.realized_pnl_usd, 0))
            FROM public.strategy_market_paper_runs run
            WHERE run.strategy_id = strategy.id
              AND run.status = '{{StrategyMarketPaperRunStatuses.Settled}}'), 0)
        ELSE
            COALESCE((
                SELECT SUM(fill_all.realized_pnl_usd)
                FROM public.paper_orders paper_order_all
                INNER JOIN public.paper_fills fill_all
                    ON fill_all.paper_order_id = paper_order_all.id
                WHERE paper_order_all.strategy_id = strategy.id), 0)
            +
            COALESCE((
                SELECT SUM(settlement.realized_pnl_usd)
                FROM public.paper_position_settlements settlement
                WHERE (
                    strategy.id = @FollowLeaderStrategyId
                    AND lower(settlement.copied_trader_wallet) NOT LIKE 'strategy:%')
                   OR (
                    strategy.id <> @FollowLeaderStrategyId
                    AND lower(settlement.copied_trader_wallet) =
                        lower('strategy:' || strategy.code))), 0)
    END AS gross_realized_pnl_usd
FROM public.strategies strategy
LEFT JOIN public.dashboard_strategy_performance_snapshots performance
    ON performance.strategy_id = strategy.id
CROSS JOIN LATERAL (
    SELECT 1
    FROM public.paper_orders source_order
    WHERE source_order.strategy_id = strategy.id
      AND source_order.side = '{{TradeSide.Buy}}'
      AND source_order.execution_source IN (
           '{{HistoricalPaperFakDirectSource}}',
           '{{HistoricalPaperFakChildSource}}')
    LIMIT 1
) historical_source
ORDER BY gross_realized_pnl_usd DESC, strategy.id;
""");
        command.CommandTimeout = HistoricalPaperFakFeeBackfillCommandTimeoutSeconds;
        command.Parameters.AddWithValue("FollowLeaderStrategyId", StrategyIds.FollowLeader);

        var strategies = new List<HistoricalPaperFakFeeBackfillStrategyRank>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            strategies.Add(new HistoricalPaperFakFeeBackfillStrategyRank(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetDecimal(2)));
        }

        return strategies;
    }

    public async Task<HistoricalPaperFakFeeBackfillPage> GetHistoricalPaperFakFeeBackfillCandidatesAsync(
        DateTimeOffset filledBeforeUtc,
        Guid strategyId,
        int limit,
        HistoricalPaperFakFeeBackfillCursor? afterCursor = null,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new HistoricalPaperFakFeeBackfillPage([], null, true);
        }

        if (afterCursor is not null && afterCursor.StrategyId != strategyId)
        {
            throw new ArgumentException(
                "Historical Paper FAK fee backfill cursor belongs to another strategy.",
                nameof(afterCursor));
        }

        var pageSize = Math.Min(limit, HistoricalPaperFakFeeBackfillMaxPageSize);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, $$"""
SELECT
    paper_order.id,
    paper_order.signal_id,
    paper_order.strategy_id,
    paper_order.copied_trader_wallet,
    paper_order.status,
    paper_order.side,
    paper_order.asset_id,
    paper_order.condition_id,
    paper_order.outcome,
    paper_order.price,
    paper_order.size_shares,
    paper_order.notional_usd,
    paper_order.created_at_utc,
    paper_order.expires_at_utc,
    paper_order.filled_at_utc,
    paper_order.cancelled_at_utc,
    paper_order.raw_decision_json::text,
    paper_order.correlation_id,
    paper_order.execution_source,
    fill.id,
    fill.paper_order_id,
    fill.price,
    fill.size_shares,
    fill.filled_at_utc,
    fill.evidence,
    fill.realized_pnl_usd,
    fill.fee_usd,
    fill.fee_accounting_status,
    fill.fee_liquidity_role,
    fill.fee_calculation_source,
    fill.fee_rate,
    fill.fee_exponent,
    fill.fee_taker_only,
    fill.fee_calculated_at_utc,
    fill.net_realized_pnl_usd
FROM public.paper_fills fill
INNER JOIN public.paper_orders paper_order ON paper_order.id = fill.paper_order_id
WHERE fill.fee_accounting_status = '{{FeeAccountingStatus.LegacyUnknown}}'
  AND fill.filled_at_utc < @FilledBeforeUtc
  AND paper_order.strategy_id = @StrategyId
  AND paper_order.side = '{{TradeSide.Buy}}'
  AND paper_order.execution_source IN (
      '{{HistoricalPaperFakDirectSource}}',
      '{{HistoricalPaperFakChildSource}}')
  AND (
      NOT @HasCursor
      OR fill.filled_at_utc > @AfterFilledAtUtc
      OR (
          fill.filled_at_utc = @AfterFilledAtUtc
          AND fill.paper_order_id > @AfterPaperOrderId)
      OR (
          fill.filled_at_utc = @AfterFilledAtUtc
          AND fill.paper_order_id = @AfterPaperOrderId
          AND fill.id > @AfterFillId)
  )
ORDER BY fill.filled_at_utc, fill.paper_order_id, fill.id
LIMIT @FetchLimit;
""");
        command.CommandTimeout = HistoricalPaperFakFeeBackfillCommandTimeoutSeconds;
        command.Parameters.Add("FilledBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(filledBeforeUtc);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("HasCursor", afterCursor is not null);
        command.Parameters.Add("AfterFilledAtUtc", NpgsqlDbType.TimestampTz).Value =
            UtcDateTime(afterCursor?.FilledAtUtc ?? DateTimeOffset.MinValue);
        command.Parameters.AddWithValue("AfterPaperOrderId", afterCursor?.PaperOrderId ?? Guid.Empty);
        command.Parameters.AddWithValue("AfterFillId", afterCursor?.FillId ?? Guid.Empty);
        command.Parameters.AddWithValue("FetchLimit", pageSize + 1);

        var scanned = new List<HistoricalPaperFakFeeBackfillCandidate>(pageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            scanned.Add(new HistoricalPaperFakFeeBackfillCandidate(
                ReadHistoricalPaperFakOrder(reader),
                ReadHistoricalPaperFakFill(reader)));
        }

        var reachedEnd = scanned.Count <= pageSize;
        var candidates = scanned.Take(pageSize).ToArray();
        var continuationCursor = candidates.Length == 0
            ? null
            : new HistoricalPaperFakFeeBackfillCursor(
                strategyId,
                candidates[^1].Fill.FilledAtUtc,
                candidates[^1].Fill.PaperOrderId,
                candidates[^1].Fill.Id);
        return new HistoricalPaperFakFeeBackfillPage(candidates, continuationCursor, reachedEnd);
    }

    public async Task<HistoricalPaperFakFeeBackfillBatchResult> ApplyHistoricalPaperFakFeeBackfillBatchAsync(
        IReadOnlyList<HistoricalPaperFakFeeBackfillUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
        {
            return EmptyHistoricalPaperFakFeeBackfillResult;
        }

        ValidateHistoricalPaperFakFeeBackfillUpdates(updates);
        var databaseUpdates = updates.Select(CreateHistoricalPaperFakFeeBackfillDatabaseUpdate).ToArray();
        var updatesJson = JsonSerializer.Serialize(databaseUpdates, BulkInsertJsonOptions);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await ConfigureHistoricalPaperFakFeeBackfillTransactionAsync(
                connection,
                transaction,
                cancellationToken);
            await LockHistoricalPaperFakFeeBackfillPositionKeysAsync(
                connection,
                transaction,
                updates,
                cancellationToken);

            await using var command = CreateCommand(connection, HistoricalPaperFakFeeBackfillApplySql);
            command.Transaction = transaction;
            command.CommandTimeout = HistoricalPaperFakFeeBackfillCommandTimeoutSeconds;
            AddJsonbParameter(command, "UpdatesJson", updatesJson);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Historical Paper FAK fee backfill did not return a batch result.");
            }

            var result = new HistoricalPaperFakFeeBackfillBatchResult(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7));
            await reader.DisposeAsync();
            var chainsRequiringUpdate = result.Eligible - result.AlreadyApplied;
            if (result.FillsUpdated != chainsRequiringUpdate ||
                result.RunsUpdated != chainsRequiringUpdate ||
                result.PositionsUpdated != chainsRequiringUpdate ||
                result.SettlementsUpdated != chainsRequiringUpdate)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new InvalidOperationException(
                    "Historical Paper FAK fee backfill did not update every eligible dependent row atomically.");
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.QueryCanceled)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new HistoricalPaperFakFeeBackfillBatchResult(
                updates.Count,
                0,
                0,
                0,
                0,
                0,
                0,
                updates.Count);
        }
    }

    private static readonly HistoricalPaperFakFeeBackfillBatchResult EmptyHistoricalPaperFakFeeBackfillResult =
        new(0, 0, 0, 0, 0, 0, 0, 0);

    internal const string HistoricalPaperFakFeeBackfillApplySql = """
WITH requested AS MATERIALIZED (
    SELECT requested_row.*
    FROM jsonb_to_recordset(CAST(@UpdatesJson AS jsonb)) AS requested_row(
        paper_order_id uuid,
        expected_strategy_id uuid,
        expected_wallet text,
        expected_order_status text,
        expected_asset_id text,
        expected_condition_id text,
        expected_outcome text,
        expected_order_price numeric,
        expected_order_size numeric,
        expected_order_notional numeric,
        expected_order_filled_at timestamptz,
        expected_order_cancelled_at timestamptz,
        expected_execution_source text,
        fill_id uuid,
        expected_fill_price numeric,
        expected_fill_size numeric,
        expected_filled_at timestamptz,
        expected_evidence text,
        expected_realized numeric,
        expected_fee numeric,
        expected_fee_status text,
        expected_fee_role text,
        expected_fee_source text,
        expected_fee_rate numeric,
        expected_fee_exponent integer,
        expected_fee_taker_only boolean,
        expected_fee_calculated_at timestamptz,
        expected_net numeric,
        desired_fee numeric,
        desired_fee_status text,
        desired_fee_role text,
        desired_fee_source text,
        desired_fee_rate numeric,
        desired_fee_exponent integer,
        desired_fee_taker_only boolean,
        desired_fee_calculated_at timestamptz,
        desired_net numeric)
),
structural_chain AS MATERIALIZED (
    SELECT
        requested.*,
        run.id AS run_id,
        position.id AS position_id,
        settlement.id AS settlement_id,
        fill.fee_usd AS actual_fill_fee,
        fill.fee_accounting_status AS actual_fill_status,
        fill.fee_liquidity_role AS actual_fill_role,
        fill.fee_calculation_source AS actual_fill_source,
        fill.fee_rate AS actual_fill_rate,
        fill.fee_exponent AS actual_fill_exponent,
        fill.fee_taker_only AS actual_fill_taker_only,
        fill.fee_calculated_at_utc AS actual_fill_calculated_at,
        fill.net_realized_pnl_usd AS actual_fill_net,
        run.realized_pnl_usd AS run_realized,
        run.fee_usd AS actual_run_fee,
        run.fee_accounting_status AS actual_run_status,
        run.fee_liquidity_role AS actual_run_role,
        run.fee_calculation_source AS actual_run_source,
        run.fee_rate AS actual_run_rate,
        run.fee_exponent AS actual_run_exponent,
        run.fee_taker_only AS actual_run_taker_only,
        run.fee_calculated_at_utc AS actual_run_calculated_at,
        run.net_realized_pnl_usd AS actual_run_net,
        position.fee_usd AS actual_position_fee,
        position.fee_accounting_status AS actual_position_status,
        position.fee_liquidity_role AS actual_position_role,
        position.fee_calculation_source AS actual_position_source,
        position.fee_rate AS actual_position_rate,
        position.fee_exponent AS actual_position_exponent,
        position.fee_taker_only AS actual_position_taker_only,
        position.fee_calculated_at_utc AS actual_position_calculated_at,
        position.net_unrealized_pnl_usd AS actual_position_net,
        settlement.realized_pnl_usd AS settlement_realized,
        settlement.fee_usd AS actual_settlement_fee,
        settlement.fee_accounting_status AS actual_settlement_status,
        settlement.fee_liquidity_role AS actual_settlement_role,
        settlement.fee_calculation_source AS actual_settlement_source,
        settlement.fee_rate AS actual_settlement_rate,
        settlement.fee_exponent AS actual_settlement_exponent,
        settlement.fee_taker_only AS actual_settlement_taker_only,
        settlement.fee_calculated_at_utc AS actual_settlement_calculated_at,
        settlement.net_realized_pnl_usd AS actual_settlement_net
    FROM requested
    INNER JOIN public.paper_orders paper_order
        ON paper_order.id = requested.paper_order_id
       AND paper_order.strategy_id = requested.expected_strategy_id
       AND paper_order.copied_trader_wallet = requested.expected_wallet
       AND paper_order.status = requested.expected_order_status
       AND paper_order.side = 'Buy'
       AND paper_order.asset_id = requested.expected_asset_id
       AND paper_order.condition_id = requested.expected_condition_id
       AND paper_order.outcome = requested.expected_outcome
       AND paper_order.price = requested.expected_order_price
       AND paper_order.size_shares = requested.expected_order_size
       AND paper_order.notional_usd = requested.expected_order_notional
       AND paper_order.filled_at_utc IS NOT DISTINCT FROM requested.expected_order_filled_at
       AND paper_order.cancelled_at_utc IS NOT DISTINCT FROM requested.expected_order_cancelled_at
       AND paper_order.execution_source = requested.expected_execution_source
       AND paper_order.status IN ('Filled', 'PartiallyFilledExpired')
       AND paper_order.execution_source IN (
           'btc_updown5m_fak_taker_paper',
           'btc_updown5m_child_mirror_fak_paper')
    INNER JOIN public.paper_fills fill
        ON fill.id = requested.fill_id
       AND fill.paper_order_id = paper_order.id
       AND fill.price = requested.expected_fill_price
       AND fill.size_shares = requested.expected_fill_size
       AND fill.filled_at_utc = requested.expected_filled_at
       AND fill.evidence = requested.expected_evidence
       AND fill.realized_pnl_usd = requested.expected_realized
    INNER JOIN public.strategy_market_paper_runs run
        ON run.paper_order_id = paper_order.id
       AND run.strategy_id = paper_order.strategy_id
       AND run.condition_id = paper_order.condition_id
       AND run.status = 'Settled'
       AND run.selected_asset_id = paper_order.asset_id
       AND run.selected_outcome = paper_order.outcome
       AND run.entry_price = fill.price
       AND run.size_shares = fill.size_shares
       AND run.stake_usd = round(fill.price * fill.size_shares, 8)
       AND run.realized_pnl_usd IS NOT NULL
       AND run.settled_at_utc IS NOT NULL
    INNER JOIN public.paper_positions position
        ON position.copied_trader_wallet = paper_order.copied_trader_wallet
       AND position.asset_id = paper_order.asset_id
       AND position.condition_id = paper_order.condition_id
       AND position.outcome = paper_order.outcome
       AND position.size_shares = 0
       AND position.average_price = 0
       AND position.estimated_value_usd = 0
       AND position.unrealized_pnl_usd = 0
    INNER JOIN public.paper_position_settlements settlement
        ON settlement.copied_trader_wallet = paper_order.copied_trader_wallet
       AND settlement.asset_id = paper_order.asset_id
       AND settlement.condition_id = paper_order.condition_id
       AND settlement.outcome = paper_order.outcome
       AND settlement.settled_size_shares = fill.size_shares
       AND settlement.average_price = fill.price
       AND settlement.cost_basis_usd = round(fill.price * fill.size_shares, 8)
       AND settlement.settlement_value_usd = run.settlement_value_usd
       AND settlement.realized_pnl_usd = run.realized_pnl_usd
       AND settlement.settled_at_utc = run.settled_at_utc
       AND settlement.settlement_source = 'BtcUpDown5mGammaClosedMarket'
       AND run.settlement_price = CASE WHEN settlement.won THEN 1 ELSE 0 END
    WHERE paper_order.price = fill.price
      AND paper_order.size_shares = fill.size_shares
      AND (
          SELECT count(*)
          FROM requested sibling_request
          WHERE sibling_request.paper_order_id = requested.paper_order_id) = 1
      AND (
          SELECT count(*)
          FROM requested sibling_request
          WHERE sibling_request.fill_id = requested.fill_id) = 1
      AND (
          SELECT count(*)
          FROM requested sibling_request
          WHERE sibling_request.expected_wallet = requested.expected_wallet
            AND sibling_request.expected_asset_id = requested.expected_asset_id) = 1
      AND (
          SELECT count(*)
          FROM public.paper_fills sibling_fill
          WHERE sibling_fill.paper_order_id = paper_order.id) = 1
      AND (
          SELECT count(*)
          FROM public.paper_orders sibling_order
          WHERE sibling_order.copied_trader_wallet = paper_order.copied_trader_wallet
            AND sibling_order.asset_id = paper_order.asset_id
            AND sibling_order.execution_source IN (
                'btc_updown5m_fak_taker_paper',
                'btc_updown5m_child_mirror_fak_paper')) = 1
      AND (
          SELECT count(*)
          FROM public.strategy_market_paper_runs sibling_run
          WHERE sibling_run.paper_order_id = paper_order.id) = 1
      AND (
          SELECT count(*)
          FROM public.paper_positions sibling_position
          WHERE sibling_position.copied_trader_wallet = paper_order.copied_trader_wallet
            AND sibling_position.asset_id = paper_order.asset_id) = 1
      AND (
          SELECT count(*)
          FROM public.paper_position_settlements sibling_settlement
          WHERE sibling_settlement.copied_trader_wallet = paper_order.copied_trader_wallet
            AND sibling_settlement.asset_id = paper_order.asset_id) = 1
    ORDER BY
        paper_order.copied_trader_wallet COLLATE "C",
        paper_order.asset_id COLLATE "C",
        paper_order.id
    FOR UPDATE OF paper_order, fill, run, position, settlement
),
classified AS MATERIALIZED (
    SELECT
        structural_chain.*,
        (
            expected_fee = 0
            AND expected_fee_status = 'LegacyUnknown'
            AND expected_fee_role = 'Unknown'
            AND expected_fee_source = ''
            AND expected_fee_rate IS NULL
            AND expected_fee_exponent IS NULL
            AND expected_fee_taker_only IS NULL
            AND expected_fee_calculated_at IS NULL
            AND expected_net IS NULL
            AND actual_fill_fee = 0
            AND actual_fill_status = 'LegacyUnknown'
            AND actual_fill_role = 'Unknown'
            AND actual_fill_source = ''
            AND actual_fill_rate IS NULL
            AND actual_fill_exponent IS NULL
            AND actual_fill_taker_only IS NULL
            AND actual_fill_calculated_at IS NULL
            AND actual_fill_net IS NULL
            AND actual_run_fee = 0
            AND actual_run_status = 'LegacyUnknown'
            AND actual_run_role = 'Unknown'
            AND actual_run_source = ''
            AND actual_run_rate IS NULL
            AND actual_run_exponent IS NULL
            AND actual_run_taker_only IS NULL
            AND actual_run_calculated_at IS NULL
            AND actual_run_net IS NULL
            AND actual_settlement_fee = 0
            AND actual_settlement_status = 'LegacyUnknown'
            AND actual_settlement_role = 'Unknown'
            AND actual_settlement_source = ''
            AND actual_settlement_rate IS NULL
            AND actual_settlement_exponent IS NULL
            AND actual_settlement_taker_only IS NULL
            AND actual_settlement_calculated_at IS NULL
            AND actual_settlement_net IS NULL
            AND (
                (
                    actual_position_fee = 0
                    AND actual_position_status = 'LegacyUnknown'
                    AND actual_position_role = 'Unknown'
                    AND actual_position_source = ''
                    AND actual_position_rate IS NULL
                    AND actual_position_exponent IS NULL
                    AND actual_position_taker_only IS NULL
                    AND actual_position_calculated_at IS NULL
                    AND actual_position_net IS NULL)
                OR (
                    actual_position_fee = 0
                    AND actual_position_status = 'Calculated'
                    AND actual_position_role = 'Unknown'
                    AND actual_position_source = ''
                    AND actual_position_rate IS NULL
                    AND actual_position_exponent IS NULL
                    AND actual_position_taker_only IS NULL
                    AND actual_position_calculated_at IS NULL
                    AND actual_position_net = 0)
            )
        ) AS requires_update,
        (
            actual_fill_fee = desired_fee
            AND actual_fill_status = desired_fee_status
            AND actual_fill_role = desired_fee_role
            AND actual_fill_source = desired_fee_source
            AND actual_fill_rate IS NOT DISTINCT FROM desired_fee_rate
            AND actual_fill_exponent IS NOT DISTINCT FROM desired_fee_exponent
            AND actual_fill_taker_only IS NOT DISTINCT FROM desired_fee_taker_only
            AND actual_fill_calculated_at IS NOT DISTINCT FROM desired_fee_calculated_at
            AND actual_fill_net IS NOT DISTINCT FROM desired_net
            AND actual_run_fee = desired_fee
            AND actual_run_status = desired_fee_status
            AND actual_run_role = desired_fee_role
            AND actual_run_source = desired_fee_source
            AND actual_run_rate IS NOT DISTINCT FROM desired_fee_rate
            AND actual_run_exponent IS NOT DISTINCT FROM desired_fee_exponent
            AND actual_run_taker_only IS NOT DISTINCT FROM desired_fee_taker_only
            AND actual_run_calculated_at IS NOT DISTINCT FROM desired_fee_calculated_at
            AND actual_run_net IS NOT DISTINCT FROM (
                CASE WHEN desired_fee_status = 'Calculated'
                    THEN run_realized - desired_fee
                    ELSE NULL
                END)
            AND actual_position_fee = 0
            AND actual_position_status = 'Calculated'
            AND actual_position_role = 'Unknown'
            AND actual_position_source = ''
            AND actual_position_rate IS NULL
            AND actual_position_exponent IS NULL
            AND actual_position_taker_only IS NULL
            AND actual_position_calculated_at IS NULL
            AND actual_position_net = 0
            AND actual_settlement_fee = desired_fee
            AND actual_settlement_status = desired_fee_status
            AND actual_settlement_role = desired_fee_role
            AND actual_settlement_source = desired_fee_source
            AND actual_settlement_rate IS NOT DISTINCT FROM desired_fee_rate
            AND actual_settlement_exponent IS NOT DISTINCT FROM desired_fee_exponent
            AND actual_settlement_taker_only IS NOT DISTINCT FROM desired_fee_taker_only
            AND actual_settlement_calculated_at IS NOT DISTINCT FROM desired_fee_calculated_at
            AND actual_settlement_net IS NOT DISTINCT FROM (
                CASE WHEN desired_fee_status = 'Calculated'
                    THEN settlement_realized - desired_fee
                    ELSE NULL
                END)
        ) AS already_applied
    FROM structural_chain
),
eligible AS MATERIALIZED (
    SELECT *
    FROM classified
    WHERE requires_update OR already_applied
),
fill_updates AS (
    UPDATE public.paper_fills target
    SET
        fee_usd = eligible.desired_fee,
        fee_accounting_status = eligible.desired_fee_status,
        fee_liquidity_role = eligible.desired_fee_role,
        fee_calculation_source = eligible.desired_fee_source,
        fee_rate = eligible.desired_fee_rate,
        fee_exponent = eligible.desired_fee_exponent,
        fee_taker_only = eligible.desired_fee_taker_only,
        fee_calculated_at_utc = eligible.desired_fee_calculated_at,
        net_realized_pnl_usd = eligible.desired_net
    FROM eligible
    WHERE eligible.requires_update
      AND target.id = eligible.fill_id
    RETURNING target.id, target.paper_order_id
),
run_updates AS (
    UPDATE public.strategy_market_paper_runs target
    SET
        fee_usd = eligible.desired_fee,
        fee_accounting_status = eligible.desired_fee_status,
        fee_liquidity_role = eligible.desired_fee_role,
        fee_calculation_source = eligible.desired_fee_source,
        fee_rate = eligible.desired_fee_rate,
        fee_exponent = eligible.desired_fee_exponent,
        fee_taker_only = eligible.desired_fee_taker_only,
        fee_calculated_at_utc = eligible.desired_fee_calculated_at,
        net_realized_pnl_usd = CASE WHEN eligible.desired_fee_status = 'Calculated'
            THEN target.realized_pnl_usd - eligible.desired_fee
            ELSE NULL
        END
    FROM eligible
    INNER JOIN fill_updates ON fill_updates.id = eligible.fill_id
    WHERE target.id = eligible.run_id
    RETURNING target.id
),
position_updates AS (
    UPDATE public.paper_positions target
    SET
        fee_usd = 0,
        fee_accounting_status = 'Calculated',
        fee_liquidity_role = 'Unknown',
        fee_calculation_source = '',
        fee_rate = NULL,
        fee_exponent = NULL,
        fee_taker_only = NULL,
        fee_calculated_at_utc = NULL,
        net_unrealized_pnl_usd = 0
    FROM eligible
    INNER JOIN fill_updates ON fill_updates.id = eligible.fill_id
    WHERE target.id = eligible.position_id
    RETURNING target.id
),
settlement_updates AS (
    UPDATE public.paper_position_settlements target
    SET
        fee_usd = eligible.desired_fee,
        fee_accounting_status = eligible.desired_fee_status,
        fee_liquidity_role = eligible.desired_fee_role,
        fee_calculation_source = eligible.desired_fee_source,
        fee_rate = eligible.desired_fee_rate,
        fee_exponent = eligible.desired_fee_exponent,
        fee_taker_only = eligible.desired_fee_taker_only,
        fee_calculated_at_utc = eligible.desired_fee_calculated_at,
        net_realized_pnl_usd = CASE WHEN eligible.desired_fee_status = 'Calculated'
            THEN target.realized_pnl_usd - eligible.desired_fee
            ELSE NULL
        END
    FROM eligible
    INNER JOIN fill_updates ON fill_updates.id = eligible.fill_id
    WHERE target.id = eligible.settlement_id
    RETURNING target.id
)
SELECT
    (SELECT count(*)::integer FROM requested) AS requested,
    (SELECT count(*)::integer FROM eligible) AS eligible,
    (SELECT count(*)::integer FROM fill_updates) AS fills_updated,
    (SELECT count(*)::integer FROM run_updates) AS runs_updated,
    (SELECT count(*)::integer FROM position_updates) AS positions_updated,
    (SELECT count(*)::integer FROM settlement_updates) AS settlements_updated,
    (SELECT count(*)::integer FROM eligible WHERE already_applied) AS already_applied,
    ((SELECT count(*) FROM requested) - (SELECT count(*) FROM eligible))::integer AS conflicts_or_deferred;
""";

    private static async Task ConfigureHistoricalPaperFakFeeBackfillTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, """
SET LOCAL lock_timeout = '250ms';
SET LOCAL statement_timeout = '10s';
SET LOCAL work_mem = '4MB';
SET LOCAL max_parallel_workers_per_gather = 0;
""");
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockHistoricalPaperFakFeeBackfillPositionKeysAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<HistoricalPaperFakFeeBackfillUpdate> updates,
        CancellationToken cancellationToken)
    {
        var wallets = updates
            .Select(update => update.Expected.Order.CopiedTraderWallet)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await LockPaperWalletsAsync(connection, transaction, wallets, cancellationToken);

        var keys = updates
            .Select(update => new
            {
                copied_trader_wallet = update.Expected.Order.CopiedTraderWallet,
                asset_id = update.Expected.Order.AssetId
            })
            .Distinct()
            .ToArray();
        await using var command = CreateCommand(connection, """
WITH requested_position_keys AS (
    SELECT position_key.copied_trader_wallet, position_key.asset_id
    FROM jsonb_to_recordset(CAST(@PositionKeysJson AS jsonb)) AS position_key(
        copied_trader_wallet text,
        asset_id text)
)
SELECT position.id
FROM public.paper_positions position
INNER JOIN requested_position_keys requested
    ON requested.copied_trader_wallet = position.copied_trader_wallet
   AND requested.asset_id = position.asset_id
ORDER BY position.copied_trader_wallet COLLATE "C", position.asset_id COLLATE "C"
FOR UPDATE OF position;
""");
        command.Transaction = transaction;
        AddJsonbParameter(command, "PositionKeysJson", JsonSerializer.Serialize(keys));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
        }
    }

    internal static void ValidateHistoricalPaperFakFeeBackfillUpdates(
        IReadOnlyList<HistoricalPaperFakFeeBackfillUpdate> updates)
    {
        foreach (var update in updates)
        {
            ArgumentNullException.ThrowIfNull(update);
            ArgumentNullException.ThrowIfNull(update.Expected);
            ArgumentNullException.ThrowIfNull(update.Expected.Order);
            ArgumentNullException.ThrowIfNull(update.Expected.Fill);
            ArgumentNullException.ThrowIfNull(update.EvaluatedFill);

            var order = update.Expected.Order;
            var expected = update.Expected.Fill;
            var evaluated = update.EvaluatedFill;
            if (expected.PaperOrderId != order.Id || evaluated.PaperOrderId != order.Id)
            {
                throw new ArgumentException("Historical Paper FAK fee backfill fill/order identity does not match.", nameof(updates));
            }

            if (order.Side != TradeSide.Buy ||
                order.ExecutionSource is not (HistoricalPaperFakDirectSource or HistoricalPaperFakChildSource))
            {
                throw new ArgumentException("Historical Paper FAK fee backfill expected row is outside the fixed pure-Paper legacy scope.", nameof(updates));
            }

            if (evaluated.Id != expected.Id ||
                evaluated.Price != expected.Price ||
                evaluated.SizeShares != expected.SizeShares ||
                evaluated.FilledAtUtc != expected.FilledAtUtc ||
                !string.Equals(evaluated.Evidence, expected.Evidence, StringComparison.Ordinal) ||
                evaluated.RealizedPnlUsd != expected.RealizedPnlUsd ||
                evaluated.NetRealizedPnlUsd != expected.NetRealizedPnlUsd)
            {
                throw new ArgumentException("Historical Paper FAK fee evaluation changed fill identity or gross accounting fields.", nameof(updates));
            }

            var desiredStatus = FeeAccountingRules.ParseStatus(evaluated.FeeAccountingStatus);
            if (desiredStatus is not (FeeAccountingStatus.Calculated or FeeAccountingStatus.CalculationUnavailable) ||
                !string.Equals(evaluated.FeeLiquidityRole, FeeLiquidityRole.Taker.ToString(), StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(evaluated.FeeCalculationSource) ||
                !evaluated.FeeCalculationSource.StartsWith(
                    HistoricalPaperFakFeeCalculationSourcePrefix,
                    StringComparison.Ordinal) ||
                evaluated.FeeCalculatedAtUtc is null ||
                evaluated.FeeUsd < 0m)
            {
                throw new ArgumentException("Historical Paper FAK fee evaluation has an unsupported accounting result.", nameof(updates));
            }

            var underlyingCalculationSource = evaluated.FeeCalculationSource[
                HistoricalPaperFakFeeCalculationSourcePrefix.Length..];
            if (desiredStatus == FeeAccountingStatus.Calculated)
            {
                var isCurveResult = string.Equals(
                    underlyingCalculationSource,
                    PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
                    StringComparison.Ordinal);
                var isFeeFreeResult = string.Equals(
                    underlyingCalculationSource,
                    PolymarketFeeCalculationConstants.FeeFreeMarketCalculationSource,
                    StringComparison.Ordinal);
                if (isCurveResult &&
                    (evaluated.FeeRate is null ||
                     evaluated.FeeExponent is null or < 0 ||
                     evaluated.FeeTakerOnly is null))
                {
                    throw new ArgumentException(
                        "A calculated Historical Paper FAK curve fee requires a complete fee schedule.",
                        nameof(updates));
                }

                if (isFeeFreeResult &&
                    (evaluated.FeeUsd != 0m ||
                     evaluated.FeeRate is not null ||
                     evaluated.FeeExponent is not null ||
                     evaluated.FeeTakerOnly is not null))
                {
                    throw new ArgumentException(
                        "A fee-free Historical Paper FAK result must have zero fee and no fd schedule.",
                        nameof(updates));
                }

                if (!isCurveResult && !isFeeFreeResult)
                {
                    throw new ArgumentException(
                        "A calculated Historical Paper FAK fee has an unknown calculation source.",
                        nameof(updates));
                }
            }

            if (desiredStatus == FeeAccountingStatus.CalculationUnavailable &&
                (evaluated.FeeUsd != 0m ||
                 !string.Equals(
                     underlyingCalculationSource,
                     PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
                     StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "An unavailable Historical Paper FAK fee must be a zero-fee calculator result with historical provenance.",
                    nameof(updates));
            }
        }
    }

    private static object CreateHistoricalPaperFakFeeBackfillDatabaseUpdate(
        HistoricalPaperFakFeeBackfillUpdate update)
    {
        var order = update.Expected.Order;
        var expected = update.Expected.Fill;
        var desired = update.EvaluatedFill;
        return new
        {
            paper_order_id = order.Id,
            expected_strategy_id = order.StrategyId,
            expected_wallet = order.CopiedTraderWallet,
            expected_order_status = order.Status.ToString(),
            expected_asset_id = order.AssetId,
            expected_condition_id = order.ConditionId,
            expected_outcome = order.Outcome,
            expected_order_price = order.Price,
            expected_order_size = order.SizeShares,
            expected_order_notional = order.NotionalUsd,
            expected_order_filled_at = order.FilledAtUtc,
            expected_order_cancelled_at = order.CancelledAtUtc,
            expected_execution_source = order.ExecutionSource,
            fill_id = expected.Id,
            expected_fill_price = expected.Price,
            expected_fill_size = expected.SizeShares,
            expected_filled_at = expected.FilledAtUtc,
            expected_evidence = expected.Evidence,
            expected_realized = expected.RealizedPnlUsd,
            expected_fee = expected.FeeUsd,
            expected_fee_status = expected.FeeAccountingStatus,
            expected_fee_role = expected.FeeLiquidityRole,
            expected_fee_source = expected.FeeCalculationSource,
            expected_fee_rate = expected.FeeRate,
            expected_fee_exponent = expected.FeeExponent,
            expected_fee_taker_only = expected.FeeTakerOnly,
            expected_fee_calculated_at = expected.FeeCalculatedAtUtc,
            expected_net = expected.NetRealizedPnlUsd,
            desired_fee = desired.FeeUsd,
            desired_fee_status = desired.FeeAccountingStatus,
            desired_fee_role = desired.FeeLiquidityRole,
            desired_fee_source = desired.FeeCalculationSource,
            desired_fee_rate = desired.FeeRate,
            desired_fee_exponent = desired.FeeExponent,
            desired_fee_taker_only = desired.FeeTakerOnly,
            desired_fee_calculated_at = desired.FeeCalculatedAtUtc,
            desired_net = desired.NetRealizedPnlUsd
        };
    }

    private static PaperOrder ReadHistoricalPaperFakOrder(NpgsqlDataReader reader)
    {
        return new PaperOrder(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(3),
            Enum.Parse<PaperOrderStatus>(reader.GetString(4)),
            Enum.Parse<TradeSide>(reader.GetString(5)),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            reader.GetDecimal(11),
            DateTimeOffsetFromUtc(reader.GetDateTime(12)),
            DateTimeOffsetFromUtc(reader.GetDateTime(13)),
            reader.IsDBNull(14) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(14)),
            reader.IsDBNull(15) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(15)),
            reader.GetGuid(2),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetGuid(17),
            reader.GetString(18));
    }

    private static PaperFill ReadHistoricalPaperFakFill(NpgsqlDataReader reader)
    {
        const int offset = 19;
        return new PaperFill(
            reader.GetGuid(offset),
            reader.GetGuid(offset + 1),
            reader.GetDecimal(offset + 2),
            reader.GetDecimal(offset + 3),
            DateTimeOffsetFromUtc(reader.GetDateTime(offset + 4)),
            reader.GetString(offset + 5),
            reader.GetDecimal(offset + 6),
            reader.GetDecimal(offset + 7),
            reader.GetString(offset + 8),
            reader.GetString(offset + 9),
            reader.GetString(offset + 10),
            reader.IsDBNull(offset + 11) ? null : reader.GetDecimal(offset + 11),
            reader.IsDBNull(offset + 12) ? null : reader.GetInt32(offset + 12),
            reader.IsDBNull(offset + 13) ? null : reader.GetBoolean(offset + 13),
            reader.IsDBNull(offset + 14) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(offset + 14)),
            reader.IsDBNull(offset + 15) ? null : reader.GetDecimal(offset + 15));
    }
}
