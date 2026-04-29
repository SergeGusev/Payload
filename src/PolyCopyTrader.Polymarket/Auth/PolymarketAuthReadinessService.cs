using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class PolymarketAuthReadinessService(
    PolymarketAuthOptions options,
    ISecretProvider secretProvider,
    PolymarketL2HmacSigner signer) : IPolymarketAuthService
{
    public async Task<AuthReadinessStatus> GetReadinessAsync(CancellationToken ct)
    {
        if (!options.Enabled)
        {
            return AuthReadinessStatus.NotConfigured(["Polymarket auth is disabled."]);
        }

        var missing = new List<string>();
        if (!IsAddressLike(options.SigningAddress))
        {
            missing.Add("Signing address is not configured.");
        }

        var apiKey = await ReadSecretAsync(options.ApiKeyName, "API key", missing, ct);
        var apiSecret = await ReadSecretAsync(options.ApiSecretName, "API secret", missing, ct);
        var apiPassphrase = await ReadSecretAsync(options.ApiPassphraseName, "API passphrase", missing, ct);

        if (missing.Count > 0)
        {
            return AuthReadinessStatus.NotConfigured(missing);
        }

        try
        {
            _ = new PolymarketAuthHeaderFactory(signer).CreateL2Headers(
                options.SigningAddress,
                new PolymarketApiCredentials(apiKey!, apiSecret!, apiPassphrase!),
                new PolymarketAuthenticatedRequest("GET", "/data/orders"),
                DateTimeOffset.UtcNow);

            return AuthReadinessStatus.ConfiguredButUntested();
        }
        catch (FormatException)
        {
            return AuthReadinessStatus.Error("API secret is not valid base64url.");
        }
        catch (ArgumentException ex)
        {
            return AuthReadinessStatus.Error(ex.Message);
        }
    }

    private async Task<string?> ReadSecretAsync(
        string name,
        string label,
        List<string> missing,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            missing.Add($"{label} reference is not configured.");
            return null;
        }

        var value = await secretProvider.GetSecretAsync(name, cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add($"{label} value is unavailable from the configured provider.");
            return null;
        }

        return value;
    }

    private static bool IsAddressLike(string value)
    {
        return value.Length == 42 &&
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            value.Skip(2).All(Uri.IsHexDigit);
    }
}
