using System.Net;
using Npgsql;

namespace ReferenceAverageHistoryCorrectionApply;

internal static class DatabaseConnection
{
    public const string RequiredHost = "192.168.0.101";
    public const int RequiredPort = 5432;
    public const string RequiredDatabase = "polycopytrader";
    public const string RequiredSearchPath = "pg_catalog,public";
    public const string ConnectionEnvironmentVariable = "POLYCOPYTRADER_POSTGRES_CONNECTION";

    public static NpgsqlConnection Create(ToolOptions options)
    {
        if (!options.Host.Equals(RequiredHost, StringComparison.Ordinal) ||
            options.Port != RequiredPort ||
            !options.Database.Equals(RequiredDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Physical correction is pinned to {RequiredHost}:{RequiredPort}/{RequiredDatabase}.");
        }

        var raw = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                $"Database credentials are missing. Set {ConnectionEnvironmentVariable}; credentials are never accepted on the command line.");
        }

        var builder = new NpgsqlConnectionStringBuilder(raw)
        {
            Host = options.Host,
            Port = options.Port,
            Database = options.Database,
            SearchPath = RequiredSearchPath,
            Pooling = false,
            Multiplexing = false,
            ApplicationName = "reference-average-history-correction-apply",
            IncludeErrorDetail = false,
            CommandTimeout = 0,
            Timeout = 15
        };
        return new NpgsqlConnection(builder.ConnectionString);
    }

    public static async Task VerifyIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolOptions options,
        bool expectedReadOnly,
        string expectedIsolation,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT inet_server_addr()::text,
                   inet_server_port(),
                   current_database(),
                   current_setting('search_path'),
                   current_setting('TimeZone'),
                   current_setting('transaction_isolation'),
                   current_setting('transaction_read_only')::boolean;
            """, connection, transaction);
        await using var data = await command.ExecuteReaderAsync(cancellationToken);
        if (!await data.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Database identity query returned no row.");
        }

        var addressText = data.GetString(0);
        var port = data.GetInt32(1);
        var database = data.GetString(2);
        var searchPath = data.GetString(3).Replace(" ", string.Empty, StringComparison.Ordinal);
        var timeZone = data.GetString(4);
        var isolation = data.GetString(5);
        var readOnly = data.GetBoolean(6);
        if (!IPAddress.TryParse(addressText, out var actualAddress) ||
            !IPAddress.TryParse(options.Host, out var requiredAddress) ||
            !actualAddress.MapToIPv4().Equals(requiredAddress.MapToIPv4()) ||
            port != options.Port || !database.Equals(options.Database, StringComparison.Ordinal) ||
            !searchPath.Equals(RequiredSearchPath, StringComparison.Ordinal) ||
            !timeZone.Equals("UTC", StringComparison.OrdinalIgnoreCase) ||
            !isolation.Equals(expectedIsolation, StringComparison.OrdinalIgnoreCase) ||
            readOnly != expectedReadOnly)
        {
            throw new InvalidOperationException(
                $"Database identity/transaction mismatch: server={addressText}:{port}, db={database}, " +
                $"search_path={searchPath}, timezone={timeZone}, isolation={isolation}, read_only={readOnly}.");
        }
    }
}
