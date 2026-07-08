using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.LiveTrading;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Service.Signals;
using PolyCopyTrader.Service.Startup;
using PolyCopyTrader.Service.Strategies;
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
    public async Task GeoblockCheckFailureCanBeConfiguredAsWarning()
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
            new ThrowingGeoClient(),
            blockOnGeoblockCheckFailure: false);

        await processor.ProcessQueuedAsync();

        Assert.Equal(0, tradingClient.PlaceCalls);
        var order = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.PreflightRejected, order.Status);
        Assert.DoesNotContain("Geoblock check failed", order.ValidationSummary, StringComparison.OrdinalIgnoreCase);
        var warning = Assert.Single(repository.LiveTradingEvents, item => item.Action == "GeoblockCheck");
        Assert.Equal("Warning", warning.Status);
        Assert.Contains("geoblock endpoint unavailable", warning.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BlockOnGeoblockCheckFailure is false", warning.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupGeoblockCheckFailureCanBeConfiguredAsWarning()
    {
        var repository = new TestAppRepository();
        var controlState = new ServiceControlState();
        var service = new StartupSafetyCheckService(
            NullLogger<StartupSafetyCheckService>.Instance,
            new ThrowingGeoClient(),
            new LiveTradingOptions { BlockOnGeoblockCheckFailure = false },
            controlState,
            repository);

        await service.StartAsync(CancellationToken.None);
        await WaitForLiveTradingEventAsync(repository, "StartupGeoblockCheck");

        Assert.False(controlState.LiveTradingPaused);
        var liveEvent = Assert.Single(repository.LiveTradingEvents, item => item.Action == "StartupGeoblockCheck");
        Assert.Equal("Warning", liveEvent.Status);
        Assert.Contains("geoblock endpoint unavailable", liveEvent.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BlockOnGeoblockCheckFailure is false", liveEvent.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupGeoblockCheckFailurePausesLiveByDefault()
    {
        var repository = new TestAppRepository();
        var controlState = new ServiceControlState();
        var service = new StartupSafetyCheckService(
            NullLogger<StartupSafetyCheckService>.Instance,
            new ThrowingGeoClient(),
            new LiveTradingOptions(),
            controlState,
            repository);

        await service.StartAsync(CancellationToken.None);
        await WaitForLiveTradingEventAsync(repository, "StartupGeoblockCheck");

        Assert.True(controlState.LiveTradingPaused);
        var liveEvent = Assert.Single(repository.LiveTradingEvents, item => item.Action == "StartupGeoblockCheck");
        Assert.Equal("Error", liveEvent.Status);
        Assert.Contains("geoblock endpoint unavailable", liveEvent.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiveEnabledRejectsLeaderPriceSignalUntilLiveExecutionPolicyExists()
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
            new PassGeoClient());

        var result = await processor.ProcessQueuedAsync();

        Assert.Equal(0, result.LiveOrdersSubmitted);
        Assert.Equal(0, tradingClient.PlaceCalls);
        Assert.Null(tradingClient.LastRequest);
        var order = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.PreflightRejected, order.Status);
        Assert.Contains("leader-price Follow leader signals is disabled", order.ValidationSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiveModeWithLiveLostCounterBoostsFollowLeaderPreflightNotional()
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
            new PassGeoClient(),
            liveStakeAmount: 0.74m,
            liveLostCoeff: 2m,
            liveLostCounter: 6,
            maxOrderNotionalUsd: 100m);

        var result = await processor.ProcessQueuedAsync();

        Assert.Equal(0, result.LiveOrdersSubmitted);
        Assert.Equal(0, tradingClient.PlaceCalls);
        var order = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.PreflightRejected, order.Status);
        Assert.Contains("leader-price Follow leader signals is disabled", order.ValidationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3m, order.SizeShares);
        Assert.Equal(2.22m, order.NotionalUsd);
    }

    [Fact]
    public async Task LivePreflightAllowsOpenLiveOrderInDifferentMarket()
    {
        var repository = new TestAppRepository();
        await repository.AddLiveOrderAsync(CreateOpenLiveOrder(
            DateTimeOffset.UtcNow,
            "other-asset",
            "other-condition",
            "No"));
        var queue = new InMemoryLeaderTradeCandidateQueue();
        await queue.EnqueueAsync(Trade());
        var tradingClient = new CapturingTradingClient();
        var processor = CreateProcessor(
            queue,
            repository,
            tradingClient,
            LiveEnabledBot(),
            new PassGeoClient());

        await processor.ProcessQueuedAsync();

        var candidateOrder = repository.LiveOrders.Single(order =>
            string.Equals(order.ConditionId, "condition-1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(LiveOrderStatus.PreflightRejected, candidateOrder.Status);
        Assert.Contains("leader-price Follow leader signals is disabled", candidateOrder.ValidationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Maximum open live order count reached", candidateOrder.ValidationSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LivePreflightRejectsOppositeLiveOrderInSameMarket()
    {
        var repository = new TestAppRepository();
        await repository.AddLiveOrderAsync(CreateOpenLiveOrder(
            DateTimeOffset.UtcNow,
            "asset-no",
            "condition-1",
            "No"));
        var queue = new InMemoryLeaderTradeCandidateQueue();
        await queue.EnqueueAsync(Trade());
        var tradingClient = new CapturingTradingClient();
        var processor = CreateProcessor(
            queue,
            repository,
            tradingClient,
            LiveEnabledBot(),
            new PassGeoClient());

        var result = await processor.ProcessQueuedAsync();

        Assert.Equal(1, result.SignalsAccepted);
        Assert.Equal(0, result.SignalsRejected);
        Assert.Equal(0, result.LiveOrdersSubmitted);
        Assert.Equal(0, tradingClient.PlaceCalls);
        Assert.Equal(2, repository.LiveOrders.Count);
        var candidateOrder = repository.LiveOrders.Single(order =>
            string.Equals(order.ConditionId, "condition-1", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(order.Outcome, "Yes", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(LiveOrderStatus.PreflightRejected, candidateOrder.Status);
        Assert.Contains(SignalReasonCodes.OppositeOutcomeOpenOrder, candidateOrder.ValidationSummary, StringComparison.Ordinal);
        Assert.Empty(repository.SignalRejections);
    }

    [Fact]
    public async Task LiveModeWithStrategyLiveStakesDisabledDoesNotCreateLiveOrder()
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
            new PassGeoClient(),
            liveStakes: false);

        var result = await processor.ProcessQueuedAsync();

        Assert.Equal(1, result.SignalsAccepted);
        Assert.Equal(0, result.LiveOrdersSubmitted);
        Assert.Equal(0, tradingClient.PlaceCalls);
        Assert.Empty(repository.LiveOrders);
    }

    [Fact]
    public async Task LiveModeWithPaperRunInLiveModeCreatesShadowPaperOrder()
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
            new PassGeoClient(),
            runPaperInLiveMode: true);

        var result = await processor.ProcessQueuedAsync();

        Assert.Equal(1, result.SignalsAccepted);
        Assert.Equal(1, result.PaperOrdersCreated);
        Assert.Single(repository.PaperOrders);
        Assert.Single(repository.LiveOrders);
        Assert.Equal(0, tradingClient.PlaceCalls);
    }

    [Fact]
    public async Task LiveModeWithInsufficientStrategyBalanceKeepsStrategyLiveStakesEnabled()
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
            new PassGeoClient(),
            liveAvailableBalance: 0.50m);

        var result = await processor.ProcessQueuedAsync();

        Assert.Equal(0, result.LiveOrdersSubmitted);
        Assert.Equal(0, tradingClient.PlaceCalls);
        Assert.True(repository.StrategySettings[StrategyIds.FollowLeader].LiveStakes);
        var order = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.PreflightRejected, order.Status);
        Assert.Contains("live available balance is insufficient", order.ValidationSummary, StringComparison.OrdinalIgnoreCase);
        var liveEvent = Assert.Single(repository.LiveTradingEvents, item => item.Action == "StrategyLiveBalance");
        Assert.Equal("Error", liveEvent.Status);
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
            "GTC",
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
            new FakeGammaClient([]),
            tradingClient,
            repository,
            new ExposureSnapshotCache(repository),
            new DefaultPaperTradingEngine(),
            new ServiceControlState());

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OpenOrdersChecked);
        Assert.Equal(1, result.OrdersCanceled);
        Assert.Equal(1, tradingClient.CancelOrderCalls);
        Assert.Equal(LiveOrderStatus.Cancelled, repository.LiveOrders.Single().Status);
    }

    [Fact]
    public async Task LiveProcessorTreatsSuccessfulEmptyCancelResponseAsClosed()
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
            "GTC",
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
        var tradingClient = new CapturingTradingClient
        {
            CancelResult = new LiveOrderCancellationResult(true, [], new Dictionary<string, string>(), "{\"canceled\":[],\"not_canceled\":{}}")
        };
        var processor = new LiveTradingProcessor(
            NullLogger<LiveTradingProcessor>.Instance,
            new LiveTradingOptions(),
            new RiskOptions(),
            new FakeGammaClient([]),
            tradingClient,
            repository,
            new ExposureSnapshotCache(repository),
            new DefaultPaperTradingEngine(),
            new ServiceControlState());

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersCanceled);
        Assert.Equal(LiveOrderStatus.Cancelled, repository.LiveOrders.Single().Status);
    }

    [Fact]
    public async Task LiveMaintenanceWorkerProcessesOpenOrdersIndependently()
    {
        var repository = new TestAppRepository();
        var processor = new CapturingLiveTradingProcessor();
        var worker = new LiveTradingMaintenanceWorker(
            NullLogger<LiveTradingMaintenanceWorker>.Instance,
            new LiveTradingOptions { MaintenancePollIntervalSeconds = 1 },
            processor,
            repository);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(timeout.Token);
        await processor.WaitForProcessCallAsync(timeout.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(processor.ProcessOpenOrderCalls > 0);
    }

    [Fact]
    public async Task LiveProcessorMirrorsShadowLiveFillToPaperOrder()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var correlationId = Guid.NewGuid();
        var signalId = Guid.NewGuid();
        var paperOrderId = Guid.NewGuid();
        var strategyId = StrategyIds.BtcUpDown5mUpSimple;
        await repository.AddPaperOrderAsync(new PaperOrder(
            paperOrderId,
            signalId,
            StrategyIds.BtcUpDown5mUpSimpleCode,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-yes",
            "condition-1",
            "Yes",
            0.40m,
            10m,
            4m,
            now.AddMinutes(-1),
            now.AddMinutes(4),
            StrategyId: strategyId,
            CorrelationId: correlationId,
            ExecutionSource: "paper_live_shadow_test"));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            signalId,
            LiveOrderStatus.Live,
            "0xorder",
            TradeSide.Buy,
            "asset-yes",
            "condition-1",
            "Yes",
            0.99m,
            5m,
            4.95m,
            "FAK",
            now.AddMinutes(-1),
            now.AddMinutes(4),
            now.AddMinutes(-1),
            "live",
            0m,
            5m,
            string.Empty,
            "{}",
            string.Empty,
            now.AddMinutes(-1),
            StrategyId: strategyId,
            CorrelationId: correlationId,
            ExecutionSource: "paper_live_shadow_test",
            PostOnly: false,
            PaperOrderId: paperOrderId));
        var tradingClient = new CapturingTradingClient
        {
            StatusResult = new LiveOrderStatusResult("0xorder", "LIVE", "5000000", "3000000", "0.40", "{}")
        };
        var processor = new LiveTradingProcessor(
            NullLogger<LiveTradingProcessor>.Instance,
            new LiveTradingOptions(),
            new RiskOptions(),
            new FakeGammaClient([]),
            tradingClient,
            repository,
            new ExposureSnapshotCache(repository),
            new DefaultPaperTradingEngine(),
            new ServiceControlState());

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersPolled);
        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(3m, liveOrder.FilledSize);
        Assert.Equal(2m, liveOrder.RemainingSize);
        var paperFill = Assert.Single(repository.PaperFills);
        Assert.Equal(3m, paperFill.SizeShares);
        Assert.Equal(0.40m, paperFill.Price);
        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.PartiallyFilled, paperOrder.Status);
        Assert.Equal(3m, Assert.Single(repository.PaperPositions).SizeShares);
        Assert.Empty(repository.PaperLiveShadowDiscrepancies);
    }

    [Fact]
    public async Task LiveProcessorAllowsShadowFakWhenPaperDecisionExpectsNonPostOnly()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var correlationId = Guid.NewGuid();
        var signalId = Guid.NewGuid();
        var paperOrderId = Guid.NewGuid();
        var strategyId = StrategyIds.BtcUpDown5mUpSimple;
        await repository.AddPaperOrderAsync(new PaperOrder(
            paperOrderId,
            signalId,
            StrategyIds.BtcUpDown5mUpSimpleCode,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.44m,
            10m,
            4.40m,
            now.AddMinutes(-1),
            now.AddMinutes(4),
            StrategyId: strategyId,
            RawDecisionJson: "{\"paper_live_shadow_test\":true,\"post_only\":false}",
            CorrelationId: correlationId,
            ExecutionSource: "paper_live_shadow_test"));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            signalId,
            LiveOrderStatus.Live,
            "0xorder",
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.99m,
            5m,
            4.95m,
            "FAK",
            now.AddMinutes(-1),
            now.AddMinutes(4),
            now.AddMinutes(-1),
            "live",
            0m,
            5m,
            string.Empty,
            "{}",
            string.Empty,
            now.AddMinutes(-1),
            StrategyId: strategyId,
            CorrelationId: correlationId,
            ExecutionSource: "paper_live_shadow_test",
            PostOnly: false,
            PaperOrderId: paperOrderId));
        var tradingClient = new CapturingTradingClient
        {
            StatusResult = new LiveOrderStatusResult("0xorder", "LIVE", "5000000", "3000000", "0.44", "{}")
        };
        var processor = new LiveTradingProcessor(
            NullLogger<LiveTradingProcessor>.Instance,
            new LiveTradingOptions(),
            new RiskOptions(),
            new FakeGammaClient([]),
            tradingClient,
            repository,
            new ExposureSnapshotCache(repository),
            new DefaultPaperTradingEngine(),
            new ServiceControlState());

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersPolled);
        Assert.Empty(repository.PaperLiveShadowDiscrepancies);
        Assert.False(Assert.Single(repository.LiveOrders).PostOnly);
    }

    [Fact]
    public async Task LiveProcessorIgnoresExpectedFakPaperAndLivePriceDifference()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var correlationId = Guid.NewGuid();
        var signalId = Guid.NewGuid();
        var paperOrderId = Guid.NewGuid();
        var strategyId = StrategyIds.BtcUpDown5mUpSimple;
        repository.StrategySettings[strategyId] = StrategyRuntimeSettings.Default(strategyId) with
        {
            LiveStakes = true
        };
        await repository.AddPaperOrderAsync(new PaperOrder(
            paperOrderId,
            signalId,
            StrategyIds.BtcUpDown5mUpSimpleCode,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.400000m,
            10m,
            4m,
            now.AddMinutes(-1),
            now.AddMinutes(4),
            StrategyId: strategyId,
            CorrelationId: correlationId,
            ExecutionSource: "paper_live_shadow_test"));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            signalId,
            LiveOrderStatus.Live,
            "0xorder",
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.99m,
            10m,
            9.90m,
            "FAK",
            now.AddMinutes(-1),
            now.AddMinutes(4),
            now.AddMinutes(-1),
            "live",
            0m,
            10m,
            string.Empty,
            "{}",
            string.Empty,
            now.AddMinutes(-1),
            StrategyId: strategyId,
            CorrelationId: correlationId,
            ExecutionSource: "paper_live_shadow_test",
            PostOnly: false,
            PaperOrderId: paperOrderId));
        var tradingClient = new CapturingTradingClient
        {
            StatusResult = new LiveOrderStatusResult("0xorder", "LIVE", "10000000", "0", "0.400002", "{}")
        };
        var processor = new LiveTradingProcessor(
            NullLogger<LiveTradingProcessor>.Instance,
            new LiveTradingOptions(),
            new RiskOptions(),
            new FakeGammaClient([]),
            tradingClient,
            repository,
            new ExposureSnapshotCache(repository),
            new DefaultPaperTradingEngine(),
            new ServiceControlState());

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersPolled);
        Assert.Equal(0, tradingClient.CancelOrderCalls);
        Assert.True(repository.StrategySettings[strategyId].LiveStakes);
        Assert.Empty(repository.StrategyLiveStakeUpdates);
        Assert.Empty(repository.PaperLiveShadowDiscrepancies);
        Assert.Empty(repository.PaperFills);
        Assert.Equal(LiveOrderStatus.Live, Assert.Single(repository.LiveOrders).Status);
        Assert.DoesNotContain(repository.LiveTradingEvents, item => item.Action == "PaperLiveShadowIncident");
        Assert.DoesNotContain(repository.LiveTradingEvents, item => item.Action == "PaperLiveShadowDiscrepancy");
    }

    [Fact]
    public async Task LiveProcessorRecordsIncidentButKeepsShadowLiveWhenFakWorstPriceIsUnexpected()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var correlationId = Guid.NewGuid();
        var signalId = Guid.NewGuid();
        var paperOrderId = Guid.NewGuid();
        var strategyId = StrategyIds.BtcUpDown5mUpSimple;
        repository.StrategySettings[strategyId] = StrategyRuntimeSettings.Default(strategyId) with
        {
            LiveStakes = true
        };
        await repository.AddPaperOrderAsync(new PaperOrder(
            paperOrderId,
            signalId,
            StrategyIds.BtcUpDown5mUpSimpleCode,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.400000m,
            10m,
            4m,
            now.AddMinutes(-1),
            now.AddMinutes(4),
            StrategyId: strategyId,
            CorrelationId: correlationId,
            ExecutionSource: "paper_live_shadow_test"));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            signalId,
            LiveOrderStatus.Live,
            "0xorder",
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.980000m,
            10m,
            9.8m,
            "FAK",
            now.AddMinutes(-1),
            now.AddMinutes(4),
            now.AddMinutes(-1),
            "live",
            0m,
            10m,
            string.Empty,
            "{}",
            string.Empty,
            now.AddMinutes(-1),
            StrategyId: strategyId,
            CorrelationId: correlationId,
            ExecutionSource: "paper_live_shadow_test",
            PostOnly: false,
            PaperOrderId: paperOrderId));
        var tradingClient = new CapturingTradingClient
        {
            StatusResult = new LiveOrderStatusResult("0xorder", "LIVE", "10000000", "0", "0.98", "{}")
        };
        var processor = new LiveTradingProcessor(
            NullLogger<LiveTradingProcessor>.Instance,
            new LiveTradingOptions(),
            new RiskOptions(),
            new FakeGammaClient([]),
            tradingClient,
            repository,
            new ExposureSnapshotCache(repository),
            new DefaultPaperTradingEngine(),
            new ServiceControlState());

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersPolled);
        Assert.Equal(0, tradingClient.CancelOrderCalls);
        Assert.True(repository.StrategySettings[strategyId].LiveStakes);
        Assert.Empty(repository.StrategyLiveStakeUpdates);
        var discrepancy = Assert.Single(repository.PaperLiveShadowDiscrepancies);
        Assert.Equal(strategyId, discrepancy.StrategyId);
        Assert.Equal("paper_live_shadow_shape_incident", discrepancy.Classification);
        Assert.Equal("warning", discrepancy.Severity);
        Assert.Contains("FAK worst_price unexpected", discrepancy.Details, StringComparison.Ordinal);
        Assert.Empty(repository.PaperFills);
        Assert.Equal(LiveOrderStatus.Live, Assert.Single(repository.LiveOrders).Status);
        Assert.Single(repository.LiveTradingEvents, item => item.Action == "PaperLiveShadowIncident");
    }

    [Fact]
    public async Task LiveProcessorStillDisablesShadowLiveWhenNonFakPaperAndLivePriceDiffer()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var correlationId = Guid.NewGuid();
        var signalId = Guid.NewGuid();
        var paperOrderId = Guid.NewGuid();
        var strategyId = StrategyIds.BtcUpDown5mUpSimple;
        repository.StrategySettings[strategyId] = StrategyRuntimeSettings.Default(strategyId) with
        {
            LiveStakes = true
        };
        await repository.AddPaperOrderAsync(new PaperOrder(
            paperOrderId,
            signalId,
            StrategyIds.BtcUpDown5mUpSimpleCode,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.400000m,
            10m,
            4m,
            now.AddMinutes(-1),
            now.AddMinutes(4),
            StrategyId: strategyId,
            RawDecisionJson: "{\"live_order_type\":\"GTD\"}",
            CorrelationId: correlationId,
            ExecutionSource: "paper_live_shadow_test"));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            signalId,
            LiveOrderStatus.Live,
            "0xorder",
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.400002m,
            10m,
            4.00002m,
            "GTD",
            now.AddMinutes(-1),
            now.AddMinutes(4),
            now.AddMinutes(-1),
            "live",
            0m,
            10m,
            string.Empty,
            "{}",
            string.Empty,
            now.AddMinutes(-1),
            StrategyId: strategyId,
            CorrelationId: correlationId,
            ExecutionSource: "paper_live_shadow_test",
            PostOnly: false,
            PaperOrderId: paperOrderId));
        var tradingClient = new CapturingTradingClient
        {
            StatusResult = new LiveOrderStatusResult("0xorder", "LIVE", "10000000", "0", "0.400002", "{}")
        };
        var processor = new LiveTradingProcessor(
            NullLogger<LiveTradingProcessor>.Instance,
            new LiveTradingOptions(),
            new RiskOptions(),
            new FakeGammaClient([]),
            tradingClient,
            repository,
            new ExposureSnapshotCache(repository),
            new DefaultPaperTradingEngine(),
            new ServiceControlState());

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersPolled);
        Assert.Equal(1, tradingClient.CancelOrderCalls);
        Assert.False(repository.StrategySettings[strategyId].LiveStakes);
        var discrepancy = Assert.Single(repository.PaperLiveShadowDiscrepancies);
        Assert.Equal(strategyId, discrepancy.StrategyId);
        Assert.Equal("paper_live_shadow_shape_mismatch", discrepancy.Classification);
        Assert.Contains("limit_price mismatch", discrepancy.Details, StringComparison.Ordinal);
        Assert.Empty(repository.PaperFills);
        Assert.Equal(LiveOrderStatus.Cancelled, Assert.Single(repository.LiveOrders).Status);
        Assert.Single(repository.LiveTradingEvents, item => item.Action == "PaperLiveShadowDiscrepancy");
    }

    [Fact]
    public async Task LiveProcessorDoesNotSettleShadowFillFromAggregateDataApiPosition()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var correlationId = Guid.NewGuid();
        var signalId = Guid.NewGuid();
        var paperOrderId = Guid.NewGuid();
        var strategyId = StrategyIds.BtcUpDown5mUpSimple;
        await repository.AddPaperOrderAsync(new PaperOrder(
            paperOrderId,
            signalId,
            StrategyIds.BtcUpDown5mUpSimpleCode,
            PaperOrderStatus.Cancelled,
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.29m,
            6.9m,
            2.001m,
            now.AddMinutes(-10),
            now.AddMinutes(-5),
            CancelledAtUtc: now.AddMinutes(-5),
            StrategyId: strategyId,
            CorrelationId: correlationId,
            ExecutionSource: "paper_live_shadow_test"));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            signalId,
            LiveOrderStatus.CancelFailed,
            "0xorder",
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.29m,
            6.9m,
            2.001m,
            "FAK",
            now.AddMinutes(-10),
            now.AddMinutes(-5),
            now.AddMinutes(-10),
            "live",
            0m,
            6.9m,
            string.Empty,
            "{}",
            string.Empty,
            now.AddMinutes(-5),
            StrategyId: strategyId,
            CorrelationId: correlationId,
            ExecutionSource: "paper_live_shadow_test",
            PostOnly: false,
            PaperOrderId: paperOrderId));
        var processor = new LiveTradingProcessor(
            NullLogger<LiveTradingProcessor>.Instance,
            new LiveTradingOptions(),
            new RiskOptions(),
            new FakeGammaClient([]),
            new CapturingTradingClient(),
            repository,
            new ExposureSnapshotCache(repository),
            new DefaultPaperTradingEngine(),
            new ServiceControlState(),
            new FakeDataApiClient(
                currentPositions:
                [
                    Position(
                        Wallet,
                        PolymarketDataApiPositionStatus.Open,
                        "asset-up",
                        "condition-1",
                        "Up",
                        6.9m,
                        6.9m,
                        0.29m)
                ]),
            new PolymarketAuthOptions { FunderAddress = Wallet });

        var result = await processor.ProcessOpenOrdersAsync();

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(1, result.DataApiPositionObservations);
        Assert.Equal(LiveOrderStatus.CancelFailed, liveOrder.Status);
        Assert.Equal("live", liveOrder.ResponseStatus);
        Assert.Equal(0m, liveOrder.FilledSize);
        Assert.Equal(6.9m, liveOrder.RemainingSize);
        Assert.Null(liveOrder.AverageFillPrice);
        Assert.Equal(0m, liveOrder.FilledNotionalUsd);
        Assert.Equal(0m, liveOrder.CostBasisUsd);
        Assert.False(liveOrder.BalanceEffectApplied);
        Assert.Contains("exact per-order fill not applied", liveOrder.ValidationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Cancelled, Assert.Single(repository.PaperOrders).Status);
        Assert.Single(repository.LiveTradingEvents, item => item.Action == "LiveDataApiPositionObservation" && item.Status == "Warning");

        var secondResult = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(0, secondResult.DataApiPositionObservations);
        Assert.Single(repository.LiveTradingEvents, item => item.Action == "LiveDataApiPositionObservation");
    }

    [Fact]
    public async Task LiveProcessorSettlesMatchedWinningOrderAndCapsStrategyBalance()
    {
        var repository = new TestAppRepository();
        repository.StrategySettings[StrategyIds.FollowLeader] = StrategyRuntimeSettings.Default(StrategyIds.FollowLeader) with
        {
            LiveStakes = true,
            LiveAvailableBalance = 100m,
            LiveLostCoeff = 2m
        };
        var now = DateTimeOffset.UtcNow;
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Matched,
            "0xorder",
            TradeSide.Buy,
            "asset-yes",
            "condition-1",
            "Yes",
            0.40m,
            10m,
            4m,
            "GTD",
            now.AddMinutes(-10),
            now.AddMinutes(-5),
            now.AddMinutes(-10),
            "matched",
            10m,
            0m,
            string.Empty,
            "{}",
            string.Empty,
            now.AddMinutes(-5),
            StrategyId: StrategyIds.FollowLeader));
        var processor = new LiveTradingProcessor(
            NullLogger<LiveTradingProcessor>.Instance,
            new LiveTradingOptions(),
            new RiskOptions(),
            new FakeGammaClient([
                TokenMetadata("asset-yes", "Yes", "Yes"),
                TokenMetadata("asset-no", "No", "Yes")
            ]),
            new CapturingTradingClient(),
            repository,
            new ExposureSnapshotCache(repository),
            new DefaultPaperTradingEngine(),
            new ServiceControlState());

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.BalanceSettlementsApplied);
        var order = Assert.Single(repository.LiveOrders);
        Assert.True(order.BalanceEffectApplied);
        Assert.Equal(10m, order.SettlementValueUsd);
        Assert.Equal(6m, order.RealizedPnlUsd);
        Assert.Equal(100m, repository.StrategySettings[StrategyIds.FollowLeader].LiveAvailableBalance);
        Assert.Equal(-1, repository.StrategySettings[StrategyIds.FollowLeader].LiveLostCounter);
        Assert.True(repository.StrategySettings[StrategyIds.FollowLeader].LiveStakes);
    }

    [Fact]
    public async Task LiveProcessorSettlesMatchedLosingOrderAndDisablesStrategyWhenBalanceFallsBelowStake()
    {
        var repository = new TestAppRepository();
        repository.StrategySettings[StrategyIds.FollowLeader] = StrategyRuntimeSettings.Default(StrategyIds.FollowLeader) with
        {
            LiveStakes = true,
            LiveAvailableBalance = 3m,
            LiveStakeAmount = 2.50m,
            LiveLostCoeff = 2m
        };
        var now = DateTimeOffset.UtcNow;
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Matched,
            "0xorder",
            TradeSide.Buy,
            "asset-yes",
            "condition-1",
            "Yes",
            0.40m,
            10m,
            4m,
            "GTD",
            now.AddMinutes(-10),
            now.AddMinutes(-5),
            now.AddMinutes(-10),
            "matched",
            10m,
            0m,
            string.Empty,
            "{}",
            string.Empty,
            now.AddMinutes(-5),
            StrategyId: StrategyIds.FollowLeader));
        var processor = new LiveTradingProcessor(
            NullLogger<LiveTradingProcessor>.Instance,
            new LiveTradingOptions(),
            new RiskOptions(),
            new FakeGammaClient([
                TokenMetadata("asset-yes", "Yes", "No"),
                TokenMetadata("asset-no", "No", "No")
            ]),
            new CapturingTradingClient(),
            repository,
            new ExposureSnapshotCache(repository),
            new DefaultPaperTradingEngine(),
            new ServiceControlState());

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.BalanceSettlementsApplied);
        var order = Assert.Single(repository.LiveOrders);
        Assert.True(order.BalanceEffectApplied);
        Assert.Equal(0m, order.SettlementValueUsd);
        Assert.Equal(-4m, order.RealizedPnlUsd);
        Assert.Equal(0m, repository.StrategySettings[StrategyIds.FollowLeader].LiveAvailableBalance);
        Assert.Equal(1, repository.StrategySettings[StrategyIds.FollowLeader].LiveLostCounter);
        Assert.False(repository.StrategySettings[StrategyIds.FollowLeader].LiveStakes);
        Assert.Single(repository.LiveTradingEvents, item => item.Action == "StrategyLiveBalance");
    }

    private sealed class CapturingLiveTradingProcessor : ILiveTradingProcessor
    {
        private readonly TaskCompletionSource processCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ProcessOpenOrderCalls { get; private set; }

        public Task<LiveTradingProcessingResult> ProcessOpenOrdersAsync(CancellationToken cancellationToken = default)
        {
            ProcessOpenOrderCalls++;
            processCalled.TrySetResult();
            return Task.FromResult(new LiveTradingProcessingResult(0, 0, 0));
        }

        public Task CancelAllOpenOrdersAsync(string source, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public async Task WaitForProcessCallAsync(CancellationToken cancellationToken)
        {
            await processCalled.Task.WaitAsync(cancellationToken);
        }
    }

    private static SignalProcessor CreateProcessor(
        ILeaderTradeCandidateQueue queue,
        TestAppRepository repository,
        CapturingTradingClient tradingClient,
        BotOptions botOptions,
        IPolymarketGeoClient geoClient,
        bool liveStakes = true,
        decimal liveAvailableBalance = 100m,
        bool runPaperInLiveMode = false,
        decimal liveStakeAmount = 1m,
        decimal liveLostCoeff = 1m,
        int liveLostCounter = 0,
        decimal maxOrderNotionalUsd = 1m,
        bool blockOnGeoblockCheckFailure = true)
    {
        var riskOptions = new RiskOptions();
        var paperOptions = new PaperTradingOptions { InitialBankrollUsd = 10_000m, RunInLiveMode = runPaperInLiveMode };
        repository.StrategySettings[StrategyIds.FollowLeader] = StrategyRuntimeSettings.Default(StrategyIds.FollowLeader) with
        {
            LiveStakes = liveStakes,
            LiveAvailableBalance = liveAvailableBalance,
            LiveStakeAmount = liveStakeAmount,
            LiveLostCoeff = liveLostCoeff,
            LiveLostCounter = liveLostCounter
        };
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
            new LiveTradingOptions
            {
                ManualEnableCode = "LIVE_TRADING_ENABLED",
                MaxOrderNotionalUsd = maxOrderNotionalUsd,
                BlockOnGeoblockCheckFailure = blockOnGeoblockCheckFailure
            },
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
            new ExposureSnapshotCache(repository),
            new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance, repository),
            repository);
    }

    private static BotOptions LiveEnabledBot()
    {
        return new BotOptions { Mode = BotMode.Live, EnableLiveTrading = true };
    }

    private static async Task WaitForLiveTradingEventAsync(TestAppRepository repository, string action)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (repository.LiveTradingEvents.Any(item => item.Action == action))
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for live trading event '{action}'.");
    }

    private static LiveTradingProcessor CreateLiveSettlementProcessor(
        TestAppRepository repository,
        LiveTradingOptions liveTradingOptions)
    {
        return new LiveTradingProcessor(
            NullLogger<LiveTradingProcessor>.Instance,
            liveTradingOptions,
            new RiskOptions(),
            new FakeGammaClient([
                TokenMetadata("asset-yes", "Yes", "No"),
                TokenMetadata("asset-no", "No", "No")
            ]),
            new CapturingTradingClient(),
            repository,
            new ExposureSnapshotCache(repository),
            new DefaultPaperTradingEngine(),
            new ServiceControlState());
    }

    private static LiveOrder CreateMatchedLiveOrder(DateTimeOffset createdAtUtc, string orderId)
    {
        return new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Matched,
            orderId,
            TradeSide.Buy,
            "asset-yes",
            "condition-1",
            "Yes",
            0.40m,
            10m,
            4m,
            "GTD",
            createdAtUtc,
            createdAtUtc.AddMinutes(5),
            createdAtUtc,
            "matched",
            10m,
            0m,
            string.Empty,
            "{}",
            string.Empty,
            createdAtUtc,
            StrategyId: StrategyIds.FollowLeader);
    }

    private static LiveOrder CreateOpenLiveOrder(
        DateTimeOffset createdAtUtc,
        string assetId,
        string conditionId,
        string outcome)
    {
        return new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Live,
            "0xopen-" + Guid.NewGuid().ToString("N"),
            TradeSide.Buy,
            assetId,
            conditionId,
            outcome,
            0.50m,
            5m,
            2.50m,
            "GTD",
            createdAtUtc,
            createdAtUtc.AddMinutes(5),
            createdAtUtc,
            "live",
            0m,
            5m,
            string.Empty,
            "{}",
            string.Empty,
            createdAtUtc,
            StrategyId: StrategyIds.FollowLeader);
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
                [new OrderBookLevel(0.74m, 1_000m)],
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

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(string tokenId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PolymarketClobMarketByToken?>(null);
        }
    }

    private sealed class CapturingTradingClient : IPolymarketTradingClient
    {
        public int PlaceCalls { get; private set; }
        public int CancelOrderCalls { get; private set; }
        public ClobV2OrderRequest? LastRequest { get; private set; }
        public LiveOrderPlacementResult PlacementResult { get; init; } = new(true, "0xorder", "live", null, null, null, "{}", "{}");
        public LiveOrderCancellationResult? CancelResult { get; init; }
        public LiveOrderStatusResult? StatusResult { get; init; }

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
            return Task.FromResult(CancelResult ?? new LiveOrderCancellationResult(true, [orderId], new Dictionary<string, string>(), "{}"));
        }

        public Task<LiveOrderCancellationResult> CancelAllOrdersAsync(CancellationToken ct)
        {
            return Task.FromResult(CancelResult ?? new LiveOrderCancellationResult(true, [], new Dictionary<string, string>(), "{}"));
        }

        public Task<LiveOrderStatusResult?> GetLiveOrderStatusAsync(string orderId, CancellationToken ct)
        {
            return Task.FromResult(StatusResult);
        }
    }

    private sealed class FakeDataApiClient(
        IReadOnlyList<PolymarketDataApiPosition>? currentPositions = null,
        IReadOnlyList<PolymarketDataApiPosition>? closedPositions = null) : IPolymarketDataApiClient
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
            return Task.FromResult(currentPositions ?? []);
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
            return Task.FromResult(closedPositions ?? []);
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

    private sealed class ThrowingGeoClient : IPolymarketGeoClient
    {
        public Task<GeoblockStatus> GetGeoblockStatusAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("geoblock endpoint unavailable");
        }
    }

    private sealed class ReadyAuthService : IPolymarketAuthService
    {
        public Task<AuthReadinessStatus> GetReadinessAsync(CancellationToken ct)
        {
            return Task.FromResult(AuthReadinessStatus.ConfiguredButUntested());
        }
    }

    private sealed class FakeGammaClient(IReadOnlyList<PolymarketOnChainTokenMetadata> metadata) : IPolymarketGammaClient
    {
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
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>(
                metadata.Any(item => string.Equals(item.TokenId, tokenId, StringComparison.OrdinalIgnoreCase))
                    ? metadata
                    : []);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(
            string conditionId,
            string requestedTokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>(
                metadata.Any(item => string.Equals(item.ConditionId, conditionId, StringComparison.OrdinalIgnoreCase))
                    ? metadata
                    : []);
        }

        public Task<string?> GetEventCategoryAsync(string eventId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private static PolymarketOnChainTokenMetadata TokenMetadata(
        string tokenId,
        string outcome,
        string winningOutcome)
    {
        return new PolymarketOnChainTokenMetadata(
            tokenId,
            "condition-1",
            "market-1",
            "sample-market",
            "Sample market",
            outcome,
            outcome == "Yes" ? 0 : 1,
            "Politics",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            Active: false,
            Closed: true,
            Archived: false,
            Resolved: true,
            winningOutcome,
            ["asset-yes", "asset-no"],
            ["Yes", "No"],
            LookupSucceeded: true,
            LookupError: null,
            RawJson: "{}",
            LastRefreshedUtc: DateTimeOffset.UtcNow);
    }

    private static PolymarketDataApiPosition Position(
        string wallet,
        PolymarketDataApiPositionStatus status,
        string assetId,
        string conditionId,
        string outcome,
        decimal size,
        decimal totalBought,
        decimal avgPrice)
    {
        return new PolymarketDataApiPosition(
            wallet,
            status,
            assetId,
            conditionId,
            status == PolymarketDataApiPositionStatus.Open ? size : null,
            avgPrice,
            totalBought * avgPrice,
            size * avgPrice,
            0m,
            0m,
            totalBought,
            0m,
            0m,
            avgPrice,
            status == PolymarketDataApiPositionStatus.Closed ? DateTimeOffset.UtcNow : null,
            "Bitcoin Up or Down",
            "btc-updown-5m-test",
            null,
            null,
            null,
            "Crypto",
            outcome,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            false,
            "{}");
    }
}
