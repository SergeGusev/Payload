using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Startup;

/// <summary>Closed, explicitly approved historical model; never starts the service or submits orders.</summary>
public static class EthLossDiffPositiveProgressHistoryBackfillCommand
{
    public const string CommandFlag = "--backfill-eth-progress34-history";
    public const string ApprovalDigest = "sha256:d2ec671347eb083cab33ab7ed9c67280e6f8887eba06bcae14b2e6eae57602f2";
    internal const string ContractId = "RC-20260903-eth-progress34-native-history";
    internal const string ExecutionSource = "eth_lossdiff_positive_progress_history_research_paper";
    internal const string EvidenceVersion = "eth_progress34_parent_average_full_fill_history_v1";
    internal const string MarkerKey = "20260903_eth_progress34_native_history_v1";
    internal const string ServiceVersion = "info=1.0.0+eab41015744d4d2fcc04b042d946529efeb13084; assembly=1.0.0.0; mvid=0b7cc2c4a796";
    // Frozen by the successful read-only31612-chain preview on2026-09-03 at09:43 UTC.
    internal const string FrozenSourceDigest = "sha256:3048d1e890a5f605e9d7c4107731477f11dd98e0f62521880f1d263f41ac6533";
    internal const string FrozenPlanDigest = "sha256:966103194019979106007e3659316bd41e6938f4dc4e4076636a2fb086e66817";
    internal static readonly DateTimeOffset CutoffUtc = DateTimeOffset.Parse("2026-09-03T05:32:51.200614Z", CultureInfo.InvariantCulture);
    internal static readonly Guid Up4 = Guid.Parse("b7c50005-0000-4000-8137-000000000104");
    internal static readonly Guid Up8 = Guid.Parse("b7c50005-0000-4000-8137-000000000108");
    private const long AdvisoryKey = 8236202609030034;
    internal const int MaximumPendingBatches = 8;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    internal sealed record Child(Guid Id, Guid AssignmentId, Guid ParentId, string Code, string Name, int Cap)
    {
        public string Wallet => "strategy:" + Code;
    }

    // Closed ranges were independently compared to all34 literal approved/migration tuples.
    internal static readonly Child[] Children = Enumerable.Range(1, 16).Select(n => DefineChild(4, n))
        .Concat(Enumerable.Range(1, 18).Select(n => DefineChild(8, n))).ToArray();

    private static Child DefineChild(int bps, int cap) => new(
        Guid.Parse($"b7c50005-0000-4000-{(bps == 4 ? 8236 : 8237)}-{cap:000000000000}"),
        Guid.Parse($"b7c50005-0000-4000-{(bps == 4 ? 8238 : 8239)}-{cap:000000000000}"),
        bps == 4 ? Up4 : Up8,
        $"eth_up_down_5m_up_bps_{bps}_fak_premarket_lossdiff_positive_progress_cap_{cap}",
        $"ETH 5m Up {bps} bps Reference Average Premarket LossDiff Positive Progress Cap {cap}", cap);

    internal sealed record Source(
        Guid RunId, Guid ParentId, string MarketId, string AssetId, string ConditionId, string Outcome,
        DateTimeOffset EnteredAt, DateTimeOffset SettledAt, decimal Spent, decimal Shares,
        decimal SettlementPrice, decimal Payout, decimal Gross, decimal ParentFee,
        decimal? Rate, int? Exponent, bool? TakerOnly, string FeeSource, decimal Net,
        bool ChainConsistent, string NativeSnapshot, decimal DisplayPrice)
    {
        public string Fingerprint => Hash(NativeSnapshot);
    }

    internal sealed record Entry(Child Child, Source Source, int Counter, int Multiplier,
        decimal Spent, decimal Shares, decimal AveragePrice, decimal Fee, decimal Payout)
    {
        public decimal Gross => Payout - Spent;
        public decimal Net => Gross - Fee;
        public Guid Id(string role) => DeterministicId(Child.Id, Source.RunId, role);
    }

    internal sealed record Metrics(int Trades, int Wins, int Losses, decimal Spent, decimal Payout, decimal Fee)
    {
        public decimal Gross => Payout - Spent;
        public decimal Net => Gross - Fee;
        public decimal NetRoi => Spent + Fee == 0 ? 0 : 100 * Net / (Spent + Fee);
    }

    internal sealed record NativeChain(Guid ChildId, string MarketId, string Wallet, string AssetId, string ConditionId,
        JsonObject Signal, JsonObject Order, JsonObject Fill, JsonObject Run, JsonObject Position, JsonObject Settlement);
    internal sealed record Plan(IReadOnlyList<Source> Sources, IReadOnlyList<Entry> Entries, string SourceDigest, string PlanDigest);
    internal sealed record Baseline(string ProtectedSettings, string SchemaFingerprint);
    internal sealed record LockSnapshot(long Waiting, string Participants);
    internal sealed record ProjectionSnapshot(long Events, long Queued, long Inflight, long Reconciliation)
    {
        public bool Pending => Events + Queued + Inflight + Reconciliation > 0;
    }

    internal enum WriteOutcome { None, Active, RolledBack, Committed, Unknown }

    internal sealed class Progress
    {
        public string Stage { get; set; } = "preview";
        public int? Completed { get; set; }
        public int? Total { get; set; }
        public int WindowBatches { get; set; }
        public WriteOutcome Outcome { get; set; }
        private string? lastWait;
        private DateTimeOffset waitStarted;
        private DateTimeOffset lastReported;

        public string Counts => $"completed={Completed?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; remaining={(Total - Completed)?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; window_batches={WindowBatches}";

        public async Task ReportWaitAsync(TextWriter output, string kind, string detail)
        {
            var now = MicrosecondUtcNow();
            var signature = kind + ";" + detail;
            if (lastWait is null) waitStarted = now;
            if (lastWait != signature || now - lastReported >= TimeSpan.FromSeconds(30))
            {
                await output.WriteLineAsync($"{kind} utc={now:O}; stage={Stage}; {Counts}; wait_seconds={(now - waitStarted).TotalSeconds:F1}; {detail}; no active write transaction.");
                lastReported = now;
            }
            lastWait = signature;
        }

        public void EndWait() => lastWait = null;

        public string StopMessage(Exception error) =>
            $"STOPPED utc={MicrosecondUtcNow():O}; stage={Stage}; {Counts}; type={error.GetType().Name}; reason={error.Message}; write_outcome={Outcome}; " + (Outcome switch
            {
                WriteOutcome.RolledBack => "uncommitted transaction rollback confirmed; prior committed batches retained.",
                WriteOutcome.Unknown or WriteOutcome.Active => "transaction/commit outcome unknown; no automatic replay; inspect deterministic chains before further writes.",
                _ => "no active write transaction; prior committed batches retained."
            });
    }

    internal static bool IsRetryableBatchLock(Exception error, bool rollbackConfirmed, bool commitAttempted) =>
        rollbackConfirmed && !commitAttempted && error is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable };
    private sealed record Marker(string Contract, string Digest, string SourceDigest, string PlanDigest,
        DateTimeOffset Cutoff, int Batches, int Trades, object[] PerChild,
        string Model, string CounterPolicy, object[] RecordedFeeProfiles);

    internal static string? ValidateArguments(string[] args)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!seen.Add(args[i])) return "Duplicate argument.";
            switch (args[i])
            {
                case CommandFlag:
                case "--apply":
                    break;
                case "--approved-contract-digest":
                    if (++i >= args.Length || args[i] != ApprovalDigest) return "Approval digest mismatch.";
                    break;
                default:
                    return "Unknown argument.";
            }
        }
        if (!seen.Contains(CommandFlag)) return "Command flag missing.";
        if (seen.Contains("--apply") && !seen.Contains("--approved-contract-digest")) return "Apply requires the approved digest.";
        return null;
    }

    internal static Plan BuildPlan(IReadOnlyList<Source> sources)
    {
        Require(sources.Select(s => s.RunId).Distinct().Count() == sources.Count, "Duplicate source run.");
        var entries = new List<Entry>();
        foreach (var group in sources.GroupBy(s => s.ParentId).OrderBy(g => g.Key.ToString("D"), StringComparer.Ordinal))
        {
            Require(group.Key == Up4 || group.Key == Up8, "Source parent outside allowlist.");
            var ordered = group.OrderBy(s => s.EnteredAt).ThenBy(s => s.RunId.ToString("D"), StringComparer.Ordinal).ToArray();
            var outcomes = group.OrderBy(s => s.SettledAt).ThenBy(s => s.EnteredAt)
                .ThenBy(s => s.RunId.ToString("D"), StringComparer.Ordinal).ToArray();
            var cursor = 0;
            var counter = 0;
            foreach (var source in ordered)
            {
                Require(source.EnteredAt < CutoffUtc && source.SettledAt >= source.EnteredAt &&
                    source.Gross != 0 && source.Spent > 0 && source.Shares > 0 &&
                    source.Payout - source.Spent == source.Gross && source.Gross - source.ParentFee == source.Net,
                    $"Invalid source outcome {source.RunId:D}.");
                while (cursor < outcomes.Length && outcomes[cursor].SettledAt < source.EnteredAt)
                {
                    counter = Math.Max(0, checked(counter - Math.Sign(outcomes[cursor++].Gross)));
                }
                if (counter == 0) continue;
                Require(source.ChainConsistent, $"Inconsistent native source chain {source.RunId:D}.");
                Require(source.SettlementPrice is 0m or 1m &&
                    source.Payout == source.Shares * source.SettlementPrice, "Settlement outcome mismatch.");
                foreach (var child in Children.Where(c => c.ParentId == group.Key))
                {
                    var multiplier = Math.Min(counter, child.Cap);
                    var shares = checked(source.Shares * multiplier);
                    var spent = checked(source.Spent * multiplier);
                    var average = source.Spent / source.Shares;
                    var fee = PolymarketFeeCalculator.CalculatePlatformFee(shares, average, FeeLiquidityRole.Taker,
                        new PolymarketClobMarketInfo(source.ConditionId, null, null,
                            new PolymarketClobFeeSchedule(source.Rate, source.Exponent, source.TakerOnly), "{}"));
                    Require(fee.Status == FeeAccountingStatus.Calculated && fee.FeeUsd.HasValue,
                        $"Unavailable fee for {source.RunId:D}: {fee.UnavailableReason}");
                    entries.Add(new Entry(child, source, counter, multiplier, spent, shares, average,
                        fee.FeeUsd!.Value, shares * source.SettlementPrice));
                }
            }
        }
        var orderedSources = sources.OrderBy(s => s.ParentId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(s => s.EnteredAt).ThenBy(s => s.RunId.ToString("D"), StringComparer.Ordinal).ToArray();
        var sourceDigest = Hash(string.Join("\n", orderedSources.Select(s => s.Fingerprint)));
        var planDigest = Hash(JsonSerializer.Serialize(entries.Select(e => new
        {
            child = e.Child.Id, parent_run = e.Source.RunId, source = e.Source.Fingerprint,
            e.Counter, e.Multiplier, e.Spent, e.Shares, e.AveragePrice, e.Fee, e.Payout
        }), JsonOptions));
        return new Plan(orderedSources, entries, sourceDigest, planDigest);
    }

    internal static Metrics CalculateMetrics(IEnumerable<Entry> entries)
    {
        var array = entries.ToArray();
        return new Metrics(array.Length, array.Count(e => e.Gross > 0), array.Count(e => e.Gross < 0),
            array.Sum(e => e.Spent), array.Sum(e => e.Payout), array.Sum(e => e.Fee));
    }

    internal static Guid DeterministicId(Guid child, Guid parentRun, string role) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{EvidenceVersion}|{child:D}|{parentRun:D}|{role}")).AsSpan(0, 16));

    internal static string Hash(string value) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // Native JSON below is a field-preserving audit snapshot, not the financial calculation model.
    // Every business amount above is typed decimal and every changed database field is enumerated here.
    internal static NativeChain CreateChain(Entry entry, Plan plan, DateTimeOffset reconstructedAt)
    {
        var source = JsonNode.Parse(entry.Source.NativeSnapshot)!.AsObject();
        JsonObject Copy(string key) => source[key]!.DeepClone().AsObject();
        var signal = Copy("signal");
        var order = Copy("order");
        var fill = Copy("fill");
        var run = Copy("run");
        var position = Copy("position");
        var settlement = Copy("settlement");
        var audit = JsonSerializer.SerializeToNode(new
        {
            version = EvidenceVersion, execution_source = ExecutionSource, classification = "ResearchOnly",
            ordinary_paper_metrics_included = true, model = "sufficient_depth_parent_average_full_fill",
            contract_id = ContractId, approved_contract_digest = ApprovalDigest,
            source_digest = plan.SourceDigest, plan_digest = plan.PlanDigest,
            parent_chain_digest = entry.Source.Fingerprint, parent_run_id = entry.Source.RunId,
            parent_strategy_id = entry.Source.ParentId, child_strategy_id = entry.Child.Id,
            parent_signal_id = signal["id"]!.GetValue<string>(), parent_order_id = order["id"]!.GetValue<string>(),
            parent_fill_id = fill["id"]!.GetValue<string>(), parent_position_id = position["id"]!.GetValue<string>(),
            parent_settlement_id = settlement["id"]!.GetValue<string>(), cutoff_utc = CutoffUtc,
            pre_entry_loss_diff = entry.Counter, cap = entry.Child.Cap, multiplier = entry.Multiplier,
            parent_spent = entry.Source.Spent, parent_shares = entry.Source.Shares,
            model_average_price = entry.AveragePrice, parent_fee_source = entry.Source.FeeSource,
            reconstructed_at_utc = reconstructedAt, venue_execution_proven = false
        }, JsonOptions)!.AsObject();
        var price = Math.Round(entry.AveragePrice, 8, MidpointRounding.AwayFromZero);
        Set(signal, ("id", entry.Id("signal")), ("trader_wallet", entry.Child.Wallet),
            ("accepted", true), ("decision", "accepted_eth_progress34_modeled_history"),
            ("proposed_paper_price", price), ("proposed_size_shares", entry.Shares),
            ("proposed_notional_usd", entry.Spent));
        signal["raw_context_json"] = new JsonObject { ["history_model"] = audit.DeepClone(), ["parent_evidence"] = signal["raw_context_json"]?.DeepClone() };
        Set(order, ("id", entry.Id("order")), ("signal_id", entry.Id("signal")), ("strategy_id", entry.Child.Id),
            ("copied_trader_wallet", entry.Child.Wallet), ("status", "Filled"), ("side", "Buy"),
            ("price", price), ("size_shares", entry.Shares), ("notional_usd", entry.Spent),
            ("cancelled_at_utc", null), ("correlation_id", null), ("execution_source", ExecutionSource));
        order["raw_decision_json"] = new JsonObject { ["history_model"] = audit.DeepClone(), ["parent_evidence"] = order["raw_decision_json"]?.DeepClone() };
        Set(fill, ("id", entry.Id("fill")), ("paper_order_id", entry.Id("order")),
            ("price", price), ("size_shares", entry.Shares), ("realized_pnl_usd", 0m), ("net_realized_pnl_usd", -entry.Fee));
        var fillAudit = new JsonObject { ["history_model"] = audit.DeepClone(), ["parent_evidence"] = fill["evidence"]?.DeepClone() };
        Set(fill, ("evidence", fillAudit.ToJsonString()));
        Set(run, ("id", entry.Id("run")), ("strategy_id", entry.Child.Id), ("status", "Settled"),
            ("entry_price", price), ("stake_usd", entry.Spent), ("size_shares", entry.Shares),
            ("signal_id", entry.Id("signal")), ("paper_order_id", entry.Id("order")),
            ("settlement_value_usd", entry.Payout), ("realized_pnl_usd", entry.Gross),
            ("net_realized_pnl_usd", entry.Net), ("skip_reason", null), ("retention_scope", "PaperOnly"));
        run["skip_diagnostics_json"] = new JsonObject { ["history_model"] = audit.DeepClone() };
        Set(position, ("id", entry.Id("position")), ("copied_trader_wallet", entry.Child.Wallet),
            ("size_shares", 0m), ("average_price", 0m), ("estimated_value_usd", 0m),
            ("unrealized_pnl_usd", 0m), ("fee_usd", 0m), ("net_unrealized_pnl_usd", 0m),
            ("fee_accounting_status", "Calculated"), ("fee_liquidity_role", "Unknown"),
            ("fee_calculation_source", ""), ("fee_rate", null), ("fee_exponent", null),
            ("fee_taker_only", null), ("fee_calculated_at_utc", null));
        Set(settlement, ("id", entry.Id("settlement")), ("copied_trader_wallet", entry.Child.Wallet),
            ("settled_size_shares", entry.Shares), ("average_price", price), ("cost_basis_usd", entry.Spent),
            ("settlement_value_usd", entry.Payout), ("realized_pnl_usd", entry.Gross),
            ("net_realized_pnl_usd", entry.Net), ("settlement_source", ExecutionSource));
        foreach (var row in new[] { fill, run, settlement })
            Set(row, ("fee_usd", entry.Fee), ("fee_accounting_status", "Calculated"), ("fee_liquidity_role", "Taker"),
                ("fee_calculation_source", EvidenceVersion + ":" + PolymarketFeeCalculationConstants.FeeCurveCalculationSource),
                ("fee_rate", entry.Source.Rate), ("fee_exponent", entry.Source.Exponent),
                ("fee_taker_only", entry.Source.TakerOnly), ("fee_calculated_at_utc", reconstructedAt));
        return new NativeChain(entry.Child.Id, entry.Source.MarketId, entry.Child.Wallet, entry.Source.AssetId, entry.Source.ConditionId,
            signal, order, fill, run, position, settlement);
    }

    private static void Set(JsonObject row, params (string Field, object? Value)[] values)
    {
        foreach (var (field, value) in values)
        {
            Require(row.ContainsKey(field), $"Native schema field missing: {field}");
            row[field] = JsonSerializer.SerializeToNode(value, JsonOptions);
        }
    }

    internal static async Task<IReadOnlyList<Source>> ReadSourcesAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, CancellationToken token, Guid? onlyRun = null)
    {
        var ids = new List<Guid>();
        if (onlyRun.HasValue) ids.Add(onlyRun.Value);
        else
        {
            await using var command = Command("""
SELECT id FROM public.strategy_market_paper_runs
WHERE strategy_id=ANY(@Parents) AND status='Settled' AND entered_at_utc<@Cutoff
ORDER BY strategy_id,entered_at_utc,id;
""", connection, transaction);
            command.Parameters.AddWithValue("Parents", new[] { Up4, Up8 });
            command.Parameters.AddWithValue("Cutoff", CutoffUtc);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) ids.Add(reader.GetGuid(0));
        }
        var result = new List<Source>();
        // The 2830-chain query exceeded the15s Production limit. Bound native/TOAST
        // reads using exact primary keys; no larger timeout or schema change.
        foreach (var chunk in ids.Chunk(64))
            result.AddRange(await ReadSourceChunkAsync(connection, transaction, chunk, token));
        return result;
    }

    private static async Task<IReadOnlyList<Source>> ReadSourceChunkAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, Guid[] runIds, CancellationToken token)
    {
        const string sql = """
SELECT r.id,r.strategy_id,r.market_id,r.selected_asset_id,r.condition_id,r.selected_outcome,
 r.entered_at_utc,r.settled_at_utc,r.stake_usd,r.size_shares,r.settlement_price,
 r.settlement_value_usd,r.realized_pnl_usd,r.fee_usd,r.fee_rate,r.fee_exponent,r.fee_taker_only,
 r.fee_calculation_source,r.net_realized_pnl_usd,
 (o.strategy_id=r.strategy_id AND o.signal_id=r.signal_id AND o.asset_id=r.selected_asset_id
  AND o.condition_id=r.condition_id AND o.outcome=r.selected_outcome AND o.side='Buy' AND o.status='Filled'
  AND o.execution_source='btc_updown5m_fak_taker_paper'
  AND s.condition_id=r.condition_id AND s.asset_id=r.selected_asset_id
  AND f.size_shares=r.size_shares AND ps.settled_size_shares=r.size_shares
  AND ps.cost_basis_usd=r.stake_usd AND ps.settlement_value_usd=r.settlement_value_usd
  AND ps.realized_pnl_usd=r.realized_pnl_usd AND ps.won=(r.realized_pnl_usd>0)
  AND ps.outcome=r.selected_outcome AND ps.condition_id=r.condition_id
  AND p.condition_id=r.condition_id AND p.outcome=r.selected_outcome
  AND r.fee_accounting_status='Calculated' AND r.fee_liquidity_role='Taker'
  AND ps.fee_usd=r.fee_usd AND ps.net_realized_pnl_usd=r.net_realized_pnl_usd),
 jsonb_build_object('signal',to_jsonb(s),'order',to_jsonb(o),'fill',to_jsonb(f),
    'run',to_jsonb(r),'position',to_jsonb(p),'settlement',to_jsonb(ps))::text,
 r.entry_price
FROM public.strategy_market_paper_runs r
JOIN public.paper_orders o ON o.id=r.paper_order_id
JOIN public.signals s ON s.id=r.signal_id
JOIN public.paper_fills f ON f.paper_order_id=o.id
JOIN public.paper_positions p ON p.copied_trader_wallet=o.copied_trader_wallet AND p.asset_id=o.asset_id
JOIN public.paper_position_settlements ps ON ps.copied_trader_wallet=o.copied_trader_wallet AND ps.asset_id=o.asset_id
WHERE r.strategy_id=ANY(@Parents) AND r.status='Settled' AND r.entered_at_utc<@Cutoff
 AND r.id=ANY(@RunIds)
ORDER BY r.strategy_id,r.entered_at_utc,r.id;
""";
        await using var command = Command(sql, connection, transaction);
        command.Parameters.AddWithValue("Parents", new[] { Up4, Up8 });
        command.Parameters.AddWithValue("Cutoff", CutoffUtc);
        command.Parameters.AddWithValue("RunIds", runIds);
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new List<Source>();
        while (await reader.ReadAsync(token))
            result.Add(new Source(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetFieldValue<DateTimeOffset>(7), reader.GetDecimal(8), reader.GetDecimal(9),
                reader.GetDecimal(10), reader.GetDecimal(11), reader.GetDecimal(12), reader.GetDecimal(13),
                reader.IsDBNull(14) ? null : reader.GetDecimal(14), reader.IsDBNull(15) ? null : reader.GetInt32(15),
                reader.IsDBNull(16) ? null : reader.GetBoolean(16), reader.GetString(17), reader.GetDecimal(18),
                !reader.IsDBNull(19) && reader.GetBoolean(19), reader.GetString(20), reader.GetDecimal(21)));
        return result;
    }


    internal static async Task<Baseline> ReadHealthAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        CancellationToken token)
    {
        const string sql = """
WITH expected AS (SELECT * FROM jsonb_to_recordset(@Children::jsonb)
 AS x(id uuid,assignment_id uuid,parent_id uuid,code text,name text,cap int))
SELECT
 (SELECT count(*) FROM public.service_heartbeats WHERE service_name='PolyCopyTrader.Service'
    AND status='Running' AND mode='Live' AND version=@Version AND last_error IS NULL
    AND last_heartbeat_utc BETWEEN clock_timestamp()-interval '120 seconds' AND clock_timestamp()),
 (SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND wait_event_type='Lock'),
 (SELECT count(*) FROM expected e JOIN public.strategies s ON s.id=e.id AND s.code=e.code AND s.name=e.name
   JOIN public.strategy_child_parent_assignments a ON a.id=e.assignment_id AND a.child_strategy_id=e.id
    AND a.parent_strategy_id=e.parent_id AND a.child_mode='LossDiffPositive' AND a.asset_symbol='ETH'
    AND a.lookback_hours=0 AND a.ended_at_utc IS NULL AND a.assigned_at_utc=@Cutoff
   JOIN public.strategy_loss_diff_states d ON d.child_strategy_id=e.id AND d.parent_strategy_id=e.parent_id
    AND d.mode='LossDiffPositive' AND d.threshold=1 AND d.started_at_utc=@Cutoff
   WHERE s.enabled AND NOT s.paused AND NOT s.auto_live_paused AND NOT s.live_stakes AND s.live_enabled_at_utc IS NULL),
 (SELECT count(*) FROM public.strategies WHERE (id,code) IN (
  ('b7c50005-0000-4000-8137-000000000104'::uuid,'eth_up_down_5m_up_bps_4_fak_premarket'),
  ('b7c50005-0000-4000-8137-000000000108'::uuid,'eth_up_down_5m_up_bps_8_fak_premarket'))
  AND enabled AND NOT paused AND NOT auto_live_paused AND NOT live_stakes),
 (SELECT count(*) FROM public.schema_migration_history WHERE migration_id=@Migration AND semantic_checksum=@Checksum),
 (SELECT count(*) FROM public.dashboard_projection_control WHERE singleton_id=1 AND initialized AND status='Running' AND last_error IS NULL),
 (SELECT count(*) FROM public.strategy_child_parent_assignments WHERE parent_strategy_id=ANY(@Ids) AND ended_at_utc IS NULL),
 (SELECT count(*) FROM public.dashboard_projection_reconciliation_queue WHERE strategy_id=ANY(@Ids) AND last_error IS NOT NULL),
 (SELECT count(*) FROM public.strategy_loss_diff_parent_events WHERE child_strategy_id=ANY(@Ids) AND parent_entered_at_utc<@Cutoff),
 pg_is_in_recovery(),
 (SELECT jsonb_agg(to_jsonb(s)-ARRAY['updated_at_utc','paper_lost_counter'] ORDER BY s.id)::text
    FROM public.strategies s WHERE s.id=ANY(@AllIds)),
 (SELECT count(*) FROM public.strategies WHERE lower(code)=ANY(@Codes)),
 (SELECT count(*) FROM public.strategy_market_paper_skip_tombstones WHERE strategy_id=ANY(@Ids) AND archive_format_version IS DISTINCT FROM 1);
""";
        await using var command = Command(sql, connection, transaction);
        command.Parameters.AddWithValue("Children", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(Children, JsonOptions));
        command.Parameters.AddWithValue("Version", ServiceVersion);
        command.Parameters.AddWithValue("Cutoff", CutoffUtc);
        command.Parameters.AddWithValue("Migration", PostgresLossDiffPositiveProgressStrategySchemaMigration.Id);
        command.Parameters.AddWithValue("Checksum", PostgresLossDiffPositiveProgressStrategySchemaMigration.SemanticChecksum);
        command.Parameters.AddWithValue("Ids", Children.Select(c => c.Id).ToArray());
        command.Parameters.AddWithValue("AllIds", Children.Select(c => c.Id).Concat(new[] { Up4, Up8 }).ToArray());
        command.Parameters.AddWithValue("Codes", Children.Select(c=>c.Code).ToArray());
        string settings;
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            Require(await reader.ReadAsync(token), "Health snapshot absent.");
            Require(reader.GetInt64(0) == 1, "Exact service build/heartbeat/error guard failed.");
            // The independent lock observation is transient, not a baseline/fatal condition.
            // It is enforced outside write transactions by WaitForReadyAsync.
            Require(reader.GetInt64(2) == 34 && reader.GetInt64(3) == 2, "Exact Paper family/state/assignment guard failed.");
            Require(reader.GetInt64(4) == 1, "Creation migration checksum mismatch.");
            Require(reader.GetInt64(5) == 1 && reader.GetInt64(7) == 0, "Projection health guard failed.");
            Require(reader.GetInt64(6) == 0, "A target strategy has active descendants outside this scope.");
            Require(reader.GetInt64(8) == 0, "Historical counter events exist outside the approved zero-start policy.");
            Require(!reader.GetBoolean(9), "Replica is not an apply target.");
            settings = Hash(reader.GetString(10));
            Require(reader.GetInt64(11)==34,"Case-insensitive strategy wallet alias outside allowlist.");
            Require(reader.GetInt64(12)==0,"An incomplete legacy archive can block native dependency writes.");
        }
        await using var schema = Command("""
SELECT jsonb_build_object(
 'columns',(SELECT jsonb_agg(to_jsonb(x) ORDER BY x.table_name,x.ordinal_position)
 FROM (SELECT table_name,column_name,ordinal_position,data_type,udt_name,is_nullable,column_default
 FROM information_schema.columns WHERE table_schema='public' AND table_name=ANY(@Tables)) x),
 'triggers',(SELECT jsonb_agg(jsonb_build_array(c.relname,t.tgname,t.tgenabled,pg_get_triggerdef(t.oid),pg_get_functiondef(t.tgfoid)) ORDER BY c.relname,t.tgname)
 FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
 WHERE n.nspname='public' AND c.relname=ANY(@Tables) AND NOT t.tgisinternal),
 'migrations',(SELECT jsonb_agg(jsonb_build_array(migration_id,semantic_checksum) ORDER BY migration_id) FROM public.schema_migration_history),
 'dependency_functions',(SELECT jsonb_agg(jsonb_build_array(p.proname,pg_get_functiondef(p.oid)) ORDER BY p.proname,p.oid)
 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.proname IN
 ('restore_archived_strategy_runs_for_dependency_core','restore_archived_strategy_runs_for_dependency','lock_strategy_run_retention_dependency'))
)::text;
""", connection, transaction);
        schema.Parameters.AddWithValue("Tables", new[] { "signals", "paper_orders", "paper_fills",
            "strategy_market_paper_runs", "paper_positions", "paper_position_settlements" });
        return new Baseline(settings, Hash((string)(await schema.ExecuteScalarAsync(token))!));
    }

    private static async Task RequireBaselineAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Baseline baseline, CancellationToken token)
    {
        Require(await ReadHealthAsync(connection, transaction, token) == baseline, "Strategy settings or schema changed since preview.");
    }

    internal static async Task<LockSnapshot> ReadLocksAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = Command("""
WITH waiting AS MATERIALIZED (
 SELECT pid,application_name,wait_event,pg_blocking_pids(pid) AS blocking_pids
 FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND wait_event_type='Lock'
)
SELECT (SELECT count(*) FROM waiting),
 COALESCE((SELECT jsonb_agg(to_jsonb(x) ORDER BY pid)::text FROM (SELECT * FROM waiting ORDER BY pid LIMIT 30) x),'[]');
""", connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        Require(await reader.ReadAsync(token), "Lock snapshot absent.");
        return new LockSnapshot(reader.GetInt64(0), reader.GetString(1));
    }

    internal static async Task WaitForReadyAsync(NpgsqlConnection connection, Baseline baseline,
        Progress progress, TextWriter output, CancellationToken token)
    {
        while (true)
        {
            // Fatal guards are evaluated even while another session waits for a lock.
            await RequireBaselineAsync(connection, null, baseline, token);
            var locks = await ReadLocksAsync(connection, token);
            if (locks.Waiting == 0) return;
            await progress.ReportWaitAsync(output, "WAITING_LOCKS", $"waiting={locks.Waiting}; participants={locks.Participants}");
            await Task.Delay(PollInterval, token);
        }
    }

    internal static void RequireFrozenPlan(Plan plan)
    {
        Require(plan.SourceDigest == FrozenSourceDigest && plan.PlanDigest == FrozenPlanDigest,
            "Source or accounting manifest changed from the independently reviewed frozen preview.");
    }

    private static async Task ValidateUniverseAsync(NpgsqlConnection connection, Plan plan, CancellationToken token)
    {
        RequireFrozenPlan(plan);
        Require(plan.Sources.Count == 2830 && plan.Entries.Count == 31612, "Approved source/target scale changed.");
        foreach (var (parent, count, wins, eligible, max) in new[]
                 { (Up4, 1749, 1003, 1132, 16), (Up8, 1081, 610, 750, 18) })
        {
            var sources = plan.Sources.Where(s => s.ParentId == parent).ToArray();
            var entries = plan.Entries.Where(e => e.Source.ParentId == parent).ToArray();
            Require(sources.Length == count && sources.Count(s => s.Gross > 0) == wins, "Parent outcome counts changed.");
            Require(entries.Select(e => e.Source.RunId).Distinct().Count() == eligible && entries.Max(e => e.Counter) == max,
                "Approved causal membership scale changed.");
        }
        var eligibleSources = plan.Entries.Select(e => e.Source).DistinctBy(s => s.RunId).ToArray();
        var retrospective = "historical-current-paper-model-v1:" + PolymarketFeeCalculationConstants.FeeCurveCalculationSource;
        Require(eligibleSources.All(s => s.Rate == .07m && s.Exponent == 1 && s.TakerOnly == true &&
            (s.FeeSource == retrospective || s.FeeSource == PolymarketFeeCalculationConstants.FeeCurveCalculationSource)),
            "Recorded approved fee profile changed.");
        Require(eligibleSources.Count(s => s.FeeSource == retrospective) == 1054, "Retrospective fee-profile population changed.");
        await using var command = Command("""
SELECT strategy_id,count(*),sum(stake_usd),sum(size_shares),sum(settlement_value_usd),
 sum(realized_pnl_usd),sum(fee_usd),sum(net_realized_pnl_usd)
FROM public.strategy_market_paper_runs WHERE strategy_id=ANY(@Parents) AND entered_at_utc<@Cutoff AND status='Settled'
GROUP BY strategy_id ORDER BY strategy_id;
""", connection);
        command.Parameters.AddWithValue("Parents", new[] { Up4, Up8 });
        command.Parameters.AddWithValue("Cutoff", CutoffUtc);
        var groups = 0;
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            groups++;
            var sources = plan.Sources.Where(s => s.ParentId == reader.GetGuid(0)).ToArray();
            Require(reader.GetInt64(1) == sources.Length && reader.GetDecimal(2) == sources.Sum(s => s.Spent) &&
                reader.GetDecimal(3) == sources.Sum(s => s.Shares) && reader.GetDecimal(4) == sources.Sum(s => s.Payout) &&
                reader.GetDecimal(5) == sources.Sum(s => s.Gross) && reader.GetDecimal(6) == sources.Sum(s => s.ParentFee) &&
                reader.GetDecimal(7) == sources.Sum(s => s.Net), "Independent SQL/source decimal aggregate mismatch.");
        }
        Require(groups == 2, "Missing exact source aggregate.");
    }

    private static async Task<string> ReadProtectedDigestAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken token)
    {
        // Supplementary own-write attribution under mandatory READ COMMITTED.
        // Existing INSERT trigger call paths do not delete/update these rows;
        // archive restoration is separately excluded while holding its shared gate.
        // Concurrent ordinary service activity has a different xmin and is not
        // misreported as a write by this command.
        const string sql = """
WITH protected AS (
 SELECT 'strategy' kind,s.id::text key,to_jsonb(s) value,s.xmin row_xid FROM public.strategies s WHERE s.id=ANY(@AllIds)
 UNION ALL SELECT 'state',d.child_strategy_id::text,to_jsonb(d),d.xmin FROM public.strategy_loss_diff_states d WHERE d.child_strategy_id=ANY(@Ids)
 UNION ALL SELECT 'assignment',a.id::text,to_jsonb(a),a.xmin FROM public.strategy_child_parent_assignments a WHERE a.child_strategy_id=ANY(@Ids)
 UNION ALL SELECT 'event',e.child_strategy_id::text||e.parent_run_id::text,to_jsonb(e),e.xmin FROM public.strategy_loss_diff_parent_events e WHERE e.child_strategy_id=ANY(@Ids)
 UNION ALL SELECT 'live',o.id::text,to_jsonb(o),o.xmin FROM public.live_orders o WHERE o.strategy_id=ANY(@Ids)
 UNION ALL SELECT 'shadow',d.correlation_id::text,to_jsonb(d),d.xmin FROM public.paper_live_shadow_decisions d WHERE d.strategy_id=ANY(@Ids)
 UNION ALL SELECT 'organic_run',r.id::text,to_jsonb(r),r.xmin FROM public.strategy_market_paper_runs r WHERE r.strategy_id=ANY(@Ids) AND r.created_at_utc>=@Cutoff
 UNION ALL SELECT 'organic_order',o.id::text,to_jsonb(o),o.xmin FROM public.paper_orders o WHERE o.strategy_id=ANY(@Ids) AND o.created_at_utc>=@Cutoff
 UNION ALL SELECT 'organic_signal',s.id::text,to_jsonb(s),s.xmin FROM public.signals s WHERE s.trader_wallet=ANY(@Wallets) AND s.created_at_utc>=@Cutoff
 UNION ALL SELECT 'organic_fill',f.id::text,to_jsonb(f),f.xmin FROM public.paper_orders o JOIN public.paper_fills f ON f.paper_order_id=o.id WHERE o.strategy_id=ANY(@Ids) AND o.created_at_utc>=@Cutoff
 UNION ALL SELECT 'organic_position',p.id::text,to_jsonb(p),p.xmin FROM public.paper_positions p WHERE p.copied_trader_wallet=ANY(@Wallets) AND EXISTS(SELECT 1 FROM public.paper_orders o WHERE o.strategy_id=ANY(@Ids) AND o.asset_id=p.asset_id AND o.copied_trader_wallet=p.copied_trader_wallet AND o.created_at_utc>=@Cutoff)
 UNION ALL SELECT 'organic_settlement',ps.id::text,to_jsonb(ps),ps.xmin FROM public.paper_position_settlements ps WHERE ps.copied_trader_wallet=ANY(@Wallets) AND EXISTS(SELECT 1 FROM public.paper_orders o WHERE o.strategy_id=ANY(@Ids) AND o.asset_id=ps.asset_id AND o.copied_trader_wallet=ps.copied_trader_wallet AND o.created_at_utc>=@Cutoff)
)
SELECT encode(sha256(convert_to(COALESCE(jsonb_agg(jsonb_build_array(kind,key,value) ORDER BY kind,key)::text,'[]'),'UTF8')),'hex') FROM protected WHERE row_xid=pg_current_xact_id_if_assigned()::xid;
""";
        await using var command = Command(sql, connection, transaction);
        command.Parameters.AddWithValue("Ids", Children.Select(c => c.Id).ToArray());
        command.Parameters.AddWithValue("AllIds", Children.Select(c => c.Id).Concat(new[] { Up4, Up8 }).ToArray());
        command.Parameters.AddWithValue("Wallets", Children.Select(c => c.Wallet).ToArray());
        command.Parameters.AddWithValue("Cutoff", CutoffUtc);
        return Hash((string)(await command.ExecuteScalarAsync(token))!);
    }

    private static async Task<ProjectionSnapshot> ReadProjectionQueuesAsync(NpgsqlConnection connection,
        IReadOnlyList<NativeChain> chains, CancellationToken token, bool allChildren = false,
        NpgsqlTransaction? transaction = null, bool includeReconciliation = false)
    {
        var ids = chains.SelectMany(c => new[] { c.Signal, c.Order, c.Fill, c.Run, c.Position, c.Settlement })
            .Select(r => r["id"]!.GetValue<Guid>()).ToArray();
        await using var command = Command("""
SELECT
 (SELECT count(*) FROM public.dashboard_projection_events WHERE strategy_id=ANY(@Children) AND (@AllChildren OR source_id=ANY(@Ids))),
 (SELECT count(*) FROM public.paper_copied_trader_performance_refresh_queue WHERE copied_trader_wallet=ANY(@Wallets)),
 (SELECT count(*) FROM public.paper_copied_trader_performance_refresh_inflight WHERE copied_trader_wallet=ANY(@Wallets)),
 (SELECT count(*) FROM public.dashboard_projection_reconciliation_queue WHERE @Reconciliation AND strategy_id=ANY(@Children));
""", connection, transaction);
        command.Parameters.AddWithValue("Ids", ids);
        command.Parameters.AddWithValue("AllChildren", allChildren);
        command.Parameters.AddWithValue("Reconciliation", includeReconciliation);
        command.Parameters.AddWithValue("Children", allChildren ? Children.Select(c => c.Id).ToArray() : chains.Select(c => c.ChildId).Distinct().ToArray());
        command.Parameters.AddWithValue("Wallets", allChildren ? Children.Select(c => c.Wallet).ToArray() : chains.Select(c => c.Wallet).Distinct().ToArray());
        await using var reader = await command.ExecuteReaderAsync(token);
        Require(await reader.ReadAsync(token), "Projection queue state absent.");
        return new ProjectionSnapshot(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    private static string QueueDetail(ProjectionSnapshot queues) =>
        $"events={queues.Events}; queued={queues.Queued}; inflight={queues.Inflight}; reconciliation={queues.Reconciliation}";

    private static async Task WaitForProjectionsAsync(NpgsqlConnection connection, IReadOnlyList<NativeChain> chains,
        Baseline baseline, Progress progress, TextWriter output, CancellationToken token, bool allChildren = false)
    {
        while (true)
        {
            await WaitForReadyAsync(connection, baseline, progress, output, token);
            var queues = await ReadProjectionQueuesAsync(connection, chains, token, allChildren);
            if (!queues.Pending)
            {
                progress.EndWait();
                return;
            }
            await progress.ReportWaitAsync(output, "WAITING_PROJECTIONS", QueueDetail(queues));
            await Task.Delay(PollInterval, token);
        }
    }

    private static async Task VerifyFinancialTotalsAsync(NpgsqlConnection connection, Plan plan, CancellationToken token,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = Command("""
SELECT strategy_id,count(*),count(*) FILTER(WHERE realized_pnl_usd>0),count(*) FILTER(WHERE realized_pnl_usd<0),
 sum(stake_usd),sum(settlement_value_usd),sum(fee_usd),sum(realized_pnl_usd),sum(net_realized_pnl_usd)
FROM public.strategy_market_paper_runs
WHERE strategy_id=ANY(@Ids) AND entered_at_utc<@Cutoff AND status='Settled'
GROUP BY strategy_id ORDER BY strategy_id;
""", connection, transaction);
        command.Parameters.AddWithValue("Ids", Children.Select(c => c.Id).ToArray());
        command.Parameters.AddWithValue("Cutoff", CutoffUtc);
        await using var reader = await command.ExecuteReaderAsync(token);
        var count = 0;
        while (await reader.ReadAsync(token))
        {
            count++;
            var expected = CalculateMetrics(plan.Entries.Where(e => e.Child.Id == reader.GetGuid(0)));
            var actual = new Metrics(checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2)),
                checked((int)reader.GetInt64(3)), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6));
            Require(actual == expected && reader.GetDecimal(7) == expected.Gross && reader.GetDecimal(8) == expected.Net,
                $"Native accounting total mismatch for {reader.GetGuid(0):D}.");
        }
        Require(count == 34, "Missing per-child native accounting totals.");
    }

    private static async Task VerifyOrdinaryProjectionsAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken token)
    {
        // Compare the ordinary lifetime settled contribution, including concurrent organic trades.
        // An unconsumed organic event is not a discrepancy and is waited for before this snapshot.
        await using var command = Command("""
SELECT r.strategy_id,count(*) AS trades,COALESCE(sum(r.realized_pnl_usd),0),COALESCE(sum(r.net_realized_pnl_usd),0),
 d.settled_runs_count,d.realized_pnl_usd,d.net_realized_pnl_usd,
 d.win_rate_pct,round(COALESCE(100.0*count(*) FILTER(WHERE r.realized_pnl_usd>0)
   /NULLIF(count(*) FILTER(WHERE r.realized_pnl_usd<>0),0),0),8),
 d.net_closed_roi_pct,round(COALESCE(100*sum(r.net_realized_pnl_usd)
   /NULLIF(sum(r.stake_usd)+sum(r.fee_usd),0),0),8)
FROM public.strategy_market_paper_runs r
JOIN public.dashboard_strategy_performance_snapshots d ON d.strategy_id=r.strategy_id
WHERE r.strategy_id=ANY(@Ids) AND r.status='Settled'
GROUP BY r.strategy_id,d.settled_runs_count,d.realized_pnl_usd,d.net_realized_pnl_usd,
 d.win_rate_pct,d.net_closed_roi_pct;
""", connection, transaction);
        command.Parameters.AddWithValue("Ids", Children.Select(c => c.Id).ToArray());
        var count = 0;
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            count++;
            Require(reader.GetInt64(1) == reader.GetInt32(4) && reader.GetDecimal(2) == reader.GetDecimal(5) &&
                !reader.IsDBNull(6) && reader.GetDecimal(3) == reader.GetDecimal(6) &&
                reader.GetDecimal(7) == reader.GetDecimal(8) && !reader.IsDBNull(9) &&
                reader.GetDecimal(9) == reader.GetDecimal(10),
                $"Ordinary projection has not reconciled for {reader.GetGuid(0):D}.");
        }
        Require(count == 34, "Missing ordinary projection.");
    }

    // Check all34 queues and totals in the SAME snapshot. A new organic event
    // committed later belongs to a later snapshot, not a false discrepancy here.
    private static async Task CompleteWhenReconciledAsync(NpgsqlConnection connection, Plan plan,
        Baseline baseline, string marker, bool writeMarker, Progress progress, TextWriter output, CancellationToken token)
    {
        progress.Stage = "final_reconciliation";
        while (true)
        {
            await WaitForReadyAsync(connection, baseline, progress, output, token);
            ProjectionSnapshot queues;
            progress.Outcome = WriteOutcome.None;
            await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token))
            {
                var commitAttempted = false;
                try
                {
                    if (writeMarker)
                    {
                        progress.Outcome = WriteOutcome.Active;
                        await using var rw = Command("SET TRANSACTION READ WRITE;", connection, transaction);
                        await rw.ExecuteNonQueryAsync(token);
                    }
                    await RequireBaselineAsync(connection, transaction, baseline, token);
                    queues = await ReadProjectionQueuesAsync(connection, [], token, true, transaction, true);
                    if (!queues.Pending)
                    {
                        await VerifyFinancialTotalsAsync(connection, plan, token, transaction);
                        await VerifyOrdinaryProjectionsAsync(connection, transaction, token);
                        if (writeMarker)
                        {
                            await using var finish = Command("INSERT INTO public.schema_data_migrations(migration_key,applied_at_utc,details) VALUES (@Key,clock_timestamp(),@Details);", connection, transaction);
                            finish.Parameters.AddWithValue("Key", MarkerKey);
                            finish.Parameters.AddWithValue("Details", marker);
                            Require(await finish.ExecuteNonQueryAsync(token) == 1, "Final marker was not inserted.");
                        }
                        commitAttempted = true;
                        progress.Outcome = writeMarker ? WriteOutcome.Unknown : WriteOutcome.None;
                        await transaction.CommitAsync(token);
                        progress.Outcome = writeMarker ? WriteOutcome.Committed : WriteOutcome.None;
                        progress.EndWait();
                        return;
                    }
                    await transaction.RollbackAsync(CancellationToken.None);
                    progress.Outcome = WriteOutcome.None; // Queue check made no writes.
                }
                catch
                {
                    if (commitAttempted) throw; // No marker replay after uncertain COMMIT.
                    progress.Outcome = writeMarker ? WriteOutcome.Unknown : WriteOutcome.None;
                    await transaction.RollbackAsync(CancellationToken.None);
                    progress.Outcome = writeMarker ? WriteOutcome.RolledBack : WriteOutcome.None;
                    throw;
                }
            }
            await progress.ReportWaitAsync(output, "WAITING_PROJECTIONS", QueueDetail(queues) + "; no marker written");
            await Task.Delay(PollInterval, token);
        }
    }

    private static NpgsqlCommand Command(string sql, NpgsqlConnection connection, NpgsqlTransaction? transaction = null) =>
        new(sql, connection, transaction) { CommandTimeout = 15 };


    public static async Task<int> ExecuteAsync(AppConfiguration configuration, string[] args,
        TextWriter output, CancellationToken cancellationToken)
    {
        if (ValidateArguments(args) is { } error)
        {
            await output.WriteLineAsync("REFUSED: " + error);
            return 1;
        }
        var apply = args.Contains("--apply", StringComparer.Ordinal);
        var factory = new PostgresConnectionFactory(configuration.Storage, "eth_progress34_history");
        var builder = new NpgsqlConnectionStringBuilder(factory.ConnectionString);
        Require(builder.Database == "polycopytrader", "Unexpected configured database.");
        builder.Host = "192.168.0.101";
        builder.Port = 5432;
        builder.Pooling = false;
        builder.Timeout = 5;
        builder.CommandTimeout = 15;
        builder.Options = "-c default_transaction_read_only=on -c timezone=UTC -c statement_timeout=15000 -c lock_timeout=2000";
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var identity = Command("SELECT current_database(),host(inet_server_addr()),current_setting('transaction_read_only');", connection))
        await using (var reader = await identity.ExecuteReaderAsync(cancellationToken))
        {
            Require(await reader.ReadAsync(cancellationToken) && reader.GetString(0) == "polycopytrader" &&
                reader.GetString(1) == "192.168.0.101" && reader.GetString(2) == "on", "Production/read-only identity mismatch.");
        }
        var acquired = false;
        var progress = new Progress();
        try
        {
            if (apply)
            {
                await using var acquire = Command("SELECT pg_try_advisory_lock(@Key);", connection);
                acquire.Parameters.AddWithValue("Key", AdvisoryKey);
                acquired = (bool)(await acquire.ExecuteScalarAsync(cancellationToken))!;
                Require(acquired, "Another copy of this operation holds its advisory lock.");
            }
            await output.WriteLineAsync($"PREVIEW_STAGE utc={MicrosecondUtcNow():O}; health_and_schema");
            var baseline = await ReadHealthAsync(connection, null, cancellationToken);
            await output.WriteLineAsync($"PREVIEW_STAGE utc={MicrosecondUtcNow():O}; source_chains");
            var sources = await ReadSourcesAsync(connection, null, cancellationToken);
            await output.WriteLineAsync($"PREVIEW_STAGE utc={MicrosecondUtcNow():O}; causal_plan source_rows={sources.Count}");
            var plan = BuildPlan(sources);
            await output.WriteLineAsync($"PREVIEW_STAGE utc={MicrosecondUtcNow():O}; independent_source_totals");
            await ValidateUniverseAsync(connection, plan, cancellationToken);
            return await RunPlanAsync(connection, plan, baseline, apply, output, cancellationToken, progress);
        }
        catch (Exception ex)
        {
            // Do not emit connection strings or raw row JSON in operational errors.
            await output.WriteLineAsync(progress.StopMessage(ex));
            await output.WriteLineAsync(ex.StackTrace);
            return 1;
        }
        finally
        {
            if (acquired && connection.State == ConnectionState.Open)
            {
                await using var release = Command("SELECT pg_advisory_unlock(@Key);", connection);
                release.Parameters.AddWithValue("Key", AdvisoryKey);
                await release.ExecuteScalarAsync(CancellationToken.None);
            }
        }
    }

    // Only reached after production identity/source-universe checks above.
    // Internal visibility allows the same lifecycle to run in isolated PG tests.
    internal static async Task<int> RunPlanAsync(NpgsqlConnection connection, Plan plan,
        Baseline baseline, bool apply, TextWriter output, CancellationToken cancellationToken, Progress? progress = null)
    {
        progress ??= new Progress();
        progress.Stage = "preview";
        progress.Total = plan.Entries.Count;
        await output.WriteLineAsync($"PREVIEW_STAGE utc={MicrosecondUtcNow():O}; existing_history");
        var timestamps = await ReadExistingTimestampsAsync(connection, plan, cancellationToken);
        await VerifyTargetCoverageAsync(connection,cancellationToken);
        var now = MicrosecondUtcNow();
        NativeChain Chain(Entry entry) => CreateChain(entry, plan, timestamps.GetValueOrDefault(entry.Id("run"), now));
        var batches = plan.Entries.GroupBy(x => x.Source.RunId).Select(g => g.ToArray()).ToArray();
        var completed = 0;
        await output.WriteLineAsync($"PREVIEW_STAGE utc={MicrosecondUtcNow():O}; native_and_archive_collisions");
        var inspected = 0;
        foreach (var chunk in plan.Entries.Chunk(128))
        {
            completed += await CheckChainsAsync(connection, null, chunk.Select(Chain).ToArray(), cancellationToken);
            inspected += chunk.Length;
            if (inspected % 4096 == 0)
            {
                await output.WriteLineAsync($"PREVIEW_PROGRESS utc={MicrosecondUtcNow():O}; checked={inspected}/{plan.Entries.Count}; writes=0");
            }
        }
        Require(completed == timestamps.Count, "Historical target records outside the frozen plan.");
        progress.Completed = completed;
        var marker = BuildMarker(plan);
        var existingMarker = await ReadMarkerAsync(connection, cancellationToken);
        Require(existingMarker is null || existingMarker == marker, "Final marker does not match the current frozen plan.");
        Require(existingMarker is null || completed == plan.Entries.Count, "Marker exists but history is incomplete.");
        await output.WriteLineAsync($"PREVIEW cutoff={now:O}; parents={plan.Sources.Count}; batches={batches.Length}; trades={plan.Entries.Count}; verified_existing={completed}; source={plan.SourceDigest}; plan={plan.PlanDigest}; writes=0");
        foreach (var child in Children)
            await output.WriteLineAsync(JsonSerializer.Serialize(new { child.Id, child.Code, metrics = CalculateMetrics(plan.Entries.Where(e => e.Child.Id == child.Id)) }, JsonOptions));
        await RequireBaselineAsync(connection, null, baseline, cancellationToken);
        if (!apply)
        {
            var locks = await ReadLocksAsync(connection, cancellationToken);
            if (locks.Waiting > 0)
                await progress.ReportWaitAsync(output, "WAITING_LOCKS", $"waiting={locks.Waiting}; participants={locks.Participants}; preview_only=true; apply_not_ready=true");
            await output.WriteLineAsync("PREVIEW_OK: read-only; native rows, source plan and collisions verified.");
            return 0;
        }
        if (existingMarker is not null)
        {
            await CompleteWhenReconciledAsync(connection, plan, baseline, marker, false, progress, output, cancellationToken);
            await output.WriteLineAsync("IDEMPOTENT_OK: full marker and native history verified; writes=0.");
            return 0;
        }
        progress.Stage = "prior_run_queues";
        await WaitForProjectionsAsync(connection, [], baseline, progress, output, cancellationToken, allChildren: true);
        var window = new List<NativeChain>();
        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Stage = "batch_preflight";
            await WaitForReadyAsync(connection, baseline, progress, output, cancellationToken);
            var batchChains = batch.Select(Chain).ToArray();
            var existing = await CheckChainsAsync(connection, null, batchChains, cancellationToken);
            if (existing == batch.Length)
            {
                continue;
            }
            Require(existing == 0, "Only complete one-parent batches may be resumed.");
            await ApplyBatchAsync(connection, batch, batchChains, baseline, progress, output, cancellationToken);
            completed += batch.Length;
            progress.Completed = completed;
            progress.WindowBatches++;
            window.AddRange(batchChains);
            progress.EndWait();
            await output.WriteLineAsync($"BATCH_COMMITTED utc={MicrosecondUtcNow():O}; parent_run={batch[0].Source.RunId:D}; trades={batch.Length}; primary_rows={batch.Length * 6}; {progress.Counts}");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            progress.Stage = "window_queues";
            await WaitForReadyAsync(connection, baseline, progress, output, cancellationToken);
            var queues = await ReadProjectionQueuesAsync(connection, window, cancellationToken);
            if (progress.WindowBatches == MaximumPendingBatches)
                await WaitForProjectionsAsync(connection, window, baseline, progress, output, cancellationToken);
            if (!queues.Pending || progress.WindowBatches == MaximumPendingBatches)
            {
                window.Clear();
                progress.WindowBatches = 0;
                progress.EndWait();
            }
        }
        if (window.Count > 0)
        {
            await WaitForProjectionsAsync(connection, window, baseline, progress, output, cancellationToken);
            progress.WindowBatches = 0;
        }
        progress.Stage = "final_sources_and_chains";
        var refreshed = BuildPlan(await ReadSourcesAsync(connection, null, cancellationToken));
        Require(refreshed.SourceDigest == plan.SourceDigest && refreshed.PlanDigest == plan.PlanDigest, "Source changed before final reconciliation.");
        foreach (var chunk in plan.Entries.Chunk(128))
            Require(await CheckChainsAsync(connection, null, chunk.Select(Chain).ToArray(), cancellationToken) == chunk.Length, "Missing completed history.");
        await CompleteWhenReconciledAsync(connection, plan, baseline, marker, true, progress, output, cancellationToken);
        Require(await ReadMarkerAsync(connection, cancellationToken) == marker, "Final marker verification failed.");
        await output.WriteLineAsync($"COMPLETE utc={MicrosecondUtcNow():O}; native_trades={completed}; ordinary_paper_metrics_included=true; provenance=ResearchOnly; marker={MarkerKey}");
        return 0;
    }

    private static async Task ApplyBatchAsync(NpgsqlConnection connection, Entry[] batch,
        NativeChain[] batchChains, Baseline baseline, Progress progress, TextWriter output, CancellationToken token)
    {
        while (true)
        {
            progress.Stage = "batch_preflight";
            await WaitForReadyAsync(connection, baseline, progress, output, token);
            await output.WriteLineAsync($"BATCH_ATTEMPT utc={MicrosecondUtcNow():O}; parent_run={batch[0].Source.RunId:D}; {progress.Counts}");
            var retry = false;
            await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token))
            {
                var commitAttempted = false;
                progress.Stage = "batch_write";
                progress.Outcome = WriteOutcome.Active;
                try
                {
                    await using (var rw = Command("SET TRANSACTION READ WRITE;", connection, transaction))
                        await rw.ExecuteNonQueryAsync(token);
                    await using (var retention = Command("SELECT public.lock_strategy_run_retention_dependency();", connection, transaction))
                        await retention.ExecuteNonQueryAsync(token);
                    await RequireBaselineAsync(connection, transaction, baseline, token);
                    var protectedBefore = await ReadProtectedDigestAsync(connection, transaction, token);
                    var currentSource = await ReadSourcesAsync(connection, transaction, token, batch[0].Source.RunId);
                    Require(currentSource.Count == 1 && currentSource[0].Fingerprint == batch[0].Source.Fingerprint,
                        "Parent chain changed since the frozen preview.");
                    Require(await CheckChainsAsync(connection, transaction, batchChains, token) == 0, "Concurrent target collision.");
                    await InsertChainsAsync(connection, transaction, batchChains, token);
                    Require(await CheckChainsAsync(connection, transaction, batchChains, token) == batch.Length, "Incomplete batch.");
                    Require(await ReadProtectedDigestAsync(connection, transaction, token) == protectedBefore,
                        "Batch changed protected current/native/Live state.");
                    progress.Stage = "batch_commit";
                    commitAttempted = true;
                    progress.Outcome = WriteOutcome.Unknown;
                    await transaction.CommitAsync(token);
                    progress.Outcome = WriteOutcome.Committed;
                }
                catch (Exception ex)
                {
                    if (commitAttempted) throw; // Never replay an uncertain COMMIT.
                    progress.Outcome = WriteOutcome.Unknown;
                    await transaction.RollbackAsync(CancellationToken.None);
                    progress.Outcome = WriteOutcome.RolledBack;
                    if (!IsRetryableBatchLock(ex, rollbackConfirmed: true, commitAttempted)) throw;
                    retry = true;
                }
            }
            if (!retry) return;
            progress.Stage = "batch_lock_retry";
            await WaitForReadyAsync(connection, baseline, progress, output, token);
            await progress.ReportWaitAsync(output, "WAITING_LOCKS", "sqlstate=55P03; rollback_confirmed=true; retry_after_seconds=5");
            await Task.Delay(PollInterval, token);
        }
    }

    private static async Task VerifyTargetCoverageAsync(NpgsqlConnection connection,CancellationToken token)
    {
        foreach(var child in Children)
        {
            // Materialize ONLY this strategy's orders. The all-wallet anti-join
            // chose a broad Production index walk and exceeded the15s limit.
            await using var command = Command("""
WITH orders AS MATERIALIZED (
 SELECT id,signal_id,asset_id,copied_trader_wallet,created_at_utc FROM paper_orders WHERE strategy_id=@Id
)
SELECT
 EXISTS(SELECT 1 FROM orders o WHERE o.created_at_utc<@Cutoff
   AND NOT EXISTS(SELECT 1 FROM strategy_market_paper_runs r WHERE r.paper_order_id=o.id AND r.strategy_id=@Id AND r.entered_at_utc<@Cutoff))
 OR EXISTS(SELECT 1 FROM signals s WHERE s.trader_wallet=@Wallet AND s.created_at_utc<@Cutoff
   AND NOT EXISTS(SELECT 1 FROM orders o WHERE o.signal_id=s.id AND o.copied_trader_wallet=@Wallet))
 OR EXISTS(SELECT 1 FROM paper_positions p WHERE p.copied_trader_wallet=@Wallet
   AND NOT EXISTS(SELECT 1 FROM orders o WHERE o.copied_trader_wallet=@Wallet AND o.asset_id=p.asset_id))
 OR EXISTS(SELECT 1 FROM paper_position_settlements p WHERE p.copied_trader_wallet=@Wallet
   AND NOT EXISTS(SELECT 1 FROM orders o WHERE o.copied_trader_wallet=@Wallet AND o.asset_id=p.asset_id));
""",connection);
            command.Parameters.AddWithValue("Id",child.Id);
            command.Parameters.AddWithValue("Wallet",child.Wallet);
            command.Parameters.AddWithValue("Cutoff",CutoffUtc);
            Require(!(bool)(await command.ExecuteScalarAsync(token))!,$"Unmatched native history dependency for {child.Id:D}.");
        }
    }

    private static DateTimeOffset MicrosecondUtcNow()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }

    private static string BuildMarker(Plan plan) => JsonSerializer.Serialize(new Marker(ContractId, ApprovalDigest,
        plan.SourceDigest, plan.PlanDigest, CutoffUtc, plan.Entries.Select(e => e.Source.RunId).Distinct().Count(),
        plan.Entries.Count, Children.Select(c => (object)new { c.Id, c.Code, c.ParentId, c.Wallet, c.Cap,
            zero_gate = plan.Sources.Count(s => s.ParentId == c.ParentId) - plan.Entries.Count(e => e.Child.Id == c.Id),
            metrics = CalculateMetrics(plan.Entries.Where(e => e.Child.Id == c.Id)) }).ToArray(),
        "sufficient_depth_parent_average_full_fill; actual spent/shares scaled; own fee; ResearchOnly provenance; ordinary Paper metrics included",
        "historical zero start; settlement strictly before entry; loss+1, win max(0,k-1); min(k,cap); current rollout state unchanged",
        plan.Entries.Select(e=>e.Source).DistinctBy(s=>s.RunId).GroupBy(s=>new{s.FeeSource,s.Rate,s.Exponent,s.TakerOnly})
            .OrderBy(g=>g.Key.FeeSource,StringComparer.Ordinal)
            .Select(g=>(object)new{g.Key.FeeSource,g.Key.Rate,g.Key.Exponent,g.Key.TakerOnly,parent_entries=g.Count(),venue_reported=false}).ToArray()), JsonOptions);

    private static async Task<string?> ReadMarkerAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = Command("SELECT details FROM public.schema_data_migrations WHERE migration_key=@Key;", connection);
        command.Parameters.AddWithValue("Key", MarkerKey);
        return await command.ExecuteScalarAsync(token) as string;
    }

    internal static async Task<int> CheckChainsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        IReadOnlyList<NativeChain> chains, CancellationToken token)
    {
        const string sql = """
WITH expected AS (SELECT value AS v FROM jsonb_array_elements(@Rows::jsonb))
SELECT
 (s.id IS NOT NULL)::int+(o.id IS NOT NULL)::int+(f.id IS NOT NULL)::int+
 (r.id IS NOT NULL)::int+(p.id IS NOT NULL)::int+(ps.id IS NOT NULL)::int,
 (NOT @CheckValues OR (COALESCE(to_jsonb(s)=to_jsonb(jsonb_populate_record(NULL::public.signals,e.v->'signal')),s.id IS NULL)
 AND COALESCE(to_jsonb(o)=to_jsonb(jsonb_populate_record(NULL::public.paper_orders,e.v->'order')),o.id IS NULL)
 AND COALESCE(to_jsonb(f)=to_jsonb(jsonb_populate_record(NULL::public.paper_fills,e.v->'fill')),f.id IS NULL)
 AND COALESCE(to_jsonb(r)=to_jsonb(jsonb_populate_record(NULL::public.strategy_market_paper_runs,e.v->'run')),r.id IS NULL)
 AND COALESCE(to_jsonb(p)=to_jsonb(jsonb_populate_record(NULL::public.paper_positions,e.v->'position')),p.id IS NULL)
 AND COALESCE(to_jsonb(ps)=to_jsonb(jsonb_populate_record(NULL::public.paper_position_settlements,e.v->'settlement')),ps.id IS NULL))),
 EXISTS(SELECT 1 FROM public.strategy_market_paper_skip_tombstones t
        WHERE t.strategy_id=(e.v->>'child_id')::uuid AND (t.archive_format_version IS DISTINCT FROM 1
         OR t.market_id=e.v->>'market_id' OR t.condition_id=e.v->>'condition_id' OR t.archived_run_id=(e.v->'run'->>'id')::uuid))
 OR EXISTS(SELECT 1 FROM public.strategy_market_paper_skip_tombstones_v2 t
        JOIN public.strategy_skip_archive_market_identities m ON m.market_identity_id=t.market_identity_id
        JOIN public.strategy_skip_archive_market_metadata_versions v ON v.metadata_version_id=t.metadata_version_id AND v.market_identity_id=t.market_identity_id
        WHERE t.strategy_id=(e.v->>'child_id')::uuid AND (m.market_id=e.v->>'market_id' OR v.condition_id=e.v->>'condition_id' OR t.archived_run_id=(e.v->'run'->>'id')::uuid)),
 (e.v->'run'->>'id')::uuid
FROM expected e
LEFT JOIN public.signals s ON s.id=(e.v->'signal'->>'id')::uuid
LEFT JOIN public.paper_orders o ON o.id=(e.v->'order'->>'id')::uuid
LEFT JOIN public.paper_fills f ON f.id=(e.v->'fill'->>'id')::uuid OR f.paper_order_id=(e.v->'order'->>'id')::uuid
LEFT JOIN public.strategy_market_paper_runs r ON r.id=(e.v->'run'->>'id')::uuid
 OR (r.strategy_id=(e.v->>'child_id')::uuid AND r.market_id=e.v->>'market_id')
LEFT JOIN public.paper_positions p ON p.id=(e.v->'position'->>'id')::uuid
 OR (p.copied_trader_wallet=e.v->>'wallet' AND p.asset_id=e.v->>'asset_id')
LEFT JOIN public.paper_position_settlements ps ON ps.id=(e.v->'settlement'->>'id')::uuid
 OR (ps.copied_trader_wallet=e.v->>'wallet' AND ps.asset_id=e.v->>'asset_id');
""";
        async Task<HashSet<Guid>> CheckAsync(bool values, IReadOnlyList<NativeChain> selected)
        {
            // Empty targets need only indexed identity/collision lookups, not31k
            // copies of potentially large parent order-book evidence sent to PG.
            object payload = values ? selected : selected.Select(c=>new
            {
                c.ChildId,c.MarketId,c.Wallet,c.AssetId,c.ConditionId,
                signal=new { id=c.Signal["id"]!.GetValue<Guid>() }, order=new { id=c.Order["id"]!.GetValue<Guid>() },
                fill=new { id=c.Fill["id"]!.GetValue<Guid>() }, run=new { id=c.Run["id"]!.GetValue<Guid>() },
                position=new { id=c.Position["id"]!.GetValue<Guid>() }, settlement=new { id=c.Settlement["id"]!.GetValue<Guid>() }
            }).ToArray();
            await using var command = Command(sql, connection, transaction);
            command.Parameters.AddWithValue("Rows", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload, JsonOptions));
            command.Parameters.AddWithValue("CheckValues",values);
            await using var reader = await command.ExecuteReaderAsync(token);
            var rows = 0;
            var presentIds = new HashSet<Guid>();
            while (await reader.ReadAsync(token))
            {
                rows++;
                Require(reader.GetBoolean(1) && !reader.GetBoolean(2), "Native identity/value or archive/tombstone collision.");
                var present = reader.GetInt32(0);
                Require(present is 0 or 6, "Partial native six-record chain; refusing overwrite.");
                if (present == 6) presentIds.Add(reader.GetGuid(3));
            }
            Require(rows == selected.Count, "Duplicate native chain identity.");
            return presentIds;
        }
        var existing = await CheckAsync(false,chains);
        if (existing.Count == 0) return 0;
        var verified = await CheckAsync(true,chains.Where(c=>existing.Contains(c.Run["id"]!.GetValue<Guid>())).ToArray());
        Require(verified.SetEquals(existing),"Native chain changed during verification.");
        return verified.Count;
    }

    internal static async Task InsertChainsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyList<NativeChain> chains, CancellationToken token)
    {
        const string sql = """
WITH e AS MATERIALIZED (SELECT value AS v FROM jsonb_array_elements(@Rows::jsonb)),
s AS (INSERT INTO public.signals SELECT x.* FROM e CROSS JOIN LATERAL jsonb_populate_record(NULL::public.signals,e.v->'signal') x RETURNING id),
o AS (INSERT INTO public.paper_orders SELECT x.* FROM e CROSS JOIN LATERAL jsonb_populate_record(NULL::public.paper_orders,e.v->'order') x WHERE EXISTS(SELECT 1 FROM s WHERE s.id=x.signal_id) RETURNING id),
f AS (INSERT INTO public.paper_fills SELECT x.* FROM e CROSS JOIN LATERAL jsonb_populate_record(NULL::public.paper_fills,e.v->'fill') x WHERE EXISTS(SELECT 1 FROM o WHERE o.id=x.paper_order_id) RETURNING id),
r AS (INSERT INTO public.strategy_market_paper_runs SELECT x.* FROM e CROSS JOIN LATERAL jsonb_populate_record(NULL::public.strategy_market_paper_runs,e.v->'run') x WHERE EXISTS(SELECT 1 FROM f WHERE f.id=(e.v->'fill'->>'id')::uuid) RETURNING id),
p AS (INSERT INTO public.paper_positions SELECT x.* FROM e CROSS JOIN LATERAL jsonb_populate_record(NULL::public.paper_positions,e.v->'position') x WHERE EXISTS(SELECT 1 FROM r WHERE r.id=(e.v->'run'->>'id')::uuid) RETURNING id),
ps AS (INSERT INTO public.paper_position_settlements SELECT x.* FROM e CROSS JOIN LATERAL jsonb_populate_record(NULL::public.paper_position_settlements,e.v->'settlement') x WHERE EXISTS(SELECT 1 FROM p WHERE p.id=(e.v->'position'->>'id')::uuid) RETURNING id)
SELECT (SELECT count(*) FROM s)+(SELECT count(*) FROM o)+(SELECT count(*) FROM f)+(SELECT count(*) FROM r)+(SELECT count(*) FROM p)+(SELECT count(*) FROM ps);
""";
        await using var command = Command(sql, connection, transaction);
        command.Parameters.AddWithValue("Rows", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(chains, JsonOptions));
        Require((long)(await command.ExecuteScalarAsync(token))! == chains.Count * 6L, "Incomplete native insertion.");
    }

    private static async Task<Dictionary<Guid, DateTimeOffset>> ReadExistingTimestampsAsync(NpgsqlConnection connection,
        Plan plan, CancellationToken token)
    {
        await using var command = Command("""
SELECT id,skip_diagnostics_json->'history_model'->>'reconstructed_at_utc'
FROM public.strategy_market_paper_runs WHERE strategy_id=ANY(@Ids) AND entered_at_utc<@Cutoff;
""", connection);
        command.Parameters.AddWithValue("Ids", Children.Select(c => c.Id).ToArray());
        command.Parameters.AddWithValue("Cutoff", CutoffUtc);
        var allowed = plan.Entries.Select(e => e.Id("run")).ToHashSet();
        var result = new Dictionary<Guid, DateTimeOffset>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            Require(allowed.Contains(reader.GetGuid(0)) && !reader.IsDBNull(1), "Unrelated historical child run exists.");
            Require(DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var time), "Invalid reconstruction timestamp.");
            result.Add(reader.GetGuid(0), time);
        }
        return result;
    }

}
