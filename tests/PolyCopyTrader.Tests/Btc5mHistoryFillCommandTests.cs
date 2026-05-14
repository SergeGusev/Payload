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

    [Fact]
    public async Task ExecuteAsync_ResolvesMissingResultFromGammaBeforeWriting()
    {
        var startUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var unresolvedMarket = new Btc5mHistoryMarket(
            "stale-market",
            "stale-condition",
            "btc-updown-5m-1767225600",
            startUtc,
            startUtc.AddMinutes(5),
            Result: null);
        var resolvedMarket = unresolvedMarket with
        {
            MarketId = "resolved-market",
            ConditionId = "resolved-condition",
            Result = "Down"
        };
        var database = new FakeBtc5mHistoryDatabase([unresolvedMarket]);
        var binance = new FakeBinanceAggTradeClient(
            [
                new BinanceAggTrade(1, 100.00m, startUtc.AddSeconds(-1)),
                new BinanceAggTrade(2, 99.88m, startUtc.AddSeconds(2)),
                new BinanceAggTrade(3, 99.90m, startUtc.AddSeconds(7))
            ]);
        var gamma = new FakeGammaBtc5mHistoryResolver(
            new Dictionary<string, Btc5mHistoryMarket>(StringComparer.OrdinalIgnoreCase)
            {
                [unresolvedMarket.Slug] = resolvedMarket
            });
        using var output = new StringWriter();

        var exitCode = await Btc5mHistoryFillCommand.ExecuteAsync(
            database,
            binance,
            gamma,
            CreateOptions(),
            output,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(database.Truncated);
        Assert.NotEmpty(database.SavedRows);
        Assert.Contains(database.SavedRows, row => row.State == Btc5mHistoryCacheState.Inserted && row.DownCount == 1);
        Assert.Contains("Resolved missing BTC 5m results from Gamma API: 1/1", output.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotTruncateWhenNoResultsCanBeResolved()
    {
        var startUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var database = new FakeBtc5mHistoryDatabase(
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
            new FakeBinanceAggTradeClient([]),
            new FakeGammaBtc5mHistoryResolver(new Dictionary<string, Btc5mHistoryMarket>()),
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
            ResolveMissingResultsFromGamma: true,
            GammaBaseUrl: "https://gamma-api.polymarket.com",
            GammaResolutionBatchSize: 200,
            GammaResolutionRequestDelayMilliseconds: 0,
            DryRun: false);
    }

    private sealed class FakeBtc5mHistoryDatabase(IReadOnlyList<Btc5mHistoryMarket> markets) : IBtc5mHistoryDatabase
    {
        public bool Truncated { get; private set; }

        public List<Btc5mHistoryCacheRow> SavedRows { get; } = [];

        public Task<IReadOnlyList<Btc5mHistoryMarket>> LoadClosedBtc5mMarketsAsync(
            Btc5mHistoryFillOptions options,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(markets);
        }

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

    private sealed class FakeBinanceAggTradeClient(IReadOnlyList<BinanceAggTrade> trades) : IBinanceAggTradeClient
    {
        public Task<IReadOnlyList<BinanceAggTrade>> GetAggTradesAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(trades);
        }
    }

    private sealed class FakeGammaBtc5mHistoryResolver(
        IReadOnlyDictionary<string, Btc5mHistoryMarket> marketsBySlug) : IGammaBtc5mHistoryResolver
    {
        public Task<IReadOnlyDictionary<string, Btc5mHistoryMarket>> ResolveClosedMarketsBySlugAsync(
            IReadOnlyCollection<Btc5mHistoryMarket> markets,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(marketsBySlug);
        }
    }
}
