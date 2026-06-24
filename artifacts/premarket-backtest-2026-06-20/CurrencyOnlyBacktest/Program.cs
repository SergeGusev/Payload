using System.Globalization;
using System.IO.Compression;
using System.Text;

const string Symbol = "ETHUSDT";
var artifactRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "premarket-backtest-2026-06-20"));
var dataDir = Path.Combine(artifactRoot, "data");
Directory.CreateDirectory(dataDir);

var backtestStartUtc = new DateTimeOffset(2025, 12, 20, 0, 0, 0, TimeSpan.Zero);
var backtestEndUtc = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);
var dataStartUtc = backtestStartUtc.AddMinutes(-10);
var dataStartSecond = dataStartUtc.ToUnixTimeSeconds();
var backtestStartSecond = backtestStartUtc.ToUnixTimeSeconds();
var backtestEndSecond = backtestEndUtc.ToUnixTimeSeconds();
var priceBySecond = new double[checked((int)(backtestEndSecond - dataStartSecond))];
Array.Fill(priceBySecond, double.NaN);

var offsets = new[] { 30, 25, 20, 15, 10, 5 };
var stats = offsets
    .SelectMany(offset => Enumerable.Range(1, 50).Select(threshold => new StrategyStats(offset, threshold)))
    .ToDictionary(item => (item.OffsetSeconds, item.ThresholdBps));
var baseline = new BaselineStats();

using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromMinutes(15)
};

var files = BuildSourceFiles(backtestStartUtc, backtestEndUtc);
Console.WriteLine($"Downloading/using {files.Count.ToString(CultureInfo.InvariantCulture)} Binance {Symbol} 1s kline archives...");
foreach (var source in files)
{
    await EnsureDownloadedAsync(httpClient, source.Url, source.LocalPath);
}

Console.WriteLine("Parsing Binance archives...");
foreach (var source in files)
{
    var rows = LoadZipPrices(Path.Combine(dataDir, source.LocalPath), dataStartSecond, backtestEndSecond, priceBySecond);
    Console.WriteLine($"{Path.GetFileName(source.LocalPath)} rows loaded: {rows.ToString(CultureInfo.InvariantCulture)}");
}

var filledSeconds = ForwardFill(priceBySecond);
Console.WriteLine($"Usable second prices after forward-fill: {filledSeconds.ToString(CultureInfo.InvariantCulture)}");

var firstStart = AlignUp(backtestStartSecond + 300, 300);
var marketCount = 0;
var skippedMissingPrice = 0;
for (var currentStart = firstStart; currentStart + 300 <= backtestEndSecond; currentStart += 300)
{
    var currentEnd = currentStart + 300;
    if (!TryGetPriceBefore(currentStart, out var currentStartPrice) ||
        !TryGetPriceBefore(currentEnd, out var currentEndPrice))
    {
        skippedMissingPrice++;
        continue;
    }

    var currentMoveBps = (currentEndPrice - currentStartPrice) / currentStartPrice * 10_000.0;
    var outcome = currentMoveBps < 0.0 ? Outcome.Down : currentMoveBps > 0.0 ? Outcome.Up : Outcome.Tie;
    baseline.Add(outcome, MonthKey(currentStart), currentMoveBps);
    marketCount++;

    var previousStart = currentStart - 300;
    if (!TryGetPriceBefore(previousStart, out var previousStartPrice))
    {
        skippedMissingPrice++;
        continue;
    }

    foreach (var offset in offsets)
    {
        var sampleTime = currentStart - offset;
        if (!TryGetPriceBefore(sampleTime, out var previousSamplePrice))
        {
            skippedMissingPrice++;
            continue;
        }

        var previousMoveBps = (previousSamplePrice - previousStartPrice) / previousStartPrice * 10_000.0;
        if (previousMoveBps <= 0.0)
        {
            continue;
        }

        var maxThreshold = Math.Min(50, (int)Math.Floor(previousMoveBps + 1e-12));
        for (var threshold = 1; threshold <= maxThreshold; threshold++)
        {
            stats[(offset, threshold)].Add(outcome, MonthKey(currentStart), previousMoveBps, currentMoveBps);
        }
    }
}

Console.WriteLine($"Markets evaluated: {marketCount.ToString(CultureInfo.InvariantCulture)}");
Console.WriteLine($"Missing-price skips: {skippedMissingPrice.ToString(CultureInfo.InvariantCulture)}");

var ordered = stats.Values
    .OrderByDescending(item => item.SettledTrades >= 500 ? item.RoiPct : double.NegativeInfinity)
    .ThenByDescending(item => item.SettledTrades)
    .ToArray();

WriteStrategyResults(Path.Combine(artifactRoot, "strategy_results.csv"), stats.Values.OrderBy(item => item.OffsetSeconds).ThenBy(item => item.ThresholdBps));
WriteTopResults(Path.Combine(artifactRoot, "top_by_roi.csv"), ordered.Take(50));
WriteMonthlyResults(Path.Combine(artifactRoot, "monthly_summary.csv"), stats.Values.OrderBy(item => item.OffsetSeconds).ThenBy(item => item.ThresholdBps));
WriteMarkdownReport(Path.Combine(artifactRoot, "summary.md"), stats.Values, ordered, baseline, backtestStartUtc, backtestEndUtc, marketCount, skippedMissingPrice, files);

Console.WriteLine("Wrote:");
Console.WriteLine(Path.Combine(artifactRoot, "strategy_results.csv"));
Console.WriteLine(Path.Combine(artifactRoot, "top_by_roi.csv"));
Console.WriteLine(Path.Combine(artifactRoot, "monthly_summary.csv"));
Console.WriteLine(Path.Combine(artifactRoot, "summary.md"));

bool TryGetPriceBefore(long timestampSecond, out double price)
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

static IReadOnlyList<SourceFile> BuildSourceFiles(DateTimeOffset startUtc, DateTimeOffset endUtc)
{
    var result = new List<SourceFile>();
    var monthlyStart = new DateTimeOffset(startUtc.Year, startUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    var monthlyEnd = new DateTimeOffset(endUtc.Year, endUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    for (var month = monthlyStart; month < monthlyEnd; month = month.AddMonths(1))
    {
        var name = $"{Symbol}-1s-{month:yyyy-MM}.zip";
        result.Add(new SourceFile(
            $"https://data.binance.vision/data/spot/monthly/klines/{Symbol}/1s/{name}",
            Path.Combine("monthly", name)));
    }

    for (var day = monthlyEnd; day < endUtc; day = day.AddDays(1))
    {
        var name = $"{Symbol}-1s-{day:yyyy-MM-dd}.zip";
        result.Add(new SourceFile(
            $"https://data.binance.vision/data/spot/daily/klines/{Symbol}/1s/{name}",
            Path.Combine("daily", name)));
    }

    return result;
}

async Task EnsureDownloadedAsync(HttpClient client, string url, string relativePath)
{
    var path = Path.Combine(dataDir, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    if (File.Exists(path) && new FileInfo(path).Length > 0)
    {
        Console.WriteLine($"Using cached {relativePath}");
        return;
    }

    var tempPath = path + ".tmp";
    Console.WriteLine($"Downloading {url}");
    await using var responseStream = await client.GetStreamAsync(url);
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
            count++;
        }
    }

    return count;
}

static long AlignUp(long value, long interval)
{
    var remainder = value % interval;
    return remainder == 0 ? value : value + interval - remainder;
}

static string MonthKey(long unixSecond)
{
    return DateTimeOffset.FromUnixTimeSeconds(unixSecond).ToString("yyyy-MM", CultureInfo.InvariantCulture);
}

static string Csv(string value)
{
    return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

static void WriteStrategyResults(string path, IEnumerable<StrategyStats> rows)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.WriteLine("offset_seconds,threshold_bps,entries,settled_trades,wins,losses,ties,win_rate_pct,roi_pct,pnl_units,wilson_lower_win_rate_pct,z_score,avg_prev_move_bps,avg_current_move_bps,trades_per_day,max_drawdown_units,longest_loss_streak,active_months,profitable_active_months,worst_month_roi_pct,best_month_roi_pct,suitability");
    foreach (var row in rows)
    {
        writer.WriteLine(string.Join(
            ',',
            row.OffsetSeconds.ToString(CultureInfo.InvariantCulture),
            row.ThresholdBps.ToString(CultureInfo.InvariantCulture),
            row.Entries.ToString(CultureInfo.InvariantCulture),
            row.SettledTrades.ToString(CultureInfo.InvariantCulture),
            row.Wins.ToString(CultureInfo.InvariantCulture),
            row.Losses.ToString(CultureInfo.InvariantCulture),
            row.Ties.ToString(CultureInfo.InvariantCulture),
            F(row.WinRatePct),
            F(row.RoiPct),
            F(row.PnlUnits),
            F(row.WilsonLowerWinRatePct),
            F(row.ZScore),
            F(row.AvgPreviousMoveBps),
            F(row.AvgCurrentMoveBps),
            F(row.TradesPerDay(182.0)),
            F(row.MaxDrawdownUnits),
            row.LongestLossStreak.ToString(CultureInfo.InvariantCulture),
            row.ActiveMonths.ToString(CultureInfo.InvariantCulture),
            row.ProfitableActiveMonths.ToString(CultureInfo.InvariantCulture),
            F(row.WorstMonthRoiPct),
            F(row.BestMonthRoiPct),
            Csv(row.Suitability)));
    }
}

static void WriteTopResults(string path, IEnumerable<StrategyStats> rows)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.WriteLine("rank,offset_seconds,threshold_bps,settled_trades,win_rate_pct,roi_pct,pnl_units,wilson_lower_win_rate_pct,z_score,active_months,profitable_active_months,suitability");
    var rank = 1;
    foreach (var row in rows)
    {
        writer.WriteLine(string.Join(
            ',',
            rank.ToString(CultureInfo.InvariantCulture),
            row.OffsetSeconds.ToString(CultureInfo.InvariantCulture),
            row.ThresholdBps.ToString(CultureInfo.InvariantCulture),
            row.SettledTrades.ToString(CultureInfo.InvariantCulture),
            F(row.WinRatePct),
            F(row.RoiPct),
            F(row.PnlUnits),
            F(row.WilsonLowerWinRatePct),
            F(row.ZScore),
            row.ActiveMonths.ToString(CultureInfo.InvariantCulture),
            row.ProfitableActiveMonths.ToString(CultureInfo.InvariantCulture),
            Csv(row.Suitability)));
        rank++;
    }
}

static void WriteMonthlyResults(string path, IEnumerable<StrategyStats> rows)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.WriteLine("offset_seconds,threshold_bps,month,settled_trades,wins,losses,ties,win_rate_pct,roi_pct,pnl_units");
    foreach (var row in rows)
    {
        foreach (var month in row.Months.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var item = month.Value;
            writer.WriteLine(string.Join(
                ',',
                row.OffsetSeconds.ToString(CultureInfo.InvariantCulture),
                row.ThresholdBps.ToString(CultureInfo.InvariantCulture),
                month.Key,
                item.SettledTrades.ToString(CultureInfo.InvariantCulture),
                item.Wins.ToString(CultureInfo.InvariantCulture),
                item.Losses.ToString(CultureInfo.InvariantCulture),
                item.Ties.ToString(CultureInfo.InvariantCulture),
                F(item.WinRatePct),
                F(item.RoiPct),
                F(item.PnlUnits)));
        }
    }
}

static void WriteMarkdownReport(
    string path,
    IEnumerable<StrategyStats> stats,
    IReadOnlyList<StrategyStats> ordered,
    BaselineStats baseline,
    DateTimeOffset startUtc,
    DateTimeOffset endUtc,
    int marketCount,
    int skippedMissingPrice,
    IReadOnlyList<SourceFile> files)
{
    var all = stats.ToArray();
    var candidates = all.Where(item => item.Suitability == StrategyStats.ResearchCandidate).OrderByDescending(item => item.RoiPct).ToArray();
    var watch = all.Where(item => item.Suitability == StrategyStats.WatchOnly).OrderByDescending(item => item.RoiPct).ToArray();
    using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.WriteLine("# ETH Premarket currency-only backtest");
    writer.WriteLine();
    writer.WriteLine($"Range: {startUtc:O} through {endUtc:O} exclusive.");
    writer.WriteLine($"Markets evaluated: {marketCount.ToString(CultureInfo.InvariantCulture)}. Missing-price skips: {skippedMissingPrice.ToString(CultureInfo.InvariantCulture)}.");
    writer.WriteLine("Model: fixed Down countertrend. Enter Down when the previous ETH 5m move from previous start to start-offset is positive and at least threshold bps. Entry price is assumed to be 0.5. Outcome uses ETHUSDT move over the current 5m window.");
    writer.WriteLine("Timestamp rule: price at a timestamp is the last Binance 1s close strictly before that timestamp.");
    writer.WriteLine();
    writer.WriteLine("## Baseline");
    writer.WriteLine();
    writer.WriteLine($"All 5m Down baseline: settled={baseline.SettledTrades.ToString(CultureInfo.InvariantCulture)}, win_rate={F(baseline.WinRatePct)}%, ROI={F(baseline.RoiPct)}%, pnl_units={F(baseline.PnlUnits)}.");
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
    writer.WriteLine("| Rank | Offset | Threshold | Settled | Win % | ROI % | Wilson lower % | Z | Active months | Positive months | Suitability |");
    writer.WriteLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
    var rank = 1;
    foreach (var item in ordered.Where(item => item.SettledTrades >= 500).Take(20))
    {
        writer.WriteLine($"| {rank.ToString(CultureInfo.InvariantCulture)} | -{item.OffsetSeconds.ToString(CultureInfo.InvariantCulture)}s | {item.ThresholdBps.ToString(CultureInfo.InvariantCulture)} | {item.SettledTrades.ToString(CultureInfo.InvariantCulture)} | {F(item.WinRatePct)} | {F(item.RoiPct)} | {F(item.WilsonLowerWinRatePct)} | {F(item.ZScore)} | {item.ActiveMonths.ToString(CultureInfo.InvariantCulture)} | {item.ProfitableActiveMonths.ToString(CultureInfo.InvariantCulture)} | {item.Suitability} |");
        rank++;
    }

    writer.WriteLine();
    writer.WriteLine("## Best candidate set");
    writer.WriteLine();
    if (candidates.Length == 0)
    {
        writer.WriteLine("No strategy passed the research-candidate gate.");
    }
    else
    {
        foreach (var item in candidates.Take(30))
        {
            writer.WriteLine($"- -{item.OffsetSeconds.ToString(CultureInfo.InvariantCulture)}s / {item.ThresholdBps.ToString(CultureInfo.InvariantCulture)} bps: settled={item.SettledTrades.ToString(CultureInfo.InvariantCulture)}, win={F(item.WinRatePct)}%, ROI={F(item.RoiPct)}%, Wilson lower={F(item.WilsonLowerWinRatePct)}%, months={item.ProfitableActiveMonths.ToString(CultureInfo.InvariantCulture)}/{item.ActiveMonths.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    writer.WriteLine();
    writer.WriteLine("## Watch-only set");
    writer.WriteLine();
    if (watch.Length == 0)
    {
        writer.WriteLine("No strategy passed the watch-only gate.");
    }
    else
    {
        foreach (var item in watch.Take(30))
        {
            writer.WriteLine($"- -{item.OffsetSeconds.ToString(CultureInfo.InvariantCulture)}s / {item.ThresholdBps.ToString(CultureInfo.InvariantCulture)} bps: settled={item.SettledTrades.ToString(CultureInfo.InvariantCulture)}, win={F(item.WinRatePct)}%, ROI={F(item.RoiPct)}%, Wilson lower={F(item.WilsonLowerWinRatePct)}%, months={item.ProfitableActiveMonths.ToString(CultureInfo.InvariantCulture)}/{item.ActiveMonths.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    writer.WriteLine();
    writer.WriteLine("## Source files");
    writer.WriteLine();
    foreach (var file in files)
    {
        writer.WriteLine($"- {file.Url}");
    }
}

static string F(double value)
{
    return value.ToString("0.####", CultureInfo.InvariantCulture);
}

sealed record SourceFile(string Url, string RelativePath)
{
    public string LocalPath => RelativePath;
}

enum Outcome
{
    Up,
    Down,
    Tie
}

sealed class BaselineStats
{
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public int Ties { get; private set; }
    public double CurrentMoveBpsSum { get; private set; }

    public int SettledTrades => Wins + Losses;
    public int Entries => Wins + Losses + Ties;
    public double PnlUnits => Wins - Losses;
    public double WinRatePct => SettledTrades == 0 ? 0.0 : 100.0 * Wins / SettledTrades;
    public double RoiPct => Entries == 0 ? 0.0 : 100.0 * PnlUnits / Entries;

    public void Add(Outcome outcome, string month, double currentMoveBps)
    {
        _ = month;
        CurrentMoveBpsSum += currentMoveBps;
        if (outcome == Outcome.Down)
        {
            Wins++;
        }
        else if (outcome == Outcome.Up)
        {
            Losses++;
        }
        else
        {
            Ties++;
        }
    }
}

sealed class StrategyStats(int offsetSeconds, int thresholdBps)
{
    public const string ResearchCandidate = "research_candidate";
    public const string WatchOnly = "watch_only";
    public const string Reject = "reject";
    public const string InsufficientSample = "insufficient_sample";

    private double previousMoveBpsSum;
    private double currentMoveBpsSum;
    private double cumulativePnl;
    private double peakPnl;
    private int currentLossStreak;

    public int OffsetSeconds { get; } = offsetSeconds;
    public int ThresholdBps { get; } = thresholdBps;
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public int Ties { get; private set; }
    public double MaxDrawdownUnits { get; private set; }
    public int LongestLossStreak { get; private set; }
    public Dictionary<string, MonthlyStats> Months { get; } = new(StringComparer.Ordinal);

    public int Entries => Wins + Losses + Ties;
    public int SettledTrades => Wins + Losses;
    public double PnlUnits => Wins - Losses;
    public double WinRatePct => SettledTrades == 0 ? 0.0 : 100.0 * Wins / SettledTrades;
    public double RoiPct => Entries == 0 ? 0.0 : 100.0 * PnlUnits / Entries;
    public double AvgPreviousMoveBps => Entries == 0 ? 0.0 : previousMoveBpsSum / Entries;
    public double AvgCurrentMoveBps => Entries == 0 ? 0.0 : currentMoveBpsSum / Entries;
    public double WilsonLowerWinRatePct => 100.0 * WilsonLower(Wins, SettledTrades);
    public double ZScore => SettledTrades == 0 ? 0.0 : (Wins - SettledTrades * 0.5) / Math.Sqrt(SettledTrades * 0.25);
    public int ActiveMonths => Months.Values.Count(item => item.SettledTrades >= 20);
    public int ProfitableActiveMonths => Months.Values.Count(item => item.SettledTrades >= 20 && item.PnlUnits > 0.0);
    public double WorstMonthRoiPct => Months.Values.Where(item => item.SettledTrades >= 20).Select(item => item.RoiPct).DefaultIfEmpty(0.0).Min();
    public double BestMonthRoiPct => Months.Values.Where(item => item.SettledTrades >= 20).Select(item => item.RoiPct).DefaultIfEmpty(0.0).Max();

    public string Suitability
    {
        get
        {
            if (SettledTrades < 500 || ActiveMonths < 4)
            {
                return InsufficientSample;
            }

            var positiveMonthShare = ActiveMonths == 0 ? 0.0 : (double)ProfitableActiveMonths / ActiveMonths;
            if (RoiPct >= 3.0 && WilsonLowerWinRatePct > 50.0 && positiveMonthShare >= 0.60)
            {
                return ResearchCandidate;
            }

            if (RoiPct > 0.0 && WinRatePct > 50.0 && positiveMonthShare >= 0.50)
            {
                return WatchOnly;
            }

            return Reject;
        }
    }

    public void Add(Outcome outcome, string month, double previousMoveBps, double currentMoveBps)
    {
        previousMoveBpsSum += previousMoveBps;
        currentMoveBpsSum += currentMoveBps;

        if (!Months.TryGetValue(month, out var monthly))
        {
            monthly = new MonthlyStats();
            Months.Add(month, monthly);
        }

        if (outcome == Outcome.Down)
        {
            Wins++;
            cumulativePnl += 1.0;
            currentLossStreak = 0;
            monthly.AddWin();
        }
        else if (outcome == Outcome.Up)
        {
            Losses++;
            cumulativePnl -= 1.0;
            currentLossStreak++;
            LongestLossStreak = Math.Max(LongestLossStreak, currentLossStreak);
            monthly.AddLoss();
        }
        else
        {
            Ties++;
            currentLossStreak = 0;
            monthly.AddTie();
        }

        peakPnl = Math.Max(peakPnl, cumulativePnl);
        MaxDrawdownUnits = Math.Max(MaxDrawdownUnits, peakPnl - cumulativePnl);
    }

    public double TradesPerDay(double days)
    {
        return days <= 0.0 ? 0.0 : Entries / days;
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
    public double RoiPct => Wins + Losses + Ties == 0 ? 0.0 : 100.0 * PnlUnits / (Wins + Losses + Ties);

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
