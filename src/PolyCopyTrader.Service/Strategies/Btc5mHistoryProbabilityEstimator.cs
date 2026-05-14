using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.Strategies;

public static class Btc5mHistoryProbabilityEstimator
{
    public const string MethodName = "bilinear_4pt";

    public static Btc5mHistoryInterpolationGrid BuildGrid(
        decimal seconds,
        decimal cents,
        int secondsStep,
        int centsStep,
        int maxSeconds)
    {
        if (secondsStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(secondsStep), "Seconds step must be positive.");
        }

        if (centsStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(centsStep), "Cents step must be positive.");
        }

        var secondsAxis = BuildClampedAxis(seconds, secondsStep, 0, maxSeconds);
        var centsAxis = BuildOpenAxis(cents, centsStep);
        var weights = new Dictionary<Btc5mHistoryKey, decimal>();

        AddWeight(weights, new Btc5mHistoryKey(secondsAxis.Lower, centsAxis.Lower), (1m - secondsAxis.T) * (1m - centsAxis.T));
        AddWeight(weights, new Btc5mHistoryKey(secondsAxis.Upper, centsAxis.Lower), secondsAxis.T * (1m - centsAxis.T));
        AddWeight(weights, new Btc5mHistoryKey(secondsAxis.Lower, centsAxis.Upper), (1m - secondsAxis.T) * centsAxis.T);
        AddWeight(weights, new Btc5mHistoryKey(secondsAxis.Upper, centsAxis.Upper), secondsAxis.T * centsAxis.T);

        return new Btc5mHistoryInterpolationGrid(
            secondsAxis.Lower,
            secondsAxis.Upper,
            centsAxis.Lower,
            centsAxis.Upper,
            weights
                .Where(item => item.Value > 0m)
                .Select(item => new Btc5mHistoryWeightedKey(item.Key, item.Value))
                .ToArray());
    }

    public static Btc5mHistoryProbabilityEstimate Estimate(
        Btc5mHistoryInterpolationGrid grid,
        IReadOnlyCollection<Btc5mHistoryRow> rows)
    {
        var rowsByKey = rows.ToDictionary(
            row => new Btc5mHistoryKey(row.Seconds, row.Cents),
            row => row);
        var weightedCount = 0m;
        var weightedUp = 0m;
        var weightedDown = 0m;
        var rowsFound = 0;

        foreach (var weightedKey in grid.WeightedKeys)
        {
            if (!rowsByKey.TryGetValue(weightedKey.Key, out var row))
            {
                continue;
            }

            rowsFound++;
            weightedCount += weightedKey.Weight * row.Count;
            weightedUp += weightedKey.Weight * row.UpCount;
            weightedDown += weightedKey.Weight * row.DownCount;
        }

        var upProbability = weightedCount > 0m ? weightedUp / weightedCount : (decimal?)null;
        var downProbability = weightedCount > 0m ? weightedDown / weightedCount : (decimal?)null;

        return new Btc5mHistoryProbabilityEstimate(
            MethodName,
            grid.SecondsLower,
            grid.SecondsUpper,
            grid.CentsLower,
            grid.CentsUpper,
            grid.WeightedKeys.Count,
            rowsFound,
            grid.WeightedKeys.Count - rowsFound,
            weightedCount,
            weightedUp,
            weightedDown,
            upProbability,
            downProbability);
    }

    private static Axis BuildClampedAxis(decimal value, int step, int min, int max)
    {
        if (max < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Max must be greater than or equal to min.");
        }

        if (value <= min)
        {
            return new Axis(min, min, 0m);
        }

        if (value >= max)
        {
            return new Axis(max, max, 0m);
        }

        var lower = FloorToStep(value, step);
        lower = Math.Max(min, Math.Min(max, lower));
        var upper = Math.Max(min, Math.Min(max, lower + step));
        var t = upper == lower ? 0m : Clamp01((value - lower) / (upper - lower));
        return new Axis(lower, upper, t);
    }

    private static Axis BuildOpenAxis(decimal value, int step)
    {
        var lower = FloorToStep(value, step);
        var upper = lower + step;
        var t = Clamp01((value - lower) / step);
        return new Axis(lower, upper, t);
    }

    private static int FloorToStep(decimal value, int step)
    {
        return decimal.ToInt32(decimal.Floor(value / step) * step);
    }

    private static decimal Clamp01(decimal value)
    {
        if (value < 0m)
        {
            return 0m;
        }

        return value > 1m ? 1m : value;
    }

    private static void AddWeight(Dictionary<Btc5mHistoryKey, decimal> weights, Btc5mHistoryKey key, decimal weight)
    {
        if (weight <= 0m)
        {
            return;
        }

        weights[key] = weights.TryGetValue(key, out var existing)
            ? existing + weight
            : weight;
    }

    private readonly record struct Axis(int Lower, int Upper, decimal T);
}

public sealed record Btc5mHistoryInterpolationGrid(
    int SecondsLower,
    int SecondsUpper,
    int CentsLower,
    int CentsUpper,
    IReadOnlyList<Btc5mHistoryWeightedKey> WeightedKeys);

public sealed record Btc5mHistoryWeightedKey(Btc5mHistoryKey Key, decimal Weight);

public sealed record Btc5mHistoryProbabilityEstimate(
    string Method,
    int SecondsLower,
    int SecondsUpper,
    int CentsLower,
    int CentsUpper,
    int Corners,
    int RowsFound,
    int MissingCorners,
    decimal EffectiveCount,
    decimal WeightedUpCount,
    decimal WeightedDownCount,
    decimal? UpProbability,
    decimal? DownProbability);
