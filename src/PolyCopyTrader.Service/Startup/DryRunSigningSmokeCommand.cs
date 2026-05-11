using System.Text;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;

namespace PolyCopyTrader.Service.Startup;

public static class DryRunSigningSmokeCommand
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
        await output.WriteLineAsync("Dry-run signing smoke: local only; no HTTP request will be sent.");
        await output.WriteLineAsync($"Live trading enabled: {configuration.Bot.EnableLiveTrading}");
        await output.WriteLineAsync($"Auth enabled: {authOptions.Enabled}");
        await output.WriteLineAsync($"Dry-run signing enabled: {authOptions.DryRunSigningEnabled}");
        await output.WriteLineAsync($"Signer address: {RedactAddress(authOptions.SigningAddress)}");
        await output.WriteLineAsync($"Funder address: {RedactAddress(ResolveMakerAddress(authOptions))}");

        if (!authOptions.DryRunSigningEnabled)
        {
            await output.WriteLineAsync("Dry-run signing smoke status: DryRunSigningDisabled");
            return 1;
        }

        try
        {
            using var httpClient = new HttpClient(new RejectingHttpMessageHandler());
            var client = new PolymarketTradingClient(
                httpClient,
                configuration.Polymarket,
                authOptions,
                secretProvider,
                new ClobV2OrderBuilder(new OrderAmountCalculator()),
                new ClobV2OrderSigner(),
                new ClobV2OrderPayloadSerializer(),
                new PolymarketAuthHeaderFactory(new PolymarketL2HmacSigner()),
                new NullPolymarketApiErrorSink());

            var result = await client.PrepareDryRunOrderAsync(CreateSmokeOrderRequest(authOptions), cancellationToken);
            var signaturePresent = !string.IsNullOrWhiteSpace(result.Signature);

            await output.WriteLineAsync($"Dry-run signing smoke status: {result.Status}");
            await output.WriteLineAsync($"Signature present: {signaturePresent}");
            await output.WriteLineAsync($"Local signature verified: {result.Status == DryRunOrderStatus.DryRunSigned}");
            await output.WriteLineAsync($"Redacted payload bytes: {Encoding.UTF8.GetByteCount(result.RedactedPayloadJson)}");

            foreach (var message in result.ValidationMessages)
            {
                await output.WriteLineAsync($"Validation: {message}");
            }

            return result.Status == DryRunOrderStatus.DryRunSigned ? 0 : 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await output.WriteLineAsync("Dry-run signing smoke status: Error");
            await output.WriteLineAsync($"Error type: {ex.GetType().Name}");
            return 1;
        }
    }

    private static ClobV2OrderRequest CreateSmokeOrderRequest(PolymarketAuthOptions authOptions)
    {
        return new ClobV2OrderRequest(
            TokenId: "12345678901234567890",
            Side: TradeSide.Buy,
            Price: 0.50m,
            SizeShares: 2.00m,
            TickSize: 0.01m,
            MinOrderSize: 1.00m,
            MakerAddress: ResolveMakerAddress(authOptions),
            SignerAddress: authOptions.SigningAddress,
            SignatureType: ParseSignatureType(authOptions.SignatureType),
            OrderType: ClobV2OrderType.GTC,
            CreatedAtUtc: DateTimeOffset.FromUnixTimeSeconds(1_800_000_000),
            Salt: "123456789");
    }

    private static string ResolveMakerAddress(PolymarketAuthOptions authOptions)
    {
        return string.IsNullOrWhiteSpace(authOptions.FunderAddress)
            ? authOptions.SigningAddress
            : authOptions.FunderAddress;
    }

    private static ClobV2SignatureType ParseSignatureType(string value)
    {
        return Enum.TryParse<ClobV2SignatureType>(value, ignoreCase: true, out var signatureType)
            ? signatureType
            : ClobV2SignatureType.EOA;
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

    private sealed class RejectingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Dry-run signing smoke must not send HTTP requests.");
        }
    }
}
