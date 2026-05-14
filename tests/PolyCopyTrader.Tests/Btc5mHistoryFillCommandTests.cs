using PolyCopyTrader.Service.Startup;

namespace PolyCopyTrader.Tests;

public sealed class Btc5mHistoryFillCommandTests
{
    [Theory]
    [InlineData(12, 10)]
    [InlineData(10, 10)]
    [InlineData(4, 0)]
    [InlineData(0, 0)]
    [InlineData(-4, 0)]
    [InlineData(-10, -10)]
    [InlineData(-12, -10)]
    public void RoundCentsTowardZeroToFiveCents_TruncatesTowardZero(int input, int expected)
    {
        Assert.Equal(expected, Btc5mHistoryFillCommand.RoundCentsTowardZeroToFiveCents(input));
    }

    [Fact]
    public void EnumerateSampleCounters_StopsBeforeRoundedSecond300()
    {
        var counters = Btc5mHistoryFillCommand.EnumerateSampleCounters();
        var roundedSeconds = counters
            .Select(Btc5mHistoryFillCommand.RoundSecondsTowardZeroToFiveSeconds)
            .ToArray();

        Assert.Equal(60, counters.Count);
        Assert.Equal(2, counters[0]);
        Assert.Equal(297, counters[^1]);
        Assert.Equal(0, roundedSeconds[0]);
        Assert.Equal(295, roundedSeconds[^1]);
        Assert.DoesNotContain(300, roundedSeconds);
    }

    [Fact]
    public void TryGetWinningOutcome_ReturnsUniqueResolvedWinnerFromEncodedArrays()
    {
        const string rawJson = """
{
  "outcomes": "[\"Up\",\"Down\"]",
  "outcomePrices": "[\"0\",\"1\"]"
}
""";

        var winner = Btc5mHistoryFillCommand.TryGetWinningOutcome(rawJson, closed: true);

        Assert.Equal("Down", winner);
    }

    [Fact]
    public void BuildMarketCacheUpdates_UpdatesLoadedRowsAndIncrementsWinnerCounter()
    {
        var startUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var market = new Btc5mHistoryMarket(
            "market-1",
            "condition-1",
            "btc-updown-5m-1767225600",
            startUtc,
            startUtc.AddMinutes(5),
            "Up");
        var loadedRow = new Btc5mHistoryCacheRow(
            id: 10,
            seconds: 0,
            cents: 10,
            count: 2,
            upCount: 1,
            downCount: 1,
            state: Btc5mHistoryCacheState.Loaded);
        var cache = new Dictionary<Btc5mHistoryPointKey, Btc5mHistoryCacheRow>
        {
            [new Btc5mHistoryPointKey(0, 10)] = loadedRow
        };
        var trades = new[]
        {
            new BinanceAggTrade(1, 100.00m, startUtc.AddSeconds(-1)),
            new BinanceAggTrade(2, 100.12m, startUtc.AddSeconds(2)),
            new BinanceAggTrade(3, 99.88m, startUtc.AddSeconds(7))
        };

        var result = Btc5mHistoryFillCommand.BuildMarketCacheUpdates(market, trades, cache);

        Assert.Null(result.SkipReason);
        Assert.Equal(59, result.PointsInserted);
        Assert.Equal(1, result.PointsUpdated);
        Assert.Equal(0, result.PointsSkippedMissingPrice);
        Assert.Equal(60, result.ChangedRows.Count);

        Assert.Equal(Btc5mHistoryCacheState.Updated, loadedRow.State);
        Assert.Equal(3, loadedRow.Count);
        Assert.Equal(2, loadedRow.UpCount);
        Assert.Equal(1, loadedRow.DownCount);

        var insertedRow = cache[new Btc5mHistoryPointKey(5, -10)];
        Assert.Equal(Btc5mHistoryCacheState.Inserted, insertedRow.State);
        Assert.Equal(1, insertedRow.Count);
        Assert.Equal(1, insertedRow.UpCount);
        Assert.Equal(0, insertedRow.DownCount);

        Assert.True(cache.ContainsKey(new Btc5mHistoryPointKey(295, -10)));
    }
}
