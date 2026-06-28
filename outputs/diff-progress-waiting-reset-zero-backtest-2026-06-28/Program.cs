using System.Globalization;
using System.Text;

internal static class Program
{
    private const int MaxThreshold = 50;
    private const decimal MaxMultiplier = 10m;

    private static async Task<int> Main()
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var inputPath = Path.Combine(
            repoRoot,
            "outputs",
            "binance-diff-time-chart-2026-06-28",
            "binance-diff-timeseries.csv");
        var outputDir = Path.Combine(repoRoot, "outputs", "diff-progress-waiting-reset-zero-backtest-2026-06-28");

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
        var dailyRows = new List<DailyStrategyMetrics>();
        var assetSummaries = new List<AssetSummary>();

        foreach (var (asset, assetCandles) in groupedCandles)
        {
            if (assetCandles.Count == 0)
            {
                continue;
            }

            var assetStrategies = CreateStrategies(asset);
            SimulateAsset(assetCandles, assetStrategies, dailyRows);
            strategies.AddRange(assetStrategies);
            assetSummaries.Add(BuildAssetSummary(asset, assetCandles, assetStrategies));
        }

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "diff-progress-waiting-reset-zero-strategies.csv"),
            BuildStrategyCsv(strategies),
            Encoding.UTF8);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "diff-progress-waiting-reset-zero-daily-by-strategy.csv"),
            BuildDailyCsv(dailyRows),
            Encoding.UTF8);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "diff-progress-waiting-reset-zero-asset-summary.csv"),
            BuildAssetSummaryCsv(assetSummaries),
            Encoding.UTF8);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "diff-progress-waiting-reset-zero-summary.md"),
            BuildMarkdownSummary(groupedCandles, strategies, assetSummaries),
            Encoding.UTF8);

        Console.WriteLine("Wrote Diff Progress waiting-reset-zero backtest outputs to " + outputDir);
        Console.WriteLine($"Strategies={strategies.Count}; candles={groupedCandles.Sum(item => item.Value.Count).ToString(CultureInfo.InvariantCulture)}");
        foreach (var summary in assetSummaries)
        {
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{summary.AssetSymbol}: strategies={summary.StrategyCount}, entries={summary.Entries}, settled={summary.SettledEntries}, pnl={summary.PnlUnits:0.##}, roi={summary.RoiPct:0.##}%"));
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
            var asset = fields[indexes["asset_symbol"]];
            candles.Add(new Candle(
                asset,
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

    private static IReadOnlyList<StrategyBacktest> CreateStrategies(string asset)
    {
        var strategies = new List<StrategyBacktest>(MaxThreshold * 2);
        for (var threshold = 1; threshold <= MaxThreshold; threshold++)
        {
            strategies.Add(new StrategyBacktest(asset, "Up", "Down", threshold));
            strategies.Add(new StrategyBacktest(asset, "Down", "Up", threshold));
        }

        return strategies;
    }

    private static void SimulateAsset(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<StrategyBacktest> strategies,
        List<DailyStrategyMetrics> dailyRows)
    {
        foreach (var candle in candles)
        {
            foreach (var strategy in strategies)
            {
                strategy.MarketsEvaluated++;
                strategy.Evaluate(candle);
            }
        }

        foreach (var strategy in strategies)
        {
            dailyRows.AddRange(strategy.DailyMetrics.Values
                .Where(item => item.Entries > 0)
                .OrderBy(item => item.DayUtc));
        }
    }

    private static AssetSummary BuildAssetSummary(
        string asset,
        IReadOnlyList<Candle> candles,
        IReadOnlyList<StrategyBacktest> strategies)
    {
        var bestPnl = strategies
            .OrderByDescending(item => item.PnlUnits)
            .ThenByDescending(item => item.RoiPct)
            .First();
        var bestRoi = strategies
            .Where(item => item.SettledEntries >= 50)
            .OrderByDescending(item => item.RoiPct)
            .ThenByDescending(item => item.PnlUnits)
            .FirstOrDefault() ?? bestPnl;
        var worstPnl = strategies
            .OrderBy(item => item.PnlUnits)
            .ThenBy(item => item.RoiPct)
            .First();

        var entries = strategies.Sum(item => item.Entries);
        var settledEntries = strategies.Sum(item => item.SettledEntries);
        var wins = strategies.Sum(item => item.Wins);
        var losses = strategies.Sum(item => item.Losses);
        var flatEntries = strategies.Sum(item => item.FlatEntries);
        var totalStake = strategies.Sum(item => item.TotalStakeUnits);
        var settledStake = strategies.Sum(item => item.SettledStakeUnits);
        var pnl = strategies.Sum(item => item.PnlUnits);
        return new AssetSummary(
            asset,
            strategies.Count,
            strategies.Count(item => item.OpenAtEnd),
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
            settledStake == 0 ? 0 : pnl / settledStake * 100m,
            settledEntries == 0 ? 0 : (decimal)wins / settledEntries * 100m,
            bestPnl.Name,
            bestPnl.PnlUnits,
            bestPnl.RoiPct,
            bestRoi.Name,
            bestRoi.PnlUnits,
            bestRoi.RoiPct,
            worstPnl.Name,
            worstPnl.PnlUnits,
            worstPnl.RoiPct);
    }

    private static string BuildStrategyCsv(IEnumerable<StrategyBacktest> strategies)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset,strategy_name,trigger_side,target_outcome,threshold,markets_evaluated,betting_episodes,open_at_end,entries,settled_entries,flat_entries,wins,losses,win_rate_pct,total_stake_units,settled_stake_units,pnl_units,roi_pct,avg_stake_units,max_multiplier,capped_entries,max_drawdown_units,active_days,positive_days,negative_days,flat_days,first_entry_utc,last_entry_utc");
        foreach (var strategy in strategies
            .OrderBy(item => item.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.PnlUnits)
            .ThenByDescending(item => item.RoiPct))
        {
            builder.AppendCsv(strategy.AssetSymbol);
            builder.AppendCsv(strategy.Name);
            builder.AppendCsv(strategy.TriggerSide);
            builder.AppendCsv(strategy.TargetOutcome);
            builder.AppendCsv(strategy.Threshold);
            builder.AppendCsv(strategy.MarketsEvaluated);
            builder.AppendCsv(strategy.BettingEpisodes);
            builder.AppendCsv(strategy.OpenAtEnd ? "true" : "false");
            builder.AppendCsv(strategy.Entries);
            builder.AppendCsv(strategy.SettledEntries);
            builder.AppendCsv(strategy.FlatEntries);
            builder.AppendCsv(strategy.Wins);
            builder.AppendCsv(strategy.Losses);
            builder.AppendCsv(strategy.WinRatePct);
            builder.AppendCsv(strategy.TotalStakeUnits);
            builder.AppendCsv(strategy.SettledStakeUnits);
            builder.AppendCsv(strategy.PnlUnits);
            builder.AppendCsv(strategy.RoiPct);
            builder.AppendCsv(strategy.AvgStakeUnits);
            builder.AppendCsv(strategy.MaxMultiplierSeen);
            builder.AppendCsv(strategy.CappedEntries);
            builder.AppendCsv(strategy.MaxDrawdownUnits);
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
        builder.AppendLine("asset,strategy_name,trigger_side,target_outcome,threshold,day_utc,entries,settled_entries,flat_entries,wins,losses,total_stake_units,settled_stake_units,pnl_units,capped_entries,max_multiplier");
        foreach (var row in rows
            .OrderBy(item => item.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.StrategyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DayUtc))
        {
            builder.AppendCsv(row.AssetSymbol);
            builder.AppendCsv(row.StrategyName);
            builder.AppendCsv(row.TriggerSide);
            builder.AppendCsv(row.TargetOutcome);
            builder.AppendCsv(row.Threshold);
            builder.AppendCsv(row.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            builder.AppendCsv(row.Entries);
            builder.AppendCsv(row.SettledEntries);
            builder.AppendCsv(row.FlatEntries);
            builder.AppendCsv(row.Wins);
            builder.AppendCsv(row.Losses);
            builder.AppendCsv(row.TotalStakeUnits);
            builder.AppendCsv(row.SettledStakeUnits);
            builder.AppendCsv(row.PnlUnits);
            builder.AppendCsv(row.CappedEntries);
            builder.AppendCsv(row.MaxMultiplierSeen, last: true);
        }

        return builder.ToString();
    }

    private static string BuildAssetSummaryCsv(IEnumerable<AssetSummary> summaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("asset,strategies,open_strategies_at_end,markets,first_market_utc,last_market_utc,entries,settled_entries,flat_entries,wins,losses,win_rate_pct,total_stake_units,settled_stake_units,pnl_units,roi_pct,best_pnl_strategy,best_pnl_units,best_pnl_roi_pct,best_roi_strategy,best_roi_pnl_units,best_roi_pct,worst_pnl_strategy,worst_pnl_units,worst_pnl_roi_pct");
        foreach (var summary in summaries)
        {
            builder.AppendCsv(summary.AssetSymbol);
            builder.AppendCsv(summary.StrategyCount);
            builder.AppendCsv(summary.OpenStrategiesAtEnd);
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
            builder.AppendCsv(summary.BestPnlStrategy);
            builder.AppendCsv(summary.BestPnlUnits);
            builder.AppendCsv(summary.BestPnlRoiPct);
            builder.AppendCsv(summary.BestRoiStrategy);
            builder.AppendCsv(summary.BestRoiPnlUnits);
            builder.AppendCsv(summary.BestRoiPct);
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
        builder.AppendLine("# Diff Progress Waiting-Reset-Zero Backtest");
        builder.AppendLine();
        builder.AppendLine("Source: `outputs/binance-diff-time-chart-2026-06-28/binance-diff-timeseries.csv`.");
        builder.AppendLine("Model: each strategy owns its own counters because reset behavior depends on that strategy's mode; evaluate each 5m market using previous resolved outcomes; while `Waiting`, reset counters at every UTC day boundary; while `Betting`, carry counters across midnight and keep betting until side-specific `Diff <= 0`; enter when side-specific `Diff > N`; buy the opposite outcome; stake multiplier is `min(max(Diff - N, 1), 10)` so bets continue below `N` down to zero.");
        builder.AppendLine("PnL model: fixed 0.50 binary odds, so a settled winning stake earns `+stake` and a losing stake earns `-stake`; Binance `Flat` candles are counted as `flat_entries` and excluded from settled PnL/ROI because they do not provide an Up/Down result.");
        builder.AppendLine("The first partial UTC day in the six-month CSV is skipped per asset so initial waiting-mode counters start from midnight.");
        builder.AppendLine();
        builder.AppendLine("## Asset Summary");
        builder.AppendLine();
        builder.AppendLine("| Asset | Markets | Open@end | Entries | Settled | Flat | PnL units | ROI | Best PnL strategy | Worst PnL strategy |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|---|");
        foreach (var summary in assetSummaries)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| {summary.AssetSymbol} | {summary.Markets} | {summary.OpenStrategiesAtEnd} | {summary.Entries} | {summary.SettledEntries} | {summary.FlatEntries} | {summary.PnlUnits:0.##} | {summary.RoiPct:0.##}% | {EscapeMarkdown(summary.BestPnlStrategy)} ({summary.BestPnlUnits:0.##}) | {EscapeMarkdown(summary.WorstPnlStrategy)} ({summary.WorstPnlUnits:0.##}) |");
        }

        builder.AppendLine();
        builder.AppendLine("## Top Strategies By PnL");
        builder.AppendLine();
        AppendStrategyTable(builder, strategies.OrderByDescending(item => item.PnlUnits).ThenByDescending(item => item.RoiPct).Take(15));
        builder.AppendLine();
        builder.AppendLine("## Top Strategies By ROI (min 50 settled bets)");
        builder.AppendLine();
        AppendStrategyTable(builder, strategies.Where(item => item.SettledEntries >= 50).OrderByDescending(item => item.RoiPct).ThenByDescending(item => item.PnlUnits).Take(15));
        builder.AppendLine();
        builder.AppendLine("## Worst Strategies By PnL");
        builder.AppendLine();
        AppendStrategyTable(builder, strategies.OrderBy(item => item.PnlUnits).ThenBy(item => item.RoiPct).Take(15));
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

    private static void AppendStrategyTable(StringBuilder builder, IEnumerable<StrategyBacktest> strategies)
    {
        builder.AppendLine("| Strategy | Entries | Settled | Win % | Stake | PnL | ROI | Max DD | Capped |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var strategy in strategies)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| {EscapeMarkdown(strategy.Name)} | {strategy.Entries} | {strategy.SettledEntries} | {strategy.WinRatePct:0.##}% | {strategy.SettledStakeUnits:0.##} | {strategy.PnlUnits:0.##} | {strategy.RoiPct:0.##}% | {strategy.MaxDrawdownUnits:0.##} | {strategy.CappedEntries} |");
        }
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
        public StrategyBacktest(string assetSymbol, string triggerSide, string targetOutcome, int threshold)
        {
            AssetSymbol = assetSymbol;
            TriggerSide = triggerSide;
            TargetOutcome = targetOutcome;
            Threshold = threshold;
            Name = $"{assetSymbol} Up or Down 5m {threshold.ToString(CultureInfo.InvariantCulture)} Diff {triggerSide} Progress";
        }

        public string AssetSymbol { get; }

        public string Name { get; }

        public string TriggerSide { get; }

        public string TargetOutcome { get; }

        public int Threshold { get; }

        public int MarketsEvaluated { get; set; }

        public int BettingEpisodes { get; private set; }

        public bool OpenAtEnd => bettingMode;

        public int Entries { get; private set; }

        public int SettledEntries => Wins + Losses;

        public int FlatEntries { get; private set; }

        public int Wins { get; private set; }

        public int Losses { get; private set; }

        public decimal TotalStakeUnits { get; private set; }

        public decimal SettledStakeUnits { get; private set; }

        public decimal PnlUnits { get; private set; }

        public decimal MaxMultiplierSeen { get; private set; }

        public int CappedEntries { get; private set; }

        public decimal Equity { get; private set; }

        public decimal PeakEquity { get; private set; }

        public decimal MaxDrawdownUnits { get; private set; }

        public DateTimeOffset? FirstEntryUtc { get; private set; }

        public DateTimeOffset? LastEntryUtc { get; private set; }

        public Dictionary<DateTime, DailyStrategyMetrics> DailyMetrics { get; } = [];

        public int ActiveDays => DailyMetrics.Values.Count(item => item.Entries > 0);

        public int PositiveDays => DailyMetrics.Values.Count(item => item.PnlUnits > 0);

        public int NegativeDays => DailyMetrics.Values.Count(item => item.PnlUnits < 0);

        public int FlatDays => DailyMetrics.Values.Count(item => item.Entries > 0 && item.PnlUnits == 0);

        public decimal WinRatePct => SettledEntries == 0 ? 0 : (decimal)Wins / SettledEntries * 100m;

        public decimal RoiPct => SettledStakeUnits == 0 ? 0 : PnlUnits / SettledStakeUnits * 100m;

        public decimal AvgStakeUnits => Entries == 0 ? 0 : TotalStakeUnits / Entries;

        private bool bettingMode;

        private DateTime? currentCounterDayUtc;

        private int upCount;

        private int downCount;

        public void Evaluate(Candle candle)
        {
            HandleDayBoundary(candle.OpenTimeUtc.UtcDateTime.Date);

            var rawDiffBeforeMarket = upCount - downCount;
            var effectiveDiff = string.Equals(TriggerSide, "Up", StringComparison.OrdinalIgnoreCase)
                ? rawDiffBeforeMarket
                : -rawDiffBeforeMarket;

            if (!bettingMode && effectiveDiff <= Threshold)
            {
                RecordCounterOutcome(candle);
                return;
            }

            if (!bettingMode)
            {
                bettingMode = true;
                BettingEpisodes++;
            }
            else if (effectiveDiff <= 0)
            {
                bettingMode = false;
                ResetCounters();
                RecordCounterOutcome(candle);
                return;
            }

            var uncappedMultiplier = effectiveDiff - Threshold;
            var multiplier = Math.Min(Math.Max(uncappedMultiplier, 1m), MaxMultiplier);
            if (multiplier <= 0)
            {
                RecordCounterOutcome(candle);
                return;
            }

            Entries++;
            TotalStakeUnits += multiplier;
            MaxMultiplierSeen = Math.Max(MaxMultiplierSeen, multiplier);
            if (uncappedMultiplier > MaxMultiplier)
            {
                CappedEntries++;
            }

            FirstEntryUtc ??= candle.OpenTimeUtc;
            LastEntryUtc = candle.OpenTimeUtc;

            var dayMetrics = GetDailyMetrics(candle.OpenTimeUtc.UtcDateTime.Date);
            dayMetrics.RecordEntry(multiplier, uncappedMultiplier > MaxMultiplier);

            if (string.Equals(candle.Direction, "Flat", StringComparison.OrdinalIgnoreCase))
            {
                FlatEntries++;
                dayMetrics.FlatEntries++;
                RecordCounterOutcome(candle);
                return;
            }

            SettledStakeUnits += multiplier;
            dayMetrics.SettledStakeUnits += multiplier;
            var won = string.Equals(candle.Direction, TargetOutcome, StringComparison.OrdinalIgnoreCase);
            var pnl = won ? multiplier : -multiplier;
            if (won)
            {
                Wins++;
                dayMetrics.Wins++;
            }
            else
            {
                Losses++;
                dayMetrics.Losses++;
            }

            PnlUnits += pnl;
            dayMetrics.PnlUnits += pnl;
            Equity += pnl;
            if (Equity > PeakEquity)
            {
                PeakEquity = Equity;
            }

            MaxDrawdownUnits = Math.Max(MaxDrawdownUnits, PeakEquity - Equity);
            RecordCounterOutcome(candle);
        }

        private void HandleDayBoundary(DateTime candleDayUtc)
        {
            if (currentCounterDayUtc == candleDayUtc)
            {
                return;
            }

            currentCounterDayUtc = candleDayUtc;
            if (!bettingMode)
            {
                ResetCounters();
            }
        }

        private void ResetCounters()
        {
            upCount = 0;
            downCount = 0;
        }

        private void RecordCounterOutcome(Candle candle)
        {
            if (string.Equals(candle.Direction, "Up", StringComparison.OrdinalIgnoreCase))
            {
                upCount++;
            }
            else if (string.Equals(candle.Direction, "Down", StringComparison.OrdinalIgnoreCase))
            {
                downCount++;
            }
        }

        private DailyStrategyMetrics GetDailyMetrics(DateTime dayUtc)
        {
            if (!DailyMetrics.TryGetValue(dayUtc, out var metrics))
            {
                metrics = new DailyStrategyMetrics(
                    AssetSymbol,
                    Name,
                    TriggerSide,
                    TargetOutcome,
                    Threshold,
                    dayUtc);
                DailyMetrics.Add(dayUtc, metrics);
            }

            return metrics;
        }
    }

    private sealed class DailyStrategyMetrics(
        string assetSymbol,
        string strategyName,
        string triggerSide,
        string targetOutcome,
        int threshold,
        DateTime dayUtc)
    {
        public string AssetSymbol { get; } = assetSymbol;

        public string StrategyName { get; } = strategyName;

        public string TriggerSide { get; } = triggerSide;

        public string TargetOutcome { get; } = targetOutcome;

        public int Threshold { get; } = threshold;

        public DateTime DayUtc { get; } = dayUtc;

        public int Entries { get; private set; }

        public int SettledEntries => Wins + Losses;

        public int FlatEntries { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public decimal TotalStakeUnits { get; private set; }

        public decimal SettledStakeUnits { get; set; }

        public decimal PnlUnits { get; set; }

        public int CappedEntries { get; private set; }

        public decimal MaxMultiplierSeen { get; private set; }

        public void RecordEntry(decimal multiplier, bool capped)
        {
            Entries++;
            TotalStakeUnits += multiplier;
            MaxMultiplierSeen = Math.Max(MaxMultiplierSeen, multiplier);
            if (capped)
            {
                CappedEntries++;
            }
        }
    }

    private sealed record AssetSummary(
        string AssetSymbol,
        int StrategyCount,
        int OpenStrategiesAtEnd,
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
        string BestPnlStrategy,
        decimal BestPnlUnits,
        decimal BestPnlRoiPct,
        string BestRoiStrategy,
        decimal BestRoiPnlUnits,
        decimal BestRoiPct,
        string WorstPnlStrategy,
        decimal WorstPnlUnits,
        decimal WorstPnlRoiPct);

    private static void AppendCsv(this StringBuilder builder, string value, bool last = false)
    {
        builder.Append('"');
        builder.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        builder.Append('"');
        builder.Append(last ? '\n' : ',');
    }

    private static void AppendCsv(this StringBuilder builder, int value, bool last = false)
    {
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
        builder.Append(last ? '\n' : ',');
    }

    private static void AppendCsv(this StringBuilder builder, decimal value, bool last = false)
    {
        builder.Append(value.ToString("0.########", CultureInfo.InvariantCulture));
        builder.Append(last ? '\n' : ',');
    }
}
