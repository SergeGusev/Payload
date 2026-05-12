using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.MarketData;

public sealed class BtcUpDown5mOrderBookRefreshWorker(
    ILogger<BtcUpDown5mOrderBookRefreshWorker> logger,
    BtcUpDown5mStrategyOptions options,
    IPolymarketClobPublicClient clobClient,
    IMarketDataCache marketDataCache,
    IActiveMarketAssetSubscriptionRegistry activeMarketAssetSubscriptionRegistry,
    IAppRepository repository) : BackgroundService
{
    private const string ComponentName = "BtcUpDown5mOrderBookRefreshWorker";
    private const string Source = "btc_orderbook_refresh_worker";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !options.OrderBookRefreshWorkerEnabled)
        {
            logger.LogInformation("BTC Up or Down 5m order-book refresh worker disabled.");
            return;
        }

        logger.LogInformation(
            "BTC Up or Down 5m order-book refresh worker started. IntervalMs={IntervalMs}, MaxMarkets={MaxMarkets}, LookaheadSeconds={LookaheadSeconds}, BehindSeconds={BehindSeconds}",
            options.OrderBookRefreshIntervalMilliseconds,
            options.OrderBookRefreshMaxMarketsPerCycle,
            options.OrderBookRefreshMarketLookaheadSeconds,
            options.OrderBookRefreshMarketBehindSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await RefreshOnceAsync(stoppingToken);
                if (result.RefreshedAssets > 0 || result.MissingOrderBooks > 0 || result.FailedAssets > 0)
                {
                    logger.LogInformation(
                        "BTC 5m order-book refresh: markets={Markets}, assets={Assets}, refreshed={Refreshed}, missing={Missing}, failed={Failed}",
                        result.SelectedMarkets,
                        result.SelectedAssets,
                        result.RefreshedAssets,
                        result.MissingOrderBooks,
                        result.FailedAssets);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BTC 5m order-book refresh cycle failed.");
                await TryRecordApiErrorAsync("RefreshCycle", ex.Message, stoppingToken);
            }

            try
            {
                await Task.Delay(GetRefreshInterval(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<BtcUpDown5mOrderBookRefreshResult> RefreshOnceAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var markets = await repository.GetBtcUpDownStrategyGammaMarketsAsync(GetGammaMarketFetchLimit(), cancellationToken);
        var selectedMarkets = SelectRefreshMarkets(markets, nowUtc);
        if (selectedMarkets.Count == 0)
        {
            return new BtcUpDown5mOrderBookRefreshResult(0, 0, 0, 0, 0);
        }

        activeMarketAssetSubscriptionRegistry.AddOrUpdateMarkets(selectedMarkets);

        var assetIds = selectedMarkets
            .SelectMany(BtcUpDown5mMarketAnalyzer.GetOutcomeQuotes)
            .Select(quote => quote.AssetId)
            .Where(assetId => !string.IsNullOrWhiteSpace(assetId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var refreshed = 0;
        var missing = 0;
        var failed = 0;
        foreach (var assetId in assetIds)
        {
            var result = await RefreshAssetAsync(assetId, cancellationToken);
            refreshed += result == BtcUpDown5mOrderBookAssetRefreshStatus.Refreshed ? 1 : 0;
            missing += result == BtcUpDown5mOrderBookAssetRefreshStatus.Missing ? 1 : 0;
            failed += result == BtcUpDown5mOrderBookAssetRefreshStatus.Failed ? 1 : 0;
        }

        return new BtcUpDown5mOrderBookRefreshResult(
            selectedMarkets.Count,
            assetIds.Length,
            refreshed,
            missing,
            failed);
    }

    private async Task<BtcUpDown5mOrderBookAssetRefreshStatus> RefreshAssetAsync(
        string assetId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.OrderBookRefreshRequestTimeoutSeconds));

            var orderBook = await clobClient.GetOrderBookAsync(assetId, timeout.Token);
            if (orderBook is null)
            {
                return BtcUpDown5mOrderBookAssetRefreshStatus.Missing;
            }

            var receivedAtUtc = DateTimeOffset.UtcNow;
            var normalized = orderBook with
            {
                AssetId = string.IsNullOrWhiteSpace(orderBook.AssetId) ? assetId : orderBook.AssetId,
                SnapshotAtUtc = receivedAtUtc
            };

            var update = new MarketDataUpdate(
                MarketDataEventType.Book,
                Source,
                normalized.AssetId,
                normalized.ConditionId,
                normalized,
                normalized.BestBid,
                normalized.BestAsk,
                null,
                null,
                TradeSide.Unknown,
                false,
                receivedAtUtc);

            marketDataCache.ApplyUpdate(update);
            activeMarketAssetSubscriptionRegistry.ApplyMarketDataUpdate(update);
            return BtcUpDown5mOrderBookAssetRefreshStatus.Refreshed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(
                ex,
                "BTC 5m order-book refresh timed out for asset {AssetId}.",
                assetId);
            await TryRecordApiErrorAsync("RefreshOrderBook", $"Asset {assetId}: request timed out.", cancellationToken);
            return BtcUpDown5mOrderBookAssetRefreshStatus.Failed;
        }
        catch (PolymarketApiException ex) when (IsMissingOrderBook(ex))
        {
            logger.LogDebug(
                "BTC 5m order-book refresh found no order book for asset {AssetId}: {Message}",
                assetId,
                ex.Message);
            return BtcUpDown5mOrderBookAssetRefreshStatus.Missing;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "BTC 5m order-book refresh failed for asset {AssetId}.",
                assetId);
            await TryRecordApiErrorAsync("RefreshOrderBook", $"Asset {assetId}: {ex.Message}", cancellationToken);
            return BtcUpDown5mOrderBookAssetRefreshStatus.Failed;
        }
    }

    private IReadOnlyList<PolymarketGammaMarket> SelectRefreshMarkets(
        IReadOnlyList<PolymarketGammaMarket> markets,
        DateTimeOffset nowUtc)
    {
        var earliestEndUtc = nowUtc.AddSeconds(-options.OrderBookRefreshMarketBehindSeconds);
        var latestStartUtc = nowUtc.AddSeconds(options.OrderBookRefreshMarketLookaheadSeconds);

        return markets
            .Where(market => market.Active && !market.Closed && !market.Archived)
            .Where(BtcUpDown5mMarketAnalyzer.IsStrategyCandidate)
            .Select(market => new
            {
                Market = market,
                StartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market),
                EndUtc = market.EndDateUtc
            })
            .Where(item => item.StartUtc is not null && item.EndUtc is not null)
            .Where(item => item.StartUtc!.Value <= latestStartUtc && item.EndUtc!.Value >= earliestEndUtc)
            .OrderByDescending(item => item.StartUtc!.Value <= nowUtc && item.EndUtc!.Value >= nowUtc)
            .ThenBy(item => item.StartUtc!.Value > nowUtc ? (item.StartUtc.Value - nowUtc).TotalSeconds : 0d)
            .ThenBy(item => Math.Abs((item.StartUtc!.Value - nowUtc).TotalSeconds))
            .ThenBy(item => item.EndUtc)
            .Select(item => item.Market)
            .Take(options.OrderBookRefreshMaxMarketsPerCycle)
            .ToArray();
    }

    private int GetGammaMarketFetchLimit()
    {
        var minimumLimit = Math.Max(options.OrderBookRefreshMaxMarketsPerCycle * 20, options.OrderBookRefreshMaxMarketsPerCycle);
        return Math.Max(options.MaxMarketsPerCycle, minimumLimit);
    }

    private TimeSpan GetRefreshInterval()
    {
        return TimeSpan.FromMilliseconds(Math.Max(100, options.OrderBookRefreshIntervalMilliseconds));
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), ComponentName, operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to persist BTC 5m order-book refresh API error.");
        }
    }

    private static bool IsMissingOrderBook(PolymarketApiException ex)
    {
        return string.Equals(ex.Operation, "GetOrderBook", StringComparison.Ordinal) &&
            ex.Message.Contains("No orderbook exists", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record BtcUpDown5mOrderBookRefreshResult(
    int SelectedMarkets,
    int SelectedAssets,
    int RefreshedAssets,
    int MissingOrderBooks,
    int FailedAssets);

internal enum BtcUpDown5mOrderBookAssetRefreshStatus
{
    Refreshed,
    Missing,
    Failed
}
