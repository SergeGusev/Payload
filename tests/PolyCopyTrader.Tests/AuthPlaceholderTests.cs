using PolyCopyTrader.Polymarket.Auth;

namespace PolyCopyTrader.Tests;

public sealed class AuthPlaceholderTests
{
    [Fact]
    public void AuthService_InterfaceOnlyExposesReadiness()
    {
        var method = Assert.Single(typeof(IPolymarketAuthService).GetMethods());

        Assert.Equal(nameof(IPolymarketAuthService.GetReadinessAsync), method.Name);
        Assert.Equal(typeof(Task<AuthReadinessStatus>), method.ReturnType);
    }

    [Fact]
    public void TradingClient_IsPlaceholderWithoutLiveOrderMethods()
    {
        Assert.Empty(typeof(IPolymarketTradingClient).GetMethods());
    }

    [Fact]
    public void Readiness_NotConfiguredDoesNotRequireSecrets()
    {
        var checkedAt = new DateTimeOffset(2026, 04, 29, 00, 00, 00, TimeSpan.Zero);

        var status = AuthReadinessStatus.NotConfigured(checkedAt);

        Assert.Equal("NotConfigured", status.State);
        Assert.False(status.IsConfigured);
        Assert.False(status.CanAuthenticate);
        Assert.Contains(
            status.MissingRequirements,
            item => item.Contains("No API credentials", StringComparison.Ordinal));
        Assert.Equal(checkedAt, status.CheckedAtUtc);
    }
}
