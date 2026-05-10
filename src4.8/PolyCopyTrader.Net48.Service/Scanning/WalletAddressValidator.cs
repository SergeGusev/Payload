using System.Text.RegularExpressions;

namespace PolyCopyTrader.Service.Scanning;

public static class WalletAddressValidator
{
    private static readonly Regex WalletRegex = new Regex(
        "^0x[a-fA-F0-9]{40}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsValid(string? wallet)
    {
        return wallet is not null && WalletRegex.IsMatch(wallet.Trim());
    }

    public static string Normalize(string wallet)
    {
        return wallet.Trim().ToLowerInvariant();
    }
}
