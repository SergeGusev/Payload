using System.Globalization;
using System.Text;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 2;
}

var outputRoot = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0]
    : Path.Combine("outputs", "diff-strategy-backtest-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
var counterMode = args.Length > 1 && string.Equals(args[1], "dynamic", StringComparison.OrdinalIgnoreCase)
    ? CounterMode.Dynamic
    : CounterMode.Raw;
Directory.CreateDirectory(outputRoot);

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

var markets = await LoadMarketsAsync(connection);
if (markets.Count == 0)
{
    Console.Error.WriteLine("No market rows loaded.");
    return 1;
}

var thresholds = Enumerable.Range(1, 10)
    .Concat(Enumerable.Range(3, 28).Select(value => value * 5))
    .ToArray();
var strategyResults = new List<StrategyResult>();
var assetSummaries = new List<AssetSummary>();

foreach (var assetGroup in markets.GroupBy(market => market.AssetSymbol).OrderBy(group => group.Key, StringComparer.Ordinal))
{
    var ordered = assetGroup
        .OrderBy(market => market.MarketStartUtc)
        .ToArray();
    var resultsByKey = CreateResultsForAsset(assetGroup.Key, thresholds);
    var state = new DiffState(counterMode);
    var signalMarketCount = 0;
    var instantCapSkippedOrders = 0;
    var instantMissingPriceOrders = 0;
    var unsettledStrongOrders = 0;
    var unsettledBinanceOrders = 0;

    foreach (var market in ordered)
    {
        var diff = state.Diff;
        var absDiff = Math.Abs(diff);
        if (absDiff > 0)
        {
            signalMarketCount++;
            var groupName = diff > 0 ? "Up" : "Down";
            var targetOutcome = diff > 0 ? "Down" : "Up";
            var selectedAsk = targetOutcome == "Up" ? market.EntryUpAsk : market.EntryDownAsk;
            foreach (var threshold in thresholds)
            {
                if (threshold > absDiff)
                {
                    continue;
                }

                var result = resultsByKey[(groupName, threshold)];
                result.SignalMarkets++;
                result.MaxAbsDiffSeen = Math.Max(result.MaxAbsDiffSeen, absDiff);
                RecordFixedStrong(result, market, targetOutcome, ref unsettledStrongOrders);
                RecordFixedBinance(result, market, targetOutcome, ref unsettledBinanceOrders);
                RecordInstant(result, market, targetOutcome, selectedAsk, ref instantCapSkippedOrders, ref instantMissingPriceOrders, ref unsettledStrongOrders);
            }
        }

        if (market.StrongOddsOutcome is { } strongOutcome)
        {
            state.Apply(strongOutcome);
        }
    }

    strategyResults.AddRange(resultsByKey.Values);
    assetSummaries.Add(new AssetSummary(
        assetGroup.Key,
        ordered.Length,
        ordered.First().MarketStartUtc,
        ordered.Last().MarketStartUtc,
        ordered.Count(market => market.StrongOddsOutcome is not null),
        ordered.Count(market => market.BinanceOutcome is not null),
        signalMarketCount,
        resultsByKey.Values.Sum(result => result.SignalMarkets),
        resultsByKey.Values.Sum(result => result.Instant.Bets),
        resultsByKey.Values.Sum(result => result.Instant.Pnl),
        resultsByKey.Values.Sum(result => result.FixedStrong.Bets),
        resultsByKey.Values.Sum(result => result.FixedStrong.Pnl),
        instantCapSkippedOrders,
        instantMissingPriceOrders,
        unsettledStrongOrders,
        unsettledBinanceOrders));
}

var strategyCsv = Path.Combine(outputRoot, "diff-strategy-backtest-strategies.csv");
var summaryCsv = Path.Combine(outputRoot, "diff-strategy-backtest-summary.csv");
var markdownPath = Path.Combine(outputRoot, "diff-strategy-backtest-summary.md");

WriteStrategyCsv(strategyCsv, strategyResults);
WriteSummaryCsv(summaryCsv, assetSummaries);
WriteMarkdown(markdownPath, assetSummaries, strategyResults, counterMode);

Console.WriteLine("Wrote:");
Console.WriteLine(Path.GetFullPath(summaryCsv));
Console.WriteLine(Path.GetFullPath(strategyCsv));
Console.WriteLine(Path.GetFullPath(markdownPath));
return 0;

static async Task<List<MarketRow>> LoadMarketsAsync(NpgsqlConnection connection)
{
    const string sql = """
WITH btc_entry AS (
    SELECT DISTINCT ON (market_start_utc)
           'BTC'::text AS asset_symbol,
           market_start_utc,
           market_end_utc,
           sampled_at_utc AS entry_sampled_at_utc,
           seconds_after_start AS entry_seconds_after_start,
           up_best_ask AS entry_up_ask,
           down_best_ask AS entry_down_ask
    FROM btc_up_down_5m_odds_ticks
    WHERE seconds_after_start >= 0 AND seconds_after_start <= 60
    ORDER BY market_start_utc, sampled_at_utc ASC
), btc_final AS (
    SELECT DISTINCT ON (market_start_utc)
           'BTC'::text AS asset_symbol,
           market_start_utc,
           sampled_at_utc AS final_sampled_at_utc,
           seconds_after_start AS final_seconds_after_start,
           btc_move_from_start_bps AS move_from_start_bps,
           up_best_bid AS final_up_bid,
           up_best_ask AS final_up_ask,
           down_best_bid AS final_down_bid,
           down_best_ask AS final_down_ask
    FROM btc_up_down_5m_odds_ticks
    WHERE seconds_after_start >= 250
    ORDER BY market_start_utc, sampled_at_utc DESC
), crypto_entry AS (
    SELECT DISTINCT ON (asset_symbol, market_start_utc)
           asset_symbol,
           market_start_utc,
           market_end_utc,
           sampled_at_utc AS entry_sampled_at_utc,
           seconds_after_start AS entry_seconds_after_start,
           up_best_ask AS entry_up_ask,
           down_best_ask AS entry_down_ask
    FROM crypto_up_down_5m_odds_ticks
    WHERE asset_symbol IN ('ETH','SOL')
      AND seconds_after_start >= 0 AND seconds_after_start <= 60
    ORDER BY asset_symbol, market_start_utc, sampled_at_utc ASC
), crypto_final AS (
    SELECT DISTINCT ON (asset_symbol, market_start_utc)
           asset_symbol,
           market_start_utc,
           sampled_at_utc AS final_sampled_at_utc,
           seconds_after_start AS final_seconds_after_start,
           asset_move_from_start_bps AS move_from_start_bps,
           up_best_bid AS final_up_bid,
           up_best_ask AS final_up_ask,
           down_best_bid AS final_down_bid,
           down_best_ask AS final_down_ask
    FROM crypto_up_down_5m_odds_ticks
    WHERE asset_symbol IN ('ETH','SOL')
      AND seconds_after_start >= 250
    ORDER BY asset_symbol, market_start_utc, sampled_at_utc DESC
), all_entry AS (
    SELECT * FROM btc_entry
    UNION ALL
    SELECT * FROM crypto_entry
), all_final AS (
    SELECT * FROM btc_final
    UNION ALL
    SELECT * FROM crypto_final
)
SELECT entry.asset_symbol,
       entry.market_start_utc,
       entry.market_end_utc,
       entry.entry_sampled_at_utc,
       entry.entry_seconds_after_start,
       entry.entry_up_ask,
       entry.entry_down_ask,
       final.final_sampled_at_utc,
       final.final_seconds_after_start,
       final.move_from_start_bps,
       final.final_up_bid,
       final.final_up_ask,
       final.final_down_bid,
       final.final_down_ask
FROM all_entry entry
INNER JOIN all_final final
    ON final.asset_symbol = entry.asset_symbol
   AND final.market_start_utc = entry.market_start_utc
ORDER BY entry.asset_symbol, entry.market_start_utc;
""";

    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.CommandTimeout = 120;
    var rows = new List<MarketRow>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var finalUpBid = GetNullableDecimal(reader, 10);
        var finalUpAsk = GetNullableDecimal(reader, 11);
        var finalDownBid = GetNullableDecimal(reader, 12);
        var finalDownAsk = GetNullableDecimal(reader, 13);
        var strongOddsOutcome = GetStrongOddsOutcome(finalUpBid, finalUpAsk, finalDownBid, finalDownAsk);
        var moveBps = reader.GetDecimal(9);
        var binanceOutcome = moveBps > 0m
            ? "Up"
            : moveBps < 0m
                ? "Down"
                : null;
        rows.Add(new MarketRow(
            reader.GetString(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetDecimal(4),
            GetNullableDecimal(reader, 5),
            GetNullableDecimal(reader, 6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetDecimal(8),
            moveBps,
            finalUpBid,
            finalUpAsk,
            finalDownBid,
            finalDownAsk,
            strongOddsOutcome,
            binanceOutcome));
    }

    return rows;
}

static Dictionary<(string GroupName, int Threshold), StrategyResult> CreateResultsForAsset(string assetSymbol, int[] thresholds)
{
    var results = new Dictionary<(string GroupName, int Threshold), StrategyResult>();
    foreach (var groupName in new[] { "Up", "Down" })
    {
        foreach (var threshold in thresholds)
        {
            var targetOutcome = groupName == "Up" ? "Down" : "Up";
            var strategyName = $"{assetSymbol} Up or Down 5m {groupName} {threshold} Diff Instant";
            results[(groupName, threshold)] = new StrategyResult(assetSymbol, strategyName, groupName, threshold, targetOutcome);
        }
    }

    return results;
}

static void RecordFixedStrong(StrategyResult result, MarketRow market, string targetOutcome, ref int unsettledStrongOrders)
{
    if (market.StrongOddsOutcome is not { } outcome)
    {
        result.FixedStrong.Unsettled++;
        unsettledStrongOrders++;
        return;
    }

    result.FixedStrong.Record(string.Equals(outcome, targetOutcome, StringComparison.OrdinalIgnoreCase), 0.50m);
}

static void RecordFixedBinance(StrategyResult result, MarketRow market, string targetOutcome, ref int unsettledBinanceOrders)
{
    if (market.BinanceOutcome is not { } outcome)
    {
        result.FixedBinance.Unsettled++;
        unsettledBinanceOrders++;
        return;
    }

    result.FixedBinance.Record(string.Equals(outcome, targetOutcome, StringComparison.OrdinalIgnoreCase), 0.50m);
}

static void RecordInstant(
    StrategyResult result,
    MarketRow market,
    string targetOutcome,
    decimal? selectedAsk,
    ref int instantCapSkippedOrders,
    ref int instantMissingPriceOrders,
    ref int unsettledStrongOrders)
{
    if (market.StrongOddsOutcome is not { } outcome)
    {
        result.Instant.Unsettled++;
        unsettledStrongOrders++;
        return;
    }

    if (selectedAsk is null || selectedAsk <= 0m)
    {
        result.Instant.MissingEntryPrice++;
        instantMissingPriceOrders++;
        return;
    }

    if (selectedAsk > 0.65m)
    {
        result.Instant.PriceAboveCap++;
        instantCapSkippedOrders++;
        return;
    }

    result.Instant.Record(string.Equals(outcome, targetOutcome, StringComparison.OrdinalIgnoreCase), selectedAsk.Value);
}

static string? GetStrongOddsOutcome(decimal? upBid, decimal? upAsk, decimal? downBid, decimal? downAsk)
{
    var upHigh = MaxNullable(upBid, upAsk);
    var downHigh = MaxNullable(downBid, downAsk);
    var upLow = MinNullable(upBid, upAsk);
    var downLow = MinNullable(downBid, downAsk);

    if (upHigh is not null && downLow is not null && upHigh >= 0.80m && downLow <= 0.20m)
    {
        return "Up";
    }

    if (downHigh is not null && upLow is not null && downHigh >= 0.80m && upLow <= 0.20m)
    {
        return "Down";
    }

    return null;
}

static decimal? MaxNullable(decimal? first, decimal? second)
{
    if (first is null)
    {
        return second;
    }

    if (second is null)
    {
        return first;
    }

    return Math.Max(first.Value, second.Value);
}

static decimal? MinNullable(decimal? first, decimal? second)
{
    if (first is null)
    {
        return second;
    }

    if (second is null)
    {
        return first;
    }

    return Math.Min(first.Value, second.Value);
}

static decimal? GetNullableDecimal(NpgsqlDataReader reader, int index)
{
    return reader.IsDBNull(index) ? null : reader.GetDecimal(index);
}

static void WriteStrategyCsv(string path, IReadOnlyList<StrategyResult> results)
{
    var builder = new StringBuilder();
    builder.AppendLine("asset,strategy_name,diff_group,threshold,target_outcome,signal_markets,max_abs_diff_seen,instant_bets,instant_wins,instant_losses,instant_win_rate,instant_pnl_per_1usd,instant_roi_pct,instant_avg_entry_price,instant_price_above_cap,instant_missing_entry_price,instant_unsettled,strong_bets,strong_wins,strong_losses,strong_win_rate,strong_pnl_per_1usd,strong_roi_pct,strong_avg_entry_price,strong_price_above_cap,strong_missing_entry_price,strong_unsettled,binance_bets,binance_wins,binance_losses,binance_win_rate,binance_pnl_per_1usd,binance_roi_pct,binance_avg_entry_price,binance_price_above_cap,binance_missing_entry_price,binance_unsettled");
    foreach (var result in results.OrderBy(item => item.AssetSymbol).ThenBy(item => item.GroupName).ThenBy(item => item.Threshold))
    {
        builder.Append(Csv(result.AssetSymbol)).Append(',');
        builder.Append(Csv(result.StrategyName)).Append(',');
        builder.Append(Csv(result.GroupName)).Append(',');
        builder.Append(result.Threshold.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(Csv(result.TargetOutcome)).Append(',');
        builder.Append(result.SignalMarkets.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(result.MaxAbsDiffSeen.ToString(CultureInfo.InvariantCulture)).Append(',');
        AppendMetric(builder, result.Instant);
        builder.Append(',');
        AppendMetric(builder, result.FixedStrong);
        builder.Append(',');
        AppendMetric(builder, result.FixedBinance);
        builder.AppendLine();
    }

    File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
}

static void AppendMetric(StringBuilder builder, Metric metric)
{
    builder.Append(metric.Bets.ToString(CultureInfo.InvariantCulture)).Append(',');
    builder.Append(metric.Wins.ToString(CultureInfo.InvariantCulture)).Append(',');
    builder.Append(metric.Losses.ToString(CultureInfo.InvariantCulture)).Append(',');
    builder.Append(Format(metric.WinRate)).Append(',');
    builder.Append(Format(metric.Pnl)).Append(',');
    builder.Append(Format(metric.RoiPercent)).Append(',');
    builder.Append(Format(metric.AverageEntryPrice)).Append(',');
    builder.Append(metric.PriceAboveCap.ToString(CultureInfo.InvariantCulture)).Append(',');
    builder.Append(metric.MissingEntryPrice.ToString(CultureInfo.InvariantCulture)).Append(',');
    builder.Append(metric.Unsettled.ToString(CultureInfo.InvariantCulture));
}

static void WriteSummaryCsv(string path, IReadOnlyList<AssetSummary> summaries)
{
    var builder = new StringBuilder();
    builder.AppendLine("asset,markets,first_market_utc,last_market_utc,strong_odds_markets,binance_sign_markets,signal_markets,strategy_signal_orders,instant_bets,instant_pnl_per_1usd,instant_roi_pct,fixed05_strong_bets,fixed05_strong_pnl_per_1usd,fixed05_strong_roi_pct,instant_price_above_cap_orders,instant_missing_price_orders,strong_unsettled_orders,binance_unsettled_orders");
    foreach (var summary in summaries.OrderBy(item => item.AssetSymbol))
    {
        builder.Append(Csv(summary.AssetSymbol)).Append(',');
        builder.Append(summary.Markets.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(Csv(summary.FirstMarketUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(',');
        builder.Append(Csv(summary.LastMarketUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(',');
        builder.Append(summary.StrongOddsMarkets.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(summary.BinanceSignMarkets.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(summary.SignalMarkets.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(summary.StrategySignalOrders.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(summary.InstantBets.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(Format(summary.InstantPnl)).Append(',');
        builder.Append(Format(summary.InstantRoiPercent)).Append(',');
        builder.Append(summary.FixedStrongBets.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(Format(summary.FixedStrongPnl)).Append(',');
        builder.Append(Format(summary.FixedStrongRoiPercent)).Append(',');
        builder.Append(summary.InstantPriceAboveCapOrders.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(summary.InstantMissingPriceOrders.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(summary.StrongUnsettledOrders.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(summary.BinanceUnsettledOrders.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine();
    }

    File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
}

static void WriteMarkdown(string path, IReadOnlyList<AssetSummary> summaries, IReadOnlyList<StrategyResult> results, CounterMode counterMode)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Diff Strategy Backtest");
    builder.AppendLine();
    builder.Append("- Counter model: ");
    builder.AppendLine(counterMode == CounterMode.Dynamic
        ? "zero-start per asset at the first loaded historical market, dynamic `DiffCount` threshold +/-5."
        : "zero-start per asset at the first loaded historical market, raw `Diff = UpCount - DownCount` without dynamic zero adjustment.");
    builder.Append("- Entry signal: market `T` uses ");
    builder.Append(counterMode == CounterMode.Dynamic ? "trend-adjusted" : "raw");
    builder.AppendLine(" Diff after market `T-5m`; positive Diff buys `Down`, negative Diff buys `Up`.");
    builder.AppendLine("- `instant` model: first available odds tick in the first 60 seconds, selected outcome `best_ask <= 0.65`, settled only when the terminal odds result is strong (`winner >= 0.80`, loser `<= 0.20`).");
    builder.AppendLine("- `fixed05_strong` model: same strong terminal odds result, assumed entry price `0.50`.");
    builder.AppendLine("- `fixed05_binance` model: assumed entry price `0.50`, settled by the final Binance move sign; this is a coverage/sensitivity check, not the primary Polymarket settlement proxy.");
    builder.AppendLine();
    builder.AppendLine("## Asset Summary");
    builder.AppendLine();
    builder.AppendLine("| Asset | Markets | Period UTC | Instant bets | Instant PnL | Instant ROI | Fixed 0.50 strong bets | Fixed 0.50 PnL | Fixed 0.50 ROI |");
    builder.AppendLine("|---|---:|---|---:|---:|---:|---:|---:|---:|");
    foreach (var summary in summaries.OrderBy(item => item.AssetSymbol))
    {
        builder.Append("| ")
            .Append(summary.AssetSymbol)
            .Append(" | ")
            .Append(summary.Markets.ToString(CultureInfo.InvariantCulture))
            .Append(" | ")
            .Append(summary.FirstMarketUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            .Append("..")
            .Append(summary.LastMarketUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            .Append(" | ")
            .Append(summary.InstantBets.ToString(CultureInfo.InvariantCulture))
            .Append(" | ")
            .Append(Format(summary.InstantPnl))
            .Append(" | ")
            .Append(Format(summary.InstantRoiPercent))
            .Append("% | ")
            .Append(summary.FixedStrongBets.ToString(CultureInfo.InvariantCulture))
            .Append(" | ")
            .Append(Format(summary.FixedStrongPnl))
            .Append(" | ")
            .Append(Format(summary.FixedStrongRoiPercent))
            .AppendLine("% |");
    }

    AppendTopStrategies(builder, "Top Instant Strategies By PnL", results
        .Where(item => item.Instant.Bets >= 20)
        .OrderByDescending(item => item.Instant.Pnl)
        .ThenByDescending(item => item.Instant.RoiPercent)
        .Take(15)
        .ToArray(), metricSelector: item => item.Instant);
    AppendTopStrategies(builder, "Worst Instant Strategies By PnL", results
        .Where(item => item.Instant.Bets >= 20)
        .OrderBy(item => item.Instant.Pnl)
        .ThenBy(item => item.Instant.RoiPercent)
        .Take(15)
        .ToArray(), metricSelector: item => item.Instant);
    AppendTopStrategies(builder, "Top Fixed 0.50 Strong Strategies By PnL", results
        .Where(item => item.FixedStrong.Bets >= 20)
        .OrderByDescending(item => item.FixedStrong.Pnl)
        .ThenByDescending(item => item.FixedStrong.RoiPercent)
        .Take(15)
        .ToArray(), metricSelector: item => item.FixedStrong);

    File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
}

static void AppendTopStrategies(
    StringBuilder builder,
    string title,
    IReadOnlyList<StrategyResult> results,
    Func<StrategyResult, Metric> metricSelector)
{
    builder.AppendLine();
    builder.Append("## ").AppendLine(title);
    builder.AppendLine();
    builder.AppendLine("| Strategy | Bets | Wins | Win Rate | PnL | ROI | Avg Entry |");
    builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
    foreach (var result in results)
    {
        var metric = metricSelector(result);
        builder.Append("| ")
            .Append(result.StrategyName)
            .Append(" | ")
            .Append(metric.Bets.ToString(CultureInfo.InvariantCulture))
            .Append(" | ")
            .Append(metric.Wins.ToString(CultureInfo.InvariantCulture))
            .Append(" | ")
            .Append(Format(metric.WinRate))
            .Append("% | ")
            .Append(Format(metric.Pnl))
            .Append(" | ")
            .Append(Format(metric.RoiPercent))
            .Append("% | ")
            .Append(Format(metric.AverageEntryPrice))
            .AppendLine(" |");
    }
}

static string Csv(string? value)
{
    value ??= string.Empty;
    return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

static string Format(decimal? value)
{
    return value is null ? string.Empty : value.Value.ToString("0.########", CultureInfo.InvariantCulture);
}

sealed record MarketRow(
    string AssetSymbol,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset MarketEndUtc,
    DateTimeOffset EntrySampledAtUtc,
    decimal EntrySecondsAfterStart,
    decimal? EntryUpAsk,
    decimal? EntryDownAsk,
    DateTimeOffset FinalSampledAtUtc,
    decimal FinalSecondsAfterStart,
    decimal MoveFromStartBps,
    decimal? FinalUpBid,
    decimal? FinalUpAsk,
    decimal? FinalDownBid,
    decimal? FinalDownAsk,
    string? StrongOddsOutcome,
    string? BinanceOutcome);

enum CounterMode
{
    Dynamic,
    Raw
}

sealed class DiffState(CounterMode counterMode)
{
    private int upCount;
    private int downCount;
    private int diffCount;

    public int Diff => upCount - downCount;

    public void Apply(string outcome)
    {
        if (string.Equals(outcome, "Up", StringComparison.OrdinalIgnoreCase))
        {
            upCount++;
        }
        else if (string.Equals(outcome, "Down", StringComparison.OrdinalIgnoreCase))
        {
            downCount++;
        }
        else
        {
            return;
        }

        var diff = upCount - downCount;
        if (counterMode == CounterMode.Raw)
        {
            return;
        }

        diffCount += diff;
        if (diffCount >= 5 && upCount > 0)
        {
            diffCount--;
            upCount--;
        }
        else if (diffCount <= -5 && downCount > 0)
        {
            diffCount++;
            downCount--;
        }
    }
}

sealed class StrategyResult(
    string assetSymbol,
    string strategyName,
    string groupName,
    int threshold,
    string targetOutcome)
{
    public string AssetSymbol { get; } = assetSymbol;
    public string StrategyName { get; } = strategyName;
    public string GroupName { get; } = groupName;
    public int Threshold { get; } = threshold;
    public string TargetOutcome { get; } = targetOutcome;
    public int SignalMarkets { get; set; }
    public int MaxAbsDiffSeen { get; set; }
    public Metric Instant { get; } = new();
    public Metric FixedStrong { get; } = new();
    public Metric FixedBinance { get; } = new();
}

sealed class Metric
{
    private decimal entryPriceSum;

    public int Bets { get; private set; }
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public int PriceAboveCap { get; set; }
    public int MissingEntryPrice { get; set; }
    public int Unsettled { get; set; }
    public decimal Pnl { get; private set; }
    public decimal? WinRate => Bets == 0 ? null : Wins * 100m / Bets;
    public decimal? RoiPercent => Bets == 0 ? null : Pnl * 100m / Bets;
    public decimal? AverageEntryPrice => Bets == 0 ? null : entryPriceSum / Bets;

    public void Record(bool won, decimal entryPrice)
    {
        Bets++;
        entryPriceSum += entryPrice;
        if (won)
        {
            Wins++;
            Pnl += (1m / entryPrice) - 1m;
        }
        else
        {
            Losses++;
            Pnl -= 1m;
        }
    }
}

sealed record AssetSummary(
    string AssetSymbol,
    int Markets,
    DateTimeOffset FirstMarketUtc,
    DateTimeOffset LastMarketUtc,
    int StrongOddsMarkets,
    int BinanceSignMarkets,
    int SignalMarkets,
    int StrategySignalOrders,
    int InstantBets,
    decimal InstantPnl,
    int FixedStrongBets,
    decimal FixedStrongPnl,
    int InstantPriceAboveCapOrders,
    int InstantMissingPriceOrders,
    int StrongUnsettledOrders,
    int BinanceUnsettledOrders)
{
    public decimal? InstantRoiPercent => InstantBets == 0 ? null : InstantPnl * 100m / InstantBets;
    public decimal? FixedStrongRoiPercent => FixedStrongBets == 0 ? null : FixedStrongPnl * 100m / FixedStrongBets;
}
