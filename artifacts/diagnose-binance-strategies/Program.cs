using System.Globalization;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

string[] codes =
[
    "btc_up_down_5m_binance",
    "btc_up_down_5m_binance_bps_1",
    "btc_up_down_5m_binance_bps_2",
    "btc_up_down_5m_binance_bps_5",
    "btc_up_down_5m_binance_45",
    "btc_up_down_5m_binance_47",
    "btc_up_down_5m_binance_49",
    "btc_up_down_5m_binance_clever",
    "btc_up_down_5m_binance_clever_aggressive",
    "btc_up_down_5m_binance_clever_conservative"
];

var connectionFactory = new PostgresConnectionFactory(new StorageOptions());
await using var connection = connectionFactory.CreateConnection();
await connection.OpenAsync();

Console.WriteLine("utc_now=" + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

await PrintStrategyRowsAsync(connection, codes);
await PrintClosedSummaryAsync(connection, codes);
await PrintRunBreakdownAsync(connection, codes);
await PrintOrderBreakdownAsync(connection, codes);
await PrintOutcomeSummaryAsync(connection, codes);
await PrintPriceBucketSummaryAsync(connection, codes);
await PrintHourlySummaryAsync(connection, codes);
await PrintRecentRunsAsync(connection, codes);
await PrintRecentOrdersAsync(connection, codes);
await PrintDecisionSummaryAsync(connection, codes);

static async Task PrintStrategyRowsAsync(NpgsqlConnection connection, IReadOnlyList<string> codes)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT id, code, name, enabled, live_stakes, paper_stake_amount, live_stake_amount, updated_at_utc
        FROM strategies
        WHERE code = ANY(@Codes)
        ORDER BY code;
        """,
        connection);
    command.Parameters.AddWithValue("Codes", codes.ToArray());

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "strategy",
            "id=" + reader.GetGuid(0),
            "code=" + reader.GetString(1),
            "name=\"" + reader.GetString(2) + "\"",
            "enabled=" + reader.GetBoolean(3),
            "live_stakes=" + reader.GetBoolean(4),
            "paper_stake_amount=" + FormatDecimal(reader.GetDecimal(5)),
            "live_stake_amount=" + FormatDecimal(reader.GetDecimal(6)),
            "updated=" + FormatTime(reader.GetFieldValue<DateTimeOffset>(7))));
    }
}

static async Task PrintClosedSummaryAsync(NpgsqlConnection connection, IReadOnlyList<string> codes)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                s.code,
                r.id AS run_id,
                r.market_start_utc,
                r.status,
                r.realized_pnl_usd,
                r.settlement_value_usd,
                po.id AS paper_order_id,
                po.status AS order_status,
                COALESCE(SUM(pf.size_shares), 0) AS filled_size_shares,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd,
                CASE
                    WHEN COALESCE(SUM(pf.size_shares), 0) = 0 THEN NULL
                    ELSE SUM(pf.price * pf.size_shares) / SUM(pf.size_shares)
                END AS avg_fill_price
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = ANY(@Codes)
            GROUP BY
                s.code,
                r.id,
                r.market_start_utc,
                r.status,
                r.realized_pnl_usd,
                r.settlement_value_usd,
                po.id,
                po.status
        )
        SELECT
            code,
            COUNT(*)::integer AS runs,
            COUNT(*) FILTER (WHERE paper_order_id IS NOT NULL)::integer AS orders,
            COUNT(*) FILTER (WHERE filled_size_shares > 0)::integer AS rows_with_fills,
            COUNT(*) FILTER (WHERE status = 'Settled')::integer AS settled,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd > 0)::integer AS wins,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd < 0)::integer AS losses,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd = 0)::integer AS flats,
            COALESCE(SUM(fill_cost_usd) FILTER (WHERE status = 'Settled'), 0) AS settled_fill_cost_usd,
            COALESCE(SUM(settlement_value_usd) FILTER (WHERE status = 'Settled'), 0) AS settlement_value_usd,
            COALESCE(SUM(realized_pnl_usd) FILTER (WHERE status = 'Settled'), 0) AS realized_pnl_usd,
            CASE
                WHEN COALESCE(SUM(fill_cost_usd) FILTER (WHERE status = 'Settled'), 0) = 0 THEN NULL
                ELSE COALESCE(SUM(realized_pnl_usd) FILTER (WHERE status = 'Settled'), 0)
                    / SUM(fill_cost_usd) FILTER (WHERE status = 'Settled')
            END AS roi,
            CASE
                WHEN COALESCE(SUM(filled_size_shares) FILTER (WHERE status = 'Settled'), 0) = 0 THEN NULL
                ELSE SUM(fill_cost_usd) FILTER (WHERE status = 'Settled')
                    / SUM(filled_size_shares) FILTER (WHERE status = 'Settled')
            END AS weighted_avg_fill_price,
            CASE
                WHEN COUNT(*) FILTER (WHERE status = 'Settled') = 0 THEN NULL
                ELSE COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd > 0)::numeric
                    / COUNT(*) FILTER (WHERE status = 'Settled')
            END AS win_rate,
            MIN(market_start_utc) FILTER (WHERE status = 'Settled') AS first_settled_market_start,
            MAX(market_start_utc) FILTER (WHERE status = 'Settled') AS last_settled_market_start
        FROM row_base
        GROUP BY code
        ORDER BY code;
        """,
        connection);
    command.Parameters.AddWithValue("Codes", codes.ToArray());

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "closed_summary",
            "code=" + reader.GetString(0),
            "runs=" + reader.GetInt32(1),
            "orders=" + reader.GetInt32(2),
            "rows_with_fills=" + reader.GetInt32(3),
            "settled=" + reader.GetInt32(4),
            "wins=" + reader.GetInt32(5),
            "losses=" + reader.GetInt32(6),
            "flats=" + reader.GetInt32(7),
            "settled_fill_cost_usd=" + FormatDecimal(reader.GetDecimal(8)),
            "settlement_value_usd=" + FormatDecimal(reader.GetDecimal(9)),
            "realized_pnl_usd=" + FormatDecimal(reader.GetDecimal(10)),
            "roi=" + FormatNullablePercent(reader, 11),
            "weighted_avg_fill_price=" + FormatNullableDecimal(reader, 12),
            "win_rate=" + FormatNullablePercent(reader, 13),
            "first_market=" + FormatNullableTime(reader, 14),
            "last_market=" + FormatNullableTime(reader, 15)));
    }
}

static async Task PrintRunBreakdownAsync(NpgsqlConnection connection, IReadOnlyList<string> codes)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT
            s.code,
            r.status,
            COALESCE(r.skip_reason, '') AS skip_reason,
            COUNT(*)::integer AS count,
            MIN(r.created_at_utc) AS first_created,
            MAX(r.updated_at_utc) AS last_updated
        FROM strategy_market_paper_runs r
        INNER JOIN strategies s ON s.id = r.strategy_id
        WHERE s.code = ANY(@Codes)
        GROUP BY s.code, r.status, COALESCE(r.skip_reason, '')
        ORDER BY s.code, count DESC, r.status, COALESCE(r.skip_reason, '');
        """,
        connection);
    command.Parameters.AddWithValue("Codes", codes.ToArray());

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "run_breakdown",
            "code=" + reader.GetString(0),
            "status=" + reader.GetString(1),
            "skip_reason=" + FormatBlank(reader.GetString(2)),
            "count=" + reader.GetInt32(3),
            "first=" + FormatNullableTime(reader, 4),
            "last=" + FormatNullableTime(reader, 5)));
    }
}

static async Task PrintOrderBreakdownAsync(NpgsqlConnection connection, IReadOnlyList<string> codes)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT
            s.code,
            COALESCE(po.status, '') AS order_status,
            COUNT(*)::integer AS count,
            COALESCE(SUM(po.notional_usd), 0) AS order_notional_usd,
            COALESCE(SUM(f.filled_cost_usd), 0) AS filled_cost_usd
        FROM paper_orders po
        INNER JOIN strategies s ON s.id = po.strategy_id
        LEFT JOIN LATERAL (
            SELECT SUM(pf.price * pf.size_shares) AS filled_cost_usd
            FROM paper_fills pf
            WHERE pf.paper_order_id = po.id
        ) f ON true
        WHERE s.code = ANY(@Codes)
        GROUP BY s.code, COALESCE(po.status, '')
        ORDER BY s.code, count DESC, order_status;
        """,
        connection);
    command.Parameters.AddWithValue("Codes", codes.ToArray());

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "order_breakdown",
            "code=" + reader.GetString(0),
            "order_status=" + FormatBlank(reader.GetString(1)),
            "count=" + reader.GetInt32(2),
            "order_notional=" + FormatDecimal(reader.GetDecimal(3)),
            "filled_cost=" + FormatDecimal(reader.GetDecimal(4))));
    }
}

static async Task PrintOutcomeSummaryAsync(NpgsqlConnection connection, IReadOnlyList<string> codes)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                s.code,
                COALESCE(r.selected_outcome, '') AS outcome,
                r.status,
                r.realized_pnl_usd,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = ANY(@Codes)
            GROUP BY s.code, r.id, COALESCE(r.selected_outcome, ''), r.status, r.realized_pnl_usd
        )
        SELECT
            code,
            outcome,
            COUNT(*) FILTER (WHERE status = 'Settled')::integer AS settled,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd > 0)::integer AS wins,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd < 0)::integer AS losses,
            COALESCE(SUM(fill_cost_usd) FILTER (WHERE status = 'Settled'), 0) AS cost_usd,
            COALESCE(SUM(realized_pnl_usd) FILTER (WHERE status = 'Settled'), 0) AS pnl_usd
        FROM row_base
        GROUP BY code, outcome
        ORDER BY code, outcome;
        """,
        connection);
    command.Parameters.AddWithValue("Codes", codes.ToArray());

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "outcome_summary",
            "code=" + reader.GetString(0),
            "outcome=" + FormatBlank(reader.GetString(1)),
            "settled=" + reader.GetInt32(2),
            "wins=" + reader.GetInt32(3),
            "losses=" + reader.GetInt32(4),
            "cost=" + FormatDecimal(reader.GetDecimal(5)),
            "pnl=" + FormatDecimal(reader.GetDecimal(6))));
    }
}

static async Task PrintPriceBucketSummaryAsync(NpgsqlConnection connection, IReadOnlyList<string> codes)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                s.code,
                width_bucket(r.entry_price, 0.00, 0.50, 5) AS bucket,
                r.status,
                r.realized_pnl_usd,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = ANY(@Codes)
              AND r.entry_price IS NOT NULL
            GROUP BY s.code, r.id, r.entry_price, r.status, r.realized_pnl_usd
        )
        SELECT
            code,
            bucket,
            COUNT(*) FILTER (WHERE status = 'Settled')::integer AS settled,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd > 0)::integer AS wins,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd < 0)::integer AS losses,
            COALESCE(SUM(fill_cost_usd) FILTER (WHERE status = 'Settled'), 0) AS cost_usd,
            COALESCE(SUM(realized_pnl_usd) FILTER (WHERE status = 'Settled'), 0) AS pnl_usd
        FROM row_base
        GROUP BY code, bucket
        ORDER BY code, bucket;
        """,
        connection);
    command.Parameters.AddWithValue("Codes", codes.ToArray());

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "price_bucket",
            "code=" + reader.GetString(0),
            "bucket=" + reader.GetInt32(1),
            "settled=" + reader.GetInt32(2),
            "wins=" + reader.GetInt32(3),
            "losses=" + reader.GetInt32(4),
            "cost=" + FormatDecimal(reader.GetDecimal(5)),
            "pnl=" + FormatDecimal(reader.GetDecimal(6))));
    }
}

static async Task PrintHourlySummaryAsync(NpgsqlConnection connection, IReadOnlyList<string> codes)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                s.code,
                date_trunc('hour', r.market_start_utc) AS hour_utc,
                r.status,
                r.realized_pnl_usd,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = ANY(@Codes)
              AND r.market_start_utc IS NOT NULL
            GROUP BY s.code, r.id, date_trunc('hour', r.market_start_utc), r.status, r.realized_pnl_usd
        )
        SELECT
            code,
            hour_utc,
            COUNT(*) FILTER (WHERE status = 'Settled')::integer AS settled,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd > 0)::integer AS wins,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd < 0)::integer AS losses,
            COALESCE(SUM(fill_cost_usd) FILTER (WHERE status = 'Settled'), 0) AS cost_usd,
            COALESCE(SUM(realized_pnl_usd) FILTER (WHERE status = 'Settled'), 0) AS pnl_usd
        FROM row_base
        GROUP BY code, hour_utc
        HAVING COUNT(*) FILTER (WHERE status = 'Settled') > 0
        ORDER BY code, hour_utc;
        """,
        connection);
    command.Parameters.AddWithValue("Codes", codes.ToArray());

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "hour_summary",
            "code=" + reader.GetString(0),
            "hour=" + FormatNullableTime(reader, 1),
            "settled=" + reader.GetInt32(2),
            "wins=" + reader.GetInt32(3),
            "losses=" + reader.GetInt32(4),
            "cost=" + FormatDecimal(reader.GetDecimal(5)),
            "pnl=" + FormatDecimal(reader.GetDecimal(6))));
    }
}

static async Task PrintRecentRunsAsync(NpgsqlConnection connection, IReadOnlyList<string> codes)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT *
        FROM (
            SELECT
                s.code,
                r.market_slug,
                r.market_start_utc,
                r.status,
                r.selected_outcome,
                r.entry_price,
                r.realized_pnl_usd,
                r.skip_reason,
                po.status AS order_status,
                po.price AS order_price,
                po.notional_usd AS order_notional_usd,
                COALESCE(SUM(pf.size_shares), 0) AS filled_size,
                CASE
                    WHEN COALESCE(SUM(pf.size_shares), 0) = 0 THEN NULL
                    ELSE SUM(pf.price * pf.size_shares) / SUM(pf.size_shares)
                END AS avg_fill_price,
                ROW_NUMBER() OVER (PARTITION BY s.code ORDER BY r.market_start_utc DESC NULLS LAST, r.created_at_utc DESC) AS rn
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = ANY(@Codes)
            GROUP BY
                s.code,
                r.market_slug,
                r.market_start_utc,
                r.status,
                r.selected_outcome,
                r.entry_price,
                r.realized_pnl_usd,
                r.skip_reason,
                po.status,
                po.price,
                po.notional_usd,
                r.created_at_utc
        ) rows
        WHERE rn <= 15
        ORDER BY code, rn;
        """,
        connection);
    command.Parameters.AddWithValue("Codes", codes.ToArray());

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "recent_run",
            "code=" + reader.GetString(0),
            "market=" + reader.GetString(1),
            "start=" + FormatNullableTime(reader, 2),
            "status=" + reader.GetString(3),
            "outcome=" + GetNullableString(reader, 4),
            "entry_price=" + FormatNullableDecimal(reader, 5),
            "pnl=" + FormatNullableDecimal(reader, 6),
            "skip_reason=" + GetNullableString(reader, 7),
            "order_status=" + GetNullableString(reader, 8),
            "order_price=" + FormatNullableDecimal(reader, 9),
            "order_notional=" + FormatNullableDecimal(reader, 10),
            "filled_size=" + FormatDecimal(reader.GetDecimal(11)),
            "avg_fill_price=" + FormatNullableDecimal(reader, 12)));
    }
}

static async Task PrintRecentOrdersAsync(NpgsqlConnection connection, IReadOnlyList<string> codes)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT *
        FROM (
            SELECT
                s.code,
                po.created_at_utc,
                po.expires_at_utc,
                po.status,
                po.outcome,
                po.price,
                po.notional_usd,
                po.raw_decision_json ->> 'pricing_mode' AS pricing_mode,
                po.raw_decision_json ->> 'limit_pricing_mode' AS limit_pricing_mode,
                po.raw_decision_json ->> 'decision_source' AS decision_source,
                po.raw_decision_json ->> 'btc_start_price_usd' AS start_price,
                po.raw_decision_json ->> 'btc_current_price_usd' AS current_price,
                po.raw_decision_json ->> 'btc_move_bps' AS move_bps,
                ROW_NUMBER() OVER (PARTITION BY s.code ORDER BY po.created_at_utc DESC) AS rn
            FROM paper_orders po
            INNER JOIN strategies s ON s.id = po.strategy_id
            WHERE s.code = ANY(@Codes)
        ) rows
        WHERE rn <= 12
        ORDER BY code, rn;
        """,
        connection);
    command.Parameters.AddWithValue("Codes", codes.ToArray());

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "recent_order",
            "code=" + reader.GetString(0),
            "created=" + FormatNullableTime(reader, 1),
            "expires=" + FormatNullableTime(reader, 2),
            "status=" + reader.GetString(3),
            "outcome=" + reader.GetString(4),
            "price=" + FormatDecimal(reader.GetDecimal(5)),
            "notional=" + FormatDecimal(reader.GetDecimal(6)),
            "pricing_mode=" + GetNullableString(reader, 7),
            "limit_pricing_mode=" + GetNullableString(reader, 8),
            "decision_source=" + GetNullableString(reader, 9),
            "start_price=" + GetNullableString(reader, 10),
            "current_price=" + GetNullableString(reader, 11),
            "move_bps=" + GetNullableString(reader, 12)));
    }
}

static async Task PrintDecisionSummaryAsync(NpgsqlConnection connection, IReadOnlyList<string> codes)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT
            s.code,
            COALESCE(po.raw_decision_json ->> 'pricing_mode', '') AS pricing_mode,
            COALESCE(po.raw_decision_json ->> 'limit_pricing_mode', '') AS limit_pricing_mode,
            COALESCE(po.raw_decision_json ->> 'decision_source', '') AS decision_source,
            COUNT(*)::integer AS count
        FROM paper_orders po
        INNER JOIN strategies s ON s.id = po.strategy_id
        WHERE s.code = ANY(@Codes)
        GROUP BY
            s.code,
            COALESCE(po.raw_decision_json ->> 'pricing_mode', ''),
            COALESCE(po.raw_decision_json ->> 'limit_pricing_mode', ''),
            COALESCE(po.raw_decision_json ->> 'decision_source', '')
        ORDER BY s.code, count DESC;
        """,
        connection);
    command.Parameters.AddWithValue("Codes", codes.ToArray());

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "decision_summary",
            "code=" + reader.GetString(0),
            "pricing_mode=" + FormatBlank(reader.GetString(1)),
            "limit_pricing_mode=" + FormatBlank(reader.GetString(2)),
            "decision_source=" + FormatBlank(reader.GetString(3)),
            "count=" + reader.GetInt32(4)));
    }
}

static string GetNullableString(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? "null" : reader.GetString(ordinal);
}

static string FormatNullableTime(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal)
        ? "null"
        : FormatTime(reader.GetFieldValue<DateTimeOffset>(ordinal));
}

static string FormatTime(DateTimeOffset value)
{
    return value.ToString("O", CultureInfo.InvariantCulture);
}

static string FormatNullableDecimal(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? "null" : FormatDecimal(reader.GetDecimal(ordinal));
}

static string FormatNullablePercent(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? "null" : FormatPercent(reader.GetDecimal(ordinal));
}

static string FormatPercent(decimal value)
{
    return (value * 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}

static string FormatDecimal(decimal value)
{
    return value.ToString("0.########", CultureInfo.InvariantCulture);
}

static string FormatBlank(string value)
{
    return string.IsNullOrWhiteSpace(value) ? "-" : value;
}
