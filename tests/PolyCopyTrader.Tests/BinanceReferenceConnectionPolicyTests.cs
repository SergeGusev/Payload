using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Diagnostics;
using PolyCopyTrader.Service.ExternalPrices;

namespace PolyCopyTrader.Tests;

public sealed class BinanceReferenceConnectionPolicyTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> AssetAges =>
        from asset in new[] { "BTC", "ETH", "SOL" }
        from seconds in new[] { 5d, 5.001d, 3600d }
        select new object[] { asset, seconds };

    public static IEnumerable<object[]> AssetUnavailableStates =>
        from asset in new[] { "BTC", "ETH", "SOL" }
        from state in new[] { WebSocketState.Connecting, WebSocketState.CloseSent,
            WebSocketState.CloseReceived, WebSocketState.Closed, WebSocketState.Aborted }
        select new object[] { asset, state };

    [Theory]
    [MemberData(nameof(AssetAges))]
    public async Task ConnectedQuoteHasNoAgeExpiryAndKeepsOriginalValues(string asset, double seconds)
    {
        using var h = new Harness();
        h.Send(asset, 1234.56m);
        var original = await h.Get(asset);
        h.Clock.Advance(TimeSpan.FromSeconds(seconds));

        Assert.Equal(original, await h.Get(asset));
        Assert.Equal(1234.56m, original.PriceUsd);
        Assert.Equal(Epoch.AddSeconds(-1), original.SourceUpdatedAtUtc);
        Assert.Equal(Epoch, original.FetchedAtUtc);
        Assert.Equal(asset == "BTC" ? BinanceBtcUsdTradeParser.SourceName : BinanceCryptoTradeParser.SourceName,
            original.Source);
        Assert.Equal(1, h.SampleCount(asset));
    }

    [Theory]
    [InlineData("BTC")]
    [InlineData("ETH")]
    [InlineData("SOL")]
    public async Task NeverSeenAndCallerCancellationKeepOriginalBehavior(string asset)
    {
        using var h = new Harness(connect: false);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get(asset));
        Assert.Equal($"Binance {asset}/USDT trade stream has not received a price yet.", error.Message);
        h.Connect(asset, WebSocketState.Connecting);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get(asset));
        h.Socket(asset).TestState = WebSocketState.Open;
        // Connection/control traffic alone cannot supply the first price.
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get(asset));
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Get(asset, caller.Token));
        h.Send(asset);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Get(asset, caller.Token));
    }

    [Theory]
    [MemberData(nameof(AssetUnavailableStates))]
    public async Task ObservedSocketStateBlocksQuoteBeforeEndConnection(string asset, WebSocketState state)
    {
        using var h = new Harness();
        h.Send(asset);
        await h.Get(asset);
        h.Socket(asset).TestState = state;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get(asset));
        Assert.Contains("price is unavailable", error.Message);
        h.Send(asset, 9999m);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get(asset));
        Assert.Equal(1, h.SampleCount(asset));
    }

    [Theory]
    [InlineData("BTC")]
    [InlineData("ETH")]
    [InlineData("SOL")]
    public async Task CancelledConnectionLifetimeBlocksBeforeFinallyAndCannotPublish(string asset)
    {
        using var h = new Harness(connect: false);
        using var lifetime = new CancellationTokenSource();
        h.Connect(asset, lifetime: lifetime.Token);
        h.Send(asset);
        lifetime.Cancel();

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get(asset));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Send(asset, 9999m);
        Assert.Equal(1, h.SampleCount(asset));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get(asset));
    }

    [Theory]
    [InlineData("BTC")]
    [InlineData("ETH")]
    [InlineData("SOL")]
    public async Task EndAndReconnectRequireCurrentGenerationTrade(string asset)
    {
        using var h = new Harness();
        h.Send(asset, 1234m);
        var priorGeneration = h.Generation(asset);
        h.End(asset, priorGeneration);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get(asset));
        Assert.Equal(1, h.SampleCount(asset));

        h.Connect(asset);
        Assert.True(h.Generation(asset) > priorGeneration);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get(asset));
        h.Send(asset, 9000m, priorGeneration);
        h.SendPayload(asset, "not-json"u8.ToArray());
        h.SendPayload(asset, Encoding.UTF8.GetBytes($$"""{"s":"{{asset}}USDT","p":"0"}"""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get(asset));

        h.Clock.Advance(TimeSpan.FromSeconds(1));
        h.Send(asset, 1235m);
        var current = await h.Get(asset);
        Assert.Equal(1235m, current.PriceUsd);
        Assert.Equal(Epoch.AddSeconds(1), current.FetchedAtUtc);
        Assert.Equal(Epoch.AddSeconds(-1), current.SourceUpdatedAtUtc);
        // Old finally/publication cannot invalidate or overwrite the new generation.
        h.End(asset, priorGeneration);
        h.Send(asset, 9001m, priorGeneration);
        Assert.Equal(current, await h.Get(asset));
        // Reconnect did not clear or reset the established sampled-price schedule.
        Assert.Equal(1, h.SampleCount(asset));
    }

    [Theory]
    [InlineData("ETH", "SOL")]
    [InlineData("SOL", "ETH")]
    public async Task SharedSocketNeedsFirstTradeSeparatelyForEachAsset(string first, string second)
    {
        using var h = new Harness();
        h.Send(first);
        h.Send(second);
        h.Connect(first);
        h.Send(first, 4000m);

        Assert.Equal(4000m, (await h.Get(first)).PriceUsd);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get(second));
        // BTC messages are disabled on the configured ETH/SOL stream.
        h.SendPayload(second, Encoding.UTF8.GetBytes("{\"s\":\"BTCUSDT\",\"p\":\"5000\"}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get(second));
        h.Send(second, 2000m);
        Assert.Equal(2000m, (await h.Get(second)).PriceUsd);
    }

    [Fact]
    public async Task BtcAndSharedCryptoConnectionsHaveIndependentAvailability()
    {
        using var h = new Harness();
        foreach (var asset in new[] { "BTC", "ETH", "SOL" }) h.Send(asset);
        h.Socket("BTC").Abort();
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get("BTC"));
        await h.Get("ETH");
        await h.Get("SOL");
        h.Connect("BTC");
        h.Send("BTC");
        h.Socket("ETH").Abort();
        await h.Get("BTC");
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get("ETH"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Get("SOL"));
    }

    [Theory]
    [InlineData("BTC")]
    [InlineData("ETH")]
    [InlineData("SOL")]
    public async Task LatestTradeUpdatesBeforeSamplingAndSampleCadenceStaysUnchanged(string asset)
    {
        using var h = new Harness();
        h.Send(asset, 100m);
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        h.Send(asset, 101m);
        Assert.Equal(101m, (await h.Get(asset)).PriceUsd);
        Assert.Equal(1, h.SampleCount(asset));
        h.Clock.Advance(TimeSpan.FromSeconds(59));
        h.Send(asset, 102m);
        Assert.Equal(102m, (await h.Get(asset)).PriceUsd);
        Assert.Equal(2, h.SampleCount(asset));
    }

    [Fact]
    public void ProductionSocketFactoriesConfigureNativeTenSecondPingAndPong()
    {
        using var btc = BinanceBtcUsdTradeStreamService.CreateSocket();
        using var crypto = BinanceCryptoReferenceTradeStreamService.CreateSocket();
        foreach (var socket in new[] { btc, crypto })
        {
            Assert.Equal(WebSocketState.None, socket.State);
            Assert.Equal(TimeSpan.FromSeconds(10), socket.Options.KeepAliveInterval);
            Assert.Equal(TimeSpan.FromSeconds(10), socket.Options.KeepAliveTimeout);
        }
    }

    private sealed record Quote(decimal PriceUsd, DateTimeOffset SourceUpdatedAtUtc,
        DateTimeOffset FetchedAtUtc, string Source);

    private sealed class Harness : IDisposable
    {
        public ManualClock Clock { get; } = new();
        private readonly BinanceBtcUsdTradeStreamService btc;
        private readonly BinanceCryptoReferenceTradeStreamService crypto;
        private readonly BtcUsdReferencePriceCache btcCache;
        private readonly List<TestBinanceReferenceWebSocket> sockets = [];
        private TestBinanceReferenceWebSocket? btcSocket;
        private TestBinanceReferenceWebSocket? cryptoSocket;
        private long btcGeneration;
        private long cryptoGeneration;

        public Harness(bool connect = true)
        {
            var btcOptions = new BinanceBtcUsdReferenceOptions { StaleAfterSeconds = 5, SampleIntervalSeconds = 60 };
            btcCache = new BtcUsdReferencePriceCache(btcOptions);
            btc = new BinanceBtcUsdTradeStreamService(NullLogger<BinanceBtcUsdTradeStreamService>.Instance,
                btcOptions, btcCache, new NoOpLagDiagnostics(), new TestAppRepository(), Clock);
            var logger = NullLogger<BinanceCryptoReferenceTradeStreamService>.Instance;
            crypto = new BinanceCryptoReferenceTradeStreamService(logger,
                new BinanceCryptoReferenceOptions { AssetSymbols = ["ETH", "SOL"], StaleAfterSeconds = 5, SampleIntervalSeconds = 60 },
                new TestAppRepository(), Clock, new BinanceCryptoReferenceDiagnostics(logger, Clock));
            if (connect)
            {
                Connect("BTC");
                Connect("ETH");
            }
        }

        public void Connect(string asset, WebSocketState state = WebSocketState.Open, CancellationToken lifetime = default)
        {
            var socket = new TestBinanceReferenceWebSocket { TestState = state };
            sockets.Add(socket);
            if (asset == "BTC")
            {
                btcSocket = socket;
                btcGeneration = btc.BeginConnection(socket, lifetime);
            }
            else
            {
                cryptoSocket = socket;
                cryptoGeneration = crypto.BeginConnection(socket, lifetime);
            }
        }

        public TestBinanceReferenceWebSocket Socket(string asset) =>
            (asset == "BTC" ? btcSocket : cryptoSocket) ?? throw new InvalidOperationException("No test connection.");
        public long Generation(string asset) => asset == "BTC" ? btcGeneration : cryptoGeneration;
        public void End(string asset, long generation)
        {
            if (asset == "BTC") btc.EndConnection(generation);
            else crypto.EndConnection(generation);
        }
        public void Send(string asset, decimal price = 1234m, long? generation = null) => SendPayload(asset,
            Encoding.UTF8.GetBytes($$"""{"s":"{{asset}}USDT","p":"{{price.ToString(CultureInfo.InvariantCulture)}}","T":{{Epoch.AddSeconds(-1).ToUnixTimeMilliseconds()}}}"""), generation);
        public void SendPayload(string asset, byte[] payload, long? generation = null)
        {
            if (asset == "BTC") btc.ProcessMessage(payload, generation ?? btcGeneration);
            else crypto.ProcessMessage(payload, generation ?? cryptoGeneration);
        }
        public async Task<Quote> Get(string asset, CancellationToken cancellation = default)
        {
            if (asset == "BTC")
            {
                var point = await btc.GetBtcUsdPriceAsync(cancellation);
                return new Quote(point.PriceUsd, point.SourceUpdatedAtUtc, point.FetchedAtUtc, point.Source);
            }
            var cryptoPoint = await crypto.GetPriceAsync(asset, cancellation);
            return new Quote(cryptoPoint.PriceUsd, cryptoPoint.SourceUpdatedAtUtc, cryptoPoint.FetchedAtUtc, cryptoPoint.Source);
        }
        public int SampleCount(string asset) => asset == "BTC" ? btcCache.Snapshot.SampleCount : crypto.GetSnapshot(asset).SampleCount;
        public void Dispose()
        {
            btc.EndConnection(btcGeneration);
            crypto.EndConnection(cryptoGeneration);
            btc.Dispose();
            crypto.Dispose();
            foreach (var socket in sockets) socket.Dispose();
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset utcNow = Epoch;
        public override DateTimeOffset GetUtcNow() => utcNow;
        public void Advance(TimeSpan elapsed) => utcNow += elapsed;
    }

    private sealed class NoOpLagDiagnostics : IBtcOrderBookLagDiagnosticService
    {
        public void RecordBinanceTrade(BtcUsdReferencePricePoint point) { }
        public void RecordPolymarketTopOfBook(MarketDataUpdate update, DateTimeOffset receivedAtUtc) { }
    }
}

// No network: production connection/publication methods inspect this same State property.
internal sealed class TestBinanceReferenceWebSocket : WebSocket
{
    public WebSocketState TestState { get; set; } = WebSocketState.Open;
    public override WebSocketState State => TestState;
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override string? SubProtocol => null;
    public override void Abort() => TestState = WebSocketState.Aborted;
    public override void Dispose() => TestState = WebSocketState.Closed;
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => throw new NotSupportedException();
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => throw new NotSupportedException();
    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) => throw new NotSupportedException();
    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
}
