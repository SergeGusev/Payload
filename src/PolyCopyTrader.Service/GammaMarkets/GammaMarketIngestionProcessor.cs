using System.Globalization;
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
    private const int Crypto5mPriorityLookBehindWindows = 1;
    private const int Crypto5mPriorityLookAheadWindows = 24;
    private const long FiveMinuteWindowSeconds = 300;
    private static readonly string[] Crypto5mPriorityAssetSymbols = ["BTC", "ETH", "SOL"];
    private static readonly IReadOnlySet<string> Crypto5mSubscriptionAssetSymbols = new HashSet<string>(
        Crypto5mPriorityAssetSymbols,
        StringComparer.OrdinalIgnoreCase);

    public async Task<GammaMarketIngestionResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var offset = 0;
        var pagesFetched = 0;
        var marketsFetched = 0;
        var marketsUpserted = 0;
        var activeAssetIdsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var priorityMarkets = await SyncPriorityCryptoUpDown5mMarketsAsync(activeAssetIdsSeen, cancellationToken);
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

    private async Task<int> SyncPriorityCryptoUpDown5mMarketsAsync(
        HashSet<string> activeAssetIdsSeen,
        CancellationToken cancellationToken)
    {
        var assetSymbols = GetPriorityCryptoUpDown5mAssetSymbols();
        var slugs = BuildCryptoUpDown5mSlugs(
            DateTimeOffset.UtcNow,
            assetSymbols,
            Crypto5mPriorityLookBehindWindows,
            Crypto5mPriorityLookAheadWindows);
        var markets = await gammaClient.GetMarketsBySlugsAsync(slugs, activeOnly: true, cancellationToken);
        var cryptoMarkets = markets
            .Where(market => IsCryptoUpDown5mSubscriptionCandidate(market, assetSymbols))
            .ToArray();
        if (cryptoMarkets.Length == 0)
        {
            return 0;
        }

        var subscriptionMarkets = SelectSubscriptionMarkets(cryptoMarkets);
        AddSeenActiveAssetIds(activeAssetIdsSeen, subscriptionMarkets);
        var registryUpdate = activeMarketAssetSubscriptionRegistry.AddOrUpdateMarkets(subscriptionMarkets);
        if (registryUpdate.Added > 0)
        {
            logger.LogInformation(
                "Gamma active market ingestion registered priority crypto 5m WebSocket assets before full scan. NewAssets={NewAssets} SubscriptionScope={SubscriptionScope}",
                registryUpdate.Added,
                marketDataWebSocketOptions.SubscriptionScope);
        }

        foreach (var market in cryptoMarkets)
        {
            await repository.UpsertPolymarketGammaMarketAsync(market, cancellationToken);
        }

        logger.LogInformation(
            "Gamma active market ingestion priority crypto 5m sync completed. Assets={Assets} Slugs={Slugs} Markets={Markets}",
            string.Join(",", assetSymbols),
            slugs.Count,
            cryptoMarkets.Length);

        return cryptoMarkets.Length;
    }

    private static IReadOnlyList<string> BuildCryptoUpDown5mSlugs(
        DateTimeOffset nowUtc,
        IReadOnlyCollection<string> assetSymbols,
        int lookBehindWindows,
        int lookAheadWindows)
    {
        if (lookBehindWindows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lookBehindWindows), "Look-behind windows must be non-negative.");
        }

        if (lookAheadWindows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lookAheadWindows), "Look-ahead windows must be non-negative.");
        }

        var unixSeconds = nowUtc.ToUnixTimeSeconds();
        var floorUnixSeconds = unixSeconds - (unixSeconds % FiveMinuteWindowSeconds);
        var slugs = new List<string>(assetSymbols.Count * (lookBehindWindows + lookAheadWindows + 1));
        foreach (var assetSymbol in assetSymbols)
        {
            var assetSlugPrefix = assetSymbol.Trim().ToLowerInvariant();
            for (var offset = -lookBehindWindows; offset <= lookAheadWindows; offset++)
            {
                var windowUnixSeconds = floorUnixSeconds + (offset * FiveMinuteWindowSeconds);
                slugs.Add(assetSlugPrefix + "-updown-5m-" + windowUnixSeconds.ToString(CultureInfo.InvariantCulture));
            }
        }

        return slugs;
    }

    private IReadOnlyList<string> GetPriorityCryptoUpDown5mAssetSymbols()
    {
        return marketDataWebSocketOptions.SubscriptionScope == MarketDataWebSocketSubscriptionScope.BtcUpDown5mOnly
            ? ["BTC"]
            : Crypto5mPriorityAssetSymbols;
    }

    private static bool IsCryptoUpDown5mSubscriptionCandidate(
        PolymarketGammaMarket market,
        IReadOnlyCollection<string>? assetSymbols = null)
    {
        return CryptoUpDown5mMarketAnalyzer.TryGetAssetSymbol(
                market,
                ToAssetSymbolSet(assetSymbols),
                out _) &&
            CryptoUpDown5mMarketAnalyzer.GetMarketInterval(market) == BtcUpDownMarketInterval.FiveMinutes;
    }

    private static IReadOnlySet<string> ToAssetSymbolSet(IReadOnlyCollection<string>? assetSymbols)
    {
        if (assetSymbols is IReadOnlySet<string> assetSymbolSet)
        {
            return assetSymbolSet;
        }

        return assetSymbols is null
            ? Crypto5mSubscriptionAssetSymbols
            : new HashSet<string>(assetSymbols, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBtcUpDown5mSubscriptionCandidate(PolymarketGammaMarket market)
    {
        return BtcUpDown5mMarketAnalyzer.IsCandidate(market);
    }

    private static bool IsGammaActiveMarketsMaxOffset(PolymarketApiException ex)
    {
        return string.Equals(ex.Component, "PolymarketGammaClient", StringComparison.Ordinal) &&
            string.Equals(ex.Operation, "GetActiveMarkets", StringComparison.Ordinal) &&
            ex.Message.Contains("HTTP 422", StringComparison.OrdinalIgnoreCase) &&
            (ex.Message.Contains("offset exceeds maximum allowed", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("offset too large", StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyCollection<PolymarketGammaMarket> SelectSubscriptionMarkets(
        IReadOnlyCollection<PolymarketGammaMarket> markets)
    {
        return marketDataWebSocketOptions.SubscriptionScope switch
        {
            MarketDataWebSocketSubscriptionScope.AllActiveMarkets => markets,
            MarketDataWebSocketSubscriptionScope.BtcUpDown5mOnly => markets
                .Where(IsBtcUpDown5mSubscriptionCandidate)
                .ToArray(),
            MarketDataWebSocketSubscriptionScope.CryptoUpDown5mOnly => markets
                .Where(market => IsCryptoUpDown5mSubscriptionCandidate(market))
                .ToArray(),
            _ => []
        };
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
