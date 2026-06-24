using System.Globalization;
using System.IO.Compression;
using System.Text;

const int MarketSeconds = 300;
const int SampleIntervalSeconds = 60;
const int MaxDepth = 100;
const double AssumedEntryPrice = 0.50;

var artifactRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "middle-premarket-backtest-2026-06-21"));
var dataDir = Path.Combine(artifactRoot, "data");
Directory.CreateDirectory(dataDir);

var backtestStartUtc = new DateTimeOffset(2025, 12, 20, 0, 0, 0, TimeSpan.Zero);
var backtestEndUtc = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);
var warmup = TimeSpan.FromSeconds((MaxDepth + 2) * SampleIntervalSeconds + MarketSeconds);
var dataStartUtc = backtestStartUtc.Subtract(warmup);
var dataStartSecond = dataStartUtc.ToUnixTimeSeconds();
var backtestStartSecond = backtestStartUtc.ToUnixTimeSeconds();
var backtestEndSecond = backtestEndUtc.ToUnixTimeSeconds();
var days = (backtestEndUtc - backtestStartUtc).TotalDays;

var offsets = new[] { 30, 25, 20, 15, 10, 5 };
var deployRelevantOffsets = new HashSet<int>([30, 10, 5]);
var depths = new[] { 100, 90, 80, 70, 60, 50, 40, 30, 20, 10 };
var thresholds = new[] { 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100 };
var assets = new[] { "BTC", "ETH", "SOL" };
var allStats = new List<StrategyStats>();
var baselines = new List<BaselineStats>();
var allFiles = new List<SourceFile>();

using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromMinutes(20)
};

foreach (var asset in assets)
{
    var symbol = asset + "USDT";
    Console.WriteLine($"Preparing {symbol}...");
    var sourceFiles = BuildSourceFiles(symbol, dataStartUtc, backtestEndUtc);
    allFiles.AddRange(sourceFiles);
    foreach (var source in sourceFiles)
    {
        await EnsureDownloadedAsync(httpClient, source);
    }

    var priceBySecond = new double[checked((int)(backtestEndSecond - dataStartSecond))];
    Array.Fill(priceBySecond, double.NaN);

    Console.WriteLine($"Parsing {symbol} archives...");
    foreach (var source in sourceFiles)
    {
        var rows = LoadZipPrices(Path.Combine(dataDir, source.LocalPath), dataStartSecond, backtestEndSecond, priceBySecond);
        Console.WriteLine($"{asset} {Path.GetFileName(source.LocalPath)} rows loaded: {rows.ToString(CultureInfo.InvariantCulture)}");
    }

    var filledSeconds = ForwardFill(priceBySecond);
    Console.WriteLine($"{asset} usable second prices after forward-fill: {filledSeconds.ToString(CultureInfo.InvariantCulture)}");

    var stats = offsets
        .SelectMany(offset => depths.SelectMany(depth => thresholds.Select(threshold => new StrategyStats(asset, offset, depth, threshold))))
        .ToDictionary(item => (item.Asset, item.OffsetSeconds, item.Depth, item.ThresholdBps));
    var baseline = new BaselineStats(asset);

    var firstStart = AlignUp(backtestStartSecond, MarketSeconds);
    var marketCount = 0;
    var missingPriceSkips = 0;
    for (var currentStart = firstStart; currentStart + MarketSeconds <= backtestEndSecond; currentStart += MarketSeconds)
    {
        var currentEnd = currentStart + MarketSeconds;
        if (!TryGetPriceBefore(priceBySecond, currentStart, out var currentStartPrice) ||
            !TryGetPriceBefore(priceBySecond, currentEnd, out var currentEndPrice))
        {
            missingPriceSkips++;
            continue;
        }

        var currentMoveBps = MoveBps(currentStartPrice, currentEndPrice);
        var outcome = currentMoveBps < 0.0 ? Outcome.Down : currentMoveBps > 0.0 ? Outcome.Up : Outcome.Tie;
        baseline.Add(outcome, MonthKey(currentStart), currentMoveBps);
        marketCount++;

        foreach (var offset in offsets)
        {
            var decisionSecond = currentStart - offset;
            if (!TryGetPriceBefore(priceBySecond, decisionSecond, out var decisionPrice))
            {
                missingPriceSkips++;
                continue;
            }

            foreach (var depth in depths)
            {
                if (!TryGetSampleMean(priceBySecond, decisionSecond, depth, out var meanPrice))
                {
                    missingPriceSkips++;
                    continue;
                }

                var deviationBps = MoveBps(meanPrice, decisionPrice);
                if (Math.Abs(deviationBps) < 1e-12)
                {
                    continue;
                }

                var selectedOutcome = deviationBps > 0.0 ? Outcome.Down : Outcome.Up;
                var absDeviationBps = Math.Abs(deviationBps);
                foreach (var threshold in thresholds)
                {
                    if (absDeviationBps + 1e-12 < threshold)
                    {
                        continue;
                    }

                    stats[(asset, offset, depth, threshold)].Add(
                        selectedOutcome,
                        outcome,
                        MonthKey(currentStart),
                        deviationBps,
                        currentMoveBps);
                }
            }
        }
    }

    baseline.MarketsEvaluated = marketCount;
    baseline.MissingPriceSkips = missingPriceSkips;
    Console.WriteLine($"{asset} markets evaluated: {marketCount.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine($"{asset} missing-price skips: {missingPriceSkips.ToString(CultureInfo.InvariantCulture)}");
    allStats.AddRange(stats.Values);
    baselines.Add(baseline);
}

var ordered = allStats
    .OrderByDescending(item => item.SettledTrades >= 500 ? item.RoiPctAtHalf : double.NegativeInfinity)
    .ThenByDescending(item => item.SettledTrades)
    .ToArray();

WriteStrategyResults(Path.Combine(artifactRoot, "strategy_results.csv"), allStats.OrderBy(item => item.Asset, StringComparer.Ordinal).ThenBy(item => item.OffsetSeconds).ThenByDescending(item => item.Depth).ThenBy(item => item.ThresholdBps), days);
WriteTopResults(Path.Combine(artifactRoot, "top_by_roi.csv"), ordered.Where(item => item.SettledTrades >= 500).Take(200), days);
WriteDeployOffsetTopResults(Path.Combine(artifactRoot, "top_deploy_offsets.csv"), allStats.Where(item => deployRelevantOffsets.Contains(item.OffsetSeconds)).OrderByDescending(item => item.SettledTrades >= 500 ? item.RoiPctAtHalf : double.NegativeInfinity).ThenByDescending(item => item.SettledTrades).Take(200), days);
WriteMonthlyResults(Path.Combine(artifactRoot, "monthly_summary.csv"), allStats.OrderBy(item => item.Asset, StringComparer.Ordinal).ThenBy(item => item.OffsetSeconds).ThenByDescending(item => item.Depth).ThenBy(item => item.ThresholdBps));
WriteAssetSummary(Path.Combine(artifactRoot, "asset_summary.csv"), baselines, allStats, days);
WriteMarkdownReport(Path.Combine(artifactRoot, "summary.md"), allStats, ordered, baselines, backtestStartUtc, backtestEndUtc, days, allFiles);

Console.WriteLine("Wrote:");
Console.WriteLine(Path.Combine(artifactRoot, "strategy_results.csv"));
Console.WriteLine(Path.Combine(artifactRoot, "top_by_roi.csv"));
Console.WriteLine(Path.Combine(artifactRoot, "top_deploy_offsets.csv"));
Console.WriteLine(Path.Combine(artifactRoot, "monthly_summary.csv"));
Console.WriteLine(Path.Combine(artifactRoot, "asset_summary.csv"));
Console.WriteLine(Path.Combine(artifactRoot, "summary.md"));

bool TryGetPriceBefore(double[] priceBySecond, long timestampSecond, out double price)
{
    var index = timestampSecond - 1 - dataStartSecond;
    if (index < 0 || index >= priceBySecond.Length)
    {
        price = 0.0;
        return false;
    }

    price = priceBySecond[index];
    return price > 0.0 && !double.IsNaN(price);
}

bool TryGetSampleMean(double[] priceBySecond, long decisionSecond, int depth, out double meanPrice)
{
    meanPrice = 0.0;
    var latestSampleSecond = AlignDown(decisionSecond, SampleIntervalSeconds);
    for (var i = 0; i < depth; i++)
    {
        if (!TryGetPriceBefore(priceBySecond, latestSampleSecond - (i * SampleIntervalSeconds), out var samplePrice))
        {
            meanPrice = 0.0;
            return false;
        }

        meanPrice += samplePrice;
    }

    meanPrice /= depth;
    return meanPrice > 0.0;
}

static IReadOnlyList<SourceFile> BuildSourceFiles(string symbol, DateTimeOffset startUtc, DateTimeOffset endUtc)
{
    var result = new List<SourceFile>();
    var monthlyStart = new DateTimeOffset(startUtc.Year, startUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    var monthlyEnd = new DateTimeOffset(endUtc.Year, endUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    for (var month = monthlyStart; month < monthlyEnd; month = month.AddMonths(1))
    {
        var name = $"{symbol}-1s-{month:yyyy-MM}.zip";
        result.Add(new SourceFile(
            symbol,
            $"https://data.binance.vision/data/spot/monthly/klines/{symbol}/1s/{name}",
            Path.Combine(symbol, "monthly", name)));
    }

    for (var day = monthlyEnd; day < endUtc; day = day.AddDays(1))
    {
        var name = $"{symbol}-1s-{day:yyyy-MM-dd}.zip";
        result.Add(new SourceFile(
            symbol,
            $"https://data.binance.vision/data/spot/daily/klines/{symbol}/1s/{name}",
            Path.Combine(symbol, "daily", name)));
    }

    return result;
}

async Task EnsureDownloadedAsync(HttpClient client, SourceFile source)
{
    var path = Path.Combine(dataDir, source.LocalPath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    if (File.Exists(path) && new FileInfo(path).Length > 0)
    {
        Console.WriteLine($"Using cached {source.LocalPath}");
        return;
    }

    var tempPath = path + ".tmp";
    Console.WriteLine($"Downloading {source.Url}");
    await using var responseStream = await client.GetStreamAsync(source.Url);
    await using var file = File.Create(tempPath);
    await responseStream.CopyToAsync(file);
    file.Close();
    File.Move(tempPath, path, overwrite: true);
}

static int LoadZipPrices(string path, long dataStartSecond, long dataEndSecond, double[] priceBySecond)
{
    using var archive = ZipFile.OpenRead(path);
    var entry = archive.Entries.FirstOrDefault(item => item.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        ?? archive.Entries.First();
    using var stream = entry.Open();
    using var reader = new StreamReader(stream);
    var loaded = 0;
    while (reader.ReadLine() is { } line)
    {
        if (line.Length == 0 || !char.IsDigit(line[0]))
        {
            continue;
        }

        if (!TryParseOpenTimeAndClose(line, out var openTimeMs, out var close))
        {
            continue;
        }

        var second = ToUnixSecond(openTimeMs);
        if (second < dataStartSecond || second >= dataEndSecond)
        {
            continue;
        }

        priceBySecond[second - dataStartSecond] = close;
        loaded++;
    }

    return loaded;
}

static bool TryParseOpenTimeAndClose(string line, out long openTimeMs, out double close)
{
    openTimeMs = 0;
    close = 0.0;

    var firstComma = line.IndexOf(',');
    if (firstComma <= 0)
    {
        return false;
    }

    if (!long.TryParse(line.AsSpan(0, firstComma), NumberStyles.Integer, CultureInfo.InvariantCulture, out openTimeMs))
    {
        return false;
    }

    var comma = firstComma;
    for (var field = 1; field < 4; field++)
    {
        comma = line.IndexOf(',', comma + 1);
        if (comma < 0)
        {
            return false;
        }
    }

    var closeEnd = line.IndexOf(',', comma + 1);
    if (closeEnd < 0)
    {
        return false;
    }

    return double.TryParse(line.AsSpan(comma + 1, closeEnd - comma - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out close);
}

static long ToUnixSecond(long exchangeTimestamp)
{
    return exchangeTimestamp > 10_000_000_000_000L
        ? exchangeTimestamp / 1_000_000L
        : exchangeTimestamp / 1_000L;
}

static int ForwardFill(double[] values)
{
    var count = 0;
    var last = double.NaN;
    for (var i = 0; i < values.Length; i++)
    {
        if (values[i] > 0.0 && !double.IsNaN(values[i]))
        {
            last = values[i];
            count++;
            continue;
        }

        if (last > 0.0 && !double.IsNaN(last))
        {
            values[i] = last;
        }
    }

    return count;
}

static long AlignUp(long value, long interval)
{
    var remainder = value % interval;
    return remainder == 0 ? value : value + interval - remainder;
}

static long AlignDown(long value, long interval)
{
    var remainder = value % interval;
    return value - remainder;
}

static string MonthKey(long unixSecond)
{
    return DateTimeOffset.FromUnixTimeSeconds(unixSecond).ToString("yyyy-MM", CultureInfo.InvariantCulture);
}

static double MoveBps(double fromPrice, double toPrice)
{
    return (toPrice - fromPrice) / fromPrice * 10_000.0;
}

static string Csv(string value)
{
    return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

static void WriteStrategyResults(string path, IEnumerable<StrategyStats> rows, double days)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.WriteLine("asset,offset_seconds,depth,threshold_bps,catalog_equivalent_rows,entries,settled_trades,wins,losses,ties,win_rate_pct,roi_pct_at_0_50,pnl_units,break_even_entry_price,roi_pct_at_0_52,wilson_lower_win_rate_pct,z_score,avg_abs_deviation_bps,avg_signed_deviation_bps,avg_current_move_bps,trades_per_day,max_drawdown_units,longest_loss_streak,active_months,profitable_active_months,worst_month_roi_pct,best_month_roi_pct,suitability");
    foreach (var row in rows)
    {
        writer.WriteLine(StrategyCsvRow(row, days));
    }
}

static void WriteTopResults(string path, IEnumerable<StrategyStats> rows, double days)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.WriteLine("rank,asset,offset_seconds,depth,threshold_bps,catalog_equivalent_rows,settled_trades,win_rate_pct,roi_pct_at_0_50,break_even_entry_price,roi_pct_at_0_52,wilson_lower_win_rate_pct,z_score,active_months,profitable_active_months,suitability");
    var rank = 1;
    foreach (var row in rows)
    {
        writer.WriteLine(string.Join(
            ',',
            rank.ToString(CultureInfo.InvariantCulture),
            row.Asset,
            row.OffsetSeconds.ToString(CultureInfo.InvariantCulture),
            row.Depth.ToString(CultureInfo.InvariantCulture),
            row.ThresholdBps.ToString(CultureInfo.InvariantCulture),
            row.CatalogEquivalentRows.ToString(CultureInfo.InvariantCulture),
            row.SettledTrades.ToString(CultureInfo.InvariantCulture),
            F(row.WinRatePct),
            F(row.RoiPctAtHalf),
            F(row.BreakEvenEntryPrice),
            F(row.RoiPctAt052),
            F(row.WilsonLowerWinRatePct),
            F(row.ZScore),
            row.ActiveMonths.ToString(CultureInfo.InvariantCulture),
            row.ProfitableActiveMonths.ToString(CultureInfo.InvariantCulture),
            Csv(row.Suitability)));
        rank++;
    }
}

static void WriteDeployOffsetTopResults(string path, IEnumerable<StrategyStats> rows, double days)
{
    WriteTopResults(path, rows, days);
}

static string StrategyCsvRow(StrategyStats row, double days)
{
    return string.Join(
        ',',
        row.Asset,
        row.OffsetSeconds.ToString(CultureInfo.InvariantCulture),
        row.Depth.ToString(CultureInfo.InvariantCulture),
        row.ThresholdBps.ToString(CultureInfo.InvariantCulture),
        row.CatalogEquivalentRows.ToString(CultureInfo.InvariantCulture),
        row.Entries.ToString(CultureInfo.InvariantCulture),
        row.SettledTrades.ToString(CultureInfo.InvariantCulture),
        row.Wins.ToString(CultureInfo.InvariantCulture),
        row.Losses.ToString(CultureInfo.InvariantCulture),
        row.Ties.ToString(CultureInfo.InvariantCulture),
        F(row.WinRatePct),
        F(row.RoiPctAtHalf),
        F(row.PnlUnits),
        F(row.BreakEvenEntryPrice),
        F(row.RoiPctAt052),
        F(row.WilsonLowerWinRatePct),
        F(row.ZScore),
        F(row.AvgAbsDeviationBps),
        F(row.AvgSignedDeviationBps),
        F(row.AvgCurrentMoveBps),
        F(row.TradesPerDay(days)),
        F(row.MaxDrawdownUnits),
        row.LongestLossStreak.ToString(CultureInfo.InvariantCulture),
        row.ActiveMonths.ToString(CultureInfo.InvariantCulture),
        row.ProfitableActiveMonths.ToString(CultureInfo.InvariantCulture),
        F(row.WorstMonthRoiPct),
        F(row.BestMonthRoiPct),
        Csv(row.Suitability));
}

static void WriteMonthlyResults(string path, IEnumerable<StrategyStats> rows)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.WriteLine("asset,offset_seconds,depth,threshold_bps,month,settled_trades,wins,losses,ties,win_rate_pct,roi_pct_at_0_50,pnl_units");
    foreach (var row in rows)
    {
        foreach (var month in row.Months.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var item = month.Value;
            writer.WriteLine(string.Join(
                ',',
                row.Asset,
                row.OffsetSeconds.ToString(CultureInfo.InvariantCulture),
                row.Depth.ToString(CultureInfo.InvariantCulture),
                row.ThresholdBps.ToString(CultureInfo.InvariantCulture),
                month.Key,
                item.SettledTrades.ToString(CultureInfo.InvariantCulture),
                item.Wins.ToString(CultureInfo.InvariantCulture),
                item.Losses.ToString(CultureInfo.InvariantCulture),
                item.Ties.ToString(CultureInfo.InvariantCulture),
                F(item.WinRatePct),
                F(item.RoiPctAtHalf),
                F(item.PnlUnits)));
        }
    }
}

static void WriteAssetSummary(string path, IEnumerable<BaselineStats> baselines, IEnumerable<StrategyStats> rows, double days)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.WriteLine("asset,markets_evaluated,baseline_up_win_rate_pct,baseline_down_win_rate_pct,baseline_up_roi_pct_at_0_50,baseline_down_roi_pct_at_0_50,best_offset_seconds,best_depth,best_threshold_bps,best_settled_trades,best_win_rate_pct,best_roi_pct_at_0_50,best_break_even_entry_price,best_roi_pct_at_0_52,best_suitability");
    foreach (var baseline in baselines.OrderBy(item => item.Asset, StringComparer.Ordinal))
    {
        var best = rows
            .Where(item => item.Asset == baseline.Asset && item.SettledTrades >= 500)
            .OrderByDescending(item => item.RoiPctAtHalf)
            .ThenByDescending(item => item.SettledTrades)
            .FirstOrDefault();
        writer.WriteLine(string.Join(
            ',',
            baseline.Asset,
            baseline.MarketsEvaluated.ToString(CultureInfo.InvariantCulture),
            F(baseline.UpWinRatePct),
            F(baseline.DownWinRatePct),
            F(baseline.UpRoiPctAtHalf),
            F(baseline.DownRoiPctAtHalf),
            best?.OffsetSeconds.ToString(CultureInfo.InvariantCulture) ?? "",
            best?.Depth.ToString(CultureInfo.InvariantCulture) ?? "",
            best?.ThresholdBps.ToString(CultureInfo.InvariantCulture) ?? "",
            best?.SettledTrades.ToString(CultureInfo.InvariantCulture) ?? "",
            best is null ? "" : F(best.WinRatePct),
            best is null ? "" : F(best.RoiPctAtHalf),
            best is null ? "" : F(best.BreakEvenEntryPrice),
            best is null ? "" : F(best.RoiPctAt052),
            best?.Suitability ?? ""));
        _ = days;
    }
}

static void WriteMarkdownReport(
    string path,
    IEnumerable<StrategyStats> stats,
    IReadOnlyList<StrategyStats> ordered,
    IReadOnlyList<BaselineStats> baselines,
    DateTimeOffset startUtc,
    DateTimeOffset endUtc,
    double days,
    IReadOnlyList<SourceFile> files)
{
    var all = stats.ToArray();
    var candidates = all.Where(item => item.Suitability == StrategyStats.ResearchCandidate).OrderByDescending(item => item.RoiPctAtHalf).ToArray();
    var watch = all.Where(item => item.Suitability == StrategyStats.WatchOnly).OrderByDescending(item => item.RoiPctAtHalf).ToArray();
    using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.WriteLine("# Middle Premarket currency-only backtest");
    writer.WriteLine();
    writer.WriteLine($"Range: {startUtc:O} through {endUtc:O} exclusive.");
    writer.WriteLine($"Assets: {string.Join(", ", baselines.Select(item => item.Asset))}. Markets evaluated per asset: {string.Join(", ", baselines.Select(item => item.Asset + "=" + item.MarketsEvaluated.ToString(CultureInfo.InvariantCulture)))}.");
    writer.WriteLine("Model: current catalog Middle signal moved to premarket time. At `market_start - offset`, compare Binance price to the arithmetic mean of the latest N sampled prices; above mean buys Down, below mean buys Up. Threshold rows require absolute deviation from mean >= threshold bps. Outcome uses the asset's Binance move over the current 5m window.");
    writer.WriteLine($"Sampling model: one sample every {SampleIntervalSeconds.ToString(CultureInfo.InvariantCulture)} seconds, latest sample aligned to UTC minute boundary at or before decision time; depths are 100,90,...,10. This approximates `BinanceBtcUsdReference`/`BinanceCryptoReference` rolling sample caches and fixes the otherwise runtime-dependent sample phase.");
    writer.WriteLine($"Entry-price model: assumed fixed entry price {AssumedEntryPrice.ToString("0.00", CultureInfo.InvariantCulture)}. Break-even entry price equals win rate. `roi_pct_at_0_52` is included as a sensitivity check for slightly expensive premarket asks. No Polymarket pre-start order book depth/liquidity is simulated.");
    writer.WriteLine("Timestamp rule: price at a timestamp is the last Binance 1s close strictly before that timestamp.");
    writer.WriteLine();
    writer.WriteLine("## Baseline");
    writer.WriteLine();
    writer.WriteLine("| Asset | Markets | Up win % | Up ROI@0.50 % | Down win % | Down ROI@0.50 % |");
    writer.WriteLine("|---|---:|---:|---:|---:|---:|");
    foreach (var baseline in baselines.OrderBy(item => item.Asset, StringComparer.Ordinal))
    {
        writer.WriteLine($"| {baseline.Asset} | {baseline.MarketsEvaluated.ToString(CultureInfo.InvariantCulture)} | {F(baseline.UpWinRatePct)} | {F(baseline.UpRoiPctAtHalf)} | {F(baseline.DownWinRatePct)} | {F(baseline.DownRoiPctAtHalf)} |");
    }

    writer.WriteLine();
    writer.WriteLine("## Suitability counts");
    writer.WriteLine();
    foreach (var group in all.GroupBy(item => item.Suitability).OrderBy(item => item.Key, StringComparer.Ordinal))
    {
        writer.WriteLine($"- {group.Key}: {group.Count().ToString(CultureInfo.InvariantCulture)}");
    }

    writer.WriteLine();
    writer.WriteLine("## Top by ROI, minimum 500 settled trades");
    writer.WriteLine();
    writer.WriteLine("| Rank | Asset | Offset | Depth | Threshold | Settled | Win % | ROI@0.50 % | BE price | ROI@0.52 % | Wilson lower % | Months | Suitability |");
    writer.WriteLine("|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
    var rank = 1;
    foreach (var item in ordered.Where(item => item.SettledTrades >= 500).Take(30))
    {
        writer.WriteLine($"| {rank.ToString(CultureInfo.InvariantCulture)} | {item.Asset} | -{item.OffsetSeconds.ToString(CultureInfo.InvariantCulture)}s | {item.Depth.ToString(CultureInfo.InvariantCulture)} | {item.ThresholdBps.ToString(CultureInfo.InvariantCulture)} | {item.SettledTrades.ToString(CultureInfo.InvariantCulture)} | {F(item.WinRatePct)} | {F(item.RoiPctAtHalf)} | {F(item.BreakEvenEntryPrice)} | {F(item.RoiPctAt052)} | {F(item.WilsonLowerWinRatePct)} | {item.ProfitableActiveMonths.ToString(CultureInfo.InvariantCulture)}/{item.ActiveMonths.ToString(CultureInfo.InvariantCulture)} | {item.Suitability} |");
        rank++;
    }

    writer.WriteLine();
    writer.WriteLine("## Top deploy-relevant offsets");
    writer.WriteLine();
    writer.WriteLine("Only offsets matching the existing ETH Premarket shape are shown here: -30s, -10s, -5s.");
    writer.WriteLine();
    writer.WriteLine("| Rank | Asset | Offset | Depth | Threshold | Settled | Win % | ROI@0.50 % | BE price | ROI@0.52 % | Months | Suitability |");
    writer.WriteLine("|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
    rank = 1;
    foreach (var item in all
        .Where(item => item.SettledTrades >= 500 && (item.OffsetSeconds == 30 || item.OffsetSeconds == 10 || item.OffsetSeconds == 5))
        .OrderByDescending(item => item.RoiPctAtHalf)
        .ThenByDescending(item => item.SettledTrades)
        .Take(30))
    {
        writer.WriteLine($"| {rank.ToString(CultureInfo.InvariantCulture)} | {item.Asset} | -{item.OffsetSeconds.ToString(CultureInfo.InvariantCulture)}s | {item.Depth.ToString(CultureInfo.InvariantCulture)} | {item.ThresholdBps.ToString(CultureInfo.InvariantCulture)} | {item.SettledTrades.ToString(CultureInfo.InvariantCulture)} | {F(item.WinRatePct)} | {F(item.RoiPctAtHalf)} | {F(item.BreakEvenEntryPrice)} | {F(item.RoiPctAt052)} | {item.ProfitableActiveMonths.ToString(CultureInfo.InvariantCulture)}/{item.ActiveMonths.ToString(CultureInfo.InvariantCulture)} | {item.Suitability} |");
        rank++;
    }

    writer.WriteLine();
    writer.WriteLine("## Best by asset");
    writer.WriteLine();
    foreach (var asset in baselines.Select(item => item.Asset).OrderBy(item => item, StringComparer.Ordinal))
    {
        var assetBest = all
            .Where(item => item.Asset == asset && item.SettledTrades >= 500)
            .OrderByDescending(item => item.RoiPctAtHalf)
            .ThenByDescending(item => item.SettledTrades)
            .Take(10)
            .ToArray();
        writer.WriteLine($"### {asset}");
        writer.WriteLine();
        if (assetBest.Length == 0)
        {
            writer.WriteLine("No strategy reached 500 settled trades.");
            writer.WriteLine();
            continue;
        }

        foreach (var item in assetBest)
        {
            writer.WriteLine($"- -{item.OffsetSeconds.ToString(CultureInfo.InvariantCulture)}s / depth {item.Depth.ToString(CultureInfo.InvariantCulture)} / {item.ThresholdBps.ToString(CultureInfo.InvariantCulture)} bps: settled={item.SettledTrades.ToString(CultureInfo.InvariantCulture)}, win={F(item.WinRatePct)}%, ROI@0.50={F(item.RoiPctAtHalf)}%, BE={F(item.BreakEvenEntryPrice)}, ROI@0.52={F(item.RoiPctAt052)}%, months={item.ProfitableActiveMonths.ToString(CultureInfo.InvariantCulture)}/{item.ActiveMonths.ToString(CultureInfo.InvariantCulture)}, {item.Suitability}.");
        }

        writer.WriteLine();
    }

    writer.WriteLine("## Candidate sets");
    writer.WriteLine();
    writer.WriteLine($"Research candidates: {candidates.Length.ToString(CultureInfo.InvariantCulture)}. Watch-only: {watch.Length.ToString(CultureInfo.InvariantCulture)}.");
    writer.WriteLine();
    writer.WriteLine("Research-candidate gate: >=500 settled trades, >=4 active months, ROI@0.50 >= 3%, Wilson lower win rate > 50%, and at least 60% profitable active months. This is a research filter, not a live-trading recommendation.");
    writer.WriteLine();
    writer.WriteLine("## Source files");
    writer.WriteLine();
    foreach (var file in files.OrderBy(item => item.Symbol, StringComparer.Ordinal).ThenBy(item => item.LocalPath, StringComparer.Ordinal))
    {
        writer.WriteLine($"- {file.Url}");
    }

    _ = days;
}

static string F(double value)
{
    return value.ToString("0.####", CultureInfo.InvariantCulture);
}

sealed record SourceFile(string Symbol, string Url, string LocalPath);

enum Outcome
{
    Up,
    Down,
    Tie
}

sealed class BaselineStats(string asset)
{
    public string Asset { get; } = asset;
    public int UpWins { get; private set; }
    public int DownWins { get; private set; }
    public int Ties { get; private set; }
    public int MarketsEvaluated { get; set; }
    public int MissingPriceSkips { get; set; }

    public int SettledTrades => UpWins + DownWins;
    public double UpWinRatePct => SettledTrades == 0 ? 0.0 : 100.0 * UpWins / SettledTrades;
    public double DownWinRatePct => SettledTrades == 0 ? 0.0 : 100.0 * DownWins / SettledTrades;
    public double UpRoiPctAtHalf => 2.0 * UpWinRatePct - 100.0;
    public double DownRoiPctAtHalf => 2.0 * DownWinRatePct - 100.0;

    public void Add(Outcome outcome, string month, double currentMoveBps)
    {
        _ = month;
        _ = currentMoveBps;
        if (outcome == Outcome.Up)
        {
            UpWins++;
        }
        else if (outcome == Outcome.Down)
        {
            DownWins++;
        }
        else
        {
            Ties++;
        }
    }
}

sealed class StrategyStats(string asset, int offsetSeconds, int depth, int thresholdBps)
{
    public const string ResearchCandidate = "research_candidate";
    public const string WatchOnly = "watch_only";
    public const string Reject = "reject";
    public const string InsufficientSample = "insufficient_sample";

    private double absDeviationBpsSum;
    private double signedDeviationBpsSum;
    private double currentMoveBpsSum;
    private double cumulativePnl;
    private double peakPnl;
    private int currentLossStreak;

    public string Asset { get; } = asset;
    public int OffsetSeconds { get; } = offsetSeconds;
    public int Depth { get; } = depth;
    public int ThresholdBps { get; } = thresholdBps;
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public int Ties { get; private set; }
    public double MaxDrawdownUnits { get; private set; }
    public int LongestLossStreak { get; private set; }
    public Dictionary<string, MonthlyStats> Months { get; } = new(StringComparer.Ordinal);

    public int CatalogEquivalentRows => ThresholdBps == 0 ? 1 : 2;
    public int Entries => Wins + Losses + Ties;
    public int SettledTrades => Wins + Losses;
    public double PnlUnits => Wins - Losses;
    public double WinRate => SettledTrades == 0 ? 0.0 : (double)Wins / SettledTrades;
    public double WinRatePct => 100.0 * WinRate;
    public double RoiPctAtHalf => Entries == 0 ? 0.0 : 100.0 * PnlUnits / Entries;
    public double BreakEvenEntryPrice => WinRate;
    public double RoiPctAt052 => AssumedRoiPct(0.52);
    public double AvgAbsDeviationBps => Entries == 0 ? 0.0 : absDeviationBpsSum / Entries;
    public double AvgSignedDeviationBps => Entries == 0 ? 0.0 : signedDeviationBpsSum / Entries;
    public double AvgCurrentMoveBps => Entries == 0 ? 0.0 : currentMoveBpsSum / Entries;
    public double WilsonLowerWinRatePct => 100.0 * WilsonLower(Wins, SettledTrades);
    public double ZScore => SettledTrades == 0 ? 0.0 : (Wins - SettledTrades * 0.5) / Math.Sqrt(SettledTrades * 0.25);
    public int ActiveMonths => Months.Values.Count(item => item.SettledTrades >= 20);
    public int ProfitableActiveMonths => Months.Values.Count(item => item.SettledTrades >= 20 && item.PnlUnits > 0.0);
    public double WorstMonthRoiPct => Months.Values.Where(item => item.SettledTrades >= 20).Select(item => item.RoiPctAtHalf).DefaultIfEmpty(0.0).Min();
    public double BestMonthRoiPct => Months.Values.Where(item => item.SettledTrades >= 20).Select(item => item.RoiPctAtHalf).DefaultIfEmpty(0.0).Max();

    public string Suitability
    {
        get
        {
            if (SettledTrades < 500 || ActiveMonths < 4)
            {
                return InsufficientSample;
            }

            var positiveMonthShare = ActiveMonths == 0 ? 0.0 : (double)ProfitableActiveMonths / ActiveMonths;
            if (RoiPctAtHalf >= 3.0 && WilsonLowerWinRatePct > 50.0 && positiveMonthShare >= 0.60)
            {
                return ResearchCandidate;
            }

            if (RoiPctAtHalf > 0.0 && WinRatePct > 50.0 && positiveMonthShare >= 0.50)
            {
                return WatchOnly;
            }

            return Reject;
        }
    }

    public void Add(Outcome selectedOutcome, Outcome actualOutcome, string month, double signedDeviationBps, double currentMoveBps)
    {
        absDeviationBpsSum += Math.Abs(signedDeviationBps);
        signedDeviationBpsSum += signedDeviationBps;
        currentMoveBpsSum += currentMoveBps;

        if (!Months.TryGetValue(month, out var monthly))
        {
            monthly = new MonthlyStats();
            Months.Add(month, monthly);
        }

        if (actualOutcome == Outcome.Tie)
        {
            Ties++;
            currentLossStreak = 0;
            monthly.AddTie();
            return;
        }

        if (selectedOutcome == actualOutcome)
        {
            Wins++;
            cumulativePnl += 1.0;
            currentLossStreak = 0;
            monthly.AddWin();
        }
        else
        {
            Losses++;
            cumulativePnl -= 1.0;
            currentLossStreak++;
            LongestLossStreak = Math.Max(LongestLossStreak, currentLossStreak);
            monthly.AddLoss();
        }

        peakPnl = Math.Max(peakPnl, cumulativePnl);
        MaxDrawdownUnits = Math.Max(MaxDrawdownUnits, peakPnl - cumulativePnl);
    }

    public double TradesPerDay(double days)
    {
        return days <= 0.0 ? 0.0 : Entries / days;
    }

    private double AssumedRoiPct(double entryPrice)
    {
        return entryPrice <= 0.0 ? 0.0 : 100.0 * (WinRate - entryPrice) / entryPrice;
    }

    private static double WilsonLower(int wins, int n)
    {
        if (n == 0)
        {
            return 0.0;
        }

        const double z = 1.96;
        var p = (double)wins / n;
        var z2 = z * z;
        var denominator = 1.0 + z2 / n;
        var center = p + z2 / (2.0 * n);
        var margin = z * Math.Sqrt((p * (1.0 - p) + z2 / (4.0 * n)) / n);
        return Math.Max(0.0, (center - margin) / denominator);
    }
}

sealed class MonthlyStats
{
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public int Ties { get; private set; }
    public int SettledTrades => Wins + Losses;
    public double PnlUnits => Wins - Losses;
    public double WinRatePct => SettledTrades == 0 ? 0.0 : 100.0 * Wins / SettledTrades;
    public double RoiPctAtHalf => Wins + Losses + Ties == 0 ? 0.0 : 100.0 * PnlUnits / (Wins + Losses + Ties);

    public void AddWin()
    {
        Wins++;
    }

    public void AddLoss()
    {
        Losses++;
    }

    public void AddTie()
    {
        Ties++;
    }
}
