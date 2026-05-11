using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

var repository = new PostgresAppRepository(new PostgresConnectionFactory(new StorageOptions()));
var samples = (await repository.GetRecentBtcUsdReferenceCorrelationSamplesAsync(100_000))
    .OrderBy(sample => sample.CreatedAtUtc)
    .ToArray();

Console.WriteLine("total_rows=" + samples.Length);
PrintStats("all_rows", samples);

var latestContinuous = LatestContinuousSegment(samples, TimeSpan.FromSeconds(60));
PrintStats("latest_continuous_segment_gap_le_60s", latestContinuous);

Console.WriteLine("recent_rows=5");
foreach (var sample in samples.Reverse().Take(5))
{
    Console.WriteLine(string.Join(
        " ",
        "created_utc=" + sample.CreatedAtUtc.ToString("O"),
        "binance=" + sample.BinancePriceUsd,
        "chainlink=" + sample.ChainlinkPriceUsd,
        "diff_usd=" + sample.PriceDiffUsd,
        "diff_bps=" + sample.PriceDiffBps,
        "time_delta_seconds=" + sample.TimeDeltaSeconds));
}

static void PrintStats(string label, IReadOnlyList<PolyCopyTrader.Domain.BtcUsdReferenceCorrelationSample> samples)
{
    Console.WriteLine("section=" + label);
    Console.WriteLine("count=" + samples.Count);
    if (samples.Count == 0)
    {
        return;
    }

    var ordered = samples.OrderBy(sample => sample.CreatedAtUtc).ToArray();
    var intervals = ordered
        .Skip(1)
        .Select((sample, index) => (sample.CreatedAtUtc - ordered[index].CreatedAtUtc).TotalSeconds)
        .ToArray();
    var binance = ordered.Select(sample => (double)sample.BinancePriceUsd).ToArray();
    var chainlink = ordered.Select(sample => (double)sample.ChainlinkPriceUsd).ToArray();
    var diffUsd = ordered.Select(sample => (double)sample.PriceDiffUsd).ToArray();
    var absDiffUsd = diffUsd.Select(Math.Abs).ToArray();
    var diffBps = ordered.Select(sample => (double)sample.PriceDiffBps).ToArray();
    var absDiffBps = diffBps.Select(Math.Abs).ToArray();
    var timeDelta = ordered.Select(sample => (double)sample.TimeDeltaSeconds).ToArray();
    var binanceDelta = Differences(binance);
    var chainlinkDelta = Differences(chainlink);

    Console.WriteLine("from_utc=" + ordered[0].CreatedAtUtc.ToString("O"));
    Console.WriteLine("to_utc=" + ordered[^1].CreatedAtUtc.ToString("O"));
    Console.WriteLine("span_minutes=" + Format((ordered[^1].CreatedAtUtc - ordered[0].CreatedAtUtc).TotalMinutes));
    Console.WriteLine("avg_interval_seconds=" + Format(Mean(intervals)));
    Console.WriteLine("max_interval_seconds=" + Format(intervals.Length == 0 ? double.NaN : intervals.Max()));
    Console.WriteLine("price_corr=" + Format(Pearson(binance, chainlink)));
    Console.WriteLine("delta_corr=" + Format(Pearson(binanceDelta, chainlinkDelta)));
    Console.WriteLine("diff_usd_avg=" + Format(Mean(diffUsd)));
    Console.WriteLine("diff_usd_median=" + Format(Percentile(diffUsd, 0.50)));
    Console.WriteLine("diff_usd_std=" + Format(StdDev(diffUsd)));
    Console.WriteLine("diff_usd_p05=" + Format(Percentile(diffUsd, 0.05)));
    Console.WriteLine("diff_usd_p95=" + Format(Percentile(diffUsd, 0.95)));
    Console.WriteLine("diff_usd_min=" + Format(diffUsd.Min()));
    Console.WriteLine("diff_usd_max=" + Format(diffUsd.Max()));
    Console.WriteLine("abs_diff_usd_avg=" + Format(Mean(absDiffUsd)));
    Console.WriteLine("abs_diff_usd_median=" + Format(Percentile(absDiffUsd, 0.50)));
    Console.WriteLine("abs_diff_usd_p95=" + Format(Percentile(absDiffUsd, 0.95)));
    Console.WriteLine("abs_diff_usd_max=" + Format(absDiffUsd.Max()));
    Console.WriteLine("diff_bps_avg=" + Format(Mean(diffBps)));
    Console.WriteLine("diff_bps_median=" + Format(Percentile(diffBps, 0.50)));
    Console.WriteLine("diff_bps_std=" + Format(StdDev(diffBps)));
    Console.WriteLine("diff_bps_p05=" + Format(Percentile(diffBps, 0.05)));
    Console.WriteLine("diff_bps_p95=" + Format(Percentile(diffBps, 0.95)));
    Console.WriteLine("abs_diff_bps_avg=" + Format(Mean(absDiffBps)));
    Console.WriteLine("abs_diff_bps_p95=" + Format(Percentile(absDiffBps, 0.95)));
    Console.WriteLine("time_delta_seconds_avg=" + Format(Mean(timeDelta)));
    Console.WriteLine("time_delta_seconds_median=" + Format(Percentile(timeDelta, 0.50)));
    Console.WriteLine("time_delta_seconds_min=" + Format(timeDelta.Min()));
    Console.WriteLine("time_delta_seconds_max=" + Format(timeDelta.Max()));
}

static IReadOnlyList<PolyCopyTrader.Domain.BtcUsdReferenceCorrelationSample> LatestContinuousSegment(
    IReadOnlyList<PolyCopyTrader.Domain.BtcUsdReferenceCorrelationSample> samples,
    TimeSpan maxGap)
{
    if (samples.Count == 0)
    {
        return [];
    }

    var start = 0;
    for (var i = 1; i < samples.Count; i++)
    {
        if (samples[i].CreatedAtUtc - samples[i - 1].CreatedAtUtc > maxGap)
        {
            start = i;
        }
    }

    return samples.Skip(start).ToArray();
}

static double[] Differences(IReadOnlyList<double> values)
{
    if (values.Count < 2)
    {
        return [];
    }

    var result = new double[values.Count - 1];
    for (var i = 1; i < values.Count; i++)
    {
        result[i - 1] = values[i] - values[i - 1];
    }

    return result;
}

static double Mean(IReadOnlyList<double> values)
{
    return values.Count == 0 ? double.NaN : values.Average();
}

static double StdDev(IReadOnlyList<double> values)
{
    if (values.Count < 2)
    {
        return double.NaN;
    }

    var mean = values.Average();
    var variance = values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1);
    return Math.Sqrt(variance);
}

static double Percentile(IReadOnlyList<double> values, double percentile)
{
    if (values.Count == 0)
    {
        return double.NaN;
    }

    var ordered = values.Order().ToArray();
    var index = (ordered.Length - 1) * percentile;
    var lower = (int)Math.Floor(index);
    var upper = (int)Math.Ceiling(index);
    if (lower == upper)
    {
        return ordered[lower];
    }

    return ordered[lower] + (ordered[upper] - ordered[lower]) * (index - lower);
}

static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
{
    if (x.Count != y.Count || x.Count < 2)
    {
        return double.NaN;
    }

    var meanX = x.Average();
    var meanY = y.Average();
    var sumXY = 0d;
    var sumXX = 0d;
    var sumYY = 0d;
    for (var i = 0; i < x.Count; i++)
    {
        var dx = x[i] - meanX;
        var dy = y[i] - meanY;
        sumXY += dx * dy;
        sumXX += dx * dx;
        sumYY += dy * dy;
    }

    return sumXX == 0d || sumYY == 0d ? double.NaN : sumXY / Math.Sqrt(sumXX * sumYY);
}

static string Format(double value)
{
    return double.IsNaN(value) ? "n/a" : value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
}
