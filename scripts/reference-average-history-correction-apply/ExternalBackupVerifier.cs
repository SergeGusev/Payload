using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReferenceAverageHistoryCorrectionApply;

internal sealed record VerifiedExternalBackup(
    string BackupDirectory,
    string HashManifestPath,
    string HashManifestSha256,
    string MetadataManifestPath,
    string MetadataManifestSha256,
    string RestoreEvidencePath,
    string RestoreEvidenceSha256,
    string RestoredRowCountManifestPath,
    string RestoredRowCountManifestSha256,
    string SchemaManifestPath,
    string SchemaManifestSha256,
    string SchemaFingerprintSha256,
    IReadOnlyList<(string RelativePath, string Sha256)> Files);

internal sealed record CanonicalSchemaProof(
    string ManifestSha256,
    string FingerprintSha256,
    IReadOnlySet<string> PublicTables);

internal sealed record BackupMetadataProof(
    string RestoreListRelativePath,
    string DumpLogRelativePath,
    string ManifestSha256);

internal sealed record BackupHashManifestProof(
    IReadOnlyList<(string RelativePath, string Sha256)> Files,
    string ManifestSha256);

internal sealed record RowCountManifestProof(int TableCount, string ManifestSha256);

internal static class ExternalBackupVerifier
{
    public static VerifiedExternalBackup Verify(ToolOptions options)
    {
        var directory = options.FullBackupDirectory ?? throw new InvalidOperationException("Full backup path is missing.");
        var hashManifest = options.FullBackupHashManifestPath ??
                           throw new InvalidOperationException("Full backup hash manifest is missing.");
        var metadataManifest = options.FullBackupMetadataManifestPath ??
                               throw new InvalidOperationException("Full backup metadata manifest is missing.");
        var restoreEvidence = options.FullBackupRestoreEvidencePath ??
                              throw new InvalidOperationException("Full backup restore evidence is missing.");
        var rowCountManifest = options.FullBackupRestoredRowCountManifestPath ??
                               throw new InvalidOperationException("Restored row-count manifest is missing.");
        var schemaManifest = options.FullBackupSchemaManifestPath ??
                             throw new InvalidOperationException("Source schema manifest is missing.");
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Full backup directory does not exist: {directory}.");
        }

        var hashManifestProof = ParseHashManifest(hashManifest);
        var files = hashManifestProof.Files;
        var diskFiles = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelative(directory, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var listedFiles = files.Select(item => item.RelativePath).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (!diskFiles.SequenceEqual(listedFiles, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Full backup SHA manifest file set differs from the exact backup directory file set.");
        }

        foreach (var file in files)
        {
            var absolute = SafeCombine(directory, file.RelativePath);
            var actual = GraphPackageReader.Sha256File(absolute);
            if (!actual.Equals(file.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Full backup hash mismatch for {file.RelativePath}: expected {file.Sha256}, actual {actual}.");
            }
        }

        var schema = ValidateSchemaManifest(schemaManifest, options);
        var manifestSha = hashManifestProof.ManifestSha256;
        var metadata = ValidateBackupMetadata(metadataManifest, manifestSha, files, directory, schema, options);
        var metadataSha = metadata.ManifestSha256;
        var restoreListEvidence = files.Single(item =>
            item.RelativePath.Equals(metadata.RestoreListRelativePath, StringComparison.Ordinal));
        var tocTables = ReadPublicTableSetFromRestoreList(
            SafeCombine(directory, metadata.RestoreListRelativePath), restoreListEvidence.Sha256);
        RequireExactTableSet(schema.PublicTables, tocTables, "source schema manifest vs pg_restore TABLE entries");
        var rowCounts = ValidateRowCountManifest(rowCountManifest, schema);
        var restoreEvidenceSha = ValidateRestoreEvidence(restoreEvidence, manifestSha, metadataSha,
            rowCounts.ManifestSha256, rowCounts.TableCount, schema, files, directory, options);
        return new VerifiedExternalBackup(directory, hashManifest, manifestSha,
            metadataManifest, metadataSha, restoreEvidence,
            restoreEvidenceSha, rowCountManifest, rowCounts.ManifestSha256,
            schemaManifest, schema.ManifestSha256, schema.FingerprintSha256, files);
    }

    public static void CopyToDurable(VerifiedExternalBackup backup, string durableRoot)
    {
        var destination = Path.Combine(durableRoot, "full-backup");
        Directory.CreateDirectory(destination);
        foreach (var file in backup.Files)
        {
            var source = SafeCombine(backup.BackupDirectory, file.RelativePath);
            var target = SafeCombine(destination, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);
        }

        File.Copy(backup.HashManifestPath, Path.Combine(durableRoot, "full-backup-sha256.txt"), overwrite: false);
        File.Copy(backup.MetadataManifestPath, Path.Combine(durableRoot, "full-backup-metadata.json"), overwrite: false);
        File.Copy(backup.RestoreEvidencePath, Path.Combine(durableRoot, "full-backup-restore-evidence.json"),
            overwrite: false);
        File.Copy(backup.RestoredRowCountManifestPath,
            Path.Combine(durableRoot, "full-backup-restored-row-counts.json"), overwrite: false);
        File.Copy(backup.SchemaManifestPath,
            Path.Combine(durableRoot, "full-backup-schema.json"), overwrite: false);

        foreach (var file in backup.Files)
        {
            var copied = SafeCombine(destination, file.RelativePath);
            var actual = GraphPackageReader.Sha256File(copied);
            if (!actual.Equals(file.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Durable full-backup copy failed hash verification: {file.RelativePath}.");
            }
        }

        VerifyCopiedEvidenceHash(Path.Combine(durableRoot, "full-backup-sha256.txt"), backup.HashManifestSha256);
        VerifyCopiedEvidenceHash(Path.Combine(durableRoot, "full-backup-metadata.json"), backup.MetadataManifestSha256);
        VerifyCopiedEvidenceHash(Path.Combine(durableRoot, "full-backup-restore-evidence.json"), backup.RestoreEvidenceSha256);
        VerifyCopiedEvidenceHash(Path.Combine(durableRoot, "full-backup-restored-row-counts.json"),
            backup.RestoredRowCountManifestSha256);
        VerifyCopiedEvidenceHash(Path.Combine(durableRoot, "full-backup-schema.json"),
            backup.SchemaManifestSha256);
    }

    private static BackupHashManifestProof ParseHashManifest(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Full backup SHA manifest does not exist.", path);
        }

        var bytes = File.ReadAllBytes(path);
        var manifestSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var text = new UTF8Encoding(false, true).GetString(bytes);
        var result = new List<(string RelativePath, string Sha256)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Length < 67 || line[64] != ' ')
            {
                throw new InvalidDataException($"Invalid SHA manifest line {lineNumber}.");
            }

            var sha = GraphPackageReader.RequireSha256(line[..64], $"full backup manifest line {lineNumber}");
            var relative = line[65..].TrimStart('*').Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Contains("..", StringComparer.Ordinal) ||
                string.IsNullOrWhiteSpace(relative))
            {
                throw new InvalidDataException($"Unsafe backup relative path at line {lineNumber}: {relative}.");
            }

            relative = relative.Replace(Path.DirectorySeparatorChar, '/');
            if (!seen.Add(relative))
            {
                throw new InvalidDataException($"Duplicate backup manifest path: {relative}.");
            }

            result.Add((relative, sha));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("Full backup SHA manifest is empty.");
        }

        return new BackupHashManifestProof(
            result.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray(), manifestSha);
    }

    private static BackupMetadataProof ValidateBackupMetadata(
        string path,
        string hashManifestSha256,
        IReadOnlyList<(string RelativePath, string Sha256)> files,
        string backupDirectory,
        CanonicalSchemaProof schema,
        ToolOptions options)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Full backup metadata manifest does not exist.", path);
        }

        var bytes = File.ReadAllBytes(path);
        var manifestSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var started = RequiredTimestamp(root, "backup_started_at_utc");
        var completed = RequiredTimestamp(root, "backup_completed_at_utc");
        var pgDumpVersion = RequiredString(root, "pg_dump_version");
        var sourceVersion = RequiredString(root, "source_server_version");
        var sourceHost = RequiredString(root, "source_host");
        var sourcePort = RequiredInt(root, "source_port");
        var sourceDatabase = RequiredString(root, "source_database");
        var format = RequiredString(root, "format");
        var jobs = RequiredInt(root, "jobs");
        var compression = RequiredString(root, "compression");
        var exitCode = RequiredInt(root, "pg_dump_exit_code");
        var fileCount = RequiredLong(root, "backup_file_count");
        var totalBytes = RequiredLong(root, "backup_total_bytes");
        var tocCount = RequiredLong(root, "toc_entry_count");
        var restoreListRelative = RequiredString(root, "pg_restore_list_relative_path").Replace('\\', '/');
        var restoreListSha = GraphPackageReader.RequireSha256(
            RequiredString(root, "pg_restore_list_sha256"), "backup.pg_restore_list_sha256");
        var dumpLogRelative = RequiredString(root, "pg_dump_log_relative_path").Replace('\\', '/');
        var dumpLogSha = GraphPackageReader.RequireSha256(
            RequiredString(root, "pg_dump_log_sha256"), "backup.pg_dump_log_sha256");
        var declaredHashManifestSha = GraphPackageReader.RequireSha256(
            RequiredString(root, "backup_hash_manifest_sha256"), "backup.backup_hash_manifest_sha256");
        var declaredSchemaManifestSha = GraphPackageReader.RequireSha256(
            RequiredString(root, "source_schema_manifest_sha256"), "backup.source_schema_manifest_sha256");
        var declaredSchemaFingerprint = GraphPackageReader.RequireSha256(
            RequiredString(root, "source_schema_fingerprint_sha256"), "backup.source_schema_fingerprint_sha256");

        if (RequiredInt(root, "schema_version") != 1 || completed < started || completed > DateTimeOffset.UtcNow.AddMinutes(1) ||
            !IsPostgres18(pgDumpVersion) || !IsPostgres18(sourceVersion) ||
            !sourceHost.Equals(options.Host, StringComparison.Ordinal) || sourcePort != options.Port ||
            !sourceDatabase.Equals(options.Database, StringComparison.Ordinal) ||
            !format.Equals("directory", StringComparison.Ordinal) || jobs != 2 ||
            !compression.Equals("0", StringComparison.Ordinal) || exitCode != 0 ||
            fileCount != files.Count || totalBytes != files.Sum(item => new FileInfo(SafeCombine(backupDirectory, item.RelativePath)).Length) ||
            tocCount <= 0 || !declaredHashManifestSha.Equals(hashManifestSha256, StringComparison.Ordinal) ||
            !declaredSchemaManifestSha.Equals(schema.ManifestSha256, StringComparison.Ordinal) ||
            !declaredSchemaFingerprint.Equals(schema.FingerprintSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Full backup metadata does not satisfy the exact production directory-dump contract.");
        }

        var listedRestoreFile = files.SingleOrDefault(item => item.RelativePath.Equals(restoreListRelative, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(listedRestoreFile.RelativePath) ||
            !listedRestoreFile.Sha256.Equals(restoreListSha, StringComparison.Ordinal))
        {
            throw new InvalidDataException("pg_restore list is absent from or disagrees with the full-backup SHA manifest.");
        }

        var listedDumpLog = files.SingleOrDefault(item =>
            item.RelativePath.Equals(dumpLogRelative, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(listedDumpLog.RelativePath) ||
            !listedDumpLog.Sha256.Equals(dumpLogSha, StringComparison.Ordinal))
        {
            throw new InvalidDataException("pg_dump log is absent from or disagrees with the full-backup SHA manifest.");
        }

        return new BackupMetadataProof(restoreListRelative, dumpLogRelative, manifestSha);
    }

    private static RowCountManifestProof ValidateRowCountManifest(string path, CanonicalSchemaProof schema)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Restored row-count manifest does not exist.", path);
        }

        var bytes = File.ReadAllBytes(path);
        var manifestSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (RequiredInt(root, "schema_version") != 1 ||
            !root.TryGetProperty("tables", out var tables) || tables.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Restored row-count manifest schema is invalid.");
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in tables.EnumerateArray())
        {
            var schemaName = RequiredString(table, "schema");
            var name = RequiredString(table, "table");
            var rows = RequiredLong(table, "row_count");
            if (!schemaName.Equals("public", StringComparison.Ordinal) || rows < 0 ||
                !identities.Add(schemaName + "." + name))
            {
                throw new InvalidDataException("Restored row-count manifest has a non-public, duplicate, or negative row.");
            }
        }

        if (identities.Count == 0)
        {
            throw new InvalidDataException("Restored row-count manifest contains no public tables.");
        }

        var fingerprint = GraphPackageReader.RequireSha256(
            RequiredString(root, "schema_fingerprint_sha256"), "row_counts.schema_fingerprint_sha256");
        var schemaManifestSha = GraphPackageReader.RequireSha256(
            RequiredString(root, "source_schema_manifest_sha256"), "row_counts.source_schema_manifest_sha256");
        if (!fingerprint.Equals(schema.FingerprintSha256, StringComparison.Ordinal) ||
            !schemaManifestSha.Equals(schema.ManifestSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Restored row-count manifest schema proof differs from the source schema manifest.");
        }
        RequireExactTableSet(schema.PublicTables, identities, "source schema manifest vs restored row counts");

        return new RowCountManifestProof(identities.Count, manifestSha);
    }

    private static string ValidateRestoreEvidence(
        string path,
        string hashManifestSha256,
        string metadataManifestSha256,
        string rowCountManifestSha256,
        int restoredTableCount,
        CanonicalSchemaProof schema,
        IReadOnlyList<(string RelativePath, string Sha256)> files,
        string backupDirectory,
        ToolOptions options)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Full backup restore evidence does not exist.", path);
        }

        var bytes = File.ReadAllBytes(path);
        var manifestSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var started = RequiredTimestamp(root, "restore_started_at_utc");
        var completed = RequiredTimestamp(root, "restore_completed_at_utc");
        var testedAt = RequiredTimestamp(root, "tested_at_utc");
        var restoredHost = RequiredString(root, "restored_host");
        var restoredPort = RequiredInt(root, "restored_port");
        var restoredDatabase = RequiredString(root, "restored_database");
        var schemaHash = GraphPackageReader.RequireSha256(
            RequiredString(root, "schema_fingerprint_sha256"), "restore.schema_fingerprint_sha256");
        var schemaManifestSha = GraphPackageReader.RequireSha256(
            RequiredString(root, "source_schema_manifest_sha256"), "restore.source_schema_manifest_sha256");
        var declaredRowsHash = GraphPackageReader.RequireSha256(
            RequiredString(root, "restored_row_count_manifest_sha256"),
            "restore.restored_row_count_manifest_sha256");
        var declaredBackupManifestHash = GraphPackageReader.RequireSha256(
            RequiredString(root, "backup_manifest_sha256"), "restore.backup_manifest_sha256");
        var declaredBackupHashManifest = GraphPackageReader.RequireSha256(
            RequiredString(root, "backup_hash_manifest_sha256"), "restore.backup_hash_manifest_sha256");
        var restoreLogRelative = RequiredString(root, "pg_restore_log_relative_path").Replace('\\', '/');
        var restoreLogSha = GraphPackageReader.RequireSha256(
            RequiredString(root, "pg_restore_log_sha256"), "restore.pg_restore_log_sha256");
        var sourceSnapshotSchemaSha = GraphPackageReader.RequireSha256(
            RequiredString(root, "source_snapshot_schema_sha256"),
            "restore.source_snapshot_schema_sha256");
        var loopback = restoredHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                       System.Net.IPAddress.TryParse(restoredHost, out var address) && System.Net.IPAddress.IsLoopback(address);
        if (RequiredInt(root, "schema_version") != 1 ||
            !declaredBackupManifestHash.Equals(metadataManifestSha256, StringComparison.Ordinal) ||
            !declaredBackupHashManifest.Equals(hashManifestSha256, StringComparison.Ordinal) ||
            !declaredRowsHash.Equals(rowCountManifestSha256, StringComparison.Ordinal) ||
            !schemaHash.Equals(schema.FingerprintSha256, StringComparison.Ordinal) ||
            !sourceSnapshotSchemaSha.Equals(schema.FingerprintSha256, StringComparison.Ordinal) ||
            !schemaManifestSha.Equals(schema.ManifestSha256, StringComparison.Ordinal) ||
            !RequiredBool(root, "restore_completed") || RequiredInt(root, "restore_exit_code") != 0 ||
            !IsPostgres18(RequiredString(root, "pg_restore_version")) ||
            !RequiredString(root, "source_host").Equals(options.Host, StringComparison.Ordinal) ||
            RequiredInt(root, "source_port") != options.Port ||
            !RequiredString(root, "source_database").Equals(options.Database, StringComparison.Ordinal) ||
            started > completed || completed > testedAt || testedAt > DateTimeOffset.UtcNow.AddMinutes(1) ||
            !loopback || restoredPort is < 1 or > 65535 || string.IsNullOrWhiteSpace(restoredDatabase) ||
            !restoredDatabase.StartsWith("reference_history_restore_", StringComparison.Ordinal) ||
            RequiredInt(root, "restored_public_table_count") != restoredTableCount ||
            !RequiredBool(root, "source_and_restored_row_counts_equal"))
        {
            throw new InvalidDataException("Full backup restore evidence does not satisfy the exact restore-test contract.");
        }


        var listedRestoreLog = files.SingleOrDefault(item =>
            item.RelativePath.Equals(restoreLogRelative, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(listedRestoreLog.RelativePath) ||
            !listedRestoreLog.Sha256.Equals(restoreLogSha, StringComparison.Ordinal) ||
            !File.Exists(SafeCombine(backupDirectory, restoreLogRelative)))
        {
            throw new InvalidDataException(
                "pg_restore rehearsal log is absent from or disagrees with the full-backup SHA manifest.");
        }
        return manifestSha;
    }

    internal static CanonicalSchemaProof ValidateSchemaManifest(string path, ToolOptions options)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Canonical source schema manifest does not exist.", path);
        }

        var bytes = File.ReadAllBytes(path);
        var manifestSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var capturedAt = RequiredTimestamp(root, "captured_at_utc");
        if (RequiredInt(root, "schema_version") != 1 ||
            !RequiredString(root, "source_host").Equals(options.Host, StringComparison.Ordinal) ||
            RequiredInt(root, "source_port") != options.Port ||
            !RequiredString(root, "source_database").Equals(options.Database, StringComparison.Ordinal) ||
            capturedAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            throw new InvalidDataException("Canonical source schema manifest identity is invalid.");
        }

        if (!root.TryGetProperty("public_tables", out var tableRows) || tableRows.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("canonical_schema_lines", out var lineRows) || lineRows.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Canonical source schema manifest arrays are missing.");
        }

        var tables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tableRow in tableRows.EnumerateArray())
        {
            var schemaName = RequiredString(tableRow, "schema");
            var tableName = RequiredString(tableRow, "table");
            if (!schemaName.Equals("public", StringComparison.Ordinal) ||
                !Regex.IsMatch(tableName, "^[A-Za-z_][A-Za-z0-9_$]*$", RegexOptions.CultureInvariant) ||
                !tables.Add(schemaName + "." + tableName))
            {
                throw new InvalidDataException("Canonical source schema manifest has a non-public, unsupported, or duplicate table.");
            }
        }

        var lines = lineRows.EnumerateArray().Select(node =>
            node.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(node.GetString())
                ? node.GetString()!
                : throw new InvalidDataException("Canonical source schema line is empty or non-string.")).ToArray();
        if (tables.Count == 0 || lines.Length == 0 || lines.Distinct(StringComparer.Ordinal).Count() != lines.Length ||
            !lines.SequenceEqual(lines.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException("Canonical source schema tables/lines must be nonempty, unique, and ordinal-sorted.");
        }

        var seenTables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var separator = line.IndexOf('\t');
            var table = separator > 0 ? line[..separator] : string.Empty;
            if (!tables.Contains(table))
            {
                throw new InvalidDataException($"Canonical schema line is not tied to a declared public table: {line}.");
            }
            seenTables.Add(table);
        }
        RequireExactTableSet(tables, seenTables, "declared source tables vs canonical schema lines");

        var canonical = string.Join("\n", lines) + "\n";
        var computed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var declared = GraphPackageReader.RequireSha256(
            RequiredString(root, "schema_fingerprint_sha256"), "schema.schema_fingerprint_sha256");
        if (!computed.Equals(declared, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Canonical source schema fingerprint mismatch: declared {declared}, computed {computed}.");
        }

        return new CanonicalSchemaProof(manifestSha, computed, tables);
    }

    internal static IReadOnlySet<string> ReadPublicTableSetFromRestoreList(
        string path,
        string? expectedSha256 = null)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("pg_restore list does not exist.", path);
        }

        var bytes = File.ReadAllBytes(path);
        if (expectedSha256 is not null)
        {
            var actualSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!actualSha.Equals(expectedSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("pg_restore list changed after full-backup hash verification.");
            }
        }

        var text = new UTF8Encoding(false, true).GetString(bytes);
        var result = new HashSet<string>(StringComparer.Ordinal);
        var expression = new Regex(
            @"^\d+;\s+\d+\s+\d+\s+TABLE\s+public\s+([A-Za-z_][A-Za-z0-9_$]*)\s+\S+\s*$",
            RegexOptions.CultureInvariant);
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            var match = expression.Match(line);
            if (match.Success)
            {
                result.Add("public." + match.Groups[1].Value);
            }
        }
        if (result.Count == 0)
        {
            throw new InvalidDataException("pg_restore list contains no parseable public TABLE entries.");
        }
        return result;
    }

    internal static void RequireExactTableSet(
        IReadOnlySet<string> expected,
        IReadOnlySet<string> actual,
        string label)
    {
        var missing = expected.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var extra = actual.Except(expected, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length != 0 || extra.Length != 0)
        {
            throw new InvalidDataException(
                $"Public table set mismatch ({label}); missing=[{string.Join(",", missing)}], extra=[{string.Join(",", extra)}].");
        }
    }

    private static string NormalizeRelative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string SafeCombine(string root, string relative)
    {
        var normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Path escapes backup directory: {relative}.");
        }

        return combined;
    }

    private static void VerifyCopiedEvidenceHash(string path, string expected)
    {
        var actual = GraphPackageReader.Sha256File(path);
        if (!actual.Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Durable evidence copy hash mismatch: {Path.GetFileName(path)}.");
        }
    }

    private static bool IsPostgres18(string version) =>
        version.StartsWith("18.", StringComparison.Ordinal) ||
        version.Contains("PostgreSQL) 18.", StringComparison.Ordinal);

    private static string RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(node.GetString())
            ? node.GetString()!
            : throw new InvalidDataException($"Required evidence string '{name}' is missing.");

    private static int RequiredInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var node) && node.TryGetInt32(out var value)
            ? value
            : throw new InvalidDataException($"Required evidence integer '{name}' is missing.");

    private static long RequiredLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var node) && node.TryGetInt64(out var value)
            ? value
            : throw new InvalidDataException($"Required evidence integer '{name}' is missing.");

    private static bool RequiredBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var node) && node.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? node.GetBoolean()
            : throw new InvalidDataException($"Required evidence boolean '{name}' is missing.");

    private static DateTimeOffset RequiredTimestamp(JsonElement root, string name) =>
        Parse.Timestamp(RequiredString(root, name), "evidence." + name);
}
