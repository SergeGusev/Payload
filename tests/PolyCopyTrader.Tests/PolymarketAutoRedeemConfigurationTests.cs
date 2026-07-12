using Microsoft.Extensions.Configuration;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Configuration;

namespace PolyCopyTrader.Tests;

public sealed class PolymarketAutoRedeemConfigurationTests
{
    [Fact]
    public void Load_ReadsPolymarketAutoRedeemSection()
    {
        var values = new Dictionary<string, string?>
        {
            ["PolymarketAutoRedeem:Enabled"] = "true",
            ["PolymarketAutoRedeem:DryRun"] = "true",
            ["PolymarketAutoRedeem:WalletAddress"] = "0x1111111111111111111111111111111111111111",
            ["PolymarketAutoRedeem:PollIntervalSeconds"] = "45",
            ["PolymarketAutoRedeem:MaxClaimsPerCycle"] = "7"
        };
        var configurationRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var configuration = AppConfigurationLoader.Load(configurationRoot);

        Assert.True(configuration.PolymarketAutoRedeem.Enabled);
        Assert.True(configuration.PolymarketAutoRedeem.DryRun);
        Assert.Equal("0x1111111111111111111111111111111111111111", configuration.PolymarketAutoRedeem.WalletAddress);
        Assert.Equal(45, configuration.PolymarketAutoRedeem.PollIntervalSeconds);
        Assert.Equal(7, configuration.PolymarketAutoRedeem.MaxClaimsPerCycle);
    }

    [Fact]
    public void AutoSubmitRequiresManualCodeDryRunOffAndAuth()
    {
        var configuration = new AppConfiguration
        {
            PolymarketAutoRedeem = new PolymarketAutoRedeemOptions
            {
                Enabled = true,
                DryRun = true,
                AutoSubmitEnabled = true,
                WalletAddress = "0x1111111111111111111111111111111111111111",
                ManualEnableCode = ""
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("ManualEnableCode", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DryRun", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PolymarketAuth.Enabled", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("SigningAddress", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("FunderAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void AutoSubmitGateCanValidateWhenExplicitlyConfigured()
    {
        var configuration = new AppConfiguration
        {
            PolymarketAutoRedeem = new PolymarketAutoRedeemOptions
            {
                Enabled = true,
                DryRun = false,
                AutoSubmitEnabled = true,
                ManualEnableCode = "AUTO_REDEEM_ENABLED",
                WalletAddress = "0x1111111111111111111111111111111111111111"
            },
            PolymarketAuth = new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "0x2222222222222222222222222222222222222222",
                FunderAddress = "0x1111111111111111111111111111111111111111"
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Empty(errors);
    }
}
