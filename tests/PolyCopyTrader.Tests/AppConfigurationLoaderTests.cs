using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using PolyCopyTrader.Service.Configuration;

namespace PolyCopyTrader.Tests;

public sealed class AppConfigurationLoaderTests
{
    [Fact]
    public void Load_UsesCryptoReferencePriceHistoryDefaultsWhenSectionIsMissing()
    {
        var configurationRoot = new ConfigurationBuilder().Build();

        var configuration = AppConfigurationLoader.Load(configurationRoot);

        Assert.Equal(["BTC", "ETH", "SOL"], configuration.CryptoReferencePriceHistory.AssetSymbols);
        Assert.Equal([1_440, 720, 360, 180, 90, 45, 20, 10], configuration.CryptoReferencePriceHistory.WindowMinutes);
    }

    [Fact]
    public void Load_BindsAndNormalizesCryptoReferencePriceHistory()
    {
        Dictionary<string, string?> values = new()
        {
            ["CryptoReferencePriceHistory:Enabled"] = "false",
            ["CryptoReferencePriceHistory:AssetSymbols:0"] = " eth ",
            ["CryptoReferencePriceHistory:AssetSymbols:1"] = "BTC",
            ["CryptoReferencePriceHistory:AssetSymbols:2"] = "eth",
            ["CryptoReferencePriceHistory:WriteIntervalSeconds"] = "15",
            ["CryptoReferencePriceHistory:StartupLookbackHours"] = "36",
            ["CryptoReferencePriceHistory:TargetSamplesPerWindow"] = "45",
            ["CryptoReferencePriceHistory:WindowMinutes:0"] = "120",
            ["CryptoReferencePriceHistory:WindowMinutes:1"] = "30"
        };
        var configurationRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var configuration = AppConfigurationLoader.Load(configurationRoot);

        Assert.False(configuration.CryptoReferencePriceHistory.Enabled);
        Assert.Equal(["ETH", "BTC"], configuration.CryptoReferencePriceHistory.AssetSymbols);
        Assert.Equal(15, configuration.CryptoReferencePriceHistory.WriteIntervalSeconds);
        Assert.Equal(36, configuration.CryptoReferencePriceHistory.StartupLookbackHours);
        Assert.Equal(45, configuration.CryptoReferencePriceHistory.TargetSamplesPerWindow);
        Assert.Equal([120, 30], configuration.CryptoReferencePriceHistory.WindowMinutes);
    }

    [Fact]
    public void Load_BindsStrategyRunRetentionSafetyGates()
    {
        Dictionary<string, string?> values = new()
        {
            ["StrategyRunRetention:Enabled"] = "true",
            ["StrategyRunRetention:ApplyEnabled"] = "false",
            ["StrategyRunRetention:DirectPaperSkipCompactionEnabled"] = "true",
            ["StrategyRunRetention:DirectPaperSkipCompactionApplyEnabled"] = "false",
            ["StrategyRunRetention:RawRetentionHours"] = "96",
            ["StrategyRunRetention:CleanupIntervalMinutes"] = "15",
            ["StrategyRunRetention:CleanupBatchSize"] = "750",
            ["StrategyRunRetention:CleanupMaxBatchesPerCycle"] = "2"
        };
        var configurationRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var configuration = AppConfigurationLoader.Load(configurationRoot);

        Assert.True(configuration.StrategyRunRetention.Enabled);
        Assert.False(configuration.StrategyRunRetention.ApplyEnabled);
        Assert.True(configuration.StrategyRunRetention.DirectPaperSkipCompactionEnabled);
        Assert.False(configuration.StrategyRunRetention.DirectPaperSkipCompactionApplyEnabled);
        Assert.Equal(96, configuration.StrategyRunRetention.RawRetentionHours);
        Assert.Equal(15, configuration.StrategyRunRetention.CleanupIntervalMinutes);
        Assert.Equal(750, configuration.StrategyRunRetention.CleanupBatchSize);
        Assert.Equal(2, configuration.StrategyRunRetention.CleanupMaxBatchesPerCycle);
    }

    [Fact]
    public void Load_BindsPaperFakFeeBackfillOptions()
    {
        Dictionary<string, string?> values = new()
        {
            ["PaperFakFeeBackfill:Enabled"] = "true",
            ["PaperFakFeeBackfill:ApplyEnabled"] = "true",
            ["PaperFakFeeBackfill:HistoricalCutoffUtc"] = "2026-08-07T22:44:55.219515Z",
            ["PaperFakFeeBackfill:BatchSize"] = "42",
            ["PaperFakFeeBackfill:CycleIntervalSeconds"] = "17",
            ["PaperFakFeeBackfill:InitialDelaySeconds"] = "301",
            ["PaperFakFeeBackfill:IdleDelaySeconds"] = "901",
            ["PaperFakFeeBackfill:ErrorDelaySeconds"] = "61",
            ["PaperFakFeeBackfill:MaxErrorDelaySeconds"] = "901"
        };
        var configurationRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var configuration = AppConfigurationLoader.Load(configurationRoot);

        Assert.True(configuration.PaperFakFeeBackfill.Enabled);
        Assert.True(configuration.PaperFakFeeBackfill.ApplyEnabled);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 7, 22, 44, 55, TimeSpan.Zero).AddTicks(2_195_150),
            configuration.PaperFakFeeBackfill.HistoricalCutoffUtc);
        Assert.Equal(42, configuration.PaperFakFeeBackfill.BatchSize);
        Assert.Equal(17, configuration.PaperFakFeeBackfill.CycleIntervalSeconds);
        Assert.Equal(301, configuration.PaperFakFeeBackfill.InitialDelaySeconds);
        Assert.Equal(901, configuration.PaperFakFeeBackfill.IdleDelaySeconds);
        Assert.Equal(61, configuration.PaperFakFeeBackfill.ErrorDelaySeconds);
        Assert.Equal(901, configuration.PaperFakFeeBackfill.MaxErrorDelaySeconds);
    }

    [Fact]
    public void ServiceAppsettings_EnableBoundedPaperFakFeeBackfill()
    {
        var appsettingsPath = FindRepositoryFile(
            Path.Combine("src", "PolyCopyTrader.Service", "appsettings.json"));
        var configurationRoot = new ConfigurationBuilder()
            .AddJsonFile(appsettingsPath, optional: false)
            .Build();

        var configuration = AppConfigurationLoader.Load(configurationRoot);

        Assert.True(configuration.PaperFakFeeBackfill.Enabled);
        Assert.True(configuration.PaperFakFeeBackfill.ApplyEnabled);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 7, 22, 44, 55, TimeSpan.Zero).AddTicks(2_195_150),
            configuration.PaperFakFeeBackfill.HistoricalCutoffUtc);
        Assert.Equal(50, configuration.PaperFakFeeBackfill.BatchSize);
        Assert.Equal(15, configuration.PaperFakFeeBackfill.CycleIntervalSeconds);
        Assert.Equal(300, configuration.PaperFakFeeBackfill.InitialDelaySeconds);
        Assert.Equal(900, configuration.PaperFakFeeBackfill.IdleDelaySeconds);
        Assert.Equal(60, configuration.PaperFakFeeBackfill.ErrorDelaySeconds);
        Assert.Equal(900, configuration.PaperFakFeeBackfill.MaxErrorDelaySeconds);
    }

    private static string FindRepositoryFile(
        string relativePath,
        [CallerFilePath] string sourceFilePath = "")
    {
        var testProjectDirectory = Directory.GetParent(sourceFilePath)?.FullName ??
            throw new InvalidOperationException("The test source directory is unavailable.");
        var repositoryRoot = Path.GetFullPath(Path.Combine(testProjectDirectory, "..", ".."));
        var candidate = Path.Combine(repositoryRoot, relativePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }

    [Fact]
    public void Load_BindsDashboardProjectionReconciliationInterval()
    {
        Dictionary<string, string?> values = new()
        {
            ["Dashboard:ProjectionReconciliationIntervalSeconds"] = "45"
        };
        var configurationRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var configuration = AppConfigurationLoader.Load(configurationRoot);

        Assert.Equal(45, configuration.Dashboard.ProjectionReconciliationIntervalSeconds);
    }
}
