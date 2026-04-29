using PolyCopyTrader.Domain.Configuration;
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
    public void TradingClient_IsPlaceholderWithoutLiveOrderMethods()
    {
        Assert.Empty(typeof(IPolymarketTradingClient).GetMethods());
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
                ApiSecretName = "api-secret",
                ApiPassphraseName = "api-passphrase"
            },
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["api-key"] = "fixture-key",
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
                ApiSecretName = "api-secret",
                ApiPassphraseName = "api-passphrase"
            },
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["api-key"] = "fixture-key",
                ["api-secret"] = "!!!not-base64!!!",
                ["api-passphrase"] = "fixture-passphrase"
            }),
            new PolymarketL2HmacSigner());

        var status = await service.GetReadinessAsync(CancellationToken.None);

        Assert.Equal("Error", status.State);
        Assert.Contains(status.MissingRequirements, item => item.Contains("base64", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeSecretProvider(IReadOnlyDictionary<string, string>? values = null) : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string name, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(values is not null && values.TryGetValue(name, out var value) ? value : null);
        }
    }
}
