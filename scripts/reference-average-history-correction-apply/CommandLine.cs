using System.Globalization;
using System.Text.Json;

namespace ReferenceAverageHistoryCorrectionApply;

internal static class CommandLine
{
    public const string Usage = """
Reference-average history correction (fail closed)

Preflight (default):
  dotnet run --project scripts/reference-average-history-correction-apply --
    --host 192.168.0.101 --port 5432 --database polycopytrader
    --cutoff <UTC> --graph-dir <directory> --graph-manifest-sha256 <64 hex>

Mutation modes additionally require:
  --prepare --durable-backup-dir D:\My\Business\PolyMarket\outputs\postgres-backups\<unique-empty-dir>
    --full-backup-dir <verified pg_dump directory> --full-backup-hash-manifest <SHA manifest>
    --full-backup-metadata-manifest <backup metadata JSON>
    --full-backup-restore-evidence <restore-test evidence JSON>
    --full-backup-restored-row-count-manifest <all-public-table row counts>
    --full-backup-schema-manifest <canonical source public-schema manifest>
  --apply --staging-dir D:\CodexTemp\runs\<marked-run>\... --durable-backup-dir <prepared durable directory>
    --prepared-package-sha256 <SHA-256 printed by --prepare>
    --operator-attestation <fresh local-admin JSON> --operator-attestation-sha256 <SHA-256>
  --finalize-apply --rollback-manifest <durable-dir>\backup-manifest.json --staging-dir D:\CodexTemp\runs\<marked-run>\...
    (recovery only after an apply committed but durable evidence finalization did not complete)
  --finalize-rollback --rollback-manifest <durable-dir>\backup-manifest.json --staging-dir D:\CodexTemp\runs\<marked-run>\...
    (recovery only after a rollback commit outcome/finalization failure)
  --rollback --rollback-manifest <durable-dir>\backup-manifest.json --staging-dir D:\CodexTemp\runs\<marked-run>\...
  --rollback-reconciled --rollback-manifest <durable-dir>\backup-manifest.json --staging-dir D:\CodexTemp\runs\<marked-run>\...
    (only when a fresh gate proves zero new affected Paper/Child/Live decisions since apply)
  --maintenance-rebuild --rollback-manifest <durable-dir>\backup-manifest.json --staging-dir <marked-run>
    (service Stopped+Disabled: exact Storage dashboard bootstrap, full hourly refresh, scoped copied-performance rebuild)
  --post-child-gate --rollback-manifest <durable-dir>\backup-manifest.json --staging-dir <marked-run>
    --child-refresh-attestation <SHA-pinned JSON> --child-refresh-attestation-sha256 <64 hex>
    (read-only final gate after controlled child-assignment refresh and another full service stop)

Credentials are read only from POLYCOPYTRADER_POSTGRES_CONNECTION.
""";

    public static ToolOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "--prepare" or "--apply" or "--maintenance-rebuild" or "--post-child-gate" or
                "--finalize-apply" or "--finalize-rollback" or
                "--rollback" or "--rollback-reconciled" or "--help")
            {
                if (!flags.Add(arg))
                {
                    throw new ArgumentException($"Duplicate option: {arg}.");
                }

                continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument: {arg}.");
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Missing value for {arg}.");
            }

            if (!values.TryAdd(arg, args[++index]))
            {
                throw new ArgumentException($"Duplicate option: {arg}.");
            }
        }

        if (flags.Contains("--help"))
        {
            throw new HelpRequestedException();
        }

        var operationFlagCount = new[]
            { "--prepare", "--apply", "--maintenance-rebuild", "--post-child-gate", "--finalize-apply", "--finalize-rollback", "--rollback", "--rollback-reconciled" }
            .Count(flags.Contains);
        if (operationFlagCount > 1)
        {
            throw new ArgumentException("Mutation and preparation mode flags are mutually exclusive.");
        }

        var mode = flags.Contains("--prepare") ? OperationMode.Prepare
            : flags.Contains("--apply") ? OperationMode.Apply
            : flags.Contains("--maintenance-rebuild") ? OperationMode.MaintenanceRebuild
            : flags.Contains("--post-child-gate") ? OperationMode.PostChildGate
            : flags.Contains("--finalize-apply") ? OperationMode.FinalizeApply
            : flags.Contains("--finalize-rollback") ? OperationMode.FinalizeRollback
            : flags.Contains("--rollback") ? OperationMode.Rollback
            : flags.Contains("--rollback-reconciled") ? OperationMode.RollbackReconciled
            : OperationMode.Preflight;
        RejectUnknown(values.Keys);

        var host = Required(values, "--host");
        var port = int.TryParse(Required(values, "--port"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var parsedPort) ? parsedPort : 0;
        if (port is < 1 or > 65535)
        {
            throw new ArgumentException("--port must be between 1 and 65535.");
        }

        var database = Required(values, "--database");
        var cutoff = ReferenceAverageHistoryCorrectionApply.Parse.Timestamp(
            Required(values, "--cutoff"), "--cutoff");
        var graphDir = Path.GetFullPath(Required(values, "--graph-dir"));
        var graphHash = RequiredSha256(values, "--graph-manifest-sha256");
        var heartbeatMinutes = values.TryGetValue("--heartbeat-stale-minutes", out var minutesText) &&
                               int.TryParse(minutesText, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            ? minutes
            : 5;
        if (heartbeatMinutes is < 2 or > 60)
        {
            throw new ArgumentException("--heartbeat-stale-minutes must be between 2 and 60.");
        }

        var staging = OptionalFullPath(values, "--staging-dir");
        var durable = OptionalFullPath(values, "--durable-backup-dir");
        var fullBackup = OptionalFullPath(values, "--full-backup-dir");
        var fullBackupManifest = OptionalFullPath(values, "--full-backup-hash-manifest");
        var fullBackupMetadata = OptionalFullPath(values, "--full-backup-metadata-manifest");
        var restoreEvidence = OptionalFullPath(values, "--full-backup-restore-evidence");
        var restoredRowCounts = OptionalFullPath(values, "--full-backup-restored-row-count-manifest");
        var schemaManifest = OptionalFullPath(values, "--full-backup-schema-manifest");
        var preparedPackageSha256 = values.ContainsKey("--prepared-package-sha256")
            ? RequiredSha256(values, "--prepared-package-sha256")
            : null;
        var rollbackManifest = OptionalFullPath(values, "--rollback-manifest");
        var operatorAttestation = OptionalFullPath(values, "--operator-attestation");
        var operatorAttestationSha256 = values.ContainsKey("--operator-attestation-sha256")
            ? RequiredSha256(values, "--operator-attestation-sha256")
            : null;
        if ((operatorAttestation is null) != (operatorAttestationSha256 is null))
        {
            throw new ArgumentException(
                "--operator-attestation and --operator-attestation-sha256 must be supplied together.");
        }
        var childRefreshAttestation = OptionalFullPath(values, "--child-refresh-attestation");
        var childRefreshAttestationSha256 = values.ContainsKey("--child-refresh-attestation-sha256")
            ? RequiredSha256(values, "--child-refresh-attestation-sha256")
            : null;
        if ((childRefreshAttestation is null) != (childRefreshAttestationSha256 is null))
        {
            throw new ArgumentException(
                "--child-refresh-attestation and --child-refresh-attestation-sha256 must be supplied together.");
        }
        if (mode == OperationMode.Prepare)
        {
            RequirePreparationOptions(durable, fullBackup, fullBackupManifest, fullBackupMetadata,
                restoreEvidence, restoredRowCounts, schemaManifest);
            if (staging is not null || preparedPackageSha256 is not null || rollbackManifest is not null ||
                operatorAttestation is not null || childRefreshAttestation is not null)
            {
                throw new ArgumentException("--prepare does not accept staging, prepared-package, or rollback options.");
            }
        }
        else if (mode == OperationMode.Apply)
        {
            if (staging is null || durable is null || preparedPackageSha256 is null ||
                operatorAttestation is null)
            {
                throw new ArgumentException(
                    "--apply requires staging, durable/prepared-package, and fresh operator-attestation options.");
            }

            RequireStagingUnderCodexTemp(staging);
            RequireDurableUnderRepository(durable);
            RejectPreparationSources(fullBackup, fullBackupManifest, fullBackupMetadata, restoreEvidence,
                restoredRowCounts, schemaManifest);
            if (rollbackManifest is not null)
            {
                throw new ArgumentException("--rollback-manifest is not valid with --apply.");
            }
            if (childRefreshAttestation is not null)
            {
                throw new ArgumentException("Child-refresh evidence is valid only with --post-child-gate.");
            }
        }
        else if (mode is OperationMode.MaintenanceRebuild or OperationMode.PostChildGate or
                 OperationMode.FinalizeApply or OperationMode.FinalizeRollback or
                 OperationMode.Rollback or OperationMode.RollbackReconciled)
        {
            if (rollbackManifest is null || staging is null || operatorAttestation is null)
            {
                throw new ArgumentException(
                    "This mode requires --rollback-manifest, --staging-dir, and fresh operator-attestation options.");
            }

            if (mode == OperationMode.PostChildGate && childRefreshAttestation is null)
            {
                throw new ArgumentException(
                    "--post-child-gate requires SHA-pinned external child-refresh attestation options.");
            }
            if (mode != OperationMode.PostChildGate && childRefreshAttestation is not null)
            {
                throw new ArgumentException("Child-refresh evidence is valid only with --post-child-gate.");
            }

            RequireStagingUnderCodexTemp(staging);
            RequireRollbackManifestUnderRepository(rollbackManifest);
            if (preparedPackageSha256 is not null)
            {
                throw new ArgumentException("--prepared-package-sha256 is valid only with --apply.");
            }
            if (durable is not null || fullBackup is not null || fullBackupManifest is not null ||
                fullBackupMetadata is not null || restoreEvidence is not null || restoredRowCounts is not null ||
                schemaManifest is not null)
            {
                throw new ArgumentException("Backup preparation options are apply-only.");
            }
        }
        else if (staging is not null || durable is not null || fullBackup is not null ||
                 fullBackupManifest is not null || fullBackupMetadata is not null ||
                 restoreEvidence is not null || restoredRowCounts is not null || schemaManifest is not null ||
                 preparedPackageSha256 is not null || rollbackManifest is not null ||
                 operatorAttestation is not null || childRefreshAttestation is not null)
        {
            throw new ArgumentException("Operation-specific evidence/options require an explicit operation mode.");
        }

        return new ToolOptions(mode, host, port, database, cutoff, graphDir, graphHash,
            staging, durable, fullBackup, fullBackupManifest, fullBackupMetadata, restoreEvidence,
            restoredRowCounts, schemaManifest, preparedPackageSha256, rollbackManifest,
            operatorAttestation, operatorAttestationSha256,
            childRefreshAttestation, childRefreshAttestationSha256, heartbeatMinutes);
    }

    private static void RequirePreparationOptions(
        string? durable,
        string? fullBackup,
        string? fullBackupManifest,
        string? fullBackupMetadata,
        string? restoreEvidence,
        string? restoredRowCounts,
        string? schemaManifest)
    {
        if (durable is null || fullBackup is null || fullBackupManifest is null ||
            fullBackupMetadata is null || restoreEvidence is null || restoredRowCounts is null ||
            schemaManifest is null)
        {
            throw new ArgumentException(
                "--prepare requires durable, full-backup, hash-manifest, metadata, restore-evidence, restored-row-count, and schema-manifest paths.");
        }

        RequireDurableUnderRepository(durable);
    }

    private static void RequireDurableUnderRepository(string durable)
    {
        string repositoryRoot;
        try
        {
            repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
        }
        catch (InvalidOperationException)
        {
            repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        }
        var durableRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "outputs", "postgres-backups")) +
                          Path.DirectorySeparatorChar;
        if (!durable.StartsWith(durableRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"--durable-backup-dir must be a unique child of {durableRoot}.");
        }
    }

    private static void RequireRollbackManifestUnderRepository(string manifestPath)
    {
        if (!Path.GetFileName(manifestPath).Equals("backup-manifest.json", StringComparison.Ordinal))
        {
            throw new ArgumentException("--rollback-manifest must name backup-manifest.json exactly.");
        }

        var durableDirectory = Path.GetDirectoryName(manifestPath) ??
                               throw new ArgumentException("--rollback-manifest has no parent directory.");
        RequireDurableUnderRepository(durableDirectory);
    }

    private static void RejectPreparationSources(params string?[] values)
    {
        if (values.Any(value => value is not null))
        {
            throw new ArgumentException("Full-backup source options are valid only with --prepare.");
        }
    }

    private static void RequireStagingUnderCodexTemp(string staging)
    {
        var runsRoot = Path.GetFullPath(@"D:\CodexTemp\runs");
        var resolved = Path.GetFullPath(staging);
        var relative = Path.GetRelativePath(runsRoot, resolved);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length < 2 || segments[0] is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "--staging-dir must be a descendant of an exact marked D:\\CodexTemp\\runs\\<session> directory.");
        }

        var sessionRoot = Path.Combine(runsRoot, segments[0]);
        ValidateSessionMarker(sessionRoot, segments[0]);
        RejectReparsePoints(runsRoot, resolved);
    }

    private static void ValidateSessionMarker(string sessionRoot, string sessionId)
    {
        var markerPath = Path.Combine(sessionRoot, ".codex-ephemeral.json");
        if (!File.Exists(markerPath))
        {
            throw new ArgumentException($"Codex temp session marker is missing: {markerPath}.");
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(markerPath));
        var root = document.RootElement;
        var valid = root.TryGetProperty("schemaVersion", out var schema) && schema.TryGetInt32(out var version) &&
                    version == 1 &&
                    root.TryGetProperty("owner", out var owner) && owner.GetString() == "OpenAI Codex" &&
                    root.TryGetProperty("kind", out var kind) && kind.GetString() == "ephemeral-session" &&
                    root.TryGetProperty("sessionId", out var id) && id.GetString() == sessionId &&
                    root.TryGetProperty("runPath", out var runPath) &&
                    string.Equals(Path.GetFullPath(runPath.GetString() ?? string.Empty),
                        Path.GetFullPath(sessionRoot), StringComparison.OrdinalIgnoreCase);
        if (!valid)
        {
            throw new ArgumentException($"Codex temp session marker is invalid: {markerPath}.");
        }
    }

    private static void RejectReparsePoints(string runsRoot, string target)
    {
        var current = new DirectoryInfo(runsRoot);
        var relative = Path.GetRelativePath(runsRoot, target);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = new DirectoryInfo(Path.Combine(current.FullName, segment));
            if (!current.Exists)
            {
                continue;
            }
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException($"--staging-dir traverses a reparse point: {current.FullName}.");
            }
        }
    }

    internal static string FindRepositoryRoot(string start)
    {
        var current = new DirectoryInfo(Path.GetFullPath(start));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "src")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private static void RejectUnknown(IEnumerable<string> names)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "--host", "--port", "--database", "--cutoff", "--graph-dir",
            "--graph-manifest-sha256", "--staging-dir", "--durable-backup-dir",
            "--full-backup-dir", "--full-backup-hash-manifest", "--full-backup-metadata-manifest",
            "--full-backup-restore-evidence", "--full-backup-restored-row-count-manifest",
            "--full-backup-schema-manifest",
            "--prepared-package-sha256", "--rollback-manifest", "--heartbeat-stale-minutes",
            "--operator-attestation", "--operator-attestation-sha256",
            "--child-refresh-attestation", "--child-refresh-attestation-sha256"
        };
        foreach (var name in names)
        {
            if (!allowed.Contains(name))
            {
                throw new ArgumentException($"Unknown option: {name}.");
            }
        }
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException($"Required option missing: {name}.");

    private static string RequiredSha256(IReadOnlyDictionary<string, string> values, string name)
    {
        var value = Required(values, name).ToLowerInvariant();
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException($"{name} must be exactly 64 hexadecimal characters.");
        }

        return value;
    }

    private static string? OptionalFullPath(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? Path.GetFullPath(value.Trim())
            : null;
}

internal sealed class HelpRequestedException : Exception;
