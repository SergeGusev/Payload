using PolyCopyTrader.Service;

namespace PolyCopyTrader.Tests;

public sealed class ServiceBuildVersionTests
{
    [Fact]
    public void FormatHeartbeatVersion_IncludesDeploymentInformationAndFingerprint()
    {
        var version = ServiceBuildVersion.FormatHeartbeatVersion(
            "prod-abcdef123456",
            "1.0.0+abcdef123456",
            "1.0.0.0",
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"));

        Assert.Contains("deploy=prod-abcdef123456", version, StringComparison.Ordinal);
        Assert.Contains("info=1.0.0+abcdef123456", version, StringComparison.Ordinal);
        Assert.Contains("assembly=1.0.0.0", version, StringComparison.Ordinal);
        Assert.Contains("mvid=0123456789ab", version, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatHeartbeatVersion_SanitizesSeparators()
    {
        var version = ServiceBuildVersion.FormatHeartbeatVersion(
            "prod;bad\r\nvalue",
            null,
            "1.0.0.0",
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"));

        Assert.Contains("deploy=prod,bad  value", version, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", version, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", version, StringComparison.Ordinal);
    }
}
