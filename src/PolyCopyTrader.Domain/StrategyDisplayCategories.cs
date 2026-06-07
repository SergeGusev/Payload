using System.Globalization;

namespace PolyCopyTrader.Domain;

public static class StrategyDisplayCategories
{
    private static readonly string[] UpDownAssetSymbols = ["BTC", "ETH", "SOL"];
    private static readonly string[] UpDownIntervals = ["5m", "15m", "1h", "4h"];

    public static string GetCategory(string? strategyName)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
        {
            return "Other";
        }

        var name = strategyName.Trim();
        var upDownPrefix = UpDownAssetSymbols
            .Select(asset => asset + " Up or Down ")
            .FirstOrDefault(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(upDownPrefix))
        {
            return "Other";
        }

        var suffixAfterAsset = name.Substring(upDownPrefix.Length).Trim();
        var interval = UpDownIntervals.FirstOrDefault(candidate =>
            suffixAfterAsset.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
            suffixAfterAsset.StartsWith(candidate + " ", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(interval))
        {
            return "Other";
        }

        var categoryPrefix = upDownPrefix + interval + " ";
        var suffix = suffixAfterAsset.Substring(interval.Length).Trim();
        if (StartsWithStrategyWord(suffix, "PreOpen"))
        {
            var parts = suffix.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                (string.Equals(parts[1], "Half", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(parts[1], "Full", StringComparison.OrdinalIgnoreCase)))
            {
                return categoryPrefix + "PreOpen " + parts[1];
            }

            return categoryPrefix + "PreOpen";
        }

        if (StartsWithBpsThreshold(suffix, "Up"))
        {
            return categoryPrefix + "Up Bps";
        }

        if (StartsWithBpsThreshold(suffix, "Down"))
        {
            return categoryPrefix + "Down Bps";
        }

        if (!string.Equals(interval, "5m", StringComparison.OrdinalIgnoreCase))
        {
            return categoryPrefix + "Other";
        }

        if (StartsWithStrategyWord(suffix, "More"))
        {
            return ContainsStrategyWord(suffix, "Gamma")
                ? categoryPrefix + "More Gamma"
                : categoryPrefix + "More";
        }

        if (StartsWithStrategyWord(suffix, "Less"))
        {
            return ContainsStrategyWord(suffix, "Gamma")
                ? categoryPrefix + "Less Gamma"
                : categoryPrefix + "Less";
        }

        if (StartsWithStrategyWord(suffix, "Binance"))
        {
            return categoryPrefix + "Binance";
        }

        if (StartsWithStrategyWord(suffix, "Middle"))
        {
            return ContainsStrategyWord(suffix, "Revert")
                ? categoryPrefix + "Middle Revert"
                : categoryPrefix + "Middle";
        }

        if (StartsWithStrategyWord(suffix, "Skip"))
        {
            return categoryPrefix + "Skip";
        }

        if (ContainsStrategyWord(suffix, "Countertrend"))
        {
            return categoryPrefix + "Countertrend";
        }

        return categoryPrefix + "Other";
    }

    private static bool StartsWithStrategyWord(string value, string word)
    {
        return value.Equals(word, StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(word + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsStrategyWord(string value, string word)
    {
        return value
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(item => string.Equals(item, word, StringComparison.OrdinalIgnoreCase));
    }

    private static bool StartsWithBpsThreshold(string value, string word)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 &&
            string.Equals(parts[0], word, StringComparison.OrdinalIgnoreCase) &&
            decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out _) &&
            string.Equals(parts[2], "bps", StringComparison.OrdinalIgnoreCase);
    }
}
