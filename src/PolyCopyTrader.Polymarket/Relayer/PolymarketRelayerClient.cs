using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.Auth;

namespace PolyCopyTrader.Polymarket;

public sealed class PolymarketRelayerClient(
    HttpClient httpClient,
    PolymarketAutoRedeemOptions autoRedeemOptions,
    PolymarketAuthOptions authOptions,
    ISecretProvider secretProvider,
    PolymarketDepositWalletBatchSigner batchSigner) : IPolymarketRelayerClient
{
    private const string WalletTransactionType = "WALLET";
    private const string DepositWalletFactoryPolygon = "0x00000000000Fb5C9ADea0298D729A0CB3823Cc07";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<PolymarketRelayerSubmissionResult> SubmitDepositWalletBatchAsync(
        string ownerAddress,
        string depositWalletAddress,
        IReadOnlyList<PolymarketDepositWalletCall> calls,
        string? metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(depositWalletAddress);
        ArgumentNullException.ThrowIfNull(calls);

        if (calls.Count == 0)
        {
            throw new ArgumentException("Deposit wallet batch must contain at least one call.", nameof(calls));
        }

        var privateKey = await ReadRequiredSecretAsync(
            autoRedeemOptions.RelayerSigningPrivateKeyName,
            "relayer signing private key",
            cancellationToken);
        var signingAddress = batchSigner.GetAddress(privateKey);
        if (!string.Equals(signingAddress, ownerAddress, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Relayer signing private key does not match the configured owner signer address.");
        }

        var nonce = await GetNonceAsync(ownerAddress, cancellationToken);
        var deadline = DateTimeOffset.UtcNow
            .AddSeconds(autoRedeemOptions.RelayerSubmissionDeadlineSeconds)
            .ToUnixTimeSeconds()
            .ToString();
        var batch = new PolymarketDepositWalletBatch(
            ownerAddress,
            depositWalletAddress,
            nonce,
            deadline,
            calls);
        var signature = batchSigner.Sign(batch, privateKey, authOptions.ChainId);
        var request = new DepositWalletBatchRequest(
            WalletTransactionType,
            ownerAddress,
            ResolveDepositWalletFactoryAddress(),
            nonce,
            signature,
            new DepositWalletParams(depositWalletAddress, deadline, calls),
            string.IsNullOrWhiteSpace(metadata) ? null : metadata);

        return await SubmitAsync(request, cancellationToken);
    }

    private async Task<string> GetNonceAsync(string ownerAddress, CancellationToken cancellationToken)
    {
        var requestUri = UriBuilderExtensions.WithPathAndQuery(
            autoRedeemOptions.RelayerBaseUrl,
            "/nonce",
            new Dictionary<string, string?>
            {
                ["address"] = ownerAddress,
                ["type"] = WalletTransactionType
            });

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PolymarketApiException(
                nameof(PolymarketRelayerClient),
                "GetNonce",
                $"Relayer nonce request failed with HTTP {(int)response.StatusCode}: {Trim(body)}");
        }

        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("nonce", out var nonce)
            ? nonce.ValueKind == JsonValueKind.String ? nonce.GetString() ?? "0" : nonce.ToString()
            : throw new PolymarketApiException(
                nameof(PolymarketRelayerClient),
                "GetNonce",
                "Relayer nonce response did not contain nonce.");
    }

    private async Task<PolymarketRelayerSubmissionResult> SubmitAsync(
        DepositWalletBatchRequest body,
        CancellationToken cancellationToken)
    {
        var apiKey = await ReadRequiredSecretAsync(autoRedeemOptions.RelayerApiKeyName, "relayer API key", cancellationToken);
        var apiKeyAddress = await ReadRequiredSecretAsync(
            autoRedeemOptions.RelayerApiKeyAddressName,
            "relayer API key address",
            cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(autoRedeemOptions.RelayerBaseUrl), "/submit"));
        request.Headers.TryAddWithoutValidation("RELAYER_API_KEY", apiKey);
        request.Headers.TryAddWithoutValidation("RELAYER_API_KEY_ADDRESS", apiKeyAddress.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var json = JsonSerializer.Serialize(body, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PolymarketApiException(
                nameof(PolymarketRelayerClient),
                "SubmitDepositWalletBatch",
                $"Relayer submit failed with HTTP {(int)response.StatusCode}: {Trim(responseBody)}");
        }

        var result = JsonSerializer.Deserialize<SubmitResponse>(responseBody, JsonOptions);
        if (result is null || string.IsNullOrWhiteSpace(result.TransactionId))
        {
            throw new PolymarketApiException(
                nameof(PolymarketRelayerClient),
                "SubmitDepositWalletBatch",
                "Relayer submit response did not contain transactionID.");
        }

        return new PolymarketRelayerSubmissionResult(
            result.TransactionId,
            string.IsNullOrWhiteSpace(result.State) ? "STATE_NEW" : result.State,
            string.IsNullOrWhiteSpace(result.TransactionHash) ? null : result.TransactionHash);
    }

    private string ResolveDepositWalletFactoryAddress()
    {
        return authOptions.ChainId == 137
            ? DepositWalletFactoryPolygon
            : throw new InvalidOperationException($"Deposit wallet relayer submit is not configured for chain {authOptions.ChainId}.");
    }

    private async Task<string> ReadRequiredSecretAsync(string name, string label, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"{label} secret reference is not configured.");
        }

        var value = await secretProvider.GetSecretAsync(name, cancellationToken);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{label} is unavailable from the configured secret provider.")
            : value.Trim();
    }

    private static string Trim(string value)
    {
        return value.Length <= 512 ? value : value[..512];
    }

    private sealed record DepositWalletBatchRequest(
        string Type,
        string From,
        string To,
        string Nonce,
        string Signature,
        DepositWalletParams DepositWalletParams,
        string? Metadata);

    private sealed record DepositWalletParams(
        string DepositWallet,
        string Deadline,
        IReadOnlyList<PolymarketDepositWalletCall> Calls);

    private sealed record SubmitResponse(
        [property: JsonPropertyName("transactionID")] string TransactionId,
        string State,
        string? TransactionHash);
}
