using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Service.Signals;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class PipelineIntegrationTests
{
    private const string Wallet = "0x56687bf447db6ffa42ffe2204a05edaa20f55839";

    [Fact]
    public async Task WatchlistSignalPaperPipeline_CreatesFillAndPosition()
    {
        var repository = new TestAppRepository();
        var queue = new InMemoryLeaderTradeCandidateQueue();
        var watchlistOptions = Watchlist();
        var scanner = new WatchlistScanner(
            NullLogger<WatchlistScanner>.Instance,
            watchlistOptions,
            new FakeDataApiClient([Trade()], [Position()]),
            repository,
            queue);

        var scannerStatus = await scanner.ScanOnceAsync();

        Assert.Equal("Healthy", scannerStatus.ScannerStatus);
        Assert.Single(repository.LeaderTrades);
        Assert.Single(repository.LeaderPositions);

        var signalProcessor = new SignalProcessor(
            NullLogger<SignalProcessor>.Instance,
            new BotOptions { Mode = BotMode.Paper },
            new PolymarketAuthOptions(),
            new PaperTradingOptions { InitialBankrollUsd = 10_000m, DefaultOrderTtlSeconds = 300 },
            new LiveTradingOptions(),
            watchlistOptions,
            queue,
            new FakeClobClient(OrderBook(bestBid: 0.73m, bestAsk: 0.75m)),
            new FakeGeoClient(),
            new FakeTradingClient(),
            new FakeAuthService(),
            SignalEngine(),
            new DefaultPaperTradingEngine(),
            new ServiceControlState(),
            repository);

        var signalResult = await signalProcessor.ProcessQueuedAsync();

        Assert.Equal(1, signalResult.SignalsAccepted);
        Assert.Equal(1, signalResult.PaperOrdersCreated);
        Assert.Single(repository.Signals);
        Assert.Single(repository.PaperOrders);

        var paperProcessor = new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            new FakeClobClient(OrderBook(bestBid: 0.73m, bestAsk: 0.74m)),
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new MarketDataWebSocketOptions(),
            repository);

        var paperResult = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(1, paperResult.OrdersFilled);
        Assert.Single(repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Filled, repository.PaperOrders.Single().Status);
        var position = Assert.Single(repository.PaperPositions);
        Assert.Equal("asset-1", position.AssetId);
        Assert.True(position.EstimatedValueUsd > 0m);
    }

    private static ISignalEngine SignalEngine()
    {
        var riskOptions = new RiskOptions();
        var paperOptions = new PaperTradingOptions { InitialBankrollUsd = 10_000m };
        return new DefaultSignalEngine(
            new SignalOptions(),
            new ExecutionOptions(),
            riskOptions,
            paperOptions,
            new DefaultRiskEngine(riskOptions, paperOptions));
    }

    private static WatchlistOptions Watchlist()
    {
        return new WatchlistOptions
        {
            MaxTradesPerTraderPerPoll = 100,
            MaxPositionsPerTraderPerPoll = 100,
            Traders =
            [
                new TraderRuleOptions
                {
                    Name = "Gopfan",
                    Wallet = Wallet,
                    AllowedCategories = ["POLITICS"],
                    Enabled = true,
                    MaxLagSeconds = 300,
                    MaxSlippageCents = 1m,
                    MaxSpreadCents = 2m,
                    MaxSpreadPct = 3m,
                    MinLeaderTradeUsd = 500m
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

    private static LeaderPosition Position()
    {
        return new LeaderPosition(
            Wallet,
            "condition-1",
            "asset-1",
            "Yes",
            100m,
            0.74m,
            81m,
            7m,
            0.81m,
            DateTimeOffset.UtcNow);
    }

    private static OrderBookSnapshot OrderBook(decimal bestBid, decimal bestAsk)
    {
        return new OrderBookSnapshot(
            "asset-1",
            [new OrderBookLevel(bestBid, 1_000m)],
            [new OrderBookLevel(bestAsk, 1_000m)],
            DateTimeOffset.UtcNow,
            "condition-1",
            TickSize: 0.01m);
    }

    private sealed class FakeDataApiClient(
        IReadOnlyList<LeaderTrade> trades,
        IReadOnlyList<LeaderPosition> positions) : IPolymarketDataApiClient
    {
        public Task<IReadOnlyList<TraderLeaderboardEntry>> GetTraderLeaderboardAsync(
            string category = "OVERALL",
            string timePeriod = "DAY",
            string orderBy = "PNL",
            int limit = 25,
            int offset = 0,
            string? user = null,
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

    private sealed class FakeClobClient(OrderBookSnapshot? orderBook) : IPolymarketClobPublicClient
    {
        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(orderBook);
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

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(string tokenId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PolymarketClobMarketByToken?>(null);
        }
    }

    private sealed class FakeTradingClient : IPolymarketTradingClient
    {
        public Task<ClobV2DryRunOrderResult> PrepareDryRunOrderAsync(ClobV2OrderRequest request, CancellationToken ct)
        {
            throw new InvalidOperationException("Paper-mode integration test should not create dry-run orders.");
        }

        public Task<LiveOrderPlacementResult> PlaceLiveOrderAsync(ClobV2OrderRequest request, CancellationToken ct)
        {
            throw new InvalidOperationException("Paper-mode integration test should not create live orders.");
        }

        public Task<LiveOrderCancellationResult> CancelOrderAsync(string orderId, CancellationToken ct)
        {
            throw new InvalidOperationException("Paper-mode integration test should not cancel live orders.");
        }

        public Task<LiveOrderCancellationResult> CancelAllOrdersAsync(CancellationToken ct)
        {
            throw new InvalidOperationException("Paper-mode integration test should not cancel live orders.");
        }

        public Task<LiveOrderStatusResult?> GetLiveOrderStatusAsync(string orderId, CancellationToken ct)
        {
            throw new InvalidOperationException("Paper-mode integration test should not poll live orders.");
        }
    }

    private sealed class FakeGeoClient : IPolymarketGeoClient
    {
        public Task<GeoblockStatus> GetGeoblockStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GeoblockStatus(false, "127.0.0.1", "US", null));
        }
    }

    private sealed class FakeAuthService : IPolymarketAuthService
    {
        public Task<AuthReadinessStatus> GetReadinessAsync(CancellationToken ct)
        {
            return Task.FromResult(AuthReadinessStatus.NotConfigured());
        }
    }
}
