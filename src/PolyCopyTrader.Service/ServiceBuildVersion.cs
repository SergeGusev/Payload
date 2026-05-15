using System.Reflection;

namespace PolyCopyTrader.Service;

public static class ServiceBuildVersion
{
    public const string DeploymentVersionEnvironmentVariable = "POLYCOPYTRADER_DEPLOYMENT_VERSION";

    public static string GetHeartbeatVersion()
    {
        var assembly = typeof(ServiceBuildVersion).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var assemblyVersion = assembly.GetName().Version?.ToString();
        return FormatHeartbeatVersion(
            Environment.GetEnvironmentVariable(DeploymentVersionEnvironmentVariable),
            informationalVersion,
            assemblyVersion,
            assembly.ManifestModule.ModuleVersionId);
    }

    public static string FormatHeartbeatVersion(
        string? deploymentVersion,
        string? informationalVersion,
        string? assemblyVersion,
        Guid moduleVersionId)
    {
        var parts = new List<string>();
        AddPart(parts, "deploy", deploymentVersion);
        AddPart(parts, "info", informationalVersion);
        AddPart(parts, "assembly", assemblyVersion);
        parts.Add("mvid=" + moduleVersionId.ToString("N")[..12]);
        return string.Join("; ", parts);
    }

    private static void AddPart(List<string> parts, string name, string? value)
    {
        var normalized = Normalize(value);
        if (normalized is not null)
        {
            parts.Add(name + "=" + normalized);
        }
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(';', ',')
            .Trim();
        return normalized.Length <= 160 ? normalized : normalized[..160];
    }
}
