using System.Globalization;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

var connectionFactory = new PostgresConnectionFactory(new StorageOptions());
await using var connection = connectionFactory.CreateConnection();
await connection.OpenAsync();

var rows = new List<StrategyRunRow>();

await using (var command = new NpgsqlCommand(
    """
    SELECT
        s.code,
        s.name,
        r.market_id,
        r.market_slug,
        r.market_start_utc,
        r.market_end_utc,
        r.status AS run_status,
        r.selected_outcome,
        r.entry_price,
        r.stake_usd,
        r.size_shares AS requested_size_shares,
        r.paper_order_id,
        r.settlement_price,
        r.settlement_value_usd,
        r.realized_pnl_usd,
        r.settled_at_utc,
        r.skip_reason,
        po.status AS order_status,
        po.price AS order_price,
        po.size_shares AS order_size_shares,
        po.notional_usd AS order_notional_usd,
        po.created_at_utc AS order_created_at_utc,
        po.expires_at_utc AS order_expires_at_utc,
        COUNT(pf.id)::integer AS fill_count,
        COALESCE(SUM(pf.size_shares), 0) AS filled_size_shares,
        CASE
            WHEN COALESCE(SUM(pf.size_shares), 0) = 0 THEN NULL
            ELSE SUM(pf.price * pf.size_shares) / SUM(pf.size_shares)
        END AS average_fill_price,
        MIN(pf.filled_at_utc) AS first_fill_at_utc,
        MAX(pf.filled_at_utc) AS last_fill_at_utc
    FROM strategy_market_paper_runs r
    INNER JOIN strategies s ON s.id = r.strategy_id
    LEFT JOIN paper_orders po ON po.id = r.paper_order_id
    LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
    WHERE s.code IN ('btc_up_down_5m_up', 'btc_up_down_5m_down')
    GROUP BY
        s.code,
        s.name,
        r.market_id,
        r.market_slug,
        r.market_start_utc,
        r.market_end_utc,
        r.status,
        r.selected_outcome,
        r.entry_price,
        r.stake_usd,
        r.size_shares,
        r.paper_order_id,
        r.settlement_price,
        r.settlement_value_usd,
        r.realized_pnl_usd,
        r.settled_at_utc,
        r.skip_reason,
        po.status,
        po.price,
        po.size_shares,
        po.notional_usd,
        po.created_at_utc,
        po.expires_at_utc
    ORDER BY COALESCE(r.market_start_utc, po.created_at_utc), s.code;
    """,
    connection))
{
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new StrategyRunRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            GetNullableDateTime(reader, 4),
            GetNullableDateTime(reader, 5),
            reader.GetString(6),
            GetNullableString(reader, 7),
            GetNullableDecimal(reader, 8),
            reader.GetDecimal(9),
            GetNullableDecimal(reader, 10),
            GetNullableGuid(reader, 11),
            GetNullableDecimal(reader, 12),
            GetNullableDecimal(reader, 13),
            GetNullableDecimal(reader, 14),
            GetNullableDateTime(reader, 15),
            GetNullableString(reader, 16),
            GetNullableString(reader, 17),
            GetNullableDecimal(reader, 18),
            GetNullableDecimal(reader, 19),
            GetNullableDecimal(reader, 20),
            GetNullableDateTime(reader, 21),
            GetNullableDateTime(reader, 22),
            reader.GetInt32(23),
            reader.GetDecimal(24),
            GetNullableDecimal(reader, 25),
            GetNullableDateTime(reader, 26),
            GetNullableDateTime(reader, 27)));
    }
}

Console.WriteLine("total_runs=" + rows.Count);

foreach (var group in rows.GroupBy(row => row.Code).OrderBy(group => group.Key))
{
    var settled = group.Where(row => string.Equals(row.RunStatus, "Settled", StringComparison.OrdinalIgnoreCase)).ToArray();
    var wins = settled.Count(row => row.RealizedPnlUsd > 0);
    var losses = settled.Count(row => row.RealizedPnlUsd < 0);
    var flat = settled.Count(row => row.RealizedPnlUsd == 0);
    var skipped = group.Count(row => string.Equals(row.RunStatus, "Skipped", StringComparison.OrdinalIgnoreCase));
    var entered = group.Count(row => string.Equals(row.RunStatus, "Entered", StringComparison.OrdinalIgnoreCase));
    var observed = group.Count(row => string.Equals(row.RunStatus, "Observed", StringComparison.OrdinalIgnoreCase));
    var expired = group.Count(row => row.OrderStatus is "Expired" or "PartiallyFilledExpired");
    var filledRows = group.Count(row => row.FilledSizeShares > 0);
    var pnl = settled.Sum(row => row.RealizedPnlUsd ?? 0);

    Console.WriteLine(string.Join(
        " ",
        "summary",
        "code=" + group.Key,
        "runs=" + group.Count(),
        "settled=" + settled.Length,
        "wins=" + wins,
        "losses=" + losses,
        "flat=" + flat,
        "skipped=" + skipped,
        "entered=" + entered,
        "observed=" + observed,
        "order_expired=" + expired,
        "rows_with_fills=" + filledRows,
        "pnl=" + FormatDecimal(pnl)));

    foreach (var statusGroup in group.GroupBy(row => (row.RunStatus, row.OrderStatus ?? "(no_order)", row.SkipReason ?? string.Empty))
                 .OrderByDescending(statusGroup => statusGroup.Count())
                 .ThenBy(statusGroup => statusGroup.Key.RunStatus)
                 .ThenBy(statusGroup => statusGroup.Key.Item2)
                 .Take(10))
    {
        Console.WriteLine(string.Join(
            " ",
            "status_breakdown",
            "code=" + group.Key,
            "run_status=" + statusGroup.Key.RunStatus,
            "order_status=" + statusGroup.Key.Item2,
            "skip_reason=" + (string.IsNullOrWhiteSpace(statusGroup.Key.Item3) ? "-" : statusGroup.Key.Item3),
            "count=" + statusGroup.Count()));
    }
}

var byMarket = rows
    .GroupBy(row => row.MarketId)
    .Select(group => new
    {
        MarketId = group.Key,
        MarketSlug = group.Select(row => row.MarketSlug).FirstOrDefault(slug => !string.IsNullOrWhiteSpace(slug)) ?? string.Empty,
        MarketStartUtc = group.Select(row => row.MarketStartUtc).FirstOrDefault(value => value.HasValue),
        Up = group.FirstOrDefault(row => row.Code == "btc_up_down_5m_up"),
        Down = group.FirstOrDefault(row => row.Code == "btc_up_down_5m_down")
    })
    .OrderBy(item => item.MarketStartUtc)
    .ToArray();

var bothSettled = byMarket.Count(item => IsSettled(item.Up) && IsSettled(item.Down));
var onlyUpSettled = byMarket.Count(item => IsSettled(item.Up) && !IsSettled(item.Down));
var onlyDownSettled = byMarket.Count(item => !IsSettled(item.Up) && IsSettled(item.Down));
var neitherSettled = byMarket.Count(item => !IsSettled(item.Up) && !IsSettled(item.Down));
var bothFilled = byMarket.Count(item => HasFill(item.Up) && HasFill(item.Down));
var onlyUpFilled = byMarket.Count(item => HasFill(item.Up) && !HasFill(item.Down));
var onlyDownFilled = byMarket.Count(item => !HasFill(item.Up) && HasFill(item.Down));
var neitherFilled = byMarket.Count(item => !HasFill(item.Up) && !HasFill(item.Down));
var sameSettledSign = byMarket.Count(item =>
    IsSettled(item.Up) &&
    IsSettled(item.Down) &&
    Math.Sign((double)(item.Up!.RealizedPnlUsd ?? 0)) == Math.Sign((double)(item.Down!.RealizedPnlUsd ?? 0)));

Console.WriteLine(string.Join(
    " ",
    "pair_summary",
    "markets=" + byMarket.Length,
    "both_settled=" + bothSettled,
    "only_up_settled=" + onlyUpSettled,
    "only_down_settled=" + onlyDownSettled,
    "neither_settled=" + neitherSettled,
    "both_filled=" + bothFilled,
    "only_up_filled=" + onlyUpFilled,
    "only_down_filled=" + onlyDownFilled,
    "neither_filled=" + neitherFilled,
    "same_settled_sign=" + sameSettledSign));

Console.WriteLine("pairs=");
foreach (var item in byMarket)
{
    Console.WriteLine(string.Join(
        " ",
        "pair",
        "start=" + FormatTime(item.MarketStartUtc),
        "slug=" + item.MarketSlug,
        "up=" + FormatRun(item.Up),
        "down=" + FormatRun(item.Down)));
}

static bool IsSettled(StrategyRunRow? row)
{
    return row is not null && string.Equals(row.RunStatus, "Settled", StringComparison.OrdinalIgnoreCase);
}

static bool HasFill(StrategyRunRow? row)
{
    return row is not null && row.FilledSizeShares > 0;
}

static string FormatRun(StrategyRunRow? row)
{
    if (row is null)
    {
        return "missing";
    }

    return string.Join(
        ",",
        row.RunStatus,
        "outcome=" + (row.SelectedOutcome ?? "-"),
        "order=" + (row.OrderStatus ?? "-"),
        "fills=" + FormatDecimal(row.FilledSizeShares),
        "avg=" + FormatDecimal(row.AverageFillPrice),
        "pnl=" + FormatDecimal(row.RealizedPnlUsd),
        "skip=" + (row.SkipReason ?? "-"));
}

static string FormatDecimal(decimal? value)
{
    return value.HasValue ? value.Value.ToString("0.########", CultureInfo.InvariantCulture) : "null";
}

static string FormatTime(DateTimeOffset? value)
{
    return value.HasValue ? value.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "null";
}

static string? GetNullableString(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}

static decimal? GetNullableDecimal(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}

static Guid? GetNullableGuid(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
}

static DateTimeOffset? GetNullableDateTime(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
}

internal sealed record StrategyRunRow(
    string Code,
    string Name,
    string MarketId,
    string MarketSlug,
    DateTimeOffset? MarketStartUtc,
    DateTimeOffset? MarketEndUtc,
    string RunStatus,
    string? SelectedOutcome,
    decimal? EntryPrice,
    decimal StakeUsd,
    decimal? RequestedSizeShares,
    Guid? PaperOrderId,
    decimal? SettlementPrice,
    decimal? SettlementValueUsd,
    decimal? RealizedPnlUsd,
    DateTimeOffset? SettledAtUtc,
    string? SkipReason,
    string? OrderStatus,
    decimal? OrderPrice,
    decimal? OrderSizeShares,
    decimal? OrderNotionalUsd,
    DateTimeOffset? OrderCreatedAtUtc,
    DateTimeOffset? OrderExpiresAtUtc,
    int FillCount,
    decimal FilledSizeShares,
    decimal? AverageFillPrice,
    DateTimeOffset? FirstFillAtUtc,
    DateTimeOffset? LastFillAtUtc);
