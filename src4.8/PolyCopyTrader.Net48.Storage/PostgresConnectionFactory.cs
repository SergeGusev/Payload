using Npgsql;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Storage;

public sealed class PostgresConnectionFactory
{
    public PostgresConnectionFactory(StorageOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        ConnectionString = StorageConnectionResolver.Resolve(options)
            ?? throw new InvalidOperationException("PostgreSQL connection string is not configured.");
    }

    public string ConnectionString { get; }

    public NpgsqlConnection CreateConnection()
    {
        return new NpgsqlConnection(ConnectionString);
    }
}
