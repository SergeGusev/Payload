using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.Startup;
using PolyCopyTrader.Storage;
using Cmd = PolyCopyTrader.Service.Startup.EthLossDiffPositiveProgressHistoryBackfillCommand;

namespace PolyCopyTrader.Tests;

public sealed class EthLossDiffPositiveProgressHistoryBackfillCommandTests
{
    internal static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-07-20T00:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void RetryPolicy_OnlyConfirmedRollbackOf55P03BeforeCommit()
    {
        var locked = new PostgresException("fixture", "ERROR", "ERROR", "55P03");
        Assert.True(Cmd.IsRetryableBatchLock(locked, true, false));
        Assert.False(Cmd.IsRetryableBatchLock(locked, false, false));
        Assert.False(Cmd.IsRetryableBatchLock(locked, true, true));
        foreach (var code in new[] { "40P01", "57014", "40001", "08006", "23505" })
            Assert.False(Cmd.IsRetryableBatchLock(new PostgresException("fixture", "ERROR", "ERROR", code), true, false));
        Assert.False(Cmd.IsRetryableBatchLock(new InvalidOperationException("55P03"), true, false));
        Assert.False(Cmd.IsRetryableBatchLock(new OperationCanceledException(), true, false));
    }

    [Fact]
    public void StopDiagnostics_DistinguishCommittedRolledBackAndUnknown()
    {
        var progress = new Cmd.Progress { Stage = "window_queues", Completed = 128, Total = 1088, WindowBatches = 8 };
        var error = new InvalidOperationException("fixture");
        foreach (var outcome in new[] { Cmd.WriteOutcome.None, Cmd.WriteOutcome.Committed })
        {
            progress.Outcome = outcome;
            Assert.Contains("no active write transaction", progress.StopMessage(error));
            Assert.DoesNotContain("rollback confirmed", progress.StopMessage(error));
        }
        progress.Outcome = Cmd.WriteOutcome.RolledBack;
        Assert.Contains("uncommitted transaction rollback confirmed", progress.StopMessage(error));
        progress.Outcome = Cmd.WriteOutcome.Unknown;
        Assert.Contains("outcome unknown; no automatic replay", progress.StopMessage(error));
        Assert.Contains("completed=128; remaining=960; window_batches=8", progress.StopMessage(error));
    }

    [Fact]
    public void ClosedAllowlist_MatchesEveryLiteralMigrationTuple()
    {
        var matches = Regex.Matches(PostgresLossDiffPositiveProgressStrategySchemaMigration.Sql,
            @"\('([^']+)'::uuid, '([^']+)'::uuid, '([^']+)'::uuid, '([^']+)', '([^']+)', (\d+)\)");
        Assert.Equal(34, matches.Count);
        Assert.Equal(34, Cmd.Children.Select(c => c.Id).Distinct().Count());
        foreach (Match match in matches)
        {
            var child = Assert.Single(Cmd.Children, c => c.Id == Guid.Parse(match.Groups[1].Value));
            Assert.Equal(Guid.Parse(match.Groups[2].Value), child.AssignmentId);
            Assert.Equal(Guid.Parse(match.Groups[3].Value), child.ParentId);
            Assert.Equal(match.Groups[4].Value, child.Code);
            Assert.Equal(match.Groups[5].Value, child.Name);
            Assert.Equal(int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture), child.Cap);
        }
    }

    [Fact]
    public void Arguments_DefaultPreviewAndExactApprovalOnly()
    {
        Assert.Null(Cmd.ValidateArguments([Cmd.CommandFlag]));
        Assert.Null(Cmd.ValidateArguments([Cmd.CommandFlag, "--apply", "--approved-contract-digest", Cmd.ApprovalDigest]));
        foreach (var args in new[]
        {
            Array.Empty<string>(), new[] { Cmd.CommandFlag, "--apply" },
            new[] { Cmd.CommandFlag, "--approved-contract-digest", "wrong" },
            new[] { Cmd.CommandFlag, "--apply", "--apply" }, new[] { Cmd.CommandFlag, "--host", "elsewhere" },
            new[] { EthLossDiffHistoryBackfillCommand.CommandFlag }, new[] { Cmd.CommandFlag, "--approved-contract-digest" }
        }) Assert.NotNull(Cmd.ValidateArguments(args));
    }

    [Fact]
    public void FrozenManifest_RejectsEitherDigestDrift()
    {
        var plan = new Cmd.Plan([], [], Cmd.FrozenSourceDigest, Cmd.FrozenPlanDigest);
        Cmd.RequireFrozenPlan(plan);
        Assert.Throws<InvalidOperationException>(() => Cmd.RequireFrozenPlan(plan with { SourceDigest = Cmd.Hash("different source") }));
        Assert.Throws<InvalidOperationException>(() => Cmd.RequireFrozenPlan(plan with { PlanDigest = Cmd.Hash("different accounting") }));
    }

    [Fact]
    public void Causality_StrictSettlementBoundaryOverlapFloorAndNoWinReset()
    {
        var sources = new[] { Row(1, 0, 10, false), Row(2, 5, 6, false),
            Row(3, 10, 12, true), Row(4, 13, 14, false), Row(5, 16, 17, true) };
        var plan = Cmd.BuildPlan(sources.Reverse().ToArray());
        Assert.DoesNotContain(plan.Entries, e => e.Source.RunId == sources[0].RunId || e.Source.RunId == sources[1].RunId);
        Assert.All(plan.Entries.Where(e => e.Source.RunId == sources[2].RunId), e => Assert.Equal(1, e.Counter));
        Assert.All(plan.Entries.Where(e => e.Source.RunId == sources[3].RunId), e => Assert.Equal(1, e.Counter));
        Assert.All(plan.Entries.Where(e => e.Source.RunId == sources[4].RunId), e => Assert.Equal(2, e.Counter));
        Assert.Equal(plan.PlanDigest, Cmd.BuildPlan(sources).PlanDigest);
    }

    [Fact]
    public void AllCounters_MatchIndependentReflectedPrefixCalculation()
    {
        var random = new Random(8236);
        var sources = Enumerable.Range(1, 100).Select(i => Row(i, i * 10, i * 10 + random.Next(1, 35), random.Next(2) == 0)).ToArray();
        var plan = Cmd.BuildPlan(sources);
        foreach (var source in sources)
        {
            var total = 0;
            var minimum = 0;
            foreach (var outcome in sources.Where(s => s.SettledAt < source.EnteredAt)
                         .OrderBy(s => s.SettledAt).ThenBy(s => s.EnteredAt)
                         .ThenBy(s => Convert.ToHexString(s.RunId.ToByteArray(bigEndian: true)), StringComparer.Ordinal))
            {
                total += outcome.Gross < 0 ? 1 : -1;
                minimum = Math.Min(minimum, total);
            }
            var expected = total - minimum;
            var selected = plan.Entries.Where(e => e.Source.RunId == source.RunId).ToArray();
            Assert.Equal(expected == 0 ? 0 : 16, selected.Length);
            Assert.All(selected, e => { Assert.Equal(expected, e.Counter); Assert.Equal(Math.Min(expected, e.Child.Cap), e.Multiplier); });
        }
    }

    [Fact]
    public void OwnFee_UsesFullPrecisionAverage_NotParentRoundedFeeOrDisplayPrice()
    {
        var losses = Enumerable.Range(1, 5).Select(i => Row(i, i * 10, i * 10 + 1, false)).ToArray();
        var win = Row(6, 60, 61, true, shares: 11.3m) with { DisplayPrice = .99m, ParentFee = .19699m, Net = 5.10301m };
        var plan = Cmd.BuildPlan(losses.Append(win).ToArray());
        var entry = Assert.Single(plan.Entries, e => e.Source.RunId == win.RunId && e.Child.Cap == 5);
        Assert.Equal(5, entry.Counter);
        Assert.Equal(30m, entry.Spent);
        Assert.Equal(56.5m, entry.Shares);
        Assert.Equal(6m / 11.3m, entry.AveragePrice);
        Assert.Equal(.98496m, entry.Fee);
        Assert.NotEqual(win.ParentFee * 5, entry.Fee);
        Assert.Equal(26.5m, entry.Gross);
        Assert.Equal(25.51504m, entry.Net);
        var metrics = Cmd.CalculateMetrics([entry]);
        Assert.Equal(100m * 25.51504m / 30.98496m, metrics.NetRoi);
        var cap1 = Assert.Single(plan.Entries, e => e.Source.RunId == win.RunId && e.Child.Cap == 1);
        Assert.Equal(6m, cap1.Spent);
        Assert.Equal(1, cap1.Multiplier);
    }

    [Fact]
    public void UnknownCandidateFeeAndInconsistentSourceReject_ZeroGateNeedsNeither()
    {
        var first = Row(1, 1, 2, false) with { Rate = null, ChainConsistent = false };
        Assert.Empty(Cmd.BuildPlan([first]).Entries);
        Assert.Throws<InvalidOperationException>(() => Cmd.BuildPlan([first, Row(2, 3, 4, true) with { Rate = null }]));
        Assert.Throws<InvalidOperationException>(() => Cmd.BuildPlan([first, Row(2, 3, 4, true) with { ChainConsistent = false }]));
        Assert.Throws<InvalidOperationException>(() => Cmd.BuildPlan([first, first]));
        Assert.Throws<InvalidOperationException>(() => Cmd.BuildPlan([first with { EnteredAt = Cmd.CutoffUtc }]));
        Assert.Throws<InvalidOperationException>(() => Cmd.BuildPlan([first with { ParentId = Guid.NewGuid() }]));
    }

    [Fact]
    public void CapFiveAtTwelve_Up8AllEighteenCaps_AndClosedExceptionDocumentation()
    {
        var sources=Enumerable.Range(1,19).Select(i=>Row(i,i*10,i*10+1,false,parent:Cmd.Up8)).ToArray();
        var plan=Cmd.BuildPlan(sources);
        var capped=Assert.Single(plan.Entries,e=>e.Counter==12 && e.Child.Cap==5);
        Assert.Equal(5,capped.Multiplier);
        Assert.Equal(30m,capped.Spent);
        Assert.Equal(18,Assert.Single(plan.Entries,e=>e.Counter==18 && e.Child.Cap==18).Multiplier);
        // Artifact roots are outside the repository; use the source location that
        // already identifies the test project, not a guessed current directory.
        var repository=Path.GetFullPath(Path.Combine(SourceDirectory(),"..",".."));
        foreach(var file in new[]{"AGENTS.md","Codex/Rules/Workflow.md","Codex/Rules/CodingRules.md","docs/architecture/PAPER_LIVE_PARITY.md"})
        {
            var text=File.ReadAllText(Path.Combine(repository,file));
            Assert.Contains(Cmd.ContractId,text);
            Assert.Contains(Cmd.ApprovalDigest[7..],text);
            Assert.Contains(Cmd.ExecutionSource,text);
            Assert.Contains(Cmd.EvidenceVersion,text);
            Assert.Contains("ordinary_paper_metrics_included=true",text);
            Assert.Contains("2026-09-03T05:32:51.200614Z",text);
        }
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string file="") => Path.GetDirectoryName(file)!;

    [Fact]
    public void NativeHistory_KeepsModelProvenanceAndDates_ClosesPosition_IncludesOrdinaryNet()
    {
        var first = Row(1, 1, 2, false);
        var source = Row(2, 3, 4, true) with { NativeSnapshot = Snapshot() };
        var plan = Cmd.BuildPlan([first, source]);
        var entry = plan.Entries[0];
        var reconstructed = Start.AddDays(40);
        var chain = Cmd.CreateChain(entry, plan, reconstructed);
        Assert.Equal("Filled", chain.Order["status"]!.GetValue<string>());
        Assert.Equal(Cmd.ExecutionSource, chain.Order["execution_source"]!.GetValue<string>());
        Assert.Null(chain.Order["correlation_id"]);
        Assert.Equal("Settled", chain.Run["status"]!.GetValue<string>());
        Assert.Equal("PaperOnly", chain.Run["retention_scope"]!.GetValue<string>());
        Assert.Equal(0m, chain.Position["size_shares"]!.GetValue<decimal>());
        Assert.Equal(0m, chain.Position["fee_usd"]!.GetValue<decimal>());
        Assert.Equal(entry.Fee, chain.Fill["fee_usd"]!.GetValue<decimal>());
        Assert.Equal(-entry.Fee, chain.Fill["net_realized_pnl_usd"]!.GetValue<decimal>());
        var audit = chain.Run["skip_diagnostics_json"]!["history_model"]!;
        Assert.Equal("ResearchOnly", audit["classification"]!.GetValue<string>());
        Assert.True(audit["ordinary_paper_metrics_included"]!.GetValue<bool>());
        Assert.False(audit["venue_execution_proven"]!.GetValue<bool>());
        Assert.Equal(Cmd.ApprovalDigest, audit["approved_contract_digest"]!.GetValue<string>());
        Assert.Equal(plan.SourceDigest, audit["source_digest"]!.GetValue<string>());
        Assert.Equal(reconstructed, audit["reconstructed_at_utc"]!.GetValue<DateTimeOffset>());
        Assert.Equal(JsonNode.Parse(source.NativeSnapshot)!["run"]!["entered_at_utc"]!.ToJsonString(), chain.Run["entered_at_utc"]!.ToJsonString());
        Assert.Equal(JsonNode.Parse(source.NativeSnapshot)!["settlement"]!["settled_at_utc"]!.ToJsonString(), chain.Settlement["settled_at_utc"]!.ToJsonString());
        var contribution = DashboardProjectionCalculator.GetLifetimeContribution(new StrategyRunProjectionPayload(
            entry.Id("run"), entry.Child.Id, "Settled", entry.Spent, entry.Id("order"), source.EnteredAt,
            source.EnteredAt, entry.Gross, source.SettledAt, null, source.SettledAt, null,
            entry.Fee, "Calculated", entry.Net));
        Assert.Equal(1, contribution.SettledRunsCount);
        Assert.Equal(entry.Net, contribution.RunNetRealizedPnlUsd);
        Assert.Equal(entry.Fee, contribution.RunAccountedFeeUsd);
        Assert.Equal(6, new[] { "signal", "order", "fill", "run", "position", "settlement" }.Select(entry.Id).Distinct().Count());
        Assert.Equal(Snapshot(), source.NativeSnapshot);
    }

    internal static Cmd.Source Row(int id, int entered, int settled, bool won, decimal shares = 12m, Guid? parent = null)
    {
        var payout = won ? shares : 0;
        var fee = Math.Round(.07m * 6m * (1m - 6m / shares), 5, MidpointRounding.AwayFromZero);
        return new Cmd.Source(Guid.Parse($"a0000000-0000-0000-0000-{id:000000000000}"), parent ?? Cmd.Up4,
            "market" + id, "asset" + id, "condition" + id, "Up", Start.AddSeconds(entered), Start.AddSeconds(settled),
            6, shares, won ? 1 : 0, payout, payout - 6, fee, .07m, 1, true,
            PolymarketFeeCalculationConstants.FeeCurveCalculationSource, payout - 6 - fee, true, "{}", .5m);
    }

    private static string Snapshot()
    {
        var rows = new JsonObject();
        var fields = new Dictionary<string, string>
        {
            ["signal"] = "id trader_wallet accepted decision proposed_paper_price proposed_size_shares proposed_notional_usd raw_context_json created_at_utc",
            ["order"] = "id signal_id strategy_id copied_trader_wallet status side price size_shares notional_usd cancelled_at_utc correlation_id execution_source raw_decision_json created_at_utc filled_at_utc",
            ["fill"] = "id paper_order_id price size_shares realized_pnl_usd net_realized_pnl_usd evidence filled_at_utc",
            ["run"] = "id strategy_id status entry_price stake_usd size_shares signal_id paper_order_id settlement_value_usd realized_pnl_usd net_realized_pnl_usd skip_reason retention_scope skip_diagnostics_json entered_at_utc settled_at_utc",
            ["position"] = "id copied_trader_wallet size_shares average_price estimated_value_usd unrealized_pnl_usd net_unrealized_pnl_usd updated_at_utc",
            ["settlement"] = "id copied_trader_wallet settled_size_shares average_price cost_basis_usd settlement_value_usd realized_pnl_usd net_realized_pnl_usd settlement_source settled_at_utc"
        };
        foreach (var (kind, names) in fields)
        {
            var row = new JsonObject();
            foreach (var field in names.Split(' ')) row[field] = null;
            row["id"] = "50000000-0000-0000-0000-000000000001";
            foreach (var field in names.Split(' ').Where(s => s.EndsWith("_at_utc", StringComparison.Ordinal)))
                row[field] = Start.ToString("O");
            if (kind is "fill" or "run" or "settlement" or "position")
                foreach (var field in "fee_usd fee_accounting_status fee_liquidity_role fee_calculation_source fee_rate fee_exponent fee_taker_only fee_calculated_at_utc".Split(' '))
                    row[field] = null;
            rows[kind] = row;
        }
        return rows.ToJsonString();
    }
}
