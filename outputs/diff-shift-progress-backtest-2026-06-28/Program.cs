using System.Globalization;
using System.Text;

internal static class Program
{
    private const decimal Unit = 1m;

    private static async Task<int> Main()
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var inputPath = Path.Combine(
            repoRoot,
            "outputs",
            "binance-diff-time-chart-2026-06-28",
            "binance-diff-timeseries.csv");
        var outputDir = Path.Combine(repoRoot, "outputs", "diff-shift-progress-backtest-2026-06-28");

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine("Input CSV not found: " + inputPath);
            return 1;
        }

        Directory.CreateDirectory(outputDir);

        var candles = await LoadCandlesAsync(inputPath);
        var groupedCandles = candles
            .GroupBy(item => item.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => TrimFirstPartialUtcDay(group.OrderBy(item => item.OpenTimeUtc).ToArray()),
                StringComparer.OrdinalIgnoreCase);

        var strategies = new List<StrategyBacktest>();
        var pnlEvents = new List<PnlEvent>();

        foreach (var (asset, assetCandles) in groupedCandles)
        {
            if (assetCandles.Count == 0)
            {
                continue;
            }

            var assetStrategies = new[]
            {
                new StrategyBacktest(asset, "Up", "Down"),
                new StrategyBacktest(asset, "Down", "Up")
            };
            SimulateAsset(assetCandles, assetStrategies, pnlEvents);
            strategies.AddRange(assetStrategies);
        }

        var assetSummaries = BuildAssetSummaries(groupedCandles, strategies, pnlEvents);
        var dailyRows = strategies
            .SelectMany(item => item.DailyMetrics.Values)
            .Where(item => item.Entries > 0 || item.ShiftsApplied > 0)
            .OrderBy(item => item.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.StrategyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DayUtc)
            .ToArray();

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "diff-shift-progress-strategies.csv"),
            BuildStrategyCsv(strategies),
            Encoding.UTF8);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "diff-shift-progress-daily-by-strategy.csv"),
            BuildDailyCsv(dailyRows),
            Encoding.UTF8);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "diff-shift-progress-asset-summary.csv"),
            BuildAssetSummaryCsv(assetSummaries),
            Encoding.UTF8);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "diff-shift-progress-summary.md"),
            BuildMarkdownSummary(groupedCandles, strategies, assetSummaries),
            Encoding.UTF8);

        Console.WriteLine("Wrote Diff Shift Progress backtest outputs to " + outputDir);
        Console.WriteLine($"Strategies={strategies.Count}; candles={groupedCandles.Sum(item => item.Value.Count).ToString(CultureInfo.InvariantCulture)}");
        foreach (var summary in assetSummaries)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"{summary.AssetSymbol}: entries={summary.Entries}, settled={summary.SettledEntries}, pnl={summary.PnlUnits:0.##}, roi={summary.RoiPct:0.##}%, max_dd={summary.MaxDrawdownUnits:0.##}, min_equity={summary.MinEquityUnits:0.##}"));
        }

        return 0;
    }

    private static async Task<IReadOnlyList<Candle>> LoadCandlesAsync(string path)
    {
        var lines = await File.ReadAllLinesAsync(path);
        if (lines.Length < 2)
        {
            return [];
        }

        var header = ParseCsvLine(lines[0]);
        var indexes = header
            .Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);

        var candles = new List<Candle>(lines.Length - 1);
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var fields = ParseCsvLine(lines[i]);
            candles.Add(new Candle(
                fields[indexes["asset_symbol"]],
                DateTimeOffset.Parse(fields[indexes["open_time_utc"]], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                fields[indexes["direction"]]));
        }

        return candles;
    }

    private static IReadOnlyList<Candle> TrimFirstPartialUtcDay(IReadOnlyList<Candle> candles)
    {
        if (candles.Count == 0)
        {
            return [];
        }

        var first = candles[0].OpenTimeUtc;
        var firstDate = first.UtcDateTime.Date;
        if (first.TimeOfDay == TimeSpan.Zero)
        {
            return candles;
        }

        return candles
            .Where(item => item.OpenTimeUtc.UtcDateTime.Date > firstDate)
            .ToArray();
    }

    private static void SimulateAsset(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<StrategyBacktest> strategies,
        List<PnlEvent> pnlEvents)
    {
        for (var index = 0; index < candles.Count; index++)
        {
            if (index > 0)
            {
                foreach (var strategy in strategies)
                {
                    strategy.ApplyResolvedResult(candles[index - 1], pnlEvents);
                }
            }

            foreach (var strategy in strategies)
            {
                strategy.MarketsEvaluated++;
                strategy.EvaluateCurrentMarket(candles[index]);
            }
        }

        if (candles.Count > 0)
        {
            var lastCandle = candles[^1];
            foreach (var strategy in strategies)
            {
                strategy.ApplyResolvedResult(lastCandle, pnlEvents);
            }
        }
    }

    private static IReadOnlyList<AssetSummary> BuildAssetSummaries(
        IReadOnlyDictionary<string, IReadOnlyList<Candle>> candlesByAsset,
        IReadOnlyList<StrategyBacktest> strategies,
        IReadOnlyList<PnlEvent> pnlEvents)
    {
        var summaries = new List<AssetSummary>();
        foreach (var asset in candlesByAsset.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            var assetStrategies = strategies
                .Where(item => string.Equals(item.AssetSymbol, asset, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (assetStrategies.Length == 0 || candlesByAsset[asset].Count == 0)
            {
                continue;
            }

            summaries.Add(BuildAssetSummary(asset, candlesByAsset[asset], assetStrategies, pnlEvents));
        }

        summaries.Add(BuildAssetSummary(
            "ALL",
            candlesByAsset.Values.SelectMany(item => item).OrderBy(item => item.OpenTimeUtc).ToArray(),
            strategies,
            pnlEvents));
        return summaries;
    }

    private static AssetSummary BuildAssetSummary(
        string asset,
        IReadOnlyList<Candle> candles,
        IReadOnlyList<StrategyBacktest> strategies,
        IReadOnlyList<PnlEvent> pnlEvents)
    {
        var entries = strategies.Sum(item => item.Entries);
        var settledEntries = strategies.Sum(item => item.SettledEntries);
        var wins = strategies.Sum(item => item.Wins);
        var losses = strategies.Sum(item => item.Losses);
        var flatEntries = strategies.Sum(item => item.FlatEntries);
        var totalStake = strategies.Sum(item => item.TotalStakeUnits);
        var settledStake = strategies.Sum(item => item.SettledStakeUnits);
        var pnl = strategies.Sum(item => item.PnlUnits);
        var shifts = strategies.Sum(item => item.ShiftsApplied);
        var maxMultiplier = strategies.Max(item => item.MaxMultiplierSeen);
        var equityStats = CalculateEquityStats(
            string.Equals(asset, "ALL", StringComparison.OrdinalIgnoreCase)
                ? pnlEvents
                : pnlEvents.Where(item => string.Equals(item.AssetSymbol, asset, StringComparison.OrdinalIgnoreCase)));
        var bestPnl = strategies
            .OrderByDescending(item => item.PnlUnits)
            .ThenByDescending(item => item.RoiPct)
            .First();
        var worstPnl = strategies
            .OrderBy(item => item.PnlUnits)
            .ThenBy(item => item.RoiPct)
            .First();

        return new AssetSummary(
            asset,
            strategies.Count,
            candles.Count,
            candles.First().OpenTimeUtc,
            candles.Last().OpenTimeUtc,
            entries,
            settledEntries,
            wins,
            losses,
            flatEntries,
            totalStake,
            settledStake,
            pnl,
            settledStake == 0 ? 0m : pnl / settledStake * 100m,
            settledEntries == 0 ? 0m : (decimal)wins / settledEntries * 100m,
            shifts,
            maxMultiplier,
            equityStats.MaxDrawdownUnits,
            equityStats.MinEquityUnits,
            bestPnl.Name,
            bestPnl.PnlUnits,
            bestPnl.RoiPct,
            worstPnl.Name,
            worstPnl.PnlUnits,
            worstPnl.RoiPct);
    }

    private static EquityStats CalculateEquityStats(IEnumerable<PnlEvent> events)
    {
        var equity = 0m;
        var peak = 0m;
        var maxDrawdown = 0m;
        var minEquity = 0m;

        foreach (var item in events
            .OrderBy(item => item.SettledAtUtc)
            .ThenBy(item => item.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.StrategyName, StringComparer.OrdinalIgnoreCase))
        {
            equity += item.PnlUnits;
            if (equity > peak)
            {
                peak = equity;
            }

            var drawdown = peak - equity;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }

            if (equity < minEquity)
            {
                minEquity = equity;
            }
        }

        return new EquityStats(maxDrawdown, minEquity);
    }

    private static string BuildStrategyCsv(IEnumerable<StrategyBacktest> strategies)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset,strategy_name,trigger_side,target_outcome,markets_evaluated,entries,settled_entries,flat_entries,wins,losses,zero_skips,negative_skips,win_rate_pct,total_stake_units,settled_stake_units,pnl_units,roi_pct,avg_stake_units,max_multiplier,max_drawdown_units,min_equity_units,shifts_applied,final_up_count,final_down_count,final_effective_diff,final_sum,active_days,positive_days,negative_days,flat_days,first_entry_utc,last_entry_utc");
        foreach (var strategy in strategies
            .OrderBy(item => item.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.PnlUnits)
            .ThenByDescending(item => item.RoiPct))
        {
            builder.AppendCsv(strategy.AssetSymbol);
            builder.AppendCsv(strategy.Name);
            builder.AppendCsv(strategy.TriggerSide);
            builder.AppendCsv(strategy.TargetOutcome);
            builder.AppendCsv(strategy.MarketsEvaluated);
            builder.AppendCsv(strategy.Entries);
            builder.AppendCsv(strategy.SettledEntries);
            builder.AppendCsv(strategy.FlatEntries);
            builder.AppendCsv(strategy.Wins);
            builder.AppendCsv(strategy.Losses);
            builder.AppendCsv(strategy.ZeroSkips);
            builder.AppendCsv(strategy.NegativeSkips);
            builder.AppendCsv(strategy.WinRatePct);
            builder.AppendCsv(strategy.TotalStakeUnits);
            builder.AppendCsv(strategy.SettledStakeUnits);
            builder.AppendCsv(strategy.PnlUnits);
            builder.AppendCsv(strategy.RoiPct);
            builder.AppendCsv(strategy.AvgStakeUnits);
            builder.AppendCsv(strategy.MaxMultiplierSeen);
            builder.AppendCsv(strategy.MaxDrawdownUnits);
            builder.AppendCsv(strategy.MinEquityUnits);
            builder.AppendCsv(strategy.ShiftsApplied);
            builder.AppendCsv(strategy.UpCount);
            builder.AppendCsv(strategy.DownCount);
            builder.AppendCsv(strategy.EffectiveDiff);
            builder.AppendCsv(strategy.SumAmount);
            builder.AppendCsv(strategy.ActiveDays);
            builder.AppendCsv(strategy.PositiveDays);
            builder.AppendCsv(strategy.NegativeDays);
            builder.AppendCsv(strategy.FlatDays);
            builder.AppendCsv(strategy.FirstEntryUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "");
            builder.AppendCsv(strategy.LastEntryUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "", last: true);
        }

        return builder.ToString();
    }

    private static string BuildDailyCsv(IEnumerable<DailyStrategyMetrics> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset,strategy_name,trigger_side,target_outcome,day_utc,entries,settled_entries,flat_entries,wins,losses,total_stake_units,settled_stake_units,pnl_units,shifts_applied,max_multiplier");
        foreach (var row in rows)
        {
            builder.AppendCsv(row.AssetSymbol);
            builder.AppendCsv(row.StrategyName);
            builder.AppendCsv(row.TriggerSide);
            builder.AppendCsv(row.TargetOutcome);
            builder.AppendCsv(row.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            builder.AppendCsv(row.Entries);
            builder.AppendCsv(row.SettledEntries);
            builder.AppendCsv(row.FlatEntries);
            builder.AppendCsv(row.Wins);
            builder.AppendCsv(row.Losses);
            builder.AppendCsv(row.TotalStakeUnits);
            builder.AppendCsv(row.SettledStakeUnits);
            builder.AppendCsv(row.PnlUnits);
            builder.AppendCsv(row.ShiftsApplied);
            builder.AppendCsv(row.MaxMultiplierSeen, last: true);
        }

        return builder.ToString();
    }

    private static string BuildAssetSummaryCsv(IEnumerable<AssetSummary> summaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset,strategies,markets,first_market_utc,last_market_utc,entries,settled_entries,flat_entries,wins,losses,win_rate_pct,total_stake_units,settled_stake_units,pnl_units,roi_pct,shifts_applied,max_multiplier,max_drawdown_units,min_equity_units,best_pnl_strategy,best_pnl_units,best_pnl_roi_pct,worst_pnl_strategy,worst_pnl_units,worst_pnl_roi_pct");
        foreach (var summary in summaries)
        {
            builder.AppendCsv(summary.AssetSymbol);
            builder.AppendCsv(summary.StrategyCount);
            builder.AppendCsv(summary.Markets);
            builder.AppendCsv(summary.FirstMarketUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.LastMarketUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendCsv(summary.Entries);
            builder.AppendCsv(summary.SettledEntries);
            builder.AppendCsv(summary.FlatEntries);
            builder.AppendCsv(summary.Wins);
            builder.AppendCsv(summary.Losses);
            builder.AppendCsv(summary.WinRatePct);
            builder.AppendCsv(summary.TotalStakeUnits);
            builder.AppendCsv(summary.SettledStakeUnits);
            builder.AppendCsv(summary.PnlUnits);
            builder.AppendCsv(summary.RoiPct);
            builder.AppendCsv(summary.ShiftsApplied);
            builder.AppendCsv(summary.MaxMultiplierSeen);
            builder.AppendCsv(summary.MaxDrawdownUnits);
            builder.AppendCsv(summary.MinEquityUnits);
            builder.AppendCsv(summary.BestPnlStrategy);
            builder.AppendCsv(summary.BestPnlUnits);
            builder.AppendCsv(summary.BestPnlRoiPct);
            builder.AppendCsv(summary.WorstPnlStrategy);
            builder.AppendCsv(summary.WorstPnlUnits);
            builder.AppendCsv(summary.WorstPnlRoiPct, last: true);
        }

        return builder.ToString();
    }

    private static string BuildMarkdownSummary(
        IReadOnlyDictionary<string, IReadOnlyList<Candle>> candlesByAsset,
        IReadOnlyList<StrategyBacktest> strategies,
        IReadOnlyList<AssetSummary> assetSummaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Diff Shift Progress Backtest");
        builder.AppendLine();
        builder.AppendLine("Source: `outputs/binance-diff-time-chart-2026-06-28/binance-diff-timeseries.csv`.");
        builder.AppendLine("Model: six strategies, BTC/ETH/SOL x Up/Down. The Up side uses `Diff = UpCount - DownCount`; the Down side uses `Diff = DownCount - UpCount`. Counters and `Sum` are continuous after the first full UTC day. Before each market, the previous candle result settles the pending bet, updates counts, applies `while Sum > Unit && Diff > 1` shift, and then enters only when `Diff > 0`. Stake multiplier is `Diff + 1`.");
        builder.AppendLine("PnL model: fixed 0.50 binary odds, `Unit = 1`, so a settled winning stake earns `+stake` and a losing stake earns `-stake`. Binance `Flat` candles close pending entries with zero PnL and do not change Up/Down counts.");
        builder.AppendLine("The first partial UTC day in the six-month CSV is skipped per asset so counters start at a clean midnight.");
        builder.AppendLine();
        builder.AppendLine("## Asset Summary");
        builder.AppendLine();
        builder.AppendLine("| Asset | Strategies | Markets | Entries | Settled | Flat | PnL units | ROI | Max DD | Min equity | Shifts | Max mult | Best | Worst |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
        foreach (var summary in assetSummaries)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| {summary.AssetSymbol} | {summary.StrategyCount} | {summary.Markets} | {summary.Entries} | {summary.SettledEntries} | {summary.FlatEntries} | {summary.PnlUnits:0.##} | {summary.RoiPct:0.##}% | {summary.MaxDrawdownUnits:0.##} | {summary.MinEquityUnits:0.##} | {summary.ShiftsApplied} | {summary.MaxMultiplierSeen:0.##} | {EscapeMarkdown(summary.BestPnlStrategy)} ({summary.BestPnlUnits:0.##}) | {EscapeMarkdown(summary.WorstPnlStrategy)} ({summary.WorstPnlUnits:0.##}) |");
        }

        builder.AppendLine();
        builder.AppendLine("## Strategies");
        builder.AppendLine();
        builder.AppendLine("| Strategy | Entries | Settled | Win % | Stake | PnL | ROI | Max DD | Min equity | Shifts | Final Diff | Final Sum |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var strategy in strategies
            .OrderBy(item => item.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TriggerSide, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| {EscapeMarkdown(strategy.Name)} | {strategy.Entries} | {strategy.SettledEntries} | {strategy.WinRatePct:0.##}% | {strategy.SettledStakeUnits:0.##} | {strategy.PnlUnits:0.##} | {strategy.RoiPct:0.##}% | {strategy.MaxDrawdownUnits:0.##} | {strategy.MinEquityUnits:0.##} | {strategy.ShiftsApplied} | {strategy.EffectiveDiff} | {strategy.SumAmount:0.##} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Window");
        builder.AppendLine();
        foreach (var (asset, candles) in candlesByAsset.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (candles.Count == 0)
            {
                continue;
            }

            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- {asset}: {candles.Count} evaluated candles, {candles.First().OpenTimeUtc:O} through {candles.Last().OpenTimeUtc:O}.");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static string EscapeMarkdown(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private sealed record Candle(
        string AssetSymbol,
        DateTimeOffset OpenTimeUtc,
        string Direction);

    private sealed class StrategyBacktest
    {
        public StrategyBacktest(string assetSymbol, string triggerSide, string targetOutcome)
        {
            AssetSymbol = assetSymbol;
            TriggerSide = triggerSide;
            TargetOutcome = targetOutcome;
            Name = $"{assetSymbol} Up or Down 5m Diff {triggerSide} Shift Progress";
        }

        public string AssetSymbol { get; }

        public string Name { get; }

        public string TriggerSide { get; }

        public string TargetOutcome { get; }

        public int MarketsEvaluated { get; set; }

        public int UpCount { get; private set; }

        public int DownCount { get; private set; }

        public decimal SumAmount { get; private set; }

        public int Entries { get; private set; }

        public int SettledEntries { get; private set; }

        public int FlatEntries { get; private set; }

        public int Wins { get; private set; }

        public int Losses { get; private set; }

        public int ZeroSkips { get; private set; }

        public int NegativeSkips { get; private set; }

        public decimal TotalStakeUnits { get; private set; }

        public decimal SettledStakeUnits { get; private set; }

        public decimal PnlUnits { get; private set; }

        public decimal EquityUnits { get; private set; }

        public decimal PeakEquityUnits { get; private set; }

        public decimal MaxDrawdownUnits { get; private set; }

        public decimal MinEquityUnits { get; private set; }

        public decimal MaxMultiplierSeen { get; private set; }

        public int ShiftsApplied { get; private set; }

        public DateTimeOffset? FirstEntryUtc { get; private set; }

        public DateTimeOffset? LastEntryUtc { get; private set; }

        public Dictionary<DateTime, DailyStrategyMetrics> DailyMetrics { get; } = [];

        public int EffectiveDiff => string.Equals(TriggerSide, "Up", StringComparison.OrdinalIgnoreCase)
            ? UpCount - DownCount
            : DownCount - UpCount;

        public decimal WinRatePct => SettledEntries == 0 ? 0m : (decimal)Wins / SettledEntries * 100m;

        public decimal RoiPct => SettledStakeUnits == 0 ? 0m : PnlUnits / SettledStakeUnits * 100m;

        public decimal AvgStakeUnits => Entries == 0 ? 0m : TotalStakeUnits / Entries;

        public int ActiveDays => DailyMetrics.Values.Count(item => item.Entries > 0);

        public int PositiveDays => DailyMetrics.Values.Count(item => item.PnlUnits > 0m);

        public int NegativeDays => DailyMetrics.Values.Count(item => item.PnlUnits < 0m);

        public int FlatDays => DailyMetrics.Values.Count(item => item.Entries > 0 && item.PnlUnits == 0m);

        private PendingBet? Pending { get; set; }

        public void ApplyResolvedResult(Candle candle, List<PnlEvent> pnlEvents)
        {
            if (Pending is { } pending && pending.MarketStartUtc == candle.OpenTimeUtc)
            {
                SettlePending(candle, pending, pnlEvents);
                Pending = null;
            }

            if (string.Equals(candle.Direction, "Up", StringComparison.OrdinalIgnoreCase))
            {
                UpCount++;
            }
            else if (string.Equals(candle.Direction, "Down", StringComparison.OrdinalIgnoreCase))
            {
                DownCount++;
            }

            ApplyShift(candle.OpenTimeUtc.UtcDateTime.Date);
        }

        public void EvaluateCurrentMarket(Candle candle)
        {
            if (Pending is not null)
            {
                return;
            }

            var diff = EffectiveDiff;
            if (diff <= 0)
            {
                if (diff == 0)
                {
                    ZeroSkips++;
                }
                else
                {
                    NegativeSkips++;
                }

                return;
            }

            var multiplier = diff + 1m;
            var stake = Unit * multiplier;
            Entries++;
            TotalStakeUnits += stake;
            MaxMultiplierSeen = Math.Max(MaxMultiplierSeen, multiplier);
            FirstEntryUtc ??= candle.OpenTimeUtc;
            LastEntryUtc = candle.OpenTimeUtc;

            var daily = GetDaily(candle.OpenTimeUtc.UtcDateTime.Date);
            daily.Entries++;
            daily.TotalStakeUnits += stake;
            daily.MaxMultiplierSeen = Math.Max(daily.MaxMultiplierSeen, multiplier);

            Pending = new PendingBet(
                candle.OpenTimeUtc,
                TargetOutcome,
                stake,
                candle.OpenTimeUtc.UtcDateTime.Date);
        }

        private void SettlePending(Candle candle, PendingBet pending, List<PnlEvent> pnlEvents)
        {
            var daily = GetDaily(pending.EntryDayUtc);
            if (!string.Equals(candle.Direction, "Up", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candle.Direction, "Down", StringComparison.OrdinalIgnoreCase))
            {
                FlatEntries++;
                daily.FlatEntries++;
                return;
            }

            var won = string.Equals(candle.Direction, pending.TargetOutcome, StringComparison.OrdinalIgnoreCase);
            var pnl = won ? pending.StakeUnits : -pending.StakeUnits;
            SettledEntries++;
            SettledStakeUnits += pending.StakeUnits;
            PnlUnits += pnl;
            EquityUnits += pnl;
            SumAmount += pnl;
            if (won)
            {
                Wins++;
                daily.Wins++;
            }
            else
            {
                Losses++;
                daily.Losses++;
            }

            if (EquityUnits > PeakEquityUnits)
            {
                PeakEquityUnits = EquityUnits;
            }

            var drawdown = PeakEquityUnits - EquityUnits;
            if (drawdown > MaxDrawdownUnits)
            {
                MaxDrawdownUnits = drawdown;
            }

            if (EquityUnits < MinEquityUnits)
            {
                MinEquityUnits = EquityUnits;
            }

            daily.SettledEntries++;
            daily.SettledStakeUnits += pending.StakeUnits;
            daily.PnlUnits += pnl;
            pnlEvents.Add(new PnlEvent(AssetSymbol, Name, candle.OpenTimeUtc, pnl));
        }

        private void ApplyShift(DateTime resultDayUtc)
        {
            var shiftsToday = 0;
            while (SumAmount > Unit && EffectiveDiff > 1)
            {
                if (string.Equals(TriggerSide, "Up", StringComparison.OrdinalIgnoreCase))
                {
                    UpCount = Math.Max(0, UpCount - 1);
                }
                else
                {
                    DownCount = Math.Max(0, DownCount - 1);
                }

                SumAmount -= Unit;
                ShiftsApplied++;
                shiftsToday++;
            }

            if (shiftsToday > 0)
            {
                GetDaily(resultDayUtc).ShiftsApplied += shiftsToday;
            }
        }

        private DailyStrategyMetrics GetDaily(DateTime dayUtc)
        {
            if (!DailyMetrics.TryGetValue(dayUtc, out var metrics))
            {
                metrics = new DailyStrategyMetrics(AssetSymbol, Name, TriggerSide, TargetOutcome, dayUtc);
                DailyMetrics[dayUtc] = metrics;
            }

            return metrics;
        }
    }

    private sealed record PendingBet(
        DateTimeOffset MarketStartUtc,
        string TargetOutcome,
        decimal StakeUnits,
        DateTime EntryDayUtc);

    private sealed record PnlEvent(
        string AssetSymbol,
        string StrategyName,
        DateTimeOffset SettledAtUtc,
        decimal PnlUnits);

    private sealed record EquityStats(
        decimal MaxDrawdownUnits,
        decimal MinEquityUnits);

    private sealed class DailyStrategyMetrics(
        string assetSymbol,
        string strategyName,
        string triggerSide,
        string targetOutcome,
        DateTime dayUtc)
    {
        public string AssetSymbol { get; } = assetSymbol;

        public string StrategyName { get; } = strategyName;

        public string TriggerSide { get; } = triggerSide;

        public string TargetOutcome { get; } = targetOutcome;

        public DateTime DayUtc { get; } = dayUtc;

        public int Entries { get; set; }

        public int SettledEntries { get; set; }

        public int FlatEntries { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public decimal TotalStakeUnits { get; set; }

        public decimal SettledStakeUnits { get; set; }

        public decimal PnlUnits { get; set; }

        public int ShiftsApplied { get; set; }

        public decimal MaxMultiplierSeen { get; set; }
    }

    private sealed record AssetSummary(
        string AssetSymbol,
        int StrategyCount,
        int Markets,
        DateTimeOffset FirstMarketUtc,
        DateTimeOffset LastMarketUtc,
        int Entries,
        int SettledEntries,
        int Wins,
        int Losses,
        int FlatEntries,
        decimal TotalStakeUnits,
        decimal SettledStakeUnits,
        decimal PnlUnits,
        decimal RoiPct,
        decimal WinRatePct,
        int ShiftsApplied,
        decimal MaxMultiplierSeen,
        decimal MaxDrawdownUnits,
        decimal MinEquityUnits,
        string BestPnlStrategy,
        decimal BestPnlUnits,
        decimal BestPnlRoiPct,
        string WorstPnlStrategy,
        decimal WorstPnlUnits,
        decimal WorstPnlRoiPct);
}

internal static class CsvExtensions
{
    public static void AppendCsv(this StringBuilder builder, string value, bool last = false)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            builder.Append('"');
            builder.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            builder.Append('"');
        }
        else
        {
            builder.Append(value);
        }

        builder.Append(last ? Environment.NewLine : ",");
    }

    public static void AppendCsv(this StringBuilder builder, int value, bool last = false)
    {
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
        builder.Append(last ? Environment.NewLine : ",");
    }

    public static void AppendCsv(this StringBuilder builder, decimal value, bool last = false)
    {
        builder.Append(value.ToString("0.########", CultureInfo.InvariantCulture));
        builder.Append(last ? Environment.NewLine : ",");
    }
}
