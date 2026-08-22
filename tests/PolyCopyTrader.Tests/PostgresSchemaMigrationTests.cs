using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class PostgresSchemaMigrationTests
{
    private const string IntegrationConnectionVariable =
        "POLYCOPYTRADER_MIGRATION_TEST_POSTGRES_CONNECTION";

    [Fact]
    public void CalculateSemanticChecksum_NormalizesLineEndings()
    {
        var windows = PostgresSchemaMigration.CalculateSemanticChecksum("SELECT 1;\r\nSELECT 2;\r\n");
        var unix = PostgresSchemaMigration.CalculateSemanticChecksum("SELECT 1;\nSELECT 2;\n");

        Assert.Equal(unix, windows);
        Assert.Matches("^[0-9a-f]{64}$", windows);
    }

    [Fact]
    public void ValidateAndOrder_OrdersCatalogAndRejectsDuplicateIdentity()
    {
        var baseline = Migration(0, "baseline", "SELECT 0;", isBaseline: true);
        var second = Migration(2, "second", "SELECT 2;");
        var first = Migration(1, "first", "SELECT 1;");

        var ordered = PostgresSchemaMigrationCatalog.ValidateAndOrder([second, baseline, first]);

        Assert.Equal(["baseline", "first", "second"], ordered.Select(item => item.Id));
        var duplicateId = Assert.Throws<InvalidOperationException>(() =>
            PostgresSchemaMigrationCatalog.ValidateAndOrder(
                [baseline, first, Migration(2, "first", "SELECT 3;")]));
        Assert.Contains("Duplicate PostgreSQL migration id 'first'", duplicateId.Message);
        var duplicateOrder = Assert.Throws<InvalidOperationException>(() =>
            PostgresSchemaMigrationCatalog.ValidateAndOrder(
                [baseline, first, Migration(1, "other", "SELECT 3;")]));
        Assert.Contains("Duplicate PostgreSQL migration order '1'", duplicateOrder.Message);
    }

    [Fact]
    public void NonTransactionalMigration_RequiresCompletionCheck()
    {
        var exception = Assert.Throws<ArgumentException>(() => new PostgresSchemaMigration(
            1,
            "non-transactional",
            "CREATE INDEX CONCURRENTLY example ON example_table(id);",
            transactional: false,
            details: "test"));

        Assert.Contains("requires an explicit completion check", exception.Message);
    }

    [Fact]
    public void LegacyBaselineState_RequiresEveryExactExistingDatabasePredicate()
    {
        var eligible = new PostgresLegacyBaselineState(100, true, true, true, true, true, true);
        var empty = new PostgresLegacyBaselineState(0, false, false, false, false, false, false);
        var partial = eligible with { HasCopiedPerformanceTerminalTrigger = false };

        Assert.True(eligible.IsEligibleExistingDatabase);
        Assert.False(eligible.IsEmpty);
        Assert.True(empty.IsEmpty);
        Assert.False(empty.IsEligibleExistingDatabase);
        Assert.False(partial.IsEligibleExistingDatabase);
        Assert.Contains("copied_performance_terminal_trigger=False", partial.GetFailureDiagnostic());
    }

    [Fact]
    public void DefaultCatalog_IsBoundToApprovedLegacyChecksum()
    {
        var catalog = PostgresSchemaMigrationCatalog.CreateDefault();
        var baseline = Assert.Single(catalog);

        Assert.Equal(PostgresSchemaMigrationCatalog.LegacyBaselineId, baseline.Id);
        Assert.Equal(
            PostgresSchemaMigrationCatalog.LegacyBaselineSemanticChecksum,
            baseline.SemanticChecksum);
        Assert.True(baseline.IsLegacyBaseline);
        Assert.False(baseline.Transactional);
    }

    [Fact]
    public void ProductionDiRegistration_ResolvesDefaultCatalogWithoutMigrationServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateFactory(
            "Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Password=not_used;Timeout=1"));
        services.AddSingleton<IStorageSchemaInitializer, PostgresSchemaInitializer>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<PostgresSchemaInitializer>(
            provider.GetRequiredService<IStorageSchemaInitializer>());
    }

    [Fact]
    [Trait("Category", "PostgresMigrationIntegration")]
    public async Task Initializer_EmptyPendingFailureChecksumConcurrencyAndNoReplayContracts()
    {
        var connectionString = Environment.GetEnvironmentVariable(IntegrationConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = CreateFactory(connectionString);
        await ResetPublicSchemaAsync(connectionString);

        var baseline = new PostgresSchemaMigration(
            0,
            "test-baseline",
            """
CREATE SEQUENCE public.migration_replay_probe;
SELECT nextval('public.migration_replay_probe');
""",
            transactional: false,
            details: "test baseline",
            isLegacyBaseline: true);
        var pending = Migration(
            1,
            "test-pending",
            """
CREATE TABLE public.migration_pending_probe(id integer PRIMARY KEY, value text NOT NULL);
INSERT INTO public.migration_pending_probe(id, value) VALUES (1, 'initial');
""");
        var initializer = new PostgresSchemaInitializer(factory, [pending, baseline]);

        await initializer.InitializeAsync();
        Assert.Equal(2, await ScalarAsync<int>(connectionString, "SELECT count(*)::integer FROM public.schema_migration_history;"));
        Assert.Equal(1L, await ScalarAsync<long>(connectionString, "SELECT last_value FROM public.migration_replay_probe;"));
        Assert.Equal("initial", await ScalarAsync<string>(connectionString, "SELECT value FROM public.migration_pending_probe WHERE id=1;"));

        await initializer.InitializeAsync();
        Assert.Equal(1L, await ScalarAsync<long>(connectionString, "SELECT last_value FROM public.migration_replay_probe;"));
        Assert.Equal(2, await ScalarAsync<int>(connectionString, "SELECT count(*)::integer FROM public.schema_migration_history;"));

        var changedPending = Migration(
            1,
            "test-pending",
            "UPDATE public.migration_pending_probe SET value='changed' WHERE id=1;");
        var checksumMismatch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostgresSchemaInitializer(factory, [baseline, changedPending]).InitializeAsync());
        Assert.Contains("checksum mismatch", checksumMismatch.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("initial", await ScalarAsync<string>(connectionString, "SELECT value FROM public.migration_pending_probe WHERE id=1;"));

        await ResetPublicSchemaAsync(connectionString);
        var failing = Migration(
            1,
            "retryable-pending",
        """
CREATE TABLE public.must_rollback(id integer);
DO $$ BEGIN RAISE EXCEPTION 'sensitive-sql-marker'; END $$;
""");
        var failedInitializer = new PostgresSchemaInitializer(factory, [baseline, failing]);
        await using var failureLockProbe = new NpgsqlConnection(connectionString);
        await failureLockProbe.OpenAsync();
        var originalOutput = Console.Out;
        using var capturedOutput = new StringWriter();
        try
        {
            Console.SetOut(capturedOutput);
            var sanitizedFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                failedInitializer.InitializeAsync());
            Assert.Contains("SQLSTATE P0001", sanitizedFailure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("sensitive-sql-marker", sanitizedFailure.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        Assert.Contains("retryable-pending", capturedOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("SQLSTATE P0001", capturedOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-sql-marker", capturedOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, capturedOutput.ToString(), StringComparison.Ordinal);
        await AssertAdvisoryLockAvailableAsync(failureLockProbe);
        Assert.Null(await ScalarAsync<string?>(connectionString, "SELECT to_regclass('public.must_rollback')::text;"));
        Assert.Equal(1, await ScalarAsync<int>(connectionString, "SELECT count(*)::integer FROM public.schema_migration_history;"));

        var repaired = Migration(
            1,
            "retryable-pending",
            "CREATE TABLE public.must_rollback(id integer);");
        await new PostgresSchemaInitializer(factory, [baseline, repaired]).InitializeAsync();
        Assert.Equal("must_rollback", await ScalarAsync<string>(connectionString, "SELECT to_regclass('public.must_rollback')::text;"));
        Assert.Equal(2, await ScalarAsync<int>(connectionString, "SELECT count(*)::integer FROM public.schema_migration_history;"));

        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync();
        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(@LockKey);",
            lockConnection))
        {
            lockCommand.Parameters.AddWithValue("LockKey", PostgresSchemaInitializer.AdvisoryLockKey);
            Assert.True((bool)(await lockCommand.ExecuteScalarAsync() ?? false));
        }

        try
        {
            var concurrent = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PostgresSchemaInitializer(factory, [baseline, repaired]).InitializeAsync());
            Assert.Contains("already running in another process", concurrent.Message);
        }
        finally
        {
            await using var unlockCommand = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(@LockKey);",
                lockConnection);
            unlockCommand.Parameters.AddWithValue("LockKey", PostgresSchemaInitializer.AdvisoryLockKey);
            Assert.True((bool)(await unlockCommand.ExecuteScalarAsync() ?? false));
        }

        await ResetPublicSchemaAsync(connectionString);
        var cancellable = Migration(1, "cancellable-pending", "SELECT pg_sleep(30);");
        await using var cancellationLockProbe = new NpgsqlConnection(connectionString);
        await cancellationLockProbe.OpenAsync();
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new PostgresSchemaInitializer(factory, [baseline, cancellable])
                    .InitializeAsync(cancellation.Token));
        }

        await AssertAdvisoryLockAvailableAsync(cancellationLockProbe);
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connectionString,
                "SELECT count(*)::integer FROM public.schema_migration_history;"));
        var recoveredAfterCancellation = Migration(
            1,
            "cancellable-pending",
            "CREATE TABLE public.cancellation_recovery_probe(id integer);");
        await new PostgresSchemaInitializer(factory, [baseline, recoveredAfterCancellation]).InitializeAsync();
        Assert.Equal(
            "cancellation_recovery_probe",
            await ScalarAsync<string>(
                connectionString,
                "SELECT to_regclass('public.cancellation_recovery_probe')::text;"));

        await ResetPublicSchemaAsync(connectionString);
        var nonTransactional = new PostgresSchemaMigration(
            1,
            "non-transactional-pending",
            "CREATE TABLE public.non_transactional_probe(id integer);",
            transactional: false,
            details: "test non-transactional migration",
            completionCheckSql: "SELECT to_regclass('public.non_transactional_probe') IS NOT NULL;");
        var nonTransactionalInitializer = new PostgresSchemaInitializer(factory, [baseline, nonTransactional]);
        await nonTransactionalInitializer.InitializeAsync();
        await nonTransactionalInitializer.InitializeAsync();
        Assert.Equal(
            2,
            await ScalarAsync<int>(
                connectionString,
                "SELECT count(*)::integer FROM public.schema_migration_history;"));

        await ExecuteAsync(
            connectionString,
            """
INSERT INTO public.schema_migration_history(migration_id, semantic_checksum, applied_at_utc, details)
VALUES ('unknown-migration', repeat('a', 64), clock_timestamp(), 'test unknown');
""");
        var unknownHistory = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nonTransactionalInitializer.InitializeAsync());
        Assert.Contains("unknown PostgreSQL migration", unknownHistory.Message, StringComparison.Ordinal);

        await ResetPublicSchemaAsync(connectionString);
        var orderedFirst = Migration(1, "ordered-first", "CREATE TABLE public.ordered_first(id integer);");
        var orderedSecond = Migration(2, "ordered-second", "CREATE TABLE public.ordered_second(id integer);");
        var orderedInitializer = new PostgresSchemaInitializer(factory, [baseline, orderedFirst, orderedSecond]);
        await orderedInitializer.InitializeAsync();
        await ExecuteAsync(
            connectionString,
            "DELETE FROM public.schema_migration_history WHERE migration_id='ordered-first';");
        var historyGap = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orderedInitializer.InitializeAsync());
        Assert.Contains("after a missing earlier migration", historyGap.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "PostgresMigrationIntegration")]
    public async Task Initializer_RegistersOnlyExactEligibleExistingBaseline()
    {
        var connectionString = Environment.GetEnvironmentVariable(IntegrationConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = CreateFactory(connectionString);
        var baseline = new PostgresSchemaMigration(
            0,
            "existing-baseline",
            "CREATE TABLE public.must_not_run(id integer);",
            transactional: false,
            details: "existing baseline",
            isLegacyBaseline: true);

        await ResetPublicSchemaAsync(connectionString);
        await ExecuteAsync(connectionString, EligibleLegacySchemaSql);
        await new PostgresSchemaInitializer(factory, [baseline]).InitializeAsync();

        Assert.Null(await ScalarAsync<string?>(connectionString, "SELECT to_regclass('public.must_not_run')::text;"));
        Assert.Equal(
            "existing-baseline",
            await ScalarAsync<string>(connectionString, "SELECT migration_id FROM public.schema_migration_history;"));

        await ResetPublicSchemaAsync(connectionString);
        await ExecuteAsync(
            connectionString,
            "CREATE FUNCTION public.unknown_function_only() RETURNS integer LANGUAGE sql AS 'SELECT 1';");
        var functionOnly = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostgresSchemaInitializer(factory, [baseline]).InitializeAsync());
        Assert.Contains("application_objects=1", functionOnly.Message, StringComparison.Ordinal);
        Assert.Null(await ScalarAsync<string?>(
            connectionString,
            "SELECT to_regclass('public.schema_migration_history')::text;"));

        await ResetPublicSchemaAsync(connectionString);
        await ExecuteAsync(connectionString, "CREATE TYPE public.unknown_type_only AS ENUM ('value');");
        var typeOnly = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostgresSchemaInitializer(factory, [baseline]).InitializeAsync());
        Assert.Contains("application_objects=2", typeOnly.Message, StringComparison.Ordinal);
        Assert.Null(await ScalarAsync<string?>(
            connectionString,
            "SELECT to_regclass('public.schema_migration_history')::text;"));

        await ResetPublicSchemaAsync(connectionString);
        await ExecuteAsync(connectionString, "CREATE TABLE public.partial_legacy(id integer);");
        var ineligible = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostgresSchemaInitializer(factory, [baseline]).InitializeAsync());

        Assert.Contains("not eligible", ineligible.Message);
        Assert.Null(await ScalarAsync<string?>(
            connectionString,
            "SELECT to_regclass('public.schema_migration_history')::text;"));
        Assert.Null(await ScalarAsync<string?>(connectionString, "SELECT to_regclass('public.must_not_run')::text;"));
    }

    [Fact]
    [Trait("Category", "PostgresMigrationIntegration")]
    public async Task DefaultInitializer_BootstrapsOnceAndSecondStartDoesNotReplayBaseline()
    {
        var connectionString = Environment.GetEnvironmentVariable(IntegrationConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await ResetPublicSchemaAsync(connectionString);
        var initializer = new PostgresSchemaInitializer(CreateFactory(connectionString));

        await initializer.InitializeAsync();
        var firstUpdatedAt = await ScalarAsync<DateTime>(
            connectionString,
            "SELECT max(updated_at_utc) FROM public.strategies;");
        var relationCount = await ScalarAsync<int>(
            connectionString,
            """
SELECT count(*)::integer
FROM pg_class cls
JOIN pg_namespace ns ON ns.oid=cls.relnamespace
WHERE ns.nspname='public' AND cls.relkind IN ('r','p');
""");
        Assert.True(relationCount > 100);
        Assert.Equal(1, await ScalarAsync<int>(connectionString, "SELECT count(*)::integer FROM public.schema_migration_history;"));

        await Task.Delay(25);
        await initializer.InitializeAsync();

        Assert.Equal(
            firstUpdatedAt,
            await ScalarAsync<DateTime>(connectionString, "SELECT max(updated_at_utc) FROM public.strategies;"));
        Assert.Equal(1, await ScalarAsync<int>(connectionString, "SELECT count(*)::integer FROM public.schema_migration_history;"));
    }

    private static PostgresSchemaMigration Migration(
        int order,
        string id,
        string sql,
        bool isBaseline = false)
    {
        return new PostgresSchemaMigration(
            order,
            id,
            sql,
            transactional: !isBaseline,
            details: "test migration",
            isLegacyBaseline: isBaseline);
    }

    private static PostgresConnectionFactory CreateFactory(string connectionString)
    {
        return new PostgresConnectionFactory(new StorageOptions
        {
            ConnectionString = connectionString
        });
    }

    private static async Task ResetPublicSchemaAsync(string connectionString)
    {
        await ExecuteAsync(
            connectionString,
            """
DROP SCHEMA public CASCADE;
CREATE SCHEMA public;
GRANT ALL ON SCHEMA public TO PUBLIC;
""");
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertAdvisoryLockAvailableAsync(NpgsqlConnection connection)
    {
        await using (var acquire = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(@LockKey);",
            connection))
        {
            acquire.Parameters.AddWithValue("LockKey", PostgresSchemaInitializer.AdvisoryLockKey);
            Assert.True((bool)(await acquire.ExecuteScalarAsync() ?? false));
        }

        await using var release = new NpgsqlCommand(
            "SELECT pg_advisory_unlock(@LockKey);",
            connection);
        release.Parameters.AddWithValue("LockKey", PostgresSchemaInitializer.AdvisoryLockKey);
        Assert.True((bool)(await release.ExecuteScalarAsync() ?? false));
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull)
        {
            return default!;
        }

        return (T)value;
    }

    private const string EligibleLegacySchemaSql = """
CREATE TABLE public.service_heartbeats (
    service_name text PRIMARY KEY,
    version text NOT NULL
);
INSERT INTO public.service_heartbeats(service_name, version)
VALUES ('PolyCopyTrader.Service', 'info=1.0.0+a3b0457fc113fc5ef482aabd5c090c3162045001; assembly=1.0.0.0; mvid=test');

CREATE TABLE public.schema_data_migrations (
    migration_key text PRIMARY KEY
);
INSERT INTO public.schema_data_migrations(migration_key)
VALUES ('20260813_remove_paired_maker_gtd_first_accepting_strategies');

CREATE TABLE public.dashboard_projection_control(id integer);
CREATE TABLE public.paper_copied_trader_performance_refresh_queue(id integer);
CREATE TABLE public.live_orders(id integer);
CREATE TABLE public.paper_positions(id integer);

CREATE FUNCTION public.queue_dashboard_live_order_projection_event()
RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$;
CREATE TRIGGER trg_dashboard_projection_live_order
AFTER INSERT OR UPDATE OR DELETE ON public.live_orders
FOR EACH ROW EXECUTE FUNCTION public.queue_dashboard_live_order_projection_event();

CREATE FUNCTION public.queue_paper_copied_trader_performance_position_delete()
RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NULL; END $$;
CREATE TRIGGER trg_paper_copied_trader_performance_position_delete
AFTER DELETE ON public.paper_positions
REFERENCING OLD TABLE AS old_paper_positions
FOR EACH STATEMENT
EXECUTE FUNCTION public.queue_paper_copied_trader_performance_position_delete();
""";
}
