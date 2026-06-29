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

        if (StartsWithSimpleFixedOutcome(suffix))
        {
            return "Simple";
        }

        if (StartsWithAdjustedDiffThreshold(suffix, "Up", out var adjustedDiffUpRevert))
        {
            return categoryPrefix + "AdjustedDiff Up" + (adjustedDiffUpRevert ? " Revert" : string.Empty);
        }

        if (StartsWithAdjustedDiffThreshold(suffix, "Down", out var adjustedDiffDownRevert))
        {
            return categoryPrefix + "AdjustedDiff Down" + (adjustedDiffDownRevert ? " Revert" : string.Empty);
        }

        if (TryGetShiftDiffCategory(suffix, "Up", out var shiftDiffUpCategory))
        {
            return categoryPrefix + shiftDiffUpCategory;
        }

        if (TryGetShiftDiffCategory(suffix, "Down", out var shiftDiffDownCategory))
        {
            return categoryPrefix + shiftDiffDownCategory;
        }

        if (StartsWithDiffProgressThreshold(suffix))
        {
            return categoryPrefix + "Diff Progress";
        }

        if (IsDiffShiftProgress(suffix))
        {
            return "Up Or Down 5 min Diff Shift Progress";
        }

        if (IsDiffLimitProgress(suffix))
        {
            return "Up Or Down 5 min Diff Limit Progress";
        }

        if (IsDiffRealLimitProgress(suffix))
        {
            return "Up Or Down 5 min Diff Real Limit Progress";
        }

        if (StartsWithDiffThreshold(suffix, "Up", out var diffUpRevert))
        {
            return categoryPrefix + "Diff Up" + GetDiffCategorySuffix(suffix, diffUpRevert);
        }

        if (StartsWithDiffThreshold(suffix, "Down", out var diffDownRevert))
        {
            return categoryPrefix + "Diff Down" + GetDiffCategorySuffix(suffix, diffDownRevert);
        }

        if (IsPremarketBpsThreshold(suffix))
        {
            if (string.Equals(upDownPrefix, "ETH Up or Down ", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(interval, "5m", StringComparison.OrdinalIgnoreCase) &&
                StartsWithBpsThreshold(suffix, "Down") &&
                !HasReferenceAverageMarker(suffix))
            {
                return categoryPrefix + "Down Bps Premarket";
            }

            return categoryPrefix + "Reference Average Bps Premarket";
        }

        if (StartsWithBpsThreshold(suffix, "Up"))
        {
            return categoryPrefix + "Up Bps";
        }

        if (string.Equals(upDownPrefix, "ETH Up or Down ", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(interval, "5m", StringComparison.OrdinalIgnoreCase) &&
            StartsWithBpsThreshold(suffix, "Down") &&
            ContainsStrategyWord(suffix, "Premarket"))
        {
            return categoryPrefix + "Down Bps Premarket";
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
            return ContainsStrategyWord(suffix, "Premarket")
                ? categoryPrefix + "Countertrend Premarket"
                : categoryPrefix + "Countertrend";
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

    private static bool IsPremarketBpsThreshold(string value)
    {
        return (StartsWithBpsThreshold(value, "Up") || StartsWithBpsThreshold(value, "Down")) &&
            ContainsStrategyWord(value, "Premarket") &&
            !HasPremarketTimingSuffix(value);
    }

    private static bool HasPremarketTimingSuffix(string value)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part =>
            part.Length > 2 &&
            part[0] == '-' &&
            part.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(part[1..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _));
    }

    private static bool HasReferenceAverageMarker(string value)
    {
        return ContainsStrategyWord(value, "Reference") &&
            ContainsStrategyWord(value, "Average");
    }

    private static bool StartsWithSimpleFixedOutcome(string value)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 &&
            (string.Equals(parts[0], "Up", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parts[0], "Down", StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(parts[1], "Simple", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDiffCategorySuffix(string value, bool isRevert)
    {
        var suffix = isRevert ? " Revert" : string.Empty;
        return ContainsStrategyWord(value, "Premarket")
            ? suffix + " Premarket"
            : suffix;
    }

    private static bool StartsWithDiffThreshold(string value, string word, out bool isRevert)
    {
        isRevert = false;
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var matches = parts.Length >= 3 &&
            string.Equals(parts[0], word, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out _) &&
            string.Equals(parts[2], "Diff", StringComparison.OrdinalIgnoreCase);
        isRevert = matches &&
            parts.Length >= 4 &&
            string.Equals(parts[3], "Revert", StringComparison.OrdinalIgnoreCase);
        return matches;
    }

    private static bool StartsWithDiffProgressThreshold(string value)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 4 &&
            int.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out _) &&
            string.Equals(parts[1], "Diff", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(parts[2], "Up", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parts[2], "Down", StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(parts[3], "Progress", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiffShiftProgress(string value)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 &&
            string.Equals(parts[0], "Diff", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(parts[1], "Up", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parts[1], "Down", StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(parts[2], "Shift", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[3], "Progress", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return parts.Length >= 4 &&
            int.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out _) &&
            string.Equals(parts[1], "Diff", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[2], "Shift", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[3], "Progress", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiffLimitProgress(string value)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 4 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
            string.Equals(parts[1], "Diff", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[2], "Limit", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[3], "Progress", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiffRealLimitProgress(string value)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 5 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
            string.Equals(parts[1], "Diff", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[2], "Real", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[3], "Limit", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[4], "Progress", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetShiftDiffCategory(string value, string word, out string category)
    {
        category = string.Empty;
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 ||
            !string.Equals(parts[0], word, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var shift) ||
            !int.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold) ||
            !string.Equals(parts[3], "ShiftDiff", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        category = "ShiftDiff " + shift.ToString(CultureInfo.InvariantCulture);
        if (parts.Length >= 5 &&
            string.Equals(parts[4], "Revert", StringComparison.OrdinalIgnoreCase))
        {
            category += " Revert";
        }

        return true;
    }

    private static bool StartsWithAdjustedDiffThreshold(string value, string word, out bool isRevert)
    {
        isRevert = false;
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var matches = parts.Length >= 3 &&
            string.Equals(parts[0], word, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out _) &&
            string.Equals(parts[2], "AdjustedDiff", StringComparison.OrdinalIgnoreCase);
        isRevert = matches &&
            parts.Length >= 4 &&
            string.Equals(parts[3], "Revert", StringComparison.OrdinalIgnoreCase);
        return matches;
    }
}
