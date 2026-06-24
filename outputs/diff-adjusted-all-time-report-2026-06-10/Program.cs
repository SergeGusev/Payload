using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Npgsql;

var outputDir = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Directory.GetCurrentDirectory();
Directory.CreateDirectory(outputDir);

var outputPath = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(outputDir, "diff-adjusted-all-time-report-2026-06-10.xlsx");

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 2;
}

var capturedAtUtc = DateTimeOffset.UtcNow;
var settledRuns = await LoadSettledRunsAsync(connectionString);
var strategyTables = BuildStrategyTables(settledRuns);
var sheetSummaries = BuildSheetSummaries(strategyTables);

if (File.Exists(outputPath))
{
    File.Delete(outputPath);
}

CreateWorkbook(outputPath, capturedAtUtc, strategyTables, sheetSummaries);
ValidateWorkbook(outputPath);
VerifyWorkbook(outputPath, strategyTables);

Console.WriteLine($"output={outputPath}");
Console.WriteLine($"captured_at_utc={capturedAtUtc:O}");
Console.WriteLine($"settled_runs={settledRuns.Count}");
Console.WriteLine($"strategies_with_settled={strategyTables.Count}");
Console.WriteLine($"detail_sheets={SheetDefinitions.All.Count}");
Console.WriteLine($"formula_cells={5 + strategyTables.Count * 4}");
return 0;

static async Task<List<SettledRun>> LoadSettledRunsAsync(string connectionString)
{
    const string sql = """
        WITH diff_strategies AS MATERIALIZED (
            SELECT
                id,
                code,
                name,
                upper(substring(code from '^(btc|eth|sol)')) AS asset_symbol
            FROM strategies
            WHERE code ~* '^(btc|eth|sol)_up_down_5m_(up|down)_(adjusted_diff|diff)_[0-9]+_instant$'
        )
        SELECT
            ds.code,
            ds.name,
            run.market_start_utc,
            run.settled_at_utc,
            run.selected_outcome,
            run.realized_pnl_usd,
            COALESCE(poll.winning_outcome, ws.winning_outcome) AS winning_outcome
        FROM diff_strategies ds
        JOIN strategy_market_paper_runs run
          ON run.strategy_id = ds.id
         AND run.status = 'Settled'
         AND run.settled_at_utc IS NOT NULL
         AND run.realized_pnl_usd IS NOT NULL
        LEFT JOIN crypto_up_down_5m_result_polling_observations poll
          ON poll.asset_symbol = ds.asset_symbol
         AND poll.market_start_utc = run.market_start_utc
        LEFT JOIN crypto_up_down_5m_websocket_resolved_markets ws
          ON ws.asset_symbol = ds.asset_symbol
         AND ws.market_start_utc = run.market_start_utc
        ORDER BY ds.code, run.market_start_utc, run.settled_at_utc;
        """;

    var rows = new List<SettledRun>();
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.CommandTimeout = 180;

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var strategyCode = reader.GetString(0);
        var strategyName = reader.GetString(1);
        var info = StrategyInfo.Parse(strategyCode, strategyName);
        var marketStartUtc = reader.IsDBNull(2) ? GetUtcDateTime(reader, 3) : GetUtcDateTime(reader, 2);
        var selectedOutcome = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
        var pnl = reader.GetDecimal(5);
        var winningOutcome = reader.IsDBNull(6) ? null : reader.GetString(6);
        var won = DetermineWinLoss(selectedOutcome, winningOutcome, pnl);

        rows.Add(new SettledRun(
            info,
            DateOnly.FromDateTime(marketStartUtc.Date),
            selectedOutcome,
            winningOutcome,
            pnl,
            won));
    }

    return rows;
}

static DateTime GetUtcDateTime(NpgsqlDataReader reader, int ordinal)
{
    var value = reader.GetDateTime(ordinal);
    return value.Kind == DateTimeKind.Utc
        ? value
        : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

static bool? DetermineWinLoss(string selectedOutcome, string? winningOutcome, decimal pnl)
{
    if (!string.IsNullOrWhiteSpace(winningOutcome) &&
        !string.IsNullOrWhiteSpace(selectedOutcome))
    {
        return string.Equals(selectedOutcome, winningOutcome, StringComparison.OrdinalIgnoreCase);
    }

    if (pnl > 0m)
    {
        return true;
    }

    if (pnl < 0m)
    {
        return false;
    }

    return null;
}

static List<StrategyTable> BuildStrategyTables(IReadOnlyList<SettledRun> settledRuns)
{
    return settledRuns
        .GroupBy(row => row.Info)
        .Select(group =>
        {
            var dailyRows = group
                .GroupBy(row => row.ReportDate)
                .OrderBy(group => group.Key)
                .Select(dayGroup => new DailyAggregate(
                    dayGroup.Key,
                    dayGroup.Count(),
                    dayGroup.Count(row => row.Won == true),
                    dayGroup.Count(row => row.Won == false),
                    dayGroup.Sum(row => row.PnlCurrentUsd)))
                .ToArray();

            return new StrategyTable(group.Key, dailyRows);
        })
        .Where(table => table.TotalSettledBets > 0)
        .OrderBy(table => SheetDefinitions.SortKey(table.Info.SheetName))
        .ThenBy(table => table.Info.Threshold)
        .ThenBy(table => table.Info.StrategyName, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static List<SheetSummary> BuildSheetSummaries(IReadOnlyList<StrategyTable> strategyTables)
{
    var bySheet = strategyTables
        .GroupBy(table => table.Info.SheetName)
        .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

    return SheetDefinitions.All
        .Select(sheet =>
        {
            bySheet.TryGetValue(sheet.Name, out var tables);
            tables ??= [];
            return new SheetSummary(
                sheet.Name,
                tables.Length,
                tables.Sum(table => table.TotalSettledBets),
                tables.Sum(table => table.TotalWins),
                tables.Sum(table => table.TotalLosses),
                tables.Sum(table => table.TotalPnlCurrentUsd),
                tables.SelectMany(table => table.Rows).Select(row => (DateOnly?)row.ReportDate).DefaultIfEmpty(null).Min(),
                tables.SelectMany(table => table.Rows).Select(row => (DateOnly?)row.ReportDate).DefaultIfEmpty(null).Max());
        })
        .OrderBy(summary => SheetDefinitions.SortKey(summary.SheetName))
        .ToList();
}

static void CreateWorkbook(
    string outputPath,
    DateTimeOffset capturedAtUtc,
    IReadOnlyList<StrategyTable> strategyTables,
    IReadOnlyList<SheetSummary> sheetSummaries)
{
    using var document = SpreadsheetDocument.Create(outputPath, SpreadsheetDocumentType.Workbook);
    var workbookPart = document.AddWorkbookPart();
    workbookPart.Workbook = new Workbook();
    var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
    stylesPart.Stylesheet = BuildStylesheet();
    stylesPart.Stylesheet.Save();

    var sheets = workbookPart.Workbook.AppendChild(new Sheets());
    var sheetId = 1U;
    AddWorksheet(workbookPart, sheets, "Summary", sheetId++, BuildSummarySheet(sheetSummaries));
    AddWorksheet(workbookPart, sheets, "Assumptions", sheetId++, BuildAssumptionsSheet(capturedAtUtc));

    var tablesBySheet = strategyTables
        .GroupBy(table => table.Info.SheetName)
        .ToDictionary(group => group.Key, group => group.OrderBy(table => table.Info.Threshold).ToArray(), StringComparer.Ordinal);

    foreach (var sheet in SheetDefinitions.All)
    {
        tablesBySheet.TryGetValue(sheet.Name, out var tables);
        AddWorksheet(workbookPart, sheets, sheet.Name, sheetId++, BuildStrategySheet(sheet.Name, tables ?? []));
    }

    workbookPart.Workbook.Append(new CalculationProperties { CalculationMode = CalculateModeValues.Auto });
    workbookPart.Workbook.Save();
}

static Worksheet BuildSummarySheet(IReadOnlyList<SheetSummary> summaries)
{
    var sheetData = new SheetData();
    var merges = new MergeCells();

    AppendRow(sheetData, 1, [TextCell("A", 1, "Diff and AdjustedDiff All-Time Report", 1)]);
    merges.Append(new MergeCell { Reference = "A1:H1" });
    AppendRow(sheetData, 2, [TextCell("A", 2, "Only Settled Paper strategy runs are included. Dates are UTC market-start dates.", 6)]);
    merges.Append(new MergeCell { Reference = "A2:H2" });

    AppendRow(sheetData, 4, HeaderCells(4, [
        "Sheet",
        "Strategies",
        "Settled bets",
        "Wins",
        "Losses",
        "Pnl current",
        "First date UTC",
        "Last date UTC"
    ]));

    var rowIndex = 5U;
    foreach (var row in summaries)
    {
        AppendRow(sheetData, rowIndex,
        [
            TextCell("A", rowIndex, row.SheetName, 0),
            NumberCell("B", rowIndex, row.StrategiesWithSettled, 7),
            NumberCell("C", rowIndex, row.SettledBets, 7),
            NumberCell("D", rowIndex, row.WinCount, 7),
            NumberCell("E", rowIndex, row.LossCount, 7),
            NumberCell("F", rowIndex, row.PnlCurrentUsd, 3),
            TextCell("G", rowIndex, FormatDate(row.FirstDate), 0),
            TextCell("H", rowIndex, FormatDate(row.LastDate), 0)
        ]);
        rowIndex++;
    }

    var firstDataRow = 5U;
    var lastDataRow = rowIndex - 1;
    AppendRow(sheetData, rowIndex + 1,
    [
        TextCell("A", rowIndex + 1, "Total", 5),
        FormulaNumberCell("B", rowIndex + 1, $"SUM(B{firstDataRow}:B{lastDataRow})", summaries.Sum(row => row.StrategiesWithSettled), 8),
        FormulaNumberCell("C", rowIndex + 1, $"SUM(C{firstDataRow}:C{lastDataRow})", summaries.Sum(row => row.SettledBets), 8),
        FormulaNumberCell("D", rowIndex + 1, $"SUM(D{firstDataRow}:D{lastDataRow})", summaries.Sum(row => row.WinCount), 8),
        FormulaNumberCell("E", rowIndex + 1, $"SUM(E{firstDataRow}:E{lastDataRow})", summaries.Sum(row => row.LossCount), 8),
        FormulaNumberCell("F", rowIndex + 1, $"SUM(F{firstDataRow}:F{lastDataRow})", summaries.Sum(row => row.PnlCurrentUsd), 4),
        TextCell("G", rowIndex + 1, string.Empty, 5),
        TextCell("H", rowIndex + 1, string.Empty, 5)
    ]);

    return CreateWorksheet(sheetData, merges, [
        Width(1, 22),
        Width(2, 12),
        Width(3, 14),
        Width(4, 10),
        Width(5, 10),
        Width(6, 16),
        Width(7, 15),
        Width(8, 15)
    ]);
}

static Worksheet BuildAssumptionsSheet(DateTimeOffset capturedAtUtc)
{
    var sheetData = new SheetData();
    var merges = new MergeCells();

    AppendRow(sheetData, 1, [TextCell("A", 1, "Report Assumptions", 1)]);
    merges.Append(new MergeCell { Reference = "A1:B1" });

    var rows = new (string Label, string Value)[]
    {
        ("Captured at UTC", capturedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
        ("Data source", "Production PostgreSQL read-only export via POLYCOPYTRADER_POSTGRES_CONNECTION with only the host overridden by the shell command."),
        ("Scope", "BTC/ETH/SOL 5m strategy codes matching regular Diff and AdjustedDiff Instant variants."),
        ("Included rows", "strategy_market_paper_runs.status = Settled with non-null settled_at_utc and realized_pnl_usd."),
        ("Date rows", "UTC date of market_start_utc; settled_at_utc is used only as a fallback if market_start_utc is null."),
        ("Wins and losses", "Selected outcome is compared to the recorded winning outcome when available; otherwise realized PnL sign is used."),
        ("PnL", "Pnl current is the realized Paper PnL stored on the settled strategy run."),
        ("Sheet naming", "Regular Diff sheets use ASSET_DIRECTION, e.g. BTC_UP. AdjustedDiff sheets use ASSET_Adjusted_DIRECTION, e.g. BTC_Adjusted_UP."),
        ("Strategy sorting", "Within each sheet, strategy tables are sorted by the numeric threshold N in the strategy code/name."),
        ("Empty sheets", "All requested asset/type/direction sheets are present; sheets with no settled strategy rows contain an explicit no-data note.")
    };

    AppendRow(sheetData, 3, HeaderCells(3, ["Item", "Value"]));
    var rowIndex = 4U;
    foreach (var row in rows)
    {
        AppendRow(sheetData, rowIndex,
        [
            TextCell("A", rowIndex, row.Label, 5),
            TextCell("B", rowIndex, row.Value, 6)
        ]);
        rowIndex++;
    }

    return CreateWorksheet(sheetData, merges, [
        Width(1, 24),
        Width(2, 120)
    ]);
}

static Worksheet BuildStrategySheet(string sheetName, IReadOnlyList<StrategyTable> tables)
{
    var sheetData = new SheetData();
    var merges = new MergeCells();

    AppendRow(sheetData, 1, [TextCell("A", 1, sheetName, 1)]);
    merges.Append(new MergeCell { Reference = "A1:E1" });
    AppendRow(sheetData, 2, [TextCell("A", 2, "Each table is one strategy with daily Settled counts, wins, losses, PnL, and a Total row.", 6)]);
    merges.Append(new MergeCell { Reference = "A2:E2" });

    var rowIndex = 4U;
    if (tables.Count == 0)
    {
        AppendRow(sheetData, rowIndex, [TextCell("A", rowIndex, "No strategies with Settled bets in this sheet scope.", 6)]);
        merges.Append(new MergeCell { Reference = $"A{rowIndex}:E{rowIndex}" });
        return CreateStrategyWorksheet(sheetData, merges);
    }

    foreach (var table in tables.OrderBy(table => table.Info.Threshold).ThenBy(table => table.Info.StrategyName, StringComparer.OrdinalIgnoreCase))
    {
        AppendRow(sheetData, rowIndex, [TextCell("A", rowIndex, table.Info.StrategyName, 1)]);
        merges.Append(new MergeCell { Reference = $"A{rowIndex}:E{rowIndex}" });
        rowIndex++;

        AppendRow(sheetData, rowIndex, HeaderCells(rowIndex, [
            "Дата",
            "Settled ставок",
            "Выигрыши",
            "Проигрыши",
            "Pnl текущий"
        ]));
        rowIndex++;

        var firstDataRow = rowIndex;
        foreach (var dailyRow in table.Rows)
        {
            AppendRow(sheetData, rowIndex,
            [
                TextCell("A", rowIndex, dailyRow.ReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 0),
                NumberCell("B", rowIndex, dailyRow.SettledBets, 7),
                NumberCell("C", rowIndex, dailyRow.WinCount, 7),
                NumberCell("D", rowIndex, dailyRow.LossCount, 7),
                NumberCell("E", rowIndex, dailyRow.PnlCurrentUsd, 3)
            ]);
            rowIndex++;
        }

        var lastDataRow = rowIndex - 1;
        AppendRow(sheetData, rowIndex,
        [
            TextCell("A", rowIndex, "Total", 5),
            FormulaNumberCell("B", rowIndex, $"SUM(B{firstDataRow}:B{lastDataRow})", table.TotalSettledBets, 8),
            FormulaNumberCell("C", rowIndex, $"SUM(C{firstDataRow}:C{lastDataRow})", table.TotalWins, 8),
            FormulaNumberCell("D", rowIndex, $"SUM(D{firstDataRow}:D{lastDataRow})", table.TotalLosses, 8),
            FormulaNumberCell("E", rowIndex, $"SUM(E{firstDataRow}:E{lastDataRow})", table.TotalPnlCurrentUsd, 4)
        ]);
        rowIndex += 2;
    }

    return CreateStrategyWorksheet(sheetData, merges);
}

static Worksheet CreateStrategyWorksheet(SheetData sheetData, MergeCells merges)
{
    return CreateWorksheet(sheetData, merges, [
        Width(1, 14),
        Width(2, 16),
        Width(3, 12),
        Width(4, 12),
        Width(5, 16)
    ]);
}

static Worksheet CreateWorksheet(SheetData sheetData, MergeCells merges, IEnumerable<Column> columns)
{
    var sheetViews = new SheetViews(new SheetView { WorkbookViewId = 0U, ShowGridLines = false });
    var worksheet = new Worksheet(sheetViews, new Columns(columns), sheetData);
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
        new CellFormat(),
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

static Cell FormulaNumberCell(string columnName, uint rowIndex, string formula, decimal cachedValue, uint styleIndex)
{
    return new Cell
    {
        CellReference = columnName + rowIndex.ToString(CultureInfo.InvariantCulture),
        DataType = CellValues.Number,
        StyleIndex = styleIndex,
        CellFormula = new CellFormula(formula),
        CellValue = new CellValue(cachedValue.ToString("0.########", CultureInfo.InvariantCulture))
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

static string FormatDate(DateOnly? date)
{
    return date.HasValue ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
}

static void ValidateWorkbook(string path)
{
    using var document = SpreadsheetDocument.Open(path, false);
    var validator = new OpenXmlValidator();
    var errors = validator.Validate(document).Take(20).ToArray();
    if (errors.Length > 0)
    {
        foreach (var error in errors)
        {
            Console.Error.WriteLine(error.Description);
        }

        throw new InvalidOperationException($"Workbook validation failed with {errors.Length} error(s).");
    }
}

static void VerifyWorkbook(string path, IReadOnlyList<StrategyTable> strategyTables)
{
    using var document = SpreadsheetDocument.Open(path, false);
    var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("Workbook part is missing.");
    var workbook = workbookPart.Workbook ?? throw new InvalidOperationException("Workbook is missing.");
    var sheetNames = workbook.Sheets?.Elements<Sheet>().Select(sheet => sheet.Name?.Value ?? string.Empty).ToArray()
        ?? throw new InvalidOperationException("Workbook sheets are missing.");

    var expectedSheetNames = new[] { "Summary", "Assumptions" }.Concat(SheetDefinitions.All.Select(sheet => sheet.Name)).ToArray();
    var missingSheets = expectedSheetNames.Except(sheetNames, StringComparer.Ordinal).ToArray();
    if (missingSheets.Length > 0)
    {
        throw new InvalidOperationException("Missing sheet(s): " + string.Join(", ", missingSheets));
    }

    var formulaCount = workbookPart.WorksheetParts
        .SelectMany(part => part.Worksheet?.Descendants<Cell>() ?? [])
        .Count(cell => cell.CellFormula is not null);
    var expectedFormulaCount = 5 + strategyTables.Count * 4;
    if (formulaCount != expectedFormulaCount)
    {
        throw new InvalidOperationException($"Unexpected formula count: expected {expectedFormulaCount}, got {formulaCount}.");
    }
}

sealed record SettledRun(
    StrategyInfo Info,
    DateOnly ReportDate,
    string SelectedOutcome,
    string? WinningOutcome,
    decimal PnlCurrentUsd,
    bool? Won);

sealed record StrategyTable(StrategyInfo Info, IReadOnlyList<DailyAggregate> Rows)
{
    public int TotalSettledBets => Rows.Sum(row => row.SettledBets);

    public int TotalWins => Rows.Sum(row => row.WinCount);

    public int TotalLosses => Rows.Sum(row => row.LossCount);

    public decimal TotalPnlCurrentUsd => Rows.Sum(row => row.PnlCurrentUsd);
}

sealed record DailyAggregate(
    DateOnly ReportDate,
    int SettledBets,
    int WinCount,
    int LossCount,
    decimal PnlCurrentUsd);

sealed record SheetSummary(
    string SheetName,
    int StrategiesWithSettled,
    int SettledBets,
    int WinCount,
    int LossCount,
    decimal PnlCurrentUsd,
    DateOnly? FirstDate,
    DateOnly? LastDate);

sealed record StrategyInfo(
    string AssetSymbol,
    string Direction,
    string StrategyType,
    int Threshold,
    string StrategyCode,
    string StrategyName,
    string SheetName)
{
    private static readonly Regex StrategyCodeRegex = new(
        "^(?<asset>btc|eth|sol)_up_down_5m_(?<direction>up|down)_(?<type>adjusted_diff|diff)_(?<threshold>\\d+)_instant$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static StrategyInfo Parse(string strategyCode, string strategyName)
    {
        var match = StrategyCodeRegex.Match(strategyCode);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Unexpected Diff strategy code: {strategyCode}");
        }

        var asset = match.Groups["asset"].Value.ToUpperInvariant();
        var direction = match.Groups["direction"].Value.ToUpperInvariant();
        var typeToken = match.Groups["type"].Value.ToLowerInvariant();
        var type = typeToken == "adjusted_diff" ? "AdjustedDiff" : "Diff";
        var threshold = int.Parse(match.Groups["threshold"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        var sheetName = type == "AdjustedDiff"
            ? $"{asset}_Adjusted_{direction}"
            : $"{asset}_{direction}";

        return new StrategyInfo(asset, direction, type, threshold, strategyCode, strategyName, sheetName);
    }
}

sealed record SheetDefinition(string Name);

static class SheetDefinitions
{
    public static IReadOnlyList<SheetDefinition> All { get; } =
    [
        new("BTC_UP"),
        new("BTC_DOWN"),
        new("BTC_Adjusted_UP"),
        new("BTC_Adjusted_DOWN"),
        new("ETH_UP"),
        new("ETH_DOWN"),
        new("ETH_Adjusted_UP"),
        new("ETH_Adjusted_DOWN"),
        new("SOL_UP"),
        new("SOL_DOWN"),
        new("SOL_Adjusted_UP"),
        new("SOL_Adjusted_DOWN")
    ];

    public static int SortKey(string sheetName)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i].Name, sheetName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
