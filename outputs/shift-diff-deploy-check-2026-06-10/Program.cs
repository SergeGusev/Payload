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
    CommandTimeout = 120
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await ExecuteNonQueryAsync("SET TRANSACTION READ ONLY");

Console.WriteLine("== Database ==");
await PrintRowsAsync("""
SELECT
    now() AT TIME ZONE 'UTC' AS db_now_utc,
    current_database() AS database_name;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff summary ==");
await PrintRowsAsync("""
WITH shift AS (
    SELECT
        id,
        code,
        name,
        enabled,
        live_stakes,
        paper_stake_amount,
        created_at_utc,
        updated_at_utc,
        regexp_match(code, '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_([1-6])_([1-9]|1[0-2])_instant$') AS parts
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    count(*) AS total_shift_diff,
    count(DISTINCT id) AS distinct_ids,
    count(DISTINCT code) AS distinct_codes,
    count(*) FILTER (WHERE parts IS NULL) AS invalid_code_shape,
    count(*) FILTER (WHERE enabled) AS enabled_count,
    count(*) FILTER (WHERE live_stakes) AS live_count,
    min(created_at_utc) AS first_created_utc,
    max(updated_at_utc) AS last_updated_utc
FROM shift;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff by asset/direction ==");
await PrintRowsAsync("""
WITH shift AS (
    SELECT regexp_match(code, '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_([1-6])_([1-9]|1[0-2])_instant$') AS parts
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    upper(parts[1]) AS asset,
    upper(parts[2]) AS direction,
    count(*) AS count
FROM shift
WHERE parts IS NOT NULL
GROUP BY upper(parts[1]), upper(parts[2])
ORDER BY asset, direction;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff by shift N ==");
await PrintRowsAsync("""
WITH shift AS (
    SELECT regexp_match(code, '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_([1-6])_([1-9]|1[0-2])_instant$') AS parts
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    parts[3]::int AS shift_n,
    count(*) AS count
FROM shift
WHERE parts IS NOT NULL
GROUP BY parts[3]::int
ORDER BY shift_n;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff by threshold M ==");
await PrintRowsAsync("""
WITH shift AS (
    SELECT regexp_match(code, '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_([1-6])_([1-9]|1[0-2])_instant$') AS parts
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    parts[4]::int AS threshold_m,
    count(*) AS count
FROM shift
WHERE parts IS NOT NULL
GROUP BY parts[4]::int
ORDER BY threshold_m;
""");

Console.WriteLine();
Console.WriteLine("== Sample strategy ==");
await PrintRowsAsync("""
SELECT
    id,
    code,
    name,
    enabled,
    live_stakes,
    paper_stake_amount,
    created_at_utc,
    updated_at_utc
FROM strategies
WHERE code = 'btc_up_down_5m_up_shift_diff_2_4_instant';
""");

Console.WriteLine();
Console.WriteLine("== Enabled strategy counts ==");
await PrintRowsAsync("""
SELECT
    count(*) AS total_strategies,
    count(*) FILTER (WHERE enabled) AS enabled_total,
    count(*) FILTER (WHERE enabled AND code LIKE '%_shift_diff_%') AS enabled_shift_diff,
    count(*) FILTER (WHERE enabled AND live_stakes) AS enabled_live,
    count(*) FILTER (WHERE enabled AND code LIKE '%_diff_%' AND code NOT LIKE '%_shift_diff_%') AS enabled_diff_adjusted
FROM strategies;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff paper runs ==");
await PrintRowsAsync("""
WITH shift_ids AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    count(*) AS total_runs,
    count(DISTINCT run.market_start_utc) AS distinct_markets,
    count(*) FILTER (WHERE run.status = 'PendingEntry') AS pending_entry,
    count(*) FILTER (WHERE run.status = 'Entered') AS entered,
    count(*) FILTER (WHERE run.status = 'Skipped') AS skipped,
    count(*) FILTER (WHERE run.status = 'Settled') AS settled,
    min(run.created_at_utc) AS first_run_created_utc,
    max(run.created_at_utc) AS last_run_created_utc,
    max(run.updated_at_utc) AS last_run_updated_utc
FROM strategy_market_paper_runs run
WHERE run.strategy_id IN (SELECT id FROM shift_ids);
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff status breakdown ==");
await PrintRowsAsync("""
WITH shift_ids AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    run.status,
    count(*) AS count,
    min(run.market_start_utc) AS first_market_start_utc,
    max(run.market_start_utc) AS last_market_start_utc,
    max(run.updated_at_utc) AS last_updated_utc
FROM strategy_market_paper_runs run
WHERE run.strategy_id IN (SELECT id FROM shift_ids)
GROUP BY run.status
ORDER BY run.status;
""");

Console.WriteLine();
Console.WriteLine("== Recent ShiftDiff paper run markets ==");
await PrintRowsAsync("""
WITH shift_ids AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    run.market_start_utc,
    count(*) AS run_count,
    count(*) FILTER (WHERE run.status = 'PendingEntry') AS pending_entry,
    count(*) FILTER (WHERE run.status = 'Entered') AS entered,
    count(*) FILTER (WHERE run.status = 'Skipped') AS skipped,
    count(*) FILTER (WHERE run.status = 'Settled') AS settled,
    max(run.updated_at_utc) AS last_updated_utc
FROM strategy_market_paper_runs run
WHERE run.strategy_id IN (SELECT id FROM shift_ids)
GROUP BY run.market_start_utc
ORDER BY run.market_start_utc DESC NULLS LAST
LIMIT 5;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff entered outcome check ==");
await PrintRowsAsync("""
WITH shift_strategies AS MATERIALIZED (
    SELECT id, code
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
),
entered AS (
    SELECT
        regexp_match(strategy.code, '^(btc|eth|sol)_up_down_5m_(up|down)_shift_diff_([1-6])_([1-9]|1[0-2])_instant$') AS parts,
        run.selected_outcome
    FROM strategy_market_paper_runs run
    INNER JOIN shift_strategies strategy ON strategy.id = run.strategy_id
    WHERE run.status IN ('Entered', 'Settled')
)
SELECT
    upper(parts[2]) AS diff_direction,
    selected_outcome,
    count(*) AS count
FROM entered
WHERE parts IS NOT NULL
GROUP BY upper(parts[2]), selected_outcome
ORDER BY diff_direction, selected_outcome;
""");

Console.WriteLine();
Console.WriteLine("== ShiftDiff paper orders ==");
await PrintRowsAsync("""
WITH shift_ids AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code LIKE '%_shift_diff_%'
)
SELECT
    count(*) AS paper_orders,
    min(order_row.created_at_utc) AS first_order_created_utc,
    max(order_row.created_at_utc) AS last_order_created_utc
FROM paper_orders order_row
WHERE order_row.strategy_id IN (SELECT id FROM shift_ids);
""");

Console.WriteLine();
Console.WriteLine("== Service heartbeats ==");
await PrintRowsAsync("""
SELECT
    service_name,
    status,
    mode,
    version,
    started_at_utc,
    last_heartbeat_utc,
    round(extract(epoch FROM ((now() AT TIME ZONE 'UTC') - (last_heartbeat_utc AT TIME ZONE 'UTC'))))::int AS heartbeat_age_seconds,
    current_loop,
    left(coalesce(last_error, ''), 240) AS last_error_prefix
FROM service_heartbeats
ORDER BY service_name;
""");

Console.WriteLine();
Console.WriteLine("== Recent activity ==");
await PrintRowsAsync("""
SELECT 'strategy_market_paper_runs.updated' AS metric, max(updated_at_utc) AS last_utc FROM strategy_market_paper_runs
UNION ALL
SELECT 'paper_orders.created', max(created_at_utc) FROM paper_orders
UNION ALL
SELECT 'live_orders.updated', max(updated_at_utc) FROM live_orders
UNION ALL
SELECT 'crypto_diff_snapshots.sampled', max(sampled_at_utc) FROM crypto_up_down_5m_diff_snapshots
ORDER BY metric;
""");

await transaction.CommitAsync();

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    await command.ExecuteNonQueryAsync();
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
