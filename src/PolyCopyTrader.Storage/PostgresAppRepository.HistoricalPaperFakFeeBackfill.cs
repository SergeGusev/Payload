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
        // Rank from the materialized lifetime Gross shown by the Dashboard. Keep
        // source-based exact work and all-source unresolved Settled run work in one
        // frozen strategy order without joining the multi-million-row fill payload.
        // The raw accounting formula remains a fail-safe for a strategy whose
        // Dashboard snapshot has not been materialized yet.
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
WHERE EXISTS (
    SELECT 1
    FROM public.paper_orders source_order
    WHERE source_order.strategy_id = strategy.id
      AND source_order.side = '{{TradeSide.Buy}}'
      AND source_order.execution_source IN (
           '{{HistoricalPaperFakDirectSource}}',
           '{{HistoricalPaperFakChildSource}}')
)
OR EXISTS (
    SELECT 1
    FROM public.strategy_market_paper_runs unresolved_run
    WHERE unresolved_run.strategy_id = strategy.id
      AND unresolved_run.status = '{{StrategyMarketPaperRunStatuses.Settled}}'
      AND unresolved_run.retention_scope = '{{StrategyRunRetentionScopes.PaperOnly}}'
      AND unresolved_run.stake_usd > 0
      AND unresolved_run.realized_pnl_usd IS NOT NULL
      AND unresolved_run.fee_calculation_source IS DISTINCT FROM
          '{{HistoricalPaperNetFallbackConstants.CalculationSource}}'
      AND NOT (
          unresolved_run.fee_accounting_status IN ('Calculated', 'VenueReported')
          AND unresolved_run.fee_usd >= 0
          AND unresolved_run.net_realized_pnl_usd IS NOT NULL
          AND unresolved_run.net_realized_pnl_usd =
              unresolved_run.realized_pnl_usd - unresolved_run.fee_usd)
)
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
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await using (var configureCommand = CreateCommand(connection, "SET TRANSACTION READ ONLY;"))
        {
            configureCommand.Transaction = transaction;
            configureCommand.CommandTimeout = HistoricalPaperFakFeeBackfillCommandTimeoutSeconds;
            await configureCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        // Keep the chronological page strategy-local. The LATERAL boundary makes
        // PostgreSQL probe fills by paper_order_id for each materialized strategy
        // order instead of starting with the global LegacyUnknown fill timeline.
        // Run the key preflight, legacy JSON binding scan, and remaining indexed
        // candidate query as separately bounded commands on one frozen read-only
        // snapshot. Wide order/fill payloads are loaded only after the N+1
        // candidate keys are selected.
        await using var preflightCommand = CreateCommand(connection, $$"""
WITH strategy_orders AS MATERIALIZED (
    SELECT paper_order.id
    FROM public.paper_orders paper_order
    WHERE paper_order.strategy_id = @StrategyId
      AND paper_order.side = '{{TradeSide.Buy}}'
      AND paper_order.execution_source IN (
          '{{HistoricalPaperFakDirectSource}}',
          '{{HistoricalPaperFakChildSource}}')
),
strategy_fill_keys AS MATERIALIZED (
    SELECT
        fill.id AS fill_id,
        fill.paper_order_id,
        fill.filled_at_utc
    FROM strategy_orders strategy_order
    CROSS JOIN LATERAL (
        SELECT
            candidate_fill.id,
            candidate_fill.paper_order_id,
            candidate_fill.filled_at_utc
        FROM public.paper_fills candidate_fill
        WHERE candidate_fill.paper_order_id = strategy_order.id
          AND candidate_fill.fee_accounting_status = '{{FeeAccountingStatus.LegacyUnknown}}'
          AND candidate_fill.filled_at_utc < @FilledBeforeUtc
          AND (
              NOT @HasCursor
              OR candidate_fill.filled_at_utc > @AfterFilledAtUtc
              OR (
                  candidate_fill.filled_at_utc = @AfterFilledAtUtc
                  AND candidate_fill.paper_order_id > @AfterPaperOrderId)
              OR (
                  candidate_fill.filled_at_utc = @AfterFilledAtUtc
                  AND candidate_fill.paper_order_id = @AfterPaperOrderId
                  AND candidate_fill.id > @AfterFillId)
          )
        OFFSET 0
    ) fill
)
SELECT
    fill.fill_id,
    fill.paper_order_id
FROM strategy_fill_keys fill;
""");
        preflightCommand.Transaction = transaction;
        preflightCommand.CommandTimeout = HistoricalPaperFakFeeBackfillCommandTimeoutSeconds;
        preflightCommand.Parameters.Add("FilledBeforeUtc", NpgsqlDbType.TimestampTz).Value =
            UtcDateTime(filledBeforeUtc);
        preflightCommand.Parameters.AddWithValue("StrategyId", strategyId);
        preflightCommand.Parameters.AddWithValue("HasCursor", afterCursor is not null);
        preflightCommand.Parameters.Add("AfterFilledAtUtc", NpgsqlDbType.TimestampTz).Value =
            UtcDateTime(afterCursor?.FilledAtUtc ?? DateTimeOffset.MinValue);
        preflightCommand.Parameters.AddWithValue(
            "AfterPaperOrderId",
            afterCursor?.PaperOrderId ?? Guid.Empty);
        preflightCommand.Parameters.AddWithValue("AfterFillId", afterCursor?.FillId ?? Guid.Empty);

        var preflightKeys = new List<(Guid FillId, Guid PaperOrderId)>();
        await using (var preflightReader =
                     await preflightCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await preflightReader.ReadAsync(cancellationToken))
            {
                preflightKeys.Add((preflightReader.GetGuid(0), preflightReader.GetGuid(1)));
            }
        }

        if (preflightKeys.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new HistoricalPaperFakFeeBackfillPage([], null, true);
        }

        var candidatePaperOrderIds = preflightKeys
            .Select(key => key.PaperOrderId.ToString("D").ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await using var legacyParityCommand = CreateCommand(connection, $$"""
SELECT DISTINCT
    parity_audit.old_payload_json ->> 'paper_order_id' AS paper_order_id
FROM public.historical_gross_net_parity_audit parity_audit
WHERE parity_audit.source_kind = 'PaperRun'
  AND parity_audit.calculation_version =
        '{{HistoricalGrossNetParityConstants.CalculationVersion}}'
  AND parity_audit.operation_kind = 'AccountingDecision'
  AND parity_audit.old_payload_json ->> 'paper_order_id' =
        ANY(@CandidatePaperOrderIds);
""");
        legacyParityCommand.Transaction = transaction;
        legacyParityCommand.CommandTimeout = HistoricalPaperFakFeeBackfillCommandTimeoutSeconds;
        legacyParityCommand.Parameters.Add(
            "CandidatePaperOrderIds",
            NpgsqlDbType.Array | NpgsqlDbType.Text).Value = candidatePaperOrderIds;

        var legacyParityPaperOrderIds = new List<string>();
        await using (var legacyParityReader =
                     await legacyParityCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await legacyParityReader.ReadAsync(cancellationToken))
            {
                legacyParityPaperOrderIds.Add(legacyParityReader.GetString(0));
            }
        }

        await using var command = CreateCommand(connection, $$"""
WITH strategy_orders AS MATERIALIZED (
    SELECT paper_order.id
    FROM public.paper_orders paper_order
    WHERE paper_order.strategy_id = @StrategyId
      AND paper_order.side = '{{TradeSide.Buy}}'
      AND paper_order.execution_source IN (
          '{{HistoricalPaperFakDirectSource}}',
          '{{HistoricalPaperFakChildSource}}')
),
strategy_fill_keys AS MATERIALIZED (
    SELECT
        fill.id AS fill_id,
        fill.paper_order_id,
        fill.filled_at_utc
    FROM strategy_orders strategy_order
    CROSS JOIN LATERAL (
        SELECT
            candidate_fill.id,
            candidate_fill.paper_order_id,
            candidate_fill.filled_at_utc
        FROM public.paper_fills candidate_fill
        WHERE candidate_fill.paper_order_id = strategy_order.id
          AND candidate_fill.fee_accounting_status = '{{FeeAccountingStatus.LegacyUnknown}}'
          AND candidate_fill.filled_at_utc < @FilledBeforeUtc
          AND (
              NOT @HasCursor
              OR candidate_fill.filled_at_utc > @AfterFilledAtUtc
              OR (
                  candidate_fill.filled_at_utc = @AfterFilledAtUtc
                  AND candidate_fill.paper_order_id > @AfterPaperOrderId)
              OR (
                  candidate_fill.filled_at_utc = @AfterFilledAtUtc
                  AND candidate_fill.paper_order_id = @AfterPaperOrderId
                  AND candidate_fill.id > @AfterFillId)
          )
        OFFSET 0
    ) fill
),
candidate_scope AS MATERIALIZED (
    SELECT COALESCE(
        array_agg(DISTINCT lower(fill.fill_id::text)),
        ARRAY[]::text[]) AS fill_ids
    FROM strategy_fill_keys fill
),
parity_sell_fill_keys AS MATERIALIZED (
    SELECT fill.fill_id
    FROM strategy_fill_keys fill
    WHERE EXISTS (
        SELECT 1
        FROM public.historical_gross_net_parity_audit parity_audit
        WHERE parity_audit.source_kind = 'PaperSellFill'
          AND parity_audit.source_id = fill.fill_id
          AND parity_audit.calculation_version =
                '{{HistoricalGrossNetParityConstants.CalculationVersion}}'
          AND parity_audit.operation_kind = 'AccountingDecision')
),
parity_run_source_keys AS MATERIALIZED (
    SELECT fill.fill_id
    FROM strategy_fill_keys fill
    WHERE EXISTS (
        SELECT 1
        FROM public.strategy_market_paper_runs parity_run
        INNER JOIN public.historical_gross_net_parity_audit parity_audit
            ON parity_audit.source_kind = 'PaperRun'
           AND parity_audit.source_id = parity_run.id
           AND parity_audit.calculation_version =
                '{{HistoricalGrossNetParityConstants.CalculationVersion}}'
           AND parity_audit.operation_kind = 'AccountingDecision'
        WHERE parity_run.paper_order_id = fill.paper_order_id)
),
parity_position_settlement_bindings AS MATERIALIZED (
    SELECT
        parity_audit.evidence_payload_json
            -> 'historicalGrossNetParityBindingV1'
            -> 'paperFillIds' AS paper_fill_ids
    FROM public.historical_gross_net_parity_audit parity_audit
    CROSS JOIN candidate_scope scope
    WHERE parity_audit.source_kind IN ('PaperPosition', 'PaperSettlement')
      AND parity_audit.calculation_version =
            '{{HistoricalGrossNetParityConstants.CalculationVersion}}'
      AND parity_audit.operation_kind = 'AccountingDecision'
      AND parity_audit.evidence_payload_json
              -> 'historicalGrossNetParityBindingV1'
              -> 'paperFillIds'
            ?| scope.fill_ids
),
parity_excluded_fill_keys AS MATERIALIZED (
    SELECT fill.fill_id
    FROM parity_sell_fill_keys fill
    UNION
    SELECT fill.fill_id
    FROM parity_run_source_keys fill
    UNION
    SELECT fill.fill_id
    FROM strategy_fill_keys fill
    INNER JOIN parity_position_settlement_bindings parity_binding
        ON parity_binding.paper_fill_ids ? lower(fill.fill_id::text)
),
candidate_keys AS MATERIALIZED (
    SELECT
        fill.fill_id,
        fill.paper_order_id,
        fill.filled_at_utc
    FROM strategy_fill_keys fill
    WHERE NOT EXISTS (
          SELECT 1
          FROM public.strategy_market_paper_runs fallback_run
          WHERE fallback_run.paper_order_id = fill.paper_order_id
            AND fallback_run.strategy_id = @StrategyId
            AND fallback_run.status = '{{StrategyMarketPaperRunStatuses.Settled}}'
            AND fallback_run.retention_scope = '{{StrategyRunRetentionScopes.PaperOnly}}'
            AND fallback_run.stake_usd > 0
            AND fallback_run.realized_pnl_usd IS NOT NULL
            AND fallback_run.fee_usd >= 0
            AND fallback_run.fee_accounting_status = '{{FeeAccountingStatus.Calculated}}'
            AND fallback_run.fee_calculation_source =
                '{{HistoricalPaperNetFallbackConstants.CalculationSource}}'
            AND fallback_run.net_realized_pnl_usd IS NOT NULL
            AND fallback_run.net_realized_pnl_usd =
                fallback_run.realized_pnl_usd - fallback_run.fee_usd)
      AND NOT (
          lower(fill.paper_order_id::text) = ANY(@LegacyParityPaperOrderIds))
      AND NOT EXISTS (
          SELECT 1
          FROM parity_excluded_fill_keys parity_excluded
          WHERE parity_excluded.fill_id = fill.fill_id)
      AND NOT EXISTS (
          SELECT 1
          FROM public.strategy_market_paper_runs parity_terminal_run
          WHERE parity_terminal_run.paper_order_id = fill.paper_order_id
            AND parity_terminal_run.fee_calculation_source IN (
                'historical-gross-net-parity-donor-v1',
                'historical-gross-net-parity-fixed-0p0333-v1',
                'historical-gross-net-parity-nonpositive-basis-v1'))
    ORDER BY fill.filled_at_utc, fill.paper_order_id, fill.fill_id
    LIMIT @FetchLimit
)
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
FROM candidate_keys candidate
INNER JOIN public.paper_fills fill ON fill.id = candidate.fill_id
INNER JOIN public.paper_orders paper_order ON paper_order.id = candidate.paper_order_id
ORDER BY candidate.filled_at_utc, candidate.paper_order_id, candidate.fill_id;
""");
        command.Transaction = transaction;
        command.CommandTimeout = HistoricalPaperFakFeeBackfillCommandTimeoutSeconds;
        command.Parameters.Add("FilledBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(filledBeforeUtc);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("HasCursor", afterCursor is not null);
        command.Parameters.Add("AfterFilledAtUtc", NpgsqlDbType.TimestampTz).Value =
            UtcDateTime(afterCursor?.FilledAtUtc ?? DateTimeOffset.MinValue);
        command.Parameters.AddWithValue("AfterPaperOrderId", afterCursor?.PaperOrderId ?? Guid.Empty);
        command.Parameters.AddWithValue("AfterFillId", afterCursor?.FillId ?? Guid.Empty);
        command.Parameters.AddWithValue("FetchLimit", pageSize + 1);
        command.Parameters.Add(
            "LegacyParityPaperOrderIds",
            NpgsqlDbType.Array | NpgsqlDbType.Text).Value = legacyParityPaperOrderIds.ToArray();

        var scanned = new List<HistoricalPaperFakFeeBackfillCandidate>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                scanned.Add(new HistoricalPaperFakFeeBackfillCandidate(
                    ReadHistoricalPaperFakOrder(reader),
                    ReadHistoricalPaperFakFill(reader)));
            }
        }

        await transaction.CommitAsync(cancellationToken);

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
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetInt32(12));
            await reader.DisposeAsync();
            var fullChainsRequiringUpdate =
                result.FullChainEligible - result.FullChainAlreadyApplied;
            var runOnlyChainsRequiringUpdate =
                result.RunOnlyLegacyEligible - result.RunOnlyLegacyAlreadyApplied;
            var allChainsRequiringUpdate =
                fullChainsRequiringUpdate + runOnlyChainsRequiringUpdate;
            if (result.Deferred != 0 ||
                result.Requested != updates.Count ||
                result.Requested !=
                    result.StructuralConflicts +
                    result.AccountingConflicts +
                    result.FullChainEligible +
                    result.RunOnlyLegacyEligible ||
                fullChainsRequiringUpdate < 0 ||
                runOnlyChainsRequiringUpdate < 0 ||
                result.FillsUpdated != allChainsRequiringUpdate ||
                result.RunsUpdated != allChainsRequiringUpdate ||
                result.PositionsUpdated != fullChainsRequiringUpdate ||
                result.SettlementsUpdated != fullChainsRequiringUpdate)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new InvalidOperationException(
                    "Historical Paper FAK fee backfill did not preserve its shape-specific atomic update invariants.");
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new HistoricalPaperFakFeeBackfillBatchResult(
                Requested: updates.Count,
                DeferredByLockTimeout: updates.Count);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.QueryCanceled &&
            !cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new HistoricalPaperFakFeeBackfillBatchResult(
                Requested: updates.Count,
                DeferredByQueryCancel: updates.Count);
        }
    }

    private static readonly HistoricalPaperFakFeeBackfillBatchResult EmptyHistoricalPaperFakFeeBackfillResult =
        new();

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
run_structural_chain AS MATERIALIZED (
    SELECT
        requested.*,
        run.id AS run_id,
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
        run.settlement_price AS run_settlement_price,
        run.settlement_value_usd AS run_settlement_value,
        run.settled_at_utc AS run_settled_at,
        run.fee_usd AS actual_run_fee,
        run.fee_accounting_status AS actual_run_status,
        run.fee_liquidity_role AS actual_run_role,
        run.fee_calculation_source AS actual_run_source,
        run.fee_rate AS actual_run_rate,
        run.fee_exponent AS actual_run_exponent,
        run.fee_taker_only AS actual_run_taker_only,
        run.fee_calculated_at_utc AS actual_run_calculated_at,
        run.net_realized_pnl_usd AS actual_run_net
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
       AND run.settlement_price IS NOT NULL
       AND run.settlement_value_usd IS NOT NULL
       AND run.settled_at_utc IS NOT NULL
    WHERE paper_order.price = fill.price
      AND paper_order.size_shares = fill.size_shares
      AND NOT EXISTS (
          SELECT 1
          FROM public.historical_gross_net_parity_audit parity_audit
          WHERE parity_audit.source_kind = 'PaperSellFill'
            AND parity_audit.source_id = fill.id
            AND parity_audit.calculation_version =
                    'historical-gross-net-parity-v1'
            AND parity_audit.operation_kind = 'AccountingDecision')
      AND NOT EXISTS (
          SELECT 1
          FROM public.strategy_market_paper_runs parity_run
          WHERE parity_run.paper_order_id = fill.paper_order_id
            AND EXISTS (
                SELECT 1
                FROM public.historical_gross_net_parity_audit parity_audit
                WHERE parity_audit.source_kind = 'PaperRun'
                  AND parity_audit.source_id = parity_run.id
                  AND parity_audit.calculation_version =
                        'historical-gross-net-parity-v1'
                  AND parity_audit.operation_kind = 'AccountingDecision'))
      AND NOT EXISTS (
          SELECT 1
          FROM public.historical_gross_net_parity_audit parity_audit
          WHERE parity_audit.source_kind = 'PaperRun'
            AND parity_audit.operation_kind = 'AccountingDecision'
            AND parity_audit.calculation_version =
                    'historical-gross-net-parity-v1'
            AND parity_audit.old_payload_json ->> 'paper_order_id' =
                    lower(fill.paper_order_id::text))
      AND NOT EXISTS (
          SELECT 1
          FROM public.historical_gross_net_parity_audit parity_audit
          WHERE parity_audit.source_kind IN ('PaperPosition', 'PaperSettlement')
            AND parity_audit.calculation_version =
                    'historical-gross-net-parity-v1'
            AND parity_audit.operation_kind = 'AccountingDecision'
            AND parity_audit.evidence_payload_json
                    -> 'historicalGrossNetParityBindingV1'
                    -> 'paperFillIds'
                ? lower(fill.id::text))
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
    ORDER BY
        paper_order.copied_trader_wallet COLLATE "C",
        paper_order.asset_id COLLATE "C",
        paper_order.id
    FOR UPDATE OF paper_order, fill, run
),
full_chain_structural AS MATERIALIZED (
    SELECT
        run_chain.*,
        'FullChain'::text AS chain_shape,
        position.id AS position_id,
        settlement.id AS settlement_id,
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
    FROM run_structural_chain run_chain
    INNER JOIN public.paper_positions position
        ON position.copied_trader_wallet = run_chain.expected_wallet
       AND position.asset_id = run_chain.expected_asset_id
       AND position.condition_id = run_chain.expected_condition_id
       AND position.outcome = run_chain.expected_outcome
       AND position.size_shares = 0
       AND position.average_price = 0
       AND position.estimated_value_usd = 0
       AND position.unrealized_pnl_usd = 0
    INNER JOIN public.paper_position_settlements settlement
        ON settlement.copied_trader_wallet = run_chain.expected_wallet
       AND settlement.asset_id = run_chain.expected_asset_id
       AND settlement.condition_id = run_chain.expected_condition_id
       AND settlement.outcome = run_chain.expected_outcome
       AND settlement.settled_size_shares = run_chain.expected_fill_size
       AND settlement.average_price = run_chain.expected_fill_price
       AND settlement.cost_basis_usd = round(
           run_chain.expected_fill_price * run_chain.expected_fill_size,
           8)
       AND settlement.settlement_value_usd = run_chain.run_settlement_value
       AND settlement.realized_pnl_usd = run_chain.run_realized
       AND run_chain.run_settlement_price = CASE WHEN settlement.won THEN 1 ELSE 0 END
       AND (
           (
               settlement.settlement_source = 'BtcUpDown5mGammaClosedMarket'
               AND settlement.settled_at_utc = run_chain.run_settled_at)
           OR (
               settlement.settlement_source = 'MarketWebSocket'
               AND settlement.settled_at_utc <= run_chain.run_settled_at)
       )
    WHERE (
          SELECT count(*)
          FROM public.paper_positions sibling_position
          WHERE sibling_position.copied_trader_wallet = run_chain.expected_wallet
            AND sibling_position.asset_id = run_chain.expected_asset_id) = 1
      AND (
          SELECT count(*)
          FROM public.paper_position_settlements sibling_settlement
          WHERE sibling_settlement.copied_trader_wallet = run_chain.expected_wallet
            AND sibling_settlement.asset_id = run_chain.expected_asset_id) = 1
    ORDER BY
        run_chain.expected_wallet COLLATE "C",
        run_chain.expected_asset_id COLLATE "C",
        run_chain.paper_order_id
    FOR UPDATE OF position, settlement
),
run_only_legacy_structural AS MATERIALIZED (
    SELECT
        run_chain.*,
        'RunOnlyLegacy'::text AS chain_shape,
        NULL::uuid AS position_id,
        NULL::uuid AS settlement_id,
        NULL::numeric AS actual_position_fee,
        NULL::text AS actual_position_status,
        NULL::text AS actual_position_role,
        NULL::text AS actual_position_source,
        NULL::numeric AS actual_position_rate,
        NULL::integer AS actual_position_exponent,
        NULL::boolean AS actual_position_taker_only,
        NULL::timestamptz AS actual_position_calculated_at,
        NULL::numeric AS actual_position_net,
        NULL::numeric AS settlement_realized,
        NULL::numeric AS actual_settlement_fee,
        NULL::text AS actual_settlement_status,
        NULL::text AS actual_settlement_role,
        NULL::text AS actual_settlement_source,
        NULL::numeric AS actual_settlement_rate,
        NULL::integer AS actual_settlement_exponent,
        NULL::boolean AS actual_settlement_taker_only,
        NULL::timestamptz AS actual_settlement_calculated_at,
        NULL::numeric AS actual_settlement_net
    FROM run_structural_chain run_chain
    WHERE run_chain.run_settlement_price IN (0, 1)
      AND run_chain.run_settlement_value =
          run_chain.expected_fill_size * run_chain.run_settlement_price
      AND run_chain.run_realized =
          run_chain.run_settlement_value - round(
              run_chain.expected_fill_price * run_chain.expected_fill_size,
              8)
      AND NOT EXISTS (
          SELECT 1
          FROM public.paper_positions position
          WHERE position.copied_trader_wallet = run_chain.expected_wallet
            AND position.asset_id = run_chain.expected_asset_id)
      AND NOT EXISTS (
          SELECT 1
          FROM public.paper_position_settlements settlement
          WHERE settlement.copied_trader_wallet = run_chain.expected_wallet
            AND settlement.asset_id = run_chain.expected_asset_id)
),
structural_chain AS MATERIALIZED (
    SELECT * FROM full_chain_structural
    UNION ALL
    SELECT * FROM run_only_legacy_structural
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
            AND (
                chain_shape = 'RunOnlyLegacy'
                OR (
                    chain_shape = 'FullChain'
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
                )
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
            AND (
                chain_shape = 'RunOnlyLegacy'
                OR (
                    chain_shape = 'FullChain'
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
                )
            )
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
    WHERE eligible.chain_shape = 'FullChain'
      AND target.id = eligible.position_id
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
    WHERE eligible.chain_shape = 'FullChain'
      AND target.id = eligible.settlement_id
    RETURNING target.id
)
SELECT
    (SELECT count(*)::integer FROM requested) AS requested,
    ((SELECT count(*) FROM requested) -
        (SELECT count(*) FROM structural_chain))::integer AS structural_conflicts,
    ((SELECT count(*) FROM structural_chain) -
        (SELECT count(*) FROM eligible))::integer AS accounting_conflicts,
    (SELECT count(*)::integer
        FROM eligible
        WHERE chain_shape = 'FullChain') AS full_chain_eligible,
    (SELECT count(*)::integer
        FROM eligible
        WHERE chain_shape = 'RunOnlyLegacy') AS run_only_legacy_eligible,
    (SELECT count(*)::integer FROM fill_updates) AS fills_updated,
    (SELECT count(*)::integer FROM run_updates) AS runs_updated,
    (SELECT count(*)::integer FROM position_updates) AS positions_updated,
    (SELECT count(*)::integer FROM settlement_updates) AS settlements_updated,
    (SELECT count(*)::integer
        FROM eligible
        WHERE chain_shape = 'FullChain'
          AND already_applied) AS full_chain_already_applied,
    (SELECT count(*)::integer
        FROM eligible
        WHERE chain_shape = 'RunOnlyLegacy'
          AND already_applied) AS run_only_legacy_already_applied,
    0::integer AS deferred_by_lock_timeout,
    0::integer AS deferred_by_query_cancel;
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
