using Npgsql;

namespace PolyCopyTrader.Storage;

public sealed class PostgresSchemaInitializer(PostgresConnectionFactory connectionFactory) : IStorageSchemaInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(PostgresSchema.SchemaSql, connection);
            command.CommandTimeout = 0;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }
}
