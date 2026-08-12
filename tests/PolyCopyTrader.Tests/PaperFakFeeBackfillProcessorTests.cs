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
        var processor = CreateProcessor(repository, feeService, applyEnabled: true);

        var result = await processor.RunCycleAsync();

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
        Assert.True(result.ReachedEnd);
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
        var sweepEndCycle = await processor.RunCycleAsync();
        var retryCycle = await processor.RunCycleAsync();

        Assert.Equal(1, transientCycle.TransientLookupUnavailable);
        Assert.Null(transientCycle.ApplyResult);
        Assert.True(sweepEndCycle.ReachedEnd);
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
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                1)
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

        var firstSweepEnd = await processor.RunCycleAsync();
        var secondSweepEnd = await processor.RunCycleAsync();

        Assert.True(firstSweepEnd.ReachedEnd);
        Assert.Equal(1, firstSweepEnd.TransientLookupUnavailable);
        Assert.Equal(1, firstSweepEnd.ApplyResult?.ConflictsOrDeferred);
        Assert.True(secondSweepEnd.ReachedEnd);
        Assert.Equal(0, secondSweepEnd.TransientLookupUnavailable);
        Assert.Equal(2, repository.HistoricalPaperFakFeeBackfillApplyCalls.Count);
        Assert.All(
            repository.HistoricalPaperFakFeeBackfillCalls,
            call => Assert.Null(call.AfterCursor));
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
        var loserCycle = await processor.RunCycleAsync();

        Assert.False(winnerFirstPageCycle.ReachedEnd);
        Assert.False(winnerLastPageCycle.ReachedEnd);
        Assert.True(loserCycle.ReachedEnd);
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
        var loserCycle = await processor.RunCycleAsync();
        var winnerRetryCycle = await processor.RunCycleAsync();

        Assert.Equal(1, winnerTransientCycle.TransientLookupUnavailable);
        Assert.False(winnerTransientCycle.ReachedEnd);
        Assert.True(loserCycle.ReachedEnd);
        Assert.False(winnerRetryCycle.ReachedEnd);
        Assert.Equal(
            [winnerStrategyId, loserStrategyId, winnerStrategyId],
            feeService.Calls.Select(call => call.Order.StrategyId).ToArray());
        Assert.Equal(2, repository.HistoricalPaperFakFeeBackfillStrategyRankCalls.Count);
        Assert.Equal(2, repository.HistoricalPaperFakFeeBackfillApplyCalls.Count);
    }

    private static PaperFakFeeBackfillProcessor CreateProcessor(
        TestAppRepository repository,
        IPolymarketFeeAccountingService feeService,
        bool applyEnabled)
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
            feeService);
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
