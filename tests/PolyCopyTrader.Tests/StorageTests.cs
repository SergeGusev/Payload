using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class StorageTests
{
    [Fact]
    public void PostgresSchema_ContainsRequiredTables()
    {
        foreach (var table in PostgresSchema.RequiredTables)
        {
            Assert.Contains($"CREATE TABLE IF NOT EXISTS {table}", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        }

        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_leader_trades_dedup", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS ix_polymarket_http_logs_requested", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_positions", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_onchain_position_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_performance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_onchain_wallet_performance_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionFactory_RequiresConfiguredConnectionString()
    {
        var options = new StorageOptions
        {
            ConnectionString = string.Empty,
            ConnectionStringEnvironmentVariable = "POLYCOPYTRADER_TEST_MISSING_CONNECTION"
        };

        Assert.Throws<InvalidOperationException>(() => new PostgresConnectionFactory(options));
    }

    [Fact]
    public async Task NoOpRepository_IsSafeWhenDatabaseIsNotConfigured()
    {
        var repository = new NoOpAppRepository();
        var heartbeat = new ServiceHeartbeat(
            "PolyCopyTrader.Service",
            "Running",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "1.0.0",
            BotMode.ReadOnly,
            "Test",
            null);

        await repository.UpsertServiceHeartbeatAsync(heartbeat);
        await repository.AddPolymarketHttpLogAsync(new PolymarketHttpLogEntry(
            Guid.NewGuid(),
            "PolymarketDataApiClient",
            "GetUserTrades",
            "GET",
            "https://data-api.polymarket.com/trades",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            10,
            1,
            200,
            true,
            "{}",
            null));
        var heartbeats = await repository.GetServiceHeartbeatsAsync();
        var httpLogs = await repository.GetRecentPolymarketHttpLogsAsync();

        Assert.Empty(heartbeats);
        Assert.Empty(httpLogs);
    }

    [Fact]
    public async Task PostgresRepository_InitializesSchema_WhenTestConnectionIsConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new StorageOptions { ConnectionString = connectionString };
        var factory = new PostgresConnectionFactory(options);
        var initializer = new PostgresSchemaInitializer(factory);
        await initializer.InitializeAsync();

        var repository = new PostgresAppRepository(factory);
        var heartbeat = new ServiceHeartbeat(
            "PolyCopyTrader.Tests",
            "Running",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "1.0.0",
            BotMode.ReadOnly,
            "IntegrationTest",
            null);

        await repository.UpsertServiceHeartbeatAsync(heartbeat);
        var httpLog = new PolymarketHttpLogEntry(
            Guid.NewGuid(),
            "PolymarketDataApiClient",
            "GetTraderLeaderboard",
            "GET",
            "https://data-api.polymarket.com/v1/leaderboard",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            25,
            1,
            200,
            true,
            "[]",
            null);
        await repository.AddPolymarketHttpLogAsync(httpLog);

        var heartbeats = await repository.GetServiceHeartbeatsAsync();
        var httpLogs = await repository.GetRecentPolymarketHttpLogsAsync();

        Assert.Contains(heartbeats, item => item.ServiceName == "PolyCopyTrader.Tests");
        Assert.Contains(httpLogs, item => item.Id == httpLog.Id);
    }
}
