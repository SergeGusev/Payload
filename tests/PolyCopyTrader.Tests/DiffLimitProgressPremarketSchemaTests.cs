using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class DiffLimitProgressPremarketSchemaTests
{
    [Fact]
    public void PostgresSchema_SeedsDiffLimitProgressPremarketStrategies()
    {
        Assert.Contains("('BTC', '8169')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8170')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8171')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains(
            "lower(asset_symbol) || '_up_down_5m_' || limit_value::text || '_diff_limit_progress_premarket'",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "asset_symbol || ' Up or Down 5m ' || limit_value::text || ' Diff Limit Progress Premarket'",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "each BUY FAK Paper entry uses multiplier min(abs(Diff), ' || limit_value::text || ')",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchema_SeedsDiffRealLimitProgressPremarketStrategies()
    {
        Assert.Contains("('BTC', '8172')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8173')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8174')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains(
            "lower(asset_symbol) || '_up_down_5m_' || limit_value::text || '_diff_real_limit_progress_premarket'",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "asset_symbol || ' Up or Down 5m ' || limit_value::text || ' Diff Real Limit Progress Premarket'",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "UpCount and DownCount stop changing when the next result would move Diff outside [-' || limit_value::text || ', ' || limit_value::text || ']",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
    }
}
