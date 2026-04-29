using System.Text.RegularExpressions;

namespace PolyCopyTrader.Service.Scanning;

public static partial class WalletAddressValidator
{
    public static bool IsValid(string? wallet)
    {
        return wallet is not null && WalletRegex().IsMatch(wallet.Trim());
    }

    public static string Normalize(string wallet)
    {
        return wallet.Trim().ToLowerInvariant();
    }

    [GeneratedRegex("^0x[a-fA-F0-9]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex WalletRegex();
}
