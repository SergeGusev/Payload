using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public sealed record MarketTradeTickMatchResult(
    TradeTickTraderMatchStatus Status,
    LeaderTrade? Trade,
    string Details);

public static class MarketTradeTickMatcher
{
    private const decimal DecimalTolerance = 0.000001m;

    public static MarketTradeTickMatchResult Match(
        PolymarketWebSocketTradeTick tick,
        IReadOnlyList<LeaderTrade> trades,
        TimeSpan timestampTolerance)
    {
        if (!string.IsNullOrWhiteSpace(tick.TransactionHash))
        {
            var transactionHashMatches = trades
                .Where(trade => string.Equals(
                    Normalize(trade.TransactionHash),
                    Normalize(tick.TransactionHash),
                    StringComparison.Ordinal))
                .ToArray();

            if (transactionHashMatches.Length == 1)
            {
                return new MarketTradeTickMatchResult(
                    TradeTickTraderMatchStatus.FoundByTransactionHash,
                    transactionHashMatches[0],
                    "transaction_hash_exact");
            }

            if (transactionHashMatches.Length > 1)
            {
                var narrowed = FindCompositeMatches(tick, transactionHashMatches, timestampTolerance, requirePrice: true);
                if (narrowed.Length == 1)
                {
                    return new MarketTradeTickMatchResult(
                        TradeTickTraderMatchStatus.FoundByTransactionHash,
                        narrowed[0],
                        $"transaction_hash_composite_narrowed:{transactionHashMatches.Length}");
                }

                var narrowedWithoutPrice = FindCompositeMatches(tick, transactionHashMatches, timestampTolerance, requirePrice: false);
                if (narrowedWithoutPrice.Length == 1)
                {
                    return new MarketTradeTickMatchResult(
                        TradeTickTraderMatchStatus.FoundByTransactionHash,
                        narrowedWithoutPrice[0],
                        $"transaction_hash_asset_side_size_time_narrowed:{transactionHashMatches.Length}");
                }

                var wallets = transactionHashMatches
                    .Select(trade => trade.TraderWallet)
                    .Where(wallet => !string.IsNullOrWhiteSpace(wallet))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (wallets.Length == 1)
                {
                    return new MarketTradeTickMatchResult(
                        TradeTickTraderMatchStatus.FoundByTransactionHash,
                        transactionHashMatches[0],
                        $"transaction_hash_wallet_unique:{transactionHashMatches.Length}");
                }

                return new MarketTradeTickMatchResult(
                    TradeTickTraderMatchStatus.NotFound,
                    null,
                    $"transaction_hash_ambiguous:{transactionHashMatches.Length}");
            }
        }

        var compositeMatches = FindCompositeMatches(tick, trades, timestampTolerance, requirePrice: true);
        if (compositeMatches.Length == 1)
        {
            return new MarketTradeTickMatchResult(
                TradeTickTraderMatchStatus.FoundByComposite,
                compositeMatches[0],
                "composite_exact");
        }

        if (compositeMatches.Length > 1)
        {
            return new MarketTradeTickMatchResult(
                TradeTickTraderMatchStatus.NotFound,
                null,
                $"composite_ambiguous:{compositeMatches.Length}");
        }

        return new MarketTradeTickMatchResult(
            TradeTickTraderMatchStatus.NotFound,
            null,
            string.IsNullOrWhiteSpace(tick.TransactionHash)
                ? "transaction_hash_missing;composite_not_found"
                : "transaction_hash_not_found;composite_not_found");
    }

    private static LeaderTrade[] FindCompositeMatches(
        PolymarketWebSocketTradeTick tick,
        IReadOnlyList<LeaderTrade> trades,
        TimeSpan timestampTolerance,
        bool requirePrice)
    {
        if (tick.Size is not { } size || requirePrice && tick.Price is null)
        {
            return [];
        }

        var matches = trades
            .Where(trade => string.Equals(trade.AssetId, tick.AssetId, StringComparison.OrdinalIgnoreCase))
            .Where(trade => SideMatches(tick.Side, trade.Side))
            .Where(trade => Math.Abs(trade.Size - size) <= DecimalTolerance)
            .Where(trade => (trade.TimestampUtc - tick.TradeTimestampUtc).Duration() <= timestampTolerance);

        if (requirePrice)
        {
            var price = tick.Price!.Value;
            matches = matches.Where(trade => Math.Abs(trade.Price - price) <= DecimalTolerance);
        }

        return matches.ToArray();
    }

    private static bool SideMatches(TradeSide tickSide, TradeSide tradeSide)
    {
        return tickSide == TradeSide.Unknown ||
            tradeSide == TradeSide.Unknown ||
            tickSide == tradeSide;
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}
