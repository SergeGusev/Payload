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
    PolymarketAuthOptions? authOptions = null,
    IPaperLiveShadowFillReconciler? paperLiveShadowFillReconciler = null,
    IPolymarketFeeAccountingService? feeAccountingService = null) : ILiveTradingProcessor
{
    private const string PaperLiveShadowTestSource = "paper_live_shadow_test";
    private const string PaperLiveShadowActualFillSource = "paper_live_shadow_actual_fill";
    private const string DataApiPositionObservationMarker = "Data API aggregate position observed; exact per-order fill not applied.";
    private const decimal ShadowPriceTolerance = 0.000001m;
    private const decimal FillSizeTolerance = 0.000001m;
    private readonly IPaperLiveShadowFillReconciler shadowFillReconciler =
        paperLiveShadowFillReconciler ?? CreateShadowFillReconciler(repository, exposureCache, paperTradingEngine);

    private static IPaperLiveShadowFillReconciler CreateShadowFillReconciler(
        IAppRepository appRepository,
        IExposureSnapshotCache snapshotCache,
        IPaperTradingEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new PaperLiveShadowFillReconciler(appRepository, snapshotCache);
    }

    public async Task<LiveTradingProcessingResult> ProcessOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        var dataApiPositionObservations = await ObserveRecentLiveOrderDataApiPositionsAsync(cancellationToken);
        var balanceSettlementsApplied = await SettleMatchedOrdersAsync(cancellationToken);
        var openOrders = await repository.GetOpenLiveOrdersAsync(cancellationToken);
        if (openOrders.Count == 0)
        {
            return new LiveTradingProcessingResult(0, 0, 0, balanceSettlementsApplied, dataApiPositionObservations);
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
                        if (feeAccountingService is not null && updatedOrder.FilledSize > 0m)
                        {
                            updatedOrder = await feeAccountingService.ApplyToLiveOrderAsync(
                                updatedOrder,
                                cancellationToken);
                        }
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

        return new LiveTradingProcessingResult(openOrders.Count, polled, canceled, balanceSettlementsApplied, dataApiPositionObservations);
    }

    private async Task<int> ObserveRecentLiveOrderDataApiPositionsAsync(CancellationToken cancellationToken)
    {
        if (dataApiClient is null || authOptions is null || string.IsNullOrWhiteSpace(authOptions.FunderAddress))
        {
            return 0;
        }

        var recentOrders = await repository.GetRecentLiveOrdersAsync(100, cancellationToken);
        var candidates = recentOrders
            .Where(IsDataApiPositionReconciliationCandidate)
            .Where(order => !HasDataApiPositionObservation(order))
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
            logger.LogWarning(ex, "Live Data API position observation failed.");
            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(Guid.NewGuid(), "LiveDataApiPositionObservation", "Error", ex.Message, DateTimeOffset.UtcNow),
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

            var updatedOrder = ApplyDataApiPositionObservation(order, position, DateTimeOffset.UtcNow);
            await repository.UpdateLiveOrderAsync(updatedOrder, cancellationToken);
            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(
                    Guid.NewGuid(),
                    "LiveDataApiPositionObservation",
                    "Warning",
                    $"LiveOrderId={updatedOrder.Id}; status={position.Status}; observedShares={GetObservedFilledShares(position):0.########}; avg={position.AvgPrice:0.########}; exactFillApplied=false.",
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

    private static bool HasDataApiPositionObservation(LiveOrder order)
    {
        return order.ValidationSummary.Contains(
            DataApiPositionObservationMarker,
            StringComparison.OrdinalIgnoreCase);
    }

    private static LiveOrder ApplyDataApiPositionObservation(
        LiveOrder order,
        PolymarketDataApiPosition position,
        DateTimeOffset now)
    {
        var observedShares = GetObservedFilledShares(position);
        var observation = $"{DataApiPositionObservationMarker} position_status={position.Status}; observed_shares={observedShares:0.########}; avg_price={position.AvgPrice:0.########}.";

        return order with
        {
            ValidationSummary = AppendValidationSummary(order.ValidationSummary, observation),
            UpdatedAtUtc = now
        };
    }

    private static string AppendValidationSummary(string existing, string addition)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return addition;
        }

        return existing.Contains(addition, StringComparison.OrdinalIgnoreCase)
            ? existing
            : existing.TrimEnd() + "; " + addition;
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
                var settlementOrder = order;
                if (feeAccountingService is not null &&
                    FeeAccountingRules.ParseStatus(order.FeeAccountingStatus) == FeeAccountingStatus.CalculationUnavailable &&
                    order.FeeCalculatedAtUtc is not null)
                {
                    settlementOrder = await feeAccountingService.ApplyToLiveOrderAsync(order, cancellationToken);
                    await repository.UpdateLiveOrderAsync(settlementOrder, cancellationToken);
                }

                await TrySyncPaperShadowBeforeLiveSettlementAsync(settlementOrder, cancellationToken);

                var metadata = await GetResolvedMetadataAsync(settlementOrder, cancellationToken);
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
                var settledSizeShares = settlementOrder.FilledSize > 0m ? settlementOrder.FilledSize : settlementOrder.SizeShares;
                var grossCostBasis = settlementOrder.FilledNotionalUsd > 0m
                    ? settlementOrder.FilledNotionalUsd
                    : (settlementOrder.AverageFillPrice ?? settlementOrder.Price) * settledSizeShares;
                var settlementValue = IsWinningOrder(settlementOrder, winningAssetId, winningOutcome) ? settledSizeShares : 0m;
                var grossRealizedPnl = settlementValue - grossCostBasis;
                var netRealizedPnl = FeeAccountingRules.IsAccounted(settlementOrder.FeeAccountingStatus)
                    ? grossRealizedPnl - settlementOrder.FeeUsd
                    : (decimal?)null;
                var now = DateTimeOffset.UtcNow;
                var result = await repository.ApplyLiveOrderSettlementToStrategyBalanceAsync(
                    settlementOrder.Id,
                    settlementOrder.StrategyId,
                    settlementValue,
                    grossRealizedPnl,
                    netRealizedPnl,
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
                    "Applied live order settlement to strategy balance. LiveOrderId={LiveOrderId} StrategyId={StrategyId} SettlementValueUsd={SettlementValueUsd} GrossRealizedPnlUsd={GrossRealizedPnlUsd} NetRealizedPnlUsd={NetRealizedPnlUsd} FeeAccountingStatus={FeeAccountingStatus} AvailableBalance={AvailableBalance}.",
                    settlementOrder.Id,
                    StrategyIds.Normalize(settlementOrder.StrategyId),
                    settlementValue,
                    grossRealizedPnl,
                    netRealizedPnl,
                    settlementOrder.FeeAccountingStatus,
                    result.AvailableBalance);

                await UpdateStrategyLiveLostCounterAfterSettlementAsync(settlementOrder.StrategyId, settlementValue > 0m, now, cancellationToken);

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

    private async Task TrySyncPaperShadowBeforeLiveSettlementAsync(
        LiveOrder order,
        CancellationToken cancellationToken)
    {
        try
        {
            await SyncPaperShadowAsync(order, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Paper shadow synchronization failed before Live settlement for {LiveOrderId}; Live settlement will continue independently.",
                order.Id);
            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(
                    Guid.NewGuid(),
                    "PaperLiveShadowSettlementSync",
                    "Error",
                    $"LiveOrderId={order.Id:D}; {ex.Message}",
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }

    private async Task UpdateStrategyLiveLostCounterAfterSettlementAsync(
        Guid strategyId,
        bool won,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        var runtimeSettings = await repository.GetStrategyRuntimeSettingsAsync(cancellationToken);
        var settings = runtimeSettings.TryGetValue(normalizedStrategyId, out var value)
            ? value
            : StrategyRuntimeSettings.Default(normalizedStrategyId);
        var result = await repository.UpdateStrategyLostCounterAfterSettlementAsync(
            normalizedStrategyId,
            isLive: true,
            won,
            counterEnabled: settings.LiveLostCoeff > 1m,
            updatedAtUtc,
            cancellationToken);
        if (!result.Applied)
        {
            logger.LogWarning(
                "Live LostCounter update skipped because strategy was not found. StrategyId={StrategyId}",
                normalizedStrategyId);
            return;
        }

        logger.LogInformation(
            "Live LostCounter updated after settlement. StrategyId={StrategyId} Won={Won} Counter={LostCounter}",
            normalizedStrategyId,
            won,
            result.LiveLostCounter);
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

        var validation = ValidateShadowOrderShape(paperOrder, liveOrder);
        if (validation.BlockingMismatches.Count > 0)
        {
            await RecordShadowDiscrepancyAndDisableLiveAsync(
                liveOrder,
                "paper_live_shadow_shape_mismatch",
                "critical",
                string.Join("; ", validation.BlockingMismatches),
                cancellationToken);
            return;
        }

        if (validation.Incidents.Count > 0)
        {
            await RecordShadowIncidentAsync(
                liveOrder,
                "paper_live_shadow_shape_incident",
                "warning",
                string.Join("; ", validation.Incidents),
                cancellationToken);
        }

        var targetFilledShares = Math.Min(liveOrder.FilledSize, paperOrder.SizeShares);
        if (targetFilledShares > FillSizeTolerance)
        {
            var reconciliation = await shadowFillReconciler.ReconcileAsync(
                paperOrder.Id,
                liveOrder.Id,
                cancellationToken);
            paperOrder = reconciliation.PaperOrder;
            targetFilledShares = reconciliation.PaperFill.SizeShares;
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

    private static ShadowOrderShapeValidation ValidateShadowOrderShape(PaperOrder paperOrder, LiveOrder liveOrder)
    {
        var mismatches = new List<string>();
        var incidents = new List<string>();
        if (paperOrder.Side != TradeSide.Buy || liveOrder.Side != TradeSide.Buy)
        {
            mismatches.Add($"side mismatch or unsupported: expected=Buy; paper={paperOrder.Side}; live={liveOrder.Side}");
        }

        if (StrategyIds.Normalize(paperOrder.StrategyId) != StrategyIds.Normalize(liveOrder.StrategyId))
        {
            mismatches.Add("strategy_id mismatch");
        }

        if (paperOrder.SignalId != liveOrder.SignalId)
        {
            mismatches.Add("signal_id mismatch");
        }

        if (liveOrder.PaperOrderId != paperOrder.Id)
        {
            mismatches.Add("paper_order_id mismatch");
        }

        if (paperOrder.CorrelationId is null || liveOrder.CorrelationId != paperOrder.CorrelationId)
        {
            mismatches.Add("correlation_id mismatch or missing");
        }

        if (!string.Equals(paperOrder.ExecutionSource, PaperLiveShadowTestSource, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(paperOrder.ExecutionSource, PaperLiveShadowActualFillSource, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add($"paper_execution_source mismatch: paper={paperOrder.ExecutionSource}");
        }

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

        var expectedOrderType = GetExpectedShadowOrderType(paperOrder);
        var isExpectedFak = string.Equals(expectedOrderType, "FAK", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(liveOrder.OrderType, "FAK", StringComparison.OrdinalIgnoreCase);

        if (isExpectedFak)
        {
            var expectedMaximumOrderPrice = GetExpectedFakMaximumOrderPrice(paperOrder);
            if (Math.Abs(expectedMaximumOrderPrice - liveOrder.Price) > ShadowPriceTolerance)
            {
                mismatches.Add(
                    $"FAK maximum_order_price mismatch: paper_intent={expectedMaximumOrderPrice:0.########}; live={liveOrder.Price:0.########}");
            }

            var expectedTargetNotionalUsd = GetExpectedFakTargetNotionalUsd(paperOrder);
            if (Math.Abs(expectedTargetNotionalUsd - liveOrder.NotionalUsd) > ShadowPriceTolerance)
            {
                mismatches.Add(
                    $"FAK target_notional_usd mismatch: paper_intent={expectedTargetNotionalUsd:0.########}; live={liveOrder.NotionalUsd:0.########}");
            }
        }
        else if (Math.Abs(paperOrder.Price - liveOrder.Price) > ShadowPriceTolerance)
        {
            mismatches.Add($"limit_price mismatch: paper={paperOrder.Price:0.########}; live={liveOrder.Price:0.########}");
        }

        if (!string.Equals(liveOrder.OrderType, expectedOrderType, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add($"order_type mismatch: expected={expectedOrderType}; live={liveOrder.OrderType}");
        }

        var expectedPostOnly = GetExpectedShadowPostOnly(paperOrder);
        if (liveOrder.PostOnly.GetValueOrDefault(false) != expectedPostOnly)
        {
            mismatches.Add(
                $"post_only mismatch: expected={expectedPostOnly.ToString().ToLowerInvariant()}; live={(liveOrder.PostOnly is { } livePostOnly ? livePostOnly.ToString().ToLowerInvariant() : "null")}");
        }

        return new ShadowOrderShapeValidation(mismatches, incidents);
    }

    private static bool GetExpectedShadowPostOnly(PaperOrder _)
    {
        return false;
    }

    private static string GetExpectedShadowOrderType(PaperOrder paperOrder)
    {
        if (TryReadStringFromRawDecisionJson(paperOrder.RawDecisionJson, "live_order_type", out var liveOrderType))
        {
            return liveOrderType;
        }

        return "FAK";
    }

    private static decimal GetExpectedFakMaximumOrderPrice(PaperOrder paperOrder)
    {
        foreach (var propertyName in new[]
        {
            "execution_intent_maximum_order_price",
            "paper_fak_maximum_order_price",
            "paper_fak_worst_price",
            "live_fak_worst_price",
            "fak_worst_price"
        })
        {
            if (TryReadDecimalFromRawDecisionJson(paperOrder.RawDecisionJson, propertyName, out var value))
            {
                return value;
            }
        }

        return paperOrder.Price;
    }

    private static decimal GetExpectedFakTargetNotionalUsd(PaperOrder paperOrder)
    {
        foreach (var propertyName in new[]
        {
            "execution_intent_target_notional_usd",
            "paper_fak_requested_notional_usd",
            "target_notional_usd"
        })
        {
            if (TryReadDecimalFromRawDecisionJson(paperOrder.RawDecisionJson, propertyName, out var value))
            {
                return value;
            }
        }

        return paperOrder.NotionalUsd;
    }

    private static bool TryReadDecimalFromRawDecisionJson(
        string? rawDecisionJson,
        string propertyName,
        out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(rawDecisionJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawDecisionJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.Number &&
                property.TryGetDecimal(out value);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadStringFromRawDecisionJson(string? rawDecisionJson, string propertyName, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(rawDecisionJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawDecisionJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(propertyName, out var element) &&
                element.ValueKind == JsonValueKind.String)
            {
                value = element.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
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

    private async Task RecordShadowIncidentAsync(
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
                    live_response_status = liveOrder.ResponseStatus,
                    live_stakes_disabled = false
                }),
                now),
            cancellationToken);

        await repository.AddLiveTradingEventAsync(
            new LiveTradingEvent(Guid.NewGuid(), "PaperLiveShadowIncident", "Warning", details, now),
            cancellationToken);
    }

    private sealed record ShadowOrderShapeValidation(
        IReadOnlyList<string> BlockingMismatches,
        IReadOnlyList<string> Incidents);

    private static decimal FromTokenUnits(string value)
    {
        return decimal.TryParse(value, out var units) ? units / 1_000_000m : 0m;
    }

    private static decimal? TryParsePrice(string value)
    {
        return decimal.TryParse(value, out var price) && price > 0m ? price : null;
    }
}
