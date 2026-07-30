using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ReferenceAverageHistoryCorrectionApply;

internal static class BackupStore
{
    private sealed record SnapshotSpec(string Table, string Query);

    private static readonly SnapshotSpec[] Specs =
    [
        new("signals", """
            SELECT row_to_json(snapshot_row)::text
            FROM (
                SELECT source.* FROM public.signals source
                JOIN correction_target_signals target ON target.id = source.id
                ORDER BY source.id
            ) snapshot_row;
            """),
        new("signal_rejections", """
            SELECT row_to_json(snapshot_row)::text
            FROM (
                SELECT source.* FROM public.signal_rejections source
                JOIN correction_target_signals target ON target.id = source.signal_id
                ORDER BY source.id
            ) snapshot_row;
            """),
        new("paper_orders", """
            SELECT row_to_json(snapshot_row)::text
            FROM (
                SELECT source.* FROM public.paper_orders source
                JOIN correction_target_orders target ON target.id = source.id
                ORDER BY source.id
            ) snapshot_row;
            """),
        new("paper_fills", """
            SELECT row_to_json(snapshot_row)::text
            FROM (
                SELECT source.* FROM public.paper_fills source
                JOIN correction_target_orders target ON target.id = source.paper_order_id
                ORDER BY source.paper_order_id, source.id
            ) snapshot_row;
            """),
        new("strategy_market_paper_runs", """
            SELECT row_to_json(snapshot_row)::text
            FROM (
                SELECT source.* FROM public.strategy_market_paper_runs source
                JOIN correction_target_runs target ON target.id = source.id
                ORDER BY source.id
            ) snapshot_row;
            """),
        new("paper_positions", """
            SELECT row_to_json(snapshot_row)::text
            FROM (
                SELECT source.* FROM public.paper_positions source
                JOIN correction_position_keys target
                  ON target.copied_trader_wallet = source.copied_trader_wallet
                 AND target.asset_id = source.asset_id
                ORDER BY source.copied_trader_wallet COLLATE "C", source.asset_id COLLATE "C", source.id
            ) snapshot_row;
            """),
        new("paper_position_settlements", """
            SELECT row_to_json(snapshot_row)::text
            FROM (
                SELECT source.* FROM public.paper_position_settlements source
                JOIN correction_position_keys target
                  ON target.copied_trader_wallet = source.copied_trader_wallet
                 AND target.asset_id = source.asset_id
                ORDER BY source.copied_trader_wallet COLLATE "C", source.asset_id COLLATE "C", source.id
            ) snapshot_row;
            """),
        new("dashboard_projection_events", """
            SELECT row_to_json(snapshot_row)::text
            FROM (
                SELECT source.* FROM public.dashboard_projection_events source
                JOIN correction_target_strategies target ON target.id = source.strategy_id
                ORDER BY source.id
            ) snapshot_row;
            """),
        new("dashboard_projection_control", """
            SELECT row_to_json(snapshot_row)::text
            FROM (
                SELECT source.* FROM public.dashboard_projection_control source
                WHERE source.singleton_id = 1
                ORDER BY source.singleton_id
            ) snapshot_row;
            """),
        new("dashboard_projection_reconciliation_queue", """
            SELECT row_to_json(snapshot_row)::text
            FROM (
                SELECT source.* FROM public.dashboard_projection_reconciliation_queue source
                JOIN correction_target_strategies target ON target.id = source.strategy_id
                ORDER BY source.strategy_id
            ) snapshot_row;
            """),
        new("paper_copied_trader_performance_refresh_queue", """
            SELECT row_to_json(snapshot_row)::text
            FROM (
                SELECT source.* FROM public.paper_copied_trader_performance_refresh_queue source
                JOIN correction_target_wallets target
                  ON target.copied_trader_wallet = source.copied_trader_wallet
                ORDER BY source.copied_trader_wallet COLLATE "C"
            ) snapshot_row;
            """)
    ];

    private static readonly SnapshotSpec[] MaintenanceSpecs =
    [
        new("dashboard_projection_control", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.dashboard_projection_control source ORDER BY source.singleton_id
            ) snapshot_row;
            """),
        new("dashboard_projection_events", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.dashboard_projection_events source ORDER BY source.id
            ) snapshot_row;
            """),
        new("dashboard_projection_reconciliation_queue", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.dashboard_projection_reconciliation_queue source ORDER BY source.strategy_id
            ) snapshot_row;
            """),
        new("dashboard_strategy_lifetime_projection_states", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.dashboard_strategy_lifetime_projection_states source ORDER BY source.strategy_id
            ) snapshot_row;
            """),
        new("dashboard_strategy_recent_projection_states", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.dashboard_strategy_recent_projection_states source
                ORDER BY source.strategy_id, source.window_hours
            ) snapshot_row;
            """),
        new("dashboard_strategy_recent_projection_facts", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.dashboard_strategy_recent_projection_facts source
                ORDER BY source.source_kind COLLATE "C", source.source_id, source.fact_kind COLLATE "C"
            ) snapshot_row;
            """),
        new("dashboard_strategy_position_projection_facts", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.dashboard_strategy_position_projection_facts source ORDER BY source.source_id
            ) snapshot_row;
            """),
        new("dashboard_strategy_performance_snapshots", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.dashboard_strategy_performance_snapshots source ORDER BY source.strategy_id
            ) snapshot_row;
            """),
        new("dashboard_strategy_recent_performance_snapshots", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.dashboard_strategy_recent_performance_snapshots source
                ORDER BY source.strategy_id, source.window_hours
            ) snapshot_row;
            """),
        new("date_dependent_strategy_hourly_paper_pnl", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.date_dependent_strategy_hourly_paper_pnl source
                ORDER BY source.strategy_id, source.hour_utc
            ) snapshot_row;
            """),
        new("paper_copied_trader_performance", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.paper_copied_trader_performance source
                JOIN correction_target_wallets target
                  ON target.copied_trader_wallet = source.copied_trader_wallet
                ORDER BY source.copied_trader_wallet COLLATE "C", source.category COLLATE "C"
            ) snapshot_row;
            """),
        new("paper_copied_trader_performance_refresh_queue", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.paper_copied_trader_performance_refresh_queue source
                JOIN correction_target_wallets target
                  ON target.copied_trader_wallet = source.copied_trader_wallet
                ORDER BY source.copied_trader_wallet COLLATE "C"
            ) snapshot_row;
        """)
    ];

    private static readonly SnapshotSpec ChildAssignmentSpec = new(
        "strategy_child_parent_assignments", """
            SELECT row_to_json(snapshot_row)::text FROM (
                SELECT source.* FROM public.strategy_child_parent_assignments source
                WHERE EXISTS (
                    SELECT 1 FROM correction_target_strategies target
                    WHERE target.id = source.child_strategy_id OR target.id = source.parent_strategy_id)
                ORDER BY source.child_strategy_id, source.assigned_at_utc, source.id
            ) snapshot_row;
            """);

    public static async Task<IReadOnlyList<SnapshotFile>> SnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string directory,
        string phase,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var result = new List<SnapshotFile>();
        foreach (var spec in Specs)
        {
            var fileName = $"{phase}-{spec.Table}.ndjson";
            var path = Path.Combine(directory, fileName);
            result.Add(await WriteSnapshotAsync(connection, transaction, spec, path, fileName, cancellationToken));
        }

        return result;
    }

    public static Task<IReadOnlyList<SnapshotFile>> SnapshotMaintenanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string directory,
        string phase,
        CancellationToken cancellationToken) =>
        SnapshotSpecsAsync(connection, transaction, directory, phase, MaintenanceSpecs, cancellationToken);

    public static async Task<SnapshotFile> SnapshotChildAssignmentsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string directory,
        string phase,
        CancellationToken cancellationToken)
    {
        var files = await SnapshotSpecsAsync(connection, transaction, directory, phase,
            [ChildAssignmentSpec], cancellationToken);
        return files.Single();
    }

    private static async Task<IReadOnlyList<SnapshotFile>> SnapshotSpecsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string directory,
        string phase,
        IReadOnlyList<SnapshotSpec> specs,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var result = new List<SnapshotFile>();
        foreach (var spec in specs)
        {
            var fileName = $"{phase}-{spec.Table}.ndjson";
            var path = Path.Combine(directory, fileName);
            result.Add(await WriteSnapshotAsync(connection, transaction, spec, path, fileName, cancellationToken));
        }
        return result;
    }

    public static async Task RestorePreimageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string directory,
        IReadOnlyList<SnapshotFile> files,
        IReadOnlySet<string>? includedTables,
        CancellationToken cancellationToken)
    {
        var byTable = files.ToDictionary(item => item.TableName, StringComparer.Ordinal);
        var order = new[]
        {
            "signals", "paper_orders", "paper_fills", "strategy_market_paper_runs",
            "signal_rejections", "paper_positions", "paper_position_settlements",
            "dashboard_projection_events", "dashboard_projection_control",
            "dashboard_projection_reconciliation_queue",
            "paper_copied_trader_performance_refresh_queue"
        };
        foreach (var table in order)
        {
            if (includedTables is not null && !includedTables.Contains(table))
            {
                continue;
            }

            if (!byTable.TryGetValue(table, out var evidence))
            {
                throw new InvalidDataException($"Backup manifest lacks preimage for {table}.");
            }

            var path = Path.Combine(directory, evidence.FileName);
            using var snapshotStream = OpenVerifiedSnapshot(path, evidence);
            if (evidence.RowCount == 0)
            {
                continue;
            }

            var stagingTable = "correction_restore_" + table;
            await using (var create = new NpgsqlCommand(
                             $"CREATE TEMP TABLE {Quote(stagingTable)} (row_json jsonb NOT NULL) ON COMMIT DROP;",
                             connection, transaction))
            {
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var importer = await connection.BeginBinaryImportAsync(
                             $"COPY {Quote(stagingTable)} (row_json) FROM STDIN (FORMAT BINARY)",
                             cancellationToken))
            {
                using var reader = new StreamReader(snapshotStream, new UTF8Encoding(false, true),
                    detectEncodingFromByteOrderMarks: false, bufferSize: 1024 * 1024, leaveOpen: true);
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    await importer.StartRowAsync(cancellationToken);
                    await importer.WriteAsync(line, NpgsqlDbType.Jsonb, cancellationToken);
                }

                await importer.CompleteAsync(cancellationToken);
            }

            await ValidateRestoreScopeAsync(connection, transaction, stagingTable, table,
                cancellationToken);

            await using var insert = new NpgsqlCommand($$"""
                {{(table == "dashboard_projection_control"
                    ? "DELETE FROM public.dashboard_projection_control WHERE singleton_id = 1;"
                    : string.Empty)}}
                INSERT INTO public.{{Quote(table)}}
                SELECT (jsonb_populate_record(NULL::public.{{Quote(table)}}, row_json)).*
                FROM {{Quote(stagingTable)}};
                """, connection, transaction);
            var affected = await insert.ExecuteNonQueryAsync(cancellationToken);
            var inserted = table == "dashboard_projection_control" ? affected - 1 : affected;
            if (inserted != evidence.RowCount)
            {
                throw new InvalidDataException(
                    $"Restore row count mismatch for {table}: expected {evidence.RowCount}, inserted {inserted}.");
            }
        }
    }

    public static void ValidateSnapshotSet(string directory, IReadOnlyList<SnapshotFile> files)
    {
        ValidateExactSnapshotTableSet(files, Specs.Select(spec => spec.Table), "scoped correction snapshot");
        foreach (var evidence in files)
        {
            ValidateSnapshotFile(Path.Combine(directory, evidence.FileName), evidence);
        }
    }

    public static void ValidateMaintenanceSnapshotSet(
        string directory,
        IReadOnlyList<SnapshotFile> files,
        string label)
    {
        ValidateExactSnapshotTableSet(files, MaintenanceSpecs.Select(spec => spec.Table), label);
        foreach (var evidence in files)
        {
            ValidateSnapshotFile(Path.Combine(directory, evidence.FileName), evidence);
        }
    }

    public static void ValidateChildAssignmentSnapshot(string directory, SnapshotFile file)
    {
        if (!file.TableName.Equals(ChildAssignmentSpec.Table, StringComparison.Ordinal) ||
            !file.FileName.EndsWith("-strategy_child_parent_assignments.ndjson", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Child-assignment snapshot metadata is invalid.");
        }
        ValidateSnapshotFile(Path.Combine(directory, file.FileName), file);
    }

    private static void ValidateExactSnapshotTableSet(
        IReadOnlyList<SnapshotFile> files,
        IEnumerable<string> expectedTables,
        string label)
    {
        var expected = expectedTables.Order(StringComparer.Ordinal).ToArray();
        var actual = files.Select(file => file.TableName).Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"{label} table set is incomplete, duplicated, or contains an unexpected table.");
        }

        foreach (var file in files)
        {
            if (file.RowCount < 0 || file.Sha256.Length != 64 ||
                !file.FileName.EndsWith("-" + file.TableName + ".ndjson", StringComparison.Ordinal) ||
                Path.GetFileName(file.FileName) != file.FileName)
            {
                throw new InvalidDataException($"{label} contains invalid evidence metadata for {file.TableName}.");
            }
        }
    }

    public static void CopyScopedEvidence(string sourceDirectory, string durableRoot)
    {
        var destination = Path.Combine(durableRoot, "scoped");
        Directory.CreateDirectory(destination);
        foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            File.Copy(source, Path.Combine(destination, Path.GetFileName(source)), overwrite: false);
        }
    }

    public static void CopyGraphPackage(GraphManifest manifest, string durableRoot)
    {
        var destination = Path.Combine(durableRoot, "graph-package");
        Directory.CreateDirectory(destination);
        File.Copy(Path.Combine(manifest.Directory, "manifest.json"),
            Path.Combine(destination, "manifest.json"), overwrite: false);
        foreach (var file in manifest.Files.Values.OrderBy(item => item.FileName, StringComparer.Ordinal))
        {
            File.Copy(Path.Combine(manifest.Directory, file.FileName),
                Path.Combine(destination, file.FileName), overwrite: false);
        }

        if (GraphPackageReader.Sha256File(Path.Combine(destination, "manifest.json")) != manifest.ManifestSha256)
        {
            throw new InvalidDataException("Durable graph manifest copy hash mismatch.");
        }

        foreach (var file in manifest.Files.Values)
        {
            if (GraphPackageReader.Sha256File(Path.Combine(destination, file.FileName)) != file.Sha256)
            {
                throw new InvalidDataException($"Durable graph package copy hash mismatch: {file.FileName}.");
            }
        }
    }

    public static async Task WriteManifestAsync(
        string path,
        CorrectionBackupManifest manifest,
        CancellationToken cancellationToken)
    {
        await WriteJsonAtomicallyAsync(path, manifest, overwrite: true, cancellationToken);
    }

    public static async Task WriteManifestNewAsync(
        string path,
        CorrectionBackupManifest manifest,
        CancellationToken cancellationToken)
    {
        await WriteJsonAtomicallyAsync(path, manifest, overwrite: false, cancellationToken);
    }

    public static async Task WriteJsonNewAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        await WriteJsonAtomicallyAsync(path, value, overwrite: false, cancellationToken);
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        }) + Environment.NewLine;
        var partial = path + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(partial, json, new UTF8Encoding(false), cancellationToken);
            File.Move(partial, path, overwrite);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    public static CorrectionBackupManifest ReadManifest(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Rollback manifest does not exist.", path);
        }

        var manifest = JsonSerializer.Deserialize<CorrectionBackupManifest>(File.ReadAllBytes(path),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        return manifest ?? throw new InvalidDataException("Rollback manifest is invalid.");
    }

    public static T ReadJson<T>(string path, string label)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(label + " does not exist.", path);
        }
        return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path),
                   new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }) ??
               throw new InvalidDataException(label + " is invalid.");
    }

    private static async Task<SnapshotFile> WriteSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SnapshotSpec spec,
        string path,
        string fileName,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 1024 * 1024, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var command = new NpgsqlCommand(spec.Query, connection, transaction)
        {
            CommandTimeout = 0
        };
        await using var reader = await command.ExecuteReaderAsync(
            System.Data.CommandBehavior.SequentialAccess, cancellationToken);
        var newline = new byte[] { (byte)'\n' };
        long count = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var json = reader.GetString(0);
            var bytes = Encoding.UTF8.GetBytes(json);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.WriteAsync(newline, cancellationToken);
            hash.AppendData(bytes);
            hash.AppendData(newline);
            count++;
        }

        await stream.FlushAsync(cancellationToken);
        return new SnapshotFile(spec.Table, fileName, count,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void ValidateSnapshotFile(string path, SnapshotFile evidence)
    {
        using var _ = OpenVerifiedSnapshot(path, evidence);
    }

    private static FileStream OpenVerifiedSnapshot(string path, SnapshotFile evidence)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, FileOptions.SequentialScan);
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            stream.Position = 0;
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: false, bufferSize: 1024 * 1024, leaveOpen: true);
            long rows = 0;
            while (reader.ReadLine() is not null)
            {
                rows++;
            }
            stream.Position = 0;

            if (!hash.Equals(evidence.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Scoped snapshot hash mismatch for {evidence.TableName}: expected {evidence.Sha256}, actual {hash}.");
            }

            if (rows != evidence.RowCount)
            {
                throw new InvalidDataException(
                    $"Scoped snapshot row count mismatch for {evidence.TableName}: expected {evidence.RowCount}, actual {rows}.");
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static async Task ValidateRestoreScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string stagingTable,
        string table,
        CancellationToken cancellationToken)
    {
        var outsideScopePredicate = table switch
        {
            "signals" =>
                "NOT EXISTS (SELECT 1 FROM correction_target_signals target WHERE target.id = (row_json ->> 'id')::uuid)",
            "signal_rejections" =>
                "NOT EXISTS (SELECT 1 FROM correction_target_signals target WHERE target.id = (row_json ->> 'signal_id')::uuid)",
            "paper_orders" =>
                "NOT EXISTS (SELECT 1 FROM correction_target_orders target WHERE target.id = (row_json ->> 'id')::uuid)",
            "paper_fills" =>
                "NOT EXISTS (SELECT 1 FROM correction_target_orders target WHERE target.id = (row_json ->> 'paper_order_id')::uuid)",
            "strategy_market_paper_runs" =>
                "NOT EXISTS (SELECT 1 FROM correction_target_runs target WHERE target.id = (row_json ->> 'id')::uuid)",
            "paper_positions" or "paper_position_settlements" =>
                "NOT EXISTS (SELECT 1 FROM correction_position_keys target " +
                "WHERE target.copied_trader_wallet = row_json ->> 'copied_trader_wallet' " +
                "AND target.asset_id = row_json ->> 'asset_id')",
            "dashboard_projection_events" =>
                "NOT EXISTS (SELECT 1 FROM correction_target_strategies target " +
                "WHERE target.id = (row_json ->> 'strategy_id')::uuid)",
            "dashboard_projection_control" => "(row_json ->> 'singleton_id')::integer <> 1",
            "dashboard_projection_reconciliation_queue" =>
                "NOT EXISTS (SELECT 1 FROM correction_target_strategies target " +
                "WHERE target.id = (row_json ->> 'strategy_id')::uuid)",
            "paper_copied_trader_performance_refresh_queue" =>
                "NOT EXISTS (SELECT 1 FROM correction_target_wallets target " +
                "WHERE target.copied_trader_wallet = row_json ->> 'copied_trader_wallet')",
            _ => throw new InvalidDataException($"No restore-scope contract exists for table {table}.")
        };
        await using var command = new NpgsqlCommand(
            $"SELECT count(*)::integer FROM {Quote(stagingTable)} WHERE {outsideScopePredicate};",
            connection, transaction);
        var outsideScope = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (outsideScope != 0)
        {
            throw new InvalidDataException(
                $"Rollback snapshot for {table} contains {outsideScope:N0} rows outside the exact graph scope.");
        }
    }

    private static string Quote(string identifier) => '"' + identifier.Replace("\"", "\"\"") + '"';
}
