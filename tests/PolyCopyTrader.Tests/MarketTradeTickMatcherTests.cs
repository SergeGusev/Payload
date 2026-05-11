using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Tests;

public sealed class MarketTradeTickMatcherTests
{
    [Fact]
    public void Match_FindsTraderByTransactionHash()
    {
        var tick = BuildTick(transactionHash: "0xabc");
        var trade = BuildTrade(transactionHash: "0xAbC");

        var result = MarketTradeTickMatcher.Match(tick, [trade], TimeSpan.FromSeconds(5));

        Assert.Equal(TradeTickTraderMatchStatus.FoundByTransactionHash, result.Status);
        Assert.Equal("0xwallet", result.Trade?.TraderWallet);
    }

    [Fact]
    public void Match_FindsTraderByCompositeFieldsWhenTransactionHashIsMissing()
    {
        var tradeTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(1_757_908_892_351);
        var tick = BuildTick(transactionHash: null, tradeTimestampUtc: tradeTimestamp);
        var trade = BuildTrade(transactionHash: "0xdef", timestampUtc: tradeTimestamp.AddSeconds(3));

        var result = MarketTradeTickMatcher.Match(tick, [trade], TimeSpan.FromSeconds(5));

        Assert.Equal(TradeTickTraderMatchStatus.FoundByComposite, result.Status);
        Assert.Equal("0xwallet", result.Trade?.TraderWallet);
    }

    [Fact]
    public void Match_ReturnsNotFoundWhenTradeDoesNotMatch()
    {
        var tick = BuildTick(transactionHash: "0xmissing");
        var trade = BuildTrade(transactionHash: "0xdef", price: 0.44m);

        var result = MarketTradeTickMatcher.Match(tick, [trade], TimeSpan.FromSeconds(5));

        Assert.Equal(TradeTickTraderMatchStatus.NotFound, result.Status);
        Assert.Null(result.Trade);
    }

    [Fact]
    public void Match_NarrowsTransactionHashRowsWithSmallDataApiPriceDrift()
    {
        var tradeTimestamp = DateTimeOffset.FromUnixTimeSeconds(1_777_804_928);
        var tick = BuildTick(transactionHash: "0xabc", tradeTimestampUtc: tradeTimestamp) with
        {
            Price = 0.72m,
            Size = 1.597221m
        };
        var matchingTrade = BuildTrade(
            transactionHash: "0xabc",
            timestampUtc: tradeTimestamp,
            price: 0.7199999248695077m,
            size: 1.597221m,
            wallet: "0xwallet-match");
        var otherTrade = BuildTrade(
            transactionHash: "0xabc",
            timestampUtc: tradeTimestamp,
            assetId: "asset-2",
            price: 0.2800000751304923m,
            size: 1.597221m,
            wallet: "0xwallet-other");

        var result = MarketTradeTickMatcher.Match(tick, [matchingTrade, otherTrade], TimeSpan.FromSeconds(5));

        Assert.Equal(TradeTickTraderMatchStatus.FoundByTransactionHash, result.Status);
        Assert.Equal("0xwallet-match", result.Trade?.TraderWallet);
        Assert.Equal("transaction_hash_composite_narrowed:2", result.Details);
    }

    [Fact]
    public void Match_NarrowsTransactionHashRowsWithoutPriceWhenAssetSideSizeAndTimeAreUnique()
    {
        var tradeTimestamp = DateTimeOffset.FromUnixTimeSeconds(1_777_804_926);
        var tick = BuildTick(transactionHash: "0xabc", tradeTimestampUtc: tradeTimestamp) with
        {
            Price = 0.8m,
            Size = 706.44864m
        };
        var matchingTrade = BuildTrade(
            transactionHash: "0xabc",
            timestampUtc: tradeTimestamp,
            price: 0.8026061144374204m,
            size: 706.44864m,
            wallet: "0xwallet-match");
        var otherTrade = BuildTrade(
            transactionHash: "0xabc",
            timestampUtc: tradeTimestamp,
            side: TradeSide.Sell,
            price: 0.8099999978273698m,
            size: 184.10864m,
            wallet: "0xwallet-other");

        var result = MarketTradeTickMatcher.Match(tick, [matchingTrade, otherTrade], TimeSpan.FromSeconds(5));

        Assert.Equal(TradeTickTraderMatchStatus.FoundByTransactionHash, result.Status);
        Assert.Equal("0xwallet-match", result.Trade?.TraderWallet);
        Assert.Equal("transaction_hash_asset_side_size_time_narrowed:2", result.Details);
    }

    [Fact]
    public async Task PagedLookup_ScansOffsetsUntilTransactionHashIsFound()
    {
        var tick = BuildTick(transactionHash: "0xtarget");
        var matchingTrade = BuildTrade(transactionHash: "0xtarget");
        var api = new PagedDataApiClient
        {
            Pages =
            {
                [0] = [BuildTrade(transactionHash: "0xother-0")],
                [1000] = [BuildTrade(transactionHash: "0xother-1")],
                [2000] = [matchingTrade]
            }
        };

        var result = await MarketTradeTickPagedLookup.MatchAsync(
            tick,
            api,
            new MarketTradeDiagnosticsOptions { MarketTradesLimit = 1000 },
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(TradeTickTraderMatchStatus.FoundByTransactionHash, result.Status);
        Assert.Equal("0xwallet", result.Trade?.TraderWallet);
        Assert.Equal([0, 1000, 2000], api.Offsets);
        Assert.Contains("offset:2000", result.Details, StringComparison.Ordinal);
        Assert.Contains("pages_scanned:3", result.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PagedLookup_StopsAtHistoricalOffsetLimit()
    {
        var tick = BuildTick(transactionHash: "0xtarget");
        var api = new PagedDataApiClient
        {
            OffsetLimitErrorAt = 3000,
            Pages =
            {
                [0] = [BuildTrade(transactionHash: "0xother-0")],
                [1000] = [BuildTrade(transactionHash: "0xother-1")],
                [2000] = [BuildTrade(transactionHash: "0xother-2")]
            }
        };

        var result = await MarketTradeTickPagedLookup.MatchAsync(
            tick,
            api,
            new MarketTradeDiagnosticsOptions { MarketTradesLimit = 1000 },
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(TradeTickTraderMatchStatus.NotFound, result.Status);
        Assert.Equal([0, 1000, 2000, 3000], api.Offsets);
        Assert.Contains("history_offset_limit_reached:3000", result.Details, StringComparison.Ordinal);
        Assert.Contains("pages_scanned:3", result.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiagnosticService_RecordsLastTradeTickWithInitialNotFoundStatus()
    {
        var repository = new TestAppRepository();
        var service = new MarketTradeTickDiagnosticService(
            NullLogger<MarketTradeTickDiagnosticService>.Instance,
            new MarketTradeDiagnosticsOptions { Enabled = true },
            repository);

        await service.RecordAsync(new MarketDataUpdate(
            MarketDataEventType.LastTradePrice,
            "last_trade_price",
            "asset-1",
            "condition-1",
            null,
            null,
            null,
            0.45m,
            10m,
            TradeSide.Buy,
            false,
            DateTimeOffset.FromUnixTimeMilliseconds(1_757_908_892_351),
            "0xabc",
            """{"event_type":"last_trade_price"}"""));

        var tick = Assert.Single(repository.PolymarketWebSocketTradeTicks);
        Assert.Equal(TradeTickTraderMatchStatus.NotFound, tick.TraderMatchStatus);
        Assert.True(tick.TransactionHashPresent);
        Assert.Equal("0xabc", tick.TransactionHash);
        Assert.Null(tick.TraderWallet);
        Assert.Equal(0, tick.MatchAttempts);
        Assert.Equal("""{"event_type":"last_trade_price"}""", tick.RawJson);
    }

    private static PolymarketWebSocketTradeTick BuildTick(
        string? transactionHash,
        DateTimeOffset? tradeTimestampUtc = null)
    {
        var timestamp = tradeTimestampUtc ?? DateTimeOffset.FromUnixTimeMilliseconds(1_757_908_892_351);
        return new PolymarketWebSocketTradeTick(
            Guid.NewGuid(),
            Guid.NewGuid().ToString("N"),
            "asset-1",
            "condition-1",
            TradeSide.Buy,
            0.45m,
            10m,
            timestamp,
            transactionHash,
            !string.IsNullOrWhiteSpace(transactionHash),
            TradeTickTraderMatchStatus.NotFound,
            null,
            timestamp,
            null,
            0,
            null,
            null,
            null,
            null,
            "{}",
            timestamp);
    }

    private static LeaderTrade BuildTrade(
        string? transactionHash,
        DateTimeOffset? timestampUtc = null,
        decimal price = 0.45m,
        decimal size = 10m,
        string assetId = "asset-1",
        TradeSide side = TradeSide.Buy,
        string wallet = "0xwallet")
    {
        return new LeaderTrade(
            wallet,
            "Trader",
            "condition-1",
            assetId,
            "sample-market",
            "Sample market",
            "Yes",
            side,
            price,
            size,
            price * size,
            timestampUtc ?? DateTimeOffset.FromUnixTimeMilliseconds(1_757_908_892_351),
            transactionHash);
    }

    private sealed class EmptyDataApiClient : IPolymarketDataApiClient
    {
        public Task<IReadOnlyList<TraderLeaderboardEntry>> GetTraderLeaderboardAsync(
            string category = "OVERALL",
            string timePeriod = "DAY",
            string orderBy = "PNL",
            int limit = 25,
            int offset = 0,
            string? user = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TraderLeaderboardEntry>>([]);
        }

        public Task<IReadOnlyList<LeaderTrade>> GetUserTradesAsync(
            string wallet,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderTrade>>([]);
        }

        public Task<IReadOnlyList<LeaderTrade>> GetMarketTradesAsync(
            string conditionId,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderTrade>>([]);
        }

        public Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
            string wallet,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderPosition>>([]);
        }
    }

    private sealed class PagedDataApiClient : IPolymarketDataApiClient
    {
        public Dictionary<int, IReadOnlyList<LeaderTrade>> Pages { get; } = [];

        public List<int> Offsets { get; } = [];

        public int? OffsetLimitErrorAt { get; init; }

        public Task<IReadOnlyList<TraderLeaderboardEntry>> GetTraderLeaderboardAsync(
            string category = "OVERALL",
            string timePeriod = "DAY",
            string orderBy = "PNL",
            int limit = 25,
            int offset = 0,
            string? user = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TraderLeaderboardEntry>>([]);
        }

        public Task<IReadOnlyList<LeaderTrade>> GetUserTradesAsync(
            string wallet,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderTrade>>([]);
        }

        public Task<IReadOnlyList<LeaderTrade>> GetMarketTradesAsync(
            string conditionId,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            Offsets.Add(offset);
            if (OffsetLimitErrorAt is { } limitAt && offset >= limitAt)
            {
                throw new PolymarketApiException(
                    "test",
                    "GetMarketTrades",
                    """GetMarketTrades failed with HTTP 400 Bad Request. Body: {"error":"max historical activity offset of 3000 exceeded"}""");
            }

            return Task.FromResult(Pages.TryGetValue(offset, out var trades)
                ? trades
                : Array.Empty<LeaderTrade>());
        }

        public Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
            string wallet,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderPosition>>([]);
        }
    }
}
