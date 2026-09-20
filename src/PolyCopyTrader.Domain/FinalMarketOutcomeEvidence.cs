using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolyCopyTrader.Domain;

/// <summary>Validated venue finality, including the complete token/outcome identity.</summary>
public sealed class FinalMarketOutcomeEvidence
{
    private FinalMarketOutcomeEvidence(string marketId, string conditionId, string[] tokens,
        string[] outcomes, int winner, string source, string rawJson, DateTimeOffset observedAtUtc)
    {
        MarketId = marketId; ConditionId = conditionId; Tokens = Array.AsReadOnly(tokens);
        Outcomes = Array.AsReadOnly(outcomes); WinningAssetId = tokens[winner];
        WinningOutcome = outcomes[winner]; Source = source; RawJson = rawJson; ObservedAtUtc = observedAtUtc;
    }

    public string MarketId { get; }
    public string ConditionId { get; }
    public IReadOnlyList<string> Tokens { get; }
    public IReadOnlyList<string> Outcomes { get; }
    public string WinningAssetId { get; }
    public string WinningOutcome { get; }
    public string Source { get; }
    public string RawJson { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public bool Matches(string condition, string asset, string outcome)
    {
        var index = Tokens.ToList().IndexOf(asset);
        return ConditionId == condition && index >= 0 &&
            string.Equals(Outcomes[index], outcome, StringComparison.OrdinalIgnoreCase);
    }

    public string ToAuditJson() => JsonSerializer.Serialize(new
    {
        source = Source, market_id = MarketId, condition_id = ConditionId,
        winning_asset_id = WinningAssetId, winning_outcome = WinningOutcome,
        checked_at_utc = ObservedAtUtc, response = RawJson
    });

    public static FinalMarketOutcomeEvidence? FromGamma(
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata, DateTimeOffset checkedAtUtc)
    {
        if (metadata.Count == 0) return null;
        var own = metadata[0];
        if (!ValidMapping(own.MarketId, own.ConditionId, own.ClobTokenIds, own.Outcomes) ||
            metadata.Any(x => !x.LookupSucceeded || !x.Resolved || !x.Closed ||
                x.ConditionId != own.ConditionId || x.MarketId != own.MarketId ||
                x.WinningOutcome != own.WinningOutcome || !x.ClobTokenIds.SequenceEqual(own.ClobTokenIds) ||
                !x.Outcomes.SequenceEqual(own.Outcomes) || x.OutcomeIndex < 0 || x.OutcomeIndex >= own.Outcomes.Count ||
                own.ClobTokenIds[x.OutcomeIndex] != x.TokenId || own.Outcomes[x.OutcomeIndex] != x.Outcome)) return null;
        FinalMarketOutcomeEvidence? final = null;
        foreach (var row in metadata)
        {
            final = FromGamma(row.MarketId, row.ConditionId, row.ClobTokenIds, row.Outcomes,
                row.WinningOutcome, row.RawJson, checkedAtUtc);
            if (final is null) return null;
        }
        return final;
    }

    public static FinalMarketOutcomeEvidence? FromGamma(string marketId, string conditionId,
        IReadOnlyList<string> tokens, IReadOnlyList<string> outcomes, string? winner, string rawJson,
        DateTimeOffset checkedAtUtc)
    {
        if (!ValidMapping(marketId, conditionId, tokens, outcomes)) return null;
        var index = outcomes.ToList().FindIndex(x => string.Equals(x, winner, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        try
        {
            var final = JsonSerializer.Deserialize<GammaFinalOutcome>(rawJson);
            var prices = final?.OutcomePrices is null ? null : JsonSerializer.Deserialize<string[]>(final.OutcomePrices);
            if (final?.UmaResolutionStatus is not ("resolved" or "settled") || prices?.Length != outcomes.Count ||
                prices.Where((price, i) => !decimal.TryParse(price, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value) || value != (i == index ? 1m : 0m)).Any()) return null;
        }
        catch (JsonException) { return null; }
        return new(marketId, conditionId, tokens.ToArray(), outcomes.ToArray(), index,
            "GammaClosedMarket", rawJson, checkedAtUtc);
    }

    public static FinalMarketOutcomeEvidence? FromWebSocket(string marketId, string conditionId,
        IReadOnlyList<string> tokens, IReadOnlyList<string> outcomes, string rawJson, DateTimeOffset observedAtUtc)
    {
        if (!ValidMapping(marketId, conditionId, tokens, outcomes)) return null;
        try
        {
            var message = JsonSerializer.Deserialize<ResolvedEvent>(rawJson);
            if (message?.EventType != "market_resolved" || message.Market != conditionId) return null;
            var index = tokens.ToList().IndexOf(message.WinningAssetId ?? "");
            if (index < 0 || (message.WinningOutcome is not null && !string.Equals(outcomes[index], message.WinningOutcome, StringComparison.OrdinalIgnoreCase))) return null;
            if (message.Assets is { } assets && (assets.Distinct().Count() != assets.Length || assets.Any(x => !tokens.Contains(x)))) return null;
            return new(marketId, conditionId, tokens.ToArray(), outcomes.ToArray(), index,
                "MarketWebSocket", rawJson, observedAtUtc);
        }
        catch (JsonException) { return null; }
    }

    public static FinalMarketOutcomeEvidence? FromLedger(PolymarketGammaMarket market, CryptoUpDown5mWebSocketResolvedMarket row)
    {
        if (market.MarketId != row.MarketId || market.ConditionId != row.ConditionId) return null;
        if (row.Source == "MarketWebSocket")
            return FromWebSocket(market.MarketId, market.ConditionId, market.ClobTokenIds, market.Outcomes,
                row.RawJson, row.EventTimestampUtc) is { } final && final.WinningAssetId == row.WinningAssetId &&
                    final.WinningOutcome == row.WinningOutcome ? final : null;
        if (row.Source != "GammaClosedMarket") return null;
        try
        {
            var payload = JsonSerializer.Deserialize<GammaLedgerPayload>(row.RawJson);
            var final = payload?.Response is null ? null : FromGamma(market.MarketId, market.ConditionId,
                market.ClobTokenIds, market.Outcomes, row.WinningOutcome, payload.Response, row.EventTimestampUtc);
            return final?.WinningAssetId == row.WinningAssetId ? final : null;
        }
        catch (JsonException) { return null; }
    }

    private sealed record GammaLedgerPayload([property: JsonPropertyName("response")] string? Response);

    private static bool ValidMapping(string market, string condition, IReadOnlyList<string> tokens, IReadOnlyList<string> outcomes) =>
        !string.IsNullOrWhiteSpace(market) && !string.IsNullOrWhiteSpace(condition) && tokens.Count > 1 &&
        tokens.Count == outcomes.Count && !tokens.Any(string.IsNullOrWhiteSpace) && !outcomes.Any(string.IsNullOrWhiteSpace) &&
        tokens.Distinct().Count() == tokens.Count && outcomes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == outcomes.Count;

    private sealed record GammaFinalOutcome(
        [property: JsonPropertyName("umaResolutionStatus")] string? UmaResolutionStatus,
        [property: JsonPropertyName("outcomePrices")] string? OutcomePrices);
    private sealed record ResolvedEvent(
        [property: JsonPropertyName("event_type")] string? EventType,
        [property: JsonPropertyName("market")] string? Market,
        [property: JsonPropertyName("winning_asset_id")] string? WinningAssetId,
        [property: JsonPropertyName("winning_outcome")] string? WinningOutcome,
        [property: JsonPropertyName("assets_ids")] string[]? Assets);
}
