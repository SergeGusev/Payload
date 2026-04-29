using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Service.Signals;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class ResilienceTests
{
    private const string Wallet = "0x56687bf447db6ffa42ffe2204a05edaa20f55839";

    [Fact]
    public async Task WatchlistScanner_SurvivesApiFailureAndRecordsError()
    {
        var repository = new TestAppRepository();
        var scanner = CreateScanner(new ThrowingDataApiClient("HTTP 429"), repository);

        var status = await scanner.ScanOnceAsync();

        Assert.Equal("Warning", status.ScannerStatus);
        Assert.Single(repository.ApiErrors);
    }

    [Fact]
    public async Task WatchlistScanner_SurvivesApiFailureWhenErrorPersistenceFails()
    {
        var repository = new TestAppRepository { ThrowOnAddApiError = true };
        var scanner = CreateScanner(new ThrowingDataApiClient("HTTP 500"), repository);

        var status = await scanner.ScanOnceAsync();

        Assert.Equal("Warning", status.ScannerStatus);
    }

    [Fact]
    public async Task WatchlistScanner_SurvivesTemporaryDatabaseFailureWhileStoringTrade()
    {
        var repository = new TestAppRepository
        {
            ThrowOnTryAddLeaderTrade = true,
            ThrowOnAddApiError = true
        };
        var scanner = CreateScanner(new StaticDataApiClient([Trade()], []), repository);

        var status = await scanner.ScanOnceAsync();

        Assert.Equal("Warning", status.ScannerStatus);
        Assert.Empty(repository.LeaderTrades);
    }

    [Fact]
    public async Task SignalProcessor_RejectsMissingOrderBook()
    {
        var repository = new TestAppRepository();
        var queue = new InMemoryLeaderTradeCandidateQueue();
        await queue.EnqueueAsync(Trade());
        var processor = CreateSignalProcessor(queue, new NullClobClient(), repository);

        var result = await processor.ProcessQueuedAsync();

        Assert.Equal(1, result.SignalsRejected);
        Assert.Single(repository.Signals);
        Assert.Contains(repository.SignalRejections, rejection => rejection.ReasonCode == SignalReasonCodes.MissingOrderBook);
    }

    [Fact]
    public async Task SignalProcessor_SurvivesClobFailureWhenErrorPersistenceFails()
    {
        var repository = new TestAppRepository { ThrowOnAddApiError = true };
        var queue = new InMemoryLeaderTradeCandidateQueue();
        await queue.EnqueueAsync(Trade());
        var processor = CreateSignalProcessor(queue, new ThrowingClobClient(), repository);

        var result = await processor.ProcessQueuedAsync();

        Assert.Equal(1, result.CandidatesProcessed);
        Assert.Equal(1, result.SignalsRejected);
    }

    [Fact]
    public async Task PaperTradingProcessor_SurvivesOrderBookFailureWhenErrorPersistenceFails()
    {
        var repository = new TestAppRepository { ThrowOnAddApiError = true };
        await repository.AddPaperOrderAsync(new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Yes",
            0.74m,
            10m,
            7.40m,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(5)));
        var processor = new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            new ThrowingClobClient(),
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new MarketDataWebSocketOptions(),
            repository);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OpenOrdersChecked);
        Assert.Equal(0, result.OrdersFilled);
    }

    [Fact]
    public void MarketDataCache_RejectsStaleSnapshotsAfterDisconnectWindow()
    {
        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        cache.ApplyUpdate(new MarketDataUpdate(
            MarketDataEventType.Book,
            "book",
            "asset-1",
            "condition-1",
            new OrderBookSnapshot(
                "asset-1",
                [new OrderBookLevel(0.50m, 100m)],
                [new OrderBookLevel(0.52m, 100m)],
                DateTimeOffset.UtcNow.AddMinutes(-5),
                "condition-1"),
            0.50m,
            0.52m,
            null,
            null,
            TradeSide.Unknown,
            false,
            DateTimeOffset.UtcNow.AddMinutes(-5)));

        var fresh = cache.TryGetFreshOrderBook("asset-1", TimeSpan.FromSeconds(30), out _);

        Assert.False(fresh);
    }

    private static WatchlistScanner CreateScanner(
        IPolymarketDataApiClient dataApiClient,
        TestAppRepository repository)
    {
        return new WatchlistScanner(
            NullLogger<WatchlistScanner>.Instance,
            Watchlist(),
            dataApiClient,
            repository,
            new InMemoryLeaderTradeCandidateQueue());
    }

    private static SignalProcessor CreateSignalProcessor(
        ILeaderTradeCandidateQueue queue,
        IPolymarketClobPublicClient clobClient,
        TestAppRepository repository)
    {
        var riskOptions = new RiskOptions();
        var paperOptions = new PaperTradingOptions { InitialBankrollUsd = 10_000m };
        return new SignalProcessor(
            NullLogger<SignalProcessor>.Instance,
            new BotOptions { Mode = BotMode.Paper },
            paperOptions,
            Watchlist(),
            queue,
            clobClient,
            new DefaultSignalEngine(
                new SignalOptions(),
                new ExecutionOptions(),
                riskOptions,
                paperOptions,
                new DefaultRiskEngine(riskOptions, paperOptions)),
            new DefaultPaperTradingEngine(),
            repository);
    }

    private static WatchlistOptions Watchlist()
    {
        return new WatchlistOptions
        {
            Traders =
            [
                new TraderRuleOptions
                {
                    Name = "Gopfan",
                    Wallet = Wallet,
                    AllowedCategories = ["POLITICS"],
                    Enabled = true
                }
            ]
        };
    }

    private static LeaderTrade Trade()
    {
        return new LeaderTrade(
            Wallet,
            "Gopfan",
            "condition-1",
            "asset-1",
            "sample-market",
            "Will sample event happen?",
            "Yes",
            TradeSide.Buy,
            0.74m,
            2_000m,
            1_480m,
            DateTimeOffset.UtcNow,
            "0xabc");
    }

    private sealed class StaticDataApiClient(
        IReadOnlyList<LeaderTrade> trades,
        IReadOnlyList<LeaderPosition> positions) : IPolymarketDataApiClient
    {
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
            return Task.FromResult(trades);
        }

        public Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
            string wallet,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(positions);
        }
    }

    private sealed class ThrowingDataApiClient(string message) : IPolymarketDataApiClient
    {
        public Task<IReadOnlyList<TraderLeaderboardEntry>> GetTraderLeaderboardAsync(
            string category = "OVERALL",
            string timePeriod = "DAY",
            string orderBy = "PNL",
            int limit = 25,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(message);
        }

        public Task<IReadOnlyList<LeaderTrade>> GetUserTradesAsync(
            string wallet,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(message);
        }

        public Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
            string wallet,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class NullClobClient : IPolymarketClobPublicClient
    {
        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OrderBookSnapshot?>(null);
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }
    }

    private sealed class ThrowingClobClient : IPolymarketClobPublicClient
    {
        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("order book unavailable");
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("server time unavailable");
        }

        public Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("midpoint unavailable");
        }

        public Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("spread unavailable");
        }
    }
}
