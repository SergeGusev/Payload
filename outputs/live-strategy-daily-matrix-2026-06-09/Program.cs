using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

var workDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var outputPath = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(workDir, "live-strategy-daily-matrix-with-totals-2026-06-09.xlsx");

var strategies = LoadStrategies(Path.Combine(workDir, "live_strategies.tsv"));
var dailyRows = LoadDailyRows(Path.Combine(workDir, "live_daily_pnl.tsv"));
var meta = LoadMeta(Path.Combine(workDir, "report_meta.tsv"));

if (strategies.Count == 0)
{
    Console.Error.WriteLine("No current live strategies found.");
    return 2;
}

var dates = BuildDateRange(dailyRows);
if (File.Exists(outputPath))
{
    File.Delete(outputPath);
}

CreateWorkbook(outputPath, strategies, dailyRows, dates);
ValidateWorkbook(outputPath);

Console.WriteLine($"output={outputPath}");
Console.WriteLine($"generated_at_utc={meta.GetValueOrDefault("generated_at_utc", string.Empty)}");
Console.WriteLine($"strategies={strategies.Count}");
Console.WriteLine($"dates={dates.Count}");
Console.WriteLine($"settled_live_orders={meta.GetValueOrDefault("settled_live_orders", string.Empty)}");
Console.WriteLine($"pnl_usd={meta.GetValueOrDefault("pnl_usd", string.Empty)}");
return 0;

static List<StrategyRow> LoadStrategies(string path)
{
    return ReadTsv(path)
        .Select(row => new StrategyRow(
            row["code"],
            row["name"],
            ParseBool(row["enabled"]),
            ParseBool(row["live_stakes"]),
            ParseBool(row["auto_live_paused"]),
            row["live_enabled_utc"]))
        .OrderBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static List<DailyPnlRow> LoadDailyRows(string path)
{
    return ReadTsv(path)
        .Select(row => new DailyPnlRow(
            DateOnly.ParseExact(row["settlement_date_utc"], "yyyy-MM-dd", CultureInfo.InvariantCulture),
            row["strategy_code"],
            ParseInt(row["settled_orders"]),
            ParseDecimal(row["pnl_usd"])))
        .OrderBy(row => row.SettlementDateUtc)
        .ThenBy(row => row.StrategyCode, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static Dictionary<string, string> LoadMeta(string path)
{
    return ReadTsv(path).FirstOrDefault() ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

static List<DateOnly> BuildDateRange(IReadOnlyList<DailyPnlRow> dailyRows)
{
    if (dailyRows.Count == 0)
    {
        return [DateOnly.FromDateTime(DateTime.UtcNow)];
    }

    var minDate = dailyRows.Min(row => row.SettlementDateUtc);
    var maxDate = dailyRows.Max(row => row.SettlementDateUtc);
    var dates = new List<DateOnly>();
    for (var date = minDate; date <= maxDate; date = date.AddDays(1))
    {
        dates.Add(date);
    }

    return dates;
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
        for (var index = 0; index < headers.Length; index++)
        {
            row[headers[index]] = index < values.Length ? values[index] : string.Empty;
        }

        rows.Add(row);
    }

    return rows;
}

static void CreateWorkbook(
    string outputPath,
    IReadOnlyList<StrategyRow> strategies,
    IReadOnlyList<DailyPnlRow> dailyRows,
    IReadOnlyList<DateOnly> dates)
{
    using var document = SpreadsheetDocument.Create(outputPath, SpreadsheetDocumentType.Workbook);
    var workbookPart = document.AddWorkbookPart();
    workbookPart.Workbook = new Workbook();

    var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
    stylesPart.Stylesheet = BuildStylesheet();
    stylesPart.Stylesheet.Save();

    var sheets = workbookPart.Workbook.AppendChild(new Sheets());
    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    worksheetPart.Worksheet = BuildMatrixSheet(strategies, dailyRows, dates);
    worksheetPart.Worksheet.Save();

    var relationshipId = workbookPart.GetIdOfPart(worksheetPart);
    sheets.Append(new Sheet
    {
        Id = relationshipId,
        SheetId = 1U,
        Name = "Live Daily PnL"
    });

    workbookPart.Workbook.CalculationProperties = new CalculationProperties
    {
        CalculationMode = CalculateModeValues.Auto,
        FullCalculationOnLoad = true,
        ForceFullCalculation = true
    };
    workbookPart.Workbook.Save();
}

static Worksheet BuildMatrixSheet(
    IReadOnlyList<StrategyRow> strategies,
    IReadOnlyList<DailyPnlRow> dailyRows,
    IReadOnlyList<DateOnly> dates)
{
    var pnlByDateAndStrategy = dailyRows.ToDictionary(
        row => (row.SettlementDateUtc, row.StrategyCode),
        row => row.PnlUsd);

    var lastStrategyColumnName = ColumnName(strategies.Count + 1);
    var totalColumnName = ColumnName(strategies.Count + 2);
    var sheetData = new SheetData();
    var headerCells = new List<Cell> { TextCell("A", 1U, "DateUtc", 1U) };
    for (var index = 0; index < strategies.Count; index++)
    {
        headerCells.Add(TextCell(ColumnName(index + 2), 1U, strategies[index].Name, 1U));
    }

    headerCells.Add(TextCell(totalColumnName, 1U, "Total", 1U));

    AppendRow(sheetData, 1U, headerCells);

    var rowIndex = 2U;
    foreach (var date in dates)
    {
        var cells = new List<Cell> { TextCell("A", rowIndex, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 2U) };
        var rowTotal = 0m;
        for (var strategyIndex = 0; strategyIndex < strategies.Count; strategyIndex++)
        {
            var strategy = strategies[strategyIndex];
            pnlByDateAndStrategy.TryGetValue((date, strategy.Code), out var pnlUsd);
            rowTotal += pnlUsd;
            cells.Add(NumberCell(ColumnName(strategyIndex + 2), rowIndex, pnlUsd, 3U));
        }

        cells.Add(FormulaCell(
            totalColumnName,
            rowIndex,
            $"SUM(B{rowIndex}:{lastStrategyColumnName}{rowIndex})",
            rowTotal,
            4U));
        AppendRow(sheetData, rowIndex, cells);
        rowIndex++;
    }

    var totalRowIndex = rowIndex;
    var totalCells = new List<Cell> { TextCell("A", totalRowIndex, "Total", 1U) };
    var grandTotal = 0m;
    for (var strategyIndex = 0; strategyIndex < strategies.Count; strategyIndex++)
    {
        var strategy = strategies[strategyIndex];
        var columnName = ColumnName(strategyIndex + 2);
        var strategyTotal = dates.Sum(date =>
            pnlByDateAndStrategy.TryGetValue((date, strategy.Code), out var pnlUsd) ? pnlUsd : 0m);
        grandTotal += strategyTotal;
        totalCells.Add(FormulaCell(
            columnName,
            totalRowIndex,
            $"SUM({columnName}2:{columnName}{totalRowIndex - 1})",
            strategyTotal,
            4U));
    }

    totalCells.Add(FormulaCell(
        totalColumnName,
        totalRowIndex,
        $"SUM(B{totalRowIndex}:{lastStrategyColumnName}{totalRowIndex})",
        grandTotal,
        4U));
    AppendRow(sheetData, totalRowIndex, totalCells);

    var worksheet = new Worksheet(
        BuildSheetViews(),
        BuildColumns(strategies),
        sheetData,
        new AutoFilter { Reference = $"A1:{totalColumnName}{Math.Max(1, dates.Count + 1)}" },
        new PageMargins { Left = 0.7D, Right = 0.7D, Top = 0.75D, Bottom = 0.75D, Header = 0.3D, Footer = 0.3D });

    return worksheet;
}

static SheetViews BuildSheetViews()
{
    return new SheetViews(
        new SheetView(
            new Pane
            {
                HorizontalSplit = 1D,
                VerticalSplit = 1D,
                TopLeftCell = "B2",
                ActivePane = PaneValues.BottomRight,
                State = PaneStateValues.Frozen
            },
            new Selection
            {
                Pane = PaneValues.BottomRight,
                ActiveCell = "B2",
                SequenceOfReferences = new ListValue<StringValue> { InnerText = "B2" }
            })
        { WorkbookViewId = 0U });
}

static Columns BuildColumns(IReadOnlyList<StrategyRow> strategies)
{
    var columns = new Columns();
    columns.Append(new Column { Min = 1U, Max = 1U, Width = 13D, CustomWidth = true });
    for (var index = 0; index < strategies.Count; index++)
    {
        var width = Math.Clamp(strategies[index].Name.Length + 2D, 20D, 44D);
        var columnIndex = (uint)(index + 2);
        columns.Append(new Column { Min = columnIndex, Max = columnIndex, Width = width, CustomWidth = true });
    }

    var totalColumnIndex = (uint)(strategies.Count + 2);
    columns.Append(new Column { Min = totalColumnIndex, Max = totalColumnIndex, Width = 16D, CustomWidth = true });

    return columns;
}

static Stylesheet BuildStylesheet()
{
    var numberFormats = new NumberingFormats(
        new NumberingFormat
        {
            NumberFormatId = 164U,
            FormatCode = "0.00000000;[Red]-0.00000000;0.00000000"
        })
    { Count = 1U };

    var fonts = new Fonts(
        new Font(
            new FontSize { Val = 11D },
            new Color { Rgb = "FF1F2937" },
            new FontName { Val = "Calibri" }),
        new Font(
            new Bold(),
            new FontSize { Val = 11D },
            new Color { Rgb = "FFFFFFFF" },
            new FontName { Val = "Calibri" }))
    { Count = 2U };

    var fills = new Fills(
        new Fill(new PatternFill { PatternType = PatternValues.None }),
        new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
        new Fill(new PatternFill(new ForegroundColor { Rgb = "FF1F4E79" }) { PatternType = PatternValues.Solid }))
    { Count = 3U };

    var borders = new Borders(
        new Border(),
        new Border(
            new LeftBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFD9E2EC" } },
            new RightBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFD9E2EC" } },
            new TopBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFD9E2EC" } },
            new BottomBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFD9E2EC" } },
            new DiagonalBorder()))
    { Count = 2U };

    var cellFormats = new CellFormats(
        new CellFormat(),
        new CellFormat
        {
            FontId = 1U,
            FillId = 2U,
            BorderId = 1U,
            ApplyFont = true,
            ApplyFill = true,
            ApplyBorder = true,
            Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center, WrapText = true },
            ApplyAlignment = true
        },
        new CellFormat
        {
            BorderId = 1U,
            ApplyBorder = true,
            Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Left },
            ApplyAlignment = true
        },
        new CellFormat
        {
            NumberFormatId = 164U,
            BorderId = 1U,
            ApplyNumberFormat = true,
            ApplyBorder = true,
            Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right },
            ApplyAlignment = true
        },
        new CellFormat
        {
            FontId = 1U,
            FillId = 2U,
            NumberFormatId = 164U,
            BorderId = 1U,
            ApplyFont = true,
            ApplyFill = true,
            ApplyNumberFormat = true,
            ApplyBorder = true,
            Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right },
            ApplyAlignment = true
        })
    { Count = 5U };

    return new Stylesheet(numberFormats, fonts, fills, borders, cellFormats);
}

static void AppendRow(SheetData sheetData, uint rowIndex, IEnumerable<Cell> cells)
{
    sheetData.Append(new Row(cells) { RowIndex = rowIndex });
}

static Cell TextCell(string columnName, uint rowIndex, string value, uint styleIndex)
{
    return new Cell
    {
        CellReference = $"{columnName}{rowIndex}",
        DataType = CellValues.InlineString,
        StyleIndex = styleIndex,
        InlineString = new InlineString(new Text(value ?? string.Empty))
    };
}

static Cell NumberCell(string columnName, uint rowIndex, decimal value, uint styleIndex)
{
    return new Cell
    {
        CellReference = $"{columnName}{rowIndex}",
        StyleIndex = styleIndex,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
    };
}

static Cell FormulaCell(string columnName, uint rowIndex, string formula, decimal cachedValue, uint styleIndex)
{
    return new Cell
    {
        CellReference = $"{columnName}{rowIndex}",
        StyleIndex = styleIndex,
        CellFormula = new CellFormula(formula),
        CellValue = new CellValue(cachedValue.ToString(CultureInfo.InvariantCulture))
    };
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

static void ValidateWorkbook(string path)
{
    using var document = SpreadsheetDocument.Open(path, false);
    var validator = new OpenXmlValidator();
    var errors = validator.Validate(document).ToList();
    if (errors.Count == 0)
    {
        return;
    }

    foreach (var error in errors.Take(10))
    {
        Console.Error.WriteLine($"{error.Path?.XPath}: {error.Description}");
    }

    throw new InvalidOperationException($"OpenXML validation failed with {errors.Count} error(s).");
}

static bool ParseBool(string value)
{
    return bool.TryParse(value, out var parsed) && parsed;
}

static int ParseInt(string value)
{
    return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
}

static decimal ParseDecimal(string value)
{
    return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}

internal sealed record StrategyRow(
    string Code,
    string Name,
    bool Enabled,
    bool LiveStakes,
    bool AutoLivePaused,
    string LiveEnabledUtc);

internal sealed record DailyPnlRow(
    DateOnly SettlementDateUtc,
    string StrategyCode,
    int SettledOrders,
    decimal PnlUsd);
