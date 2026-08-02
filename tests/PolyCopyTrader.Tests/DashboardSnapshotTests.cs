namespace PolyCopyTrader.Tests;

public sealed class DashboardSnapshotTests
{
    [Fact]
    public void PostgresSchema_ContainsStrategyPerformanceSnapshotTable()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Storage", "PostgresSchema.cs");

        Assert.Contains("\"dashboard_strategy_performance_snapshots\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "CREATE TABLE IF NOT EXISTS dashboard_strategy_performance_snapshots",
            source,
            StringComparison.Ordinal);
        Assert.Contains("strategy_id uuid PRIMARY KEY", source, StringComparison.Ordinal);
        Assert.Contains("refreshed_at_utc timestamptz NOT NULL", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchema_ContainsStrategyRecentPerformanceSnapshotTable()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Storage", "PostgresSchema.cs");

        Assert.Contains("\"dashboard_strategy_recent_performance_snapshots\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "CREATE TABLE IF NOT EXISTS dashboard_strategy_recent_performance_snapshots",
            source,
            StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (strategy_id, window_label)", source, StringComparison.Ordinal);
        Assert.Contains("refreshed_at_utc timestamptz NOT NULL", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardDataService_ReadsPrecomputedStrategyPerformanceSnapshot()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Dashboard", "Services", "DashboardDataService.cs");
        var start = source.IndexOf("private async Task<IReadOnlyList<StrategyPerformance>> GetStrategyPerformanceAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("private async Task<IReadOnlyList<StrategyRecentPerformance>>", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("dashboardSnapshots.GetStrategyPerformanceSnapshotAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("repository.GetStrategyPerformanceAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardDataService_ReadsPrecomputedStrategyRecentPerformanceSnapshot()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Dashboard", "Services", "DashboardDataService.cs");
        var start = source.IndexOf("private async Task<IReadOnlyList<StrategyRecentPerformance>> GetStrategyRecentPerformanceAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("private async Task<IReadOnlyList<T>> LoadOptionalReportAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("dashboardSnapshots.GetStrategyRecentPerformanceSnapshotAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("repository.GetStrategyRecentPerformanceAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardDataService_StrategyRecentPerformanceSnapshotFailuresAreNonFatal()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Dashboard", "Services", "DashboardDataService.cs");
        var start = source.IndexOf("private async Task<IReadOnlyList<StrategyRecentPerformance>> GetStrategyRecentPerformanceAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("private async Task<IReadOnlyList<T>> LoadOptionalReportAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("catch (Exception ex) when (!cancellationToken.IsCancellationRequested)", method, StringComparison.Ordinal);
        Assert.Contains("Recent strategy performance snapshot failed", method, StringComparison.Ordinal);
        Assert.Contains("AddStrategyRecentPerformanceWarning(diagnostics)", method, StringComparison.Ordinal);
        Assert.Contains("cachedStrategyRecentPerformance ?? []", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresDashboardSnapshotRepository_StrategyPerformanceReadIsFlatSelect()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Storage", "PostgresDashboardSnapshotRepository.cs");
        var start = source.IndexOf("private const string SelectStrategyPerformanceSnapshotSql", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("private const string UpsertStrategyPerformanceSnapshotSql", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var selectSql = source[start..end];
        Assert.Contains("FROM dashboard_strategy_performance_snapshots", selectSql, StringComparison.Ordinal);
        Assert.Contains("JOIN strategies AS strategy ON strategy.id = snapshot.strategy_id", selectSql, StringComparison.Ordinal);
        Assert.Contains("strategy.live_stakes", selectSql, StringComparison.Ordinal);
        Assert.Contains("strategy.enabled", selectSql, StringComparison.Ordinal);
        Assert.Contains("strategy.paused", selectSql, StringComparison.Ordinal);
        Assert.Contains("strategy.live_available_balance", selectSql, StringComparison.Ordinal);
        Assert.Contains("strategy.paused_until_utc > CURRENT_TIMESTAMP", selectSql, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH ", selectSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paper_orders", selectSql, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy_market_paper_runs", selectSql, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM live_orders", selectSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgresDashboardSnapshotRepository_StrategyRecentPerformanceReadIsFlatSelect()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Storage", "PostgresDashboardSnapshotRepository.cs");
        var start = source.IndexOf("private const string SelectStrategyRecentPerformanceSnapshotSql", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("private const string UpsertStrategyRecentPerformanceSnapshotSql", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var selectSql = source[start..end];
        Assert.Contains("FROM dashboard_strategy_recent_performance_snapshots", selectSql, StringComparison.Ordinal);
        Assert.Contains("JOIN strategies AS strategy ON strategy.id = snapshot.strategy_id", selectSql, StringComparison.Ordinal);
        Assert.Contains("strategy.live_stakes", selectSql, StringComparison.Ordinal);
        Assert.Contains("CURRENT_TIMESTAMP - (snapshot.window_hours * interval '1 hour')", selectSql, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH ", selectSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paper_orders", selectSql, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy_market_paper_runs", selectSql, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM live_orders", selectSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DashboardProjectionSchema_ContainsDurableOutboxAndSourceTriggers()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Storage", "DashboardProjectionSchema.cs");

        Assert.Contains("CREATE TABLE IF NOT EXISTS dashboard_projection_events", source, StringComparison.Ordinal);
        Assert.Contains("transaction_id xid8 NOT NULL", source, StringComparison.Ordinal);
        Assert.Contains("dashboard_strategy_lifetime_projection_states", source, StringComparison.Ordinal);
        Assert.Contains("dashboard_strategy_recent_projection_facts", source, StringComparison.Ordinal);
        Assert.Contains("dashboard_strategy_position_projection_facts", source, StringComparison.Ordinal);
        Assert.Contains("ux_dashboard_projection_events_paper_position", source, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (source_kind, source_id) WHERE source_kind = 'PaperPosition'", source, StringComparison.Ordinal);
        Assert.Contains("trg_dashboard_projection_paper_order", source, StringComparison.Ordinal);
        Assert.Contains("trg_dashboard_projection_strategy_run", source, StringComparison.Ordinal);
        Assert.Contains("trg_dashboard_projection_live_order", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardProjectionBuild_AddsPaperSkipRollupsOnlyToLifetimeState()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresDashboardProjectionRepository.Build.cs");
        var start = source.IndexOf(
            "async Task AccumulateStrategyPaperSkipRollupsAsync()",
            StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("async Task AccumulateLiveOrdersAsync()", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("FROM strategy_paper_skip_rollups rollup", method, StringComparison.Ordinal);
        Assert.Contains("StrategyPaperSkipRollupProjectionPayload", method, StringComparison.Ordinal);
        Assert.Contains("GetLifetimeContribution(payload)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AddFactsAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRecentFacts", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardProjectionWorkers_UseIncrementalAndIndependentReconciliationLoops()
    {
        var projectionWorker = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "Analytics",
            "DashboardStrategyPerformanceSnapshotWorker.cs");
        var reconciliationWorker = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "Analytics",
            "DashboardStrategyProjectionReconciliationWorker.cs");
        var program = ReadRepositorySource("src", "PolyCopyTrader.Service", "Program.cs");
        var reconciliationRepository = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresDashboardProjectionRepository.Reconciliation.cs");

        Assert.Contains("ApplyPendingEventsAsync", projectionWorker, StringComparison.Ordinal);
        Assert.Contains("options.ProjectionEventBatchSize", projectionWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("EventBatchSize = 2_000", projectionWorker, StringComparison.Ordinal);
        Assert.Contains("ExpireRecentFactsAsync", projectionWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("GetStrategyPerformanceAsync", projectionWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("GetStrategyRecentPerformanceAsync", projectionWorker, StringComparison.Ordinal);
        Assert.Contains("options.ProjectionReconciliationIntervalSeconds", reconciliationWorker, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(cadence, clock, stoppingToken)", reconciliationWorker, StringComparison.Ordinal);
        Assert.Contains("BatchSize=1", reconciliationWorker, StringComparison.Ordinal);
        Assert.Contains("ReconcileNextStrategyAsync", reconciliationWorker, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<DashboardStrategyProjectionReconciliationWorker>", program, StringComparison.Ordinal);
        Assert.Contains("SET LOCAL max_parallel_workers_per_gather = 0", reconciliationRepository, StringComparison.Ordinal);
        Assert.Contains("SET LOCAL work_mem = '4MB'", reconciliationRepository, StringComparison.Ordinal);
        Assert.Contains("SET LOCAL statement_timeout = '15s'", reconciliationRepository, StringComparison.Ordinal);
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
