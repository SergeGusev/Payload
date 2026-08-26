using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class PaperFakFeeBackfillProcessorTests
{
    private static readonly DateTimeOffset CutoffUtc =
        new DateTimeOffset(2026, 8, 7, 22, 44, 55, TimeSpan.Zero).AddTicks(2_195_150);
    private static readonly DateTimeOffset FilledAtUtc = CutoffUtc.AddMinutes(-1);

    [Fact]
    public async Task RunCycle_ForcesTaker_GroupsConditions_AndAppliesNonTransientResultsWithProvenance()
    {
        var conditionAFirst = CreateCandidate("condition-a");
        var conditionB = CreateCandidate("condition-b");
        var conditionASecond = CreateCandidate("CONDITION-A");
        var transient = CreateCandidate("condition-c");
        var repository = new TestAppRepository();
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage(
                [conditionAFirst, conditionB, conditionASecond, transient],
                null,
                true));
        var feeService = new RecordingFeeAccountingService(async (order, fill, _) =>
        {
            await Task.Yield();
            if (string.Equals(order.ConditionId, "condition-c", StringComparison.OrdinalIgnoreCase))
            {
                return fill with
                {
                    FeeAccountingStatus = FeeAccountingStatus.CalculationUnavailable.ToString(),
                    FeeCalculationSource = PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource,
                    FeeCalculatedAtUtc = FilledAtUtc.AddMinutes(1)
                };
            }

            if (string.Equals(order.ConditionId, "condition-b", StringComparison.OrdinalIgnoreCase))
            {
                return fill with
                {
                    FeeAccountingStatus = FeeAccountingStatus.CalculationUnavailable.ToString(),
                    FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
                    FeeCalculatedAtUtc = FilledAtUtc.AddMinutes(1)
                };
            }

            return fill with
            {
                FeeUsd = 0.01234m,
                FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
                FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
                FeeCalculatedAtUtc = FilledAtUtc.AddMinutes(1)
            };
        });
        var eventRecorder = new RecordingEventRecorder();
        var processor = CreateProcessor(
            repository,
            feeService,
            applyEnabled: true,
            eventRecorder);
        var cycleId = Guid.Parse("50000000-0000-0000-0000-000000000001");

        var result = await processor.RunCycleAsync(cycleId);

        Assert.Equal(
            ["condition-a", "CONDITION-A", "condition-b", "condition-c"],
            feeService.Calls.Select(call => call.Order.ConditionId).ToArray());
        Assert.All(
            feeService.Calls,
            call => Assert.Equal(FeeLiquidityRole.Taker.ToString(), call.Fill.FeeLiquidityRole));
        Assert.Equal(1, feeService.MaxConcurrentCalls);
        Assert.Equal(4, result.Candidates);
        Assert.Equal(3, result.EvaluatedForApply);
        Assert.Equal(1, result.TransientLookupUnavailable);
        Assert.False(result.ReachedEnd);
        Assert.Equal(3, result.ApplyResult?.Requested);

        var updates = Assert.Single(repository.HistoricalPaperFakFeeBackfillApplyCalls);
        Assert.Equal(3, updates.Count);
        Assert.DoesNotContain(updates, update => update.Expected.Fill.Id == transient.Fill.Id);
        Assert.Contains(
            updates,
            update => update.Expected.Fill.Id == conditionB.Fill.Id &&
                update.EvaluatedFill.FeeAccountingStatus == FeeAccountingStatus.CalculationUnavailable.ToString());
        Assert.All(
            updates,
            update => Assert.Equal(
                PaperFakFeeBackfillProcessor.HistoricalCalculationSourcePrefix +
                    PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
                update.EvaluatedFill.FeeCalculationSource));
        Assert.Equal(CutoffUtc, Assert.Single(repository.HistoricalPaperFakFeeBackfillCalls).FilledBeforeUtc);
        Assert.Equal(50, Assert.Single(repository.HistoricalPaperFakFeeBackfillCalls).Limit);

        var rankingEvent = Assert.Single(
            eventRecorder.Events,
            entry => entry.EventType == PaperFakFeeBackfillEventTypes.StrategyRankingFrozen);
        var contextEvent = Assert.Single(
            eventRecorder.Events,
            entry => entry.EventType == PaperFakFeeBackfillEventTypes.CycleContext);
        var cycleEvent = Assert.Single(
            eventRecorder.Events,
            entry => entry.EventType == PaperFakFeeBackfillEventTypes.CycleCompleted);
        Assert.Equal(cycleId, rankingEvent.CycleId);
        Assert.Equal(cycleId, contextEvent.CycleId);
        Assert.Equal(cycleId, cycleEvent.CycleId);
        Assert.NotNull(rankingEvent.SweepId);
        Assert.Equal(rankingEvent.SweepId, contextEvent.SweepId);
        Assert.Equal(rankingEvent.SweepId, cycleEvent.SweepId);
        Assert.Equal(conditionAFirst.Order.StrategyId, contextEvent.StrategyId);
        Assert.Equal(1, contextEvent.StrategyRank);
        Assert.Equal(1, contextEvent.StrategyCount);
        Assert.Equal(1, cycleEvent.StrategyRank);
        Assert.Equal(1, cycleEvent.StrategyCount);
        Assert.Equal(4, cycleEvent.Candidates);
        Assert.Equal(3, cycleEvent.EvaluatedForApply);
        Assert.Equal(1, cycleEvent.TransientLookupUnavailable);
        Assert.Equal(3, cycleEvent.Requested);
        Assert.Equal(3, cycleEvent.StructuralConflicts);
        Assert.False(cycleEvent.ReachedStrategyEnd);
        Assert.False(cycleEvent.ReachedSweepEnd);
        Assert.True(cycleEvent.DurationMilliseconds >= 0);
    }

    [Fact]
    public async Task RunCycle_EmptyRanking_RecordsCompletedSweepAndResetsItForNextCycle()
    {
        var repository = new TestAppRepository();
        var feeService = new RecordingFeeAccountingService((_, _, _) =>
            throw new InvalidOperationException("Fee evaluation must not run for an empty ranking."));
        var eventRecorder = new RecordingEventRecorder();
        var processor = CreateProcessor(
            repository,
            feeService,
            applyEnabled: true,
            eventRecorder);
        var firstCycleId = Guid.Parse("51000000-0000-0000-0000-000000000001");
        var secondCycleId = Guid.Parse("51000000-0000-0000-0000-000000000002");

        var first = await processor.RunCycleAsync(firstCycleId);
        var second = await processor.RunCycleAsync(secondCycleId);

        Assert.True(first.ReachedEnd);
        Assert.True(second.ReachedEnd);
        Assert.Equal(2, repository.HistoricalPaperFakFeeBackfillStrategyRankCalls.Count);
        Assert.Empty(feeService.Calls);
        Assert.DoesNotContain(
            eventRecorder.Events,
            entry => entry.EventType == PaperFakFeeBackfillEventTypes.CycleContext);

        var rankingEvents = eventRecorder.Events
            .Where(entry => entry.EventType == PaperFakFeeBackfillEventTypes.StrategyRankingFrozen)
            .ToArray();
        var completedEvents = eventRecorder.Events
            .Where(entry => entry.EventType == PaperFakFeeBackfillEventTypes.CycleCompleted)
            .ToArray();
        Assert.Equal(2, rankingEvents.Length);
        Assert.Equal(2, completedEvents.Length);
        Assert.Equal([firstCycleId, secondCycleId], rankingEvents.Select(entry => entry.CycleId));
        Assert.Equal([firstCycleId, secondCycleId], completedEvents.Select(entry => entry.CycleId));
        Assert.All(rankingEvents, entry => Assert.Equal(0, entry.StrategyCount));
        Assert.All(completedEvents, entry =>
        {
            Assert.Equal(0, entry.StrategyCount);
            Assert.Equal(0, entry.Candidates);
            Assert.True(entry.ReachedStrategyEnd);
            Assert.True(entry.ReachedSweepEnd);
        });
        Assert.NotNull(rankingEvents[0].SweepId);
        Assert.NotNull(rankingEvents[1].SweepId);
        Assert.NotEqual(rankingEvents[0].SweepId, rankingEvents[1].SweepId);
        Assert.Equal(rankingEvents[0].SweepId, completedEvents[0].SweepId);
        Assert.Equal(rankingEvents[1].SweepId, completedEvents[1].SweepId);
    }

    [Fact]
    public async Task RunCycle_AdvancesPastTransientLookupAndRetriesItOnNextSweep()
    {
        var candidate = CreateCandidate("condition-a");
        var cursor = new HistoricalPaperFakFeeBackfillCursor(
            candidate.Order.StrategyId,
            candidate.Fill.FilledAtUtc,
            candidate.Order.Id,
            candidate.Fill.Id);
        var repository = new TestAppRepository();
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([candidate], cursor, false));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([], null, true));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([candidate], null, true));
        var feeService = new RecordingFeeAccountingService((_, fill, callNumber) =>
            Task.FromResult(callNumber == 1
                ? fill with
                {
                    FeeAccountingStatus = FeeAccountingStatus.CalculationUnavailable.ToString(),
                    FeeCalculationSource = PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource
                }
                : fill with
                {
                    FeeUsd = 0.01m,
                    FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
                    FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource
                }));
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        var transientCycle = await processor.RunCycleAsync();
        var exactEndCycle = await processor.RunCycleAsync();
        var repairCycle = await processor.RunCycleAsync();
        var sweepEndCycle = await processor.RunCycleAsync();
        var retryCycle = await processor.RunCycleAsync();

        Assert.Equal(1, transientCycle.TransientLookupUnavailable);
        Assert.Null(transientCycle.ApplyResult);
        Assert.False(exactEndCycle.ReachedEnd);
        Assert.NotNull(repairCycle.AuthoritativeNetRepairResult);
        Assert.True(sweepEndCycle.ReachedEnd);
        Assert.NotNull(sweepEndCycle.NetFallbackResult);
        Assert.Equal(1, retryCycle.EvaluatedForApply);
        Assert.Single(repository.HistoricalPaperFakFeeBackfillApplyCalls);
        Assert.All(
            repository.HistoricalPaperFakFeeBackfillCalls,
            call => Assert.Equal(candidate.Order.StrategyId, call.StrategyId));
        Assert.Null(repository.HistoricalPaperFakFeeBackfillCalls[0].AfterCursor);
        Assert.Equal(cursor, repository.HistoricalPaperFakFeeBackfillCalls[1].AfterCursor);
        Assert.Null(repository.HistoricalPaperFakFeeBackfillCalls[2].AfterCursor);
    }

    [Fact]
    public async Task RunCycle_LastPageWithTransientAndConflictStartsAnotherSweep()
    {
        var transient = CreateCandidate("condition-transient");
        var conflicting = CreateCandidate("condition-conflict");
        var repository = new TestAppRepository
        {
            HistoricalPaperFakFeeBackfillApplyResult = new(
                StructuralConflicts: 1)
        };
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([transient, conflicting], null, true));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([transient, conflicting], null, true));
        var feeService = new RecordingFeeAccountingService((order, fill, callNumber) =>
            Task.FromResult(
                string.Equals(order.ConditionId, "condition-transient", StringComparison.OrdinalIgnoreCase) &&
                callNumber <= 2
                    ? fill with
                    {
                        FeeAccountingStatus = FeeAccountingStatus.CalculationUnavailable.ToString(),
                        FeeCalculationSource = PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource
                    }
                    : fill with
                    {
                        FeeUsd = 0.01m,
                        FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
                        FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource
                    }));
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        var firstExactEnd = await processor.RunCycleAsync();
        var repairCycle = await processor.RunCycleAsync();
        var firstSweepEnd = await processor.RunCycleAsync();
        var secondExactEnd = await processor.RunCycleAsync();

        Assert.True(firstSweepEnd.ReachedEnd);
        Assert.Equal(1, firstExactEnd.TransientLookupUnavailable);
        Assert.Equal(1, firstExactEnd.ApplyResult?.StructuralConflicts);
        Assert.Equal(0, firstExactEnd.ApplyResult?.Deferred);
        Assert.NotNull(repairCycle.AuthoritativeNetRepairResult);
        Assert.NotNull(firstSweepEnd.NetFallbackResult);
        Assert.False(secondExactEnd.ReachedEnd);
        Assert.Equal(0, secondExactEnd.TransientLookupUnavailable);
        Assert.Equal(2, repository.HistoricalPaperFakFeeBackfillApplyCalls.Count);
        Assert.All(
            repository.HistoricalPaperFakFeeBackfillCalls,
            call => Assert.Null(call.AfterCursor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunCycle_WholeBatchDeferralRetriesExactPageWithoutAdvancingCursor(
        bool queryCancelled)
    {
        var candidate = CreateCandidate("condition-deferred");
        var cursor = new HistoricalPaperFakFeeBackfillCursor(
            candidate.Order.StrategyId,
            candidate.Fill.FilledAtUtc,
            candidate.Order.Id,
            candidate.Fill.Id);
        var repository = new TestAppRepository
        {
            HistoricalPaperFakFeeBackfillApplyResult = queryCancelled
                ? new(DeferredByQueryCancel: 1)
                : new(DeferredByLockTimeout: 1)
        };
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([candidate], cursor, false));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([candidate], cursor, false));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([], null, true));
        var feeService = new RecordingFeeAccountingService((_, fill, _) =>
            Task.FromResult(fill with
            {
                FeeUsd = 0.01m,
                FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
                FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource
            }));
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        var deferredCycle = await processor.RunCycleAsync();
        repository.HistoricalPaperFakFeeBackfillApplyResult = new(
            RunOnlyLegacyEligible: 1,
            FillsUpdated: 1,
            RunsUpdated: 1);
        var retryCycle = await processor.RunCycleAsync();
        var nextPageCycle = await processor.RunCycleAsync();
        var repairCycle = await processor.RunCycleAsync();
        var sweepEndCycle = await processor.RunCycleAsync();

        Assert.False(deferredCycle.ReachedEnd);
        Assert.True(deferredCycle.ApplyResult?.WholeBatchDeferred);
        Assert.Equal(1, deferredCycle.ApplyResult?.Deferred);
        Assert.False(retryCycle.ReachedEnd);
        Assert.False(retryCycle.ApplyResult?.WholeBatchDeferred);
        Assert.False(nextPageCycle.ReachedEnd);
        Assert.NotNull(repairCycle.AuthoritativeNetRepairResult);
        Assert.True(sweepEndCycle.ReachedEnd);
        Assert.Equal(2, repository.HistoricalPaperFakFeeBackfillApplyCalls.Count);
        Assert.Equal(3, repository.HistoricalPaperFakFeeBackfillCalls.Count);
        Assert.Null(repository.HistoricalPaperFakFeeBackfillCalls[0].AfterCursor);
        Assert.Null(repository.HistoricalPaperFakFeeBackfillCalls[1].AfterCursor);
        Assert.Equal(cursor, repository.HistoricalPaperFakFeeBackfillCalls[2].AfterCursor);
        Assert.Equal(
            [candidate.Fill.Id, candidate.Fill.Id],
            feeService.Calls.Take(2).Select(call => call.Fill.Id).ToArray());
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public async Task RunCycle_CompletedItemConflictAdvancesCursor(
        int structuralConflicts,
        int accountingConflicts)
    {
        var candidate = CreateCandidate("condition-conflict");
        var cursor = new HistoricalPaperFakFeeBackfillCursor(
            candidate.Order.StrategyId,
            candidate.Fill.FilledAtUtc,
            candidate.Order.Id,
            candidate.Fill.Id);
        var repository = new TestAppRepository
        {
            HistoricalPaperFakFeeBackfillApplyResult = new(
                StructuralConflicts: structuralConflicts,
                AccountingConflicts: accountingConflicts)
        };
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([candidate], cursor, false));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([], null, true));
        var feeService = new RecordingFeeAccountingService((_, fill, _) =>
            Task.FromResult(fill with
            {
                FeeUsd = 0.01m,
                FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
                FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource
            }));
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        var conflictCycle = await processor.RunCycleAsync();
        var nextPageCycle = await processor.RunCycleAsync();
        var repairCycle = await processor.RunCycleAsync();
        var sweepEndCycle = await processor.RunCycleAsync();

        Assert.False(conflictCycle.ReachedEnd);
        Assert.Equal(1, conflictCycle.ApplyResult?.ItemConflicts);
        Assert.Equal(0, conflictCycle.ApplyResult?.Deferred);
        Assert.False(conflictCycle.ApplyResult?.WholeBatchDeferred);
        Assert.False(nextPageCycle.ReachedEnd);
        Assert.NotNull(repairCycle.AuthoritativeNetRepairResult);
        Assert.True(sweepEndCycle.ReachedEnd);
        Assert.Equal(2, repository.HistoricalPaperFakFeeBackfillCalls.Count);
        Assert.Null(repository.HistoricalPaperFakFeeBackfillCalls[0].AfterCursor);
        Assert.Equal(cursor, repository.HistoricalPaperFakFeeBackfillCalls[1].AfterCursor);
    }

    [Fact]
    public async Task RunCycle_DoesNotMutateWhenApplyIsDisabled()
    {
        var candidate = CreateCandidate("condition-a");
        var repository = new TestAppRepository();
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([candidate], null, true));
        var feeService = new RecordingFeeAccountingService((_, fill, _) =>
            Task.FromResult(fill with
            {
                FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
                FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource
            }));
        var processor = CreateProcessor(repository, feeService, applyEnabled: false);

        var result = await processor.RunCycleAsync();

        Assert.False(result.ApplyEnabled);
        Assert.Equal(1, result.EvaluatedForApply);
        Assert.Null(result.ApplyResult);
        Assert.Empty(repository.HistoricalPaperFakFeeBackfillApplyCalls);
    }

    [Fact]
    public async Task RunCycle_CompletesStrategiesByGrossRealizedPnlDescendingAcrossConditionGroups()
    {
        var winnerStrategyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var loserStrategyId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var winnerA = CreateCandidate("condition-a", winnerStrategyId);
        var winnerB = CreateCandidate("condition-b", winnerStrategyId);
        var loserA = CreateCandidate("condition-a", loserStrategyId);
        var winnerCursor = new HistoricalPaperFakFeeBackfillCursor(
            winnerStrategyId,
            winnerA.Fill.FilledAtUtc,
            winnerA.Order.Id,
            winnerA.Fill.Id);
        var repository = new TestAppRepository();
        repository.HistoricalPaperFakFeeBackfillStrategyRanks.AddRange(
        [
            new(winnerStrategyId, "winner", 125m),
            new(loserStrategyId, "loser", -25m)
        ]);
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([winnerA], winnerCursor, false));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([winnerB], null, true));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([loserA], null, true));
        var feeService = new RecordingFeeAccountingService((_, fill, _) =>
            Task.FromResult(fill with
            {
                FeeUsd = 0.01m,
                FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
                FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource
            }));
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        var winnerFirstPageCycle = await processor.RunCycleAsync();
        var winnerLastPageCycle = await processor.RunCycleAsync();
        var winnerRepairCycle = await processor.RunCycleAsync();
        var winnerFallbackCycle = await processor.RunCycleAsync();
        var loserExactCycle = await processor.RunCycleAsync();
        var loserRepairCycle = await processor.RunCycleAsync();
        var loserFallbackCycle = await processor.RunCycleAsync();

        Assert.False(winnerFirstPageCycle.ReachedEnd);
        Assert.False(winnerLastPageCycle.ReachedEnd);
        Assert.NotNull(winnerRepairCycle.AuthoritativeNetRepairResult);
        Assert.NotNull(winnerFallbackCycle.NetFallbackResult);
        Assert.False(loserExactCycle.ReachedEnd);
        Assert.NotNull(loserRepairCycle.AuthoritativeNetRepairResult);
        Assert.True(loserFallbackCycle.ReachedEnd);
        Assert.Equal(
            [winnerStrategyId, winnerStrategyId, loserStrategyId],
            feeService.Calls.Select(call => call.Order.StrategyId).ToArray());
        Assert.Equal(
            [winnerStrategyId, winnerStrategyId, loserStrategyId],
            repository.HistoricalPaperFakFeeBackfillCalls
                .Select(call => call.StrategyId)
                .ToArray());
        Assert.Null(repository.HistoricalPaperFakFeeBackfillCalls[0].AfterCursor);
        Assert.Equal(winnerCursor, repository.HistoricalPaperFakFeeBackfillCalls[1].AfterCursor);
        Assert.Null(repository.HistoricalPaperFakFeeBackfillCalls[2].AfterCursor);
        Assert.Equal(
            [winnerStrategyId, loserStrategyId],
            repository.HistoricalPaperAuthoritativeNetRepairCalls
                .Select(call => call.StrategyId)
                .ToArray());
        Assert.Equal(
            [winnerStrategyId, loserStrategyId],
            repository.HistoricalPaperNetFallbackCalls
                .Select(call => call.StrategyId)
                .ToArray());
        Assert.Single(repository.HistoricalPaperFakFeeBackfillStrategyRankCalls);
    }

    [Fact]
    public async Task RunCycle_ContinuesPastTransientWinnerAndRetriesItOnNextRankedSweep()
    {
        var winnerStrategyId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var loserStrategyId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var winner = CreateCandidate("condition-winner", winnerStrategyId);
        var loser = CreateCandidate("condition-loser", loserStrategyId);
        var repository = new TestAppRepository();
        repository.HistoricalPaperFakFeeBackfillStrategyRanks.AddRange(
        [
            new(winnerStrategyId, "winner", 10m),
            new(loserStrategyId, "loser", -10m)
        ]);
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([winner], null, true));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([loser], null, true));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([winner], null, true));
        var feeService = new RecordingFeeAccountingService((order, fill, callNumber) =>
            Task.FromResult(order.StrategyId == winnerStrategyId && callNumber == 1
                ? fill with
                {
                    FeeAccountingStatus = FeeAccountingStatus.CalculationUnavailable.ToString(),
                    FeeCalculationSource =
                        PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource
                }
                : fill with
                {
                    FeeUsd = 0.01m,
                    FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
                    FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource
                }));
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        var winnerTransientCycle = await processor.RunCycleAsync();
        var winnerRepairCycle = await processor.RunCycleAsync();
        var winnerFallbackCycle = await processor.RunCycleAsync();
        var loserExactCycle = await processor.RunCycleAsync();
        var loserRepairCycle = await processor.RunCycleAsync();
        var loserFallbackCycle = await processor.RunCycleAsync();
        var winnerRetryCycle = await processor.RunCycleAsync();

        Assert.Equal(1, winnerTransientCycle.TransientLookupUnavailable);
        Assert.False(winnerTransientCycle.ReachedEnd);
        Assert.NotNull(winnerRepairCycle.AuthoritativeNetRepairResult);
        Assert.NotNull(winnerFallbackCycle.NetFallbackResult);
        Assert.False(loserExactCycle.ReachedEnd);
        Assert.NotNull(loserRepairCycle.AuthoritativeNetRepairResult);
        Assert.True(loserFallbackCycle.ReachedEnd);
        Assert.False(winnerRetryCycle.ReachedEnd);
        Assert.Equal(
            [winnerStrategyId, loserStrategyId, winnerStrategyId],
            feeService.Calls.Select(call => call.Order.StrategyId).ToArray());
        Assert.Equal(
            [winner.Order.Id],
            repository.HistoricalPaperNetFallbackCalls[0].ExcludedPaperOrderIds);
        Assert.Empty(repository.HistoricalPaperNetFallbackCalls[1].ExcludedPaperOrderIds);
        Assert.Equal(2, repository.HistoricalPaperFakFeeBackfillStrategyRankCalls.Count);
        Assert.Equal(2, repository.HistoricalPaperFakFeeBackfillApplyCalls.Count);
    }

    [Fact]
    public async Task RunCycle_ProcessesOneBoundedPagePerPhaseBeforeCompletingStrategy()
    {
        var strategyId = Guid.Parse("60000000-0000-0000-0000-000000000001");
        var repairCursor = new HistoricalPaperNetRunCursor(
            strategyId,
            Guid.Parse("60000000-0000-0000-0000-000000000002"));
        var fallbackCursor = new HistoricalPaperNetRunCursor(
            strategyId,
            Guid.Parse("60000000-0000-0000-0000-000000000003"));
        var repository = new TestAppRepository();
        repository.HistoricalPaperFakFeeBackfillStrategyRanks.Add(
            new(strategyId, "strategy", 1m));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([], null, true));
        repository.HistoricalPaperAuthoritativeNetRepairResults.Enqueue(
            new(Candidates: 50, ReachedEnd: false, ContinuationCursor: repairCursor));
        repository.HistoricalPaperAuthoritativeNetRepairResults.Enqueue(
            new(Candidates: 1, RunsUpdated: 1));
        repository.HistoricalPaperNetFallbackResults.Enqueue(
            new(
                Candidates: 50,
                ExactDonorCount: 2,
                ExactDonorFeeUsd: 3m,
                ExactDonorStakeUsd: 150m,
                FeeToStakeRatio: 0.02m,
                RunsUpdated: 50,
                DonorAvailable: true,
                ReachedEnd: false,
                ContinuationCursor: fallbackCursor));
        repository.HistoricalPaperNetFallbackResults.Enqueue(
            new(
                Candidates: 1,
                ExactDonorCount: 2,
                ExactDonorFeeUsd: 3m,
                ExactDonorStakeUsd: 150m,
                FeeToStakeRatio: 0.02m,
                RunsUpdated: 1,
                DonorAvailable: true));
        var feeService = new RecordingFeeAccountingService((_, _, _) =>
            throw new InvalidOperationException("The exact page is empty."));
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        var exactCycle = await processor.RunCycleAsync();
        var firstRepairCycle = await processor.RunCycleAsync();
        var lastRepairCycle = await processor.RunCycleAsync();
        var firstFallbackCycle = await processor.RunCycleAsync();
        var lastFallbackCycle = await processor.RunCycleAsync();

        Assert.False(exactCycle.ReachedEnd);
        Assert.False(firstRepairCycle.ReachedEnd);
        Assert.False(lastRepairCycle.ReachedEnd);
        Assert.False(firstFallbackCycle.ReachedEnd);
        Assert.True(lastFallbackCycle.ReachedEnd);
        Assert.Equal(50, firstRepairCycle.AuthoritativeNetRepairResult?.Candidates);
        Assert.Equal(1, lastRepairCycle.AuthoritativeNetRepairResult?.RunsUpdated);
        Assert.Equal(50, firstFallbackCycle.NetFallbackResult?.RunsUpdated);
        Assert.Equal(1, lastFallbackCycle.NetFallbackResult?.RunsUpdated);
        Assert.Equal(
            new HistoricalPaperNetRunCursor?[] { null, repairCursor },
            repository.HistoricalPaperAuthoritativeNetRepairCalls
                .Select(call => call.AfterCursor)
                .ToArray());
        Assert.Equal(
            new HistoricalPaperNetRunCursor?[] { null, fallbackCursor },
            repository.HistoricalPaperNetFallbackCalls
                .Select(call => call.AfterCursor)
                .ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunCycle_FallbackDeferralRetriesSamePageWithoutAdvancingCursor(
        bool queryCancelled)
    {
        var strategyId = Guid.Parse("61000000-0000-0000-0000-000000000001");
        var cursor = new HistoricalPaperNetRunCursor(
            strategyId,
            Guid.Parse("61000000-0000-0000-0000-000000000002"));
        var repository = new TestAppRepository();
        repository.HistoricalPaperFakFeeBackfillStrategyRanks.Add(
            new(strategyId, "strategy", 1m));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([], null, true));
        repository.HistoricalPaperNetFallbackResults.Enqueue(
            queryCancelled
                ? new(
                    Candidates: 1,
                    ReachedEnd: false,
                    ContinuationCursor: cursor,
                    DeferredByQueryCancel: 1)
                : new(
                    Candidates: 1,
                    ReachedEnd: false,
                    ContinuationCursor: cursor,
                    DeferredByLockTimeout: 1));
        repository.HistoricalPaperNetFallbackResults.Enqueue(
            new(
                Candidates: 1,
                DonorAvailable: true,
                ReachedEnd: false,
                ContinuationCursor: cursor));
        repository.HistoricalPaperNetFallbackResults.Enqueue(
            new(Candidates: 0, DonorAvailable: true));
        var feeService = new RecordingFeeAccountingService((_, _, _) =>
            throw new InvalidOperationException("The exact page is empty."));
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        await processor.RunCycleAsync();
        await processor.RunCycleAsync();
        var deferredCycle = await processor.RunCycleAsync();
        var retryCycle = await processor.RunCycleAsync();
        var sweepEndCycle = await processor.RunCycleAsync();

        Assert.True(deferredCycle.NetFallbackResult?.WholeBatchDeferred);
        Assert.False(deferredCycle.ReachedEnd);
        Assert.False(retryCycle.NetFallbackResult?.WholeBatchDeferred);
        Assert.False(retryCycle.ReachedEnd);
        Assert.True(sweepEndCycle.ReachedEnd);
        Assert.Equal(
            new HistoricalPaperNetRunCursor?[] { null, null, cursor },
            repository.HistoricalPaperNetFallbackCalls
                .Select(call => call.AfterCursor)
                .ToArray());
    }

    [Fact]
    public async Task RunCycle_NoDonorFallbackCompletesStrategyVisitWithoutUpdate()
    {
        var strategyId = Guid.Parse("62000000-0000-0000-0000-000000000001");
        var repository = new TestAppRepository();
        repository.HistoricalPaperFakFeeBackfillStrategyRanks.Add(
            new(strategyId, "strategy", 1m));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([], null, true));
        repository.HistoricalPaperNetFallbackResults.Enqueue(
            new(Candidates: 5, DonorAvailable: false));
        var feeService = new RecordingFeeAccountingService((_, _, _) =>
            throw new InvalidOperationException("The exact page is empty."));
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        await processor.RunCycleAsync();
        await processor.RunCycleAsync();
        var fallbackCycle = await processor.RunCycleAsync();

        Assert.True(fallbackCycle.ReachedEnd);
        Assert.False(fallbackCycle.NetFallbackResult?.DonorAvailable);
        Assert.Equal(0, fallbackCycle.NetFallbackResult?.RunsUpdated);
        Assert.Single(repository.HistoricalPaperNetFallbackCalls);
    }

    [Fact]
    public async Task RunCycle_ExcludesTransientExactOrdersFromFallbackForCurrentStrategyVisit()
    {
        var strategyId = Guid.Parse("62500000-0000-0000-0000-000000000001");
        var transientFirst = CreateCandidate("condition-transient-first", strategyId);
        var transientSecond = CreateCandidate("condition-transient-second", strategyId);
        var otherUnresolved = CreateCandidate("condition-other-unresolved", strategyId);
        var exactCursor = new HistoricalPaperFakFeeBackfillCursor(
            strategyId,
            transientFirst.Fill.FilledAtUtc,
            transientFirst.Order.Id,
            transientFirst.Fill.Id);
        var repository = new TestAppRepository();
        repository.HistoricalPaperFakFeeBackfillStrategyRanks.Add(
            new(strategyId, "strategy", 1m));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([transientFirst], exactCursor, false));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([otherUnresolved, transientSecond], null, true));
        repository.HistoricalPaperNetFallbackResults.Enqueue(
            new(Candidates: 1, RunsUpdated: 1, DonorAvailable: true));
        var feeService = new RecordingFeeAccountingService((order, fill, _) =>
            Task.FromResult(
                order.Id == otherUnresolved.Order.Id
                    ? fill with
                    {
                        FeeAccountingStatus = FeeAccountingStatus.CalculationUnavailable.ToString(),
                        FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource
                    }
                    : fill with
                    {
                        FeeAccountingStatus = FeeAccountingStatus.CalculationUnavailable.ToString(),
                        FeeCalculationSource =
                            PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource
                    }));
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        var firstExactCycle = await processor.RunCycleAsync();
        var lastExactCycle = await processor.RunCycleAsync();
        await processor.RunCycleAsync();
        var fallbackCycle = await processor.RunCycleAsync();

        Assert.Equal(1, firstExactCycle.TransientLookupUnavailable);
        Assert.Equal(1, lastExactCycle.TransientLookupUnavailable);
        Assert.True(fallbackCycle.ReachedEnd);
        Assert.Equal(1, fallbackCycle.NetFallbackResult?.RunsUpdated);
        Assert.Equal(
            new[] { transientFirst.Order.Id, transientSecond.Order.Id }.Order().ToArray(),
            Assert.Single(repository.HistoricalPaperNetFallbackCalls)
                .ExcludedPaperOrderIds
                .Order()
                .ToArray());
        Assert.Contains(
            Assert.Single(repository.HistoricalPaperFakFeeBackfillApplyCalls),
            update => update.Expected.Order.Id == otherUnresolved.Order.Id);
    }

    [Fact]
    public async Task RunCycle_MissingConditionIsFinancialGapAndRemainsFallbackEligible()
    {
        var strategyId = Guid.Parse("62700000-0000-0000-0000-000000000001");
        var missingCondition = CreateCandidate(string.Empty, strategyId);
        var repository = new TestAppRepository();
        repository.HistoricalPaperFakFeeBackfillStrategyRanks.Add(
            new(strategyId, "strategy", 1m));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([missingCondition], null, true));
        repository.HistoricalPaperNetFallbackResults.Enqueue(
            new(Candidates: 1, RunsUpdated: 1, DonorAvailable: true));
        var feeService = new RecordingFeeAccountingService((_, fill, _) =>
            Task.FromResult(fill with
            {
                FeeAccountingStatus = FeeAccountingStatus.CalculationUnavailable.ToString(),
                FeeCalculationSource =
                    PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource
            }));
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        var exactCycle = await processor.RunCycleAsync();
        await processor.RunCycleAsync();
        var fallbackCycle = await processor.RunCycleAsync();

        Assert.Equal(1, exactCycle.TransientLookupUnavailable);
        Assert.True(fallbackCycle.ReachedEnd);
        Assert.Equal(1, fallbackCycle.NetFallbackResult?.RunsUpdated);
        Assert.Empty(Assert.Single(repository.HistoricalPaperNetFallbackCalls).ExcludedPaperOrderIds);
        Assert.Empty(repository.HistoricalPaperFakFeeBackfillApplyCalls);
    }

    [Fact]
    public async Task RunCycle_PreviewAdvancesThroughNetPhasesWithoutEnablingApply()
    {
        var strategyId = Guid.Parse("63000000-0000-0000-0000-000000000001");
        var repository = new TestAppRepository();
        repository.HistoricalPaperFakFeeBackfillStrategyRanks.Add(
            new(strategyId, "strategy", 1m));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([], null, true));
        repository.HistoricalPaperAuthoritativeNetRepairResults.Enqueue(
            new(Candidates: 1));
        repository.HistoricalPaperNetFallbackResults.Enqueue(
            new(Candidates: 1, DonorAvailable: true));
        var feeService = new RecordingFeeAccountingService((_, _, _) =>
            throw new InvalidOperationException("The exact page is empty."));
        var processor = CreateProcessor(repository, feeService, applyEnabled: false);

        var exactCycle = await processor.RunCycleAsync();
        var repairCycle = await processor.RunCycleAsync();
        var fallbackCycle = await processor.RunCycleAsync();

        Assert.False(exactCycle.ReachedEnd);
        Assert.False(repairCycle.ReachedEnd);
        Assert.True(fallbackCycle.ReachedEnd);
        Assert.False(Assert.Single(repository.HistoricalPaperAuthoritativeNetRepairCalls).ApplyEnabled);
        Assert.False(Assert.Single(repository.HistoricalPaperNetFallbackCalls).ApplyEnabled);
        Assert.Empty(repository.HistoricalPaperFakFeeBackfillApplyCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunCycle_OperationalFeeEvaluationFailureDoesNotEstimateAndRetriesExactPage(
        bool cancellationFailure)
    {
        var strategyId = Guid.Parse("63500000-0000-0000-0000-000000000001");
        var candidate = CreateCandidate("condition-operational-failure", strategyId);
        var repository = new TestAppRepository();
        repository.HistoricalPaperFakFeeBackfillStrategyRanks.Add(
            new(strategyId, "strategy", 1m));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([candidate], null, true));
        repository.HistoricalPaperFakFeeBackfillPages.Enqueue(
            new HistoricalPaperFakFeeBackfillPage([candidate], null, true));
        var feeService = new RecordingFeeAccountingService((_, fill, call) =>
        {
            if (call == 1)
            {
                if (cancellationFailure)
                {
                    throw new OperationCanceledException("Injected operational cancellation.");
                }

                throw new InvalidOperationException("Injected operational fee failure.");
            }

            return Task.FromResult(fill with
            {
                FeeUsd = 0.1m,
                FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
                FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
                NetRealizedPnlUsd = fill.RealizedPnlUsd - 0.1m
            });
        });
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        if (cancellationFailure)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.RunCycleAsync());
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => processor.RunCycleAsync());
        }

        Assert.Empty(repository.HistoricalPaperFakFeeBackfillApplyCalls);
        Assert.Empty(repository.HistoricalPaperAuthoritativeNetRepairCalls);
        Assert.Empty(repository.HistoricalPaperNetFallbackCalls);

        var retry = await processor.RunCycleAsync();

        Assert.Equal(1, retry.EvaluatedForApply);
        Assert.Equal(2, feeService.Calls.Count);
        Assert.All(feeService.Calls, call => Assert.Equal(candidate.Order.Id, call.Order.Id));
        Assert.Equal(
            new HistoricalPaperFakFeeBackfillCursor?[] { null, null },
            repository.HistoricalPaperFakFeeBackfillCalls
                .Select(call => call.AfterCursor)
                .ToArray());
        Assert.Single(repository.HistoricalPaperFakFeeBackfillApplyCalls);
        Assert.Empty(repository.HistoricalPaperAuthoritativeNetRepairCalls);
        Assert.Empty(repository.HistoricalPaperNetFallbackCalls);
    }

    [Fact]
    public async Task HistoricalParityProcessor_ReachesExactBoundaryBeforeLazyFixedFallback()
    {
        var target = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("71000000-0000-0000-0000-000000000001"),
            Guid.Parse("71000000-0000-0000-0000-000000000002"),
            HistoricalGrossNetParityExactEligibility.FallbackRequired,
            gross: 10m,
            basis: 12.34m,
            fee: 0m,
            net: null);
        var store = new RecordingParityStore
        {
            CandidatePageFactory = _ => CreateParityPage(target)
        };
        var feeService = new RecordingHistoricalFeeService((_, _) =>
            throw new InvalidOperationException("Donor fallback must not call the external fee service."));
        var events = new RecordingEventRecorder();
        var processor = CreateHistoricalParityProcessor(store, feeService, events);
        var exactCycle = Guid.Parse("71000000-0000-0000-0000-000000000003");
        var fallbackCycle = Guid.Parse("71000000-0000-0000-0000-000000000004");

        var exact = await processor.RunCycleAsync(exactCycle);

        Assert.Equal(HistoricalGrossNetParityCycleState.ExactBoundaryReached, exact.State);
        Assert.Equal(HistoricalGrossNetParityProcessingPhase.Exact, exact.Phase);
        Assert.Equal(1, exact.FallbackEligible);
        Assert.Empty(store.DonorPreviewRequests);
        Assert.Empty(store.LiveAccountingRequests);
        Assert.Empty(events.Events);

        var fallback = await processor.RunCycleAsync(fallbackCycle);

        Assert.Equal(HistoricalGrossNetParityCycleState.StrategyCompleted, fallback.State);
        Assert.Equal(HistoricalGrossNetParityProcessingPhase.Fallback, fallback.Phase);
        Assert.Single(store.DonorPreviewRequests);
        var request = Assert.Single(store.LiveAccountingRequests);
        Assert.Equal(HistoricalGrossNetParityDecisionKind.Fixed0p033, request.Decision.DecisionKind);
        Assert.Equal(0.40722000m, request.Decision.ContributionEffectiveFeeUsd);
        Assert.Equal(9.59278000m, request.Decision.NetPnlUsd);
        Assert.Equal(
            "historical-gross-net-parity-fixed-net-roi-minus-3p3-v1",
            request.Decision.FeeCalculationSource);
        Assert.Contains(
            "tier:fixed-net-roi-minus-3.3-points",
            request.Decision.EvidenceJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "fixed-0.0333",
            request.Decision.EvidenceJson,
            StringComparison.Ordinal);
        Assert.Null(request.Decision.DonorDecision?.SelectedTier);
        Assert.Equal(FeeLiquidityRole.Unknown.ToString(), request.Decision.FeeLiquidityRole);
        Assert.Empty(feeService.Requests);
        Assert.Equal(
            [PaperFakFeeBackfillEventTypes.ParityTargetCommitted,
             PaperFakFeeBackfillEventTypes.ParityPageCompleted],
            events.Events.Select(entry => entry.EventType).ToArray());
        Assert.All(events.Events, entry => Assert.Equal(fallbackCycle, entry.CycleId));
    }

    [Fact]
    public async Task HistoricalParityProcessor_CompletesSelectedStrategyBeforeCurrentGrossReselection()
    {
        var highStrategyId = Guid.Parse("71100000-0000-0000-0000-000000000001");
        var lowStrategyId = Guid.Parse("71100000-0000-0000-0000-000000000002");
        var high = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("71100000-0000-0000-0000-000000000011"),
            highStrategyId,
            HistoricalGrossNetParityExactEligibility.FallbackRequired,
            gross: 20m,
            basis: 100m,
            fee: 0m,
            net: null);
        var low = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("71100000-0000-0000-0000-000000000012"),
            lowStrategyId,
            HistoricalGrossNetParityExactEligibility.FallbackRequired,
            gross: 10m,
            basis: 100m,
            fee: 0m,
            net: null) with { StrategyRank = 2 };
        var completed = new HashSet<Guid>();
        var ranksFlipped = false;
        var store = new RecordingParityStore
        {
            CandidatePageFactory = request =>
            {
                var candidates = new[]
                    {
                        ranksFlipped ? high with { StrategyRank = 2 } : high,
                        ranksFlipped ? low with { StrategyRank = 1 } : low
                    }
                    .Where(target => !completed.Contains(target.SourceId))
                    .Where(target => request.StrategyId is null || target.StrategyId == request.StrategyId)
                    .OrderBy(target => target.StrategyRank)
                    .ToArray();
                return CreateParityPage(request.Phase, candidates, []);
            },
            LiveApplyFactory = request =>
            {
                completed.Add(request.Target.SourceId);
                return new HistoricalGrossNetParityApplyResult(
                    HistoricalGrossNetParityApplyStatus.Applied,
                    true,
                    request.Target.TargetTupleHash,
                    HistoricalGrossNetParityOwnership.Completed);
            }
        };
        var processor = CreateHistoricalParityProcessor(
            store,
            new RecordingHistoricalFeeService((_, _) =>
                throw new InvalidOperationException("Fallback must not dispatch a historical lookup.")));

        var highExact = await processor.RunCycleAsync(Guid.NewGuid());
        ranksFlipped = true;
        var highFallback = await processor.RunCycleAsync(Guid.NewGuid());
        var lowExact = await processor.RunCycleAsync(Guid.NewGuid());
        var lowFallback = await processor.RunCycleAsync(Guid.NewGuid());

        Assert.Equal(HistoricalGrossNetParityCycleState.ExactBoundaryReached, highExact.State);
        Assert.Equal(HistoricalGrossNetParityCycleState.StrategyCompleted, highFallback.State);
        Assert.Equal(HistoricalGrossNetParityCycleState.ExactBoundaryReached, lowExact.State);
        Assert.Equal(HistoricalGrossNetParityCycleState.StrategyCompleted, lowFallback.State);
        Assert.Equal([highStrategyId, lowStrategyId],
            store.LiveAccountingRequests.Select(request => request.Target.StrategyId).ToArray());
        Assert.Equal(
            [null, highStrategyId, highStrategyId, null, lowStrategyId],
            store.CandidatePageRequests.Select(request => request.StrategyId).ToArray());
    }

    [Fact]
    public async Task HistoricalParityProcessor_DeferredTargetKeepsLowerGrossStrategyBlocked()
    {
        var highStrategyId = Guid.Parse("71200000-0000-0000-0000-000000000001");
        var lowStrategyId = Guid.Parse("71200000-0000-0000-0000-000000000002");
        var high = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("71200000-0000-0000-0000-000000000011"),
            highStrategyId,
            HistoricalGrossNetParityExactEligibility.FallbackRequired,
            gross: 20m,
            basis: 100m,
            fee: 0m,
            net: null);
        var low = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("71200000-0000-0000-0000-000000000012"),
            lowStrategyId,
            HistoricalGrossNetParityExactEligibility.FallbackRequired,
            gross: 10m,
            basis: 100m,
            fee: 0m,
            net: null) with { StrategyRank = 2 };
        var completed = new HashSet<Guid>();
        var applyAttempts = 0;
        var store = new RecordingParityStore
        {
            CandidatePageFactory = request => CreateParityPage(
                request.Phase,
                new[] { high, low }
                    .Where(target => !completed.Contains(target.SourceId))
                    .Where(target => request.StrategyId is null || target.StrategyId == request.StrategyId)
                    .OrderBy(target => target.StrategyRank)
                    .ToArray(),
                []),
            LiveApplyFactory = request =>
            {
                applyAttempts++;
                if (applyAttempts == 1)
                {
                    return new HistoricalGrossNetParityApplyResult(
                        HistoricalGrossNetParityApplyStatus.DeferredCas,
                        false,
                        request.Target.TargetTupleHash,
                        HistoricalGrossNetParityOwnership.None);
                }

                completed.Add(request.Target.SourceId);
                return new HistoricalGrossNetParityApplyResult(
                    HistoricalGrossNetParityApplyStatus.Applied,
                    true,
                    request.Target.TargetTupleHash,
                    HistoricalGrossNetParityOwnership.Completed);
            }
        };
        var processor = CreateHistoricalParityProcessor(
            store,
            new RecordingHistoricalFeeService((_, _) =>
                throw new InvalidOperationException("Fallback must not dispatch a historical lookup.")));

        Assert.Equal(HistoricalGrossNetParityCycleState.ExactBoundaryReached,
            (await processor.RunCycleAsync(Guid.NewGuid())).State);
        Assert.Equal(HistoricalGrossNetParityCycleState.Deferred,
            (await processor.RunCycleAsync(Guid.NewGuid())).State);
        Assert.Equal(HistoricalGrossNetParityCycleState.ExactBoundaryReached,
            (await processor.RunCycleAsync(Guid.NewGuid())).State);
        Assert.Equal(HistoricalGrossNetParityCycleState.StrategyCompleted,
            (await processor.RunCycleAsync(Guid.NewGuid())).State);

        Assert.Equal(2, store.LiveAccountingRequests.Count);
        Assert.All(store.LiveAccountingRequests,
            request => Assert.Equal(highStrategyId, request.Target.StrategyId));
        Assert.DoesNotContain(store.CandidatePageRequests,
            request => request.StrategyId == lowStrategyId);

        Assert.Equal(HistoricalGrossNetParityCycleState.ExactBoundaryReached,
            (await processor.RunCycleAsync(Guid.NewGuid())).State);
        Assert.Equal(HistoricalGrossNetParityCycleState.StrategyCompleted,
            (await processor.RunCycleAsync(Guid.NewGuid())).State);
        Assert.Equal(lowStrategyId, store.LiveAccountingRequests[^1].Target.StrategyId);
    }

    [Fact]
    public async Task HistoricalParityProcessor_DeferredLiveBalanceKeepsLowerGrossStrategyBlocked()
    {
        var highStrategyId = Guid.Parse("71300000-0000-0000-0000-000000000001");
        var lowStrategyId = Guid.Parse("71300000-0000-0000-0000-000000000002");
        var high = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("71300000-0000-0000-0000-000000000011"),
            highStrategyId,
            HistoricalGrossNetParityExactEligibility.FallbackRequired,
            gross: 20m,
            basis: 100m,
            fee: 0m,
            net: null) with { StrategyGrossPnlUsd = 20m };
        var low = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("71300000-0000-0000-0000-000000000012"),
            lowStrategyId,
            HistoricalGrossNetParityExactEligibility.FallbackRequired,
            gross: 10m,
            basis: 100m,
            fee: 0m,
            net: null) with { StrategyRank = 2, StrategyGrossPnlUsd = 10m };
        var completed = new HashSet<Guid>();
        var highAccountingApplied = false;
        var balanceAttempts = 0;
        var store = new RecordingParityStore
        {
            CandidatePageFactory = request => CreateParityPage(
                request.Phase,
                new[] { high, low }
                    .Where(target => !completed.Contains(target.SourceId))
                    .Where(target => request.StrategyId is null || target.StrategyId == request.StrategyId)
                    .OrderBy(target => target.StrategyRank)
                    .ToArray(),
                []),
            LiveApplyFactory = request =>
            {
                if (request.Target.SourceId == low.SourceId)
                {
                    completed.Add(low.SourceId);
                    return new HistoricalGrossNetParityApplyResult(
                        HistoricalGrossNetParityApplyStatus.Applied,
                        true,
                        request.Target.TargetTupleHash,
                        HistoricalGrossNetParityOwnership.Completed);
                }

                var status = highAccountingApplied
                    ? HistoricalGrossNetParityApplyStatus.TerminalNoOp
                    : HistoricalGrossNetParityApplyStatus.Applied;
                highAccountingApplied = true;
                return new HistoricalGrossNetParityApplyResult(
                    status,
                    status == HistoricalGrossNetParityApplyStatus.Applied,
                    request.Target.TargetTupleHash,
                    HistoricalGrossNetParityOwnership.Pending);
            },
            LiveBalanceFactory = request =>
            {
                balanceAttempts++;
                if (balanceAttempts == 1)
                {
                    return new HistoricalGrossNetParityLiveBalanceResult(
                        HistoricalGrossNetParityApplyStatus.DeferredCas,
                        request.LiveOrderId,
                        HistoricalGrossNetParityOwnership.Pending,
                        null,
                        null,
                        null,
                        false,
                        "retry");
                }

                completed.Add(high.SourceId);
                return new HistoricalGrossNetParityLiveBalanceResult(
                    HistoricalGrossNetParityApplyStatus.Applied,
                    request.LiveOrderId,
                    HistoricalGrossNetParityOwnership.Completed,
                    0m,
                    0m,
                    0m,
                    true);
            }
        };
        var processor = CreateHistoricalParityProcessor(
            store,
            new RecordingHistoricalFeeService((_, _) =>
                throw new InvalidOperationException("Fallback must not dispatch a historical lookup.")));

        Assert.Equal(HistoricalGrossNetParityCycleState.ExactBoundaryReached,
            (await processor.RunCycleAsync(Guid.NewGuid())).State);
        Assert.Equal(HistoricalGrossNetParityCycleState.Deferred,
            (await processor.RunCycleAsync(Guid.NewGuid())).State);
        Assert.Equal(HistoricalGrossNetParityCycleState.ExactBoundaryReached,
            (await processor.RunCycleAsync(Guid.NewGuid())).State);
        Assert.Equal(HistoricalGrossNetParityCycleState.StrategyCompleted,
            (await processor.RunCycleAsync(Guid.NewGuid())).State);

        Assert.Equal(2, store.LiveBalanceRequests.Count);
        Assert.All(store.LiveBalanceRequests,
            request => Assert.Equal(highStrategyId, request.StrategyId));
        Assert.DoesNotContain(store.LiveAccountingRequests,
            request => request.Target.StrategyId == lowStrategyId);

        Assert.Equal(HistoricalGrossNetParityCycleState.ExactBoundaryReached,
            (await processor.RunCycleAsync(Guid.NewGuid())).State);
        Assert.Equal(HistoricalGrossNetParityCycleState.StrategyCompleted,
            (await processor.RunCycleAsync(Guid.NewGuid())).State);
        Assert.Equal(lowStrategyId, store.LiveAccountingRequests[^1].Target.StrategyId);
    }

    [Fact]
    public async Task HistoricalParityProcessor_UsesThreeDistinctExactCyclesAndNoFallbackLookup()
    {
        var target = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("72000000-0000-0000-0000-000000000001"),
            Guid.Parse("72000000-0000-0000-0000-000000000002"),
            HistoricalGrossNetParityExactEligibility.LocalLookupRequired,
            gross: 5m,
            basis: 100m,
            fee: 0m,
            net: null);
        var lookup = CreateParityLookup(target, FeeLiquidityRole.Taker.ToString());
        var store = new RecordingParityStore
        {
            CandidatePageFactory = _ => CreateParityPage(target, [lookup])
        };
        var feeService = new RecordingHistoricalFeeService((_, _) => OperationalLookupFailure());
        var processor = CreateHistoricalParityProcessor(store, feeService);
        var cycleA = Guid.Parse("72000000-0000-0000-0000-00000000000a");
        var cycleB = Guid.Parse("72000000-0000-0000-0000-00000000000b");
        var cycleC = Guid.Parse("72000000-0000-0000-0000-00000000000c");

        var first = await processor.RunCycleAsync(cycleA);
        await processor.RunCycleAsync(Guid.NewGuid()); // fallback pass; tuple only, no lookup
        var duplicateCycle = await processor.RunCycleAsync(cycleA);
        await processor.RunCycleAsync(Guid.NewGuid());
        var second = await processor.RunCycleAsync(cycleB);
        await processor.RunCycleAsync(Guid.NewGuid());
        var third = await processor.RunCycleAsync(cycleC);

        Assert.Equal(1, first.LookupAttemptsThisCycle);
        Assert.Equal(0, duplicateCycle.LookupAttemptsThisCycle);
        Assert.Equal(1, second.LookupAttemptsThisCycle);
        Assert.Equal(1, third.LookupAttemptsThisCycle);
        Assert.Equal(1, third.FallbackEligible);
        Assert.Equal(3, feeService.Requests.Count);
        Assert.Empty(store.LiveAccountingRequests);
        Assert.Empty(store.DonorPreviewRequests);

        var fallback = await processor.RunCycleAsync(Guid.NewGuid());

        Assert.Equal(HistoricalGrossNetParityCycleState.StrategyCompleted, fallback.State);
        Assert.Equal(3, feeService.Requests.Count);
        Assert.Single(store.DonorPreviewRequests);
        Assert.Single(store.LiveAccountingRequests);
    }

    [Fact]
    public async Task HistoricalParityProcessor_PartialLookupFallbackRecordsComponentButUsesExact3p3Points()
    {
        var target = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("72500000-0000-0000-0000-000000000001"),
            Guid.Parse("72500000-0000-0000-0000-000000000002"),
            HistoricalGrossNetParityExactEligibility.LocalLookupRequired,
            gross: 2m,
            basis: 10m,
            fee: 0m,
            net: null);
        var first = CreateParityLookup(target, FeeLiquidityRole.Taker.ToString()) with
        {
            FeeApplicationKind =
                HistoricalGrossNetParityLookupFeeApplicationKind.AdditionalNonoverlappingComponent
        };
        var second = first with
        {
            TupleHash = target.SourceId.ToString("N") + "lookup-2",
            FeeAllocationId = $"lookup-allocation:{target.SourceId:D}:2",
            FeeSourceChargeId = $"canonical-local:{target.SourceKind}:{target.SourceId:D}:2"
        };
        var store = new RecordingParityStore
        {
            CandidatePageFactory = _ => CreateParityPage(target, [first, second])
        };
        var feeService = new RecordingHistoricalFeeService((_, call) => call == 1
            ? new HistoricalFeeLookupResult(
                HistoricalFeeLookupDisposition.Calculated,
                0.75m,
                FeeAccountingStatus.Calculated.ToString(),
                FeeLiquidityRole.Taker.ToString(),
                PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
                0.01m,
                2,
                true,
                HistoricalGrossNetParityConstants.CutoffUtc,
                200,
                "exact-component")
            : new HistoricalFeeLookupResult(
                HistoricalFeeLookupDisposition.SemanticUnavailable,
                null,
                FeeAccountingStatus.CalculationUnavailable.ToString(),
                FeeLiquidityRole.Unknown.ToString(),
                PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource,
                null,
                null,
                null,
                HistoricalGrossNetParityConstants.CutoffUtc,
                404,
                "missing-component"));
        var processor = CreateHistoricalParityProcessor(store, feeService);

        var exact = await processor.RunCycleAsync(Guid.NewGuid());
        var fallback = await processor.RunCycleAsync(Guid.NewGuid());

        Assert.Equal(1, exact.FallbackEligible);
        Assert.Equal(HistoricalGrossNetParityCycleState.StrategyCompleted, fallback.State);
        var request = Assert.Single(store.LiveAccountingRequests);
        Assert.Equal(HistoricalGrossNetParityDecisionKind.Fixed0p033, request.Decision.DecisionKind);
        Assert.Equal(0.75m, request.Decision.ComponentFloorUsd);
        Assert.Equal(0.33m, request.Decision.ContributionEffectiveFeeUsd);
        Assert.Equal(1.67m, request.Decision.NetPnlUsd);
        Assert.Single(request.Target.ProvedComponents);
        Assert.Equal(2, feeService.Requests.Count);
    }

    [Fact]
    public async Task HistoricalParityProcessor_RestartResetsOperationalLookupLedger()
    {
        var target = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("73000000-0000-0000-0000-000000000001"),
            Guid.Parse("73000000-0000-0000-0000-000000000002"),
            HistoricalGrossNetParityExactEligibility.LocalLookupRequired,
            1m,
            10m,
            0m,
            null);
        var lookup = CreateParityLookup(target, FeeLiquidityRole.Taker.ToString());
        var store = new RecordingParityStore
        {
            CandidatePageFactory = _ => CreateParityPage(target, [lookup])
        };
        var feeService = new RecordingHistoricalFeeService((_, _) => OperationalLookupFailure());

        var beforeRestart = CreateHistoricalParityProcessor(store, feeService);
        var afterRestart = CreateHistoricalParityProcessor(store, feeService);

        var first = await beforeRestart.RunCycleAsync(Guid.NewGuid());
        var restarted = await afterRestart.RunCycleAsync(Guid.NewGuid());

        Assert.Equal(1, first.LookupAttemptsThisCycle);
        Assert.Equal(1, restarted.LookupAttemptsThisCycle);
        Assert.Equal(2, feeService.Requests.Count);
        Assert.Empty(store.LiveAccountingRequests);
    }

    [Fact]
    public async Task HistoricalParityProcessor_InvalidRawRoleIsTerminalSemanticFallbackWithoutAttemptExhaustion()
    {
        var target = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("73500000-0000-0000-0000-000000000001"),
            Guid.Parse("73500000-0000-0000-0000-000000000002"),
            HistoricalGrossNetParityExactEligibility.LocalLookupRequired,
            1m,
            10m,
            0m,
            null);
        var lookup = CreateParityLookup(target, "not-a-role");
        var store = new RecordingParityStore
        {
            CandidatePageFactory = _ => CreateParityPage(target, [lookup])
        };
        var feeService = new RecordingHistoricalFeeService((request, _) =>
        {
            Assert.False(request.LiquidityRoleIsValid);
            return new HistoricalFeeLookupResult(
                HistoricalFeeLookupDisposition.SemanticUnavailable,
                null,
                FeeAccountingStatus.CalculationUnavailable.ToString(),
                FeeLiquidityRole.Unknown.ToString(),
                "invalid-liquidity-role-v1",
                null,
                null,
                null,
                HistoricalGrossNetParityConstants.CutoffUtc,
                null,
                "invalid-liquidity-role");
        });
        var processor = CreateHistoricalParityProcessor(store, feeService);

        var exact = await processor.RunCycleAsync(Guid.NewGuid());
        var fallback = await processor.RunCycleAsync(Guid.NewGuid());

        Assert.Equal(HistoricalGrossNetParityCycleState.ExactBoundaryReached, exact.State);
        Assert.Equal(1, exact.FallbackEligible);
        Assert.Equal(0, exact.LookupAttemptsThisCycle);
        Assert.Equal(HistoricalGrossNetParityCycleState.StrategyCompleted, fallback.State);
        Assert.Single(feeService.Requests);
        Assert.Single(store.LiveAccountingRequests);
    }

    [Fact]
    public async Task HistoricalParityProcessor_ZeroCandidateSweepWritesNothing()
    {
        var store = new RecordingParityStore
        {
            CandidatePageFactory = request => CreateParityPage(
                phase: request.Phase,
                targets: [],
                lookups: [])
        };
        var feeService = new RecordingHistoricalFeeService((_, _) =>
            throw new InvalidOperationException("An empty sweep cannot perform fee lookup."));
        var events = new RecordingEventRecorder();
        var processor = CreateHistoricalParityProcessor(store, feeService, events);

        var exact = await processor.RunCycleAsync(Guid.NewGuid());
        var fallback = await processor.RunCycleAsync(Guid.NewGuid());

        Assert.Equal(HistoricalGrossNetParityCycleState.Idle, exact.State);
        Assert.Equal(HistoricalGrossNetParityCycleState.Idle, fallback.State);
        Assert.Empty(store.PaperDecisionRequests);
        Assert.Empty(store.LiveAccountingRequests);
        Assert.Empty(store.DonorPreviewRequests);
        Assert.Empty(events.Events);
    }

    [Fact]
    public async Task HistoricalParityProcessor_TargetConflictDoesNotBlockIndependentExactTarget()
    {
        var strategyId = Guid.Parse("74000000-0000-0000-0000-000000000001");
        var blocked = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("74000000-0000-0000-0000-000000000002"),
            strategyId,
            HistoricalGrossNetParityExactEligibility.ExistingExactPreserved,
            2m,
            10m,
            1m,
            1m);
        var exact = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("74000000-0000-0000-0000-000000000003"),
            strategyId,
            HistoricalGrossNetParityExactEligibility.ExistingExactPreserved,
            3m,
            10m,
            1m,
            2m);
        var conflict = new HistoricalGrossNetParityTargetConflict(
            "injected-target-conflict",
            blocked.SourceKind,
            blocked.SourceId,
            blocked.StrategyId,
            "test");
        var store = new RecordingParityStore
        {
            CandidatePageFactory = _ => CreateParityPage(
                HistoricalGrossNetParityProcessingPhase.Exact,
                [blocked, exact],
                [],
                [conflict])
        };
        var processor = CreateHistoricalParityProcessor(
            store,
            new RecordingHistoricalFeeService((_, _) => throw new InvalidOperationException()));

        var result = await processor.RunCycleAsync(Guid.NewGuid());

        Assert.Equal(2, result.Candidates);
        Assert.Equal(1, result.Applied);
        Assert.Equal(1, result.Deferred);
        Assert.Equal(exact.SourceId, Assert.Single(store.LiveAccountingRequests).Target.SourceId);
    }

    [Fact]
    public async Task HistoricalParityProcessor_UsesDeduplicatedCountForMatcherAndKeepsAllCountsInAudit()
    {
        var target = CreateParityTarget(
            HistoricalGrossNetParitySourceKind.LiveOrder,
            Guid.Parse("75000000-0000-0000-0000-000000000001"),
            Guid.Parse("75000000-0000-0000-0000-000000000002"),
            HistoricalGrossNetParityExactEligibility.FallbackRequired,
            5m,
            100m,
            0m,
            null);
        var store = new RecordingParityStore
        {
            CandidatePageFactory = _ => CreateParityPage(target),
            DonorPreviewFactory = request => CreateDonorPreview(
                request,
                candidate => candidate.StrategyId == target.StrategyId
                    ? new HistoricalGrossNetParityDonorCandidateAggregate(
                        candidate.StrategyId,
                        candidate.MatcherOrder,
                        candidate.Tier,
                        candidate.DistanceComponents,
                        3,
                        2,
                        1,
                        100m,
                        2m,
                        100m,
                        new string('a', 64))
                    : null)
        };
        var processor = CreateHistoricalParityProcessor(
            store,
            new RecordingHistoricalFeeService((_, _) => throw new InvalidOperationException()));

        await processor.RunCycleAsync(Guid.NewGuid());
        await processor.RunCycleAsync(Guid.NewGuid());

        var decision = Assert.Single(store.LiveAccountingRequests).Decision;
        Assert.Equal(HistoricalGrossNetParityDecisionKind.DonorRatio, decision.DecisionKind);
        Assert.Equal(2m, decision.ContributionEffectiveFeeUsd);
        Assert.Equal(3, decision.DonorDecision?.RawDonorCount);
        Assert.Equal(2, decision.DonorDecision?.ExactDonorCount);
        Assert.Equal(1, decision.DonorDecision?.DeduplicatedDonorCount);
        Assert.Equal(new System.Numerics.BigInteger(0), decision.DonorDecision?.SelectedTier);
    }

    [Fact]
    public void HistoricalParityPaperPrepare_MultiBuyResidualUsesAggregateEvidenceGraph()
    {
        var strategyId = Guid.Parse("76000000-0000-0000-0000-000000000001");
        var buy1 = CreateParityFill(
            Guid.Parse("76000000-0000-0000-0000-000000000002"),
            strategyId,
            TradeSide.Buy,
            CutoffUtc.AddMinutes(-3),
            1m,
            0.5m,
            0.00000001m);
        var buy2 = CreateParityFill(
            Guid.Parse("76000000-0000-0000-0000-000000000003"),
            strategyId,
            TradeSide.Buy,
            CutoffUtc.AddMinutes(-2),
            0.5m,
            0.5m,
            0m);
        var sell = CreateParityFill(
            Guid.Parse("76000000-0000-0000-0000-000000000004"),
            strategyId,
            TradeSide.Sell,
            CutoffUtc.AddMinutes(2),
            0.6m,
            0.50000001m,
            0m,
            realizedPnlUsd: 0.00000001m,
            netRealizedPnlUsd: 0m);
        var page = CreatePaperSellPage(strategyId, sell, [buy1, buy2, sell]);

        var prepared = HistoricalGrossNetParityPaperPreparer.Prepare(
            page,
            HistoricalGrossNetParityConstants.CutoffUtc);

        Assert.Empty(prepared.Conflicts);
        var target = Assert.Single(prepared.Targets);
        Assert.Equal(HistoricalGrossNetParityExactEligibility.ExistingExactPreserved, target.ExactEligibility);
        var entry = Assert.Single(
            target.ProvedComponents,
            component => component.AllocationId == $"paper-entry-allocation:{sell.FillId:D}");
        Assert.Equal(0.00000001m, entry.AmountUsd);
        Assert.Equal(2, entry.SourceCharges?.Count);
        Assert.Equal(2, entry.CoverageEdges?.Count);
        Assert.Equal(0.000000004m, entry.PoolMovement?.RawAllocationUsd);
        Assert.Equal(0.00000001m, entry.PoolMovement?.RemainingAfterUsd);
        Assert.Equal(0m, entry.PoolMovement?.DecrementUsd);
        Assert.Equal(0.00000001m, entry.PoolMovement?.ResidualUsd);
        Assert.Equal(
            target.ComponentHash,
            HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash(target.ProvedComponents));
        Assert.DoesNotContain("EffectiveAllocated", entry.EvidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("rawAllocatedUsd", entry.EvidenceJson, StringComparison.Ordinal);
        Assert.All(entry.CoverageEdges!, edge =>
            Assert.DoesNotContain("amount", edge.EvidenceJson, StringComparison.OrdinalIgnoreCase));
        var decision = HistoricalGrossNetParityDecisionFactory.TryCreateExact(
            target,
            [],
            HistoricalGrossNetParityConstants.CutoffUtc,
            "historical-gross-net-parity-v1");
        Assert.NotNull(decision);
        Assert.DoesNotContain("rawAllocatedUsd", decision!.EvidenceJson, StringComparison.Ordinal);
        Assert.Contains(target.StrategyId.ToString("D"), decision.EvidenceJson, StringComparison.Ordinal);
        Assert.Contains("\"StrategyRank\":1", decision.EvidenceJson, StringComparison.Ordinal);
        Assert.Contains("\"StrategyGrossPnlUsd\":", decision.EvidenceJson, StringComparison.Ordinal);
        Assert.Contains(
            HistoricalGrossNetParityConstants.StrategyCompletionContractId,
            decision.EvidenceJson,
            StringComparison.Ordinal);
        Assert.Contains(
            HistoricalGrossNetParityConstants.StrategyCompletionSemanticDigest,
            decision.EvidenceJson,
            StringComparison.Ordinal);
        Assert.Contains(
            HistoricalGrossNetParityConstants.CalculationVersion,
            decision.EvidenceJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricalParityPaperPrepare_CanonicalizesTimezoneBearingLineageBeforeHashing()
    {
        var strategyId = Guid.Parse("76500000-0000-0000-0000-000000000001");
        var buy = CreateParityFill(
            Guid.Parse("76500000-0000-0000-0000-000000000002"),
            strategyId,
            TradeSide.Buy,
            DateTimeOffset.Parse("2026-07-10T02:25:05.354423+00:00"),
            1m,
            0.5m,
            0.01m) with
        {
            CanonicalPayloadJson =
                "{\"filledAtUtc\":\"2026-07-10T05:25:05.354423+03:00\",\"nested\":{\"role\":\"Taker\"}}"
        };
        var sell = CreateParityFill(
            Guid.Parse("76500000-0000-0000-0000-000000000003"),
            strategyId,
            TradeSide.Sell,
            DateTimeOffset.Parse("2026-07-10T02:30:05.354423+00:00"),
            1m,
            0.6m,
            0.01m,
            realizedPnlUsd: 0.1m,
            netRealizedPnlUsd: 0.08m) with
        {
            CanonicalPayloadJson =
                "{\"filledAtUtc\":\"2026-07-10T05:30:05.354423+03:00\",\"nested\":{\"role\":\"Taker\"}}"
        };
        var page = CreatePaperSellPage(strategyId, sell, [buy, sell]);

        var prepared = HistoricalGrossNetParityPaperPreparer.Prepare(
            page,
            HistoricalGrossNetParityConstants.CutoffUtc);

        Assert.Empty(prepared.Conflicts);
        var target = Assert.Single(prepared.Targets);
        using var lineageDocument = System.Text.Json.JsonDocument.Parse(target.LineagePayloadJson);
        var canonicalLineage = System.Text.Json.JsonSerializer.Serialize(lineageDocument.RootElement);
        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonicalLineage)))
            .ToLowerInvariant();

        Assert.Equal(canonicalLineage, target.LineagePayloadJson);
        Assert.Equal(expectedHash, target.LineageHash);
        Assert.Contains("\\u002B03:00", target.LineagePayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricalParityPaperPrepare_DerivesRunlessSellExitLookupBeforeFallback()
    {
        var strategyId = Guid.Parse("77000000-0000-0000-0000-000000000001");
        var buy = CreateParityFill(
            Guid.Parse("77000000-0000-0000-0000-000000000002"),
            strategyId,
            TradeSide.Buy,
            CutoffUtc.AddMinutes(-2),
            1m,
            0.5m,
            0.1m);
        var sell = CreateParityFill(
            Guid.Parse("77000000-0000-0000-0000-000000000003"),
            strategyId,
            TradeSide.Sell,
            CutoffUtc.AddMinutes(2),
            0.5m,
            0.7m,
            0m,
            realizedPnlUsd: 0.1m) with
        {
            FeeAccountingStatus = FeeAccountingStatus.CalculationUnavailable.ToString(),
            FeeCalculationSource = PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource,
            FeeRate = null,
            FeeExponent = null,
            FeeTakerOnly = null,
            NetRealizedPnlUsd = null
        };
        var page = CreatePaperSellPage(strategyId, sell, [buy, sell]);

        var prepared = HistoricalGrossNetParityPaperPreparer.Prepare(
            page,
            HistoricalGrossNetParityConstants.CutoffUtc);

        Assert.Empty(prepared.Conflicts);
        var target = Assert.Single(prepared.Targets);
        Assert.Equal(HistoricalGrossNetParityExactEligibility.LocalLookupRequired, target.ExactEligibility);
        var lookup = Assert.Single(prepared.LookupRequests);
        Assert.Equal(TradeSide.Sell.ToString(), lookup.Side);
        Assert.Equal($"paper-fill:{sell.FillId:D}:exit", lookup.FeeSourceChargeId);
        Assert.Equal(0.05m, target.ProvedComponentFloorUsd);

        var outcome = new HistoricalGrossNetParityLookupOutcome(
            lookup.TupleHash,
            HistoricalGrossNetParityLookupOutcomeStatus.Success,
            0.01m,
            PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
            FeeLiquidityRole.Taker.ToString(),
            0.01m,
            2,
            true,
            lookup.FeeApplicationKind,
            lookup.FeeAllocationId,
            lookup.FeeSourceChargeId,
            CutoffUtc.AddMinutes(3),
            "lookup-evidence");
        var augmented = HistoricalGrossNetParityDecisionFactory.WithLookupEvidence(target, [outcome]);
        var exact = HistoricalGrossNetParityDecisionFactory.TryCreateExact(
            augmented,
            [outcome],
            CutoffUtc.AddMinutes(3),
            HistoricalGrossNetParityConstants.CalculationVersion);

        Assert.NotNull(exact);
        Assert.Equal(0.06m, augmented.ProvedComponentFloorUsd);
        Assert.Equal(2, augmented.ProvedComponents.Count);
        Assert.Contains(lookup.FeeAllocationId, exact!.EvidenceJson, StringComparison.Ordinal);
        Assert.Equal("mixed", exact.FeeCalculationSource);
        Assert.Equal(FeeLiquidityRole.Unknown.ToString(), exact.FeeLiquidityRole);
    }

    [Fact]
    public void HistoricalParityPaperPrepare_InvalidPoolDefersOnlyItsOwnTarget()
    {
        var strategyId = Guid.Parse("78000000-0000-0000-0000-000000000001");
        var goodBuy = CreateParityFill(
            Guid.Parse("78000000-0000-0000-0000-000000000002"),
            strategyId,
            TradeSide.Buy,
            CutoffUtc.AddMinutes(-3),
            1m,
            0.5m,
            0.1m) with
        {
            CopiedTraderWallet = "good-wallet",
            AssetId = "good-asset"
        };
        var goodSell = CreateParityFill(
            Guid.Parse("78000000-0000-0000-0000-000000000003"),
            strategyId,
            TradeSide.Sell,
            CutoffUtc.AddMinutes(1),
            0.5m,
            0.7m,
            0m,
            realizedPnlUsd: 0.1m,
            netRealizedPnlUsd: 0.05m) with
        {
            CopiedTraderWallet = "good-wallet",
            AssetId = "good-asset"
        };
        var invalidSell = CreateParityFill(
            Guid.Parse("78000000-0000-0000-0000-000000000004"),
            strategyId,
            TradeSide.Sell,
            CutoffUtc.AddMinutes(2),
            1m,
            0.6m,
            0m) with
        {
            CopiedTraderWallet = "invalid-wallet",
            AssetId = "invalid-asset"
        };
        HistoricalGrossNetParityCandidateKey Candidate(
            HistoricalGrossNetParityPaperFillObservation sell) => new(
                HistoricalGrossNetParitySourceKind.PaperSellFill,
                sell.FillId,
                strategyId,
                $"strategy-{strategyId:N}",
                1,
                100m,
                CutoffUtc.AddMinutes(-3),
                4,
                sell.FillRowVersion,
                HistoricalGrossNetParityOwnership.None);
        var page = new HistoricalGrossNetParityCandidatePage(
            HistoricalGrossNetParityReadStatus.Complete,
            [Candidate(goodSell), Candidate(invalidSell)],
            [],
            [goodBuy, goodSell, invalidSell],
            [],
            [],
            [],
            [CreatePaperSourceSelection(strategyId, usesRuns: false)],
            [],
            [],
            null,
            true);

        var prepared = HistoricalGrossNetParityPaperPreparer.Prepare(page, CutoffUtc);

        Assert.Equal(goodSell.FillId, Assert.Single(prepared.Targets).SourceId);
        var conflict = Assert.Single(
            prepared.Conflicts,
            value => value.SourceId == invalidSell.FillId);
        Assert.Equal("paper_sell_replay_missing", conflict.Code);
        Assert.Contains("no positive replayed", conflict.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(prepared.Conflicts, value => value.SourceId is null);
    }

    [Fact]
    public void HistoricalParityPaperPrepare_MixedPositionAfterPartialSellKeepsAggregateRemainingPoolProof()
    {
        var strategyId = Guid.Parse("79000000-0000-0000-0000-000000000001");
        var wallet = "mixed-position-wallet";
        var assetId = "mixed-position-asset";
        var buy1 = CreateParityFill(
            Guid.Parse("79000000-0000-0000-0000-000000000002"),
            strategyId,
            TradeSide.Buy,
            CutoffUtc.AddMinutes(-3),
            1m,
            0.4m,
            0.1m) with
        {
            CopiedTraderWallet = wallet,
            AssetId = assetId
        };
        var buy2 = CreateParityFill(
            Guid.Parse("79000000-0000-0000-0000-000000000003"),
            strategyId,
            TradeSide.Buy,
            CutoffUtc.AddMinutes(-2),
            1m,
            0.6m,
            0.2m) with
        {
            CopiedTraderWallet = wallet,
            AssetId = assetId,
            FeeCalculationSource = "historical-current-paper-model-v1:" +
                PolymarketFeeCalculationConstants.FeeCurveCalculationSource
        };
        var sell = CreateParityFill(
            Guid.Parse("79000000-0000-0000-0000-000000000004"),
            strategyId,
            TradeSide.Sell,
            CutoffUtc.AddMinutes(1),
            1m,
            0.7m,
            0m,
            realizedPnlUsd: 0.2m,
            netRealizedPnlUsd: 0.05m) with
        {
            CopiedTraderWallet = wallet,
            AssetId = assetId
        };
        var positionId = Guid.Parse("79000000-0000-0000-0000-000000000005");
        var position = new HistoricalGrossNetParityPaperPositionObservation(
            positionId,
            7,
            strategyId,
            wallet,
            assetId,
            "condition",
            "Yes",
            1m,
            0.5m,
            0.6m,
            0.1m,
            0.15m,
            FeeAccountingStatus.Calculated.ToString(),
            FeeLiquidityRole.Taker.ToString(),
            "mixed",
            null,
            null,
            null,
            CutoffUtc.AddMinutes(2),
            -0.05m,
            CutoffUtc.AddMinutes(2),
            "position-payload");
        var candidate = new HistoricalGrossNetParityCandidateKey(
            HistoricalGrossNetParitySourceKind.PaperPosition,
            positionId,
            strategyId,
            $"strategy-{strategyId:N}",
            1,
            position.UnrealizedPnlUsd,
            CutoffUtc.AddMinutes(-3),
            2,
            position.RowVersion,
            HistoricalGrossNetParityOwnership.None);
        var page = new HistoricalGrossNetParityCandidatePage(
            HistoricalGrossNetParityReadStatus.Complete,
            [candidate],
            [],
            [buy1, buy2, sell],
            [position],
            [],
            [],
            [CreatePaperSourceSelection(strategyId, usesRuns: false)],
            [],
            [],
            null,
            true);

        var prepared = HistoricalGrossNetParityPaperPreparer.Prepare(page, CutoffUtc);

        Assert.Empty(prepared.Conflicts);
        var target = Assert.Single(prepared.Targets);
        var expectedTargetHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(position.CanonicalPayloadJson)))
            .ToLowerInvariant();
        Assert.Equal(expectedTargetHash, target.TargetTupleHash);
        Assert.Equal(HistoricalGrossNetParityExactEligibility.ExistingExactPreserved, target.ExactEligibility);
        var component = Assert.Single(target.ProvedComponents);
        Assert.Equal(0.15m, component.AmountUsd);
        Assert.Equal(2, component.SourceCharges?.Count);
        Assert.Equal(2, component.CoverageEdges?.Count);
        Assert.Equal(0.15m, component.PoolMovement?.RemainingBeforeUsd);
        Assert.Equal(0m, component.PoolMovement?.RemainingAfterUsd);
        Assert.Equal(0.15m, component.PoolMovement?.DecrementUsd);
        Assert.Equal(0m, component.PoolMovement?.ResidualUsd);

        var venuePosition = position with
        {
            FeeAccountingStatus = FeeAccountingStatus.VenueReported.ToString(),
            FeeLiquidityRole = FeeLiquidityRole.Unknown.ToString(),
            FeeCalculationSource = "paper-venue-reported-integration-v1",
            FeeRate = null,
            FeeExponent = null,
            FeeTakerOnly = null
        };
        var venuePrepared = HistoricalGrossNetParityPaperPreparer.Prepare(
            page with { PaperPositionObservations = [venuePosition] },
            CutoffUtc);
        Assert.Empty(venuePrepared.Conflicts);
        Assert.Equal(
            HistoricalGrossNetParityExactEligibility.ExistingExactPreserved,
            Assert.Single(venuePrepared.Targets).ExactEligibility);
    }

    [Fact]
    public void HistoricalParityPaperPrepare_PositionReplaysWholeWalletAssetPoolAcrossStrategies()
    {
        var targetStrategyId = Guid.Parse("7a000000-0000-0000-0000-000000000001");
        var otherStrategyId = Guid.Parse("7a000000-0000-0000-0000-000000000002");
        var wallet = "cross-strategy-wallet";
        var assetId = "cross-strategy-asset";
        var firstBuy = CreateParityFill(
            Guid.Parse("7a000000-0000-0000-0000-000000000003"),
            targetStrategyId,
            TradeSide.Buy,
            CutoffUtc.AddMinutes(-3),
            1m,
            0.4m,
            0.1m) with
        {
            CopiedTraderWallet = wallet,
            AssetId = assetId
        };
        var secondBuy = CreateParityFill(
            Guid.Parse("7a000000-0000-0000-0000-000000000004"),
            otherStrategyId,
            TradeSide.Buy,
            CutoffUtc.AddMinutes(-2),
            1m,
            0.6m,
            0.2m) with
        {
            CopiedTraderWallet = wallet,
            AssetId = assetId
        };
        var positionId = Guid.Parse("7a000000-0000-0000-0000-000000000005");
        var position = new HistoricalGrossNetParityPaperPositionObservation(
            positionId,
            3,
            targetStrategyId,
            wallet,
            assetId,
            "condition",
            "Yes",
            2m,
            0.5m,
            0.6m,
            0.2m,
            0.3m,
            FeeAccountingStatus.Calculated.ToString(),
            FeeLiquidityRole.Taker.ToString(),
            "mixed",
            null,
            null,
            null,
            CutoffUtc.AddMinutes(1),
            -0.1m,
            CutoffUtc.AddMinutes(1),
            "cross-strategy-position-payload");
        var candidate = new HistoricalGrossNetParityCandidateKey(
            HistoricalGrossNetParitySourceKind.PaperPosition,
            positionId,
            targetStrategyId,
            $"strategy-{targetStrategyId:N}",
            1,
            position.UnrealizedPnlUsd,
            firstBuy.FilledAtUtc,
            2,
            position.RowVersion,
            HistoricalGrossNetParityOwnership.None);
        var page = new HistoricalGrossNetParityCandidatePage(
            HistoricalGrossNetParityReadStatus.Complete,
            [candidate],
            [],
            [firstBuy, secondBuy],
            [position],
            [],
            [],
            [CreatePaperSourceSelection(targetStrategyId, usesRuns: false)],
            [],
            [],
            null,
            true);

        var prepared = HistoricalGrossNetParityPaperPreparer.Prepare(page, CutoffUtc);

        Assert.Empty(prepared.Conflicts);
        var target = Assert.Single(prepared.Targets);
        var component = Assert.Single(target.ProvedComponents);
        Assert.Equal(0.3m, component.AmountUsd);
        Assert.Equal(
            [
                $"paper-fill:{firstBuy.FillId:D}:entry",
                $"paper-fill:{secondBuy.FillId:D}:entry"
            ],
            component.SourceCharges!
                .Select(charge => charge.SourceChargeId)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(2, component.CoverageEdges?.Count);
        Assert.Equal(firstBuy.FilledAtUtc, target.OriginatedAtUtc);
    }

    private static HistoricalGrossNetParityTargetSnapshot CreateParityTarget(
        HistoricalGrossNetParitySourceKind sourceKind,
        Guid sourceId,
        Guid strategyId,
        HistoricalGrossNetParityExactEligibility eligibility,
        decimal gross,
        decimal basis,
        decimal fee,
        decimal? net,
        IReadOnlyList<HistoricalGrossNetParityComponentAllocationV1>? components = null)
    {
        components ??= [];
        var exact = eligibility == HistoricalGrossNetParityExactEligibility.ExistingExactPreserved;
        var targetHash = sourceId.ToString("N") + sourceId.ToString("N");
        var lineageHash = new string('b', 64);
        var componentHash = HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash(components);
        return new HistoricalGrossNetParityTargetSnapshot(
            SourceKind: sourceKind,
            SourceId: sourceId,
            StrategyId: strategyId,
            StrategyRank: 1,
            StrategyGrossPnlUsd: 100m,
            RowVersion: 10,
            OriginatedAtUtc: HistoricalGrossNetParityConstants.CutoffUtc.AddDays(-1),
            SettledAtUtc: HistoricalGrossNetParityConstants.CutoffUtc.AddHours(1),
            GrossPnlUsd: gross,
            GrossRoiBasisUsd: basis,
            FeeUsd: fee,
            FeeAccountingStatus: exact
                ? FeeAccountingStatus.Calculated.ToString()
                : FeeAccountingStatus.LegacyUnknown.ToString(),
            FeeLiquidityRole: exact
                ? FeeLiquidityRole.Taker.ToString()
                : FeeLiquidityRole.Unknown.ToString(),
            FeeCalculationSource: exact
                ? PolymarketFeeCalculationConstants.FeeCurveCalculationSource
                : "legacy",
            FeeRate: exact ? 0.01m : null,
            FeeExponent: exact ? 2 : null,
            FeeTakerOnly: exact,
            FeeCalculatedAtUtc: exact ? HistoricalGrossNetParityConstants.CutoffUtc.AddHours(-1) : null,
            NetPnlUsd: net,
            BalanceEffectApplied: false,
            Ownership: HistoricalGrossNetParityOwnership.None,
            TargetTupleHash: targetHash,
            LineageHash: lineageHash,
            ComponentHash: componentHash,
            ExactEligibility: eligibility,
            AuthoritativeEffectiveFeeUsd: exact ? gross - net : null,
            ExactEvidenceReferences: exact
                ? [new HistoricalGrossNetParityEvidenceReferenceV1(
                    "exact-accounting",
                    "v1",
                    new string('c', 64),
                    sourceKind,
                    sourceId)]
                : [],
            ProvedCryptoAssetSymbol: null,
            CryptoAssetEvidenceReference: null,
            ProvedComponentFloorUsd: components.Sum(component => component.AmountUsd),
            ProvedComponents: components,
            BaselineEffectKind: sourceKind == HistoricalGrossNetParitySourceKind.LiveOrder
                ? HistoricalGrossNetParityBaselineEffectKind.None
                : null,
            NominalBaselineGrossPnlUsd: sourceKind == HistoricalGrossNetParitySourceKind.LiveOrder
                ? gross
                : null,
            NominalBaselineNetPnlUsd: null,
            CanonicalPayloadJson: $"{{\"sourceId\":\"{sourceId:D}\"}}",
            LineagePayloadJson: "{}",
            ComponentPayloadJson: "[]",
            BindingHash: HistoricalGrossNetParityBindingV1.Compute(
                targetHash,
                lineageHash,
                componentHash));
    }

    private static HistoricalGrossNetParityCandidatePage CreateParityPage(
        HistoricalGrossNetParityTargetSnapshot target,
        IReadOnlyList<HistoricalGrossNetParityLookupRequest>? lookups = null) =>
        CreateParityPage(
            HistoricalGrossNetParityProcessingPhase.Exact,
            [target],
            lookups ?? []);

    private static HistoricalGrossNetParityCandidatePage CreateParityPage(
        HistoricalGrossNetParityProcessingPhase phase,
        IReadOnlyList<HistoricalGrossNetParityTargetSnapshot> targets,
        IReadOnlyList<HistoricalGrossNetParityLookupRequest> lookups,
        IReadOnlyList<HistoricalGrossNetParityTargetConflict>? conflicts = null)
    {
        _ = phase;
        var candidates = targets.Select(target => new HistoricalGrossNetParityCandidateKey(
            target.SourceKind,
            target.SourceId,
            target.StrategyId,
            $"strategy-{target.StrategyId:N}",
            target.StrategyRank,
            target.StrategyGrossPnlUsd,
            target.OriginatedAtUtc,
            (int)target.SourceKind,
            target.RowVersion,
            target.Ownership)).ToArray();
        return new HistoricalGrossNetParityCandidatePage(
            HistoricalGrossNetParityReadStatus.Complete,
            candidates,
            targets.Where(target => target.SourceKind == HistoricalGrossNetParitySourceKind.LiveOrder)
                .ToArray(),
            [],
            [],
            [],
            [],
            [],
            lookups,
            conflicts ?? [],
            null,
            true);
    }

    private static HistoricalGrossNetParityCandidatePage CreatePaperSellPage(
        Guid strategyId,
        HistoricalGrossNetParityPaperFillObservation sell,
        IReadOnlyList<HistoricalGrossNetParityPaperFillObservation> fills)
    {
        var candidate = new HistoricalGrossNetParityCandidateKey(
            HistoricalGrossNetParitySourceKind.PaperSellFill,
            sell.FillId,
            strategyId,
            $"strategy-{strategyId:N}",
            1,
            sell.RealizedPnlUsd,
            fills.Where(fill => string.Equals(fill.OrderSide, TradeSide.Buy.ToString(), StringComparison.Ordinal))
                .Min(fill => fill.FilledAtUtc),
            4,
            sell.FillRowVersion,
            HistoricalGrossNetParityOwnership.None);
        return new HistoricalGrossNetParityCandidatePage(
            HistoricalGrossNetParityReadStatus.Complete,
            [candidate],
            [],
            fills,
            [],
            [],
            [],
            [CreatePaperSourceSelection(strategyId, usesRuns: false)],
            [],
            [],
            null,
            true);
    }

    private static HistoricalGrossNetParityLookupRequest CreateParityLookup(
        HistoricalGrossNetParityTargetSnapshot target,
        string liquidityRole) => new(
            target.SourceId.ToString("N") + "lookup",
            target.SourceKind,
            target.SourceId,
            target.StrategyId,
            "condition",
            "asset",
            TradeSide.Buy.ToString(),
            "Up",
            target.GrossRoiBasisUsd,
            10m,
            0.5m,
            liquidityRole,
            HistoricalGrossNetParityLookupFeeApplicationKind.TotalContributionFee,
            $"lookup-allocation:{target.SourceId:D}",
            $"canonical-local:{target.SourceKind}:{target.SourceId:D}:v1",
            "{}");

    private static HistoricalGrossNetParityPaperFillObservation CreateParityFill(
        Guid fillId,
        Guid strategyId,
        TradeSide side,
        DateTimeOffset filledAtUtc,
        decimal shares,
        decimal price,
        decimal feeUsd,
        decimal realizedPnlUsd = 0m,
        decimal? netRealizedPnlUsd = null) => new(
            FillId: fillId,
            FillRowVersion: 1,
            PaperOrderId: fillId,
            PaperOrderRowVersion: 1,
            StrategyId: strategyId,
            CopiedTraderWallet: "wallet",
            OrderStatus: PaperOrderStatus.Filled.ToString(),
            OrderSide: side.ToString(),
            ExecutionSource: "historical-test",
            AssetId: "asset",
            ConditionId: "condition",
            Outcome: "Up",
            OrderPrice: price,
            OrderSizeShares: shares,
            OrderCreatedAtUtc: filledAtUtc.AddMinutes(-1),
            FillPrice: price,
            FillSizeShares: shares,
            FilledAtUtc: filledAtUtc,
            RealizedPnlUsd: realizedPnlUsd,
            FeeUsd: feeUsd,
            FeeAccountingStatus: FeeAccountingStatus.Calculated.ToString(),
            FeeLiquidityRole: FeeLiquidityRole.Taker.ToString(),
            FeeCalculationSource: PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
            FeeRate: 0.01m,
            FeeExponent: 2,
            FeeTakerOnly: true,
            FeeCalculatedAtUtc: filledAtUtc,
            NetRealizedPnlUsd: netRealizedPnlUsd,
            CanonicalEventKey: $"{filledAtUtc:O}|{fillId:D}",
            CanonicalPayloadJson: $"{{\"fillId\":\"{fillId:D}\"}}");

    private static HistoricalGrossNetParityPaperSourceSelection CreatePaperSourceSelection(
        Guid strategyId,
        bool usesRuns) => new(
            strategyId,
            usesRuns,
            usesRuns ? 1 : 0,
            0,
            new string('d', 64),
            "{}");

    private static HistoricalFeeLookupResult OperationalLookupFailure() => new(
        HistoricalFeeLookupDisposition.OperationalFailure,
        null,
        FeeAccountingStatus.CalculationUnavailable.ToString(),
        FeeLiquidityRole.Taker.ToString(),
        "operational-test",
        null,
        null,
        null,
        HistoricalGrossNetParityConstants.CutoffUtc,
        503,
        "retry");

    private static HistoricalGrossNetParityDonorPreviewResult CreateDonorPreview(
        HistoricalGrossNetParityDonorPreviewRequest request,
        Func<HistoricalGrossNetDonorCandidateDescriptorV1,
            HistoricalGrossNetParityDonorCandidateAggregate?>? aggregateFactory = null)
    {
        var candidates = request.OrderedCandidates
            .Skip(request.CandidateOffset)
            .Take(request.PageSize)
            .ToArray();
        var aggregates = candidates.Select(candidate => aggregateFactory?.Invoke(candidate) ??
            new HistoricalGrossNetParityDonorCandidateAggregate(
                candidate.StrategyId,
                candidate.MatcherOrder,
                candidate.Tier,
                candidate.DistanceComponents,
                0,
                0,
                0,
                0m,
                0m,
                0m,
                HistoricalGrossNetDonorHashV1.ComputeMembershipHash([])))
            .ToArray();
        var next = request.CandidateOffset + candidates.Length;
        return new HistoricalGrossNetParityDonorPreviewResult(
            HistoricalGrossNetParityReadStatus.Complete,
            aggregates,
            next,
            next == request.OrderedCandidates.Count);
    }

    private static HistoricalGrossNetParityProcessor CreateHistoricalParityProcessor(
        IHistoricalGrossNetParityStore store,
        IPolymarketFeeAccountingService feeService,
        IPaperFakFeeBackfillEventRecorder? eventRecorder = null) =>
        HistoricalGrossNetParityProcessor.CreateForTests(
            NullLogger<HistoricalGrossNetParityProcessor>.Instance,
            new HistoricalGrossNetParityOptions
            {
                Enabled = true,
                HistoricalCutoffUtc = HistoricalGrossNetParityConstants.CutoffUtc,
                BatchSize = 50,
                LookupMaxAttempts = 3
            },
            store,
            feeService,
            eventRecorder);

    private static PaperFakFeeBackfillProcessor CreateProcessor(
        TestAppRepository repository,
        IPolymarketFeeAccountingService feeService,
        bool applyEnabled,
        IPaperFakFeeBackfillEventRecorder? eventRecorder = null)
    {
        if (repository.HistoricalPaperFakFeeBackfillStrategyRanks.Count == 0)
        {
            repository.HistoricalPaperFakFeeBackfillStrategyRanks.AddRange(
                repository.HistoricalPaperFakFeeBackfillPages
                    .SelectMany(page => page.Candidates)
                    .Select(candidate => candidate.Order.StrategyId)
                    .Distinct()
                    .Order()
                    .Select(strategyId => new HistoricalPaperFakFeeBackfillStrategyRank(
                        strategyId,
                        $"strategy-{strategyId:N}",
                        0m)));
        }

        return new PaperFakFeeBackfillProcessor(
            NullLogger<PaperFakFeeBackfillProcessor>.Instance,
            new PaperFakFeeBackfillOptions
            {
                Enabled = true,
                ApplyEnabled = applyEnabled,
                HistoricalCutoffUtc = CutoffUtc,
                BatchSize = 50
            },
            repository,
            feeService,
            eventRecorder);
    }

    private sealed class RecordingEventRecorder : IPaperFakFeeBackfillEventRecorder
    {
        public List<PaperFakFeeBackfillEvent> Events { get; } = [];

        public Task RecordAsync(
            PaperFakFeeBackfillEvent entry,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(entry);
            return Task.CompletedTask;
        }
    }

    private static HistoricalPaperFakFeeBackfillCandidate CreateCandidate(
        string conditionId,
        Guid strategyId = default)
    {
        var orderId = Guid.NewGuid();
        var assetId = $"asset-{Guid.NewGuid():N}";
        var order = new PaperOrder(
            orderId,
            Guid.NewGuid(),
            "paper-wallet",
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            assetId,
            conditionId,
            "Up",
            0.50m,
            10m,
            5m,
            FilledAtUtc.AddMinutes(-1),
            FilledAtUtc.AddMinutes(4),
            FilledAtUtc: FilledAtUtc,
            StrategyId: strategyId,
            ExecutionSource: "btc_updown5m_fak_taker_paper");
        var fill = new PaperFill(
            Guid.NewGuid(),
            orderId,
            0.50m,
            10m,
            FilledAtUtc,
            "historical-test");
        return new HistoricalPaperFakFeeBackfillCandidate(order, fill);
    }

    private sealed class RecordingFeeAccountingService(
        Func<PaperOrder, PaperFill, int, Task<PaperFill>> handler) : IPolymarketFeeAccountingService
    {
        private int concurrentCalls;
        private int callCount;

        public List<(PaperOrder Order, PaperFill Fill)> Calls { get; } = [];

        public int MaxConcurrentCalls { get; private set; }

        public async Task<PaperFill> ApplyToPaperFillAsync(
            PaperOrder order,
            PaperFill fill,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((order, fill));
            var currentConcurrentCalls = Interlocked.Increment(ref concurrentCalls);
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, currentConcurrentCalls);
            var currentCall = Interlocked.Increment(ref callCount);
            try
            {
                return await handler(order, fill, currentCall);
            }
            finally
            {
                Interlocked.Decrement(ref concurrentCalls);
            }
        }

        public Task<LiveOrder> ApplyToLiveOrderAsync(
            LiveOrder order,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PaperEntryPersistenceBatch> ApplyToEntryBatchAsync(
            PaperEntryPersistenceBatch batch,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingHistoricalFeeService(
        Func<HistoricalFeeLookupRequest, int, HistoricalFeeLookupResult> handler)
        : IPolymarketFeeAccountingService
    {
        private int callCount;

        public List<HistoricalFeeLookupRequest> Requests { get; } = [];

        public Task<HistoricalFeeLookupResult> CalculateHistoricalFeeAsync(
            HistoricalFeeLookupRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(handler(request, Interlocked.Increment(ref callCount)));
        }

        public Task<PaperFill> ApplyToPaperFillAsync(
            PaperOrder order,
            PaperFill fill,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LiveOrder> ApplyToLiveOrderAsync(
            LiveOrder order,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaperEntryPersistenceBatch> ApplyToEntryBatchAsync(
            PaperEntryPersistenceBatch batch,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingParityStore : IHistoricalGrossNetParityStore
    {
        public Func<HistoricalGrossNetParityCandidatePageRequest, HistoricalGrossNetParityCandidatePage>?
            CandidatePageFactory { get; init; }

        public Func<HistoricalGrossNetParityDonorPreviewRequest, HistoricalGrossNetParityDonorPreviewResult>?
            DonorPreviewFactory { get; init; }

        public Func<HistoricalGrossNetParityPaperDecisionRequest, HistoricalGrossNetParityApplyResult>?
            PaperApplyFactory { get; init; }

        public Func<HistoricalGrossNetParityLiveAccountingRequest, HistoricalGrossNetParityApplyResult>?
            LiveApplyFactory { get; init; }

        public Func<HistoricalGrossNetParityLiveBalanceRequest, HistoricalGrossNetParityLiveBalanceResult>?
            LiveBalanceFactory { get; init; }

        public List<HistoricalGrossNetParityCandidatePageRequest> CandidatePageRequests { get; } = [];
        public List<HistoricalGrossNetParityDonorPreviewRequest> DonorPreviewRequests { get; } = [];
        public List<HistoricalGrossNetParityPaperDecisionRequest> PaperDecisionRequests { get; } = [];
        public List<HistoricalGrossNetParityLiveAccountingRequest> LiveAccountingRequests { get; } = [];
        public List<HistoricalGrossNetParityLiveBalanceRequest> LiveBalanceRequests { get; } = [];

        public Task<HistoricalGrossNetParityCandidatePage>
            LoadHistoricalGrossNetParityCandidatePageAsync(
                HistoricalGrossNetParityCandidatePageRequest request,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CandidatePageRequests.Add(request);
            return Task.FromResult(CandidatePageFactory?.Invoke(request) ??
                throw new InvalidOperationException("A candidate-page factory is required."));
        }

        public Task<HistoricalGrossNetParityDonorPreviewResult>
            LoadHistoricalGrossNetParityDonorPreviewAsync(
                HistoricalGrossNetParityDonorPreviewRequest request,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DonorPreviewRequests.Add(request);
            return Task.FromResult(DonorPreviewFactory?.Invoke(request) ?? CreateDonorPreview(request));
        }

        public Task<HistoricalGrossNetParityApplyResult>
            TryApplyHistoricalGrossNetParityPaperDecisionAsync(
                HistoricalGrossNetParityPaperDecisionRequest request,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PaperDecisionRequests.Add(request);
            return Task.FromResult(PaperApplyFactory?.Invoke(request) ?? new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.Applied,
                true,
                request.Target.TargetTupleHash,
                HistoricalGrossNetParityOwnership.Completed));
        }

        public Task<HistoricalGrossNetParityApplyResult>
            TryApplyHistoricalGrossNetParityLiveAccountingAsync(
                HistoricalGrossNetParityLiveAccountingRequest request,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LiveAccountingRequests.Add(request);
            return Task.FromResult(LiveApplyFactory?.Invoke(request) ?? new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.Applied,
                true,
                request.Target.TargetTupleHash,
                HistoricalGrossNetParityOwnership.Completed));
        }

        public Task<HistoricalGrossNetParityLiveBalanceResult>
            TryApplyHistoricalGrossNetParityEarliestLiveBalanceAsync(
                HistoricalGrossNetParityLiveBalanceRequest request,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LiveBalanceRequests.Add(request);
            return Task.FromResult(LiveBalanceFactory?.Invoke(request) ??
                new HistoricalGrossNetParityLiveBalanceResult(
                HistoricalGrossNetParityApplyStatus.Applied,
                request.LiveOrderId,
                HistoricalGrossNetParityOwnership.Completed,
                0m,
                0m,
                0m,
                true));
        }

        public Task<HistoricalGrossNetParityVenueRevisionResult>
            ApplyHistoricalGrossNetParityVenueRevisionAsync(
                HistoricalGrossNetParityVenueRevisionRequest request,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HistoricalGrossNetParityVenueRevisionResult(
                false,
                true,
                HistoricalGrossNetParityOwnership.Completed,
                0m,
                0m,
                0m,
                false,
                "not used"));
        }
    }

}
