using System.Globalization;
using System.Text.RegularExpressions;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.Strategies;

public static class CryptoUpDown5mMarketAnalyzer
{
    private static readonly Regex UpDown5mSlugRegexValue = new Regex(
        "^(?<asset>[a-z0-9]+)-updown-5m-(?<unix>\\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UpDown5mSeriesRegexValue = new Regex(
        "^(?<asset>[a-z0-9]+)-up-or-down-5m$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryGetAssetSymbol(
        PolymarketGammaMarket market,
        IReadOnlyCollection<string> allowedAssetSymbols,
        out string assetSymbol)
    {
        assetSymbol = string.Empty;
        foreach (var candidate in new[] { market.Slug, market.EventSlug, market.SeriesSlug })
        {
            if (TryGetAssetSymbol(candidate, out var symbol) &&
                allowedAssetSymbols.Contains(symbol))
            {
                assetSymbol = symbol;
                return true;
            }
        }

        return false;
    }

    public static DateTimeOffset? GetWindowStartUtc(PolymarketGammaMarket market)
    {
        if (market.EventStartTimeUtc is { } eventStart)
        {
            return eventStart;
        }

        foreach (var candidate in new[] { market.Slug, market.EventSlug, market.SeriesSlug })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                var match = UpDown5mSlugRegexValue.Match(candidate);
                if (match.Success &&
                    long.TryParse(match.Groups["unix"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                }
            }
        }

        return market.EndDateUtc?.AddMinutes(-5);
    }

    private static bool TryGetAssetSymbol(string? value, out string assetSymbol)
    {
        assetSymbol = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var slugMatch = UpDown5mSlugRegexValue.Match(value);
        if (slugMatch.Success)
        {
            assetSymbol = slugMatch.Groups["asset"].Value.ToUpperInvariant();
            return true;
        }

        var seriesMatch = UpDown5mSeriesRegexValue.Match(value);
        if (seriesMatch.Success)
        {
            assetSymbol = seriesMatch.Groups["asset"].Value.ToUpperInvariant();
            return true;
        }

        return false;
    }
}
