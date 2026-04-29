using Microsoft.Data.Sqlite;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Storage;

public sealed class SqliteConnectionFactory
{
    public SqliteConnectionFactory(StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        DatabasePath = ResolveDatabasePath(options.DatabasePath);
    }

    public string DatabasePath { get; }

    public SqliteConnection CreateConnection()
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true
        };

        return new SqliteConnection(builder.ToString());
    }

    private static string ResolveDatabasePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        return Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
    }
}
