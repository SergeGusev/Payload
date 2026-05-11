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
    PaperTradingOptions paperTradingOptions,
    IExposureSnapshotCache exposureCache,
    ConservativePaperGtdFillEstimator conservativeGtdFillEstimator,
    IAppRepository repository) : IPaperTradingProcessor
{
    private const string PaperLiveShadowTestSource = "paper_live_shadow_test";

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
        var fillSimulationCandidatesProcessed = 0;
        var maxFillSimulationCandidates = Math.Max(1, paperTradingOptions.OpenOrderFillSimulationBatchSize);

        foreach (var order in openOrders)
        {
            if (string.Equals(order.ExecutionSource, PaperLiveShadowTestSource, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var expiredOrder = paperTradingEngine.ExpireIfNeeded(order, now);
            if (expiredOrder.Status != order.Status)
            {
                await repository.UpdatePaperOrderAsync(expiredOrder, cancellationToken);
                exposureCache.ApplyPaperOrder(expiredOrder);
                ordersExpired++;
                continue;
            }

            if (fillSimulationCandidatesProcessed >= maxFillSimulationCandidates)
            {
                continue;
            }

            fillSimulationCandidatesProcessed++;

            try
            {
                var orderBook = await GetOrderBookAsync(order.AssetId, cancellationToken);
                var existingFills = await repository.GetPaperFillsForOrderAsync(order.Id, cancellationToken);
                var previouslyFilledShares = GetFilledShares(existingFills, order.SizeShares);
                var orderForFill = order;
                PaperFill? fill;
                var conservativeGtdEvaluation = conservativeGtdFillEstimator.Evaluate(
                    order,
                    orderBook,
                    now,
                    previouslyFilledShares);
                if (conservativeGtdEvaluation.Handled)
                {
                    if (conservativeGtdEvaluation.OrderChanged && conservativeGtdEvaluation.Fill is null)
                    {
                        await repository.UpdatePaperOrderAsync(conservativeGtdEvaluation.Order, cancellationToken);
                        exposureCache.ApplyPaperOrder(conservativeGtdEvaluation.Order);
                    }

                    if (conservativeGtdEvaluation.Fill is null)
                    {
                        continue;
                    }

                    orderForFill = conservativeGtdEvaluation.Order;
                    fill = conservativeGtdEvaluation.Fill;
                }
                else
                {
                    fill = paperTradingEngine.TrySimulateFill(order, orderBook, null, now, previouslyFilledShares);
                }

                if (fill is null)
                {
                    continue;
                }

                var currentPosition = FindPosition(positions, orderForFill);
                if (orderForFill.Side == TradeSide.Sell && currentPosition is null)
                {
                    continue;
                }

                var currentBid = orderBook?.BestBid ?? currentPosition?.AveragePrice ?? 0m;
                if (orderForFill.Side == TradeSide.Sell && currentPosition is not null)
                {
                    fill = fill with
                    {
                        RealizedPnlUsd = (fill.Price - currentPosition.AveragePrice) * fill.SizeShares
                    };
                }

                var filledOrder = paperTradingEngine.ApplyFillStatus(orderForFill, fill, previouslyFilledShares);
                await repository.AddPaperFillAsync(fill, cancellationToken);
                await repository.UpdatePaperOrderAsync(filledOrder, cancellationToken);
                exposureCache.ApplyPaperOrder(filledOrder);
                ordersFilled++;

                var updatedPosition = orderForFill.Side == TradeSide.Buy
                    ? paperTradingEngine.ApplyBuyFill(currentPosition, orderForFill, fill, currentBid, now)
                    : paperTradingEngine.ApplySellFill(currentPosition!, orderForFill, fill, currentBid, now);
                await repository.UpsertPaperPositionAsync(updatedPosition, cancellationToken);
                exposureCache.ApplyPaperPosition(updatedPosition);
                if (orderForFill.Side == TradeSide.Buy)
                {
                    await repository.ActivatePaperCopiedLeaderPositionAsync(
                        orderForFill.Id,
                        fill.SizeShares,
                        fill.FilledAtUtc,
                        cancellationToken);
                }

                RemovePosition(positions, updatedPosition);
                positions.Add(updatedPosition);
                positionsUpdated++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                logger.LogError(ex, "Paper order processing timed out for order {PaperOrderId}.", order.Id);
                await TryRecordApiErrorAsync("ProcessOpenOrderTimeout", ex.Message, cancellationToken);
            }
            catch (PolymarketApiException ex) when (IsMissingOrderBook(ex))
            {
                logger.LogDebug(
                    "Paper order processing skipped because CLOB has no order book for asset {AssetId}. PaperOrderId={PaperOrderId} Message={Message}",
                    order.AssetId,
                    order.Id,
                    ex.Message);
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
                exposureCache.ApplyPaperPosition(position with
                {
                    EstimatedValueUsd = estimatedValue,
                    UnrealizedPnlUsd = unrealizedPnl,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
                updated++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                logger.LogError(ex, "Paper position mark update timed out for asset {AssetId}.", position.AssetId);
                await TryRecordApiErrorAsync("UpdatePositionMarkTimeout", ex.Message, cancellationToken);
            }
            catch (PolymarketApiException ex) when (IsMissingOrderBook(ex))
            {
                logger.LogDebug(
                    "Paper position mark update skipped because CLOB has no order book for asset {AssetId}. Message={Message}",
                    position.AssetId,
                    ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Paper position mark update failed for asset {AssetId}.", position.AssetId);
                await TryRecordApiErrorAsync("UpdatePositionMark", ex.Message, cancellationToken);
            }
        }

        return updated;
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

    private static bool IsMissingOrderBook(PolymarketApiException ex)
    {
        return string.Equals(ex.Operation, "GetOrderBook", StringComparison.Ordinal) &&
            (ex.Message.Contains("No orderbook exists", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase));
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
