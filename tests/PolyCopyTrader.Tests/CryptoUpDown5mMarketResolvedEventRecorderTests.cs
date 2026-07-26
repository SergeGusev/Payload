using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Tests;

public sealed class CryptoUpDown5mMarketResolvedEventRecorderTests
{
    [Fact]
    public async Task RecordAsync_StoresCryptoResolvedMarketAndDeduplicatesAssetEvents()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var marketEndUtc = marketStartUtc.AddMinutes(5);
        var receivedAtUtc = marketEndUtc.AddSeconds(8);
        var repository = new TestAppRepository();
        var recorder = new CryptoUpDown5mMarketResolvedEventRecorder(
            NullLogger<CryptoUpDown5mMarketResolvedEventRecorder>.Instance,
            repository);
        var snapshot = CreateSnapshot("BTC", marketStartUtc);

        await recorder.RecordAsync(
            CriticalCryptoUpDown5mAssetSelector.ComponentName,
            CreateResolvedUpdate("asset-up", "asset-down", winningOutcome: null, receivedAtUtc),
            snapshot,
            receivedAtUtc);
        await recorder.RecordAsync(
            CriticalCryptoUpDown5mAssetSelector.ComponentName,
            CreateResolvedUpdate("asset-down", "asset-down", winningOutcome: "Down", receivedAtUtc.AddSeconds(1)),
            snapshot,
            receivedAtUtc.AddSeconds(1));

        var result = Assert.Single(repository.CryptoUpDown5mWebSocketResolvedMarkets);
        Assert.Equal("BTC", result.AssetSymbol);
        Assert.Equal("btc-updown-5m-" + marketStartUtc.ToUnixTimeSeconds(), result.MarketSlug);
        Assert.Equal(marketStartUtc, result.MarketStartUtc);
        Assert.Equal(marketEndUtc, result.MarketEndUtc);
        Assert.Equal("Down", result.WinningOutcome);
        Assert.Equal("asset-down", result.WinningAssetId);
        Assert.Equal("MarketWebSocket", result.Source);
        Assert.Equal(2, result.EventCount);
        Assert.Equal(8m, result.ResultDelaySeconds);
        Assert.Equal(2, repository.MarketResolvedEventDiagnostics.Count);
        Assert.All(repository.MarketResolvedEventDiagnostics, diagnostic =>
        {
            Assert.Equal(CriticalCryptoUpDown5mAssetSelector.ComponentName, diagnostic.Component);
            Assert.True(diagnostic.ActiveSnapshotFound);
            Assert.Equal("BTC", diagnostic.SnapshotAssetSymbol);
            Assert.True(diagnostic.SnapshotIsCryptoUpDown5m);
            Assert.Equal("RecordedCryptoUpDown5mResult", diagnostic.RecorderAction);
        });
        Assert.Empty(repository.ApiErrors);
    }

    [Fact]
    public async Task RecordAsync_PreservesResolvedMarketWhenDiagnosticPersistenceIsDisabled()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var receivedAtUtc = marketStartUtc.AddMinutes(5).AddSeconds(8);
        var repository = new TestAppRepository();
        var recorder = new CryptoUpDown5mMarketResolvedEventRecorder(
            NullLogger<CryptoUpDown5mMarketResolvedEventRecorder>.Instance,
            repository,
            new MarketDataWebSocketOptions
            {
                PersistMarketResolvedDiagnostics = false
            });

        await recorder.RecordAsync(
            CriticalCryptoUpDown5mAssetSelector.ComponentName,
            CreateResolvedUpdate("asset-up", "asset-down", "Down", receivedAtUtc),
            CreateSnapshot("BTC", marketStartUtc),
            receivedAtUtc);

        var result = Assert.Single(repository.CryptoUpDown5mWebSocketResolvedMarkets);
        Assert.Equal("Down", result.WinningOutcome);
        Assert.Empty(repository.MarketResolvedEventDiagnostics);
        Assert.Empty(repository.ApiErrors);
    }

    [Fact]
    public async Task RecordAsync_IgnoresNonCryptoUpDownMarket()
    {
        var repository = new TestAppRepository();
        var recorder = new CryptoUpDown5mMarketResolvedEventRecorder(
            NullLogger<CryptoUpDown5mMarketResolvedEventRecorder>.Instance,
            repository);
        var snapshot = CreateSnapshot("DOGE", new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero));
        var receivedAtUtc = snapshot.EventStartTimeUtc!.Value.AddMinutes(5).AddSeconds(8);

        await recorder.RecordAsync(
            "PolymarketMarketWebSocket:shard-001",
            CreateResolvedUpdate("asset-up", "asset-up", "Up", receivedAtUtc),
            snapshot,
            receivedAtUtc);

        Assert.Empty(repository.CryptoUpDown5mWebSocketResolvedMarkets);
        var diagnostic = Assert.Single(repository.MarketResolvedEventDiagnostics);
        Assert.Equal("IgnoredUnsupportedMarket", diagnostic.RecorderAction);
        Assert.True(diagnostic.ActiveSnapshotFound);
        Assert.False(diagnostic.SnapshotIsCryptoUpDown5m);
        Assert.Empty(repository.ApiErrors);
    }

    [Fact]
    public async Task RecordAsync_RecordsRawDiagnosticWhenSnapshotMissing()
    {
        var receivedAtUtc = new DateTimeOffset(2026, 6, 8, 12, 5, 8, TimeSpan.Zero);
        var repository = new TestAppRepository();
        var recorder = new CryptoUpDown5mMarketResolvedEventRecorder(
            NullLogger<CryptoUpDown5mMarketResolvedEventRecorder>.Instance,
            repository);

        await recorder.RecordAsync(
            "PolymarketMarketWebSocket:shard-009",
            CreateResolvedUpdate("asset-missing", "asset-down", "Down", receivedAtUtc),
            null,
            receivedAtUtc);

        Assert.Empty(repository.CryptoUpDown5mWebSocketResolvedMarkets);
        var diagnostic = Assert.Single(repository.MarketResolvedEventDiagnostics);
        Assert.Equal("PolymarketMarketWebSocket:shard-009", diagnostic.Component);
        Assert.Equal("market_resolved", diagnostic.RawEventType);
        Assert.Equal("asset-missing", diagnostic.AssetId);
        Assert.Equal("asset-down", diagnostic.WinningAssetId);
        Assert.Equal("Down", diagnostic.WinningOutcome);
        Assert.False(diagnostic.ActiveSnapshotFound);
        Assert.Null(diagnostic.SnapshotMarketId);
        Assert.False(diagnostic.SnapshotIsCryptoUpDown5m);
        Assert.Equal("IgnoredNoSnapshot", diagnostic.RecorderAction);
        Assert.Empty(repository.ApiErrors);
    }

    private static MarketDataUpdate CreateResolvedUpdate(
        string assetId,
        string winningAssetId,
        string? winningOutcome,
        DateTimeOffset receivedAtUtc)
    {
        return new MarketDataUpdate(
            MarketDataEventType.MarketResolved,
            "market_resolved",
            assetId,
            "condition-1",
            null,
            null,
            null,
            null,
            null,
            TradeSide.Unknown,
            true,
            receivedAtUtc,
            RawJson: """{"event_type":"market_resolved"}""",
            WinningAssetId: winningAssetId,
            WinningOutcome: winningOutcome);
    }

    private static ActiveMarketAssetSnapshot CreateSnapshot(
        string assetSymbol,
        DateTimeOffset marketStartUtc)
    {
        var normalized = assetSymbol.Trim().ToUpperInvariant();
        var prefix = normalized.ToLowerInvariant();
        return new ActiveMarketAssetSnapshot(
            "asset-up",
            "market-1",
            "condition-1",
            "question-1",
            prefix + "-updown-5m-" + marketStartUtc.ToUnixTimeSeconds(),
            normalized + " Up or Down - test",
            null,
            null,
            null,
            prefix + "-up-or-down-5m",
            "Crypto",
            "Up",
            0,
            ["Up", "Down"],
            ["asset-up", "asset-down"],
            Active: true,
            Closed: false,
            Archived: false,
            Restricted: false,
            AcceptingOrders: true,
            EnableOrderBook: true,
            NegativeRisk: false,
            Liquidity: null,
            LiquidityClob: null,
            Volume: null,
            Volume24Hr: null,
            BestBid: null,
            BestAsk: null,
            Spread: null,
            LastTradePrice: null,
            OrderMinSize: null,
            OrderPriceMinTickSize: null,
            CreatedAtUtc: marketStartUtc.AddMinutes(-10),
            UpdatedAtUtc: marketStartUtc,
            StartDateUtc: null,
            EndDateUtc: marketStartUtc.AddMinutes(5),
            EventStartTimeUtc: marketStartUtc,
            MarketFetchedAtUtc: marketStartUtc,
            OrderBookUpdatedAtUtc: null,
            SnapshotUpdatedAtUtc: marketStartUtc);
    }
}
