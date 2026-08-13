using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Diagnostics;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class MarketDataWebSocketFrameProcessingTests
{
    private const string Component = "market-ws-test";
    private const string ValidMarketUpdateJson =
        "{\"event_type\":\"last_trade_price\",\"asset_id\":\"asset-1\",\"market\":\"condition-1\",\"price\":\"0.51\",\"size\":\"2\",\"side\":\"BUY\",\"timestamp\":\"1752580800000\"}";

    [Fact]
    public async Task ProcessTextMessageAndResetBackoffAsync_MalformedPayloadDoesNotReset()
    {
        var queue = new ControlledSideEffectQueue(MarketDataSideEffectEnqueueOutcome.Enqueued);
        var service = CreateService(queue);
        var backoff = await CreateEscalatedBackoffAsync();

        await MarketDataWebSocketShardRunner.ProcessTextMessageAndResetBackoffAsync(
            service.ProcessTextMessageAsync,
            Component,
            "{not-json",
            DateTimeOffset.UtcNow,
            backoff,
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(8), backoff.CurrentDelay);
        Assert.Equal(0, queue.UpdateCalls);
    }

    [Theory]
    [InlineData("PONG")]
    [InlineData("{\"status\":\"ok\"}")]
    public async Task ProcessTextMessageAndResetBackoffAsync_ZeroUpdatePayloadDoesNotReset(string payload)
    {
        var queue = new ControlledSideEffectQueue(MarketDataSideEffectEnqueueOutcome.Enqueued);
        var service = CreateService(queue);
        var backoff = await CreateEscalatedBackoffAsync();

        await MarketDataWebSocketShardRunner.ProcessTextMessageAndResetBackoffAsync(
            service.ProcessTextMessageAsync,
            Component,
            payload,
            DateTimeOffset.UtcNow,
            backoff,
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(8), backoff.CurrentDelay);
        Assert.Equal(0, queue.UpdateCalls);
    }

    [Theory]
    [InlineData(MarketDataSideEffectEnqueueOutcome.Dropped)]
    [InlineData(MarketDataSideEffectEnqueueOutcome.Rejected)]
    public async Task ProcessTextMessageAndResetBackoffAsync_UnacceptedUpdateDoesNotReset(
        MarketDataSideEffectEnqueueOutcome outcome)
    {
        var queue = new ControlledSideEffectQueue(outcome);
        var service = CreateService(queue);
        var backoff = await CreateEscalatedBackoffAsync();

        await MarketDataWebSocketShardRunner.ProcessTextMessageAndResetBackoffAsync(
            service.ProcessTextMessageAsync,
            Component,
            ValidMarketUpdateJson,
            DateTimeOffset.UtcNow,
            backoff,
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(8), backoff.CurrentDelay);
        Assert.Equal(1, queue.UpdateCalls);
    }

    [Fact]
    public async Task ProcessTextMessageAndResetBackoffAsync_UpdateDispatchFailureDoesNotReset()
    {
        var queue = new ControlledSideEffectQueue(
            MarketDataSideEffectEnqueueOutcome.Enqueued,
            throwOnUpdate: true);
        var service = CreateService(queue);
        var backoff = await CreateEscalatedBackoffAsync();

        await MarketDataWebSocketShardRunner.ProcessTextMessageAndResetBackoffAsync(
            service.ProcessTextMessageAsync,
            Component,
            ValidMarketUpdateJson,
            DateTimeOffset.UtcNow,
            backoff,
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(8), backoff.CurrentDelay);
        Assert.Equal(1, queue.UpdateCalls);
    }

    [Theory]
    [InlineData(MarketDataSideEffectEnqueueOutcome.Dropped)]
    [InlineData(MarketDataSideEffectEnqueueOutcome.Rejected)]
    public async Task ProcessTextMessageAsync_UnacceptedUpdatePoisonsOnlyMatchingAssetAndWindow(
        MarketDataSideEffectEnqueueOutcome outcome)
    {
        var handoff = new MakerGtdPaperPlacementHandoff();
        var queue = new ControlledSideEffectQueue(outcome);
        var receivedAtUtc = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var order = MakerOrder(receivedAtUtc);
        var repository = new TestAppRepository();
        repository.PaperOrders.Add(order);
        var exposureCache = new ExposureSnapshotCache(repository, handoff);
        await exposureCache.GetSnapshotAsync();
        var service = CreateService(queue, handoff, exposureCache, repository);
        await using var receiptAdmission = await handoff.EnterMarketDataReceiptAsync();

        Assert.False(await service.ProcessTextMessageAsync(
            Component,
            ValidMarketUpdateJson,
            receivedAtUtc,
            CancellationToken.None));

        Assert.Contains(
            order.Id,
            Assert.IsAssignableFrom<IReadOnlySet<Guid>>(queue.LastEligiblePaperOrderIds));
        Assert.True(handoff.TryGetMarketDataFailure(
            order.Id,
            "asset-1",
            "condition-1",
            receivedAtUtc.AddSeconds(-1),
            receivedAtUtc.AddSeconds(1),
            out var failure));
        Assert.Equal(
            MakerGtdPaperExecutionContract.MarketDataEnqueueFailureCode,
            Assert.IsType<MakerGtdPaperMarketDataFailure>(failure).FailureCode);
        Assert.False(handoff.TryGetMarketDataFailure(
            Guid.NewGuid(),
            "asset-2",
            "condition-1",
            receivedAtUtc.AddSeconds(-1),
            receivedAtUtc.AddSeconds(1),
            out _));
    }

    [Fact]
    public async Task ProcessTextMessageAsync_QueueThrowRecordsDispatchFailure()
    {
        var handoff = new MakerGtdPaperPlacementHandoff();
        var queue = new ControlledSideEffectQueue(
            MarketDataSideEffectEnqueueOutcome.Enqueued,
            throwOnUpdate: true);
        var receivedAtUtc = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var order = MakerOrder(receivedAtUtc);
        var repository = new TestAppRepository();
        repository.PaperOrders.Add(order);
        var exposureCache = new ExposureSnapshotCache(repository, handoff);
        await exposureCache.GetSnapshotAsync();
        var service = CreateService(queue, handoff, exposureCache, repository);
        await using var receiptAdmission = await handoff.EnterMarketDataReceiptAsync();

        Assert.False(await service.ProcessTextMessageAsync(
            Component,
            ValidMarketUpdateJson,
            receivedAtUtc,
            CancellationToken.None));

        Assert.Contains(
            order.Id,
            Assert.IsAssignableFrom<IReadOnlySet<Guid>>(queue.LastEligiblePaperOrderIds));
        Assert.True(handoff.TryGetMarketDataFailure(
            order.Id,
            "asset-1",
            "condition-1",
            receivedAtUtc.AddSeconds(-1),
            receivedAtUtc.AddSeconds(1),
            out var failure));
        Assert.Equal(
            MakerGtdPaperExecutionContract.MarketDataDispatchFailureCode,
            Assert.IsType<MakerGtdPaperMarketDataFailure>(failure).FailureCode);
    }

    [Fact]
    public async Task ProcessTextMessageAsync_ParseFailureInsideReceiptPoisonsAllMatchingLifetimes()
    {
        var handoff = new MakerGtdPaperPlacementHandoff();
        var service = CreateService(
            new ControlledSideEffectQueue(MarketDataSideEffectEnqueueOutcome.Enqueued),
            handoff);
        var receivedAtUtc = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        await using var receiptAdmission = await handoff.EnterMarketDataReceiptAsync();

        Assert.False(await service.ProcessTextMessageAsync(
            Component,
            "{not-json",
            receivedAtUtc,
            CancellationToken.None));

        Assert.True(handoff.TryGetMarketDataFailure(
            Guid.NewGuid(),
            "any-asset",
            "any-condition",
            receivedAtUtc.AddSeconds(-1),
            receivedAtUtc.AddSeconds(1),
            out var failure));
        Assert.Equal(
            MakerGtdPaperExecutionContract.MarketDataParseFailureCode,
            Assert.IsType<MakerGtdPaperMarketDataFailure>(failure).FailureCode);
    }

    [Theory]
    [InlineData(MarketDataSideEffectEnqueueOutcome.Enqueued)]
    [InlineData(MarketDataSideEffectEnqueueOutcome.Coalesced)]
    public async Task ProcessTextMessageAndResetBackoffAsync_AcceptedUpdateResets(
        MarketDataSideEffectEnqueueOutcome outcome)
    {
        var queue = new ControlledSideEffectQueue(outcome);
        var service = CreateService(queue);
        var backoff = await CreateEscalatedBackoffAsync();

        await MarketDataWebSocketShardRunner.ProcessTextMessageAndResetBackoffAsync(
            service.ProcessTextMessageAsync,
            Component,
            ValidMarketUpdateJson,
            DateTimeOffset.UtcNow,
            backoff,
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(2), backoff.CurrentDelay);
        Assert.Equal(1, queue.UpdateCalls);
    }

    [Fact]
    public async Task ProcessTextMessageAsync_PropagatesFrameReceiptIntoTimestampEvidence()
    {
        var queue = new ControlledSideEffectQueue(MarketDataSideEffectEnqueueOutcome.Enqueued);
        var service = CreateService(queue);
        var receivedAtUtc = new DateTimeOffset(2026, 8, 9, 12, 15, 0, TimeSpan.Zero);
        const string missingTimestampJson =
            "{\"event_type\":\"last_trade_price\",\"asset_id\":\"asset-1\",\"market\":\"condition-1\",\"price\":\"0.49\",\"size\":\"2\",\"side\":\"SELL\"}";

        var accepted = await service.ProcessTextMessageAsync(
            Component,
            missingTimestampJson,
            receivedAtUtc,
            CancellationToken.None);

        Assert.True(accepted);
        Assert.Equal(receivedAtUtc, queue.LastReceivedAtUtc);
        var update = Assert.IsType<MarketDataUpdate>(queue.LastUpdate);
        Assert.Equal(receivedAtUtc, update.ReceivedAtUtc);
        Assert.Equal(receivedAtUtc, update.TimestampUtc);
        Assert.Null(update.SourceTimestampUtc);
        Assert.Equal(MarketDataTimestampQuality.ReceiveTimeFallback, update.TimestampQuality);
        Assert.False(update.HasAuthoritativeSourceTimestamp);
    }

    [Fact]
    public async Task ProcessTextMessageAsync_S1ToActivationAdmissionCannotCaptureEmptyMakerEligibility()
    {
        var queue = new ControlledSideEffectQueue(MarketDataSideEffectEnqueueOutcome.Enqueued);
        var handoff = new MakerGtdPaperPlacementHandoff();
        var service = CreateService(queue, handoff);
        var paperOrderId = Guid.NewGuid();
        var admission = await handoff.EnterPlacementAdmissionAsync("asset-1");

        var dispatchTask = service.ProcessTextMessageAsync(
            Component,
            ValidMarketUpdateJson,
            new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.False(dispatchTask.IsCompleted);
        Assert.Equal(0, queue.UpdateCalls);

        admission.ActivatePendingOrder(
            paperOrderId,
            MakerGtdPaperExecutionContract.ExecutionSource);
        await admission.DisposeAsync();

        Assert.True(await dispatchTask);
        Assert.Equal(1, queue.UpdateCalls);
        Assert.Contains(paperOrderId, Assert.IsAssignableFrom<IReadOnlySet<Guid>>(queue.LastEligiblePaperOrderIds));

        var publicationWait = handoff.WaitForPublicationAsync(
            queue.LastEligiblePaperOrderIds,
            CancellationToken.None);
        Assert.False(publicationWait.IsCompleted);

        handoff.MarkPublished(paperOrderId);
        await publicationWait;
    }

    [Fact]
    public async Task ReconnectPolicy_InvalidFrameKeepsEscalationAndAcceptedFrameResetsNextDelay()
    {
        var queue = new ControlledSideEffectQueue(MarketDataSideEffectEnqueueOutcome.Enqueued);
        var service = CreateService(queue);
        var backoff = await CreateEscalatedBackoffAsync();

        await MarketDataWebSocketShardRunner.ProcessTextMessageAndResetBackoffAsync(
            service.ProcessTextMessageAsync,
            Component,
            "PONG",
            DateTimeOffset.UtcNow,
            backoff,
            CancellationToken.None);
        var invalidFrameDelay = await ObserveAndAdvanceAsync(backoff);

        await MarketDataWebSocketShardRunner.ProcessTextMessageAndResetBackoffAsync(
            service.ProcessTextMessageAsync,
            Component,
            ValidMarketUpdateJson,
            DateTimeOffset.UtcNow,
            backoff,
            CancellationToken.None);
        var acceptedFrameDelay = await ObserveAndAdvanceAsync(backoff);

        Assert.Equal(TimeSpan.FromSeconds(8), invalidFrameDelay);
        Assert.Equal(TimeSpan.FromSeconds(2), acceptedFrameDelay);
        Assert.Equal(TimeSpan.FromSeconds(4), backoff.CurrentDelay);
    }

    private static MarketDataWebSocketService CreateService(
        IMarketDataSideEffectQueue sideEffectQueue,
        IMakerGtdPaperPlacementHandoff? makerGtdPaperPlacementHandoff = null,
        IExposureSnapshotCache? exposureSnapshotCache = null,
        IAppRepository? repository = null)
    {
        var options = new MarketDataWebSocketOptions();
        repository ??= new NoOpAppRepository();
        return new MarketDataWebSocketService(
            NullLogger<MarketDataWebSocketService>.Instance,
            NullLoggerFactory.Instance,
            new BotOptions { UseWebSockets = true },
            options,
            new PolymarketOptions(),
            new EmptyRelevantMarketAssetProvider(),
            new ActiveMarketAssetSubscriptionRegistry(),
            new NoOpBtcOrderBookLagDiagnosticService(),
            new MarketDataCache(options),
            exposureSnapshotCache ?? new ExposureSnapshotCache(repository, makerGtdPaperPlacementHandoff),
            sideEffectQueue,
            repository,
            makerGtdPaperPlacementHandoff);
    }

    private static PaperOrder MakerOrder(DateTimeOffset receivedAtUtc)
    {
        return new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "strategy:maker-gtd",
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-1",
            "condition-1",
            "Up",
            Price: 0.50m,
            SizeShares: 1m,
            NotionalUsd: 0.50m,
            CreatedAtUtc: receivedAtUtc.AddMinutes(-1),
            ExpiresAtUtc: receivedAtUtc.AddMinutes(1),
            ExecutionSource: MakerGtdPaperExecutionContract.ExecutionSource);
    }

    private static async Task<MarketDataWebSocketReconnectBackoff> CreateEscalatedBackoffAsync()
    {
        var backoff = new MarketDataWebSocketReconnectBackoff(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(60));
        await ObserveAndAdvanceAsync(backoff);
        await ObserveAndAdvanceAsync(backoff);
        return backoff;
    }

    private static async Task<TimeSpan> ObserveAndAdvanceAsync(MarketDataWebSocketReconnectBackoff backoff)
    {
        TimeSpan? observedDelay = null;
        await backoff.DelayAndAdvanceAsync(
            (delay, _) =>
            {
                observedDelay = delay;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        return Assert.IsType<TimeSpan>(observedDelay);
    }

    private sealed class EmptyRelevantMarketAssetProvider : IRelevantMarketAssetProvider
    {
        public Task<IReadOnlyCollection<string>> GetRelevantAssetIdsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<string>>([]);
        }
    }

    private sealed class ControlledSideEffectQueue(
        MarketDataSideEffectEnqueueOutcome updateOutcome,
        bool throwOnUpdate = false) : IMarketDataSideEffectQueue
    {
        public int UpdateCalls { get; private set; }

        public MarketDataUpdate? LastUpdate { get; private set; }

        public DateTimeOffset? LastReceivedAtUtc { get; private set; }

        public IReadOnlySet<Guid>? LastEligiblePaperOrderIds { get; private set; }

        public MarketDataSideEffectEnqueueOutcome EnqueueUpdate(
            string component,
            MarketDataUpdate update,
            ActiveMarketAssetSnapshot? activeMarketSnapshot,
            DateTimeOffset receivedAtUtc,
            IReadOnlySet<Guid>? eligiblePaperOrderIds)
        {
            UpdateCalls++;
            LastUpdate = update;
            LastReceivedAtUtc = receivedAtUtc;
            LastEligiblePaperOrderIds = eligiblePaperOrderIds;
            if (throwOnUpdate)
            {
                throw new InvalidOperationException("simulated update queue failure");
            }

            return updateOutcome;
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
}
