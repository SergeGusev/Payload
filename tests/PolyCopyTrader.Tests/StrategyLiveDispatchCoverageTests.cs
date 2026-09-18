using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed partial class BtcUpDown5mPaperStrategyProcessorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StrategyLiveDispatchCoverage_Eth22ChildRoiUsesOwnLiveCheckbox(bool parentLive, bool childLive)
    {
        var parent = StrategyIds.UpDown5mStrategyVariants.Single(v => v.Id == StrategyIds.EthDiffConfirmedAveragePremarketParent);
        var child = StrategyIds.UpDown5mStrategyVariants.Single(v => v.Id == Guid.Parse("b7c50005-0000-4000-8195-000000000022"));
        var trading = new CapturingTradingClient
        {
            PlacementResult = new LiveOrderPlacementResult(true, "0xchild-matrix", "matched", null,
                "0.80", "1.25", "{\"status\":\"matched\",\"makingAmount\":\"0.80\",\"takingAmount\":\"1.25\"}", "{}")
        };
        var liveCodes = new List<string>();
        if (parentLive) liveCodes.Add(parent.Code);
        if (childLive) liveCodes.Add(child.Code);
        var context = CreateEthConfirmedAverageTestContext(2020m, [parent.Code, child.Code], trading,
            configureRepository: (repo, now) => AddDynamicChildLiveAssignment(repo, parent, child, now),
            liveStrategyCodes: liveCodes);

        var result = await context.Processor.ProcessDiffCounterDueEntriesAsync();

        var hasChildEntry = !parentLive || childLive;
        Assert.Equal(hasChildEntry ? 2 : 1, result.EntriesPlaced);
        Assert.Equal((parentLive ? 1 : 0) + (childLive ? 1 : 0), trading.PlaceCalls);
        var parentPaper = Assert.Single(context.Repository.PaperOrders, p => p.StrategyId == parent.Id);
        if (!hasChildEntry)
        {
            // Preserve the existing Paper-off path: a Live parent did not create a dynamic Paper child.
            Assert.DoesNotContain(context.Repository.PaperOrders, p => p.StrategyId == child.Id);
            await context.Processor.ProcessDiffCounterDueEntriesAsync();
            Assert.Single(trading.Requests);
            return;
        }
        var childPaper = Assert.Single(context.Repository.PaperOrders, p => p.StrategyId == child.Id);
        Assert.Equal(parentPaper.AssetId, childPaper.AssetId);
        if (childLive)
        {
            var childOrder = Assert.Single(context.Repository.LiveOrders, o => o.StrategyId == child.Id);
            using var raw = JsonDocument.Parse(childPaper.RawDecisionJson!);
            var intent = raw.RootElement.GetProperty("parent_execution_intent");
            Assert.Equal(intent.GetProperty("target_notional_usd").GetDecimal(), childOrder.NotionalUsd);
            Assert.Equal(intent.GetProperty("maximum_order_price").GetDecimal(), childOrder.Price);
            Assert.Equal("FAK", childOrder.OrderType);
            Assert.False(childOrder.PostOnly);
        }
        else
        {
            Assert.DoesNotContain(context.Repository.LiveOrders, o => o.StrategyId == child.Id);
            Assert.Equal(PaperOrderStatus.Filled, childPaper.Status);
        }
        var previousCalls = trading.PlaceCalls;
        await context.Processor.ProcessDiffCounterDueEntriesAsync();
        Assert.Equal(previousCalls, trading.PlaceCalls);
        Assert.Single(context.Repository.PaperOrders, p => p.StrategyId == child.Id);
    }

    [Theory]
    [InlineData(BtcUpDown5mStrategyBehavior.ChildMirror)]
    [InlineData(BtcUpDown5mStrategyBehavior.ChildRoiMirror)]
    [InlineData(BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror)]
    public async Task StrategyLiveDispatchCoverage_DynamicChildCopiesTenDollarRequestDespiteThreeDollarParentFill(
        BtcUpDown5mStrategyBehavior behavior)
    {
        var parent = StrategyIds.UpDown5mStrategyVariants.Single(v => v.Id == StrategyIds.EthDiffConfirmedAveragePremarketParent);
        var child = behavior == BtcUpDown5mStrategyBehavior.ChildRoiMirror
            ? StrategyIds.UpDown5mStrategyVariants.Single(v => v.Id == Guid.Parse("b7c50005-0000-4000-8195-000000000022"))
            : StrategyIds.UpDown5mStrategyVariants.First(v => v.Behavior == behavior && v.ReferenceAssetSymbol == "ETH");
        // Selection is outside this test: exercise the persisted, accepted assignment without changing its request.
        var trading = new CapturingTradingClient
        {
            PlacementResult = new LiveOrderPlacementResult(true, "0xpartial-original", "matched", null,
                "3", "5", "{\"status\":\"matched\",\"makingAmount\":\"3\",\"takingAmount\":\"5\"}", "{}")
        };
        var context = CreateEthConfirmedAverageTestContext(2020m, [parent.Code, child.Code], trading,
            configureRepository: (repo, now) => AddDynamicChildLiveAssignment(repo, parent, child, now),
            maximumLiveOrderNotionalUsd: 20m);
        context.Repository.StrategySettings[parent.Id] = context.Repository.StrategySettings[parent.Id] with { LiveStakeAmount = 10m };
        context.Repository.StrategySettings[child.Id] = context.Repository.StrategySettings[child.Id] with
        {
            LiveStakeAmount = 1m, LiveLostCoeff = 2m, LiveLostCounter = 3
        };

        await context.Processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(2, trading.PlaceCalls);
        var parentOrder = Assert.Single(context.Repository.LiveOrders, o => o.StrategyId == parent.Id);
        var childOrder = Assert.Single(context.Repository.LiveOrders, o => o.StrategyId == child.Id);
        Assert.Equal(3m, parentOrder.FilledNotionalUsd);
        Assert.Equal(10m, parentOrder.NotionalUsd);
        Assert.Equal(10m, childOrder.NotionalUsd);
        Assert.Equal(10m, trading.Requests[1].MarketBuyAmountUsd);
        Assert.Equal(trading.Requests[0].Price, trading.Requests[1].Price);
        Assert.Equal(trading.Requests[0].SizeShares, trading.Requests[1].SizeShares);
        Assert.Equal(trading.Requests[0].TokenId, trading.Requests[1].TokenId);
        Assert.NotEqual(parentOrder.SignalId, childOrder.SignalId);
        Assert.NotEqual(parentOrder.Id, childOrder.Id);
        Assert.NotEqual(parentOrder.CorrelationId, childOrder.CorrelationId);
        Assert.Equal(1, context.ClobClient.GetOrderBookCalls);
    }

    [Fact]
    public async Task StrategyLiveDispatchCoverage_DynamicChildChecksOwnBalance()
    {
        var parent = StrategyIds.UpDown5mStrategyVariants.Single(v => v.Id == StrategyIds.EthDiffConfirmedAveragePremarketParent);
        var child = StrategyIds.UpDown5mStrategyVariants.Single(v => v.Id == Guid.Parse("b7c50005-0000-4000-8195-000000000022"));
        var trading = new CapturingTradingClient();
        var context = CreateEthConfirmedAverageTestContext(2020m, [parent.Code, child.Code], trading,
            configureRepository: (repo, now) => AddDynamicChildLiveAssignment(repo, parent, child, now),
            liveStrategyCodes: [child.Code]);
        context.Repository.StrategySettings[child.Id] = context.Repository.StrategySettings[child.Id] with { LiveAvailableBalance = 0m };

        await context.Processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, trading.PlaceCalls);
        Assert.Single(context.Repository.PaperOrders, p => p.StrategyId == parent.Id && p.Status == PaperOrderStatus.Filled);
        Assert.Contains(context.Repository.LiveOrders, o => o.StrategyId == child.Id && o.Status == LiveOrderStatus.PreflightRejected);
    }

    private static void AddDynamicChildLiveAssignment(TestAppRepository repo, BtcUpDown5mStrategyVariant parent,
        BtcUpDown5mStrategyVariant child, DateTimeOffset now)
    {
        repo.StrategyChildParentAssignments.Add(new StrategyChildParentAssignment(Guid.NewGuid(), child.Id, parent.Id,
            child.ReferenceAssetSymbol!, child.DecisionDepth, child.Behavior.ToString(), 1m, 1m,
            now.AddHours(-1), null, now.AddHours(-1)));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(0)]
    public async Task StrategyLiveDispatchCoverage_FollowMarketPreservesMinimumRequestWithoutDepthGate(int askDepth)
    {
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 30, TimeSpan.Zero);
        var start = now.AddSeconds(-30);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(v => v.Code == "btc_up_down_5m_follow_market_30_65");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(start, start.AddMinutes(5), 0.65m, 0.35m));
        repository.StrategySettings[variant.Id] = StrategyRuntimeSettings.Default(variant.Id) with
        {
            LiveStakes = true, LiveStakeAmount = 90m, LiveAvailableBalance = 100m
        };
        var books = new[]
        {
            OrderBook("asset-up", [new OrderBookLevel(0.64m, 100m)], [new OrderBookLevel(askDepth == 0 ? 0.995m : 0.65m, 100m)], now, minOrderSize: 5m, tickSize: 0.001m),
            OrderBook("asset-down", [new OrderBookLevel(0.34m, 100m)], [new OrderBookLevel(0.35m, 100m)], now, minOrderSize: 5m, tickSize: 0.01m)
        };
        var trading = new CapturingTradingClient();
        var processor = CreateProcessorCoreWithOptions(repository, [], books, _ => { }, books,
            CreateBtcOptions(false, [variant.Code]), tradingClient: trading,
            botOptions: new BotOptions { Mode = BotMode.Live, EnableLiveTrading = true },
            paperTradingOptions: new PaperTradingOptions { InitialBankrollUsd = 10000m, RunInLiveMode = true },
            liveTradingOptions: new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 10m },
            timeProvider: new ManualTimeProvider(now));

        await processor.ProcessAsync();

        var request = Assert.Single(trading.Requests);
        Assert.Equal(4.95m, request.MarketBuyAmountUsd);
        Assert.Equal(5m, request.SizeShares);
        Assert.Equal(0.99m, request.Price);
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        await processor.ProcessAsync();
        Assert.Single(trading.Requests);
    }

    [Fact]
    public async Task StrategyLiveDispatchCoverage_FollowMarketRejectsMissingFinalVenueMinimumWithLiveEnabled()
    {
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 30, TimeSpan.Zero);
        var start = now.AddSeconds(-30);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(v => v.Code == "btc_up_down_5m_follow_market_30_65");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(start, start.AddMinutes(5), 0.65m, 0.35m));
        repository.StrategySettings[variant.Id] = StrategyRuntimeSettings.Default(variant.Id) with
        {
            LiveStakes = true, LiveStakeAmount = 90m, LiveAvailableBalance = 100m
        };
        var books = new[]
        {
            OrderBook("asset-up", [new OrderBookLevel(0.64m, 100m)], [new OrderBookLevel(0.65m, 100m)], now, minOrderSize: null, tickSize: 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.34m, 100m)], [new OrderBookLevel(0.35m, 100m)], now, minOrderSize: 5m, tickSize: 0.01m)
        };
        var clob = new FakeClobClient(books);
        var trading = new CapturingTradingClient();
        var processor = CreateProcessorCoreWithOptions(repository, [], books, _ => { }, books,
            CreateBtcOptions(false, [variant.Code]), tradingClient: trading, clobClient: clob,
            botOptions: new BotOptions { Mode = BotMode.Live, EnableLiveTrading = true },
            paperTradingOptions: new PaperTradingOptions { InitialBankrollUsd = 10000m, RunInLiveMode = true },
            liveTradingOptions: new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 100m },
            timeProvider: new ManualTimeProvider(now));

        var result = await processor.ProcessAsync();
        await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        Assert.Equal(1, clob.GetOrderBookCallsForAsset("asset-up"));
        Assert.Equal("follow_market_min_order_size_unavailable", Assert.Single(repository.StrategyMarketPaperRuns).SkipReason);
        Assert.Empty(trading.Requests);
        Assert.Empty(repository.LiveOrders);
        Assert.Empty(repository.PaperOrders);
        Assert.Empty(repository.PaperLiveShadowDecisions);
    }
}
