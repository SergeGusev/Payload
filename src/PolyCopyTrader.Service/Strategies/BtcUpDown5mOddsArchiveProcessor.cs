using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class BtcUpDown5mOddsArchiveProcessor(
    ILogger<BtcUpDown5mOddsArchiveProcessor> logger,
    BtcUpDown5mOddsArchiveOptions options,
    IAppRepository repository,
    IMarketDataCache marketDataCache,
    IPolymarketClobPublicClient clobClient,
    IBtcUsdReferencePriceClient btcUsdReferencePriceClient) : IBtcUpDown5mOddsArchiveProcessor
{
    private const string WebSocketCacheSource = "websocket_cache";
    private const string ClobRestSource = "clob_rest";
    private const string MissingSource = "missing";

    public async Task<BtcUpDown5mOddsArchiveCycleResult> ProcessAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return new BtcUpDown5mOddsArchiveCycleResult(0, 0, 0, 0, 0);
        }

        var sampledAtUtc = DateTimeOffset.UtcNow;
        BtcUsdReferencePricePoint btcPrice;
        try
        {
            btcPrice = await btcUsdReferencePriceClient.GetBtcUsdPriceAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "BTC Up or Down 5m odds archive skipped because BTC reference price is unavailable.");
            return new BtcUpDown5mOddsArchiveCycleResult(0, 0, 1, 0, 0);
        }

        var markets = await repository.GetBtcUpDown5mGammaMarketsAsync(options.MaxMarketsPerCycle, cancellationToken);
        var ticksStored = 0;
        var skippedNoOutcomeTokens = 0;
        var missingBothBooks = 0;

        foreach (var market in markets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetActiveWindow(market, sampledAtUtc, out var marketStartUtc, out var marketEndUtc) ||
                !TryGetOutcomeTokens(market, out var tokens))
            {
                if (TryGetActiveWindow(market, sampledAtUtc, out _, out _))
                {
                    skippedNoOutcomeTokens++;
                }

                continue;
            }

            var upBook = await ResolveBookAsync(tokens.UpAssetId, cancellationToken);
            var downBook = await ResolveBookAsync(tokens.DownAssetId, cancellationToken);
            var upPrice = BuildOutcomeBookPrice(upBook);
            var downPrice = BuildOutcomeBookPrice(downBook);
            if (upPrice.PriceProxy is null && downPrice.PriceProxy is null)
            {
                missingBothBooks++;
            }

            var startPrice = await repository.GetBtcUpDown5mOddsStartPriceAsync(market.MarketId, cancellationToken)
                ?? btcPrice.PriceUsd;
            var moveUsd = btcPrice.PriceUsd - startPrice;
            var moveBps = startPrice == 0m ? 0m : moveUsd / startPrice * 10_000m;
            var diagnosticsJson = JsonSerializer.Serialize(new
            {
                market_active = market.Active,
                market_closed = market.Closed,
                market_archived = market.Archived,
                market_accepting_orders = market.AcceptingOrders,
                market_enable_order_book = market.EnableOrderBook,
                up_book_status = upBook.Status,
                up_book_source = upBook.Source,
                up_book_error = upBook.Error,
                down_book_status = downBook.Status,
                down_book_source = downBook.Source,
                down_book_error = downBook.Error,
                btc_source = btcPrice.Source
            });

            var tick = new BtcUpDown5mOddsTick(
                Guid.NewGuid(),
                market.MarketId,
                market.ConditionId,
                market.Slug,
                marketStartUtc,
                marketEndUtc,
                sampledAtUtc,
                ToDecimalSeconds(sampledAtUtc - marketStartUtc),
                ToDecimalSeconds(marketEndUtc - sampledAtUtc),
                btcPrice.PriceUsd,
                btcPrice.SourceUpdatedAtUtc,
                btcPrice.FetchedAtUtc,
                startPrice,
                moveUsd,
                moveBps,
                tokens.UpAssetId,
                upPrice.BestBid,
                upPrice.BestAsk,
                upPrice.Mid,
                upPrice.PriceProxy,
                upPrice.PriceProxyKind,
                upPrice.LastTradePrice,
                upBook.Source,
                upBook.AgeMs,
                tokens.DownAssetId,
                downPrice.BestBid,
                downPrice.BestAsk,
                downPrice.Mid,
                downPrice.PriceProxy,
                downPrice.PriceProxyKind,
                downPrice.LastTradePrice,
                downBook.Source,
                downBook.AgeMs,
                diagnosticsJson,
                DateTimeOffset.UtcNow);

            await repository.AddBtcUpDown5mOddsTickAsync(tick, cancellationToken);
            ticksStored++;
        }

        return new BtcUpDown5mOddsArchiveCycleResult(
            markets.Count,
            ticksStored,
            0,
            skippedNoOutcomeTokens,
            missingBothBooks);
    }

    private async Task<OrderBookLookupResult> ResolveBookAsync(string assetId, CancellationToken cancellationToken)
    {
        var maxAge = TimeSpan.FromMilliseconds(options.MaxOrderBookAgeMilliseconds);
        var lookup = marketDataCache.GetOrderBook(assetId, maxAge);
        if (lookup is { Status: OrderBookCacheLookupStatus.Fresh, Snapshot: { } snapshot })
        {
            return OrderBookLookupResult.Found(
                NormalizeOrderBook(assetId, snapshot),
                WebSocketCacheSource,
                lookup.Age);
        }

        if (!options.RestFallbackEnabled)
        {
            return OrderBookLookupResult.Missing(
                lookup.Status.ToString(),
                lookup.Snapshot is null ? MissingSource : "stale_websocket_cache",
                null,
                lookup.Age);
        }

        try
        {
            var fetched = await clobClient.GetOrderBookAsync(assetId, cancellationToken);
            if (fetched is null)
            {
                return OrderBookLookupResult.Missing("clob_rest_empty", ClobRestSource, null, lookup.Age);
            }

            var normalized = NormalizeOrderBook(assetId, fetched);
            return OrderBookLookupResult.Found(
                normalized,
                ClobRestSource,
                DateTimeOffset.UtcNow - normalized.SnapshotAtUtc);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "BTC Up or Down 5m odds archive CLOB /book fallback failed. AssetId={AssetId}", assetId);
            return OrderBookLookupResult.Missing(lookup.Status.ToString(), "clob_rest_error", ex.Message, lookup.Age);
        }
    }

    private static bool TryGetActiveWindow(
        PolymarketGammaMarket market,
        DateTimeOffset nowUtc,
        out DateTimeOffset marketStartUtc,
        out DateTimeOffset marketEndUtc)
    {
        marketStartUtc = default;
        marketEndUtc = default;

        if (!BtcUpDown5mMarketAnalyzer.IsCandidate(market) ||
            !market.Active ||
            market.Closed ||
            market.Archived)
        {
            return false;
        }

        var start = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        if (start is null)
        {
            return false;
        }

        var end = market.EndDateUtc ?? start.Value.AddMinutes(5);
        if (nowUtc < start.Value || nowUtc > end)
        {
            return false;
        }

        marketStartUtc = start.Value;
        marketEndUtc = end;
        return true;
    }

    private static bool TryGetOutcomeTokens(PolymarketGammaMarket market, out OutcomeTokens tokens)
    {
        tokens = new OutcomeTokens(string.Empty, string.Empty);
        if (market.Outcomes.Count == 0 || market.Outcomes.Count != market.ClobTokenIds.Count)
        {
            return false;
        }

        string? upAssetId = null;
        string? downAssetId = null;
        for (var index = 0; index < market.Outcomes.Count; index++)
        {
            var outcome = market.Outcomes[index];
            var assetId = market.ClobTokenIds[index];
            if (string.IsNullOrWhiteSpace(assetId))
            {
                continue;
            }

            if (string.Equals(outcome, "Up", StringComparison.OrdinalIgnoreCase))
            {
                upAssetId = assetId;
            }
            else if (string.Equals(outcome, "Down", StringComparison.OrdinalIgnoreCase))
            {
                downAssetId = assetId;
            }
        }

        if (string.IsNullOrWhiteSpace(upAssetId) || string.IsNullOrWhiteSpace(downAssetId))
        {
            return false;
        }

        tokens = new OutcomeTokens(upAssetId, downAssetId);
        return true;
    }

    private static OutcomeBookPrice BuildOutcomeBookPrice(OrderBookLookupResult result)
    {
        var orderBook = result.OrderBook;
        var bestBid = orderBook?.BestBid;
        var bestAsk = orderBook?.BestAsk;
        var mid = bestBid is { } bid && bestAsk is { } ask
            ? (bid + ask) / 2m
            : (decimal?)null;

        if (mid is { } midValue)
        {
            return new OutcomeBookPrice(bestBid, bestAsk, midValue, midValue, "mid", orderBook?.LastTradePrice);
        }

        if (bestBid is { } bidOnly)
        {
            return new OutcomeBookPrice(bestBid, bestAsk, null, bidOnly, "bid_only", orderBook?.LastTradePrice);
        }

        if (bestAsk is { } askOnly)
        {
            return new OutcomeBookPrice(bestBid, bestAsk, null, askOnly, "ask_only", orderBook?.LastTradePrice);
        }

        return new OutcomeBookPrice(bestBid, bestAsk, null, null, "missing", orderBook?.LastTradePrice);
    }

    private static OrderBookSnapshot NormalizeOrderBook(string requestedAssetId, OrderBookSnapshot orderBook)
    {
        return string.IsNullOrWhiteSpace(orderBook.AssetId) ||
            !string.Equals(orderBook.AssetId, requestedAssetId, StringComparison.OrdinalIgnoreCase)
            ? orderBook with { AssetId = requestedAssetId }
            : orderBook;
    }

    private static decimal ToDecimalSeconds(TimeSpan value)
    {
        return Convert.ToDecimal(value.TotalSeconds);
    }

    private sealed record OutcomeTokens(string UpAssetId, string DownAssetId);

    private sealed record OutcomeBookPrice(
        decimal? BestBid,
        decimal? BestAsk,
        decimal? Mid,
        decimal? PriceProxy,
        string PriceProxyKind,
        decimal? LastTradePrice);

    private sealed record OrderBookLookupResult(
        OrderBookSnapshot? OrderBook,
        string Status,
        string Source,
        decimal? AgeMs,
        string? Error)
    {
        public static OrderBookLookupResult Found(OrderBookSnapshot orderBook, string source, TimeSpan? age)
        {
            return new OrderBookLookupResult(orderBook, "found", source, ToAgeMs(age), null);
        }

        public static OrderBookLookupResult Missing(string status, string source, string? error, TimeSpan? age)
        {
            return new OrderBookLookupResult(null, status, source, ToAgeMs(age), error);
        }

        private static decimal? ToAgeMs(TimeSpan? age)
        {
            if (age is null)
            {
                return null;
            }

            return Convert.ToDecimal(Math.Max(0d, age.Value.TotalMilliseconds));
        }
    }
}
