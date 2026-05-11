using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Tests;

public sealed class BtcUpDown5mOrderBookRefreshWorkerTests
{
    [Fact]
    public async Task RefreshOnceAsync_RefreshesNearbyBtcOrderBooksWithLocalReceiveTime()
    {
        const string upAssetId = "up-token";
        const string downAssetId = "down-token";

        var nowUtc = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateBtcMarket(nowUtc, upAssetId, downAssetId));

        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        var staleExchangeTimestamp = nowUtc.AddSeconds(-30);
        var clobClient = new FakeClobPublicClient(staleExchangeTimestamp);
        var worker = new BtcUpDown5mOrderBookRefreshWorker(
            NullLogger<BtcUpDown5mOrderBookRefreshWorker>.Instance,
            new BtcUpDown5mStrategyOptions
            {
                OrderBookRefreshMaxMarketsPerCycle = 1,
                OrderBookRefreshMarketLookaheadSeconds = 120,
                OrderBookRefreshMarketBehindSeconds = 60,
                OrderBookRefreshRequestTimeoutSeconds = 1
            },
            clobClient,
            cache,
            registry,
            repository);

        var result = await worker.RefreshOnceAsync();

        Assert.Equal(1, result.SelectedMarkets);
        Assert.Equal(2, result.SelectedAssets);
        Assert.Equal(2, result.RefreshedAssets);
        Assert.Equal(0, result.MissingOrderBooks);
        Assert.Equal(0, result.FailedAssets);

        var lookup = cache.GetOrderBook(upAssetId, TimeSpan.FromSeconds(2));
        Assert.Equal(OrderBookCacheLookupStatus.Fresh, lookup.Status);
        Assert.NotNull(lookup.Snapshot);
        Assert.True(lookup.Snapshot.SnapshotAtUtc > staleExchangeTimestamp);
        Assert.NotNull(lookup.Age);
        Assert.True(lookup.Age.Value <= TimeSpan.FromSeconds(2));

        Assert.True(registry.TryGetSnapshot(upAssetId, out var registrySnapshot));
        Assert.NotNull(registrySnapshot.OrderBookUpdatedAtUtc);
        Assert.True(registrySnapshot.OrderBookUpdatedAtUtc > staleExchangeTimestamp);
    }

    private static PolymarketGammaMarket CreateBtcMarket(
        DateTimeOffset startUtc,
        string upAssetId,
        string downAssetId)
    {
        return new PolymarketGammaMarket(
            MarketId: "btc-market",
            ConditionId: "condition-id",
            QuestionId: "question-id",
            Slug: "btc-updown-5m-" + startUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            Question: "Bitcoin Up or Down - 5m",
            EventId: "event-id",
            EventSlug: "btc-updown-5m-" + startUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            EventTitle: "Bitcoin Up or Down - 5m",
            SeriesSlug: "btc-up-or-down-5m",
            Category: "Crypto",
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
            CreatedAtUtc: startUtc.AddMinutes(-1),
            UpdatedAtUtc: startUtc,
            StartDateUtc: startUtc,
            EndDateUtc: startUtc.AddMinutes(5),
            EventStartTimeUtc: startUtc,
            Outcomes: ["Up", "Down"],
            ClobTokenIds: [upAssetId, downAssetId],
            RawJson: "{\"outcomePrices\":[\"0.50\",\"0.50\"]}",
            FetchedAtUtc: startUtc,
            LastTradePrice: null,
            OrderMinSize: 5m,
            OrderPriceMinTickSize: 0.01m);
    }

    private sealed class FakeClobPublicClient(DateTimeOffset snapshotAtUtc) : IPolymarketClobPublicClient
    {
        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OrderBookSnapshot?>(new OrderBookSnapshot(
                assetId,
                [new OrderBookLevel(0.49m, 10m)],
                [new OrderBookLevel(0.51m, 10m)],
                snapshotAtUtc,
                "condition-id",
                5m,
                0.01m));
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(0.50m);
        }

        public Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(0.02m);
        }

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(
            string tokenId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PolymarketClobMarketByToken?>(null);
        }
    }
}
