using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class HistoricalGrossNetParityPostgresIntegrationTests
{
    private static readonly Guid StrategyId =
        Guid.Parse("b7c50005-0000-4000-8179-000000000102");
    private static readonly Guid UnrelatedStrategyId =
        Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid RunlessStrategyId =
        Guid.Parse("b7c50005-0000-4000-8179-000000000103");
    private static readonly Guid PaperOrderId =
        Guid.Parse("a1000000-0000-4000-8000-000000000002");
    private static readonly Guid SecondPaperOrderId =
        Guid.Parse("a1000000-0000-4000-8000-000000000005");
    private static readonly Guid DonorRunId =
        Guid.Parse("a1000000-0000-4000-8000-000000000003");
    private static readonly Guid TargetRunId =
        Guid.Parse("a1000000-0000-4000-8000-000000000004");
    private static readonly Guid PaperPositionId =
        Guid.Parse("a1000000-0000-4000-8000-000000000006");
    private static readonly Guid LiveDonorOrderId =
        Guid.Parse("a1000000-0000-4000-8000-000000000007");
    private static readonly Guid LiveTargetOrderId =
        Guid.Parse("a1000000-0000-4000-8000-000000000008");
    private static readonly Guid ConsumedLinkedPaperOrderId =
        Guid.Parse("a1000000-0000-4000-8000-000000000009");
    private static readonly Guid ConsumedSellPaperOrderId =
        Guid.Parse("a1000000-0000-4000-8000-00000000000a");
    private static readonly Guid RemainingPaperOrderId =
        Guid.Parse("a1000000-0000-4000-8000-00000000000b");
    private static readonly Guid ConsumedPoolPositionId =
        Guid.Parse("a1000000-0000-4000-8000-00000000000c");
    private static readonly Guid ConsumedLiveOrderId =
        Guid.Parse("a1000000-0000-4000-8000-00000000000d");
    private static readonly Guid ZeroBasisLiveOrderId =
        Guid.Parse("a1000000-0000-4000-8000-00000000000e");
    private static readonly Guid RunlessBuyOrderOneId =
        Guid.Parse("a2000000-0000-4000-8000-000000000001");
    private static readonly Guid RunlessBuyOrderTwoId =
        Guid.Parse("a2000000-0000-4000-8000-000000000002");
    private static readonly Guid RunlessSellOrderId =
        Guid.Parse("a2000000-0000-4000-8000-000000000003");
    private static readonly Guid RunlessBuyFillOneId =
        Guid.Parse("a2000000-0000-4000-8000-000000000011");
    private static readonly Guid RunlessBuyFillTwoId =
        Guid.Parse("a2000000-0000-4000-8000-000000000012");
    private static readonly Guid RunlessSellFillId =
        Guid.Parse("a2000000-0000-4000-8000-000000000013");
    private static readonly Guid PaginationStrategyOneId =
        Guid.Parse("b3000000-0000-4000-8100-000000000001");
    private static readonly Guid PaginationStrategyTwoId =
        Guid.Parse("b3000000-0000-4000-8100-000000000002");
    private static readonly Guid PaginationAuditedRunId =
        Guid.Parse("b3000000-0000-4000-8300-000000000001");
    private static readonly Guid PaginationPostCutoffRunId =
        Guid.Parse("b3000000-0000-4000-8300-000000000002");

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task StrategyRankingAndScopedCandidatePage_SelectGreatestGrossStrategyOnly()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var firstStrategyRunIds = Enumerable.Range(1, 120)
            .Select(value => Guid.Parse($"b3000000-0000-4000-8101-{value:000000000000}"))
            .ToArray();
        var secondStrategyRunIds = Enumerable.Range(1, 3)
            .Select(value => Guid.Parse($"b3000000-0000-4000-8102-{value:000000000000}"))
            .ToArray();
        await SeedCandidatePaginationAsync(factory, firstStrategyRunIds, secondStrategyRunIds);

        var ranking = await repository.LoadHistoricalGrossNetParityStrategyRankingAsync(
            new HistoricalGrossNetParityStrategyRankingRequest(30, 1_000));
        var firstStrategy = ranking.Single(value => value.StrategyId == PaginationStrategyOneId);
        var secondStrategy = ranking.Single(value => value.StrategyId == PaginationStrategyTwoId);
        Assert.True(
            firstStrategy.StrategyRank < secondStrategy.StrategyRank);

        var first = await repository.LoadHistoricalGrossNetParityCandidatePageAsync(
            new HistoricalGrossNetParityCandidatePageRequest(
                HistoricalGrossNetParityProcessingPhase.Exact,
                HistoricalGrossNetParityConstants.CutoffUtc,
                50,
                null,
                30,
                1_000,
                HistoricalGrossNetParityConstants.CalculationVersion,
                firstStrategy));

        Assert.Equal(HistoricalGrossNetParityReadStatus.Complete, first.Status);
        Assert.Equal(50, first.Candidates.Count);
        Assert.False(first.ReachedBoundary);
        Assert.All(first.Candidates,
            candidate => Assert.Equal(PaginationStrategyOneId, candidate.StrategyId));
        Assert.DoesNotContain(first.Candidates,
            candidate => candidate.StrategyId == PaginationStrategyTwoId);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task CandidatePage_StrategyScopeFinishesSelectedStrategyWithoutCrossingToNext()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var firstStrategyRunIds = Enumerable.Range(1, 120)
            .Select(value => Guid.Parse($"b3000000-0000-4000-8101-{value:000000000000}"))
            .ToArray();
        var secondStrategyRunIds = Enumerable.Range(1, 3)
            .Select(value => Guid.Parse($"b3000000-0000-4000-8102-{value:000000000000}"))
            .ToArray();
        await SeedCandidatePaginationAsync(factory, firstStrategyRunIds, secondStrategyRunIds);
        var selectedStrategy = await LoadRankedStrategyAsync(repository, PaginationStrategyOneId);

        HistoricalGrossNetParityCandidateCursor? cursor = null;
        var scoped = new List<HistoricalGrossNetParityCandidateKey>();
        for (var pageNumber = 0; pageNumber < 3; pageNumber++)
        {
            var page = await repository.LoadHistoricalGrossNetParityCandidatePageAsync(
                new HistoricalGrossNetParityCandidatePageRequest(
                    HistoricalGrossNetParityProcessingPhase.Exact,
                    HistoricalGrossNetParityConstants.CutoffUtc,
                    50,
                    cursor,
                    30,
                    1_000,
                    HistoricalGrossNetParityConstants.CalculationVersion,
                    selectedStrategy));
            Assert.Equal(HistoricalGrossNetParityReadStatus.Complete, page.Status);
            Assert.All(page.Candidates,
                candidate => Assert.Equal(PaginationStrategyOneId, candidate.StrategyId));
            scoped.AddRange(page.Candidates);
            cursor = page.NextCursor;
            Assert.Equal(pageNumber == 2, page.ReachedBoundary);
        }

        Assert.Equal(firstStrategyRunIds, scoped.Select(candidate => candidate.SourceId));
        Assert.DoesNotContain(scoped, candidate => candidate.StrategyId == PaginationStrategyTwoId);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task MixedRunDonor_IsReplayedAtTargetTime_AndStrategyPathUsesSettledIndex()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        await SeedAsync(factory);
        var selectedStrategy = await LoadRankedStrategyAsync(repository, StrategyId);
        Assert.True(await CountPaperPositionSettlementsAsync(factory) > 250);
        var settlementPlan = await ReadCanonicalSettlementPlanAsync(factory);
        Assert.Contains(
            "ix_paper_position_settlements_wallet_time",
            settlementPlan,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Seq Scan on paper_position_settlements",
            settlementPlan,
            StringComparison.Ordinal);

        var page = await repository.LoadHistoricalGrossNetParityCandidatePageAsync(
            new HistoricalGrossNetParityCandidatePageRequest(
                HistoricalGrossNetParityProcessingPhase.Exact,
                HistoricalGrossNetParityConstants.CutoffUtc,
                50,
                null,
                30,
                1_000,
                HistoricalGrossNetParityConstants.CalculationVersion,
                selectedStrategy));
        Assert.Equal(HistoricalGrossNetParityReadStatus.Complete, page.Status);
        var prepared = HistoricalGrossNetParityPaperPreparer.Prepare(
            page,
            HistoricalGrossNetParityConstants.CutoffUtc);
        Assert.DoesNotContain(prepared.Conflicts, value => value.StrategyId == StrategyId);
        var target = Assert.Single(prepared.Targets, value => value.SourceId == TargetRunId);

        var matcher = new HistoricalGrossNetDonorMatcher();
        var descriptor = Assert.Single(
            matcher.GetOrderedCandidates(new HistoricalGrossNetDonorTarget(StrategyId)),
            value => value.StrategyId == StrategyId);
        var preview = await repository.LoadHistoricalGrossNetParityDonorPreviewAsync(
            new HistoricalGrossNetParityDonorPreviewRequest(
                target.SourceKind,
                target.SourceId,
                target.StrategyId,
                target.TargetTupleHash,
                [descriptor],
                0,
                64,
                30,
                1_000));

        Assert.True(
            preview.Status == HistoricalGrossNetParityReadStatus.Complete,
            preview.Details);
        var aggregate = Assert.Single(preview.Aggregates);
        Assert.Equal(304, aggregate.RawDonorCount);
        Assert.Equal(303, aggregate.ExactDonorCount);
        Assert.Equal(303, aggregate.DeduplicatedDonorCount);
        Assert.Equal(3.90m, aggregate.N);
        Assert.Equal(330m, aggregate.D);
        Assert.NotEqual(
            HistoricalGrossNetDonorHashV1.ComputeMembershipHash([]),
            aggregate.MembershipHashV1);

        var positionTarget = Assert.Single(
            prepared.Targets,
            value => value.SourceKind == HistoricalGrossNetParitySourceKind.PaperPosition &&
                     value.SourceId == PaperPositionId);
        var positionPreview = await repository.LoadHistoricalGrossNetParityDonorPreviewAsync(
            new HistoricalGrossNetParityDonorPreviewRequest(
                positionTarget.SourceKind,
                positionTarget.SourceId,
                positionTarget.StrategyId,
                positionTarget.TargetTupleHash,
                [descriptor],
                0,
                64,
                30,
                1_000));
        Assert.True(
            positionPreview.Status == HistoricalGrossNetParityReadStatus.Complete,
            positionPreview.Details);

        var runlessStrategy = await LoadRankedStrategyAsync(repository, RunlessStrategyId);
        var runlessPage = await repository.LoadHistoricalGrossNetParityCandidatePageAsync(
            new HistoricalGrossNetParityCandidatePageRequest(
                HistoricalGrossNetParityProcessingPhase.Exact,
                HistoricalGrossNetParityConstants.CutoffUtc,
                50,
                null,
                30,
                1_000,
                HistoricalGrossNetParityConstants.CalculationVersion,
                runlessStrategy));
        Assert.Equal(HistoricalGrossNetParityReadStatus.Complete, runlessPage.Status);
        var runlessPrepared = HistoricalGrossNetParityPaperPreparer.Prepare(
            runlessPage,
            HistoricalGrossNetParityConstants.CutoffUtc);
        var runlessTarget = Assert.Single(
            runlessPrepared.Targets,
            value => value.SourceKind == HistoricalGrossNetParitySourceKind.PaperSellFill &&
                     value.SourceId == RunlessSellFillId);
        var runlessDescriptor = Assert.Single(
            matcher.GetOrderedCandidates(new HistoricalGrossNetDonorTarget(RunlessStrategyId)),
            value => value.StrategyId == RunlessStrategyId);
        var runlessPreview = await repository.LoadHistoricalGrossNetParityDonorPreviewAsync(
            new HistoricalGrossNetParityDonorPreviewRequest(
                HistoricalGrossNetParitySourceKind.PaperSellFill,
                RunlessSellFillId,
                RunlessStrategyId,
                runlessTarget.TargetTupleHash,
                [runlessDescriptor],
                0,
                64,
                30,
                1_000));
        Assert.True(
            runlessPreview.Status == HistoricalGrossNetParityReadStatus.Complete,
            runlessPreview.Details);
        var runlessAggregate = Assert.Single(runlessPreview.Aggregates);
        Assert.Equal(1, runlessAggregate.RawDonorCount);
        Assert.Equal(1, runlessAggregate.ExactDonorCount);
        Assert.Equal(1, runlessAggregate.DeduplicatedDonorCount);
        Assert.Equal(0.20m, runlessAggregate.N);
        Assert.Equal(5.00m, runlessAggregate.D);
        Assert.NotEqual(
            HistoricalGrossNetDonorHashV1.ComputeMembershipHash([]),
            runlessAggregate.MembershipHashV1);

        var liveTarget = Assert.Single(
            prepared.Targets,
            value => value.SourceKind == HistoricalGrossNetParitySourceKind.LiveOrder &&
                     value.SourceId == LiveTargetOrderId);
        var livePreview = await repository.LoadHistoricalGrossNetParityDonorPreviewAsync(
            new HistoricalGrossNetParityDonorPreviewRequest(
                liveTarget.SourceKind,
                liveTarget.SourceId,
                liveTarget.StrategyId,
                liveTarget.TargetTupleHash,
                [descriptor],
                0,
                64,
                30,
                1_000));
        Assert.True(livePreview.Status == HistoricalGrossNetParityReadStatus.Complete, livePreview.Details);
        var liveAggregate = Assert.Single(livePreview.Aggregates);
        Assert.Equal(308, liveAggregate.RawDonorCount);
        Assert.Equal(305, liveAggregate.ExactDonorCount);
        Assert.Equal(303, liveAggregate.DeduplicatedDonorCount);
        Assert.Equal(3.60m, liveAggregate.N);
        Assert.Equal(320m, liveAggregate.D);

        var plan = await ReadSettledRunPlanAsync(factory);
        Assert.Contains(
            "ix_strategy_market_paper_runs_strategy_settled",
            plan,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan on strategy_market_paper_runs", plan, StringComparison.Ordinal);

        var baseConnectionString = Environment.GetEnvironmentVariable(
            "POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(baseConnectionString));
        var sequentialConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString!)
        {
            Options = "-c enable_indexscan=off -c enable_bitmapscan=off"
        };
        var sequentialRepository = new PostgresAppRepository(
            new PostgresConnectionFactory(
                new StorageOptions { ConnectionString = sequentialConnectionString.ConnectionString }));
        var rejectedPreview = await sequentialRepository.LoadHistoricalGrossNetParityDonorPreviewAsync(
            new HistoricalGrossNetParityDonorPreviewRequest(
                target.SourceKind,
                target.SourceId,
                target.StrategyId,
                target.TargetTupleHash,
                [descriptor],
                0,
                64,
                30,
                1_000));
        Assert.Equal(HistoricalGrossNetParityReadStatus.DeferredOperational, rejectedPreview.Status);
        Assert.Contains("strategy_market_paper_runs", rejectedPreview.Details, StringComparison.Ordinal);
        Assert.Contains("more than 250 rows", rejectedPreview.Details, StringComparison.Ordinal);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectFixedPaperDecision_AppliesAtomicallyWithoutDonorCandidates()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var enteredAtUtc = HistoricalGrossNetParityConstants.CutoffUtc.AddDays(-2);
        var settledAtUtc = HistoricalGrossNetParityConstants.CutoffUtc.AddDays(-1);

        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync();
            await using var seed = new NpgsqlCommand(
                """
INSERT INTO strategies (
    id, code, name, description, enabled, live_stakes, created_at_utc, updated_at_utc)
VALUES (
    @StrategyId, @Code, @Name, 'integration fixture',
    true, false, @EnteredAtUtc, @EnteredAtUtc);

INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title,
    detected_at_utc, entry_due_at_utc, status, selected_asset_id, selected_outcome,
    entry_price, stake_usd, size_shares, entered_at_utc, settlement_price,
    settlement_value_usd, realized_pnl_usd, fee_usd, fee_accounting_status,
    fee_liquidity_role, fee_calculation_source, net_realized_pnl_usd,
    settled_at_utc, retention_scope, created_at_utc, updated_at_utc)
VALUES (
    @RunId, @StrategyId, @MarketId, @ConditionId, @MarketId, 'direct fixed target',
    @EnteredAtUtc, @EnteredAtUtc, 'Settled', 'asset-direct-fixed', 'Yes',
    0.50, 12.34, 24.68, @EnteredAtUtc, 0.90,
    22.34, 10.00, 0, 'LegacyUnknown', 'Unknown', '', NULL,
    @SettledAtUtc, 'PaperOnly', @EnteredAtUtc, @SettledAtUtc);
""",
                connection);
            seed.Parameters.AddWithValue("StrategyId", strategyId);
            seed.Parameters.AddWithValue("RunId", runId);
            seed.Parameters.AddWithValue("Code", "historical_parity_direct_fixed_" + strategyId.ToString("N"));
            seed.Parameters.AddWithValue("Name", "direct fixed integration " + strategyId.ToString("N"));
            seed.Parameters.AddWithValue("MarketId", "direct-fixed-" + runId.ToString("N"));
            seed.Parameters.AddWithValue("ConditionId", "direct-fixed-condition-" + runId.ToString("N"));
            seed.Parameters.AddWithValue("EnteredAtUtc", enteredAtUtc.UtcDateTime);
            seed.Parameters.AddWithValue("SettledAtUtc", settledAtUtc.UtcDateTime);
            await seed.ExecuteNonQueryAsync();
        }

        var selectedStrategy = await LoadRankedStrategyAsync(repository, strategyId);
        var page = await repository.LoadHistoricalGrossNetParityCandidatePageAsync(
            new HistoricalGrossNetParityCandidatePageRequest(
                HistoricalGrossNetParityProcessingPhase.Fallback,
                HistoricalGrossNetParityConstants.CutoffUtc,
                50,
                null,
                30,
                1_000,
                HistoricalGrossNetParityConstants.CalculationVersion,
                selectedStrategy));
        Assert.Equal(HistoricalGrossNetParityReadStatus.Complete, page.Status);
        var prepared = HistoricalGrossNetParityPaperPreparer.Prepare(
            page,
            HistoricalGrossNetParityConstants.CutoffUtc);
        var target = Assert.Single(prepared.Targets, value => value.SourceId == runId);
        var decision = HistoricalGrossNetParityDecisionFactory.CreateFallback(
            target,
            DateTimeOffset.UtcNow,
            HistoricalGrossNetParityConstants.CalculationVersion);

        var result = await repository.TryApplyHistoricalGrossNetParityPaperDecisionAsync(
            new HistoricalGrossNetParityPaperDecisionRequest(
                target,
                decision,
                [],
                HistoricalGrossNetParityConstants.CutoffUtc,
                50,
                30,
                1_000,
                HistoricalGrossNetParityConstants.CalculationVersion));

        Assert.Equal(HistoricalGrossNetParityApplyStatus.Applied, result.Status);
        Assert.Equal(HistoricalGrossNetParityDecisionKind.Fixed0p0333, decision.DecisionKind);
        Assert.Equal(0.41092200m, decision.ContributionEffectiveFeeUsd);
        Assert.Equal(9.58907800m, decision.NetPnlUsd);
        Assert.Null(decision.DonorDecision);

        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync();
            await using var verify = new NpgsqlCommand(
                """
SELECT r.fee_usd,
       r.net_realized_pnl_usd,
       r.fee_calculation_source,
       count(a.audit_id)
FROM strategy_market_paper_runs r
LEFT JOIN historical_gross_net_parity_audit a
  ON a.source_kind = 'PaperRun'
 AND a.source_id = r.id
 AND a.calculation_version = @CalculationVersion
 AND a.operation_kind = 'AccountingDecision'
WHERE r.id = @RunId
GROUP BY r.fee_usd, r.net_realized_pnl_usd, r.fee_calculation_source;
""",
                connection);
            verify.Parameters.AddWithValue("RunId", runId);
            verify.Parameters.AddWithValue(
                "CalculationVersion",
                HistoricalGrossNetParityConstants.CalculationVersion);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0.41092200m, reader.GetDecimal(0));
            Assert.Equal(9.58907800m, reader.GetDecimal(1));
            Assert.Equal(
                "historical-gross-net-parity-fixed-net-roi-minus-3p33-v1",
                reader.GetString(2));
            Assert.Equal(1, reader.GetInt64(3));
            Assert.False(await reader.ReadAsync());
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectFixedLiveDecision_UsesCanonicalHashAndAppliesOrderedBalanceOnlyOnce()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var fixture = await SeedLiveHashFixtureAsync(factory);
        var targets = await LoadLiveHashTargetsAsync(repository, fixture.StrategyId);
        Assert.Equal(2, targets.Count);
        Assert.DoesNotContain(targets, target => target.SourceId == fixture.PostCutoffId);
        var first = Assert.Single(targets, target => target.SourceId == fixture.FirstId);
        var second = Assert.Single(targets, target => target.SourceId == fixture.SecondId);

        foreach (var target in new[] { first, second })
        {
            var request = LiveHashAccountingRequest(target);
            var result = await repository.TryApplyHistoricalGrossNetParityLiveAccountingAsync(request);
            // This real reader-to-writer call failed with InvariantConflict before the hash fix.
            Assert.True(result.Status == HistoricalGrossNetParityApplyStatus.Applied, result.Details);
            Assert.Equal(HistoricalGrossNetParityOwnership.Pending, result.Ownership);
            Assert.Equal(HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash(target.ProvedComponents),
                target.ComponentHash);
            Assert.Equal(HistoricalGrossNetParityBindingV1.Compute(
                target.TargetTupleHash, target.LineageHash, target.ComponentHash), target.BindingHash);
            Assert.Equal(0.41092200m, request.Decision.StoredFeeUsd);
            Assert.Equal(9.58907800m, request.Decision.NetPnlUsd);
            Assert.Null(request.Decision.DonorDecision);
            Assert.Equal(HistoricalGrossNetParityApplyStatus.TerminalNoOp,
                (await repository.TryApplyHistoricalGrossNetParityLiveAccountingAsync(request)).Status);
        }

        Assert.Equal(HistoricalGrossNetParityApplyStatus.NotEarliest,
            (await repository.TryApplyHistoricalGrossNetParityEarliestLiveBalanceAsync(
                LiveHashBalanceRequest(second))).Status);
        foreach (var target in new[] { first, second })
        {
            var request = LiveHashBalanceRequest(target);
            var result = await repository.TryApplyHistoricalGrossNetParityEarliestLiveBalanceAsync(request);
            Assert.True(result.Status == HistoricalGrossNetParityApplyStatus.Applied, result.Details);
            Assert.Equal(HistoricalGrossNetParityOwnership.Completed, result.Ownership);
            Assert.Equal(-0.41092200m, result.ActualAppliedDelta);
            Assert.Equal(0m, result.ResidualUnappliedDelta);
            Assert.Equal(HistoricalGrossNetParityApplyStatus.TerminalNoOp,
                (await repository.TryApplyHistoricalGrossNetParityEarliestLiveBalanceAsync(request)).Status);
            Assert.Equal(HistoricalGrossNetParityApplyStatus.TerminalNoOp,
                (await repository.TryApplyHistoricalGrossNetParityLiveAccountingAsync(
                    LiveHashAccountingRequest(target))).Status);
        }

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var verify = new NpgsqlCommand(
            """
SELECT strategy.live_available_balance,
       (SELECT count(*) FROM live_orders o WHERE o.strategy_id = strategy.id
          AND o.fee_usd = 0.410922 AND o.net_realized_pnl_usd = 9.589078
          AND o.realized_pnl_usd = 10 AND o.filled_notional_usd = 12.34
          AND o.historical_gross_net_parity_ownership = 'Completed'),
       (SELECT count(*) FROM historical_gross_net_parity_audit a WHERE a.strategy_id = strategy.id
          AND a.operation_kind = 'AccountingBaseline' AND a.baseline_effect_kind = 'LegacyGrossApplied'),
       (SELECT count(*) FROM historical_gross_net_parity_audit a WHERE a.strategy_id = strategy.id
          AND a.operation_kind = 'AccountingDecision' AND a.decision_kind = 'Fixed0p0333'),
       (SELECT count(*) FROM historical_gross_net_parity_audit a WHERE a.strategy_id = strategy.id
          AND a.operation_kind = 'InitialBalanceApplication'),
       (SELECT sum(a.actual_applied_delta) FROM historical_gross_net_parity_audit a
          WHERE a.strategy_id = strategy.id),
       (SELECT net_realized_pnl_usd IS NULL AND fee_usd = 0
               AND historical_gross_net_parity_ownership = 'None'
          FROM live_orders WHERE id = @PostCutoffId)
FROM strategies strategy WHERE strategy.id = @StrategyId;
""", connection);
        verify.Parameters.AddWithValue("StrategyId", fixture.StrategyId);
        verify.Parameters.AddWithValue("PostCutoffId", fixture.PostCutoffId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(79.17815600m, reader.GetDecimal(0));
        for (var index = 1; index <= 4; index++) Assert.Equal(2, reader.GetInt64(index));
        Assert.Equal(-0.82184400m, reader.GetDecimal(5));
        Assert.True(reader.GetBoolean(6));
        Assert.False(await reader.ReadAsync());
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LiveCanonicalHash_RejectsTamperedComponentsAndStaleRowWithoutWritingAccounting()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var fixture = await SeedLiveHashFixtureAsync(factory);
        var targets = await LoadLiveHashTargetsAsync(repository, fixture.StrategyId);
        var target = Assert.Single(targets, value => value.SourceId == fixture.FirstId);
        var wrongComponentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("{}")))
            .ToLowerInvariant();
        var tampered = target with
        {
            ComponentHash = wrongComponentHash,
            BindingHash = HistoricalGrossNetParityBindingV1.Compute(
                target.TargetTupleHash, target.LineageHash, wrongComponentHash)
        };
        var rejected = await repository.TryApplyHistoricalGrossNetParityLiveAccountingAsync(
            LiveHashAccountingRequest(tampered));
        Assert.Equal(HistoricalGrossNetParityApplyStatus.InvariantConflict, rejected.Status);
        Assert.Contains("binding hashes", rejected.Details, StringComparison.Ordinal);

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using (var change = new NpgsqlCommand(
            "UPDATE live_orders SET row_version = row_version + 1 WHERE id = @Id;", connection))
        {
            change.Parameters.AddWithValue("Id", target.SourceId);
            Assert.Equal(1, await change.ExecuteNonQueryAsync());
        }
        var stale = await repository.TryApplyHistoricalGrossNetParityLiveAccountingAsync(
            LiveHashAccountingRequest(target));
        Assert.Equal(HistoricalGrossNetParityApplyStatus.DeferredCas, stale.Status);

        await using var verify = new NpgsqlCommand(
            """
SELECT (SELECT count(*) FROM historical_gross_net_parity_audit WHERE strategy_id = @StrategyId),
       (SELECT count(*) FROM live_orders WHERE strategy_id = @StrategyId
          AND net_realized_pnl_usd IS NULL AND fee_usd = 0
          AND historical_gross_net_parity_ownership = 'None'),
       (SELECT live_available_balance FROM strategies WHERE id = @StrategyId);
""", connection);
        verify.Parameters.AddWithValue("StrategyId", fixture.StrategyId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt64(0));
        Assert.Equal(3, reader.GetInt64(1));
        Assert.Equal(80m, reader.GetDecimal(2));
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LiveCanonicalHash_PreservesExactFeesAndIndependentVenueEvidence()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        foreach (var feeStatus in new[] { "Calculated", "VenueReported" })
        {
            var fixture = await SeedLiveHashFixtureAsync(factory, feeStatus);
            await using var connection = factory.CreateConnection();
            await connection.OpenAsync();
            if (feeStatus == "VenueReported")
            {
                await using var venue = new NpgsqlCommand(
                    """
INSERT INTO historical_gross_net_parity_audit (
    audit_id, source_kind, source_id, strategy_id, calculation_version,
    operation_kind, evidence_version, old_payload_json, new_payload_json, evidence_payload_json)
SELECT gen_random_uuid(), 'LiveOrder', id, strategy_id, @Version,
       'VenueReportedRevision', 'venue-hash-regression', '{}'::jsonb, '{}'::jsonb,
       '{"authority":"hash-regression","fee_usd":0.2}'::jsonb
FROM live_orders WHERE strategy_id = @StrategyId;
""", connection);
                venue.Parameters.AddWithValue("StrategyId", fixture.StrategyId);
                venue.Parameters.AddWithValue("Version", HistoricalGrossNetParityConstants.CalculationVersion);
                Assert.Equal(3, await venue.ExecuteNonQueryAsync());
            }

            var targets = await LoadLiveHashTargetsAsync(repository, fixture.StrategyId);
            Assert.Equal(2, targets.Count);
            Assert.DoesNotContain(targets, value => value.SourceId == fixture.PostCutoffId);
            foreach (var id in new[] { fixture.FirstId, fixture.SecondId })
            {
                var target = Assert.Single(targets, value => value.SourceId == id);
                Assert.Equal(HistoricalGrossNetParityExactEligibility.ExistingExactPreserved, target.ExactEligibility);
                Assert.Equal(HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash(target.ProvedComponents),
                    target.ComponentHash);
                if (feeStatus == "VenueReported")
                {
                    using var payload = JsonDocument.Parse(target.ComponentPayloadJson);
                    Assert.Equal("hash-regression", payload.RootElement.GetProperty("authority").GetString());
                    var evidence = Assert.Single(target.ExactEvidenceReferences);
                    Assert.Equal("LiveVenueReported", evidence.EvidenceKind);
                    Assert.Equal("venue-hash-regression", evidence.EvidenceVersion);
                    Assert.Equal(Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(target.ComponentPayloadJson))).ToLowerInvariant(), evidence.EvidenceHash);
                    Assert.NotEqual(target.ComponentHash, evidence.EvidenceHash);
                }
                var decision = HistoricalGrossNetParityDecisionFactory.TryCreateExact(
                    target, [], DateTimeOffset.UtcNow, HistoricalGrossNetParityConstants.CalculationVersion);
                Assert.NotNull(decision);
                Assert.Equal(HistoricalGrossNetParityDecisionKind.ExistingExactPreserved, decision.DecisionKind);
                var request = LiveHashAccountingRequest(target) with { Decision = decision };
                var result = await repository.TryApplyHistoricalGrossNetParityLiveAccountingAsync(request);
                Assert.True(result.Status == HistoricalGrossNetParityApplyStatus.Applied, result.Details);
                var balance = await repository.TryApplyHistoricalGrossNetParityEarliestLiveBalanceAsync(
                    LiveHashBalanceRequest(target));
                Assert.True(balance.Status == HistoricalGrossNetParityApplyStatus.Applied, balance.Details);
                Assert.Equal(0m, balance.ActualAppliedDelta);
            }

            await using var verify = new NpgsqlCommand(
                """
SELECT (SELECT count(*) FROM live_orders WHERE strategy_id = @StrategyId
          AND fee_usd = 0.2 AND net_realized_pnl_usd = 9.8 AND fee_accounting_status = @Status
          AND fee_calculated_at_utc = @FeeAt AND realized_pnl_usd = 10),
       (SELECT live_available_balance FROM strategies WHERE id = @StrategyId),
       (SELECT count(*) FROM historical_gross_net_parity_audit WHERE strategy_id = @StrategyId
          AND operation_kind = 'AccountingDecision' AND decision_kind = 'ExistingExactPreserved');
""", connection);
            verify.Parameters.AddWithValue("StrategyId", fixture.StrategyId);
            verify.Parameters.AddWithValue("Status", feeStatus);
            verify.Parameters.AddWithValue("FeeAt", HistoricalGrossNetParityConstants.CutoffUtc.AddDays(-1).UtcDateTime);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(3, reader.GetInt64(0));
            Assert.Equal(80m, reader.GetDecimal(1));
            Assert.Equal(2, reader.GetInt64(2));
        }
    }

    private static HistoricalGrossNetParityLiveAccountingRequest LiveHashAccountingRequest(
        HistoricalGrossNetParityTargetSnapshot target) => new(
        target,
        HistoricalGrossNetParityDecisionFactory.CreateFallback(
            target, DateTimeOffset.UtcNow, HistoricalGrossNetParityConstants.CalculationVersion),
        [], HistoricalGrossNetParityConstants.CutoffUtc, 50, 30, 1_000,
        HistoricalGrossNetParityConstants.CalculationVersion);

    private static HistoricalGrossNetParityLiveBalanceRequest LiveHashBalanceRequest(
        HistoricalGrossNetParityTargetSnapshot target) => new(
        target.StrategyId, target.SourceId, HistoricalGrossNetParityConstants.CutoffUtc,
        30, 1_000, HistoricalGrossNetParityConstants.CalculationVersion);

    private static async Task<IReadOnlyList<HistoricalGrossNetParityTargetSnapshot>> LoadLiveHashTargetsAsync(
        PostgresAppRepository repository, Guid strategyId)
    {
        var selectedStrategy = await LoadRankedStrategyAsync(repository, strategyId);
        var page = await repository.LoadHistoricalGrossNetParityCandidatePageAsync(
            new HistoricalGrossNetParityCandidatePageRequest(
                HistoricalGrossNetParityProcessingPhase.Exact,
                HistoricalGrossNetParityConstants.CutoffUtc, 50, null, 30, 1_000,
                HistoricalGrossNetParityConstants.CalculationVersion, selectedStrategy));
        Assert.True(page.Status == HistoricalGrossNetParityReadStatus.Complete, page.Details);
        var prepared = HistoricalGrossNetParityPaperPreparer.Prepare(page, HistoricalGrossNetParityConstants.CutoffUtc);
        Assert.Empty(prepared.Conflicts);
        return prepared.Targets;
    }

    private static async Task<(Guid StrategyId, Guid FirstId, Guid SecondId, Guid PostCutoffId)>
        SeedLiveHashFixtureAsync(PostgresConnectionFactory factory, string feeStatus = "LegacyUnknown")
    {
        var strategyId = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var seed = new NpgsqlCommand(
            """
INSERT INTO strategies (id, code, name, description, enabled, live_stakes,
                        live_available_balance, created_at_utc, updated_at_utc)
VALUES (@StrategyId, @Code, @Code, 'Live hash regression', true, false, 80, @EnteredAt, @EnteredAt);
INSERT INTO live_orders (
    id, signal_id, strategy_id, status, order_id, side, asset_id, condition_id,
    outcome, price, size_shares, notional_usd, order_type, created_at_utc,
    expires_at_utc, submitted_at_utc, response_status, filled_size, remaining_size,
    average_fill_price, filled_notional_usd, cost_basis_usd, fee_usd,
    fee_accounting_status, fee_liquidity_role, fee_calculation_source,
    fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc,
    cancel_status, raw_response_json, validation_summary, balance_effect_applied,
    settlement_value_usd, realized_pnl_usd, net_realized_pnl_usd,
    settled_at_utc, winning_asset_id, winning_outcome, won, settlement_source, updated_at_utc)
SELECT id, gen_random_uuid(), @StrategyId, 'Matched', 'hash-' || id::text,
       'Buy', 'hash-asset', 'hash-condition', 'Yes', 0.5, 24.68, 12.34, 'FAK', @EnteredAt,
       @Cutoff + interval '5 minutes',
       CASE WHEN ordinal = 3 THEN @Cutoff ELSE @EnteredAt + ordinal * interval '1 second' END,
       'ok', 24.68, 0, 0.5, 12.34, 12.34 + CASE WHEN @Exact THEN 0.2 ELSE 0 END,
       CASE WHEN @Exact THEN 0.2 ELSE 0 END, @FeeStatus,
       CASE WHEN @Exact THEN 'Taker' ELSE 'Unknown' END,
       CASE WHEN @Exact THEN @ExactSource ELSE '' END,
       CASE WHEN @Exact THEN 0.01 END, CASE WHEN @Exact THEN 2 END,
       CASE WHEN @Exact THEN true END, CASE WHEN @Exact THEN @SettledAt END,
       '', '{}'::jsonb, 'Live hash fixture', true, 22.34, 10,
       CASE WHEN @Exact THEN 9.8 END,
       CASE WHEN ordinal = 3 THEN @Cutoff + interval '1 day'
            ELSE @SettledAt + ordinal * interval '1 second' END,
       'hash-asset', 'Yes', true, 'integration', @SettledAt
FROM unnest(@Ids::uuid[]) WITH ORDINALITY AS entries(id, ordinal);
""", connection);
        seed.Parameters.AddWithValue("StrategyId", strategyId);
        seed.Parameters.AddWithValue("Code", "historical_live_hash_" + strategyId.ToString("N"));
        seed.Parameters.Add("Ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = ids;
        seed.Parameters.AddWithValue("FeeStatus", feeStatus);
        seed.Parameters.AddWithValue("Exact", feeStatus != "LegacyUnknown");
        seed.Parameters.AddWithValue("ExactSource",
            "polymarket-clob-v2-fd-shares-rate-price-curve-round5-away-from-zero-v1");
        seed.Parameters.AddWithValue("EnteredAt", HistoricalGrossNetParityConstants.CutoffUtc.AddDays(-2).UtcDateTime);
        seed.Parameters.AddWithValue("SettledAt", HistoricalGrossNetParityConstants.CutoffUtc.AddDays(-1).UtcDateTime);
        seed.Parameters.AddWithValue("Cutoff", HistoricalGrossNetParityConstants.CutoffUtc.UtcDateTime);
        await seed.ExecuteNonQueryAsync();
        return (strategyId, ids[0], ids[1], ids[2]);
    }

    private static async Task<PostgresConnectionFactory> CreateFactoryAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "POLYCOPYTRADER_TEST_POSTGRES_CONNECTION disappeared after test discovery.");
        }

        var factory = new PostgresConnectionFactory(
            new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        return factory;
    }

    private static async Task<HistoricalGrossNetParityRankedStrategy> LoadRankedStrategyAsync(
        PostgresAppRepository repository,
        Guid strategyId)
    {
        var ranking = await repository.LoadHistoricalGrossNetParityStrategyRankingAsync(
            new HistoricalGrossNetParityStrategyRankingRequest(30, 1_000));
        return Assert.Single(ranking, value => value.StrategyId == strategyId);
    }

    private static async Task SeedCandidatePaginationAsync(
        PostgresConnectionFactory factory,
        Guid[] firstStrategyRunIds,
        Guid[] secondStrategyRunIds)
    {
        var enteredAtUtc = HistoricalGrossNetParityConstants.CutoffUtc.AddDays(-3);
        var settledAtUtc = HistoricalGrossNetParityConstants.CutoffUtc.AddDays(-2);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO strategies (
    id, code, name, description, enabled, live_stakes, created_at_utc, updated_at_utc)
VALUES
    (@FirstStrategyId, 'historical_parity_pagination_first', 'pagination first',
     'integration fixture', true, false, @EnteredAtUtc, @EnteredAtUtc),
    (@SecondStrategyId, 'historical_parity_pagination_second', 'pagination second',
     'integration fixture', true, false, @EnteredAtUtc, @EnteredAtUtc)
ON CONFLICT (id) DO NOTHING;

INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title,
    detected_at_utc, entry_due_at_utc, status, selected_asset_id, selected_outcome,
    stake_usd, realized_pnl_usd, settled_at_utc, retention_scope,
    entered_at_utc, created_at_utc, updated_at_utc)
SELECT source.id, @FirstStrategyId, 'page-first-' || source.ordinality::text,
       'page-first-condition-' || source.ordinality::text,
       'page-first-' || source.ordinality::text, 'pagination first',
       @EnteredAtUtc + source.ordinality * interval '1 second',
       @EnteredAtUtc + source.ordinality * interval '1 second',
       'Settled', 'asset', 'Yes', 1, 10, @SettledAtUtc, 'PaperOnly',
       @EnteredAtUtc + source.ordinality * interval '1 second',
       @EnteredAtUtc + source.ordinality * interval '1 second', @SettledAtUtc
FROM unnest(@FirstRunIds::uuid[]) WITH ORDINALITY source(id, ordinality)
ON CONFLICT (id) DO NOTHING;

INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title,
    detected_at_utc, entry_due_at_utc, status, selected_asset_id, selected_outcome,
    stake_usd, realized_pnl_usd, settled_at_utc, retention_scope,
    entered_at_utc, created_at_utc, updated_at_utc)
SELECT source.id, @SecondStrategyId, 'page-second-' || source.ordinality::text,
       'page-second-condition-' || source.ordinality::text,
       'page-second-' || source.ordinality::text, 'pagination second',
       @EnteredAtUtc + interval '1 day' + source.ordinality * interval '1 second',
       @EnteredAtUtc + interval '1 day' + source.ordinality * interval '1 second',
       'Settled', 'asset', 'Yes', 1, 100, @SettledAtUtc, 'PaperOnly',
       @EnteredAtUtc + interval '1 day' + source.ordinality * interval '1 second',
       @EnteredAtUtc + interval '1 day' + source.ordinality * interval '1 second', @SettledAtUtc
FROM unnest(@SecondRunIds::uuid[]) WITH ORDINALITY source(id, ordinality)
ON CONFLICT (id) DO NOTHING;

INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title,
    detected_at_utc, entry_due_at_utc, status, selected_asset_id, selected_outcome,
    stake_usd, realized_pnl_usd, settled_at_utc, retention_scope,
    entered_at_utc, created_at_utc, updated_at_utc)
VALUES
    (@AuditedRunId, @FirstStrategyId, 'page-audited', 'page-audited-condition',
     'page-audited', 'pagination audited', @EnteredAtUtc, @EnteredAtUtc,
     'Settled', 'asset', 'Yes', 1, 0, @SettledAtUtc, 'PaperOnly',
     @EnteredAtUtc, @EnteredAtUtc, @SettledAtUtc),
    (@PostCutoffRunId, @FirstStrategyId, 'page-post-cutoff', 'page-post-cutoff-condition',
     'page-post-cutoff', 'pagination post cutoff', @PostCutoffUtc, @PostCutoffUtc,
     'Settled', 'asset', 'Yes', 1, 0, @PostCutoffUtc, 'PaperOnly',
     @PostCutoffUtc, @PostCutoffUtc, @PostCutoffUtc)
ON CONFLICT (id) DO NOTHING;

INSERT INTO historical_gross_net_parity_audit (
    audit_id, source_kind, source_id, strategy_id, calculation_version,
    operation_kind, evidence_version, occurred_at_utc,
    old_payload_json, new_payload_json, evidence_payload_json)
VALUES (
    gen_random_uuid(), 'PaperRun', @AuditedRunId, @FirstStrategyId,
    @CalculationVersion, 'AccountingDecision', 'pagination-audited', @SettledAtUtc,
    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb)
ON CONFLICT DO NOTHING;

ANALYZE strategy_market_paper_runs;
ANALYZE historical_gross_net_parity_audit;
""",
            connection);
        command.Parameters.AddWithValue("FirstStrategyId", PaginationStrategyOneId);
        command.Parameters.AddWithValue("SecondStrategyId", PaginationStrategyTwoId);
        command.Parameters.AddWithValue("AuditedRunId", PaginationAuditedRunId);
        command.Parameters.AddWithValue("PostCutoffRunId", PaginationPostCutoffRunId);
        command.Parameters.Add("FirstRunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value =
            firstStrategyRunIds;
        command.Parameters.Add("SecondRunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value =
            secondStrategyRunIds;
        command.Parameters.AddWithValue("EnteredAtUtc", enteredAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("SettledAtUtc", settledAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "PostCutoffUtc",
            HistoricalGrossNetParityConstants.CutoffUtc.AddDays(1).UtcDateTime);
        command.Parameters.AddWithValue(
            "CalculationVersion",
            HistoricalGrossNetParityConstants.CalculationVersion);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedAsync(PostgresConnectionFactory factory)
    {
        var enteredAtUtc = HistoricalGrossNetParityConstants.CutoffUtc.AddDays(-2);
        var settledAtUtc = HistoricalGrossNetParityConstants.CutoffUtc.AddDays(-1);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO strategies (
    id, code, name, description, enabled, live_stakes, created_at_utc, updated_at_utc)
VALUES
    (@StrategyId, 'eth_up_down_5m_reference_average_bps_2_fak_premarket',
     'parity donor strategy', 'integration fixture', true, false, @EnteredAtUtc, @EnteredAtUtc),
    (@RunlessStrategyId, 'eth_up_down_5m_reference_average_bps_3_fak_premarket',
     'runless sell donor strategy', 'integration fixture', true, false, @EnteredAtUtc, @EnteredAtUtc),
    (@UnrelatedStrategyId, 'historical_parity_unrelated',
     'unrelated strategy', 'integration fixture', true, false, @EnteredAtUtc, @EnteredAtUtc)
ON CONFLICT (id) DO NOTHING;

INSERT INTO paper_orders (
    id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id,
    condition_id, outcome, price, size_shares, notional_usd, created_at_utc,
    expires_at_utc, filled_at_utc, raw_decision_json)
VALUES (
    @PaperOrderId, gen_random_uuid(), @StrategyId, 'strategy:eth_up_down_5m_reference_average_bps_2_fak_premarket',
    'Filled', 'Buy', 'asset-yes', 'condition', 'Yes', 0.50, 20, 10,
    @EnteredAtUtc, @EnteredAtUtc + interval '5 minutes', @EnteredAtUtc + interval '2 seconds', '{}'::jsonb),
    (@SecondPaperOrderId, gen_random_uuid(), @StrategyId,
     'strategy:eth_up_down_5m_reference_average_bps_2_fak_premarket',
     'Filled', 'Buy', 'asset-yes', 'condition', 'Yes', 0.50, 10, 5,
     @EnteredAtUtc, @EnteredAtUtc + interval '5 minutes', @EnteredAtUtc + interval '3 seconds', '{}'::jsonb),
    (@ConsumedLinkedPaperOrderId, gen_random_uuid(), @StrategyId,
     'strategy:eth_up_down_5m_reference_average_bps_2_fak_premarket',
     'Filled', 'Buy', 'asset-consumed', 'condition-consumed', 'Yes', 0.50, 10, 5,
     @EnteredAtUtc, @EnteredAtUtc + interval '5 minutes', @EnteredAtUtc + interval '10 seconds', '{}'::jsonb),
    (@ConsumedSellPaperOrderId, gen_random_uuid(), @StrategyId,
     'strategy:eth_up_down_5m_reference_average_bps_2_fak_premarket',
     'Filled', 'Sell', 'asset-consumed', 'condition-consumed', 'Yes', 0.60, 10, 6,
     @EnteredAtUtc, @EnteredAtUtc + interval '5 minutes', @EnteredAtUtc + interval '11 seconds', '{}'::jsonb),
    (@RemainingPaperOrderId, gen_random_uuid(), @StrategyId,
     'strategy:eth_up_down_5m_reference_average_bps_2_fak_premarket',
     'Filled', 'Buy', 'asset-consumed', 'condition-consumed', 'Yes', 0.50, 10, 5,
     @EnteredAtUtc, @EnteredAtUtc + interval '5 minutes', @EnteredAtUtc + interval '12 seconds', '{}'::jsonb),
    (@RunlessBuyOrderOneId, gen_random_uuid(), @RunlessStrategyId,
     'strategy:eth_up_down_5m_reference_average_bps_3_fak_premarket',
     'Filled', 'Buy', 'asset-runless', 'condition-runless', 'Yes', 0.50, 10, 5,
     @EnteredAtUtc, @EnteredAtUtc + interval '5 minutes', @EnteredAtUtc + interval '21 seconds', '{}'::jsonb),
    (@RunlessBuyOrderTwoId, gen_random_uuid(), @RunlessStrategyId,
     'strategy:eth_up_down_5m_reference_average_bps_3_fak_premarket',
     'Filled', 'Buy', 'asset-runless', 'condition-runless', 'Yes', 0.50, 10, 5,
     @EnteredAtUtc, @EnteredAtUtc + interval '5 minutes', @EnteredAtUtc + interval '22 seconds', '{}'::jsonb),
    (@RunlessSellOrderId, gen_random_uuid(), @RunlessStrategyId,
     'strategy:eth_up_down_5m_reference_average_bps_3_fak_premarket',
     'Filled', 'Sell', 'asset-runless', 'condition-runless', 'Yes', 0.60, 10, 6,
     @EnteredAtUtc, @EnteredAtUtc + interval '5 minutes', @EnteredAtUtc + interval '23 seconds', '{}'::jsonb);

INSERT INTO paper_fills (
    id, paper_order_id, price, size_shares, filled_at_utc, evidence,
    realized_pnl_usd, fee_usd, fee_accounting_status, fee_liquidity_role,
    fee_calculation_source, fee_rate, fee_exponent, fee_taker_only,
    fee_calculated_at_utc, net_realized_pnl_usd)
VALUES
    (gen_random_uuid(), @PaperOrderId, 0.50, 10, @EnteredAtUtc + interval '1 second',
     'integration exact child 1', 0, 0.10, 'Calculated', 'Taker', @ExactSource,
     0.01, 2, true, @EnteredAtUtc + interval '1 second', NULL),
    (gen_random_uuid(), @PaperOrderId, 0.50, 10, @EnteredAtUtc + interval '2 seconds',
     'integration exact child 2', 0, 0.20, 'Calculated', 'Taker', @ExactSource,
     0.01, 2, true, @EnteredAtUtc + interval '2 seconds', NULL),
    (gen_random_uuid(), @SecondPaperOrderId, 0.50, 10, @EnteredAtUtc + interval '3 seconds',
     'integration exact unlinked child', 0, 0.15, 'Calculated', 'Taker', @ExactSource,
     0.01, 2, true, @EnteredAtUtc + interval '3 seconds', NULL),
    (gen_random_uuid(), @ConsumedLinkedPaperOrderId, 0.50, 10,
     @EnteredAtUtc + interval '10 seconds', 'integration consumed linked buy',
     0, 0.10, 'Calculated', 'Taker', @ExactSource,
     0.01, 2, true, @EnteredAtUtc + interval '10 seconds', NULL),
    (gen_random_uuid(), @ConsumedSellPaperOrderId, 0.60, 10,
     @EnteredAtUtc + interval '11 seconds', 'integration full close',
     1, 0.05, 'Calculated', 'Taker', @ExactSource,
     0.01, 2, true, @EnteredAtUtc + interval '11 seconds', 0.85),
    (gen_random_uuid(), @RemainingPaperOrderId, 0.50, 10,
     @EnteredAtUtc + interval '12 seconds', 'integration unrelated remainder',
     0, 0.15, 'Calculated', 'Taker', @ExactSource,
     0.01, 2, true, @EnteredAtUtc + interval '12 seconds', NULL),
    (@RunlessBuyFillOneId, @RunlessBuyOrderOneId, 0.50, 10,
     @EnteredAtUtc + interval '21 seconds', 'integration runless entry one',
     0, 0.10, 'Calculated', 'Taker', @ExactSource,
     0.01, 2, true, @EnteredAtUtc + interval '21 seconds', NULL),
    (@RunlessBuyFillTwoId, @RunlessBuyOrderTwoId, 0.50, 10,
     @EnteredAtUtc + interval '22 seconds', 'integration runless entry two',
     0, 0.20, 'Calculated', 'Taker', @ExactSource,
     0.01, 2, true, @EnteredAtUtc + interval '22 seconds', NULL),
    (@RunlessSellFillId, @RunlessSellOrderId, 0.60, 10,
     @EnteredAtUtc + interval '23 seconds', 'integration runless partial sell',
     1.00, 0.05, 'Calculated', 'Taker', @ExactSource,
     0.01, 2, true, @EnteredAtUtc + interval '23 seconds', 0.80);

INSERT INTO paper_positions (
    id, copied_trader_wallet, asset_id, condition_id, outcome, size_shares,
    average_price, estimated_value_usd, unrealized_pnl_usd, fee_usd,
    fee_accounting_status, fee_liquidity_role, fee_calculation_source,
    fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc,
    net_unrealized_pnl_usd, updated_at_utc)
VALUES (
    @PaperPositionId, 'strategy:eth_up_down_5m_reference_average_bps_2_fak_premarket',
    'asset-yes', 'condition', 'Yes', 30, 0.50, 15.30, 0.30, 0.45,
    'Calculated', 'Taker', @ExactSource, 0.01, 2, true,
    @SettledAtUtc, -0.15, @SettledAtUtc),
    (@ConsumedPoolPositionId,
     'strategy:eth_up_down_5m_reference_average_bps_2_fak_premarket',
     'asset-consumed', 'condition-consumed', 'Yes', 10, 0.50, 5.20, 0.20, 0.15,
     'Calculated', 'Taker', @ExactSource, 0.01, 2, true,
     @SettledAtUtc, 0.05, @SettledAtUtc);

INSERT INTO paper_position_settlements (
    id, copied_trader_wallet, asset_id, condition_id, outcome,
    winning_asset_id, winning_outcome, settled_size_shares, average_price,
    cost_basis_usd, settlement_value_usd, realized_pnl_usd, fee_usd,
    fee_accounting_status, fee_liquidity_role, fee_calculation_source,
    fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc,
    net_realized_pnl_usd, won, settlement_source, settled_at_utc, created_at_utc)
SELECT gen_random_uuid(), 'strategy:historical_parity-unrelated-' || value::text,
       'unrelated-settlement-asset-' || value::text,
       'unrelated-settlement-condition-' || value::text,
       'Yes', 'unrelated-settlement-asset-' || value::text, 'Yes',
       1, 0.50, 0.50, 0.60, 0.10, 0.01,
       'Calculated', 'Taker', @ExactSource, 0.01, 2, true,
       @SettledAtUtc, 0.09, true, 'integration', @SettledAtUtc, @SettledAtUtc
FROM generate_series(1, 300) value
ON CONFLICT (copied_trader_wallet, asset_id) DO NOTHING;

INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title,
    detected_at_utc, entry_due_at_utc, status, selected_asset_id, selected_outcome,
    entry_price, stake_usd, size_shares, paper_order_id, entered_at_utc,
    settlement_price, settlement_value_usd, realized_pnl_usd, fee_usd,
    fee_accounting_status, fee_liquidity_role, fee_calculation_source,
    fee_calculated_at_utc, net_realized_pnl_usd, settled_at_utc,
    retention_scope, created_at_utc, updated_at_utc)
VALUES
    (@DonorRunId, @StrategyId, 'donor-market', 'condition', 'donor-market', 'donor',
     @EnteredAtUtc, @EnteredAtUtc, 'Settled', 'asset-yes', 'Yes',
     0.50, 10, 20, @PaperOrderId, @EnteredAtUtc,
     0.60, 12, 2, 0.30, 'Calculated', 'Unknown', 'mixed',
     @SettledAtUtc, 1.70, @SettledAtUtc, 'PaperOnly', @EnteredAtUtc, @SettledAtUtc),
    (@TargetRunId, @StrategyId, 'target-market', 'condition-2', 'target-market', 'target',
     @EnteredAtUtc, @EnteredAtUtc, 'Settled', 'asset-no', 'No',
     0.50, 10, 20, NULL, @EnteredAtUtc,
     0.55, 11, 1, 0, 'LegacyUnknown', 'Unknown', '',
     NULL, NULL, @SettledAtUtc, 'PaperOnly', @EnteredAtUtc, @SettledAtUtc);

INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title,
    detected_at_utc, entry_due_at_utc, status, selected_asset_id, selected_outcome,
    stake_usd, realized_pnl_usd, settled_at_utc, retention_scope,
    created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), @UnrelatedStrategyId, 'unrelated-' || value::text,
       'unrelated-condition-' || value::text, 'unrelated-' || value::text, 'unrelated',
       @EnteredAtUtc, @EnteredAtUtc, 'Settled', 'asset', 'Yes', 1, 0,
       @SettledAtUtc, 'PaperOnly', @EnteredAtUtc, @SettledAtUtc
FROM generate_series(1, 5000) value;

INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title,
    detected_at_utc, entry_due_at_utc, status, selected_asset_id, selected_outcome,
    stake_usd, realized_pnl_usd, fee_usd, fee_accounting_status,
    fee_liquidity_role, fee_calculation_source, fee_rate, fee_exponent,
    fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd, settled_at_utc,
    retention_scope, entered_at_utc, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), @StrategyId, 'paged-donor-' || value::text,
       'paged-condition-' || value::text, 'paged-donor-' || value::text, 'paged donor',
       @PostCutoffUtc, @PostCutoffUtc, 'Settled', 'asset-paged', 'Yes',
       1, 0.20, 0.01, 'Calculated', 'Taker', @ExactSource,
       0.01, 2, true, @PostCutoffUtc, 0.19, @PostCutoffUtc,
       'PaperOnly', @PostCutoffUtc, @PostCutoffUtc, @PostCutoffUtc
FROM generate_series(1, 300) value;

INSERT INTO live_orders (
    id, signal_id, strategy_id, status, order_id, side, asset_id, condition_id,
    outcome, price, size_shares, notional_usd, order_type, created_at_utc,
    expires_at_utc, submitted_at_utc, response_status, filled_size, remaining_size,
    average_fill_price, filled_notional_usd, cost_basis_usd, fee_usd,
    fee_accounting_status, fee_liquidity_role, fee_calculation_source,
    fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc,
    cancel_status, raw_response_json, validation_summary, balance_effect_applied,
    settlement_value_usd, realized_pnl_usd, net_realized_pnl_usd,
    settled_at_utc, winning_asset_id, winning_outcome, won, settlement_source,
    paper_order_id, updated_at_utc)
VALUES
    (@LiveDonorOrderId, gen_random_uuid(), @StrategyId, 'Matched', 'integration-live-donor',
     'Buy', 'asset-yes', 'condition', 'Yes', 0.50, 20, 10, 'FAK', @EnteredAtUtc,
     @EnteredAtUtc + interval '5 minutes', @EnteredAtUtc + interval '1 second', 'ok',
     20, 0, 0.50, 10, 10.35, 0.35, 'Calculated', 'Taker', @ExactSource,
     0.01, 2, true, @SettledAtUtc, '', '{}'::jsonb, 'integration donor', false,
     12, 2, 1.65, @SettledAtUtc, 'asset-yes', 'Yes', true, 'integration',
     @PaperOrderId, @SettledAtUtc),
    (@LiveTargetOrderId, gen_random_uuid(), @StrategyId, 'Matched', 'integration-live-target',
     'Buy', 'asset-target', 'condition-target', 'Yes', 0.50, 20, 10, 'FAK', @EnteredAtUtc,
     @EnteredAtUtc + interval '5 minutes', @EnteredAtUtc + interval '4 seconds', 'ok',
     20, 0, 0.50, 10, 10, 0, 'LegacyUnknown', 'Unknown', '',
     NULL, NULL, NULL, NULL, '', '{}'::jsonb, 'integration target', false,
     11, 1, NULL, @SettledAtUtc, 'asset-target', 'Yes', true, 'integration',
     NULL, @SettledAtUtc),
    (@ConsumedLiveOrderId, gen_random_uuid(), @StrategyId, 'Matched',
     'integration-consumed-live-donor', 'Buy', 'asset-consumed', 'condition-consumed',
     'Yes', 0.50, 10, 5, 'FAK', @EnteredAtUtc,
     @EnteredAtUtc + interval '5 minutes', @EnteredAtUtc + interval '10 seconds', 'ok',
     10, 0, 0.50, 5, 5.10, 0.10, 'Calculated', 'Taker', @ExactSource,
     0.01, 2, true, @SettledAtUtc, '', '{}'::jsonb, 'consumed linked donor', false,
     6, 1, 0.90, @SettledAtUtc, 'asset-consumed', 'Yes', true, 'integration',
     @ConsumedLinkedPaperOrderId, @SettledAtUtc),
    (@ZeroBasisLiveOrderId, gen_random_uuid(), @StrategyId, 'Matched',
     'integration-zero-basis-live', 'Buy', 'asset-consumed', 'condition-consumed',
     'Yes', 0.50, 10, 5, 'FAK', @EnteredAtUtc,
     @EnteredAtUtc + interval '5 minutes', @EnteredAtUtc + interval '12 seconds', 'ok',
     0, 0, NULL, 0, 0, 0, 'Calculated', 'Taker', @ExactSource,
     0.01, 2, true, @SettledAtUtc, '', '{}'::jsonb, 'zero basis must not suppress', false,
     0.50, 0.50, 0.50, @SettledAtUtc, 'asset-consumed', 'Yes', true, 'integration',
     @RemainingPaperOrderId, @SettledAtUtc);

INSERT INTO strategy_paper_skip_rollups (
    strategy_id, bucket_start_utc, skip_reason, run_count,
    first_updated_at_utc, last_updated_at_utc, created_at_utc, updated_at_utc)
SELECT @UnrelatedStrategyId,
       (DATE '2025-01-01' + value)::timestamp AT TIME ZONE 'UTC',
       'integration-' || value::text, 1,
       @EnteredAtUtc, @EnteredAtUtc, @EnteredAtUtc, @EnteredAtUtc
FROM generate_series(1, 300) value;

INSERT INTO historical_gross_net_parity_audit (
    audit_id, source_kind, source_id, strategy_id, calculation_version,
    operation_kind, evidence_version, occurred_at_utc,
    old_payload_json, new_payload_json, evidence_payload_json)
SELECT gen_random_uuid(), 'LiveOrder', gen_random_uuid(), @UnrelatedStrategyId,
       @CalculationVersion, 'AccountingDecision', 'integration-' || value::text,
       @SettledAtUtc, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb
FROM generate_series(1, 300) value;

ANALYZE strategy_market_paper_runs;
ANALYZE strategy_paper_skip_rollups;
ANALYZE historical_gross_net_parity_audit;
ANALYZE paper_position_settlements;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", StrategyId);
        command.Parameters.AddWithValue("UnrelatedStrategyId", UnrelatedStrategyId);
        command.Parameters.AddWithValue("RunlessStrategyId", RunlessStrategyId);
        command.Parameters.AddWithValue("PaperOrderId", PaperOrderId);
        command.Parameters.AddWithValue("SecondPaperOrderId", SecondPaperOrderId);
        command.Parameters.AddWithValue("DonorRunId", DonorRunId);
        command.Parameters.AddWithValue("TargetRunId", TargetRunId);
        command.Parameters.AddWithValue("PaperPositionId", PaperPositionId);
        command.Parameters.AddWithValue("LiveDonorOrderId", LiveDonorOrderId);
        command.Parameters.AddWithValue("LiveTargetOrderId", LiveTargetOrderId);
        command.Parameters.AddWithValue("ConsumedLinkedPaperOrderId", ConsumedLinkedPaperOrderId);
        command.Parameters.AddWithValue("ConsumedSellPaperOrderId", ConsumedSellPaperOrderId);
        command.Parameters.AddWithValue("RemainingPaperOrderId", RemainingPaperOrderId);
        command.Parameters.AddWithValue("ConsumedPoolPositionId", ConsumedPoolPositionId);
        command.Parameters.AddWithValue("ConsumedLiveOrderId", ConsumedLiveOrderId);
        command.Parameters.AddWithValue("ZeroBasisLiveOrderId", ZeroBasisLiveOrderId);
        command.Parameters.AddWithValue("RunlessBuyOrderOneId", RunlessBuyOrderOneId);
        command.Parameters.AddWithValue("RunlessBuyOrderTwoId", RunlessBuyOrderTwoId);
        command.Parameters.AddWithValue("RunlessSellOrderId", RunlessSellOrderId);
        command.Parameters.AddWithValue("RunlessBuyFillOneId", RunlessBuyFillOneId);
        command.Parameters.AddWithValue("RunlessBuyFillTwoId", RunlessBuyFillTwoId);
        command.Parameters.AddWithValue("RunlessSellFillId", RunlessSellFillId);
        command.Parameters.AddWithValue("EnteredAtUtc", enteredAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("SettledAtUtc", settledAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "PostCutoffUtc",
            HistoricalGrossNetParityConstants.CutoffUtc.AddDays(1).UtcDateTime);
        command.Parameters.AddWithValue(
            "ExactSource",
            "polymarket-clob-v2-fd-shares-rate-price-curve-round5-away-from-zero-v1");
        command.Parameters.AddWithValue(
            "CalculationVersion",
            HistoricalGrossNetParityConstants.CalculationVersion);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountPaperPositionSettlementsAsync(
        PostgresConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM paper_position_settlements;",
            connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadCanonicalSettlementPlanAsync(
        PostgresConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
EXPLAIN (FORMAT JSON, COSTS true)
WITH candidate_strategy AS MATERIALIZED (
    SELECT code FROM strategies WHERE id=@StrategyId
)
SELECT id
FROM paper_position_settlements
WHERE copied_trader_wallet=(SELECT 'strategy:'||code FROM candidate_strategy);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", RunlessStrategyId);
        var json = (string?)await command.ExecuteScalarAsync();
        Assert.False(string.IsNullOrWhiteSpace(json));
        using var document = JsonDocument.Parse(json!);
        return document.RootElement.GetRawText();
    }

    private static async Task<string> ReadSettledRunPlanAsync(
        PostgresConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
EXPLAIN (FORMAT JSON, COSTS true)
SELECT id
FROM strategy_market_paper_runs
WHERE strategy_id = @StrategyId
  AND settled_at_utc IS NOT NULL;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", StrategyId);
        var json = (string?)await command.ExecuteScalarAsync();
        Assert.False(string.IsNullOrWhiteSpace(json));
        using var document = JsonDocument.Parse(json!);
        return document.RootElement.GetRawText();
    }
}
