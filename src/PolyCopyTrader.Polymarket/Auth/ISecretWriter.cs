namespace PolyCopyTrader.Polymarket.Auth;

public interface ISecretWriter
{
    Task SetSecretAsync(string name, string value, CancellationToken ct);
}
