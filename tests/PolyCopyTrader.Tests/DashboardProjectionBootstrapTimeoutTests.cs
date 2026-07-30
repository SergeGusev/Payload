using Npgsql;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class DashboardProjectionBootstrapTimeoutTests
{
    [Fact]
    public void BootstrapConnectionString_DisablesCommandTimeoutWithoutChangingEndpointOrPoolSettings()
    {
        const string source =
            "Host=bootstrap-host;Port=5544;Database=bootstrap-db;Username=bootstrap-user;" +
            "Application Name=bootstrap-test;Maximum Pool Size=7;" +
            "Command Timeout=30";

        var configured = new NpgsqlConnectionStringBuilder(
            PostgresDashboardProjectionRepository.CreateBootstrapConnectionString(source));

        Assert.Equal(0, configured.CommandTimeout);
        Assert.Equal("bootstrap-host", configured.Host);
        Assert.Equal(5544, configured.Port);
        Assert.Equal("bootstrap-db", configured.Database);
        Assert.Equal("bootstrap-user", configured.Username);
        Assert.Equal("bootstrap-test", configured.ApplicationName);
        Assert.Equal(7, configured.MaxPoolSize);
    }
}
