namespace PolyCopyTrader.Polymarket.Auth;

public interface IPolymarketAuthService
{
    Task<AuthReadinessStatus> GetReadinessAsync(CancellationToken ct);
}
