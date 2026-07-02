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
    public void PostgresDashboardSnapshotRepository_StrategyPerformanceReadIsFlatSelect()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Storage", "PostgresDashboardSnapshotRepository.cs");
        var start = source.IndexOf("private const string SelectStrategyPerformanceSnapshotSql", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("private const string UpsertStrategyPerformanceSnapshotSql", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var selectSql = source[start..end];
        Assert.Contains("FROM dashboard_strategy_performance_snapshots", selectSql, StringComparison.Ordinal);
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
        Assert.DoesNotContain("WITH ", selectSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paper_orders", selectSql, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy_market_paper_runs", selectSql, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM live_orders", selectSql, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepositorySource(params string[] segments)
    {
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
