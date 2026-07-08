using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const string BaseUrl = "https://api.binance.com";
    private const string Interval = "5m";
    private const int Limit = 1000;
    private static readonly AssetConfig[] Assets =
    [
        new("BTC", "BTCUSDT", "#2563eb"),
        new("ETH", "ETHUSDT", "#059669"),
        new("SOL", "SOLUSDT", "#d97706"),
    ];

    private static async Task<int> Main()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };

        var outputDirectory = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDirectory);

        var serverNowUtc = await GetBinanceServerTimeUtcAsync(httpClient);
        var endUtc = FloorToFiveMinutes(serverNowUtc).AddMinutes(-5);
        var startUtc = FloorToFiveMinutes(endUtc.AddMonths(-6));

        Console.WriteLine($"analysis_started_utc={DateTimeOffset.UtcNow:O}");
        Console.WriteLine($"binance_server_time_utc={serverNowUtc:O}");
        Console.WriteLine($"window_start_utc={startUtc:O}");
        Console.WriteLine($"window_end_utc={endUtc:O}");

        var allPoints = new List<DiffPoint>();
        var summaries = new List<AssetSummary>();
        foreach (var asset in Assets)
        {
            var klines = await FetchKlinesAsync(httpClient, asset, startUtc, endUtc);
            var points = BuildDiffPoints(asset, klines);
            allPoints.AddRange(points);
            var summary = BuildSummary(asset, startUtc, endUtc, points);
            summaries.Add(summary);
            Console.WriteLine(
                "summary=" +
                $"asset={summary.AssetSymbol};" +
                $"symbol={summary.BinanceSymbol};" +
                $"points={summary.Points.Count.ToString(CultureInfo.InvariantCulture)};" +
                $"up={summary.UpCount.ToString(CultureInfo.InvariantCulture)};" +
                $"down={summary.DownCount.ToString(CultureInfo.InvariantCulture)};" +
                $"flat={summary.FlatCount.ToString(CultureInfo.InvariantCulture)};" +
                $"last_diff={summary.LastDiff.ToString(CultureInfo.InvariantCulture)};" +
                $"min_diff={summary.MinPoint?.Diff.ToString(CultureInfo.InvariantCulture) ?? ""};" +
                $"min_utc={summary.MinPoint?.OpenTimeUtc.ToString("O", CultureInfo.InvariantCulture) ?? ""};" +
                $"max_diff={summary.MaxPoint?.Diff.ToString(CultureInfo.InvariantCulture) ?? ""};" +
                $"max_utc={summary.MaxPoint?.OpenTimeUtc.ToString("O", CultureInfo.InvariantCulture) ?? ""};" +
                $"first_utc={summary.FirstOpenTimeUtc?.ToString("O", CultureInfo.InvariantCulture) ?? ""};" +
                $"last_utc={summary.LastOpenTimeUtc?.ToString("O", CultureInfo.InvariantCulture) ?? ""}");

            await Task.Delay(TimeSpan.FromMilliseconds(120));
        }

        var timeSeriesPath = Path.Combine(outputDirectory, "binance-diff-timeseries.csv");
        var summaryPath = Path.Combine(outputDirectory, "binance-diff-summary.csv");
        var chartPath = Path.Combine(outputDirectory, "binance-diff-chart.svg");
        await File.WriteAllTextAsync(timeSeriesPath, BuildTimeSeriesCsv(allPoints), Encoding.UTF8);
        await File.WriteAllTextAsync(summaryPath, BuildSummaryCsv(summaries), Encoding.UTF8);
        await File.WriteAllTextAsync(chartPath, BuildSvg(summaries, startUtc, endUtc), Encoding.UTF8);

        Console.WriteLine($"timeseries_csv={timeSeriesPath}");
        Console.WriteLine($"summary_csv={summaryPath}");
        Console.WriteLine($"chart_svg={chartPath}");
        Console.WriteLine($"analysis_finished_utc={DateTimeOffset.UtcNow:O}");
        return 0;
    }

    private static async Task<DateTimeOffset> GetBinanceServerTimeUtcAsync(HttpClient httpClient)
    {
        using var response = await httpClient.GetAsync("/api/v3/time");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var serverTimeMs = document.RootElement.GetProperty("serverTime").GetInt64();
        return DateTimeOffset.FromUnixTimeMilliseconds(serverTimeMs);
    }

    private static async Task<List<Kline>> FetchKlinesAsync(
        HttpClient httpClient,
        AssetConfig asset,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        var results = new List<Kline>(60_000);
        var cursorUtc = startUtc;
        while (cursorUtc <= endUtc)
        {
            var url =
                "/api/v3/klines" +
                $"?symbol={Uri.EscapeDataString(asset.BinanceSymbol)}" +
                $"&interval={Interval}" +
                $"&startTime={cursorUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}" +
                $"&endTime={endUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}" +
                $"&limit={Limit.ToString(CultureInfo.InvariantCulture)}";
            using var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() == 0)
            {
                break;
            }

            var batch = new List<Kline>(document.RootElement.GetArrayLength());
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 7)
                {
                    continue;
                }

                var openTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(item[0].GetInt64());
                if (openTimeUtc < startUtc || openTimeUtc > endUtc)
                {
                    continue;
                }

                batch.Add(new Kline(
                    asset.AssetSymbol,
                    asset.BinanceSymbol,
                    openTimeUtc,
                    DateTimeOffset.FromUnixTimeMilliseconds(item[6].GetInt64()),
                    decimal.Parse(item[1].GetString() ?? "0", CultureInfo.InvariantCulture),
                    decimal.Parse(item[4].GetString() ?? "0", CultureInfo.InvariantCulture),
                    decimal.Parse(item[5].GetString() ?? "0", CultureInfo.InvariantCulture),
                    decimal.Parse(item[7].GetString() ?? "0", CultureInfo.InvariantCulture),
                    item[8].GetInt32()));
            }

            if (batch.Count == 0)
            {
                break;
            }

            results.AddRange(batch);
            var nextOpenTimeUtc = batch[^1].OpenTimeUtc.AddMinutes(5);
            if (nextOpenTimeUtc <= cursorUtc)
            {
                break;
            }

            cursorUtc = nextOpenTimeUtc;
            await Task.Delay(TimeSpan.FromMilliseconds(120));
        }

        return results
            .GroupBy(kline => kline.OpenTimeUtc)
            .Select(group => group.OrderBy(item => item.OpenTimeUtc).First())
            .OrderBy(kline => kline.OpenTimeUtc)
            .ToList();
    }

    private static List<DiffPoint> BuildDiffPoints(AssetConfig asset, IReadOnlyList<Kline> klines)
    {
        var upCount = 0;
        var downCount = 0;
        var flatCount = 0;
        var points = new List<DiffPoint>(klines.Count);
        foreach (var kline in klines.OrderBy(kline => kline.OpenTimeUtc))
        {
            string direction;
            if (kline.ClosePrice > kline.OpenPrice)
            {
                upCount++;
                direction = "Up";
            }
            else if (kline.ClosePrice < kline.OpenPrice)
            {
                downCount++;
                direction = "Down";
            }
            else
            {
                flatCount++;
                direction = "Flat";
            }

            points.Add(new DiffPoint(
                asset.AssetSymbol,
                asset.BinanceSymbol,
                kline.OpenTimeUtc,
                kline.CloseTimeUtc,
                kline.OpenPrice,
                kline.ClosePrice,
                direction,
                upCount,
                downCount,
                flatCount,
                upCount - downCount,
                kline.Volume,
                kline.QuoteVolume,
                kline.TradeCount));
        }

        return points;
    }

    private static AssetSummary BuildSummary(
        AssetConfig asset,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        IReadOnlyList<DiffPoint> points)
    {
        var ordered = points.OrderBy(point => point.OpenTimeUtc).ToList();
        var last = ordered.LastOrDefault();
        var min = ordered
            .OrderBy(point => point.Diff)
            .ThenBy(point => point.OpenTimeUtc)
            .FirstOrDefault();
        var max = ordered
            .OrderByDescending(point => point.Diff)
            .ThenBy(point => point.OpenTimeUtc)
            .FirstOrDefault();
        return new AssetSummary(
            asset.AssetSymbol,
            asset.BinanceSymbol,
            asset.Color,
            windowStartUtc,
            windowEndUtc,
            ordered,
            last?.UpCount ?? 0,
            last?.DownCount ?? 0,
            last?.FlatCount ?? 0,
            last?.Diff ?? 0,
            min,
            max,
            ordered.FirstOrDefault()?.OpenTimeUtc,
            last?.OpenTimeUtc);
    }

    private static string BuildTimeSeriesCsv(IReadOnlyList<DiffPoint> points)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset_symbol,binance_symbol,open_time_utc,close_time_utc,open_price,close_price,direction,up_count,down_count,flat_count,diff,volume,quote_volume,trade_count");
        foreach (var point in points.OrderBy(point => point.AssetSymbol).ThenBy(point => point.OpenTimeUtc))
        {
            builder.AppendCsv(point.AssetSymbol);
            builder.AppendCsv(point.BinanceSymbol);
            builder.AppendCsv(point.OpenTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(point.CloseTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(point.OpenPrice.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.ClosePrice.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.Direction);
            builder.AppendCsv(point.UpCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.DownCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.FlatCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.Diff.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.Volume.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.QuoteVolume.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.TradeCount.ToString(CultureInfo.InvariantCulture), last: true);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildSummaryCsv(IReadOnlyList<AssetSummary> summaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset_symbol,binance_symbol,window_start_utc,window_end_utc,first_open_time_utc,last_open_time_utc,points,up_count,down_count,flat_count,last_diff,min_diff,min_open_time_utc,max_diff,max_open_time_utc");
        foreach (var summary in summaries)
        {
            builder.AppendCsv(summary.AssetSymbol);
            builder.AppendCsv(summary.BinanceSymbol);
            builder.AppendCsv(summary.WindowStartUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.WindowEndUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.FirstOpenTimeUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.LastOpenTimeUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.Points.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.UpCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.DownCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.FlatCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.LastDiff.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.MinPoint?.Diff.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MinPoint?.OpenTimeUtc.ToString("O", CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MaxPoint?.Diff.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MaxPoint?.OpenTimeUtc.ToString("O", CultureInfo.InvariantCulture) ?? "", last: true);
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
        builder.AppendLine("text{font-family:Segoe UI,Arial,sans-serif;fill:#111827}.title{font-size:28px;font-weight:700}.subtitle{font-size:15px;fill:#4b5563}.axis{font-size:12px;fill:#6b7280}.asset{font-size:20px;font-weight:700}.summary{font-size:14px;fill:#374151}.label{font-size:13px;font-weight:600}.line{fill:none;stroke-width:2}.grid{stroke:#e5e7eb;stroke-width:1}.axisline{stroke:#9ca3af;stroke-width:1}.min{fill:#dc2626}.max{fill:#16a34a}");
        builder.AppendLine("</style>");
        builder.AppendLine("<text x=\"96\" y=\"42\" class=\"title\">Binance 5m cumulative UpCount - DownCount</text>");
        builder.AppendLine($"""<text x="96" y="68" class="subtitle">Window UTC: {EscapeXml(windowStartUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))} - {EscapeXml(windowEndUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}; Up=close&gt;open, Down=close&lt;open, Flat unchanged</text>""");

        var monthTicks = BuildMonthTicks(windowStartUtc, windowEndUtc);
        for (var panelIndex = 0; panelIndex < summaries.Count; panelIndex++)
        {
            var summary = summaries[panelIndex];
            var yTop = top + (panelIndex * panelHeight);
            var plotTop = yTop + 48;
            var plotBottom = plotTop + chartHeight;
            var yValues = summary.Points.Select(point => point.Diff).Concat([0]).ToArray();
            var yMin = yValues.Min();
            var yMax = yValues.Max();
            if (yMin == yMax)
            {
                yMin--;
                yMax++;
            }

            var padding = Math.Max(5, (int)Math.Ceiling((yMax - yMin) * 0.06));
            yMin -= padding;
            yMax += padding;
            var yRange = Math.Max(1, yMax - yMin);

            double X(DateTimeOffset timestamp) =>
                left + ((timestamp - windowStartUtc).TotalSeconds / totalSeconds * chartWidth);
            double Y(int value) =>
                plotBottom - ((value - yMin) / (double)yRange * chartHeight);

            builder.AppendLine($"""<text x="{left}" y="{yTop + 24}" class="asset">{summary.AssetSymbol}</text>""");
            builder.AppendLine($"""<text x="{left + 64}" y="{yTop + 24}" class="summary">symbol={summary.BinanceSymbol}; points={summary.Points.Count}; Up={summary.UpCount}; Down={summary.DownCount}; Flat={summary.FlatCount}; last Diff={summary.LastDiff}; min={summary.MinPoint?.Diff.ToString(CultureInfo.InvariantCulture) ?? "-"}; max={summary.MaxPoint?.Diff.ToString(CultureInfo.InvariantCulture) ?? "-"}</text>""");
            builder.AppendLine($"""<line x1="{left}" y1="{plotBottom:F1}" x2="{left + chartWidth}" y2="{plotBottom:F1}" class="axisline"/>""");
            builder.AppendLine($"""<line x1="{left}" y1="{plotTop:F1}" x2="{left}" y2="{plotBottom:F1}" class="axisline"/>""");

            foreach (var tick in BuildYTicks(yMin, yMax))
            {
                var y = Y(tick);
                builder.AppendLine($"""<line x1="{left}" y1="{y:F1}" x2="{left + chartWidth}" y2="{y:F1}" class="grid"/>""");
                builder.AppendLine($"""<text x="{left - 12}" y="{y + 4:F1}" text-anchor="end" class="axis">{tick.ToString(CultureInfo.InvariantCulture)}</text>""");
            }

            foreach (var tick in monthTicks)
            {
                var x = X(tick);
                builder.AppendLine($"""<line x1="{x:F1}" y1="{plotTop}" x2="{x:F1}" y2="{plotBottom}" class="grid"/>""");
                builder.AppendLine($"""<text x="{x:F1}" y="{plotBottom + 22}" text-anchor="middle" class="axis">{EscapeXml(tick.ToString("MMM yyyy", CultureInfo.InvariantCulture))}</text>""");
            }

            if (summary.Points.Count > 0)
            {
                builder.AppendLine($"""<path d="{BuildPath(summary.Points, X, Y)}" class="line" stroke="{summary.Color}"/>""");
                DrawMarker(builder, summary.MinPoint, "min", X, Y, valueAnchorAbove: false, width);
                DrawMarker(builder, summary.MaxPoint, "max", X, Y, valueAnchorAbove: true, width);
            }
        }

        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static string BuildPath(
        IReadOnlyList<DiffPoint> points,
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
            builder.Append(y(point.Diff).ToString("F1", CultureInfo.InvariantCulture));
            builder.Append(' ');
        }

        return builder.ToString();
    }

    private static void DrawMarker(
        StringBuilder builder,
        DiffPoint? point,
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
        var markerY = y(point.Diff);
        var labelY = valueAnchorAbove ? markerY - 12 : markerY + 24;
        var label = markerClass + " " + point.Diff.ToString(CultureInfo.InvariantCulture) +
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

    private static DateTimeOffset FloorToFiveMinutes(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var minute = utc.Minute - (utc.Minute % 5);
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, minute, 0, TimeSpan.Zero);
    }

    private static string ResolveOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BinanceDiffTimeChart.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
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

    private sealed record AssetConfig(string AssetSymbol, string BinanceSymbol, string Color);

    private sealed record Kline(
        string AssetSymbol,
        string BinanceSymbol,
        DateTimeOffset OpenTimeUtc,
        DateTimeOffset CloseTimeUtc,
        decimal OpenPrice,
        decimal ClosePrice,
        decimal Volume,
        decimal QuoteVolume,
        int TradeCount);

    private sealed record DiffPoint(
        string AssetSymbol,
        string BinanceSymbol,
        DateTimeOffset OpenTimeUtc,
        DateTimeOffset CloseTimeUtc,
        decimal OpenPrice,
        decimal ClosePrice,
        string Direction,
        int UpCount,
        int DownCount,
        int FlatCount,
        int Diff,
        decimal Volume,
        decimal QuoteVolume,
        int TradeCount);

    private sealed record AssetSummary(
        string AssetSymbol,
        string BinanceSymbol,
        string Color,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndUtc,
        IReadOnlyList<DiffPoint> Points,
        int UpCount,
        int DownCount,
        int FlatCount,
        int LastDiff,
        DiffPoint? MinPoint,
        DiffPoint? MaxPoint,
        DateTimeOffset? FirstOpenTimeUtc,
        DateTimeOffset? LastOpenTimeUtc);
}
