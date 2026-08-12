using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public interface IPaperFakFeeBackfillProcessor
{
    Task<PaperFakFeeBackfillCycleResult> RunCycleAsync(
        CancellationToken cancellationToken = default);
}

public sealed class PaperFakFeeBackfillProcessor(
    ILogger<PaperFakFeeBackfillProcessor> logger,
    PaperFakFeeBackfillOptions options,
    IAppRepository repository,
    IPolymarketFeeAccountingService feeAccountingService) : IPaperFakFeeBackfillProcessor
{
    internal const string HistoricalCalculationSourcePrefix =
        "historical-current-paper-model-v1:";

    private IReadOnlyList<HistoricalPaperFakFeeBackfillStrategyRank>? strategyRanks;
    private int strategyRankIndex;
    private HistoricalPaperFakFeeBackfillCursor? continuationCursor;

    public async Task<PaperFakFeeBackfillCycleResult> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureStrategyRanksAsync(cancellationToken).ConfigureAwait(false);
        if (strategyRanks!.Count == 0)
        {
            ResetSweep();
            return new PaperFakFeeBackfillCycleResult(
                0,
                0,
                0,
                true,
                options.ApplyEnabled,
                null);
        }

        var activeRank = strategyRanks[strategyRankIndex];
        var activeRankPosition = strategyRankIndex + 1;
        var strategyCount = strategyRanks.Count;
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

        var reachedSweepEnd = AdvanceCursor(page);

        logger.LogInformation(
            "Historical Paper FAK fee backfill cycle completed. ApplyEnabled={ApplyEnabled} " +
            "CutoffUtc={CutoffUtc:O} StrategyRank={StrategyRank}/{StrategyCount} " +
            "StrategyId={StrategyId} StrategyCode={StrategyCode} GrossRealizedPnlUsd={GrossRealizedPnlUsd} " +
            "Candidates={Candidates} EvaluatedForApply={EvaluatedForApply} " +
            "TransientLookupUnavailable={TransientLookupUnavailable} Requested={Requested} Eligible={Eligible} " +
            "FillsUpdated={FillsUpdated} RunsUpdated={RunsUpdated} PositionsUpdated={PositionsUpdated} " +
            "SettlementsUpdated={SettlementsUpdated} AlreadyApplied={AlreadyApplied} " +
            "ConflictsOrDeferred={ConflictsOrDeferred} ReachedStrategyEnd={ReachedStrategyEnd} " +
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
            applyResult?.FillsUpdated ?? 0,
            applyResult?.RunsUpdated ?? 0,
            applyResult?.PositionsUpdated ?? 0,
            applyResult?.SettlementsUpdated ?? 0,
            applyResult?.AlreadyApplied ?? 0,
            applyResult?.ConflictsOrDeferred ?? 0,
            page.ReachedEnd,
            reachedSweepEnd);

        return new PaperFakFeeBackfillCycleResult(
            page.Candidates.Count,
            updates.Count,
            transientLookupUnavailable,
            reachedSweepEnd,
            options.ApplyEnabled,
            applyResult);
    }

    private async Task EnsureStrategyRanksAsync(CancellationToken cancellationToken)
    {
        if (strategyRanks is not null)
        {
            return;
        }

        var loadedRanks = await repository.GetHistoricalPaperFakFeeBackfillStrategyRanksAsync(
            options.HistoricalCutoffUtc,
            cancellationToken).ConfigureAwait(false);
        ValidateStrategyRanks(loadedRanks);
        strategyRanks = loadedRanks.ToArray();
        strategyRankIndex = 0;
        continuationCursor = null;

        logger.LogInformation(
            "Historical Paper FAK fee backfill Gross-PnL strategy ranking frozen for this sweep. " +
            "CutoffUtc={CutoffUtc:O} StrategyCount={StrategyCount}",
            options.HistoricalCutoffUtc,
            strategyRanks.Count);
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
}

public sealed record PaperFakFeeBackfillCycleResult(
    int Candidates,
    int EvaluatedForApply,
    int TransientLookupUnavailable,
    bool ReachedEnd,
    bool ApplyEnabled,
    HistoricalPaperFakFeeBackfillBatchResult? ApplyResult);
