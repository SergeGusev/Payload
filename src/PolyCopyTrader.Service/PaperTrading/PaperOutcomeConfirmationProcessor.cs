using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperOutcomeConfirmationProcessor(
    ILogger<PaperOutcomeConfirmationProcessor> logger,
    IAppRepository repository,
    IPolymarketGammaClient gamma,
    IStrategyStateProvider strategies) : IPaperOutcomeConfirmationProcessor
{
    public async Task ProcessOneAsync(ServiceActivityState.BackgroundLease idle, CancellationToken cancellationToken = default)
    {
        var trace = idle.Trace;
        trace?.Enter(PaperConfirmationStage.IdleCheck);
        idle.CheckIdle();
        trace?.Enter(PaperConfirmationStage.Claim);
        var order = await repository.TryClaimPaperOutcomeConfirmationAsync(DateTimeOffset.UtcNow, cancellationToken);
        if (order is null) { trace?.Complete("NoCandidate", "QueueEmpty"); return; }
        trace?.SetOrder(order.Id);
        CancellationTokenSource? lookup = null;
        var lookupInProgress = false;
        try
        {
            trace?.Enter(PaperConfirmationStage.IdleCheck);
            idle.CheckIdle();
            lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lookup.CancelAfter(TimeSpan.FromSeconds(5));
            lookupInProgress = true;
            trace?.Enter(PaperConfirmationStage.GammaToken);
            var metadata = await gamma.GetTokenMetadataAsync(order.AssetId, closed: true, lookup.Token);
            if (metadata.Count == 0)
            {
                trace?.Enter(PaperConfirmationStage.GammaCondition);
                metadata = await gamma.GetTokenMetadataByConditionIdAsync(order.ConditionId, order.AssetId, closed: true, lookup.Token);
            }
            lookupInProgress = false;
            trace?.Enter(PaperConfirmationStage.IdleCheck);
            idle.CheckIdle();
            trace?.Enter(PaperConfirmationStage.ValidateOutcome);
            var confirmation = Resolve(order, metadata, DateTimeOffset.UtcNow);
            if (confirmation is null)
            {
                trace?.Complete("Deferred", "final_outcome_missing_or_identity_conflict");
                trace?.Enter(PaperConfirmationStage.Defer);
                await DeferAsync(order.Id, "final_outcome_missing_or_identity_conflict", cancellationToken);
                return;
            }
            using var cacheUpdate = (strategies as StrategyStateProvider)?.BeginPaperOutcomeUpdate();
            trace?.Enter(PaperConfirmationStage.ApplyDatabase);
            var result = await repository.ConfirmPaperOutcomeAsync(confirmation, cancellationToken, trace);
            if (!result.Confirmed)
            {
                trace?.Complete("Deferred", result.Reason);
                trace?.Enter(PaperConfirmationStage.IdleCheck);
                idle.CheckIdle();
                trace?.Enter(PaperConfirmationStage.Defer);
                await DeferAsync(order.Id, result.Reason, cancellationToken);
                return;
            }
            trace?.Complete(result.Corrected ? "Corrected" : "Matched", result.Reason);
            logger.LogInformation("Paper outcome confirmed: {PaperOrderId}, corrected={Corrected}, winner={WinningOutcome}",
                order.Id, result.Corrected, confirmation.WinningOutcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var timedOut = lookupInProgress && ex is OperationCanceledException && lookup?.IsCancellationRequested == true;
            var reason = timedOut ? "GammaTimeout" : ex is HttpRequestException ? "HttpError" :
                ex is Npgsql.NpgsqlException ? "DatabaseError" : "UnknownError";
            trace?.Error(ex.GetType().Name, (ex as Npgsql.PostgresException)?.SqlState);
            trace?.Complete(timedOut ? "Timeout" : "Error", reason);
            idle.CheckIdle();
            // Preserve the existing persisted retry payload; new telemetry excludes messages/payloads.
            logger.LogWarning("Paper outcome lookup/application failed for {PaperOrderId}. ErrorType={ErrorType} Reason={Reason} SqlState={SqlState}",
                order.Id, ex.GetType().Name, reason, (ex as Npgsql.PostgresException)?.SqlState);
            trace?.Enter(PaperConfirmationStage.Defer);
            await DeferAsync(order.Id, ex.GetType().Name + ": " + ex.Message, cancellationToken);
        }
        finally { lookup?.Dispose(); }
    }
    private Task DeferAsync(Guid id, string reason, CancellationToken token) =>
        repository.DeferPaperOutcomeConfirmationAsync(id, DateTimeOffset.UtcNow.AddMinutes(1), reason, token);

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
