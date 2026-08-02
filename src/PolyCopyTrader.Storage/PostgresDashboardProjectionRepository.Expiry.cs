using Npgsql;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresDashboardProjectionRepository
{
    public async Task<DashboardProjectionExpiryResult> ExpireRecentFactsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await TryAcquireProjectionLockAsync(connection, transaction, cancellationToken) ||
            !await IsProjectionInitializedAsync(connection, transaction, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new DashboardProjectionExpiryResult(0, 0);
        }

        var nowUtc = await ReadDatabaseNowAsync(connection, transaction, cancellationToken);
        var facts = await ReadExpiringFactsAsync(
            connection,
            transaction,
            nowUtc,
            limit,
            cancellationToken);
        if (facts.Count == 0)
        {
            await UpdateExpiryControlAsync(connection, transaction, nowUtc, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new DashboardProjectionExpiryResult(0, 0);
        }

        var strategyIds = facts.Select(fact => fact.StrategyId).Distinct().ToArray();
        var descriptors = await ReadStrategyDescriptorBatchAsync(
            connection,
            transaction,
            strategyIds,
            cancellationToken);
        var recentStates = await ReadRecentStateBatchAsync(
            connection,
            transaction,
            strategyIds,
            cancellationToken);
        var rebuildKeys = new HashSet<(Guid StrategyId, int WindowHours)>();
        var updatedFacts = new List<DashboardRecentProjectionFact>();
        var deletedFactKeys = new List<DashboardRecentProjectionFactKey>();
        var touchedStrategies = new HashSet<Guid>();

        foreach (var fact in facts)
        {
            var applied1Hour = fact.Applied1Hour && fact.OccurredAtUtc >= nowUtc.AddHours(-1);
            var applied6Hours = fact.Applied6Hours && fact.OccurredAtUtc >= nowUtc.AddHours(-6);
            var applied24Hours = fact.Applied24Hours && fact.OccurredAtUtc >= nowUtc.AddHours(-24);
            ExpireWindow(1, fact.Applied1Hour, applied1Hour);
            ExpireWindow(6, fact.Applied6Hours, applied6Hours);
            ExpireWindow(24, fact.Applied24Hours, applied24Hours);

            var updated = fact with
            {
                Applied1Hour = applied1Hour,
                Applied6Hours = applied6Hours,
                Applied24Hours = applied24Hours
            };
            if (!applied1Hour && !applied6Hours && !applied24Hours &&
                fact.OccurredAtUtc < nowUtc.AddHours(-24))
            {
                deletedFactKeys.Add(new DashboardRecentProjectionFactKey(
                    fact.SourceKind,
                    fact.SourceId,
                    fact.FactKind));
            }
            else
            {
                updatedFacts.Add(updated);
            }

            touchedStrategies.Add(fact.StrategyId);

            void ExpireWindow(int windowHours, bool wasApplied, bool remainsApplied)
            {
                if (!wasApplied || remainsApplied ||
                    !recentStates.TryGetValue((fact.StrategyId, windowHours), out var state))
                {
                    return;
                }

                var oldWindowFact = fact with
                {
                    Applied1Hour = windowHours == 1,
                    Applied6Hours = windowHours == 6,
                    Applied24Hours = windowHours == 24
                };
                if (DashboardProjectionCalculator.RequiresRecentCandidateRebuild(
                    state,
                    [oldWindowFact],
                    []))
                {
                    rebuildKeys.Add((fact.StrategyId, windowHours));
                }

                DashboardProjectionCalculator.Apply(state, fact.Contribution, -1);
                state.ProjectionVersion++;
            }
        }

        await PersistExpiredFactsAsync(
            connection,
            transaction,
            updatedFacts,
            deletedFactKeys,
            nowUtc,
            cancellationToken);
        if (rebuildKeys.Count > 0)
        {
            await RebuildRecentCandidatesAsync(
                connection,
                transaction,
                recentStates,
                rebuildKeys,
                cancellationToken);
        }

        var touchedRecent = recentStates
            .Where(pair => touchedStrategies.Contains(pair.Key.StrategyId))
            .ToDictionary();
        var touchedDescriptors = descriptors
            .Where(pair => touchedStrategies.Contains(pair.Key))
            .ToDictionary();
        await WriteRecentProjectionAsync(
            connection,
            transaction,
            touchedDescriptors,
            touchedRecent,
            nowUtc,
            cancellationToken);
        await UpdateExpiryControlAsync(connection, transaction, nowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DashboardProjectionExpiryResult(facts.Count, touchedStrategies.Count);
    }

    private static async Task<List<DashboardRecentProjectionFact>> ReadExpiringFactsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
-- The cutoffs are nested, so the earliest still-applied window owns each due fact.
-- Locking inside each disjoint branch preserves SKIP LOCKED backfill while letting
-- every window use its partial expiry index.
WITH due_1h AS MATERIALIZED (
    SELECT source_kind, source_id, fact_kind, strategy_id, occurred_at_utc,
           contribution_json::text AS contribution_json,
           applied_1h, applied_6h, applied_24h
    FROM dashboard_strategy_recent_projection_facts
    WHERE applied_1h
      AND occurred_at_utc < @OneHourStartUtc
    ORDER BY occurred_at_utc, strategy_id, source_id
    LIMIT @Limit
    FOR UPDATE SKIP LOCKED
),
due_6h AS MATERIALIZED (
    SELECT source_kind, source_id, fact_kind, strategy_id, occurred_at_utc,
           contribution_json::text AS contribution_json,
           applied_1h, applied_6h, applied_24h
    FROM dashboard_strategy_recent_projection_facts
    WHERE applied_6h
      AND NOT applied_1h
      AND occurred_at_utc < @SixHourStartUtc
    ORDER BY occurred_at_utc, strategy_id, source_id
    LIMIT @Limit
    FOR UPDATE SKIP LOCKED
),
due_24h AS MATERIALIZED (
    SELECT source_kind, source_id, fact_kind, strategy_id, occurred_at_utc,
           contribution_json::text AS contribution_json,
           applied_1h, applied_6h, applied_24h
    FROM dashboard_strategy_recent_projection_facts
    WHERE applied_24h
      AND NOT applied_1h
      AND NOT applied_6h
      AND occurred_at_utc < @TwentyFourHourStartUtc
    ORDER BY occurred_at_utc, strategy_id, source_id
    LIMIT @Limit
    FOR UPDATE SKIP LOCKED
)
SELECT source_kind, source_id, fact_kind, strategy_id, occurred_at_utc,
       contribution_json, applied_1h, applied_6h, applied_24h
FROM (
    SELECT * FROM due_1h
    UNION ALL
    SELECT * FROM due_6h
    UNION ALL
    SELECT * FROM due_24h
) due
ORDER BY occurred_at_utc, strategy_id, source_id
LIMIT @Limit;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("OneHourStartUtc", UtcDateTime(nowUtc.AddHours(-1)));
        command.Parameters.AddWithValue("SixHourStartUtc", UtcDateTime(nowUtc.AddHours(-6)));
        command.Parameters.AddWithValue("TwentyFourHourStartUtc", UtcDateTime(nowUtc.AddHours(-24)));
        command.Parameters.AddWithValue("Limit", limit);
        var results = new List<DashboardRecentProjectionFact>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DashboardRecentProjectionFact(
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetGuid(3),
                UtcNow(reader.GetDateTime(4)),
                Deserialize<DashboardRecentContribution>(reader.GetString(5)),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8)));
        }

        return results;
    }

    private static async Task PersistExpiredFactsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyCollection<DashboardRecentProjectionFact> updatedFacts,
        IReadOnlyCollection<DashboardRecentProjectionFactKey> deletedFactKeys,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var batch = new NpgsqlBatch(connection) { Transaction = transaction };
        foreach (var fact in updatedFacts)
        {
            var update = new NpgsqlBatchCommand(
                """
UPDATE dashboard_strategy_recent_projection_facts
SET applied_1h = @Applied1h,
    applied_6h = @Applied6h,
    applied_24h = @Applied24h,
    updated_at_utc = @UpdatedAtUtc
WHERE source_kind = @SourceKind
  AND source_id = @SourceId
  AND fact_kind = @FactKind;
""");
            update.Parameters.AddWithValue("Applied1h", fact.Applied1Hour);
            update.Parameters.AddWithValue("Applied6h", fact.Applied6Hours);
            update.Parameters.AddWithValue("Applied24h", fact.Applied24Hours);
            update.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(nowUtc));
            update.Parameters.AddWithValue("SourceKind", fact.SourceKind);
            update.Parameters.AddWithValue("SourceId", fact.SourceId);
            update.Parameters.AddWithValue("FactKind", fact.FactKind);
            batch.BatchCommands.Add(update);
        }

        foreach (var fact in deletedFactKeys)
        {
            var delete = new NpgsqlBatchCommand(
                """
DELETE FROM dashboard_strategy_recent_projection_facts
WHERE source_kind = @SourceKind
  AND source_id = @SourceId
  AND fact_kind = @FactKind;
""");
            delete.Parameters.AddWithValue("SourceKind", fact.SourceKind);
            delete.Parameters.AddWithValue("SourceId", fact.SourceId);
            delete.Parameters.AddWithValue("FactKind", fact.FactKind);
            batch.BatchCommands.Add(delete);
        }

        if (batch.BatchCommands.Count > 0)
        {
            await batch.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task UpdateExpiryControlAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
UPDATE dashboard_projection_control
SET last_expiry_at_utc = @NowUtc,
    updated_at_utc = @NowUtc
WHERE singleton_id = 1;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("NowUtc", UtcDateTime(nowUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
