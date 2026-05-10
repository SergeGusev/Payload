namespace PolyCopyTrader.Storage;

public sealed class NoOpStorageSchemaInitializer : IStorageSchemaInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
