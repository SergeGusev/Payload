using System.Data;
using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;

internal static class Program
{
    private static readonly string[] Assets = ["BTC", "ETH", "SOL"];
    private static readonly HashSet<string> AcceptedSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "MarketWebSocket",
        "ReferenceStartEnd",
        "TerminalOrderBook",
        "GammaClosedMarket",
    };

    private static async Task<int> Main()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
            return 2;
        }

        var outputDirectory = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDirectory);
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
        if (!string.IsNullOrWhiteSpace(hostOverride))
        {
            builder.Host = hostOverride;
        }

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "SET LOCAL transaction_read_only = on; SET LOCAL statement_timeout = '120s'; SET LOCAL lock_timeout = '2s';");

        var databaseNowUtc = await GetDatabaseNowUtcAsync(connection, transaction);
        var endUtc = FloorToFiveMinutes(databaseNowUtc);
        var startUtc = FloorToFiveMinutes(endUtc.AddMonths(-6));
        var rows = await LoadRowsAsync(connection, transaction, startUtc, endUtc);
        await transaction.CommitAsync();

        var points = BuildPoints(rows);
        var summaries = BuildSummaries(points, startUtc, endUtc);

        var timeSeriesPath = Path.Combine(outputDirectory, "diff-timeseries.csv");
        var summaryPath = Path.Combine(outputDirectory, "diff-summary.csv");
        var chartPath = Path.Combine(outputDirectory, "diff-chart.svg");
        var zoomChartPath = Path.Combine(outputDirectory, "diff-chart-data-range.svg");
        var dataStartUtc = summaries
            .Select(summary => summary.FirstMarketStartUtc)
            .Where(value => value.HasValue)
            .Min() ?? startUtc;
        var dataEndUtc = summaries
            .Select(summary => summary.LastMarketStartUtc)
            .Where(value => value.HasValue)
            .Max() ?? endUtc;

        await File.WriteAllTextAsync(timeSeriesPath, BuildTimeSeriesCsv(points), Encoding.UTF8);
        await File.WriteAllTextAsync(summaryPath, BuildSummaryCsv(summaries), Encoding.UTF8);
        await File.WriteAllTextAsync(
            chartPath,
            BuildSvg(
                summaries,
                startUtc,
                endUtc,
                "Cumulative UpCount - DownCount by 5m market outcome",
                "requested six-month UTC window"),
            Encoding.UTF8);
        await File.WriteAllTextAsync(
            zoomChartPath,
            BuildSvg(
                summaries,
                dataStartUtc,
                dataEndUtc,
                "Cumulative UpCount - DownCount by 5m market outcome",
                "zoomed to available accepted-result data"),
            Encoding.UTF8);

        Console.WriteLine($"analysis_started_utc={DateTimeOffset.UtcNow:O}");
        Console.WriteLine($"target_database={builder.Database}");
        Console.WriteLine($"target_host={builder.Host}");
        Console.WriteLine($"window_start_utc={startUtc:O}");
        Console.WriteLine($"window_end_utc={endUtc:O}");
        foreach (var summary in summaries)
        {
            Console.WriteLine(
                "summary=" +
                $"asset={summary.AssetSymbol};" +
                $"points={summary.Points.Count.ToString(CultureInfo.InvariantCulture)};" +
                $"up={summary.UpCount.ToString(CultureInfo.InvariantCulture)};" +
                $"down={summary.DownCount.ToString(CultureInfo.InvariantCulture)};" +
                $"last_diff={summary.LastDiff.ToString(CultureInfo.InvariantCulture)};" +
                $"min_diff={summary.MinPoint?.Diff.ToString(CultureInfo.InvariantCulture) ?? ""};" +
                $"min_utc={summary.MinPoint?.MarketStartUtc.ToString("O", CultureInfo.InvariantCulture) ?? ""};" +
                $"max_diff={summary.MaxPoint?.Diff.ToString(CultureInfo.InvariantCulture) ?? ""};" +
                $"max_utc={summary.MaxPoint?.MarketStartUtc.ToString("O", CultureInfo.InvariantCulture) ?? ""};" +
                $"first_utc={summary.FirstMarketStartUtc?.ToString("O", CultureInfo.InvariantCulture) ?? ""};" +
                $"last_utc={summary.LastMarketStartUtc?.ToString("O", CultureInfo.InvariantCulture) ?? ""}");
        }

        Console.WriteLine($"timeseries_csv={timeSeriesPath}");
        Console.WriteLine($"summary_csv={summaryPath}");
        Console.WriteLine($"chart_svg={chartPath}");
        Console.WriteLine($"chart_zoom_svg={zoomChartPath}");
        Console.WriteLine($"analysis_finished_utc={DateTimeOffset.UtcNow:O}");
        return 0;
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }

    private static string ResolveOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CryptoDiffTimeChart.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static async Task<DateTimeOffset> GetDatabaseNowUtcAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT now();";
        command.CommandTimeout = 120;
        var value = await command.ExecuteScalarAsync();
        return value switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => DateTimeOffset.UtcNow,
        };
    }

    private static async Task<List<ResultRow>> LoadRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 120;
        command.CommandText = """
SELECT upper(asset_symbol) AS asset_symbol,
       market_start_utc,
       market_end_utc,
       market_id,
       market_slug,
       winning_outcome,
       source
FROM crypto_up_down_5m_websocket_resolved_markets
WHERE upper(asset_symbol) = ANY(@AssetSymbols)
  AND market_start_utc >= @StartUtc
  AND market_start_utc <= @EndUtc
  AND upper(winning_outcome) IN ('UP', 'DOWN')
  AND source = ANY(@Sources)
ORDER BY asset_symbol, market_start_utc;
""";
        command.Parameters.AddWithValue("AssetSymbols", Assets);
        command.Parameters.AddWithValue("Sources", AcceptedSources.ToArray());
        command.Parameters.Add("StartUtc", NpgsqlDbType.TimestampTz).Value = startUtc;
        command.Parameters.Add("EndUtc", NpgsqlDbType.TimestampTz).Value = endUtc;

        var rows = new List<ResultRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var winningOutcome = reader.GetString(5);
            if (!string.Equals(winningOutcome, "Up", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(winningOutcome, "Down", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.Add(new ResultRow(
                reader.GetString(0),
                reader.GetFieldValue<DateTimeOffset>(1).ToUniversalTime(),
                reader.GetFieldValue<DateTimeOffset>(2).ToUniversalTime(),
                reader.GetString(3),
                reader.GetString(4),
                string.Equals(winningOutcome, "Up", StringComparison.OrdinalIgnoreCase) ? "Up" : "Down",
                reader.GetString(6)));
        }

        return rows;
    }

    private static List<DiffPoint> BuildPoints(List<ResultRow> rows)
    {
        var points = new List<DiffPoint>(rows.Count);
        foreach (var assetGroup in rows
            .GroupBy(row => row.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var upCount = 0;
            var downCount = 0;
            var seenMarketStarts = new HashSet<DateTimeOffset>();
            foreach (var row in assetGroup.OrderBy(row => row.MarketStartUtc))
            {
                if (!seenMarketStarts.Add(row.MarketStartUtc))
                {
                    continue;
                }

                if (string.Equals(row.WinningOutcome, "Up", StringComparison.OrdinalIgnoreCase))
                {
                    upCount++;
                }
                else
                {
                    downCount++;
                }

                points.Add(new DiffPoint(
                    row.AssetSymbol,
                    row.MarketStartUtc,
                    row.MarketEndUtc,
                    row.WinningOutcome,
                    row.Source,
                    row.MarketId,
                    row.MarketSlug,
                    upCount,
                    downCount,
                    upCount - downCount));
            }
        }

        return points;
    }

    private static List<AssetSummary> BuildSummaries(
        List<DiffPoint> points,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        return Assets
            .Select(asset =>
            {
                var assetPoints = points
                    .Where(point => string.Equals(point.AssetSymbol, asset, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(point => point.MarketStartUtc)
                    .ToList();
                var upCount = assetPoints.LastOrDefault()?.UpCount ?? 0;
                var downCount = assetPoints.LastOrDefault()?.DownCount ?? 0;
                var min = assetPoints
                    .OrderBy(point => point.Diff)
                    .ThenBy(point => point.MarketStartUtc)
                    .FirstOrDefault();
                var max = assetPoints
                    .OrderByDescending(point => point.Diff)
                    .ThenBy(point => point.MarketStartUtc)
                    .FirstOrDefault();
                return new AssetSummary(
                    asset,
                    windowStartUtc,
                    windowEndUtc,
                    assetPoints,
                    upCount,
                    downCount,
                    upCount - downCount,
                    min,
                    max,
                    assetPoints.FirstOrDefault()?.MarketStartUtc,
                    assetPoints.LastOrDefault()?.MarketStartUtc);
            })
            .ToList();
    }

    private static string BuildTimeSeriesCsv(IReadOnlyList<DiffPoint> points)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset_symbol,market_start_utc,market_end_utc,winning_outcome,source,market_id,market_slug,up_count,down_count,diff");
        foreach (var point in points.OrderBy(point => point.AssetSymbol).ThenBy(point => point.MarketStartUtc))
        {
            builder.AppendCsv(point.AssetSymbol);
            builder.AppendCsv(point.MarketStartUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(point.MarketEndUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(point.WinningOutcome);
            builder.AppendCsv(point.Source);
            builder.AppendCsv(point.MarketId);
            builder.AppendCsv(point.MarketSlug);
            builder.AppendCsv(point.UpCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.DownCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(point.Diff.ToString(CultureInfo.InvariantCulture), last: true);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildSummaryCsv(IReadOnlyList<AssetSummary> summaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset_symbol,window_start_utc,window_end_utc,first_market_start_utc,last_market_start_utc,points,up_count,down_count,last_diff,min_diff,min_market_start_utc,max_diff,max_market_start_utc");
        foreach (var summary in summaries)
        {
            builder.AppendCsv(summary.AssetSymbol);
            builder.AppendCsv(summary.WindowStartUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.WindowEndUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.FirstMarketStartUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.LastMarketStartUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.Points.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.UpCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.DownCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.LastDiff.ToString(CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.MinPoint?.Diff.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MinPoint?.MarketStartUtc.ToString("O", CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MaxPoint?.Diff.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(summary.MaxPoint?.MarketStartUtc.ToString("O", CultureInfo.InvariantCulture) ?? "", last: true);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildSvg(
        IReadOnlyList<AssetSummary> summaries,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        string title,
        string subtitle)
    {
        const int width = 1600;
        const int panelHeight = 320;
        const int top = 92;
        const int left = 96;
        const int right = 44;
        const int chartWidth = width - left - right;
        const int chartHeight = 220;
        var height = top + (panelHeight * summaries.Count) + 80;
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BTC"] = "#2563eb",
            ["ETH"] = "#059669",
            ["SOL"] = "#d97706",
        };

        var builder = new StringBuilder();
        builder.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">""");
        builder.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
        builder.AppendLine("<style>");
        builder.AppendLine("text{font-family:Segoe UI,Arial,sans-serif;fill:#111827}.title{font-size:28px;font-weight:700}.subtitle{font-size:15px;fill:#4b5563}.axis{font-size:12px;fill:#6b7280}.asset{font-size:20px;font-weight:700}.summary{font-size:14px;fill:#374151}.label{font-size:13px;font-weight:600}.line{fill:none;stroke-width:2}.grid{stroke:#e5e7eb;stroke-width:1}.axisline{stroke:#9ca3af;stroke-width:1}.min{fill:#dc2626}.max{fill:#16a34a}");
        builder.AppendLine("</style>");
        builder.AppendLine($"""<text x="96" y="42" class="title">{EscapeXml(title)}</text>""");
        builder.AppendLine($"""<text x="96" y="68" class="subtitle">Window UTC: {EscapeXml(windowStartUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))} - {EscapeXml(windowEndUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}; {EscapeXml(subtitle)}; source: crypto_up_down_5m_websocket_resolved_markets</text>""");

        var totalSeconds = Math.Max(1, (windowEndUtc - windowStartUtc).TotalSeconds);
        var tickMonths = BuildMonthTicks(windowStartUtc, windowEndUtc);
        for (var panelIndex = 0; panelIndex < summaries.Count; panelIndex++)
        {
            var summary = summaries[panelIndex];
            var yTop = top + (panelIndex * panelHeight);
            var plotTop = yTop + 48;
            var plotBottom = plotTop + chartHeight;
            var color = colors.TryGetValue(summary.AssetSymbol, out var knownColor) ? knownColor : "#111827";
            var yValues = summary.Points.Select(point => point.Diff).Concat([0]).ToArray();
            var yMin = yValues.Length == 0 ? -1 : yValues.Min();
            var yMax = yValues.Length == 0 ? 1 : yValues.Max();
            if (yMin == yMax)
            {
                yMin--;
                yMax++;
            }

            var padding = Math.Max(2, (int)Math.Ceiling((yMax - yMin) * 0.06));
            yMin -= padding;
            yMax += padding;
            var yRange = Math.Max(1, yMax - yMin);

            double X(DateTimeOffset timestamp) =>
                left + ((timestamp - windowStartUtc).TotalSeconds / totalSeconds * chartWidth);
            double Y(int value) =>
                plotBottom - ((value - yMin) / (double)yRange * chartHeight);

            builder.AppendLine($"""<text x="{left}" y="{yTop + 24}" class="asset">{summary.AssetSymbol}</text>""");
            builder.AppendLine($"""<text x="{left + 64}" y="{yTop + 24}" class="summary">points={summary.Points.Count}; Up={summary.UpCount}; Down={summary.DownCount}; last Diff={summary.LastDiff}; min={summary.MinPoint?.Diff.ToString(CultureInfo.InvariantCulture) ?? "-"}; max={summary.MaxPoint?.Diff.ToString(CultureInfo.InvariantCulture) ?? "-"}; data={summary.FirstMarketStartUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-"}..{summary.LastMarketStartUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-"}</text>""");
            builder.AppendLine($"""<line x1="{left}" y1="{plotBottom:F1}" x2="{left + chartWidth}" y2="{plotBottom:F1}" class="axisline"/>""");
            builder.AppendLine($"""<line x1="{left}" y1="{plotTop:F1}" x2="{left}" y2="{plotBottom:F1}" class="axisline"/>""");

            foreach (var tick in BuildYTicks(yMin, yMax))
            {
                var y = Y(tick);
                builder.AppendLine($"""<line x1="{left}" y1="{y:F1}" x2="{left + chartWidth}" y2="{y:F1}" class="grid"/>""");
                builder.AppendLine($"""<text x="{left - 12}" y="{y + 4:F1}" text-anchor="end" class="axis">{tick.ToString(CultureInfo.InvariantCulture)}</text>""");
            }

            foreach (var tick in tickMonths)
            {
                var x = X(tick);
                builder.AppendLine($"""<line x1="{x:F1}" y1="{plotTop}" x2="{x:F1}" y2="{plotBottom}" class="grid"/>""");
                builder.AppendLine($"""<text x="{x:F1}" y="{plotBottom + 22}" text-anchor="middle" class="axis">{EscapeXml(tick.ToString("MMM yyyy", CultureInfo.InvariantCulture))}</text>""");
            }

            if (summary.Points.Count > 0)
            {
                var path = BuildPath(summary.Points, X, Y);
                builder.AppendLine($"""<path d="{path}" class="line" stroke="{color}"/>""");
                DrawMarker(builder, summary.MinPoint, "min", X, Y, valueAnchorAbove: false, width);
                DrawMarker(builder, summary.MaxPoint, "max", X, Y, valueAnchorAbove: true, width);
            }
            else
            {
                builder.AppendLine($"""<text x="{left + 20}" y="{plotTop + 48}" class="summary">No accepted resolved rows in this window.</text>""");
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
            builder.Append(x(point.MarketStartUtc).ToString("F1", CultureInfo.InvariantCulture));
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

        var markerX = x(point.MarketStartUtc);
        var markerY = y(point.Diff);
        var labelY = valueAnchorAbove ? markerY - 12 : markerY + 24;
        var label = markerClass + " " + point.Diff.ToString(CultureInfo.InvariantCulture) +
            " @ " + point.MarketStartUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var anchorAtEnd = markerX > svgWidth - 360;
        var labelX = anchorAtEnd ? markerX - 8 : markerX + 8;
        var anchor = anchorAtEnd ? "end" : "start";
        builder.AppendLine($"""<circle cx="{markerX:F1}" cy="{markerY:F1}" r="5" class="{markerClass}"/>""");
        builder.AppendLine($"""<text x="{labelX:F1}" y="{labelY:F1}" text-anchor="{anchor}" class="label {markerClass}">{EscapeXml(label)}</text>""");
    }

    private static IReadOnlyList<DateTimeOffset> BuildMonthTicks(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var first = new DateTimeOffset(
            startUtc.Year,
            startUtc.Month,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
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

    private sealed record ResultRow(
        string AssetSymbol,
        DateTimeOffset MarketStartUtc,
        DateTimeOffset MarketEndUtc,
        string MarketId,
        string MarketSlug,
        string WinningOutcome,
        string Source);

    private sealed record DiffPoint(
        string AssetSymbol,
        DateTimeOffset MarketStartUtc,
        DateTimeOffset MarketEndUtc,
        string WinningOutcome,
        string Source,
        string MarketId,
        string MarketSlug,
        int UpCount,
        int DownCount,
        int Diff);

    private sealed record AssetSummary(
        string AssetSymbol,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndUtc,
        IReadOnlyList<DiffPoint> Points,
        int UpCount,
        int DownCount,
        int LastDiff,
        DiffPoint? MinPoint,
        DiffPoint? MaxPoint,
        DateTimeOffset? FirstMarketStartUtc,
        DateTimeOffset? LastMarketStartUtc);
}
