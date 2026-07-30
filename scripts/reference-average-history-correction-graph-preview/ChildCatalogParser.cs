using System.Security.Cryptography;

namespace ReferenceAverageHistoryCorrectionGraphPreview;

internal static class ChildCatalogParser
{
    public const string CatalogRelativePath =
        "Codex/Tasks/REFERENCE_AVERAGE_MAX_MIN_MIGRATION_2026-07-27.md";

    public static IReadOnlyList<ChildStrategy> ParseAndValidate(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, CatalogRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var inChildSection = false;
        var result = new List<ChildStrategy>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("## Conditional downstream: ChildMirror", StringComparison.Ordinal))
            {
                inChildSection = true;
                continue;
            }

            if (!inChildSection || !line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length != 9 || !string.Equals(cells[2], "Conditional downstream", StringComparison.Ordinal))
            {
                continue;
            }

            if (!Guid.TryParseExact(cells[5], "D", out var id))
            {
                throw new InvalidDataException($"Invalid Child strategy ID in catalog: {line}");
            }

            var code = cells[6];
            var name = cells[7];
            var asset = code.StartsWith("btc_", StringComparison.Ordinal) ? "BTC" :
                code.StartsWith("eth_", StringComparison.Ordinal) ? "ETH" :
                code.StartsWith("sol_", StringComparison.Ordinal) ? "SOL" :
                throw new InvalidDataException($"Unknown Child asset in catalog code: {code}");
            result.Add(new ChildStrategy(id, code, name, asset, cells[3]));
        }

        if (result.Count != 247 ||
            result.Select(item => item.Id).Distinct().Count() != 247 ||
            result.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() != 247 ||
            result.Count(item => item.Asset == "BTC") != 96 ||
            result.Count(item => item.Asset == "ETH") != 63 ||
            result.Count(item => item.Asset == "SOL") != 88)
        {
            throw new InvalidDataException(
                $"Child catalog validation failed: rows={result.Count}, " +
                $"BTC={result.Count(item => item.Asset == "BTC")}, " +
                $"ETH={result.Count(item => item.Asset == "ETH")}, " +
                $"SOL={result.Count(item => item.Asset == "SOL")}.");
        }

        return result.OrderBy(item => item.Id).ToArray();
    }

    public static string ComputeSha256(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, CatalogRelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static void RequirePinnedSha256(string actualSha256, string requiredSha256)
    {
        if (!string.Equals(actualSha256, requiredSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Child catalog SHA-256 {actualSha256} does not match pinned source SHA-256 {requiredSha256}.");
        }
    }
}
