using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 2;
}

var builder = new NpgsqlConnectionStringBuilder(connectionString);
var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
if (!string.IsNullOrWhiteSpace(hostOverride))
{
    builder.Host = hostOverride;
}

builder.Options = "-c default_transaction_read_only=on -c statement_timeout=120000 -c lock_timeout=2000";

var repository = new PostgresAppRepository(
    new PostgresConnectionFactory(new StorageOptions
    {
        ConnectionString = builder.ConnectionString
    }));

Console.WriteLine($"probe_started_utc={DateTimeOffset.UtcNow:O}");
Console.WriteLine($"target_database={builder.Database}");
Console.WriteLine($"target_host={builder.Host}");
Console.WriteLine($"target_port={builder.Port}");

var rows = await repository.GetStrategyPerformanceAsync();
Console.WriteLine($"performance_rows={rows.Count}");

var targetRows = rows
    .Where(row => row.Code.Contains("prev_score_countertrend", StringComparison.Ordinal))
    .OrderBy(row => row.Code)
    .ToArray();

Console.WriteLine($"countertrend_rows={targetRows.Length}");
foreach (var row in targetRows)
{
    Console.WriteLine(
        "countertrend=" + row.Code +
        "|orders=" + row.OrdersCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        "|avg_score_bps=" + row.AvgCountertrendScoreBps.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        "|avg_signal_bps=" + row.AvgCountertrendSignalBps.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        "|last_signal_bps=" + (row.LastCountertrendSignalBps?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""));
}

Console.WriteLine($"probe_finished_utc={DateTimeOffset.UtcNow:O}");
return 0;
