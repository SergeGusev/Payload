namespace PolyCopyTrader.Polymarket.Auth;

public interface ISecretProvider
{
    Task<string?> GetSecretAsync(string name, CancellationToken ct);
}
