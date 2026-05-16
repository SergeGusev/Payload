using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;

namespace PolyCopyTrader.Tests;

public sealed class CertificatePinningTests
{
    [Fact]
    public void ValidateServerCertificate_UsesStandardTls_WhenHostHasNoPin()
    {
        var options = new PolymarketOptions();

        var accepted = PolymarketCertificatePinning.ValidateServerCertificate(
            new Uri("https://data-api.polymarket.com"),
            null,
            SslPolicyErrors.None,
            options);
        var rejected = PolymarketCertificatePinning.ValidateServerCertificate(
            new Uri("https://data-api.polymarket.com"),
            null,
            SslPolicyErrors.RemoteCertificateChainErrors,
            options);

        Assert.True(accepted.Accepted);
        Assert.False(rejected.Accepted);
    }

    [Fact]
    public void ValidateServerCertificate_AcceptsMatchingPin_WhenStandardTlsFails()
    {
        using var certificate = CreateCertificate();
        var pin = PolymarketCertificatePinning.ComputeSubjectPublicKeyInfoSha256Pin(certificate);
        var options = new PolymarketOptions
        {
            CertificatePins = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["data-api.polymarket.com"] = [pin]
            }
        };

        var result = PolymarketCertificatePinning.ValidateServerCertificate(
            new Uri("https://data-api.polymarket.com"),
            certificate,
            SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch,
            options);

        Assert.True(result.Accepted);
        Assert.Equal(pin, result.PresentedPin);
    }

    [Fact]
    public void ValidateServerCertificate_RejectsMismatchedPin()
    {
        using var certificate = CreateCertificate();
        var options = new PolymarketOptions
        {
            CertificatePins = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["data-api.polymarket.com"] = ["sha256/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="]
            }
        };

        var result = PolymarketCertificatePinning.ValidateServerCertificate(
            new Uri("https://data-api.polymarket.com"),
            certificate,
            SslPolicyErrors.None,
            options);

        Assert.False(result.Accepted);
        Assert.NotNull(result.PresentedPin);
    }

    [Fact]
    public void ComputeSubjectPublicKeyInfoSha256Pin_ReturnsExpectedFormat()
    {
        using var certificate = CreateCertificate();

        var pin = PolymarketCertificatePinning.ComputeSubjectPublicKeyInfoSha256Pin(certificate);

        Assert.StartsWith(PolymarketCertificatePinning.Sha256SubjectPublicKeyInfoPrefix, pin, StringComparison.Ordinal);
        var hash = Convert.FromBase64String(pin[PolymarketCertificatePinning.Sha256SubjectPublicKeyInfoPrefix.Length..]);
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public async Task CertificateCheckService_ReportsMatchedConfiguredPin()
    {
        using var certificate = CreateCertificate();
        var pin = PolymarketCertificatePinning.ComputeSubjectPublicKeyInfoSha256Pin(certificate);
        var service = new PolymarketCertificateCheckService(
            CreatePinnedOptions(pin),
            new MarketDataWebSocketOptions(),
            (_, _) => Task.FromResult(new CertificateProbeResult(new X509Certificate2(certificate), SslPolicyErrors.None)));

        var results = await service.CheckAsync(CancellationToken.None);

        var dataApi = Assert.Single(results, item => item.EndpointName == "Data API");
        Assert.Equal("OK", dataApi.Status);
        Assert.Equal("Matched", dataApi.PinStatus);
        Assert.Equal(pin, dataApi.PresentedPin);
        Assert.DoesNotContain("BEGIN", dataApi.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CertificateCheckService_ReportsMismatchedConfiguredPin()
    {
        using var certificate = CreateCertificate();
        var service = new PolymarketCertificateCheckService(
            CreatePinnedOptions("sha256/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="),
            new MarketDataWebSocketOptions(),
            (_, _) => Task.FromResult(new CertificateProbeResult(new X509Certificate2(certificate), SslPolicyErrors.None)));

        var results = await service.CheckAsync(CancellationToken.None);

        var dataApi = Assert.Single(results, item => item.EndpointName == "Data API");
        Assert.Equal("Error", dataApi.Status);
        Assert.Equal("Mismatch", dataApi.PinStatus);
        Assert.NotEmpty(dataApi.PresentedPin);
    }

    [Fact]
    public async Task CertificateCheckService_WarnsWhenMatchingPinHasStandardTlsErrors()
    {
        using var certificate = CreateCertificate();
        var pin = PolymarketCertificatePinning.ComputeSubjectPublicKeyInfoSha256Pin(certificate);
        var service = new PolymarketCertificateCheckService(
            CreatePinnedOptions(pin),
            new MarketDataWebSocketOptions(),
            (_, _) => Task.FromResult(new CertificateProbeResult(
                new X509Certificate2(certificate),
                SslPolicyErrors.RemoteCertificateChainErrors)));

        var results = await service.CheckAsync(CancellationToken.None);

        var dataApi = Assert.Single(results, item => item.EndpointName == "Data API");
        Assert.Equal("Warning", dataApi.Status);
        Assert.Equal("Matched", dataApi.PinStatus);
        Assert.Contains(nameof(SslPolicyErrors.RemoteCertificateChainErrors), dataApi.Details, StringComparison.Ordinal);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=data-api.polymarket.com",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    private static PolymarketOptions CreatePinnedOptions(string pin)
    {
        return new PolymarketOptions
        {
            DataApiBaseUrl = "https://data-api.polymarket.com",
            CertificatePins = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["data-api.polymarket.com"] = [pin]
            }
        };
    }
}
