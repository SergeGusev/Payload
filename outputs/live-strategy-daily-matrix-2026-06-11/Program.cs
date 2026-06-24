using System.Globalization;
using System.Data;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Npgsql;

var outputDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
Directory.CreateDirectory(outputDir);

var outputPath = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(outputDir, "live-strategy-daily-matrix-2026-06-11.xlsx");

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 2;
}

var capturedAtUtc = DateTimeOffset.UtcNow;
var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    Host = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE") ?? "192.168.0.101",
    Timeout = 10,
    CommandTimeout = 120,
    ApplicationName = "LiveStrategyDailyMatrix"
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await using (var readOnlyCommand = connection.CreateCommand())
{
    readOnlyCommand.Transaction = transaction;
    readOnlyCommand.CommandText = "SET TRANSACTION READ ONLY";
    await readOnlyCommand.ExecuteNonQueryAsync();
}

var strategies = await LoadCurrentLiveStrategiesAsync(connection, transaction);
var dailyRows = await LoadDailyPnlRowsAsync(connection, transaction);
var meta = await LoadReportMetaAsync(connection, transaction);
await transaction.CommitAsync();
var dates = BuildDateRange(dailyRows);

if (strategies.Count == 0)
{
    Console.Error.WriteLine("No current live strategies found.");
    return 2;
}

if (File.Exists(outputPath))
{
    File.Delete(outputPath);
}

CreateWorkbook(outputPath, strategies, dailyRows, dates);
ValidateWorkbook(outputPath);
VerifyWorkbook(outputPath, strategies, dates, meta.PnlUsd);

Console.WriteLine($"output={outputPath}");
Console.WriteLine($"captured_at_utc={capturedAtUtc:O}");
Console.WriteLine($"strategies={strategies.Count}");
Console.WriteLine($"dates={dates.Count}");
Console.WriteLine($"settled_live_orders={meta.SettledLiveOrders}");
Console.WriteLine($"pnl_usd={meta.PnlUsd:0.########}");
Console.WriteLine($"first_settlement_utc={meta.FirstSettlementUtc}");
Console.WriteLine($"last_settlement_utc={meta.LastSettlementUtc}");
return 0;

static async Task<List<StrategyRow>> LoadCurrentLiveStrategiesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
{
    const string sql = """
        SELECT code, name
        FROM strategies
        WHERE live_stakes
        ORDER BY code;
        """;

    var rows = new List<StrategyRow>();
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    command.CommandTimeout = 60;

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new StrategyRow(reader.GetString(0), reader.GetString(1)));
    }

    return rows;
}

static async Task<List<DailyPnlRow>> LoadDailyPnlRowsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
{
    const string sql = """
        SELECT
            to_char((live_order.settled_at_utc AT TIME ZONE 'utc')::date, 'YYYY-MM-DD') AS settlement_date_utc,
            strategy.code,
            count(*)::integer AS settled_orders,
            COALESCE(sum(live_order.realized_pnl_usd), 0)::numeric AS pnl_usd
        FROM live_orders live_order
        JOIN strategies strategy ON strategy.id = live_order.strategy_id
        WHERE strategy.live_stakes
          AND live_order.settled_at_utc IS NOT NULL
          AND live_order.realized_pnl_usd IS NOT NULL
        GROUP BY 1, 2
        ORDER BY 1, 2;
        """;

    var rows = new List<DailyPnlRow>();
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    command.CommandTimeout = 120;

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new DailyPnlRow(
            DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetDecimal(3)));
    }

    return rows;
}

static async Task<ReportMeta> LoadReportMetaAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
{
    const string sql = """
        SELECT
            count(*)::integer AS settled_live_orders,
            COALESCE(sum(live_order.realized_pnl_usd), 0)::numeric AS pnl_usd,
            COALESCE(to_char(min(live_order.settled_at_utc) AT TIME ZONE 'utc', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'), '') AS first_settlement_utc,
            COALESCE(to_char(max(live_order.settled_at_utc) AT TIME ZONE 'utc', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'), '') AS last_settlement_utc
        FROM live_orders live_order
        JOIN strategies strategy ON strategy.id = live_order.strategy_id
        WHERE strategy.live_stakes
          AND live_order.settled_at_utc IS NOT NULL
          AND live_order.realized_pnl_usd IS NOT NULL;
        """;

    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    command.CommandTimeout = 120;

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return new ReportMeta(0, 0m, string.Empty, string.Empty);
    }

    return new ReportMeta(
        reader.GetInt32(0),
        reader.GetDecimal(1),
        reader.GetString(2),
        reader.GetString(3));
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

    sheets.Append(new Sheet
    {
        Id = workbookPart.GetIdOfPart(worksheetPart),
        SheetId = 1U,
        Name = "Live Daily PnL"
    });

    workbookPart.Workbook.Append(new CalculationProperties
    {
        CalculationMode = CalculateModeValues.Auto,
        FullCalculationOnLoad = true,
        ForceFullCalculation = true
    });
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
        var cells = new List<Cell>
        {
            TextCell("A", rowIndex, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 2U)
        };
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

    return new Worksheet(
        BuildSheetViews(),
        BuildColumns(strategies),
        sheetData,
        new AutoFilter { Reference = $"A1:{totalColumnName}{Math.Max(1, dates.Count + 1)}" },
        new PageMargins { Left = 0.7D, Right = 0.7D, Top = 0.75D, Bottom = 0.75D, Header = 0.3D, Footer = 0.3D });
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

static void VerifyWorkbook(
    string path,
    IReadOnlyList<StrategyRow> strategies,
    IReadOnlyList<DateOnly> dates,
    decimal expectedGrandTotal)
{
    using var document = SpreadsheetDocument.Open(path, false);
    var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("Workbook part is missing.");
    var workbook = workbookPart.Workbook ?? throw new InvalidOperationException("Workbook is missing.");
    var sheets = workbook.Sheets?.Elements<Sheet>().ToArray() ?? [];
    if (sheets.Length != 1 || sheets[0].Name?.Value != "Live Daily PnL")
    {
        throw new InvalidOperationException("Workbook must contain exactly one sheet named Live Daily PnL.");
    }

    var relationshipId = sheets[0].Id?.Value ?? throw new InvalidOperationException("Worksheet relationship id is missing.");
    var worksheetPart = (WorksheetPart)workbookPart.GetPartById(relationshipId);
    var worksheet = worksheetPart.Worksheet ?? throw new InvalidOperationException("Worksheet is missing.");
    var cells = worksheet.Descendants<Cell>().ToDictionary(cell => cell.CellReference?.Value ?? string.Empty, StringComparer.Ordinal);
    var totalColumnName = ColumnName(strategies.Count + 2);
    var totalRowIndex = (uint)(dates.Count + 2);
    var formulaCount = cells.Values.Count(cell => cell.CellFormula is not null);
    var expectedFormulaCount = dates.Count + strategies.Count + 1;
    if (formulaCount != expectedFormulaCount)
    {
        throw new InvalidOperationException($"Unexpected formula count: expected {expectedFormulaCount}, got {formulaCount}.");
    }

    var totalHeader = GetText(cells[$"{totalColumnName}1"]);
    if (!string.Equals(totalHeader, "Total", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Total column header is missing.");
    }

    var totalRowLabel = GetText(cells[$"A{totalRowIndex}"]);
    if (!string.Equals(totalRowLabel, "Total", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Total row label is missing.");
    }

    var grandTotalCell = cells[$"{totalColumnName}{totalRowIndex}"];
    var cachedGrandTotal = decimal.Parse(grandTotalCell.CellValue?.Text ?? "0", CultureInfo.InvariantCulture);
    if (Math.Abs(cachedGrandTotal - expectedGrandTotal) > 0.00000001m)
    {
        throw new InvalidOperationException($"Grand total mismatch: expected {expectedGrandTotal}, got {cachedGrandTotal}.");
    }
}

static string GetText(Cell cell)
{
    return cell.InlineString?.Text?.Text ?? cell.CellValue?.Text ?? string.Empty;
}

internal sealed record StrategyRow(string Code, string Name);

internal sealed record DailyPnlRow(
    DateOnly SettlementDateUtc,
    string StrategyCode,
    int SettledOrders,
    decimal PnlUsd);

internal sealed record ReportMeta(
    int SettledLiveOrders,
    decimal PnlUsd,
    string FirstSettlementUtc,
    string LastSettlementUtc);
