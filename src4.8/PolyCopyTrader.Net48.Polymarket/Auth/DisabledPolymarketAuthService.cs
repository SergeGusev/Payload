namespace PolyCopyTrader.Polymarket.Auth;

public sealed class DisabledPolymarketAuthService : IPolymarketAuthService
{
    public Task<AuthReadinessStatus> GetReadinessAsync(CancellationToken ct)
    {
        return Task.FromResult(AuthReadinessStatus.NotConfigured());
    }
}
