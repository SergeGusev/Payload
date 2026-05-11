using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class PolymarketTradingClient : IPolymarketTradingClient
{
    private const string PostOrderPath = "/order";
    private const string CancelAllPath = "/cancel-all";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly PolymarketOptions polymarketOptions;
    private readonly PolymarketAuthOptions authOptions;
    private readonly ISecretProvider secretProvider;
    private readonly ClobV2OrderBuilder orderBuilder;
    private readonly ClobV2OrderSigner orderSigner;
    private readonly ClobV2OrderPayloadSerializer payloadSerializer;
    private readonly PolymarketAuthHeaderFactory headerFactory;
    private readonly IPolymarketApiErrorSink errorSink;
    private readonly IPolymarketHttpLogSink httpLogSink;

    public PolymarketTradingClient(
        HttpClient httpClient,
        PolymarketOptions polymarketOptions,
        PolymarketAuthOptions authOptions,
        ISecretProvider secretProvider,
        ClobV2OrderBuilder orderBuilder,
        ClobV2OrderSigner orderSigner,
        ClobV2OrderPayloadSerializer payloadSerializer,
        PolymarketAuthHeaderFactory headerFactory,
        IPolymarketApiErrorSink errorSink,
        IPolymarketHttpLogSink? httpLogSink = null)
    {
        this.httpClient = httpClient;
        this.polymarketOptions = polymarketOptions;
        this.authOptions = authOptions;
        this.secretProvider = secretProvider;
        this.orderBuilder = orderBuilder;
        this.orderSigner = orderSigner;
        this.payloadSerializer = payloadSerializer;
        this.headerFactory = headerFactory;
        this.errorSink = errorSink;
        this.httpLogSink = httpLogSink ?? new NullPolymarketHttpLogSink();
        httpClient.Timeout = TimeSpan.FromSeconds(polymarketOptions.TimeoutSeconds);
    }

    public async Task<ClobV2DryRunOrderResult> PrepareDryRunOrderAsync(
        ClobV2OrderRequest request,
        CancellationToken ct)
    {
        Guard.NotNull(request, nameof(request));

        var validationMessages = orderBuilder.Validate(request).ToList();
        if (validationMessages.Count > 0)
        {
            var rejectedOrder = BuildRejectedOrder(request);
            var rejectedPayload = payloadSerializer.SerializeRedacted(rejectedOrder, null);
            return new ClobV2DryRunOrderResult(
                DryRunOrderStatus.DryRunRejected,
                rejectedOrder,
                null,
                rejectedPayload,
                rejectedPayload,
                validationMessages);
        }

        ClobV2Order order;
        try
        {
            order = orderBuilder.Build(request);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            validationMessages.Add($"Dry-run order build failed: {ex.GetType().Name}.");
            var rejectedOrder = BuildRejectedOrder(request);
            var rejectedPayload = payloadSerializer.SerializeRedacted(rejectedOrder, null);
            return new ClobV2DryRunOrderResult(
                DryRunOrderStatus.DryRunRejected,
                rejectedOrder,
                null,
                rejectedPayload,
                rejectedPayload,
                validationMessages);
        }

        string? signature = null;
        if (authOptions.DryRunSigningEnabled)
        {
            var privateKey = await secretProvider.GetSecretAsync(authOptions.DryRunPrivateKeyName, ct);
            if (!string.IsNullOrWhiteSpace(privateKey))
            {
                try
                {
                    var keyAddress = orderSigner.GetAddress(privateKey);
                    if (!string.Equals(keyAddress, request.SignerAddress, StringComparison.OrdinalIgnoreCase))
                    {
                        validationMessages.Add("Dry-run private key does not match the request signer address.");
                    }
                    else
                    {
                        signature = orderSigner.Sign(order, privateKey, authOptions.ChainId);
                        if (!orderSigner.Verify(order, signature, request.SignerAddress, authOptions.ChainId))
                        {
                            validationMessages.Add("Dry-run signature verification failed.");
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    validationMessages.Add($"Dry-run signing failed: {ex.GetType().Name}.");
                }
            }
        }

        if (validationMessages.Count > 0)
        {
            signature = null;
        }

        var status = validationMessages.Count > 0
            ? DryRunOrderStatus.DryRunRejected
            : string.IsNullOrWhiteSpace(signature)
                ? DryRunOrderStatus.DryRunUnsigned
                : DryRunOrderStatus.DryRunSigned;

        var payload = payloadSerializer.Serialize(order, status == DryRunOrderStatus.DryRunSigned ? signature : null);
        var redactedPayload = payloadSerializer.SerializeRedacted(order, status == DryRunOrderStatus.DryRunSigned ? signature : null);
        return new ClobV2DryRunOrderResult(status, order, signature, payload, redactedPayload, validationMessages);
    }

    public async Task<LiveOrderPlacementResult> PlaceLiveOrderAsync(
        ClobV2OrderRequest request,
        CancellationToken ct)
    {
        Guard.NotNull(request, nameof(request));

        var secrets = await LoadAuthenticatedSecretsAsync(ct);
        var privateKey = await ReadRequiredSecretAsync(authOptions.OrderSigningPrivateKeyName, "order signing private key", ct);
        var order = orderBuilder.Build(request);
        var signerAddress = orderSigner.GetAddress(privateKey);
        if (!string.Equals(signerAddress, request.SignerAddress, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Order signing private key does not match the configured signer address.");
        }

        var signature = orderSigner.Sign(order, privateKey, authOptions.ChainId);
        if (!orderSigner.Verify(order, signature, request.SignerAddress, authOptions.ChainId))
        {
            throw new InvalidOperationException("Live order signature verification failed.");
        }

        var body = payloadSerializer.Serialize(order, signature, secrets.Credentials.ApiKey);
        var redactedBody = payloadSerializer.SerializeRedacted(order, signature, "[REDACTED_OWNER]");
        var response = await SendAuthenticatedAsync(HttpMethod.Post, PostOrderPath, "PostOrder", body, secrets.Credentials, ct);

        if (!response.IsSuccessStatusCode)
        {
            await RecordErrorAsync("PostOrder", response.Body, ct);
            return new LiveOrderPlacementResult(
                false,
                null,
                response.StatusCode.ToString(),
                $"HTTP {(int)response.StatusCode}",
                null,
                null,
                Redact(response.Body),
                redactedBody);
        }

        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        var success = GetBool(root, "success");
        var status = GetString(root, "status");
        var errorMessage = GetString(root, "errorMsg");
        return new LiveOrderPlacementResult(
            success && string.IsNullOrWhiteSpace(errorMessage),
            GetString(root, "orderID"),
            status ?? string.Empty,
            errorMessage,
            GetString(root, "makingAmount"),
            GetString(root, "takingAmount"),
            Redact(response.Body),
            redactedBody);
    }

    public async Task<LiveOrderCancellationResult> CancelOrderAsync(string orderId, CancellationToken ct)
    {
        Guard.NotNullOrWhiteSpace(orderId, nameof(orderId));
        var body = JsonSerializer.Serialize(new { orderID = orderId }, JsonOptions);
        return await CancelAsync(PostOrderPath, body, "CancelOrder", ct);
    }

    public async Task<LiveOrderCancellationResult> CancelAllOrdersAsync(CancellationToken ct)
    {
        return await CancelAsync(CancelAllPath, null, "CancelAllOrders", ct);
    }

    public async Task<LiveOrderStatusResult?> GetLiveOrderStatusAsync(string orderId, CancellationToken ct)
    {
        Guard.NotNullOrWhiteSpace(orderId, nameof(orderId));
        var secrets = await LoadAuthenticatedSecretsAsync(ct);
        var path = "/order/" + Uri.EscapeDataString(orderId);
        var response = await SendAuthenticatedAsync(HttpMethod.Get, path, "GetLiveOrderStatus", null, secrets.Credentials, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            await RecordErrorAsync("GetLiveOrderStatus", response.Body, ct);
            throw new PolymarketApiException(
                nameof(PolymarketTradingClient),
                "GetLiveOrderStatus",
                $"Get live order status failed with HTTP {(int)response.StatusCode}. Body: {Redact(response.Body)}");
        }

        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        return new LiveOrderStatusResult(
            GetString(root, "id") ?? orderId,
            GetString(root, "status") ?? string.Empty,
            GetString(root, "original_size") ?? "0",
            GetString(root, "size_matched") ?? "0",
            GetString(root, "price") ?? string.Empty,
            Redact(response.Body));
    }

    private async Task<LiveOrderCancellationResult> CancelAsync(
        string path,
        string? body,
        string operation,
        CancellationToken ct)
    {
        var secrets = await LoadAuthenticatedSecretsAsync(ct);
        var response = await SendAuthenticatedAsync(HttpMethod.Delete, path, operation, body, secrets.Credentials, ct);

        if (!response.IsSuccessStatusCode)
        {
            await RecordErrorAsync(operation, response.Body, ct);
            return new LiveOrderCancellationResult(
                false,
                [],
                new Dictionary<string, string>(),
                Redact(response.Body),
                $"HTTP {(int)response.StatusCode}");
        }

        using var json = JsonDocument.Parse(response.Body);
        var canceled = new List<string>();
        if (json.RootElement.TryGetProperty("canceled", out var canceledJson) &&
            canceledJson.ValueKind == JsonValueKind.Array)
        {
            canceled.AddRange(canceledJson.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item))!);
        }

        var notCanceled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (json.RootElement.TryGetProperty("not_canceled", out var notCanceledJson) &&
            notCanceledJson.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in notCanceledJson.EnumerateObject())
            {
                notCanceled[property.Name] = property.Value.ToString();
            }
        }

        return new LiveOrderCancellationResult(
            notCanceled.Count == 0,
            canceled,
            notCanceled,
            Redact(response.Body));
    }

    private async Task<AuthenticatedSecrets> LoadAuthenticatedSecretsAsync(CancellationToken ct)
    {
        var apiKey = await ReadRequiredSecretAsync(authOptions.ApiKeyName, "API key", ct);
        var apiSecret = await ReadRequiredSecretAsync(authOptions.ApiSecretName, "API secret", ct);
        var apiPassphrase = await ReadRequiredSecretAsync(authOptions.ApiPassphraseName, "API passphrase", ct);

        return new AuthenticatedSecrets(
            new PolymarketApiCredentials(apiKey, apiSecret, apiPassphrase));
    }

    private async Task<string> ReadRequiredSecretAsync(string name, string label, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"{label} secret reference is not configured.");
        }

        var value = await secretProvider.GetSecretAsync(name, ct);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{label} is unavailable from the configured secret provider.")
            : value;
    }

    private async Task<AuthenticatedResponse> SendAuthenticatedAsync(
        HttpMethod method,
        string path,
        string operation,
        string? body,
        PolymarketApiCredentials credentials,
        CancellationToken ct)
    {
        var requestUri = new Uri(new Uri(polymarketOptions.ClobBaseUrl), path);
        using var request = new HttpRequestMessage(method, requestUri);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        foreach (var header in headerFactory.CreateL2Headers(
            authOptions.SigningAddress,
            credentials,
            new PolymarketAuthenticatedRequest(method.Method, path, body),
            DateTimeOffset.UtcNow))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var requestedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync();
            await RecordHttpLogAsync(
                method.Method,
                requestUri,
                operation,
                requestedAtUtc,
                stopwatch.ElapsedMilliseconds,
                response.StatusCode,
                response.IsSuccessStatusCode,
                Redact(responseBody),
                null,
                ct);

            return new AuthenticatedResponse(response.StatusCode, response.IsSuccessStatusCode, responseBody);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordHttpLogAsync(
                method.Method,
                requestUri,
                operation,
                requestedAtUtc,
                stopwatch.ElapsedMilliseconds,
                null,
                false,
                string.Empty,
                ex.Message,
                ct);
            throw;
        }
    }

    private Task RecordErrorAsync(string operation, string message, CancellationToken ct)
    {
        return errorSink.RecordAsync(
            new ApiError(Guid.NewGuid(), nameof(PolymarketTradingClient), operation, Redact(message), DateTimeOffset.UtcNow),
            ct);
    }

    private Task RecordHttpLogAsync(
        string httpMethod,
        Uri requestUri,
        string operation,
        DateTimeOffset requestedAtUtc,
        long durationMilliseconds,
        HttpStatusCode? statusCode,
        bool succeeded,
        string responseBody,
        string? errorMessage,
        CancellationToken ct)
    {
        return httpLogSink.RecordAsync(
            new PolymarketHttpLogEntry(
                Guid.NewGuid(),
                nameof(PolymarketTradingClient),
                operation,
                httpMethod,
                PolymarketRequestUrlFormatter.Format(requestUri),
                requestedAtUtc,
                statusCode is null ? null : DateTimeOffset.UtcNow,
                Math.Max(0, durationMilliseconds),
                1,
                statusCode is { } value ? (int)value : null,
                succeeded,
                Redact(responseBody),
                errorMessage is null ? null : Redact(errorMessage)),
            ct);
    }

    private static ClobV2Order BuildRejectedOrder(ClobV2OrderRequest request)
    {
        return new ClobV2Order(
            request.Salt ?? "0",
            request.MakerAddress,
            request.SignatureType == ClobV2SignatureType.POLY_1271
                ? request.MakerAddress
                : request.SignerAddress,
            request.TokenId,
            "0",
            "0",
            request.Side,
            request.SignatureType,
            request.CreatedAtUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ClobV2OrderBuilder.NormalizeBytes32(request.Metadata),
            ClobV2OrderBuilder.NormalizeBytes32(request.Builder),
            "0",
            request.OrderType,
            request.PostOnly,
            request.DeferExec,
            request.NegativeRisk);
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= 2_048 ? value : value.Substring(0, 2_048);
    }

    private sealed record AuthenticatedSecrets(PolymarketApiCredentials Credentials);

    private sealed record AuthenticatedResponse(
        HttpStatusCode StatusCode,
        bool IsSuccessStatusCode,
        string Body);
}
