using System.Net;
using System.Text.Json;
using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class LossDiffPositiveProgressPostgresTests : IAsyncLifetime
{
    private PostgresConnectionFactory factory = null!;
    private PostgresAppRepository repository = null!;
    private readonly List<Guid> runIds = [];
    private static Guid[] Children => Enumerable.Range(1, 16).Select(n => Guid.Parse($"b7c50005-0000-4000-8236-{n:000000000000}"))
        .Concat(Enumerable.Range(1, 18).Select(n => Guid.Parse($"b7c50005-0000-4000-8237-{n:000000000000}"))).ToArray();

    public async Task InitializeAsync()
    {
        Assert.Null(DisposablePostgresIntegrationGuard.GetConfiguredConnectionValidationError());
        factory = new PostgresConnectionFactory(new StorageOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION")!
        }, "PolyCopyTrader.Tests.PositiveProgress");
        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT inet_server_addr(),current_database(),current_setting('data_directory');", connection);
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
        await new PostgresSchemaInitializer(factory, PostgresSchemaMigrationCatalog.CreateDefault().Take(7)).InitializeAsync();
        repository = new PostgresAppRepository(factory);
    }

    public async Task DisposeAsync()
    {
        if (factory is null) return;
        await ExecuteAsync("""
            DELETE FROM strategy_loss_diff_parent_events WHERE parent_run_id=ANY(@Runs);
            DELETE FROM strategy_market_paper_runs WHERE id=ANY(@Runs);
            DELETE FROM strategy_loss_diff_states WHERE child_strategy_id=ANY(@Children);
            DELETE FROM strategy_child_parent_assignments WHERE child_strategy_id=ANY(@Children);
            DELETE FROM strategies WHERE id=ANY(@Children);
            DELETE FROM schema_migration_history WHERE migration_id='0008-eth-lossdiff-positive-progress-34';
            """);
    }

    [Fact]
    public async Task Migration_Exact102RowsZeroNoHistoryAndIdempotentFlagsAndState()
    {
        var before = await OtherRowsFingerprintAsync();
        var parent = Guid.Parse("b7c50005-0000-4000-8137-000000000104");
        await InsertRun(parent, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(-1), -1);
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        Assert.Equal(34L, await ScalarAsync<long>("SELECT count(*) FROM strategies WHERE id=ANY(@Children) AND enabled AND NOT paused AND NOT auto_live_paused AND NOT live_stakes AND live_enabled_at_utc IS NULL AND paper_stake_amount=1 AND live_stake_amount=1 AND paper_lost_coeff=1 AND live_lost_coeff=1 AND paper_lost_counter=0 AND live_lost_counter=0 AND live_available_balance=100;"));
        Assert.Equal(34L, await ScalarAsync<long>("SELECT count(*) FROM strategy_child_parent_assignments WHERE child_strategy_id=ANY(@Children) AND child_mode='LossDiffPositive' AND ended_at_utc IS NULL AND lookback_hours=0;"));
        Assert.Equal(34L, await ScalarAsync<long>("SELECT count(*) FROM strategy_loss_diff_states WHERE child_strategy_id=ANY(@Children) AND current_value=0 AND threshold=1;"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(DISTINCT started_at_utc) FROM strategy_loss_diff_states WHERE child_strategy_id=ANY(@Children);"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM strategy_loss_diff_parent_events WHERE child_strategy_id=ANY(@Children);"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id=ANY(@Children);"));
        Assert.Equal(before, await OtherRowsFingerprintAsync());
        var states = await repository.ReconcileStrategyLossDiffStatesAsync(parent, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(16, states.Count);
        Assert.All(states.Values, state => Assert.Equal(0, state.CurrentValue));
        await ExecuteAsync("UPDATE strategies SET enabled=false, paused=true, live_stakes=true, live_available_balance=17 WHERE id=ANY(@Children); UPDATE strategy_loss_diff_states SET current_value=7 WHERE child_strategy_id=ANY(@Children);");
        var snapshot = await NewRowsFingerprintAsync();
        await ApplySeedAsync();
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        Assert.Equal(snapshot, await NewRowsFingerprintAsync());
        Assert.Equal(before, await OtherRowsFingerprintAsync());
    }

    [Fact]
    public async Task Migration_IdentityConflictRollsBackWithoutPartialSeed()
    {
        await ExecuteAsync("""
            INSERT INTO strategies(id,code,name,description,enabled,created_at_utc,updated_at_utc)
            VALUES ('b7c50005-0000-4000-8236-000000000001','wrong-progress-code','wrong-progress-name','test',false,now(),now());
            """);
        var before = await NewRowsFingerprintAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(ApplySeedAsync);
        Assert.Contains("identity conflict", error.MessageText);
        Assert.Equal(before, await NewRowsFingerprintAsync());
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM strategy_loss_diff_states WHERE child_strategy_id=ANY(@Children);"));
    }

    [Fact]
    public async Task Counter_ExactCutoffsSettlementOrderRestartAndLegacyOrder()
    {
        await ApplySeedAsync();
        var child = LossDiffPositiveProgressStrategyTests.Child(8, 18);
        var parent = child.ParentStrategyId!.Value;
        var start = new DateTimeOffset(await ScalarAsync<DateTime>("SELECT min(started_at_utc) FROM strategy_loss_diff_states WHERE child_strategy_id=ANY(@Children);"));
        var cutoff = start.AddMinutes(10);
        await InsertRun(parent, start.AddTicks(-10), start.AddMinutes(1), -1);
        await InsertRun(parent, start, start.AddMinutes(4), 1);
        await InsertRun(parent, start.AddMinutes(2), start.AddMinutes(3), -1);
        await InsertRun(parent, start.AddMinutes(5), start.AddMinutes(6), 0);
        await InsertRun(parent, start.AddMinutes(7), cutoff, -1);
        await InsertRun(parent, start.AddMinutes(8), cutoff.AddMinutes(1), -1);
        var atCutoff = await repository.ReconcileStrategyLossDiffStatesAsync(parent, cutoff);
        Assert.Equal(0, atCutoff[child.Id].CurrentValue);
        Assert.All(atCutoff.Values.Where(s => Children.Contains(s.ChildStrategyId)), s => Assert.Equal(0, s.CurrentValue));
        Assert.Equal(1, atCutoff[StrategyIds.EthUp8BpsLossDiff16PlusPositive].CurrentValue);
        Assert.Equal(36L, await ScalarAsync<long>("SELECT count(*) FROM strategy_loss_diff_parent_events WHERE child_strategy_id=ANY(@Children);"));
        var after = await repository.ReconcileStrategyLossDiffStatesAsync(parent, cutoff.AddMinutes(2));
        Assert.Equal(2, after[child.Id].CurrentValue);
        var restarted = await new PostgresAppRepository(factory).ReconcileStrategyLossDiffStatesAsync(parent, cutoff.AddMinutes(2));
        Assert.Equal(2, restarted[child.Id].CurrentValue);
        Assert.Equal(72L, await ScalarAsync<long>("SELECT count(*) FROM strategy_loss_diff_parent_events WHERE child_strategy_id=ANY(@Children);"));
        var earlier = await repository.ReconcileStrategyLossDiffStatesAsync(parent, cutoff);
        Assert.Equal(0, earlier[child.Id].CurrentValue); // Already stored future events cannot leak into earlier decisions.
    }

    private async Task InsertRun(Guid parent, DateTimeOffset entered, DateTimeOffset settled, decimal gross)
    {
        var run = LossDiffPositiveProgressStrategyTests.Run(parent, entered, settled, gross);
        run = run with { MarketId = run.Id.ToString(), ConditionId = run.Id.ToString(), MarketSlug = run.Id.ToString() };
        runIds.Add(run.Id);
        Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
    }

    private async Task ApplySeedAsync()
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(PostgresLossDiffPositiveProgressStrategySchemaMigration.Sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = Command(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = Command(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
    private NpgsqlCommand Command(string sql, NpgsqlConnection connection)
    {
        var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("Children", Children);
        command.Parameters.AddWithValue("Runs", runIds.ToArray());
        return command;
    }
    private Task<string> OtherRowsFingerprintAsync() => ScalarAsync<string>("""
        SELECT md5(coalesce((SELECT string_agg(row_to_json(s)::text,'' ORDER BY id) FROM strategies s WHERE NOT(id=ANY(@Children))),'') ||
                   coalesce((SELECT string_agg(row_to_json(a)::text,'' ORDER BY id) FROM strategy_child_parent_assignments a WHERE NOT(child_strategy_id=ANY(@Children))),'') ||
                   coalesce((SELECT string_agg(row_to_json(d)::text,'' ORDER BY child_strategy_id) FROM strategy_loss_diff_states d WHERE NOT(child_strategy_id=ANY(@Children))),''));
        """);
    private Task<string> NewRowsFingerprintAsync() => ScalarAsync<string>("""
        SELECT md5(coalesce((SELECT string_agg(row_to_json(s)::text,'' ORDER BY id) FROM strategies s WHERE id=ANY(@Children)),'') ||
                   coalesce((SELECT string_agg(row_to_json(a)::text,'' ORDER BY id) FROM strategy_child_parent_assignments a WHERE child_strategy_id=ANY(@Children)),'') ||
                   coalesce((SELECT string_agg(row_to_json(d)::text,'' ORDER BY child_strategy_id) FROM strategy_loss_diff_states d WHERE child_strategy_id=ANY(@Children)),''));
        """);
}
