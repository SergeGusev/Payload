using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Tests;

public sealed class MarketDataWebSocketReconnectBackoffTests
{
    [Fact]
    public async Task DelayAndAdvanceAsync_RepeatedUnsuccessfulConnectionsEscalateAndCap()
    {
        var backoff = CreateBackoff();
        var observedDelays = new List<TimeSpan>();

        for (var attempt = 0; attempt < 7; attempt++)
        {
            await backoff.DelayAndAdvanceAsync(
                (delay, _) =>
                {
                    observedDelays.Add(delay);
                    return Task.CompletedTask;
                },
                CancellationToken.None);
        }

        Assert.Equal(
            new[] { 2d, 4d, 8d, 16d, 32d, 60d, 60d },
            observedDelays.Select(delay => delay.TotalSeconds));
        Assert.Equal(TimeSpan.FromSeconds(60), backoff.CurrentDelay);
    }

    [Fact]
    public async Task ProcessTextMessageAndResetBackoffAsync_SuccessfulFrameResetsEscalatedDelay()
    {
        var backoff = await CreateEscalatedBackoffAsync();
        var receivedAtUtc = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var processed = false;

        await MarketDataWebSocketShardRunner.ProcessTextMessageAndResetBackoffAsync(
            (component, message, timestamp, cancellationToken) =>
            {
                Assert.Equal("market-ws-test", component);
                Assert.Equal("payload", message);
                Assert.Equal(receivedAtUtc, timestamp);
                Assert.False(cancellationToken.IsCancellationRequested);
                processed = true;
                return Task.FromResult(true);
            },
            "market-ws-test",
            "payload",
            receivedAtUtc,
            backoff,
            CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(TimeSpan.FromSeconds(2), backoff.CurrentDelay);
    }

    [Fact]
    public async Task DelayAndAdvanceAsync_ConnectionFlapWithoutProcessedFrameDoesNotReset()
    {
        var backoff = await CreateEscalatedBackoffAsync();
        TimeSpan? observedDelay = null;

        await backoff.DelayAndAdvanceAsync(
            (delay, _) =>
            {
                observedDelay = delay;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(8), observedDelay);
        Assert.Equal(TimeSpan.FromSeconds(16), backoff.CurrentDelay);
    }

    [Fact]
    public async Task ProcessTextMessageAndResetBackoffAsync_CanceledFrameDoesNotReset()
    {
        var backoff = await CreateEscalatedBackoffAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            MarketDataWebSocketShardRunner.ProcessTextMessageAndResetBackoffAsync(
                (_, _, _, cancellationToken) => Task.FromCanceled<bool>(cancellationToken),
                "market-ws-test",
                "payload",
                DateTimeOffset.UtcNow,
                backoff,
                cancellation.Token));

        Assert.Equal(TimeSpan.FromSeconds(8), backoff.CurrentDelay);
    }

    [Fact]
    public async Task ProcessTextMessageAndResetBackoffAsync_FaultedFrameDoesNotReset()
    {
        var backoff = await CreateEscalatedBackoffAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MarketDataWebSocketShardRunner.ProcessTextMessageAndResetBackoffAsync(
                (_, _, _, _) => Task.FromException<bool>(new InvalidOperationException("processing failed")),
                "market-ws-test",
                "payload",
                DateTimeOffset.UtcNow,
                backoff,
                CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(8), backoff.CurrentDelay);
    }

    [Fact]
    public async Task DelayAndAdvanceAsync_CanceledDelayDoesNotAdvance()
    {
        var backoff = await CreateEscalatedBackoffAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            backoff.DelayAndAdvanceAsync(
                (_, cancellationToken) => Task.FromCanceled(cancellationToken),
                cancellation.Token));

        Assert.Equal(TimeSpan.FromSeconds(8), backoff.CurrentDelay);
    }

    private static MarketDataWebSocketReconnectBackoff CreateBackoff()
    {
        return new MarketDataWebSocketReconnectBackoff(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(60));
    }

    private static async Task<MarketDataWebSocketReconnectBackoff> CreateEscalatedBackoffAsync()
    {
        var backoff = CreateBackoff();
        await backoff.DelayAndAdvanceAsync((_, _) => Task.CompletedTask, CancellationToken.None);
        await backoff.DelayAndAdvanceAsync((_, _) => Task.CompletedTask, CancellationToken.None);
        return backoff;
    }
}
