using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.TraderDiscovery;

namespace PolyCopyTrader.Tests;

public sealed class TraderDiscoveryTests
{
    [Fact]
    public async Task Refresh_StoresBestAndWorstCandidatesWithTradeAndPositionMetrics()
    {
        var dataApi = new FakeDataApi();
        dataApi.Leaderboard.AddRange(
        [
            Entry(1, "0x1111111111111111111111111111111111111111", "Winner", 1_000m, 50_000m),
            Entry(2, "0x2222222222222222222222222222222222222222", "Middle", 100m, 10_000m),
            Entry(3, "0x3333333333333333333333333333333333333333", "Loser", -750m, 20_000m)
        ]);
        dataApi.Trades["0x1111111111111111111111111111111111111111"] =
        [
            Trade("0x1111111111111111111111111111111111111111", TradeSide.Buy, 0.40m, 100m),
            Trade("0x1111111111111111111111111111111111111111", TradeSide.Sell, 0.55m, 50m)
        ];
        dataApi.Positions["0x1111111111111111111111111111111111111111"] =
        [
            Position("0x1111111111111111111111111111111111111111", 250m, 25m, 5m)
        ];

        var repository = new TestAppRepository();
        var processor = new TraderDiscoveryProcessor(
            NullLogger<TraderDiscoveryProcessor>.Instance,
            new TraderDiscoveryOptions
            {
                LeaderboardPages = 1,
                CandidatesPerSide = 1,
                TradesPerCandidate = 10,
                PositionsPerCandidate = 10,
                RequestDelayMilliseconds = 0
            },
            dataApi,
            repository);

        var candidates = await processor.RefreshAsync();

        Assert.Equal(2, candidates.Count);
        var best = Assert.Single(repository.TraderDiscoveryCandidates, item => item.DiscoveryType == "BestPnl");
        Assert.Equal("0x1111111111111111111111111111111111111111", best.Wallet);
        Assert.Equal(2, best.TradesFetched);
        Assert.Equal(1, best.BuyTrades);
        Assert.Equal(1, best.SellTrades);
        Assert.Equal(67.5m, best.RecentTradeVolumeUsd);
        Assert.Equal(250m, best.OpenPositionValueUsd);
        Assert.Equal(25m, best.OpenPositionCashPnlUsd);

        var worst = Assert.Single(repository.TraderDiscoveryCandidates, item => item.DiscoveryType == "WorstPnl");
        Assert.Equal("0x3333333333333333333333333333333333333333", worst.Wallet);
        Assert.Equal(-750m, worst.LeaderboardPnl);
    }

    [Fact]
    public void TraderDiscovery_InvalidConfiguration_ReturnsErrors()
    {
        var configuration = new AppConfiguration
        {
            TraderDiscovery = new TraderDiscoveryOptions
            {
                Category = "BAD",
                TimePeriod = "DECADE",
                LeaderboardPages = 22
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("TraderDiscovery.Category", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TraderDiscovery.TimePeriod", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TraderDiscovery.LeaderboardPages", StringComparison.Ordinal));
    }

    private static TraderLeaderboardEntry Entry(int rank, string wallet, string name, decimal pnl, decimal volume)
    {
        return new TraderLeaderboardEntry(rank, wallet, name, volume, pnl, null, null, false);
    }

    private static LeaderTrade Trade(string wallet, TradeSide side, decimal price, decimal size)
    {
        return new LeaderTrade(
            wallet,
            "Trader",
            "condition",
            "asset",
            "slug",
            "Market",
            "Yes",
            side,
            price,
            size,
            price * size,
            DateTimeOffset.UtcNow);
    }

    private static LeaderPosition Position(string wallet, decimal currentValue, decimal cashPnl, decimal realizedPnl)
    {
        return new LeaderPosition(
            wallet,
            "condition",
            "asset",
            "Yes",
            10m,
            0.5m,
            currentValue,
            cashPnl,
            0.6m,
            DateTimeOffset.UtcNow,
            RealizedPnl: realizedPnl);
    }

    private sealed class FakeDataApi : IPolymarketDataApiClient
    {
        public List<TraderLeaderboardEntry> Leaderboard { get; } = [];

        public Dictionary<string, IReadOnlyList<LeaderTrade>> Trades { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, IReadOnlyList<LeaderPosition>> Positions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<TraderLeaderboardEntry>> GetTraderLeaderboardAsync(
            string category = "OVERALL",
            string timePeriod = "DAY",
            string orderBy = "PNL",
            int limit = 25,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TraderLeaderboardEntry>>(Leaderboard.Skip(offset).Take(limit).ToArray());
        }

        public Task<IReadOnlyList<LeaderTrade>> GetUserTradesAsync(
            string wallet,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            Trades.TryGetValue(wallet, out var trades);
            return Task.FromResult<IReadOnlyList<LeaderTrade>>((trades ?? []).Skip(offset).Take(limit).ToArray());
        }

        public Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
            string wallet,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            Positions.TryGetValue(wallet, out var positions);
            return Task.FromResult<IReadOnlyList<LeaderPosition>>((positions ?? []).Skip(offset).Take(limit).ToArray());
        }
    }
}
