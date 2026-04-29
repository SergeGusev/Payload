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
        var heartbeats = await repository.GetServiceHeartbeatsAsync();

        Assert.Empty(heartbeats);
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
        var heartbeats = await repository.GetServiceHeartbeatsAsync();

        Assert.Contains(heartbeats, item => item.ServiceName == "PolyCopyTrader.Tests");
    }
}
