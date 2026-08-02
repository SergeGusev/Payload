using System.Data;
using System.Diagnostics;
using System.Reflection;
using Npgsql;
using NpgsqlTypes;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresDashboardProjectionRepository
{
    private static readonly PropertyInfo[] LifetimeValueProperties =
        typeof(DashboardLifetimeContribution).GetProperties(BindingFlags.Instance | BindingFlags.Public);
    private static readonly PropertyInfo[] RecentValueProperties =
        typeof(DashboardRecentContribution).GetProperties(BindingFlags.Instance | BindingFlags.Public);

    public async Task<DashboardProjectionReconciliationResult> ReconcileNextStrategyAsync(
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        Guid? selectedStrategyId = null;
        string? selectedStrategyCode = null;
        PostgresPaperPositionsScanStats? paperPositionsBuildScanDelta = null;
        try
        {
            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
            if (!await TryAcquireProjectionLockAsync(connection, transaction, cancellationToken) ||
                !await IsProjectionInitializedAsync(connection, transaction, cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return new DashboardProjectionReconciliationResult(
                    false, null, null, stopwatch.Elapsed, false, null);
            }

            await ConfigureReconciliationTransactionAsync(connection, transaction, cancellationToken);
            var candidate = await ReadNextReconciliationCandidateAsync(
                connection,
                transaction,
                cancellationToken);
            if (candidate is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new DashboardProjectionReconciliationResult(
                    false, null, null, stopwatch.Elapsed, false, null);
            }

            selectedStrategyId = candidate.Value.StrategyId;
            selectedStrategyCode = candidate.Value.StrategyCode;
            var nowUtc = await ReadDatabaseNowAsync(connection, transaction, cancellationToken);
            var existingState = await ReadLifetimeStateAsync(
                connection,
                transaction,
                selectedStrategyId.Value,
                cancellationToken);
            var existingRecentStates = await ReadRecentStatesAsync(
                connection,
                transaction,
                selectedStrategyId.Value,
                cancellationToken);
            var facts = new List<DashboardRecentProjectionFact>();
            var positionFacts = new List<PaperPositionProjectionPayload>();
            var paperPositionsStatsBeforeBuild = await PostgresPaperPositionsScanTelemetry.ReadAsync(
                connection,
                transaction,
                cancellationToken);
            var projection = await BuildProjectionAsync(
                connection,
                transaction,
                selectedStrategyId.Value,
                nowUtc,
                (fact, _) =>
                {
                    facts.Add(fact);
                    return ValueTask.CompletedTask;
                },
                (fact, _) =>
                {
                    positionFacts.Add(fact);
                    return ValueTask.CompletedTask;
                },
                includePaperPositions: true,
                cancellationToken);
            var paperPositionsStatsAfterBuild = await PostgresPaperPositionsScanTelemetry.ReadAsync(
                connection,
                transaction,
                cancellationToken);
            paperPositionsBuildScanDelta = PostgresPaperPositionsScanStats.Delta(
                paperPositionsStatsBeforeBuild,
                paperPositionsStatsAfterBuild);

            if (projection.Strategies.Count == 0)
            {
                await DeleteStrategyProjectionAsync(
                    connection,
                    transaction,
                    selectedStrategyId.Value,
                    cancellationToken);
                await DeleteVisibleStrategyEventsAsync(
                    connection,
                    transaction,
                    selectedStrategyId.Value,
                    cancellationToken);
                await UpdateReconciliationControlAsync(
                    connection,
                    transaction,
                    selectedStrategyId.Value,
                    nowUtc,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new DashboardProjectionReconciliationResult(
                    true,
                    selectedStrategyId,
                    selectedStrategyCode,
                    stopwatch.Elapsed,
                    existingState is not null,
                    null,
                    paperPositionsBuildScanDelta?.SequentialScans,
                    paperPositionsBuildScanDelta?.SequentialTuplesRead);
            }

            var lastVisibleEventId = await ReadLastVisibleStrategyEventIdAsync(
                connection,
                transaction,
                selectedStrategyId.Value,
                cancellationToken);
            var lifetimeState = projection.LifetimeStates[selectedStrategyId.Value];
            lifetimeState.ProjectionVersion = (existingState?.ProjectionVersion ?? 0) + 1;
            lifetimeState.LastEventId = lastVisibleEventId ?? existingState?.LastEventId;
            lifetimeState.LastReconciledAtUtc = nowUtc;
            foreach (var windowHours in WindowHours)
            {
                var recentState = projection.RecentStates[(selectedStrategyId.Value, windowHours)];
                recentState.ProjectionVersion = lifetimeState.ProjectionVersion;
                recentState.LastEventId = lifetimeState.LastEventId;
                recentState.LastReconciledAtUtc = nowUtc;
            }

            await ReplaceStrategyFactsAsync(
                connection,
                transaction,
                selectedStrategyId.Value,
                facts,
                nowUtc,
                cancellationToken);
            await ReplaceStrategyPositionFactsAsync(
                connection,
                transaction,
                selectedStrategyId.Value,
                positionFacts,
                nowUtc,
                cancellationToken);
            await WriteProjectionAsync(
                connection,
                transaction,
                projection.Strategies,
                projection.LifetimeStates,
                projection.RecentStates,
                nowUtc,
                cancellationToken);
            await DeleteVisibleStrategyEventsAsync(
                connection,
                transaction,
                selectedStrategyId.Value,
                cancellationToken);
            await using (var deleteQueue = new NpgsqlCommand(
                "DELETE FROM dashboard_projection_reconciliation_queue WHERE strategy_id = @StrategyId;",
                connection,
                transaction))
            {
                deleteQueue.Parameters.AddWithValue("StrategyId", selectedStrategyId.Value);
                await deleteQueue.ExecuteNonQueryAsync(cancellationToken);
            }

            await UpdateReconciliationControlAsync(
                connection,
                transaction,
                selectedStrategyId.Value,
                nowUtc,
                cancellationToken);
            var valuesChanged = existingState is null ||
                                !EquivalentLifetimeValues(existingState, lifetimeState) ||
                                WindowHours.Any(windowHours =>
                                    !existingRecentStates.TryGetValue(windowHours, out var existingRecentState) ||
                                    !EquivalentRecentValues(
                                        existingRecentState,
                                        projection.RecentStates[(selectedStrategyId.Value, windowHours)]));
            await transaction.CommitAsync(cancellationToken);
            return new DashboardProjectionReconciliationResult(
                true,
                selectedStrategyId,
                selectedStrategyCode,
                stopwatch.Elapsed,
                valuesChanged,
                null,
                paperPositionsBuildScanDelta?.SequentialScans,
                paperPositionsBuildScanDelta?.SequentialTuplesRead);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (selectedStrategyId is not null)
            {
                await RecordReconciliationFailureAsync(
                    selectedStrategyId.Value,
                    ex.Message,
                    CancellationToken.None);
            }

            return new DashboardProjectionReconciliationResult(
                false,
                selectedStrategyId,
                selectedStrategyCode,
                stopwatch.Elapsed,
                false,
                ex.Message,
                paperPositionsBuildScanDelta?.SequentialScans,
                paperPositionsBuildScanDelta?.SequentialTuplesRead);
        }
    }

    private static async Task ConfigureReconciliationTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
SET LOCAL max_parallel_workers_per_gather = 0;
SET LOCAL work_mem = '4MB';
SET LOCAL lock_timeout = '250ms';
SET LOCAL statement_timeout = '15s';
""",
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(Guid StrategyId, string StrategyCode)?> ReadNextReconciliationCandidateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using (var queuedCommand = new NpgsqlCommand(
            """
SELECT queue.strategy_id, strategy.code
FROM dashboard_projection_reconciliation_queue queue
INNER JOIN strategies strategy ON strategy.id = queue.strategy_id
WHERE queue.next_attempt_at_utc <= clock_timestamp()
ORDER BY queue.priority DESC, queue.requested_at_utc, queue.strategy_id
LIMIT 1
FOR UPDATE OF queue SKIP LOCKED;
""",
            connection,
            transaction))
        {
            await using var queuedReader = await queuedCommand.ExecuteReaderAsync(cancellationToken);
            if (await queuedReader.ReadAsync(cancellationToken))
            {
                return (queuedReader.GetGuid(0), queuedReader.GetString(1));
            }
        }

        await using var command = new NpgsqlCommand(
            """
WITH control AS (
    SELECT reconciliation_cursor_strategy_id AS cursor_id
    FROM dashboard_projection_control
    WHERE singleton_id = 1
), candidate AS (
    SELECT strategy.id, strategy.code
    FROM strategies strategy
    CROSS JOIN control
    WHERE control.cursor_id IS NULL OR strategy.id > control.cursor_id
    ORDER BY strategy.id
    LIMIT 1
), wrapped AS (
    SELECT candidate.id, candidate.code, 0 AS priority
    FROM candidate
    UNION ALL
    SELECT strategy.id, strategy.code, 1 AS priority
    FROM strategies strategy
    WHERE NOT EXISTS (SELECT 1 FROM candidate)
    ORDER BY priority, id
    LIMIT 1
)
SELECT id, code FROM wrapped;
""",
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetString(1))
            : null;
    }

    private static async Task ReplaceStrategyFactsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        IReadOnlyList<DashboardRecentProjectionFact> facts,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using (var delete = new NpgsqlCommand(
            "DELETE FROM dashboard_strategy_recent_projection_facts WHERE strategy_id = @StrategyId;",
            connection,
            transaction))
        {
            delete.Parameters.AddWithValue("StrategyId", strategyId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        if (facts.Count == 0)
        {
            return;
        }

        await using var importer = await connection.BeginBinaryImportAsync(
            """
COPY dashboard_strategy_recent_projection_facts (
    source_kind, source_id, fact_kind, strategy_id, occurred_at_utc, contribution_json,
    applied_1h, applied_6h, applied_24h, updated_at_utc)
FROM STDIN (FORMAT BINARY)
""",
            cancellationToken);
        foreach (var fact in facts)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(fact.SourceKind, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(fact.SourceId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(fact.FactKind, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(fact.StrategyId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(UtcDateTime(fact.OccurredAtUtc), NpgsqlDbType.TimestampTz, cancellationToken);
            await importer.WriteAsync(Serialize(fact.Contribution), NpgsqlDbType.Jsonb, cancellationToken);
            await importer.WriteAsync(fact.Applied1Hour, NpgsqlDbType.Boolean, cancellationToken);
            await importer.WriteAsync(fact.Applied6Hours, NpgsqlDbType.Boolean, cancellationToken);
            await importer.WriteAsync(fact.Applied24Hours, NpgsqlDbType.Boolean, cancellationToken);
            await importer.WriteAsync(UtcDateTime(nowUtc), NpgsqlDbType.TimestampTz, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task ReplaceStrategyPositionFactsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        IReadOnlyList<PaperPositionProjectionPayload> facts,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using (var delete = new NpgsqlCommand(
            "DELETE FROM dashboard_strategy_position_projection_facts WHERE strategy_id = @StrategyId;",
            connection,
            transaction))
        {
            delete.Parameters.AddWithValue("StrategyId", strategyId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        if (facts.Count == 0)
        {
            return;
        }

        await using var importer = await connection.BeginBinaryImportAsync(
            """
COPY dashboard_strategy_position_projection_facts (
    source_id, strategy_id, size_shares, unrealized_pnl_usd, updated_at_utc)
FROM STDIN (FORMAT BINARY)
""",
            cancellationToken);
        foreach (var fact in facts)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(fact.Id, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(fact.StrategyId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(fact.SizeShares, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(fact.UnrealizedPnlUsd, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(UtcDateTime(nowUtc), NpgsqlDbType.TimestampTz, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task<long?> ReadLastVisibleStrategyEventIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT max(id) FROM dashboard_projection_events WHERE strategy_id = @StrategyId;",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : (long)value;
    }

    private static async Task DeleteVisibleStrategyEventsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM dashboard_projection_events WHERE strategy_id = @StrategyId;",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateReconciliationControlAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
UPDATE dashboard_projection_control
SET status = 'Running',
    reconciliation_cursor_strategy_id = @StrategyId,
    last_reconciliation_at_utc = @NowUtc,
    last_error = NULL,
    updated_at_utc = @NowUtc
WHERE singleton_id = 1;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("NowUtc", UtcDateTime(nowUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RecordReconciliationFailureAsync(
        Guid strategyId,
        string error,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
INSERT INTO dashboard_projection_reconciliation_queue (
    strategy_id, priority, reason, requested_at_utc, attempt_count, next_attempt_at_utc, last_error)
VALUES (
    @StrategyId, 100, 'reconciliation_failure', clock_timestamp(), 1,
    clock_timestamp() + interval '1 minute', @Error)
ON CONFLICT (strategy_id) DO UPDATE SET
    attempt_count = dashboard_projection_reconciliation_queue.attempt_count + 1,
    next_attempt_at_utc = clock_timestamp() + interval '1 minute',
    last_error = EXCLUDED.last_error;

UPDATE dashboard_projection_control
SET status = 'ReconciliationFailed',
    last_error = @Error,
    updated_at_utc = clock_timestamp()
WHERE singleton_id = 1;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("Error", error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool EquivalentLifetimeValues(
        DashboardLifetimeProjectionState existing,
        DashboardLifetimeProjectionState recalculated)
    {
        return EquivalentProperties(existing, recalculated, LifetimeValueProperties);
    }

    private static bool EquivalentRecentValues(
        DashboardRecentProjectionState existing,
        DashboardRecentProjectionState recalculated)
    {
        return EquivalentProperties(existing, recalculated, RecentValueProperties) &&
               existing.MaxEntryDelaySeconds == recalculated.MaxEntryDelaySeconds &&
               existing.SkipReasonCounts.Count == recalculated.SkipReasonCounts.Count &&
               existing.SkipReasonCounts.All(pair =>
                   recalculated.SkipReasonCounts.TryGetValue(pair.Key, out var count) &&
                   count == pair.Value);
    }

    private static bool EquivalentProperties(
        object existing,
        object recalculated,
        IEnumerable<PropertyInfo> properties)
    {
        return properties.All(property =>
            Equals(property.GetValue(existing), property.GetValue(recalculated)));
    }
}
