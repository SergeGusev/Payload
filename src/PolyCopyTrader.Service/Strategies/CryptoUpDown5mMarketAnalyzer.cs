using System.Globalization;
using System.Text.RegularExpressions;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.Strategies;

public static partial class CryptoUpDown5mMarketAnalyzer
{
    public static bool TryGetAssetSymbol(
        PolymarketGammaMarket market,
        IReadOnlySet<string> allowedAssetSymbols,
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

    public static BtcUpDownMarketInterval? GetMarketInterval(PolymarketGammaMarket market)
    {
        foreach (var candidate in new[] { market.Slug, market.EventSlug, market.SeriesSlug })
        {
            if (TryGetMarketInterval(candidate, out var interval))
            {
                return interval;
            }
        }

        return null;
    }

    public static TimeSpan GetIntervalDuration(BtcUpDownMarketInterval interval)
    {
        return interval switch
        {
            BtcUpDownMarketInterval.FiveMinutes => TimeSpan.FromMinutes(5),
            BtcUpDownMarketInterval.FifteenMinutes => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromMinutes(5)
        };
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
                var match = UpDownSlugRegex().Match(candidate);
                if (match.Success &&
                    long.TryParse(match.Groups["unix"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                }
            }
        }

        var interval = GetMarketInterval(market) ?? BtcUpDownMarketInterval.FiveMinutes;
        return market.EndDateUtc?.Subtract(GetIntervalDuration(interval));
    }

    private static bool TryGetAssetSymbol(string? value, out string assetSymbol)
    {
        assetSymbol = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var slugMatch = UpDownSlugRegex().Match(value);
        if (slugMatch.Success)
        {
            assetSymbol = slugMatch.Groups["asset"].Value.ToUpperInvariant();
            return true;
        }

        var seriesMatch = UpDownSeriesRegex().Match(value);
        if (seriesMatch.Success)
        {
            assetSymbol = seriesMatch.Groups["asset"].Value.ToUpperInvariant();
            return true;
        }

        return false;
    }

    private static bool TryGetMarketInterval(string? value, out BtcUpDownMarketInterval interval)
    {
        interval = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var slugMatch = UpDownSlugRegex().Match(value);
        if (slugMatch.Success)
        {
            return TryParseInterval(slugMatch.Groups["interval"].Value, out interval);
        }

        var seriesMatch = UpDownSeriesRegex().Match(value);
        return seriesMatch.Success && TryParseInterval(seriesMatch.Groups["interval"].Value, out interval);
    }

    private static bool TryParseInterval(string value, out BtcUpDownMarketInterval interval)
    {
        interval = value.ToLowerInvariant() switch
        {
            "5m" => BtcUpDownMarketInterval.FiveMinutes,
            "15m" => BtcUpDownMarketInterval.FifteenMinutes,
            _ => default
        };

        return interval is BtcUpDownMarketInterval.FiveMinutes or BtcUpDownMarketInterval.FifteenMinutes;
    }

    [GeneratedRegex("^(?<asset>[a-z0-9]+)-updown-(?<interval>5m|15m)-(?<unix>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpDownSlugRegex();

    [GeneratedRegex("^(?<asset>[a-z0-9]+)-up-or-down-(?<interval>5m|15m)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpDownSeriesRegex();
}
