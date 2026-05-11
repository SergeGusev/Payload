using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket.Auth;

public static class PolymarketSecretProviderFactory
{
    public static ISecretProvider Create(PolymarketAuthOptions options)
    {
        return string.Equals(options.SecretProvider, "CredentialManager", StringComparison.OrdinalIgnoreCase)
            ? new WindowsCredentialManagerSecretProvider()
            : new EnvironmentVariableSecretProvider();
    }
}
