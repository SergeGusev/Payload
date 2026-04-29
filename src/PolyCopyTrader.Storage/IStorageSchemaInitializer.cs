namespace PolyCopyTrader.Storage;

public interface IStorageSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
