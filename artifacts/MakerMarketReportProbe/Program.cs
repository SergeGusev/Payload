using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;

const string UpMakerCode = "btc_up_down_5m_up_maker";
const string DownMakerCode = "btc_up_down_5m_down_maker";

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 1;
}

var requestedMarketSlug = args.Length > 0 ? args[0] : null;
var outputDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "outputs", "maker-market-report"));
Directory.CreateDirectory(outputDirectory);

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

var market = string.IsNullOrWhiteSpace(requestedMarketSlug)
    ? await LoadLatestMarketWithMakerActivityAsync(connection)
    : await LoadMarketBySlugAsync(connection, requestedMarketSlug);
if (market is null)
{
    Console.Error.WriteLine("No BTC 5m market with odds ticks was found.");
    return 2;
}

var ticks = await LoadTicksAsync(connection, market.MarketSlug);
var simulatedDecisions = BuildHighWaterSimulation(ticks, market);
var events = await LoadMakerEventsAsync(connection, market.MarketSlug);
var orders = await LoadMakerOrdersAsync(connection, market.ConditionId, events);

var generatedAtUtc = DateTimeOffset.UtcNow;
var safeSlug = SafeFileName(market.MarketSlug);
var htmlPath = Path.Combine(outputDirectory, $"{safeSlug}-maker-report.html");
var ticksCsvPath = Path.Combine(outputDirectory, $"{safeSlug}-ticks.csv");
var simulationCsvPath = Path.Combine(outputDirectory, $"{safeSlug}-high-water-simulation.csv");
var eventsCsvPath = Path.Combine(outputDirectory, $"{safeSlug}-maker-events.csv");
var ordersCsvPath = Path.Combine(outputDirectory, $"{safeSlug}-maker-orders.csv");

await File.WriteAllTextAsync(ticksCsvPath, BuildTicksCsv(ticks), Encoding.UTF8);
await File.WriteAllTextAsync(simulationCsvPath, BuildSimulationCsv(simulatedDecisions), Encoding.UTF8);
await File.WriteAllTextAsync(eventsCsvPath, BuildEventsCsv(events), Encoding.UTF8);
await File.WriteAllTextAsync(ordersCsvPath, BuildOrdersCsv(orders), Encoding.UTF8);
await File.WriteAllTextAsync(
    htmlPath,
    BuildHtmlReport(market, ticks, simulatedDecisions, events, orders, generatedAtUtc, ticksCsvPath, simulationCsvPath, eventsCsvPath, ordersCsvPath),
    Encoding.UTF8);

Console.WriteLine($"Market: {market.MarketSlug}");
Console.WriteLine($"StartUtc: {market.MarketStartUtc:O}");
Console.WriteLine($"EndUtc: {market.MarketEndUtc:O}");
Console.WriteLine($"Ticks: {ticks.Count}");
Console.WriteLine($"HighWaterSimulationRows: {simulatedDecisions.Count}");
Console.WriteLine($"HighWaterSimulatedOrders: {simulatedDecisions.Count(item => item.PlacesOrder)}");
Console.WriteLine($"MakerEvents: {events.Count}");
Console.WriteLine($"MakerOrders: {orders.Count}");
Console.WriteLine($"Report: {htmlPath}");
Console.WriteLine($"TicksCsv: {ticksCsvPath}");
Console.WriteLine($"SimulationCsv: {simulationCsvPath}");
Console.WriteLine($"EventsCsv: {eventsCsvPath}");
Console.WriteLine($"OrdersCsv: {ordersCsvPath}");
return 0;

static async Task<MarketInfo?> LoadLatestMarketWithMakerActivityAsync(NpgsqlConnection connection)
{
    const string sql = """
WITH market_ticks AS (
    SELECT
        tick.market_id,
        tick.condition_id,
        tick.market_slug,
        min(tick.market_start_utc) AS market_start_utc,
        max(tick.market_end_utc) AS market_end_utc,
        count(*)::integer AS tick_count,
        max(tick.sampled_at_utc) AS latest_tick_utc
    FROM btc_up_down_5m_odds_ticks tick
    GROUP BY tick.market_id, tick.condition_id, tick.market_slug
),
ranked AS (
    SELECT
        market_ticks.*,
        (
            SELECT count(*)::integer
            FROM strategy_market_paper_runs run
            INNER JOIN strategies strategy ON strategy.id = run.strategy_id
            WHERE strategy.code IN ('btc_up_down_5m_up_maker', 'btc_up_down_5m_down_maker')
              AND run.market_slug = market_ticks.market_slug
        ) AS maker_event_count,
        (
            SELECT count(*)::integer
            FROM paper_orders paper_order
            INNER JOIN strategies strategy ON strategy.id = paper_order.strategy_id
            WHERE strategy.code IN ('btc_up_down_5m_up_maker', 'btc_up_down_5m_down_maker')
              AND paper_order.condition_id = market_ticks.condition_id
              AND paper_order.execution_source = 'btc_updown5m_maker_post_only'
        ) AS maker_order_count
    FROM market_ticks
)
SELECT market_id, condition_id, market_slug, market_start_utc, market_end_utc,
       tick_count, maker_event_count, maker_order_count, latest_tick_utc
FROM ranked
ORDER BY
    CASE WHEN maker_event_count > 0 OR maker_order_count > 0 THEN 0 ELSE 1 END,
    market_start_utc DESC,
    latest_tick_utc DESC
LIMIT 1;
""";

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? ReadMarket(reader) : null;
}

static async Task<MarketInfo?> LoadMarketBySlugAsync(NpgsqlConnection connection, string marketSlug)
{
    const string sql = """
SELECT
    tick.market_id,
    tick.condition_id,
    tick.market_slug,
    min(tick.market_start_utc) AS market_start_utc,
    max(tick.market_end_utc) AS market_end_utc,
    count(*)::integer AS tick_count,
    (
        SELECT count(*)::integer
        FROM strategy_market_paper_runs run
        INNER JOIN strategies strategy ON strategy.id = run.strategy_id
        WHERE strategy.code IN ('btc_up_down_5m_up_maker', 'btc_up_down_5m_down_maker')
          AND run.market_slug = tick.market_slug
    ) AS maker_event_count,
    (
        SELECT count(*)::integer
        FROM paper_orders paper_order
        INNER JOIN strategies strategy ON strategy.id = paper_order.strategy_id
        WHERE strategy.code IN ('btc_up_down_5m_up_maker', 'btc_up_down_5m_down_maker')
          AND paper_order.condition_id = tick.condition_id
          AND paper_order.execution_source = 'btc_updown5m_maker_post_only'
    ) AS maker_order_count,
    max(tick.sampled_at_utc) AS latest_tick_utc
FROM btc_up_down_5m_odds_ticks tick
WHERE tick.market_slug = @MarketSlug
GROUP BY tick.market_id, tick.condition_id, tick.market_slug;
""";

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("MarketSlug", marketSlug);
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? ReadMarket(reader) : null;
}

static MarketInfo ReadMarket(NpgsqlDataReader reader)
{
    return new MarketInfo(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetFieldValue<DateTimeOffset>(3),
        reader.GetFieldValue<DateTimeOffset>(4),
        reader.GetInt32(5),
        reader.GetInt32(6),
        reader.GetInt32(7),
        reader.GetFieldValue<DateTimeOffset>(8));
}

static async Task<List<TickRow>> LoadTicksAsync(NpgsqlConnection connection, string marketSlug)
{
    const string sql = """
SELECT sampled_at_utc, seconds_after_start, seconds_to_close,
       binance_price_usd, btc_move_from_start_bps,
       up_best_bid, up_best_ask, up_mid, up_price_proxy, up_book_source, up_book_age_ms,
       down_best_bid, down_best_ask, down_mid, down_price_proxy, down_book_source, down_book_age_ms
FROM btc_up_down_5m_odds_ticks
WHERE market_slug = @MarketSlug
ORDER BY sampled_at_utc ASC;
""";

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("MarketSlug", marketSlug);
    await using var reader = await command.ExecuteReaderAsync();
    var results = new List<TickRow>();
    while (await reader.ReadAsync())
    {
        results.Add(new TickRow(
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetDecimal(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            GetDecimal(reader, 5),
            GetDecimal(reader, 6),
            GetDecimal(reader, 7),
            GetDecimal(reader, 8),
            reader.GetString(9),
            GetDecimal(reader, 10),
            GetDecimal(reader, 11),
            GetDecimal(reader, 12),
            GetDecimal(reader, 13),
            GetDecimal(reader, 14),
            reader.GetString(15),
            GetDecimal(reader, 16)));
    }

    return results;
}

static async Task<List<MakerEventRow>> LoadMakerEventsAsync(NpgsqlConnection connection, string marketSlug)
{
    const string sql = """
WITH run_rows AS (
    SELECT
        strategy.code AS strategy_code,
        run.id,
        run.market_id,
        run.market_slug,
        run.condition_id,
        run.status,
        run.selected_outcome,
        run.selected_asset_id,
        run.entry_price,
        run.stake_usd,
        run.size_shares,
        run.paper_order_id,
        run.entered_at_utc,
        run.skip_reason,
        run.skip_diagnostics_json,
        run.created_at_utc,
        run.updated_at_utc,
        CASE
            WHEN (run.skip_diagnostics_json->>'blocking_order_id') ~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
            THEN (run.skip_diagnostics_json->>'blocking_order_id')::uuid
            ELSE NULL
        END AS blocking_order_id
    FROM strategy_market_paper_runs run
    INNER JOIN strategies strategy ON strategy.id = run.strategy_id
    WHERE strategy.code IN ('btc_up_down_5m_up_maker', 'btc_up_down_5m_down_maker')
      AND run.market_slug = @MarketSlug
)
SELECT
    run_rows.strategy_code,
    run_rows.id,
    run_rows.market_id,
    run_rows.status,
    run_rows.selected_outcome,
    run_rows.selected_asset_id,
    run_rows.entry_price,
    run_rows.stake_usd,
    run_rows.size_shares,
    run_rows.paper_order_id,
    run_rows.entered_at_utc,
    run_rows.skip_reason,
    run_rows.skip_diagnostics_json::text,
    run_rows.created_at_utc,
    run_rows.updated_at_utc,
    COALESCE(block_paper_strategy.code, block_live_strategy.code, '') AS blocking_strategy_code,
    COALESCE(block_paper_order.outcome, block_live_order.outcome, '') AS blocking_outcome,
    COALESCE(block_paper_order.status, block_live_order.status::text, '') AS blocking_status,
    COALESCE(block_paper_order.created_at_utc, block_live_order.created_at_utc) AS blocking_created_at_utc
FROM run_rows
LEFT JOIN paper_orders block_paper_order ON block_paper_order.id = run_rows.blocking_order_id
LEFT JOIN strategies block_paper_strategy ON block_paper_strategy.id = block_paper_order.strategy_id
LEFT JOIN live_orders block_live_order ON block_live_order.id = run_rows.blocking_order_id
LEFT JOIN strategies block_live_strategy ON block_live_strategy.id = block_live_order.strategy_id
ORDER BY run_rows.created_at_utc ASC, run_rows.strategy_code ASC;
""";

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("MarketSlug", marketSlug);
    await using var reader = await command.ExecuteReaderAsync();
    var results = new List<MakerEventRow>();
    while (await reader.ReadAsync())
    {
        var diagnostics = reader.IsDBNull(12) ? "" : reader.GetString(12);
        var diagnostic = ParseDiagnostics(diagnostics);
        results.Add(new MakerEventRow(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            GetString(reader, 4),
            GetString(reader, 5),
            GetDecimal(reader, 6),
            reader.GetDecimal(7),
            GetDecimal(reader, 8),
            GetGuid(reader, 9),
            GetDateTimeOffset(reader, 10),
            GetString(reader, 11),
            diagnostics,
            reader.GetFieldValue<DateTimeOffset>(13),
            reader.GetFieldValue<DateTimeOffset>(14),
            GetString(reader, 15),
            GetString(reader, 16),
            GetString(reader, 17),
            GetDateTimeOffset(reader, 18),
            diagnostic.PreviousMaxBestAsk,
            diagnostic.CurrentMaxBestAsk,
            diagnostic.MakerLimitPrice,
            diagnostic.BestBid,
            diagnostic.BestAsk,
            diagnostic.GtdExpirationUtc,
            diagnostic.BlockingOrderId));
    }

    return results;
}

static async Task<List<MakerOrderRow>> LoadMakerOrdersAsync(
    NpgsqlConnection connection,
    string conditionId,
    IReadOnlyList<MakerEventRow> events)
{
    const string sql = """
SELECT strategy.code,
       paper_order.id,
       paper_order.status,
       paper_order.outcome,
       paper_order.asset_id,
       paper_order.price,
       paper_order.size_shares,
       paper_order.notional_usd,
       paper_order.created_at_utc,
       paper_order.expires_at_utc,
       paper_order.filled_at_utc,
       paper_order.cancelled_at_utc,
       paper_order.raw_decision_json::text
FROM paper_orders paper_order
INNER JOIN strategies strategy ON strategy.id = paper_order.strategy_id
WHERE strategy.code IN ('btc_up_down_5m_up_maker', 'btc_up_down_5m_down_maker')
  AND paper_order.condition_id = @ConditionId
  AND paper_order.execution_source = 'btc_updown5m_maker_post_only'
ORDER BY paper_order.created_at_utc ASC, strategy.code ASC;
""";

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("ConditionId", conditionId);
    await using var reader = await command.ExecuteReaderAsync();
    var eventByOrderId = events
        .Where(item => item.PaperOrderId.HasValue)
        .ToDictionary(item => item.PaperOrderId!.Value, item => item);
    var results = new List<MakerOrderRow>();
    while (await reader.ReadAsync())
    {
        var orderId = reader.GetGuid(1);
        eventByOrderId.TryGetValue(orderId, out var sourceEvent);
        results.Add(new MakerOrderRow(
            reader.GetString(0),
            orderId,
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetDecimal(5),
            reader.GetDecimal(6),
            reader.GetDecimal(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            GetDateTimeOffset(reader, 10),
            GetDateTimeOffset(reader, 11),
            reader.IsDBNull(12) ? "" : reader.GetString(12),
            sourceEvent?.PreviousMaxBestAsk,
            sourceEvent?.CurrentMaxBestAsk));
    }

    return results;
}

static List<MakerSimulationRow> BuildHighWaterSimulation(IReadOnlyList<TickRow> ticks, MarketInfo market)
{
    const decimal tickSize = 0.01m;
    var results = new List<MakerSimulationRow>();
    SimulateOutcome(
        ticks,
        market,
        UpMakerCode,
        "Up",
        tick => tick.UpBestAsk,
        results,
        tickSize);
    SimulateOutcome(
        ticks,
        market,
        DownMakerCode,
        "Down",
        tick => tick.DownBestAsk,
        results,
        tickSize);
    return results
        .OrderBy(item => item.SampledAtUtc)
        .ThenBy(item => item.Outcome, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static void SimulateOutcome(
    IReadOnlyList<TickRow> ticks,
    MarketInfo market,
    string strategyCode,
    string outcome,
    Func<TickRow, decimal?> bestAskSelector,
    List<MakerSimulationRow> results,
    decimal tickSize)
{
    decimal? maxBestAsk = null;
    var orderSequence = 0;
    var cutoffSeconds = Math.Max(0m, (decimal)(market.MarketEndUtc - market.MarketStartUtc).TotalSeconds - 60m);

    foreach (var tick in ticks)
    {
        var bestAsk = bestAskSelector(tick);
        if (bestAsk is not > 0m)
        {
            continue;
        }

        if (maxBestAsk is null)
        {
            maxBestAsk = bestAsk.Value;
            results.Add(new MakerSimulationRow(
                tick.SampledAtUtc,
                tick.SecondsAfterStart,
                strategyCode,
                outcome,
                "baseline",
                bestAsk.Value,
                PreviousMaxBestAsk: null,
                CurrentMaxBestAsk: bestAsk.Value,
                MakerLimitPrice: null,
                PlacesOrder: false,
                OrderSequence: 0,
                Reason: "baseline"));
            continue;
        }

        if (bestAsk.Value <= maxBestAsk.Value)
        {
            results.Add(new MakerSimulationRow(
                tick.SampledAtUtc,
                tick.SecondsAfterStart,
                strategyCode,
                outcome,
                "no_order",
                bestAsk.Value,
                maxBestAsk.Value,
                maxBestAsk.Value,
                MakerLimitPrice: null,
                PlacesOrder: false,
                OrderSequence: orderSequence,
                Reason: "below_or_equal_previous_max"));
            continue;
        }

        var previousMax = maxBestAsk.Value;
        maxBestAsk = bestAsk.Value;
        orderSequence++;
        var placesOrder = tick.SecondsAfterStart <= cutoffSeconds;
        results.Add(new MakerSimulationRow(
            tick.SampledAtUtc,
            tick.SecondsAfterStart,
            strategyCode,
            outcome,
            placesOrder ? "place_order" : "skip_after_cutoff",
            bestAsk.Value,
            previousMax,
            bestAsk.Value,
            placesOrder ? RoundDownToTick(Math.Max(0m, bestAsk.Value - tickSize), tickSize) : null,
            placesOrder,
            orderSequence,
            placesOrder ? "new_high" : "new_high_after_maker_cutoff"));
    }
}

static string BuildHtmlReport(
    MarketInfo market,
    IReadOnlyList<TickRow> ticks,
    IReadOnlyList<MakerSimulationRow> simulatedDecisions,
    IReadOnlyList<MakerEventRow> events,
    IReadOnlyList<MakerOrderRow> orders,
    DateTimeOffset generatedAtUtc,
    string ticksCsvPath,
    string simulationCsvPath,
    string eventsCsvPath,
    string ordersCsvPath)
{
    var upEvents = events.Where(item => item.StrategyCode == UpMakerCode).ToArray();
    var downEvents = events.Where(item => item.StrategyCode == DownMakerCode).ToArray();
    var enteredEvents = events.Where(item => string.Equals(item.Status, "Entered", StringComparison.OrdinalIgnoreCase)).ToArray();
    var simulatedOrders = simulatedDecisions.Where(item => item.PlacesOrder).ToArray();
    var simulatedNoOrders = simulatedDecisions
        .Where(item => string.Equals(item.Action, "no_order", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    var simulatedCutoffSkips = simulatedDecisions
        .Where(item => string.Equals(item.Action, "skip_after_cutoff", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    var skippedByReason = events
        .Where(item => !string.IsNullOrWhiteSpace(item.SkipReason))
        .GroupBy(item => item.SkipReason)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => $"{Html(group.Key)}: {group.Count()}");

    var svg = BuildSvg(market, ticks, simulatedDecisions, events, orders);
    var summaryCards = new[]
    {
        ("Market", market.MarketSlug),
        ("Window UTC", $"{market.MarketStartUtc:HH:mm:ss} - {market.MarketEndUtc:HH:mm:ss}"),
        ("Ticks", ticks.Count.ToString(CultureInfo.InvariantCulture)),
        ("Simulated orders", simulatedOrders.Length.ToString(CultureInfo.InvariantCulture)),
        ("No-order samples", simulatedNoOrders.Length.ToString(CultureInfo.InvariantCulture)),
        ("Cutoff skips", simulatedCutoffSkips.Length.ToString(CultureInfo.InvariantCulture)),
        ("Maker events", events.Count.ToString(CultureInfo.InvariantCulture)),
        ("Up Maker events", upEvents.Length.ToString(CultureInfo.InvariantCulture)),
        ("Down Maker events", downEvents.Length.ToString(CultureInfo.InvariantCulture)),
        ("Entered Maker orders", enteredEvents.Length.ToString(CultureInfo.InvariantCulture)),
        ("Paper orders", orders.Count.ToString(CultureInfo.InvariantCulture))
    };

    var html = new StringBuilder();
    html.AppendLine("<!doctype html>");
    html.AppendLine("<html lang=\"en\">");
    html.AppendLine("<head>");
    html.AppendLine("<meta charset=\"utf-8\">");
    html.AppendLine("<title>BTC 5m Maker Market Report</title>");
    html.AppendLine("<style>");
    html.AppendLine("""
body { margin: 0; font-family: Segoe UI, Arial, sans-serif; color: #1f2937; background: #f6f7f9; }
main { max-width: 1360px; margin: 0 auto; padding: 24px; }
h1 { font-size: 24px; margin: 0 0 6px; }
h2 { font-size: 18px; margin: 26px 0 10px; }
.muted { color: #6b7280; }
.cards { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 10px; margin: 18px 0; }
.card { background: #fff; border: 1px solid #d9dde5; border-radius: 6px; padding: 10px 12px; }
.label { color: #6b7280; font-size: 12px; }
.value { font-size: 16px; font-weight: 600; margin-top: 3px; }
.panel { background: #fff; border: 1px solid #d9dde5; border-radius: 6px; padding: 14px; overflow: auto; }
.legend { display: flex; flex-wrap: wrap; gap: 14px; font-size: 12px; margin: 10px 0 0; }
.swatch { display: inline-block; width: 16px; height: 3px; vertical-align: middle; margin-right: 5px; }
table { width: 100%; border-collapse: collapse; font-size: 12px; background: #fff; }
th, td { border-bottom: 1px solid #e5e7eb; padding: 6px 8px; text-align: left; white-space: nowrap; }
th { position: sticky; top: 0; background: #eef1f5; z-index: 1; }
.reason { max-width: 280px; overflow: hidden; text-overflow: ellipsis; }
.files code { display: block; margin: 4px 0; }
""");
    html.AppendLine("</style>");
    html.AppendLine("</head>");
    html.AppendLine("<body><main>");
    html.AppendLine($"<h1>BTC Up or Down 5m Maker report: {Html(market.MarketSlug)}</h1>");
    html.AppendLine($"<div class=\"muted\">Generated {generatedAtUtc:O}. Selection is the latest archived BTC 5m market with Maker activity when available.</div>");
    html.AppendLine("<div class=\"cards\">");
    foreach (var (label, value) in summaryCards)
    {
        html.AppendLine($"<div class=\"card\"><div class=\"label\">{Html(label)}</div><div class=\"value\">{Html(value)}</div></div>");
    }
    html.AppendLine("</div>");

    html.AppendLine("<div class=\"panel\">");
    html.AppendLine(svg);
    html.AppendLine("""
<div class="legend">
  <span><span class="swatch" style="background:#d62728"></span>Up ask</span>
  <span><span class="swatch" style="background:#ff9896"></span>Up bid</span>
  <span><span class="swatch" style="background:#1f77b4"></span>Down ask</span>
  <span><span class="swatch" style="background:#aec7e8"></span>Down bid</span>
  <span><span class="swatch" style="background:#16a34a"></span>simulated high-water maker order</span>
  <span><span class="swatch" style="background:#9ca3af"></span>simulated no order: below/equal previous max</span>
  <span>actual DB Maker event / placed order</span>
  <span>actual DB skipped Maker attempt</span>
  <span>vertical dashed line = maker expiration cutoff, market_end - 60s</span>
</div>
""");
    html.AppendLine("</div>");

    html.AppendLine("<h2>Interpretation</h2>");
    html.AppendLine("<div class=\"panel\">");
    html.AppendLine("<p>The line chart uses <code>btc_up_down_5m_odds_ticks</code>, which is the archived book snapshot stream. Simulated markers apply the current high-water Maker rule locally: first usable ask is a baseline, asks below or equal to the previous maximum are no-order points, and a new order appears only when the ask exceeds the prior high.</p>");
    html.AppendLine($"<p>Skip reasons: {(events.Count == 0 ? "no Maker events for this market" : string.Join("; ", skippedByReason))}.</p>");
    html.AppendLine("</div>");

    html.AppendLine("<h2>High-Water Simulation</h2>");
    html.AppendLine("<div class=\"panel\"><table><thead><tr>");
    foreach (var header in new[] { "UTC", "t+", "Strategy", "Outcome", "Action", "Ask", "Prev max", "New max", "Limit", "Reason" })
    {
        html.AppendLine($"<th>{Html(header)}</th>");
    }

    html.AppendLine("</tr></thead><tbody>");
    foreach (var item in simulatedDecisions)
    {
        html.AppendLine("<tr>");
        html.AppendLine($"<td>{item.SampledAtUtc:HH:mm:ss}</td>");
        html.AppendLine($"<td>{item.SecondsAfterStart.ToString("0.0", CultureInfo.InvariantCulture)}s</td>");
        html.AppendLine($"<td>{Html(ShortStrategyName(item.StrategyCode))}</td>");
        html.AppendLine($"<td>{Html(item.Outcome)}</td>");
        html.AppendLine($"<td>{Html(item.Action)}</td>");
        html.AppendLine($"<td>{FormatDecimal(item.BestAsk)}</td>");
        html.AppendLine($"<td>{FormatDecimal(item.PreviousMaxBestAsk)}</td>");
        html.AppendLine($"<td>{FormatDecimal(item.CurrentMaxBestAsk)}</td>");
        html.AppendLine($"<td>{FormatDecimal(item.MakerLimitPrice)}</td>");
        html.AppendLine($"<td class=\"reason\">{Html(item.Reason)}</td>");
        html.AppendLine("</tr>");
    }

    html.AppendLine("</tbody></table></div>");

    html.AppendLine("<h2>Maker Events</h2>");
    html.AppendLine("<div class=\"panel\"><table><thead><tr>");
    foreach (var header in new[] { "UTC", "t+", "Strategy", "Status", "Outcome", "Prev max ask", "New max ask", "Limit", "Skip reason", "Blocking strategy", "Blocking outcome/status", "Paper order" })
    {
        html.AppendLine($"<th>{Html(header)}</th>");
    }
    html.AppendLine("</tr></thead><tbody>");
    foreach (var item in events)
    {
        var seconds = SecondsAfterStart(market, item.CreatedAtUtc);
        html.AppendLine("<tr>");
        html.AppendLine($"<td>{item.CreatedAtUtc:HH:mm:ss}</td>");
        html.AppendLine($"<td>{seconds.ToString("0.0", CultureInfo.InvariantCulture)}s</td>");
        html.AppendLine($"<td>{Html(ShortStrategyName(item.StrategyCode))}</td>");
        html.AppendLine($"<td>{Html(item.Status)}</td>");
        html.AppendLine($"<td>{Html(item.SelectedOutcome)}</td>");
        html.AppendLine($"<td>{FormatDecimal(item.PreviousMaxBestAsk)}</td>");
        html.AppendLine($"<td>{FormatDecimal(item.CurrentMaxBestAsk)}</td>");
        html.AppendLine($"<td>{FormatDecimal(item.EntryPrice ?? item.MakerLimitPrice)}</td>");
        html.AppendLine($"<td class=\"reason\">{Html(item.SkipReason)}</td>");
        html.AppendLine($"<td>{Html(item.BlockingStrategyCode)}</td>");
        html.AppendLine($"<td>{Html(JoinNonEmpty(item.BlockingOutcome, item.BlockingStatus))}</td>");
        html.AppendLine($"<td>{Html(item.PaperOrderId?.ToString() ?? "")}</td>");
        html.AppendLine("</tr>");
    }
    html.AppendLine("</tbody></table></div>");

    html.AppendLine("<h2>Maker Paper Orders</h2>");
    html.AppendLine("<div class=\"panel\"><table><thead><tr>");
    foreach (var header in new[] { "UTC", "Strategy", "Status", "Outcome", "Price", "Size", "Notional", "Expires", "Filled", "Prev max ask", "New max ask" })
    {
        html.AppendLine($"<th>{Html(header)}</th>");
    }
    html.AppendLine("</tr></thead><tbody>");
    foreach (var order in orders)
    {
        html.AppendLine("<tr>");
        html.AppendLine($"<td>{order.CreatedAtUtc:HH:mm:ss}</td>");
        html.AppendLine($"<td>{Html(ShortStrategyName(order.StrategyCode))}</td>");
        html.AppendLine($"<td>{Html(order.Status)}</td>");
        html.AppendLine($"<td>{Html(order.Outcome)}</td>");
        html.AppendLine($"<td>{FormatDecimal(order.Price)}</td>");
        html.AppendLine($"<td>{FormatDecimal(order.SizeShares)}</td>");
        html.AppendLine($"<td>{FormatDecimal(order.NotionalUsd)}</td>");
        html.AppendLine($"<td>{order.ExpiresAtUtc:HH:mm:ss}</td>");
        html.AppendLine($"<td>{(order.FilledAtUtc is null ? "" : order.FilledAtUtc.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture))}</td>");
        html.AppendLine($"<td>{FormatDecimal(order.PreviousMaxBestAsk)}</td>");
        html.AppendLine($"<td>{FormatDecimal(order.CurrentMaxBestAsk)}</td>");
        html.AppendLine("</tr>");
    }
    html.AppendLine("</tbody></table></div>");

    html.AppendLine("<h2>Export Files</h2>");
    html.AppendLine("<div class=\"panel files\">");
    html.AppendLine($"<code>{Html(ticksCsvPath)}</code>");
    html.AppendLine($"<code>{Html(simulationCsvPath)}</code>");
    html.AppendLine($"<code>{Html(eventsCsvPath)}</code>");
    html.AppendLine($"<code>{Html(ordersCsvPath)}</code>");
    html.AppendLine("</div>");
    html.AppendLine("</main></body></html>");
    return html.ToString();
}

static string BuildSvg(
    MarketInfo market,
    IReadOnlyList<TickRow> ticks,
    IReadOnlyList<MakerSimulationRow> simulatedDecisions,
    IReadOnlyList<MakerEventRow> events,
    IReadOnlyList<MakerOrderRow> orders)
{
    const int width = 1260;
    const int height = 620;
    const int left = 70;
    const int right = 24;
    const int top = 34;
    const int bottom = 70;
    var plotWidth = width - left - right;
    var plotHeight = height - top - bottom;
    var minX = 0m;
    var maxX = Math.Max(300m, ticks.Count > 0 ? ticks.Max(item => item.SecondsAfterStart) : 300m);
    var minY = 0m;
    var maxY = 1m;

    double X(decimal seconds) => left + (double)((seconds - minX) / (maxX - minX)) * plotWidth;
    double Y(decimal price) => top + (1d - (double)((price - minY) / (maxY - minY))) * plotHeight;
    var html = new StringBuilder();
    html.AppendLine($"<svg viewBox=\"0 0 {width} {height}\" width=\"100%\" height=\"auto\" role=\"img\" aria-label=\"BTC 5m maker order book chart\">");
    html.AppendLine("<rect x=\"0\" y=\"0\" width=\"1260\" height=\"620\" fill=\"#fff\"/>");

    for (var price = 0m; price <= 1.0001m; price += 0.1m)
    {
        var y = Y(price);
        html.AppendLine($"<line x1=\"{left}\" y1=\"{Fmt(y)}\" x2=\"{width - right}\" y2=\"{Fmt(y)}\" stroke=\"#e5e7eb\" stroke-width=\"1\"/>");
        html.AppendLine($"<text x=\"{left - 10}\" y=\"{Fmt(y + 4)}\" text-anchor=\"end\" font-size=\"11\" fill=\"#6b7280\">{price:0.0}</text>");
    }

    for (var seconds = 0m; seconds <= maxX; seconds += 30m)
    {
        var x = X(seconds);
        html.AppendLine($"<line x1=\"{Fmt(x)}\" y1=\"{top}\" x2=\"{Fmt(x)}\" y2=\"{height - bottom}\" stroke=\"#f1f3f6\" stroke-width=\"1\"/>");
        html.AppendLine($"<text x=\"{Fmt(x)}\" y=\"{height - bottom + 20}\" text-anchor=\"middle\" font-size=\"11\" fill=\"#6b7280\">{seconds:0}s</text>");
    }

    var expirationSeconds = (decimal)(market.MarketEndUtc - market.MarketStartUtc).TotalSeconds - 60m;
    if (expirationSeconds > 0m)
    {
        var x = X(expirationSeconds);
        html.AppendLine($"<line x1=\"{Fmt(x)}\" y1=\"{top}\" x2=\"{Fmt(x)}\" y2=\"{height - bottom}\" stroke=\"#374151\" stroke-dasharray=\"6 6\" stroke-width=\"1.5\"/>");
        html.AppendLine($"<text x=\"{Fmt(x + 4)}\" y=\"{top + 14}\" font-size=\"11\" fill=\"#374151\">maker cutoff</text>");
    }

    AppendLine(html, ticks.Select(item => (item.SecondsAfterStart, item.UpBestAsk)), "#d62728", 2.2, X, Y);
    AppendLine(html, ticks.Select(item => (item.SecondsAfterStart, item.UpBestBid)), "#ff9896", 1.7, X, Y, "5 4");
    AppendLine(html, ticks.Select(item => (item.SecondsAfterStart, item.DownBestAsk)), "#1f77b4", 2.2, X, Y);
    AppendLine(html, ticks.Select(item => (item.SecondsAfterStart, item.DownBestBid)), "#aec7e8", 1.7, X, Y, "5 4");

    foreach (var item in simulatedDecisions)
    {
        var x = X(item.SecondsAfterStart);
        var y = Y(Clamp(item.BestAsk, 0m, 1m));
        var isUp = item.StrategyCode == UpMakerCode;
        var stroke = isUp ? "#7f1d1d" : "#1e3a8a";
        if (item.PlacesOrder)
        {
            html.AppendLine($"<circle cx=\"{Fmt(x)}\" cy=\"{Fmt(y)}\" r=\"7\" fill=\"#16a34a\" stroke=\"{stroke}\" stroke-width=\"2\"><title>{Html(ShortStrategyName(item.StrategyCode))} simulated order ask={FormatDecimal(item.BestAsk)} limit={FormatDecimal(item.MakerLimitPrice)}</title></circle>");
            if (item.MakerLimitPrice is { } makerLimitPrice)
            {
                var orderY = Y(Clamp(makerLimitPrice, 0m, 1m));
                html.AppendLine($"<line x1=\"{Fmt(x)}\" y1=\"{Fmt(y)}\" x2=\"{Fmt(x)}\" y2=\"{Fmt(orderY)}\" stroke=\"#16a34a\" stroke-width=\"1.5\"/>");
                html.AppendLine($"<circle cx=\"{Fmt(x)}\" cy=\"{Fmt(orderY)}\" r=\"4\" fill=\"#16a34a\"><title>Simulated maker limit {FormatDecimal(makerLimitPrice)}</title></circle>");
            }
        }
        else if (string.Equals(item.Action, "baseline", StringComparison.OrdinalIgnoreCase))
        {
            html.AppendLine($"<rect x=\"{Fmt(x - 4)}\" y=\"{Fmt(y - 4)}\" width=\"8\" height=\"8\" fill=\"#f59e0b\" stroke=\"{stroke}\" stroke-width=\"1.5\"><title>{Html(ShortStrategyName(item.StrategyCode))} baseline ask={FormatDecimal(item.BestAsk)}</title></rect>");
        }
        else if (string.Equals(item.Action, "skip_after_cutoff", StringComparison.OrdinalIgnoreCase))
        {
            html.AppendLine($"<path d=\"M {Fmt(x - 5)} {Fmt(y)} L {Fmt(x)} {Fmt(y - 6)} L {Fmt(x + 5)} {Fmt(y)} L {Fmt(x)} {Fmt(y + 6)} Z\" fill=\"#f97316\" stroke=\"{stroke}\" stroke-width=\"1.5\"><title>{Html(ShortStrategyName(item.StrategyCode))} new high after cutoff ask={FormatDecimal(item.BestAsk)}</title></path>");
        }
        else
        {
            html.AppendLine($"<circle cx=\"{Fmt(x)}\" cy=\"{Fmt(y)}\" r=\"3\" fill=\"#9ca3af\" opacity=\"0.72\"><title>{Html(ShortStrategyName(item.StrategyCode))} no order ask={FormatDecimal(item.BestAsk)} previous max={FormatDecimal(item.PreviousMaxBestAsk)}</title></circle>");
        }
    }

    var ordersById = orders.ToDictionary(item => item.Id);
    foreach (var item in events)
    {
        var seconds = (decimal)(item.CreatedAtUtc - market.MarketStartUtc).TotalSeconds;
        var price = item.CurrentMaxBestAsk ?? item.BestAsk ?? item.EntryPrice ?? item.MakerLimitPrice;
        if (price is null)
        {
            continue;
        }

        var x = X(seconds);
        var y = Y(Clamp(price.Value, 0m, 1m));
        var isUp = item.StrategyCode == UpMakerCode;
        var stroke = isUp ? "#991b1b" : "#1e3a8a";
        var fill = item.Status.Equals("Entered", StringComparison.OrdinalIgnoreCase) ? "#16a34a" : "#fff";
        var label = $"{ShortStrategyName(item.StrategyCode)} {item.Status} {item.SelectedOutcome} ask={FormatDecimal(price)}";
        if (item.Status.Equals("Entered", StringComparison.OrdinalIgnoreCase))
        {
            html.AppendLine($"<circle cx=\"{Fmt(x)}\" cy=\"{Fmt(y)}\" r=\"6\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"2\"><title>{Html(label)}</title></circle>");
        }
        else
        {
            html.AppendLine($"<g stroke=\"{stroke}\" stroke-width=\"2\"><line x1=\"{Fmt(x - 5)}\" y1=\"{Fmt(y - 5)}\" x2=\"{Fmt(x + 5)}\" y2=\"{Fmt(y + 5)}\"/><line x1=\"{Fmt(x - 5)}\" y1=\"{Fmt(y + 5)}\" x2=\"{Fmt(x + 5)}\" y2=\"{Fmt(y - 5)}\"/><title>{Html(label)} skip={Html(item.SkipReason)}</title></g>");
        }

        if (item.PaperOrderId is { } orderId && ordersById.TryGetValue(orderId, out var order))
        {
            var orderY = Y(Clamp(order.Price, 0m, 1m));
            html.AppendLine($"<line x1=\"{Fmt(x)}\" y1=\"{Fmt(y)}\" x2=\"{Fmt(x)}\" y2=\"{Fmt(orderY)}\" stroke=\"#16a34a\" stroke-width=\"1.5\"/>");
            html.AppendLine($"<circle cx=\"{Fmt(x)}\" cy=\"{Fmt(orderY)}\" r=\"4\" fill=\"#16a34a\"><title>Order price {FormatDecimal(order.Price)}</title></circle>");
        }
    }

    html.AppendLine($"<line x1=\"{left}\" y1=\"{height - bottom}\" x2=\"{width - right}\" y2=\"{height - bottom}\" stroke=\"#111827\"/>");
    html.AppendLine($"<line x1=\"{left}\" y1=\"{top}\" x2=\"{left}\" y2=\"{height - bottom}\" stroke=\"#111827\"/>");
    html.AppendLine($"<text x=\"{width / 2}\" y=\"{height - 20}\" text-anchor=\"middle\" font-size=\"12\" fill=\"#374151\">seconds after market start</text>");
    html.AppendLine($"<text x=\"18\" y=\"{height / 2}\" transform=\"rotate(-90 18 {height / 2})\" text-anchor=\"middle\" font-size=\"12\" fill=\"#374151\">Polymarket price</text>");
    html.AppendLine("</svg>");
    return html.ToString();
}

static void AppendLine(
    StringBuilder html,
    IEnumerable<(decimal X, decimal? Y)> values,
    string color,
    double strokeWidth,
    Func<decimal, double> xScale,
    Func<decimal, double> yScale,
    string? dash = null)
{
    var segments = new List<List<(decimal X, decimal Y)>>();
    List<(decimal X, decimal Y)> current = [];
    foreach (var value in values)
    {
        if (value.Y is null)
        {
            if (current.Count > 1)
            {
                segments.Add(current);
            }

            current = [];
            continue;
        }

        current.Add((value.X, Clamp(value.Y.Value, 0m, 1m)));
    }

    if (current.Count > 1)
    {
        segments.Add(current);
    }

    foreach (var segment in segments)
    {
        var points = string.Join(" ", segment.Select(point => $"{Fmt(xScale(point.X))},{Fmt(yScale(point.Y))}"));
        var dashAttribute = string.IsNullOrWhiteSpace(dash) ? "" : $" stroke-dasharray=\"{dash}\"";
        html.AppendLine($"<polyline points=\"{points}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{Fmt(strokeWidth)}\"{dashAttribute}/>");
    }
}

static MakerDiagnostic ParseDiagnostics(string diagnosticsJson)
{
    if (string.IsNullOrWhiteSpace(diagnosticsJson))
    {
        return new MakerDiagnostic(null, null, null, null, null, null, null);
    }

    try
    {
        using var document = JsonDocument.Parse(diagnosticsJson);
        var root = document.RootElement;
        return new MakerDiagnostic(
            GetJsonDecimal(root, "previous_max_best_ask") ?? GetJsonDecimal(root, "previous_best_ask"),
            GetJsonDecimal(root, "current_max_best_ask") ?? GetJsonDecimal(root, "current_best_ask"),
            GetJsonDecimal(root, "maker_limit_price"),
            GetJsonDecimal(root, "best_bid"),
            GetJsonDecimal(root, "best_ask"),
            GetJsonDateTimeOffset(root, "gtd_expiration_utc"),
            GetJsonGuid(root, "blocking_order_id"));
    }
    catch (JsonException)
    {
        return new MakerDiagnostic(null, null, null, null, null, null, null);
    }
}

static string BuildTicksCsv(IReadOnlyList<TickRow> ticks)
{
    var csv = new StringBuilder();
    csv.AppendLine("sampled_at_utc,seconds_after_start,seconds_to_close,binance_price_usd,btc_move_from_start_bps,up_best_bid,up_best_ask,up_mid,up_price_proxy,up_book_source,up_book_age_ms,down_best_bid,down_best_ask,down_mid,down_price_proxy,down_book_source,down_book_age_ms");
    foreach (var item in ticks)
    {
        csv.AppendLine(string.Join(",", [
            CsvText(item.SampledAtUtc.ToString("O", CultureInfo.InvariantCulture)),
            CsvDecimal(item.SecondsAfterStart),
            CsvDecimal(item.SecondsToClose),
            CsvDecimal(item.BinancePriceUsd),
            CsvDecimal(item.BtcMoveFromStartBps),
            CsvDecimal(item.UpBestBid),
            CsvDecimal(item.UpBestAsk),
            CsvDecimal(item.UpMid),
            CsvDecimal(item.UpPriceProxy),
            CsvText(item.UpBookSource),
            CsvDecimal(item.UpBookAgeMs),
            CsvDecimal(item.DownBestBid),
            CsvDecimal(item.DownBestAsk),
            CsvDecimal(item.DownMid),
            CsvDecimal(item.DownPriceProxy),
            CsvText(item.DownBookSource),
            CsvDecimal(item.DownBookAgeMs)
        ]));
    }

    return csv.ToString();
}

static string BuildSimulationCsv(IReadOnlyList<MakerSimulationRow> simulatedDecisions)
{
    var csv = new StringBuilder();
    csv.AppendLine("sampled_at_utc,seconds_after_start,strategy_code,outcome,action,best_ask,previous_max_best_ask,current_max_best_ask,maker_limit_price,places_order,order_sequence,reason");
    foreach (var item in simulatedDecisions)
    {
        csv.AppendLine(string.Join(",", [
            CsvText(item.SampledAtUtc.ToString("O", CultureInfo.InvariantCulture)),
            CsvDecimal(item.SecondsAfterStart),
            CsvText(item.StrategyCode),
            CsvText(item.Outcome),
            CsvText(item.Action),
            CsvDecimal(item.BestAsk),
            CsvDecimal(item.PreviousMaxBestAsk),
            CsvDecimal(item.CurrentMaxBestAsk),
            CsvDecimal(item.MakerLimitPrice),
            CsvText(item.PlacesOrder ? "true" : "false"),
            CsvDecimal(item.OrderSequence),
            CsvText(item.Reason)
        ]));
    }

    return csv.ToString();
}

static string BuildEventsCsv(IReadOnlyList<MakerEventRow> events)
{
    var csv = new StringBuilder();
    csv.AppendLine("created_at_utc,strategy_code,status,selected_outcome,entry_price,stake_usd,size_shares,previous_max_best_ask,current_max_best_ask,maker_limit_price,skip_reason,blocking_strategy_code,blocking_outcome,blocking_status,paper_order_id,market_id");
    foreach (var item in events)
    {
        csv.AppendLine(string.Join(",", [
            CsvText(item.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
            CsvText(item.StrategyCode),
            CsvText(item.Status),
            CsvText(item.SelectedOutcome),
            CsvDecimal(item.EntryPrice),
            CsvDecimal(item.StakeUsd),
            CsvDecimal(item.SizeShares),
            CsvDecimal(item.PreviousMaxBestAsk),
            CsvDecimal(item.CurrentMaxBestAsk),
            CsvDecimal(item.MakerLimitPrice),
            CsvText(item.SkipReason),
            CsvText(item.BlockingStrategyCode),
            CsvText(item.BlockingOutcome),
            CsvText(item.BlockingStatus),
            CsvText(item.PaperOrderId?.ToString()),
            CsvText(item.MarketId)
        ]));
    }

    return csv.ToString();
}

static string BuildOrdersCsv(IReadOnlyList<MakerOrderRow> orders)
{
    var csv = new StringBuilder();
    csv.AppendLine("created_at_utc,strategy_code,status,outcome,price,size_shares,notional_usd,expires_at_utc,filled_at_utc,cancelled_at_utc,previous_max_best_ask,current_max_best_ask,order_id");
    foreach (var item in orders)
    {
        csv.AppendLine(string.Join(",", [
            CsvText(item.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
            CsvText(item.StrategyCode),
            CsvText(item.Status),
            CsvText(item.Outcome),
            CsvDecimal(item.Price),
            CsvDecimal(item.SizeShares),
            CsvDecimal(item.NotionalUsd),
            CsvText(item.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture)),
            CsvText(item.FilledAtUtc?.ToString("O", CultureInfo.InvariantCulture)),
            CsvText(item.CancelledAtUtc?.ToString("O", CultureInfo.InvariantCulture)),
            CsvDecimal(item.PreviousMaxBestAsk),
            CsvDecimal(item.CurrentMaxBestAsk),
            CsvText(item.Id.ToString())
        ]));
    }

    return csv.ToString();
}

static decimal? GetDecimal(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}

static Guid? GetGuid(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
}

static string GetString(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
}

static DateTimeOffset? GetDateTimeOffset(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
}

static decimal? GetJsonDecimal(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null)
    {
        return null;
    }

    return property.ValueKind switch
    {
        JsonValueKind.Number when property.TryGetDecimal(out var value) => value,
        JsonValueKind.String when decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) => value,
        _ => null
    };
}

static DateTimeOffset? GetJsonDateTimeOffset(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null)
    {
        return null;
    }

    return DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
        ? value
        : null;
}

static Guid? GetJsonGuid(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null)
    {
        return null;
    }

    return Guid.TryParse(property.GetString(), out var value) ? value : null;
}

static decimal SecondsAfterStart(MarketInfo market, DateTimeOffset value)
{
    return (decimal)(value - market.MarketStartUtc).TotalSeconds;
}

static decimal Clamp(decimal value, decimal min, decimal max)
{
    return Math.Min(max, Math.Max(min, value));
}

static decimal RoundDownToTick(decimal value, decimal tickSize)
{
    return tickSize <= 0m
        ? value
        : Math.Floor(value / tickSize) * tickSize;
}

static string CsvDecimal(decimal? value)
{
    return value is null ? "" : value.Value.ToString("0.########", CultureInfo.InvariantCulture);
}

static string CsvText(string? value)
{
    if (string.IsNullOrEmpty(value))
    {
        return "";
    }

    return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

static string FormatDecimal(decimal? value)
{
    return value is null ? "" : value.Value.ToString("0.########", CultureInfo.InvariantCulture);
}

static string Fmt(double value)
{
    return value.ToString("0.###", CultureInfo.InvariantCulture);
}

static string Html(string? value)
{
    return WebUtility.HtmlEncode(value ?? "");
}

static string SafeFileName(string value)
{
    var invalid = Path.GetInvalidFileNameChars().ToHashSet();
    var builder = new StringBuilder(value.Length);
    foreach (var ch in value)
    {
        builder.Append(invalid.Contains(ch) ? '_' : ch);
    }

    return builder.ToString();
}

static string ShortStrategyName(string code)
{
    return code switch
    {
        UpMakerCode => "Up Maker",
        DownMakerCode => "Down Maker",
        _ => code
    };
}

static string JoinNonEmpty(params string[] values)
{
    return string.Join(" / ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
}

sealed record MarketInfo(
    string MarketId,
    string ConditionId,
    string MarketSlug,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset MarketEndUtc,
    int TickCount,
    int MakerEventCount,
    int MakerOrderCount,
    DateTimeOffset LatestTickUtc);

sealed record TickRow(
    DateTimeOffset SampledAtUtc,
    decimal SecondsAfterStart,
    decimal SecondsToClose,
    decimal BinancePriceUsd,
    decimal BtcMoveFromStartBps,
    decimal? UpBestBid,
    decimal? UpBestAsk,
    decimal? UpMid,
    decimal? UpPriceProxy,
    string UpBookSource,
    decimal? UpBookAgeMs,
    decimal? DownBestBid,
    decimal? DownBestAsk,
    decimal? DownMid,
    decimal? DownPriceProxy,
    string DownBookSource,
    decimal? DownBookAgeMs);

sealed record MakerSimulationRow(
    DateTimeOffset SampledAtUtc,
    decimal SecondsAfterStart,
    string StrategyCode,
    string Outcome,
    string Action,
    decimal BestAsk,
    decimal? PreviousMaxBestAsk,
    decimal CurrentMaxBestAsk,
    decimal? MakerLimitPrice,
    bool PlacesOrder,
    int OrderSequence,
    string Reason);

sealed record MakerEventRow(
    string StrategyCode,
    Guid Id,
    string MarketId,
    string Status,
    string SelectedOutcome,
    string SelectedAssetId,
    decimal? EntryPrice,
    decimal StakeUsd,
    decimal? SizeShares,
    Guid? PaperOrderId,
    DateTimeOffset? EnteredAtUtc,
    string SkipReason,
    string DiagnosticsJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string BlockingStrategyCode,
    string BlockingOutcome,
    string BlockingStatus,
    DateTimeOffset? BlockingCreatedAtUtc,
    decimal? PreviousMaxBestAsk,
    decimal? CurrentMaxBestAsk,
    decimal? MakerLimitPrice,
    decimal? BestBid,
    decimal? BestAsk,
    DateTimeOffset? GtdExpirationUtc,
    Guid? BlockingOrderId);

sealed record MakerOrderRow(
    string StrategyCode,
    Guid Id,
    string Status,
    string Outcome,
    string AssetId,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? FilledAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string RawDecisionJson,
    decimal? PreviousMaxBestAsk,
    decimal? CurrentMaxBestAsk);

sealed record MakerDiagnostic(
    decimal? PreviousMaxBestAsk,
    decimal? CurrentMaxBestAsk,
    decimal? MakerLimitPrice,
    decimal? BestBid,
    decimal? BestAsk,
    DateTimeOffset? GtdExpirationUtc,
    Guid? BlockingOrderId);
