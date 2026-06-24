using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

var baseDir = AppContext.BaseDirectory;
var workDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var dailyPath = Path.Combine(workDir, "diff_daily_report_data.tsv");
var summaryPath = Path.Combine(workDir, "diff_summary.tsv");
var outputPath = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(workDir, "diff-strategies-all-time-report-2026-06-09.xlsx");

var dailyRows = LoadDailyRows(dailyPath);
var summaryRows = LoadSummaryRows(summaryPath);
if (dailyRows.Count == 0)
{
    Console.Error.WriteLine("No daily rows found.");
    return 2;
}

if (File.Exists(outputPath))
{
    File.Delete(outputPath);
}

CreateWorkbook(outputPath, dailyRows, summaryRows);
ValidateWorkbook(outputPath);

Console.WriteLine($"output={outputPath}");
Console.WriteLine($"daily_rows={dailyRows.Count}");
Console.WriteLine($"summary_rows={summaryRows.Count}");
Console.WriteLine($"strategies={dailyRows.Select(row => row.StrategyCode).Distinct(StringComparer.OrdinalIgnoreCase).Count()}");
return 0;

static List<DailyRow> LoadDailyRows(string path)
{
    return ReadTsv(path)
        .Where(row => string.Equals(row["row_type"], "DATA", StringComparison.OrdinalIgnoreCase))
        .Select(row => new DailyRow(
            row["sheet_name"],
            row["asset_symbol"],
            row["direction"],
            row["strategy_code"],
            row["strategy_name"],
            row["report_date"],
            ParseInt(row["bet_count"]),
            ParseInt(row["win_count"]),
            ParseInt(row["loss_count"]),
            ParseDecimal(row["pnl_current_usd"]),
            ParseDecimal(row["pnl_no_above_0_5_usd"]),
            ParseDecimal(row["pnl_market_above_0_5_usd"])))
        .OrderBy(row => SheetSortKey(row.SheetName))
        .ThenBy(row => StrategySortKey(row.StrategyCode))
        .ThenBy(row => row.ReportDate, StringComparer.Ordinal)
        .ToList();
}

static List<SummaryRow> LoadSummaryRows(string path)
{
    return ReadTsv(path)
        .Select(row => new SummaryRow(
            row["sheet_name"],
            ParseInt(row["strategies_with_orders"]),
            ParseInt(row["all_orders"]),
            ParseInt(row["included_orders"]),
            ParseInt(row["excluded_unresolved_orders"]),
            ParseInt(row["win_count"]),
            ParseInt(row["loss_count"]),
            ParseInt(row["above_market_orders"]),
            ParseInt(row["self_capped_orders"]),
            ParseDecimal(row["pnl_current_usd"]),
            ParseDecimal(row["pnl_no_above_0_5_usd"]),
            ParseDecimal(row["pnl_market_above_0_5_usd"]),
            row["first_market_start_utc"],
            row["last_market_start_utc"]))
        .OrderBy(row => SheetSortKey(row.SheetName))
        .ToList();
}

static IReadOnlyList<Dictionary<string, string>> ReadTsv(string path)
{
    var lines = File.ReadAllLines(path, Encoding.UTF8)
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .ToArray();
    if (lines.Length == 0)
    {
        return [];
    }

    var headers = lines[0].Split('\t');
    var rows = new List<Dictionary<string, string>>();
    foreach (var line in lines.Skip(1))
    {
        var values = line.Split('\t');
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
        {
            row[headers[i]] = i < values.Length ? values[i] : string.Empty;
        }

        rows.Add(row);
    }

    return rows;
}

static void CreateWorkbook(string outputPath, IReadOnlyList<DailyRow> dailyRows, IReadOnlyList<SummaryRow> summaryRows)
{
    using var document = SpreadsheetDocument.Create(outputPath, SpreadsheetDocumentType.Workbook);
    var workbookPart = document.AddWorkbookPart();
    workbookPart.Workbook = new Workbook();
    var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
    stylesPart.Stylesheet = BuildStylesheet();
    stylesPart.Stylesheet.Save();

    var sheets = workbookPart.Workbook.AppendChild(new Sheets());
    var sheetId = 1U;
    AddWorksheet(workbookPart, sheets, "Summary", sheetId++, BuildSummarySheet(summaryRows));
    AddWorksheet(workbookPart, sheets, "Assumptions", sheetId++, BuildAssumptionsSheet());

    foreach (var sheetName in dailyRows.Select(row => row.SheetName).Distinct().OrderBy(SheetSortKey))
    {
        var rows = dailyRows.Where(row => row.SheetName == sheetName).ToArray();
        AddWorksheet(workbookPart, sheets, sheetName, sheetId++, BuildStrategySheet(sheetName, rows));
    }

    workbookPart.Workbook.Save();
}

static Worksheet BuildSummarySheet(IReadOnlyList<SummaryRow> summaryRows)
{
    var sheetData = new SheetData();
    var merges = new MergeCells();

    AppendRow(sheetData, 1, [TextCell("A", 1, "Diff Strategies All-Time Report", 1)]);
    merges.Append(new MergeCell { Reference = "A1:N1" });
    AppendRow(sheetData, 2, [TextCell("A", 2, "Dates are UTC market-start dates. PnL is USD.", 6)]);
    merges.Append(new MergeCell { Reference = "A2:N2" });

    var headers = new[]
    {
        "Sheet",
        "Strategies",
        "All runs",
        "Settled included",
        "Non-Settled excluded",
        "Wins",
        "Losses",
        ">0.5 orders",
        "Self-capped",
        "Pnl current",
        "Pnl no >0.5",
        "Pnl >0.5 at market",
        "First market UTC",
        "Last market UTC"
    };
    AppendRow(sheetData, 4, HeaderCells(4, headers));

    var rowIndex = 5U;
    foreach (var row in summaryRows)
    {
        AppendRow(sheetData, rowIndex,
        [
            TextCell("A", rowIndex, row.SheetName, 0),
            NumberCell("B", rowIndex, row.StrategiesWithOrders, 7),
            NumberCell("C", rowIndex, row.AllOrders, 7),
            NumberCell("D", rowIndex, row.IncludedOrders, 7),
            NumberCell("E", rowIndex, row.ExcludedUnresolvedOrders, 7),
            NumberCell("F", rowIndex, row.WinCount, 7),
            NumberCell("G", rowIndex, row.LossCount, 7),
            NumberCell("H", rowIndex, row.AboveMarketOrders, 7),
            NumberCell("I", rowIndex, row.SelfCappedOrders, 7),
            NumberCell("J", rowIndex, row.PnlCurrentUsd, 3),
            NumberCell("K", rowIndex, row.PnlNoAbove05Usd, 3),
            NumberCell("L", rowIndex, row.PnlMarketAbove05Usd, 3),
            TextCell("M", rowIndex, row.FirstMarketStartUtc, 0),
            TextCell("N", rowIndex, row.LastMarketStartUtc, 0)
        ]);
        rowIndex++;
    }

    var totalRow = rowIndex + 1;
    AppendRow(sheetData, totalRow,
    [
        TextCell("A", totalRow, "Total", 5),
        NumberCell("B", totalRow, summaryRows.Sum(row => row.StrategiesWithOrders), 8),
        NumberCell("C", totalRow, summaryRows.Sum(row => row.AllOrders), 8),
        NumberCell("D", totalRow, summaryRows.Sum(row => row.IncludedOrders), 8),
        NumberCell("E", totalRow, summaryRows.Sum(row => row.ExcludedUnresolvedOrders), 8),
        NumberCell("F", totalRow, summaryRows.Sum(row => row.WinCount), 8),
        NumberCell("G", totalRow, summaryRows.Sum(row => row.LossCount), 8),
        NumberCell("H", totalRow, summaryRows.Sum(row => row.AboveMarketOrders), 8),
        NumberCell("I", totalRow, summaryRows.Sum(row => row.SelfCappedOrders), 8),
        NumberCell("J", totalRow, summaryRows.Sum(row => row.PnlCurrentUsd), 4),
        NumberCell("K", totalRow, summaryRows.Sum(row => row.PnlNoAbove05Usd), 4),
        NumberCell("L", totalRow, summaryRows.Sum(row => row.PnlMarketAbove05Usd), 4),
        TextCell("M", totalRow, string.Empty, 5),
        TextCell("N", totalRow, string.Empty, 5)
    ]);

    return CreateWorksheet(sheetData, merges, [
        Width(1, 18),
        Width(2, 12),
        Width(3, 12),
        Width(4, 12),
        Width(5, 18),
        Width(6, 10),
        Width(7, 10),
        Width(8, 13),
        Width(9, 13),
        Width(10, 15),
        Width(11, 16),
        Width(12, 20),
        Width(13, 22),
        Width(14, 22)
    ]);
}

static Worksheet BuildAssumptionsSheet()
{
    var sheetData = new SheetData();
    var merges = new MergeCells();
    AppendRow(sheetData, 1, [TextCell("A", 1, "Report Assumptions", 1)]);
    merges.Append(new MergeCell { Reference = "A1:B1" });

    var rows = new (string Label, string Value)[]
    {
        ("Data source", "Production PostgreSQL read-only export from paper_orders, strategies, strategy_market_paper_runs, and BTC/ETH/SOL 5m result tables."),
        ("Scope", "All Diff strategies with at least one included Paper order/run; grouped by asset + direction sheets."),
        ("Date rows", "UTC market-start date."),
        ("Bet/win/loss counts", "Only strategy_market_paper_runs.status = Settled rows are included. Paper not accepted, Skipped, Entered, and other non-settled rows are excluded. Wins/losses are based on selected outcome versus market winner when available, otherwise realized PnL sign."),
        ("Pnl current", "Actual realized Paper PnL from settled strategy_market_paper_runs rows."),
        ("Pnl no >0.5", "Rows whose raw market limit was above 0.50 are treated as skipped with zero PnL; all other rows keep current PnL."),
        ("Pnl >0.5 at market", "Rows already placed at market keep current PnL; self-capped rows are simulated at raw market limit using code-equivalent min-order and rounding logic."),
        ("Self-capped marker", "paper_orders.raw_decision_json instant_resting_at_max_price = true."),
        ("Market result source", "crypto_up_down_5m_result_polling_observations first, then crypto_up_down_5m_websocket_resolved_markets."),
        ("Excluded rows", "Non-settled rows are excluded from strategy tables, counts, wins/losses, and PnL columns.")
    };

    AppendRow(sheetData, 3, HeaderCells(3, ["Item", "Value"]));
    var rowIndex = 4U;
    foreach (var row in rows)
    {
        AppendRow(sheetData, rowIndex,
        [
            TextCell("A", rowIndex, row.Label, 2),
            TextCell("B", rowIndex, row.Value, 6)
        ]);
        rowIndex++;
    }

    return CreateWorksheet(sheetData, merges, [Width(1, 28), Width(2, 115)]);
}

static Worksheet BuildStrategySheet(string sheetName, IReadOnlyList<DailyRow> rows)
{
    var sheetData = new SheetData();
    var merges = new MergeCells();

    AppendRow(sheetData, 1, [TextCell("A", 1, sheetName, 1)]);
    merges.Append(new MergeCell { Reference = "A1:G1" });
    AppendRow(sheetData, 2, [TextCell("A", 2, "Each strategy table includes UTC daily counts and PnL by scenario.", 6)]);
    merges.Append(new MergeCell { Reference = "A2:G2" });

    var rowIndex = 4U;
    foreach (var group in rows
        .GroupBy(row => new { row.StrategyCode, row.StrategyName })
        .OrderBy(group => StrategySortKey(group.Key.StrategyCode)))
    {
        var strategyRows = group.OrderBy(row => row.ReportDate, StringComparer.Ordinal).ToArray();
        AppendRow(sheetData, rowIndex, [TextCell("A", rowIndex, group.Key.StrategyName, 1)]);
        merges.Append(new MergeCell { Reference = $"A{rowIndex}:G{rowIndex}" });
        rowIndex++;

        AppendRow(sheetData, rowIndex, HeaderCells(rowIndex, [
            "Дата",
            "Количество ставок",
            "Выигрыши",
            "Проигрыши",
            "Pnl текущий",
            "Pnl без >0.5",
            "Pnl >0.5 по рынку"
        ]));
        rowIndex++;

        foreach (var row in strategyRows)
        {
            AppendRow(sheetData, rowIndex,
            [
                TextCell("A", rowIndex, row.ReportDate, 0),
                NumberCell("B", rowIndex, row.BetCount, 7),
                NumberCell("C", rowIndex, row.WinCount, 7),
                NumberCell("D", rowIndex, row.LossCount, 7),
                NumberCell("E", rowIndex, row.PnlCurrentUsd, 3),
                NumberCell("F", rowIndex, row.PnlNoAbove05Usd, 3),
                NumberCell("G", rowIndex, row.PnlMarketAbove05Usd, 3)
            ]);
            rowIndex++;
        }

        AppendRow(sheetData, rowIndex,
        [
            TextCell("A", rowIndex, "Total", 5),
            NumberCell("B", rowIndex, strategyRows.Sum(row => row.BetCount), 8),
            NumberCell("C", rowIndex, strategyRows.Sum(row => row.WinCount), 8),
            NumberCell("D", rowIndex, strategyRows.Sum(row => row.LossCount), 8),
            NumberCell("E", rowIndex, strategyRows.Sum(row => row.PnlCurrentUsd), 4),
            NumberCell("F", rowIndex, strategyRows.Sum(row => row.PnlNoAbove05Usd), 4),
            NumberCell("G", rowIndex, strategyRows.Sum(row => row.PnlMarketAbove05Usd), 4)
        ]);
        rowIndex += 2;
    }

    return CreateWorksheet(sheetData, merges, [
        Width(1, 14),
        Width(2, 20),
        Width(3, 12),
        Width(4, 12),
        Width(5, 15),
        Width(6, 16),
        Width(7, 20)
    ]);
}

static Worksheet CreateWorksheet(SheetData sheetData, MergeCells merges, IEnumerable<Column> columns)
{
    var sheetViews = new SheetViews(new SheetView { WorkbookViewId = 0U, ShowGridLines = false });
    var cols = new Columns(columns);
    var worksheet = new Worksheet(sheetViews, cols, sheetData);
    if (merges.HasChildren)
    {
        worksheet.Append(merges);
    }

    return worksheet;
}

static void AddWorksheet(WorkbookPart workbookPart, Sheets sheets, string sheetName, uint sheetId, Worksheet worksheet)
{
    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    worksheetPart.Worksheet = worksheet;
    worksheetPart.Worksheet.Save();
    var relationshipId = workbookPart.GetIdOfPart(worksheetPart);
    sheets.Append(new Sheet { Id = relationshipId, SheetId = sheetId, Name = sheetName });
}

static Stylesheet BuildStylesheet()
{
    var fonts = new Fonts(
        new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
        new Font(new Bold(), new FontSize { Val = 14 }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Calibri" }),
        new Font(new Bold(), new FontSize { Val = 11 }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Calibri" }),
        new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }));

    var fills = new Fills(
        new Fill(new PatternFill { PatternType = PatternValues.None }),
        new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
        new Fill(new PatternFill(new ForegroundColor { Rgb = "FF1F4E78" }) { PatternType = PatternValues.Solid }),
        new Fill(new PatternFill(new ForegroundColor { Rgb = "FF5B9BD5" }) { PatternType = PatternValues.Solid }),
        new Fill(new PatternFill(new ForegroundColor { Rgb = "FFE2F0D9" }) { PatternType = PatternValues.Solid }));

    var borders = new Borders(
        new Border(),
        new Border(
            new LeftBorder(new Color { Rgb = "FFD9E2F3" }) { Style = BorderStyleValues.Thin },
            new RightBorder(new Color { Rgb = "FFD9E2F3" }) { Style = BorderStyleValues.Thin },
            new TopBorder(new Color { Rgb = "FFD9E2F3" }) { Style = BorderStyleValues.Thin },
            new BottomBorder(new Color { Rgb = "FFD9E2F3" }) { Style = BorderStyleValues.Thin },
            new DiagonalBorder()));

    var numberingFormats = new NumberingFormats(
        new NumberingFormat { NumberFormatId = 164U, FormatCode = "$#,##0.0000;[Red]-$#,##0.0000" },
        new NumberingFormat { NumberFormatId = 165U, FormatCode = "0" });

    var cellFormats = new CellFormats(
        new CellFormat(), // 0 default
        new CellFormat { FontId = 1U, FillId = 2U, BorderId = 1U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center } },
        new CellFormat { FontId = 2U, FillId = 3U, BorderId = 1U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center } },
        new CellFormat { NumberFormatId = 164U, FontId = 0U, FillId = 0U, BorderId = 1U, ApplyNumberFormat = true, ApplyBorder = true },
        new CellFormat { NumberFormatId = 164U, FontId = 3U, FillId = 4U, BorderId = 1U, ApplyNumberFormat = true, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
        new CellFormat { FontId = 3U, FillId = 4U, BorderId = 1U, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
        new CellFormat { FontId = 0U, FillId = 0U, BorderId = 0U, ApplyAlignment = true, Alignment = new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top } },
        new CellFormat { NumberFormatId = 165U, FontId = 0U, FillId = 0U, BorderId = 1U, ApplyNumberFormat = true, ApplyBorder = true },
        new CellFormat { NumberFormatId = 165U, FontId = 3U, FillId = 4U, BorderId = 1U, ApplyNumberFormat = true, ApplyFont = true, ApplyFill = true, ApplyBorder = true });

    return new Stylesheet(numberingFormats, fonts, fills, borders, cellFormats);
}

static IReadOnlyList<Cell> HeaderCells(uint rowIndex, IReadOnlyList<string> headers)
{
    return headers.Select((header, index) => TextCell(ColumnName(index + 1), rowIndex, header, 2)).ToArray();
}

static void AppendRow(SheetData sheetData, uint rowIndex, IReadOnlyList<Cell> cells)
{
    var row = new Row { RowIndex = rowIndex };
    row.Append(cells);
    sheetData.Append(row);
}

static Cell TextCell(string columnName, uint rowIndex, string value, uint styleIndex)
{
    return new Cell
    {
        CellReference = columnName + rowIndex.ToString(CultureInfo.InvariantCulture),
        DataType = CellValues.InlineString,
        StyleIndex = styleIndex,
        InlineString = new InlineString(new Text(value ?? string.Empty))
    };
}

static Cell NumberCell(string columnName, uint rowIndex, decimal value, uint styleIndex)
{
    return new Cell
    {
        CellReference = columnName + rowIndex.ToString(CultureInfo.InvariantCulture),
        DataType = CellValues.Number,
        StyleIndex = styleIndex,
        CellValue = new CellValue(value.ToString("0.########", CultureInfo.InvariantCulture))
    };
}

static Column Width(uint column, double width)
{
    return new Column { Min = column, Max = column, Width = width, CustomWidth = true };
}

static string ColumnName(int index)
{
    var name = string.Empty;
    while (index > 0)
    {
        var modulo = (index - 1) % 26;
        name = Convert.ToChar('A' + modulo) + name;
        index = (index - modulo) / 26;
    }

    return name;
}

static decimal ParseDecimal(string value)
{
    return decimal.Parse(value, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
}

static int ParseInt(string value)
{
    return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
}

static int SheetSortKey(string sheetName)
{
    return sheetName.ToUpperInvariant() switch
    {
        "BTC_UP" => 10,
        "BTC_DOWN" => 11,
        "ETH_UP" => 20,
        "ETH_DOWN" => 21,
        "SOL_UP" => 30,
        "SOL_DOWN" => 31,
        _ => 100
    };
}

static (string Prefix, int Threshold, string Direction) StrategySortKey(string strategyCode)
{
    var parts = strategyCode.Split('_');
    var threshold = parts.Length > 6 && int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        ? value
        : int.MaxValue;
    var direction = parts.Length > 4 ? parts[4] : string.Empty;
    return (strategyCode[..Math.Min(strategyCode.Length, 32)], threshold, direction);
}

static void ValidateWorkbook(string path)
{
    using var document = SpreadsheetDocument.Open(path, false);
    var validator = new OpenXmlValidator();
    var errors = validator.Validate(document).Take(10).ToArray();
    if (errors.Length > 0)
    {
        foreach (var error in errors)
        {
            Console.Error.WriteLine(error.Description);
        }

        throw new InvalidOperationException($"Workbook validation failed with {errors.Length} error(s).");
    }
}

sealed record DailyRow(
    string SheetName,
    string AssetSymbol,
    string Direction,
    string StrategyCode,
    string StrategyName,
    string ReportDate,
    int BetCount,
    int WinCount,
    int LossCount,
    decimal PnlCurrentUsd,
    decimal PnlNoAbove05Usd,
    decimal PnlMarketAbove05Usd);

sealed record SummaryRow(
    string SheetName,
    int StrategiesWithOrders,
    int AllOrders,
    int IncludedOrders,
    int ExcludedUnresolvedOrders,
    int WinCount,
    int LossCount,
    int AboveMarketOrders,
    int SelfCappedOrders,
    decimal PnlCurrentUsd,
    decimal PnlNoAbove05Usd,
    decimal PnlMarketAbove05Usd,
    string FirstMarketStartUtc,
    string LastMarketStartUtc);
