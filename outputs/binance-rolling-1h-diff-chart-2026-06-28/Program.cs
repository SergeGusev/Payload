using System.Globalization;
using System.Text;

internal static class Program
{
    private const int WindowCandles = 12;
    private static readonly TimeSpan CandleInterval = TimeSpan.FromMinutes(5);
    private static readonly string[] Assets = ["BTC", "ETH", "SOL"];
    private static readonly Dictionary<string, string> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = "#2563eb",
        ["ETH"] = "#059669",
        ["SOL"] = "#d97706",
    };

    private static async Task<int> Main(string[] args)
    {
        var outputDirectory = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDirectory);
        var inputPath = args.Length > 0
            ? Path.GetFullPath(args[0])
            : ResolveDefaultInputPath(outputDirectory);

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine("Input CSV not found: " + inputPath);
            return 2;
        }

        var sourceRows = await LoadSourceRowsAsync(inputPath);
        var windowStartUtc = sourceRows.Min(row => row.OpenTimeUtc);
        var windowEndUtc = sourceRows.Max(row => row.OpenTimeUtc);
        var points = BuildRollingWindowPoints(sourceRows);
        var dailySummaries = BuildDailySummaries(points);
        var assetSummaries = BuildAssetSummaries(points, dailySummaries, windowStartUtc, windowEndUtc);

        var timeSeriesPath = Path.Combine(outputDirectory, "binance-rolling-1h-diff-timeseries.csv");
        var dailySummaryPath = Path.Combine(outputDirectory, "binance-rolling-1h-diff-daily-summary.csv");
        var assetSummaryPath = Path.Combine(outputDirectory, "binance-rolling-1h-diff-asset-summary.csv");
        var chartPath = Path.Combine(outputDirectory, "binance-rolling-1h-diff-chart.svg");

        await File.WriteAllTextAsync(timeSeriesPath, BuildTimeSeriesCsv(points), Encoding.UTF8);
        await File.WriteAllTextAsync(dailySummaryPath, BuildDailySummaryCsv(dailySummaries), Encoding.UTF8);
        await File.WriteAllTextAsync(assetSummaryPath, BuildAssetSummaryCsv(assetSummaries), Encoding.UTF8);
        await File.WriteAllTextAsync(chartPath, BuildSvg(assetSummaries, points), Encoding.UTF8);

        Console.WriteLine($"analysis_started_utc={DateTimeOffset.UtcNow:O}");
        Console.WriteLine($"input_csv={inputPath}");
        Console.WriteLine($"window_start_utc={windowStartUtc:O}");
        Console.WriteLine($"window_end_utc={windowEndUtc:O}");
        Console.WriteLine($"rolling_window_candles={WindowCandles.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"rolling_window_minutes={(WindowCandles * 5).ToString(CultureInfo.InvariantCulture)}");
        foreach (var summary in assetSummaries)
        {
            Console.WriteLine(
                "summary=" +
                $"asset={summary.AssetSymbol};" +
                $"symbol={summary.BinanceSymbol};" +
                $"points={summary.Points.Count.ToString(CultureInfo.InvariantCulture)};" +
                $"days={summary.DailySummaries.Count.ToString(CultureInfo.InvariantCulture)};" +
                $"global_min_diff_1h={summary.MinPoint?.Diff1h.ToString(CultureInfo.InvariantCulture) ?? ""};" +
                $"global_min_window_start_utc={summary.MinPoint?.WindowStartUtc.ToString("O", CultureInfo.InvariantCulture) ?? ""};" +
                $"global_min_window_end_utc={summary.MinPoint?.WindowEndUtc.ToString("O", CultureInfo.InvariantCulture) ?? ""};" +
                $"global_max_diff_1h={summary.MaxPoint?.Diff1h.ToString(CultureInfo.InvariantCulture) ?? ""};" +
                $"global_max_window_start_utc={summary.MaxPoint?.WindowStartUtc.ToString("O", CultureInfo.InvariantCulture) ?? ""};" +
                $"global_max_window_end_utc={summary.MaxPoint?.WindowEndUtc.ToString("O", CultureInfo.InvariantCulture) ?? ""};" +
                $"max_daily_range={summary.MaxRangeDay?.DailyRange.ToString(CultureInfo.InvariantCulture) ?? ""};" +
                $"max_range_day={summary.MaxRangeDay?.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""};" +
                $"max_range_day_min={summary.MaxRangeDay?.MinDiff1h.ToString(CultureInfo.InvariantCulture) ?? ""};" +
                $"max_range_day_max={summary.MaxRangeDay?.MaxDiff1h.ToString(CultureInfo.InvariantCulture) ?? ""}");
        }

        Console.WriteLine($"timeseries_csv={timeSeriesPath}");
        Console.WriteLine($"daily_summary_csv={dailySummaryPath}");
        Console.WriteLine($"asset_summary_csv={assetSummaryPath}");
        Console.WriteLine($"chart_svg={chartPath}");
        Console.WriteLine($"analysis_finished_utc={DateTimeOffset.UtcNow:O}");
        return 0;
    }

    private static async Task<List<SourceRow>> LoadSourceRowsAsync(string inputPath)
    {
        var lines = await File.ReadAllLinesAsync(inputPath, Encoding.UTF8);
        if (lines.Length <= 1)
        {
            return [];
        }

        var rows = new List<SourceRow>(lines.Length - 1);
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = SplitCsvLine(line);
            if (columns.Count < 14)
            {
                continue;
            }

            rows.Add(new SourceRow(
                columns[0],
                columns[1],
                DateTimeOffset.Parse(columns[2], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime(),
                DateTimeOffset.Parse(columns[3], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime(),
                decimal.Parse(columns[4], CultureInfo.InvariantCulture),
                decimal.Parse(columns[5], CultureInfo.InvariantCulture),
                columns[6],
                decimal.Parse(columns[11], CultureInfo.InvariantCulture),
                decimal.Parse(columns[12], CultureInfo.InvariantCulture),
                int.Parse(columns[13], CultureInfo.InvariantCulture)));
        }

        return rows
            .Where(row => Assets.Contains(row.AssetSymbol, StringComparer.OrdinalIgnoreCase))
            .OrderBy(row => row.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.OpenTimeUtc)
            .ToList();
    }

    private static List<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(ch);
            }
        }

        values.Add(builder.ToString());
        return values;
    }

    private static List<RollingWindowPoint> BuildRollingWindowPoints(IReadOnlyList<SourceRow> rows)
    {
        var points = new List<RollingWindowPoint>(rows.Count);
        foreach (var assetGroup in rows.GroupBy(row => row.AssetSymbol, StringComparer.OrdinalIgnoreCase))
        {
            var window = new Queue<SourceRow>(WindowCandles);
            var upCount = 0;
            var downCount = 0;
            var flatCount = 0;

            foreach (var row in assetGroup.OrderBy(row => row.OpenTimeUtc))
            {
                window.Enqueue(row);
                AddDirection(row, ref upCount, ref downCount, ref flatCount);

                while (window.Count > WindowCandles)
                {
                    RemoveDirection(window.Dequeue(), ref upCount, ref downCount, ref flatCount);
                }

                while (window.Count > 0 && row.OpenTimeUtc - window.Peek().OpenTimeUtc >= WindowCandles * CandleInterval)
                {
                    RemoveDirection(window.Dequeue(), ref upCount, ref downCount, ref flatCount);
                }

                if (window.Count != WindowCandles)
                {
                    continue;
                }

                var first = window.Peek();
                points.Add(new RollingWindowPoint(
                    row.AssetSymbol,
                    row.BinanceSymbol,
                    first.OpenTimeUtc,
                    row.CloseTimeUtc,
                    row.OpenTimeUtc,
                    row.CloseTimeUtc,
                    WindowCandles,
                    upCount,
                    downCount,
                    flatCount,
                    upCount - downCount,
                    row.Volume,
                    row.QuoteVolume,
                    row.TradeCount));
            }
        }

        return points
            .OrderBy(point => point.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.WindowEndUtc)
            .ToList();
    }

    private static void AddDirection(SourceRow row, ref int upCount, ref int downCount, ref int flatCount)
    {
        if (string.Equals(row.Direction, "Up", StringComparison.OrdinalIgnoreCase))
        {
            upCount++;
        }
        else if (string.Equals(row.Direction, "Down", StringComparison.OrdinalIgnoreCase))
        {
            downCount++;
        }
        else
        {
            flatCount++;
        }
    }

    private static void RemoveDirection(SourceRow row, ref int upCount, ref int downCount, ref int flatCount)
    {
        if (string.Equals(row.Direction, "Up", StringComparison.OrdinalIgnoreCase))
        {
            upCount--;
        }
        else if (string.Equals(row.Direction, "Down", StringComparison.OrdinalIgnoreCase))
        {
            downCount--;
        }
        else
        {
            flatCount--;
        }
    }

    private static List<DailySummary> BuildDailySummaries(IReadOnlyList<RollingWindowPoint> points)
    {
        var summaries = new List<DailySummary>();
        foreach (var group in points.GroupBy(point => (point.AssetSymbol, DayUtc: point.WindowEndUtc.Date)))
        {
            var ordered = group.OrderBy(point => point.WindowEndUtc).ToList();
            var minPoint = ordered
                .OrderBy(point => point.Diff1h)
                .ThenBy(point => point.WindowEndUtc)
                .First();
            var maxPoint = ordered
                .OrderByDescending(point => point.Diff1h)
                .ThenBy(point => point.WindowEndUtc)
                .First();

            summaries.Add(new DailySummary(
                minPoint.AssetSymbol,
                minPoint.BinanceSymbol,
                group.Key.DayUtc,
                ordered.Count,
                minPoint.Diff1h,
                minPoint.WindowStartUtc,
                minPoint.WindowEndUtc,
                maxPoint.Diff1h,
                maxPoint.WindowStartUtc,
                maxPoint.WindowEndUtc,
                maxPoint.Diff1h - minPoint.Diff1h));
        }

        return summaries
            .OrderBy(summary => summary.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.DayUtc)
            .ToList();
    }

    private static List<AssetSummary> BuildAssetSummaries(
        IReadOnlyList<RollingWindowPoint> points,
        IReadOnlyList<DailySummary> dailySummaries,
        DateTimeOffset sourceWindowStartUtc,
        DateTimeOffset sourceWindowEndUtc)
    {
        var summaries = new List<AssetSummary>();
        foreach (var asset in Assets)
        {
            var assetPoints = points
                .Where(point => string.Equals(point.AssetSymbol, asset, StringComparison.OrdinalIgnoreCase))
                .OrderBy(point => point.WindowEndUtc)
                .ToList();
            if (assetPoints.Count == 0)
            {
                continue;
            }

            var minPoint = assetPoints
                .OrderBy(point => point.Diff1h)
                .ThenBy(point => point.WindowEndUtc)
                .First();
            var maxPoint = assetPoints
                .OrderByDescending(point => point.Diff1h)
                .ThenBy(point => point.WindowEndUtc)
                .First();
            var assetDailySummaries = dailySummaries
                .Where(summary => string.Equals(summary.AssetSymbol, asset, StringComparison.OrdinalIgnoreCase))
                .OrderBy(summary => summary.DayUtc)
                .ToList();
            var maxRangeDay = assetDailySummaries
                .OrderByDescending(summary => summary.DailyRange)
                .ThenBy(summary => summary.DayUtc)
                .FirstOrDefault();

            summaries.Add(new AssetSummary(
                asset,
                assetPoints[0].BinanceSymbol,
                sourceWindowStartUtc,
                sourceWindowEndUtc,
                assetPoints,
                assetDailySummaries,
                minPoint,
                maxPoint,
                maxRangeDay));
        }

        return summaries;
    }

    private static string BuildTimeSeriesCsv(IReadOnlyList<RollingWindowPoint> points)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset_symbol,binance_symbol,window_start_utc,window_end_utc,candle_open_time_utc,candle_close_time_utc,window_candles,up_count_1h,down_count_1h,flat_count_1h,diff_1h,volume,quote_volume,trade_count");
        foreach (var point in points)
        {
            builder
                .Append(EscapeCsv(point.AssetSymbol)).Append(',')
                .Append(EscapeCsv(point.BinanceSymbol)).Append(',')
                .Append(point.WindowStartUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.WindowEndUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.CandleOpenTimeUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.CandleCloseTimeUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.WindowCandles.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(point.UpCount1h.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(point.DownCount1h.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(point.FlatCount1h.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(point.Diff1h.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(point.Volume.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(point.QuoteVolume.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(point.TradeCount.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildDailySummaryCsv(IReadOnlyList<DailySummary> summaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset_symbol,binance_symbol,day_utc,points,min_diff_1h,min_window_start_utc,min_window_end_utc,max_diff_1h,max_window_start_utc,max_window_end_utc,daily_range");
        foreach (var summary in summaries)
        {
            builder
                .Append(EscapeCsv(summary.AssetSymbol)).Append(',')
                .Append(EscapeCsv(summary.BinanceSymbol)).Append(',')
                .Append(summary.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.Points.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MinDiff1h.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MinWindowStartUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MinWindowEndUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MaxDiff1h.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MaxWindowStartUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MaxWindowEndUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.DailyRange.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildAssetSummaryCsv(IReadOnlyList<AssetSummary> summaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset_symbol,binance_symbol,source_window_start_utc,source_window_end_utc,rolling_window_candles,rolling_window_minutes,points,days,global_min_diff_1h,global_min_window_start_utc,global_min_window_end_utc,global_max_diff_1h,global_max_window_start_utc,global_max_window_end_utc,max_daily_range,max_daily_range_day_utc,max_range_day_min_diff_1h,max_range_day_max_diff_1h");
        foreach (var summary in summaries)
        {
            builder
                .Append(EscapeCsv(summary.AssetSymbol)).Append(',')
                .Append(EscapeCsv(summary.BinanceSymbol)).Append(',')
                .Append(summary.SourceWindowStartUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.SourceWindowEndUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(WindowCandles.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append((WindowCandles * 5).ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.Points.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.DailySummaries.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MinPoint.Diff1h.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MinPoint.WindowStartUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MinPoint.WindowEndUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MaxPoint.Diff1h.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MaxPoint.WindowStartUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MaxPoint.WindowEndUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(summary.MaxRangeDay?.DailyRange.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(summary.MaxRangeDay?.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(summary.MaxRangeDay?.MinDiff1h.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(summary.MaxRangeDay?.MaxDiff1h.ToString(CultureInfo.InvariantCulture) ?? "")
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildSvg(IReadOnlyList<AssetSummary> summaries, IReadOnlyList<RollingWindowPoint> points)
    {
        const int width = 1800;
        const int height = 1000;
        const int marginLeft = 92;
        const int marginRight = 260;
        const int marginTop = 76;
        const int marginBottom = 96;

        var plotLeft = marginLeft;
        var plotTop = marginTop;
        var plotWidth = width - marginLeft - marginRight;
        var plotHeight = height - marginTop - marginBottom;
        var minTime = points.Min(point => point.WindowEndUtc);
        var maxTime = points.Max(point => point.WindowEndUtc);
        var minDiff = Math.Min(-12, points.Min(point => point.Diff1h));
        var maxDiff = Math.Max(12, points.Max(point => point.Diff1h));
        var yStep = 3;
        var spanTicks = Math.Max(1, (maxTime - minTime).Ticks);

        double X(DateTimeOffset value) => plotLeft + ((double)(value - minTime).Ticks / spanTicks * plotWidth);
        double Y(int value) => plotTop + ((double)(maxDiff - value) / (maxDiff - minDiff) * plotHeight);

        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-labelledby="title desc">""");
        builder.AppendLine("<title id=\"title\">Binance BTC/ETH/SOL rolling 1h Diff</title>");
        builder.AppendLine("<desc id=\"desc\">Diff is UpCount minus DownCount over the latest twelve 5-minute Binance candles. Global min and max are marked for each asset.</desc>");
        builder.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
        builder.AppendLine("""<text x="92" y="36" font-family="Segoe UI, Arial, sans-serif" font-size="24" font-weight="700" fill="#111827">Binance 5m Rolling 1h Diff: BTC, ETH, SOL</text>""");
        builder.AppendLine($"""<text x="92" y="62" font-family="Segoe UI, Arial, sans-serif" font-size="13" fill="#4b5563">Window: latest {WindowCandles} candles = 1 hour. Full windows only. Period: {minTime:yyyy-MM-dd HH:mm} UTC to {maxTime:yyyy-MM-dd HH:mm} UTC.</text>""");
        builder.AppendLine($"""<rect x="{plotLeft}" y="{plotTop}" width="{plotWidth}" height="{plotHeight}" fill="#ffffff" stroke="#d1d5db" stroke-width="1"/>""");

        for (var value = minDiff; value <= maxDiff; value += yStep)
        {
            var y = Y(value);
            var gridColor = value == 0 ? "#9ca3af" : "#e5e7eb";
            var strokeWidth = value == 0 ? "1.5" : "1";
            builder.AppendLine($"""<line x1="{plotLeft}" y1="{Format(y)}" x2="{plotLeft + plotWidth}" y2="{Format(y)}" stroke="{gridColor}" stroke-width="{strokeWidth}"/>""");
            builder.AppendLine($"""<text x="{plotLeft - 12}" y="{Format(y + 4)}" text-anchor="end" font-family="Segoe UI, Arial, sans-serif" font-size="12" fill="#4b5563">{value.ToString(CultureInfo.InvariantCulture)}</text>""");
        }

        foreach (var tick in BuildMonthTicks(minTime, maxTime))
        {
            var x = X(tick);
            builder.AppendLine($"""<line x1="{Format(x)}" y1="{plotTop}" x2="{Format(x)}" y2="{plotTop + plotHeight}" stroke="#f3f4f6" stroke-width="1"/>""");
            builder.AppendLine($"""<text x="{Format(x)}" y="{plotTop + plotHeight + 24}" text-anchor="middle" font-family="Segoe UI, Arial, sans-serif" font-size="12" fill="#4b5563">{tick:MMM yyyy}</text>""");
        }

        builder.AppendLine($"""<text x="{plotLeft + plotWidth / 2}" y="{height - 24}" text-anchor="middle" font-family="Segoe UI, Arial, sans-serif" font-size="13" fill="#374151">UTC time</text>""");
        builder.AppendLine($"""<text x="24" y="{plotTop + plotHeight / 2}" transform="rotate(-90 24 {plotTop + plotHeight / 2})" text-anchor="middle" font-family="Segoe UI, Arial, sans-serif" font-size="13" fill="#374151">Diff1h = UpCount - DownCount</text>""");

        foreach (var summary in summaries)
        {
            var color = Colors.GetValueOrDefault(summary.AssetSymbol, "#111827");
            var path = BuildPath(summary.Points, X, Y);
            builder.AppendLine($"""<path d="{path}" fill="none" stroke="{color}" stroke-width="1.6" stroke-linejoin="round" stroke-linecap="round"/>""");
        }

        foreach (var summary in summaries)
        {
            var color = Colors.GetValueOrDefault(summary.AssetSymbol, "#111827");
            AddMarker(builder, summary.MinPoint, "min", color, X, Y);
            AddMarker(builder, summary.MaxPoint, "max", color, X, Y);
        }

        var legendX = plotLeft + plotWidth + 34;
        var legendY = plotTop + 8;
        builder.AppendLine($"""<text x="{legendX}" y="{legendY}" font-family="Segoe UI, Arial, sans-serif" font-size="15" font-weight="700" fill="#111827">Summary</text>""");
        legendY += 28;
        foreach (var summary in summaries)
        {
            var color = Colors.GetValueOrDefault(summary.AssetSymbol, "#111827");
            builder.AppendLine($"""<line x1="{legendX}" y1="{legendY - 5}" x2="{legendX + 28}" y2="{legendY - 5}" stroke="{color}" stroke-width="3"/>""");
            builder.AppendLine($"""<text x="{legendX + 38}" y="{legendY}" font-family="Segoe UI, Arial, sans-serif" font-size="13" font-weight="700" fill="#111827">{EscapeXml(summary.AssetSymbol)}</text>""");
            legendY += 18;
            builder.AppendLine($"""<text x="{legendX + 38}" y="{legendY}" font-family="Segoe UI, Arial, sans-serif" font-size="12" fill="#374151">min {summary.MinPoint.Diff1h} ({summary.MinPoint.WindowEndUtc:yyyy-MM-dd HH:mm})</text>""");
            legendY += 16;
            builder.AppendLine($"""<text x="{legendX + 38}" y="{legendY}" font-family="Segoe UI, Arial, sans-serif" font-size="12" fill="#374151">max {summary.MaxPoint.Diff1h} ({summary.MaxPoint.WindowEndUtc:yyyy-MM-dd HH:mm})</text>""");
            legendY += 16;
            if (summary.MaxRangeDay is not null)
            {
                builder.AppendLine($"""<text x="{legendX + 38}" y="{legendY}" font-family="Segoe UI, Arial, sans-serif" font-size="12" fill="#374151">max daily range {summary.MaxRangeDay.DailyRange} ({summary.MaxRangeDay.DayUtc:yyyy-MM-dd})</text>""");
                legendY += 28;
            }
        }

        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static string BuildPath(IReadOnlyList<RollingWindowPoint> points, Func<DateTimeOffset, double> x, Func<int, double> y)
    {
        var builder = new StringBuilder(points.Count * 16);
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            builder.Append(index == 0 ? 'M' : 'L')
                .Append(Format(x(point.WindowEndUtc)))
                .Append(' ')
                .Append(Format(y(point.Diff1h)));
        }

        return builder.ToString();
    }

    private static void AddMarker(
        StringBuilder builder,
        RollingWindowPoint point,
        string label,
        string color,
        Func<DateTimeOffset, double> x,
        Func<int, double> y)
    {
        var px = x(point.WindowEndUtc);
        var py = y(point.Diff1h);
        var labelX = px + 7;
        var labelY = py - 7;
        builder.AppendLine($"""<circle cx="{Format(px)}" cy="{Format(py)}" r="4.5" fill="{color}" stroke="#ffffff" stroke-width="1.5"/>""");
        builder.AppendLine($"""<text x="{Format(labelX)}" y="{Format(labelY)}" font-family="Segoe UI, Arial, sans-serif" font-size="11" fill="{color}">{EscapeXml(point.AssetSymbol)} {EscapeXml(label)} {point.Diff1h}</text>""");
    }

    private static List<DateTimeOffset> BuildMonthTicks(DateTimeOffset minTime, DateTimeOffset maxTime)
    {
        var ticks = new List<DateTimeOffset>();
        var current = new DateTimeOffset(minTime.Year, minTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
        if (current < minTime)
        {
            current = current.AddMonths(1);
        }

        while (current <= maxTime)
        {
            ticks.Add(current);
            current = current.AddMonths(1);
        }

        return ticks;
    }

    private static string ResolveOutputDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BinanceRolling1hDiffChart.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static string ResolveDefaultInputPath(string outputDirectory)
    {
        return Path.GetFullPath(Path.Combine(
            outputDirectory,
            "..",
            "binance-diff-time-chart-2026-06-28",
            "binance-diff-timeseries.csv"));
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private sealed record SourceRow(
        string AssetSymbol,
        string BinanceSymbol,
        DateTimeOffset OpenTimeUtc,
        DateTimeOffset CloseTimeUtc,
        decimal OpenPrice,
        decimal ClosePrice,
        string Direction,
        decimal Volume,
        decimal QuoteVolume,
        int TradeCount);

    private sealed record RollingWindowPoint(
        string AssetSymbol,
        string BinanceSymbol,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndUtc,
        DateTimeOffset CandleOpenTimeUtc,
        DateTimeOffset CandleCloseTimeUtc,
        int WindowCandles,
        int UpCount1h,
        int DownCount1h,
        int FlatCount1h,
        int Diff1h,
        decimal Volume,
        decimal QuoteVolume,
        int TradeCount);

    private sealed record DailySummary(
        string AssetSymbol,
        string BinanceSymbol,
        DateTime DayUtc,
        int Points,
        int MinDiff1h,
        DateTimeOffset MinWindowStartUtc,
        DateTimeOffset MinWindowEndUtc,
        int MaxDiff1h,
        DateTimeOffset MaxWindowStartUtc,
        DateTimeOffset MaxWindowEndUtc,
        int DailyRange);

    private sealed record AssetSummary(
        string AssetSymbol,
        string BinanceSymbol,
        DateTimeOffset SourceWindowStartUtc,
        DateTimeOffset SourceWindowEndUtc,
        IReadOnlyList<RollingWindowPoint> Points,
        IReadOnlyList<DailySummary> DailySummaries,
        RollingWindowPoint MinPoint,
        RollingWindowPoint MaxPoint,
        DailySummary? MaxRangeDay);
}
