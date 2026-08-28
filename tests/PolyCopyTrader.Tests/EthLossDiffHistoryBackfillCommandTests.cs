using System.Globalization;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Startup;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class EthLossDiffHistoryBackfillCommandTests
{
    [Fact]
    public void EthUp8LossDiffHistoryBackfill_ArgumentsAndFrozenFactsAreExact()
    {
        Assert.Null(EthUp8LossDiffHistoryBackfillCommand.ValidateArguments(
            [EthUp8LossDiffHistoryBackfillCommand.CommandFlag]));
        Assert.Null(EthUp8LossDiffHistoryBackfillCommand.ValidateArguments(
            [EthUp8LossDiffHistoryBackfillCommand.CommandFlag,
                EthUp8LossDiffHistoryBackfillCommand.ApplyFlag,
                "--approved-contract-digest",
                EthUp8LossDiffHistoryBackfillCommand.ApprovalDigest]));
        Assert.NotNull(EthUp8LossDiffHistoryBackfillCommand.ValidateArguments(
            [EthLossDiffHistoryBackfillCommand.CommandFlag]));
        Assert.NotNull(EthUp8LossDiffHistoryBackfillCommand.ValidateArguments(
            [EthUp8LossDiffHistoryBackfillCommand.CommandFlag,
                EthUp8LossDiffHistoryBackfillCommand.ApplyFlag]));

        Assert.Empty(EthUp8LossDiffHistoryBackfillCommand.ValidateSourceFacts(
            1022,
            574,
            448,
            EthUp8LossDiffHistoryBackfillCommand.SourceDigest,
            EthUp8LossDiffHistoryBackfillCommand.FullSourceDigest,
            EthUp8LossDiffHistoryBackfillCommand.CutoffUtc));
        Assert.NotEmpty(EthUp8LossDiffHistoryBackfillCommand.ValidateSourceFacts(
            1021,
            574,
            448,
            EthUp8LossDiffHistoryBackfillCommand.SourceDigest,
            EthUp8LossDiffHistoryBackfillCommand.FullSourceDigest,
            EthUp8LossDiffHistoryBackfillCommand.CutoffUtc));
    }

    [Fact]
    public void EthUp8LossDiffHistoryBackfill_ExpectedChildrenAndCausalThresholdsAreExact()
    {
        Assert.Collection(
            EthUp8LossDiffHistoryBackfillCommand.Children,
            reset =>
            {
                Assert.Equal(Guid.Parse("b7c50005-0000-4000-8229-000000000003"), reset.Id);
                Assert.Equal("LossDiffReset", reset.Mode);
                Assert.Equal(3, reset.Threshold);
                Assert.Equal(new EthUp8LossDiffHistoryBackfillCommand.Metrics(
                    46, 31, 15, 276.24179998m, 69.99539091m, 8.86157000m, 61.13382091m, 20),
                    reset.ExpectedMetrics);
            },
            positive =>
            {
                Assert.Equal(Guid.Parse("b7c50005-0000-4000-8229-000000000016"), positive.Id);
                Assert.Equal("LossDiffPositive", positive.Mode);
                Assert.Equal(16, positive.Threshold);
                Assert.Equal(new EthUp8LossDiffHistoryBackfillCommand.Metrics(
                    21, 15, 6, 126.00000010m, 41.83029883m, 4.08783000m, 37.74246883m, 21),
                    positive.ExpectedMetrics);
            });

        var rows = Enumerable.Range(1, 16)
            .Select(index => Up8Row(index, index * 10, index * 10 + 1, won: false))
            .Append(Up8Row(100, 200, 201, won: true))
            .ToArray();
        var candidateId = rows[^1].RunId;
        var plan = EthUp8LossDiffHistoryBackfillCommand.BuildPlan(rows);

        Assert.Equal(16, Assert.Single(plan, entry =>
            entry.Child.Mode == "LossDiffReset" && entry.Source.RunId == candidateId).PreEntryValue);
        Assert.Equal(16, Assert.Single(plan, entry =>
            entry.Child.Mode == "LossDiffPositive" && entry.Source.RunId == candidateId).PreEntryValue);
    }

    [Fact]
    public void EthUp8LossDiffHistoryBackfill_ResetModeResetsAfterWin()
    {
        var rows = new[]
        {
            Up8Row(201, 10, 11, won: false),
            Up8Row(202, 20, 21, won: false),
            Up8Row(203, 30, 31, won: true),
            Up8Row(204, 40, 41, won: false),
            Up8Row(205, 50, 51, won: false),
            Up8Row(206, 60, 61, won: false),
            Up8Row(207, 70, 71, won: true)
        };

        var candidateId = rows[^1].RunId;
        var plan = EthUp8LossDiffHistoryBackfillCommand.BuildPlan(rows);

        Assert.Equal(3, Assert.Single(plan, entry =>
            entry.Child.Mode == "LossDiffReset" && entry.Source.RunId == candidateId).PreEntryValue);
        Assert.DoesNotContain(plan, entry =>
            entry.Child.Mode == "LossDiffPositive" && entry.Source.RunId == candidateId);
    }

    [Fact]
    public void EthUp8LossDiffHistoryBackfill_PositiveModeFloorsDecrementsAndUsesOnlyPriorSettlements()
    {
        var rows = new List<EthUp8LossDiffHistoryBackfillCommand.SourceRow>
        {
            Up8Row(301, 10, 11, won: true),
            Up8Row(302, 20, 21, won: true)
        };
        rows.AddRange(Enumerable.Range(0, 17)
            .Select(offset => Up8Row(303 + offset, 30 + offset * 10, 31 + offset * 10, won: false)));
        rows.Add(Up8Row(320, 200, 201, won: true));
        rows.Add(Up8Row(321, 210, 1000, won: false));
        rows.Add(Up8Row(322, 220, 221, won: true));

        var candidateId = rows[^1].RunId;
        var plan = EthUp8LossDiffHistoryBackfillCommand.BuildPlan(rows);

        Assert.Equal(16, Assert.Single(plan, entry =>
            entry.Child.Mode == "LossDiffPositive" && entry.Source.RunId == candidateId).PreEntryValue);
        Assert.DoesNotContain(plan, entry =>
            entry.Child.Mode == "LossDiffReset" && entry.Source.RunId == candidateId);
    }

    [Fact]
    public void EthUp8LossDiffHistoryBackfill_OperationalBaselineDetectsEveryProtectedDrift()
    {
        Assert.Empty(EthUp8LossDiffHistoryBackfillCommand.CompareOperationalBaseline(
            "source", "full", "membership", "invariant",
            "source", "full", "membership", "invariant"));

        var problems = EthUp8LossDiffHistoryBackfillCommand.CompareOperationalBaseline(
            "source", "full", "membership", "invariant",
            "changed-source", "changed-full", "changed-membership", "changed-invariant");

        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, problem => problem.StartsWith("source digest changed", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("full source-chain digest changed", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("causal selected membership/run-ID plan changed", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("protected flags/states/events invariant changed", StringComparison.Ordinal));
        Assert.Contains("lock_timeout = '2s'", EthUp8LossDiffHistoryBackfillCommand.TransactionSafetySql,
            StringComparison.Ordinal);
        Assert.Equal(
            "info=1.0.0+7a0b967d9c610166975a57b066a5154ff2499cf9; assembly=1.0.0.0; mvid=50215310ddd6",
            EthUp8LossDiffHistoryBackfillCommand.DeployedServiceVersion);
    }

    [Fact]
    public void EthUp8LossDiffHistoryBackfill_IndependentProductionMembershipRunIdsAreRetained()
    {
        var reset = ParseGuidEvidence(IndependentResetParentRunIds);
        var positive = ParseGuidEvidence(IndependentPositiveParentRunIds);

        Assert.Equal(46, reset.Length);
        Assert.Equal(21, positive.Length);
        Assert.Equal(reset.Length, reset.Distinct().Count());
        Assert.Equal(positive.Length, positive.Distinct().Count());
        Assert.Equal(2, reset.Intersect(positive).Count());
        Assert.Equal(
            EthUp8LossDiffHistoryBackfillCommand.ExpectedResetParentRunIdDigest,
            EthUp8LossDiffHistoryBackfillCommand.ComputeSourceDigest(reset.Select(id => id.ToString("D"))));
        Assert.Equal(
            EthUp8LossDiffHistoryBackfillCommand.ExpectedPositiveParentRunIdDigest,
            EthUp8LossDiffHistoryBackfillCommand.ComputeSourceDigest(positive.Select(id => id.ToString("D"))));
    }

    [Fact]
    public void EthUp8LossDiffHistoryBackfill_DeterministicIdsAndMarkerAreProfileBound()
    {
        var ids = new HashSet<Guid>();
        foreach (var child in EthUp8LossDiffHistoryBackfillCommand.Children)
        {
            for (var index = 1; index <= 67; index++)
            {
                foreach (var role in new[] { "signal", "order", "fill", "run", "position", "settlement" })
                {
                    Assert.True(ids.Add(EthUp8LossDiffHistoryBackfillCommand.DeterministicId(
                        child.Id,
                        GuidFromIndex(index),
                        role)));
                }
            }
        }

        var plan = new[]
        {
            new EthUp8LossDiffHistoryBackfillCommand.PlannedEntry(
                EthUp8LossDiffHistoryBackfillCommand.Children[0],
                Up8Row(42, 100, 200, won: false),
                3)
        };
        var details = EthUp8LossDiffHistoryBackfillCommand.BuildMarkerDetails(plan);
        Assert.True(EthUp8LossDiffHistoryBackfillCommand.MarkerMatches(details, plan));
        Assert.Contains("eth_up8_lossdiff_history_backfill_batched_v1", details,
            StringComparison.Ordinal);
        Assert.Equal(
            "sha256:b83c8ba3ee3b432a551aeeb1ed76d33d30e76ff4765c63831341ad9f12fe7be5",
            EthUp8LossDiffHistoryBackfillCommand.ApprovalDigest);
        Assert.Contains(EthUp8LossDiffHistoryBackfillCommand.AppliedHistoryContractDigest, details,
            StringComparison.Ordinal);
        Assert.DoesNotContain(EthUp8LossDiffHistoryBackfillCommand.ApprovalDigest, details,
            StringComparison.Ordinal);
        Assert.False(EthUp8LossDiffHistoryBackfillCommand.MarkerMatches(
            details.Replace("83e3f246", "corrupted", StringComparison.Ordinal), plan));
    }

    [Fact]
    public void Arguments_RejectMixedOrDuplicatedCommandsBeforeDatabaseAccess()
    {
        Assert.Null(EthLossDiffHistoryBackfillCommand.ValidateArguments(
            [EthLossDiffHistoryBackfillCommand.CommandFlag]));
        Assert.Null(EthLossDiffHistoryBackfillCommand.ValidateArguments(
            [EthLossDiffHistoryBackfillCommand.CommandFlag, EthLossDiffHistoryBackfillCommand.ApplyFlag,
                "--approved-contract-digest", EthLossDiffHistoryBackfillCommand.ApprovalDigest]));
        Assert.NotNull(EthLossDiffHistoryBackfillCommand.ValidateArguments(
            [EthLossDiffHistoryBackfillCommand.CommandFlag, "--disable-all-live-stakes"]));
        Assert.NotNull(EthLossDiffHistoryBackfillCommand.ValidateArguments(
            [EthLossDiffHistoryBackfillCommand.CommandFlag, EthLossDiffHistoryBackfillCommand.CommandFlag]));
        Assert.NotNull(EthLossDiffHistoryBackfillCommand.ValidateArguments(
            [EthLossDiffHistoryBackfillCommand.CommandFlag, EthLossDiffHistoryBackfillCommand.ApplyFlag]));
    }

    [Fact]
    public void BuildPlan_UsesStrictSettlementBeforeEntryAndThresholdOrHigher()
    {
        var rows = new List<EthLossDiffHistoryBackfillCommand.SourceRow>
        {
            Row(1, entered: 10, settled: 11, won: false),
            Row(2, entered: 20, settled: 21, won: false),
            Row(3, entered: 30, settled: 31, won: false),
            Row(4, entered: 40, settled: 41, won: false),
            Row(5, entered: 45, settled: 50, won: true),
            Row(6, entered: 50, settled: 51, won: true)
        };

        var plan = EthLossDiffHistoryBackfillCommand.BuildPlan(rows);
        var reset = Assert.Single(plan, entry =>
            entry.Child.Mode == "LossDiffReset" && entry.Source.RunId == rows[^1].RunId);

        Assert.Equal(4, reset.PreEntryValue);

        rows[4] = Row(5, entered: 45, settled: 49, won: true);
        plan = EthLossDiffHistoryBackfillCommand.BuildPlan(rows);

        Assert.DoesNotContain(plan, entry =>
            entry.Child.Mode == "LossDiffReset" && entry.Source.RunId == rows[^1].RunId);
    }

    [Fact]
    public void BuildPlan_PositiveModeFloorsWinsAtZeroAndAdmitsThirteenPlus()
    {
        var rows = new List<EthLossDiffHistoryBackfillCommand.SourceRow>
        {
            Row(1, entered: 10, settled: 11, won: true),
            Row(2, entered: 20, settled: 21, won: true)
        };
        for (var index = 0; index < 14; index++)
        {
            rows.Add(Row(index + 3, entered: 30 + index * 10, settled: 31 + index * 10, won: false));
        }

        var candidate = Row(100, entered: 200, settled: 201, won: true);
        rows.Add(candidate);

        var plan = EthLossDiffHistoryBackfillCommand.BuildPlan(rows);
        var positive = Assert.Single(plan, entry =>
            entry.Child.Mode == "LossDiffPositive" && entry.Source.RunId == candidate.RunId);

        Assert.Equal(14, positive.PreEntryValue);
        Assert.True(positive.PreEntryValue >= positive.Child.Threshold);
    }

    [Fact]
    public void CanonicalDigest_PreservesLfAndFinalLf()
    {
        Assert.Equal(
            "sha256:911169ddaaf146aff539f58c26c489af3b892dff0fe283c1c264c65ae5aa59a2",
            EthLossDiffHistoryBackfillCommand.ComputeSourceDigest(["a", "b"]));
    }

    [Fact]
    public void SourceGuard_RejectsEveryFrozenFactDriftIncludingCutoff()
    {
        Assert.Empty(EthLossDiffHistoryBackfillCommand.ValidateSourceFacts(
            1060,
            615,
            445,
            EthLossDiffHistoryBackfillCommand.SourceDigest,
            EthLossDiffHistoryBackfillCommand.FullSourceDigest,
            EthLossDiffHistoryBackfillCommand.CutoffUtc));

        Assert.NotEmpty(EthLossDiffHistoryBackfillCommand.ValidateSourceFacts(
            1059, 615, 445, EthLossDiffHistoryBackfillCommand.SourceDigest,
            EthLossDiffHistoryBackfillCommand.FullSourceDigest, EthLossDiffHistoryBackfillCommand.CutoffUtc));
        Assert.NotEmpty(EthLossDiffHistoryBackfillCommand.ValidateSourceFacts(
            1060, 614, 445, EthLossDiffHistoryBackfillCommand.SourceDigest,
            EthLossDiffHistoryBackfillCommand.FullSourceDigest, EthLossDiffHistoryBackfillCommand.CutoffUtc));
        Assert.NotEmpty(EthLossDiffHistoryBackfillCommand.ValidateSourceFacts(
            1060, 615, 444, EthLossDiffHistoryBackfillCommand.SourceDigest,
            EthLossDiffHistoryBackfillCommand.FullSourceDigest, EthLossDiffHistoryBackfillCommand.CutoffUtc));
        Assert.NotEmpty(EthLossDiffHistoryBackfillCommand.ValidateSourceFacts(
            1060, 615, 445, "sha256:drift",
            EthLossDiffHistoryBackfillCommand.FullSourceDigest, EthLossDiffHistoryBackfillCommand.CutoffUtc));
        Assert.NotEmpty(EthLossDiffHistoryBackfillCommand.ValidateSourceFacts(
            1060, 615, 445, EthLossDiffHistoryBackfillCommand.SourceDigest,
            "sha256:drift", EthLossDiffHistoryBackfillCommand.CutoffUtc));
        Assert.NotEmpty(EthLossDiffHistoryBackfillCommand.ValidateSourceFacts(
            1060, 615, 445, EthLossDiffHistoryBackfillCommand.SourceDigest,
            EthLossDiffHistoryBackfillCommand.FullSourceDigest,
            EthLossDiffHistoryBackfillCommand.CutoffUtc.AddTicks(1)));
    }

    [Fact]
    public void SourceParser_PreservesCanonicalNullFillNet()
    {
        var original = Row(7, entered: 10, settled: 11, won: false);
        var fields = original.CanonicalLine.Split('|');

        var parsed = EthLossDiffHistoryBackfillCommand.SourceRow.Parse(
            original.CanonicalLine,
            original.FullChainCanonicalLine,
            original.ParentPositionId,
            original.ParentSettlementId,
            fields);

        Assert.Null(parsed.FillNet);
        Assert.Equal(original.RunId, parsed.RunId);
        Assert.Equal(original.Fee, parsed.Fee);
    }

    [Fact]
    public void DeterministicIds_AreStableRoleSeparatedAndCollisionFreeForApprovedScale()
    {
        var child = EthLossDiffHistoryBackfillCommand.Children[0];
        var ids = new HashSet<Guid>();

        for (var index = 0; index < 52; index++)
        {
            var parentRunId = GuidFromIndex(index + 1);
            foreach (var role in new[] { "signal", "order", "fill", "run", "position", "settlement" })
            {
                var id = EthLossDiffHistoryBackfillCommand.DeterministicId(child.Id, parentRunId, role);
                Assert.Equal(id, EthLossDiffHistoryBackfillCommand.DeterministicId(child.Id, parentRunId, role));
                Assert.True(ids.Add(id));
            }
        }
    }

    [Fact]
    public void MarkerMatch_RequiresExactSemanticJsonIncludingMetricsAndCommandBuild()
    {
        var plan = new[]
        {
            new EthLossDiffHistoryBackfillCommand.PlannedEntry(
                EthLossDiffHistoryBackfillCommand.Children[0],
                Row(42, entered: 100, settled: 200, won: false),
                4)
        };
        var exact = EthLossDiffHistoryBackfillCommand.BuildMarkerDetails(plan);

        Assert.True(EthLossDiffHistoryBackfillCommand.MarkerMatches(exact, plan));
        Assert.False(EthLossDiffHistoryBackfillCommand.MarkerMatches(
            exact.Replace("\"Trades\":22", "\"Trades\":23", StringComparison.Ordinal), plan));
        Assert.False(EthLossDiffHistoryBackfillCommand.MarkerMatches(
            exact.Replace("bf75b521", "corrupted", StringComparison.Ordinal), plan));
        Assert.False(EthLossDiffHistoryBackfillCommand.MarkerMatches(exact[..^1] + ",\"extra\":true}", plan));
    }

    [Fact]
    public void ApprovedExpectedTotals_AreBoundToBothExactChildren()
    {
        Assert.Collection(
            EthLossDiffHistoryBackfillCommand.Children,
            reset =>
            {
                Assert.Equal(4, reset.Threshold);
                Assert.Equal(new EthLossDiffHistoryBackfillCommand.Metrics(
                    22, 16, 6, 132.11160001m, 45.64509784m, 4.20526000m, 41.43983784m, 10),
                    reset.ExpectedMetrics);
            },
            positive =>
            {
                Assert.Equal(13, positive.Threshold);
                Assert.Equal(new EthLossDiffHistoryBackfillCommand.Metrics(
                    30, 21, 9, 179.99999996m, 51.39379117m, 5.78854000m, 45.60525117m, 30),
                    positive.ExpectedMetrics);
            });
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task ApplyAndRetry_CommitOneExactChainMarkerOnceAndPreserveNullFillNet()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION")
            ?? throw new InvalidOperationException("PostgreSQL integration connection disappeared after discovery.");
        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();

        await using (var trigger = new NpgsqlCommand(
            "SELECT count(*) FROM pg_trigger WHERE tgname IN ('trg_dashboard_projection_paper_order', 'trg_dashboard_projection_paper_fill', 'trg_dashboard_projection_strategy_run', 'trg_dashboard_projection_paper_position', 'trg_dashboard_projection_paper_settlement') AND NOT tgisinternal;",
            connection))
        {
            Assert.Equal(5L, Assert.IsType<long>(await trigger.ExecuteScalarAsync()));
        }
        await using var transaction = await connection.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var sourceIndex = Random.Shared.Next(100_000, int.MaxValue);
        var source = Row(sourceIndex, entered: 1_700_000_000_000_000 + sourceIndex,
            settled: 1_700_000_300_000_000 + sourceIndex, won: true);
        var child = EthLossDiffHistoryBackfillCommand.Children[0];
        var parentWallet = "test-parent-" + suffix;
        var conditionId = "test-condition-" + suffix;
        var assetId = "test-asset-" + suffix;
        var marketId = "test-market-" + suffix;
        var settlementId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var enteredAt = DateTimeOffset.FromUnixTimeMilliseconds(source.EnteredMicros / 1000);
        var settledAt = DateTimeOffset.FromUnixTimeMilliseconds(source.SettledMicros / 1000);

        const string seedSql = """
INSERT INTO public.signals (
    id, leader_trade_id, trader_wallet, condition_id, asset_id, outcome, leader_price, score,
    accepted, decision, proposed_paper_price, proposed_size_shares, proposed_notional_usd,
    created_at_utc, raw_context_json)
VALUES (@SignalId, NULL, @ParentWallet, @ConditionId, @AssetId, 'Up', 0.5, 1, true,
    'test-parent', 0.5, 2, 1, @EnteredAt, '{"test":true}'::jsonb);

INSERT INTO public.paper_orders (
    id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id, condition_id,
    outcome, price, size_shares, notional_usd, created_at_utc, expires_at_utc, filled_at_utc,
    raw_decision_json, execution_source)
VALUES (@OrderId, @SignalId, @ParentStrategyId, @ParentWallet, 'Filled', 'Buy', @AssetId,
    @ConditionId, 'Up', 0.5, 2, 1, @EnteredAt, @EnteredAt + interval '5 minutes', @EnteredAt,
    '{"order_type":"FAK","execution_intent_order_book_snapshot":{}}'::jsonb,
    'btc_updown5m_fak_taker_paper');

INSERT INTO public.paper_fills (
    id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd, fee_usd,
    fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate, fee_exponent,
    fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd)
VALUES (@FillId, @OrderId, 0.5, 2, @EnteredAt, 'test-parent-fill', 0, 0.01,
    'Calculated', 'Taker', 'test', 0.01, 1, true, @SettledAt, NULL);

INSERT INTO public.strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares, signal_id,
    paper_order_id, entered_at_utc, settlement_price, settlement_value_usd, realized_pnl_usd,
    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd, settled_at_utc,
    retention_scope, created_at_utc, updated_at_utc)
VALUES (@RunId, @ParentStrategyId, @MarketId, @ConditionId, @MarketId, 'Test ETH market', 'Crypto',
    @EnteredAt - interval '30 seconds', @SettledAt, @EnteredAt - interval '1 minute', @EnteredAt,
    'Settled', @AssetId, 'Up', 0.5, 1, 2, @SignalId, @OrderId, @EnteredAt, 1, 2, 1,
    0.01, 'Calculated', 'Taker', 'test', 0.01, 1, true, @SettledAt, 0.99, @SettledAt,
    'PaperOnly', @EnteredAt, @SettledAt);

INSERT INTO public.paper_positions (
    id, copied_trader_wallet, asset_id, condition_id, outcome, size_shares, average_price,
    estimated_value_usd, unrealized_pnl_usd, fee_usd, fee_accounting_status, fee_liquidity_role,
    fee_calculation_source, fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc,
    net_unrealized_pnl_usd, updated_at_utc)
VALUES (@PositionId, @ParentWallet, @AssetId, @ConditionId, 'Up', 0, 0.5, 0, 0, 0.01,
    'Calculated', 'Taker', 'test', 0.01, 1, true, @SettledAt, 0, @SettledAt);

INSERT INTO public.paper_position_settlements (
    id, copied_trader_wallet, asset_id, condition_id, outcome, winning_asset_id, winning_outcome,
    category, settled_size_shares, average_price, cost_basis_usd, settlement_value_usd,
    realized_pnl_usd, fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source,
    fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd, won,
    settlement_source, settled_at_utc, created_at_utc)
VALUES (@SettlementId, @ParentWallet, @AssetId, @ConditionId, 'Up', @AssetId, 'Up', 'Crypto',
    2, 0.5, 1, 2, 1, 0.01, 'Calculated', 'Taker', 'test', 0.01, 1, true, @SettledAt,
    0.99, true, 'test', @SettledAt, @SettledAt);
""";
        await using (var seed = new NpgsqlCommand(seedSql, connection, transaction))
        {
            seed.Parameters.AddWithValue("SignalId", source.SignalId);
            seed.Parameters.AddWithValue("OrderId", source.OrderId);
            seed.Parameters.AddWithValue("FillId", source.FillId);
            seed.Parameters.AddWithValue("RunId", source.RunId);
            seed.Parameters.AddWithValue("PositionId", positionId);
            seed.Parameters.AddWithValue("SettlementId", settlementId);
            seed.Parameters.AddWithValue("ParentStrategyId", EthLossDiffHistoryBackfillCommand.ParentStrategyId);
            seed.Parameters.AddWithValue("ParentWallet", parentWallet);
            seed.Parameters.AddWithValue("ConditionId", conditionId);
            seed.Parameters.AddWithValue("AssetId", assetId);
            seed.Parameters.AddWithValue("MarketId", marketId);
            seed.Parameters.AddWithValue("EnteredAt", enteredAt);
            seed.Parameters.AddWithValue("SettledAt", settledAt);
            await seed.ExecuteNonQueryAsync();
        }

        source = source with
        {
            AssetId = assetId,
            SignalId = source.SignalId,
            OrderId = source.OrderId,
            FillId = source.FillId,
            ParentPositionId = positionId,
            ParentSettlementId = settlementId
        };
        var entry = new EthLossDiffHistoryBackfillCommand.PlannedEntry(child, source, 4);
        var plan = new[] { entry };
        var markerKey = EthLossDiffHistoryBackfillCommand.MarkerKey + "_test_" + suffix;
        await transaction.CommitAsync();

        string invariantBefore;
        await using (var before = await connection.BeginTransactionAsync())
        {
            invariantBefore = await EthLossDiffHistoryBackfillCommand.ReadInvariantDigestAsync(
                connection, before, CancellationToken.None);
            await before.RollbackAsync();
        }

        Assert.True(await EthLossDiffHistoryBackfillCommand.ApplyOneEntryBatchAsync(
            connection, entry, CancellationToken.None));

        var targetRunId = EthLossDiffHistoryBackfillCommand.DeterministicId(child.Id, source.RunId, "run");
        const string verifySql = """
SELECT
    count(*),
    count(*) FILTER (WHERE f.net_realized_pnl_usd IS NULL AND f.fee_accounting_status = 'Calculated'),
    count(*) FILTER (WHERE r.net_realized_pnl_usd = 0.99 AND r.fee_usd = 0.01),
    count(*) FILTER (WHERE o.execution_source = 'btc_updown5m_child_mirror_fak_paper'
        AND o.raw_decision_json->>'version' = 'eth_lossdiff_parent_mirror_history_v1'),
    count(*) FILTER (WHERE p.size_shares = 0),
    count(*) FILTER (WHERE ps.net_realized_pnl_usd = 0.99)
FROM public.strategy_market_paper_runs r
INNER JOIN public.paper_orders o ON o.id = r.paper_order_id
INNER JOIN public.paper_fills f ON f.paper_order_id = o.id
INNER JOIN public.paper_positions p ON p.copied_trader_wallet = o.copied_trader_wallet AND p.asset_id = o.asset_id
INNER JOIN public.paper_position_settlements ps ON ps.copied_trader_wallet = o.copied_trader_wallet AND ps.asset_id = o.asset_id
WHERE r.id = @RunId;
""";
        await using (var verifyTransaction = await connection.BeginTransactionAsync())
        {
            await using (var verify = new NpgsqlCommand(verifySql, connection, verifyTransaction))
            {
                verify.Parameters.AddWithValue("RunId", targetRunId);
                await using var reader = await verify.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                for (var column = 0; column < 6; column++)
                {
                    Assert.Equal(1L, reader.GetInt64(column));
                }
            }
            Assert.Equal(invariantBefore, await EthLossDiffHistoryBackfillCommand.ReadInvariantDigestAsync(
                connection, verifyTransaction, CancellationToken.None));
            Assert.Equal(1L, await EthLossDiffHistoryBackfillCommand.ReadExactTargetChainCountAsync(
                connection, verifyTransaction, plan, CancellationToken.None));
            Assert.Equal(6L, await EthLossDiffHistoryBackfillCommand.ReadTargetIdCollisionCountAsync(
                connection, verifyTransaction, plan, CancellationToken.None));
            await verifyTransaction.RollbackAsync();
        }

        await EthLossDiffHistoryBackfillCommand.InsertFinalMarkerAsync(
            connection, plan, markerKey, CancellationToken.None);

        await using var retry = await connection.BeginTransactionAsync();
        await using var marker = new NpgsqlCommand(
            "SELECT details FROM public.schema_data_migrations WHERE migration_key=@Key;", connection, retry);
        marker.Parameters.AddWithValue("Key", markerKey);
        var markerDetails = Assert.IsType<string>(await marker.ExecuteScalarAsync());
        Assert.True(EthLossDiffHistoryBackfillCommand.MarkerMatches(markerDetails, plan));
        var beforeRetryIds = await EthLossDiffHistoryBackfillCommand.ReadTargetIdCollisionCountAsync(
            connection, retry, plan, CancellationToken.None);

        var afterRetryIds = await EthLossDiffHistoryBackfillCommand.ReadTargetIdCollisionCountAsync(
            connection, retry, plan, CancellationToken.None);
        Assert.Equal(6L, beforeRetryIds);
        Assert.Equal(beforeRetryIds, afterRetryIds);
        await retry.RollbackAsync();

        // This is the production retry branch: the exact chain is skipped and the marker is unchanged.
        Assert.False(await EthLossDiffHistoryBackfillCommand.ApplyOneEntryBatchAsync(
            connection, entry, CancellationToken.None));

        var conflictingChild = EthLossDiffHistoryBackfillCommand.Children[1];
        var conflictingPlan = new[]
        {
            new EthLossDiffHistoryBackfillCommand.PlannedEntry(conflictingChild, source, 13)
        };
        var conflictingSettlementId = EthLossDiffHistoryBackfillCommand.DeterministicId(
            conflictingChild.Id, source.RunId, "settlement");
        await using (var seedConflict = await connection.BeginTransactionAsync())
        {
            await using var conflict = new NpgsqlCommand("""
INSERT INTO public.paper_position_settlements
SELECT @Id, @Wallet, asset_id, condition_id, outcome, winning_asset_id, winning_outcome,
       category, settled_size_shares, average_price, cost_basis_usd, settlement_value_usd,
       realized_pnl_usd, fee_usd, fee_accounting_status, fee_liquidity_role,
       fee_calculation_source, fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc,
       net_realized_pnl_usd, won, settlement_source, settled_at_utc, created_at_utc
FROM public.paper_position_settlements WHERE id=@ParentId;
""", connection, seedConflict);
            conflict.Parameters.AddWithValue("Id", conflictingSettlementId);
            conflict.Parameters.AddWithValue("Wallet", "strategy:" + conflictingChild.Code);
            conflict.Parameters.AddWithValue("ParentId", settlementId);
            Assert.Equal(1, await conflict.ExecuteNonQueryAsync());
            await seedConflict.CommitAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EthLossDiffHistoryBackfillCommand.ApplyOneEntryBatchAsync(
                connection,
                conflictingPlan[0],
                CancellationToken.None));
        Assert.Contains("Partial or conflicting target chain", exception.Message, StringComparison.Ordinal);

        await using var conflictVerify = await connection.BeginTransactionAsync();
        Assert.Equal(1L, await EthLossDiffHistoryBackfillCommand.ReadTargetIdCollisionCountAsync(
            connection, conflictVerify, conflictingPlan, CancellationToken.None));
        await using var absentMarker = new NpgsqlCommand(
            "SELECT count(*) FROM public.schema_data_migrations WHERE migration_key=@Key;",
            connection,
            conflictVerify);
        absentMarker.Parameters.AddWithValue("Key", markerKey + "_conflict");
        Assert.Equal(0L, Assert.IsType<long>(await absentMarker.ExecuteScalarAsync()));
        await conflictVerify.RollbackAsync();
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task EthUp8LossDiffHistoryBackfill_ApplyAndRetryCommitsExactChainAndPreservesProfileEvidence()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION")
            ?? throw new InvalidOperationException("PostgreSQL integration connection disappeared after discovery.");
        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();

        await using (var trigger = new NpgsqlCommand(
            "SELECT count(*) FROM pg_trigger WHERE tgname IN ('trg_dashboard_projection_paper_order', 'trg_dashboard_projection_paper_fill', 'trg_dashboard_projection_strategy_run', 'trg_dashboard_projection_paper_position', 'trg_dashboard_projection_paper_settlement') AND NOT tgisinternal;",
            connection))
        {
            Assert.Equal(5L, Assert.IsType<long>(await trigger.ExecuteScalarAsync()));
        }
        await using var transaction = await connection.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var sourceIndex = Random.Shared.Next(100_000, int.MaxValue);
        var source = Up8Row(sourceIndex, entered: 1_700_000_000_000_000 + sourceIndex,
            settled: 1_700_000_300_000_000 + sourceIndex, won: true);
        var child = EthUp8LossDiffHistoryBackfillCommand.Children[0];
        var parentWallet = "test-parent-" + suffix;
        var conditionId = "test-condition-" + suffix;
        var assetId = "test-asset-" + suffix;
        var marketId = "test-market-" + suffix;
        var settlementId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var enteredAt = DateTimeOffset.FromUnixTimeMilliseconds(source.EnteredMicros / 1000);
        var settledAt = DateTimeOffset.FromUnixTimeMilliseconds(source.SettledMicros / 1000);

        const string seedSql = """
INSERT INTO public.signals (
    id, leader_trade_id, trader_wallet, condition_id, asset_id, outcome, leader_price, score,
    accepted, decision, proposed_paper_price, proposed_size_shares, proposed_notional_usd,
    created_at_utc, raw_context_json)
VALUES (@SignalId, NULL, @ParentWallet, @ConditionId, @AssetId, 'Up', 0.5, 1, true,
    'test-parent', 0.5, 2, 1, @EnteredAt, '{"test":true}'::jsonb);

INSERT INTO public.paper_orders (
    id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id, condition_id,
    outcome, price, size_shares, notional_usd, created_at_utc, expires_at_utc, filled_at_utc,
    raw_decision_json, execution_source)
VALUES (@OrderId, @SignalId, @ParentStrategyId, @ParentWallet, 'Filled', 'Buy', @AssetId,
    @ConditionId, 'Up', 0.5, 2, 1, @EnteredAt, @EnteredAt + interval '5 minutes', @EnteredAt,
    '{"order_type":"FAK","execution_intent_order_book_snapshot":{}}'::jsonb,
    'btc_updown5m_fak_taker_paper');

INSERT INTO public.paper_fills (
    id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd, fee_usd,
    fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate, fee_exponent,
    fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd)
VALUES (@FillId, @OrderId, 0.5, 2, @EnteredAt, 'test-parent-fill', 0, 0.01,
    'Calculated', 'Taker', 'test', 0.01, 1, true, @SettledAt, NULL);

INSERT INTO public.strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares, signal_id,
    paper_order_id, entered_at_utc, settlement_price, settlement_value_usd, realized_pnl_usd,
    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd, settled_at_utc,
    retention_scope, created_at_utc, updated_at_utc)
VALUES (@RunId, @ParentStrategyId, @MarketId, @ConditionId, @MarketId, 'Test ETH market', 'Crypto',
    @EnteredAt - interval '30 seconds', @SettledAt, @EnteredAt - interval '1 minute', @EnteredAt,
    'Settled', @AssetId, 'Up', 0.5, 1, 2, @SignalId, @OrderId, @EnteredAt, 1, 2, 1,
    0.01, 'Calculated', 'Taker', 'test', 0.01, 1, true, @SettledAt, 0.99, @SettledAt,
    'PaperOnly', @EnteredAt, @SettledAt);

INSERT INTO public.paper_positions (
    id, copied_trader_wallet, asset_id, condition_id, outcome, size_shares, average_price,
    estimated_value_usd, unrealized_pnl_usd, fee_usd, fee_accounting_status, fee_liquidity_role,
    fee_calculation_source, fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc,
    net_unrealized_pnl_usd, updated_at_utc)
VALUES (@PositionId, @ParentWallet, @AssetId, @ConditionId, 'Up', 0, 0.5, 0, 0, 0.01,
    'Calculated', 'Taker', 'test', 0.01, 1, true, @SettledAt, 0, @SettledAt);

INSERT INTO public.paper_position_settlements (
    id, copied_trader_wallet, asset_id, condition_id, outcome, winning_asset_id, winning_outcome,
    category, settled_size_shares, average_price, cost_basis_usd, settlement_value_usd,
    realized_pnl_usd, fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source,
    fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd, won,
    settlement_source, settled_at_utc, created_at_utc)
VALUES (@SettlementId, @ParentWallet, @AssetId, @ConditionId, 'Up', @AssetId, 'Up', 'Crypto',
    2, 0.5, 1, 2, 1, 0.01, 'Calculated', 'Taker', 'test', 0.01, 1, true, @SettledAt,
    0.99, true, 'test', @SettledAt, @SettledAt);
""";
        await using (var seed = new NpgsqlCommand(seedSql, connection, transaction))
        {
            seed.Parameters.AddWithValue("SignalId", source.SignalId);
            seed.Parameters.AddWithValue("OrderId", source.OrderId);
            seed.Parameters.AddWithValue("FillId", source.FillId);
            seed.Parameters.AddWithValue("RunId", source.RunId);
            seed.Parameters.AddWithValue("PositionId", positionId);
            seed.Parameters.AddWithValue("SettlementId", settlementId);
            seed.Parameters.AddWithValue("ParentStrategyId", EthUp8LossDiffHistoryBackfillCommand.ParentStrategyId);
            seed.Parameters.AddWithValue("ParentWallet", parentWallet);
            seed.Parameters.AddWithValue("ConditionId", conditionId);
            seed.Parameters.AddWithValue("AssetId", assetId);
            seed.Parameters.AddWithValue("MarketId", marketId);
            seed.Parameters.AddWithValue("EnteredAt", enteredAt);
            seed.Parameters.AddWithValue("SettledAt", settledAt);
            await seed.ExecuteNonQueryAsync();
        }

        source = source with
        {
            AssetId = assetId,
            SignalId = source.SignalId,
            OrderId = source.OrderId,
            FillId = source.FillId,
            ParentPositionId = positionId,
            ParentSettlementId = settlementId
        };
        var entry = new EthUp8LossDiffHistoryBackfillCommand.PlannedEntry(child, source, 3);
        var plan = new[] { entry };
        var markerKey = EthUp8LossDiffHistoryBackfillCommand.MarkerKey + "_test_" + suffix;
        await transaction.CommitAsync();

        string invariantBefore;
        await using (var before = await connection.BeginTransactionAsync())
        {
            await using (var utc = new NpgsqlCommand("SET LOCAL TIME ZONE 'UTC';", connection, before))
            {
                await utc.ExecuteNonQueryAsync();
            }
            invariantBefore = await EthUp8LossDiffHistoryBackfillCommand.ReadInvariantDigestAsync(
                connection, before, CancellationToken.None);
            await before.RollbackAsync();
        }

        Assert.True(await EthUp8LossDiffHistoryBackfillCommand.ApplyOneEntryBatchAsync(
            connection, entry, CancellationToken.None));

        var targetRunId = EthUp8LossDiffHistoryBackfillCommand.DeterministicId(child.Id, source.RunId, "run");
        const string verifySql = """
SELECT
    count(*),
    count(*) FILTER (WHERE f.net_realized_pnl_usd IS NULL AND f.fee_accounting_status = 'Calculated'),
    count(*) FILTER (WHERE r.net_realized_pnl_usd = 0.99 AND r.fee_usd = 0.01),
    count(*) FILTER (WHERE o.execution_source = 'btc_updown5m_child_mirror_fak_paper'
        AND o.raw_decision_json->>'version' = 'eth_up8_lossdiff_parent_mirror_history_v1'),
    count(*) FILTER (WHERE p.size_shares = 0),
    count(*) FILTER (WHERE ps.net_realized_pnl_usd = 0.99)
FROM public.strategy_market_paper_runs r
INNER JOIN public.paper_orders o ON o.id = r.paper_order_id
INNER JOIN public.paper_fills f ON f.paper_order_id = o.id
INNER JOIN public.paper_positions p ON p.copied_trader_wallet = o.copied_trader_wallet AND p.asset_id = o.asset_id
INNER JOIN public.paper_position_settlements ps ON ps.copied_trader_wallet = o.copied_trader_wallet AND ps.asset_id = o.asset_id
WHERE r.id = @RunId;
""";
        await using (var verifyTransaction = await connection.BeginTransactionAsync())
        {
            await using (var utc = new NpgsqlCommand("SET LOCAL TIME ZONE 'UTC';", connection, verifyTransaction))
            {
                await utc.ExecuteNonQueryAsync();
            }
            await using (var verify = new NpgsqlCommand(verifySql, connection, verifyTransaction))
            {
                verify.Parameters.AddWithValue("RunId", targetRunId);
                await using var reader = await verify.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                for (var column = 0; column < 6; column++)
                {
                    Assert.Equal(1L, reader.GetInt64(column));
                }
            }
            Assert.Equal(6L, await EthUp8LossDiffHistoryBackfillCommand.ReadTargetIdCollisionCountAsync(
                connection, verifyTransaction, plan, CancellationToken.None));
            await using (var identity = new NpgsqlCommand("""
SELECT
    r.id=@RunId,
    r.strategy_id=@ChildId,
    r.signal_id=@SignalId,
    r.paper_order_id=@OrderId,
    f.id=@FillId,
    p.id=@PositionId,
    ps.id=@SettlementId,
    (r.skip_diagnostics_json->>'parent_run_id')::uuid=@ParentRunId,
    (r.skip_diagnostics_json->>'parent_signal_id')::uuid=@ParentSignalId,
    (r.skip_diagnostics_json->>'parent_order_id')::uuid=@ParentOrderId,
    (r.skip_diagnostics_json->>'parent_fill_id')::uuid=@ParentFillId,
    (r.skip_diagnostics_json->>'parent_settlement_id')::uuid=@ParentSettlementId,
    (r.skip_diagnostics_json->>'threshold')::integer=@Threshold,
    (r.skip_diagnostics_json->>'pre_entry_loss_diff')::integer=@PreEntryValue,
    (r.skip_diagnostics_json->>'embedded_order_book_snapshot_available')::boolean=@HasSnapshot
FROM public.strategy_market_paper_runs r
INNER JOIN public.paper_orders o ON o.id=r.paper_order_id
INNER JOIN public.paper_fills f ON f.paper_order_id=o.id
INNER JOIN public.paper_positions p
    ON p.copied_trader_wallet=o.copied_trader_wallet AND p.asset_id=o.asset_id
INNER JOIN public.paper_position_settlements ps
    ON ps.copied_trader_wallet=o.copied_trader_wallet AND ps.asset_id=o.asset_id
WHERE r.id=@RunId;
""", connection, verifyTransaction))
            {
                identity.Parameters.AddWithValue("RunId", targetRunId);
                identity.Parameters.AddWithValue("ChildId", child.Id);
                identity.Parameters.AddWithValue("SignalId", EthUp8LossDiffHistoryBackfillCommand.DeterministicId(child.Id, source.RunId, "signal"));
                identity.Parameters.AddWithValue("OrderId", EthUp8LossDiffHistoryBackfillCommand.DeterministicId(child.Id, source.RunId, "order"));
                identity.Parameters.AddWithValue("FillId", EthUp8LossDiffHistoryBackfillCommand.DeterministicId(child.Id, source.RunId, "fill"));
                identity.Parameters.AddWithValue("PositionId", EthUp8LossDiffHistoryBackfillCommand.DeterministicId(child.Id, source.RunId, "position"));
                identity.Parameters.AddWithValue("SettlementId", EthUp8LossDiffHistoryBackfillCommand.DeterministicId(child.Id, source.RunId, "settlement"));
                identity.Parameters.AddWithValue("ParentRunId", source.RunId);
                identity.Parameters.AddWithValue("ParentSignalId", source.SignalId);
                identity.Parameters.AddWithValue("ParentOrderId", source.OrderId);
                identity.Parameters.AddWithValue("ParentFillId", source.FillId);
                identity.Parameters.AddWithValue("ParentSettlementId", source.ParentSettlementId);
                identity.Parameters.AddWithValue("Threshold", child.Threshold);
                identity.Parameters.AddWithValue("PreEntryValue", entry.PreEntryValue);
                identity.Parameters.AddWithValue("HasSnapshot", source.HasEmbeddedSnapshot);
                await using var reader = await identity.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var labels = new[] { "run", "child", "signal", "order", "fill", "position", "settlement",
                    "parent_run", "parent_signal", "parent_order", "parent_fill", "parent_settlement",
                    "threshold", "pre_entry", "snapshot" };
                for (var column = 0; column < labels.Length; column++)
                {
                    Assert.True(reader.GetBoolean(column), labels[column]);
                }
            }
            Assert.Equal(1L, await EthUp8LossDiffHistoryBackfillCommand.ReadExactTargetChainCountAsync(
                connection, verifyTransaction, plan, CancellationToken.None));
            Assert.Equal(invariantBefore, await EthUp8LossDiffHistoryBackfillCommand.ReadInvariantDigestAsync(
                connection, verifyTransaction, CancellationToken.None));
            await using (var projectionSideEffects = new NpgsqlCommand("""
SELECT
    (SELECT count(*) FROM public.dashboard_projection_events
        WHERE strategy_id=@ChildId AND source_id=ANY(@ProjectionSourceIds)),
    (SELECT count(*) FROM public.paper_copied_trader_performance_refresh_queue
        WHERE copied_trader_wallet=@ChildWallet AND priority=100);
""", connection, verifyTransaction))
            {
                projectionSideEffects.Parameters.AddWithValue("ChildId", child.Id);
                projectionSideEffects.Parameters.AddWithValue("ProjectionSourceIds", new[]
                {
                    EthUp8LossDiffHistoryBackfillCommand.DeterministicId(child.Id, source.RunId, "order"),
                    EthUp8LossDiffHistoryBackfillCommand.DeterministicId(child.Id, source.RunId, "fill"),
                    EthUp8LossDiffHistoryBackfillCommand.DeterministicId(child.Id, source.RunId, "run"),
                    EthUp8LossDiffHistoryBackfillCommand.DeterministicId(child.Id, source.RunId, "position"),
                    EthUp8LossDiffHistoryBackfillCommand.DeterministicId(child.Id, source.RunId, "settlement")
                });
                projectionSideEffects.Parameters.AddWithValue("ChildWallet", "strategy:" + child.Code);
                await using var reader = await projectionSideEffects.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(5L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
            }
            await verifyTransaction.RollbackAsync();
        }

        await EthUp8LossDiffHistoryBackfillCommand.InsertFinalMarkerAsync(
            connection, plan, markerKey, CancellationToken.None);

        await using var retry = await connection.BeginTransactionAsync();
        await using var marker = new NpgsqlCommand(
            "SELECT details FROM public.schema_data_migrations WHERE migration_key=@Key;", connection, retry);
        marker.Parameters.AddWithValue("Key", markerKey);
        var markerDetails = Assert.IsType<string>(await marker.ExecuteScalarAsync());
        Assert.True(EthUp8LossDiffHistoryBackfillCommand.MarkerMatches(markerDetails, plan));
        var beforeRetryIds = await EthUp8LossDiffHistoryBackfillCommand.ReadTargetIdCollisionCountAsync(
            connection, retry, plan, CancellationToken.None);

        var afterRetryIds = await EthUp8LossDiffHistoryBackfillCommand.ReadTargetIdCollisionCountAsync(
            connection, retry, plan, CancellationToken.None);
        Assert.Equal(6L, beforeRetryIds);
        Assert.Equal(beforeRetryIds, afterRetryIds);
        await retry.RollbackAsync();

        // This is the production retry branch: the exact chain is skipped and the marker is unchanged.
        Assert.False(await EthUp8LossDiffHistoryBackfillCommand.ApplyOneEntryBatchAsync(
            connection, entry, CancellationToken.None));

        var conflictingChild = EthUp8LossDiffHistoryBackfillCommand.Children[1];
        var conflictingPlan = new[]
        {
            new EthUp8LossDiffHistoryBackfillCommand.PlannedEntry(conflictingChild, source, 16)
        };
        var conflictingSettlementId = EthUp8LossDiffHistoryBackfillCommand.DeterministicId(
            conflictingChild.Id, source.RunId, "settlement");
        await using (var seedConflict = await connection.BeginTransactionAsync())
        {
            await using var conflict = new NpgsqlCommand("""
INSERT INTO public.paper_position_settlements
SELECT @Id, @Wallet, asset_id, condition_id, outcome, winning_asset_id, winning_outcome,
       category, settled_size_shares, average_price, cost_basis_usd, settlement_value_usd,
       realized_pnl_usd, fee_usd, fee_accounting_status, fee_liquidity_role,
       fee_calculation_source, fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc,
       net_realized_pnl_usd, won, settlement_source, settled_at_utc, created_at_utc
FROM public.paper_position_settlements WHERE id=@ParentId;
""", connection, seedConflict);
            conflict.Parameters.AddWithValue("Id", conflictingSettlementId);
            conflict.Parameters.AddWithValue("Wallet", "strategy:" + conflictingChild.Code);
            conflict.Parameters.AddWithValue("ParentId", settlementId);
            Assert.Equal(1, await conflict.ExecuteNonQueryAsync());
            await seedConflict.CommitAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EthUp8LossDiffHistoryBackfillCommand.ApplyOneEntryBatchAsync(
                connection,
                conflictingPlan[0],
                CancellationToken.None));
        Assert.Contains("Partial or conflicting target chain", exception.Message, StringComparison.Ordinal);

        await using var conflictVerify = await connection.BeginTransactionAsync();
        Assert.Equal(1L, await EthUp8LossDiffHistoryBackfillCommand.ReadTargetIdCollisionCountAsync(
            connection, conflictVerify, conflictingPlan, CancellationToken.None));
        await using var absentMarker = new NpgsqlCommand(
            "SELECT count(*) FROM public.schema_data_migrations WHERE migration_key=@Key;",
            connection,
            conflictVerify);
        absentMarker.Parameters.AddWithValue("Key", markerKey + "_conflict");
        Assert.Equal(0L, Assert.IsType<long>(await absentMarker.ExecuteScalarAsync()));
        await conflictVerify.RollbackAsync();
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task BatchedApply_ResumesAfterTenChainsCompletes52AndRetryIsNoOp()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION")
            ?? throw new InvalidOperationException("PostgreSQL integration connection disappeared after discovery.");
        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();

        var batch = Guid.NewGuid().ToString("N");
        var sources = Enumerable.Range(0, 52).Select(offset =>
        {
            var index = Random.Shared.Next(100_000, int.MaxValue);
            var asset = $"exact52-asset-{batch}-{offset}";
            return Row(index, 1_700_100_000_000_000 + offset * 600_000_000L,
                    1_700_100_300_000_000 + offset * 600_000_000L, won: offset % 3 != 0) with
                {
                    AssetId = asset,
                    ParentPositionId = Guid.NewGuid(),
                    ParentSettlementId = Guid.NewGuid()
                };
        }).ToArray();
        var plan = sources.Take(22)
            .Select(source => new EthLossDiffHistoryBackfillCommand.PlannedEntry(
                EthLossDiffHistoryBackfillCommand.Children[0], source, 4))
            .Concat(sources.Skip(22).Select(source => new EthLossDiffHistoryBackfillCommand.PlannedEntry(
                EthLossDiffHistoryBackfillCommand.Children[1], source, 13)))
            .ToArray();
        Assert.Equal(22, plan.Count(entry => entry.Child.Id == EthLossDiffHistoryBackfillCommand.Children[0].Id));
        Assert.Equal(30, plan.Count(entry => entry.Child.Id == EthLossDiffHistoryBackfillCommand.Children[1].Id));

        var parentWallet = "exact52-parent-" + batch;
        var conditions = sources.Select((_, index) => $"exact52-condition-{batch}-{index}").ToArray();
        var markets = sources.Select((_, index) => $"exact52-market-{batch}-{index}").ToArray();
        var entered = sources.Select(source => DateTimeOffset.FromUnixTimeMilliseconds(source.EnteredMicros / 1000)).ToArray();
        var settled = sources.Select(source => DateTimeOffset.FromUnixTimeMilliseconds(source.SettledMicros / 1000)).ToArray();
        const string seedSql = """
INSERT INTO public.signals (id, trader_wallet, condition_id, asset_id, outcome, leader_price, score,
    accepted, decision, proposed_paper_price, proposed_size_shares, proposed_notional_usd,
    created_at_utc, raw_context_json)
SELECT * FROM unnest(@SignalIds::uuid[], array_fill(@Wallet::text, ARRAY[52]), @Conditions::text[],
    @Assets::text[], array_fill('Up'::text, ARRAY[52]), array_fill(0.5::numeric, ARRAY[52]),
    array_fill(1::numeric, ARRAY[52]), array_fill(true, ARRAY[52]),
    array_fill('test-parent'::text, ARRAY[52]), array_fill(0.5::numeric, ARRAY[52]),
    array_fill(2::numeric, ARRAY[52]), array_fill(1::numeric, ARRAY[52]), @Entered::timestamptz[],
    array_fill('{"test":true}'::jsonb, ARRAY[52]));

INSERT INTO public.paper_orders (id, signal_id, strategy_id, copied_trader_wallet, status, side,
    asset_id, condition_id, outcome, price, size_shares, notional_usd, created_at_utc,
    expires_at_utc, filled_at_utc, raw_decision_json, execution_source)
SELECT order_id, signal_id, @ParentStrategyId, @Wallet, 'Filled', 'Buy', asset, condition_id,
    'Up', 0.5, 2, 1, entered_at, entered_at + interval '5 minutes', entered_at,
    '{"order_type":"FAK","execution_intent_order_book_snapshot":{}}'::jsonb,
    'btc_updown5m_fak_taker_paper'
FROM unnest(@OrderIds::uuid[], @SignalIds::uuid[], @Assets::text[], @Conditions::text[],
    @Entered::timestamptz[]) AS x(order_id, signal_id, asset, condition_id, entered_at);

INSERT INTO public.paper_fills (id, paper_order_id, price, size_shares, filled_at_utc, evidence,
    realized_pnl_usd, fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source,
    fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd)
SELECT fill_id, order_id, 0.5, 2, entered_at, 'test-parent-fill', 0, 0.01, 'Calculated',
    'Taker', 'test', 0.01, 1, true, settled_at, NULL
FROM unnest(@FillIds::uuid[], @OrderIds::uuid[], @Entered::timestamptz[], @Settled::timestamptz[])
    AS x(fill_id, order_id, entered_at, settled_at);

INSERT INTO public.strategy_market_paper_runs (id, strategy_id, market_id, condition_id,
    market_slug, market_title, category, market_start_utc, market_end_utc, detected_at_utc,
    entry_due_at_utc, status, selected_asset_id, selected_outcome, entry_price, stake_usd,
    size_shares, signal_id, paper_order_id, entered_at_utc, settlement_price,
    settlement_value_usd, realized_pnl_usd, fee_usd, fee_accounting_status,
    fee_liquidity_role, fee_calculation_source, fee_rate, fee_exponent, fee_taker_only,
    fee_calculated_at_utc, net_realized_pnl_usd, settled_at_utc, retention_scope,
    created_at_utc, updated_at_utc)
SELECT run_id, @ParentStrategyId, market_id, condition_id, market_id, 'Test ETH market', 'Crypto',
    entered_at - interval '30 seconds', settled_at, entered_at - interval '1 minute', entered_at,
    'Settled', asset, 'Up', 0.5, 1, 2, signal_id, order_id, entered_at, 1, 2, 1, 0.01,
    'Calculated', 'Taker', 'test', 0.01, 1, true, settled_at, 0.99, settled_at, 'PaperOnly',
    entered_at, settled_at
FROM unnest(@RunIds::uuid[], @Markets::text[], @Conditions::text[], @Assets::text[],
    @SignalIds::uuid[], @OrderIds::uuid[], @Entered::timestamptz[], @Settled::timestamptz[])
    AS x(run_id, market_id, condition_id, asset, signal_id, order_id, entered_at, settled_at);

INSERT INTO public.paper_positions (id, copied_trader_wallet, asset_id, condition_id, outcome,
    size_shares, average_price, estimated_value_usd, unrealized_pnl_usd, fee_usd,
    fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate, fee_exponent,
    fee_taker_only, fee_calculated_at_utc, net_unrealized_pnl_usd, updated_at_utc)
SELECT position_id, @Wallet, asset, condition_id, 'Up', 0, 0.5, 0, 0, 0.01, 'Calculated',
    'Taker', 'test', 0.01, 1, true, settled_at, 0, settled_at
FROM unnest(@PositionIds::uuid[], @Assets::text[], @Conditions::text[], @Settled::timestamptz[])
    AS x(position_id, asset, condition_id, settled_at);

INSERT INTO public.paper_position_settlements (id, copied_trader_wallet, asset_id, condition_id,
    outcome, winning_asset_id, winning_outcome, category, settled_size_shares, average_price,
    cost_basis_usd, settlement_value_usd, realized_pnl_usd, fee_usd, fee_accounting_status,
    fee_liquidity_role, fee_calculation_source, fee_rate, fee_exponent, fee_taker_only,
    fee_calculated_at_utc, net_realized_pnl_usd, won, settlement_source, settled_at_utc,
    created_at_utc)
SELECT settlement_id, @Wallet, asset, condition_id, 'Up', asset, 'Up', 'Crypto', 2, 0.5,
    1, 2, 1, 0.01, 'Calculated', 'Taker', 'test', 0.01, 1, true, settled_at, 0.99,
    true, 'test', settled_at, settled_at
FROM unnest(@SettlementIds::uuid[], @Assets::text[], @Conditions::text[], @Settled::timestamptz[])
    AS x(settlement_id, asset, condition_id, settled_at);
""";

        var markerKey = EthLossDiffHistoryBackfillCommand.MarkerKey + "_exact52_" + batch;
        await using (var seedTransaction = await connection.BeginTransactionAsync())
        {
            await using (var seed = new NpgsqlCommand(seedSql, connection, seedTransaction))
            {
                seed.Parameters.AddWithValue("ParentStrategyId", EthLossDiffHistoryBackfillCommand.ParentStrategyId);
                seed.Parameters.AddWithValue("Wallet", parentWallet);
                seed.Parameters.AddWithValue("RunIds", sources.Select(source => source.RunId).ToArray());
                seed.Parameters.AddWithValue("SignalIds", sources.Select(source => source.SignalId).ToArray());
                seed.Parameters.AddWithValue("OrderIds", sources.Select(source => source.OrderId).ToArray());
                seed.Parameters.AddWithValue("FillIds", sources.Select(source => source.FillId).ToArray());
                seed.Parameters.AddWithValue("PositionIds", sources.Select(source => source.ParentPositionId).ToArray());
                seed.Parameters.AddWithValue("SettlementIds", sources.Select(source => source.ParentSettlementId).ToArray());
                seed.Parameters.AddWithValue("Assets", sources.Select(source => source.AssetId).ToArray());
                seed.Parameters.AddWithValue("Conditions", conditions);
                seed.Parameters.AddWithValue("Markets", markets);
                seed.Parameters.AddWithValue("Entered", entered);
                seed.Parameters.AddWithValue("Settled", settled);
                await seed.ExecuteNonQueryAsync();
            }
            await seedTransaction.CommitAsync();
        }

        string invariantBefore;
        await using (var before = await connection.BeginTransactionAsync())
        {
            invariantBefore = await EthLossDiffHistoryBackfillCommand.ReadInvariantDigestAsync(
                connection, before, CancellationToken.None);
            await before.RollbackAsync();
        }

        foreach (var entry in plan.Take(10))
        {
            Assert.True(await EthLossDiffHistoryBackfillCommand.ApplyOneEntryBatchAsync(
                connection, entry, CancellationToken.None));
        }

        await using (var interrupted = await connection.BeginTransactionAsync())
        {
            Assert.Equal(10L, await EthLossDiffHistoryBackfillCommand.ReadExactTargetChainCountAsync(
                connection, interrupted, plan, CancellationToken.None));
            Assert.Equal(60L, await EthLossDiffHistoryBackfillCommand.ReadTargetIdCollisionCountAsync(
                connection, interrupted, plan, CancellationToken.None));
            await using var absentMarker = new NpgsqlCommand(
                "SELECT count(*) FROM public.schema_data_migrations WHERE migration_key=@Key;",
                connection,
                interrupted);
            absentMarker.Parameters.AddWithValue("Key", markerKey);
            Assert.Equal(0L, Assert.IsType<long>(await absentMarker.ExecuteScalarAsync()));
            await interrupted.RollbackAsync();
        }

        var prematureMarker = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EthLossDiffHistoryBackfillCommand.InsertFinalMarkerAsync(
                connection, plan, markerKey, CancellationToken.None));
        Assert.Contains("exact_chains=10/52", prematureMarker.Message, StringComparison.Ordinal);

        var insertedOnResume = 0;
        foreach (var entry in plan)
        {
            if (await EthLossDiffHistoryBackfillCommand.ApplyOneEntryBatchAsync(
                    connection, entry, CancellationToken.None))
            {
                insertedOnResume++;
            }
        }
        Assert.Equal(42, insertedOnResume);
        await EthLossDiffHistoryBackfillCommand.InsertFinalMarkerAsync(
            connection, plan, markerKey, CancellationToken.None);

        foreach (var entry in plan)
        {
            Assert.False(await EthLossDiffHistoryBackfillCommand.ApplyOneEntryBatchAsync(
                connection, entry, CancellationToken.None));
        }

        string details;
        await using (var retry = await connection.BeginTransactionAsync())
        {
            await using var marker = new NpgsqlCommand(
                "SELECT details FROM public.schema_data_migrations WHERE migration_key=@Key;", connection, retry);
            marker.Parameters.AddWithValue("Key", markerKey);
            details = Assert.IsType<string>(await marker.ExecuteScalarAsync());
            Assert.True(EthLossDiffHistoryBackfillCommand.MarkerMatches(details, plan));
            Assert.Equal(invariantBefore, await EthLossDiffHistoryBackfillCommand.ReadInvariantDigestAsync(
                connection, retry, CancellationToken.None));
            Assert.Equal(52L, await EthLossDiffHistoryBackfillCommand.ReadExactTargetChainCountAsync(
                connection, retry, plan, CancellationToken.None));
            Assert.Equal(312L, await EthLossDiffHistoryBackfillCommand.ReadTargetIdCollisionCountAsync(
                connection, retry, plan, CancellationToken.None));
            await retry.RollbackAsync();
        }

        var first = plan[0];
        var second = plan[1];
        await using (var corrupted = await connection.BeginTransactionAsync())
        {
            await using var corrupt = new NpgsqlCommand(
                "UPDATE public.paper_orders SET signal_id=@OtherSignalId WHERE id=@OrderId;",
                connection,
                corrupted);
            corrupt.Parameters.AddWithValue("OtherSignalId",
                EthLossDiffHistoryBackfillCommand.DeterministicId(second.Child.Id, second.Source.RunId, "signal"));
            corrupt.Parameters.AddWithValue("OrderId",
                EthLossDiffHistoryBackfillCommand.DeterministicId(first.Child.Id, first.Source.RunId, "order"));
            Assert.Equal(1, await corrupt.ExecuteNonQueryAsync());
            await corrupted.CommitAsync();
        }

        var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EthLossDiffHistoryBackfillCommand.ApplyOneEntryBatchAsync(
                connection, first, CancellationToken.None));
        Assert.Contains("Partial or conflicting target chain", conflict.Message, StringComparison.Ordinal);
    }

    private static EthUp8LossDiffHistoryBackfillCommand.SourceRow Up8Row(
        int index,
        long entered,
        long settled,
        bool won)
    {
        var runId = GuidFromIndex(index);
        var signalId = GuidFromIndex(index + 1000);
        var orderId = GuidFromIndex(index + 2000);
        var fillId = GuidFromIndex(index + 3000);
        var positionId = GuidFromIndex(index + 4000);
        var settlementId = GuidFromIndex(index + 5000);
        var gross = won ? 1m : -1m;
        var fee = 0.01m;
        var net = gross - fee;
        var canonical = string.Join('|',
            runId.ToString("D"), entered.ToString(), settled.ToString(), won ? "1" : "0",
            "1.00000000", gross.ToString("F8", CultureInfo.InvariantCulture),
            fee.ToString("F8", CultureInfo.InvariantCulture), net.ToString("F8", CultureInfo.InvariantCulture),
            signalId.ToString("D"), orderId.ToString("D"), $"asset-{index}", "Up", "0.50000000",
            "2.00000000", "1.00000000", "btc_updown5m_fak_taker_paper", "1",
            fillId.ToString("D"), "0.50000000", "2.00000000",
            fee.ToString("F8", CultureInfo.InvariantCulture), string.Empty);

        return new EthUp8LossDiffHistoryBackfillCommand.SourceRow(
            canonical, "{}", positionId, settlementId, runId, entered, settled, won, 1m, gross, fee, net,
            signalId, orderId, $"asset-{index}", "Up", 0.5m, 2m, 1m,
            "btc_updown5m_fak_taker_paper", true, fillId, 0.5m, 2m, fee, null);
    }

    private static EthLossDiffHistoryBackfillCommand.SourceRow Row(
        int index,
        long entered,
        long settled,
        bool won)
    {
        var runId = GuidFromIndex(index);
        var signalId = GuidFromIndex(index + 1000);
        var orderId = GuidFromIndex(index + 2000);
        var fillId = GuidFromIndex(index + 3000);
        var positionId = GuidFromIndex(index + 4000);
        var settlementId = GuidFromIndex(index + 5000);
        var gross = won ? 1m : -1m;
        var fee = 0.01m;
        var net = gross - fee;
        var canonical = string.Join('|',
            runId.ToString("D"), entered.ToString(), settled.ToString(), won ? "1" : "0",
            "1.00000000", gross.ToString("F8", CultureInfo.InvariantCulture),
            fee.ToString("F8", CultureInfo.InvariantCulture), net.ToString("F8", CultureInfo.InvariantCulture),
            signalId.ToString("D"), orderId.ToString("D"), $"asset-{index}", "Up", "0.50000000",
            "2.00000000", "1.00000000", "btc_updown5m_fak_taker_paper", "1",
            fillId.ToString("D"), "0.50000000", "2.00000000",
            fee.ToString("F8", CultureInfo.InvariantCulture), string.Empty);

        return new EthLossDiffHistoryBackfillCommand.SourceRow(
            canonical, "{}", positionId, settlementId, runId, entered, settled, won, 1m, gross, fee, net, signalId, orderId,
            $"asset-{index}", "Up", 0.5m, 2m, 1m, "btc_updown5m_fak_taker_paper", true,
            fillId, 0.5m, 2m, fee, null);
    }

    private static Guid GuidFromIndex(int value) =>
        Guid.Parse($"{value:x8}-0000-4000-8000-{value:x12}");

    private static Guid[] ParseGuidEvidence(string evidence) =>
        evidence.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Guid.Parse)
            .ToArray();

    // Independently recalculated read-only from the frozen 1,022-row Production parent source on
    // 2026-08-28, then compared equal to the parent_run_id lineage in all 67 persisted child runs.
    private const string IndependentResetParentRunIds =
        "1c1b28c5-180e-44af-b63e-403e41f53880,e11faffa-f58e-421e-a4f0-6c4aa83529d0,bc1c5eed-057f-4a0d-815a-95d355622390,04087689-aa7c-4414-be73-fe822c2acf78,d1a9aa68-700e-4988-83d3-3cebb2c09094,748f7741-fefc-4a88-82f7-ad2659c751b6,113f7314-06bf-44ce-92c5-e90a25b507dd,6c83dfb2-308a-494e-819f-4d54cfcc03c2,b40deaa5-c3a7-43cd-8f92-1f0d4549a923,4f4f7b74-fa58-4d6d-811d-341786a261a3,5c0fe869-a9ef-4ddc-9b69-24f51ece859d,8e538bab-c1b8-4df1-8cc4-d4b179bc6963,7c2e5f43-d46f-4f75-a480-127db325bf22,8ab74b0b-ffe1-4ffa-b895-f9d4935c3243,ea04c5c4-d00f-4448-8a58-3116a387cebc,5fe3ca6f-ec1b-420f-b33a-d1f48bd1366f,c81d6e66-e32b-4f31-aacb-1e8e3fc6b1f6,8b297dbb-1be4-4c34-9fae-2a1f66ae12c2,ff4c0544-a705-4372-9aae-3aeb3e3ddbeb,fc02e0cb-c6e7-4eb3-98ba-8dbe9562a0a8,30fdf0b5-222d-405b-a5ea-f16c77f14096,a366cc34-e854-4875-bf88-96beaf3d50ab,a70b436a-b0e1-473b-b7c7-f01a40d9afc4,357e102c-5b64-47a1-8054-9d1e61b8c733,be977ba2-6dab-4b2b-92f8-819b5ece1695,ae6f8670-b42d-4e65-8ddb-7472a2c80144,9efb6885-3675-4000-83b1-20d64c051d5d,23d31391-d4b1-4c1d-adb4-e7e52c600d59,50a2442a-46f5-4543-bde5-6218a59144e2,3c535781-25f9-46de-92cf-0c2268c0c7c7,6557361b-9da5-4903-8e18-1cce2051f841,8f7243b3-0918-4192-a8be-46c5a3d28acb,7f9b3c58-cd77-454d-a1b2-ff199d977ae4,a0be10a6-e13e-47cb-bb33-573a6a9da1cf,140dec0e-a991-4834-a415-b3b656d4c7fd,1b5830a6-a3e0-4751-9974-cd94af959a82,3202f5b0-b84d-4999-92d7-148d48988a95,692322de-7345-47ae-98f9-1ebef673e9df,67fda39f-e871-4341-9f27-6cab7b1c34b6,8ba7e6be-8de6-4ec5-b73a-c0e8eb884566,bfb3c36e-f5eb-43bd-abdf-a150be4e7048,8d0b54de-49e8-492c-b206-da798cad391f,5bb3b089-888c-4a86-aa9b-61fc748052b1,e1769237-070d-4ccb-8b02-ff975c79f58e,3bfcb857-6373-4338-9002-b7843d9907dc,20e2e7f5-e1b9-460a-afcd-821251a4ec63";

    private const string IndependentPositiveParentRunIds =
        "a8102575-8979-44de-bcdb-6abcbf71f032,5bb3b089-888c-4a86-aa9b-61fc748052b1,d9f12b60-e7a7-4861-8e40-d6319c7843c5,fdb09d4f-11b9-4cbc-844f-e67e16cfcd08,4ad54b5d-eaa9-44f2-80f4-1bc379dc8964,40c34fe0-c1a8-4436-99c9-36a0e1103fc6,45e48073-b49f-4689-b7aa-57c804429be4,8ac0204c-bafc-455a-91e3-bad6af4bdf43,0c4a8f00-2af6-442d-91bb-ed0f5663b7bd,6d3d9dcf-511e-48e6-baf3-59a7d004223d,e5ea9d0f-1fb4-42a7-85d4-b6222afe87ac,94dddc80-43ed-4071-81dc-bea9cd2d99ce,e19375f4-90f4-46ae-bfc2-823a34580ded,c1585a6a-decc-404a-b660-28d6e802257c,7eaf6696-93ee-4911-9e96-aee44b84dae9,e52c22f5-5c9a-46f3-a31e-d5d0e1107dc7,e1769237-070d-4ccb-8b02-ff975c79f58e,2aae4960-cc9c-45bd-939b-c7a5d512c71f,217e045d-966c-4a0c-9a27-4c7820af76e6,8cbcd2b8-b077-4ce4-8db0-005f72ab80a9,d86bbb48-e765-407b-aa3a-03c963305c2b";
}
