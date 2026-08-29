using Microsoft.Extensions.Configuration;
using PolyCopyTrader.Service.Configuration;
using Serilog;

namespace PolyCopyTrader.Tests;

public sealed class ServiceLogPathResolverTests
{
    [Fact]
    public void Resolve_UsesConfiguredAbsoluteDirectory()
    {
        var root = CreateTestDirectory();
        var configured = Path.Combine(root, "production-logs");
        var configuration = Configuration(
            new Dictionary<string, string?>
            {
                [ServiceLogPathResolver.DirectoryConfigurationKey] = configured
            });

        var result = ServiceLogPathResolver.Resolve(configuration, root, TextWriter.Null);

        Assert.Equal(Path.GetFullPath(configured), result.DirectoryPath);
        Assert.False(result.UsedFallback);
        Assert.True(Directory.Exists(configured));
    }

    [Fact]
    public void Resolve_UsesExecutableLocalLogsWhenNotConfigured()
    {
        var root = CreateTestDirectory();

        var result = ServiceLogPathResolver.Resolve(Configuration(), root, TextWriter.Null);

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "logs")), result.DirectoryPath);
        Assert.False(result.UsedFallback);
        Assert.True(Directory.Exists(result.DirectoryPath));
    }

    [Fact]
    public void Resolve_EmptyConfiguredDirectoryReportsAndFallsBack()
    {
        var root = CreateTestDirectory();
        var diagnostic = new StringWriter();
        var configuration = Configuration(
            new Dictionary<string, string?>
            {
                [ServiceLogPathResolver.DirectoryConfigurationKey] = "   "
            });

        var result = ServiceLogPathResolver.Resolve(configuration, root, diagnostic);

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "logs")), result.DirectoryPath);
        Assert.True(result.UsedFallback);
        Assert.Contains("is empty", diagnostic.ToString(), StringComparison.Ordinal);
        Assert.Contains(result.DirectoryPath, diagnostic.ToString(), StringComparison.Ordinal);
        Assert.True(Directory.Exists(result.DirectoryPath));
    }

    [Fact]
    public void Resolve_UnavailableConfiguredDirectoryReportsAndFallsBack()
    {
        var root = CreateTestDirectory();
        var blockingFile = Path.Combine(root, "not-a-directory");
        File.WriteAllText(blockingFile, "blocking file");
        var diagnostic = new StringWriter();
        var configuration = Configuration(
            new Dictionary<string, string?>
            {
                [ServiceLogPathResolver.DirectoryConfigurationKey] = blockingFile
            });

        var result = ServiceLogPathResolver.Resolve(configuration, root, diagnostic);

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "logs")), result.DirectoryPath);
        Assert.True(result.UsedFallback);
        Assert.Contains("IOException", diagnostic.ToString(), StringComparison.Ordinal);
        Assert.Contains(result.DirectoryPath, diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolvedDirectory_AcceptsFlushedSerilogFileRecord()
    {
        var root = CreateTestDirectory();
        var configured = Path.Combine(root, "file-sink");
        var result = ServiceLogPathResolver.Resolve(
            Configuration(new Dictionary<string, string?>
            {
                [ServiceLogPathResolver.DirectoryConfigurationKey] = configured
            }),
            root,
            TextWriter.Null);
        var path = Path.Combine(result.DirectoryPath, "service-test.log");

        Log.Logger = new LoggerConfiguration().WriteTo.File(path).CreateLogger();
        try
        {
            Log.Information("service-log-path-test-record");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }

        Assert.True(File.Exists(path));
        Assert.Contains(
            "service-log-path-test-record",
            await File.ReadAllTextAsync(path),
            StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(
        IReadOnlyDictionary<string, string?>? values = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();

    private static string CreateTestDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "PolyCopyTrader.ServiceLogPathResolverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
