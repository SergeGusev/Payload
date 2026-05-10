using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.MarketData;

public sealed class PaperTradingMarketDataUpdater(
    ILogger<PaperTradingMarketDataUpdater> logger,
    IPaperTradingEngine paperTradingEngine,
    IPaperSettlementProcessor paperSettlementProcessor,
    IExposureSnapshotCache exposureCache,
    IAppRepository repository) : IPaperTradingMarketDataUpdater
{
    private readonly SemaphoreSlim sync = new(1, 1);

    public async Task ApplyUpdateAsync(MarketDataUpdate update, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.AssetId))
        {
            return;
        }

        await sync.WaitAsync(cancellationToken);
        try
        {
            if (update.MarketResolved)
            {
                await paperSettlementProcessor.SettleMarketResolutionAsync(
                    update.ConditionId,
                    update.AssetId,
                    update.WinningAssetId,
                    update.WinningOutcome,
                    null,
                    "MarketWebSocket",
                    update.TimestampUtc,
                    cancellationToken);
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var openOrders = await repository.GetOpenPaperOrdersAsync(cancellationToken);
            var matchingOrders = openOrders
                .Where(order => string.Equals(order.AssetId, update.AssetId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var positions = (await repository.GetPaperPositionsAsync(cancellationToken)).ToList();

            foreach (var order in matchingOrders)
            {
                var expiredOrder = paperTradingEngine.ExpireIfNeeded(order, now);
                if (expiredOrder.Status != order.Status)
                {
                    await repository.UpdatePaperOrderAsync(expiredOrder, cancellationToken);
                    exposureCache.ApplyPaperOrder(expiredOrder);
                    continue;
                }

                var existingFills = await repository.GetPaperFillsForOrderAsync(order.Id, cancellationToken);
                var previouslyFilledShares = GetFilledShares(existingFills, order.SizeShares);
                var fill = paperTradingEngine.TrySimulateFill(
                    order,
                    update.OrderBookSnapshot,
                    ToObservedTrade(order, update),
                    now,
                    previouslyFilledShares);
                if (fill is null)
                {
                    continue;
                }

                var currentPosition = FindPosition(positions, order);
                if (order.Side == TradeSide.Sell && currentPosition is null)
                {
                    continue;
                }

                var currentBid = update.OrderBookSnapshot?.BestBid ?? currentPosition?.AveragePrice ?? 0m;
                if (order.Side == TradeSide.Sell && currentPosition is not null)
                {
                    fill = fill with
                    {
                        RealizedPnlUsd = (fill.Price - currentPosition.AveragePrice) * fill.SizeShares
                    };
                }

                var filledOrder = paperTradingEngine.ApplyFillStatus(order, fill, previouslyFilledShares);
                await repository.AddPaperFillAsync(fill, cancellationToken);
                await repository.UpdatePaperOrderAsync(filledOrder, cancellationToken);
                exposureCache.ApplyPaperOrder(filledOrder);

                var updatedPosition = order.Side == TradeSide.Buy
                    ? paperTradingEngine.ApplyBuyFill(currentPosition, order, fill, currentBid, now)
                    : paperTradingEngine.ApplySellFill(currentPosition!, order, fill, currentBid, now);
                await repository.UpsertPaperPositionAsync(updatedPosition, cancellationToken);
                exposureCache.ApplyPaperPosition(updatedPosition);
                if (order.Side == TradeSide.Buy)
                {
                    await repository.ActivatePaperCopiedLeaderPositionAsync(
                        order.Id,
                        fill.SizeShares,
                        fill.FilledAtUtc,
                        cancellationToken);
                }

                RemovePosition(positions, updatedPosition);
                positions.Add(updatedPosition);
            }

            if (update.OrderBookSnapshot?.BestBid is { } bestBid)
            {
                await UpdatePositionMarksAsync(positions, update.AssetId, bestBid, now, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply WebSocket market data update to paper trading for asset {AssetId}.", update.AssetId);
            await TryRecordApiErrorAsync("ApplyUpdate", ex.Message, cancellationToken);
        }
        finally
        {
            sync.Release();
        }
    }

    private async Task UpdatePositionMarksAsync(
        IReadOnlyList<PaperPosition> positions,
        string assetId,
        decimal bestBid,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var position in positions.Where(position => string.Equals(position.AssetId, assetId, StringComparison.OrdinalIgnoreCase)))
        {
            var estimatedValue = position.SizeShares * bestBid;
            var unrealizedPnl = estimatedValue - position.SizeShares * position.AveragePrice;
            if (estimatedValue == position.EstimatedValueUsd && unrealizedPnl == position.UnrealizedPnlUsd)
            {
                continue;
            }

            var updatedPosition = position with
            {
                EstimatedValueUsd = estimatedValue,
                UnrealizedPnlUsd = unrealizedPnl,
                UpdatedAtUtc = now
            };
            await repository.UpsertPaperPositionAsync(updatedPosition, cancellationToken);
            exposureCache.ApplyPaperPosition(updatedPosition);
        }
    }

    private static PaperPosition? FindPosition(
        IEnumerable<PaperPosition> positions,
        PaperOrder order)
    {
        return positions.FirstOrDefault(position =>
            string.Equals(position.AssetId, order.AssetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(position.CopiedTraderWallet, order.CopiedTraderWallet, StringComparison.OrdinalIgnoreCase));
    }

    private static void RemovePosition(
        List<PaperPosition> positions,
        PaperPosition updatedPosition)
    {
        positions.RemoveAll(position =>
            string.Equals(position.AssetId, updatedPosition.AssetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(position.CopiedTraderWallet, updatedPosition.CopiedTraderWallet, StringComparison.OrdinalIgnoreCase));
    }

    private static decimal GetFilledShares(IReadOnlyList<PaperFill> fills, decimal maxShares)
    {
        return Math.Min(maxShares, fills.Sum(fill => Math.Max(0m, fill.SizeShares)));
    }

    private static LeaderTrade? ToObservedTrade(PaperOrder order, MarketDataUpdate update)
    {
        if (update.EventType != MarketDataEventType.LastTradePrice || update.Price is not { } price)
        {
            return null;
        }

        return new LeaderTrade(
            "market-websocket",
            "Market WebSocket",
            order.ConditionId,
            order.AssetId,
            string.Empty,
            string.Empty,
            order.Outcome,
            update.Side,
            price,
            update.Size ?? 0m,
            price * (update.Size ?? 0m),
            update.TimestampUtc);
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "PaperTradingMarketDataUpdater", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist paper trading market-data API error for {Operation}.", operation);
        }
    }
}
