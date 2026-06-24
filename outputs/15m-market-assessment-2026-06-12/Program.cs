using System.Data;
using System.Globalization;
using System.Text.Json;
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
    CommandTimeout = 30,
    ApplicationName = "FifteenMinuteMarketAssessment"
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await ExecuteNonQueryAsync("SET default_transaction_read_only = on");
await ExecuteNonQueryAsync("SET statement_timeout = '30s'");

Console.WriteLine("== Database ==");
await PrintRowsAsync("""
SELECT
    now() AT TIME ZONE 'UTC' AS db_now_utc,
    current_database() AS database_name;
""");

Console.WriteLine();
Console.WriteLine("== Service heartbeat ==");
await PrintRowsAsync("""
SELECT
    service_name,
    status,
    mode,
    version,
    started_at_utc,
    last_heartbeat_utc,
    round(extract(epoch FROM ((now() AT TIME ZONE 'UTC') - (last_heartbeat_utc AT TIME ZONE 'UTC'))))::int AS heartbeat_age_seconds,
    left(coalesce(last_error, ''), 180) AS last_error_prefix
FROM service_heartbeats
WHERE service_name = 'PolyCopyTrader.Service';
""");

Console.WriteLine();
Console.WriteLine("== 15m strategy posture ==");
await PrintRowsAsync("""
SELECT
    CASE
        WHEN code ~ '^(btc|eth|sol)_up_down_15m_' THEN 'fixed_15m'
        WHEN code ~ '^btc_up_down_15m_preopen_' THEN 'btc_preopen_15m'
        WHEN lower(name) LIKE '% 15m %' THEN 'other_15m_named'
        ELSE 'non_15m'
    END AS family,
    count(*) AS rows,
    count(*) FILTER (WHERE enabled) AS enabled,
    count(*) FILTER (WHERE live_stakes) AS live,
    count(*) FILTER (WHERE auto_live_paused) AS auto_live_paused,
    min(updated_at_utc) AS first_updated_utc,
    max(updated_at_utc) AS latest_updated_utc
FROM strategies
WHERE code ~ '^(btc|eth|sol)_up_down_15m_'
   OR lower(name) LIKE '% 15m %'
GROUP BY family
ORDER BY family;
""");

Console.WriteLine();
Console.WriteLine("== Rolling Gamma market volume/liquidity: 5m vs 15m ==");
await PrintRowsAsync("""
WITH parsed AS MATERIALIZED (
    SELECT
        upper(match[1]) AS asset,
        match[2] AS market_interval,
        to_timestamp((match[3])::bigint) AS market_start_utc,
        coalesce(market.volume, 0)::numeric AS volume,
        coalesce(market.liquidity_clob, market.liquidity, 0)::numeric AS liquidity,
        market.spread::numeric AS spread,
        market.fetched_at_utc
    FROM polymarket_gamma_markets market
    CROSS JOIN LATERAL regexp_match(lower(coalesce(market.slug, '')), '^(btc|eth|sol)-updown-(5m|15m)-([0-9]+)$') AS match
    WHERE market.active
      AND NOT market.archived
),
windows(window_label, window_order, window_duration) AS (
    VALUES
        ('1h', 1, interval '1 hour'),
        ('6h', 2, interval '6 hours'),
        ('24h', 3, interval '24 hours')
)
SELECT
    parsed.asset,
    parsed.market_interval,
    windows.window_label,
    count(*) AS markets,
    count(*) FILTER (WHERE parsed.volume > 0) AS nonzero_volume_markets,
    round(sum(parsed.volume), 2) AS total_volume,
    round(avg(parsed.volume), 2) AS avg_market_volume,
    round((percentile_cont(0.5) WITHIN GROUP (ORDER BY parsed.volume))::numeric, 2) AS median_market_volume,
    round((percentile_cont(0.9) WITHIN GROUP (ORDER BY parsed.volume))::numeric, 2) AS p90_market_volume,
    round(avg(parsed.liquidity), 2) AS avg_liquidity,
    round(avg(parsed.spread), 4) AS avg_spread,
    round(max(parsed.spread), 4) AS max_spread,
    max(parsed.fetched_at_utc) AS latest_gamma_fetch_utc
FROM parsed
CROSS JOIN windows
WHERE parsed.market_start_utc <= now()
  AND parsed.market_start_utc > now() - windows.window_duration
GROUP BY parsed.asset, parsed.market_interval, windows.window_label, windows.window_order
ORDER BY parsed.asset, windows.window_order, parsed.market_interval;
""");

Console.WriteLine();
Console.WriteLine("== 15m volume ratio vs 5m ==");
await PrintRowsAsync("""
WITH parsed AS MATERIALIZED (
    SELECT
        upper(match[1]) AS asset,
        match[2] AS market_interval,
        to_timestamp((match[3])::bigint) AS market_start_utc,
        coalesce(market.volume, 0)::numeric AS volume
    FROM polymarket_gamma_markets market
    CROSS JOIN LATERAL regexp_match(lower(coalesce(market.slug, '')), '^(btc|eth|sol)-updown-(5m|15m)-([0-9]+)$') AS match
    WHERE market.active
      AND NOT market.archived
),
windows(window_label, window_order, window_duration) AS (
    VALUES
        ('1h', 1, interval '1 hour'),
        ('6h', 2, interval '6 hours'),
        ('24h', 3, interval '24 hours')
),
summary AS (
    SELECT
        parsed.asset,
        windows.window_label,
        windows.window_order,
        sum(parsed.volume) FILTER (WHERE parsed.market_interval = '5m') AS volume_5m,
        sum(parsed.volume) FILTER (WHERE parsed.market_interval = '15m') AS volume_15m
    FROM parsed
    CROSS JOIN windows
    WHERE parsed.market_start_utc <= now()
      AND parsed.market_start_utc > now() - windows.window_duration
    GROUP BY parsed.asset, windows.window_label, windows.window_order
)
SELECT
    asset,
    window_label,
    round(coalesce(volume_5m, 0), 2) AS volume_5m,
    round(coalesce(volume_15m, 0), 2) AS volume_15m,
    CASE
        WHEN coalesce(volume_5m, 0) = 0 THEN NULL
        ELSE round((coalesce(volume_15m, 0) / volume_5m) * 100, 4)
    END AS volume_15m_pct_of_5m
FROM summary
ORDER BY asset, window_order;
""");

Console.WriteLine();
Console.WriteLine("== Current and near Gamma markets ==");
await PrintRowsAsync("""
WITH parsed AS MATERIALIZED (
    SELECT
        upper(match[1]) AS asset,
        match[2] AS market_interval,
        to_timestamp((match[3])::bigint) AS market_start_utc,
        market.*
    FROM polymarket_gamma_markets market
    CROSS JOIN LATERAL regexp_match(lower(coalesce(market.slug, '')), '^(btc|eth|sol)-updown-(5m|15m)-([0-9]+)$') AS match
    WHERE market.active
      AND NOT market.archived
)
SELECT
    asset,
    market_interval,
    slug,
    market_start_utc,
    end_date_utc,
    active,
    closed,
    accepting_orders,
    enable_order_book,
    round(coalesce(volume, 0), 2) AS volume,
    round(coalesce(liquidity_clob, liquidity, 0), 2) AS liquidity,
    round(spread, 4) AS spread,
    best_bid,
    best_ask,
    round(extract(epoch FROM (now() - fetched_at_utc)))::int AS gamma_fetch_age_seconds
FROM parsed
WHERE market_start_utc >= now() - interval '20 minutes'
  AND market_start_utc <= now() + interval '45 minutes'
ORDER BY asset, market_interval, market_start_utc;
""");

Console.WriteLine();
Console.WriteLine("== Odds archive coverage and book quality ==");
await PrintRowsAsync("""
WITH ticks AS MATERIALIZED (
    SELECT
        'BTC'::text AS asset,
        match[1] AS market_interval,
        sampled_at_utc,
        market_slug,
        up_book_source,
        down_book_source,
        CASE WHEN up_best_bid IS NOT NULL AND up_best_ask IS NOT NULL THEN up_best_ask - up_best_bid END AS up_spread,
        CASE WHEN down_best_bid IS NOT NULL AND down_best_ask IS NOT NULL THEN down_best_ask - down_best_bid END AS down_spread
    FROM (
        SELECT *
        FROM btc_up_down_5m_odds_ticks
        WHERE sampled_at_utc >= now() - interval '24 hours'
    ) tick
    CROSS JOIN LATERAL regexp_match(lower(coalesce(tick.market_slug, '')), '^btc-updown-(5m|15m)-[0-9]+$') AS match
    WHERE match IS NOT NULL
    UNION ALL
    SELECT
        upper(tick.asset_symbol) AS asset,
        match[2] AS market_interval,
        sampled_at_utc,
        market_slug,
        up_book_source,
        down_book_source,
        CASE WHEN up_best_bid IS NOT NULL AND up_best_ask IS NOT NULL THEN up_best_ask - up_best_bid END AS up_spread,
        CASE WHEN down_best_bid IS NOT NULL AND down_best_ask IS NOT NULL THEN down_best_ask - down_best_bid END AS down_spread
    FROM (
        SELECT *
        FROM crypto_up_down_5m_odds_ticks
        WHERE sampled_at_utc >= now() - interval '24 hours'
    ) tick
    CROSS JOIN LATERAL regexp_match(lower(coalesce(tick.market_slug, '')), '^(eth|sol)-updown-(5m|15m)-[0-9]+$') AS match
    WHERE match IS NOT NULL
),
windows(window_label, window_order, window_duration) AS (
    VALUES
        ('1h', 1, interval '1 hour'),
        ('6h', 2, interval '6 hours'),
        ('24h', 3, interval '24 hours')
)
SELECT
    ticks.asset,
    ticks.market_interval,
    windows.window_label,
    count(*) AS ticks,
    count(DISTINCT ticks.market_slug) AS markets,
    count(*) FILTER (WHERE up_spread IS NOT NULL AND down_spread IS NOT NULL) AS both_sided_ticks,
    round(avg((coalesce(up_spread, 0) + coalesce(down_spread, 0)) / nullif((CASE WHEN up_spread IS NULL THEN 0 ELSE 1 END + CASE WHEN down_spread IS NULL THEN 0 ELSE 1 END), 0)), 4) AS avg_outcome_spread,
    count(*) FILTER (WHERE up_book_source = 'clob_rest' OR down_book_source = 'clob_rest') AS rest_fallback_ticks,
    max(sampled_at_utc) AS latest_tick_utc
FROM ticks
CROSS JOIN windows
WHERE ticks.sampled_at_utc >= now() - windows.window_duration
GROUP BY ticks.asset, ticks.market_interval, windows.window_label, windows.window_order
ORDER BY ticks.asset, windows.window_order, ticks.market_interval;
""");

Console.WriteLine();
Console.WriteLine("== 15m historical order/run remnants after removal ==");
await PrintRowsAsync("""
WITH target_strategy AS MATERIALIZED (
    SELECT id
    FROM strategies
    WHERE code ~ '^(btc|eth|sol)_up_down_15m_'
       OR lower(name) LIKE '% 15m %'
),
rows AS (
    SELECT 'paper_orders' AS source, order_row.created_at_utc AS created_or_updated_at_utc
    FROM paper_orders order_row
    WHERE order_row.strategy_id IN (SELECT id FROM target_strategy)
      AND order_row.created_at_utc >= timestamptz '2026-06-07 13:13:06Z'
    UNION ALL
    SELECT 'live_orders' AS source, live_order.created_at_utc AS created_or_updated_at_utc
    FROM live_orders live_order
    WHERE live_order.strategy_id IN (SELECT id FROM target_strategy)
      AND live_order.created_at_utc >= timestamptz '2026-06-07 13:13:06Z'
    UNION ALL
    SELECT 'strategy_market_paper_runs' AS source, run.updated_at_utc AS created_or_updated_at_utc
    FROM strategy_market_paper_runs run
    WHERE run.strategy_id IN (SELECT id FROM target_strategy)
      AND run.updated_at_utc >= timestamptz '2026-06-07 13:13:06Z'
)
SELECT
    source,
    count(*) AS rows,
    min(created_or_updated_at_utc) AS first_seen_utc,
    max(created_or_updated_at_utc) AS latest_seen_utc
FROM rows
GROUP BY source
ORDER BY source;
""");

Console.WriteLine();
Console.WriteLine("== Near 15m CLOB book depth ==");
var nearMarkets = await LoadNearFifteenMinuteMarketsAsync();
if (nearMarkets.Count == 0)
{
    Console.WriteLine("(no near 15m markets found)");
}
else
{
    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    var rows = new List<string[]>();
    foreach (var market in nearMarkets)
    {
        foreach (var token in market.Tokens)
        {
            var book = await TryGetOrderBookAsync(httpClient, token.TokenId);
            rows.Add(CreateBookRow(market, token, book));
            await Task.Delay(100);
        }
    }

    PrintTable(
        [
            "asset",
            "slug",
            "start_utc",
            "outcome",
            "best_bid",
            "best_ask",
            "spread",
            "ask_depth_<=50",
            "ask_depth_<=55",
            "ask_depth_<=60",
            "ask_depth_<=65",
            "min_order",
            "book_score"
        ],
        rows);
}

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

async Task PrintRowsAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
    var fieldCount = reader.FieldCount;
    var rows = new List<string[]>();
    while (await reader.ReadAsync())
    {
        var values = new string[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            values[i] = FormatValue(reader.GetValue(i));
        }

        rows.Add(values);
    }

    var headers = Enumerable.Range(0, fieldCount).Select(reader.GetName).ToArray();
    PrintTable(headers, rows);
}

async Task<IReadOnlyList<NearMarket>> LoadNearFifteenMinuteMarketsAsync()
{
    await using var command = new NpgsqlCommand("""
WITH parsed AS MATERIALIZED (
    SELECT
        upper(match[1]) AS asset,
        to_timestamp((match[3])::bigint) AS market_start_utc,
        market.slug,
        market.market_id,
        market.condition_id,
        market.end_date_utc,
        market.order_min_size,
        market.volume,
        market.liquidity_clob,
        market.liquidity,
        market.outcomes_json,
        market.clob_token_ids_json
    FROM polymarket_gamma_markets market
    CROSS JOIN LATERAL regexp_match(lower(coalesce(market.slug, '')), '^(btc|eth|sol)-updown-(15m)-([0-9]+)$') AS match
    WHERE market.active
      AND NOT market.archived
)
SELECT
    asset,
    slug,
    market_id,
    condition_id,
    market_start_utc,
    end_date_utc,
    order_min_size,
    coalesce(volume, 0) AS volume,
    coalesce(liquidity_clob, liquidity, 0) AS liquidity,
    outcomes_json->>0 AS outcome_0,
    outcomes_json->>1 AS outcome_1,
    clob_token_ids_json->>0 AS token_0,
    clob_token_ids_json->>1 AS token_1
FROM parsed
WHERE market_start_utc >= now() - interval '20 minutes'
  AND market_start_utc <= now() + interval '45 minutes'
ORDER BY asset, market_start_utc;
""", connection);

    var markets = new List<NearMarket>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var tokens = new List<OutcomeToken>();
        AddToken(tokens, GetNullableString(reader, "outcome_0"), GetNullableString(reader, "token_0"));
        AddToken(tokens, GetNullableString(reader, "outcome_1"), GetNullableString(reader, "token_1"));

        markets.Add(new NearMarket(
            reader.GetString(reader.GetOrdinal("asset")),
            reader.GetString(reader.GetOrdinal("slug")),
            reader.GetString(reader.GetOrdinal("market_id")),
            reader.GetString(reader.GetOrdinal("condition_id")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("market_start_utc")).ToUniversalTime(),
            GetNullableDateTimeOffset(reader, "end_date_utc"),
            GetNullableDecimal(reader, "order_min_size"),
            GetNullableDecimal(reader, "volume") ?? 0m,
            GetNullableDecimal(reader, "liquidity") ?? 0m,
            tokens));
    }

    return markets;
}

static void AddToken(List<OutcomeToken> tokens, string? outcome, string? tokenId)
{
    if (!string.IsNullOrWhiteSpace(outcome) && !string.IsNullOrWhiteSpace(tokenId))
    {
        tokens.Add(new OutcomeToken(outcome.Trim(), tokenId.Trim()));
    }
}

static async Task<BookSnapshot?> TryGetOrderBookAsync(HttpClient httpClient, string tokenId)
{
    try
    {
        var uri = "https://clob.polymarket.com/book?token_id=" + Uri.EscapeDataString(tokenId);
        using var response = await httpClient.GetAsync(uri);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return ParseBook(document.RootElement);
    }
    catch
    {
        return null;
    }
}

static BookSnapshot ParseBook(JsonElement root)
{
    return new BookSnapshot(
        ParseBookLevels(root, "bids"),
        ParseBookLevels(root, "asks"),
        GetDecimal(root, "min_order_size"));
}

static IReadOnlyList<BookLevel> ParseBookLevels(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out var levelsElement) ||
        levelsElement.ValueKind != JsonValueKind.Array)
    {
        return [];
    }

    var levels = new List<BookLevel>();
    foreach (var level in levelsElement.EnumerateArray())
    {
        var price = GetDecimal(level, "price");
        var size = GetDecimal(level, "size");
        if (price is { } parsedPrice && size is { } parsedSize)
        {
            levels.Add(new BookLevel(parsedPrice, parsedSize));
        }
    }

    return levels;
}

static string[] CreateBookRow(NearMarket market, OutcomeToken token, BookSnapshot? book)
{
    var bestBid = book?.Bids.Count > 0 ? book.Bids.Max(level => level.Price) : (decimal?)null;
    var bestAsk = book?.Asks.Count > 0 ? book.Asks.Min(level => level.Price) : (decimal?)null;
    var spread = bestBid is { } bid && bestAsk is { } ask ? ask - bid : (decimal?)null;
    var minOrder = book?.MinOrderSize ?? market.OrderMinSize ?? 5m;
    var depth50 = AskDepth(book, 0.50m);
    var depth55 = AskDepth(book, 0.55m);
    var depth60 = AskDepth(book, 0.60m);
    var depth65 = AskDepth(book, 0.65m);
    var score = ScoreBook(bestAsk, spread, depth60, minOrder);

    return
    [
        market.Asset,
        market.Slug,
        market.StartUtc.ToString("O", CultureInfo.InvariantCulture),
        token.Outcome,
        FormatValue(bestBid),
        FormatValue(bestAsk),
        FormatValue(spread),
        FormatValue(depth50),
        FormatValue(depth55),
        FormatValue(depth60),
        FormatValue(depth65),
        FormatValue(minOrder),
        score
    ];
}

static decimal AskDepth(BookSnapshot? book, decimal maxPrice)
{
    if (book is null)
    {
        return 0m;
    }

    return book.Asks
        .Where(level => level.Price <= maxPrice)
        .Sum(level => level.Size);
}

static string ScoreBook(decimal? bestAsk, decimal? spread, decimal depthAt60, decimal minOrder)
{
    if (bestAsk is null)
    {
        return "no_book";
    }

    if (bestAsk > 0.65m)
    {
        return "ask_above_65";
    }

    if (spread is null || spread > 0.05m)
    {
        return "wide_or_one_sided";
    }

    if (depthAt60 < minOrder)
    {
        return "thin_under_min_depth";
    }

    return "usable_small";
}

static decimal? GetDecimal(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property))
    {
        return null;
    }

    return property.ValueKind switch
    {
        JsonValueKind.Number when property.TryGetDecimal(out var number) => number,
        JsonValueKind.String when decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
        _ => null
    };
}

static string? GetNullableString(NpgsqlDataReader reader, string columnName)
{
    var ordinal = reader.GetOrdinal(columnName);
    return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}

static decimal? GetNullableDecimal(NpgsqlDataReader reader, string columnName)
{
    var ordinal = reader.GetOrdinal(columnName);
    return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}

static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
{
    var ordinal = reader.GetOrdinal(columnName);
    return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal).ToUniversalTime();
}

static void PrintTable(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
{
    if (rows.Count == 0)
    {
        Console.WriteLine("(no rows)");
        return;
    }

    var widths = new int[headers.Count];
    for (var i = 0; i < headers.Count; i++)
    {
        widths[i] = Math.Min(80, headers[i].Length);
    }

    foreach (var row in rows)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            widths[i] = Math.Min(80, Math.Max(widths[i], row[i].Length));
        }
    }

    Console.WriteLine(string.Join(" | ", headers.Select((header, i) => Pad(header, widths[i]))));
    Console.WriteLine(string.Join("-+-", widths.Select(width => new string('-', width))));
    foreach (var row in rows)
    {
        Console.WriteLine(string.Join(" | ", row.Select((value, i) => Pad(value, widths[i]))));
    }
}

static string FormatValue(object? value)
{
    return value switch
    {
        null => "",
        DBNull => "",
        DateTime dateTime => dateTime.Kind == DateTimeKind.Utc
            ? dateTime.ToString("O", CultureInfo.InvariantCulture)
            : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.########", CultureInfo.InvariantCulture),
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        float number => number.ToString("0.###", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}

static string Pad(string value, int width)
{
    if (value.Length > width)
    {
        return value[..Math.Max(0, width - 1)] + "…";
    }

    return value.PadRight(width);
}

internal sealed record NearMarket(
    string Asset,
    string Slug,
    string MarketId,
    string ConditionId,
    DateTimeOffset StartUtc,
    DateTimeOffset? EndUtc,
    decimal? OrderMinSize,
    decimal Volume,
    decimal Liquidity,
    IReadOnlyList<OutcomeToken> Tokens);

internal sealed record OutcomeToken(string Outcome, string TokenId);

internal sealed record BookSnapshot(
    IReadOnlyList<BookLevel> Bids,
    IReadOnlyList<BookLevel> Asks,
    decimal? MinOrderSize);

internal sealed record BookLevel(decimal Price, decimal Size);
