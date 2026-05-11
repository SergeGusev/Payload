using System.Net;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text.Json;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class PolymarketApiCredentialBootstrapClient
{
    private const string TimePath = "/time";
    private const string DeriveApiKeyPath = "/auth/derive-api-key";
    private const string CreateApiKeyPath = "/auth/api-key";
    private static readonly BigInteger DefaultNonce = BigInteger.Zero;

    private readonly HttpClient httpClient;
    private readonly PolymarketOptions polymarketOptions;
    private readonly PolymarketAuthOptions authOptions;
    private readonly ClobL1AuthSigner signer;
    private readonly PolymarketL1AuthHeaderFactory headerFactory;

    public PolymarketApiCredentialBootstrapClient(
        HttpClient httpClient,
        PolymarketOptions polymarketOptions,
        PolymarketAuthOptions authOptions,
        ClobL1AuthSigner signer,
        PolymarketL1AuthHeaderFactory headerFactory)
    {
        this.httpClient = httpClient;
        this.polymarketOptions = polymarketOptions;
        this.authOptions = authOptions;
        this.signer = signer;
        this.headerFactory = headerFactory;
        httpClient.Timeout = TimeSpan.FromSeconds(polymarketOptions.TimeoutSeconds);
        if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PolyCopyTrader", "1.0"));
        }
    }

    public async Task<PolymarketApiCredentialBootstrapResult> CreateOrDeriveAsync(
        string privateKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);

        if (string.IsNullOrWhiteSpace(authOptions.SigningAddress))
        {
            throw new InvalidOperationException("PolymarketAuth.SigningAddress is not configured.");
        }

        var keyAddress = signer.GetAddress(privateKey);
        if (!string.Equals(keyAddress, authOptions.SigningAddress, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Order signing private key does not match the configured signer address.");
        }

        var timestamp = await GetServerTimestampAsync(cancellationToken);
        var headers = headerFactory.CreateL1Headers(
            authOptions.SigningAddress,
            timestamp,
            DefaultNonce,
            privateKey,
            authOptions.ChainId);

        var derived = await SendCredentialRequestAsync(HttpMethod.Get, DeriveApiKeyPath, headers, cancellationToken);
        if (derived.Credentials is { } derivedCredentials)
        {
            return new PolymarketApiCredentialBootstrapResult(
                PolymarketApiCredentialBootstrapSource.Derived,
                derivedCredentials,
                timestamp,
                DefaultNonce);
        }

        if (!ShouldCreateAfterDeriveFailure(derived.StatusCode))
        {
            throw new PolymarketApiCredentialBootstrapException(
                $"Derive API credentials failed with HTTP {(int?)derived.StatusCode ?? 0}.");
        }

        var created = await SendCredentialRequestAsync(HttpMethod.Post, CreateApiKeyPath, headers, cancellationToken);
        if (created.Credentials is null)
        {
            throw new PolymarketApiCredentialBootstrapException(
                $"Create API credentials failed with HTTP {(int?)created.StatusCode ?? 0}.");
        }

        return new PolymarketApiCredentialBootstrapResult(
            PolymarketApiCredentialBootstrapSource.Created,
            created.Credentials,
            timestamp,
            DefaultNonce);
    }

    private async Task<string> GetServerTimestampAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(polymarketOptions.ClobBaseUrl), TimePath))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PolymarketApiCredentialBootstrapException($"CLOB server time check failed with HTTP {(int)response.StatusCode}.");
            }

            var timestamp = body.Trim();
            return long.TryParse(timestamp, out _)
                ? timestamp
                : throw new PolymarketApiCredentialBootstrapException("CLOB server time response was not a UNIX timestamp.");
        }
    }

    private async Task<CredentialResponse> SendCredentialRequestAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(new Uri(polymarketOptions.ClobBaseUrl), path))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PolymarketApiCredentialBootstrapException($"CLOB {path} request timed out.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new CredentialResponse(response.StatusCode, body, null);
            }

            return new CredentialResponse(response.StatusCode, body, ParseCredentials(body));
        }
    }

    private static bool ShouldCreateAfterDeriveFailure(HttpStatusCode? statusCode)
    {
        if (statusCode is HttpStatusCode.NotFound)
        {
            return true;
        }

        return statusCode is HttpStatusCode.BadRequest;
    }

    private static PolymarketApiCredentials ParseCredentials(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var apiKey = GetString(root, "apiKey") ?? GetString(root, "key");
        var secret = GetString(root, "secret");
        var passphrase = GetString(root, "passphrase");

        if (string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(secret) ||
            string.IsNullOrWhiteSpace(passphrase))
        {
            throw new PolymarketApiCredentialBootstrapException("API credential response did not contain apiKey/key, secret, and passphrase.");
        }

        return new PolymarketApiCredentials(apiKey, secret, passphrase);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;
    }

    private sealed record CredentialResponse(
        HttpStatusCode? StatusCode,
        string Body,
        PolymarketApiCredentials? Credentials);
}

public sealed record PolymarketApiCredentialBootstrapResult(
    PolymarketApiCredentialBootstrapSource Source,
    PolymarketApiCredentials Credentials,
    string Timestamp,
    BigInteger Nonce);

public enum PolymarketApiCredentialBootstrapSource
{
    Derived,
    Created
}

public sealed class PolymarketApiCredentialBootstrapException : Exception
{
    public PolymarketApiCredentialBootstrapException(string message)
        : base(message)
    {
    }

    public PolymarketApiCredentialBootstrapException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
