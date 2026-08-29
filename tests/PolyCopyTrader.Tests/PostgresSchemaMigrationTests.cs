using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PolyCopyTrader.Domain;
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
        Assert.Equal(4, catalog.Count);
        var baseline = catalog[0];
        var lossDiff = catalog[1];
        var ethUp8LossDiff = catalog[2];
        var signalsTraderWalletIndex = catalog[3];

        Assert.Equal(PostgresSchemaMigrationCatalog.LegacyBaselineId, baseline.Id);
        Assert.Equal(
            PostgresSchemaMigrationCatalog.LegacyBaselineSemanticChecksum,
            baseline.SemanticChecksum);
        Assert.True(baseline.IsLegacyBaseline);
        Assert.False(baseline.Transactional);
        Assert.Equal(PostgresLossDiffStrategySchemaMigration.Id, lossDiff.Id);
        Assert.Equal(1, lossDiff.Order);
        Assert.True(lossDiff.Transactional);
        Assert.False(lossDiff.IsLegacyBaseline);
        Assert.Equal(PostgresLossDiffStrategySchemaMigration.SemanticChecksum, lossDiff.SemanticChecksum);
        Assert.Contains("strategy_loss_diff_states", lossDiff.Sql, StringComparison.Ordinal);
        Assert.Contains("strategy_loss_diff_parent_events", lossDiff.Sql, StringComparison.Ordinal);
        Assert.Contains("current_value", lossDiff.Sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (child_strategy_id) DO NOTHING", lossDiff.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT count(*)", lossDiff.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO public.strategy_market_paper_runs", lossDiff.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PostgresEthUp8LossDiffStrategySchemaMigration.Id, ethUp8LossDiff.Id);
        Assert.Equal(2, ethUp8LossDiff.Order);
        Assert.True(ethUp8LossDiff.Transactional);
        Assert.False(ethUp8LossDiff.IsLegacyBaseline);
        Assert.Equal(
            PostgresEthUp8LossDiffStrategySchemaMigration.SemanticChecksum,
            ethUp8LossDiff.SemanticChecksum);
        Assert.Contains(StrategyIds.EthUp8BpsLossDiff3PlusIdValue, ethUp8LossDiff.Sql, StringComparison.Ordinal);
        Assert.Contains(StrategyIds.EthUp8BpsLossDiff16PlusPositiveIdValue, ethUp8LossDiff.Sql, StringComparison.Ordinal);
        Assert.Contains(StrategyIds.EthUp8BpsReferenceAveragePremarketParentIdValue, ethUp8LossDiff.Sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (child_strategy_id) DO NOTHING", ethUp8LossDiff.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO public.strategy_market_paper_runs", ethUp8LossDiff.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PostgresSignalsTraderWalletIndexSchemaMigration.Id, signalsTraderWalletIndex.Id);
        Assert.Equal(3, signalsTraderWalletIndex.Order);
        Assert.False(signalsTraderWalletIndex.Transactional);
        Assert.False(signalsTraderWalletIndex.IsLegacyBaseline);
        Assert.Equal(
            PostgresSignalsTraderWalletIndexSchemaMigration.SemanticChecksum,
            signalsTraderWalletIndex.SemanticChecksum);
        Assert.Equal(
            PostgresSignalsTraderWalletIndexSchemaMigration.CompletionCheckSql,
            signalsTraderWalletIndex.CompletionCheckSql);
        Assert.Equal(
            """
            CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_signals_trader_wallet_id
            ON public.signals(trader_wallet, id);
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            signalsTraderWalletIndex.Sql);
        Assert.Contains("index_metadata.indisvalid", signalsTraderWalletIndex.CompletionCheckSql, StringComparison.Ordinal);
        Assert.Contains("index_metadata.indisready", signalsTraderWalletIndex.CompletionCheckSql, StringComparison.Ordinal);
        Assert.Contains("index_metadata.indislive", signalsTraderWalletIndex.CompletionCheckSql, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "PostgresMigrationIntegration")]
    public async Task SignalsTraderWalletIndexMigration_CreatesExactShapeRejectsWrongShapesAndIsIdempotent()
    {
        var connectionString = Environment.GetEnvironmentVariable(IntegrationConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = CreateFactory(connectionString);
        var baseline = new PostgresSchemaMigration(
            0,
            "signals-index-test-baseline",
            "CREATE TABLE public.signals(id uuid NOT NULL, trader_wallet text NOT NULL, accepted boolean NOT NULL DEFAULT false);",
            transactional: false,
            details: "signals index test baseline",
            isLegacyBaseline: true);
        var migration = PostgresSchemaMigrationCatalog.CreateDefault()[3];

        await ResetPublicSchemaAsync(connectionString);
        var initializer = new PostgresSchemaInitializer(factory, [baseline, migration]);
        await initializer.InitializeAsync();
        Assert.True(await ScalarAsync<bool>(connectionString, migration.CompletionCheckSql!));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connectionString,
                $"SELECT count(*)::integer FROM public.schema_migration_history WHERE migration_id = '{migration.Id}';"));

        await initializer.InitializeAsync();
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connectionString,
                $"SELECT count(*)::integer FROM public.schema_migration_history WHERE migration_id = '{migration.Id}';"));

        await ResetPublicSchemaAsync(connectionString);
        await new PostgresSchemaInitializer(factory, [baseline]).InitializeAsync();
        await ExecuteAsync(connectionString, migration.Sql);
        await new PostgresSchemaInitializer(factory, [baseline, migration]).InitializeAsync();
        Assert.True(await ScalarAsync<bool>(connectionString, migration.CompletionCheckSql!));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connectionString,
                $"SELECT count(*)::integer FROM public.schema_migration_history WHERE migration_id = '{migration.Id}';"));

        var wrongIndexStatements = new[]
        {
            "CREATE UNIQUE INDEX ix_signals_trader_wallet_id ON public.signals(trader_wallet, id);",
            "CREATE INDEX ix_signals_trader_wallet_id ON public.signals(trader_wallet, id) WHERE accepted;",
            "CREATE INDEX ix_signals_trader_wallet_id ON public.signals(trader_wallet) INCLUDE (id);",
            "CREATE INDEX ix_signals_trader_wallet_id ON public.signals((lower(trader_wallet)), id);",
            "CREATE INDEX ix_signals_trader_wallet_id ON public.signals(id, trader_wallet);",
            "CREATE INDEX ix_signals_trader_wallet_id ON public.signals(trader_wallet DESC, id);",
            "CREATE INDEX ix_signals_trader_wallet_id ON public.signals(trader_wallet COLLATE \"C\", id);",
            "CREATE INDEX ix_signals_trader_wallet_id ON public.signals(trader_wallet text_pattern_ops, id);"
        };

        foreach (var wrongIndexStatement in wrongIndexStatements)
        {
            await ResetPublicSchemaAsync(connectionString);
            await ExecuteAsync(
                connectionString,
                "CREATE TABLE public.signals(id uuid NOT NULL, trader_wallet text NOT NULL, accepted boolean NOT NULL DEFAULT false);");
            await ExecuteAsync(connectionString, wrongIndexStatement);
            Assert.False(await ScalarAsync<bool>(connectionString, migration.CompletionCheckSql!));
        }
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
    public async Task EthUp8LossDiffMigration_StartsAtZeroWithoutHistoryAndDoesNotResetOnSecondStart()
    {
        var connectionString = Environment.GetEnvironmentVariable(IntegrationConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await ResetPublicSchemaAsync(connectionString);
        var factory = CreateFactory(connectionString);
        var baseline = PostgresSchemaMigrationCatalog.CreateDefault()[0];
        await new PostgresSchemaInitializer(factory, [baseline]).InitializeAsync();
        await ExecuteAsync(
            connectionString,
            """
INSERT INTO public.strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc
)
VALUES (
    'b7c50005-0000-4000-8227-000000000001',
    'b7c50005-0000-4000-8204-000000000001',
    'pre-lossdiff-migration-parent-run',
    'pre-lossdiff-migration-condition',
    'eth-updown-5m-pre-lossdiff-migration',
    'ETH Up or Down - pre LossDiff migration',
    'ETH Up/Down 5m Diff Confirmed Average Premarket',
    '2026-01-01T00:00:00Z',
    '2026-01-01T00:05:00Z',
    '2025-12-31T23:59:30Z',
    '2025-12-31T23:59:30Z',
    'Settled',
    'pre-lossdiff-asset',
    'Down',
    0.50,
    1.00,
    2.00,
    NULL,
    NULL,
    '2025-12-31T23:59:30Z',
    0,
    0,
    -1.00,
    '2026-01-01T00:05:00Z',
    NULL,
    '2025-12-31T23:59:30Z',
    '2026-01-01T00:05:00Z'
);
""");

        var initializer = new PostgresSchemaInitializer(factory);
        await initializer.InitializeAsync();

        Assert.Equal(4, await ScalarAsync<int>(
            connectionString,
            "SELECT count(*)::integer FROM public.strategy_loss_diff_states;"));
        Assert.Equal(0, await ScalarAsync<int>(
            connectionString,
            "SELECT sum(current_value)::integer FROM public.strategy_loss_diff_states;"));
        Assert.Equal(2, await ScalarAsync<int>(
            connectionString,
            "SELECT count(DISTINCT started_at_utc)::integer FROM public.strategy_loss_diff_states;"));
        Assert.Equal(0, await ScalarAsync<int>(
            connectionString,
            "SELECT count(*)::integer FROM public.strategy_loss_diff_parent_events;"));
        Assert.Equal(4, await ScalarAsync<int>(
            connectionString,
            "SELECT count(*)::integer FROM public.strategy_child_parent_assignments WHERE child_mode IN ('LossDiffReset', 'LossDiffPositive') AND ended_at_utc IS NULL;"));
        Assert.Equal(2, await ScalarAsync<int>(
            connectionString,
            "SELECT count(*)::integer FROM public.strategies WHERE id IN ('b7c50005-0000-4000-8225-000000000004', 'b7c50005-0000-4000-8225-000000000013') AND enabled AND NOT paused AND NOT live_stakes;"));
        Assert.Equal(2, await ScalarAsync<int>(
            connectionString,
            "SELECT count(*)::integer FROM public.strategies WHERE id IN ('b7c50005-0000-4000-8229-000000000003', 'b7c50005-0000-4000-8229-000000000016') AND enabled AND NOT paused AND NOT auto_live_paused AND NOT live_stakes AND paper_stake_amount = 1.00 AND live_stake_amount = 1.00 AND live_available_balance = 100.00;"));

        var startedAt = DateTime.SpecifyKind(
            await ScalarAsync<DateTime>(
                connectionString,
                "SELECT min(started_at_utc) FROM public.strategy_loss_diff_states WHERE parent_strategy_id = 'b7c50005-0000-4000-8204-000000000001';"),
            DateTimeKind.Utc);
        var firstEntry = new DateTimeOffset(startedAt).AddMinutes(1);
        var cutoff = firstEntry.AddHours(1);
        var postCutoffRows = string.Join(
            ",\n",
            Enumerable.Range(1, 4).Select(index =>
            {
                var enteredAt = firstEntry.AddMinutes((index - 1) * 5);
                return $"('b7c50005-0000-4000-8231-{index.ToString("000000000000", System.Globalization.CultureInfo.InvariantCulture)}', 'post-lossdiff-parent-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}', '{enteredAt:O}', '{enteredAt.AddMinutes(5):O}', -1.00)";
            }));
        await ExecuteAsync(
            connectionString,
            $$"""
INSERT INTO public.strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc
)
SELECT
    source.id::uuid,
    'b7c50005-0000-4000-8204-000000000001'::uuid,
    source.market_id,
    source.market_id || '-condition',
    source.market_id || '-slug',
    'ETH Up or Down - post LossDiff migration',
    'ETH Up/Down 5m Diff Confirmed Average Premarket',
    source.entered_at_utc::timestamptz,
    source.settled_at_utc::timestamptz,
    source.entered_at_utc::timestamptz,
    source.entered_at_utc::timestamptz,
    'Settled',
    source.market_id || '-asset',
    'Down',
    0.50,
    1.00,
    2.00,
    NULL,
    NULL,
    source.entered_at_utc::timestamptz,
    0,
    0,
    source.realized_pnl_usd,
    source.settled_at_utc::timestamptz,
    NULL,
    source.entered_at_utc::timestamptz,
    source.settled_at_utc::timestamptz
FROM (VALUES
{{postCutoffRows}}
) AS source(id, market_id, entered_at_utc, settled_at_utc, realized_pnl_usd);
""");
        var excludedChildEnteredAt = firstEntry.AddMinutes(22);
        var excludedFutureParentEnteredAt = cutoff.AddMinutes(5);
        await ExecuteAsync(
            connectionString,
            $"""
INSERT INTO public.strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc
)
SELECT
    excluded.id::uuid,
    excluded.strategy_id::uuid,
    excluded.market_id,
    excluded.market_id || '-condition',
    excluded.market_id || '-slug',
    'Excluded LossDiff outcome',
    'ETH Up/Down 5m Diff Confirmed Average Premarket',
    excluded.entered_at_utc::timestamptz,
    excluded.settled_at_utc::timestamptz,
    excluded.entered_at_utc::timestamptz,
    excluded.entered_at_utc::timestamptz,
    'Settled',
    excluded.market_id || '-asset',
    'Down',
    0.50,
    1.00,
    2.00,
    NULL,
    NULL,
    excluded.entered_at_utc::timestamptz,
    0,
    0,
    -1.00,
    excluded.settled_at_utc::timestamptz,
    NULL,
    excluded.entered_at_utc::timestamptz,
    excluded.settled_at_utc::timestamptz
FROM (VALUES
    (
        'b7c50005-0000-4000-8227-000000000006',
        'b7c50005-0000-4000-8225-000000000004',
        'post-lossdiff-child-outcome',
        '{excludedChildEnteredAt:O}',
        '{excludedChildEnteredAt.AddMinutes(5):O}'
    ),
    (
        'b7c50005-0000-4000-8227-000000000007',
        'b7c50005-0000-4000-8204-000000000001',
        'post-lossdiff-parent-future-settlement',
        '{excludedFutureParentEnteredAt:O}',
        '{excludedFutureParentEnteredAt.AddMinutes(5):O}'
    )
) AS excluded(id, strategy_id, market_id, entered_at_utc, settled_at_utc);
""");
        var repository = new PostgresAppRepository(factory);
        var concurrentResults = await Task.WhenAll(
            repository.ReconcileStrategyLossDiffStatesAsync(
                StrategyIds.EthDiffConfirmedAveragePremarketParent,
                cutoff),
            repository.ReconcileStrategyLossDiffStatesAsync(
                StrategyIds.EthDiffConfirmedAveragePremarketParent,
                cutoff));
        Assert.All(concurrentResults, states =>
        {
            Assert.Equal(4, states[StrategyIds.EthLossDiff4Plus].CurrentValue);
            Assert.Equal(4, states[StrategyIds.EthLossDiff13PlusPositive].CurrentValue);
        });
        Assert.Equal(8, await ScalarAsync<int>(
            connectionString,
            "SELECT count(*)::integer FROM public.strategy_loss_diff_parent_events;"));

        var winEnteredAt = firstEntry.AddMinutes(25);
        await ExecuteAsync(
            connectionString,
            $"""
INSERT INTO public.strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc
)
VALUES (
    'b7c50005-0000-4000-8227-000000000005',
    'b7c50005-0000-4000-8204-000000000001',
    'post-lossdiff-parent-win',
    'post-lossdiff-parent-win-condition',
    'post-lossdiff-parent-win-slug',
    'ETH Up or Down - post LossDiff migration win',
    'ETH Up/Down 5m Diff Confirmed Average Premarket',
    '{winEnteredAt:O}',
    '{winEnteredAt.AddMinutes(5):O}',
    '{winEnteredAt:O}',
    '{winEnteredAt:O}',
    'Settled',
    'post-lossdiff-parent-win-asset',
    'Up',
    0.50,
    1.00,
    2.00,
    NULL,
    NULL,
    '{winEnteredAt:O}',
    1,
    2,
    1.00,
    '{winEnteredAt.AddMinutes(5):O}',
    NULL,
    '{winEnteredAt:O}',
    '{winEnteredAt.AddMinutes(5):O}'
);
""");
        var afterWin = await repository.ReconcileStrategyLossDiffStatesAsync(
            StrategyIds.EthDiffConfirmedAveragePremarketParent,
            cutoff);
        Assert.Equal(0, afterWin[StrategyIds.EthLossDiff4Plus].CurrentValue);
        Assert.Equal(3, afterWin[StrategyIds.EthLossDiff13PlusPositive].CurrentValue);
        Assert.Equal(10, await ScalarAsync<int>(
            connectionString,
            "SELECT count(*)::integer FROM public.strategy_loss_diff_parent_events;"));

        await ExecuteAsync(
            connectionString,
            "UPDATE public.strategy_loss_diff_states SET current_value = 7 WHERE parent_strategy_id = 'b7c50005-0000-4000-8204-000000000001';");
        await initializer.InitializeAsync();

        Assert.Equal(14, await ScalarAsync<int>(
            connectionString,
            "SELECT sum(current_value)::integer FROM public.strategy_loss_diff_states;"));
        Assert.Equal(0, await ScalarAsync<int>(
            connectionString,
            "SELECT sum(current_value)::integer FROM public.strategy_loss_diff_states WHERE parent_strategy_id = 'b7c50005-0000-4000-8137-000000000108';"));
        Assert.Equal(4, await ScalarAsync<int>(
            connectionString,
            "SELECT count(*)::integer FROM public.schema_migration_history;"));
    }

    [Fact]
    [Trait("Category", "PostgresMigrationIntegration")]
    public async Task EthUp8LossDiffState_UsesResetThreeAndPositiveSixteenAfterZeroCutoff()
    {
        var connectionString = Environment.GetEnvironmentVariable(IntegrationConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await ResetPublicSchemaAsync(connectionString);
        var factory = CreateFactory(connectionString);
        var baseline = PostgresSchemaMigrationCatalog.CreateDefault()[0];
        await new PostgresSchemaInitializer(factory, [baseline]).InitializeAsync();
        await ExecuteAsync(
            connectionString,
            CreateSettledLossDiffParentRunsSql(
                StrategyIds.EthUp8BpsReferenceAveragePremarketParent,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                [-1m],
                "eth-up8-lossdiff-pre-rollout"));

        var initializer = new PostgresSchemaInitializer(factory);
        await initializer.InitializeAsync();

        Assert.Equal(0, await ScalarAsync<int>(
            connectionString,
            $"SELECT sum(current_value)::integer FROM public.strategy_loss_diff_states WHERE child_strategy_id IN ('{StrategyIds.EthUp8BpsLossDiff3Plus:D}', '{StrategyIds.EthUp8BpsLossDiff16PlusPositive:D}');"));
        Assert.Equal(0, await ScalarAsync<int>(
            connectionString,
            $"SELECT count(*)::integer FROM public.strategy_loss_diff_parent_events WHERE child_strategy_id IN ('{StrategyIds.EthUp8BpsLossDiff3Plus:D}', '{StrategyIds.EthUp8BpsLossDiff16PlusPositive:D}');"));

        var startedAt = DateTime.SpecifyKind(
            await ScalarAsync<DateTime>(
                connectionString,
                $"SELECT started_at_utc FROM public.strategy_loss_diff_states WHERE child_strategy_id = '{StrategyIds.EthUp8BpsLossDiff3Plus:D}';"),
            DateTimeKind.Utc);
        var firstEntry = new DateTimeOffset(startedAt).AddMinutes(1);
        await ExecuteAsync(
            connectionString,
            CreateSettledLossDiffParentRunsSql(
                StrategyIds.EthUp8BpsReferenceAveragePremarketParent,
                firstEntry,
                Enumerable.Repeat(-1m, 16).ToArray(),
                "eth-up8-lossdiff-post-rollout"));

        var repository = new PostgresAppRepository(factory);
        var lossCutoff = firstEntry.AddMinutes(16 * 5 + 1);
        var afterLosses = await repository.ReconcileStrategyLossDiffStatesAsync(
            StrategyIds.EthUp8BpsReferenceAveragePremarketParent,
            lossCutoff);
        var retryAfterLosses = await repository.ReconcileStrategyLossDiffStatesAsync(
            StrategyIds.EthUp8BpsReferenceAveragePremarketParent,
            lossCutoff);
        Assert.Equal(16, afterLosses[StrategyIds.EthUp8BpsLossDiff3Plus].CurrentValue);
        Assert.Equal(16, afterLosses[StrategyIds.EthUp8BpsLossDiff16PlusPositive].CurrentValue);
        Assert.Equal(16, retryAfterLosses[StrategyIds.EthUp8BpsLossDiff3Plus].CurrentValue);
        Assert.Equal(16, retryAfterLosses[StrategyIds.EthUp8BpsLossDiff16PlusPositive].CurrentValue);

        var winEntry = firstEntry.AddMinutes(16 * 5 + 5);
        await ExecuteAsync(
            connectionString,
            CreateSettledLossDiffParentRunsSql(
                StrategyIds.EthUp8BpsReferenceAveragePremarketParent,
                winEntry,
                [1m],
                "eth-up8-lossdiff-win"));
        var afterWin = await repository.ReconcileStrategyLossDiffStatesAsync(
            StrategyIds.EthUp8BpsReferenceAveragePremarketParent,
            winEntry.AddMinutes(6));

        Assert.Equal(0, afterWin[StrategyIds.EthUp8BpsLossDiff3Plus].CurrentValue);
        Assert.Equal(15, afterWin[StrategyIds.EthUp8BpsLossDiff16PlusPositive].CurrentValue);
        Assert.Equal(34, await ScalarAsync<int>(
            connectionString,
            $"SELECT count(*)::integer FROM public.strategy_loss_diff_parent_events WHERE child_strategy_id IN ('{StrategyIds.EthUp8BpsLossDiff3Plus:D}', '{StrategyIds.EthUp8BpsLossDiff16PlusPositive:D}');"));
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
        Assert.Equal(4, await ScalarAsync<int>(connectionString, "SELECT count(*)::integer FROM public.schema_migration_history;"));

        await Task.Delay(25);
        await initializer.InitializeAsync();

        Assert.Equal(
            firstUpdatedAt,
            await ScalarAsync<DateTime>(connectionString, "SELECT max(updated_at_utc) FROM public.strategies;"));
        Assert.Equal(4, await ScalarAsync<int>(connectionString, "SELECT count(*)::integer FROM public.schema_migration_history;"));
    }

    private static string CreateSettledLossDiffParentRunsSql(
        Guid parentStrategyId,
        DateTimeOffset firstEntryUtc,
        IReadOnlyList<decimal> realizedPnlValues,
        string identityPrefix)
    {
        var values = string.Join(
            ",\n",
            realizedPnlValues.Select((realizedPnlUsd, index) =>
            {
                var enteredAtUtc = firstEntryUtc.AddMinutes(index * 5);
                var settledAtUtc = enteredAtUtc.AddMinutes(5);
                return $"('{Guid.NewGuid():D}', '{identityPrefix}-{index}', '{enteredAtUtc:O}', '{settledAtUtc:O}', {realizedPnlUsd.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            }));

        return $$"""
INSERT INTO public.strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc
)
SELECT
    source.id::uuid,
    '{{parentStrategyId:D}}'::uuid,
    source.market_id,
    source.market_id || '-condition',
    source.market_id || '-slug',
    'ETH Up 8 bps LossDiff parent outcome',
    'ETH Up/Down 5m Up Bps Reference Average Premarket',
    source.entered_at_utc::timestamptz,
    source.settled_at_utc::timestamptz,
    source.entered_at_utc::timestamptz,
    source.entered_at_utc::timestamptz,
    'Settled',
    source.market_id || '-asset',
    CASE WHEN source.realized_pnl_usd > 0 THEN 'Up' ELSE 'Down' END,
    0.50,
    1.00,
    2.00,
    NULL,
    NULL,
    source.entered_at_utc::timestamptz,
    CASE WHEN source.realized_pnl_usd > 0 THEN 1 ELSE 0 END,
    CASE WHEN source.realized_pnl_usd > 0 THEN 2 ELSE 0 END,
    source.realized_pnl_usd,
    source.settled_at_utc::timestamptz,
    NULL,
    source.entered_at_utc::timestamptz,
    source.settled_at_utc::timestamptz
FROM (VALUES
{{values}}
) AS source(id, market_id, entered_at_utc, settled_at_utc, realized_pnl_usd);
""";
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
