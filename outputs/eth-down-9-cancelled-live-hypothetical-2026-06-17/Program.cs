using System.Data;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;

const string StrategyCode = "eth_up_down_5m_down_bps_9_instant";
const string StrategyName = "ETH Up or Down 5m Down 9 bps Instant";
const string DefaultOutputDirectory = "outputs/eth-down-9-cancelled-live-hypothetical-2026-06-17";
const string FunderAddress = "0x49d6fEE74b294951668a4160f450Ff1C92E94cEC";
const string DataApiBaseUrl = "https://data-api.polymarket.com";

var outputDirectory = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase)) ?? DefaultOutputDirectory;
Directory.CreateDirectory(outputDirectory);

var summaryPath = Path.Combine(outputDirectory, "result.txt");
var ordersPath = Path.Combine(outputDirectory, "orders.tsv");
var statusDetailsPath = Path.Combine(outputDirectory, "status-details.tsv");
var dataApiTradeMatchesPath = Path.Combine(outputDirectory, "data-api-trade-matches.tsv");
var matchedTimingPath = Path.Combine(outputDirectory, "matched-timing.tsv");

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "EthDown9CancelledLiveHypothetical",
    Timeout = 10,
    CommandTimeout = 60
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await ExecuteNonQueryAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '45s';
SET LOCAL lock_timeout = '500ms';
""");

var capturedAtUtc = DateTimeOffset.UtcNow;
var strategy = await ReadStrategyAsync();
var orders = await ReadOrdersAsync();
var statusDetails = await ReadStatusDetailsAsync();
var dataApiTradeMatches = await ReadDataApiTradeMatchesAsync(orders);
var matchedTiming = await ReadMatchedTimingAsync();

var resolvedOrders = orders.Where(order => order.IsResolved).ToArray();
var unresolvedOrders = orders.Where(order => !order.IsResolved).ToArray();
var wonOrders = resolvedOrders.Where(order => order.WouldWin == true).ToArray();
var lostOrders = resolvedOrders.Where(order => order.WouldWin == false).ToArray();
var hypotheticalPnl = resolvedOrders.Sum(order => order.HypotheticalRealizedPnlUsd.GetValueOrDefault());
var hypotheticalBalance = strategy.LiveAvailableBalance + hypotheticalPnl;

await using (var writer = new StreamWriter(summaryPath, append: false))
{
    await writer.WriteLineAsync($"CapturedAtUtc\t{capturedAtUtc:O}");
    await writer.WriteLineAsync($"Database\t{await ScalarAsync("SELECT current_database();")}");
    await writer.WriteLineAsync($"ServerAddress\t{await ScalarAsync("SELECT inet_server_addr()::text;")}");
    await writer.WriteLineAsync($"ServerTimeUtc\t{await ScalarAsync("SELECT to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"');")}");
    await writer.WriteLineAsync($"StrategyCode\t{strategy.Code}");
    await writer.WriteLineAsync($"StrategyName\t{strategy.Name}");
    await writer.WriteLineAsync($"StrategyId\t{strategy.Id}");
    await writer.WriteLineAsync($"Enabled\t{strategy.Enabled}");
    await writer.WriteLineAsync($"LiveStakes\t{strategy.LiveStakes}");
    await writer.WriteLineAsync($"LiveStakeAmount\t{ReportFormat.Decimal(strategy.LiveStakeAmount)}");
    await writer.WriteLineAsync($"CurrentLiveAvailableBalance\t{ReportFormat.Decimal(strategy.LiveAvailableBalance)}");
    await writer.WriteLineAsync($"CancelledOrCancelFailedOrders\t{orders.Count}");
    await writer.WriteLineAsync($"ResolvedOrders\t{resolvedOrders.Length}");
    await writer.WriteLineAsync($"UnresolvedOrders\t{unresolvedOrders.Length}");
    await writer.WriteLineAsync($"WouldWinOrders\t{wonOrders.Length}");
    await writer.WriteLineAsync($"WouldLoseOrders\t{lostOrders.Length}");
    await writer.WriteLineAsync($"HypotheticalMatchedCostUsd\t{ReportFormat.Decimal(resolvedOrders.Sum(order => order.HypotheticalCostBasisUsd.GetValueOrDefault()))}");
    await writer.WriteLineAsync($"HypotheticalMatchedSettlementValueUsd\t{ReportFormat.Decimal(resolvedOrders.Sum(order => order.HypotheticalSettlementValueUsd.GetValueOrDefault()))}");
    await writer.WriteLineAsync($"HypotheticalMatchedRealizedPnlUsd\t{ReportFormat.Decimal(hypotheticalPnl)}");
    await writer.WriteLineAsync($"HypotheticalLiveAvailableBalance\t{ReportFormat.Decimal(hypotheticalBalance)}");
    await writer.WriteLineAsync($"BalanceDeltaUsd\t{ReportFormat.Decimal(hypotheticalPnl)}");
    await writer.WriteLineAsync($"OrdersTsv\t{Path.GetFullPath(ordersPath)}");
    await writer.WriteLineAsync($"StatusDetailsTsv\t{Path.GetFullPath(statusDetailsPath)}");
    await writer.WriteLineAsync($"DataApiTradeMatchesTsv\t{Path.GetFullPath(dataApiTradeMatchesPath)}");
    await writer.WriteLineAsync($"DataApiMatchedTargetRows\t{dataApiTradeMatches.Count(match => match.DataApiTradeCount > 0)}");
    await writer.WriteLineAsync($"DataApiTargetRowsCoveredByTradeSize\t{dataApiTradeMatches.Count(match => match.MatchSignal == DataApiMatchSignal.SizeCovered)}");
    await writer.WriteLineAsync($"MatchedTimingTsv\t{Path.GetFullPath(matchedTimingPath)}");
    await writer.WriteLineAsync($"MatchedOrders\t{matchedTiming.Count}");
    await writer.WriteLineAsync($"MatchedImmediatelyByPlacement\t{matchedTiming.Count(row => row.IsImmediatePlacementMatch)}");
    await writer.WriteLineAsync($"MatchedByDataApiReconciliation\t{matchedTiming.Count(row => row.IsDataApiReconciled)}");
    await writer.WriteLineAsync($"MatchedByLaterStatusPolling\t{matchedTiming.Count(row => row.IsLaterStatusPollingMatch)}");
    await writer.WriteLineAsync();
    await writer.WriteLineAsync("Notes");
    await writer.WriteLineAsync("- Hypothesis assumes each cancelled/cancel-failed order was fully matched at its stored limit price.");
    await writer.WriteLineAsync("- Hypothetical balance is current strategies.live_available_balance plus hypothetical realized PnL for resolved target orders.");
    await writer.WriteLineAsync("- Unresolved orders are excluded from the balance delta.");
    await writer.WriteLineAsync("- The query is read-only and does not change live_orders or strategy balances.");
}

await using (var writer = new StreamWriter(ordersPath, append: false))
{
    await writer.WriteLineAsync(string.Join('\t', OrderReport.Columns));
    foreach (var order in orders)
    {
        await writer.WriteLineAsync(order.ToTsv());
    }
}

await using (var writer = new StreamWriter(statusDetailsPath, append: false))
{
    await writer.WriteLineAsync(string.Join('\t', StatusDetailReport.Columns));
    foreach (var detail in statusDetails)
    {
        await writer.WriteLineAsync(detail.ToTsv());
    }
}

await using (var writer = new StreamWriter(dataApiTradeMatchesPath, append: false))
{
    await writer.WriteLineAsync(string.Join('\t', DataApiTradeMatchReport.Columns));
    foreach (var match in dataApiTradeMatches)
    {
        await writer.WriteLineAsync(match.ToTsv());
    }
}

await using (var writer = new StreamWriter(matchedTimingPath, append: false))
{
    await writer.WriteLineAsync(string.Join('\t', MatchedTimingReport.Columns));
    foreach (var row in matchedTiming)
    {
        await writer.WriteLineAsync(row.ToTsv());
    }
}

await tx.CommitAsync();

Console.WriteLine($"summary={Path.GetFullPath(summaryPath)}");
Console.WriteLine($"orders={Path.GetFullPath(ordersPath)}");
Console.WriteLine($"status_details={Path.GetFullPath(statusDetailsPath)}");
Console.WriteLine($"data_api_trade_matches={Path.GetFullPath(dataApiTradeMatchesPath)}");
Console.WriteLine($"matched_timing={Path.GetFullPath(matchedTimingPath)}");
Console.WriteLine($"strategy={strategy.Code}");
Console.WriteLine($"current_live_available_balance={ReportFormat.Decimal(strategy.LiveAvailableBalance)}");
Console.WriteLine($"target_orders={orders.Count}");
Console.WriteLine($"resolved={resolvedOrders.Length}");
Console.WriteLine($"unresolved={unresolvedOrders.Length}");
Console.WriteLine($"would_win={wonOrders.Length}");
Console.WriteLine($"would_lose={lostOrders.Length}");
Console.WriteLine($"hypothetical_pnl={ReportFormat.Decimal(hypotheticalPnl)}");
Console.WriteLine($"hypothetical_live_available_balance={ReportFormat.Decimal(hypotheticalBalance)}");
Console.WriteLine($"data_api_matched_target_rows={dataApiTradeMatches.Count(match => match.DataApiTradeCount > 0)}");
Console.WriteLine($"data_api_target_rows_covered_by_trade_size={dataApiTradeMatches.Count(match => match.MatchSignal == DataApiMatchSignal.SizeCovered)}");
Console.WriteLine($"matched_orders={matchedTiming.Count}");
Console.WriteLine($"matched_immediately_by_placement={matchedTiming.Count(row => row.IsImmediatePlacementMatch)}");
Console.WriteLine($"matched_by_data_api_reconciliation={matchedTiming.Count(row => row.IsDataApiReconciled)}");
Console.WriteLine($"matched_by_later_status_polling={matchedTiming.Count(row => row.IsLaterStatusPollingMatch)}");

async Task<StrategySnapshot> ReadStrategyAsync()
{
    await using var command = new NpgsqlCommand("""
SELECT
    id,
    code,
    name,
    enabled,
    live_stakes,
    live_stake_amount,
    live_available_balance
FROM strategies
WHERE code = @Code OR name = @Name
ORDER BY CASE WHEN code = @Code THEN 0 ELSE 1 END
LIMIT 1;
""", connection, tx);
    command.Parameters.AddWithValue("Code", StrategyCode);
    command.Parameters.AddWithValue("Name", StrategyName);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new InvalidOperationException($"Strategy not found: {StrategyCode} / {StrategyName}");
    }

    return new StrategySnapshot(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetBoolean(3),
        reader.GetBoolean(4),
        reader.GetDecimal(5),
        reader.GetDecimal(6));
}

async Task<IReadOnlyList<OrderReport>> ReadOrdersAsync()
{
    await using var command = new NpgsqlCommand("""
WITH target_strategy AS (
    SELECT id
    FROM strategies
    WHERE code = @Code OR name = @Name
    ORDER BY CASE WHEN code = @Code THEN 0 ELSE 1 END
    LIMIT 1
),
target_orders AS (
    SELECT
        live_order.*,
        gamma.market_id AS gamma_market_id,
        gamma.slug AS gamma_slug,
        gamma.question AS gamma_question,
        gamma.end_date_utc AS gamma_end_date_utc
    FROM live_orders live_order
    JOIN target_strategy strategy
        ON strategy.id = live_order.strategy_id
    LEFT JOIN polymarket_gamma_markets gamma
        ON gamma.condition_id = live_order.condition_id
    WHERE live_order.status IN ('Cancelled', 'CancelFailed')
),
resolved AS (
    SELECT
        target.id AS live_order_id,
        result.winning_outcome,
        result.winning_asset_id,
        result.result_source,
        result.result_at_utc
    FROM target_orders target
    LEFT JOIN LATERAL (
        SELECT *
        FROM (
            SELECT
                live_result.winning_outcome,
                live_result.winning_asset_id,
                live_result.result_source,
                live_result.result_at_utc,
                1 AS priority
            FROM (
                SELECT
                    live_settled.winning_outcome,
                    live_settled.winning_asset_id,
                    'live_orders_same_condition' AS result_source,
                    live_settled.settled_at_utc AS result_at_utc
                FROM live_orders live_settled
                WHERE live_settled.condition_id = target.condition_id
                  AND live_settled.winning_outcome IS NOT NULL
                ORDER BY live_settled.settled_at_utc DESC NULLS LAST, live_settled.updated_at_utc DESC
                LIMIT 1
            ) live_result

            UNION ALL

            SELECT
                websocket_result.winning_outcome,
                websocket_result.winning_asset_id,
                websocket_result.result_source,
                websocket_result.result_at_utc,
                2 AS priority
            FROM (
                SELECT
                    websocket.winning_outcome,
                    websocket.winning_asset_id,
                    'crypto_up_down_5m_websocket_resolved_markets:' || websocket.source AS result_source,
                    websocket.event_timestamp_utc AS result_at_utc
                FROM crypto_up_down_5m_websocket_resolved_markets websocket
                WHERE websocket.condition_id = target.condition_id
                ORDER BY websocket.event_timestamp_utc ASC
                LIMIT 1
            ) websocket_result

            UNION ALL

            SELECT
                polling_result.winning_outcome,
                polling_result.winning_asset_id,
                polling_result.result_source,
                polling_result.result_at_utc,
                3 AS priority
            FROM (
                SELECT
                    polling.winning_outcome,
                    NULL::text AS winning_asset_id,
                    'crypto_up_down_5m_result_polling_observations' AS result_source,
                    COALESCE(polling.first_winner_at_utc, polling.updated_at_utc) AS result_at_utc
                FROM crypto_up_down_5m_result_polling_observations polling
                WHERE polling.condition_id = target.condition_id
                  AND polling.winning_outcome IS NOT NULL
                ORDER BY COALESCE(polling.first_winner_at_utc, polling.updated_at_utc) ASC
                LIMIT 1
            ) polling_result

            UNION ALL

            SELECT
                token_result.winning_outcome,
                token_result.winning_asset_id,
                token_result.result_source,
                token_result.result_at_utc,
                4 AS priority
            FROM (
                SELECT
                    token.winning_outcome,
                    (
                        SELECT winner_token.token_id
                        FROM polymarket_onchain_token_metadata winner_token
                        WHERE winner_token.condition_id = token.condition_id
                          AND winner_token.winning_outcome IS NOT NULL
                          AND winner_token.outcome = token.winning_outcome
                        ORDER BY winner_token.last_refreshed_utc DESC
                        LIMIT 1
                    ) AS winning_asset_id,
                    'polymarket_onchain_token_metadata' AS result_source,
                    token.last_refreshed_utc AS result_at_utc
                FROM polymarket_onchain_token_metadata token
                WHERE token.condition_id = target.condition_id
                  AND token.resolved
                  AND token.winning_outcome IS NOT NULL
                ORDER BY token.last_refreshed_utc DESC
                LIMIT 1
            ) token_result

            UNION ALL

            SELECT
                settlement_result.winning_outcome,
                settlement_result.winning_asset_id,
                settlement_result.result_source,
                settlement_result.result_at_utc,
                5 AS priority
            FROM (
                SELECT
                    settlement.winning_outcome,
                    settlement.winning_asset_id,
                    'paper_position_settlements:' || settlement.settlement_source AS result_source,
                    settlement.settled_at_utc AS result_at_utc
                FROM paper_position_settlements settlement
                WHERE settlement.condition_id = target.condition_id
                ORDER BY settlement.settled_at_utc DESC
                LIMIT 1
            ) settlement_result
        ) candidates
        ORDER BY priority
        LIMIT 1
    ) result ON true
    ORDER BY result.priority
)
SELECT
    target.id,
    target.order_id,
    target.status,
    target.cancel_status,
    target.response_status,
    target.side,
    target.outcome,
    target.asset_id,
    target.condition_id,
    COALESCE(target.gamma_market_id, '') AS market_id,
    COALESCE(target.gamma_slug, '') AS market_slug,
    COALESCE(target.gamma_question, '') AS market_question,
    target.price,
    target.size_shares,
    target.notional_usd,
    target.filled_size,
    target.remaining_size,
    target.average_fill_price,
    target.fee_usd,
    target.created_at_utc,
    target.expires_at_utc,
    target.submitted_at_utc,
    target.updated_at_utc,
    target.gamma_end_date_utc,
    resolved.winning_outcome,
    resolved.winning_asset_id,
    resolved.result_source,
    resolved.result_at_utc,
    CASE
        WHEN resolved.winning_outcome IS NULL THEN NULL
        ELSE lower(target.outcome) = lower(resolved.winning_outcome)
    END AS would_win,
    CASE
        WHEN resolved.winning_outcome IS NULL THEN NULL
        WHEN lower(target.outcome) = lower(resolved.winning_outcome) THEN target.size_shares
        ELSE 0
    END AS hypothetical_settlement_value_usd,
    CASE
        WHEN resolved.winning_outcome IS NULL THEN NULL
        ELSE ((COALESCE(target.average_fill_price, target.price) * target.size_shares) + target.fee_usd)
    END AS hypothetical_cost_basis_usd,
    CASE
        WHEN resolved.winning_outcome IS NULL THEN NULL
        WHEN lower(target.outcome) = lower(resolved.winning_outcome)
            THEN target.size_shares - ((COALESCE(target.average_fill_price, target.price) * target.size_shares) + target.fee_usd)
        ELSE -((COALESCE(target.average_fill_price, target.price) * target.size_shares) + target.fee_usd)
    END AS hypothetical_realized_pnl_usd
FROM target_orders target
LEFT JOIN resolved
    ON resolved.live_order_id = target.id
ORDER BY target.created_at_utc ASC, target.id;
""", connection, tx);
    command.Parameters.AddWithValue("Code", StrategyCode);
    command.Parameters.AddWithValue("Name", StrategyName);

    var rows = new List<OrderReport>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new OrderReport(
            reader.GetGuid(0),
            ReadNullableString(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetDecimal(12),
            reader.GetDecimal(13),
            reader.GetDecimal(14),
            reader.GetDecimal(15),
            reader.GetDecimal(16),
            ReadNullableDecimal(reader, 17),
            reader.GetDecimal(18),
            reader.GetFieldValue<DateTime>(19),
            reader.GetFieldValue<DateTime>(20),
            ReadNullableDateTime(reader, 21),
            reader.GetFieldValue<DateTime>(22),
            ReadNullableDateTime(reader, 23),
            ReadNullableString(reader, 24),
            ReadNullableString(reader, 25),
            ReadNullableString(reader, 26),
            ReadNullableDateTime(reader, 27),
            ReadNullableBool(reader, 28),
            ReadNullableDecimal(reader, 29),
            ReadNullableDecimal(reader, 30),
            ReadNullableDecimal(reader, 31)));
    }

    return rows;
}

async Task<IReadOnlyList<StatusDetailReport>> ReadStatusDetailsAsync()
{
    await using var command = new NpgsqlCommand("""
WITH target_strategy AS (
    SELECT id
    FROM strategies
    WHERE code = @Code OR name = @Name
    ORDER BY CASE WHEN code = @Code THEN 0 ELSE 1 END
    LIMIT 1
)
SELECT
    live_order.created_at_utc,
    live_order.submitted_at_utc,
    live_order.expires_at_utc,
    live_order.updated_at_utc,
    gamma.end_date_utc AS market_end_utc,
    live_order.status,
    live_order.response_status,
    live_order.cancel_status,
    live_order.order_type,
    live_order.post_only,
    live_order.price,
    live_order.size_shares,
    live_order.filled_size,
    live_order.remaining_size,
    live_order.average_fill_price,
    live_order.filled_notional_usd,
    live_order.cost_basis_usd,
    paper_order.raw_decision_json ->> 'decision_source' AS decision_source,
    paper_order.raw_decision_json ->> 'order_ttl_seconds' AS decision_order_ttl_seconds,
    paper_order.raw_decision_json ->> 'configured_order_ttl_seconds' AS configured_order_ttl_seconds,
    paper_order.raw_decision_json ->> 'market_end_expire_before_seconds' AS market_end_expire_before_seconds,
    paper_order.raw_decision_json ->> 'gtd_expiration_utc' AS decision_gtd_expiration_utc,
    paper_order.raw_decision_json ->> 'cancel_deadline_utc' AS decision_cancel_deadline_utc,
    paper_order.raw_decision_json ->> 'clob_wire_gtd_expiration_utc' AS clob_wire_gtd_expiration_utc,
    paper_order.raw_decision_json ->> 'instant_best_bid' AS instant_best_bid,
    paper_order.raw_decision_json ->> 'instant_best_ask' AS instant_best_ask,
    paper_order.raw_decision_json ->> 'instant_spread' AS instant_spread,
    paper_order.raw_decision_json ->> 'quote_age_ms' AS quote_age_ms,
    paper_order.raw_decision_json ->> 'decision_delay_ms' AS decision_delay_ms,
    CASE
        WHEN gamma.end_date_utc IS NULL THEN NULL
        ELSE EXTRACT(EPOCH FROM gamma.end_date_utc - live_order.expires_at_utc)::numeric
    END AS seconds_expire_before_market_end,
    EXTRACT(EPOCH FROM live_order.expires_at_utc - live_order.created_at_utc)::numeric AS actual_local_ttl_seconds,
    EXTRACT(EPOCH FROM live_order.updated_at_utc - live_order.expires_at_utc)::numeric AS updated_after_expiry_seconds,
    left(live_order.raw_response_json::text, 800) AS raw_response_excerpt,
    gamma.slug AS market_slug,
    live_order.order_id,
    live_order.id AS live_order_id
FROM live_orders live_order
JOIN target_strategy strategy
    ON strategy.id = live_order.strategy_id
LEFT JOIN paper_orders paper_order
    ON paper_order.id = live_order.paper_order_id
LEFT JOIN polymarket_gamma_markets gamma
    ON gamma.condition_id = live_order.condition_id
WHERE live_order.status IN ('Cancelled', 'CancelFailed')
ORDER BY live_order.created_at_utc ASC, live_order.id ASC;
""", connection, tx);
    command.Parameters.AddWithValue("Code", StrategyCode);
    command.Parameters.AddWithValue("Name", StrategyName);

    var details = new List<StatusDetailReport>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        details.Add(new StatusDetailReport(
            reader.GetFieldValue<DateTime>(0),
            ReadNullableDateTime(reader, 1),
            reader.GetFieldValue<DateTime>(2),
            reader.GetFieldValue<DateTime>(3),
            ReadNullableDateTime(reader, 4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetBoolean(9),
            reader.GetDecimal(10),
            reader.GetDecimal(11),
            reader.GetDecimal(12),
            reader.GetDecimal(13),
            reader.IsDBNull(14) ? null : reader.GetDecimal(14),
            reader.GetDecimal(15),
            reader.GetDecimal(16),
            ReadNullableString(reader, 17),
            ReadNullableString(reader, 18),
            ReadNullableString(reader, 19),
            ReadNullableString(reader, 20),
            ReadNullableString(reader, 21),
            ReadNullableString(reader, 22),
            ReadNullableString(reader, 23),
            ReadNullableString(reader, 24),
            ReadNullableString(reader, 25),
            ReadNullableString(reader, 26),
            ReadNullableString(reader, 27),
            ReadNullableString(reader, 28),
            reader.IsDBNull(29) ? null : reader.GetDecimal(29),
            reader.IsDBNull(30) ? null : reader.GetDecimal(30),
            reader.IsDBNull(31) ? null : reader.GetDecimal(31),
            reader.GetString(32),
            ReadNullableString(reader, 33),
            ReadNullableString(reader, 34),
            reader.GetGuid(35)));
    }

    return details;
}

async Task<IReadOnlyList<DataApiTradeMatchReport>> ReadDataApiTradeMatchesAsync(IReadOnlyList<OrderReport> targetOrders)
{
    if (targetOrders.Count == 0)
    {
        return [];
    }

    var earliestTargetUtc = targetOrders
        .Select(order => new DateTimeOffset(DateTime.SpecifyKind(order.CreatedAtUtc, DateTimeKind.Utc)))
        .Min()
        .AddHours(-1);
    var latestTargetUtc = targetOrders
        .Select(order => new DateTimeOffset(DateTime.SpecifyKind(order.MarketEndUtc ?? order.UpdatedAtUtc, DateTimeKind.Utc)))
        .Max()
        .AddHours(1);

    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PolyCopyTrader", "1.0"));

    var allTrades = new List<DataApiTradeSnapshot>();
    const int limit = 500;
    for (var offset = 0; offset < 5000; offset += limit)
    {
        var uri = new UriBuilder(DataApiBaseUrl)
        {
            Path = "trades",
            Query = string.Join('&',
            [
                "user=" + Uri.EscapeDataString(FunderAddress),
                "takerOnly=false",
                "limit=" + limit.ToString(CultureInfo.InvariantCulture),
                "offset=" + offset.ToString(CultureInfo.InvariantCulture),
                "timestamp=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            ])
        }.Uri;

        using var response = await httpClient.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            break;
        }

        var pageTrades = document.RootElement
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(ParseDataApiTrade)
            .Where(trade => trade.TimestampUtc >= earliestTargetUtc && trade.TimestampUtc <= latestTargetUtc)
            .ToArray();
        allTrades.AddRange(pageTrades);

        var pageLength = document.RootElement.GetArrayLength();
        if (pageLength < limit)
        {
            break;
        }

        var oldestPageTradeUtc = document.RootElement
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => FromUnixSeconds(ReadLong(item, "timestamp")))
            .DefaultIfEmpty(DateTimeOffset.MaxValue)
            .Min();
        if (oldestPageTradeUtc < earliestTargetUtc)
        {
            break;
        }
    }

    return targetOrders
        .Select(order =>
        {
            var matchingTrades = allTrades
                .Where(trade =>
                    string.Equals(trade.ConditionId, order.ConditionId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(trade.AssetId, order.AssetId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(trade.Side, "BUY", StringComparison.OrdinalIgnoreCase))
                .OrderBy(trade => trade.TimestampUtc)
                .ToArray();
            var totalSize = matchingTrades.Sum(trade => trade.Size);
            var totalNotional = matchingTrades.Sum(trade => trade.Price * trade.Size);
            var weightedAveragePrice = totalSize > 0m ? totalNotional / totalSize : (decimal?)null;
            var signal = matchingTrades.Length == 0
                ? DataApiMatchSignal.None
                : totalSize >= order.SizeShares - 0.000001m
                    ? DataApiMatchSignal.SizeCovered
                    : DataApiMatchSignal.SomeTradeSameAsset;

            return new DataApiTradeMatchReport(
                order.Id,
                order.CreatedAtUtc,
                order.MarketSlug,
                order.Status,
                order.Price,
                order.SizeShares,
                order.AssetId,
                order.ConditionId,
                matchingTrades.Length,
                totalSize,
                weightedAveragePrice,
                matchingTrades.FirstOrDefault()?.TimestampUtc,
                matchingTrades.LastOrDefault()?.TimestampUtc,
                signal,
                string.Join(',', matchingTrades.Select(trade => ShortId(trade.TransactionHash)).Where(value => value.Length > 0).Distinct()));
        })
        .ToArray();
}

async Task<IReadOnlyList<MatchedTimingReport>> ReadMatchedTimingAsync()
{
    await using var command = new NpgsqlCommand("""
WITH target_strategy AS (
    SELECT id
    FROM strategies
    WHERE code = @Code OR name = @Name
    ORDER BY CASE WHEN code = @Code THEN 0 ELSE 1 END
    LIMIT 1
)
SELECT
    live_order.created_at_utc,
    live_order.submitted_at_utc,
    live_order.updated_at_utc,
    live_order.settled_at_utc,
    gamma.end_date_utc AS market_end_utc,
    live_order.status,
    live_order.response_status,
    live_order.price,
    live_order.size_shares,
    live_order.filled_size,
    live_order.remaining_size,
    live_order.average_fill_price,
    live_order.cost_basis_usd,
    live_order.realized_pnl_usd,
    CASE
        WHEN live_order.submitted_at_utc IS NULL THEN NULL
        ELSE EXTRACT(EPOCH FROM live_order.updated_at_utc - live_order.submitted_at_utc)::numeric
    END AS update_after_submit_seconds,
    CASE
        WHEN live_order.submitted_at_utc IS NULL OR live_order.settled_at_utc IS NULL THEN NULL
        ELSE EXTRACT(EPOCH FROM live_order.settled_at_utc - live_order.submitted_at_utc)::numeric
    END AS settlement_after_submit_seconds,
    gamma.slug AS market_slug,
    live_order.order_id,
    live_order.id AS live_order_id
FROM live_orders live_order
JOIN target_strategy strategy
    ON strategy.id = live_order.strategy_id
LEFT JOIN polymarket_gamma_markets gamma
    ON gamma.condition_id = live_order.condition_id
WHERE live_order.status = 'Matched'
ORDER BY live_order.created_at_utc ASC, live_order.id ASC;
""", connection, tx);
    command.Parameters.AddWithValue("Code", StrategyCode);
    command.Parameters.AddWithValue("Name", StrategyName);

    var rows = new List<MatchedTimingReport>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new MatchedTimingReport(
            reader.GetFieldValue<DateTime>(0),
            ReadNullableDateTime(reader, 1),
            reader.GetFieldValue<DateTime>(2),
            ReadNullableDateTime(reader, 3),
            ReadNullableDateTime(reader, 4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            ReadNullableDecimal(reader, 11),
            reader.GetDecimal(12),
            ReadNullableDecimal(reader, 13),
            ReadNullableDecimal(reader, 14),
            ReadNullableDecimal(reader, 15),
            ReadNullableString(reader, 16),
            ReadNullableString(reader, 17),
            reader.GetGuid(18)));
    }

    return rows;
}

static DataApiTradeSnapshot ParseDataApiTrade(JsonElement item)
{
    return new DataApiTradeSnapshot(
        ReadString(item, "asset"),
        ReadString(item, "conditionId"),
        ReadString(item, "side"),
        ReadDecimal(item, "price"),
        ReadDecimal(item, "size"),
        FromUnixSeconds(ReadLong(item, "timestamp")),
        ReadString(item, "transactionHash"));
}

static string ReadString(JsonElement element, string propertyName)
{
    if (element.ValueKind != JsonValueKind.Object ||
        !element.TryGetProperty(propertyName, out var property) ||
        property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
    {
        return string.Empty;
    }

    return property.ValueKind == JsonValueKind.String
        ? property.GetString() ?? string.Empty
        : property.ToString();
}

static decimal ReadDecimal(JsonElement element, string propertyName)
{
    var value = ReadString(element, propertyName);
    return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : 0m;
}

static long ReadLong(JsonElement element, string propertyName)
{
    var value = ReadString(element, propertyName);
    return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : 0L;
}

static DateTimeOffset FromUnixSeconds(long value)
{
    return value <= 0
        ? DateTimeOffset.MinValue
        : DateTimeOffset.FromUnixTimeSeconds(value);
}

static string ShortId(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    return value.Length <= 14
        ? value
        : string.Concat(value.AsSpan(0, 6), "...", value.AsSpan(value.Length - 4, 4));
}

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    await command.ExecuteNonQueryAsync();
}

async Task<object?> ScalarAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    return await command.ExecuteScalarAsync();
}

static string? ReadNullableString(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}

static decimal? ReadNullableDecimal(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}

static bool? ReadNullableBool(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
}

static DateTime? ReadNullableDateTime(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTime>(ordinal);
}

sealed record StrategySnapshot(
    Guid Id,
    string Code,
    string Name,
    bool Enabled,
    bool LiveStakes,
    decimal LiveStakeAmount,
    decimal LiveAvailableBalance);

sealed record OrderReport(
    Guid Id,
    string? OrderId,
    string Status,
    string CancelStatus,
    string ResponseStatus,
    string Side,
    string Outcome,
    string AssetId,
    string ConditionId,
    string MarketId,
    string MarketSlug,
    string MarketQuestion,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    decimal FilledSize,
    decimal RemainingSize,
    decimal? AverageFillPrice,
    decimal FeeUsd,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? SubmittedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? MarketEndUtc,
    string? WinningOutcome,
    string? WinningAssetId,
    string? ResultSource,
    DateTime? ResultAtUtc,
    bool? WouldWin,
    decimal? HypotheticalSettlementValueUsd,
    decimal? HypotheticalCostBasisUsd,
    decimal? HypotheticalRealizedPnlUsd)
{
    public static readonly string[] Columns =
    [
        "created_utc",
        "updated_utc",
        "market_end_utc",
        "status",
        "cancel_status",
        "response_status",
        "outcome",
        "winning_outcome",
        "would_win",
        "price",
        "size_shares",
        "notional_usd",
        "hypothetical_cost_basis_usd",
        "hypothetical_settlement_value_usd",
        "hypothetical_realized_pnl_usd",
        "result_source",
        "market_slug",
        "condition_id",
        "asset_id",
        "winning_asset_id",
        "order_id",
        "live_order_id",
        "market_question"
    ];

    public bool IsResolved => !string.IsNullOrWhiteSpace(WinningOutcome) && HypotheticalRealizedPnlUsd.HasValue;

    public string ToTsv()
    {
        return string.Join('\t',
            DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
            DateTime.SpecifyKind(UpdatedAtUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
            ReportFormat.NullableDateTime(MarketEndUtc),
            ReportFormat.EscapeTsv(Status),
            ReportFormat.EscapeTsv(CancelStatus),
            ReportFormat.EscapeTsv(ResponseStatus),
            ReportFormat.EscapeTsv(Outcome),
            ReportFormat.EscapeTsv(WinningOutcome),
            ReportFormat.NullableBool(WouldWin),
            ReportFormat.Decimal(Price),
            ReportFormat.Decimal(SizeShares),
            ReportFormat.Decimal(NotionalUsd),
            ReportFormat.NullableDecimal(HypotheticalCostBasisUsd),
            ReportFormat.NullableDecimal(HypotheticalSettlementValueUsd),
            ReportFormat.NullableDecimal(HypotheticalRealizedPnlUsd),
            ReportFormat.EscapeTsv(ResultSource),
            ReportFormat.EscapeTsv(MarketSlug),
            ReportFormat.EscapeTsv(ConditionId),
            ReportFormat.EscapeTsv(AssetId),
            ReportFormat.EscapeTsv(WinningAssetId),
            ReportFormat.EscapeTsv(OrderId),
            Id.ToString(),
            ReportFormat.EscapeTsv(MarketQuestion));
    }
}

sealed record StatusDetailReport(
    DateTime CreatedUtc,
    DateTime? SubmittedUtc,
    DateTime ExpiresUtc,
    DateTime UpdatedUtc,
    DateTime? MarketEndUtc,
    string Status,
    string ResponseStatus,
    string CancelStatus,
    string OrderType,
    bool? PostOnly,
    decimal Price,
    decimal SizeShares,
    decimal FilledSize,
    decimal RemainingSize,
    decimal? AverageFillPrice,
    decimal FilledNotionalUsd,
    decimal CostBasisUsd,
    string? DecisionSource,
    string? DecisionOrderTtlSeconds,
    string? ConfiguredOrderTtlSeconds,
    string? MarketEndExpireBeforeSeconds,
    string? DecisionGtdExpirationUtc,
    string? DecisionCancelDeadlineUtc,
    string? ClobWireGtdExpirationUtc,
    string? InstantBestBid,
    string? InstantBestAsk,
    string? InstantSpread,
    string? QuoteAgeMs,
    string? DecisionDelayMs,
    decimal? SecondsExpireBeforeMarketEnd,
    decimal? ActualLocalTtlSeconds,
    decimal? UpdatedAfterExpirySeconds,
    string RawResponseExcerpt,
    string? MarketSlug,
    string? OrderId,
    Guid LiveOrderId)
{
    public static readonly string[] Columns =
    [
        "created_utc",
        "submitted_utc",
        "expires_utc",
        "updated_utc",
        "market_end_utc",
        "status",
        "response_status",
        "cancel_status",
        "order_type",
        "post_only",
        "price",
        "size_shares",
        "filled_size",
        "remaining_size",
        "average_fill_price",
        "filled_notional_usd",
        "cost_basis_usd",
        "decision_source",
        "decision_order_ttl_seconds",
        "configured_order_ttl_seconds",
        "market_end_expire_before_seconds",
        "decision_gtd_expiration_utc",
        "decision_cancel_deadline_utc",
        "clob_wire_gtd_expiration_utc",
        "instant_best_bid",
        "instant_best_ask",
        "instant_spread",
        "quote_age_ms",
        "decision_delay_ms",
        "seconds_expire_before_market_end",
        "actual_local_ttl_seconds",
        "updated_after_expiry_seconds",
        "raw_response_excerpt",
        "market_slug",
        "order_id",
        "live_order_id"
    ];

    public string ToTsv()
    {
        var values = new object?[]
        {
            CreatedUtc,
            SubmittedUtc,
            ExpiresUtc,
            UpdatedUtc,
            MarketEndUtc,
            Status,
            ResponseStatus,
            CancelStatus,
            OrderType,
            PostOnly,
            Price,
            SizeShares,
            FilledSize,
            RemainingSize,
            AverageFillPrice,
            FilledNotionalUsd,
            CostBasisUsd,
            DecisionSource,
            DecisionOrderTtlSeconds,
            ConfiguredOrderTtlSeconds,
            MarketEndExpireBeforeSeconds,
            DecisionGtdExpirationUtc,
            DecisionCancelDeadlineUtc,
            ClobWireGtdExpirationUtc,
            InstantBestBid,
            InstantBestAsk,
            InstantSpread,
            QuoteAgeMs,
            DecisionDelayMs,
            SecondsExpireBeforeMarketEnd,
            ActualLocalTtlSeconds,
            UpdatedAfterExpirySeconds,
            RawResponseExcerpt,
            MarketSlug,
            OrderId,
            LiveOrderId
        };

        return string.Join('\t', values.Select(ReportFormat.Object));
    }
}

enum DataApiMatchSignal
{
    None,
    SomeTradeSameAsset,
    SizeCovered
}

sealed record DataApiTradeSnapshot(
    string AssetId,
    string ConditionId,
    string Side,
    decimal Price,
    decimal Size,
    DateTimeOffset TimestampUtc,
    string TransactionHash);

sealed record DataApiTradeMatchReport(
    Guid LiveOrderId,
    DateTime CreatedUtc,
    string MarketSlug,
    string Status,
    decimal OrderPrice,
    decimal OrderSizeShares,
    string AssetId,
    string ConditionId,
    int DataApiTradeCount,
    decimal DataApiTotalSize,
    decimal? DataApiWeightedAveragePrice,
    DateTimeOffset? FirstTradeUtc,
    DateTimeOffset? LastTradeUtc,
    DataApiMatchSignal MatchSignal,
    string TransactionHashes)
{
    public static readonly string[] Columns =
    [
        "live_order_id",
        "created_utc",
        "market_slug",
        "status",
        "order_price",
        "order_size_shares",
        "asset_id",
        "condition_id",
        "data_api_trade_count",
        "data_api_total_size",
        "data_api_weighted_average_price",
        "first_trade_utc",
        "last_trade_utc",
        "match_signal",
        "transaction_hashes"
    ];

    public string ToTsv()
    {
        var values = new object?[]
        {
            LiveOrderId,
            CreatedUtc,
            MarketSlug,
            Status,
            OrderPrice,
            OrderSizeShares,
            AssetId,
            ConditionId,
            DataApiTradeCount,
            DataApiTotalSize,
            DataApiWeightedAveragePrice,
            FirstTradeUtc,
            LastTradeUtc,
            MatchSignal,
            TransactionHashes
        };

        return string.Join('\t', values.Select(ReportFormat.Object));
    }
}

sealed record MatchedTimingReport(
    DateTime CreatedUtc,
    DateTime? SubmittedUtc,
    DateTime UpdatedUtc,
    DateTime? SettledUtc,
    DateTime? MarketEndUtc,
    string Status,
    string ResponseStatus,
    decimal Price,
    decimal SizeShares,
    decimal FilledSize,
    decimal RemainingSize,
    decimal? AverageFillPrice,
    decimal CostBasisUsd,
    decimal? RealizedPnlUsd,
    decimal? UpdateAfterSubmitSeconds,
    decimal? SettlementAfterSubmitSeconds,
    string? MarketSlug,
    string? OrderId,
    Guid LiveOrderId)
{
    public bool IsImmediatePlacementMatch =>
        string.Equals(ResponseStatus, "matched", StringComparison.OrdinalIgnoreCase) &&
        UpdateAfterSubmitSeconds is not null and <= 5m;

    public bool IsDataApiReconciled =>
        ResponseStatus.Contains("data_api", StringComparison.OrdinalIgnoreCase);

    public bool IsLaterStatusPollingMatch =>
        !IsImmediatePlacementMatch &&
        !IsDataApiReconciled &&
        string.Equals(Status, "Matched", StringComparison.OrdinalIgnoreCase);

    public static readonly string[] Columns =
    [
        "created_utc",
        "submitted_utc",
        "updated_utc",
        "settled_utc",
        "market_end_utc",
        "status",
        "response_status",
        "classification",
        "price",
        "size_shares",
        "filled_size",
        "remaining_size",
        "average_fill_price",
        "cost_basis_usd",
        "realized_pnl_usd",
        "update_after_submit_seconds",
        "settlement_after_submit_seconds",
        "market_slug",
        "order_id",
        "live_order_id"
    ];

    public string Classification =>
        IsImmediatePlacementMatch ? "immediate_placement_matched" :
        IsDataApiReconciled ? "data_api_reconciled" :
        IsLaterStatusPollingMatch ? "later_status_polling_matched" :
        "other";

    public string ToTsv()
    {
        var values = new object?[]
        {
            CreatedUtc,
            SubmittedUtc,
            UpdatedUtc,
            SettledUtc,
            MarketEndUtc,
            Status,
            ResponseStatus,
            Classification,
            Price,
            SizeShares,
            FilledSize,
            RemainingSize,
            AverageFillPrice,
            CostBasisUsd,
            RealizedPnlUsd,
            UpdateAfterSubmitSeconds,
            SettlementAfterSubmitSeconds,
            MarketSlug,
            OrderId,
            LiveOrderId
        };

        return string.Join('\t', values.Select(ReportFormat.Object));
    }
}

static class ReportFormat
{
    public static string Decimal(decimal value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    public static string NullableDecimal(decimal? value)
    {
        return value.HasValue ? Decimal(value.Value) : string.Empty;
    }

    public static string NullableDateTime(DateTime? value)
    {
        return value.HasValue
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    public static string NullableBool(bool? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    public static string EscapeTsv(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    public static string Object(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => EscapeTsv(text),
            DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            decimal decimalValue => Decimal(decimalValue),
            bool boolValue => boolValue.ToString(CultureInfo.InvariantCulture),
            _ => EscapeTsv(Convert.ToString(value, CultureInfo.InvariantCulture))
        };
    }
}
