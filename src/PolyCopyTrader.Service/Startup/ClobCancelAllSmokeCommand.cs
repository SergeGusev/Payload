using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Polymarket;

namespace PolyCopyTrader.Service.Startup;

public static class ClobCancelAllSmokeCommand
{
    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        return await ExecuteAsync(
            configuration,
            PolymarketSecretProviderFactory.Create(configuration.PolymarketAuth),
            httpClient,
            output,
            cancellationToken);
    }

    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        ISecretProvider secretProvider,
        HttpClient httpClient,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secretProvider);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(output);

        var authOptions = configuration.PolymarketAuth;
        await output.WriteLineAsync("CLOB cancel-all smoke: DELETE /cancel-all; no order placement will be sent.");
        await output.WriteLineAsync($"Live trading enabled: {configuration.Bot.EnableLiveTrading}");
        await output.WriteLineAsync($"Auth enabled: {authOptions.Enabled}");
        await output.WriteLineAsync($"Signer address: {RedactAddress(authOptions.SigningAddress)}");
        await output.WriteLineAsync($"Funder address: {RedactAddress(authOptions.FunderAddress)}");

        if (!authOptions.Enabled)
        {
            await output.WriteLineAsync("CLOB cancel-all smoke status: AuthDisabled");
            return 1;
        }

        try
        {
            var client = new PolymarketTradingClient(
                httpClient,
                configuration.Polymarket,
                authOptions,
                secretProvider,
                new ClobV2OrderBuilder(new OrderAmountCalculator()),
                new ClobV2OrderSigner(),
                new ClobV2OrderPayloadSerializer(),
                new PolymarketAuthHeaderFactory(new PolymarketL2HmacSigner()),
                new NullPolymarketApiErrorSink(),
                new NullPolymarketHttpLogSink());

            var result = await client.CancelAllOrdersAsync(cancellationToken);
            await output.WriteLineAsync($"Canceled count: {result.CanceledOrderIds.Count}");
            await output.WriteLineAsync($"Not canceled count: {result.NotCanceled.Count}");
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                await output.WriteLineAsync($"Error: {result.ErrorMessage}");
            }

            await output.WriteLineAsync($"CLOB cancel-all smoke status: {(result.Success ? "OK" : "Error")}");
            return result.Success ? 0 : 1;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("CLOB cancel-all smoke status: Error");
            await output.WriteLineAsync("Reason: network request timed out.");
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await output.WriteLineAsync("CLOB cancel-all smoke status: Error");
            await output.WriteLineAsync($"Error type: {ex.GetType().Name}");
            await output.WriteLineAsync($"Reason: {ex.Message}");
            return 1;
        }
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
