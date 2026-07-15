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

    private static MarketDataWebSocketService CreateService(IMarketDataSideEffectQueue sideEffectQueue)
    {
        var options = new MarketDataWebSocketOptions();
        var repository = new NoOpAppRepository();
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
            new ExposureSnapshotCache(repository),
            sideEffectQueue,
            repository);
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

        public MarketDataSideEffectEnqueueOutcome EnqueueUpdate(
            string component,
            MarketDataUpdate update,
            ActiveMarketAssetSnapshot? activeMarketSnapshot,
            DateTimeOffset receivedAtUtc,
            IReadOnlySet<Guid>? eligiblePaperOrderIds)
        {
            UpdateCalls++;
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
