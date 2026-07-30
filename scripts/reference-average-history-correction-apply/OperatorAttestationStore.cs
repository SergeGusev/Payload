using System.Security.Cryptography;
using System.Text.Json;

namespace ReferenceAverageHistoryCorrectionApply;

internal static class OperatorAttestationStore
{
    public static OperatorAttestation? Read(ToolOptions options, bool required)
    {
        if (options.OperatorAttestationPath is null || options.OperatorAttestationSha256 is null)
        {
            if (required)
            {
                throw new InvalidOperationException(
                    "A fresh, SHA-pinned --operator-attestation is required for every production mutation/recovery mode.");
            }
            return null;
        }

        var path = options.OperatorAttestationPath;
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Operator attestation does not exist.", path);
        }
        var actualSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!actualSha.Equals(options.OperatorAttestationSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Operator attestation SHA-256 mismatch: expected {options.OperatorAttestationSha256}, actual {actualSha}.");
        }

        var attestation = JsonSerializer.Deserialize<OperatorAttestation>(File.ReadAllBytes(path),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }) ??
            throw new InvalidDataException("Operator attestation JSON is invalid.");
        var now = DateTimeOffset.UtcNow;
        if (attestation.SchemaVersion != 1 ||
            !attestation.Host.Equals(options.Host, StringComparison.Ordinal) ||
            attestation.Port != options.Port ||
            !attestation.Database.Equals(options.Database, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(attestation.DataDirectory) ||
            !attestation.DataVolume.Equals("C:", StringComparison.OrdinalIgnoreCase) ||
            attestation.FreeBytes <= 0 ||
            !attestation.ServiceName.Equals("PolyCopyTrader.Service", StringComparison.Ordinal) ||
            !attestation.ServiceState.Equals("Stopped", StringComparison.Ordinal) ||
            !attestation.ServiceStartMode.Equals("Disabled", StringComparison.Ordinal) ||
            !attestation.CollectionMethod.Equals("windows_service_and_driveinfo_local_capture_v1",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(attestation.Observer) ||
            attestation.ObservedAtUtc > now.AddMinutes(1) ||
            attestation.ObservedAtUtc < now.AddMinutes(-15))
        {
            throw new InvalidDataException(
                "Operator attestation is stale or does not prove the exact DB identity, C: free bytes, " +
                "and PolyCopyTrader.Service Stopped+Disabled state.");
        }
        return attestation;
    }
}
