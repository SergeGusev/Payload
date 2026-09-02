using System.Net;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class CryptoReferencePriceRetentionPostgresIntegrationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Cutoff = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    private readonly List<Guid> insertedIds = [];
    private PostgresConnectionFactory factory = null!;
    private PostgresAppRepository repository = null!;

    public async Task InitializeAsync()
    {
        Assert.Null(DisposablePostgresIntegrationGuard.GetConfiguredConnectionValidationError());
        factory = new PostgresConnectionFactory(new StorageOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION")!
        }, "PolyCopyTrader.Tests.ReferencePriceRetention");
        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT inet_server_addr(),current_database(),current_setting('data_directory');", connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(IPAddress.IsLoopback(reader.GetFieldValue<IPAddress>(0)));
            Assert.Equal(new NpgsqlConnectionStringBuilder(factory.ConnectionString).Database, reader.GetString(1));
            var dataPath = Path.GetFullPath(reader.GetString(2));
            Assert.StartsWith(Path.GetFullPath(@"D:\CodexTemp\runs\"), dataPath, StringComparison.OrdinalIgnoreCase);
            var runPath = Directory.GetParent(dataPath)!.Parent!.FullName;
            using var marker = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(runPath, ".codex-ephemeral.json")));
            Assert.Equal("OpenAI Codex", marker.RootElement.GetProperty("owner").GetString());
            Assert.Equal("ephemeral-session", marker.RootElement.GetProperty("kind").GetString());
            Assert.Equal(runPath, Path.GetFullPath(marker.RootElement.GetProperty("runPath").GetString()!));
        }

        await new PostgresSchemaInitializer(factory).InitializeAsync();
        repository = new PostgresAppRepository(factory);
        Assert.Empty(await ReadRowsAsync());
    }

    public async Task DisposeAsync()
    {
        if (factory is null || insertedIds.Count == 0)
        {
            return;
        }

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM public.crypto_reference_price_ticks WHERE id = ANY(@Ids);", connection);
        command.Parameters.Add("Ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = insertedIds.ToArray();
        await command.ExecuteNonQueryAsync();
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Cleanup_OnlyExactAssetsAndStrictlyOlderRows_PreservesEveryOtherField()
    {
        var oldIds = new Dictionary<string, Guid>();
        foreach (var asset in new[] { "BTC", "ETH", "SOL" })
        {
            oldIds[asset] = await InsertAsync(asset, Cutoff.AddTicks(-10));
            await InsertAsync(asset, Cutoff);
            await InsertAsync(asset, Cutoff.AddHours(1));
            await InsertAsync(asset, Cutoff.AddDays(10));
        }

        foreach (var asset in new[] { "btc", "Btc", "BTC ", "BTCUSDT", "XRP" })
        {
            await InsertAsync(asset, Cutoff.AddDays(-10));
        }

        var before = await ReadRowsAsync();
        Assert.Equal(17, before.Count);
        var deleted = new List<Guid>();
        foreach (var (asset, id) in oldIds)
        {
            Assert.Equal(1, await repository.CleanupCryptoReferencePriceTicksAsync(
                asset, Cutoff.ToOffset(TimeSpan.FromHours(3)), 1000));
            deleted.Add(id);
            AssertOnlyDeleted(before, await ReadRowsAsync(), deleted);
            Assert.Equal(0, await repository.CleanupCryptoReferencePriceTicksAsync(asset, Cutoff, 1000));
        }

        foreach (var asset in new[] { "btc", "Btc", "BTC ", "BTCUSDT", "XRP", "" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.CleanupCryptoReferencePriceTicksAsync(asset, Cutoff, 1000));
        }

        AssertOnlyDeleted(before, await ReadRowsAsync(), deleted);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Cleanup_CapsAt1000_OrdersBySampleAndId_ResumesFromCommittedRows()
    {
        var ids = new List<Guid>();
        for (var index = 1; index <= 1002; index++)
        {
            var id = Guid.Parse($"00000000-0000-0000-0000-{index:D12}");
            var bucket = Cutoff.AddSeconds(-2000 + index);
            ids.Add(await InsertAsync("BTC", index == 2 ? bucket.AddSeconds(-1) : bucket, id, bucket));
        }

        await InsertAsync("BTC", Cutoff);
        await InsertAsync("ETH", Cutoff.AddDays(-1));
        var before = await ReadRowsAsync();
        Assert.Equal(1004, before.Count);
        Assert.Equal(1, await repository.CleanupCryptoReferencePriceTicksAsync("BTC", Cutoff, 1));
        AssertOnlyDeleted(before, await ReadRowsAsync(), ids.Take(1));
        Assert.Equal(1000, await repository.CleanupCryptoReferencePriceTicksAsync("BTC", Cutoff, 2000));
        AssertOnlyDeleted(before, await ReadRowsAsync(), ids.Take(1001));

        var restarted = new PostgresAppRepository(factory);
        Assert.Equal(1, await restarted.CleanupCryptoReferencePriceTicksAsync("BTC", Cutoff, 1000));
        Assert.Equal(0, await restarted.CleanupCryptoReferencePriceTicksAsync("BTC", Cutoff, 1000));
        AssertOnlyDeleted(before, await ReadRowsAsync(), ids);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Cleanup_SkipsLockedRow_ThenRemovesItAfterUnlock()
    {
        var lockedId = await InsertAsync("SOL", Cutoff.AddHours(-2));
        var availableId = await InsertAsync("SOL", Cutoff.AddHours(-1));
        await InsertAsync("SOL", Cutoff);
        var before = await ReadRowsAsync();
        await using var blocker = factory.CreateConnection();
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand(
            "SELECT id FROM public.crypto_reference_price_ticks WHERE id=@Id FOR UPDATE;", blocker, transaction))
        {
            command.Parameters.AddWithValue("Id", lockedId);
            Assert.Equal(lockedId, await command.ExecuteScalarAsync());
        }

        Assert.Equal(1, await repository.CleanupCryptoReferencePriceTicksAsync("SOL", Cutoff, 1000));
        AssertOnlyDeleted(before, await ReadRowsAsync(), [availableId]);
        await transaction.RollbackAsync();
        Assert.Equal(1, await repository.CleanupCryptoReferencePriceTicksAsync("SOL", Cutoff, 1000));
        AssertOnlyDeleted(before, await ReadRowsAsync(), [availableId, lockedId]);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Cleanup_LockTimeoutRollsBackBatch_AndRetryIsSafe()
    {
        var id = await InsertAsync("ETH", Cutoff.AddHours(-1));
        await InsertAsync("ETH", Cutoff);
        var before = await ReadRowsAsync();
        await using var blocker = factory.CreateConnection();
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand(
            "LOCK TABLE public.crypto_reference_price_ticks IN SHARE MODE;", blocker, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            repository.CleanupCryptoReferencePriceTicksAsync("ETH", Cutoff, 1000));
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
        AssertOnlyDeleted(before, await ReadRowsAsync(), []);
        await transaction.RollbackAsync();
        Assert.Equal(1, await repository.CleanupCryptoReferencePriceTicksAsync("ETH", Cutoff, 1000));
        AssertOnlyDeleted(before, await ReadRowsAsync(), [id]);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Cleanup_CancellationWhileBlocked_RollsBackAndPreservesRows()
    {
        await InsertAsync("BTC", Cutoff.AddHours(-1));
        var before = await ReadRowsAsync();
        await using var blocker = factory.CreateConnection();
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand(
            "LOCK TABLE public.crypto_reference_price_ticks IN SHARE MODE;", blocker, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.CleanupCryptoReferencePriceTicksAsync("BTC", Cutoff, 1000, cancellation.Token));
        AssertOnlyDeleted(before, await ReadRowsAsync(), []);
        await transaction.RollbackAsync();
        Assert.Equal(1, await repository.CleanupCryptoReferencePriceTicksAsync("BTC", Cutoff, 1000));
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Cleanup_EnforcesTransactionLocalLimits_AndRollsBackOnDatabaseError()
    {
        await InsertAsync("BTC", Cutoff.AddHours(-1));
        var before = await ReadRowsAsync();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand("""
CREATE FUNCTION public.test_reference_retention_limits() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF current_setting('statement_timeout') <> '5s' OR current_setting('lock_timeout') <> '250ms' THEN
        RAISE EXCEPTION 'retention limits missing';
    END IF;
    RAISE EXCEPTION 'retention limits verified';
END;
$$;
CREATE TRIGGER test_reference_retention_limits BEFORE DELETE ON public.crypto_reference_price_ticks
FOR EACH ROW EXECUTE FUNCTION public.test_reference_retention_limits();
""", connection))
        {
            await setup.ExecuteNonQueryAsync();
        }

        try
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                repository.CleanupCryptoReferencePriceTicksAsync("BTC", Cutoff, 1000));
            Assert.Equal("retention limits verified", error.MessageText);
            AssertOnlyDeleted(before, await ReadRowsAsync(), []);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
DROP TRIGGER test_reference_retention_limits ON public.crypto_reference_price_ticks;
DROP FUNCTION public.test_reference_retention_limits();
""", connection);
            await cleanup.ExecuteNonQueryAsync();
        }

        await using var pooled = factory.CreateConnection();
        await pooled.OpenAsync();
        await using var limits = new NpgsqlCommand("SELECT current_setting('statement_timeout'),current_setting('lock_timeout');", pooled);
        await using var reader = await limits.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("0", reader.GetString(0));
        Assert.Equal("0", reader.GetString(1));
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Cleanup_NonpositiveBatchSize_DoesNotDelete()
    {
        await InsertAsync("BTC", Cutoff.AddDays(-1));
        var before = await ReadRowsAsync();
        Assert.Equal(0, await repository.CleanupCryptoReferencePriceTicksAsync("BTC", Cutoff, 0));
        Assert.Equal(0, await repository.CleanupCryptoReferencePriceTicksAsync("BTC", Cutoff, -1));
        AssertOnlyDeleted(before, await ReadRowsAsync(), []);
    }

    private async Task<Guid> InsertAsync(string asset, DateTimeOffset sampledAt, Guid? id = null, DateTimeOffset? bucket = null)
    {
        var rowId = id ?? Guid.NewGuid();
        insertedIds.Add(rowId);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
INSERT INTO public.crypto_reference_price_ticks
(id,asset_symbol,binance_symbol,sampled_at_utc,bucket_start_utc,price_usd,
 source_updated_at_utc,fetched_at_utc,source,created_at_utc)
VALUES (@Id,@Asset,'fixture',@Sampled,@Bucket,123.45678901,@Sampled,@Sampled,'retention-test',@Sampled);
""", connection);
        command.Parameters.AddWithValue("Id", rowId);
        command.Parameters.AddWithValue("Asset", asset);
        command.Parameters.AddWithValue("Sampled", sampledAt.UtcDateTime);
        command.Parameters.AddWithValue("Bucket", (bucket ?? sampledAt).UtcDateTime);
        await command.ExecuteNonQueryAsync();
        return rowId;
    }

    private async Task<Dictionary<Guid, string>> ReadRowsAsync()
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id,to_jsonb(t)::text FROM public.crypto_reference_price_ticks t;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new Dictionary<Guid, string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetGuid(0), reader.GetString(1));
        }

        return rows;
    }

    private static void AssertOnlyDeleted(
        Dictionary<Guid, string> before, Dictionary<Guid, string> after, IEnumerable<Guid> deletedIds)
    {
        var deleted = deletedIds.ToHashSet();
        Assert.All(deleted, id => Assert.Contains(id, before.Keys));
        Assert.Equal(before.Where(pair => !deleted.Contains(pair.Key)).OrderBy(pair => pair.Key).ToArray(),
            after.OrderBy(pair => pair.Key).ToArray());
    }
}
