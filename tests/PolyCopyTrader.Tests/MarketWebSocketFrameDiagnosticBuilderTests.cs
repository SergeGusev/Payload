using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Tests;

public sealed class MarketWebSocketFrameDiagnosticBuilderTests
{
    [Fact]
    public void Sampler_DoesNotCaptureImportantFramesWhenPersistenceIsDisabled()
    {
        var sampler = new MarketWebSocketFrameDiagnosticSampler(new MarketDataWebSocketOptions
        {
            PersistFrameDiagnostics = false,
            CriticalFrameDiagnosticSampleEvery = 1
        });

        var decision = sampler.Evaluate("not-json", null, parseSucceeded: false);

        Assert.False(decision.ShouldCapture);
        Assert.False(decision.Important);
        Assert.Equal("disabled", decision.Reason);
    }

    [Fact]
    public void Sampler_CapturesOnlyEveryConfiguredRoutineFrame()
    {
        var sampler = new MarketWebSocketFrameDiagnosticSampler(new MarketDataWebSocketOptions
        {
            CriticalFrameDiagnosticSampleEvery = 3
        });

        Assert.False(sampler.Evaluate("{}", [], parseSucceeded: true).ShouldCapture);
        Assert.False(sampler.Evaluate("{}", [], parseSucceeded: true).ShouldCapture);
        var third = sampler.Evaluate("{}", [], parseSucceeded: true);

        Assert.True(third.ShouldCapture);
        Assert.False(third.Important);
        Assert.Equal("routine_sample", third.Reason);
    }

    [Fact]
    public void Sampler_AlwaysCapturesImportantFramesWhenRoutineSamplingIsDisabled()
    {
        var sampler = new MarketWebSocketFrameDiagnosticSampler(new MarketDataWebSocketOptions
        {
            CriticalFrameDiagnosticSampleEvery = 0
        });
        var resolvedUpdate = new MarketDataUpdate(
            MarketDataEventType.MarketResolved,
            "market_resolved",
            "asset-1",
            "condition-1",
            null,
            null,
            null,
            null,
            null,
            TradeSide.Unknown,
            true,
            DateTimeOffset.UtcNow);
        var bulkUpdates = Enumerable.Repeat(resolvedUpdate with
        {
            EventType = MarketDataEventType.BestBidAsk,
            RawEventType = "best_bid_ask",
            MarketResolved = false
        }, 100).ToArray();

        Assert.True(sampler.Evaluate("not-json", null, parseSucceeded: false).Important);
        Assert.True(sampler.Evaluate("PONG", [], parseSucceeded: true).Important);
        Assert.True(sampler.Evaluate("{}", [resolvedUpdate], parseSucceeded: true).Important);
        Assert.True(sampler.Evaluate("[]", bulkUpdates, parseSucceeded: true).Important);
        Assert.False(sampler.Evaluate("{}", [], parseSucceeded: true).ShouldCapture);
    }

    [Fact]
    public void Build_ExtractsEventTypesAssetIdsAndResolvedFlags()
    {
        var receivedAtUtc = new DateTimeOffset(2026, 6, 8, 12, 15, 0, TimeSpan.Zero);
        var message = "[" + BestBidAskJson + "," + MarketResolvedJson + "]";
        var updates = PolymarketMarketDataWebSocketParser.ParseMarketMessage(message);

        var diagnostic = MarketWebSocketFrameDiagnosticBuilder.Build(
            CriticalCryptoUpDown5mAssetSelector.ComponentName,
            message,
            receivedAtUtc,
            updates,
            parseSucceeded: true,
            parseError: null);

        Assert.Equal(CriticalCryptoUpDown5mAssetSelector.ComponentName, diagnostic.Component);
        Assert.Equal("JsonArray", diagnostic.FrameKind);
        Assert.Equal(message.Length, diagnostic.PayloadLengthChars);
        Assert.Equal(2, diagnostic.EventCount);
        Assert.True(diagnostic.ContainsMarketResolvedText);
        Assert.True(diagnostic.ContainsResolvedText);
        Assert.True(diagnostic.ParseSucceeded);
        Assert.Equal(3, diagnostic.ParsedUpdateCount);
        Assert.False(diagnostic.RawPayloadTruncated);
        Assert.Equal(message, diagnostic.RawPayload);
        Assert.Contains("best_bid_ask", ReadJsonArray(diagnostic.EventTypesJson));
        Assert.Contains("market_resolved", ReadJsonArray(diagnostic.EventTypesJson));
        Assert.Contains("asset-best", ReadJsonArray(diagnostic.AssetIdsJson));
        Assert.Contains("token-yes", ReadJsonArray(diagnostic.AssetIdsJson));
        Assert.Contains("token-no", ReadJsonArray(diagnostic.AssetIdsJson));
        Assert.Contains("0xcondition", ReadJsonArray(diagnostic.MarketIdsJson));
        Assert.Equal(64, diagnostic.PayloadSha256.Length);
    }

    [Fact]
    public void Build_MarksPongWithoutJsonParseDetails()
    {
        var diagnostic = MarketWebSocketFrameDiagnosticBuilder.Build(
            CriticalCryptoUpDown5mAssetSelector.ComponentName,
            "PONG",
            DateTimeOffset.UtcNow,
            [],
            parseSucceeded: true,
            parseError: null);

        Assert.Equal("Pong", diagnostic.FrameKind);
        Assert.Equal(0, diagnostic.EventCount);
        Assert.Empty(ReadJsonArray(diagnostic.EventTypesJson));
        Assert.Empty(ReadJsonArray(diagnostic.AssetIdsJson));
        Assert.True(diagnostic.ParseSucceeded);
        Assert.Equal(0, diagnostic.ParsedUpdateCount);
    }

    [Fact]
    public void Build_MarksInvalidJsonAndTruncatesLargePayload()
    {
        var message = "{\"event_type\":\"book\",\"asset_id\":\"asset-1\"," +
            new string('x', MarketWebSocketFrameDiagnosticBuilder.MaxRawPayloadChars + 10);

        var diagnostic = MarketWebSocketFrameDiagnosticBuilder.Build(
            CriticalCryptoUpDown5mAssetSelector.ComponentName,
            message,
            DateTimeOffset.UtcNow,
            null,
            parseSucceeded: false,
            parseError: "invalid json");

        Assert.Equal("InvalidJson", diagnostic.FrameKind);
        Assert.False(diagnostic.ParseSucceeded);
        Assert.Equal("invalid json", diagnostic.ParseError);
        Assert.Equal(message.Length, diagnostic.PayloadLengthChars);
        Assert.True(diagnostic.RawPayloadTruncated);
        Assert.Equal(MarketWebSocketFrameDiagnosticBuilder.MaxRawPayloadChars, diagnostic.RawPayload.Length);
    }

    private static string[] ReadJsonArray(string json)
    {
        return JsonSerializer.Deserialize<string[]>(json) ?? [];
    }

    private const string BestBidAskJson = """
{
  "event_type": "best_bid_ask",
  "market": "0xmarket",
  "asset_id": "asset-best",
  "best_bid": "0.73",
  "best_ask": "0.77",
  "timestamp": "1766789469958"
}
""";

    private const string MarketResolvedJson = """
{
  "event_type": "market_resolved",
  "id": "1031769",
  "market": "0xcondition",
  "assets_ids": [
    "token-yes",
    "token-no"
  ],
  "winning_asset_id": "token-yes",
  "winning_outcome": "Yes",
  "timestamp": "1766790415550"
}
""";
}
