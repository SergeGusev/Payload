using System.Text.Json;
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
using PolyCopyTrader.Service.Strategies;
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
        repository.PolymarketOnChainTokenMetadata.Add(TokenMetadata());
        repository.PolymarketOnChainWalletCategoryPerformance.Add(CategoryPerformance());

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
            new ExposureSnapshotCache(repository),
            new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance, repository),
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
            new PaperTradingOptions(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var paperResult = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(1, paperResult.OrdersFilled);
        Assert.Single(repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Filled, repository.PaperOrders.Single().Status);
        var position = Assert.Single(repository.PaperPositions);
        Assert.Equal("asset-1", position.AssetId);
        Assert.True(position.EstimatedValueUsd > 0m);
    }

    [Fact]
    public async Task PaperTradingProcessor_BalancedBuyFillTracksPartialDepthAndRemainingShares()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Wallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Yes",
            0.50m,
            10m,
            5m,
            now,
            now.AddMinutes(5));
        await repository.AddPaperOrderAsync(order);

        var firstProcessor = CreatePaperProcessor(
            repository,
            OrderBook(
                "asset-1",
                [new OrderBookLevel(0.49m, 100m)],
                [new OrderBookLevel(0.49m, 4m), new OrderBookLevel(0.51m, 100m)]));

        var firstResult = await firstProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(1, firstResult.OrdersFilled);
        Assert.Equal(PaperOrderStatus.PartiallyFilled, repository.PaperOrders.Single().Status);
        Assert.Equal(4m, Assert.Single(repository.PaperFills).SizeShares);
        Assert.Equal(4m, Assert.Single(repository.PaperPositions).SizeShares);

        var secondProcessor = CreatePaperProcessor(
            repository,
            OrderBook(
                "asset-1",
                [new OrderBookLevel(0.49m, 100m)],
                [new OrderBookLevel(0.50m, 100m)]));

        var secondResult = await secondProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(1, secondResult.OrdersFilled);
        Assert.Equal(PaperOrderStatus.Filled, repository.PaperOrders.Single().Status);
        Assert.Equal(2, repository.PaperFills.Count);
        Assert.Equal(6m, repository.PaperFills.OrderBy(fill => fill.FilledAtUtc).Last().SizeShares);
        var position = Assert.Single(repository.PaperPositions);
        Assert.Equal(10m, position.SizeShares);
        Assert.Equal(0.50m, position.AveragePrice);
    }

    [Fact]
    public async Task PaperTradingProcessor_SellFillClosesCopiedWalletPosition()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Wallet,
            PaperOrderStatus.Pending,
            TradeSide.Sell,
            "asset-1",
            "condition-1",
            "Yes",
            0.74m,
            25m,
            18.50m,
            now,
            now.AddMinutes(5));
        await repository.AddPaperOrderAsync(order);
        await repository.UpsertPaperPositionAsync(new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            100m,
            0.60m,
            73m,
            13m,
            now,
            Wallet));

        var paperProcessor = new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            new FakeClobClient(OrderBook(bestBid: 0.74m, bestAsk: 0.75m)),
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new MarketDataWebSocketOptions(),
            new PaperTradingOptions(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var paperResult = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(1, paperResult.OrdersFilled);
        Assert.Equal(PaperOrderStatus.Filled, repository.PaperOrders.Single().Status);
        var fill = Assert.Single(repository.PaperFills);
        Assert.Equal(3.50m, fill.RealizedPnlUsd);
        var position = Assert.Single(repository.PaperPositions);
        Assert.Equal(75m, position.SizeShares);
        Assert.Equal(Wallet, position.CopiedTraderWallet);
    }

    [Fact]
    public async Task PaperTradingProcessor_BatchesFillSimulationButExpiresAllDueOrders()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        await repository.AddPaperOrderAsync(PaperOrder("expired-1", now.AddSeconds(-1)));
        await repository.AddPaperOrderAsync(PaperOrder("expired-2", now.AddSeconds(-1)));
        await repository.AddPaperOrderAsync(PaperOrder("active-1", now.AddMinutes(5)));
        await repository.AddPaperOrderAsync(PaperOrder("active-2", now.AddMinutes(5)));

        var paperProcessor = new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            new FakeClobClient(OrderBook(bestBid: 0.49m, bestAsk: 0.49m)),
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new MarketDataWebSocketOptions(),
            new PaperTradingOptions { OpenOrderFillSimulationBatchSize = 1 },
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var paperResult = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(4, paperResult.OpenOrdersChecked);
        Assert.Equal(2, paperResult.OrdersExpired);
        Assert.Equal(1, paperResult.OrdersFilled);
        Assert.Equal(2, repository.PaperOrders.Count(order => order.Status == PaperOrderStatus.Expired));
        Assert.Equal(1, repository.PaperOrders.Count(order => order.Status == PaperOrderStatus.Filled));
        Assert.Equal(1, repository.PaperOrders.Count(order => order.Status == PaperOrderStatus.Pending));
    }

    [Fact]
    public async Task PaperTradingProcessor_PrioritizesInitialExecutableGtdOrdersWithinBatch()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        await repository.AddPaperOrderAsync(PaperOrder("ordinary-active", now.AddMinutes(2)));

        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_1h_preopen_full_down_49");
        var urgentOrderId = Guid.NewGuid();
        await repository.AddPaperOrderAsync(new PaperOrder(
            urgentOrderId,
            Guid.NewGuid(),
            variant.CopiedTraderWallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-down",
            "condition-1",
            "Down",
            0.49m,
            10m,
            4.90m,
            now.AddMinutes(-1),
            now.AddMinutes(30),
            StrategyId: variant.Id,
            RawDecisionJson: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["pricing_mode"] = "paper_gtd_limit",
                ["order_type"] = "GTD",
                ["order_execution_mode"] = "GTD",
                ["paper_gtd_initial_snapshot_at_utc"] = now.AddMinutes(-1).ToString("O"),
                ["paper_gtd_initial_best_bid"] = 0.48m,
                ["paper_gtd_initial_best_ask"] = 0.47m,
                ["paper_gtd_initial_last_trade_price"] = 0.44m,
                ["paper_gtd_initial_queue_ahead_shares"] = 0m,
                ["paper_gtd_initial_executable_ask_shares"] = 6m,
                ["paper_gtd_initial_executable_ask_vwap"] = 0.48m
            })));

        var paperProcessor = new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            new FakeClobClient(OrderBook(bestBid: 0.45m, bestAsk: 0.60m)),
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new MarketDataWebSocketOptions(),
            new PaperTradingOptions { OpenOrderFillSimulationBatchSize = 1 },
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var paperResult = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(1, paperResult.OrdersFilled);
        var fill = Assert.Single(repository.PaperFills);
        Assert.Equal(urgentOrderId, fill.PaperOrderId);
        Assert.Contains("ConservativeGtdImmediateFill", fill.Evidence);
        Assert.Equal(PaperOrderStatus.PartiallyFilled, repository.PaperOrders.Single(order => order.Id == urgentOrderId).Status);
        Assert.Equal(PaperOrderStatus.Pending, repository.PaperOrders.Single(order => order.AssetId == "ordinary-active").Status);
    }

    [Fact]
    public async Task PaperTradingProcessor_FillsInitialExecutableGtdOrderBeforeExpiringIt()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_1h_preopen_full_down_49");
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            variant.CopiedTraderWallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-down",
            "condition-1",
            "Down",
            0.49m,
            10m,
            4.90m,
            now.AddMinutes(-3),
            now.AddSeconds(-1),
            StrategyId: variant.Id,
            RawDecisionJson: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["pricing_mode"] = "paper_gtd_limit",
                ["order_type"] = "GTD",
                ["order_execution_mode"] = "GTD",
                ["paper_gtd_initial_snapshot_at_utc"] = now.AddMinutes(-3).ToString("O"),
                ["paper_gtd_initial_best_bid"] = 0.48m,
                ["paper_gtd_initial_best_ask"] = 0.47m,
                ["paper_gtd_initial_last_trade_price"] = 0.44m,
                ["paper_gtd_initial_queue_ahead_shares"] = 0m,
                ["paper_gtd_initial_executable_ask_shares"] = 6m,
                ["paper_gtd_initial_executable_ask_vwap"] = 0.48m
            }));
        await repository.AddPaperOrderAsync(order);

        var paperProcessor = CreatePaperProcessor(
            repository,
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.48m, 100m)],
                [new OrderBookLevel(0.60m, 100m)]));

        var paperResult = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(1, paperResult.OrdersFilled);
        Assert.Equal(0, paperResult.OrdersExpired);
        var fill = Assert.Single(repository.PaperFills);
        Assert.Equal(order.Id, fill.PaperOrderId);
        Assert.Contains("ConservativeGtdImmediateFill", fill.Evidence);
        var updatedOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.PartiallyFilled, updatedOrder.Status);
        Assert.Contains("filled_immediate_marketable", updatedOrder.RawDecisionJson);
    }

    [Fact]
    public async Task PaperTradingMarketDataUpdater_FillsInitialExecutableGtdOrderBeforeExpiringIt()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_1h_preopen_full_down_49");
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            variant.CopiedTraderWallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-down",
            "condition-1",
            "Down",
            0.49m,
            10m,
            4.90m,
            now.AddMinutes(-3),
            now.AddSeconds(-1),
            StrategyId: variant.Id,
            RawDecisionJson: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["pricing_mode"] = "paper_gtd_limit",
                ["order_type"] = "GTD",
                ["order_execution_mode"] = "GTD",
                ["paper_gtd_initial_snapshot_at_utc"] = now.AddMinutes(-3).ToString("O"),
                ["paper_gtd_initial_best_bid"] = 0.48m,
                ["paper_gtd_initial_best_ask"] = 0.47m,
                ["paper_gtd_initial_last_trade_price"] = 0.44m,
                ["paper_gtd_initial_queue_ahead_shares"] = 0m,
                ["paper_gtd_initial_executable_ask_shares"] = 6m,
                ["paper_gtd_initial_executable_ask_vwap"] = 0.48m
            }));
        await repository.AddPaperOrderAsync(order);

        var updater = new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        await updater.ApplyUpdateAsync(new MarketDataUpdate(
            MarketDataEventType.Book,
            "book",
            "asset-down",
            "condition-1",
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.48m, 100m)],
                [new OrderBookLevel(0.60m, 100m)]),
            BestBid: 0.48m,
            BestAsk: 0.60m,
            Price: null,
            Size: null,
            TradeSide.Buy,
            MarketResolved: false,
            now));

        var fill = Assert.Single(repository.PaperFills);
        Assert.Equal(order.Id, fill.PaperOrderId);
        Assert.Contains("ConservativeGtdImmediateFill", fill.Evidence);
        var updatedOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.PartiallyFilled, updatedOrder.Status);
        Assert.Contains("filled_immediate_marketable", updatedOrder.RawDecisionJson);
    }

    [Fact]
    public async Task PaperTradingProcessor_PaperGtdLimitUsesInitialExecutableAskForImmediateFill()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_1h_preopen_full_down_49");
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            variant.CopiedTraderWallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-down",
            "condition-1",
            "Down",
            0.49m,
            10m,
            4.90m,
            now.AddMinutes(-1),
            now.AddMinutes(30),
            StrategyId: variant.Id,
            RawDecisionJson: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["pricing_mode"] = "paper_gtd_limit",
                ["order_type"] = "GTD",
                ["order_execution_mode"] = "GTD",
                ["paper_gtd_initial_snapshot_at_utc"] = now.AddMinutes(-1).ToString("O"),
                ["paper_gtd_initial_best_bid"] = 0.48m,
                ["paper_gtd_initial_best_ask"] = 0.47m,
                ["paper_gtd_initial_last_trade_price"] = 0.44m,
                ["paper_gtd_initial_queue_ahead_shares"] = 0m,
                ["paper_gtd_initial_executable_ask_shares"] = 6m,
                ["paper_gtd_initial_executable_ask_vwap"] = 0.48m
            }));
        await repository.AddPaperOrderAsync(order);

        var paperProcessor = CreatePaperProcessor(
            repository,
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.48m, 100m)],
                [new OrderBookLevel(0.51m, 100m)]));

        var paperResult = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(1, paperResult.OrdersFilled);
        var fill = Assert.Single(repository.PaperFills);
        Assert.Equal(6m, fill.SizeShares);
        Assert.Equal(0.49m, fill.Price);
        Assert.Contains("ConservativeGtdImmediateFill", fill.Evidence);
        var updatedOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.PartiallyFilled, updatedOrder.Status);
        Assert.Contains("filled_immediate_marketable", updatedOrder.RawDecisionJson);
    }

    private static PaperTradingProcessor CreatePaperProcessor(
        TestAppRepository repository,
        OrderBookSnapshot orderBook)
    {
        return new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            new FakeClobClient(orderBook),
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new MarketDataWebSocketOptions(),
            new PaperTradingOptions(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);
    }

    private static PaperOrder PaperOrder(string assetId, DateTimeOffset expiresAtUtc)
    {
        var now = DateTimeOffset.UtcNow;
        return new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Wallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            assetId,
            "condition-1",
            "Yes",
            0.50m,
            10m,
            5m,
            now,
            expiresAtUtc);
    }

    private static ISignalEngine SignalEngine()
    {
        var riskOptions = new RiskOptions();
        var paperOptions = new PaperTradingOptions { InitialBankrollUsd = 10_000m };
        return new DefaultSignalEngine(
            new SignalOptions
            {
                RequireKnownMarketCategory = true,
                RequireLeaderCategoryPerformance = true,
                MinLeaderCategoryResolvedPositions = 3,
                MinLeaderCategoryResolvedRoiPct = 0m,
                MinLeaderCategoryWinRatePct = 50m,
                MinLeaderCategoryScore = 0m,
                MinLeaderCategorySampleQuality = "Low"
            },
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

    private static PolymarketOnChainTokenMetadata TokenMetadata()
    {
        return new PolymarketOnChainTokenMetadata(
            "asset-1",
            "condition-1",
            "market-1",
            "sample-market",
            "Will sample event happen?",
            "Yes",
            0,
            "POLITICS",
            DateTimeOffset.UtcNow.AddDays(2),
            Active: true,
            Closed: false,
            Archived: false,
            Resolved: false,
            WinningOutcome: null,
            ClobTokenIds: ["asset-1", "asset-2"],
            Outcomes: ["Yes", "No"],
            LookupSucceeded: true,
            LookupError: null,
            RawJson: "{}",
            LastRefreshedUtc: DateTimeOffset.UtcNow);
    }

    private static PolymarketOnChainWalletCategoryPerformance CategoryPerformance()
    {
        return new PolymarketOnChainWalletCategoryPerformance(
            Wallet,
            "POLITICS",
            PositionsCount: 12,
            OpenPositions: 2,
            FlatPositions: 3,
            ResolvedPositions: 7,
            ProfitableResolvedPositions: 5,
            LosingResolvedPositions: 2,
            MarketsTraded: 10,
            VolumeUsd: 5_000m,
            ResolvedVolumeUsd: 3_000m,
            OpenExposureUsd: 500m,
            ResolvedCostUsd: 2_000m,
            ResolvedPnlUsd: 250m,
            ResolvedRoiPct: 12.5m,
            WinRatePct: 71.4m,
            AveragePositionSizeUsd: 416.67m,
            Score: 120m,
            SampleQuality: "Low",
            FirstActiveUtc: DateTimeOffset.UtcNow.AddDays(-30),
            LastActiveUtc: DateTimeOffset.UtcNow.AddHours(-1),
            RefreshedAtUtc: DateTimeOffset.UtcNow);
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
        return OrderBook(
            "asset-1",
            [new OrderBookLevel(bestBid, 1_000m)],
            [new OrderBookLevel(bestAsk, 1_000m)]);
    }

    private static OrderBookSnapshot OrderBook(
        string assetId,
        IReadOnlyList<OrderBookLevel> bids,
        IReadOnlyList<OrderBookLevel> asks)
    {
        return new OrderBookSnapshot(
            assetId,
            bids,
            asks,
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

        public Task<IReadOnlyList<LeaderTrade>> GetMarketTradesAsync(
            string conditionId,
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

    private sealed class NoOpPaperSettlementProcessor : IPaperSettlementProcessor
    {
        public Task<PaperSettlementProcessingResult> ProcessOpenPositionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaperSettlementProcessingResult(0, 0, 0, 0));
        }

        public Task<PaperSettlementProcessingResult> SettleMarketResolutionAsync(
            string? conditionId,
            string? assetId,
            string? winningAssetId,
            string? winningOutcome,
            string? category,
            string settlementSource,
            DateTimeOffset settledAtUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaperSettlementProcessingResult(0, 0, 0, 0));
        }
    }
}
