using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class StrategyRunRetentionPostgresIntegrationTests
{
    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Transfer_CompactsOnlyPreviewedPaperSkipAndPreservesLifetimeTotals()
    {
        var factory = await TryCreateFactoryAsync();
        if (factory is null)
        {
            return;
        }

        var repository = new PostgresAppRepository(factory);
        var projection = new PostgresDashboardProjectionRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var run = CreateSkippedRun(strategyId, DateTimeOffset.UtcNow.AddDays(-4));
        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            Assert.Equal(StrategyRunRetentionScopes.PaperOnly, await ReadRetentionScopeAsync(factory, run.Id));
            await projection.BootstrapAsync();

            var before = (await repository.GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            var dashboardBefore = await ReadDashboardSnapshotAsync(snapshots, strategyId);
            var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 10);
            Assert.Contains(run.Id, preview.CandidateRunIds);

            var result = await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                cutoffUtc);

            Assert.Equal(1, result.SelectedRows);
            Assert.Equal(1, result.DeletedRows);
            Assert.Equal(1, result.RollupRowsChanged);
            Assert.Equal(1, result.TombstonesChanged);
            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal(0, counts.RawRuns);
            Assert.Equal(1, counts.RollupRuns);
            Assert.Equal(1, counts.Tombstones);
            Assert.Equal(0, counts.ProjectionEvents);

            var reconciliation = await projection.ReconcileNextStrategyAsync();
            Assert.True(reconciliation.Reconciled, reconciliation.Error);
            Assert.Equal(strategyId, reconciliation.StrategyId);

            var after = (await repository.GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            var dashboardAfter = await ReadDashboardSnapshotAsync(snapshots, strategyId);
            Assert.Equal(before.SkippedRunsCount, after.SkippedRunsCount);
            Assert.Equal(before.PaperConditionSkippedRunsCount, after.PaperConditionSkippedRunsCount);
            Assert.Equal(before.LastRunUtc, after.LastRunUtc);
            Assert.Equal(dashboardBefore, dashboardAfter);

            Assert.False(await repository.TryAddStrategyMarketPaperRunAsync(run with { Id = Guid.NewGuid() }));
            var bulkInserted = await repository.TryAddStrategyMarketPaperRunsAsync(
                [run with { Id = Guid.NewGuid() }]);
            Assert.Empty(bulkInserted);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Transfer_WhenAllowlistBecomesStale_RollsBackEntireBatch()
    {
        var factory = await TryCreateFactoryAsync();
        if (factory is null)
        {
            return;
        }

        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var firstRun = CreateSkippedRun(strategyId, oldUtc);
        var secondRun = CreateSkippedRun(strategyId, oldUtc.AddMinutes(1));
        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(firstRun));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(secondRun));
            await DeleteProjectionBlockersAsync(factory, strategyId);

            var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 10);
            Assert.Contains(firstRun.Id, preview.CandidateRunIds);
            Assert.Contains(secondRun.Id, preview.CandidateRunIds);
            await AddSkipDiagnosticsAsync(factory, secondRun.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                    [firstRun.Id, secondRun.Id],
                    cutoffUtc));

            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal(2, counts.RawRuns);
            Assert.Equal(0, counts.RollupRuns);
            Assert.Equal(0, counts.Tombstones);
            Assert.Equal(0, counts.ReconciliationQueueRows);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LiveGuard_MakesCurrentAndFutureRunsPermanentlyIneligible()
    {
        var factory = await TryCreateFactoryAsync();
        if (factory is null)
        {
            return;
        }

        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var liveRun = CreateSkippedRun(strategyId, oldUtc);
        var laterPaperRun = CreateSkippedRun(strategyId, oldUtc.AddMinutes(1));
        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: true);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(liveRun));
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow, await ReadRetentionScopeAsync(factory, liveRun.Id));

            await SetStrategyLiveStakesAsync(factory, strategyId, liveStakes: false);
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(laterPaperRun));
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow, await ReadRetentionScopeAsync(factory, laterPaperRun.Id));

            await TryDemoteRetentionScopeAsync(factory, liveRun.Id);
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow, await ReadRetentionScopeAsync(factory, liveRun.Id));
            await DeleteProjectionBlockersAsync(factory, strategyId);

            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(
                DateTimeOffset.UtcNow.AddHours(-48),
                10);
            Assert.DoesNotContain(liveRun.Id, preview.CandidateRunIds);
            Assert.DoesNotContain(laterPaperRun.Id, preview.CandidateRunIds);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    private static async Task<PostgresConnectionFactory?> TryCreateFactoryAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        return factory;
    }

    private static StrategyMarketPaperRun CreateSkippedRun(Guid strategyId, DateTimeOffset updatedAtUtc)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new StrategyMarketPaperRun(
            Guid.NewGuid(),
            strategyId,
            $"retention-market-{suffix}",
            $"retention-condition-{suffix}",
            $"retention-market-{suffix}",
            "Strategy run retention integration test",
            "Test",
            updatedAtUtc.AddMinutes(-5),
            updatedAtUtc,
            updatedAtUtc.AddMinutes(-10),
            updatedAtUtc.AddMinutes(-5),
            StrategyMarketPaperRunStatuses.Skipped,
            null,
            null,
            null,
            1m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "retention_test_skip",
            updatedAtUtc.AddMinutes(-10),
            updatedAtUtc);
    }

    private static async Task InsertStrategyAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        string strategyCode,
        bool liveStakes)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO strategies (
    id, code, name, description, enabled, live_stakes,
    live_enabled_at_utc, created_at_utc, updated_at_utc)
VALUES (
    @Id, @Code, @Name, 'retention integration test', true, @LiveStakes,
    CASE WHEN @LiveStakes THEN clock_timestamp() ELSE NULL END,
    clock_timestamp(), clock_timestamp());
""",
            connection);
        command.Parameters.AddWithValue("Id", strategyId);
        command.Parameters.AddWithValue("Code", strategyCode);
        command.Parameters.AddWithValue("Name", strategyCode);
        command.Parameters.AddWithValue("LiveStakes", liveStakes);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetStrategyLiveStakesAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        bool liveStakes)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE strategies
SET live_stakes = @LiveStakes,
    live_enabled_at_utc = CASE WHEN @LiveStakes THEN clock_timestamp() ELSE NULL END,
    updated_at_utc = clock_timestamp()
WHERE id = @Id;
""",
            connection);
        command.Parameters.AddWithValue("Id", strategyId);
        command.Parameters.AddWithValue("LiveStakes", liveStakes);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task TryDemoteRetentionScopeAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE strategy_market_paper_runs SET retention_scope = 'PaperOnly' WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AddSkipDiagnosticsAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE strategy_market_paper_runs SET skip_diagnostics_json = '{}'::jsonb WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadRetentionScopeAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT retention_scope FROM strategy_market_paper_runs WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Strategy run was not found."));
    }

    private static async Task DeleteProjectionBlockersAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_projection_events WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_recent_projection_facts WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_projection_reconciliation_queue WHERE strategy_id = @StrategyId;",
            strategyId);
    }

    private static async Task<RetentionCounts> ReadRetentionCountsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    (SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id = @StrategyId),
    (SELECT COALESCE(sum(run_count), 0) FROM strategy_paper_skip_rollups WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM strategy_market_paper_skip_tombstones WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM dashboard_projection_events WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM dashboard_projection_reconciliation_queue WHERE strategy_id = @StrategyId);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new RetentionCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
    }

    private static async Task<StrategyPerformance> ReadDashboardSnapshotAsync(
        PostgresDashboardSnapshotRepository snapshots,
        Guid strategyId)
    {
        return (await snapshots.GetStrategyPerformanceSnapshotAsync())
            .Single(row => row.StrategyId == strategyId);
    }

    private static async Task DeleteTestStrategyAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await DeleteProjectionBlockersAsync(factory, strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_lifetime_projection_states WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_recent_projection_states WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_performance_snapshots WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_recent_performance_snapshots WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategy_market_paper_runs WHERE strategy_id = @StrategyId;",
            strategyId);
        await DeleteProjectionBlockersAsync(factory, strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategy_paper_skip_rollups WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategy_market_paper_skip_tombstones WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategy_live_retention_guards WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategies WHERE id = @StrategyId;",
            strategyId);
        await DeleteProjectionBlockersAsync(factory, strategyId);
    }

    private static async Task ExecuteForStrategyAsync(
        PostgresConnectionFactory factory,
        string sql,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record RetentionCounts(
        long RawRuns,
        long RollupRuns,
        long Tombstones,
        long ProjectionEvents,
        long ReconciliationQueueRows);
}
