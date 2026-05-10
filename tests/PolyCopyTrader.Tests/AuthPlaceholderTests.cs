using System.Net;
using System.Globalization;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Startup;

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
    public async Task AuthReadinessSmokeCommand_PrintsNoSecretsAndReturnsSuccess()
    {
        var output = new StringWriter();

        var exitCode = await AuthReadinessSmokeCommand.ExecuteAsync(
            CreateAuthSmokeConfiguration(),
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["api-key"] = "fixture-key",
                ["api-key-owner"] = "fixture-key",
                ["api-secret"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                ["api-passphrase"] = "fixture-passphrase"
            }),
            output,
            CancellationToken.None);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Auth readiness status: ConfiguredButUntested", text, StringComparison.Ordinal);
        Assert.Contains("Auth can authenticate: True", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-passphrase", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthReadinessSmokeCommand_ReturnsFailureForInvalidSecret()
    {
        var output = new StringWriter();

        var exitCode = await AuthReadinessSmokeCommand.ExecuteAsync(
            CreateAuthSmokeConfiguration(),
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["api-key"] = "fixture-key",
                ["api-key-owner"] = "fixture-key",
                ["api-secret"] = "!!!not-base64!!!",
                ["api-passphrase"] = "fixture-passphrase"
            }),
            output,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Auth readiness status: Error", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClobAuthenticatedReadSmokeCommand_SendsReadOnlyRequestAndPrintsNoSecrets()
    {
        var output = new StringWriter();
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"count":0,"data":[]}""")
        });
        using var httpClient = new HttpClient(handler);

        var exitCode = await ClobAuthenticatedReadSmokeCommand.ExecuteAsync(
            CreateAuthSmokeConfiguration(),
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["api-key"] = "fixture-key",
                ["api-secret"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                ["api-passphrase"] = "fixture-passphrase"
            }),
            httpClient,
            output,
            CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/trades", request.RequestUri?.AbsolutePath);
        Assert.True(request.Headers.Contains(PolymarketAuthHeaderFactory.PolyAddress));
        Assert.True(request.Headers.Contains(PolymarketAuthHeaderFactory.PolySignature));
        Assert.True(request.Headers.Contains(PolymarketAuthHeaderFactory.PolyTimestamp));
        Assert.True(request.Headers.Contains(PolymarketAuthHeaderFactory.PolyApiKey));
        Assert.True(request.Headers.Contains(PolymarketAuthHeaderFactory.PolyPassphrase));
        Assert.Contains("CLOB authenticated read smoke status: OK", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-passphrase", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClobAuthenticatedReadSmokeCommand_DoesNotPrintFailureBody()
    {
        var output = new StringWriter();
        using var httpClient = new HttpClient(new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":"fixture-key fixture-passphrase"}""")
        }));

        var exitCode = await ClobAuthenticatedReadSmokeCommand.ExecuteAsync(
            CreateAuthSmokeConfiguration(),
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["api-key"] = "fixture-key",
                ["api-secret"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                ["api-passphrase"] = "fixture-passphrase"
            }),
            httpClient,
            output,
            CancellationToken.None);

        var text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("CLOB authenticated read smoke status: Error", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-passphrase", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClobCancelAllSmokeCommand_SendsCancelAllAndPrintsNoSecrets()
    {
        var output = new StringWriter();
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"canceled":[],"not_canceled":{}}""")
        });
        using var httpClient = new HttpClient(handler);

        var exitCode = await ClobCancelAllSmokeCommand.ExecuteAsync(
            CreateAuthSmokeConfiguration(),
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["api-key"] = "fixture-key",
                ["api-secret"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                ["api-passphrase"] = "fixture-passphrase"
            }),
            httpClient,
            output,
            CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/cancel-all", request.RequestUri?.AbsolutePath);
        Assert.True(request.Headers.Contains(PolymarketAuthHeaderFactory.PolyAddress));
        Assert.True(request.Headers.Contains(PolymarketAuthHeaderFactory.PolySignature));
        Assert.True(request.Headers.Contains(PolymarketAuthHeaderFactory.PolyTimestamp));
        Assert.True(request.Headers.Contains(PolymarketAuthHeaderFactory.PolyApiKey));
        Assert.True(request.Headers.Contains(PolymarketAuthHeaderFactory.PolyPassphrase));
        Assert.Contains("CLOB cancel-all smoke status: OK", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-passphrase", text, StringComparison.Ordinal);
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
    public void OrderAmountCalculator_RoundsLimitAmountsLikeOfficialSdk()
    {
        var builder = new ClobV2OrderBuilder(new OrderAmountCalculator());

        var errors = builder.Validate(FixedOrderRequest() with
        {
            Price = 0.01m,
            SizeShares = 1.123456m,
            MinOrderSize = 1m
        });
        var order = builder.Build(FixedOrderRequest() with
        {
            Price = 0.01m,
            SizeShares = 1.123456m,
            MinOrderSize = 1m
        });

        Assert.Empty(errors);
        Assert.Equal("11200", order.MakerAmount);
        Assert.Equal("1120000", order.TakerAmount);
    }

    [Fact]
    public void OrderBuilder_GeneratesJsonSafeSaltLikeOfficialSdk()
    {
        var order = new ClobV2OrderBuilder(new OrderAmountCalculator()).Build(FixedOrderRequest() with
        {
            Salt = null
        });

        var salt = long.Parse(order.Salt, CultureInfo.InvariantCulture);

        Assert.InRange(salt, 1L, 9_007_199_254_740_991L);
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
        Assert.Contains(@"""timestamp"":""1777464000000""", result.PayloadJson, StringComparison.Ordinal);
        Assert.Contains(@"""metadata"":""0x0000000000000000000000000000000000000000000000000000000000000000""", result.PayloadJson, StringComparison.Ordinal);
        Assert.Contains(@"""builder"":""0x0000000000000000000000000000000000000000000000000000000000000000""", result.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain(@"""taker"":", result.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain(@"""nonce"":", result.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain(@"""feeRateBps"":", result.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderSigner_Poly1271UsesDepositWalletSignerAndWrappedSignature()
    {
        var signer = new ClobV2OrderSigner();
        var privateKey = DeterministicUnfundedTestPrivateKey;
        var eoaAddress = signer.GetAddress(privateKey);
        const string depositWalletAddress = "0x49d6fEE74b294951668a4160f450Ff1C92E94cEC";

        var order = new ClobV2OrderBuilder(new OrderAmountCalculator()).Build(FixedOrderRequest() with
        {
            MakerAddress = depositWalletAddress,
            SignerAddress = eoaAddress,
            SignatureType = ClobV2SignatureType.POLY_1271
        });
        var signature = signer.Sign(order, privateKey);
        var payload = new ClobV2OrderPayloadSerializer().Serialize(order, signature, "fixture-owner");

        Assert.Equal(depositWalletAddress, order.Maker);
        Assert.Equal(depositWalletAddress, order.Signer);
        Assert.StartsWith("0x", signature, StringComparison.Ordinal);
        Assert.True(signature.Length > 132);
        Assert.True(signer.Verify(order, signature, eoaAddress));
        Assert.Contains(@"""signer"":""0x49d6fEE74b294951668a4160f450Ff1C92E94cEC""", payload, StringComparison.Ordinal);
        Assert.Contains(@"""signatureType"":3", payload, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderSigner_Poly1271MatchesOfficialSdkVector()
    {
        const string officialSdkPrivateKey =
            "0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80";
        const string depositWalletAddress = "0x1111111111111111111111111111111111111111";
        const string expectedSignature =
            "0xa3a093c83b6c20c83355c16ce94c92e6e9fcbdeb840618cc74f6c57a42ad145b" +
            "2b98db73d2c73cbf1f2b6af288566ae81960ddbc3a13921027358a8bff3be6ff1c" +
            "a440cbd865bc0c6243d7a8df9a8bf48a8827b0a4abbb61c30e96d305423af148" +
            "d23d42d3ad94e65d78258cecaf8dcbaddac0f73dc085040f2c12bb595dd83804" +
            "4f726465722875696e743235362073616c742c61646472657373206d616b65722c" +
            "61646472657373207369676e65722c75696e7432353620746f6b656e49642c75" +
            "696e74323536206d616b6572416d6f756e742c75696e743235362074616b6572" +
            "416d6f756e742c75696e743820736964652c75696e7438207369676e61747572" +
            "65547970652c75696e743235362074696d657374616d702c6279746573333220" +
            "6d657461646174612c62797465733332206275696c6465722900ba";
        var order = new ClobV2Order(
            "479249096354",
            depositWalletAddress,
            depositWalletAddress,
            "1234",
            "100000000",
            "50000000",
            TradeSide.Buy,
            ClobV2SignatureType.POLY_1271,
            "1710000000000",
            ClobV2ExchangeContracts.ZeroBytes32,
            ClobV2ExchangeContracts.ZeroBytes32,
            "0",
            ClobV2OrderType.GTC,
            PostOnly: false,
            DeferExec: false,
            NegativeRisk: false);

        var signature = new ClobV2OrderSigner().Sign(order, officialSdkPrivateKey, chainId: 80002);

        Assert.Equal(expectedSignature, signature);
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
        var httpLogSink = new CapturingHttpLogSink();
        var client = CreateLiveClient(handler, new Dictionary<string, string>
        {
            ["api-key"] = "fixture-key",
            ["api-key-owner"] = "fixture-owner",
            ["api-secret"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            ["api-passphrase"] = "fixture-passphrase",
            ["live-private-key"] = privateKey
        }, httpLogSink);

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
        Assert.Contains(@"""owner"":""fixture-key""", body, StringComparison.Ordinal);
        Assert.Contains(@"""orderType"":""GTD""", body, StringComparison.Ordinal);
        Assert.Contains(@"""postOnly"":true", body, StringComparison.Ordinal);
        Assert.Contains(@"""expiration"":""1777636800""", body, StringComparison.Ordinal);
        Assert.Contains(@"""signature"":""0x", body, StringComparison.Ordinal);
        Assert.DoesNotContain(@"""signature"":""0x", result.RedactedRequestJson, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.RedactedRequestJson, StringComparison.Ordinal);

        var httpLog = Assert.Single(httpLogSink.Entries);
        Assert.Equal(nameof(PolymarketTradingClient), httpLog.Component);
        Assert.Equal("PostOrder", httpLog.Operation);
        Assert.Equal("POST", httpLog.HttpMethod);
        Assert.Equal("https://clob.polymarket.com/order", httpLog.RequestUrl);
        Assert.Equal(200, httpLog.StatusCode);
        Assert.True(httpLog.Succeeded);
        Assert.Contains("orderID", httpLog.ResponseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClobMinimumLiveOrderSmokeCommand_RefusesWithoutSubmitFlag()
    {
        var output = new StringWriter();

        var exitCode = await ClobMinimumLiveOrderSmokeCommand.ExecuteAsync(
            CreateLiveSmokeConfiguration(),
            new FakeSecretProvider(CreateAuthSecrets()),
            new FakeGammaClient([]),
            new FakeClobPublicClient(CreateOrderBook("token-1")),
            new FakeGeoClient(false),
            new FakeTradingClient(),
            output,
            submit: false,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("--submit", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClobMinimumLiveOrderSmokeCommand_SubmitsMinimumGtcAndCancelsOpenResidue()
    {
        const string tokenId = "123456789012345678901234567890";
        var trading = new FakeTradingClient
        {
            Placement = new LiveOrderPlacementResult(
                true,
                "0xorder123",
                "live",
                null,
                "2500000",
                "5000000",
                """{"success":true,"orderID":"0xorder123","status":"live"}""",
                "{}")
        };
        var output = new StringWriter();

        var exitCode = await ClobMinimumLiveOrderSmokeCommand.ExecuteAsync(
            CreateLiveSmokeConfiguration(),
            new FakeSecretProvider(CreateAuthSecrets()),
            new FakeGammaClient([CreateGammaMarket(tokenId)]),
            new FakeClobPublicClient(CreateOrderBook(tokenId)),
            new FakeGeoClient(false),
            trading,
            output,
            submit: true,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(trading.LastRequest);
        Assert.Equal(tokenId, trading.LastRequest!.TokenId);
        Assert.Equal(ClobV2OrderType.GTC, trading.LastRequest.OrderType);
        Assert.False(trading.LastRequest.PostOnly);
        Assert.Equal(0.50m, trading.LastRequest.Price);
        Assert.Equal(5.00m, trading.LastRequest.SizeShares);
        Assert.Equal(1, trading.CancelOrderCalls);
        Assert.Contains("CLOB minimal live order smoke status: OK", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClobMinimumLiveOrderSmokeCommand_RefusesFutureBtcMarket()
    {
        const string tokenId = "123456789012345678901234567890";
        var futureStartUtc = DateTimeOffset.UtcNow.AddDays(1);
        var trading = new FakeTradingClient();
        var output = new StringWriter();

        var exitCode = await ClobMinimumLiveOrderSmokeCommand.ExecuteAsync(
            CreateLiveSmokeConfiguration(),
            new FakeSecretProvider(CreateAuthSecrets()),
            new FakeGammaClient([CreateGammaMarket(tokenId, futureStartUtc)]),
            new FakeClobPublicClient(CreateOrderBook(tokenId)),
            new FakeGeoClient(false),
            trading,
            output,
            submit: true,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Null(trading.LastRequest);
        Assert.Contains("CLOB minimal live order smoke status: NoCandidate", output.ToString(), StringComparison.Ordinal);
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

    private static AppConfiguration CreateAuthSmokeConfiguration()
    {
        return new AppConfiguration
        {
            Bot = new BotOptions
            {
                Mode = BotMode.Paper,
                EnableLiveTrading = false
            },
            Polymarket = new PolymarketOptions
            {
                ClobBaseUrl = "https://clob.polymarket.com",
                TimeoutSeconds = 30
            },
            PolymarketAuth = new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "0x1111111111111111111111111111111111111111",
                FunderAddress = "0x2222222222222222222222222222222222222222",
                ApiKeyName = "api-key",
                ApiKeyOwnerName = "api-key-owner",
                ApiSecretName = "api-secret",
                ApiPassphraseName = "api-passphrase"
            }
        };
    }

    private static AppConfiguration CreateLiveSmokeConfiguration()
    {
        var signerAddress = new ClobV2OrderSigner().GetAddress(DeterministicUnfundedTestPrivateKey);
        return new AppConfiguration
        {
            Bot = new BotOptions
            {
                Mode = BotMode.Live,
                EnableLiveTrading = true
            },
            Polymarket = new PolymarketOptions
            {
                ClobBaseUrl = "https://clob.polymarket.com",
                GammaBaseUrl = "https://gamma-api.polymarket.com",
                GeoblockUrl = "https://polymarket.com/api/geoblock?source=api",
                TimeoutSeconds = 30
            },
            PolymarketAuth = new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = signerAddress,
                FunderAddress = "0x49d6fEE74b294951668a4160f450Ff1C92E94cEC",
                SignatureType = "POLY_1271",
                ApiKeyName = "api-key",
                ApiKeyOwnerName = "api-key-owner",
                ApiSecretName = "api-secret",
                ApiPassphraseName = "api-passphrase",
                OrderSigningPrivateKeyName = "live-private-key"
            },
            LiveTrading = new LiveTradingOptions
            {
                MaxOrderNotionalUsd = 5m,
                MaxClockDriftSeconds = 5
            }
        };
    }

    private static IReadOnlyDictionary<string, string> CreateAuthSecrets()
    {
        return new Dictionary<string, string>
        {
            ["api-key"] = "fixture-key",
            ["api-key-owner"] = "fixture-key",
            ["api-secret"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            ["api-passphrase"] = "fixture-passphrase",
            ["live-private-key"] = DeterministicUnfundedTestPrivateKey
        };
    }

    private static PolymarketGammaMarket CreateGammaMarket(
        string tokenId,
        DateTimeOffset? windowStartUtc = null)
    {
        var startUtc = windowStartUtc ?? DateTimeOffset.UtcNow;
        return new PolymarketGammaMarket(
            "market-1",
            "condition-1",
            "question-1",
            "btc-updown-5m-" + startUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            "BTC Up or Down 5m",
            null,
            null,
            null,
            "btc-up-or-down-5m",
            "Crypto",
            Active: true,
            Closed: false,
            Archived: false,
            Restricted: false,
            AcceptingOrders: true,
            EnableOrderBook: true,
            NegativeRisk: false,
            Liquidity: null,
            LiquidityClob: null,
            Volume: null,
            Volume24Hr: null,
            BestBid: null,
            BestAsk: null,
            Spread: null,
            CreatedAtUtc: null,
            UpdatedAtUtc: null,
            StartDateUtc: null,
            EndDateUtc: startUtc.AddMinutes(5),
            EventStartTimeUtc: startUtc,
            Outcomes: ["Up"],
            ClobTokenIds: [tokenId],
            RawJson: """{"outcomePrices":["0.50"]}""",
            FetchedAtUtc: DateTimeOffset.UtcNow,
            OrderMinSize: 5m,
            OrderPriceMinTickSize: 0.01m);
    }

    private static OrderBookSnapshot CreateOrderBook(string tokenId)
    {
        return new OrderBookSnapshot(
            tokenId,
            Bids: [new OrderBookLevel(0.49m, 100m)],
            Asks: [new OrderBookLevel(0.50m, 100m)],
            SnapshotAtUtc: DateTimeOffset.UtcNow,
            ConditionId: "condition-1",
            MinOrderSize: 5m,
            TickSize: 0.01m,
            NegativeRisk: false);
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
        IReadOnlyDictionary<string, string> secrets,
        IPolymarketHttpLogSink? httpLogSink = null)
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
            new CapturingApiErrorSink(),
            httpLogSink);
    }

    private sealed class FakeSecretProvider(IReadOnlyDictionary<string, string>? values = null) : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string name, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(values is not null && values.TryGetValue(name, out var value) ? value : null);
        }
    }

    private sealed class FakeGammaClient(IReadOnlyList<PolymarketGammaMarket> markets) : IPolymarketGammaClient
    {
        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(
            int limit = 500,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(offset == 0 ? markets : []);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(
            string tokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>([]);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(
            string conditionId,
            string requestedTokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>([]);
        }

        public Task<string?> GetEventCategoryAsync(
            string eventId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class FakeClobPublicClient(OrderBookSnapshot orderBook) : IPolymarketClobPublicClient
    {
        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OrderBookSnapshot?>(orderBook with { AssetId = assetId });
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(0.50m);
        }

        public Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(0.01m);
        }

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(
            string tokenId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PolymarketClobMarketByToken?>(null);
        }
    }

    private sealed class FakeGeoClient(bool blocked) : IPolymarketGeoClient
    {
        public Task<GeoblockStatus> GetGeoblockStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GeoblockStatus(blocked, "127.0.0.1", "US", null));
        }
    }

    private sealed class FakeTradingClient : IPolymarketTradingClient
    {
        public LiveOrderPlacementResult Placement { get; init; } = new(
            false,
            null,
            "error",
            "not configured",
            null,
            null,
            "{}",
            "{}");

        public ClobV2OrderRequest? LastRequest { get; private set; }

        public int CancelOrderCalls { get; private set; }

        public Task<ClobV2DryRunOrderResult> PrepareDryRunOrderAsync(ClobV2OrderRequest request, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<LiveOrderPlacementResult> PlaceLiveOrderAsync(ClobV2OrderRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(Placement);
        }

        public Task<LiveOrderCancellationResult> CancelOrderAsync(string orderId, CancellationToken ct)
        {
            CancelOrderCalls++;
            return Task.FromResult(new LiveOrderCancellationResult(true, [orderId], new Dictionary<string, string>(), "{}"));
        }

        public Task<LiveOrderCancellationResult> CancelAllOrdersAsync(CancellationToken ct)
        {
            return Task.FromResult(new LiveOrderCancellationResult(true, [], new Dictionary<string, string>(), "{}"));
        }

        public Task<LiveOrderStatusResult?> GetLiveOrderStatusAsync(string orderId, CancellationToken ct)
        {
            return Task.FromResult<LiveOrderStatusResult?>(null);
        }
    }

    private sealed class CapturingApiErrorSink : IPolymarketApiErrorSink
    {
        public Task RecordAsync(ApiError error, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingHttpLogSink : IPolymarketHttpLogSink
    {
        public List<PolymarketHttpLogEntry> Entries { get; } = [];

        public Task RecordAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
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
