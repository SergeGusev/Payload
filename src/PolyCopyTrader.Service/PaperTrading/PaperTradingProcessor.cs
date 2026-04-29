using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperTradingProcessor(
    ILogger<PaperTradingProcessor> logger,
    IPaperTradingEngine paperTradingEngine,
    IPolymarketClobPublicClient clobClient,
    IAppRepository repository) : IPaperTradingProcessor
{
    public async Task<PaperTradingProcessingResult> ProcessOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        var openOrders = await repository.GetOpenPaperOrdersAsync(cancellationToken);
        if (openOrders.Count == 0)
        {
            return new PaperTradingProcessingResult(0, 0, 0, 0);
        }

        var positions = (await repository.GetPaperPositionsAsync(cancellationToken)).ToList();
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
                var orderBook = await clobClient.GetOrderBookAsync(order.AssetId, cancellationToken);
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
                await repository.AddApiErrorAsync(
                    new ApiError(Guid.NewGuid(), "PaperTradingProcessor", "ProcessOpenOrder", ex.Message, DateTimeOffset.UtcNow),
                    cancellationToken);
            }
        }

        return new PaperTradingProcessingResult(openOrders.Count, ordersFilled, ordersExpired, positionsUpdated);
    }
}
