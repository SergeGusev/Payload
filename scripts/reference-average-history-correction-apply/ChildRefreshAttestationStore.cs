using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReferenceAverageHistoryCorrectionApply;

internal sealed record VerifiedChildRefreshAttestation(
    ChildRefreshAttestation Attestation,
    string AttestationPath,
    string AttestationSha256,
    string ServiceLogPath);

internal static partial class ChildRefreshAttestationStore
{
    private const string RequiredCollectionMethod =
        "serilog_plaintext_exact_line_sha256_plus_operator_capture_v1";

    [GeneratedRegex(
        @"^\[(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) INF\] BTC Up or Down 5m child-parent assignments refreshed\. Children=(?<children>\d+) ActiveParents=(?<activeParents>\d+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CompletionLineRegex();

    public static VerifiedChildRefreshAttestation Read(
        ToolOptions options,
        DateTimeOffset maintenanceCompletedAtUtc)
    {
        var path = options.ChildRefreshAttestationPath ??
                   throw new InvalidOperationException("Child-refresh attestation path is missing.");
        var expectedSha = options.ChildRefreshAttestationSha256 ??
                          throw new InvalidOperationException("Child-refresh attestation SHA-256 is missing.");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Child-refresh attestation does not exist.", path);
        }

        var actualSha = GraphPackageReader.Sha256File(path);
        if (!actualSha.Equals(expectedSha, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Child-refresh attestation SHA-256 mismatch: expected {expectedSha}, actual {actualSha}.");
        }

        var attestation = JsonSerializer.Deserialize<ChildRefreshAttestation>(File.ReadAllBytes(path),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }) ??
            throw new InvalidDataException("Child-refresh attestation JSON is invalid.");
        var now = DateTimeOffset.UtcNow;
        if (attestation.SchemaVersion != 1 ||
            !attestation.Host.Equals(options.Host, StringComparison.Ordinal) ||
            attestation.Port != options.Port ||
            !attestation.Database.Equals(options.Database, StringComparison.Ordinal) ||
            !attestation.ServiceName.Equals("PolyCopyTrader.Service", StringComparison.Ordinal) ||
            Path.GetFileName(attestation.ServiceLogFileName) != attestation.ServiceLogFileName ||
            string.IsNullOrWhiteSpace(attestation.ServiceLogFileName) ||
            !attestation.CollectionMethod.Equals(RequiredCollectionMethod, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(attestation.Observer) ||
            attestation.Children < 0 || attestation.ActiveParents < 0 ||
            attestation.ActiveParents > attestation.Children ||
            attestation.RefreshCompletedAtUtc < maintenanceCompletedAtUtc ||
            attestation.RefreshCompletedAtUtc > attestation.ObservedAtUtc ||
            attestation.ObservedAtUtc > now.AddMinutes(1) ||
            attestation.ObservedAtUtc < now.AddMinutes(-15))
        {
            throw new InvalidDataException(
                "Child-refresh attestation is stale or does not match the exact service/database/count/time contract.");
        }

        var logSha = GraphPackageReader.RequireSha256(attestation.ServiceLogSha256,
            "child_refresh.service_log_sha256");
        if (!logSha.Equals(attestation.ServiceLogSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Child-refresh service-log SHA-256 must be lowercase.");
        }

        var logPath = Path.Combine(Path.GetDirectoryName(path)!, attestation.ServiceLogFileName);
        if (!File.Exists(logPath))
        {
            throw new FileNotFoundException("SHA-pinned child-refresh service log does not exist.", logPath);
        }
        var logInfo = new FileInfo(logPath);
        if (logInfo.Length <= 0 || logInfo.Length > 100_000_000)
        {
            throw new InvalidDataException("Child-refresh service-log evidence must be between 1 byte and 100 MB.");
        }

        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        var actualLogSha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actualLogSha.Equals(logSha, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Child-refresh service-log SHA-256 mismatch: expected {logSha}, actual {actualLogSha}.");
        }

        var match = CompletionLineRegex().Match(attestation.CompletionLogLine);
        if (!match.Success ||
            !DateTimeOffset.TryParseExact(match.Groups["timestamp"].Value,
                "yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedTimestamp) ||
            parsedTimestamp.ToUniversalTime() != attestation.RefreshCompletedAtUtc.ToUniversalTime() ||
            !int.TryParse(match.Groups["children"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var children) ||
            !int.TryParse(match.Groups["activeParents"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var activeParents) ||
            children != attestation.Children || activeParents != attestation.ActiveParents)
        {
            throw new InvalidDataException(
                "Child-refresh completion log line does not match the exact Serilog event/timestamp/count contract.");
        }

        stream.Position = 0;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true, bufferSize: 1024 * 1024, leaveOpen: false);
        var exactLineMatches = 0;
        while (reader.ReadLine() is { } line)
        {
            if (line.Equals(attestation.CompletionLogLine, StringComparison.Ordinal))
            {
                exactLineMatches++;
            }
        }
        if (exactLineMatches != 1)
        {
            throw new InvalidDataException(
                $"SHA-pinned service log contains {exactLineMatches:N0} exact completion lines; exactly one is required.");
        }

        return new VerifiedChildRefreshAttestation(attestation, path, actualSha, logPath);
    }
}
