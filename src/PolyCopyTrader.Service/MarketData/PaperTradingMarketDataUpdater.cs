using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.MarketData;

public sealed class PaperTradingMarketDataUpdater(
    ILogger<PaperTradingMarketDataUpdater> logger,
    IPaperTradingEngine paperTradingEngine,
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
                    continue;
                }

                var fill = paperTradingEngine.TrySimulateFill(
                    order,
                    update.OrderBookSnapshot,
                    ToObservedTrade(order, update),
                    now);
                if (fill is null)
                {
                    continue;
                }

                var filledOrder = paperTradingEngine.ApplyFillStatus(order, fill);
                await repository.AddPaperFillAsync(fill, cancellationToken);
                await repository.UpdatePaperOrderAsync(filledOrder, cancellationToken);

                if (order.Side == TradeSide.Buy)
                {
                    var currentPosition = positions.FirstOrDefault(position =>
                        string.Equals(position.AssetId, order.AssetId, StringComparison.OrdinalIgnoreCase));
                    var currentBid = update.OrderBookSnapshot?.BestBid ?? currentPosition?.AveragePrice ?? 0m;
                    var updatedPosition = paperTradingEngine.ApplyBuyFill(currentPosition, order, fill, currentBid, now);
                    await repository.UpsertPaperPositionAsync(updatedPosition, cancellationToken);
                    positions.RemoveAll(position => string.Equals(position.AssetId, updatedPosition.AssetId, StringComparison.OrdinalIgnoreCase));
                    positions.Add(updatedPosition);
                }
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
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "PaperTradingMarketDataUpdater", "ApplyUpdate", ex.Message, DateTimeOffset.UtcNow),
                cancellationToken);
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

            await repository.UpsertPaperPositionAsync(
                position with
                {
                    EstimatedValueUsd = estimatedValue,
                    UnrealizedPnlUsd = unrealizedPnl,
                    UpdatedAtUtc = now
                },
                cancellationToken);
        }
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
}
