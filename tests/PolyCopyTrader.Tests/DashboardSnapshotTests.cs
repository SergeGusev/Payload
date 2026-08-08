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

        var end = source.IndexOf(
            "async Task AccumulateRecentStrategyPaperSkipTombstonesAsync()",
            start,
            StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("FROM strategy_paper_skip_rollups rollup", method, StringComparison.Ordinal);
        Assert.Contains("StrategyPaperSkipRollupProjectionPayload", method, StringComparison.Ordinal);
        Assert.Contains("GetLifetimeContribution(payload)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AddFactsAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRecentFacts", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardProjectionExpiry_UsesDisjointPartialIndexBranches()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresDashboardProjectionRepository.Expiry.cs");

        Assert.Contains("WITH due_1h AS MATERIALIZED", source, StringComparison.Ordinal);
        Assert.Contains("due_6h AS MATERIALIZED", source, StringComparison.Ordinal);
        Assert.Contains("due_24h AS MATERIALIZED", source, StringComparison.Ordinal);
        Assert.Contains("WHERE applied_1h", source, StringComparison.Ordinal);
        Assert.Matches(@"WHERE applied_6h\s+AND NOT applied_1h", source);
        Assert.Matches(
            @"WHERE applied_24h\s+AND NOT applied_1h\s+AND NOT applied_6h",
            source);
        Assert.Equal(3, source.Split("FOR UPDATE SKIP LOCKED").Length - 1);
        Assert.DoesNotContain(
            "WHERE (applied_1h AND occurred_at_utc",
            source,
            StringComparison.Ordinal);
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
        var botWorker = ReadRepositorySource("src", "PolyCopyTrader.Service", "BotWorker.cs");
        var telemetryState = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "Analytics",
            "DatabaseScanTelemetryState.cs");
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
        Assert.Contains("AddSingleton<DatabaseScanTelemetryState>", program, StringComparison.Ordinal);
        Assert.Contains("RecordDashboardReconciliation", projectionWorker, StringComparison.Ordinal);
        Assert.Contains("RecordDashboardReconciliation", reconciliationWorker, StringComparison.Ordinal);
        Assert.Contains("PaperPositionsBuildSequentialScans", projectionWorker, StringComparison.Ordinal);
        Assert.Contains("PaperPositionsBuildSequentialTuplesRead", projectionWorker, StringComparison.Ordinal);
        Assert.Contains("PaperPositionsBuildSequentialScans", reconciliationWorker, StringComparison.Ordinal);
        Assert.Contains("PaperPositionsBuildSequentialTuplesRead", reconciliationWorker, StringComparison.Ordinal);
        Assert.Contains("GetHeartbeatSummary", botWorker, StringComparison.Ordinal);
        Assert.Contains("DBScanTelemetry", telemetryState, StringComparison.Ordinal);
        Assert.Contains("SET LOCAL max_parallel_workers_per_gather = 0", reconciliationRepository, StringComparison.Ordinal);
        Assert.Contains("SET LOCAL work_mem = '4MB'", reconciliationRepository, StringComparison.Ordinal);
        Assert.Contains("SET LOCAL statement_timeout = '15s'", reconciliationRepository, StringComparison.Ordinal);
        var statsBeforeBuild = reconciliationRepository.IndexOf(
            "paperPositionsStatsBeforeBuild = await PostgresPaperPositionsScanTelemetry.ReadAsync",
            StringComparison.Ordinal);
        var buildProjection = reconciliationRepository.IndexOf(
            "var projection = await BuildProjectionAsync",
            StringComparison.Ordinal);
        var statsAfterBuild = reconciliationRepository.IndexOf(
            "paperPositionsStatsAfterBuild = await PostgresPaperPositionsScanTelemetry.ReadAsync",
            StringComparison.Ordinal);
        Assert.True(
            statsBeforeBuild >= 0
            && buildProjection > statsBeforeBuild
            && statsAfterBuild > buildProjection);
    }

    [Fact]
    public void DashboardStrategyPresentation_UsesNullableNetMetricsAndExplicitGrossAuditLabels()
    {
        var xaml = ReadRepositorySource("src", "PolyCopyTrader.Dashboard", "MainWindow.xaml");

        Assert.Contains("Header=\"Net realized\" Binding=\"{Binding NetRealizedPnlUsd}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Net closed ROI\" Binding=\"{Binding NetClosedRoiPct}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Net closed ROI\" Binding=\"{Binding NetRoiPct}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Net open\" Binding=\"{Binding NetUnrealizedPnlUsd}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Net MtM\" Binding=\"{Binding NetTotalPnlUsd}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Net MtM ROI\" Binding=\"{Binding NetRoiPct}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Net live realized\" Binding=\"{Binding LiveNetRealizedPnlUsd}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Net live ROI\" Binding=\"{Binding LiveNetRoiPct}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ClosedFeeCoverage}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding MarkToMarketFeeCoverage}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding LiveFeeCoverage}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Gross realized (audit)\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Gross stake (audit)\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Gross live stake (audit)\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Gross open unrealized (audit)\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Gross MtM PnL (audit)\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Gross avg win\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Gross avg loss\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Gross profit factor\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Gross expectancy\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Only positive net\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Big net ROI\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardStrategyFilters_RequireAvailableNetRoi()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Dashboard",
            "ViewModels",
            "MainViewModel.cs");

        Assert.Equal(
            2,
            source.Split("strategy.NetClosedRoiPct is { } netClosedRoiPct", StringSplitOptions.None).Length - 1);
        Assert.Equal(
            2,
            source.Split("strategy.NetRoiPct is { } netRoiPct", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("strategy.ClosedRoiPct >= 0m", source, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy.ClosedRoiPct > BigRoiThresholdPct", source, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy.RoiPct >= 0m", source, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy.RoiPct > BigRoiThresholdPct", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardDataService_MapsNetMetricsFeesAndCoverageCounts()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Dashboard",
            "Services",
            "DashboardDataService.cs");

        foreach (var property in new[]
        {
            "performance.NetRealizedPnlUsd",
            "performance.NetUnrealizedPnlUsd",
            "performance.NetTotalPnlUsd",
            "performance.NetRoiPct",
            "performance.NetClosedRoiPct",
            "performance.AccountedFeeUsd",
            "performance.FeeAccountedSettledCount",
            "performance.FeeRequiredSettledCount",
            "performance.FeeAccountedOpenPositionCount",
            "performance.FeeRequiredOpenPositionCount",
            "performance.LiveNetRealizedPnlUsd",
            "performance.LiveNetRoiPct",
            "performance.LiveAccountedFeeUsd",
            "performance.LiveFeeAccountedSettledCount",
            "performance.LiveFeeRequiredSettledCount"
        })
        {
            Assert.Contains(property, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DashboardCoverageDisplay_UsesAccountedOverRequiredAndEmptyScopeIsNotApplicable()
    {
        var rows = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Dashboard",
            "Models",
            "DashboardRows.cs");

        Assert.Contains("public string ClosedFeeCoverage", rows, StringComparison.Ordinal);
        Assert.Contains("public string MarkToMarketFeeCoverage", rows, StringComparison.Ordinal);
        Assert.Contains("public string LiveFeeCoverage", rows, StringComparison.Ordinal);
        Assert.Equal(
            2,
            rows.Split("accountedCount == 0 && requiredCount == 0", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, rows.Split("? \"N/A\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void DashboardStrategyCsv_PutsNullableNetFeeAndCoverageBeforeGrossAuditColumns()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Dashboard",
            "Services",
            "DashboardCsvExporter.cs");
        var lifetimeStart = source.IndexOf("Path.Combine(exportDirectory, \"Strategies.csv\")", StringComparison.Ordinal);
        var recentStart = source.IndexOf("Path.Combine(exportDirectory, \"StrategyRecentPerformance.csv\")", StringComparison.Ordinal);
        Assert.True(lifetimeStart >= 0);
        Assert.True(recentStart > lifetimeStart);

        var lifetime = source[lifetimeStart..recentStart];
        Assert.True(
            lifetime.IndexOf("\"NetRealizedPnlUsd\"", StringComparison.Ordinal) <
            lifetime.IndexOf("\"GrossRealizedPnlUsd\"", StringComparison.Ordinal));
        Assert.True(
            lifetime.IndexOf("\"LiveNetRealizedPnlUsd\"", StringComparison.Ordinal) <
            lifetime.IndexOf("\"GrossLiveRealizedPnlUsd\"", StringComparison.Ordinal));
        Assert.Contains("\"AccountedFeeUsd\"", lifetime, StringComparison.Ordinal);
        Assert.Contains("\"ClosedFeeCoverage\"", lifetime, StringComparison.Ordinal);
        Assert.Contains("\"MarkToMarketFeeCoverage\"", lifetime, StringComparison.Ordinal);
        Assert.Contains("\"LiveFeeCoverage\"", lifetime, StringComparison.Ordinal);

        var recent = source[recentStart..];
        Assert.True(
            recent.IndexOf("\"NetRealizedPnlUsd\"", StringComparison.Ordinal) <
            recent.IndexOf("\"GrossRealizedPnlUsd\"", StringComparison.Ordinal));
        Assert.True(
            recent.IndexOf("\"LiveNetRealizedPnlUsd\"", StringComparison.Ordinal) <
            recent.IndexOf("\"GrossLiveRealizedPnlUsd\"", StringComparison.Ordinal));
        Assert.Contains("FormatFeeCoverage", recent, StringComparison.Ordinal);
        Assert.Equal(string.Empty, PolyCopyTrader.Domain.CsvFormatter.FormatValue(null));
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
