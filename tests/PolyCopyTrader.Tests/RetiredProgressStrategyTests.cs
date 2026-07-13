using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class RetiredProgressStrategyTests
{
    [Fact]
    public void StrategyIds_ExcludeExactHopelessProgressAllowlist()
    {
        var retiredCodes = GetRetiredCodes();

        Assert.Equal(57, retiredCodes.Count);
        Assert.All(retiredCodes, code => Assert.Null(StrategyIds.TryGetStrategyIdByCode(code)));

        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_24_child_progress"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_3_diff_shift_progress_premarket"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("eth_up_down_5m_diff_3_up_progress"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("eth_up_down_5m_3_diff_shift_progress_premarket"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("eth_up_down_5m_10_child_progress_roi"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("sol_up_down_5m_4_diff_limit_progress_premarket"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("sol_up_down_5m_7_child_progress_roi"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("sol_up_down_5m_8_child_progress_roi"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("sol_up_down_5m_16_child_progress_roi"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("sol_up_down_5m_24_child_progress_roi"));
    }

    [Fact]
    public void PostgresSchema_ExcludesRetiredProgressSeedsAndContainsExactCleanupMigration()
    {
        Assert.Contains("20260713_remove_hopeless_progress_strategies", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("allowlist=57", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("mode_code = 'child_progress'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("mode_code = 'child_progress_roi'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("threshold_value IN (1, 2, 13, 14, 15, 16)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("threshold_value IN (1, 2, 4, 5)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("WHERE asset_symbol <> 'BTC'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("Refusing hopeless Progress cleanup because a strategy id/code collision was found.", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("Refusing hopeless Progress cleanup because % active Live orders still exist.", PostgresSchema.SchemaSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostgresSchema_InitializesWithoutRetiredProgressRows()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
SELECT
    (SELECT count(*)::integer FROM strategies WHERE code = ANY(@RetiredCodes)) AS retired_count,
    (SELECT count(*)::integer FROM strategies WHERE code = ANY(@WatchCodes)) AS watch_count,
    (SELECT count(*)::integer FROM schema_data_migrations WHERE migration_key = '20260713_remove_hopeless_progress_strategies') AS migration_count,
    (SELECT details FROM schema_data_migrations WHERE migration_key = '20260713_remove_hopeless_progress_strategies') AS migration_details;
""", connection);
        command.Parameters.AddWithValue("RetiredCodes", GetRetiredCodes().ToArray());
        command.Parameters.AddWithValue("WatchCodes", new[]
        {
            "btc_up_down_5m_24_child_progress",
            "btc_up_down_5m_3_diff_shift_progress_premarket",
            "eth_up_down_5m_diff_3_up_progress",
            "eth_up_down_5m_3_diff_shift_progress_premarket",
            "eth_up_down_5m_10_child_progress_roi",
            "sol_up_down_5m_4_diff_limit_progress_premarket",
            "sol_up_down_5m_7_child_progress_roi",
            "sol_up_down_5m_8_child_progress_roi",
            "sol_up_down_5m_16_child_progress_roi",
            "sol_up_down_5m_24_child_progress_roi"
        });

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(10, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Contains("allowlist=57;target_strategies=0", reader.GetString(3), StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetRetiredCodes()
    {
        var codes = new List<string>();
        codes.AddRange(Enumerable.Range(1, 5).Select(value =>
            $"btc_up_down_5m_{value}_diff_limit_progress_premarket"));
        codes.AddRange(new[] { 1, 2, 4, 5 }.Select(value =>
            $"btc_up_down_5m_{value}_diff_shift_progress_premarket"));
        codes.AddRange(new[] { 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 13, 14, 19, 21, 24 }.Select(value =>
            $"eth_up_down_5m_{value}_child_progress"));
        codes.AddRange(new[] { 3, 5, 7, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24 }.Select(value =>
            $"eth_up_down_5m_{value}_child_progress_roi"));
        codes.Add("eth_up_down_5m_4_diff_shift_progress_premarket");
        codes.AddRange(new[] { 1, 2, 13, 14, 15, 16 }.Select(value =>
            $"eth_up_down_5m_diff_{value}_up_progress"));
        codes.AddRange(new[] { 4, 5, 6, 13, 14, 19, 21, 23 }.Select(value =>
            $"sol_up_down_5m_{value}_child_progress_roi"));
        return codes;
    }
}
