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
    public void EnumerateMarketStartTimesUtc_FloorsStartAndBuildsFiveMinuteSlugs()
    {
        var starts = Btc5mHistoryFillCommand.EnumerateMarketStartTimesUtc(
            new DateTimeOffset(2025, 12, 18, 4, 26, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 12, 18, 4, 35, 0, TimeSpan.Zero));

        Assert.Equal(3, starts.Count);
        Assert.Equal(new DateTimeOffset(2025, 12, 18, 4, 25, 0, TimeSpan.Zero), starts[0]);
        Assert.Equal(new DateTimeOffset(2025, 12, 18, 4, 35, 0, TimeSpan.Zero), starts[^1]);
        Assert.Equal("btc-updown-5m-1766031900", Btc5mHistoryFillCommand.ToBtc5mSlug(starts[0]));
        Assert.Equal("btc-updown-5m-1766032500", Btc5mHistoryFillCommand.ToBtc5mSlug(starts[^1]));
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
            new BinanceBtcPricePoint(1, 100.00m, startUtc.AddSeconds(-1)),
            new BinanceBtcPricePoint(2, 100.12m, startUtc.AddSeconds(2)),
            new BinanceBtcPricePoint(3, 99.88m, startUtc.AddSeconds(7))
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

    [Fact]
    public async Task ExecuteAsync_LoadsResolvedMarketsFromPolymarketApiSourceBeforeWriting()
    {
        var startUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var market = new Btc5mHistoryMarket(
            "gamma-market",
            "gamma-condition",
            "btc-updown-5m-1767225600",
            startUtc,
            startUtc.AddMinutes(5),
            "Down");
        var database = new FakeBtc5mHistoryDatabase();
        var binance = new FakeBinanceOneSecondKlineClient(
            [
                new BinanceBtcPricePoint(1, 100.00m, startUtc.AddSeconds(-1)),
                new BinanceBtcPricePoint(2, 99.88m, startUtc.AddSeconds(2)),
                new BinanceBtcPricePoint(3, 99.90m, startUtc.AddSeconds(7))
            ]);
        var marketSource = new FakeBtc5mHistoryMarketSource([market]);
        using var output = new StringWriter();

        var exitCode = await Btc5mHistoryFillCommand.ExecuteAsync(
            database,
            binance,
            marketSource,
            CreateOptions(),
            output,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(database.Truncated);
        Assert.NotEmpty(database.SavedRows);
        Assert.Contains(database.SavedRows, row => row.State == Btc5mHistoryCacheState.Inserted && row.DownCount == 1);
        Assert.Contains("Loading resolved BTC Up or Down 5m markets from Polymarket Gamma API", output.ToString());
        Assert.Contains("Markets loaded: 1", output.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotTruncateWhenApiSourceHasNoKnownResults()
    {
        var startUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var database = new FakeBtc5mHistoryDatabase();
        var marketSource = new FakeBtc5mHistoryMarketSource(
            [
                new Btc5mHistoryMarket(
                    "stale-market",
                    "stale-condition",
                    "btc-updown-5m-1767225600",
                    startUtc,
                    startUtc.AddMinutes(5),
                    Result: null)
            ]);
        using var output = new StringWriter();

        var exitCode = await Btc5mHistoryFillCommand.ExecuteAsync(
            database,
            new FakeBinanceOneSecondKlineClient([]),
            marketSource,
            CreateOptions(),
            output,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(database.Truncated);
        Assert.Empty(database.SavedRows);
        Assert.Contains("btc_5m_history was not truncated", output.ToString());
    }

    private static Btc5mHistoryFillOptions CreateOptions()
    {
        return new Btc5mHistoryFillOptions(
            StartUtc: null,
            EndUtc: null,
            MaxMarkets: null,
            BinanceBaseUrl: "https://api.binance.com",
            HttpTimeoutSeconds: 30,
            BinanceRequestDelayMilliseconds: 0,
            ProgressEveryMarkets: 0,
            GammaBaseUrl: "https://gamma-api.polymarket.com",
            GammaMarketBatchSize: 200,
            GammaMarketRequestDelayMilliseconds: 0,
            DryRun: false);
    }

    private sealed class FakeBtc5mHistoryDatabase : IBtc5mHistoryDatabase
    {
        public bool Truncated { get; private set; }

        public List<Btc5mHistoryCacheRow> SavedRows { get; } = [];

        public Task TruncateHistoryAsync(CancellationToken cancellationToken)
        {
            Truncated = true;
            return Task.CompletedTask;
        }

        public Task<Dictionary<Btc5mHistoryPointKey, Btc5mHistoryCacheRow>> LoadHistoryCacheAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new Dictionary<Btc5mHistoryPointKey, Btc5mHistoryCacheRow>());
        }

        public Task SaveHistoryCacheChangesAsync(
            IReadOnlyCollection<Btc5mHistoryCacheRow> rows,
            CancellationToken cancellationToken)
        {
            SavedRows.AddRange(rows);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBinanceOneSecondKlineClient(IReadOnlyList<BinanceBtcPricePoint> trades) : IBinanceBtcPriceClient
    {
        public Task<IReadOnlyList<BinanceBtcPricePoint>> GetPricePointsAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(trades);
        }
    }

    private sealed class FakeBtc5mHistoryMarketSource(IReadOnlyList<Btc5mHistoryMarket> markets) : IBtc5mHistoryMarketSource
    {
        public Task<IReadOnlyList<Btc5mHistoryMarket>> LoadResolvedMarketsAsync(
            Btc5mHistoryFillOptions options,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(markets);
        }
    }
}
