[CmdletBinding()]
param(
    [string[]] $Hosts = @(
        "data-api.polymarket.com",
        "clob.polymarket.com",
        "polymarket.com",
        "ws-subscriptions-clob.polymarket.com"
    ),
    [switch] $AsAppSettings
)

$ErrorActionPreference = "Stop"

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("PolyCopyTraderPins_" + [Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $tempDirectory | Out-Null
    dotnet new console --framework net10.0 --output $tempDirectory --no-restore | Out-Null

    $programPath = Join-Path $tempDirectory "Program.cs"
    Set-Content -Path $programPath -Encoding UTF8 -Value @'
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

var results = new List<Result>();
foreach (var host in args)
{
    try
    {
        using var tcp = new TcpClient(host, 443);
        using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
        ssl.AuthenticateAsClient(host);

        using var certificate = new X509Certificate2(ssl.RemoteCertificate!);
        var subjectPublicKeyInfo = certificate.PublicKey.ExportSubjectPublicKeyInfo();
        var pin = "sha256/" + Convert.ToBase64String(SHA256.HashData(subjectPublicKeyInfo));

        results.Add(new Result(
            host,
            pin,
            certificate.Subject,
            certificate.Issuer,
            certificate.NotAfter.ToUniversalTime().ToString("u"),
            null));
    }
    catch (Exception ex)
    {
        results.Add(new Result(host, null, null, null, null, ex.Message));
    }
}

Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

public sealed record Result(
    string Host,
    string? Pin,
    string? Subject,
    string? Issuer,
    string? NotAfterUtc,
    string? Error);
'@

    $json = dotnet run --project $tempDirectory -- $Hosts
    $results = $json | ConvertFrom-Json

    if ($AsAppSettings) {
        $validResults = @($results | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Pin) })
        Write-Output '"CertificatePins": {'
        for ($i = 0; $i -lt $validResults.Count; $i++) {
            $item = $validResults[$i]
            $suffix = if ($i -lt ($validResults.Count - 1)) { "," } else { "" }
            Write-Output "  `"$($item.Host)`": [ `"$($item.Pin)`" ]$suffix"
        }
        Write-Output '}'
    }
    else {
        $results | Format-Table Host, NotAfterUtc, Pin, Subject, Issuer, Error -AutoSize -Wrap
    }
}
finally {
    if (Test-Path $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force
    }
}
