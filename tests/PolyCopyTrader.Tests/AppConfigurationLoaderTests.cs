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
        Assert.Equal(96, configuration.StrategyRunRetention.RawRetentionHours);
        Assert.Equal(15, configuration.StrategyRunRetention.CleanupIntervalMinutes);
        Assert.Equal(750, configuration.StrategyRunRetention.CleanupBatchSize);
        Assert.Equal(2, configuration.StrategyRunRetention.CleanupMaxBatchesPerCycle);
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
