using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public sealed class ActiveMarketAssetSubscriptionRegistry : IActiveMarketAssetSubscriptionRegistry
{
    private readonly object gate = new object();
    private readonly Dictionary<string, ActiveMarketAssetSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim changeSignal = new(0);
    private int pendingSignal;

    public ActiveMarketAssetRegistryUpdateResult AddOrUpdateMarkets(IReadOnlyCollection<PolymarketGammaMarket> markets)
    {
        var added = 0;
        var updated = 0;
        int totalAssets;

        lock (gate)
        {
            foreach (var market in markets)
            {
                if (!market.Active || market.Closed)
                {
                    continue;
                }

                foreach (var snapshot in BuildSnapshots(market))
                {
                    if (!snapshots.TryGetValue(snapshot.AssetId, out var existing))
                    {
                        snapshots[snapshot.AssetId] = snapshot;
                        added++;
                        continue;
                    }

                    var merged = MergeGammaSnapshot(existing, snapshot);
                    if (HasDecisionDataChanged(existing, merged))
                    {
                        updated++;
                    }

                    snapshots[snapshot.AssetId] = merged;
                }
            }

            totalAssets = snapshots.Count;
        }

        if (added > 0)
        {
            SignalChange();
        }

        return new ActiveMarketAssetRegistryUpdateResult(added, updated, 0, totalAssets);
    }

    public ActiveMarketAssetRegistryUpdateResult RetainAssets(IReadOnlyCollection<string> activeAssetIds)
    {
        var retained = activeAssetIds
            .Select(NormalizeAssetId)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = 0;

        lock (gate)
        {
            foreach (var assetId in snapshots.Keys.ToArray())
            {
                if (retained.Contains(assetId))
                {
                    continue;
                }

                snapshots.Remove(assetId);
                removed++;
            }

            if (removed > 0)
            {
                SignalChange();
            }

            return new ActiveMarketAssetRegistryUpdateResult(0, 0, removed, snapshots.Count);
        }
    }

    public bool ApplyMarketDataUpdate(MarketDataUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.AssetId))
        {
            return false;
        }

        var assetId = NormalizeAssetId(update.AssetId);
        if (assetId is null)
        {
            return false;
        }

        lock (gate)
        {
            if (!snapshots.TryGetValue(assetId, out var existing))
            {
                return false;
            }

            if (update.MarketResolved)
            {
                snapshots.Remove(assetId);
                SignalChange();
                return true;
            }

            if (update.EventType is not (
                MarketDataEventType.Book or
                MarketDataEventType.PriceChange or
                MarketDataEventType.BestBidAsk or
                MarketDataEventType.LastTradePrice))
            {
                return false;
            }

            if (existing.OrderBookUpdatedAtUtc is { } existingUpdatedAt &&
                update.TimestampUtc < existingUpdatedAt)
            {
                return false;
            }

            var bestBid = update.BestBid ?? update.OrderBookSnapshot?.BestBid ?? existing.BestBid;
            var bestAsk = update.BestAsk ?? update.OrderBookSnapshot?.BestAsk ?? existing.BestAsk;
            var lastTradePrice = update.EventType == MarketDataEventType.LastTradePrice
                ? update.Price ?? existing.LastTradePrice
                : existing.LastTradePrice;
            var spread = bestBid is { } bid && bestAsk is { } ask
                ? ask - bid
                : existing.Spread;

            snapshots[assetId] = existing with
            {
                BestBid = bestBid,
                BestAsk = bestAsk,
                Spread = spread,
                LastTradePrice = lastTradePrice,
                OrderBookUpdatedAtUtc = update.TimestampUtc,
                SnapshotUpdatedAtUtc = DateTimeOffset.UtcNow
            };

            return true;
        }
    }

    public IReadOnlyCollection<string> GetAssetIds()
    {
        lock (gate)
        {
            return snapshots
                .Values
                .Where(snapshot => snapshot.IsSubscribable)
                .Select(snapshot => snapshot.AssetId)
                .ToArray();
        }
    }

    public IReadOnlyCollection<ActiveMarketAssetSnapshot> GetSnapshots()
    {
        lock (gate)
        {
            return snapshots.Values.ToArray();
        }
    }

    public bool TryGetSnapshot(string assetId, out ActiveMarketAssetSnapshot snapshot)
    {
        var normalized = NormalizeAssetId(assetId);
        if (normalized is null)
        {
            snapshot = default!;
            return false;
        }

        lock (gate)
        {
            return snapshots.TryGetValue(normalized, out snapshot!);
        }
    }

    public async Task WaitForChangeAsync(CancellationToken cancellationToken = default)
    {
        await changeSignal.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref pendingSignal, 0);
    }

    private static IEnumerable<ActiveMarketAssetSnapshot> BuildSnapshots(PolymarketGammaMarket market)
    {
        var clobTokenIds = market.ClobTokenIds.ToArray();
        var outcomes = market.Outcomes.ToArray();
        for (var index = 0; index < clobTokenIds.Length; index++)
        {
            var assetId = NormalizeAssetId(clobTokenIds[index]);
            if (assetId is null)
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            yield return new ActiveMarketAssetSnapshot(
                assetId,
                market.MarketId,
                market.ConditionId,
                market.QuestionId,
                market.Slug,
                market.Question,
                market.EventId,
                market.EventSlug,
                market.EventTitle,
                market.SeriesSlug,
                market.Category,
                index < outcomes.Length ? outcomes[index] : string.Empty,
                index,
                outcomes,
                clobTokenIds,
                market.Active,
                market.Closed,
                market.Archived,
                market.Restricted,
                market.AcceptingOrders,
                market.EnableOrderBook,
                market.NegativeRisk,
                market.Liquidity,
                market.LiquidityClob,
                market.Volume,
                market.Volume24Hr,
                market.BestBid,
                market.BestAsk,
                market.Spread,
                market.LastTradePrice,
                market.OrderMinSize,
                market.OrderPriceMinTickSize,
                market.CreatedAtUtc,
                market.UpdatedAtUtc,
                market.StartDateUtc,
                market.EndDateUtc,
                market.EventStartTimeUtc,
                market.FetchedAtUtc,
                HasOrderBookData(market) ? market.FetchedAtUtc : null,
                now);
        }
    }

    private static ActiveMarketAssetSnapshot MergeGammaSnapshot(
        ActiveMarketAssetSnapshot existing,
        ActiveMarketAssetSnapshot gammaSnapshot)
    {
        if (existing.OrderBookUpdatedAtUtc is not { } existingOrderBookUpdatedAt ||
            gammaSnapshot.OrderBookUpdatedAtUtc is not { } gammaOrderBookUpdatedAt ||
            gammaOrderBookUpdatedAt >= existingOrderBookUpdatedAt)
        {
            return gammaSnapshot;
        }

        return gammaSnapshot with
        {
            BestBid = existing.BestBid,
            BestAsk = existing.BestAsk,
            Spread = existing.Spread,
            LastTradePrice = existing.LastTradePrice,
            OrderBookUpdatedAtUtc = existing.OrderBookUpdatedAtUtc
        };
    }

    private static bool HasDecisionDataChanged(
        ActiveMarketAssetSnapshot existing,
        ActiveMarketAssetSnapshot next)
    {
        return !string.Equals(existing.MarketId, next.MarketId, StringComparison.Ordinal) ||
            !string.Equals(existing.ConditionId, next.ConditionId, StringComparison.Ordinal) ||
            !string.Equals(existing.QuestionId, next.QuestionId, StringComparison.Ordinal) ||
            !string.Equals(existing.Slug, next.Slug, StringComparison.Ordinal) ||
            !string.Equals(existing.Question, next.Question, StringComparison.Ordinal) ||
            !string.Equals(existing.EventId, next.EventId, StringComparison.Ordinal) ||
            !string.Equals(existing.EventSlug, next.EventSlug, StringComparison.Ordinal) ||
            !string.Equals(existing.EventTitle, next.EventTitle, StringComparison.Ordinal) ||
            !string.Equals(existing.SeriesSlug, next.SeriesSlug, StringComparison.Ordinal) ||
            !string.Equals(existing.Category, next.Category, StringComparison.Ordinal) ||
            !string.Equals(existing.Outcome, next.Outcome, StringComparison.Ordinal) ||
            existing.OutcomeIndex != next.OutcomeIndex ||
            !existing.Outcomes.SequenceEqual(next.Outcomes, StringComparer.Ordinal) ||
            !existing.ClobTokenIds.SequenceEqual(next.ClobTokenIds, StringComparer.Ordinal) ||
            existing.Active != next.Active ||
            existing.Closed != next.Closed ||
            existing.Archived != next.Archived ||
            existing.Restricted != next.Restricted ||
            existing.AcceptingOrders != next.AcceptingOrders ||
            existing.EnableOrderBook != next.EnableOrderBook ||
            existing.NegativeRisk != next.NegativeRisk ||
            existing.Liquidity != next.Liquidity ||
            existing.LiquidityClob != next.LiquidityClob ||
            existing.Volume != next.Volume ||
            existing.Volume24Hr != next.Volume24Hr ||
            existing.BestBid != next.BestBid ||
            existing.BestAsk != next.BestAsk ||
            existing.Spread != next.Spread ||
            existing.LastTradePrice != next.LastTradePrice ||
            existing.OrderMinSize != next.OrderMinSize ||
            existing.OrderPriceMinTickSize != next.OrderPriceMinTickSize ||
            existing.CreatedAtUtc != next.CreatedAtUtc ||
            existing.UpdatedAtUtc != next.UpdatedAtUtc ||
            existing.StartDateUtc != next.StartDateUtc ||
            existing.EndDateUtc != next.EndDateUtc ||
            existing.EventStartTimeUtc != next.EventStartTimeUtc;
    }

    private static bool HasOrderBookData(PolymarketGammaMarket market)
    {
        return market.BestBid is not null ||
            market.BestAsk is not null ||
            market.Spread is not null ||
            market.LastTradePrice is not null;
    }

    private static string? NormalizeAssetId(string? assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return null;
        }

        var trimmed = assetId.Trim();
        return trimmed.Equals("0", StringComparison.Ordinal) ||
            trimmed.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    private void SignalChange()
    {
        if (Interlocked.Exchange(ref pendingSignal, 1) == 0)
        {
            changeSignal.Release();
        }
    }
}
