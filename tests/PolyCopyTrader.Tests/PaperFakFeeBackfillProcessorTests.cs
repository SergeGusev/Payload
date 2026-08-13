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
}
