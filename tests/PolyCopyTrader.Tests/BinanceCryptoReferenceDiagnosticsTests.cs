using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.ExternalPrices;

namespace PolyCopyTrader.Tests;

public sealed class BinanceCryptoReferenceDiagnosticsTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LatestTradeUpdatesBeforeSampleGate_AndExactlyFiveSecondsIsFresh()
    {
        using var h = new Harness();
        h.Send("ETH", 3100m);
        h.Advance(TimeSpan.FromSeconds(1));
        h.Send("ETH", 3101.25m);
        h.Advance(TimeSpan.FromSeconds(5));

        var point = await h.Service.GetPriceAsync(" eth ");
        Assert.Equal(3101.25m, point.PriceUsd);
        Assert.Equal(Epoch.AddSeconds(1), point.FetchedAtUtc);
        Assert.Equal(Epoch, point.SourceUpdatedAtUtc);
        Assert.Equal(BinanceCryptoTradeParser.SourceName, point.Source);
        Assert.Equal(1, h.Service.GetSnapshot("ETH").SampleCount);
        Assert.Empty(h.Logger.Diagnostics);

        h.Advance(TimeSpan.FromMilliseconds(1));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH"));
        Assert.Equal($"Binance ETH/USDT trade stream price is stale. AgeSeconds={5.001:0.###}; StaleAfterSeconds=5.", error.Message);
        var stale = Assert.Single(h.Logger.Diagnostics);
        Assert.Equal(LogLevel.Warning, stale.Level);
        Assert.Equal("stale", stale.Kind);
    }

    [Fact]
    public async Task NeverSeenAndCancellationPreserveOriginalBehaviorWithoutRecovery()
    {
        using var h = new Harness();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync(" sol "));
        Assert.Equal("Binance SOL/USDT trade stream has not received a price yet.", error.Message);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Service.GetPriceAsync("SOL", cancellation.Token));
        h.Send("SOL");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Service.GetPriceAsync("SOL", cancellation.Token));
        Assert.Empty(h.Logger.Diagnostics);
        Assert.False(h.Snapshot("SOL").IsStale);
    }

    [Fact]
    public async Task InvalidIgnoredAndOtherAssetMessagesDoNotRecoverStaleAsset()
    {
        using var h = new Harness();
        h.Send("ETH");
        h.Advance(TimeSpan.FromSeconds(6));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH"));
        h.Service.ProcessMessage("not-json"u8.ToArray());
        h.Service.ProcessMessage("{\"s\":\"ETHUSDT\",\"p\":\"0\"}"u8.ToArray());
        h.Send("BTC");
        h.Send("SOL");
        Assert.Single(h.Logger.Diagnostics);
        Assert.True(h.Snapshot("ETH").IsStale);
        Assert.Equal(2, h.Snapshot("ETH").ParserRejectedMessages);
        Assert.Equal(1, h.Snapshot("ETH").IgnoredMessages);
        Assert.Equal(1, h.Snapshot("ETH").AcceptedMessages);

        h.Send("ETH", 3102m);
        var recovery = h.Logger.Diagnostics.Last();
        Assert.Equal("recovery", recovery.Kind);
        Assert.Equal("ETH", recovery.Asset);
        Assert.Equal(LogLevel.Information, recovery.Level);
        Assert.False(h.Snapshot("ETH").IsStale);
        Assert.Equal(3102m, (await h.Service.GetPriceAsync("ETH")).PriceUsd);
    }

    [Fact]
    public async Task AcceptedMessageThatAgesDuringSampleLogDoesNotDeclareRecovery()
    {
        using var h = new Harness(sampleIntervalSeconds: 1);
        h.Send("ETH");
        h.Advance(TimeSpan.FromSeconds(6));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH"));
        h.Logger.OnSample = () => h.Advance(TimeSpan.FromSeconds(6));
        h.Send("ETH", 3105m);
        Assert.Single(h.Logger.Diagnostics);
        Assert.True(h.Snapshot("ETH").IsStale);
        Assert.Equal(6000, h.Snapshot("ETH").LastSampleLogDurationMs);
        Assert.Equal(6000, h.Snapshot("ETH").LastPublicationLockHoldMs);

        h.Logger.OnSample = null;
        h.Send("ETH", 3106m);
        Assert.Equal(2, h.Logger.Diagnostics.Length);
        Assert.Equal("recovery", h.Logger.Diagnostics.Last().Kind);
        Assert.Equal(3106m, (await h.Service.GetPriceAsync("ETH")).PriceUsd);
    }

    [Fact]
    public async Task NewDiagnosticEventsAreOutsidePriceCacheLock()
    {
        using var h = new Harness();
        h.Send("ETH");
        h.Logger.OnDiagnostic = () =>
        {
            // A different thread must be able to acquire the business cache lock.
            Task.Run(() => h.Service.GetSnapshot("ETH")).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        };
        h.Advance(TimeSpan.FromSeconds(6));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH"));
        h.Send("ETH");
        Assert.Equal(2, h.Logger.Diagnostics.Length);
        Assert.Equal(2, h.Logger.CompletedDiagnosticCallbacks);
    }

    [Fact]
    public async Task NewLoggerFailureCannotReplaceStaleExceptionOrFreshResult()
    {
        using var h = new Harness();
        h.Send("ETH");
        h.Advance(TimeSpan.FromSeconds(6));
        h.Logger.ThrowDiagnostics = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH"));
        Assert.Contains("price is stale", error.Message);
        h.Send("ETH", 3200m);
        Assert.Equal(3200m, (await h.Service.GetPriceAsync("ETH")).PriceUsd);
        Assert.False(h.Snapshot("ETH").IsStale);
        Assert.True(h.Logger.DiagnosticAttempts >= 2);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DiagnosticClockFailureDoesNotAffectBusinessPriceOrException(bool failTimestamp, bool failUtc)
    {
        using var h = new Harness();
        h.DiagnosticClock.ThrowTimestamp = failTimestamp;
        h.DiagnosticClock.ThrowUtc = failUtc;
        h.Send("ETH", 3111m);
        Assert.Equal(3111m, (await h.Service.GetPriceAsync("ETH")).PriceUsd);
        h.Advance(TimeSpan.FromSeconds(6));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH"));
        Assert.Contains("price is stale", error.Message);
        h.Service.ProcessMessage("bad-json"u8.ToArray());
        h.Send("ETH", 3112m);
        Assert.Equal(3112m, (await h.Service.GetPriceAsync("ETH")).PriceUsd);
    }

    [Fact]
    public async Task SnapshotTimingFailureCannotReplaceBusinessResult()
    {
        using var h = new Harness();
        h.Send("ETH");
        h.DiagnosticClock.ThrowFrequency = true;
        Assert.Null(h.Service.Diagnostics.GetSnapshot("ETH"));
        h.Advance(TimeSpan.FromSeconds(6));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH"));
        Assert.Contains("price is stale", error.Message);
        h.Send("ETH", 3120m);
        Assert.Equal(3120m, (await h.Service.GetPriceAsync("ETH")).PriceUsd);
    }

    [Fact]
    public async Task ConcurrentStaleGettersEmitOnceAndRetainEverySuppressedCandidate()
    {
        using var h = new Harness();
        h.Send("ETH");
        h.Advance(TimeSpan.FromSeconds(6));
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
            await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH")))));
        Assert.Single(h.Logger.Diagnostics);
        Assert.Equal(31, h.Snapshot("ETH").SuppressedStaleCount);
        h.Advance(TimeSpan.FromSeconds(60));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH"));
        Assert.Equal(31, h.Logger.Diagnostics.Last().SuppressedCount);
        Assert.Equal(0, h.Snapshot("ETH").SuppressedStaleCount);
    }

    [Fact]
    public async Task InFlightStaleEventRetainsConcurrentSuppressionForTheNextEvent()
    {
        using var h = new Harness();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Send("ETH");
        h.Advance(TimeSpan.FromSeconds(6));
        h.Logger.OnDiagnostic = () =>
        {
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)), "Diagnostic sink was not released.");
        };
        var first = Task.Run(async () => await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH")));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
                await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH")))));
            Assert.Equal(8, h.Snapshot("ETH").SuppressedStaleCount);
        }
        finally
        {
            release.Set();
            await first;
        }
        Assert.Single(h.Logger.Diagnostics);
        Assert.Equal(0, h.Logger.Diagnostics[0].SuppressedCount);
        Assert.Equal(8, h.Snapshot("ETH").SuppressedStaleCount);
        h.Logger.OnDiagnostic = null;
        h.Advance(TimeSpan.FromSeconds(60));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH"));
        Assert.Equal(8, h.Logger.Diagnostics.Last().SuppressedCount);
        Assert.Equal(0, h.Snapshot("ETH").SuppressedStaleCount);
    }

    [Fact]
    public async Task ExistingSampleLoggerFailureStillPropagatesAfterPricePublication()
    {
        using var h = new Harness();
        var failure = new InvalidOperationException("existing sampled logger failure");
        h.Logger.OnSample = () => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => h.Send("ETH")));
        Assert.Equal(3100m, (await h.Service.GetPriceAsync("ETH")).PriceUsd);
    }

    [Fact]
    public void SnapshotRecordsMonotonicPhasesCountersAndAssetTimestamps()
    {
        using var h = new Harness();
        var d = h.Service.Diagnostics;
        d.ConnectionStarting();
        d.ConnectionOpened();
        d.ReceiveStarted();
        h.DiagnosticClock.Advance(TimeSpan.FromMilliseconds(25));
        var waiting = h.Snapshot("ETH");
        Assert.Equal(1, waiting.ConnectionGeneration);
        Assert.Equal(25, waiting.ActivePhaseDurationMs);
        Assert.NotNull(waiting.PhaseStartedAtUtc);
        d.ReceiveCompleted(false);
        d.ReceiveStarted();
        h.DiagnosticClock.Advance(TimeSpan.FromMilliseconds(5));
        d.ReceiveCompleted(true);
        d.ParseStarted();
        h.DiagnosticClock.Advance(TimeSpan.FromMilliseconds(2));
        d.ParseCompleted(true);
        d.PublicationWaiting();
        h.DiagnosticClock.Advance(TimeSpan.FromMilliseconds(3));
        d.PublicationEntered();
        var point = Point("ETH", Epoch);
        d.RecordPublished(point);
        d.SampleLogStarted();
        h.DiagnosticClock.Advance(TimeSpan.FromMilliseconds(4));
        d.SampleLogCompleted();
        h.DiagnosticClock.Advance(TimeSpan.FromMilliseconds(1));
        d.PublicationCompleted();
        var snapshot = h.Snapshot("ETH");
        Assert.Equal(2, snapshot.ReceivedFrames);
        Assert.Equal(1, snapshot.CompletedMessages);
        Assert.Equal(Epoch.AddMilliseconds(30), snapshot.LastReceiveCompletedAtUtc);
        Assert.Equal(2, snapshot.LastParseDurationMs);
        Assert.Equal(3, snapshot.LastPublicationLockWaitMs);
        Assert.Equal(5, snapshot.LastPublicationLockHoldMs);
        Assert.Equal(4, snapshot.LastSampleLogDurationMs);
        Assert.Equal(point.SourceUpdatedAtUtc, snapshot.LastSourceUpdatedAtUtc);
        Assert.Equal(point.FetchedAtUtc, snapshot.LastFetchedAtUtc);
        Assert.Equal(1, snapshot.AcceptedMessages);
        Assert.Equal(0, h.Snapshot("SOL").AcceptedMessages);
        Assert.Null(d.GetSnapshot("BTC"));
        d.ConnectionClosed();
        d.ConnectionStarting();
        Assert.Equal(2, h.Snapshot("ETH").ConnectionGeneration);
    }

    [Fact]
    public void StaleAndRecoveryThrottleSeparatelyPerAsset_ExactSixtySecondsEligible()
    {
        using var h = new Harness();
        var d = h.Service.Diagnostics;
        void Stale(string asset) => d.ObserveStale(Point(asset, Epoch), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(5));
        void Recover(string asset)
        {
            var point = Point(asset, h.BusinessClock.GetUtcNow());
            d.ObserveRecovery(point, d.RecordPublished(point), h.BusinessClock.GetUtcNow, TimeSpan.FromSeconds(5));
        }

        Stale("ETH");
        Stale("SOL");
        Recover("ETH");
        Recover("SOL");
        Assert.Equal(4, h.Logger.Diagnostics.Length);
        h.Advance(TimeSpan.FromSeconds(10));
        Stale("ETH");
        Recover("ETH");
        h.Advance(TimeSpan.FromSeconds(10));
        Stale("ETH");
        Stale("ETH");
        Recover("ETH");
        Assert.Equal(3, h.Snapshot("ETH").SuppressedStaleCount);
        Assert.Equal(2, h.Snapshot("ETH").SuppressedRecoveryCount);
        Assert.False(h.Snapshot("ETH").IsStale);
        h.Advance(TimeSpan.FromSeconds(39));
        Stale("ETH");
        Assert.Equal(4, h.Logger.Diagnostics.Length);
        h.Advance(TimeSpan.FromSeconds(1));
        Stale("ETH");
        var stale = h.Logger.Diagnostics.Last();
        Assert.Equal("stale", stale.Kind);
        Assert.Equal(4, stale.SuppressedCount);
        Assert.Equal(0, h.Snapshot("ETH").SuppressedStaleCount);
        Assert.Equal(2, h.Snapshot("ETH").SuppressedRecoveryCount);
        Recover("ETH");
        Assert.Equal("recovery", h.Logger.Diagnostics.Last().Kind);
        Assert.Equal(2, h.Logger.Diagnostics.Last().SuppressedCount);
        Assert.Equal(0, h.Snapshot("ETH").SuppressedRecoveryCount);
    }

    [Fact]
    public void RecoveryRequiresPublicationAfterTheObservedStaleRejection()
    {
        using var h = new Harness();
        var d = h.Service.Diagnostics;
        var point = Point("ETH", Epoch);
        var earlier = d.RecordPublished(point);
        d.ObserveStale(point, TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(5));
        d.ObserveRecovery(point, earlier, h.BusinessClock.GetUtcNow, TimeSpan.FromSeconds(5));
        Assert.True(h.Snapshot("ETH").IsStale);
        Assert.Single(h.Logger.Diagnostics);

        var later = d.RecordPublished(point);
        d.ObserveRecovery(point, earlier, h.BusinessClock.GetUtcNow, TimeSpan.FromSeconds(5));
        Assert.True(h.Snapshot("ETH").IsStale);
        d.ObserveRecovery(point, later, h.BusinessClock.GetUtcNow, TimeSpan.FromSeconds(5));
        Assert.False(h.Snapshot("ETH").IsStale);
        Assert.Equal(2, h.Logger.Diagnostics.Length);
        d.ObserveRecovery(point, later, h.BusinessClock.GetUtcNow, TimeSpan.FromSeconds(5));
        Assert.Equal(2, h.Logger.Diagnostics.Length);
    }

    [Fact]
    public async Task WallClockJumpDoesNotOpenThrottle_AndSuppressedRecoveryIsNotReplayed()
    {
        using var h = new Harness();
        h.Send("ETH");
        h.Advance(TimeSpan.FromSeconds(6));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH"));
        h.Send("ETH");
        h.Advance(TimeSpan.FromSeconds(6));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH"));
        h.Send("ETH");
        Assert.Equal(2, h.Logger.Diagnostics.Length);
        Assert.Equal(1, h.Snapshot("ETH").SuppressedRecoveryCount);
        h.BusinessClock.JumpUtc(TimeSpan.FromDays(2));
        h.DiagnosticClock.JumpUtc(TimeSpan.FromDays(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetPriceAsync("ETH"));
        Assert.Equal(2, h.Logger.Diagnostics.Length);
        h.Send("ETH");
        h.Advance(TimeSpan.FromSeconds(60));
        // No new getter rejection: another accepted trade must not invent recovery.
        h.Send("ETH");
        Assert.Equal(2, h.Logger.Diagnostics.Length);
        Assert.False(h.Snapshot("ETH").IsStale);
    }

    [Fact]
    public async Task ConcurrentSnapshotReadsRemainCoherentWhilePublishing()
    {
        using var h = new Harness();
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                h.Send(i % 2 == 0 ? "ETH" : "SOL", 3100 + i);
            }
        });
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                var snapshot = h.Snapshot(i % 2 == 0 ? "ETH" : "SOL");
                Assert.InRange(snapshot.AcceptedMessages, 0, 250);
                if (snapshot.AcceptedMessages > 0)
                {
                    Assert.Equal(Epoch, snapshot.LastFetchedAtUtc);
                    Assert.Equal(Epoch, snapshot.LastSourceUpdatedAtUtc);
                }
            }
        }));
        await Task.WhenAll(readers.Append(writer)).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(250, h.Snapshot("ETH").AcceptedMessages);
        Assert.Equal(250, h.Snapshot("SOL").AcceptedMessages);
        Assert.Empty(h.Logger.Diagnostics);
    }

    private static CryptoReferencePricePoint Point(string asset, DateTimeOffset fetched) =>
        new(asset, asset + "USDT", 3100m, Epoch, fetched, BinanceCryptoTradeParser.SourceName);

    private sealed class Harness : IDisposable
    {
        public ManualClock BusinessClock { get; } = new();
        public ManualClock DiagnosticClock { get; } = new();
        public RecordingLogger Logger { get; } = new();
        public BinanceCryptoReferenceTradeStreamService Service { get; }

        public Harness(int sampleIntervalSeconds = 60)
        {
            Service = new BinanceCryptoReferenceTradeStreamService(Logger,
                new BinanceCryptoReferenceOptions { AssetSymbols = ["ETH", "SOL"], StaleAfterSeconds = 5, SampleIntervalSeconds = sampleIntervalSeconds },
                new TestAppRepository(), BusinessClock, new BinanceCryptoReferenceDiagnostics(Logger, DiagnosticClock));
        }

        public void Advance(TimeSpan elapsed)
        {
            BusinessClock.Advance(elapsed);
            DiagnosticClock.Advance(elapsed);
        }

        public void Send(string asset, decimal price = 3100m) => Service.ProcessMessage(Encoding.UTF8.GetBytes(
            $$"""{"s":"{{asset}}USDT","p":"{{price.ToString(CultureInfo.InvariantCulture)}}","T":{{Epoch.ToUnixTimeMilliseconds()}}} """));

        public BinanceCryptoReferenceDiagnosticSnapshot Snapshot(string asset) => Assert.IsType<BinanceCryptoReferenceDiagnosticSnapshot>(Service.Diagnostics.GetSnapshot(asset));
        public void Dispose() => Service.Dispose();
    }

    private sealed class ManualClock : TimeProvider
    {
        private long utcTicks = Epoch.UtcTicks;
        private long timestamp;
        public bool ThrowTimestamp { get; set; }
        public bool ThrowUtc { get; set; }
        public bool ThrowFrequency { get; set; }
        public override long TimestampFrequency => ThrowFrequency ? throw new InvalidOperationException("diagnostic duration clock failure") : TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ThrowTimestamp ? throw new InvalidOperationException("diagnostic monotonic clock failure") : Interlocked.Read(ref timestamp);
        public override DateTimeOffset GetUtcNow() => ThrowUtc ? throw new InvalidOperationException("diagnostic UTC clock failure") : new DateTimeOffset(Interlocked.Read(ref utcTicks), TimeSpan.Zero);
        public void Advance(TimeSpan elapsed)
        {
            Interlocked.Add(ref timestamp, elapsed.Ticks);
            JumpUtc(elapsed);
        }
        public void JumpUtc(TimeSpan delta) => Interlocked.Add(ref utcTicks, delta.Ticks);
    }

    private sealed record Entry(LogLevel Level, string Kind, string Asset, long SuppressedCount);

    private sealed class RecordingLogger : ILogger<BinanceCryptoReferenceTradeStreamService>
    {
        private readonly ConcurrentQueue<Entry> entries = new();
        public Entry[] Diagnostics => entries.ToArray();
        public Action? OnSample { get; set; }
        public Action? OnDiagnostic { get; set; }
        public bool ThrowDiagnostics { get; set; }
        public int DiagnosticAttempts;
        public int CompletedDiagnosticCallbacks;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(pair => pair.Key, pair => pair.Value);
            if (values.TryGetValue("DiagnosticKind", out var kind))
            {
                Interlocked.Increment(ref DiagnosticAttempts);
                if (ThrowDiagnostics) throw new InvalidOperationException("new diagnostic logger failure");
                OnDiagnostic?.Invoke();
                Interlocked.Increment(ref CompletedDiagnosticCallbacks);
                entries.Enqueue(new Entry(logLevel, (string)kind!, (string)values["AssetSymbol"]!, Convert.ToInt64(values["SuppressedCount"], CultureInfo.InvariantCulture)));
            }
            else if (((string)values["{OriginalFormat}"]!).Contains("reference price sampled", StringComparison.Ordinal))
            {
                OnSample?.Invoke();
            }
        }
    }
}
