using Microsoft.Data.Sqlite;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class StorageTests
{
    [Fact]
    public async Task SchemaInitializer_CreatesRequiredTables()
    {
        var databasePath = CreateTempDatabasePath();
        var factory = new SqliteConnectionFactory(new StorageOptions { DatabasePath = databasePath });
        var initializer = new SqliteSchemaInitializer(factory);

        await initializer.InitializeAsync();

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();

        var tableNames = await GetTableNamesAsync(connection);
        Assert.Contains("LeaderTrades", tableNames);
        Assert.Contains("Signals", tableNames);
        Assert.Contains("PaperOrders", tableNames);
        Assert.Contains("ServiceHeartbeats", tableNames);
        Assert.Contains("ApiErrors", tableNames);
    }

    [Fact]
    public async Task Repository_UpsertsServiceHeartbeat()
    {
        var databasePath = CreateTempDatabasePath();
        var factory = new SqliteConnectionFactory(new StorageOptions { DatabasePath = databasePath });
        await new SqliteSchemaInitializer(factory).InitializeAsync();
        var repository = new SqliteAppRepository(factory);

        var heartbeat = new ServiceHeartbeat(
            "PolyCopyTrader.Service",
            "Running",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            "1.0.0",
            BotMode.ReadOnly,
            "Test",
            null);

        await repository.UpsertServiceHeartbeatAsync(heartbeat);
        var heartbeats = await repository.GetServiceHeartbeatsAsync();

        var stored = Assert.Single(heartbeats);
        Assert.Equal("PolyCopyTrader.Service", stored.ServiceName);
        Assert.Equal(BotMode.ReadOnly, stored.Mode);
    }

    private static string CreateTempDatabasePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PolyCopyTrader.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "test.db");
    }

    private static async Task<HashSet<string>> GetTableNamesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        await using var reader = await command.ExecuteReaderAsync();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
