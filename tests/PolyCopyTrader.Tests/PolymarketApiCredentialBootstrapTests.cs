using System.Net;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Startup;

namespace PolyCopyTrader.Tests;

public sealed class PolymarketApiCredentialBootstrapTests
{
    [Fact]
    public void ClobL1AuthSigner_SignsAndVerifiesAuthMessage()
    {
        var signer = new ClobL1AuthSigner();
        var address = signer.GetAddress(DeterministicUnfundedTestPrivateKey);

        var signature = signer.Sign(address, "1800000000", 0, DeterministicUnfundedTestPrivateKey);

        Assert.StartsWith("0x", signature, StringComparison.Ordinal);
        Assert.True(signer.Verify(address, "1800000000", 0, signature));
        Assert.False(signer.Verify("0x1111111111111111111111111111111111111111", "1800000000", 0, signature));
    }

    [Fact]
    public async Task BootstrapClient_DerivesCredentialsWithL1Headers()
    {
        var signer = new ClobL1AuthSigner();
        var signingAddress = signer.GetAddress(DeterministicUnfundedTestPrivateKey);
        var handler = new CapturingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/time")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("1800000000")
                };
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/auth/derive-api-key")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"apiKey":"fixture-api-key","secret":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=","passphrase":"fixture-passphrase"}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = CreateClient(handler, signingAddress);

        var result = await client.CreateOrDeriveAsync(DeterministicUnfundedTestPrivateKey, CancellationToken.None);

        Assert.Equal(PolymarketApiCredentialBootstrapSource.Derived, result.Source);
        Assert.Equal("fixture-api-key", result.Credentials.ApiKey);
        Assert.Equal("1800000000", result.Timestamp);

        var derive = handler.Requests.Single(request => request.RequestUri?.AbsolutePath == "/auth/derive-api-key");
        Assert.True(derive.Headers.Contains(PolymarketL1AuthHeaderFactory.PolyAddress));
        Assert.True(derive.Headers.Contains(PolymarketL1AuthHeaderFactory.PolySignature));
        Assert.True(derive.Headers.Contains(PolymarketL1AuthHeaderFactory.PolyTimestamp));
        Assert.True(derive.Headers.Contains(PolymarketL1AuthHeaderFactory.PolyNonce));
        Assert.Equal(signingAddress, derive.Headers.GetValues(PolymarketL1AuthHeaderFactory.PolyAddress).Single());
        Assert.Equal("1800000000", derive.Headers.GetValues(PolymarketL1AuthHeaderFactory.PolyTimestamp).Single());
        Assert.Equal("0", derive.Headers.GetValues(PolymarketL1AuthHeaderFactory.PolyNonce).Single());
    }

    [Fact]
    public async Task BootstrapClient_CreatesCredentialsWhenDeriveIsMissing()
    {
        var signer = new ClobL1AuthSigner();
        var signingAddress = signer.GetAddress(DeterministicUnfundedTestPrivateKey);
        var handler = new CapturingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/time")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("1800000000")
                };
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/auth/derive-api-key")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"error":"not found"}""")
                };
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/auth/api-key")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"key":"created-api-key","secret":"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=","passphrase":"created-passphrase"}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = CreateClient(handler, signingAddress);

        var result = await client.CreateOrDeriveAsync(DeterministicUnfundedTestPrivateKey, CancellationToken.None);

        Assert.Equal(PolymarketApiCredentialBootstrapSource.Created, result.Source);
        Assert.Equal("created-api-key", result.Credentials.ApiKey);
        Assert.Contains(handler.Requests, request => request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/auth/api-key");
    }

    [Fact]
    public async Task BootstrapClient_CreatesCredentialsWhenDeriveReturnsBadRequest()
    {
        var signer = new ClobL1AuthSigner();
        var signingAddress = signer.GetAddress(DeterministicUnfundedTestPrivateKey);
        var handler = new CapturingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/time")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("1800000000")
                };
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/auth/derive-api-key")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""{"error":"no api key was found"}""")
                };
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/auth/api-key")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"key":"created-api-key","secret":"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=","passphrase":"created-passphrase"}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = CreateClient(handler, signingAddress);

        var result = await client.CreateOrDeriveAsync(DeterministicUnfundedTestPrivateKey, CancellationToken.None);

        Assert.Equal(PolymarketApiCredentialBootstrapSource.Created, result.Source);
        Assert.Equal("created-api-key", result.Credentials.ApiKey);
        Assert.Contains(handler.Requests, request => request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/auth/api-key");
    }

    [Fact]
    public async Task BootstrapCommand_StoresCredentialsWithoutPrintingSecrets()
    {
        var signer = new ClobL1AuthSigner();
        var signingAddress = signer.GetAddress(DeterministicUnfundedTestPrivateKey);
        var output = new StringWriter();
        var secretProvider = new FakeSecretProvider(new Dictionary<string, string>
        {
            ["live-private-key"] = DeterministicUnfundedTestPrivateKey
        });
        var secretWriter = new CapturingSecretWriter();
        using var httpClient = new HttpClient(new CapturingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/time")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("1800000000")
                };
            }

            if (request.RequestUri?.AbsolutePath == "/auth/derive-api-key")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"apiKey":"fixture-api-key","secret":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=","passphrase":"fixture-passphrase"}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        var exitCode = await PolymarketApiCredentialBootstrapCommand.ExecuteAsync(
            CreateConfiguration(signingAddress),
            secretProvider,
            secretWriter,
            httpClient,
            output,
            CancellationToken.None);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal("fixture-api-key", secretWriter.Values["api-key"]);
        Assert.Equal("fixture-api-key", secretWriter.Values["api-key-owner"]);
        Assert.Equal("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", secretWriter.Values["api-secret"]);
        Assert.Equal("fixture-passphrase", secretWriter.Values["api-passphrase"]);
        Assert.Contains("Bootstrap status: Stored", text, StringComparison.Ordinal);
        Assert.DoesNotContain(DeterministicUnfundedTestPrivateKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-api-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-passphrase", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BootstrapCommand_RefusesWhenLiveTradingIsEnabled()
    {
        var signer = new ClobL1AuthSigner();
        var signingAddress = signer.GetAddress(DeterministicUnfundedTestPrivateKey);
        var output = new StringWriter();

        var exitCode = await PolymarketApiCredentialBootstrapCommand.ExecuteAsync(
            CreateConfiguration(signingAddress, liveTradingEnabled: true),
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["live-private-key"] = DeterministicUnfundedTestPrivateKey
            }),
            new CapturingSecretWriter(),
            new HttpClient(new RejectingHttpMessageHandler()),
            output,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bootstrap status: Refused", output.ToString(), StringComparison.Ordinal);
    }

    private const string DeterministicUnfundedTestPrivateKey =
        "0x0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static PolymarketApiCredentialBootstrapClient CreateClient(
        HttpMessageHandler handler,
        string signingAddress)
    {
        var signer = new ClobL1AuthSigner();
        return new PolymarketApiCredentialBootstrapClient(
            new HttpClient(handler),
            new PolymarketOptions
            {
                ClobBaseUrl = "https://clob.polymarket.com",
                TimeoutSeconds = 30
            },
            new PolymarketAuthOptions
            {
                SigningAddress = signingAddress,
                ChainId = 137
            },
            signer,
            new PolymarketL1AuthHeaderFactory(signer));
    }

    private static AppConfiguration CreateConfiguration(
        string signingAddress,
        bool liveTradingEnabled = false)
    {
        return new AppConfiguration
        {
            Bot = new BotOptions
            {
                Mode = liveTradingEnabled ? BotMode.Live : BotMode.Paper,
                EnableLiveTrading = liveTradingEnabled
            },
            Polymarket = new PolymarketOptions
            {
                ClobBaseUrl = "https://clob.polymarket.com",
                TimeoutSeconds = 30
            },
            PolymarketAuth = new PolymarketAuthOptions
            {
                SecretProvider = "CredentialManager",
                SigningAddress = signingAddress,
                FunderAddress = "0x2222222222222222222222222222222222222222",
                SignatureType = "POLY_GNOSIS_SAFE",
                OrderSigningPrivateKeyName = "live-private-key",
                ApiKeyName = "api-key",
                ApiKeyOwnerName = "api-key-owner",
                ApiSecretName = "api-secret",
                ApiPassphraseName = "api-passphrase"
            }
        };
    }

    private sealed class FakeSecretProvider(IReadOnlyDictionary<string, string> values) : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string name, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(values.TryGetValue(name, out var value) ? value : null);
        }
    }

    private sealed class CapturingSecretWriter : ISecretWriter
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public Task SetSecretAsync(string name, string value, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Values[name] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class RejectingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Bootstrap refusal tests must not send HTTP requests.");
        }
    }
}
