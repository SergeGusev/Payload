using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void DefaultConfiguration_IsValid()
    {
        var configuration = new AppConfiguration();

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Empty(errors);
    }

    [Fact]
    public void InvalidConfiguration_ReturnsClearErrors()
    {
        var configuration = new AppConfiguration
        {
            Bot = new BotOptions
            {
                PollIntervalSeconds = 0
            },
            Polymarket = new PolymarketOptions
            {
                DataApiBaseUrl = "not-a-url",
                ClobBaseUrl = "https://clob.polymarket.com",
                GammaBaseUrl = "https://gamma-api.polymarket.com",
                GeoblockUrl = "https://polymarket.com/api/geoblock"
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("PollIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DataApiBaseUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void SanitizedSummary_DoesNotExposeSecrets()
    {
        var configuration = new AppConfiguration();

        var summary = AppOptionsValidator.ToSanitizedSummary(configuration);

        Assert.Contains("Mode:", summary);
        Assert.DoesNotContain("private", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", summary, StringComparison.OrdinalIgnoreCase);
    }
}
