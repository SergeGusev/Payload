using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class PaperFakFeeBackfillEventPostgresIntegrationTests
{
    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task EventApis_InsertTypedRowsAndCleanupOnlyStrictlyOlderRowsInBoundedBatches()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var workerInstanceId = Guid.NewGuid();
        var oldId = Guid.NewGuid();
        var boundaryId = Guid.NewGuid();
        var recentId = Guid.NewGuid();
        Guid[] eventIds = [oldId, boundaryId, recentId];
        var cutoffUtc = new DateTimeOffset(2000, 1, 2, 0, 0, 0, TimeSpan.Zero);

        try
        {
            Assert.Equal(0, await CountRowsOlderThanAsync(factory, cutoffUtc));

            await repository.AddPaperFakFeeBackfillEventAsync(CreateEvent(
                oldId,
                workerInstanceId,
                sequence: 1,
                cutoffUtc.AddMinutes(-1)) with
            {
                SweepId = Guid.NewGuid(),
                CycleId = Guid.NewGuid(),
                Level = PaperFakFeeBackfillEventLevels.Error,
                EventType = PaperFakFeeBackfillEventTypes.CycleFailed,
                Message = "integration cycle failed",
                BackfillEnabled = true,
                ApplyEnabled = true,
                CutoffUtc = cutoffUtc.AddYears(26),
                BatchSize = 50,
                PendingPaperEntryBatches = 2,
                PendingMarketDataUpdates = 3,
                DelaySeconds = 60,
                StrategyId = Guid.NewGuid(),
                StrategyCode = "integration-strategy",
                StrategyRank = 4,
                StrategyCount = 10,
                GrossRealizedPnlUsd = 12.34m,
                Candidates = 50,
                EvaluatedForApply = 49,
                TransientLookupUnavailable = 1,
                Requested = 49,
                Eligible = 48,
                FullChainEligible = 40,
                RunOnlyLegacyEligible = 8,
                FillsUpdated = 48,
                RunsUpdated = 48,
                PositionsUpdated = 40,
                SettlementsUpdated = 40,
                FullChainAlreadyApplied = 1,
                RunOnlyLegacyAlreadyApplied = 2,
                AlreadyApplied = 3,
                StructuralConflicts = 1,
                AccountingConflicts = 0,
                DeferredByLockTimeout = 0,
                DeferredByQueryCancel = 0,
                ReachedStrategyEnd = false,
                ReachedSweepEnd = false,
                DurationMilliseconds = 1234,
                ExceptionType = "System.InvalidOperationException",
                ExceptionMessage = "integration failure"
            });
            await repository.AddPaperFakFeeBackfillEventAsync(CreateEvent(
                boundaryId,
                workerInstanceId,
                sequence: 2,
                cutoffUtc));
            await repository.AddPaperFakFeeBackfillEventAsync(CreateEvent(
                recentId,
                workerInstanceId,
                sequence: 3,
                cutoffUtc.AddMinutes(1)));

            var persisted = await ReadRowsAsync(factory, eventIds);
            Assert.Equal(3, persisted.Count);
            var old = Assert.Single(persisted, row => row.Id == oldId);
            Assert.Equal(PaperFakFeeBackfillEventTypes.CycleFailed, old.EventType);
            Assert.Equal(49, old.Requested);
            Assert.Equal(12.34m, old.GrossRealizedPnlUsd);
            Assert.True(old.ApplyEnabled is true);

            var firstDeleted = await repository.CleanupPaperFakFeeBackfillEventsAsync(
                cutoffUtc,
                batchSize: 1);

            Assert.Equal(1, firstDeleted);
            var afterFirstCleanup = await ReadRowsAsync(factory, eventIds);
            Assert.DoesNotContain(afterFirstCleanup, row => row.Id == oldId);
            Assert.Contains(afterFirstCleanup, row => row.Id == boundaryId);
            Assert.Contains(afterFirstCleanup, row => row.Id == recentId);

            var secondDeleted = await repository.CleanupPaperFakFeeBackfillEventsAsync(
                cutoffUtc,
                batchSize: 1);

            Assert.Equal(0, secondDeleted);
            Assert.Equal(
                new[] { boundaryId, recentId }.Order().ToArray(),
                (await ReadRowsAsync(factory, eventIds))
                    .Select(row => row.Id)
                    .Order()
                    .ToArray());
        }
        finally
        {
            await DeleteExactRowsAsync(factory, eventIds);
        }
    }

    private static PaperFakFeeBackfillEvent CreateEvent(
        Guid id,
        Guid workerInstanceId,
        long sequence,
        DateTimeOffset occurredAtUtc)
    {
        return new PaperFakFeeBackfillEvent
        {
            Id = id,
            WorkerInstanceId = workerInstanceId,
            Sequence = sequence,
            OccurredAtUtc = occurredAtUtc,
            Level = PaperFakFeeBackfillEventLevels.Information,
            EventType = PaperFakFeeBackfillEventTypes.CycleCompleted,
            Message = "integration event",
            BuildVersion = "integration-test",
            HostName = "integration-test-host",
            ProcessId = Environment.ProcessId
        };
    }

    private static async Task<PostgresConnectionFactory> CreateFactoryAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "POLYCOPYTRADER_TEST_POSTGRES_CONNECTION disappeared after test discovery.");
        }

        var factory = new PostgresConnectionFactory(
            new StorageOptions { ConnectionString = connectionString },
            "PolyCopyTrader.Tests.BackfillEvents");
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        return factory;
    }

    private static async Task<int> CountRowsOlderThanAsync(
        PostgresConnectionFactory factory,
        DateTimeOffset cutoffUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*)::integer FROM paper_fak_fee_backfill_events " +
            "WHERE occurred_at_utc < @CutoffUtc;",
            connection);
        command.Parameters.Add("CutoffUtc", NpgsqlDbType.TimestampTz).Value = cutoffUtc.UtcDateTime;
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<IReadOnlyList<PersistedEventRow>> ReadRowsAsync(
        PostgresConnectionFactory factory,
        Guid[] eventIds)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
SELECT id, event_type, requested, gross_realized_pnl_usd, apply_enabled
FROM paper_fak_fee_backfill_events
WHERE id = ANY(@Ids)
ORDER BY id;
""", connection);
        command.Parameters.Add("Ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = eventIds;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<PersistedEventRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new PersistedEventRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetBoolean(4)));
        }

        return rows;
    }

    private static async Task DeleteExactRowsAsync(
        PostgresConnectionFactory factory,
        Guid[] eventIds)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM paper_fak_fee_backfill_events WHERE id = ANY(@Ids);",
            connection);
        command.Parameters.Add("Ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = eventIds;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record PersistedEventRow(
        Guid Id,
        string EventType,
        int? Requested,
        decimal? GrossRealizedPnlUsd,
        bool? ApplyEnabled);
}
