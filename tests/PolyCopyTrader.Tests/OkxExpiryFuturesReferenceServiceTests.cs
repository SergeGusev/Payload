using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class OkxExpiryFuturesReferenceServiceTests
{
    private static readonly DateTimeOffset BaseUtc =
        new(2026, 9, 16, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetPayloadAsync_TimeoutRetriesOnceAfterBoundedJitter()
    {
        var calls = 0;
        var delays = new List<TimeSpan>();
        var randomBounds = new List<(int Minimum, int ExclusiveMaximum)>();
        using var client = new HttpClient(new AsyncStubHttpMessageHandler((_, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new TaskCanceledException("simulated request timeout", new TimeoutException());
            }

            return Task.FromResult(JsonResponse("{\"ok\":true}"));
        }));
        var service = CreateService(
            client,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            nextRandom: (minimum, exclusiveMaximum) =>
            {
                randomBounds.Add((minimum, exclusiveMaximum));
                return 100;
            });

        var payload = await service.GetPayloadAsync("/test", CancellationToken.None);

        Assert.Equal("{\"ok\":true}", Encoding.UTF8.GetString(payload));
        Assert.Equal(2, calls);
        Assert.Equal([TimeSpan.FromMilliseconds(100)], delays);
        Assert.Equal([(100, 251)], randomBounds);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetPayloadAsync_TransientHttpStatusRetriesOnce(HttpStatusCode statusCode)
    {
        var calls = 0;
        var delays = new List<TimeSpan>();
        using var client = new HttpClient(new AsyncStubHttpMessageHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? JsonResponse("{\"error\":true}", statusCode)
                : JsonResponse("{\"ok\":true}"));
        }));
        var service = CreateService(
            client,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            nextRandom: (_, _) => 250);

        await service.GetPayloadAsync("/test", CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal([TimeSpan.FromMilliseconds(250)], delays);
    }

    [Fact]
    public async Task GetPayloadAsync_NonTransientHttpStatusDoesNotRetry()
    {
        var calls = 0;
        using var client = new HttpClient(new AsyncStubHttpMessageHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse("{\"error\":true}", HttpStatusCode.BadRequest));
        }));
        var service = CreateService(client);

        var exception = await Assert.ThrowsAsync<OkxHttpStatusException>(
            () => service.GetPayloadAsync("/test", CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetPayloadAsync_CallerCancellationDoesNotRetry()
    {
        var calls = 0;
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new AsyncStubHttpMessageHandler((_, _) =>
        {
            calls++;
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }));
        var service = CreateService(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetPayloadAsync("/test", cancellation.Token));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TryRefreshIndexTickerAsync_ParseFailureDoesNotRetry()
    {
        var calls = 0;
        using var client = new HttpClient(new AsyncStubHttpMessageHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse("{not-json"));
        }));
        var repository = new TestAppRepository();
        var service = CreateService(client, repository: repository);

        await service.TryRefreshIndexTickerAsync("BTC", CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Single(repository.ApiErrors);
    }

    [Fact]
    public async Task ExecuteAsync_BlockedFuturesTickerDoesNotPaceIndexLoop()
    {
        var indexCalls = 0;
        var futuresStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new AsyncStubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (path.StartsWith("/api/v5/public/instruments", StringComparison.Ordinal))
            {
                return JsonResponse(InstrumentsJson("BTC"));
            }

            if (path.StartsWith("/api/v5/market/tickers", StringComparison.Ordinal))
            {
                futuresStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (path.StartsWith("/api/v5/market/index-tickers", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref indexCalls);
                return JsonResponse(IndexJson("BTC", BaseUtc));
            }

            throw new InvalidOperationException("Unexpected OKX test path: " + path);
        }))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var options = Options(pollIntervalMilliseconds: 20);
        using var service = new OkxExpiryFuturesReferencePriceService(
            NullLogger<OkxExpiryFuturesReferencePriceService>.Instance,
            options,
            new SingletonHttpClientFactory(client),
            new NoOpAppRepository());

        await service.StartAsync(CancellationToken.None);
        try
        {
            await futuresStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var observedBefore = Volatile.Read(ref indexCalls);
            await WaitUntilAsync(
                () => Volatile.Read(ref indexCalls) > observedBefore,
                TimeSpan.FromSeconds(3));

            Assert.True(Volatile.Read(ref indexCalls) > observedBefore);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BlockedAssetIndexDoesNotPaceAnotherAssetIndexLoop()
    {
        var ethIndexCalls = 0;
        var btcIndexStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new AsyncStubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (path.StartsWith("/api/v5/public/instruments", StringComparison.Ordinal))
            {
                return JsonResponse(InstrumentsJson("BTC", "ETH"));
            }

            if (path.StartsWith("/api/v5/market/tickers", StringComparison.Ordinal))
            {
                return JsonResponse(FuturesTickerJson("BTC", BaseUtc));
            }

            if (path.Contains("instId=BTC-USD", StringComparison.Ordinal))
            {
                btcIndexStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (path.Contains("instId=ETH-USD", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref ethIndexCalls);
                return JsonResponse(IndexJson("ETH", BaseUtc));
            }

            throw new InvalidOperationException("Unexpected OKX test path: " + path);
        }))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var options = Options(20, ["BTC", "ETH"]);
        using var service = new OkxExpiryFuturesReferencePriceService(
            NullLogger<OkxExpiryFuturesReferencePriceService>.Instance,
            options,
            new SingletonHttpClientFactory(client),
            new NoOpAppRepository());

        await service.StartAsync(CancellationToken.None);
        try
        {
            await btcIndexStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var observedBefore = Volatile.Read(ref ethIndexCalls);
            await WaitUntilAsync(
                () => Volatile.Read(ref ethIndexCalls) > observedBefore,
                TimeSpan.FromSeconds(3));

            Assert.True(Volatile.Read(ref ethIndexCalls) > observedBefore);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RunInstrumentPollingLoopAsync_FailedCatalogRefreshRetriesAtNormalPollCadence()
    {
        var calls = 0;
        var delays = new List<TimeSpan>();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new AsyncStubHttpMessageHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse("{not-json"));
        }));
        var service = CreateService(
            client,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                cancellation.Cancel();
                return Task.CompletedTask;
            });

        await service.RunInstrumentPollingLoopAsync(cancellation.Token);

        Assert.Equal(1, calls);
        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
    }

    [Fact]
    public async Task RunInstrumentPollingLoopAsync_SuccessfulCatalogRefreshWaitsConfiguredRefreshInterval()
    {
        var calls = 0;
        var delays = new List<TimeSpan>();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new AsyncStubHttpMessageHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse(InstrumentsJson("BTC")));
        }));
        var service = CreateService(
            client,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                cancellation.Cancel();
                return Task.CompletedTask;
            });

        await service.RunInstrumentPollingLoopAsync(cancellation.Token);

        Assert.Equal(1, calls);
        Assert.Equal([TimeSpan.FromSeconds(300)], delays);
    }

    [Fact]
    public async Task SuccessfulTickerPayloads_AreTimestampedAfterReceiptAndParsing_AndStillExpireAtFiveSeconds()
    {
        var clock = new ManualClock(BaseUtc);
        using var client = new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (path.StartsWith("/api/v5/public/instruments", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(InstrumentsJson("BTC")));
            }

            if (path.StartsWith("/api/v5/market/tickers", StringComparison.Ordinal))
            {
                clock.UtcNow = BaseUtc.AddSeconds(1);
                return Task.FromResult(JsonResponse(FuturesTickerJson("BTC", BaseUtc)));
            }

            if (path.StartsWith("/api/v5/market/index-tickers", StringComparison.Ordinal))
            {
                clock.UtcNow = BaseUtc.AddSeconds(2);
                return Task.FromResult(JsonResponse(IndexJson("BTC", BaseUtc)));
            }

            throw new InvalidOperationException("Unexpected OKX test path: " + path);
        }));
        var service = CreateService(client, utcNow: () => clock.UtcNow);

        await service.TryRefreshInstrumentsAsync(CancellationToken.None);
        await service.TryRefreshTickersAsync(CancellationToken.None);
        await service.TryRefreshIndexTickerAsync("BTC", CancellationToken.None);
        var points = await service.GetNearestExpiryPricesAsync(
            "BTC",
            BaseUtc.AddDays(1),
            1,
            CancellationToken.None);

        var point = Assert.Single(points);
        Assert.Equal(BaseUtc.AddSeconds(1), point.FetchedAtUtc);

        clock.UtcNow = BaseUtc.AddSeconds(6);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetNearestExpiryPricesAsync(
                "BTC",
                BaseUtc.AddDays(1),
                1,
                CancellationToken.None));
        Assert.Contains("StaleAfterSeconds=5", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceIncident_FirstAndThirtySecondSummaryPersist_AndRecoveryLogsOnce()
    {
        var clock = new ManualClock(BaseUtc);
        var fail = true;
        using var client = new HttpClient(new AsyncStubHttpMessageHandler((_, _) =>
            Task.FromResult(fail
                ? JsonResponse("{\"error\":true}", HttpStatusCode.ServiceUnavailable)
                : JsonResponse(IndexJson("BTC", BaseUtc)))));
        var logger = new RecordingLogger<OkxExpiryFuturesReferencePriceService>();
        var repository = new TestAppRepository();
        var service = CreateService(
            client,
            logger,
            repository,
            () => clock.UtcNow,
            (_, _) => Task.CompletedTask);

        await service.TryRefreshIndexTickerAsync("BTC", CancellationToken.None);
        clock.UtcNow = BaseUtc.AddSeconds(1);
        await service.TryRefreshIndexTickerAsync("BTC", CancellationToken.None);
        clock.UtcNow = BaseUtc.AddSeconds(31);
        await service.TryRefreshIndexTickerAsync("BTC", CancellationToken.None);

        Assert.Equal(2, repository.ApiErrors.Count);
        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Warning));

        fail = false;
        clock.UtcNow = BaseUtc.AddSeconds(32);
        await service.TryRefreshIndexTickerAsync("BTC", CancellationToken.None);
        await service.TryRefreshIndexTickerAsync("BTC", CancellationToken.None);

        var recovery = Assert.Single(logger.Entries, entry =>
            entry.Level == LogLevel.Information &&
            entry.Message.Contains("recovered", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("FailureCount", recovery.Properties.Keys);
        Assert.Equal(3L, recovery.Properties["FailureCount"]);
    }

    private static OkxExpiryFuturesReferencePriceService CreateService(
        HttpClient client,
        ILogger<OkxExpiryFuturesReferencePriceService>? logger = null,
        IAppRepository? repository = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<int, int, int>? nextRandom = null)
    {
        return new OkxExpiryFuturesReferencePriceService(
            logger ?? NullLogger<OkxExpiryFuturesReferencePriceService>.Instance,
            Options(),
            new SingletonHttpClientFactory(client),
            repository ?? new NoOpAppRepository(),
            utcNow ?? (() => BaseUtc),
            delayAsync ?? ((_, _) => Task.CompletedTask),
            nextRandom ?? ((minimum, _) => minimum));
    }

    private static OkxExpiryFuturesReferenceOptions Options(
        int pollIntervalMilliseconds = 1_000,
        List<string>? assetSymbols = null)
    {
        return new OkxExpiryFuturesReferenceOptions
        {
            Enabled = true,
            RestBaseUrl = "https://okx.test",
            AssetSymbols = assetSymbols ?? ["BTC"],
            PollIntervalMilliseconds = pollIntervalMilliseconds,
            InstrumentRefreshIntervalSeconds = 300,
            RequestTimeoutMilliseconds = 2_000,
            StaleAfterSeconds = 5
        };
    }

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string InstrumentsJson(params string[] assetSymbols)
    {
        var expiry = BaseUtc.AddDays(7).ToUnixTimeMilliseconds();
        var data = string.Join(",", assetSymbols.Select(assetSymbol =>
            $$"""{"instType":"FUTURES","instId":"{{assetSymbol}}-USD_UM-261001","instFamily":"{{assetSymbol}}-USD_UM","ctType":"linear","settleCcy":"USD","state":"live","expTime":"{{expiry}}"}"""));
        return $$"""
            {"code":"0","msg":"","data":[{{data}}]}
            """;
    }

    private static string FuturesTickerJson(string assetSymbol, DateTimeOffset sourceUpdatedAtUtc)
    {
        return $$"""
            {"code":"0","msg":"","data":[{"instId":"{{assetSymbol}}-USD_UM-261001","bidPx":"99","askPx":"101","ts":"{{sourceUpdatedAtUtc.ToUnixTimeMilliseconds()}}"}]}
            """;
    }

    private static string IndexJson(string assetSymbol, DateTimeOffset sourceUpdatedAtUtc)
    {
        return $$"""
            {"code":"0","msg":"","data":[{"instId":"{{assetSymbol}}-USD","idxPx":"100","ts":"{{sourceUpdatedAtUtc.ToUnixTimeMilliseconds()}}"}]}
            """;
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

    private sealed class ManualClock(DateTimeOffset utcNow)
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class SingletonHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class AsyncStubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);
}
