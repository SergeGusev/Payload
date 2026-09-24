using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperOutcomeConfirmationProcessor(
    ILogger<PaperOutcomeConfirmationProcessor> logger,
    IAppRepository repository,
    IPolymarketGammaClient gamma,
    IStrategyStateProvider strategies, PaperConfirmationOptions? options = null) : IPaperOutcomeConfirmationProcessor
{
    private readonly PaperConfirmationOptions settings = options ?? new();
    public Task<PaperConfirmationProgress?> GetProgressAsync(CancellationToken cancellationToken)
        => repository.GetPaperConfirmationProgressAsync(cancellationToken);
    public Task<IReadOnlyList<PaperOrder>> ClaimBatchAsync(PaperConfirmationLane lane, Guid strategyId, int limit,
        CancellationToken cancellationToken) => repository.ClaimPaperConfirmationBatchAsync(
            lane, strategyId, DateTimeOffset.UtcNow, settings.RecentHours, limit, cancellationToken);
    public Task<PaperOrder?> ClaimAsync(CancellationToken cancellationToken) =>
        repository.TryClaimPaperOutcomeConfirmationAsync(DateTimeOffset.UtcNow, cancellationToken);

    // The worker owns admission and the shared token/condition lookup deadline.
    public async Task<PaperOutcomeConfirmation?> LookupAsync(PaperOrder order,
        PaperOutcomeConfirmationTrace trace, CancellationToken cancellationToken)
    {
        PaperConfirmationMarketEvidence? cached;
        trace.Enter(PaperConfirmationStage.MarketCacheRead);
        using (var cacheBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cacheBudget.CancelAfter(TimeSpan.FromSeconds(settings.DatabaseTimeoutSeconds));
            cached = await repository.GetPaperConfirmationMarketAsync(order.ConditionId, cacheBudget.Token);
        }
        if (cached is not null)
        {
            trace.CacheHit();
            trace.Enter(PaperConfirmationStage.ValidateOutcome);
            logger.LogDebug("Paper confirmation market cache hit. ConditionId={ConditionId}", order.ConditionId);
            return ResolveCached(order, cached);
        }
        trace.Enter(PaperConfirmationStage.GammaToken);
        trace.HttpCall();
        var metadata = await gamma.GetTokenMetadataAsync(order.AssetId, closed: true, cancellationToken);
        if (metadata.Count == 0)
        {
            trace.Enter(PaperConfirmationStage.GammaCondition);
            trace.HttpCall();
            metadata = await gamma.GetTokenMetadataByConditionIdAsync(order.ConditionId, order.AssetId,
                closed: true, cancellationToken);
        }
        trace.Enter(PaperConfirmationStage.ValidateOutcome);
        var resolved = Resolve(order, metadata, DateTimeOffset.UtcNow);
        if (resolved is null) return null;
        trace.Enter(PaperConfirmationStage.MarketCacheWrite);
        using var saveBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        saveBudget.CancelAfter(TimeSpan.FromSeconds(settings.DatabaseTimeoutSeconds));
        var persisted = await repository.SavePaperConfirmationMarketAsync(
            new(order.ConditionId, metadata, resolved.CheckedAtUtc), saveBudget.Token);
        return ResolveCached(order, persisted);
    }

    private static PaperOutcomeConfirmation? ResolveCached(PaperOrder order, PaperConfirmationMarketEvidence evidence)
    {
        var resolved = Resolve(order, evidence.Tokens, DateTimeOffset.UtcNow);
        return resolved is null ? null : resolved with
        {
            EvidenceJson = JsonSerializer.Serialize(new
            {
                source = "GammaFinalMarketCache", market_checked_at_utc = evidence.CheckedAtUtc,
                order_checked_at_utc = resolved.CheckedAtUtc,
                final_outcome = JsonSerializer.Deserialize<JsonElement>(resolved.EvidenceJson)
            })
        };
    }

    public async Task<PaperOutcomeConfirmationResult> ApplyGroupAsync(
        IReadOnlyList<PaperOutcomeConfirmation> confirmations, PaperOutcomeConfirmationTrace trace,
        CancellationToken cancellationToken)
    {
        using var cacheUpdate = (strategies as StrategyStateProvider)?.BeginPaperOutcomeUpdate();
        var result = await repository.ConfirmPaperOutcomeGroupAsync(confirmations, cancellationToken, trace);
        if (result.Confirmed)
            foreach (var confirmation in confirmations)
                logger.LogInformation("Paper outcome confirmed: {PaperOrderId}, corrected={Corrected}, winner={WinningOutcome}",
                    confirmation.PaperOrderId, result.Corrected, confirmation.WinningOutcome);
        return result;
    }

    public async Task<PaperOutcomeConfirmationResult> ApplyAsync(PaperOutcomeConfirmation confirmation,
        PaperOutcomeConfirmationTrace trace, CancellationToken cancellationToken)
    {
        // Never hold this scope while awaiting a later idle admission.
        using var cacheUpdate = (strategies as StrategyStateProvider)?.BeginPaperOutcomeUpdate();
        var result = await repository.ConfirmPaperOutcomeAsync(confirmation, cancellationToken, trace);
        if (result.Confirmed)
            logger.LogInformation("Paper outcome confirmed: {PaperOrderId}, corrected={Corrected}, winner={WinningOutcome}",
                confirmation.PaperOrderId, result.Corrected, confirmation.WinningOutcome);
        return result;
    }

    public Task DeferAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        repository.DeferPaperOutcomeConfirmationAsync(id, DateTimeOffset.UtcNow.AddMinutes(1), reason, cancellationToken);

    public static PaperOutcomeConfirmation? Resolve(PaperOrder order,
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata, DateTimeOffset checkedAtUtc)
    {
        var final = FinalMarketOutcomeEvidence.FromGamma(metadata, checkedAtUtc);
        return final is null || !final.Matches(order.ConditionId, order.AssetId, order.Outcome) ? null :
            new PaperOutcomeConfirmation(order.Id, order.ConditionId, order.AssetId, order.Outcome,
                final.WinningAssetId, final.WinningOutcome, checkedAtUtc, final.ToAuditJson());
    }
}
