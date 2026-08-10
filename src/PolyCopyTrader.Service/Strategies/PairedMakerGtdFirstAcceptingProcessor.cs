using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.GammaMarkets;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.Strategies;

public sealed class PairedMakerGtdFirstAcceptingProcessor(
    ILogger<PairedMakerGtdFirstAcceptingProcessor> logger,
    BotOptions botOptions,
    PaperTradingOptions paperTradingOptions,
    BtcUpDown5mStrategyOptions strategyOptions,
    IPolymarketClobPublicClient clobClient,
    IMarketDataCache marketDataCache,
    IActiveMarketAssetSubscriptionRegistry activeMarketAssetSubscriptionRegistry,
    IExposureSnapshotCache exposureCache,
    IMakerGtdPaperPlacementHandoff makerGtdPaperPlacementHandoff,
    IStrategyStateProvider strategyStateProvider,
    IAppRepository repository,
    TimeProvider? timeProvider = null) : IPairedMakerGtdFirstAcceptingProcessor
{
    private const int MaximumPlacementAttempts = 10;
    private const int DueRunLimit = 600;
    private const decimal MinimumStakeSafetyMultiplier = 1.10m;
    private const string StakeRoundingMode = "ceil_usd";
    private const string ClobBookSource = "clob_book";
    private const string EntryWindowElapsedReason = "paired_maker_gtd_entry_window_elapsed";
    private const string AttemptsExhaustedReason = "paired_maker_gtd_post_only_attempts_exhausted";
    private static readonly IReadOnlyList<Guid> StrategyIdsInScope =
        StrategyIds.PairedMakerGtdFirstAcceptingVariants.Select(variant => variant.Id).ToArray();
    private static readonly IReadOnlyDictionary<Guid, BtcUpDown5mStrategyVariant> VariantsById =
        StrategyIds.PairedMakerGtdFirstAcceptingVariants.ToDictionary(variant => variant.Id);

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim processingGate = new(1, 1);

    public async Task<PairedMakerGtdFirstAcceptingResult> ProcessFirstAcceptingMarketAsync(
        PairedMakerGtdFirstAcceptingCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            return new PairedMakerGtdFirstAcceptingResult();
        }

        await processingGate.WaitAsync(cancellationToken);
        try
        {
            var nowUtc = clock.GetUtcNow();
            if (!TryResolveMarket(candidate.Market, out var marketIdentity, out var rejectionReason))
            {
                logger.LogWarning(
                    "Paired Maker-GTD first-accepting candidate rejected by processor. MarketId={MarketId} ConditionId={ConditionId} Slug={Slug} Reason={Reason}",
                    candidate.Market.MarketId,
                    candidate.Market.ConditionId,
                    candidate.Market.Slug,
                    rejectionReason);
                return new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1);
            }

            var settings = await strategyStateProvider.GetStrategySettingsAsync(cancellationToken);
            var variants = GetExactPair(marketIdentity.AssetSymbol);
            if (variants.Count != 2 ||
                !variants.All(variant => IsEnabledForPaperEntry(variant, settings, nowUtc)))
            {
                return new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1);
            }

            if (candidate.RequestStartedAtUtc > candidate.ResponseCompletedAtUtc ||
                candidate.ResponseCompletedAtUtc > candidate.FirstObservedAcceptingAtUtc)
            {
                logger.LogWarning(
                    "Paired Maker-GTD first-accepting timestamps are invalid. MarketId={MarketId} RequestStarted={RequestStarted} ResponseCompleted={ResponseCompleted} FirstObserved={FirstObserved}",
                    candidate.Market.MarketId,
                    candidate.RequestStartedAtUtc,
                    candidate.ResponseCompletedAtUtc,
                    candidate.FirstObservedAcceptingAtUtc);
                return new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1);
            }

            var observationJson = BuildObservationJson(candidate).ToJsonString();
            var observedRuns = variants
                .Select(variant => CreateObservedRun(
                    candidate.Market,
                    marketIdentity,
                    variant,
                    candidate.FirstObservedAcceptingAtUtc,
                    observationJson))
                .ToArray();
            var insertedIds = await repository.TryAddStrategyMarketPaperRunsAsync(
                observedRuns,
                cancellationToken);

            if (insertedIds.Count != observedRuns.Length)
            {
                return new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1);
            }

            return await ProcessMarketRunsAsync(
                candidate.Market,
                observedRuns,
                settings,
                cancellationToken);
        }
        finally
        {
            processingGate.Release();
        }
    }

    public async Task<PairedMakerGtdFirstAcceptingResult> ProcessDueAsync(
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            return new PairedMakerGtdFirstAcceptingResult();
        }

        await processingGate.WaitAsync(cancellationToken);
        try
        {
            var nowUtc = clock.GetUtcNow();
            var dueRuns = await repository.GetDueStrategyMarketPaperRunsWithExpandedLastDueAsync(
                StrategyIdsInScope,
                StrategyMarketPaperRunStatuses.Observed,
                nowUtc,
                DueRunLimit,
                cancellationToken);
            if (dueRuns.Count == 0)
            {
                return new PairedMakerGtdFirstAcceptingResult();
            }

            var settings = await strategyStateProvider.GetStrategySettingsAsync(cancellationToken);
            var result = new PairedMakerGtdFirstAcceptingResult();
            foreach (var group in dueRuns.GroupBy(run => new { run.MarketId, run.ConditionId }))
            {
                var runs = group.ToArray();
                var market = await repository.GetPolymarketGammaMarketAsync(
                    group.Key.MarketId,
                    cancellationToken);
                if (market is null)
                {
                    if (runs.All(run => run.MarketStartUtc is { } startUtc && nowUtc >= startUtc))
                    {
                        var skipped = await PersistTerminalSkipsAsync(
                            runs,
                            EntryWindowElapsedReason,
                            cancellationToken);
                        result = Add(result, markets: 1, accepted: 0, skipped);
                    }

                    continue;
                }

                activeMarketAssetSubscriptionRegistry.AddOrUpdateMarkets(
                    [market],
                    protectFromFullScanRetention: true);
                var marketResult = await ProcessMarketRunsAsync(
                    market,
                    runs,
                    settings,
                    cancellationToken);
                result = Add(
                    result,
                    marketResult.MarketsProcessed,
                    marketResult.LegsAccepted,
                    marketResult.LegsSkipped);
            }

            return result;
        }
        finally
        {
            processingGate.Release();
        }
    }

    private async Task<PairedMakerGtdFirstAcceptingResult> ProcessMarketRunsAsync(
        PolymarketGammaMarket market,
        IReadOnlyCollection<StrategyMarketPaperRun> runs,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> settings,
        CancellationToken cancellationToken)
    {
        var nowUtc = clock.GetUtcNow();
        if (!TryResolveMarket(market, out var marketIdentity, out _))
        {
            var skipped = nowUtc >= (runs.Select(run => run.MarketStartUtc).FirstOrDefault(value => value is not null) ?? nowUtc)
                ? await PersistTerminalSkipsAsync(runs, EntryWindowElapsedReason, cancellationToken)
                : 0;
            return new PairedMakerGtdFirstAcceptingResult(1, 0, skipped);
        }

        var pairVariants = GetExactPair(marketIdentity.AssetSymbol);
        var runByStrategyId = runs
            .Where(run => VariantsById.ContainsKey(StrategyIds.Normalize(run.StrategyId)))
            .GroupBy(run => StrategyIds.Normalize(run.StrategyId))
            .ToDictionary(group => group.Key, group => group.First());
        var activeVariants = pairVariants
            .Where(variant => runByStrategyId.ContainsKey(variant.Id))
            .ToArray();

        if (activeVariants.Length == 0 || activeVariants.Length != runs.Count)
        {
            var skipped = nowUtc >= marketIdentity.MarketStartUtc
                ? await PersistTerminalSkipsAsync(runs, EntryWindowElapsedReason, cancellationToken)
                : 0;
            return new PairedMakerGtdFirstAcceptingResult(1, 0, skipped);
        }

        if (nowUtc >= marketIdentity.MarketStartUtc)
        {
            var skipped = await PersistTerminalSkipsAsync(runs, EntryWindowElapsedReason, cancellationToken);
            return new PairedMakerGtdFirstAcceptingResult(1, 0, skipped);
        }

        if (activeVariants.Any(variant => !IsEnabledForPaperEntry(variant, settings, nowUtc)))
        {
            var skipped = await PersistTerminalSkipsAsync(
                runs,
                "paired_maker_gtd_strategy_disabled_or_paused",
                cancellationToken);
            return new PairedMakerGtdFirstAcceptingResult(1, 0, skipped);
        }

        if (!market.Active || market.Closed || market.Archived ||
            !market.AcceptingOrders || !market.EnableOrderBook)
        {
            return new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1);
        }

        var states = new List<LegState>(activeVariants.Length);
        foreach (var variant in activeVariants)
        {
            if (!TryResolveOutcomeAssetId(market, variant.FixedOutcome!.Value, out var assetId, out var outcome))
            {
                return new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1);
            }

            states.Add(new LegState(
                runByStrategyId[variant.Id],
                variant,
                settings[variant.Id],
                assetId,
                outcome));
        }

        foreach (var state in states)
        {
            if (!TryValidatePersistedFirstAcceptingObservation(
                    state,
                    market,
                    marketIdentity,
                    out var observationRejectionReason))
            {
                logger.LogWarning(
                    "Paired Maker-GTD persisted first-accepting observation rejected. MarketId={MarketId} ConditionId={ConditionId} StrategyId={StrategyId} Reason={Reason}",
                    market.MarketId,
                    market.ConditionId,
                    state.Variant.Id,
                    observationRejectionReason);
                return new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1);
            }
        }

        if (!TryRecoverPersistedContinuations(
                states,
                marketIdentity.MarketStartUtc,
                clock.GetUtcNow(),
                out var recoveredAttemptsCompleted,
                out var recoveredFrozenCommonSize,
                out var continuationRejectionReason))
        {
            logger.LogWarning(
                "Paired Maker-GTD persisted continuation rejected. MarketId={MarketId} ConditionId={ConditionId} Reason={Reason}",
                market.MarketId,
                market.ConditionId,
                continuationRejectionReason);
            return new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1);
        }

        var allTokenIds = market.ClobTokenIds.ToArray();
        if (!IsMarketDataReady(allTokenIds))
        {
            return new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1);
        }

        var exposure = await exposureCache.GetSnapshotAsync(cancellationToken);
        var existingPairOrders = exposure.OpenPaperOrders
            .Where(order =>
                string.Equals(order.ConditionId, market.ConditionId, StringComparison.Ordinal) &&
                pairVariants.Any(variant => variant.Id == StrategyIds.Normalize(order.StrategyId)) &&
                string.Equals(
                    order.ExecutionSource,
                    PairedMakerGtdPaperExecutionContract.ExecutionSource,
                    StringComparison.Ordinal))
            .ToArray();
        var existingPairOrderSizes = existingPairOrders
            .Select(order => order.SizeShares)
            .Distinct()
            .ToArray();
        if (existingPairOrders
                .GroupBy(order => StrategyIds.Normalize(order.StrategyId))
                .Any(group => group.Count() != 1) ||
            existingPairOrderSizes.Length > 1 ||
            (recoveredFrozenCommonSize is { } recoveredSize &&
             existingPairOrderSizes.Length == 1 &&
             existingPairOrderSizes[0] != recoveredSize.Shares))
        {
            logger.LogWarning(
                "Paired Maker-GTD recovered common size disagrees with existing pair orders. MarketId={MarketId} ConditionId={ConditionId} RecoveredShares={RecoveredShares} ExistingShares={ExistingShares}",
                market.MarketId,
                market.ConditionId,
                recoveredFrozenCommonSize?.Shares,
                existingPairOrderSizes);
            return new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1);
        }

        if (states.Any(state => existingPairOrders.Any(order =>
                StrategyIds.Normalize(order.StrategyId) == state.Variant.Id)))
        {
            return new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1);
        }

        if (states.Count == 1 && recoveredFrozenCommonSize is null)
        {
            logger.LogWarning(
                "Paired Maker-GTD one-leg recovery is missing the durably frozen common size. MarketId={MarketId} ConditionId={ConditionId}",
                market.MarketId,
                market.ConditionId);
            return new PairedMakerGtdFirstAcceptingResult(MarketsProcessed: 1);
        }

        decimal? commonRequestedShares = recoveredFrozenCommonSize?.Shares;
        DateTimeOffset? commonSharesFrozenAtUtc = recoveredFrozenCommonSize?.FrozenAtUtc;

        var effectiveExpiresAtUtc = marketIdentity.MarketEndUtc;
        var clobGtdExpirationUtc = marketIdentity.MarketEndUtc.AddSeconds(
            MakerGtdBuyExecutionIntent.VenueEarlyExpirationSeconds);
        var activatedOrderIds = new HashSet<Guid>();
        var publishedOrderIds = new HashSet<Guid>();
        var transientStop = false;
        var marketStartElapsed = false;

        try
        {
            for (var attemptNumber = recoveredAttemptsCompleted + 1;
                 attemptNumber <= MaximumPlacementAttempts && states.Any(state => state.Accepted is null);
                 attemptNumber++)
            {
                var attemptStartedAtUtc = clock.GetUtcNow();
                if (attemptStartedAtUtc >= marketIdentity.MarketStartUtc)
                {
                    marketStartElapsed = true;
                    break;
                }

                if (!IsMarketDataReady(allTokenIds))
                {
                    transientStop = true;
                    break;
                }

                var pendingStates = states.Where(state => state.Accepted is null).ToArray();
                var attemptByState = new Dictionary<LegState, JsonObject>();
                foreach (var state in pendingStates)
                {
                    var attempt = new JsonObject
                    {
                        ["attempt_number"] = attemptNumber,
                        ["started_at_utc"] = FormatTimestamp(attemptStartedAtUtc)
                    };
                    state.Attempts.Add(attempt);
                    attemptByState.Add(state, attempt);
                }

                // Count every started attempt before the first external S0 read. A restart
                // may lose later stage evidence, but it must never reset the global cap.
                await PersistObservedContinuationsAsync(
                    pendingStates,
                    commonRequestedShares,
                    commonSharesFrozenAtUtc,
                    cancellationToken);

                var contexts = new Dictionary<LegState, LegAttemptContext>();
                foreach (var state in pendingStates)
                {
                    var attempt = attemptByState[state];
                    var s0Read = await ReadDirectOrderBookAsync(
                        state.AssetId,
                        "S0",
                        cancellationToken);
                    var s0EvaluatedAtUtc = clock.GetUtcNow();
                    attempt["s0"] = BuildBookEvidenceJson(
                        s0Read,
                        s0EvaluatedAtUtc,
                        GetMaximumQuoteAge());
                    if (s0Read.OrderBook is not { } s0)
                    {
                        SetAttemptFailure(
                            attempt,
                            "s0_evidence_unavailable",
                            s0Read.RejectionReason ?? "paired_maker_gtd_s0_missing");
                        continue;
                    }

                    var s0Reason = ValidateS0(
                        s0Read,
                        state.AssetId,
                        market.ConditionId,
                        s0EvaluatedAtUtc,
                        GetMaximumQuoteAge());
                    if (s0Reason is not null)
                    {
                        SetAttemptFailure(attempt, "s0_rejected", s0Reason);
                        continue;
                    }

                    var cap = state.Variant.MakerMaximumOrderPrice!.Value;
                    var tickSize = s0.TickSize!.Value;
                    var rawLimitPrice = Math.Min(cap, s0.BestAsk!.Value - tickSize);
                    var limitPrice = RoundDownToTick(rawLimitPrice, tickSize);
                    attempt["raw_limit_price"] = rawLimitPrice;
                    attempt["limit_price"] = limitPrice;
                    attempt["tick_size"] = tickSize;
                    if (limitPrice <= 0m || limitPrice >= s0.BestAsk.Value || limitPrice > cap)
                    {
                        SetAttemptFailure(
                            attempt,
                            "price_rejected",
                            "paired_maker_gtd_post_only_limit_price_invalid");
                        continue;
                    }

                    var sizing = CreateMinimumStakeSizing(
                        s0.MinOrderSize!.Value,
                        limitPrice,
                        state.Settings.PaperStakeAmount);
                    attempt["stake_sizing"] = BuildSizingJson(sizing);
                    if (!sizing.Available)
                    {
                        SetAttemptFailure(
                            attempt,
                            "sizing_rejected",
                            sizing.RejectionReason ?? "paired_maker_gtd_stake_sizing_rejected");
                        continue;
                    }

                    contexts.Add(state, new LegAttemptContext(attempt, s0Read, limitPrice, sizing));
                }

                // Persist the completed S0 evidence before any S1 can accept a leg.
                await PersistObservedContinuationsAsync(
                    pendingStates,
                    commonRequestedShares,
                    commonSharesFrozenAtUtc,
                    cancellationToken);

                if (commonRequestedShares is null)
                {
                    if (!IsMarketDataReady(allTokenIds))
                    {
                        transientStop = true;
                        break;
                    }

                    if (states.Count != 2 || contexts.Count != 2)
                    {
                        foreach (var context in contexts.Values)
                        {
                            SetAttemptFailure(
                                context.Attempt,
                                "pair_sizing_wait",
                                "paired_maker_gtd_peer_s0_unavailable");
                        }

                        await PersistObservedContinuationsAsync(
                            pendingStates,
                            commonRequestedShares,
                            commonSharesFrozenAtUtc,
                            cancellationToken);
                        continue;
                    }

                    var commonFreezeAtUtc = clock.GetUtcNow();
                    var staleAtCommonFreeze = contexts
                        .Select(entry => new
                        {
                            Context = entry.Value,
                            Reason = ValidateS0(
                                entry.Value.S0Read,
                                entry.Key.AssetId,
                                market.ConditionId,
                                commonFreezeAtUtc,
                                GetMaximumQuoteAge())
                        })
                        .Where(entry => entry.Reason is not null)
                        .ToArray();
                    if (staleAtCommonFreeze.Length > 0)
                    {
                        foreach (var context in contexts.Values)
                        {
                            var ownFailure = staleAtCommonFreeze.FirstOrDefault(entry =>
                                ReferenceEquals(entry.Context, context));
                            SetAttemptFailure(
                                context.Attempt,
                                ownFailure is null ? "pair_sizing_wait" : "s0_rejected_at_common_freeze",
                                ownFailure?.Reason ?? "paired_maker_gtd_peer_s0_not_current_at_common_freeze");
                        }

                        await PersistObservedContinuationsAsync(
                            pendingStates,
                            commonRequestedShares,
                            commonSharesFrozenAtUtc,
                            cancellationToken);
                        continue;
                    }

                    commonRequestedShares = contexts.Values.Max(context => context.Sizing.TargetSizeShares);
                    commonSharesFrozenAtUtc = commonFreezeAtUtc;
                    await PersistFrozenCommonSizeAsync(
                        states,
                        commonRequestedShares.Value,
                        commonSharesFrozenAtUtc.Value,
                        cancellationToken);
                }

                foreach (var state in pendingStates)
                {
                    if (!contexts.TryGetValue(state, out var context))
                    {
                        continue;
                    }

                    var frozenAtUtc = clock.GetUtcNow();
                    if (frozenAtUtc >= marketIdentity.MarketStartUtc)
                    {
                        SetAttemptFailure(
                            context.Attempt,
                            "entry_window_elapsed",
                            EntryWindowElapsedReason);
                        marketStartElapsed = true;
                        break;
                    }

                    var s0AtIntentFreezeReason = ValidateS0(
                        context.S0Read,
                        state.AssetId,
                        market.ConditionId,
                        frozenAtUtc,
                        GetMaximumQuoteAge());
                    if (s0AtIntentFreezeReason is not null)
                    {
                        SetAttemptFailure(
                            context.Attempt,
                            "s0_rejected_at_intent_freeze",
                            s0AtIntentFreezeReason);
                        continue;
                    }

                    MakerGtdBuyExecutionIntent intent;
                    try
                    {
                        intent = MakerGtdBuyExecutionIntent.Create(
                            state.Variant.Id,
                            Guid.NewGuid(),
                            market.ConditionId,
                            state.AssetId,
                            state.Variant.MakerMaximumOrderPrice!.Value,
                            context.LimitPrice,
                            commonRequestedShares.Value * context.LimitPrice,
                            commonRequestedShares.Value,
                            context.S0,
                            frozenAtUtc,
                            effectiveExpiresAtUtc,
                            clobGtdExpirationUtc);
                    }
                    catch (ArgumentException ex)
                    {
                        SetAttemptFailure(
                            context.Attempt,
                            "intent_rejected",
                            "paired_maker_gtd_execution_intent_invalid:" + ex.Message);
                        continue;
                    }

                    context.Attempt["frozen_intent"] = BuildFrozenIntentJson(intent);
                    var parity = MakerGtdExecutionParity.Validate(intent);
                    if (!parity.IsValid || intent.TargetSizeShares != commonRequestedShares.Value)
                    {
                        context.Attempt["intent_validation_errors"] =
                            JsonSerializer.SerializeToNode(parity.Errors);
                        SetAttemptFailure(
                            context.Attempt,
                            "intent_rejected",
                            "paired_maker_gtd_execution_intent_invalid");
                        continue;
                    }

                    var admission = await makerGtdPaperPlacementHandoff.EnterPlacementAdmissionAsync(
                        state.AssetId,
                        cancellationToken);
                    try
                    {
                        var s1Read = await ReadDirectOrderBookAsync(
                            state.AssetId,
                            "S1",
                            cancellationToken);
                        var s1EvaluatedAtUtc = clock.GetUtcNow();
                        context.Attempt["s1"] = BuildBookEvidenceJson(
                            s1Read,
                            s1EvaluatedAtUtc,
                            GetMaximumQuoteAge());
                        if (s1EvaluatedAtUtc >= marketIdentity.MarketStartUtc)
                        {
                            SetAttemptFailure(
                                context.Attempt,
                                "entry_window_elapsed",
                                EntryWindowElapsedReason);
                            marketStartElapsed = true;
                            break;
                        }

                        if (s1Read.OrderBook is not { } s1)
                        {
                            SetAttemptFailure(
                                context.Attempt,
                                "s1_evidence_unavailable",
                                s1Read.RejectionReason ?? "paired_maker_gtd_s1_missing");
                            continue;
                        }

                        var s1ValidationReason = ValidateS1(
                            s1Read,
                            state.AssetId,
                            market.ConditionId,
                            s1EvaluatedAtUtc,
                            GetMaximumQuoteAge(),
                            intent);
                        if (s1ValidationReason is not null)
                        {
                            SetAttemptFailure(
                                context.Attempt,
                                "s1_evidence_unavailable",
                                s1ValidationReason);
                            continue;
                        }

                        var acceptance = MakerGtdPaperPostOnlyAcceptanceEvaluator.Evaluate(
                            new MakerGtdFrozenPostOnlyBuyIntent(
                                intent.AssetId,
                                intent.ConditionId,
                                intent.LimitPrice,
                                intent.TargetSizeShares,
                                intent.FrozenAtUtc),
                            new MakerGtdPostOnlyBookEvidence(
                                s1.AssetId,
                                s1.ConditionId,
                                s1.BestAsk,
                                s1.SourceTimestampUtc ?? s1.SnapshotAtUtc,
                                s1.ReceivedAtUtc.GetValueOrDefault(),
                                s1.HasAuthoritativeSourceTimestamp,
                                IsCurrent: true,
                                IsDuplicateDelivery: false));
                        context.Attempt["acceptance_outcome"] = acceptance.Outcome.ToString();
                        context.Attempt["acceptance_reason_code"] = acceptance.ReasonCode;
                        context.Attempt["observed_best_ask"] = acceptance.ObservedBestAsk;
                        context.Attempt["s1_received_at_utc"] = FormatTimestamp(acceptance.AcceptedAtUtc);
                        if (!acceptance.Accepted || acceptance.AcceptedAtUtc is not { } s1ReceivedAtUtc)
                        {
                            SetAttemptFailure(
                                context.Attempt,
                                "post_only_not_accepted",
                                acceptance.ReasonCode);
                            continue;
                        }

                        if (s1ReceivedAtUtc >= marketIdentity.MarketStartUtc)
                        {
                            SetAttemptFailure(
                                context.Attempt,
                                "entry_window_elapsed",
                                EntryWindowElapsedReason);
                            marketStartElapsed = true;
                            break;
                        }

                        if (!TryCaptureAcceptanceMarketData(
                                allTokenIds,
                                state.AssetId,
                                out var acceptedMarketData))
                        {
                            SetAttemptFailure(
                                context.Attempt,
                                "market_data_unavailable",
                                "paired_maker_gtd_market_data_not_healthy_at_acceptance");
                            transientStop = true;
                            break;
                        }

                        var acceptedAtUtc = clock.GetUtcNow();
                        if (!IsDirectBookCurrent(
                                s1Read,
                                acceptedAtUtc,
                                GetMaximumQuoteAge(),
                                out _))
                        {
                            SetAttemptFailure(
                                context.Attempt,
                                "s1_evidence_unavailable",
                                "paired_maker_gtd_s1_book_not_current_at_acceptance");
                            continue;
                        }

                        if (acceptedAtUtc >= marketIdentity.MarketStartUtc)
                        {
                            SetAttemptFailure(
                                context.Attempt,
                                "entry_window_elapsed",
                                EntryWindowElapsedReason);
                            marketStartElapsed = true;
                            break;
                        }

                        var paperOrderId = Guid.NewGuid();
                        admission.ActivatePendingOrder(
                            paperOrderId,
                            PairedMakerGtdPaperExecutionContract.ExecutionSource);
                        activatedOrderIds.Add(paperOrderId);
                        context.Attempt["outcome"] = "accepted_resting";
                        context.Attempt["reason_code"] = acceptance.ReasonCode;
                        context.Attempt["accepted_at_utc"] = FormatTimestamp(acceptedAtUtc);
                        context.Attempt["completed_at_utc"] = FormatTimestamp(s1EvaluatedAtUtc);
                        state.Accepted = new AcceptedLeg(
                            paperOrderId,
                            intent,
                            acceptedAtUtc,
                            acceptedMarketData.Status,
                            acceptedMarketData.ConfirmedAssetsCount,
                            acceptedMarketData.ContinuityGeneration,
                            acceptedMarketData.AcceptedAssetSubscription);
                        await admission.DisposeAsync();
                        await PersistAcceptedLegAsync(
                            market,
                            state,
                            pairVariants,
                            commonRequestedShares.Value,
                            commonSharesFrozenAtUtc,
                            effectiveExpiresAtUtc,
                            publishedOrderIds,
                            cancellationToken);
                    }
                    finally
                    {
                        await admission.DisposeAsync();
                    }

                    if (marketStartElapsed || transientStop)
                    {
                        break;
                    }
                }

                if (marketStartElapsed || transientStop)
                {
                    break;
                }
            }

            var acceptedStates = states.Where(state => state.Accepted is not null).ToArray();
            var remainingStates = states.Where(state => state.Accepted is null).ToArray();
            var terminalReason = marketStartElapsed || clock.GetUtcNow() >= marketIdentity.MarketStartUtc
                ? EntryWindowElapsedReason
                : transientStop
                    ? null
                    : AttemptsExhaustedReason;

            var updatedRuns = new List<StrategyMarketPaperRun>(remainingStates.Length);
            foreach (var state in remainingStates)
            {
                if (terminalReason is not null)
                {
                    updatedRuns.Add(CreateSkippedRun(
                        state,
                        terminalReason,
                        commonRequestedShares,
                        commonSharesFrozenAtUtc));
                }
                else
                {
                    updatedRuns.Add(CreateObservedContinuationRun(
                        state,
                        commonRequestedShares,
                        commonSharesFrozenAtUtc));
                }
            }

            if (updatedRuns.Count > 0)
            {
                await repository.AddPaperEntryPersistenceBatchAsync(
                    new PaperEntryPersistenceBatch(
                        [],
                        [],
                        [],
                        [],
                        [],
                        updatedRuns),
                    cancellationToken);
            }

            logger.LogInformation(
                "Paired Maker-GTD first-accepting Paper processing completed. Asset={Asset} Market={MarketSlug} AcceptedLegs={AcceptedLegs} TerminalSkippedLegs={SkippedLegs} CommonRequestedShares={CommonRequestedShares} TransientStop={TransientStop}",
                marketIdentity.AssetSymbol,
                market.Slug,
                acceptedStates.Length,
                terminalReason is null ? 0 : remainingStates.Length,
                commonRequestedShares,
                transientStop);
            return new PairedMakerGtdFirstAcceptingResult(
                MarketsProcessed: 1,
                LegsAccepted: acceptedStates.Length,
                LegsSkipped: terminalReason is null ? 0 : remainingStates.Length);
        }
        finally
        {
            foreach (var paperOrderId in activatedOrderIds.Except(publishedOrderIds))
            {
                makerGtdPaperPlacementHandoff.MarkFailed(paperOrderId);
            }
        }
    }

    private async Task PersistAcceptedLegAsync(
        PolymarketGammaMarket market,
        LegState state,
        IReadOnlyCollection<BtcUpDown5mStrategyVariant> pairVariants,
        decimal commonRequestedShares,
        DateTimeOffset? commonSharesFrozenAtUtc,
        DateTimeOffset effectiveExpiresAtUtc,
        ISet<Guid> publishedOrderIds,
        CancellationToken cancellationToken)
    {
        var accepted = state.Accepted ?? throw new InvalidOperationException(
            "An accepted paired Maker-GTD leg is required before persistence.");
        var rawDecisionJson = BuildAcceptedDecisionJson(
            market,
            state,
            pairVariants,
            commonRequestedShares,
            commonSharesFrozenAtUtc,
            effectiveExpiresAtUtc).ToJsonString();
        var signal = CreateSignal(market, state, accepted);
        var order = CreatePaperOrder(state, signal, accepted, rawDecisionJson);
        var restingRun = CreateRestingRun(state.Run, market, state, signal, order, accepted);
        await repository.AddPaperEntryPersistenceBatchAsync(
            new PaperEntryPersistenceBatch(
                [signal],
                [order],
                [],
                [],
                [],
                [restingRun]),
            cancellationToken);

        // Persistence is intentionally per leg. A later peer rejection, failure,
        // or process stop must not roll back this already accepted resting order.
        exposureCache.ApplyPaperOrder(order);
        makerGtdPaperPlacementHandoff.MarkPublished(order.Id);
        publishedOrderIds.Add(order.Id);
    }

    private async Task PersistFrozenCommonSizeAsync(
        IReadOnlyCollection<LegState> states,
        decimal commonRequestedShares,
        DateTimeOffset commonSharesFrozenAtUtc,
        CancellationToken cancellationToken)
    {
        if (states.Count != 2 || states.Any(state =>
                !string.Equals(
                    state.Run.Status,
                    StrategyMarketPaperRunStatuses.Observed,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Frozen paired Maker-GTD size requires both original Observed runs.");
        }

        var frozenRuns = states
            .Select(state => CreateObservedContinuationRun(
                state,
                commonRequestedShares,
                commonSharesFrozenAtUtc))
            .ToArray();
        await repository.UpdateStrategyMarketPaperRunsAsync(frozenRuns, cancellationToken);
        for (var index = 0; index < frozenRuns.Length; index++)
        {
            states.ElementAt(index).ReplaceRun(frozenRuns[index]);
        }
    }

    private async Task PersistObservedContinuationsAsync(
        IReadOnlyCollection<LegState> states,
        decimal? commonRequestedShares,
        DateTimeOffset? commonSharesFrozenAtUtc,
        CancellationToken cancellationToken)
    {
        if (states.Count == 0)
        {
            return;
        }

        if (states.Any(state => !string.Equals(
                state.Run.Status,
                StrategyMarketPaperRunStatuses.Observed,
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Paired Maker-GTD attempt continuation requires Observed runs.");
        }

        var continuedRuns = states
            .Select(state => CreateObservedContinuationRun(
                state,
                commonRequestedShares,
                commonSharesFrozenAtUtc))
            .ToArray();
        await repository.UpdateStrategyMarketPaperRunsAsync(continuedRuns, cancellationToken);
        for (var index = 0; index < continuedRuns.Length; index++)
        {
            states.ElementAt(index).ReplaceRun(continuedRuns[index]);
        }
    }

    private async Task<DirectBookRead> ReadDirectOrderBookAsync(
        string assetId,
        string stage,
        CancellationToken cancellationToken)
    {
        var requestStartedAtUtc = clock.GetUtcNow();
        try
        {
            var orderBook = await clobClient.GetOrderBookAsync(assetId, cancellationToken);
            var responseCompletedAtUtc = clock.GetUtcNow();
            return orderBook is null
                ? DirectBookRead.Reject(
                    "paired_maker_gtd_order_book_missing",
                    requestStartedAtUtc,
                    responseCompletedAtUtc)
                : DirectBookRead.Found(
                    orderBook,
                    requestStartedAtUtc,
                    responseCompletedAtUtc);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Direct CLOB book read failed for paired Maker-GTD Paper. Stage={Stage} TokenId={TokenId}",
                stage,
                assetId);
            await TryRecordApiErrorAsync(stage, ex.Message, cancellationToken);
            return DirectBookRead.Reject(
                "paired_maker_gtd_order_book_request_failed",
                requestStartedAtUtc,
                clock.GetUtcNow(),
                ex.Message);
        }
    }

    private async Task<int> PersistTerminalSkipsAsync(
        IReadOnlyCollection<StrategyMarketPaperRun> runs,
        string reason,
        CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return 0;
        }

        var nowUtc = clock.GetUtcNow();
        var skippedRuns = runs.Select(run => run with
        {
            Status = StrategyMarketPaperRunStatuses.Skipped,
            SkipReason = reason,
            SkipDiagnosticsJson = new JsonObject
            {
                ["execution_source"] = PairedMakerGtdPaperExecutionContract.ExecutionSource,
                ["skip_reason"] = reason,
                ["maker_gtd"] = new JsonObject
                {
                    ["execution_source"] = PairedMakerGtdPaperExecutionContract.ExecutionSource,
                    ["terminal_outcome"] = "skipped",
                    ["terminal_reason"] = reason
                },
                ["completed_at_utc"] = FormatTimestamp(nowUtc)
            }.ToJsonString(),
            UpdatedAtUtc = nowUtc
        }).ToArray();
        await repository.UpdateStrategyMarketPaperRunsAsync(skippedRuns, cancellationToken);
        return skippedRuns.Length;
    }

    private static StrategyMarketPaperRun CreateObservedRun(
        PolymarketGammaMarket market,
        MarketIdentity identity,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset firstObservedAcceptingAtUtc,
        string observationJson)
    {
        return new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            market.MarketId,
            market.ConditionId,
            market.Slug,
            market.Question,
            variant.Category,
            identity.MarketStartUtc,
            identity.MarketEndUtc,
            firstObservedAcceptingAtUtc,
            firstObservedAcceptingAtUtc,
            StrategyMarketPaperRunStatuses.Observed,
            SelectedAssetId: null,
            SelectedOutcome: variant.FixedOutcome?.ToString(),
            EntryPrice: null,
            StakeUsd: 0m,
            SizeShares: null,
            SignalId: null,
            PaperOrderId: null,
            EnteredAtUtc: null,
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            CreatedAtUtc: firstObservedAcceptingAtUtc,
            UpdatedAtUtc: firstObservedAcceptingAtUtc,
            SkipDiagnosticsJson: observationJson);
    }

    private static Signal CreateSignal(
        PolymarketGammaMarket market,
        LegState state,
        AcceptedLeg accepted)
    {
        var trade = new LeaderTrade(
            state.Variant.CopiedTraderWallet,
            state.Variant.Name,
            market.ConditionId,
            state.AssetId,
            market.Slug,
            market.Question,
            state.Outcome,
            TradeSide.Buy,
            accepted.Intent.LimitPrice,
            accepted.Intent.TargetSizeShares,
            accepted.Intent.TargetNotionalUsd,
            accepted.AcceptedAtUtc);
        return new Signal(
            Guid.NewGuid(),
            trade,
            Score: 100,
            Accepted: true,
            DecisionCode: state.Variant.Code + "_entry",
            Reasons: [],
            ProposedPaperPrice: accepted.Intent.LimitPrice,
            ProposedSizeShares: accepted.Intent.TargetSizeShares,
            ProposedNotionalUsd: accepted.Intent.TargetNotionalUsd,
            CreatedAtUtc: accepted.AcceptedAtUtc);
    }

    private static PaperOrder CreatePaperOrder(
        LegState state,
        Signal signal,
        AcceptedLeg accepted,
        string rawDecisionJson)
    {
        return new PaperOrder(
            accepted.PaperOrderId,
            signal.Id,
            state.Variant.CopiedTraderWallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            state.AssetId,
            signal.LeaderTrade.ConditionId,
            state.Outcome,
            accepted.Intent.LimitPrice,
            accepted.Intent.TargetSizeShares,
            accepted.Intent.TargetNotionalUsd,
            accepted.AcceptedAtUtc,
            accepted.Intent.EffectiveExpiresAtUtc,
            StrategyId: state.Variant.Id,
            RawDecisionJson: rawDecisionJson,
            ExecutionSource: PairedMakerGtdPaperExecutionContract.ExecutionSource);
    }

    private static StrategyMarketPaperRun CreateRestingRun(
        StrategyMarketPaperRun run,
        PolymarketGammaMarket market,
        LegState state,
        Signal signal,
        PaperOrder order,
        AcceptedLeg accepted)
    {
        return run with
        {
            ConditionId = market.ConditionId,
            MarketSlug = market.Slug,
            MarketTitle = market.Question,
            Category = state.Variant.Category,
            MarketEndUtc = market.EndDateUtc,
            Status = StrategyMarketPaperRunStatuses.Resting,
            SelectedAssetId = state.AssetId,
            SelectedOutcome = state.Outcome,
            EntryPrice = accepted.Intent.LimitPrice,
            StakeUsd = accepted.Intent.TargetNotionalUsd,
            SizeShares = accepted.Intent.TargetSizeShares,
            SignalId = signal.Id,
            PaperOrderId = order.Id,
            EnteredAtUtc = null,
            SkipReason = null,
            SkipDiagnosticsJson = null,
            UpdatedAtUtc = accepted.AcceptedAtUtc
        };
    }

    private StrategyMarketPaperRun CreateSkippedRun(
        LegState state,
        string reason,
        decimal? commonRequestedShares,
        DateTimeOffset? commonSharesFrozenAtUtc)
    {
        var nowUtc = clock.GetUtcNow();
        return state.Run with
        {
            Status = StrategyMarketPaperRunStatuses.Skipped,
            SkipReason = reason,
            SkipDiagnosticsJson = BuildContinuationDecisionJson(
                state,
                reason,
                commonRequestedShares,
                commonSharesFrozenAtUtc,
                nowUtc).ToJsonString(),
            UpdatedAtUtc = nowUtc
        };
    }

    private StrategyMarketPaperRun CreateObservedContinuationRun(
        LegState state,
        decimal? commonRequestedShares,
        DateTimeOffset? commonSharesFrozenAtUtc)
    {
        var nowUtc = clock.GetUtcNow();
        return state.Run with
        {
            Status = StrategyMarketPaperRunStatuses.Observed,
            SkipReason = null,
            SkipDiagnosticsJson = BuildContinuationDecisionJson(
                state,
                terminalReason: null,
                commonRequestedShares,
                commonSharesFrozenAtUtc,
                nowUtc).ToJsonString(),
            UpdatedAtUtc = nowUtc
        };
    }

    private static JsonObject BuildAcceptedDecisionJson(
        PolymarketGammaMarket market,
        LegState state,
        IReadOnlyCollection<BtcUpDown5mStrategyVariant> pairVariants,
        decimal commonRequestedShares,
        DateTimeOffset? commonSharesFrozenAtUtc,
        DateTimeOffset effectiveExpiresAtUtc)
    {
        var accepted = state.Accepted!;
        return new JsonObject
        {
            ["paper_only"] = true,
            ["post_only"] = true,
            ["order_type"] = MakerGtdBuyExecutionIntent.TimeInForce,
            ["execution_source"] = PairedMakerGtdPaperExecutionContract.ExecutionSource,
            ["paper_model_label"] = PairedMakerGtdPaperExecutionContract.MandatoryLabel,
            ["maker_rebate_modeled"] = false,
            ["pair"] = new JsonObject
            {
                ["pair_id"] = BuildPairId(market.ConditionId, state.Variant.ReferenceAssetSymbol),
                ["strategy_id"] = state.Variant.Id.ToString("D"),
                ["paired_strategy_id"] = state.Variant.PairedStrategyId?.ToString("D"),
                ["pair_strategy_ids"] = JsonSerializer.SerializeToNode(
                    pairVariants.Select(variant => variant.Id.ToString("D")).OrderBy(value => value)),
                ["common_requested_size_shares"] = commonRequestedShares,
                ["common_size_frozen_at_utc"] = FormatTimestamp(commonSharesFrozenAtUtc),
                ["atomic"] = false,
                ["rollback"] = false
            },
            ["first_accepting_observation"] = ParseObservationNode(state.Run.SkipDiagnosticsJson),
            ["maker_gtd"] = new JsonObject
            {
                ["contract_version"] = PairedMakerGtdPaperExecutionContract.ContractVersion,
                ["gap_recovery_policy_version"] =
                    PairedMakerGtdPaperExecutionContract.GapRecoveryLifecyclePolicyVersion,
                ["observation_gaps_backfilled"] = false,
                ["execution_source"] = PairedMakerGtdPaperExecutionContract.ExecutionSource,
                ["strategy_run_id"] = state.Run.Id.ToString("D"),
                ["paper_only"] = true,
                ["post_only"] = true,
                ["order_type"] = MakerGtdBuyExecutionIntent.TimeInForce,
                ["maximum_placement_attempts"] = MaximumPlacementAttempts,
                ["price_formula"] = PairedMakerGtdPaperExecutionContract.PriceFormula,
                ["maximum_order_price"] = state.Variant.MakerMaximumOrderPrice,
                ["market_start_utc"] = FormatTimestamp(state.Run.MarketStartUtc),
                ["market_end_utc"] = FormatTimestamp(state.Run.MarketEndUtc),
                ["effective_expires_at_utc"] = FormatTimestamp(effectiveExpiresAtUtc),
                ["clob_gtd_expiration_utc"] = FormatTimestamp(accepted.Intent.ClobGtdExpirationUtc),
                ["accepted_at_utc"] = FormatTimestamp(accepted.AcceptedAtUtc),
                ["frozen_intent"] = BuildFrozenIntentJson(accepted.Intent),
                ["attempts_completed"] = state.Attempts.Count,
                ["attempts"] = state.Attempts.DeepClone()
            },
            ["market_data_status_at_acceptance"] = new JsonObject
            {
                ["connection_state"] = accepted.Status.ConnectionState.ToString(),
                ["stale"] = accepted.Status.Stale,
                ["reconnect_count"] = accepted.Status.ReconnectCount,
                ["last_connected_utc"] = FormatTimestamp(accepted.Status.LastConnectedUtc),
                ["last_disconnected_utc"] = FormatTimestamp(accepted.Status.LastDisconnectedUtc),
                ["accepted_at_utc"] = FormatTimestamp(accepted.AcceptedAtUtc),
                ["asset_subscribed"] = true,
                ["asset_confirmed_live"] = accepted.AcceptedAssetSubscription.ConfirmedLive,
                ["asset_subscription_component"] = accepted.AcceptedAssetSubscription.Component,
                ["subscribed_assets_count"] = accepted.ConfirmedAssetsCount,
                ["continuity_generation"] = accepted.ContinuityGeneration,
                ["asset_subscription_generation"] = accepted.AcceptedAssetSubscription.Generation,
                ["asset_subscription_session_id"] = accepted.AcceptedAssetSubscription.SessionId
            }
        };
    }

    private static JsonObject BuildContinuationDecisionJson(
        LegState state,
        string? terminalReason,
        decimal? commonRequestedShares,
        DateTimeOffset? commonSharesFrozenAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        return new JsonObject
        {
            ["execution_source"] = PairedMakerGtdPaperExecutionContract.ExecutionSource,
            ["paper_model_label"] = PairedMakerGtdPaperExecutionContract.MandatoryLabel,
            ["skip_reason"] = terminalReason,
            ["updated_at_utc"] = FormatTimestamp(updatedAtUtc),
            ["first_accepting_observation"] = ParseObservationNode(state.Run.SkipDiagnosticsJson),
            ["pair"] = new JsonObject
            {
                ["strategy_id"] = state.Variant.Id.ToString("D"),
                ["paired_strategy_id"] = state.Variant.PairedStrategyId?.ToString("D"),
                ["common_requested_size_shares"] = commonRequestedShares,
                ["common_size_frozen_at_utc"] = FormatTimestamp(commonSharesFrozenAtUtc),
                ["atomic"] = false,
                ["rollback"] = false
            },
            ["maker_gtd"] = new JsonObject
            {
                ["execution_source"] = PairedMakerGtdPaperExecutionContract.ExecutionSource,
                ["contract_version"] = PairedMakerGtdPaperExecutionContract.ContractVersion,
                ["gap_recovery_policy_version"] =
                    PairedMakerGtdPaperExecutionContract.GapRecoveryLifecyclePolicyVersion,
                ["observation_gaps_backfilled"] = false,
                ["terminal_outcome"] = terminalReason is null ? "observed" : "skipped",
                ["terminal_reason"] = terminalReason,
                ["attempts_completed"] = state.Attempts.Count,
                ["attempts"] = state.Attempts.DeepClone()
            }
        };
    }

    private static JsonObject BuildObservationJson(PairedMakerGtdFirstAcceptingCandidate candidate)
    {
        return new JsonObject
        {
            ["phase"] = "first_accepting_observed",
            ["request_started_at_utc"] = FormatTimestamp(candidate.RequestStartedAtUtc),
            ["response_completed_at_utc"] = FormatTimestamp(candidate.ResponseCompletedAtUtc),
            ["first_observed_accepting_at_utc"] = FormatTimestamp(candidate.FirstObservedAcceptingAtUtc),
            ["market_id"] = candidate.Market.MarketId,
            ["condition_id"] = candidate.Market.ConditionId,
            ["market_slug"] = candidate.Market.Slug,
            ["accepting_orders"] = candidate.Market.AcceptingOrders,
            ["clob_token_ids"] = JsonSerializer.SerializeToNode(candidate.Market.ClobTokenIds)
        };
    }

    private static JsonNode? ParseObservationNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(json);
            return root?["first_accepting_observation"]?.DeepClone() ?? root;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryRecoverPersistedContinuations(
        IReadOnlyCollection<LegState> states,
        DateTimeOffset marketStartUtc,
        DateTimeOffset recoveryCheckedAtUtc,
        out int attemptsCompleted,
        out FrozenCommonSize? frozenCommonSize,
        out string? rejectionReason)
    {
        attemptsCompleted = 0;
        frozenCommonSize = null;
        rejectionReason = null;
        var recoveries = new List<(LegState State, PersistedContinuation Continuation)>(states.Count);
        foreach (var state in states)
        {
            if (!TryParsePersistedContinuation(state, out var continuation, out rejectionReason))
            {
                return false;
            }

            if (continuation.FrozenCommonSize is { } recoveredSize &&
                (recoveredSize.FrozenAtUtc < state.Run.DetectedAtUtc ||
                 recoveredSize.FrozenAtUtc >= marketStartUtc ||
                 recoveredSize.FrozenAtUtc > recoveryCheckedAtUtc))
            {
                rejectionReason = "paired_maker_gtd_continuation_common_size_timestamp_invalid";
                return false;
            }

            recoveries.Add((state, continuation));
        }

        var continuationCount = recoveries.Count(item => item.Continuation.IsContinuation);
        if (continuationCount == 0)
        {
            return true;
        }

        if (continuationCount != recoveries.Count)
        {
            rejectionReason = "paired_maker_gtd_continuation_pair_shape_mismatch";
            return false;
        }

        var first = recoveries[0].Continuation;
        if (recoveries.Any(item =>
                item.Continuation.Attempts.Count != first.Attempts.Count ||
                item.Continuation.FrozenCommonSize?.Shares != first.FrozenCommonSize?.Shares ||
                item.Continuation.FrozenCommonSize?.FrozenAtUtc != first.FrozenCommonSize?.FrozenAtUtc))
        {
            rejectionReason = "paired_maker_gtd_continuation_pair_state_mismatch";
            return false;
        }

        attemptsCompleted = first.Attempts.Count;
        frozenCommonSize = first.FrozenCommonSize;
        foreach (var (state, continuation) in recoveries)
        {
            foreach (var attempt in continuation.Attempts)
            {
                state.Attempts.Add(attempt.DeepClone());
            }
        }

        return true;
    }

    private static bool TryValidatePersistedFirstAcceptingObservation(
        LegState state,
        PolymarketGammaMarket market,
        MarketIdentity marketIdentity,
        out string rejectionReason)
    {
        rejectionReason = string.Empty;
        if (!string.Equals(state.Run.MarketId, market.MarketId, StringComparison.Ordinal) ||
            !string.Equals(state.Run.ConditionId, market.ConditionId, StringComparison.Ordinal) ||
            !string.Equals(state.Run.MarketSlug, market.Slug, StringComparison.Ordinal) ||
            state.Run.MarketStartUtc is not { } runMarketStartUtc ||
            !SameTimestamp(runMarketStartUtc, marketIdentity.MarketStartUtc) ||
            state.Run.MarketEndUtc is not { } runMarketEndUtc ||
            !SameTimestamp(runMarketEndUtc, marketIdentity.MarketEndUtc) ||
            !string.Equals(
                state.Run.SelectedOutcome,
                state.Variant.FixedOutcome?.ToString(),
                StringComparison.Ordinal))
        {
            rejectionReason = "paired_maker_gtd_observed_run_identity_mismatch";
            return false;
        }

        if (string.IsNullOrWhiteSpace(state.Run.SkipDiagnosticsJson))
        {
            rejectionReason = "paired_maker_gtd_observation_evidence_missing";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(state.Run.SkipDiagnosticsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                rejectionReason = "paired_maker_gtd_observation_evidence_not_object";
                return false;
            }

            var observation = root.TryGetProperty("first_accepting_observation", out var nestedObservation)
                ? nestedObservation
                : root;
            if (observation.ValueKind != JsonValueKind.Object ||
                !HasExactString(observation, "phase", "first_accepting_observed") ||
                !TryGetRequiredTimestamp(observation, "request_started_at_utc", out var requestStartedAtUtc) ||
                !TryGetRequiredTimestamp(observation, "response_completed_at_utc", out var responseCompletedAtUtc) ||
                !TryGetRequiredTimestamp(
                    observation,
                    "first_observed_accepting_at_utc",
                    out var firstObservedAcceptingAtUtc) ||
                requestStartedAtUtc > responseCompletedAtUtc ||
                responseCompletedAtUtc > firstObservedAcceptingAtUtc ||
                firstObservedAcceptingAtUtc >= marketIdentity.MarketStartUtc ||
                !SameTimestamp(firstObservedAcceptingAtUtc, state.Run.DetectedAtUtc) ||
                !SameTimestamp(firstObservedAcceptingAtUtc, state.Run.EntryDueAtUtc) ||
                !HasExactString(observation, "market_id", market.MarketId) ||
                !HasExactString(observation, "condition_id", market.ConditionId) ||
                !HasExactString(observation, "market_slug", market.Slug) ||
                !HasExactTrue(observation, "accepting_orders") ||
                !HasExactStringSet(observation, "clob_token_ids", market.ClobTokenIds))
            {
                rejectionReason = "paired_maker_gtd_first_accepting_observation_mismatch";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            rejectionReason = "paired_maker_gtd_observation_json_malformed";
            return false;
        }
    }

    private static bool TryParsePersistedContinuation(
        LegState state,
        out PersistedContinuation continuation,
        out string? rejectionReason)
    {
        continuation = PersistedContinuation.Initial;
        rejectionReason = null;
        if (string.IsNullOrWhiteSpace(state.Run.SkipDiagnosticsJson))
        {
            rejectionReason = "paired_maker_gtd_observation_evidence_missing";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(state.Run.SkipDiagnosticsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                rejectionReason = "paired_maker_gtd_observation_evidence_not_object";
                return false;
            }

            var hasContinuationMarker = root.TryGetProperty("execution_source", out _) ||
                root.TryGetProperty("pair", out _) ||
                root.TryGetProperty("maker_gtd", out _) ||
                root.TryGetProperty("first_accepting_observation", out _);
            if (!hasContinuationMarker)
            {
                if (HasExactString(root, "phase", "first_accepting_observed"))
                {
                    return true;
                }

                rejectionReason = "paired_maker_gtd_initial_observation_shape_invalid";
                return false;
            }

            if (!HasExactString(
                    root,
                    "execution_source",
                    PairedMakerGtdPaperExecutionContract.ExecutionSource) ||
                !HasExactString(
                    root,
                    "paper_model_label",
                    PairedMakerGtdPaperExecutionContract.MandatoryLabel) ||
                !root.TryGetProperty("skip_reason", out var skipReason) ||
                skipReason.ValueKind != JsonValueKind.Null ||
                !root.TryGetProperty("first_accepting_observation", out var observation) ||
                observation.ValueKind != JsonValueKind.Object ||
                !HasExactString(observation, "phase", "first_accepting_observed") ||
                !root.TryGetProperty("pair", out var pair) ||
                pair.ValueKind != JsonValueKind.Object ||
                !HasExactGuid(pair, "strategy_id", state.Variant.Id) ||
                state.Variant.PairedStrategyId is not { } pairedStrategyId ||
                !HasExactGuid(pair, "paired_strategy_id", pairedStrategyId) ||
                !HasExactFalse(pair, "atomic") ||
                !HasExactFalse(pair, "rollback") ||
                !root.TryGetProperty("maker_gtd", out var makerGtd) ||
                makerGtd.ValueKind != JsonValueKind.Object ||
                !HasExactString(
                    makerGtd,
                    "execution_source",
                    PairedMakerGtdPaperExecutionContract.ExecutionSource) ||
                !makerGtd.TryGetProperty("contract_version", out var contractVersionElement) ||
                contractVersionElement.ValueKind != JsonValueKind.String ||
                !PairedMakerGtdPaperExecutionContract.IsSupportedContractVersion(
                    contractVersionElement.GetString()) ||
                !HasExactString(makerGtd, "terminal_outcome", "observed") ||
                !makerGtd.TryGetProperty("terminal_reason", out var terminalReason) ||
                terminalReason.ValueKind != JsonValueKind.Null ||
                !makerGtd.TryGetProperty("attempts", out var attemptsElement) ||
                attemptsElement.ValueKind != JsonValueKind.Array)
            {
                rejectionReason = "paired_maker_gtd_continuation_contract_mismatch";
                return false;
            }

            var contractVersion = contractVersionElement.GetString();
            var usesGapRecoveryLifecycle =
                PairedMakerGtdPaperExecutionContract.UsesGapRecoveryLifecycle(contractVersion);
            if (usesGapRecoveryLifecycle !=
                    (HasExactString(
                         makerGtd,
                         "gap_recovery_policy_version",
                         PairedMakerGtdPaperExecutionContract.GapRecoveryLifecyclePolicyVersion) &&
                     HasExactFalse(makerGtd, "observation_gaps_backfilled")) ||
                !usesGapRecoveryLifecycle &&
                (makerGtd.TryGetProperty("gap_recovery_policy_version", out _) ||
                 makerGtd.TryGetProperty("observation_gaps_backfilled", out _)))
            {
                rejectionReason = "paired_maker_gtd_continuation_lifecycle_contract_mismatch";
                return false;
            }

            var attempts = new List<JsonObject>();
            var expectedAttemptNumber = 1;
            foreach (var attemptElement in attemptsElement.EnumerateArray())
            {
                if (attemptElement.ValueKind != JsonValueKind.Object ||
                    !attemptElement.TryGetProperty("attempt_number", out var attemptNumberElement) ||
                    !attemptNumberElement.TryGetInt32(out var attemptNumber) ||
                    attemptNumber != expectedAttemptNumber ||
                    JsonNode.Parse(attemptElement.GetRawText()) is not JsonObject attempt)
                {
                    rejectionReason = "paired_maker_gtd_continuation_attempt_sequence_invalid";
                    return false;
                }

                attempts.Add(attempt);
                expectedAttemptNumber++;
            }

            if (attempts.Count > MaximumPlacementAttempts)
            {
                rejectionReason = "paired_maker_gtd_continuation_attempt_cap_exceeded";
                return false;
            }

            if (makerGtd.TryGetProperty("attempts_completed", out var completedElement) &&
                (!completedElement.TryGetInt32(out var persistedCount) || persistedCount != attempts.Count))
            {
                rejectionReason = "paired_maker_gtd_continuation_attempt_count_mismatch";
                return false;
            }

            if (!TryParseFrozenCommonSize(pair, out var parsedFrozenCommonSize))
            {
                rejectionReason = "paired_maker_gtd_continuation_common_size_invalid";
                return false;
            }

            continuation = new PersistedContinuation(
                IsContinuation: true,
                attempts,
                parsedFrozenCommonSize);
            return true;
        }
        catch (JsonException)
        {
            rejectionReason = "paired_maker_gtd_continuation_json_malformed";
            return false;
        }
    }

    private static bool TryParseFrozenCommonSize(
        JsonElement pair,
        out FrozenCommonSize? frozenCommonSize)
    {
        frozenCommonSize = null;
        if (!pair.TryGetProperty("common_requested_size_shares", out var shares) ||
            !pair.TryGetProperty("common_size_frozen_at_utc", out var frozenAt))
        {
            return false;
        }

        if (shares.ValueKind == JsonValueKind.Null && frozenAt.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (!shares.TryGetDecimal(out var parsedShares) ||
            parsedShares <= 0m ||
            frozenAt.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                frozenAt.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedFrozenAtUtc))
        {
            return false;
        }

        frozenCommonSize = new FrozenCommonSize(parsedShares, parsedFrozenAtUtc);
        return true;
    }

    private static bool HasExactString(JsonElement parent, string propertyName, string expected)
    {
        return parent.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            string.Equals(value.GetString(), expected, StringComparison.Ordinal);
    }

    private static bool HasExactGuid(JsonElement parent, string propertyName, Guid expected)
    {
        return parent.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            Guid.TryParseExact(value.GetString(), "D", out var parsed) &&
            parsed == expected;
    }

    private static bool HasExactFalse(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value) &&
            value.ValueKind is JsonValueKind.False;
    }

    private static bool HasExactTrue(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value) &&
            value.ValueKind is JsonValueKind.True;
    }

    private static bool HasExactStringSet(
        JsonElement parent,
        string propertyName,
        IReadOnlyCollection<string> expected)
    {
        if (!parent.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var actual = value.EnumerateArray().ToArray();
        return actual.Length == expected.Count &&
            actual.All(item => item.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(item.GetString())) &&
            actual.Select(item => item.GetString()!)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(expected);
    }

    private static bool TryGetRequiredTimestamp(
        JsonElement parent,
        string propertyName,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        return parent.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out timestamp);
    }

    private static bool SameTimestamp(DateTimeOffset left, DateTimeOffset right)
    {
        return Math.Abs((left - right).Ticks) <= TimeSpan.TicksPerMicrosecond;
    }

    private static JsonObject BuildBookEvidenceJson(
        DirectBookRead read,
        DateTimeOffset evaluatedAtUtc,
        TimeSpan maxQuoteAge)
    {
        var orderBook = read.OrderBook;
        var freshness = EvaluateDirectBookFreshness(read, evaluatedAtUtc, maxQuoteAge);
        return new JsonObject
        {
            ["fetch_rejection_reason"] = read.RejectionReason,
            ["fetch_error"] = read.Error,
            ["freshness_basis"] = PairedMakerGtdPaperExecutionContract.DirectHttpReceiptFreshnessBasis,
            ["request_started_at_utc"] = FormatTimestamp(read.RequestStartedAtUtc),
            ["response_completed_at_utc"] = FormatTimestamp(read.ResponseCompletedAtUtc),
            ["evaluated_at_utc"] = FormatTimestamp(evaluatedAtUtc),
            ["max_age_ms"] = (long)Math.Ceiling(maxQuoteAge.TotalMilliseconds),
            ["age_ms"] = ToCeilingMilliseconds(freshness.ReceiptAge),
            ["receipt_age_ms"] = ToCeilingMilliseconds(freshness.ReceiptAge),
            ["request_duration_ms"] = ToCeilingMilliseconds(freshness.RequestDuration),
            ["source_age_ms"] = ToCeilingMilliseconds(freshness.SourceAge),
            ["is_current"] = freshness.IsCurrent,
            ["asset_id"] = orderBook?.AssetId,
            ["condition_id"] = orderBook?.ConditionId,
            ["source_timestamp_utc"] = FormatTimestamp(orderBook?.SourceTimestampUtc),
            ["received_at_utc"] = FormatTimestamp(orderBook?.ReceivedAtUtc),
            ["timestamp_quality"] = orderBook?.TimestampQuality.ToString(),
            ["timestamp_is_authoritative"] = orderBook?.HasAuthoritativeSourceTimestamp ?? false,
            ["source_event_id"] = orderBook?.SourceEventId,
            ["best_bid"] = orderBook?.BestBid,
            ["best_ask"] = orderBook?.BestAsk,
            ["spread_abs"] = orderBook?.SpreadAbs,
            ["min_order_size"] = orderBook?.MinOrderSize,
            ["tick_size"] = orderBook?.TickSize,
            ["negative_risk"] = orderBook?.NegativeRisk,
            ["last_trade_price"] = orderBook?.LastTradePrice,
            ["bids"] = orderBook is null
                ? new JsonArray()
                : JsonSerializer.SerializeToNode(orderBook.Bids),
            ["asks"] = orderBook is null
                ? new JsonArray()
                : JsonSerializer.SerializeToNode(orderBook.Asks)
        };
    }

    private static long? ToCeilingMilliseconds(TimeSpan? value)
    {
        return value is { } duration
            ? checked((long)Math.Ceiling(duration.TotalMilliseconds))
            : null;
    }

    private static JsonObject BuildSizingJson(MinimumStakeSizing sizing)
    {
        return new JsonObject
        {
            ["available"] = sizing.Available,
            ["rejection_reason"] = sizing.RejectionReason,
            ["source"] = ClobBookSource,
            ["stake_multiplier"] = sizing.PaperStakeAmount,
            ["minimum_stake_safety_multiplier"] = MinimumStakeSafetyMultiplier,
            ["rounding_mode"] = StakeRoundingMode,
            ["min_order_size"] = sizing.MinOrderSize,
            ["minimum_notional_usd"] = sizing.MinOrderSize * sizing.LimitPrice,
            ["raw_target_notional_usd"] = sizing.RawTargetNotionalUsd,
            ["target_notional_usd"] = sizing.TargetNotionalUsd,
            ["target_size_shares"] = sizing.TargetSizeShares
        };
    }

    private static JsonObject BuildFrozenIntentJson(MakerGtdBuyExecutionIntent intent)
    {
        return new JsonObject
        {
            ["strategy_id"] = intent.StrategyId.ToString("D"),
            ["decision_id"] = intent.DecisionId.ToString("D"),
            ["condition_id"] = intent.ConditionId,
            ["asset_id"] = intent.AssetId,
            ["side"] = intent.Side.ToString(),
            ["post_only"] = intent.PostOnly,
            ["order_type"] = MakerGtdBuyExecutionIntent.TimeInForce,
            ["maximum_order_price"] = intent.MaximumOrderPrice,
            ["limit_price"] = intent.LimitPrice,
            ["requested_notional_usd"] = intent.RequestedNotionalUsd,
            ["requested_size_shares"] = intent.RequestedSizeShares,
            ["target_notional_usd"] = intent.TargetNotionalUsd,
            ["target_size_shares"] = intent.TargetSizeShares,
            ["tick_size"] = intent.TickSize,
            ["min_order_size"] = intent.MinOrderSize,
            ["negative_risk"] = intent.NegativeRisk,
            ["decision_snapshot_at_utc"] = FormatTimestamp(intent.DecisionSnapshotAtUtc),
            ["frozen_at_utc"] = FormatTimestamp(intent.FrozenAtUtc),
            ["effective_expires_at_utc"] = FormatTimestamp(intent.EffectiveExpiresAtUtc),
            ["clob_gtd_expiration_utc"] = FormatTimestamp(intent.ClobGtdExpirationUtc)
        };
    }

    private static MinimumStakeSizing CreateMinimumStakeSizing(
        decimal minOrderSize,
        decimal limitPrice,
        decimal paperStakeAmount)
    {
        if (minOrderSize <= 0m || limitPrice <= 0m || limitPrice >= 1m || paperStakeAmount <= 0m)
        {
            return MinimumStakeSizing.Reject(
                minOrderSize,
                limitPrice,
                paperStakeAmount,
                "paired_maker_gtd_minimum_stake_inputs_invalid");
        }

        var rawTargetNotionalUsd =
            minOrderSize * limitPrice * MinimumStakeSafetyMultiplier * paperStakeAmount;
        var targetNotionalUsd = Math.Ceiling(rawTargetNotionalUsd);
        var targetSizeShares = Math.Ceiling((targetNotionalUsd / limitPrice) * 100m) / 100m;
        return new MinimumStakeSizing(
            true,
            null,
            minOrderSize,
            limitPrice,
            paperStakeAmount,
            rawTargetNotionalUsd,
            targetNotionalUsd,
            targetSizeShares);
    }

    private static string? ValidateS0(
        DirectBookRead read,
        string expectedAssetId,
        string expectedConditionId,
        DateTimeOffset evaluatedAtUtc,
        TimeSpan maxQuoteAge)
    {
        if (read.OrderBook is not { } orderBook)
        {
            return "paired_maker_gtd_s0_missing";
        }

        if (!string.Equals(orderBook.AssetId, expectedAssetId, StringComparison.Ordinal))
        {
            return "paired_maker_gtd_s0_asset_mismatch";
        }

        if (!string.Equals(orderBook.ConditionId, expectedConditionId, StringComparison.Ordinal))
        {
            return "paired_maker_gtd_s0_condition_mismatch";
        }

        if (!orderBook.HasAuthoritativeSourceTimestamp || orderBook.ReceivedAtUtc is null)
        {
            return "paired_maker_gtd_s0_timestamp_not_authoritative";
        }

        if (!IsDirectBookCurrent(read, evaluatedAtUtc, maxQuoteAge, out _))
        {
            return "paired_maker_gtd_s0_book_not_current";
        }

        if (orderBook.BestBid is not > 0m or >= 1m ||
            orderBook.BestAsk is not > 0m or >= 1m ||
            orderBook.IsCrossed)
        {
            return "paired_maker_gtd_s0_book_invalid";
        }

        if (orderBook.TickSize is not (0.1m or 0.01m or 0.001m or 0.0001m))
        {
            return "paired_maker_gtd_s0_tick_size_invalid";
        }

        return orderBook.MinOrderSize is > 0m
            ? null
            : "paired_maker_gtd_s0_min_order_size_missing";
    }

    private static string? ValidateS1(
        DirectBookRead read,
        string expectedAssetId,
        string expectedConditionId,
        DateTimeOffset evaluatedAtUtc,
        TimeSpan maxQuoteAge,
        MakerGtdBuyExecutionIntent intent)
    {
        if (read.OrderBook is not { } orderBook)
        {
            return "paired_maker_gtd_s1_missing";
        }

        if (!string.Equals(orderBook.AssetId, expectedAssetId, StringComparison.Ordinal))
        {
            return "paired_maker_gtd_s1_asset_mismatch";
        }

        if (!string.Equals(orderBook.ConditionId, expectedConditionId, StringComparison.Ordinal))
        {
            return "paired_maker_gtd_s1_condition_mismatch";
        }

        if (read.RequestStartedAtUtc < intent.FrozenAtUtc)
        {
            return "paired_maker_gtd_s1_request_before_intent_freeze";
        }

        if (!orderBook.HasAuthoritativeSourceTimestamp || orderBook.ReceivedAtUtc is null)
        {
            return "paired_maker_gtd_s1_timestamp_not_authoritative";
        }

        if (!IsDirectBookCurrent(read, evaluatedAtUtc, maxQuoteAge, out _))
        {
            return "paired_maker_gtd_s1_book_not_current";
        }

        if (orderBook.BestBid is not > 0m or >= 1m ||
            orderBook.BestAsk is not > 0m or >= 1m ||
            orderBook.IsCrossed)
        {
            return "paired_maker_gtd_s1_book_invalid";
        }

        if (orderBook.TickSize != intent.TickSize)
        {
            return "paired_maker_gtd_s1_tick_size_changed";
        }

        if (orderBook.MinOrderSize != intent.MinOrderSize)
        {
            return "paired_maker_gtd_s1_min_order_size_changed";
        }

        return orderBook.NegativeRisk == intent.NegativeRisk
            ? null
            : "paired_maker_gtd_s1_negative_risk_changed";
    }

    private static bool IsDirectBookCurrent(
        DirectBookRead read,
        DateTimeOffset evaluatedAtUtc,
        TimeSpan maxQuoteAge,
        out DirectBookFreshness freshness)
    {
        freshness = EvaluateDirectBookFreshness(read, evaluatedAtUtc, maxQuoteAge);
        return freshness.IsCurrent;
    }

    private static DirectBookFreshness EvaluateDirectBookFreshness(
        DirectBookRead read,
        DateTimeOffset evaluatedAtUtc,
        TimeSpan maxQuoteAge)
    {
        if (read.OrderBook is not { } orderBook ||
            !orderBook.HasAuthoritativeSourceTimestamp ||
            orderBook.SourceTimestampUtc is not { } sourceTimestampUtc ||
            orderBook.ReceivedAtUtc is not { } receivedAtUtc ||
            read.RequestStartedAtUtc == default ||
            read.ResponseCompletedAtUtc == default ||
            evaluatedAtUtc == default ||
            maxQuoteAge <= TimeSpan.Zero)
        {
            return DirectBookFreshness.Unavailable;
        }

        var requestDuration = read.ResponseCompletedAtUtc - read.RequestStartedAtUtc;
        var receiptAge = evaluatedAtUtc - receivedAtUtc;
        var sourceAge = evaluatedAtUtc - sourceTimestampUtc;
        var timestampsOrdered =
            read.RequestStartedAtUtc <= receivedAtUtc &&
            receivedAtUtc <= read.ResponseCompletedAtUtc &&
            read.ResponseCompletedAtUtc <= evaluatedAtUtc &&
            sourceTimestampUtc <= receivedAtUtc;
        var isCurrent = timestampsOrdered &&
            requestDuration >= TimeSpan.Zero &&
            requestDuration <= maxQuoteAge &&
            receiptAge >= TimeSpan.Zero &&
            receiptAge <= maxQuoteAge &&
            sourceAge >= TimeSpan.Zero;
        return new DirectBookFreshness(
            isCurrent,
            requestDuration,
            receiptAge,
            sourceAge);
    }

    private bool IsMarketDataReady(IReadOnlyCollection<string> assetIds)
    {
        return TryCaptureStableConfirmedMarketData(assetIds, out _, out _);
    }

    private bool TryCaptureAcceptanceMarketData(
        IReadOnlyCollection<string> requiredAssetIds,
        string acceptedAssetId,
        out AcceptedMarketDataSnapshot snapshot)
    {
        snapshot = default!;
        if (!TryCaptureStableConfirmedMarketData(
                requiredAssetIds,
                out var status,
                out var subscriptions) ||
            !subscriptions.TryGetValue(acceptedAssetId, out var acceptedSubscription))
        {
            return false;
        }

        snapshot = new AcceptedMarketDataSnapshot(
            status,
            subscriptions.Count,
            status.ContinuityGeneration,
            acceptedSubscription);
        return true;
    }

    private bool TryCaptureStableConfirmedMarketData(
        IReadOnlyCollection<string> requiredAssetIds,
        out MarketDataStatusSnapshot status,
        out IReadOnlyDictionary<string, ConfirmedAssetSubscriptionSnapshot> subscriptions)
    {
        status = default!;
        subscriptions = new Dictionary<string, ConfirmedAssetSubscriptionSnapshot>(
            StringComparer.Ordinal);
        if (requiredAssetIds.Count != 2 ||
            requiredAssetIds.Any(string.IsNullOrWhiteSpace) ||
            requiredAssetIds.Distinct(StringComparer.Ordinal).Count() != 2)
        {
            return false;
        }

        var required = requiredAssetIds.ToArray();
        var firstSubscriptions = required.ToDictionary(
            assetId => assetId,
            marketDataCache.GetConfirmedAssetSubscription,
            StringComparer.Ordinal);
        var secondStatus = marketDataCache.Status;
        var secondSubscriptions = required.ToDictionary(
            assetId => assetId,
            marketDataCache.GetConfirmedAssetSubscription,
            StringComparer.Ordinal);

        foreach (var assetId in required)
        {
            var first = firstSubscriptions[assetId];
            var second = secondSubscriptions[assetId];
            if (!first.ConfirmedLive ||
                !second.ConfirmedLive ||
                string.IsNullOrWhiteSpace(first.Component) ||
                !string.Equals(first.AssetId, assetId, StringComparison.Ordinal) ||
                !string.Equals(second.AssetId, assetId, StringComparison.Ordinal) ||
                !string.Equals(first.Component, second.Component, StringComparison.Ordinal) ||
                first.Generation != second.Generation ||
                string.IsNullOrWhiteSpace(first.SessionId) ||
                !string.Equals(first.SessionId, second.SessionId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        status = secondStatus;
        subscriptions = secondSubscriptions;
        return true;
    }

    private TimeSpan GetMaximumQuoteAge()
    {
        return TimeSpan.FromMilliseconds(Math.Max(1, strategyOptions.PaperTakerMaxQuoteAgeMilliseconds));
    }

    private static bool IsEnabledForPaperEntry(
        BtcUpDown5mStrategyVariant variant,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> settings,
        DateTimeOffset nowUtc)
    {
        return PairedMakerGtdPaperExecutionContract.IsApprovedStrategyVariant(variant) &&
            settings.TryGetValue(variant.Id, out var runtime) &&
            runtime.Enabled &&
            !runtime.IsPausedAt(nowUtc) &&
            runtime.PaperStakeAmount > 0m;
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> GetExactPair(string assetSymbol)
    {
        return StrategyIds.PairedMakerGtdFirstAcceptingVariants
            .Where(variant => string.Equals(
                variant.ReferenceAssetSymbol,
                assetSymbol,
                StringComparison.Ordinal))
            .OrderBy(variant => variant.FixedOutcome)
            .ToArray();
    }

    private static bool TryResolveMarket(
        PolymarketGammaMarket market,
        out MarketIdentity identity,
        out string rejectionReason)
    {
        identity = default!;
        rejectionReason = string.Empty;
        if (!TryParseExactMarketSlug(market.Slug, out var assetSymbol, out var marketStartUtc))
        {
            rejectionReason = "paired_maker_gtd_market_identity_invalid";
            return false;
        }

        if (market.EndDateUtc is not { } marketEndUtc ||
            marketEndUtc != marketStartUtc.AddMinutes(5) ||
            market.EventStartTimeUtc is { } eventStartTimeUtc && eventStartTimeUtc != marketStartUtc ||
            string.IsNullOrWhiteSpace(market.MarketId) ||
            string.IsNullOrWhiteSpace(market.ConditionId) ||
            market.Outcomes.Count != 2 ||
            !market.Outcomes.Contains("Up", StringComparer.Ordinal) ||
            !market.Outcomes.Contains("Down", StringComparer.Ordinal) ||
            market.ClobTokenIds.Count != 2 ||
            market.ClobTokenIds.Any(tokenId =>
                string.IsNullOrWhiteSpace(tokenId) ||
                tokenId.Trim().Equals("0", StringComparison.Ordinal) ||
                tokenId.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)) ||
            market.ClobTokenIds.Distinct(StringComparer.Ordinal).Count() != 2)
        {
            rejectionReason = "paired_maker_gtd_market_contract_invalid";
            return false;
        }

        identity = new MarketIdentity(assetSymbol, marketStartUtc, marketEndUtc);
        return true;
    }

    private static bool TryParseExactMarketSlug(
        string? slug,
        out string assetSymbol,
        out DateTimeOffset marketStartUtc)
    {
        assetSymbol = string.Empty;
        marketStartUtc = default;
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        foreach (var candidate in new[] { "BTC", "ETH", "SOL" })
        {
            var prefix = candidate.ToLowerInvariant() + "-updown-5m-";
            if (!slug.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var unixText = slug[prefix.Length..];
            if (!long.TryParse(
                    unixText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var unixSeconds) ||
                unixSeconds % 300 != 0)
            {
                return false;
            }

            try
            {
                marketStartUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                assetSymbol = candidate;
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryResolveOutcomeAssetId(
        PolymarketGammaMarket market,
        BtcUpDownFixedOutcome fixedOutcome,
        out string assetId,
        out string outcome)
    {
        assetId = string.Empty;
        outcome = fixedOutcome.ToString();
        for (var index = 0; index < market.Outcomes.Count; index++)
        {
            if (!string.Equals(market.Outcomes[index], outcome, StringComparison.Ordinal))
            {
                continue;
            }

            assetId = market.ClobTokenIds[index];
            outcome = market.Outcomes[index];
            return !string.IsNullOrWhiteSpace(assetId);
        }

        return false;
    }

    private static decimal RoundDownToTick(decimal value, decimal tickSize)
    {
        return value <= 0m || tickSize <= 0m
            ? 0m
            : Math.Floor(value / tickSize) * tickSize;
    }

    private void SetAttemptFailure(JsonObject attempt, string outcome, string reasonCode)
    {
        attempt["outcome"] = outcome;
        attempt["reason_code"] = reasonCode;
        attempt["completed_at_utc"] = FormatTimestamp(clock.GetUtcNow());
    }

    private async Task TryRecordApiErrorAsync(
        string stage,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(
                    Guid.NewGuid(),
                    nameof(PairedMakerGtdFirstAcceptingProcessor),
                    "ReadDirectOrderBook" + stage,
                    message,
                    clock.GetUtcNow()),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist paired Maker-GTD API error. Stage={Stage}", stage);
        }
    }

    private static string BuildPairId(string conditionId, string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() + ":" + conditionId;
    }

    private static string? FormatTimestamp(DateTimeOffset? value)
    {
        return value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static PairedMakerGtdFirstAcceptingResult Add(
        PairedMakerGtdFirstAcceptingResult current,
        int markets,
        int accepted,
        int skipped)
    {
        return new PairedMakerGtdFirstAcceptingResult(
            current.MarketsProcessed + markets,
            current.LegsAccepted + accepted,
            current.LegsSkipped + skipped);
    }

    private sealed record MarketIdentity(
        string AssetSymbol,
        DateTimeOffset MarketStartUtc,
        DateTimeOffset MarketEndUtc);

    private sealed record FrozenCommonSize(
        decimal Shares,
        DateTimeOffset? FrozenAtUtc);

    private sealed record PersistedContinuation(
        bool IsContinuation,
        IReadOnlyList<JsonObject> Attempts,
        FrozenCommonSize? FrozenCommonSize)
    {
        public static PersistedContinuation Initial { get; } = new(
            IsContinuation: false,
            Attempts: [],
            FrozenCommonSize: null);
    }

    private sealed record DirectBookRead(
        OrderBookSnapshot? OrderBook,
        string? RejectionReason,
        string? Error,
        DateTimeOffset RequestStartedAtUtc,
        DateTimeOffset ResponseCompletedAtUtc)
    {
        public static DirectBookRead Found(
            OrderBookSnapshot orderBook,
            DateTimeOffset requestStartedAtUtc,
            DateTimeOffset responseCompletedAtUtc) =>
            new(orderBook, null, null, requestStartedAtUtc, responseCompletedAtUtc);

        public static DirectBookRead Reject(
            string reason,
            DateTimeOffset requestStartedAtUtc,
            DateTimeOffset responseCompletedAtUtc,
            string? error = null) =>
            new(null, reason, error, requestStartedAtUtc, responseCompletedAtUtc);
    }

    private readonly record struct DirectBookFreshness(
        bool IsCurrent,
        TimeSpan? RequestDuration,
        TimeSpan? ReceiptAge,
        TimeSpan? SourceAge)
    {
        public static DirectBookFreshness Unavailable { get; } = new(
            IsCurrent: false,
            RequestDuration: null,
            ReceiptAge: null,
            SourceAge: null);
    }

    private sealed record MinimumStakeSizing(
        bool Available,
        string? RejectionReason,
        decimal MinOrderSize,
        decimal LimitPrice,
        decimal PaperStakeAmount,
        decimal RawTargetNotionalUsd,
        decimal TargetNotionalUsd,
        decimal TargetSizeShares)
    {
        public static MinimumStakeSizing Reject(
            decimal minOrderSize,
            decimal limitPrice,
            decimal paperStakeAmount,
            string reason)
        {
            return new MinimumStakeSizing(
                false,
                reason,
                minOrderSize,
                limitPrice,
                paperStakeAmount,
                0m,
                0m,
                0m);
        }
    }

    private sealed record LegAttemptContext(
        JsonObject Attempt,
        DirectBookRead S0Read,
        decimal LimitPrice,
        MinimumStakeSizing Sizing)
    {
        public OrderBookSnapshot S0 => S0Read.OrderBook!;
    }

    private sealed record AcceptedLeg(
        Guid PaperOrderId,
        MakerGtdBuyExecutionIntent Intent,
        DateTimeOffset AcceptedAtUtc,
        MarketDataStatusSnapshot Status,
        int ConfirmedAssetsCount,
        long ContinuityGeneration,
        ConfirmedAssetSubscriptionSnapshot AcceptedAssetSubscription);

    private sealed record AcceptedMarketDataSnapshot(
        MarketDataStatusSnapshot Status,
        int ConfirmedAssetsCount,
        long ContinuityGeneration,
        ConfirmedAssetSubscriptionSnapshot AcceptedAssetSubscription);

    private sealed class LegState(
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        StrategyRuntimeSettings settings,
        string assetId,
        string outcome)
    {
        public StrategyMarketPaperRun Run { get; private set; } = run;

        public BtcUpDown5mStrategyVariant Variant { get; } = variant;

        public StrategyRuntimeSettings Settings { get; } = settings;

        public string AssetId { get; } = assetId;

        public string Outcome { get; } = outcome;

        public JsonArray Attempts { get; } = [];

        public AcceptedLeg? Accepted { get; set; }

        public void ReplaceRun(StrategyMarketPaperRun replacement)
        {
            Run = replacement;
        }
    }
}
