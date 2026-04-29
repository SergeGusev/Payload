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

    public static AuthReadinessStatus NotConfigured(
        IReadOnlyList<string> missingRequirements,
        DateTimeOffset? checkedAtUtc = null)
    {
        return new AuthReadinessStatus(
            "NotConfigured",
            false,
            false,
            missingRequirements,
            checkedAtUtc ?? DateTimeOffset.UtcNow);
    }

    public static AuthReadinessStatus ConfiguredButUntested(DateTimeOffset? checkedAtUtc = null)
    {
        return new AuthReadinessStatus(
            "ConfiguredButUntested",
            true,
            true,
            [],
            checkedAtUtc ?? DateTimeOffset.UtcNow);
    }

    public static AuthReadinessStatus Ready(DateTimeOffset? checkedAtUtc = null)
    {
        return new AuthReadinessStatus(
            "Ready",
            true,
            true,
            [],
            checkedAtUtc ?? DateTimeOffset.UtcNow);
    }

    public static AuthReadinessStatus Error(string message, DateTimeOffset? checkedAtUtc = null)
    {
        return new AuthReadinessStatus(
            "Error",
            false,
            false,
            [message],
            checkedAtUtc ?? DateTimeOffset.UtcNow);
    }
}
