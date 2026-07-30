using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace ReferenceAverageHistoryCorrectionBackupEvidence;

internal static class Program
{
    private const string SourceEnvironment = "POLYCOPYTRADER_POSTGRES_CONNECTION";
    private const string RestoredEnvironment = "REFERENCE_AVERAGE_RESTORE_CONNECTION";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            await SealAsync(options, CancellationToken.None);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL CLOSED: " + exception.Message);
            return 2;
        }
    }

    private static async Task SealAsync(Options options, CancellationToken cancellationToken)
    {
        RequireEmptyDirectory(options.EvidenceDirectory, "evidence output");
        if (!Directory.Exists(options.DumpDirectory) ||
            !File.Exists(Path.Combine(options.DumpDirectory, "toc.dat")))
        {
            throw new InvalidDataException("Existing directory dump or toc.dat is missing.");
        }
        if (options.DumpExitCode != 0 || options.DumpCompletedAtUtc < options.DumpStartedAtUtc ||
            options.DumpCompletedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            throw new InvalidDataException("Supplied pg_dump completion/exit evidence is invalid.");
        }
        if (!File.Exists(options.DumpLogPath))
        {
            throw new FileNotFoundException("pg_dump log is missing.", options.DumpLogPath);
        }

        var sourceBuilder = ReadConnection(SourceEnvironment);
        var restoredBuilder = ReadConnection(RestoredEnvironment);
        ValidateConnections(options, sourceBuilder, restoredBuilder);
        await RequireEmptyRestoredDatabaseAsync(restoredBuilder.ConnectionString, cancellationToken);

        var evidenceInsideDump = Path.Combine(options.DumpDirectory, "evidence");
        if (Directory.Exists(evidenceInsideDump))
        {
            throw new InvalidOperationException(
                "Dump already contains an evidence directory; blind resealing is forbidden.");
        }
        Directory.CreateDirectory(evidenceInsideDump);
        var copiedDumpLog = Path.Combine(evidenceInsideDump, "pg-dump.log");
        File.Copy(options.DumpLogPath, copiedDumpLog, overwrite: false);

        var pgDumpVersion = await ReadVersionAsync(Path.Combine(options.PostgresBinDirectory, "pg_dump.exe"),
            cancellationToken);
        var pgRestorePath = Path.Combine(options.PostgresBinDirectory, "pg_restore.exe");
        var pgRestoreVersion = await ReadVersionAsync(pgRestorePath, cancellationToken);
        RequirePostgres18(pgDumpVersion, "pg_dump");
        RequirePostgres18(pgRestoreVersion, "pg_restore");

        var restoreListPath = Path.Combine(evidenceInsideDump, "pg-restore-list.txt");
        var listResult = await RunProcessAsync(pgRestorePath, ["--list", options.DumpDirectory], null,
            cancellationToken);
        if (listResult.ExitCode != 0 || string.IsNullOrWhiteSpace(listResult.StandardOutput))
        {
            throw new InvalidDataException(
                $"pg_restore --list failed with exit {listResult.ExitCode}: {listResult.StandardError}");
        }
        await File.WriteAllTextAsync(restoreListPath, listResult.StandardOutput,
            new UTF8Encoding(false), cancellationToken);

        var sourceBefore = await CaptureDatabaseAsync(sourceBuilder.ConnectionString, cancellationToken);
        var restoreStarted = DateTimeOffset.UtcNow;
        var restoreResult = await RunProcessAsync(pgRestorePath,
            ["--exit-on-error", "--no-owner", "--no-privileges", "--jobs=2", options.DumpDirectory],
            BuildPgEnvironment(restoredBuilder), cancellationToken);
        var restoreCompleted = DateTimeOffset.UtcNow;
        var restoreLogPath = Path.Combine(evidenceInsideDump, "pg-restore.log");
        await File.WriteAllTextAsync(restoreLogPath,
            $"stdout:{Environment.NewLine}{restoreResult.StandardOutput}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{restoreResult.StandardError}",
            new UTF8Encoding(false), cancellationToken);
        if (restoreResult.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"Restore rehearsal failed with exit {restoreResult.ExitCode}; restored DB was left intact for inspection.");
        }

        var restored = await CaptureDatabaseAsync(restoredBuilder.ConnectionString, cancellationToken);
        var sourceAfter = await CaptureDatabaseAsync(sourceBuilder.ConnectionString, cancellationToken);
        RequireExactSnapshot(sourceBefore, sourceAfter, "source before/after restore rehearsal");
        RequireExactSnapshot(sourceAfter, restored, "source vs independently restored database");

        var schemaLines = sourceAfter.SchemaLines.Order(StringComparer.Ordinal).ToArray();
        var canonicalText = string.Join("\n", schemaLines) + "\n";
        var schemaFingerprint = Sha256(Encoding.UTF8.GetBytes(canonicalText));
        var capturedAt = DateTimeOffset.UtcNow;
        var schemaPath = Path.Combine(options.EvidenceDirectory, "full-backup-schema.json");
        await WriteJsonAsync(schemaPath, new
        {
            schema_version = 1,
            source_host = options.SourceHost,
            source_port = options.SourcePort,
            source_database = options.SourceDatabase,
            captured_at_utc = capturedAt,
            schema_fingerprint_sha256 = schemaFingerprint,
            public_tables = sourceAfter.Tables.Select(table => new { schema = "public", table }),
            canonical_schema_lines = schemaLines
        }, cancellationToken);
        var schemaManifestSha = Sha256File(schemaPath);

        var rowCountPath = Path.Combine(options.EvidenceDirectory,
            "full-backup-restored-row-counts.json");
        await WriteJsonAsync(rowCountPath, new
        {
            schema_version = 1,
            source_schema_manifest_sha256 = schemaManifestSha,
            schema_fingerprint_sha256 = schemaFingerprint,
            tables = restored.RowCounts.Select(pair => new
            {
                schema = "public",
                table = pair.Key,
                row_count = pair.Value
            })
        }, cancellationToken);
        var rowCountManifestSha = Sha256File(rowCountPath);

        var dumpFiles = Directory.EnumerateFiles(options.DumpDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new DumpFile(
                NormalizeRelative(options.DumpDirectory, path),
                new FileInfo(path).Length,
                Sha256File(path)))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (dumpFiles.Length == 0)
        {
            throw new InvalidDataException("Dump directory is empty after evidence capture.");
        }
        var hashManifestPath = Path.Combine(options.EvidenceDirectory, "full-backup-sha256.txt");
        var hashText = string.Concat(dumpFiles.Select(file => $"{file.Sha256}  {file.RelativePath}\n"));
        await File.WriteAllTextAsync(hashManifestPath, hashText, new UTF8Encoding(false), cancellationToken);
        var hashManifestSha = Sha256File(hashManifestPath);

        var restoreListRelative = NormalizeRelative(options.DumpDirectory, restoreListPath);
        var tocEntryCount = File.ReadLines(restoreListPath)
            .LongCount(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith(';'));
        var metadataPath = Path.Combine(options.EvidenceDirectory, "full-backup-metadata.json");
        await WriteJsonAsync(metadataPath, new
        {
            schema_version = 1,
            backup_started_at_utc = options.DumpStartedAtUtc,
            backup_completed_at_utc = options.DumpCompletedAtUtc,
            pg_dump_version = pgDumpVersion,
            source_server_version = sourceAfter.ServerVersion,
            source_host = options.SourceHost,
            source_port = options.SourcePort,
            source_database = options.SourceDatabase,
            format = "directory",
            jobs = 2,
            compression = "0",
            pg_dump_exit_code = options.DumpExitCode,
            backup_file_count = dumpFiles.LongLength,
            backup_total_bytes = dumpFiles.Sum(file => file.Length),
            toc_entry_count = tocEntryCount,
            pg_restore_list_relative_path = restoreListRelative,
            pg_restore_list_sha256 = Sha256File(restoreListPath),
            pg_dump_log_relative_path = NormalizeRelative(options.DumpDirectory, copiedDumpLog),
            pg_dump_log_sha256 = Sha256File(copiedDumpLog),
            backup_hash_manifest_sha256 = hashManifestSha,
            source_schema_manifest_sha256 = schemaManifestSha,
            source_schema_fingerprint_sha256 = schemaFingerprint
        }, cancellationToken);
        var metadataSha = Sha256File(metadataPath);

        var restoreEvidencePath = Path.Combine(options.EvidenceDirectory,
            "full-backup-restore-evidence.json");
        await WriteJsonAsync(restoreEvidencePath, new
        {
            schema_version = 1,
            backup_manifest_sha256 = metadataSha,
            backup_hash_manifest_sha256 = hashManifestSha,
            source_schema_manifest_sha256 = schemaManifestSha,
            schema_fingerprint_sha256 = schemaFingerprint,
            restored_row_count_manifest_sha256 = rowCountManifestSha,
            restore_started_at_utc = restoreStarted,
            restore_completed_at_utc = restoreCompleted,
            tested_at_utc = capturedAt,
            restore_completed = true,
            restore_exit_code = restoreResult.ExitCode,
            pg_restore_version = pgRestoreVersion,
            source_host = options.SourceHost,
            source_port = options.SourcePort,
            source_database = options.SourceDatabase,
            restored_host = restoredBuilder.Host,
            restored_port = restoredBuilder.Port,
            restored_database = restoredBuilder.Database,
            restored_public_table_count = restored.Tables.Count,
            pg_restore_log_relative_path = NormalizeRelative(options.DumpDirectory, restoreLogPath),
            pg_restore_log_sha256 = Sha256File(restoreLogPath),
            source_snapshot_schema_sha256 = Sha256(
                Encoding.UTF8.GetBytes(string.Join("\n", sourceAfter.SchemaLines) + "\n")),
            source_and_restored_row_counts_equal = true
        }, cancellationToken);

        Console.WriteLine("SEALED EXISTING DUMP; no pg_dump was started by this generator.");
        Console.WriteLine("Dump directory: " + options.DumpDirectory);
        Console.WriteLine("Evidence directory: " + options.EvidenceDirectory);
        Console.WriteLine("Source/restored canonical schema and all-public row counts: EXACT MATCH.");
        Console.WriteLine("Use these preparation inputs:");
        Console.WriteLine("  --full-backup-hash-manifest " + hashManifestPath);
        Console.WriteLine("  --full-backup-metadata-manifest " + metadataPath);
        Console.WriteLine("  --full-backup-restore-evidence " + restoreEvidencePath);
        Console.WriteLine("  --full-backup-restored-row-count-manifest " + rowCountPath);
        Console.WriteLine("  --full-backup-schema-manifest " + schemaPath);
    }

    private static async Task<DatabaseSnapshot> CaptureDatabaseAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        await using (var configure = new NpgsqlCommand(
                         "SET TRANSACTION READ ONLY; SET LOCAL TIME ZONE 'UTC'; " +
                         "SET LOCAL search_path TO pg_catalog, public;", connection, transaction))
        {
            await configure.ExecuteNonQueryAsync(cancellationToken);
        }

        var serverVersion = await ScalarStringAsync(connection, transaction, "SHOW server_version;",
            cancellationToken);
        RequirePostgres18(serverVersion, "server");
        var tables = new List<string>();
        await using (var command = new NpgsqlCommand("""
                         SELECT table_row.relname
                         FROM pg_class table_row
                         JOIN pg_namespace schema_row ON schema_row.oid = table_row.relnamespace
                         WHERE schema_row.nspname = 'public' AND table_row.relkind IN ('r','p')
                         ORDER BY table_row.relname COLLATE "C";
                         """, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }
        if (tables.Count == 0 || tables.Distinct(StringComparer.Ordinal).Count() != tables.Count)
        {
            throw new InvalidDataException("Database contains no unique public ordinary/partitioned tables.");
        }

        var lines = new List<string>();
        await using (var command = new NpgsqlCommand(CanonicalSchemaSql, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add(reader.GetString(0));
            }
        }
        if (lines.Count == 0 || lines.Distinct(StringComparer.Ordinal).Count() != lines.Count)
        {
            throw new InvalidDataException("Canonical schema query returned an empty or duplicate line set.");
        }
        lines.Sort(StringComparer.Ordinal);

        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            if (!Regex.IsMatch(table, "^[A-Za-z_][A-Za-z0-9_$]*$", RegexOptions.CultureInvariant))
            {
                throw new InvalidDataException($"Unsupported public table identifier: {table}.");
            }
            var quoted = '"' + table.Replace("\"", "\"\"") + '"';
            var count = await ScalarLongAsync(connection, transaction,
                $"SELECT count(*)::bigint FROM public.{quoted};", cancellationToken);
            counts.Add(table, count);
        }
        await transaction.CommitAsync(cancellationToken);
        return new DatabaseSnapshot(serverVersion, tables, lines, counts);
    }

    private static void RequireExactSnapshot(DatabaseSnapshot expected, DatabaseSnapshot actual, string label)
    {
        if (!expected.Tables.SequenceEqual(actual.Tables, StringComparer.Ordinal) ||
            !expected.SchemaLines.SequenceEqual(actual.SchemaLines, StringComparer.Ordinal) ||
            !expected.RowCounts.SequenceEqual(actual.RowCounts))
        {
            throw new InvalidDataException($"Canonical schema/table row-count mismatch: {label}.");
        }
    }

    private static async Task RequireEmptyRestoredDatabaseAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT count(*)::integer
            FROM pg_class relation
            JOIN pg_namespace schema_row ON schema_row.oid = relation.relnamespace
            WHERE schema_row.nspname = 'public';
            """, connection);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (count != 0)
        {
            throw new InvalidOperationException(
                $"Restored rehearsal database is not empty: public schema contains {count:N0} relations.");
        }
    }

    private static void ValidateConnections(
        Options options,
        NpgsqlConnectionStringBuilder source,
        NpgsqlConnectionStringBuilder restored)
    {
        var sourceHost = source.Host ?? string.Empty;
        var sourceDatabase = source.Database ?? string.Empty;
        var restoredHost = restored.Host ?? string.Empty;
        var restoredDatabase = restored.Database ?? string.Empty;
        if (!sourceHost.Equals(options.SourceHost, StringComparison.Ordinal) ||
            source.Port != options.SourcePort ||
            !sourceDatabase.Equals(options.SourceDatabase, StringComparison.Ordinal) ||
            !options.SourceHost.Equals("192.168.0.101", StringComparison.Ordinal) ||
            options.SourcePort != 5432 ||
            !options.SourceDatabase.Equals("polycopytrader", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Source connection does not match the pinned production identity.");
        }
        var loopback = restoredHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                       System.Net.IPAddress.TryParse(restoredHost, out var restoredAddress) &&
                       System.Net.IPAddress.IsLoopback(restoredAddress);
        if (!loopback || sourceHost.Equals(restoredHost, StringComparison.OrdinalIgnoreCase) &&
            source.Port == restored.Port ||
            string.IsNullOrWhiteSpace(restoredDatabase) ||
            !restoredDatabase.StartsWith("reference_history_restore_", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Restore rehearsal must target a loopback database named reference_history_restore_<unique>.");
        }
    }

    private static NpgsqlConnectionStringBuilder ReadConnection(string environmentName)
    {
        var value = Environment.GetEnvironmentVariable(environmentName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required environment variable {environmentName} is missing.");
        }
        return new NpgsqlConnectionStringBuilder(value)
        {
            ApplicationName = "ReferenceAverageHistoryCorrectionBackupEvidence",
            SearchPath = "pg_catalog,public",
            Timezone = "UTC"
        };
    }

    private static Dictionary<string, string> BuildPgEnvironment(NpgsqlConnectionStringBuilder builder) =>
        new(StringComparer.Ordinal)
        {
            ["PGHOST"] = builder.Host ?? string.Empty,
            ["PGPORT"] = builder.Port.ToString(CultureInfo.InvariantCulture),
            ["PGDATABASE"] = builder.Database ?? string.Empty,
            ["PGUSER"] = builder.Username ?? string.Empty,
            ["PGPASSWORD"] = builder.Password ?? string.Empty,
            ["PGSSLMODE"] = builder.SslMode.ToString().ToLowerInvariant()
        };

    private static async Task<string> ReadVersionAsync(string executable, CancellationToken cancellationToken)
    {
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("PostgreSQL executable is missing.", executable);
        }
        var result = await RunProcessAsync(executable, ["--version"], null, cancellationToken);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidDataException($"Could not read version from {executable}.");
        }
        return result.StandardOutput.Trim();
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                start.Environment[pair.Key] = pair.Value;
            }
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException(
            $"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static void RequirePostgres18(string version, string component)
    {
        if (!version.StartsWith("18.", StringComparison.Ordinal) &&
            !version.Contains("PostgreSQL) 18.", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{component} is not PostgreSQL 18: {version}.");
        }
    }

    private static async Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine;
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken);
    }

    private static void RequireEmptyDirectory(string path, string label)
    {
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new InvalidOperationException($"{label} must be unique and empty: {path}.");
        }
        Directory.CreateDirectory(path);
    }

    private static string NormalizeRelative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Sha256File(string path) => Sha256(File.ReadAllBytes(path));
    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task<string> ScalarStringAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken) =>
        Convert.ToString(await new NpgsqlCommand(sql, connection, transaction)
            .ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) ??
        throw new InvalidDataException("Scalar string query returned null.");

    private static async Task<long> ScalarLongAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken) =>
        Convert.ToInt64(await new NpgsqlCommand(sql, connection, transaction)
            .ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

    private const string CanonicalSchemaSql = """
        WITH tables AS (
            SELECT table_row.oid, table_row.relname,
                   jsonb_build_object(
                       'relkind', table_row.relkind,
                       'persistence', table_row.relpersistence,
                       'partition_key', pg_get_partkeydef(table_row.oid))::text AS definition
            FROM pg_class table_row
            JOIN pg_namespace schema_row ON schema_row.oid = table_row.relnamespace
            WHERE schema_row.nspname = 'public' AND table_row.relkind IN ('r','p')
        ), schema_rows AS (
            SELECT 'public.' || relname AS table_key, 'TABLE'::text AS object_kind,
                   ''::text AS object_identity, definition
            FROM tables
            UNION ALL
            SELECT 'public.' || table_row.relname, 'COLUMN',
                   lpad(column_row.attnum::text, 5, '0') || ':' || column_row.attname,
                   jsonb_build_object(
                       'type', format_type(column_row.atttypid, column_row.atttypmod),
                       'not_null', column_row.attnotnull,
                       'identity', column_row.attidentity,
                       'generated', column_row.attgenerated,
                       'default', pg_get_expr(default_row.adbin, default_row.adrelid),
                       'collation', CASE WHEN column_row.attcollation = 0 THEN NULL
                           ELSE column_row.attcollation::regcollation::text END)::text
            FROM tables table_row
            JOIN pg_attribute column_row ON column_row.attrelid = table_row.oid
              AND column_row.attnum > 0 AND NOT column_row.attisdropped
            LEFT JOIN pg_attrdef default_row ON default_row.adrelid = table_row.oid
              AND default_row.adnum = column_row.attnum
            UNION ALL
            SELECT 'public.' || table_row.relname, 'CONSTRAINT', constraint_row.conname,
                   jsonb_build_object(
                       'type', constraint_row.contype,
                       'definition', pg_get_constraintdef(constraint_row.oid, true),
                       'validated', constraint_row.convalidated,
                       'deferrable', constraint_row.condeferrable,
                       'deferred', constraint_row.condeferred)::text
            FROM tables table_row
            JOIN pg_constraint constraint_row ON constraint_row.conrelid = table_row.oid
            UNION ALL
            SELECT 'public.' || table_row.relname, 'INDEX', index_class.relname,
                   jsonb_build_object(
                       'definition', pg_get_indexdef(index_row.indexrelid),
                       'valid', index_row.indisvalid,
                       'ready', index_row.indisready)::text
            FROM tables table_row
            JOIN pg_index index_row ON index_row.indrelid = table_row.oid
            JOIN pg_class index_class ON index_class.oid = index_row.indexrelid
            UNION ALL
            SELECT 'public.' || table_row.relname, 'TRIGGER', trigger_row.tgname,
                   jsonb_build_object(
                       'definition', pg_get_triggerdef(trigger_row.oid, true),
                       'enabled', trigger_row.tgenabled)::text
            FROM tables table_row
            JOIN pg_trigger trigger_row ON trigger_row.tgrelid = table_row.oid
            WHERE NOT trigger_row.tgisinternal
        )
        SELECT table_key || E'\t' || object_kind || E'\t' || object_identity || E'\t' || definition
        FROM schema_rows
        ORDER BY table_key COLLATE "C", object_kind COLLATE "C", object_identity COLLATE "C", definition COLLATE "C";
        """;

    private sealed record DumpFile(string RelativePath, long Length, string Sha256);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
    private sealed record DatabaseSnapshot(
        string ServerVersion,
        IReadOnlyList<string> Tables,
        IReadOnlyList<string> SchemaLines,
        IReadOnlyDictionary<string, long> RowCounts);

    private sealed record Options(
        string SourceHost,
        int SourcePort,
        string SourceDatabase,
        string DumpDirectory,
        string DumpLogPath,
        DateTimeOffset DumpStartedAtUtc,
        DateTimeOffset DumpCompletedAtUtc,
        int DumpExitCode,
        string EvidenceDirectory,
        string PostgresBinDirectory)
    {
        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal) ||
                    !values.TryAdd(args[index], args[index + 1]))
                {
                    throw new ArgumentException("Every generator option must be a unique --name value pair.");
                }
            }
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "--source-host", "--source-port", "--source-database", "--dump-dir", "--dump-log",
                "--dump-started-at-utc", "--dump-completed-at-utc", "--dump-exit-code",
                "--evidence-dir", "--postgres-bin-dir"
            };
            var unknown = values.Keys.Except(allowed, StringComparer.Ordinal).ToArray();
            if (unknown.Length != 0)
            {
                throw new ArgumentException("Unknown generator option: " + string.Join(", ", unknown));
            }
            string Required(string name) => values.TryGetValue(name, out var value) &&
                                             !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : throw new ArgumentException("Required generator option missing: " + name);
            return new Options(
                Required("--source-host"),
                int.Parse(Required("--source-port"), NumberStyles.None, CultureInfo.InvariantCulture),
                Required("--source-database"),
                Path.GetFullPath(Required("--dump-dir")),
                Path.GetFullPath(Required("--dump-log")),
                DateTimeOffset.Parse(Required("--dump-started-at-utc"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal).ToUniversalTime(),
                DateTimeOffset.Parse(Required("--dump-completed-at-utc"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal).ToUniversalTime(),
                int.Parse(Required("--dump-exit-code"), NumberStyles.Integer, CultureInfo.InvariantCulture),
                Path.GetFullPath(Required("--evidence-dir")),
                Path.GetFullPath(Required("--postgres-bin-dir")));
        }
    }
}
