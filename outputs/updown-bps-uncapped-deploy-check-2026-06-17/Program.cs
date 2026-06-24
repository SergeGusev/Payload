using System.Data;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;

var options = CliOptions.Parse(args);
var configPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    "..",
    "..",
    "..",
    "..",
    "..",
    "src",
    "PolyCopyTrader.Service",
    "appsettings.json"));

var configuration = new ConfigurationBuilder()
    .AddJsonFile(configPath, optional: false)
    .AddEnvironmentVariables()
    .Build();
var storageOptions = configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
var connectionString = StorageConnectionResolver.Resolve(storageOptions);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Storage connection string is not configured.");
    return 2;
}

var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString)
{
    Timeout = 10,
    CommandTimeout = 30,
    Pooling = false
};
if (!string.IsNullOrWhiteSpace(options.HostOverride))
{
    connectionBuilder.Host = options.HostOverride;
}

await using var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await using (var readOnlyCommand = new NpgsqlCommand("SET TRANSACTION READ ONLY;", connection, transaction))
{
    await readOnlyCommand.ExecuteNonQueryAsync();
}

var capturedAtUtc = DateTimeOffset.UtcNow;
var dbNow = await GetDbNowUtcAsync(connection, transaction);
var sinceUtc = dbNow.AddMinutes(-Math.Abs(options.Minutes));
var fixedCodeRegex = "^(btc|eth|sol)_up_down_5m_(up|down)_bps_[0-9]+_instant$";

Console.WriteLine("Up/Down bps uncapped deploy check");
Console.WriteLine("captured_at_utc=" + capturedAtUtc.ToString("O", CultureInfo.InvariantCulture));
Console.WriteLine("db_now_utc=" + dbNow.ToString("O", CultureInfo.InvariantCulture));
Console.WriteLine("db_host=" + connectionBuilder.Host);
Console.WriteLine("database=" + connectionBuilder.Database);
Console.WriteLine("lookback_minutes=" + Math.Abs(options.Minutes).ToString(CultureInfo.InvariantCulture));
Console.WriteLine();

Console.WriteLine("service_heartbeats");
await PrintRowsAsync(
    connection,
    transaction,
    """
    SELECT
        service_name,
        status,
        started_at_utc,
        last_heartbeat_utc,
        round(extract(epoch from (now() - last_heartbeat_utc))::numeric, 1) AS heartbeat_age_seconds,
        version,
        mode,
        current_loop,
        COALESCE(NULLIF(last_error, ''), '<none>') AS last_error
    FROM service_heartbeats
    ORDER BY service_name;
    """);

Console.WriteLine();
Console.WriteLine("fixed_up_down_bps_strategy_inventory");
await PrintRowsAsync(
    connection,
    transaction,
    """
    SELECT
        count(*) AS total,
        count(*) FILTER (WHERE enabled) AS enabled,
        count(*) FILTER (WHERE live_stakes) AS live,
        count(*) FILTER (WHERE paused) AS manually_paused,
        count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused
    FROM strategies
    WHERE code ~ @FixedCodeRegex;
    """,
    ("FixedCodeRegex", fixedCodeRegex));

Console.WriteLine();
Console.WriteLine("recent_fixed_up_down_bps_runs_by_status");
await PrintRowsAsync(
    connection,
    transaction,
    """
    SELECT
        run.status,
        count(*) AS runs,
        count(*) FILTER (WHERE run.entry_price > 0.65) AS entry_price_above_old_cap,
        max(run.entered_at_utc) AS latest_entered_at_utc,
        max(run.updated_at_utc) AS latest_updated_at_utc
    FROM strategy_market_paper_runs run
    INNER JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE strategy.code ~ @FixedCodeRegex
      AND run.updated_at_utc >= @SinceUtc
    GROUP BY run.status
    ORDER BY run.status;
    """,
    ("FixedCodeRegex", fixedCodeRegex),
    ("SinceUtc", sinceUtc));

Console.WriteLine();
Console.WriteLine("recent_fixed_up_down_bps_order_diagnostics");
await PrintRowsAsync(
    connection,
    transaction,
    """
    WITH recent_orders AS (
        SELECT
            paper_order.created_at_utc,
            paper_order.price,
            strategy.code,
            NULLIF(paper_order.raw_decision_json->>'instant_max_buy_price', '')::numeric AS instant_max_buy_price,
            NULLIF(paper_order.raw_decision_json->>'instant_limit_price', '')::numeric AS instant_limit_price,
            paper_order.raw_decision_json->>'decision_source' AS decision_source
        FROM paper_orders paper_order
        INNER JOIN strategies strategy ON strategy.id = paper_order.strategy_id
        WHERE strategy.code ~ @FixedCodeRegex
          AND paper_order.created_at_utc >= @SinceUtc
    )
    SELECT
        count(*) AS orders,
        count(*) FILTER (WHERE instant_max_buy_price = 1.00) AS max_buy_price_1_00,
        count(*) FILTER (WHERE instant_max_buy_price = 0.65) AS max_buy_price_0_65,
        count(*) FILTER (WHERE price > 0.65) AS order_price_above_old_cap,
        max(price) AS max_order_price,
        max(created_at_utc) AS latest_order_at_utc
    FROM recent_orders;
    """,
    ("FixedCodeRegex", fixedCodeRegex),
    ("SinceUtc", sinceUtc));

Console.WriteLine();
Console.WriteLine("recent_fixed_up_down_bps_orders_sample");
await PrintRowsAsync(
    connection,
    transaction,
    """
    SELECT
        paper_order.created_at_utc,
        strategy.code,
        paper_order.outcome,
        paper_order.price,
        paper_order.raw_decision_json->>'instant_max_buy_price' AS instant_max_buy_price,
        paper_order.raw_decision_json->>'instant_limit_price' AS instant_limit_price,
        paper_order.raw_decision_json->>'decision_source' AS decision_source
    FROM paper_orders paper_order
    INNER JOIN strategies strategy ON strategy.id = paper_order.strategy_id
    WHERE strategy.code ~ @FixedCodeRegex
      AND paper_order.created_at_utc >= @SinceUtc
    ORDER BY paper_order.created_at_utc DESC
    LIMIT 12;
    """,
    ("FixedCodeRegex", fixedCodeRegex),
    ("SinceUtc", sinceUtc));

Console.WriteLine();
Console.WriteLine("recent_fixed_up_down_bps_instant_price_above_max_skips");
await PrintRowsAsync(
    connection,
    transaction,
    """
    SELECT
        count(*) AS skips,
        max(run.updated_at_utc) AS latest_skip_updated_at_utc
    FROM strategy_market_paper_runs run
    INNER JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE strategy.code ~ @FixedCodeRegex
      AND run.updated_at_utc >= @SinceUtc
      AND run.skip_reason = 'instant_price_above_max';
    """,
    ("FixedCodeRegex", fixedCodeRegex),
    ("SinceUtc", sinceUtc));

Console.WriteLine();
Console.WriteLine("latest_fixed_up_down_bps_instant_price_above_max_skips_sample");
await PrintRowsAsync(
    connection,
    transaction,
    """
    SELECT
        run.updated_at_utc,
        strategy.code,
        run.status,
        run.entry_price,
        run.skip_reason,
        run.skip_diagnostics_json->>'instant_max_buy_price' AS instant_max_buy_price,
        run.skip_diagnostics_json->>'instant_limit_price' AS instant_limit_price
    FROM strategy_market_paper_runs run
    INNER JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE strategy.code ~ @FixedCodeRegex
      AND run.skip_reason = 'instant_price_above_max'
    ORDER BY run.updated_at_utc DESC
    LIMIT 12;
    """,
    ("FixedCodeRegex", fixedCodeRegex));

await transaction.RollbackAsync();
return 0;

static async Task<DateTimeOffset> GetDbNowUtcAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction)
{
    await using var command = new NpgsqlCommand("SELECT now();", connection, transaction);
    var value = await command.ExecuteScalarAsync();
    return value switch
    {
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime(),
        DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        _ => throw new InvalidOperationException("Unexpected PostgreSQL now() value type: " + value?.GetType().FullName)
    };
}

static async Task PrintRowsAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    string sql,
    params (string Name, object Value)[] parameters)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }

    await using var reader = await command.ExecuteReaderAsync();
    var fieldCount = reader.FieldCount;
    var rows = 0;
    while (await reader.ReadAsync())
    {
        rows++;
        var values = new string[fieldCount];
        for (var index = 0; index < fieldCount; index++)
        {
            var name = reader.GetName(index);
            var value = reader.IsDBNull(index)
                ? "<null>"
                : FormatValue(reader.GetValue(index));
            values[index] = name + "=" + value;
        }

        Console.WriteLine(string.Join(" | ", values));
    }

    if (rows == 0)
    {
        Console.WriteLine("<no rows>");
    }
}

static string FormatValue(object value)
{
    return value switch
    {
        DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        decimal decimalValue => decimalValue.ToString("0.########", CultureInfo.InvariantCulture),
        double doubleValue => doubleValue.ToString("0.########", CultureInfo.InvariantCulture),
        float floatValue => floatValue.ToString("0.########", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}

internal sealed record CliOptions(int Minutes, string? HostOverride)
{
    public static CliOptions Parse(string[] args)
    {
        var minutes = 180;
        string? hostOverride = null;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--minutes", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Length &&
                int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMinutes))
            {
                minutes = parsedMinutes;
                index++;
                continue;
            }

            if (string.Equals(arg, "--host", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Length)
            {
                hostOverride = args[index + 1];
                index++;
            }
        }

        return new CliOptions(minutes, hostOverride);
    }
}
