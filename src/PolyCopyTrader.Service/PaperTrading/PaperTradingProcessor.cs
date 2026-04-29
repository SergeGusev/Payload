using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperTradingProcessor(
    ILogger<PaperTradingProcessor> logger,
    IPaperTradingEngine paperTradingEngine,
    IPolymarketClobPublicClient clobClient,
    IMarketDataCache marketDataCache,
    MarketDataWebSocketOptions marketDataWebSocketOptions,
    IAppRepository repository) : IPaperTradingProcessor
{
    public async Task<PaperTradingProcessingResult> ProcessOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        var openOrders = await repository.GetOpenPaperOrdersAsync(cancellationToken);
        var positions = (await repository.GetPaperPositionsAsync(cancellationToken)).ToList();
        if (openOrders.Count == 0)
        {
            var updatedPositionMarks = await UpdatePositionMarksAsync(positions, cancellationToken);
            return new PaperTradingProcessingResult(0, 0, 0, updatedPositionMarks);
        }

        var ordersFilled = 0;
        var ordersExpired = 0;
        var positionsUpdated = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var order in openOrders)
        {
            var expiredOrder = paperTradingEngine.ExpireIfNeeded(order, now);
            if (expiredOrder.Status != order.Status)
            {
                await repository.UpdatePaperOrderAsync(expiredOrder, cancellationToken);
                ordersExpired++;
                continue;
            }

            try
            {
                var orderBook = await GetOrderBookAsync(order.AssetId, cancellationToken);
                var fill = paperTradingEngine.TrySimulateFill(order, orderBook, null, now);
                if (fill is null)
                {
                    continue;
                }

                var filledOrder = paperTradingEngine.ApplyFillStatus(order, fill);
                await repository.AddPaperFillAsync(fill, cancellationToken);
                await repository.UpdatePaperOrderAsync(filledOrder, cancellationToken);
                ordersFilled++;

                if (order.Side == TradeSide.Buy)
                {
                    var currentPosition = positions.FirstOrDefault(
                        position => string.Equals(position.AssetId, order.AssetId, StringComparison.OrdinalIgnoreCase));
                    var currentBid = orderBook?.BestBid ?? 0m;
                    var updatedPosition = paperTradingEngine.ApplyBuyFill(currentPosition, order, fill, currentBid, now);
                    await repository.UpsertPaperPositionAsync(updatedPosition, cancellationToken);
                    positions.RemoveAll(position => string.Equals(position.AssetId, updatedPosition.AssetId, StringComparison.OrdinalIgnoreCase));
                    positions.Add(updatedPosition);
                    positionsUpdated++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Paper order processing failed for order {PaperOrderId}.", order.Id);
                await TryRecordApiErrorAsync("ProcessOpenOrder", ex.Message, cancellationToken);
            }
        }

        positionsUpdated += await UpdatePositionMarksAsync(positions, cancellationToken);
        return new PaperTradingProcessingResult(openOrders.Count, ordersFilled, ordersExpired, positionsUpdated);
    }

    private async Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken)
    {
        return marketDataCache.TryGetFreshOrderBook(
            assetId,
            TimeSpan.FromSeconds(marketDataWebSocketOptions.StaleAfterSeconds),
            out var cachedOrderBook)
            ? cachedOrderBook
            : await clobClient.GetOrderBookAsync(assetId, cancellationToken);
    }

    private async Task<int> UpdatePositionMarksAsync(
        IReadOnlyCollection<PaperPosition> positions,
        CancellationToken cancellationToken)
    {
        var updated = 0;
        foreach (var position in positions)
        {
            try
            {
                var orderBook = await GetOrderBookAsync(position.AssetId, cancellationToken);
                if (orderBook?.BestBid is not { } bestBid)
                {
                    continue;
                }

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
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    },
                    cancellationToken);
                updated++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Paper position mark update failed for asset {AssetId}.", position.AssetId);
                await TryRecordApiErrorAsync("UpdatePositionMark", ex.Message, cancellationToken);
            }
        }

        return updated;
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "PaperTradingProcessor", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist paper trading API error for {Operation}.", operation);
        }
    }
}
