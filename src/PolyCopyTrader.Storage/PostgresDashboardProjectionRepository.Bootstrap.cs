using System.Data;
using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresDashboardProjectionRepository
{
    private const long ProjectionAdvisoryLockKey = 6_842_827_444_835_641L;

    public async Task<DashboardProjectionBootstrapResult> BootstrapAsync(
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        await SetBootstrapStatusAsync("Bootstrapping", null, cancellationToken);

        await using var readConnection = CreateBootstrapConnection();
        await using var writeConnection = CreateBootstrapConnection();
        await readConnection.OpenAsync(cancellationToken);
        await writeConnection.OpenAsync(cancellationToken);
        await using var readTransaction = await readConnection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await using var writeTransaction = await writeConnection.BeginTransactionAsync(cancellationToken);

        if (!await TryAcquireProjectionLockAsync(writeConnection, writeTransaction, cancellationToken))
        {
            throw new InvalidOperationException("Another Dashboard projection worker owns the projection lock.");
        }

        await ConfigureBootstrapReadTransactionAsync(readConnection, readTransaction, cancellationToken);
        var (bootstrapSnapshot, projectionNowUtc) = await ReadBootstrapSnapshotAsync(
            readConnection,
            readTransaction,
            cancellationToken);
        await ClearProjectionBuildTablesAsync(writeConnection, writeTransaction, cancellationToken);

        var factCount = 0;
        ProjectionBuildResult projection;
        await using (var importer = await writeConnection.BeginBinaryImportAsync(
            """
COPY dashboard_strategy_recent_projection_facts (
    source_kind,
    source_id,
    fact_kind,
    strategy_id,
    occurred_at_utc,
    contribution_json,
    applied_1h,
    applied_6h,
    applied_24h,
    updated_at_utc)
FROM STDIN (FORMAT BINARY)
""",
            cancellationToken))
        {
            projection = await BuildProjectionAsync(
                readConnection,
                readTransaction,
                strategyId: null,
                projectionNowUtc,
                async (fact, token) =>
                {
                    await importer.StartRowAsync(token);
                    await importer.WriteAsync(fact.SourceKind, NpgsqlDbType.Text, token);
                    await importer.WriteAsync(fact.SourceId, NpgsqlDbType.Uuid, token);
                    await importer.WriteAsync(fact.FactKind, NpgsqlDbType.Text, token);
                    await importer.WriteAsync(fact.StrategyId, NpgsqlDbType.Uuid, token);
                    await importer.WriteAsync(UtcDateTime(fact.OccurredAtUtc), NpgsqlDbType.TimestampTz, token);
                    await importer.WriteAsync(Serialize(fact.Contribution), NpgsqlDbType.Jsonb, token);
                    await importer.WriteAsync(fact.Applied1Hour, NpgsqlDbType.Boolean, token);
                    await importer.WriteAsync(fact.Applied6Hours, NpgsqlDbType.Boolean, token);
                    await importer.WriteAsync(fact.Applied24Hours, NpgsqlDbType.Boolean, token);
                    await importer.WriteAsync(UtcDateTime(projectionNowUtc), NpgsqlDbType.TimestampTz, token);
                    factCount++;
                },
                positionFactSink: null,
                includePaperPositions: false,
                cancellationToken);
            await importer.CompleteAsync(cancellationToken);
        }

        await using (var positionImporter = await writeConnection.BeginBinaryImportAsync(
            """
COPY dashboard_strategy_position_projection_facts (
    source_id,
    strategy_id,
    size_shares,
    unrealized_pnl_usd,
    updated_at_utc)
FROM STDIN (FORMAT BINARY)
""",
            cancellationToken))
        {
            await AccumulatePaperPositionsAsync(
                readConnection,
                readTransaction,
                strategyId: null,
                projection.Strategies,
                projection.LifetimeStates,
                async (fact, token) =>
                {
                    await positionImporter.StartRowAsync(token);
                    await positionImporter.WriteAsync(fact.Id, NpgsqlDbType.Uuid, token);
                    await positionImporter.WriteAsync(fact.StrategyId, NpgsqlDbType.Uuid, token);
                    await positionImporter.WriteAsync(fact.SizeShares, NpgsqlDbType.Numeric, token);
                    await positionImporter.WriteAsync(fact.UnrealizedPnlUsd, NpgsqlDbType.Numeric, token);
                    await positionImporter.WriteAsync(UtcDateTime(projectionNowUtc), NpgsqlDbType.TimestampTz, token);
                },
                cancellationToken);
            await positionImporter.CompleteAsync(cancellationToken);
        }

        await readTransaction.CommitAsync(cancellationToken);

        foreach (var state in projection.LifetimeStates.Values)
        {
            state.ProjectionVersion = 1;
            state.LastReconciledAtUtc = projectionNowUtc;
        }

        foreach (var state in projection.RecentStates.Values)
        {
            state.ProjectionVersion = 1;
            state.LastReconciledAtUtc = projectionNowUtc;
        }

        await WriteProjectionAsync(
            writeConnection,
            writeTransaction,
            projection.Strategies,
            projection.LifetimeStates,
            projection.RecentStates,
            projectionNowUtc,
            cancellationToken);
        await DeleteRemovedStrategyProjectionRowsAsync(writeConnection, writeTransaction, cancellationToken);
        var discardedEvents = await DeleteBootstrapVisibleEventsAsync(
            writeConnection,
            writeTransaction,
            bootstrapSnapshot,
            cancellationToken);
        await using (var clearQueue = new NpgsqlCommand(
            "DELETE FROM dashboard_projection_reconciliation_queue;",
            writeConnection,
            writeTransaction))
        {
            await clearQueue.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var completeControl = new NpgsqlCommand(
            """
UPDATE dashboard_projection_control
SET initialized = true,
    calculation_version = @CalculationVersion,
    status = 'Running',
    reconciliation_cursor_strategy_id = NULL,
    bootstrap_completed_at_utc = @CompletedAtUtc,
    last_event_applied_at_utc = @CompletedAtUtc,
    last_expiry_at_utc = @CompletedAtUtc,
    last_reconciliation_at_utc = @CompletedAtUtc,
    last_error = NULL,
    updated_at_utc = @CompletedAtUtc
WHERE singleton_id = 1;
""",
            writeConnection,
            writeTransaction))
        {
            completeControl.Parameters.AddWithValue("CalculationVersion", CalculationVersion);
            completeControl.Parameters.AddWithValue("CompletedAtUtc", UtcDateTime(projectionNowUtc));
            await completeControl.ExecuteNonQueryAsync(cancellationToken);
        }

        await writeTransaction.CommitAsync(cancellationToken);
        return new DashboardProjectionBootstrapResult(
            projection.Strategies.Count,
            factCount,
            projection.RecentStates.Count,
            discardedEvents,
            stopwatch.Elapsed);
    }

    internal static async Task<bool> TryAcquireProjectionLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_xact_lock(@LockKey);",
            connection,
            transaction);
        command.Parameters.AddWithValue("LockKey", ProjectionAdvisoryLockKey);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private NpgsqlConnection CreateBootstrapConnection()
    {
        return new NpgsqlConnection(CreateBootstrapConnectionString(connectionFactory.ConnectionString));
    }

    internal static string CreateBootstrapConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            CommandTimeout = 0
        };
        return builder.ConnectionString;
    }

    private async Task SetBootstrapStatusAsync(
        string status,
        string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
UPDATE dashboard_projection_control
SET initialized = false,
    status = @Status,
    bootstrap_started_at_utc = clock_timestamp(),
    bootstrap_completed_at_utc = NULL,
    last_error = @Error,
    updated_at_utc = clock_timestamp()
WHERE singleton_id = 1;
""",
            connection);
        command.Parameters.AddWithValue("Status", status);
        AddNullable(command, "Error", error, NpgsqlDbType.Text);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ConfigureBootstrapReadTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
SET LOCAL max_parallel_workers_per_gather = 0;
SET LOCAL work_mem = '4MB';
SET LOCAL lock_timeout = '500ms';
""",
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(string Snapshot, DateTimeOffset NowUtc)> ReadBootstrapSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_current_snapshot()::text, clock_timestamp();",
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Could not capture Dashboard projection bootstrap snapshot.");
        }

        return (reader.GetString(0), UtcNow(reader.GetDateTime(1)));
    }

    private static async Task ClearProjectionBuildTablesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
DELETE FROM dashboard_strategy_recent_projection_facts;
DELETE FROM dashboard_strategy_position_projection_facts;
DELETE FROM dashboard_strategy_recent_projection_states;
DELETE FROM dashboard_strategy_lifetime_projection_states;
""",
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteRemovedStrategyProjectionRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
DELETE FROM dashboard_strategy_performance_snapshots snapshot
WHERE NOT EXISTS (SELECT 1 FROM strategies strategy WHERE strategy.id = snapshot.strategy_id);

DELETE FROM dashboard_strategy_recent_performance_snapshots snapshot
WHERE NOT EXISTS (SELECT 1 FROM strategies strategy WHERE strategy.id = snapshot.strategy_id);
""",
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> DeleteBootstrapVisibleEventsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string bootstrapSnapshot,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
DELETE FROM dashboard_projection_events
WHERE pg_visible_in_snapshot(transaction_id, CAST(@BootstrapSnapshot AS pg_snapshot));
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("BootstrapSnapshot", bootstrapSnapshot);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
