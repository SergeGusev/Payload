using System.Runtime.CompilerServices;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class PaperFakFeeBackfillEventStorageTests
{
    [Fact]
    public void Schema_DeclaresDedicatedEventTableAndRetentionIndex()
    {
        Assert.Contains(
            "paper_fak_fee_backfill_events",
            PostgresSchema.RequiredTables);
        Assert.Contains(
            "CREATE TABLE IF NOT EXISTS paper_fak_fee_backfill_events",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE INDEX IF NOT EXISTS ix_paper_fak_fee_backfill_events_occurred",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ON paper_fak_fee_backfill_events(occurred_at_utc DESC, id DESC)",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryCleanup_UsesStrictCutoffAndOldestFirstBoundedSelection()
    {
        var source = ReadRepositorySource(
            Path.Combine(
                "src",
                "PolyCopyTrader.Storage",
                "PostgresAppRepository.PaperFakFeeBackfillEvents.cs"));

        Assert.Contains(
            "INSERT INTO paper_fak_fee_backfill_events",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE occurred_at_utc < @OccurredBeforeUtc",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY occurred_at_utc ASC, id ASC",
            source,
            StringComparison.Ordinal);
        Assert.Contains("LIMIT @BatchSize", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE SKIP LOCKED", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestRepositoryCleanup_PreservesBoundaryAndDeletesOldestRowsFirst()
    {
        var repository = new TestAppRepository();
        var cutoffUtc = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
        var oldestId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var olderId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var boundaryId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var recentId = Guid.Parse("00000000-0000-0000-0000-000000000004");
        repository.PaperFakFeeBackfillEvents.AddRange(
        [
            CreateEvent(recentId, cutoffUtc.AddMinutes(1)),
            CreateEvent(boundaryId, cutoffUtc),
            CreateEvent(olderId, cutoffUtc.AddHours(-1)),
            CreateEvent(oldestId, cutoffUtc.AddHours(-2))
        ]);

        var firstDeleted = await repository.CleanupPaperFakFeeBackfillEventsAsync(
            cutoffUtc,
            batchSize: 1);

        Assert.Equal(1, firstDeleted);
        Assert.DoesNotContain(repository.PaperFakFeeBackfillEvents, entry => entry.Id == oldestId);
        Assert.Contains(repository.PaperFakFeeBackfillEvents, entry => entry.Id == olderId);
        Assert.Contains(repository.PaperFakFeeBackfillEvents, entry => entry.Id == boundaryId);

        var remainingDeleted = await repository.CleanupPaperFakFeeBackfillEventsAsync(
            cutoffUtc,
            batchSize: 500);

        Assert.Equal(1, remainingDeleted);
        Assert.DoesNotContain(repository.PaperFakFeeBackfillEvents, entry => entry.Id == olderId);
        Assert.Contains(repository.PaperFakFeeBackfillEvents, entry => entry.Id == boundaryId);
        Assert.Contains(repository.PaperFakFeeBackfillEvents, entry => entry.Id == recentId);
        Assert.Equal(
            [(cutoffUtc, 1), (cutoffUtc, 500)],
            repository.PaperFakFeeBackfillEventCleanupCalls);
    }

    [Fact]
    public async Task TestRepository_PersistsIncrementalParityJournalAfterTargetCommit()
    {
        var repository = new TestAppRepository();
        var cycleId = Guid.Parse("81000000-0000-0000-0000-000000000001");
        var targetCommitted = CreateEvent(Guid.NewGuid(), DateTimeOffset.UtcNow) with
        {
            CycleId = cycleId,
            EventType = PaperFakFeeBackfillEventTypes.ParityTargetCommitted,
            Candidates = 1,
            EvaluatedForApply = 1,
            ReachedSweepEnd = false
        };
        var page = CreateEvent(Guid.NewGuid(), targetCommitted.OccurredAtUtc.AddSeconds(1)) with
        {
            CycleId = cycleId,
            EventType = PaperFakFeeBackfillEventTypes.ParityPageCompleted,
            Candidates = 5,
            EvaluatedForApply = 4,
            StructuralConflicts = 1,
            ReachedSweepEnd = true
        };

        await repository.AddPaperFakFeeBackfillEventAsync(targetCommitted);
        await repository.AddPaperFakFeeBackfillEventAsync(page);

        Assert.Equal([targetCommitted, page], repository.PaperFakFeeBackfillEvents);
        Assert.All(repository.PaperFakFeeBackfillEvents, entry => Assert.Equal(cycleId, entry.CycleId));
        Assert.All(repository.PaperFakFeeBackfillEvents, entry => Assert.Null(entry.SweepId));
    }

    private static PaperFakFeeBackfillEvent CreateEvent(Guid id, DateTimeOffset occurredAtUtc)
    {
        return new PaperFakFeeBackfillEvent
        {
            Id = id,
            WorkerInstanceId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Sequence = 1,
            OccurredAtUtc = occurredAtUtc,
            Level = PaperFakFeeBackfillEventLevels.Information,
            EventType = PaperFakFeeBackfillEventTypes.CycleCompleted,
            Message = "test",
            BuildVersion = "test",
            HostName = "test-host",
            ProcessId = 1
        };
    }

    private static string ReadRepositorySource(
        string relativePath,
        [CallerFilePath] string sourceFilePath = "")
    {
        var testProjectDirectory = Directory.GetParent(sourceFilePath)?.FullName ??
            throw new InvalidOperationException("The test source directory is unavailable.");
        var repositoryRoot = Path.GetFullPath(Path.Combine(testProjectDirectory, "..", ".."));
        var candidate = Path.Combine(repositoryRoot, relativePath);
        if (File.Exists(candidate))
        {
            return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
