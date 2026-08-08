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

    private HistoricalPaperFakFeeBackfillCursor? continuationCursor;

    public async Task<PaperFakFeeBackfillCycleResult> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        var page = await repository.GetHistoricalPaperFakFeeBackfillCandidatesAsync(
            options.HistoricalCutoffUtc,
            options.BatchSize,
            continuationCursor,
            cancellationToken).ConfigureAwait(false);
        ValidatePage(page);

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

        AdvanceCursor(page);

        logger.LogInformation(
            "Historical Paper FAK fee backfill cycle completed. ApplyEnabled={ApplyEnabled} " +
            "CutoffUtc={CutoffUtc:O} Candidates={Candidates} EvaluatedForApply={EvaluatedForApply} " +
            "TransientLookupUnavailable={TransientLookupUnavailable} Requested={Requested} Eligible={Eligible} " +
            "FillsUpdated={FillsUpdated} RunsUpdated={RunsUpdated} PositionsUpdated={PositionsUpdated} " +
            "SettlementsUpdated={SettlementsUpdated} AlreadyApplied={AlreadyApplied} " +
            "ConflictsOrDeferred={ConflictsOrDeferred} ReachedEnd={ReachedEnd}",
            options.ApplyEnabled,
            options.HistoricalCutoffUtc,
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
            page.ReachedEnd);

        return new PaperFakFeeBackfillCycleResult(
            page.Candidates.Count,
            updates.Count,
            transientLookupUnavailable,
            page.ReachedEnd,
            options.ApplyEnabled,
            applyResult);
    }

    private void ValidatePage(HistoricalPaperFakFeeBackfillPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Candidates.Count > options.BatchSize)
        {
            throw new InvalidOperationException(
                "Historical Paper FAK fee backfill repository returned more candidates than requested.");
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

    private void AdvanceCursor(HistoricalPaperFakFeeBackfillPage page)
    {
        continuationCursor = page.ReachedEnd
            ? null
            : page.ContinuationCursor;
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
