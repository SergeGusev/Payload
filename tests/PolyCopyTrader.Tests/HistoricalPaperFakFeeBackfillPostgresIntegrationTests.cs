using Npgsql;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class HistoricalPaperFakFeeBackfillPostgresIntegrationTests
{
    private const string HistoricalPrefix = "historical-current-paper-model-v1:";

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task CandidateAndApply_EnforceScopeAtomicityIdempotencyAndProjectionGuards()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var scenario = BackfillScenario.Create();

        try
        {
            await SeedScenarioAsync(factory, repository, scenario);

            var strategyRanks = await repository.GetHistoricalPaperFakFeeBackfillStrategyRanksAsync(
                scenario.CutoffUtc);
            var scenarioRank = Assert.Single(
                strategyRanks,
                rank => rank.StrategyId == scenario.StrategyId);
            Assert.Equal(2m, scenarioRank.GrossRealizedPnlUsd);
            AssertGrossRanksDescending(strategyRanks);

            var firstPage = await repository.GetHistoricalPaperFakFeeBackfillCandidatesAsync(
                scenario.CutoffUtc,
                scenario.StrategyId,
                1);
            Assert.False(firstPage.ReachedEnd);
            Assert.Equal(scenario.TargetOrderId, Assert.Single(firstPage.Candidates).Order.Id);
            Assert.Equal(scenario.StrategyId, firstPage.ContinuationCursor?.StrategyId);

            var secondPage = await repository.GetHistoricalPaperFakFeeBackfillCandidatesAsync(
                scenario.CutoffUtc,
                scenario.StrategyId,
                1,
                firstPage.ContinuationCursor);
            Assert.True(secondPage.ReachedEnd);
            Assert.Equal(scenario.ConflictOrderId, Assert.Single(secondPage.Candidates).Order.Id);

            var page = await repository.GetHistoricalPaperFakFeeBackfillCandidatesAsync(
                scenario.CutoffUtc,
                scenario.StrategyId,
                20);

            Assert.True(page.ReachedEnd);
            Assert.Equal(
                new[] { scenario.TargetOrderId, scenario.ConflictOrderId }.OrderBy(id => id),
                page.Candidates.Select(candidate => candidate.Order.Id).OrderBy(id => id));
            Assert.DoesNotContain(page.Candidates, candidate =>
                candidate.Order.Id is var id &&
                (id == scenario.ShadowOrderId ||
                 id == scenario.GtdOrderId ||
                 id == scenario.CurrentOrderId ||
                 id == scenario.AfterCutoffOrderId));

            var target = Assert.Single(
                page.Candidates,
                candidate => candidate.Order.Id == scenario.TargetOrderId);
            var conflict = Assert.Single(
                page.Candidates,
                candidate => candidate.Order.Id == scenario.ConflictOrderId);
            Assert.Equal(9m, target.Order.NotionalUsd);
            Assert.Equal("btc_updown5m_fak_taker_paper", target.Order.ExecutionSource);
            Assert.Equal("btc_updown5m_child_mirror_fak_paper", conflict.Order.ExecutionSource);

            var targetGrossBefore = await ReadGrossSnapshotAsync(factory, scenario.TargetOrderId);
            var conflictGrossBefore = await ReadGrossSnapshotAsync(factory, scenario.ConflictOrderId);
            var conflictAccountingBefore = await ReadAccountingSnapshotAsync(
                factory,
                scenario.ConflictOrderId);
            var projectionBefore = await ReadProjectionSnapshotAsync(factory, scenario);
            var targetUpdate = CreateUpdate(target, scenario.FeeCalculatedAtUtc);

            var applied = await repository.ApplyHistoricalPaperFakFeeBackfillBatchAsync([targetUpdate]);

            Assert.Equal(
                new HistoricalPaperFakFeeBackfillBatchResult(
                    Requested: 1,
                    FullChainEligible: 1,
                    FillsUpdated: 1,
                    RunsUpdated: 1,
                    PositionsUpdated: 1,
                    SettlementsUpdated: 1),
                applied);
            Assert.Equal(
                targetGrossBefore,
                await ReadGrossSnapshotAsync(factory, scenario.TargetOrderId));
            AssertCalculatedAccounting(
                await ReadAccountingSnapshotAsync(factory, scenario.TargetOrderId),
                scenario.FeeCalculatedAtUtc);
            Assert.Equal(projectionBefore, await ReadProjectionSnapshotAsync(factory, scenario));

            var retry = await repository.ApplyHistoricalPaperFakFeeBackfillBatchAsync([targetUpdate]);

            Assert.Equal(
                new HistoricalPaperFakFeeBackfillBatchResult(
                    Requested: 1,
                    FullChainEligible: 1,
                    FullChainAlreadyApplied: 1),
                retry);
            Assert.Equal(
                targetGrossBefore,
                await ReadGrossSnapshotAsync(factory, scenario.TargetOrderId));
            Assert.Equal(projectionBefore, await ReadProjectionSnapshotAsync(factory, scenario));

            var conflicted = await repository.ApplyHistoricalPaperFakFeeBackfillBatchAsync(
                [CreateUpdate(conflict, scenario.FeeCalculatedAtUtc)]);

            Assert.Equal(
                new HistoricalPaperFakFeeBackfillBatchResult(
                    Requested: 1,
                    StructuralConflicts: 1),
                conflicted);
            Assert.Equal(
                conflictGrossBefore,
                await ReadGrossSnapshotAsync(factory, scenario.ConflictOrderId));
            Assert.Equal(
                conflictAccountingBefore,
                await ReadAccountingSnapshotAsync(factory, scenario.ConflictOrderId));
            Assert.Equal(projectionBefore, await ReadProjectionSnapshotAsync(factory, scenario));
        }
        finally
        {
            await CleanupScenarioAsync(factory, scenario);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task CandidateQuery_PreservesEveryParityDecisionBindingShape()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var scenario = BackfillScenario.Create();
        var orders = Enumerable.Range(0, 6)
            .Select(index => CreateOrder(
                scenario,
                Guid.NewGuid(),
                $"parity-asset-{index}-{scenario.Suffix}",
                $"parity-condition-{index}-{scenario.Suffix}",
                scenario.BaseUtc.AddSeconds(index),
                "btc_updown5m_fak_taker_paper"))
            .ToArray();
        var fills = orders
            .Select(order => CreateLegacyFill(Guid.NewGuid(), order))
            .ToArray();
        var run = CreateSettledRun(Guid.NewGuid(), orders[1]);

        try
        {
            await SeedStrategyAsync(factory, scenario);
            foreach (var order in orders)
            {
                await repository.AddPaperOrderAsync(order);
            }

            foreach (var fill in fills)
            {
                await repository.AddPaperFillAsync(fill);
            }

            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await InsertParityDecisionAuditAsync(
                factory,
                scenario.StrategyId,
                "PaperSellFill",
                fills[0].Id,
                "{}",
                "{}");
            await InsertParityDecisionAuditAsync(
                factory,
                scenario.StrategyId,
                "PaperRun",
                run.Id,
                "{}",
                "{}");
            await InsertParityDecisionAuditAsync(
                factory,
                scenario.StrategyId,
                "PaperRun",
                Guid.NewGuid(),
                JsonSerializer.Serialize(new
                {
                    paper_order_id = orders[2].Id.ToString("D").ToLowerInvariant()
                }),
                "{}");
            await InsertParityDecisionAuditAsync(
                factory,
                scenario.StrategyId,
                "PaperPosition",
                Guid.NewGuid(),
                "{}",
                CreateParityBindingEvidence(fills[3].Id));
            await InsertParityDecisionAuditAsync(
                factory,
                scenario.StrategyId,
                "PaperSettlement",
                Guid.NewGuid(),
                "{}",
                CreateParityBindingEvidence(fills[4].Id));

            var page = await repository.GetHistoricalPaperFakFeeBackfillCandidatesAsync(
                scenario.CutoffUtc,
                scenario.StrategyId,
                20);

            Assert.True(page.ReachedEnd);
            var candidate = Assert.Single(page.Candidates);
            Assert.Equal(orders[5].Id, candidate.Order.Id);
            Assert.Equal(fills[5].Id, candidate.Fill.Id);
        }
        finally
        {
            await CleanupScenarioAsync(factory, scenario);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task CandidatePaging_PreservesFullTupleOrderAndCursorProgression()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var scenario = BackfillScenario.Create();
        Guid OrderedId(uint prefix) =>
            Guid.Parse($"{prefix:x8}-0000-4000-8000-{scenario.Suffix[..12]}");

        var sharedFilledAtUtc = scenario.BaseUtc.AddMinutes(10);
        var earlierOrder = CreateOrder(
            scenario,
            OrderedId(0x90000000),
            $"keyset-earlier-{scenario.Suffix}",
            $"keyset-earlier-condition-{scenario.Suffix}",
            sharedFilledAtUtc.AddSeconds(-1),
            "btc_updown5m_fak_taker_paper");
        var firstSharedOrder = CreateOrder(
            scenario,
            OrderedId(0x10000000),
            $"keyset-first-shared-{scenario.Suffix}",
            $"keyset-first-shared-condition-{scenario.Suffix}",
            sharedFilledAtUtc,
            "btc_updown5m_fak_taker_paper");
        var excludedSourceOrder = CreateOrder(
            scenario,
            OrderedId(0x18000000),
            $"keyset-excluded-source-{scenario.Suffix}",
            $"keyset-excluded-source-condition-{scenario.Suffix}",
            sharedFilledAtUtc,
            "paper_live_shadow_actual_fill");
        var secondSharedOrder = CreateOrder(
            scenario,
            OrderedId(0x20000000),
            $"keyset-second-shared-{scenario.Suffix}",
            $"keyset-second-shared-condition-{scenario.Suffix}",
            sharedFilledAtUtc,
            "btc_updown5m_child_mirror_fak_paper");
        var laterOrder = CreateOrder(
            scenario,
            OrderedId(0x05000000),
            $"keyset-later-{scenario.Suffix}",
            $"keyset-later-condition-{scenario.Suffix}",
            sharedFilledAtUtc.AddSeconds(1),
            "btc_updown5m_fak_taker_paper");
        var cutoffEqualityOrder = CreateOrder(
            scenario,
            OrderedId(0x30000000),
            $"keyset-cutoff-{scenario.Suffix}",
            $"keyset-cutoff-condition-{scenario.Suffix}",
            scenario.CutoffUtc,
            "btc_updown5m_fak_taker_paper");

        var earlierFill = CreateLegacyFill(OrderedId(0x90000000), earlierOrder);
        var firstSharedFill = CreateLegacyFill(OrderedId(0x10000000), firstSharedOrder);
        var excludedStatusFill = CreateLegacyFill(OrderedId(0x18000000), firstSharedOrder) with
        {
            FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString()
        };
        var secondSharedFill = CreateLegacyFill(OrderedId(0x20000000), firstSharedOrder);
        var otherOrderSharedFill = CreateLegacyFill(OrderedId(0x01000000), secondSharedOrder);
        var laterFill = CreateLegacyFill(OrderedId(0x01000001), laterOrder);
        var excludedSourceFill = CreateLegacyFill(OrderedId(0x01000002), excludedSourceOrder);
        var cutoffEqualityFill = CreateLegacyFill(OrderedId(0x01000003), cutoffEqualityOrder);
        var expected = new (Guid OrderId, Guid FillId, DateTimeOffset FilledAtUtc)[]
        {
            (earlierOrder.Id, earlierFill.Id, earlierFill.FilledAtUtc),
            (firstSharedOrder.Id, firstSharedFill.Id, firstSharedFill.FilledAtUtc),
            (firstSharedOrder.Id, secondSharedFill.Id, secondSharedFill.FilledAtUtc),
            (secondSharedOrder.Id, otherOrderSharedFill.Id, otherOrderSharedFill.FilledAtUtc),
            (laterOrder.Id, laterFill.Id, laterFill.FilledAtUtc)
        };

        try
        {
            await SeedStrategyAsync(factory, scenario);
            foreach (var order in new[]
                     {
                         earlierOrder,
                         firstSharedOrder,
                         excludedSourceOrder,
                         secondSharedOrder,
                         laterOrder,
                         cutoffEqualityOrder
                     })
            {
                await repository.AddPaperOrderAsync(order);
            }

            foreach (var fill in new[]
                     {
                         earlierFill,
                         firstSharedFill,
                         excludedStatusFill,
                         secondSharedFill,
                         otherOrderSharedFill,
                         laterFill,
                         excludedSourceFill,
                         cutoffEqualityFill
                     })
            {
                await repository.AddPaperFillAsync(fill);
            }

            HistoricalPaperFakFeeBackfillCursor? cursor = null;
            var actual = new List<(Guid OrderId, Guid FillId, DateTimeOffset FilledAtUtc)>();
            for (var index = 0; index < expected.Length; index++)
            {
                var page = await repository.GetHistoricalPaperFakFeeBackfillCandidatesAsync(
                    scenario.CutoffUtc,
                    scenario.StrategyId,
                    1,
                    cursor);
                var candidate = Assert.Single(page.Candidates);
                var expectedTuple = expected[index];

                Assert.Equal(expectedTuple.OrderId, candidate.Order.Id);
                Assert.Equal(expectedTuple.FillId, candidate.Fill.Id);
                Assert.Equal(expectedTuple.FilledAtUtc, candidate.Fill.FilledAtUtc);
                Assert.Equal(index == expected.Length - 1, page.ReachedEnd);
                var nextCursor = Assert.IsType<HistoricalPaperFakFeeBackfillCursor>(
                    page.ContinuationCursor);
                Assert.Equal(scenario.StrategyId, nextCursor.StrategyId);
                Assert.Equal(candidate.Fill.FilledAtUtc, nextCursor.FilledAtUtc);
                Assert.Equal(candidate.Fill.PaperOrderId, nextCursor.PaperOrderId);
                Assert.Equal(candidate.Fill.Id, nextCursor.FillId);

                actual.Add((candidate.Order.Id, candidate.Fill.Id, candidate.Fill.FilledAtUtc));
                cursor = nextCursor;
            }

            Assert.Equal(expected, actual);
            Assert.Equal(expected.Length, actual.Distinct().Count());
            Assert.DoesNotContain(actual, item => item.FillId == excludedStatusFill.Id);
            Assert.DoesNotContain(actual, item => item.FillId == excludedSourceFill.Id);
            Assert.DoesNotContain(actual, item => item.FillId == cutoffEqualityFill.Id);

            var exhausted = await repository.GetHistoricalPaperFakFeeBackfillCandidatesAsync(
                scenario.CutoffUtc,
                scenario.StrategyId,
                1,
                cursor);
            Assert.True(exhausted.ReachedEnd);
            Assert.Empty(exhausted.Candidates);
            Assert.Null(exhausted.ContinuationCursor);
        }
        finally
        {
            await CleanupScenarioAsync(factory, scenario);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Apply_AcceptsExactWebSocketAndRunOnlyShapes_AndRejectsUnsafeLegacyShapes()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var scenario = BackfillScenario.Create();
        var fullWebSocketOrder = CreateOrder(
            scenario,
            Guid.NewGuid(),
            $"websocket-full-{scenario.Suffix}",
            $"websocket-full-condition-{scenario.Suffix}",
            scenario.BaseUtc,
            "btc_updown5m_fak_taker_paper");
        var runOnlyOrder = CreateOrder(
            scenario,
            Guid.NewGuid(),
            $"run-only-{scenario.Suffix}",
            $"run-only-condition-{scenario.Suffix}",
            scenario.BaseUtc.AddSeconds(1),
            "btc_updown5m_child_mirror_fak_paper");
        var webSocketAfterRunOrder = CreateOrder(
            scenario,
            Guid.NewGuid(),
            $"websocket-after-{scenario.Suffix}",
            $"websocket-after-condition-{scenario.Suffix}",
            scenario.BaseUtc.AddSeconds(2),
            "btc_updown5m_fak_taker_paper");
        var unknownSettlementSourceOrder = CreateOrder(
            scenario,
            Guid.NewGuid(),
            $"unknown-source-{scenario.Suffix}",
            $"unknown-source-condition-{scenario.Suffix}",
            scenario.BaseUtc.AddSeconds(3),
            "btc_updown5m_fak_taker_paper");
        var partialChainOrder = CreateOrder(
            scenario,
            Guid.NewGuid(),
            $"partial-chain-{scenario.Suffix}",
            $"partial-chain-condition-{scenario.Suffix}",
            scenario.BaseUtc.AddSeconds(4),
            "btc_updown5m_fak_taker_paper");
        var orders = new[]
        {
            fullWebSocketOrder,
            runOnlyOrder,
            webSocketAfterRunOrder,
            unknownSettlementSourceOrder,
            partialChainOrder
        };

        try
        {
            await SeedStrategyAsync(factory, scenario);
            foreach (var order in orders)
            {
                await repository.AddPaperOrderAsync(order);
                await repository.AddPaperFillAsync(CreateLegacyFill(Guid.NewGuid(), order));
                Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(
                    CreateSettledRun(Guid.NewGuid(), order)));
            }

            foreach (var order in new[]
                     {
                         fullWebSocketOrder,
                         webSocketAfterRunOrder,
                         unknownSettlementSourceOrder,
                         partialChainOrder
                     })
            {
                await repository.UpsertPaperPositionAsync(CreateZeroPosition(order));
            }

            Assert.True(await repository.TryAddPaperPositionSettlementAsync(
                CreateSettlement(Guid.NewGuid(), fullWebSocketOrder, costBasisUsd: 1m) with
                {
                    SettlementSource = "MarketWebSocket",
                    SettledAtUtc = fullWebSocketOrder.FilledAtUtc!.Value.AddMinutes(4),
                    CreatedAtUtc = fullWebSocketOrder.FilledAtUtc!.Value.AddMinutes(4).AddSeconds(1)
                }));
            Assert.True(await repository.TryAddPaperPositionSettlementAsync(
                CreateSettlement(Guid.NewGuid(), webSocketAfterRunOrder, costBasisUsd: 1m) with
                {
                    SettlementSource = "MarketWebSocket",
                    SettledAtUtc = webSocketAfterRunOrder.FilledAtUtc!.Value.AddMinutes(6),
                    CreatedAtUtc = webSocketAfterRunOrder.FilledAtUtc!.Value.AddMinutes(6).AddSeconds(1)
                }));
            Assert.True(await repository.TryAddPaperPositionSettlementAsync(
                CreateSettlement(Guid.NewGuid(), unknownSettlementSourceOrder, costBasisUsd: 1m) with
                {
                    SettlementSource = "market_websocket"
                }));

            var page = await repository.GetHistoricalPaperFakFeeBackfillCandidatesAsync(
                scenario.CutoffUtc,
                scenario.StrategyId,
                20);
            Assert.True(page.ReachedEnd);
            Assert.Equal(
                orders.Select(order => order.Id).OrderBy(id => id),
                page.Candidates.Select(candidate => candidate.Order.Id).OrderBy(id => id));

            var candidates = page.Candidates.ToDictionary(candidate => candidate.Order.Id);
            var updates = orders
                .Select(order => CreateUpdate(
                    candidates[order.Id],
                    scenario.FeeCalculatedAtUtc))
                .ToArray();
            Assert.Null(await ReadDashboardReconciliationAsync(factory, scenario.StrategyId));
            var fullGrossBefore = await ReadGrossSnapshotAsync(factory, fullWebSocketOrder.Id);
            var runOnlyGrossBefore = await ReadFillRunGrossSnapshotAsync(factory, runOnlyOrder.Id);
            var unsafeOrders = new[]
            {
                webSocketAfterRunOrder,
                unknownSettlementSourceOrder,
                partialChainOrder
            };
            var unsafeAccountingBefore = new Dictionary<Guid, FillRunAccountingSnapshot>();
            foreach (var order in unsafeOrders)
            {
                unsafeAccountingBefore.Add(
                    order.Id,
                    await ReadFillRunAccountingSnapshotAsync(factory, order.Id));
            }

            var applied = await repository.ApplyHistoricalPaperFakFeeBackfillBatchAsync(updates);

            Assert.Equal(
                new HistoricalPaperFakFeeBackfillBatchResult(
                    Requested: 5,
                    StructuralConflicts: 3,
                    FullChainEligible: 1,
                    RunOnlyLegacyEligible: 1,
                    FillsUpdated: 2,
                    RunsUpdated: 2,
                    PositionsUpdated: 1,
                    SettlementsUpdated: 1),
                applied);
            Assert.Equal(
                fullGrossBefore,
                await ReadGrossSnapshotAsync(factory, fullWebSocketOrder.Id));
            Assert.Equal(
                runOnlyGrossBefore,
                await ReadFillRunGrossSnapshotAsync(factory, runOnlyOrder.Id));
            AssertCalculatedAccounting(
                await ReadAccountingSnapshotAsync(factory, fullWebSocketOrder.Id),
                scenario.FeeCalculatedAtUtc);
            AssertCalculatedFillRunAccounting(
                await ReadFillRunAccountingSnapshotAsync(factory, runOnlyOrder.Id),
                scenario.FeeCalculatedAtUtc);
            Assert.Equal(
                new DependentCounts(0, 0),
                await ReadDependentCountsAsync(factory, scenario.Wallet, runOnlyOrder.AssetId));
            Assert.Equal(
                new DashboardReconciliationSnapshot(50, 0, null),
                await ReadDashboardReconciliationAsync(factory, scenario.StrategyId));
            Assert.Equal(
                runOnlyGrossBefore,
                await ReadFillRunGrossSnapshotAsync(factory, runOnlyOrder.Id));
            foreach (var order in unsafeOrders)
            {
                Assert.Equal(
                    unsafeAccountingBefore[order.Id],
                    await ReadFillRunAccountingSnapshotAsync(factory, order.Id));
            }

            var retry = await repository.ApplyHistoricalPaperFakFeeBackfillBatchAsync(updates);

            Assert.Equal(
                new HistoricalPaperFakFeeBackfillBatchResult(
                    Requested: 5,
                    StructuralConflicts: 3,
                    FullChainEligible: 1,
                    RunOnlyLegacyEligible: 1,
                    FullChainAlreadyApplied: 1,
                    RunOnlyLegacyAlreadyApplied: 1),
                retry);
            Assert.Equal(
                new DashboardReconciliationSnapshot(50, 0, null),
                await ReadDashboardReconciliationAsync(factory, scenario.StrategyId));
        }
        finally
        {
            await CleanupScenarioAsync(factory, scenario);
        }
    }

    private static void AssertGrossRanksDescending(
        IReadOnlyList<HistoricalPaperFakFeeBackfillStrategyRank> ranks)
    {
        decimal? previousGross = null;
        foreach (var rank in ranks)
        {
            if (previousGross is not null)
            {
                Assert.True(previousGross.Value >= rank.GrossRealizedPnlUsd);
            }

            previousGross = rank.GrossRealizedPnlUsd;
        }
    }

    private static HistoricalPaperFakFeeBackfillUpdate CreateUpdate(
        HistoricalPaperFakFeeBackfillCandidate candidate,
        DateTimeOffset calculatedAtUtc)
    {
        return new HistoricalPaperFakFeeBackfillUpdate(
            candidate,
            candidate.Fill with
            {
                FeeUsd = 0.035m,
                FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
                FeeLiquidityRole = FeeLiquidityRole.Taker.ToString(),
                FeeCalculationSource = HistoricalPrefix +
                    PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
                FeeRate = 0.07m,
                FeeExponent = 1,
                FeeTakerOnly = true,
                FeeCalculatedAtUtc = calculatedAtUtc
            });
    }

    private static void AssertCalculatedAccounting(
        AccountingSnapshot actual,
        DateTimeOffset calculatedAtUtc)
    {
        Assert.Equal(0.035m, actual.FillFeeUsd);
        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), actual.FillStatus);
        Assert.Equal(FeeLiquidityRole.Taker.ToString(), actual.FillRole);
        Assert.Equal(
            HistoricalPrefix + PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
            actual.FillSource);
        Assert.Equal(0.07m, actual.FillRate);
        Assert.Equal(1, actual.FillExponent);
        Assert.True(actual.FillTakerOnly);
        Assert.Equal(calculatedAtUtc, actual.FillCalculatedAtUtc);
        Assert.Null(actual.FillNetPnlUsd);

        Assert.Equal(0.035m, actual.RunFeeUsd);
        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), actual.RunStatus);
        Assert.Equal(actual.FillSource, actual.RunSource);
        Assert.Equal(0.965m, actual.RunNetPnlUsd);

        Assert.Equal(0m, actual.PositionFeeUsd);
        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), actual.PositionStatus);
        Assert.Equal(FeeLiquidityRole.Unknown.ToString(), actual.PositionRole);
        Assert.Equal(string.Empty, actual.PositionSource);
        Assert.Null(actual.PositionRate);
        Assert.Null(actual.PositionExponent);
        Assert.Null(actual.PositionTakerOnly);
        Assert.Null(actual.PositionCalculatedAtUtc);
        Assert.Equal(0m, actual.PositionNetPnlUsd);

        Assert.Equal(0.035m, actual.SettlementFeeUsd);
        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), actual.SettlementStatus);
        Assert.Equal(actual.FillSource, actual.SettlementSource);
        Assert.Equal(0.965m, actual.SettlementNetPnlUsd);
    }

    private static void AssertCalculatedFillRunAccounting(
        FillRunAccountingSnapshot actual,
        DateTimeOffset calculatedAtUtc)
    {
        var expectedSource = HistoricalPrefix +
            PolymarketFeeCalculationConstants.FeeCurveCalculationSource;
        Assert.Equal(0.035m, actual.FillFeeUsd);
        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), actual.FillStatus);
        Assert.Equal(FeeLiquidityRole.Taker.ToString(), actual.FillRole);
        Assert.Equal(expectedSource, actual.FillSource);
        Assert.Equal(0.07m, actual.FillRate);
        Assert.Equal(1, actual.FillExponent);
        Assert.True(actual.FillTakerOnly);
        Assert.Equal(calculatedAtUtc, actual.FillCalculatedAtUtc);
        Assert.Null(actual.FillNetPnlUsd);

        Assert.Equal(0.035m, actual.RunFeeUsd);
        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), actual.RunStatus);
        Assert.Equal(FeeLiquidityRole.Taker.ToString(), actual.RunRole);
        Assert.Equal(expectedSource, actual.RunSource);
        Assert.Equal(0.07m, actual.RunRate);
        Assert.Equal(1, actual.RunExponent);
        Assert.True(actual.RunTakerOnly);
        Assert.Equal(calculatedAtUtc, actual.RunCalculatedAtUtc);
        Assert.Equal(0.965m, actual.RunNetPnlUsd);
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

    private static async Task SeedScenarioAsync(
        PostgresConnectionFactory factory,
        PostgresAppRepository repository,
        BackfillScenario scenario)
    {
        await SeedStrategyAsync(factory, scenario);

        var targetOrder = CreateOrder(
            scenario,
            scenario.TargetOrderId,
            scenario.TargetAssetId,
            scenario.TargetConditionId,
            scenario.BaseUtc,
            "btc_updown5m_fak_taker_paper",
            notionalUsd: 9m);
        var conflictOrder = CreateOrder(
            scenario,
            scenario.ConflictOrderId,
            scenario.ConflictAssetId,
            scenario.ConflictConditionId,
            scenario.BaseUtc.AddSeconds(1),
            "btc_updown5m_child_mirror_fak_paper");
        var shadowOrder = CreateOrder(
            scenario,
            scenario.ShadowOrderId,
            $"shadow-{scenario.Suffix}",
            $"shadow-condition-{scenario.Suffix}",
            scenario.BaseUtc.AddSeconds(2),
            "paper_live_shadow_actual_fill");
        var gtdOrder = CreateOrder(
            scenario,
            scenario.GtdOrderId,
            $"gtd-{scenario.Suffix}",
            $"gtd-condition-{scenario.Suffix}",
            scenario.BaseUtc.AddSeconds(3),
            "btc_updown5m_gtd_limit");
        var currentOrder = CreateOrder(
            scenario,
            scenario.CurrentOrderId,
            $"current-{scenario.Suffix}",
            $"current-condition-{scenario.Suffix}",
            scenario.BaseUtc.AddSeconds(4),
            "btc_updown5m_fak_taker_paper");
        var afterCutoffOrder = CreateOrder(
            scenario,
            scenario.AfterCutoffOrderId,
            $"after-cutoff-{scenario.Suffix}",
            $"after-cutoff-condition-{scenario.Suffix}",
            scenario.CutoffUtc.AddSeconds(1),
            "btc_updown5m_fak_taker_paper");
        var orders = new[]
        {
            targetOrder,
            conflictOrder,
            shadowOrder,
            gtdOrder,
            currentOrder,
            afterCutoffOrder
        };
        foreach (var order in orders)
        {
            await repository.AddPaperOrderAsync(order);
        }

        var targetFill = CreateLegacyFill(scenario.TargetFillId, targetOrder);
        var conflictFill = CreateLegacyFill(scenario.ConflictFillId, conflictOrder);
        var fills = new[]
        {
            targetFill,
            conflictFill,
            CreateLegacyFill(Guid.NewGuid(), shadowOrder),
            CreateLegacyFill(Guid.NewGuid(), gtdOrder),
            CreateLegacyFill(Guid.NewGuid(), afterCutoffOrder),
            CreateLegacyFill(Guid.NewGuid(), currentOrder) with
            {
                FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
                FeeLiquidityRole = FeeLiquidityRole.Taker.ToString(),
                FeeCalculationSource = PolymarketFeeCalculationConstants.FeeFreeMarketCalculationSource,
                FeeCalculatedAtUtc = scenario.BaseUtc.AddMinutes(1),
                NetRealizedPnlUsd = 0m
            }
        };
        foreach (var fill in fills)
        {
            await repository.AddPaperFillAsync(fill);
        }

        Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(
            CreateSettledRun(scenario.TargetRunId, targetOrder)));
        Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(
            CreateSettledRun(scenario.ConflictRunId, conflictOrder)));
        await repository.UpsertPaperPositionAsync(CreateZeroPosition(targetOrder));
        await repository.UpsertPaperPositionAsync(CreateZeroPosition(conflictOrder));
        Assert.True(await repository.TryAddPaperPositionSettlementAsync(
            CreateSettlement(scenario.TargetSettlementId, targetOrder, costBasisUsd: 1m)));
        Assert.True(await repository.TryAddPaperPositionSettlementAsync(
            CreateSettlement(scenario.ConflictSettlementId, conflictOrder, costBasisUsd: 1.01m)));
    }

    private static async Task SeedStrategyAsync(
        PostgresConnectionFactory factory,
        BackfillScenario scenario)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.strategies (
    id, code, name, description, enabled, live_stakes, created_at_utc, updated_at_utc)
VALUES (
    @Id, @Code, @Code, 'historical fee backfill integration test', true, false,
    @CreatedAtUtc, @CreatedAtUtc);
""",
            connection);
        command.Parameters.AddWithValue("Id", scenario.StrategyId);
        command.Parameters.AddWithValue("Code", scenario.StrategyCode);
        command.Parameters.AddWithValue("CreatedAtUtc", scenario.BaseUtc.UtcDateTime);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertParityDecisionAuditAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        string sourceKind,
        Guid sourceId,
        string oldPayloadJson,
        string evidencePayloadJson)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.historical_gross_net_parity_audit (
    audit_id, source_kind, source_id, strategy_id, calculation_version,
    operation_kind, evidence_version, old_payload_json, new_payload_json,
    evidence_payload_json)
VALUES (
    @AuditId, @SourceKind, @SourceId, @StrategyId, 'historical-gross-net-parity-v1',
    'AccountingDecision', @EvidenceVersion, @OldPayload::jsonb, '{}'::jsonb,
    @EvidencePayload::jsonb);
""",
            connection);
        command.Parameters.AddWithValue("AuditId", Guid.NewGuid());
        command.Parameters.AddWithValue("SourceKind", sourceKind);
        command.Parameters.AddWithValue("SourceId", sourceId);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("EvidenceVersion", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("OldPayload", oldPayloadJson);
        command.Parameters.AddWithValue("EvidencePayload", evidencePayloadJson);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static string CreateParityBindingEvidence(Guid fillId) =>
        JsonSerializer.Serialize(new
        {
            historicalGrossNetParityBindingV1 = new
            {
                paperFillIds = new[] { fillId.ToString("D").ToLowerInvariant() }
            }
        });

    private static PaperOrder CreateOrder(
        BackfillScenario scenario,
        Guid orderId,
        string assetId,
        string conditionId,
        DateTimeOffset filledAtUtc,
        string executionSource,
        decimal notionalUsd = 1m)
    {
        return new PaperOrder(
            orderId,
            Guid.NewGuid(),
            scenario.Wallet,
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            assetId,
            conditionId,
            "Up",
            0.50m,
            2m,
            notionalUsd,
            filledAtUtc.AddSeconds(-10),
            filledAtUtc.AddSeconds(10),
            FilledAtUtc: filledAtUtc,
            StrategyId: scenario.StrategyId,
            ExecutionSource: executionSource);
    }

    private static PaperFill CreateLegacyFill(Guid fillId, PaperOrder order)
    {
        return new PaperFill(
            fillId,
            order.Id,
            order.Price,
            order.SizeShares,
            order.FilledAtUtc!.Value,
            "historical FAK integration fill");
    }

    private static StrategyMarketPaperRun CreateSettledRun(Guid runId, PaperOrder order)
    {
        var filledAtUtc = order.FilledAtUtc!.Value;
        return new StrategyMarketPaperRun(
            runId,
            order.StrategyId,
            $"market-{runId:N}",
            order.ConditionId,
            $"market-{runId:N}",
            "Historical FAK fee integration market",
            "Test",
            filledAtUtc.AddMinutes(-5),
            filledAtUtc,
            filledAtUtc.AddMinutes(-6),
            filledAtUtc.AddMinutes(-1),
            StrategyMarketPaperRunStatuses.Settled,
            order.AssetId,
            order.Outcome,
            order.Price,
            1m,
            order.SizeShares,
            null,
            order.Id,
            filledAtUtc,
            1m,
            2m,
            1m,
            filledAtUtc.AddMinutes(5),
            null,
            filledAtUtc.AddMinutes(-6),
            filledAtUtc.AddMinutes(5));
    }

    private static PaperPosition CreateZeroPosition(PaperOrder order)
    {
        return new PaperPosition(
            order.AssetId,
            order.ConditionId,
            order.Outcome,
            0m,
            0m,
            0m,
            0m,
            order.FilledAtUtc!.Value.AddMinutes(5),
            order.CopiedTraderWallet);
    }

    private static PaperPositionSettlement CreateSettlement(
        Guid settlementId,
        PaperOrder order,
        decimal costBasisUsd)
    {
        var settledAtUtc = order.FilledAtUtc!.Value.AddMinutes(5);
        return new PaperPositionSettlement(
            settlementId,
            order.CopiedTraderWallet,
            order.AssetId,
            order.ConditionId,
            order.Outcome,
            order.AssetId,
            order.Outcome,
            "Test",
            order.SizeShares,
            order.Price,
            costBasisUsd,
            2m,
            1m,
            true,
            "BtcUpDown5mGammaClosedMarket",
            settledAtUtc,
            settledAtUtc.AddSeconds(1));
    }

    private static async Task<string> ReadGrossSnapshotAsync(
        PostgresConnectionFactory factory,
        Guid orderId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT jsonb_build_object(
    'fill', jsonb_build_object(
        'price', fill.price, 'size', fill.size_shares, 'filled', fill.filled_at_utc,
        'evidence', fill.evidence, 'realized', fill.realized_pnl_usd),
    'run', jsonb_build_object(
        'status', run.status, 'stake', run.stake_usd, 'entry', run.entry_price,
        'size', run.size_shares, 'settlement_price', run.settlement_price,
        'settlement_value', run.settlement_value_usd, 'realized', run.realized_pnl_usd,
        'settled', run.settled_at_utc, 'updated', run.updated_at_utc),
    'position', jsonb_build_object(
        'condition', position.condition_id, 'outcome', position.outcome,
        'size', position.size_shares, 'average', position.average_price,
        'estimated', position.estimated_value_usd, 'unrealized', position.unrealized_pnl_usd,
        'updated', position.updated_at_utc),
    'settlement', jsonb_build_object(
        'size', settlement.settled_size_shares, 'average', settlement.average_price,
        'cost', settlement.cost_basis_usd, 'value', settlement.settlement_value_usd,
        'realized', settlement.realized_pnl_usd, 'won', settlement.won,
        'settled', settlement.settled_at_utc, 'created', settlement.created_at_utc))::text
FROM public.paper_orders paper_order
INNER JOIN public.paper_fills fill ON fill.paper_order_id = paper_order.id
INNER JOIN public.strategy_market_paper_runs run ON run.paper_order_id = paper_order.id
INNER JOIN public.paper_positions position
    ON position.copied_trader_wallet = paper_order.copied_trader_wallet
   AND position.asset_id = paper_order.asset_id
INNER JOIN public.paper_position_settlements settlement
    ON settlement.copied_trader_wallet = paper_order.copied_trader_wallet
   AND settlement.asset_id = paper_order.asset_id
WHERE paper_order.id = @OrderId;
""",
            connection);
        command.Parameters.AddWithValue("OrderId", orderId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The backfill gross chain was not found."));
    }

    private static async Task<AccountingSnapshot> ReadAccountingSnapshotAsync(
        PostgresConnectionFactory factory,
        Guid orderId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    fill.fee_usd, fill.fee_accounting_status, fill.fee_liquidity_role,
    fill.fee_calculation_source, fill.fee_rate, fill.fee_exponent,
    fill.fee_taker_only, fill.fee_calculated_at_utc, fill.net_realized_pnl_usd,
    run.fee_usd, run.fee_accounting_status, run.fee_calculation_source,
    run.net_realized_pnl_usd,
    position.fee_usd, position.fee_accounting_status, position.fee_liquidity_role,
    position.fee_calculation_source, position.fee_rate, position.fee_exponent,
    position.fee_taker_only, position.fee_calculated_at_utc, position.net_unrealized_pnl_usd,
    settlement.fee_usd, settlement.fee_accounting_status,
    settlement.fee_calculation_source, settlement.net_realized_pnl_usd
FROM public.paper_orders paper_order
INNER JOIN public.paper_fills fill ON fill.paper_order_id = paper_order.id
INNER JOIN public.strategy_market_paper_runs run ON run.paper_order_id = paper_order.id
INNER JOIN public.paper_positions position
    ON position.copied_trader_wallet = paper_order.copied_trader_wallet
   AND position.asset_id = paper_order.asset_id
INNER JOIN public.paper_position_settlements settlement
    ON settlement.copied_trader_wallet = paper_order.copied_trader_wallet
   AND settlement.asset_id = paper_order.asset_id
WHERE paper_order.id = @OrderId;
""",
            connection);
        command.Parameters.AddWithValue("OrderId", orderId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new AccountingSnapshot(
            reader.GetDecimal(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            NullableDecimal(reader, 4),
            NullableInt32(reader, 5),
            NullableBoolean(reader, 6),
            NullableDateTimeOffset(reader, 7),
            NullableDecimal(reader, 8),
            reader.GetDecimal(9),
            reader.GetString(10),
            reader.GetString(11),
            NullableDecimal(reader, 12),
            reader.GetDecimal(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            NullableDecimal(reader, 17),
            NullableInt32(reader, 18),
            NullableBoolean(reader, 19),
            NullableDateTimeOffset(reader, 20),
            NullableDecimal(reader, 21),
            reader.GetDecimal(22),
            reader.GetString(23),
            reader.GetString(24),
            NullableDecimal(reader, 25));
    }

    private static async Task<FillRunAccountingSnapshot> ReadFillRunAccountingSnapshotAsync(
        PostgresConnectionFactory factory,
        Guid orderId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    fill.fee_usd, fill.fee_accounting_status, fill.fee_liquidity_role,
    fill.fee_calculation_source, fill.fee_rate, fill.fee_exponent,
    fill.fee_taker_only, fill.fee_calculated_at_utc, fill.net_realized_pnl_usd,
    run.fee_usd, run.fee_accounting_status, run.fee_liquidity_role,
    run.fee_calculation_source, run.fee_rate, run.fee_exponent,
    run.fee_taker_only, run.fee_calculated_at_utc, run.net_realized_pnl_usd
FROM public.paper_orders paper_order
INNER JOIN public.paper_fills fill ON fill.paper_order_id = paper_order.id
INNER JOIN public.strategy_market_paper_runs run ON run.paper_order_id = paper_order.id
WHERE paper_order.id = @OrderId;
""",
            connection);
        command.Parameters.AddWithValue("OrderId", orderId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new FillRunAccountingSnapshot(
            reader.GetDecimal(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            NullableDecimal(reader, 4),
            NullableInt32(reader, 5),
            NullableBoolean(reader, 6),
            NullableDateTimeOffset(reader, 7),
            NullableDecimal(reader, 8),
            reader.GetDecimal(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            NullableDecimal(reader, 13),
            NullableInt32(reader, 14),
            NullableBoolean(reader, 15),
            NullableDateTimeOffset(reader, 16),
            NullableDecimal(reader, 17));
    }

    private static async Task<string> ReadFillRunGrossSnapshotAsync(
        PostgresConnectionFactory factory,
        Guid orderId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT jsonb_build_object(
    'fill', jsonb_build_object(
        'price', fill.price, 'size', fill.size_shares, 'filled', fill.filled_at_utc,
        'evidence', fill.evidence, 'realized', fill.realized_pnl_usd),
    'run', jsonb_build_object(
        'status', run.status, 'stake', run.stake_usd, 'entry', run.entry_price,
        'size', run.size_shares, 'settlement_price', run.settlement_price,
        'settlement_value', run.settlement_value_usd, 'realized', run.realized_pnl_usd,
        'settled', run.settled_at_utc, 'updated', run.updated_at_utc))::text
FROM public.paper_orders paper_order
INNER JOIN public.paper_fills fill ON fill.paper_order_id = paper_order.id
INNER JOIN public.strategy_market_paper_runs run ON run.paper_order_id = paper_order.id
WHERE paper_order.id = @OrderId;
""",
            connection);
        command.Parameters.AddWithValue("OrderId", orderId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The backfill fill/run Gross chain was not found."));
    }

    private static async Task<DependentCounts> ReadDependentCountsAsync(
        PostgresConnectionFactory factory,
        string wallet,
        string assetId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    (SELECT count(*)::integer
     FROM public.paper_positions
     WHERE copied_trader_wallet = @Wallet AND asset_id = @AssetId),
    (SELECT count(*)::integer
     FROM public.paper_position_settlements
     WHERE copied_trader_wallet = @Wallet AND asset_id = @AssetId);
""",
            connection);
        command.Parameters.AddWithValue("Wallet", wallet);
        command.Parameters.AddWithValue("AssetId", assetId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new DependentCounts(reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task<DashboardReconciliationSnapshot?> ReadDashboardReconciliationAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT priority, attempt_count, last_error
FROM public.dashboard_projection_reconciliation_queue
WHERE strategy_id = @StrategyId;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var result = new DashboardReconciliationSnapshot(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task<string> ReadProjectionSnapshotAsync(
        PostgresConnectionFactory factory,
        BackfillScenario scenario)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT jsonb_build_object(
    'dashboard_count', (
        SELECT count(*)
        FROM public.dashboard_projection_events
        WHERE strategy_id = @StrategyId),
    'dashboard_max_id', (
        SELECT max(id)
        FROM public.dashboard_projection_events
        WHERE strategy_id = @StrategyId),
    'performance_queue', (
        SELECT COALESCE(jsonb_agg(to_jsonb(queue_row) ORDER BY queue_row.copied_trader_wallet), '[]'::jsonb)
        FROM (
            SELECT copied_trader_wallet, priority, requested_at_utc, source_kind
            FROM public.paper_copied_trader_performance_refresh_queue
            WHERE copied_trader_wallet = @Wallet
        ) queue_row))::text;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", scenario.StrategyId);
        command.Parameters.AddWithValue("Wallet", scenario.Wallet);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The projection snapshot was not returned."));
    }

    private static async Task CleanupScenarioAsync(
        PostgresConnectionFactory factory,
        BackfillScenario scenario)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
DELETE FROM public.paper_position_settlements WHERE copied_trader_wallet = @Wallet;
DELETE FROM public.paper_positions WHERE copied_trader_wallet = @Wallet;
DELETE FROM public.strategy_market_paper_runs WHERE strategy_id = @StrategyId;
DELETE FROM public.paper_fills fill
USING public.paper_orders paper_order
WHERE fill.paper_order_id = paper_order.id
  AND paper_order.strategy_id = @StrategyId;
DELETE FROM public.paper_orders WHERE strategy_id = @StrategyId;
DELETE FROM public.dashboard_strategy_recent_projection_facts WHERE strategy_id = @StrategyId;
DELETE FROM public.dashboard_strategy_lifetime_projection_states WHERE strategy_id = @StrategyId;
DELETE FROM public.dashboard_strategy_recent_projection_states WHERE strategy_id = @StrategyId;
DELETE FROM public.dashboard_strategy_performance_snapshots WHERE strategy_id = @StrategyId;
DELETE FROM public.dashboard_strategy_recent_performance_snapshots WHERE strategy_id = @StrategyId;
DELETE FROM public.dashboard_projection_reconciliation_queue WHERE strategy_id = @StrategyId;
DELETE FROM public.strategies WHERE id = @StrategyId;
DELETE FROM public.dashboard_projection_events WHERE strategy_id = @StrategyId;
DELETE FROM public.paper_copied_trader_performance_refresh_inflight
WHERE copied_trader_wallet = @Wallet;
DELETE FROM public.paper_copied_trader_performance_refresh_queue
WHERE copied_trader_wallet = @Wallet;
DELETE FROM public.paper_copied_trader_performance
WHERE copied_trader_wallet = @Wallet;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", scenario.StrategyId);
        command.Parameters.AddWithValue("Wallet", scenario.Wallet);
        await command.ExecuteNonQueryAsync();
    }

    private static decimal? NullableDecimal(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static int? NullableInt32(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static bool? NullableBoolean(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);

    private static DateTimeOffset? NullableDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return new DateTimeOffset(
            DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    }

    private sealed record AccountingSnapshot(
        decimal FillFeeUsd,
        string FillStatus,
        string FillRole,
        string FillSource,
        decimal? FillRate,
        int? FillExponent,
        bool? FillTakerOnly,
        DateTimeOffset? FillCalculatedAtUtc,
        decimal? FillNetPnlUsd,
        decimal RunFeeUsd,
        string RunStatus,
        string RunSource,
        decimal? RunNetPnlUsd,
        decimal PositionFeeUsd,
        string PositionStatus,
        string PositionRole,
        string PositionSource,
        decimal? PositionRate,
        int? PositionExponent,
        bool? PositionTakerOnly,
        DateTimeOffset? PositionCalculatedAtUtc,
        decimal? PositionNetPnlUsd,
        decimal SettlementFeeUsd,
        string SettlementStatus,
        string SettlementSource,
        decimal? SettlementNetPnlUsd);

    private sealed record FillRunAccountingSnapshot(
        decimal FillFeeUsd,
        string FillStatus,
        string FillRole,
        string FillSource,
        decimal? FillRate,
        int? FillExponent,
        bool? FillTakerOnly,
        DateTimeOffset? FillCalculatedAtUtc,
        decimal? FillNetPnlUsd,
        decimal RunFeeUsd,
        string RunStatus,
        string RunRole,
        string RunSource,
        decimal? RunRate,
        int? RunExponent,
        bool? RunTakerOnly,
        DateTimeOffset? RunCalculatedAtUtc,
        decimal? RunNetPnlUsd);

    private sealed record DependentCounts(int Positions, int Settlements);

    private sealed record DashboardReconciliationSnapshot(
        int Priority,
        int AttemptCount,
        string? LastError);

    private sealed record BackfillScenario(
        Guid StrategyId,
        string StrategyCode,
        string Wallet,
        string Suffix,
        DateTimeOffset BaseUtc,
        DateTimeOffset CutoffUtc,
        DateTimeOffset FeeCalculatedAtUtc,
        Guid TargetOrderId,
        Guid TargetFillId,
        Guid TargetRunId,
        Guid TargetSettlementId,
        string TargetAssetId,
        string TargetConditionId,
        Guid ConflictOrderId,
        Guid ConflictFillId,
        Guid ConflictRunId,
        Guid ConflictSettlementId,
        string ConflictAssetId,
        string ConflictConditionId,
        Guid ShadowOrderId,
        Guid GtdOrderId,
        Guid CurrentOrderId,
        Guid AfterCutoffOrderId)
    {
        public static BackfillScenario Create()
        {
            var suffix = Guid.NewGuid().ToString("N");
            var code = $"fee_backfill_{suffix}";
            var baseUtc = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)
                .AddSeconds(Random.Shared.Next(1, 3600));
            return new BackfillScenario(
                Guid.NewGuid(),
                code,
                $"strategy:{code}",
                suffix,
                baseUtc,
                baseUtc.AddHours(1),
                baseUtc.AddHours(2),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                $"target-{suffix}",
                $"target-condition-{suffix}",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                $"conflict-{suffix}",
                $"conflict-condition-{suffix}",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid());
        }
    }
}
