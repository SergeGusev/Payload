using System.Diagnostics;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainPaperSignalProcessor(
    ILogger<OnChainPaperSignalProcessor> logger,
    BotOptions botOptions,
    OnChainIngestionOptions onChainOptions,
    DataApiTraderIngestionOptions dataApiOptions,
    ExecutionOptions executionOptions,
    SignalOptions signalOptions,
    PaperTradingOptions paperTradingOptions,
    MarketDataWebSocketOptions marketDataWebSocketOptions,
    IPolymarketClobPublicClient clobClient,
    IMarketDataCache marketDataCache,
    IExposureSnapshotCache exposureCache,
    ISignalEngine signalEngine,
    IPaperTradingEngine paperTradingEngine,
    IStrategyStateProvider strategyStateProvider,
    IAppRepository repository) : IOnChainPaperSignalProcessor
{
    private const string StatusPaperOrderCreated = "PaperOrderCreated";
    private const string StatusAcceptedNoPaper = "AcceptedNoPaper";
    private const string StatusRejected = "Rejected";
    private const string StatusIgnored = "Ignored";
    private const string StatusError = "Error";
    private const string ErrorDecisionCode = "onchain_paper_signal_error";
    private const string OnChainSellIgnoredDecisionCode = "onchain_sell_ignored";

    private readonly SemaphoreSlim singleRun = new(1, 1);

    public async Task<OnChainPaperSignalProcessingResult> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!await singleRun.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new InvalidOperationException("On-chain paper signal processing is already running.");
        }

        try
        {
            return await ProcessOnceCoreAsync(cancellationToken);
        }
        finally
        {
            singleRun.Release();
        }
    }

    public async Task<OnChainPaperSignalProcessingResult> ProcessCapturesAsync(
        IReadOnlyList<PolymarketOnChainTradeCapture> captures,
        CancellationToken cancellationToken = default)
    {
        if (!await singleRun.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            logger.LogInformation("On-chain hot paper signal processing skipped because another run is active.");
            return new OnChainPaperSignalProcessingResult(0, 0, 0, 0, 0, 0);
        }

        try
        {
            return await ProcessCapturesCoreAsync(captures, cancellationToken);
        }
        finally
        {
            singleRun.Release();
        }
    }

    private async Task<OnChainPaperSignalProcessingResult> ProcessOnceCoreAsync(CancellationToken cancellationToken)
    {
        if (!onChainOptions.PaperSignalEnabled || !onChainOptions.PaperSignalBacklogEnabled)
        {
            return new OnChainPaperSignalProcessingResult(0, 0, 0, 0, 0, 0);
        }

        if (!await strategyStateProvider.IsStrategyEnabledAsync(StrategyIds.FollowLeader, cancellationToken))
        {
            return new OnChainPaperSignalProcessingResult(0, 0, 0, 0, 0, 0);
        }

        var candidates = await repository.GetPendingOnChainPaperSignalCandidatesAsync(
            dataApiOptions.PolymarketRatingTimePeriod,
            dataApiOptions.PolymarketRatingOrderBy,
            onChainOptions.PaperSignalBatchSize,
            cancellationToken);
        if (candidates.Count == 0)
        {
            return new OnChainPaperSignalProcessingResult(0, 0, 0, 0, 0, 0);
        }

        return await ProcessCandidatesAsync(candidates, hotPathOrderBook: false, cancellationToken);
    }

    private async Task<OnChainPaperSignalProcessingResult> ProcessCapturesCoreAsync(
        IReadOnlyList<PolymarketOnChainTradeCapture> captures,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        if (!onChainOptions.PaperSignalEnabled ||
            !onChainOptions.PaperSignalHotPathEnabled ||
            captures.Count == 0)
        {
            return new OnChainPaperSignalProcessingResult(0, 0, 0, 0, 0, 0);
        }

        if (!await strategyStateProvider.IsStrategyEnabledAsync(StrategyIds.FollowLeader, cancellationToken))
        {
            return new OnChainPaperSignalProcessingResult(0, 0, 0, 0, 0, 0);
        }

        var now = DateTimeOffset.UtcNow;
        var maxAge = TimeSpan.FromSeconds(onChainOptions.PaperSignalHotMaxAgeSeconds);
        var filterStopwatch = Stopwatch.StartNew();
        var latestCandidatesLimit = Math.Max(1, onChainOptions.PaperSignalLatestCandidatesLimit);
        var freshCaptures = captures
            .Where(capture => !capture.Removed)
            .Where(capture => now - capture.BlockTimestampUtc <= maxAge)
            .OrderByDescending(capture => capture.BlockTimestampUtc)
            .ThenByDescending(capture => capture.BlockNumber)
            .ThenByDescending(capture => capture.LogIndex)
            .Take(latestCandidatesLimit)
            .OrderBy(capture => capture.BlockTimestampUtc)
            .ThenBy(capture => capture.BlockNumber)
            .ThenBy(capture => capture.LogIndex)
            .ToArray();
        filterStopwatch.Stop();
        if (freshCaptures.Length == 0)
        {
            return new OnChainPaperSignalProcessingResult(0, 0, 0, 0, 0, 0);
        }

        var candidateLookupStopwatch = Stopwatch.StartNew();
        var candidates = await repository.GetOnChainPaperSignalCandidatesForCapturesAsync(
            freshCaptures,
            dataApiOptions.PolymarketRatingTimePeriod,
            dataApiOptions.PolymarketRatingOrderBy,
            cancellationToken);
        candidateLookupStopwatch.Stop();
        if (candidates.Count == 0)
        {
            logger.LogInformation(
                "On-chain hot paper signal selection found no mapped candidates. InputCaptures={InputCaptures} FreshCaptureWindow={FreshCaptureWindow} FilterMs={FilterMs} CandidateLookupMs={CandidateLookupMs} TotalMs={TotalMs}",
                captures.Count,
                freshCaptures.Length,
                filterStopwatch.ElapsedMilliseconds,
                candidateLookupStopwatch.ElapsedMilliseconds,
                totalStopwatch.ElapsedMilliseconds);
            return new OnChainPaperSignalProcessingResult(0, 0, 0, 0, 0, 0);
        }

        var selectionStopwatch = Stopwatch.StartNew();
        var selections = SelectHotCandidates(
            candidates,
            now,
            latestCandidatesLimit,
            out var buyCandidates,
            out var candidatesRejectedBeforeScoring,
            out var nonBuyCandidates);
        selectionStopwatch.Stop();
        if (selections.Count == 0)
        {
            totalStopwatch.Stop();
            logger.LogInformation(
                "On-chain hot paper signal selection skipped batch. InputCaptures={InputCaptures} FreshCaptureWindow={FreshCaptureWindow} Candidates={Candidates} BuyCandidates={BuyCandidates} NonBuyCandidates={NonBuyCandidates} PrecheckRejected={PrecheckRejected} FilterMs={FilterMs} CandidateLookupMs={CandidateLookupMs} SelectionMs={SelectionMs} TotalMs={TotalMs}",
                captures.Count,
                freshCaptures.Length,
                candidates.Count,
                buyCandidates,
                nonBuyCandidates,
                candidatesRejectedBeforeScoring,
                filterStopwatch.ElapsedMilliseconds,
                candidateLookupStopwatch.ElapsedMilliseconds,
                selectionStopwatch.ElapsedMilliseconds,
                totalStopwatch.ElapsedMilliseconds);
            return new OnChainPaperSignalProcessingResult(candidates.Count, 0, 0, 0, 0, 0);
        }

        var processingStopwatch = Stopwatch.StartNew();
        var signalsCreated = 0;
        var accepted = 0;
        var rejected = 0;
        var paperOrdersCreated = 0;
        var errors = 0;
        var attempted = 0;
        HotCandidateSelection? lastSelection = null;
        CandidateOutcome? lastOutcome = null;
        foreach (var selection in selections)
        {
            lastSelection = selection;
            attempted++;
            CandidateOutcome outcome;
            try
            {
                outcome = await ProcessCandidateAsync(selection.Candidate, hotPathOrderBook: true, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                logger.LogError(
                    ex,
                    "On-chain paper signal processing failed. Tx={TransactionHash} LogIndex={LogIndex} Role={ParticipantRole}",
                    selection.Candidate.TransactionHash,
                    selection.Candidate.LogIndex,
                    selection.Candidate.ParticipantRole);
                await TryRecordApiErrorAsync("ProcessCandidate", ex.Message, cancellationToken);
                await TryRecordProcessingResultAsync(
                    ToProcessingResult(selection.Candidate, StatusError, ErrorDecisionCode, ex.Message, null, null, DateTimeOffset.UtcNow),
                    cancellationToken);
                break;
            }

            lastOutcome = outcome;
            signalsCreated += outcome.SignalCreated ? 1 : 0;
            accepted += outcome.SignalAccepted ? 1 : 0;
            rejected += outcome.SignalRejected ? 1 : 0;
            paperOrdersCreated += outcome.PaperOrderCreated ? 1 : 0;
            if (outcome.PaperOrderCreated || !IsMissingOrderBookDecision(outcome.DecisionCode))
            {
                break;
            }
        }

        processingStopwatch.Stop();
        totalStopwatch.Stop();
        var result = new OnChainPaperSignalProcessingResult(
            candidates.Count,
            signalsCreated,
            accepted,
            rejected,
            paperOrdersCreated,
            errors);
        logger.LogInformation(
            "On-chain hot paper signal selection completed. InputCaptures={InputCaptures} FreshCaptureWindow={FreshCaptureWindow} Candidates={Candidates} BuyCandidates={BuyCandidates} NonBuyCandidates={NonBuyCandidates} PrecheckRejected={PrecheckRejected} EligibleSelections={EligibleSelections} AttemptedSelections={AttemptedSelections} LastScore={LastScore} LastDecisionCode={LastDecisionCode} LastWallet={LastWallet} LastTokenId={LastTokenId} LastTx={LastTx} LastBlock={LastBlock} PaperOrders={PaperOrders} FilterMs={FilterMs} CandidateLookupMs={CandidateLookupMs} SelectionMs={SelectionMs} ProcessingMs={ProcessingMs} TotalMs={TotalMs}",
            captures.Count,
            freshCaptures.Length,
            candidates.Count,
            buyCandidates,
            nonBuyCandidates,
            candidatesRejectedBeforeScoring,
            selections.Count,
            attempted,
            lastSelection?.Score,
            lastOutcome?.DecisionCode ?? string.Empty,
            lastSelection?.Candidate.Wallet,
            lastSelection?.Candidate.TokenId,
            lastSelection?.Candidate.TransactionHash,
            lastSelection?.Candidate.BlockNumber,
            result.PaperOrdersCreated,
            filterStopwatch.ElapsedMilliseconds,
            candidateLookupStopwatch.ElapsedMilliseconds,
            selectionStopwatch.ElapsedMilliseconds,
            processingStopwatch.ElapsedMilliseconds,
            totalStopwatch.ElapsedMilliseconds);

        return result;
    }

    private async Task<OnChainPaperSignalProcessingResult> ProcessCandidatesAsync(
        IReadOnlyList<OnChainPaperSignalCandidate> candidates,
        bool hotPathOrderBook,
        CancellationToken cancellationToken)
    {
        var signalsCreated = 0;
        var accepted = 0;
        var rejected = 0;
        var paperOrdersCreated = 0;
        var errors = 0;

        foreach (var candidate in candidates)
        {
            try
            {
                var outcome = await ProcessCandidateAsync(candidate, hotPathOrderBook, cancellationToken);
                signalsCreated += outcome.SignalCreated ? 1 : 0;
                accepted += outcome.SignalAccepted ? 1 : 0;
                rejected += outcome.SignalRejected ? 1 : 0;
                paperOrdersCreated += outcome.PaperOrderCreated ? 1 : 0;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                logger.LogError(
                    ex,
                    "On-chain paper signal processing failed. Tx={TransactionHash} LogIndex={LogIndex} Role={ParticipantRole}",
                    candidate.TransactionHash,
                    candidate.LogIndex,
                    candidate.ParticipantRole);
                await TryRecordApiErrorAsync("ProcessCandidate", ex.Message, cancellationToken);
                await TryRecordProcessingResultAsync(
                    ToProcessingResult(candidate, StatusError, ErrorDecisionCode, ex.Message, null, null, DateTimeOffset.UtcNow),
                    cancellationToken);
            }
        }

        return new OnChainPaperSignalProcessingResult(
            candidates.Count,
            signalsCreated,
            accepted,
            rejected,
            paperOrdersCreated,
            errors);
    }

    private async Task<CandidateOutcome> ProcessCandidateAsync(
        OnChainPaperSignalCandidate candidate,
        bool hotPathOrderBook,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var orderBookMs = 0L;
        var exposureMs = 0L;
        var evaluationMs = 0L;
        var persistenceMs = 0L;
        var now = DateTimeOffset.UtcNow;
        var leaderTrade = ToLeaderTrade(candidate);
        if (candidate.Side == TradeSide.Sell)
        {
            var persistStopwatch = Stopwatch.StartNew();
            await repository.AddOnChainPaperSignalResultAsync(
                ToProcessingResult(
                    candidate,
                    StatusIgnored,
                    OnChainSellIgnoredDecisionCode,
                    "On-chain SELL notifications are ignored; copied exits are tracked from leader Data API activity.",
                    null,
                    null,
                    now),
                cancellationToken);
            persistStopwatch.Stop();
            persistenceMs = persistStopwatch.ElapsedMilliseconds;
            LogCandidateTiming(
                candidate,
                StatusIgnored,
                OnChainSellIgnoredDecisionCode,
                hotPathOrderBook,
                totalStopwatch,
                orderBookMs,
                exposureMs,
                evaluationMs,
                persistenceMs);
            return new CandidateOutcome(false, false, false, false, OnChainSellIgnoredDecisionCode);
        }

        var precheckReasons = ValidatePreconditions(candidate, now);
        if (precheckReasons.Count > 0)
        {
            var rejectedSignal = ToRejectedSignal(leaderTrade, precheckReasons, now);
            var persistStopwatch = Stopwatch.StartNew();
            await repository.AddSignalAsync(rejectedSignal, cancellationToken);
            await PersistSignalRejectionsAsync(rejectedSignal.Id, precheckReasons, now, cancellationToken);
            await repository.AddOnChainPaperSignalResultAsync(
                ToProcessingResult(
                    candidate,
                    StatusRejected,
                    precheckReasons[0].ReasonCode,
                    JoinReasonDetails(precheckReasons),
                    rejectedSignal.Id,
                    null,
                    now),
                cancellationToken);
            persistStopwatch.Stop();
            persistenceMs = persistStopwatch.ElapsedMilliseconds;
            LogCandidateTiming(
                candidate,
                StatusRejected,
                precheckReasons[0].ReasonCode,
                hotPathOrderBook,
                totalStopwatch,
                orderBookMs,
                exposureMs,
                evaluationMs,
                persistenceMs);
            return new CandidateOutcome(true, false, true, false, precheckReasons[0].ReasonCode);
        }

        var orderBookTask = TimedAsync(() => GetOrderBookAsync(candidate.TokenId, hotPathOrderBook, cancellationToken));
        var exposureTask = TimedAsync(() => exposureCache.GetSnapshotAsync(cancellationToken));
        await Task.WhenAll(orderBookTask, exposureTask);

        var orderBookResult = await orderBookTask;
        var exposureResult = await exposureTask;
        var orderBookLookup = orderBookResult.Value;
        var orderBook = orderBookLookup.OrderBook;
        var exposureSnapshot = exposureResult.Value;
        orderBookMs = orderBookResult.ElapsedMilliseconds;
        exposureMs = exposureResult.ElapsedMilliseconds;

        if (orderBookLookup.RejectionReason is { } orderBookRejection)
        {
            var rejectedSignal = ToRejectedSignal(leaderTrade, [orderBookRejection], now);
            var rejectedPersistenceStopwatch = Stopwatch.StartNew();
            await repository.AddSignalAsync(rejectedSignal, cancellationToken);
            await PersistSignalRejectionsAsync(rejectedSignal.Id, [orderBookRejection], now, cancellationToken);
            await repository.AddOnChainPaperSignalResultAsync(
                ToProcessingResult(
                    candidate,
                    StatusRejected,
                    orderBookRejection.ReasonCode,
                    orderBookRejection.ReasonDetails,
                    rejectedSignal.Id,
                    null,
                    now),
                cancellationToken);
            rejectedPersistenceStopwatch.Stop();
            persistenceMs = rejectedPersistenceStopwatch.ElapsedMilliseconds;
            LogCandidateTiming(
                candidate,
                StatusRejected,
                orderBookRejection.ReasonCode,
                hotPathOrderBook,
                totalStopwatch,
                orderBookMs,
                exposureMs,
                evaluationMs,
                persistenceMs);
            return new CandidateOutcome(true, false, true, false, orderBookRejection.ReasonCode);
        }

        var availablePositionSize = FindCopiedPosition(exposureSnapshot.PaperPositions, leaderTrade)?.SizeShares;
        var copiedTraderOverallPerformance = await ResolveCopiedTraderPerformanceAsync(candidate.Wallet, "OVERALL", cancellationToken);
        var copiedTraderCategoryPerformance = await ResolveCopiedTraderPerformanceAsync(candidate.Wallet, candidate.LocalCategory, cancellationToken);
        var evaluationStopwatch = Stopwatch.StartNew();
        var decision = signalEngine.Evaluate(
            new SignalEvaluationContext(
                leaderTrade,
                ToTraderRule(candidate),
                ToMarketInfo(candidate),
                orderBook,
                BuildExposure(
                    leaderTrade,
                    exposureSnapshot.OpenPaperOrders,
                    exposureSnapshot.PaperPositions,
                    exposureSnapshot.OpenLiveOrders),
                ToSyntheticPerformance(candidate, now),
                copiedTraderOverallPerformance,
                copiedTraderCategoryPerformance,
                availablePositionSize));
        evaluationStopwatch.Stop();
        evaluationMs = evaluationStopwatch.ElapsedMilliseconds;

        var signal = ToSignal(leaderTrade, decision);

        if (!decision.Accepted)
        {
            var rejectedPersistenceStopwatch = Stopwatch.StartNew();
            await repository.AddSignalAsync(signal, cancellationToken);
            var signalReasons = decision.Reasons
                .Select(reason => new RejectionReason(reason, reason))
                .ToArray();
            await PersistSignalRejectionsAsync(signal.Id, signalReasons, decision.CreatedAtUtc, cancellationToken);
            await repository.AddOnChainPaperSignalResultAsync(
                ToProcessingResult(
                    candidate,
                    StatusRejected,
                    decision.DecisionCode,
                    string.Join("; ", decision.Reasons),
                    signal.Id,
                    null,
                    decision.CreatedAtUtc),
                cancellationToken);
            rejectedPersistenceStopwatch.Stop();
            persistenceMs = rejectedPersistenceStopwatch.ElapsedMilliseconds;
            LogCandidateTiming(
                candidate,
                StatusRejected,
                decision.DecisionCode,
                hotPathOrderBook,
                totalStopwatch,
                orderBookMs,
                exposureMs,
                evaluationMs,
                persistenceMs);
            return new CandidateOutcome(true, false, true, false, decision.DecisionCode);
        }

        var paperTradingEnabled = RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions);
        if (!paperTradingEnabled ||
            decision.ProposedPrice is not { } price ||
            decision.ProposedSizeShares is not { } sizeShares)
        {
            var reason = paperTradingEnabled
                ? "Accepted signal did not include paper order price/size."
                : RuntimeModePolicy.PaperTradingDisabledReason(botOptions, paperTradingOptions);
            var noPaperPersistenceStopwatch = Stopwatch.StartNew();
            await repository.AddSignalAsync(signal, cancellationToken);
            await repository.AddOnChainPaperSignalResultAsync(
                ToProcessingResult(
                    candidate,
                    StatusAcceptedNoPaper,
                    paperTradingEnabled ? decision.DecisionCode : SignalReasonCodes.BotModeNotPaper,
                    reason,
                    signal.Id,
                    null,
                    decision.CreatedAtUtc),
                cancellationToken);
            noPaperPersistenceStopwatch.Stop();
            persistenceMs = noPaperPersistenceStopwatch.ElapsedMilliseconds;
            LogCandidateTiming(
                candidate,
                StatusAcceptedNoPaper,
                paperTradingEnabled ? decision.DecisionCode : SignalReasonCodes.BotModeNotPaper,
                hotPathOrderBook,
                totalStopwatch,
                orderBookMs,
                exposureMs,
                evaluationMs,
                persistenceMs);
            return new CandidateOutcome(true, true, false, false, paperTradingEnabled ? decision.DecisionCode : SignalReasonCodes.BotModeNotPaper);
        }

        var order = paperTradingEngine.CreateOrder(
            signal,
            price,
            sizeShares,
            decision.CreatedAtUtc.AddSeconds(paperTradingOptions.DefaultOrderTtlSeconds));
        var copiedLeaderPosition = CreateCopiedLeaderEntry(candidate, signal, order);
        var acceptedPersistenceStopwatch = Stopwatch.StartNew();
        await repository.AddAcceptedOnChainPaperOrderAsync(
            signal,
            order,
            copiedLeaderPosition,
            ToProcessingResult(
                candidate,
                StatusPaperOrderCreated,
                decision.DecisionCode,
                string.Empty,
                signal.Id,
                order.Id,
                decision.CreatedAtUtc),
            cancellationToken);
        exposureCache.ApplyPaperOrder(order);
        acceptedPersistenceStopwatch.Stop();
        persistenceMs = acceptedPersistenceStopwatch.ElapsedMilliseconds;
        LogCandidateTiming(
            candidate,
            StatusPaperOrderCreated,
            decision.DecisionCode,
            hotPathOrderBook,
            totalStopwatch,
            orderBookMs,
            exposureMs,
            evaluationMs,
            persistenceMs);

        return new CandidateOutcome(true, true, false, true, decision.DecisionCode);
    }

    private IReadOnlyList<HotCandidateSelection> SelectHotCandidates(
        IReadOnlyList<OnChainPaperSignalCandidate> candidates,
        DateTimeOffset now,
        int latestCandidatesLimit,
        out int buyCandidates,
        out int candidatesRejectedBeforeScoring,
        out int nonBuyCandidates)
    {
        var latestBuyCandidates = candidates
            .Where(candidate => candidate.Side == TradeSide.Buy)
            .OrderByDescending(candidate => candidate.BlockTimestampUtc)
            .ThenByDescending(candidate => candidate.BlockNumber)
            .ThenByDescending(candidate => candidate.LogIndex)
            .ThenBy(candidate => candidate.ParticipantRole.ToString(), StringComparer.Ordinal)
            .Take(latestCandidatesLimit)
            .ToArray();
        buyCandidates = latestBuyCandidates.Length;
        nonBuyCandidates = candidates.Count - candidates.Count(candidate => candidate.Side == TradeSide.Buy);
        candidatesRejectedBeforeScoring = 0;

        var selections = new List<HotCandidateSelection>(latestBuyCandidates.Length);
        foreach (var candidate in latestBuyCandidates)
        {
            var precheckReasons = ValidatePreconditions(candidate, now);
            if (precheckReasons.Count > 0)
            {
                candidatesRejectedBeforeScoring++;
                continue;
            }

            selections.Add(new HotCandidateSelection(
                candidate,
                CalculateHotCandidateScore(candidate, now)));
        }

        return selections
            .OrderByDescending(selection => selection.Score)
            .ThenByDescending(selection => selection.Candidate.LeaderboardPnlToVolumePct ?? decimal.MinValue)
            .ThenByDescending(selection => selection.Candidate.LeaderboardPnlUsd ?? decimal.MinValue)
            .ThenByDescending(selection => selection.Candidate.NotionalUsd)
            .ThenByDescending(selection => selection.Candidate.BlockTimestampUtc)
            .ThenByDescending(selection => selection.Candidate.BlockNumber)
            .ThenByDescending(selection => selection.Candidate.LogIndex)
            .ToArray();
    }

    private int CalculateHotCandidateScore(OnChainPaperSignalCandidate candidate, DateTimeOffset now)
    {
        var score = 0;
        if (!IsUnknownCategory(candidate.LocalCategory))
        {
            score += signalOptions.CategoryAllowedScore;
        }

        var age = now - candidate.BlockTimestampUtc;
        if (age < TimeSpan.FromSeconds(10))
        {
            score += signalOptions.AgeUnder10SecondsScore;
        }
        else if (age < TimeSpan.FromSeconds(60))
        {
            score += signalOptions.AgeUnder60SecondsScore;
        }
        else if (age < TimeSpan.FromMinutes(5))
        {
            score += signalOptions.AgeUnder5MinutesScore;
        }

        var largeTradeThreshold = executionOptions.MinLeaderTradeUsd * signalOptions.LargeLeaderTradeMultiplier;
        if (candidate.NotionalUsd >= largeTradeThreshold)
        {
            score += signalOptions.LargeLeaderTradeScore;
        }

        if (candidate.MarketEndDateUtc is { } endDate && endDate > now.AddDays(1))
        {
            score += signalOptions.SlowMarketScore;
        }

        score += signalOptions.LeaderCategoryPerformanceScore;
        return score;
    }

    private static PaperCopiedLeaderPosition? CreateCopiedLeaderEntry(
        OnChainPaperSignalCandidate candidate,
        Signal signal,
        PaperOrder order)
    {
        if (order.Side != TradeSide.Buy)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        return new PaperCopiedLeaderPosition(
            Guid.NewGuid(),
            signal.Id,
            order.Id,
            candidate.Wallet,
            candidate.TokenId,
            candidate.ConditionId,
            candidate.Outcome,
            candidate.TransactionHash,
            candidate.BlockTimestampUtc,
            candidate.Price,
            candidate.SizeShares,
            CopiedInitialSizeShares: 0m,
            LeaderSoldSizeShares: 0m,
            CopiedExitRequestedSizeShares: 0m,
            PaperCopiedLeaderPositionStatus.PendingEntry,
            LastActivityTimestampUtc: null,
            LastActivityTransactionHash: null,
            LastActivitySyncAtUtc: null,
            NextActivitySyncAtUtc: now,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);
    }

    private IReadOnlyList<RejectionReason> ValidatePreconditions(
        OnChainPaperSignalCandidate candidate,
        DateTimeOffset now)
    {
        var reasons = new List<RejectionReason>();

        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            reasons.Add(new RejectionReason(
                SignalReasonCodes.BotModeNotPaper,
                RuntimeModePolicy.PaperTradingDisabledReason(botOptions, paperTradingOptions)));
        }

        if (now - candidate.BlockTimestampUtc > TimeSpan.FromSeconds(onChainOptions.PaperSignalMaxLagSeconds))
        {
            reasons.Add(new RejectionReason(
                SignalReasonCodes.TradeTooOld,
                $"On-chain trade is older than configured maximum lag {onChainOptions.PaperSignalMaxLagSeconds} seconds."));
            return reasons;
        }

        if (!candidate.MarketFound)
        {
            reasons.Add(new RejectionReason(
                SignalReasonCodes.MissingMarketMetadata,
                "Gamma market metadata is missing for the on-chain token."));
            return reasons;
        }

        if (IsUnknownCategory(candidate.LocalCategory))
        {
            reasons.Add(new RejectionReason(
                SignalReasonCodes.MissingMarketCategory,
                "Gamma market category is missing or unknown."));
        }

        if (!candidate.MarketActive || candidate.MarketClosed || candidate.MarketArchived)
        {
            reasons.Add(new RejectionReason(
                SignalReasonCodes.MarketInactive,
                "Gamma market is inactive, closed, or archived."));
        }

        if (!candidate.MarketAcceptingOrders || !candidate.MarketEnableOrderBook)
        {
            reasons.Add(new RejectionReason(
                SignalReasonCodes.MarketNotAcceptingOrders,
                "Gamma market is not accepting orders or has no enabled order book."));
        }

        if (string.IsNullOrWhiteSpace(candidate.PolymarketCategory) || candidate.RatingFound is null)
        {
            reasons.Add(new RejectionReason(
                SignalReasonCodes.MissingPolymarketRating,
                "No Polymarket category rating row exists for this wallet/category."));
            return reasons;
        }

        if (onChainOptions.PaperSignalRequirePolymarketRatingFound && candidate.RatingFound != true)
        {
            reasons.Add(new RejectionReason(
                SignalReasonCodes.PolymarketRatingNotFound,
                "Polymarket leaderboard did not return this wallet for the mapped category."));
        }

        if (candidate.RatingRefreshedAtUtc is null ||
            now - candidate.RatingRefreshedAtUtc.Value > TimeSpan.FromHours(onChainOptions.PaperSignalRatingStaleAfterHours))
        {
            reasons.Add(new RejectionReason(
                SignalReasonCodes.PolymarketRatingStale,
                "Polymarket wallet/category rating is stale or has no refresh timestamp."));
        }

        if (candidate.LeaderboardPnlUsd is null ||
            candidate.LeaderboardPnlUsd.Value < onChainOptions.PaperSignalMinLeaderboardPnlUsd)
        {
            reasons.Add(new RejectionReason(
                SignalReasonCodes.PolymarketRatingPnlTooLow,
                $"Leaderboard PnL is below configured minimum {onChainOptions.PaperSignalMinLeaderboardPnlUsd:0.########}."));
        }

        if (candidate.LeaderboardPnlToVolumePct is null ||
            candidate.LeaderboardPnlToVolumePct.Value < onChainOptions.PaperSignalMinLeaderboardPnlToVolumePct)
        {
            reasons.Add(new RejectionReason(
                SignalReasonCodes.PolymarketRatingEfficiencyTooLow,
                $"Leaderboard PnL-to-volume ratio is below configured minimum {onChainOptions.PaperSignalMinLeaderboardPnlToVolumePct:0.########}%."));
        }

        return reasons;
    }

    private async Task<OrderBookLookupResult> GetOrderBookAsync(
        string assetId,
        bool hotPath,
        CancellationToken cancellationToken)
    {
        var maxAge = TimeSpan.FromSeconds(marketDataWebSocketOptions.StaleAfterSeconds);
        var cacheLookup = marketDataCache.GetOrderBook(assetId, maxAge);
        if (cacheLookup is { Status: OrderBookCacheLookupStatus.Fresh, Snapshot: { } cachedOrderBook })
        {
            return BuildOrderBookLookupResult(
                cachedOrderBook,
                SignalReasonCodes.MissingOrderBookEmptySide,
                "Fresh cached order book is missing a usable best bid or best ask.");
        }

        var fallbackReason = BuildCacheFallbackReason(assetId, cacheLookup, maxAge);

        try
        {
            var orderBook = await clobClient.GetOrderBookAsync(assetId, cancellationToken);
            if (orderBook is not null)
            {
                var normalizedOrderBook = NormalizeOrderBook(assetId, orderBook);
                marketDataCache.ApplyUpdate(ToOrderBookMarketDataUpdate(normalizedOrderBook));
                if (hotPath)
                {
                    logger.LogDebug(
                        "Fresh CLOB /book order book fetched for hot on-chain paper path. TokenId={TokenId} PreviousCacheDetails={PreviousCacheDetails}",
                        assetId,
                        fallbackReason.ReasonDetails);
                }

                return BuildOrderBookLookupResult(
                    normalizedOrderBook,
                    SignalReasonCodes.MissingOrderBookEmptySide,
                    "CLOB /book response did not contain a usable best bid or best ask.");
            }

            return BuildOrderBookLookupResult(
                null,
                SignalReasonCodes.MissingOrderBookRestMissing,
                "CLOB /book response did not contain an order book.");
        }
        catch (PolymarketApiException ex) when (IsMissingOrderBook(ex))
        {
            logger.LogInformation(
                "CLOB order book is unavailable for token. TokenId={TokenId} Message={Message}",
                assetId,
                ex.Message);
            return new OrderBookLookupResult(
                null,
                new RejectionReason(
                    SignalReasonCodes.MissingOrderBookRestNotFound,
                    "CLOB returned no order book for the requested token."));
        }
    }

    private RejectionReason BuildCacheFallbackReason(
        string assetId,
        OrderBookCacheLookup cacheLookup,
        TimeSpan maxAge)
    {
        if (!marketDataCache.SubscribedAssetIds.Contains(assetId, StringComparer.OrdinalIgnoreCase))
        {
            return new RejectionReason(
                SignalReasonCodes.MissingOrderBookUnsubscribed,
                "Token is not currently present in the market WebSocket subscription set.");
        }

        return cacheLookup.Status == OrderBookCacheLookupStatus.Stale
            ? new RejectionReason(
                SignalReasonCodes.MissingOrderBookCacheStale,
                $"Cached order book is stale. AgeSeconds={cacheLookup.Age?.TotalSeconds:0.###}; MaxAgeSeconds={maxAge.TotalSeconds:0.###}.")
            : new RejectionReason(
                SignalReasonCodes.MissingOrderBookCacheMiss,
                "Token is subscribed but no order book has been received into the WebSocket cache.");
    }

    private static OrderBookSnapshot NormalizeOrderBook(string requestedAssetId, OrderBookSnapshot orderBook)
    {
        return string.IsNullOrWhiteSpace(orderBook.AssetId)
            ? orderBook with { AssetId = requestedAssetId, SnapshotAtUtc = DateTimeOffset.UtcNow }
            : orderBook;
    }

    private static MarketDataUpdate ToOrderBookMarketDataUpdate(OrderBookSnapshot orderBook)
    {
        return new MarketDataUpdate(
            MarketDataEventType.Book,
            "clob_book",
            orderBook.AssetId,
            orderBook.ConditionId,
            orderBook,
            orderBook.BestBid,
            orderBook.BestAsk,
            null,
            null,
            TradeSide.Unknown,
            false,
            orderBook.SnapshotAtUtc);
    }

    private static OrderBookLookupResult BuildOrderBookLookupResult(
        OrderBookSnapshot? orderBook,
        string rejectionCode,
        string rejectionDetails)
    {
        if (orderBook?.BestBid is not { } || orderBook.BestAsk is not { })
        {
            return new OrderBookLookupResult(
                null,
                new RejectionReason(rejectionCode, rejectionDetails));
        }

        return new OrderBookLookupResult(orderBook, null);
    }

    private static bool IsMissingOrderBook(PolymarketApiException ex)
    {
        return ex.Message.Contains("No orderbook exists", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<TimedValue<T>> TimedAsync<T>(Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var value = await action();
        stopwatch.Stop();
        return new TimedValue<T>(value, stopwatch.ElapsedMilliseconds);
    }

    private void LogCandidateTiming(
        OnChainPaperSignalCandidate candidate,
        string status,
        string decisionCode,
        bool hotPathOrderBook,
        Stopwatch totalStopwatch,
        long orderBookMs,
        long exposureMs,
        long evaluationMs,
        long persistenceMs)
    {
        totalStopwatch.Stop();
        logger.LogInformation(
            "On-chain paper candidate processed. Status={Status} DecisionCode={DecisionCode} HotPathOrderBook={HotPathOrderBook} Wallet={Wallet} TokenId={TokenId} Tx={TransactionHash} Block={BlockNumber} OrderBookMs={OrderBookMs} ExposureMs={ExposureMs} EvaluationMs={EvaluationMs} PersistenceMs={PersistenceMs} TotalMs={TotalMs}",
            status,
            decisionCode,
            hotPathOrderBook,
            candidate.Wallet,
            candidate.TokenId,
            candidate.TransactionHash,
            candidate.BlockNumber,
            orderBookMs,
            exposureMs,
            evaluationMs,
            persistenceMs,
            totalStopwatch.ElapsedMilliseconds);
    }

    private TraderRule ToTraderRule(OnChainPaperSignalCandidate candidate)
    {
        return new TraderRule(
            candidate.Wallet,
            [],
            onChainOptions.PaperSignalMaxLagSeconds,
            executionOptions.MaxSlippageCents,
            executionOptions.MaxSpreadCents,
            executionOptions.MaxSpreadPct,
            executionOptions.MinLeaderTradeUsd,
            Enabled: true);
    }

    private static LeaderTrade ToLeaderTrade(OnChainPaperSignalCandidate candidate)
    {
        return new LeaderTrade(
            candidate.Wallet,
            string.IsNullOrWhiteSpace(candidate.RatingUserName) ? candidate.Wallet : candidate.RatingUserName,
            candidate.ConditionId,
            candidate.TokenId,
            candidate.MarketSlug,
            string.IsNullOrWhiteSpace(candidate.MarketTitle) ? "Unenriched on-chain market" : candidate.MarketTitle,
            string.IsNullOrWhiteSpace(candidate.Outcome) ? "Unknown" : candidate.Outcome,
            candidate.Side,
            candidate.Price,
            candidate.SizeShares,
            candidate.NotionalUsd,
            candidate.BlockTimestampUtc,
            candidate.TransactionHash);
    }

    private static MarketInfo ToMarketInfo(OnChainPaperSignalCandidate candidate)
    {
        return new MarketInfo(
            candidate.ConditionId,
            candidate.MarketSlug,
            candidate.MarketTitle,
            candidate.LocalCategory,
            candidate.MarketEndDateUtc);
    }

    private PolymarketOnChainWalletCategoryPerformance ToSyntheticPerformance(
        OnChainPaperSignalCandidate candidate,
        DateTimeOffset now)
    {
        var positionsCount = Math.Max(
            signalOptions.MinLeaderCategoryResolvedPositions,
            candidate.CurrentPositionsCount + candidate.ClosedPositionsCount);
        var resolvedPositions = Math.Max(signalOptions.MinLeaderCategoryResolvedPositions, candidate.ClosedPositionsCount);
        var volume = candidate.LeaderboardVolumeUsd ?? Math.Abs(candidate.PositionsTotalPnlUsd);
        var pnl = candidate.LeaderboardPnlUsd ?? candidate.PositionsTotalPnlUsd;
        var roi = candidate.LeaderboardPnlToVolumePct ?? candidate.PositionsTotalPercentPnl ?? 0m;
        var firstActive = candidate.RatingRefreshedAtUtc ?? now;

        return new PolymarketOnChainWalletCategoryPerformance(
            candidate.Wallet,
            candidate.LocalCategory ?? string.Empty,
            positionsCount,
            OpenPositions: Math.Max(0, candidate.CurrentPositionsCount),
            FlatPositions: 0,
            resolvedPositions,
            ProfitableResolvedPositions: pnl >= 0m ? resolvedPositions : 0,
            LosingResolvedPositions: pnl < 0m ? resolvedPositions : 0,
            MarketsTraded: positionsCount,
            VolumeUsd: Math.Max(0m, volume),
            ResolvedVolumeUsd: Math.Max(0m, volume),
            OpenExposureUsd: 0m,
            ResolvedCostUsd: Math.Max(0m, volume),
            ResolvedPnlUsd: pnl,
            ResolvedRoiPct: roi,
            WinRatePct: pnl >= 0m ? 100m : 0m,
            AveragePositionSizeUsd: positionsCount == 0 ? 0m : Math.Max(0m, volume) / positionsCount,
            Score: Math.Max(0m, pnl) + Math.Max(0m, roi) + positionsCount,
            SampleQuality: "High",
            FirstActiveUtc: firstActive,
            LastActiveUtc: candidate.RatingRefreshedAtUtc ?? now,
            RefreshedAtUtc: candidate.RatingRefreshedAtUtc ?? now);
    }

    private async Task<PaperCopiedTraderPerformance?> ResolveCopiedTraderPerformanceAsync(
        string copiedTraderWallet,
        string? category,
        CancellationToken cancellationToken)
    {
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (IsUnknownCategory(normalizedCategory))
        {
            return null;
        }

        return await repository.GetPaperCopiedTraderPerformanceAsync(
            copiedTraderWallet.Trim().ToLowerInvariant(),
            normalizedCategory!,
            cancellationToken);
    }

    private static ExposureSnapshot BuildExposure(
        LeaderTrade trade,
        IReadOnlyList<PaperOrder> openOrders,
        IReadOnlyList<PaperPosition> positions,
        IReadOnlyList<LiveOrder> liveOrders)
    {
        var orderExposure = openOrders.Sum(order => order.NotionalUsd);
        var liveOrderExposure = liveOrders.Sum(order => order.NotionalUsd);
        var positionExposure = positions.Sum(position => position.EstimatedValueUsd);
        var marketOrderExposure = openOrders
            .Where(order => string.Equals(order.ConditionId, trade.ConditionId, StringComparison.OrdinalIgnoreCase))
            .Sum(order => order.NotionalUsd);
        var marketLiveOrderExposure = liveOrders
            .Where(order => string.Equals(order.ConditionId, trade.ConditionId, StringComparison.OrdinalIgnoreCase))
            .Sum(order => order.NotionalUsd);
        var marketPositionExposure = positions
            .Where(position => string.Equals(position.ConditionId, trade.ConditionId, StringComparison.OrdinalIgnoreCase))
            .Sum(position => position.EstimatedValueUsd);
        var oldestPaperOrderAgeSeconds = openOrders.Count == 0
            ? 0
            : (int)Math.Max(0, openOrders.Max(order => (DateTimeOffset.UtcNow - order.CreatedAtUtc).TotalSeconds));
        var oldestLiveOrderAgeSeconds = liveOrders.Count == 0
            ? 0
            : (int)Math.Max(0, liveOrders.Max(order => (DateTimeOffset.UtcNow - order.CreatedAtUtc).TotalSeconds));

        return new ExposureSnapshot(
            marketOrderExposure + marketLiveOrderExposure + marketPositionExposure,
            0m,
            0m,
            orderExposure + liveOrderExposure + positionExposure,
            0m,
            openOrders.Count + liveOrders.Count,
            Math.Max(oldestPaperOrderAgeSeconds, oldestLiveOrderAgeSeconds));
    }

    private static PaperPosition? FindCopiedPosition(
        IEnumerable<PaperPosition> positions,
        LeaderTrade trade)
    {
        return positions.FirstOrDefault(position =>
            string.Equals(position.AssetId, trade.AssetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(position.CopiedTraderWallet, trade.TraderWallet, StringComparison.OrdinalIgnoreCase));
    }

    private static Signal ToRejectedSignal(
        LeaderTrade trade,
        IReadOnlyList<RejectionReason> reasons,
        DateTimeOffset createdAtUtc)
    {
        return new Signal(
            Guid.NewGuid(),
            trade,
            0,
            Accepted: false,
            reasons[0].ReasonCode,
            reasons.Select(reason => reason.ReasonCode).ToArray(),
            null,
            null,
            null,
            createdAtUtc);
    }

    private static Signal ToSignal(LeaderTrade trade, SignalDecision decision)
    {
        return new Signal(
            Guid.NewGuid(),
            trade,
            decision.Score,
            decision.Accepted,
            decision.DecisionCode,
            decision.Reasons,
            decision.ProposedPrice,
            decision.ProposedSizeShares,
            decision.ProposedNotionalUsd,
            decision.CreatedAtUtc);
    }

    private async Task PersistSignalRejectionsAsync(
        Guid signalId,
        IReadOnlyList<RejectionReason> reasons,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        foreach (var reason in reasons)
        {
            await repository.AddSignalRejectionAsync(
                new SignalRejection(Guid.NewGuid(), signalId, reason.ReasonCode, reason.ReasonDetails, createdAtUtc),
                cancellationToken);
        }
    }

    private static OnChainPaperSignalResult ToProcessingResult(
        OnChainPaperSignalCandidate candidate,
        string status,
        string decisionCode,
        string reasonDetails,
        Guid? signalId,
        Guid? paperOrderId,
        DateTimeOffset processedAtUtc)
    {
        return new OnChainPaperSignalResult(
            Guid.NewGuid(),
            candidate.CaptureId,
            candidate.TransactionHash,
            candidate.LogIndex,
            candidate.ParticipantRole,
            candidate.Wallet,
            candidate.CounterpartyWallet,
            candidate.Side,
            candidate.TokenId,
            candidate.ConditionId,
            candidate.MarketSlug,
            candidate.Outcome,
            candidate.LocalCategory,
            candidate.PolymarketCategory,
            candidate.RatingFound,
            candidate.LeaderboardRank,
            candidate.LeaderboardPnlUsd,
            candidate.LeaderboardVolumeUsd,
            candidate.LeaderboardPnlToVolumePct,
            signalId,
            paperOrderId,
            status,
            decisionCode,
            reasonDetails,
            processedAtUtc);
    }

    private async Task TryRecordProcessingResultAsync(
        OnChainPaperSignalResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddOnChainPaperSignalResultAsync(result, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist on-chain paper signal result.");
        }
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "OnChainPaperSignalProcessor", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist on-chain paper signal processor API error for {Operation}.", operation);
        }
    }

    private static string JoinReasonDetails(IReadOnlyList<RejectionReason> reasons)
    {
        return string.Join("; ", reasons.Select(reason => reason.ReasonDetails));
    }

    private static bool IsUnknownCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ||
            string.Equals(category, "unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingOrderBookDecision(string decisionCode)
    {
        return string.Equals(decisionCode, SignalReasonCodes.MissingOrderBook, StringComparison.OrdinalIgnoreCase) ||
            decisionCode.StartsWith("missing_orderbook_", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RejectionReason(string ReasonCode, string ReasonDetails);

    private sealed record CandidateOutcome(
        bool SignalCreated,
        bool SignalAccepted,
        bool SignalRejected,
        bool PaperOrderCreated,
        string DecisionCode);

    private sealed record HotCandidateSelection(
        OnChainPaperSignalCandidate Candidate,
        int Score);

    private sealed record OrderBookLookupResult(
        OrderBookSnapshot? OrderBook,
        RejectionReason? RejectionReason);

    private readonly record struct TimedValue<T>(
        T Value,
        long ElapsedMilliseconds);
}
