namespace PolyCopyTrader.Polymarket.Auth;

public sealed class EnvironmentVariableSecretProvider : ISecretProvider
{
    public Task<string?> GetSecretAsync(string name, CancellationToken ct)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        ct.ThrowIfCancellationRequested();

        var value = Environment.GetEnvironmentVariable(name);
        return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value);
    }
}
