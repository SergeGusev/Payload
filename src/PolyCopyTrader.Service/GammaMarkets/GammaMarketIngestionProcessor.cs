using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.GammaMarkets;

public sealed class GammaMarketIngestionProcessor(
    ILogger<GammaMarketIngestionProcessor> logger,
    GammaMarketIngestionOptions options,
    MarketDataWebSocketOptions marketDataWebSocketOptions,
    IPolymarketGammaClient gammaClient,
    IActiveMarketAssetSubscriptionRegistry activeMarketAssetSubscriptionRegistry,
    IAppRepository repository) : IGammaMarketIngestionProcessor
{
    private const int Btc5mPriorityLookBehindWindows = 1;
    private const int Btc5mPriorityLookAheadWindows = 24;

    public async Task<GammaMarketIngestionResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var offset = 0;
        var pagesFetched = 0;
        var marketsFetched = 0;
        var marketsUpserted = 0;
        var activeAssetIdsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var priorityMarkets = await SyncPriorityBtcUpDown5mMarketsAsync(activeAssetIdsSeen, cancellationToken);
        marketsFetched += priorityMarkets;
        marketsUpserted += priorityMarkets;

        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<PolymarketGammaMarket> markets;
            try
            {
                markets = await gammaClient.GetActiveMarketsAsync(
                    options.PageLimit,
                    offset,
                    cancellationToken);
            }
            catch (PolymarketApiException ex) when (IsGammaActiveMarketsMaxOffset(ex))
            {
                logger.LogInformation(
                    "Gamma active market ingestion reached the API maximum offset and completed the scan. Offset={Offset} PagesFetched={PagesFetched} MarketsFetched={MarketsFetched}",
                    offset,
                    pagesFetched,
                    marketsFetched);
                return CompleteFullScan(
                    activeAssetIdsSeen,
                    pagesFetched,
                    marketsFetched,
                    marketsUpserted,
                    offset);
            }

            pagesFetched++;
            marketsFetched += markets.Count;
            if (markets.Count == 0)
            {
                return CompleteFullScan(
                    activeAssetIdsSeen,
                    pagesFetched,
                    marketsFetched,
                    marketsUpserted,
                    offset);
            }

            var subscriptionMarkets = SelectSubscriptionMarkets(markets);
            AddSeenActiveAssetIds(activeAssetIdsSeen, subscriptionMarkets);
            var registryUpdate = activeMarketAssetSubscriptionRegistry.AddOrUpdateMarkets(subscriptionMarkets);
            if (registryUpdate.Added > 0)
            {
                logger.LogInformation(
                    "Gamma active market ingestion registered new WebSocket subscription assets before storage upsert. Offset={Offset} NewAssets={NewAssets} SubscriptionScope={SubscriptionScope}",
                    offset,
                    registryUpdate.Added,
                    marketDataWebSocketOptions.SubscriptionScope);
            }

            foreach (var market in markets)
            {
                await repository.UpsertPolymarketGammaMarketAsync(market, cancellationToken);
                marketsUpserted++;
            }

            logger.LogDebug(
                "Gamma active market ingestion page processed. Offset={Offset} Count={Count}",
                offset,
                markets.Count);

            offset += options.PageLimit;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new GammaMarketIngestionResult(
            pagesFetched,
            marketsFetched,
            marketsUpserted,
            ReachedEmptyPage: false,
            offset);
    }

    private GammaMarketIngestionResult CompleteFullScan(
        HashSet<string> activeAssetIdsSeen,
        int pagesFetched,
        int marketsFetched,
        int marketsUpserted,
        int nextOffset)
    {
        var retained = activeMarketAssetSubscriptionRegistry.RetainAssets(activeAssetIdsSeen);
        if (retained.Removed > 0)
        {
            logger.LogInformation(
                "Gamma active market ingestion removed inactive WebSocket subscription assets after a full scan. RemovedAssets={RemovedAssets} ActiveAssets={ActiveAssets}",
                retained.Removed,
                retained.TotalAssets);
        }

        return new GammaMarketIngestionResult(
            pagesFetched,
            marketsFetched,
            marketsUpserted,
            ReachedEmptyPage: true,
            nextOffset);
    }

    private async Task<int> SyncPriorityBtcUpDown5mMarketsAsync(
        HashSet<string> activeAssetIdsSeen,
        CancellationToken cancellationToken)
    {
        var slugs = BtcUpDown5mMarketAnalyzer.BuildFiveMinuteSlugs(
            DateTimeOffset.UtcNow,
            Btc5mPriorityLookBehindWindows,
            Btc5mPriorityLookAheadWindows);
        var markets = await gammaClient.GetMarketsBySlugsAsync(slugs, activeOnly: true, cancellationToken);
        var btcMarkets = markets
            .Where(BtcUpDown5mMarketAnalyzer.IsCandidate)
            .ToArray();
        if (btcMarkets.Length == 0)
        {
            return 0;
        }

        var subscriptionMarkets = SelectSubscriptionMarkets(btcMarkets);
        AddSeenActiveAssetIds(activeAssetIdsSeen, subscriptionMarkets);
        var registryUpdate = activeMarketAssetSubscriptionRegistry.AddOrUpdateMarkets(subscriptionMarkets);
        if (registryUpdate.Added > 0)
        {
            logger.LogInformation(
                "Gamma active market ingestion registered priority BTC 5m WebSocket assets before full scan. NewAssets={NewAssets}",
                registryUpdate.Added);
        }

        foreach (var market in btcMarkets)
        {
            await repository.UpsertPolymarketGammaMarketAsync(market, cancellationToken);
        }

        logger.LogInformation(
            "Gamma active market ingestion priority BTC 5m sync completed. Slugs={Slugs} Markets={Markets}",
            slugs.Count,
            btcMarkets.Length);

        return btcMarkets.Length;
    }

    private static bool IsGammaActiveMarketsMaxOffset(PolymarketApiException ex)
    {
        return string.Equals(ex.Component, "PolymarketGammaClient", StringComparison.Ordinal) &&
            string.Equals(ex.Operation, "GetActiveMarkets", StringComparison.Ordinal) &&
            ex.Message.Contains("HTTP 422", StringComparison.OrdinalIgnoreCase) &&
            ex.Message.Contains("offset exceeds maximum allowed", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyCollection<PolymarketGammaMarket> SelectSubscriptionMarkets(
        IReadOnlyCollection<PolymarketGammaMarket> markets)
    {
        if (marketDataWebSocketOptions.SubscriptionScope != MarketDataWebSocketSubscriptionScope.BtcUpDown5mOnly)
        {
            return markets;
        }

        return markets
            .Where(BtcUpDown5mMarketAnalyzer.IsCandidate)
            .ToArray();
    }

    private static void AddSeenActiveAssetIds(HashSet<string> assetIds, IReadOnlyCollection<PolymarketGammaMarket> markets)
    {
        foreach (var market in markets)
        {
            if (!market.Active || market.Closed)
            {
                continue;
            }

            foreach (var assetId in market.ClobTokenIds)
            {
                if (string.IsNullOrWhiteSpace(assetId))
                {
                    continue;
                }

                var trimmed = assetId.Trim();
                if (trimmed.Equals("0", StringComparison.Ordinal) ||
                    trimmed.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                assetIds.Add(trimmed);
            }
        }
    }
}
