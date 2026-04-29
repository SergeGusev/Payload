namespace PolyCopyTrader.Storage;

public interface ISqliteSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
