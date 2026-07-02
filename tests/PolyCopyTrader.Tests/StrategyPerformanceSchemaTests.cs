using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class StrategyPerformanceSchemaTests
{
    [Fact]
    public void PostgresSchema_AddsCountertrendSignalPerformanceIndex()
    {
        Assert.Contains(
            "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_orders_countertrend_signal_perf",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE raw_decision_json IS NOT NULL",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "raw_decision_json ? 'previous_score'",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "raw_decision_json ? 'previous_score_bps'",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
    }
}
