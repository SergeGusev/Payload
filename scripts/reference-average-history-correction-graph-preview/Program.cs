using System.Data;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Npgsql;

namespace ReferenceAverageHistoryCorrectionGraphPreview;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(Options.HelpText);
                return 0;
            }

            using var cancellationSource = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellationSource.Cancel();
            };
            return await RunAsync(options, cancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Graph preview cancelled. No database writes were issued.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Graph preview failed: " + exception.Message);
            Console.Error.WriteLine("No database writes were issued. Any *.partial output is incomplete.");
            return 1;
        }
    }

    private static async Task<int> RunAsync(Options options, CancellationToken cancellationToken)
    {
        var repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
        var input = await SignalPreviewInputReader.LoadAsync(
            options.SignalPreviewDirectory,
            options.CutoffUtc,
            cancellationToken);
        if (!string.Equals(
                input.ManifestSha256,
                options.SignalPreviewManifestSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Loaded signal-preview manifest hash {input.ManifestSha256} does not match --signal-preview-manifest-sha256.");
        }
        var childCatalogHash = ChildCatalogParser.ComputeSha256(repositoryRoot);
        ChildCatalogParser.RequirePinnedSha256(
            childCatalogHash,
            CorrectionContract.RequiredInputCatalogSourceSha256);
        var childStrategies = ChildCatalogParser.ParseAndValidate(repositoryRoot);
        PrepareOutputDirectory(options.OutputDirectory, input.Directory);

        var liveGammaByMarket = await LiveGammaResolutionReader.FetchAsync(
            input.Adds.Select(item => item.MarketId),
            cancellationToken);

        var rawConnectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(rawConnectionString))
        {
            throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
        }

        var builder = new NpgsqlConnectionStringBuilder(rawConnectionString)
        {
            Host = CorrectionContract.RequiredHost,
            Port = CorrectionContract.RequiredPort,
            Database = CorrectionContract.RequiredDatabase,
            SearchPath = "pg_catalog,public",
            Pooling = false,
            Multiplexing = false,
            ApplicationName = "reference-average-history-correction-graph-preview-read-only",
            Timeout = Math.Min(options.CommandTimeoutSeconds, 30),
            CommandTimeout = options.CommandTimeoutSeconds,
            IncludeErrorDetail = false
        };

        GraphSnapshot snapshot;
        var rolledBack = false;
        await using (var connection = new NpgsqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
            try
            {
                snapshot = await GraphDatabaseReader.ReadAsync(
                    connection,
                    transaction,
                    builder,
                    input,
                    childStrategies,
                    liveGammaByMarket,
                    options.CommandTimeoutSeconds,
                    cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                rolledBack = true;
            }
            finally
            {
                if (!rolledBack)
                {
                    try
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                        rolledBack = true;
                    }
                    catch
                    {
                        // Connection disposal remains the final rollback boundary.
                    }
                }
            }
        }

        snapshot = snapshot with { TransactionRolledBack = rolledBack };
        var output = new DeterministicCsvOutput(options.OutputDirectory);
        await WriteOutputsAsync(output, snapshot, cancellationToken);

        var liveBlockers = snapshot.LiveShadowOverlaps.Count(item => item.BlocksMutation);
        var dependencyBlockers = snapshot.Dependencies.Count(item => item.BlocksMutation);
        var positionBlockers = snapshot.PositionKeys.Count(item => item.BlocksMutation);
        var reconciliationBlockers = snapshot.ReconciliationTargets.Count(item => item.BlocksMutation);
        var infeasibleAdds = snapshot.Adds.Count(item => !item.CanAdd);
        var invariantCount = snapshot.InvariantErrors.Count;
        var footprintSnapshotRows = snapshot.OperationFootprint
            .Where(item => item.SnapshotRowCount is not null)
            .Sum(item => item.SnapshotRowCount!.Value);
        var footprintSnapshotBytes = snapshot.OperationFootprint
            .Where(item => item.SnapshotPgColumnSizeBytes is not null)
            .Sum(item => item.SnapshotPgColumnSizeBytes!.Value);
        var directRowOperationFloor = snapshot.OperationFootprint.Sum(item => item.PlannedDirectRowOperations);
        var projectionEventFootprint = snapshot.OperationFootprint.Single(item =>
            string.Equals(item.Scope, "dashboard_projection_event_reconciliation", StringComparison.Ordinal));
        var projectionQueueFootprint = snapshot.OperationFootprint.Single(item =>
            string.Equals(item.Scope, "dashboard_projection_queue_reconciliation", StringComparison.Ordinal));
        var safe = rolledBack && invariantCount == 0 && liveBlockers == 0 &&
                   dependencyBlockers == 0 && positionBlockers == 0 && infeasibleAdds == 0 &&
                   reconciliationBlockers == 0;
        var manifest = new
        {
            schema_version = 2,
            tool = "reference-average-history-correction-graph-preview",
            cutoff_utc = options.CutoffUtc,
            input = new
            {
                signal_preview_directory = input.Directory,
                signal_preview_manifest_sha256 = input.ManifestSha256,
                remove_rows = input.Removes.Count,
                add_rows = input.Adds.Count,
                files = input.Files.OrderBy(item => item.FileName, StringComparer.Ordinal)
            },
            child_catalog = new
            {
                path = ChildCatalogParser.CatalogRelativePath,
                sha256 = childCatalogHash,
                strategy_count = childStrategies.Count,
                asset_counts = childStrategies.GroupBy(item => item.Asset)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count())
            },
            database_snapshot = snapshot.Database,
            sql_rehearsals = snapshot.SqlRehearsals.OrderBy(item => item.QueryName, StringComparer.Ordinal),
            safety = new
            {
                host_parameter = options.Host,
                required_host = CorrectionContract.RequiredHost,
                transaction_isolation = "repeatable read",
                transaction_read_only = true,
                transaction_time_zone = "UTC",
                transaction_rolled_back = rolledBack,
                database_write_statements_issued = 0,
                tool_can_mutate_database = false,
                database_parameter_batch_size = GraphDatabaseReader.BatchSize,
                live_shadow_blockers = liveBlockers,
                semantic_dependency_blockers = dependencyBlockers,
                shared_position_key_blockers = positionBlockers,
                required_reconciliation_plan_blockers = reconciliationBlockers,
                infeasible_adds = infeasibleAdds,
                invariant_errors = invariantCount,
                safe_to_prepare_separate_mutation = safe
            },
            counts = new
            {
                main_removals = snapshot.MainRemovals.Count,
                exact_child_removals = snapshot.ChildRemovals.Count,
                graph_orders = snapshot.GraphOrders.Count,
                graph_fills = snapshot.GraphFills.Count,
                live_shadow_overlaps = snapshot.LiveShadowOverlaps.Count,
                dependencies = snapshot.Dependencies.Count,
                sql_rehearsals = snapshot.SqlRehearsals.Count,
                position_keys = snapshot.PositionKeys.Count,
                exclusive_position_keys = snapshot.PositionKeys.Count(item => item.Exclusive),
                positions = snapshot.Positions.Count,
                position_settlements = snapshot.PositionSettlements.Count,
                position_settlement_winning_asset_conflicts = snapshot.PositionSettlements.Count(item =>
                    !string.IsNullOrWhiteSpace(item.WinningAssetId) &&
                    (string.Equals(item.WinningAssetId, item.AssetId, StringComparison.Ordinal) != item.Won)),
                live_gamma_resolution_markets = snapshot.LiveGammaResolutions.Count,
                market_resolved_event_diagnostic_rows = snapshot.MarketResolvedEventDiagnostics.Count,
                validated_market_websocket_ledger_raw_adds = snapshot.Adds.Count(item =>
                    item.CanAdd && item.ResolutionLedgerRawValidated),
                sticky_ledger_winning_asset_conflicts = snapshot.Adds.Count(item =>
                    item.CanAdd && !item.ResolutionLedgerWinningAssetAgreesWithGamma),
                reconciliation_targets = snapshot.ReconciliationTargets.Count,
                operation_footprint_rows = snapshot.OperationFootprint.Count,
                adds = snapshot.Adds.Count,
                feasible_adds = snapshot.Adds.Count(item => item.CanAdd)
            },
            contract = new
            {
                child_link = "pricing_mode=child_parent_mirror + exact FAK source/monetary/timestamp/fill/settlement parity + matching parent run/order/signal IDs",
                removal_dispatch = "exact FAK only: main btc_updown5m_fak_taker_paper; child btc_updown5m_child_mirror_fak_paper; Filled BUY; one exact fill",
                live_overlap = "any Live/shadow/discrepancy overlap blocks mutation",
                position_exclusivity = "all Paper orders for wallet+asset must belong to the exact removal graph",
                position_settlement = "when position rows exist, exact zeroed position and settlement arithmetic must match graph fills/runs; exact binary winning_outcome is authoritative, winning_asset_id is emitted but non-authoritative",
                corrected_remove_skip_reason = CorrectionContract.CorrectedSkipReason,
                removal_base_stake = "exact root paper_lost_base_stake_usd with lost-counter/effective/target sizing proof from the historical order raw_decision_json projection",
                mutation_scope_hash = new
                {
                    schema_version = CanonicalEvidence.SchemaVersion,
                    algorithm = CanonicalEvidence.Algorithm,
                    serialization = CanonicalEvidence.Serialization,
                    full_physical_row = "UPPER hex SHA-256 of UTF-8 PostgreSQL to_jsonb(row)::text for every run/order/signal/fill/position/settlement preimage row",
                    graph_order = "complete physical run/order/signal row hashes plus independently emitted deterministic semantic projection and raw-decision proof SHA-256",
                    graph_fill_set = "all fills for the exact paper order sorted by order/time/id including per-row full physical hash",
                    add_source = "complete physical skipped source-run hash plus explicit modeled mutation payload"
                },
                deterministic_add_ids = new
                {
                    algorithm = "UUIDv5/RFC4122-SHA1",
                    namespace_id = ModeledAddPayloadBuilder.IdNamespace,
                    name_format = ModeledAddPayloadBuilder.IdNameFormat,
                    entity_kinds = new[] { "signal", "paper_order", "paper_fill", "paper_position", "paper_position_settlement" }
                },
                add_fill_prices = new { regular = 0.52m, low_or_lower_enter = 0.50m },
                add_sizing = "historical skip target_notional_usd multiplier; min_size*0.99*1.10; ceil USD; ceil worst-price shares to 2 decimals; full modeled fill",
                add_timestamps = new
                {
                    entry = "exact source skipped run updated_at_utc",
                    settlement = "modeled only: max(market_end_utc,resolution_ledger_first_received_at_utc)",
                    settlement_source = "modeled_earliest_observable_resolution"
                },
                add_payload = "complete canonical correction-specific final signal/order/fill/run-update/position/settlement payload; no historical order-book snapshot is asserted",
                reconciliation = new
                {
                    schema_version = ReconciliationContract.SchemaVersion,
                    algorithm = ReconciliationContract.Algorithm,
                    serialization = ReconciliationContract.Serialization,
                    contract_sha256 = ReconciliationContract.ContractSha256,
                    target_count = ReconciliationContract.Targets.Count,
                    blocking_target_count = ReconciliationContract.Targets.Count(item => item.BlocksMutation),
                    apply_handshake = ReconciliationContract.ApplyHandshake
                },
                resolution = new
                {
                    authoritative_gamma = "fresh official GET https://gamma-api.polymarket.com/markets/{id}; exact closed binary 1/0 outcome",
                    resolved_ledger = "persisted resolved-market ledger; scalar event_timestamp_utc and winning_asset_id are sticky historical fields and are emitted as non-authoritative evidence",
                    market_websocket_ledger_raw = "latest persisted raw_json must independently match Gamma identity, winner and token bijection; raw timestamp is bounded by market_end/last_received/updated and is not compared to sticky scalar event_timestamp_utc",
                    binance_timed_close_ledger = "must agree with exact archived Binance tick replay; both belong to one provenance group",
                    archived_binance_ticks = "min_samples=2, max_end_age_ms=15000; a MarketWebSocket outcome disagreement is diagnostic and does not override authoritative Gamma plus validated WebSocket raw evidence",
                    raw_websocket_diagnostics = "validated when matching rows exist; zero matching rows is explicitly reported as unavailable and never counted as independent evidence",
                    raw_gamma_response = "kept in memory only; output records URL, fetched_at, byte count and SHA-256"
                },
                operation_footprint = new
                {
                    exact_snapshot_row_count = footprintSnapshotRows,
                    exact_snapshot_pg_column_size_bytes = footprintSnapshotBytes,
                    minimum_direct_row_operations = directRowOperationFloor,
                    affected_strategy_projection_event_rows = projectionEventFootprint.SnapshotRowCount,
                    affected_strategy_projection_event_pg_column_size_bytes =
                        projectionEventFootprint.SnapshotPgColumnSizeBytes,
                    affected_strategy_reconciliation_queue_rows = projectionQueueFootprint.SnapshotRowCount,
                    affected_strategy_reconciliation_queue_pg_column_size_bytes =
                        projectionQueueFootprint.SnapshotPgColumnSizeBytes,
                    byte_measurement = "sum(pg_column_size(target_row)) inside the pinned read-only snapshot",
                    wal_planning = "row-operation floor only; exact WAL bytes are unknown because indexes, TOAST, " +
                                   "tuple/WAL overhead, trigger-generated projection events, full-page images and vacuum are excluded"
                }
            },
            files = output.Evidence.OrderBy(item => item.FileName, StringComparer.Ordinal),
            limitations = new[]
            {
                "Read-only preview only. It contains no INSERT, UPDATE, DELETE, DDL, COPY, or mutation command.",
                "Modeled add fills use the user-supplied 0.52/0.50 premise; they are not historical order-book observations.",
                "Modeled settlement timestamps are deterministic earliest-observable-resolution proxies, not exact counterfactual runtime timestamps.",
                "Operation footprint bytes are exact pg_column_size(row) snapshot sums, not an estimate or lower bound for WAL bytes.",
                "History correction intentionally leaves strategies.paper_lost_counter unchanged; future staking state is not replayed.",
                "daily_reports must be exactly empty again at the final pre-apply gate.",
                "A separate reviewed backup/apply/rollback tool is required before any physical history correction."
            }
        };
        var manifestPath = Path.Combine(options.OutputDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            }) + Environment.NewLine,
            cancellationToken);

        Console.WriteLine("Graph preview manifest: " + manifestPath);
        Console.WriteLine($"Main={snapshot.MainRemovals.Count:N0}; Child={snapshot.ChildRemovals.Count:N0}; Adds={snapshot.Adds.Count:N0}");
        if (!safe)
        {
            Console.Error.WriteLine(
                $"FAIL CLOSED: invariants={invariantCount:N0}, live/shadow={liveBlockers:N0}, " +
                $"dependencies={dependencyBlockers:N0}, position keys={positionBlockers:N0}, " +
                $"infeasible adds={infeasibleAdds:N0}, reconciliation plan={reconciliationBlockers:N0}.");
            return 3;
        }
        return 0;
    }

    private static async Task WriteOutputsAsync(
        DeterministicCsvOutput output,
        GraphSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await output.WriteAsync("main-removals.csv",
            OutputRows.MainRemovalHeader,
            snapshot.MainRemovals.OrderBy(item => item.RunId).Select(OutputRows.MainRemoval),
            cancellationToken);

        await output.WriteAsync("child-removals.csv",
            ["parent_run_id", "parent_paper_order_id", "parent_signal_id", "child_run_id", "child_strategy_id", "child_strategy_code", "market_id", "child_paper_order_id", "child_signal_id", "outcome", "fill_count", "fill_size_shares", "fill_notional_usd", "run_realized_pnl_usd", "settled_at_utc", "graph_state_sha256", "fill_set_sha256"],
            snapshot.ChildRemovals.OrderBy(item => item.ParentRunId).ThenBy(item => item.ChildRunId)
                .Select(item => (IReadOnlyList<string>)
                [item.ParentRunId.ToString("D"), item.ParentOrderId.ToString("D"), item.ParentSignalId.ToString("D"),
                    item.ChildRunId.ToString("D"), item.ChildStrategyId.ToString("D"), item.ChildStrategyCode,
                    item.MarketId, item.ChildOrderId.ToString("D"), item.ChildSignalId.ToString("D"), item.Outcome,
                    item.FillCount.ToString(CultureInfo.InvariantCulture), Format.Decimal(item.FillSizeShares),
                    Format.Decimal(item.FillNotionalUsd), Format.Decimal(item.RunRealizedPnlUsd),
                    Format.Timestamp(item.SettledAtUtc), item.GraphStateSha256, item.FillSetSha256]), cancellationToken);

        await output.WriteAsync("graph-orders.csv",
            OutputRows.GraphOrderHeader,
            snapshot.GraphOrders.OrderBy(item => item.Scope, StringComparer.Ordinal).ThenBy(item => item.RunId)
                .Select(OutputRows.GraphOrder),
            cancellationToken);

        await output.WriteAsync("graph-fills.csv",
            ["scope", "parent_main_run_id", "run_id", "paper_order_id", "fill_id", "price", "size_shares", "filled_at_utc", "realized_pnl_usd", "evidence", "full_row_sha256"],
            snapshot.GraphFills.OrderBy(item => item.OrderId).ThenBy(item => item.FilledAtUtc).ThenBy(item => item.FillId)
                .Select(item => (IReadOnlyList<string>)
                [item.Scope, Format.Guid(item.ParentMainRunId), item.RunId.ToString("D"), item.OrderId.ToString("D"),
                    item.FillId.ToString("D"), Format.Decimal(item.Price), Format.Decimal(item.SizeShares),
                    Format.Timestamp(item.FilledAtUtc), Format.Decimal(item.RealizedPnlUsd), item.Evidence,
                    item.FullRowSha256]),
            cancellationToken);

        await output.WriteAsync("live-shadow-overlaps.csv",
            ["relation", "row_type", "row_id", "strategy_id", "paper_order_id", "signal_id", "live_order_id", "correlation_id", "status", "details", "blocks_mutation"],
            snapshot.LiveShadowOverlaps.Select(item => (IReadOnlyList<string>)
            [item.Relation, item.RowType, item.RowId, Format.Guid(item.StrategyId), Format.Guid(item.PaperOrderId),
                Format.Guid(item.SignalId), Format.Guid(item.LiveOrderId), Format.Guid(item.CorrelationId),
                item.Status, item.Details, Format.Bool(item.BlocksMutation)]), cancellationToken);

        await output.WriteAsync("dependencies.csv",
            ["dependency_class", "relation", "table_name", "row_id", "graph_order_id", "graph_signal_id", "correlation_id", "details", "blocks_mutation"],
            snapshot.Dependencies.Select(item => (IReadOnlyList<string>)
            [item.DependencyClass, item.Relation, item.TableName, item.RowId, Format.Guid(item.GraphOrderId),
                Format.Guid(item.GraphSignalId), Format.Guid(item.CorrelationId), item.Details,
                Format.Bool(item.BlocksMutation)]), cancellationToken);

        await output.WriteAsync("foreign-keys.csv",
            ["constraint_name", "source_table", "source_columns", "target_table", "target_columns", "delete_action", "update_action", "expected"],
            snapshot.ForeignKeys.OrderBy(item => item.SourceTable, StringComparer.Ordinal).ThenBy(item => item.ConstraintName)
                .Select(item => (IReadOnlyList<string>)
                [item.ConstraintName, item.SourceTable, item.SourceColumns, item.TargetTable, item.TargetColumns,
                    item.DeleteAction, item.UpdateAction, Format.Bool(item.Expected)]), cancellationToken);

        await output.WriteAsync("schema-reference-columns.csv",
            ["table_name", "column_name", "data_type", "expected"],
            snapshot.SchemaReferenceColumns.Select(item => (IReadOnlyList<string>)
            [item.TableName, item.ColumnName, item.DataType, Format.Bool(item.Expected)]), cancellationToken);

        await output.WriteAsync("reconciliation-targets.csv",
            OutputRows.ReconciliationTargetHeader,
            snapshot.ReconciliationTargets.OrderBy(item => item.TargetId, StringComparer.Ordinal)
                .Select(OutputRows.ReconciliationTargetRow),
            cancellationToken);

        await output.WriteAsync("operation-footprint.csv",
            OutputRows.OperationFootprintHeader,
            snapshot.OperationFootprint.Select(OutputRows.OperationFootprintCsvRow),
            cancellationToken);

        await output.WriteAsync("position-keys.csv",
            ["copied_trader_wallet", "asset_id", "graph_order_count", "database_order_count", "outside_graph_order_count", "position_count", "settlement_count", "exclusive", "blocks_mutation", "details"],
            snapshot.PositionKeys.Select(item => (IReadOnlyList<string>)
            [item.CopiedTraderWallet, item.AssetId, item.GraphOrderCount.ToString(CultureInfo.InvariantCulture),
                item.DatabaseOrderCount.ToString(CultureInfo.InvariantCulture), item.OutsideGraphOrderCount.ToString(CultureInfo.InvariantCulture),
                item.PositionCount.ToString(CultureInfo.InvariantCulture), item.SettlementCount.ToString(CultureInfo.InvariantCulture),
                Format.Bool(item.Exclusive), Format.Bool(item.BlocksMutation), item.Details]), cancellationToken);

        await output.WriteAsync("positions.csv",
            ["id", "copied_trader_wallet", "asset_id", "condition_id", "outcome", "size_shares", "average_price", "estimated_value_usd", "unrealized_pnl_usd", "updated_at_utc", "full_row_sha256"],
            snapshot.Positions.Select(item => (IReadOnlyList<string>)
            [item.Id.ToString("D"), item.CopiedTraderWallet, item.AssetId, item.ConditionId, item.Outcome,
                Format.Decimal(item.SizeShares), Format.Decimal(item.AveragePrice), Format.Decimal(item.EstimatedValueUsd),
                Format.Decimal(item.UnrealizedPnlUsd), Format.Timestamp(item.UpdatedAtUtc), item.FullRowSha256]), cancellationToken);

        await output.WriteAsync("position-settlements.csv",
            ["id", "copied_trader_wallet", "asset_id", "condition_id", "outcome", "winning_asset_id", "winning_outcome", "settled_size_shares", "average_price", "cost_basis_usd", "settlement_value_usd", "realized_pnl_usd", "won", "settlement_source", "settled_at_utc", "category", "created_at_utc", "full_row_sha256"],
            snapshot.PositionSettlements.Select(item => (IReadOnlyList<string>)
            [item.Id.ToString("D"), item.CopiedTraderWallet, item.AssetId, item.ConditionId, item.Outcome,
                item.WinningAssetId ?? "", item.WinningOutcome, Format.Decimal(item.SettledSizeShares),
                Format.Decimal(item.AveragePrice), Format.Decimal(item.CostBasisUsd),
                Format.Decimal(item.SettlementValueUsd), Format.Decimal(item.RealizedPnlUsd), Format.Bool(item.Won),
                item.SettlementSource, Format.Timestamp(item.SettledAtUtc), item.Category ?? "",
                Format.Timestamp(item.CreatedAtUtc), item.FullRowSha256]), cancellationToken);

        await output.WriteAsync("live-gamma-resolutions.csv",
            ["market_id", "condition_id", "market_slug", "closed", "outcomes_json", "token_ids_json", "outcome_prices_json", "winning_outcome", "winning_token_id", "order_min_size", "resolution_source", "request_url", "raw_sha256", "raw_bytes", "fetched_at_utc"],
            snapshot.LiveGammaResolutions.OrderBy(item => item.MarketId, StringComparer.Ordinal)
                .Select(item => (IReadOnlyList<string>)
                [item.MarketId, item.ConditionId, item.MarketSlug, Format.Bool(item.Closed),
                    item.OutcomesJson, item.TokenIdsJson, item.OutcomePricesJson, item.WinningOutcome,
                    item.WinningTokenId, Format.NullableDecimal(item.OrderMinSize), item.ResolutionSource ?? "",
                    item.RequestUrl, item.RawSha256, item.RawBytes.ToString(CultureInfo.InvariantCulture),
                    Format.Timestamp(item.FetchedAtUtc)]), cancellationToken);

        await output.WriteAsync("market-resolved-event-evidence.csv",
            ["id", "component", "raw_event_type", "asset_id", "condition_id", "winning_asset_id", "winning_outcome", "event_timestamp_utc", "received_at_utc", "active_snapshot_found", "snapshot_market_id", "snapshot_condition_id", "snapshot_market_slug", "snapshot_asset_symbol", "snapshot_market_start_utc", "snapshot_is_crypto_up_down_5m", "recorder_action", "db_jsonb_text_sha256", "db_jsonb_text_bytes", "created_at_utc"],
            snapshot.MarketResolvedEventDiagnostics.OrderBy(item => item.ReceivedAtUtc).ThenBy(item => item.Id)
                .Select(item => (IReadOnlyList<string>)
                [item.Id.ToString("D"), item.Component, item.RawEventType, item.AssetId, item.ConditionId,
                    item.WinningAssetId, item.WinningOutcome, Format.Timestamp(item.EventTimestampUtc),
                    Format.Timestamp(item.ReceivedAtUtc), Format.Bool(item.ActiveSnapshotFound),
                    item.SnapshotMarketId, item.SnapshotConditionId, item.SnapshotMarketSlug,
                    item.SnapshotAssetSymbol, Format.Timestamp(item.SnapshotMarketStartUtc),
                    Format.Bool(item.SnapshotIsCryptoUpDown5m), item.RecorderAction, item.RawSha256,
                    item.RawBytes.ToString(CultureInfo.InvariantCulture), Format.Timestamp(item.CreatedAtUtc)]),
            cancellationToken);

        await output.WriteAsync("add-feasibility.csv",
            OutputRows.AddHeader,
            snapshot.Adds.OrderBy(item => item.RunId).Select(OutputRows.Add), cancellationToken);

        await output.WriteAsync("invariant-errors.csv",
            ["scope", "entity_id", "code", "details"],
            snapshot.InvariantErrors.Select(item => (IReadOnlyList<string>)
            [item.Scope, item.EntityId, item.Code, item.Details]), cancellationToken);
    }

    private static string FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            var catalog = Path.Combine(current.FullName,
                ChildCatalogParser.CatalogRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(catalog)) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private static void PrepareOutputDirectory(string outputDirectory, string inputDirectory)
    {
        var resolved = SignalPreviewInputReader.RequireBelowCodexTemp(outputDirectory, "--output-dir");
        if (string.Equals(resolved, inputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("--output-dir must differ from --signal-preview-dir.");
        }
        if (Directory.Exists(resolved) && Directory.EnumerateFileSystemEntries(resolved).Any())
        {
            throw new IOException("Output directory must be empty: " + resolved);
        }
        Directory.CreateDirectory(resolved);
    }

    private sealed record Options(
        string Host,
        DateTimeOffset CutoffUtc,
        string SignalPreviewManifestSha256,
        string SignalPreviewDirectory,
        string OutputDirectory,
        int CommandTimeoutSeconds,
        bool ShowHelp)
    {
        public const string HelpText = """
            Read-only Reference Average physical-correction graph preview.

            Required:
              --host 192.168.0.101
              --cutoff 2026-07-27T13:24:05.932282Z
              --signal-preview-manifest-sha256 19BE8C1EA87BBA18FEEAEC4791EA075C3649EC0276225BDE9E85097A8BB8EACD
              --signal-preview-dir <completed signal preview below D:\CodexTemp>
              --output-dir <empty path below D:\CodexTemp>

            Optional:
              --command-timeout-seconds <1..900>   Default: 180

            Credentials come only from POLYCOPYTRADER_POSTGRES_CONNECTION. The Host is
            overridden with 192.168.0.101. The transaction is REPEATABLE READ, READ ONLY,
            UTC and explicitly rolled back. This tool cannot mutate PostgreSQL.
            """;

        public static Options Parse(string[] args)
        {
            if (args.Length == 1 && args[0] is "--help" or "-h")
            {
                return new Options("", default, "", "", "", 180, true);
            }
            if (args.Length % 2 != 0)
            {
                throw new ArgumentException("Arguments must be supplied as --name value pairs.");
            }
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index += 2)
            {
                if (!args[index].StartsWith("--", StringComparison.Ordinal) ||
                    !values.TryAdd(args[index], args[index + 1]))
                {
                    throw new ArgumentException("Invalid or duplicate argument: " + args[index]);
                }
            }
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "--host", "--cutoff", "--signal-preview-manifest-sha256",
                "--signal-preview-dir", "--output-dir", "--command-timeout-seconds"
            };
            var unknown = values.Keys.FirstOrDefault(key => !allowed.Contains(key));
            if (unknown is not null) throw new ArgumentException("Unknown argument: " + unknown);
            var host = Require(values, "--host");
            if (!string.Equals(host, CorrectionContract.RequiredHost, StringComparison.Ordinal))
                throw new ArgumentException("--host must be exactly " + CorrectionContract.RequiredHost);
            if (!DateTimeOffset.TryParse(Require(values, "--cutoff"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var cutoff) || cutoff.Offset != TimeSpan.Zero ||
                cutoff.ToUniversalTime() != CorrectionContract.RequiredCutoffUtc)
                throw new ArgumentException("--cutoff must be exactly 2026-07-27T13:24:05.932282Z.");
            var inputManifestSha256 = Require(values, "--signal-preview-manifest-sha256");
            if (!string.Equals(
                    inputManifestSha256,
                    CorrectionContract.RequiredInputManifestSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "--signal-preview-manifest-sha256 must be exactly " +
                    CorrectionContract.RequiredInputManifestSha256 + ".");
            }
            var timeout = 180;
            if (values.TryGetValue("--command-timeout-seconds", out var rawTimeout) &&
                (!int.TryParse(rawTimeout, out timeout) || timeout is < 1 or > 900))
                throw new ArgumentException("--command-timeout-seconds must be between 1 and 900.");
            return new Options(host, cutoff.ToUniversalTime(), inputManifestSha256.ToUpperInvariant(),
                Path.GetFullPath(Require(values, "--signal-preview-dir")),
                Path.GetFullPath(Require(values, "--output-dir")), timeout, false);
        }

        private static string Require(IReadOnlyDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException("Missing required argument " + key);
    }
}
