using Microsoft.Extensions.Configuration;

namespace PolyCopyTrader.Service.Configuration;

public static class ServiceLogPathResolver
{
    public const string DirectoryConfigurationKey = "ServiceLogging:Directory";

    public static ServiceLogPathResolution Resolve(
        IConfiguration configuration,
        string baseDirectory,
        TextWriter diagnosticWriter)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(diagnosticWriter);

        var fallbackDirectory = Path.GetFullPath(Path.Combine(baseDirectory, "logs"));
        var configuredDirectory = configuration[DirectoryConfigurationKey];
        if (configuredDirectory is null)
        {
            Directory.CreateDirectory(fallbackDirectory);
            return new ServiceLogPathResolution(fallbackDirectory, UsedFallback: false);
        }

        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            diagnosticWriter.WriteLine(
                $"Configured service log directory is empty. Falling back to '{fallbackDirectory}'.");
            Directory.CreateDirectory(fallbackDirectory);
            return new ServiceLogPathResolution(fallbackDirectory, UsedFallback: true);
        }

        try
        {
            var resolvedDirectory = Path.GetFullPath(configuredDirectory.Trim());
            Directory.CreateDirectory(resolvedDirectory);
            return new ServiceLogPathResolution(resolvedDirectory, UsedFallback: false);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException or
            IOException or UnauthorizedAccessException)
        {
            diagnosticWriter.WriteLine(
                $"Configured service log directory '{configuredDirectory}' is unavailable: " +
                $"{exception.GetType().Name}. Falling back to '{fallbackDirectory}'.");
            Directory.CreateDirectory(fallbackDirectory);
            return new ServiceLogPathResolution(fallbackDirectory, UsedFallback: true);
        }
    }
}

public sealed record ServiceLogPathResolution(string DirectoryPath, bool UsedFallback);
