using System.Globalization;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

var targetCode = args.Length > 0 ? args[0] : "btc_up_down_5m_less_30";

var connectionFactory = new PostgresConnectionFactory(new StorageOptions());
await using var connection = connectionFactory.CreateConnection();
await connection.OpenAsync();

Console.WriteLine("utc_now=" + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

await PrintStrategyAsync(connection, targetCode);
await PrintClosedSummaryAsync(connection, targetCode);
await PrintRiskStatsAsync(connection, targetCode);
await PrintRollingSummaryAsync(connection, targetCode);
await PrintEntryPriceBandSummaryAsync(connection, targetCode);
await PrintTopPnLAsync(connection, targetCode);
await PrintSiblingSummaryAsync(connection);
await PrintRunBreakdownAsync(connection, targetCode);
await PrintOutcomeSummaryAsync(connection, targetCode);
await PrintEntryPriceBucketsAsync(connection, targetCode);
await PrintHourlySummaryAsync(connection, targetCode);
await PrintTimingSummaryAsync(connection, targetCode);
await PrintQuoteSourceSummaryAsync(connection, targetCode);
await PrintRecentSettledAsync(connection, targetCode);
await PrintRecentDecisionJsonAsync(connection, targetCode);

static async Task PrintStrategyAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT id, code, name, enabled, live_stakes, paper_stake_amount, live_stake_amount, updated_at_utc
        FROM strategies
        WHERE code = @Code;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

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
            "updated=" + reader.GetFieldValue<DateTimeOffset>(7).ToString("O", CultureInfo.InvariantCulture)));
    }
}

static async Task PrintClosedSummaryAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                r.market_id,
                r.market_slug,
                r.market_start_utc,
                r.status,
                r.selected_outcome,
                r.entry_price,
                r.stake_usd,
                r.size_shares AS run_size_shares,
                r.settlement_value_usd,
                r.realized_pnl_usd,
                po.id AS paper_order_id,
                po.status AS order_status,
                po.price AS order_price,
                po.notional_usd AS order_notional_usd,
                COALESCE(SUM(pf.size_shares), 0) AS filled_size_shares,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd,
                CASE
                    WHEN COALESCE(SUM(pf.size_shares), 0) = 0 THEN NULL
                    ELSE SUM(pf.price * pf.size_shares) / SUM(pf.size_shares)
                END AS avg_fill_price,
                MIN(pf.filled_at_utc) AS first_fill_at_utc,
                MAX(pf.filled_at_utc) AS last_fill_at_utc
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = @Code
            GROUP BY
                r.market_id,
                r.market_slug,
                r.market_start_utc,
                r.status,
                r.selected_outcome,
                r.entry_price,
                r.stake_usd,
                r.size_shares,
                r.settlement_value_usd,
                r.realized_pnl_usd,
                po.id,
                po.status,
                po.price,
                po.notional_usd
        )
        SELECT
            COUNT(*) FILTER (WHERE status = 'Settled')::integer AS settled,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd > 0)::integer AS wins,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd < 0)::integer AS losses,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd = 0)::integer AS flats,
            COUNT(*) FILTER (WHERE filled_size_shares > 0)::integer AS rows_with_fills,
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
        FROM row_base;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "closed_summary",
            "settled=" + reader.GetInt32(0),
            "wins=" + reader.GetInt32(1),
            "losses=" + reader.GetInt32(2),
            "flats=" + reader.GetInt32(3),
            "rows_with_fills=" + reader.GetInt32(4),
            "settled_fill_cost_usd=" + FormatDecimal(reader.GetDecimal(5)),
            "settlement_value_usd=" + FormatDecimal(reader.GetDecimal(6)),
            "realized_pnl_usd=" + FormatDecimal(reader.GetDecimal(7)),
            "roi=" + FormatNullablePercent(reader, 8),
            "weighted_avg_fill_price=" + FormatNullableDecimal(reader, 9),
            "win_rate=" + FormatNullablePercent(reader, 10),
            "first_market=" + FormatNullableTime(reader, 11),
            "last_market=" + FormatNullableTime(reader, 12)));
    }
}

static async Task PrintRiskStatsAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                r.id,
                r.realized_pnl_usd,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd,
                COALESCE(SUM(pf.size_shares), 0) AS filled_size_shares,
                CASE
                    WHEN COALESCE(SUM(pf.size_shares), 0) = 0 THEN NULL
                    ELSE SUM(pf.price * pf.size_shares) / SUM(pf.size_shares)
                END AS avg_fill_price
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = @Code
              AND r.status = 'Settled'
            GROUP BY r.id, r.realized_pnl_usd
        )
        SELECT
            COUNT(*)::integer AS settled,
            AVG(realized_pnl_usd) AS avg_pnl,
            COALESCE(STDDEV_SAMP(realized_pnl_usd), 0) AS stdev_pnl,
            MIN(realized_pnl_usd) AS min_pnl,
            MAX(realized_pnl_usd) AS max_pnl,
            COALESCE(SUM(realized_pnl_usd) FILTER (WHERE realized_pnl_usd > 0), 0) AS gross_profit,
            COALESCE(-SUM(realized_pnl_usd) FILTER (WHERE realized_pnl_usd < 0), 0) AS gross_loss_abs,
            AVG(realized_pnl_usd) FILTER (WHERE realized_pnl_usd > 0) AS avg_win,
            AVG(realized_pnl_usd) FILTER (WHERE realized_pnl_usd < 0) AS avg_loss,
            CASE
                WHEN COALESCE(-SUM(realized_pnl_usd) FILTER (WHERE realized_pnl_usd < 0), 0) = 0 THEN NULL
                ELSE COALESCE(SUM(realized_pnl_usd) FILTER (WHERE realized_pnl_usd > 0), 0)
                    / (-SUM(realized_pnl_usd) FILTER (WHERE realized_pnl_usd < 0))
            END AS profit_factor,
            MIN(avg_fill_price) AS min_avg_fill_price,
            AVG(avg_fill_price) AS simple_avg_fill_price,
            MAX(avg_fill_price) AS max_avg_fill_price
        FROM row_base;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "risk_stats",
            "settled=" + reader.GetInt32(0),
            "avg_pnl=" + FormatNullableDecimal(reader, 1),
            "stdev_pnl=" + FormatNullableDecimal(reader, 2),
            "min_pnl=" + FormatNullableDecimal(reader, 3),
            "max_pnl=" + FormatNullableDecimal(reader, 4),
            "gross_profit=" + FormatNullableDecimal(reader, 5),
            "gross_loss_abs=" + FormatNullableDecimal(reader, 6),
            "avg_win=" + FormatNullableDecimal(reader, 7),
            "avg_loss=" + FormatNullableDecimal(reader, 8),
            "profit_factor=" + FormatNullableDecimal(reader, 9),
            "min_avg_fill_price=" + FormatNullableDecimal(reader, 10),
            "simple_avg_fill_price=" + FormatNullableDecimal(reader, 11),
            "max_avg_fill_price=" + FormatNullableDecimal(reader, 12)));
    }
}

static async Task PrintRollingSummaryAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                r.market_start_utc,
                r.realized_pnl_usd,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd,
                ROW_NUMBER() OVER (ORDER BY r.market_start_utc DESC) AS rn
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = @Code
              AND r.status = 'Settled'
            GROUP BY r.id, r.market_start_utc, r.realized_pnl_usd
        ),
        windows AS (
            SELECT 20 AS window_size
            UNION ALL SELECT 50
            UNION ALL SELECT 100
        )
        SELECT
            w.window_size,
            COUNT(*)::integer AS settled,
            COUNT(*) FILTER (WHERE rb.realized_pnl_usd > 0)::integer AS wins,
            COUNT(*) FILTER (WHERE rb.realized_pnl_usd < 0)::integer AS losses,
            COALESCE(SUM(rb.fill_cost_usd), 0) AS cost_usd,
            COALESCE(SUM(rb.realized_pnl_usd), 0) AS pnl_usd,
            CASE
                WHEN COALESCE(SUM(rb.fill_cost_usd), 0) = 0 THEN NULL
                ELSE COALESCE(SUM(rb.realized_pnl_usd), 0) / SUM(rb.fill_cost_usd)
            END AS roi,
            MIN(rb.market_start_utc) AS first_market,
            MAX(rb.market_start_utc) AS last_market
        FROM windows w
        LEFT JOIN row_base rb ON rb.rn <= w.window_size
        GROUP BY w.window_size
        ORDER BY w.window_size;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "rolling_summary",
            "last=" + reader.GetInt32(0),
            "settled=" + reader.GetInt32(1),
            "wins=" + reader.GetInt32(2),
            "losses=" + reader.GetInt32(3),
            "cost=" + FormatDecimal(reader.GetDecimal(4)),
            "pnl=" + FormatDecimal(reader.GetDecimal(5)),
            "roi=" + FormatNullablePercent(reader, 6),
            "first=" + FormatNullableTime(reader, 7),
            "last_market=" + FormatNullableTime(reader, 8)));
    }
}

static async Task PrintEntryPriceBandSummaryAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                CASE
                    WHEN r.entry_price < 0.60 THEN '0.50-0.59'
                    WHEN r.entry_price < 0.70 THEN '0.60-0.69'
                    WHEN r.entry_price < 0.80 THEN '0.70-0.79'
                    WHEN r.entry_price < 0.90 THEN '0.80-0.89'
                    ELSE '0.90-1.00'
                END AS price_band,
                r.status,
                r.realized_pnl_usd,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd,
                COALESCE(SUM(pf.size_shares), 0) AS filled_size_shares
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = @Code
              AND r.entry_price IS NOT NULL
            GROUP BY r.id, r.entry_price, r.status, r.realized_pnl_usd
        )
        SELECT
            price_band,
            COUNT(*) FILTER (WHERE status = 'Settled')::integer AS settled,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd > 0)::integer AS wins,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd < 0)::integer AS losses,
            COALESCE(SUM(fill_cost_usd) FILTER (WHERE status = 'Settled'), 0) AS cost_usd,
            COALESCE(SUM(realized_pnl_usd) FILTER (WHERE status = 'Settled'), 0) AS pnl_usd,
            CASE
                WHEN COALESCE(SUM(fill_cost_usd) FILTER (WHERE status = 'Settled'), 0) = 0 THEN NULL
                ELSE COALESCE(SUM(realized_pnl_usd) FILTER (WHERE status = 'Settled'), 0)
                    / SUM(fill_cost_usd) FILTER (WHERE status = 'Settled')
            END AS roi
        FROM row_base
        GROUP BY price_band
        ORDER BY price_band;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "entry_price_band",
            "band=" + reader.GetString(0),
            "settled=" + reader.GetInt32(1),
            "wins=" + reader.GetInt32(2),
            "losses=" + reader.GetInt32(3),
            "cost=" + FormatDecimal(reader.GetDecimal(4)),
            "pnl=" + FormatDecimal(reader.GetDecimal(5)),
            "roi=" + FormatNullablePercent(reader, 6)));
    }
}

static async Task PrintTopPnLAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                r.market_slug,
                r.market_start_utc,
                r.selected_outcome,
                r.entry_price,
                r.realized_pnl_usd,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = @Code
              AND r.status = 'Settled'
            GROUP BY r.id, r.market_slug, r.market_start_utc, r.selected_outcome, r.entry_price, r.realized_pnl_usd
        )
        SELECT 'best' AS side, market_slug, market_start_utc, selected_outcome, entry_price, fill_cost_usd, realized_pnl_usd
        FROM row_base
        ORDER BY realized_pnl_usd DESC
        LIMIT 5;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "top_pnl",
            "side=" + reader.GetString(0),
            "market=" + reader.GetString(1),
            "start=" + FormatNullableTime(reader, 2),
            "outcome=" + reader.GetString(3),
            "entry_price=" + FormatNullableDecimal(reader, 4),
            "cost=" + FormatDecimal(reader.GetDecimal(5)),
            "pnl=" + FormatNullableDecimal(reader, 6)));
    }
    await reader.DisposeAsync();

    await using var lossCommand = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                r.market_slug,
                r.market_start_utc,
                r.selected_outcome,
                r.entry_price,
                r.realized_pnl_usd,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = @Code
              AND r.status = 'Settled'
            GROUP BY r.id, r.market_slug, r.market_start_utc, r.selected_outcome, r.entry_price, r.realized_pnl_usd
        )
        SELECT 'worst' AS side, market_slug, market_start_utc, selected_outcome, entry_price, fill_cost_usd, realized_pnl_usd
        FROM row_base
        ORDER BY realized_pnl_usd ASC
        LIMIT 5;
        """,
        connection);
    lossCommand.Parameters.AddWithValue("Code", strategyCode);

    await using var lossReader = await lossCommand.ExecuteReaderAsync();
    while (await lossReader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "top_pnl",
            "side=" + lossReader.GetString(0),
            "market=" + lossReader.GetString(1),
            "start=" + FormatNullableTime(lossReader, 2),
            "outcome=" + lossReader.GetString(3),
            "entry_price=" + FormatNullableDecimal(lossReader, 4),
            "cost=" + FormatDecimal(lossReader.GetDecimal(5)),
            "pnl=" + FormatNullableDecimal(lossReader, 6)));
    }
}

static async Task PrintSiblingSummaryAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                s.code,
                s.name,
                r.status,
                r.realized_pnl_usd,
                r.settlement_value_usd,
                COALESCE(SUM(pf.size_shares), 0) AS filled_size_shares,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code ~ '^btc_up_down_5m_(less|more)_[0-9]+$'
            GROUP BY s.code, s.name, r.id, r.status, r.realized_pnl_usd, r.settlement_value_usd
        )
        SELECT
            code,
            name,
            COUNT(*) FILTER (WHERE status = 'Settled')::integer AS settled,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd > 0)::integer AS wins,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd < 0)::integer AS losses,
            COALESCE(SUM(fill_cost_usd) FILTER (WHERE status = 'Settled'), 0) AS cost_usd,
            COALESCE(SUM(realized_pnl_usd) FILTER (WHERE status = 'Settled'), 0) AS pnl_usd,
            CASE
                WHEN COALESCE(SUM(fill_cost_usd) FILTER (WHERE status = 'Settled'), 0) = 0 THEN NULL
                ELSE COALESCE(SUM(realized_pnl_usd) FILTER (WHERE status = 'Settled'), 0)
                    / SUM(fill_cost_usd) FILTER (WHERE status = 'Settled')
            END AS roi,
            CASE
                WHEN COUNT(*) FILTER (WHERE status = 'Settled') = 0 THEN NULL
                ELSE COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd > 0)::numeric
                    / COUNT(*) FILTER (WHERE status = 'Settled')
            END AS win_rate
        FROM row_base
        GROUP BY code, name
        ORDER BY pnl_usd DESC, code;
        """,
        connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "sibling_summary",
            "code=" + reader.GetString(0),
            "settled=" + reader.GetInt32(2),
            "wins=" + reader.GetInt32(3),
            "losses=" + reader.GetInt32(4),
            "cost=" + FormatDecimal(reader.GetDecimal(5)),
            "pnl=" + FormatDecimal(reader.GetDecimal(6)),
            "roi=" + FormatNullablePercent(reader, 7),
            "win_rate=" + FormatNullablePercent(reader, 8)));
    }
}

static async Task PrintRunBreakdownAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT
            r.status,
            COALESCE(r.skip_reason, '') AS skip_reason,
            COALESCE(po.status, '') AS order_status,
            COUNT(*)::integer AS count,
            MIN(r.created_at_utc) AS first_created,
            MAX(r.updated_at_utc) AS last_updated
        FROM strategy_market_paper_runs r
        INNER JOIN strategies s ON s.id = r.strategy_id
        LEFT JOIN paper_orders po ON po.id = r.paper_order_id
        WHERE s.code = @Code
        GROUP BY r.status, COALESCE(r.skip_reason, ''), COALESCE(po.status, '')
        ORDER BY count DESC, r.status, COALESCE(r.skip_reason, '');
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "run_breakdown",
            "run_status=" + reader.GetString(0),
            "skip_reason=" + (string.IsNullOrWhiteSpace(reader.GetString(1)) ? "-" : reader.GetString(1)),
            "order_status=" + (string.IsNullOrWhiteSpace(reader.GetString(2)) ? "-" : reader.GetString(2)),
            "count=" + reader.GetInt32(3),
            "first=" + FormatNullableTime(reader, 4),
            "last=" + FormatNullableTime(reader, 5)));
    }
}

static async Task PrintOutcomeSummaryAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                r.selected_outcome,
                r.status,
                r.realized_pnl_usd,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = @Code
            GROUP BY r.id, r.selected_outcome, r.status, r.realized_pnl_usd
        )
        SELECT
            COALESCE(selected_outcome, '') AS outcome,
            COUNT(*) FILTER (WHERE status = 'Settled')::integer AS settled,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd > 0)::integer AS wins,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd < 0)::integer AS losses,
            COALESCE(SUM(fill_cost_usd) FILTER (WHERE status = 'Settled'), 0) AS cost_usd,
            COALESCE(SUM(realized_pnl_usd) FILTER (WHERE status = 'Settled'), 0) AS pnl_usd
        FROM row_base
        GROUP BY COALESCE(selected_outcome, '')
        ORDER BY outcome;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "outcome_summary",
            "outcome=" + (string.IsNullOrWhiteSpace(reader.GetString(0)) ? "-" : reader.GetString(0)),
            "settled=" + reader.GetInt32(1),
            "wins=" + reader.GetInt32(2),
            "losses=" + reader.GetInt32(3),
            "cost=" + FormatDecimal(reader.GetDecimal(4)),
            "pnl=" + FormatDecimal(reader.GetDecimal(5))));
    }
}

static async Task PrintEntryPriceBucketsAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                width_bucket(r.entry_price, 0.00, 0.50, 5) AS bucket,
                r.status,
                r.realized_pnl_usd,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd,
                COALESCE(SUM(pf.size_shares), 0) AS filled_size_shares
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = @Code
              AND r.entry_price IS NOT NULL
            GROUP BY r.id, r.entry_price, r.status, r.realized_pnl_usd
        )
        SELECT
            bucket,
            COUNT(*) FILTER (WHERE status = 'Settled')::integer AS settled,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd > 0)::integer AS wins,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd < 0)::integer AS losses,
            COALESCE(SUM(fill_cost_usd) FILTER (WHERE status = 'Settled'), 0) AS cost_usd,
            COALESCE(SUM(realized_pnl_usd) FILTER (WHERE status = 'Settled'), 0) AS pnl_usd,
            CASE
                WHEN COALESCE(SUM(filled_size_shares) FILTER (WHERE status = 'Settled'), 0) = 0 THEN NULL
                ELSE SUM(fill_cost_usd) FILTER (WHERE status = 'Settled')
                    / SUM(filled_size_shares) FILTER (WHERE status = 'Settled')
            END AS avg_price
        FROM row_base
        GROUP BY bucket
        ORDER BY bucket;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "price_bucket",
            "bucket=" + reader.GetInt32(0),
            "settled=" + reader.GetInt32(1),
            "wins=" + reader.GetInt32(2),
            "losses=" + reader.GetInt32(3),
            "cost=" + FormatDecimal(reader.GetDecimal(4)),
            "pnl=" + FormatDecimal(reader.GetDecimal(5)),
            "avg_price=" + FormatNullableDecimal(reader, 6)));
    }
}

static async Task PrintHourlySummaryAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        WITH row_base AS (
            SELECT
                date_trunc('hour', r.market_start_utc) AS hour_utc,
                r.status,
                r.realized_pnl_usd,
                COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost_usd
            FROM strategy_market_paper_runs r
            INNER JOIN strategies s ON s.id = r.strategy_id
            LEFT JOIN paper_orders po ON po.id = r.paper_order_id
            LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
            WHERE s.code = @Code
              AND r.market_start_utc IS NOT NULL
            GROUP BY r.id, date_trunc('hour', r.market_start_utc), r.status, r.realized_pnl_usd
        )
        SELECT
            hour_utc,
            COUNT(*) FILTER (WHERE status = 'Settled')::integer AS settled,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd > 0)::integer AS wins,
            COUNT(*) FILTER (WHERE status = 'Settled' AND realized_pnl_usd < 0)::integer AS losses,
            COALESCE(SUM(fill_cost_usd) FILTER (WHERE status = 'Settled'), 0) AS cost_usd,
            COALESCE(SUM(realized_pnl_usd) FILTER (WHERE status = 'Settled'), 0) AS pnl_usd
        FROM row_base
        GROUP BY hour_utc
        ORDER BY hour_utc;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "hour_summary",
            "hour=" + FormatNullableTime(reader, 0),
            "settled=" + reader.GetInt32(1),
            "wins=" + reader.GetInt32(2),
            "losses=" + reader.GetInt32(3),
            "cost=" + FormatDecimal(reader.GetDecimal(4)),
            "pnl=" + FormatDecimal(reader.GetDecimal(5))));
    }
}

static async Task PrintTimingSummaryAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT
            COUNT(*)::integer,
            MIN(EXTRACT(EPOCH FROM (po.created_at_utc - r.market_start_utc))) AS min_start_offset_sec,
            percentile_cont(0.5) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM (po.created_at_utc - r.market_start_utc))) AS median_start_offset_sec,
            MAX(EXTRACT(EPOCH FROM (po.created_at_utc - r.market_start_utc))) AS max_start_offset_sec,
            MIN(EXTRACT(EPOCH FROM (po.created_at_utc - r.entry_due_at_utc))) AS min_due_delay_sec,
            percentile_cont(0.5) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM (po.created_at_utc - r.entry_due_at_utc))) AS median_due_delay_sec,
            MAX(EXTRACT(EPOCH FROM (po.created_at_utc - r.entry_due_at_utc))) AS max_due_delay_sec
        FROM strategy_market_paper_runs r
        INNER JOIN strategies s ON s.id = r.strategy_id
        INNER JOIN paper_orders po ON po.id = r.paper_order_id
        WHERE s.code = @Code
          AND r.market_start_utc IS NOT NULL
          AND r.entry_due_at_utc IS NOT NULL;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "timing_summary",
            "orders=" + reader.GetInt32(0),
            "min_start_offset_sec=" + FormatNullableDecimal(reader, 1),
            "median_start_offset_sec=" + FormatNullableDouble(reader, 2),
            "max_start_offset_sec=" + FormatNullableDecimal(reader, 3),
            "min_due_delay_sec=" + FormatNullableDecimal(reader, 4),
            "median_due_delay_sec=" + FormatNullableDouble(reader, 5),
            "max_due_delay_sec=" + FormatNullableDecimal(reader, 6)));
    }
}

static async Task PrintQuoteSourceSummaryAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT
            COALESCE(po.raw_decision_json ->> 'source', '') AS source,
            COUNT(*)::integer AS count,
            COUNT(*) FILTER (WHERE (po.raw_decision_json ->> 'quote_age_ms')::numeric < 0)::integer AS negative_quote_age_count
        FROM paper_orders po
        INNER JOIN strategies s ON s.id = po.strategy_id
        WHERE s.code = @Code
        GROUP BY COALESCE(po.raw_decision_json ->> 'source', '')
        ORDER BY count DESC, source;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "quote_source_summary",
            "source=" + (string.IsNullOrWhiteSpace(reader.GetString(0)) ? "-" : reader.GetString(0)),
            "count=" + reader.GetInt32(1),
            "negative_quote_age_count=" + reader.GetInt32(2)));
    }
}

static async Task PrintRecentSettledAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT
            r.market_slug,
            r.market_start_utc,
            r.selected_outcome,
            r.entry_price,
            r.settlement_price,
            r.settlement_value_usd,
            r.realized_pnl_usd,
            po.status AS order_status,
            COALESCE(SUM(pf.size_shares), 0) AS filled_size,
            CASE
                WHEN COALESCE(SUM(pf.size_shares), 0) = 0 THEN NULL
                ELSE SUM(pf.price * pf.size_shares) / SUM(pf.size_shares)
            END AS avg_fill_price,
            COALESCE(SUM(pf.price * pf.size_shares), 0) AS fill_cost
        FROM strategy_market_paper_runs r
        INNER JOIN strategies s ON s.id = r.strategy_id
        LEFT JOIN paper_orders po ON po.id = r.paper_order_id
        LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
        WHERE s.code = @Code
          AND r.status = 'Settled'
        GROUP BY
            r.market_slug,
            r.market_start_utc,
            r.selected_outcome,
            r.entry_price,
            r.settlement_price,
            r.settlement_value_usd,
            r.realized_pnl_usd,
            po.status
        ORDER BY r.market_start_utc DESC
        LIMIT 20;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "recent_settled",
            "market=" + reader.GetString(0),
            "start=" + FormatNullableTime(reader, 1),
            "outcome=" + reader.GetString(2),
            "entry_price=" + FormatNullableDecimal(reader, 3),
            "settlement_price=" + FormatNullableDecimal(reader, 4),
            "settlement_value=" + FormatNullableDecimal(reader, 5),
            "pnl=" + FormatNullableDecimal(reader, 6),
            "order_status=" + GetNullableString(reader, 7),
            "filled_size=" + FormatDecimal(reader.GetDecimal(8)),
            "avg_fill_price=" + FormatNullableDecimal(reader, 9),
            "fill_cost=" + FormatDecimal(reader.GetDecimal(10))));
    }
}

static async Task PrintRecentDecisionJsonAsync(NpgsqlConnection connection, string strategyCode)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT
            po.created_at_utc,
            po.outcome,
            po.price,
            po.notional_usd,
            po.raw_decision_json ->> 'outcome_selection_source' AS selection_source,
            po.raw_decision_json ->> 'order_execution_mode' AS execution_mode,
            po.raw_decision_json ->> 'selected_entry_price' AS selected_entry_price,
            po.raw_decision_json ->> 'actual_paper_notional_usd' AS actual_notional,
            po.raw_decision_json ->> 'min_order_size' AS min_order_size,
            po.raw_decision_json ->> 'stake_notional_rounding_mode' AS rounding_mode,
            po.raw_decision_json ->> 'source' AS quote_source,
            po.raw_decision_json ->> 'quote_age_ms' AS quote_age_ms
        FROM paper_orders po
        INNER JOIN strategies s ON s.id = po.strategy_id
        WHERE s.code = @Code
        ORDER BY po.created_at_utc DESC
        LIMIT 10;
        """,
        connection);
    command.Parameters.AddWithValue("Code", strategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(
            " ",
            "decision",
            "created=" + FormatNullableTime(reader, 0),
            "outcome=" + reader.GetString(1),
            "order_price=" + FormatDecimal(reader.GetDecimal(2)),
            "order_notional=" + FormatDecimal(reader.GetDecimal(3)),
            "selection_source=" + GetNullableString(reader, 4),
            "execution_mode=" + GetNullableString(reader, 5),
            "selected_entry_price=" + GetNullableString(reader, 6),
            "actual_notional=" + GetNullableString(reader, 7),
            "min_order_size=" + GetNullableString(reader, 8),
            "rounding=" + GetNullableString(reader, 9),
            "quote_source=" + GetNullableString(reader, 10),
            "quote_age_ms=" + GetNullableString(reader, 11)));
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
        : reader.GetFieldValue<DateTimeOffset>(ordinal).ToString("O", CultureInfo.InvariantCulture);
}

static string FormatNullableDecimal(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? "null" : FormatDecimal(reader.GetDecimal(ordinal));
}

static string FormatNullablePercent(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? "null" : FormatPercent(reader.GetDecimal(ordinal));
}

static string FormatNullableDouble(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal)
        ? "null"
        : reader.GetDouble(ordinal).ToString("0.###", CultureInfo.InvariantCulture);
}

static string FormatPercent(decimal value)
{
    return (value * 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}

static string FormatDecimal(decimal value)
{
    return value.ToString("0.########", CultureInfo.InvariantCulture);
}
