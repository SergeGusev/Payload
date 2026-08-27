using System.Diagnostics;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
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
    IAppRepository repository,
    IPolymarketFeeAccountingService? feeAccountingService = null,
    MarketDataWebSocketOptions? marketDataWebSocketOptions = null,
    IMakerGtdPaperPlacementHandoff? makerGtdPaperPlacementHandoff = null) : IPaperTradingMarketDataUpdater
{
    private const string PaperLiveShadowTestSource = "paper_live_shadow_test";
    private const int MakerPositionCasMaximumAttempts = 3;
    private readonly TimeSpan makerMaximumEventAge = TimeSpan.FromSeconds(
        Math.Max(1, (marketDataWebSocketOptions ?? new MarketDataWebSocketOptions()).StaleAfterSeconds));
    private readonly IMakerGtdPaperPlacementHandoff makerGtdHandoff =
        makerGtdPaperPlacementHandoff ?? NoOpMakerGtdPaperPlacementHandoff.Instance;
    private readonly SemaphoreSlim makerSync = new(1, 1);
    private readonly SemaphoreSlim sync = new(1, 1);

    public async Task ApplyMakerGtdUpdateAsync(
        MarketDataUpdate update,
        DateTimeOffset receivedAtUtc,
        IReadOnlySet<Guid> eligibleMakerGtdPaperOrderIds,
        CancellationToken cancellationToken = default,
        MarketDataSideEffectExecutionTrace? executionTrace = null)
    {
        if (string.IsNullOrWhiteSpace(update.AssetId) ||
            eligibleMakerGtdPaperOrderIds.Count == 0 ||
            update.EventType is not (
                MarketDataEventType.Book or
                MarketDataEventType.PriceChange or
                MarketDataEventType.LastTradePrice or
                MarketDataEventType.BestBidAsk))
        {
            return;
        }

        executionTrace?.EnterPhase(MarketDataSideEffectPhases.WaitForPublication, DateTimeOffset.UtcNow);
        await makerGtdHandoff.WaitForPublicationAsync(
            eligibleMakerGtdPaperOrderIds,
            cancellationToken);

        var operationStarted = Stopwatch.GetTimestamp();
        var lockWaitStarted = Stopwatch.GetTimestamp();
        executionTrace?.EnterPhase(MarketDataSideEffectPhases.WaitForSerializationLock, DateTimeOffset.UtcNow);
        await makerSync.WaitAsync(cancellationToken);
        var lockWaitDuration = Stopwatch.GetElapsedTime(lockWaitStarted);
        if (lockWaitDuration >= TimeSpan.FromSeconds(1))
        {
            logger.LogWarning(
                "Maker-GTD Paper market-data updater waited for its dedicated serialization lock. AssetId={AssetId} EventType={EventType} WaitDurationMs={WaitDurationMs}",
                update.AssetId,
                update.EventType,
                lockWaitDuration.TotalMilliseconds);
        }

        var phase = "LoadExposureSnapshot";
        var operation = "ExposureSnapshotCache.GetSnapshot";
        Guid? paperOrderId = null;
        var pendingMakerOrderIds = eligibleMakerGtdPaperOrderIds.ToHashSet();
        try
        {
            executionTrace?.EnterPhase(MarketDataSideEffectPhases.LoadExposureSnapshot, DateTimeOffset.UtcNow);
            var exposure = await exposureCache.GetSnapshotAsync(cancellationToken);
            var matchingOrders = exposure.OpenPaperOrders
                .Where(order =>
                    string.Equals(order.AssetId, update.AssetId, StringComparison.OrdinalIgnoreCase) &&
                    eligibleMakerGtdPaperOrderIds.Contains(order.Id) &&
                    MakerGtdPaperExecutionContract.IsMakerGtdOrder(order))
                .ToArray();
            var positions = exposure.PaperPositions
                .Where(position => string.Equals(position.AssetId, update.AssetId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var order in matchingOrders)
            {
                paperOrderId = order.Id;
                executionTrace?.EnterPhase(MarketDataSideEffectPhases.ApplyMakerGtdPaperUpdate, DateTimeOffset.UtcNow);
                phase = "ApplyMakerGtdPaperUpdate";
                operation = "IAppRepository.TryApplyMakerGtdPaperFullFill";
                await TryApplyMakerGtdPaperUpdateAsync(
                    order,
                    update,
                    receivedAtUtc,
                    positions,
                    cancellationToken);
                pendingMakerOrderIds.Remove(order.Id);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordMakerGtdMarketDataFailure(
                pendingMakerOrderIds,
                update,
                receivedAtUtc,
                MakerGtdPaperExecutionContract.MarketDataApplyFailureCode);

            var duration = Stopwatch.GetElapsedTime(operationStarted);
            logger.LogError(
                ex,
                "Failed to apply dedicated Maker-GTD WebSocket evidence. AssetId={AssetId} EventType={EventType} Phase={Phase} Operation={Operation} PaperOrderId={PaperOrderId} DurationMs={DurationMs}",
                update.AssetId,
                update.EventType,
                phase,
                operation,
                paperOrderId,
                duration.TotalMilliseconds);
            await TryRecordApiErrorAsync(
                $"ApplyMakerGtdUpdate/{phase}",
                $"AssetId={update.AssetId}; EventType={update.EventType}; Operation={operation}; PaperOrderId={paperOrderId?.ToString() ?? "<null>"}; DurationMs={duration.TotalMilliseconds:F0}; Error={ex.Message}",
                cancellationToken);
        }
        finally
        {
            makerSync.Release();
        }
    }

    public async Task ApplyUpdateAsync(
        MarketDataUpdate update,
        DateTimeOffset? receivedAtUtc = null,
        IReadOnlySet<Guid>? eligiblePaperOrderIds = null,
        CancellationToken cancellationToken = default,
        MarketDataSideEffectExecutionTrace? executionTrace = null)
    {
        if (string.IsNullOrWhiteSpace(update.AssetId))
        {
            return;
        }

        executionTrace?.EnterPhase(MarketDataSideEffectPhases.WaitForPublication, DateTimeOffset.UtcNow);
        await makerGtdHandoff.WaitForPublicationAsync(
            eligiblePaperOrderIds,
            cancellationToken);

        var operationStarted = Stopwatch.GetTimestamp();
        var lockWaitStarted = Stopwatch.GetTimestamp();
        executionTrace?.EnterPhase(MarketDataSideEffectPhases.WaitForSerializationLock, DateTimeOffset.UtcNow);
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
        HashSet<Guid>? pendingMakerOrderIds = null;
        try
        {
            if (update.MarketResolved)
            {
                executionTrace?.EnterPhase(MarketDataSideEffectPhases.SettleMarketResolution, DateTimeOffset.UtcNow);
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
            executionTrace?.EnterPhase(MarketDataSideEffectPhases.LoadExposureSnapshot, DateTimeOffset.UtcNow);
            phase = "LoadExposureSnapshot";
            operation = "ExposureSnapshotCache.GetSnapshot";
            var exposure = await exposureCache.GetSnapshotAsync(cancellationToken);
            var matchingOrders = exposure.OpenPaperOrders
                .Where(order =>
                    string.Equals(order.AssetId, update.AssetId, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(order.ExecutionSource, PaperLiveShadowTestSource, StringComparison.OrdinalIgnoreCase) &&
                    (eligiblePaperOrderIds is null || eligiblePaperOrderIds.Contains(order.Id)))
                .ToArray();
            pendingMakerOrderIds = matchingOrders
                .Where(MakerGtdPaperExecutionContract.IsMakerGtdOrder)
                .Select(order => order.Id)
                .ToHashSet();
            var positions = exposure.PaperPositions
                .Where(position => string.Equals(position.AssetId, update.AssetId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var order in matchingOrders)
            {
                paperOrderId = order.Id;
                if (MakerGtdPaperExecutionContract.IsMakerGtdOrder(order))
                {
                    executionTrace?.EnterPhase(MarketDataSideEffectPhases.ApplyMakerGtdPaperUpdate, DateTimeOffset.UtcNow);
                    phase = "ApplyMakerGtdPaperUpdate";
                    operation = "IAppRepository.TryApplyMakerGtdPaperFullFill";
                    await TryApplyMakerGtdPaperUpdateAsync(
                        order,
                        update,
                        receivedAtUtc,
                        positions,
                        cancellationToken);
                    pendingMakerOrderIds.Remove(order.Id);
                    continue;
                }

                executionTrace?.EnterPhase(MarketDataSideEffectPhases.ApplyOrdinaryPaperUpdate, DateTimeOffset.UtcNow);
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

                if (feeAccountingService is not null)
                {
                    fill = await feeAccountingService.ApplyToPaperFillAsync(orderForFill, fill, cancellationToken);
                }

                var currentBid = update.OrderBookSnapshot?.BestBid ?? currentPosition?.AveragePrice ?? 0m;
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
                executionTrace?.EnterPhase(MarketDataSideEffectPhases.UpdatePositionMarks, DateTimeOffset.UtcNow);
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
            RecordMakerGtdMarketDataFailure(
                pendingMakerOrderIds ?? eligiblePaperOrderIds,
                update,
                receivedAtUtc ?? update.ReceivedAtUtc,
                MakerGtdPaperExecutionContract.MarketDataApplyFailureCode);

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

    private async Task TryApplyMakerGtdPaperUpdateAsync(
        PaperOrder order,
        MarketDataUpdate update,
        DateTimeOffset? receivedAtUtc,
        List<PaperPosition> positions,
        CancellationToken cancellationToken)
    {
        if (!MakerGtdPaperOrderEvidenceParser.TryParse(
                order,
                out var orderEvidence,
                out var parseFailure) ||
            orderEvidence is null)
        {
            logger.LogWarning(
                "Maker-GTD Paper update skipped because acceptance evidence is invalid. PaperOrderId={PaperOrderId} Detail={Detail}",
                order.Id,
                parseFailure);
            return;
        }

        var processedAtUtc = DateTimeOffset.UtcNow;
        var eventReceivedAtUtc = receivedAtUtc ?? update.ReceivedAtUtc;
        if (!update.HasAuthoritativeSourceTimestamp ||
            update.SourceTimestampUtc is not { } sourceTimestampUtc ||
            eventReceivedAtUtc is not { } receiptTimestampUtc ||
            string.IsNullOrWhiteSpace(update.EventFingerprint) ||
            receiptTimestampUtc <= order.CreatedAtUtc ||
            receiptTimestampUtc <= orderEvidence.AcceptedAtUtc ||
            receiptTimestampUtc >= order.ExpiresAtUtc ||
            receiptTimestampUtc > processedAtUtc)
        {
            return;
        }

        var sourceAge = receiptTimestampUtc - sourceTimestampUtc;
        if (sourceAge < TimeSpan.Zero)
        {
            sourceAge = TimeSpan.Zero;
        }

        var touchEvidence = new MakerGtdTouchNoDepthEvidence(
            update.EventType,
            update.AssetId,
            update.ConditionId,
            LastTradePrice: update.EventType == MarketDataEventType.LastTradePrice
                ? update.Price
                : null,
            BestAsk: update.EventType is MarketDataEventType.Book or
                MarketDataEventType.PriceChange or
                MarketDataEventType.BestBidAsk
                    ? update.BestAsk ?? update.OrderBookSnapshot?.BestAsk
                    : null,
            TimestampUtc: sourceTimestampUtc,
            TimestampIsAuthoritative: true,
            IsCurrent: sourceAge <= makerMaximumEventAge,
            IsDuplicateEvent: false);
        var evaluation = MakerGtdTouchNoDepthEvaluator.Evaluate(
            new MakerGtdRestingBuyOrder(
                order.AssetId,
                order.ConditionId,
                order.Price,
                order.SizeShares,
                orderEvidence.AcceptedAtUtc,
                order.ExpiresAtUtc),
            touchEvidence);
        if (!evaluation.Filled)
        {
            return;
        }

        var linkedRuns = await repository.GetStrategyMarketPaperRunsByPaperOrderIdsAsync(
            [order.Id],
            cancellationToken);
        var matchingRuns = linkedRuns
            .Where(run => run.PaperOrderId == order.Id)
            .ToArray();
        if (matchingRuns.Length != 1)
        {
            RecordMakerGtdMarketDataFailure(
                order.Id,
                update,
                receiptTimestampUtc,
                MakerGtdPaperExecutionContract.MarketDataApplyFailureCode);
            logger.LogWarning(
                "Maker-GTD Paper update skipped because exactly one linked strategy run was not available. PaperOrderId={PaperOrderId} LinkedRunCount={LinkedRunCount}",
                order.Id,
                matchingRuns.Length);
            return;
        }

        var restingRun = matchingRuns[0];

        var fill = new PaperFill(
            Guid.NewGuid(),
            order.Id,
            order.Price,
            order.SizeShares,
            sourceTimestampUtc,
            JsonSerializer.Serialize(new
            {
                model = "touch_no_depth",
                reason_code = evaluation.ReasonCode,
                event_type = update.EventType.ToString(),
                trigger = evaluation.Trigger.ToString(),
                trigger_price = evaluation.TriggerPrice,
                source_timestamp_utc = sourceTimestampUtc,
                received_at_utc = receiptTimestampUtc,
                processed_at_utc = processedAtUtc,
                source_event_id = update.SourceEventId,
                event_fingerprint = update.EventFingerprint
            }),
            FeeLiquidityRole: FeeLiquidityRole.Maker.ToString());
        if (feeAccountingService is not null)
        {
            fill = await feeAccountingService.ApplyToPaperFillAsync(order, fill, cancellationToken);
        }

        var expectedPosition = FindPosition(positions, order);
        var filledOrder = order with
        {
            Status = PaperOrderStatus.Filled,
            FilledAtUtc = fill.FilledAtUtc,
            CancelledAtUtc = null
        };
        var enteredRun = restingRun with
        {
            Status = StrategyMarketPaperRunStatuses.Entered,
            EntryPrice = fill.Price,
            StakeUsd = order.NotionalUsd,
            SizeShares = fill.SizeShares,
            EnteredAtUtc = fill.FilledAtUtc,
            SkipReason = null,
            SkipDiagnosticsJson = null,
            UpdatedAtUtc = fill.FilledAtUtc,
            FeeUsd = fill.FeeUsd,
            FeeAccountingStatus = fill.FeeAccountingStatus,
            FeeLiquidityRole = fill.FeeLiquidityRole,
            FeeCalculationSource = fill.FeeCalculationSource,
            FeeRate = fill.FeeRate,
            FeeExponent = fill.FeeExponent,
            FeeTakerOnly = fill.FeeTakerOnly,
            FeeCalculatedAtUtc = fill.FeeCalculatedAtUtc
        };

        MakerGtdPaperMutationResult mutation;
        PaperPosition updatedPosition;
        var mutationAttempt = 0;
        var retryPositionConflict = false;
        do
        {
            mutationAttempt++;
            var positionHasNewerMark = expectedPosition is not null &&
                expectedPosition.UpdatedAtUtc > receiptTimestampUtc;
            var currentBid = positionHasNewerMark && expectedPosition!.SizeShares > 0m
                ? expectedPosition.EstimatedValueUsd / expectedPosition.SizeShares
                : update.BestBid ??
                    update.OrderBookSnapshot?.BestBid ??
                    expectedPosition?.AveragePrice ??
                    fill.Price;
            var positionUpdatedAtUtc = positionHasNewerMark
                ? expectedPosition!.UpdatedAtUtc
                : receiptTimestampUtc;
            updatedPosition = paperTradingEngine.ApplyBuyFill(
                expectedPosition,
                order,
                fill,
                currentBid,
                positionUpdatedAtUtc);
            mutation = await repository.TryApplyMakerGtdPaperFullFillAsync(
                new MakerGtdPaperFullFillRequest(
                    MakerGtdPaperExecutionContract.ExecutionSource,
                    filledOrder,
                    fill,
                    expectedPosition,
                    updatedPosition,
                    enteredRun),
                cancellationToken);
            retryPositionConflict =
                mutation.Outcome == MakerGtdPaperMutationOutcome.NotEligible &&
                string.Equals(
                    mutation.ReasonCode,
                    MakerGtdPaperMutationReasonCodes.PositionConcurrencyConflict,
                    StringComparison.Ordinal) &&
                mutationAttempt < MakerPositionCasMaximumAttempts;
            if (retryPositionConflict)
            {
                expectedPosition = mutation.PaperPosition;
            }
        }
        while (retryPositionConflict);

        if (mutation.Outcome == MakerGtdPaperMutationOutcome.NotEligible)
        {
            RecordMakerGtdMarketDataFailure(
                order.Id,
                update,
                receiptTimestampUtc,
                MakerGtdPaperExecutionContract.MarketDataApplyFailureCode);
            logger.LogWarning(
                "Maker-GTD Paper full fill was not eligible for atomic persistence. PaperOrderId={PaperOrderId} ReasonCode={ReasonCode} MutationAttempts={MutationAttempts} MismatchDiagnostic={MismatchDiagnostic}",
                order.Id,
                mutation.ReasonCode,
                mutationAttempt,
                mutation.MismatchDiagnostic is null
                    ? null
                    : JsonSerializer.Serialize(mutation.MismatchDiagnostic));
            return;
        }

        makerGtdHandoff.ClearMarketDataFailures(order.Id);

        if (mutation.PaperOrder is { } persistedOrder)
        {
            exposureCache.ApplyPaperOrder(persistedOrder);
        }

        if (mutation.PaperPosition is { } persistedPosition)
        {
            exposureCache.ApplyPaperPosition(persistedPosition);
            RemovePosition(positions, persistedPosition);
            positions.Add(persistedPosition);
        }
    }

    private void RecordMakerGtdMarketDataFailure(
        Guid paperOrderId,
        MarketDataUpdate update,
        DateTimeOffset? receivedAtUtc,
        string failureCode)
    {
        RecordMakerGtdMarketDataFailure(
            new HashSet<Guid> { paperOrderId },
            update,
            receivedAtUtc,
            failureCode);
    }

    private void RecordMakerGtdMarketDataFailure(
        IReadOnlySet<Guid>? paperOrderIds,
        MarketDataUpdate update,
        DateTimeOffset? receivedAtUtc,
        string failureCode)
    {
        if (receivedAtUtc is not { } failureReceivedAtUtc ||
            update.EventType is not (
                MarketDataEventType.Book or
                MarketDataEventType.PriceChange or
                MarketDataEventType.LastTradePrice or
                MarketDataEventType.BestBidAsk))
        {
            return;
        }

        makerGtdHandoff.RecordMarketDataFailure(
            update.AssetId,
            update.ConditionId,
            failureReceivedAtUtc,
            paperOrderIds,
            failureCode);
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
            var netUnrealizedPnl = FeeAccountingRules.IsAccounted(position.FeeAccountingStatus)
                ? unrealizedPnl - position.FeeUsd
                : (decimal?)null;
            if (estimatedValue == position.EstimatedValueUsd &&
                unrealizedPnl == position.UnrealizedPnlUsd &&
                netUnrealizedPnl == position.NetUnrealizedPnlUsd)
            {
                continue;
            }

            markUpdates.Add(new PaperPositionMarkUpdate(
                position,
                estimatedValue,
                unrealizedPnl,
                now,
                netUnrealizedPnl));
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
