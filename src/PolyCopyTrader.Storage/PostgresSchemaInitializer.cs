using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace PolyCopyTrader.Storage;

public sealed class PostgresSchemaInitializer : IStorageSchemaInitializer
{
    public const long AdvisoryLockKey = 5_663_213_941_773_011_087L;

    private const int MetadataCommandTimeoutSeconds = 15;
    private const int MigrationCommandTimeoutSeconds = 0;
    private const string HistoryRelationName = "schema_migration_history";
    private const string QualifiedHistoryRelationName = "public.schema_migration_history";

    private const string CreateHistoryTableSql = """
CREATE TABLE public.schema_migration_history (
    migration_id text PRIMARY KEY,
    semantic_checksum text NOT NULL,
    applied_at_utc timestamptz NOT NULL,
    details text NOT NULL,
    CONSTRAINT ck_schema_migration_history_checksum
        CHECK (semantic_checksum ~ '^[0-9a-f]{64}$')
);
""";

    private readonly PostgresConnectionFactory connectionFactory;
    private readonly IReadOnlyList<PostgresSchemaMigration> migrations;

    public PostgresSchemaInitializer(PostgresConnectionFactory connectionFactory)
        : this(connectionFactory, PostgresSchemaMigrationCatalog.CreateDefault())
    {
    }

    public PostgresSchemaInitializer(
        PostgresConnectionFactory connectionFactory,
        IEnumerable<PostgresSchemaMigration> migrations)
    {
        this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        this.migrations = PostgresSchemaMigrationCatalog.ValidateAndOrder(migrations);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var lockAcquired = false;
        try
        {
            await connection.OpenAsync(cancellationToken);
            lockAcquired = await TryAcquireMigrationLockAsync(connection, cancellationToken);
            if (!lockAcquired)
            {
                throw new InvalidOperationException(
                    "PostgreSQL schema initialization is already running in another process; no migration SQL was executed.");
            }

            var baseline = migrations[0];
            var historyExists = await SchemaRelationExistsAsync(
                connection,
                HistoryRelationName,
                transaction: null,
                cancellationToken);

            if (!historyExists)
            {
                var baselineState = await ReadLegacyBaselineStateAsync(
                    connection,
                    transaction: null,
                    cancellationToken);
                if (baselineState.IsEmpty)
                {
                    await BootstrapEmptyDatabaseAsync(connection, baseline, cancellationToken);
                }
                else if (baselineState.IsEligibleExistingDatabase)
                {
                    await RegisterExistingLegacyBaselineAsync(
                        connection,
                        baseline,
                        cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Existing PostgreSQL database is not eligible for automatic legacy baseline registration; " +
                        baselineState.GetFailureDiagnostic() + ". No legacy schema SQL was executed.");
                }
            }

            var applied = await ReadAppliedMigrationsAsync(connection, cancellationToken);
            ValidateAppliedMigrations(applied);

            foreach (var migration in migrations)
            {
                if (applied.ContainsKey(migration.Id))
                {
                    Console.WriteLine($"Skipping applied PostgreSQL migration {migration.Id}");
                    continue;
                }

                if (migration.IsLegacyBaseline)
                {
                    throw new InvalidOperationException(
                        $"PostgreSQL migration history exists without required baseline '{migration.Id}'. " +
                        "This indicates a partial or unknown initialization; no legacy schema SQL was replayed.");
                }

                await ApplyPendingMigrationAsync(connection, migration, cancellationToken);
                applied.Add(
                    migration.Id,
                    new AppliedPostgresSchemaMigration(
                        migration.Id,
                        migration.SemanticChecksum,
                        DateTimeOffset.UtcNow,
                        migration.Details));
            }

            Console.WriteLine("PostgreSQL migrations processed");
        }
        catch (PostgresException exception)
        {
            var diagnostic = GetSafeExceptionDiagnostic(exception);
            Console.WriteLine($"PostgreSQL schema initialization failed: {diagnostic}");
            throw new InvalidOperationException(
                $"PostgreSQL schema initialization failed: {diagnostic}. " +
                "The database error text was suppressed to prevent migration SQL or data values from entering host logs.");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"PostgreSQL schema initialization failed: {GetSafeExceptionDiagnostic(exception)}");
            throw;
        }
        finally
        {
            if (lockAcquired && connection.State == ConnectionState.Open)
            {
                await ReleaseMigrationLockAsync(connection);
            }
        }
    }

    public static IReadOnlyList<string> SplitSchemaSqlStatements(string schemaSql)
    {
        var statements = new List<string>();
        var currentStatement = new StringBuilder();
        var inSingleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;
        string? dollarQuoteTag = null;

        for (var index = 0; index < schemaSql.Length; index++)
        {
            var current = schemaSql[index];
            var next = index + 1 < schemaSql.Length ? schemaSql[index + 1] : '\0';
            currentStatement.Append(current);

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    currentStatement.Append(next);
                    index++;
                    inBlockComment = false;
                }

                continue;
            }

            if (dollarQuoteTag is not null)
            {
                if (current == '$' && MatchesAt(schemaSql, index, dollarQuoteTag))
                {
                    AppendTagRemainder(schemaSql, currentStatement, index, dollarQuoteTag);
                    index += dollarQuoteTag.Length - 1;
                    dollarQuoteTag = null;
                }

                continue;
            }

            if (inSingleQuote)
            {
                if (current == '\'' && next == '\'')
                {
                    currentStatement.Append(next);
                    index++;
                }
                else if (current == '\'')
                {
                    inSingleQuote = false;
                }

                continue;
            }

            if (current == '-' && next == '-')
            {
                currentStatement.Append(next);
                index++;
                inLineComment = true;
                continue;
            }

            if (current == '/' && next == '*')
            {
                currentStatement.Append(next);
                index++;
                inBlockComment = true;
                continue;
            }

            if (current == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (current == '$')
            {
                var tag = TryReadDollarQuoteTag(schemaSql, index);
                if (tag is not null)
                {
                    AppendTagRemainder(schemaSql, currentStatement, index, tag);
                    index += tag.Length - 1;
                    dollarQuoteTag = tag;
                    continue;
                }
            }

            if (current == ';')
            {
                AddStatement(statements, currentStatement);
            }
        }

        AddStatement(statements, currentStatement);
        return statements;
    }

    public static string? TryReadCreateIndexIfNotExistsName(string statement)
    {
        var match = Regex.Match(
            statement,
            @"^\s*CREATE\s+(?:UNIQUE\s+)?INDEX\s+(?:CONCURRENTLY\s+)?IF\s+NOT\s+EXISTS\s+([a-zA-Z_][a-zA-Z0-9_]*)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task<bool> TryAcquireMigrationLockAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            "SELECT pg_try_advisory_lock(@LockKey);",
            connection,
            transaction: null);
        command.Parameters.AddWithValue("LockKey", AdvisoryLockKey);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task ReleaseMigrationLockAsync(NpgsqlConnection connection)
    {
        try
        {
            await using var command = CreateCommand(
                "SELECT pg_advisory_unlock(@LockKey);",
                connection,
                transaction: null);
            command.Parameters.AddWithValue("LockKey", AdvisoryLockKey);
            _ = await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"PostgreSQL migration advisory lock release failed: {GetSafeExceptionDiagnostic(exception)}");
        }
    }

    private static string GetSafeExceptionDiagnostic(Exception exception)
    {
        return exception switch
        {
            InvalidOperationException or ArgumentException =>
                $"{exception.GetType().Name}: {exception.Message}",
            PostgresException postgresException =>
                $"{exception.GetType().Name} (SQLSTATE {postgresException.SqlState})",
            _ => exception.GetType().Name
        };
    }

    private static async Task BootstrapEmptyDatabaseAsync(
        NpgsqlConnection connection,
        PostgresSchemaMigration baseline,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Bootstrapping empty PostgreSQL database with migration {baseline.Id}");
        await ExecuteNonQueryAsync(
            connection,
            transaction: null,
            CreateHistoryTableSql,
            cancellationToken);

        foreach (var statement in SplitSchemaSqlStatements(baseline.Sql))
        {
            await ExecuteNonQueryAsync(
                connection,
                transaction: null,
                statement,
                cancellationToken,
                MigrationCommandTimeoutSeconds);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await InsertHistoryAsync(connection, transaction, baseline, "empty-database-bootstrap", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Console.WriteLine($"Applied PostgreSQL migration {baseline.Id}");
    }

    private static async Task RegisterExistingLegacyBaselineAsync(
        NpgsqlConnection connection,
        PostgresSchemaMigration baseline,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var state = await ReadLegacyBaselineStateAsync(connection, transaction, cancellationToken);
        if (!state.IsEligibleExistingDatabase)
        {
            throw new InvalidOperationException(
                "Existing PostgreSQL database changed during legacy baseline verification; " +
                state.GetFailureDiagnostic() + ". No baseline was recorded.");
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            CreateHistoryTableSql,
            cancellationToken);
        await InsertHistoryAsync(
            connection,
            transaction,
            baseline,
            $"existing-database-baseline; prior-build={PostgresSchemaMigrationCatalog.ExpectedExistingServiceBuild}",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Console.WriteLine($"Recorded existing PostgreSQL baseline {baseline.Id} without replaying schema SQL");
    }

    private static async Task<PostgresLegacyBaselineState> ReadLegacyBaselineStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        const string objectInventorySql = """
WITH target_namespace AS (
    SELECT oid
    FROM pg_namespace
    WHERE nspname = 'public'
),
application_objects AS (
    SELECT cls.oid
    FROM pg_class cls
    WHERE cls.relnamespace = (SELECT oid FROM target_namespace)
      AND cls.relname <> 'schema_migration_history'
    UNION ALL
    SELECT routine.oid
    FROM pg_proc routine
    WHERE routine.pronamespace = (SELECT oid FROM target_namespace)
    UNION ALL
    SELECT type_row.oid
    FROM pg_type type_row
    WHERE type_row.typnamespace = (SELECT oid FROM target_namespace)
    UNION ALL
    SELECT collation_row.oid
    FROM pg_collation collation_row
    WHERE collation_row.collnamespace = (SELECT oid FROM target_namespace)
    UNION ALL
    SELECT conversion_row.oid
    FROM pg_conversion conversion_row
    WHERE conversion_row.connamespace = (SELECT oid FROM target_namespace)
    UNION ALL
    SELECT operator_row.oid
    FROM pg_operator operator_row
    WHERE operator_row.oprnamespace = (SELECT oid FROM target_namespace)
    UNION ALL
    SELECT operator_class.oid
    FROM pg_opclass operator_class
    WHERE operator_class.opcnamespace = (SELECT oid FROM target_namespace)
    UNION ALL
    SELECT operator_family.oid
    FROM pg_opfamily operator_family
    WHERE operator_family.opfnamespace = (SELECT oid FROM target_namespace)
    UNION ALL
    SELECT statistic_row.oid
    FROM pg_statistic_ext statistic_row
    WHERE statistic_row.stxnamespace = (SELECT oid FROM target_namespace)
    UNION ALL
    SELECT text_search_config.oid
    FROM pg_ts_config text_search_config
    WHERE text_search_config.cfgnamespace = (SELECT oid FROM target_namespace)
    UNION ALL
    SELECT text_search_dictionary.oid
    FROM pg_ts_dict text_search_dictionary
    WHERE text_search_dictionary.dictnamespace = (SELECT oid FROM target_namespace)
    UNION ALL
    SELECT text_search_parser.oid
    FROM pg_ts_parser text_search_parser
    WHERE text_search_parser.prsnamespace = (SELECT oid FROM target_namespace)
    UNION ALL
    SELECT text_search_template.oid
    FROM pg_ts_template text_search_template
    WHERE text_search_template.tmplnamespace = (SELECT oid FROM target_namespace)
)
SELECT
    (SELECT count(*)::integer FROM application_objects),
    to_regclass('public.service_heartbeats') IS NOT NULL,
    to_regclass('public.schema_data_migrations') IS NOT NULL,
    to_regclass('public.dashboard_projection_control') IS NOT NULL,
    to_regclass('public.paper_copied_trader_performance_refresh_queue') IS NOT NULL
;
""";

        int applicationObjectCount;
        bool hasHeartbeatRelation;
        bool hasDataMigrationRelation;
        bool hasDashboardControlRelation;
        bool hasCopiedPerformanceQueueRelation;
        await using (var command = CreateCommand(objectInventorySql, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("PostgreSQL legacy baseline object inventory returned no row.");
            }

            applicationObjectCount = reader.GetInt32(0);
            hasHeartbeatRelation = reader.GetBoolean(1);
            hasDataMigrationRelation = reader.GetBoolean(2);
            hasDashboardControlRelation = reader.GetBoolean(3);
            hasCopiedPerformanceQueueRelation = reader.GetBoolean(4);
        }

        if (applicationObjectCount == 0)
        {
            return new PostgresLegacyBaselineState(
                0,
                HasExpectedHeartbeat: false,
                HasRequiredDataMigration: false,
                HasDashboardControlRelation: false,
                HasCopiedPerformanceQueueRelation: false,
                HasDashboardTerminalTrigger: false,
                HasCopiedPerformanceTerminalTrigger: false);
        }

        var hasExpectedHeartbeat = hasHeartbeatRelation && await ScalarExistsAsync(
            connection,
            transaction,
            """
SELECT EXISTS (
    SELECT 1
    FROM public.service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
      AND version LIKE @ExpectedVersionPrefix
);
""",
            cancellationToken,
            command => command.Parameters.AddWithValue(
                "ExpectedVersionPrefix",
                $"info=1.0.0+{PostgresSchemaMigrationCatalog.ExpectedExistingServiceBuild};%"));

        var hasRequiredDataMigration = hasDataMigrationRelation && await ScalarExistsAsync(
            connection,
            transaction,
            """
SELECT EXISTS (
    SELECT 1
    FROM public.schema_data_migrations
    WHERE migration_key = @MigrationKey
);
""",
            cancellationToken,
            command => command.Parameters.AddWithValue(
                "MigrationKey",
                PostgresSchemaMigrationCatalog.RequiredDataMigrationKey));

        const string terminalTriggerSql = """
SELECT EXISTS (
    SELECT 1
    FROM pg_trigger trigger_row
    JOIN pg_class relation_row ON relation_row.oid = trigger_row.tgrelid
    JOIN pg_namespace relation_namespace ON relation_namespace.oid = relation_row.relnamespace
    JOIN pg_proc function_row ON function_row.oid = trigger_row.tgfoid
    JOIN pg_namespace function_namespace ON function_namespace.oid = function_row.pronamespace
    WHERE relation_namespace.nspname = 'public'
      AND function_namespace.nspname = 'public'
      AND relation_row.relname = @RelationName
      AND trigger_row.tgname = @TriggerName
      AND function_row.proname = @FunctionName
      AND trigger_row.tgtype = @TriggerType
      AND trigger_row.tgenabled = 'O'
      AND NOT trigger_row.tgisinternal
      AND (@OldTableName = '' OR trigger_row.tgoldtable = @OldTableName)
);
""";

        var hasDashboardTerminalTrigger = await ScalarExistsAsync(
            connection,
            transaction,
            terminalTriggerSql,
            cancellationToken,
            command =>
            {
                command.Parameters.AddWithValue("RelationName", "live_orders");
                command.Parameters.AddWithValue("TriggerName", "trg_dashboard_projection_live_order");
                command.Parameters.AddWithValue("FunctionName", "queue_dashboard_live_order_projection_event");
                command.Parameters.AddWithValue("TriggerType", (short)29);
                command.Parameters.AddWithValue("OldTableName", string.Empty);
            });

        var hasCopiedPerformanceTerminalTrigger = await ScalarExistsAsync(
            connection,
            transaction,
            terminalTriggerSql,
            cancellationToken,
            command =>
            {
                command.Parameters.AddWithValue("RelationName", "paper_positions");
                command.Parameters.AddWithValue(
                    "TriggerName",
                    "trg_paper_copied_trader_performance_position_delete");
                command.Parameters.AddWithValue(
                    "FunctionName",
                    "queue_paper_copied_trader_performance_position_delete");
                command.Parameters.AddWithValue("TriggerType", (short)8);
                command.Parameters.AddWithValue("OldTableName", "old_paper_positions");
            });

        return new PostgresLegacyBaselineState(
            applicationObjectCount,
            hasExpectedHeartbeat,
            hasRequiredDataMigration,
            hasDashboardControlRelation,
            hasCopiedPerformanceQueueRelation,
            hasDashboardTerminalTrigger,
            hasCopiedPerformanceTerminalTrigger);
    }

    private async Task ApplyPendingMigrationAsync(
        NpgsqlConnection connection,
        PostgresSchemaMigration migration,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Applying PostgreSQL migration {migration.Id}");
        if (migration.Transactional)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                migration.Sql,
                cancellationToken,
                MigrationCommandTimeoutSeconds);
            await InsertHistoryAsync(
                connection,
                transaction,
                migration,
                migration.Details,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            if (migration.CompletionCheckSql is null)
            {
                throw new InvalidOperationException(
                    $"Non-transactional migration '{migration.Id}' has no completion check.");
            }

            var complete = await ScalarExistsAsync(
                connection,
                transaction: null,
                migration.CompletionCheckSql,
                cancellationToken);
            if (!complete)
            {
                await ExecuteNonQueryAsync(
                    connection,
                    transaction: null,
                    migration.Sql,
                    cancellationToken,
                    MigrationCommandTimeoutSeconds);
                complete = await ScalarExistsAsync(
                    connection,
                    transaction: null,
                    migration.CompletionCheckSql,
                    cancellationToken);
            }

            if (!complete)
            {
                throw new InvalidOperationException(
                    $"Non-transactional migration '{migration.Id}' did not satisfy its completion check.");
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await InsertHistoryAsync(
                connection,
                transaction,
                migration,
                migration.Details,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        Console.WriteLine($"Applied PostgreSQL migration {migration.Id}");
    }

    private void ValidateAppliedMigrations(
        IReadOnlyDictionary<string, AppliedPostgresSchemaMigration> applied)
    {
        var catalogById = migrations.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var appliedMigration in applied.Values)
        {
            if (!catalogById.TryGetValue(appliedMigration.Id, out var catalogMigration))
            {
                throw new InvalidOperationException(
                    $"Database contains unknown PostgreSQL migration '{appliedMigration.Id}'.");
            }

            if (!string.Equals(
                    appliedMigration.SemanticChecksum,
                    catalogMigration.SemanticChecksum,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"PostgreSQL migration checksum mismatch for '{appliedMigration.Id}'. " +
                    $"Expected {catalogMigration.SemanticChecksum}, stored {appliedMigration.SemanticChecksum}.");
            }
        }

        var missingEarlierMigration = false;
        foreach (var migration in migrations)
        {
            if (applied.ContainsKey(migration.Id))
            {
                if (missingEarlierMigration)
                {
                    throw new InvalidOperationException(
                        $"PostgreSQL migration history has an applied migration after a missing earlier migration: '{migration.Id}'.");
                }
            }
            else
            {
                missingEarlierMigration = true;
            }
        }
    }

    private static async Task<Dictionary<string, AppliedPostgresSchemaMigration>> ReadAppliedMigrationsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, AppliedPostgresSchemaMigration>(StringComparer.Ordinal);
        await using var command = CreateCommand(
            $"""
SELECT migration_id, semantic_checksum, applied_at_utc, details
FROM {QualifiedHistoryRelationName}
ORDER BY migration_id;
""",
            connection,
            transaction: null);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var migration = new AppliedPostgresSchemaMigration(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetString(3));
            if (!result.TryAdd(migration.Id, migration))
            {
                throw new InvalidOperationException(
                    $"Duplicate PostgreSQL migration history id '{migration.Id}'.");
            }
        }

        return result;
    }

    private static async Task InsertHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PostgresSchemaMigration migration,
        string details,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            $"""
INSERT INTO {QualifiedHistoryRelationName} (
    migration_id,
    semantic_checksum,
    applied_at_utc,
    details)
VALUES (
    @MigrationId,
    @SemanticChecksum,
    clock_timestamp(),
    @Details);
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("MigrationId", migration.Id);
        command.Parameters.AddWithValue("SemanticChecksum", migration.SemanticChecksum);
        command.Parameters.AddWithValue("Details", details);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> SchemaRelationExistsAsync(
        NpgsqlConnection connection,
        string relationName,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT EXISTS (
    SELECT 1
    FROM pg_class cls
    JOIN pg_namespace ns ON ns.oid = cls.relnamespace
    WHERE ns.nspname = 'public'
      AND cls.relname = @RelationName
      AND cls.relkind IN ('r', 'p')
);
""";

        return await ScalarExistsAsync(
            connection,
            transaction,
            sql,
            cancellationToken,
            command => command.Parameters.AddWithValue("RelationName", relationName));
    }

    private static async Task<bool> ScalarExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        Action<NpgsqlCommand>? configure = null)
    {
        await using var command = CreateCommand(sql, connection, transaction);
        configure?.Invoke(command);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        int commandTimeoutSeconds = MetadataCommandTimeoutSeconds)
    {
        await using var command = CreateCommand(
            sql,
            connection,
            transaction,
            commandTimeoutSeconds);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlCommand CreateCommand(
        string sql,
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        int commandTimeoutSeconds = MetadataCommandTimeoutSeconds)
    {
        return new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
    }

    private static void AddStatement(List<string> statements, StringBuilder currentStatement)
    {
        var statement = currentStatement.ToString().Trim();
        currentStatement.Clear();
        if (!string.IsNullOrWhiteSpace(statement))
        {
            statements.Add(statement);
        }
    }

    private static void AppendTagRemainder(
        string schemaSql,
        StringBuilder currentStatement,
        int tagStartIndex,
        string tag)
    {
        for (var offset = 1; offset < tag.Length; offset++)
        {
            currentStatement.Append(schemaSql[tagStartIndex + offset]);
        }
    }

    private static bool MatchesAt(string value, int startIndex, string expected)
    {
        if (startIndex + expected.Length > value.Length)
        {
            return false;
        }

        for (var offset = 0; offset < expected.Length; offset++)
        {
            if (value[startIndex + offset] != expected[offset])
            {
                return false;
            }
        }

        return true;
    }

    private static string? TryReadDollarQuoteTag(string value, int startIndex)
    {
        if (value[startIndex] != '$')
        {
            return null;
        }

        for (var index = startIndex + 1; index < value.Length; index++)
        {
            var current = value[index];
            if (current == '$')
            {
                return value[startIndex..(index + 1)];
            }

            if (!char.IsLetterOrDigit(current) && current != '_')
            {
                return null;
            }
        }

        return null;
    }
}
