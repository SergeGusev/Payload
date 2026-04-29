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
            [
                "Auth implementation is intentionally research-only in Task 13.",
                "No API credentials or private key provider configured."
            ],
            checkedAtUtc ?? DateTimeOffset.UtcNow);
    }
}
