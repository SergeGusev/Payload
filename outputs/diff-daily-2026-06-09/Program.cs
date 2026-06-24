using System.Globalization;
using System.Net;
using System.Text;

var workDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var reportDate = args.Length > 1 ? args[1] : "2026-06-09";
var inputPath = Path.Combine(workDir, $"diff-daily-{reportDate}.tsv");
var csvPath = Path.Combine(workDir, $"diff-daily-{reportDate}.csv");
var htmlPath = Path.Combine(workDir, $"diff-daily-{reportDate}.html");

var rows = LoadRows(inputPath)
    .OrderBy(row => row.MarketStartUtc)
    .ThenBy(row => AssetSortKey(row.Asset))
    .ToArray();

if (rows.Length == 0)
{
    Console.Error.WriteLine("No rows found.");
    return 2;
}

WriteCsv(csvPath, rows);
WriteHtml(htmlPath, reportDate, rows, Path.GetFileName(csvPath));

Console.WriteLine($"html={htmlPath}");
Console.WriteLine($"csv={csvPath}");
Console.WriteLine($"rows={rows.Length}");
foreach (var group in rows.GroupBy(row => row.Asset).OrderBy(group => AssetSortKey(group.Key)))
{
    Console.WriteLine($"{group.Key}={group.Count()}");
}

return 0;

static Snapshot[] LoadRows(string path)
{
    var lines = File.ReadAllLines(path, Encoding.UTF8)
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .ToArray();
    if (lines.Length < 2)
    {
        return [];
    }

    var headers = lines[0].Split('\t');
    var rows = new List<Snapshot>();
    foreach (var line in lines.Skip(1))
    {
        var values = line.Split('\t');
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
        {
            map[headers[i]] = i < values.Length ? values[i] : string.Empty;
        }

        rows.Add(new Snapshot(
            map["asset_symbol"],
            DateTimeOffset.Parse(map["market_start_utc"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            DateTimeOffset.Parse(map["sampled_at_utc"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            ParseOptionalDate(map["counter_start_market_start_utc"]),
            int.Parse(map["up_count"], CultureInfo.InvariantCulture),
            int.Parse(map["down_count"], CultureInfo.InvariantCulture),
            int.Parse(map["diff"], CultureInfo.InvariantCulture),
            int.Parse(map["diff_count"], CultureInfo.InvariantCulture),
            int.Parse(map["processed_market_count"], CultureInfo.InvariantCulture),
            bool.Parse(map["counter_initialized"]),
            !string.Equals(map["history_fetch_failed"], "False", StringComparison.OrdinalIgnoreCase)));
    }

    return rows.ToArray();
}

static DateTimeOffset? ParseOptionalDate(string value)
{
    return string.IsNullOrWhiteSpace(value)
        ? null
        : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}

static void WriteCsv(string path, IReadOnlyList<Snapshot> rows)
{
    var builder = new StringBuilder();
    builder.AppendLine("asset_symbol,market_start_utc,sampled_at_utc,counter_start_market_start_utc,up_count,down_count,diff,diff_count,processed_market_count,counter_initialized,history_fetch_failed");
    foreach (var row in rows)
    {
        builder.Append(Csv(row.Asset)).Append(',');
        builder.Append(Csv(FormatUtc(row.MarketStartUtc))).Append(',');
        builder.Append(Csv(FormatUtc(row.SampledAtUtc))).Append(',');
        builder.Append(Csv(row.CounterStartUtc is null ? string.Empty : FormatUtc(row.CounterStartUtc.Value))).Append(',');
        builder.Append(row.UpCount.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(row.DownCount.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(row.Diff.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(row.DiffCount.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(row.ProcessedMarketCount.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(row.CounterInitialized ? "true" : "false").Append(',');
        builder.Append(row.HistoryFetchFailed ? "true" : "false").AppendLine();
    }

    File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
}

static string Csv(string value)
{
    return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

static void WriteHtml(string path, string reportDate, IReadOnlyList<Snapshot> rows, string csvFileName)
{
    var generatedUtc = DateTimeOffset.UtcNow;
    var groups = rows.GroupBy(row => row.Asset)
        .OrderBy(group => AssetSortKey(group.Key))
        .Select(group => group.OrderBy(row => row.MarketStartUtc).ToArray())
        .ToArray();
    var xMin = rows.Min(row => row.MarketStartUtc);
    var xMax = rows.Max(row => row.MarketStartUtc);

    var builder = new StringBuilder();
    builder.AppendLine("<!doctype html>");
    builder.AppendLine("<html lang=\"en\">");
    builder.AppendLine("<head>");
    builder.AppendLine("<meta charset=\"utf-8\">");
    builder.AppendLine($"<title>Diff around zero - {Html(reportDate)}</title>");
    builder.AppendLine("<style>");
    builder.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#F8FAFC;color:#111827}h1{font-size:24px;margin:0 0 6px}h2{font-size:18px;margin:0 0 12px}p{margin:6px 0 14px;color:#4B5563}.panel{background:#fff;border:1px solid #E5E7EB;border-radius:8px;padding:16px;margin:14px 0;box-shadow:0 1px 2px rgba(15,23,42,.04)}table{border-collapse:collapse;width:100%;background:#fff;font-size:13px}th,td{border:1px solid #E5E7EB;padding:7px 8px;text-align:left;vertical-align:top}th{background:#F1F5F9;color:#111827}.neg{color:#B91C1C;font-weight:700}.pos{color:#047857;font-weight:700}.meta{display:flex;gap:18px;flex-wrap:wrap;font-size:13px;color:#374151}.note{font-size:12px;color:#6B7280}.links a{margin-right:14px}.chart-title{font-size:16px;font-weight:700;fill:#111827}");
    builder.AppendLine("</style>");
    builder.AppendLine("</head>");
    builder.AppendLine("<body>");
    builder.AppendLine($"<h1>Diff around zero: BTC / ETH / SOL - {Html(reportDate)} UTC</h1>");
    builder.AppendLine("<p>X-axis uses <code>market_start_utc</code>. Y-axis is symmetric around zero. Source table: <code>crypto_up_down_5m_diff_snapshots</code>, value: <code>diff</code>.</p>");
    builder.AppendLine("<div class=\"meta\">");
    builder.AppendLine($"<span>Generated: {Html(FormatUtc(generatedUtc))}</span>");
    builder.AppendLine($"<span>Period: {Html(FormatPeriod(xMin, xMax, includeDate: true))}</span>");
    builder.AppendLine($"<span>Rows: {rows.Count.ToString(CultureInfo.InvariantCulture)}</span>");
    builder.AppendLine($"<span>CSV: {Html(csvFileName)}</span>");
    builder.AppendLine("</div>");
    builder.AppendLine("<div class=\"panel\"><h2>Summary</h2>");
    builder.AppendLine(BuildSummaryTable(groups));
    builder.AppendLine("<p class=\"note\">Zero hits are exact Diff=0 points. Sign crossings count adjacent points that move from negative to positive or positive to negative inside the same counter-start segment.</p>");
    builder.AppendLine("</div>");
    builder.AppendLine("<div class=\"panel\">");
    builder.AppendLine(BuildChart("BTC / ETH / SOL", groups, xMin, xMax, labelExtremes: false));
    builder.AppendLine("</div>");

    foreach (var group in groups)
    {
        builder.AppendLine("<div class=\"panel\">");
        builder.AppendLine(BuildChart(group[0].Asset, [group], xMin, xMax, labelExtremes: true));
        builder.AppendLine("</div>");
    }

    builder.AppendLine("<div class=\"panel\"><h2>Files</h2>");
    builder.AppendLine($"<p><a href=\"{Html(csvFileName)}\">{Html(csvFileName)}</a></p>");
    builder.AppendLine("</div>");
    builder.AppendLine("</body>");
    builder.AppendLine("</html>");

    File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
}

static string BuildSummaryTable(IReadOnlyList<Snapshot[]> groups)
{
    var builder = new StringBuilder();
    builder.AppendLine("<table><thead><tr><th>Asset</th><th>Points</th><th>Period UTC</th><th>Min Diff</th><th>Min at</th><th>Max Diff</th><th>Max at</th><th>Zero hits</th><th>Sign crossings</th><th>Counter starts</th></tr></thead><tbody>");
    foreach (var rows in groups)
    {
        var min = rows.Min(row => row.Diff);
        var max = rows.Max(row => row.Diff);
        var minTimes = rows.Where(row => row.Diff == min).Select(row => row.MarketStartUtc).ToArray();
        var maxTimes = rows.Where(row => row.Diff == max).Select(row => row.MarketStartUtc).ToArray();
        var zeroHits = rows.Count(row => row.Diff == 0);
        var crossings = CountSignCrossings(rows);
        var starts = rows.Select(row => row.CounterStartUtc).Distinct().Count();

        builder.Append("<tr>");
        builder.Append("<td>").Append(Html(rows[0].Asset)).Append("</td>");
        builder.Append("<td>").Append(rows.Length.ToString(CultureInfo.InvariantCulture)).Append("</td>");
        builder.Append("<td>").Append(Html(FormatPeriod(rows.First().MarketStartUtc, rows.Last().MarketStartUtc, includeDate: false))).Append("</td>");
        builder.Append("<td class=\"").Append(min < 0 ? "neg" : min > 0 ? "pos" : string.Empty).Append("\">").Append(min.ToString(CultureInfo.InvariantCulture)).Append("</td>");
        builder.Append("<td>").Append(Html(FormatTimes(minTimes))).Append("</td>");
        builder.Append("<td class=\"").Append(max < 0 ? "neg" : max > 0 ? "pos" : string.Empty).Append("\">").Append(max.ToString(CultureInfo.InvariantCulture)).Append("</td>");
        builder.Append("<td>").Append(Html(FormatTimes(maxTimes))).Append("</td>");
        builder.Append("<td>").Append(zeroHits.ToString(CultureInfo.InvariantCulture)).Append("</td>");
        builder.Append("<td>").Append(crossings.ToString(CultureInfo.InvariantCulture)).Append("</td>");
        builder.Append("<td>").Append(starts.ToString(CultureInfo.InvariantCulture)).Append("</td>");
        builder.AppendLine("</tr>");
    }

    builder.AppendLine("</tbody></table>");
    return builder.ToString();
}

static string BuildChart(string title, IReadOnlyList<Snapshot[]> seriesGroups, DateTimeOffset xMin, DateTimeOffset xMax, bool labelExtremes)
{
    const double width = 1220;
    const double height = 440;
    const double left = 70;
    const double right = 28;
    const double top = 40;
    const double bottom = 58;
    var plotWidth = width - left - right;
    var plotHeight = height - top - bottom;

    var maxAbs = Math.Max(1, seriesGroups.SelectMany(group => group).Max(row => Math.Abs(row.Diff)));
    var step = NiceStep(maxAbs);
    var yMaxAbs = Math.Max(step, (int)Math.Ceiling(maxAbs / (double)step) * step);
    var xRangeSeconds = Math.Max(1, (xMax - xMin).TotalSeconds);

    double X(DateTimeOffset value) => left + ((value - xMin).TotalSeconds / xRangeSeconds * plotWidth);
    double Y(double value) => top + ((yMaxAbs - value) / (2.0 * yMaxAbs) * plotHeight);

    var builder = new StringBuilder();
    builder.AppendLine($"<svg viewBox=\"0 0 {N(width)} {N(height)}\" width=\"100%\" height=\"{N(height)}\" role=\"img\" aria-label=\"{Html(title)} Diff around zero\">");
    builder.AppendLine($"<rect x=\"0\" y=\"0\" width=\"{N(width)}\" height=\"{N(height)}\" fill=\"#FFFFFF\"/>");
    builder.AppendLine($"<text x=\"18\" y=\"24\" class=\"chart-title\">{Html(title)} Diff around zero</text>");

    for (var value = -yMaxAbs; value <= yMaxAbs; value += step)
    {
        var y = Y(value);
        var isZero = value == 0;
        builder.AppendLine($"<line x1=\"{N(left)}\" y1=\"{N(y)}\" x2=\"{N(width - right)}\" y2=\"{N(y)}\" stroke=\"{(isZero ? "#111827" : "#E5E7EB")}\" stroke-width=\"{(isZero ? "1.6" : "1")}\"/>");
        builder.AppendLine($"<text x=\"{N(left - 10)}\" y=\"{N(y + 4)}\" text-anchor=\"end\" font-size=\"11\" fill=\"#4B5563\">{value.ToString(CultureInfo.InvariantCulture)}</text>");
    }

    foreach (var tick in HourTicks(xMin, xMax))
    {
        var x = X(tick);
        builder.AppendLine($"<line x1=\"{N(x)}\" y1=\"{N(top)}\" x2=\"{N(x)}\" y2=\"{N(top + plotHeight)}\" stroke=\"#F3F4F6\"/>");
        builder.AppendLine($"<text x=\"{N(x)}\" y=\"{N(height - 28)}\" text-anchor=\"middle\" font-size=\"11\" fill=\"#4B5563\">{Html(tick.ToString("HH:mm", CultureInfo.InvariantCulture))}</text>");
    }

    var resetMarkers = seriesGroups.SelectMany(group => GetResetMarkers(group))
        .Distinct()
        .Where(value => value >= xMin && value <= xMax)
        .OrderBy(value => value)
        .ToArray();
    foreach (var marker in resetMarkers)
    {
        var x = X(marker);
        builder.AppendLine($"<line x1=\"{N(x)}\" y1=\"{N(top)}\" x2=\"{N(x)}\" y2=\"{N(top + plotHeight)}\" stroke=\"#9CA3AF\" stroke-dasharray=\"4 5\" stroke-width=\"1\"/>");
    }

    foreach (var group in seriesGroups)
    {
        var color = AssetColor(group[0].Asset);
        foreach (var segment in SplitSegments(group))
        {
            var points = string.Join(' ', segment.Select(row => $"{N(X(row.MarketStartUtc))},{N(Y(row.Diff))}"));
            builder.AppendLine($"<polyline points=\"{points}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2.4\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        }

        foreach (var row in group.Where(row => row.Diff == 0))
        {
            builder.AppendLine($"<circle cx=\"{N(X(row.MarketStartUtc))}\" cy=\"{N(Y(row.Diff))}\" r=\"3.2\" fill=\"#FFFFFF\" stroke=\"{color}\" stroke-width=\"1.6\"><title>{Html(row.Asset)} {Html(FormatUtc(row.MarketStartUtc))}: Diff 0</title></circle>");
        }

        var min = group.Min(row => row.Diff);
        var max = group.Max(row => row.Diff);
        foreach (var row in group.Where(row => row.Diff == min || row.Diff == max))
        {
            builder.AppendLine($"<circle cx=\"{N(X(row.MarketStartUtc))}\" cy=\"{N(Y(row.Diff))}\" r=\"4\" fill=\"{color}\"><title>{Html(row.Asset)} {Html(FormatUtc(row.MarketStartUtc))}: Diff {row.Diff.ToString(CultureInfo.InvariantCulture)}</title></circle>");
            if (labelExtremes)
            {
                var labelY = row.Diff >= 0 ? Y(row.Diff) - 8 : Y(row.Diff) + 16;
                builder.AppendLine($"<text x=\"{N(X(row.MarketStartUtc) + 6)}\" y=\"{N(labelY)}\" font-size=\"11\" fill=\"{color}\">{row.Diff.ToString(CultureInfo.InvariantCulture)}</text>");
            }
        }
    }

    var legendX = left;
    var legendY = height - 9;
    foreach (var group in seriesGroups)
    {
        var color = AssetColor(group[0].Asset);
        builder.AppendLine($"<line x1=\"{N(legendX)}\" y1=\"{N(legendY - 4)}\" x2=\"{N(legendX + 24)}\" y2=\"{N(legendY - 4)}\" stroke=\"{color}\" stroke-width=\"3\"/>");
        builder.AppendLine($"<text x=\"{N(legendX + 30)}\" y=\"{N(legendY)}\" font-size=\"12\" fill=\"#374151\">{Html(group[0].Asset)}</text>");
        legendX += 92;
    }

    if (resetMarkers.Length > 0)
    {
        builder.AppendLine($"<line x1=\"{N(width - 250)}\" y1=\"{N(legendY - 4)}\" x2=\"{N(width - 222)}\" y2=\"{N(legendY - 4)}\" stroke=\"#9CA3AF\" stroke-dasharray=\"4 5\"/>");
        builder.AppendLine($"<text x=\"{N(width - 216)}\" y=\"{N(legendY)}\" font-size=\"12\" fill=\"#374151\">counter start change</text>");
    }

    builder.AppendLine("</svg>");
    return builder.ToString();
}

static IReadOnlyList<IReadOnlyList<Snapshot>> SplitSegments(IReadOnlyList<Snapshot> rows)
{
    var segments = new List<IReadOnlyList<Snapshot>>();
    var current = new List<Snapshot>();
    Snapshot? previous = null;
    foreach (var row in rows.OrderBy(row => row.MarketStartUtc))
    {
        var newSegment = previous is not null &&
            (!Nullable.Equals(previous.CounterStartUtc, row.CounterStartUtc) ||
             (row.MarketStartUtc - previous.MarketStartUtc).TotalMinutes > 15);
        if (newSegment && current.Count > 0)
        {
            segments.Add(current.ToArray());
            current.Clear();
        }

        current.Add(row);
        previous = row;
    }

    if (current.Count > 0)
    {
        segments.Add(current.ToArray());
    }

    return segments;
}

static IReadOnlyList<DateTimeOffset> GetResetMarkers(IReadOnlyList<Snapshot> rows)
{
    var markers = new List<DateTimeOffset>();
    Snapshot? previous = null;
    foreach (var row in rows.OrderBy(row => row.MarketStartUtc))
    {
        if (previous is null || !Nullable.Equals(previous.CounterStartUtc, row.CounterStartUtc))
        {
            markers.Add(row.MarketStartUtc);
        }

        previous = row;
    }

    return markers;
}

static IEnumerable<DateTimeOffset> HourTicks(DateTimeOffset start, DateTimeOffset end)
{
    var current = new DateTimeOffset(start.Year, start.Month, start.Day, start.Hour, 0, 0, TimeSpan.Zero);
    if (current < start)
    {
        current = current.AddHours(1);
    }

    while (current <= end)
    {
        yield return current;
        current = current.AddHours(1);
    }
}

static int CountSignCrossings(IReadOnlyList<Snapshot> rows)
{
    var count = 0;
    Snapshot? previous = null;
    foreach (var row in rows.OrderBy(row => row.MarketStartUtc))
    {
        if (previous is not null &&
            Nullable.Equals(previous.CounterStartUtc, row.CounterStartUtc) &&
            previous.Diff != 0 &&
            row.Diff != 0 &&
            Math.Sign(previous.Diff) != Math.Sign(row.Diff))
        {
            count++;
        }

        previous = row;
    }

    return count;
}

static int NiceStep(int maxAbs)
{
    return maxAbs switch
    {
        <= 5 => 1,
        <= 10 => 2,
        <= 25 => 5,
        <= 50 => 10,
        _ => 25
    };
}

static string AssetColor(string asset)
{
    return asset.ToUpperInvariant() switch
    {
        "BTC" => "#2563EB",
        "ETH" => "#059669",
        "SOL" => "#D97706",
        _ => "#4B5563"
    };
}

static int AssetSortKey(string asset)
{
    return asset.ToUpperInvariant() switch
    {
        "BTC" => 10,
        "ETH" => 20,
        "SOL" => 30,
        _ => 100
    };
}

static string FormatUtc(DateTimeOffset value)
{
    return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

static string FormatPeriod(DateTimeOffset start, DateTimeOffset end, bool includeDate)
{
    var format = includeDate ? "yyyy-MM-dd HH:mm" : "HH:mm";
    return start.UtcDateTime.ToString(format, CultureInfo.InvariantCulture) + "-" +
        end.UtcDateTime.ToString(format, CultureInfo.InvariantCulture) + " UTC";
}

static string FormatTimes(IReadOnlyList<DateTimeOffset> values)
{
    var times = values.Select(value => value.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)).ToArray();
    return times.Length <= 8
        ? string.Join(", ", times)
        : string.Join(", ", times.Take(8)) + ", ...";
}

static string N(double value)
{
    return value.ToString("0.###", CultureInfo.InvariantCulture);
}

static string Html(string value)
{
    return WebUtility.HtmlEncode(value);
}

sealed record Snapshot(
    string Asset,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset SampledAtUtc,
    DateTimeOffset? CounterStartUtc,
    int UpCount,
    int DownCount,
    int Diff,
    int DiffCount,
    int ProcessedMarketCount,
    bool CounterInitialized,
    bool HistoryFetchFailed);
