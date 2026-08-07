using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.Strategies;
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
    IAppRepository repository,
    IPolymarketFeeAccountingService? feeAccountingService = null) : IPaperTradingProcessor
{
    private const string PaperLiveShadowTestSource = "paper_live_shadow_test";
    private const string BtcFakTakerPaperExecutionSource = "btc_updown5m_fak_taker_paper";
    private const string PaperExecutableSnapshotEvidenceClass = "paper_executable_snapshot_model";
    private const string PaperFakExecutableSnapshotFillModel = "fak_taker_executable_snapshot_v2";
    private const string LegacyNonReproducibleEvidenceClass = "legacy_non_reproducible";
    private const string FakImmutableSnapshotMissingReason = "paper_fak_immutable_snapshot_missing";

    public async Task<PaperTradingProcessingResult> ProcessOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var openOrders = PrioritizeOpenOrders(await repository.GetOpenPaperOrdersAsync(cancellationToken), now);
        var positions = (await exposureCache.GetSnapshotAsync(cancellationToken)).PaperPositions.ToList();
        if (openOrders.Count == 0)
        {
            var updatedPositionMarks = await UpdatePositionMarksAsync(positions, cancellationToken);
            return new PaperTradingProcessingResult(0, 0, 0, updatedPositionMarks);
        }

        var ordersFilled = 0;
        var ordersExpired = 0;
        var positionsUpdated = 0;
        var fillSimulationCandidatesProcessed = 0;
        var maxFillSimulationCandidates = Math.Max(1, paperTradingOptions.OpenOrderFillSimulationBatchSize);

        foreach (var order in openOrders)
        {
            if (string.Equals(order.ExecutionSource, PaperLiveShadowTestSource, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsFakTakerPaperOrder(order))
            {
                if (fillSimulationCandidatesProcessed >= maxFillSimulationCandidates)
                {
                    continue;
                }

                fillSimulationCandidatesProcessed++;
                var fakResult = await ProcessFakTakerPaperOrderAsync(order, now, positions, cancellationToken);
                if (fakResult.OrderFilled)
                {
                    ordersFilled++;
                }
                else if (fakResult.OrderFinalized)
                {
                    ordersExpired++;
                }

                if (fakResult.PositionUpdated)
                {
                    positionsUpdated++;
                }

                continue;
            }

            var canFillFromInitialGtdSnapshot = IsInitialExecutableGtdBuy(order);
            if (!canFillFromInitialGtdSnapshot &&
                await ExpireOrderIfNeededAsync(order, now, cancellationToken))
            {
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
                var existingFills = await repository.GetPaperFillsForOrderAsync(order.Id, cancellationToken);
                var previouslyFilledShares = GetFilledShares(existingFills, order.SizeShares);
                var orderForFill = order;
                PaperFill? fill = null;
                OrderBookSnapshot? orderBook = null;

                if (canFillFromInitialGtdSnapshot)
                {
                    var initialSnapshotEvaluation = conservativeGtdFillEstimator.Evaluate(
                        order,
                        null,
                        now,
                        previouslyFilledShares);
                    if (initialSnapshotEvaluation.Handled && initialSnapshotEvaluation.Fill is not null)
                    {
                        orderForFill = initialSnapshotEvaluation.Order;
                        fill = initialSnapshotEvaluation.Fill;
                    }
                }

                if (fill is null)
                {
                    orderBook = await GetOrderBookAsync(order.AssetId, cancellationToken);
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
                            if (canFillFromInitialGtdSnapshot &&
                                await ExpireOrderIfNeededAsync(conservativeGtdEvaluation.Order, now, cancellationToken))
                            {
                                ordersExpired++;
                            }

                            continue;
                        }

                        orderForFill = conservativeGtdEvaluation.Order;
                        fill = conservativeGtdEvaluation.Fill;
                    }
                    else
                    {
                        fill = paperTradingEngine.TrySimulateFill(order, orderBook, null, now, previouslyFilledShares);
                    }
                }

                if (fill is null)
                {
                    if (canFillFromInitialGtdSnapshot &&
                        await ExpireOrderIfNeededAsync(order, now, cancellationToken))
                    {
                        ordersExpired++;
                    }

                    continue;
                }

                var currentPosition = await GetCurrentOpenPaperPositionAsync(orderForFill, cancellationToken);
                if (orderForFill.Side == TradeSide.Sell && currentPosition is null)
                {
                    continue;
                }

                if (feeAccountingService is not null)
                {
                    fill = await feeAccountingService.ApplyToPaperFillAsync(orderForFill, fill, cancellationToken);
                }

                var currentBid = orderBook?.BestBid ?? currentPosition?.AveragePrice ?? fill.Price;
                if (orderForFill.Side == TradeSide.Sell && currentPosition is not null)
                {
                    var grossRealizedPnlUsd = (fill.Price - currentPosition.AveragePrice) * fill.SizeShares;
                    fill = fill with
                    {
                        RealizedPnlUsd = grossRealizedPnlUsd,
                        NetRealizedPnlUsd = CalculateNetSellPnl(grossRealizedPnlUsd, currentPosition, fill)
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

    private static bool IsFakTakerPaperOrder(PaperOrder order)
    {
        if (order.Side != TradeSide.Buy)
        {
            return false;
        }

        if (string.Equals(order.ExecutionSource, BtcFakTakerPaperExecutionSource, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(order.RawDecisionJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(order.RawDecisionJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return string.Equals(TryGetString(root, "fak_stats_probe"), bool.TrueString, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(TryGetString(root, "paper_order_type"), "FAK", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(TryGetString(root, "paper_order_execution_mode"), "FAK", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(TryGetString(root, "live_order_type"), "FAK", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(TryGetString(root, "live_order_execution_mode"), "FAK", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<FakPaperOrderProcessingResult> ProcessFakTakerPaperOrderAsync(
        PaperOrder order,
        DateTimeOffset nowUtc,
        List<PaperPosition> positions,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestedNotionalUsd = order.NotionalUsd;
            var existingFills = await repository.GetPaperFillsForOrderAsync(order.Id, cancellationToken);
            if (existingFills.Count > 0)
            {
                // The existing fill does not prove which later accounting writes
                // completed. Do not duplicate them or hide the still-open order.
                return new FakPaperOrderProcessingResult(false, false, false);
            }

            if (!TryReadImmutableFakExecutionContext(order, out var executionContext) ||
                executionContext is null)
            {
                var rejectedOrder = order with
                {
                    Status = PaperOrderStatus.Rejected,
                    Price = order.Price,
                    CancelledAtUtc = nowUtc,
                    RawDecisionJson = AttachFakPaperProcessorJson(
                        order.RawDecisionJson,
                        null,
                        null,
                        requestedNotionalUsd,
                        order.Price,
                        FakImmutableSnapshotMissingReason,
                        nowUtc,
                        LegacyNonReproducibleEvidenceClass,
                        replayEligible: false),
                    ExecutionSource = BtcFakTakerPaperExecutionSource
                };
                await repository.UpdatePaperOrderAsync(rejectedOrder, cancellationToken);
                exposureCache.ApplyPaperOrder(rejectedOrder);
                return new FakPaperOrderProcessingResult(false, true, false);
            }

            var executionIntent = executionContext.Intent;
            var orderBook = executionContext.OrderBook;
            var worstPrice = executionIntent.MaximumOrderPrice;
            var estimate = FakBuyExecutionParity.SimulatePaper(
                executionIntent,
                orderBook,
                maximumSpreadAbsolute: null);
            if (!estimate.Filled)
            {
                var rejectedOrder = order with
                {
                    Status = PaperOrderStatus.Rejected,
                    Price = worstPrice,
                    CancelledAtUtc = nowUtc,
                    RawDecisionJson = AttachFakPaperProcessorJson(
                        order.RawDecisionJson,
                        orderBook,
                        estimate,
                        executionIntent.TargetNotionalUsd,
                        worstPrice,
                        estimate.RejectionReason ?? "paper_fak_not_filled",
                        nowUtc),
                    ExecutionSource = BtcFakTakerPaperExecutionSource
                };
                await repository.UpdatePaperOrderAsync(rejectedOrder, cancellationToken);
                exposureCache.ApplyPaperOrder(rejectedOrder);
                return new FakPaperOrderProcessingResult(false, true, false);
            }

            var currentPosition = await GetCurrentOpenPaperPositionAsync(order, cancellationToken);
            var fill = new PaperFill(
                Guid.NewGuid(),
                order.Id,
                estimate.AverageFillPrice,
                estimate.SizeShares,
                nowUtc,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "FakTakerPaperFill bestAsk={0} avgFillPrice={1} shares={2} notionalUsd={3} levelsUsed={4}",
                    estimate.BestAsk,
                    estimate.AverageFillPrice,
                    estimate.SizeShares,
                    estimate.NotionalUsd,
                    estimate.LevelsUsed),
                FeeLiquidityRole: "Taker");
            if (feeAccountingService is not null)
            {
                fill = await feeAccountingService.ApplyToPaperFillAsync(order, fill, cancellationToken);
            }
            var isPartialFill = FakExecutionRules.IsPartialNotionalFill(
                estimate.NotionalUsd,
                executionIntent.TargetNotionalUsd);
            var filledOrder = order with
            {
                Status = isPartialFill
                    ? PaperOrderStatus.PartiallyFilledExpired
                    : PaperOrderStatus.Filled,
                Price = estimate.AverageFillPrice,
                SizeShares = estimate.SizeShares,
                NotionalUsd = estimate.NotionalUsd,
                FilledAtUtc = isPartialFill ? null : nowUtc,
                CancelledAtUtc = isPartialFill ? nowUtc : null,
                RawDecisionJson = AttachFakPaperProcessorJson(
                    order.RawDecisionJson,
                    orderBook,
                    estimate,
                    executionIntent.TargetNotionalUsd,
                    worstPrice,
                    null,
                    nowUtc),
                ExecutionSource = BtcFakTakerPaperExecutionSource
            };

            await repository.AddPaperFillAsync(fill, cancellationToken);
            await repository.UpdatePaperOrderAsync(filledOrder, cancellationToken);
            exposureCache.ApplyPaperOrder(filledOrder);

            var currentBid = orderBook.BestBid ?? estimate.AverageFillPrice;
            var updatedPosition = paperTradingEngine.ApplyBuyFill(currentPosition, filledOrder, fill, currentBid, nowUtc);
            await repository.UpsertPaperPositionAsync(updatedPosition, cancellationToken);
            exposureCache.ApplyPaperPosition(updatedPosition);
            await repository.ActivatePaperCopiedLeaderPositionAsync(
                filledOrder.Id,
                fill.SizeShares,
                fill.FilledAtUtc,
                cancellationToken);

            RemovePosition(positions, updatedPosition);
            positions.Add(updatedPosition);

            logger.LogInformation(
                "Paper FAK BUY simulated from taker depth. PaperOrderId={PaperOrderId} AssetId={AssetId} WorstPrice={WorstPrice} AverageFillPrice={AverageFillPrice} Shares={Shares} NotionalUsd={NotionalUsd} LevelsUsed={LevelsUsed}",
                order.Id,
                order.AssetId,
                worstPrice,
                estimate.AverageFillPrice,
                estimate.SizeShares,
                estimate.NotionalUsd,
                estimate.LevelsUsed);
            return new FakPaperOrderProcessingResult(true, true, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogError(ex, "Paper FAK order processing timed out for order {PaperOrderId}.", order.Id);
            await TryRecordApiErrorAsync("ProcessFakTakerPaperOrderTimeout", ex.Message, cancellationToken);
            return new FakPaperOrderProcessingResult(false, false, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Paper FAK order processing failed for order {PaperOrderId}.", order.Id);
            await TryRecordApiErrorAsync("ProcessFakTakerPaperOrder", ex.Message, cancellationToken);
            return new FakPaperOrderProcessingResult(false, false, false);
        }
    }

    private static decimal? CalculateNetSellPnl(
        decimal grossRealizedPnlUsd,
        PaperPosition currentPosition,
        PaperFill sellFill)
    {
        if (!FeeAccountingRules.IsAccounted(currentPosition.FeeAccountingStatus) ||
            !FeeAccountingRules.IsAccounted(sellFill.FeeAccountingStatus) ||
            currentPosition.SizeShares <= 0m)
        {
            return null;
        }

        var allocatedEntryFeeUsd = currentPosition.FeeUsd *
            Math.Min(1m, sellFill.SizeShares / currentPosition.SizeShares);
        return grossRealizedPnlUsd - allocatedEntryFeeUsd - sellFill.FeeUsd;
    }

    private static string AttachFakPaperProcessorJson(
        string? rawDecisionJson,
        OrderBookSnapshot? orderBook,
        TakerBuyFillEstimate? estimate,
        decimal requestedNotionalUsd,
        decimal worstPrice,
        string? rejectionReason,
        DateTimeOffset nowUtc,
        string evidenceClass = PaperExecutableSnapshotEvidenceClass,
        bool replayEligible = true)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(rawDecisionJson)
                ? new JsonObject()
                : JsonNode.Parse(rawDecisionJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }
        catch (InvalidOperationException)
        {
            root = new JsonObject();
        }

        root["paper_order_type"] = "FAK";
        root["paper_order_execution_mode"] = "FAK";
        root["paper_execution_source"] = BtcFakTakerPaperExecutionSource;
        root["paper_execution_evidence_class"] = evidenceClass;
        root["paper_fak_fill_model"] = PaperFakExecutableSnapshotFillModel;
        root["paper_fak_replay_eligible"] = replayEligible;
        root["paper_fak_processed_at_utc"] = nowUtc.ToString("O", CultureInfo.InvariantCulture);
        root["paper_fak_snapshot_at_utc"] = orderBook?.SnapshotAtUtc.ToString("O", CultureInfo.InvariantCulture);
        root["paper_fak_best_bid"] = orderBook?.BestBid;
        root["paper_fak_best_ask"] = orderBook?.BestAsk;
        root["paper_fak_spread"] = orderBook?.SpreadAbs;
        root["paper_fak_requested_notional_usd"] = requestedNotionalUsd;
        root["paper_fak_worst_price"] = estimate?.MaxAllowedPrice ?? worstPrice;
        root["paper_fak_average_fill_price"] = estimate?.Filled == true
            ? estimate.AverageFillPrice
            : null;
        root["paper_fak_filled_size_shares"] = estimate?.Filled == true
            ? estimate.SizeShares
            : 0m;
        root["paper_fak_filled_notional_usd"] = estimate?.Filled == true
            ? estimate.NotionalUsd
            : 0m;
        root["paper_fak_target_size_shares"] = estimate?.TargetSizeShares;
        root["paper_fak_levels_used"] = estimate?.LevelsUsed;
        root["paper_fak_partial_fill"] = estimate?.Filled == true &&
            FakExecutionRules.IsPartialNotionalFill(
                estimate.NotionalUsd,
                requestedNotionalUsd);
        root["paper_fak_rejection_reason"] = rejectionReason;
        if (!string.IsNullOrWhiteSpace(rejectionReason))
        {
            root["skip_reason"] = rejectionReason;
        }

        return root.ToJsonString();
    }

    private static bool TryReadImmutableFakExecutionContext(
        PaperOrder order,
        out FakImmutableExecutionContext? context)
    {
        context = null;
        if (string.IsNullOrWhiteSpace(order.RawDecisionJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(order.RawDecisionJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetRequiredGuid(root, "execution_intent_strategy_id", out var strategyId) ||
                !TryGetRequiredGuid(root, "execution_intent_decision_id", out var decisionId) ||
                !TryGetRequiredString(root, "execution_intent_condition_id", out var conditionId) ||
                !TryGetRequiredString(root, "execution_intent_asset_id", out var assetId) ||
                !TryGetRequiredString(root, "execution_intent_side", out var sideText) ||
                !Enum.TryParse<TradeSide>(sideText, ignoreCase: true, out var side) ||
                !TryGetRequiredString(root, "execution_intent_order_type", out var orderType) ||
                !TryGetRequiredString(root, "execution_intent_time_in_force", out var timeInForce) ||
                !TryGetRequiredBoolean(root, "execution_intent_post_only", out var postOnly) ||
                !TryGetRequiredDecimal(root, "execution_intent_maximum_order_price", out var maximumOrderPrice) ||
                !TryGetRequiredDecimal(root, "execution_intent_requested_notional_usd", out var requestedNotionalUsd) ||
                !TryGetRequiredDecimal(root, "execution_intent_requested_size_shares", out var requestedSizeShares) ||
                !TryGetRequiredDecimal(root, "execution_intent_target_notional_usd", out var targetNotionalUsd) ||
                !TryGetRequiredDecimal(root, "execution_intent_target_size_shares", out var targetSizeShares) ||
                !TryGetRequiredDecimal(root, "execution_intent_tick_size", out var tickSize) ||
                !TryGetRequiredDecimal(root, "execution_intent_min_order_size", out var minOrderSize) ||
                !TryGetRequiredBoolean(root, "execution_intent_negative_risk", out var negativeRisk) ||
                !TryGetRequiredDateTimeOffset(root, "execution_intent_created_at_utc", out var createdAtUtc) ||
                !root.TryGetProperty("execution_intent_order_book_snapshot", out var snapshotElement) ||
                snapshotElement.ValueKind != JsonValueKind.Object ||
                !TryGetRequiredString(snapshotElement, "asset_id", out var snapshotAssetId) ||
                !TryGetOptionalString(snapshotElement, "condition_id", out var snapshotConditionId) ||
                !TryGetRequiredDateTimeOffset(snapshotElement, "snapshot_at_utc", out var snapshotAtUtc) ||
                !TryGetOptionalDecimal(snapshotElement, "min_order_size", out var snapshotMinOrderSize) ||
                !TryGetOptionalDecimal(snapshotElement, "tick_size", out var snapshotTickSize) ||
                !TryGetRequiredBoolean(snapshotElement, "negative_risk", out var snapshotNegativeRisk) ||
                !TryGetOptionalDecimal(snapshotElement, "last_trade_price", out var snapshotLastTradePrice) ||
                !TryReadOrderBookLevels(snapshotElement, "bids", out var bids) ||
                !TryReadOrderBookLevels(snapshotElement, "asks", out var asks))
            {
                return false;
            }

            // DecisionId is an audit/correlation identifier (for example, a run id),
            // not the Paper order's SignalId.
            // A REST fallback can complete after the strategy cycle timestamp. The
            // resulting snapshot is still immutable because it is persisted atomically
            // with the intent in the same RawDecisionJson payload.
            if (strategyId != order.StrategyId ||
                side != TradeSide.Buy ||
                side != order.Side ||
                !string.Equals(orderType, FakBuyExecutionIntent.TimeInForce, StringComparison.Ordinal) ||
                !string.Equals(timeInForce, FakBuyExecutionIntent.TimeInForce, StringComparison.Ordinal) ||
                postOnly ||
                !string.Equals(conditionId, order.ConditionId, StringComparison.Ordinal) ||
                !string.Equals(assetId, order.AssetId, StringComparison.Ordinal) ||
                !string.Equals(snapshotAssetId, assetId, StringComparison.Ordinal) ||
                (snapshotConditionId is not null &&
                    !string.Equals(snapshotConditionId, conditionId, StringComparison.Ordinal)) ||
                maximumOrderPrice != order.Price ||
                targetNotionalUsd != order.NotionalUsd ||
                targetSizeShares != order.SizeShares ||
                (snapshotTickSize ?? 0.01m) != tickSize ||
                (snapshotMinOrderSize ?? 1m) != minOrderSize ||
                snapshotNegativeRisk != negativeRisk ||
                NormalizeToPostgresMicroseconds(createdAtUtc) !=
                    NormalizeToPostgresMicroseconds(order.CreatedAtUtc))
            {
                return false;
            }

            var orderBook = new OrderBookSnapshot(
                snapshotAssetId,
                bids,
                asks,
                snapshotAtUtc,
                snapshotConditionId,
                snapshotMinOrderSize,
                snapshotTickSize,
                snapshotNegativeRisk,
                snapshotLastTradePrice);
            var intent = new FakBuyExecutionIntent(
                strategyId,
                decisionId,
                conditionId,
                assetId,
                side,
                maximumOrderPrice,
                requestedNotionalUsd,
                requestedSizeShares,
                targetNotionalUsd,
                targetSizeShares,
                tickSize,
                minOrderSize,
                negativeRisk,
                createdAtUtc);
            if (!FakBuyExecutionParity.Validate(intent).IsValid)
            {
                return false;
            }

            context = new FakImmutableExecutionContext(intent, orderBook);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static DateTimeOffset NormalizeToPostgresMicroseconds(DateTimeOffset value)
    {
        var utcValue = value.ToUniversalTime();
        return utcValue.AddTicks(-(utcValue.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    private static bool TryReadOrderBookLevels(
        JsonElement root,
        string propertyName,
        out IReadOnlyList<OrderBookLevel> levels)
    {
        levels = [];
        if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<OrderBookLevel>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !TryGetRequiredDecimal(element, "price", out var price) ||
                !TryGetRequiredDecimal(element, "size", out var size))
            {
                return false;
            }

            parsed.Add(new OrderBookLevel(price, size));
        }

        levels = parsed;
        return true;
    }

    private static bool TryGetRequiredGuid(JsonElement root, string propertyName, out Guid value)
    {
        value = default;
        return TryGetRequiredString(root, propertyName, out var text) && Guid.TryParse(text, out value);
    }

    private static bool TryGetRequiredString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryGetOptionalString(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return true;
    }

    private static bool TryGetRequiredDecimal(JsonElement root, string propertyName, out decimal value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetDecimal(out value);
    }

    private static bool TryGetOptionalDecimal(JsonElement root, string propertyName, out decimal? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetRequiredBoolean(JsonElement root, string propertyName, out bool value)
    {
        value = default;
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    private static bool TryGetRequiredDateTimeOffset(
        JsonElement root,
        string propertyName,
        out DateTimeOffset value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.String &&
            element.TryGetDateTimeOffset(out value);
    }

    private async Task<bool> ExpireOrderIfNeededAsync(
        PaperOrder order,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var expiredOrder = paperTradingEngine.ExpireIfNeeded(order, nowUtc);
        if (expiredOrder.Status == order.Status)
        {
            return false;
        }

        await repository.UpdatePaperOrderAsync(expiredOrder, cancellationToken);
        exposureCache.ApplyPaperOrder(expiredOrder);
        return true;
    }

    private static IReadOnlyList<PaperOrder> PrioritizeOpenOrders(
        IReadOnlyList<PaperOrder> openOrders,
        DateTimeOffset nowUtc)
    {
        return openOrders
            .OrderBy(order => order.ExpiresAtUtc <= nowUtc ? 0 : 1)
            .ThenBy(order => IsInitialExecutableGtdBuy(order) ? 0 : 1)
            .ThenBy(order => order.ExpiresAtUtc)
            .ThenBy(order => order.CreatedAtUtc)
            .ThenBy(order => order.Id)
            .ToArray();
    }

    private static bool IsInitialExecutableGtdBuy(PaperOrder order)
    {
        if (order.Side != TradeSide.Buy || string.IsNullOrWhiteSpace(order.RawDecisionJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(order.RawDecisionJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !IsGtd(root) ||
                !IsOpeningLimit(root))
            {
                return false;
            }

            return TryGetDecimal(root, "paper_gtd_initial_executable_ask_shares") is > 0m;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsGtd(JsonElement root)
    {
        return string.Equals(TryGetString(root, "order_type"), "GTD", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TryGetString(root, "order_execution_mode"), "GTD", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpeningLimit(JsonElement root)
    {
        return string.Equals(TryGetString(root, "pricing_mode"), "opening_limit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TryGetString(root, "pricing_mode"), "paper_gtd_limit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TryGetString(root, "converted_to_gtd_limit_order"), "True", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static decimal? TryGetDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
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
                var netUnrealizedPnl = FeeAccountingRules.IsAccounted(position.FeeAccountingStatus)
                    ? unrealizedPnl - position.FeeUsd
                    : (decimal?)null;
                if (estimatedValue == position.EstimatedValueUsd &&
                    unrealizedPnl == position.UnrealizedPnlUsd &&
                    netUnrealizedPnl == position.NetUnrealizedPnlUsd)
                {
                    continue;
                }

                var updatedPosition = position with
                {
                    EstimatedValueUsd = estimatedValue,
                    UnrealizedPnlUsd = unrealizedPnl,
                    NetUnrealizedPnlUsd = netUnrealizedPnl,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                if (!await repository.TryUpdatePaperPositionMarkAsync(
                        position,
                        updatedPosition.EstimatedValueUsd,
                        updatedPosition.UnrealizedPnlUsd,
                        updatedPosition.NetUnrealizedPnlUsd,
                        updatedPosition.UpdatedAtUtc,
                        cancellationToken))
                {
                    continue;
                }

                exposureCache.ApplyPaperPosition(updatedPosition);
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

    private async Task<PaperPosition?> GetCurrentOpenPaperPositionAsync(
        PaperOrder order,
        CancellationToken cancellationToken)
    {
        var cachedPosition = exposureCache.GetPaperPosition(
            order.CopiedTraderWallet,
            order.AssetId);
        var position = await repository.GetPaperPositionAsync(
            cachedPosition?.CopiedTraderWallet ?? order.CopiedTraderWallet,
            cachedPosition?.AssetId ?? order.AssetId,
            cancellationToken);
        return position is { SizeShares: > 0m }
            ? position
            : null;
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

    private sealed record FakImmutableExecutionContext(
        FakBuyExecutionIntent Intent,
        OrderBookSnapshot OrderBook);

    private sealed record FakPaperOrderProcessingResult(bool OrderFilled, bool OrderFinalized, bool PositionUpdated);
}
