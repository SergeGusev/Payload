using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class WatchlistScannerTests
{
    private const string ValidWallet = "0x56687bf447db6ffa42ffe2204a05edaa20f55839";

    [Fact]
    public void DedupKey_UsesTransactionHashAssetSideTimestamp()
    {
        var trade = SampleTrade();
        var sameFillWithDifferentAmounts = trade with
        {
            TraderWallet = ValidWallet.ToUpperInvariant(),
            TransactionHash = "0XABC",
            Price = 0.90m,
            Size = 999m
        };
        var differentSide = trade with { Side = TradeSide.Sell };
        var missingHash = trade with { TransactionHash = null };
        var missingHashDifferentSize = missingHash with { Size = 101m };

        Assert.Equal(
            LeaderTradeDeduplication.BuildKey(trade),
            LeaderTradeDeduplication.BuildKey(sameFillWithDifferentAmounts));
        Assert.NotEqual(
            LeaderTradeDeduplication.BuildKey(trade),
            LeaderTradeDeduplication.BuildKey(differentSide));
        Assert.NotEqual(
            LeaderTradeDeduplication.BuildKey(missingHash),
            LeaderTradeDeduplication.BuildKey(missingHashDifferentSize));
    }

    [Fact]
    public void WalletValidator_RejectsPlaceholders()
    {
        Assert.True(WalletAddressValidator.IsValid(ValidWallet));
        Assert.False(WalletAddressValidator.IsValid("0xPLACEHOLDER"));
        Assert.False(WalletAddressValidator.IsValid("not-a-wallet"));
    }

    [Fact]
    public async Task Scanner_StoresAndQueuesNewTradesOnce()
    {
        var api = new FakeDataApiClient
        {
            TradesToReturn = [SampleTrade()],
            PositionsToReturn = [SamplePosition()]
        };
        var repository = new FakeRepository();
        var queue = new CapturingCandidateQueue();
        var scanner = CreateScanner(api, repository, queue, ValidWallet);

        var first = await scanner.ScanOnceAsync();
        var second = await scanner.ScanOnceAsync();

        Assert.False(api.LastTakerOnly);
        Assert.Equal(100, api.LastTradeLimit);
        Assert.Equal(500, api.LastPositionLimit);
        Assert.Equal(2, api.TradesCallCount);
        Assert.Single(repository.LeaderTrades);
        Assert.Single(queue.Trades);
        Assert.Equal(2, repository.LeaderPositions.Count);
        Assert.Equal(1, first.NewTradesStored);
        Assert.Equal(0, second.NewTradesStored);
        Assert.Equal("Healthy", second.ScannerStatus);
    }

    [Fact]
    public async Task Scanner_WarnsAndSkipsInvalidWallet()
    {
        var api = new FakeDataApiClient();
        var repository = new FakeRepository();
        var queue = new CapturingCandidateQueue();
        var scanner = CreateScanner(api, repository, queue, "0xPLACEHOLDER");

        var status = await scanner.ScanOnceAsync();

        Assert.Equal(0, api.TradesCallCount);
        Assert.Equal("Warning", status.ScannerStatus);
        Assert.Contains("Invalid wallet", status.LastErrorMessage);
        Assert.Empty(repository.LeaderTrades);
        Assert.Empty(queue.Trades);
    }

    private static WatchlistScanner CreateScanner(
        IPolymarketDataApiClient api,
        IAppRepository repository,
        ILeaderTradeCandidateQueue queue,
        string wallet)
    {
        var watchlist = new WatchlistOptions
        {
            MaxTradesPerTraderPerPoll = 100,
            MaxPositionsPerTraderPerPoll = 500,
            Traders =
            [
                new TraderRuleOptions
                {
                    Name = "Gopfan",
                    Wallet = wallet,
                    AllowedCategories = ["POLITICS"],
                    Enabled = true
                }
            ]
        };

        return new WatchlistScanner(
            NullLogger<WatchlistScanner>.Instance,
            watchlist,
            api,
            repository,
            queue);
    }

    private static LeaderTrade SampleTrade()
    {
        return new LeaderTrade(
            ValidWallet,
            "Gopfan",
            "0xdd22472e552920b8438158ea7238bfadfa4f736aa4cee91a6b86c39ead110917",
            "12345678901234567890",
            "sample-market",
            "Will sample event happen?",
            "Yes",
            TradeSide.Buy,
            0.74m,
            100m,
            74m,
            DateTimeOffset.FromUnixTimeSeconds(1710000000),
            "0xabc");
    }

    private static LeaderPosition SamplePosition()
    {
        return new LeaderPosition(
            ValidWallet,
            "0xdd22472e552920b8438158ea7238bfadfa4f736aa4cee91a6b86c39ead110917",
            "12345678901234567890",
            "Yes",
            100m,
            0.74m,
            81m,
            7m,
            0.81m,
            DateTimeOffset.FromUnixTimeSeconds(1710000100),
            EndDateUtc: DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
    }

    private sealed class FakeDataApiClient : IPolymarketDataApiClient
    {
        public IReadOnlyList<LeaderTrade> TradesToReturn { get; init; } = [];

        public IReadOnlyList<LeaderPosition> PositionsToReturn { get; init; } = [];

        public int TradesCallCount { get; private set; }

        public bool LastTakerOnly { get; private set; }

        public int LastTradeLimit { get; private set; }

        public int LastPositionLimit { get; private set; }

        public Task<IReadOnlyList<TraderLeaderboardEntry>> GetTraderLeaderboardAsync(
            string category = "OVERALL",
            string timePeriod = "DAY",
            string orderBy = "PNL",
            int limit = 25,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TraderLeaderboardEntry>>([]);
        }

        public Task<IReadOnlyList<LeaderTrade>> GetUserTradesAsync(
            string wallet,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            TradesCallCount++;
            LastTakerOnly = takerOnly;
            LastTradeLimit = limit;
            return Task.FromResult(TradesToReturn);
        }

        public Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
            string wallet,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            LastPositionLimit = limit;
            return Task.FromResult(PositionsToReturn);
        }
    }

    private sealed class CapturingCandidateQueue : ILeaderTradeCandidateQueue
    {
        public List<LeaderTrade> Trades { get; } = [];

        public Task EnqueueAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
        {
            Trades.Add(trade);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LeaderTrade>> DrainAsync(int maxItems, CancellationToken cancellationToken = default)
        {
            var drained = Trades.Take(maxItems).ToArray();
            Trades.RemoveRange(0, drained.Length);
            return Task.FromResult<IReadOnlyList<LeaderTrade>>(drained);
        }
    }

    private sealed class FakeRepository : IAppRepository
    {
        private readonly HashSet<string> leaderTradeKeys = [];

        public List<LeaderTrade> LeaderTrades { get; } = [];

        public List<LeaderPosition> LeaderPositions { get; } = [];

        public List<ScannerStatusSnapshot> ScannerStatuses { get; } = [];

        public List<ApiError> ApiErrors { get; } = [];

        public async Task AddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
        {
            await TryAddLeaderTradeAsync(trade, cancellationToken);
        }

        public Task<bool> TryAddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
        {
            if (!leaderTradeKeys.Add(LeaderTradeDeduplication.BuildKey(trade)))
            {
                return Task.FromResult(false);
            }

            LeaderTrades.Add(trade);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<LeaderTrade>> GetRecentLeaderTradesAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderTrade>>(LeaderTrades);
        }

        public Task AddLeaderPositionAsync(LeaderPosition position, CancellationToken cancellationToken = default)
        {
            LeaderPositions.Add(position);
            return Task.CompletedTask;
        }

        public Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SignalSummary>> GetRecentSignalsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SignalSummary>>([]);
        }

        public Task AddSignalRejectionAsync(SignalRejection rejection, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SignalRejection>> GetRecentSignalRejectionsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SignalRejection>>([]);
        }

        public Task AddPaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdatePaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PaperOrder>>([]);
        }

        public Task<IReadOnlyList<PaperOrder>> GetRecentPaperOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PaperOrder>>([]);
        }

        public Task AddPaperFillAsync(PaperFill fill, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PaperFill>> GetRecentPaperFillsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PaperFill>>([]);
        }

        public Task UpsertPaperPositionAsync(PaperPosition position, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PaperPosition>> GetPaperPositionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PaperPosition>>([]);
        }

        public Task AddDryRunOrderAsync(DryRunOrder order, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DryRunOrder>> GetRecentDryRunOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<DryRunOrder>>([]);
        }

        public Task AddLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LiveOrder>> GetOpenLiveOrdersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LiveOrder>>([]);
        }

        public Task<IReadOnlyList<LiveOrder>> GetRecentLiveOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LiveOrder>>([]);
        }

        public Task AddLiveTradingEventAsync(LiveTradingEvent liveEvent, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LiveTradingEvent>> GetRecentLiveTradingEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LiveTradingEvent>>([]);
        }

        public Task AddApiErrorAsync(ApiError error, CancellationToken cancellationToken = default)
        {
            ApiErrors.Add(error);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ApiError>> GetRecentApiErrorsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ApiError>>(ApiErrors);
        }

        public Task<IReadOnlyList<RiskEvent>> GetRecentRiskEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RiskEvent>>([]);
        }

        public Task AddOrderBookSnapshotAsync(OrderBookSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<OrderBookSnapshot?> GetLatestOrderBookSnapshotAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OrderBookSnapshot?>(null);
        }

        public Task<IReadOnlyList<OrderBookSnapshot>> GetLatestOrderBookSnapshotsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OrderBookSnapshot>>([]);
        }

        public Task AddMarketDataEventAsync(MarketDataEvent marketDataEvent, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MarketDataEvent>> GetRecentMarketDataEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MarketDataEvent>>([]);
        }

        public Task UpsertMarketDataStatusAsync(MarketDataStatusSnapshot status, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MarketDataStatusSnapshot>> GetMarketDataStatusesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MarketDataStatusSnapshot>>([]);
        }

        public Task AddPinnedMarketAssetAsync(PinnedMarketAsset asset, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RemovePinnedMarketAssetAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PinnedMarketAsset>> GetPinnedMarketAssetsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PinnedMarketAsset>>([]);
        }

        public Task<DailyReport> BuildDailyReportAsync(DateOnly reportDate, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DailyReport(reportDate, 0, 0, 0, 0, 0, 0, 0m, 0m, string.Empty, 0, DateTimeOffset.UtcNow));
        }

        public Task UpsertDailyReportAsync(DailyReport report, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DailyReport>> GetDailyReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<DailyReport>>([]);
        }

        public Task<IReadOnlyList<TraderPerformanceReport>> GetTraderPerformanceReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TraderPerformanceReport>>([]);
        }

        public Task<IReadOnlyList<CategoryPerformanceReport>> GetCategoryPerformanceReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<CategoryPerformanceReport>>([]);
        }

        public Task<IReadOnlyList<ExecutionQualityReport>> GetExecutionQualityReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ExecutionQualityReport>>([]);
        }

        public Task<IReadOnlyList<RejectionAnalysisReport>> GetRejectionAnalysisReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RejectionAnalysisReport>>([]);
        }

        public Task AddServiceCommandAuditAsync(ServiceCommandAudit audit, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ServiceCommandAudit>> GetRecentServiceCommandAuditsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ServiceCommandAudit>>([]);
        }

        public Task UpsertScannerStatusAsync(ScannerStatusSnapshot status, CancellationToken cancellationToken = default)
        {
            ScannerStatuses.Add(status);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ScannerStatusSnapshot>> GetScannerStatusesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ScannerStatusSnapshot>>(ScannerStatuses);
        }

        public Task UpsertServiceHeartbeatAsync(ServiceHeartbeat heartbeat, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ServiceHeartbeat>> GetServiceHeartbeatsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ServiceHeartbeat>>([]);
        }
    }
}
