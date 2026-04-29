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
}
