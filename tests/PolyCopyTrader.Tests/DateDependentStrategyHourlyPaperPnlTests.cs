using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.Analytics;

namespace PolyCopyTrader.Tests;

public sealed class DateDependentStrategyHourlyPaperPnlTests
{
    [Fact]
    public void StrategyIds_DateDependentStrategiesContainOnlySolDown8ReferenceAveragePremarket()
    {
        var variant = Assert.Single(StrategyIds.DateDependentStrategyVariants);

        Assert.Equal(StrategyIds.SolUpDown5mDown8BpsReferenceAveragePremarketCode, variant.Code);
        Assert.Equal("SOL Up or Down 5m Down 8 bps Reference Average Premarket", variant.Name);
    }

    [Fact]
    public void PostgresSchema_ContainsDateDependentHourlyPaperPnlTable()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Storage", "PostgresSchema.cs");

        Assert.Contains("\"date_dependent_strategy_hourly_paper_pnl\"", source, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS date_dependent_strategy_hourly_paper_pnl", source, StringComparison.Ordinal);
        Assert.Contains("REFERENCES strategies(id) ON DELETE CASCADE", source, StringComparison.Ordinal);
        Assert.Contains("hour_utc integer NOT NULL CHECK (hour_utc >= 0 AND hour_utc <= 23)", source, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (strategy_id, hour_utc)", source, StringComparison.Ordinal);
        Assert.Contains("ix_date_dependent_strategy_hourly_paper_pnl_code_hour", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_RefreshesHourlyPaperPnlFromSettledRunsByUtcEntryHour()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Storage", "PostgresAppRepository.cs");
        var start = source.IndexOf("public async Task<int> RefreshDateDependentStrategyHourlyPaperPnlAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("public async Task<IReadOnlyList<StrategyRecentPerformance>>", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("generate_series(0, 23)::integer AS hour_utc", method, StringComparison.Ordinal);
        Assert.Contains("run.status = 'Settled'", method, StringComparison.Ordinal);
        Assert.Contains("EXTRACT(HOUR FROM run.entered_at_utc AT TIME ZONE 'UTC')", method, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO date_dependent_strategy_hourly_paper_pnl", method, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (strategy_id, hour_utc) DO UPDATE", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DateDependentStrategyHourlyPaperPnlWorker_SchedulesAfterNextUtcHour()
    {
        var delay = DateDependentStrategyHourlyPaperPnlWorker.GetDelayUntilNextHourlyRefresh(
            new DateTimeOffset(2026, 7, 4, 19, 22, 30, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromMinutes(38) + TimeSpan.FromSeconds(30), delay);
    }

    [Fact]
    public void Program_RegistersDateDependentHourlyPaperPnlWorker()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Service", "Program.cs");

        Assert.Contains("AddHostedService<DateDependentStrategyHourlyPaperPnlWorker>", source, StringComparison.Ordinal);
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
