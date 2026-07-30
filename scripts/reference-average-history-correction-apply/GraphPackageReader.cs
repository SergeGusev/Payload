using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReferenceAverageHistoryCorrectionApply;

internal static class GraphPackageReader
{
    private static readonly string[] RequiredFiles =
    [
        "main-removals.csv",
        "child-removals.csv",
        "live-shadow-overlaps.csv",
        "dependencies.csv",
        "reconciliation-targets.csv",
        "operation-footprint.csv",
        "position-keys.csv",
        "foreign-keys.csv",
        "schema-reference-columns.csv",
        "graph-orders.csv",
        "graph-fills.csv",
        "positions.csv",
        "position-settlements.csv",
        "add-feasibility.csv",
        "invariant-errors.csv"
    ];

    public static GraphPackage Read(ToolOptions options)
    {
        if (!Directory.Exists(options.GraphDirectory))
        {
            throw new DirectoryNotFoundException($"Graph directory does not exist: {options.GraphDirectory}.");
        }

        var manifestPath = Path.Combine(options.GraphDirectory, "manifest.json");
        var lockedFiles = new List<FileStream>();
        try
        {
            var manifestStream = OpenReadLocked(manifestPath);
            lockedFiles.Add(manifestStream);
            var actualManifestHash = Sha256Stream(manifestStream);
            if (!actualManifestHash.Equals(options.GraphManifestSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Graph manifest SHA-256 mismatch: expected {options.GraphManifestSha256}, actual {actualManifestHash}.");
            }

            using var document = JsonDocument.Parse(manifestStream);
            var root = document.RootElement;
            var schemaVersion = RequiredInt(root, "schema_version");
            if (schemaVersion != 2)
            {
                throw new InvalidDataException($"Unsupported graph schema_version={schemaVersion}; exactly 2 is required.");
            }

            var tool = RequiredString(root, "tool");
            if (!tool.Equals("reference-average-history-correction-graph-preview", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unexpected graph tool identity: {tool}.");
            }

            var cutoff = Parse.Timestamp(RequiredString(root, "cutoff_utc"), "manifest.cutoff_utc");
            if (cutoff != options.CutoffUtc)
            {
                throw new InvalidDataException(
                    $"Cutoff mismatch: command={options.CutoffUtc:O}, manifest={cutoff:O}.");
            }

            var files = ReadFileEvidence(root);
            foreach (var required in RequiredFiles)
            {
                if (!files.ContainsKey(required))
                {
                    throw new InvalidDataException($"Graph manifest does not list required file {required}.");
                }
            }

            VerifyAndLockFiles(options.GraphDirectory, files, lockedFiles);
            var safety = RequiredObject(root, "safety");
            var reconciliation = RequiredObject(RequiredObject(root, "contract"), "reconciliation");
            var manifest = new GraphManifest(
                schemaVersion,
                tool,
                cutoff,
                actualManifestHash,
                options.GraphDirectory,
                files,
                RequiredInt(safety, "invariant_errors"),
                RequiredInt(safety, "live_shadow_blockers"),
                RequiredInt(safety, "semantic_dependency_blockers"),
                RequiredInt(safety, "shared_position_key_blockers"),
                RequiredInt(safety, "required_reconciliation_plan_blockers"),
                RequiredInt(safety, "infeasible_adds"),
                RequiredBool(safety, "safe_to_prepare_separate_mutation"),
                RequiredInt(reconciliation, "schema_version"),
                RequiredString(reconciliation, "algorithm"),
                RequiredString(reconciliation, "serialization"),
                RequireSha256(RequiredString(reconciliation, "contract_sha256"),
                    "contract.reconciliation.contract_sha256"),
                RequiredInt(reconciliation, "target_count"),
                RequiredInt(reconciliation, "blocking_target_count"),
                RequiredString(reconciliation, "apply_handshake"));

            var blockers = new List<string>();
            var reconciliationAuthorized = ValidateReconciliationContract(options.GraphDirectory, manifest, blockers);
            AddManifestBlockers(manifest, reconciliationAuthorized, blockers);
            AddCsvBlockers(options.GraphDirectory, blockers);

            var main = ReadMainRemovals(options.GraphDirectory, blockers);
            var children = ReadChildRemovals(options.GraphDirectory, blockers);
            var adds = ReadAdds(options.GraphDirectory, blockers);
            var positionKeys = ReadPositionKeys(options.GraphDirectory, blockers);
            var foreignKeys = ReadForeignKeys(options.GraphDirectory, blockers);
            var referenceColumns = ReadSchemaReferenceColumns(options.GraphDirectory, blockers);
            var graphRowHashes = ReadGraphRowHashes(options.GraphDirectory, blockers);
            var fillRowHashes = ReadFillRowHashes(options.GraphDirectory, blockers);
            var positionRowHashes = ReadPositionRowHashes(options.GraphDirectory, blockers);
            var settlementRowHashes = ReadPositionSettlementRowHashes(options.GraphDirectory, blockers);
            var operationFootprint = ReadOperationFootprint(options.GraphDirectory, blockers);
            ValidateIdentities(main, children, adds, blockers);
            ValidatePhysicalHashCoverage(main, children, graphRowHashes, fillRowHashes, blockers);
            ValidateOperationFootprint(main, children, adds, positionKeys, graphRowHashes, fillRowHashes,
                positionRowHashes, settlementRowHashes, operationFootprint, blockers);

            return new GraphPackage(manifest, main, children, adds, positionKeys, foreignKeys, referenceColumns,
                graphRowHashes, fillRowHashes, positionRowHashes, settlementRowHashes,
                operationFootprint,
                blockers.Distinct(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            foreach (var stream in lockedFiles.AsEnumerable().Reverse())
            {
                stream.Dispose();
            }
        }
    }

    private static IReadOnlyDictionary<string, GraphFileEvidence> ReadFileEvidence(JsonElement root)
    {
        var filesNode = RequiredArray(root, "files");
        var files = new Dictionary<string, GraphFileEvidence>(StringComparer.Ordinal);
        foreach (var item in filesNode.EnumerateArray())
        {
            var name = RequiredString(item, "file_name");
            var rows = RequiredLong(item, "row_count");
            var sha = RequireSha256(RequiredString(item, "sha256"), $"files[{name}].sha256");
            if (!files.TryAdd(name, new GraphFileEvidence(name, rows, sha)))
            {
                throw new InvalidDataException($"Duplicate graph file in manifest: {name}.");
            }
        }

        return files;
    }

    private static void VerifyAndLockFiles(
        string directory,
        IReadOnlyDictionary<string, GraphFileEvidence> files,
        ICollection<FileStream> lockedFiles)
    {
        foreach (var file in files.Values.OrderBy(item => item.FileName, StringComparer.Ordinal))
        {
            if (Path.GetFileName(file.FileName) != file.FileName)
            {
                throw new InvalidDataException($"Graph manifest contains a non-leaf file name: {file.FileName}.");
            }

            var path = Path.Combine(directory, file.FileName);
            var stream = OpenReadLocked(path);
            lockedFiles.Add(stream);
            var actualHash = Sha256Stream(stream);
            if (!actualHash.Equals(file.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Graph file SHA-256 mismatch for {file.FileName}: expected {file.Sha256}, actual {actualHash}.");
            }

            if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var actualRows = CsvFile.CountDataRows(path);
                if (actualRows != file.RowCount)
                {
                    throw new InvalidDataException(
                        $"Graph row count mismatch for {file.FileName}: expected {file.RowCount}, actual {actualRows}.");
                }
            }
        }
    }

    private static FileStream OpenReadLocked(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required file does not exist.", path);
        }
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
    }

    private static string Sha256Stream(FileStream stream)
    {
        stream.Position = 0;
        var sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        stream.Position = 0;
        return sha;
    }

    private static void AddManifestBlockers(
        GraphManifest manifest,
        bool reconciliationAuthorized,
        ICollection<string> blockers)
    {
        if (!manifest.SafeToPrepareMutation && !reconciliationAuthorized)
        {
            blockers.Add("Graph manifest safety.safe_to_prepare_separate_mutation is false.");
        }

        AddCountBlocker(manifest.InvariantErrors, "invariant errors", blockers);
        AddCountBlocker(manifest.LiveShadowBlockers, "Live/shadow blockers", blockers);
        AddCountBlocker(manifest.DependencyBlockers, "semantic dependency blockers", blockers);
        AddCountBlocker(manifest.PositionBlockers, "shared position-key blockers", blockers);
        if (!reconciliationAuthorized)
        {
            AddCountBlocker(manifest.ReconciliationBlockers, "unresolved reconciliation blockers", blockers);
        }
        AddCountBlocker(manifest.InfeasibleAdds, "infeasible modeled adds", blockers);
    }

    private static void AddCountBlocker(int count, string label, ICollection<string> blockers)
    {
        if (count != 0)
        {
            blockers.Add($"Graph manifest reports {count:N0} {label}.");
        }
    }

    private static void AddCsvBlockers(string directory, ICollection<string> blockers)
    {
        var liveRows = CsvFile.Read(Path.Combine(directory, "live-shadow-overlaps.csv"), row =>
            row.Required("row_id"));
        if (liveRows.Count != 0)
        {
            blockers.Add($"Live/shadow overlap file contains {liveRows.Count:N0} rows; every overlap hard-blocks mutation.");
        }

        AddFlaggedRows(directory, "dependencies.csv", "blocks_mutation", "semantic dependencies", blockers);
        AddFlaggedRows(directory, "position-keys.csv", "blocks_mutation", "shared position keys", blockers);
        var invariantRows = CsvFile.Read(Path.Combine(directory, "invariant-errors.csv"), row =>
            row.Required("code"));
        if (invariantRows.Count != 0)
        {
            blockers.Add($"Invariant error file contains {invariantRows.Count:N0} rows.");
        }
    }

    private static bool ValidateReconciliationContract(
        string directory,
        GraphManifest manifest,
        ICollection<string> blockers)
    {
        try
        {
            var rows = CsvFile.Read(Path.Combine(directory, "reconciliation-targets.csv"), row =>
                new ReconciliationTargetContract(
                    row.Required("target_id"),
                    row.Required("table_name"),
                    row.Required("key_scope"),
                    row.Required("method_id"),
                    row.Required("required_action"),
                    row.Required("reason"),
                    Parse.Bool(row.Required("blocks_mutation"), "reconciliation.blocks_mutation"),
                    RequireSha256(row.Required("target_contract_sha256"),
                        "reconciliation.target_contract_sha256")));
            ReconciliationPlanContract.ValidateManifestAndRows(manifest, rows);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            blockers.Add("Exact reconciliation maintenance-plan handshake failed: " + exception.Message);
            return false;
        }
    }

    private static void AddFlaggedRows(
        string directory,
        string fileName,
        string flagColumn,
        string label,
        ICollection<string> blockers)
    {
        var flags = CsvFile.Read(Path.Combine(directory, fileName), row =>
            Parse.Bool(row.Required(flagColumn), $"{fileName}.{flagColumn}"));
        var count = flags.Count(flag => flag);
        if (count != 0)
        {
            blockers.Add($"{fileName} contains {count:N0} blocking {label}.");
        }
    }

    private static IReadOnlyList<OperationFootprintContract> ReadOperationFootprint(
        string directory,
        ICollection<string> blockers)
    {
        try
        {
            return CsvFile.Read(Path.Combine(directory, "operation-footprint.csv"), row =>
                new OperationFootprintContract(
                    row.Required("scope"),
                    row.Required("table_name"),
                    row.Required("operation"),
                    row.Required("selector"),
                    Parse.Long(row.Required("selector_identity_count"), "operation-footprint.selector_identity_count"),
                    ParseNullableLong(row.Optional("snapshot_row_count"),
                        "operation-footprint.snapshot_row_count"),
                    ParseNullableLong(row.Optional("snapshot_pg_column_size_bytes"),
                        "operation-footprint.snapshot_pg_column_size_bytes"),
                    Parse.Long(row.Required("planned_direct_row_operations"),
                        "operation-footprint.planned_direct_row_operations"),
                    Parse.Bool(row.Required("exact_snapshot_measurement"),
                        "operation-footprint.exact_snapshot_measurement"),
                    row.Required("evidence")));
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            blockers.Add("Operation-footprint contract could not be parsed: " + exception.Message);
            return [];
        }
    }

    private static long? ParseNullableLong(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? null : Parse.Long(value, field);

    private static IReadOnlyList<MainRemoval> ReadMainRemovals(string directory, ICollection<string> blockers)
    {
        try
        {
            return CsvFile.Read(Path.Combine(directory, "main-removals.csv"), row =>
            {
                var correctedReason = row.Required("corrected_skip_reason");
                if (!correctedReason.Equals("reference_average_history_correction_v2_would_skip", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Unexpected corrected_skip_reason: {correctedReason}.");
                }

                var restored = Parse.Decimal(row.Required("restored_base_stake_usd"), "restored_base_stake_usd");
                var effective = Parse.Decimal(row.Required("historical_effective_stake_usd"), "historical_effective_stake_usd");
                var target = Parse.Decimal(row.Required("historical_target_notional_usd"), "historical_target_notional_usd");
                if (restored <= 0 || effective <= 0 || target <= 0)
                {
                    throw new InvalidDataException("Historical base/effective/target stake proof values must all be positive.");
                }

                var replayJson = row.Required("replay_evidence_json");
                var replayHash = RequireSha256(row.Required("replay_evidence_sha256"), "replay_evidence_sha256");
                var actualReplayHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(replayJson)))
                    .ToLowerInvariant();
                if (!actualReplayHash.Equals(replayHash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("replay_evidence_json SHA-256 does not match replay_evidence_sha256.");
                }

                var signalPreviewManifestSha = RequireSha256(
                    row.Required("signal_preview_manifest_sha256"), "signal_preview_manifest_sha256");
                var replayClassifierSha = RequireSha256(
                    row.Required("replay_classifier_sha256"), "replay_classifier_sha256");
                if (!signalPreviewManifestSha.Equals(
                        ModeledAddPayloadValidator.SignalPreviewManifestSha256, StringComparison.Ordinal) ||
                    !replayClassifierSha.Equals(
                        ModeledAddPayloadValidator.ReplayClassifierSha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Removal replay evidence does not use the exact pinned preview/classifier contract.");
                }

                return new MainRemoval(
                    Parse.Guid(row.Required("run_id"), "main.run_id"),
                    Parse.Guid(row.Required("strategy_id"), "main.strategy_id"),
                    row.Required("strategy_code"),
                    row.Required("market_id"),
                    Parse.Guid(row.Required("paper_order_id"), "main.paper_order_id"),
                    Parse.Guid(row.Required("signal_id"), "main.signal_id"),
                    row.Required("asset_id"),
                    row.Required("outcome"),
                    row.Required("copied_trader_wallet"),
                    Parse.Timestamp(row.Required("corrected_skipped_updated_at_utc"),
                        "corrected_skipped_updated_at_utc"),
                    restored,
                    effective,
                    target,
                    row.Required("historical_stake_sizing_source"),
                    RequireSha256(row.Required("stake_sizing_proof_sha256"), "stake_sizing_proof_sha256"),
                    row.Required("classifier_reason"),
                    row.Required("classifier_action"),
                    signalPreviewManifestSha,
                    replayClassifierSha,
                    replayJson,
                    replayHash,
                    RequireSha256(row.Required("graph_state_sha256"), "graph_state_sha256"),
                    RequireSha256(row.Required("fill_set_sha256"), "fill_set_sha256"));
            });
        }
        catch (InvalidDataException exception)
        {
            blockers.Add("main-removals.csv does not satisfy the physical-apply v2 contract: " + exception.Message);
            return [];
        }
    }

    private static IReadOnlyList<ChildRemoval> ReadChildRemovals(string directory, ICollection<string> blockers)
    {
        try
        {
            return CsvFile.Read(Path.Combine(directory, "child-removals.csv"), row => new ChildRemoval(
                Parse.Guid(row.Required("parent_run_id"), "child.parent_run_id"),
                Parse.Guid(row.Required("child_run_id"), "child.child_run_id"),
                Parse.Guid(row.Required("child_strategy_id"), "child.child_strategy_id"),
                row.Required("child_strategy_code"),
                row.Required("market_id"),
                Parse.Guid(row.Required("child_paper_order_id"), "child.paper_order_id"),
                Parse.Guid(row.Required("child_signal_id"), "child.signal_id"),
                RequireSha256(row.Required("graph_state_sha256"), "child.graph_state_sha256"),
                RequireSha256(row.Required("fill_set_sha256"), "child.fill_set_sha256")));
        }
        catch (InvalidDataException exception)
        {
            blockers.Add("child-removals.csv does not satisfy the physical-apply v2 contract: " + exception.Message);
            return [];
        }
    }

    private static IReadOnlyList<AddCandidate> ReadAdds(string directory, ICollection<string> blockers)
    {
        try
        {
            var adds = CsvFile.Read(Path.Combine(directory, "add-feasibility.csv"), row => new AddCandidate(
                Parse.Guid(row.Required("run_id"), "add.run_id"),
                Parse.Guid(row.Required("strategy_id"), "add.strategy_id"),
                row.Required("strategy_code"),
                row.Required("market_id"),
                row.Required("condition_id"),
                row.Required("asset"),
                row.Required("kind"),
                RequireSha256(row.Required("add_source_state_sha256"), "add.add_source_state_sha256"),
                RequireSha256(row.Required("add_source_run_full_row_sha256"),
                    "add.add_source_run_full_row_sha256"),
                Parse.Timestamp(row.Required("modeled_entry_at_utc"), "add.modeled_entry_at_utc"),
                Parse.Timestamp(row.Required("modeled_settled_at_utc"), "add.modeled_settled_at_utc"),
                row.Required("modeled_settlement_timestamp_source"),
                row.Required("settlement_category"),
                row.Required("modeled_raw_decision_json"),
                RequireSha256(row.Required("modeled_raw_decision_sha256"),
                    "add.modeled_raw_decision_sha256"),
                row.Required("modeled_fill_evidence"),
                row.Required("modeled_mutation_payload_json"),
                RequireSha256(row.Required("modeled_mutation_payload_sha256"),
                    "add.modeled_mutation_payload_sha256"),
                Parse.Decimal(row.Required("assumed_fill_price"), "add.assumed_fill_price"),
                Parse.Decimal(row.Required("historical_stake_multiplier"), "add.historical_stake_multiplier"),
                Parse.Decimal(row.Required("gamma_order_min_size"), "add.gamma_order_min_size"),
                Parse.Decimal(row.Required("raw_worst_price_notional_usd"),
                    "add.raw_worst_price_notional_usd"),
                Parse.Decimal(row.Required("rounded_worst_price_notional_usd"),
                    "add.rounded_worst_price_notional_usd"),
                row.Required("selected_outcome"),
                row.Required("selected_token_id"),
                row.Required("resolved_winning_outcome"),
                row.Required("resolved_winning_token_id"),
                row.Required("resolution_ledger_source"),
                row.Required("resolution_ledger_provenance_group"),
                row.Optional("resolution_ledger_raw_event_type"),
                row.Optional("resolution_ledger_raw_sha256"),
                Parse.Long(row.Required("resolution_ledger_raw_bytes"), "add.resolution_ledger_raw_bytes"),
                Parse.Timestamp(row.Required("resolution_ledger_first_received_at_utc"), "add.resolution_ledger_first_received_at_utc"),
                Parse.Timestamp(row.Required("resolution_ledger_last_received_at_utc"), "add.resolution_ledger_last_received_at_utc"),
                Parse.Bool(row.Required("resolution_ledger_raw_validated"), "add.resolution_ledger_raw_validated"),
                row.Optional("archived_tick_source"),
                row.Optional("archived_tick_provenance_group"),
                Parse.Int(row.Required("archived_tick_sample_count"), "add.archived_tick_sample_count"),
                Parse.Bool(row.Required("archived_tick_agrees_with_authoritative_winner"), "add.archived_tick_agrees_with_authoritative_winner"),
                row.Required("gamma_resolution_source"),
                row.Required("gamma_resolution_provenance_group"),
                row.Required("gamma_request_url"),
                RequireSha256(row.Required("gamma_raw_sha256"), "add.gamma_raw_sha256"),
                Parse.Long(row.Required("gamma_raw_bytes"), "add.gamma_raw_bytes"),
                Parse.Timestamp(row.Required("gamma_fetched_at_utc"), "add.gamma_fetched_at_utc"),
                Parse.Int(row.Required("agreeing_independent_resolution_source_count"), "add.agreeing_independent_resolution_source_count"),
                Parse.Decimal(row.Required("worst_price_target_size_shares"),
                    "add.worst_price_target_size_shares"),
                Parse.Decimal(row.Required("requested_notional_usd"), "add.requested_notional_usd"),
                Parse.Decimal(row.Required("filled_size_shares"), "add.filled_size_shares"),
                Parse.Bool(row.Required("won"), "add.won"),
                Parse.Decimal(row.Required("settlement_price"), "add.settlement_price"),
                Parse.Decimal(row.Required("settlement_value_usd"), "add.settlement_value_usd"),
                Parse.Decimal(row.Required("realized_pnl_usd"), "add.realized_pnl_usd"),
                Parse.Bool(row.Required("can_add"), "add.can_add"),
                row.Required("reason")));

            foreach (var add in adds)
            {
                ValidateAdd(add);
            }

            return adds;
        }
        catch (InvalidDataException exception)
        {
            blockers.Add("add-feasibility.csv does not satisfy the physical-apply v2 contract: " + exception.Message);
            return [];
        }
    }

    private static IReadOnlyList<PositionKeyTarget> ReadPositionKeys(
        string directory,
        ICollection<string> blockers)
    {
        try
        {
            var rows = CsvFile.Read(Path.Combine(directory, "position-keys.csv"), row =>
                new PositionKeyTarget(
                    row.Required("copied_trader_wallet"),
                    row.Required("asset_id"),
                    Parse.Int(row.Required("graph_order_count"), "position.graph_order_count"),
                    Parse.Int(row.Required("database_order_count"), "position.database_order_count"),
                    Parse.Int(row.Required("outside_graph_order_count"), "position.outside_graph_order_count"),
                    Parse.Int(row.Required("position_count"), "position.position_count"),
                    Parse.Int(row.Required("settlement_count"), "position.settlement_count"),
                    Parse.Bool(row.Required("exclusive"), "position.exclusive"),
                    Parse.Bool(row.Required("blocks_mutation"), "position.blocks_mutation")));

            foreach (var row in rows)
            {
                if (row.GraphOrderCount <= 0 || row.DatabaseOrderCount != row.GraphOrderCount ||
                    row.OutsideGraphOrderCount != 0 || !row.Exclusive || row.BlocksMutation ||
                    row.PositionCount < 0 || row.SettlementCount < 0)
                {
                    throw new InvalidDataException(
                        $"Position key {row.CopiedTraderWallet}/{row.AssetId} is not an exclusive exact graph aggregate.");
                }
            }

            var duplicateCount = rows.Count - rows
                .Select(row => (row.CopiedTraderWallet, row.AssetId))
                .Distinct()
                .Count();
            if (duplicateCount != 0)
            {
                throw new InvalidDataException($"position-keys.csv contains {duplicateCount:N0} duplicate keys.");
            }

            return rows;
        }
        catch (InvalidDataException exception)
        {
            blockers.Add("position-keys.csv does not satisfy the physical-apply v2 contract: " + exception.Message);
            return [];
        }
    }

    private static IReadOnlyList<ForeignKeyContract> ReadForeignKeys(
        string directory,
        ICollection<string> blockers)
    {
        try
        {
            var rows = CsvFile.Read(Path.Combine(directory, "foreign-keys.csv"), row => new ForeignKeyContract(
                row.Required("constraint_name"),
                row.Required("source_table"),
                row.Required("source_columns"),
                row.Required("target_table"),
                row.Required("target_columns"),
                row.Required("delete_action"),
                row.Required("update_action"),
                Parse.Bool(row.Required("expected"), "foreign-key.expected")));
            if (rows.Count == 0 || rows.Any(row => !row.Expected) || rows.Distinct().Count() != rows.Count)
            {
                throw new InvalidDataException(
                    "Foreign-key evidence must be nonempty, duplicate-free, and entirely reviewed/expected.");
            }
            return rows;
        }
        catch (InvalidDataException exception)
        {
            blockers.Add("foreign-keys.csv does not satisfy the physical-apply v2 contract: " + exception.Message);
            return [];
        }
    }

    private static IReadOnlyList<SchemaReferenceColumnContract> ReadSchemaReferenceColumns(
        string directory,
        ICollection<string> blockers)
    {
        try
        {
            var rows = CsvFile.Read(Path.Combine(directory, "schema-reference-columns.csv"), row =>
                new SchemaReferenceColumnContract(
                    row.Required("table_name"),
                    row.Required("column_name"),
                    row.Required("data_type"),
                    Parse.Bool(row.Required("expected"), "schema-reference.expected")));
            if (rows.Count == 0 || rows.Any(row => !row.Expected) || rows.Distinct().Count() != rows.Count)
            {
                throw new InvalidDataException(
                    "Reference-column evidence must be nonempty, duplicate-free, and entirely reviewed/expected.");
            }
            return rows;
        }
        catch (InvalidDataException exception)
        {
            blockers.Add(
                "schema-reference-columns.csv does not satisfy the physical-apply v2 contract: " + exception.Message);
            return [];
        }
    }

    private static IReadOnlyList<GraphPhysicalRowHashes> ReadGraphRowHashes(
        string directory,
        ICollection<string> blockers)
    {
        try
        {
            return CsvFile.Read(Path.Combine(directory, "graph-orders.csv"), row =>
                new GraphPhysicalRowHashes(
                    row.Required("scope"),
                    Parse.Guid(row.Required("run_id"), "graph-order.run_id"),
                    Parse.Guid(row.Required("paper_order_id"), "graph-order.paper_order_id"),
                    Parse.Guid(row.Required("signal_id"), "graph-order.signal_id"),
                    RequireSha256(row.Required("run_full_row_sha256"), "graph-order.run_full_row_sha256"),
                    RequireSha256(row.Required("order_full_row_sha256"), "graph-order.order_full_row_sha256"),
                    RequireSha256(row.Required("signal_full_row_sha256"), "graph-order.signal_full_row_sha256")));
        }
        catch (InvalidDataException exception)
        {
            blockers.Add("graph-orders.csv full-row hash contract failed: " + exception.Message);
            return [];
        }
    }

    private static IReadOnlyList<FillPhysicalRowHash> ReadFillRowHashes(
        string directory,
        ICollection<string> blockers)
    {
        try
        {
            return CsvFile.Read(Path.Combine(directory, "graph-fills.csv"), row => new FillPhysicalRowHash(
                Parse.Guid(row.Required("fill_id"), "graph-fill.fill_id"),
                Parse.Guid(row.Required("paper_order_id"), "graph-fill.paper_order_id"),
                RequireSha256(row.Required("full_row_sha256"), "graph-fill.full_row_sha256")));
        }
        catch (InvalidDataException exception)
        {
            blockers.Add("graph-fills.csv full-row hash contract failed: " + exception.Message);
            return [];
        }
    }

    private static IReadOnlyList<PositionPhysicalRowHash> ReadPositionRowHashes(
        string directory,
        ICollection<string> blockers)
    {
        try
        {
            return CsvFile.Read(Path.Combine(directory, "positions.csv"), row => new PositionPhysicalRowHash(
                Parse.Guid(row.Required("id"), "position.id"),
                row.Required("copied_trader_wallet"),
                row.Required("asset_id"),
                RequireSha256(row.Required("full_row_sha256"), "position.full_row_sha256")));
        }
        catch (InvalidDataException exception)
        {
            blockers.Add("positions.csv full-row hash contract failed: " + exception.Message);
            return [];
        }
    }

    private static IReadOnlyList<PositionSettlementPhysicalRowHash> ReadPositionSettlementRowHashes(
        string directory,
        ICollection<string> blockers)
    {
        try
        {
            return CsvFile.Read(Path.Combine(directory, "position-settlements.csv"), row =>
                new PositionSettlementPhysicalRowHash(
                    Parse.Guid(row.Required("id"), "position-settlement.id"),
                    row.Required("copied_trader_wallet"),
                    row.Required("asset_id"),
                    RequireSha256(row.Required("full_row_sha256"), "position-settlement.full_row_sha256")));
        }
        catch (InvalidDataException exception)
        {
            blockers.Add("position-settlements.csv full-row hash contract failed: " + exception.Message);
            return [];
        }
    }

    private static void ValidateOperationFootprint(
        IReadOnlyList<MainRemoval> main,
        IReadOnlyList<ChildRemoval> children,
        IReadOnlyList<AddCandidate> adds,
        IReadOnlyList<PositionKeyTarget> positionKeys,
        IReadOnlyList<GraphPhysicalRowHashes> graphRows,
        IReadOnlyList<FillPhysicalRowHash> fills,
        IReadOnlyList<PositionPhysicalRowHash> positions,
        IReadOnlyList<PositionSettlementPhysicalRowHash> settlements,
        IReadOnlyList<OperationFootprintContract> actual,
        ICollection<string> blockers)
    {
        const string measuredEvidence =
            "Exact REPEATABLE READ snapshot aggregation: count(*) and sum(pg_column_size(target_row)); " +
            "indexes, TOAST side tables, triggers and WAL overhead excluded.";
        const string insertEvidence =
            "Modeled payload row count only. Heap/index/TOAST/WAL bytes cannot be known until PostgreSQL " +
            "materializes the reviewed payload; no byte estimate is fabricated.";
        const string controlEvidence =
            "Exact singleton precondition snapshot: initialized=True;calculation_version=2;status=Running;" +
            "last_error_is_null=True. Target state is PendingHistoryCorrectionBootstrap; indexes, TOAST side " +
            "tables, triggers and WAL overhead excluded.";

        var graphEntityCount = graphRows.Count;
        var targetStrategyCount = main.Select(row => row.StrategyId)
            .Concat(children.Select(row => row.ChildStrategyId))
            .Concat(adds.Select(row => row.StrategyId)).Distinct().LongCount();
        var targetWalletCount = positionKeys.Select(row => row.CopiedTraderWallet)
            .Concat(adds.Select(row => "strategy:" + row.StrategyCode))
            .Distinct(StringComparer.Ordinal).LongCount();
        var specs = new[]
        {
            Exact("main_run_updates", "strategy_market_paper_runs", "UPDATE",
                "exact Main run_id allowlist", main.Count, requireOnePerIdentity: true),
            Exact("child_run_deletes", "strategy_market_paper_runs", "DELETE",
                "exact Child run_id allowlist", children.Count, requireOnePerIdentity: true),
            Exact("modeled_add_run_updates", "strategy_market_paper_runs", "UPDATE",
                "exact feasible Add source run_id allowlist", adds.Count, requireOnePerIdentity: true),
            Exact("graph_signal_deletes", "signals", "DELETE",
                "exact graph signal_id allowlist", graphEntityCount, requireOnePerIdentity: true),
            Exact("graph_signal_rejection_deletes", "signal_rejections", "DELETE",
                "rows whose signal_id is in the exact graph signal allowlist", graphEntityCount),
            Exact("graph_order_deletes", "paper_orders", "DELETE",
                "exact graph paper_order_id allowlist", graphEntityCount, requireOnePerIdentity: true),
            Exact("graph_fill_deletes", "paper_fills", "DELETE",
                "exact complete graph fill_id allowlist", fills.Count, requireOnePerIdentity: true),
            Exact("exclusive_position_deletes", "paper_positions", "DELETE",
                "exact rows proven exclusive by wallet+asset", positions.Count, requireOnePerIdentity: true),
            Exact("exclusive_position_settlement_deletes", "paper_position_settlements", "DELETE",
                "exact rows proven exclusive by wallet+asset", settlements.Count, requireOnePerIdentity: true),
            Exact("dashboard_projection_event_reconciliation", "dashboard_projection_events", "DELETE",
                "all rows for exact affected strategy_id set", targetStrategyCount),
            Exact("dashboard_projection_queue_reconciliation", "dashboard_projection_reconciliation_queue", "UPSERT",
                "all existing rows for exact affected strategy_id set", targetStrategyCount,
                plannedOperations: targetStrategyCount),
            new FootprintSpec("dashboard_projection_control_transition", "dashboard_projection_control", "UPDATE",
                "exact singleton_id=1 with initialized=true, calculation_version=2, status=Running, last_error IS NULL",
                1, true, true, 1, controlEvidence),
            Exact("paper_copied_trader_refresh_queue_reconciliation",
                "paper_copied_trader_performance_refresh_queue", "UPSERT",
                "all existing rows for exact affected copied_trader_wallet set", targetWalletCount,
                plannedOperations: targetWalletCount),
            Insert("signals"), Insert("paper_orders"), Insert("paper_fills"), Insert("paper_positions"),
            Insert("paper_position_settlements")
        };

        if (actual.Count != specs.Length)
        {
            blockers.Add($"Operation-footprint contract has {actual.Count:N0} rows; exactly {specs.Length:N0} are required.");
            return;
        }

        var byKey = new Dictionary<string, OperationFootprintContract>(StringComparer.Ordinal);
        foreach (var row in actual)
        {
            var key = FootprintKey(row.Scope, row.TableName, row.Operation);
            if (!byKey.TryAdd(key, row))
            {
                blockers.Add("Operation-footprint contains a duplicate scope/table/operation key: " + key);
                return;
            }
        }

        foreach (var spec in specs)
        {
            var key = FootprintKey(spec.Scope, spec.TableName, spec.Operation);
            if (!byKey.TryGetValue(key, out var row))
            {
                blockers.Add("Operation-footprint is missing reviewed target: " + key);
                continue;
            }

            var invalid = row.Selector != spec.Selector ||
                          row.SelectorIdentityCount != spec.IdentityCount ||
                          row.PlannedDirectRowOperations !=
                          (spec.PlannedOperations ?? row.SnapshotRowCount) ||
                          row.ExactSnapshotMeasurement != spec.ExactMeasurement ||
                          row.Evidence != spec.Evidence ||
                          row.SelectorIdentityCount < 0 || row.PlannedDirectRowOperations < 0;
            if (spec.ExactMeasurement)
            {
                invalid |= row.SnapshotRowCount is null or < 0 ||
                           row.SnapshotPgColumnSizeBytes is null or < 0;
                if (spec.RequireOnePerIdentity)
                {
                    invalid |= row.SnapshotRowCount != spec.IdentityCount;
                }
            }
            else
            {
                invalid |= row.SnapshotRowCount is not null || row.SnapshotPgColumnSizeBytes is not null;
            }

            if (invalid)
            {
                blockers.Add("Operation-footprint row differs from the reviewed contract: " + key);
            }
        }

        return;

        FootprintSpec Exact(
            string scope,
            string table,
            string operation,
            string selector,
            long identities,
            bool requireOnePerIdentity = false,
            long? plannedOperations = null) =>
            new(scope, table, operation, selector, identities, true, requireOnePerIdentity,
                plannedOperations, measuredEvidence);

        FootprintSpec Insert(string table) =>
            new("modeled_add_inserts", table, "INSERT",
                "one deterministic correction entity per exact feasible Add run", adds.Count,
                false, false, adds.Count, insertEvidence);
    }

    private static string FootprintKey(string scope, string table, string operation) =>
        scope + "/" + table + "/" + operation;

    private sealed record FootprintSpec(
        string Scope,
        string TableName,
        string Operation,
        string Selector,
        long IdentityCount,
        bool ExactMeasurement,
        bool RequireOnePerIdentity,
        long? PlannedOperations,
        string Evidence);

    private static void ValidatePhysicalHashCoverage(
        IReadOnlyList<MainRemoval> main,
        IReadOnlyList<ChildRemoval> children,
        IReadOnlyList<GraphPhysicalRowHashes> graphRows,
        IReadOnlyList<FillPhysicalRowHash> fills,
        ICollection<string> blockers)
    {
        var removalRuns = main.Select(row => row.RunId).Concat(children.Select(row => row.ChildRunId)).ToHashSet();
        var removalOrders = main.Select(row => row.OrderId).Concat(children.Select(row => row.ChildOrderId)).ToHashSet();
        var removalSignals = main.Select(row => row.SignalId).Concat(children.Select(row => row.ChildSignalId)).ToHashSet();
        if (!removalRuns.SetEquals(graphRows.Select(row => row.RunId)) ||
            !removalOrders.SetEquals(graphRows.Select(row => row.OrderId)) ||
            !removalSignals.SetEquals(graphRows.Select(row => row.SignalId)) ||
            graphRows.Select(row => row.RunId).Distinct().Count() != graphRows.Count)
        {
            blockers.Add("graph-orders.csv full-row hashes do not cover the exact removal run/order/signal set.");
        }

        if (!removalOrders.SetEquals(fills.Select(row => row.OrderId)) || fills.Count != removalOrders.Count ||
            fills.Select(row => row.FillId).Distinct().Count() != fills.Count)
        {
            blockers.Add("graph-fills.csv must contain exactly one full-row-hashed FAK fill per removal order.");
        }
    }

    private static void ValidateAdd(AddCandidate add)
    {
        ModeledAddPayloadValidator.Validate(add);

        if (!add.CanAdd)
        {
            throw new InvalidDataException($"Run {add.RunId:D} has can_add=false: {add.Reason}");
        }

        var expectedPrice = add.Kind.Contains("LowEnter", StringComparison.OrdinalIgnoreCase) ||
                            add.Kind.Contains("LowerEnter", StringComparison.OrdinalIgnoreCase)
            ? 0.50m
            : 0.52m;
        if (add.AssumedFillPrice != expectedPrice)
        {
            throw new InvalidDataException(
                $"Run {add.RunId:D} fill price {add.AssumedFillPrice} disagrees with kind '{add.Kind}' ({expectedPrice}).");
        }

        if (add.HistoricalStakeMultiplier <= 0 || add.GammaOrderMinSize <= 0 ||
            add.RawWorstPriceNotionalUsd <= 0 || add.RoundedWorstPriceNotionalUsd <= 0 ||
            add.WorstPriceTargetSizeShares <= 0 || add.RequestedNotionalUsd <= 0 ||
            add.FilledSizeShares <= 0)
        {
            throw new InvalidDataException($"Run {add.RunId:D} has non-positive modeled sizing values.");
        }

        var rawWorstPriceNotional = add.GammaOrderMinSize * 0.99m * 1.10m *
                                    add.HistoricalStakeMultiplier;
        var roundedWorstPriceNotional = decimal.Ceiling(rawWorstPriceNotional);
        var worstPriceTargetSize = decimal.Ceiling(roundedWorstPriceNotional / 0.99m * 100m) / 100m;
        var requestedNotional = decimal.Round(worstPriceTargetSize * 0.99m, 8,
            MidpointRounding.AwayFromZero);
        var filledSize = decimal.Round(requestedNotional / add.AssumedFillPrice, 8,
            MidpointRounding.AwayFromZero);
        if (add.RawWorstPriceNotionalUsd != rawWorstPriceNotional ||
            add.RoundedWorstPriceNotionalUsd != roundedWorstPriceNotional ||
            add.WorstPriceTargetSizeShares != worstPriceTargetSize ||
            add.RequestedNotionalUsd != requestedNotional || add.FilledSizeShares != filledSize)
        {
            throw new InvalidDataException(
                $"Run {add.RunId:D} does not satisfy the exact 0.99 worst-price sizing and modeled fill formula.");
        }

        if (add.ModeledSettledAtUtc < add.ModeledEntryAtUtc ||
            !add.ModeledSettlementTimestampSource.Equals(
                "modeled_earliest_observable_resolution=max(market_end_utc,resolution_ledger_first_received_at_utc)",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Run {add.RunId:D} has an invalid modeled timestamp contract.");
        }

        var expectedNotional = decimal.Round(
            add.AssumedFillPrice * add.FilledSizeShares, 8, MidpointRounding.AwayFromZero);
        if (expectedNotional != add.RequestedNotionalUsd)
        {
            throw new InvalidDataException(
                $"Run {add.RunId:D} modeled notional mismatch: price*size={expectedNotional}, requested={add.RequestedNotionalUsd}.");
        }

        var expectedPriceAtSettlement = add.Won ? 1m : 0m;
        var expectedValue = add.Won ? add.FilledSizeShares : 0m;
        var outcomeWon = add.SelectedOutcome.Equals(add.ResolvedWinningOutcome,
            StringComparison.OrdinalIgnoreCase);
        if (add.SettlementPrice != expectedPriceAtSettlement || add.SettlementValueUsd != expectedValue ||
            add.RealizedPnlUsd != expectedValue - add.RequestedNotionalUsd || add.Won != outcomeWon ||
            !add.SelectedOutcome.Equals("Up", StringComparison.Ordinal) ||
            (add.Won && !add.SelectedTokenId.Equals(add.ResolvedWinningTokenId, StringComparison.Ordinal)) ||
            (!add.Won && add.SelectedTokenId.Equals(add.ResolvedWinningTokenId, StringComparison.Ordinal)) ||
            !(add.ResolvedWinningOutcome.Equals("Up", StringComparison.Ordinal) ||
              add.ResolvedWinningOutcome.Equals("Down", StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"Run {add.RunId:D} has internally inconsistent modeled settlement math.");
        }

        if (add.AgreeingIndependentResolutionSourceCount < 2 || add.GammaRawBytes <= 0 ||
            !add.GammaResolutionSource.Contains("Gamma", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Run {add.RunId:D} lacks the required independent resolution evidence.");
        }

        var gammaUri = Uri.TryCreate(add.GammaRequestUrl, UriKind.Absolute, out var parsed) ? parsed : null;
        if (gammaUri is null || gammaUri.Scheme != Uri.UriSchemeHttps ||
            !gammaUri.Host.Equals("gamma-api.polymarket.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Run {add.RunId:D} has an unexpected Gamma request URL.");
        }

        var ledgerProof = add.ResolutionLedgerRawValidated && add.ResolutionLedgerRawBytes > 0 &&
                          IsSha256(add.ResolutionLedgerRawSha256);
        var archivedProof = add.ArchivedTickSampleCount >= 2 && add.ArchivedTickAgreesWithAuthoritativeWinner &&
                            !string.IsNullOrWhiteSpace(add.ArchivedTickSource) &&
                            !string.IsNullOrWhiteSpace(add.ArchivedTickProvenanceGroup);
        if (!ledgerProof && !archivedProof)
        {
            throw new InvalidDataException($"Run {add.RunId:D} has neither validated ledger raw JSON nor archived tick proof.");
        }
    }

    private static void ValidateIdentities(
        IReadOnlyList<MainRemoval> main,
        IReadOnlyList<ChildRemoval> children,
        IReadOnlyList<AddCandidate> adds,
        ICollection<string> blockers)
    {
        AddDuplicateBlocker(main.Select(row => row.RunId), "main run IDs", blockers);
        AddDuplicateBlocker(children.Select(row => row.ChildRunId), "child run IDs", blockers);
        AddDuplicateBlocker(adds.Select(row => row.RunId), "add run IDs", blockers);
        AddDuplicateBlocker(main.Select(row => row.OrderId).Concat(children.Select(row => row.ChildOrderId)),
            "removal order IDs", blockers);
        AddDuplicateBlocker(main.Select(row => row.SignalId).Concat(children.Select(row => row.ChildSignalId)),
            "removal signal IDs", blockers);

        var overlap = main.Select(row => row.RunId).Concat(children.Select(row => row.ChildRunId))
            .Intersect(adds.Select(row => row.RunId)).Count();
        if (overlap != 0)
        {
            blockers.Add($"Removal and add run sets overlap by {overlap:N0} IDs.");
        }

        if (adds.Count != 327)
        {
            blockers.Add($"This correction contract requires exactly 327 modeled adds; graph contains {adds.Count:N0}.");
        }
    }

    private static void AddDuplicateBlocker<T>(IEnumerable<T> values, string label, ICollection<string> blockers)
        where T : notnull
    {
        var array = values.ToArray();
        var unique = array.Distinct().Count();
        if (unique != array.Length)
        {
            blockers.Add($"Graph contains {array.Length - unique:N0} duplicate {label}.");
        }
    }

    internal static string Sha256File(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required file does not exist.", path);
        }

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static string RequireSha256(string value, string field)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!IsSha256(normalized))
        {
            throw new InvalidDataException($"{field} is not a 64-character SHA-256 digest.");
        }

        return normalized;
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static JsonElement RequiredObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException($"Required JSON object '{name}' is missing.");

    private static JsonElement RequiredArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidDataException($"Required JSON array '{name}' is missing.");

    private static string RequiredString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException($"Required JSON string '{name}' is missing.");

    private static int RequiredInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidDataException($"Required JSON integer '{name}' is missing.");

    private static long RequiredLong(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : throw new InvalidDataException($"Required JSON integer '{name}' is missing.");

    private static bool RequiredBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new InvalidDataException($"Required JSON boolean '{name}' is missing.");
}
