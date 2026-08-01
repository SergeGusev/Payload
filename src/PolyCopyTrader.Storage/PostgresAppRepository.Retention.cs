using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    private const int StrategyRunRetentionCommandTimeoutSeconds = 30;
    private const int StrategyRunRetentionCandidatePageSize = 500;

    private const string IntrinsicPaperOnlySkippedRunFilter = """
run.status = 'Skipped'
AND run.retention_scope = 'PaperOnly'
AND run.updated_at_utc < LEAST(@UpdatedBeforeUtc, clock_timestamp() - interval '48 hours')
AND run.market_end_utc IS NOT NULL
AND run.market_end_utc < LEAST(@UpdatedBeforeUtc, clock_timestamp() - interval '48 hours')
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
""";

    // Blockers are evaluated against an already materialized candidate relation.
    // This prevents an unbounded correlated dependency scan for every raw run and
    // keeps closed paper positions (size_shares = 0) in the durable-history guard.
    private const string StrategyRunRetentionBlockerCtes = """
candidate_strategy_keys AS MATERIALIZED (
    SELECT
        candidate.id,
        candidate.condition_id,
        lower('strategy:' || strategy.code) AS copied_trader_wallet_key
    FROM candidate_batch candidate
    INNER JOIN public.strategies strategy ON strategy.id = candidate.strategy_id
),
blocker_hits AS (
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
    INNER JOIN public.dashboard_projection_events dependency
        ON dependency.source_kind = 'StrategyRun'
       AND dependency.source_id = candidate.id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN LATERAL (
        SELECT 1
        FROM public.dashboard_strategy_recent_projection_facts dependency
        WHERE dependency.source_kind = 'StrategyRun'
          AND dependency.source_id = candidate.id
        LIMIT 1
    ) recent_fact_hit ON true

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.dashboard_projection_reconciliation_queue dependency
        ON dependency.strategy_id = candidate.strategy_id
),
blocked_candidate_ids AS MATERIALIZED (
    SELECT DISTINCT blocker_hit.id
    FROM blocker_hits blocker_hit
)
""";

    public async Task<StrategyRunRetentionPreview> PreviewPaperOnlySkippedRunRetentionAsync(
        DateTimeOffset updatedBeforeUtc,
        int limit,
        StrategyRunRetentionCursor? afterCursor = null,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new StrategyRunRetentionPreview([], 0, null, null);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(
            connection,
            $$"""
WITH intrinsic_scan AS MATERIALIZED (
    SELECT
        run.id,
        run.strategy_id,
        run.market_id,
        run.condition_id,
        run.updated_at_utc
    FROM public.strategy_market_paper_runs run
    WHERE {{IntrinsicPaperOnlySkippedRunFilter}}
      AND (
          NOT @HasCursor
          OR run.updated_at_utc > @AfterUpdatedAtUtc
          OR (run.updated_at_utc = @AfterUpdatedAtUtc AND run.id > @AfterRunId)
    )
    ORDER BY run.updated_at_utc, run.id
    LIMIT @CandidateScanSize
),
candidate_batch AS MATERIALIZED (
    SELECT scan.id, scan.strategy_id, scan.market_id, scan.condition_id, scan.updated_at_utc
    FROM intrinsic_scan scan
    ORDER BY scan.updated_at_utc, scan.id
    LIMIT @CandidatePageSize
),
{{StrategyRunRetentionBlockerCtes}}
SELECT
    candidate.id,
    candidate.strategy_id,
    candidate.updated_at_utc,
    blocked.id IS NULL AS is_eligible,
    (SELECT count(*) > @CandidatePageSize FROM intrinsic_scan) AS has_more
FROM candidate_batch candidate
LEFT JOIN blocked_candidate_ids blocked ON blocked.id = candidate.id
ORDER BY candidate.updated_at_utc, candidate.id;
""");
        command.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
        command.Parameters.AddWithValue("UpdatedBeforeUtc", updatedBeforeUtc.UtcDateTime);
        var candidatePageSize = Math.Min(limit, StrategyRunRetentionCandidatePageSize);
        command.Parameters.AddWithValue("CandidatePageSize", candidatePageSize);
        command.Parameters.AddWithValue("CandidateScanSize", candidatePageSize + 1);
        command.Parameters.AddWithValue("HasCursor", afterCursor is not null);
        command.Parameters.AddWithValue(
            "AfterUpdatedAtUtc",
            afterCursor?.UpdatedAtUtc.UtcDateTime ?? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
        command.Parameters.AddWithValue("AfterRunId", afterCursor?.RunId ?? Guid.Empty);

        var candidateIds = new List<Guid>();
        var strategyIds = new HashSet<Guid>();
        DateTimeOffset? oldestUpdatedAtUtc = null;
        DateTimeOffset? newestUpdatedAtUtc = null;
        StrategyRunRetentionCursor? continuationCursor = null;
        var intrinsicRowsScanned = 0;
        var hasMore = false;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var updatedAtUtc = DateTimeOffsetFromUtc(reader.GetDateTime(2));
            continuationCursor = new StrategyRunRetentionCursor(updatedAtUtc, reader.GetGuid(0));
            intrinsicRowsScanned++;
            hasMore = reader.GetBoolean(4);
            if (reader.GetBoolean(3))
            {
                candidateIds.Add(reader.GetGuid(0));
                strategyIds.Add(reader.GetGuid(1));
                oldestUpdatedAtUtc ??= updatedAtUtc;
                newestUpdatedAtUtc = updatedAtUtc;
            }
        }

        return new StrategyRunRetentionPreview(
            candidateIds,
            strategyIds.Count,
            oldestUpdatedAtUtc,
            newestUpdatedAtUtc,
            intrinsicRowsScanned,
            continuationCursor,
            !hasMore);
    }

    public async Task<StrategyRunRetentionSummary> GetPaperOnlySkippedRunRetentionSummaryAsync(
        DateTimeOffset updatedBeforeUtc,
        int sampleLimit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(
            connection,
            $$"""
WITH candidate_batch AS MATERIALIZED (
    SELECT
        run.id,
        run.strategy_id,
        run.market_id,
        run.condition_id,
        run.updated_at_utc
    FROM public.strategy_market_paper_runs run
    WHERE {{IntrinsicPaperOnlySkippedRunFilter}}
),
{{StrategyRunRetentionBlockerCtes}},
retained_candidates AS MATERIALIZED (
    SELECT candidate.id, candidate.strategy_id, candidate.updated_at_utc
    FROM candidate_batch candidate
    LEFT JOIN blocked_candidate_ids blocked ON blocked.id = candidate.id
    WHERE blocked.id IS NULL
),
summary AS (
    SELECT
        count(*)::bigint AS total_candidate_rows,
        count(DISTINCT strategy_id)::bigint AS distinct_strategies,
        min(updated_at_utc) AS oldest_updated_at_utc,
        max(updated_at_utc) AS newest_updated_at_utc
    FROM retained_candidates
),
sample AS (
    SELECT COALESCE(array_agg(sample_row.id ORDER BY sample_row.updated_at_utc, sample_row.id), ARRAY[]::uuid[]) AS run_ids
    FROM (
        SELECT candidate.id, candidate.updated_at_utc
        FROM retained_candidates candidate
        ORDER BY candidate.updated_at_utc, candidate.id
        LIMIT @SampleLimit
    ) sample_row
)
SELECT
    summary.total_candidate_rows,
    summary.distinct_strategies,
    summary.oldest_updated_at_utc,
    summary.newest_updated_at_utc,
    sample.run_ids
FROM summary
CROSS JOIN sample;
""");
        command.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
        command.Parameters.AddWithValue("UpdatedBeforeUtc", updatedBeforeUtc.UtcDateTime);
        command.Parameters.AddWithValue("SampleLimit", Math.Clamp(sampleLimit, 0, 25_000));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Paper-only skipped-run retention summary returned no result.");
        }

        return new StrategyRunRetentionSummary(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.IsDBNull(2) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(2)),
            reader.IsDBNull(3) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(3)),
            reader.GetFieldValue<Guid[]>(4));
    }

    public async Task<StrategyRunRetentionBatchResult> TransferPaperOnlySkippedRunsToRollupsAsync(
        IReadOnlyCollection<Guid> expectedRunIds,
        DateTimeOffset updatedBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedRunIds);

        var normalizedRunIds = expectedRunIds.Distinct().ToArray();
        if (normalizedRunIds.Length == 0)
        {
            return new StrategyRunRetentionBatchResult(0, 0, 0, 0, 0);
        }

        if (normalizedRunIds.Length > 25_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedRunIds),
                expectedRunIds.Count,
                "A retention transfer batch cannot exceed 25,000 run ids.");
        }

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

            // The session gate is acquired before the SERIALIZABLE transaction
            // takes a snapshot. Dependency writers that won the shared gate
            // first are therefore visible to the exact allowlist recheck.
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            await using (var settingsCommand = CreateCommand(
                connection,
                "SET LOCAL polycopytrader.skip_run_retention_transfer = 'on';"))
            {
                settingsCommand.Transaction = transaction;
                await settingsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

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
      AND {{IntrinsicPaperOnlySkippedRunFilter}}
    ORDER BY run.updated_at_utc, run.id
    FOR UPDATE OF run
),
{{StrategyRunRetentionBlockerCtes}},
candidates AS MATERIALIZED (
    SELECT candidate.*
    FROM candidate_batch candidate
    LEFT JOIN blocked_candidate_ids blocked ON blocked.id = candidate.id
    WHERE blocked.id IS NULL
    ORDER BY candidate.updated_at_utc, candidate.id
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
    FROM candidates candidate
    GROUP BY
        candidate.strategy_id,
        candidate.rollup_bucket_start_utc,
        candidate.skip_reason
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
    RETURNING 1
),
deleted AS (
    DELETE FROM public.strategy_market_paper_runs run
    USING candidates candidate
    WHERE run.id = candidate.id
    RETURNING run.id
),
queued AS (
    INSERT INTO public.dashboard_projection_reconciliation_queue AS existing_queue (
        strategy_id, priority, reason, requested_at_utc, attempt_count, next_attempt_at_utc, last_error)
    SELECT
        distinct_candidate.strategy_id,
        50,
        'paper_skip_retention_transfer',
        clock_timestamp(),
        0,
        clock_timestamp(),
        NULL
    FROM (
        SELECT DISTINCT candidate.strategy_id
        FROM candidates candidate
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
            command.Parameters.Add("RunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = normalizedRunIds;
            command.Parameters.AddWithValue("UpdatedBeforeUtc", updatedBeforeUtc.UtcDateTime);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Paper-only skipped-run retention returned no batch result.");
            }

            var result = new StrategyRunRetentionBatchResult(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4));
            await reader.DisposeAsync();

            if (result.SelectedRows != normalizedRunIds.Length ||
                result.SelectedRows != result.DeletedRows ||
                result.SelectedRows != result.TombstonesChanged)
            {
                throw new InvalidOperationException(
                    $"Paper-only skipped-run retention invariant failed: expected={normalizedRunIds.Length}, " +
                    $"selected={result.SelectedRows}, " +
                    $"deleted={result.DeletedRows}, tombstones={result.TombstonesChanged}.");
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            if (!gateHeld)
            {
                NpgsqlConnection.ClearPool(connection);
            }
            else
            {
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
                        throw new InvalidOperationException("Strategy-run retention session gate was not held during release.");
                    }
                }
                catch
                {
                    NpgsqlConnection.ClearPool(connection);
                    throw;
                }
            }
        }
    }
}
