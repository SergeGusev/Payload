using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class BtcUpDown5mArbitrageScannerProcessor(
    ILogger<BtcUpDown5mArbitrageScannerProcessor> logger,
    BtcUpDown5mArbitrageScannerOptions options,
    IAppRepository repository,
    IMarketDataCache marketDataCache,
    IPolymarketClobPublicClient clobClient) : IBtcUpDown5mArbitrageScannerProcessor
{
    private const string WebSocketCacheSource = "websocket_cache";
    private const string ClobRestSource = "clob_rest";
    private const string MissingSource = "missing";
    private const string DecisionCoveredArbitrage = "covered_arbitrage";
    private const string DecisionNoOpportunity = "no_covered_arbitrage";
    private const string DecisionMissingOrderBook = "missing_orderbook";
    private const string DecisionMissingAsks = "missing_asks";
    private const string DecisionInsufficientDepth = "insufficient_depth";

    public async Task<BtcUpDown5mArbitrageScannerCycleResult> ProcessAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return new BtcUpDown5mArbitrageScannerCycleResult(0, 0, 0, 0, 0, 0, 0);
        }

        var sampledAtUtc = DateTimeOffset.UtcNow;
        var markets = await repository.GetBtcUpDown5mGammaMarketsAsync(options.MaxMarketsPerCycle, cancellationToken);
        var marketsScanned = 0;
        var scansStored = 0;
        var opportunities = 0;
        var skippedNoOutcomeTokens = 0;
        var missingOrderBooks = 0;
        var insufficientDepth = 0;
        var noOpportunity = 0;

        foreach (var market in markets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetActiveWindow(market, sampledAtUtc, out var marketStartUtc, out var marketEndUtc))
            {
                continue;
            }

            marketsScanned++;
            if (!TryGetOutcomeTokens(market, out var tokens))
            {
                skippedNoOutcomeTokens++;
                continue;
            }

            var upBook = await ResolveBookAsync(tokens.UpAssetId, cancellationToken);
            var downBook = await ResolveBookAsync(tokens.DownAssetId, cancellationToken);
            var evaluation = Evaluate(upBook.OrderBook, downBook.OrderBook);
            var scan = BuildScan(
                market,
                marketStartUtc,
                marketEndUtc,
                sampledAtUtc,
                tokens,
                upBook,
                downBook,
                evaluation);

            await repository.AddBtcUpDown5mArbitrageScanAsync(scan, cancellationToken);
            scansStored++;

            if (scan.WouldArbitrage)
            {
                opportunities++;
                logger.LogInformation(
                    "BTC 5m covered arbitrage opportunity. Market={MarketSlug} Shares={Shares} TotalCost={TotalCost} GuaranteedPayout={GuaranteedPayout} NetProfit={NetProfit} EdgePerShare={EdgePerShare}",
                    scan.MarketSlug,
                    scan.BestExecutableShares,
                    scan.TotalCostUsd,
                    scan.GuaranteedPayoutUsd,
                    scan.NetProfitUsd,
                    scan.EdgePerShare);
            }
            else if (scan.DecisionCode == DecisionMissingOrderBook || scan.DecisionCode == DecisionMissingAsks)
            {
                missingOrderBooks++;
            }
            else if (scan.DecisionCode == DecisionInsufficientDepth)
            {
                insufficientDepth++;
            }
            else
            {
                noOpportunity++;
            }
        }

        return new BtcUpDown5mArbitrageScannerCycleResult(
            marketsScanned,
            scansStored,
            opportunities,
            skippedNoOutcomeTokens,
            missingOrderBooks,
            insufficientDepth,
            noOpportunity);
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
            logger.LogDebug(ex, "BTC Up or Down 5m arbitrage scanner CLOB /book fallback failed. AssetId={AssetId}", assetId);
            return OrderBookLookupResult.Missing(lookup.Status.ToString(), "clob_rest_error", ex.Message, lookup.Age);
        }
    }

    private ArbitrageEvaluation Evaluate(OrderBookSnapshot? upBook, OrderBookSnapshot? downBook)
    {
        if (upBook is null || downBook is null)
        {
            return ArbitrageEvaluation.Empty(DecisionMissingOrderBook);
        }

        var upAsks = GetAskLevels(upBook);
        var downAsks = GetAskLevels(downBook);
        if (upAsks.Count == 0 || downAsks.Count == 0)
        {
            return ArbitrageEvaluation.Empty(DecisionMissingAsks);
        }

        var requiredMinShares = GetRequiredMinShares(upBook, downBook);
        var upDepth = GetDepthShares(upAsks, options.MaxExecutableShares);
        var downDepth = GetDepthShares(downAsks, options.MaxExecutableShares);
        var maxCommonShares = Math.Min(Math.Min(upDepth, downDepth), options.MaxExecutableShares);
        if (maxCommonShares < requiredMinShares)
        {
            return new ArbitrageEvaluation(
                DecisionInsufficientDepth,
                false,
                requiredMinShares,
                maxCommonShares,
                null);
        }

        var best = FindBestExecutableCandidate(upAsks, downAsks, requiredMinShares, maxCommonShares);
        if (best is null)
        {
            return new ArbitrageEvaluation(
                DecisionInsufficientDepth,
                false,
                requiredMinShares,
                maxCommonShares,
                null);
        }

        var wouldArbitrage = best.NetProfitUsd >= options.MinNetProfitUsd;
        return new ArbitrageEvaluation(
            wouldArbitrage ? DecisionCoveredArbitrage : DecisionNoOpportunity,
            wouldArbitrage,
            requiredMinShares,
            maxCommonShares,
            best);
    }

    private ExecutableCandidate? FindBestExecutableCandidate(
        IReadOnlyList<OrderBookLevel> upAsks,
        IReadOnlyList<OrderBookLevel> downAsks,
        decimal requiredMinShares,
        decimal maxCommonShares)
    {
        var candidates = new SortedSet<decimal>
        {
            requiredMinShares,
            maxCommonShares
        };
        AddDepthBreakpoints(candidates, upAsks, requiredMinShares, maxCommonShares);
        AddDepthBreakpoints(candidates, downAsks, requiredMinShares, maxCommonShares);

        ExecutableCandidate? best = null;
        foreach (var shares in candidates)
        {
            var upCost = TryGetCost(upAsks, shares);
            var downCost = TryGetCost(downAsks, shares);
            if (upCost is null || downCost is null)
            {
                continue;
            }

            var totalCost = upCost.Value + downCost.Value;
            var guaranteedPayout = shares;
            var grossProfit = guaranteedPayout - totalCost;
            var safetyBuffer = shares * options.SafetyBufferPerShare;
            var netProfit = grossProfit - safetyBuffer;
            var averageCost = totalCost / shares;
            var edgePerShare = 1m - averageCost;
            var candidate = new ExecutableCandidate(
                shares,
                upCost.Value,
                downCost.Value,
                totalCost,
                guaranteedPayout,
                grossProfit,
                safetyBuffer,
                netProfit,
                averageCost,
                edgePerShare);

            if (best is null ||
                candidate.NetProfitUsd > best.NetProfitUsd ||
                candidate.NetProfitUsd == best.NetProfitUsd && candidate.ExecutableShares > best.ExecutableShares)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static void AddDepthBreakpoints(
        SortedSet<decimal> candidates,
        IReadOnlyList<OrderBookLevel> asks,
        decimal requiredMinShares,
        decimal maxCommonShares)
    {
        var cumulative = 0m;
        foreach (var level in asks)
        {
            cumulative += level.Size;
            if (cumulative >= requiredMinShares && cumulative <= maxCommonShares)
            {
                candidates.Add(cumulative);
            }

            if (cumulative >= maxCommonShares)
            {
                break;
            }
        }
    }

    private decimal GetRequiredMinShares(OrderBookSnapshot upBook, OrderBookSnapshot downBook)
    {
        var required = options.MinExecutableShares;
        if (upBook.MinOrderSize is { } upMin)
        {
            required = Math.Max(required, upMin);
        }

        if (downBook.MinOrderSize is { } downMin)
        {
            required = Math.Max(required, downMin);
        }

        return required;
    }

    private static decimal? TryGetCost(IReadOnlyList<OrderBookLevel> asks, decimal shares)
    {
        var remaining = shares;
        var cost = 0m;
        foreach (var ask in asks)
        {
            if (remaining <= 0m)
            {
                break;
            }

            var take = Math.Min(remaining, ask.Size);
            cost += take * ask.Price;
            remaining -= take;
        }

        return remaining <= 0m ? cost : null;
    }

    private static decimal GetDepthShares(IReadOnlyList<OrderBookLevel> asks, decimal maxShares)
    {
        var total = 0m;
        foreach (var ask in asks)
        {
            total += ask.Size;
            if (total >= maxShares)
            {
                return maxShares;
            }
        }

        return total;
    }

    private static IReadOnlyList<OrderBookLevel> GetAskLevels(OrderBookSnapshot orderBook)
    {
        return orderBook.Asks
            .Where(level => level.Size > 0m && level.Price > 0m && level.Price <= 1m)
            .OrderBy(level => level.Price)
            .ToArray();
    }

    private BtcUpDown5mArbitrageScan BuildScan(
        PolymarketGammaMarket market,
        DateTimeOffset marketStartUtc,
        DateTimeOffset marketEndUtc,
        DateTimeOffset sampledAtUtc,
        OutcomeTokens tokens,
        OrderBookLookupResult upBook,
        OrderBookLookupResult downBook,
        ArbitrageEvaluation evaluation)
    {
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
            max_executable_shares = options.MaxExecutableShares,
            rest_fallback_enabled = options.RestFallbackEnabled
        });

        return new BtcUpDown5mArbitrageScan(
            Guid.NewGuid(),
            market.MarketId,
            market.ConditionId,
            market.Slug,
            marketStartUtc,
            marketEndUtc,
            sampledAtUtc,
            ToDecimalSeconds(sampledAtUtc - marketStartUtc),
            ToDecimalSeconds(marketEndUtc - sampledAtUtc),
            tokens.UpAssetId,
            upBook.OrderBook?.BestBid,
            upBook.OrderBook?.BestAsk,
            upBook.OrderBook is null ? null : GetDepthShares(GetAskLevels(upBook.OrderBook), options.MaxExecutableShares),
            upBook.Source,
            upBook.AgeMs,
            tokens.DownAssetId,
            downBook.OrderBook?.BestBid,
            downBook.OrderBook?.BestAsk,
            downBook.OrderBook is null ? null : GetDepthShares(GetAskLevels(downBook.OrderBook), options.MaxExecutableShares),
            downBook.Source,
            downBook.AgeMs,
            evaluation.RequiredMinShares,
            evaluation.MaxCommonExecutableShares,
            evaluation.Best?.ExecutableShares,
            evaluation.Best?.UpCostUsd,
            evaluation.Best?.DownCostUsd,
            evaluation.Best?.TotalCostUsd,
            evaluation.Best?.GuaranteedPayoutUsd,
            evaluation.Best?.GrossProfitUsd,
            evaluation.Best?.SafetyBufferUsd,
            evaluation.Best?.NetProfitUsd,
            evaluation.Best?.AverageCostPerShare,
            evaluation.Best?.EdgePerShare,
            options.SafetyBufferPerShare,
            options.MinNetProfitUsd,
            evaluation.DecisionCode,
            evaluation.WouldArbitrage,
            diagnosticsJson,
            DateTimeOffset.UtcNow);
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

    private sealed record ArbitrageEvaluation(
        string DecisionCode,
        bool WouldArbitrage,
        decimal RequiredMinShares,
        decimal MaxCommonExecutableShares,
        ExecutableCandidate? Best)
    {
        public static ArbitrageEvaluation Empty(string decisionCode)
        {
            return new ArbitrageEvaluation(decisionCode, false, 0m, 0m, null);
        }
    }

    private sealed record ExecutableCandidate(
        decimal ExecutableShares,
        decimal UpCostUsd,
        decimal DownCostUsd,
        decimal TotalCostUsd,
        decimal GuaranteedPayoutUsd,
        decimal GrossProfitUsd,
        decimal SafetyBufferUsd,
        decimal NetProfitUsd,
        decimal AverageCostPerShare,
        decimal EdgePerShare);

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
