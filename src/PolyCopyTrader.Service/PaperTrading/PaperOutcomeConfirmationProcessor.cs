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
    public Task<IReadOnlyList<PaperOrder>> ClaimBatchAsync(PaperConfirmationLane lane, int limit,
        CancellationToken cancellationToken) => repository.ClaimPaperConfirmationBatchAsync(
            lane, DateTimeOffset.UtcNow, settings.RecentHours, limit, cancellationToken);
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
        if (metadata.Count == 0 || metadata.Any(x => !x.LookupSucceeded || !x.Resolved || !x.Closed ||
            !string.Equals(x.ConditionId, order.ConditionId, StringComparison.OrdinalIgnoreCase))) return null;
        var own = metadata.FirstOrDefault(x => x.TokenId == order.AssetId &&
            string.Equals(x.Outcome, order.Outcome, StringComparison.OrdinalIgnoreCase));
        if (own is null || string.IsNullOrWhiteSpace(own.WinningOutcome) ||
            metadata.Any(x => !string.Equals(x.WinningOutcome, own.WinningOutcome, StringComparison.OrdinalIgnoreCase)) ||
            own.Outcomes.Count != own.ClobTokenIds.Count) return null;
        var winners = own.Outcomes.Select((outcome, index) => (outcome, index))
            .Where(x => string.Equals(x.outcome, own.WinningOutcome, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (winners.Length != 1) return null;
        var winner = own.ClobTokenIds[winners[0].index];
        if (string.IsNullOrWhiteSpace(winner) || !own.ClobTokenIds.Contains(order.AssetId)) return null;
        if (own.ClobTokenIds.Distinct().Count() != own.ClobTokenIds.Count ||
            own.Outcomes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != own.Outcomes.Count ||
            metadata.Any(x => x.MarketId != own.MarketId || !x.ClobTokenIds.SequenceEqual(own.ClobTokenIds) ||
                !x.Outcomes.SequenceEqual(own.Outcomes) || x.OutcomeIndex < 0 || x.OutcomeIndex >= own.Outcomes.Count ||
                own.ClobTokenIds[x.OutcomeIndex] != x.TokenId || own.Outcomes[x.OutcomeIndex] != x.Outcome)) return null;
        // The existing metadata parser uses Closed for Resolved and a >= .999
        // price heuristic. Confirmation additionally requires final oracle status.
        GammaFinalOutcome? final;
        string[]? prices;
        try
        {
            final = JsonSerializer.Deserialize<GammaFinalOutcome>(own.RawJson);
            prices = final?.OutcomePrices is null ? null : JsonSerializer.Deserialize<string[]>(final.OutcomePrices);
        }
        catch (JsonException) { return null; }
        if (final is null || final.UmaResolutionStatus is not ("resolved" or "settled") ||
            prices?.Length != own.Outcomes.Count || prices.Where((price, index) =>
                !decimal.TryParse(price, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ||
                value != (index == winners[0].index ? 1m : 0m)).Any()) return null;
        return new PaperOutcomeConfirmation(order.Id, order.ConditionId, order.AssetId, order.Outcome,
            winner, own.WinningOutcome, checkedAtUtc,
            JsonSerializer.Serialize(new { source = "GammaClosedMarket", market_id = own.MarketId,
                condition_id = own.ConditionId, token_id = own.TokenId, winning_asset_id = winner,
                winning_outcome = own.WinningOutcome, checked_at_utc = checkedAtUtc, response = own.RawJson }));
    }

    private sealed record GammaFinalOutcome(
        [property: JsonPropertyName("umaResolutionStatus")] string? UmaResolutionStatus,
        [property: JsonPropertyName("outcomePrices")] string? OutcomePrices);
}
