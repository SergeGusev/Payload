using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class StrategyRunRetentionTests
{
    [Fact]
    public async Task RunCycleAsync_PreviewOnlyModeUsesBoundedPageAndNeverTransfersRows()
    {
        var firstRunId = Guid.NewGuid();
        var secondRunId = Guid.NewGuid();
        var nowUtc = new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        var repository = new TestAppRepository();
        repository.StrategyRunRetentionPreviews.Enqueue(new StrategyRunRetentionPreview(
            [firstRunId, secondRunId],
            2,
            nowUtc.AddDays(-4),
            nowUtc.AddDays(-3),
            2,
            null,
            true));
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
        Assert.Empty(repository.StrategyRunRetentionSummaryCalls);
        var previewCall = Assert.Single(repository.StrategyRunRetentionPreviewCalls);
        Assert.Equal(nowUtc.AddHours(-72), previewCall.UpdatedBeforeUtc);
        Assert.Equal(10, previewCall.Limit);
        Assert.Null(previewCall.AfterCursor);
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
        var continuationCursor = new StrategyRunRetentionCursor(
            nowUtc.AddDays(-3),
            Guid.NewGuid());
        repository.StrategyRunRetentionPreviews.Enqueue(new StrategyRunRetentionPreview(
            previewedRunIds,
            1,
            nowUtc.AddDays(-4),
            nowUtc.AddDays(-3),
            10,
            continuationCursor,
            false));
        repository.StrategyRunRetentionPreviews.Enqueue(new StrategyRunRetentionPreview(
            [],
            0,
            null,
            null,
            3,
            null,
            true));
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
        Assert.Collection(
            repository.StrategyRunRetentionPreviewCalls,
            firstCall =>
            {
                Assert.Equal(nowUtc.AddHours(-48), firstCall.UpdatedBeforeUtc);
                Assert.Null(firstCall.AfterCursor);
            },
            secondCall =>
            {
                Assert.Equal(nowUtc.AddHours(-48), secondCall.UpdatedBeforeUtc);
                Assert.Equal(continuationCursor, secondCall.AfterCursor);
            });
    }

    [Fact]
    public async Task RunCycleAsync_AllBlockedPageAdvancesCursorAcrossCyclesAndResetsAtEnd()
    {
        var candidateRunId = Guid.NewGuid();
        var nowUtc = new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        var continuationCursor = new StrategyRunRetentionCursor(
            nowUtc.AddDays(-4),
            Guid.NewGuid());
        var repository = new TestAppRepository
        {
            StrategyRunRetentionTransferResult = new StrategyRunRetentionBatchResult(1, 1, 1, 1, 1)
        };
        repository.StrategyRunRetentionPreviews.Enqueue(new StrategyRunRetentionPreview(
            [],
            0,
            null,
            null,
            10,
            continuationCursor,
            false));
        repository.StrategyRunRetentionPreviews.Enqueue(new StrategyRunRetentionPreview(
            [candidateRunId],
            1,
            nowUtc.AddDays(-3),
            nowUtc.AddDays(-3),
            1,
            null,
            true));
        repository.StrategyRunRetentionPreviews.Enqueue(new StrategyRunRetentionPreview(
            [],
            0,
            null,
            null,
            0,
            null,
            true));
        var options = new StrategyRunRetentionOptions
        {
            Enabled = true,
            ApplyEnabled = true,
            RawRetentionHours = 48,
            CleanupBatchSize = 10,
            CleanupMaxBatchesPerCycle = 1
        };
        var worker = new StrategyRunRetentionWorker(
            NullLogger<StrategyRunRetentionWorker>.Instance,
            options,
            repository);

        var blockedPageResult = await worker.RunCycleAsync(nowUtc);
        var eligiblePageResult = await worker.RunCycleAsync(nowUtc.AddMinutes(1));
        var restartedSweepResult = await worker.RunCycleAsync(nowUtc.AddMinutes(2));

        Assert.Equal(0, blockedPageResult.PreviewedRows);
        Assert.Equal(0, blockedPageResult.TransferredRows);
        Assert.Equal(1, eligiblePageResult.PreviewedRows);
        Assert.Equal(1, eligiblePageResult.TransferredRows);
        Assert.Equal(0, restartedSweepResult.PreviewedRows);
        Assert.Equal(0, restartedSweepResult.TransferredRows);
        var transferCall = Assert.Single(repository.StrategyRunRetentionTransferCalls);
        Assert.Equal(new[] { candidateRunId }, transferCall.RunIds);
        Assert.Equal(nowUtc.AddHours(-48), transferCall.UpdatedBeforeUtc);
        Assert.Collection(
            repository.StrategyRunRetentionPreviewCalls,
            firstCall =>
            {
                Assert.Equal(nowUtc.AddHours(-48), firstCall.UpdatedBeforeUtc);
                Assert.Null(firstCall.AfterCursor);
            },
            secondCall =>
            {
                Assert.Equal(nowUtc.AddHours(-48), secondCall.UpdatedBeforeUtc);
                Assert.Equal(continuationCursor, secondCall.AfterCursor);
            },
            thirdCall =>
            {
                Assert.Equal(nowUtc.AddMinutes(2).AddHours(-48), thirdCall.UpdatedBeforeUtc);
                Assert.Null(thirdCall.AfterCursor);
            });
    }

    [Fact]
    public async Task RunCycleAsync_TransferFailureRetainsSweepCutoffAndCursorForRetry()
    {
        var candidateRunId = Guid.NewGuid();
        var nowUtc = new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        var continuationCursor = new StrategyRunRetentionCursor(
            nowUtc.AddDays(-4),
            Guid.NewGuid());
        var page = new StrategyRunRetentionPreview(
            [candidateRunId],
            1,
            nowUtc.AddDays(-4),
            nowUtc.AddDays(-4),
            10,
            continuationCursor,
            false);
        var repository = new TestAppRepository
        {
            StrategyRunRetentionTransferFailuresToThrow = 1,
            StrategyRunRetentionTransferResult = new StrategyRunRetentionBatchResult(1, 1, 1, 1, 1)
        };
        repository.StrategyRunRetentionPreviews.Enqueue(page);
        repository.StrategyRunRetentionPreviews.Enqueue(page);
        var options = new StrategyRunRetentionOptions
        {
            Enabled = true,
            ApplyEnabled = true,
            RawRetentionHours = 48,
            CleanupBatchSize = 10,
            CleanupMaxBatchesPerCycle = 1
        };
        var worker = new StrategyRunRetentionWorker(
            NullLogger<StrategyRunRetentionWorker>.Instance,
            options,
            repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunCycleAsync(nowUtc));
        var retryResult = await worker.RunCycleAsync(nowUtc.AddMinutes(1));

        Assert.Equal(1, retryResult.PreviewedRows);
        Assert.Equal(1, retryResult.TransferredRows);
        Assert.Collection(
            repository.StrategyRunRetentionPreviewCalls,
            firstCall =>
            {
                Assert.Equal(nowUtc.AddHours(-48), firstCall.UpdatedBeforeUtc);
                Assert.Null(firstCall.AfterCursor);
            },
            secondCall =>
            {
                Assert.Equal(nowUtc.AddHours(-48), secondCall.UpdatedBeforeUtc);
                Assert.Null(secondCall.AfterCursor);
            });
        Assert.Collection(
            repository.StrategyRunRetentionTransferCalls,
            firstCall =>
            {
                Assert.Equal(new[] { candidateRunId }, firstCall.RunIds);
                Assert.Equal(nowUtc.AddHours(-48), firstCall.UpdatedBeforeUtc);
            },
            secondCall =>
            {
                Assert.Equal(new[] { candidateRunId }, secondCall.RunIds);
                Assert.Equal(nowUtc.AddHours(-48), secondCall.UpdatedBeforeUtc);
            });
    }

    [Fact]
    public void Configuration_DefaultsToDisabledAndRejectsUnsafeActivation()
    {
        var defaults = new StrategyRunRetentionOptions();

        Assert.False(defaults.Enabled);
        Assert.False(defaults.ApplyEnabled);
        Assert.False(defaults.DirectPaperSkipCompactionEnabled);
        Assert.False(defaults.DirectPaperSkipCompactionApplyEnabled);
        Assert.Equal(48, defaults.RawRetentionHours);

        var errors = AppOptionsValidator.Validate(new AppConfiguration
        {
            StrategyRunRetention = new StrategyRunRetentionOptions
            {
                Enabled = false,
                ApplyEnabled = true,
                DirectPaperSkipCompactionEnabled = false,
                DirectPaperSkipCompactionApplyEnabled = true,
                RawRetentionHours = 24,
                CleanupIntervalMinutes = 0,
                CleanupBatchSize = 25_001,
                CleanupMaxBatchesPerCycle = 101
            }
        });

        Assert.Contains(errors, error => error.Contains("ApplyEnabled requires", StringComparison.Ordinal));
        Assert.Contains(
            errors,
            error => error.Contains(
                "DirectPaperSkipCompactionApplyEnabled requires",
                StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("RawRetentionHours", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CleanupIntervalMinutes", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CleanupBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CleanupMaxBatchesPerCycle", StringComparison.Ordinal));
    }

    [Fact]
    public void Appsettings_EnablesDirectCompactionButLeavesBulkRetentionDisabled()
    {
        var appsettings = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "appsettings.json");
        using var document = JsonDocument.Parse(appsettings);
        var retention = document.RootElement.GetProperty("StrategyRunRetention");

        Assert.False(retention.GetProperty("Enabled").GetBoolean());
        Assert.False(retention.GetProperty("ApplyEnabled").GetBoolean());
        Assert.True(retention.GetProperty("DirectPaperSkipCompactionEnabled").GetBoolean());
        Assert.True(retention.GetProperty("DirectPaperSkipCompactionApplyEnabled").GetBoolean());
        Assert.True(retention.GetProperty("RawRetentionHours").GetInt32() >= 48);
    }

    [Fact]
    public void DirectCompactionSql_FailsClosedWhenDiagnosticsArePersisted()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.DirectSkipCompaction.cs");

        Assert.Contains(
            "AND run.skip_diagnostics_json IS NULL",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StrategyRunCompactionSql_FailsClosedWhenFeeShapeIsNotNeutral()
    {
        var directSource = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.DirectSkipCompaction.cs");
        var retentionSource = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.Retention.cs");

        var neutralPredicates = new[]
        {
            "fee_usd = 0",
            "fee_accounting_status = 'LegacyUnknown'",
            "fee_liquidity_role = 'Unknown'",
            "fee_calculation_source = ''",
            "fee_rate IS NULL",
            "fee_exponent IS NULL",
            "fee_taker_only IS NULL",
            "fee_calculated_at_utc IS NULL",
            "net_realized_pnl_usd IS NULL"
        };
        foreach (var predicate in neutralPredicates)
        {
            Assert.Equal(2, CountOccurrences(directSource, predicate));
            Assert.Equal(1, CountOccurrences(retentionSource, predicate));
        }
    }

    [Fact]
    public void DirectCompactionSql_ArchivesBeforeRawInsertAndUsesCanonicalProjectionPayload()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.DirectSkipCompaction.cs");

        Assert.Contains(
            "var archivedIds = await ArchiveNewDirectPaperSkippedRunsAsync(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public.dashboard_projection_run_payload(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "jsonb_populate_record(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "other_input.ordinality <> candidate.ordinality",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE existing_queue.priority < EXCLUDED.priority",
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(
                source,
                "WHERE existing_queue.priority < EXCLUDED.priority"));
    }

    [Fact]
    public void Schema_InstallsRetentionDependencyLookupIndexesConcurrently()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
        var paperOrdersIndex = Assert.Single(statements, statement =>
            statement.StartsWith(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_orders_strategy_condition",
                StringComparison.Ordinal));
        var paperPositionsIndex = Assert.Single(statements, statement =>
            statement.StartsWith(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_positions_wallet_condition_ci",
                StringComparison.Ordinal));

        Assert.Equal(
            """
            CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_orders_strategy_condition
            ON paper_orders(strategy_id, condition_id);
            """,
            paperOrdersIndex.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(
            """
            CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_positions_wallet_condition_ci
            ON paper_positions(lower(copied_trader_wallet), condition_id);
            """,
            paperPositionsIndex.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Schema_InstallsDirectCompactionRecentLookupIndexesConcurrently()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
        var strategyUpdatedIndex = Assert.Single(statements, statement =>
            statement.StartsWith(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS " +
                "ix_strategy_market_paper_skip_tombstones_strategy_updated_run",
                StringComparison.Ordinal));
        var updatedStrategyIndex = Assert.Single(statements, statement =>
            statement.StartsWith(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS " +
                "ix_strategy_market_paper_skip_tombstones_updated_strategy_run",
                StringComparison.Ordinal));

        Assert.Equal(
            """
            CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_strategy_market_paper_skip_tombstones_strategy_updated_run
            ON strategy_market_paper_skip_tombstones(
                strategy_id, run_updated_at_utc, archived_run_id)
            WHERE archive_format_version = 1;
            """,
            strategyUpdatedIndex.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(
            """
            CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_strategy_market_paper_skip_tombstones_updated_strategy_run
            ON strategy_market_paper_skip_tombstones(
                run_updated_at_utc, strategy_id, archived_run_id)
            WHERE archive_format_version = 1;
            """,
            updatedStrategyIndex.Replace("\r\n", "\n", StringComparison.Ordinal));
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
    public void RetentionSql_IsFailClosedAndUsesGatedSerializableAllowlist()
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
        Assert.DoesNotContain("strategy_market_paper_run_retention_blockers(run)", repositorySource, StringComparison.Ordinal);
        Assert.Contains("AND run.skip_diagnostics_json IS NULL", repositorySource, StringComparison.Ordinal);
        Assert.Contains("candidate_batch AS MATERIALIZED", repositorySource, StringComparison.Ordinal);
        Assert.Contains("candidate_strategy_keys AS MATERIALIZED", repositorySource, StringComparison.Ordinal);
        Assert.Contains("strategy.live_enabled_at_utc", repositorySource, StringComparison.Ordinal);
        Assert.Contains("candidate_key.live_enabled_at_utc IS NOT NULL", repositorySource, StringComparison.Ordinal);
        Assert.Contains(
            "candidate_key.updated_at_utc >= candidate_key.live_enabled_at_utc",
            repositorySource,
            StringComparison.Ordinal);
        Assert.Contains("blocked_candidate_ids AS MATERIALIZED", repositorySource, StringComparison.Ordinal);
        Assert.Contains("LIMIT @CandidatePageSize", repositorySource, StringComparison.Ordinal);
        Assert.Contains("run.updated_at_utc > @AfterUpdatedAtUtc", repositorySource, StringComparison.Ordinal);
        Assert.Contains("run.id > @AfterRunId", repositorySource, StringComparison.Ordinal);
        Assert.Contains("blocked.id IS NULL AS is_eligible", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN public.paper_orders dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN public.dry_run_orders dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN public.live_orders dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN public.paper_live_shadow_decisions dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN public.paper_positions dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN public.paper_position_settlements dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN public.paper_copied_leader_positions dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN public.paper_copied_leader_activity_events dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN public.polymarket_onchain_paper_signal_results dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN public.strategy_market_paper_skip_tombstones dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN public.dashboard_projection_events dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("FROM public.dashboard_strategy_recent_projection_facts dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN public.dashboard_projection_reconciliation_queue dependency", repositorySource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN LATERAL", repositorySource, StringComparison.Ordinal);
        Assert.Contains("lower(dependency.copied_trader_wallet)", repositorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("dependency.size_shares", repositorySource, StringComparison.Ordinal);
        Assert.Contains(
            "DELETE FROM public.strategy_market_paper_runs run",
            repositorySource,
            StringComparison.Ordinal);
        Assert.Contains(
            "INSERT INTO public.strategy_paper_skip_rollups AS existing_rollup",
            repositorySource,
            StringComparison.Ordinal);
        Assert.Contains(
            "INSERT INTO public.strategy_market_paper_skip_tombstones",
            repositorySource,
            StringComparison.Ordinal);
        Assert.Contains(
            "INSERT INTO public.dashboard_projection_reconciliation_queue AS existing_queue",
            repositorySource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("FROM strategy_market_paper_runs run", repositorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("INTO strategy_paper_skip_rollups", repositorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("INTO strategy_market_paper_skip_tombstones", repositorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("INTO dashboard_projection_reconciliation_queue", repositorySource, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", repositorySource, StringComparison.Ordinal);
        Assert.Contains(
            "SELECT public.lock_strategy_run_retention_transfer();",
            repositorySource,
            StringComparison.Ordinal);
        Assert.Contains(
            "SELECT public.unlock_strategy_run_retention_transfer();",
            repositorySource,
            StringComparison.Ordinal);
        Assert.Contains("archive_format_version", repositorySource, StringComparison.Ordinal);
        Assert.Contains("candidate.condition_id", repositorySource, StringComparison.Ordinal);
        Assert.Contains("candidate.rollup_bucket_start_utc", repositorySource, StringComparison.Ordinal);
        Assert.Contains("SELECT DISTINCT candidate.strategy_id", repositorySource, StringComparison.Ordinal);
        Assert.Contains("run.id = ANY(@RunIds)", repositorySource, StringComparison.Ordinal);
        Assert.Contains("result.SelectedRows != normalizedRunIds.Length", repositorySource, StringComparison.Ordinal);
        Assert.Contains("count(*)::bigint AS total_candidate_rows", repositorySource, StringComparison.Ordinal);

        Assert.Contains("signal_id", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("live_skip_projection_dependency", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("strategy.live_enabled_at_utc IS NOT NULL", projectionSchema, StringComparison.Ordinal);
        Assert.Contains(
            "(target_run).updated_at_utc >= strategy.live_enabled_at_utc",
            projectionSchema,
            StringComparison.Ordinal);
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
        Assert.Contains("lock_strategy_run_retention_dependency", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("restore_archived_strategy_runs_for_dependency", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("archive_format_version IS DISTINCT FROM 1", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("possible legacy/incomplete tombstone", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("FOR target_strategy_id IN", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("restore_strategy_runs_after_strategy_code_update", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("lock_strategy_run_dependency_mapping_mutation", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("late_dependency_run_restore", projectionSchema, StringComparison.Ordinal);
        Assert.Contains("suppressed_run_projection_ids", projectionSchema, StringComparison.Ordinal);
        Assert.Equal(
            9,
            CountOccurrences(
                projectionSchema,
                "EXECUTE FUNCTION public.restore_strategy_runs_after_dependency_write();"));
    }

    [Fact]
    public void DashboardBuild_UsesFreshV1TombstonesOnlyForRecentPaperSkipFacts()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresDashboardProjectionRepository.Build.cs");
        const string recentMethodName = "async Task AccumulateRecentStrategyPaperSkipTombstonesAsync()";
        var recentStart = source.IndexOf(recentMethodName, StringComparison.Ordinal);
        Assert.True(recentStart >= 0);
        var recentEnd = source.IndexOf(
            "async Task AccumulateLiveOrdersAsync()",
            recentStart,
            StringComparison.Ordinal);

        Assert.True(recentEnd > recentStart);
        var recentSource = source[recentStart..recentEnd];
        Assert.Contains(
            "FROM strategy_market_paper_skip_tombstones tombstone",
            recentSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "tombstone.archive_format_version = 1",
            recentSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "tombstone.run_updated_at_utc >= @RecentCutoffUtc",
            recentSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "tombstone.run_updated_at_utc <= @NowUtc",
            recentSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "AND tombstone.strategy_id = @StrategyId",
            recentSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "DashboardProjectionCalculator.GetRecentFacts(payload)",
            recentSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetLifetimeContribution", recentSource, StringComparison.Ordinal);
        Assert.Contains(
            "await AccumulateStrategyPaperSkipRollupsAsync();",
            source,
            StringComparison.Ordinal);
        Assert.Equal(4, DashboardProjectionVersions.Current);
    }

    [Fact]
    public void DirectRecentPerformance_IncludesFreshV1PaperSkipTombstonesWithoutLiveCounts()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.cs");
        var methodStart = source.IndexOf(
            "GetStrategyRecentPerformanceAsync",
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodEnd = source.IndexOf(
            "GetStrategySettledPnlByLookbackHoursAsync",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart);

        var method = source[methodStart..methodEnd];
        Assert.Contains("archived_skip_window_rows AS", method, StringComparison.Ordinal);
        Assert.Contains(
            "FROM strategy_market_paper_skip_tombstones tombstone",
            method,
            StringComparison.Ordinal);
        Assert.Contains(
            "tombstone.archive_format_version = 1",
            method,
            StringComparison.Ordinal);
        Assert.Contains(
            "tombstone.run_updated_at_utc >= window_row.window_start_utc",
            method,
            StringComparison.Ordinal);
        var archivedRowsStart = method.IndexOf(
            "archived_skip_window_rows AS",
            StringComparison.Ordinal);
        Assert.True(archivedRowsStart >= 0);
        var archivedRowsEnd = method.IndexOf(
            "run_window_rows AS",
            archivedRowsStart,
            StringComparison.Ordinal);
        Assert.True(archivedRowsEnd > archivedRowsStart);
        var archivedRows = method[archivedRowsStart..archivedRowsEnd];
        const string overallLowerBound =
            "tombstone.run_updated_at_utc >= CAST(@NowUtc AS timestamptz) - interval '24 hours'";
        const string overallUpperBound =
            "tombstone.run_updated_at_utc <= CAST(@NowUtc AS timestamptz)";
        Assert.Equal(1, CountOccurrences(archivedRows, overallLowerBound));
        Assert.Equal(1, CountOccurrences(archivedRows, overallUpperBound));
        Assert.Contains(
            "NULL::timestamptz AS live_enabled_at_utc",
            method,
            StringComparison.Ordinal);
        Assert.Contains("UNION ALL", method, StringComparison.Ordinal);
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
        Assert.Contains("archive_format_version smallint NULL", schema, StringComparison.Ordinal);
        Assert.Contains("rollup_bucket_start_utc timestamptz NULL", schema, StringComparison.Ordinal);
        Assert.Contains("ux_strategy_market_paper_skip_tombstones_run", schema, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_skip_tombstones_incomplete", schema, StringComparison.Ordinal);
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
