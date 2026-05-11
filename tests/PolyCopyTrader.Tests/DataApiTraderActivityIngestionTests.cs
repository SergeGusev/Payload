using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.DataApiTraderActivity;

namespace PolyCopyTrader.Tests;

public sealed class DataApiTraderActivityIngestionTests
{
    private const string SampleConditionId = "0xdd22472e552920b8438158ea7238bfadfa4f736aa20f55839aaaa";

    [Fact]
    public async Task Refresh_UpsertsTradersOnlyWithoutBlockingOnUserSync()
    {
        var wallet = "0x56687bf447db6ffa42ffe2204a05edaa20f55839";
        var globalTrade = Trade(wallet, "0xglobal", DateTimeOffset.UtcNow);
        var olderTrade = Trade(wallet, "0xolder", DateTimeOffset.UtcNow.AddMinutes(-1));
        var dataApi = new FakeDataApi
        {
            GlobalTrades = [globalTrade],
            UserPages =
            {
                [0] = [globalTrade, olderTrade],
                [1000] = []
            }
        };
        var repository = new TestAppRepository();
        var processor = new DataApiTraderActivityIngestionProcessor(
            NullLogger<DataApiTraderActivityIngestionProcessor>.Instance,
            new DataApiTraderIngestionOptions(),
            dataApi,
            new FakeGammaClient(),
            repository);

        var result = await processor.RefreshAsync();

        Assert.Equal(1, result.TradersUpserted);
        Assert.Equal(0, result.NewTraders);
        Assert.Equal(0, result.FullSyncs);
        Assert.Equal(0, result.UserTradesFetched);
        Assert.Equal(0, result.UserTradesAdvanced);
        Assert.Equal(0, result.GlobalTradesInserted);
        Assert.Empty(dataApi.UserOffsetsRequested);
        var trader = Assert.Single(repository.PolymarketDataApiTraders);
        Assert.False(trader.FullSyncCompleted);
        Assert.Equal(0, trader.FullSyncTradesInserted);
    }

    [Fact]
    public async Task RefreshTraderSyncBatch_NewTraderRunsFullSyncWithoutPersistingRawTrades()
    {
        var wallet = "0x56687bf447db6ffa42ffe2204a05edaa20f55839";
        var globalTrade = Trade(wallet, "0xglobal", DateTimeOffset.UtcNow);
        var olderTrade = Trade(wallet, "0xolder", DateTimeOffset.UtcNow.AddMinutes(-1));
        var dataApi = new FakeDataApi
        {
            UserPages =
            {
                [0] = [globalTrade, olderTrade],
                [1000] = []
            }
        };
        var repository = new TestAppRepository();
        repository.PolymarketDataApiTraders.Add(Trader(wallet, fullSyncCompleted: false));
        var processor = new DataApiTraderActivityIngestionProcessor(
            NullLogger<DataApiTraderActivityIngestionProcessor>.Instance,
            new DataApiTraderIngestionOptions(),
            dataApi,
            new FakeGammaClient(),
            repository);

        var result = await processor.RefreshTraderSyncBatchAsync();

        Assert.Equal(1, result.FullSyncs);
        Assert.Equal(2, result.UserTradesFetched);
        Assert.Equal(2, result.UserTradesAdvanced);
        var trader = Assert.Single(repository.PolymarketDataApiTraders);
        Assert.True(trader.FullSyncCompleted);
        Assert.Equal(2, trader.FullSyncTradesInserted);
    }

    [Fact]
    public async Task RefreshTraderSyncBatch_ExistingTraderStopsFreshSyncAtFirstKnownTrade()
    {
        var wallet = "0x56687bf447db6ffa42ffe2204a05edaa20f55839";
        var knownTrade = Trade(wallet, "0xknown", DateTimeOffset.UtcNow.AddMinutes(-2));
        var newTrade = Trade(wallet, "0xnew", DateTimeOffset.UtcNow);
        var dataApi = new FakeDataApi
        {
            GlobalTrades = [newTrade],
            UserPages =
            {
                [0] = [newTrade, knownTrade],
                [1000] = [Trade(wallet, "0xshould-not-fetch", DateTimeOffset.UtcNow.AddMinutes(-3))]
            }
        };
        var repository = new TestAppRepository();
        repository.PolymarketDataApiTraders.Add(Trader(
            wallet,
            fullSyncCompleted: true,
            lastTradeTimestampUtc: knownTrade.TimestampUtc));
        var processor = new DataApiTraderActivityIngestionProcessor(
            NullLogger<DataApiTraderActivityIngestionProcessor>.Instance,
            new DataApiTraderIngestionOptions(),
            dataApi,
            new FakeGammaClient(),
            repository);

        var result = await processor.RefreshTraderSyncBatchAsync();

        Assert.Equal(0, result.ExistingTraders);
        Assert.Equal(1, result.IncrementalSyncs);
        Assert.Equal(2, result.UserTradesFetched);
        Assert.Equal(1, result.UserTradesAdvanced);
        Assert.DoesNotContain(dataApi.UserOffsetsRequested, offset => offset == 1000);
        var trader = Assert.Single(repository.PolymarketDataApiTraders);
        Assert.Equal(1, trader.IncrementalSyncCount);
        Assert.Equal(newTrade.TimestampUtc, trader.LastTradeTimestampUtc);
    }

    [Fact]
    public async Task RefreshTraderSyncBatch_DoesNotRunLegacyPositionPerformanceRefresh()
    {
        var wallet = "0x56687bf447db6ffa42ffe2204a05edaa20f55839";
        var globalTrade = Trade(wallet, "0xglobal", DateTimeOffset.UtcNow);
        var currentPosition = Position(
            wallet,
            PolymarketDataApiPositionStatus.Open,
            "open-asset",
            size: 100m,
            avgPrice: 0.50m,
            initialValue: 50m,
            currentValue: 65m,
            cashPnl: 15m,
            realizedPnl: 2m);
        var dataApi = new FakeDataApi
        {
            GlobalTrades = [globalTrade],
            UserPages =
            {
                [0] = [globalTrade],
                [1000] = []
            },
            CurrentPositionPages =
            {
                [0] = [currentPosition]
            }
        };
        var gamma = new FakeGammaClient();
        gamma.CategoriesByConditionId[SampleConditionId] = "Sports";
        var repository = new TestAppRepository();
        repository.PolymarketDataApiTraders.Add(Trader(wallet, fullSyncCompleted: false));
        var processor = new DataApiTraderActivityIngestionProcessor(
            NullLogger<DataApiTraderActivityIngestionProcessor>.Instance,
            new DataApiTraderIngestionOptions(),
            dataApi,
            gamma,
            repository);

        var result = await processor.RefreshTraderSyncBatchAsync();

        Assert.Equal(0, result.PositionRefreshes);
        Assert.Equal(0, result.CurrentPositionsFetched);
        Assert.Equal(0, result.ClosedPositionsFetched);
        Assert.Equal(0, result.PositionsUpserted);
        Assert.Empty(repository.PolymarketDataApiPositions);
        Assert.Empty(gamma.ConditionRequests);
    }

    [Fact]
    public async Task RefreshPolymarketRatingBatch_RefreshesDueTradersFromMappedLeaderboards()
    {
        var wallet = "0x56687bf447db6ffa42ffe2204a05edaa20f55839";
        var dataApi = new FakeDataApi();
        dataApi.LeaderboardEntries[FakeDataApi.LeaderboardKey("SPORTS", wallet)] =
            new TraderLeaderboardEntry(12, wallet, "Trader", 10_000m, 750m, "profile.png", "x_name", true);
        dataApi.LeaderboardEntries[FakeDataApi.LeaderboardKey("CULTURE", wallet)] =
            new TraderLeaderboardEntry(20, wallet, "Trader", 2_000m, 150m, null, null, false);
        var currentSportsPosition = Position(
            wallet,
            PolymarketDataApiPositionStatus.Open,
            "open-asset",
            size: 100m,
            avgPrice: 0.50m,
            initialValue: 50m,
            currentValue: 65m,
            cashPnl: 15m,
            realizedPnl: 2m) with
        {
            Category = "Sports",
            PercentPnl = 34m,
            PercentRealizedPnl = 4m
        };
        var closedSportsPosition = Position(
            wallet,
            PolymarketDataApiPositionStatus.Closed,
            "closed-asset",
            size: null,
            avgPrice: 0.40m,
            initialValue: null,
            currentValue: null,
            cashPnl: null,
            realizedPnl: 12m) with
        {
            Category = "Sports",
            TotalBought = 100m,
            PercentRealizedPnl = 30m
        };
        dataApi.CurrentPositionPages[0] = [currentSportsPosition];
        dataApi.ClosedPositionPages[0] = [closedSportsPosition];
        var repository = new TestAppRepository();
        repository.PolymarketCategoryMappings.Clear();
        repository.PolymarketCategoryMappings.Add(new PolymarketCategoryMapping("Sports", "SPORTS"));
        repository.PolymarketCategoryMappings.Add(new PolymarketCategoryMapping("Culture", "CULTURE"));
        repository.PolymarketDataApiTraders.Add(Trader(wallet, fullSyncCompleted: true) with
        {
            PolymarketRatingNextRefreshAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        var processor = new DataApiTraderActivityIngestionProcessor(
            NullLogger<DataApiTraderActivityIngestionProcessor>.Instance,
            new DataApiTraderIngestionOptions(),
            dataApi,
            new FakeGammaClient(),
            repository);

        var result = await processor.RefreshPolymarketRatingBatchAsync();

        Assert.Equal(1, result.TradersSelected);
        Assert.Equal(2, result.MappingsLoaded);
        Assert.Equal(1, result.WalletRefreshes);
        Assert.Equal(0, result.WalletFailures);
        Assert.Equal(2, result.RatingRowsUpserted);
        Assert.Equal(1, result.CurrentPositionsFetched);
        Assert.Equal(1, result.ClosedPositionsFetched);
        Assert.Equal(2, repository.PolymarketDataApiWalletCategoryRatings.Count);
        var sports = Assert.Single(repository.PolymarketDataApiWalletCategoryRatings, item => item.LocalCategory == "Sports");
        Assert.True(sports.Found);
        Assert.Equal("SPORTS", sports.PolymarketCategory);
        Assert.Equal("ALL", sports.TimePeriod);
        Assert.Equal(12, sports.Rank);
        Assert.Equal(750m, sports.LeaderboardPnlUsd);
        Assert.Equal(10_000m, sports.LeaderboardVolumeUsd);
        Assert.Equal(7.5m, sports.LeaderboardPnlToVolumePct);
        Assert.Equal(1, sports.CurrentPositionsCount);
        Assert.Equal(50m, sports.CurrentPositionsInitialValueUsd);
        Assert.Equal(65m, sports.CurrentPositionsCurrentValueUsd);
        Assert.Equal(15m, sports.CurrentPositionsCashPnlUsd);
        Assert.Equal(2m, sports.CurrentPositionsRealizedPnlUsd);
        Assert.Equal(17m, sports.CurrentPositionsTotalPnlUsd);
        Assert.Equal(34m, sports.CurrentPositionsPercentPnl);
        Assert.Equal(4m, sports.CurrentPositionsPercentRealizedPnl);
        Assert.Equal(1, sports.ClosedPositionsCount);
        Assert.Equal(40m, sports.ClosedPositionsCostBasisUsd);
        Assert.Equal(12m, sports.ClosedPositionsRealizedPnlUsd);
        Assert.Equal(30m, sports.ClosedPositionsPercentRealizedPnl);
        Assert.Equal(90m, sports.PositionsTotalCostBasisUsd);
        Assert.Equal(29m, sports.PositionsTotalPnlUsd);
        Assert.Equal(29m / 90m * 100m, sports.PositionsTotalPercentPnl);
        Assert.NotNull(sports.PositionsRefreshedAtUtc);
        var culture = Assert.Single(repository.PolymarketDataApiWalletCategoryRatings, item => item.LocalCategory == "Culture");
        Assert.Equal(7.5m, culture.LeaderboardPnlToVolumePct);
        Assert.Equal(0, culture.CurrentPositionsCount);
        Assert.Equal(0, culture.ClosedPositionsCount);
        Assert.Equal(0m, culture.PositionsTotalPnlUsd);
        Assert.Contains(dataApi.LeaderboardRequests, request =>
            request.Category == "SPORTS" &&
            request.TimePeriod == "ALL" &&
            request.OrderBy == "PNL" &&
            string.Equals(request.User, wallet, StringComparison.OrdinalIgnoreCase));
        Assert.Equal([0], dataApi.CurrentPositionOffsetsRequested);
        Assert.Equal([0], dataApi.ClosedPositionOffsetsRequested);
        var trader = Assert.Single(repository.PolymarketDataApiTraders);
        Assert.NotNull(trader.PolymarketRatingRefreshedAtUtc);
        Assert.NotNull(trader.PolymarketRatingNextRefreshAtUtc);
        Assert.Null(trader.PolymarketRatingLastError);
    }

    [Fact]
    public async Task RefreshPolymarketRatingBatch_RecordsFailureAndSchedulesRetry()
    {
        var wallet = "0x56687bf447db6ffa42ffe2204a05edaa20f55839";
        var dataApi = new FakeDataApi { ThrowOnLeaderboard = true };
        var repository = new TestAppRepository();
        repository.PolymarketCategoryMappings.Clear();
        repository.PolymarketCategoryMappings.Add(new PolymarketCategoryMapping("Sports", "SPORTS"));
        repository.PolymarketDataApiTraders.Add(Trader(wallet, fullSyncCompleted: true) with
        {
            PolymarketRatingNextRefreshAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        var processor = new DataApiTraderActivityIngestionProcessor(
            NullLogger<DataApiTraderActivityIngestionProcessor>.Instance,
            new DataApiTraderIngestionOptions { PolymarketRatingFailureDelaySeconds = 60 },
            dataApi,
            new FakeGammaClient(),
            repository);

        var result = await processor.RefreshPolymarketRatingBatchAsync();

        Assert.Equal(1, result.TradersSelected);
        Assert.Equal(0, result.WalletRefreshes);
        Assert.Equal(1, result.WalletFailures);
        Assert.Empty(repository.PolymarketDataApiWalletCategoryRatings);
        var error = Assert.Single(repository.ApiErrors, item => item.Operation == "RefreshPolymarketRatings");
        Assert.Contains(wallet, error.Message, StringComparison.OrdinalIgnoreCase);
        var trader = Assert.Single(repository.PolymarketDataApiTraders);
        Assert.Equal(1, trader.PolymarketRatingRefreshAttempts);
        Assert.NotNull(trader.PolymarketRatingLastError);
        Assert.True(trader.PolymarketRatingNextRefreshAtUtc > DateTimeOffset.UtcNow);
    }

    private static PolymarketDataApiTrader Trader(
        string wallet,
        bool fullSyncCompleted,
        DateTimeOffset? lastTradeTimestampUtc = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new PolymarketDataApiTrader(
            wallet,
            "Trader",
            null,
            null,
            null,
            null,
            now,
            now,
            now,
            fullSyncCompleted ? now : null,
            null,
            lastTradeTimestampUtc,
            fullSyncCompleted,
            0,
            0,
            0,
            now);
    }

    private static PolymarketDataApiTrade Trade(
        string wallet,
        string transactionHash,
        DateTimeOffset timestampUtc)
    {
        return new PolymarketDataApiTrade(
            wallet,
            TradeSide.Buy,
            "1234567890",
            "0xdd22472e552920b8438158ea7238bfadfa4f736aa4cee91a6b86c39ead110917",
            100m,
            0.50m,
            timestampUtc,
            "Market title",
            "market-slug",
            null,
            "event-slug",
            "Yes",
            0,
            "Trader",
            null,
            null,
            null,
            null,
            transactionHash,
            "{}");
    }

    private static PolymarketDataApiPosition Position(
        string wallet,
        PolymarketDataApiPositionStatus status,
        string assetId,
        decimal? size,
        decimal avgPrice,
        decimal? initialValue,
        decimal? currentValue,
        decimal? cashPnl,
        decimal realizedPnl)
    {
        return new PolymarketDataApiPosition(
            wallet,
            status,
            assetId,
            SampleConditionId,
            size,
            avgPrice,
            initialValue,
            currentValue,
            cashPnl,
            null,
            100m,
            realizedPnl,
            null,
            status == PolymarketDataApiPositionStatus.Open ? 0.65m : 0m,
            status == PolymarketDataApiPositionStatus.Closed ? DateTimeOffset.UtcNow.AddHours(-1) : null,
            "Market title",
            "market-slug",
            null,
            "event-id",
            "event-slug",
            null,
            "Yes",
            0,
            "No",
            "opposite-asset",
            DateTimeOffset.UtcNow.AddDays(1),
            status == PolymarketDataApiPositionStatus.Open ? false : null,
            status == PolymarketDataApiPositionStatus.Open ? false : null,
            false,
            "{}");
    }

    private sealed class FakeDataApi : IPolymarketDataApiClient
    {
        public IReadOnlyList<PolymarketDataApiTrade> GlobalTrades { get; init; } = [];

        public Dictionary<int, IReadOnlyList<PolymarketDataApiTrade>> UserPages { get; } = [];

        public Dictionary<int, IReadOnlyList<PolymarketDataApiPosition>> CurrentPositionPages { get; init; } = [];

        public Dictionary<int, IReadOnlyList<PolymarketDataApiPosition>> ClosedPositionPages { get; init; } = [];

        public List<int> UserOffsetsRequested { get; } = [];

        public List<int> CurrentPositionOffsetsRequested { get; } = [];

        public List<int> ClosedPositionOffsetsRequested { get; } = [];

        public Dictionary<string, TraderLeaderboardEntry> LeaderboardEntries { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Category, string TimePeriod, string OrderBy, string? User)> LeaderboardRequests { get; } = [];

        public bool ThrowOnLeaderboard { get; init; }

        public static string LeaderboardKey(string category, string wallet)
        {
            return string.Concat(category.Trim(), "|", wallet.Trim());
        }

        public Task<IReadOnlyList<PolymarketDataApiTrade>> GetGlobalDataApiTradesAsync(
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            long? timestampCacheBuster = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GlobalTrades);
        }

        public Task<IReadOnlyList<PolymarketDataApiTrade>> GetUserDataApiTradesAsync(
            string wallet,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            long? timestampCacheBuster = null,
            CancellationToken cancellationToken = default)
        {
            UserOffsetsRequested.Add(offset);
            return Task.FromResult(UserPages.TryGetValue(offset, out var page)
                ? page
                : Array.Empty<PolymarketDataApiTrade>());
        }

        public Task<IReadOnlyList<TraderLeaderboardEntry>> GetTraderLeaderboardAsync(
            string category = "OVERALL",
            string timePeriod = "DAY",
            string orderBy = "PNL",
            int limit = 25,
            int offset = 0,
            string? user = null,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnLeaderboard)
            {
                throw new InvalidOperationException("leaderboard unavailable");
            }

            LeaderboardRequests.Add((category, timePeriod, orderBy, user));
            return Task.FromResult<IReadOnlyList<TraderLeaderboardEntry>>(
                user is not null && LeaderboardEntries.TryGetValue(LeaderboardKey(category, user), out var entry)
                    ? [entry]
                    : []);
        }

        public Task<IReadOnlyList<LeaderTrade>> GetUserTradesAsync(
            string wallet,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderTrade>>([]);
        }

        public Task<IReadOnlyList<LeaderTrade>> GetMarketTradesAsync(
            string conditionId,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderTrade>>([]);
        }

        public Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
            string wallet,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderPosition>>([]);
        }

        public Task<IReadOnlyList<PolymarketDataApiPosition>> GetUserCurrentPositionsAsync(
            string wallet,
            int limit = 500,
            int offset = 0,
            string sortBy = "CURRENT",
            string sortDirection = "DESC",
            long? timestampCacheBuster = null,
            CancellationToken cancellationToken = default)
        {
            CurrentPositionOffsetsRequested.Add(offset);
            return Task.FromResult(CurrentPositionPages.TryGetValue(offset, out var page)
                ? page
                : Array.Empty<PolymarketDataApiPosition>());
        }

        public Task<IReadOnlyList<PolymarketDataApiPosition>> GetUserClosedPositionsAsync(
            string wallet,
            int limit = 50,
            int offset = 0,
            string sortBy = "TIMESTAMP",
            string sortDirection = "DESC",
            long? timestampCacheBuster = null,
            CancellationToken cancellationToken = default)
        {
            ClosedPositionOffsetsRequested.Add(offset);
            return Task.FromResult(ClosedPositionPages.TryGetValue(offset, out var page)
                ? page
                : Array.Empty<PolymarketDataApiPosition>());
        }
    }

    private sealed class FakeGammaClient : IPolymarketGammaClient
    {
        public Dictionary<string, string?> CategoriesByConditionId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string?> EventCategories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string ConditionId, string AssetId, bool Closed)> ConditionRequests { get; } = [];

        public List<string> EventRequests { get; } = [];

        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(
            int limit = 500,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>([]);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(
            string tokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>([]);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(
            string conditionId,
            string requestedTokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            ConditionRequests.Add((conditionId, requestedTokenId, closed));
            if (!CategoriesByConditionId.TryGetValue(conditionId, out var category))
            {
                return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>([]);
            }

            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>(
                [Metadata(conditionId, requestedTokenId, category, closed)]);
        }

        public Task<string?> GetEventCategoryAsync(
            string eventId,
            CancellationToken cancellationToken = default)
        {
            EventRequests.Add(eventId);
            return Task.FromResult(EventCategories.TryGetValue(eventId, out var category) ? category : null);
        }

        private static PolymarketOnChainTokenMetadata Metadata(
            string conditionId,
            string tokenId,
            string? category,
            bool closed)
        {
            return new PolymarketOnChainTokenMetadata(
                tokenId,
                conditionId,
                "market-id",
                "market-slug",
                "Market title",
                "Yes",
                0,
                category,
                DateTimeOffset.UtcNow.AddDays(1),
                Active: !closed,
                Closed: closed,
                Archived: false,
                Resolved: closed,
                WinningOutcome: closed ? "Yes" : null,
                ClobTokenIds: [tokenId, tokenId + "-opposite"],
                Outcomes: ["Yes", "No"],
                LookupSucceeded: true,
                LookupError: null,
                RawJson: "{}",
                LastRefreshedUtc: DateTimeOffset.UtcNow);
        }
    }
}
