using PolyCopyTrader.Domain;
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
    public void AuthEnabled_RequiresSigningAddress()
    {
        var configuration = new AppConfiguration
        {
            PolymarketAuth = new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "not-an-address"
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("SigningAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void LiveTrading_RequiresManualCodeAndAuth()
    {
        var configuration = new AppConfiguration
        {
            Bot = new BotOptions
            {
                Mode = BotMode.Live,
                EnableLiveTrading = true
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("ManualEnableCode", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PolymarketAuth.Enabled", StringComparison.Ordinal));
    }

    [Fact]
    public void LiveTrading_ConfiguredGateCanValidate()
    {
        var configuration = new AppConfiguration
        {
            Bot = new BotOptions
            {
                Mode = BotMode.Live,
                EnableLiveTrading = true
            },
            LiveTrading = new LiveTradingOptions
            {
                ManualEnableCode = "LIVE_TRADING_ENABLED"
            },
            PolymarketAuth = new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "0x1111111111111111111111111111111111111111",
                FunderAddress = "0x1111111111111111111111111111111111111111"
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Empty(errors);
    }

    [Fact]
    public void CertificatePins_RequireValidSha256SpkiFormat()
    {
        var configuration = new AppConfiguration
        {
            Polymarket = new PolymarketOptions
            {
                CertificatePins = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["data-api.polymarket.com"] = ["not-a-pin"]
                }
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("CertificatePins", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("sha256/<base64-spki-hash>", StringComparison.Ordinal));
    }

    [Fact]
    public void CertificatePins_MustMatchConfiguredEndpointHosts()
    {
        var configuration = new AppConfiguration
        {
            Polymarket = new PolymarketOptions
            {
                CertificatePins = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["example.com"] = [ValidCertificatePin()]
                }
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("example.com", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("configured Polymarket endpoint host", StringComparison.Ordinal));
    }

    [Fact]
    public void CertificatePins_AcceptConfiguredEndpointHost()
    {
        var configuration = new AppConfiguration
        {
            Polymarket = new PolymarketOptions
            {
                CertificatePins = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["data-api.polymarket.com"] = [ValidCertificatePin()]
                }
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Empty(errors);
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

    private static string ValidCertificatePin()
    {
        return "sha256/" + Convert.ToBase64String(new byte[32]);
    }
}
