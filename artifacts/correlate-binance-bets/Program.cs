using System.Globalization;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

var connectionFactory = new PostgresConnectionFactory(new StorageOptions());
await using var connection = connectionFactory.CreateConnection();
await connection.OpenAsync();

var binance = await LoadBinanceAsync(connection);
var observations = await LoadUpPriceObservationsAsync(connection);

Console.WriteLine("binance_samples=" + binance.Count);
if (binance.Count > 0)
{
    Console.WriteLine("binance_from_utc=" + FormatTime(binance[0].AtUtc));
    Console.WriteLine("binance_to_utc=" + FormatTime(binance[^1].AtUtc));
}

Console.WriteLine("up_price_observations=" + observations.Count);
Console.WriteLine("up_price_mid_observations=" + observations.Count(item => item.PriceKind == "mid"));
Console.WriteLine("up_price_single_side_observations=" + observations.Count(item => item.PriceKind != "mid"));
Console.WriteLine("markets_with_up_prices=" + observations.Select(item => item.MarketId).Distinct(StringComparer.Ordinal).Count());
if (observations.Count > 0)
{
    Console.WriteLine("up_prices_from_utc=" + FormatTime(observations[0].AtUtc));
    Console.WriteLine("up_prices_to_utc=" + FormatTime(observations[^1].AtUtc));
}

if (binance.Count < 3 || observations.Count < 3)
{
    Console.WriteLine("not_enough_data=true");
    return;
}

var pairs = BuildConsecutivePairs(observations, binance, TimeSpan.FromSeconds(30));
PrintDeltaStats("same_interval_all_price_proxies", pairs);
PrintDeltaStats("same_interval_mid_only", pairs.Where(pair => pair.Start.PriceKind == "mid" && pair.End.PriceKind == "mid").ToArray());

foreach (var leadSeconds in new[] { 10, 20, 30, 60, 120 })
{
    var leadPairs = BuildLeadPairs(observations, binance, TimeSpan.FromSeconds(leadSeconds), TimeSpan.FromSeconds(30));
    PrintLeadStats("btc_prior_" + leadSeconds + "s_predicts_next_up_price_change", leadPairs);
}

var startRelative = BuildStartRelativeSamples(observations, binance, TimeSpan.FromSeconds(30));
PrintLevelStats("market_start_relative", startRelative);

Console.WriteLine("recent_delta_pairs=");
foreach (var pair in pairs.TakeLast(12))
{
    Console.WriteLine(string.Join(
        " ",
        "pair",
        "market=" + pair.Start.MarketSlug,
        "from=" + FormatTime(pair.Start.AtUtc),
        "to=" + FormatTime(pair.End.AtUtc),
        "seconds=" + Format((pair.End.AtUtc - pair.Start.AtUtc).TotalSeconds),
        "btc_delta_usd=" + Format(pair.BtcDeltaUsd),
        "up_delta=" + Format(pair.UpDelta),
        "up_start=" + FormatDecimal(pair.Start.UpPrice),
        "up_end=" + FormatDecimal(pair.End.UpPrice),
        "price_kind=" + pair.Start.PriceKind + "->" + pair.End.PriceKind));
}

static async Task<List<BinancePoint>> LoadBinanceAsync(NpgsqlConnection connection)
{
    var result = new List<BinancePoint>();
    await using var command = new NpgsqlCommand(
        """
        SELECT binance_source_updated_at_utc, binance_price_usd
        FROM btc_usd_reference_correlation_samples
        ORDER BY binance_source_updated_at_utc ASC;
        """,
        connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        result.Add(new BinancePoint(
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetDecimal(1)));
    }

    return result
        .GroupBy(item => item.AtUtc)
        .Select(group => group.Last())
        .OrderBy(item => item.AtUtc)
        .ToList();
}

static async Task<List<UpPriceObservation>> LoadUpPriceObservationsAsync(NpgsqlConnection connection)
{
    var result = new List<UpPriceObservation>();
    await using var command = new NpgsqlCommand(
        """
        WITH btc_tokens AS (
            SELECT
                market.market_id,
                market.condition_id,
                market.slug,
                COALESCE(run.market_start_utc, market.start_date_utc, market.event_start_time_utc, market.end_date_utc - interval '5 minutes') AS market_start_utc,
                COALESCE(run.market_end_utc, market.end_date_utc, market.event_start_time_utc) AS market_end_utc,
                token.token_id,
                COALESCE(outcome.outcome, '') AS outcome
            FROM polymarket_gamma_markets market
            CROSS JOIN LATERAL jsonb_array_elements_text(market.clob_token_ids_json) WITH ORDINALITY AS token(token_id, token_ordinality)
            LEFT JOIN LATERAL jsonb_array_elements_text(market.outcomes_json) WITH ORDINALITY AS outcome(outcome, outcome_ordinality)
              ON outcome.outcome_ordinality = token.token_ordinality
            LEFT JOIN LATERAL (
                SELECT
                    MIN(r.market_start_utc) AS market_start_utc,
                    MIN(r.market_end_utc) AS market_end_utc
                FROM strategy_market_paper_runs r
                WHERE r.market_id = market.market_id
            ) run ON true
            WHERE lower(market.slug) ~ '^btc-updown-5m-[0-9]+$'
        )
        SELECT
            token.market_id,
            token.slug,
            token.condition_id,
            token.market_start_utc,
            token.market_end_utc,
            snapshot.snapshot_at_utc,
            snapshot.best_bid,
            snapshot.best_ask,
            CASE
                WHEN snapshot.best_bid IS NOT NULL AND snapshot.best_ask IS NOT NULL THEN (snapshot.best_bid + snapshot.best_ask) / 2
                WHEN snapshot.best_bid IS NOT NULL THEN snapshot.best_bid
                ELSE snapshot.best_ask
            END AS up_price,
            CASE
                WHEN snapshot.best_bid IS NOT NULL AND snapshot.best_ask IS NOT NULL THEN 'mid'
                WHEN snapshot.best_bid IS NOT NULL THEN 'bid_only'
                ELSE 'ask_only'
            END AS price_kind
        FROM order_book_snapshots snapshot
        INNER JOIN btc_tokens token ON token.token_id = snapshot.asset_id
        WHERE lower(token.outcome) = 'up'
          AND (snapshot.best_bid IS NOT NULL OR snapshot.best_ask IS NOT NULL)
        ORDER BY snapshot.snapshot_at_utc ASC, token.market_id ASC;
        """,
        connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        result.Add(new UpPriceObservation(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            GetNullableDateTime(reader, 3),
            GetNullableDateTime(reader, 4),
            reader.GetFieldValue<DateTimeOffset>(5),
            GetNullableDecimal(reader, 6),
            GetNullableDecimal(reader, 7),
            reader.GetDecimal(8),
            reader.GetString(9)));
    }

    return result;
}

static IReadOnlyList<DeltaPair> BuildConsecutivePairs(
    IReadOnlyList<UpPriceObservation> observations,
    IReadOnlyList<BinancePoint> binance,
    TimeSpan maxNearestAge)
{
    var result = new List<DeltaPair>();
    foreach (var group in observations.GroupBy(item => item.MarketId, StringComparer.Ordinal))
    {
        var ordered = group.OrderBy(item => item.AtUtc).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            var start = ordered[i - 1];
            var end = ordered[i];
            if (end.AtUtc <= start.AtUtc)
            {
                continue;
            }

            var btcStart = FindNearest(binance, start.AtUtc, maxNearestAge);
            var btcEnd = FindNearest(binance, end.AtUtc, maxNearestAge);
            if (btcStart is null || btcEnd is null)
            {
                continue;
            }

            result.Add(new DeltaPair(
                start,
                end,
                (double)(btcEnd.PriceUsd - btcStart.PriceUsd),
                (double)(end.UpPrice - start.UpPrice)));
        }
    }

    return result;
}

static IReadOnlyList<LeadPair> BuildLeadPairs(
    IReadOnlyList<UpPriceObservation> observations,
    IReadOnlyList<BinancePoint> binance,
    TimeSpan leadWindow,
    TimeSpan maxNearestAge)
{
    var result = new List<LeadPair>();
    foreach (var group in observations.GroupBy(item => item.MarketId, StringComparer.Ordinal))
    {
        var ordered = group.OrderBy(item => item.AtUtc).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            var start = ordered[i - 1];
            var end = ordered[i];
            if (end.AtUtc <= start.AtUtc)
            {
                continue;
            }

            var btcBefore = FindNearest(binance, start.AtUtc - leadWindow, maxNearestAge);
            var btcAtStart = FindNearest(binance, start.AtUtc, maxNearestAge);
            if (btcBefore is null || btcAtStart is null)
            {
                continue;
            }

            result.Add(new LeadPair(
                start,
                end,
                (double)(btcAtStart.PriceUsd - btcBefore.PriceUsd),
                (double)(end.UpPrice - start.UpPrice)));
        }
    }

    return result;
}

static IReadOnlyList<LevelSample> BuildStartRelativeSamples(
    IReadOnlyList<UpPriceObservation> observations,
    IReadOnlyList<BinancePoint> binance,
    TimeSpan maxNearestAge)
{
    var result = new List<LevelSample>();
    foreach (var group in observations.GroupBy(item => item.MarketId, StringComparer.Ordinal))
    {
        var ordered = group.OrderBy(item => item.AtUtc).ToArray();
        if (ordered.Length == 0)
        {
            continue;
        }

        var startTime = ordered[0].MarketStartUtc ?? ordered[0].AtUtc;
        var btcStart = FindNearest(binance, startTime, maxNearestAge);
        if (btcStart is null)
        {
            continue;
        }

        foreach (var item in ordered)
        {
            var btcAtObservation = FindNearest(binance, item.AtUtc, maxNearestAge);
            if (btcAtObservation is null)
            {
                continue;
            }

            result.Add(new LevelSample(
                item,
                (double)(btcAtObservation.PriceUsd - btcStart.PriceUsd),
                (double)item.UpPrice));
        }
    }

    return result;
}

static void PrintDeltaStats(string label, IReadOnlyList<DeltaPair> pairs)
{
    var btc = pairs.Select(pair => pair.BtcDeltaUsd).ToArray();
    var up = pairs.Select(pair => pair.UpDelta).ToArray();
    Console.WriteLine(string.Join(
        " ",
        "delta_stats",
        "label=" + label,
        "pairs=" + pairs.Count,
        "corr=" + Format(Pearson(btc, up)),
        "direction_accuracy=" + Format(DirectionAccuracy(btc, up)),
        "avg_abs_btc_delta_usd=" + Format(MeanAbs(btc)),
        "avg_abs_up_delta=" + Format(MeanAbs(up)),
        "btc_up_when_up_price_up=" + CountSamePositive(btc, up),
        "btc_down_when_up_price_down=" + CountSameNegative(btc, up)));
}

static void PrintLeadStats(string label, IReadOnlyList<LeadPair> pairs)
{
    var btc = pairs.Select(pair => pair.PriorBtcDeltaUsd).ToArray();
    var up = pairs.Select(pair => pair.FutureUpDelta).ToArray();
    Console.WriteLine(string.Join(
        " ",
        "lead_stats",
        "label=" + label,
        "pairs=" + pairs.Count,
        "corr=" + Format(Pearson(btc, up)),
        "direction_accuracy=" + Format(DirectionAccuracy(btc, up)),
        "avg_abs_prior_btc_delta_usd=" + Format(MeanAbs(btc)),
        "avg_abs_future_up_delta=" + Format(MeanAbs(up))));
}

static void PrintLevelStats(string label, IReadOnlyList<LevelSample> samples)
{
    var btc = samples.Select(sample => sample.BtcMoveSinceMarketStartUsd).ToArray();
    var up = samples.Select(sample => sample.UpPrice).ToArray();
    Console.WriteLine(string.Join(
        " ",
        "level_stats",
        "label=" + label,
        "samples=" + samples.Count,
        "corr_btc_move_vs_up_price=" + Format(Pearson(btc, up)),
        "direction_accuracy_vs_0_5=" + Format(DirectionAccuracy(
            btc,
            up.Select(value => value - 0.5d).ToArray())),
        "avg_abs_btc_move_usd=" + Format(MeanAbs(btc)),
        "avg_abs_up_minus_half=" + Format(MeanAbs(up.Select(value => value - 0.5d).ToArray()))));
}

static BinancePoint? FindNearest(IReadOnlyList<BinancePoint> points, DateTimeOffset atUtc, TimeSpan maxAge)
{
    if (points.Count == 0)
    {
        return null;
    }

    var left = 0;
    var right = points.Count - 1;
    while (left <= right)
    {
        var middle = left + (right - left) / 2;
        if (points[middle].AtUtc < atUtc)
        {
            left = middle + 1;
        }
        else
        {
            right = middle - 1;
        }
    }

    BinancePoint? best = null;
    var bestAge = TimeSpan.MaxValue;
    foreach (var index in new[] { right, left })
    {
        if (index < 0 || index >= points.Count)
        {
            continue;
        }

        var age = (points[index].AtUtc - atUtc).Duration();
        if (age < bestAge)
        {
            best = points[index];
            bestAge = age;
        }
    }

    return bestAge <= maxAge ? best : null;
}

static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
{
    if (x.Count != y.Count || x.Count < 2)
    {
        return double.NaN;
    }

    var meanX = x.Average();
    var meanY = y.Average();
    var sumXY = 0d;
    var sumXX = 0d;
    var sumYY = 0d;
    for (var i = 0; i < x.Count; i++)
    {
        var dx = x[i] - meanX;
        var dy = y[i] - meanY;
        sumXY += dx * dy;
        sumXX += dx * dx;
        sumYY += dy * dy;
    }

    return sumXX == 0d || sumYY == 0d ? double.NaN : sumXY / Math.Sqrt(sumXX * sumYY);
}

static double DirectionAccuracy(IReadOnlyList<double> x, IReadOnlyList<double> y)
{
    var total = 0;
    var correct = 0;
    for (var i = 0; i < x.Count && i < y.Count; i++)
    {
        var sx = Math.Sign(x[i]);
        var sy = Math.Sign(y[i]);
        if (sx == 0 || sy == 0)
        {
            continue;
        }

        total++;
        if (sx == sy)
        {
            correct++;
        }
    }

    return total == 0 ? double.NaN : (double)correct / total;
}

static int CountSamePositive(IReadOnlyList<double> x, IReadOnlyList<double> y)
{
    var count = 0;
    for (var i = 0; i < x.Count && i < y.Count; i++)
    {
        if (x[i] > 0 && y[i] > 0)
        {
            count++;
        }
    }

    return count;
}

static int CountSameNegative(IReadOnlyList<double> x, IReadOnlyList<double> y)
{
    var count = 0;
    for (var i = 0; i < x.Count && i < y.Count; i++)
    {
        if (x[i] < 0 && y[i] < 0)
        {
            count++;
        }
    }

    return count;
}

static double MeanAbs(IReadOnlyList<double> values)
{
    return values.Count == 0 ? double.NaN : values.Select(Math.Abs).Average();
}

static decimal? GetNullableDecimal(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}

static DateTimeOffset? GetNullableDateTime(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
}

static string Format(double value)
{
    return double.IsNaN(value) ? "n/a" : value.ToString("0.########", CultureInfo.InvariantCulture);
}

static string FormatDecimal(decimal value)
{
    return value.ToString("0.########", CultureInfo.InvariantCulture);
}

static string FormatTime(DateTimeOffset value)
{
    return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}

internal sealed record BinancePoint(DateTimeOffset AtUtc, decimal PriceUsd);

internal sealed record UpPriceObservation(
    string MarketId,
    string MarketSlug,
    string ConditionId,
    DateTimeOffset? MarketStartUtc,
    DateTimeOffset? MarketEndUtc,
    DateTimeOffset AtUtc,
    decimal? BestBid,
    decimal? BestAsk,
    decimal UpPrice,
    string PriceKind);

internal sealed record DeltaPair(
    UpPriceObservation Start,
    UpPriceObservation End,
    double BtcDeltaUsd,
    double UpDelta);

internal sealed record LeadPair(
    UpPriceObservation Start,
    UpPriceObservation End,
    double PriorBtcDeltaUsd,
    double FutureUpDelta);

internal sealed record LevelSample(
    UpPriceObservation Observation,
    double BtcMoveSinceMarketStartUsd,
    double UpPrice);
