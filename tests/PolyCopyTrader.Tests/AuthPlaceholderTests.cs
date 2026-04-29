using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;

namespace PolyCopyTrader.Tests;

public sealed class AuthPlaceholderTests
{
    [Fact]
    public void AuthService_InterfaceOnlyExposesReadiness()
    {
        var method = Assert.Single(typeof(IPolymarketAuthService).GetMethods());

        Assert.Equal(nameof(IPolymarketAuthService.GetReadinessAsync), method.Name);
        Assert.Equal(typeof(Task<AuthReadinessStatus>), method.ReturnType);
    }

    [Fact]
    public void TradingClient_ExposesDryRunAndGatedLiveMethods()
    {
        var methods = typeof(IPolymarketTradingClient).GetMethods().Select(method => method.Name).ToArray();

        Assert.Contains(nameof(IPolymarketTradingClient.PrepareDryRunOrderAsync), methods);
        Assert.Contains(nameof(IPolymarketTradingClient.PlaceLiveOrderAsync), methods);
        Assert.Contains(nameof(IPolymarketTradingClient.CancelAllOrdersAsync), methods);
        Assert.DoesNotContain(methods, method => method.Contains("Taker", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Readiness_NotConfiguredDoesNotRequireSecrets()
    {
        var checkedAt = new DateTimeOffset(2026, 04, 29, 00, 00, 00, TimeSpan.Zero);

        var status = AuthReadinessStatus.NotConfigured(checkedAt);

        Assert.Equal("NotConfigured", status.State);
        Assert.False(status.IsConfigured);
        Assert.False(status.CanAuthenticate);
        Assert.Contains(
            status.MissingRequirements,
            item => item.Contains("No API credentials", StringComparison.Ordinal));
        Assert.Equal(checkedAt, status.CheckedAtUtc);
    }

    [Fact]
    public void HmacSigner_MatchesOfficialPythonClientVector()
    {
        var signer = new PolymarketL2HmacSigner();

        var signature = signer.Sign(
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "1000000",
            "test-sign",
            "/orders",
            """{"hash": "0x123"}""");

        Assert.Equal("ZwAdJKvoYRlEKDkNMwd5BuwNNtg93kNaR_oU2HrfVvc=", signature);
    }

    [Fact]
    public void HmacSigner_OmitsNullAndEmptyBody()
    {
        var signer = new PolymarketL2HmacSigner();

        var nullBody = signer.Sign("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "1000000", "GET", "/data/orders");
        var emptyBody = signer.Sign("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "1000000", "GET", "/data/orders", string.Empty);

        Assert.Equal(nullBody, emptyBody);
    }

    [Fact]
    public void HeaderFactory_CreatesDeterministicL2Headers()
    {
        var factory = new PolymarketAuthHeaderFactory(new PolymarketL2HmacSigner());
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1_000_000);

        var headers = factory.CreateL2Headers(
            "0x1111111111111111111111111111111111111111",
            new PolymarketApiCredentials("fixture-key", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "fixture-passphrase"),
            new PolymarketAuthenticatedRequest("post", "/orders", """[{"id":"abc"}]"""),
            timestamp);

        Assert.Equal("0x1111111111111111111111111111111111111111", headers[PolymarketAuthHeaderFactory.PolyAddress]);
        Assert.Equal("1000000", headers[PolymarketAuthHeaderFactory.PolyTimestamp]);
        Assert.Equal("fixture-key", headers[PolymarketAuthHeaderFactory.PolyApiKey]);
        Assert.Equal("fixture-passphrase", headers[PolymarketAuthHeaderFactory.PolyPassphrase]);
        Assert.Equal(
            new PolymarketL2HmacSigner().Sign("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "1000000", "POST", "/orders", """[{"id":"abc"}]"""),
            headers[PolymarketAuthHeaderFactory.PolySignature]);
    }

    [Fact]
    public async Task EnvironmentSecretProvider_ReadsEnvironmentVariableByName()
    {
        const string variableName = "POLYCOPYTRADER_TEST_ENV_SECRET";
        var previous = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, "fixture-value");
            var provider = new EnvironmentVariableSecretProvider();

            var value = await provider.GetSecretAsync(variableName, CancellationToken.None);

            Assert.Equal("fixture-value", value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
        }
    }

    [Fact]
    public async Task AuthReadiness_DisabledIsNotConfigured()
    {
        var service = new PolymarketAuthReadinessService(
            new PolymarketAuthOptions { Enabled = false },
            new FakeSecretProvider(),
            new PolymarketL2HmacSigner());

        var status = await service.GetReadinessAsync(CancellationToken.None);

        Assert.Equal("NotConfigured", status.State);
        Assert.False(status.IsConfigured);
        Assert.Contains(status.MissingRequirements, item => item.Contains("disabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AuthReadiness_ConfiguredInputsAreConfiguredButUntested()
    {
        var service = new PolymarketAuthReadinessService(
            new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "0x1111111111111111111111111111111111111111",
                ApiKeyName = "api-key",
                ApiKeyOwnerName = "api-key-owner",
                ApiSecretName = "api-secret",
                ApiPassphraseName = "api-passphrase"
            },
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["api-key"] = "fixture-key",
                ["api-key-owner"] = "fixture-owner",
                ["api-secret"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                ["api-passphrase"] = "fixture-passphrase"
            }),
            new PolymarketL2HmacSigner());

        var status = await service.GetReadinessAsync(CancellationToken.None);

        Assert.Equal("ConfiguredButUntested", status.State);
        Assert.True(status.IsConfigured);
        Assert.True(status.CanAuthenticate);
        Assert.Empty(status.MissingRequirements);
    }

    [Fact]
    public async Task AuthReadiness_InvalidBase64SecretReturnsError()
    {
        var service = new PolymarketAuthReadinessService(
            new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "0x1111111111111111111111111111111111111111",
                ApiKeyName = "api-key",
                ApiKeyOwnerName = "api-key-owner",
                ApiSecretName = "api-secret",
                ApiPassphraseName = "api-passphrase"
            },
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["api-key"] = "fixture-key",
                ["api-key-owner"] = "fixture-owner",
                ["api-secret"] = "!!!not-base64!!!",
                ["api-passphrase"] = "fixture-passphrase"
            }),
            new PolymarketL2HmacSigner());

        var status = await service.GetReadinessAsync(CancellationToken.None);

        Assert.Equal("Error", status.State);
        Assert.Contains(status.MissingRequirements, item => item.Contains("base64", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OrderAmountCalculator_ConvertsBuyAndSellAmounts()
    {
        var calculator = new OrderAmountCalculator();

        var buy = calculator.Calculate(TradeSide.Buy, 0.74m, 100m);
        var sell = calculator.Calculate(TradeSide.Sell, 0.74m, 100m);

        Assert.Equal("74000000", buy.MakerAmount.ToString());
        Assert.Equal("100000000", buy.TakerAmount.ToString());
        Assert.Equal("100000000", sell.MakerAmount.ToString());
        Assert.Equal("74000000", sell.TakerAmount.ToString());
        Assert.Equal(0, ClobV2OrderBuilder.SideToTypedValue(TradeSide.Buy));
        Assert.Equal(1, ClobV2OrderBuilder.SideToTypedValue(TradeSide.Sell));
    }

    [Fact]
    public void OrderBuilder_ValidatesTickSizeAndMinimumSize()
    {
        var builder = new ClobV2OrderBuilder(new OrderAmountCalculator());

        var errors = builder.Validate(FixedOrderRequest() with
        {
            Price = 0.745m,
            TickSize = 0.01m,
            SizeShares = 0.5m,
            MinOrderSize = 1m
        });

        Assert.Contains(errors, error => error.Contains("tick size", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("minimum order size", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OrderBuilder_ValidatesSixDecimalTokenUnits()
    {
        var builder = new ClobV2OrderBuilder(new OrderAmountCalculator());

        var errors = builder.Validate(FixedOrderRequest() with
        {
            Price = 0.01m,
            SizeShares = 1.123456m,
            MinOrderSize = 1m
        });

        Assert.Contains(errors, error => error.Contains("notional", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OrderBuilder_AddsGtdExpirationToWirePayloadOnly()
    {
        var expiration = new DateTimeOffset(2026, 05, 01, 12, 0, 0, TimeSpan.Zero);
        var order = new ClobV2OrderBuilder(new OrderAmountCalculator()).Build(FixedOrderRequest() with
        {
            OrderType = ClobV2OrderType.GTD,
            GtdExpirationUtc = expiration
        });

        var payload = new ClobV2OrderPayloadSerializer().Serialize(order, null);
        using var json = JsonDocument.Parse(payload);

        Assert.Equal(expiration.ToUnixTimeSeconds().ToString(), order.Expiration);
        Assert.Equal("GTD", json.RootElement.GetProperty("orderType").GetString());
        Assert.Equal(expiration.ToUnixTimeSeconds().ToString(), json.RootElement.GetProperty("order").GetProperty("expiration").GetString());
    }

    [Fact]
    public async Task DryRunTradingClient_MissingKeyCreatesUnsignedDryRun()
    {
        var client = CreateDryRunClient(new Dictionary<string, string>());

        var result = await client.PrepareDryRunOrderAsync(FixedOrderRequest(), CancellationToken.None);

        Assert.Equal(DryRunOrderStatus.DryRunUnsigned, result.Status);
        Assert.Null(result.Signature);
        Assert.Contains(@"""signature"":""""", result.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DryRunTradingClient_SignsAndRedactsPayloadWithDeterministicTestKey()
    {
        var signer = new ClobV2OrderSigner();
        var privateKey = DeterministicUnfundedTestPrivateKey;
        var signerAddress = signer.GetAddress(privateKey);
        var client = CreateDryRunClient(new Dictionary<string, string>
        {
            ["dry-run-private-key"] = privateKey
        });

        var result = await client.PrepareDryRunOrderAsync(FixedOrderRequest() with
        {
            MakerAddress = signerAddress,
            SignerAddress = signerAddress
        }, CancellationToken.None);

        Assert.Equal(DryRunOrderStatus.DryRunSigned, result.Status);
        Assert.NotNull(result.Signature);
        Assert.True(signer.Verify(result.Order, result.Signature!, signerAddress));
        Assert.Contains(result.Signature!, result.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Signature!, result.RedactedPayloadJson, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.RedactedPayloadJson, StringComparison.Ordinal);
        Assert.Equal(
            "0x05358de9a0bc0dac3413355799171ad6b3833fd3647983395f8589da9cea8e99207258c0563ac8132ae6dd9ce3afc46b537d83d440b6d651ddbe5560405497491c",
            result.Signature);
    }

    [Fact]
    public async Task DryRunTradingClient_InvalidKeyCreatesRejectedDryRun()
    {
        var client = CreateDryRunClient(new Dictionary<string, string>
        {
            ["dry-run-private-key"] = "not-a-private-key"
        });

        var result = await client.PrepareDryRunOrderAsync(FixedOrderRequest(), CancellationToken.None);

        Assert.Equal(DryRunOrderStatus.DryRunRejected, result.Status);
        Assert.Null(result.Signature);
        Assert.Contains(result.ValidationMessages, error => error.Contains("signing failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TradingClient_PostsLiveOrderWithL2HeadersAndRedactedRequest()
    {
        var signer = new ClobV2OrderSigner();
        var privateKey = DeterministicUnfundedTestPrivateKey;
        var signerAddress = signer.GetAddress(privateKey);
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""{"success":true,"orderID":"0xorder","status":"live","makingAmount":"999000","takingAmount":"1350000"}""")
        });
        var client = CreateLiveClient(handler, new Dictionary<string, string>
        {
            ["api-key"] = "fixture-key",
            ["api-key-owner"] = "fixture-owner",
            ["api-secret"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            ["api-passphrase"] = "fixture-passphrase",
            ["live-private-key"] = privateKey
        });

        var result = await client.PlaceLiveOrderAsync(FixedOrderRequest() with
        {
            MakerAddress = signerAddress,
            SignerAddress = signerAddress,
            OrderType = ClobV2OrderType.GTD,
            GtdExpirationUtc = new DateTimeOffset(2026, 05, 01, 12, 0, 0, TimeSpan.Zero)
        }, CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        var body = handler.Bodies.Single();
        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/order", sent.RequestUri?.AbsolutePath);
        Assert.True(sent.Headers.Contains(PolymarketAuthHeaderFactory.PolySignature));
        Assert.Contains(@"""owner"":""fixture-owner""", body, StringComparison.Ordinal);
        Assert.Contains(@"""orderType"":""GTD""", body, StringComparison.Ordinal);
        Assert.Contains(@"""postOnly"":true", body, StringComparison.Ordinal);
        Assert.Contains(@"""expiration"":""1777636800""", body, StringComparison.Ordinal);
        Assert.Contains(@"""signature"":""0x", body, StringComparison.Ordinal);
        Assert.DoesNotContain(@"""signature"":""0x", result.RedactedRequestJson, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.RedactedRequestJson, StringComparison.Ordinal);
    }

    // Public deterministic local test key only. Never fund it or replace it with a real key.
    private const string DeterministicUnfundedTestPrivateKey =
        "0x0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static ClobV2OrderRequest FixedOrderRequest()
    {
        var signer = new ClobV2OrderSigner();
        var signerAddress = signer.GetAddress(DeterministicUnfundedTestPrivateKey);
        return new ClobV2OrderRequest(
            "12345678901234567890",
            TradeSide.Buy,
            0.74m,
            100m,
            0.01m,
            1m,
            signerAddress,
            signerAddress,
            ClobV2SignatureType.EOA,
            ClobV2OrderType.GTC,
            new DateTimeOffset(2026, 04, 29, 12, 0, 0, TimeSpan.Zero),
            Salt: "123456789");
    }

    private static PolymarketTradingClient CreateDryRunClient(IReadOnlyDictionary<string, string> secrets)
    {
        return new PolymarketTradingClient(
            new HttpClient(new RejectingHttpMessageHandler()),
            new PolymarketOptions
            {
                ClobBaseUrl = "https://clob.polymarket.com",
                TimeoutSeconds = 30
            },
            new PolymarketAuthOptions
            {
                ChainId = 137,
                DryRunSigningEnabled = true,
                DryRunPrivateKeyName = "dry-run-private-key"
            },
            new FakeSecretProvider(secrets),
            new ClobV2OrderBuilder(new OrderAmountCalculator()),
            new ClobV2OrderSigner(),
            new ClobV2OrderPayloadSerializer(),
            new PolymarketAuthHeaderFactory(new PolymarketL2HmacSigner()),
            new CapturingApiErrorSink());
    }

    private static PolymarketTradingClient CreateLiveClient(
        HttpMessageHandler handler,
        IReadOnlyDictionary<string, string> secrets)
    {
        return new PolymarketTradingClient(
            new HttpClient(handler),
            new PolymarketOptions
            {
                ClobBaseUrl = "https://clob.polymarket.com",
                TimeoutSeconds = 30
            },
            new PolymarketAuthOptions
            {
                Enabled = true,
                ChainId = 137,
                SigningAddress = new ClobV2OrderSigner().GetAddress(DeterministicUnfundedTestPrivateKey),
                FunderAddress = new ClobV2OrderSigner().GetAddress(DeterministicUnfundedTestPrivateKey),
                ApiKeyName = "api-key",
                ApiKeyOwnerName = "api-key-owner",
                ApiSecretName = "api-secret",
                ApiPassphraseName = "api-passphrase",
                OrderSigningPrivateKeyName = "live-private-key"
            },
            new FakeSecretProvider(secrets),
            new ClobV2OrderBuilder(new OrderAmountCalculator()),
            new ClobV2OrderSigner(),
            new ClobV2OrderPayloadSerializer(),
            new PolymarketAuthHeaderFactory(new PolymarketL2HmacSigner()),
            new CapturingApiErrorSink());
    }

    private sealed class FakeSecretProvider(IReadOnlyDictionary<string, string>? values = null) : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string name, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(values is not null && values.TryGetValue(name, out var value) ? value : null);
        }
    }

    private sealed class CapturingApiErrorSink : IPolymarketApiErrorSink
    {
        public Task RecordAsync(ApiError error, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RejectingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Dry-run signing tests must not send HTTP requests.");
        }
    }

    private sealed class CapturingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return responseFactory(request);
        }
    }
}
