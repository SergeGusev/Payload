using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ReferenceAverageHistoryCorrectionPreview;

internal sealed class CsvOutput : IDisposable
{
    private const string ClassificationHeader =
        "scope,asset,family,location,kind,trigger,catalog_threshold_bps,strategy_id,strategy_code,run_id,paper_order_id,market_id,entry_due_at_utc,settled_at_utc,run_outcome,order_outcome,action,reason,current_price_usd,minimum_average_price_usd,minimum_average_window,minimum_average_window_seconds,maximum_average_price_usd,maximum_average_window,maximum_average_window_seconds,json_threshold_bps,move_below_minimum_bps,move_above_maximum_bps,required_window,historical_stake_multiplier,assumed_fill_price,legacy_v1_outcome,corrected_v2_outcome";

    private readonly string outputDirectory;
    private readonly Dictionary<ReplayAction, OutputWriter> writers;
    private bool disposed;

    public CsvOutput(string outputDirectory)
    {
        this.outputDirectory = outputDirectory;
        writers = Enum.GetValues<ReplayAction>().ToDictionary(
            action => action,
            action => new OutputWriter(
                Path.Combine(outputDirectory, GetFileName(action) + ".partial"),
                ClassificationHeader));
    }

    public IReadOnlyDictionary<ReplayAction, long> ActionCounts =>
        writers.ToDictionary(pair => pair.Key, pair => pair.Value.RowCount);

    public void Write(
        StrategyDefinition strategy,
        SourceRow row,
        ReplayDecision decision)
    {
        var values = new[]
        {
            row.Scope,
            strategy.Asset,
            strategy.Family.ToString(),
            strategy.Location.ToString(),
            strategy.Kind,
            strategy.Trigger.ToString(),
            strategy.CatalogThresholdBps.ToString(CultureInfo.InvariantCulture),
            strategy.Id.ToString("D"),
            strategy.Code,
            row.RunId.ToString("D"),
            row.PaperOrderId?.ToString("D") ?? string.Empty,
            row.MarketId,
            row.EntryDueAtUtc.ToString("O", CultureInfo.InvariantCulture),
            row.SettledAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            row.RunOutcome ?? string.Empty,
            row.OrderOutcome ?? string.Empty,
            decision.Action.ToString(),
            decision.Reason,
            FormatDecimal(decision.CurrentPriceUsd),
            FormatDecimal(decision.MinimumAveragePriceUsd),
            decision.MinimumAverageWindow ?? string.Empty,
            decision.MinimumAverageWindowSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            FormatDecimal(decision.MaximumAveragePriceUsd),
            decision.MaximumAverageWindow ?? string.Empty,
            decision.MaximumAverageWindowSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            FormatDecimal(decision.ThresholdBps),
            FormatDecimal(decision.MoveBelowMinimumBps),
            FormatDecimal(decision.MoveAboveMaximumBps),
            decision.RequiredWindow ?? string.Empty,
            FormatDecimal(decision.HistoricalStakeMultiplier),
            FormatDecimal(decision.AssumedFillPrice),
            decision.LegacyV1Outcome?.ToString() ?? string.Empty,
            decision.CorrectedV2Outcome?.ToString() ?? string.Empty
        };
        writers[decision.Action].Write(values);
    }

    public async Task<IReadOnlyList<OutputFileEvidence>> CompleteAsync(
        IReadOnlyList<StrategyDefinition> strategies,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        foreach (var writer in writers.Values)
        {
            writer.Dispose();
        }

        var catalogPartialPath = Path.Combine(outputDirectory, "catalog.csv.partial");
        await using (var stream = new FileStream(
            catalogPartialPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.NewLine = "\n";
            await writer.WriteLineAsync("asset,family,location,kind,trigger,catalog_threshold_bps,reference_threshold_bps,strategy_id,code,name,uses_low_enter_price");
            foreach (var strategy in strategies
                         .OrderBy(item => item.Asset, StringComparer.Ordinal)
                         .ThenBy(item => item.Family)
                         .ThenBy(item => item.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(string.Join(',', new[]
                {
                    Escape(strategy.Asset),
                    Escape(strategy.Family.ToString()),
                    Escape(strategy.Location.ToString()),
                    Escape(strategy.Kind),
                    Escape(strategy.Trigger.ToString()),
                    strategy.CatalogThresholdBps.ToString(CultureInfo.InvariantCulture),
                    strategy.ReferenceThresholdBps.ToString(CultureInfo.InvariantCulture),
                    strategy.Id.ToString("D"),
                    Escape(strategy.Code),
                    Escape(strategy.Name),
                    strategy.UsesLowEnterPrice ? "true" : "false"
                }));
            }
        }

        var completed = new List<(string Path, long Count)>();
        foreach (var pair in writers.OrderBy(pair => pair.Key))
        {
            var finalPath = Path.Combine(outputDirectory, GetFileName(pair.Key));
            File.Move(pair.Value.PartialPath, finalPath);
            completed.Add((finalPath, pair.Value.RowCount));
        }

        var catalogPath = Path.Combine(outputDirectory, "catalog.csv");
        File.Move(catalogPartialPath, catalogPath);
        completed.Add((catalogPath, strategies.Count));

        var evidence = new List<OutputFileEvidence>(completed.Count);
        foreach (var item in completed.OrderBy(item => Path.GetFileName(item.Path), StringComparer.Ordinal))
        {
            await using var stream = File.OpenRead(item.Path);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            evidence.Add(new OutputFileEvidence(
                Path.GetFileName(item.Path),
                item.Count,
                Convert.ToHexString(hash)));
        }

        disposed = true;
        return evidence;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        foreach (var writer in writers.Values)
        {
            writer.Dispose();
        }

        disposed = true;
    }

    private static string GetFileName(ReplayAction action) => action switch
    {
        ReplayAction.Retain => "retain.csv",
        ReplayAction.Remove => "remove.csv",
        ReplayAction.Unreplayable => "unreplayable.csv",
        ReplayAction.InvariantError => "invariant-errors.csv",
        ReplayAction.Add => "add.csv",
        ReplayAction.StillSkip => "still-skip.csv",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    private static string FormatDecimal(decimal? value) =>
        value?.ToString("0.############################", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') &&
            !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private sealed class OutputWriter : IDisposable
    {
        private readonly StreamWriter writer;
        private bool disposed;

        public OutputWriter(string partialPath, string header)
        {
            PartialPath = partialPath;
            var stream = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                NewLine = "\n"
            };
            writer.WriteLine(header);
        }

        public string PartialPath { get; }
        public long RowCount { get; private set; }

        public void Write(IEnumerable<string> values)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            writer.WriteLine(string.Join(',', values.Select(Escape)));
            RowCount++;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            writer.Dispose();
            disposed = true;
        }
    }
}
