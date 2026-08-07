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
        var repository = new TestAppRepository
        {
            UseCaseSensitivePaperPositionLookup = true
        };
        var now = DateTimeOffset.UtcNow;
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Wallet.ToUpperInvariant(),
            PaperOrderStatus.Pending,
            TradeSide.Sell,
            "ASSET-1",
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

        var exposureCache = new ExposureSnapshotCache(repository);
        var paperProcessor = new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            new FakeClobClient(OrderBook(bestBid: 0.74m, bestAsk: 0.75m)),
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new MarketDataWebSocketOptions(),
            new PaperTradingOptions(),
            exposureCache,
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var paperResult = await paperProcessor.ProcessOpenOrdersAsync();
        await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(1, paperResult.OrdersFilled);
        Assert.Equal(PaperOrderStatus.Filled, repository.PaperOrders.Single().Status);
        var fill = Assert.Single(repository.PaperFills);
        Assert.Equal(3.50m, fill.RealizedPnlUsd);
        var position = Assert.Single(repository.PaperPositions);
        Assert.Equal(75m, position.SizeShares);
        Assert.Equal(Wallet, position.CopiedTraderWallet);
        Assert.Equal(1, repository.GetOpenPaperPositionsCalls);
        Assert.Equal(1, repository.GetPaperPositionCalls);
    }

    [Fact]
    public async Task PaperTradingProcessor_SellFillRechecksStaleCachedPositionInRepository()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var openPosition = new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            100m,
            0.60m,
            73m,
            13m,
            now,
            Wallet);
        await repository.UpsertPaperPositionAsync(openPosition);
        var exposureCache = new ExposureSnapshotCache(repository);
        await exposureCache.GetSnapshotAsync();

        await repository.UpsertPaperPositionAsync(openPosition with
        {
            SizeShares = 0m,
            EstimatedValueUsd = 0m,
            UnrealizedPnlUsd = 0m,
            UpdatedAtUtc = now.AddSeconds(1)
        });
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
        var paperProcessor = new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            new FakeClobClient(OrderBook(bestBid: 0.74m, bestAsk: 0.75m)),
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new MarketDataWebSocketOptions(),
            new PaperTradingOptions(),
            exposureCache,
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var result = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(0, result.OrdersFilled);
        Assert.Empty(repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(repository.PaperOrders).Status);
        Assert.Equal(0m, Assert.Single(repository.PaperPositions).SizeShares);
        Assert.Equal(1, repository.GetOpenPaperPositionsCalls);
        Assert.Equal(1, repository.GetPaperPositionCalls);
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
            item.Code == "btc_up_down_1h_preopen_half_down_49");
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
            item.Code == "btc_up_down_1h_preopen_half_down_49");
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
        Assert.Equal(1, repository.GetOpenPaperPositionsCalls);
        Assert.Equal(0, repository.GetPaperPositionsCalls);
    }

    [Fact]
    public async Task PaperTradingMarketDataUpdater_FillsInitialExecutableGtdOrderBeforeExpiringIt()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_1h_preopen_half_down_49");
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
        Assert.Equal(1, repository.GetOpenPaperPositionsCalls);
        Assert.Equal(0, repository.GetPaperPositionsCalls);
    }

    [Fact]
    public async Task PaperTradingProcessor_PaperGtdLimitUsesInitialExecutableAskForImmediateFill()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_1h_preopen_half_down_49");
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

    [Fact]
    public async Task PaperTradingProcessor_FakPaperOrderUsesExecutableAskVwapInsteadOfWorstPrice()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var immutableOrderBook = OrderBook(
            "asset-down",
            [new OrderBookLevel(0.19m, 100m)],
            [
                new OrderBookLevel(0.20m, 10m),
                new OrderBookLevel(0.30m, 10m)
            ]) with
        {
            SnapshotAtUtc = now
        };
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Wallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-down",
            "condition-1",
            "Down",
            0.99m,
            5.05050505m,
            5m,
            now,
            now.AddMinutes(5));
        order = WithImmutableFakExecutionContext(order, immutableOrderBook);
        await repository.AddPaperOrderAsync(order);

        // The current CLOB is deliberately non-executable. The fill must come
        // only from the immutable snapshot persisted with the decision.
        var paperProcessor = CreatePaperProcessor(
            repository,
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.98m, 100m)],
                [new OrderBookLevel(1.00m, 100m)]));

        var paperResult = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(1, paperResult.OrdersFilled);
        var fill = Assert.Single(repository.PaperFills);
        Assert.Equal(0.25m, fill.Price);
        Assert.Equal(20m, fill.SizeShares);
        Assert.Contains("FakTakerPaperFill", fill.Evidence);
        Assert.Equal("Taker", fill.FeeLiquidityRole);

        var updatedOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, updatedOrder.Status);
        Assert.Equal(0.25m, updatedOrder.Price);
        Assert.Equal(20m, updatedOrder.SizeShares);
        Assert.Equal(5m, updatedOrder.NotionalUsd);
        Assert.Contains("fak_taker_executable_snapshot_v2", updatedOrder.RawDecisionJson);
        Assert.Contains("\"paper_execution_evidence_class\":\"paper_executable_snapshot_model\"", updatedOrder.RawDecisionJson);
        Assert.Contains("\"paper_fak_replay_eligible\":true", updatedOrder.RawDecisionJson);
        Assert.Contains("\"paper_fak_worst_price\":0.99", updatedOrder.RawDecisionJson);
        Assert.Contains("\"paper_fak_average_fill_price\":0.25", updatedOrder.RawDecisionJson);

        var position = Assert.Single(repository.PaperPositions);
        Assert.Equal(20m, position.SizeShares);
        Assert.Equal(0.25m, position.AveragePrice);
    }

    [Fact]
    public async Task PaperTradingProcessor_FakPaperOrderRejectsLegacyRowWithoutImmutableSnapshotEvenWhenCurrentBookIsExecutable()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Wallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-down",
            "condition-1",
            "Down",
            0.50m,
            10m,
            5m,
            now,
            now.AddMinutes(5),
            RawDecisionJson: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["paper_order_type"] = "FAK"
            }));
        await repository.AddPaperOrderAsync(order);

        var paperProcessor = CreatePaperProcessor(
            repository,
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.47m, 100m)],
                [new OrderBookLevel(0.48m, 100m)]));

        var paperResult = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(0, paperResult.OrdersFilled);
        Assert.Equal(1, paperResult.OrdersExpired);
        Assert.Empty(repository.PaperFills);
        Assert.Empty(repository.PaperPositions);

        var updatedOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Rejected, updatedOrder.Status);
        Assert.Equal(0.50m, updatedOrder.Price);
        Assert.NotNull(updatedOrder.CancelledAtUtc);
        Assert.Contains("paper_fak_immutable_snapshot_missing", updatedOrder.RawDecisionJson);
        Assert.Contains("fak_taker_executable_snapshot_v2", updatedOrder.RawDecisionJson);
        Assert.Contains("\"paper_execution_evidence_class\":\"legacy_non_reproducible\"", updatedOrder.RawDecisionJson);
        Assert.Contains("\"paper_fak_replay_eligible\":false", updatedOrder.RawDecisionJson);
        Assert.Contains("\"paper_fak_worst_price\":0.50", updatedOrder.RawDecisionJson);
    }

    [Fact]
    public async Task PaperTradingProcessor_FakPaperOrderFillsOnlyDepthWithinPersistedPriceAndCancelsRemainder()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var immutableOrderBook = OrderBook(
            "asset-down",
            [new OrderBookLevel(0.47m, 100m)],
            [
                new OrderBookLevel(0.48m, 1m),
                new OrderBookLevel(0.52m, 100m)
            ]) with
        {
            SnapshotAtUtc = now
        };
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Wallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-down",
            "condition-1",
            "Down",
            0.50m,
            10m,
            5m,
            now,
            now.AddMinutes(5));
        order = WithImmutableFakExecutionContext(order, immutableOrderBook);
        await repository.AddPaperOrderAsync(order);

        // The current CLOB could fully fill the request. A partial result proves
        // the processor used the persisted decision-time snapshot instead.
        var paperProcessor = CreatePaperProcessor(
            repository,
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.19m, 100m)],
                [new OrderBookLevel(0.20m, 100m)]));

        var paperResult = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(1, paperResult.OrdersFilled);
        var fill = Assert.Single(repository.PaperFills);
        Assert.Equal(0.48m, fill.Price);
        Assert.Equal(1m, fill.SizeShares);

        var updatedOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.PartiallyFilledExpired, updatedOrder.Status);
        Assert.Equal(0.48m, updatedOrder.Price);
        Assert.Equal(0.48m, updatedOrder.NotionalUsd);
        Assert.Null(updatedOrder.FilledAtUtc);
        Assert.NotNull(updatedOrder.CancelledAtUtc);
        Assert.Contains("\"paper_fak_replay_eligible\":true", updatedOrder.RawDecisionJson);
        Assert.Contains("\"paper_fak_worst_price\":0.50", updatedOrder.RawDecisionJson);
        Assert.Contains("\"paper_fak_levels_used\":1", updatedOrder.RawDecisionJson);
        Assert.Contains("\"paper_fak_partial_fill\":true", updatedOrder.RawDecisionJson);
    }

    [Fact]
    public async Task PaperTradingProcessor_FakPaperOrderWithExistingFillIsFailSafeNoOp()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        const decimal fillPrice = 0.48m;
        var filledAtUtc = now.AddSeconds(-1);
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Wallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-down",
            "condition-1",
            "Down",
            0.50m,
            1m,
            0.50m,
            now.AddMinutes(-1),
            now.AddMinutes(5),
            RawDecisionJson: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["paper_order_type"] = "FAK"
            }));
        var persistedFill = new PaperFill(
            Guid.NewGuid(),
            order.Id,
            fillPrice,
            1m,
            filledAtUtc,
            "persisted-before-order-terminal-update");
        var accountedPosition = new PaperPosition(
            order.AssetId,
            order.ConditionId,
            order.Outcome,
            1m,
            fillPrice,
            fillPrice,
            0m,
            filledAtUtc,
            Wallet);
        var copiedLeaderPosition = new PaperCopiedLeaderPosition(
            Guid.NewGuid(),
            order.SignalId,
            order.Id,
            Wallet,
            order.AssetId,
            order.ConditionId,
            order.Outcome,
            EntryTransactionHash: null,
            EntryTimestampUtc: order.CreatedAtUtc,
            LeaderEntryPrice: order.Price,
            LeaderInitialSizeShares: 1m,
            CopiedInitialSizeShares: 1m,
            LeaderSoldSizeShares: 0m,
            CopiedExitRequestedSizeShares: 0m,
            Status: PaperCopiedLeaderPositionStatus.Active,
            LastActivityTimestampUtc: null,
            LastActivityTransactionHash: null,
            LastActivitySyncAtUtc: null,
            NextActivitySyncAtUtc: now.AddMinutes(1),
            CreatedAtUtc: order.CreatedAtUtc,
            UpdatedAtUtc: filledAtUtc);
        await repository.AddPaperOrderAsync(order);
        await repository.AddPaperFillAsync(persistedFill);
        await repository.UpsertPaperPositionAsync(accountedPosition);
        repository.PaperCopiedLeaderPositions.Add(copiedLeaderPosition);

        var paperProcessor = CreatePaperProcessor(
            repository,
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.47m, 100m)],
                [new OrderBookLevel(0.48m, 100m)]));

        var firstResult = await paperProcessor.ProcessOpenOrdersAsync();
        var secondResult = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(0, firstResult.OrdersFilled);
        Assert.Equal(0, firstResult.OrdersExpired);
        Assert.Equal(0, secondResult.OrdersFilled);
        Assert.Equal(0, secondResult.OrdersExpired);

        var retainedOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(order, retainedOrder);
        Assert.Equal(PaperOrderStatus.Pending, retainedOrder.Status);
        Assert.Null(retainedOrder.FilledAtUtc);
        Assert.Null(retainedOrder.CancelledAtUtc);

        Assert.Equal(persistedFill, Assert.Single(repository.PaperFills));
        var retainedPosition = Assert.Single(repository.PaperPositions);
        Assert.Equal(1m, retainedPosition.SizeShares);
        Assert.Equal(fillPrice, retainedPosition.AveragePrice);
        var retainedCopiedPosition = Assert.Single(repository.PaperCopiedLeaderPositions);
        Assert.Equal(1m, retainedCopiedPosition.CopiedInitialSizeShares);
        Assert.Equal(PaperCopiedLeaderPositionStatus.Active, retainedCopiedPosition.Status);
    }

    [Fact]
    public async Task PaperTradingProcessor_FakImmutableContextAcceptsAuditDecisionIdPostgresMicrosecondsAndLaterRestSnapshot()
    {
        var repository = new TestAppRepository();
        var preDatabaseCreatedAtUtc = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)
            .AddTicks(1_234_567);
        var persistedCreatedAtUtc = preDatabaseCreatedAtUtc.AddTicks(
            -(preDatabaseCreatedAtUtc.Ticks % TimeSpan.TicksPerMicrosecond));
        var laterRestSnapshotAtUtc = preDatabaseCreatedAtUtc.AddMilliseconds(25);
        var runCorrelationDecisionId = Guid.Parse("7b310000-0000-4000-8000-000000000031");
        var immutableOrderBook = OrderBook(
            "asset-down",
            [new OrderBookLevel(0.47m, 100m)],
            [new OrderBookLevel(0.48m, 100m)]) with
        {
            SnapshotAtUtc = laterRestSnapshotAtUtc
        };
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Wallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-down",
            "condition-1",
            "Down",
            0.50m,
            10m,
            5m,
            persistedCreatedAtUtc,
            persistedCreatedAtUtc.AddMinutes(5));
        Assert.NotEqual(order.SignalId, runCorrelationDecisionId);
        Assert.InRange(
            (preDatabaseCreatedAtUtc - persistedCreatedAtUtc).Ticks,
            1,
            TimeSpan.TicksPerMicrosecond - 1);
        Assert.True(laterRestSnapshotAtUtc > preDatabaseCreatedAtUtc);
        order = WithImmutableFakExecutionContext(
            order,
            immutableOrderBook,
            runCorrelationDecisionId,
            preDatabaseCreatedAtUtc);
        await repository.AddPaperOrderAsync(order);

        var paperProcessor = CreatePaperProcessor(
            repository,
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.98m, 100m)],
                [new OrderBookLevel(1.00m, 100m)]));

        var result = await paperProcessor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersFilled);
        Assert.Single(repository.PaperFills);
        var filledOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, filledOrder.Status);
        using var decisionJson = JsonDocument.Parse(filledOrder.RawDecisionJson!);
        Assert.Equal(
            runCorrelationDecisionId,
            decisionJson.RootElement.GetProperty("execution_intent_decision_id").GetGuid());
        Assert.Equal(
            laterRestSnapshotAtUtc,
            decisionJson.RootElement
                .GetProperty("execution_intent_order_book_snapshot")
                .GetProperty("snapshot_at_utc")
                .GetDateTimeOffset());
    }

    private static PaperOrder WithImmutableFakExecutionContext(
        PaperOrder order,
        OrderBookSnapshot orderBook,
        Guid? decisionId = null,
        DateTimeOffset? intentCreatedAtUtc = null)
    {
        var intent = FakBuyExecutionIntent.Create(
            order.StrategyId,
            decisionId ?? order.SignalId,
            order.ConditionId,
            order.AssetId,
            order.Price,
            order.NotionalUsd,
            order.SizeShares,
            orderBook,
            intentCreatedAtUtc ?? order.CreatedAtUtc);
        var normalizedOrder = order with
        {
            SizeShares = intent.TargetSizeShares,
            NotionalUsd = intent.TargetNotionalUsd
        };
        return normalizedOrder with
        {
            RawDecisionJson = JsonSerializer.Serialize(new
            {
                paper_order_type = "FAK",
                execution_intent_strategy_id = intent.StrategyId.ToString(),
                execution_intent_decision_id = intent.DecisionId.ToString(),
                execution_intent_condition_id = intent.ConditionId,
                execution_intent_asset_id = intent.AssetId,
                execution_intent_side = intent.Side.ToString(),
                execution_intent_order_type = FakBuyExecutionIntent.TimeInForce,
                execution_intent_time_in_force = FakBuyExecutionIntent.TimeInForce,
                execution_intent_post_only = intent.PostOnly,
                execution_intent_maximum_order_price = intent.MaximumOrderPrice,
                execution_intent_requested_notional_usd = intent.RequestedNotionalUsd,
                execution_intent_requested_size_shares = intent.RequestedSizeShares,
                execution_intent_target_notional_usd = intent.TargetNotionalUsd,
                execution_intent_target_size_shares = intent.TargetSizeShares,
                execution_intent_tick_size = intent.TickSize,
                execution_intent_min_order_size = intent.MinOrderSize,
                execution_intent_negative_risk = intent.NegativeRisk,
                execution_intent_created_at_utc = intent.CreatedAtUtc.ToString("O"),
                execution_intent_order_book_snapshot = new
                {
                    source = "pipeline_test",
                    age_ms = 0,
                    asset_id = orderBook.AssetId,
                    condition_id = orderBook.ConditionId,
                    snapshot_at_utc = orderBook.SnapshotAtUtc,
                    min_order_size = orderBook.MinOrderSize,
                    tick_size = orderBook.TickSize,
                    negative_risk = orderBook.NegativeRisk,
                    last_trade_price = orderBook.LastTradePrice,
                    bids = orderBook.Bids.Select(level => new { price = level.Price, size = level.Size }).ToArray(),
                    asks = orderBook.Asks.Select(level => new { price = level.Price, size = level.Size }).ToArray()
                }
            })
        };
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
