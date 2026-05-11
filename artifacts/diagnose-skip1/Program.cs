using System.Globalization;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

const string StrategyCode = "btc_up_down_5m_skip_1";

var connectionFactory = new PostgresConnectionFactory(new StorageOptions());
await using var connection = connectionFactory.CreateConnection();
await connection.OpenAsync();

Console.WriteLine("utc_now=" + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

await PrintStrategyAsync(connection);
await PrintPaperOrdersAsync(connection);
await PrintRunStatusBreakdownAsync(connection);
await PrintSettledSampleAsync(connection);
await PrintRecentRunsAsync(connection);
await PrintCurrentWindowRunsAsync(connection);

static async Task PrintStrategyAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT id, code, name, enabled, live_stakes, paper_stake_amount, live_stake_amount, live_available_balance, updated_at_utc
        FROM strategies
        WHERE code = @StrategyCode;
        """,
        connection);
    command.Parameters.AddWithValue("StrategyCode", StrategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "strategy",
            "id=" + reader.GetGuid(0),
            "code=" + reader.GetString(1),
            "enabled=" + reader.GetBoolean(3),
            "live_stakes=" + reader.GetBoolean(4),
            "paper_stake_amount=" + FormatMoney(reader.GetDecimal(5)),
            "live_stake_amount=" + FormatMoney(reader.GetDecimal(6)),
            "live_available_balance=" + FormatMoney(reader.GetDecimal(7)),
            "updated=" + reader.GetFieldValue<DateTimeOffset>(8).ToString("O", CultureInfo.InvariantCulture)));
    }
}

static async Task PrintPaperOrdersAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT COUNT(*)::integer, MIN(po.created_at_utc), MAX(po.created_at_utc)
        FROM paper_orders po
        INNER JOIN strategies s ON s.id = po.strategy_id
        WHERE s.code = @StrategyCode;
        """,
        connection);
    command.Parameters.AddWithValue("StrategyCode", StrategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "paper_orders",
            "count=" + reader.GetInt32(0),
            "first=" + FormatTime(reader, 1),
            "last=" + FormatTime(reader, 2)));
    }
}

static async Task PrintRunStatusBreakdownAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT
            r.status,
            COALESCE(r.skip_reason, '') AS skip_reason,
            COUNT(*)::integer AS count,
            MIN(r.created_at_utc) AS first_created,
            MAX(r.updated_at_utc) AS last_updated
        FROM strategy_market_paper_runs r
        INNER JOIN strategies s ON s.id = r.strategy_id
        WHERE s.code = @StrategyCode
        GROUP BY r.status, COALESCE(r.skip_reason, '')
        ORDER BY COUNT(*) DESC, r.status, COALESCE(r.skip_reason, '');
        """,
        connection);
    command.Parameters.AddWithValue("StrategyCode", StrategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "run_breakdown",
            "status=" + reader.GetString(0),
            "skip_reason=" + (string.IsNullOrWhiteSpace(reader.GetString(1)) ? "-" : reader.GetString(1)),
            "count=" + reader.GetInt32(2),
            "first=" + reader.GetFieldValue<DateTimeOffset>(3).ToString("O", CultureInfo.InvariantCulture),
            "last=" + reader.GetFieldValue<DateTimeOffset>(4).ToString("O", CultureInfo.InvariantCulture)));
    }
}

static async Task PrintSettledSampleAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT
            COUNT(*)::integer AS settled,
            COUNT(*) FILTER (WHERE r.realized_pnl_usd > 0)::integer AS wins,
            COUNT(*) FILTER (WHERE r.realized_pnl_usd < 0)::integer AS losses,
            COALESCE(SUM(r.realized_pnl_usd), 0) AS pnl
        FROM strategy_market_paper_runs r
        INNER JOIN strategies s ON s.id = r.strategy_id
        WHERE s.code = @StrategyCode
          AND r.status = 'Settled'
          AND r.realized_pnl_usd IS NOT NULL;
        """,
        connection);
    command.Parameters.AddWithValue("StrategyCode", StrategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "settled_sample",
            "count=" + reader.GetInt32(0),
            "wins=" + reader.GetInt32(1),
            "losses=" + reader.GetInt32(2),
            "pnl=" + FormatMoney(reader.GetDecimal(3))));
    }
}

static async Task PrintRecentRunsAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT
            r.market_slug,
            r.market_start_utc,
            r.market_end_utc,
            r.entry_due_at_utc,
            r.status,
            r.selected_outcome,
            r.entry_price,
            r.stake_usd,
            r.size_shares,
            r.paper_order_id,
            r.entered_at_utc,
            r.skip_reason,
            LEFT(COALESCE(r.skip_diagnostics_json::text, ''), 700) AS diagnostics,
            po.status AS order_status,
            po.created_at_utc AS order_created,
            po.expires_at_utc AS order_expires
        FROM strategy_market_paper_runs r
        INNER JOIN strategies s ON s.id = r.strategy_id
        LEFT JOIN paper_orders po ON po.id = r.paper_order_id
        WHERE s.code = @StrategyCode
        ORDER BY r.created_at_utc DESC
        LIMIT 20;
        """,
        connection);
    command.Parameters.AddWithValue("StrategyCode", StrategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "recent_run",
            "market=" + reader.GetString(0),
            "start=" + FormatTime(reader, 1),
            "end=" + FormatTime(reader, 2),
            "entry_due=" + FormatTime(reader, 3),
            "status=" + reader.GetString(4),
            "outcome=" + FormatString(reader, 5),
            "entry_price=" + FormatDecimalColumn(reader, 6),
            "stake=" + FormatMoney(reader.GetDecimal(7)),
            "size=" + FormatDecimalColumn(reader, 8),
            "paper_order_id=" + FormatGuid(reader, 9),
            "entered=" + FormatTime(reader, 10),
            "skip_reason=" + FormatString(reader, 11),
            "order_status=" + FormatString(reader, 13),
            "order_created=" + FormatTime(reader, 14),
            "order_expires=" + FormatTime(reader, 15)));

        var diagnostics = reader.IsDBNull(12) ? string.Empty : reader.GetString(12);
        if (!string.IsNullOrWhiteSpace(diagnostics))
        {
            Console.WriteLine("diagnostics=" + diagnostics);
        }
    }
}

static async Task PrintCurrentWindowRunsAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT
            r.market_slug,
            r.market_start_utc,
            r.market_end_utc,
            r.entry_due_at_utc,
            r.status,
            r.selected_outcome,
            r.entry_price,
            r.paper_order_id,
            r.entered_at_utc,
            r.skip_reason,
            LEFT(COALESCE(r.skip_diagnostics_json::text, ''), 700) AS diagnostics
        FROM strategy_market_paper_runs r
        INNER JOIN strategies s ON s.id = r.strategy_id
        WHERE s.code = @StrategyCode
          AND r.market_start_utc <= now() + interval '10 minutes'
        ORDER BY r.market_start_utc DESC NULLS LAST, r.created_at_utc DESC
        LIMIT 20;
        """,
        connection);
    command.Parameters.AddWithValue("StrategyCode", StrategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "current_window_run",
            "market=" + reader.GetString(0),
            "start=" + FormatTime(reader, 1),
            "end=" + FormatTime(reader, 2),
            "entry_due=" + FormatTime(reader, 3),
            "status=" + reader.GetString(4),
            "outcome=" + FormatString(reader, 5),
            "entry_price=" + FormatDecimalColumn(reader, 6),
            "paper_order_id=" + FormatGuid(reader, 7),
            "entered=" + FormatTime(reader, 8),
            "skip_reason=" + FormatString(reader, 9)));

        var diagnostics = reader.IsDBNull(10) ? string.Empty : reader.GetString(10);
        if (!string.IsNullOrWhiteSpace(diagnostics))
        {
            Console.WriteLine("current_window_diagnostics=" + diagnostics);
        }
    }
}

static string FormatString(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? "null" : reader.GetString(ordinal);
}

static string FormatGuid(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? "null" : reader.GetGuid(ordinal).ToString();
}

static string FormatTime(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal)
        ? "null"
        : reader.GetFieldValue<DateTimeOffset>(ordinal).ToString("O", CultureInfo.InvariantCulture);
}

static string FormatDecimalColumn(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? "null" : FormatMoney(reader.GetDecimal(ordinal));
}

static string FormatMoney(decimal value)
{
    return value.ToString("0.########", CultureInfo.InvariantCulture);
}
