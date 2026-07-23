using System.Globalization;

namespace PolyCopyTrader.Service.Analytics;

public enum CryptoOrderBookPredictionAsset
{
    Btc,
    Eth,
    Sol
}

public static class CryptoOrderBookPredictionAssetCatalog
{
    private const int MarketSeconds = 300;

    public static CryptoOrderBookPredictionAsset Parse(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "btc" => CryptoOrderBookPredictionAsset.Btc,
            "eth" => CryptoOrderBookPredictionAsset.Eth,
            "sol" => CryptoOrderBookPredictionAsset.Sol,
            _ => throw new ArgumentException("Order-book prediction asset must be btc, eth, or sol.", nameof(value))
        };
    }

    public static bool TryParse(string? value, out CryptoOrderBookPredictionAsset asset)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "btc":
                    asset = CryptoOrderBookPredictionAsset.Btc;
                    return true;
                case "eth":
                    asset = CryptoOrderBookPredictionAsset.Eth;
                    return true;
                case "sol":
                    asset = CryptoOrderBookPredictionAsset.Sol;
                    return true;
            }
        }

        asset = default;
        return false;
    }

    public static string ToCode(this CryptoOrderBookPredictionAsset asset) => asset switch
    {
        CryptoOrderBookPredictionAsset.Btc => "btc",
        CryptoOrderBookPredictionAsset.Eth => "eth",
        CryptoOrderBookPredictionAsset.Sol => "sol",
        _ => throw new ArgumentOutOfRangeException(nameof(asset), asset, null)
    };

    public static string ToDisplaySymbol(this CryptoOrderBookPredictionAsset asset) =>
        asset.ToCode().ToUpperInvariant();

    public static string ToBinanceSymbol(this CryptoOrderBookPredictionAsset asset) =>
        asset.ToDisplaySymbol() + "USDT";

    public static string ToBinanceStreamSymbol(this CryptoOrderBookPredictionAsset asset) =>
        asset.ToBinanceSymbol().ToLowerInvariant();

    public static string ToMarketSlugPrefix(this CryptoOrderBookPredictionAsset asset) =>
        asset.ToCode() + "-updown-5m-";

    public static string ToMarketSlug(
        this CryptoOrderBookPredictionAsset asset,
        DateTimeOffset marketStartUtc)
    {
        long unixSeconds = marketStartUtc.ToUniversalTime().ToUnixTimeSeconds();
        if (unixSeconds % MarketSeconds != 0)
        {
            throw new ArgumentException("Market start must be an exact five-minute Unix boundary.", nameof(marketStartUtc));
        }

        return asset.ToMarketSlugPrefix() + unixSeconds.ToString(CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset? TryParseMarketStartUtc(
        this CryptoOrderBookPredictionAsset asset,
        string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        string prefix = asset.ToMarketSlugPrefix();
        if (!slug.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string suffix = slug[prefix.Length..];
        if (!long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out long unixSeconds) ||
            unixSeconds % MarketSeconds != 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
