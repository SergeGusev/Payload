using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Analytics;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class DashboardStrategyProjectionReconciliationWorkerTests
{
    [Fact]
    public async Task Worker_UsesConfiguredIntervalAndDoesNotOverlapReconciliations()
    {
        var timeProvider = new PulsedTimeProvider();
        var firstCallStarted = NewCompletionSource();
        var releaseFirstCall = NewCompletionSource();
        var secondCallStarted = NewCompletionSource();
        var calls = 0;
        var activeCalls = 0;
        var maximumActiveCalls = 0;
        var repository = new TestDashboardProjectionRepository
        {
            ReconcileAsync = async cancellationToken =>
            {
                var active = Interlocked.Increment(ref activeCalls);
                UpdateMaximum(ref maximumActiveCalls, active);
                var call = Interlocked.Increment(ref calls);
                try
                {
                    if (call == 1)
                    {
                        firstCallStarted.TrySetResult();
                        await releaseFirstCall.Task.WaitAsync(cancellationToken);
                    }
                    else if (call == 2)
                    {
                        secondCallStarted.TrySetResult();
                    }

                    return new DashboardProjectionReconciliationResult(
                        false,
                        null,
                        null,
                        TimeSpan.Zero,
                        false,
                        null);
                }
                finally
                {
                    Interlocked.Decrement(ref activeCalls);
                }
            }
        };
        using var worker = new DashboardStrategyProjectionReconciliationWorker(
            NullLogger<DashboardStrategyProjectionReconciliationWorker>.Instance,
            repository,
            new DashboardOptions
            {
                ProjectionReconciliationIntervalSeconds = 5
            },
            timeProvider);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(timeout.Token);
        await timeProvider.WaitForTimerCountAsync(1, timeout.Token);
        Assert.Equal(0, Volatile.Read(ref calls));
        Assert.Equal(TimeSpan.FromSeconds(5), timeProvider.LatestDueTime);

        timeProvider.PulseLatest();
        await firstCallStarted.Task.WaitAsync(timeout.Token);
        timeProvider.PulseLatest();
        Assert.Equal(1, Volatile.Read(ref calls));

        releaseFirstCall.TrySetResult();
        await timeProvider.WaitForTimerCountAsync(2, timeout.Token);
        timeProvider.PulseLatest();
        await secondCallStarted.Task.WaitAsync(timeout.Token);

        Assert.Equal(2, Volatile.Read(ref calls));
        Assert.Equal(1, Volatile.Read(ref maximumActiveCalls));

        await worker.StopAsync(timeout.Token);
    }

    private static TaskCompletionSource NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        var current = Volatile.Read(ref maximum);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref maximum, candidate, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private sealed class TestDashboardProjectionRepository : IDashboardProjectionRepository
    {
        public required Func<CancellationToken, Task<DashboardProjectionReconciliationResult>> ReconcileAsync
        {
            get;
            init;
        }

        public Task<DashboardProjectionControlState> GetControlStateAsync(
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(new DashboardProjectionControlState(
                true,
                DashboardProjectionVersions.Current,
                "Running",
                null,
                null,
                null,
                null,
                null,
                null,
                null));
        }

        public Task<DashboardProjectionReconciliationResult> ReconcileNextStrategyAsync(
            CancellationToken cancellationToken = default) =>
            ReconcileAsync(cancellationToken);

        public Task<DashboardProjectionBootstrapResult> BootstrapAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DashboardProjectionBatchResult> ApplyPendingEventsAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DashboardProjectionExpiryResult> ExpireRecentFactsAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordFailureAsync(
            string operation,
            string error,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PulsedTimeProvider : TimeProvider
    {
        private readonly object sync = new();
        private TaskCompletionSource timerCreated = NewCompletionSource();
        private PulsedTimer? latestTimer;
        private TimeSpan latestDueTime;
        private int timerCount;

        public TimeSpan LatestDueTime
        {
            get
            {
                lock (sync)
                {
                    return latestDueTime;
                }
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _ = period;
            var timer = new PulsedTimer(callback, state);
            TaskCompletionSource signal;
            lock (sync)
            {
                latestTimer = timer;
                latestDueTime = dueTime;
                timerCount++;
                signal = timerCreated;
                timerCreated = NewCompletionSource();
            }

            signal.TrySetResult();
            return timer;
        }

        public async Task WaitForTimerCountAsync(int expectedCount, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task waitTask;
                lock (sync)
                {
                    if (timerCount >= expectedCount)
                    {
                        return;
                    }

                    waitTask = timerCreated.Task;
                }

                await waitTask.WaitAsync(cancellationToken);
            }
        }

        public void PulseLatest()
        {
            PulsedTimer timer;
            lock (sync)
            {
                timer = latestTimer ??
                    throw new InvalidOperationException("The worker has not scheduled a delay.");
            }

            timer.Pulse();
        }
    }

    private sealed class PulsedTimer(TimerCallback callback, object? state) : ITimer
    {
        private int active = 1;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _ = dueTime;
            _ = period;
            Volatile.Write(ref active, 1);
            return true;
        }

        public void Pulse()
        {
            if (Interlocked.Exchange(ref active, 0) == 1)
            {
                callback(state);
            }
        }

        public void Dispose()
        {
            Volatile.Write(ref active, 0);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
