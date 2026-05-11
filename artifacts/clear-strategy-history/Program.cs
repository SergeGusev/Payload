using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 1;
}

string[] tables =
[
    "paper_live_shadow_discrepancies",
    "paper_live_shadow_decisions",
    "live_orders",
    "live_trading_events",
    "dry_run_orders",
    "paper_copied_leader_activity_events",
    "paper_copied_leader_positions",
    "strategy_market_paper_runs",
    "paper_fills",
    "paper_orders",
    "paper_positions",
    "paper_position_settlements",
    "paper_copied_trader_performance",
    "signal_rejections",
    "signals",
    "risk_events",
    "daily_reports",
    "order_book_snapshots"
];

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync();

try
{
    Console.WriteLine("Counts before cleanup:");
    foreach (var table in tables)
    {
        await using var countCommand = new NpgsqlCommand($"SELECT count(*) FROM public.{table};", connection, transaction);
        countCommand.CommandTimeout = 300;
        var count = (long)(await countCommand.ExecuteScalarAsync() ?? 0L);
        Console.WriteLine($"{table}: {count}");
    }

    var truncateSql = "TRUNCATE TABLE " + string.Join(", ", tables.Select(table => "public." + table)) + ";";
    await using (var truncateCommand = new NpgsqlCommand(truncateSql, connection, transaction))
    {
        truncateCommand.CommandTimeout = 300;
        await truncateCommand.ExecuteNonQueryAsync();
    }

    Console.WriteLine("Counts after cleanup:");
    foreach (var table in tables)
    {
        await using var countCommand = new NpgsqlCommand($"SELECT count(*) FROM public.{table};", connection, transaction);
        countCommand.CommandTimeout = 300;
        var count = (long)(await countCommand.ExecuteScalarAsync() ?? 0L);
        Console.WriteLine($"{table}: {count}");
    }

    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}

Console.WriteLine("Strategy history cleanup committed.");
return 0;
