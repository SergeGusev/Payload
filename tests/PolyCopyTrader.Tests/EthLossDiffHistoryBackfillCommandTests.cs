using System.Globalization;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Startup;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class EthLossDiffHistoryBackfillCommandTests
{
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
}
