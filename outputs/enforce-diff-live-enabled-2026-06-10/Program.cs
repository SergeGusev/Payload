using System.Data;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not configured.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    Host = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE") ?? "192.168.0.101",
    Timeout = 10,
    CommandTimeout = 60
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);

Console.WriteLine("== Before ==");
await PrintRowsAsync(BuildCountsSql());

Console.WriteLine();
Console.WriteLine("== Sample before ==");
await PrintRowsAsync(BuildSampleSql());

Console.WriteLine();
Console.WriteLine("== Update ==");
await PrintRowsAsync("""
WITH classified AS (
    SELECT
        id,
        (
            live_stakes
            OR code ~ '^[a-z]+_up_down_5m_(up|down)_diff_[0-9]+_instant$'
            OR code ~ '^[a-z]+_up_down_5m_(up|down)_adjusted_diff_[0-9]+_instant$'
            OR code ~ '^[a-z]+_up_down_5m_(up|down)_shift_diff_[0-9]+_[0-9]+_instant$'
        ) AS should_enable
    FROM strategies
),
updated AS (
    UPDATE strategies strategy
    SET
        enabled = classified.should_enable,
        updated_at_utc = now()
    FROM classified
    WHERE strategy.id = classified.id
      AND strategy.enabled IS DISTINCT FROM classified.should_enable
    RETURNING classified.should_enable
)
SELECT
    count(*) AS changed_rows,
    count(*) FILTER (WHERE should_enable) AS enabled_rows,
    count(*) FILTER (WHERE NOT should_enable) AS disabled_rows
FROM updated;
""");

Console.WriteLine();
Console.WriteLine("== After ==");
await PrintRowsAsync(BuildCountsSql());

Console.WriteLine();
Console.WriteLine("== Sample after ==");
await PrintRowsAsync(BuildSampleSql());

Console.WriteLine();
Console.WriteLine("== Enabled non-allowed sample ==");
await PrintRowsAsync("""
WITH classified AS (
    SELECT
        code,
        name,
        enabled,
        live_stakes,
        (
            live_stakes
            OR code ~ '^[a-z]+_up_down_5m_(up|down)_diff_[0-9]+_instant$'
            OR code ~ '^[a-z]+_up_down_5m_(up|down)_adjusted_diff_[0-9]+_instant$'
            OR code ~ '^[a-z]+_up_down_5m_(up|down)_shift_diff_[0-9]+_[0-9]+_instant$'
        ) AS should_enable
    FROM strategies
)
SELECT code, name, live_stakes
FROM classified
WHERE enabled
  AND NOT should_enable
ORDER BY code
LIMIT 20;
""");

await transaction.CommitAsync();

static string BuildCountsSql()
{
    return """
WITH classified AS (
    SELECT
        enabled,
        live_stakes,
        code ~ '^[a-z]+_up_down_5m_(up|down)_diff_[0-9]+_instant$' AS regular_diff,
        code ~ '^[a-z]+_up_down_5m_(up|down)_adjusted_diff_[0-9]+_instant$' AS adjusted_diff,
        code ~ '^[a-z]+_up_down_5m_(up|down)_shift_diff_[0-9]+_[0-9]+_instant$' AS shift_diff
    FROM strategies
),
classified2 AS (
    SELECT
        *,
        live_stakes OR regular_diff OR adjusted_diff OR shift_diff AS should_enable
    FROM classified
)
SELECT
    count(*) AS total_strategies,
    count(*) FILTER (WHERE enabled) AS enabled_total,
    count(*) FILTER (WHERE should_enable) AS allowed_total,
    count(*) FILTER (WHERE enabled AND should_enable) AS enabled_allowed,
    count(*) FILTER (WHERE enabled AND NOT should_enable) AS enabled_non_allowed,
    count(*) FILTER (WHERE NOT enabled AND should_enable) AS disabled_allowed,
    count(*) FILTER (WHERE enabled AND regular_diff) AS enabled_regular_diff,
    count(*) FILTER (WHERE enabled AND adjusted_diff) AS enabled_adjusted_diff,
    count(*) FILTER (WHERE enabled AND shift_diff) AS enabled_shift_diff,
    count(*) FILTER (WHERE enabled AND live_stakes) AS enabled_live
FROM classified2;
""";
}

static string BuildSampleSql()
{
    return """
SELECT
    code,
    name,
    enabled,
    live_stakes,
    CASE
        WHEN live_stakes THEN 'live'
        WHEN code ~ '^[a-z]+_up_down_5m_(up|down)_diff_[0-9]+_instant$' THEN 'regular_diff'
        WHEN code ~ '^[a-z]+_up_down_5m_(up|down)_adjusted_diff_[0-9]+_instant$' THEN 'adjusted_diff'
        WHEN code ~ '^[a-z]+_up_down_5m_(up|down)_shift_diff_[0-9]+_[0-9]+_instant$' THEN 'shift_diff'
        ELSE 'not_allowed'
    END AS allow_reason
FROM strategies
WHERE code = 'eth_up_down_5m_down_diff_3_instant';
""";
}

async Task PrintRowsAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    await using var reader = await command.ExecuteReaderAsync();
    var fieldCount = reader.FieldCount;
    var hasRows = false;

    while (await reader.ReadAsync())
    {
        hasRows = true;
        for (var index = 0; index < fieldCount; index++)
        {
            if (index > 0)
            {
                Console.Write(" | ");
            }

            Console.Write(reader.GetName(index));
            Console.Write('=');
            Console.Write(FormatValue(reader.IsDBNull(index) ? null : reader.GetValue(index)));
        }

        Console.WriteLine();
    }

    if (!hasRows)
    {
        Console.WriteLine("(no rows)");
    }
}

static string FormatValue(object? value)
{
    return value switch
    {
        null => "NULL",
        DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
        DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
        decimal number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        double number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        float number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
