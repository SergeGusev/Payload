using System.Data;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace ReferenceAverageHistoryCorrectionGraphPreview;

internal sealed record GraphSnapshot(
    DatabaseSnapshotMetadata Database,
    IReadOnlyList<GraphOrder> GraphOrders,
    IReadOnlyList<GraphFill> GraphFills,
    IReadOnlyList<MainRemovalSummary> MainRemovals,
    IReadOnlyList<ChildRemovalSummary> ChildRemovals,
    IReadOnlyList<LiveShadowOverlap> LiveShadowOverlaps,
    IReadOnlyList<DependencyRow> Dependencies,
    IReadOnlyList<ForeignKeyEvidence> ForeignKeys,
    IReadOnlyList<SchemaReferenceColumn> SchemaReferenceColumns,
    IReadOnlyList<SqlRehearsalEvidence> SqlRehearsals,
    IReadOnlyList<ReconciliationTarget> ReconciliationTargets,
    IReadOnlyList<OperationFootprintRow> OperationFootprint,
    IReadOnlyList<PositionKey> PositionKeys,
    IReadOnlyList<PositionRow> Positions,
    IReadOnlyList<PositionSettlementRow> PositionSettlements,
    IReadOnlyList<LiveGammaResolutionEvidence> LiveGammaResolutions,
    IReadOnlyList<MarketResolvedEventEvidence> MarketResolvedEventDiagnostics,
    IReadOnlyList<AddFeasibility> Adds,
    IReadOnlyList<InvariantError> InvariantErrors,
    bool TransactionRolledBack);

internal sealed class GraphDatabaseReader
{
    internal const int BatchSize = 25_000;
    internal const string AddCollisionSql = """
        WITH targets(run_id, strategy_id, strategy_code, condition_id, token_id, wallet) AS (
            SELECT * FROM unnest(
                @run_ids::uuid[], @strategy_ids::uuid[], @strategy_codes::text[],
                @condition_ids::text[], @token_ids::text[], @wallets::text[])
        )
        SELECT target.run_id, 'paper_orders', paper_order.id::text
        FROM targets target
        JOIN paper_orders paper_order
          ON paper_order.strategy_id = target.strategy_id
         AND paper_order.condition_id = target.condition_id
        UNION ALL
        SELECT target.run_id, 'paper_orders_wallet_asset', paper_order.id::text
        FROM targets target
        JOIN paper_orders paper_order
          ON paper_order.copied_trader_wallet = target.wallet
         AND paper_order.asset_id = target.token_id
        UNION ALL
        SELECT target.run_id, 'signals', signal.id::text
        FROM targets target
        JOIN signals signal
          ON signal.condition_id = target.condition_id
         AND signal.decision = target.strategy_code || '_entry'
        UNION ALL
        SELECT target.run_id, 'live_orders', live_order.id::text
        FROM targets target
        JOIN live_orders live_order
          ON live_order.strategy_id = target.strategy_id
         AND live_order.condition_id = target.condition_id
        UNION ALL
        SELECT target.run_id, 'paper_live_shadow_decisions', shadow.correlation_id::text
        FROM targets target
        JOIN paper_live_shadow_decisions shadow
          ON shadow.strategy_id = target.strategy_id
         AND shadow.condition_id = target.condition_id
        UNION ALL
        SELECT target.run_id, 'paper_positions', position_row.id::text
        FROM targets target
        JOIN paper_positions position_row
          ON position_row.copied_trader_wallet = target.wallet
         AND position_row.asset_id = target.token_id
        UNION ALL
        SELECT target.run_id, 'paper_position_settlements', settlement_row.id::text
        FROM targets target
        JOIN paper_position_settlements settlement_row
          ON settlement_row.copied_trader_wallet = target.wallet
         AND settlement_row.asset_id = target.token_id
        ORDER BY 1, 2, 3;
        """;
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    private readonly DateTimeOffset cutoffUtc;
    private readonly int commandTimeoutSeconds;
    private readonly IReadOnlyDictionary<string, LiveGammaResolutionEvidence> liveGammaByMarket;
    private readonly List<InvariantError> invariantErrors = [];
    private readonly Dictionary<Guid, RemovalStakeEvidence> removalStakeEvidenceByRun = [];
    private string lastQueryStage = "not_started";

    private GraphDatabaseReader(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset cutoffUtc,
        IReadOnlyDictionary<string, LiveGammaResolutionEvidence> liveGammaByMarket,
        int commandTimeoutSeconds)
    {
        this.connection = connection;
        this.transaction = transaction;
        this.cutoffUtc = cutoffUtc;
        this.liveGammaByMarket = liveGammaByMarket;
        this.commandTimeoutSeconds = commandTimeoutSeconds;
    }

    internal static IEnumerable<Guid[][]> BatchGuidParameterSets(params Guid[][] arrays)
    {
        var length = arrays.Length == 0 ? 0 : arrays.Max(item => item.Length);
        for (var offset = 0; offset < length; offset += BatchSize)
        {
            yield return arrays
                .Select(item => item.Skip(offset).Take(BatchSize).ToArray())
                .ToArray();
        }
    }

    public static async Task<GraphSnapshot> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        NpgsqlConnectionStringBuilder connectionBuilder,
        SignalPreviewInput input,
        IReadOnlyList<ChildStrategy> childStrategies,
        IReadOnlyDictionary<string, LiveGammaResolutionEvidence> liveGammaByMarket,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var reader = new GraphDatabaseReader(
            connection,
            transaction,
            input.CutoffUtc,
            liveGammaByMarket,
            commandTimeoutSeconds);
        try
        {
            await reader.ConfigureReadOnlyAsync(cancellationToken);
            var metadata = await reader.ReadAndValidateMetadataAsync(connectionBuilder, cancellationToken);
            var sqlRehearsals = await reader.RehearseComposedGraphSqlAsync(cancellationToken);
            var schemaColumns = await reader.ReadSchemaReferenceColumnsAsync(cancellationToken);
            var foreignKeys = await reader.ReadForeignKeysAsync(cancellationToken);

            var mainOrders = await reader.ReadMainOrdersAsync(input.Removes, cancellationToken);
            var mainByRun = mainOrders.ToDictionary(item => item.RunId);
            var mainByOrder = mainOrders.ToDictionary(item => item.OrderId);
            var mainBySignal = mainOrders.ToDictionary(item => item.SignalId);
            var childOrders = await reader.ReadChildOrdersAsync(
                childStrategies,
                mainByRun,
                mainByOrder,
                mainBySignal,
                cancellationToken);
            var graphOrders = mainOrders.Concat(childOrders)
                .OrderBy(item => item.Scope, StringComparer.Ordinal)
                .ThenBy(item => item.RunId)
                .ToArray();
            reader.ValidateGraphUniqueness(graphOrders);

            var graphFills = await reader.ReadGraphFillsAsync(graphOrders, cancellationToken);
            reader.ValidateGraphArithmetic(graphOrders, graphFills);
            var mainSummaries = reader.BuildMainSummaries(mainOrders, graphFills, input.Removes);
            var childSummaries = reader.BuildChildSummaries(childOrders, graphFills, mainByRun);
            var liveShadow = await reader.ReadLiveShadowOverlapsAsync(graphOrders, cancellationToken);
            var (positionKeys, positions, settlements) =
                await reader.ReadPositionsAsync(graphOrders, graphFills, cancellationToken);
            var dependencies = await reader.ReadDependenciesAsync(
                graphOrders,
                graphFills,
                positions,
                settlements,
                liveShadow,
                cancellationToken);
            var (adds, marketResolvedDiagnostics) = await reader.ReadAddsAsync(input.Adds, cancellationToken);
            var operationFootprint = await reader.ReadOperationFootprintAsync(
                graphOrders,
                graphFills,
                positions,
                settlements,
                adds,
                cancellationToken);

            return new GraphSnapshot(
                metadata,
                graphOrders,
                graphFills,
                mainSummaries,
                childSummaries,
                liveShadow,
                dependencies,
                foreignKeys,
                schemaColumns,
                sqlRehearsals,
                ReconciliationContract.Targets,
                operationFootprint,
                positionKeys,
                positions,
                settlements,
                liveGammaByMarket.Values.OrderBy(item => item.MarketId, StringComparer.Ordinal).ToArray(),
                marketResolvedDiagnostics,
                adds,
                reader.invariantErrors.OrderBy(item => item.Scope, StringComparer.Ordinal)
                    .ThenBy(item => item.EntityId, StringComparer.Ordinal)
                    .ThenBy(item => item.Code, StringComparer.Ordinal)
                    .ToArray(),
                TransactionRolledBack: false);
        }
        catch (PostgresException exception)
        {
            throw new InvalidOperationException(
                $"PostgreSQL query stage '{reader.lastQueryStage}' failed: " +
                $"sqlstate={exception.SqlState};position={exception.Position};message={exception.MessageText}",
                exception);
        }
    }

    private async Task ConfigureReadOnlyAsync(CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync("SET TRANSACTION READ ONLY;", cancellationToken);
        await using var command = Command(
            "SELECT set_config('TimeZone', 'UTC', true), " +
            "set_config('search_path', 'pg_catalog, public', true), " +
            "set_config('statement_timeout', @timeout, true);");
        command.Parameters.AddWithValue("timeout", $"{commandTimeoutSeconds}s");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<DatabaseSnapshotMetadata> ReadAndValidateMetadataAsync(
        NpgsqlConnectionStringBuilder builder,
        CancellationToken cancellationToken)
    {
        await using var command = Command("""
            SELECT current_setting('transaction_isolation'),
                   current_setting('transaction_read_only'),
                   current_setting('TimeZone'),
                   host(inet_server_addr()),
                   inet_server_port(),
                   current_database(),
                   current_setting('search_path'),
                   (SELECT count(*) FROM daily_reports);
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Database metadata query returned no row.");
        }

        var isolation = reader.GetString(0);
        var readOnly = string.Equals(reader.GetString(1), "on", StringComparison.OrdinalIgnoreCase);
        var timeZone = reader.GetString(2);
        var serverAddress = reader.GetString(3);
        var serverPort = reader.GetInt32(4);
        var currentDatabase = reader.GetString(5);
        var searchPath = reader.GetString(6);
        var dailyReportsRowCount = reader.GetInt64(7);
        if (!string.Equals(isolation, "repeatable read", StringComparison.OrdinalIgnoreCase) ||
            !readOnly ||
            !string.Equals(timeZone, "UTC", StringComparison.OrdinalIgnoreCase) ||
            serverPort != CorrectionContract.RequiredPort ||
            !string.Equals(currentDatabase, CorrectionContract.RequiredDatabase, StringComparison.Ordinal) ||
            !string.Equals(searchPath, "pg_catalog, public", StringComparison.Ordinal) ||
            builder.Port != CorrectionContract.RequiredPort ||
            !string.Equals(builder.Database, CorrectionContract.RequiredDatabase, StringComparison.Ordinal) ||
            !string.Equals(builder.SearchPath, "pg_catalog,public", StringComparison.Ordinal) ||
            !IPAddress.TryParse(serverAddress, out var actual) ||
            !IPAddress.TryParse(CorrectionContract.RequiredHost, out var required) ||
            !actual.MapToIPv4().Equals(required.MapToIPv4()))
        {
            throw new InvalidOperationException(
                $"Unsafe database snapshot: isolation={isolation}, read_only={readOnly}, " +
                $"timezone={timeZone}, server={serverAddress}:{serverPort}, " +
                $"database={currentDatabase}, search_path={searchPath}.");
        }
        if (dailyReportsRowCount != 0)
        {
            Error(
                "daily_reports",
                "entire_table",
                "daily_reports_must_be_empty_for_history_correction",
                $"snapshot_row_count={dailyReportsRowCount}");
        }

        return new DatabaseSnapshotMetadata(
            builder.Host ?? string.Empty,
            builder.Port,
            builder.Database ?? string.Empty,
            serverAddress,
            serverPort,
            currentDatabase,
            connection.PostgreSqlVersion.ToString(),
            isolation,
            readOnly,
            timeZone,
            searchPath,
            dailyReportsRowCount);
    }

    private async Task<IReadOnlyList<SqlRehearsalEvidence>> RehearseComposedGraphSqlAsync(
        CancellationToken cancellationToken)
    {
        var mainSql = BuildMainOrdersSql();
        var childSql = BuildChildOrdersSql();
        await using (var command = Command(
            "EXPLAIN (FORMAT JSON, COSTS FALSE)\n" + mainSql,
            "sql_rehearsal_main_orders"))
        {
            command.Parameters.AddWithValue(
                "run_ids",
                NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                Array.Empty<Guid>());
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            if (!await data.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Main graph SQL EXPLAIN returned no plan.");
            }
        }

        await using (var command = Command(
            "EXPLAIN (FORMAT JSON, COSTS FALSE)\n" + childSql,
            "sql_rehearsal_child_orders"))
        {
            command.Parameters.AddWithValue(
                "cutoff_utc",
                NpgsqlDbType.TimestampTz,
                cutoffUtc.UtcDateTime);
            command.Parameters.AddWithValue(
                "parent_keys",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                new[] { "parent_run_id", "parent_paper_order_id", "parent_signal_id" });
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            if (!await data.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Child graph SQL EXPLAIN returned no plan.");
            }
        }

        var version = connection.PostgreSqlVersion.ToString();
        return
        [
            new SqlRehearsalEvidence(
                "main_orders_exact_composed_sql",
                CanonicalEvidence.HashSql(mainSql),
                version,
                ExplainPlanned: true),
            new SqlRehearsalEvidence(
                "child_orders_exact_composed_sql",
                CanonicalEvidence.HashSql(childSql),
                version,
                ExplainPlanned: true)
        ];
    }

    private async Task<IReadOnlyList<SchemaReferenceColumn>> ReadSchemaReferenceColumnsAsync(
        CancellationToken cancellationToken)
    {
        var expected = new HashSet<(string Table, string Column)>
        {
            ("paper_orders", "signal_id"), ("paper_orders", "correlation_id"),
            ("paper_fills", "paper_order_id"),
            ("strategy_market_paper_runs", "signal_id"), ("strategy_market_paper_runs", "paper_order_id"),
            ("signal_rejections", "signal_id"),
            ("paper_copied_leader_positions", "entry_signal_id"),
            ("paper_copied_leader_positions", "entry_paper_order_id"),
            ("dry_run_orders", "signal_id"),
            ("live_orders", "signal_id"), ("live_orders", "paper_order_id"),
            ("live_orders", "correlation_id"),
            ("paper_live_shadow_decisions", "signal_id"),
            ("paper_live_shadow_decisions", "paper_order_id"),
            ("paper_live_shadow_decisions", "live_order_id"),
            ("paper_live_shadow_decisions", "correlation_id"),
            ("paper_live_shadow_discrepancies", "correlation_id"),
            ("polymarket_onchain_paper_signal_results", "signal_id"),
            ("polymarket_onchain_paper_signal_results", "paper_order_id"),
            ("dashboard_projection_events", "source_id"),
            ("dashboard_strategy_recent_projection_facts", "source_id"),
            ("dashboard_strategy_position_projection_facts", "source_id")
        };
        await using var command = Command("""
            SELECT table_name, column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND column_name = ANY(@column_names)
            ORDER BY table_name, column_name;
            """);
        command.Parameters.AddWithValue(
            "column_names",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            new[] { "signal_id", "paper_order_id", "entry_signal_id", "entry_paper_order_id",
                "live_order_id", "correlation_id", "source_id" });
        var result = new List<SchemaReferenceColumn>();
        await using var data = await command.ExecuteReaderAsync(cancellationToken);
        while (await data.ReadAsync(cancellationToken))
        {
            var table = data.GetString(0);
            var column = data.GetString(1);
            var isExpected = expected.Contains((table, column));
            result.Add(new SchemaReferenceColumn(table, column, data.GetString(2), isExpected));
            if (!isExpected)
            {
                Error("schema", table + "." + column, "unknown_reference_column",
                    "A reference-like UUID column is outside the reviewed allowlist.");
            }
        }

        foreach (var missing in expected.Except(result.Select(item => (item.TableName, item.ColumnName))))
        {
            Error("schema", missing.Item1 + "." + missing.Item2, "expected_reference_column_missing", "");
        }

        return result;
    }

    private async Task<IReadOnlyList<ForeignKeyEvidence>> ReadForeignKeysAsync(CancellationToken cancellationToken)
    {
        var expected = new Dictionary<
            (string Source, string SourceColumn, string Target, string TargetColumn),
            (string DeleteAction, string UpdateAction)>
        {
            [("signals", "leader_trade_id", "leader_trades", "id")] = ("NO ACTION", "NO ACTION"),
            [("signal_rejections", "signal_id", "signals", "id")] = ("NO ACTION", "NO ACTION"),
            [("paper_orders", "strategy_id", "strategies", "id")] = ("NO ACTION", "NO ACTION"),
            [("paper_fills", "paper_order_id", "paper_orders", "id")] = ("NO ACTION", "NO ACTION"),
            [("strategy_market_paper_runs", "strategy_id", "strategies", "id")] = ("NO ACTION", "NO ACTION"),
            [("strategy_market_paper_runs", "signal_id", "signals", "id")] = ("NO ACTION", "NO ACTION"),
            [("strategy_market_paper_runs", "paper_order_id", "paper_orders", "id")] = ("NO ACTION", "NO ACTION"),
            [("strategy_child_parent_assignments", "child_strategy_id", "strategies", "id")] = ("NO ACTION", "NO ACTION"),
            [("strategy_child_parent_assignments", "parent_strategy_id", "strategies", "id")] = ("NO ACTION", "NO ACTION"),
            [("date_dependent_strategy_hourly_paper_pnl", "strategy_id", "strategies", "id")] = ("CASCADE", "NO ACTION"),
            [("dry_run_orders", "strategy_id", "strategies", "id")] = ("NO ACTION", "NO ACTION"),
            [("live_orders", "strategy_id", "strategies", "id")] = ("NO ACTION", "NO ACTION"),
            [("live_orders", "paper_order_id", "paper_orders", "id")] = ("NO ACTION", "NO ACTION"),
            [("paper_live_shadow_decisions", "strategy_id", "strategies", "id")] = ("NO ACTION", "NO ACTION"),
            [("paper_live_shadow_decisions", "signal_id", "signals", "id")] = ("NO ACTION", "NO ACTION"),
            [("paper_live_shadow_decisions", "paper_order_id", "paper_orders", "id")] = ("NO ACTION", "NO ACTION"),
            [("paper_live_shadow_decisions", "live_order_id", "live_orders", "id")] = ("NO ACTION", "NO ACTION"),
            [("paper_live_shadow_discrepancies", "strategy_id", "strategies", "id")] = ("NO ACTION", "NO ACTION"),
            [("dashboard_strategy_lifetime_projection_states", "strategy_id", "strategies", "id")] = ("CASCADE", "NO ACTION"),
            [("dashboard_strategy_recent_projection_states", "strategy_id", "strategies", "id")] = ("CASCADE", "NO ACTION"),
            [("dashboard_strategy_recent_projection_facts", "strategy_id", "strategies", "id")] = ("CASCADE", "NO ACTION"),
            [("dashboard_strategy_position_projection_facts", "strategy_id", "strategies", "id")] = ("CASCADE", "NO ACTION")
        };
        var graphTables = new[]
        {
            "strategies", "leader_trades", "signals", "signal_rejections", "paper_orders", "paper_fills",
            "strategy_market_paper_runs", "strategy_child_parent_assignments", "dry_run_orders",
            "live_orders", "paper_live_shadow_decisions", "paper_live_shadow_discrepancies",
            "paper_positions", "paper_position_settlements", "paper_copied_leader_positions",
            "polymarket_onchain_paper_signal_results", "dashboard_projection_events",
            "dashboard_projection_reconciliation_queue", "dashboard_strategy_lifetime_projection_states",
            "dashboard_strategy_recent_projection_states", "dashboard_strategy_recent_projection_facts",
            "dashboard_strategy_position_projection_facts", "dashboard_strategy_performance_snapshots",
            "dashboard_strategy_recent_performance_snapshots", "date_dependent_strategy_hourly_paper_pnl",
            "paper_copied_trader_performance"
        };
        await using var command = Command("""
            SELECT constraint_row.conname,
                   source_table.relname,
                   string_agg(source_column.attname, ',' ORDER BY source_key.ordinality),
                   target_table.relname,
                   string_agg(target_column.attname, ',' ORDER BY source_key.ordinality),
                   CASE constraint_row.confdeltype
                     WHEN 'a' THEN 'NO ACTION' WHEN 'r' THEN 'RESTRICT' WHEN 'c' THEN 'CASCADE'
                     WHEN 'n' THEN 'SET NULL' WHEN 'd' THEN 'SET DEFAULT' ELSE constraint_row.confdeltype::text END,
                   CASE constraint_row.confupdtype
                     WHEN 'a' THEN 'NO ACTION' WHEN 'r' THEN 'RESTRICT' WHEN 'c' THEN 'CASCADE'
                     WHEN 'n' THEN 'SET NULL' WHEN 'd' THEN 'SET DEFAULT' ELSE constraint_row.confupdtype::text END
            FROM pg_constraint constraint_row
            JOIN pg_class source_table ON source_table.oid = constraint_row.conrelid
            JOIN pg_namespace source_schema ON source_schema.oid = source_table.relnamespace
            JOIN pg_class target_table ON target_table.oid = constraint_row.confrelid
            JOIN pg_namespace target_schema ON target_schema.oid = target_table.relnamespace
            JOIN unnest(constraint_row.conkey) WITH ORDINALITY source_key(attnum, ordinality) ON true
            JOIN unnest(constraint_row.confkey) WITH ORDINALITY target_key(attnum, ordinality)
              ON target_key.ordinality = source_key.ordinality
            JOIN pg_attribute source_column ON source_column.attrelid = source_table.oid
                                           AND source_column.attnum = source_key.attnum
            JOIN pg_attribute target_column ON target_column.attrelid = target_table.oid
                                           AND target_column.attnum = target_key.attnum
            WHERE constraint_row.contype = 'f'
              AND source_schema.nspname = 'public'
              AND target_schema.nspname = 'public'
              AND (source_table.relname = ANY(@tables) OR target_table.relname = ANY(@tables))
            GROUP BY constraint_row.conname, source_table.relname, target_table.relname,
                     constraint_row.confdeltype, constraint_row.confupdtype
            ORDER BY source_table.relname, constraint_row.conname;
            """);
        command.Parameters.AddWithValue(
            "tables",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            graphTables);
        var result = new List<ForeignKeyEvidence>();
        await using var data = await command.ExecuteReaderAsync(cancellationToken);
        while (await data.ReadAsync(cancellationToken))
        {
            var source = data.GetString(1);
            var sourceColumns = data.GetString(2);
            var target = data.GetString(3);
            var targetColumns = data.GetString(4);
            var key = (source, sourceColumns, target, targetColumns);
            var isExpected = expected.TryGetValue(key, out var expectedActions);
            var deleteAction = data.GetString(5);
            var updateAction = data.GetString(6);
            result.Add(new ForeignKeyEvidence(
                data.GetString(0), source, sourceColumns, target, targetColumns,
                deleteAction, updateAction, isExpected));
            if (!isExpected)
            {
                Error("schema", data.GetString(0), "unknown_graph_foreign_key",
                    $"{source}({sourceColumns})->{target}({targetColumns})");
            }
            if (isExpected &&
                (!string.Equals(deleteAction, expectedActions.DeleteAction, StringComparison.Ordinal) ||
                 !string.Equals(updateAction, expectedActions.UpdateAction, StringComparison.Ordinal)))
            {
                Error("schema", data.GetString(0), "graph_foreign_key_action_changed",
                    $"expected_delete={expectedActions.DeleteAction};actual_delete={deleteAction};" +
                    $"expected_update={expectedActions.UpdateAction};actual_update={updateAction}");
            }
        }

        foreach (var missing in expected.Keys.Where(item => !result.Any(row =>
                     row.SourceTable == item.Source && row.SourceColumns == item.SourceColumn &&
                     row.TargetTable == item.Target && row.TargetColumns == item.TargetColumn)))
        {
            Error("schema", missing.Source + "." + missing.SourceColumn,
                "expected_foreign_key_missing", missing.Target + "." + missing.TargetColumn);
        }

        return result;
    }

    private async Task<IReadOnlyList<GraphOrder>> ReadMainOrdersAsync(
        IReadOnlyList<SignalPreviewRow> inputs,
        CancellationToken cancellationToken)
    {
        var inputByRun = inputs.ToDictionary(item => item.RunId);
        var result = new List<GraphOrder>(inputs.Count);
        foreach (var batch in inputs.Select(item => item.RunId).Order().Chunk(BatchSize))
        {
            await using var command = Command(BuildMainOrdersSql());
            command.Parameters.AddWithValue("run_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, batch);
            await using var data = await command.ExecuteReaderAsync(
                CommandBehavior.SingleResult,
                cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                var row = ReadGraphOrder(
                    data,
                    "Main",
                    null,
                    keepRawDecision: true,
                    recordGraphMismatch: true,
                    out _);
                if (!inputByRun.TryGetValue(row.RunId, out var input))
                {
                    Error("main", row.RunId.ToString("D"), "database_returned_unrequested_run", "");
                    continue;
                }

                ValidateMainOrder(input, row);
                result.Add(row with { RawDecisionJson = null });
            }
        }

        var returned = result.Select(item => item.RunId).ToHashSet();
        foreach (var missing in inputs.Where(item => !returned.Contains(item.RunId)))
        {
            Error("main", missing.RunId.ToString("D"), "remove_run_not_found", missing.StrategyCode);
        }

        return result;
    }

    private async Task<IReadOnlyList<GraphOrder>> ReadChildOrdersAsync(
        IReadOnlyList<ChildStrategy> childStrategies,
        IReadOnlyDictionary<Guid, GraphOrder> mainByRun,
        IReadOnlyDictionary<Guid, GraphOrder> mainByOrder,
        IReadOnlyDictionary<Guid, GraphOrder> mainBySignal,
        CancellationToken cancellationToken)
    {
        var result = new List<GraphOrder>();
        var catalogById = childStrategies.ToDictionary(item => item.Id);
        var preflightChildOrders = await ReadRawChildOrderReferencesAsync(
            mainByRun,
            mainByOrder,
            mainBySignal,
            cancellationToken);
        await using var command = Command(BuildChildOrdersSql());
        command.Parameters.AddWithValue("cutoff_utc", NpgsqlDbType.TimestampTz, cutoffUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "parent_keys",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            new[] { "parent_run_id", "parent_paper_order_id", "parent_signal_id" });
        await using var data = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken);
        while (await data.ReadAsync(cancellationToken))
        {
            var candidate = ReadGraphOrder(
                data,
                "Child",
                null,
                keepRawDecision: true,
                recordGraphMismatch: false,
                out var graphIdentityValid);
            var match = ChildLinkMatcher.Match(candidate, mainByRun, mainByOrder, mainBySignal);
            if (match.Disposition == ChildLinkDisposition.Unrelated)
            {
                continue;
            }

            if (match.Disposition == ChildLinkDisposition.InvariantError || match.ParentRunId is null)
            {
                Error("child", candidate.RunId.ToString("D"), "child_link_invariant", match.Reason);
                continue;
            }

            if (!graphIdentityValid)
            {
                Error("child", candidate.RunId.ToString("D"), "linked_child_graph_identity_invalid", "");
                continue;
            }

            if (!catalogById.TryGetValue(candidate.StrategyId, out var childStrategy) ||
                !string.Equals(candidate.StrategyCode, childStrategy.Code, StringComparison.Ordinal))
            {
                Error("child", candidate.RunId.ToString("D"), "linked_child_outside_current_catalog", "");
                continue;
            }

            if (!IsRemovalGraphStateValid(candidate))
            {
                Error("child", candidate.RunId.ToString("D"), "linked_child_not_valid_settled_removal", candidate.RunStatus);
                continue;
            }

            result.Add(candidate with
            {
                ParentMainRunId = match.ParentRunId,
                RawDecisionJson = null
            });
        }

        var resultOrderIds = result.Select(item => item.OrderId).ToHashSet();
        foreach (var missing in preflightChildOrders.Values.Where(item => !resultOrderIds.Contains(item.OrderId)))
        {
            Error(
                "child-order",
                missing.OrderId.ToString("D"),
                "raw_linked_child_order_missing_from_exact_run_order_signal_graph",
                $"signal_id={missing.SignalId:D};strategy_id={missing.StrategyId:D};parent_run_id={missing.ParentRunId:D}");
        }
        foreach (var unexpected in result.Where(item => !preflightChildOrders.ContainsKey(item.OrderId)))
        {
            Error(
                "child-order",
                unexpected.OrderId.ToString("D"),
                "joined_child_graph_missing_from_independent_raw_order_preflight",
                unexpected.RunId.ToString("D"));
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<Guid, RawChildOrderReference>> ReadRawChildOrderReferencesAsync(
        IReadOnlyDictionary<Guid, GraphOrder> mainByRun,
        IReadOnlyDictionary<Guid, GraphOrder> mainByOrder,
        IReadOnlyDictionary<Guid, GraphOrder> mainBySignal,
        CancellationToken cancellationToken)
    {
        var references = new Dictionary<Guid, RawChildOrderReference>();
        await using (var command = Command("""
            SELECT paper_order.id, paper_order.signal_id, paper_order.strategy_id,
                   paper_order.created_at_utc, paper_order.raw_decision_json::text
            FROM paper_orders paper_order
            WHERE paper_order.raw_decision_json ?| @parent_keys
            ORDER BY paper_order.created_at_utc, paper_order.id;
            """))
        {
            command.Parameters.AddWithValue(
                "parent_keys",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                new[] { "parent_run_id", "parent_paper_order_id", "parent_signal_id" });
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                var orderId = data.GetGuid(0);
                var inspection = ChildLinkMatcher.InspectParentReference(
                    data.GetString(4),
                    mainByRun,
                    mainByOrder,
                    mainBySignal);
                if (inspection.Disposition == ChildLinkDisposition.Unrelated)
                {
                    continue;
                }
                if (inspection.Disposition == ChildLinkDisposition.InvariantError)
                {
                    Error("child-order", orderId.ToString("D"), inspection.Reason, "raw_paper_order_preflight");
                    continue;
                }
                references[orderId] = new RawChildOrderReference(
                    orderId,
                    data.GetGuid(1),
                    data.GetGuid(2),
                    ReadUtc(data, 3),
                    inspection.ParentRunId!.Value);
            }
        }

        if (references.Count == 0)
        {
            return references;
        }

        var runLinksById = new Dictionary<Guid, ChildRunLinkEvidence>();
        var referenceOrderIds = references.Keys.Order().ToArray();
        var referenceSignalIds = references.Values.Select(item => item.SignalId).Distinct().Order().ToArray();
        foreach (var parameterSet in BatchGuidParameterSets(referenceOrderIds, referenceSignalIds))
        {
            await using var command = Command("""
                SELECT strategy_run.id, strategy_run.strategy_id, strategy_run.signal_id,
                       strategy_run.paper_order_id, strategy_run.entry_due_at_utc
                FROM strategy_market_paper_runs strategy_run
                WHERE strategy_run.paper_order_id = ANY(@order_ids)
                   OR strategy_run.signal_id = ANY(@signal_ids)
                ORDER BY strategy_run.id;
                """);
            command.Parameters.AddWithValue("order_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[0]);
            command.Parameters.AddWithValue("signal_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[1]);
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                var link = new ChildRunLinkEvidence(
                    data.GetGuid(0),
                    data.GetGuid(1),
                    data.IsDBNull(2) ? null : data.GetGuid(2),
                    data.IsDBNull(3) ? null : data.GetGuid(3),
                    ReadUtc(data, 4));
                runLinksById[link.RunId] = link;
            }
        }

        var runLinks = runLinksById.Values.ToArray();
        var valid = new Dictionary<Guid, RawChildOrderReference>();
        var runLinksByOrder = runLinks
            .Where(item => item.PaperOrderId is not null)
            .GroupBy(item => item.PaperOrderId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var runLinksBySignal = runLinks
            .Where(item => item.SignalId is not null)
            .GroupBy(item => item.SignalId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var reference in references.Values)
        {
            var candidates = (runLinksByOrder.GetValueOrDefault(reference.OrderId) ?? [])
                .Concat(runLinksBySignal.GetValueOrDefault(reference.SignalId) ?? [])
                .DistinctBy(item => item.RunId)
                .ToArray();
            var validation = ChildLinkMatcher.ValidateOrderRunLink(
                reference.OrderId,
                reference.SignalId,
                reference.StrategyId,
                cutoffUtc,
                candidates);
            if (!validation.Valid)
            {
                Error(
                    "child-order",
                    reference.OrderId.ToString("D"),
                    validation.Reason,
                    $"candidate_runs={validation.CandidateRunCount};" +
                    $"exact_pre_cutoff_runs={validation.ExactPreCutoffRunCount};" +
                    $"signal_id={reference.SignalId:D};strategy_id={reference.StrategyId:D}");
                continue;
            }
            valid[reference.OrderId] = reference;
        }
        return valid;
    }

    private void ValidateMainOrder(SignalPreviewRow input, GraphOrder row)
    {
        if (row.StrategyId != input.StrategyId ||
            !string.Equals(row.StrategyCode, input.StrategyCode, StringComparison.Ordinal) ||
            !string.Equals(row.MarketId, input.MarketId, StringComparison.Ordinal) ||
            row.OrderId != input.PaperOrderId ||
            row.EntryDueAtUtc != input.EntryDueAtUtc ||
            row.EntryDueAtUtc >= cutoffUtc)
        {
            Error("main", row.RunId.ToString("D"), "remove_input_database_identity_mismatch", "");
        }

        if (!IsRemovalGraphStateValid(row))
        {
            Error("main", row.RunId.ToString("D"), "remove_database_state_invalid", "");
        }

        if (!HasExactLegacyReferenceDecisionSource(input.Family, row.RawDecisionJson))
        {
            Error("main", row.RunId.ToString("D"), "legacy_reference_decision_provenance_missing", input.Family);
        }
        try
        {
            removalStakeEvidenceByRun[row.RunId] = RemovalStakeEvidenceParser.Parse(row, row.RawDecisionJson);
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
        {
            Error("main", row.RunId.ToString("D"), "removal_stake_proof_invalid", exception.Message);
        }
    }

    private static bool IsRemovalGraphStateValid(GraphOrder row)
    {
        var filledStatus = string.Equals(row.OrderStatus, "Filled", StringComparison.Ordinal) ||
            string.Equals(row.OrderStatus, "PartiallyFilled", StringComparison.Ordinal) ||
            string.Equals(row.OrderStatus, "PartiallyFilledExpired", StringComparison.Ordinal);
        return string.Equals(row.RunStatus, "Settled", StringComparison.Ordinal) &&
            string.Equals(row.RunOutcome, "Up", StringComparison.Ordinal) &&
            string.Equals(row.OrderOutcome, "Up", StringComparison.Ordinal) &&
            string.Equals(row.OrderSide, "Buy", StringComparison.Ordinal) &&
            filledStatus &&
            row.SettledAtUtc is not null && row.RunRealizedPnlUsd is not null &&
            row.SettlementPrice is 0m or 1m && row.SettlementValueUsd is not null &&
            row.RunAssetId is not null &&
            string.Equals(row.RunAssetId, row.AssetId, StringComparison.Ordinal) &&
            string.Equals(row.CopiedTraderWallet, "strategy:" + row.StrategyCode, StringComparison.Ordinal) &&
            row.EntryPrice is > 0m && row.StakeUsd > 0m && row.RunSizeShares is > 0m &&
            row.OrderPrice > 0m && row.OrderSizeShares > 0m && row.OrderNotionalUsd > 0m;
    }

    private static bool HasExactLegacyReferenceDecisionSource(string family, string? rawDecisionJson)
    {
        if (string.IsNullOrWhiteSpace(rawDecisionJson))
        {
            return false;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(rawDecisionJson);
            var node = document.RootElement;
            var nestedProperty = family switch
            {
                "BpsConfirmedAverage" => "base_signal_decision",
                "DiffConfirmedAverage" => "confirmation_signal_decision",
                _ => null
            };
            if (nestedProperty is not null &&
                (!node.TryGetProperty(nestedProperty, out node) ||
                 node.ValueKind != System.Text.Json.JsonValueKind.Object))
            {
                return false;
            }

            return node.TryGetProperty("decision_source", out var source) &&
                source.ValueKind == System.Text.Json.JsonValueKind.String &&
                string.Equals(
                    source.GetString(),
                    CorrectionContract.LegacyReferenceDecisionSource,
                    StringComparison.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private void ValidateGraphUniqueness(IReadOnlyList<GraphOrder> graphOrders)
    {
        ValidateUnique(graphOrders, item => item.RunId, "run_id");
        ValidateUnique(graphOrders, item => item.OrderId, "paper_order_id");
        ValidateUnique(graphOrders, item => item.SignalId, "signal_id");
        foreach (var row in graphOrders)
        {
            if (!string.Equals(row.CopiedTraderWallet, "strategy:" + row.StrategyCode, StringComparison.Ordinal))
            {
                Error("graph", row.RunId.ToString("D"), "strategy_wallet_mismatch", row.CopiedTraderWallet);
            }

            var validation = CorrectionGraphInvariantValidator.ValidateRow(row);
            if (!validation.Valid)
            {
                Error(row.Scope.ToLowerInvariant(), row.RunId.ToString("D"), validation.Reason, validation.Details);
            }
        }
    }

    private void ValidateUnique<T>(
        IReadOnlyList<GraphOrder> rows,
        Func<GraphOrder, T> selector,
        string field)
        where T : notnull
    {
        foreach (var duplicate in rows.GroupBy(selector).Where(group => group.Count() > 1))
        {
            Error("graph", duplicate.Key.ToString() ?? string.Empty, "duplicate_" + field,
                string.Join(';', duplicate.Select(item => item.RunId.ToString("D"))));
        }
    }

    private async Task<IReadOnlyList<GraphFill>> ReadGraphFillsAsync(
        IReadOnlyList<GraphOrder> graphOrders,
        CancellationToken cancellationToken)
    {
        var orderById = graphOrders.ToDictionary(item => item.OrderId);
        var result = new List<GraphFill>(graphOrders.Count);
        foreach (var batch in orderById.Keys.Order().Chunk(BatchSize))
        {
            await using var command = Command("""
                SELECT fill_row.id, fill_row.paper_order_id, fill_row.price, fill_row.size_shares,
                       fill_row.filled_at_utc, fill_row.realized_pnl_usd, fill_row.evidence,
                       upper(encode(sha256(convert_to(to_jsonb(fill_row)::text, 'UTF8')), 'hex')) AS fill_full_row_sha256
                FROM paper_fills fill_row
                WHERE fill_row.paper_order_id = ANY(@order_ids)
                ORDER BY fill_row.paper_order_id, fill_row.filled_at_utc, fill_row.id;
                """);
            command.Parameters.AddWithValue("order_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, batch);
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                var orderId = data.GetGuid(1);
                var order = orderById[orderId];
                var fill = new GraphFill(
                    order.Scope,
                    order.ParentMainRunId,
                    order.RunId,
                    orderId,
                    data.GetGuid(0),
                    data.GetDecimal(2),
                    data.GetDecimal(3),
                    ReadUtc(data, 4),
                    data.GetDecimal(5),
                    data.GetString(6),
                    data.GetString(7));
                if (fill.Price <= 0m || fill.SizeShares <= 0m)
                {
                    Error("fill", fill.FillId.ToString("D"), "non_positive_graph_fill", "");
                }
                result.Add(fill);
            }
        }

        var fillGroups = result.GroupBy(item => item.OrderId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var order in graphOrders)
        {
            var fills = fillGroups.GetValueOrDefault(order.OrderId) ?? [];
            if (fills.Length == 0 || fills.All(item => item.Price <= 0m || item.SizeShares <= 0m))
            {
                Error("fill", order.OrderId.ToString("D"), "graph_order_has_no_positive_fill", order.RunId.ToString("D"));
            }
        }

        return result;
    }

    private IReadOnlyList<MainRemovalSummary> BuildMainSummaries(
        IReadOnlyList<GraphOrder> mainOrders,
        IReadOnlyList<GraphFill> fills,
        IReadOnlyList<SignalPreviewRow> inputs)
    {
        var fillsByOrder = fills.GroupBy(item => item.OrderId).ToDictionary(group => group.Key, group => group.ToArray());
        var inputByRun = inputs.ToDictionary(item => item.RunId);
        return mainOrders.OrderBy(item => item.RunId).Select(order =>
        {
            var orderFills = fillsByOrder.GetValueOrDefault(order.OrderId) ?? [];
            var input = inputByRun[order.RunId];
            var stakeEvidence = removalStakeEvidenceByRun.GetValueOrDefault(order.RunId) ??
                new RemovalStakeEvidence(0m, 0m, 0m, string.Empty, string.Empty);
            return new MainRemovalSummary(
                order.RunId, order.StrategyId, order.StrategyCode, order.MarketId,
                order.OrderId, order.SignalId, order.AssetId, order.OrderOutcome,
                order.CopiedTraderWallet, orderFills.Length,
                orderFills.Sum(item => item.SizeShares),
                orderFills.Sum(item => item.Price * item.SizeShares),
                orderFills.Sum(item => item.RealizedPnlUsd),
                order.RunRealizedPnlUsd ?? throw new InvalidDataException("main_settled_pnl_missing"),
                order.SettledAtUtc ?? throw new InvalidDataException("main_settled_timestamp_missing"),
                CorrectionContract.CorrectedSkipReason,
                order.OrderCreatedAtUtc,
                stakeEvidence.BaseStakeUsd,
                stakeEvidence.EffectiveStakeUsd,
                stakeEvidence.TargetNotionalUsd,
                stakeEvidence.StakeSizingSource,
                stakeEvidence.ProofSha256,
                input.Action,
                input.Reason,
                CorrectionContract.RequiredInputManifestSha256,
                CorrectionContract.RequiredInputReplayClassifierSha256,
                input.ReplayEvidenceJson,
                input.ReplayEvidenceSha256,
                CanonicalEvidence.HashGraphOrder(order),
                CanonicalEvidence.HashFillSet(orderFills));
        }).ToArray();
    }

    private void ValidateGraphArithmetic(
        IReadOnlyList<GraphOrder> graphOrders,
        IReadOnlyList<GraphFill> graphFills)
    {
        var fillsByOrder = graphFills
            .GroupBy(item => item.OrderId)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.FilledAtUtc).ToArray());
        foreach (var order in graphOrders)
        {
            var fills = fillsByOrder.GetValueOrDefault(order.OrderId) ?? [];
            var validation = CorrectionGraphInvariantValidator.ValidateFillSet(order, fills);
            if (!validation.Valid)
            {
                Error(
                    order.Scope.ToLowerInvariant(),
                    order.RunId.ToString("D"),
                    validation.Reason,
                    validation.Details);
            }
        }

        var mainByRun = graphOrders.Where(item => string.Equals(item.Scope, "Main", StringComparison.Ordinal))
            .ToDictionary(item => item.RunId);
        foreach (var child in graphOrders.Where(item => string.Equals(item.Scope, "Child", StringComparison.Ordinal)))
        {
            if (child.ParentMainRunId is not { } parentRunId || !mainByRun.TryGetValue(parentRunId, out var parent))
            {
                Error("child", child.RunId.ToString("D"), "child_parent_main_missing_for_fill_parity", "");
                continue;
            }

            var validation = CorrectionGraphInvariantValidator.ValidateChildParentFillParity(
                child,
                parent,
                fillsByOrder.GetValueOrDefault(child.OrderId) ?? [],
                fillsByOrder.GetValueOrDefault(parent.OrderId) ?? []);
            if (!validation.Valid)
            {
                Error("child", child.RunId.ToString("D"), validation.Reason, validation.Details);
            }
        }
    }

    private IReadOnlyList<ChildRemovalSummary> BuildChildSummaries(
        IReadOnlyList<GraphOrder> childOrders,
        IReadOnlyList<GraphFill> fills,
        IReadOnlyDictionary<Guid, GraphOrder> mainByRun)
    {
        var fillsByOrder = fills.GroupBy(item => item.OrderId).ToDictionary(group => group.Key, group => group.ToArray());
        return childOrders.OrderBy(item => item.ParentMainRunId).ThenBy(item => item.RunId).Select(order =>
        {
            var parent = mainByRun[order.ParentMainRunId!.Value];
            var orderFills = fillsByOrder.GetValueOrDefault(order.OrderId) ?? [];
            return new ChildRemovalSummary(
                parent.RunId, parent.OrderId, parent.SignalId,
                order.RunId, order.StrategyId, order.StrategyCode, order.MarketId,
                order.OrderId, order.SignalId, order.OrderOutcome,
                orderFills.Length,
                orderFills.Sum(item => item.SizeShares),
                orderFills.Sum(item => item.Price * item.SizeShares),
                order.RunRealizedPnlUsd ?? throw new InvalidDataException("child_settled_pnl_missing"),
                order.SettledAtUtc ?? throw new InvalidDataException("child_settled_timestamp_missing"),
                CanonicalEvidence.HashGraphOrder(order),
                CanonicalEvidence.HashFillSet(orderFills));
        }).ToArray();
    }

    private async Task<IReadOnlyList<LiveShadowOverlap>> ReadLiveShadowOverlapsAsync(
        IReadOnlyList<GraphOrder> graphOrders,
        CancellationToken cancellationToken)
    {
        var orderIds = graphOrders.Select(item => item.OrderId).ToHashSet();
        var signalIds = graphOrders.Select(item => item.SignalId).ToHashSet();
        var correlations = graphOrders.Where(item => item.CorrelationId is not null)
            .Select(item => item.CorrelationId!.Value).ToHashSet();
        var liveRows = new Dictionary<Guid, LiveLink>();
        await ReadLiveLinksAsync(
            orderIds.Order().ToArray(),
            signalIds.Order().ToArray(),
            correlations.Order().ToArray(),
            liveRows,
            cancellationToken);

        foreach (var live in liveRows.Values)
        {
            if (live.CorrelationId is { } correlationId)
            {
                correlations.Add(correlationId);
            }
        }

        var shadowRows = new Dictionary<Guid, ShadowLink>();
        await ReadShadowLinksAsync(
            orderIds.Order().ToArray(),
            signalIds.Order().ToArray(),
            liveRows.Keys.Order().ToArray(),
            correlations.Order().ToArray(),
            shadowRows,
            cancellationToken);
        foreach (var shadow in shadowRows.Values)
        {
            correlations.Add(shadow.CorrelationId);
        }

        var discrepancies = new Dictionary<Guid, DiscrepancyLink>();
        foreach (var batch in correlations.Order().Chunk(BatchSize))
        {
            await using var command = Command("""
                SELECT id, correlation_id, strategy_id, classification, severity, details
                FROM paper_live_shadow_discrepancies
                WHERE correlation_id = ANY(@ids)
                ORDER BY correlation_id, id;
                """);
            command.Parameters.AddWithValue(
                "ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, batch);
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                discrepancies[data.GetGuid(0)] = new DiscrepancyLink(
                    data.GetGuid(0), data.GetGuid(1), data.GetGuid(2),
                    data.GetString(3), data.GetString(4), data.GetString(5));
            }
        }

        var result = new List<LiveShadowOverlap>();
        result.AddRange(liveRows.Values.Select(row => new LiveShadowOverlap(
            ResolveLinkRelation(row.PaperOrderId, row.SignalId, row.CorrelationId, orderIds, signalIds, correlations),
            "LiveOrder", row.Id.ToString("D"), row.StrategyId, row.PaperOrderId,
            row.SignalId, row.Id, row.CorrelationId, row.Status,
            $"order_id={row.ExchangeOrderId};response_status={row.ResponseStatus};" +
            $"filled_size={Format.Decimal(row.FilledSize)};remaining_size={Format.Decimal(row.RemainingSize)};" +
            $"settled_at={Format.Timestamp(row.SettledAtUtc)};execution_source={row.ExecutionSource}", true)));
        result.AddRange(shadowRows.Values.Select(row => new LiveShadowOverlap(
            ResolveLinkRelation(row.PaperOrderId, row.SignalId, row.CorrelationId, orderIds, signalIds, correlations),
            "ShadowDecision", row.CorrelationId.ToString("D"), row.StrategyId,
            row.PaperOrderId, row.SignalId, row.LiveOrderId, row.CorrelationId,
            row.Status, "source=" + row.Source, true)));
        result.AddRange(discrepancies.Values.Select(row => new LiveShadowOverlap(
            "correlation_id", "ShadowDiscrepancy", row.Id.ToString("D"), row.StrategyId,
            null, null, null, row.CorrelationId, row.Severity,
            $"classification={row.Classification};details={row.Details}", true)));
        return result.OrderBy(item => item.RowType, StringComparer.Ordinal)
            .ThenBy(item => item.RowId, StringComparer.Ordinal).ToArray();
    }

    private async Task ReadLiveLinksAsync(
        Guid[] orderIds,
        Guid[] signalIds,
        Guid[] correlationIds,
        IDictionary<Guid, LiveLink> rows,
        CancellationToken cancellationToken)
    {
        foreach (var parameterSet in BatchGuidParameterSets(orderIds, signalIds, correlationIds))
        {
            await using var command = Command("""
                SELECT id, strategy_id, signal_id, paper_order_id, correlation_id, status,
                       order_id, response_status, filled_size, remaining_size, settled_at_utc,
                       execution_source
                FROM live_orders
                WHERE paper_order_id = ANY(@order_ids)
                   OR signal_id = ANY(@signal_ids)
                   OR correlation_id = ANY(@correlation_ids)
                ORDER BY id;
                """);
            command.Parameters.AddWithValue("order_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[0]);
            command.Parameters.AddWithValue("signal_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[1]);
            command.Parameters.AddWithValue("correlation_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[2]);
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                var row = new LiveLink(
                    data.GetGuid(0), data.GetGuid(1), data.GetGuid(2),
                    data.IsDBNull(3) ? null : data.GetGuid(3),
                    data.IsDBNull(4) ? null : data.GetGuid(4),
                    data.GetString(5), data.IsDBNull(6) ? null : data.GetString(6),
                    data.GetString(7), data.GetDecimal(8), data.GetDecimal(9),
                    data.IsDBNull(10) ? null : ReadUtc(data, 10), data.GetString(11));
                rows[row.Id] = row;
            }
        }
    }

    private async Task ReadShadowLinksAsync(
        Guid[] orderIds,
        Guid[] signalIds,
        Guid[] liveOrderIds,
        Guid[] correlationIds,
        IDictionary<Guid, ShadowLink> rows,
        CancellationToken cancellationToken)
    {
        foreach (var parameterSet in BatchGuidParameterSets(orderIds, signalIds, liveOrderIds, correlationIds))
        {
            await using var command = Command("""
                SELECT correlation_id, strategy_id, signal_id, paper_order_id, live_order_id,
                       status, source
                FROM paper_live_shadow_decisions
                WHERE paper_order_id = ANY(@order_ids)
                   OR signal_id = ANY(@signal_ids)
                   OR live_order_id = ANY(@live_order_ids)
                   OR correlation_id = ANY(@correlation_ids)
                ORDER BY correlation_id;
                """);
            command.Parameters.AddWithValue("order_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[0]);
            command.Parameters.AddWithValue("signal_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[1]);
            command.Parameters.AddWithValue("live_order_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[2]);
            command.Parameters.AddWithValue("correlation_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[3]);
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                var row = new ShadowLink(
                    data.GetGuid(0), data.GetGuid(1),
                    data.IsDBNull(2) ? null : data.GetGuid(2),
                    data.IsDBNull(3) ? null : data.GetGuid(3),
                    data.IsDBNull(4) ? null : data.GetGuid(4),
                    data.GetString(5), data.GetString(6));
                rows[row.CorrelationId] = row;
            }
        }
    }

    private static string ResolveLinkRelation(
        Guid? orderId,
        Guid? signalId,
        Guid? correlationId,
        IReadOnlySet<Guid> orderIds,
        IReadOnlySet<Guid> signalIds,
        IReadOnlySet<Guid> correlations)
    {
        var matches = new List<string>();
        if (orderId is { } order && orderIds.Contains(order)) matches.Add("paper_order_id");
        if (signalId is { } signal && signalIds.Contains(signal)) matches.Add("signal_id");
        if (correlationId is { } correlation && correlations.Contains(correlation)) matches.Add("correlation_id");
        return string.Join('+', matches);
    }

    private async Task<(IReadOnlyList<PositionKey> Keys, IReadOnlyList<PositionRow> Positions,
        IReadOnlyList<PositionSettlementRow> Settlements)> ReadPositionsAsync(
        IReadOnlyList<GraphOrder> graphOrders,
        IReadOnlyList<GraphFill> graphFills,
        CancellationToken cancellationToken)
    {
        var graphByKey = graphOrders
            .GroupBy(item => (item.CopiedTraderWallet, item.AssetId))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var databaseOrdersByKey = new Dictionary<(string, string), HashSet<Guid>>();
        var positions = new List<PositionRow>();
        var settlements = new List<PositionSettlementRow>();
        foreach (var batch in graphByKey.Keys
                     .OrderBy(item => item.CopiedTraderWallet, StringComparer.Ordinal)
                     .ThenBy(item => item.AssetId, StringComparer.Ordinal)
                     .Chunk(BatchSize))
        {
            var wallets = batch.Select(item => item.CopiedTraderWallet).ToArray();
            var assets = batch.Select(item => item.AssetId).ToArray();
            await using (var command = Command("""
                WITH target_keys AS (
                    SELECT * FROM unnest(@wallets::text[], @assets::text[]) AS target_key(wallet, asset_id)
                )
                SELECT paper_order.id, paper_order.copied_trader_wallet, paper_order.asset_id
                FROM target_keys target_key
                JOIN paper_orders paper_order
                  ON paper_order.copied_trader_wallet = target_key.wallet
                 AND paper_order.asset_id = target_key.asset_id
                ORDER BY paper_order.copied_trader_wallet, paper_order.asset_id, paper_order.id;
                """))
            {
                command.Parameters.AddWithValue("wallets", NpgsqlDbType.Array | NpgsqlDbType.Text, wallets);
                command.Parameters.AddWithValue("assets", NpgsqlDbType.Array | NpgsqlDbType.Text, assets);
                await using var data = await command.ExecuteReaderAsync(cancellationToken);
                while (await data.ReadAsync(cancellationToken))
                {
                    var key = (data.GetString(1), data.GetString(2));
                    if (!databaseOrdersByKey.TryGetValue(key, out var orderIds))
                    {
                        orderIds = [];
                        databaseOrdersByKey[key] = orderIds;
                    }
                    orderIds.Add(data.GetGuid(0));
                }
            }

            await using (var command = Command("""
                WITH target_keys AS (
                    SELECT * FROM unnest(@wallets::text[], @assets::text[]) AS target_key(wallet, asset_id)
                )
                SELECT position_row.id, position_row.copied_trader_wallet, position_row.asset_id,
                       position_row.condition_id, position_row.outcome, position_row.size_shares,
                       position_row.average_price, position_row.estimated_value_usd,
                       position_row.unrealized_pnl_usd, position_row.updated_at_utc,
                       upper(encode(sha256(convert_to(to_jsonb(position_row)::text, 'UTF8')), 'hex')) AS position_full_row_sha256
                FROM target_keys target_key
                JOIN paper_positions position_row
                  ON position_row.copied_trader_wallet = target_key.wallet
                 AND position_row.asset_id = target_key.asset_id
                ORDER BY position_row.copied_trader_wallet, position_row.asset_id, position_row.id;
                """))
            {
                command.Parameters.AddWithValue("wallets", NpgsqlDbType.Array | NpgsqlDbType.Text, wallets);
                command.Parameters.AddWithValue("assets", NpgsqlDbType.Array | NpgsqlDbType.Text, assets);
                await using var data = await command.ExecuteReaderAsync(cancellationToken);
                while (await data.ReadAsync(cancellationToken))
                {
                    positions.Add(new PositionRow(
                        data.GetGuid(0), data.GetString(1), data.GetString(2), data.GetString(3),
                        data.GetString(4), data.GetDecimal(5), data.GetDecimal(6), data.GetDecimal(7),
                        data.GetDecimal(8), ReadUtc(data, 9), data.GetString(10)));
                }
            }

            await using (var command = Command("""
                WITH target_keys AS (
                    SELECT * FROM unnest(@wallets::text[], @assets::text[]) AS target_key(wallet, asset_id)
                )
                SELECT settlement_row.id, settlement_row.copied_trader_wallet, settlement_row.asset_id,
                       settlement_row.condition_id, settlement_row.outcome, settlement_row.winning_asset_id,
                       settlement_row.winning_outcome, settlement_row.settled_size_shares,
                       settlement_row.average_price, settlement_row.cost_basis_usd,
                       settlement_row.settlement_value_usd, settlement_row.realized_pnl_usd,
                       settlement_row.won, settlement_row.settlement_source, settlement_row.settled_at_utc,
                       settlement_row.category, settlement_row.created_at_utc,
                       upper(encode(sha256(convert_to(to_jsonb(settlement_row)::text, 'UTF8')), 'hex')) AS settlement_full_row_sha256
                FROM target_keys target_key
                JOIN paper_position_settlements settlement_row
                  ON settlement_row.copied_trader_wallet = target_key.wallet
                 AND settlement_row.asset_id = target_key.asset_id
                ORDER BY settlement_row.copied_trader_wallet, settlement_row.asset_id, settlement_row.id;
                """))
            {
                command.Parameters.AddWithValue("wallets", NpgsqlDbType.Array | NpgsqlDbType.Text, wallets);
                command.Parameters.AddWithValue("assets", NpgsqlDbType.Array | NpgsqlDbType.Text, assets);
                await using var data = await command.ExecuteReaderAsync(cancellationToken);
                while (await data.ReadAsync(cancellationToken))
                {
                    settlements.Add(new PositionSettlementRow(
                        data.GetGuid(0), data.GetString(1), data.GetString(2), data.GetString(3),
                        data.GetString(4), data.IsDBNull(5) ? null : data.GetString(5),
                        data.GetString(6), data.GetDecimal(7), data.GetDecimal(8), data.GetDecimal(9),
                        data.GetDecimal(10), data.GetDecimal(11), data.GetBoolean(12), data.GetString(13),
                        ReadUtc(data, 14), data.IsDBNull(15) ? null : data.GetString(15),
                        ReadUtc(data, 16), data.GetString(17)));
                }
            }
        }

        var positionsByKey = positions.GroupBy(item => (item.CopiedTraderWallet, item.AssetId))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var settlementsByKey = settlements.GroupBy(item => (item.CopiedTraderWallet, item.AssetId))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var fillsByOrder = graphFills.GroupBy(item => item.OrderId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var keys = new List<PositionKey>(graphByKey.Count);
        foreach (var pair in graphByKey.OrderBy(item => item.Key.CopiedTraderWallet, StringComparer.Ordinal)
                     .ThenBy(item => item.Key.AssetId, StringComparer.Ordinal))
        {
            var graphIds = pair.Value.Select(item => item.OrderId).ToHashSet();
            var databaseIds = databaseOrdersByKey.GetValueOrDefault(pair.Key) ?? [];
            var outside = databaseIds.Count(id => !graphIds.Contains(id));
            var positionCount = positionsByKey.GetValueOrDefault(pair.Key)?.Length ?? 0;
            var settlementCount = settlementsByKey.GetValueOrDefault(pair.Key)?.Length ?? 0;
            var exclusive = outside == 0 && databaseIds.SetEquals(graphIds);
            var blocks = !exclusive && (positionCount > 0 || settlementCount > 0);
            var details = exclusive
                ? "all_orders_for_wallet_asset_are_in_graph"
                : "wallet_asset_is_shared_with_orders_outside_graph";
            keys.Add(new PositionKey(
                pair.Key.CopiedTraderWallet, pair.Key.AssetId, graphIds.Count, databaseIds.Count,
                outside, positionCount, settlementCount, exclusive, blocks, details));
            if (positionCount > 1 || settlementCount > 1)
            {
                Error("position", pair.Key.CopiedTraderWallet + "/" + pair.Key.AssetId,
                    "wallet_asset_uniqueness_violation", $"positions={positionCount};settlements={settlementCount}");
            }
            if (databaseIds.Count < graphIds.Count)
            {
                Error("position", pair.Key.CopiedTraderWallet + "/" + pair.Key.AssetId,
                    "graph_order_missing_from_wallet_asset_lookup", "");
            }
            if (exclusive)
            {
                var positionRows = positionsByKey.GetValueOrDefault(pair.Key) ?? [];
                var settlementRows = settlementsByKey.GetValueOrDefault(pair.Key) ?? [];
                var keyFills = pair.Value
                    .SelectMany(item => fillsByOrder.GetValueOrDefault(item.OrderId) ?? [])
                    .ToArray();
                var validation = PositionEvidenceValidator.ValidateExclusiveKey(
                    pair.Value,
                    keyFills,
                    positionRows,
                    settlementRows);
                if (!validation.Valid)
                {
                    Error(
                        "position",
                        pair.Key.CopiedTraderWallet + "/" + pair.Key.AssetId,
                        validation.Reason,
                        validation.Details);
                }
            }
        }

        return (
            keys,
            positions.OrderBy(item => item.CopiedTraderWallet, StringComparer.Ordinal).ThenBy(item => item.AssetId).ToArray(),
            settlements.OrderBy(item => item.CopiedTraderWallet, StringComparer.Ordinal).ThenBy(item => item.AssetId).ToArray());
    }

    private async Task<IReadOnlyList<OperationFootprintRow>> ReadOperationFootprintAsync(
        IReadOnlyList<GraphOrder> graphOrders,
        IReadOnlyList<GraphFill> graphFills,
        IReadOnlyList<PositionRow> positions,
        IReadOnlyList<PositionSettlementRow> settlements,
        IReadOnlyList<AddFeasibility> adds,
        CancellationToken cancellationToken)
    {
        var result = new List<OperationFootprintRow>();
        var mainRunIds = graphOrders
            .Where(item => string.Equals(item.Scope, "Main", StringComparison.Ordinal))
            .Select(item => item.RunId).Distinct().Order().ToArray();
        var childRunIds = graphOrders
            .Where(item => string.Equals(item.Scope, "Child", StringComparison.Ordinal))
            .Select(item => item.RunId).Distinct().Order().ToArray();
        var feasibleAdds = adds.Where(item => item.CanAdd).OrderBy(item => item.RunId).ToArray();
        var addRunIds = feasibleAdds.Select(item => item.RunId).Distinct().Order().ToArray();
        var graphSignalIds = graphOrders.Select(item => item.SignalId).Distinct().Order().ToArray();
        var graphOrderIds = graphOrders.Select(item => item.OrderId).Distinct().Order().ToArray();
        var graphFillIds = graphFills.Select(item => item.FillId).Distinct().Order().ToArray();
        var positionIds = positions.Select(item => item.Id).Distinct().Order().ToArray();
        var settlementIds = settlements.Select(item => item.Id).Distinct().Order().ToArray();
        var targetStrategyIds = graphOrders.Select(item => item.StrategyId)
            .Concat(feasibleAdds.Select(item => item.StrategyId))
            .Distinct().Order().ToArray();
        var targetWallets = graphOrders.Select(item => item.CopiedTraderWallet)
            .Concat(feasibleAdds.Select(item => "strategy:" + item.StrategyCode))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        async Task AddUuidMeasurementAsync(
            string scope,
            string tableName,
            string operation,
            string queryKey,
            string selector,
            Guid[] identities,
            bool requireOneRowPerIdentity,
            long? plannedDirectRowOperations = null)
        {
            var measurement = await ReadUuidFootprintMeasurementAsync(queryKey, identities, cancellationToken);
            if (requireOneRowPerIdentity && measurement.RowCount != identities.LongLength)
            {
                Error(
                    "operation_footprint",
                    scope,
                    "snapshot_row_count_mismatch",
                    $"table={tableName};selector_count={identities.LongLength};row_count={measurement.RowCount}");
            }
            result.Add(new OperationFootprintRow(
                scope,
                tableName,
                operation,
                selector,
                identities.LongLength,
                measurement.RowCount,
                measurement.PgColumnSizeBytes,
                plannedDirectRowOperations ?? measurement.RowCount,
                true,
                "Exact REPEATABLE READ snapshot aggregation: count(*) and " +
                "sum(pg_column_size(target_row)); indexes, TOAST side tables, triggers and WAL overhead excluded."));
        }

        await AddUuidMeasurementAsync(
            "main_run_updates", "strategy_market_paper_runs", "UPDATE", "strategy_runs_by_id",
            "exact Main run_id allowlist", mainRunIds, true);
        await AddUuidMeasurementAsync(
            "child_run_deletes", "strategy_market_paper_runs", "DELETE", "strategy_runs_by_id",
            "exact Child run_id allowlist", childRunIds, true);
        await AddUuidMeasurementAsync(
            "modeled_add_run_updates", "strategy_market_paper_runs", "UPDATE", "strategy_runs_by_id",
            "exact feasible Add source run_id allowlist", addRunIds, true);
        await AddUuidMeasurementAsync(
            "graph_signal_deletes", "signals", "DELETE", "signals_by_id",
            "exact graph signal_id allowlist", graphSignalIds, true);
        await AddUuidMeasurementAsync(
            "graph_signal_rejection_deletes", "signal_rejections", "DELETE", "signal_rejections_by_signal_id",
            "rows whose signal_id is in the exact graph signal allowlist", graphSignalIds, false);
        await AddUuidMeasurementAsync(
            "graph_order_deletes", "paper_orders", "DELETE", "paper_orders_by_id",
            "exact graph paper_order_id allowlist", graphOrderIds, true);
        await AddUuidMeasurementAsync(
            "graph_fill_deletes", "paper_fills", "DELETE", "paper_fills_by_id",
            "exact complete graph fill_id allowlist", graphFillIds, true);
        await AddUuidMeasurementAsync(
            "exclusive_position_deletes", "paper_positions", "DELETE", "paper_positions_by_id",
            "exact rows proven exclusive by wallet+asset", positionIds, true);
        await AddUuidMeasurementAsync(
            "exclusive_position_settlement_deletes", "paper_position_settlements", "DELETE",
            "paper_position_settlements_by_id", "exact rows proven exclusive by wallet+asset", settlementIds, true);
        await AddUuidMeasurementAsync(
            "dashboard_projection_event_reconciliation", "dashboard_projection_events", "DELETE",
            "dashboard_projection_events_by_strategy_id", "all rows for exact affected strategy_id set",
            targetStrategyIds, false);
        await AddUuidMeasurementAsync(
            "dashboard_projection_queue_reconciliation", "dashboard_projection_reconciliation_queue", "UPSERT",
            "dashboard_projection_queue_by_strategy_id", "all existing rows for exact affected strategy_id set",
            targetStrategyIds, false, targetStrategyIds.LongLength);

        var projectionControl = await ReadProjectionControlFootprintAsync(cancellationToken);
        if (projectionControl.RowCount != 1 || !projectionControl.Initialized ||
            projectionControl.CalculationVersion != 2 ||
            !string.Equals(projectionControl.Status, "Running", StringComparison.Ordinal) ||
            !projectionControl.LastErrorIsNull)
        {
            Error(
                "operation_footprint",
                "dashboard_projection_control/1",
                "projection_control_apply_precondition_mismatch",
                $"row_count={projectionControl.RowCount};initialized={projectionControl.Initialized};" +
                $"calculation_version={projectionControl.CalculationVersion};status={projectionControl.Status};" +
                $"last_error_is_null={projectionControl.LastErrorIsNull}");
        }
        result.Add(new OperationFootprintRow(
            "dashboard_projection_control_transition",
            "dashboard_projection_control",
            "UPDATE",
            "exact singleton_id=1 with initialized=true, calculation_version=2, status=Running, last_error IS NULL",
            1,
            projectionControl.RowCount,
            projectionControl.PgColumnSizeBytes,
            1,
            true,
            $"Exact singleton precondition snapshot: initialized={projectionControl.Initialized};" +
            $"calculation_version={projectionControl.CalculationVersion};status={projectionControl.Status};" +
            $"last_error_is_null={projectionControl.LastErrorIsNull}. " +
            "Target state is PendingHistoryCorrectionBootstrap; indexes, TOAST side tables, triggers and WAL overhead excluded."));

        var copiedTraderQueue = await ReadTextFootprintMeasurementAsync(
            "paper_copied_trader_refresh_queue_by_wallet", targetWallets, cancellationToken);
        result.Add(new OperationFootprintRow(
            "paper_copied_trader_refresh_queue_reconciliation",
            "paper_copied_trader_performance_refresh_queue",
            "UPSERT",
            "all existing rows for exact affected copied_trader_wallet set",
            targetWallets.LongLength,
            copiedTraderQueue.RowCount,
            copiedTraderQueue.PgColumnSizeBytes,
            targetWallets.LongLength,
            true,
            "Exact REPEATABLE READ snapshot aggregation: count(*) and " +
            "sum(pg_column_size(target_row)); indexes, TOAST side tables, triggers and WAL overhead excluded."));

        foreach (var tableName in new[]
                 {
                     "signals", "paper_orders", "paper_fills", "paper_positions",
                     "paper_position_settlements"
                 })
        {
            result.Add(new OperationFootprintRow(
                "modeled_add_inserts",
                tableName,
                "INSERT",
                "one deterministic correction entity per exact feasible Add run",
                addRunIds.LongLength,
                null,
                null,
                addRunIds.LongLength,
                false,
                "Modeled payload row count only. Heap/index/TOAST/WAL bytes cannot be known until PostgreSQL " +
                "materializes the reviewed payload; no byte estimate is fabricated."));
        }

        return result.OrderBy(item => item.Scope, StringComparer.Ordinal)
            .ThenBy(item => item.TableName, StringComparer.Ordinal)
            .ThenBy(item => item.Operation, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<(long RowCount, long PgColumnSizeBytes)> ReadUuidFootprintMeasurementAsync(
        string queryKey,
        Guid[] identities,
        CancellationToken cancellationToken)
    {
        if (identities.Length == 0)
        {
            return (0, 0);
        }

        var sql = queryKey switch
        {
            "strategy_runs_by_id" => FootprintSql("strategy_market_paper_runs", "id"),
            "signals_by_id" => FootprintSql("signals", "id"),
            "signal_rejections_by_signal_id" => FootprintSql("signal_rejections", "signal_id"),
            "paper_orders_by_id" => FootprintSql("paper_orders", "id"),
            "paper_fills_by_id" => FootprintSql("paper_fills", "id"),
            "paper_positions_by_id" => FootprintSql("paper_positions", "id"),
            "paper_position_settlements_by_id" => FootprintSql("paper_position_settlements", "id"),
            "dashboard_projection_events_by_strategy_id" => FootprintSql("dashboard_projection_events", "strategy_id"),
            "dashboard_projection_queue_by_strategy_id" => FootprintSql("dashboard_projection_reconciliation_queue", "strategy_id"),
            _ => throw new InvalidOperationException("Unknown UUID operation-footprint query key: " + queryKey)
        };

        long rowCount = 0;
        long rowBytes = 0;
        foreach (var batch in identities.Distinct().Order().Chunk(BatchSize))
        {
            await using var command = Command(sql);
            command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, batch);
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            if (!await data.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Operation-footprint aggregate returned no row: " + queryKey);
            }
            rowCount += data.GetInt64(0);
            rowBytes += data.GetInt64(1);
        }
        return (rowCount, rowBytes);
    }

    private async Task<(long RowCount, long PgColumnSizeBytes, bool Initialized, int CalculationVersion,
        string Status, bool LastErrorIsNull)> ReadProjectionControlFootprintAsync(
        CancellationToken cancellationToken)
    {
        await using var command = Command("""
            SELECT initialized, calculation_version, status, last_error IS NULL,
                   pg_column_size(target_row)::bigint
            FROM public.dashboard_projection_control AS target_row
            WHERE singleton_id = 1;
            """);
        await using var data = await command.ExecuteReaderAsync(cancellationToken);
        if (!await data.ReadAsync(cancellationToken))
        {
            return (0, 0, false, 0, string.Empty, false);
        }
        var result = (
            RowCount: 1L,
            PgColumnSizeBytes: data.GetInt64(4),
            Initialized: data.GetBoolean(0),
            CalculationVersion: data.GetInt32(1),
            Status: data.GetString(2),
            LastErrorIsNull: data.GetBoolean(3));
        if (await data.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("dashboard_projection_control singleton_id=1 returned multiple rows.");
        }
        return result;
    }

    private async Task<(long RowCount, long PgColumnSizeBytes)> ReadTextFootprintMeasurementAsync(
        string queryKey,
        string[] identities,
        CancellationToken cancellationToken)
    {
        if (identities.Length == 0)
        {
            return (0, 0);
        }
        if (!string.Equals(
                queryKey,
                "paper_copied_trader_refresh_queue_by_wallet",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unknown text operation-footprint query key: " + queryKey);
        }

        const string sql = """
            SELECT count(*)::bigint,
                   COALESCE(sum(pg_column_size(target_row)), 0)::bigint
            FROM public.paper_copied_trader_performance_refresh_queue AS target_row
            WHERE target_row.copied_trader_wallet = ANY(@ids);
            """;
        long rowCount = 0;
        long rowBytes = 0;
        foreach (var batch in identities.Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal).Chunk(BatchSize))
        {
            await using var command = Command(sql);
            command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Text, batch);
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            if (!await data.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Operation-footprint aggregate returned no row: " + queryKey);
            }
            rowCount += data.GetInt64(0);
            rowBytes += data.GetInt64(1);
        }
        return (rowCount, rowBytes);
    }

    private static string FootprintSql(string tableName, string columnName) => $"""
        SELECT count(*)::bigint,
               COALESCE(sum(pg_column_size(target_row)), 0)::bigint
        FROM public.{tableName} AS target_row
        WHERE target_row.{columnName} = ANY(@ids);
        """;

    private async Task<IReadOnlyList<DependencyRow>> ReadDependenciesAsync(
        IReadOnlyList<GraphOrder> graphOrders,
        IReadOnlyList<GraphFill> graphFills,
        IReadOnlyList<PositionRow> positions,
        IReadOnlyList<PositionSettlementRow> settlements,
        IReadOnlyList<LiveShadowOverlap> liveShadow,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, DependencyRow>(StringComparer.Ordinal);
        var graphOrderIds = graphOrders.Select(item => item.OrderId).ToHashSet();
        var graphSignalIds = graphOrders.Select(item => item.SignalId).ToHashSet();
        var graphRunIds = graphOrders.Select(item => item.RunId).ToHashSet();
        var graphCorrelationIds = graphOrders.Where(item => item.CorrelationId is not null)
            .Select(item => item.CorrelationId!.Value).ToHashSet();
        foreach (var batch in graphSignalIds.Order().Chunk(BatchSize))
        {
            await using (var command = Command("""
                SELECT id, signal_id, reason_code, reason_details
                FROM signal_rejections WHERE signal_id = ANY(@ids) ORDER BY signal_id, id;
                """))
            {
                command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, batch);
                await using var data = await command.ExecuteReaderAsync(cancellationToken);
                while (await data.ReadAsync(cancellationToken))
                {
                    AddDependency(result, new DependencyRow(
                        "FK", "signal_id", "signal_rejections", data.GetGuid(0).ToString("D"),
                        null, data.GetGuid(1), null,
                        $"reason_code={data.GetString(2)};details={data.GetString(3)}", false));
                }
            }

            await using (var command = Command("""
                SELECT id, signal_id, strategy_id, status
                FROM dry_run_orders WHERE signal_id = ANY(@ids) ORDER BY signal_id, id;
                """))
            {
                command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, batch);
                await using var data = await command.ExecuteReaderAsync(cancellationToken);
                while (await data.ReadAsync(cancellationToken))
                {
                    AddDependency(result, new DependencyRow(
                        "Semantic", "signal_id", "dry_run_orders", data.GetGuid(0).ToString("D"),
                        null, data.GetGuid(1), null,
                        $"strategy_id={data.GetGuid(2):D};status={data.GetString(3)}", true));
                }
            }

            await using (var command = Command("""
                SELECT id, paper_order_id, signal_id, strategy_id, market_id
                FROM strategy_market_paper_runs
                WHERE signal_id = ANY(@ids)
                ORDER BY signal_id, id;
                """))
            {
                command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, batch);
                await using var data = await command.ExecuteReaderAsync(cancellationToken);
                while (await data.ReadAsync(cancellationToken))
                {
                    var runId = data.GetGuid(0);
                    if (!graphRunIds.Contains(runId))
                    {
                        Guid? signalId = data.IsDBNull(2) ? null : data.GetGuid(2);
                        AddDependency(result, new DependencyRow(
                            "Semantic", "shared_signal_id", "strategy_market_paper_runs", runId.ToString("D"),
                            data.IsDBNull(1) ? null : data.GetGuid(1), signalId, null,
                            $"strategy_id={data.GetGuid(3):D};market_id={data.GetString(4)}", true));
                    }
                }
            }

            await using (var command = Command("""
                SELECT id, signal_id, strategy_id, status, paper_order_id
                FROM live_orders WHERE signal_id = ANY(@ids) ORDER BY signal_id, id;
                """))
            {
                command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, batch);
                await using var data = await command.ExecuteReaderAsync(cancellationToken);
                while (await data.ReadAsync(cancellationToken))
                {
                    AddDependency(result, new DependencyRow(
                        "FK/Semantic", "signal_id", "live_orders", data.GetGuid(0).ToString("D"),
                        data.IsDBNull(4) ? null : data.GetGuid(4), data.GetGuid(1), null,
                        $"strategy_id={data.GetGuid(2):D};status={data.GetString(3)}", true));
                }
            }
        }

        foreach (var batch in graphOrderIds.Order().Chunk(BatchSize))
        {
            await using (var command = Command("""
                SELECT id, paper_order_id, strategy_id, signal_id, market_id
                FROM strategy_market_paper_runs
                WHERE paper_order_id = ANY(@ids)
                ORDER BY paper_order_id, id;
                """))
            {
                command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, batch);
                await using var data = await command.ExecuteReaderAsync(cancellationToken);
                while (await data.ReadAsync(cancellationToken))
                {
                    var runId = data.GetGuid(0);
                    if (!graphRunIds.Contains(runId))
                    {
                        AddDependency(result, new DependencyRow(
                            "FK", "paper_order_id", "strategy_market_paper_runs", runId.ToString("D"),
                            data.GetGuid(1), data.IsDBNull(3) ? null : data.GetGuid(3), null,
                            $"strategy_id={data.GetGuid(2):D};market_id={data.GetString(4)}", true));
                    }
                }
            }

        }

        var orderedGraphOrderIds = graphOrderIds.Order().ToArray();
        var orderedGraphSignalIds = graphSignalIds.Order().ToArray();
        foreach (var parameterSet in BatchGuidParameterSets(orderedGraphOrderIds, orderedGraphSignalIds))
        {
            await using (var command = Command("""
                SELECT id, entry_paper_order_id, entry_signal_id, copied_trader_wallet, asset_id, status
                FROM paper_copied_leader_positions
                WHERE entry_paper_order_id = ANY(@ids)
                   OR entry_signal_id = ANY(@signal_ids)
                ORDER BY entry_paper_order_id, id;
                """))
            {
                command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[0]);
                command.Parameters.AddWithValue(
                    "signal_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[1]);
                await using var data = await command.ExecuteReaderAsync(cancellationToken);
                while (await data.ReadAsync(cancellationToken))
                {
                    AddDependency(result, new DependencyRow(
                        "Semantic", "entry_paper_order_id", "paper_copied_leader_positions",
                        data.GetGuid(0).ToString("D"),
                        data.IsDBNull(1) ? null : data.GetGuid(1),
                        data.IsDBNull(2) ? null : data.GetGuid(2), null,
                        $"wallet={data.GetString(3)};asset_id={data.GetString(4)};status={data.GetString(5)}", true));
                }
            }

            await using (var command = Command("""
                SELECT id, paper_order_id, signal_id, status, decision_code
                FROM polymarket_onchain_paper_signal_results
                WHERE paper_order_id = ANY(@ids)
                   OR signal_id = ANY(@signal_ids)
                ORDER BY paper_order_id, id;
                """))
            {
                command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[0]);
                command.Parameters.AddWithValue(
                    "signal_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[1]);
                await using var data = await command.ExecuteReaderAsync(cancellationToken);
                while (await data.ReadAsync(cancellationToken))
                {
                    AddDependency(result, new DependencyRow(
                        "Semantic", "paper_order_id", "polymarket_onchain_paper_signal_results",
                        data.GetGuid(0).ToString("D"), data.IsDBNull(1) ? null : data.GetGuid(1),
                        data.IsDBNull(2) ? null : data.GetGuid(2), null,
                        $"status={data.GetString(3)};decision_code={data.GetString(4)}", true));
                }
            }
        }

        // A graph signal must not be shared by a Paper order outside the exact graph.
        foreach (var parameterSet in BatchGuidParameterSets(
                     orderedGraphSignalIds,
                     graphCorrelationIds.Order().ToArray()))
        {
            await using var command = Command("""
                SELECT id, signal_id, strategy_id, status, correlation_id
                FROM paper_orders
                WHERE signal_id = ANY(@ids)
                   OR correlation_id = ANY(@correlation_ids)
                ORDER BY signal_id, id;
                """);
            command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, parameterSet[0]);
            command.Parameters.AddWithValue(
                "correlation_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                parameterSet[1]);
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                var orderId = data.GetGuid(0);
                if (!graphOrderIds.Contains(orderId))
                {
                    AddDependency(result, new DependencyRow(
                        "Semantic", "shared_signal_id", "paper_orders", orderId.ToString("D"),
                        orderId, data.IsDBNull(1) ? null : data.GetGuid(1),
                        data.IsDBNull(4) ? null : data.GetGuid(4),
                        $"strategy_id={data.GetGuid(2):D};status={data.GetString(3)}", true));
                }
            }
        }

        var projectionTargetGroups = new (string Kind, IEnumerable<Guid> Ids)[]
        {
            ("StrategyRun", graphRunIds),
            ("PaperOrder", graphOrderIds),
            ("PaperFill", graphFills.Select(item => item.FillId)),
            ("PaperPosition", positions.Select(item => item.Id)),
            ("PaperSettlement", settlements.Select(item => item.Id)),
            ("LiveOrder", liveShadow.Where(item => item.LiveOrderId is not null)
                .Select(item => item.LiveOrderId!.Value))
        };
        foreach (var targetGroup in projectionTargetGroups)
        {
            foreach (var sourceIds in targetGroup.Ids.Distinct().Order().Chunk(BatchSize))
            {
                await using (var command = Command("""
                SELECT event.id, event.source_kind, event.source_id, event.strategy_id, event.operation
                FROM dashboard_projection_events event
                WHERE event.source_kind = @source_kind
                  AND event.source_id = ANY(@source_ids)
                ORDER BY event.source_kind, event.source_id, event.id;
                """))
                {
                    command.Parameters.AddWithValue("source_kind", NpgsqlDbType.Text, targetGroup.Kind);
                    command.Parameters.AddWithValue(
                        "source_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, sourceIds);
                    await using var data = await command.ExecuteReaderAsync(cancellationToken);
                    while (await data.ReadAsync(cancellationToken))
                    {
                        AddDependency(result, new DependencyRow(
                            "Projection", "source_id", "dashboard_projection_events", data.GetInt64(0).ToString(),
                            null, null, null,
                            $"source_kind={data.GetString(1)};source_id={data.GetGuid(2):D};" +
                            $"strategy_id={(data.IsDBNull(3) ? "" : data.GetGuid(3).ToString("D"))};" +
                            $"operation={data.GetString(4)}", false));
                    }
                }

                await using (var command = Command("""
                SELECT fact.source_kind, fact.source_id, fact.fact_kind, fact.strategy_id
                FROM dashboard_strategy_recent_projection_facts fact
                WHERE fact.source_kind = @source_kind
                  AND fact.source_id = ANY(@source_ids)
                ORDER BY fact.source_kind, fact.source_id, fact.fact_kind;
                """))
                {
                    command.Parameters.AddWithValue("source_kind", NpgsqlDbType.Text, targetGroup.Kind);
                    command.Parameters.AddWithValue(
                        "source_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, sourceIds);
                    await using var data = await command.ExecuteReaderAsync(cancellationToken);
                    while (await data.ReadAsync(cancellationToken))
                    {
                        AddDependency(result, new DependencyRow(
                            "Projection", "source_id", "dashboard_strategy_recent_projection_facts",
                            $"{data.GetString(0)}:{data.GetGuid(1):D}:{data.GetString(2)}",
                            null, null, null, $"strategy_id={data.GetGuid(3):D}", false));
                    }
                }
            }
        }

        foreach (var positionIds in positions.Select(item => item.Id).Distinct().Order().Chunk(BatchSize))
        {
            await using var command = Command("""
                SELECT source_id, strategy_id, size_shares, unrealized_pnl_usd
                FROM dashboard_strategy_position_projection_facts
                WHERE source_id = ANY(@ids)
                ORDER BY source_id;
                """);
            command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, positionIds);
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                AddDependency(result, new DependencyRow(
                    "Projection", "source_id", "dashboard_strategy_position_projection_facts",
                    data.GetGuid(0).ToString("D"), null, null, null,
                    $"strategy_id={data.GetGuid(1):D};size={Format.Decimal(data.GetDecimal(2))};" +
                    $"unrealized_pnl={Format.Decimal(data.GetDecimal(3))}", false));
            }
        }

        return result.Values.OrderBy(item => item.DependencyClass, StringComparer.Ordinal)
            .ThenBy(item => item.TableName, StringComparer.Ordinal)
            .ThenBy(item => item.RowId, StringComparer.Ordinal).ToArray();
    }

    private static void AddDependency(IDictionary<string, DependencyRow> target, DependencyRow row)
    {
        target[$"{row.TableName}|{row.RowId}|{row.Relation}"] = row;
    }

    private async Task<(IReadOnlyList<AddFeasibility> Adds,
        IReadOnlyList<MarketResolvedEventEvidence> MarketResolvedDiagnostics)> ReadAddsAsync(
        IReadOnlyList<SignalPreviewRow> inputs,
        CancellationToken cancellationToken)
    {
        var runs = new Dictionary<Guid, AddSourceRow>();
        var gammaByMarket = new Dictionary<string, GammaMarketEvidence>(StringComparer.Ordinal);
        foreach (var batch in inputs.Select(item => item.RunId).Order().Chunk(BatchSize))
        {
            await using var command = Command("""
                SELECT strategy_run.id, strategy_run.strategy_id, strategy.code,
                       strategy_run.market_id, strategy_run.condition_id,
                       strategy_run.status, strategy_run.skip_reason, strategy_run.entry_due_at_utc,
                       strategy_run.skip_diagnostics_json::text, strategy_run.market_end_utc,
                       strategy_run.stake_usd, strategy_run.selected_asset_id,
                       strategy_run.selected_outcome, strategy_run.entry_price, strategy_run.size_shares,
                       strategy_run.signal_id, strategy_run.paper_order_id, strategy_run.entered_at_utc,
                       strategy_run.settlement_price, strategy_run.settlement_value_usd,
                       strategy_run.realized_pnl_usd, strategy_run.settled_at_utc,
                       strategy_run.market_slug,
                       gamma.market_id, gamma.condition_id, gamma.order_min_size,
                       gamma.outcomes_json::text, gamma.clob_token_ids_json::text,
                       upper(encode(sha256(convert_to(to_jsonb(strategy_run)::text, 'UTF8')), 'hex')) AS run_full_row_sha256,
                       strategy_run.updated_at_utc, strategy_run.category
                FROM strategy_market_paper_runs strategy_run
                JOIN strategies strategy ON strategy.id = strategy_run.strategy_id
                LEFT JOIN polymarket_gamma_markets gamma ON gamma.market_id = strategy_run.market_id
                WHERE strategy_run.id = ANY(@run_ids)
                ORDER BY strategy_run.id;
                """);
            command.Parameters.AddWithValue("run_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, batch);
            await using var data = await command.ExecuteReaderAsync(
                CommandBehavior.SingleResult,
                cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                var runId = data.GetGuid(0);
                var skipDiagnosticsJson = data.IsDBNull(8) ? null : data.GetString(8);
                runs[runId] = new AddSourceRow(
                    runId, data.GetGuid(1), data.GetString(2), data.GetString(3), data.GetString(4),
                    data.GetString(5), data.IsDBNull(6) ? null : data.GetString(6), ReadUtc(data, 7),
                    data.IsDBNull(9) ? null : ReadUtc(data, 9), data.GetDecimal(10),
                    data.IsDBNull(11) ? null : data.GetString(11),
                    data.IsDBNull(12) ? null : data.GetString(12),
                    data.IsDBNull(13) ? null : data.GetDecimal(13),
                    data.IsDBNull(14) ? null : data.GetDecimal(14),
                    data.IsDBNull(15) ? null : data.GetGuid(15),
                    data.IsDBNull(16) ? null : data.GetGuid(16),
                    data.IsDBNull(17) ? null : ReadUtc(data, 17),
                    data.IsDBNull(18) ? null : data.GetDecimal(18),
                    data.IsDBNull(19) ? null : data.GetDecimal(19),
                    data.IsDBNull(20) ? null : data.GetDecimal(20),
                    data.IsDBNull(21) ? null : ReadUtc(data, 21),
                    skipDiagnosticsJson,
                    data.GetString(22),
                    data.GetString(28),
                    ReadUtc(data, 29),
                    data.IsDBNull(30) ? null : data.GetString(30));
                if (!data.IsDBNull(23))
                {
                    var gamma = new GammaMarketEvidence(
                        data.GetString(23), data.GetString(24),
                        data.IsDBNull(25) ? null : data.GetDecimal(25),
                        data.GetString(26), data.GetString(27));
                    gammaByMarket[gamma.MarketId] = gamma;
                }
            }
        }

        var selectedTokenByRun = new Dictionary<Guid, string>();
        foreach (var input in inputs)
        {
            if (!runs.TryGetValue(input.RunId, out var run) ||
                !gammaByMarket.TryGetValue(input.MarketId, out var gamma))
            {
                continue;
            }
            try
            {
                selectedTokenByRun[input.RunId] =
                    AddFeasibilityCalculator.MapOutcomeTokens(gamma, "Up").SelectedTokenId;
            }
            catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
            {
                Error("add", input.RunId.ToString("D"), "gamma_outcome_mapping_invalid", exception.Message);
            }
        }

        var addCollisionRuns = await ReadAddCollisionRunIdsAsync(
            inputs,
            runs,
            selectedTokenByRun,
            cancellationToken);

        var marketSet = inputs.Select(item => item.MarketId).ToHashSet(StringComparer.Ordinal);
        var ledgerByMarket = new Dictionary<string, List<ResolvedMarketLedgerEvidence>>(StringComparer.Ordinal);
        foreach (var batch in runs.Values.Select(item => item.ConditionId).Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal).Chunk(BatchSize))
        {
            await using var command = Command("""
                SELECT id, asset_symbol, market_id, condition_id, market_slug,
                       market_start_utc, market_end_utc, winning_outcome,
                       winning_asset_id, event_timestamp_utc, first_received_at_utc,
                       last_received_at_utc, event_count, result_delay_seconds, source,
                       raw_event_type, raw_json::text, created_at_utc, updated_at_utc
                FROM crypto_up_down_5m_websocket_resolved_markets
                WHERE condition_id = ANY(@condition_ids)
                ORDER BY market_id, event_timestamp_utc, id;
                """);
            command.Parameters.AddWithValue("condition_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, batch);
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                var rawJson = data.GetString(16);
                var rawBytes = Encoding.UTF8.GetBytes(rawJson);
                var row = new ResolvedMarketLedgerEvidence(
                    data.GetGuid(0), data.GetString(1), data.GetString(2), data.GetString(3),
                    data.GetString(4), ReadUtc(data, 5), ReadUtc(data, 6), data.GetString(7),
                    data.IsDBNull(8) ? null : data.GetString(8), ReadUtc(data, 9),
                    ReadUtc(data, 10), ReadUtc(data, 11), data.GetInt32(12), data.GetDecimal(13),
                    data.GetString(14), data.GetString(15), rawJson,
                    Convert.ToHexString(SHA256.HashData(rawBytes)), rawBytes.LongLength,
                    ReadUtc(data, 17), ReadUtc(data, 18));
                if (!marketSet.Contains(row.MarketId))
                {
                    continue;
                }
                if (!ledgerByMarket.TryGetValue(row.MarketId, out var rows))
                {
                    rows = [];
                    ledgerByMarket[row.MarketId] = rows;
                }
                rows.Add(row);
            }
        }

        var (diagnosticsByMarket, allDiagnostics) = await ReadMarketResolvedEventDiagnosticsAsync(
            inputs,
            runs,
            cancellationToken);
        var archivedResolutions = await ReadArchivedReferenceResolutionsAsync(inputs, runs, cancellationToken);
        var result = new List<AddFeasibility>(inputs.Count);
        foreach (var input in inputs.OrderBy(item => item.RunId))
        {
            try
            {
                if (!runs.TryGetValue(input.RunId, out var run))
                {
                    throw new InvalidDataException("add_run_not_found");
                }
                if (addCollisionRuns.Contains(input.RunId))
                {
                    throw new InvalidDataException("add_target_has_existing_graph_or_wallet_asset_collision");
                }
                if (run.StrategyId != input.StrategyId ||
                    !string.Equals(run.StrategyCode, input.StrategyCode, StringComparison.Ordinal) ||
                    !string.Equals(run.MarketId, input.MarketId, StringComparison.Ordinal) ||
                    run.EntryDueAtUtc != input.EntryDueAtUtc || run.EntryDueAtUtc >= cutoffUtc ||
                    !string.Equals(run.RunStatus, "Skipped", StringComparison.Ordinal) ||
                    !string.Equals(run.SkipReason, CorrectionContract.PotentialAddSkipReason, StringComparison.Ordinal) ||
                    run.MarketEndUtc is null ||
                    run.SelectedAssetId is not null || run.SelectedOutcome is not null ||
                    run.EntryPrice is not null || run.SizeShares is not null ||
                    run.SignalId is not null || run.PaperOrderId is not null || run.EnteredAtUtc is not null ||
                    run.SettlementPrice is not null || run.SettlementValueUsd is not null ||
                    run.RealizedPnlUsd is not null || run.SettledAtUtc is not null)
                {
                    throw new InvalidDataException("add_run_input_or_state_mismatch");
                }
                AddFeasibilityCalculator.ValidateHistoricalStakeSemantics(run.StakeUsd, run.SkipDiagnosticsJson);
                if (!gammaByMarket.TryGetValue(input.MarketId, out var gamma) ||
                    !string.Equals(gamma.ConditionId, run.ConditionId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("gamma_market_missing_or_condition_mismatch");
                }
                if (!selectedTokenByRun.ContainsKey(input.RunId) ||
                    !liveGammaByMarket.TryGetValue(input.MarketId, out var liveGamma) ||
                    !string.Equals(liveGamma.ConditionId, run.ConditionId, StringComparison.Ordinal) ||
                    !string.Equals(liveGamma.MarketSlug, run.MarketSlug, StringComparison.Ordinal) ||
                    !liveGamma.Closed)
                {
                    throw new InvalidDataException("live_gamma_resolution_identity_or_state_invalid");
                }
                if (!archivedResolutions.TryGetValue(AddResolutionKey(input.Asset, input.MarketId), out var archived))
                {
                    throw new InvalidDataException("archived_reference_resolution_missing");
                }
                if (!ledgerByMarket.TryGetValue(input.MarketId, out var ledgerRows) ||
                    ledgerRows.Count != 1)
                {
                    throw new InvalidDataException(
                        "resolved_market_ledger_row_count=" + (ledgerRows?.Count ?? 0));
                }
                var diagnostics = diagnosticsByMarket.GetValueOrDefault(input.MarketId) ??
                    Array.Empty<MarketResolvedEventEvidence>();
                var rawWebSocket = AddFeasibilityCalculator.ValidateRawMarketResolvedDiagnostics(
                    input,
                    run,
                    gamma,
                    liveGamma,
                    diagnostics);
                var add = AddFeasibilityCalculator.Calculate(
                    input,
                    run,
                    gamma,
                    liveGamma,
                    rawWebSocket,
                    archived,
                    ledgerRows[0]);
                if (!add.ResolutionLedgerWinningAssetAgreesWithGamma &&
                    !add.ResolutionLedgerRawValidated)
                {
                    Error(
                        "add",
                        input.RunId.ToString("D"),
                        "resolution_ledger_winning_asset_disagrees_with_official_mapping",
                        $"market_id={input.MarketId};ledger_asset_id={add.ResolutionLedgerWinningAssetId};" +
                        $"official_winning_token_id={add.ResolvedWinningTokenId}");
                }
                result.Add(add);
            }
            catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
            {
                Error("add", input.RunId.ToString("D"), "add_feasibility_failed", exception.Message);
                result.Add(FailedAdd(input, runs.GetValueOrDefault(input.RunId), gammaByMarket.GetValueOrDefault(input.MarketId), exception.Message));
            }
        }

        return (
            result,
            allDiagnostics.OrderBy(item => item.Id).ToArray());
    }

    private async Task<(IReadOnlyDictionary<string, IReadOnlyList<MarketResolvedEventEvidence>> ByMarket,
        IReadOnlyList<MarketResolvedEventEvidence> AllRows)> ReadMarketResolvedEventDiagnosticsAsync(
        IReadOnlyList<SignalPreviewRow> inputs,
        IReadOnlyDictionary<Guid, AddSourceRow> runs,
        CancellationToken cancellationToken)
    {
        var marketIds = inputs.Select(item => item.MarketId).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var conditionToMarket = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var run in runs.Values)
        {
            if (conditionToMarket.TryGetValue(run.ConditionId, out var existing) &&
                !string.Equals(existing, run.MarketId, StringComparison.Ordinal))
            {
                Error("add", run.RunId.ToString("D"), "add_condition_maps_to_multiple_markets", run.ConditionId);
                continue;
            }
            conditionToMarket[run.ConditionId] = run.MarketId;
        }
        var conditionIds = conditionToMarket.Keys.Order(StringComparer.Ordinal).ToArray();
        if (marketIds.Length == 0 || conditionIds.Length == 0)
        {
            return (
                new Dictionary<string, IReadOnlyList<MarketResolvedEventEvidence>>(StringComparer.Ordinal),
                Array.Empty<MarketResolvedEventEvidence>());
        }

        await using var command = Command("""
            SELECT id, component, raw_event_type, asset_id, condition_id,
                   winning_asset_id, winning_outcome, event_timestamp_utc, received_at_utc,
                   active_snapshot_found, snapshot_market_id, snapshot_condition_id,
                   snapshot_market_slug, snapshot_asset_symbol, snapshot_market_start_utc,
                   snapshot_is_crypto_up_down_5m, recorder_action, raw_json::text, created_at_utc
            FROM market_resolved_event_diagnostics
            WHERE snapshot_market_id = ANY(@market_ids)
               OR snapshot_condition_id = ANY(@condition_ids)
               OR condition_id = ANY(@condition_ids)
            ORDER BY received_at_utc, id;
            """);
        command.Parameters.AddWithValue("market_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, marketIds);
        command.Parameters.AddWithValue("condition_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, conditionIds);
        var byMarket = new Dictionary<string, List<MarketResolvedEventEvidence>>(StringComparer.Ordinal);
        var allRows = new List<MarketResolvedEventEvidence>();
        await using var data = await command.ExecuteReaderAsync(cancellationToken);
        while (await data.ReadAsync(cancellationToken))
        {
            var rawJson = data.GetString(17);
            var rawBytes = Encoding.UTF8.GetBytes(rawJson);
            var row = new MarketResolvedEventEvidence(
                data.GetGuid(0),
                data.GetString(1),
                data.GetString(2),
                data.IsDBNull(3) ? string.Empty : data.GetString(3),
                data.IsDBNull(4) ? string.Empty : data.GetString(4),
                data.IsDBNull(5) ? string.Empty : data.GetString(5),
                data.IsDBNull(6) ? string.Empty : data.GetString(6),
                ReadUtc(data, 7),
                ReadUtc(data, 8),
                data.GetBoolean(9),
                data.IsDBNull(10) ? string.Empty : data.GetString(10),
                data.IsDBNull(11) ? string.Empty : data.GetString(11),
                data.IsDBNull(12) ? string.Empty : data.GetString(12),
                data.IsDBNull(13) ? string.Empty : data.GetString(13),
                data.IsDBNull(14) ? default : ReadUtc(data, 14),
                data.GetBoolean(15),
                data.GetString(16),
                rawJson,
                Convert.ToHexString(SHA256.HashData(rawBytes)),
                rawBytes.LongLength,
                ReadUtc(data, 18));
            allRows.Add(row);

            string? targetMarket = null;
            if (marketIds.Contains(row.SnapshotMarketId, StringComparer.Ordinal))
            {
                targetMarket = row.SnapshotMarketId;
            }
            else if (conditionToMarket.TryGetValue(row.SnapshotConditionId, out var snapshotConditionMarket))
            {
                targetMarket = snapshotConditionMarket;
            }
            else if (conditionToMarket.TryGetValue(row.ConditionId, out var rawConditionMarket))
            {
                targetMarket = rawConditionMarket;
            }
            if (targetMarket is null)
            {
                Error("add", row.Id.ToString("D"), "raw_market_resolved_diagnostic_unmapped", "");
                continue;
            }
            if (!byMarket.TryGetValue(targetMarket, out var rows))
            {
                rows = [];
                byMarket[targetMarket] = rows;
            }
            rows.Add(row);
        }

        return (
            byMarket.ToDictionary(
                item => item.Key,
                item => (IReadOnlyList<MarketResolvedEventEvidence>)item.Value,
                StringComparer.Ordinal),
            allRows);
    }

    private async Task<IReadOnlySet<Guid>> ReadAddCollisionRunIdsAsync(
        IReadOnlyList<SignalPreviewRow> inputs,
        IReadOnlyDictionary<Guid, AddSourceRow> runs,
        IReadOnlyDictionary<Guid, string> selectedTokenByRun,
        CancellationToken cancellationToken)
    {
        var targets = inputs
            .Where(item => runs.ContainsKey(item.RunId) && selectedTokenByRun.ContainsKey(item.RunId))
            .OrderBy(item => item.RunId)
            .ToArray();
        if (targets.Length == 0)
        {
            return new HashSet<Guid>();
        }

        await using var command = Command(AddCollisionSql);
        command.Parameters.AddWithValue(
            "run_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, targets.Select(item => item.RunId).ToArray());
        command.Parameters.AddWithValue(
            "strategy_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, targets.Select(item => item.StrategyId).ToArray());
        command.Parameters.AddWithValue(
            "strategy_codes", NpgsqlDbType.Array | NpgsqlDbType.Text, targets.Select(item => item.StrategyCode).ToArray());
        command.Parameters.AddWithValue(
            "condition_ids", NpgsqlDbType.Array | NpgsqlDbType.Text,
            targets.Select(item => runs[item.RunId].ConditionId).ToArray());
        command.Parameters.AddWithValue(
            "token_ids", NpgsqlDbType.Array | NpgsqlDbType.Text,
            targets.Select(item => selectedTokenByRun[item.RunId]).ToArray());
        command.Parameters.AddWithValue(
            "wallets", NpgsqlDbType.Array | NpgsqlDbType.Text,
            targets.Select(item => "strategy:" + item.StrategyCode).ToArray());
        var result = new HashSet<Guid>();
        await using var data = await command.ExecuteReaderAsync(cancellationToken);
        while (await data.ReadAsync(cancellationToken))
        {
            var runId = data.GetGuid(0);
            result.Add(runId);
            Error("add", runId.ToString("D"), "existing_add_graph_collision",
                $"table={data.GetString(1)};row_id={data.GetString(2)}");
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<string, ArchivedReferenceResolution>>
        ReadArchivedReferenceResolutionsAsync(
            IReadOnlyList<SignalPreviewRow> inputs,
            IReadOnlyDictionary<Guid, AddSourceRow> runs,
            CancellationToken cancellationToken)
    {
        var samples = new List<ReferenceResolutionTick>();
        var btcMarketIds = inputs
            .Where(item => string.Equals(item.Asset, "BTC", StringComparison.Ordinal))
            .Select(item => item.MarketId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (btcMarketIds.Length > 0)
        {
            await using var command = Command("""
                SELECT 'BTC', market_id, condition_id, market_end_utc, sampled_at_utc,
                       binance_price_usd, binance_start_price_usd,
                       binance_source_updated_at_utc, created_at_utc
                FROM btc_up_down_5m_odds_ticks
                WHERE market_id = ANY(@market_ids)
                ORDER BY market_id, sampled_at_utc, created_at_utc;
                """);
            command.Parameters.AddWithValue("market_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, btcMarketIds);
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                samples.Add(ReadReferenceResolutionTick(data));
            }
        }

        var cryptoTargets = inputs
            .Where(item => !string.Equals(item.Asset, "BTC", StringComparison.Ordinal))
            .Select(item => (item.Asset, item.MarketId))
            .Distinct()
            .OrderBy(item => item.Asset, StringComparer.Ordinal)
            .ThenBy(item => item.MarketId, StringComparer.Ordinal)
            .ToArray();
        if (cryptoTargets.Length > 0)
        {
            await using var command = Command("""
                WITH targets(asset_symbol, market_id) AS (
                    SELECT * FROM unnest(@asset_symbols::text[], @market_ids::text[])
                )
                SELECT tick.asset_symbol, tick.market_id, tick.condition_id, tick.market_end_utc,
                       tick.sampled_at_utc, tick.binance_price_usd,
                       tick.binance_start_price_usd, tick.binance_source_updated_at_utc,
                       tick.created_at_utc
                FROM targets target
                JOIN crypto_up_down_5m_odds_ticks tick
                  ON tick.asset_symbol = target.asset_symbol
                 AND tick.market_id = target.market_id
                ORDER BY tick.asset_symbol, tick.market_id, tick.sampled_at_utc, tick.created_at_utc;
                """);
            command.Parameters.AddWithValue(
                "asset_symbols",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                cryptoTargets.Select(item => item.Asset).ToArray());
            command.Parameters.AddWithValue(
                "market_ids",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                cryptoTargets.Select(item => item.MarketId).ToArray());
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                samples.Add(ReadReferenceResolutionTick(data));
            }
        }

        var result = new Dictionary<string, ArchivedReferenceResolution>(StringComparer.Ordinal);
        foreach (var inputGroup in inputs.GroupBy(item => AddResolutionKey(item.Asset, item.MarketId)))
        {
            var input = inputGroup.First();
            if (!runs.TryGetValue(input.RunId, out var run) || run.MarketEndUtc is null)
            {
                continue;
            }
            var marketSamples = samples
                .Where(item => string.Equals(item.AssetSymbol, input.Asset, StringComparison.Ordinal) &&
                               string.Equals(item.MarketId, input.MarketId, StringComparison.Ordinal))
                .ToArray();
            try
            {
                result[inputGroup.Key] = AddFeasibilityCalculator.ReplayArchivedReferenceTicks(
                    input.Asset,
                    input.MarketId,
                    run.ConditionId,
                    run.MarketEndUtc.Value,
                    marketSamples);
            }
            catch (InvalidDataException exception)
            {
                Error("add-resolution", input.MarketId, "archived_reference_replay_failed", exception.Message);
            }
        }

        return result;
    }

    private static ReferenceResolutionTick ReadReferenceResolutionTick(NpgsqlDataReader data) => new(
        data.GetString(0),
        data.GetString(1),
        data.GetString(2),
        ReadUtc(data, 3),
        ReadUtc(data, 4),
        data.GetDecimal(5),
        data.GetDecimal(6),
        ReadUtc(data, 7),
        ReadUtc(data, 8));

    private static string AddResolutionKey(string asset, string marketId) => asset + "|" + marketId;

    private static AddFeasibility FailedAdd(
        SignalPreviewRow input,
        AddSourceRow? run,
        GammaMarketEvidence? gamma,
        string reason)
    {
        decimal multiplier = 0m;
        if (!string.IsNullOrWhiteSpace(run?.SkipDiagnosticsJson))
        {
            try { multiplier = AddFeasibilityCalculator.ReadHistoricalStakeMultiplier(run.SkipDiagnosticsJson); }
            catch { /* The exact failure is retained in reason. */ }
        }
        return new AddFeasibility(
            RunId: input.RunId,
            StrategyId: input.StrategyId,
            StrategyCode: input.StrategyCode,
            MarketId: input.MarketId,
            ConditionId: run?.ConditionId ?? string.Empty,
            Asset: input.Asset,
            Kind: input.Kind,
            AddSourceStateSha256: run is null ? string.Empty : CanonicalEvidence.HashAddSource(run),
            AddSourceRunFullRowSha256: run?.RunFullRowSha256 ?? string.Empty,
            ModeledEntryAtUtc: run?.UpdatedAtUtc ?? default,
            ModeledSettledAtUtc: run?.MarketEndUtc ?? default,
            ModeledSettlementTimestampSource: "not_modeled_add_infeasible",
            SettlementCategory: run?.Category ?? string.Empty,
            ModeledRawDecisionJson: string.Empty,
            ModeledRawDecisionSha256: string.Empty,
            ModeledFillEvidence: string.Empty,
            ModeledMutationPayloadJson: string.Empty,
            ModeledMutationPayloadSha256: string.Empty,
            AssumedFillPrice: input.AssumedFillPrice ?? 0m,
            HistoricalStakeMultiplier: multiplier,
            GammaOrderMinSize: gamma?.OrderMinSize ?? 0m,
            SelectedOutcome: "Up",
            SelectedTokenId: string.Empty,
            ResolvedWinningOutcome: string.Empty,
            ResolvedWinningTokenId: string.Empty,
            ResolutionLedgerSource: string.Empty,
            ResolutionLedgerProvenanceGroup: string.Empty,
            ResolutionLedgerWinningAssetId: string.Empty,
            ResolutionLedgerWinningAssetAgreesWithGamma: false,
            ResolutionLedgerRawEventType: string.Empty,
            ResolutionLedgerRawSha256: string.Empty,
            ResolutionLedgerRawBytes: 0,
            ResolutionLedgerEventTimestampUtc: default,
            ResolutionLedgerRawEventTimestampUtc: null,
            ResolutionLedgerFirstReceivedAtUtc: default,
            ResolutionLedgerLastReceivedAtUtc: default,
            ResolutionLedgerRawValidated: false,
            RawWebSocketResolutionSource: "market_resolved_event_diagnostics:not_evaluated",
            RawWebSocketResolutionProvenanceGroup: string.Empty,
            RawWebSocketDiagnosticRowCount: 0,
            RawWebSocketDistinctEventCount: 0,
            ArchivedTickSource: string.Empty,
            ArchivedTickProvenanceGroup: string.Empty,
            ArchivedTickSampleCount: 0,
            ArchivedTickStartPriceUsd: 0m,
            ArchivedTickEndPriceUsd: 0m,
            ArchivedTickEndAgeMilliseconds: 0m,
            ArchivedTickWinningOutcome: string.Empty,
            ArchivedTickAgreesWithAuthoritativeWinner: false,
            GammaResolutionSource: string.Empty,
            GammaResolutionProvenanceGroup: string.Empty,
            GammaRequestUrl: string.Empty,
            GammaRawSha256: string.Empty,
            GammaRawBytes: 0,
            GammaFetchedAtUtc: default,
            GammaResolutionSourceDetail: string.Empty,
            GammaLiveOrderMinSize: null,
            AgreeingIndependentResolutionSourceCount: 0,
            RawWorstPriceNotionalUsd: 0m,
            RoundedWorstPriceNotionalUsd: 0m,
            WorstPriceTargetSizeShares: 0m,
            RequestedNotionalUsd: 0m,
            FilledSizeShares: 0m,
            Won: false,
            SettlementPrice: 0m,
            SettlementValueUsd: 0m,
            RealizedPnlUsd: 0m,
            CanAdd: false,
            Reason: reason);
    }

    private static string BaseGraphOrderSql(bool fullRawDecision)
    {
        var rawDecisionProjection = fullRawDecision
            ? "paper_order.raw_decision_json::text"
            : "jsonb_build_object(" +
              "'decision_source', paper_order.raw_decision_json -> 'decision_source', " +
              "'order_execution_mode', paper_order.raw_decision_json -> 'order_execution_mode', " +
              "'paper_lost_counter_coeff', paper_order.raw_decision_json -> 'paper_lost_counter_coeff', " +
              "'paper_lost_base_stake_usd', paper_order.raw_decision_json -> 'paper_lost_base_stake_usd', " +
              "'paper_lost_add_stake_usd', paper_order.raw_decision_json -> 'paper_lost_add_stake_usd', " +
              "'paper_lost_effective_stake_usd', paper_order.raw_decision_json -> 'paper_lost_effective_stake_usd', " +
              "'stake_multiplier', paper_order.raw_decision_json -> 'stake_multiplier', " +
              "'stake_sizing_source', paper_order.raw_decision_json -> 'stake_sizing_source', " +
              "'target_notional_usd', paper_order.raw_decision_json -> 'target_notional_usd', " +
              "'target_size_shares', paper_order.raw_decision_json -> 'target_size_shares', " +
              "'paper_fak_average_fill_price', paper_order.raw_decision_json -> 'paper_fak_average_fill_price', " +
              "'paper_fak_filled_size_shares', paper_order.raw_decision_json -> 'paper_fak_filled_size_shares', " +
              "'paper_fak_filled_notional_usd', paper_order.raw_decision_json -> 'paper_fak_filled_notional_usd', " +
              "'paper_fak_partial_fill', paper_order.raw_decision_json -> 'paper_fak_partial_fill', " +
              "'base_signal_decision', jsonb_build_object('decision_source', " +
              "paper_order.raw_decision_json -> 'base_signal_decision' -> 'decision_source'), " +
              "'confirmation_signal_decision', jsonb_build_object('decision_source', " +
              "paper_order.raw_decision_json -> 'confirmation_signal_decision' -> 'decision_source'))::text";
        return $$"""
        SELECT strategy_run.id, strategy_run.strategy_id, strategy.code,
               strategy_run.market_id, strategy_run.condition_id,
               strategy_run.entry_due_at_utc, strategy_run.status,
               strategy_run.selected_outcome, strategy_run.selected_asset_id,
               strategy_run.entry_price, strategy_run.stake_usd, strategy_run.size_shares,
               strategy_run.signal_id, strategy_run.paper_order_id,
               strategy_run.settlement_price, strategy_run.settlement_value_usd,
               strategy_run.realized_pnl_usd, strategy_run.settled_at_utc,
               paper_order.id, paper_order.signal_id, paper_order.strategy_id, paper_order.status,
               paper_order.side, paper_order.outcome, paper_order.asset_id, paper_order.condition_id,
               paper_order.copied_trader_wallet, paper_order.price, paper_order.size_shares,
               paper_order.notional_usd, paper_order.correlation_id, paper_order.execution_source,
               paper_order.created_at_utc, {{rawDecisionProjection}},
               signal.id, signal.outcome, signal.asset_id, signal.condition_id,
               signal.trader_wallet, signal.leader_price, signal.score, signal.accepted,
               signal.decision, signal.proposed_paper_price, signal.proposed_size_shares,
               signal.proposed_notional_usd, signal.created_at_utc,
               paper_order.expires_at_utc, paper_order.filled_at_utc, paper_order.cancelled_at_utc,
               upper(encode(sha256(convert_to(to_jsonb(strategy_run)::text, 'UTF8')), 'hex')) AS run_full_row_sha256,
               upper(encode(sha256(convert_to(to_jsonb(paper_order)::text, 'UTF8')), 'hex')) AS order_full_row_sha256,
               upper(encode(sha256(convert_to(to_jsonb(signal)::text, 'UTF8')), 'hex')) AS signal_full_row_sha256,
               strategy.name, strategy_run.market_slug, strategy_run.entered_at_utc,
               strategy_run.created_at_utc, strategy_run.updated_at_utc, strategy_run.market_end_utc,
               strategy_run.skip_reason, strategy_run.skip_diagnostics_json IS NULL,
               signal.leader_trade_id, signal.best_bid, signal.best_ask, signal.spread_abs,
               signal.spread_pct, signal.lag_seconds, signal.raw_context_json::text,
               strategy_run.category
        FROM strategy_market_paper_runs strategy_run
        JOIN strategies strategy ON strategy.id = strategy_run.strategy_id
        JOIN paper_orders paper_order ON paper_order.id = strategy_run.paper_order_id
        LEFT JOIN signals signal ON signal.id = strategy_run.signal_id
        """;
    }

    internal static string BuildMainOrdersSql() => BaseGraphOrderSql(fullRawDecision: false) + "\n" + """
        WHERE strategy_run.id = ANY(@run_ids)
        ORDER BY strategy_run.id;
        """;

    internal static string BuildChildOrdersSql() => BaseGraphOrderSql(fullRawDecision: true) + "\n" + """
        WHERE strategy_run.entry_due_at_utc < @cutoff_utc
          AND paper_order.raw_decision_json ?| @parent_keys
        ORDER BY strategy_run.entry_due_at_utc, strategy_run.id;
        """;

    private GraphOrder ReadGraphOrder(
        NpgsqlDataReader data,
        string scope,
        Guid? parentMainRunId,
        bool keepRawDecision,
        bool recordGraphMismatch,
        out bool graphIdentityValid)
    {
        var runId = data.GetGuid(0);
        var runStrategyId = data.GetGuid(1);
        var strategyCode = data.GetString(2);
        var marketId = data.GetString(3);
        var runConditionId = data.GetString(4);
        var entryDueAtUtc = ReadUtc(data, 5);
        var runStatus = data.GetString(6);
        var runOutcome = data.IsDBNull(7) ? null : data.GetString(7);
        var runAssetId = data.IsDBNull(8) ? null : data.GetString(8);
        decimal? entryPrice = data.IsDBNull(9) ? null : data.GetDecimal(9);
        var stakeUsd = data.GetDecimal(10);
        decimal? runSizeShares = data.IsDBNull(11) ? null : data.GetDecimal(11);
        var runSignalId = data.IsDBNull(12) ? (Guid?)null : data.GetGuid(12);
        var runOrderId = data.IsDBNull(13) ? (Guid?)null : data.GetGuid(13);
        decimal? settlementPrice = data.IsDBNull(14) ? null : data.GetDecimal(14);
        decimal? settlementValueUsd = data.IsDBNull(15) ? null : data.GetDecimal(15);
        decimal? runRealizedPnlUsd = data.IsDBNull(16) ? null : data.GetDecimal(16);
        DateTimeOffset? settledAtUtc = data.IsDBNull(17) ? null : ReadUtc(data, 17);
        var orderId = data.GetGuid(18);
        var orderSignalId = data.GetGuid(19);
        var orderStrategyId = data.GetGuid(20);
        var orderStatus = data.GetString(21);
        var orderSide = data.GetString(22);
        var orderOutcome = data.GetString(23);
        var orderAssetId = data.GetString(24);
        var orderConditionId = data.GetString(25);
        var copiedTraderWallet = data.GetString(26);
        var orderPrice = data.GetDecimal(27);
        var orderSizeShares = data.GetDecimal(28);
        var orderNotionalUsd = data.GetDecimal(29);
        var correlationId = data.IsDBNull(30) ? (Guid?)null : data.GetGuid(30);
        var executionSource = data.GetString(31);
        var orderCreatedAtUtc = ReadUtc(data, 32);
        var rawDecisionProjection = data.IsDBNull(33) ? null : data.GetString(33);
        var rawDecisionJson = keepRawDecision ? rawDecisionProjection : null;
        var rawDecisionProofSha256 = rawDecisionProjection is null
            ? string.Empty
            : CanonicalEvidence.HashRawDecisionProof(rawDecisionProjection);
        var orderExecutionModeProof = ReadJsonString(rawDecisionProjection, "order_execution_mode");
        var signalId = data.IsDBNull(34) ? (Guid?)null : data.GetGuid(34);
        var signalOutcome = data.IsDBNull(35) ? null : data.GetString(35);
        var signalAssetId = data.IsDBNull(36) ? null : data.GetString(36);
        var signalConditionId = data.IsDBNull(37) ? null : data.GetString(37);
        var signalTraderWallet = data.IsDBNull(38) ? null : data.GetString(38);
        decimal? signalLeaderPrice = data.IsDBNull(39) ? null : data.GetDecimal(39);
        int? signalScore = data.IsDBNull(40) ? null : data.GetInt32(40);
        bool? signalAccepted = data.IsDBNull(41) ? null : data.GetBoolean(41);
        var signalDecision = data.IsDBNull(42) ? null : data.GetString(42);
        decimal? signalProposedPaperPrice = data.IsDBNull(43) ? null : data.GetDecimal(43);
        decimal? signalProposedSizeShares = data.IsDBNull(44) ? null : data.GetDecimal(44);
        decimal? signalProposedNotionalUsd = data.IsDBNull(45) ? null : data.GetDecimal(45);
        DateTimeOffset? signalCreatedAtUtc = data.IsDBNull(46) ? null : ReadUtc(data, 46);
        var orderExpiresAtUtc = ReadUtc(data, 47);
        DateTimeOffset? orderFilledAtUtc = data.IsDBNull(48) ? null : ReadUtc(data, 48);
        DateTimeOffset? orderCancelledAtUtc = data.IsDBNull(49) ? null : ReadUtc(data, 49);
        var runFullRowSha256 = data.GetString(50);
        var orderFullRowSha256 = data.GetString(51);
        var signalFullRowSha256 = data.IsDBNull(52) ? string.Empty : data.GetString(52);
        var strategyName = data.GetString(53);
        var marketSlug = data.GetString(54);
        DateTimeOffset? runEnteredAtUtc = data.IsDBNull(55) ? null : ReadUtc(data, 55);
        var runCreatedAtUtc = ReadUtc(data, 56);
        var runUpdatedAtUtc = ReadUtc(data, 57);
        DateTimeOffset? marketEndUtc = data.IsDBNull(58) ? null : ReadUtc(data, 58);
        var runSkipReason = data.IsDBNull(59) ? null : data.GetString(59);
        var runSkipDiagnosticsIsNull = data.GetBoolean(60);
        Guid? signalLeaderTradeId = data.IsDBNull(61) ? null : data.GetGuid(61);
        decimal? signalBestBid = data.IsDBNull(62) ? null : data.GetDecimal(62);
        decimal? signalBestAsk = data.IsDBNull(63) ? null : data.GetDecimal(63);
        decimal? signalSpreadAbs = data.IsDBNull(64) ? null : data.GetDecimal(64);
        decimal? signalSpreadPct = data.IsDBNull(65) ? null : data.GetDecimal(65);
        int? signalLagSeconds = data.IsDBNull(66) ? null : data.GetInt32(66);
        var signalRawContextJson = data.IsDBNull(67) ? null : data.GetString(67);
        var runCategory = data.IsDBNull(68) ? null : data.GetString(68);
        var signalNullableShapeValid = signalLeaderTradeId is null && signalBestBid is null &&
            signalBestAsk is null && signalSpreadAbs is null && signalSpreadPct is null &&
            signalLagSeconds is null && signalRawContextJson is null;
        graphIdentityValid = runSignalId is not null && runOrderId is not null && signalId is not null &&
            runOrderId == orderId && runSignalId == orderSignalId && runSignalId == signalId &&
            runStrategyId == orderStrategyId &&
            string.Equals(runConditionId, orderConditionId, StringComparison.Ordinal) &&
            string.Equals(orderOutcome, signalOutcome, StringComparison.Ordinal) &&
            string.Equals(orderAssetId, signalAssetId, StringComparison.Ordinal) &&
            string.Equals(runConditionId, signalConditionId, StringComparison.Ordinal);
        if (!graphIdentityValid && recordGraphMismatch)
        {
            Error(scope.ToLowerInvariant(), runId.ToString("D"), "run_order_signal_graph_mismatch", "");
        }

        return new GraphOrder(
            scope,
            parentMainRunId,
            runId,
            runStrategyId,
            strategyCode,
            marketId,
            runConditionId,
            entryDueAtUtc,
            runStatus,
            runOutcome,
            runAssetId,
            entryPrice,
            stakeUsd,
            runSizeShares,
            settlementPrice,
            settlementValueUsd,
            runRealizedPnlUsd,
            settledAtUtc,
            orderId,
            orderSignalId,
            orderStatus,
            orderSide,
            orderOutcome,
            orderAssetId,
            copiedTraderWallet,
            orderPrice,
            orderSizeShares,
            orderNotionalUsd,
            correlationId,
            executionSource,
            orderCreatedAtUtc,
            runSignalId,
            runOrderId,
            orderStrategyId,
            signalId,
            signalOutcome,
            signalAssetId,
            signalConditionId,
            signalTraderWallet,
            signalLeaderPrice,
            signalScore,
            signalAccepted,
            signalDecision,
            signalProposedPaperPrice,
            signalProposedSizeShares,
            signalProposedNotionalUsd,
            signalCreatedAtUtc,
            orderExpiresAtUtc,
            orderFilledAtUtc,
            orderCancelledAtUtc,
            runFullRowSha256,
            orderFullRowSha256,
            signalFullRowSha256,
            strategyName,
            marketSlug,
            runCategory,
            runEnteredAtUtc,
            runCreatedAtUtc,
            runUpdatedAtUtc,
            marketEndUtc,
            runSkipReason,
            runSkipDiagnosticsIsNull,
            signalLeaderTradeId,
            signalBestBid,
            signalBestAsk,
            signalSpreadAbs,
            signalSpreadPct,
            signalLagSeconds,
            signalRawContextJson,
            signalNullableShapeValid,
            orderExecutionModeProof,
            rawDecisionProofSha256,
            rawDecisionJson);
    }

    private static string ReadJsonString(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(property, out var node) &&
                node.ValueKind == System.Text.Json.JsonValueKind.String
                    ? node.GetString() ?? string.Empty
                    : string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }
    }

    private NpgsqlCommand Command(string sql, [CallerMemberName] string caller = "")
    {
        var sqlHash = CanonicalEvidence.HashSql(sql);
        lastQueryStage = $"{caller}:{sqlHash[..12]}";
        return new NpgsqlCommand(
            $"/* graph_preview_query_stage={lastQueryStage} */\n{sql}",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = Command(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private void Error(string scope, string entityId, string code, string details)
    {
        invariantErrors.Add(new InvariantError(scope, entityId, code, details));
    }

    private sealed record LiveLink(
        Guid Id,
        Guid StrategyId,
        Guid SignalId,
        Guid? PaperOrderId,
        Guid? CorrelationId,
        string Status,
        string? ExchangeOrderId,
        string ResponseStatus,
        decimal FilledSize,
        decimal RemainingSize,
        DateTimeOffset? SettledAtUtc,
        string ExecutionSource);

    private sealed record ShadowLink(
        Guid CorrelationId,
        Guid StrategyId,
        Guid? SignalId,
        Guid? PaperOrderId,
        Guid? LiveOrderId,
        string Status,
        string Source);

    private sealed record DiscrepancyLink(
        Guid Id,
        Guid CorrelationId,
        Guid StrategyId,
        string Classification,
        string Severity,
        string Details);

    private sealed record RawChildOrderReference(
        Guid OrderId,
        Guid SignalId,
        Guid StrategyId,
        DateTimeOffset CreatedAtUtc,
        Guid ParentRunId);

}
