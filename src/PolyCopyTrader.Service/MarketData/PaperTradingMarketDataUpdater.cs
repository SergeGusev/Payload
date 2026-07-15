using System.Diagnostics;
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
    ConservativePaperGtdFillEstimator conservativeGtdFillEstimator,
    IAppRepository repository) : IPaperTradingMarketDataUpdater
{
    private readonly SemaphoreSlim sync = new(1, 1);

    public async Task ApplyUpdateAsync(
        MarketDataUpdate update,
        DateTimeOffset? receivedAtUtc = null,
        IReadOnlySet<Guid>? eligiblePaperOrderIds = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.AssetId))
        {
            return;
        }

        var operationStarted = Stopwatch.GetTimestamp();
        var lockWaitStarted = Stopwatch.GetTimestamp();
        await sync.WaitAsync(cancellationToken);
        var lockWaitDuration = Stopwatch.GetElapsedTime(lockWaitStarted);
        if (lockWaitDuration >= TimeSpan.FromSeconds(1))
        {
            logger.LogWarning(
                "Paper market-data updater waited for its serialization lock. AssetId={AssetId} EventType={EventType} WaitDurationMs={WaitDurationMs}",
                update.AssetId,
                update.EventType,
                lockWaitDuration.TotalMilliseconds);
        }

        var phase = "Initialize";
        var operation = "None";
        Guid? paperOrderId = null;
        try
        {
            if (update.MarketResolved)
            {
                phase = "SettleMarketResolution";
                operation = "PaperSettlementProcessor.SettleMarketResolution";
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

            var observedAtUtc = receivedAtUtc ?? DateTimeOffset.UtcNow;
            phase = "LoadExposureSnapshot";
            operation = "ExposureSnapshotCache.GetSnapshot";
            var exposure = await exposureCache.GetSnapshotAsync(cancellationToken);
            var matchingOrders = exposure.OpenPaperOrders
                .Where(order =>
                    string.Equals(order.AssetId, update.AssetId, StringComparison.OrdinalIgnoreCase) &&
                    (eligiblePaperOrderIds is null || eligiblePaperOrderIds.Contains(order.Id)))
                .ToArray();
            var positions = exposure.PaperPositions
                .Where(position => string.Equals(position.AssetId, update.AssetId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var order in matchingOrders)
            {
                paperOrderId = order.Id;
                phase = "LoadPaperFills";
                operation = "IAppRepository.GetPaperFillsForOrder";
                var existingFills = await repository.GetPaperFillsForOrderAsync(order.Id, cancellationToken);
                var previouslyFilledShares = GetFilledShares(existingFills, order.SizeShares);
                var orderForFill = order;
                PaperFill? fill = null;
                var conservativeGtdEvaluation = conservativeGtdFillEstimator.Evaluate(
                    order,
                    update.OrderBookSnapshot,
                    observedAtUtc,
                    previouslyFilledShares);

                if (conservativeGtdEvaluation.Handled)
                {
                    orderForFill = conservativeGtdEvaluation.Order;
                    fill = conservativeGtdEvaluation.Fill;

                    if (fill is null)
                    {
                        if (conservativeGtdEvaluation.OrderChanged)
                        {
                            phase = "UpdateConservativePaperOrder";
                            operation = "IAppRepository.UpdatePaperOrder";
                            await repository.UpdatePaperOrderAsync(orderForFill, cancellationToken);
                            exposureCache.ApplyPaperOrder(orderForFill);
                        }

                        var expiredOrder = paperTradingEngine.ExpireIfNeeded(orderForFill, observedAtUtc);
                        if (expiredOrder.Status != orderForFill.Status)
                        {
                            phase = "ExpireConservativePaperOrder";
                            operation = "IAppRepository.UpdatePaperOrder";
                            await repository.UpdatePaperOrderAsync(expiredOrder, cancellationToken);
                            exposureCache.ApplyPaperOrder(expiredOrder);
                        }

                        continue;
                    }
                }
                else
                {
                    var expiredOrder = paperTradingEngine.ExpireIfNeeded(order, observedAtUtc);
                    if (expiredOrder.Status != order.Status)
                    {
                        phase = "ExpirePaperOrder";
                        operation = "IAppRepository.UpdatePaperOrder";
                        await repository.UpdatePaperOrderAsync(expiredOrder, cancellationToken);
                        exposureCache.ApplyPaperOrder(expiredOrder);
                        continue;
                    }

                    fill = paperTradingEngine.TrySimulateFill(
                        order,
                        update.OrderBookSnapshot,
                        ToObservedTrade(order, update),
                        observedAtUtc,
                        previouslyFilledShares);
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

                var currentBid = update.OrderBookSnapshot?.BestBid ?? currentPosition?.AveragePrice ?? 0m;
                if (orderForFill.Side == TradeSide.Sell && currentPosition is not null)
                {
                    fill = fill with
                    {
                        RealizedPnlUsd = (fill.Price - currentPosition.AveragePrice) * fill.SizeShares
                    };
                }

                var filledOrder = paperTradingEngine.ApplyFillStatus(orderForFill, fill, previouslyFilledShares);
                phase = "AddPaperFill";
                operation = "IAppRepository.AddPaperFill";
                await repository.AddPaperFillAsync(fill, cancellationToken);
                phase = "UpdateFilledPaperOrder";
                operation = "IAppRepository.UpdatePaperOrder";
                await repository.UpdatePaperOrderAsync(filledOrder, cancellationToken);
                exposureCache.ApplyPaperOrder(filledOrder);

                var updatedPosition = orderForFill.Side == TradeSide.Buy
                    ? paperTradingEngine.ApplyBuyFill(currentPosition, orderForFill, fill, currentBid, observedAtUtc)
                    : paperTradingEngine.ApplySellFill(currentPosition!, orderForFill, fill, currentBid, observedAtUtc);
                phase = "UpsertFilledPaperPosition";
                operation = "IAppRepository.UpsertPaperPosition";
                await repository.UpsertPaperPositionAsync(updatedPosition, cancellationToken);
                exposureCache.ApplyPaperPosition(updatedPosition);
                if (orderForFill.Side == TradeSide.Buy)
                {
                    phase = "ActivateCopiedLeaderPosition";
                    operation = "IAppRepository.ActivatePaperCopiedLeaderPosition";
                    await repository.ActivatePaperCopiedLeaderPositionAsync(
                        orderForFill.Id,
                        fill.SizeShares,
                        fill.FilledAtUtc,
                        cancellationToken);
                }

                RemovePosition(positions, updatedPosition);
                positions.Add(updatedPosition);
            }

            if (update.OrderBookSnapshot?.BestBid is { } bestBid)
            {
                paperOrderId = null;
                phase = "UpdatePositionMarks";
                operation = "IAppRepository.TryUpdatePaperPositionMarks";
                await UpdatePositionMarksAsync(
                    positions,
                    update.AssetId,
                    bestBid,
                    observedAtUtc,
                    receivedAtUtc,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var duration = Stopwatch.GetElapsedTime(operationStarted);
            logger.LogError(
                ex,
                "Failed to apply WebSocket market data update to paper trading. AssetId={AssetId} EventType={EventType} Phase={Phase} Operation={Operation} PaperOrderId={PaperOrderId} DurationMs={DurationMs}",
                update.AssetId,
                update.EventType,
                phase,
                operation,
                paperOrderId,
                duration.TotalMilliseconds);
            await TryRecordApiErrorAsync(
                $"ApplyUpdate/{phase}",
                $"AssetId={update.AssetId}; EventType={update.EventType}; Operation={operation}; PaperOrderId={paperOrderId?.ToString() ?? "<null>"}; DurationMs={duration.TotalMilliseconds:F0}; Error={ex.Message}",
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
        DateTimeOffset? receivedAtUtc,
        CancellationToken cancellationToken)
    {
        var markUpdates = new List<PaperPositionMarkUpdate>();
        foreach (var position in positions.Where(position => string.Equals(position.AssetId, assetId, StringComparison.OrdinalIgnoreCase)))
        {
            if (receivedAtUtc is { } receivedAt && position.UpdatedAtUtc > receivedAt)
            {
                continue;
            }

            var estimatedValue = position.SizeShares * bestBid;
            var unrealizedPnl = estimatedValue - position.SizeShares * position.AveragePrice;
            if (estimatedValue == position.EstimatedValueUsd && unrealizedPnl == position.UnrealizedPnlUsd)
            {
                continue;
            }

            markUpdates.Add(new PaperPositionMarkUpdate(
                position,
                estimatedValue,
                unrealizedPnl,
                now));
        }

        if (markUpdates.Count == 0)
        {
            return;
        }

        var updatedPositions = await repository.TryUpdatePaperPositionMarksAsync(markUpdates, cancellationToken);
        foreach (var updatedPosition in updatedPositions)
        {
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
