using System.Data;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace ReferenceAverageHistoryCorrectionPreview;

internal static class Program
{
    private const string RequiredHost = "192.168.0.101";
    private const string CatalogRelativePath = "Codex/Tasks/REFERENCE_AVERAGE_MAX_MIN_MIGRATION_2026-07-27.md";
    private const int StrategyBatchSize = 1;

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
            Console.Error.WriteLine("Preview cancelled. No database writes were issued.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Preview failed: {exception.Message}");
            Console.Error.WriteLine("No database writes were issued. Any *.partial files are incomplete.");
            return 1;
        }
    }

    private static async Task<int> RunAsync(Options options, CancellationToken cancellationToken)
    {
        var repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
        var catalogPath = Path.Combine(repositoryRoot, CatalogRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var strategies = CatalogParser.ParseAndValidate(catalogPath);
        var catalogSha256 = CatalogParser.ComputeSha256(catalogPath);
        PrepareOutputDirectory(options.OutputDirectory);

        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
        }

        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Host = RequiredHost,
            Pooling = false,
            Multiplexing = false,
            ApplicationName = "reference-average-history-correction-preview-read-only",
            Timeout = Math.Min(options.CommandTimeoutSeconds, 30),
            CommandTimeout = options.CommandTimeoutSeconds,
            IncludeErrorDetail = false
        };

        var stopwatch = Stopwatch.StartNew();
        var queryGroups = new List<QueryGroupResult>();
        DatabaseSnapshotMetadata? snapshotMetadata = null;
        var transactionRolledBack = false;
        IReadOnlyDictionary<ReplayAction, long> actionCounts;
        IReadOnlyList<OutputFileEvidence> outputEvidence;

        using (var output = new CsvOutput(options.OutputDirectory))
        await using (var connection = new NpgsqlConnection(connectionBuilder.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
            try
            {
                await ConfigureReadOnlyTransactionAsync(
                    connection,
                    transaction,
                    options.CommandTimeoutSeconds,
                    cancellationToken);
                snapshotMetadata = await ReadAndValidateSnapshotMetadataAsync(
                    connection,
                    transaction,
                    connectionBuilder,
                    cancellationToken);

                var strategyById = strategies.ToDictionary(item => item.Id);
                foreach (var group in strategies
                             .GroupBy(item => (item.Asset, item.Family))
                             .OrderBy(item => item.Key.Asset, StringComparer.Ordinal)
                             .ThenBy(item => item.Key.Family))
                {
                    var ids = group.Select(item => item.Id).Order().ToArray();
                    long rowCount = 0;
                    foreach (var batch in ids.Chunk(StrategyBatchSize))
                    {
                        rowCount += await StreamExistingEntriesAsync(
                            connection,
                            transaction,
                            batch,
                            group.Key.Family,
                            strategyById,
                            options,
                            output,
                            cancellationToken);
                    }
                    queryGroups.Add(new QueryGroupResult(
                        "existing_settled_up_entry",
                        group.Key.Asset,
                        group.Key.Family,
                        ids.Length,
                        rowCount));
                    Console.WriteLine($"Existing {group.Key.Asset}/{group.Key.Family}: {rowCount:N0}");
                }

                foreach (var group in strategies
                             .Where(item =>
                                 item.Family == StrategyFamily.OptimizedReferenceAverage &&
                                 item.Trigger is StrategyTrigger.Down or StrategyTrigger.Neutral)
                             .GroupBy(item => item.Asset)
                             .OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    var ids = group.Select(item => item.Id).Order().ToArray();
                    long rowCount = 0;
                    foreach (var batch in ids.Chunk(StrategyBatchSize))
                    {
                        var optimizedStrategy = strategyById[batch[0]];
                        var ordinaryReferenceStrategy = ResolveOrdinaryReferenceEvidenceStrategy(
                            optimizedStrategy,
                            strategies);
                        rowCount += await StreamPotentialAddsAsync(
                            connection,
                            transaction,
                            batch,
                            ordinaryReferenceStrategy.Id,
                            strategyById,
                            options,
                            output,
                            cancellationToken);
                    }
                    queryGroups.Add(new QueryGroupResult(
                        "potential_add_optimized_legacy_skip",
                        group.Key,
                        StrategyFamily.OptimizedReferenceAverage,
                        ids.Length,
                        rowCount));
                    Console.WriteLine($"Potential add {group.Key}/Optimized: {rowCount:N0}");
                }

                await transaction.RollbackAsync(cancellationToken);
                transactionRolledBack = true;
            }
            finally
            {
                if (!transactionRolledBack)
                {
                    try
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                        transactionRolledBack = true;
                    }
                    catch
                    {
                        // The connection disposal remains the final rollback boundary.
                    }
                }
            }

            actionCounts = output.ActionCounts;
            outputEvidence = await output.CompleteAsync(strategies, cancellationToken);
        }

        stopwatch.Stop();
        var invariantCount = actionCounts.GetValueOrDefault(ReplayAction.InvariantError);
        var manifest = new
        {
            schema_version = 1,
            tool = "reference-average-history-correction-preview",
            generated_at_utc = DateTimeOffset.UtcNow,
            elapsed_seconds = stopwatch.Elapsed.TotalSeconds,
            cutoff_utc = options.CutoffUtc,
            catalog = new
            {
                path = CatalogRelativePath,
                sha256 = catalogSha256,
                strategy_count = strategies.Count,
                potential_add_strategy_count = CatalogParser.ExpectedPotentialAddStrategyCount,
                strategy_batch_size = StrategyBatchSize,
                asset_counts = strategies.GroupBy(item => item.Asset)
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .ToDictionary(item => item.Key, item => item.Count()),
                family_counts = strategies.GroupBy(item => item.Family)
                    .OrderBy(item => item.Key)
                    .ToDictionary(item => item.Key.ToString(), item => item.Count()),
                location_counts = strategies.GroupBy(item => item.Location)
                    .OrderBy(item => item.Key)
                    .ToDictionary(item => item.Key.ToString(), item => item.Count())
            },
            database_snapshot = snapshotMetadata,
            safety = new
            {
                host_parameter = options.Host,
                required_host = RequiredHost,
                transaction_isolation = "repeatable read",
                transaction_read_only = true,
                transaction_time_zone = "UTC",
                transaction_rolled_back = transactionRolledBack,
                database_write_statements_issued = 0,
                safe_to_continue_to_mutation_design = invariantCount == 0 && transactionRolledBack
            },
            action_counts = actionCounts.OrderBy(item => item.Key)
                .ToDictionary(item => item.Key.ToString(), item => item.Value),
            invariant_error_count = invariantCount,
            query_groups = queryGroups,
            files = outputEvidence,
            limitations = new[]
            {
                "Read-only preview only; it does not mutate PostgreSQL.",
                "ChildMirror and other dependency expansion are outside this preview.",
                "Adds are exact signal replays but use modeled fills: 0.50 LowerEnter, 0.52 regular.",
                "Stake progression, settlement/PnL, positions, Live links, and deletion dependencies are not computed.",
                "Only exact legacy optimized skip_reason=optimized_average_required_window_not_selected is considered for adds.",
                "Missing optimized skip diagnostics are replayed only from the exact ordinary Reference Average strategy with the same asset, trigger, threshold, and market; its complete settled order graph and recomputed legacy outcome are required.",
                "A fallback ordinary snapshot cannot supply the target optimized strategy's historical stake multiplier; any fallback row that would become Add remains Unreplayable.",
                "The optimized 3h requirement may be recovered only from the two exact audited 2026-07-25 backfill markers embedded in the target order JSON.",
                "Existing Down bets are mathematically unchanged by the Max/Min migration and are not emitted row-by-row.",
                "Only the compact Reference decision node is streamed; unrelated FAK/order-book JSON is neither transferred nor copied."
            }
        };
        var manifestPath = Path.Combine(options.OutputDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                Converters = { new JsonStringEnumConverter() }
            }) + Environment.NewLine,
            cancellationToken);

        Console.WriteLine($"Manifest: {manifestPath}");
        if (invariantCount > 0)
        {
            Console.Error.WriteLine($"FAIL CLOSED: {invariantCount:N0} invariant errors were emitted.");
            return 3;
        }

        if (!transactionRolledBack)
        {
            Console.Error.WriteLine("FAIL CLOSED: an explicit transaction rollback was not confirmed.");
            return 4;
        }

        return 0;
    }

    private static async Task<long> StreamExistingEntriesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid[] strategyIds,
        StrategyFamily family,
        IReadOnlyDictionary<Guid, StrategyDefinition> strategyById,
        Options options,
        CsvOutput output,
        CancellationToken cancellationToken)
    {
        var compactDecisionJson = BuildCompactReferenceDecisionSql(family);
        var sql = $"""
            SELECT run.id,
                   run.strategy_id,
                   run.market_id,
                   run.entry_due_at_utc,
                   run.settled_at_utc,
                   run.selected_outcome,
                   run.paper_order_id,
                   paper_order.id,
                   paper_order.strategy_id,
                   paper_order.outcome,
                   EXISTS (
                       SELECT 1
                       FROM paper_fills fill_row
                       WHERE fill_row.paper_order_id = run.paper_order_id
                         AND fill_row.price > 0
                         AND fill_row.size_shares > 0
                   ) AS has_positive_fill,
                   {compactDecisionJson}
            FROM strategy_market_paper_runs run
            LEFT JOIN paper_orders paper_order ON paper_order.id = run.paper_order_id
            WHERE run.strategy_id = @strategy_id
              AND run.status = 'Settled'
              AND run.selected_outcome = 'Up'
              AND run.entry_due_at_utc < @cutoff_utc
            ORDER BY run.entry_due_at_utc;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = options.CommandTimeoutSeconds
        };
        if (strategyIds.Length != 1)
        {
            throw new InvalidOperationException("Existing-entry query requires exactly one strategy ID.");
        }

        command.Parameters.AddWithValue("strategy_id", NpgsqlDbType.Uuid, strategyIds[0]);
        command.Parameters.AddWithValue("cutoff_utc", NpgsqlDbType.TimestampTz, options.CutoffUtc.UtcDateTime);
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess | CommandBehavior.SingleResult,
            cancellationToken);
        long count = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new SourceRow(
                "existing_settled_up_entry",
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                ReadUtc(reader, 3),
                reader.IsDBNull(4) ? null : ReadUtc(reader, 4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetBoolean(10),
                reader.IsDBNull(11) ? null : reader.GetString(11));
            var strategy = strategyById[row.StrategyId];
            var decision = ValidateExistingGraph(row) ?? ReplayClassifier.ClassifyExistingEntry(
                strategy,
                row.RunOutcome,
                row.OrderOutcome,
                row.RawDecisionJson);
            output.Write(strategy, row, decision);
            count++;
        }

        return count;
    }

    private static async Task<long> StreamPotentialAddsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid[] strategyIds,
        Guid ordinaryReferenceStrategyId,
        IReadOnlyDictionary<Guid, StrategyDefinition> strategyById,
        Options options,
        CsvOutput output,
        CancellationToken cancellationToken)
    {
        var evidenceDecisionJson = BuildCompactReferenceDecisionSql(
            StrategyFamily.ReferenceAverage,
            "evidence_order");
        var sql = $"""
            SELECT run.id,
                   run.strategy_id,
                   run.market_id,
                   run.entry_due_at_utc,
                   run.selected_outcome,
                   run.paper_order_id,
                   run.skip_diagnostics_json::text,
                   evidence_run.id,
                   evidence_run.entry_due_at_utc,
                   evidence_run.status,
                   evidence_run.selected_outcome,
                   evidence_order.id,
                   evidence_order.strategy_id,
                   evidence_order.outcome,
                   EXISTS (
                       SELECT 1
                       FROM paper_fills evidence_fill
                       WHERE evidence_fill.paper_order_id = evidence_run.paper_order_id
                         AND evidence_fill.price > 0
                         AND evidence_fill.size_shares > 0
                   ) AS evidence_has_positive_fill,
                   {evidenceDecisionJson}
            FROM strategy_market_paper_runs run
            LEFT JOIN strategy_market_paper_runs evidence_run
              ON evidence_run.strategy_id = @ordinary_reference_strategy_id
             AND evidence_run.market_id = run.market_id
            LEFT JOIN paper_orders evidence_order ON evidence_order.id = evidence_run.paper_order_id
            WHERE run.strategy_id = @strategy_id
              AND run.status = 'Skipped'
              AND run.entry_due_at_utc < @cutoff_utc
              AND run.skip_reason = @skip_reason
            ORDER BY run.entry_due_at_utc;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = options.CommandTimeoutSeconds
        };
        if (strategyIds.Length != 1)
        {
            throw new InvalidOperationException("Potential-add query requires exactly one strategy ID.");
        }

        command.Parameters.AddWithValue("strategy_id", NpgsqlDbType.Uuid, strategyIds[0]);
        command.Parameters.AddWithValue(
            "ordinary_reference_strategy_id",
            NpgsqlDbType.Uuid,
            ordinaryReferenceStrategyId);
        command.Parameters.AddWithValue("cutoff_utc", NpgsqlDbType.TimestampTz, options.CutoffUtc.UtcDateTime);
        command.Parameters.AddWithValue("skip_reason", ReplayClassifier.PotentialAddSkipReason);
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess | CommandBehavior.SingleResult,
            cancellationToken);
        long count = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new SourceRow(
                "potential_add_optimized_legacy_skip",
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                ReadUtc(reader, 3),
                null,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                null,
                null,
                null,
                false,
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : ReadUtc(reader, 8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11),
                reader.IsDBNull(12) ? null : reader.GetGuid(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetBoolean(14),
                reader.IsDBNull(15) ? null : reader.GetString(15));
            var strategy = strategyById[row.StrategyId];
            ReplayDecision decision;
            if (row.PaperOrderId is not null)
            {
                decision = ReplayDecision.InvariantError("potential_add_skip_has_paper_order");
            }
            else if (!string.IsNullOrWhiteSpace(row.RawDecisionJson))
            {
                decision = ReplayClassifier.ClassifyPotentialAdd(
                    strategy,
                    ReplayClassifier.PotentialAddSkipReason,
                    row.RawDecisionJson);
            }
            else
            {
                decision = ValidatePotentialEvidenceGraph(row, ordinaryReferenceStrategyId) ??
                    ReplayClassifier.ClassifyPotentialAddFromOrdinaryReferenceEvidence(
                        strategy,
                        ReplayClassifier.PotentialAddSkipReason,
                        row.EvidenceOrderOutcome,
                        row.EvidenceRawDecisionJson);
            }
            output.Write(strategy, row, decision);
            count++;
        }

        return count;
    }

    private static StrategyDefinition ResolveOrdinaryReferenceEvidenceStrategy(
        StrategyDefinition optimizedStrategy,
        IReadOnlyList<StrategyDefinition> strategies)
    {
        var matches = strategies.Where(item =>
                item.Asset == optimizedStrategy.Asset &&
                item.Family == StrategyFamily.ReferenceAverage &&
                item.Location == StrategyLocation.Direct &&
                string.Equals(item.Kind, "Base", StringComparison.Ordinal) &&
                item.Trigger == optimizedStrategy.Trigger &&
                item.CatalogThresholdBps == optimizedStrategy.CatalogThresholdBps &&
                item.ReferenceThresholdBps == optimizedStrategy.ReferenceThresholdBps)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one ordinary Reference Average evidence strategy for {optimizedStrategy.Code}; found {matches.Length}.");
        }

        return matches[0];
    }

    private static ReplayDecision? ValidatePotentialEvidenceGraph(
        SourceRow row,
        Guid expectedStrategyId)
    {
        if (row.EvidenceRunId is null || row.EvidenceOrderId is null ||
            row.EvidenceOrderStrategyId is null)
        {
            return ReplayDecision.Unreplayable("ordinary_reference_evidence_graph_missing");
        }

        if (row.EvidenceEntryDueAtUtc != row.EntryDueAtUtc)
        {
            return ReplayDecision.InvariantError("ordinary_reference_evidence_entry_due_mismatch");
        }

        if (!string.Equals(row.EvidenceRunStatus, "Settled", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(row.EvidenceRunOutcome) ||
            string.IsNullOrWhiteSpace(row.EvidenceOrderOutcome))
        {
            return ReplayDecision.InvariantError("ordinary_reference_evidence_not_settled");
        }

        if (!string.Equals(
                row.EvidenceRunOutcome,
                row.EvidenceOrderOutcome,
                StringComparison.OrdinalIgnoreCase))
        {
            return ReplayDecision.InvariantError("ordinary_reference_evidence_outcome_mismatch");
        }

        if (row.EvidenceOrderStrategyId != expectedStrategyId)
        {
            return ReplayDecision.InvariantError("ordinary_reference_evidence_strategy_mismatch");
        }

        if (!row.EvidenceHasPositiveFill)
        {
            return ReplayDecision.InvariantError("ordinary_reference_evidence_has_no_positive_fill");
        }

        if (string.IsNullOrWhiteSpace(row.EvidenceRawDecisionJson))
        {
            return ReplayDecision.Unreplayable("ordinary_reference_evidence_json_missing");
        }

        return null;
    }

    private static ReplayDecision? ValidateExistingGraph(SourceRow row)
    {
        if (row.SettledAtUtc is null)
        {
            return ReplayDecision.InvariantError("settled_run_missing_settled_at_utc");
        }

        if (row.PaperOrderId is null)
        {
            return ReplayDecision.InvariantError("settled_run_missing_paper_order_id");
        }

        if (row.JoinedPaperOrderId is null || row.PaperOrderStrategyId is null)
        {
            return ReplayDecision.InvariantError("settled_run_paper_order_not_joined");
        }

        if (row.PaperOrderId != row.JoinedPaperOrderId)
        {
            return ReplayDecision.InvariantError("joined_paper_order_id_mismatch");
        }

        if (row.StrategyId != row.PaperOrderStrategyId)
        {
            return ReplayDecision.InvariantError("paper_order_strategy_id_mismatch");
        }

        if (!row.HasPositiveFill)
        {
            return ReplayDecision.InvariantError("settled_run_has_no_positive_fill");
        }

        return null;
    }

    private static string BuildCompactReferenceDecisionSql(
        StrategyFamily family,
        string orderAlias = "paper_order")
    {
        var nestedProperty = family switch
        {
            StrategyFamily.BpsConfirmedAverage => "base_signal_decision",
            StrategyFamily.DiffConfirmedAverage => "confirmation_signal_decision",
            _ => null
        };
        var node = nestedProperty is null
            ? $"{orderAlias}.raw_decision_json"
            : $"{orderAlias}.raw_decision_json -> '{nestedProperty}'";
        var compactNode = $"""
            jsonb_build_object(
                'decision_source', {node} -> 'decision_source',
                'reference_asset_symbol', {node} -> 'reference_asset_symbol',
                'selected_reference_average_window', {node} -> 'selected_reference_average_window',
                'current_price_usd', {node} -> 'current_price_usd',
                'reference_average_min_move_from_middle_bps', {node} -> 'reference_average_min_move_from_middle_bps',
                'optimized_average_required_window', {node} -> 'optimized_average_required_window',
                'reference_averages', {node} -> 'reference_averages',
                'target_notional_usd', {node} -> 'target_notional_usd',
                'codex_backfill', {node} -> 'codex_backfill',
                'history_backfill', {node} -> 'history_backfill')
            """;
        var projected = nestedProperty is null
            ? compactNode
            : $"jsonb_build_object('{nestedProperty}', {compactNode})";
        return $"CASE WHEN {node} IS NULL THEN NULL ELSE {projected}::text END";
    }

    private static async Task ConfigureReadOnlyTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using (var readOnlyCommand = new NpgsqlCommand("SET TRANSACTION READ ONLY;", connection, transaction))
        {
            readOnlyCommand.CommandTimeout = commandTimeoutSeconds;
            await readOnlyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var settingsCommand = new NpgsqlCommand(
            "SELECT set_config('TimeZone', 'UTC', true), set_config('statement_timeout', @timeout, true);",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        settingsCommand.Parameters.AddWithValue("timeout", $"{commandTimeoutSeconds}s");
        await settingsCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<DatabaseSnapshotMetadata> ReadAndValidateSnapshotMetadataAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        NpgsqlConnectionStringBuilder connectionBuilder,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT current_setting('transaction_isolation'),
                   current_setting('transaction_read_only'),
                   current_setting('TimeZone'),
                   host(inet_server_addr());
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Database snapshot metadata query returned no row.");
        }

        var isolation = reader.GetString(0);
        var readOnly = string.Equals(reader.GetString(1), "on", StringComparison.OrdinalIgnoreCase);
        var timeZone = reader.GetString(2);
        var serverAddress = reader.GetString(3);
        if (!string.Equals(isolation, "repeatable read", StringComparison.OrdinalIgnoreCase) ||
            !readOnly ||
            !string.Equals(timeZone, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsafe transaction settings: isolation={isolation}, read_only={readOnly}, timezone={timeZone}.");
        }

        if (!IPAddress.TryParse(serverAddress, out var actualAddress) ||
            !IPAddress.TryParse(RequiredHost, out var requiredAddress) ||
            !actualAddress.MapToIPv4().Equals(requiredAddress.MapToIPv4()))
        {
            throw new InvalidOperationException(
                $"Connected server address {serverAddress} is not the required host {RequiredHost}.");
        }

        return new DatabaseSnapshotMetadata(
            connectionBuilder.Host ?? throw new InvalidOperationException("Connection host is missing."),
            connectionBuilder.Port,
            connectionBuilder.Database ?? string.Empty,
            serverAddress,
            connection.PostgreSqlVersion.ToString(),
            isolation,
            readOnly,
            timeZone);
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static string FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, CatalogRelativePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find repository root containing {CatalogRelativePath} from {startPath}.");
    }

    private static void PrepareOutputDirectory(string outputDirectory)
    {
        var root = Path.GetFullPath(@"D:\CodexTemp").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"--output-dir must resolve below {root}");
        }

        if (Directory.Exists(resolved) && Directory.EnumerateFileSystemEntries(resolved).Any())
        {
            throw new IOException($"Output directory must be empty: {resolved}");
        }

        Directory.CreateDirectory(resolved);
    }

    private sealed record Options(
        string Host,
        DateTimeOffset CutoffUtc,
        string OutputDirectory,
        int CommandTimeoutSeconds,
        bool ShowHelp)
    {
        public const string HelpText = """
            Read-only Reference Average history correction preview.

            Required:
              --host 192.168.0.101
              --cutoff <UTC ISO-8601 instant>
              --output-dir <empty path below D:\CodexTemp>

            Optional:
              --command-timeout-seconds <1..600>   Default: 120

            Connection credentials come only from POLYCOPYTRADER_POSTGRES_CONNECTION;
            its Host is always overridden with 192.168.0.101. The transaction is
            REPEATABLE READ, READ ONLY, UTC and is always rolled back.
            """;

        public static Options Parse(string[] args)
        {
            if (args.Length == 1 && args[0] is "--help" or "-h")
            {
                return new Options(string.Empty, default, string.Empty, 120, true);
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Arguments must be supplied as --name value pairs. Use --help.");
                }

                if (!values.TryAdd(args[index], args[index + 1]))
                {
                    throw new ArgumentException($"Duplicate argument: {args[index]}");
                }
            }

            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "--host", "--cutoff", "--output-dir", "--command-timeout-seconds"
            };
            var unknown = values.Keys.FirstOrDefault(key => !allowed.Contains(key));
            if (unknown is not null)
            {
                throw new ArgumentException($"Unknown argument: {unknown}");
            }

            var host = Require(values, "--host");
            if (!string.Equals(host, RequiredHost, StringComparison.Ordinal))
            {
                throw new ArgumentException($"--host must be exactly {RequiredHost}.");
            }

            var cutoffRaw = Require(values, "--cutoff");
            if (!DateTimeOffset.TryParse(
                    cutoffRaw,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var cutoff) ||
                cutoff.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("--cutoff must be an ISO-8601 UTC instant with Z or +00:00.");
            }

            var outputDirectory = Path.GetFullPath(Require(values, "--output-dir"));
            var timeout = 120;
            if (values.TryGetValue("--command-timeout-seconds", out var timeoutRaw) &&
                (!int.TryParse(timeoutRaw, out timeout) || timeout is < 1 or > 600))
            {
                throw new ArgumentException("--command-timeout-seconds must be between 1 and 600.");
            }

            return new Options(host, cutoff.ToUniversalTime(), outputDirectory, timeout, false);
        }

        private static string Require(IReadOnlyDictionary<string, string> values, string name) =>
            values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException($"Missing required argument {name}.");
    }
}
