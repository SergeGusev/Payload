using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.Auth;

namespace PolyCopyTrader.Service.Startup;

public static class AuthReadinessSmokeCommand
{
    public static Task<int> ExecuteAsync(
        AppConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            configuration,
            PolymarketSecretProviderFactory.Create(configuration.PolymarketAuth),
            output,
            cancellationToken);
    }

    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        ISecretProvider secretProvider,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secretProvider);
        ArgumentNullException.ThrowIfNull(output);

        var authOptions = configuration.PolymarketAuth;
        await output.WriteLineAsync("Auth readiness smoke: local HMAC/header construction only; no HTTP request will be sent.");
        await output.WriteLineAsync($"Live trading enabled: {configuration.Bot.EnableLiveTrading}");
        await output.WriteLineAsync($"Auth enabled: {authOptions.Enabled}");
        await output.WriteLineAsync($"Signer address: {RedactAddress(authOptions.SigningAddress)}");
        await output.WriteLineAsync($"Funder address: {RedactAddress(authOptions.FunderAddress)}");

        var service = new PolymarketAuthReadinessService(
            authOptions,
            secretProvider,
            new PolymarketL2HmacSigner());

        var status = await service.GetReadinessAsync(cancellationToken);
        await output.WriteLineAsync($"Auth readiness status: {status.State}");
        await output.WriteLineAsync($"Auth can authenticate: {status.CanAuthenticate}");
        foreach (var requirement in status.MissingRequirements)
        {
            await output.WriteLineAsync($"Requirement: {requirement}");
        }

        return status.CanAuthenticate ? 0 : 1;
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
