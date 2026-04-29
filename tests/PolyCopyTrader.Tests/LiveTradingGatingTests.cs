using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.LiveTrading;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Service.Signals;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class LiveTradingGatingTests
{
    private const string Wallet = "0x56687bf447db6ffa42ffe2204a05edaa20f55839";
    private const string Signer = "0x1111111111111111111111111111111111111111";

    [Fact]
    public async Task LiveModeWithoutExplicitEnablePersistsPreflightReject()
    {
        var repository = new TestAppRepository();
        var queue = new InMemoryLeaderTradeCandidateQueue();
        await queue.EnqueueAsync(Trade());
        var tradingClient = new CapturingTradingClient();
        var processor = CreateProcessor(
            queue,
            repository,
            tradingClient,
            new BotOptions { Mode = BotMode.Live, EnableLiveTrading = false },
            new PassGeoClient());

        var result = await processor.ProcessQueuedAsync();

        Assert.Equal(1, result.SignalsAccepted);
        Assert.Equal(0, result.LiveOrdersSubmitted);
        Assert.Equal(0, tradingClient.PlaceCalls);
        var order = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.PreflightRejected, order.Status);
        Assert.Contains("not explicitly enabled", order.ValidationSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GeoblockPreventsLivePlacement()
    {
        var repository = new TestAppRepository();
        var queue = new InMemoryLeaderTradeCandidateQueue();
        await queue.EnqueueAsync(Trade());
        var tradingClient = new CapturingTradingClient();
        var processor = CreateProcessor(
            queue,
            repository,
            tradingClient,
            LiveEnabledBot(),
            new BlockedGeoClient());

        await processor.ProcessQueuedAsync();

        Assert.Equal(0, tradingClient.PlaceCalls);
        var order = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.PreflightRejected, order.Status);
        Assert.Contains("Geoblock", order.ValidationSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiveEnabledPlacesTinyGtdPostOnlyOrder()
    {
        var repository = new TestAppRepository();
        var queue = new InMemoryLeaderTradeCandidateQueue();
        await queue.EnqueueAsync(Trade());
        var tradingClient = new CapturingTradingClient
        {
            PlacementResult = new LiveOrderPlacementResult(
                true,
                "0xorder",
                "live",
                null,
                "999962",
                "1351300",
                """{"success":true,"orderID":"0xorder","status":"live"}""",
                """{"signature":"[REDACTED]"}""")
        };
        var processor = CreateProcessor(
            queue,
            repository,
            tradingClient,
            LiveEnabledBot(),
            new PassGeoClient());

        var result = await processor.ProcessQueuedAsync();

        Assert.Equal(1, result.LiveOrdersSubmitted);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        Assert.Equal(ClobV2OrderType.GTD, tradingClient.LastRequest.OrderType);
        Assert.True(tradingClient.LastRequest.PostOnly);
        Assert.NotNull(tradingClient.LastRequest.GtdExpirationUtc);
        Assert.True(tradingClient.LastRequest.Price < 0.75m);
        var order = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.Live, order.Status);
        Assert.True(order.NotionalUsd <= 1.0m);
    }

    [Fact]
    public async Task LiveProcessorCancelsExpiredOpenOrders()
    {
        var repository = new TestAppRepository();
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Live,
            "0xorder",
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Yes",
            0.74m,
            1m,
            0.74m,
            "GTD",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(-10),
            "live",
            0m,
            1m,
            string.Empty,
            "{}",
            string.Empty,
            DateTimeOffset.UtcNow.AddMinutes(-10)));
        var tradingClient = new CapturingTradingClient();
        var processor = new LiveTradingProcessor(
            NullLogger<LiveTradingProcessor>.Instance,
            new LiveTradingOptions(),
            new RiskOptions(),
            tradingClient,
            repository,
            new ServiceControlState());

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OpenOrdersChecked);
        Assert.Equal(1, result.OrdersCanceled);
        Assert.Equal(1, tradingClient.CancelOrderCalls);
        Assert.Equal(LiveOrderStatus.Cancelled, repository.LiveOrders.Single().Status);
    }

    private static SignalProcessor CreateProcessor(
        ILeaderTradeCandidateQueue queue,
        TestAppRepository repository,
        CapturingTradingClient tradingClient,
        BotOptions botOptions,
        IPolymarketGeoClient geoClient)
    {
        var riskOptions = new RiskOptions();
        var paperOptions = new PaperTradingOptions { InitialBankrollUsd = 10_000m };
        return new SignalProcessor(
            NullLogger<SignalProcessor>.Instance,
            botOptions,
            new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = Signer,
                FunderAddress = Signer,
                SignatureType = "EOA"
            },
            paperOptions,
            new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 1m },
            Watchlist(),
            queue,
            new StaticClobClient(),
            geoClient,
            tradingClient,
            new ReadyAuthService(),
            new DefaultSignalEngine(
                new SignalOptions(),
                new ExecutionOptions(),
                riskOptions,
                paperOptions,
                new DefaultRiskEngine(riskOptions, paperOptions)),
            new DefaultPaperTradingEngine(),
            new ServiceControlState(),
            repository);
    }

    private static BotOptions LiveEnabledBot()
    {
        return new BotOptions { Mode = BotMode.Live, EnableLiveTrading = true };
    }

    private static WatchlistOptions Watchlist()
    {
        return new WatchlistOptions
        {
            Traders =
            [
                new TraderRuleOptions
                {
                    Name = "Leader",
                    Wallet = Wallet,
                    Enabled = true,
                    AllowedCategories = ["POLITICS"],
                    MinLeaderTradeUsd = 500m
                }
            ]
        };
    }

    private static LeaderTrade Trade()
    {
        return new LeaderTrade(
            Wallet,
            "Leader",
            "condition-1",
            "12345678901234567890",
            "sample-election-market",
            "Will sample event happen?",
            "Yes",
            TradeSide.Buy,
            0.74m,
            2_000m,
            1_480m,
            DateTimeOffset.UtcNow,
            "0xabc");
    }

    private sealed class StaticClobClient : IPolymarketClobPublicClient
    {
        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OrderBookSnapshot?>(new OrderBookSnapshot(
                assetId,
                [new OrderBookLevel(0.73m, 1_000m)],
                [new OrderBookLevel(0.75m, 1_000m)],
                DateTimeOffset.UtcNow,
                "condition-1",
                TickSize: 0.01m,
                MinOrderSize: 1m));
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(0.74m);
        }

        public Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(0.02m);
        }
    }

    private sealed class CapturingTradingClient : IPolymarketTradingClient
    {
        public int PlaceCalls { get; private set; }
        public int CancelOrderCalls { get; private set; }
        public ClobV2OrderRequest? LastRequest { get; private set; }
        public LiveOrderPlacementResult PlacementResult { get; init; } = new(true, "0xorder", "live", null, null, null, "{}", "{}");

        public Task<ClobV2DryRunOrderResult> PrepareDryRunOrderAsync(ClobV2OrderRequest request, CancellationToken ct)
        {
            throw new InvalidOperationException("Live tests should not create dry-run orders.");
        }

        public Task<LiveOrderPlacementResult> PlaceLiveOrderAsync(ClobV2OrderRequest request, CancellationToken ct)
        {
            PlaceCalls++;
            LastRequest = request;
            return Task.FromResult(PlacementResult);
        }

        public Task<LiveOrderCancellationResult> CancelOrderAsync(string orderId, CancellationToken ct)
        {
            CancelOrderCalls++;
            return Task.FromResult(new LiveOrderCancellationResult(true, [orderId], new Dictionary<string, string>(), "{}"));
        }

        public Task<LiveOrderCancellationResult> CancelAllOrdersAsync(CancellationToken ct)
        {
            return Task.FromResult(new LiveOrderCancellationResult(true, [], new Dictionary<string, string>(), "{}"));
        }

        public Task<LiveOrderStatusResult?> GetLiveOrderStatusAsync(string orderId, CancellationToken ct)
        {
            return Task.FromResult<LiveOrderStatusResult?>(null);
        }
    }

    private sealed class PassGeoClient : IPolymarketGeoClient
    {
        public Task<GeoblockStatus> GetGeoblockStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GeoblockStatus(false, "127.0.0.1", "US", null));
        }
    }

    private sealed class BlockedGeoClient : IPolymarketGeoClient
    {
        public Task<GeoblockStatus> GetGeoblockStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GeoblockStatus(true, "203.0.113.1", "XX", null));
        }
    }

    private sealed class ReadyAuthService : IPolymarketAuthService
    {
        public Task<AuthReadinessStatus> GetReadinessAsync(CancellationToken ct)
        {
            return Task.FromResult(AuthReadinessStatus.ConfiguredButUntested());
        }
    }
}
