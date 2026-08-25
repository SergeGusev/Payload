using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Startup;

public static class EthLossDiffHistoryBackfillCommand
{
    public const string CommandFlag = "--backfill-eth-lossdiff-history";
    public const string ApplyFlag = "--apply";
    public const string ApprovalDigest = "sha256:a7837d3ea4858bea6d705b244c3d5863468d28d1d3041126ded5307abc524e14";
    public const string SourceDigest = "sha256:c5f2baabe698c05eb0d8e4f6c98571390a07e858c1ac3c6a373bc7ead509bf01";
    public const string FullSourceDigest = "sha256:10420947c2f2bc78475421463da9457304d39c089c093357376f919651f578fc";
    public const string MarkerKey = "20260823_eth_lossdiff_history_backfill_v1";
    public const string EvidenceVersion = "eth_lossdiff_parent_mirror_history_v1";
    public const string CommandBuild = "eth_lossdiff_history_backfill_v1+a7837d3e";
    public const string DeployedServiceVersion =
        "info=1.0.0+99f57d2d4bb04813a83d355f034b68b5ca40de18; assembly=1.0.0.0; mvid=e50ea26bd2c8";

    internal static readonly Guid ParentStrategyId = Guid.Parse("b7c50005-0000-4000-8204-000000000001");
    internal static readonly DateTimeOffset CutoffUtc = DateTimeOffset.Parse(
        "2026-08-23T19:44:26.488143Z",
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal);

    internal static readonly ChildDefinition[] Children =
    [
        new(
            Guid.Parse("b7c50005-0000-4000-8225-000000000004"),
            "eth_up_down_5m_1_diff_confirmed_average_premarket_lossdiff_4_plus",
            "ETH 5m 1 Diff Confirmed Average Premarket LossDiff 4+",
            "LossDiffReset",
            4,
            22,
            16,
            6,
            132.11160001m,
            45.64509784m,
            4.20526000m,
            41.43983784m,
            10),
        new(
            Guid.Parse("b7c50005-0000-4000-8225-000000000013"),
            "eth_up_down_5m_1_diff_confirmed_average_premarket_lossdiff_13_plus_positive",
            "ETH 5m 1 Diff Confirmed Average Premarket LossDiff 13+ Positive",
            "LossDiffPositive",
            13,
            30,
            21,
            9,
            179.99999996m,
            51.39379117m,
            5.78854000m,
            45.60525117m,
            30)
    ];

    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        var argumentProblem = ValidateArguments(args);
        if (argumentProblem is not null)
        {
            await output.WriteLineAsync("Command refused: " + argumentProblem);
            return 1;
        }

        if (!StorageConnectionResolver.IsConfigured(configuration.Storage))
        {
            await output.WriteLineAsync("PostgreSQL storage is not configured.");
            return 1;
        }

        var apply = args.Contains(ApplyFlag, StringComparer.OrdinalIgnoreCase);
        var suppliedApproval = GetOption(args, "--approved-contract-digest");
        if (apply && !string.Equals(suppliedApproval, ApprovalDigest, StringComparison.Ordinal))
        {
            await output.WriteLineAsync($"Apply refused: --approved-contract-digest must equal {ApprovalDigest}.");
            return 1;
        }

        var factory = new PostgresConnectionFactory(
            configuration.Storage,
            apply ? "eth_lossdiff_history_backfill_apply" : "eth_lossdiff_history_backfill_preview");
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var builder = new NpgsqlConnectionStringBuilder(factory.ConnectionString);
        var isolation = apply
            ? System.Data.IsolationLevel.Serializable
            : System.Data.IsolationLevel.RepeatableRead;
        await using var transaction = await connection.BeginTransactionAsync(isolation, cancellationToken);
        if (!apply)
        {
            await ExecuteNonQueryAsync(connection, transaction, "SET TRANSACTION READ ONLY;", cancellationToken);
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "SET LOCAL TIME ZONE 'UTC'; SET LOCAL lock_timeout = '3s'; SET LOCAL statement_timeout = '15s';",
            cancellationToken);

        if (apply)
        {
            var locked = await ExecuteScalarAsync<bool>(
                connection,
                transaction,
                "SELECT pg_try_advisory_xact_lock(8225202608230001);",
                cancellationToken);
            if (!locked)
            {
                await output.WriteLineAsync("Apply refused: dedicated backfill advisory lock is held.");
                await transaction.RollbackAsync(cancellationToken);
                return 1;
            }
        }

        var source = await ReadSourceAsync(connection, transaction, cancellationToken);
        var plan = BuildPlan(source);
        var snapshot = await ReadSnapshotAsync(connection, transaction, builder, plan, cancellationToken);
        var problems = ValidateSnapshot(snapshot, source, plan, apply);

        await WritePreviewAsync(output, snapshot, source, plan, problems, apply);
        if (problems.Count != 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return 1;
        }

        if (!apply)
        {
            await transaction.RollbackAsync(cancellationToken);
            await output.WriteLineAsync("PREVIEW_OK: read-only transaction rolled back; writes=0.");
            return 0;
        }

        var inserted = await ExecuteValidatedApplyBranchAsync(
            connection,
            transaction,
            plan,
            snapshot.MarkerDetails,
            MarkerKey,
            cancellationToken);
        if (!inserted)
        {
            await transaction.RollbackAsync(cancellationToken);
            await output.WriteLineAsync("IDEMPOTENT_OK: matching complete marker and exact target history verified; writes=0.");
            return 0;
        }

        var postSnapshot = await ReadSnapshotAsync(connection, transaction, builder, plan, cancellationToken);
        var postProblems = ValidateSnapshot(postSnapshot, source, plan, apply: true);
        if (!string.Equals(snapshot.InvariantDigest, postSnapshot.InvariantDigest, StringComparison.Ordinal))
        {
            postProblems.Add("state/settings/Live invariant digest changed inside backfill transaction");
        }
        if (postProblems.Count != 0 || postSnapshot.MarkerDetails is null)
        {
            throw new InvalidOperationException("Post-insert verification failed: " + string.Join("; ", postProblems));
        }

        await transaction.CommitAsync(cancellationToken);
        await output.WriteLineAsync("APPLY_OK: exactly 22 Reset and 30 Positive complete Paper chains plus one marker committed.");
        return 0;
    }

    internal static async Task<bool> ExecuteValidatedApplyBranchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<PlannedEntry> plan,
        string? markerDetails,
        string markerKey,
        CancellationToken cancellationToken)
    {
        if (markerDetails is not null)
        {
            if (!MarkerMatches(markerDetails, plan))
            {
                throw new InvalidOperationException("Validated apply branch received mismatched marker details.");
            }

            var exactChainCount = await ReadExactTargetChainCountAsync(
                connection,
                transaction,
                plan,
                cancellationToken);
            var targetIdCount = await ReadTargetIdCollisionCountAsync(
                connection,
                transaction,
                plan,
                cancellationToken);
            if (exactChainCount != plan.Count || targetIdCount != plan.Count * 6L)
            {
                throw new InvalidOperationException(
                    $"Validated apply branch received incomplete target history: exact_chains={exactChainCount}/{plan.Count}; target_ids={targetIdCount}/{plan.Count * 6L}.");
            }

            return false;
        }

        await ApplyPlanAndMarkerAsync(connection, transaction, plan, markerKey, cancellationToken);
        return true;
    }

    internal static async Task ApplyPlanAndMarkerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<PlannedEntry> plan,
        string markerKey,
        CancellationToken cancellationToken)
    {
        foreach (var entry in plan)
        {
            await InsertEntryAsync(connection, transaction, entry, cancellationToken);
        }

        await using var marker = new NpgsqlCommand(
            "INSERT INTO public.schema_data_migrations (migration_key, applied_at_utc, details) VALUES (@Key, clock_timestamp(), @Details);",
            connection,
            transaction);
        marker.Parameters.AddWithValue("Key", markerKey);
        marker.Parameters.AddWithValue("Details", BuildMarkerDetails(plan));
        if (await marker.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Backfill marker insert did not affect exactly one row.");
        }
    }

    internal static IReadOnlyList<PlannedEntry> BuildPlan(IReadOnlyList<SourceRow> source)
    {
        var result = new List<PlannedEntry>();
        foreach (var child in Children)
        {
            foreach (var candidate in source)
            {
                var value = 0;
                foreach (var prior in source)
                {
                    if (prior.SettledMicros >= candidate.EnteredMicros)
                    {
                        continue;
                    }

                    if (child.Mode == "LossDiffReset")
                    {
                        value = prior.Won ? 0 : value + 1;
                    }
                    else
                    {
                        value = prior.Won ? Math.Max(0, value - 1) : value + 1;
                    }
                }

                if (value >= child.Threshold)
                {
                    result.Add(new PlannedEntry(child, candidate, value));
                }
            }
        }

        return result;
    }

    internal static string ComputeSourceDigest(IEnumerable<string> canonicalLines)
    {
        var canonical = string.Join('\n', canonicalLines) + "\n";
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static Guid DeterministicId(Guid childId, Guid parentRunId, string role)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{EvidenceVersion}|{childId:D}|{parentRunId:D}|{role}"));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes.AsSpan(0, 16), bigEndian: true);
    }

    internal static string? ValidateArguments(string[] args)
    {
        var commandCount = 0;
        var applyCount = 0;
        var approvalCount = 0;
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], CommandFlag, StringComparison.OrdinalIgnoreCase))
            {
                commandCount++;
                continue;
            }

            if (string.Equals(args[index], ApplyFlag, StringComparison.OrdinalIgnoreCase))
            {
                applyCount++;
                continue;
            }

            if (string.Equals(args[index], "--approved-contract-digest", StringComparison.OrdinalIgnoreCase))
            {
                approvalCount++;
                if (++index >= args.Length || !string.Equals(args[index], ApprovalDigest, StringComparison.Ordinal))
                {
                    return $"--approved-contract-digest must be followed by {ApprovalDigest}.";
                }

                continue;
            }

            return $"argument '{args[index]}' is outside the exact backfill allowlist.";
        }

        if (commandCount != 1 || applyCount > 1 || approvalCount > 1)
        {
            return $"invalid argument multiplicity (command={commandCount}, apply={applyCount}, approval={approvalCount}).";
        }

        if (applyCount == 1 && approvalCount != 1)
        {
            return "apply requires the exact approved contract digest.";
        }

        if (applyCount == 0 && approvalCount != 0)
        {
            return "preview does not accept an approval digest.";
        }

        return null;
    }

    private static async Task<IReadOnlyList<SourceRow>> ReadSourceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT array_to_string(ARRAY[
    r.id::text,
    round(extract(epoch from r.entered_at_utc) * 1000000)::bigint::text,
    round(extract(epoch from r.settled_at_utc) * 1000000)::bigint::text,
    CASE WHEN r.realized_pnl_usd > 0 THEN '1' ELSE '0' END,
    r.stake_usd::text,
    r.realized_pnl_usd::text,
    r.fee_usd::text,
    r.net_realized_pnl_usd::text,
    r.signal_id::text,
    r.paper_order_id::text,
    o.asset_id,
    o.outcome,
    o.price::text,
    o.size_shares::text,
    o.notional_usd::text,
    o.execution_source,
    CASE WHEN o.raw_decision_json ? 'execution_intent_order_book_snapshot' THEN '1' ELSE '0' END,
    f.id::text,
    f.price::text,
    f.size_shares::text,
    f.fee_usd::text,
    f.net_realized_pnl_usd::text], '|', ''),
    jsonb_build_object(
        'signal', to_jsonb(s),
        'order', to_jsonb(o),
        'fill', to_jsonb(f),
        'run', to_jsonb(r),
        'position', to_jsonb(p),
        'settlement', to_jsonb(ps))::text,
    p.id,
    ps.id
FROM public.strategy_market_paper_runs r
INNER JOIN public.paper_orders o ON o.id = r.paper_order_id
INNER JOIN public.signals s ON s.id = r.signal_id AND s.id = o.signal_id
INNER JOIN public.paper_fills f ON f.paper_order_id = o.id
INNER JOIN public.paper_positions p
    ON p.copied_trader_wallet = o.copied_trader_wallet AND p.asset_id = o.asset_id
INNER JOIN public.paper_position_settlements ps
    ON ps.copied_trader_wallet = o.copied_trader_wallet AND ps.asset_id = o.asset_id
WHERE r.strategy_id = @ParentId
  AND r.entered_at_utc < @CutoffUtc
  AND r.status = 'Settled'
  AND r.realized_pnl_usd IS NOT NULL
  AND r.realized_pnl_usd <> 0
  AND (SELECT count(*) FROM public.paper_fills one_fill WHERE one_fill.paper_order_id = o.id) = 1
  AND (SELECT count(*) FROM public.paper_positions one_position
       WHERE one_position.copied_trader_wallet = o.copied_trader_wallet
         AND one_position.asset_id = o.asset_id) = 1
  AND (SELECT count(*) FROM public.paper_position_settlements one_settlement
       WHERE one_settlement.copied_trader_wallet = o.copied_trader_wallet
         AND one_settlement.asset_id = o.asset_id) = 1
  AND o.status = 'Filled' AND o.side = 'Buy' AND o.execution_source = 'btc_updown5m_fak_taker_paper'
  AND p.size_shares = 0
  AND r.fee_accounting_status = 'Calculated'
  AND f.fee_accounting_status = 'Calculated'
  AND ps.fee_accounting_status = 'Calculated'
  AND f.realized_pnl_usd = 0 AND f.net_realized_pnl_usd IS NULL
  AND r.net_realized_pnl_usd = r.realized_pnl_usd - r.fee_usd
  AND ps.realized_pnl_usd = r.realized_pnl_usd
  AND ps.fee_usd = r.fee_usd
  AND ps.net_realized_pnl_usd = r.net_realized_pnl_usd
  AND ps.settlement_value_usd = r.settlement_value_usd
  AND ps.condition_id = r.condition_id
  AND ps.asset_id = r.selected_asset_id
  AND ps.outcome = r.selected_outcome
ORDER BY r.entered_at_utc, r.id;
""";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("ParentId", ParentStrategyId);
        command.Parameters.AddWithValue("CutoffUtc", CutoffUtc);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<SourceRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var line = reader.GetString(0);
            var fields = line.Split('|');
            if (fields.Length != 22)
            {
                throw new InvalidOperationException($"Canonical source row has {fields.Length} fields instead of 22.");
            }

            rows.Add(SourceRow.Parse(line, reader.GetString(1), reader.GetGuid(2), reader.GetGuid(3), fields));
        }

        return rows;
    }

    private static async Task<DatabaseSnapshot> ReadSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        NpgsqlConnectionStringBuilder builder,
        IReadOnlyList<PlannedEntry> plan,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT
    current_database(),
    NOT pg_is_in_recovery(),
    (SELECT count(*) FROM public.schema_migration_history WHERE migration_id = '0002-eth-lossdiff-gated-children'
        AND semantic_checksum = 'ea4acf9b6a444fea242ba86807f2b3580b0d111c7370fa40cda56f0e0cfd3767'),
    (SELECT count(*) FROM public.strategies s WHERE
        s.id = 'b7c50005-0000-4000-8204-000000000001'
        AND s.code = 'eth_up_down_5m_1_diff_confirmed_average_premarket'
        AND s.name = 'ETH Up or Down 5m 1 Diff Confirmed Average Premarket'
        AND s.enabled AND NOT s.live_stakes AND NOT s.paused AND NOT s.auto_live_paused),
    (SELECT count(*) FROM public.strategies s WHERE
        (s.id = 'b7c50005-0000-4000-8225-000000000004' AND s.code = 'eth_up_down_5m_1_diff_confirmed_average_premarket_lossdiff_4_plus' AND s.name = 'ETH 5m 1 Diff Confirmed Average Premarket LossDiff 4+' OR
         s.id = 'b7c50005-0000-4000-8225-000000000013' AND s.code = 'eth_up_down_5m_1_diff_confirmed_average_premarket_lossdiff_13_plus_positive' AND s.name = 'ETH 5m 1 Diff Confirmed Average Premarket LossDiff 13+ Positive')
        AND s.enabled AND NOT s.live_stakes AND NOT s.paused AND NOT s.auto_live_paused),
    (SELECT count(*) FROM public.strategies s WHERE
        s.id IN ('b7c50005-0000-4000-8204-000000000001', 'b7c50005-0000-4000-8225-000000000004', 'b7c50005-0000-4000-8225-000000000013')
        OR s.code IN ('eth_up_down_5m_1_diff_confirmed_average_premarket', 'eth_up_down_5m_1_diff_confirmed_average_premarket_lossdiff_4_plus', 'eth_up_down_5m_1_diff_confirmed_average_premarket_lossdiff_13_plus_positive')
        OR s.name IN ('ETH Up or Down 5m 1 Diff Confirmed Average Premarket', 'ETH 5m 1 Diff Confirmed Average Premarket LossDiff 4+', 'ETH 5m 1 Diff Confirmed Average Premarket LossDiff 13+ Positive')),
    (SELECT count(*) FROM public.strategy_loss_diff_states state WHERE
        (state.child_strategy_id = 'b7c50005-0000-4000-8225-000000000004' AND state.mode = 'LossDiffReset' AND state.threshold = 4 OR
         state.child_strategy_id = 'b7c50005-0000-4000-8225-000000000013' AND state.mode = 'LossDiffPositive' AND state.threshold = 13)
        AND state.parent_strategy_id = 'b7c50005-0000-4000-8204-000000000001'
        AND state.started_at_utc = @CutoffUtc),
    (SELECT count(*) FROM public.strategy_child_parent_assignments assignment WHERE
        (assignment.id = 'b7c50005-0000-4000-8226-000000000004' AND assignment.child_strategy_id = 'b7c50005-0000-4000-8225-000000000004' AND assignment.child_mode = 'LossDiffReset'
         OR assignment.id = 'b7c50005-0000-4000-8226-000000000013' AND assignment.child_strategy_id = 'b7c50005-0000-4000-8225-000000000013' AND assignment.child_mode = 'LossDiffPositive')
        AND assignment.parent_strategy_id = 'b7c50005-0000-4000-8204-000000000001'
        AND assignment.asset_symbol = 'ETH' AND assignment.lookback_hours = 0
        AND assignment.parent_pnl_usd = 0 AND assignment.parent_roi_pct = 0
        AND assignment.assigned_at_utc = @CutoffUtc AND assignment.ended_at_utc IS NULL),
    (SELECT count(*) FROM public.strategy_market_paper_runs r
        WHERE r.strategy_id = 'b7c50005-0000-4000-8204-000000000001'
          AND r.entered_at_utc < @CutoffUtc AND r.status = 'Settled'
          AND r.realized_pnl_usd IS NOT NULL AND r.realized_pnl_usd <> 0),
    (SELECT count(*) FROM public.strategy_market_paper_runs WHERE strategy_id = ANY(@ChildIds) AND entered_at_utc < @CutoffUtc),
    (SELECT details FROM public.schema_data_migrations WHERE migration_key = @MarkerKey),
    (SELECT count(*) FROM public.service_heartbeats heartbeat
        WHERE heartbeat.service_name = 'PolyCopyTrader.Service'
        AND heartbeat.status = 'Running' AND heartbeat.mode = 'Live'
        AND heartbeat.version = @DeployedServiceVersion
        AND heartbeat.last_error IS NULL AND heartbeat.last_heartbeat_utc > clock_timestamp() - interval '3 minutes'),
    (SELECT count(*) FROM pg_stat_activity activity WHERE activity.datname = current_database()
        AND activity.pid <> pg_backend_pid() AND activity.wait_event_type = 'Lock'),
    (SELECT count(*) FROM public.strategy_loss_diff_parent_events WHERE child_strategy_id = ANY(@ChildIds) AND parent_entered_at_utc < @CutoffUtc);
""";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("CutoffUtc", CutoffUtc);
        command.Parameters.AddWithValue("ChildIds", Children.Select(child => child.Id).ToArray());
        command.Parameters.AddWithValue("MarkerKey", MarkerKey);
        command.Parameters.AddWithValue("DeployedServiceVersion", DeployedServiceVersion);
        DatabaseSnapshot snapshot;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Database snapshot query returned no row.");
            }

            snapshot = new DatabaseSnapshot(
                builder.Host ?? string.Empty,
                builder.Port,
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetInt64(11),
                reader.GetInt64(12),
                reader.GetInt64(13),
                string.Empty);
        }

        snapshot = snapshot with
        {
            InvariantDigest = await ReadInvariantDigestAsync(connection, transaction, cancellationToken),
            TargetIdCollisionCount = await ReadTargetIdCollisionCountAsync(connection, transaction, plan, cancellationToken),
            TargetRowCount = await ReadTargetRowCountAsync(connection, transaction, cancellationToken)
        };

        if (snapshot.MarkerDetails is not null)
        {
            snapshot = snapshot with
            {
                Target = await ReadTargetMetricsAsync(connection, transaction, cancellationToken),
                ExactTargetChainCount = await ReadExactTargetChainCountAsync(connection, transaction, plan, cancellationToken)
            };
        }
        else if (snapshot.PreCutoffChildRuns != 0)
        {
            snapshot = snapshot with
            {
                Target = await ReadTargetMetricsAsync(connection, transaction, cancellationToken),
                ExactTargetChainCount = await ReadExactTargetChainCountAsync(connection, transaction, plan, cancellationToken)
            };
        }

        return snapshot;
    }

    private static List<string> ValidateSnapshot(
        DatabaseSnapshot snapshot,
        IReadOnlyList<SourceRow> source,
        IReadOnlyList<PlannedEntry> plan,
        bool apply)
    {
        var problems = new List<string>();
        void Require(bool condition, string message)
        {
            if (!condition) problems.Add(message);
        }

        Require(string.Equals(snapshot.Host, "192.168.0.101", StringComparison.OrdinalIgnoreCase), $"host={snapshot.Host}");
        Require(snapshot.Port == 5432, $"port={snapshot.Port}");
        Require(snapshot.Database == "polycopytrader", $"database={snapshot.Database}");
        Require(snapshot.IsPrimary, "database is not primary");
        Require(snapshot.MigrationCount == 1, $"migration_count={snapshot.MigrationCount}");
        Require(snapshot.ParentStrategyCount == 1, $"parent_strategy_count={snapshot.ParentStrategyCount}");
        Require(snapshot.StrategyCount == 2, $"strategy_count={snapshot.StrategyCount}");
        Require(snapshot.CatalogIdentityCount == 3, $"catalog_identity_count={snapshot.CatalogIdentityCount}");
        Require(snapshot.StateCount == 2, $"state_count={snapshot.StateCount}");
        Require(snapshot.AssignmentCount == 2, $"assignment_count={snapshot.AssignmentCount}");
        problems.AddRange(ValidateSourceFacts(
            source.Count,
            source.Count(row => row.Won),
            source.Count(row => !row.Won),
            ComputeSourceDigest(source.Select(row => row.CanonicalLine)),
            ComputeSourceDigest(source.Select(row => row.FullChainCanonicalLine)),
            CutoffUtc));
        Require(snapshot.BaseSourceRows == 1060, $"base_source_rows={snapshot.BaseSourceRows}");
        Require(snapshot.HealthyHeartbeatCount == 1, $"exact_healthy_heartbeat_count={snapshot.HealthyHeartbeatCount}");
        Require(snapshot.WaitingLockCount == 0, $"waiting_locks={snapshot.WaitingLockCount}");
        Require(snapshot.ImportedPreCutoffEventCount == 0, $"imported_pre_cutoff_events={snapshot.ImportedPreCutoffEventCount}");

        foreach (var child in Children)
        {
            var metrics = CalculateMetrics(plan.Where(entry => entry.Child.Id == child.Id).Select(entry => entry.Source));
            Require(metrics == child.ExpectedMetrics, $"{child.Code} plan metrics drift: {metrics}");
        }

        if (snapshot.MarkerDetails is null)
        {
            Require(snapshot.PreCutoffChildRuns == 0, $"unmarked_pre_cutoff_child_runs={snapshot.PreCutoffChildRuns}");
            Require(snapshot.TargetIdCollisionCount == 0,
                $"unmarked_target_id_collisions={snapshot.TargetIdCollisionCount}");
            Require(snapshot.TargetRowCount == 0, $"unmarked_target_rows={snapshot.TargetRowCount}");
        }
        else
        {
            Require(MarkerMatches(snapshot.MarkerDetails, plan), "marker details mismatch");
            Require(snapshot.Target is not null, "marker exists but target metrics are absent");
            if (snapshot.Target is { } target)
            {
                foreach (var child in Children)
                {
                    Require(target.TryGetValue(child.Id, out var metrics) && metrics == child.ExpectedMetrics,
                        $"{child.Code} target metrics mismatch");
                }
            }
            Require(snapshot.ExactTargetChainCount == plan.Count,
                $"exact_target_chain_count={snapshot.ExactTargetChainCount}; expected={plan.Count}");
            Require(snapshot.TargetIdCollisionCount == plan.Count * 6L,
                $"target_id_count={snapshot.TargetIdCollisionCount}; expected={plan.Count * 6L}");
            Require(snapshot.TargetRowCount == plan.Count * 6L,
                $"target_row_count={snapshot.TargetRowCount}; expected={plan.Count * 6L}");
        }

        return problems;
    }

    internal static IReadOnlyList<string> ValidateSourceFacts(
        int count,
        int wins,
        int losses,
        string sourceDigest,
        string fullSourceDigest,
        DateTimeOffset cutoffUtc)
    {
        var problems = new List<string>();
        if (count != 1060) problems.Add($"source_count={count}");
        if (wins != 615) problems.Add($"source_wins={wins}");
        if (losses != 445) problems.Add($"source_losses={losses}");
        if (!string.Equals(sourceDigest, SourceDigest, StringComparison.Ordinal))
            problems.Add("source_digest drift");
        if (!string.Equals(fullSourceDigest, FullSourceDigest, StringComparison.Ordinal))
            problems.Add("full_source_digest drift");
        if (cutoffUtc != CutoffUtc) problems.Add($"cutoff={cutoffUtc:O}");
        return problems;
    }

    internal static async Task InsertEntryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PlannedEntry entry,
        CancellationToken cancellationToken)
    {
        var childWallet = "strategy:" + entry.Child.Code;
        var signalId = DeterministicId(entry.Child.Id, entry.Source.RunId, "signal");
        var orderId = DeterministicId(entry.Child.Id, entry.Source.RunId, "order");
        var fillId = DeterministicId(entry.Child.Id, entry.Source.RunId, "fill");
        var runId = DeterministicId(entry.Child.Id, entry.Source.RunId, "run");
        var positionId = DeterministicId(entry.Child.Id, entry.Source.RunId, "position");
        var settlementId = DeterministicId(entry.Child.Id, entry.Source.RunId, "settlement");
        var evidenceMode = entry.Source.HasEmbeddedSnapshot
            ? "embedded_order_book_snapshot"
            : "parent_persisted_fak_chain_only";

        const string sql = """
WITH parent AS (
    SELECT
        r.*, o.id AS parent_order_id, o.signal_id AS parent_signal_id,
        o.copied_trader_wallet AS parent_wallet, o.status AS order_status, o.side AS order_side,
        o.asset_id AS order_asset_id, o.condition_id AS order_condition_id, o.outcome AS order_outcome,
        o.price AS order_price, o.size_shares AS order_size_shares, o.notional_usd AS order_notional_usd,
        o.created_at_utc AS order_created_at_utc, o.expires_at_utc AS order_expires_at_utc,
        o.filled_at_utc AS order_filled_at_utc, o.cancelled_at_utc AS order_cancelled_at_utc,
        o.raw_decision_json AS parent_raw_decision_json, o.correlation_id AS parent_correlation_id,
        f.id AS parent_fill_id, f.price AS fill_price, f.size_shares AS fill_size_shares,
        f.filled_at_utc AS fill_filled_at_utc, f.evidence AS parent_fill_evidence,
        f.realized_pnl_usd AS fill_realized_pnl_usd, f.fee_usd AS fill_fee_usd,
        f.fee_accounting_status AS fill_fee_accounting_status, f.fee_liquidity_role AS fill_fee_liquidity_role,
        f.fee_calculation_source AS fill_fee_calculation_source, f.fee_rate AS fill_fee_rate,
        f.fee_exponent AS fill_fee_exponent, f.fee_taker_only AS fill_fee_taker_only,
        f.fee_calculated_at_utc AS fill_fee_calculated_at_utc, f.net_realized_pnl_usd AS fill_net_realized_pnl_usd,
        ps.id AS parent_settlement_id
    FROM public.strategy_market_paper_runs r
    INNER JOIN public.paper_orders o ON o.id = r.paper_order_id
    INNER JOIN public.paper_fills f ON f.id = @ParentFillId AND f.paper_order_id = o.id
    INNER JOIN public.paper_position_settlements ps ON ps.id = @ParentSettlementId
        AND ps.copied_trader_wallet = o.copied_trader_wallet AND ps.asset_id = o.asset_id
    WHERE r.id = @ParentRunId AND r.strategy_id = @ParentStrategyId
), inserted_signal AS (
    INSERT INTO public.signals (
        id, leader_trade_id, trader_wallet, condition_id, asset_id, outcome, leader_price,
        best_bid, best_ask, spread_abs, spread_pct, lag_seconds, score, accepted, decision,
        proposed_paper_price, proposed_size_shares, proposed_notional_usd, created_at_utc, raw_context_json)
    SELECT
        @SignalId, s.leader_trade_id, @ChildWallet, s.condition_id, s.asset_id, s.outcome, s.leader_price,
        s.best_bid, s.best_ask, s.spread_abs, s.spread_pct, s.lag_seconds, s.score, true,
        'accepted_eth_lossdiff_history_backfill', s.proposed_paper_price, s.proposed_size_shares,
        s.proposed_notional_usd, s.created_at_utc,
        COALESCE(s.raw_context_json, '{}'::jsonb) || jsonb_build_object(
            'version', @EvidenceVersion, 'child_mode', @ChildMode, 'threshold', @Threshold,
            'pre_entry_loss_diff', @PreEntryValue, 'parent_run_id', p.id, 'parent_signal_id', p.parent_signal_id,
            'parent_order_id', p.parent_order_id, 'parent_fill_id', p.parent_fill_id,
            'parent_settlement_id', p.parent_settlement_id, 'cutoff_utc', @CutoffUtc,
            'source_digest', @SourceDigest, 'evidence_mode', @EvidenceMode,
            'embedded_order_book_snapshot_available', @HasSnapshot, 'exact_snapshot_replay', @HasSnapshot)
    FROM parent p INNER JOIN public.signals s ON s.id = p.parent_signal_id
    RETURNING id
), inserted_order AS (
    INSERT INTO public.paper_orders (
        id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id, condition_id, outcome,
        price, size_shares, notional_usd, created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc,
        raw_decision_json, correlation_id, execution_source)
    SELECT
        @OrderId, @SignalId, @ChildStrategyId, @ChildWallet, p.order_status, p.order_side,
        p.order_asset_id, p.order_condition_id, p.order_outcome, p.order_price, p.order_size_shares,
        p.order_notional_usd, p.order_created_at_utc, p.order_expires_at_utc, p.order_filled_at_utc,
        p.order_cancelled_at_utc,
        COALESCE(p.parent_raw_decision_json, '{}'::jsonb) || jsonb_build_object(
            'version', @EvidenceVersion, 'child_mode', @ChildMode, 'threshold', @Threshold,
            'pre_entry_loss_diff', @PreEntryValue, 'parent_run_id', p.id, 'parent_signal_id', p.parent_signal_id,
            'parent_order_id', p.parent_order_id, 'parent_fill_id', p.parent_fill_id,
            'parent_settlement_id', p.parent_settlement_id, 'cutoff_utc', @CutoffUtc,
            'source_digest', @SourceDigest, 'evidence_mode', @EvidenceMode,
            'embedded_order_book_snapshot_available', @HasSnapshot, 'exact_snapshot_replay', @HasSnapshot),
        p.parent_correlation_id, 'btc_updown5m_child_mirror_fak_paper'
    FROM parent p, inserted_signal
    RETURNING id
), inserted_fill AS (
    INSERT INTO public.paper_fills (
        id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd, fee_usd,
        fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate, fee_exponent,
        fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd)
    SELECT
        @FillId, @OrderId, p.fill_price, p.fill_size_shares, p.fill_filled_at_utc,
        jsonb_build_object('version', @EvidenceVersion, 'parent_evidence', p.parent_fill_evidence,
            'parent_run_id', p.id, 'parent_signal_id', p.parent_signal_id, 'parent_order_id', p.parent_order_id,
            'parent_fill_id', p.parent_fill_id, 'parent_settlement_id', p.parent_settlement_id,
            'child_mode', @ChildMode, 'threshold', @Threshold, 'pre_entry_loss_diff', @PreEntryValue,
            'cutoff_utc', @CutoffUtc, 'source_digest', @SourceDigest, 'evidence_mode', @EvidenceMode,
            'embedded_order_book_snapshot_available', @HasSnapshot, 'exact_snapshot_replay', @HasSnapshot)::text,
        p.fill_realized_pnl_usd, p.fill_fee_usd, p.fill_fee_accounting_status, p.fill_fee_liquidity_role,
        p.fill_fee_calculation_source, p.fill_fee_rate, p.fill_fee_exponent, p.fill_fee_taker_only,
        p.fill_fee_calculated_at_utc, p.fill_net_realized_pnl_usd
    FROM parent p, inserted_order
    RETURNING id
), inserted_run AS (
    INSERT INTO public.strategy_market_paper_runs (
        id, strategy_id, market_id, condition_id, market_slug, market_title, category, market_start_utc,
        market_end_utc, detected_at_utc, entry_due_at_utc, status, selected_asset_id, selected_outcome,
        entry_price, stake_usd, size_shares, signal_id, paper_order_id, entered_at_utc, settlement_price,
        settlement_value_usd, realized_pnl_usd, fee_usd, fee_accounting_status, fee_liquidity_role,
        fee_calculation_source, fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc,
        net_realized_pnl_usd, settled_at_utc, skip_reason, skip_diagnostics_json, retention_scope,
        created_at_utc, updated_at_utc)
    SELECT
        @RunId, @ChildStrategyId, p.market_id, p.condition_id, p.market_slug, p.market_title, p.category,
        p.market_start_utc, p.market_end_utc, p.detected_at_utc, p.entry_due_at_utc, p.status,
        p.selected_asset_id, p.selected_outcome, p.entry_price, p.stake_usd, p.size_shares,
        @SignalId, @OrderId, p.entered_at_utc, p.settlement_price, p.settlement_value_usd,
        p.realized_pnl_usd, p.fee_usd, p.fee_accounting_status, p.fee_liquidity_role,
        p.fee_calculation_source, p.fee_rate, p.fee_exponent, p.fee_taker_only,
        p.fee_calculated_at_utc, p.net_realized_pnl_usd, p.settled_at_utc, NULL,
        jsonb_build_object('version', @EvidenceVersion, 'child_mode', @ChildMode, 'threshold', @Threshold,
            'pre_entry_loss_diff', @PreEntryValue, 'parent_run_id', p.id, 'parent_signal_id', p.parent_signal_id,
            'parent_order_id', p.parent_order_id, 'parent_fill_id', p.parent_fill_id,
            'parent_settlement_id', p.parent_settlement_id, 'cutoff_utc', @CutoffUtc,
            'source_digest', @SourceDigest, 'evidence_mode', @EvidenceMode,
            'embedded_order_book_snapshot_available', @HasSnapshot, 'exact_snapshot_replay', @HasSnapshot),
        'PaperOnly', p.created_at_utc, p.updated_at_utc
    FROM parent p, inserted_fill
    RETURNING id
), inserted_position AS (
    INSERT INTO public.paper_positions (
        id, copied_trader_wallet, asset_id, condition_id, outcome, size_shares, average_price,
        estimated_value_usd, unrealized_pnl_usd, fee_usd, fee_accounting_status, fee_liquidity_role,
        fee_calculation_source, fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc,
        net_unrealized_pnl_usd, updated_at_utc)
    SELECT
        @PositionId, @ChildWallet, pos.asset_id, pos.condition_id, pos.outcome, pos.size_shares,
        pos.average_price, pos.estimated_value_usd, pos.unrealized_pnl_usd, pos.fee_usd,
        pos.fee_accounting_status, pos.fee_liquidity_role, pos.fee_calculation_source, pos.fee_rate,
        pos.fee_exponent, pos.fee_taker_only, pos.fee_calculated_at_utc, pos.net_unrealized_pnl_usd,
        pos.updated_at_utc
    FROM parent p
    INNER JOIN public.paper_positions pos ON pos.id = @ParentPositionId
        AND pos.copied_trader_wallet = p.parent_wallet AND pos.asset_id = p.order_asset_id,
        inserted_run
    RETURNING id
)
INSERT INTO public.paper_position_settlements (
    id, copied_trader_wallet, asset_id, condition_id, outcome, winning_asset_id, winning_outcome,
    category, settled_size_shares, average_price, cost_basis_usd, settlement_value_usd,
    realized_pnl_usd, fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source,
    fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd, won,
    settlement_source, settled_at_utc, created_at_utc)
SELECT
    @SettlementId, @ChildWallet, ps.asset_id, ps.condition_id, ps.outcome, ps.winning_asset_id,
    ps.winning_outcome, ps.category, ps.settled_size_shares, ps.average_price, ps.cost_basis_usd,
    ps.settlement_value_usd, ps.realized_pnl_usd, ps.fee_usd, ps.fee_accounting_status,
    ps.fee_liquidity_role, ps.fee_calculation_source, ps.fee_rate, ps.fee_exponent,
    ps.fee_taker_only, ps.fee_calculated_at_utc, ps.net_realized_pnl_usd, ps.won,
    ps.settlement_source, ps.settled_at_utc, ps.created_at_utc
FROM parent p
INNER JOIN public.paper_position_settlements ps ON ps.id = p.parent_settlement_id,
inserted_position;
""";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("ParentRunId", entry.Source.RunId);
        command.Parameters.AddWithValue("ParentFillId", entry.Source.FillId);
        command.Parameters.AddWithValue("ParentPositionId", entry.Source.ParentPositionId);
        command.Parameters.AddWithValue("ParentSettlementId", entry.Source.ParentSettlementId);
        command.Parameters.AddWithValue("ParentStrategyId", ParentStrategyId);
        command.Parameters.AddWithValue("SignalId", signalId);
        command.Parameters.AddWithValue("OrderId", orderId);
        command.Parameters.AddWithValue("FillId", fillId);
        command.Parameters.AddWithValue("RunId", runId);
        command.Parameters.AddWithValue("PositionId", positionId);
        command.Parameters.AddWithValue("SettlementId", settlementId);
        command.Parameters.AddWithValue("ChildStrategyId", entry.Child.Id);
        command.Parameters.AddWithValue("ChildWallet", childWallet);
        command.Parameters.AddWithValue("EvidenceVersion", EvidenceVersion);
        command.Parameters.AddWithValue("ChildMode", entry.Child.Mode);
        command.Parameters.AddWithValue("Threshold", entry.Child.Threshold);
        command.Parameters.AddWithValue("PreEntryValue", entry.PreEntryValue);
        command.Parameters.AddWithValue("CutoffUtc", CutoffUtc);
        command.Parameters.AddWithValue("SourceDigest", SourceDigest);
        command.Parameters.AddWithValue("EvidenceMode", evidenceMode);
        command.Parameters.AddWithValue("HasSnapshot", entry.Source.HasEmbeddedSnapshot);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException($"Incomplete insert chain for {entry.Child.Code}/{entry.Source.RunId:D}.");
        }
    }

    private static async Task<Dictionary<Guid, Metrics>> ReadTargetMetricsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT strategy_id, count(*), count(*) FILTER (WHERE realized_pnl_usd > 0),
       count(*) FILTER (WHERE realized_pnl_usd < 0), sum(stake_usd), sum(realized_pnl_usd),
       sum(fee_usd), sum(net_realized_pnl_usd),
       count(*) FILTER (WHERE skip_diagnostics_json->>'embedded_order_book_snapshot_available' = 'true')
FROM public.strategy_market_paper_runs
WHERE strategy_id = ANY(@ChildIds) AND entered_at_utc < @CutoffUtc AND status = 'Settled'
GROUP BY strategy_id;
""";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("ChildIds", Children.Select(child => child.Id).ToArray());
        command.Parameters.AddWithValue("CutoffUtc", CutoffUtc);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<Guid, Metrics>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetGuid(0)] = new Metrics(
                checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2)), checked((int)reader.GetInt64(3)),
                reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7),
                checked((int)reader.GetInt64(8)));
        }

        return result;
    }

    internal static async Task<long> ReadExactTargetChainCountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<PlannedEntry> plan,
        CancellationToken cancellationToken)
    {
        const string sql = """
WITH expected AS (
    SELECT * FROM unnest(
        @RunIds::uuid[], @SignalIds::uuid[], @OrderIds::uuid[], @FillIds::uuid[],
        @PositionIds::uuid[], @SettlementIds::uuid[], @ParentRunIds::uuid[], @ParentSignalIds::uuid[],
        @ParentOrderIds::uuid[], @ParentFillIds::uuid[], @ParentPositionIds::uuid[],
        @ParentSettlementIds::uuid[], @ChildIds::uuid[], @ChildCodes::text[], @ChildModes::text[],
        @Thresholds::integer[], @PreEntryValues::integer[], @HasSnapshots::boolean[])
    AS item(run_id, signal_id, order_id, fill_id, position_id, settlement_id, parent_run_id,
        parent_signal_id, parent_order_id, parent_fill_id, parent_position_id, parent_settlement_id,
        child_id, child_code, child_mode, threshold, pre_entry_value, has_snapshot)
), expected_with_audit AS (
    SELECT e.*, jsonb_build_object(
        'version', @EvidenceVersion, 'child_mode', e.child_mode, 'threshold', e.threshold,
        'pre_entry_loss_diff', e.pre_entry_value, 'parent_run_id', e.parent_run_id,
        'parent_signal_id', e.parent_signal_id, 'parent_order_id', e.parent_order_id,
        'parent_fill_id', e.parent_fill_id, 'parent_settlement_id', e.parent_settlement_id,
        'cutoff_utc', @CutoffUtc, 'source_digest', @SourceDigest,
        'evidence_mode', CASE WHEN e.has_snapshot THEN 'embedded_order_book_snapshot' ELSE 'parent_persisted_fak_chain_only' END,
        'embedded_order_book_snapshot_available', e.has_snapshot, 'exact_snapshot_replay', e.has_snapshot) AS audit
    FROM expected e
)
SELECT count(*)
FROM expected_with_audit e
INNER JOIN public.strategy_market_paper_runs r ON r.id = e.run_id AND r.strategy_id = e.child_id
INNER JOIN public.signals s ON s.id = e.signal_id AND s.id = r.signal_id
INNER JOIN public.paper_orders o ON o.id = e.order_id AND o.id = r.paper_order_id
    AND o.signal_id = e.signal_id AND o.strategy_id = e.child_id
INNER JOIN public.paper_fills f ON f.id = e.fill_id AND f.paper_order_id = o.id
INNER JOIN public.strategy_market_paper_runs parent_run ON parent_run.id = e.parent_run_id
INNER JOIN public.signals parent_signal ON parent_signal.id = e.parent_signal_id
    AND parent_signal.id = parent_run.signal_id
INNER JOIN public.paper_orders parent_order ON parent_order.id = e.parent_order_id
    AND parent_order.id = parent_run.paper_order_id AND parent_order.signal_id = parent_signal.id
INNER JOIN public.paper_fills parent_fill ON parent_fill.id = e.parent_fill_id
    AND parent_fill.paper_order_id = parent_order.id
INNER JOIN public.paper_positions parent_position ON parent_position.id = e.parent_position_id
INNER JOIN public.paper_position_settlements parent_settlement ON parent_settlement.id = e.parent_settlement_id
INNER JOIN public.paper_positions p ON p.id = e.position_id
INNER JOIN public.paper_position_settlements ps ON ps.id = e.settlement_id
WHERE r.status = 'Settled' AND r.retention_scope = 'PaperOnly'
  AND o.status = 'Filled' AND o.side = 'Buy'
  AND o.execution_source = 'btc_updown5m_child_mirror_fak_paper'
  AND o.copied_trader_wallet = 'strategy:' || e.child_code
  AND s.trader_wallet = 'strategy:' || e.child_code AND s.accepted
  AND s.decision = 'accepted_eth_lossdiff_history_backfill'
  AND s.raw_context_json = COALESCE(parent_signal.raw_context_json, '{}'::jsonb) || e.audit
  AND o.raw_decision_json = COALESCE(parent_order.raw_decision_json, '{}'::jsonb) || e.audit
  AND f.evidence::jsonb = jsonb_build_object(
      'version', @EvidenceVersion, 'parent_evidence', parent_fill.evidence,
      'parent_run_id', e.parent_run_id, 'parent_signal_id', e.parent_signal_id,
      'parent_order_id', e.parent_order_id, 'parent_fill_id', e.parent_fill_id,
      'parent_settlement_id', e.parent_settlement_id, 'child_mode', e.child_mode,
      'threshold', e.threshold, 'pre_entry_loss_diff', e.pre_entry_value,
      'cutoff_utc', @CutoffUtc, 'source_digest', @SourceDigest,
      'evidence_mode', CASE WHEN e.has_snapshot THEN 'embedded_order_book_snapshot' ELSE 'parent_persisted_fak_chain_only' END,
      'embedded_order_book_snapshot_available', e.has_snapshot, 'exact_snapshot_replay', e.has_snapshot)
  AND r.skip_reason IS NULL AND r.skip_diagnostics_json = e.audit
  AND p.copied_trader_wallet = 'strategy:' || e.child_code
  AND ps.copied_trader_wallet = 'strategy:' || e.child_code
  AND (to_jsonb(s) - ARRAY['id','trader_wallet','accepted','decision','raw_context_json']::text[])
      = (to_jsonb(parent_signal) - ARRAY['id','trader_wallet','accepted','decision','raw_context_json']::text[])
  AND (to_jsonb(o) - ARRAY['id','signal_id','strategy_id','copied_trader_wallet','raw_decision_json','execution_source']::text[])
      = (to_jsonb(parent_order) - ARRAY['id','signal_id','strategy_id','copied_trader_wallet','raw_decision_json','execution_source']::text[])
  AND (to_jsonb(f) - ARRAY['id','paper_order_id','evidence']::text[])
      = (to_jsonb(parent_fill) - ARRAY['id','paper_order_id','evidence']::text[])
  AND (to_jsonb(r) - ARRAY['id','strategy_id','signal_id','paper_order_id','skip_reason','skip_diagnostics_json','retention_scope']::text[])
      = (to_jsonb(parent_run) - ARRAY['id','strategy_id','signal_id','paper_order_id','skip_reason','skip_diagnostics_json','retention_scope']::text[])
  AND (to_jsonb(p) - ARRAY['id','copied_trader_wallet']::text[])
      = (to_jsonb(parent_position) - ARRAY['id','copied_trader_wallet']::text[])
  AND (to_jsonb(ps) - ARRAY['id','copied_trader_wallet']::text[])
      = (to_jsonb(parent_settlement) - ARRAY['id','copied_trader_wallet']::text[]);
""";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("RunIds", plan.Select(entry => DeterministicId(entry.Child.Id, entry.Source.RunId, "run")).ToArray());
        command.Parameters.AddWithValue("SignalIds", plan.Select(entry => DeterministicId(entry.Child.Id, entry.Source.RunId, "signal")).ToArray());
        command.Parameters.AddWithValue("OrderIds", plan.Select(entry => DeterministicId(entry.Child.Id, entry.Source.RunId, "order")).ToArray());
        command.Parameters.AddWithValue("FillIds", plan.Select(entry => DeterministicId(entry.Child.Id, entry.Source.RunId, "fill")).ToArray());
        command.Parameters.AddWithValue("PositionIds", plan.Select(entry => DeterministicId(entry.Child.Id, entry.Source.RunId, "position")).ToArray());
        command.Parameters.AddWithValue("SettlementIds", plan.Select(entry => DeterministicId(entry.Child.Id, entry.Source.RunId, "settlement")).ToArray());
        command.Parameters.AddWithValue("ParentRunIds", plan.Select(entry => entry.Source.RunId).ToArray());
        command.Parameters.AddWithValue("ParentSignalIds", plan.Select(entry => entry.Source.SignalId).ToArray());
        command.Parameters.AddWithValue("ParentOrderIds", plan.Select(entry => entry.Source.OrderId).ToArray());
        command.Parameters.AddWithValue("ParentFillIds", plan.Select(entry => entry.Source.FillId).ToArray());
        command.Parameters.AddWithValue("ParentPositionIds", plan.Select(entry => entry.Source.ParentPositionId).ToArray());
        command.Parameters.AddWithValue("ParentSettlementIds", plan.Select(entry => entry.Source.ParentSettlementId).ToArray());
        command.Parameters.AddWithValue("ChildIds", plan.Select(entry => entry.Child.Id).ToArray());
        command.Parameters.AddWithValue("ChildCodes", plan.Select(entry => entry.Child.Code).ToArray());
        command.Parameters.AddWithValue("ChildModes", plan.Select(entry => entry.Child.Mode).ToArray());
        command.Parameters.AddWithValue("Thresholds", plan.Select(entry => entry.Child.Threshold).ToArray());
        command.Parameters.AddWithValue("PreEntryValues", plan.Select(entry => entry.PreEntryValue).ToArray());
        command.Parameters.AddWithValue("HasSnapshots", plan.Select(entry => entry.Source.HasEmbeddedSnapshot).ToArray());
        command.Parameters.AddWithValue("EvidenceVersion", EvidenceVersion);
        command.Parameters.AddWithValue("CutoffUtc", CutoffUtc);
        command.Parameters.AddWithValue("SourceDigest", SourceDigest);
        return await ExecuteScalarAsync<long>(connection, transaction, command, cancellationToken);
    }

    internal static async Task<long> ReadTargetIdCollisionCountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<PlannedEntry> plan,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT
    (SELECT count(*) FROM public.signals WHERE id = ANY(@SignalIds)) +
    (SELECT count(*) FROM public.paper_orders WHERE id = ANY(@OrderIds)) +
    (SELECT count(*) FROM public.paper_fills WHERE id = ANY(@FillIds)) +
    (SELECT count(*) FROM public.strategy_market_paper_runs WHERE id = ANY(@RunIds)) +
    (SELECT count(*) FROM public.paper_positions WHERE id = ANY(@PositionIds)) +
    (SELECT count(*) FROM public.paper_position_settlements WHERE id = ANY(@SettlementIds));
""";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("SignalIds", plan.Select(entry => DeterministicId(entry.Child.Id, entry.Source.RunId, "signal")).ToArray());
        command.Parameters.AddWithValue("OrderIds", plan.Select(entry => DeterministicId(entry.Child.Id, entry.Source.RunId, "order")).ToArray());
        command.Parameters.AddWithValue("FillIds", plan.Select(entry => DeterministicId(entry.Child.Id, entry.Source.RunId, "fill")).ToArray());
        command.Parameters.AddWithValue("RunIds", plan.Select(entry => DeterministicId(entry.Child.Id, entry.Source.RunId, "run")).ToArray());
        command.Parameters.AddWithValue("PositionIds", plan.Select(entry => DeterministicId(entry.Child.Id, entry.Source.RunId, "position")).ToArray());
        command.Parameters.AddWithValue("SettlementIds", plan.Select(entry => DeterministicId(entry.Child.Id, entry.Source.RunId, "settlement")).ToArray());
        return await ExecuteScalarAsync<long>(connection, transaction, command, cancellationToken);
    }

    internal static async Task<long> ReadTargetRowCountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT
    (SELECT count(*) FROM public.signals
        WHERE trader_wallet = ANY(@ChildWallets) AND created_at_utc < @CutoffUtc) +
    (SELECT count(*) FROM public.paper_orders
        WHERE strategy_id = ANY(@ChildIds) AND created_at_utc < @CutoffUtc) +
    (SELECT count(*) FROM public.paper_fills f INNER JOIN public.paper_orders o ON o.id = f.paper_order_id
        WHERE o.strategy_id = ANY(@ChildIds) AND o.created_at_utc < @CutoffUtc) +
    (SELECT count(*) FROM public.strategy_market_paper_runs
        WHERE strategy_id = ANY(@ChildIds) AND entered_at_utc < @CutoffUtc) +
    (SELECT count(*) FROM public.paper_positions
        WHERE copied_trader_wallet = ANY(@ChildWallets) AND updated_at_utc < @CutoffUtc) +
    (SELECT count(*) FROM public.paper_position_settlements
        WHERE copied_trader_wallet = ANY(@ChildWallets) AND settled_at_utc < @CutoffUtc);
""";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("ChildIds", Children.Select(child => child.Id).ToArray());
        command.Parameters.AddWithValue("ChildWallets", Children.Select(child => "strategy:" + child.Code).ToArray());
        command.Parameters.AddWithValue("CutoffUtc", CutoffUtc);
        return await ExecuteScalarAsync<long>(connection, transaction, command, cancellationToken);
    }

    internal static async Task<string> ReadInvariantDigestAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT value
FROM (
    SELECT 'assignment|' || to_jsonb(a)::text AS value
    FROM public.strategy_child_parent_assignments a WHERE a.child_strategy_id = ANY(@ChildIds)
    UNION ALL
    SELECT 'event|' || to_jsonb(e)::text
    FROM public.strategy_loss_diff_parent_events e WHERE e.child_strategy_id = ANY(@ChildIds)
    UNION ALL
    SELECT 'live_order|' || to_jsonb(l)::text
    FROM public.live_orders l WHERE l.strategy_id = ANY(@ChildIds)
    UNION ALL
    SELECT 'live_event|' || to_jsonb(e)::text
    FROM public.live_trading_events e
    WHERE e.details ILIKE ANY(@ChildPatterns)
    UNION ALL
    SELECT 'shadow|' || to_jsonb(d)::text
    FROM public.paper_live_shadow_decisions d WHERE d.strategy_id = ANY(@ChildIds)
    UNION ALL
    SELECT 'state|' || to_jsonb(s)::text
    FROM public.strategy_loss_diff_states s WHERE s.child_strategy_id = ANY(@ChildIds)
    UNION ALL
    SELECT 'strategy|' || to_jsonb(s)::text
    FROM public.strategies s WHERE s.id = ANY(@StrategyIds)
) invariant
ORDER BY value;
""";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var childIds = Children.Select(child => child.Id).ToArray();
        command.Parameters.AddWithValue("ChildIds", childIds);
        command.Parameters.AddWithValue("StrategyIds", childIds.Append(ParentStrategyId).ToArray());
        command.Parameters.AddWithValue("ChildPatterns", Children
            .SelectMany(child => new[] { "%" + child.Id.ToString("D") + "%", "%" + child.Code + "%" })
            .ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return ComputeSourceDigest(rows);
    }

    private static Metrics CalculateMetrics(IEnumerable<SourceRow> rows)
    {
        var items = rows.ToArray();
        return new Metrics(
            items.Length,
            items.Count(row => row.Won),
            items.Count(row => !row.Won),
            items.Sum(row => row.Stake),
            items.Sum(row => row.Gross),
            items.Sum(row => row.Fee),
            items.Sum(row => row.Net),
            items.Count(row => row.HasEmbeddedSnapshot));
    }

    private static string BuildMembershipDigest(IReadOnlyList<PlannedEntry> plan) =>
        ComputeSourceDigest(plan
            .OrderBy(entry => entry.Child.Id)
            .ThenBy(entry => entry.Source.EnteredMicros)
            .ThenBy(entry => entry.Source.RunId)
            .Select(entry => $"{entry.Child.Id:D}|{entry.Source.RunId:D}|{entry.PreEntryValue}"));

    internal static string BuildMarkerDetails(IReadOnlyList<PlannedEntry> plan) => JsonSerializer.Serialize(new
    {
        version = EvidenceVersion,
        cutoff_utc = CutoffUtc,
        parent_strategy_id = ParentStrategyId,
        source_digest = SourceDigest,
        full_source_chain_digest = FullSourceDigest,
        selected_membership_digest = BuildMembershipDigest(plan),
        contract_digest = ApprovalDigest,
        command_build = CommandBuild,
        reset = Children[0].ExpectedMetrics,
        positive = Children[1].ExpectedMetrics
    });

    internal static bool MarkerMatches(string details, IReadOnlyList<PlannedEntry> plan)
    {
        try
        {
            using var actual = JsonDocument.Parse(details);
            using var expected = JsonDocument.Parse(BuildMarkerDetails(plan));
            return JsonElement.DeepEquals(actual.RootElement, expected.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task WritePreviewAsync(
        TextWriter output,
        DatabaseSnapshot snapshot,
        IReadOnlyList<SourceRow> source,
        IReadOnlyList<PlannedEntry> plan,
        IReadOnlyList<string> problems,
        bool apply)
    {
        await output.WriteLineAsync($"mode={(apply ? "apply" : "preview")}; host={snapshot.Host}; port={snapshot.Port}; database={snapshot.Database}; primary={snapshot.IsPrimary}; cutoff={CutoffUtc:O}");
        await output.WriteLineAsync($"source_rows={source.Count}; wins={source.Count(row => row.Won)}; losses={source.Count(row => !row.Won)}; digest={ComputeSourceDigest(source.Select(row => row.CanonicalLine))}; full_chain_digest={ComputeSourceDigest(source.Select(row => row.FullChainCanonicalLine))}; base_rows={snapshot.BaseSourceRows}");
        foreach (var child in Children)
        {
            var metrics = CalculateMetrics(plan.Where(entry => entry.Child.Id == child.Id).Select(entry => entry.Source));
            await output.WriteLineAsync($"{child.Code}: {metrics}");
        }
        await output.WriteLineAsync($"marker={(snapshot.MarkerDetails is null ? "absent" : "present")}; pre_cutoff_child_runs={snapshot.PreCutoffChildRuns}; target_rows={snapshot.TargetRowCount}; heartbeat_count={snapshot.HealthyHeartbeatCount}; waiting_locks={snapshot.WaitingLockCount}; imported_pre_cutoff_events={snapshot.ImportedPreCutoffEventCount}; invariant_digest={snapshot.InvariantDigest}");
        foreach (var problem in problems)
        {
            await output.WriteLineAsync("BLOCKED: " + problem);
        }
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is T typed ? typed : throw new InvalidOperationException($"Expected scalar {typeof(T).Name}.");
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        _ = connection;
        _ = transaction;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is T typed ? typed : throw new InvalidOperationException($"Expected scalar {typeof(T).Name}.");
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    internal sealed record ChildDefinition(
        Guid Id,
        string Code,
        string Name,
        string Mode,
        int Threshold,
        int ExpectedTrades,
        int ExpectedWins,
        int ExpectedLosses,
        decimal ExpectedStake,
        decimal ExpectedGross,
        decimal ExpectedFee,
        decimal ExpectedNet,
        int ExpectedSnapshots)
    {
        public Metrics ExpectedMetrics => new(
            ExpectedTrades, ExpectedWins, ExpectedLosses, ExpectedStake, ExpectedGross, ExpectedFee,
            ExpectedNet, ExpectedSnapshots);
    }

    internal sealed record SourceRow(
        string CanonicalLine,
        string FullChainCanonicalLine,
        Guid ParentPositionId,
        Guid ParentSettlementId,
        Guid RunId,
        long EnteredMicros,
        long SettledMicros,
        bool Won,
        decimal Stake,
        decimal Gross,
        decimal Fee,
        decimal Net,
        Guid SignalId,
        Guid OrderId,
        string AssetId,
        string Outcome,
        decimal OrderPrice,
        decimal OrderSize,
        decimal OrderNotional,
        string ExecutionSource,
        bool HasEmbeddedSnapshot,
        Guid FillId,
        decimal FillPrice,
        decimal FillSize,
        decimal FillFee,
        decimal? FillNet)
    {
        public static SourceRow Parse(
            string line,
            string fullChainLine,
            Guid parentPositionId,
            Guid parentSettlementId,
            string[] f) => new(
            line,
            fullChainLine,
            parentPositionId,
            parentSettlementId,
            Guid.Parse(f[0]),
            long.Parse(f[1], CultureInfo.InvariantCulture),
            long.Parse(f[2], CultureInfo.InvariantCulture),
            f[3] == "1",
            decimal.Parse(f[4], CultureInfo.InvariantCulture),
            decimal.Parse(f[5], CultureInfo.InvariantCulture),
            decimal.Parse(f[6], CultureInfo.InvariantCulture),
            decimal.Parse(f[7], CultureInfo.InvariantCulture),
            Guid.Parse(f[8]),
            Guid.Parse(f[9]),
            f[10],
            f[11],
            decimal.Parse(f[12], CultureInfo.InvariantCulture),
            decimal.Parse(f[13], CultureInfo.InvariantCulture),
            decimal.Parse(f[14], CultureInfo.InvariantCulture),
            f[15],
            f[16] == "1",
            Guid.Parse(f[17]),
            decimal.Parse(f[18], CultureInfo.InvariantCulture),
            decimal.Parse(f[19], CultureInfo.InvariantCulture),
            decimal.Parse(f[20], CultureInfo.InvariantCulture),
            string.IsNullOrEmpty(f[21]) ? null : decimal.Parse(f[21], CultureInfo.InvariantCulture));
    }

    internal sealed record PlannedEntry(ChildDefinition Child, SourceRow Source, int PreEntryValue);

    internal sealed record Metrics(
        int Trades,
        int Wins,
        int Losses,
        decimal Stake,
        decimal Gross,
        decimal Fee,
        decimal Net,
        int EmbeddedSnapshots)
    {
        public override string ToString() =>
            $"trades={Trades}; wins={Wins}; losses={Losses}; stake={Stake:F8}; gross={Gross:F8}; fee={Fee:F8}; net={Net:F8}; embedded_snapshots={EmbeddedSnapshots}";
    }

    private sealed record DatabaseSnapshot(
        string Host,
        int Port,
        string Database,
        bool IsPrimary,
        long MigrationCount,
        long ParentStrategyCount,
        long StrategyCount,
        long CatalogIdentityCount,
        long StateCount,
        long AssignmentCount,
        long BaseSourceRows,
        long PreCutoffChildRuns,
        string? MarkerDetails,
        long HealthyHeartbeatCount,
        long WaitingLockCount,
        long ImportedPreCutoffEventCount,
        string InvariantDigest)
    {
        public Dictionary<Guid, Metrics>? Target { get; init; }
        public long? ExactTargetChainCount { get; init; }
        public long TargetIdCollisionCount { get; init; }
        public long TargetRowCount { get; init; }
    }
}
