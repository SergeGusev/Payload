using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public interface IPaperFakFeeBackfillProcessor
{
    Task<PaperFakFeeBackfillCycleResult> RunCycleAsync(
        CancellationToken cancellationToken = default);

    Task<PaperFakFeeBackfillCycleResult> RunCycleAsync(
        Guid cycleId,
        CancellationToken cancellationToken = default)
    {
        return RunCycleAsync(cancellationToken);
    }
}

public sealed class PaperFakFeeBackfillProcessor(
    ILogger<PaperFakFeeBackfillProcessor> logger,
    PaperFakFeeBackfillOptions options,
    IAppRepository repository,
    IPolymarketFeeAccountingService feeAccountingService,
    IPaperFakFeeBackfillEventRecorder? eventRecorder = null) : IPaperFakFeeBackfillProcessor
{
    internal const string HistoricalCalculationSourcePrefix =
        "historical-current-paper-model-v1:";

    private IReadOnlyList<HistoricalPaperFakFeeBackfillStrategyRank>? strategyRanks;
    private Guid? sweepId;
    private int strategyRankIndex;
    private HistoricalPaperFakFeeBackfillCursor? continuationCursor;
    private HistoricalPaperNetRunCursor? netContinuationCursor;
    private PaperFakFeeBackfillPhase phase = PaperFakFeeBackfillPhase.ExactHistorical;
    private readonly HashSet<Guid> transientPaperOrderIds = [];

    public Task<PaperFakFeeBackfillCycleResult> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        return RunCycleAsync(Guid.NewGuid(), cancellationToken);
    }

    public async Task<PaperFakFeeBackfillCycleResult> RunCycleAsync(
        Guid cycleId,
        CancellationToken cancellationToken = default)
    {
        var cycleStartedTimestamp = Stopwatch.GetTimestamp();
        await EnsureStrategyRanksAsync(cycleId, cancellationToken).ConfigureAwait(false);
        if (strategyRanks!.Count == 0)
        {
            var emptyCompletedSweepId = sweepId;
            ResetSweep();
            var emptyResult = new PaperFakFeeBackfillCycleResult(
                0,
                0,
                0,
                true,
                options.ApplyEnabled,
                null);
            await TryRecordEventAsync(
                CreateProcessorEvent(
                    PaperFakFeeBackfillEventTypes.CycleCompleted,
                    PaperFakFeeBackfillEventLevels.Information,
                    "Historical Paper FAK fee backfill cycle completed with an empty strategy ranking.") with
                {
                    SweepId = emptyCompletedSweepId,
                    CycleId = cycleId,
                    StrategyCount = 0,
                    Candidates = 0,
                    EvaluatedForApply = 0,
                    TransientLookupUnavailable = 0,
                    Requested = 0,
                    Eligible = 0,
                    FullChainEligible = 0,
                    RunOnlyLegacyEligible = 0,
                    FillsUpdated = 0,
                    RunsUpdated = 0,
                    PositionsUpdated = 0,
                    SettlementsUpdated = 0,
                    FullChainAlreadyApplied = 0,
                    RunOnlyLegacyAlreadyApplied = 0,
                    AlreadyApplied = 0,
                    StructuralConflicts = 0,
                    AccountingConflicts = 0,
                    DeferredByLockTimeout = 0,
                    DeferredByQueryCancel = 0,
                    ReachedStrategyEnd = true,
                    ReachedSweepEnd = true,
                    DurationMilliseconds = GetElapsedMilliseconds(cycleStartedTimestamp)
                },
                cancellationToken).ConfigureAwait(false);
            return emptyResult;
        }

        var activeRank = strategyRanks[strategyRankIndex];
        var activeRankPosition = strategyRankIndex + 1;
        var strategyCount = strategyRanks.Count;
        await TryRecordEventAsync(
            CreateProcessorEvent(
                PaperFakFeeBackfillEventTypes.CycleContext,
                PaperFakFeeBackfillEventLevels.Information,
                "Historical Paper FAK fee backfill cycle context resolved.") with
            {
                SweepId = sweepId,
                CycleId = cycleId,
                StrategyId = activeRank.StrategyId,
                StrategyCode = activeRank.StrategyCode,
                StrategyRank = activeRankPosition,
                StrategyCount = strategyCount,
                GrossRealizedPnlUsd = activeRank.GrossRealizedPnlUsd
            },
            cancellationToken).ConfigureAwait(false);

        if (phase == PaperFakFeeBackfillPhase.AuthoritativeNetRepair)
        {
            return await RunAuthoritativeNetRepairCycleAsync(
                cycleId,
                cycleStartedTimestamp,
                activeRank,
                activeRankPosition,
                strategyCount,
                cancellationToken).ConfigureAwait(false);
        }

        if (phase == PaperFakFeeBackfillPhase.NetFallback)
        {
            return await RunNetFallbackCycleAsync(
                cycleId,
                cycleStartedTimestamp,
                activeRank,
                activeRankPosition,
                strategyCount,
                cancellationToken).ConfigureAwait(false);
        }

        var page = await repository.GetHistoricalPaperFakFeeBackfillCandidatesAsync(
            options.HistoricalCutoffUtc,
            activeRank.StrategyId,
            options.BatchSize,
            continuationCursor,
            cancellationToken).ConfigureAwait(false);
        ValidatePage(activeRank, page);

        var updates = new List<HistoricalPaperFakFeeBackfillUpdate>(page.Candidates.Count);
        var transientLookupUnavailable = 0;
        foreach (var conditionGroup in page.Candidates.GroupBy(
                     candidate => candidate.Order.ConditionId,
                     StringComparer.OrdinalIgnoreCase))
        {
            foreach (var candidate in conditionGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (candidate.Order.Id != candidate.Fill.PaperOrderId)
                {
                    throw new InvalidOperationException(
                        $"Historical Paper FAK fee backfill candidate fill {candidate.Fill.Id} " +
                        $"does not belong to order {candidate.Order.Id}.");
                }

                var takerFill = candidate.Fill with
                {
                    FeeLiquidityRole = FeeLiquidityRole.Taker.ToString()
                };
                var evaluatedFill = await feeAccountingService.ApplyToPaperFillAsync(
                    candidate.Order,
                    takerFill,
                    cancellationToken).ConfigureAwait(false);

                if (IsTransientLookupUnavailable(evaluatedFill))
                {
                    transientLookupUnavailable++;
                    // A non-empty condition reaches this source only after the
                    // production CLOB lookup threw and was converted to an
                    // unavailable result. Keep that operational failure out of
                    // the financial fallback for this strategy visit. A missing
                    // condition ID performs no lookup, so it is a persisted data
                    // gap and remains eligible for the approved ratio fallback.
                    if (!string.IsNullOrWhiteSpace(candidate.Order.ConditionId))
                    {
                        transientPaperOrderIds.Add(candidate.Order.Id);
                    }

                    continue;
                }

                var update = new HistoricalPaperFakFeeBackfillUpdate(
                    candidate,
                    evaluatedFill with
                    {
                        FeeCalculationSource = AddHistoricalProvenance(
                            evaluatedFill.FeeCalculationSource)
                    });
                updates.Add(update);
            }
        }

        HistoricalPaperFakFeeBackfillBatchResult? applyResult = null;
        if (options.ApplyEnabled && updates.Count > 0)
        {
            applyResult = await repository.ApplyHistoricalPaperFakFeeBackfillBatchAsync(
                updates,
                cancellationToken).ConfigureAwait(false);
        }

        var wholeBatchDeferred = applyResult?.WholeBatchDeferred == true;
        var completedSweepId = sweepId;
        var reachedExactPhaseEnd = !wholeBatchDeferred && page.ReachedEnd;
        if (!wholeBatchDeferred)
        {
            AdvanceExactPhase(page);
        }

        const bool reachedStrategyEnd = false;
        const bool reachedSweepEnd = false;

        logger.LogInformation(
            "Historical Paper FAK fee backfill cycle completed. ApplyEnabled={ApplyEnabled} " +
            "CutoffUtc={CutoffUtc:O} StrategyRank={StrategyRank}/{StrategyCount} " +
            "StrategyId={StrategyId} StrategyCode={StrategyCode} GrossRealizedPnlUsd={GrossRealizedPnlUsd} " +
            "Candidates={Candidates} EvaluatedForApply={EvaluatedForApply} " +
            "TransientLookupUnavailable={TransientLookupUnavailable} Requested={Requested} Eligible={Eligible} " +
            "FullChainEligible={FullChainEligible} RunOnlyLegacyEligible={RunOnlyLegacyEligible} " +
            "FillsUpdated={FillsUpdated} RunsUpdated={RunsUpdated} PositionsUpdated={PositionsUpdated} " +
            "SettlementsUpdated={SettlementsUpdated} FullChainAlreadyApplied={FullChainAlreadyApplied} " +
            "RunOnlyLegacyAlreadyApplied={RunOnlyLegacyAlreadyApplied} AlreadyApplied={AlreadyApplied} " +
            "StructuralConflicts={StructuralConflicts} AccountingConflicts={AccountingConflicts} " +
            "DeferredByLockTimeout={DeferredByLockTimeout} " +
            "DeferredByQueryCancel={DeferredByQueryCancel} ReachedExactPhaseEnd={ReachedExactPhaseEnd} " +
            "ReachedStrategyEnd={ReachedStrategyEnd} " +
            "ReachedSweepEnd={ReachedSweepEnd}",
            options.ApplyEnabled,
            options.HistoricalCutoffUtc,
            activeRankPosition,
            strategyCount,
            activeRank.StrategyId,
            activeRank.StrategyCode,
            activeRank.GrossRealizedPnlUsd,
            page.Candidates.Count,
            updates.Count,
            transientLookupUnavailable,
            applyResult?.Requested ?? 0,
            applyResult?.Eligible ?? 0,
            applyResult?.FullChainEligible ?? 0,
            applyResult?.RunOnlyLegacyEligible ?? 0,
            applyResult?.FillsUpdated ?? 0,
            applyResult?.RunsUpdated ?? 0,
            applyResult?.PositionsUpdated ?? 0,
            applyResult?.SettlementsUpdated ?? 0,
            applyResult?.FullChainAlreadyApplied ?? 0,
            applyResult?.RunOnlyLegacyAlreadyApplied ?? 0,
            applyResult?.AlreadyApplied ?? 0,
            applyResult?.StructuralConflicts ?? 0,
            applyResult?.AccountingConflicts ?? 0,
            applyResult?.DeferredByLockTimeout ?? 0,
            applyResult?.DeferredByQueryCancel ?? 0,
            reachedExactPhaseEnd,
            reachedStrategyEnd,
            reachedSweepEnd);

        await TryRecordEventAsync(
            CreateProcessorEvent(
                PaperFakFeeBackfillEventTypes.CycleCompleted,
                PaperFakFeeBackfillEventLevels.Information,
                "Historical Paper FAK fee backfill cycle completed.") with
            {
                SweepId = completedSweepId,
                CycleId = cycleId,
                StrategyId = activeRank.StrategyId,
                StrategyCode = activeRank.StrategyCode,
                StrategyRank = activeRankPosition,
                StrategyCount = strategyCount,
                GrossRealizedPnlUsd = activeRank.GrossRealizedPnlUsd,
                Candidates = page.Candidates.Count,
                EvaluatedForApply = updates.Count,
                TransientLookupUnavailable = transientLookupUnavailable,
                Requested = applyResult?.Requested ?? 0,
                Eligible = applyResult?.Eligible ?? 0,
                FullChainEligible = applyResult?.FullChainEligible ?? 0,
                RunOnlyLegacyEligible = applyResult?.RunOnlyLegacyEligible ?? 0,
                FillsUpdated = applyResult?.FillsUpdated ?? 0,
                RunsUpdated = applyResult?.RunsUpdated ?? 0,
                PositionsUpdated = applyResult?.PositionsUpdated ?? 0,
                SettlementsUpdated = applyResult?.SettlementsUpdated ?? 0,
                FullChainAlreadyApplied = applyResult?.FullChainAlreadyApplied ?? 0,
                RunOnlyLegacyAlreadyApplied = applyResult?.RunOnlyLegacyAlreadyApplied ?? 0,
                AlreadyApplied = applyResult?.AlreadyApplied ?? 0,
                StructuralConflicts = applyResult?.StructuralConflicts ?? 0,
                AccountingConflicts = applyResult?.AccountingConflicts ?? 0,
                DeferredByLockTimeout = applyResult?.DeferredByLockTimeout ?? 0,
                DeferredByQueryCancel = applyResult?.DeferredByQueryCancel ?? 0,
                ReachedStrategyEnd = reachedStrategyEnd,
                ReachedSweepEnd = reachedSweepEnd,
                DurationMilliseconds = GetElapsedMilliseconds(cycleStartedTimestamp)
            },
            cancellationToken).ConfigureAwait(false);

        return new PaperFakFeeBackfillCycleResult(
            page.Candidates.Count,
            updates.Count,
            transientLookupUnavailable,
            reachedSweepEnd,
            options.ApplyEnabled,
            applyResult);
    }

    private async Task EnsureStrategyRanksAsync(
        Guid cycleId,
        CancellationToken cancellationToken)
    {
        if (strategyRanks is not null)
        {
            return;
        }

        var loadedRanks = await repository.GetHistoricalPaperFakFeeBackfillStrategyRanksAsync(
            options.HistoricalCutoffUtc,
            cancellationToken).ConfigureAwait(false);
        ValidateStrategyRanks(loadedRanks);
        sweepId = Guid.NewGuid();
        strategyRanks = loadedRanks.ToArray();
        strategyRankIndex = 0;
        continuationCursor = null;
        netContinuationCursor = null;
        phase = PaperFakFeeBackfillPhase.ExactHistorical;
        transientPaperOrderIds.Clear();

        logger.LogInformation(
            "Historical Paper FAK fee backfill Gross-PnL strategy ranking frozen for this sweep. " +
            "CutoffUtc={CutoffUtc:O} StrategyCount={StrategyCount}",
            options.HistoricalCutoffUtc,
            strategyRanks.Count);
        await TryRecordEventAsync(
            CreateProcessorEvent(
                PaperFakFeeBackfillEventTypes.StrategyRankingFrozen,
                PaperFakFeeBackfillEventLevels.Information,
                "Historical Paper FAK fee backfill Gross-PnL strategy ranking frozen for this sweep.") with
            {
                SweepId = sweepId,
                CycleId = cycleId,
                StrategyCount = strategyRanks.Count
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateStrategyRanks(
        IReadOnlyList<HistoricalPaperFakFeeBackfillStrategyRank> ranks)
    {
        ArgumentNullException.ThrowIfNull(ranks);
        var strategyIds = new HashSet<Guid>();
        decimal? previousGrossRealizedPnlUsd = null;
        foreach (var rank in ranks)
        {
            if (!strategyIds.Add(rank.StrategyId))
            {
                throw new InvalidOperationException(
                    $"Historical Paper FAK fee backfill strategy ranking contains duplicate strategy {rank.StrategyId}.");
            }

            if (previousGrossRealizedPnlUsd is not null &&
                rank.GrossRealizedPnlUsd > previousGrossRealizedPnlUsd.Value)
            {
                throw new InvalidOperationException(
                    "Historical Paper FAK fee backfill strategy ranking is not ordered by " +
                    "Gross realized PnL descending.");
            }

            previousGrossRealizedPnlUsd = rank.GrossRealizedPnlUsd;
        }
    }

    private void ValidatePage(
        HistoricalPaperFakFeeBackfillStrategyRank activeRank,
        HistoricalPaperFakFeeBackfillPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Candidates.Count > options.BatchSize)
        {
            throw new InvalidOperationException(
                "Historical Paper FAK fee backfill repository returned more candidates than requested.");
        }

        if (page.Candidates.Any(candidate => candidate.Order.StrategyId != activeRank.StrategyId))
        {
            throw new InvalidOperationException(
                "Historical Paper FAK fee backfill repository returned a candidate for another strategy.");
        }

        if (page.ContinuationCursor is not null &&
            page.ContinuationCursor.StrategyId != activeRank.StrategyId)
        {
            throw new InvalidOperationException(
                "Historical Paper FAK fee backfill page returned a cursor for another strategy.");
        }

        if (!page.ReachedEnd && page.ContinuationCursor is null)
        {
            throw new InvalidOperationException(
                "Historical Paper FAK fee backfill page did not provide a continuation cursor before sweep end.");
        }

        if (!page.ReachedEnd && page.ContinuationCursor == continuationCursor)
        {
            throw new InvalidOperationException(
                "Historical Paper FAK fee backfill continuation cursor did not advance.");
        }
    }

    private void AdvanceExactPhase(HistoricalPaperFakFeeBackfillPage page)
    {
        if (!page.ReachedEnd)
        {
            continuationCursor = page.ContinuationCursor;
            return;
        }

        continuationCursor = null;
        netContinuationCursor = null;
        phase = PaperFakFeeBackfillPhase.AuthoritativeNetRepair;
    }

    private bool CompleteStrategy()
    {
        continuationCursor = null;
        netContinuationCursor = null;
        phase = PaperFakFeeBackfillPhase.ExactHistorical;
        transientPaperOrderIds.Clear();
        strategyRankIndex++;
        if (strategyRankIndex < strategyRanks!.Count)
        {
            return false;
        }

        ResetSweep();
        return true;
    }

    private void ResetSweep()
    {
        strategyRanks = null;
        sweepId = null;
        strategyRankIndex = 0;
        continuationCursor = null;
        netContinuationCursor = null;
        phase = PaperFakFeeBackfillPhase.ExactHistorical;
        transientPaperOrderIds.Clear();
    }

    private async Task<PaperFakFeeBackfillCycleResult> RunAuthoritativeNetRepairCycleAsync(
        Guid cycleId,
        long cycleStartedTimestamp,
        HistoricalPaperFakFeeBackfillStrategyRank activeRank,
        int activeRankPosition,
        int strategyCount,
        CancellationToken cancellationToken)
    {
        var result = await repository.ApplyHistoricalPaperAuthoritativeNetRepairBatchAsync(
            activeRank.StrategyId,
            options.BatchSize,
            options.ApplyEnabled,
            netContinuationCursor,
            cancellationToken).ConfigureAwait(false);
        ValidateNetPage(activeRank, result.Candidates, result.ReachedEnd, result.ContinuationCursor,
            result.WholeBatchDeferred);

        if (!result.WholeBatchDeferred)
        {
            if (result.ReachedEnd)
            {
                netContinuationCursor = null;
                phase = PaperFakFeeBackfillPhase.NetFallback;
            }
            else
            {
                netContinuationCursor = result.ContinuationCursor;
            }
        }

        logger.LogInformation(
            "Historical Paper authoritative-Fee Net repair cycle completed. " +
            "ApplyEnabled={ApplyEnabled} StrategyRank={StrategyRank}/{StrategyCount} " +
            "StrategyId={StrategyId} StrategyCode={StrategyCode} GrossRealizedPnlUsd={GrossRealizedPnlUsd} " +
            "Candidates={Candidates} RunsUpdated={RunsUpdated} CompareAndSetConflicts={CompareAndSetConflicts} " +
            "DeferredByLockTimeout={DeferredByLockTimeout} DeferredByQueryCancel={DeferredByQueryCancel} " +
            "ReachedPhaseEnd={ReachedPhaseEnd}",
            options.ApplyEnabled,
            activeRankPosition,
            strategyCount,
            activeRank.StrategyId,
            activeRank.StrategyCode,
            activeRank.GrossRealizedPnlUsd,
            result.Candidates,
            result.RunsUpdated,
            result.CompareAndSetConflicts,
            result.DeferredByLockTimeout,
            result.DeferredByQueryCancel,
            !result.WholeBatchDeferred && result.ReachedEnd);

        await RecordNetPhaseCompletedEventAsync(
            cycleId,
            cycleStartedTimestamp,
            activeRank,
            activeRankPosition,
            strategyCount,
            "Historical Paper authoritative-Fee Net repair cycle completed.",
            result.Candidates,
            result.RunsUpdated,
            result.CompareAndSetConflicts,
            result.DeferredByLockTimeout,
            result.DeferredByQueryCancel,
            reachedStrategyEnd: false,
            reachedSweepEnd: false,
            completedSweepId: sweepId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new PaperFakFeeBackfillCycleResult(
            result.Candidates,
            result.Candidates,
            0,
            false,
            options.ApplyEnabled,
            null)
        {
            AuthoritativeNetRepairResult = result
        };
    }

    private async Task<PaperFakFeeBackfillCycleResult> RunNetFallbackCycleAsync(
        Guid cycleId,
        long cycleStartedTimestamp,
        HistoricalPaperFakFeeBackfillStrategyRank activeRank,
        int activeRankPosition,
        int strategyCount,
        CancellationToken cancellationToken)
    {
        var result = await repository.ApplyHistoricalPaperNetFallbackBatchAsync(
            activeRank.StrategyId,
            options.BatchSize,
            options.ApplyEnabled,
            transientPaperOrderIds,
            netContinuationCursor,
            cancellationToken).ConfigureAwait(false);
        ValidateNetPage(activeRank, result.Candidates, result.ReachedEnd, result.ContinuationCursor,
            result.WholeBatchDeferred);

        var reachedStrategyEnd = !result.WholeBatchDeferred && result.ReachedEnd;
        var reachedSweepEnd = false;
        var completedSweepId = sweepId;
        if (!result.WholeBatchDeferred)
        {
            if (result.ReachedEnd)
            {
                reachedSweepEnd = CompleteStrategy();
            }
            else
            {
                netContinuationCursor = result.ContinuationCursor;
            }
        }

        logger.LogInformation(
            "Historical Paper same-strategy fee-ratio Net fallback cycle completed. " +
            "ApplyEnabled={ApplyEnabled} StrategyRank={StrategyRank}/{StrategyCount} " +
            "StrategyId={StrategyId} StrategyCode={StrategyCode} GrossRealizedPnlUsd={GrossRealizedPnlUsd} " +
            "Candidates={Candidates} DonorAvailable={DonorAvailable} ExactDonorCount={ExactDonorCount} " +
            "FeeToStakeRatio={FeeToStakeRatio} RunsUpdated={RunsUpdated} " +
            "CompareAndSetConflicts={CompareAndSetConflicts} DeferredByLockTimeout={DeferredByLockTimeout} " +
            "DeferredByQueryCancel={DeferredByQueryCancel} ReachedStrategyEnd={ReachedStrategyEnd} " +
            "ReachedSweepEnd={ReachedSweepEnd}",
            options.ApplyEnabled,
            activeRankPosition,
            strategyCount,
            activeRank.StrategyId,
            activeRank.StrategyCode,
            activeRank.GrossRealizedPnlUsd,
            result.Candidates,
            result.DonorAvailable,
            result.ExactDonorCount,
            result.FeeToStakeRatio,
            result.RunsUpdated,
            result.CompareAndSetConflicts,
            result.DeferredByLockTimeout,
            result.DeferredByQueryCancel,
            reachedStrategyEnd,
            reachedSweepEnd);

        await RecordNetPhaseCompletedEventAsync(
            cycleId,
            cycleStartedTimestamp,
            activeRank,
            activeRankPosition,
            strategyCount,
            "Historical Paper same-strategy fee-ratio Net fallback cycle completed.",
            result.Candidates,
            result.RunsUpdated,
            result.CompareAndSetConflicts,
            result.DeferredByLockTimeout,
            result.DeferredByQueryCancel,
            reachedStrategyEnd,
            reachedSweepEnd,
            completedSweepId,
            cancellationToken).ConfigureAwait(false);

        return new PaperFakFeeBackfillCycleResult(
            result.Candidates,
            result.Candidates,
            0,
            reachedSweepEnd,
            options.ApplyEnabled,
            null)
        {
            NetFallbackResult = result
        };
    }

    private void ValidateNetPage(
        HistoricalPaperFakFeeBackfillStrategyRank activeRank,
        int candidates,
        bool reachedEnd,
        HistoricalPaperNetRunCursor? nextCursor,
        bool wholeBatchDeferred)
    {
        if (candidates < 0 || candidates > options.BatchSize)
        {
            throw new InvalidOperationException(
                "Historical Paper Net backfill repository returned an invalid candidate count.");
        }

        if (nextCursor is not null && nextCursor.StrategyId != activeRank.StrategyId)
        {
            throw new InvalidOperationException(
                "Historical Paper Net backfill page returned a cursor for another strategy.");
        }

        if (wholeBatchDeferred)
        {
            return;
        }

        if (!reachedEnd && nextCursor is null)
        {
            throw new InvalidOperationException(
                "Historical Paper Net backfill page did not provide a continuation cursor before phase end.");
        }

        if (!reachedEnd && nextCursor == netContinuationCursor)
        {
            throw new InvalidOperationException(
                "Historical Paper Net backfill continuation cursor did not advance.");
        }
    }

    private Task RecordNetPhaseCompletedEventAsync(
        Guid cycleId,
        long cycleStartedTimestamp,
        HistoricalPaperFakFeeBackfillStrategyRank activeRank,
        int activeRankPosition,
        int strategyCount,
        string message,
        int candidates,
        int runsUpdated,
        int compareAndSetConflicts,
        int deferredByLockTimeout,
        int deferredByQueryCancel,
        bool reachedStrategyEnd,
        bool reachedSweepEnd,
        Guid? completedSweepId,
        CancellationToken cancellationToken)
    {
        return TryRecordEventAsync(
            CreateProcessorEvent(
                PaperFakFeeBackfillEventTypes.CycleCompleted,
                PaperFakFeeBackfillEventLevels.Information,
                message) with
            {
                SweepId = completedSweepId,
                CycleId = cycleId,
                StrategyId = activeRank.StrategyId,
                StrategyCode = activeRank.StrategyCode,
                StrategyRank = activeRankPosition,
                StrategyCount = strategyCount,
                GrossRealizedPnlUsd = activeRank.GrossRealizedPnlUsd,
                Candidates = candidates,
                EvaluatedForApply = candidates,
                Requested = candidates,
                RunsUpdated = runsUpdated,
                AccountingConflicts = compareAndSetConflicts,
                DeferredByLockTimeout = deferredByLockTimeout,
                DeferredByQueryCancel = deferredByQueryCancel,
                ReachedStrategyEnd = reachedStrategyEnd,
                ReachedSweepEnd = reachedSweepEnd,
                DurationMilliseconds = GetElapsedMilliseconds(cycleStartedTimestamp)
            },
            cancellationToken);
    }

    private static bool IsTransientLookupUnavailable(PaperFill fill)
    {
        return string.Equals(
            fill.FeeCalculationSource,
            PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource,
            StringComparison.Ordinal);
    }

    private static string AddHistoricalProvenance(string? calculationSource)
    {
        if (!string.IsNullOrEmpty(calculationSource) &&
            calculationSource.StartsWith(HistoricalCalculationSourcePrefix, StringComparison.Ordinal))
        {
            return calculationSource;
        }

        return HistoricalCalculationSourcePrefix + (calculationSource ?? string.Empty);
    }

    private PaperFakFeeBackfillEvent CreateProcessorEvent(
        string eventType,
        string level,
        string message)
    {
        return new PaperFakFeeBackfillEvent
        {
            EventType = eventType,
            Level = level,
            Message = message,
            BackfillEnabled = options.Enabled,
            ApplyEnabled = options.ApplyEnabled,
            CutoffUtc = options.HistoricalCutoffUtc,
            BatchSize = options.BatchSize
        };
    }

    private async Task TryRecordEventAsync(
        PaperFakFeeBackfillEvent entry,
        CancellationToken cancellationToken)
    {
        if (eventRecorder is null)
        {
            return;
        }

        try
        {
            await eventRecorder.RecordAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Historical Paper FAK fee backfill database-event recorder failed unexpectedly. " +
                "EventType={EventType}. File logging remains active.",
                entry.EventType);
        }
    }

    private static long GetElapsedMilliseconds(long startedTimestamp)
    {
        return (long)Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
    }
}

public sealed record PaperFakFeeBackfillCycleResult(
    int Candidates,
    int EvaluatedForApply,
    int TransientLookupUnavailable,
    bool ReachedEnd,
    bool ApplyEnabled,
    HistoricalPaperFakFeeBackfillBatchResult? ApplyResult)
{
    public HistoricalPaperAuthoritativeNetRepairBatchResult? AuthoritativeNetRepairResult { get; init; }

    public HistoricalPaperNetFallbackBatchResult? NetFallbackResult { get; init; }
}

internal enum PaperFakFeeBackfillPhase
{
    ExactHistorical,
    AuthoritativeNetRepair,
    NetFallback
}

public interface IHistoricalGrossNetParityProcessor
{
    Task<HistoricalGrossNetParityCycleResult> RunCycleAsync(
        Guid workerCycleId,
        CancellationToken cancellationToken = default);
}

public enum HistoricalGrossNetParityCycleState
{
    Disabled,
    Idle,
    ExactPageProcessed,
    ExactBoundaryReached,
    FallbackPageProcessed,
    StrategyCompleted,
    Deferred
}

public sealed record HistoricalGrossNetParityCycleResult(
    HistoricalGrossNetParityCycleState State,
    bool ReachedEnd,
    HistoricalGrossNetParityProcessingPhase Phase,
    int Candidates,
    int Applied,
    int TerminalNoOps,
    int FallbackEligible,
    int Deferred,
    int LookupAttemptsThisCycle,
    int DonorTargets,
    int LiveBalancesApplied,
    string Details);

internal sealed class HistoricalGrossNetParityProcessor : IHistoricalGrossNetParityProcessor
{
    private readonly ILogger<HistoricalGrossNetParityProcessor> logger;
    private readonly HistoricalGrossNetParityOptions options;
    private readonly IHistoricalGrossNetParityStore store;
    private readonly IPolymarketFeeAccountingService feeAccountingService;
    private readonly IPaperFakFeeBackfillEventRecorder? eventRecorder;
    private readonly SemaphoreSlim cycleGate = new(1, 1);
    private readonly Dictionary<TargetLookupIdentity, TargetLookupLedger> lookupLedgers = [];
    private HistoricalGrossNetParityProcessingPhase phase = HistoricalGrossNetParityProcessingPhase.Exact;
    private HistoricalGrossNetParityCandidateCursor? cursor;
    private Guid? activeStrategyId;
    private bool fallbackPassDeferred;

    private HistoricalGrossNetParityProcessor(
        ILogger<HistoricalGrossNetParityProcessor> logger,
        HistoricalGrossNetParityOptions options,
        IHistoricalGrossNetParityStore store,
        IPolymarketFeeAccountingService feeAccountingService,
        IPaperFakFeeBackfillEventRecorder? eventRecorder = null)
    {
        this.logger = logger;
        this.options = options;
        this.store = store;
        this.feeAccountingService = feeAccountingService;
        this.eventRecorder = eventRecorder;
    }

    internal static HistoricalGrossNetParityProcessor Create(
        IServiceProvider serviceProvider,
        HistoricalGrossNetParityOptions options)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(options);
        return new HistoricalGrossNetParityProcessor(
            serviceProvider.GetRequiredService<ILogger<HistoricalGrossNetParityProcessor>>(),
            options,
            serviceProvider.GetRequiredService<IAppRepository>(),
            serviceProvider.GetRequiredService<IPolymarketFeeAccountingService>(),
            serviceProvider.GetService<IPaperFakFeeBackfillEventRecorder>());
    }

    internal static HistoricalGrossNetParityProcessor CreateForTests(
        ILogger<HistoricalGrossNetParityProcessor> logger,
        HistoricalGrossNetParityOptions options,
        IHistoricalGrossNetParityStore store,
        IPolymarketFeeAccountingService feeAccountingService,
        IPaperFakFeeBackfillEventRecorder? eventRecorder = null) =>
        new(logger, options, store, feeAccountingService, eventRecorder);

    public async Task<HistoricalGrossNetParityCycleResult> RunCycleAsync(
        Guid workerCycleId,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return Result(
                HistoricalGrossNetParityCycleState.Disabled,
                reachedEnd: true,
                details: "Historical Gross/Net parity is disabled.");
        }

        if (workerCycleId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty worker cycle ID is required.", nameof(workerCycleId));
        }

        ValidateRuntimeOptions();
        await cycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requestedPhase = phase;
            var page = await store.LoadHistoricalGrossNetParityCandidatePageAsync(
                    new HistoricalGrossNetParityCandidatePageRequest(
                        requestedPhase,
                        options.HistoricalCutoffUtc,
                        options.BatchSize,
                        cursor,
                        options.CommandTimeoutSeconds,
                        options.LockTimeoutMilliseconds,
                        options.CalculationVersion,
                        activeStrategyId),
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateCandidatePage(page);
            if (page.Status != HistoricalGrossNetParityReadStatus.Complete)
            {
                logger.LogWarning(
                    "Historical Gross/Net parity candidate page deferred. Phase={Phase} Status={Status} " +
                    "After={After} Details={Details}",
                    requestedPhase,
                    page.Status,
                    cursor,
                    page.Details);
                return Result(
                    HistoricalGrossNetParityCycleState.Deferred,
                    reachedEnd: false,
                    details: page.Details);
            }

            if (activeStrategyId is null && page.Candidates.Count != 0)
            {
                activeStrategyId = page.Candidates[0].StrategyId;
                logger.LogInformation(
                    "Historical Gross/Net parity selected the greatest-current-Gross unfinished strategy. " +
                    "StrategyId={StrategyId} StrategyCode={StrategyCode} StrategyRank={StrategyRank} Gross={Gross}",
                    activeStrategyId,
                    page.Candidates[0].StrategyCode,
                    page.Candidates[0].StrategyRank,
                    page.Candidates[0].StrategyGrossPnlUsd);

                if (page.Candidates.Any(candidate => candidate.StrategyId != activeStrategyId.Value))
                {
                    page = await store.LoadHistoricalGrossNetParityCandidatePageAsync(
                            new HistoricalGrossNetParityCandidatePageRequest(
                                requestedPhase,
                                options.HistoricalCutoffUtc,
                                options.BatchSize,
                                null,
                                options.CommandTimeoutSeconds,
                                options.LockTimeoutMilliseconds,
                                options.CalculationVersion,
                                activeStrategyId),
                            cancellationToken)
                        .ConfigureAwait(false);
                    ValidateCandidatePage(page);
                    if (page.Status != HistoricalGrossNetParityReadStatus.Complete)
                    {
                        logger.LogWarning(
                            "Historical Gross/Net parity selected-strategy page deferred. " +
                            "Phase={Phase} StrategyId={StrategyId} Status={Status} Details={Details}",
                            requestedPhase,
                            activeStrategyId,
                            page.Status,
                            page.Details);
                        return Result(
                            HistoricalGrossNetParityCycleState.Deferred,
                            reachedEnd: false,
                            details: page.Details);
                    }
                }
            }

            if (activeStrategyId is not null &&
                page.Candidates.Any(candidate => candidate.StrategyId != activeStrategyId.Value))
            {
                throw new InvalidOperationException(
                    $"A strategy-scoped parity page for {activeStrategyId:D} contains another strategy.");
            }

            if (activeStrategyId is null)
            {
                phase = HistoricalGrossNetParityProcessingPhase.Exact;
                cursor = null;
                fallbackPassDeferred = false;
                PruneTerminalLookupLedgers();
                return Result(
                    HistoricalGrossNetParityCycleState.Idle,
                    reachedEnd: true,
                    details: page.Details);
            }

            var prepared = HistoricalGrossNetParityPaperPreparer.Prepare(page, options.HistoricalCutoffUtc);
            var counters = await ProcessPageAsync(
                    workerCycleId,
                    requestedPhase,
                    page,
                    prepared,
                    cancellationToken)
                .ConfigureAwait(false);
            cursor = page.NextCursor;

            if (requestedPhase == HistoricalGrossNetParityProcessingPhase.Fallback &&
                counters.Deferred != 0)
            {
                fallbackPassDeferred = true;
            }

            if (!page.ReachedBoundary)
            {
                return Result(
                    requestedPhase == HistoricalGrossNetParityProcessingPhase.Exact
                        ? HistoricalGrossNetParityCycleState.ExactPageProcessed
                        : HistoricalGrossNetParityCycleState.FallbackPageProcessed,
                    reachedEnd: false,
                    counters,
                    page.Details);
            }

            if (requestedPhase == HistoricalGrossNetParityProcessingPhase.Exact)
            {
                phase = HistoricalGrossNetParityProcessingPhase.Fallback;
                cursor = null;
                fallbackPassDeferred = false;
                logger.LogInformation(
                    "Historical Gross/Net parity exact/authoritative/local pass reached the active-strategy boundary. " +
                    "Fallback donor work for the same strategy begins on the next bounded cycle. " +
                    "StrategyId={StrategyId} Candidates={Candidates} Applied={Applied} " +
                    "FallbackEligible={FallbackEligible} Deferred={Deferred}",
                    activeStrategyId,
                    counters.Candidates,
                    counters.Applied,
                    counters.FallbackEligible,
                    counters.Deferred);
                return Result(
                    HistoricalGrossNetParityCycleState.ExactBoundaryReached,
                    reachedEnd: false,
                    counters,
                    page.Details,
                    requestedPhase);
            }

            if (fallbackPassDeferred)
            {
                phase = HistoricalGrossNetParityProcessingPhase.Exact;
                cursor = null;
                fallbackPassDeferred = false;
                logger.LogWarning(
                    "Historical Gross/Net parity keeps the active strategy selected because at least one " +
                    "target was deferred. StrategyId={StrategyId}",
                    activeStrategyId);
                return Result(
                    HistoricalGrossNetParityCycleState.Deferred,
                    reachedEnd: false,
                    counters,
                    "The active strategy has deferred targets and will be retried before another strategy.",
                    requestedPhase);
            }

            var completedStrategyId = activeStrategyId.Value;
            phase = HistoricalGrossNetParityProcessingPhase.Exact;
            cursor = null;
            activeStrategyId = null;
            fallbackPassDeferred = false;
            PruneTerminalLookupLedgers();
            logger.LogInformation(
                "Historical Gross/Net parity completed the active strategy before selecting another. " +
                "StrategyId={StrategyId}",
                completedStrategyId);
            return Result(
                HistoricalGrossNetParityCycleState.StrategyCompleted,
                reachedEnd: false,
                counters,
                page.Details,
                requestedPhase);
        }
        finally
        {
            cycleGate.Release();
        }
    }

    private async Task<PageCounters> ProcessPageAsync(
        Guid workerCycleId,
        HistoricalGrossNetParityProcessingPhase requestedPhase,
        HistoricalGrossNetParityCandidatePage page,
        HistoricalGrossNetParityPreparedPage prepared,
        CancellationToken cancellationToken)
    {
        var counters = new PageCounters { Candidates = page.Candidates.Count };
        var conflictsByTarget = prepared.Conflicts
            .Where(conflict => conflict.SourceKind is not null && conflict.SourceId is not null)
            .GroupBy(conflict => (conflict.SourceKind!.Value, conflict.SourceId!.Value))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var pageWideConflicts = prepared.Conflicts
            .Where(conflict => conflict.SourceKind is null || conflict.SourceId is null)
            .ToArray();
        if (pageWideConflicts.Length != 0)
        {
            logger.LogError(
                "Historical Gross/Net parity page has {ConflictCount} unscoped invariant conflicts; " +
                "the page is deferred without target mutation. Conflicts={Conflicts}",
                pageWideConflicts.Length,
                JsonSerializer.Serialize(pageWideConflicts));
            counters.Deferred = page.Candidates.Count;
            return counters;
        }

        var targets = prepared.Targets.ToDictionary(target => (target.SourceKind, target.SourceId));
        var lookupsByTarget = prepared.LookupRequests
            .GroupBy(request => (request.SourceKind, request.SourceId))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<HistoricalGrossNetParityLookupRequest>)group
                    .OrderBy(request => request.TupleHash, StringComparer.Ordinal)
                    .ToArray());

        foreach (var candidate in page.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = (candidate.SourceKind, candidate.SourceId);
            if (conflictsByTarget.TryGetValue(identity, out var targetConflicts))
            {
                counters.Deferred++;
                logger.LogError(
                    "Historical Gross/Net parity target deferred on replay/lineage conflict. " +
                    "Phase={Phase} SourceKind={SourceKind} SourceId={SourceId} StrategyId={StrategyId} " +
                    "Conflicts={Conflicts}",
                    requestedPhase,
                    candidate.SourceKind,
                    candidate.SourceId,
                    candidate.StrategyId,
                    JsonSerializer.Serialize(targetConflicts));
                continue;
            }

            if (!targets.TryGetValue(identity, out var target))
            {
                counters.Deferred++;
                logger.LogError(
                    "Historical Gross/Net parity target preparation produced no canonical target. " +
                    "Phase={Phase} SourceKind={SourceKind} SourceId={SourceId} StrategyId={StrategyId}",
                    requestedPhase,
                    candidate.SourceKind,
                    candidate.SourceId,
                    candidate.StrategyId);
                continue;
            }

            var requests = lookupsByTarget.GetValueOrDefault(identity) ?? [];
            if (requestedPhase == HistoricalGrossNetParityProcessingPhase.Exact)
            {
                await ProcessExactTargetAsync(
                        workerCycleId,
                        candidate,
                        target,
                        requests,
                        counters,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await ProcessFallbackTargetAsync(
                        workerCycleId,
                        candidate,
                        target,
                        requests,
                        counters,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (counters.Applied != 0)
        {
            await TryRecordParityEventAsync(
                    new PaperFakFeeBackfillEvent
                    {
                        EventType = PaperFakFeeBackfillEventTypes.ParityPageCompleted,
                        Level = PaperFakFeeBackfillEventLevels.Information,
                        Message = "Historical Gross/Net parity incremental page completed after target commits.",
                        CycleId = workerCycleId,
                        BackfillEnabled = options.Enabled,
                        CutoffUtc = options.HistoricalCutoffUtc,
                        BatchSize = options.BatchSize,
                        Candidates = counters.Candidates,
                        EvaluatedForApply = counters.Applied + counters.TerminalNoOps,
                        AlreadyApplied = counters.TerminalNoOps,
                        AccountingConflicts = counters.Deferred,
                        ReachedSweepEnd = page.ReachedBoundary
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        logger.LogInformation(
            "Historical Gross/Net parity incremental page completed. Phase={Phase} Candidates={Candidates} " +
            "Applied={Applied} TerminalNoOps={TerminalNoOps} FallbackEligible={FallbackEligible} " +
            "Deferred={Deferred} LookupAttempts={LookupAttempts} DonorTargets={DonorTargets} " +
            "LiveBalancesApplied={LiveBalancesApplied} ReachedBoundary={ReachedBoundary}",
            requestedPhase,
            counters.Candidates,
            counters.Applied,
            counters.TerminalNoOps,
            counters.FallbackEligible,
            counters.Deferred,
            counters.LookupAttempts,
            counters.DonorTargets,
            counters.LiveBalancesApplied,
            page.ReachedBoundary);
        return counters;
    }

    private async Task ProcessExactTargetAsync(
        Guid workerCycleId,
        HistoricalGrossNetParityCandidateKey candidate,
        HistoricalGrossNetParityTargetSnapshot target,
        IReadOnlyList<HistoricalGrossNetParityLookupRequest> requests,
        PageCounters counters,
        CancellationToken cancellationToken)
    {
        if (target.ExactEligibility == HistoricalGrossNetParityExactEligibility.InvariantConflict)
        {
            counters.Deferred++;
            return;
        }

        var resolution = await ResolveTargetLookupsAsync(
                workerCycleId,
                target,
                requests,
                cancellationToken)
            .ConfigureAwait(false);
        counters.LookupAttempts += resolution.AttemptsThisCycle;
        if (resolution.ProtocolInvariantConflict || !resolution.IsClosed)
        {
            counters.Deferred++;
            return;
        }

        if (resolution.RequiresFallback ||
            target.ExactEligibility == HistoricalGrossNetParityExactEligibility.FallbackRequired)
        {
            counters.FallbackEligible++;
            return;
        }

        target = HistoricalGrossNetParityDecisionFactory.WithLookupEvidence(
            target,
            resolution.Outcomes);
        var decision = HistoricalGrossNetParityDecisionFactory.TryCreateExact(
            target,
            resolution.Outcomes,
            DateTimeOffset.UtcNow,
            options.CalculationVersion);
        if (decision is null)
        {
            counters.Deferred++;
            return;
        }

        var apply = await ApplyDecisionAsync(
                workerCycleId,
                candidate,
                target,
                decision,
                [],
                counters,
                cancellationToken)
            .ConfigureAwait(false);
        if (apply.Status is HistoricalGrossNetParityApplyStatus.Applied or
            HistoricalGrossNetParityApplyStatus.TerminalNoOp)
        {
            RemoveLookupLedgers(target.SourceKind, target.SourceId);
        }
    }

    private async Task ProcessFallbackTargetAsync(
        Guid workerCycleId,
        HistoricalGrossNetParityCandidateKey candidate,
        HistoricalGrossNetParityTargetSnapshot target,
        IReadOnlyList<HistoricalGrossNetParityLookupRequest> requests,
        PageCounters counters,
        CancellationToken cancellationToken)
    {
        if (!IsFallbackEligible(target, requests))
        {
            counters.Deferred++;
            return;
        }

        if (requests.Count != 0)
        {
            var identity = TargetLookupIdentity.Create(target, requests);
            if (!lookupLedgers.TryGetValue(identity, out var ledger) ||
                !ledger.IsClosed ||
                ledger.ProtocolInvariantConflict)
            {
                counters.Deferred++;
                return;
            }

            target = HistoricalGrossNetParityDecisionFactory.WithLookupEvidence(
                target,
                ledger.Outcomes.Values
                    .OrderBy(outcome => outcome.TupleHash, StringComparer.Ordinal)
                    .ToArray());
        }

        var decision = HistoricalGrossNetParityDecisionFactory.CreateFallback(
            target,
            DateTimeOffset.UtcNow,
            options.CalculationVersion);
        var apply = await ApplyDecisionAsync(
                workerCycleId,
                candidate,
                target,
                decision,
                [],
                counters,
                cancellationToken)
            .ConfigureAwait(false);
        if (apply.Status is HistoricalGrossNetParityApplyStatus.Applied or
            HistoricalGrossNetParityApplyStatus.TerminalNoOp)
        {
            RemoveLookupLedgers(target.SourceKind, target.SourceId);
        }
    }

    private async Task<HistoricalGrossNetParityApplyResult> ApplyDecisionAsync(
        Guid workerCycleId,
        HistoricalGrossNetParityCandidateKey candidate,
        HistoricalGrossNetParityTargetSnapshot target,
        HistoricalGrossNetParityAccountingDecisionV1 decision,
        IReadOnlyList<HistoricalGrossNetDonorCandidateDescriptorV1> orderedCandidates,
        PageCounters counters,
        CancellationToken cancellationToken)
    {
        HistoricalGrossNetParityApplyResult apply;
        if (target.SourceKind == HistoricalGrossNetParitySourceKind.LiveOrder)
        {
            apply = await store.TryApplyHistoricalGrossNetParityLiveAccountingAsync(
                    new HistoricalGrossNetParityLiveAccountingRequest(
                        target,
                        decision,
                        orderedCandidates,
                        options.HistoricalCutoffUtc,
                        options.BatchSize,
                        options.CommandTimeoutSeconds,
                        options.LockTimeoutMilliseconds,
                        options.CalculationVersion),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            apply = await store.TryApplyHistoricalGrossNetParityPaperDecisionAsync(
                    new HistoricalGrossNetParityPaperDecisionRequest(
                        target,
                        decision,
                        orderedCandidates,
                        options.HistoricalCutoffUtc,
                        options.BatchSize,
                        options.CommandTimeoutSeconds,
                        options.LockTimeoutMilliseconds,
                        options.CalculationVersion),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        switch (apply.Status)
        {
            case HistoricalGrossNetParityApplyStatus.Applied:
                counters.Applied++;
                await TryRecordParityEventAsync(
                        new PaperFakFeeBackfillEvent
                        {
                            EventType = PaperFakFeeBackfillEventTypes.ParityTargetCommitted,
                            Level = PaperFakFeeBackfillEventLevels.Information,
                            Message = "Historical Gross/Net parity target transaction committed.",
                            CycleId = workerCycleId,
                            BackfillEnabled = options.Enabled,
                            CutoffUtc = options.HistoricalCutoffUtc,
                            BatchSize = options.BatchSize,
                            StrategyId = candidate.StrategyId,
                            StrategyCode = candidate.StrategyCode,
                            StrategyRank = candidate.StrategyRank,
                            GrossRealizedPnlUsd = candidate.StrategyGrossPnlUsd,
                            Candidates = 1,
                            EvaluatedForApply = 1,
                            ReachedSweepEnd = false
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case HistoricalGrossNetParityApplyStatus.TerminalNoOp:
                counters.TerminalNoOps++;
                break;
            default:
                counters.Deferred++;
                break;
        }

        if (target.SourceKind == HistoricalGrossNetParitySourceKind.LiveOrder &&
            (apply.Status is HistoricalGrossNetParityApplyStatus.Applied or
                HistoricalGrossNetParityApplyStatus.TerminalNoOp) &&
            apply.Ownership == HistoricalGrossNetParityOwnership.Pending)
        {
            var balance = await store.TryApplyHistoricalGrossNetParityEarliestLiveBalanceAsync(
                    new HistoricalGrossNetParityLiveBalanceRequest(
                        target.StrategyId,
                        target.SourceId,
                        options.HistoricalCutoffUtc,
                        options.CommandTimeoutSeconds,
                        options.LockTimeoutMilliseconds,
                        options.CalculationVersion),
                    cancellationToken)
                .ConfigureAwait(false);
            if (balance.Status == HistoricalGrossNetParityApplyStatus.Applied)
            {
                counters.LiveBalancesApplied++;
            }
            else if (balance.Status != HistoricalGrossNetParityApplyStatus.TerminalNoOp)
            {
                counters.Deferred++;
                logger.LogWarning(
                    "Historical Gross/Net parity Live balance remains unfinished. " +
                    "StrategyId={StrategyId} LiveOrderId={LiveOrderId} Status={Status} Details={Details}",
                    target.StrategyId,
                    target.SourceId,
                    balance.Status,
                    balance.Details);
            }
        }

        return apply;
    }

    private async Task<TargetLookupResolution> ResolveTargetLookupsAsync(
        Guid workerCycleId,
        HistoricalGrossNetParityTargetSnapshot target,
        IReadOnlyList<HistoricalGrossNetParityLookupRequest> requests,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return TargetLookupResolution.Closed([],
                target.ExactEligibility == HistoricalGrossNetParityExactEligibility.FallbackRequired);
        }

        var identity = TargetLookupIdentity.Create(target, requests);
        RemoveChangedLookupLedgers(identity);
        if (!lookupLedgers.TryGetValue(identity, out var ledger))
        {
            ledger = new TargetLookupLedger();
            lookupLedgers.Add(identity, ledger);
        }

        if (ledger.IsClosed)
        {
            return TargetLookupResolution.Closed(ledger.Outcomes.Values.ToArray(), ledger.RequiresFallback);
        }

        if (ledger.LastOperationalCycleId == workerCycleId && ledger.LastOperationalOutcomes.Count != 0)
        {
            return TargetLookupResolution.Open(ledger.LastOperationalOutcomes.Values.ToArray());
        }

        var current = new Dictionary<string, HistoricalGrossNetParityLookupOutcome>(StringComparer.Ordinal);
        var operational = false;
        foreach (var request in requests)
        {
            if (ledger.Outcomes.TryGetValue(request.TupleHash, out var cached))
            {
                current.Add(request.TupleHash, cached);
                continue;
            }

            var outcome = await ExecuteLookupAsync(request, cancellationToken).ConfigureAwait(false);
            current.Add(request.TupleHash, outcome);
            switch (outcome.Status)
            {
                case HistoricalGrossNetParityLookupOutcomeStatus.OperationalFailure:
                    operational = true;
                    break;
                case HistoricalGrossNetParityLookupOutcomeStatus.ProtocolInvariantConflict:
                    ledger.ProtocolInvariantConflict = true;
                    break;
                default:
                    ledger.Outcomes[request.TupleHash] = outcome;
                    break;
            }
        }

        if (ledger.ProtocolInvariantConflict)
        {
            return TargetLookupResolution.Invariant(current.Values.ToArray());
        }

        var attemptsThisCycle = 0;
        if (operational)
        {
            if (ledger.LastOperationalCycleId != workerCycleId)
            {
                ledger.OperationalAttempts++;
                attemptsThisCycle = 1;
                ledger.LastOperationalCycleId = workerCycleId;
            }

            ledger.LastOperationalOutcomes.Clear();
            foreach (var outcome in current.Values.Where(value =>
                         value.Status == HistoricalGrossNetParityLookupOutcomeStatus.OperationalFailure))
            {
                ledger.LastOperationalOutcomes[outcome.TupleHash] = outcome;
            }

            if (ledger.OperationalAttempts < options.LookupMaxAttempts)
            {
                return TargetLookupResolution.Open(current.Values.ToArray(), attemptsThisCycle);
            }

            foreach (var outcome in ledger.LastOperationalOutcomes.Values)
            {
                ledger.Outcomes[outcome.TupleHash] = outcome with
                {
                    Status = HistoricalGrossNetParityLookupOutcomeStatus.HistoricalLookupExhausted,
                    CalculationSource = "historical-clob-lookup-exhausted-v1",
                    CapturedAtUtc = DateTimeOffset.UtcNow,
                    EvidenceJson = JsonSerializer.Serialize(new
                    {
                        reason = "three-distinct-worker-cycles-exhausted",
                        attempts = ledger.OperationalAttempts,
                        last = outcome.EvidenceJson
                    })
                };
            }
        }

        ledger.LastOperationalOutcomes.Clear();
        ledger.IsClosed = requests.All(request => ledger.Outcomes.ContainsKey(request.TupleHash));
        ledger.RequiresFallback = ledger.IsClosed && ledger.Outcomes.Values.Any(outcome =>
            outcome.Status != HistoricalGrossNetParityLookupOutcomeStatus.Success);
        return ledger.IsClosed
            ? TargetLookupResolution.Closed(
                ledger.Outcomes.Values.OrderBy(value => value.TupleHash, StringComparer.Ordinal).ToArray(),
                ledger.RequiresFallback,
                attemptsThisCycle)
            : TargetLookupResolution.Open(current.Values.ToArray(), attemptsThisCycle);
    }

    private async Task<HistoricalGrossNetParityLookupOutcome> ExecuteLookupAsync(
        HistoricalGrossNetParityLookupRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.LookupTimeoutSeconds));
        HistoricalFeeLookupResult result;
        try
        {
            var liquidityRoleIsValid = Enum.TryParse<FeeLiquidityRole>(
                request.LiquidityRole,
                ignoreCase: false,
                out var liquidityRole) &&
                Enum.IsDefined(liquidityRole) &&
                string.Equals(liquidityRole.ToString(), request.LiquidityRole, StringComparison.Ordinal);
            result = await feeAccountingService.CalculateHistoricalFeeAsync(
                    new HistoricalFeeLookupRequest(
                        request.ConditionId,
                        request.Quantity,
                        request.Price,
                        liquidityRole,
                        liquidityRoleIsValid),
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HistoricalGrossNetParityLookupOutcome(
                request.TupleHash,
                HistoricalGrossNetParityLookupOutcomeStatus.OperationalFailure,
                null,
                "historical-clob-lookup-timeout-v1",
                request.LiquidityRole,
                null,
                null,
                null,
                request.FeeApplicationKind,
                request.FeeAllocationId,
                request.FeeSourceChargeId,
                DateTimeOffset.UtcNow,
                JsonSerializer.Serialize(new
                {
                    reason = "lookup-timeout",
                    timeoutSeconds = options.LookupTimeoutSeconds
                }));
        }

        var status = result.Disposition switch
        {
            HistoricalFeeLookupDisposition.Calculated => HistoricalGrossNetParityLookupOutcomeStatus.Success,
            HistoricalFeeLookupDisposition.ProvedMarketAbsent => HistoricalGrossNetParityLookupOutcomeStatus.Proved404,
            HistoricalFeeLookupDisposition.SemanticUnavailable =>
                HistoricalGrossNetParityLookupOutcomeStatus.SemanticUnavailable,
            HistoricalFeeLookupDisposition.OperationalFailure =>
                HistoricalGrossNetParityLookupOutcomeStatus.OperationalFailure,
            HistoricalFeeLookupDisposition.ProtocolInvariantConflict =>
                HistoricalGrossNetParityLookupOutcomeStatus.ProtocolInvariantConflict,
            _ => throw new InvalidOperationException(
                $"Unknown historical Fee lookup disposition '{result.Disposition}'.")
        };
        return new HistoricalGrossNetParityLookupOutcome(
            request.TupleHash,
            status,
            result.FeeUsd,
            result.CalculationSource,
            result.FeeLiquidityRole,
            result.FeeRate,
            result.FeeExponent,
            result.FeeTakerOnly,
            request.FeeApplicationKind,
            request.FeeAllocationId,
            request.FeeSourceChargeId,
            result.CalculatedAtUtc,
            JsonSerializer.Serialize(new
            {
                result.HttpStatusCode,
                result.Evidence,
                result.MarketEvidence,
                request.SourceKind,
                request.SourceId,
                request.StrategyId
            }));
    }

    private bool IsFallbackEligible(
        HistoricalGrossNetParityTargetSnapshot target,
        IReadOnlyList<HistoricalGrossNetParityLookupRequest> requests)
    {
        if (target.ExactEligibility == HistoricalGrossNetParityExactEligibility.FallbackRequired &&
            requests.Count == 0)
        {
            return true;
        }

        if (target.ExactEligibility != HistoricalGrossNetParityExactEligibility.LocalLookupRequired ||
            requests.Count == 0)
        {
            return false;
        }

        var identity = TargetLookupIdentity.Create(target, requests);
        return lookupLedgers.TryGetValue(identity, out var ledger) &&
            ledger.IsClosed &&
            ledger.RequiresFallback &&
            !ledger.ProtocolInvariantConflict;
    }

    private void RemoveChangedLookupLedgers(TargetLookupIdentity current)
    {
        foreach (var identity in lookupLedgers.Keys.Where(identity =>
                     identity.SourceKind == current.SourceKind &&
                     identity.SourceId == current.SourceId &&
                     identity != current).ToArray())
        {
            lookupLedgers.Remove(identity);
        }
    }

    private void RemoveLookupLedgers(HistoricalGrossNetParitySourceKind sourceKind, Guid sourceId)
    {
        foreach (var identity in lookupLedgers.Keys.Where(identity =>
                     identity.SourceKind == sourceKind && identity.SourceId == sourceId).ToArray())
        {
            lookupLedgers.Remove(identity);
        }
    }

    private void PruneTerminalLookupLedgers()
    {
        foreach (var identity in lookupLedgers
                     .Where(entry => entry.Value.IsClosed && !entry.Value.RequiresFallback)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            lookupLedgers.Remove(identity);
        }
    }

    private void ValidateRuntimeOptions()
    {
        var errors = AppOptionsValidator.ValidateHistoricalGrossNetParity(options);
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                "Invalid HistoricalGrossNetParity configuration: " + string.Join("; ", errors));
        }
    }

    private void ValidateCandidatePage(
        HistoricalGrossNetParityCandidatePage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Candidates.Count > options.BatchSize)
        {
            throw new InvalidOperationException("A parity candidate page exceeds the configured BatchSize.");
        }

        if (page.Candidates.GroupBy(candidate => (candidate.SourceKind, candidate.SourceId))
            .Any(group => group.Count() != 1))
        {
            throw new InvalidOperationException("A parity candidate page contains duplicate canonical targets.");
        }

        if (!page.ReachedBoundary && page.NextCursor is null)
        {
            throw new InvalidOperationException("A non-terminal parity candidate page has no keyset cursor.");
        }

        // Fallback pages repeat immutable lookup tuple definitions so the in-memory
        // three-cycle ledger can prove eligibility. ProcessFallbackTargetAsync never
        // dispatches them to the external fee service.
    }

    private async Task TryRecordParityEventAsync(
        PaperFakFeeBackfillEvent entry,
        CancellationToken cancellationToken)
    {
        if (eventRecorder is null)
        {
            return;
        }

        try
        {
            await eventRecorder.RecordAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Historical Gross/Net parity database-event recorder failed. " +
                "EventType={EventType}. File logging remains active.",
                entry.EventType);
        }
    }

    private HistoricalGrossNetParityCycleResult Result(
        HistoricalGrossNetParityCycleState state,
        bool reachedEnd,
        PageCounters? counters = null,
        string details = "",
        HistoricalGrossNetParityProcessingPhase? resultPhase = null)
    {
        counters ??= new PageCounters();
        return new HistoricalGrossNetParityCycleResult(
            state,
            reachedEnd,
            resultPhase ?? phase,
            counters.Candidates,
            counters.Applied,
            counters.TerminalNoOps,
            counters.FallbackEligible,
            counters.Deferred,
            counters.LookupAttempts,
            counters.DonorTargets,
            counters.LiveBalancesApplied,
            details);
    }

    private sealed class PageCounters
    {
        public int Candidates { get; set; }
        public int Applied { get; set; }
        public int TerminalNoOps { get; set; }
        public int FallbackEligible { get; set; }
        public int Deferred { get; set; }
        public int LookupAttempts { get; set; }
        public int DonorTargets { get; set; }
        public int LiveBalancesApplied { get; set; }
    }

    private readonly record struct TargetLookupIdentity(
        HistoricalGrossNetParitySourceKind SourceKind,
        Guid SourceId,
        string TargetTupleHash,
        string LookupTupleSet)
    {
        public static TargetLookupIdentity Create(
            HistoricalGrossNetParityTargetSnapshot target,
            IReadOnlyList<HistoricalGrossNetParityLookupRequest> requests) =>
            new(
                target.SourceKind,
                target.SourceId,
                target.TargetTupleHash,
                string.Join("\n", requests.Select(request => request.TupleHash)
                    .OrderBy(value => value, StringComparer.Ordinal)));
    }

    private sealed class TargetLookupLedger
    {
        public int OperationalAttempts { get; set; }
        public Guid? LastOperationalCycleId { get; set; }
        public bool IsClosed { get; set; }
        public bool RequiresFallback { get; set; }
        public bool ProtocolInvariantConflict { get; set; }
        public Dictionary<string, HistoricalGrossNetParityLookupOutcome> Outcomes { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, HistoricalGrossNetParityLookupOutcome> LastOperationalOutcomes { get; } =
            new(StringComparer.Ordinal);
    }

    private sealed record TargetLookupResolution(
        bool IsClosed,
        bool RequiresFallback,
        bool ProtocolInvariantConflict,
        int AttemptsThisCycle,
        IReadOnlyList<HistoricalGrossNetParityLookupOutcome> Outcomes)
    {
        public static TargetLookupResolution Closed(
            IReadOnlyList<HistoricalGrossNetParityLookupOutcome> outcomes,
            bool requiresFallback,
            int attempts = 0) =>
            new(true, requiresFallback, false, attempts, outcomes);

        public static TargetLookupResolution Open(
            IReadOnlyList<HistoricalGrossNetParityLookupOutcome> outcomes,
            int attempts = 0) =>
            new(false, false, false, attempts, outcomes);

        public static TargetLookupResolution Invariant(
            IReadOnlyList<HistoricalGrossNetParityLookupOutcome> outcomes) =>
            new(false, false, true, 0, outcomes);
    }
}

internal static class HistoricalGrossNetParityDecisionFactory
{
    private const string Fixed3p33PointsCalculationSource =
        "historical-gross-net-parity-fixed-net-roi-minus-3p33-v1";
    private static readonly HistoricalGrossNetExactDecimal Fixed3p33PointsCoefficient =
        HistoricalGrossNetExactDecimal.Parse("0.0333");

    private const string CalculatedStatus = "Calculated";

    public static HistoricalGrossNetParityTargetSnapshot WithLookupEvidence(
        HistoricalGrossNetParityTargetSnapshot target,
        IReadOnlyList<HistoricalGrossNetParityLookupOutcome> outcomes)
    {
        var additional = outcomes
            .Where(outcome => outcome.Status == HistoricalGrossNetParityLookupOutcomeStatus.Success &&
                              outcome.FeeApplicationKind ==
                                  HistoricalGrossNetParityLookupFeeApplicationKind.AdditionalNonoverlappingComponent)
            .Select(CreateLookupComponent)
            .ToArray();
        if (additional.Length == 0)
        {
            return target;
        }

        var components = target.ProvedComponents
            .Concat(additional)
            .GroupBy(component => component.AllocationId, StringComparer.Ordinal)
            .Select(group => group.Count() == 1
                ? group.Single()
                : throw new InvalidOperationException(
                    $"Lookup evidence duplicates allocation {group.Key}."))
            .OrderBy(component => component.AllocationId, StringComparer.Ordinal)
            .ToArray();
        var componentFloor = components.Sum(component => component.AmountUsd);
        var componentHash = HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash(components);
        var bindingHash = HistoricalGrossNetParityBindingV1.Compute(
            target.TargetTupleHash,
            target.LineageHash,
            componentHash);
        var references = target.ExactEvidenceReferences.Concat(outcomes
                .Where(outcome => outcome.Status == HistoricalGrossNetParityLookupOutcomeStatus.Success)
                .Select(outcome => new HistoricalGrossNetParityEvidenceReferenceV1(
                    "historical-local-fee-lookup",
                    outcome.CalculationSource,
                    HashEvidence(outcome.EvidenceJson),
                    target.SourceKind,
                    target.SourceId)))
            .ToArray();
        return target with
        {
            ProvedComponents = components,
            ProvedComponentFloorUsd = componentFloor,
            ComponentHash = componentHash,
            ComponentPayloadJson = JsonSerializer.Serialize(components),
            BindingHash = bindingHash,
            ExactEvidenceReferences = references
        };
    }

    public static HistoricalGrossNetParityAccountingDecisionV1? TryCreateExact(
        HistoricalGrossNetParityTargetSnapshot target,
        IReadOnlyList<HistoricalGrossNetParityLookupOutcome> outcomes,
        DateTimeOffset calculatedAtUtc,
        string calculationVersion)
    {
        return target.ExactEligibility switch
        {
            HistoricalGrossNetParityExactEligibility.ExistingExactPreserved =>
                CreateExistingExact(target, outcomes, calculatedAtUtc, calculationVersion),
            HistoricalGrossNetParityExactEligibility.AuthoritativeNetRepair =>
                CreateAuthoritativeRepair(target, outcomes, calculatedAtUtc, calculationVersion),
            HistoricalGrossNetParityExactEligibility.LocalLookupRequired =>
                CreateLocalExact(target, outcomes, calculatedAtUtc, calculationVersion),
            _ => null
        };
    }

    public static HistoricalGrossNetParityAccountingDecisionV1 CreateFallback(
        HistoricalGrossNetParityTargetSnapshot target,
        DateTimeOffset calculatedAtUtc,
        string calculationVersion)
    {
        var basis = HistoricalGrossNetExactDecimal.FromDecimal(target.GrossRoiBasisUsd);
        var components = target.ProvedComponents
            .Select(component => new HistoricalGrossNetProvedFeeComponent(
                component.AllocationId,
                component.CoverageHash,
                HistoricalGrossNetExactDecimal.FromDecimal(component.AmountUsd)))
            .ToArray();
        var estimate = target.GrossRoiBasisUsd > 0m
            ? CreateFixed3p33PointsEstimate(basis, components)
            : HistoricalGrossNetFeeEstimator.Calculate(basis, null, components);
        var fee = ParseExactDecimal(estimate.TotalFee);
        var kind = target.GrossRoiBasisUsd <= 0m
            ? HistoricalGrossNetParityDecisionKind.NonpositiveBasis
            : HistoricalGrossNetParityDecisionKind.Fixed0p0333;

        return Create(
            target,
            kind,
            fee,
            fee,
            Round8(target.GrossPnlUsd - fee),
            CalculatedStatus,
            FeeLiquidityRole.Unknown.ToString(),
            estimate.CalculationSource,
            null,
            null,
            null,
            calculatedAtUtc,
            donor: null,
            calculationVersion,
            new
            {
                decision = kind.ToString(),
                donorPolicy = "disabled",
                directFixedContractId = HistoricalGrossNetParityConstants.DirectFixedFallbackContractId,
                directFixedContractDigest =
                    HistoricalGrossNetParityConstants.DirectFixedFallbackSemanticDigest,
                comparisonKey = target.GrossRoiBasisUsd > 0m
                    ? (IReadOnlyList<string>)["tier:direct-fixed-net-roi-minus-3.33-points"]
                    : [],
                target.ProvedComponentFloorUsd,
                target.ProvedComponents
            });
    }

    private static HistoricalGrossNetFeeEstimate CreateFixed3p33PointsEstimate(
        HistoricalGrossNetExactDecimal basis,
        IReadOnlyList<HistoricalGrossNetProvedFeeComponent> components)
    {
        var componentFloor = HistoricalGrossNetFeeEstimator.CalculateComponentFloor(components);
        var fee = basis.Multiply(Fixed3p33PointsCoefficient).RoundAwayFromZero(8);
        return new HistoricalGrossNetFeeEstimate(
            fee,
            componentFloor,
            fee,
            Fixed3p33PointsCalculationSource);
    }

    private static HistoricalGrossNetParityAccountingDecisionV1 CreateExistingExact(
        HistoricalGrossNetParityTargetSnapshot target,
        IReadOnlyList<HistoricalGrossNetParityLookupOutcome> outcomes,
        DateTimeOffset calculatedAtUtc,
        string calculationVersion)
    {
        var effectiveFee = target.AuthoritativeEffectiveFeeUsd ??
            (target.SourceKind == HistoricalGrossNetParitySourceKind.PaperSellFill && target.NetPnlUsd is not null
                ? target.GrossPnlUsd - target.NetPnlUsd.Value
                : target.FeeUsd);
        ValidateEffectiveFee(target, effectiveFee);
        if (target.NetPnlUsd is null || Round8(target.GrossPnlUsd - effectiveFee) != target.NetPnlUsd.Value)
        {
            throw new InvalidOperationException(
                $"Existing exact target {target.SourceKind}/{target.SourceId:D} has inconsistent Net.");
        }

        return Create(
            target,
            HistoricalGrossNetParityDecisionKind.ExistingExactPreserved,
            target.FeeUsd,
            effectiveFee,
            target.NetPnlUsd.Value,
            target.FeeAccountingStatus,
            target.FeeLiquidityRole,
            target.FeeCalculationSource,
            target.FeeRate,
            target.FeeExponent,
            target.FeeTakerOnly,
            target.FeeCalculatedAtUtc ?? calculatedAtUtc,
            null,
            calculationVersion,
            new { decision = "existing-exact-preserved", outcomes });
    }

    private static HistoricalGrossNetParityAccountingDecisionV1 CreateAuthoritativeRepair(
        HistoricalGrossNetParityTargetSnapshot target,
        IReadOnlyList<HistoricalGrossNetParityLookupOutcome> outcomes,
        DateTimeOffset calculatedAtUtc,
        string calculationVersion)
    {
        var effectiveFee = target.AuthoritativeEffectiveFeeUsd ??
            throw new InvalidOperationException(
                $"Authoritative target {target.SourceKind}/{target.SourceId:D} has no effective Fee.");
        ValidateEffectiveFee(target, effectiveFee);
        return Create(
            target,
            HistoricalGrossNetParityDecisionKind.AuthoritativeNetRepair,
            target.FeeUsd,
            effectiveFee,
            Round8(target.GrossPnlUsd - effectiveFee),
            target.FeeAccountingStatus,
            target.FeeLiquidityRole,
            target.FeeCalculationSource,
            target.FeeRate,
            target.FeeExponent,
            target.FeeTakerOnly,
            target.FeeCalculatedAtUtc ?? calculatedAtUtc,
            null,
            calculationVersion,
            new { decision = "authoritative-net-repair", outcomes });
    }

    private static HistoricalGrossNetParityAccountingDecisionV1? CreateLocalExact(
        HistoricalGrossNetParityTargetSnapshot target,
        IReadOnlyList<HistoricalGrossNetParityLookupOutcome> outcomes,
        DateTimeOffset calculatedAtUtc,
        string calculationVersion)
    {
        if (outcomes.Count == 0 ||
            outcomes.Any(outcome => outcome.Status != HistoricalGrossNetParityLookupOutcomeStatus.Success))
        {
            return null;
        }

        var total = outcomes.Where(outcome =>
            outcome.FeeApplicationKind == HistoricalGrossNetParityLookupFeeApplicationKind.TotalContributionFee)
            .ToArray();
        var additional = outcomes.Where(outcome =>
            outcome.FeeApplicationKind ==
                HistoricalGrossNetParityLookupFeeApplicationKind.AdditionalNonoverlappingComponent)
            .ToArray();
        if (total.Length > 1 || (total.Length == 1 && additional.Length != 0))
        {
            throw new InvalidOperationException(
                $"Target {target.SourceKind}/{target.SourceId:D} has ambiguous lookup composition.");
        }

        var effectiveFee = total.Length == 1
            ? total[0].FeeUsd ?? throw new InvalidOperationException("A successful total lookup has no Fee.")
            : target.ProvedComponentFloorUsd + additional.Sum(outcome =>
                outcome.FeeUsd ?? throw new InvalidOperationException("A successful component lookup has no Fee."));
        ValidateEffectiveFee(target, effectiveFee);
        var storedFee = target.SourceKind == HistoricalGrossNetParitySourceKind.PaperSellFill
            ? outcomes
                .Where(outcome => outcome.FeeSourceChargeId.EndsWith(":exit", StringComparison.Ordinal))
                .Select(outcome => outcome.FeeUsd)
                .SingleOrDefault() ?? target.FeeUsd
            : effectiveFee;
        var calculationSources = outcomes.Select(outcome => outcome.CalculationSource)
            .Distinct(StringComparer.Ordinal).ToArray();
        var roles = outcomes.Select(outcome => outcome.FeeLiquidityRole)
            .Distinct(StringComparer.Ordinal).ToArray();
        var rates = outcomes.Select(outcome => outcome.FeeRate).Distinct().ToArray();
        var exponents = outcomes.Select(outcome => outcome.FeeExponent).Distinct().ToArray();
        var takerOnlyValues = outcomes.Select(outcome => outcome.FeeTakerOnly).Distinct().ToArray();
        var representative = outcomes[0];
        return Create(
            target,
            HistoricalGrossNetParityDecisionKind.LocalExactCalculated,
            storedFee,
            effectiveFee,
            Round8(target.GrossPnlUsd - effectiveFee),
            CalculatedStatus,
            target.ProvedComponents.Count == 0 && roles.Length == 1
                ? roles[0]
                : FeeLiquidityRole.Unknown.ToString(),
            target.ProvedComponents.Count == 0 && calculationSources.Length == 1
                ? calculationSources[0]
                : "mixed",
            target.ProvedComponents.Count == 0 && rates.Length == 1 ? rates[0] : null,
            target.ProvedComponents.Count == 0 && exponents.Length == 1 ? exponents[0] : null,
            target.ProvedComponents.Count == 0 && takerOnlyValues.Length == 1
                ? takerOnlyValues[0]
                : null,
            representative.CapturedAtUtc,
            null,
            calculationVersion,
            new { decision = "local-exact-calculated", outcomes });
    }

    private static HistoricalGrossNetParityComponentAllocationV1 CreateLookupComponent(
        HistoricalGrossNetParityLookupOutcome outcome)
    {
        var fee = outcome.FeeUsd ?? throw new InvalidOperationException(
            "A successful component lookup has no Fee.");
        var sourceEvidence = JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityLocalLookupSourceChargeV1",
            outcome.TupleHash,
            outcome.FeeSourceChargeId,
            fee,
            outcome.CalculationSource,
            outcome.FeeLiquidityRole,
            outcome.FeeRate,
            outcome.FeeExponent,
            outcome.FeeTakerOnly,
            outcome.CapturedAtUtc,
            outcome.EvidenceJson
        });
        var evidenceHash = HashEvidence(sourceEvidence);
        var poolId = "canonical-local-lookup:" + evidenceHash;
        var edgeEvidence = JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityLocalLookupCoverageV1",
            outcome.FeeSourceChargeId,
            poolId,
            outcome.FeeAllocationId,
            evidenceHash
        });
        return HistoricalGrossNetParityComponentGraphV1.Create(
            outcome.FeeAllocationId,
            fee,
            [new HistoricalGrossNetParitySourceChargeV1(
                outcome.FeeSourceChargeId,
                fee,
                evidenceHash,
                sourceEvidence)],
            [new HistoricalGrossNetParityChargeCoverageEdgeV1(
                outcome.FeeSourceChargeId,
                poolId,
                outcome.FeeAllocationId,
                HashEvidence(edgeEvidence),
                edgeEvidence)]);
    }

    private static string HashEvidence(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();

    private static HistoricalGrossNetParityAccountingDecisionV1 Create(
        HistoricalGrossNetParityTargetSnapshot target,
        HistoricalGrossNetParityDecisionKind kind,
        decimal storedFee,
        decimal effectiveFee,
        decimal net,
        string status,
        string role,
        string source,
        decimal? rate,
        int? exponent,
        bool? takerOnly,
        DateTimeOffset calculatedAt,
        HistoricalGrossNetParityDonorDecisionV1? donor,
        string calculationVersion,
        object evidence) =>
        new(
            kind,
            Round8(storedFee),
            Round8(effectiveFee),
            Round8(net),
            status,
            role,
            source,
            rate,
            exponent,
            takerOnly,
            calculatedAt,
            target.SourceKind == HistoricalGrossNetParitySourceKind.LiveOrder
                ? Round8(target.GrossRoiBasisUsd + effectiveFee)
                : null,
            target.ProvedComponentFloorUsd,
            donor,
            calculationVersion,
            JsonSerializer.Serialize(new
            {
                target.StrategyId,
                target.StrategyRank,
                target.StrategyGrossPnlUsd,
                contractId = HistoricalGrossNetParityConstants.StrategyCompletionContractId,
                contractDigest = HistoricalGrossNetParityConstants.StrategyCompletionSemanticDigest,
                calculationVersion,
                componentAllocationHashV1 = ComputeComponentAllocationHash(target),
                evidence
            }));

    private static string ComputeComponentAllocationHash(
        HistoricalGrossNetParityTargetSnapshot target) =>
        HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash(target.ProvedComponents);

    private static void ValidateEffectiveFee(
        HistoricalGrossNetParityTargetSnapshot target,
        decimal effectiveFee)
    {
        if (effectiveFee < 0m || effectiveFee < target.ProvedComponentFloorUsd)
        {
            throw new InvalidOperationException(
                $"Target {target.SourceKind}/{target.SourceId:D} has an invalid contribution-effective Fee.");
        }
    }

    private static decimal ParseExactDecimal(HistoricalGrossNetExactDecimal value) =>
        decimal.Parse(value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static decimal Round8(decimal value) =>
        Math.Round(value, 8, MidpointRounding.AwayFromZero);
}

internal sealed record HistoricalGrossNetParityPreparedPage(
    IReadOnlyList<HistoricalGrossNetParityTargetSnapshot> Targets,
    IReadOnlyList<HistoricalGrossNetParityLookupRequest> LookupRequests,
    IReadOnlyList<HistoricalGrossNetParityTargetConflict> Conflicts);

internal static class HistoricalGrossNetParityPaperPreparer
{
    private const string ExactHistoricalPrefix = "historical-current-paper-model-v1:";

    private sealed record PreparedStrategyRank(
        Guid StrategyId,
        string StrategyCode,
        int Rank,
        decimal GrossPnlUsd);

    public static HistoricalGrossNetParityPreparedPage Prepare(
        HistoricalGrossNetParityCandidatePage page,
        DateTimeOffset cutoffUtc)
    {
        ArgumentNullException.ThrowIfNull(page);
        var conflicts = page.Conflicts.ToList();
        var ranks = new Dictionary<Guid, PreparedStrategyRank>();
        foreach (var group in page.Candidates.GroupBy(candidate => candidate.StrategyId))
        {
            var first = group.First();
            if (group.Any(candidate =>
                    candidate.StrategyRank != first.StrategyRank ||
                    candidate.StrategyGrossPnlUsd != first.StrategyGrossPnlUsd ||
                    !string.Equals(candidate.StrategyCode, first.StrategyCode, StringComparison.Ordinal)))
            {
                AddConflict(
                    conflicts,
                    "strategy_rank_conflict",
                    null,
                    null,
                    group.Key,
                    "One candidate page contains conflicting Gross-rank metadata for a strategy.");
                continue;
            }

            ranks.Add(group.Key, new PreparedStrategyRank(
                group.Key,
                first.StrategyCode,
                first.StrategyRank,
                first.StrategyGrossPnlUsd));
        }

        var sourceSelections = UniqueBy(
            page.PaperSourceSelections,
            value => value.StrategyId,
            "Paper source selection",
            conflicts).ToDictionary(value => value.StrategyId);
        var fillsByOrder = page.PaperFillObservations
            .GroupBy(fill => fill.PaperOrderId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(fill => fill.FilledAtUtc)
                    .ThenBy(fill => fill.PaperOrderId.ToString("D"), StringComparer.Ordinal)
                    .ThenBy(fill => fill.FillId.ToString("D"), StringComparer.Ordinal)
                    .ToArray());
        var poolKeys = page.PaperPositionObservations
            .Select(position => new PoolKey(position.CopiedTraderWallet, position.AssetId))
            .Concat(page.PaperSettlementObservations.Select(settlement =>
                new PoolKey(settlement.CopiedTraderWallet, settlement.AssetId)))
            .Concat(page.Candidates
                .Where(candidate => candidate.SourceKind == HistoricalGrossNetParitySourceKind.PaperSellFill)
                .Join(
                    page.PaperFillObservations,
                    candidate => candidate.SourceId,
                    fill => fill.FillId,
                    (_, fill) => new PoolKey(fill.CopiedTraderWallet, fill.AssetId)))
            .ToHashSet(PoolKeyComparer.Instance);
        var pools = new Dictionary<PoolKey, PoolReplay>(PoolKeyComparer.Instance);
        var poolErrors = new Dictionary<PoolKey, string>(PoolKeyComparer.Instance);
        foreach (var group in page.PaperFillObservations
                     .Where(fill => poolKeys.Contains(new PoolKey(fill.CopiedTraderWallet, fill.AssetId)))
                     .GroupBy(fill => new PoolKey(fill.CopiedTraderWallet, fill.AssetId), PoolKeyComparer.Instance))
        {
            try
            {
                pools.Add(group.Key, ReplayPool(group.Key, group, cutoffUtc, conflicts));
            }
            catch (Exception exception) when (exception is ArgumentException or ArithmeticException or
                                               InvalidOperationException)
            {
                poolErrors[group.Key] = exception.Message;
            }
        }
        var liveTargets = UniqueBy(
            page.LiveTargets,
            target => target.SourceId,
            "Live target",
            conflicts).ToDictionary(target => target.SourceId);
        var runs = UniqueBy(
            page.PaperRunObservations,
            run => run.RunId,
            "Paper run",
            conflicts).ToDictionary(run => run.RunId);
        var positions = UniqueBy(
            page.PaperPositionObservations,
            position => position.PositionId,
            "Paper position",
            conflicts).ToDictionary(position => position.PositionId);
        var settlements = UniqueBy(
            page.PaperSettlementObservations,
            settlement => settlement.SettlementId,
            "Paper settlement",
            conflicts).ToDictionary(settlement => settlement.SettlementId);
        var fills = UniqueBy(
            page.PaperFillObservations,
            fill => fill.FillId,
            "Paper fill",
            conflicts).ToDictionary(fill => fill.FillId);
        var targets = new List<HistoricalGrossNetParityTargetSnapshot>();
        var lookups = page.LookupRequests.ToList();

        foreach (var candidate in page.Candidates)
        {
            if (!ranks.TryGetValue(candidate.StrategyId, out var rank))
            {
                continue;
            }

            if (candidate.SourceKind == HistoricalGrossNetParitySourceKind.LiveOrder)
            {
                if (!liveTargets.TryGetValue(candidate.SourceId, out var liveTarget) ||
                    liveTarget.StrategyId != candidate.StrategyId ||
                    liveTarget.StrategyRank != candidate.StrategyRank ||
                    liveTarget.StrategyGrossPnlUsd != candidate.StrategyGrossPnlUsd ||
                    liveTarget.RowVersion != candidate.RowVersion ||
                    liveTarget.Ownership != candidate.Ownership)
                {
                    AddConflict(
                        conflicts,
                        "live_candidate_binding_mismatch",
                        candidate.SourceKind,
                        candidate.SourceId,
                        candidate.StrategyId,
                        "The bounded Live candidate key does not match one canonical Live target snapshot.");
                    continue;
                }

                targets.Add(liveTarget);
                continue;
            }

            if (!TryGetPaperScope(
                    candidate.StrategyId,
                    ranks,
                    sourceSelections,
                    conflicts,
                    out rank,
                    out var selection))
            {
                continue;
            }

            HistoricalGrossNetParityTargetSnapshot? target = null;
            IReadOnlyList<HistoricalGrossNetParityPaperFillObservation> targetFills = [];
            IReadOnlyList<HistoricalGrossNetParityLookupRequest> targetLookups = [];
            switch (candidate.SourceKind)
            {
                case HistoricalGrossNetParitySourceKind.PaperRun:
                {
                    if (!selection.UsesRuns || !runs.TryGetValue(candidate.SourceId, out var run))
                    {
                        AddConflict(
                            conflicts,
                            "paper_run_candidate_missing",
                            candidate.SourceKind,
                            candidate.SourceId,
                            candidate.StrategyId,
                            "A Gross-selected Paper run candidate is absent or its strategy does not use runs.");
                        break;
                    }

                    target = PrepareRun(run, rank, fillsByOrder, cutoffUtc, conflicts);
                    targetFills = run.PaperOrderId is { } orderId &&
                        fillsByOrder.TryGetValue(orderId, out var linkedFills)
                            ? linkedFills
                            : [];
                    if (target is not null)
                    {
                        targetLookups = CreateRunLookups(run, target, targetFills);
                    }

                    break;
                }
                case HistoricalGrossNetParitySourceKind.PaperPosition:
                {
                    if (!positions.TryGetValue(candidate.SourceId, out var position))
                    {
                        AddConflict(
                            conflicts,
                            "paper_position_pool_missing",
                            candidate.SourceKind,
                            candidate.SourceId,
                            candidate.StrategyId,
                            "An open Gross-contributing position has no replayable wallet/asset fill pool.");
                        break;
                    }

                    var positionPoolKey = new PoolKey(position.CopiedTraderWallet, position.AssetId);
                    if (!pools.TryGetValue(positionPoolKey, out var replay))
                    {
                        AddConflict(
                            conflicts,
                            "paper_position_pool_missing",
                            candidate.SourceKind,
                            candidate.SourceId,
                            candidate.StrategyId,
                            poolErrors.GetValueOrDefault(positionPoolKey) ??
                            "An open Gross-contributing position has no replayable wallet/asset fill pool.");
                        break;
                    }

                    target = PreparePosition(position, rank, replay, conflicts);
                    targetFills = replay.OrderedFills;
                    if (target is not null)
                    {
                        targetLookups = CreatePoolEntryLookups(
                            target,
                            targetFills,
                            $"paper-entry-remaining:PaperPosition:{position.PositionId:D}");
                    }

                    break;
                }
                case HistoricalGrossNetParitySourceKind.PaperSettlement:
                {
                    if (selection.UsesRuns ||
                        !settlements.TryGetValue(candidate.SourceId, out var settlement))
                    {
                        AddConflict(
                            conflicts,
                            "paper_settlement_pool_missing",
                            candidate.SourceKind,
                            candidate.SourceId,
                            candidate.StrategyId,
                            "A runless Gross-contributing settlement has no replayable wallet/asset fill pool.");
                        break;
                    }

                    var settlementPoolKey = new PoolKey(settlement.CopiedTraderWallet, settlement.AssetId);
                    if (!pools.TryGetValue(settlementPoolKey, out var replay))
                    {
                        AddConflict(
                            conflicts,
                            "paper_settlement_pool_missing",
                            candidate.SourceKind,
                            candidate.SourceId,
                            candidate.StrategyId,
                            poolErrors.GetValueOrDefault(settlementPoolKey) ??
                            "A runless Gross-contributing settlement has no replayable wallet/asset fill pool.");
                        break;
                    }

                    target = PrepareSettlement(settlement, rank, replay, conflicts);
                    targetFills = replay.OrderedFills
                        .Where(fill => fill.FilledAtUtc < settlement.SettledAtUtc)
                        .ToArray();
                    if (target is not null)
                    {
                        targetLookups = CreatePoolEntryLookups(
                            target,
                            targetFills,
                            $"paper-entry-remaining:PaperSettlement:{settlement.SettlementId:D}");
                    }

                    break;
                }
                case HistoricalGrossNetParitySourceKind.PaperSellFill:
                {
                    if (selection.UsesRuns ||
                        !fills.TryGetValue(candidate.SourceId, out var fill) ||
                        !IsSell(fill))
                    {
                        AddConflict(
                            conflicts,
                            "paper_sell_replay_missing",
                            candidate.SourceKind,
                            candidate.SourceId,
                            candidate.StrategyId,
                            "A runless Gross-contributing SELL has no deterministic pool replay state.");
                        break;
                    }

                    var sellPoolKey = new PoolKey(fill.CopiedTraderWallet, fill.AssetId);
                    if (!pools.TryGetValue(sellPoolKey, out var replay) ||
                        !replay.Sells.TryGetValue(fill.FillId, out var sell))
                    {
                        AddConflict(
                            conflicts,
                            "paper_sell_replay_missing",
                            candidate.SourceKind,
                            candidate.SourceId,
                            candidate.StrategyId,
                            poolErrors.GetValueOrDefault(sellPoolKey) ??
                            "A runless Gross-contributing SELL has no deterministic pool replay state.");
                        break;
                    }

                    target = PrepareSell(fill, rank, sell, conflicts);
                    targetFills = replay.OrderedFills.TakeWhile(item => item.FillId != fill.FillId)
                        .Append(fill)
                        .ToArray();
                    if (target is not null)
                    {
                        targetLookups = CreateSellLookups(fill, target, sell);
                    }

                    break;
                }
                default:
                    AddConflict(
                        conflicts,
                        "unsupported_candidate_source",
                        candidate.SourceKind,
                        candidate.SourceId,
                        candidate.StrategyId,
                        "The candidate page contains a dependency-only or unsupported source kind.");
                    break;
            }

            if (target is null)
            {
                continue;
            }

            target = WithPaperBindings(target, selection, targetFills);
            if (targetLookups.Count != 0)
            {
                target = target with
                {
                    ExactEligibility = HistoricalGrossNetParityExactEligibility.LocalLookupRequired
                };
                lookups.AddRange(targetLookups);
            }

            targets.Add(target);
        }

        var duplicateTarget = targets
            .GroupBy(target => (target.SourceKind, target.SourceId))
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicateTarget is not null)
        {
            AddConflict(
                conflicts,
                "duplicate_canonical_target",
                duplicateTarget.Key.SourceKind,
                duplicateTarget.Key.SourceId,
                null,
                "The bounded prepared target page contains a duplicate canonical identity.");
        }

        var uniqueLookups = lookups
            .GroupBy(request => request.TupleHash, StringComparer.Ordinal)
            .Select(group =>
            {
                var values = group.ToArray();
                if (values.Select(value => value.CanonicalPayloadJson)
                    .Distinct(StringComparer.Ordinal).Count() != 1)
                {
                    throw new InvalidOperationException(
                        $"Conflicting historical lookup tuple {group.Key} was prepared.");
                }

                return values[0];
            })
            .OrderBy(request => request.TupleHash, StringComparer.Ordinal)
            .ToArray();
        return new HistoricalGrossNetParityPreparedPage(
            targets.OrderBy(target => target.StrategyRank)
                .ThenBy(target => target.StrategyId.ToString("D"), StringComparer.Ordinal)
                .ThenBy(target => target.SourceKind)
                .ThenBy(target => target.SourceId.ToString("D"), StringComparer.Ordinal)
                .ToArray(),
            uniqueLookups,
            conflicts);
    }

    private static IReadOnlyList<HistoricalGrossNetParityLookupRequest> CreateRunLookups(
        HistoricalGrossNetParityPaperRunObservation run,
        HistoricalGrossNetParityTargetSnapshot target,
        IReadOnlyList<HistoricalGrossNetParityPaperFillObservation> linkedFills)
    {
        if (target.ExactEligibility != HistoricalGrossNetParityExactEligibility.FallbackRequired)
        {
            return [];
        }

        if (linkedFills.Count != 0)
        {
            return linkedFills
                .Where(fill => !IsAuthoritativeFillFee(fill))
                .Select(fill => CreatePaperLookup(
                    target,
                    fill.ConditionId,
                    fill.AssetId,
                    TradeSide.Buy.ToString(),
                    fill.Outcome,
                    fill.FillSizeShares,
                    fill.FillPrice,
                    fill.FeeLiquidityRole,
                    HistoricalGrossNetParityLookupFeeApplicationKind.AdditionalNonoverlappingComponent,
                    $"paper-run-entry:{run.RunId:D}:{fill.FillId:D}",
                    $"paper-fill:{fill.FillId:D}:entry",
                    fill.CanonicalPayloadJson))
                .ToArray();
        }

        if (run.EntryPrice is null || run.SizeShares is null)
        {
            return [];
        }

        return
        [
            CreatePaperLookup(
                target,
                run.ConditionId,
                run.AssetId ?? string.Empty,
                TradeSide.Buy.ToString(),
                run.Outcome ?? string.Empty,
                run.SizeShares.Value,
                run.EntryPrice.Value,
                run.FeeLiquidityRole,
                HistoricalGrossNetParityLookupFeeApplicationKind.TotalContributionFee,
                $"paper-run-total:{run.RunId:D}",
                $"canonical-local:PaperRun:{run.RunId:D}:entry-v1",
                run.CanonicalPayloadJson)
        ];
    }

    private static IReadOnlyList<HistoricalGrossNetParityLookupRequest> CreatePoolEntryLookups(
        HistoricalGrossNetParityTargetSnapshot target,
        IReadOnlyList<HistoricalGrossNetParityPaperFillObservation> contributingFills,
        string allocationPrefix)
    {
        if (target.ExactEligibility != HistoricalGrossNetParityExactEligibility.FallbackRequired ||
            contributingFills.Any(IsSell))
        {
            return [];
        }

        return contributingFills
            .Where(IsBuy)
            .Where(fill => !IsAuthoritativeFillFee(fill))
            .Select(fill => CreatePaperLookup(
                target,
                fill.ConditionId,
                fill.AssetId,
                TradeSide.Buy.ToString(),
                fill.Outcome,
                fill.FillSizeShares,
                fill.FillPrice,
                fill.FeeLiquidityRole,
                HistoricalGrossNetParityLookupFeeApplicationKind.AdditionalNonoverlappingComponent,
                $"{allocationPrefix}:{fill.FillId:D}",
                $"paper-fill:{fill.FillId:D}:entry",
                fill.CanonicalPayloadJson))
            .ToArray();
    }

    private static IReadOnlyList<HistoricalGrossNetParityLookupRequest> CreateSellLookups(
        HistoricalGrossNetParityPaperFillObservation fill,
        HistoricalGrossNetParityTargetSnapshot target,
        SellReplay sell)
    {
        if (target.ExactEligibility != HistoricalGrossNetParityExactEligibility.FallbackRequired ||
            sell.Before.EntryEvidence != EntryEvidence.Exact ||
            IsAuthoritativeFillFee(fill))
        {
            return [];
        }

        return
        [
            CreatePaperLookup(
                target,
                fill.ConditionId,
                fill.AssetId,
                TradeSide.Sell.ToString(),
                fill.Outcome,
                fill.FillSizeShares,
                fill.FillPrice,
                fill.FeeLiquidityRole,
                HistoricalGrossNetParityLookupFeeApplicationKind.AdditionalNonoverlappingComponent,
                $"paper-exit-allocation:{fill.FillId:D}",
                $"paper-fill:{fill.FillId:D}:exit",
                fill.CanonicalPayloadJson)
        ];
    }

    private static HistoricalGrossNetParityLookupRequest CreatePaperLookup(
        HistoricalGrossNetParityTargetSnapshot target,
        string conditionId,
        string assetId,
        string side,
        string outcome,
        decimal quantity,
        decimal price,
        string liquidityRole,
        HistoricalGrossNetParityLookupFeeApplicationKind applicationKind,
        string allocationId,
        string sourceChargeId,
        string sourcePayload)
    {
        var payload = JsonSerializer.Serialize(new
        {
            version = "HistoricalExactLookupTupleV1",
            target.SourceKind,
            target.SourceId,
            target.StrategyId,
            conditionId,
            assetId,
            side,
            outcome,
            target.GrossRoiBasisUsd,
            quantity,
            price,
            liquidityRole,
            applicationKind,
            allocationId,
            sourceChargeId,
            target.TargetTupleHash,
            target.LineageHash,
            target.ComponentHash,
            sourcePayload
        });
        return new HistoricalGrossNetParityLookupRequest(
            Hash(payload),
            target.SourceKind,
            target.SourceId,
            target.StrategyId,
            conditionId,
            assetId,
            side,
            outcome,
            target.GrossRoiBasisUsd,
            quantity,
            price,
            liquidityRole,
            applicationKind,
            allocationId,
            sourceChargeId,
            payload);
    }

    private static HistoricalGrossNetParityTargetSnapshot WithPaperBindings(
        HistoricalGrossNetParityTargetSnapshot target,
        HistoricalGrossNetParityPaperSourceSelection selection,
        IEnumerable<HistoricalGrossNetParityPaperFillObservation> fills)
    {
        var references = new List<HistoricalGrossNetParityEvidenceReferenceV1>
        {
            new(
                "paper-source-selection",
                "v1",
                selection.EvidenceHash,
                HistoricalGrossNetParitySourceKind.PaperSourceSelection,
                selection.StrategyId)
        };
        references.AddRange(fills
            .GroupBy(fill => fill.FillId)
            .Select(group => group.Single())
            .Select(fill => new HistoricalGrossNetParityEvidenceReferenceV1(
                "paper-fill-binding",
                "v1",
                Hash(fill.CanonicalPayloadJson),
                HistoricalGrossNetParitySourceKind.PaperFillEvidence,
                fill.FillId)));
        return target with { BindingEvidenceReferences = references };
    }

    private static HistoricalGrossNetParityTargetSnapshot? PrepareRun(
        HistoricalGrossNetParityPaperRunObservation run,
        PreparedStrategyRank rank,
        IReadOnlyDictionary<Guid, HistoricalGrossNetParityPaperFillObservation[]> fillsByOrder,
        DateTimeOffset cutoffUtc,
        List<HistoricalGrossNetParityTargetConflict> conflicts)
    {
        if (run.RealizedPnlUsd is null || run.SettledAtUtc is null)
        {
            AddConflict(
                conflicts,
                "settled_run_financial_fields_missing",
                HistoricalGrossNetParitySourceKind.PaperRun,
                run.RunId,
                run.StrategyId,
                "A settled run is missing Gross PnL or settled time.");
            return null;
        }

        HistoricalGrossNetParityPaperFillObservation[] linkedFills = [];
        DateTimeOffset originatedAtUtc;
        if (run.PaperOrderId is { } paperOrderId)
        {
            if (!fillsByOrder.TryGetValue(paperOrderId, out var foundFills) ||
                foundFills is null ||
                foundFills.Length == 0)
            {
                AddConflict(
                    conflicts,
                    "settled_run_linked_fills_missing",
                    HistoricalGrossNetParitySourceKind.PaperRun,
                    run.RunId,
                    run.StrategyId,
                    "A settled run with paper_order_id has no linked persisted fill.");
                return null;
            }

            linkedFills = foundFills;

            if (linkedFills.Any(fill => !IsBuy(fill)))
            {
                AddConflict(
                    conflicts,
                    "settled_run_nonbuy_origin",
                    HistoricalGrossNetParitySourceKind.PaperRun,
                    run.RunId,
                    run.StrategyId,
                    "A settled run links to a non-BUY originating fill.");
                return null;
            }

            var hasPreCutoffFill = linkedFills.Any(fill => fill.FilledAtUtc < cutoffUtc);
            var hasPostCutoffFill = linkedFills.Any(fill => fill.FilledAtUtc >= cutoffUtc);
            if (hasPreCutoffFill && hasPostCutoffFill)
            {
                AddConflict(
                    conflicts,
                    "settled_run_originating_fills_mixed_cutoff",
                    HistoricalGrossNetParitySourceKind.PaperRun,
                    run.RunId,
                    run.StrategyId,
                    "A settled run links to both pre-cutoff and post-cutoff originating BUY fills.");
                return null;
            }

            if (hasPostCutoffFill)
            {
                return null;
            }

            originatedAtUtc = linkedFills.Min(fill => fill.FilledAtUtc);
        }
        else
        {
            if (run.EnteredAtUtc is null)
            {
                AddConflict(
                    conflicts,
                    "settled_run_origin_unproved",
                    HistoricalGrossNetParitySourceKind.PaperRun,
                    run.RunId,
                    run.StrategyId,
                    "A settled run without order/fills has no entered_at_utc fallback origin.");
                return null;
            }

            if (run.EnteredAtUtc.Value >= cutoffUtc)
            {
                return null;
            }

            originatedAtUtc = run.EnteredAtUtc.Value;
        }

        var components = linkedFills
            .Where(IsAuthoritativeFillFee)
            .Select(fill => CreateComponent(
                $"paper-run-entry:{run.RunId:D}:{fill.FillId:D}",
                $"paper-fill:{fill.FillId:D}:entry",
                fill.FeeUsd,
                fill.CanonicalPayloadJson))
            .ToArray();
        var childAggregateExact = linkedFills.Length != 0 &&
            linkedFills.All(IsAuthoritativeFillFee) &&
            Round8(linkedFills.Sum(fill => fill.FeeUsd)) == run.FeeUsd;
        if (linkedFills.Length != 0 &&
            linkedFills.All(IsAuthoritativeFillFee) &&
            !childAggregateExact)
        {
            AddConflict(
                conflicts,
                "settled_run_exact_child_fee_mismatch",
                HistoricalGrossNetParitySourceKind.PaperRun,
                run.RunId,
                run.StrategyId,
                "The settled run Fee differs from the exact sum of its linked originating fill charges.");
            return null;
        }

        var authoritative = IsAuthoritativeTotal(
            run.FeeUsd,
            run.FeeAccountingStatus,
            run.FeeCalculationSource,
            run.FeeLiquidityRole,
            run.FeeRate,
            run.FeeExponent,
            run.FeeTakerOnly,
            run.FeeCalculatedAtUtc) ||
            (string.Equals(run.FeeCalculationSource, "mixed", StringComparison.Ordinal) && childAggregateExact);
        var lineagePayload = JsonSerializer.Serialize(new
        {
            run.RunId,
            run.PaperOrderId,
            originatedAtUtc,
            fills = linkedFills.Select(fill => fill.CanonicalPayloadJson)
        });
        return CreateTarget(
            HistoricalGrossNetParitySourceKind.PaperRun,
            run.RunId,
            run.StrategyId,
            rank.Rank,
            rank.GrossPnlUsd,
            run.RowVersion,
            originatedAtUtc,
            run.SettledAtUtc,
            run.RealizedPnlUsd.Value,
            run.StakeUsd,
            run.FeeUsd,
            run.FeeAccountingStatus,
            run.FeeLiquidityRole,
            run.FeeCalculationSource,
            run.FeeRate,
            run.FeeExponent,
            run.FeeTakerOnly,
            run.FeeCalculatedAtUtc,
            run.NetRealizedPnlUsd,
            authoritative,
            components,
            lineagePayload,
            run.CanonicalPayloadJson);
    }

    private static HistoricalGrossNetParityTargetSnapshot? PreparePosition(
        HistoricalGrossNetParityPaperPositionObservation position,
        PreparedStrategyRank rank,
        PoolReplay replay,
        List<HistoricalGrossNetParityTargetConflict> conflicts)
    {
        var state = replay.FinalState;
        if (Round8(state.SizeShares) != position.SizeShares ||
            Round8(state.AveragePrice) != position.AveragePrice)
        {
            AddConflict(
                conflicts,
                "paper_position_replay_mismatch",
                HistoricalGrossNetParitySourceKind.PaperPosition,
                position.PositionId,
                position.StrategyId,
                "Replayed size/average does not equal the persisted open position.");
            return null;
        }

        if (!TryClassifyOrigin(
                state,
                HistoricalGrossNetParitySourceKind.PaperPosition,
                position.PositionId,
                position.StrategyId,
                conflicts,
                out var originatedAtUtc))
        {
            return null;
        }

        if (state.EntryEvidence == EntryEvidence.Mixed && state.HasPriorSell)
        {
            AddConflict(
                conflicts,
                "paper_position_component_partition_ambiguous",
                HistoricalGrossNetParitySourceKind.PaperPosition,
                position.PositionId,
                position.StrategyId,
                "The remaining position Fee pool mixes proved and unproved entry charges.");
            return null;
        }

        var mixedSource = string.Equals(
            position.FeeCalculationSource,
            "mixed",
            StringComparison.Ordinal);

        var components = state.EntryEvidence == EntryEvidence.Exact && state.HasPriorSell
            ? new[]
            {
                CreateRemainingEntryPoolComponent(
                    $"paper-entry-remaining:PaperPosition:{position.PositionId:D}",
                    state,
                    replay.PoolId,
                    replay.LineagePayload)
            }
            : state.EntryEvidence == EntryEvidence.Exact
            ? new[]
            {
                CreateEntryPoolComponent(
                    $"paper-entry-remaining:PaperPosition:{position.PositionId:D}",
                    state.FeeUsd,
                    state,
                    replay.PoolId,
                    replay.LineagePayload)
            }
            : !state.HasPriorSell
                ? replay.OrderedFills
                    .Where(IsBuy)
                    .Where(IsAuthoritativeFillFee)
                    .Select(fill => CreateComponent(
                        $"paper-entry-remaining:PaperPosition:{position.PositionId:D}:{fill.FillId:D}",
                        $"paper-fill:{fill.FillId:D}:entry",
                        fill.FeeUsd,
                        fill.CanonicalPayloadJson))
                    .ToArray()
                : [];
        var poolExact = state.EntryEvidence == EntryEvidence.Exact &&
            Round8(state.FeeUsd) == position.FeeUsd;
        if (state.EntryEvidence == EntryEvidence.Exact && !poolExact)
        {
            AddConflict(
                conflicts,
                "paper_position_fee_pool_mismatch",
                HistoricalGrossNetParitySourceKind.PaperPosition,
                position.PositionId,
                position.StrategyId,
                "The exact replayed remaining Fee pool differs from the persisted position Fee.");
            return null;
        }

        var authoritative = IsAuthoritativeTotal(
            position.FeeUsd,
            position.FeeAccountingStatus,
            position.FeeCalculationSource,
            position.FeeLiquidityRole,
            position.FeeRate,
            position.FeeExponent,
            position.FeeTakerOnly,
            position.FeeCalculatedAtUtc) ||
            (mixedSource && poolExact);
        return CreateTarget(
            HistoricalGrossNetParitySourceKind.PaperPosition,
            position.PositionId,
            position.StrategyId,
            rank.Rank,
            rank.GrossPnlUsd,
            position.RowVersion,
            originatedAtUtc,
            null,
            position.UnrealizedPnlUsd,
            position.AveragePrice * position.SizeShares,
            position.FeeUsd,
            position.FeeAccountingStatus,
            position.FeeLiquidityRole,
            position.FeeCalculationSource,
            position.FeeRate,
            position.FeeExponent,
            position.FeeTakerOnly,
            position.FeeCalculatedAtUtc,
            position.NetUnrealizedPnlUsd,
            authoritative,
            components,
            replay.LineagePayload,
            position.CanonicalPayloadJson);
    }

    private static HistoricalGrossNetParityTargetSnapshot? PrepareSettlement(
        HistoricalGrossNetParityPaperSettlementObservation settlement,
        PreparedStrategyRank rank,
        PoolReplay replay,
        List<HistoricalGrossNetParityTargetConflict> conflicts)
    {
        if (replay.OrderedFills.Any(fill => fill.FilledAtUtc == settlement.SettledAtUtc))
        {
            AddConflict(
                conflicts,
                "paper_settlement_equal_timestamp_unordered",
                HistoricalGrossNetParitySourceKind.PaperSettlement,
                settlement.SettlementId,
                settlement.StrategyId,
                "A fill shares the settlement timestamp and no cross-table event order is proved.");
            return null;
        }

        var state = replay.StateStrictlyBefore(settlement.SettledAtUtc);
        if (state is null ||
            Round8(state.SizeShares) != settlement.SettledSizeShares ||
            Round8(state.AveragePrice) != settlement.AveragePrice ||
            Round8(state.AveragePrice * state.SizeShares) != settlement.CostBasisUsd)
        {
            AddConflict(
                conflicts,
                "paper_settlement_replay_mismatch",
                HistoricalGrossNetParitySourceKind.PaperSettlement,
                settlement.SettlementId,
                settlement.StrategyId,
                "Replayed pre-settlement size/average/basis differs from persisted settlement facts.");
            return null;
        }

        if (!TryClassifyOrigin(
                state,
                HistoricalGrossNetParitySourceKind.PaperSettlement,
                settlement.SettlementId,
                settlement.StrategyId,
                conflicts,
                out var originatedAtUtc))
        {
            return null;
        }

        if (state.EntryEvidence == EntryEvidence.Mixed && state.HasPriorSell)
        {
            AddConflict(
                conflicts,
                "paper_settlement_component_partition_ambiguous",
                HistoricalGrossNetParitySourceKind.PaperSettlement,
                settlement.SettlementId,
                settlement.StrategyId,
                "The settlement Fee pool mixes proved and unproved entry charges.");
            return null;
        }

        var mixedSource = string.Equals(
            settlement.FeeCalculationSource,
            "mixed",
            StringComparison.Ordinal);

        var components = state.EntryEvidence == EntryEvidence.Exact && state.HasPriorSell
            ? new[]
            {
                CreateRemainingEntryPoolComponent(
                    $"paper-entry-remaining:PaperSettlement:{settlement.SettlementId:D}",
                    state,
                    replay.PoolId,
                    replay.LineagePayloadBefore(settlement.SettledAtUtc))
            }
            : state.EntryEvidence == EntryEvidence.Exact
            ? new[]
            {
                CreateEntryPoolComponent(
                    $"paper-entry-remaining:PaperSettlement:{settlement.SettlementId:D}",
                    state.FeeUsd,
                    state,
                    replay.PoolId,
                    replay.LineagePayloadBefore(settlement.SettledAtUtc))
            }
            : !state.HasPriorSell
                ? replay.OrderedFills
                    .Where(fill => fill.FilledAtUtc < settlement.SettledAtUtc)
                    .Where(IsBuy)
                    .Where(IsAuthoritativeFillFee)
                    .Select(fill => CreateComponent(
                        $"paper-entry-remaining:PaperSettlement:{settlement.SettlementId:D}:{fill.FillId:D}",
                        $"paper-fill:{fill.FillId:D}:entry",
                        fill.FeeUsd,
                        fill.CanonicalPayloadJson))
                    .ToArray()
                : [];
        var poolExact = state.EntryEvidence == EntryEvidence.Exact &&
            Round8(state.FeeUsd) == settlement.FeeUsd;
        if (state.EntryEvidence == EntryEvidence.Exact && !poolExact)
        {
            AddConflict(
                conflicts,
                "paper_settlement_fee_pool_mismatch",
                HistoricalGrossNetParitySourceKind.PaperSettlement,
                settlement.SettlementId,
                settlement.StrategyId,
                "The exact replayed remaining Fee pool differs from the persisted settlement Fee.");
            return null;
        }

        var authoritative = IsAuthoritativeTotal(
            settlement.FeeUsd,
            settlement.FeeAccountingStatus,
            settlement.FeeCalculationSource,
            settlement.FeeLiquidityRole,
            settlement.FeeRate,
            settlement.FeeExponent,
            settlement.FeeTakerOnly,
            settlement.FeeCalculatedAtUtc) ||
            (mixedSource && poolExact);
        return CreateTarget(
            HistoricalGrossNetParitySourceKind.PaperSettlement,
            settlement.SettlementId,
            settlement.StrategyId,
            rank.Rank,
            rank.GrossPnlUsd,
            settlement.RowVersion,
            originatedAtUtc,
            settlement.SettledAtUtc,
            settlement.RealizedPnlUsd,
            settlement.CostBasisUsd,
            settlement.FeeUsd,
            settlement.FeeAccountingStatus,
            settlement.FeeLiquidityRole,
            settlement.FeeCalculationSource,
            settlement.FeeRate,
            settlement.FeeExponent,
            settlement.FeeTakerOnly,
            settlement.FeeCalculatedAtUtc,
            settlement.NetRealizedPnlUsd,
            authoritative,
            components,
            replay.LineagePayloadBefore(settlement.SettledAtUtc),
            settlement.CanonicalPayloadJson);
    }

    private static HistoricalGrossNetParityTargetSnapshot? PrepareSell(
        HistoricalGrossNetParityPaperFillObservation fill,
        PreparedStrategyRank rank,
        SellReplay sell,
        List<HistoricalGrossNetParityTargetConflict> conflicts)
    {
        if (fill.RealizedPnlUsd != sell.GrossPnlUsd)
        {
            AddConflict(
                conflicts,
                "paper_sell_gross_replay_mismatch",
                HistoricalGrossNetParitySourceKind.PaperSellFill,
                fill.FillId,
                fill.StrategyId,
                "Persisted SELL Gross does not equal the engine-equivalent replayed Gross8.");
            return null;
        }

        if (!TryClassifyOrigin(
                sell.Before,
                HistoricalGrossNetParitySourceKind.PaperSellFill,
                fill.FillId,
                fill.StrategyId,
                conflicts,
                out var originatedAtUtc))
        {
            return null;
        }

        if (sell.Before.EntryEvidence == EntryEvidence.Mixed)
        {
            AddConflict(
                conflicts,
                "paper_sell_component_partition_ambiguous",
                HistoricalGrossNetParitySourceKind.PaperSellFill,
                fill.FillId,
                fill.StrategyId,
                "The SELL input Fee pool mixes proved and unproved entry charges.");
            return null;
        }

        var components = new List<HistoricalGrossNetParityComponentAllocationV1>();
        if (sell.Before.EntryEvidence == EntryEvidence.Exact)
        {
            if (sell.EffectiveEntrySliceUsd < 0m)
            {
                AddConflict(
                    conflicts,
                    "paper_sell_negative_effective_entry_slice",
                    HistoricalGrossNetParitySourceKind.PaperSellFill,
                    fill.FillId,
                    fill.StrategyId,
                    "The persisted Gross8/Net8 replay produced a negative effective entry slice.");
                return null;
            }

            components.Add(sell.EntryAllocation ?? throw new InvalidOperationException(
                $"Exact SELL {fill.FillId:D} has no canonical entry-pool allocation graph."));
        }

        var exitExact = IsAuthoritativeFillFee(fill);
        if (exitExact)
        {
            components.Add(CreateComponent(
                $"paper-exit-allocation:{fill.FillId:D}",
                $"paper-fill:{fill.FillId:D}:exit",
                fill.FeeUsd,
                fill.CanonicalPayloadJson));
        }

        var replayExact = sell.Before.EntryEvidence == EntryEvidence.Exact && exitExact;
        var effectiveFee = replayExact ? sell.EffectiveFeeUsd : (decimal?)null;
        var authoritative = replayExact || IsAuthoritativeTotal(
            fill.FeeUsd,
            fill.FeeAccountingStatus,
            fill.FeeCalculationSource,
            fill.FeeLiquidityRole,
            fill.FeeRate,
            fill.FeeExponent,
            fill.FeeTakerOnly,
            fill.FeeCalculatedAtUtc) && sell.Before.EntryEvidence == EntryEvidence.Exact;
        return CreateTarget(
            HistoricalGrossNetParitySourceKind.PaperSellFill,
            fill.FillId,
            fill.StrategyId,
            rank.Rank,
            rank.GrossPnlUsd,
            fill.FillRowVersion,
            originatedAtUtc,
            fill.FilledAtUtc,
            fill.RealizedPnlUsd,
            (fill.FillPrice * fill.FillSizeShares) - fill.RealizedPnlUsd,
            fill.FeeUsd,
            fill.FeeAccountingStatus,
            fill.FeeLiquidityRole,
            fill.FeeCalculationSource,
            fill.FeeRate,
            fill.FeeExponent,
            fill.FeeTakerOnly,
            fill.FeeCalculatedAtUtc,
            fill.NetRealizedPnlUsd,
            authoritative,
            components,
            sell.LineagePayload,
            fill.CanonicalPayloadJson,
            authoritativeEffectiveFeeUsd: effectiveFee);
    }

    private static HistoricalGrossNetParityTargetSnapshot CreateTarget(
        HistoricalGrossNetParitySourceKind sourceKind,
        Guid sourceId,
        Guid strategyId,
        int strategyRank,
        decimal strategyGrossPnlUsd,
        long rowVersion,
        DateTimeOffset originatedAtUtc,
        DateTimeOffset? settledAtUtc,
        decimal grossPnlUsd,
        decimal grossRoiBasisUsd,
        decimal feeUsd,
        string feeAccountingStatus,
        string feeLiquidityRole,
        string feeCalculationSource,
        decimal? feeRate,
        int? feeExponent,
        bool? feeTakerOnly,
        DateTimeOffset? feeCalculatedAtUtc,
        decimal? netPnlUsd,
        bool authoritative,
        IReadOnlyList<HistoricalGrossNetParityComponentAllocationV1> components,
        string lineagePayload,
        string canonicalPayload,
        decimal? authoritativeEffectiveFeeUsd = null)
    {
        var contributionEffectiveFee = sourceKind == HistoricalGrossNetParitySourceKind.PaperSellFill &&
            netPnlUsd is not null
                ? grossPnlUsd - netPnlUsd.Value
                : feeUsd;
        var existingComplete = FeeAccountingRules.IsAccounted(feeAccountingStatus) &&
            netPnlUsd is not null &&
            contributionEffectiveFee >= 0m &&
            Round8(grossPnlUsd - contributionEffectiveFee) == netPnlUsd.Value &&
            (sourceKind != HistoricalGrossNetParitySourceKind.PaperSellFill ||
                contributionEffectiveFee >= feeUsd);
        var eligibility = existingComplete
            ? HistoricalGrossNetParityExactEligibility.ExistingExactPreserved
            : authoritative
                ? HistoricalGrossNetParityExactEligibility.AuthoritativeNetRepair
                : HistoricalGrossNetParityExactEligibility.FallbackRequired;
        var componentPayload = JsonSerializer.Serialize(components);
        var canonicalLineagePayload = NormalizeJson(lineagePayload);
        var lineageHash = Hash(canonicalLineagePayload);
        var componentHash = HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash(components);
        var targetHash = Hash(canonicalPayload);
        var bindingHash = HistoricalGrossNetParityBindingV1.Compute(
            targetHash,
            lineageHash,
            componentHash);
        var exactReferences = authoritative
            ? new[]
            {
                new HistoricalGrossNetParityEvidenceReferenceV1(
                    "canonical-paper-accounting",
                    feeCalculationSource,
                    Hash(canonicalPayload),
                    sourceKind,
                    sourceId)
            }
            : [];
        return new HistoricalGrossNetParityTargetSnapshot(
            sourceKind,
            sourceId,
            strategyId,
            strategyRank,
            strategyGrossPnlUsd,
            rowVersion,
            originatedAtUtc,
            settledAtUtc,
            grossPnlUsd,
            grossRoiBasisUsd,
            feeUsd,
            feeAccountingStatus,
            feeLiquidityRole,
            feeCalculationSource,
            feeRate,
            feeExponent,
            feeTakerOnly,
            feeCalculatedAtUtc,
            netPnlUsd,
            false,
            HistoricalGrossNetParityOwnership.None,
            targetHash,
            lineageHash,
            componentHash,
            eligibility,
            authoritativeEffectiveFeeUsd ?? (authoritative ? contributionEffectiveFee : null),
            exactReferences,
            null,
            null,
            Round8(components.Sum(component => component.AmountUsd)),
            components,
            null,
            null,
            null,
            canonicalPayload,
            canonicalLineagePayload,
            componentPayload,
            bindingHash);
    }

    private static PoolReplay ReplayPool(
        PoolKey key,
        IEnumerable<HistoricalGrossNetParityPaperFillObservation> fills,
        DateTimeOffset cutoffUtc,
        List<HistoricalGrossNetParityTargetConflict> conflicts)
    {
        var ordered = fills
            .OrderBy(fill => fill.FilledAtUtc)
            .ThenBy(fill => fill.PaperOrderId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(fill => fill.FillId.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        var state = PoolState.Empty;
        var events = new List<ReplayEvent>(ordered.Length);
        var sells = new Dictionary<Guid, SellReplay>();
        foreach (var fill in ordered)
        {
            var before = state;
            if (fill.FillSizeShares <= 0m)
            {
                AddConflict(
                    conflicts,
                    "paper_fill_nonpositive_size",
                    IsSell(fill) ? HistoricalGrossNetParitySourceKind.PaperSellFill : null,
                    fill.FillId,
                    fill.StrategyId,
                    "A replayed Paper fill has nonpositive size.");
                events.Add(new ReplayEvent(fill, before, before));
                continue;
            }

            if (IsBuy(fill))
            {
                state = ReplayBuy(before, fill, cutoffUtc);
            }
            else if (IsSell(fill))
            {
                var result = ReplaySell(before, fill, key);
                state = result.After;
                sells[fill.FillId] = result;
            }
            else
            {
                AddConflict(
                    conflicts,
                    "paper_fill_side_invalid",
                    null,
                    fill.FillId,
                    fill.StrategyId,
                    $"Paper fill side '{fill.OrderSide}' is outside BUY/SELL replay semantics.");
            }

            events.Add(new ReplayEvent(fill, before, state));
        }

        return new PoolReplay(key, ordered, events, sells, state);
    }

    private static PoolState ReplayBuy(
        PoolState current,
        HistoricalGrossNetParityPaperFillObservation fill,
        DateTimeOffset cutoffUtc)
    {
        var newSize = Round8(current.SizeShares + fill.FillSizeShares);
        if (newSize <= 0m)
        {
            throw new InvalidOperationException("A BUY replay produced nonpositive position size.");
        }

        var existingCost = current.SizeShares * current.AveragePrice;
        var fillCost = fill.FillPrice * fill.FillSizeShares;
        var average = Round8((existingCost + fillCost) / newSize);
        var fee = Round8(current.FeeUsd + fill.FeeUsd);
        var chargeAmount = fee - current.FeeUsd;
        if (chargeAmount != Round8(fill.FeeUsd))
        {
            throw new InvalidOperationException(
                $"BUY {fill.FillId:D} Fee does not reproduce the persisted numeric(28,8) pool transition.");
        }

        var sourceChargeId = $"paper-fill:{fill.FillId:D}:entry";
        var sourceEvidence = JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityPaperBuySourceChargeV1",
            fill.FillId,
            fill.PaperOrderId,
            sourceChargeId,
            amountUsd = chargeAmount,
            fill.CanonicalPayloadJson
        });
        var entryCharge = new EntryCharge(
            sourceChargeId,
            chargeAmount,
            Hash(sourceEvidence),
            sourceEvidence);
        var fillOrigin = Rational.FromDecimal(fill.FillSizeShares);
        var evidence = CombineEvidence(current.EntryEvidence, IsAuthoritativeFillFee(fill));
        return current with
        {
            SizeShares = newSize,
            AveragePrice = average,
            FeeUsd = fee,
            EntryCharges = current.EntryCharges.Append(entryCharge).ToArray(),
            PreCutoffShares = fill.FilledAtUtc < cutoffUtc
                ? current.PreCutoffShares.Add(fillOrigin)
                : current.PreCutoffShares,
            PostCutoffShares = fill.FilledAtUtc < cutoffUtc
                ? current.PostCutoffShares
                : current.PostCutoffShares.Add(fillOrigin),
            EntryEvidence = evidence,
            EarliestPreCutoffOriginUtc = fill.FilledAtUtc < cutoffUtc
                ? Min(current.EarliestPreCutoffOriginUtc, fill.FilledAtUtc)
                : current.EarliestPreCutoffOriginUtc,
            HasPriorSell = current.HasPriorSell
        };
    }

    private static SellReplay ReplaySell(
        PoolState current,
        HistoricalGrossNetParityPaperFillObservation fill,
        PoolKey key)
    {
        if (current.SizeShares <= 0m)
        {
            throw new InvalidOperationException(
                $"SELL {fill.FillId:D} has no positive replayed wallet/asset pool.");
        }

        var sellSize = Round8(fill.FillSizeShares);
        var currentSize = Round8(current.SizeShares);
        var sellFraction = Math.Min(1m, sellSize / currentSize);
        var poolAllocatedRaw = current.FeeUsd * sellFraction;
        var grossRaw = (fill.FillPrice - current.AveragePrice) * sellSize;
        var netRaw = grossRaw - poolAllocatedRaw - fill.FeeUsd;
        var gross8 = Round8(grossRaw);
        var net8 = Round8(netRaw);
        var effectiveFee8 = gross8 - net8;
        var effectiveEntrySlice8 = effectiveFee8 - fill.FeeUsd;
        var newSize = Round8(Math.Max(0m, currentSize - sellSize));
        var remainingFraction = Math.Max(0m, Math.Min(1m, newSize / currentSize));
        var remainingPool8 = Round8(current.FeeUsd * remainingFraction);
        var poolDecrement8 = current.FeeUsd - remainingPool8;
        var residual8 = effectiveEntrySlice8 - poolDecrement8;
        var remaining = Rational.FromDecimal(newSize).Divide(Rational.FromDecimal(currentSize));
        var poolId = "paper-entry-pool:" + Hash(JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityPaperEntryPoolV1",
            key.Wallet,
            key.AssetId
        }));
        var movementEvidence = JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityPaperSellPoolMovementV1",
            key.Wallet,
            key.AssetId,
            fill.FillId,
            poolId,
            poolAllocatedRaw,
            remainingBeforeUsd = current.FeeUsd,
            poolDecrement8,
            remainingPool8,
            residual8,
            effectiveEntrySlice8
        });
        var movement = new HistoricalGrossNetParityPoolMovementV1(
            poolId,
            poolAllocatedRaw,
            current.FeeUsd,
            poolDecrement8,
            remainingPool8,
            residual8,
            Hash(movementEvidence),
            movementEvidence);
        var entryAllocation = current.EntryEvidence == EntryEvidence.Exact &&
            effectiveEntrySlice8 >= 0m
                ? CreateEntryPoolComponent(
                    $"paper-entry-allocation:{fill.FillId:D}",
                    effectiveEntrySlice8,
                    current,
                    poolId,
                    movementEvidence,
                    movement)
                : null;
        var after = current with
        {
            SizeShares = newSize,
            AveragePrice = newSize == 0m ? 0m : current.AveragePrice,
            FeeUsd = newSize == 0m ? 0m : remainingPool8,
            EntryCharges = newSize == 0m ? [] : current.EntryCharges,
            PreCutoffShares = current.PreCutoffShares.Multiply(remaining),
            PostCutoffShares = current.PostCutoffShares.Multiply(remaining),
            EntryEvidence = newSize == 0m ? EntryEvidence.None : current.EntryEvidence,
            EarliestPreCutoffOriginUtc = newSize == 0m ? null : current.EarliestPreCutoffOriginUtc,
            HasPriorSell = true
        };
        var lineagePayload = JsonSerializer.Serialize(new
        {
            key.Wallet,
            key.AssetId,
            fill.FillId,
            before = current,
            poolAllocatedRaw,
            grossRaw,
            netRaw,
            gross8,
            net8,
            effectiveFee8,
            effectiveEntrySlice8,
            remainingPool8,
            poolDecrement8,
            residual8,
            entryAllocation = entryAllocation?.EvidenceJson
        });
        return new SellReplay(
            current,
            after,
            gross8,
            net8,
            effectiveFee8,
            effectiveEntrySlice8,
            entryAllocation,
            lineagePayload);
    }

    private static bool TryGetPaperScope(
        Guid strategyId,
        IReadOnlyDictionary<Guid, PreparedStrategyRank> ranks,
        IReadOnlyDictionary<Guid, HistoricalGrossNetParityPaperSourceSelection> selections,
        List<HistoricalGrossNetParityTargetConflict> conflicts,
        out PreparedStrategyRank rank,
        out HistoricalGrossNetParityPaperSourceSelection selection)
    {
        if (!ranks.TryGetValue(strategyId, out rank!))
        {
            AddConflict(
                conflicts,
                "paper_strategy_rank_missing",
                null,
                null,
                strategyId,
                "A Paper observation has no bounded-page Gross-rank entry.");
            selection = null!;
            return false;
        }

        if (!selections.TryGetValue(strategyId, out selection!))
        {
            AddConflict(
                conflicts,
                "paper_source_selection_missing",
                null,
                null,
                strategyId,
                "A Paper observation has no bounded-page uses-runs source-selection proof.");
            return false;
        }

        return true;
    }

    private static bool TryClassifyOrigin(
        PoolState state,
        HistoricalGrossNetParitySourceKind sourceKind,
        Guid sourceId,
        Guid strategyId,
        List<HistoricalGrossNetParityTargetConflict> conflicts,
        out DateTimeOffset originatedAtUtc)
    {
        originatedAtUtc = state.EarliestPreCutoffOriginUtc ?? default;
        var hasPre = state.PreCutoffShares.IsPositive;
        var hasPost = state.PostCutoffShares.IsPositive;
        if (hasPre && hasPost)
        {
            AddConflict(
                conflicts,
                "paper_originating_pool_mixed_cutoff",
                sourceKind,
                sourceId,
                strategyId,
                "The exact-rational originating share pool contains both pre- and post-cutoff residue.");
            return false;
        }

        if (hasPost)
        {
            return false;
        }

        if (!hasPre || state.EarliestPreCutoffOriginUtc is null)
        {
            AddConflict(
                conflicts,
                "paper_originating_pool_unproved",
                sourceKind,
                sourceId,
                strategyId,
                "The Gross-selected Paper contribution has no provable originating BUY shares.");
            return false;
        }

        originatedAtUtc = state.EarliestPreCutoffOriginUtc.Value;
        return true;
    }

    private static HistoricalGrossNetParityComponentAllocationV1 CreateComponent(
        string allocationId,
        string sourceChargeId,
        decimal amountUsd,
        string evidencePayload)
    {
        var roundedAmount = Round8(amountUsd);
        var sourceEvidence = JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityDirectSourceChargeV1",
            allocationId,
            sourceChargeId,
            amountUsd = roundedAmount,
            evidencePayload
        });
        var poolId = "canonical-direct:" + Hash(sourceChargeId);
        var edgeEvidence = JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityDirectCoverageV1",
            sourceChargeId,
            poolId,
            allocationId
        });
        return HistoricalGrossNetParityComponentGraphV1.Create(
            allocationId,
            roundedAmount,
            [new HistoricalGrossNetParitySourceChargeV1(
                sourceChargeId,
                roundedAmount,
                Hash(sourceEvidence),
                sourceEvidence)],
            [new HistoricalGrossNetParityChargeCoverageEdgeV1(
                sourceChargeId,
                poolId,
                allocationId,
                Hash(edgeEvidence),
                edgeEvidence)]);
    }

    private static HistoricalGrossNetParityComponentAllocationV1 CreateEntryPoolComponent(
        string allocationId,
        decimal effectiveAmountUsd,
        PoolState state,
        string poolId,
        string evidencePayload,
        HistoricalGrossNetParityPoolMovementV1? movement = null)
    {
        if (state.EntryCharges.Count == 0)
        {
            throw new InvalidOperationException(
                $"Entry-pool allocation {allocationId} has no originating BUY source charges.");
        }

        var sourceCharges = state.EntryCharges
            .Select(charge => new HistoricalGrossNetParitySourceChargeV1(
                charge.SourceChargeId,
                charge.OriginalAmountUsd,
                charge.EvidenceHash,
                charge.EvidenceJson))
            .ToArray();
        var contextEvidenceHash = Hash(evidencePayload);
        var edges = state.EntryCharges.Select(charge =>
        {
            var edgeEvidence = JsonSerializer.Serialize(new
            {
                version = "HistoricalGrossNetParityEntryPoolCoverageV1",
                charge.SourceChargeId,
                poolId,
                allocationId,
                chargeEvidenceHash = charge.EvidenceHash,
                contextEvidenceHash
            });
            return new HistoricalGrossNetParityChargeCoverageEdgeV1(
                charge.SourceChargeId,
                poolId,
                allocationId,
                Hash(edgeEvidence),
                edgeEvidence);
        }).ToArray();
        return HistoricalGrossNetParityComponentGraphV1.Create(
            allocationId,
            Round8(effectiveAmountUsd),
            sourceCharges,
            edges,
            movement);
    }

    private static HistoricalGrossNetParityComponentAllocationV1 CreateRemainingEntryPoolComponent(
        string allocationId,
        PoolState state,
        string poolId,
        string evidencePayload)
    {
        var remainingFee = Round8(state.FeeUsd);
        var movementEvidence = JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityRemainingEntryPoolMovementV1",
            allocationId,
            poolId,
            remainingFee,
            contextEvidenceHash = Hash(evidencePayload)
        });
        var movement = new HistoricalGrossNetParityPoolMovementV1(
            poolId,
            remainingFee,
            remainingFee,
            remainingFee,
            0m,
            0m,
            Hash(movementEvidence),
            movementEvidence);
        return CreateEntryPoolComponent(
            allocationId,
            remainingFee,
            state,
            poolId,
            evidencePayload,
            movement);
    }

    private static bool IsAuthoritativeFillFee(
        HistoricalGrossNetParityPaperFillObservation fill) =>
        IsAuthoritativeTotal(
            fill.FeeUsd,
            fill.FeeAccountingStatus,
            fill.FeeCalculationSource,
            fill.FeeLiquidityRole,
            fill.FeeRate,
            fill.FeeExponent,
            fill.FeeTakerOnly,
            fill.FeeCalculatedAtUtc);

    private static bool IsAuthoritativeTotal(
        decimal feeUsd,
        string status,
        string source,
        string role,
        decimal? rate,
        int? exponent,
        bool? takerOnly,
        DateTimeOffset? calculatedAtUtc)
    {
        if (feeUsd < 0m || calculatedAtUtc is null)
        {
            return false;
        }

        var parsedStatus = FeeAccountingRules.ParseStatus(status);
        if (parsedStatus == FeeAccountingStatus.VenueReported)
        {
            // The bounded Paper observations do not carry an independently associated
            // venue-authority record. Status/source text alone is not authoritative evidence.
            return false;
        }

        if (parsedStatus != FeeAccountingStatus.Calculated)
        {
            return false;
        }

        var exactSource = source.StartsWith(ExactHistoricalPrefix, StringComparison.Ordinal)
            ? source[ExactHistoricalPrefix.Length..]
            : source;
        if (string.Equals(
                exactSource,
                PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
                StringComparison.Ordinal))
        {
            return FeeAccountingRules.ParseLiquidityRole(role) != FeeLiquidityRole.Unknown &&
                rate is not null && exponent is not null && takerOnly is not null;
        }

        return string.Equals(
                exactSource,
                PolymarketFeeCalculationConstants.FeeFreeMarketCalculationSource,
                StringComparison.Ordinal) &&
            feeUsd == 0m;
    }

    private static EntryEvidence CombineEvidence(EntryEvidence current, bool exact)
    {
        var next = exact ? EntryEvidence.Exact : EntryEvidence.Unproved;
        if (current == EntryEvidence.None)
        {
            return next;
        }

        return current == next ? current : EntryEvidence.Mixed;
    }

    private static bool IsBuy(HistoricalGrossNetParityPaperFillObservation fill) =>
        string.Equals(fill.OrderSide, TradeSide.Buy.ToString(), StringComparison.OrdinalIgnoreCase);

    private static bool IsSell(HistoricalGrossNetParityPaperFillObservation fill) =>
        string.Equals(fill.OrderSide, TradeSide.Sell.ToString(), StringComparison.OrdinalIgnoreCase);

    private static T[] UniqueBy<T, TKey>(
        IEnumerable<T> values,
        Func<T, TKey> keySelector,
        string kind,
        List<HistoricalGrossNetParityTargetConflict> conflicts)
        where TKey : notnull
    {
        var result = new List<T>();
        foreach (var group in values.GroupBy(keySelector))
        {
            var items = group.ToArray();
            result.Add(items[0]);
            if (items.Length != 1)
            {
                AddConflict(
                    conflicts,
                    "duplicate_" + kind.Replace(' ', '_'),
                    null,
                    null,
                    null,
                    $"Bounded candidate page contains duplicate {kind} key '{group.Key}'.");
            }
        }

        return result.ToArray();
    }

    private static void AddConflict(
        List<HistoricalGrossNetParityTargetConflict> conflicts,
        string code,
        HistoricalGrossNetParitySourceKind? sourceKind,
        Guid? sourceId,
        Guid? strategyId,
        string details)
    {
        conflicts.Add(new HistoricalGrossNetParityTargetConflict(
            code,
            sourceKind,
            sourceId,
            strategyId,
            details));
    }

    private static DateTimeOffset? Min(DateTimeOffset? left, DateTimeOffset right) =>
        left is null || right < left.Value ? right : left;

    private static decimal Round8(decimal value) =>
        Math.Round(value, 8, MidpointRounding.AwayFromZero);

    private static string NormalizeJson(string value)
    {
        using var document = JsonDocument.Parse(value);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)))
            .ToLowerInvariant();

    private readonly record struct PoolKey(string Wallet, string AssetId);

    private sealed class PoolKeyComparer : IEqualityComparer<PoolKey>
    {
        public static PoolKeyComparer Instance { get; } = new();

        public bool Equals(PoolKey left, PoolKey right) =>
            string.Equals(left.Wallet, right.Wallet, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.AssetId, right.AssetId, StringComparison.Ordinal);

        public int GetHashCode(PoolKey value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Wallet),
            StringComparer.Ordinal.GetHashCode(value.AssetId));
    }

    private enum EntryEvidence
    {
        None,
        Exact,
        Unproved,
        Mixed
    }

    private sealed record PoolState(
        decimal SizeShares,
        decimal AveragePrice,
        decimal FeeUsd,
        IReadOnlyList<EntryCharge> EntryCharges,
        Rational PreCutoffShares,
        Rational PostCutoffShares,
        EntryEvidence EntryEvidence,
        DateTimeOffset? EarliestPreCutoffOriginUtc,
        bool HasPriorSell)
    {
        public static PoolState Empty { get; } = new(
            0m,
            0m,
            0m,
            [],
            Rational.Zero,
            Rational.Zero,
            EntryEvidence.None,
            null,
            false);
    }

    private sealed record ReplayEvent(
        HistoricalGrossNetParityPaperFillObservation Fill,
        PoolState Before,
        PoolState After);

    private sealed record SellReplay(
        PoolState Before,
        PoolState After,
        decimal GrossPnlUsd,
        decimal NetPnlUsd,
        decimal EffectiveFeeUsd,
        decimal EffectiveEntrySliceUsd,
        HistoricalGrossNetParityComponentAllocationV1? EntryAllocation,
        string LineagePayload);

    private sealed record EntryCharge(
        string SourceChargeId,
        decimal OriginalAmountUsd,
        string EvidenceHash,
        string EvidenceJson);

    private sealed class PoolReplay(
        PoolKey key,
        IReadOnlyList<HistoricalGrossNetParityPaperFillObservation> orderedFills,
        IReadOnlyList<ReplayEvent> events,
        IReadOnlyDictionary<Guid, SellReplay> sells,
        PoolState finalState)
    {
        public IReadOnlyList<HistoricalGrossNetParityPaperFillObservation> OrderedFills { get; } = orderedFills;

        public IReadOnlyDictionary<Guid, SellReplay> Sells { get; } = sells;

        public PoolState FinalState { get; } = finalState;

        public string PoolId { get; } = "paper-entry-pool:" + Hash(JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityPaperEntryPoolV1",
            key.Wallet,
            key.AssetId
        }));

        public string LineagePayload => JsonSerializer.Serialize(new
        {
            key.Wallet,
            key.AssetId,
            fills = OrderedFills.Select(fill => fill.CanonicalPayloadJson)
        });

        public PoolState? StateStrictlyBefore(DateTimeOffset timestamp)
        {
            var selected = events.Where(value => value.Fill.FilledAtUtc < timestamp).LastOrDefault();
            return selected?.After ?? (events.Count == 0 ? null : PoolState.Empty);
        }

        public string LineagePayloadBefore(DateTimeOffset timestamp) => JsonSerializer.Serialize(new
        {
            key.Wallet,
            key.AssetId,
            fills = orderedFills
                .Where(fill => fill.FilledAtUtc < timestamp)
                .Select(fill => fill.CanonicalPayloadJson)
        });

    }

    private readonly record struct Rational(BigInteger Numerator, BigInteger Denominator)
    {
        public static Rational Zero { get; } = new(BigInteger.Zero, BigInteger.One);

        public bool IsPositive => Numerator.Sign > 0;

        public static Rational FromDecimal(decimal value)
        {
            var exact = HistoricalGrossNetExactDecimal.FromDecimal(value);
            return Normalize(exact.Significand, BigInteger.Pow(10, exact.Scale));
        }

        public Rational Add(Rational other) => Normalize(
            Numerator * other.Denominator + other.Numerator * Denominator,
            Denominator * other.Denominator);

        public Rational Multiply(Rational other) => Normalize(
            Numerator * other.Numerator,
            Denominator * other.Denominator);

        public Rational Divide(Rational other)
        {
            if (other.Numerator.IsZero)
            {
                throw new DivideByZeroException("Origin-share rational divisor is zero.");
            }

            return Normalize(
                Numerator * other.Denominator,
                Denominator * other.Numerator);
        }

        private static Rational Normalize(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.IsZero)
            {
                throw new DivideByZeroException("Origin-share rational denominator is zero.");
            }

            if (denominator.Sign < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            return divisor.IsZero
                ? Zero
                : new Rational(numerator / divisor, denominator / divisor);
        }
    }
}
