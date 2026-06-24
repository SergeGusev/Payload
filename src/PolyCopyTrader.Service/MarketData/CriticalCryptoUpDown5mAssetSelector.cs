using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public static class CriticalCryptoUpDown5mAssetSelector
{
    public const string ComponentName = "PolymarketMarketWebSocket:crypto-updown-5m-critical";
    private static readonly string[] AssetSymbols = ["BTC", "ETH", "SOL"];

    public static IReadOnlyCollection<string> SelectAssetIds(IReadOnlyCollection<ActiveMarketAssetSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        return snapshots
            .Where(snapshot => snapshot.IsSubscribable)
            .Where(IsCriticalSnapshot)
            .Select(snapshot => NormalizeAssetId(snapshot.AssetId))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(assetId => assetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsCriticalSnapshot(ActiveMarketAssetSnapshot snapshot)
    {
        foreach (var assetSymbol in AssetSymbols)
        {
            if (IsAssetFiveMinuteText(snapshot.Slug, assetSymbol) ||
                IsAssetFiveMinuteText(snapshot.EventSlug, assetSymbol) ||
                IsAssetFiveMinuteText(snapshot.SeriesSlug, assetSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAssetFiveMinuteText(string? value, string assetSymbol)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var asset = assetSymbol.Trim();
        return normalized.StartsWith(asset + "-updown-5m-", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(asset + "-up-or-down-5m", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeAssetId(string? assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return null;
        }

        var trimmed = assetId.Trim();
        return trimmed.Equals("0", StringComparison.Ordinal) ||
            trimmed.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }
}
