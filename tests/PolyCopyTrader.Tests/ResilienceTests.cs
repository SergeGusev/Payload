using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service;
using PolyCopyTrader.Service.Analytics;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.Diagnostics;
using PolyCopyTrader.Service.LiveTrading;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Service.Signals;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Strategy;
using PolyCopyTrader.Storage;

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
            "0xleader",
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
            new PaperTradingOptions(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OpenOrdersChecked);
        Assert.Equal(0, result.OrdersFilled);
    }

    [Fact]
    public async Task PaperTradingProcessor_SkipsMissingOrderBookWhenMarkingPosition()
    {
        var repository = new TestAppRepository();
        var position = new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            10m,
            0.50m,
            5m,
            0m,
            DateTimeOffset.UtcNow,
            "0xleader");
        await repository.UpsertPaperPositionAsync(position);
        var processor = new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            new MissingOrderBookClobClient(),
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new MarketDataWebSocketOptions(),
            new PaperTradingOptions(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(0, result.OpenOrdersChecked);
        Assert.Equal(0, result.PositionsUpdated);
        Assert.Empty(repository.ApiErrors);
        Assert.Equal(position, Assert.Single(repository.PaperPositions));
    }

    [Fact]
    public async Task PaperTradingProcessor_SurvivesOrderBookTimeoutWhenMarkingPosition()
    {
        var repository = new TestAppRepository();
        var position = new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            10m,
            0.50m,
            5m,
            0m,
            DateTimeOffset.UtcNow,
            "0xleader");
        await repository.UpsertPaperPositionAsync(position);
        var processor = new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            new TimeoutClobClient(),
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new MarketDataWebSocketOptions(),
            new PaperTradingOptions(),
            new ExposureSnapshotCache(repository),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(0, result.OpenOrdersChecked);
        Assert.Equal(0, result.PositionsUpdated);
        Assert.Contains(repository.ApiErrors, error => error.Operation == "UpdatePositionMarkTimeout");
        Assert.Equal(position, Assert.Single(repository.PaperPositions));
    }

    [Fact]
    public async Task PaperTradingProcessor_DoesNotRestorePositionSettledWhileMarkPriceIsLoading()
    {
        var repository = new TestAppRepository();
        var initialUpdatedAtUtc = new DateTimeOffset(2026, 7, 15, 7, 10, 0, TimeSpan.Zero);
        var position = new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            10m,
            0.50m,
            5m,
            0m,
            initialUpdatedAtUtc,
            "0xleader");
        await repository.UpsertPaperPositionAsync(position);
        var exposureCache = new ExposureSnapshotCache(repository);
        await exposureCache.RefreshAsync();
        var clobClient = new BlockingOrderBookClobClient();
        var processor = new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            clobClient,
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new MarketDataWebSocketOptions(),
            new PaperTradingOptions(),
            exposureCache,
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var processing = processor.ProcessOpenOrdersAsync();
        await clobClient.OrderBookRequested.WaitAsync(TimeSpan.FromSeconds(5));

        var settledAtUtc = initialUpdatedAtUtc.AddSeconds(1);
        var settledPosition = position with
        {
            SizeShares = 0m,
            AveragePrice = 0m,
            EstimatedValueUsd = 0m,
            UnrealizedPnlUsd = 0m,
            UpdatedAtUtc = settledAtUtc
        };
        var settlement = new PaperPositionSettlement(
            Guid.NewGuid(),
            position.CopiedTraderWallet,
            position.AssetId,
            position.ConditionId,
            position.Outcome,
            position.AssetId,
            position.Outcome,
            "Crypto",
            position.SizeShares,
            position.AveragePrice,
            position.SizeShares * position.AveragePrice,
            position.SizeShares,
            position.SizeShares - position.SizeShares * position.AveragePrice,
            true,
            "TestResolution",
            settledAtUtc,
            settledAtUtc);
        await repository.PersistPaperPositionSettlementBatchAsync(
            [new PaperPositionSettlementWrite(settlement, settledPosition)]);
        exposureCache.ApplyPaperPosition(settledPosition);

        clobClient.Complete(new OrderBookSnapshot(
            position.AssetId,
            [new OrderBookLevel(0.75m, 100m)],
            [new OrderBookLevel(0.76m, 100m)],
            settledAtUtc,
            position.ConditionId));
        var result = await processing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, result.PositionsUpdated);
        Assert.Equal(1, repository.TryUpdatePaperPositionMarkCalls);
        Assert.Equal(settledPosition, Assert.Single(repository.PaperPositions));
        Assert.Single(repository.PaperPositionSettlements);
        Assert.Null(exposureCache.GetPaperPosition(position.CopiedTraderWallet, position.AssetId));
    }

    [Fact]
    public async Task PaperTradingProcessor_UpdatesMarkWhenExpectedPositionIsStillCurrent()
    {
        var repository = new TestAppRepository();
        var initialUpdatedAtUtc = new DateTimeOffset(2026, 7, 15, 7, 10, 0, TimeSpan.Zero);
        var position = new PaperPosition(
            "asset-1",
            "condition-1",
            "Yes",
            10m,
            0.50m,
            5m,
            0m,
            initialUpdatedAtUtc,
            "0xleader",
            FeeUsd: 0.10m,
            FeeAccountingStatus: "Calculated",
            NetUnrealizedPnlUsd: -0.10m);
        await repository.UpsertPaperPositionAsync(position);
        var exposureCache = new ExposureSnapshotCache(repository);
        await exposureCache.RefreshAsync();
        var clobClient = new BlockingOrderBookClobClient();
        clobClient.Complete(new OrderBookSnapshot(
            position.AssetId,
            [new OrderBookLevel(0.75m, 100m)],
            [new OrderBookLevel(0.76m, 100m)],
            initialUpdatedAtUtc.AddSeconds(1),
            position.ConditionId));
        var processor = new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            clobClient,
            new MarketDataCache(new MarketDataWebSocketOptions()),
            new MarketDataWebSocketOptions(),
            new PaperTradingOptions(),
            exposureCache,
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.PositionsUpdated);
        Assert.Equal(1, repository.TryUpdatePaperPositionMarkCalls);
        var updatedPosition = Assert.Single(repository.PaperPositions);
        Assert.Equal(position.AssetId, updatedPosition.AssetId);
        Assert.Equal(position.ConditionId, updatedPosition.ConditionId);
        Assert.Equal(position.Outcome, updatedPosition.Outcome);
        Assert.Equal(position.SizeShares, updatedPosition.SizeShares);
        Assert.Equal(position.AveragePrice, updatedPosition.AveragePrice);
        Assert.Equal(7.50m, updatedPosition.EstimatedValueUsd);
        Assert.Equal(2.50m, updatedPosition.UnrealizedPnlUsd);
        Assert.Equal(2.40m, updatedPosition.NetUnrealizedPnlUsd);
        Assert.True(updatedPosition.UpdatedAtUtc > position.UpdatedAtUtc);
        Assert.Equal(updatedPosition, exposureCache.GetPaperPosition(position.CopiedTraderWallet, position.AssetId));
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

    [Fact]
    public async Task MarketDataWebSocketService_RetriesSupervisorCycleAfterAssetProviderFailure()
    {
        var options = new MarketDataWebSocketOptions
        {
            SubscriptionRefreshSeconds = 1,
            WatchdogIntervalSeconds = 1,
            StatusPersistIntervalSeconds = 1
        };
        var assetProvider = new ThrowOnceRelevantMarketAssetProvider();
        var cache = new MarketDataCache(options);
        var service = new MarketDataWebSocketService(
            NullLogger<MarketDataWebSocketService>.Instance,
            NullLoggerFactory.Instance,
            new BotOptions { UseWebSockets = true },
            options,
            new PolymarketOptions(),
            assetProvider,
            new ActiveMarketAssetSubscriptionRegistry(),
            new NoOpBtcOrderBookLagDiagnosticService(),
            cache,
            new ExposureSnapshotCache(new NoOpAppRepository()),
            new NoOpMarketDataSideEffectQueue(),
            new NoOpAppRepository());

        await service.StartAsync(CancellationToken.None);
        try
        {
            var completed = await Task.WhenAny(assetProvider.SecondCall, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Same(assetProvider.SecondCall, completed);
            Assert.True(assetProvider.Calls >= 2);
        }
        finally
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await service.StopAsync(stopCts.Token);
        }
    }

    [Fact]
    public async Task BotWorker_HeartbeatPersistenceFailure_DoesNotPauseSubsystems()
    {
        var repository = new TestAppRepository { ThrowOnUpsertServiceHeartbeat = true };
        var controlState = new ServiceControlState();
        var service = new BotWorker(
            NullLogger<BotWorker>.Instance,
            new BotOptions { Mode = BotMode.Paper, PollIntervalSeconds = 60 },
            repository,
            new NoOpWatchlistScanner(),
            new NoOpSignalProcessor(),
            controlState);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var completed = await Task.WhenAny(
                repository.ServiceHeartbeatAttempt.Task,
                Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Same(repository.ServiceHeartbeatAttempt.Task, completed);
            await WaitUntilAsync(
                () => controlState.Snapshot.CurrentLoop.Contains("Heartbeat persistence failed", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));

            var snapshot = controlState.Snapshot;
            Assert.Equal(ServiceRunState.Running, snapshot.RunState);
            Assert.False(snapshot.ScanningPaused);
            Assert.False(snapshot.PaperTradingPaused);
            Assert.False(snapshot.LiveTradingPaused);
            Assert.False(snapshot.KillSwitchActive);
        }
        finally
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await service.StopAsync(stopCts.Token);
        }
    }

    [Fact]
    public async Task BotWorker_HeartbeatIncludesLatestDatabaseScanTelemetry()
    {
        var repository = new TestAppRepository();
        var controlState = new ServiceControlState();
        var telemetryState = new DatabaseScanTelemetryState();
        telemetryState.RecordCopiedPerformance(new PaperCopiedTraderPerformanceRefreshResult(
            LockAcquired: true,
            WalletsSeeded: 0,
            WalletsProcessed: 1,
            PerformanceRowsWritten: 1,
            QueueRemaining: 0,
            ReconciliationCycleCompleted: false,
            PaperPositionsSeedSequentialScans: 1,
            PaperPositionsSeedSequentialTuplesRead: 1_852_305,
            PaperPositionsAggregationSequentialScans: 0,
            PaperPositionsAggregationSequentialTuplesRead: 0));
        telemetryState.RecordDashboardReconciliation(new DashboardProjectionReconciliationResult(
            Reconciled: true,
            StrategyId: Guid.NewGuid(),
            StrategyCode: "test_strategy",
            Duration: TimeSpan.FromMilliseconds(25),
            ValuesChanged: false,
            Error: null,
            PaperPositionsBuildSequentialScans: 0,
            PaperPositionsBuildSequentialTuplesRead: 0));
        telemetryState.RecordCopiedPerformance(new PaperCopiedTraderPerformanceRefreshResult(
            LockAcquired: true,
            WalletsSeeded: 0,
            WalletsProcessed: 1,
            PerformanceRowsWritten: 1,
            QueueRemaining: 0,
            ReconciliationCycleCompleted: false,
            PaperPositionsSeedSequentialScans: 0,
            PaperPositionsSeedSequentialTuplesRead: 0,
            PaperPositionsAggregationSequentialScans: 0,
            PaperPositionsAggregationSequentialTuplesRead: 0));
        var service = new BotWorker(
            NullLogger<BotWorker>.Instance,
            new BotOptions { Mode = BotMode.Paper, PollIntervalSeconds = 60 },
            repository,
            new NoOpWatchlistScanner(),
            new NoOpSignalProcessor(),
            controlState,
            telemetryState);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var completed = await Task.WhenAny(
                repository.ServiceHeartbeatAttempt.Task,
                Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Same(repository.ServiceHeartbeatAttempt.Task, completed);
            var heartbeat = await repository.ServiceHeartbeatAttempt.Task;
            Assert.Contains("DBScanTelemetry", heartbeat.CurrentLoop, StringComparison.Ordinal);
            Assert.Contains(
                "Seed(last=0/0,total=1/1852305,lastPositive=",
                heartbeat.CurrentLoop,
                StringComparison.Ordinal);
            Assert.Contains(
                "Aggregate(last=0/0,total=0/0,lastPositive=none)",
                heartbeat.CurrentLoop,
                StringComparison.Ordinal);
            Assert.Contains(
                "Build(last=0/0,total=0/0,lastPositive=none)",
                heartbeat.CurrentLoop,
                StringComparison.Ordinal);
            Assert.Matches(
                @"CopiedPerformance@\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z",
                heartbeat.CurrentLoop);
        }
        finally
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await service.StopAsync(stopCts.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.True(condition(), "Condition was not met before the timeout.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
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
            new PolymarketAuthOptions(),
            paperOptions,
            new LiveTradingOptions(),
            Watchlist(),
            queue,
            clobClient,
            new FakeGeoClient(),
            new FakeTradingClient(),
            new FakeAuthService(),
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

    private sealed class FakeTradingClient : IPolymarketTradingClient
    {
        public Task<ClobV2DryRunOrderResult> PrepareDryRunOrderAsync(ClobV2OrderRequest request, CancellationToken ct)
        {
            throw new InvalidOperationException("Paper-mode resilience tests should not create dry-run orders.");
        }

        public Task<LiveOrderPlacementResult> PlaceLiveOrderAsync(ClobV2OrderRequest request, CancellationToken ct)
        {
            throw new InvalidOperationException("Paper-mode resilience tests should not create live orders.");
        }

        public Task<LiveOrderCancellationResult> CancelOrderAsync(string orderId, CancellationToken ct)
        {
            throw new InvalidOperationException("Paper-mode resilience tests should not cancel live orders.");
        }

        public Task<LiveOrderCancellationResult> CancelAllOrdersAsync(CancellationToken ct)
        {
            throw new InvalidOperationException("Paper-mode resilience tests should not cancel live orders.");
        }

        public Task<LiveOrderStatusResult?> GetLiveOrderStatusAsync(string orderId, CancellationToken ct)
        {
            throw new InvalidOperationException("Paper-mode resilience tests should not poll live orders.");
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

    private sealed class ThrowOnceRelevantMarketAssetProvider : IRelevantMarketAssetProvider
    {
        private readonly TaskCompletionSource<bool> secondCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public Task SecondCall => secondCall.Task;

        public Task<IReadOnlyCollection<string>> GetRelevantAssetIdsAsync(CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                throw new InvalidOperationException("simulated database recovery");
            }

            secondCall.TrySetResult(true);
            return Task.FromResult<IReadOnlyCollection<string>>([]);
        }
    }

    private sealed class NoOpBtcOrderBookLagDiagnosticService : IBtcOrderBookLagDiagnosticService
    {
        public void RecordBinanceTrade(BtcUsdReferencePricePoint point)
        {
        }

        public void RecordPolymarketTopOfBook(MarketDataUpdate update, DateTimeOffset receivedAtUtc)
        {
        }
    }

    private sealed class NoOpMarketDataSideEffectQueue : IMarketDataSideEffectQueue
    {
        public MarketDataSideEffectEnqueueOutcome EnqueueUpdate(
            string component,
            MarketDataUpdate update,
            ActiveMarketAssetSnapshot? activeMarketSnapshot,
            DateTimeOffset receivedAtUtc,
            IReadOnlySet<Guid>? eligiblePaperOrderIds)
        {
            return MarketDataSideEffectEnqueueOutcome.Enqueued;
        }

        public MarketDataSideEffectEnqueueOutcome EnqueueFrameDiagnostic(
            MarketWebSocketFrameDiagnostic diagnostic,
            bool important)
        {
            return MarketDataSideEffectEnqueueOutcome.Enqueued;
        }

        public MarketDataSideEffectEnqueueOutcome EnqueueApiError(ApiError apiError)
        {
            return MarketDataSideEffectEnqueueOutcome.Enqueued;
        }

        public MarketDataSideEffectQueueMetrics GetMetrics()
        {
            return new MarketDataSideEffectQueueMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
    }

    private sealed class NoOpWatchlistScanner : IWatchlistScanner
    {
        public Task<ScannerStatusSnapshot> ScanOnceAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScannerStatusSnapshot(
                "NoOpWatchlistScanner",
                DateTimeOffset.UtcNow,
                null,
                null,
                0,
                0,
                0,
                "OK",
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class NoOpSignalProcessor : ISignalProcessor
    {
        public Task<SignalProcessingResult> ProcessQueuedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SignalProcessingResult(0, 0, 0, 0));
        }
    }

    private sealed class NoOpPaperTradingProcessor : IPaperTradingProcessor
    {
        public Task<PaperTradingProcessingResult> ProcessOpenOrdersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaperTradingProcessingResult(0, 0, 0, 0));
        }
    }

    private sealed class NoOpLiveTradingProcessor : ILiveTradingProcessor
    {
        public Task<LiveTradingProcessingResult> ProcessOpenOrdersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LiveTradingProcessingResult(0, 0, 0));
        }

        public Task CancelAllOpenOrdersAsync(string source, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
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

    private sealed class ThrowingDataApiClient(string message) : IPolymarketDataApiClient
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

        public Task<IReadOnlyList<LeaderTrade>> GetMarketTradesAsync(
            string conditionId,
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

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(string tokenId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PolymarketClobMarketByToken?>(null);
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

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(string tokenId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("market by token unavailable");
        }
    }

    private sealed class TimeoutClobClient : IPolymarketClobPublicClient
    {
        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing.");
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            throw new TaskCanceledException("server time timed out");
        }

        public Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default)
        {
            throw new TaskCanceledException("midpoint timed out");
        }

        public Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default)
        {
            throw new TaskCanceledException("spread timed out");
        }

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(string tokenId, CancellationToken cancellationToken = default)
        {
            throw new TaskCanceledException("market by token timed out");
        }
    }

    private sealed class MissingOrderBookClobClient : IPolymarketClobPublicClient
    {
        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            throw new PolymarketApiException(
                "PolymarketClobPublicClient",
                "GetOrderBook",
                """GetOrderBook failed with HTTP 404 Not Found. Body: {"error":"No orderbook exists for the requested token id"}""");
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

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(string tokenId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("market by token unavailable");
        }
    }

    private sealed class BlockingOrderBookClobClient : IPolymarketClobPublicClient
    {
        private readonly TaskCompletionSource<bool> orderBookRequested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OrderBookSnapshot?> orderBook =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OrderBookRequested => orderBookRequested.Task;

        public void Complete(OrderBookSnapshot snapshot)
        {
            orderBook.TrySetResult(snapshot);
        }

        public Task<OrderBookSnapshot?> GetOrderBookAsync(
            string assetId,
            CancellationToken cancellationToken = default)
        {
            orderBookRequested.TrySetResult(true);
            return orderBook.Task.WaitAsync(cancellationToken);
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

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(
            string tokenId,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("market by token unavailable");
        }
    }
}
