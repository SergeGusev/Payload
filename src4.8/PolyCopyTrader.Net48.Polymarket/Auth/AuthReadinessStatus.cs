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
            Array.Empty<string>(),
            checkedAtUtc ?? DateTimeOffset.UtcNow);
    }

    public static AuthReadinessStatus Ready(DateTimeOffset? checkedAtUtc = null)
    {
        return new AuthReadinessStatus(
            "Ready",
            true,
            true,
            Array.Empty<string>(),
            checkedAtUtc ?? DateTimeOffset.UtcNow);
    }

    public static AuthReadinessStatus Error(string message, DateTimeOffset? checkedAtUtc = null)
    {
        return new AuthReadinessStatus(
            "Error",
            false,
            false,
            new[] { message },
            checkedAtUtc ?? DateTimeOffset.UtcNow);
    }
}
