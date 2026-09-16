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
    public async Task FrameHandoff_ReceivesLaterFramesWhileMakerReceiptAdmissionIsHeld_ThenDrainsCloseInFifoOrder()
    {
        var firstReceivedAtUtc = new DateTimeOffset(2026, 9, 16, 6, 0, 0, TimeSpan.Zero);
        var secondReceivedAtUtc = firstReceivedAtUtc.AddMilliseconds(1);
        var closeFrame = new MarketWebSocketCloseFrame(
            System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
            "test close",
            firstReceivedAtUtc.AddMilliseconds(2));
        var received = new Queue<MarketWebSocketReceivedMessage>(
        [
            new(new MarketWebSocketDispatchFrame("first", firstReceivedAtUtc), null),
            new(new MarketWebSocketDispatchFrame("second", secondReceivedAtUtc), null),
            new(null, closeFrame)
        ]);
        var receiveCalls = 0;
        var observedReceipts = new List<MarketWebSocketDispatchFrame>();
        var dispatched = new List<MarketWebSocketDispatchFrame>();
        var handoff = new MakerGtdPaperPlacementHandoff();
        var placementAdmission = await handoff.EnterPlacementAdmissionAsync("asset-1");

        var run = MarketDataWebSocketFrameHandoff.RunAsync(
            capacity: 2,
            saturationThreshold: TimeSpan.FromHours(1),
            _ =>
            {
                Interlocked.Increment(ref receiveCalls);
                return Task.FromResult(received.Dequeue());
            },
            async (frame, cancellationToken) =>
            {
                await using (await handoff.EnterMarketDataReceiptAsync(cancellationToken))
                {
                    await using (await handoff.EnterMarketDataAdmissionAsync("asset-1", cancellationToken))
                    {
                        dispatched.Add(frame);
                    }
                }
            },
            observedReceipts.Add,
            _ => throw new InvalidOperationException("The channel should not saturate in this test."),
            () => throw new InvalidOperationException("The consumer should not fail in this test."),
            CancellationToken.None);

        await WaitUntilAsync(() => Volatile.Read(ref receiveCalls) == 3, TimeSpan.FromSeconds(3));
        Assert.False(run.IsCompleted);
        Assert.Empty(dispatched);
        Assert.Equal([firstReceivedAtUtc, secondReceivedAtUtc], observedReceipts.Select(frame => frame.ReceivedAtUtc));

        await placementAdmission.DisposeAsync();
        var observedClose = await run;

        Assert.Same(closeFrame, observedClose);
        Assert.Equal(["first", "second"], dispatched.Select(frame => frame.Text));
        Assert.Equal([firstReceivedAtUtc, secondReceivedAtUtc], dispatched.Select(frame => frame.ReceivedAtUtc));
    }

    [Fact]
    public async Task FrameHandoff_BoundedSaturationWaitsAndReportsWithoutDroppingFrames()
    {
        var received = new Queue<MarketWebSocketReceivedMessage>(
        [
            Frame("first", 0),
            Frame("second", 1),
            Frame("third", 2),
            new(null, new MarketWebSocketCloseFrame(null, "done", DateTimeOffset.UtcNow))
        ]);
        var receiveCalls = 0;
        var dispatched = new List<string>();
        var firstDispatchEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstDispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saturationDurations = new List<TimeSpan>();

        var run = MarketDataWebSocketFrameHandoff.RunAsync(
            capacity: 1,
            saturationThreshold: TimeSpan.FromMilliseconds(5),
            _ =>
            {
                Interlocked.Increment(ref receiveCalls);
                return Task.FromResult(received.Dequeue());
            },
            async (frame, _) =>
            {
                dispatched.Add(frame.Text);
                if (frame.Text == "first")
                {
                    firstDispatchEntered.TrySetResult();
                    await releaseFirstDispatch.Task;
                }
            },
            _ => { },
            saturationDurations.Add,
            () => throw new InvalidOperationException("The consumer should not fail in this test."),
            CancellationToken.None);

        await firstDispatchEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => Volatile.Read(ref receiveCalls) >= 3, TimeSpan.FromSeconds(3));
        await Task.Delay(20);
        Assert.False(run.IsCompleted);

        releaseFirstDispatch.TrySetResult();
        await run;

        Assert.Equal(["first", "second", "third"], dispatched);
        Assert.NotEmpty(saturationDurations);
        Assert.All(saturationDurations, duration => Assert.True(duration >= TimeSpan.FromMilliseconds(5)));
    }

    [Fact]
    public async Task FrameHandoff_ConsumerFailureCancelsProducerAndPropagatesOriginalFailure()
    {
        var receiveCalls = 0;
        var producerCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumerFailureCallbacks = 0;

        var run = MarketDataWebSocketFrameHandoff.RunAsync(
            capacity: 1,
            saturationThreshold: TimeSpan.FromHours(1),
            async cancellationToken =>
            {
                if (Interlocked.Increment(ref receiveCalls) == 1)
                {
                    return Frame("first", 0);
                }

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("Unreachable test path.");
                }
                catch (OperationCanceledException)
                {
                    producerCanceled.TrySetResult();
                    throw;
                }
            },
            (_, _) => Task.FromException(new InvalidOperationException("simulated dispatch failure")),
            _ => { },
            _ => { },
            () => Interlocked.Increment(ref consumerFailureCallbacks),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => run);

        Assert.Equal("simulated dispatch failure", exception.Message);
        await producerCanceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, consumerFailureCallbacks);
    }

    [Fact]
    public async Task FrameHandoff_ServiceCancellationPropagatesWithoutConsumerFailureCallback()
    {
        using var cancellation = new CancellationTokenSource();
        var receiveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumerFailureCallbacks = 0;
        var run = MarketDataWebSocketFrameHandoff.RunAsync(
            capacity: 1,
            saturationThreshold: TimeSpan.FromHours(1),
            async cancellationToken =>
            {
                receiveStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable test path.");
            },
            (_, _) => Task.CompletedTask,
            _ => { },
            _ => { },
            () => Interlocked.Increment(ref consumerFailureCallbacks),
            cancellation.Token);

        await receiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, consumerFailureCallbacks);
    }

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

    private static MarketWebSocketReceivedMessage Frame(string text, int millisecondOffset)
    {
        return new MarketWebSocketReceivedMessage(
            new MarketWebSocketDispatchFrame(
                text,
                new DateTimeOffset(2026, 9, 16, 6, 0, 0, TimeSpan.Zero).AddMilliseconds(millisecondOffset)),
            null);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected condition was not reached before the test timeout.");
            }

            await Task.Delay(10);
        }
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
