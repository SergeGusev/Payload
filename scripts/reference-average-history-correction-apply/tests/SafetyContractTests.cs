using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace ReferenceAverageHistoryCorrectionApply.Tests;

public sealed class SafetyContractTests
{
    [Fact]
    public void UuidV5MatchesRfcExample()
    {
        var dnsNamespace = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        var actual = DeterministicGuid.CreateVersion5(dnsNamespace, "www.widgets.com");

        Assert.Equal(new Guid("21f7f8de-8051-5b89-8680-0195ef798b6a"), actual);
    }

    [Fact]
    public void CorrectionEntityIdsAreStableAndEntitySpecific()
    {
        var graphHash = new string('a', 64);
        var run = new Guid("11111111-2222-3333-4444-555555555555");

        var first = DeterministicGuid.Create(graphHash, run, "signal");
        var repeated = DeterministicGuid.Create(graphHash, run, "signal");
        var order = DeterministicGuid.Create(graphHash, run, "paper_order");

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, order);
        Assert.Equal(5, first.ToByteArray()[7] >> 4);
    }

    [Fact]
    public void CsvReaderHandlesQuotedCommasQuotesAndNewlines()
    {
        var directory = NewTempDirectory();
        var path = Path.Combine(directory, "quoted.csv");
        File.WriteAllText(path, "id,text\r\n1,\"a,b\"\r\n2,\"line1\nline2 \"\"quoted\"\"\"\r\n",
            new UTF8Encoding(false));

        var rows = CsvFile.Read(path, row => (row.Required("id"), row.Required("text")));

        Assert.Equal(2, rows.Count);
        Assert.Equal("a,b", rows[0].Item2);
        Assert.Equal("line1\nline2 \"quoted\"", rows[1].Item2);
        Assert.Equal(2, CsvFile.CountDataRows(path));
    }

    [Fact]
    public void CommandLineDefaultsToReadOnlyPreflightAndRejectsDualMutationFlags()
    {
        var options = CommandLine.Parse(
        [
            "--host", DatabaseConnection.RequiredHost,
            "--port", DatabaseConnection.RequiredPort.ToString(),
            "--database", DatabaseConnection.RequiredDatabase,
            "--cutoff", "2026-07-27T13:24:05.932282Z",
            "--graph-dir", ".",
            "--graph-manifest-sha256", new string('b', 64)
        ]);

        Assert.Equal(OperationMode.Preflight, options.Mode);
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(
        [
            "--apply", "--rollback",
            "--host", DatabaseConnection.RequiredHost,
            "--port", "5432",
            "--database", DatabaseConnection.RequiredDatabase,
            "--cutoff", "2026-07-27T13:24:05.932282Z",
            "--graph-dir", ".",
            "--graph-manifest-sha256", new string('b', 64)
        ]));
    }

    [Fact]
    public void CommandLineRejectsUnscopedEvidenceAndRequiresChildEvidenceForPostChildGate()
    {
        var common = new[]
        {
            "--host", DatabaseConnection.RequiredHost,
            "--port", DatabaseConnection.RequiredPort.ToString(),
            "--database", DatabaseConnection.RequiredDatabase,
            "--cutoff", "2026-07-27T13:24:05.932282Z",
            "--graph-dir", ".",
            "--graph-manifest-sha256", new string('b', 64)
        };

        Assert.Throws<ArgumentException>(() => CommandLine.Parse(common.Concat(new[]
        {
            "--operator-attestation", "operator.json",
            "--operator-attestation-sha256", new string('c', 64)
        }).ToArray()));
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(common.Concat(new[]
        {
            "--child-refresh-attestation", "child.json",
            "--child-refresh-attestation-sha256", new string('d', 64)
        }).ToArray()));

        var exception = Assert.Throws<ArgumentException>(() => CommandLine.Parse(common.Concat(new[]
        {
            "--post-child-gate",
            "--rollback-manifest", "backup-manifest.json",
            "--staging-dir", @"D:\CodexTemp\runs\not-used\staging",
            "--operator-attestation", "operator.json",
            "--operator-attestation-sha256", new string('c', 64)
        }).ToArray()));
        Assert.Contains("requires SHA-pinned external child-refresh attestation", exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MutationSqlNeverDisablesAllTriggersOrTouchesLiveRows()
    {
        var sql = CorrectionSql.ApplySql + CorrectionSql.DeleteScopeSql;

        Assert.DoesNotContain("session_replication_role", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DISABLE TRIGGER ALL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM public.live_orders", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE public.live_orders", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO public.live_orders", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE public.live_orders", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paper_lost_counter =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("modeled_mutation_payload_json", sql, StringComparison.Ordinal);
        Assert.Contains("btc_updown5m_fak_taker_paper", CorrectionSql.AppliedVerificationSql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("target_size_shares', target.filled_size_shares", sql,
            StringComparison.Ordinal);
        Assert.Contains("reference_average_history_correction_v2_would_skip", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN public.signals row ON row.id = target.signal_id",
            CorrectionSql.AddIdCollisionSql, StringComparison.Ordinal);
        Assert.Contains("JOIN public.paper_orders row ON row.id = target.order_id",
            CorrectionSql.AddIdCollisionSql, StringComparison.Ordinal);
        Assert.Contains("JOIN public.paper_fills row ON row.id = target.fill_id",
            CorrectionSql.AddIdCollisionSql, StringComparison.Ordinal);
        Assert.Contains("JOIN public.paper_positions row ON row.id = target.position_id",
            CorrectionSql.AddIdCollisionSql, StringComparison.Ordinal);
        Assert.Contains("JOIN public.paper_position_settlements row ON row.id = target.settlement_id",
            CorrectionSql.AddIdCollisionSql, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsafeGraphManifestNeverAuthorizesMutation()
    {
        var directory = NewTempDirectory();
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["main-removals.csv"] = "run_id,strategy_id,strategy_code,market_id,paper_order_id,signal_id,asset_id,outcome,copied_trader_wallet,corrected_skip_reason,corrected_skipped_updated_at_utc,restored_base_stake_usd,historical_effective_stake_usd,historical_target_notional_usd,historical_stake_sizing_source,stake_sizing_proof_sha256,classifier_reason,classifier_action,signal_preview_manifest_sha256,replay_classifier_sha256,replay_evidence_json,replay_evidence_sha256,graph_state_sha256,fill_set_sha256",
            ["child-removals.csv"] = "parent_run_id,child_run_id,child_strategy_id,child_strategy_code,market_id,child_paper_order_id,child_signal_id,graph_state_sha256,fill_set_sha256",
            ["live-shadow-overlaps.csv"] = "row_id",
            ["dependencies.csv"] = "blocks_mutation",
            ["reconciliation-targets.csv"] = "target_id,table_name,key_scope,method_id,required_action,reason,blocks_mutation,target_contract_sha256",
            ["operation-footprint.csv"] = "scope,table_name,operation,selector,selector_identity_count,snapshot_row_count,snapshot_pg_column_size_bytes,planned_direct_row_operations,exact_snapshot_measurement,evidence",
            ["position-keys.csv"] = "copied_trader_wallet,asset_id,graph_order_count,database_order_count,outside_graph_order_count,position_count,settlement_count,exclusive,blocks_mutation,details",
            ["foreign-keys.csv"] = "constraint_name,source_table,source_columns,target_table,target_columns,delete_action,update_action,expected",
            ["schema-reference-columns.csv"] = "table_name,column_name,data_type,expected",
            ["graph-orders.csv"] = "scope,run_id,paper_order_id,signal_id,run_full_row_sha256,order_full_row_sha256,signal_full_row_sha256",
            ["graph-fills.csv"] = "fill_id,paper_order_id,full_row_sha256",
            ["positions.csv"] = "id,copied_trader_wallet,asset_id,full_row_sha256",
            ["position-settlements.csv"] = "id,copied_trader_wallet,asset_id,full_row_sha256",
            ["add-feasibility.csv"] = "run_id,strategy_id,strategy_code,market_id,condition_id,asset,kind,add_source_state_sha256,add_source_run_full_row_sha256,modeled_entry_at_utc,modeled_settled_at_utc,modeled_settlement_timestamp_source,settlement_category,modeled_raw_decision_json,modeled_raw_decision_sha256,modeled_fill_evidence,modeled_mutation_payload_json,modeled_mutation_payload_sha256,assumed_fill_price,historical_stake_multiplier,gamma_order_min_size,raw_worst_price_notional_usd,rounded_worst_price_notional_usd,selected_outcome,selected_token_id,resolved_winning_outcome,resolved_winning_token_id,resolution_ledger_source,resolution_ledger_provenance_group,resolution_ledger_raw_event_type,resolution_ledger_raw_sha256,resolution_ledger_raw_bytes,resolution_ledger_first_received_at_utc,resolution_ledger_last_received_at_utc,resolution_ledger_raw_validated,archived_tick_source,archived_tick_provenance_group,archived_tick_sample_count,archived_tick_agrees_with_authoritative_winner,gamma_resolution_source,gamma_resolution_provenance_group,gamma_request_url,gamma_raw_sha256,gamma_raw_bytes,gamma_fetched_at_utc,agreeing_independent_resolution_source_count,worst_price_target_size_shares,requested_notional_usd,filled_size_shares,won,settlement_price,settlement_value_usd,realized_pnl_usd,can_add,reason",
            ["invariant-errors.csv"] = "code"
        };
        var evidence = new List<object>();
        foreach (var pair in headers)
        {
            var path = Path.Combine(directory, pair.Key);
            File.WriteAllText(path, pair.Value + "\n", new UTF8Encoding(false));
            evidence.Add(new { file_name = pair.Key, row_count = 0, sha256 = Sha(path) });
        }

        var manifest = new
        {
            schema_version = 2,
            tool = "reference-average-history-correction-graph-preview",
            cutoff_utc = "2026-07-27T13:24:05.932282Z",
            safety = new
            {
                invariant_errors = 0,
                live_shadow_blockers = 0,
                semantic_dependency_blockers = 0,
                shared_position_key_blockers = 0,
                required_reconciliation_plan_blockers = 1,
                infeasible_adds = 0,
                safe_to_prepare_separate_mutation = false
            },
            contract = new
            {
                reconciliation = new
                {
                    schema_version = ReconciliationPlanContract.SchemaVersion,
                    algorithm = ReconciliationPlanContract.Algorithm,
                    serialization = ReconciliationPlanContract.Serialization,
                    contract_sha256 = ReconciliationPlanContract.ContractSha256,
                    target_count = ReconciliationPlanContract.Expected.Count,
                    blocking_target_count = ReconciliationPlanContract.Expected.Count(item => item.BlocksMutation),
                    apply_handshake = ReconciliationPlanContract.ApplyHandshake
                }
            },
            files = evidence
        };
        var manifestPath = Path.Combine(directory, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest), new UTF8Encoding(false));
        var options = new ToolOptions(
            Mode: OperationMode.Preflight,
            Host: DatabaseConnection.RequiredHost,
            Port: 5432,
            Database: DatabaseConnection.RequiredDatabase,
            CutoffUtc: DateTimeOffset.Parse("2026-07-27T13:24:05.932282Z"),
            GraphDirectory: directory,
            GraphManifestSha256: Sha(manifestPath),
            StagingDirectory: null,
            DurableBackupDirectory: null,
            FullBackupDirectory: null,
            FullBackupHashManifestPath: null,
            FullBackupMetadataManifestPath: null,
            FullBackupRestoreEvidencePath: null,
            FullBackupRestoredRowCountManifestPath: null,
            FullBackupSchemaManifestPath: null,
            PreparedPackageSha256: null,
            RollbackManifestPath: null,
            OperatorAttestationPath: null,
            OperatorAttestationSha256: null,
            ChildRefreshAttestationPath: null,
            ChildRefreshAttestationSha256: null,
            HeartbeatStaleMinutes: 5);

        var package = GraphPackageReader.Read(options);

        Assert.Contains(package.BlockingErrors, error => error.Contains("safe_to_prepare", StringComparison.Ordinal));
        Assert.Contains(package.BlockingErrors, error => error.Contains("reconciliation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(package.BlockingErrors, error => error.Contains("exactly 327", StringComparison.Ordinal));
    }

    [Fact]
    public void CanonicalHashesChangeWhenMutationScopeStateChanges()
    {
        var run = new Guid("11111111-2222-3333-4444-555555555555");
        var state = new AddSourceState(
            run, new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "strategy-code", "market", "condition",
            "Skipped", "reason", DateTimeOffset.Parse("2026-07-27T12:00:00Z"),
            DateTimeOffset.Parse("2026-07-27T12:05:00Z"), 1m, null, null, null, null, null, null, null,
            null, null, null, null, "{\"x\":1}", "slug");

        var original = SourceStateHashVerifier.HashAddSource(state);
        var changed = SourceStateHashVerifier.HashAddSource(state with { StakeUsd = 1.01m });

        Assert.Equal(64, original.Length);
        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void FrozenReconciliationHandshakeAcceptsOnlyTheExactFourteenRows()
    {
        var manifest = ReconciliationManifest();

        ReconciliationPlanContract.ValidateManifestAndRows(manifest, ReconciliationPlanContract.Expected);

        var changed = ReconciliationPlanContract.Expected.ToArray();
        changed[0] = changed[0] with { MethodId = changed[0].MethodId + "-tampered" };
        Assert.Throws<InvalidDataException>(() =>
            ReconciliationPlanContract.ValidateManifestAndRows(manifest, changed));
        Assert.Throws<InvalidDataException>(() =>
            ReconciliationPlanContract.ValidateManifestAndRows(manifest,
                ReconciliationPlanContract.Expected.Skip(1).ToArray()));
    }

    [Fact]
    public void MutationAndMaintenanceSqlRemainScopedAndNeverBypassConstraints()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(CurrentSourceFile())!, "..", "CorrectionDatabase.cs")));
        var backupSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(CurrentSourceFile())!, "..", "BackupStore.cs")));
        var sql = CorrectionSql.ApplySql + CorrectionSql.DeleteScopeSql +
                  CorrectionSql.RefreshCopiedPerformanceSql + source + backupSource;

        Assert.DoesNotContain("session_replication_role", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DISABLE TRIGGER ALL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IN SHARE ROW EXCLUSIVE MODE NOWAIT", source, StringComparison.Ordinal);
        Assert.Contains("StrategyIds.DateDependentStrategyVariants", source, StringComparison.Ordinal);
        Assert.Contains("PostgresDashboardProjectionRepository", source, StringComparison.Ordinal);
        Assert.Contains("pg_try_advisory_lock", source, StringComparison.Ordinal);
        Assert.Contains("MaintenanceSessionLease", source, StringComparison.Ordinal);
        Assert.Contains("ValidateRestoreScopeAsync", backupSource, StringComparison.Ordinal);
        Assert.Contains("FileShare.Read", backupSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PreparedFileHashDetectsSameSizeSameTimestampTamper()
    {
        var directory = NewTempDirectory();
        var path = Path.Combine(directory, "immutable.bin");
        File.WriteAllText(path, "AAAA", new UTF8Encoding(false));
        var expected = Sha(path);
        var timestamp = File.GetLastWriteTimeUtc(path);

        File.WriteAllText(path, "BBBB", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, timestamp);

        Assert.Throws<InvalidDataException>(() =>
            PreparedPackageStore.OpenReadLockedAndVerify(path, expected));
    }

    [Fact]
    public void PreparedFileLeaseDeniesWritesUntilVerificationScopeEnds()
    {
        var directory = NewTempDirectory();
        var path = Path.Combine(directory, "immutable.bin");
        File.WriteAllText(path, "sealed", new UTF8Encoding(false));

        using (PreparedPackageStore.OpenReadLockedAndVerify(path, Sha(path)))
        {
            Assert.Throws<IOException>(() =>
                File.WriteAllText(path, "mutate", new UTF8Encoding(false)));
        }

        File.WriteAllText(path, "released", new UTF8Encoding(false));
        Assert.Equal("released", File.ReadAllText(path));
    }

    [Fact]
    public void RestoreListIsParsedFromTheExactHashedBytesAndTableSetMustMatch()
    {
        var directory = NewTempDirectory();
        var path = Path.Combine(directory, "restore-list.txt");
        File.WriteAllText(path,
            "1; 1259 1 TABLE public alpha owner\n2; 1259 2 TABLE public beta owner\n",
            new UTF8Encoding(false));
        var expectedHash = Sha(path);

        var tables = ExternalBackupVerifier.ReadPublicTableSetFromRestoreList(path, expectedHash);

        Assert.Equal(new[] { "public.alpha", "public.beta" }, tables.Order(StringComparer.Ordinal));
        Assert.Throws<InvalidDataException>(() => ExternalBackupVerifier.RequireExactTableSet(
            tables, new HashSet<string>(["public.alpha", "public.gamma"], StringComparer.Ordinal),
            "test substitution"));

        File.WriteAllText(path,
            "1; 1259 1 TABLE public alpha owner\n2; 1259 2 TABLE public zeta owner\n",
            new UTF8Encoding(false));
        Assert.Throws<InvalidDataException>(() =>
            ExternalBackupVerifier.ReadPublicTableSetFromRestoreList(path, expectedHash));
    }

    [Fact]
    public void OperatorAttestationMustBeFreshPinnedAndStoppedDisabled()
    {
        var directory = NewTempDirectory();
        var path = Path.Combine(directory, "operator-attestation.json");
        WriteAttestation(path, DateTimeOffset.UtcNow, freeBytes: 123_456, state: "Stopped");
        var options = BaseOptions() with
        {
            OperatorAttestationPath = path,
            OperatorAttestationSha256 = Sha(path)
        };

        var valid = OperatorAttestationStore.Read(options, required: true);
        Assert.NotNull(valid);
        Assert.Equal(123_456, valid.FreeBytes);

        WriteAttestation(path, DateTimeOffset.UtcNow.AddHours(-1), freeBytes: 123_456, state: "Stopped");
        options = options with { OperatorAttestationSha256 = Sha(path) };
        Assert.Throws<InvalidDataException>(() => OperatorAttestationStore.Read(options, required: true));

        WriteAttestation(path, DateTimeOffset.UtcNow, freeBytes: 123_456, state: "Running");
        options = options with { OperatorAttestationSha256 = Sha(path) };
        Assert.Throws<InvalidDataException>(() => OperatorAttestationStore.Read(options, required: true));
    }

    [Fact]
    public void ChildRefreshAttestationPinsExactCompletionLineAndSupportsZeroAssignments()
    {
        var directory = NewTempDirectory();
        var logName = "polycopytrader-service-test.log";
        var logPath = Path.Combine(directory, logName);
        var completed = DateTimeOffset.UtcNow.AddMinutes(-2);
        completed = new DateTimeOffset(completed.Year, completed.Month, completed.Day,
            completed.Hour, completed.Minute, completed.Second, completed.Millisecond, TimeSpan.Zero);
        var line = $"[{completed:yyyy-MM-dd HH:mm:ss.fff zzz} INF] " +
                   "BTC Up or Down 5m child-parent assignments refreshed. Children=4 ActiveParents=0";
        File.WriteAllText(logPath, "[unrelated]\n" + line + "\n", new UTF8Encoding(false));
        var attestationPath = Path.Combine(directory, "child-refresh.json");
        var payload = new
        {
            schema_version = 1,
            host = DatabaseConnection.RequiredHost,
            port = DatabaseConnection.RequiredPort,
            database = DatabaseConnection.RequiredDatabase,
            service_name = "PolyCopyTrader.Service",
            service_log_file_name = logName,
            service_log_sha256 = Sha(logPath),
            completion_log_line = line,
            refresh_completed_at_utc = completed,
            children = 4,
            active_parents = 0,
            collection_method = "serilog_plaintext_exact_line_sha256_plus_operator_capture_v1",
            observer = "test",
            observed_at_utc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(attestationPath, JsonSerializer.Serialize(payload), new UTF8Encoding(false));
        var options = BaseOptions() with
        {
            ChildRefreshAttestationPath = attestationPath,
            ChildRefreshAttestationSha256 = Sha(attestationPath)
        };

        var verified = ChildRefreshAttestationStore.Read(options, completed.AddMinutes(-1));

        Assert.Equal(0, verified.Attestation.ActiveParents);
        File.AppendAllText(logPath, "tamper", new UTF8Encoding(false));
        Assert.Throws<InvalidDataException>(() =>
            ChildRefreshAttestationStore.Read(options, completed.AddMinutes(-1)));
    }

    private static GraphManifest ReconciliationManifest() => new(
        2,
        "reference-average-history-correction-graph-preview",
        DateTimeOffset.Parse("2026-07-27T13:24:05.932282Z"),
        new string('a', 64),
        ".",
        new Dictionary<string, GraphFileEvidence>(),
        0,
        0,
        0,
        0,
        ReconciliationPlanContract.Expected.Count(item => item.BlocksMutation),
        0,
        false,
        ReconciliationPlanContract.SchemaVersion,
        ReconciliationPlanContract.Algorithm,
        ReconciliationPlanContract.Serialization,
        ReconciliationPlanContract.ContractSha256,
        ReconciliationPlanContract.Expected.Count,
        ReconciliationPlanContract.Expected.Count(item => item.BlocksMutation),
        ReconciliationPlanContract.ApplyHandshake);

    private static ToolOptions BaseOptions() => new(
        Mode: OperationMode.Preflight,
        Host: DatabaseConnection.RequiredHost,
        Port: DatabaseConnection.RequiredPort,
        Database: DatabaseConnection.RequiredDatabase,
        CutoffUtc: DateTimeOffset.Parse("2026-07-27T13:24:05.932282Z"),
        GraphDirectory: ".",
        GraphManifestSha256: new string('a', 64),
        StagingDirectory: null,
        DurableBackupDirectory: null,
        FullBackupDirectory: null,
        FullBackupHashManifestPath: null,
        FullBackupMetadataManifestPath: null,
        FullBackupRestoreEvidencePath: null,
        FullBackupRestoredRowCountManifestPath: null,
        FullBackupSchemaManifestPath: null,
        PreparedPackageSha256: null,
        RollbackManifestPath: null,
        OperatorAttestationPath: null,
        OperatorAttestationSha256: null,
        ChildRefreshAttestationPath: null,
        ChildRefreshAttestationSha256: null,
        HeartbeatStaleMinutes: 5);

    private static void WriteAttestation(
        string path,
        DateTimeOffset observedAt,
        long freeBytes,
        string state)
    {
        var payload = new
        {
            schema_version = 1,
            host = DatabaseConnection.RequiredHost,
            port = DatabaseConnection.RequiredPort,
            database = DatabaseConnection.RequiredDatabase,
            data_directory = @"C:\PostgreSQL\18\data",
            data_volume = "C:",
            free_bytes = freeBytes,
            service_name = "PolyCopyTrader.Service",
            service_state = state,
            service_start_mode = "Disabled",
            collection_method = "windows_service_and_driveinfo_local_capture_v1",
            observer = "test",
            observed_at_utc = observedAt
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload), new UTF8Encoding(false));
    }

    private static string CurrentSourceFile([CallerFilePath] string path = "") => path;

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "reference-history-apply-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Sha(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
