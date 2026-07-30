using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class StrategyRunRetentionTests
{
    [Fact]
    public async Task RunCycleAsync_PreviewOnlyModeNeverTransfersRows()
    {
        var firstRunId = Guid.NewGuid();
        var secondRunId = Guid.NewGuid();
        var nowUtc = new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        var repository = new TestAppRepository();
        repository.StrategyRunRetentionSummaries.Enqueue(new StrategyRunRetentionSummary(
            2,
            2,
            nowUtc.AddDays(-4),
            nowUtc.AddDays(-3),
            [firstRunId, secondRunId]));
        var options = new StrategyRunRetentionOptions
        {
            Enabled = true,
            ApplyEnabled = false,
            RawRetentionHours = 72,
            CleanupBatchSize = 10,
            CleanupMaxBatchesPerCycle = 3
        };
        var worker = new StrategyRunRetentionWorker(
            NullLogger<StrategyRunRetentionWorker>.Instance,
            options,
            repository);

        var result = await worker.RunCycleAsync(nowUtc);

        Assert.False(result.ApplyEnabled);
        Assert.Equal(2, result.PreviewedRows);
        Assert.Equal(0, result.TransferredRows);
        Assert.Empty(repository.StrategyRunRetentionTransferCalls);
        Assert.Empty(repository.StrategyRunRetentionPreviewCalls);
        var summaryCall = Assert.Single(repository.StrategyRunRetentionSummaryCalls);
        Assert.Equal(nowUtc.AddHours(-72), summaryCall.UpdatedBeforeUtc);
        Assert.Equal(10, summaryCall.SampleLimit);
    }

    [Fact]
    public async Task RunCycleAsync_TransfersExactlyThePreviewedAllowlist()
    {
        var previewedRunIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var nowUtc = new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        var repository = new TestAppRepository
        {
            StrategyRunRetentionTransferResult = new StrategyRunRetentionBatchResult(2, 2, 1, 2, 1)
        };
        repository.StrategyRunRetentionPreviews.Enqueue(new StrategyRunRetentionPreview(
            previewedRunIds,
            1,
            nowUtc.AddDays(-4),
            nowUtc.AddDays(-3)));
        var options = new StrategyRunRetentionOptions
        {
            Enabled = true,
            ApplyEnabled = true,
            RawRetentionHours = 48,
            CleanupBatchSize = 10,
            CleanupMaxBatchesPerCycle = 2
        };
        var worker = new StrategyRunRetentionWorker(
            NullLogger<StrategyRunRetentionWorker>.Instance,
            options,
            repository);

        var result = await worker.RunCycleAsync(nowUtc);

        Assert.True(result.ApplyEnabled);
        Assert.Equal(2, result.PreviewedRows);
        Assert.Equal(2, result.TransferredRows);
        Assert.Equal(1, result.RollupRowsChanged);
        Assert.Equal(2, result.TombstonesChanged);
        Assert.Equal(1, result.StrategiesQueuedForReconciliation);
        var transferCall = Assert.Single(repository.StrategyRunRetentionTransferCalls);
        Assert.Equal(previewedRunIds, transferCall.RunIds);
        Assert.Equal(nowUtc.AddHours(-48), transferCall.UpdatedBeforeUtc);
    }

    [Fact]
    public void Configuration_DefaultsToDisabledAndRejectsUnsafeActivation()
    {
        var defaults = new StrategyRunRetentionOptions();

        Assert.False(defaults.Enabled);
        Assert.False(defaults.ApplyEnabled);
        Assert.Equal(48, defaults.RawRetentionHours);

        var errors = AppOptionsValidator.Validate(new AppConfiguration
        {
            StrategyRunRetention = new StrategyRunRetentionOptions
            {
                Enabled = false,
                ApplyEnabled = true,
                RawRetentionHours = 24,
                CleanupIntervalMinutes = 0,
                CleanupBatchSize = 25_001,
                CleanupMaxBatchesPerCycle = 101
            }
        });

        Assert.Contains(errors, error => error.Contains("ApplyEnabled requires", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("RawRetentionHours", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CleanupIntervalMinutes", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CleanupBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CleanupMaxBatchesPerCycle", StringComparison.Ordinal));
    }

    [Fact]
    public void Appsettings_LeavesRetentionAndApplyDisabled()
    {
        var appsettings = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "appsettings.json");
        using var document = JsonDocument.Parse(appsettings);
        var retention = document.RootElement.GetProperty("StrategyRunRetention");

        Assert.False(retention.GetProperty("Enabled").GetBoolean());
        Assert.False(retention.GetProperty("ApplyEnabled").GetBoolean());
        Assert.True(retention.GetProperty("RawRetentionHours").GetInt32() >= 48);
    }

    [Fact]
    public void Schema_InstallsLiveGuardAtomicallyAndKeepsRetentionScopeMonotonic()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
        var beginIndex = FindStatement(statements, statement => statement == "BEGIN;");
        var lockIndex = FindStatement(statements, statement =>
            statement.Contains("LOCK TABLE public.strategies IN SHARE ROW EXCLUSIVE MODE", StringComparison.Ordinal));
        var guardTableIndex = FindStatement(statements, statement =>
            statement.Contains("CREATE TABLE IF NOT EXISTS strategy_live_retention_guards", StringComparison.Ordinal));
        var guardTriggerIndex = FindStatement(statements, statement =>
            statement.Contains("CREATE TRIGGER trg_record_strategy_live_retention_guard", StringComparison.Ordinal));
        var guardBackfillIndex = FindStatement(statements, statement =>
            statement.StartsWith("INSERT INTO strategy_live_retention_guards", StringComparison.Ordinal));
        var commitIndex = FindStatement(statements, statement => statement == "COMMIT;");

        Assert.All(
            new[] { beginIndex, lockIndex, guardTableIndex, guardTriggerIndex, guardBackfillIndex, commitIndex },
            index => Assert.True(index >= 0));
        Assert.True(beginIndex < lockIndex);
        Assert.True(lockIndex < guardTableIndex);
        Assert.True(guardTableIndex < guardTriggerIndex);
        Assert.True(guardTriggerIndex < guardBackfillIndex);
        Assert.True(guardBackfillIndex < commitIndex);
        Assert.Contains("IF NEW.live_stakes OR was_live THEN", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("OR NEW.retention_scope = 'LiveOrShadow'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains(
            "BEFORE INSERT OR UPDATE OF status, signal_id, paper_order_id, retention_scope",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SET retention_scope = 'PaperOnly'",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RetentionSql_IsFailClosedAndUsesExactSerializableAllowlist()
    {
        var repositorySource = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.Retention.cs");
        var projectionSchema = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "DashboardProjectionSchema.cs");

        Assert.Contains("run.retention_scope = 'PaperOnly'", repositorySource, StringComparison.Ordinal);
        Assert.Contains("clock_timestamp() - interval '48 hours'", repositorySource, StringComparison.Ordinal);
        Assert.Contains("run.market_end_utc IS NOT NULL", repositorySource, StringComparison.Ordinal);
        Assert.Contains("strategy_market_paper_run_retention_blockers(run)", repositorySource, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", repositorySource, StringComparison.Ordinal);
        Assert.Contains("run.id = ANY(@RunIds)", repositorySource, StringComparison.Ordinal);
        Assert.Contains("result.SelectedRows != normalizedRunIds.Length", repositorySource, StringComparison.Ordinal);
        Assert.Contains("count(*)::bigint AS total_candidate_rows", repositorySource, StringComparison.Ordinal);

        Assert.Contains("signal_id", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("paper_order_dependency", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("live_order_dependency", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("live_shadow_dependency", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("paper_position_dependency", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("paper_settlement_dependency", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("pending_projection_event", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("recent_projection_fact", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("pending_projection_reconciliation", projectionSchema, StringComparison.Ordinal);
        Assert.Contains(
            "current_setting('polycopytrader.skip_run_retention_transfer', true) = 'on'",
            projectionSchema,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TombstonesBlockEveryRuntimeReinsertPath()
    {
        var schema = PostgresSchema.SchemaSql;
        var repositorySource = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.cs");

        Assert.Contains("prevent_archived_strategy_market_paper_run_reinsert", schema, StringComparison.Ordinal);
        Assert.Contains(
            "BEFORE INSERT ON public.strategy_market_paper_runs",
            schema,
            StringComparison.Ordinal);
        Assert.Contains("CHECK (retention_scope IN ('Unknown', 'PaperOnly', 'LiveOrShadow')) NOT VALID", schema, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(repositorySource, "FROM strategy_market_paper_skip_tombstones tombstone") >= 3,
            "All three current strategy-run INSERT paths must check tombstones explicitly.");
    }

    private static int FindStatement(
        IReadOnlyList<string> statements,
        Func<string, bool> predicate)
    {
        for (var index = 0; index < statements.Count; index++)
        {
            if (predicate(statements[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = value.IndexOf(search, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += search.Length;
        }

        return count;
    }

    private static string ReadRepositorySource(params string[] segments)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("POLYCOPYTRADER_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var configuredPath = Path.GetFullPath(Path.Combine(configuredRoot, Path.Combine(segments)));
            if (File.Exists(configuredPath))
            {
                return File.ReadAllText(configuredPath);
            }
        }

        var workingDirectoryPath = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            Path.Combine(segments)));
        if (File.Exists(workingDirectoryPath))
        {
            return File.ReadAllText(workingDirectoryPath);
        }

        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            Path.Combine(segments)));
        return File.ReadAllText(path);
    }
}
