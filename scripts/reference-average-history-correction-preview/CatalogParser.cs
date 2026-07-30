using System.Globalization;
using System.Security.Cryptography;

namespace ReferenceAverageHistoryCorrectionPreview;

public static class CatalogParser
{
    public const int ExpectedStrategyCount = 848;
    public const int ExpectedPotentialAddStrategyCount = 192;

    private static readonly IReadOnlyDictionary<string, int> ExpectedAssetCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["BTC"] = 312,
            ["ETH"] = 322,
            ["SOL"] = 214
        };

    private static readonly IReadOnlyDictionary<StrategyFamily, int> ExpectedFamilyCounts =
        new Dictionary<StrategyFamily, int>
        {
            [StrategyFamily.ReferenceAverage] = 308,
            [StrategyFamily.OptimizedReferenceAverage] = 288,
            [StrategyFamily.NativeLowEnterReferenceAverage] = 84,
            [StrategyFamily.BpsConfirmedAverage] = 112,
            [StrategyFamily.DiffConfirmedAverage] = 56
        };

    private static readonly IReadOnlyDictionary<string, int> DiffConfirmedReferenceThresholds =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["BTC"] = 45,
            ["ETH"] = 5,
            ["SOL"] = 35
        };

    private static readonly IReadOnlyDictionary<(string Asset, StrategyFamily Family), int> ExpectedAssetFamilyCounts =
        new Dictionary<(string Asset, StrategyFamily Family), int>
        {
            [("BTC", StrategyFamily.ReferenceAverage)] = 140,
            [("BTC", StrategyFamily.OptimizedReferenceAverage)] = 60,
            [("BTC", StrategyFamily.NativeLowEnterReferenceAverage)] = 28,
            [("BTC", StrategyFamily.BpsConfirmedAverage)] = 56,
            [("BTC", StrategyFamily.DiffConfirmedAverage)] = 28,
            [("ETH", StrategyFamily.ReferenceAverage)] = 84,
            [("ETH", StrategyFamily.OptimizedReferenceAverage)] = 168,
            [("ETH", StrategyFamily.NativeLowEnterReferenceAverage)] = 28,
            [("ETH", StrategyFamily.BpsConfirmedAverage)] = 28,
            [("ETH", StrategyFamily.DiffConfirmedAverage)] = 14,
            [("SOL", StrategyFamily.ReferenceAverage)] = 84,
            [("SOL", StrategyFamily.OptimizedReferenceAverage)] = 60,
            [("SOL", StrategyFamily.NativeLowEnterReferenceAverage)] = 28,
            [("SOL", StrategyFamily.BpsConfirmedAverage)] = 28,
            [("SOL", StrategyFamily.DiffConfirmedAverage)] = 14
        };

    public static IReadOnlyList<StrategyDefinition> ParseAndValidate(string catalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException("The exact migration catalog was not found.", catalogPath);
        }

        var strategies = new List<StrategyDefinition>(ExpectedStrategyCount);
        string? currentAsset = null;
        StrategyFamily? currentFamily = null;
        StrategyLocation? currentLocation = null;

        foreach (var rawLine in File.ReadLines(catalogPath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("## Conditional downstream:", StringComparison.Ordinal))
            {
                break;
            }

            if (TryParseAssetHeading(line, out var asset))
            {
                currentAsset = asset;
                currentFamily = null;
                currentLocation = null;
                continue;
            }

            if (TryParseFamilyHeading(line, out var family, out var location))
            {
                if (currentAsset is null)
                {
                    throw new InvalidDataException($"Family heading appeared before an asset heading: {line}");
                }

                currentFamily = family;
                currentLocation = location;
                continue;
            }

            if (currentAsset is null || currentFamily is null || currentLocation is null ||
                !line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length != 7 || !Guid.TryParse(cells[4], out var strategyId))
            {
                continue;
            }

            if (!int.TryParse(cells[3], NumberStyles.None, CultureInfo.InvariantCulture, out var thresholdBps) ||
                thresholdBps <= 0)
            {
                throw new InvalidDataException($"Invalid threshold for strategy {strategyId}: {cells[3]}");
            }

            if (!Enum.TryParse<StrategyTrigger>(cells[2], ignoreCase: false, out var trigger))
            {
                throw new InvalidDataException($"Invalid trigger for strategy {strategyId}: {cells[2]}");
            }

            var kind = cells[1];
            var code = cells[5];
            var usesLowEnterPrice = currentFamily == StrategyFamily.NativeLowEnterReferenceAverage ||
                string.Equals(kind, "LowerEnter clone", StringComparison.Ordinal);
            var referenceThresholdBps = currentFamily == StrategyFamily.DiffConfirmedAverage
                ? DiffConfirmedReferenceThresholds[currentAsset]
                : thresholdBps;
            strategies.Add(new StrategyDefinition(
                strategyId,
                code,
                cells[6],
                currentAsset,
                currentFamily.Value,
                currentLocation.Value,
                trigger,
                thresholdBps,
                referenceThresholdBps,
                kind,
                usesLowEnterPrice));
        }

        Validate(strategies);
        return strategies;
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool TryParseAssetHeading(string line, out string asset)
    {
        foreach (var candidate in ExpectedAssetCounts.Keys)
        {
            if (line.StartsWith($"## {candidate} —", StringComparison.Ordinal))
            {
                asset = candidate;
                return true;
            }
        }

        asset = string.Empty;
        return false;
    }

    private static bool TryParseFamilyHeading(
        string line,
        out StrategyFamily family,
        out StrategyLocation location)
    {
        var mapping = line switch
        {
            var value when value.StartsWith("### Direct: Reference Average —", StringComparison.Ordinal) =>
                (StrategyFamily.ReferenceAverage, StrategyLocation.Direct),
            var value when value.StartsWith("### Direct: Optimized Reference Average —", StringComparison.Ordinal) =>
                (StrategyFamily.OptimizedReferenceAverage, StrategyLocation.Direct),
            var value when value.StartsWith("### Direct: Native LowEnter Reference Average —", StringComparison.Ordinal) =>
                (StrategyFamily.NativeLowEnterReferenceAverage, StrategyLocation.Direct),
            var value when value.StartsWith("### Indirect: Bps Confirmed Average —", StringComparison.Ordinal) =>
                (StrategyFamily.BpsConfirmedAverage, StrategyLocation.Indirect),
            var value when value.StartsWith("### Indirect: Diff Confirmed Average —", StringComparison.Ordinal) =>
                (StrategyFamily.DiffConfirmedAverage, StrategyLocation.Indirect),
            _ => ((StrategyFamily?)null, (StrategyLocation?)null)
        };

        if (mapping.Item1 is { } parsedFamily && mapping.Item2 is { } parsedLocation)
        {
            family = parsedFamily;
            location = parsedLocation;
            return true;
        }

        family = default;
        location = default;
        return false;
    }

    private static void Validate(IReadOnlyList<StrategyDefinition> strategies)
    {
        AssertCount("total strategy", ExpectedStrategyCount, strategies.Count);
        AssertCount("unique strategy ID", ExpectedStrategyCount, strategies.Select(item => item.Id).Distinct().Count());
        AssertCount("unique strategy code", ExpectedStrategyCount, strategies.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count());
        AssertCount("unique strategy name", ExpectedStrategyCount, strategies.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count());

        foreach (var expected in ExpectedAssetCounts)
        {
            AssertCount(
                $"asset {expected.Key}",
                expected.Value,
                strategies.Count(item => item.Asset == expected.Key));
        }

        foreach (var expected in ExpectedFamilyCounts)
        {
            AssertCount(
                $"family {expected.Key}",
                expected.Value,
                strategies.Count(item => item.Family == expected.Key));
        }

        foreach (var expected in ExpectedAssetFamilyCounts)
        {
            AssertCount(
                $"asset/family {expected.Key.Asset}/{expected.Key.Family}",
                expected.Value,
                strategies.Count(item => item.Asset == expected.Key.Asset && item.Family == expected.Key.Family));
        }

        AssertCount("location Direct", 680, strategies.Count(item => item.Location == StrategyLocation.Direct));
        AssertCount("location Indirect", 168, strategies.Count(item => item.Location == StrategyLocation.Indirect));
        AssertCount(
            "Optimized Down/Neutral potential-add cohort",
            ExpectedPotentialAddStrategyCount,
            strategies.Count(item =>
                item.Family == StrategyFamily.OptimizedReferenceAverage &&
                item.Trigger is StrategyTrigger.Down or StrategyTrigger.Neutral));
        AssertCount(
            "LowEnter-priced strategy cohort",
            326,
            strategies.Count(item => item.UsesLowEnterPrice));
        AssertCount(
            "LowEnter-priced Optimized Down/Neutral potential-add cohort",
            96,
            strategies.Count(item =>
                item.UsesLowEnterPrice &&
                item.Family == StrategyFamily.OptimizedReferenceAverage &&
                item.Trigger is StrategyTrigger.Down or StrategyTrigger.Neutral));
    }

    private static void AssertCount(string label, int expected, int actual)
    {
        if (actual != expected)
        {
            throw new InvalidDataException($"Catalog assertion failed for {label}: expected {expected}, actual {actual}.");
        }
    }
}
