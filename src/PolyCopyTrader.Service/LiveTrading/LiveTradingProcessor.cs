using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.LiveTrading;

public sealed class LiveTradingProcessor(
    ILogger<LiveTradingProcessor> logger,
    LiveTradingOptions liveTradingOptions,
    RiskOptions riskOptions,
    IPolymarketGammaClient gammaClient,
    IPolymarketTradingClient tradingClient,
    IAppRepository repository,
    IExposureSnapshotCache exposureCache,
    IPaperTradingEngine paperTradingEngine,
    ServiceControlState controlState,
    IPolymarketDataApiClient? dataApiClient = null,
    PolymarketAuthOptions? authOptions = null) : ILiveTradingProcessor
{
    private const string PaperLiveShadowTestSource = "paper_live_shadow_test";
    private const decimal ShadowPriceTolerance = 0.000001m;
    private const decimal ShadowSizeTolerance = 0.000001m;
    private const decimal FillSizeTolerance = 0.000001m;

    public async Task<LiveTradingProcessingResult> ProcessOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        var dataApiReconciledOrders = await ReconcileRecentLiveOrdersFromDataApiPositionsAsync(cancellationToken);
        var balanceSettlementsApplied = await SettleMatchedOrdersAsync(cancellationToken);
        var openOrders = await repository.GetOpenLiveOrdersAsync(cancellationToken);
        if (openOrders.Count == 0)
        {
            return new LiveTradingProcessingResult(0, 0, 0, balanceSettlementsApplied, dataApiReconciledOrders);
        }

        var polled = 0;
        var canceled = 0;
        foreach (var order in openOrders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (ShouldCancel(order))
                {
                    var cancelResult = order.OrderId is null
                        ? await tradingClient.CancelAllOrdersAsync(cancellationToken)
                        : await tradingClient.CancelOrderAsync(order.OrderId, cancellationToken);
                    var updatedOrder = await UpdateAfterCancelAsync(order, cancelResult, cancellationToken);
                    await SyncPaperShadowAsync(updatedOrder, cancellationToken);
                    canceled++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(order.OrderId))
                {
                    var status = await tradingClient.GetLiveOrderStatusAsync(order.OrderId, cancellationToken);
                    if (status is not null)
                    {
                        var updatedOrder = ApplyStatus(order, status);
                        await repository.UpdateLiveOrderAsync(updatedOrder, cancellationToken);
                        exposureCache.ApplyLiveOrder(updatedOrder);
                        await SyncPaperShadowAsync(updatedOrder, cancellationToken);
                        polled++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Live order processing failed for {LiveOrderId}.", order.Id);
                await repository.AddLiveTradingEventAsync(
                    new LiveTradingEvent(Guid.NewGuid(), "ProcessLiveOrder", "Error", ex.Message, DateTimeOffset.UtcNow),
                    cancellationToken);
            }
        }

        return new LiveTradingProcessingResult(openOrders.Count, polled, canceled, balanceSettlementsApplied, dataApiReconciledOrders);
    }

    private async Task<int> ReconcileRecentLiveOrdersFromDataApiPositionsAsync(CancellationToken cancellationToken)
    {
        if (dataApiClient is null || authOptions is null || string.IsNullOrWhiteSpace(authOptions.FunderAddress))
        {
            return 0;
        }

        var recentOrders = await repository.GetRecentLiveOrdersAsync(100, cancellationToken);
        var candidates = recentOrders
            .Where(IsDataApiPositionReconciliationCandidate)
            .ToArray();
        if (candidates.Length == 0)
        {
            return 0;
        }

        IReadOnlyList<PolymarketDataApiPosition> currentPositions;
        IReadOnlyList<PolymarketDataApiPosition> closedPositions;
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            currentPositions = await dataApiClient.GetUserCurrentPositionsAsync(
                authOptions.FunderAddress,
                limit: 500,
                offset: 0,
                timestampCacheBuster: timestamp,
                cancellationToken: cancellationToken);
            closedPositions = await dataApiClient.GetUserClosedPositionsAsync(
                authOptions.FunderAddress,
                limit: 500,
                offset: 0,
                timestampCacheBuster: timestamp,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Live Data API position reconciliation failed.");
            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(Guid.NewGuid(), "LiveDataApiPositionReconciliation", "Error", ex.Message, DateTimeOffset.UtcNow),
                cancellationToken);
            return 0;
        }

        var positions = closedPositions
            .Concat(currentPositions)
            .Where(position => !string.IsNullOrWhiteSpace(position.AssetId))
            .ToArray();
        var reconciled = 0;
        foreach (var order in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var position = FindMatchingDataApiPosition(order, positions);
            if (position is null)
            {
                continue;
            }

            var updatedOrder = ApplyDataApiPositionFill(order, position, DateTimeOffset.UtcNow);
            if (updatedOrder.FilledSize <= order.FilledSize + FillSizeTolerance)
            {
                continue;
            }

            await repository.UpdateLiveOrderAsync(updatedOrder, cancellationToken);
            exposureCache.ApplyLiveOrder(updatedOrder);
            await SyncPaperShadowAsync(updatedOrder, cancellationToken);
            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(
                    Guid.NewGuid(),
                    "LiveDataApiPositionReconciliation",
                    "OK",
                    $"LiveOrderId={updatedOrder.Id}; status={position.Status}; filled={updatedOrder.FilledSize:0.########}; avg={updatedOrder.AverageFillPrice:0.########}.",
                    updatedOrder.UpdatedAtUtc),
                cancellationToken);
            reconciled++;
        }

        return reconciled;
    }

    private static bool IsDataApiPositionReconciliationCandidate(LiveOrder order)
    {
        if (order.BalanceEffectApplied ||
            order.Side != TradeSide.Buy ||
            order.FilledSize >= order.SizeShares - FillSizeTolerance ||
            string.IsNullOrWhiteSpace(order.AssetId) ||
            string.IsNullOrWhiteSpace(order.ConditionId))
        {
            return false;
        }

        return order.Status is LiveOrderStatus.Submitted
            or LiveOrderStatus.Live
            or LiveOrderStatus.Delayed
            or LiveOrderStatus.Unmatched
            or LiveOrderStatus.CancelRequested
            or LiveOrderStatus.Cancelled
            or LiveOrderStatus.CancelFailed;
    }

    private static PolymarketDataApiPosition? FindMatchingDataApiPosition(
        LiveOrder order,
        IEnumerable<PolymarketDataApiPosition> positions)
    {
        return positions
            .Where(position =>
                string.Equals(position.AssetId, order.AssetId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(position.ConditionId, order.ConditionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(position.Outcome, order.Outcome, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(position => position.Status == PolymarketDataApiPositionStatus.Closed)
            .ThenByDescending(position => GetObservedFilledShares(position))
            .FirstOrDefault(position => GetObservedFilledShares(position) > FillSizeTolerance);
    }

    private static LiveOrder ApplyDataApiPositionFill(
        LiveOrder order,
        PolymarketDataApiPosition position,
        DateTimeOffset now)
    {
        var observedFilledShares = Math.Min(order.SizeShares, GetObservedFilledShares(position));
        var filledSize = Math.Max(order.FilledSize, observedFilledShares);
        var remaining = Math.Max(0m, order.SizeShares - filledSize);
        var fillPrice = position.AvgPrice > 0m
            ? position.AvgPrice
            : order.AverageFillPrice ?? order.Price;
        var filledNotional = filledSize * fillPrice;
        var raw = JsonSerializer.Serialize(new
        {
            source = "data_api_position_reconciliation",
            position_status = position.Status.ToString(),
            wallet = position.Wallet,
            asset_id = position.AssetId,
            condition_id = position.ConditionId,
            outcome = position.Outcome,
            total_bought = position.TotalBought,
            size = position.Size,
            avg_price = position.AvgPrice,
            current_value = position.CurrentValue,
            cash_pnl = position.CashPnl,
            realized_pnl = position.RealizedPnl,
            raw_position = position.RawJson
        });

        return order with
        {
            Status = LiveOrderStatus.Matched,
            ResponseStatus = position.Status == PolymarketDataApiPositionStatus.Closed
                ? "data_api_closed_position_reconciled"
                : "data_api_current_position_reconciled",
            FilledSize = filledSize,
            RemainingSize = remaining,
            AverageFillPrice = fillPrice,
            FilledNotionalUsd = filledNotional,
            CostBasisUsd = filledNotional + order.FeeUsd,
            RawResponseJson = raw,
            UpdatedAtUtc = now
        };
    }

    private static decimal GetObservedFilledShares(PolymarketDataApiPosition position)
    {
        var size = position.Size ?? 0m;
        return Math.Max(size, position.TotalBought);
    }

    private async Task<int> SettleMatchedOrdersAsync(CancellationToken cancellationToken)
    {
        var orders = await repository.GetMatchedLiveOrdersPendingBalanceSettlementAsync(cancellationToken: cancellationToken);
        var applied = 0;
        foreach (var order in orders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SyncPaperShadowAsync(order, cancellationToken);

                var metadata = await GetResolvedMetadataAsync(order, cancellationToken);
                if (metadata.Count == 0)
                {
                    continue;
                }

                var winningOutcome = metadata.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.WinningOutcome))?.WinningOutcome;
                if (string.IsNullOrWhiteSpace(winningOutcome))
                {
                    continue;
                }

                var winningAssetId = metadata.FirstOrDefault(item =>
                    string.Equals(item.Outcome, winningOutcome, StringComparison.OrdinalIgnoreCase))?.TokenId;
                var settledSizeShares = order.FilledSize > 0m ? order.FilledSize : order.SizeShares;
                var costBasis = order.CostBasisUsd > 0m
                    ? order.CostBasisUsd
                    : (order.AverageFillPrice ?? order.Price) * settledSizeShares + order.FeeUsd;
                var settlementValue = IsWinningOrder(order, winningAssetId, winningOutcome) ? settledSizeShares : 0m;
                var realizedPnl = settlementValue - costBasis;
                var now = DateTimeOffset.UtcNow;
                var result = await repository.ApplyLiveOrderSettlementToStrategyBalanceAsync(
                    order.Id,
                    order.StrategyId,
                    settlementValue,
                    realizedPnl,
                    winningAssetId,
                    winningOutcome,
                    now,
                    now,
                    cancellationToken);
                if (!result.Applied)
                {
                    continue;
                }

                applied++;
                logger.LogInformation(
                    "Applied live order settlement to strategy balance. LiveOrderId={LiveOrderId} StrategyId={StrategyId} SettlementValueUsd={SettlementValueUsd} RealizedPnlUsd={RealizedPnlUsd} AvailableBalance={AvailableBalance}.",
                    order.Id,
                    StrategyIds.Normalize(order.StrategyId),
                    settlementValue,
                    realizedPnl,
                    result.AvailableBalance);

                if (result.LiveStakesDisabled)
                {
                    var message =
                        $"Strategy live available balance fell below the configured live stake after settlement. StrategyId={StrategyIds.Normalize(order.StrategyId)}; " +
                        $"Available={result.AvailableBalance:0.########}.";
                    logger.LogError("{Message}", message);
                    await repository.AddLiveTradingEventAsync(
                        new LiveTradingEvent(Guid.NewGuid(), "StrategyLiveBalance", "Error", message, now),
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Live order settlement failed for {LiveOrderId}.", order.Id);
                await repository.AddLiveTradingEventAsync(
                    new LiveTradingEvent(Guid.NewGuid(), "SettleLiveOrder", "Error", ex.Message, DateTimeOffset.UtcNow),
                    cancellationToken);
            }
        }

        return applied;
    }

    private async Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetResolvedMetadataAsync(
        LiveOrder order,
        CancellationToken cancellationToken)
    {
        var byToken = await gammaClient.GetTokenMetadataAsync(order.AssetId, closed: true, cancellationToken);
        var metadata = byToken.Count > 0
            ? byToken
            : await gammaClient.GetTokenMetadataByConditionIdAsync(order.ConditionId, order.AssetId, closed: true, cancellationToken);

        return metadata
            .Where(item => item.Resolved && !string.IsNullOrWhiteSpace(item.WinningOutcome))
            .ToArray();
    }

    private static bool IsWinningOrder(LiveOrder order, string? winningAssetId, string? winningOutcome)
    {
        return (!string.IsNullOrWhiteSpace(winningAssetId) &&
                string.Equals(order.AssetId, winningAssetId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(winningOutcome) &&
                string.Equals(order.Outcome, winningOutcome, StringComparison.OrdinalIgnoreCase));
    }

    public async Task CancelAllOpenOrdersAsync(string source, CancellationToken cancellationToken = default)
    {
        var openOrders = await repository.GetOpenLiveOrdersAsync(cancellationToken);
        var result = await tradingClient.CancelAllOrdersAsync(cancellationToken);
        foreach (var order in openOrders)
        {
            var updatedOrder = await UpdateAfterCancelAsync(order, result, cancellationToken);
            await SyncPaperShadowAsync(updatedOrder, cancellationToken);
        }

        await repository.AddLiveTradingEventAsync(
            new LiveTradingEvent(
                Guid.NewGuid(),
                "CancelAll",
                result.Success ? "OK" : "Error",
                $"{source}: canceled={result.CanceledOrderIds.Count}; notCanceled={result.NotCanceled.Count}; {result.ErrorMessage}",
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private bool ShouldCancel(LiveOrder order)
    {
        var now = DateTimeOffset.UtcNow;
        if (controlState.KillSwitchActive || controlState.LiveTradingPaused)
        {
            return true;
        }

        if (IsPaperLiveShadowOrder(order))
        {
            return now >= order.ExpiresAtUtc;
        }

        return now >= order.ExpiresAtUtc ||
            now - order.CreatedAtUtc > TimeSpan.FromSeconds(Math.Min(riskOptions.MaxOrderAgeSeconds, liveTradingOptions.DefaultOrderTtlSeconds));
    }

    private async Task<LiveOrder> UpdateAfterCancelAsync(
        LiveOrder order,
        LiveOrderCancellationResult result,
        CancellationToken cancellationToken)
    {
        var orderId = order.OrderId ?? string.Empty;
        var canceled = string.IsNullOrWhiteSpace(orderId) ||
            result.CanceledOrderIds.Any(id => string.Equals(id, orderId, StringComparison.OrdinalIgnoreCase)) ||
            (result.Success && result.NotCanceled.Count == 0 && string.IsNullOrWhiteSpace(result.ErrorMessage));
        var notCanceled = !string.IsNullOrWhiteSpace(orderId) && result.NotCanceled.TryGetValue(orderId, out var notCanceledReason)
            ? notCanceledReason
            : result.ErrorMessage;

        var updatedOrder = order with
        {
            Status = canceled ? LiveOrderStatus.Cancelled : LiveOrderStatus.CancelFailed,
            CancelStatus = canceled ? "cancelled" : notCanceled,
            RawResponseJson = string.IsNullOrWhiteSpace(result.RawResponseJson) ? order.RawResponseJson : result.RawResponseJson,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await repository.UpdateLiveOrderAsync(updatedOrder, cancellationToken);
        exposureCache.ApplyLiveOrder(updatedOrder);
        return updatedOrder;
    }

    private static LiveOrder ApplyStatus(LiveOrder order, LiveOrderStatusResult status)
    {
        var originalSize = FromTokenUnits(status.OriginalSize);
        var filledSize = FromTokenUnits(status.SizeMatched);
        var remaining = Math.Max(0m, originalSize - filledSize);
        var fillPrice = TryParsePrice(status.Price) ?? order.AverageFillPrice ?? order.Price;
        var filledNotional = filledSize > 0m ? fillPrice * filledSize : 0m;
        return order with
        {
            Status = MapStatus(status.Status),
            ResponseStatus = status.Status,
            FilledSize = filledSize,
            RemainingSize = remaining,
            AverageFillPrice = filledSize > 0m ? fillPrice : null,
            FilledNotionalUsd = filledNotional,
            CostBasisUsd = filledNotional + order.FeeUsd,
            RawResponseJson = status.RawResponseJson,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static LiveOrderStatus MapStatus(string status)
    {
        return status.ToUpperInvariant() switch
        {
            "ORDER_STATUS_LIVE" or "LIVE" => LiveOrderStatus.Live,
            "ORDER_STATUS_MATCHED" or "MATCHED" => LiveOrderStatus.Matched,
            "ORDER_STATUS_UNMATCHED" or "UNMATCHED" => LiveOrderStatus.Unmatched,
            "ORDER_STATUS_CANCELED" or "ORDER_STATUS_CANCELED_MARKET_RESOLVED" or "CANCELLED" or "CANCELED" => LiveOrderStatus.Cancelled,
            "ORDER_STATUS_INVALID" or "INVALID" => LiveOrderStatus.Rejected,
            _ => LiveOrderStatus.Submitted
        };
    }

    private async Task SyncPaperShadowAsync(LiveOrder liveOrder, CancellationToken cancellationToken)
    {
        if (!IsPaperLiveShadowOrder(liveOrder))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var paperOrder = liveOrder.PaperOrderId is { } paperOrderId
            ? await repository.GetPaperOrderAsync(paperOrderId, cancellationToken)
            : liveOrder.CorrelationId is { } correlationId
                ? await repository.GetPaperOrderByCorrelationIdAsync(correlationId, cancellationToken)
                : null;

        if (paperOrder is null)
        {
            await RecordShadowDiscrepancyAndDisableLiveAsync(
                liveOrder,
                "paper_shadow_order_missing",
                "critical",
                "Live-shadow order has no matching Paper-shadow order.",
                cancellationToken);
            return;
        }

        var mismatches = ValidateShadowOrderShape(paperOrder, liveOrder);
        if (mismatches.Count > 0)
        {
            await RecordShadowDiscrepancyAndDisableLiveAsync(
                liveOrder,
                "paper_live_shadow_shape_mismatch",
                "critical",
                string.Join("; ", mismatches),
                cancellationToken);
            return;
        }

        var existingFills = await repository.GetPaperFillsForOrderAsync(paperOrder.Id, cancellationToken);
        var previouslyFilledShares = existingFills.Sum(fill => fill.SizeShares);
        var targetFilledShares = Math.Min(liveOrder.FilledSize, paperOrder.SizeShares);
        var deltaShares = targetFilledShares - previouslyFilledShares;

        if (deltaShares > 0.000001m)
        {
            var fillPrice = liveOrder.AverageFillPrice ?? liveOrder.Price;
            var fill = new PaperFill(
                Guid.NewGuid(),
                paperOrder.Id,
                fillPrice,
                deltaShares,
                now,
                JsonSerializer.Serialize(new
                {
                    source = PaperLiveShadowTestSource,
                    live_order_id = liveOrder.Id,
                    live_exchange_order_id = liveOrder.OrderId,
                    correlation_id = liveOrder.CorrelationId,
                    live_status = liveOrder.Status.ToString(),
                    live_response_status = liveOrder.ResponseStatus
                }));
            var filledOrder = paperTradingEngine.ApplyFillStatus(paperOrder, fill, previouslyFilledShares);
            await repository.AddPaperFillAsync(fill, cancellationToken);
            await repository.UpdatePaperOrderAsync(filledOrder, cancellationToken);
            exposureCache.ApplyPaperOrder(filledOrder);

            var positions = await repository.GetPaperPositionsAsync(cancellationToken);
            var currentPosition = FindPosition(positions, paperOrder);
            var updatedPosition = paperTradingEngine.ApplyBuyFill(currentPosition, paperOrder, fill, fillPrice, now);
            await repository.UpsertPaperPositionAsync(updatedPosition, cancellationToken);
            exposureCache.ApplyPaperPosition(updatedPosition);
            paperOrder = filledOrder;
        }

        if (liveOrder.Status is LiveOrderStatus.Cancelled or LiveOrderStatus.CancelFailed or LiveOrderStatus.Rejected or LiveOrderStatus.Error &&
            targetFilledShares <= 0m &&
            paperOrder.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled)
        {
            var cancelledOrder = paperOrder with
            {
                Status = PaperOrderStatus.Cancelled,
                CancelledAtUtc = now
            };
            await repository.UpdatePaperOrderAsync(cancelledOrder, cancellationToken);
            exposureCache.ApplyPaperOrder(cancelledOrder);
        }

        await repository.UpdatePaperLiveShadowDecisionLinksAsync(
            liveOrder.CorrelationId ?? Guid.Empty,
            liveOrder.SignalId,
            paperOrder.Id,
            liveOrder.Id,
            "live_status_synced",
            now,
            cancellationToken);
    }

    private static bool IsPaperLiveShadowOrder(LiveOrder order)
    {
        return string.Equals(order.ExecutionSource, PaperLiveShadowTestSource, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ValidateShadowOrderShape(PaperOrder paperOrder, LiveOrder liveOrder)
    {
        var mismatches = new List<string>();
        if (!string.Equals(paperOrder.AssetId, liveOrder.AssetId, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add("asset_id mismatch");
        }

        if (!string.Equals(paperOrder.ConditionId, liveOrder.ConditionId, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add("condition_id mismatch");
        }

        if (!string.Equals(paperOrder.Outcome, liveOrder.Outcome, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add("outcome mismatch");
        }

        if (Math.Abs(paperOrder.Price - liveOrder.Price) > ShadowPriceTolerance)
        {
            mismatches.Add($"limit_price mismatch: paper={paperOrder.Price:0.########}; live={liveOrder.Price:0.########}");
        }

        if (Math.Abs(paperOrder.SizeShares - liveOrder.SizeShares) > ShadowSizeTolerance)
        {
            mismatches.Add($"requested_size mismatch: paper={paperOrder.SizeShares:0.########}; live={liveOrder.SizeShares:0.########}");
        }

        if (!string.Equals(liveOrder.OrderType, "GTD", StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add($"order_type mismatch: live={liveOrder.OrderType}");
        }

        if (liveOrder.PostOnly != false)
        {
            mismatches.Add("post_only mismatch: live is not false");
        }

        return mismatches;
    }

    private async Task RecordShadowDiscrepancyAndDisableLiveAsync(
        LiveOrder liveOrder,
        string classification,
        string severity,
        string details,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var correlationId = liveOrder.CorrelationId ?? Guid.Empty;
        await repository.AddPaperLiveShadowDiscrepancyAsync(
            new PaperLiveShadowDiscrepancy(
                Guid.NewGuid(),
                correlationId,
                liveOrder.StrategyId,
                classification,
                severity,
                details,
                JsonSerializer.Serialize(new
                {
                    live_order_id = liveOrder.Id,
                    live_exchange_order_id = liveOrder.OrderId,
                    correlation_id = correlationId,
                    strategy_id = StrategyIds.Normalize(liveOrder.StrategyId),
                    live_status = liveOrder.Status.ToString(),
                    live_response_status = liveOrder.ResponseStatus
                }),
                now),
            cancellationToken);

        await repository.SetStrategyLiveStakesAsync(liveOrder.StrategyId, false, now, cancellationToken);
        var openOrders = await repository.GetOpenLiveOrdersForStrategyOrCorrelationAsync(
            liveOrder.StrategyId,
            liveOrder.CorrelationId,
            cancellationToken);
        foreach (var openOrder in openOrders)
        {
            var cancelResult = openOrder.OrderId is null
                ? await tradingClient.CancelAllOrdersAsync(cancellationToken)
                : await tradingClient.CancelOrderAsync(openOrder.OrderId, cancellationToken);
            await UpdateAfterCancelAsync(openOrder, cancelResult, cancellationToken);
        }

        await repository.AddLiveTradingEventAsync(
            new LiveTradingEvent(Guid.NewGuid(), "PaperLiveShadowDiscrepancy", "Error", details, now),
            cancellationToken);
    }

    private static PaperPosition? FindPosition(IEnumerable<PaperPosition> positions, PaperOrder order)
    {
        return positions.FirstOrDefault(position =>
            string.Equals(position.AssetId, order.AssetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(position.CopiedTraderWallet, order.CopiedTraderWallet, StringComparison.OrdinalIgnoreCase));
    }

    private static decimal FromTokenUnits(string value)
    {
        return decimal.TryParse(value, out var units) ? units / 1_000_000m : 0m;
    }

    private static decimal? TryParsePrice(string value)
    {
        return decimal.TryParse(value, out var price) && price > 0m ? price : null;
    }
}
