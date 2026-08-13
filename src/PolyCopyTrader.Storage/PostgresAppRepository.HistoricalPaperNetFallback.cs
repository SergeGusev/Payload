using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    internal const string HistoricalPaperAuthoritativeNetRepairSql = """
WITH candidate_page AS MATERIALIZED (
    SELECT
        run.id,
        run.strategy_id,
        run.market_id,
        run.paper_order_id,
        run.status,
        run.retention_scope,
        run.stake_usd,
        run.settlement_price,
        run.settlement_value_usd,
        run.realized_pnl_usd,
        run.settled_at_utc,
        run.fee_usd,
        run.fee_accounting_status,
        run.fee_liquidity_role,
        run.fee_calculation_source,
        run.fee_rate,
        run.fee_exponent,
        run.fee_taker_only,
        run.fee_calculated_at_utc,
        run.net_realized_pnl_usd,
        run.xmin AS row_version
    FROM public.strategy_market_paper_runs run
    WHERE run.strategy_id = @StrategyId
      AND run.status = 'Settled'
      AND run.retention_scope = 'PaperOnly'
      AND run.stake_usd > 0
      AND run.realized_pnl_usd IS NOT NULL
      AND run.fee_accounting_status IN ('Calculated', 'VenueReported')
      AND run.fee_usd >= 0
      AND run.fee_calculation_source IS DISTINCT FROM
          'strategy-settled-fee-stake-ratio-v1'
      AND (
          run.net_realized_pnl_usd IS NULL
          OR run.net_realized_pnl_usd <> run.realized_pnl_usd - run.fee_usd)
      AND (NOT @HasCursor OR run.id > @AfterRunId)
    ORDER BY run.id
    LIMIT @FetchLimit
),
candidates AS MATERIALIZED (
    SELECT *
    FROM candidate_page
    ORDER BY id
    LIMIT @Limit
),
run_updates AS (
    UPDATE public.strategy_market_paper_runs target
    SET net_realized_pnl_usd = target.realized_pnl_usd - target.fee_usd
    FROM candidates candidate
    WHERE @ApplyEnabled
      AND target.id = candidate.id
      AND target.xmin = candidate.row_version
      AND target.strategy_id = candidate.strategy_id
      AND target.market_id = candidate.market_id
      AND target.paper_order_id IS NOT DISTINCT FROM candidate.paper_order_id
      AND target.status = candidate.status
      AND target.retention_scope = candidate.retention_scope
      AND target.stake_usd = candidate.stake_usd
      AND target.settlement_price IS NOT DISTINCT FROM candidate.settlement_price
      AND target.settlement_value_usd IS NOT DISTINCT FROM candidate.settlement_value_usd
      AND target.realized_pnl_usd IS NOT DISTINCT FROM candidate.realized_pnl_usd
      AND target.settled_at_utc IS NOT DISTINCT FROM candidate.settled_at_utc
      AND target.fee_usd = candidate.fee_usd
      AND target.fee_accounting_status = candidate.fee_accounting_status
      AND target.fee_liquidity_role = candidate.fee_liquidity_role
      AND target.fee_calculation_source = candidate.fee_calculation_source
      AND target.fee_rate IS NOT DISTINCT FROM candidate.fee_rate
      AND target.fee_exponent IS NOT DISTINCT FROM candidate.fee_exponent
      AND target.fee_taker_only IS NOT DISTINCT FROM candidate.fee_taker_only
      AND target.fee_calculated_at_utc IS NOT DISTINCT FROM candidate.fee_calculated_at_utc
      AND target.net_realized_pnl_usd IS NOT DISTINCT FROM candidate.net_realized_pnl_usd
      AND target.status = 'Settled'
      AND target.retention_scope = 'PaperOnly'
      AND target.stake_usd > 0
      AND target.realized_pnl_usd IS NOT NULL
      AND target.fee_accounting_status IN ('Calculated', 'VenueReported')
      AND target.fee_usd >= 0
      AND target.fee_calculation_source IS DISTINCT FROM
          'strategy-settled-fee-stake-ratio-v1'
      AND (
          target.net_realized_pnl_usd IS NULL
          OR target.net_realized_pnl_usd <> target.realized_pnl_usd - target.fee_usd)
    RETURNING target.id
)
SELECT
    (SELECT count(*)::integer FROM candidates) AS candidates,
    (SELECT count(*)::integer FROM run_updates) AS runs_updated,
    CASE WHEN @ApplyEnabled THEN
        (SELECT count(*)::integer FROM candidates) -
        (SELECT count(*)::integer FROM run_updates)
    ELSE 0 END AS compare_and_set_conflicts,
    (SELECT count(*) FROM candidate_page) <= @Limit AS reached_end,
    (SELECT candidate.id FROM candidates candidate ORDER BY candidate.id DESC LIMIT 1)
        AS continuation_run_id;
""";

    internal const string HistoricalPaperNetFallbackSql = """
WITH exact_donors AS MATERIALIZED (
    SELECT
        count(*)::integer AS donor_count,
        COALESCE(sum(run.fee_usd), 0)::numeric AS donor_fee_usd,
        COALESCE(sum(run.stake_usd), 0)::numeric AS donor_stake_usd
    FROM public.strategy_market_paper_runs run
    WHERE run.strategy_id = @StrategyId
      AND run.status = 'Settled'
      AND run.retention_scope = 'PaperOnly'
      AND run.stake_usd > 0
      AND run.realized_pnl_usd IS NOT NULL
      AND run.fee_usd >= 0
      AND run.net_realized_pnl_usd IS NOT NULL
      AND run.fee_accounting_status IN ('Calculated', 'VenueReported')
      AND run.net_realized_pnl_usd = run.realized_pnl_usd - run.fee_usd
      AND run.fee_calculation_source IS DISTINCT FROM
          'strategy-settled-fee-stake-ratio-v1'
),
donor_ratio AS MATERIALIZED (
    SELECT
        donor_count,
        donor_fee_usd,
        donor_stake_usd,
        donor_count > 0 AND donor_stake_usd > 0 AS donor_available,
        CASE WHEN donor_count > 0 AND donor_stake_usd > 0
            THEN donor_fee_usd / donor_stake_usd
            ELSE NULL::numeric
        END AS fee_to_stake_ratio
    FROM exact_donors
),
candidate_page AS MATERIALIZED (
    SELECT
        run.id,
        run.strategy_id,
        run.market_id,
        run.paper_order_id,
        run.status,
        run.retention_scope,
        run.stake_usd,
        run.settlement_price,
        run.settlement_value_usd,
        run.realized_pnl_usd,
        run.settled_at_utc,
        run.fee_usd,
        run.fee_accounting_status,
        run.fee_liquidity_role,
        run.fee_calculation_source,
        run.fee_rate,
        run.fee_exponent,
        run.fee_taker_only,
        run.fee_calculated_at_utc,
        run.net_realized_pnl_usd,
        run.xmin AS row_version
    FROM public.strategy_market_paper_runs run
    WHERE run.strategy_id = @StrategyId
      AND run.status = 'Settled'
      AND run.retention_scope = 'PaperOnly'
      AND run.stake_usd > 0
      AND run.realized_pnl_usd IS NOT NULL
      AND run.fee_calculation_source IS DISTINCT FROM
          'strategy-settled-fee-stake-ratio-v1'
      AND (
          run.paper_order_id IS NULL
          OR run.paper_order_id <> ALL(@ExcludedPaperOrderIds))
      AND NOT (
          run.fee_accounting_status IN ('Calculated', 'VenueReported')
          AND run.fee_usd >= 0)
      AND (NOT @HasCursor OR run.id > @AfterRunId)
    ORDER BY run.id
    LIMIT @FetchLimit
),
candidates AS MATERIALIZED (
    SELECT *
    FROM candidate_page
    ORDER BY id
    LIMIT @Limit
),
estimated_candidates AS MATERIALIZED (
    SELECT
        candidate.*,
        round(candidate.stake_usd * donor.fee_to_stake_ratio, 8) AS estimated_fee_usd
    FROM candidates candidate
    CROSS JOIN donor_ratio donor
    WHERE donor.donor_available
),
run_updates AS (
    UPDATE public.strategy_market_paper_runs target
    SET
        fee_usd = estimated.estimated_fee_usd,
        fee_accounting_status = 'Calculated',
        fee_liquidity_role = 'Unknown',
        fee_calculation_source = 'strategy-settled-fee-stake-ratio-v1',
        fee_rate = NULL,
        fee_exponent = NULL,
        fee_taker_only = NULL,
        fee_calculated_at_utc = statement_timestamp(),
        net_realized_pnl_usd = target.realized_pnl_usd - estimated.estimated_fee_usd
    FROM estimated_candidates estimated
    WHERE @ApplyEnabled
      AND target.id = estimated.id
      AND target.xmin = estimated.row_version
      AND target.strategy_id = estimated.strategy_id
      AND target.market_id = estimated.market_id
      AND target.paper_order_id IS NOT DISTINCT FROM estimated.paper_order_id
      AND target.status = estimated.status
      AND target.retention_scope = estimated.retention_scope
      AND target.stake_usd = estimated.stake_usd
      AND target.settlement_price IS NOT DISTINCT FROM estimated.settlement_price
      AND target.settlement_value_usd IS NOT DISTINCT FROM estimated.settlement_value_usd
      AND target.realized_pnl_usd IS NOT DISTINCT FROM estimated.realized_pnl_usd
      AND target.settled_at_utc IS NOT DISTINCT FROM estimated.settled_at_utc
      AND target.fee_usd = estimated.fee_usd
      AND target.fee_accounting_status = estimated.fee_accounting_status
      AND target.fee_liquidity_role = estimated.fee_liquidity_role
      AND target.fee_calculation_source = estimated.fee_calculation_source
      AND target.fee_rate IS NOT DISTINCT FROM estimated.fee_rate
      AND target.fee_exponent IS NOT DISTINCT FROM estimated.fee_exponent
      AND target.fee_taker_only IS NOT DISTINCT FROM estimated.fee_taker_only
      AND target.fee_calculated_at_utc IS NOT DISTINCT FROM estimated.fee_calculated_at_utc
      AND target.net_realized_pnl_usd IS NOT DISTINCT FROM estimated.net_realized_pnl_usd
      AND target.status = 'Settled'
      AND target.retention_scope = 'PaperOnly'
      AND target.stake_usd > 0
      AND target.realized_pnl_usd IS NOT NULL
      AND target.fee_calculation_source IS DISTINCT FROM
          'strategy-settled-fee-stake-ratio-v1'
      AND (
          target.paper_order_id IS NULL
          OR target.paper_order_id <> ALL(@ExcludedPaperOrderIds))
      AND NOT (
          target.fee_accounting_status IN ('Calculated', 'VenueReported')
          AND target.fee_usd >= 0)
    RETURNING target.id
)
SELECT
    (SELECT count(*)::integer FROM candidates) AS candidates,
    donor.donor_count,
    donor.donor_fee_usd,
    donor.donor_stake_usd,
    donor.fee_to_stake_ratio,
    (SELECT count(*)::integer FROM run_updates) AS runs_updated,
    CASE WHEN @ApplyEnabled AND donor.donor_available THEN
        (SELECT count(*)::integer FROM candidates) -
        (SELECT count(*)::integer FROM run_updates)
    ELSE 0 END AS compare_and_set_conflicts,
    donor.donor_available,
    (SELECT count(*) FROM candidate_page) <= @Limit AS reached_end,
    (SELECT candidate.id FROM candidates candidate ORDER BY candidate.id DESC LIMIT 1)
        AS continuation_run_id
FROM donor_ratio donor;
""";

    public async Task<HistoricalPaperAuthoritativeNetRepairBatchResult>
        ApplyHistoricalPaperAuthoritativeNetRepairBatchAsync(
            Guid strategyId,
            int limit,
            bool applyEnabled,
            HistoricalPaperNetRunCursor? afterCursor = null,
            CancellationToken cancellationToken = default)
    {
        ValidateHistoricalPaperNetBatchArguments(strategyId, afterCursor);
        if (limit <= 0)
        {
            return new HistoricalPaperAuthoritativeNetRepairBatchResult();
        }

        var pageSize = Math.Min(limit, HistoricalPaperFakFeeBackfillMaxPageSize);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        try
        {
            await ConfigureHistoricalPaperFakFeeBackfillTransactionAsync(
                connection,
                transaction,
                cancellationToken);
            await using var command = CreateCommand(connection, HistoricalPaperAuthoritativeNetRepairSql);
            command.Transaction = transaction;
            command.CommandTimeout = HistoricalPaperFakFeeBackfillCommandTimeoutSeconds;
            AddHistoricalPaperNetBatchParameters(
                command,
                strategyId,
                pageSize,
                applyEnabled,
                afterCursor);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "Historical Paper authoritative-Net repair did not return a batch result.");
            }

            var result = new HistoricalPaperAuthoritativeNetRepairBatchResult(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetBoolean(3),
                reader.IsDBNull(4)
                    ? null
                    : new HistoricalPaperNetRunCursor(strategyId, reader.GetGuid(4)));
            await reader.DisposeAsync();
            if (!IsValidHistoricalPaperAuthoritativeNetRepairResult(
                    result,
                    pageSize,
                    applyEnabled))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new InvalidOperationException(
                    "Historical Paper authoritative-Net repair returned inconsistent batch totals.");
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new HistoricalPaperAuthoritativeNetRepairBatchResult(
                ReachedEnd: false,
                ContinuationCursor: afterCursor,
                DeferredByLockTimeout: 1);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.QueryCanceled &&
            !cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new HistoricalPaperAuthoritativeNetRepairBatchResult(
                ReachedEnd: false,
                ContinuationCursor: afterCursor,
                DeferredByQueryCancel: 1);
        }
    }

    public async Task<HistoricalPaperNetFallbackBatchResult> ApplyHistoricalPaperNetFallbackBatchAsync(
        Guid strategyId,
        int limit,
        bool applyEnabled,
        IReadOnlyCollection<Guid> excludedPaperOrderIds,
        HistoricalPaperNetRunCursor? afterCursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(excludedPaperOrderIds);
        var excludedPaperOrderIdSnapshot = excludedPaperOrderIds.Distinct().ToArray();
        if (excludedPaperOrderIdSnapshot.Any(static paperOrderId => paperOrderId == Guid.Empty))
        {
            throw new ArgumentException(
                "Historical Paper Net exclusions require exact non-empty Paper order IDs.",
                nameof(excludedPaperOrderIds));
        }

        ValidateHistoricalPaperNetBatchArguments(strategyId, afterCursor);
        if (limit <= 0)
        {
            return new HistoricalPaperNetFallbackBatchResult();
        }

        var pageSize = Math.Min(limit, HistoricalPaperFakFeeBackfillMaxPageSize);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        try
        {
            await ConfigureHistoricalPaperFakFeeBackfillTransactionAsync(
                connection,
                transaction,
                cancellationToken);
            await using var command = CreateCommand(connection, HistoricalPaperNetFallbackSql);
            command.Transaction = transaction;
            command.CommandTimeout = HistoricalPaperFakFeeBackfillCommandTimeoutSeconds;
            AddHistoricalPaperNetBatchParameters(
                command,
                strategyId,
                pageSize,
                applyEnabled,
                afterCursor,
                excludedPaperOrderIdSnapshot);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "Historical Paper Net fallback did not return a batch result.");
            }

            var result = new HistoricalPaperNetFallbackBatchResult(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.IsDBNull(9)
                    ? null
                    : new HistoricalPaperNetRunCursor(strategyId, reader.GetGuid(9)));
            await reader.DisposeAsync();
            if (!IsValidHistoricalPaperNetFallbackResult(
                    result,
                    pageSize,
                    applyEnabled))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new InvalidOperationException(
                    "Historical Paper Net fallback returned inconsistent batch totals or donor data.");
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new HistoricalPaperNetFallbackBatchResult(
                ReachedEnd: false,
                ContinuationCursor: afterCursor,
                DeferredByLockTimeout: 1);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.QueryCanceled &&
            !cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new HistoricalPaperNetFallbackBatchResult(
                ReachedEnd: false,
                ContinuationCursor: afterCursor,
                DeferredByQueryCancel: 1);
        }
    }

    private static void ValidateHistoricalPaperNetBatchArguments(
        Guid strategyId,
        HistoricalPaperNetRunCursor? afterCursor)
    {
        if (strategyId == Guid.Empty)
        {
            throw new ArgumentException(
                "Historical Paper Net processing requires an exact strategy ID.",
                nameof(strategyId));
        }

        if (afterCursor is not null && afterCursor.StrategyId != strategyId)
        {
            throw new ArgumentException(
                "Historical Paper Net cursor belongs to another strategy.",
                nameof(afterCursor));
        }
    }

    private static void AddHistoricalPaperNetBatchParameters(
        NpgsqlCommand command,
        Guid strategyId,
        int pageSize,
        bool applyEnabled,
        HistoricalPaperNetRunCursor? afterCursor,
        IReadOnlyCollection<Guid>? excludedPaperOrderIds = null)
    {
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("HasCursor", afterCursor is not null);
        command.Parameters.AddWithValue("AfterRunId", afterCursor?.RunId ?? Guid.Empty);
        command.Parameters.AddWithValue("Limit", pageSize);
        command.Parameters.AddWithValue("FetchLimit", pageSize + 1);
        command.Parameters.AddWithValue("ApplyEnabled", applyEnabled);
        if (excludedPaperOrderIds is not null)
        {
            command.Parameters.Add(
                "ExcludedPaperOrderIds",
                NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = excludedPaperOrderIds.ToArray();
        }
    }

    private static bool IsValidHistoricalPaperAuthoritativeNetRepairResult(
        HistoricalPaperAuthoritativeNetRepairBatchResult result,
        int pageSize,
        bool applyEnabled)
    {
        return HasValidHistoricalPaperNetPage(
                result.Candidates,
                result.ReachedEnd,
                result.ContinuationCursor,
                pageSize) &&
            result.RunsUpdated >= 0 &&
            result.CompareAndSetConflicts >= 0 &&
            result.Deferred == 0 &&
            (applyEnabled
                ? result.RunsUpdated + result.CompareAndSetConflicts == result.Candidates
                : result.RunsUpdated == 0 && result.CompareAndSetConflicts == 0);
    }

    private static bool IsValidHistoricalPaperNetFallbackResult(
        HistoricalPaperNetFallbackBatchResult result,
        int pageSize,
        bool applyEnabled)
    {
        var donorTotalsAreValid = result.ExactDonorCount == 0
            ? result.ExactDonorFeeUsd == 0 &&
              result.ExactDonorStakeUsd == 0 &&
              result.FeeToStakeRatio is null &&
              !result.DonorAvailable
            : result.ExactDonorCount > 0 &&
              result.ExactDonorFeeUsd >= 0 &&
              result.ExactDonorStakeUsd > 0 &&
              result.FeeToStakeRatio.HasValue &&
              result.FeeToStakeRatio.Value >= 0 &&
              result.DonorAvailable;
        var updateTotalsAreValid = !applyEnabled || !result.DonorAvailable
            ? result.RunsUpdated == 0 && result.CompareAndSetConflicts == 0
            : result.RunsUpdated + result.CompareAndSetConflicts == result.Candidates;

        return HasValidHistoricalPaperNetPage(
                result.Candidates,
                result.ReachedEnd,
                result.ContinuationCursor,
                pageSize) &&
            donorTotalsAreValid &&
            result.RunsUpdated >= 0 &&
            result.CompareAndSetConflicts >= 0 &&
            result.Deferred == 0 &&
            updateTotalsAreValid;
    }

    private static bool HasValidHistoricalPaperNetPage(
        int candidates,
        bool reachedEnd,
        HistoricalPaperNetRunCursor? continuationCursor,
        int pageSize)
    {
        return candidates >= 0 &&
            candidates <= pageSize &&
            (candidates == 0) == (continuationCursor is null) &&
            (reachedEnd || candidates == pageSize);
    }
}
