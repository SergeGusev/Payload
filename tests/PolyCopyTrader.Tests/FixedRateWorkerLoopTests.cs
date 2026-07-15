using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class FixedRateWorkerLoopTests
{
    [Fact]
    public async Task PeriodicTimer_MultipleElapsedTicks_AreCoalesced()
    {
        var timeProvider = new PulsedTimeProvider();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500), timeProvider);

        // The due-entry processor is running here, so no timer wait is active.
        timeProvider.Pulse(5);

        var firstWait = timer.WaitForNextTickAsync().AsTask();
        Assert.True(firstWait.IsCompletedSuccessfully);
        Assert.True(await firstWait);
        var secondWait = timer.WaitForNextTickAsync().AsTask();
        Assert.False(secondWait.IsCompleted);

        timeProvider.Pulse();

        Assert.True(await secondWait);
    }

    [Fact]
    public async Task RunAsync_LongRunningProcessorCycle_CoalescesPendingTickWithoutOverlap()
    {
        using var tickSource = new BufferedTickSource();
        using var stoppingTokenSource = new CancellationTokenSource();
        var firstCycleStarted = NewCompletionSource();
        var releaseFirstCycle = NewCompletionSource();
        var secondCycleStarted = NewCompletionSource();
        var cycleCount = 0;
        var activeCycles = 0;
        var maximumActiveCycles = 0;

        var loopTask = FixedRateWorkerLoop.RunAsync(
            tickSource,
            async cancellationToken =>
            {
                var active = Interlocked.Increment(ref activeCycles);
                UpdateMaximum(ref maximumActiveCycles, active);
                var cycle = Interlocked.Increment(ref cycleCount);
                try
                {
                    if (cycle == 1)
                    {
                        firstCycleStarted.TrySetResult();
                        await releaseFirstCycle.Task.WaitAsync(cancellationToken);
                    }
                    else if (cycle == 2)
                    {
                        secondCycleStarted.TrySetResult();
                        stoppingTokenSource.Cancel();
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref activeCycles);
                }
            },
            stoppingTokenSource.Token);

        await firstCycleStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Volatile.Read(ref cycleCount));
        Assert.Equal(0, Volatile.Read(ref tickSource.WaitCount));

        // Simulate the PeriodicTimer's single coalesced tick becoming pending while the
        // processor cycle is still running.
        tickSource.Pulse();
        releaseFirstCycle.TrySetResult();

        await secondCycleStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await loopTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, Volatile.Read(ref cycleCount));
        Assert.Equal(1, Volatile.Read(ref maximumActiveCycles));
        Assert.Equal(1, Volatile.Read(ref tickSource.WaitCount));
        Assert.True(tickSource.LastWaitCompletedSynchronously);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeStart_DoesNotInvokeProcessor()
    {
        using var tickSource = new BufferedTickSource();
        using var stoppingTokenSource = new CancellationTokenSource();
        stoppingTokenSource.Cancel();
        var cycleCount = 0;

        await FixedRateWorkerLoop.RunAsync(
            tickSource,
            _ =>
            {
                Interlocked.Increment(ref cycleCount);
                return Task.CompletedTask;
            },
            stoppingTokenSource.Token);

        Assert.Equal(0, cycleCount);
        Assert.Equal(0, tickSource.WaitCount);
    }

    [Theory]
    [InlineData("BtcUpDown5mDueEntryPaperStrategyWorker.cs", "ProcessDueEntriesAsync")]
    [InlineData("BtcUpDown5mDiffCounterPaperStrategyWorker.cs", "ProcessDiffCounterFastDueEntriesAsync")]
    [InlineData("BtcUpDown5mPreviousResultPaperStrategyWorker.cs", "ProcessPreviousResultFastDueEntriesAsync")]
    public void DueEntryWorker_UsesFixedRateLoopAndPreservesProcessorDispatch(
        string workerFileName,
        string processorMethodName)
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "Strategies",
            workerFileName);

        Assert.Contains("FixedRateWorkerLoop.RunAsync(interval", source, StringComparison.Ordinal);
        Assert.Contains($"processor.{processorMethodName}(cancellationToken)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(interval", source, StringComparison.Ordinal);
    }

    private static TaskCompletionSource NewCompletionSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

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

    private static string ReadRepositorySource(params string[] segments)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            Path.Combine(segments)));
        return File.ReadAllText(path);
    }

    private sealed class BufferedTickSource : IFixedRateTickSource
    {
        private readonly SemaphoreSlim pendingTicks = new(0);

        public int WaitCount;

        public bool LastWaitCompletedSynchronously { get; private set; }

        public void Pulse()
        {
            pendingTicks.Release();
        }

        public async ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref WaitCount);
            var waitTask = pendingTicks.WaitAsync(cancellationToken);
            LastWaitCompletedSynchronously = waitTask.IsCompletedSuccessfully;
            await waitTask;
            return true;
        }

        public void Dispose()
        {
            pendingTicks.Dispose();
        }
    }

    private sealed class PulsedTimeProvider : TimeProvider
    {
        private PulsedTimer? timer;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _ = dueTime;
            _ = period;
            timer = new PulsedTimer(callback, state);
            return timer;
        }

        public void Pulse(int count = 1)
        {
            if (timer is null)
            {
                throw new InvalidOperationException("PeriodicTimer has not created its underlying timer.");
            }

            for (var index = 0; index < count; index++)
            {
                timer.Pulse();
            }
        }
    }

    private sealed class PulsedTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _ = dueTime;
            _ = period;
            return !disposed;
        }

        public void Pulse()
        {
            if (!disposed)
            {
                callback(state);
            }
        }

        public void Dispose()
        {
            disposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
