using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    private const int StrategyRunRetentionCommandTimeoutSeconds = 30;

    private const string EligiblePaperOnlySkippedRunFilter = """
run.status = 'Skipped'
AND run.retention_scope = 'PaperOnly'
AND run.updated_at_utc < LEAST(@UpdatedBeforeUtc, clock_timestamp() - interval '48 hours')
AND run.market_end_utc IS NOT NULL
AND run.market_end_utc < LEAST(@UpdatedBeforeUtc, clock_timestamp() - interval '48 hours')
AND cardinality(public.strategy_market_paper_run_retention_blockers(run)) = 0
""";

    public async Task<StrategyRunRetentionPreview> PreviewPaperOnlySkippedRunRetentionAsync(
        DateTimeOffset updatedBeforeUtc,
        int limit,
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
SELECT run.id, run.strategy_id, run.updated_at_utc
FROM strategy_market_paper_runs run
WHERE {{EligiblePaperOnlySkippedRunFilter}}
ORDER BY run.updated_at_utc, run.id
LIMIT @Limit;
""");
        command.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
        command.Parameters.AddWithValue("UpdatedBeforeUtc", updatedBeforeUtc.UtcDateTime);
        command.Parameters.AddWithValue("Limit", Math.Min(limit, 100_000));

        var candidateIds = new List<Guid>();
        var strategyIds = new HashSet<Guid>();
        DateTimeOffset? oldestUpdatedAtUtc = null;
        DateTimeOffset? newestUpdatedAtUtc = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidateIds.Add(reader.GetGuid(0));
            strategyIds.Add(reader.GetGuid(1));
            var updatedAtUtc = DateTimeOffsetFromUtc(reader.GetDateTime(2));
            oldestUpdatedAtUtc ??= updatedAtUtc;
            newestUpdatedAtUtc = updatedAtUtc;
        }

        return new StrategyRunRetentionPreview(
            candidateIds,
            strategyIds.Count,
            oldestUpdatedAtUtc,
            newestUpdatedAtUtc);
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
WITH eligible AS MATERIALIZED (
    SELECT run.id, run.strategy_id, run.updated_at_utc
    FROM strategy_market_paper_runs run
    WHERE {{EligiblePaperOnlySkippedRunFilter}}
),
summary AS (
    SELECT
        count(*)::bigint AS total_candidate_rows,
        count(DISTINCT strategy_id)::bigint AS distinct_strategies,
        min(updated_at_utc) AS oldest_updated_at_utc,
        max(updated_at_utc) AS newest_updated_at_utc
    FROM eligible
),
sample AS (
    SELECT COALESCE(array_agg(sample_row.id ORDER BY sample_row.updated_at_utc, sample_row.id), ARRAY[]::uuid[]) AS run_ids
    FROM (
        SELECT eligible.id, eligible.updated_at_utc
        FROM eligible
        ORDER BY eligible.updated_at_utc, eligible.id
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
WITH candidates AS MATERIALIZED (
        SELECT
        run.id,
        run.strategy_id,
        run.market_id,
        run.skip_reason,
        run.updated_at_utc
    FROM strategy_market_paper_runs run
    WHERE run.id = ANY(@RunIds)
      AND {{EligiblePaperOnlySkippedRunFilter}}
    ORDER BY run.updated_at_utc, run.id
    FOR UPDATE OF run
),
rollups AS (
    INSERT INTO strategy_paper_skip_rollups (
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
        date_trunc('day', candidate.updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC',
        candidate.skip_reason,
        count(*)::integer,
        min(candidate.updated_at_utc),
        max(candidate.updated_at_utc),
        clock_timestamp(),
        clock_timestamp()
    FROM candidates candidate
    GROUP BY
        candidate.strategy_id,
        date_trunc('day', candidate.updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC',
        candidate.skip_reason
    ON CONFLICT (strategy_id, bucket_start_utc, skip_reason) DO UPDATE SET
        run_count = strategy_paper_skip_rollups.run_count + EXCLUDED.run_count,
        first_updated_at_utc = LEAST(
            strategy_paper_skip_rollups.first_updated_at_utc,
            EXCLUDED.first_updated_at_utc),
        last_updated_at_utc = GREATEST(
            strategy_paper_skip_rollups.last_updated_at_utc,
            EXCLUDED.last_updated_at_utc),
        updated_at_utc = clock_timestamp()
    RETURNING 1
),
tombstones AS (
    INSERT INTO strategy_market_paper_skip_tombstones (
        strategy_id, market_id, archived_run_id, archived_at_utc)
    SELECT
        candidate.strategy_id,
        candidate.market_id,
        candidate.id,
        clock_timestamp()
    FROM candidates candidate
    ON CONFLICT (strategy_id, market_id) DO NOTHING
    RETURNING 1
),
deleted AS (
    DELETE FROM strategy_market_paper_runs run
    USING candidates candidate
    WHERE run.id = candidate.id
    RETURNING run.id
),
queued AS (
    INSERT INTO dashboard_projection_reconciliation_queue (
        strategy_id, priority, reason, requested_at_utc, attempt_count, next_attempt_at_utc, last_error)
    SELECT DISTINCT
        candidate.strategy_id,
        50,
        'paper_skip_retention_transfer',
        clock_timestamp(),
        0,
        clock_timestamp(),
        NULL
    FROM candidates candidate
    ON CONFLICT (strategy_id) DO UPDATE SET
        priority = GREATEST(dashboard_projection_reconciliation_queue.priority, EXCLUDED.priority),
        reason = EXCLUDED.reason,
        requested_at_utc = LEAST(
            dashboard_projection_reconciliation_queue.requested_at_utc,
            EXCLUDED.requested_at_utc),
        next_attempt_at_utc = LEAST(
            dashboard_projection_reconciliation_queue.next_attempt_at_utc,
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
}
