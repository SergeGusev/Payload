using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.Startup;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class BtcUpDown5mStatisticsProcessor(
    ILogger<BtcUpDown5mStatisticsProcessor> logger,
    BtcUpDown5mStatisticsOptions options,
    IAppRepository repository,
    IMarketDataCache marketDataCache,
    IPolymarketClobPublicClient clobClient,
    IPolymarketGammaClient gammaClient,
    IBtcUsdReferencePriceClient btcUsdReferencePriceClient,
    IStrategyStateProvider strategyStateProvider) : IBtcUpDown5mStatisticsProcessor
{
    private const string WebSocketCacheSource = "websocket_cache";
    private const string ClobRestSource = "clob_rest";
    private const string MissingSource = "missing";
    private const string UpOutcome = "Up";
    private const string DownOutcome = "Down";

    public async Task<BtcUpDown5mStatisticsCycleResult> ProcessAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Enabled ||
            !await strategyStateProvider.IsStrategyEnabledAsync(StrategyIds.BtcUpDown5mStatistics, cancellationToken))
        {
            return EmptyResult();
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var (historyApplied, historyPending) = await SettleDueHistoryObservationsAsync(nowUtc, cancellationToken);
        BtcUsdReferencePricePoint btcPrice;
        try
        {
            btcPrice = await btcUsdReferencePriceClient.GetBtcUsdPriceAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "BTC Up or Down 5m Statistics skipped because BTC reference price is unavailable.");
            return EmptyResult() with
            {
                SkippedNoFreshBtcPrice = 1,
                HistoryObservationsApplied = historyApplied,
                HistoryObservationsPendingResult = historyPending
            };
        }

        var markets = await repository.GetBtcUpDown5mGammaMarketsAsync(options.MaxMarketsPerCycle, cancellationToken);
        var marketsScanned = 0;
        var ticksStored = 0;
        var wouldBet = 0;
        var skippedNoOutcomeTokens = 0;
        var skippedStartPriceMissing = 0;
        var skippedInsufficientHistory = 0;
        var skippedMarketPriceMissing = 0;
        var skippedNoEdge = 0;
        var historyQueued = 0;

        foreach (var market in markets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetActiveWindow(market, nowUtc, out var marketStartUtc, out var marketEndUtc))
            {
                continue;
            }

            marketsScanned++;
            if (!TryGetOutcomeTokens(market, out var tokens))
            {
                skippedNoOutcomeTokens++;
                continue;
            }

            var elapsedSeconds = ToDecimalSeconds(nowUtc - marketStartUtc);
            var secondsToClose = ToDecimalSeconds(marketEndUtc - nowUtc);
            var upBook = await ResolveBookAsync(tokens.UpAssetId, cancellationToken);
            var downBook = await ResolveBookAsync(tokens.DownAssetId, cancellationToken);
            var upMarketPrice = SelectMarketPrice(upBook);
            var downMarketPrice = SelectMarketPrice(downBook);
            var startPrice = await repository.GetBtcUpDown5mOddsStartPriceAsync(market.MarketId, cancellationToken);

            if (startPrice is null)
            {
                var tick = BuildTick(
                    market,
                    marketStartUtc,
                    marketEndUtc,
                    nowUtc,
                    elapsedSeconds,
                    secondsToClose,
                    btcPrice,
                    null,
                    null,
                    null,
                    null,
                    [],
                    null,
                    upMarketPrice,
                    downMarketPrice,
                    tokens,
                    "start_price_missing",
                    null,
                    false);
                await repository.AddBtcUpDown5mStatisticsTickAsync(tick, cancellationToken);
                ticksStored++;
                skippedStartPriceMissing++;
                continue;
            }

            var btcMoveUsd = btcPrice.PriceUsd - startPrice.Value;
            var btcMoveCents = btcMoveUsd * 100m;
            var grid = Btc5mHistoryProbabilityEstimator.BuildGrid(
                elapsedSeconds,
                btcMoveCents,
                options.HistorySecondsStep,
                options.HistoryCentsStep,
                options.HistoryMaxSeconds);
            var rows = await repository.GetBtc5mHistoryRowsAsync(
                grid.WeightedKeys.Select(key => key.Key).ToArray(),
                cancellationToken);
            var estimate = Btc5mHistoryProbabilityEstimator.Estimate(grid, rows);
            var decision = Decide(estimate, upMarketPrice.Price, downMarketPrice.Price);
            var statsTick = BuildTick(
                market,
                marketStartUtc,
                marketEndUtc,
                nowUtc,
                elapsedSeconds,
                secondsToClose,
                btcPrice,
                startPrice.Value,
                btcMoveUsd,
                btcMoveCents,
                grid,
                rows,
                estimate,
                upMarketPrice,
                downMarketPrice,
                tokens,
                decision.DecisionCode,
                decision.RecommendedOutcome,
                decision.WouldBet);

            await repository.AddBtcUpDown5mStatisticsTickAsync(statsTick, cancellationToken);
            ticksStored++;

            if (decision.WouldBet)
            {
                wouldBet++;
            }
            else if (decision.DecisionCode == "insufficient_history")
            {
                skippedInsufficientHistory++;
            }
            else if (decision.DecisionCode == "market_price_missing")
            {
                skippedMarketPriceMissing++;
            }
            else if (decision.DecisionCode == "no_positive_edge")
            {
                skippedNoEdge++;
            }

            if (TryBuildHistoryObservation(
                market,
                marketStartUtc,
                marketEndUtc,
                nowUtc,
                elapsedSeconds,
                btcPrice.PriceUsd,
                startPrice.Value,
                btcMoveUsd,
                btcMoveCents,
                out var observation))
            {
                if (await repository.TryAddBtc5mHistoryLiveObservationAsync(observation, cancellationToken))
                {
                    historyQueued++;
                }
            }
        }

        return new BtcUpDown5mStatisticsCycleResult(
            marketsScanned,
            ticksStored,
            wouldBet,
            0,
            skippedNoOutcomeTokens,
            skippedStartPriceMissing,
            skippedInsufficientHistory,
            skippedMarketPriceMissing,
            skippedNoEdge,
            historyQueued,
            historyApplied,
            historyPending);
    }

    private async Task<(int Applied, int Pending)> SettleDueHistoryObservationsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var dueBeforeUtc = nowUtc.AddSeconds(-options.ResultSettlementDelaySeconds);
        var observations = await repository.GetDueBtc5mHistoryLiveObservationsAsync(
            dueBeforeUtc,
            options.MaxHistorySettlementsPerCycle,
            cancellationToken);
        var applied = 0;
        var pending = 0;
        var closedMarketRefreshCache = new Dictionary<string, PolymarketGammaMarket?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var market = await repository.GetPolymarketGammaMarketAsync(observation.MarketId, cancellationToken);
            var result = market is null
                ? null
                : Btc5mHistoryFillCommand.TryGetWinningOutcome(market.RawJson, market.Closed);
            var pendingReason = market is null ? "market_not_found" : "result_unknown";
            if (result is not ("Up" or "Down"))
            {
                try
                {
                    var refreshedMarket = await RefreshClosedGammaMarketAsync(
                        observation,
                        closedMarketRefreshCache,
                        cancellationToken);
                    if (refreshedMarket is not null)
                    {
                        result = Btc5mHistoryFillCommand.TryGetWinningOutcome(
                            refreshedMarket.RawJson,
                            refreshedMarket.Closed);
                        pendingReason = "result_unknown";
                    }
                }
                catch (PolymarketApiException ex)
                {
                    logger.LogWarning(
                        ex,
                        "BTC Up or Down 5m Statistics could not refresh closed Gamma market metadata during history settlement. MarketId={MarketId} MarketSlug={MarketSlug}",
                        observation.MarketId,
                        observation.MarketSlug);
                    pendingReason = "gamma_refresh_failed";
                }
            }

            if (result is "Up" or "Down")
            {
                await repository.ApplyBtc5mHistoryLiveObservationResultAsync(
                    observation.Id,
                    result,
                    nowUtc,
                    cancellationToken);
                applied++;
            }
            else
            {
                await repository.MarkBtc5mHistoryLiveObservationResultPendingAsync(
                    observation.Id,
                    nowUtc.AddSeconds(options.ResultRetryDelaySeconds),
                    pendingReason,
                    nowUtc,
                    cancellationToken);
                pending++;
            }
        }

        return (applied, pending);
    }

    private async Task<PolymarketGammaMarket?> RefreshClosedGammaMarketAsync(
        Btc5mHistoryLiveObservation observation,
        Dictionary<string, PolymarketGammaMarket?> closedMarketRefreshCache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(observation.MarketSlug))
        {
            return null;
        }

        var slug = observation.MarketSlug.Trim();
        if (!closedMarketRefreshCache.TryGetValue(slug, out var market))
        {
            market = await gammaClient.GetClosedMarketBySlugAsync(slug, cancellationToken);
            if (market is not null)
            {
                await repository.UpsertPolymarketGammaMarketAsync(market, cancellationToken);
                logger.LogInformation(
                    "BTC Up or Down 5m Statistics refreshed closed Gamma market metadata during history settlement. MarketId={MarketId} MarketSlug={MarketSlug} Closed={Closed}",
                    market.MarketId,
                    market.Slug,
                    market.Closed);
            }

            closedMarketRefreshCache[slug] = market;
        }

        return market;
    }

    private BtcUpDown5mStatisticsDecision Decide(
        Btc5mHistoryProbabilityEstimate estimate,
        decimal? upMarketPrice,
        decimal? downMarketPrice)
    {
        if (estimate.EffectiveCount < options.MinHistorySupport ||
            estimate.UpProbability is null ||
            estimate.DownProbability is null)
        {
            return new BtcUpDown5mStatisticsDecision("insufficient_history", null, false);
        }

        if (upMarketPrice is null && downMarketPrice is null)
        {
            return new BtcUpDown5mStatisticsDecision("market_price_missing", null, false);
        }

        var upEdge = upMarketPrice is { } up ? estimate.UpProbability.Value - up : (decimal?)null;
        var downEdge = downMarketPrice is { } down ? estimate.DownProbability.Value - down : (decimal?)null;
        var bestOutcome = upEdge >= (downEdge ?? decimal.MinValue) ? UpOutcome : DownOutcome;
        var bestEdge = bestOutcome == UpOutcome ? upEdge : downEdge;

        if (bestEdge is null || bestEdge <= options.MinimumEdge)
        {
            return new BtcUpDown5mStatisticsDecision("no_positive_edge", null, false);
        }

        return new BtcUpDown5mStatisticsDecision(
            bestOutcome == UpOutcome ? "up_above_market" : "down_above_market",
            bestOutcome,
            true);
    }

    private BtcUpDown5mStatisticsTick BuildTick(
        PolymarketGammaMarket market,
        DateTimeOffset marketStartUtc,
        DateTimeOffset marketEndUtc,
        DateTimeOffset sampledAtUtc,
        decimal elapsedSeconds,
        decimal secondsToClose,
        BtcUsdReferencePricePoint btcPrice,
        decimal? startPrice,
        decimal? btcMoveUsd,
        decimal? btcMoveCents,
        Btc5mHistoryInterpolationGrid? grid,
        IReadOnlyCollection<Btc5mHistoryRow> rows,
        Btc5mHistoryProbabilityEstimate? estimate,
        MarketPrice upMarketPrice,
        MarketPrice downMarketPrice,
        OutcomeTokens tokens,
        string decisionCode,
        string? recommendedOutcome,
        bool wouldBet)
    {
        var upEdge = estimate?.UpProbability is { } upProbability && upMarketPrice.Price is { } upPrice
            ? upProbability - upPrice
            : (decimal?)null;
        var downEdge = estimate?.DownProbability is { } downProbability && downMarketPrice.Price is { } downPrice
            ? downProbability - downPrice
            : (decimal?)null;
        var diagnosticsJson = JsonSerializer.Serialize(new
        {
            strategy_id = StrategyIds.BtcUpDown5mStatistics,
            strategy_code = StrategyIds.BtcUpDown5mStatisticsCode,
            btc_source = btcPrice.Source,
            up_book_status = upMarketPrice.BookStatus,
            up_book_source = upMarketPrice.BookSource,
            up_book_age_ms = upMarketPrice.BookAgeMs,
            down_book_status = downMarketPrice.BookStatus,
            down_book_source = downMarketPrice.BookSource,
            down_book_age_ms = downMarketPrice.BookAgeMs,
            weighted_keys = grid?.WeightedKeys.Select(item => new
            {
                item.Key.Seconds,
                item.Key.Cents,
                item.Weight
            }),
            rows = rows.Select(row => new
            {
                row.Seconds,
                row.Cents,
                row.Count,
                row.UpCount,
                row.DownCount
            })
        });

        return new BtcUpDown5mStatisticsTick(
            Guid.NewGuid(),
            market.MarketId,
            market.ConditionId,
            market.Slug,
            marketStartUtc,
            marketEndUtc,
            sampledAtUtc,
            elapsedSeconds,
            secondsToClose,
            btcPrice.PriceUsd,
            btcPrice.SourceUpdatedAtUtc,
            btcPrice.FetchedAtUtc,
            startPrice,
            btcMoveUsd,
            btcMoveCents,
            grid?.SecondsLower,
            grid?.SecondsUpper,
            grid?.CentsLower,
            grid?.CentsUpper,
            estimate?.EffectiveCount,
            estimate?.UpProbability,
            estimate?.DownProbability,
            options.MinHistorySupport,
            estimate?.RowsFound ?? 0,
            estimate?.MissingCorners ?? 0,
            estimate?.Method ?? Btc5mHistoryProbabilityEstimator.MethodName,
            tokens.UpAssetId,
            upMarketPrice.Price,
            upMarketPrice.PriceKind,
            tokens.DownAssetId,
            downMarketPrice.Price,
            downMarketPrice.PriceKind,
            upEdge,
            downEdge,
            decisionCode,
            recommendedOutcome,
            wouldBet,
            diagnosticsJson,
            DateTimeOffset.UtcNow);
    }

    private bool TryBuildHistoryObservation(
        PolymarketGammaMarket market,
        DateTimeOffset marketStartUtc,
        DateTimeOffset marketEndUtc,
        DateTimeOffset sampledAtUtc,
        decimal elapsedSeconds,
        decimal btcPriceUsd,
        decimal startPriceUsd,
        decimal btcMoveUsd,
        decimal btcMoveCents,
        out Btc5mHistoryLiveObservation observation)
    {
        observation = default!;

        if (elapsedSeconds < 0m || elapsedSeconds > 300m)
        {
            return false;
        }

        var roundedSeconds = RoundTowardZeroToStep(elapsedSeconds, options.HistorySecondsStep);
        if (roundedSeconds > options.HistoryMaxSeconds ||
            elapsedSeconds < roundedSeconds + options.HistorySampleOffsetSeconds)
        {
            return false;
        }

        var roundedCents = RoundTowardZeroToStep(btcMoveCents, options.HistoryCentsStep);
        observation = new Btc5mHistoryLiveObservation(
            Guid.NewGuid(),
            market.MarketId,
            market.ConditionId,
            market.Slug,
            marketStartUtc,
            marketEndUtc,
            sampledAtUtc,
            roundedSeconds,
            roundedCents,
            btcPriceUsd,
            startPriceUsd,
            btcMoveUsd,
            null,
            false,
            null,
            0,
            marketEndUtc.AddSeconds(options.ResultSettlementDelaySeconds),
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        return true;
    }

    private async Task<OrderBookLookupResult> ResolveBookAsync(string assetId, CancellationToken cancellationToken)
    {
        var maxAge = TimeSpan.FromMilliseconds(options.MaxOrderBookAgeMilliseconds);
        var lookup = marketDataCache.GetOrderBook(assetId, maxAge);
        if (lookup is { Status: OrderBookCacheLookupStatus.Fresh, Snapshot: { } snapshot })
        {
            return OrderBookLookupResult.Found(NormalizeOrderBook(assetId, snapshot), WebSocketCacheSource, lookup.Age);
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
            return OrderBookLookupResult.Found(normalized, ClobRestSource, DateTimeOffset.UtcNow - normalized.SnapshotAtUtc);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "BTC Up or Down 5m Statistics CLOB /book fallback failed. AssetId={AssetId}", assetId);
            return OrderBookLookupResult.Missing(lookup.Status.ToString(), "clob_rest_error", ex.Message, lookup.Age);
        }
    }

    private static MarketPrice SelectMarketPrice(OrderBookLookupResult result)
    {
        var orderBook = result.OrderBook;
        if (orderBook?.BestAsk is { } ask)
        {
            return new MarketPrice(ask, "ask", result.Status, result.Source, ToAgeMs(result.Age));
        }

        if (orderBook?.BestBid is { } bid)
        {
            return new MarketPrice(bid, "bid_only", result.Status, result.Source, ToAgeMs(result.Age));
        }

        if (orderBook?.LastTradePrice is { } last)
        {
            return new MarketPrice(last, "last_trade", result.Status, result.Source, ToAgeMs(result.Age));
        }

        return new MarketPrice(null, "missing", result.Status, result.Source, ToAgeMs(result.Age));
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

            if (string.Equals(outcome, UpOutcome, StringComparison.OrdinalIgnoreCase))
            {
                upAssetId = assetId;
            }
            else if (string.Equals(outcome, DownOutcome, StringComparison.OrdinalIgnoreCase))
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

    private static int RoundTowardZeroToStep(decimal value, int step)
    {
        return decimal.ToInt32(decimal.Truncate(value / step) * step);
    }

    private static decimal? ToAgeMs(TimeSpan? age)
    {
        return age is null ? null : Convert.ToDecimal(Math.Max(0d, age.Value.TotalMilliseconds));
    }

    private static BtcUpDown5mStatisticsCycleResult EmptyResult()
    {
        return new BtcUpDown5mStatisticsCycleResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private sealed record OutcomeTokens(string UpAssetId, string DownAssetId);

    private sealed record MarketPrice(
        decimal? Price,
        string PriceKind,
        string BookStatus,
        string BookSource,
        decimal? BookAgeMs);

    private sealed record BtcUpDown5mStatisticsDecision(
        string DecisionCode,
        string? RecommendedOutcome,
        bool WouldBet);

    private sealed record OrderBookLookupResult(
        OrderBookSnapshot? OrderBook,
        string Status,
        string Source,
        TimeSpan? Age,
        string? Error)
    {
        public static OrderBookLookupResult Found(OrderBookSnapshot orderBook, string source, TimeSpan? age)
        {
            return new OrderBookLookupResult(orderBook, "found", source, age, null);
        }

        public static OrderBookLookupResult Missing(string status, string source, string? error, TimeSpan? age)
        {
            return new OrderBookLookupResult(null, status, source, age, error);
        }
    }
}
