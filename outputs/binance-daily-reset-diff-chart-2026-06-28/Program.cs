using System.Globalization;
using System.Text;

internal static class Program
{
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
        var points = BuildDailyResetPoints(sourceRows);
        var dailySummaries = BuildDailySummaries(points);
        var assetSummaries = BuildAssetSummaries(points, dailySummaries, windowStartUtc, windowEndUtc);

        var timeSeriesPath = Path.Combine(outputDirectory, "binance-daily-reset-diff-timeseries.csv");
        var dailySummaryPath = Path.Combine(outputDirectory, "binance-daily-reset-diff-daily-summary.csv");
        var assetSummaryPath = Path.Combine(outputDirectory, "binance-daily-reset-diff-asset-summary.csv");
        var chartPath = Path.Combine(outputDirectory, "binance-daily-reset-diff-chart.svg");

        await File.WriteAllTextAsync(timeSeriesPath, BuildTimeSeriesCsv(points), Encoding.UTF8);
        await File.WriteAllTextAsync(dailySummaryPath, BuildDailySummaryCsv(dailySummaries), Encoding.UTF8);
        await File.WriteAllTextAsync(assetSummaryPath, BuildAssetSummaryCsv(assetSummaries), Encoding.UTF8);
        await File.WriteAllTextAsync(chartPath, BuildSvg(assetSummaries, windowStartUtc, windowEndUtc), Encoding.UTF8);

        Console.WriteLine($"analysis_started_utc={DateTimeOffset.UtcNow:O}");
        Console.WriteLine($"input_csv={inputPath}");
        Console.WriteLine($"window_start_utc={windowStartUtc:O}");
        Console.WriteLine($"window_end_utc={windowEndUtc:O}");
        foreach (var summary in assetSummaries)
        {
            Console.WriteLine(
                "summary=" +
                $"asset={summary.AssetSymbol};" +
                $"symbol={summary.BinanceSymbol};" +
                $"points={summary.Points.Count.ToString(CultureInfo.InvariantCulture)};" +
                $"days={summary.DailySummaries.Count.ToString(CultureInfo.InvariantCulture)};" +
                $"global_min_diff={summary.MinPoint?.DailyDiff.ToString(CultureInfo.InvariantCulture) ?? ""};" +
                $"global_min_utc={summary.MinPoint?.OpenTimeUtc.ToString("O", CultureInfo.InvariantCulture) ?? ""};" +
                $"global_max_diff={summary.MaxPoint?.DailyDiff.ToString(CultureInfo.InvariantCulture) ?? ""};" +
                $"global_max_utc={summary.MaxPoint?.OpenTimeUtc.ToString("O", CultureInfo.InvariantCulture) ?? ""};" +
                $"max_intraday_range={summary.MaxRangeDay?.IntradayRange.ToString(CultureInfo.InvariantCulture) ?? ""};" +
                $"max_range_day={summary.MaxRangeDay?.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""};" +
                $"max_range_min={summary.MaxRangeDay?.MinDiff.ToString(CultureInfo.InvariantCulture) ?? ""};" +
                $"max_range_max={summary.MaxRangeDay?.MaxDiff.ToString(CultureInfo.InvariantCulture) ?? ""}");
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

    private static List<DailyResetPoint> BuildDailyResetPoints(IReadOnlyList<SourceRow> rows)
    {
        var points = new List<DailyResetPoint>(rows.Count);
        foreach (var assetGroup in rows.GroupBy(row => row.AssetSymbol, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var dayGroup in assetGroup
                .OrderBy(row => row.OpenTimeUtc)
                .GroupBy(row => row.OpenTimeUtc.Date))
            {
                var upCount = 0;
                var downCount = 0;
                var flatCount = 0;
                foreach (var row in dayGroup.OrderBy(row => row.OpenTimeUtc))
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

                    points.Add(new DailyResetPoint(
                        row.AssetSymbol,
                        row.BinanceSymbol,
                        dayGroup.Key,
                        row.OpenTimeUtc,
                        row.CloseTimeUtc,
                        row.OpenPrice,
                        row.ClosePrice,
                        row.Direction,
                        upCount,
                        downCount,
                        flatCount,
                        upCount - downCount,
                        row.Volume,
                        row.QuoteVolume,
                        row.TradeCount));
                }
            }
        }

        return points
            .OrderBy(point => point.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.OpenTimeUtc)
            .ToList();
    }

    private static List<DailySummary> BuildDailySummaries(IReadOnlyList<DailyResetPoint> points)
    {
        var summaries = new List<DailySummary>();
        foreach (var group in points.GroupBy(point => (point.AssetSymbol, point.DayUtc)))
        {
            var ordered = group.OrderBy(point => point.OpenTimeUtc).ToList();
            var minPoint = ordered
                .OrderBy(point => point.DailyDiff)
                .ThenBy(point => point.OpenTimeUtc)
                .First();
            var maxPoint = ordered
                .OrderByDescending(point => point.DailyDiff)
                .ThenBy(point => point.OpenTimeUtc)
                .First();
            var lastPoint = ordered[^1];
            summaries.Add(new DailySummary(
                group.Key.AssetSymbol,
                lastPoint.BinanceSymbol,
                group.Key.DayUtc,
                ordered[0].OpenTimeUtc,
                lastPoint.OpenTimeUtc,
                ordered.Count,
                lastPoint.DailyUpCount,
                lastPoint.DailyDownCount,
                lastPoint.DailyFlatCount,
                lastPoint.DailyDiff,
                minPoint.DailyDiff,
                minPoint.OpenTimeUtc,
                maxPoint.DailyDiff,
                maxPoint.OpenTimeUtc,
                maxPoint.DailyDiff - minPoint.DailyDiff));
        }

        return summaries
            .OrderBy(summary => summary.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.DayUtc)
            .ToList();
    }

    private static List<AssetSummary> BuildAssetSummaries(
        IReadOnlyList<DailyResetPoint> points,
        IReadOnlyList<DailySummary> dailySummaries,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        return Assets.Select(asset =>
        {
            var assetPoints = points
                .Where(point => string.Equals(point.AssetSymbol, asset, StringComparison.OrdinalIgnoreCase))
                .OrderBy(point => point.OpenTimeUtc)
                .ToList();
            var assetDaily = dailySummaries
                .Where(summary => string.Equals(summary.AssetSymbol, asset, StringComparison.OrdinalIgnoreCase))
                .OrderBy(summary => summary.DayUtc)
                .ToList();
            var minPoint = assetPoints
                .OrderBy(point => point.DailyDiff)
                .ThenBy(point => point.OpenTimeUtc)
                .FirstOrDefault();
            var maxPoint = assetPoints
                .OrderByDescending(point => point.DailyDiff)
                .ThenBy(point => point.OpenTimeUtc)
                .FirstOrDefault();
            var maxRangeDay = assetDaily
                .OrderByDescending(summary => summary.IntradayRange)
                .ThenBy(summary => summary.DayUtc)
                .FirstOrDefault();
            return new AssetSummary(
                asset,
                assetPoints.FirstOrDefault()?.BinanceSymbol ?? asset + "USDT",
                Colors.GetValueOrDefault(asset, "#111827"),
                windowStartUtc,
                windowEndUtc,
                assetPoints,
                assetDaily,
                minPoint,
                maxPoint,
                maxRangeDay);
        }).ToList();
    }

    private static string BuildTimeSeriesCsv(IReadOnlyList<DailyResetPoint> points)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset_symbol,binance_symbol,day_utc,open_time_utc,close_time_utc,open_price,close_price,direction,daily_up_count,daily_down_count,daily_flat_count,daily_diff,volume,quote_volume,trade_count");
        foreach (var point in points)
        {
            builder.AppendCsv(point.AssetSymbol);
            builder.AppendCsv(point.BinanceSymbol);
            builder.AppendCsv(point.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            builder.AppendCsv(point.OpenTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(point.CloseTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(point.OpenPrice.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.ClosePrice.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.Direction);
            builder.AppendCsv(point.DailyUpCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.DailyDownCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.DailyFlatCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.DailyDiff.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.Volume.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.QuoteVolume.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.TradeCount.ToString(CultureInfo.InvariantCulture), last: true);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildDailySummaryCsv(IReadOnlyList<DailySummary> summaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset_symbol,binance_symbol,day_utc,first_open_time_utc,last_open_time_utc,points,up_count,down_count,flat_count,close_diff,min_diff,min_open_time_utc,max_diff,max_open_time_utc,intraday_range");
        foreach (var summary in summaries)
        {
            builder.AppendCsv(summary.AssetSymbol);
            builder.AppendCsv(summary.BinanceSymbol);
            builder.AppendCsv(summary.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.FirstOpenTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.LastOpenTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.Points.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.UpCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.DownCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.FlatCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.CloseDiff.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.MinDiff.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.MinOpenTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.MaxDiff.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.MaxOpenTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.IntradayRange.ToString(CultureInfo.InvariantCulture), last: true);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildAssetSummaryCsv(IReadOnlyList<AssetSummary> summaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset_symbol,binance_symbol,window_start_utc,window_end_utc,points,days,global_min_diff,global_min_open_time_utc,global_max_diff,global_max_open_time_utc,max_intraday_range,max_intraday_range_day_utc,max_range_day_min_diff,max_range_day_max_diff");
        foreach (var summary in summaries)
        {
            builder.AppendCsv(summary.AssetSymbol);
            builder.AppendCsv(summary.BinanceSymbol);
            builder.AppendCsv(summary.WindowStartUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.WindowEndUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.Points.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.DailySummaries.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.MinPoint?.DailyDiff.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MinPoint?.OpenTimeUtc.ToString("O", CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MaxPoint?.DailyDiff.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MaxPoint?.OpenTimeUtc.ToString("O", CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MaxRangeDay?.IntradayRange.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MaxRangeDay?.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MaxRangeDay?.MinDiff.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MaxRangeDay?.MaxDiff.ToString(CultureInfo.InvariantCulture) ?? "", last: true);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildSvg(
        IReadOnlyList<AssetSummary> summaries,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        const int width = 1600;
        const int panelHeight = 320;
        const int top = 92;
        const int left = 96;
        const int right = 44;
        const int chartWidth = width - left - right;
        const int chartHeight = 220;
        var height = top + (panelHeight * summaries.Count) + 80;
        var totalSeconds = Math.Max(1, (windowEndUtc - windowStartUtc).TotalSeconds);

        var builder = new StringBuilder();
        builder.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">""");
        builder.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
        builder.AppendLine("<style>");
        builder.AppendLine("text{font-family:Segoe UI,Arial,sans-serif;fill:#111827}.title{font-size:28px;font-weight:700}.subtitle{font-size:15px;fill:#4b5563}.axis{font-size:12px;fill:#6b7280}.asset{font-size:20px;font-weight:700}.summary{font-size:14px;fill:#374151}.label{font-size:13px;font-weight:600}.line{fill:none;stroke-width:1.5}.grid{stroke:#e5e7eb;stroke-width:1}.axisline{stroke:#9ca3af;stroke-width:1}.dayline{stroke:#f3f4f6;stroke-width:.7}.min{fill:#dc2626}.max{fill:#16a34a}");
        builder.AppendLine("</style>");
        builder.AppendLine("<text x=\"96\" y=\"42\" class=\"title\">Binance 5m daily-reset UpCount - DownCount</text>");
        builder.AppendLine($"""<text x="96" y="68" class="subtitle">Window UTC: {EscapeXml(windowStartUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))} - {EscapeXml(windowEndUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}; counters reset at 00:00 UTC each day</text>""");

        var monthTicks = BuildMonthTicks(windowStartUtc, windowEndUtc);
        var dayTicks = BuildDayTicks(windowStartUtc, windowEndUtc);
        foreach (var (summary, panelIndex) in summaries.Select((summary, index) => (summary, index)))
        {
            var yTop = top + (panelIndex * panelHeight);
            var plotTop = yTop + 48;
            var plotBottom = plotTop + chartHeight;
            var yValues = summary.Points.Select(point => point.DailyDiff).Concat([0]).ToArray();
            var yMin = yValues.Min();
            var yMax = yValues.Max();
            if (yMin == yMax)
            {
                yMin--;
                yMax++;
            }

            var padding = Math.Max(3, (int)Math.Ceiling((yMax - yMin) * 0.08));
            yMin -= padding;
            yMax += padding;
            var yRange = Math.Max(1, yMax - yMin);

            double X(DateTimeOffset timestamp) =>
                left + ((timestamp - windowStartUtc).TotalSeconds / totalSeconds * chartWidth);
            double Y(int value) =>
                plotBottom - ((value - yMin) / (double)yRange * chartHeight);

            builder.AppendLine($"""<text x="{left}" y="{yTop + 24}" class="asset">{summary.AssetSymbol}</text>""");
            builder.AppendLine($"""<text x="{left + 64}" y="{yTop + 24}" class="summary">symbol={summary.BinanceSymbol}; days={summary.DailySummaries.Count}; min={summary.MinPoint?.DailyDiff.ToString(CultureInfo.InvariantCulture) ?? "-"}; max={summary.MaxPoint?.DailyDiff.ToString(CultureInfo.InvariantCulture) ?? "-"}; max range={summary.MaxRangeDay?.IntradayRange.ToString(CultureInfo.InvariantCulture) ?? "-"} on {summary.MaxRangeDay?.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-"}</text>""");
            builder.AppendLine($"""<line x1="{left}" y1="{plotBottom:F1}" x2="{left + chartWidth}" y2="{plotBottom:F1}" class="axisline"/>""");
            builder.AppendLine($"""<line x1="{left}" y1="{plotTop:F1}" x2="{left}" y2="{plotBottom:F1}" class="axisline"/>""");

            foreach (var tick in BuildYTicks(yMin, yMax))
            {
                var y = Y(tick);
                builder.AppendLine($"""<line x1="{left}" y1="{y:F1}" x2="{left + chartWidth}" y2="{y:F1}" class="grid"/>""");
                builder.AppendLine($"""<text x="{left - 12}" y="{y + 4:F1}" text-anchor="end" class="axis">{tick.ToString(CultureInfo.InvariantCulture)}</text>""");
            }

            foreach (var tick in dayTicks)
            {
                var x = X(tick);
                builder.AppendLine($"""<line x1="{x:F1}" y1="{plotTop}" x2="{x:F1}" y2="{plotBottom}" class="dayline"/>""");
            }

            foreach (var tick in monthTicks)
            {
                var x = X(tick);
                builder.AppendLine($"""<line x1="{x:F1}" y1="{plotTop}" x2="{x:F1}" y2="{plotBottom}" class="grid"/>""");
                builder.AppendLine($"""<text x="{x:F1}" y="{plotBottom + 22}" text-anchor="middle" class="axis">{EscapeXml(tick.ToString("MMM yyyy", CultureInfo.InvariantCulture))}</text>""");
            }

            foreach (var dayGroup in summary.Points.GroupBy(point => point.DayUtc))
            {
                var dayPoints = dayGroup.OrderBy(point => point.OpenTimeUtc).ToList();
                if (dayPoints.Count > 0)
                {
                    builder.AppendLine($"""<path d="{BuildPath(dayPoints, X, Y)}" class="line" stroke="{summary.Color}"/>""");
                }
            }

            DrawMarker(builder, summary.MinPoint, "min", X, Y, valueAnchorAbove: false, width);
            DrawMarker(builder, summary.MaxPoint, "max", X, Y, valueAnchorAbove: true, width);
        }

        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static string BuildPath(
        IReadOnlyList<DailyResetPoint> points,
        Func<DateTimeOffset, double> x,
        Func<int, double> y)
    {
        var builder = new StringBuilder(points.Count * 16);
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            builder.Append(index == 0 ? 'M' : 'L');
            builder.Append(x(point.OpenTimeUtc).ToString("F1", CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(y(point.DailyDiff).ToString("F1", CultureInfo.InvariantCulture));
            builder.Append(' ');
        }

        return builder.ToString();
    }

    private static void DrawMarker(
        StringBuilder builder,
        DailyResetPoint? point,
        string markerClass,
        Func<DateTimeOffset, double> x,
        Func<int, double> y,
        bool valueAnchorAbove,
        int svgWidth)
    {
        if (point is null)
        {
            return;
        }

        var markerX = x(point.OpenTimeUtc);
        var markerY = y(point.DailyDiff);
        var labelY = valueAnchorAbove ? markerY - 12 : markerY + 24;
        var label = markerClass + " " + point.DailyDiff.ToString(CultureInfo.InvariantCulture) +
            " @ " + point.OpenTimeUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var anchorAtEnd = markerX > svgWidth - 360;
        var labelX = anchorAtEnd ? markerX - 8 : markerX + 8;
        var anchor = anchorAtEnd ? "end" : "start";
        builder.AppendLine($"""<circle cx="{markerX:F1}" cy="{markerY:F1}" r="5" class="{markerClass}"/>""");
        builder.AppendLine($"""<text x="{labelX:F1}" y="{labelY:F1}" text-anchor="{anchor}" class="label {markerClass}">{EscapeXml(label)}</text>""");
    }

    private static IReadOnlyList<DateTimeOffset> BuildMonthTicks(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var first = new DateTimeOffset(startUtc.Year, startUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        if (first < startUtc)
        {
            first = first.AddMonths(1);
        }

        var ticks = new List<DateTimeOffset>();
        for (var tick = first; tick <= endUtc; tick = tick.AddMonths(1))
        {
            ticks.Add(tick);
        }

        return ticks;
    }

    private static IReadOnlyList<DateTimeOffset> BuildDayTicks(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var first = new DateTimeOffset(startUtc.Year, startUtc.Month, startUtc.Day, 0, 0, 0, TimeSpan.Zero);
        if (first < startUtc)
        {
            first = first.AddDays(1);
        }

        var ticks = new List<DateTimeOffset>();
        for (var tick = first; tick <= endUtc; tick = tick.AddDays(1))
        {
            ticks.Add(tick);
        }

        return ticks;
    }

    private static IReadOnlyList<int> BuildYTicks(int yMin, int yMax)
    {
        var range = Math.Max(1, yMax - yMin);
        var rawStep = range / 4.0;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var normalized = rawStep / magnitude;
        var step = normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 5 => 5,
            _ => 10,
        } * magnitude;
        var intStep = Math.Max(1, (int)Math.Ceiling(step));
        var start = (int)Math.Floor(yMin / (double)intStep) * intStep;
        var ticks = new List<int>();
        for (var value = start; value <= yMax; value += intStep)
        {
            if (value >= yMin)
            {
                ticks.Add(value);
            }
        }

        return ticks;
    }

    private static string ResolveOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BinanceDailyResetDiffChart.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static string ResolveDefaultInputPath(string outputDirectory)
    {
        var directory = new DirectoryInfo(outputDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "outputs",
                "binance-diff-time-chart-2026-06-28",
                "binance-diff-timeseries.csv");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(Path.Combine(
            outputDirectory,
            "..",
            "binance-diff-time-chart-2026-06-28",
            "binance-diff-timeseries.csv"));
    }

    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static void AppendCsv(this StringBuilder builder, string value, bool last = false)
    {
        var needsQuotes = value.Contains(',', StringComparison.Ordinal) ||
            value.Contains('"', StringComparison.Ordinal) ||
            value.Contains('\r', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal);
        if (needsQuotes)
        {
            builder.Append('"');
            builder.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            builder.Append('"');
        }
        else
        {
            builder.Append(value);
        }

        if (!last)
        {
            builder.Append(',');
        }
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

    private sealed record DailyResetPoint(
        string AssetSymbol,
        string BinanceSymbol,
        DateTime DayUtc,
        DateTimeOffset OpenTimeUtc,
        DateTimeOffset CloseTimeUtc,
        decimal OpenPrice,
        decimal ClosePrice,
        string Direction,
        int DailyUpCount,
        int DailyDownCount,
        int DailyFlatCount,
        int DailyDiff,
        decimal Volume,
        decimal QuoteVolume,
        int TradeCount);

    private sealed record DailySummary(
        string AssetSymbol,
        string BinanceSymbol,
        DateTime DayUtc,
        DateTimeOffset FirstOpenTimeUtc,
        DateTimeOffset LastOpenTimeUtc,
        int Points,
        int UpCount,
        int DownCount,
        int FlatCount,
        int CloseDiff,
        int MinDiff,
        DateTimeOffset MinOpenTimeUtc,
        int MaxDiff,
        DateTimeOffset MaxOpenTimeUtc,
        int IntradayRange);

    private sealed record AssetSummary(
        string AssetSymbol,
        string BinanceSymbol,
        string Color,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndUtc,
        IReadOnlyList<DailyResetPoint> Points,
        IReadOnlyList<DailySummary> DailySummaries,
        DailyResetPoint? MinPoint,
        DailyResetPoint? MaxPoint,
        DailySummary? MaxRangeDay);
}
