using Microsoft.Extensions.Logging;
using PolyCopyTrader.Service.ExternalPrices;

namespace PolyCopyTrader.Tests;

public sealed class CryptoReferencePriceRetentionWorkerTests
{
    [Fact]
    public async Task Run_RotatesExactAssets_UsesUtc48HourCutoffAndBoundedBatches()
    {
        using var cancellation = new CancellationTokenSource();
        var now = new DateTimeOffset(2026, 9, 2, 15, 0, 0, TimeSpan.FromHours(3));
        var calls = new List<(string Asset, DateTimeOffset Cutoff, int Size)>();
        var delays = new List<TimeSpan>();
        var logger = new RecordingLogger();
        var repository = new TestAppRepository
        {
            CryptoReferencePriceCleanup = (asset, cutoff, size, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                calls.Add((asset, cutoff, size));
                return Task.FromResult(calls.Count == 1 ? 1000 : 0);
            }
        };
        using var worker = new CryptoReferencePriceRetentionWorker(logger, repository);

        await worker.RunAsync(() => now, (delay, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            delays.Add(delay);
            now += delay;
            if (delays.Count == 4)
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        }, cancellation.Token);

        Assert.Equal(new[] { "BTC", "ETH", "SOL", "BTC" }, calls.Select(call => call.Asset));
        Assert.All(calls, call => Assert.Equal(1000, call.Size));
        var firstCutoff = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < calls.Count; index++)
        {
            Assert.Equal(TimeSpan.Zero, calls[index].Cutoff.Offset);
            Assert.Equal(firstCutoff.AddSeconds(index * 10), calls[index].Cutoff);
        }

        Assert.All(delays, delay => Assert.Equal(TimeSpan.FromSeconds(10), delay));
        var batches = logger.Entries.Where(entry => entry.Message.Contains("batch completed.")).ToArray();
        Assert.Equal(4, batches.Length);
        Assert.Contains("Deleted=1000", batches[0].Message);
        Assert.All(batches, entry =>
        {
            Assert.Contains("SampledBeforeUtc=", entry.Message);
            Assert.Contains("DurationMilliseconds=", entry.Message);
        });
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Run_DoesNotDelayOrStartNextBatchUntilCurrentBatchCompletes()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var delays = 0;
        var repository = new TestAppRepository
        {
            CryptoReferencePriceCleanup = (_, _, _, _) =>
            {
                Interlocked.Increment(ref calls);
                entered.SetResult();
                return release.Task;
            }
        };
        using var worker = new CryptoReferencePriceRetentionWorker(new RecordingLogger(), repository);
        var running = worker.RunAsync(() => DateTimeOffset.UtcNow, (delay, _) =>
        {
            Assert.True(release.Task.IsCompleted);
            Assert.Equal(TimeSpan.FromSeconds(10), delay);
            Interlocked.Increment(ref delays);
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.Equal(0, Volatile.Read(ref delays));
        Assert.False(running.IsCompleted);
        release.SetResult(17);
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
        Assert.Equal(1, delays);
    }

    [Fact]
    public async Task Run_ErrorWaits60Seconds_ThenContinuesRotation()
    {
        using var cancellation = new CancellationTokenSource();
        var assets = new List<string>();
        var delays = new List<TimeSpan>();
        var logger = new RecordingLogger();
        var repository = new TestAppRepository
        {
            CryptoReferencePriceCleanup = (asset, _, _, _) =>
            {
                assets.Add(asset);
                return assets.Count == 1
                    ? Task.FromException<int>(new InvalidOperationException("test database failure"))
                    : Task.FromResult(1);
            }
        };
        using var worker = new CryptoReferencePriceRetentionWorker(logger, repository);
        await worker.RunAsync(() => DateTimeOffset.UtcNow, (delay, _) =>
        {
            delays.Add(delay);
            if (delays.Count == 2)
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        }, cancellation.Token);

        Assert.Equal(new[] { "BTC", "ETH" }, assets);
        Assert.Equal(new[] { TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10) }, delays);
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("Asset=BTC", error.Message);
        Assert.Contains("SampledBeforeUtc=", error.Message);
        Assert.Contains("RetryDelaySeconds=60", error.Message);
    }

    [Fact]
    public async Task Run_CancellationDuringBatch_StopsWithoutErrorOrNextBatch()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var logger = new RecordingLogger();
        var repository = new TestAppRepository
        {
            CryptoReferencePriceCleanup = (_, _, _, token) =>
            {
                calls++;
                cancellation.Cancel();
                return Task.FromCanceled<int>(token);
            }
        };
        using var worker = new CryptoReferencePriceRetentionWorker(logger, repository);
        await worker.RunAsync(() => DateTimeOffset.UtcNow,
            (_, _) => throw new InvalidOperationException("No delay after cancellation expected."), cancellation.Token);
        Assert.Equal(1, calls);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Run_CancellationDuringDelay_StopsWithoutErrorOrNextBatch()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var logger = new RecordingLogger();
        var repository = new TestAppRepository
        {
            CryptoReferencePriceCleanup = (_, _, _, _) => Task.FromResult(++calls)
        };
        using var worker = new CryptoReferencePriceRetentionWorker(logger, repository);
        await worker.RunAsync(() => DateTimeOffset.UtcNow, (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(token);
        }, cancellation.Token);
        Assert.Equal(1, calls);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Run_AlreadyCancelled_DoesNotTouchRepository()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var worker = new CryptoReferencePriceRetentionWorker(new RecordingLogger(), new TestAppRepository());
        await worker.RunAsync(() => throw new InvalidOperationException("Clock should not be read."),
            (_, _) => throw new InvalidOperationException("No delay expected."), cancellation.Token);
    }

    private sealed class RecordingLogger : ILogger<CryptoReferencePriceRetentionWorker>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
