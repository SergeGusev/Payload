using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket;

public sealed class PolymarketCertificateCheckService
{
    private readonly PolymarketOptions polymarketOptions;
    private readonly MarketDataWebSocketOptions marketDataWebSocketOptions;
    private readonly Func<Uri, CancellationToken, Task<CertificateProbeResult>> probeAsync;

    public PolymarketCertificateCheckService(
        PolymarketOptions polymarketOptions,
        MarketDataWebSocketOptions marketDataWebSocketOptions)
        : this(polymarketOptions, marketDataWebSocketOptions, ProbeCertificateAsync)
    {
    }

    public PolymarketCertificateCheckService(
        PolymarketOptions polymarketOptions,
        MarketDataWebSocketOptions marketDataWebSocketOptions,
        Func<Uri, CancellationToken, Task<CertificateProbeResult>> probeAsync)
    {
        this.polymarketOptions = polymarketOptions;
        this.marketDataWebSocketOptions = marketDataWebSocketOptions;
        this.probeAsync = probeAsync;
    }

    public async Task<IReadOnlyList<PolymarketCertificateCheckResult>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<PolymarketCertificateCheckResult>();
        foreach (var endpoint in BuildEndpoints())
        {
            results.Add(await CheckEndpointAsync(endpoint, cancellationToken));
        }

        return results;
    }

    private async Task<PolymarketCertificateCheckResult> CheckEndpointAsync(
        CertificateEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        var checkedAtUtc = DateTimeOffset.UtcNow;
        if (!Uri.TryCreate(endpoint.Url, UriKind.Absolute, out var uri) ||
            !IsSupportedScheme(uri.Scheme))
        {
            return Error(
                endpoint,
                string.Empty,
                checkedAtUtc,
                "Invalid endpoint URI.",
                "Endpoint must be https or wss.");
        }

        try
        {
            var probe = await probeAsync(uri, cancellationToken);
            using var certificate = probe.Certificate;
            var tlsOk = probe.SslPolicyErrors == SslPolicyErrors.None;
            var pins = PinsForHost(uri.Host);
            var hasPins = pins.Count > 0;
            var validation = PolymarketCertificatePinning.ValidateServerCertificate(
                uri,
                certificate,
                probe.SslPolicyErrors,
                polymarketOptions);
            var status = hasPins
                ? validation.Accepted
                    ? tlsOk ? "OK" : "Warning"
                    : "Error"
                : tlsOk ? "OK" : "Error";
            var tlsStatus = tlsOk ? "OK" : probe.SslPolicyErrors.ToString();
            var pinStatus = hasPins
                ? validation.Accepted ? "Matched" : "Mismatch"
                : "Not configured";
            var details = BuildDetails(validation.Message, probe.SslPolicyErrors, pins.Count);

            return new PolymarketCertificateCheckResult(
                endpoint.Name,
                endpoint.Url,
                uri.Host,
                tlsStatus,
                pinStatus,
                status,
                validation.PresentedPin ?? TryComputePin(certificate),
                certificate?.Subject ?? string.Empty,
                certificate?.Issuer ?? string.Empty,
                FormatCertificateDate(certificate?.NotBefore),
                FormatCertificateDate(certificate?.NotAfter),
                details,
                checkedAtUtc);
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException or TimeoutException)
        {
            return Error(endpoint, uri.Host, checkedAtUtc, "Certificate probe failed.", ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(endpoint, uri.Host, checkedAtUtc, "Certificate probe timed out.", "The TLS probe timed out.");
        }
    }

    private IReadOnlyList<CertificateEndpoint> BuildEndpoints()
    {
        return
        [
            new("Data API", polymarketOptions.DataApiBaseUrl),
            new("CLOB", polymarketOptions.ClobBaseUrl),
            new("Gamma API", polymarketOptions.GammaBaseUrl),
            new("Geoblock", polymarketOptions.GeoblockUrl),
            new("Market WebSocket", marketDataWebSocketOptions.MarketEndpointUrl)
        ];
    }

    private IReadOnlyList<string> PinsForHost(string host)
    {
        if (polymarketOptions.CertificatePins is null)
        {
            return [];
        }

        foreach (var item in polymarketOptions.CertificatePins)
        {
            if (string.Equals(item.Key, host, StringComparison.OrdinalIgnoreCase))
            {
                return item.Value
                    .Where(pin => !string.IsNullOrWhiteSpace(pin))
                    .Select(pin => pin.Trim())
                    .ToArray();
            }
        }

        return [];
    }

    private static async Task<CertificateProbeResult> ProbeCertificateAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var port = uri.IsDefaultPort ? 443 : uri.Port;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(uri.Host, port, timeout.Token);
        X509Certificate2? certificate = null;
        var sslPolicyErrors = SslPolicyErrors.RemoteCertificateNotAvailable;
        await using var sslStream = new SslStream(
            tcpClient.GetStream(),
            leaveInnerStreamOpen: false,
            (_, remoteCertificate, _, errors) =>
            {
                sslPolicyErrors = errors;
                certificate?.Dispose();
                certificate = remoteCertificate is null
                    ? null
                    : new X509Certificate2(remoteCertificate);
                return true;
            });

        await sslStream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = uri.Host,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.Online
            },
            timeout.Token);

        return new CertificateProbeResult(certificate, sslPolicyErrors);
    }

    private static PolymarketCertificateCheckResult Error(
        CertificateEndpoint endpoint,
        string host,
        DateTimeOffset checkedAtUtc,
        string status,
        string details)
    {
        return new PolymarketCertificateCheckResult(
            endpoint.Name,
            endpoint.Url,
            host,
            "Error",
            "Not checked",
            "Error",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            $"{status} {details}",
            checkedAtUtc);
    }

    private static string BuildDetails(string message, SslPolicyErrors sslPolicyErrors, int configuredPinCount)
    {
        var parts = new List<string> { message };
        parts.Add($"Configured pins={configuredPinCount}.");
        if (sslPolicyErrors != SslPolicyErrors.None)
        {
            parts.Add($"Standard TLS errors={sslPolicyErrors}.");
        }

        return string.Join(" ", parts);
    }

    private static string TryComputePin(X509Certificate2? certificate)
    {
        return certificate is null
            ? string.Empty
            : PolymarketCertificatePinning.ComputeSubjectPublicKeyInfoSha256Pin(certificate);
    }

    private static string FormatCertificateDate(DateTime? value)
    {
        return value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
    }

    private static bool IsSupportedScheme(string scheme)
    {
        return string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scheme, "wss", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CertificateEndpoint(string Name, string Url);
}

public sealed record CertificateProbeResult(
    X509Certificate2? Certificate,
    SslPolicyErrors SslPolicyErrors);

public sealed record PolymarketCertificateCheckResult(
    string EndpointName,
    string EndpointUrl,
    string Host,
    string TlsStatus,
    string PinStatus,
    string Status,
    string PresentedPin,
    string Subject,
    string Issuer,
    string ValidFromUtc,
    string ValidToUtc,
    string Details,
    DateTimeOffset CheckedAtUtc);
