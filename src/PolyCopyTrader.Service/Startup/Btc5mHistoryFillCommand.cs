using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Startup;

public static class Btc5mHistoryFillCommand
{
    public const string CommandFlag = "--fill-btc-5m-history";

    private const int MarketDurationSeconds = 300;
    private const int FirstSampleSecond = 2;
    private const int SampleStepSeconds = 5;
    private const int CentsBucketSize = 5;
    private const int BinanceAggTradesLimit = 1000;

    private static readonly Regex BtcFiveMinuteSlugRegex = new(
        "^btc-updown-5m-(?<unix>\\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        if (!StorageConnectionResolver.IsConfigured(configuration.Storage))
        {
            await output.WriteLineAsync("PostgreSQL storage is not configured.");
            return 1;
        }

        var options = Btc5mHistoryFillOptions.FromArgs(args);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds) };
        var database = new Btc5mHistoryDatabase(new PostgresConnectionFactory(configuration.Storage));
        var binance = new BinanceAggTradeClient(httpClient, options.BinanceBaseUrl, options.BinanceRequestDelayMilliseconds);

        return await ExecuteAsync(database, binance, options, output, cancellationToken);
    }

    public static async Task<int> ExecuteAsync(
        IBtc5mHistoryDatabase database,
        IBinanceAggTradeClient binance,
        Btc5mHistoryFillOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(binance);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        await output.WriteLineAsync("Loading closed/resolved BTC Up or Down 5m markets from PostgreSQL history...");
        var markets = await database.LoadClosedBtc5mMarketsAsync(options, cancellationToken);
        if (markets.Count == 0)
        {
            await output.WriteLineAsync("No closed/resolved BTC Up or Down 5m markets found.");
            return 1;
        }

        await output.WriteLineAsync($"Markets loaded: {markets.Count.ToString(CultureInfo.InvariantCulture)}");
        if (options.DryRun)
        {
            await output.WriteLineAsync("Dry run: btc_5m_history will not be truncated or written.");
        }
        else
        {
            await output.WriteLineAsync("Truncating btc_5m_history with RESTART IDENTITY...");
            await database.TruncateHistoryAsync(cancellationToken);
        }

        var summary = new Btc5mHistoryFillSummary(MarketsLoaded: markets.Count);
        for (var marketIndex = 0; marketIndex < markets.Count; marketIndex++)
        {
            var market = markets[marketIndex];
            if (!IsKnownResult(market.Result))
            {
                summary = summary with { MarketsSkippedUnknownResult = summary.MarketsSkippedUnknownResult + 1 };
                continue;
            }

            var tradesFromUtc = market.StartUtc.AddMinutes(-5);
            var tradesToUtc = market.EndUtc;
            IReadOnlyList<BinanceAggTrade> trades;
            try
            {
                trades = await binance.GetAggTradesAsync(tradesFromUtc, tradesToUtc, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                summary = summary with { MarketsSkippedBinanceError = summary.MarketsSkippedBinanceError + 1 };
                await output.WriteLineAsync(
                    $"Skipped {market.Slug}: Binance BTCUSDT load failed: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (trades.Count == 0)
            {
                summary = summary with { MarketsSkippedNoTrades = summary.MarketsSkippedNoTrades + 1 };
                await output.WriteLineAsync($"Skipped {market.Slug}: Binance BTCUSDT trade history is empty.");
                continue;
            }

            var cache = options.DryRun
                ? []
                : await database.LoadHistoryCacheAsync(cancellationToken);
            var buildResult = BuildMarketCacheUpdates(market, trades, cache);
            if (buildResult.SkipReason is not null)
            {
                summary = buildResult.SkipReason switch
                {
                    Btc5mHistoryMarketSkipReason.NoBtcStart => summary with { MarketsSkippedNoBtcStart = summary.MarketsSkippedNoBtcStart + 1 },
                    Btc5mHistoryMarketSkipReason.UnknownResult => summary with { MarketsSkippedUnknownResult = summary.MarketsSkippedUnknownResult + 1 },
                    _ => summary with { MarketsSkippedNoTrades = summary.MarketsSkippedNoTrades + 1 }
                };
                await output.WriteLineAsync($"Skipped {market.Slug}: {buildResult.SkipReason.Value}.");
                continue;
            }

            if (!options.DryRun)
            {
                await database.SaveHistoryCacheChangesAsync(buildResult.ChangedRows, cancellationToken);
            }

            summary = summary with
            {
                MarketsProcessed = summary.MarketsProcessed + 1,
                PointsInserted = summary.PointsInserted + buildResult.PointsInserted,
                PointsUpdated = summary.PointsUpdated + buildResult.PointsUpdated,
                PointsSkippedMissingPrice = summary.PointsSkippedMissingPrice + buildResult.PointsSkippedMissingPrice
            };

            if (options.ProgressEveryMarkets > 0 && (marketIndex + 1) % options.ProgressEveryMarkets == 0)
            {
                await output.WriteLineAsync(
                    $"Progress: {(marketIndex + 1).ToString(CultureInfo.InvariantCulture)}/{markets.Count.ToString(CultureInfo.InvariantCulture)} markets, " +
                    $"processed={summary.MarketsProcessed.ToString(CultureInfo.InvariantCulture)}, " +
                    $"inserted={summary.PointsInserted.ToString(CultureInfo.InvariantCulture)}, " +
                    $"updated={summary.PointsUpdated.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        await output.WriteLineAsync("BTC 5m history fill complete.");
        await output.WriteLineAsync(summary.ToDisplayString());
        return summary.MarketsProcessed > 0 ? 0 : 1;
    }

    public static Btc5mHistoryMarketBuildResult BuildMarketCacheUpdates(
        Btc5mHistoryMarket market,
        IReadOnlyList<BinanceAggTrade> trades,
        IDictionary<Btc5mHistoryPointKey, Btc5mHistoryCacheRow> cache)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(cache);

        if (!IsKnownResult(market.Result))
        {
            return Btc5mHistoryMarketBuildResult.Skipped(Btc5mHistoryMarketSkipReason.UnknownResult);
        }

        var sortedTrades = trades
            .OrderBy(item => item.TradeTimeUtc)
            .ThenBy(item => item.AggregateTradeId)
            .ToArray();

        if (!TryGetLastPriceAtOrBefore(sortedTrades, market.StartUtc, out var btcStart))
        {
            return Btc5mHistoryMarketBuildResult.Skipped(Btc5mHistoryMarketSkipReason.NoBtcStart);
        }

        var pointsInserted = 0;
        var pointsUpdated = 0;
        var pointsSkippedMissingPrice = 0;

        foreach (var secCounter in EnumerateSampleCounters())
        {
            var secCounterRounded = RoundSecondsTowardZeroToFiveSeconds(secCounter);
            var targetTime = market.StartUtc.AddSeconds(secCounter);
            if (!TryGetLastPriceAtOrBefore(sortedTrades, targetTime, out var btcPrice))
            {
                pointsSkippedMissingPrice++;
                continue;
            }

            var centsRounded = RoundCentsTowardZeroToFiveCents((btcPrice - btcStart) * 100m);
            var key = new Btc5mHistoryPointKey(secCounterRounded, centsRounded);
            if (!cache.TryGetValue(key, out var row))
            {
                row = new Btc5mHistoryCacheRow(
                    id: null,
                    seconds: secCounterRounded,
                    cents: centsRounded,
                    count: 1,
                    upCount: 0,
                    downCount: 0,
                    state: Btc5mHistoryCacheState.Inserted);
                cache.Add(key, row);
                pointsInserted++;
                continue;
            }

            row.Count++;
            if (row.State == Btc5mHistoryCacheState.Loaded)
            {
                row.State = Btc5mHistoryCacheState.Updated;
                pointsUpdated++;
            }
        }

        foreach (var row in cache.Values.Where(row => row.State != Btc5mHistoryCacheState.Loaded))
        {
            if (string.Equals(market.Result, "Up", StringComparison.OrdinalIgnoreCase))
            {
                row.UpCount++;
            }
            else
            {
                row.DownCount++;
            }
        }

        return new Btc5mHistoryMarketBuildResult(
            cache.Values.Where(row => row.State != Btc5mHistoryCacheState.Loaded).ToArray(),
            pointsInserted,
            pointsUpdated,
            pointsSkippedMissingPrice,
            SkipReason: null);
    }

    public static int RoundSecondsTowardZeroToFiveSeconds(int seconds)
    {
        return seconds / SampleStepSeconds * SampleStepSeconds;
    }

    public static int RoundCentsTowardZeroToFiveCents(decimal centsDiff)
    {
        return (int)(decimal.Truncate(centsDiff / CentsBucketSize) * CentsBucketSize);
    }

    public static IReadOnlyList<int> EnumerateSampleCounters()
    {
        var counters = new List<int>();
        for (var secCounter = FirstSampleSecond; secCounter <= MarketDurationSeconds; secCounter += SampleStepSeconds)
        {
            counters.Add(secCounter);
        }

        return counters;
    }

    public static string? TryGetWinningOutcome(string rawJson, bool closed)
    {
        if (!closed || string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var outcomes = TryReadStringArray(document.RootElement, "outcomes");
            var outcomePrices = TryReadDecimalArray(document.RootElement, "outcomePrices");
            if (outcomes.Count == 0 || outcomePrices.Count != outcomes.Count)
            {
                return null;
            }

            var winners = outcomePrices
                .Select((price, index) => new { price, index })
                .Where(item => item.price >= 0.999m)
                .ToArray();
            if (winners.Length != 1)
            {
                return null;
            }

            var winner = outcomes[winners[0].index];
            if (string.Equals(winner, "Up", StringComparison.OrdinalIgnoreCase))
            {
                return "Up";
            }

            return string.Equals(winner, "Down", StringComparison.OrdinalIgnoreCase) ? "Down" : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static DateTimeOffset? TryGetWindowStartUtc(
        DateTimeOffset? eventStartUtc,
        DateTimeOffset? endUtc,
        string? slug,
        string? eventSlug)
    {
        if (eventStartUtc is { } eventStart)
        {
            return eventStart;
        }

        if (TryGetSlugStartUtc(slug) is { } slugStart)
        {
            return slugStart;
        }

        if (TryGetSlugStartUtc(eventSlug) is { } eventSlugStart)
        {
            return eventSlugStart;
        }

        return endUtc?.Subtract(TimeSpan.FromSeconds(MarketDurationSeconds));
    }

    private static bool TryGetLastPriceAtOrBefore(
        IReadOnlyList<BinanceAggTrade> sortedTrades,
        DateTimeOffset targetUtc,
        out decimal price)
    {
        price = 0m;
        var found = false;
        foreach (var trade in sortedTrades)
        {
            if (trade.TradeTimeUtc > targetUtc)
            {
                break;
            }

            price = trade.Price;
            found = true;
        }

        return found;
    }

    private static bool IsKnownResult(string? result)
    {
        return string.Equals(result, "Up", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result, "Down", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? TryGetSlugStartUtc(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var match = BtcFiveMinuteSlugRegex.Match(slug);
        if (!match.Success ||
            !long.TryParse(match.Groups["unix"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
    }

    private static IReadOnlyList<string> TryReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            return property.EnumerateArray().Select(item => item.ToString()).ToArray();
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return [];
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().Select(item => item.ToString()).ToArray()
            : [];
    }

    private static IReadOnlyList<decimal> TryReadDecimalArray(JsonElement root, string propertyName)
    {
        return TryReadStringArray(root, propertyName)
            .Select(value => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (decimal?)null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
    }

    private sealed class Btc5mHistoryDatabase(PostgresConnectionFactory connectionFactory) : IBtc5mHistoryDatabase
    {
        public async Task<IReadOnlyList<Btc5mHistoryMarket>> LoadClosedBtc5mMarketsAsync(
            Btc5mHistoryFillOptions options,
            CancellationToken cancellationToken)
        {
            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            const string sql = """
SELECT market_id, condition_id, slug, event_slug, event_start_time_utc, end_date_utc, raw_json::text
FROM polymarket_gamma_markets
WHERE closed
  AND (
      lower(slug) ~ '^btc-updown-5m-[0-9]+$'
      OR lower(COALESCE(event_slug, '')) ~ '^btc-updown-5m-[0-9]+$'
      OR lower(COALESCE(series_slug, '')) = 'btc-up-or-down-5m'
  )
ORDER BY COALESCE(event_start_time_utc, end_date_utc - interval '5 minutes', created_at_utc) ASC NULLS LAST,
         market_id ASC;
""";

            await using var command = new NpgsqlCommand(sql, connection);
            var markets = new List<Btc5mHistoryMarket>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var slug = reader.GetString(2);
                var eventSlug = reader.IsDBNull(3) ? null : reader.GetString(3);
                var eventStartUtc = ReadDateTimeOffsetOrNull(reader, 4);
                var endUtc = ReadDateTimeOffsetOrNull(reader, 5);
                var startUtc = TryGetWindowStartUtc(eventStartUtc, endUtc, slug, eventSlug);
                if (startUtc is null)
                {
                    continue;
                }

                if (options.StartUtc is { } startFilter && startUtc.Value < startFilter)
                {
                    continue;
                }

                if (options.EndUtc is { } endFilter && startUtc.Value > endFilter)
                {
                    continue;
                }

                var rawJson = reader.GetString(6);
                markets.Add(new Btc5mHistoryMarket(
                    reader.GetString(0),
                    reader.GetString(1),
                    slug,
                    startUtc.Value,
                    startUtc.Value.AddSeconds(MarketDurationSeconds),
                    TryGetWinningOutcome(rawJson, closed: true)));
            }

            return markets
                .OrderBy(market => market.StartUtc)
                .ThenBy(market => market.MarketId, StringComparer.Ordinal)
                .Take(options.MaxMarkets ?? int.MaxValue)
                .ToArray();
        }

        public async Task TruncateHistoryAsync(CancellationToken cancellationToken)
        {
            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("TRUNCATE btc_5m_history RESTART IDENTITY;", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<Dictionary<Btc5mHistoryPointKey, Btc5mHistoryCacheRow>> LoadHistoryCacheAsync(
            CancellationToken cancellationToken)
        {
            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            const string sql = """
SELECT id, seconds, cents, count, up_count, down_count
FROM btc_5m_history;
""";

            await using var command = new NpgsqlCommand(sql, connection);
            var cache = new Dictionary<Btc5mHistoryPointKey, Btc5mHistoryCacheRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var seconds = reader.GetInt32(1);
                var cents = reader.GetInt32(2);
                cache[new Btc5mHistoryPointKey(seconds, cents)] = new Btc5mHistoryCacheRow(
                    reader.GetInt64(0),
                    seconds,
                    cents,
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    Btc5mHistoryCacheState.Loaded);
            }

            return cache;
        }

        public async Task SaveHistoryCacheChangesAsync(
            IReadOnlyCollection<Btc5mHistoryCacheRow> rows,
            CancellationToken cancellationToken)
        {
            if (rows.Count == 0)
            {
                return;
            }

            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var row in rows)
            {
                if (row.State == Btc5mHistoryCacheState.Updated)
                {
                    await UpdateRowAsync(connection, transaction, row, cancellationToken);
                }
                else if (row.State == Btc5mHistoryCacheState.Inserted)
                {
                    await InsertRowAsync(connection, transaction, row, cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }

        private static async Task UpdateRowAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Btc5mHistoryCacheRow row,
            CancellationToken cancellationToken)
        {
            const string sql = """
UPDATE btc_5m_history
SET count = @Count,
    up_count = @UpCount,
    down_count = @DownCount
WHERE id = @Id;
""";

            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.Add("Id", NpgsqlDbType.Bigint).Value = row.Id!.Value;
            AddCounterParameters(command, row);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task InsertRowAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Btc5mHistoryCacheRow row,
            CancellationToken cancellationToken)
        {
            const string sql = """
INSERT INTO btc_5m_history (seconds, cents, count, up_count, down_count)
VALUES (@Seconds, @Cents, @Count, @UpCount, @DownCount)
ON CONFLICT (seconds, cents) DO UPDATE SET
    count = EXCLUDED.count,
    up_count = EXCLUDED.up_count,
    down_count = EXCLUDED.down_count;
""";

            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.Add("Seconds", NpgsqlDbType.Integer).Value = row.Seconds;
            command.Parameters.Add("Cents", NpgsqlDbType.Integer).Value = row.Cents;
            AddCounterParameters(command, row);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static void AddCounterParameters(NpgsqlCommand command, Btc5mHistoryCacheRow row)
        {
            command.Parameters.Add("Count", NpgsqlDbType.Integer).Value = row.Count;
            command.Parameters.Add("UpCount", NpgsqlDbType.Integer).Value = row.UpCount;
            command.Parameters.Add("DownCount", NpgsqlDbType.Integer).Value = row.DownCount;
        }

        private static DateTimeOffset? ReadDateTimeOffsetOrNull(NpgsqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            var value = reader.GetFieldValue<DateTime>(ordinal);
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }
    }

    private sealed class BinanceAggTradeClient(
        HttpClient httpClient,
        string binanceBaseUrl,
        int requestDelayMilliseconds) : IBinanceAggTradeClient
    {
        public async Task<IReadOnlyList<BinanceAggTrade>> GetAggTradesAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken)
        {
            if (toUtc < fromUtc)
            {
                return [];
            }

            var trades = new List<BinanceAggTrade>();
            long? nextFromId = null;
            while (true)
            {
                var requestUri = BuildAggTradesUri(fromUtc, toUtc, nextFromId);
                using var response = await httpClient.GetAsync(requestUri, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var page = ParseAggTrades(document.RootElement);
                if (page.Count == 0)
                {
                    break;
                }

                foreach (var trade in page)
                {
                    if (trade.TradeTimeUtc >= fromUtc && trade.TradeTimeUtc <= toUtc)
                    {
                        trades.Add(trade);
                    }
                }

                var last = page[^1];
                if (page.Count < BinanceAggTradesLimit || last.TradeTimeUtc > toUtc)
                {
                    break;
                }

                nextFromId = last.AggregateTradeId + 1;
                if (requestDelayMilliseconds > 0)
                {
                    await Task.Delay(requestDelayMilliseconds, cancellationToken);
                }
            }

            return trades
                .OrderBy(trade => trade.TradeTimeUtc)
                .ThenBy(trade => trade.AggregateTradeId)
                .ToArray();
        }

        private string BuildAggTradesUri(DateTimeOffset fromUtc, DateTimeOffset toUtc, long? fromId)
        {
            var normalizedBaseUrl = binanceBaseUrl.TrimEnd('/');
            if (fromId is not null)
            {
                return normalizedBaseUrl +
                    "/api/v3/aggTrades?symbol=BTCUSDT&limit=" +
                    BinanceAggTradesLimit.ToString(CultureInfo.InvariantCulture) +
                    "&fromId=" +
                    fromId.Value.ToString(CultureInfo.InvariantCulture);
            }

            return normalizedBaseUrl +
                "/api/v3/aggTrades?symbol=BTCUSDT&limit=" +
                BinanceAggTradesLimit.ToString(CultureInfo.InvariantCulture) +
                "&startTime=" +
                fromUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) +
                "&endTime=" +
                toUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        }

        private static IReadOnlyList<BinanceAggTrade> ParseAggTrades(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Binance aggTrades response must be a JSON array.");
            }

            var trades = new List<BinanceAggTrade>();
            foreach (var item in root.EnumerateArray())
            {
                var id = item.GetProperty("a").GetInt64();
                var priceText = item.GetProperty("p").GetString() ?? "0";
                if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
                {
                    throw new JsonException("Binance aggTrade price was not a decimal.");
                }

                var tradeTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(item.GetProperty("T").GetInt64());
                trades.Add(new BinanceAggTrade(id, price, tradeTimeUtc));
            }

            return trades;
        }
    }
}

public interface IBtc5mHistoryDatabase
{
    Task<IReadOnlyList<Btc5mHistoryMarket>> LoadClosedBtc5mMarketsAsync(
        Btc5mHistoryFillOptions options,
        CancellationToken cancellationToken);

    Task TruncateHistoryAsync(CancellationToken cancellationToken);

    Task<Dictionary<Btc5mHistoryPointKey, Btc5mHistoryCacheRow>> LoadHistoryCacheAsync(
        CancellationToken cancellationToken);

    Task SaveHistoryCacheChangesAsync(
        IReadOnlyCollection<Btc5mHistoryCacheRow> rows,
        CancellationToken cancellationToken);
}

public interface IBinanceAggTradeClient
{
    Task<IReadOnlyList<BinanceAggTrade>> GetAggTradesAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}

public sealed record Btc5mHistoryFillOptions(
    DateTimeOffset? StartUtc,
    DateTimeOffset? EndUtc,
    int? MaxMarkets,
    string BinanceBaseUrl,
    int HttpTimeoutSeconds,
    int BinanceRequestDelayMilliseconds,
    int ProgressEveryMarkets,
    bool DryRun)
{
    public static Btc5mHistoryFillOptions FromArgs(string[] args)
    {
        return new Btc5mHistoryFillOptions(
            ParseDateTimeOffsetOption(args, "--btc-5m-history-start-utc"),
            ParseDateTimeOffsetOption(args, "--btc-5m-history-end-utc"),
            ParseIntOption(args, "--btc-5m-history-max-markets", null, minValue: 1, maxValue: int.MaxValue),
            GetOptionValue(args, "--btc-5m-history-binance-base-url") ?? "https://api.binance.com",
            ParseIntOption(args, "--btc-5m-history-http-timeout-seconds", 30, minValue: 1, maxValue: 300)!.Value,
            ParseIntOption(args, "--btc-5m-history-binance-delay-ms", 50, minValue: 0, maxValue: 60_000)!.Value,
            ParseIntOption(args, "--btc-5m-history-progress-every", 100, minValue: 0, maxValue: 1_000_000)!.Value,
            args.Contains("--btc-5m-history-dry-run", StringComparer.OrdinalIgnoreCase));
    }

    private static DateTimeOffset? ParseDateTimeOffsetOption(string[] args, string name)
    {
        var value = GetOptionValue(args, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new ArgumentException(name + " must be an ISO 8601 UTC timestamp.");
        }

        return parsed.ToUniversalTime();
    }

    private static int? ParseIntOption(string[] args, string name, int? defaultValue, int minValue, int maxValue)
    {
        var value = GetOptionValue(args, name);
        if (value is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < minValue ||
            parsed > maxValue)
        {
            throw new ArgumentException(
                name + " must be an integer between " +
                minValue.ToString(CultureInfo.InvariantCulture) +
                " and " +
                maxValue.ToString(CultureInfo.InvariantCulture) +
                ".");
        }

        return parsed;
    }

    private static string? GetOptionValue(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return index + 1 < args.Length ? args[index + 1] : string.Empty;
        }

        return null;
    }
}

public sealed record Btc5mHistoryFillSummary(
    int MarketsLoaded,
    int MarketsProcessed = 0,
    int MarketsSkippedUnknownResult = 0,
    int MarketsSkippedNoTrades = 0,
    int MarketsSkippedNoBtcStart = 0,
    int MarketsSkippedBinanceError = 0,
    int PointsInserted = 0,
    int PointsUpdated = 0,
    int PointsSkippedMissingPrice = 0)
{
    public string ToDisplayString()
    {
        return string.Join(
            Environment.NewLine,
            "Markets loaded: " + MarketsLoaded.ToString(CultureInfo.InvariantCulture),
            "Markets processed: " + MarketsProcessed.ToString(CultureInfo.InvariantCulture),
            "Skipped unknown result: " + MarketsSkippedUnknownResult.ToString(CultureInfo.InvariantCulture),
            "Skipped no trades: " + MarketsSkippedNoTrades.ToString(CultureInfo.InvariantCulture),
            "Skipped no BTC start: " + MarketsSkippedNoBtcStart.ToString(CultureInfo.InvariantCulture),
            "Skipped Binance error: " + MarketsSkippedBinanceError.ToString(CultureInfo.InvariantCulture),
            "Points inserted: " + PointsInserted.ToString(CultureInfo.InvariantCulture),
            "Points updated: " + PointsUpdated.ToString(CultureInfo.InvariantCulture),
            "Points skipped missing price: " + PointsSkippedMissingPrice.ToString(CultureInfo.InvariantCulture));
    }
}

public sealed record Btc5mHistoryMarket(
    string MarketId,
    string ConditionId,
    string Slug,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string? Result);

public sealed record BinanceAggTrade(
    long AggregateTradeId,
    decimal Price,
    DateTimeOffset TradeTimeUtc);

public readonly record struct Btc5mHistoryPointKey(int Seconds, int Cents);

public enum Btc5mHistoryCacheState
{
    Loaded,
    Updated,
    Inserted
}

public sealed class Btc5mHistoryCacheRow
{
    public Btc5mHistoryCacheRow(
        long? id,
        int seconds,
        int cents,
        int count,
        int upCount,
        int downCount,
        Btc5mHistoryCacheState state)
    {
        Id = id;
        Seconds = seconds;
        Cents = cents;
        Count = count;
        UpCount = upCount;
        DownCount = downCount;
        State = state;
    }

    public long? Id { get; }

    public int Seconds { get; }

    public int Cents { get; }

    public int Count { get; set; }

    public int UpCount { get; set; }

    public int DownCount { get; set; }

    public Btc5mHistoryCacheState State { get; set; }
}

public sealed record Btc5mHistoryMarketBuildResult(
    IReadOnlyCollection<Btc5mHistoryCacheRow> ChangedRows,
    int PointsInserted,
    int PointsUpdated,
    int PointsSkippedMissingPrice,
    Btc5mHistoryMarketSkipReason? SkipReason)
{
    public static Btc5mHistoryMarketBuildResult Skipped(Btc5mHistoryMarketSkipReason reason)
    {
        return new Btc5mHistoryMarketBuildResult([], 0, 0, 0, reason);
    }
}

public enum Btc5mHistoryMarketSkipReason
{
    UnknownResult,
    NoBtcStart,
    NoPriceForPoint
}
