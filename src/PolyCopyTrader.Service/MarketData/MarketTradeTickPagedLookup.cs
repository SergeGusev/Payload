using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;

namespace PolyCopyTrader.Service.MarketData;

public static class MarketTradeTickPagedLookup
{
    private const string HistoricalActivityOffsetLimit = "max historical activity offset";

    public static async Task<MarketTradeTickMatchResult> MatchAsync(
        PolymarketWebSocketTradeTick tick,
        IPolymarketDataApiClient dataApiClient,
        MarketTradeDiagnosticsOptions options,
        TimeSpan timestampTolerance,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tick.ConditionId))
        {
            return new MarketTradeTickMatchResult(
                TradeTickTraderMatchStatus.NotFound,
                null,
                "condition_id_missing");
        }

        var pageSize = Math.Max(1, options.MarketTradesLimit);
        var offset = 0;
        var pagesScanned = 0;
        MarketTradeTickMatchResult? lastNotFound = null;

        while (true)
        {
            IReadOnlyList<LeaderTrade> trades;
            try
            {
                trades = await dataApiClient.GetMarketTradesAsync(
                    tick.ConditionId,
                    takerOnly: false,
                    limit: pageSize,
                    offset: offset,
                    cancellationToken);
            }
            catch (Exception ex) when (IsHistoricalActivityOffsetLimitExceeded(ex))
            {
                var details = lastNotFound?.Details ??
                    (string.IsNullOrWhiteSpace(tick.TransactionHash)
                        ? "transaction_hash_missing;composite_not_found"
                        : "transaction_hash_not_found;composite_not_found");
                return new MarketTradeTickMatchResult(
                    TradeTickTraderMatchStatus.NotFound,
                    null,
                    $"{details};history_offset_limit_reached:{offset};pages_scanned:{pagesScanned}");
            }

            pagesScanned++;
            if (trades.Count == 0)
            {
                return WithPageDetails(
                    lastNotFound ?? new MarketTradeTickMatchResult(
                        TradeTickTraderMatchStatus.NotFound,
                        null,
                        string.IsNullOrWhiteSpace(tick.TransactionHash)
                            ? "transaction_hash_missing;composite_not_found"
                            : "transaction_hash_not_found;composite_not_found"),
                    offset,
                    pagesScanned,
                    terminal: "empty_page");
            }

            var match = MatchPage(tick, trades, timestampTolerance);
            if (match.Status != TradeTickTraderMatchStatus.NotFound)
            {
                return WithPageDetails(match, offset, pagesScanned, terminal: null);
            }

            lastNotFound = match;
            offset += pageSize;
        }
    }

    private static MarketTradeTickMatchResult MatchPage(
        PolymarketWebSocketTradeTick tick,
        IReadOnlyList<LeaderTrade> trades,
        TimeSpan timestampTolerance)
    {
        if (string.IsNullOrWhiteSpace(tick.TransactionHash))
        {
            return MarketTradeTickMatcher.Match(tick, trades, timestampTolerance);
        }

        var transactionHashMatches = trades
            .Where(trade => string.Equals(
                Normalize(trade.TransactionHash),
                Normalize(tick.TransactionHash),
                StringComparison.Ordinal))
            .ToArray();

        if (transactionHashMatches.Length == 0)
        {
            return new MarketTradeTickMatchResult(
                TradeTickTraderMatchStatus.NotFound,
                null,
                "transaction_hash_not_found;composite_not_found");
        }

        return MarketTradeTickMatcher.Match(tick, transactionHashMatches, timestampTolerance);
    }

    private static MarketTradeTickMatchResult WithPageDetails(
        MarketTradeTickMatchResult match,
        int offset,
        int pagesScanned,
        string? terminal)
    {
        var details = $"{match.Details};offset:{offset};pages_scanned:{pagesScanned}";
        if (!string.IsNullOrWhiteSpace(terminal))
        {
            details = $"{details};{terminal}";
        }

        return match with { Details = details };
    }

    private static bool IsHistoricalActivityOffsetLimitExceeded(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(HistoricalActivityOffsetLimit, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}
