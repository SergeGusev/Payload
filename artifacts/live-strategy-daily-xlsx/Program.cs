using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 2;
}

var matrixMode = args.Any(item => string.Equals(item, "--matrix", StringComparison.OrdinalIgnoreCase));
var outputPath = args.FirstOrDefault(item => !item.StartsWith("--", StringComparison.Ordinal))
    ?? Path.Combine("outputs", "live-strategy-daily-report-2026-06-08", "live-strategies-daily-by-day-2026-06-08.xlsx");

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

var dbNow = await GetDbNowAsync(connection);
var orders = await LoadLiveOrdersAsync(connection);
var strategies = await LoadStrategiesAsync(connection);
var workbook = matrixMode
    ? BuildLiveStrategyMatrixWorkbook(orders, strategies)
    : BuildWorkbook(dbNow, orders, strategies);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
CreateWorkbookPackage(outputPath, workbook);
VerifyWorkbookPackage(outputPath, workbook.Sheets.Count);

Console.WriteLine($"output={Path.GetFullPath(outputPath)}");
Console.WriteLine($"mode={(matrixMode ? "matrix" : "daily")}");
Console.WriteLine($"orders={orders.Count}");
Console.WriteLine($"strategies={strategies.Count}");
Console.WriteLine($"period_start={FormatDateTime(orders.MinBy(item => item.CreatedAtUtc)?.CreatedAtUtc)}");
Console.WriteLine($"period_end={FormatDateTime(orders.MaxBy(item => item.CreatedAtUtc)?.CreatedAtUtc)}");
Console.WriteLine($"settled={orders.Count(item => item.IsSettled)}");
Console.WriteLine($"realized_pnl={orders.Where(item => item.IsSettled).Sum(item => item.RealizedPnlUsd ?? 0m).ToString("0.00", CultureInfo.InvariantCulture)}");
return 0;

static async Task<DateTimeOffset> GetDbNowAsync(NpgsqlConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "select now()";
    var value = await command.ExecuteScalarAsync();
    return value is DateTimeOffset offset
        ? offset.ToUniversalTime()
        : new DateTimeOffset(DateTime.SpecifyKind((DateTime)value!, DateTimeKind.Utc));
}

static async Task<List<LiveOrderRow>> LoadLiveOrdersAsync(NpgsqlConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
SELECT
    live_order.id::text,
    COALESCE(live_order.order_id, '') AS order_id,
    live_order.created_at_utc,
    live_order.submitted_at_utc,
    live_order.updated_at_utc,
    live_order.expires_at_utc,
    live_order.settled_at_utc,
    strategy.code,
    strategy.name,
    strategy.enabled,
    strategy.live_stakes,
    strategy.auto_live_paused,
    strategy.paused,
    strategy.live_stake_amount,
    strategy.live_available_balance,
    live_order.status,
    live_order.side,
    live_order.outcome,
    live_order.price,
    live_order.size_shares,
    live_order.notional_usd,
    live_order.filled_size,
    live_order.remaining_size,
    live_order.average_fill_price,
    live_order.filled_notional_usd,
    CASE
        WHEN live_order.cost_basis_usd > 0 THEN live_order.cost_basis_usd
        WHEN live_order.filled_notional_usd > 0 THEN live_order.filled_notional_usd + live_order.fee_usd
        WHEN live_order.filled_size > 0 THEN live_order.price * live_order.filled_size + live_order.fee_usd
        ELSE 0
    END AS cost_basis_usd,
    live_order.fee_usd,
    live_order.settlement_value_usd,
    live_order.realized_pnl_usd,
    live_order.won,
    COALESCE(live_order.winning_outcome, '') AS winning_outcome,
    COALESCE(live_order.settlement_source, '') AS settlement_source,
    COALESCE(live_order.execution_source, '') AS execution_source,
    live_order.order_type,
    live_order.response_status,
    live_order.cancel_status,
    live_order.condition_id,
    live_order.asset_id,
    COALESCE(live_order.correlation_id::text, '') AS correlation_id,
    COALESCE(live_order.paper_order_id::text, '') AS paper_order_id
FROM live_orders live_order
INNER JOIN strategies strategy ON strategy.id = live_order.strategy_id
ORDER BY live_order.created_at_utc ASC, strategy.code ASC;
""";
    command.CommandTimeout = 60;
    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<LiveOrderRow>();
    while (await reader.ReadAsync())
    {
        rows.Add(new LiveOrderRow(
            reader.GetString(0),
            reader.GetString(1),
            GetDateTimeOffset(reader, 2)!.Value,
            GetDateTimeOffset(reader, 3),
            GetDateTimeOffset(reader, 4)!.Value,
            GetDateTimeOffset(reader, 5)!.Value,
            GetDateTimeOffset(reader, 6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetBoolean(9),
            reader.GetBoolean(10),
            reader.GetBoolean(11),
            reader.GetBoolean(12),
            reader.GetDecimal(13),
            reader.GetDecimal(14),
            reader.GetString(15),
            reader.GetString(16),
            reader.GetString(17),
            reader.GetDecimal(18),
            reader.GetDecimal(19),
            reader.GetDecimal(20),
            reader.GetDecimal(21),
            reader.GetDecimal(22),
            reader.IsDBNull(23) ? null : reader.GetDecimal(23),
            reader.GetDecimal(24),
            reader.GetDecimal(25),
            reader.GetDecimal(26),
            reader.IsDBNull(27) ? null : reader.GetDecimal(27),
            reader.IsDBNull(28) ? null : reader.GetDecimal(28),
            reader.IsDBNull(29) ? null : reader.GetBoolean(29),
            reader.GetString(30),
            reader.GetString(31),
            reader.GetString(32),
            reader.GetString(33),
            reader.GetString(34),
            reader.GetString(35),
            reader.GetString(36),
            reader.GetString(37),
            reader.GetString(38),
            reader.GetString(39)));
    }

    return rows;
}

static async Task<List<StrategyRow>> LoadStrategiesAsync(NpgsqlConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
SELECT
    strategy.id::text,
    strategy.code,
    strategy.name,
    strategy.enabled,
    strategy.live_stakes,
    strategy.auto_live_paused,
    strategy.paused,
    strategy.live_stake_amount,
    strategy.live_available_balance,
    strategy.live_enabled_at_utc
FROM strategies strategy
WHERE strategy.live_stakes
   OR strategy.live_enabled_at_utc IS NOT NULL
   OR EXISTS (SELECT 1 FROM live_orders live_order WHERE live_order.strategy_id = strategy.id)
ORDER BY strategy.code ASC;
""";
    command.CommandTimeout = 60;
    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<StrategyRow>();
    while (await reader.ReadAsync())
    {
        rows.Add(new StrategyRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetBoolean(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            GetDateTimeOffset(reader, 9)));
    }

    return rows;
}

static WorkbookModel BuildWorkbook(
    DateTimeOffset dbNow,
    IReadOnlyList<LiveOrderRow> orders,
    IReadOnlyList<StrategyRow> strategies)
{
    var generatedAtUtc = DateTimeOffset.UtcNow;
    var settled = orders.Where(item => item.IsSettled).ToArray();
    var settledCost = settled.Sum(item => item.CostBasisUsd);
    var realizedPnl = settled.Sum(item => item.RealizedPnlUsd ?? 0m);
    var summaryRows = new List<object?[]>
    {
        new object?[] { "Generated at UTC", FormatDateTime(generatedAtUtc) },
        new object?[] { "Database time UTC", FormatDateTime(dbNow) },
        new object?[] { "Order date period UTC", $"{FormatDateTime(orders.MinBy(item => item.CreatedAtUtc)?.CreatedAtUtc)} - {FormatDateTime(orders.MaxBy(item => item.CreatedAtUtc)?.CreatedAtUtc)}" },
        new object?[] { "Live orders", orders.Count },
        new object?[] { "Filled live orders", orders.Count(item => item.FilledSize > 0m) },
        new object?[] { "Open live orders", orders.Count(item => item.IsOpen) },
        new object?[] { "Settled live orders", settled.Length },
        new object?[] { "Won / Lost settled", $"{settled.Count(item => item.IsWon)} / {settled.Count(item => item.IsLost)}" },
        new object?[] { "Settled cost basis, USD", settledCost },
        new object?[] { "Realized PnL, USD", realizedPnl },
        new object?[] { "Live ROI %", Percent(realizedPnl, settledCost) },
        new object?[] { "Strategies with Live flag", strategies.Count(item => item.LiveStakes) },
        new object?[] { "Strategies represented by Live orders", orders.Select(item => item.StrategyCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() },
        new object?[] { "Day grouping note", "By Day uses live_order.created_at_utc. By Settlement Day uses live_order.settled_at_utc." }
    };

    var sheets = new List<SheetModel>
    {
        new(
            "Summary",
            [new ColumnModel("Metric", ColumnKind.Text, 34), new ColumnModel("Value", ColumnKind.Text, 36)],
            summaryRows,
            "Live Strategies Daily Report",
            UseFilter: false),
        DailySheet(
            "By Day",
            "Live Orders By Created Day UTC",
            orders,
            item => item.CreatedAtUtc.Date,
            includeStrategy: false),
        DailySheet(
            "By Settlement Day",
            "Live Results By Settlement Day UTC",
            settled,
            item => item.SettledAtUtc!.Value.Date,
            includeStrategy: false),
        DailySheet(
            "By Strategy Day",
            "Live Orders By Strategy And Created Day UTC",
            orders,
            item => item.CreatedAtUtc.Date,
            includeStrategy: true),
        StrategySheet(strategies, orders),
        OrderSheet(orders)
    };

    return new WorkbookModel(sheets);
}

static WorkbookModel BuildLiveStrategyMatrixWorkbook(
    IReadOnlyList<LiveOrderRow> orders,
    IReadOnlyList<StrategyRow> strategies)
{
    var liveStrategies = strategies
        .Where(item => item.LiveStakes)
        .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var liveStrategyCodes = liveStrategies
        .Select(item => item.Code)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var settledOrders = orders
        .Where(item => item.IsSettled && liveStrategyCodes.Contains(item.StrategyCode))
        .ToArray();
    var days = CreateContinuousDays(settledOrders);
    var pnlByDayAndStrategy = settledOrders
        .GroupBy(item => (Day: item.SettledAtUtc!.Value.UtcDateTime.Date, item.StrategyCode))
        .ToDictionary(
            group => group.Key,
            group => group.Sum(item => item.RealizedPnlUsd ?? 0m));

    var columns = new List<ColumnModel>
    {
        new("Settlement Date UTC", ColumnKind.Text, 18)
    };
    columns.AddRange(liveStrategies.Select(strategy => new ColumnModel(strategy.Name, ColumnKind.Currency, 18)));
    columns.Add(new("Daily PnL Total", ColumnKind.Currency, 16));

    var rows = new List<object?[]>();
    foreach (var day in days)
    {
        var row = new List<object?>
        {
            day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
        var dailyTotal = 0m;
        foreach (var strategy in liveStrategies)
        {
            pnlByDayAndStrategy.TryGetValue((day, strategy.Code), out var value);
            row.Add(value);
            dailyTotal += value;
        }

        row.Add(dailyTotal);
        rows.Add(row.ToArray());
    }

    var totalRow = new List<object?> { "Strategy PnL Total" };
    var grandTotal = 0m;
    foreach (var strategy in liveStrategies)
    {
        var strategyTotal = settledOrders
            .Where(item => string.Equals(item.StrategyCode, strategy.Code, StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.RealizedPnlUsd ?? 0m);
        totalRow.Add(strategyTotal);
        grandTotal += strategyTotal;
    }

    totalRow.Add(grandTotal);
    rows.Add(totalRow.ToArray());

    return new WorkbookModel(
    [
        new SheetModel(
            "Live PnL Matrix",
            columns,
            rows,
            Title: null,
            UseFilter: true)
    ]);
}

static IReadOnlyList<DateTime> CreateContinuousDays(IReadOnlyList<LiveOrderRow> settledOrders)
{
    if (settledOrders.Count == 0)
    {
        return [];
    }

    var start = settledOrders.Min(item => item.SettledAtUtc!.Value.UtcDateTime.Date);
    var end = settledOrders.Max(item => item.SettledAtUtc!.Value.UtcDateTime.Date);
    var days = new List<DateTime>();
    for (var day = start; day <= end; day = day.AddDays(1))
    {
        days.Add(day);
    }

    return days;
}

static SheetModel DailySheet(
    string name,
    string title,
    IEnumerable<LiveOrderRow> orders,
    Func<LiveOrderRow, DateTime> daySelector,
    bool includeStrategy)
{
    var columns = new List<ColumnModel>
    {
        new("Day UTC", ColumnKind.Text, 12)
    };
    if (includeStrategy)
    {
        columns.Add(new("Strategy Code", ColumnKind.Text, 36));
        columns.Add(new("Strategy Name", ColumnKind.Text, 44));
        columns.Add(new("Live Flag", ColumnKind.Text, 10));
    }

    columns.AddRange(
    [
        new("Orders", ColumnKind.Integer, 10),
        new("Filled", ColumnKind.Integer, 10),
        new("Open", ColumnKind.Integer, 10),
        new("Settled", ColumnKind.Integer, 10),
        new("Won", ColumnKind.Integer, 10),
        new("Lost", ColumnKind.Integer, 10),
        new("Filled Notional", ColumnKind.Currency, 16),
        new("Settled Cost", ColumnKind.Currency, 16),
        new("Settlement Value", ColumnKind.Currency, 16),
        new("Realized PnL", ColumnKind.Currency, 16),
        new("Win Rate %", ColumnKind.Percent, 12),
        new("ROI %", ColumnKind.Percent, 12),
        new("Avg Win", ColumnKind.Currency, 14),
        new("Avg Loss", ColumnKind.Currency, 14),
        new("Last Order UTC", ColumnKind.Text, 20),
        new("Last Settlement UTC", ColumnKind.Text, 20)
    ]);

    var groups = includeStrategy
        ? orders.GroupBy(item => new DailyKey(daySelector(item), item.StrategyCode, item.StrategyName, item.LiveStakes))
            .OrderBy(group => group.Key.Day)
            .ThenBy(group => group.Key.StrategyCode, StringComparer.OrdinalIgnoreCase)
        : orders.GroupBy(item => new DailyKey(daySelector(item), string.Empty, string.Empty, false))
            .OrderBy(group => group.Key.Day)
            .ThenBy(group => group.Key.StrategyCode, StringComparer.OrdinalIgnoreCase);

    var rows = new List<object?[]>();
    foreach (var group in groups)
    {
        var items = group.ToArray();
        var settled = items.Where(item => item.IsSettled).ToArray();
        var won = settled.Count(item => item.IsWon);
        var lost = settled.Count(item => item.IsLost);
        var filledNotional = items.Sum(item => item.FilledNotionalUsd);
        var settledCost = settled.Sum(item => item.CostBasisUsd);
        var settlementValue = settled.Sum(item => item.SettlementValueUsd ?? 0m);
        var realizedPnl = settled.Sum(item => item.RealizedPnlUsd ?? 0m);
        var avgWin = AverageOrNull(settled.Where(item => item.IsWon).Select(item => item.RealizedPnlUsd ?? 0m));
        var avgLoss = AverageOrNull(settled.Where(item => item.IsLost).Select(item => item.RealizedPnlUsd ?? 0m));
        var row = new List<object?>
        {
            group.Key.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        if (includeStrategy)
        {
            row.Add(group.Key.StrategyCode);
            row.Add(group.Key.StrategyName);
            row.Add(group.Key.LiveStakes ? "On" : "Off");
        }

        row.AddRange(
        [
            items.Length,
            items.Count(item => item.FilledSize > 0m),
            items.Count(item => item.IsOpen),
            settled.Length,
            won,
            lost,
            filledNotional,
            settledCost,
            settlementValue,
            realizedPnl,
            Percent(won, settled.Length),
            Percent(realizedPnl, settledCost),
            avgWin,
            avgLoss,
            FormatDateTime(items.MaxBy(item => item.CreatedAtUtc)?.CreatedAtUtc),
            FormatDateTime(settled.MaxBy(item => item.SettledAtUtc)?.SettledAtUtc)
        ]);
        rows.Add(row.ToArray());
    }

    return new SheetModel(name, columns, rows, title);
}

static SheetModel StrategySheet(
    IReadOnlyList<StrategyRow> strategies,
    IReadOnlyList<LiveOrderRow> orders)
{
    var columns = new List<ColumnModel>
    {
        new("Strategy Code", ColumnKind.Text, 36),
        new("Strategy Name", ColumnKind.Text, 44),
        new("Enabled", ColumnKind.Text, 10),
        new("Live Flag", ColumnKind.Text, 10),
        new("Auto Live Paused", ColumnKind.Text, 16),
        new("Manual Paused", ColumnKind.Text, 14),
        new("Live Stake", ColumnKind.Currency, 12),
        new("Live Balance", ColumnKind.Currency, 14),
        new("Live Enabled UTC", ColumnKind.Text, 20),
        new("Orders", ColumnKind.Integer, 10),
        new("Filled", ColumnKind.Integer, 10),
        new("Open", ColumnKind.Integer, 10),
        new("Settled", ColumnKind.Integer, 10),
        new("Won", ColumnKind.Integer, 10),
        new("Lost", ColumnKind.Integer, 10),
        new("Settled Cost", ColumnKind.Currency, 16),
        new("Realized PnL", ColumnKind.Currency, 16),
        new("Win Rate %", ColumnKind.Percent, 12),
        new("ROI %", ColumnKind.Percent, 12),
        new("Last Order UTC", ColumnKind.Text, 20),
        new("Last Settlement UTC", ColumnKind.Text, 20)
    };

    var rows = new List<object?[]>();
    foreach (var strategy in strategies)
    {
        var items = orders.Where(item => string.Equals(item.StrategyCode, strategy.Code, StringComparison.OrdinalIgnoreCase)).ToArray();
        var settled = items.Where(item => item.IsSettled).ToArray();
        var cost = settled.Sum(item => item.CostBasisUsd);
        var pnl = settled.Sum(item => item.RealizedPnlUsd ?? 0m);
        var won = settled.Count(item => item.IsWon);
        rows.Add(
        [
            strategy.Code,
            strategy.Name,
            strategy.Enabled ? "Yes" : "No",
            strategy.LiveStakes ? "On" : "Off",
            strategy.AutoLivePaused ? "Yes" : "No",
            strategy.Paused ? "Yes" : "No",
            strategy.LiveStakeAmount,
            strategy.LiveAvailableBalance,
            FormatDateTime(strategy.LiveEnabledAtUtc),
            items.Length,
            items.Count(item => item.FilledSize > 0m),
            items.Count(item => item.IsOpen),
            settled.Length,
            won,
            settled.Count(item => item.IsLost),
            cost,
            pnl,
            Percent(won, settled.Length),
            Percent(pnl, cost),
            FormatDateTime(items.MaxBy(item => item.CreatedAtUtc)?.CreatedAtUtc),
            FormatDateTime(settled.MaxBy(item => item.SettledAtUtc)?.SettledAtUtc)
        ]);
    }

    return new SheetModel("Strategies", columns, rows, "Live Strategy Current State And Cumulative Live Orders");
}

static SheetModel OrderSheet(IReadOnlyList<LiveOrderRow> orders)
{
    var columns = new List<ColumnModel>
    {
        new("Created UTC", ColumnKind.Text, 20),
        new("Settled UTC", ColumnKind.Text, 20),
        new("Strategy Code", ColumnKind.Text, 36),
        new("Strategy Name", ColumnKind.Text, 44),
        new("Live Flag", ColumnKind.Text, 10),
        new("Status", ColumnKind.Text, 16),
        new("Side", ColumnKind.Text, 8),
        new("Outcome", ColumnKind.Text, 10),
        new("Price", ColumnKind.Number, 10),
        new("Size", ColumnKind.Number, 12),
        new("Notional", ColumnKind.Currency, 14),
        new("Filled Size", ColumnKind.Number, 14),
        new("Remaining", ColumnKind.Number, 14),
        new("Avg Fill", ColumnKind.Number, 12),
        new("Filled Notional", ColumnKind.Currency, 16),
        new("Cost Basis", ColumnKind.Currency, 14),
        new("Fee", ColumnKind.Currency, 12),
        new("Settlement Value", ColumnKind.Currency, 16),
        new("Realized PnL", ColumnKind.Currency, 14),
        new("Won", ColumnKind.Text, 8),
        new("Winning Outcome", ColumnKind.Text, 16),
        new("Settlement Source", ColumnKind.Text, 24),
        new("Execution Source", ColumnKind.Text, 24),
        new("Order Type", ColumnKind.Text, 12),
        new("Response Status", ColumnKind.Text, 18),
        new("Cancel Status", ColumnKind.Text, 18),
        new("Order Id", ColumnKind.Text, 18),
        new("Condition Id", ColumnKind.Text, 18),
        new("Asset Id", ColumnKind.Text, 18),
        new("Correlation Id", ColumnKind.Text, 18),
        new("Paper Order Id", ColumnKind.Text, 18)
    };

    var rows = orders
        .OrderBy(item => item.CreatedAtUtc)
        .Select(item => new object?[]
        {
            FormatDateTime(item.CreatedAtUtc),
            FormatDateTime(item.SettledAtUtc),
            item.StrategyCode,
            item.StrategyName,
            item.LiveStakes ? "On" : "Off",
            item.Status,
            item.Side,
            item.Outcome,
            item.Price,
            item.SizeShares,
            item.NotionalUsd,
            item.FilledSize,
            item.RemainingSize,
            item.AverageFillPrice,
            item.FilledNotionalUsd,
            item.CostBasisUsd,
            item.FeeUsd,
            item.SettlementValueUsd,
            item.RealizedPnlUsd,
            item.Won is null ? "" : item.Won.Value ? "Yes" : "No",
            item.WinningOutcome,
            item.SettlementSource,
            item.ExecutionSource,
            item.OrderType,
            item.ResponseStatus,
            item.CancelStatus,
            Shorten(item.OrderId),
            Shorten(item.ConditionId),
            Shorten(item.AssetId),
            Shorten(item.CorrelationId),
            Shorten(item.PaperOrderId)
        })
        .ToList();

    return new SheetModel("Orders", columns, rows, "Live Order Detail");
}

static void CreateWorkbookPackage(string outputPath, WorkbookModel workbook)
{
    if (File.Exists(outputPath))
    {
        File.Delete(outputPath);
    }

    using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
    AddText(archive, "[Content_Types].xml", CreateContentTypesXml(workbook.Sheets.Count));
    AddText(archive, "_rels/.rels", CreateRootRelationshipsXml());
    AddText(archive, "xl/workbook.xml", CreateWorkbookXml(workbook));
    AddText(archive, "xl/_rels/workbook.xml.rels", CreateWorkbookRelationshipsXml(workbook.Sheets.Count));
    AddText(archive, "xl/styles.xml", CreateStylesXml());
    for (var i = 0; i < workbook.Sheets.Count; i++)
    {
        AddText(archive, $"xl/worksheets/sheet{i + 1}.xml", CreateWorksheetXml(workbook.Sheets[i]));
    }
}

static string CreateWorksheetXml(SheetModel sheet)
{
    var rowOffset = string.IsNullOrWhiteSpace(sheet.Title) ? 0 : 2;
    var rows = new List<object?[]>();
    if (!string.IsNullOrWhiteSpace(sheet.Title))
    {
        rows.Add([sheet.Title]);
        rows.Add([]);
    }

    rows.Add(sheet.Columns.Select(item => (object?)item.Header).ToArray());
    rows.AddRange(sheet.Rows);
    var headerRow = rowOffset + 1;
    var lastRow = Math.Max(headerRow, rows.Count);
    var lastCol = Math.Max(1, sheet.Columns.Count);
    var autoFilterRef = $"{CellRef(1, headerRow)}:{CellRef(lastCol, lastRow)}";
    var sb = new StringBuilder();
    sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
    sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""");
    sb.Append("""<sheetViews><sheetView workbookViewId="0">""");
    sb.Append($"""<pane ySplit="{headerRow}" topLeftCell="A{headerRow + 1}" activePane="bottomLeft" state="frozen"/>""");
    sb.Append("""</sheetView></sheetViews>""");
    sb.Append("""<cols>""");
    for (var col = 1; col <= sheet.Columns.Count; col++)
    {
        sb.Append(CultureInfo.InvariantCulture, $"""<col min="{col}" max="{col}" width="{sheet.Columns[col - 1].Width}" customWidth="1"/>""");
    }

    sb.Append("""</cols><sheetData>""");
    for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
    {
        var rowNumber = rowIndex + 1;
        var row = rows[rowIndex];
        var customHeight = rowIndex == 0 && !string.IsNullOrWhiteSpace(sheet.Title)
            ? " ht=\"24\" customHeight=\"1\""
            : string.Empty;
        sb.Append(CultureInfo.InvariantCulture, $"""<row r="{rowNumber}"{customHeight}>""");
        var maxCols = rowIndex < rowOffset
            ? Math.Max(1, row.Length)
            : sheet.Columns.Count;
        for (var colIndex = 0; colIndex < maxCols; colIndex++)
        {
            var value = colIndex < row.Length ? row[colIndex] : null;
            var style = rowIndex == 0 && !string.IsNullOrWhiteSpace(sheet.Title)
                ? 1
                : rowIndex == rowOffset
                    ? 2
                    : StyleForColumn(sheet.Columns.Count > colIndex ? sheet.Columns[colIndex].Kind : ColumnKind.Text);
            AppendCell(sb, rowNumber, colIndex + 1, value, style);
        }

        sb.Append("</row>");
    }

    sb.Append("</sheetData>");
    if (sheet.UseFilter && sheet.Columns.Count > 0)
    {
        sb.Append($"""<autoFilter ref="{autoFilterRef}"/>""");
    }

    sb.Append("</worksheet>");
    return sb.ToString();
}

static string CreateContentTypesXml(int sheetCount)
{
    var sb = new StringBuilder();
    sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
    sb.Append("""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
    sb.Append("""<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""");
    sb.Append("""<Default Extension="xml" ContentType="application/xml"/>""");
    sb.Append("""<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>""");
    sb.Append("""<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""");
    for (var i = 1; i <= sheetCount; i++)
    {
        sb.Append(CultureInfo.InvariantCulture, $"""<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""");
    }

    sb.Append("</Types>");
    return sb.ToString();
}

static string CreateRootRelationshipsXml()
{
    return """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""";
}

static string CreateWorkbookXml(WorkbookModel workbook)
{
    var sb = new StringBuilder();
    sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
    sb.Append("""<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>""");
    for (var i = 0; i < workbook.Sheets.Count; i++)
    {
        sb.Append(CultureInfo.InvariantCulture, $"""<sheet name="{XmlEscape(workbook.Sheets[i].Name)}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""");
    }

    sb.Append("</sheets></workbook>");
    return sb.ToString();
}

static string CreateWorkbookRelationshipsXml(int sheetCount)
{
    var sb = new StringBuilder();
    sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
    sb.Append("""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
    for (var i = 1; i <= sheetCount; i++)
    {
        sb.Append(CultureInfo.InvariantCulture, $"""<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>""");
    }

    sb.Append(CultureInfo.InvariantCulture, $"""<Relationship Id="rId{sheetCount + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""");
    sb.Append("</Relationships>");
    return sb.ToString();
}

static string CreateStylesXml()
{
    return """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <numFmts count="4">
    <numFmt numFmtId="164" formatCode="$#,##0.00;[Red]-$#,##0.00"/>
    <numFmt numFmtId="165" formatCode="0.00"/>
    <numFmt numFmtId="166" formatCode="0.00%"/>
    <numFmt numFmtId="167" formatCode="0"/>
  </numFmts>
  <fonts count="3">
    <font><sz val="11"/><name val="Calibri"/></font>
    <font><b/><sz val="14"/><color rgb="FF1F2937"/><name val="Calibri"/></font>
    <font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>
  </fonts>
  <fills count="4">
    <fill><patternFill patternType="none"/></fill>
    <fill><patternFill patternType="gray125"/></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FF1F4E78"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFEAF2F8"/><bgColor indexed="64"/></patternFill></fill>
  </fills>
  <borders count="2">
    <border><left/><right/><top/><bottom/><diagonal/></border>
    <border><left style="thin"><color rgb="FFD9E2EC"/></left><right style="thin"><color rgb="FFD9E2EC"/></right><top style="thin"><color rgb="FFD9E2EC"/></top><bottom style="thin"><color rgb="FFD9E2EC"/></bottom><diagonal/></border>
  </borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="8">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>
    <xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/>
    <xf numFmtId="167" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/>
    <xf numFmtId="165" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/>
    <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/>
    <xf numFmtId="166" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/>
    <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1"/>
  </cellXfs>
  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
</styleSheet>
""";
}

static void AppendCell(StringBuilder sb, int row, int col, object? value, int style)
{
    var cellRef = CellRef(col, row);
    if (value is null)
    {
        sb.Append(CultureInfo.InvariantCulture, $"""<c r="{cellRef}" s="{style}"/>""");
        return;
    }

    if (value is int or long or decimal or double or float)
    {
        var numeric = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        sb.Append(CultureInfo.InvariantCulture, $"""<c r="{cellRef}" s="{style}"><v>{numeric.ToString("0.########", CultureInfo.InvariantCulture)}</v></c>""");
        return;
    }

    sb.Append(CultureInfo.InvariantCulture, $"""<c r="{cellRef}" s="{style}" t="inlineStr"><is><t>{XmlEscape(value.ToString() ?? string.Empty)}</t></is></c>""");
}

static int StyleForColumn(ColumnKind kind)
{
    return kind switch
    {
        ColumnKind.Integer => 3,
        ColumnKind.Number => 4,
        ColumnKind.Currency => 5,
        ColumnKind.Percent => 6,
        _ => 7
    };
}

static void AddText(ZipArchive archive, string path, string content)
{
    var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
    using var stream = entry.Open();
    using var writer = new StreamWriter(stream, Encoding.UTF8);
    writer.Write(content);
}

static void VerifyWorkbookPackage(string outputPath, int expectedSheets)
{
    using var archive = ZipFile.OpenRead(outputPath);
    var required = new[]
    {
        "[Content_Types].xml",
        "_rels/.rels",
        "xl/workbook.xml",
        "xl/_rels/workbook.xml.rels",
        "xl/styles.xml"
    };

    foreach (var path in required)
    {
        if (archive.GetEntry(path) is null)
        {
            throw new InvalidOperationException("Workbook package is missing " + path);
        }
    }

    for (var i = 1; i <= expectedSheets; i++)
    {
        if (archive.GetEntry($"xl/worksheets/sheet{i}.xml") is null)
        {
            throw new InvalidOperationException("Workbook package is missing worksheet " + i);
        }
    }

    foreach (var entry in archive.Entries.Where(item => item.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
    {
        using var stream = entry.Open();
        _ = XDocument.Load(stream);
    }

    using var document = SpreadsheetDocument.Open(outputPath, false);
    var validationErrors = new OpenXmlValidator().Validate(document).Take(20).ToArray();
    if (validationErrors.Length > 0)
    {
        throw new InvalidOperationException(
            "Workbook failed OpenXML validation: "
            + string.Join("; ", validationErrors.Select(item => $"{item.Path?.XPath}: {item.Description}")));
    }
}

static DateTimeOffset? GetDateTimeOffset(NpgsqlDataReader reader, int index)
{
    if (reader.IsDBNull(index))
    {
        return null;
    }

    var value = reader.GetValue(index);
    return value switch
    {
        DateTimeOffset offset => offset.ToUniversalTime(),
        DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        _ => null
    };
}

static decimal? AverageOrNull(IEnumerable<decimal> values)
{
    var items = values.ToArray();
    return items.Length == 0 ? null : items.Average();
}

static decimal? Percent(decimal numerator, decimal denominator)
{
    return denominator == 0m ? null : numerator / denominator;
}

static string FormatDateTime(DateTimeOffset? value)
{
    return value is null ? string.Empty : value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}

static string Shorten(string value)
{
    if (string.IsNullOrWhiteSpace(value) || value.Length <= 18)
    {
        return value;
    }

    return value[..10] + "..." + value[^6..];
}

static string CellRef(int col, int row)
{
    return ColumnName(col) + row.ToString(CultureInfo.InvariantCulture);
}

static string ColumnName(int col)
{
    var name = string.Empty;
    while (col > 0)
    {
        var rem = (col - 1) % 26;
        name = (char)('A' + rem) + name;
        col = (col - rem - 1) / 26;
    }

    return name;
}

static string XmlEscape(string value)
{
    return SecurityElementEscape(value);
}

static string SecurityElementEscape(string value)
{
    var escaped = SecurityElement.Escape(value) ?? string.Empty;
    return escaped.Replace("\u0000", string.Empty, StringComparison.Ordinal);
}

internal sealed record LiveOrderRow(
    string Id,
    string OrderId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? SettledAtUtc,
    string StrategyCode,
    string StrategyName,
    bool Enabled,
    bool LiveStakes,
    bool AutoLivePaused,
    bool Paused,
    decimal LiveStakeAmount,
    decimal LiveAvailableBalance,
    string Status,
    string Side,
    string Outcome,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    decimal FilledSize,
    decimal RemainingSize,
    decimal? AverageFillPrice,
    decimal FilledNotionalUsd,
    decimal CostBasisUsd,
    decimal FeeUsd,
    decimal? SettlementValueUsd,
    decimal? RealizedPnlUsd,
    bool? Won,
    string WinningOutcome,
    string SettlementSource,
    string ExecutionSource,
    string OrderType,
    string ResponseStatus,
    string CancelStatus,
    string ConditionId,
    string AssetId,
    string CorrelationId,
    string PaperOrderId)
{
    public bool IsOpen => Status is ("Submitted" or "Live" or "Delayed" or "Unmatched" or "CancelRequested") && RemainingSize > 0m;

    public bool IsSettled => SettledAtUtc is not null && RealizedPnlUsd is not null;

    public bool IsWon => IsSettled && (Won ?? SettlementValueUsd.GetValueOrDefault() > 0m);

    public bool IsLost => IsSettled && !IsWon;
}

internal sealed record StrategyRow(
    string Id,
    string Code,
    string Name,
    bool Enabled,
    bool LiveStakes,
    bool AutoLivePaused,
    bool Paused,
    decimal LiveStakeAmount,
    decimal LiveAvailableBalance,
    DateTimeOffset? LiveEnabledAtUtc);

internal sealed record ColumnModel(string Header, ColumnKind Kind, double Width);

internal enum ColumnKind
{
    Text,
    Integer,
    Number,
    Currency,
    Percent
}

internal sealed record SheetModel(
    string Name,
    IReadOnlyList<ColumnModel> Columns,
    IReadOnlyList<object?[]> Rows,
    string? Title = null,
    bool UseFilter = true);

internal sealed record WorkbookModel(IReadOnlyList<SheetModel> Sheets);

internal sealed record DailyKey(DateTime Day, string StrategyCode, string StrategyName, bool LiveStakes);
