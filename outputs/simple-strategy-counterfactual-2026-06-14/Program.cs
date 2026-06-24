using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Npgsql;

const decimal MinimumStakeSafetyMultiplier = 1.10m;
const decimal DefaultTickSize = 0.01m;
const decimal DefaultMaxSimplePrice = 0.50m;

var outputDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
Directory.CreateDirectory(outputDir);

var outputPath = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(outputDir, "simple-strategy-counterfactual-2026-06-14.xlsx");

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
    CommandTimeout = 180,
    ApplicationName = "SimpleStrategyCounterfactual"
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await ExecuteNonQueryAsync(connection, transaction, "SET TRANSACTION READ ONLY");
await ExecuteNonQueryAsync(connection, transaction, "SET LOCAL statement_timeout = '180s'");

var strategies = await LoadStrategiesAsync(connection, transaction);
var sourceRows = await LoadSourceRowsAsync(connection, transaction);
await transaction.CommitAsync();

var evaluatedRows = sourceRows.Select(EvaluateRow).ToArray();
var dailyByStrategy = BuildDailyByStrategy(strategies, evaluatedRows);
var dailyTotals = BuildTotalsByDate(dailyByStrategy);
var strategyTotals = BuildTotalsByStrategy(strategies, dailyByStrategy);
var grandTotal = BuildGrandTotal(dailyByStrategy);

var dailyByStrategyCsvPath = Path.Combine(outputDir, "simple-daily-by-strategy.csv");
var dailyTotalsCsvPath = Path.Combine(outputDir, "simple-daily-totals.csv");
var strategyTotalsCsvPath = Path.Combine(outputDir, "simple-strategy-totals.csv");
WriteDailyByStrategyCsv(dailyByStrategyCsvPath, dailyByStrategy);
WriteDailyTotalsCsv(dailyTotalsCsvPath, dailyTotals);
WriteStrategyTotalsCsv(strategyTotalsCsvPath, strategyTotals);

if (File.Exists(outputPath))
{
    File.Delete(outputPath);
}

CreateWorkbook(outputPath, capturedAtUtc, strategies, dailyTotals, dailyByStrategy, strategyTotals, grandTotal);
ValidateWorkbook(outputPath);
VerifyWorkbook(outputPath, dailyTotals, dailyByStrategy, strategyTotals, grandTotal);

Console.WriteLine($"output={outputPath}");
Console.WriteLine($"captured_at_utc={capturedAtUtc:O}");
Console.WriteLine($"strategies={strategies.Count}");
Console.WriteLine($"source_rows={sourceRows.Count}");
Console.WriteLine($"daily_total_rows={dailyTotals.Count}");
Console.WriteLine($"daily_by_strategy_rows={dailyByStrategy.Count}");
Console.WriteLine($"current_pnl={grandTotal.CurrentPnl:0.########}");
Console.WriteLine($"skip_over_0_50_pnl={grandTotal.SkipOverHalfPnl:0.########}");
Console.WriteLine($"always_market_pnl={grandTotal.AlwaysMarketPnl:0.########}");
Console.WriteLine($"always_market_not_computable={grandTotal.AlwaysMarketNotComputableCount}");
Console.WriteLine($"capped_over_0_50_decisions={grandTotal.CappedOverHalfCount}");
Console.WriteLine($"csv_daily_by_strategy={dailyByStrategyCsvPath}");
Console.WriteLine($"csv_daily_totals={dailyTotalsCsvPath}");
Console.WriteLine($"csv_strategy_totals={strategyTotalsCsvPath}");
return 0;

static async Task ExecuteNonQueryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}

static async Task<List<StrategyInfo>> LoadStrategiesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
{
    const string sql = """
        SELECT id, code, name
        FROM strategies
        WHERE lower(code) ~ '^[a-z0-9]+_up_down_5m_(up|down)_simple$'
        ORDER BY code;
        """;

    var rows = new List<StrategyInfo>();
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new StrategyInfo(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
    }

    return rows;
}

static async Task<List<SourceRow>> LoadSourceRowsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
{
    const string sql = """
        WITH simple_strategies AS (
            SELECT id, code, name
            FROM strategies
            WHERE lower(code) ~ '^[a-z0-9]+_up_down_5m_(up|down)_simple$'
        ),
        ledger AS (
            SELECT DISTINCT ON (condition_id)
                condition_id,
                winning_outcome,
                'websocket'::text AS result_source
            FROM crypto_up_down_5m_websocket_resolved_markets
            WHERE winning_outcome IN ('Up', 'Down')
            ORDER BY condition_id, first_received_at_utc DESC
        ),
        polling AS (
            SELECT DISTINCT ON (condition_id)
                condition_id,
                winning_outcome,
                'polling'::text AS result_source
            FROM crypto_up_down_5m_result_polling_observations
            WHERE status = 'Resolved'
              AND winning_outcome IN ('Up', 'Down')
            ORDER BY condition_id, updated_at_utc DESC
        ),
        inferred AS (
            SELECT
                condition_id,
                CASE
                    WHEN count(DISTINCT inferred_winning_outcome) = 1 THEN min(inferred_winning_outcome)
                    ELSE NULL
                END AS winning_outcome,
                'settled_run_inferred'::text AS result_source
            FROM (
                SELECT
                    condition_id,
                    CASE
                        WHEN selected_outcome = 'Up' AND realized_pnl_usd > 0 THEN 'Up'
                        WHEN selected_outcome = 'Up' AND realized_pnl_usd < 0 THEN 'Down'
                        WHEN selected_outcome = 'Down' AND realized_pnl_usd > 0 THEN 'Down'
                        WHEN selected_outcome = 'Down' AND realized_pnl_usd < 0 THEN 'Up'
                        ELSE NULL
                    END AS inferred_winning_outcome
                FROM strategy_market_paper_runs
                WHERE status = 'Settled'
                  AND selected_outcome IN ('Up', 'Down')
                  AND realized_pnl_usd IS NOT NULL
                  AND realized_pnl_usd <> 0
            ) inferred_rows
            WHERE inferred_winning_outcome IS NOT NULL
            GROUP BY condition_id
        )
        SELECT
            strategy.id AS strategy_id,
            strategy.code AS strategy_code,
            strategy.name AS strategy_name,
            run.id AS run_id,
            run.market_id,
            run.condition_id,
            run.market_slug,
            to_char((COALESCE(run.market_start_utc, run.market_end_utc - interval '5 minutes', run.entry_due_at_utc) AT TIME ZONE 'utc')::date, 'YYYY-MM-DD') AS market_date_utc,
            run.status,
            coalesce(run.skip_reason, '') AS skip_reason,
            coalesce(run.selected_outcome, paper_order.outcome, '') AS selected_outcome,
            run.entry_price,
            run.stake_usd,
            run.size_shares,
            run.realized_pnl_usd,
            run.settled_at_utc,
            paper_order.status AS paper_order_status,
            paper_order.price AS paper_order_price,
            paper_order.size_shares AS paper_order_size_shares,
            paper_order.notional_usd AS paper_order_notional_usd,
            COALESCE(ledger.winning_outcome, polling.winning_outcome, inferred.winning_outcome, '') AS winning_outcome,
            COALESCE(ledger.result_source, polling.result_source, inferred.result_source, '') AS result_source,
            COALESCE(paper_order.raw_decision_json::text, run.skip_diagnostics_json::text, '') AS raw_json
        FROM strategy_market_paper_runs run
        INNER JOIN simple_strategies strategy ON strategy.id = run.strategy_id
        LEFT JOIN paper_orders paper_order ON paper_order.id = run.paper_order_id
        LEFT JOIN ledger ON ledger.condition_id = run.condition_id
        LEFT JOIN polling ON polling.condition_id = run.condition_id
        LEFT JOIN inferred ON inferred.condition_id = run.condition_id
        WHERE run.status IN ('Settled', 'Skipped')
          AND (
              run.status = 'Settled'
              OR run.paper_order_id IS NOT NULL
              OR run.skip_reason = 'gtd_limit_not_filled'
          )
        ORDER BY COALESCE(run.market_start_utc, run.market_end_utc - interval '5 minutes', run.entry_due_at_utc), strategy.code;
        """;

    var rows = new List<SourceRow>();
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
    while (await reader.ReadAsync())
    {
        rows.Add(new SourceRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            DateOnly.ParseExact(reader.GetString(7), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            GetNullableDecimal(reader, 11),
            reader.GetDecimal(12),
            GetNullableDecimal(reader, 13),
            GetNullableDecimal(reader, 14),
            GetNullableDateTimeOffset(reader, 15),
            reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
            GetNullableDecimal(reader, 17),
            GetNullableDecimal(reader, 18),
            GetNullableDecimal(reader, 19),
            reader.GetString(20),
            reader.GetString(21),
            reader.GetString(22)));
    }

    return rows;
}

static EvaluatedRow EvaluateRow(SourceRow row)
{
    var currentPnl = string.Equals(row.Status, "Settled", StringComparison.OrdinalIgnoreCase)
        ? row.RealizedPnlUsd ?? 0m
        : 0m;

    var pricing = ParsePricing(row.RawJson);
    var isCappedOverHalf = pricing.IsCappedOverHalf;
    var skipOverHalfPnl = isCappedOverHalf ? 0m : currentPnl;
    var alwaysMarket = CalculateAlwaysMarketPnl(row, pricing, currentPnl);

    return new EvaluatedRow(
        row,
        currentPnl,
        isCappedOverHalf,
        skipOverHalfPnl,
        alwaysMarket.Pnl,
        alwaysMarket.Computable,
        alwaysMarket.Price,
        alwaysMarket.NotionalUsd,
        alwaysMarket.SizeShares,
        alwaysMarket.Reason);
}

static PricingInfo ParsePricing(string rawJson)
{
    if (string.IsNullOrWhiteSpace(rawJson))
    {
        return PricingInfo.Empty;
    }

    try
    {
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;
        var instantRawLimitPrice = GetDecimal(root, "instant_raw_limit_price");
        var instantLimitPrice = GetDecimal(root, "instant_limit_price") ?? GetDecimal(root, "limit_price");
        var instantMaxBuyPrice = GetDecimal(root, "instant_max_buy_price");
        var effectiveMaxBuyPrice = instantMaxBuyPrice ?? DefaultMaxSimplePrice;
        var tickSize = GetDecimal(root, "instant_tick_size") ??
            GetDecimal(root, "limit_price_tick_size");
        var effectiveTickSize = tickSize ?? DefaultTickSize;
        var minOrderSize = GetDecimal(root, "instant_min_order_size");
        var targetNotionalUsd = GetDecimal(root, "instant_target_notional_usd") ?? GetDecimal(root, "target_notional_usd");
        var targetSizeShares = GetDecimal(root, "instant_target_size_shares") ?? GetDecimal(root, "target_size_shares");
        var restingAtMax = GetBool(root, "instant_resting_at_max_price") ?? false;
        var priceMode = GetString(root, "opening_limit_price_mode");
        var rawOverMax = instantRawLimitPrice is > 0m &&
            effectiveMaxBuyPrice > 0m &&
            instantRawLimitPrice.Value > effectiveMaxBuyPrice + 0.00000001m;
        var limitAtOrBelowMax = instantLimitPrice is > 0m &&
            effectiveMaxBuyPrice > 0m &&
            instantLimitPrice.Value <= effectiveMaxBuyPrice + 0.00000001m;
        var capped = restingAtMax || (
            string.Equals(priceMode, "instant_executable_ask_depth", StringComparison.OrdinalIgnoreCase) &&
            rawOverMax &&
            limitAtOrBelowMax);

        return new PricingInfo(
            instantRawLimitPrice,
            instantLimitPrice,
            effectiveMaxBuyPrice,
            effectiveTickSize,
            minOrderSize,
            targetNotionalUsd,
            targetSizeShares,
            restingAtMax,
            capped,
            priceMode ?? string.Empty);
    }
    catch (JsonException)
    {
        return PricingInfo.Empty;
    }
}

static AlwaysMarketResult CalculateAlwaysMarketPnl(SourceRow row, PricingInfo pricing, decimal currentPnl)
{
    var selectedOutcome = NormalizeOutcome(row.SelectedOutcome);
    var winningOutcome = NormalizeOutcome(row.WinningOutcome);
    if (string.IsNullOrEmpty(selectedOutcome) || string.IsNullOrEmpty(winningOutcome))
    {
        if (!pricing.IsCappedOverHalf && string.Equals(row.Status, "Settled", StringComparison.OrdinalIgnoreCase))
        {
            return new AlwaysMarketResult(true, currentPnl, row.EntryPrice ?? row.PaperOrderPrice, row.PaperOrderNotionalUsd, row.SizeShares ?? row.PaperOrderSizeShares, "current_fallback_missing_result");
        }

        return new AlwaysMarketResult(false, 0m, null, null, null, "missing_market_result");
    }

    var marketPrice = pricing.InstantRawLimitPrice is > 0m
        ? RoundUpToTick(pricing.InstantRawLimitPrice.Value, pricing.TickSize is > 0m ? pricing.TickSize.Value : DefaultTickSize)
        : pricing.InstantLimitPrice is > 0m && !pricing.IsCappedOverHalf
            ? pricing.InstantLimitPrice.Value
            : row.EntryPrice is > 0m && !pricing.IsCappedOverHalf
                ? row.EntryPrice.Value
                : (decimal?)null;

    if (marketPrice is not > 0m || marketPrice.Value >= 1m)
    {
        if (!pricing.IsCappedOverHalf && string.Equals(row.Status, "Settled", StringComparison.OrdinalIgnoreCase))
        {
            return new AlwaysMarketResult(true, currentPnl, row.EntryPrice ?? row.PaperOrderPrice, row.PaperOrderNotionalUsd, row.SizeShares ?? row.PaperOrderSizeShares, "current_fallback_missing_market_price");
        }

        return new AlwaysMarketResult(false, 0m, marketPrice, null, null, "missing_market_price");
    }

    var stakeMultiplier = row.StakeUsd > 0m ? row.StakeUsd : 1m;
    var sizing = CalculateMarketSizing(marketPrice.Value, pricing.MinOrderSize, stakeMultiplier, pricing.TargetNotionalUsd);
    if (sizing.NotionalUsd <= 0m || sizing.SizeShares <= 0m)
    {
        return new AlwaysMarketResult(false, 0m, marketPrice, sizing.NotionalUsd, sizing.SizeShares, "invalid_market_sizing");
    }

    var pnl = string.Equals(selectedOutcome, winningOutcome, StringComparison.OrdinalIgnoreCase)
        ? sizing.SizeShares - sizing.NotionalUsd
        : -sizing.NotionalUsd;

    return new AlwaysMarketResult(true, pnl, marketPrice, sizing.NotionalUsd, sizing.SizeShares, "computed");
}

static MarketSizing CalculateMarketSizing(decimal marketPrice, decimal? minOrderSize, decimal stakeMultiplier, decimal? targetNotionalFallback)
{
    if (marketPrice <= 0m)
    {
        return new MarketSizing(0m, 0m);
    }

    if (minOrderSize is > 0m)
    {
        var rawTargetNotionalUsd = minOrderSize.Value * marketPrice * MinimumStakeSafetyMultiplier * stakeMultiplier;
        var roundedTargetNotionalUsd = RoundUp(rawTargetNotionalUsd, 0);
        var targetSizeShares = RoundUp(roundedTargetNotionalUsd / marketPrice, 2);
        return new MarketSizing(targetSizeShares * marketPrice, targetSizeShares);
    }

    var fallbackNotional = targetNotionalFallback is > 0m
        ? targetNotionalFallback.Value
        : stakeMultiplier;
    return new MarketSizing(fallbackNotional, fallbackNotional / marketPrice);
}

static List<DailyStrategyRow> BuildDailyByStrategy(
    IReadOnlyList<StrategyInfo> strategies,
    IReadOnlyList<EvaluatedRow> rows)
{
    var grouped = rows
        .GroupBy(row => new { row.Source.MarketDateUtc, row.Source.StrategyId })
        .ToDictionary(group => (group.Key.MarketDateUtc, group.Key.StrategyId), group => Aggregate(group));

    var dates = rows
        .Select(row => row.Source.MarketDateUtc)
        .Distinct()
        .OrderBy(date => date)
        .ToArray();

    var result = new List<DailyStrategyRow>();
    foreach (var date in dates)
    {
        foreach (var strategy in strategies)
        {
            grouped.TryGetValue((date, strategy.Id), out var stats);
            stats ??= Aggregate([]);
            result.Add(new DailyStrategyRow(date, strategy.Code, strategy.Name, stats));
        }
    }

    return result;
}

static List<DailyTotalRow> BuildTotalsByDate(IReadOnlyList<DailyStrategyRow> rows)
{
    return rows
        .GroupBy(row => row.DateUtc)
        .OrderBy(group => group.Key)
        .Select(group => new DailyTotalRow(group.Key, SumStats(group.Select(row => row.Stats))))
        .ToList();
}

static List<StrategyTotalRow> BuildTotalsByStrategy(
    IReadOnlyList<StrategyInfo> strategies,
    IReadOnlyList<DailyStrategyRow> rows)
{
    return strategies
        .Select(strategy =>
        {
            var stats = SumStats(rows
                .Where(row => row.StrategyCode == strategy.Code)
                .Select(row => row.Stats));
            return new StrategyTotalRow(strategy.Code, strategy.Name, stats);
        })
        .OrderBy(row => row.StrategyCode, StringComparer.Ordinal)
        .ToList();
}

static AggregateStats BuildGrandTotal(IReadOnlyList<DailyStrategyRow> rows)
{
    return SumStats(rows.Select(row => row.Stats));
}

static AggregateStats Aggregate(IEnumerable<EvaluatedRow> rows)
{
    var stats = new AggregateStats();
    foreach (var row in rows)
    {
        stats.DecisionCount++;
        stats.CurrentPnl += row.CurrentPnl;
        stats.SkipOverHalfPnl += row.SkipOverHalfPnl;
        stats.AlwaysMarketPnl += row.AlwaysMarketPnl;
        if (string.Equals(row.Source.Status, "Settled", StringComparison.OrdinalIgnoreCase))
        {
            stats.CurrentSettledCount++;
        }

        if (string.Equals(row.Source.SkipReason, "gtd_limit_not_filled", StringComparison.OrdinalIgnoreCase))
        {
            stats.GtdLimitNotFilledCount++;
        }

        if (row.IsCappedOverHalf)
        {
            stats.CappedOverHalfCount++;
            if (string.Equals(row.Source.Status, "Settled", StringComparison.OrdinalIgnoreCase))
            {
                stats.CappedOverHalfSettledCount++;
            }
            else
            {
                stats.CappedOverHalfUnfilledCount++;
            }
        }

        if (row.AlwaysMarketComputable)
        {
            stats.AlwaysMarketComputableCount++;
        }
        else
        {
            stats.AlwaysMarketNotComputableCount++;
        }
    }

    return stats;
}

static AggregateStats SumStats(IEnumerable<AggregateStats> source)
{
    var total = new AggregateStats();
    foreach (var stats in source)
    {
        total.DecisionCount += stats.DecisionCount;
        total.CurrentSettledCount += stats.CurrentSettledCount;
        total.GtdLimitNotFilledCount += stats.GtdLimitNotFilledCount;
        total.CappedOverHalfCount += stats.CappedOverHalfCount;
        total.CappedOverHalfSettledCount += stats.CappedOverHalfSettledCount;
        total.CappedOverHalfUnfilledCount += stats.CappedOverHalfUnfilledCount;
        total.AlwaysMarketComputableCount += stats.AlwaysMarketComputableCount;
        total.AlwaysMarketNotComputableCount += stats.AlwaysMarketNotComputableCount;
        total.CurrentPnl += stats.CurrentPnl;
        total.SkipOverHalfPnl += stats.SkipOverHalfPnl;
        total.AlwaysMarketPnl += stats.AlwaysMarketPnl;
    }

    return total;
}

static void WriteDailyByStrategyCsv(string path, IReadOnlyList<DailyStrategyRow> rows)
{
    var lines = new List<string[]>
    {
        new[]
        {
            "DateUtc",
            "StrategyCode",
            "StrategyName",
            "CurrentPnl",
            "SkipOver0.50Pnl",
            "AlwaysMarketPnl",
            "SkipVsCurrent",
            "AlwaysMarketVsCurrent",
            "Decisions",
            "CurrentSettled",
            "GtdLimitNotFilled",
            "CappedOver0.50",
            "CappedOver0.50Settled",
            "CappedOver0.50Unfilled",
            "AlwaysMarketComputable",
            "AlwaysMarketNotComputable"
        }
    };
    lines.AddRange(rows.Select(row => CsvValues(row.DateUtc, row.StrategyCode, row.StrategyName, row.Stats)));
    WriteCsv(path, lines);
}

static void WriteDailyTotalsCsv(string path, IReadOnlyList<DailyTotalRow> rows)
{
    var lines = new List<string[]>
    {
        new[]
        {
            "DateUtc",
            "CurrentPnl",
            "SkipOver0.50Pnl",
            "AlwaysMarketPnl",
            "SkipVsCurrent",
            "AlwaysMarketVsCurrent",
            "Decisions",
            "CurrentSettled",
            "GtdLimitNotFilled",
            "CappedOver0.50",
            "CappedOver0.50Settled",
            "CappedOver0.50Unfilled",
            "AlwaysMarketComputable",
            "AlwaysMarketNotComputable"
        }
    };
    lines.AddRange(rows.Select(row => CsvValues(row.DateUtc, null, null, row.Stats)));
    WriteCsv(path, lines);
}

static void WriteStrategyTotalsCsv(string path, IReadOnlyList<StrategyTotalRow> rows)
{
    var lines = new List<string[]>
    {
        new[]
        {
            "StrategyCode",
            "StrategyName",
            "CurrentPnl",
            "SkipOver0.50Pnl",
            "AlwaysMarketPnl",
            "SkipVsCurrent",
            "AlwaysMarketVsCurrent",
            "Decisions",
            "CurrentSettled",
            "GtdLimitNotFilled",
            "CappedOver0.50",
            "CappedOver0.50Settled",
            "CappedOver0.50Unfilled",
            "AlwaysMarketComputable",
            "AlwaysMarketNotComputable"
        }
    };
    lines.AddRange(rows.Select(row => CsvValues(null, row.StrategyCode, row.StrategyName, row.Stats)));
    WriteCsv(path, lines);
}

static string[] CsvValues(DateOnly? date, string? strategyCode, string? strategyName, AggregateStats stats)
{
    var values = new List<string>();
    if (date is { } dateValue)
    {
        values.Add(dateValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    if (strategyCode is not null)
    {
        values.Add(strategyCode);
    }

    if (strategyName is not null)
    {
        values.Add(strategyName);
    }

    values.AddRange(new[]
    {
        FormatDecimal(stats.CurrentPnl),
        FormatDecimal(stats.SkipOverHalfPnl),
        FormatDecimal(stats.AlwaysMarketPnl),
        FormatDecimal(stats.SkipOverHalfPnl - stats.CurrentPnl),
        FormatDecimal(stats.AlwaysMarketPnl - stats.CurrentPnl),
        stats.DecisionCount.ToString(CultureInfo.InvariantCulture),
        stats.CurrentSettledCount.ToString(CultureInfo.InvariantCulture),
        stats.GtdLimitNotFilledCount.ToString(CultureInfo.InvariantCulture),
        stats.CappedOverHalfCount.ToString(CultureInfo.InvariantCulture),
        stats.CappedOverHalfSettledCount.ToString(CultureInfo.InvariantCulture),
        stats.CappedOverHalfUnfilledCount.ToString(CultureInfo.InvariantCulture),
        stats.AlwaysMarketComputableCount.ToString(CultureInfo.InvariantCulture),
        stats.AlwaysMarketNotComputableCount.ToString(CultureInfo.InvariantCulture)
    });
    return values.ToArray();
}

static void WriteCsv(string path, IEnumerable<string[]> rows)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    foreach (var row in rows)
    {
        writer.WriteLine(string.Join(",", row.Select(EscapeCsv)));
    }
}

static string EscapeCsv(string value)
{
    if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    return value;
}

static void CreateWorkbook(
    string path,
    DateTimeOffset capturedAtUtc,
    IReadOnlyList<StrategyInfo> strategies,
    IReadOnlyList<DailyTotalRow> dailyTotals,
    IReadOnlyList<DailyStrategyRow> dailyByStrategy,
    IReadOnlyList<StrategyTotalRow> strategyTotals,
    AggregateStats grandTotal)
{
    using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
    var workbookPart = document.AddWorkbookPart();
    workbookPart.Workbook = new Workbook(new BookViews(new WorkbookView()));

    var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
    stylesPart.Stylesheet = BuildStylesheet();
    stylesPart.Stylesheet.Save();

    var sheets = workbookPart.Workbook.AppendChild(new Sheets());
    AppendSheet(workbookPart, sheets, 1U, "Daily Totals", BuildDailyTotalsWorksheet(dailyTotals, grandTotal));
    AppendSheet(workbookPart, sheets, 2U, "Daily By Strategy", BuildDailyByStrategyWorksheet(dailyByStrategy));
    AppendSheet(workbookPart, sheets, 3U, "Strategy Totals", BuildStrategyTotalsWorksheet(strategyTotals, grandTotal));
    AppendSheet(workbookPart, sheets, 4U, "Assumptions", BuildAssumptionsWorksheet(capturedAtUtc, strategies, grandTotal));

    workbookPart.Workbook.Append(new CalculationProperties
    {
        CalculationMode = CalculateModeValues.Auto,
        FullCalculationOnLoad = true,
        ForceFullCalculation = true
    });
    workbookPart.Workbook.Save();
}

static void AppendSheet(WorkbookPart workbookPart, Sheets sheets, uint sheetId, string name, Worksheet worksheet)
{
    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    worksheetPart.Worksheet = worksheet;
    worksheetPart.Worksheet.Save();
    sheets.Append(new Sheet
    {
        Id = workbookPart.GetIdOfPart(worksheetPart),
        SheetId = sheetId,
        Name = name
    });
}

static Worksheet BuildDailyTotalsWorksheet(IReadOnlyList<DailyTotalRow> rows, AggregateStats grandTotal)
{
    var sheetData = new SheetData();
    AppendTitleRow(sheetData, "A", 1U, "Daily totals across all Simple strategies");
    AppendHeaderRow(sheetData, 3U, DailyTotalHeaders());
    var rowIndex = 4U;
    foreach (var row in rows)
    {
        AppendStatsRow(sheetData, rowIndex++, row.DateUtc, null, null, row.Stats);
    }

    AppendStatsRow(sheetData, rowIndex, null, "TOTAL", null, grandTotal, totalStyle: true);
    return BuildWorksheet(sheetData, BuildStatsColumns(includeStrategy: false), $"A3:N{Math.Max(3, rowIndex)}");
}

static Worksheet BuildDailyByStrategyWorksheet(IReadOnlyList<DailyStrategyRow> rows)
{
    var sheetData = new SheetData();
    AppendTitleRow(sheetData, "A", 1U, "Daily by strategy");
    AppendHeaderRow(sheetData, 3U, DailyByStrategyHeaders());
    var rowIndex = 4U;
    foreach (var row in rows)
    {
        AppendStatsRow(sheetData, rowIndex++, row.DateUtc, row.StrategyCode, row.StrategyName, row.Stats);
    }

    return BuildWorksheet(sheetData, BuildStatsColumns(includeStrategy: true), $"A3:P{Math.Max(3, rowIndex - 1)}");
}

static Worksheet BuildStrategyTotalsWorksheet(IReadOnlyList<StrategyTotalRow> rows, AggregateStats grandTotal)
{
    var sheetData = new SheetData();
    AppendTitleRow(sheetData, "A", 1U, "All-time totals by Simple strategy");
    AppendHeaderRow(sheetData, 3U, StrategyTotalHeaders());
    var rowIndex = 4U;
    foreach (var row in rows)
    {
        AppendStatsRow(sheetData, rowIndex++, null, row.StrategyCode, row.StrategyName, row.Stats);
    }

    AppendStatsRow(sheetData, rowIndex, null, "TOTAL", null, grandTotal, totalStyle: true);
    return BuildWorksheet(sheetData, BuildStatsColumns(includeStrategy: true, includeDate: false), $"A3:O{Math.Max(3, rowIndex)}");
}

static Worksheet BuildAssumptionsWorksheet(
    DateTimeOffset capturedAtUtc,
    IReadOnlyList<StrategyInfo> strategies,
    AggregateStats grandTotal)
{
    var sheetData = new SheetData();
    AppendTitleRow(sheetData, "A", 1U, "Assumptions and coverage");
    var rows = new[]
    {
        ("CapturedAtUtc", capturedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
        ("Source", "Production PostgreSQL read-only repeatable-read transaction"),
        ("Scope", "Paper strategy_market_paper_runs for strategy codes matching <asset>_up_down_5m_(up|down)_simple"),
        ("Day", "UTC market_start date, not settlement date, so skipped counterfactual signals can be placed on a day"),
        ("CurrentPnl", "Existing run.realized_pnl_usd for settled runs; skipped/unfilled rows count as 0"),
        ("SkipOver0.50Pnl", "Same as CurrentPnl except decisions where Simple rested at capped 0.50 because executable ask was above 0.50 are treated as no trade / 0 PnL"),
        ("AlwaysMarketPnl", "Counterfactual entry at saved instant_raw_limit_price rounded up to tick; sizing follows min_order_size * price * 1.10 * stake multiplier where available"),
        ("MarketResult", "Winning outcome from websocket ledger, then result polling, then inferred settled run fallback"),
        ("SimpleStrategies", string.Join("; ", strategies.Select(strategy => strategy.Code))),
        ("DecisionCount", grandTotal.DecisionCount.ToString(CultureInfo.InvariantCulture)),
        ("CappedOver0.50Decisions", grandTotal.CappedOverHalfCount.ToString(CultureInfo.InvariantCulture)),
        ("AlwaysMarketComputable", grandTotal.AlwaysMarketComputableCount.ToString(CultureInfo.InvariantCulture)),
        ("AlwaysMarketNotComputable", grandTotal.AlwaysMarketNotComputableCount.ToString(CultureInfo.InvariantCulture))
    };

    AppendHeaderRow(sheetData, 3U, ["Key", "Value"]);
    var rowIndex = 4U;
    foreach (var row in rows)
    {
        AppendRow(sheetData, rowIndex, [
            TextCell("A", rowIndex, row.Item1, 2U),
            TextCell("B", rowIndex, row.Item2, 2U)
        ]);
        rowIndex++;
    }

    return BuildWorksheet(sheetData, BuildAssumptionsColumns(), $"A3:B{Math.Max(3, rowIndex - 1)}");
}

static Worksheet BuildWorksheet(SheetData sheetData, Columns columns, string autoFilterReference)
{
    return new Worksheet(
        BuildSheetViews(),
        columns,
        sheetData,
        new AutoFilter { Reference = autoFilterReference },
        new PageMargins { Left = 0.7D, Right = 0.7D, Top = 0.75D, Bottom = 0.75D, Header = 0.3D, Footer = 0.3D });
}

static void AppendTitleRow(SheetData sheetData, string columnName, uint rowIndex, string title)
{
    AppendRow(sheetData, rowIndex, [TextCell(columnName, rowIndex, title, 1U)]);
}

static void AppendHeaderRow(SheetData sheetData, uint rowIndex, IReadOnlyList<string> headers)
{
    var cells = headers
        .Select((header, index) => TextCell(ColumnName(index + 1), rowIndex, header, 1U))
        .ToArray();
    AppendRow(sheetData, rowIndex, cells);
}

static void AppendStatsRow(
    SheetData sheetData,
    uint rowIndex,
    DateOnly? date,
    string? strategyCode,
    string? strategyName,
    AggregateStats stats,
    bool totalStyle = false)
{
    var style = totalStyle ? 4U : 2U;
    var numberStyle = totalStyle ? 5U : 3U;
    var cells = new List<Cell>();
    var column = 1;
    if (date is not null)
    {
        cells.Add(TextCell(ColumnName(column++), rowIndex, date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), style));
    }

    if (strategyCode is not null)
    {
        cells.Add(TextCell(ColumnName(column++), rowIndex, strategyCode, style));
    }

    if (strategyName is not null)
    {
        cells.Add(TextCell(ColumnName(column++), rowIndex, strategyName, style));
    }

    var values = new[]
    {
        stats.CurrentPnl,
        stats.SkipOverHalfPnl,
        stats.AlwaysMarketPnl,
        stats.SkipOverHalfPnl - stats.CurrentPnl,
        stats.AlwaysMarketPnl - stats.CurrentPnl,
        (decimal)stats.DecisionCount,
        stats.CurrentSettledCount,
        stats.GtdLimitNotFilledCount,
        stats.CappedOverHalfCount,
        stats.CappedOverHalfSettledCount,
        stats.CappedOverHalfUnfilledCount,
        stats.AlwaysMarketComputableCount,
        stats.AlwaysMarketNotComputableCount
    };

    foreach (var value in values)
    {
        cells.Add(NumberCell(ColumnName(column++), rowIndex, value, numberStyle));
    }

    AppendRow(sheetData, rowIndex, cells);
}

static string[] DailyTotalHeaders()
{
    return [
        "DateUtc",
        "CurrentPnl",
        "SkipOver0.50Pnl",
        "AlwaysMarketPnl",
        "SkipVsCurrent",
        "AlwaysMarketVsCurrent",
        "Decisions",
        "CurrentSettled",
        "GtdLimitNotFilled",
        "CappedOver0.50",
        "CappedOver0.50Settled",
        "CappedOver0.50Unfilled",
        "AlwaysMarketComputable",
        "AlwaysMarketNotComputable"
    ];
}

static string[] DailyByStrategyHeaders()
{
    return [
        "DateUtc",
        "StrategyCode",
        "StrategyName",
        "CurrentPnl",
        "SkipOver0.50Pnl",
        "AlwaysMarketPnl",
        "SkipVsCurrent",
        "AlwaysMarketVsCurrent",
        "Decisions",
        "CurrentSettled",
        "GtdLimitNotFilled",
        "CappedOver0.50",
        "CappedOver0.50Settled",
        "CappedOver0.50Unfilled",
        "AlwaysMarketComputable",
        "AlwaysMarketNotComputable"
    ];
}

static string[] StrategyTotalHeaders()
{
    return [
        "StrategyCode",
        "StrategyName",
        "CurrentPnl",
        "SkipOver0.50Pnl",
        "AlwaysMarketPnl",
        "SkipVsCurrent",
        "AlwaysMarketVsCurrent",
        "Decisions",
        "CurrentSettled",
        "GtdLimitNotFilled",
        "CappedOver0.50",
        "CappedOver0.50Settled",
        "CappedOver0.50Unfilled",
        "AlwaysMarketComputable",
        "AlwaysMarketNotComputable"
    ];
}

static Columns BuildStatsColumns(bool includeStrategy, bool includeDate = true)
{
    var columns = new Columns();
    var column = 1U;
    if (includeDate)
    {
        columns.Append(new Column { Min = column, Max = column, Width = 13D, CustomWidth = true });
        column++;
    }

    if (includeStrategy)
    {
        columns.Append(new Column { Min = column, Max = column, Width = 36D, CustomWidth = true });
        column++;
        columns.Append(new Column { Min = column, Max = column, Width = 38D, CustomWidth = true });
        column++;
    }

    for (; column <= 16U; column++)
    {
        columns.Append(new Column { Min = column, Max = column, Width = 17D, CustomWidth = true });
    }

    return columns;
}

static Columns BuildAssumptionsColumns()
{
    return new Columns(
        new Column { Min = 1U, Max = 1U, Width = 30D, CustomWidth = true },
        new Column { Min = 2U, Max = 2U, Width = 120D, CustomWidth = true });
}

static SheetViews BuildSheetViews()
{
    return new SheetViews(
        new SheetView(
            new Pane
            {
                VerticalSplit = 3D,
                TopLeftCell = "A4",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen
            },
            new Selection
            {
                Pane = PaneValues.BottomLeft,
                ActiveCell = "A4",
                SequenceOfReferences = new ListValue<StringValue> { InnerText = "A4" }
            })
        { WorkbookViewId = 0U });
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
            BorderId = 1U,
            ApplyFont = true,
            ApplyFill = true,
            ApplyBorder = true,
            Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Left },
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
    { Count = 6U };

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
    IReadOnlyList<DailyTotalRow> dailyTotals,
    IReadOnlyList<DailyStrategyRow> dailyByStrategy,
    IReadOnlyList<StrategyTotalRow> strategyTotals,
    AggregateStats grandTotal)
{
    using var document = SpreadsheetDocument.Open(path, false);
    var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("Workbook part is missing.");
    var workbook = workbookPart.Workbook ?? throw new InvalidOperationException("Workbook is missing.");
    var sheets = workbook.Sheets?.Elements<Sheet>().ToArray() ?? [];
    if (sheets.Length != 4)
    {
        throw new InvalidOperationException($"Expected 4 worksheets, got {sheets.Length}.");
    }

    if (dailyTotals.Count == 0 || dailyByStrategy.Count == 0 || strategyTotals.Count == 0)
    {
        throw new InvalidOperationException("Report must contain non-empty daily and strategy rows.");
    }

    var totalCurrent = dailyTotals.Sum(row => row.Stats.CurrentPnl);
    var totalSkip = dailyTotals.Sum(row => row.Stats.SkipOverHalfPnl);
    var totalAlways = dailyTotals.Sum(row => row.Stats.AlwaysMarketPnl);
    if (Math.Abs(totalCurrent - grandTotal.CurrentPnl) > 0.00000001m ||
        Math.Abs(totalSkip - grandTotal.SkipOverHalfPnl) > 0.00000001m ||
        Math.Abs(totalAlways - grandTotal.AlwaysMarketPnl) > 0.00000001m)
    {
        throw new InvalidOperationException("Daily totals do not reconcile to grand totals.");
    }

    var strategyCurrent = strategyTotals.Sum(row => row.Stats.CurrentPnl);
    if (Math.Abs(strategyCurrent - grandTotal.CurrentPnl) > 0.00000001m)
    {
        throw new InvalidOperationException("Strategy totals do not reconcile to grand total.");
    }
}

static decimal? GetNullableDecimal(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}

static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
}

static decimal? GetDecimal(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out var element))
    {
        return null;
    }

    return element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetDecimal(out var value) ? value : null,
        JsonValueKind.String => decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null,
        _ => null
    };
}

static bool? GetBool(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out var element))
    {
        return null;
    }

    return element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(element.GetString(), out var value) ? value : null,
        _ => null
    };
}

static string? GetString(JsonElement root, string propertyName)
{
    return root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
        ? element.GetString()
        : null;
}

static string NormalizeOutcome(string value)
{
    if (string.Equals(value, "Up", StringComparison.OrdinalIgnoreCase))
    {
        return "Up";
    }

    if (string.Equals(value, "Down", StringComparison.OrdinalIgnoreCase))
    {
        return "Down";
    }

    return string.Empty;
}

static decimal RoundUpToTick(decimal value, decimal tickSize)
{
    if (tickSize <= 0m)
    {
        return value;
    }

    return Math.Ceiling(value / tickSize) * tickSize;
}

static decimal RoundUp(decimal value, int decimals)
{
    var factor = (decimal)Math.Pow(10, decimals);
    return Math.Ceiling(value * factor) / factor;
}

static string FormatDecimal(decimal value)
{
    return value.ToString("0.########", CultureInfo.InvariantCulture);
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

internal sealed record StrategyInfo(Guid Id, string Code, string Name);

internal sealed record SourceRow(
    Guid StrategyId,
    string StrategyCode,
    string StrategyName,
    Guid RunId,
    string MarketId,
    string ConditionId,
    string MarketSlug,
    DateOnly MarketDateUtc,
    string Status,
    string SkipReason,
    string SelectedOutcome,
    decimal? EntryPrice,
    decimal StakeUsd,
    decimal? SizeShares,
    decimal? RealizedPnlUsd,
    DateTimeOffset? SettledAtUtc,
    string PaperOrderStatus,
    decimal? PaperOrderPrice,
    decimal? PaperOrderSizeShares,
    decimal? PaperOrderNotionalUsd,
    string WinningOutcome,
    string ResultSource,
    string RawJson);

internal sealed record PricingInfo(
    decimal? InstantRawLimitPrice,
    decimal? InstantLimitPrice,
    decimal? InstantMaxBuyPrice,
    decimal? TickSize,
    decimal? MinOrderSize,
    decimal? TargetNotionalUsd,
    decimal? TargetSizeShares,
    bool InstantRestingAtMaxPrice,
    bool IsCappedOverHalf,
    string PriceMode)
{
    public static PricingInfo Empty { get; } = new(null, null, null, null, null, null, null, false, false, string.Empty);
}

internal sealed record MarketSizing(decimal NotionalUsd, decimal SizeShares);

internal sealed record AlwaysMarketResult(
    bool Computable,
    decimal Pnl,
    decimal? Price,
    decimal? NotionalUsd,
    decimal? SizeShares,
    string Reason);

internal sealed record EvaluatedRow(
    SourceRow Source,
    decimal CurrentPnl,
    bool IsCappedOverHalf,
    decimal SkipOverHalfPnl,
    decimal AlwaysMarketPnl,
    bool AlwaysMarketComputable,
    decimal? AlwaysMarketPrice,
    decimal? AlwaysMarketNotionalUsd,
    decimal? AlwaysMarketSizeShares,
    string AlwaysMarketReason);

internal sealed class AggregateStats
{
    public int DecisionCount { get; set; }

    public int CurrentSettledCount { get; set; }

    public int GtdLimitNotFilledCount { get; set; }

    public int CappedOverHalfCount { get; set; }

    public int CappedOverHalfSettledCount { get; set; }

    public int CappedOverHalfUnfilledCount { get; set; }

    public int AlwaysMarketComputableCount { get; set; }

    public int AlwaysMarketNotComputableCount { get; set; }

    public decimal CurrentPnl { get; set; }

    public decimal SkipOverHalfPnl { get; set; }

    public decimal AlwaysMarketPnl { get; set; }
}

internal sealed record DailyStrategyRow(DateOnly DateUtc, string StrategyCode, string StrategyName, AggregateStats Stats);

internal sealed record DailyTotalRow(DateOnly DateUtc, AggregateStats Stats);

internal sealed record StrategyTotalRow(string StrategyCode, string StrategyName, AggregateStats Stats);
