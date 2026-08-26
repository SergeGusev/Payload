using System.Numerics;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
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
    public async Task CandidatePage_KeysetContinuesWithinStrategyThenTransitionsWithoutGaps()
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

        HistoricalGrossNetParityCandidateCursor? cursor = null;
        var pages = new List<HistoricalGrossNetParityCandidatePage>();
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
                    HistoricalGrossNetParityConstants.CalculationVersion));
            Assert.Equal(HistoricalGrossNetParityReadStatus.Complete, page.Status);
            Assert.InRange(page.Candidates.Count, 1, 50);
            Assert.NotNull(page.NextCursor);
            pages.Add(page);
            cursor = page.NextCursor;
        }

        Assert.All(
            pages[0].Candidates.Concat(pages[1].Candidates),
            candidate => Assert.Equal(PaginationStrategyOneId, candidate.StrategyId));

        var relevant = pages
            .SelectMany(page => page.Candidates)
            .Where(candidate =>
                candidate.StrategyId == PaginationStrategyOneId ||
                candidate.StrategyId == PaginationStrategyTwoId)
            .ToArray();
        Assert.Equal(123, relevant.Length);
        Assert.Equal(123, relevant.Select(candidate => candidate.SourceId).Distinct().Count());
        Assert.Equal(
            firstStrategyRunIds,
            relevant.Take(firstStrategyRunIds.Length).Select(candidate => candidate.SourceId));
        Assert.Equal(
            secondStrategyRunIds,
            relevant.Skip(firstStrategyRunIds.Length).Select(candidate => candidate.SourceId));
        Assert.DoesNotContain(relevant, candidate => candidate.SourceId == PaginationAuditedRunId);
        Assert.DoesNotContain(relevant, candidate => candidate.SourceId == PaginationPostCutoffRunId);
        Assert.Equal(PaginationStrategyOneId, pages[2].Candidates[0].StrategyId);
        Assert.Equal(PaginationStrategyTwoId, pages[2].Candidates[20].StrategyId);
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
                    PaginationStrategyOneId));
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
                HistoricalGrossNetParityConstants.CalculationVersion));
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

        var runlessTarget = Assert.Single(
            prepared.Targets,
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
ON CONFLICT (source_kind, source_id, calculation_version, operation_kind) DO NOTHING;

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
