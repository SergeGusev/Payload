using Npgsql;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Storage;

public sealed class PostgresConnectionFactory
{
    public PostgresConnectionFactory(StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var resolvedConnectionString = StorageConnectionResolver.Resolve(options)
            ?? throw new InvalidOperationException("PostgreSQL connection string is not configured.");
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(resolvedConnectionString);
        if (options.MaxPoolSize is { } maxPoolSize)
        {
            connectionStringBuilder.MaxPoolSize = maxPoolSize;
        }

        ConnectionString = connectionStringBuilder.ConnectionString;
    }

    public string ConnectionString { get; }

    public NpgsqlConnection CreateConnection()
    {
        return new NpgsqlConnection(ConnectionString);
    }
}
