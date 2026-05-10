namespace PolyCopyTrader.Polymarket.Auth;

public sealed record AuthReadinessStatus(
    string State,
    bool IsConfigured,
    bool CanAuthenticate,
    IReadOnlyList<string> MissingRequirements,
    DateTimeOffset CheckedAtUtc)
{
    public static AuthReadinessStatus NotConfigured(DateTimeOffset? checkedAtUtc = null)
    {
        return new AuthReadinessStatus(
            "NotConfigured",
            false,
            false,
            new[]
            {
                "Auth is disabled in the .NET Framework 4.8 dashboard port.",
                "No API credentials or private key provider is used by this stub."
            },
            checkedAtUtc ?? DateTimeOffset.UtcNow);
    }
}
