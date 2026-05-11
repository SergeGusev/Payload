using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.Strategies;

public sealed record BtcUpDown5mOutcomeQuote(
    string AssetId,
    string Outcome,
    decimal Price,
    int OutcomeIndex);

public static partial class BtcUpDown5mMarketAnalyzer
{
    public static bool IsCandidate(PolymarketGammaMarket market)
    {
        return IsBtcUpDown5mSlug(market.Slug) ||
            IsBtcUpDown5mSlug(market.EventSlug) ||
            string.Equals(market.SeriesSlug, "btc-up-or-down-5m", StringComparison.OrdinalIgnoreCase);
    }

    public static DateTimeOffset? GetWindowStartUtc(PolymarketGammaMarket market)
    {
        if (market.EventStartTimeUtc is { } eventStart)
        {
            return eventStart;
        }

        var slug = !string.IsNullOrWhiteSpace(market.Slug) ? market.Slug : market.EventSlug;
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var match = UpDown5mSlugRegex().Match(slug);
            if (match.Success &&
                long.TryParse(match.Groups["unix"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            }
        }

        return market.EndDateUtc?.AddMinutes(-5);
    }

    public static BtcUpDown5mOutcomeQuote? TrySelectLosingOutcome(PolymarketGammaMarket market)
    {
        return TrySelectLowerPricedOutcome(market);
    }

    public static BtcUpDown5mOutcomeQuote? TrySelectLowerPricedOutcome(PolymarketGammaMarket market)
    {
        var quotes = GetOutcomeQuotes(market);
        if (quotes.Count != 2)
        {
            return null;
        }

        var lowestPrice = quotes.Min(quote => quote.Price);
        var losers = quotes
            .Where(quote => quote.Price == lowestPrice)
            .ToArray();
        return losers.Length == 1 && losers[0].Price > 0m ? losers[0] : null;
    }

    public static BtcUpDown5mOutcomeQuote? TrySelectHigherPricedOutcome(PolymarketGammaMarket market)
    {
        var quotes = GetOutcomeQuotes(market);
        if (quotes.Count != 2)
        {
            return null;
        }

        var highestPrice = quotes.Max(quote => quote.Price);
        var leaders = quotes
            .Where(quote => quote.Price == highestPrice)
            .ToArray();
        return leaders.Length == 1 && leaders[0].Price > 0m ? leaders[0] : null;
    }

    public static IReadOnlyList<BtcUpDown5mOutcomeQuote> GetOutcomeQuotes(PolymarketGammaMarket market)
    {
        var prices = ParseDecimalArray(market.RawJson, "outcomePrices");
        if (market.Outcomes.Count == 0 ||
            market.ClobTokenIds.Count != market.Outcomes.Count ||
            prices.Count != market.Outcomes.Count)
        {
            return [];
        }

        var quotes = new List<BtcUpDown5mOutcomeQuote>(market.Outcomes.Count);
        for (var index = 0; index < market.Outcomes.Count; index++)
        {
            var assetId = market.ClobTokenIds[index];
            var outcome = market.Outcomes[index];
            var price = prices[index];
            if (string.IsNullOrWhiteSpace(assetId) ||
                string.IsNullOrWhiteSpace(outcome) ||
                price < 0m ||
                price > 1m)
            {
                return [];
            }

            quotes.Add(new BtcUpDown5mOutcomeQuote(assetId, outcome, price, index));
        }

        return quotes;
    }

    private static bool IsBtcUpDown5mSlug(string? slug)
    {
        return !string.IsNullOrWhiteSpace(slug) && UpDown5mSlugRegex().IsMatch(slug);
    }

    private static IReadOnlyList<decimal> ParseDecimalArray(string rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return [];
            }

            return ParseStringArray(property)
                .Select(value => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : (decimal?)null)
                .Where(value => value is not null)
                .Select(value => value!.Value)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement property)
    {
        if (property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            return property.EnumerateArray().Select(item => item.ToString()).ToArray();
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return [];
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Select(item => item.ToString()).ToArray()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    [GeneratedRegex("^btc-updown-5m-(?<unix>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpDown5mSlugRegex();
}
