using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

const string Symbol = "BTCUSDT";
const string BaseUrl = "https://data.binance.vision/data/futures/um/daily";

var options = ReportOptions.Parse(args);
Directory.CreateDirectory(options.OutputDir);
Directory.CreateDirectory(options.CacheDir);

var capturedAtUtc = DateTimeOffset.UtcNow;
var reportRows = await LoadRowsAsync(options);
var summary = BuildSummary(reportRows);

var xlsxPath = Path.Combine(options.OutputDir, "binance-futures-btc-up-simple-correlation-2026-06-13.xlsx");
var htmlPath = Path.Combine(options.OutputDir, "binance-futures-btc-up-simple-correlation-2026-06-13.html");
var csvPath = Path.Combine(options.OutputDir, "binance-futures-btc-up-simple-correlation-2026-06-13.csv");
var summaryPath = Path.Combine(options.OutputDir, "summary.txt");

WriteCsv(csvPath, reportRows);
WriteHtml(htmlPath, reportRows, summary, options, capturedAtUtc);
CreateWorkbook(xlsxPath, reportRows, summary, options, capturedAtUtc);
ValidateWorkbook(xlsxPath);
VerifyWorkbook(xlsxPath, reportRows.Count);
WriteSummary(summaryPath, reportRows, summary, options, capturedAtUtc, xlsxPath, htmlPath, csvPath);

Console.WriteLine($"xlsx={xlsxPath}");
Console.WriteLine($"html={htmlPath}");
Console.WriteLine($"csv={csvPath}");
Console.WriteLine($"summary={summaryPath}");
Console.WriteLine($"period_utc={options.Start:yyyy-MM-dd}..{options.EndExclusive.AddDays(-1):yyyy-MM-dd}");
Console.WriteLine($"days={reportRows.Count}");
Console.WriteLine($"evaluated_days={summary.EvaluatedDays}");
Console.WriteLine($"success={summary.SuccessDays}");
Console.WriteLine($"failure={summary.FailureDays}");
Console.WriteLine($"hit_rate={FormatPercent(summary.HitRate)}");
Console.WriteLine($"pearson={FormatNullable(summary.PearsonCorrelation, "0.0000")}");
return 0;

static async Task<IReadOnlyList<DailyReportRow>> LoadRowsAsync(ReportOptions options)
{
    using var httpClient = new HttpClient();
    httpClient.Timeout = TimeSpan.FromSeconds(60);
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PolyCopyTrader-Research/1.0");

    var dates = new List<DateOnly>();
    for (var date = options.Start; date < options.EndExclusive; date = date.AddDays(1))
    {
        dates.Add(date);
    }

    var rows = new ConcurrentBag<DailyReportRow>();
    var downloadErrors = new ConcurrentQueue<string>();
    var parallelOptions = new ParallelOptions
    {
        MaxDegreeOfParallelism = options.MaxParallelism
    };

    await Parallel.ForEachAsync(dates, parallelOptions, async (date, cancellationToken) =>
    {
        try
        {
            var metrics = await LoadDailyMetricsAsync(httpClient, options.CacheDir, date, cancellationToken);
            var outcomes = await LoadDailyKlinesAsync(httpClient, options.CacheDir, date, cancellationToken);
            rows.Add(BuildReportRow(date, metrics, outcomes));
        }
        catch (Exception ex)
        {
            downloadErrors.Enqueue($"{date:yyyy-MM-dd}: {ex.GetType().Name}: {ex.Message}");
            rows.Add(BuildReportRow(date, null, null, ex.Message));
        }
    });

    var orderedRows = rows.OrderBy(row => row.Date).ToList();
    if (!downloadErrors.IsEmpty)
    {
        var errorPath = Path.Combine(options.OutputDir, "download-errors.txt");
        File.WriteAllLines(errorPath, downloadErrors.OrderBy(value => value), Encoding.UTF8);
    }

    return orderedRows;
}

static async Task<MetricsSignal?> LoadDailyMetricsAsync(
    HttpClient httpClient,
    string cacheDir,
    DateOnly date,
    CancellationToken cancellationToken)
{
    var fileName = $"{Symbol}-metrics-{date:yyyy-MM-dd}.zip";
    var url = $"{BaseUrl}/metrics/{Symbol}/{fileName}";
    var zipPath = await DownloadZipAsync(httpClient, cacheDir, "metrics", fileName, url, cancellationToken);
    if (zipPath is null)
    {
        return null;
    }

    using var archive = ZipFile.OpenRead(zipPath);
    var entry = archive.Entries.FirstOrDefault(item => item.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
    if (entry is null)
    {
        return null;
    }

    await using var stream = entry.Open();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    var headerLine = await reader.ReadLineAsync(cancellationToken);
    if (string.IsNullOrWhiteSpace(headerLine))
    {
        return null;
    }

    var header = SplitCsv(headerLine);
    var map = BuildHeaderMap(header);
    var dataLine = await reader.ReadLineAsync(cancellationToken);
    if (string.IsNullOrWhiteSpace(dataLine))
    {
        return null;
    }

    var fields = SplitCsv(dataLine);
    var ratio = GetDecimal(fields, map, "count_long_short_ratio");
    var timestampUtc = GetUtcTimestamp(fields, map, "create_time");

    return new MetricsSignal(
        timestampUtc,
        ratio,
        GetDecimal(fields, map, "count_toptrader_long_short_ratio"),
        GetDecimal(fields, map, "sum_toptrader_long_short_ratio"),
        GetDecimal(fields, map, "sum_taker_long_short_vol_ratio"),
        GetDecimal(fields, map, "sum_open_interest"),
        ToForecastDirection(ratio));
}

static async Task<OutcomeCounts?> LoadDailyKlinesAsync(
    HttpClient httpClient,
    string cacheDir,
    DateOnly date,
    CancellationToken cancellationToken)
{
    var fileName = $"{Symbol}-5m-{date:yyyy-MM-dd}.zip";
    var url = $"{BaseUrl}/klines/{Symbol}/5m/{fileName}";
    var zipPath = await DownloadZipAsync(httpClient, cacheDir, "klines", fileName, url, cancellationToken);
    if (zipPath is null)
    {
        return null;
    }

    using var archive = ZipFile.OpenRead(zipPath);
    var entry = archive.Entries.FirstOrDefault(item => item.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
    if (entry is null)
    {
        return null;
    }

    await using var stream = entry.Open();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    var headerLine = await reader.ReadLineAsync(cancellationToken);
    if (string.IsNullOrWhiteSpace(headerLine))
    {
        return null;
    }

    var header = SplitCsv(headerLine);
    var hasHeader = header.Any(item => item.Equals("open", StringComparison.OrdinalIgnoreCase));
    var map = hasHeader
        ? BuildHeaderMap(header)
        : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["open"] = 1,
            ["close"] = 4
        };

    var up = 0;
    var down = 0;
    var tie = 0;
    var total = 0;

    if (!hasHeader)
    {
        CountKline(header, map, ref up, ref down, ref tie, ref total);
    }

    while (await reader.ReadLineAsync(cancellationToken) is { } line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        CountKline(SplitCsv(line), map, ref up, ref down, ref tie, ref total);
    }

    return new OutcomeCounts(up, down, tie, total);
}

static void CountKline(
    IReadOnlyList<string> fields,
    IReadOnlyDictionary<string, int> map,
    ref int up,
    ref int down,
    ref int tie,
    ref int total)
{
    var open = GetDecimal(fields, map, "open");
    var close = GetDecimal(fields, map, "close");
    if (open is null || close is null)
    {
        return;
    }

    total++;
    if (close > open)
    {
        up++;
    }
    else if (close < open)
    {
        down++;
    }
    else
    {
        tie++;
    }
}

static DailyReportRow BuildReportRow(
    DateOnly date,
    MetricsSignal? metrics,
    OutcomeCounts? outcomes,
    string? error = null)
{
    var actualDirection = outcomes switch
    {
        null => Direction.Missing,
        { UpCount: var up, DownCount: var down } when up > down => Direction.Up,
        { UpCount: var up, DownCount: var down } when down > up => Direction.Down,
        _ => Direction.Neutral
    };

    var result = (metrics?.ForecastDirection, actualDirection, outcomes?.TotalCount ?? 0) switch
    {
        (Direction.Up, Direction.Up, > 0) => ForecastResult.Success,
        (Direction.Down, Direction.Down, > 0) => ForecastResult.Success,
        (Direction.Up, Direction.Down, > 0) => ForecastResult.Failure,
        (Direction.Down, Direction.Up, > 0) => ForecastResult.Failure,
        (_, Direction.Neutral, > 0) => ForecastResult.Tie,
        (_, _, 0) => ForecastResult.NoResults,
        (Direction.Neutral, _, _) => ForecastResult.NoSignal,
        (null, _, _) => ForecastResult.NoSignal,
        _ => ForecastResult.NoSignal
    };

    return new DailyReportRow(
        date,
        metrics,
        outcomes,
        actualDirection,
        result,
        error);
}

static async Task<string?> DownloadZipAsync(
    HttpClient httpClient,
    string cacheDir,
    string category,
    string fileName,
    string url,
    CancellationToken cancellationToken)
{
    var categoryDir = Path.Combine(cacheDir, category);
    Directory.CreateDirectory(categoryDir);
    var targetPath = Path.Combine(categoryDir, fileName);
    if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
    {
        return targetPath;
    }

    for (var attempt = 1; attempt <= 3; attempt++)
    {
        try
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var tempPath = targetPath + $".{Guid.NewGuid():N}.tmp";
            await using (var fileStream = File.Create(tempPath))
            {
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            File.Move(tempPath, targetPath, overwrite: true);
            return targetPath;
        }
        catch when (attempt < 3)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
        }
    }

    throw new InvalidOperationException($"Unable to download {url}");
}

static ReportSummary BuildSummary(IReadOnlyList<DailyReportRow> rows)
{
    var evaluated = rows
        .Where(row => row.Result is ForecastResult.Success or ForecastResult.Failure)
        .ToList();

    var success = evaluated.Count(row => row.Result == ForecastResult.Success);
    var failure = evaluated.Count - success;
    var forecastUp = evaluated.Count(row => row.Metrics?.ForecastDirection == Direction.Up);
    var forecastDown = evaluated.Count(row => row.Metrics?.ForecastDirection == Direction.Down);
    var forecastUpSuccess = evaluated.Count(row => row.Metrics?.ForecastDirection == Direction.Up && row.Result == ForecastResult.Success);
    var forecastDownSuccess = evaluated.Count(row => row.Metrics?.ForecastDirection == Direction.Down && row.Result == ForecastResult.Success);
    var missingMetrics = rows.Count(row => row.Metrics is null);
    var missingKlines = rows.Count(row => row.Outcomes is null || row.Outcomes.TotalCount == 0);
    var tieDays = rows.Count(row => row.Result == ForecastResult.Tie);

    return new ReportSummary(
        rows.Count,
        evaluated.Count,
        success,
        failure,
        evaluated.Count == 0 ? null : success / (decimal)evaluated.Count,
        forecastUp,
        forecastDown,
        forecastUp == 0 ? null : forecastUpSuccess / (decimal)forecastUp,
        forecastDown == 0 ? null : forecastDownSuccess / (decimal)forecastDown,
        missingMetrics,
        missingKlines,
        tieDays,
        ComputePearson(evaluated));
}

static decimal? ComputePearson(IReadOnlyList<DailyReportRow> rows)
{
    var pairs = rows
        .Select(row => new
        {
            Forecast = DirectionSign(row.Metrics?.ForecastDirection ?? Direction.Missing),
            Actual = DirectionSign(row.ActualDirection)
        })
        .Where(row => row.Forecast != 0 && row.Actual != 0)
        .ToList();

    if (pairs.Count < 2)
    {
        return null;
    }

    var avgForecast = pairs.Average(row => row.Forecast);
    var avgActual = pairs.Average(row => row.Actual);
    var numerator = pairs.Sum(row => (row.Forecast - avgForecast) * (row.Actual - avgActual));
    var forecastDenominator = Math.Sqrt(pairs.Sum(row => Math.Pow(row.Forecast - avgForecast, 2)));
    var actualDenominator = Math.Sqrt(pairs.Sum(row => Math.Pow(row.Actual - avgActual, 2)));
    var denominator = forecastDenominator * actualDenominator;

    return denominator == 0 ? null : (decimal)(numerator / denominator);
}

static int DirectionSign(Direction direction) => direction switch
{
    Direction.Up => 1,
    Direction.Down => -1,
    _ => 0
};

static void WriteCsv(string path, IReadOnlyList<DailyReportRow> rows)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    writer.WriteLine(string.Join(",", Csv("Date UTC"), Csv("Forecast timestamp UTC"), Csv("Forecast"), Csv("count_long_short_ratio"), Csv("top trader account ratio"), Csv("top trader position ratio"), Csv("taker buy/sell volume ratio"), Csv("Up 5m count"), Csv("Down 5m count"), Csv("Tie 5m count"), Csv("Total 5m candles"), Csv("Up minus Down"), Csv("Actual majority"), Csv("Forecast hit"), Csv("Error")));

    foreach (var row in rows)
    {
        writer.WriteLine(string.Join(
            ",",
            Csv(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Csv(FormatTimestamp(row.Metrics?.TimestampUtc)),
            Csv(DirectionLabel(row.Metrics?.ForecastDirection ?? Direction.Missing)),
            Csv(FormatNullable(row.Metrics?.CountLongShortRatio, "0.########")),
            Csv(FormatNullable(row.Metrics?.CountTopTraderLongShortRatio, "0.########")),
            Csv(FormatNullable(row.Metrics?.SumTopTraderLongShortRatio, "0.########")),
            Csv(FormatNullable(row.Metrics?.SumTakerLongShortVolRatio, "0.########")),
            Csv(row.Outcomes?.UpCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            Csv(row.Outcomes?.DownCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            Csv(row.Outcomes?.TieCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            Csv(row.Outcomes?.TotalCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            Csv(row.Outcomes is null ? string.Empty : (row.Outcomes.UpCount - row.Outcomes.DownCount).ToString(CultureInfo.InvariantCulture)),
            Csv(DirectionLabel(row.ActualDirection)),
            Csv(ResultLabel(row.Result)),
            Csv(row.Error ?? string.Empty)));
    }
}

static void WriteHtml(
    string path,
    IReadOnlyList<DailyReportRow> rows,
    ReportSummary summary,
    ReportOptions options,
    DateTimeOffset capturedAtUtc)
{
    var html = new StringBuilder();
    html.AppendLine("<!doctype html>");
    html.AppendLine("<html lang=\"ru\">");
    html.AppendLine("<head>");
    html.AppendLine("<meta charset=\"utf-8\">");
    html.AppendLine("<title>Binance Futures BTC Up Simple correlation</title>");
    html.AppendLine("<style>");
    html.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1f2328;background:#fff}");
    html.AppendLine("h1{font-size:22px;margin:0 0 12px}");
    html.AppendLine(".meta{display:grid;grid-template-columns:220px 1fr;gap:4px 16px;margin-bottom:18px;font-size:13px}");
    html.AppendLine("table{border-collapse:collapse;font-size:12px;min-width:1180px}");
    html.AppendLine("th{background:#1f4e79;color:#fff;text-align:left;position:sticky;top:0}");
    html.AppendLine("th,td{border:1px solid #b7b7b7;padding:5px 7px;white-space:nowrap}");
    html.AppendLine("tr.success td{background:#d9ead3}");
    html.AppendLine("tr.failure td{background:#f4cccc}");
    html.AppendLine("tr.tie td{background:#fff2cc}");
    html.AppendLine("tr.no-data td{background:#e7e6e6}");
    html.AppendLine(".num{text-align:right;font-variant-numeric:tabular-nums}");
    html.AppendLine("</style>");
    html.AppendLine("</head>");
    html.AppendLine("<body>");
    html.AppendLine("<h1>BTCUSDT Binance Futures sentiment vs 5m Up/Down results</h1>");
    html.AppendLine("<div class=\"meta\">");
    AppendMeta(html, "Period UTC", $"{options.Start:yyyy-MM-dd} - {options.EndExclusive.AddDays(-1):yyyy-MM-dd}");
    AppendMeta(html, "Captured UTC", capturedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
    AppendMeta(html, "Evaluated days", summary.EvaluatedDays.ToString(CultureInfo.InvariantCulture));
    AppendMeta(html, "Success / failure", $"{summary.SuccessDays} / {summary.FailureDays}");
    AppendMeta(html, "Hit rate", FormatPercent(summary.HitRate));
    AppendMeta(html, "Pearson sign correlation", FormatNullable(summary.PearsonCorrelation, "0.0000"));
    AppendMeta(html, "Forecast method", "First daily metrics count_long_short_ratio row: >1 Up, <1 Down");
    AppendMeta(html, "Result method", "5m futures klines: close > open = Up, close < open = Down");
    html.AppendLine("</div>");
    html.AppendLine("<table>");
    html.AppendLine("<thead><tr><th>Date UTC</th><th>Forecast UTC</th><th>Forecast</th><th>Long/Short</th><th>Top acc</th><th>Top pos</th><th>Taker vol</th><th>Up</th><th>Down</th><th>Tie</th><th>Total</th><th>Up-Down</th><th>Actual</th><th>Hit?</th></tr></thead>");
    html.AppendLine("<tbody>");
    foreach (var row in rows)
    {
        html.Append("<tr class=\"").Append(Html(RowCssClass(row.Result))).AppendLine("\">");
        AppendCell(html, row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AppendCell(html, FormatTimestamp(row.Metrics?.TimestampUtc));
        AppendCell(html, DirectionLabel(row.Metrics?.ForecastDirection ?? Direction.Missing));
        AppendCell(html, FormatNullable(row.Metrics?.CountLongShortRatio, "0.########"), "num");
        AppendCell(html, FormatNullable(row.Metrics?.CountTopTraderLongShortRatio, "0.########"), "num");
        AppendCell(html, FormatNullable(row.Metrics?.SumTopTraderLongShortRatio, "0.########"), "num");
        AppendCell(html, FormatNullable(row.Metrics?.SumTakerLongShortVolRatio, "0.########"), "num");
        AppendCell(html, row.Outcomes?.UpCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty, "num");
        AppendCell(html, row.Outcomes?.DownCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty, "num");
        AppendCell(html, row.Outcomes?.TieCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty, "num");
        AppendCell(html, row.Outcomes?.TotalCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty, "num");
        AppendCell(html, row.Outcomes is null ? string.Empty : (row.Outcomes.UpCount - row.Outcomes.DownCount).ToString(CultureInfo.InvariantCulture), "num");
        AppendCell(html, DirectionLabel(row.ActualDirection));
        AppendCell(html, ResultLabel(row.Result));
        html.AppendLine("</tr>");
    }

    html.AppendLine("</tbody></table>");
    html.AppendLine("</body></html>");
    File.WriteAllText(path, html.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static void CreateWorkbook(
    string outputPath,
    IReadOnlyList<DailyReportRow> rows,
    ReportSummary summary,
    ReportOptions options,
    DateTimeOffset capturedAtUtc)
{
    if (File.Exists(outputPath))
    {
        File.Delete(outputPath);
    }

    using var document = SpreadsheetDocument.Create(outputPath, SpreadsheetDocumentType.Workbook);
    var workbookPart = document.AddWorkbookPart();
    workbookPart.Workbook = new Workbook();

    var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
    stylesPart.Stylesheet = BuildStylesheet();
    stylesPart.Stylesheet.Save();

    var sheets = workbookPart.Workbook.AppendChild(new Sheets());
    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    worksheetPart.Worksheet = BuildWorksheet(rows, summary, options, capturedAtUtc);
    worksheetPart.Worksheet.Save();

    sheets.Append(new Sheet
    {
        Id = workbookPart.GetIdOfPart(worksheetPart),
        SheetId = 1U,
        Name = "BTC Forecast"
    });

    workbookPart.Workbook.Append(new CalculationProperties
    {
        CalculationMode = CalculateModeValues.Auto,
        FullCalculationOnLoad = true,
        ForceFullCalculation = true
    });
    workbookPart.Workbook.Save();
}

static Worksheet BuildWorksheet(
    IReadOnlyList<DailyReportRow> rows,
    ReportSummary summary,
    ReportOptions options,
    DateTimeOffset capturedAtUtc)
{
    var sheetData = new SheetData();
    var rowIndex = 1U;

    sheetData.Append(Row(rowIndex++, Text("A1", "Binance Futures BTCUSDT forecast vs BTC Up Simple daily 5m results", Styles.Title)));
    sheetData.Append(Row(rowIndex++, Text("A2", "Period UTC", Styles.SummaryLabel), Text("B2", $"{options.Start:yyyy-MM-dd} - {options.EndExclusive.AddDays(-1):yyyy-MM-dd}", Styles.SummaryValue), Text("D2", "Captured UTC", Styles.SummaryLabel), Text("E2", capturedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Styles.SummaryValue)));
    sheetData.Append(Row(rowIndex++, Text("A3", "Evaluated days", Styles.SummaryLabel), Number("B3", summary.EvaluatedDays, Styles.Int), Text("D3", "Success / failure", Styles.SummaryLabel), Text("E3", $"{summary.SuccessDays} / {summary.FailureDays}", Styles.SummaryValue)));
    sheetData.Append(Row(rowIndex++, Text("A4", "Hit rate", Styles.SummaryLabel), Text("B4", FormatPercent(summary.HitRate), Styles.SummaryValue), Text("D4", "Pearson sign correlation", Styles.SummaryLabel), Text("E4", FormatNullable(summary.PearsonCorrelation, "0.0000"), Styles.SummaryValue)));
    sheetData.Append(Row(rowIndex++, Text("A5", "Forecast method", Styles.SummaryLabel), Text("B5", "First daily metrics count_long_short_ratio: >1 Up, <1 Down", Styles.SummaryValue), Text("D5", "Result method", Styles.SummaryLabel), Text("E5", "5m futures klines: close > open = Up, close < open = Down", Styles.SummaryValue)));
    sheetData.Append(Row(rowIndex++, Text("A6", "Data source", Styles.SummaryLabel), Text("B6", "Official Binance Data Collection futures/um daily metrics and klines archives", Styles.SummaryValue)));
    rowIndex++;

    var headerRowIndex = rowIndex++;
    var headers = new[]
    {
        "Date UTC",
        "Forecast timestamp UTC",
        "Forecast",
        "count_long_short_ratio",
        "top trader account ratio",
        "top trader position ratio",
        "taker buy/sell volume ratio",
        "Up 5m count",
        "Down 5m count",
        "Tie 5m count",
        "Total 5m candles",
        "Up minus Down",
        "Actual majority",
        "Forecast hit"
    };

    var headerCells = headers
        .Select((header, index) => Text($"{ColumnName(index + 1)}{headerRowIndex}", header, Styles.Header))
        .ToArray();
    sheetData.Append(Row(headerRowIndex, headerCells));

    foreach (var row in rows)
    {
        var styles = RowStyles.For(row.Result);
        var cells = new[]
        {
            Text($"A{rowIndex}", row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), styles.Text),
            Text($"B{rowIndex}", FormatTimestamp(row.Metrics?.TimestampUtc), styles.Text),
            Text($"C{rowIndex}", DirectionLabel(row.Metrics?.ForecastDirection ?? Direction.Missing), styles.Text),
            NullableDecimal($"D{rowIndex}", row.Metrics?.CountLongShortRatio, styles.Decimal, styles.Text),
            NullableDecimal($"E{rowIndex}", row.Metrics?.CountTopTraderLongShortRatio, styles.Decimal, styles.Text),
            NullableDecimal($"F{rowIndex}", row.Metrics?.SumTopTraderLongShortRatio, styles.Decimal, styles.Text),
            NullableDecimal($"G{rowIndex}", row.Metrics?.SumTakerLongShortVolRatio, styles.Decimal, styles.Text),
            NullableInt($"H{rowIndex}", row.Outcomes?.UpCount, styles.Int, styles.Text),
            NullableInt($"I{rowIndex}", row.Outcomes?.DownCount, styles.Int, styles.Text),
            NullableInt($"J{rowIndex}", row.Outcomes?.TieCount, styles.Int, styles.Text),
            NullableInt($"K{rowIndex}", row.Outcomes?.TotalCount, styles.Int, styles.Text),
            NullableInt($"L{rowIndex}", row.Outcomes is null ? null : row.Outcomes.UpCount - row.Outcomes.DownCount, styles.Int, styles.Text),
            Text($"M{rowIndex}", DirectionLabel(row.ActualDirection), styles.Text),
            Text($"N{rowIndex}", ResultLabel(row.Result), styles.Text)
        };
        sheetData.Append(Row(rowIndex, cells));
        rowIndex++;
    }

    var lastRowIndex = rowIndex - 1;

    return new Worksheet(
        new SheetViews(
            new SheetView
            {
                WorkbookViewId = 0U,
                Pane = new Pane
                {
                    VerticalSplit = headerRowIndex,
                    TopLeftCell = $"A{headerRowIndex + 1}",
                    ActivePane = PaneValues.BottomLeft,
                    State = PaneStateValues.Frozen
                }
            }),
        BuildColumns(),
        sheetData,
        new AutoFilter
        {
            Reference = $"A{headerRowIndex}:N{lastRowIndex}"
        },
        new PageMargins
        {
            Left = 0.3,
            Right = 0.3,
            Top = 0.5,
            Bottom = 0.5,
            Header = 0.3,
            Footer = 0.3
        });
}

static Columns BuildColumns()
{
    var widths = new double[] { 12, 22, 11, 19, 18, 20, 20, 12, 12, 12, 15, 14, 15, 14 };
    var columns = new Columns();
    for (var index = 0; index < widths.Length; index++)
    {
        columns.Append(new Column
        {
            Min = (uint)index + 1,
            Max = (uint)index + 1,
            Width = widths[index],
            CustomWidth = true
        });
    }

    return columns;
}

static Stylesheet BuildStylesheet()
{
    var fonts = new Fonts(
        new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
        new Font(new Bold(), new FontSize { Val = 11 }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Calibri" }),
        new Font(new Bold(), new FontSize { Val = 14 }, new FontName { Val = "Calibri" }),
        new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }));

    var fills = new Fills(
        new Fill(new PatternFill { PatternType = PatternValues.None }),
        new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
        SolidFill("FF1F4E79"),
        SolidFill("FFD9EAD3"),
        SolidFill("FFF4CCCC"),
        SolidFill("FFFFF2CC"),
        SolidFill("FFE7E6E6"),
        SolidFill("FFD9EAF7"));

    var borders = new Borders(
        new Border(),
        new Border(
            new LeftBorder { Style = BorderStyleValues.Thin, Color = new Color { Auto = true } },
            new RightBorder { Style = BorderStyleValues.Thin, Color = new Color { Auto = true } },
            new TopBorder { Style = BorderStyleValues.Thin, Color = new Color { Auto = true } },
            new BottomBorder { Style = BorderStyleValues.Thin, Color = new Color { Auto = true } },
            new DiagonalBorder()));

    var numberingFormats = new NumberingFormats(
        new NumberingFormat { NumberFormatId = 164U, FormatCode = "0" },
        new NumberingFormat { NumberFormatId = 165U, FormatCode = "0.00000000" });

    var cellFormats = new CellFormats(
        CellFormat(),
        CellFormat(fontId: 2U, fillId: 0U, borderId: 0U),
        CellFormat(fontId: 1U, fillId: 2U, borderId: 1U),
        CellFormat(fontId: 3U, fillId: 7U, borderId: 1U),
        CellFormat(fontId: 0U, fillId: 7U, borderId: 1U),
        CellFormat(fontId: 0U, fillId: 0U, borderId: 1U),
        CellFormat(fontId: 0U, fillId: 0U, borderId: 1U, numberFormatId: 164U),
        CellFormat(fontId: 0U, fillId: 0U, borderId: 1U, numberFormatId: 165U),
        CellFormat(fontId: 0U, fillId: 3U, borderId: 1U),
        CellFormat(fontId: 0U, fillId: 3U, borderId: 1U, numberFormatId: 164U),
        CellFormat(fontId: 0U, fillId: 3U, borderId: 1U, numberFormatId: 165U),
        CellFormat(fontId: 0U, fillId: 4U, borderId: 1U),
        CellFormat(fontId: 0U, fillId: 4U, borderId: 1U, numberFormatId: 164U),
        CellFormat(fontId: 0U, fillId: 4U, borderId: 1U, numberFormatId: 165U),
        CellFormat(fontId: 0U, fillId: 5U, borderId: 1U),
        CellFormat(fontId: 0U, fillId: 5U, borderId: 1U, numberFormatId: 164U),
        CellFormat(fontId: 0U, fillId: 5U, borderId: 1U, numberFormatId: 165U),
        CellFormat(fontId: 0U, fillId: 6U, borderId: 1U),
        CellFormat(fontId: 0U, fillId: 6U, borderId: 1U, numberFormatId: 164U),
        CellFormat(fontId: 0U, fillId: 6U, borderId: 1U, numberFormatId: 165U));

    return new Stylesheet(numberingFormats, fonts, fills, borders, cellFormats);
}

static Fill SolidFill(string rgb) =>
    new(new PatternFill(
        new ForegroundColor { Rgb = rgb },
        new BackgroundColor { Indexed = 64U })
    {
        PatternType = PatternValues.Solid
    });

static CellFormat CellFormat(uint fontId = 0U, uint fillId = 0U, uint borderId = 0U, uint? numberFormatId = null)
{
    var format = new CellFormat
    {
        FontId = fontId,
        FillId = fillId,
        BorderId = borderId,
        ApplyFont = fontId != 0U,
        ApplyFill = fillId != 0U,
        ApplyBorder = borderId != 0U
    };

    if (numberFormatId is not null)
    {
        format.NumberFormatId = numberFormatId.Value;
        format.ApplyNumberFormat = true;
    }

    return format;
}

static Row Row(uint rowIndex, params Cell[] cells)
{
    var row = new Row { RowIndex = rowIndex };
    foreach (var cell in cells)
    {
        row.Append(cell);
    }

    return row;
}

static Cell Text(string reference, string value, uint styleIndex) =>
    new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        StyleIndex = styleIndex,
        InlineString = new InlineString(new Text(value ?? string.Empty))
    };

static Cell Number(string reference, int value, uint styleIndex) =>
    new()
    {
        CellReference = reference,
        DataType = CellValues.Number,
        StyleIndex = styleIndex,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
    };

static Cell DecimalNumber(string reference, decimal value, uint styleIndex) =>
    new()
    {
        CellReference = reference,
        DataType = CellValues.Number,
        StyleIndex = styleIndex,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
    };

static Cell NullableInt(string reference, int? value, uint numberStyleIndex, uint textStyleIndex) =>
    value is null ? Text(reference, string.Empty, textStyleIndex) : Number(reference, value.Value, numberStyleIndex);

static Cell NullableDecimal(string reference, decimal? value, uint numberStyleIndex, uint textStyleIndex) =>
    value is null ? Text(reference, string.Empty, textStyleIndex) : DecimalNumber(reference, value.Value, numberStyleIndex);

static void ValidateWorkbook(string path)
{
    using var document = SpreadsheetDocument.Open(path, false);
    var validator = new OpenXmlValidator();
    var errors = validator.Validate(document).ToList();
    if (errors.Count > 0)
    {
        throw new InvalidOperationException($"OpenXML validation failed: {string.Join("; ", errors.Take(5).Select(error => error.Description))}");
    }
}

static void VerifyWorkbook(string path, int expectedRows)
{
    using var document = SpreadsheetDocument.Open(path, false);
    var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("Workbook part is missing.");
    var workbook = workbookPart.Workbook ?? throw new InvalidOperationException("Workbook is missing.");
    var sheet = workbook.Sheets?.Elements<Sheet>().SingleOrDefault()
        ?? throw new InvalidOperationException("Workbook must contain exactly one worksheet.");
    var sheetId = sheet.Id?.Value ?? throw new InvalidOperationException("Sheet relationship id is missing.");
    var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheetId);
    var worksheet = worksheetPart.Worksheet ?? throw new InvalidOperationException("Worksheet is missing.");
    var actualRows = worksheet.Descendants<Row>().Count(row => row.RowIndex is not null && row.RowIndex.Value >= 8U);
    if (actualRows < expectedRows)
    {
        throw new InvalidOperationException($"Workbook row verification failed: expected at least {expectedRows}, got {actualRows}.");
    }
}

static void WriteSummary(
    string path,
    IReadOnlyList<DailyReportRow> rows,
    ReportSummary summary,
    ReportOptions options,
    DateTimeOffset capturedAtUtc,
    string xlsxPath,
    string htmlPath,
    string csvPath)
{
    var lines = new List<string>
    {
        "Binance Futures BTCUSDT forecast vs BTC Up Simple daily 5m results",
        $"Captured UTC: {capturedAtUtc:yyyy-MM-dd HH:mm:ss}",
        $"Period UTC: {options.Start:yyyy-MM-dd} - {options.EndExclusive.AddDays(-1):yyyy-MM-dd}",
        $"Days: {rows.Count}",
        $"Evaluated days: {summary.EvaluatedDays}",
        $"Success: {summary.SuccessDays}",
        $"Failure: {summary.FailureDays}",
        $"Hit rate: {FormatPercent(summary.HitRate)}",
        $"Forecast Up days: {summary.ForecastUpDays}",
        $"Forecast Down days: {summary.ForecastDownDays}",
        $"Forecast Up hit rate: {FormatPercent(summary.ForecastUpHitRate)}",
        $"Forecast Down hit rate: {FormatPercent(summary.ForecastDownHitRate)}",
        $"Pearson sign correlation: {FormatNullable(summary.PearsonCorrelation, "0.0000")}",
        $"Missing metrics days: {summary.MissingMetricsDays}",
        $"Missing klines days: {summary.MissingKlinesDays}",
        $"Tie days: {summary.TieDays}",
        $"XLSX: {xlsxPath}",
        $"HTML: {htmlPath}",
        $"CSV: {csvPath}",
        "Forecast method: first daily metrics count_long_short_ratio row; >1 Up, <1 Down.",
        "Result method: BTCUSDT USD-M futures 5m klines; close > open = Up, close < open = Down.",
        "Source: official Binance Data Collection futures/um daily metrics and klines archives."
    };

    File.WriteAllLines(path, lines, Encoding.UTF8);
}

static string[] SplitCsv(string line) => line.Split(',');

static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> header)
{
    var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < header.Count; index++)
    {
        map[header[index].Trim()] = index;
    }

    return map;
}

static decimal? GetDecimal(IReadOnlyList<string> fields, IReadOnlyDictionary<string, int> map, string name)
{
    if (!map.TryGetValue(name, out var index) || index < 0 || index >= fields.Count)
    {
        return null;
    }

    return decimal.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        ? value
        : null;
}

static DateTime? GetUtcTimestamp(IReadOnlyList<string> fields, IReadOnlyDictionary<string, int> map, string name)
{
    if (!map.TryGetValue(name, out var index) || index < 0 || index >= fields.Count)
    {
        return null;
    }

    var value = fields[index].Trim();
    if (DateTime.TryParseExact(
            value,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
    {
        return parsed;
    }

    return DateTime.TryParse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
        out parsed)
        ? parsed
        : null;
}

static Direction ToForecastDirection(decimal? countLongShortRatio)
{
    if (countLongShortRatio is null)
    {
        return Direction.Missing;
    }

    return countLongShortRatio.Value.CompareTo(1m) switch
    {
        > 0 => Direction.Up,
        < 0 => Direction.Down,
        _ => Direction.Neutral
    };
}

static string DirectionLabel(Direction direction) => direction switch
{
    Direction.Up => "Up",
    Direction.Down => "Down",
    Direction.Neutral => "Tie/Neutral",
    _ => string.Empty
};

static string ResultLabel(ForecastResult result) => result switch
{
    ForecastResult.Success => "Yes",
    ForecastResult.Failure => "No",
    ForecastResult.Tie => "Tie",
    ForecastResult.NoResults => "No results",
    ForecastResult.NoSignal => "No signal",
    _ => string.Empty
};

static string RowCssClass(ForecastResult result) => result switch
{
    ForecastResult.Success => "success",
    ForecastResult.Failure => "failure",
    ForecastResult.Tie => "tie",
    ForecastResult.NoResults or ForecastResult.NoSignal => "no-data",
    _ => string.Empty
};

static string FormatPercent(decimal? value) =>
    value is null ? string.Empty : value.Value.ToString("P2", CultureInfo.InvariantCulture);

static string FormatNullable(decimal? value, string format) =>
    value is null ? string.Empty : value.Value.ToString(format, CultureInfo.InvariantCulture);

static string FormatTimestamp(DateTime? value) =>
    value is null ? string.Empty : value.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

static string Csv(string value)
{
    var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
    return $"\"{escaped}\"";
}

static string Html(string value) => WebUtility.HtmlEncode(value);

static void AppendMeta(StringBuilder html, string label, string value)
{
    html.Append("<div><strong>").Append(Html(label)).Append("</strong></div><div>")
        .Append(Html(value)).AppendLine("</div>");
}

static void AppendCell(StringBuilder html, string value, string? cssClass = null)
{
    html.Append("<td");
    if (!string.IsNullOrWhiteSpace(cssClass))
    {
        html.Append(" class=\"").Append(Html(cssClass)).Append('"');
    }

    html.Append('>').Append(Html(value)).AppendLine("</td>");
}

static string ColumnName(int columnIndex)
{
    var dividend = columnIndex;
    var columnName = string.Empty;
    while (dividend > 0)
    {
        var modulo = (dividend - 1) % 26;
        columnName = Convert.ToChar('A' + modulo) + columnName;
        dividend = (dividend - modulo) / 26;
    }

    return columnName;
}

internal sealed record ReportOptions(
    string OutputDir,
    string CacheDir,
    DateOnly Start,
    DateOnly EndExclusive,
    int MaxParallelism)
{
    public static ReportOptions Parse(string[] args)
    {
        var outputDir = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? Directory.GetCurrentDirectory());
        var start = (DateOnly?)null;
        var endExclusive = (DateOnly?)null;
        var maxParallelism = 8;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg == "--start" && index + 1 < args.Length)
            {
                start = DateOnly.ParseExact(args[++index], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else if (arg == "--end-exclusive" && index + 1 < args.Length)
            {
                endExclusive = DateOnly.ParseExact(args[++index], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else if (arg == "--max-parallelism" && index + 1 < args.Length)
            {
                maxParallelism = int.Parse(args[++index], CultureInfo.InvariantCulture);
            }
        }

        var resolvedEndExclusive = endExclusive ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var resolvedStart = start ?? resolvedEndExclusive.AddMonths(-6);
        if (resolvedStart >= resolvedEndExclusive)
        {
            throw new ArgumentException("Start date must be earlier than end-exclusive date.");
        }

        return new ReportOptions(
            outputDir,
            Path.Combine(outputDir, "cache"),
            resolvedStart,
            resolvedEndExclusive,
            Math.Clamp(maxParallelism, 1, 16));
    }
}

internal sealed record MetricsSignal(
    DateTime? TimestampUtc,
    decimal? CountLongShortRatio,
    decimal? CountTopTraderLongShortRatio,
    decimal? SumTopTraderLongShortRatio,
    decimal? SumTakerLongShortVolRatio,
    decimal? SumOpenInterest,
    Direction ForecastDirection);

internal sealed record OutcomeCounts(
    int UpCount,
    int DownCount,
    int TieCount,
    int TotalCount);

internal sealed record DailyReportRow(
    DateOnly Date,
    MetricsSignal? Metrics,
    OutcomeCounts? Outcomes,
    Direction ActualDirection,
    ForecastResult Result,
    string? Error);

internal sealed record ReportSummary(
    int TotalDays,
    int EvaluatedDays,
    int SuccessDays,
    int FailureDays,
    decimal? HitRate,
    int ForecastUpDays,
    int ForecastDownDays,
    decimal? ForecastUpHitRate,
    decimal? ForecastDownHitRate,
    int MissingMetricsDays,
    int MissingKlinesDays,
    int TieDays,
    decimal? PearsonCorrelation);

internal enum Direction
{
    Missing,
    Neutral,
    Up,
    Down
}

internal enum ForecastResult
{
    NoSignal,
    NoResults,
    Tie,
    Success,
    Failure
}

internal static class Styles
{
    public const uint Title = 1U;
    public const uint Header = 2U;
    public const uint SummaryLabel = 3U;
    public const uint SummaryValue = 4U;
    public const uint Text = 5U;
    public const uint Int = 6U;
    public const uint Decimal = 7U;
}

internal readonly record struct RowStyles(uint Text, uint Int, uint Decimal)
{
    public static RowStyles For(ForecastResult result) => result switch
    {
        ForecastResult.Success => new RowStyles(8U, 9U, 10U),
        ForecastResult.Failure => new RowStyles(11U, 12U, 13U),
        ForecastResult.Tie => new RowStyles(14U, 15U, 16U),
        ForecastResult.NoResults or ForecastResult.NoSignal => new RowStyles(17U, 18U, 19U),
        _ => new RowStyles(Styles.Text, Styles.Int, Styles.Decimal)
    };
}
