using System.Diagnostics;
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
        var reachedStrategyEnd = !wholeBatchDeferred && page.ReachedEnd;
        var reachedSweepEnd = !wholeBatchDeferred && AdvanceCursor(page);

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
            "DeferredByQueryCancel={DeferredByQueryCancel} ReachedStrategyEnd={ReachedStrategyEnd} " +
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

    private bool AdvanceCursor(HistoricalPaperFakFeeBackfillPage page)
    {
        if (!page.ReachedEnd)
        {
            continuationCursor = page.ContinuationCursor;
            return false;
        }

        continuationCursor = null;
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
    HistoricalPaperFakFeeBackfillBatchResult? ApplyResult);
