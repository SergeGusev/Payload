using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class Btc5mHistoryProbabilityEstimatorTests
{
    [Fact]
    public void BuildGrid_BracketsNegativeCentsWithMathematicalFloor()
    {
        var grid = Btc5mHistoryProbabilityEstimator.BuildGrid(12m, -12m, 5, 5, 295);

        Assert.Equal(10, grid.SecondsLower);
        Assert.Equal(15, grid.SecondsUpper);
        Assert.Equal(-15, grid.CentsLower);
        Assert.Equal(-10, grid.CentsUpper);
        Assert.Equal(4, grid.WeightedKeys.Count);
        Assert.Contains(grid.WeightedKeys, item => item.Key == new Btc5mHistoryKey(10, -15));
        Assert.Contains(grid.WeightedKeys, item => item.Key == new Btc5mHistoryKey(15, -15));
        Assert.Contains(grid.WeightedKeys, item => item.Key == new Btc5mHistoryKey(10, -10));
        Assert.Contains(grid.WeightedKeys, item => item.Key == new Btc5mHistoryKey(15, -10));
    }

    [Fact]
    public void Estimate_InterpolatesWeightedCounts()
    {
        var grid = Btc5mHistoryProbabilityEstimator.BuildGrid(2.5m, 2.5m, 5, 5, 295);
        var rows = new[]
        {
            new Btc5mHistoryRow(0, 0, 100, 0, 100),
            new Btc5mHistoryRow(5, 0, 100, 25, 75),
            new Btc5mHistoryRow(0, 5, 100, 50, 50),
            new Btc5mHistoryRow(5, 5, 100, 100, 0)
        };

        var estimate = Btc5mHistoryProbabilityEstimator.Estimate(grid, rows);

        Assert.Equal(100m, estimate.EffectiveCount);
        Assert.Equal(43.75m, estimate.WeightedUpCount);
        Assert.Equal(56.25m, estimate.WeightedDownCount);
        Assert.Equal(0.4375m, estimate.UpProbability);
        Assert.Equal(0.5625m, estimate.DownProbability);
    }

    [Fact]
    public void Estimate_WeightsBySupportNotPlainCornerProbability()
    {
        var grid = Btc5mHistoryProbabilityEstimator.BuildGrid(2.5m, 2.5m, 5, 5, 295);
        var rows = new[]
        {
            new Btc5mHistoryRow(0, 0, 1, 1, 0),
            new Btc5mHistoryRow(5, 0, 999, 0, 999),
            new Btc5mHistoryRow(0, 5, 999, 0, 999),
            new Btc5mHistoryRow(5, 5, 999, 0, 999)
        };

        var estimate = Btc5mHistoryProbabilityEstimator.Estimate(grid, rows);

        Assert.Equal(749.5m, estimate.EffectiveCount);
        Assert.True(estimate.UpProbability is > 0m and < 0.001m);
        Assert.True(estimate.DownProbability is > 0.999m and < 1m);
    }
}
