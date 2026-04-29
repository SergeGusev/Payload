using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket;

public static class PolymarketCertificatePinning
{
    public const string Sha256SubjectPublicKeyInfoPrefix = "sha256/";

    public static bool HasPins(PolymarketOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.CertificatePins?.Any(entry => entry.Value is { Count: > 0 }) == true;
    }

    public static CertificatePinValidationResult ValidateServerCertificate(
        Uri? requestUri,
        X509Certificate? certificate,
        SslPolicyErrors sslPolicyErrors,
        PolymarketOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (requestUri is null)
        {
            return new CertificatePinValidationResult(false, "Request URI is not available.", null);
        }

        if (!TryGetPinsForHost(options, requestUri.Host, out var pins))
        {
            return sslPolicyErrors == SslPolicyErrors.None
                ? new CertificatePinValidationResult(true, "No certificate pin is configured for this host; standard TLS validation passed.", null)
                : new CertificatePinValidationResult(false, $"No certificate pin is configured for this host and standard TLS validation failed: {sslPolicyErrors}.", null);
        }

        if (pins.Count == 0)
        {
            return new CertificatePinValidationResult(false, $"No non-empty certificate pins are configured for host '{requestUri.Host}'.", null);
        }

        if (certificate is null)
        {
            return new CertificatePinValidationResult(false, "Server certificate is not available.", null);
        }

        if (certificate is X509Certificate2 certificate2)
        {
            return ValidatePinnedCertificate(requestUri.Host, certificate2, pins);
        }

        using var convertedCertificate = new X509Certificate2(certificate);
        return ValidatePinnedCertificate(requestUri.Host, convertedCertificate, pins);
    }

    public static string ComputeSubjectPublicKeyInfoSha256Pin(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var subjectPublicKeyInfo = certificate.PublicKey.ExportSubjectPublicKeyInfo();
        var hash = SHA256.HashData(subjectPublicKeyInfo);
        return Sha256SubjectPublicKeyInfoPrefix + Convert.ToBase64String(hash);
    }

    private static CertificatePinValidationResult ValidatePinnedCertificate(
        string host,
        X509Certificate2 certificate,
        IReadOnlyCollection<string> pins)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        if (nowUtc < certificate.NotBefore.ToUniversalTime() ||
            nowUtc > certificate.NotAfter.ToUniversalTime())
        {
            return new CertificatePinValidationResult(false, $"Server certificate for host '{host}' is outside its validity window.", null);
        }

        var presentedPin = ComputeSubjectPublicKeyInfoSha256Pin(certificate);
        var matched = pins.Any(pin => string.Equals(pin?.Trim(), presentedPin, StringComparison.Ordinal));
        return matched
            ? new CertificatePinValidationResult(true, $"Server certificate SPKI pin matched host '{host}'.", presentedPin)
            : new CertificatePinValidationResult(false, $"Server certificate SPKI pin did not match host '{host}'.", presentedPin);
    }

    private static bool TryGetPinsForHost(
        PolymarketOptions options,
        string host,
        out IReadOnlyCollection<string> pins)
    {
        if (options.CertificatePins is null)
        {
            pins = [];
            return false;
        }

        foreach (var entry in options.CertificatePins)
        {
            if (string.Equals(entry.Key, host, StringComparison.OrdinalIgnoreCase))
            {
                pins = entry.Value ?? [];
                return true;
            }
        }

        pins = [];
        return false;
    }
}

public sealed record CertificatePinValidationResult(
    bool Accepted,
    string Message,
    string? PresentedPin);
