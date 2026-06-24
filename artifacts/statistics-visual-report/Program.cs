using System.Globalization;
using System.Net;
using System.Text;
using Npgsql;

const decimal MinEffectiveCount = 20m;
const int MinChartSamples = 8;
const int MaxCharts = 12;

var outputPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "statistics-visual-report-filtered.html"));
var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 2;
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    Host = Environment.GetEnvironmentVariable("POLYCOPYTRADER_REPORT_POSTGRES_HOST") ?? "192.168.0.101",
    IncludeErrorDetail = true
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();

var markets = await LoadMarketsAsync(connection);
if (markets.Count == 0)
{
    Console.Error.WriteLine("No BTC Statistics markets with enough high-support samples were found.");
    return 1;
}

var charts = new List<MarketChart>();
foreach (var market in markets)
{
    var points = await LoadPointsAsync(connection, market.MarketId);
    if (points.Count >= MinChartSamples)
    {
        charts.Add(new MarketChart(market, points));
    }
}

var html = RenderHtml(charts);
await File.WriteAllTextAsync(outputPath, html, Encoding.UTF8);
Console.WriteLine(outputPath);
return 0;

static async Task<IReadOnlyList<MarketSummary>> LoadMarketsAsync(NpgsqlConnection connection)
{
    const string sql = """
SELECT market_id,
       max(market_slug) AS market_slug,
       min(market_start_utc) AS market_start_utc,
       max(market_end_utc) AS market_end_utc,
       count(*)::int AS filtered_samples,
       min(sampled_at_utc) AS first_sample_utc,
       max(sampled_at_utc) AS last_sample_utc
FROM btc_up_down_5m_statistics_ticks
WHERE sampled_at_utc >= now() - interval '12 hours'
  AND up_probability IS NOT NULL
  AND effective_count >= @MinEffectiveCount
GROUP BY market_id
HAVING count(*) >= @MinChartSamples
ORDER BY max(sampled_at_utc) DESC
LIMIT @MaxCharts;
""";

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("MinEffectiveCount", MinEffectiveCount);
    command.Parameters.AddWithValue("MinChartSamples", MinChartSamples);
    command.Parameters.AddWithValue("MaxCharts", MaxCharts);
    await using var reader = await command.ExecuteReaderAsync();
    var result = new List<MarketSummary>();
    while (await reader.ReadAsync())
    {
        result.Add(new MarketSummary(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetInt32(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6)));
    }

    return result;
}

static async Task<IReadOnlyList<ChartPoint>> LoadPointsAsync(NpgsqlConnection connection, string marketId)
{
    const string sql = """
SELECT t.sampled_at_utc,
       t.seconds_after_start,
       t.binance_price_usd,
       t.binance_start_price_usd,
       t.up_probability,
       COALESCE(o.up_mid, CASE WHEN o.up_best_bid IS NOT NULL AND o.up_best_ask IS NOT NULL THEN (o.up_best_bid + o.up_best_ask) / 2 END, o.up_price_proxy) AS polymarket_up_mid,
       t.up_market_price AS strategy_up_price,
       t.down_probability,
       COALESCE(o.down_mid, CASE WHEN o.down_best_bid IS NOT NULL AND o.down_best_ask IS NOT NULL THEN (o.down_best_bid + o.down_best_ask) / 2 END, o.down_price_proxy) AS polymarket_down_mid,
       t.decision_code,
       t.recommended_outcome,
       t.would_bet,
       t.effective_count,
       o.sampled_at_utc AS odds_sampled_at_utc,
       abs(extract(epoch FROM (o.sampled_at_utc - t.sampled_at_utc))) AS odds_lag_seconds
FROM btc_up_down_5m_statistics_ticks t
LEFT JOIN LATERAL (
    SELECT sampled_at_utc,
           up_mid,
           up_best_bid,
           up_best_ask,
           up_price_proxy,
           down_mid,
           down_best_bid,
           down_best_ask,
           down_price_proxy
    FROM btc_up_down_5m_odds_ticks o
    WHERE o.market_id = t.market_id
    ORDER BY abs(extract(epoch FROM (o.sampled_at_utc - t.sampled_at_utc))) ASC
    LIMIT 1
) o ON TRUE
WHERE t.market_id = @MarketId
  AND t.up_probability IS NOT NULL
  AND t.effective_count >= @MinEffectiveCount
ORDER BY t.sampled_at_utc ASC;
""";

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("MarketId", marketId);
    command.Parameters.AddWithValue("MinEffectiveCount", MinEffectiveCount);
    await using var reader = await command.ExecuteReaderAsync();
    var result = new List<ChartPoint>();
    while (await reader.ReadAsync())
    {
        result.Add(new ChartPoint(
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetDecimal(1),
            reader.GetDecimal(2),
            reader.IsDBNull(3) ? null : reader.GetDecimal(3),
            reader.IsDBNull(4) ? null : reader.GetDecimal(4),
            reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : reader.GetDecimal(6),
            reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetBoolean(11),
            reader.IsDBNull(12) ? null : reader.GetDecimal(12),
            reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
            reader.IsDBNull(14) ? null : reader.GetDecimal(14)));
    }

    return result;
}

static string RenderHtml(IReadOnlyList<MarketChart> charts)
{
    var generatedAt = DateTimeOffset.UtcNow;
    var allPoints = charts.SelectMany(chart => chart.Points).ToArray();
    var overallStats = CalculateStats(allPoints, p => p.UpProbability, p => p.PolymarketUpMid, p => p.StrategyUpPrice, _ => null);

    var sb = new StringBuilder();
    sb.AppendLine("<!doctype html>");
    sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><title>BTC Statistics High-Support Visual Report</title>");
    sb.AppendLine("""
<style>
body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#f7f8fb;color:#172033}
h1{font-size:24px;margin:0 0 6px} .sub{color:#5b6475;margin-bottom:18px}
.card{background:#fff;border:1px solid #d9dde7;border-radius:8px;margin:16px 0;padding:16px;box-shadow:0 1px 2px rgba(20,30,55,.05)}
.meta{display:flex;gap:18px;flex-wrap:wrap;color:#4c5668;font-size:13px;margin:6px 0 12px}
.legend{display:flex;gap:16px;align-items:center;font-size:13px;margin:8px 0 12px}.sw{display:inline-block;width:20px;height:3px;vertical-align:middle;margin-right:6px}
.model{background:#1b7f5f}.market{background:#c2410c}.ask{background:#9ca3af}.btc{background:#2563eb}.grid{stroke:#e7eaf0;stroke-width:1}.axis{stroke:#a9b0bf;stroke-width:1}
.line{fill:none;stroke-width:2.2}.askline{fill:none;stroke:#9ca3af;stroke-width:1.4;stroke-dasharray:5 5}.dot{stroke:#fff;stroke-width:1}
table{border-collapse:collapse;width:100%;font-size:13px}td,th{border-bottom:1px solid #e5e8ef;padding:6px;text-align:left}th{background:#fafbfe}
.note{font-size:13px;color:#4c5668}.small{font-size:12px;color:#687386}
</style>
""");
    sb.AppendLine("</head><body>");
    sb.AppendLine("<h1>BTC Up or Down 5m Statistics: high-support model vs Polymarket mid vs BTC</h1>");
    sb.AppendLine($"<div class=\"sub\">Generated {Html(generatedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture))}. Only points with effective_count >= {MinEffectiveCount.ToString(CultureInfo.InvariantCulture)} are plotted. BTC is min-max scaled inside each 5m market window.</div>");
    sb.AppendLine("<div class=\"card\"><div class=\"meta\">");
    sb.AppendLine($"<span>charts: {charts.Count.ToString(CultureInfo.InvariantCulture)}</span>");
    sb.AppendLine($"<span>filtered points: {allPoints.Length.ToString(CultureInfo.InvariantCulture)}</span>");
    sb.AppendLine($"<span>overall model-mid MAE: {Percent(overallStats.ModelMidMae)}</span>");
    sb.AppendLine($"<span>overall model-ask MAE: {Percent(overallStats.ModelAskMae)}</span>");
    sb.AppendLine("</div><div class=\"note\">Polymarket mid is taken from the nearest odds-archive tick for the same market. The gray dashed line is the Statistics strategy price, usually ask, kept for comparison.</div></div>");

    sb.AppendLine("<div class=\"card\"><table><thead><tr><th>Market</th><th>UTC window</th><th>Filtered samples</th><th>Avg effective</th><th>Model-mid MAE</th><th>Model-ask MAE</th><th>Corr model/mid</th><th>Corr model/BTC scaled</th></tr></thead><tbody>");
    foreach (var chart in charts)
    {
        var stats = CalculateStats(chart.Points, p => p.UpProbability, p => p.PolymarketUpMid, p => p.StrategyUpPrice, chart.BtcScaled);
        sb.Append("<tr>");
        sb.Append($"<td>{Html(chart.Market.MarketSlug)}</td>");
        sb.Append($"<td>{Html(chart.Market.MarketStartUtc.ToString("HH:mm", CultureInfo.InvariantCulture))}-{Html(chart.Market.MarketEndUtc.ToString("HH:mm", CultureInfo.InvariantCulture))}</td>");
        sb.Append($"<td>{chart.Points.Count.ToString(CultureInfo.InvariantCulture)}</td>");
        sb.Append($"<td>{Number(chart.Points.Average(point => point.EffectiveCount ?? 0m))}</td>");
        sb.Append($"<td>{Percent(stats.ModelMidMae)}</td>");
        sb.Append($"<td>{Percent(stats.ModelAskMae)}</td>");
        sb.Append($"<td>{Number(stats.CorrModelMid)}</td>");
        sb.Append($"<td>{Number(stats.CorrModelBtc)}</td>");
        sb.AppendLine("</tr>");
    }
    sb.AppendLine("</tbody></table></div>");

    foreach (var chart in charts)
    {
        RenderChart(sb, chart);
    }

    sb.AppendLine("</body></html>");
    return sb.ToString();
}

static void RenderChart(StringBuilder sb, MarketChart chart)
{
    const int width = 1080;
    const int height = 360;
    const int left = 58;
    const int right = 22;
    const int top = 24;
    const int bottom = 42;
    var plotWidth = width - left - right;
    var plotHeight = height - top - bottom;
    var xMax = Math.Max(300m, chart.Points.Max(point => point.SecondsAfterStart));
    var modelPath = BuildPath(chart.Points, point => point.UpProbability, point => X(point.SecondsAfterStart), Y);
    var marketMidPath = BuildPath(chart.Points, point => point.PolymarketUpMid, point => X(point.SecondsAfterStart), Y);
    var strategyAskPath = BuildPath(chart.Points, point => point.StrategyUpPrice, point => X(point.SecondsAfterStart), Y);
    var btcPath = BuildPath(chart.Points, point => chart.BtcScaled(point), point => X(point.SecondsAfterStart), Y);
    var stats = CalculateStats(chart.Points, p => p.UpProbability, p => p.PolymarketUpMid, p => p.StrategyUpPrice, chart.BtcScaled);
    var wouldBetPoints = chart.Points.Where(point => point.WouldBet).Take(80).ToArray();
    var medianLag = Median(chart.Points.Select(point => point.OddsLagSeconds).Where(value => value is not null).Select(value => value!.Value).ToArray());

    sb.AppendLine("<div class=\"card\">");
    sb.AppendLine($"<h2 style=\"font-size:18px;margin:0 0 4px\">{Html(chart.Market.MarketSlug)}</h2>");
    sb.AppendLine("<div class=\"meta\">");
    sb.AppendLine($"<span>{Html(chart.Market.MarketStartUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}-{Html(chart.Market.MarketEndUtc.ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture))}</span>");
    sb.AppendLine($"<span>BTC range: ${Money(chart.MinBtc)}-${Money(chart.MaxBtc)}</span>");
    sb.AppendLine($"<span>filtered samples: {chart.Points.Count.ToString(CultureInfo.InvariantCulture)}</span>");
    sb.AppendLine($"<span>avg effective: {Number(chart.Points.Average(point => point.EffectiveCount ?? 0m))}</span>");
    sb.AppendLine($"<span>model-mid MAE: {Percent(stats.ModelMidMae)}</span>");
    sb.AppendLine($"<span>odds lag median: {Number(medianLag)}s</span>");
    sb.AppendLine("</div>");
    sb.AppendLine("<div class=\"legend\"><span><span class=\"sw model\"></span>Statistics Up probability</span><span><span class=\"sw market\"></span>Polymarket Up mid</span><span><span class=\"sw ask\"></span>Strategy price, usually ask</span><span><span class=\"sw btc\"></span>BTC price scaled to 0..1</span></div>");
    sb.AppendLine($"<svg viewBox=\"0 0 {width} {height}\" width=\"100%\" height=\"{height}\" role=\"img\">");
    for (var i = 0; i <= 4; i++)
    {
        var y = top + i * plotHeight / 4.0;
        var label = (1.0 - i * 0.25).ToString("P0", CultureInfo.InvariantCulture);
        sb.AppendLine($"<line class=\"grid\" x1=\"{left}\" y1=\"{Fmt(y)}\" x2=\"{width - right}\" y2=\"{Fmt(y)}\"/>");
        sb.AppendLine($"<text x=\"10\" y=\"{Fmt(y + 4)}\" font-size=\"12\" fill=\"#687386\">{label}</text>");
    }
    for (var s = 0; s <= 300; s += 60)
    {
        var x = X(s);
        sb.AppendLine($"<line class=\"grid\" x1=\"{Fmt(x)}\" y1=\"{top}\" x2=\"{Fmt(x)}\" y2=\"{height - bottom}\"/>");
        sb.AppendLine($"<text x=\"{Fmt(x - 10)}\" y=\"{height - 14}\" font-size=\"12\" fill=\"#687386\">{s}s</text>");
    }
    sb.AppendLine($"<line class=\"axis\" x1=\"{left}\" y1=\"{height - bottom}\" x2=\"{width - right}\" y2=\"{height - bottom}\"/>");
    sb.AppendLine($"<line class=\"axis\" x1=\"{left}\" y1=\"{top}\" x2=\"{left}\" y2=\"{height - bottom}\"/>");
    sb.AppendLine($"<path class=\"line\" stroke=\"#2563eb\" d=\"{btcPath}\"/>");
    sb.AppendLine($"<path class=\"askline\" d=\"{strategyAskPath}\"/>");
    sb.AppendLine($"<path class=\"line\" stroke=\"#c2410c\" d=\"{marketMidPath}\"/>");
    sb.AppendLine($"<path class=\"line\" stroke=\"#1b7f5f\" d=\"{modelPath}\"/>");
    foreach (var point in wouldBetPoints)
    {
        var yValue = point.RecommendedOutcome == "Down" ? point.DownProbability : point.UpProbability;
        if (yValue is null)
        {
            continue;
        }

        sb.AppendLine($"<circle class=\"dot\" cx=\"{Fmt(X(point.SecondsAfterStart))}\" cy=\"{Fmt(Y(yValue.Value))}\" r=\"3.2\" fill=\"#111827\"><title>{Html(point.SampledAtUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture))} {Html(point.DecisionCode)} effective={Number(point.EffectiveCount)}</title></circle>");
    }
    sb.AppendLine("</svg>");
    sb.AppendLine($"<div class=\"note\">Correlations: model/mid {Number(stats.CorrModelMid)}, model/BTC-scaled {Number(stats.CorrModelBtc)}, mid/BTC-scaled {Number(stats.CorrMidBtc)}. Black dots are would-bet decisions.</div>");
    sb.AppendLine("</div>");

    double X(decimal seconds)
    {
        var clamped = Math.Max(0, Math.Min((double)xMax, (double)seconds));
        return left + clamped / (double)xMax * plotWidth;
    }

    double Y(decimal value)
    {
        var clamped = Math.Max(0, Math.Min(1, (double)value));
        return top + (1.0 - clamped) * plotHeight;
    }
}

static string BuildPath(
    IReadOnlyList<ChartPoint> points,
    Func<ChartPoint, decimal?> valueSelector,
    Func<ChartPoint, double> xSelector,
    Func<decimal, double> ySelector)
{
    var sb = new StringBuilder();
    var started = false;
    foreach (var point in points)
    {
        var value = valueSelector(point);
        if (value is null)
        {
            started = false;
            continue;
        }

        sb.Append(started ? " L " : "M ");
        sb.Append(Fmt(xSelector(point)));
        sb.Append(' ');
        sb.Append(Fmt(ySelector(value.Value)));
        started = true;
    }

    return sb.ToString();
}

static ChartStats CalculateStats(
    IReadOnlyList<ChartPoint> points,
    Func<ChartPoint, decimal?> modelSelector,
    Func<ChartPoint, decimal?> marketMidSelector,
    Func<ChartPoint, decimal?> strategyAskSelector,
    Func<ChartPoint, decimal?> btcSelector)
{
    var modelMid = points
        .Select(point => Pair(modelSelector(point), marketMidSelector(point)))
        .Where(pair => pair is not null)
        .Select(pair => pair!.Value)
        .ToArray();
    var modelAsk = points
        .Select(point => Pair(modelSelector(point), strategyAskSelector(point)))
        .Where(pair => pair is not null)
        .Select(pair => pair!.Value)
        .ToArray();
    var modelBtc = points
        .Select(point => Pair(modelSelector(point), btcSelector(point)))
        .Where(pair => pair is not null)
        .Select(pair => pair!.Value)
        .ToArray();
    var midBtc = points
        .Select(point => Pair(marketMidSelector(point), btcSelector(point)))
        .Where(pair => pair is not null)
        .Select(pair => pair!.Value)
        .ToArray();
    return new ChartStats(
        Mae(modelMid),
        Mae(modelAsk),
        Correlation(modelMid),
        Correlation(modelBtc),
        Correlation(midBtc));
}

static decimal? Mae(IReadOnlyList<PairValue> pairs)
{
    return pairs.Count == 0 ? null : pairs.Average(pair => Math.Abs(pair.Left - pair.Right));
}

static PairValue? Pair(decimal? left, decimal? right)
{
    return left is { } l && right is { } r ? new PairValue(l, r) : null;
}

static decimal? Correlation(IReadOnlyList<PairValue> pairs)
{
    if (pairs.Count < 2)
    {
        return null;
    }

    var leftAverage = pairs.Average(pair => pair.Left);
    var rightAverage = pairs.Average(pair => pair.Right);
    var numerator = pairs.Sum(pair => (pair.Left - leftAverage) * (pair.Right - rightAverage));
    var leftVariance = pairs.Sum(pair => (pair.Left - leftAverage) * (pair.Left - leftAverage));
    var rightVariance = pairs.Sum(pair => (pair.Right - rightAverage) * (pair.Right - rightAverage));
    if (leftVariance <= 0m || rightVariance <= 0m)
    {
        return null;
    }

    return numerator / (decimal)Math.Sqrt((double)(leftVariance * rightVariance));
}

static decimal? Median(IReadOnlyList<decimal> values)
{
    if (values.Count == 0)
    {
        return null;
    }

    var sorted = values.Order().ToArray();
    var mid = sorted.Length / 2;
    return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
}

static string Html(string value)
{
    return WebUtility.HtmlEncode(value);
}

static string Fmt(double value)
{
    return value.ToString("0.###", CultureInfo.InvariantCulture);
}

static string Percent(decimal? value)
{
    return value is { } actual ? actual.ToString("P1", CultureInfo.InvariantCulture) : "n/a";
}

static string Number(decimal? value)
{
    return value is { } actual ? actual.ToString("0.###", CultureInfo.InvariantCulture) : "n/a";
}

static string Money(decimal value)
{
    return value.ToString("0.00", CultureInfo.InvariantCulture);
}

sealed record MarketSummary(
    string MarketId,
    string MarketSlug,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset MarketEndUtc,
    int FilteredSamples,
    DateTimeOffset FirstSampleUtc,
    DateTimeOffset LastSampleUtc);

sealed record ChartPoint(
    DateTimeOffset SampledAtUtc,
    decimal SecondsAfterStart,
    decimal BinancePriceUsd,
    decimal? BinanceStartPriceUsd,
    decimal? UpProbability,
    decimal? PolymarketUpMid,
    decimal? StrategyUpPrice,
    decimal? DownProbability,
    decimal? PolymarketDownMid,
    string DecisionCode,
    string? RecommendedOutcome,
    bool WouldBet,
    decimal? EffectiveCount,
    DateTimeOffset? OddsSampledAtUtc,
    decimal? OddsLagSeconds);

sealed record MarketChart(MarketSummary Market, IReadOnlyList<ChartPoint> Points)
{
    public decimal MinBtc { get; } = Points.Min(point => point.BinancePriceUsd);
    public decimal MaxBtc { get; } = Points.Max(point => point.BinancePriceUsd);

    public decimal? BtcScaled(ChartPoint point)
    {
        return MaxBtc == MinBtc ? 0.5m : (point.BinancePriceUsd - MinBtc) / (MaxBtc - MinBtc);
    }
}

readonly record struct PairValue(decimal Left, decimal Right);

sealed record ChartStats(
    decimal? ModelMidMae,
    decimal? ModelAskMae,
    decimal? CorrModelMid,
    decimal? CorrModelBtc,
    decimal? CorrMidBtc);
