using System.Text;
using System.Text.Json;

namespace ReferenceAverageHistoryCorrectionApply;

internal sealed class PreparedPackageLease : IDisposable
{
    private readonly IReadOnlyList<FileStream> streams;

    public PreparedPackageLease(
        string manifestPath,
        string manifestSha256,
        PreparedPackageManifest manifest,
        IReadOnlyList<FileStream> streams)
    {
        ManifestPath = manifestPath;
        ManifestSha256 = manifestSha256;
        Manifest = manifest;
        this.streams = streams;
    }

    public string ManifestPath { get; }
    public string ManifestSha256 { get; }
    public PreparedPackageManifest Manifest { get; }

    public void Dispose()
    {
        foreach (var stream in streams.Reverse())
        {
            stream.Dispose();
        }
    }
}

internal static class PreparedPackageStore
{
    private const string ManifestFileName = "prepared-package.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task<(string Path, string Sha256)> PrepareAsync(
        ToolOptions options,
        GraphPackage graph,
        CancellationToken cancellationToken)
    {
        var durableRoot = options.DurableBackupDirectory ??
                          throw new InvalidOperationException("Durable backup directory is missing.");
        RequireEmptyDirectory(durableRoot);

        var external = ExternalBackupVerifier.Verify(options);
        ExternalBackupVerifier.CopyToDurable(external, durableRoot);
        BackupStore.CopyGraphPackage(graph.Manifest, durableRoot);

        var preparedFiles = new List<PreparedBackupFile>();
        var fullBackupRoot = Path.Combine(durableRoot, "full-backup");
        foreach (var evidence in external.Files.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            var backupPath = SafeCombine(fullBackupRoot, evidence.RelativePath);
            MakeReadOnly(backupPath);
            var info = new FileInfo(backupPath);
            preparedFiles.Add(new PreparedBackupFile(
                evidence.RelativePath,
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                evidence.Sha256));
        }

        foreach (var evidencePath in RootEvidencePaths(durableRoot).Concat(GraphEvidencePaths(durableRoot, graph)))
        {
            MakeReadOnly(evidencePath);
        }

        var manifest = new PreparedPackageManifest(
            1,
            "reference-average-history-correction-apply",
            "prepared",
            graph.Manifest.ManifestSha256,
            graph.Manifest.CutoffUtc,
            options.Host,
            options.Port,
            options.Database,
            DatabaseConnection.RequiredSearchPath,
            DateTimeOffset.UtcNow,
            preparedFiles,
            external.HashManifestSha256,
            external.MetadataManifestSha256,
            external.RestoreEvidenceSha256,
            external.RestoredRowCountManifestSha256,
            external.SchemaManifestSha256,
            external.SchemaFingerprintSha256);
        var path = Path.Combine(durableRoot, ManifestFileName);
        await WriteJsonAtomicallyAsync(path, manifest, cancellationToken);
        MakeReadOnly(path);
        var sha = GraphPackageReader.Sha256File(path);

        using var lease = VerifyAndLock(options with { PreparedPackageSha256 = sha }, graph);
        return (path, sha);
    }

    public static PreparedPackageLease VerifyAndLock(ToolOptions options, GraphPackage graph)
    {
        var durableRoot = options.DurableBackupDirectory ??
                          throw new InvalidOperationException("Durable backup directory is missing.");
        var expectedSha = options.PreparedPackageSha256 ??
                          throw new InvalidOperationException("Prepared-package SHA-256 is missing.");
        var path = Path.Combine(durableRoot, ManifestFileName);
        var actualSha = GraphPackageReader.Sha256File(path);
        if (!actualSha.Equals(expectedSha, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Prepared-package SHA-256 mismatch: expected {expectedSha}, actual {actualSha}.");
        }

        var manifest = JsonSerializer.Deserialize<PreparedPackageManifest>(File.ReadAllBytes(path), JsonOptions) ??
                       throw new InvalidDataException("Prepared-package manifest is invalid.");
        ValidateIdentity(manifest, options, graph);
        if (File.Exists(Path.Combine(durableRoot, "backup-manifest.json")) ||
            File.Exists(Path.Combine(durableRoot, "apply-committed.json")) ||
            Directory.Exists(Path.Combine(durableRoot, "scoped")))
        {
            throw new InvalidOperationException(
                "Prepared directory already contains an apply attempt; blind reuse is forbidden.");
        }

        VerifyEvidenceHash(Path.Combine(durableRoot, "full-backup-sha256.txt"),
            manifest.FullBackupHashManifestSha256);
        VerifyEvidenceHash(Path.Combine(durableRoot, "full-backup-metadata.json"),
            manifest.FullBackupMetadataManifestSha256);
        VerifyEvidenceHash(Path.Combine(durableRoot, "full-backup-restore-evidence.json"),
            manifest.FullBackupRestoreEvidenceSha256);
        VerifyEvidenceHash(Path.Combine(durableRoot, "full-backup-restored-row-counts.json"),
            manifest.FullBackupRestoredRowCountManifestSha256);
        VerifyEvidenceHash(Path.Combine(durableRoot, "full-backup-schema.json"),
            manifest.FullBackupSchemaManifestSha256);
        VerifyGraphCopy(durableRoot, graph);

        var fullBackupRoot = Path.Combine(durableRoot, "full-backup");
        var actualFiles = Directory.EnumerateFiles(fullBackupRoot, "*", SearchOption.AllDirectories)
            .Select(file => NormalizeRelative(fullBackupRoot, file))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        var expectedFiles = manifest.FullBackupFiles.Select(file => file.RelativePath)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        if (!actualFiles.SequenceEqual(expectedFiles, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Prepared full-backup file set changed after preparation.");
        }

        var lockPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [path] = actualSha,
            [Path.Combine(durableRoot, "full-backup-sha256.txt")] = manifest.FullBackupHashManifestSha256,
            [Path.Combine(durableRoot, "full-backup-metadata.json")] = manifest.FullBackupMetadataManifestSha256,
            [Path.Combine(durableRoot, "full-backup-restore-evidence.json")] = manifest.FullBackupRestoreEvidenceSha256,
            [Path.Combine(durableRoot, "full-backup-restored-row-counts.json")] =
                manifest.FullBackupRestoredRowCountManifestSha256,
            [Path.Combine(durableRoot, "full-backup-schema.json")] = manifest.FullBackupSchemaManifestSha256,
            [Path.Combine(durableRoot, "graph-package", "manifest.json")] = graph.Manifest.ManifestSha256
        };
        foreach (var graphFile in graph.Manifest.Files.Values)
        {
            lockPaths.Add(Path.Combine(durableRoot, "graph-package", graphFile.FileName), graphFile.Sha256);
        }
        foreach (var expected in manifest.FullBackupFiles)
        {
            var filePath = SafeCombine(fullBackupRoot, expected.RelativePath);
            var info = new FileInfo(filePath);
            var timestamp = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            if (info.Length != expected.Length || timestamp != expected.LastWriteTimeUtc ||
                (info.Attributes & FileAttributes.ReadOnly) == 0)
            {
                throw new InvalidDataException(
                    $"Prepared immutable file metadata changed: {expected.RelativePath}.");
            }
            lockPaths.Add(filePath, expected.Sha256);
        }

        var streams = new List<FileStream>();
        try
        {
            foreach (var pair in lockPaths.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                streams.Add(OpenReadLockedAndVerify(pair.Key, pair.Value));
            }

            return new PreparedPackageLease(path, actualSha, manifest, streams);
        }
        catch
        {
            foreach (var stream in streams.Reverse<FileStream>())
            {
                stream.Dispose();
            }
            throw;
        }
    }

    internal static FileStream OpenReadLockedAndVerify(string path, string expectedSha256)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, FileOptions.SequentialScan);
        try
        {
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))
                .ToLowerInvariant();
            if (!actual.Equals(expectedSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Prepared immutable content hash mismatch for {path}: expected {expectedSha256}, actual {actual}.");
            }
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void ValidateIdentity(
        PreparedPackageManifest manifest,
        ToolOptions options,
        GraphPackage graph)
    {
        if (manifest.SchemaVersion != 1 ||
            !manifest.Tool.Equals("reference-average-history-correction-apply", StringComparison.Ordinal) ||
            !manifest.State.Equals("prepared", StringComparison.Ordinal) ||
            !manifest.GraphManifestSha256.Equals(graph.Manifest.ManifestSha256, StringComparison.Ordinal) ||
            manifest.CutoffUtc != options.CutoffUtc ||
            !manifest.Host.Equals(options.Host, StringComparison.Ordinal) ||
            manifest.Port != options.Port ||
            !manifest.Database.Equals(options.Database, StringComparison.Ordinal) ||
            !manifest.SearchPath.Equals(DatabaseConnection.RequiredSearchPath, StringComparison.Ordinal) ||
            manifest.PreparedAtUtc == default || manifest.PreparedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1) ||
            manifest.FullBackupFiles.Count == 0)
        {
            throw new InvalidDataException(
                "Prepared package identity does not match the exact graph/database contract.");
        }
    }

    private static void VerifyGraphCopy(string durableRoot, GraphPackage graph)
    {
        var graphRoot = Path.Combine(durableRoot, "graph-package");
        VerifyEvidenceHash(Path.Combine(graphRoot, "manifest.json"), graph.Manifest.ManifestSha256);
        var actualNames = Directory.EnumerateFiles(graphRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expectedNames = graph.Manifest.Files.Values.Select(item => item.FileName)
            .Append("manifest.json")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Prepared graph-package file set changed after preparation.");
        }
        foreach (var file in graph.Manifest.Files.Values)
        {
            VerifyEvidenceHash(Path.Combine(graphRoot, file.FileName), file.Sha256);
        }
    }

    private static IEnumerable<string> RootEvidencePaths(string durableRoot)
    {
        yield return Path.Combine(durableRoot, "full-backup-sha256.txt");
        yield return Path.Combine(durableRoot, "full-backup-metadata.json");
        yield return Path.Combine(durableRoot, "full-backup-restore-evidence.json");
        yield return Path.Combine(durableRoot, "full-backup-restored-row-counts.json");
        yield return Path.Combine(durableRoot, "full-backup-schema.json");
    }

    private static IEnumerable<string> GraphEvidencePaths(string durableRoot, GraphPackage graph)
    {
        var graphRoot = Path.Combine(durableRoot, "graph-package");
        yield return Path.Combine(graphRoot, "manifest.json");
        foreach (var file in graph.Manifest.Files.Values)
        {
            yield return Path.Combine(graphRoot, file.FileName);
        }
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine;
        var partial = path + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(partial, json, new UTF8Encoding(false), cancellationToken);
            File.Move(partial, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    private static void RequireEmptyDirectory(string path)
    {
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new InvalidOperationException($"Durable preparation directory must be unique and empty: {path}.");
        }
        Directory.CreateDirectory(path);
    }

    private static void MakeReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
    }

    private static void VerifyEvidenceHash(string path, string expected)
    {
        var actual = GraphPackageReader.Sha256File(path);
        if (!actual.Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Prepared evidence hash mismatch: {Path.GetFileName(path)}.");
        }
        if ((File.GetAttributes(path) & FileAttributes.ReadOnly) == 0)
        {
            throw new InvalidDataException($"Prepared evidence is no longer read-only: {path}.");
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
            throw new InvalidDataException($"Prepared path escapes its root: {relative}.");
        }
        return combined;
    }
}
