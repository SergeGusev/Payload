using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket.Auth;

namespace PolyCopyTrader.Service.Startup;

public static class PolymarketApiCredentialBootstrapCommand
{
    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(configuration.PolymarketAuth.SecretProvider, "CredentialManager", StringComparison.OrdinalIgnoreCase))
        {
            return await WriteFailureAsync(output, "Polymarket API credential bootstrap currently requires SecretProvider=CredentialManager.");
        }

        var credentialManager = new WindowsCredentialManagerSecretProvider();
        using var httpClient = new HttpClient();
        return await ExecuteAsync(configuration, credentialManager, credentialManager, httpClient, output, cancellationToken);
    }

    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        ISecretProvider secretProvider,
        ISecretWriter secretWriter,
        HttpClient httpClient,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secretProvider);
        ArgumentNullException.ThrowIfNull(secretWriter);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(output);

        await output.WriteLineAsync("Polymarket API credential bootstrap: creates or derives L2 credentials only; no orders will be sent.");
        await output.WriteLineAsync($"Bot mode: {configuration.Bot.Mode}");
        await output.WriteLineAsync($"Live trading enabled: {configuration.Bot.EnableLiveTrading}");
        await output.WriteLineAsync($"Auth enabled: {configuration.PolymarketAuth.Enabled}");
        await output.WriteLineAsync($"Signer address: {RedactAddress(configuration.PolymarketAuth.SigningAddress)}");
        await output.WriteLineAsync($"Funder address: {RedactAddress(configuration.PolymarketAuth.FunderAddress)}");
        await output.WriteLineAsync($"Signature type: {configuration.PolymarketAuth.SignatureType}");
        await output.WriteLineAsync($"CLOB API: {configuration.Polymarket.ClobBaseUrl}");
        await output.WriteLineAsync($"HTTP timeout seconds: {configuration.Polymarket.TimeoutSeconds}");

        if (configuration.Bot.Mode is BotMode.Live || configuration.Bot.EnableLiveTrading)
        {
            await output.WriteLineAsync("Bootstrap status: Refused");
            await output.WriteLineAsync("Reason: disable Live mode before bootstrapping API credentials.");
            return 1;
        }

        var privateKey = await secretProvider.GetSecretAsync(configuration.PolymarketAuth.OrderSigningPrivateKeyName, cancellationToken);
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            await output.WriteLineAsync("Bootstrap status: MissingSigningKey");
            await output.WriteLineAsync($"Missing secret target: {configuration.PolymarketAuth.OrderSigningPrivateKeyName}");
            return 1;
        }

        try
        {
            var l1Signer = new ClobL1AuthSigner();
            var client = new PolymarketApiCredentialBootstrapClient(
                httpClient,
                configuration.Polymarket,
                configuration.PolymarketAuth,
                l1Signer,
                new PolymarketL1AuthHeaderFactory(l1Signer));

            var result = await client.CreateOrDeriveAsync(privateKey, cancellationToken);

            await secretWriter.SetSecretAsync(configuration.PolymarketAuth.ApiKeyName, result.Credentials.ApiKey, cancellationToken);
            await secretWriter.SetSecretAsync(configuration.PolymarketAuth.ApiKeyOwnerName, result.Credentials.ApiKey, cancellationToken);
            await secretWriter.SetSecretAsync(configuration.PolymarketAuth.ApiSecretName, result.Credentials.ApiSecret, cancellationToken);
            await secretWriter.SetSecretAsync(configuration.PolymarketAuth.ApiPassphraseName, result.Credentials.ApiPassphrase, cancellationToken);

            await output.WriteLineAsync($"Credential source: {result.Source}");
            await output.WriteLineAsync($"L1 auth nonce: {result.Nonce}");
            await output.WriteLineAsync($"L1 auth timestamp: {result.Timestamp}");
            await output.WriteLineAsync($"Stored target: {configuration.PolymarketAuth.ApiKeyName}");
            await output.WriteLineAsync($"Stored target: {configuration.PolymarketAuth.ApiKeyOwnerName}");
            await output.WriteLineAsync($"Stored target: {configuration.PolymarketAuth.ApiSecretName}");
            await output.WriteLineAsync($"Stored target: {configuration.PolymarketAuth.ApiPassphraseName}");
            await output.WriteLineAsync("Bootstrap status: Stored");
            return 0;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("Bootstrap status: Error");
            await output.WriteLineAsync($"Error type: {ex.GetType().Name}");
            await output.WriteLineAsync("Reason: network request timed out.");
            return 1;
        }
        catch (PolymarketApiCredentialBootstrapException ex)
        {
            await output.WriteLineAsync("Bootstrap status: Error");
            await output.WriteLineAsync($"Error type: {ex.GetType().Name}");
            await output.WriteLineAsync($"Reason: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await output.WriteLineAsync("Bootstrap status: Error");
            await output.WriteLineAsync($"Error type: {ex.GetType().Name}");
            await output.WriteLineAsync($"Reason: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> WriteFailureAsync(TextWriter output, string reason)
    {
        await output.WriteLineAsync("Polymarket API credential bootstrap: creates or derives L2 credentials only; no orders will be sent.");
        await output.WriteLineAsync("Bootstrap status: Refused");
        await output.WriteLineAsync($"Reason: {reason}");
        return 1;
    }

    private static string RedactAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "not configured";
        }

        return value.Length <= 12
            ? value
            : string.Concat(value.AsSpan(0, 6), "...", value.AsSpan(value.Length - 4, 4));
    }

}
