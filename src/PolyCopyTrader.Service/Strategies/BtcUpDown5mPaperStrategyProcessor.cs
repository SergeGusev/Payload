using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Service.LiveTrading;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.Strategies;

public sealed class BtcUpDown5mPaperStrategyProcessor(
    ILogger<BtcUpDown5mPaperStrategyProcessor> logger,
    BotOptions botOptions,
    PolymarketAuthOptions authOptions,
    PaperTradingOptions paperTradingOptions,
    LiveTradingOptions liveTradingOptions,
    BtcUpDown5mStrategyOptions options,
    MarketDataWebSocketOptions marketDataWebSocketOptions,
    IPolymarketGammaClient gammaClient,
    IPolymarketClobPublicClient clobClient,
    IPolymarketGeoClient geoClient,
    IPolymarketTradingClient tradingClient,
    IPolymarketAuthService authService,
    IBtcUsdReferencePriceClient btcUsdReferencePriceClient,
    IBtcUsdReferencePriceCache btcUsdReferencePriceCache,
    IMarketDataCache marketDataCache,
    IActiveMarketAssetSubscriptionRegistry activeMarketAssetSubscriptionRegistry,
    IExposureSnapshotCache exposureCache,
    ServiceControlState controlState,
    IStrategyStateProvider strategyStateProvider,
    IAppRepository repository) : IBtcUpDown5mPaperStrategyProcessor
{
    private const string GammaOutcomePriceSource = "gamma_outcome_price";
    private const string WebSocketCacheSource = "websocket_cache";
    private const string ClobBookSource = "clob_book";
    private const string CloseBookSnapshotSource = "order_book_snapshot";
    private const string PaperLiveShadowTestSource = "paper_live_shadow_test";
    private const string BtcGtdLimitExecutionSource = "btc_updown5m_gtd_limit";
    private const string BtcSkip1VariantCode = "btc_up_down_5m_skip_1";
    private const string OpeningLimitPricingMode = "paper_gtd_limit";
    private const string OpeningLimitOrderType = "GTD";
    private const decimal AlwaysDirectionLimitPrice = 0.45m;
    private const decimal BinanceStartRelativeDefaultLimitPrice = 0.50m;
    private const int BinanceCleverFairValueLookbackTicks = 2_000;
    private const int BinanceCleverFairValueMinSamples = 20;
    private const decimal BinanceCleverFairValueEdgeMargin = 0.03m;
    private const decimal BinanceCleverMoveScaleBps = 10m;
    private const decimal BinanceCleverTimeScaleSeconds = 60m;
    private const decimal BinanceCleverOneSidedBookDiscount = 0.02m;
    private const decimal BinanceCleverRestBookDiscount = 0.005m;
    private const decimal BinanceCleverSpreadDiscountDivisor = 4m;
    private const decimal MinimumStakeSafetyMultiplier = 1.10m;
    private const decimal FillSizeTolerance = 0.000001m;
    private const decimal CloseBookResultThreshold = 0.50m;
    private const string StakeNotionalRoundingMode = "ceil_usd";
    private static readonly TimeSpan MarketObserveAheadWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MarketObserveBehindWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CloseBookCaptureMaxDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CloseBookCaptureOrderBookTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SettlementMetadataTimeout = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, DateTimeOffset> closingOrderBookCaptureAttempts = new(StringComparer.OrdinalIgnoreCase);

    public async Task<BtcUpDown5mPaperStrategyResult> ProcessAsync(CancellationToken cancellationToken = default)
    {
        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0);
        }

        var now = DateTimeOffset.UtcNow;
        controlState.RecordLoop("BTC5mStrategy loading runtime settings", null);
        var configuredVariants = GetConfiguredVariants();
        var strategySettings = await strategyStateProvider.GetStrategySettingsAsync(cancellationToken);
        var entryVariants = configuredVariants
            .Where(variant => GetStrategySettings(strategySettings, variant.Id).Enabled)
            .ToArray();

        if (entryVariants.Length == 0)
        {
            var settledRuns = await SettleDueRunsAsync(now, StrategyIds.BtcUpDown5mVariants, cancellationToken);
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, settledRuns);
        }

        var martinEntryVariants = entryVariants
            .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.Less180Martin)
            .ToArray();
        var regularEntryVariants = entryVariants
            .Where(variant => variant.Behavior != BtcUpDown5mStrategyBehavior.Less180Martin)
            .ToArray();

        controlState.RecordLoop($"BTC5mStrategy placing regular due entries before observe. Variants={regularEntryVariants.Length}", null);
        var (regularEntriesPlacedBeforeObserve, regularEntrySkippedBeforeObserve) = await PlaceDueEntriesAsync(
            DateTimeOffset.UtcNow,
            regularEntryVariants,
            strategySettings,
            cancellationToken);
        controlState.RecordLoop($"BTC5mStrategy settling Martin runs before entry. Variants={martinEntryVariants.Length}", null);
        var settledRunsBeforeMartinEntries = martinEntryVariants.Length == 0
            ? 0
            : await SettleDueRunsAsync(DateTimeOffset.UtcNow, martinEntryVariants, cancellationToken);

        controlState.RecordLoop($"BTC5mStrategy placing Martin due entries before observe. Variants={martinEntryVariants.Length}", null);
        var (martinEntriesPlacedBeforeObserve, martinEntrySkippedBeforeObserve) = await PlaceDueEntriesAsync(
            DateTimeOffset.UtcNow,
            martinEntryVariants,
            strategySettings,
            cancellationToken);
        controlState.RecordLoop("BTC5mStrategy observing markets", null);
        var observeResult = await ObserveMarketsAsync(
            DateTimeOffset.UtcNow,
            entryVariants,
            strategySettings,
            cancellationToken);
        var observed = observeResult.Observed;
        var observeSkipped = observeResult.Skipped;
        controlState.RecordLoop($"BTC5mStrategy placing regular due entries after observe. Variants={regularEntryVariants.Length}", null);
        var (regularEntriesPlacedAfterObserve, regularEntrySkippedAfterObserve) = await PlaceDueEntriesAsync(
            DateTimeOffset.UtcNow,
            regularEntryVariants,
            strategySettings,
            cancellationToken);
        controlState.RecordLoop($"BTC5mStrategy settling Martin runs after observe. Variants={martinEntryVariants.Length}", null);
        var settledRunsBeforeMartinEntriesAfterObserve = martinEntryVariants.Length == 0
            ? 0
            : await SettleDueRunsAsync(DateTimeOffset.UtcNow, martinEntryVariants, cancellationToken);
        controlState.RecordLoop($"BTC5mStrategy placing Martin due entries after observe. Variants={martinEntryVariants.Length}", null);
        var (martinEntriesPlacedAfterObserve, martinEntrySkippedAfterObserve) = await PlaceDueEntriesAsync(
            DateTimeOffset.UtcNow,
            martinEntryVariants,
            strategySettings,
            cancellationToken);
        controlState.RecordLoop("BTC5mStrategy settling due runs after entries", null);
        var settledRunsAfterEntries = await SettleDueRunsAsync(DateTimeOffset.UtcNow, StrategyIds.BtcUpDown5mVariants, cancellationToken);
        controlState.RecordLoop("BTC5mStrategy capturing close-book snapshots", null);
        await CaptureClosingOrderBookSnapshotsAsync(DateTimeOffset.UtcNow, observeResult.Markets, cancellationToken);
        return new BtcUpDown5mPaperStrategyResult(
            observed,
            regularEntriesPlacedBeforeObserve + martinEntriesPlacedBeforeObserve + regularEntriesPlacedAfterObserve + martinEntriesPlacedAfterObserve,
            observeSkipped + regularEntrySkippedBeforeObserve + martinEntrySkippedBeforeObserve + regularEntrySkippedAfterObserve + martinEntrySkippedAfterObserve,
            settledRunsBeforeMartinEntries + settledRunsBeforeMartinEntriesAfterObserve + settledRunsAfterEntries);
    }

    private IReadOnlyList<BtcUpDown5mStrategyVariant> GetConfiguredVariants()
    {
        if (options.EnabledVariantCodes is null || options.EnabledVariantCodes.Count == 0)
        {
            return StrategyIds.BtcUpDown5mVariants;
        }

        var enabledCodes = options.EnabledVariantCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return StrategyIds.BtcUpDown5mVariants
            .Where(variant => enabledCodes.Contains(variant.Code))
            .ToArray();
    }

    private static StrategyRuntimeSettings GetStrategySettings(
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> settings,
        Guid strategyId)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        return settings.TryGetValue(normalizedStrategyId, out var value)
            ? value
            : StrategyRuntimeSettings.Default(normalizedStrategyId);
    }

    private async Task<ObserveMarketsResult> ObserveMarketsAsync(
        DateTimeOffset nowUtc,
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        CancellationToken cancellationToken)
    {
        var markets = await repository.GetBtcUpDownStrategyGammaMarketsAsync(
            options.MaxMarketsPerCycle,
            cancellationToken);

        var observed = 0;
        var skipped = 0;

        foreach (var market in markets)
        {
            var marketInterval = BtcUpDown5mMarketAnalyzer.GetMarketInterval(market);
            if (marketInterval is null)
            {
                continue;
            }

            var windowStart = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
            if (!ShouldObserveMarketWindow(windowStart, market.EndDateUtc, nowUtc))
            {
                continue;
            }

            foreach (var variant in variants)
            {
                if (!DoesVariantApplyToMarket(variant, marketInterval.Value))
                {
                    continue;
                }

                var settings = GetStrategySettings(strategySettings, variant.Id);
                var entryDueAtUtc = windowStart?.AddSeconds(variant.EntryDelaySeconds) ?? nowUtc;
                var status = StrategyMarketPaperRunStatuses.Observed;
                string? skipReason = null;
                if (windowStart is null)
                {
                    status = StrategyMarketPaperRunStatuses.Skipped;
                    skipReason = "market_start_unknown";
                }
                else if (IsEntryExpired(entryDueAtUtc, nowUtc) &&
                    !IsSkipConsecutiveMarketResults(variant) &&
                    !IsOpeningLimitEntryAllowedAfterEntryGrace(variant, windowStart, nowUtc))
                {
                    status = StrategyMarketPaperRunStatuses.Skipped;
                    skipReason = "entry_due_already_passed";
                }

                var run = new StrategyMarketPaperRun(
                    Guid.NewGuid(),
                    variant.Id,
                    market.MarketId,
                    market.ConditionId,
                    market.Slug,
                    market.Question,
                    market.Category,
                    windowStart,
                    market.EndDateUtc,
                    nowUtc,
                    entryDueAtUtc,
                    status,
                    SelectedAssetId: null,
                    SelectedOutcome: null,
                    EntryPrice: null,
                    settings.PaperStakeAmount,
                    SizeShares: null,
                    SignalId: null,
                    PaperOrderId: null,
                    EnteredAtUtc: null,
                    SettlementPrice: null,
                    SettlementValueUsd: null,
                    RealizedPnlUsd: null,
                    SettledAtUtc: null,
                    skipReason,
                    nowUtc,
                    nowUtc);

                if (await repository.TryAddStrategyMarketPaperRunAsync(run, cancellationToken))
                {
                    if (string.Equals(status, StrategyMarketPaperRunStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                    }
                    else
                    {
                        observed++;
                    }
                }
            }
        }

        return new ObserveMarketsResult(observed, skipped, markets);
    }

    private async Task CaptureClosingOrderBookSnapshotsAsync(
        DateTimeOffset nowUtc,
        IReadOnlyList<PolymarketGammaMarket> markets,
        CancellationToken cancellationToken)
    {
        if (options.CloseBookCaptureLookbackSeconds <= 0)
        {
            return;
        }

        CleanupClosingOrderBookCaptureAttempts(nowUtc);

        var startedUtc = DateTimeOffset.UtcNow;
        var lookback = TimeSpan.FromSeconds(options.CloseBookCaptureLookbackSeconds);
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.CloseBookCaptureIntervalSeconds));
        foreach (var market in markets)
        {
            if (!BtcUpDown5mMarketAnalyzer.IsCandidate(market) ||
                market.EndDateUtc is not { } endUtc ||
                endUtc <= nowUtc ||
                endUtc - nowUtc > lookback)
            {
                continue;
            }

            foreach (var outcome in BtcUpDown5mMarketAnalyzer.GetOutcomeQuotes(market))
            {
                if (string.IsNullOrWhiteSpace(outcome.AssetId))
                {
                    continue;
                }

                var captureKey = string.Concat(outcome.AssetId, "|", endUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
                if (closingOrderBookCaptureAttempts.TryGetValue(captureKey, out var lastAttemptUtc) &&
                    nowUtc - lastAttemptUtc < interval)
                {
                    continue;
                }

                if (DateTimeOffset.UtcNow - startedUtc >= CloseBookCaptureMaxDuration)
                {
                    logger.LogInformation(
                        "BTC close-book snapshot capture stopped after reaching the per-cycle time budget. Markets={Markets} BudgetSeconds={BudgetSeconds}",
                        markets.Count,
                        CloseBookCaptureMaxDuration.TotalSeconds);
                    return;
                }

                closingOrderBookCaptureAttempts[captureKey] = nowUtc;
                using var fetchTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                fetchTimeout.CancelAfter(CloseBookCaptureOrderBookTimeout);
                OrderBookFetchResult fetch;
                try
                {
                    fetch = await FetchAndCacheOrderBookAsync(outcome.AssetId, fetchTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation(
                        "BTC close-book snapshot capture timed out. Market={MarketSlug} AssetId={AssetId} TimeoutSeconds={TimeoutSeconds}",
                        market.Slug,
                        outcome.AssetId,
                        CloseBookCaptureOrderBookTimeout.TotalSeconds);
                    continue;
                }

                if (fetch.OrderBook is null)
                {
                    logger.LogInformation(
                        "BTC close-book snapshot capture skipped because CLOB /book was unavailable. Market={MarketSlug} AssetId={AssetId} Reason={Reason}",
                        market.Slug,
                        outcome.AssetId,
                        fetch.RejectionReason);
                    continue;
                }

                var snapshot = string.IsNullOrWhiteSpace(fetch.OrderBook.ConditionId) &&
                    !string.IsNullOrWhiteSpace(market.ConditionId)
                    ? fetch.OrderBook with { ConditionId = market.ConditionId }
                    : fetch.OrderBook;
                await TryPersistOrderBookSnapshotAsync(
                    snapshot,
                    "CaptureBtcCloseBookOrderBookSnapshot",
                    cancellationToken);
            }
        }
    }

    private static bool ShouldObserveMarketWindow(
        DateTimeOffset? windowStartUtc,
        DateTimeOffset? marketEndUtc,
        DateTimeOffset nowUtc)
    {
        if (windowStartUtc is null)
        {
            return true;
        }

        if (windowStartUtc.Value > nowUtc.Add(MarketObserveAheadWindow))
        {
            return false;
        }

        if (marketEndUtc is { } endUtc)
        {
            return endUtc >= nowUtc.Subtract(MarketObserveBehindWindow);
        }

        return windowStartUtc.Value >= nowUtc.Subtract(MarketObserveBehindWindow);
    }

    private static bool DoesVariantApplyToMarket(
        BtcUpDown5mStrategyVariant variant,
        BtcUpDownMarketInterval marketInterval)
    {
        return variant.MarketInterval == marketInterval;
    }

    private void CleanupClosingOrderBookCaptureAttempts(DateTimeOffset nowUtc)
    {
        if (closingOrderBookCaptureAttempts.Count == 0)
        {
            return;
        }

        var cutoffUtc = nowUtc.AddMinutes(-30);
        foreach (var item in closingOrderBookCaptureAttempts.Where(item => item.Value < cutoffUtc).ToArray())
        {
            closingOrderBookCaptureAttempts.Remove(item.Key);
        }
    }

    private async Task<(int EntriesPlaced, int RunsSkipped)> PlaceDueEntriesAsync(
        DateTimeOffset nowUtc,
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        CancellationToken cancellationToken)
    {
        if (variants.Count == 0)
        {
            return (0, 0);
        }

        var variantsById = variants
            .ToDictionary(variant => StrategyIds.Normalize(variant.Id));
        var runs = await repository.GetDueStrategyMarketPaperRunsAsync(
            variantsById.Keys.ToArray(),
            StrategyMarketPaperRunStatuses.Observed,
            nowUtc,
            options.MaxEntriesPerCycle,
            cancellationToken);
        if (runs.Count == 0)
        {
            return (0, 0);
        }

        var maxConcurrency = Math.Max(1, Math.Min(options.MaxConcurrentEntryDecisions, runs.Count));
        var btcCurrentPrices = new System.Collections.Concurrent.ConcurrentDictionary<string, BtcCurrentPriceLookupResult>(
            StringComparer.OrdinalIgnoreCase);
        var marketLookupTasks = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<PolymarketGammaMarket?>>>(
            StringComparer.OrdinalIgnoreCase);
        using var throttler = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = runs.Select(async run =>
        {
            if (!variantsById.TryGetValue(StrategyIds.Normalize(run.StrategyId), out var variant))
            {
                return (EntriesPlaced: 0, RunsSkipped: 0);
            }

            await throttler.WaitAsync(cancellationToken);
            try
            {
                return await PlaceDueEntryRunAsync(
                    DateTimeOffset.UtcNow,
                    run,
                    variant,
                    strategySettings,
                    btcCurrentPrices,
                    marketLookupTasks,
                    cancellationToken);
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        return (results.Sum(item => item.EntriesPlaced), results.Sum(item => item.RunsSkipped));
    }

    private async Task<(int EntriesPlaced, int RunsSkipped)> PlaceDueEntryRunAsync(
        DateTimeOffset nowUtc,
        StrategyMarketPaperRun dueRun,
        BtcUpDown5mStrategyVariant variant,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        IDictionary<string, BtcCurrentPriceLookupResult> btcCurrentPrices,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<PolymarketGammaMarket?>>> marketLookupTasks,
        CancellationToken cancellationToken)
    {
        var entriesPlaced = 0;
        var runsSkipped = 0;

        foreach (var run in new[] { dueRun })
        {
                nowUtc = DateTimeOffset.UtcNow;
                try
                {
                    var settings = GetStrategySettings(strategySettings, variant.Id);
                    if (IsEntryExpired(run.EntryDueAtUtc, nowUtc) &&
                        !IsSkipConsecutiveMarketResults(variant) &&
                        !IsOpeningLimitEntryAllowedAfterEntryGrace(variant, run.MarketStartUtc, nowUtc))
                    {
                        await SkipRunAsync(run, variant, "entry_due_expired", nowUtc, cancellationToken);
                        runsSkipped++;
                        continue;
                    }

                    var market = await GetPolymarketGammaMarketForEntryAsync(
                        marketLookupTasks,
                        run.MarketId,
                        cancellationToken);
                    if (market is null)
                    {
                        await SkipRunAsync(run, variant, "market_not_found", nowUtc, cancellationToken);
                        runsSkipped++;
                        continue;
                    }

                    if (market.EndDateUtc is { } endDate && endDate <= nowUtc)
                    {
                        await SkipRunAsync(run, variant, "market_already_ended", nowUtc, cancellationToken);
                        runsSkipped++;
                        continue;
                    }

                    if (market.Closed || market.Archived)
                    {
                        await SkipRunAsync(run, variant, "market_not_tradeable", nowUtc, cancellationToken);
                        runsSkipped++;
                        continue;
                    }

                    if (IsPreOpenEntryWindowElapsed(variant, BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market) ?? run.MarketStartUtc, nowUtc))
                    {
                        await SkipRunAsync(run, variant, "preopen_entry_window_elapsed", nowUtc, cancellationToken);
                        runsSkipped++;
                        continue;
                    }

                    if (!market.AcceptingOrders || !market.EnableOrderBook)
                    {
                        if (ShouldDeferUntilTradingStarts(run, variant, nowUtc))
                        {
                            continue;
                        }

                        await SkipRunAsync(run, variant, "market_not_tradeable", nowUtc, cancellationToken);
                        runsSkipped++;
                        continue;
                    }

                    var stakeMultiplier = settings.PaperStakeAmount;
                    if (variant.Behavior == BtcUpDown5mStrategyBehavior.Less180Martin)
                    {
                        var martinDecision = await GetLess180MartinEntryDecisionAsync(stakeMultiplier, cancellationToken);
                        if (!martinDecision.ShouldEnter)
                        {
                            await SkipRunAsync(
                                run,
                                variant,
                                martinDecision.SkipReason ?? "martin_not_triggered",
                                nowUtc,
                                cancellationToken);
                            runsSkipped++;
                            continue;
                        }

                        stakeMultiplier = martinDecision.StakeUsd;
                    }

                    if (UsesOpeningLimitEntry(variant))
                    {
                        var limitDecision = await GetOpeningLimitEntryDecisionAsync(
                            market,
                            variant,
                            stakeMultiplier,
                            nowUtc,
                            btcCurrentPrices,
                            cancellationToken);
                        if (!limitDecision.ShouldEnter || limitDecision.SelectedOutcome is null)
                        {
                            if (ShouldDeferOpeningLimitDecision(run, variant, limitDecision, nowUtc))
                            {
                                continue;
                            }

                            await SkipRunAsync(
                                run,
                                variant,
                                limitDecision.SkipReason ?? "gtd_limit_decision_rejected",
                                nowUtc,
                                cancellationToken,
                                limitDecision.RawDecisionJson);
                            runsSkipped++;
                            continue;
                        }

                        var limitPricing = await GetOpeningLimitPriceAsync(
                            variant,
                            limitDecision.SelectedOutcome.AssetId,
                            limitDecision.RawDecisionJson,
                            limitDecision.LimitPriceOverride,
                            nowUtc,
                            cancellationToken);
                        if (!limitPricing.ShouldEnter)
                        {
                            await SkipRunAsync(
                                run,
                                variant,
                                limitPricing.SkipReason ?? "opening_limit_price_rejected",
                                nowUtc,
                                cancellationToken,
                                limitPricing.RawDecisionJson);
                            runsSkipped++;
                            continue;
                        }

                        var limitPrice = limitPricing.LimitPrice;
                        var limitSelectedOutcome = limitDecision.SelectedOutcome;
                        var limitSizing = await GetOpeningLimitStakeSizingAsync(
                            limitSelectedOutcome.AssetId,
                            limitPrice,
                            stakeMultiplier,
                            market.OrderMinSize,
                            nowUtc,
                            cancellationToken);
                        var expiration = ResolveOpeningLimitExpiration(market, variant, nowUtc);
                        if (!expiration.Available || expiration.LocalExpiresAtUtc is null)
                        {
                            await SkipRunAsync(
                                run,
                                variant,
                                expiration.RejectionReason ?? "opening_limit_expiration_rejected",
                                nowUtc,
                                cancellationToken,
                                limitPricing.RawDecisionJson);
                            runsSkipped++;
                            continue;
                        }

                        var cancelDeadlineUtc = expiration.LocalExpiresAtUtc.Value;
                        var limitRawDecisionJson = AttachOpeningLimitStakeSizingJson(
                            limitPricing.RawDecisionJson,
                            stakeMultiplier,
                            limitSizing,
                            expiration);
                        if (!limitSizing.Available)
                        {
                            if (ShouldDeferOpeningLimitStakeSizing(run, variant, limitSizing, nowUtc))
                            {
                                continue;
                            }

                            await SkipRunAsync(
                                run,
                                variant,
                                limitSizing.RejectionReason ?? "opening_limit_stake_sizing_rejected",
                                nowUtc,
                                cancellationToken,
                                limitRawDecisionJson);
                            runsSkipped++;
                            continue;
                        }

                        var stakeUsd = limitSizing.TargetNotionalUsd;
                        var limitSizeShares = limitSizing.TargetSizeShares;
                        var isPaperLiveShadowTest = ShouldRunPaperLiveShadowTest(variant, settings);
                        Guid? correlationId = null;
                        if (isPaperLiveShadowTest)
                        {
                            var shadowSnapshot = await GetPaperLiveShadowOrderBookSnapshotAsync(
                                limitSelectedOutcome.AssetId,
                                nowUtc,
                                cancellationToken);
                            if (shadowSnapshot.OrderBook is null)
                            {
                                var shadowRawDecisionJson = AttachPaperLiveShadowDecisionJson(
                                    limitRawDecisionJson,
                                    null,
                                    null,
                                    null,
                                    "paper_live_shadow_snapshot_missing",
                                    PaperLiveShadowTestSource,
                                    expiration);
                                await SkipRunAsync(
                                    run,
                                    variant,
                                    shadowSnapshot.RejectionReason ?? "paper_live_shadow_snapshot_missing",
                                    nowUtc,
                                    cancellationToken,
                                    shadowRawDecisionJson);
                                runsSkipped++;
                                continue;
                            }

                            correlationId = Guid.NewGuid();
                            var quoteAgeMs = (int)Math.Round(GetSnapshotAge(shadowSnapshot.OrderBook.SnapshotAtUtc).TotalMilliseconds);
                            var shadowDecision = new PaperLiveShadowDecision(
                                correlationId.Value,
                                variant.Id,
                                market.MarketId,
                                market.ConditionId,
                                limitSelectedOutcome.AssetId,
                                limitSelectedOutcome.Outcome,
                                TradeSide.Buy,
                                limitPrice,
                                stakeUsd,
                                limitSizeShares,
                                limitSizeShares * limitPrice,
                                OpeningLimitOrderType,
                                false,
                                SerializePaperLiveShadowOrderBookSnapshot(shadowSnapshot.OrderBook, shadowSnapshot.Source, shadowSnapshot.Age),
                                quoteAgeMs,
                                PaperLiveShadowTestSource,
                                shadowSnapshot.OrderBook.SnapshotAtUtc,
                                nowUtc,
                                BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market),
                                market.EndDateUtc,
                                nowUtc.AddSeconds(Math.Min(10, Math.Max(1, options.EntryGraceSeconds))),
                                cancelDeadlineUtc,
                                Status: "decision_created",
                                UpdatedAtUtc: nowUtc);
                            await repository.AddPaperLiveShadowDecisionAsync(shadowDecision, cancellationToken);
                            limitRawDecisionJson = AttachPaperLiveShadowDecisionJson(
                                limitRawDecisionJson,
                                correlationId,
                                quoteAgeMs,
                                shadowSnapshot.OrderBook,
                                null,
                                PaperLiveShadowTestSource,
                                expiration);
                        }

                        var limitSignal = CreateSignal(
                            market,
                            limitSelectedOutcome,
                            variant,
                            limitPrice,
                            limitSizeShares,
                            stakeUsd,
                            nowUtc);
                        var limitOrder = CreatePendingOpeningLimitPaperOrder(
                            limitSignal,
                            limitSelectedOutcome,
                            variant,
                            limitPrice,
                            limitSizeShares,
                            stakeUsd,
                            nowUtc,
                            cancelDeadlineUtc,
                            limitRawDecisionJson,
                            correlationId,
                            isPaperLiveShadowTest ? PaperLiveShadowTestSource : string.Empty);

                        await repository.AddSignalAsync(limitSignal, cancellationToken);
                        await repository.AddPaperOrderAsync(limitOrder, cancellationToken);
                        exposureCache.ApplyPaperOrder(limitOrder);
                        if (isPaperLiveShadowTest && correlationId is { } shadowCorrelationId)
                        {
                            await repository.UpdatePaperLiveShadowDecisionLinksAsync(
                                shadowCorrelationId,
                                limitSignal.Id,
                                limitOrder.Id,
                                null,
                                "paper_shadow_created",
                                nowUtc,
                                cancellationToken);
                            await TryPlacePaperLiveShadowOrderAsync(
                                limitSignal,
                                limitSelectedOutcome,
                                variant,
                                limitOrder,
                                limitPrice,
                                limitSizeShares,
                                stakeUsd,
                                expiration,
                                shadowCorrelationId,
                                BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market),
                                market.EndDateUtc,
                                nowUtc,
                                cancellationToken);
                        }

                        await repository.UpdateStrategyMarketPaperRunAsync(
                            run with
                            {
                                ConditionId = market.ConditionId,
                                MarketSlug = market.Slug,
                                MarketTitle = market.Question,
                                Category = market.Category,
                                MarketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market) ?? run.MarketStartUtc,
                                MarketEndUtc = market.EndDateUtc,
                                Status = StrategyMarketPaperRunStatuses.Entered,
                                SelectedAssetId = limitSelectedOutcome.AssetId,
                                SelectedOutcome = limitSelectedOutcome.Outcome,
                                EntryPrice = limitPrice,
                                StakeUsd = stakeUsd,
                                SizeShares = limitSizeShares,
                                SignalId = limitSignal.Id,
                                PaperOrderId = limitOrder.Id,
                                EnteredAtUtc = nowUtc,
                                UpdatedAtUtc = nowUtc
                            },
                            cancellationToken);

                        entriesPlaced++;

                        logger.LogInformation(
                            "BTC Up or Down 5m GTD limit paper order placed. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} Price={Price} StakeUsd={StakeUsd} SizeShares={SizeShares}",
                            variant.Code,
                            market.Slug,
                            limitSelectedOutcome.Outcome,
                            limitPrice,
                            stakeUsd,
                            limitSizeShares);
                        continue;
                    }

                    BtcUpDown5mOutcomeQuote? selectedOutcome;
                    BtcPaperEntryPricingResult entryPricing;
                    if (options.PaperTakerPricingEnabled && !UsesGammaOutcomeSelection(variant))
                    {
                        var outcomeSelection = await GetTakerPaperOutcomeSelectionAsync(
                            market,
                            variant,
                            stakeMultiplier,
                            nowUtc,
                            cancellationToken);
                        if (!outcomeSelection.Filled ||
                            outcomeSelection.SelectedOutcome is null ||
                            outcomeSelection.EntryPricing is null)
                        {
                            await SkipRunAsync(
                                run,
                                variant,
                                outcomeSelection.RejectionReason ?? "paper_taker_outcome_selection_rejected",
                                nowUtc,
                                cancellationToken,
                                outcomeSelection.SkipDiagnosticsJson);
                            runsSkipped++;
                            continue;
                        }

                        selectedOutcome = outcomeSelection.SelectedOutcome;
                        entryPricing = outcomeSelection.EntryPricing;
                    }
                    else
                    {
                        selectedOutcome = SelectOutcome(market, variant);
                        if (selectedOutcome is null)
                        {
                            await SkipRunAsync(run, variant, "target_outcome_not_available", nowUtc, cancellationToken);
                            runsSkipped++;
                            continue;
                        }

                        if (!IsDirectionalPriceAllowedForVariant(selectedOutcome.Price, variant))
                        {
                            await SkipRunAsync(
                                run,
                                variant,
                                SignalReasonCodes.OutcomePriceDirectionMismatch,
                                nowUtc,
                                cancellationToken);
                            runsSkipped++;
                            continue;
                        }

                        entryPricing = await GetPaperEntryPricingAsync(
                            market,
                            selectedOutcome,
                            variant,
                            stakeMultiplier,
                            nowUtc,
                            enforceTakerDirectionalPrice: !UsesGammaOutcomeSelection(variant),
                            cancellationToken);
                    }

                    if (!entryPricing.Filled)
                    {
                        await SkipRunAsync(
                            run,
                            variant,
                            entryPricing.RejectionReason ?? "paper_entry_pricing_rejected",
                            nowUtc,
                            cancellationToken,
                            entryPricing.RawDecisionJson);
                        runsSkipped++;
                        continue;
                    }

                    var gtdLimitPrice = entryPricing.AverageFillPrice;
                    var gtdSizing = entryPricing.OrderBookLookup?.OrderBook is { } sizingOrderBook
                        ? CreateLimitMinimumStakeSizing(sizingOrderBook, gtdLimitPrice, stakeMultiplier, entryPricing.Source)
                        : entryPricing.Sizing ?? BtcMinimumStakeSizing.FallbackFixedStake(
                            stakeMultiplier,
                            gtdLimitPrice,
                            entryPricing.Source);
                    if (!gtdSizing.Available)
                    {
                        await SkipRunAsync(
                            run,
                            variant,
                            gtdSizing.RejectionReason ?? "paper_gtd_stake_sizing_rejected",
                            nowUtc,
                            cancellationToken,
                            entryPricing.RawDecisionJson);
                        runsSkipped++;
                        continue;
                    }

                    var sizeShares = gtdSizing.TargetSizeShares > 0m
                        ? gtdSizing.TargetSizeShares
                        : entryPricing.SizeShares;
                    var reservedNotionalUsd = gtdSizing.TargetNotionalUsd > 0m
                        ? gtdSizing.TargetNotionalUsd
                        : sizeShares * gtdLimitPrice;
                    var gtdExpiration = ResolveOpeningLimitExpiration(market, variant, nowUtc);
                    if (!gtdExpiration.Available || gtdExpiration.LocalExpiresAtUtc is null)
                    {
                        await SkipRunAsync(
                            run,
                            variant,
                            gtdExpiration.RejectionReason ?? "paper_gtd_expiration_rejected",
                            nowUtc,
                            cancellationToken,
                            entryPricing.RawDecisionJson);
                        runsSkipped++;
                        continue;
                    }

                    var gtdCancelDeadlineUtc = gtdExpiration.LocalExpiresAtUtc.Value;
                    var rawDecisionJson = AttachConvertedTakerGtdPricingJson(
                        entryPricing.RawDecisionJson,
                        gtdLimitPrice,
                        entryPricing.Source,
                        entryPricing.Evidence);
                    rawDecisionJson = AttachOpeningLimitStakeSizingJson(
                        rawDecisionJson,
                        stakeMultiplier,
                        gtdSizing,
                        gtdExpiration);
                    var signal = CreateSignal(market, selectedOutcome, variant, gtdLimitPrice, sizeShares, reservedNotionalUsd, nowUtc);
                    var order = CreatePendingOpeningLimitPaperOrder(
                        signal,
                        selectedOutcome,
                        variant,
                        gtdLimitPrice,
                        sizeShares,
                        reservedNotionalUsd,
                        nowUtc,
                        gtdCancelDeadlineUtc,
                        rawDecisionJson,
                        executionSource: BtcGtdLimitExecutionSource);

                    await repository.AddSignalAsync(signal, cancellationToken);
                    await repository.AddPaperOrderAsync(order, cancellationToken);
                    exposureCache.ApplyPaperOrder(order);

                    await repository.UpdateStrategyMarketPaperRunAsync(
                        run with
                        {
                            ConditionId = market.ConditionId,
                            MarketSlug = market.Slug,
                            MarketTitle = market.Question,
                            Category = market.Category,
                            MarketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market) ?? run.MarketStartUtc,
                            MarketEndUtc = market.EndDateUtc,
                            Status = StrategyMarketPaperRunStatuses.Entered,
                            SelectedAssetId = selectedOutcome.AssetId,
                            SelectedOutcome = selectedOutcome.Outcome,
                            EntryPrice = gtdLimitPrice,
                            StakeUsd = reservedNotionalUsd,
                            SizeShares = sizeShares,
                            SignalId = signal.Id,
                            PaperOrderId = order.Id,
                            EnteredAtUtc = nowUtc,
                            UpdatedAtUtc = nowUtc
                        },
                        cancellationToken);

                    if (settings.LiveStakes &&
                        botOptions.Mode == BotMode.Live &&
                        !UsesGammaOutcomeSelection(variant) &&
                            !UsesOpeningLimitEntry(variant) &&
                            CanSubmitLegacyBtcLiveOrder(variant))
                    {
                        await TryPlaceLiveOrderAsync(
                            signal,
                            selectedOutcome,
                            variant,
                            gtdLimitPrice,
                            settings.LiveStakeAmount,
                            nowUtc,
                            cancellationToken);
                    }

                    entriesPlaced++;

                    logger.LogInformation(
                        "BTC Up or Down 5m GTD paper order placed. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} Price={Price} StakeUsd={StakeUsd} SizeShares={SizeShares}",
                        variant.Code,
                        market.Slug,
                        selectedOutcome.Outcome,
                        gtdLimitPrice,
                        reservedNotionalUsd,
                        sizeShares);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "BTC Up or Down 5m paper entry failed. Strategy={StrategyCode} MarketId={MarketId}.",
                        variant.Code,
                        run.MarketId);
                    await TryRecordApiErrorAsync("PlaceDueEntry", ex.Message, cancellationToken);
                }
            }

        return (entriesPlaced, runsSkipped);
    }

    private Task<PolymarketGammaMarket?> GetPolymarketGammaMarketForEntryAsync(
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<PolymarketGammaMarket?>>> marketLookupTasks,
        string marketId,
        CancellationToken cancellationToken)
    {
        var lookup = marketLookupTasks.GetOrAdd(
            marketId,
            key => new Lazy<Task<PolymarketGammaMarket?>>(
                () => repository.GetPolymarketGammaMarketAsync(key, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lookup.Value;
    }

    private async Task<int> SettleDueRunsAsync(
        DateTimeOffset nowUtc,
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        CancellationToken cancellationToken)
    {
        var variantsById = variants.ToDictionary(variant => StrategyIds.Normalize(variant.Id));
        if (variants.Count == 0)
        {
            return 0;
        }

        var runs = await repository.GetStrategyMarketPaperRunsForSettlementAsync(
            variantsById.Keys.ToArray(),
            nowUtc,
            options.MaxSettlementsPerCycle,
            cancellationToken);
        if (runs.Count == 0)
        {
            return 0;
        }

        var maxConcurrency = Math.Max(1, Math.Min(options.MaxConcurrentSettlements, runs.Count));
        var metadataLookupTasks = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<PolymarketOnChainTokenMetadata>>>>(
            StringComparer.OrdinalIgnoreCase);
        using var throttler = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = runs.Select(async run =>
        {
            if (!variantsById.TryGetValue(StrategyIds.Normalize(run.StrategyId), out var runVariant))
            {
                return 0;
            }

            await throttler.WaitAsync(cancellationToken);
            try
            {
                return await SettleDueRunAsync(
                    DateTimeOffset.UtcNow,
                    run,
                    runVariant,
                    metadataLookupTasks,
                    cancellationToken);
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        return results.Sum();
    }

    private async Task<int> SettleDueRunAsync(
        DateTimeOffset nowUtc,
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant runVariant,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<PolymarketOnChainTokenMetadata>>>> metadataLookupTasks,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(run.SelectedAssetId) ||
            string.IsNullOrWhiteSpace(run.SelectedOutcome) ||
            run.EntryPrice is null ||
            run.SizeShares is null)
        {
            await SkipRunAsync(run, runVariant, "entry_details_missing", nowUtc, cancellationToken);
            return 0;
        }

        OpeningLimitFillSummary? openingLimitFillSummary = null;
        if (UsesOpeningLimitEntry(runVariant))
        {
            openingLimitFillSummary = await GetOpeningLimitFillSummaryAsync(run, runVariant, nowUtc, cancellationToken);
            if (openingLimitFillSummary is null)
            {
                return 0;
            }
        }
        else if (UsesConvertedTakerGtdPaperOrderSettlement(runVariant) &&
            run.PaperOrderId is { } settlementPaperOrderId)
        {
            var paperOrder = await repository.GetPaperOrderAsync(settlementPaperOrderId, cancellationToken);
            if (IsConvertedTakerGtdPaperOrder(paperOrder))
            {
                openingLimitFillSummary = await GetOpeningLimitFillSummaryAsync(run, runVariant, nowUtc, cancellationToken);
                if (openingLimitFillSummary is null)
                {
                    return 0;
                }
            }
        }

        try
        {
            using var metadataTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            metadataTimeout.CancelAfter(SettlementMetadataTimeout);
            var metadata = await GetSettlementMetadataAsync(
                run,
                metadataLookupTasks,
                metadataTimeout.Token);

            var winningOutcome = metadata
                .FirstOrDefault(item => item.Resolved && !string.IsNullOrWhiteSpace(item.WinningOutcome))
                ?.WinningOutcome;
            if (string.IsNullOrWhiteSpace(winningOutcome))
            {
                return 0;
            }

            var winningAssetId = metadata
                .FirstOrDefault(item => string.Equals(item.Outcome, winningOutcome, StringComparison.OrdinalIgnoreCase))
                ?.TokenId;
            var won = string.Equals(run.SelectedAssetId, winningAssetId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(run.SelectedOutcome, winningOutcome, StringComparison.OrdinalIgnoreCase);
            var settlementPrice = won ? 1m : 0m;
            var settledSizeShares = openingLimitFillSummary?.SizeShares ?? run.SizeShares.Value;
            var entryPrice = openingLimitFillSummary?.AverageFillPrice ?? run.EntryPrice.Value;
            var costBasisUsd = openingLimitFillSummary?.NotionalUsd ?? run.StakeUsd;
            var settlementValue = settledSizeShares * settlementPrice;
            var realizedPnl = settlementValue - costBasisUsd;
            var category = metadata.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Category))?.Category ?? run.Category;

            var settlement = new PaperPositionSettlement(
                Guid.NewGuid(),
                runVariant.CopiedTraderWallet,
                run.SelectedAssetId,
                run.ConditionId,
                run.SelectedOutcome,
                winningAssetId,
                winningOutcome,
                category,
                settledSizeShares,
                entryPrice,
                costBasisUsd,
                settlementValue,
                realizedPnl,
                won,
                "BtcUpDown5mGammaClosedMarket",
                nowUtc,
                nowUtc);

            await repository.TryAddPaperPositionSettlementAsync(settlement, cancellationToken);
            var settledPosition = new PaperPosition(
                run.SelectedAssetId,
                run.ConditionId,
                run.SelectedOutcome,
                0m,
                0m,
                0m,
                0m,
                nowUtc,
                runVariant.CopiedTraderWallet);
            await repository.UpsertPaperPositionAsync(settledPosition, cancellationToken);
            exposureCache.ApplyPaperPosition(settledPosition);

            await repository.UpdateStrategyMarketPaperRunAsync(
                run with
                {
                    Status = StrategyMarketPaperRunStatuses.Settled,
                    EntryPrice = entryPrice,
                    StakeUsd = costBasisUsd,
                    SizeShares = settledSizeShares,
                    SettlementPrice = settlementPrice,
                    SettlementValueUsd = settlementValue,
                    RealizedPnlUsd = realizedPnl,
                    SettledAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                },
                cancellationToken);

            logger.LogInformation(
                "BTC Up or Down 5m paper run settled. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} Won={Won} RealizedPnlUsd={RealizedPnlUsd}",
                runVariant.Code,
                run.MarketSlug,
                run.SelectedOutcome,
                won,
                realizedPnl);

            return 1;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogInformation(
                "BTC Up or Down 5m paper settlement metadata request timed out. Strategy={StrategyCode} MarketId={MarketId} TimeoutSeconds={TimeoutSeconds}",
                runVariant.Code,
                run.MarketId,
                SettlementMetadataTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "BTC Up or Down 5m paper settlement failed. Strategy={StrategyCode} MarketId={MarketId}.",
                runVariant.Code,
                run.MarketId);
            await TryRecordApiErrorAsync("SettleDueRun", ex.Message, cancellationToken);
        }

        return 0;
    }

    private Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetSettlementMetadataAsync(
        StrategyMarketPaperRun run,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<PolymarketOnChainTokenMetadata>>>> metadataLookupTasks,
        CancellationToken cancellationToken)
    {
        var selectedAssetId = run.SelectedAssetId ?? string.Empty;
        var cacheKey = string.IsNullOrWhiteSpace(selectedAssetId)
            ? "condition:" + run.ConditionId
            : "asset:" + selectedAssetId;
        var lookup = metadataLookupTasks.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<IReadOnlyList<PolymarketOnChainTokenMetadata>>>(
                () => LoadSettlementMetadataAsync(selectedAssetId, run.ConditionId, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lookup.Value;
    }

    private async Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> LoadSettlementMetadataAsync(
        string selectedAssetId,
        string conditionId,
        CancellationToken cancellationToken)
    {
        var metadata = string.IsNullOrWhiteSpace(selectedAssetId)
            ? Array.Empty<PolymarketOnChainTokenMetadata>()
            : await gammaClient.GetTokenMetadataAsync(selectedAssetId, closed: true, cancellationToken);
        if (metadata.Count == 0 && !string.IsNullOrWhiteSpace(conditionId))
        {
            metadata = await gammaClient.GetTokenMetadataByConditionIdAsync(
                conditionId,
                selectedAssetId,
                closed: true,
                cancellationToken);
        }

        return metadata;
    }

    private async Task<OpeningLimitFillSummary?> GetOpeningLimitFillSummaryAsync(
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (run.PaperOrderId is not { } paperOrderId)
        {
            await SkipRunAsync(run, variant, "paper_order_missing", nowUtc, cancellationToken);
            return null;
        }

        var order = await repository.GetPaperOrderAsync(paperOrderId, cancellationToken);
        if (order is null)
        {
            await SkipRunAsync(run, variant, "paper_order_not_found", nowUtc, cancellationToken);
            return null;
        }

        var fills = await repository.GetPaperFillsForOrderAsync(paperOrderId, cancellationToken);
        var fillSummary = SummarizeOpeningLimitFills(order, fills);
        if (fillSummary is not null)
        {
            var synchronizedOrder = SynchronizeOpeningLimitFilledOrderStatus(order, fillSummary, nowUtc);
            if (synchronizedOrder.Status != order.Status ||
                synchronizedOrder.FilledAtUtc != order.FilledAtUtc ||
                synchronizedOrder.CancelledAtUtc != order.CancelledAtUtc)
            {
                await repository.UpdatePaperOrderAsync(synchronizedOrder, cancellationToken);
                exposureCache.ApplyPaperOrder(synchronizedOrder);
            }

            return fillSummary;
        }

        if (order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled &&
            order.ExpiresAtUtc <= nowUtc)
        {
            var expiredOrder = order with { Status = PaperOrderStatus.Expired };
            await repository.UpdatePaperOrderAsync(expiredOrder, cancellationToken);
            exposureCache.ApplyPaperOrder(expiredOrder);
        }

        await SkipRunAsync(run, variant, "gtd_limit_not_filled", nowUtc, cancellationToken);
        return null;
    }

    private static PaperOrder SynchronizeOpeningLimitFilledOrderStatus(
        PaperOrder order,
        OpeningLimitFillSummary fillSummary,
        DateTimeOffset nowUtc)
    {
        if (fillSummary.SizeShares >= order.SizeShares - FillSizeTolerance)
        {
            return order with
            {
                Status = PaperOrderStatus.Filled,
                FilledAtUtc = fillSummary.LastFilledAtUtc ?? order.FilledAtUtc
            };
        }

        return order.ExpiresAtUtc <= nowUtc
            ? order with { Status = PaperOrderStatus.PartiallyFilledExpired }
            : order with { Status = PaperOrderStatus.PartiallyFilled };
    }

    private static OpeningLimitFillSummary? SummarizeOpeningLimitFills(
        PaperOrder order,
        IReadOnlyList<PaperFill> fills)
    {
        var sizeShares = 0m;
        var notionalUsd = 0m;
        DateTimeOffset? lastFilledAtUtc = null;

        foreach (var fill in fills.OrderBy(fill => fill.FilledAtUtc).ThenBy(fill => fill.Id))
        {
            if (sizeShares >= order.SizeShares)
            {
                break;
            }

            var fillSize = Math.Max(0m, fill.SizeShares);
            var takeShares = Math.Min(order.SizeShares - sizeShares, fillSize);
            if (takeShares <= 0m)
            {
                continue;
            }

            sizeShares += takeShares;
            notionalUsd += takeShares * fill.Price;
            lastFilledAtUtc = fill.FilledAtUtc;
        }

        if (sizeShares <= 0m)
        {
            return null;
        }

        return new OpeningLimitFillSummary(
            sizeShares,
            notionalUsd / sizeShares,
            notionalUsd,
            lastFilledAtUtc);
    }

    private async Task<Less180MartinEntryDecision> GetLess180MartinEntryDecisionAsync(
        decimal baseStakeUsd,
        CancellationToken cancellationToken)
    {
        var martinRuns = await repository.GetRecentStrategyMarketPaperRunsAsync(
            StrategyIds.BtcUpDown5mLess180Martin,
            StrategyMarketPaperRunStatuses.Settled,
            options.MartinStateLookbackRuns,
            cancellationToken);
        var latestMartinRun = martinRuns.FirstOrDefault();
        if (latestMartinRun is { RealizedPnlUsd: < 0m })
        {
            return Less180MartinEntryDecision.Enter(GetNextMartinStake(latestMartinRun.StakeUsd, baseStakeUsd));
        }

        var resetAfterUtc = latestMartinRun?.SettledAtUtc ?? latestMartinRun?.UpdatedAtUtc;
        var less180Variant = StrategyIds.GetBtcUpDown5mVariant(
            BtcUpDown5mStrategyDirection.Less,
            180);
        var less180Runs = await repository.GetRecentStrategyMarketPaperRunsAsync(
            less180Variant.Id,
            StrategyMarketPaperRunStatuses.Settled,
            options.MartinStateLookbackRuns,
            cancellationToken);
        var consecutiveLosses = 0;
        foreach (var run in less180Runs)
        {
            var runSettledAtUtc = run.SettledAtUtc ?? run.UpdatedAtUtc;
            if (resetAfterUtc is not null && runSettledAtUtc <= resetAfterUtc.Value)
            {
                break;
            }

            if (run.RealizedPnlUsd is < 0m)
            {
                consecutiveLosses++;
                continue;
            }

            break;
        }

        if (consecutiveLosses >= options.MartinTriggerLosses)
        {
            return Less180MartinEntryDecision.Enter(baseStakeUsd);
        }

        return Less180MartinEntryDecision.Skip(
            $"martin_waiting_for_less180_losses_{consecutiveLosses}_of_{options.MartinTriggerLosses}");
    }

    private decimal GetNextMartinStake(decimal previousStakeUsd, decimal baseStakeUsd)
    {
        var maxStake = baseStakeUsd * Pow2(options.MartinStakeLevels - 1);
        if (previousStakeUsd <= 0m || previousStakeUsd >= maxStake)
        {
            return baseStakeUsd;
        }

        var nextStake = previousStakeUsd * 2m;
        return nextStake > maxStake ? baseStakeUsd : nextStake;
    }

    private static decimal Pow2(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= 2m;
        }

        return result;
    }

    private bool IsEntryExpired(DateTimeOffset entryDueAtUtc, DateTimeOffset nowUtc)
    {
        return entryDueAtUtc < nowUtc.AddSeconds(-options.EntryGraceSeconds);
    }

    private static BtcUpDown5mOutcomeQuote? SelectOutcome(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant)
    {
        return variant.Direction switch
        {
            BtcUpDown5mStrategyDirection.Less => BtcUpDown5mMarketAnalyzer.TrySelectLowerPricedOutcome(market),
            BtcUpDown5mStrategyDirection.More => BtcUpDown5mMarketAnalyzer.TrySelectHigherPricedOutcome(market),
            _ => null
        };
    }

    private static string GetDirectionDescription(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Direction == BtcUpDown5mStrategyDirection.Less
            ? "lower-priced"
            : "higher-priced";
    }

    private static bool IsDirectionalPriceAllowedForVariant(
        decimal price,
        BtcUpDown5mStrategyVariant variant)
    {
        return variant.Direction switch
        {
            BtcUpDown5mStrategyDirection.Less => price is > 0m and < 0.5m,
            BtcUpDown5mStrategyDirection.More => price is > 0.5m and <= 1m,
            _ => false
        };
    }

    private static bool UsesGammaOutcomeSelection(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.GammaOutcomeSelection or
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelectionEntryPriceCap;
    }

    private static bool UsesOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.MiddleReference or
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevert or
            BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults or
            BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert or
            BtcUpDown5mStrategyBehavior.AlwaysUp or
            BtcUpDown5mStrategyBehavior.AlwaysDown or
            BtcUpDown5mStrategyBehavior.BinanceStartRelative or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeClever or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeCleverMargin or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeEdge or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeDelayed or
            BtcUpDown5mStrategyBehavior.EnsembleVote or
            BtcUpDown5mStrategyBehavior.DynamicMarkov or
            BtcUpDown5mStrategyBehavior.StrategySelector or
            BtcUpDown5mStrategyBehavior.StandardEntryPriceCap or
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelectionEntryPriceCap or
            BtcUpDown5mStrategyBehavior.PreOpenFixedDirection;
    }

    private static bool UsesConvertedTakerGtdPaperOrderSettlement(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.Standard or
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelection or
            BtcUpDown5mStrategyBehavior.Less180Martin;
    }

    private static bool IsConvertedTakerGtdPaperOrder(PaperOrder? paperOrder)
    {
        return paperOrder is not null &&
            string.Equals(paperOrder.ExecutionSource, BtcGtdLimitExecutionSource, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlwaysDirectionOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.AlwaysUp or BtcUpDown5mStrategyBehavior.AlwaysDown;
    }

    private static bool IsPreOpenFixedDirectionOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirection;
    }

    private static bool IsBinanceStartRelativeOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.BinanceStartRelative or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeClever or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeCleverMargin or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeEdge or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeDelayed;
    }

    private static bool IsBinanceCleverOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.BinanceStartRelativeClever or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeCleverMargin or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeEdge;
    }

    private static bool IsFixedPriceOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return IsAlwaysDirectionOpeningLimitEntry(variant) ||
            IsPreOpenFixedDirectionOpeningLimitEntry(variant) ||
            variant.Behavior is BtcUpDown5mStrategyBehavior.BinanceStartRelative or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeDelayed;
    }

    private static decimal GetBinanceStartRelativeLimitPrice(BtcUpDown5mStrategyVariant variant)
    {
        if (variant.Behavior != BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice ||
            variant.DecisionDepth <= 0)
        {
            return BinanceStartRelativeDefaultLimitPrice;
        }

        return Math.Min(0.50m, Math.Max(0.01m, variant.DecisionDepth / 100m));
    }

    private static decimal GetFixedDirectionLimitPrice(BtcUpDown5mStrategyVariant variant)
    {
        return variant.FixedLimitPrice is > 0m
            ? Math.Min(0.99m, variant.FixedLimitPrice.Value)
            : AlwaysDirectionLimitPrice;
    }

    private static decimal? GetBinanceStartRelativeMinMoveBps(BtcUpDown5mStrategyVariant variant)
    {
        if (variant.Behavior != BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold)
        {
            return null;
        }

        if (variant.DecisionThresholdBps is > 0m)
        {
            return variant.DecisionThresholdBps;
        }

        return variant.DecisionDepth > 0 ? variant.DecisionDepth : null;
    }

    private static decimal GetBinanceCleverFairValueEdgeMargin(BtcUpDown5mStrategyVariant variant)
    {
        return (variant.Behavior is BtcUpDown5mStrategyBehavior.BinanceStartRelativeCleverMargin or
                BtcUpDown5mStrategyBehavior.BinanceStartRelativeEdge) &&
            variant.DecisionDepth > 0
            ? Math.Max(0m, variant.DecisionDepth / 100m)
            : BinanceCleverFairValueEdgeMargin;
    }

    private static bool IsOpeningLimitEntryAllowedAfterEntryGrace(
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset? marketStartUtc,
        DateTimeOffset nowUtc)
    {
        return IsAlwaysDirectionOpeningLimitEntry(variant) ||
            IsBinanceStartRelativeOpeningLimitEntry(variant) ||
            IsPreOpenEntryWindowStillOpen(variant, marketStartUtc, nowUtc);
    }

    private static bool IsPreOpenEntryWindowStillOpen(
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset? marketStartUtc,
        DateTimeOffset nowUtc)
    {
        return IsPreOpenFixedDirectionOpeningLimitEntry(variant) &&
            marketStartUtc is { } startUtc &&
            nowUtc < startUtc;
    }

    private static bool IsPreOpenEntryWindowElapsed(
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset? marketStartUtc,
        DateTimeOffset nowUtc)
    {
        return IsPreOpenFixedDirectionOpeningLimitEntry(variant) &&
            marketStartUtc is { } startUtc &&
            nowUtc >= startUtc;
    }

    private static bool ShouldRunPaperLiveShadowTest(
        BtcUpDown5mStrategyVariant variant,
        StrategyRuntimeSettings settings)
    {
        return settings.LiveStakes &&
            string.Equals(variant.Code, BtcSkip1VariantCode, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanSubmitLegacyBtcLiveOrder(BtcUpDown5mStrategyVariant variant)
    {
        return false;
    }

    private async Task<BtcOpeningLimitDecision> GetOpeningLimitEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        IDictionary<string, BtcCurrentPriceLookupResult> middleReferenceCurrentPrices,
        CancellationToken cancellationToken)
    {
        return variant.Behavior switch
        {
            BtcUpDown5mStrategyBehavior.MiddleReference or
                BtcUpDown5mStrategyBehavior.MiddleReferenceRevert => await GetMiddleReferenceEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                middleReferenceCurrentPrices,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults or
                BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert => await GetSkipConsecutiveMarketResultsEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.AlwaysUp or
                BtcUpDown5mStrategyBehavior.AlwaysDown => GetAlwaysDirectionEntryDecision(
                market,
                variant,
                stakeUsd,
                nowUtc),
            BtcUpDown5mStrategyBehavior.PreOpenFixedDirection => GetPreOpenFixedDirectionEntryDecision(
                market,
                variant,
                stakeUsd,
                nowUtc),
            BtcUpDown5mStrategyBehavior.BinanceStartRelative or
                BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice or
                BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold or
                BtcUpDown5mStrategyBehavior.BinanceStartRelativeDelayed => await GetBinanceStartRelativeEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                middleReferenceCurrentPrices,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeClever or
                BtcUpDown5mStrategyBehavior.BinanceStartRelativeCleverMargin or
                BtcUpDown5mStrategyBehavior.BinanceStartRelativeEdge => await GetBinanceCleverEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                middleReferenceCurrentPrices,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.EnsembleVote => await GetEnsembleVoteEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                middleReferenceCurrentPrices,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.DynamicMarkov => await GetDynamicMarkovEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.StrategySelector => await GetStrategySelectorEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                middleReferenceCurrentPrices,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.StandardEntryPriceCap => await GetStandardEntryPriceCapOpeningLimitEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelectionEntryPriceCap => GetGammaEntryPriceCapOpeningLimitEntryDecision(
                market,
                variant,
                stakeUsd,
                nowUtc),
            _ => BtcOpeningLimitDecision.Reject("unsupported_opening_limit_strategy")
        };
    }

    private async Task<BtcOpeningLimitPriceDecision> GetOpeningLimitPriceAsync(
        BtcUpDown5mStrategyVariant variant,
        string assetId,
        string rawDecisionJson,
        decimal? limitPriceOverride,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (limitPriceOverride is { } overriddenLimitPrice)
        {
            if (IsStrategyEntryPriceCapVariant(variant))
            {
                var capLimitPrice = RoundDownToTick(Math.Min(1m, overriddenLimitPrice), options.OpeningLimitPriceTickSize);
                if (capLimitPrice <= 0m)
                {
                    return BtcOpeningLimitPriceDecision.Reject(
                        "strategy_entry_price_cap_non_positive",
                        AttachEntryPriceCapOpeningLimitPricingJson(
                            rawDecisionJson,
                            overriddenLimitPrice,
                            options.OpeningLimitPriceTickSize,
                            LimitPrice: null,
                            RejectionReason: "strategy_entry_price_cap_non_positive"));
                }

                return BtcOpeningLimitPriceDecision.Enter(
                    capLimitPrice,
                    AttachEntryPriceCapOpeningLimitPricingJson(
                        rawDecisionJson,
                        overriddenLimitPrice,
                        options.OpeningLimitPriceTickSize,
                        capLimitPrice,
                        RejectionReason: null));
            }

            var overrideMaxPrice = Math.Min(options.OpeningLimitMaxPrice, 0.50m);
            var overrideCappedLimitPrice = Math.Min(overrideMaxPrice, overriddenLimitPrice);
            var overrideLimitPrice = RoundDownToTick(overrideCappedLimitPrice, options.OpeningLimitPriceTickSize);
            if (overrideLimitPrice <= 0m)
            {
                return BtcOpeningLimitPriceDecision.Reject(
                    "opening_limit_price_override_non_positive",
                    AttachCleverOpeningLimitPricingJson(
                        rawDecisionJson,
                        overriddenLimitPrice,
                        overrideMaxPrice,
                        options.OpeningLimitPriceTickSize,
                        LimitPrice: null,
                        RejectionReason: "opening_limit_price_override_non_positive"));
            }

            return BtcOpeningLimitPriceDecision.Enter(
                overrideLimitPrice,
                AttachCleverOpeningLimitPricingJson(
                    rawDecisionJson,
                    overriddenLimitPrice,
                    overrideMaxPrice,
                    options.OpeningLimitPriceTickSize,
                    overrideLimitPrice,
                    RejectionReason: null));
        }

        if (IsFixedPriceOpeningLimitEntry(variant))
        {
            var fixedLimitPrice = variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice
                ? RoundDownToTick(
                    Math.Min(Math.Min(options.OpeningLimitMaxPrice, 0.50m), GetBinanceStartRelativeLimitPrice(variant)),
                    options.OpeningLimitPriceTickSize)
                : IsAlwaysDirectionOpeningLimitEntry(variant) || IsPreOpenFixedDirectionOpeningLimitEntry(variant)
                    ? RoundDownToTick(
                        Math.Min(Math.Min(options.OpeningLimitMaxPrice, 0.50m), GetFixedDirectionLimitPrice(variant)),
                        options.OpeningLimitPriceTickSize)
                    : RoundDownToTick(Math.Min(options.OpeningLimitMaxPrice, 0.50m), options.OpeningLimitPriceTickSize);
            if (fixedLimitPrice <= 0m)
            {
                return BtcOpeningLimitPriceDecision.Reject(
                    "opening_limit_price_non_positive",
                    AttachFixedOpeningLimitPricingJson(rawDecisionJson, fixedLimitPrice));
            }

            return BtcOpeningLimitPriceDecision.Enter(
                fixedLimitPrice,
                AttachFixedOpeningLimitPricingJson(rawDecisionJson, fixedLimitPrice));
        }

        var maxPrice = Math.Min(options.OpeningLimitMaxPrice, 0.50m);
        if (!options.OpeningLimitDynamicBreakEvenPricingEnabled)
        {
            var fixedLimitPrice = RoundDownToTick(maxPrice, options.OpeningLimitPriceTickSize);
            return fixedLimitPrice > 0m
                ? BtcOpeningLimitPriceDecision.Enter(
                    fixedLimitPrice,
                    AttachOpeningLimitBreakEvenPricingJson(
                        rawDecisionJson,
                        "fixed_max",
                        options.OpeningLimitBreakEvenLookbackRuns,
                        options.OpeningLimitBreakEvenMinSettledRuns,
                        SettledRuns: 0,
                        Wins: 0,
                        WinRate: null,
                        options.OpeningLimitBreakEvenMargin,
                        RawLimitPrice: maxPrice,
                        MaxLimitPrice: maxPrice,
                        options.OpeningLimitPriceTickSize,
                        fixedLimitPrice,
                        RejectionReason: null))
                : BtcOpeningLimitPriceDecision.Reject(
                    "opening_limit_price_non_positive",
                    AttachOpeningLimitBreakEvenPricingJson(
                        rawDecisionJson,
                        "fixed_max",
                        options.OpeningLimitBreakEvenLookbackRuns,
                        options.OpeningLimitBreakEvenMinSettledRuns,
                        SettledRuns: 0,
                        Wins: 0,
                        WinRate: null,
                        options.OpeningLimitBreakEvenMargin,
                        RawLimitPrice: maxPrice,
                        MaxLimitPrice: maxPrice,
                        options.OpeningLimitPriceTickSize,
                        LimitPrice: null,
                        RejectionReason: "opening_limit_price_non_positive"));
        }

        var lookbackRuns = Math.Max(1, options.OpeningLimitBreakEvenLookbackRuns);
        var minSettledRuns = Math.Max(1, options.OpeningLimitBreakEvenMinSettledRuns);
        var recentRuns = await repository.GetRecentStrategyMarketPaperRunsAsync(
            variant.Id,
            StrategyMarketPaperRunStatuses.Settled,
            lookbackRuns,
            cancellationToken);
        var settledRuns = recentRuns
            .Where(run => run.RealizedPnlUsd is not null)
            .ToArray();
        var sampleMode = "dynamic_break_even";
        var invertSamplePnl = false;
        if (settledRuns.Length < minSettledRuns &&
            TryGetBaseOpeningLimitVariantForRevert(variant) is { } baseVariant)
        {
            var baseRecentRuns = await repository.GetRecentStrategyMarketPaperRunsAsync(
                baseVariant.Id,
                StrategyMarketPaperRunStatuses.Settled,
                lookbackRuns,
                cancellationToken);
            var baseSettledRuns = baseRecentRuns
                .Where(run => run.RealizedPnlUsd is not null)
                .ToArray();
            if (baseSettledRuns.Length >= minSettledRuns)
            {
                settledRuns = baseSettledRuns;
                sampleMode = IsMiddleReferenceRevert(variant)
                    ? "dynamic_break_even_revert_bootstrap_from_base_middle"
                    : "dynamic_break_even_revert_bootstrap_from_base_skip";
                invertSamplePnl = true;
            }
        }

        var wins = invertSamplePnl
            ? settledRuns.Count(run => run.RealizedPnlUsd < 0m)
            : settledRuns.Count(run => run.RealizedPnlUsd > 0m);
        var winRate = settledRuns.Length == 0
            ? (decimal?)null
            : wins / (decimal)settledRuns.Length;

        if (settledRuns.Length < minSettledRuns)
        {
            var bootstrapPricing = await GetOpeningLimitBookBootstrapPriceAsync(
                assetId,
                nowUtc,
                cancellationToken);
            var bootstrapRawDecisionJson = AttachOpeningLimitBreakEvenPricingJson(
                rawDecisionJson,
                bootstrapPricing.Available
                    ? "dynamic_break_even_book_bootstrap"
                    : "dynamic_break_even_book_bootstrap_rejected",
                lookbackRuns,
                minSettledRuns,
                settledRuns.Length,
                wins,
                winRate,
                options.OpeningLimitBreakEvenMargin,
                RawLimitPrice: bootstrapPricing.RawLimitPrice,
                MaxLimitPrice: maxPrice,
                bootstrapPricing.TickSize ?? options.OpeningLimitPriceTickSize,
                LimitPrice: bootstrapPricing.Available ? bootstrapPricing.LimitPrice : null,
                RejectionReason: bootstrapPricing.Available ? null : bootstrapPricing.RejectionReason,
                BreakEvenInsufficientReason: "opening_limit_break_even_sample_insufficient",
                BookBootstrapPricing: bootstrapPricing);
            return bootstrapPricing.Available
                ? BtcOpeningLimitPriceDecision.Enter(
                    bootstrapPricing.LimitPrice,
                    bootstrapRawDecisionJson)
                : BtcOpeningLimitPriceDecision.Reject(
                    bootstrapPricing.RejectionReason ?? "opening_limit_book_bootstrap_rejected",
                    bootstrapRawDecisionJson);
        }

        var rawLimitPrice = winRate.GetValueOrDefault() - options.OpeningLimitBreakEvenMargin;
        var cappedLimitPrice = Math.Min(maxPrice, rawLimitPrice);
        var limitPrice = RoundDownToTick(cappedLimitPrice, options.OpeningLimitPriceTickSize);
        if (limitPrice <= 0m)
        {
            return BtcOpeningLimitPriceDecision.Reject(
                "opening_limit_break_even_price_non_positive",
                AttachOpeningLimitBreakEvenPricingJson(
                    rawDecisionJson,
                    sampleMode,
                    lookbackRuns,
                    minSettledRuns,
                    settledRuns.Length,
                    wins,
                    winRate,
                    options.OpeningLimitBreakEvenMargin,
                    rawLimitPrice,
                    maxPrice,
                    options.OpeningLimitPriceTickSize,
                    LimitPrice: null,
                    RejectionReason: "opening_limit_break_even_price_non_positive"));
        }

        return BtcOpeningLimitPriceDecision.Enter(
            limitPrice,
            AttachOpeningLimitBreakEvenPricingJson(
                rawDecisionJson,
                sampleMode,
                lookbackRuns,
                minSettledRuns,
                settledRuns.Length,
                wins,
                winRate,
                options.OpeningLimitBreakEvenMargin,
                rawLimitPrice,
                maxPrice,
                options.OpeningLimitPriceTickSize,
                limitPrice,
                RejectionReason: null));
    }

    private async Task<BtcOpeningLimitDecision> GetStandardEntryPriceCapOpeningLimitEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (TryGetStandardEntryPriceCap(variant) is not { } entryPriceCap)
        {
            return BtcOpeningLimitDecision.Reject("strategy_entry_price_cap_missing");
        }

        var outcomeSelection = await GetTakerPaperOutcomeSelectionAsync(
            market,
            variant,
            stakeUsd,
            nowUtc,
            cancellationToken,
            enforceSelectedEntryPriceCap: false);
        if (!outcomeSelection.Filled ||
            outcomeSelection.SelectedOutcome is null ||
            outcomeSelection.EntryPricing is null)
        {
            return BtcOpeningLimitDecision.Reject(
                outcomeSelection.RejectionReason ?? "paper_gtd_cap_outcome_selection_rejected",
                outcomeSelection.SkipDiagnosticsJson,
                entryPriceCap);
        }

        return BtcOpeningLimitDecision.Enter(
            outcomeSelection.SelectedOutcome,
            outcomeSelection.EntryPricing.RawDecisionJson,
            entryPriceCap);
    }

    private static BtcOpeningLimitDecision GetGammaEntryPriceCapOpeningLimitEntryDecision(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc)
    {
        if (TryGetStandardEntryPriceCap(variant) is not { } entryPriceCap)
        {
            return BtcOpeningLimitDecision.Reject("strategy_entry_price_cap_missing");
        }

        var selectedOutcome = SelectOutcome(market, variant);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildGammaEntryPriceCapOpeningLimitRawDecisionJson(
                    market,
                    selectedOutcome: null,
                    variant,
                    stakeUsd,
                    nowUtc,
                    reason: "target_outcome_not_available"),
                entryPriceCap);
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildGammaEntryPriceCapOpeningLimitRawDecisionJson(
                market,
                selectedOutcome,
                variant,
                stakeUsd,
                nowUtc,
                reason: null),
            entryPriceCap);
    }

    private static BtcOpeningLimitDecision GetAlwaysDirectionEntryDecision(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc)
    {
        var selectedDirection = variant.Behavior == BtcUpDown5mStrategyBehavior.AlwaysUp
            ? BtcPriceDirection.Up
            : BtcPriceDirection.Down;
        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildAlwaysDirectionRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    selectedDirection,
                    selectedOutcome: null,
                    reason: "target_outcome_not_available"));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildAlwaysDirectionRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                selectedDirection,
            selectedOutcome,
            reason: null));
    }

    private static BtcOpeningLimitDecision GetPreOpenFixedDirectionEntryDecision(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc)
    {
        var selectedDirection = variant.FixedOutcome switch
        {
            BtcUpDownFixedOutcome.Up => BtcPriceDirection.Up,
            BtcUpDownFixedOutcome.Down => BtcPriceDirection.Down,
            _ => (BtcPriceDirection?)null
        };
        if (selectedDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "fixed_outcome_not_configured",
                BuildAlwaysDirectionRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    BtcPriceDirection.Up,
                    selectedOutcome: null,
                    reason: "fixed_outcome_not_configured"));
        }

        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection.Value);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildAlwaysDirectionRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    selectedDirection.Value,
                    selectedOutcome: null,
                    reason: "target_outcome_not_available"));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildAlwaysDirectionRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                selectedDirection.Value,
                selectedOutcome,
                reason: null));
    }

    private async Task<BtcOpeningLimitDecision> GetBinanceStartRelativeEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        IDictionary<string, BtcCurrentPriceLookupResult> currentPrices,
        CancellationToken cancellationToken)
    {
        var startPrice = await repository.GetBtcUpDown5mOddsStartPriceAsync(market.MarketId, cancellationToken);
        if (startPrice is not > 0m)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_market_start_price_missing",
                BuildBinanceStartRelativeRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice: null,
                    startPrice,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "btc_market_start_price_missing"));
        }

        var currentPriceLookup = await GetBtcStartRelativeCurrentPriceAsync(market, currentPrices, cancellationToken);
        if (currentPriceLookup.Price is not { } currentPrice)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_reference_fetch_failed",
                BuildBinanceStartRelativeRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice: null,
                    startPrice,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "btc_reference_fetch_failed"));
        }

        var baseSelectedDirection = ResolveStartRelativeDirection(currentPrice.PriceUsd, startPrice.Value);
        if (baseSelectedDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_reference_equal_market_start",
                BuildBinanceStartRelativeRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice,
                    startPrice,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "btc_reference_equal_market_start"));
        }

        var selectedDirection = baseSelectedDirection.Value;
        if (GetBinanceStartRelativeMinMoveBps(variant) is { } minMoveBps)
        {
            var moveBps = Math.Abs((currentPrice.PriceUsd - startPrice.Value) / startPrice.Value * 10_000m);
            if (moveBps < minMoveBps)
            {
                return BtcOpeningLimitDecision.Reject(
                    "btc_reference_move_below_bps_threshold",
                    BuildBinanceStartRelativeRawDecisionJson(
                        market,
                        variant,
                        stakeUsd,
                        nowUtc,
                        currentPrice,
                        startPrice,
                        baseSelectedDirection,
                        selectedDirection: null,
                        selectedOutcome: null,
                        reason: "btc_reference_move_below_bps_threshold"));
            }
        }

        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildBinanceStartRelativeRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice,
                    startPrice,
                    baseSelectedDirection,
                    selectedDirection,
                    selectedOutcome: null,
                    reason: "target_outcome_not_available"));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildBinanceStartRelativeRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                currentPrice,
                startPrice,
                baseSelectedDirection,
                selectedDirection,
                selectedOutcome,
                reason: null));
    }

    private async Task<BtcOpeningLimitDecision> GetBinanceCleverEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        IDictionary<string, BtcCurrentPriceLookupResult> currentPrices,
        CancellationToken cancellationToken)
    {
        var edgeMargin = GetBinanceCleverFairValueEdgeMargin(variant);
        var startPrice = await repository.GetBtcUpDown5mOddsStartPriceAsync(market.MarketId, cancellationToken);
        if (startPrice is not > 0m)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_market_start_price_missing",
                BuildBinanceCleverRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice: null,
                    startPrice,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    estimate: null,
                    edgeMargin: edgeMargin,
                    reason: "btc_market_start_price_missing"));
        }

        var currentPriceLookup = await GetBtcStartRelativeCurrentPriceAsync(market, currentPrices, cancellationToken);
        if (currentPriceLookup.Price is not { } currentPrice)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_reference_fetch_failed",
                BuildBinanceCleverRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice: null,
                    startPrice,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    estimate: null,
                    edgeMargin: edgeMargin,
                    reason: "btc_reference_fetch_failed"));
        }

        var baseSelectedDirection = ResolveStartRelativeDirection(currentPrice.PriceUsd, startPrice.Value);
        if (baseSelectedDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_reference_equal_market_start",
                BuildBinanceCleverRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice,
                    startPrice,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    estimate: null,
                    edgeMargin: edgeMargin,
                    reason: "btc_reference_equal_market_start"));
        }

        var selectedDirection = baseSelectedDirection.Value;
        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildBinanceCleverRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice,
                    startPrice,
                    baseSelectedDirection,
                    selectedDirection,
                    selectedOutcome: null,
                    estimate: null,
                    edgeMargin: edgeMargin,
                    reason: "target_outcome_not_available"));
        }

        var recentTicks = await repository.GetRecentBtcUpDown5mOddsTicksAsync(
            BinanceCleverFairValueLookbackTicks,
            cancellationToken);
        var estimate = EstimateBinanceCleverFairValue(
            recentTicks,
            market,
            selectedDirection,
            currentPrice.PriceUsd,
            startPrice.Value,
            nowUtc,
            edgeMargin);
        if (!estimate.ShouldEnter || estimate.RawLimitPrice is not { } rawLimitPrice)
        {
            return BtcOpeningLimitDecision.Reject(
                estimate.RejectionReason ?? "btc_clever_fair_value_rejected",
                BuildBinanceCleverRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice,
                    startPrice,
                    baseSelectedDirection,
                    selectedDirection,
                    selectedOutcome,
                    estimate,
                    edgeMargin,
                    estimate.RejectionReason ?? "btc_clever_fair_value_rejected"),
                estimate.RawLimitPrice);
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildBinanceCleverRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                currentPrice,
                startPrice,
                baseSelectedDirection,
                selectedDirection,
                selectedOutcome,
                estimate,
                edgeMargin,
                reason: null),
            rawLimitPrice);
    }

    private BtcCleverFairValueEstimate EstimateBinanceCleverFairValue(
        IReadOnlyList<BtcUpDown5mOddsTick> recentTicks,
        PolymarketGammaMarket market,
        BtcPriceDirection selectedDirection,
        decimal currentPriceUsd,
        decimal startPriceUsd,
        DateTimeOffset nowUtc,
        decimal edgeMargin)
    {
        var currentTick = recentTicks
            .Where(tick => string.Equals(tick.MarketId, market.MarketId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(tick => tick.SampledAtUtc)
            .FirstOrDefault();
        if (currentTick is null)
        {
            return BtcCleverFairValueEstimate.Reject("btc_clever_current_odds_missing");
        }

        var currentTargetPrice = GetTargetPriceProxy(currentTick, selectedDirection);
        var currentTargetProxyKind = GetTargetPriceProxyKind(currentTick, selectedDirection);
        var currentTargetSpread = GetTargetSpread(currentTick, selectedDirection);
        var currentTargetBookSource = GetTargetBookSource(currentTick, selectedDirection);
        var currentTargetBookAgeMs = GetTargetBookAgeMs(currentTick, selectedDirection);
        if (currentTargetPrice is null || currentTargetPrice <= 0m || currentTargetPrice >= 1m)
        {
            return BtcCleverFairValueEstimate.Reject(
                "btc_clever_current_odds_missing",
                CurrentTargetPrice: currentTargetPrice,
                CurrentTargetPriceProxyKind: currentTargetProxyKind,
                CurrentTargetSpread: currentTargetSpread,
                CurrentTargetBookSource: currentTargetBookSource,
                CurrentTargetBookAgeMs: currentTargetBookAgeMs);
        }

        if (currentTargetSpread is { } spread && spread > options.PaperTakerMaxSpreadAbs)
        {
            return BtcCleverFairValueEstimate.Reject(
                "btc_clever_current_spread_too_wide",
                CurrentTargetPrice: currentTargetPrice,
                CurrentTargetPriceProxyKind: currentTargetProxyKind,
                CurrentTargetSpread: currentTargetSpread,
                CurrentTargetBookSource: currentTargetBookSource,
                CurrentTargetBookAgeMs: currentTargetBookAgeMs);
        }

        var currentMoveUsd = currentPriceUsd - startPriceUsd;
        var currentMoveBps = startPriceUsd == 0m ? 0m : currentMoveUsd / startPriceUsd * 10_000m;
        var currentAlignedMoveBps = AlignMoveBps(currentMoveBps, selectedDirection);
        var marketEndUtc = market.EndDateUtc ??
            BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market)?.AddMinutes(5) ??
            nowUtc.AddMinutes(5);
        var currentSecondsToClose = Math.Max(0m, ToDecimalSeconds(marketEndUtc - nowUtc));

        List<BtcCleverFairValueCandidate> candidates = [];
        foreach (var tick in recentTicks)
        {
            if (string.Equals(tick.MarketId, market.MarketId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var price = GetTargetPriceProxy(tick, selectedDirection);
            if (price is null || price <= 0m || price >= 1m)
            {
                continue;
            }

            var alignedMoveBps = AlignMoveBps(tick.BtcMoveFromStartBps, selectedDirection);
            var moveDistance = Math.Abs(alignedMoveBps - currentAlignedMoveBps) / BinanceCleverMoveScaleBps;
            var timeDistance = Math.Abs(tick.SecondsToClose - currentSecondsToClose) / BinanceCleverTimeScaleSeconds;
            var proxyPenalty = string.Equals(GetTargetPriceProxyKind(tick, selectedDirection), "mid", StringComparison.OrdinalIgnoreCase)
                ? 0m
                : 0.75m;
            var spreadPenalty = GetTargetSpread(tick, selectedDirection) is { } candidateSpread
                ? Math.Min(2m, candidateSpread / Math.Max(options.PaperTakerMaxSpreadAbs, 0.01m)) * 0.25m
                : 0.75m;
            var sourcePenalty = string.Equals(GetTargetBookSource(tick, selectedDirection), WebSocketCacheSource, StringComparison.OrdinalIgnoreCase)
                ? 0m
                : 0.20m;
            var distance = 1m + moveDistance + timeDistance + proxyPenalty + spreadPenalty + sourcePenalty;
            var weight = 1m / (distance * distance);
            candidates.Add(new BtcCleverFairValueCandidate(
                price.Value,
                weight,
                distance,
                alignedMoveBps,
                tick.SecondsToClose));
        }

        if (candidates.Count < BinanceCleverFairValueMinSamples)
        {
            return BtcCleverFairValueEstimate.Reject(
                "btc_clever_fair_value_sample_insufficient",
                CandidateSamples: candidates.Count,
                CurrentTargetPrice: currentTargetPrice,
                CurrentTargetPriceProxyKind: currentTargetProxyKind,
                CurrentTargetSpread: currentTargetSpread,
                CurrentTargetBookSource: currentTargetBookSource,
                CurrentTargetBookAgeMs: currentTargetBookAgeMs,
                CurrentAlignedMoveBps: currentAlignedMoveBps,
                CurrentSecondsToClose: currentSecondsToClose);
        }

        var weightSum = candidates.Sum(candidate => candidate.Weight);
        var fairValue = candidates.Sum(candidate => candidate.Price * candidate.Weight) / weightSum;
        var averageDistance = candidates.Average(candidate => candidate.Distance);
        var currentLiquidityDiscount = GetBinanceCleverCurrentLiquidityDiscount(
            currentTargetProxyKind,
            currentTargetSpread,
            currentTargetBookSource);
        var adjustedFairValue = Math.Max(0m, fairValue - currentLiquidityDiscount);
        var rawLimitPrice = adjustedFairValue - edgeMargin;
        var maxLimitPrice = Math.Min(options.OpeningLimitMaxPrice, 0.50m);
        var finalLimitPrice = RoundDownToTick(
            Math.Min(maxLimitPrice, rawLimitPrice),
            options.OpeningLimitPriceTickSize);
        if (finalLimitPrice <= 0m)
        {
            return BtcCleverFairValueEstimate.Reject(
                "btc_clever_fair_value_below_margin",
                CandidateSamples: candidates.Count,
                WeightSum: weightSum,
                FairValuePrice: fairValue,
                AdjustedFairValuePrice: adjustedFairValue,
                RawLimitPrice: rawLimitPrice,
                LimitPrice: null,
                CurrentTargetPrice: currentTargetPrice,
                CurrentTargetPriceProxyKind: currentTargetProxyKind,
                CurrentTargetSpread: currentTargetSpread,
                CurrentTargetBookSource: currentTargetBookSource,
                CurrentTargetBookAgeMs: currentTargetBookAgeMs,
                CurrentLiquidityDiscount: currentLiquidityDiscount,
                AverageDistance: averageDistance,
                CurrentAlignedMoveBps: currentAlignedMoveBps,
                CurrentSecondsToClose: currentSecondsToClose);
        }

        return BtcCleverFairValueEstimate.Enter(
            candidates.Count,
            weightSum,
            fairValue,
            adjustedFairValue,
            rawLimitPrice,
            finalLimitPrice,
            currentTargetPrice.Value,
            currentTargetProxyKind,
            currentTargetSpread,
            currentTargetBookSource,
            currentTargetBookAgeMs,
            currentLiquidityDiscount,
            averageDistance,
            currentAlignedMoveBps,
            currentSecondsToClose);
    }

    private static decimal AlignMoveBps(decimal moveBps, BtcPriceDirection selectedDirection)
    {
        return selectedDirection == BtcPriceDirection.Up ? moveBps : -moveBps;
    }

    private static decimal ToDecimalSeconds(TimeSpan value)
    {
        return (decimal)value.TotalMilliseconds / 1_000m;
    }

    private static decimal? GetTargetPriceProxy(BtcUpDown5mOddsTick tick, BtcPriceDirection selectedDirection)
    {
        return selectedDirection == BtcPriceDirection.Up ? tick.UpPriceProxy : tick.DownPriceProxy;
    }

    private static string GetTargetPriceProxyKind(BtcUpDown5mOddsTick tick, BtcPriceDirection selectedDirection)
    {
        return selectedDirection == BtcPriceDirection.Up ? tick.UpPriceProxyKind : tick.DownPriceProxyKind;
    }

    private static decimal? GetTargetSpread(BtcUpDown5mOddsTick tick, BtcPriceDirection selectedDirection)
    {
        var bestBid = selectedDirection == BtcPriceDirection.Up ? tick.UpBestBid : tick.DownBestBid;
        var bestAsk = selectedDirection == BtcPriceDirection.Up ? tick.UpBestAsk : tick.DownBestAsk;
        return bestBid is { } bid && bestAsk is { } ask ? ask - bid : null;
    }

    private static string GetTargetBookSource(BtcUpDown5mOddsTick tick, BtcPriceDirection selectedDirection)
    {
        return selectedDirection == BtcPriceDirection.Up ? tick.UpBookSource : tick.DownBookSource;
    }

    private static decimal? GetTargetBookAgeMs(BtcUpDown5mOddsTick tick, BtcPriceDirection selectedDirection)
    {
        return selectedDirection == BtcPriceDirection.Up ? tick.UpBookAgeMs : tick.DownBookAgeMs;
    }

    private static decimal GetBinanceCleverCurrentLiquidityDiscount(
        string proxyKind,
        decimal? spread,
        string bookSource)
    {
        var discount = 0m;
        if (!string.Equals(proxyKind, "mid", StringComparison.OrdinalIgnoreCase))
        {
            discount += BinanceCleverOneSidedBookDiscount;
        }

        if (spread is { } spreadValue)
        {
            discount += Math.Min(BinanceCleverOneSidedBookDiscount, spreadValue / BinanceCleverSpreadDiscountDivisor);
        }
        else
        {
            discount += BinanceCleverOneSidedBookDiscount;
        }

        if (!string.Equals(bookSource, WebSocketCacheSource, StringComparison.OrdinalIgnoreCase))
        {
            discount += BinanceCleverRestBookDiscount;
        }

        return discount;
    }

    private async Task<BtcOpeningLimitDecision> GetEnsembleVoteEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        IDictionary<string, BtcCurrentPriceLookupResult> currentPrices,
        CancellationToken cancellationToken)
    {
        var requiredVotes = Math.Max(2, variant.DecisionDepth);
        var candidates = GetEnsembleVoteCandidateVariants();
        var votes = new List<BtcOpeningLimitSignalVote>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var decision = await GetOpeningLimitEntryDecisionAsync(
                market,
                candidate,
                stakeUsd,
                nowUtc,
                currentPrices,
                cancellationToken);
            var direction = decision.ShouldEnter && decision.SelectedOutcome is not null
                ? TryResolveDirectionFromOutcome(decision.SelectedOutcome.Outcome)
                : null;
            votes.Add(new BtcOpeningLimitSignalVote(
                candidate.Code,
                decision.ShouldEnter,
                decision.SkipReason,
                direction,
                decision.SelectedOutcome?.Outcome,
                decision.SelectedOutcome?.AssetId,
                decision.LimitPriceOverride));
        }

        var upVotes = votes.Count(vote => vote.Direction == BtcPriceDirection.Up);
        var downVotes = votes.Count(vote => vote.Direction == BtcPriceDirection.Down);
        BtcPriceDirection? selectedDirection = null;
        if (upVotes >= requiredVotes && upVotes > downVotes)
        {
            selectedDirection = BtcPriceDirection.Up;
        }
        else if (downVotes >= requiredVotes && downVotes > upVotes)
        {
            selectedDirection = BtcPriceDirection.Down;
        }

        if (selectedDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "ensemble_vote_no_majority",
                BuildEnsembleVoteRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    requiredVotes,
                    votes,
                    upVotes,
                    downVotes,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "ensemble_vote_no_majority"));
        }

        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection.Value);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildEnsembleVoteRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    requiredVotes,
                    votes,
                    upVotes,
                    downVotes,
                    selectedDirection,
                    selectedOutcome: null,
                    reason: "target_outcome_not_available"));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildEnsembleVoteRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                requiredVotes,
                votes,
                upVotes,
                downVotes,
                selectedDirection,
                selectedOutcome,
                reason: null));
    }

    private async Task<BtcOpeningLimitDecision> GetDynamicMarkovEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var lookback = Math.Max(20, variant.DecisionDepth);
        var minTransitions = Math.Min(10, Math.Max(5, lookback / 5));
        var threshold = 0.55m;
        var recentResults = await repository.GetRecentBtcUpDown5mMarketResultsAsync(
            lookback + 1,
            cancellationToken);
        var orderedResults = recentResults
            .Where(result => string.Equals(result.WinningOutcome, "Up", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.WinningOutcome, "Down", StringComparison.OrdinalIgnoreCase))
            .OrderBy(result => result.MarketStartUtc ?? result.MarketEndUtc ?? result.SettledAtUtc)
            .ToArray();
        if (orderedResults.Length < minTransitions + 1)
        {
            return BtcOpeningLimitDecision.Reject(
                "markov_result_sample_insufficient",
                BuildDynamicMarkovRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    lookback,
                    minTransitions,
                    threshold,
                    orderedResults,
                    previousOutcome: null,
                    matchingTransitions: 0,
                    upProbability: null,
                    downProbability: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "markov_result_sample_insufficient"));
        }

        var previousOutcome = orderedResults[^1].WinningOutcome;
        var matchingTransitions = new List<string>();
        for (var index = 0; index < orderedResults.Length - 1; index++)
        {
            if (string.Equals(orderedResults[index].WinningOutcome, previousOutcome, StringComparison.OrdinalIgnoreCase))
            {
                matchingTransitions.Add(orderedResults[index + 1].WinningOutcome);
            }
        }

        if (matchingTransitions.Count < minTransitions)
        {
            return BtcOpeningLimitDecision.Reject(
                "markov_transition_sample_insufficient",
                BuildDynamicMarkovRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    lookback,
                    minTransitions,
                    threshold,
                    orderedResults,
                    previousOutcome,
                    matchingTransitions.Count,
                    upProbability: null,
                    downProbability: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "markov_transition_sample_insufficient"));
        }

        var upProbability = matchingTransitions.Count(outcome => string.Equals(outcome, "Up", StringComparison.OrdinalIgnoreCase)) /
            (decimal)matchingTransitions.Count;
        var downProbability = 1m - upProbability;
        var selectedDirection = upProbability >= threshold && upProbability >= downProbability
            ? BtcPriceDirection.Up
            : downProbability >= threshold
                ? BtcPriceDirection.Down
                : (BtcPriceDirection?)null;
        if (selectedDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "markov_edge_below_threshold",
                BuildDynamicMarkovRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    lookback,
                    minTransitions,
                    threshold,
                    orderedResults,
                    previousOutcome,
                    matchingTransitions.Count,
                    upProbability,
                    downProbability,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "markov_edge_below_threshold"));
        }

        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection.Value);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildDynamicMarkovRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    lookback,
                    minTransitions,
                    threshold,
                    orderedResults,
                    previousOutcome,
                    matchingTransitions.Count,
                    upProbability,
                    downProbability,
                    selectedDirection,
                    selectedOutcome: null,
                    reason: "target_outcome_not_available"));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildDynamicMarkovRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                lookback,
                minTransitions,
                threshold,
                orderedResults,
                previousOutcome,
                matchingTransitions.Count,
                upProbability,
                downProbability,
                selectedDirection,
                selectedOutcome,
                reason: null));
    }

    private async Task<BtcOpeningLimitDecision> GetStrategySelectorEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        IDictionary<string, BtcCurrentPriceLookupResult> currentPrices,
        CancellationToken cancellationToken)
    {
        var lookback = Math.Max(10, variant.DecisionDepth);
        var minSamples = Math.Min(10, Math.Max(5, lookback / 3));
        var candidates = new List<BtcStrategySelectorCandidateStats>();
        foreach (var candidate in GetStrategySelectorCandidateVariants())
        {
            var recentRuns = await repository.GetRecentStrategyMarketPaperRunsAsync(
                candidate.Id,
                StrategyMarketPaperRunStatuses.Settled,
                lookback,
                cancellationToken);
            var settledRuns = recentRuns
                .Where(run => run.RealizedPnlUsd is not null && run.StakeUsd > 0m)
                .ToArray();
            var realizedPnl = settledRuns.Sum(run => run.RealizedPnlUsd.GetValueOrDefault());
            var stakeUsdSum = settledRuns.Sum(run => run.StakeUsd);
            candidates.Add(new BtcStrategySelectorCandidateStats(
                candidate,
                settledRuns.Length,
                settledRuns.Count(run => run.RealizedPnlUsd > 0m),
                realizedPnl,
                stakeUsdSum > 0m ? realizedPnl / stakeUsdSum : null));
        }

        var ranked = candidates
            .Where(candidate => candidate.SettledRuns >= minSamples && candidate.RealizedPnlUsd > 0m)
            .OrderByDescending(candidate => candidate.AveragePnlUsd)
            .ThenByDescending(candidate => candidate.Roi)
            .ToArray();
        if (ranked.Length == 0)
        {
            return BtcOpeningLimitDecision.Reject(
                "strategy_selector_no_positive_candidate",
                BuildStrategySelectorRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    lookback,
                    minSamples,
                    candidates,
                    selectedCandidate: null,
                    candidateDecision: null,
                    selectedOutcome: null,
                    reason: "strategy_selector_no_positive_candidate"));
        }

        foreach (var candidate in ranked)
        {
            var candidateDecision = await GetOpeningLimitEntryDecisionAsync(
                market,
                candidate.Variant,
                stakeUsd,
                nowUtc,
                currentPrices,
                cancellationToken);
            if (!candidateDecision.ShouldEnter || candidateDecision.SelectedOutcome is null)
            {
                continue;
            }

            return BtcOpeningLimitDecision.Enter(
                candidateDecision.SelectedOutcome,
                BuildStrategySelectorRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    lookback,
                    minSamples,
                    candidates,
                    candidate,
                    candidateDecision,
                    candidateDecision.SelectedOutcome,
                    reason: null),
                candidateDecision.LimitPriceOverride);
        }

        return BtcOpeningLimitDecision.Reject(
            "strategy_selector_no_candidate_current_entry",
            BuildStrategySelectorRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                lookback,
                minSamples,
                candidates,
                selectedCandidate: null,
                candidateDecision: null,
                selectedOutcome: null,
                reason: "strategy_selector_no_candidate_current_entry"));
    }

    private async Task<BtcOpeningLimitDecision> GetMiddleReferenceEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        IDictionary<string, BtcCurrentPriceLookupResult> currentPrices,
        CancellationToken cancellationToken)
    {
        var snapshot = btcUsdReferencePriceCache.Snapshot;
        var requiredCachedSamples = Math.Max(0, variant.DecisionDepth - 1);
        if (snapshot.ArithmeticMeanUsd is not { } meanUsd)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_reference_mean_missing",
                BuildMiddleReferenceRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot,
                    currentPrice: null,
                    requiredCachedSamples,
                    [],
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "btc_reference_mean_missing"));
        }

        if (snapshot.Samples.Count < requiredCachedSamples)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_reference_samples_insufficient",
                BuildMiddleReferenceRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot,
                    currentPrice: null,
                    requiredCachedSamples,
                    snapshot.Samples.Take(requiredCachedSamples).ToArray(),
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "btc_reference_samples_insufficient"));
        }

        var currentPriceLookup = await GetBtcCurrentPriceAsync(market, currentPrices, cancellationToken);
        if (currentPriceLookup.Price is not { } currentPrice)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_reference_fetch_failed",
                BuildMiddleReferenceRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot,
                    currentPrice: null,
                    requiredCachedSamples,
                    snapshot.Samples.Take(requiredCachedSamples).ToArray(),
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "btc_reference_fetch_failed"));
        }

        var cachedSamples = snapshot.Samples.Take(requiredCachedSamples).ToArray();
        var comparedPrices = cachedSamples
            .Select(sample => sample.PriceUsd)
            .Prepend(currentPrice.PriceUsd)
            .ToArray();
        var baseSelectedDirection = ResolveMeanReversionDirection(comparedPrices, meanUsd);
        if (baseSelectedDirection is null)
        {
            var reason = comparedPrices.Any(price => price == meanUsd)
                ? "btc_reference_equal_mean"
                : "btc_reference_mixed_around_mean";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildMiddleReferenceRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot,
                    currentPrice,
                    requiredCachedSamples,
                    cachedSamples,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason));
        }

        var selectedDirection = IsMiddleReferenceRevert(variant)
            ? InvertDirection(baseSelectedDirection.Value)
            : baseSelectedDirection.Value;
        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildMiddleReferenceRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot,
                    currentPrice,
                    requiredCachedSamples,
                    cachedSamples,
                    baseSelectedDirection,
                    selectedDirection,
                    selectedOutcome: null,
                    reason: "target_outcome_not_available"));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildMiddleReferenceRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                snapshot,
                currentPrice,
                requiredCachedSamples,
                cachedSamples,
                baseSelectedDirection,
                selectedDirection,
                selectedOutcome,
                reason: null));
    }

    private async Task<BtcCurrentPriceLookupResult> GetBtcStartRelativeCurrentPriceAsync(
        PolymarketGammaMarket market,
        IDictionary<string, BtcCurrentPriceLookupResult> currentPrices,
        CancellationToken cancellationToken)
    {
        var cacheKey = "start_relative:" + market.MarketId;
        if (currentPrices.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        BtcCurrentPriceLookupResult result;
        var latestTick = await repository.GetLatestBtcUpDown5mOddsTickAsync(market.MarketId, cancellationToken);
        if (latestTick is { SecondsAfterStart: > 0m })
        {
            result = BtcCurrentPriceLookupResult.Success(new BtcUsdReferencePricePoint(
                latestTick.BinancePriceUsd,
                latestTick.BinanceSourceUpdatedAtUtc,
                latestTick.BinanceFetchedAtUtc,
                "BinanceTradeWebSocketOddsArchive"));
        }
        else
        {
            result = await GetBtcCurrentPriceAsync(market, currentPrices, cancellationToken);
        }

        currentPrices[cacheKey] = result;
        return result;
    }

    private async Task<BtcCurrentPriceLookupResult> GetBtcCurrentPriceAsync(
        PolymarketGammaMarket market,
        IDictionary<string, BtcCurrentPriceLookupResult> currentPrices,
        CancellationToken cancellationToken)
    {
        if (currentPrices.TryGetValue(market.MarketId, out var cached))
        {
            return cached;
        }

        BtcCurrentPriceLookupResult result;
        try
        {
            result = BtcCurrentPriceLookupResult.Success(await btcUsdReferencePriceClient.GetBtcUsdPriceAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await TryRecordApiErrorAsync("GetBtcUsdReferencePrice", ex.Message, cancellationToken);
            result = BtcCurrentPriceLookupResult.Failure(ex.Message);
        }

        currentPrices[market.MarketId] = result;
        return result;
    }

    private async Task<BtcOpeningLimitDecision> GetSkipConsecutiveMarketResultsEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var requiredResults = Math.Max(1, variant.DecisionDepth);
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        if (marketStartUtc is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_market_start_missing",
                BuildSkipConsecutiveResultsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    requiredResults,
                    [],
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "btc_market_start_missing"));
        }

        var expectedMarketStarts = GetExpectedPreviousBtc5mMarketStarts(marketStartUtc.Value, requiredResults);
        var closeBookLookup = await GetStrictPreviousCloseBookMarketResultsAsync(
            expectedMarketStarts,
            nowUtc,
            cancellationToken);
        var considered = closeBookLookup.Results;
        if (considered.Count < requiredResults)
        {
            var reason = closeBookLookup.HasOrderBookUnavailable
                ? "btc_previous_close_book_orderbook_unavailable"
                : "btc_previous_close_book_result_missing";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildSkipConsecutiveResultsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    requiredResults,
                    considered,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: reason,
                    closeBookDiagnostics: closeBookLookup.Diagnostics));
        }

        var baseSelectedDirection = ResolveOppositeDirectionAfterConsecutiveResults(considered);
        if (baseSelectedDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_market_results_not_consecutive",
                BuildSkipConsecutiveResultsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    requiredResults,
                    considered,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "btc_market_results_not_consecutive"));
        }

        var selectedDirection = IsSkipConsecutiveMarketResultsRevert(variant)
            ? InvertDirection(baseSelectedDirection.Value)
            : baseSelectedDirection.Value;
        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildSkipConsecutiveResultsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    requiredResults,
                    considered,
                    baseSelectedDirection,
                    selectedDirection,
                    selectedOutcome: null,
                    reason: "target_outcome_not_available"));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildSkipConsecutiveResultsRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                requiredResults,
                considered,
                baseSelectedDirection,
                selectedDirection,
                selectedOutcome,
                reason: null));
    }

    private static IReadOnlyList<DateTimeOffset> GetExpectedPreviousBtc5mMarketStarts(
        DateTimeOffset marketStartUtc,
        int requiredResults)
    {
        return Enumerable
            .Range(1, Math.Max(0, requiredResults))
            .Select(index => marketStartUtc.AddMinutes(-5 * index))
            .ToArray();
    }

    private async Task<BtcSkipCloseBookLookupResult> GetStrictPreviousCloseBookMarketResultsAsync(
        IReadOnlyList<DateTimeOffset> expectedMarketStarts,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (expectedMarketStarts.Count == 0)
        {
            return new BtcSkipCloseBookLookupResult([], []);
        }

        var markets = await repository.GetBtcUpDown5mGammaMarketsAsync(
            Math.Max(options.MaxMarketsPerCycle, expectedMarketStarts.Count * 4),
            cancellationToken);
        var marketsByStart = markets
            .Where(BtcUpDown5mMarketAnalyzer.IsCandidate)
            .Select(market => new
            {
                Market = market,
                WindowStart = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market)
            })
            .Where(item => item.WindowStart is not null)
            .GroupBy(item => item.WindowStart!.Value.ToUnixTimeSeconds())
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.Market.UpdatedAtUtc ?? item.Market.FetchedAtUtc)
                    .First()
                    .Market);

        var selected = new List<BtcSkipMarketResult>(expectedMarketStarts.Count);
        var diagnostics = new List<BtcSkipCloseBookDiagnostic>();
        foreach (var expectedMarketStart in expectedMarketStarts)
        {
            if (!marketsByStart.TryGetValue(expectedMarketStart.ToUnixTimeSeconds(), out var previousMarket))
            {
                diagnostics.Add(new BtcSkipCloseBookDiagnostic(
                    expectedMarketStart,
                    null,
                    null,
                    null,
                    null,
                    "btc_previous_close_book_market_missing",
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null));
                break;
            }

            if (previousMarket.EndDateUtc is { } previousEnd && previousEnd > nowUtc)
            {
                diagnostics.Add(new BtcSkipCloseBookDiagnostic(
                    expectedMarketStart,
                    previousMarket.MarketId,
                    previousMarket.ConditionId,
                    previousMarket.Slug,
                    previousMarket.EndDateUtc,
                    "btc_previous_close_book_market_not_closed",
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null));
                break;
            }

            var inference = await TryInferBtcResultFromCloseBookMidpointAsync(
                previousMarket,
                expectedMarketStart,
                nowUtc,
                cancellationToken);
            if (inference.Result is null)
            {
                if (inference.Diagnostic is not null)
                {
                    diagnostics.Add(inference.Diagnostic);
                }

                break;
            }

            selected.Add(inference.Result);
        }

        return new BtcSkipCloseBookLookupResult(selected, diagnostics);
    }

    private async Task<BtcSkipCloseBookInferenceResult> TryInferBtcResultFromCloseBookMidpointAsync(
        PolymarketGammaMarket previousMarket,
        DateTimeOffset expectedMarketStartUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var upOutcome = TrySelectOutcomeForDirection(previousMarket, BtcPriceDirection.Up);
        if (upOutcome is null)
        {
            return BtcSkipCloseBookInferenceResult.Missing(new BtcSkipCloseBookDiagnostic(
                expectedMarketStartUtc,
                previousMarket.MarketId,
                previousMarket.ConditionId,
                previousMarket.Slug,
                previousMarket.EndDateUtc,
                "btc_close_book_up_outcome_missing",
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null));
        }

        var downOutcome = TrySelectOutcomeForDirection(previousMarket, BtcPriceDirection.Down);
        var upLookup = await TryGetCloseBookMidpointAsync(upOutcome.AssetId, cancellationToken);
        var downLookup = downOutcome is null
            ? null
            : await TryGetCloseBookMidpointAsync(downOutcome.AssetId, cancellationToken);

        var candidates = BuildCloseBookInferenceCandidates(upLookup, downLookup);
        var inferredOutcomes = candidates
            .Select(candidate => candidate.WinningOutcome)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Count == 0 || inferredOutcomes.Length > 1)
        {
            var orderBookUnavailable = IsCloseBookOrderBookUnavailableReason(upLookup.RejectionReason) ||
                IsCloseBookOrderBookUnavailableReason(downLookup?.RejectionReason);
            return BtcSkipCloseBookInferenceResult.Missing(new BtcSkipCloseBookDiagnostic(
                expectedMarketStartUtc,
                previousMarket.MarketId,
                previousMarket.ConditionId,
                previousMarket.Slug,
                previousMarket.EndDateUtc,
                inferredOutcomes.Length > 1
                    ? "btc_close_book_inference_conflict"
                    : "btc_close_book_price_evidence_unavailable",
                orderBookUnavailable,
                upOutcome.AssetId,
                downOutcome?.AssetId,
                upLookup.RejectionReason,
                upLookup.Midpoint?.BestBid ?? upLookup.OrderBook?.BestBid,
                upLookup.Midpoint?.BestAsk ?? upLookup.OrderBook?.BestAsk,
                upLookup.Midpoint?.Midpoint,
                downLookup?.RejectionReason,
                downLookup?.Midpoint?.BestBid ?? downLookup?.OrderBook?.BestBid,
                downLookup?.Midpoint?.BestAsk ?? downLookup?.OrderBook?.BestAsk,
                downLookup?.Midpoint?.Midpoint,
                upLookup.Source,
                downLookup?.Source));
        }

        var selectedCandidate = candidates
            .OrderBy(candidate => candidate.Priority)
            .First();
        return BtcSkipCloseBookInferenceResult.Success(new BtcSkipMarketResult(
            previousMarket.MarketId,
            previousMarket.ConditionId,
            previousMarket.Slug,
            BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(previousMarket),
            previousMarket.EndDateUtc,
            selectedCandidate.WinningOutcome,
            nowUtc,
            selectedCandidate.Source,
            upOutcome.AssetId,
            downOutcome?.AssetId,
            upLookup.Midpoint?.BestBid ?? upLookup.OrderBook?.BestBid,
            upLookup.Midpoint?.BestAsk ?? upLookup.OrderBook?.BestAsk,
            upLookup.Midpoint?.Midpoint,
            downLookup?.Midpoint?.BestBid ?? downLookup?.OrderBook?.BestBid,
            downLookup?.Midpoint?.BestAsk ?? downLookup?.OrderBook?.BestAsk,
            downLookup?.Midpoint?.Midpoint,
            selectedCandidate.InferredUpPrice));
    }

    private static IReadOnlyList<CloseBookInferenceCandidate> BuildCloseBookInferenceCandidates(
        CloseBookMidpointLookup upLookup,
        CloseBookMidpointLookup? downLookup)
    {
        var candidates = new List<CloseBookInferenceCandidate>();
        if (upLookup.Midpoint is { } upMidpoint)
        {
            candidates.Add(new CloseBookInferenceCandidate(
                upMidpoint.Midpoint >= CloseBookResultThreshold ? "Up" : "Down",
                upMidpoint.Midpoint,
                CloseBookResultSource(upLookup.Source, "up_midpoint"),
                0));
        }
        else
        {
            AddUpCloseBookSingleSideCandidates(candidates, upLookup);
        }

        if (downLookup is null)
        {
            return candidates;
        }

        if (downLookup.Midpoint is { } downMidpoint)
        {
            var inferredUpPrice = 1m - downMidpoint.Midpoint;
            candidates.Add(new CloseBookInferenceCandidate(
                inferredUpPrice >= CloseBookResultThreshold ? "Up" : "Down",
                inferredUpPrice,
                CloseBookResultSource(downLookup.Source, "down_midpoint_complement"),
                1));
        }
        else
        {
            AddDownCloseBookSingleSideCandidates(candidates, downLookup);
        }

        return candidates;
    }

    private static void AddUpCloseBookSingleSideCandidates(
        List<CloseBookInferenceCandidate> candidates,
        CloseBookMidpointLookup lookup)
    {
        if (lookup.OrderBook?.BestBid is { } bestBid &&
            bestBid >= CloseBookResultThreshold &&
            IsUsableCloseBookPrice(bestBid))
        {
            candidates.Add(new CloseBookInferenceCandidate(
                "Up",
                bestBid,
                CloseBookResultSource(lookup.Source, "up_best_bid"),
                2));
        }

        if (lookup.OrderBook?.BestAsk is { } bestAsk &&
            bestAsk < CloseBookResultThreshold &&
            IsUsableCloseBookPrice(bestAsk))
        {
            candidates.Add(new CloseBookInferenceCandidate(
                "Down",
                bestAsk,
                CloseBookResultSource(lookup.Source, "up_best_ask"),
                2));
        }
    }

    private static void AddDownCloseBookSingleSideCandidates(
        List<CloseBookInferenceCandidate> candidates,
        CloseBookMidpointLookup lookup)
    {
        if (lookup.OrderBook?.BestAsk is { } bestAsk &&
            bestAsk <= CloseBookResultThreshold &&
            IsUsableCloseBookPrice(bestAsk))
        {
            candidates.Add(new CloseBookInferenceCandidate(
                "Up",
                1m - bestAsk,
                CloseBookResultSource(lookup.Source, "down_best_ask_complement"),
                3));
        }

        if (lookup.OrderBook?.BestBid is { } bestBid &&
            bestBid > CloseBookResultThreshold &&
            IsUsableCloseBookPrice(bestBid))
        {
            candidates.Add(new CloseBookInferenceCandidate(
                "Down",
                1m - bestBid,
                CloseBookResultSource(lookup.Source, "down_best_bid_complement"),
                3));
        }
    }

    private static string CloseBookResultSource(string source, string suffix)
    {
        var prefix = string.Equals(source, CloseBookSnapshotSource, StringComparison.OrdinalIgnoreCase)
            ? "stored_close_book_snapshot"
            : "clob_close_book";
        return string.Concat(prefix, "_", suffix);
    }

    private async Task<CloseBookMidpointLookup> TryGetCloseBookMidpointAsync(
        string assetId,
        CancellationToken cancellationToken)
    {
        var fetch = await FetchAndCacheOrderBookAsync(assetId, cancellationToken);
        var orderBook = fetch.OrderBook;
        var source = ClobBookSource;
        if (orderBook is not null)
        {
            await TryPersistOrderBookSnapshotAsync(
                orderBook,
                "PersistBtcCloseBookOrderBookSnapshot",
                cancellationToken);
        }

        if (!HasCloseBookPriceEvidence(orderBook))
        {
            var latestSnapshot = await repository.GetLatestOrderBookSnapshotAsync(assetId, cancellationToken);
            if (HasCloseBookPriceEvidence(latestSnapshot))
            {
                orderBook = latestSnapshot;
                source = CloseBookSnapshotSource;
            }
        }

        if (orderBook is null)
        {
            return new CloseBookMidpointLookup(
                null,
                fetch.RejectionReason ?? SignalReasonCodes.MissingOrderBookRestMissing,
                null,
                source);
        }

        if (orderBook.BestBid is not { } bestBid ||
            orderBook.BestAsk is not { } bestAsk ||
            bestBid <= 0m ||
            bestBid > 1m ||
            bestAsk <= 0m ||
            bestAsk > 1m)
        {
            return new CloseBookMidpointLookup(
                null,
                HasCloseBookPriceEvidence(orderBook)
                    ? null
                    : SignalReasonCodes.MissingOrderBookEmptySide,
                orderBook,
                source);
        }

        return new CloseBookMidpointLookup(
            new CloseBookMidpoint(bestBid, bestAsk, (bestBid + bestAsk) / 2m),
            null,
            orderBook,
            source);
    }

    private static bool HasCloseBookPriceEvidence(OrderBookSnapshot? orderBook)
    {
        return IsUsableCloseBookPrice(orderBook?.BestBid) ||
            IsUsableCloseBookPrice(orderBook?.BestAsk);
    }

    private static bool IsUsableCloseBookPrice(decimal? price)
    {
        return price is > 0m and <= 1m;
    }

    private static BtcPriceDirection? ResolveMeanReversionDirection(
        IReadOnlyList<decimal> prices,
        decimal meanUsd)
    {
        if (prices.Count == 0)
        {
            return null;
        }

        if (prices.All(price => price > meanUsd))
        {
            return BtcPriceDirection.Down;
        }

        return prices.All(price => price < meanUsd) ? BtcPriceDirection.Up : null;
    }

    private static BtcPriceDirection? ResolveStartRelativeDirection(decimal currentPriceUsd, decimal startPriceUsd)
    {
        if (currentPriceUsd > startPriceUsd)
        {
            return BtcPriceDirection.Up;
        }

        return currentPriceUsd < startPriceUsd ? BtcPriceDirection.Down : null;
    }

    private static BtcPriceDirection? ResolveOppositeDirectionAfterConsecutiveResults(
        IReadOnlyList<BtcSkipMarketResult> results)
    {
        if (results.Count == 0)
        {
            return null;
        }

        if (results.All(result => string.Equals(result.WinningOutcome, "Up", StringComparison.OrdinalIgnoreCase)))
        {
            return BtcPriceDirection.Down;
        }

        return results.All(result => string.Equals(result.WinningOutcome, "Down", StringComparison.OrdinalIgnoreCase))
            ? BtcPriceDirection.Up
            : null;
    }

    private static BtcPriceDirection InvertDirection(BtcPriceDirection direction)
    {
        return direction == BtcPriceDirection.Up ? BtcPriceDirection.Down : BtcPriceDirection.Up;
    }

    private static bool IsSkipConsecutiveMarketResultsRevert(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert;
    }

    private static bool IsSkipConsecutiveMarketResults(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults or
            BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert;
    }

    private static bool IsCloseBookOrderBookUnavailableReason(string? reason)
    {
        return reason is not null &&
            reason.StartsWith("missing_orderbook", StringComparison.Ordinal);
    }

    private bool ShouldDeferOpeningLimitDecision(
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        BtcOpeningLimitDecision decision,
        DateTimeOffset nowUtc)
    {
        if (IsBinanceStartRelativeOpeningLimitEntry(variant) &&
            !IsOpeningLimitSignalWaitExpired(run.EntryDueAtUtc, nowUtc) &&
            (string.Equals(decision.SkipReason, "btc_market_start_price_missing", StringComparison.Ordinal) ||
                string.Equals(decision.SkipReason, "btc_reference_equal_market_start", StringComparison.Ordinal)))
        {
            return true;
        }

        return IsSkipConsecutiveMarketResults(variant) &&
            (string.Equals(decision.SkipReason, "btc_previous_market_results_missing", StringComparison.Ordinal) ||
                string.Equals(decision.SkipReason, "btc_previous_close_book_result_missing", StringComparison.Ordinal));
    }

    private bool IsOpeningLimitSignalWaitExpired(DateTimeOffset entryDueAtUtc, DateTimeOffset nowUtc)
    {
        var waitSeconds = Math.Max(options.EntryGraceSeconds, options.OpeningLimitGtdTtlSeconds);
        return entryDueAtUtc < nowUtc.AddSeconds(-waitSeconds);
    }

    private bool ShouldDeferUntilTradingStarts(
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset nowUtc)
    {
        _ = run;
        return IsOpeningLimitEntryAllowedAfterEntryGrace(variant, run.MarketStartUtc, nowUtc);
    }

    private bool ShouldDeferOpeningLimitStakeSizing(
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        BtcMinimumStakeSizing sizing,
        DateTimeOffset nowUtc)
    {
        _ = run;
        return IsOpeningLimitEntryAllowedAfterEntryGrace(variant, run.MarketStartUtc, nowUtc) &&
            IsCloseBookOrderBookUnavailableReason(sizing.RejectionReason);
    }

    private static bool IsMiddleReferenceRevert(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevert;
    }

    private static BtcUpDown5mStrategyVariant? TryGetBaseOpeningLimitVariantForRevert(BtcUpDown5mStrategyVariant variant)
    {
        var baseBehavior = variant.Behavior switch
        {
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevert => BtcUpDown5mStrategyBehavior.MiddleReference,
            BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert => BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults,
            _ => (BtcUpDown5mStrategyBehavior?)null
        };
        if (baseBehavior is null)
        {
            return null;
        }

        return StrategyIds.BtcUpDown5mVariants.SingleOrDefault(candidate =>
            candidate.Behavior == baseBehavior.Value &&
            candidate.DecisionDepth == variant.DecisionDepth &&
            candidate.EntryDelaySeconds == variant.EntryDelaySeconds &&
            candidate.DecisionThresholdBps == variant.DecisionThresholdBps);
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> GetEnsembleVoteCandidateVariants()
    {
        return
        [
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceCode),
            GetBtcVariantByCode("btc_up_down_5m_middle_1"),
            GetBtcVariantByCode("btc_up_down_5m_skip_1")
        ];
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> GetStrategySelectorCandidateVariants()
    {
        return
        [
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceCode),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceBps01Code),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceBps02Code),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceBps03Code),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceBps04Code),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceBps05Code),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceBps06Code),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceBps07Code),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceBps08Code),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceBps09Code),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceBps1Code),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceBps2Code),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceBps5Code),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceCleverCode),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceCleverAggressiveCode),
            GetBtcVariantByCode(StrategyIds.BtcUpDown5mBinanceCleverConservativeCode),
            GetBtcVariantByCode("btc_up_down_5m_middle_1"),
            GetBtcVariantByCode("btc_up_down_5m_skip_1")
        ];
    }

    private static BtcUpDown5mStrategyVariant GetBtcVariantByCode(string code)
    {
        return StrategyIds.BtcUpDown5mVariants.Single(variant =>
            string.Equals(variant.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    private static BtcUpDown5mOutcomeQuote? TrySelectOutcomeForDirection(
        PolymarketGammaMarket market,
        BtcPriceDirection direction)
    {
        var targetOutcome = direction == BtcPriceDirection.Up ? "Up" : "Down";
        return BtcUpDown5mMarketAnalyzer.GetOutcomeQuotes(market)
            .SingleOrDefault(quote => string.Equals(quote.Outcome, targetOutcome, StringComparison.OrdinalIgnoreCase));
    }

    private static BtcPriceDirection? TryResolveDirectionFromOutcome(string? outcome)
    {
        if (string.Equals(outcome, "Up", StringComparison.OrdinalIgnoreCase))
        {
            return BtcPriceDirection.Up;
        }

        return string.Equals(outcome, "Down", StringComparison.OrdinalIgnoreCase)
            ? BtcPriceDirection.Down
            : null;
    }

    private static bool ShouldRetryTakerPricingWithRest(string? rejectionReason)
    {
        return rejectionReason is SignalReasonCodes.ExecutionPriceDirectionMismatch;
    }

    private async Task<BtcTakerOutcomeSelectionResult> GetTakerPaperOutcomeSelectionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeMultiplier,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken,
        bool enforceSelectedEntryPriceCap = true)
    {
        var selection = await TryGetTakerPaperOutcomeSelectionAsync(
            market,
            variant,
            stakeMultiplier,
            nowUtc,
            forceRestOrderBooks: false,
            cancellationToken: cancellationToken,
            enforceSelectedEntryPriceCap: enforceSelectedEntryPriceCap);
        if (!selection.Filled &&
            selection.CanRetryWithRest &&
            options.PaperTakerRestFallbackEnabled)
        {
            return await TryGetTakerPaperOutcomeSelectionAsync(
                market,
                variant,
                stakeMultiplier,
                nowUtc,
                forceRestOrderBooks: true,
                cancellationToken: cancellationToken,
                enforceSelectedEntryPriceCap: enforceSelectedEntryPriceCap);
        }

        return selection;
    }

    private async Task<BtcTakerOutcomeSelectionResult> TryGetTakerPaperOutcomeSelectionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeMultiplier,
        DateTimeOffset nowUtc,
        bool forceRestOrderBooks,
        CancellationToken cancellationToken,
        bool enforceSelectedEntryPriceCap = true)
    {
        var outcomes = BtcUpDown5mMarketAnalyzer.GetOutcomeQuotes(market);
        if (outcomes.Count != 2)
        {
            return BtcTakerOutcomeSelectionResult.Reject("target_outcome_not_available");
        }

        var candidates = new List<BtcTakerOutcomePricingCandidate>(outcomes.Count);
        var snapshots = new List<BtcTakerOutcomePricingSnapshot>(outcomes.Count);
        foreach (var outcome in outcomes)
        {
            var pricing = await GetTakerPaperEntryPricingForOutcomeAsync(
                market,
                outcome,
                variant,
                stakeMultiplier,
                nowUtc,
                forceRestOrderBooks,
                enforceDirectionalPrice: false,
                enforceStrategyEntryPriceCap: false,
                outcomeSelectionSnapshots: null,
                cancellationToken);
            if (pricing.Snapshot is not null)
            {
                snapshots.Add(pricing.Snapshot);
            }

            if (pricing.Filled)
            {
                candidates.Add(new BtcTakerOutcomePricingCandidate(outcome, pricing));
            }
        }

        if (candidates.Count != outcomes.Count)
        {
            var rejectionReason = snapshots
                .FirstOrDefault(snapshot => !string.IsNullOrWhiteSpace(snapshot.RejectionReason))
                ?.RejectionReason;
            var reason = rejectionReason ?? SignalReasonCodes.ClobOutcomeSelectionIncomplete;
            return BtcTakerOutcomeSelectionResult.Reject(
                reason,
                CanRetryWithRest: !forceRestOrderBooks &&
                    snapshots.Any(snapshot =>
                        !string.IsNullOrWhiteSpace(snapshot.RejectionReason) &&
                        string.Equals(snapshot.Source, WebSocketCacheSource, StringComparison.OrdinalIgnoreCase)),
                SkipDiagnosticsJson: BuildTakerPaperRejectionDiagnosticsJson(
                    market,
                    variant,
                    reason,
                    stakeMultiplier,
                    nowUtc,
                    snapshots));
        }

        var selected = variant.Direction switch
        {
            BtcUpDown5mStrategyDirection.Less => TrySelectExecutableLowerPricedOutcome(candidates),
            BtcUpDown5mStrategyDirection.More => TrySelectExecutableHigherPricedOutcome(candidates),
            _ => null
        };
        if (selected is null)
        {
            return BtcTakerOutcomeSelectionResult.Reject(
                SignalReasonCodes.ClobOutcomeSelectionAmbiguous,
                CanRetryWithRest: !forceRestOrderBooks &&
                    candidates.Any(candidate =>
                        string.Equals(candidate.EntryPricing.Source, WebSocketCacheSource, StringComparison.OrdinalIgnoreCase)),
                SkipDiagnosticsJson: BuildTakerPaperRejectionDiagnosticsJson(
                    market,
                    variant,
                    SignalReasonCodes.ClobOutcomeSelectionAmbiguous,
                    stakeMultiplier,
                    nowUtc,
                    snapshots));
        }

        if (!IsDirectionalPriceAllowedForVariant(selected.EntryPricing.AverageFillPrice, variant))
        {
            return BtcTakerOutcomeSelectionResult.Reject(
                SignalReasonCodes.ExecutionPriceDirectionMismatch,
                CanRetryWithRest: !forceRestOrderBooks &&
                    string.Equals(selected.EntryPricing.Source, WebSocketCacheSource, StringComparison.OrdinalIgnoreCase),
                SkipDiagnosticsJson: BuildTakerPaperRejectionDiagnosticsJson(
                    market,
                    variant,
                    SignalReasonCodes.ExecutionPriceDirectionMismatch,
                    stakeMultiplier,
                    nowUtc,
                    snapshots));
        }

        if (enforceSelectedEntryPriceCap &&
            TryGetStandardEntryPriceCap(variant) is { } entryPriceCap &&
            selected.EntryPricing.AverageFillPrice >= entryPriceCap)
        {
            return BtcTakerOutcomeSelectionResult.Reject(
                SignalReasonCodes.ExecutionPriceAboveStrategyCap,
                CanRetryWithRest: !forceRestOrderBooks &&
                    string.Equals(selected.EntryPricing.Source, WebSocketCacheSource, StringComparison.OrdinalIgnoreCase),
                SkipDiagnosticsJson: BuildTakerPaperRejectionDiagnosticsJson(
                    market,
                    variant,
                    SignalReasonCodes.ExecutionPriceAboveStrategyCap,
                    stakeMultiplier,
                    nowUtc,
                    snapshots));
        }

        var selectedPricing = selected.EntryPricing;
        if (selectedPricing.OrderBookLookup is not null &&
            selectedPricing.Estimate is not null &&
            selectedPricing.ClobGammaDiff is { } clobGammaDiff)
        {
            selectedPricing = selectedPricing with
            {
                RawDecisionJson = BuildTakerPaperEntryRawDecisionJson(
                    market,
                    selected.Outcome,
                    variant,
                    selectedPricing.OrderBookLookup,
                    selectedPricing.Estimate,
                    selectedPricing.NotionalUsd,
                    selectedPricing.Sizing?.StakeMultiplier ?? stakeMultiplier,
                    selectedPricing.Sizing ?? BtcMinimumStakeSizing.FallbackFixedStake(
                        stakeMultiplier,
                        selectedPricing.AverageFillPrice,
                        selectedPricing.Source),
                    clobGammaDiff,
                    nowUtc,
                    snapshots)
            };
        }

        return BtcTakerOutcomeSelectionResult.Fill(selected.Outcome, selectedPricing);
    }

    private static BtcTakerOutcomePricingCandidate? TrySelectExecutableLowerPricedOutcome(
        IReadOnlyList<BtcTakerOutcomePricingCandidate> candidates)
    {
        var lowestPrice = candidates.Min(candidate => candidate.EntryPricing.AverageFillPrice);
        var selected = candidates
            .Where(candidate => candidate.EntryPricing.AverageFillPrice == lowestPrice)
            .ToArray();
        return selected.Length == 1 ? selected[0] : null;
    }

    private static BtcTakerOutcomePricingCandidate? TrySelectExecutableHigherPricedOutcome(
        IReadOnlyList<BtcTakerOutcomePricingCandidate> candidates)
    {
        var highestPrice = candidates.Max(candidate => candidate.EntryPricing.AverageFillPrice);
        var selected = candidates
            .Where(candidate => candidate.EntryPricing.AverageFillPrice == highestPrice)
            .ToArray();
        return selected.Length == 1 ? selected[0] : null;
    }

    private async Task<BtcPaperEntryPricingResult> GetPaperEntryPricingAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeMultiplier,
        DateTimeOffset nowUtc,
        bool enforceTakerDirectionalPrice,
        CancellationToken cancellationToken)
    {
        if (!options.PaperTakerPricingEnabled)
        {
            var targetNotionalUsd = stakeMultiplier;
            var sizeShares = targetNotionalUsd / outcome.Price;
            return BtcPaperEntryPricingResult.CreateFilled(
                outcome.Price,
                sizeShares,
                targetNotionalUsd,
                GammaOutcomePriceSource,
                $"BtcUpDown5mPaper:{variant.Code}: GTD limit order seeded from Gamma outcomePrices on {GetDirectionDescription(variant)} outcome with {targetNotionalUsd.ToString("0.########", CultureInfo.InvariantCulture)} USD paper stake.",
                BuildGammaPaperEntryRawDecisionJson(market, outcome, variant, targetNotionalUsd, sizeShares, nowUtc));
        }

        return await GetTakerPaperEntryPricingForOutcomeAsync(
            market,
            outcome,
            variant,
            stakeMultiplier,
            nowUtc,
            forceRestOrderBooks: false,
            enforceDirectionalPrice: enforceTakerDirectionalPrice,
            enforceStrategyEntryPriceCap: true,
            outcomeSelectionSnapshots: null,
            cancellationToken);
    }

    private async Task<BtcPaperEntryPricingResult> GetTakerPaperEntryPricingForOutcomeAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeMultiplier,
        DateTimeOffset nowUtc,
        bool forceRestOrderBooks,
        bool enforceDirectionalPrice,
        bool enforceStrategyEntryPriceCap,
        IReadOnlyList<BtcTakerOutcomePricingSnapshot>? outcomeSelectionSnapshots,
        CancellationToken cancellationToken)
    {
        var orderBookLookup = forceRestOrderBooks
            ? await GetFreshRestTakerOrderBookAsync(outcome.AssetId, nowUtc, cancellationToken)
            : await GetFreshTakerOrderBookAsync(outcome.AssetId, nowUtc, cancellationToken);
        if (orderBookLookup.RejectionReason is not null || orderBookLookup.OrderBook is null)
        {
            var reason = orderBookLookup.RejectionReason ?? SignalReasonCodes.MissingOrderBook;
            var snapshot = CreateTakerOutcomePricingSnapshot(outcome, stakeMultiplier, orderBookLookup, null, reason);
            return BtcPaperEntryPricingResult.Reject(
                reason,
                snapshot,
                BuildTakerPaperRejectionDiagnosticsJson(
                    market,
                    variant,
                    reason,
                    stakeMultiplier,
                    nowUtc,
                    [snapshot]));
        }

        var entryPricing = CreateTakerPaperEntryPricingResult(
            market,
            outcome,
            variant,
            orderBookLookup,
            stakeMultiplier,
            nowUtc,
            enforceDirectionalPrice,
            enforceStrategyEntryPriceCap,
            outcomeSelectionSnapshots);
        if (ShouldRetryTakerPricingWithRest(entryPricing.RejectionReason) &&
            string.Equals(orderBookLookup.Source, WebSocketCacheSource, StringComparison.OrdinalIgnoreCase) &&
            !forceRestOrderBooks &&
            options.PaperTakerRestFallbackEnabled)
        {
            var restOrderBookLookup = await GetFreshRestTakerOrderBookAsync(outcome.AssetId, nowUtc, cancellationToken);
            if (restOrderBookLookup.RejectionReason is null && restOrderBookLookup.OrderBook is not null)
            {
                return CreateTakerPaperEntryPricingResult(
                    market,
                    outcome,
                    variant,
                    restOrderBookLookup,
                    stakeMultiplier,
                    nowUtc,
                    enforceDirectionalPrice,
                    enforceStrategyEntryPriceCap,
                    outcomeSelectionSnapshots);
            }
        }

        return entryPricing;
    }

    private BtcPaperEntryPricingResult CreateTakerPaperEntryPricingResult(
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        TakerOrderBookLookupResult orderBookLookup,
        decimal stakeMultiplier,
        DateTimeOffset nowUtc,
        bool enforceDirectionalPrice,
        bool enforceStrategyEntryPriceCap,
        IReadOnlyList<BtcTakerOutcomePricingSnapshot>? outcomeSelectionSnapshots)
    {
        if (orderBookLookup.OrderBook is not { } orderBook)
        {
            var reason = orderBookLookup.RejectionReason ?? SignalReasonCodes.MissingOrderBook;
            return BtcPaperEntryPricingResult.Reject(
                reason,
                CreateTakerOutcomePricingSnapshot(outcome, stakeMultiplier, orderBookLookup, null, reason));
        }

        var maxAllowedPrice = GetPaperTakerMaxAllowedPrice(outcome.Price);
        // Temporarily allow BTC Paper to enter at the current top-of-book ask even when it moved
        // above the reference cap; spread and executable-depth checks still apply.
        if (orderBook.BestAsk is { } bestAsk && bestAsk > maxAllowedPrice)
        {
            maxAllowedPrice = bestAsk;
        }

        var sizing = CreateTakerMinimumStakeSizing(orderBook, maxAllowedPrice, stakeMultiplier, orderBookLookup.Source);
        if (!sizing.Available)
        {
            var reason = sizing.RejectionReason ?? "paper_taker_minimum_stake_rejected";
            var snapshot = CreateTakerOutcomePricingSnapshot(outcome, stakeMultiplier, orderBookLookup, null, reason);
            return BtcPaperEntryPricingResult.Reject(
                reason,
                snapshot,
                BuildTakerPaperRejectionDiagnosticsJson(
                    market,
                    variant,
                    reason,
                    stakeMultiplier,
                    nowUtc,
                    [snapshot]));
        }

        var targetNotionalUsd = sizing.TargetNotionalUsd;
        var estimate = TakerBuyFillEstimator.Estimate(
            orderBook,
            targetNotionalUsd,
            maxAllowedPrice,
            orderBook.MinOrderSize,
            options.PaperTakerMaxSpreadAbs);
        if (!estimate.Filled)
        {
            var reason = estimate.RejectionReason ?? "paper_taker_fill_rejected";
            var snapshot = CreateTakerOutcomePricingSnapshot(outcome, targetNotionalUsd, orderBookLookup, estimate, reason);
            return BtcPaperEntryPricingResult.Reject(
                reason,
                snapshot,
                BuildTakerPaperRejectionDiagnosticsJson(
                    market,
                    variant,
                    reason,
                    targetNotionalUsd,
                    nowUtc,
                    [snapshot]));
        }

        if (enforceDirectionalPrice && !IsDirectionalPriceAllowedForVariant(estimate.AverageFillPrice, variant))
        {
            var snapshot = CreateTakerOutcomePricingSnapshot(
                outcome,
                targetNotionalUsd,
                orderBookLookup,
                estimate,
                SignalReasonCodes.ExecutionPriceDirectionMismatch);
            return BtcPaperEntryPricingResult.Reject(
                SignalReasonCodes.ExecutionPriceDirectionMismatch,
                snapshot,
                BuildTakerPaperRejectionDiagnosticsJson(
                    market,
                    variant,
                    SignalReasonCodes.ExecutionPriceDirectionMismatch,
                    targetNotionalUsd,
                    nowUtc,
                    [snapshot]));
        }

        if (enforceStrategyEntryPriceCap &&
            TryGetStandardEntryPriceCap(variant) is { } entryPriceCap &&
            estimate.AverageFillPrice >= entryPriceCap)
        {
            var snapshot = CreateTakerOutcomePricingSnapshot(
                outcome,
                targetNotionalUsd,
                orderBookLookup,
                estimate,
                SignalReasonCodes.ExecutionPriceAboveStrategyCap);
            return BtcPaperEntryPricingResult.Reject(
                SignalReasonCodes.ExecutionPriceAboveStrategyCap,
                snapshot,
                BuildTakerPaperRejectionDiagnosticsJson(
                    market,
                    variant,
                    SignalReasonCodes.ExecutionPriceAboveStrategyCap,
                    targetNotionalUsd,
                    nowUtc,
                    [snapshot]));
        }

        var clobGammaDiff = Math.Abs(estimate.AverageFillPrice - outcome.Price);
        var quoteAgeMs = orderBookLookup.Age?.TotalMilliseconds;
        var rawDecisionJson = BuildTakerPaperEntryRawDecisionJson(
            market,
            outcome,
            variant,
            orderBookLookup,
            estimate,
            targetNotionalUsd,
            stakeMultiplier,
            sizing,
            clobGammaDiff,
            nowUtc,
            outcomeSelectionSnapshots);
        var evidence = string.Concat(
            "BtcUpDown5mPaper:",
            variant.Code,
            ": GTD limit order seeded from ",
            orderBookLookup.Source,
            " VWAP. AvgPrice=",
            estimate.AverageFillPrice.ToString("0.########", CultureInfo.InvariantCulture),
            " SizeShares=",
            estimate.SizeShares.ToString("0.########", CultureInfo.InvariantCulture),
            " NotionalUsd=",
            estimate.NotionalUsd.ToString("0.########", CultureInfo.InvariantCulture),
            " MaxAllowedPrice=",
            maxAllowedPrice.ToString("0.########", CultureInfo.InvariantCulture),
            quoteAgeMs is null ? string.Empty : " QuoteAgeMs=" + quoteAgeMs.Value.ToString("0", CultureInfo.InvariantCulture));

        return BtcPaperEntryPricingResult.CreateFilled(
            estimate.AverageFillPrice,
            estimate.SizeShares,
            estimate.NotionalUsd,
            orderBookLookup.Source,
            evidence,
            rawDecisionJson,
            CreateTakerOutcomePricingSnapshot(outcome, targetNotionalUsd, orderBookLookup, estimate, null),
            orderBookLookup,
            estimate,
            sizing,
            clobGammaDiff);
    }

    private decimal GetPaperTakerMaxAllowedPrice(decimal referencePrice)
    {
        return Math.Min(
            options.PaperTakerMaxEntryPrice,
            Math.Min(1m, referencePrice + options.PaperTakerMaxReferenceSlippage));
    }

    private BtcMinimumStakeSizing CreateTakerMinimumStakeSizing(
        OrderBookSnapshot orderBook,
        decimal maxAllowedPrice,
        decimal stakeMultiplier,
        string source)
    {
        if (stakeMultiplier <= 0m)
        {
            return BtcMinimumStakeSizing.Reject("invalid_stake_multiplier", stakeMultiplier);
        }

        if (orderBook.MinOrderSize is not > 0m)
        {
            return BtcMinimumStakeSizing.FallbackFixedStake(
                stakeMultiplier,
                orderBook.BestAsk ?? maxAllowedPrice,
                source);
        }

        var minimum = TakerBuyFillEstimator.EstimateMinimumBuyNotional(
            orderBook,
            maxAllowedPrice,
            orderBook.MinOrderSize.Value,
            options.PaperTakerMaxSpreadAbs);
        if (!minimum.Available)
        {
            return BtcMinimumStakeSizing.Reject(
                minimum.RejectionReason ?? "minimum_stake_notional_unavailable",
                stakeMultiplier,
                source);
        }

        var rawTargetNotionalUsd = minimum.NotionalUsd * MinimumStakeSafetyMultiplier * stakeMultiplier;
        var targetNotionalUsd = RoundStakeNotionalUsd(rawTargetNotionalUsd);
        return new BtcMinimumStakeSizing(
            Available: true,
            RejectionReason: null,
            Source: source,
            StakeMultiplier: stakeMultiplier,
            SafetyMultiplier: MinimumStakeSafetyMultiplier,
            RoundingMode: StakeNotionalRoundingMode,
            MinOrderSize: orderBook.MinOrderSize,
            MinimumNotionalUsd: minimum.NotionalUsd,
            RawTargetNotionalUsd: rawTargetNotionalUsd,
            TargetNotionalUsd: targetNotionalUsd,
            TargetSizeShares: minimum.AveragePrice > 0m ? targetNotionalUsd / minimum.AveragePrice : 0m,
            ReferencePrice: minimum.AveragePrice,
            LevelsUsed: minimum.LevelsUsed);
    }

    private async Task<BtcOpeningLimitBookBootstrapPriceDecision> GetOpeningLimitBookBootstrapPriceAsync(
        string assetId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var maxAge = GetPaperTakerMaxQuoteAge();
        var lookup = marketDataCache.GetOrderBook(assetId, maxAge);
        if (lookup is { Status: OrderBookCacheLookupStatus.Fresh, Snapshot: { } cached })
        {
            return CreateOpeningLimitBookBootstrapPriceDecision(
                cached,
                WebSocketCacheSource,
                lookup.Age);
        }

        if (options.PaperTakerRestFallbackEnabled)
        {
            var fetched = await FetchAndCacheOrderBookAsync(assetId, cancellationToken);
            if (fetched.OrderBook is not null)
            {
                var fetchedAge = GetSnapshotAge(fetched.OrderBook.SnapshotAtUtc);
                if (fetchedAge <= maxAge)
                {
                    return CreateOpeningLimitBookBootstrapPriceDecision(
                        fetched.OrderBook,
                        ClobBookSource,
                        fetchedAge);
                }

                return BtcOpeningLimitBookBootstrapPriceDecision.Reject(
                    SignalReasonCodes.MissingOrderBookCacheStale,
                    ClobBookSource,
                    fetchedAge,
                    fetched.OrderBook);
            }

            return BtcOpeningLimitBookBootstrapPriceDecision.Reject(
                fetched.RejectionReason ?? SignalReasonCodes.MissingOrderBookRestMissing,
                ClobBookSource,
                Age: null,
                OrderBook: null);
        }

        return BtcOpeningLimitBookBootstrapPriceDecision.Reject(
            lookup.Status == OrderBookCacheLookupStatus.Stale
                ? SignalReasonCodes.MissingOrderBookCacheStale
                : SignalReasonCodes.MissingOrderBookCacheMiss,
            WebSocketCacheSource,
            lookup.Age,
            lookup.Snapshot);
    }

    private BtcOpeningLimitBookBootstrapPriceDecision CreateOpeningLimitBookBootstrapPriceDecision(
        OrderBookSnapshot orderBook,
        string source,
        TimeSpan? age)
    {
        var tickSize = orderBook.TickSize is > 0m
            ? orderBook.TickSize.Value
            : options.OpeningLimitPriceTickSize;
        var maxPrice = Math.Min(options.OpeningLimitMaxPrice, 0.50m);
        var bestAsk = TryGetBestAskFromOrderBook(orderBook);
        var bestBid = TryGetBestBidFromOrderBook(orderBook);
        decimal? rawLimitPrice = null;
        string? priceSource = null;
        if (bestAsk is { } ask && ask <= maxPrice)
        {
            rawLimitPrice = ask;
            priceSource = "best_ask";
        }
        else if (bestBid is { } bid)
        {
            rawLimitPrice = Math.Min(maxPrice, bid + tickSize);
            priceSource = "best_bid_plus_tick";
        }

        if (rawLimitPrice is not { } rawPrice)
        {
            return BtcOpeningLimitBookBootstrapPriceDecision.Reject(
                "opening_limit_book_bootstrap_orderbook_unavailable",
                source,
                age,
                orderBook,
                bestBid: bestBid,
                bestAsk: bestAsk);
        }

        var limitPrice = RoundDownToTick(Math.Min(maxPrice, rawPrice), tickSize);
        if (limitPrice <= 0m)
        {
            return BtcOpeningLimitBookBootstrapPriceDecision.Reject(
                "opening_limit_book_bootstrap_price_non_positive",
                source,
                age,
                orderBook,
                rawLimitPrice,
                tickSize,
                priceSource,
                bestBid,
                bestAsk);
        }

        return BtcOpeningLimitBookBootstrapPriceDecision.Enter(
            limitPrice,
            source,
            age,
            orderBook,
            rawPrice,
            tickSize,
            priceSource,
            bestBid,
            bestAsk);
    }

    private async Task<BtcMinimumStakeSizing> GetOpeningLimitStakeSizingAsync(
        string assetId,
        decimal limitPrice,
        decimal stakeMultiplier,
        decimal? fallbackMinOrderSize,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var maxAge = GetPaperTakerMaxQuoteAge();
        var lookup = marketDataCache.GetOrderBook(assetId, maxAge);
        if (lookup is { Status: OrderBookCacheLookupStatus.Fresh, Snapshot: { } cached })
        {
            return CreateLimitMinimumStakeSizing(cached, limitPrice, stakeMultiplier, WebSocketCacheSource);
        }

        if (options.PaperTakerRestFallbackEnabled)
        {
            var fetched = await FetchAndCacheOrderBookAsync(assetId, cancellationToken);
            if (fetched.OrderBook is not null)
            {
                var fetchedAge = GetSnapshotAge(fetched.OrderBook.SnapshotAtUtc);
                if (fetchedAge <= maxAge)
                {
                    return CreateLimitMinimumStakeSizing(fetched.OrderBook, limitPrice, stakeMultiplier, ClobBookSource);
                }

                if ((fetched.OrderBook.MinOrderSize ?? lookup.Snapshot?.MinOrderSize ?? fallbackMinOrderSize) is { } staleMinOrderSize &&
                    staleMinOrderSize > 0m)
                {
                    return CreateLimitMinimumStakeSizingFromMinOrderSize(
                        staleMinOrderSize,
                        limitPrice,
                        stakeMultiplier,
                        fetched.OrderBook.MinOrderSize is > 0m
                            ? "clob_book_stale_min_order_size"
                            : lookup.Snapshot?.MinOrderSize is > 0m
                                ? "websocket_cache_stale_min_order_size"
                                : "gamma_market_order_min_size");
                }

                return BtcMinimumStakeSizing.Reject(
                    SignalReasonCodes.MissingOrderBookCacheStale,
                    stakeMultiplier,
                    Source: ClobBookSource);
            }
        }

        if ((lookup.Snapshot?.MinOrderSize ?? fallbackMinOrderSize) is { } minOrderSize && minOrderSize > 0m)
        {
            return CreateLimitMinimumStakeSizingFromMinOrderSize(
                minOrderSize,
                limitPrice,
                stakeMultiplier,
                lookup.Snapshot?.MinOrderSize is > 0m
                    ? "websocket_cache_stale_min_order_size"
                    : "gamma_market_order_min_size");
        }

        return BtcMinimumStakeSizing.Reject(
            lookup.Status == OrderBookCacheLookupStatus.Stale
                ? SignalReasonCodes.MissingOrderBookCacheStale
                : SignalReasonCodes.MissingOrderBookCacheMiss,
            stakeMultiplier,
            Source: WebSocketCacheSource);
    }

    private async Task<PaperLiveShadowOrderBookSnapshotResult> GetPaperLiveShadowOrderBookSnapshotAsync(
        string assetId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var maxAge = GetPaperTakerMaxQuoteAge();
        var lookup = marketDataCache.GetOrderBook(assetId, maxAge);
        if (lookup is { Status: OrderBookCacheLookupStatus.Fresh, Snapshot: { } cached })
        {
            return PaperLiveShadowOrderBookSnapshotResult.Found(cached, WebSocketCacheSource, lookup.Age);
        }

        if (options.PaperTakerRestFallbackEnabled)
        {
            var fetched = await FetchAndCacheOrderBookAsync(assetId, cancellationToken);
            if (fetched.OrderBook is not null)
            {
                var fetchedAge = GetSnapshotAge(fetched.OrderBook.SnapshotAtUtc);
                if (fetchedAge <= maxAge)
                {
                    return PaperLiveShadowOrderBookSnapshotResult.Found(fetched.OrderBook, ClobBookSource, fetchedAge);
                }

                return PaperLiveShadowOrderBookSnapshotResult.Reject(
                    SignalReasonCodes.MissingOrderBookCacheStale,
                    ClobBookSource);
            }

            return PaperLiveShadowOrderBookSnapshotResult.Reject(
                fetched.RejectionReason ?? SignalReasonCodes.MissingOrderBookRestMissing,
                ClobBookSource);
        }

        return PaperLiveShadowOrderBookSnapshotResult.Reject(
            lookup.Status == OrderBookCacheLookupStatus.Stale
                ? SignalReasonCodes.MissingOrderBookCacheStale
                : SignalReasonCodes.MissingOrderBookCacheMiss,
            WebSocketCacheSource);
    }

    private static BtcMinimumStakeSizing CreateLimitMinimumStakeSizing(
        OrderBookSnapshot orderBook,
        decimal limitPrice,
        decimal stakeMultiplier,
        string source)
    {
        if (stakeMultiplier <= 0m)
        {
            return BtcMinimumStakeSizing.Reject("invalid_stake_multiplier", stakeMultiplier, Source: source);
        }

        if (limitPrice <= 0m || limitPrice >= 1m)
        {
            return BtcMinimumStakeSizing.Reject("invalid_limit_price", stakeMultiplier, Source: source);
        }

        if (orderBook.MinOrderSize is not > 0m)
        {
            return BtcMinimumStakeSizing.FallbackFixedStake(stakeMultiplier, limitPrice, source);
        }

        var rawTargetNotionalUsd = orderBook.MinOrderSize.Value * limitPrice * MinimumStakeSafetyMultiplier * stakeMultiplier;
        var roundedTargetNotionalUsd = RoundStakeNotionalUsd(rawTargetNotionalUsd);
        var targetSizeShares = RoundUpToClobLimitSizeShares(roundedTargetNotionalUsd, limitPrice);
        var targetNotionalUsd = targetSizeShares * limitPrice;
        var immediateExecutableAsk = GetBuyExecutableAskSummary(orderBook, limitPrice, targetSizeShares);
        return new BtcMinimumStakeSizing(
            Available: true,
            RejectionReason: null,
            Source: source,
            StakeMultiplier: stakeMultiplier,
            SafetyMultiplier: MinimumStakeSafetyMultiplier,
            RoundingMode: StakeNotionalRoundingMode,
            MinOrderSize: orderBook.MinOrderSize,
            MinimumNotionalUsd: orderBook.MinOrderSize.Value * limitPrice,
            RawTargetNotionalUsd: rawTargetNotionalUsd,
            TargetNotionalUsd: targetNotionalUsd,
            TargetSizeShares: targetSizeShares,
            ReferencePrice: limitPrice,
            LevelsUsed: 0,
            PaperGtdSnapshotAtUtc: orderBook.SnapshotAtUtc,
            PaperGtdBestBid: orderBook.BestBid,
            PaperGtdBestAsk: orderBook.BestAsk,
            PaperGtdLastTradePrice: orderBook.LastTradePrice,
            PaperGtdQueueAheadShares: GetBuyQueueAheadShares(orderBook, limitPrice),
            PaperGtdImmediateExecutableAskShares: immediateExecutableAsk.Shares,
            PaperGtdImmediateExecutableAskVwap: immediateExecutableAsk.Vwap);
    }

    private static decimal GetBuyQueueAheadShares(OrderBookSnapshot orderBook, decimal limitPrice)
    {
        return orderBook.Bids
            .Where(level => level is { Price: > 0m, Size: > 0m } && level.Price >= limitPrice)
            .Sum(level => level.Size);
    }

    private static (decimal Shares, decimal? Vwap) GetBuyExecutableAskSummary(
        OrderBookSnapshot orderBook,
        decimal limitPrice,
        decimal targetSizeShares)
    {
        var shares = 0m;
        var notional = 0m;
        foreach (var level in orderBook.Asks
            .Where(level => level is { Price: > 0m, Size: > 0m } && level.Price <= limitPrice)
            .OrderBy(level => level.Price))
        {
            if (shares >= targetSizeShares)
            {
                break;
            }

            var takeShares = targetSizeShares > 0m
                ? Math.Min(targetSizeShares - shares, level.Size)
                : level.Size;
            if (takeShares <= 0m)
            {
                continue;
            }

            shares += takeShares;
            notional += takeShares * level.Price;
        }

        return shares <= 0m ? (0m, null) : (shares, notional / shares);
    }

    private static BtcMinimumStakeSizing CreateLimitMinimumStakeSizingFromMinOrderSize(
        decimal minOrderSize,
        decimal limitPrice,
        decimal stakeMultiplier,
        string source)
    {
        if (stakeMultiplier <= 0m)
        {
            return BtcMinimumStakeSizing.Reject("invalid_stake_multiplier", stakeMultiplier, Source: source);
        }

        if (limitPrice <= 0m || limitPrice >= 1m)
        {
            return BtcMinimumStakeSizing.Reject("invalid_limit_price", stakeMultiplier, Source: source);
        }

        var rawTargetNotionalUsd = minOrderSize * limitPrice * MinimumStakeSafetyMultiplier * stakeMultiplier;
        var roundedTargetNotionalUsd = RoundStakeNotionalUsd(rawTargetNotionalUsd);
        var targetSizeShares = RoundUpToClobLimitSizeShares(roundedTargetNotionalUsd, limitPrice);
        var targetNotionalUsd = targetSizeShares * limitPrice;
        return new BtcMinimumStakeSizing(
            Available: true,
            RejectionReason: null,
            Source: source,
            StakeMultiplier: stakeMultiplier,
            SafetyMultiplier: MinimumStakeSafetyMultiplier,
            RoundingMode: StakeNotionalRoundingMode,
            MinOrderSize: minOrderSize,
            MinimumNotionalUsd: minOrderSize * limitPrice,
            RawTargetNotionalUsd: rawTargetNotionalUsd,
            TargetNotionalUsd: targetNotionalUsd,
            TargetSizeShares: targetSizeShares,
            ReferencePrice: limitPrice,
            LevelsUsed: 0);
    }

    private static BtcMinimumStakeSizing CreateLiveMinimumStakeSizing(
        OrderBookSnapshot? orderBook,
        decimal limitPrice,
        decimal stakeMultiplier)
    {
        if (stakeMultiplier <= 0m)
        {
            return BtcMinimumStakeSizing.Reject("invalid_stake_multiplier", stakeMultiplier, Source: ClobBookSource);
        }

        if (limitPrice <= 0m || limitPrice >= 1m)
        {
            return BtcMinimumStakeSizing.Reject("invalid_limit_price", stakeMultiplier, Source: ClobBookSource);
        }

        if (orderBook is null)
        {
            return BtcMinimumStakeSizing.Reject(SignalReasonCodes.MissingOrderBook, stakeMultiplier, Source: ClobBookSource);
        }

        return CreateLimitMinimumStakeSizing(orderBook, limitPrice, stakeMultiplier, ClobBookSource);
    }

    private static string AttachOpeningLimitStakeSizingJson(
        string rawDecisionJson,
        decimal stakeMultiplier,
        BtcMinimumStakeSizing sizing,
        OpeningLimitExpirationDecision expiration)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(rawDecisionJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root["pricing_mode"] = OpeningLimitPricingMode;
        root["order_execution_mode"] = OpeningLimitOrderType;
        root["order_type"] = OpeningLimitOrderType;
        root["post_only"] = false;
        root["order_ttl_seconds"] = expiration.LocalTtlSeconds;
        root["configured_order_ttl_seconds"] = expiration.ConfiguredTtlSeconds;
        root["gtd_expiration_mode"] = expiration.Mode;
        root["market_end_expire_before_seconds"] = expiration.MarketEndExpireBeforeSeconds;
        root["clob_gtd_expiration_security_buffer_seconds"] = expiration.ClobSecurityBufferSeconds;
        root["gtd_expiration_utc"] = expiration.LocalExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture);
        root["cancel_deadline_utc"] = expiration.LocalExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture);
        root["clob_wire_gtd_expiration_utc"] = expiration.ClobGtdExpirationUtc?.ToString("O", CultureInfo.InvariantCulture);
        root["stake_multiplier"] = stakeMultiplier;
        root["minimum_stake_safety_multiplier"] = MinimumStakeSafetyMultiplier;
        root["stake_sizing_source"] = sizing.Source;
        root["min_order_size"] = sizing.MinOrderSize;
        root["minimum_notional_usd"] = sizing.MinimumNotionalUsd;
        root["raw_target_notional_usd"] = sizing.RawTargetNotionalUsd;
        root["stake_notional_rounding"] = sizing.RoundingMode;
        root["target_notional_usd"] = sizing.TargetNotionalUsd;
        root["target_size_shares"] = sizing.TargetSizeShares;
        root["stake_sizing_rejection_reason"] = sizing.RejectionReason;
        root["paper_gtd_initial_snapshot_at_utc"] = sizing.PaperGtdSnapshotAtUtc?.ToString("O", CultureInfo.InvariantCulture);
        root["paper_gtd_initial_best_bid"] = sizing.PaperGtdBestBid;
        root["paper_gtd_initial_best_ask"] = sizing.PaperGtdBestAsk;
        root["paper_gtd_initial_last_trade_price"] = sizing.PaperGtdLastTradePrice;
        root["paper_gtd_initial_queue_ahead_shares"] = sizing.PaperGtdQueueAheadShares;
        root["paper_gtd_initial_executable_ask_shares"] = sizing.PaperGtdImmediateExecutableAskShares;
        root["paper_gtd_initial_executable_ask_vwap"] = sizing.PaperGtdImmediateExecutableAskVwap;
        return root.ToJsonString();
    }

    private static string AttachPaperLiveShadowDecisionJson(
        string rawDecisionJson,
        Guid? correlationId,
        int? quoteAgeMs,
        OrderBookSnapshot? orderBook,
        string? rejectionReason,
        string source,
        OpeningLimitExpirationDecision expiration)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(rawDecisionJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root["source"] = source;
        root["paper_live_shadow_test"] = true;
        root["correlation_id"] = correlationId?.ToString();
        root["order_type"] = OpeningLimitOrderType;
        root["order_execution_mode"] = OpeningLimitOrderType;
        root["post_only"] = false;
        root["order_ttl_seconds"] = expiration.LocalTtlSeconds;
        root["configured_order_ttl_seconds"] = expiration.ConfiguredTtlSeconds;
        root["gtd_expiration_mode"] = expiration.Mode;
        root["market_end_expire_before_seconds"] = expiration.MarketEndExpireBeforeSeconds;
        root["clob_gtd_expiration_security_buffer_seconds"] = expiration.ClobSecurityBufferSeconds;
        root["gtd_expiration_utc"] = expiration.LocalExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture);
        root["cancel_deadline_utc"] = expiration.LocalExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture);
        root["clob_wire_gtd_expiration_utc"] = expiration.ClobGtdExpirationUtc?.ToString("O", CultureInfo.InvariantCulture);
        root["quote_age_ms"] = quoteAgeMs;
        root["snapshot_at_utc"] = orderBook is null
            ? null
            : orderBook.SnapshotAtUtc.ToString("O", CultureInfo.InvariantCulture);
        root["best_bid"] = orderBook?.BestBid;
        root["best_ask"] = orderBook?.BestAsk;
        root["spread"] = orderBook?.SpreadAbs;
        root["tick_size"] = orderBook?.TickSize;
        root["min_order_size"] = orderBook?.MinOrderSize;
        root["shadow_rejection_reason"] = rejectionReason;
        if (!string.IsNullOrWhiteSpace(rejectionReason))
        {
            root["skip_reason"] = rejectionReason;
        }

        return root.ToJsonString();
    }

    private static string AttachFixedOpeningLimitPricingJson(
        string rawDecisionJson,
        decimal limitPrice)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(rawDecisionJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root["opening_limit_price_mode"] = "fixed";
        root["fixed_limit_price"] = limitPrice;
        root["limit_price"] = limitPrice;
        root["break_even_pricing_enabled"] = false;
        root["opening_limit_pricing_rejection_reason"] = null;
        return root.ToJsonString();
    }

    private static string AttachConvertedTakerGtdPricingJson(
        string rawDecisionJson,
        decimal limitPrice,
        string source,
        string evidence)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(rawDecisionJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        if (root.TryGetPropertyValue("pricing_mode", out var pricingMode))
        {
            root["pre_gtd_pricing_mode"] = pricingMode?.ToString();
        }

        if (root.TryGetPropertyValue("order_execution_mode", out var orderExecutionMode))
        {
            root["pre_gtd_order_execution_mode"] = orderExecutionMode?.ToString();
        }

        root["opening_limit_price_mode"] = "selected_entry_quote_price";
        root["limit_price"] = limitPrice;
        root["break_even_pricing_enabled"] = false;
        root["opening_limit_pricing_rejection_reason"] = null;
        root["gtd_limit_source"] = source;
        root["converted_to_gtd_limit_order"] = true;
        root["quote_evidence"] = evidence;
        return root.ToJsonString();
    }

    private static string AttachEntryPriceCapOpeningLimitPricingJson(
        string rawDecisionJson,
        decimal strategyEntryPriceCap,
        decimal tickSize,
        decimal? LimitPrice,
        string? RejectionReason)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(rawDecisionJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root["opening_limit_price_mode"] = "strategy_entry_price_cap";
        root["strategy_entry_price_cap"] = strategyEntryPriceCap;
        root["raw_limit_price"] = strategyEntryPriceCap;
        root["max_limit_price"] = 1m;
        root["tick_size"] = tickSize;
        root["limit_price"] = LimitPrice;
        root["break_even_pricing_enabled"] = false;
        root["opening_limit_pricing_rejection_reason"] = RejectionReason;
        return root.ToJsonString();
    }

    private static string AttachCleverOpeningLimitPricingJson(
        string rawDecisionJson,
        decimal rawLimitPrice,
        decimal maxLimitPrice,
        decimal tickSize,
        decimal? LimitPrice,
        string? RejectionReason)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(rawDecisionJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root["opening_limit_price_mode"] = "binance_clever_fair_value";
        root["limit_pricing_mode"] = "binance_clever_fair_value";
        root["clever_raw_limit_price"] = rawLimitPrice;
        root["opening_limit_max_price"] = maxLimitPrice;
        root["limit_price_tick_size"] = tickSize;
        root["limit_price_rounding"] = "floor_to_tick";
        root["limit_price"] = LimitPrice;
        root["break_even_pricing_enabled"] = false;
        root["opening_limit_pricing_rejection_reason"] = RejectionReason;
        root["limit_pricing_rejection_reason"] = RejectionReason;
        if (!string.IsNullOrWhiteSpace(RejectionReason))
        {
            root["skip_reason"] = RejectionReason;
        }

        return root.ToJsonString();
    }

    private static string SerializePaperLiveShadowOrderBookSnapshot(
        OrderBookSnapshot orderBook,
        string source,
        TimeSpan? age)
    {
        return JsonSerializer.Serialize(new
        {
            source,
            age_ms = age is null ? null : (int?)Math.Max(0, (int)Math.Round(age.Value.TotalMilliseconds)),
            asset_id = orderBook.AssetId,
            condition_id = orderBook.ConditionId,
            snapshot_at_utc = orderBook.SnapshotAtUtc,
            best_bid = orderBook.BestBid,
            best_ask = orderBook.BestAsk,
            spread = orderBook.SpreadAbs,
            min_order_size = orderBook.MinOrderSize,
            tick_size = orderBook.TickSize,
            negative_risk = orderBook.NegativeRisk,
            last_trade_price = orderBook.LastTradePrice,
            bids = orderBook.Bids.Take(20).Select(level => new { price = level.Price, size = level.Size }).ToArray(),
            asks = orderBook.Asks.Take(20).Select(level => new { price = level.Price, size = level.Size }).ToArray()
        });
    }

    private static string AttachOpeningLimitBreakEvenPricingJson(
        string rawDecisionJson,
        string PricingMode,
        int LookbackRuns,
        int MinSettledRuns,
        int SettledRuns,
        int Wins,
        decimal? WinRate,
        decimal Margin,
        decimal? RawLimitPrice,
        decimal MaxLimitPrice,
        decimal TickSize,
        decimal? LimitPrice,
        string? RejectionReason,
        string? BreakEvenInsufficientReason = null,
        BtcOpeningLimitBookBootstrapPriceDecision? BookBootstrapPricing = null)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(rawDecisionJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root["limit_pricing_mode"] = PricingMode;
        root["break_even_lookback_runs"] = LookbackRuns;
        root["break_even_min_settled_runs"] = MinSettledRuns;
        root["break_even_settled_runs"] = SettledRuns;
        root["break_even_wins"] = Wins;
        root["break_even_win_rate"] = WinRate;
        root["break_even_margin"] = Margin;
        root["break_even_raw_limit_price"] = RawLimitPrice;
        root["opening_limit_max_price"] = MaxLimitPrice;
        root["limit_price_tick_size"] = TickSize;
        root["limit_price_rounding"] = "floor_to_tick";
        root["limit_price"] = LimitPrice;
        root["limit_pricing_rejection_reason"] = RejectionReason;
        root["break_even_insufficient_reason"] = BreakEvenInsufficientReason;
        if (BookBootstrapPricing is not null)
        {
            root["book_bootstrap_source"] = BookBootstrapPricing.Source;
            root["book_bootstrap_quote_age_ms"] = BookBootstrapPricing.Age?.TotalMilliseconds;
            root["book_bootstrap_snapshot_at_utc"] = BookBootstrapPricing.OrderBook?.SnapshotAtUtc.ToString("O", CultureInfo.InvariantCulture);
            root["book_bootstrap_asset_id"] = BookBootstrapPricing.OrderBook?.AssetId;
            root["book_bootstrap_condition_id"] = BookBootstrapPricing.OrderBook?.ConditionId;
            root["book_bootstrap_best_bid"] = BookBootstrapPricing.BestBid;
            root["book_bootstrap_best_ask"] = BookBootstrapPricing.BestAsk;
            root["book_bootstrap_spread"] = BookBootstrapPricing.OrderBook?.SpreadAbs;
            root["book_bootstrap_tick_size"] = BookBootstrapPricing.TickSize;
            root["book_bootstrap_min_order_size"] = BookBootstrapPricing.OrderBook?.MinOrderSize;
            root["book_bootstrap_price_source"] = BookBootstrapPricing.PriceSource;
            root["book_bootstrap_raw_limit_price"] = BookBootstrapPricing.RawLimitPrice;
            root["book_bootstrap_rejection_reason"] = BookBootstrapPricing.RejectionReason;
        }

        if (!string.IsNullOrWhiteSpace(RejectionReason))
        {
            root["skip_reason"] = RejectionReason;
        }

        return root.ToJsonString();
    }

    private async Task<TakerOrderBookLookupResult> GetFreshTakerOrderBookAsync(
        string assetId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var maxAge = GetPaperTakerMaxQuoteAge();
        var lookup = marketDataCache.GetOrderBook(assetId, maxAge);
        if (lookup is { Status: OrderBookCacheLookupStatus.Fresh, Snapshot: { } cached } &&
            HasExecutableAskDepth(cached))
        {
            return TakerOrderBookLookupResult.Found(
                cached,
                WebSocketCacheSource,
                lookup.Age,
                CacheStatus: lookup.Status,
                CacheOrderBook: cached,
                CacheAge: lookup.Age);
        }

        if (options.PaperTakerRestFallbackEnabled)
        {
            var restLookup = await GetFreshRestTakerOrderBookAsync(assetId, nowUtc, cancellationToken);
            return restLookup with
            {
                RestAttempted = true,
                CacheStatus = lookup.Status,
                CacheOrderBook = lookup.Snapshot,
                CacheAge = lookup.Age
            };
        }

        return lookup.Status switch
        {
            OrderBookCacheLookupStatus.Stale => TakerOrderBookLookupResult.Reject(
                SignalReasonCodes.MissingOrderBookCacheStale,
                lookup.Snapshot,
                WebSocketCacheSource,
                lookup.Age,
                CacheStatus: lookup.Status,
                CacheOrderBook: lookup.Snapshot,
                CacheAge: lookup.Age),
            OrderBookCacheLookupStatus.Missing => TakerOrderBookLookupResult.Reject(
                SignalReasonCodes.MissingOrderBookCacheMiss,
                source: WebSocketCacheSource,
                CacheStatus: lookup.Status),
            _ => TakerOrderBookLookupResult.Reject(
                SignalReasonCodes.MissingOrderBookEmptySide,
                lookup.Snapshot,
                WebSocketCacheSource,
                lookup.Age,
                CacheStatus: lookup.Status,
                CacheOrderBook: lookup.Snapshot,
                CacheAge: lookup.Age)
        };
    }

    private async Task<TakerOrderBookLookupResult> GetFreshRestTakerOrderBookAsync(
        string assetId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var fetched = await FetchAndCacheOrderBookAsync(assetId, cancellationToken, stampWithLocalReceiveTime: true);
        if (fetched.RejectionReason is not null || fetched.OrderBook is null)
        {
            return TakerOrderBookLookupResult.Reject(
                fetched.RejectionReason ?? SignalReasonCodes.MissingOrderBookRestMissing,
                source: ClobBookSource,
                RestAttempted: true);
        }

        var fetchedAge = GetSnapshotAge(fetched.OrderBook.SnapshotAtUtc);
        if (!HasExecutableAskDepth(fetched.OrderBook))
        {
            return TakerOrderBookLookupResult.Reject(
                SignalReasonCodes.MissingOrderBookEmptySide,
                fetched.OrderBook,
                ClobBookSource,
                fetchedAge,
                RestAttempted: true);
        }

        if (fetchedAge > GetPaperTakerMaxQuoteAge())
        {
            return TakerOrderBookLookupResult.Reject(
                SignalReasonCodes.MissingOrderBookCacheStale,
                fetched.OrderBook,
                ClobBookSource,
                fetchedAge,
                RestAttempted: true);
        }

        return TakerOrderBookLookupResult.Found(
            fetched.OrderBook,
            ClobBookSource,
            fetchedAge,
            RestAttempted: true);
    }

    private TimeSpan GetPaperTakerMaxQuoteAge()
    {
        var maxAge = TimeSpan.FromMilliseconds(options.PaperTakerMaxQuoteAgeMilliseconds);
        if (marketDataWebSocketOptions.StaleAfterSeconds <= 0)
        {
            return maxAge;
        }

        return TimeSpan.FromMilliseconds(Math.Min(
            maxAge.TotalMilliseconds,
            TimeSpan.FromSeconds(marketDataWebSocketOptions.StaleAfterSeconds).TotalMilliseconds));
    }

    private static TimeSpan GetSnapshotAge(DateTimeOffset snapshotAtUtc)
    {
        var age = DateTimeOffset.UtcNow - snapshotAtUtc;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }

    private async Task<OrderBookFetchResult> FetchAndCacheOrderBookAsync(
        string assetId,
        CancellationToken cancellationToken,
        bool stampWithLocalReceiveTime = false)
    {
        try
        {
            var orderBook = await clobClient.GetOrderBookAsync(assetId, cancellationToken);
            if (orderBook is null)
            {
                return new OrderBookFetchResult(null, SignalReasonCodes.MissingOrderBookRestMissing);
            }

            var normalizedOrderBook = NormalizeOrderBook(assetId, orderBook);
            if (stampWithLocalReceiveTime)
            {
                normalizedOrderBook = normalizedOrderBook with { SnapshotAtUtc = DateTimeOffset.UtcNow };
            }

            var update = ToOrderBookMarketDataUpdate(normalizedOrderBook);
            marketDataCache.ApplyUpdate(update);
            activeMarketAssetSubscriptionRegistry.ApplyMarketDataUpdate(update);
            return new OrderBookFetchResult(normalizedOrderBook, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PolymarketApiException ex) when (IsMissingOrderBook(ex))
        {
            logger.LogInformation(
                "CLOB /book returned no order book for BTC 5m token. TokenId={TokenId} Message={Message}",
                assetId,
                ex.Message);
            return new OrderBookFetchResult(null, SignalReasonCodes.MissingOrderBookRestNotFound);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CLOB /book request failed for BTC 5m token. TokenId={TokenId}", assetId);
            await TryRecordApiErrorAsync("GetOrderBook", ex.Message, cancellationToken);
            return new OrderBookFetchResult(null, SignalReasonCodes.MissingOrderBookRestMissing);
        }
    }

    private async Task TryPersistOrderBookSnapshotAsync(
        OrderBookSnapshot snapshot,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddOrderBookSnapshotAsync(snapshot, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist BTC close-book order book snapshot. Operation={Operation} AssetId={AssetId}",
                operation,
                snapshot.AssetId);
            await TryRecordApiErrorAsync(operation, ex.Message, cancellationToken);
        }
    }

    private static OrderBookSnapshot NormalizeOrderBook(string requestedAssetId, OrderBookSnapshot orderBook)
    {
        return string.IsNullOrWhiteSpace(orderBook.AssetId) ||
            !string.Equals(orderBook.AssetId, requestedAssetId, StringComparison.OrdinalIgnoreCase)
            ? orderBook with { AssetId = requestedAssetId }
            : orderBook;
    }

    private static decimal? TryGetBestAskFromOrderBook(OrderBookCacheLookup lookup)
    {
        if (lookup is not { Status: OrderBookCacheLookupStatus.Fresh, Snapshot: { } snapshot })
        {
            return null;
        }

        var usableAsks = snapshot.Asks
            .Where(level => level.Size > 0m && IsUsableBestAsk(level.Price))
            .ToArray();
        return usableAsks.Length == 0
            ? null
            : usableAsks.Min(level => level.Price);
    }

    private static decimal? TryGetBestAskFromOrderBook(OrderBookSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var usableAsks = snapshot.Asks
            .Where(level => level.Size > 0m && IsUsableBestAsk(level.Price))
            .ToArray();
        return usableAsks.Length == 0
            ? null
            : usableAsks.Min(level => level.Price);
    }

    private static decimal? TryGetBestBidFromOrderBook(OrderBookSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var usableBids = snapshot.Bids
            .Where(level => level.Size > 0m && IsUsableBestBid(level.Price))
            .ToArray();
        return usableBids.Length == 0
            ? null
            : usableBids.Max(level => level.Price);
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

    private static bool IsMissingOrderBook(PolymarketApiException ex)
    {
        return ex.Message.Contains("No orderbook exists", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase);
    }

    private decimal? TryGetFreshBestAskFromActiveSnapshot(
        string assetId,
        TimeSpan maxAge,
        DateTimeOffset nowUtc)
    {
        if (!activeMarketAssetSubscriptionRegistry.TryGetSnapshot(assetId, out var snapshot) ||
            !snapshot.AllowsOrders ||
            !IsUsableBestAsk(snapshot.BestAsk) ||
            snapshot.OrderBookUpdatedAtUtc is not { } updatedAtUtc ||
            nowUtc - updatedAtUtc > maxAge)
        {
            return null;
        }

        return snapshot.BestAsk;
    }

    private static bool IsUsableBestAsk(decimal? price)
    {
        return price is > 0m and <= 1m;
    }

    private static bool IsUsableBestBid(decimal? price)
    {
        return price is >= 0m and < 1m;
    }

    private static bool HasExecutableAskDepth(OrderBookSnapshot snapshot)
    {
        return snapshot.Asks.Any(level => level.Price is > 0m and <= 1m && level.Size > 0m);
    }

    private static DateTimeOffset? GetEntryDueAtUtc(
        DateTimeOffset? marketStartUtc,
        BtcUpDown5mStrategyVariant variant)
    {
        return marketStartUtc?.AddSeconds(variant.EntryDelaySeconds);
    }

    private static double? GetDecisionDelayMilliseconds(DateTimeOffset? entryDueAtUtc, DateTimeOffset nowUtc)
    {
        return entryDueAtUtc is null ? null : (nowUtc - entryDueAtUtc.Value).TotalMilliseconds;
    }

    private static string BuildGammaPaperEntryRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        decimal sizeShares,
        DateTimeOffset nowUtc)
    {
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        return JsonSerializer.Serialize(new
        {
            pricing_mode = "gamma_outcome_price",
            strategy_code = variant.Code,
            outcome_selection_source = GammaOutcomePriceSource,
            quote_received_at_utc = nowUtc,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            condition_id = market.ConditionId,
            asset_id = outcome.AssetId,
            outcome = outcome.Outcome,
            gamma_outcome_price = outcome.Price,
            gamma_fetched_at_utc = market.FetchedAtUtc,
            target_notional_usd = targetNotionalUsd,
            estimated_fill_price = outcome.Price,
            estimated_fill_shares = sizeShares,
            estimated_fill_notional = targetNotionalUsd
        });
    }

    private static string BuildGammaEntryPriceCapOpeningLimitRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        string? reason)
    {
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = OpeningLimitOrderType,
            post_only = false,
            strategy_code = variant.Code,
            decision_source = GammaOutcomePriceSource,
            outcome_selection_source = GammaOutcomePriceSource,
            quote_received_at_utc = nowUtc,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            condition_id = market.ConditionId,
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            gamma_outcome_price = selectedOutcome?.Price,
            gamma_fetched_at_utc = market.FetchedAtUtc,
            target_notional_usd = targetNotionalUsd,
            strategy_entry_price_cap = TryGetStandardEntryPriceCap(variant),
            skip_reason = reason
        });
    }

    private static string BuildMiddleReferenceRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        BtcUsdReferencePriceSnapshot snapshot,
        BtcUsdReferencePricePoint? currentPrice,
        int requiredCachedSamples,
        IReadOnlyList<BtcUsdReferencePricePoint> cachedSamples,
        BtcPriceDirection? baseSelectedDirection,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason)
    {
        var limitPrice = GetBinanceStartRelativeLimitPrice(variant);
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = OpeningLimitOrderType,
            post_only = false,
            strategy_code = variant.Code,
            decision_source = IsMiddleReferenceRevert(variant)
                ? "binance_trade_stream_middle_reference_revert"
                : "binance_trade_stream_middle_reference",
            revert_decision = IsMiddleReferenceRevert(variant),
            decision_depth = variant.DecisionDepth,
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            btc_current_price_usd = currentPrice?.PriceUsd,
            btc_current_source_updated_at_utc = currentPrice?.SourceUpdatedAtUtc,
            btc_current_fetched_at_utc = currentPrice?.FetchedAtUtc,
            reference_source = snapshot.Source,
            reference_window_size = snapshot.WindowSize,
            reference_sample_count = snapshot.SampleCount,
            reference_is_full_window = snapshot.IsFullWindow,
            reference_arithmetic_mean_usd = snapshot.ArithmeticMeanUsd,
            required_cached_samples = requiredCachedSamples,
            cached_samples_used = cachedSamples
                .Select(sample => new
                {
                    price_usd = sample.PriceUsd,
                    source_updated_at_utc = sample.SourceUpdatedAtUtc,
                    fetched_at_utc = sample.FetchedAtUtc
                })
                .ToArray(),
            base_selected_direction = baseSelectedDirection?.ToString(),
            selected_direction = selectedDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            limit_price = limitPrice,
            target_notional_usd = targetNotionalUsd,
            target_size_shares = targetNotionalUsd / limitPrice,
            skip_reason = reason
        });
    }

    private static string BuildAlwaysDirectionRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        BtcPriceDirection selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason)
    {
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var limitPrice = GetFixedDirectionLimitPrice(variant);
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = OpeningLimitOrderType,
            post_only = false,
            strategy_code = variant.Code,
            decision_source = GetFixedDirectionDecisionSource(variant, selectedDirection),
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            strategy_category = variant.Category,
            market_interval = variant.MarketInterval.ToString(),
            preopen_lifetime_mode = variant.PreOpenLifetimeMode.ToString(),
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            selected_direction = selectedDirection.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            limit_price = limitPrice,
            target_notional_usd = targetNotionalUsd,
            target_size_shares = limitPrice > 0m ? targetNotionalUsd / limitPrice : (decimal?)null,
            skip_reason = reason
        });
    }

    private static string GetFixedDirectionDecisionSource(
        BtcUpDown5mStrategyVariant variant,
        BtcPriceDirection selectedDirection)
    {
        if (IsPreOpenFixedDirectionOpeningLimitEntry(variant))
        {
            return selectedDirection == BtcPriceDirection.Up
                ? "fixed_up_preopen"
                : "fixed_down_preopen";
        }

        return selectedDirection == BtcPriceDirection.Up
            ? "always_up_after_trading_started"
            : "always_down_after_trading_started";
    }

    private static string BuildBinanceStartRelativeRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        BtcUsdReferencePricePoint? currentPrice,
        decimal? startPrice,
        BtcPriceDirection? baseSelectedDirection,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason)
    {
        const decimal limitPrice = 0.50m;
        var moveUsd = currentPrice is not null && startPrice is { } start
            ? currentPrice.PriceUsd - start
            : (decimal?)null;
        var moveBps = moveUsd is { } move && startPrice is > 0m
            ? move / startPrice.Value * 10_000m
            : (decimal?)null;
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = OpeningLimitOrderType,
            post_only = false,
            strategy_code = variant.Code,
            decision_source = "binance_trade_stream_market_start_relative",
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            btc_current_price_usd = currentPrice?.PriceUsd,
            btc_current_source_updated_at_utc = currentPrice?.SourceUpdatedAtUtc,
            btc_current_fetched_at_utc = currentPrice?.FetchedAtUtc,
            btc_current_source = currentPrice?.Source,
            btc_start_price_usd = startPrice,
            btc_move_from_start_usd = moveUsd,
            btc_move_from_start_bps = moveBps,
            btc_abs_move_from_start_bps = moveBps is { } bps ? Math.Abs(bps) : (decimal?)null,
            btc_min_move_from_start_bps = GetBinanceStartRelativeMinMoveBps(variant),
            base_selected_direction = baseSelectedDirection?.ToString(),
            selected_direction = selectedDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            limit_price = limitPrice,
            target_notional_usd = targetNotionalUsd,
            target_size_shares = targetNotionalUsd / limitPrice,
            skip_reason = reason
        });
    }

    private static string BuildBinanceCleverRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        BtcUsdReferencePricePoint? currentPrice,
        decimal? startPrice,
        BtcPriceDirection? baseSelectedDirection,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        BtcCleverFairValueEstimate? estimate,
        decimal edgeMargin,
        string? reason)
    {
        var moveUsd = currentPrice is not null && startPrice is { } start
            ? currentPrice.PriceUsd - start
            : (decimal?)null;
        var moveBps = moveUsd is { } move && startPrice is > 0m
            ? move / startPrice.Value * 10_000m
            : (decimal?)null;
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = OpeningLimitOrderType,
            post_only = false,
            strategy_code = variant.Code,
            decision_source = "binance_trade_stream_market_start_relative_clever",
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            btc_current_price_usd = currentPrice?.PriceUsd,
            btc_current_source_updated_at_utc = currentPrice?.SourceUpdatedAtUtc,
            btc_current_fetched_at_utc = currentPrice?.FetchedAtUtc,
            btc_current_source = currentPrice?.Source,
            btc_start_price_usd = startPrice,
            btc_move_from_start_usd = moveUsd,
            btc_move_from_start_bps = moveBps,
            base_selected_direction = baseSelectedDirection?.ToString(),
            selected_direction = selectedDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            fair_value_model = "archive_weighted_knn_v1",
            fair_value_lookback_ticks = BinanceCleverFairValueLookbackTicks,
            fair_value_min_samples = BinanceCleverFairValueMinSamples,
            fair_value_edge_margin = edgeMargin,
            fair_value_move_scale_bps = BinanceCleverMoveScaleBps,
            fair_value_time_scale_seconds = BinanceCleverTimeScaleSeconds,
            fair_value_candidate_samples = estimate?.CandidateSamples,
            fair_value_weight_sum = estimate?.WeightSum,
            fair_value_price = estimate?.FairValuePrice,
            fair_value_adjusted_price = estimate?.AdjustedFairValuePrice,
            fair_value_raw_limit_price = estimate?.RawLimitPrice,
            fair_value_limit_price = estimate?.LimitPrice,
            fair_value_current_target_price = estimate?.CurrentTargetPrice,
            fair_value_current_target_proxy_kind = estimate?.CurrentTargetPriceProxyKind,
            fair_value_current_target_spread = estimate?.CurrentTargetSpread,
            fair_value_current_target_book_source = estimate?.CurrentTargetBookSource,
            fair_value_current_target_book_age_ms = estimate?.CurrentTargetBookAgeMs,
            fair_value_current_liquidity_discount = estimate?.CurrentLiquidityDiscount,
            fair_value_average_distance = estimate?.AverageDistance,
            fair_value_current_aligned_move_bps = estimate?.CurrentAlignedMoveBps,
            fair_value_current_seconds_to_close = estimate?.CurrentSecondsToClose,
            limit_price = estimate?.LimitPrice,
            target_notional_usd = targetNotionalUsd,
            target_size_shares = estimate?.LimitPrice is > 0m
                ? targetNotionalUsd / estimate.LimitPrice.Value
                : (decimal?)null,
            skip_reason = reason
        });
    }

    private static string BuildEnsembleVoteRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        int requiredVotes,
        IReadOnlyList<BtcOpeningLimitSignalVote> votes,
        int upVotes,
        int downVotes,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason)
    {
        const decimal limitPrice = 0.50m;
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = OpeningLimitOrderType,
            post_only = false,
            strategy_code = variant.Code,
            decision_source = "ensemble_vote_2_of_3",
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            required_votes = requiredVotes,
            up_votes = upVotes,
            down_votes = downVotes,
            votes = votes.Select(vote => new
            {
                strategy_code = vote.StrategyCode,
                entered = vote.ShouldEnter,
                skip_reason = vote.SkipReason,
                selected_direction = vote.Direction?.ToString(),
                outcome = vote.Outcome,
                asset_id = vote.AssetId,
                limit_price_override = vote.LimitPriceOverride
            }).ToArray(),
            selected_direction = selectedDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            limit_price = limitPrice,
            target_notional_usd = targetNotionalUsd,
            target_size_shares = targetNotionalUsd / limitPrice,
            skip_reason = reason
        });
    }

    private static string BuildDynamicMarkovRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        int lookback,
        int minTransitions,
        decimal threshold,
        IReadOnlyList<BtcUpDown5mMarketResult> results,
        string? previousOutcome,
        int matchingTransitions,
        decimal? upProbability,
        decimal? downProbability,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason)
    {
        const decimal limitPrice = 0.50m;
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = OpeningLimitOrderType,
            post_only = false,
            strategy_code = variant.Code,
            decision_source = "btc_result_markov_transition",
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            lookback_results = lookback,
            min_matching_transitions = minTransitions,
            decision_probability_threshold = threshold,
            observed_results = results.Count,
            previous_outcome = previousOutcome,
            matching_transitions = matchingTransitions,
            up_probability = upProbability,
            down_probability = downProbability,
            selected_direction = selectedDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            recent_results = results
                .TakeLast(Math.Min(results.Count, 20))
                .Select(result => new
                {
                    market_id = result.MarketId,
                    market_start_utc = result.MarketStartUtc,
                    winning_outcome = result.WinningOutcome,
                    settled_at_utc = result.SettledAtUtc
                })
                .ToArray(),
            limit_price = limitPrice,
            target_notional_usd = targetNotionalUsd,
            target_size_shares = targetNotionalUsd / limitPrice,
            skip_reason = reason
        });
    }

    private static string BuildStrategySelectorRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        int lookback,
        int minSamples,
        IReadOnlyList<BtcStrategySelectorCandidateStats> candidates,
        BtcStrategySelectorCandidateStats? selectedCandidate,
        BtcOpeningLimitDecision? candidateDecision,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason)
    {
        const decimal limitPrice = 0.50m;
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = OpeningLimitOrderType,
            post_only = false,
            strategy_code = variant.Code,
            decision_source = "recent_paper_strategy_selector",
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            lookback_runs = lookback,
            min_settled_samples = minSamples,
            candidates = candidates.Select(candidate => new
            {
                strategy_code = candidate.Variant.Code,
                settled_runs = candidate.SettledRuns,
                wins = candidate.Wins,
                realized_pnl_usd = candidate.RealizedPnlUsd,
                average_pnl_usd = candidate.AveragePnlUsd,
                roi = candidate.Roi
            }).ToArray(),
            selected_candidate_strategy_code = selectedCandidate?.Variant.Code,
            selected_candidate_settled_runs = selectedCandidate?.SettledRuns,
            selected_candidate_average_pnl_usd = selectedCandidate?.AveragePnlUsd,
            selected_candidate_roi = selectedCandidate?.Roi,
            selected_candidate_skip_reason = candidateDecision?.SkipReason,
            selected_candidate_limit_price_override = candidateDecision?.LimitPriceOverride,
            selected_direction = TryResolveDirectionFromOutcome(selectedOutcome?.Outcome)?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            limit_price = candidateDecision?.LimitPriceOverride ?? limitPrice,
            target_notional_usd = targetNotionalUsd,
            target_size_shares = candidateDecision?.LimitPriceOverride is > 0m
                ? targetNotionalUsd / candidateDecision.LimitPriceOverride.Value
                : targetNotionalUsd / limitPrice,
            candidate_raw_decision_json = candidateDecision?.RawDecisionJson,
            skip_reason = reason
        });
    }

    private static string BuildSkipConsecutiveResultsRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        int requiredResults,
        IReadOnlyList<BtcSkipMarketResult> results,
        BtcPriceDirection? baseSelectedDirection,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason)
    {
        return BuildSkipConsecutiveResultsRawDecisionJson(
            market,
            variant,
            targetNotionalUsd,
            nowUtc,
            requiredResults,
            results,
            baseSelectedDirection,
            selectedDirection,
            selectedOutcome,
            reason,
            closeBookDiagnostics: null);
    }

    private static string BuildSkipConsecutiveResultsRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        int requiredResults,
        IReadOnlyList<BtcSkipMarketResult> results,
        BtcPriceDirection? baseSelectedDirection,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason,
        IReadOnlyList<BtcSkipCloseBookDiagnostic>? closeBookDiagnostics)
    {
        const decimal limitPrice = 0.50m;
        var diagnostics = closeBookDiagnostics ?? [];
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var expectedPreviousMarketStarts = marketStartUtc is null
            ? Array.Empty<DateTimeOffset>()
            : GetExpectedPreviousBtc5mMarketStarts(marketStartUtc.Value, requiredResults);
        var usedMarketStartUnixTimes = results
            .Where(result => result.MarketStartUtc is not null)
            .Select(result => result.MarketStartUtc!.Value.ToUnixTimeSeconds())
            .ToHashSet();
        var missingPreviousMarketStarts = expectedPreviousMarketStarts
            .Where(expectedStart => !usedMarketStartUnixTimes.Contains(expectedStart.ToUnixTimeSeconds()))
            .ToArray();
        var secondsAfterMarketStart = marketStartUtc is null
            ? (double?)null
            : Math.Max(0d, (nowUtc - marketStartUtc.Value).TotalSeconds);
        var previousResultLags = results
            .Select(result => new
            {
                market_id = result.MarketId,
                market_slug = result.MarketSlug,
                market_start_utc = result.MarketStartUtc,
                market_end_utc = result.MarketEndUtc,
                result_at_utc = result.ResultAtUtc,
                result_source = result.ResultSource,
                result_lag_seconds = result.MarketEndUtc is null
                    ? (double?)null
                    : Math.Max(0d, (result.ResultAtUtc - result.MarketEndUtc.Value).TotalSeconds)
            })
            .ToArray();
        var closeBookDecisionSource = IsSkipConsecutiveMarketResultsRevert(variant)
            ? "clob_close_book_price_evidence_revert"
            : "clob_close_book_price_evidence";
        var closeBookDecisionSourceDetails = results
            .Select(result => result.ResultSource)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = OpeningLimitOrderType,
            post_only = false,
            strategy_code = variant.Code,
            decision_source = closeBookDecisionSource,
            decision_source_details = closeBookDecisionSourceDetails,
            revert_decision = IsSkipConsecutiveMarketResultsRevert(variant),
            decision_depth = variant.DecisionDepth,
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            decision_seconds_after_market_start = secondsAfterMarketStart,
            strict_previous_markets = true,
            strict_previous_window_minutes = 5,
            expected_previous_market_starts_utc = expectedPreviousMarketStarts,
            missing_previous_market_starts_utc = missingPreviousMarketStarts,
            required_consecutive_results = requiredResults,
            diagnostic_type = diagnostics.Count > 0
                ? "btc_skip_close_book_result_lookup"
                : null,
            close_book_result_diagnostics = diagnostics
                .Select(diagnostic => new
                {
                    expected_market_start_utc = diagnostic.ExpectedMarketStartUtc,
                    market_id = diagnostic.MarketId,
                    condition_id = diagnostic.ConditionId,
                    market_slug = diagnostic.MarketSlug,
                    market_end_utc = diagnostic.MarketEndUtc,
                    reason = diagnostic.Reason,
                    orderbook_unavailable = diagnostic.OrderBookUnavailable,
                    up_asset_id = diagnostic.UpAssetId,
                    down_asset_id = diagnostic.DownAssetId,
                    up_quote_source = diagnostic.UpQuoteSource,
                    up_lookup_reason = diagnostic.UpLookupReason,
                    up_best_bid = diagnostic.UpBestBid,
                    up_best_ask = diagnostic.UpBestAsk,
                    up_midpoint = diagnostic.UpMidpoint,
                    down_quote_source = diagnostic.DownQuoteSource,
                    down_lookup_reason = diagnostic.DownLookupReason,
                    down_best_bid = diagnostic.DownBestBid,
                    down_best_ask = diagnostic.DownBestAsk,
                    down_midpoint = diagnostic.DownMidpoint
                })
                .ToArray(),
            strict_previous_result_lags = previousResultLags,
            strict_previous_result_settlement_lags = previousResultLags,
            market_results_used = results
                .Select(result => new
                {
                    market_id = result.MarketId,
                    condition_id = result.ConditionId,
                    market_slug = result.MarketSlug,
                    market_start_utc = result.MarketStartUtc,
                    market_end_utc = result.MarketEndUtc,
                    winning_outcome = result.WinningOutcome,
                    result_source = result.ResultSource,
                    result_at_utc = result.ResultAtUtc,
                    up_asset_id = result.UpAssetId,
                    down_asset_id = result.DownAssetId,
                    up_best_bid = result.UpBestBid,
                    up_best_ask = result.UpBestAsk,
                    up_midpoint = result.UpMidpoint,
                    down_best_bid = result.DownBestBid,
                    down_best_ask = result.DownBestAsk,
                    down_midpoint = result.DownMidpoint,
                    inferred_up_price = result.InferredUpMidpoint,
                    inferred_up_midpoint = result.InferredUpMidpoint
                })
                .ToArray(),
            base_selected_direction = baseSelectedDirection?.ToString(),
            selected_direction = selectedDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            limit_price = limitPrice,
            target_notional_usd = targetNotionalUsd,
            target_size_shares = targetNotionalUsd / limitPrice,
            skip_reason = reason
        });
    }

    private static string BuildTakerPaperEntryRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        TakerOrderBookLookupResult orderBookLookup,
        TakerBuyFillEstimate estimate,
        decimal targetNotionalUsd,
        decimal stakeMultiplier,
        BtcMinimumStakeSizing sizing,
        decimal clobGammaDiff,
        DateTimeOffset nowUtc,
        IReadOnlyList<BtcTakerOutcomePricingSnapshot>? outcomeSelectionSnapshots)
    {
        var orderBook = orderBookLookup.OrderBook;
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var cacheOrderBook = orderBookLookup.CacheOrderBook;
        return JsonSerializer.Serialize(new
        {
            pricing_mode = "paper_taker_vwap",
            order_execution_mode = OpeningLimitOrderType,
            quote_pricing_mode = "executable_ask_depth_vwap",
            strategy_code = variant.Code,
            outcome_selection_source = GetTakerOutcomeSelectionSource(variant, outcomeSelectionSnapshots),
            source = orderBookLookup.Source,
            rest_attempted = orderBookLookup.RestAttempted,
            cache_status = orderBookLookup.CacheStatus?.ToString(),
            cache_quote_exchange_timestamp_utc = cacheOrderBook?.SnapshotAtUtc,
            cache_age_ms = orderBookLookup.CacheAge?.TotalMilliseconds,
            cache_best_bid = cacheOrderBook?.BestBid,
            cache_best_ask = cacheOrderBook?.BestAsk,
            cache_has_executable_ask_depth = cacheOrderBook is not null && HasExecutableAskDepth(cacheOrderBook),
            quote_received_at_utc = nowUtc,
            quote_exchange_timestamp_utc = orderBook?.SnapshotAtUtc,
            quote_age_ms = orderBookLookup.Age?.TotalMilliseconds,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            condition_id = market.ConditionId,
            asset_id = outcome.AssetId,
            outcome = outcome.Outcome,
            best_bid = estimate.BestBid,
            best_ask = estimate.BestAsk,
            spread = estimate.SpreadAbs,
            last_trade_price = orderBook?.LastTradePrice,
            tick_size = orderBook?.TickSize,
            min_order_size = orderBook?.MinOrderSize,
            strategy_entry_price_cap = TryGetStandardEntryPriceCap(variant),
            stake_multiplier = stakeMultiplier,
            minimum_stake_safety_multiplier = sizing.SafetyMultiplier,
            minimum_notional_usd = sizing.MinimumNotionalUsd,
            raw_target_notional_usd = sizing.RawTargetNotionalUsd,
            stake_notional_rounding = sizing.RoundingMode,
            target_notional_usd = targetNotionalUsd,
            target_size_shares = estimate.TargetSizeShares,
            max_allowed_price = estimate.MaxAllowedPrice,
            estimated_fill_price = estimate.AverageFillPrice,
            estimated_fill_shares = estimate.SizeShares,
            estimated_fill_notional = estimate.NotionalUsd,
            levels_used = estimate.LevelsUsed,
            gamma_outcome_price = outcome.Price,
            gamma_fetched_at_utc = market.FetchedAtUtc,
            clob_vs_gamma_diff = clobGammaDiff,
            outcome_selection_candidates = outcomeSelectionSnapshots?
                .Select(ToTakerOutcomePricingSnapshotJson)
                .ToArray(),
            asks = orderBook?.Asks
                .OrderBy(level => level.Price)
                .Take(20)
                .Select(level => new { price = level.Price, size = level.Size })
                .ToArray(),
            bids = orderBook?.Bids
                .OrderByDescending(level => level.Price)
                .Take(20)
                .Select(level => new { price = level.Price, size = level.Size })
                .ToArray()
        });
    }

    private string BuildTakerPaperRejectionDiagnosticsJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        string reason,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        IReadOnlyList<BtcTakerOutcomePricingSnapshot> snapshots)
    {
        return JsonSerializer.Serialize(new
        {
            diagnostic_type = "btc_taker_orderbook_rejection",
            pricing_mode = "paper_taker_vwap",
            order_execution_mode = OpeningLimitOrderType,
            quote_pricing_mode = "executable_ask_depth_vwap",
            strategy_code = variant.Code,
            reason,
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_title = market.Question,
            market_start_utc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market),
            market_end_utc = market.EndDateUtc,
            target_notional_usd = targetNotionalUsd,
            strategy_entry_price_cap = TryGetStandardEntryPriceCap(variant),
            paper_taker_rest_fallback_enabled = options.PaperTakerRestFallbackEnabled,
            paper_taker_max_quote_age_ms = options.PaperTakerMaxQuoteAgeMilliseconds,
            outcome_selection_candidates = snapshots
                .Select(ToTakerOutcomePricingSnapshotJson)
                .ToArray()
        });
    }

    private static object ToTakerOutcomePricingSnapshotJson(BtcTakerOutcomePricingSnapshot snapshot)
    {
        return new
        {
            asset_id = snapshot.AssetId,
            outcome = snapshot.Outcome,
            gamma_outcome_price = snapshot.GammaOutcomePrice,
            source = snapshot.Source,
            rejection_reason = snapshot.RejectionReason,
            rest_attempted = snapshot.RestAttempted,
            cache_status = snapshot.CacheStatus,
            cache_quote_exchange_timestamp_utc = snapshot.CacheQuoteExchangeTimestampUtc,
            cache_age_ms = snapshot.CacheAgeMs,
            cache_best_bid = snapshot.CacheBestBid,
            cache_best_ask = snapshot.CacheBestAsk,
            cache_has_executable_ask_depth = snapshot.CacheHasExecutableAskDepth,
            quote_exchange_timestamp_utc = snapshot.QuoteExchangeTimestampUtc,
            quote_age_ms = snapshot.QuoteAgeMs,
            best_bid = snapshot.BestBid,
            best_ask = snapshot.BestAsk,
            has_executable_ask_depth = snapshot.HasExecutableAskDepth,
            spread = snapshot.Spread,
            last_trade_price = snapshot.LastTradePrice,
            tick_size = snapshot.TickSize,
            min_order_size = snapshot.MinOrderSize,
            target_notional_usd = snapshot.TargetNotionalUsd,
            target_size_shares = snapshot.TargetSizeShares,
            max_allowed_price = snapshot.MaxAllowedPrice,
            estimated_fill_price = snapshot.EstimatedFillPrice,
            estimated_fill_shares = snapshot.EstimatedFillShares,
            estimated_fill_notional = snapshot.EstimatedFillNotional,
            levels_used = snapshot.LevelsUsed,
            asks = snapshot.Asks.Select(level => new { price = level.Price, size = level.Size }).ToArray(),
            bids = snapshot.Bids.Select(level => new { price = level.Price, size = level.Size }).ToArray(),
            cache_asks = snapshot.CacheAsks.Select(level => new { price = level.Price, size = level.Size }).ToArray(),
            cache_bids = snapshot.CacheBids.Select(level => new { price = level.Price, size = level.Size }).ToArray()
        };
    }

    private static string? GetTakerOutcomeSelectionSource(
        BtcUpDown5mStrategyVariant variant,
        IReadOnlyList<BtcTakerOutcomePricingSnapshot>? outcomeSelectionSnapshots)
    {
        if (UsesGammaOutcomeSelection(variant))
        {
            return GammaOutcomePriceSource;
        }

        return outcomeSelectionSnapshots is null ? null : "clob_executable_vwap";
    }

    private static decimal? TryGetStandardEntryPriceCap(BtcUpDown5mStrategyVariant variant)
    {
        if (!IsStrategyEntryPriceCapVariant(variant) || variant.DecisionDepth <= 0)
        {
            return null;
        }

        return variant.DecisionDepth / 100m;
    }

    private static bool IsStrategyEntryPriceCapVariant(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.StandardEntryPriceCap or
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelectionEntryPriceCap;
    }

    private static BtcTakerOutcomePricingSnapshot CreateTakerOutcomePricingSnapshot(
        BtcUpDown5mOutcomeQuote outcome,
        decimal targetNotionalUsd,
        TakerOrderBookLookupResult orderBookLookup,
        TakerBuyFillEstimate? estimate,
        string? rejectionReason)
    {
        var orderBook = orderBookLookup.OrderBook;
        var cacheOrderBook = orderBookLookup.CacheOrderBook;
        return new BtcTakerOutcomePricingSnapshot(
            outcome.AssetId,
            outcome.Outcome,
            outcome.Price,
            orderBookLookup.Source,
            rejectionReason,
            orderBookLookup.RestAttempted,
            orderBookLookup.CacheStatus?.ToString(),
            cacheOrderBook?.SnapshotAtUtc,
            orderBookLookup.CacheAge?.TotalMilliseconds,
            cacheOrderBook?.BestBid,
            cacheOrderBook?.BestAsk,
            cacheOrderBook is not null && HasExecutableAskDepth(cacheOrderBook),
            orderBook?.SnapshotAtUtc,
            orderBookLookup.Age?.TotalMilliseconds,
            estimate?.BestBid ?? orderBook?.BestBid,
            estimate?.BestAsk ?? orderBook?.BestAsk,
            orderBook is not null && HasExecutableAskDepth(orderBook),
            estimate?.SpreadAbs ?? orderBook?.SpreadAbs,
            orderBook?.LastTradePrice,
            orderBook?.TickSize,
            orderBook?.MinOrderSize,
            targetNotionalUsd,
            estimate?.TargetSizeShares ?? 0m,
            estimate?.MaxAllowedPrice ?? 0m,
            estimate?.AverageFillPrice ?? 0m,
            estimate?.SizeShares ?? 0m,
            estimate?.NotionalUsd ?? 0m,
            estimate?.LevelsUsed ?? 0,
            ToLevelSnapshots(orderBook?.Asks, descending: false),
            ToLevelSnapshots(orderBook?.Bids, descending: true),
            ToLevelSnapshots(cacheOrderBook?.Asks, descending: false),
            ToLevelSnapshots(cacheOrderBook?.Bids, descending: true));
    }

    private static IReadOnlyList<BtcOrderBookLevelSnapshot> ToLevelSnapshots(
        IEnumerable<OrderBookLevel>? levels,
        bool descending)
    {
        if (levels is null)
        {
            return [];
        }

        var orderedLevels = descending
            ? levels.OrderByDescending(level => level.Price)
            : levels.OrderBy(level => level.Price);
        return orderedLevels
            .Take(20)
            .Select(level => new BtcOrderBookLevelSnapshot(level.Price, level.Size))
            .ToArray();
    }

    private Signal CreateSignal(
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        decimal executionPrice,
        decimal sizeShares,
        decimal stakeUsd,
        DateTimeOffset nowUtc)
    {
        var trade = new LeaderTrade(
            variant.CopiedTraderWallet,
            variant.Name,
            market.ConditionId,
            outcome.AssetId,
            market.Slug,
            market.Question,
            outcome.Outcome,
            TradeSide.Buy,
            executionPrice,
            sizeShares,
            stakeUsd,
            nowUtc);

        return new Signal(
            Guid.NewGuid(),
            trade,
            Score: 100,
            Accepted: true,
            DecisionCode: variant.Code + "_entry",
            Reasons: [],
            ProposedPaperPrice: executionPrice,
            ProposedSizeShares: sizeShares,
            ProposedNotionalUsd: stakeUsd,
            CreatedAtUtc: nowUtc);
    }

    private PaperOrder CreateFilledPaperOrder(
        Signal signal,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        decimal executionPrice,
        decimal sizeShares,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        string? rawDecisionJson)
    {
        return new PaperOrder(
            Guid.NewGuid(),
            signal.Id,
            variant.CopiedTraderWallet,
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            outcome.AssetId,
            signal.LeaderTrade.ConditionId,
            outcome.Outcome,
            executionPrice,
            sizeShares,
            stakeUsd,
            nowUtc,
            nowUtc,
            FilledAtUtc: nowUtc,
            StrategyId: variant.Id,
            RawDecisionJson: rawDecisionJson);
    }

    private static PaperOrder CreatePendingOpeningLimitPaperOrder(
        Signal signal,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        decimal limitPrice,
        decimal sizeShares,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc,
        string? rawDecisionJson,
        Guid? correlationId = null,
        string executionSource = "")
    {
        return new PaperOrder(
            Guid.NewGuid(),
            signal.Id,
            variant.CopiedTraderWallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            outcome.AssetId,
            signal.LeaderTrade.ConditionId,
            outcome.Outcome,
            limitPrice,
            sizeShares,
            stakeUsd,
            nowUtc,
            expiresAtUtc,
            StrategyId: variant.Id,
            RawDecisionJson: rawDecisionJson,
            CorrelationId: correlationId,
            ExecutionSource: executionSource);
    }

    private OpeningLimitExpirationDecision ResolveOpeningLimitExpiration(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset nowUtc)
    {
        var configuredTtlSeconds = Math.Max(1, options.OpeningLimitGtdTtlSeconds);
        var clobBufferSeconds = Math.Max(60, options.ClobGtdExpirationSecurityBufferSeconds);

        if (variant.PreOpenLifetimeMode == BtcUpDownPreOpenLifetimeMode.HalfPeriod)
        {
            var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
            var effectiveMarketEndUtc = GetEffectiveMarketEndUtc(market, variant, marketStartUtc);
            if (marketStartUtc is null || effectiveMarketEndUtc is null || effectiveMarketEndUtc <= marketStartUtc)
            {
                return OpeningLimitExpirationDecision.Reject(
                    "opening_limit_half_period_window_unknown",
                    configuredTtlSeconds,
                    options.OpeningLimitExpireBeforeMarketEndSeconds,
                    clobBufferSeconds,
                    localExpiresAtUtc: null,
                    mode: "preopen_half_period");
            }

            var localExpiresAtUtc = marketStartUtc.Value.AddTicks((effectiveMarketEndUtc.Value - marketStartUtc.Value).Ticks / 2);
            if (localExpiresAtUtc <= nowUtc)
            {
                return OpeningLimitExpirationDecision.Reject(
                    "opening_limit_half_period_expiration_elapsed",
                    configuredTtlSeconds,
                    options.OpeningLimitExpireBeforeMarketEndSeconds,
                    clobBufferSeconds,
                    localExpiresAtUtc,
                    "preopen_half_period");
            }

            return OpeningLimitExpirationDecision.Enter(
                localExpiresAtUtc,
                localExpiresAtUtc.AddSeconds(clobBufferSeconds),
                nowUtc,
                configuredTtlSeconds,
                options.OpeningLimitExpireBeforeMarketEndSeconds,
                clobBufferSeconds,
                "preopen_half_period");
        }

        if (variant.PreOpenLifetimeMode == BtcUpDownPreOpenLifetimeMode.FullPeriod)
        {
            var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
            var effectiveMarketEndUtc = GetEffectiveMarketEndUtc(market, variant, marketStartUtc);
            if (effectiveMarketEndUtc is null)
            {
                return OpeningLimitExpirationDecision.Reject(
                    "opening_limit_full_period_market_end_unknown",
                    configuredTtlSeconds,
                    options.OpeningLimitExpireBeforeMarketEndSeconds,
                    clobBufferSeconds,
                    localExpiresAtUtc: null,
                    mode: "preopen_full_period");
            }

            var localExpiresAtUtc = effectiveMarketEndUtc.Value.AddSeconds(-Math.Max(0, options.OpeningLimitExpireBeforeMarketEndSeconds));
            if (localExpiresAtUtc <= nowUtc)
            {
                return OpeningLimitExpirationDecision.Reject(
                    "opening_limit_full_period_expiration_elapsed",
                    configuredTtlSeconds,
                    options.OpeningLimitExpireBeforeMarketEndSeconds,
                    clobBufferSeconds,
                    localExpiresAtUtc,
                    "preopen_full_period");
            }

            return OpeningLimitExpirationDecision.Enter(
                localExpiresAtUtc,
                localExpiresAtUtc.AddSeconds(clobBufferSeconds),
                nowUtc,
                configuredTtlSeconds,
                options.OpeningLimitExpireBeforeMarketEndSeconds,
                clobBufferSeconds,
                "preopen_full_period");
        }

        var marketStartForLateEntryCheck = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var marketEndForLateEntryCheck = GetEffectiveMarketEndUtc(market, variant, marketStartForLateEntryCheck);
        var entryDueAfterMarketMidpoint = IsEntryDueAfterMarketMidpoint(
            variant,
            marketStartForLateEntryCheck,
            marketEndForLateEntryCheck);

        if (!entryDueAfterMarketMidpoint &&
            options.OpeningLimitExpireBeforeMarketEndSeconds > 0 &&
            market.EndDateUtc is { } marketEndUtc)
        {
            var localExpiresAtUtc = marketEndUtc.AddSeconds(-options.OpeningLimitExpireBeforeMarketEndSeconds);
            if (localExpiresAtUtc <= nowUtc)
            {
                return OpeningLimitExpirationDecision.Reject(
                    "opening_limit_market_relative_expiration_elapsed",
                    configuredTtlSeconds,
                    options.OpeningLimitExpireBeforeMarketEndSeconds,
                    clobBufferSeconds,
                    localExpiresAtUtc,
                    "market_end_relative");
            }

            return OpeningLimitExpirationDecision.Enter(
                localExpiresAtUtc,
                localExpiresAtUtc.AddSeconds(clobBufferSeconds),
                nowUtc,
                configuredTtlSeconds,
                options.OpeningLimitExpireBeforeMarketEndSeconds,
                clobBufferSeconds,
                "market_end_relative");
        }

        var ttlExpirationUtc = nowUtc.AddSeconds(configuredTtlSeconds);
        var expiresAtUtc = market.EndDateUtc is { } marketEnd && marketEnd > nowUtc && marketEnd < ttlExpirationUtc
            ? marketEnd
            : ttlExpirationUtc;
        var mode = expiresAtUtc == ttlExpirationUtc ? "ttl" : "market_end_cap";
        return OpeningLimitExpirationDecision.Enter(
            expiresAtUtc,
            expiresAtUtc.AddSeconds(clobBufferSeconds),
            nowUtc,
            configuredTtlSeconds,
            options.OpeningLimitExpireBeforeMarketEndSeconds,
            clobBufferSeconds,
            mode);
    }

    private static bool IsEntryDueAfterMarketMidpoint(
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset? marketStartUtc,
        DateTimeOffset? marketEndUtc)
    {
        if (variant.PreOpenLifetimeMode != BtcUpDownPreOpenLifetimeMode.Default ||
            marketStartUtc is null ||
            marketEndUtc is null ||
            marketEndUtc <= marketStartUtc)
        {
            return false;
        }

        var entryDueAtUtc = marketStartUtc.Value.AddSeconds(variant.EntryDelaySeconds);
        var midpointUtc = marketStartUtc.Value.AddTicks((marketEndUtc.Value - marketStartUtc.Value).Ticks / 2);
        return entryDueAtUtc > midpointUtc;
    }

    private static DateTimeOffset? GetEffectiveMarketEndUtc(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset? marketStartUtc)
    {
        if (market.EndDateUtc is { } marketEndUtc)
        {
            return marketEndUtc;
        }

        return marketStartUtc?.Add(BtcUpDown5mMarketAnalyzer.GetIntervalDuration(variant.MarketInterval));
    }

    private async Task<bool> TryPlacePaperLiveShadowOrderAsync(
        Signal signal,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        PaperOrder paperOrder,
        decimal price,
        decimal sizeShares,
        decimal stakeUsd,
        OpeningLimitExpirationDecision expiration,
        Guid correlationId,
        DateTimeOffset? marketStartUtc,
        DateTimeOffset? marketEndUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var validation = new List<string>();
        if (!string.Equals(variant.Code, BtcSkip1VariantCode, StringComparison.OrdinalIgnoreCase))
        {
            validation.Add("Paper/Live shadow test is allowed only for BTC Up or Down 5m Skip 1.");
        }

        if (botOptions.Mode != BotMode.Live)
        {
            validation.Add("Bot mode is not Live.");
        }

        if (!botOptions.EnableLiveTrading)
        {
            validation.Add("Live trading is not explicitly enabled.");
        }

        if (controlState.KillSwitchActive)
        {
            validation.Add("Kill switch is active.");
        }

        if (controlState.LiveTradingPaused)
        {
            validation.Add("Live trading is paused.");
        }

        AddLiveMarketWindowValidation(validation, marketStartUtc, marketEndUtc, nowUtc);

        if (price <= 0m || price > 1m)
        {
            validation.Add("Live GTD BUY limit price is invalid.");
        }

        if (sizeShares <= 0m)
        {
            validation.Add("Live GTD BUY size must be greater than zero.");
        }

        var cancelDeadlineUtc = expiration.LocalExpiresAtUtc ?? nowUtc;
        var clobGtdExpirationUtc = expiration.ClobGtdExpirationUtc ?? cancelDeadlineUtc;
        if ((expiration.LocalTtlSeconds ?? 0) < 30)
        {
            validation.Add("Live GTD order TTL must be at least 30 seconds.");
        }

        if (cancelDeadlineUtc <= nowUtc.AddSeconds(30))
        {
            validation.Add("Live GTD local cancel deadline is too soon for CLOB placement.");
        }

        if (clobGtdExpirationUtc <= nowUtc.AddSeconds(60))
        {
            validation.Add("Live GTD wire expiration is too soon for CLOB placement.");
        }

        var liveNotional = price * sizeShares;
        var exposureSnapshot = await exposureCache.GetSnapshotAsync(cancellationToken);
        var openLiveOrders = exposureSnapshot.OpenLiveOrders;
        if (openLiveOrders.Count >= liveTradingOptions.MaxOpenLiveOrders)
        {
            validation.Add("Maximum open live order count reached.");
        }

        if (openLiveOrders.Any(order => nowUtc - order.CreatedAtUtc > TimeSpan.FromSeconds(liveTradingOptions.DefaultOrderTtlSeconds)))
        {
            validation.Add("A stale live order exists; live placement is locked until maintenance cancels it.");
        }

        var apiErrors = await repository.GetRecentApiErrorsAsync(cancellationToken: cancellationToken);
        var lockoutStart = nowUtc.AddMinutes(-liveTradingOptions.ApiErrorLockoutWindowMinutes);
        var recentPolymarketErrors = apiErrors.Count(error =>
            error.CreatedAtUtc >= lockoutStart &&
            error.Component.Contains("Polymarket", StringComparison.OrdinalIgnoreCase));
        if (recentPolymarketErrors >= liveTradingOptions.ApiErrorLockoutCount)
        {
            validation.Add("API error lockout is active.");
        }

        var riskEvents = await repository.GetRecentRiskEventsAsync(cancellationToken: cancellationToken);
        if (riskEvents.Any(item =>
            item.CreatedAtUtc >= nowUtc.AddDays(-1) &&
            item.ReasonCode.Contains("daily_loss", StringComparison.OrdinalIgnoreCase)))
        {
            validation.Add("Daily loss lockout is active.");
        }

        var authReadiness = await authService.GetReadinessAsync(cancellationToken);
        if (!authReadiness.CanAuthenticate)
        {
            validation.Add("Polymarket auth is not ready: " + string.Join(", ", authReadiness.MissingRequirements));
        }

        try
        {
            var geoblock = await geoClient.GetGeoblockStatusAsync(cancellationToken);
            if (geoblock.Blocked)
            {
                validation.Add($"Geoblock is active for VPS IP {geoblock.Ip ?? "unknown"}.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            validation.Add("Geoblock check failed: " + ex.Message);
        }

        OrderBookSnapshot? orderBook = null;
        try
        {
            orderBook = await clobClient.GetOrderBookAsync(outcome.AssetId, cancellationToken);
            var serverTime = await clobClient.GetServerTimeAsync(cancellationToken);
            var clockCheckUtc = DateTimeOffset.UtcNow;
            if (Math.Abs((serverTime - clockCheckUtc).TotalSeconds) > liveTradingOptions.MaxClockDriftSeconds)
            {
                validation.Add("CLOB server time drift exceeds configured limit.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            validation.Add("Live preflight market-data check failed: " + ex.Message);
        }

        if (orderBook?.MinOrderSize is { } minOrderSize && sizeShares > 0m && sizeShares < minOrderSize)
        {
            validation.Add("Live order size is below the market minimum order size.");
        }

        var maxTradeNotional = liveTradingOptions.MaxTradeBankrollPct / 100m * paperTradingOptions.InitialBankrollUsd;
        var maxTotalDeployed = liveTradingOptions.MaxTotalDeployedPct / 100m * paperTradingOptions.InitialBankrollUsd;
        var maxMarketNotional = liveTradingOptions.MaxMarketBankrollPct / 100m * paperTradingOptions.InitialBankrollUsd;
        var maxNotional = Math.Min(liveTradingOptions.MaxOrderNotionalUsd, maxTradeNotional);
        if (liveNotional > maxNotional)
        {
            validation.Add(
                $"Live shadow notional exceeds configured order/risk cap. Required={liveNotional:0.########}; Cap={maxNotional:0.########}.");
        }

        if (liveNotional > 0m)
        {
            await ValidateStrategyLiveBalanceAsync(
                variant.Id,
                openLiveOrders,
                liveNotional,
                validation,
                nowUtc,
                cancellationToken);
        }

        var marketExposure = openLiveOrders
            .Where(order => string.Equals(order.ConditionId, signal.LeaderTrade.ConditionId, StringComparison.OrdinalIgnoreCase))
            .Sum(order => order.NotionalUsd) +
            exposureSnapshot.OpenPaperOrders
                .Where(order => order.Id != paperOrder.Id && order.CorrelationId != correlationId)
                .Where(order => string.Equals(order.ConditionId, signal.LeaderTrade.ConditionId, StringComparison.OrdinalIgnoreCase))
                .Sum(order => order.NotionalUsd) +
            exposureSnapshot.PaperPositions
                .Where(position => string.Equals(position.ConditionId, signal.LeaderTrade.ConditionId, StringComparison.OrdinalIgnoreCase))
                .Sum(position => position.EstimatedValueUsd);
        var totalExposure = openLiveOrders.Sum(order => order.NotionalUsd) +
            exposureSnapshot.OpenPaperOrders
                .Where(order => order.Id != paperOrder.Id && order.CorrelationId != correlationId)
                .Sum(order => order.NotionalUsd) +
            exposureSnapshot.PaperPositions.Sum(position => position.EstimatedValueUsd);
        if (marketExposure + liveNotional > maxMarketNotional)
        {
            validation.Add("Live market exposure would exceed configured limit.");
        }

        if (totalExposure + liveNotional > maxTotalDeployed)
        {
            validation.Add("Live total deployed exposure would exceed configured limit.");
        }

        if (validation.Count > 0)
        {
            var rejectedOrder = CreatePaperLiveShadowLiveOrder(
                signal,
                outcome,
                variant,
                price,
                sizeShares,
                nowUtc,
                cancelDeadlineUtc,
                LiveOrderStatus.PreflightRejected,
                null,
                "preflight_rejected",
                string.Join("; ", validation),
                "{}",
                correlationId,
                paperOrder.Id);
            await PersistLiveOrderAsync(
                rejectedOrder,
                "BtcUpDown5mPaperLiveShadowPreflight",
                "Rejected",
                string.Join("; ", validation),
                cancellationToken);
            await CancelPaperShadowOrderAsync(paperOrder, nowUtc, cancellationToken);
            await repository.UpdatePaperLiveShadowDecisionLinksAsync(
                correlationId,
                signal.Id,
                paperOrder.Id,
                rejectedOrder.Id,
                "live_preflight_rejected",
                nowUtc,
                cancellationToken);
            return false;
        }

        var intent = CreatePaperLiveShadowLiveOrder(
            signal,
            outcome,
            variant,
            price,
            sizeShares,
            nowUtc,
            cancelDeadlineUtc,
            LiveOrderStatus.Submitted,
            null,
            "intent_created",
            string.Empty,
            paperOrder.RawDecisionJson ?? "{}",
            correlationId,
            paperOrder.Id);
        try
        {
            await repository.AddLiveOrderAsync(intent, cancellationToken);
            exposureCache.ApplyLiveOrder(intent);
            await repository.UpdatePaperLiveShadowDecisionLinksAsync(
                correlationId,
                signal.Id,
                paperOrder.Id,
                intent.Id,
                "live_intent_created",
                nowUtc,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await CancelPaperShadowOrderAsync(paperOrder, nowUtc, cancellationToken);
            await repository.SetStrategyLiveStakesAsync(variant.Id, false, nowUtc, cancellationToken);
            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(Guid.NewGuid(), "BtcUpDown5mPaperLiveShadowIntent", "Error", ex.Message, nowUtc),
                cancellationToken);
            return false;
        }

        var submitUtc = DateTimeOffset.UtcNow;
        var request = CreatePaperLiveShadowRequest(outcome, price, sizeShares, orderBook, submitUtc, clobGtdExpirationUtc);
        LiveOrderPlacementResult result;
        try
        {
            result = await tradingClient.PlaceLiveOrderAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var errorOrder = intent with
            {
                Status = LiveOrderStatus.Error,
                ResponseStatus = "error",
                ValidationSummary = "Live order placement failed: " + ex.Message,
                RawResponseJson = "{}",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await repository.UpdateLiveOrderAsync(errorOrder, cancellationToken);
            exposureCache.ApplyLiveOrder(errorOrder);
            await CancelPaperShadowOrderAsync(paperOrder, nowUtc, cancellationToken);
            await repository.UpdatePaperLiveShadowDecisionLinksAsync(
                correlationId,
                signal.Id,
                paperOrder.Id,
                intent.Id,
                "live_submit_error",
                DateTimeOffset.UtcNow,
                cancellationToken);
            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(Guid.NewGuid(), "BtcUpDown5mPaperLiveShadowPlaceOrder", "Error", ex.Message, DateTimeOffset.UtcNow),
                cancellationToken);
            return false;
        }

        var status = MapPlacementStatus(result);
        var fillSummary = LiveOrderPlacementAccounting.FromPlacementResult(
            TradeSide.Buy,
            price,
            sizeShares,
            status,
            result);
        var updatedLiveOrder = intent with
        {
            Status = status,
            OrderId = result.OrderId,
            SubmittedAtUtc = status is LiveOrderStatus.Live or LiveOrderStatus.Delayed or LiveOrderStatus.Matched or LiveOrderStatus.Unmatched or LiveOrderStatus.Submitted
                ? DateTimeOffset.UtcNow
                : null,
            ResponseStatus = result.ResponseStatus,
            FilledSize = fillSummary.FilledSize,
            RemainingSize = fillSummary.RemainingSize,
            AverageFillPrice = fillSummary.AverageFillPrice,
            FilledNotionalUsd = fillSummary.FilledNotionalUsd,
            CostBasisUsd = fillSummary.CostBasisUsd,
            RawResponseJson = string.IsNullOrWhiteSpace(result.RawResponseJson) ? "{}" : result.RawResponseJson,
            ValidationSummary = result.ErrorMessage ?? string.Empty,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            await repository.UpdateLiveOrderAsync(updatedLiveOrder, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!string.IsNullOrWhiteSpace(result.OrderId))
            {
                await tradingClient.CancelOrderAsync(result.OrderId, cancellationToken);
            }
            else
            {
                await tradingClient.CancelAllOrdersAsync(cancellationToken);
            }

            await repository.SetStrategyLiveStakesAsync(variant.Id, false, nowUtc, cancellationToken);
            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(Guid.NewGuid(), "BtcUpDown5mPaperLiveShadowPersistSubmit", "Error", ex.Message, DateTimeOffset.UtcNow),
                cancellationToken);
            return false;
        }

        exposureCache.ApplyLiveOrder(updatedLiveOrder);
        await repository.UpdatePaperLiveShadowDecisionLinksAsync(
            correlationId,
            signal.Id,
            paperOrder.Id,
            updatedLiveOrder.Id,
            result.Success ? "live_submitted" : "live_rejected",
            DateTimeOffset.UtcNow,
            cancellationToken);
        await repository.AddLiveTradingEventAsync(
            new LiveTradingEvent(
                Guid.NewGuid(),
                "BtcUpDown5mPaperLiveShadowPlaceOrder",
                result.Success ? "OK" : "Rejected",
                result.ErrorMessage ?? result.ResponseStatus,
                DateTimeOffset.UtcNow),
            cancellationToken);

        if (!result.Success || status is LiveOrderStatus.Rejected or LiveOrderStatus.Error)
        {
            await CancelPaperShadowOrderAsync(paperOrder, DateTimeOffset.UtcNow, cancellationToken);
            return false;
        }

        return status is LiveOrderStatus.Live or LiveOrderStatus.Delayed or LiveOrderStatus.Matched or LiveOrderStatus.Unmatched or LiveOrderStatus.Submitted;
    }

    private static void AddLiveMarketWindowValidation(
        ICollection<string> validation,
        DateTimeOffset? marketStartUtc,
        DateTimeOffset? marketEndUtc,
        DateTimeOffset nowUtc)
    {
        if (marketStartUtc is not { } startUtc)
        {
            validation.Add("BTC 5m market start time is unknown; live placement refused.");
        }
        else if (startUtc > nowUtc)
        {
            validation.Add("BTC 5m market has not started yet; live placement refused.");
        }

        if (marketEndUtc is not { } endUtc)
        {
            validation.Add("BTC 5m market end time is unknown; live placement refused.");
        }
        else if (endUtc <= nowUtc)
        {
            validation.Add("BTC 5m market has already ended; live placement refused.");
        }
    }

    private async Task<bool> TryPlaceLiveOrderAsync(
        Signal signal,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        decimal price,
        decimal liveStakeMultiplier,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var validation = new List<string>();
        if (botOptions.Mode != BotMode.Live)
        {
            validation.Add("Bot mode is not Live.");
        }

        if (!botOptions.EnableLiveTrading)
        {
            validation.Add("Live trading is not explicitly enabled.");
        }

        if (controlState.KillSwitchActive)
        {
            validation.Add("Kill switch is active.");
        }

        if (controlState.LiveTradingPaused)
        {
            validation.Add("Live trading is paused.");
        }

        if (liveStakeMultiplier <= 0m)
        {
            validation.Add("Strategy live stake multiplier must be greater than zero.");
        }

        var exposureSnapshot = await exposureCache.GetSnapshotAsync(cancellationToken);
        var openLiveOrders = exposureSnapshot.OpenLiveOrders;
        if (openLiveOrders.Count >= liveTradingOptions.MaxOpenLiveOrders)
        {
            validation.Add("Maximum open live order count reached.");
        }

        if (openLiveOrders.Any(order => nowUtc - order.CreatedAtUtc > TimeSpan.FromSeconds(liveTradingOptions.DefaultOrderTtlSeconds)))
        {
            validation.Add("A stale live order exists; live placement is locked until maintenance cancels it.");
        }

        var apiErrors = await repository.GetRecentApiErrorsAsync(cancellationToken: cancellationToken);
        var lockoutStart = nowUtc.AddMinutes(-liveTradingOptions.ApiErrorLockoutWindowMinutes);
        var recentPolymarketErrors = apiErrors.Count(error =>
            error.CreatedAtUtc >= lockoutStart &&
            error.Component.Contains("Polymarket", StringComparison.OrdinalIgnoreCase));
        if (recentPolymarketErrors >= liveTradingOptions.ApiErrorLockoutCount)
        {
            validation.Add("API error lockout is active.");
        }

        var riskEvents = await repository.GetRecentRiskEventsAsync(cancellationToken: cancellationToken);
        if (riskEvents.Any(item =>
            item.CreatedAtUtc >= nowUtc.AddDays(-1) &&
            item.ReasonCode.Contains("daily_loss", StringComparison.OrdinalIgnoreCase)))
        {
            validation.Add("Daily loss lockout is active.");
        }

        var authReadiness = await authService.GetReadinessAsync(cancellationToken);
        if (!authReadiness.CanAuthenticate)
        {
            validation.Add("Polymarket auth is not ready: " + string.Join(", ", authReadiness.MissingRequirements));
        }

        try
        {
            var geoblock = await geoClient.GetGeoblockStatusAsync(cancellationToken);
            if (geoblock.Blocked)
            {
                validation.Add($"Geoblock is active for VPS IP {geoblock.Ip ?? "unknown"}.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            validation.Add("Geoblock check failed: " + ex.Message);
        }

        OrderBookSnapshot? orderBook = null;
        try
        {
            orderBook = await clobClient.GetOrderBookAsync(outcome.AssetId, cancellationToken);
            var serverTime = await clobClient.GetServerTimeAsync(cancellationToken);
            var clockCheckUtc = DateTimeOffset.UtcNow;
            if (Math.Abs((serverTime - clockCheckUtc).TotalSeconds) > liveTradingOptions.MaxClockDriftSeconds)
            {
                validation.Add("CLOB server time drift exceeds configured limit.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            validation.Add("Live preflight market-data check failed: " + ex.Message);
        }

        if (orderBook?.BestAsk is not { } bestAsk)
        {
            validation.Add("Live GTD BUY requires a fresh best ask.");
        }
        else if (price <= 0m || price > 1m)
        {
            validation.Add("Live GTD BUY limit price is invalid.");
        }
        else if (price < bestAsk)
        {
            validation.Add("Live GTD BUY limit price is below the current best ask.");
        }

        var maxTradeNotional = liveTradingOptions.MaxTradeBankrollPct / 100m * paperTradingOptions.InitialBankrollUsd;
        var maxTotalDeployed = liveTradingOptions.MaxTotalDeployedPct / 100m * paperTradingOptions.InitialBankrollUsd;
        var maxMarketNotional = liveTradingOptions.MaxMarketBankrollPct / 100m * paperTradingOptions.InitialBankrollUsd;
        var maxNotional = Math.Min(liveTradingOptions.MaxOrderNotionalUsd, maxTradeNotional);
        var liveSizing = CreateLiveMinimumStakeSizing(orderBook, price, liveStakeMultiplier);
        if (!liveSizing.Available)
        {
            validation.Add("Live minimum stake sizing failed: " + (liveSizing.RejectionReason ?? "unknown"));
        }

        var liveNotional = liveSizing.TargetNotionalUsd;
        if (liveNotional > maxNotional)
        {
            validation.Add(
                $"Live minimum buffered notional exceeds configured order/risk cap. Required={liveNotional:0.########}; Cap={maxNotional:0.########}.");
        }

        if (liveNotional > 0m)
        {
            await ValidateStrategyLiveBalanceAsync(
                variant.Id,
                openLiveOrders,
                liveNotional,
                validation,
                nowUtc,
                cancellationToken);
        }

        var marketExposure = openLiveOrders
            .Where(order => string.Equals(order.ConditionId, signal.LeaderTrade.ConditionId, StringComparison.OrdinalIgnoreCase))
            .Sum(order => order.NotionalUsd) +
            exposureSnapshot.OpenPaperOrders
                .Where(order => string.Equals(order.ConditionId, signal.LeaderTrade.ConditionId, StringComparison.OrdinalIgnoreCase))
                .Sum(order => order.NotionalUsd) +
            exposureSnapshot.PaperPositions
                .Where(position => string.Equals(position.ConditionId, signal.LeaderTrade.ConditionId, StringComparison.OrdinalIgnoreCase))
                .Sum(position => position.EstimatedValueUsd);
        var totalExposure = openLiveOrders.Sum(order => order.NotionalUsd) +
            exposureSnapshot.OpenPaperOrders.Sum(order => order.NotionalUsd) +
            exposureSnapshot.PaperPositions.Sum(position => position.EstimatedValueUsd);
        if (marketExposure + liveNotional > maxMarketNotional)
        {
            validation.Add("Live market exposure would exceed configured limit.");
        }

        if (totalExposure + liveNotional > maxTotalDeployed)
        {
            validation.Add("Live total deployed exposure would exceed configured limit.");
        }

        var liveSizeShares = price > 0m ? RoundDown(liveNotional / price, 4) : 0m;
        if (liveSizeShares <= 0m)
        {
            validation.Add("Live order size after risk caps is zero.");
        }

        if (orderBook?.MinOrderSize is { } minOrderSize && liveSizeShares > 0m && liveSizeShares < minOrderSize)
        {
            validation.Add("Live order size is below the market minimum order size.");
        }

        if (validation.Count > 0)
        {
            await PersistLiveOrderAsync(
                CreateLiveOrder(
                    signal,
                    outcome,
                    variant,
                    price,
                    liveSizeShares,
                    nowUtc,
                    nowUtc.AddSeconds(liveTradingOptions.DefaultOrderTtlSeconds),
                    LiveOrderStatus.PreflightRejected,
                    null,
                    "preflight_rejected",
                    string.Join("; ", validation),
                    "{}"),
                "BtcUpDown5mLivePreflight",
                "Rejected",
                string.Join("; ", validation),
                cancellationToken);
            return false;
        }

        var submitUtc = DateTimeOffset.UtcNow;
        var liveOrderExpiresAtUtc = submitUtc.AddSeconds(liveTradingOptions.DefaultOrderTtlSeconds);
        var request = CreateLiveRequest(signal, outcome, price, liveSizeShares, orderBook, submitUtc, liveOrderExpiresAtUtc);
        LiveOrderPlacementResult result;
        try
        {
            result = await tradingClient.PlaceLiveOrderAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await PersistLiveOrderAsync(
                CreateLiveOrder(
                    signal,
                    outcome,
                    variant,
                    price,
                    liveSizeShares,
                    nowUtc,
                    nowUtc.AddSeconds(liveTradingOptions.DefaultOrderTtlSeconds),
                    LiveOrderStatus.Error,
                    null,
                    "error",
                    "Live order placement failed: " + ex.Message,
                    "{}"),
                "BtcUpDown5mLivePlaceOrder",
                "Error",
                ex.Message,
                cancellationToken);
            return false;
        }

        var status = MapPlacementStatus(result);
        var fillSummary = LiveOrderPlacementAccounting.FromPlacementResult(
            TradeSide.Buy,
            price,
            liveSizeShares,
            status,
            result);
        var liveOrder = CreateLiveOrder(
            signal,
            outcome,
            variant,
            price,
            liveSizeShares,
            nowUtc,
            liveOrderExpiresAtUtc,
            status,
            result.OrderId,
            result.ResponseStatus,
            result.ErrorMessage ?? string.Empty,
            string.IsNullOrWhiteSpace(result.RawResponseJson) ? "{}" : result.RawResponseJson) with
        {
            FilledSize = fillSummary.FilledSize,
            RemainingSize = fillSummary.RemainingSize,
            AverageFillPrice = fillSummary.AverageFillPrice,
            FilledNotionalUsd = fillSummary.FilledNotionalUsd,
            CostBasisUsd = fillSummary.CostBasisUsd
        };

        await PersistLiveOrderAsync(
            liveOrder,
            "BtcUpDown5mLivePlaceOrder",
            result.Success ? "OK" : "Rejected",
            result.ErrorMessage ?? result.ResponseStatus,
            cancellationToken);
        return result.Success && (liveOrder.Status is LiveOrderStatus.Live or LiveOrderStatus.Delayed);
    }

    private async Task ValidateStrategyLiveBalanceAsync(
        Guid strategyId,
        IReadOnlyList<LiveOrder> openLiveOrders,
        decimal requiredNotionalUsd,
        List<string> validation,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var settings = await strategyStateProvider.GetStrategySettingsAsync(strategyId, cancellationToken);
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        var reservedNotionalUsd = openLiveOrders
            .Where(order => StrategyIds.Normalize(order.StrategyId) == normalizedStrategyId)
            .Sum(order => order.NotionalUsd);
        var availableForNewStake = settings.LiveAvailableBalance - reservedNotionalUsd;
        if (availableForNewStake >= requiredNotionalUsd)
        {
            return;
        }

        var message =
            $"Strategy live available balance is insufficient. StrategyId={normalizedStrategyId}; " +
            $"Available={settings.LiveAvailableBalance:0.########}; Reserved={reservedNotionalUsd:0.########}; " +
            $"AvailableForNewStake={availableForNewStake:0.########}; Required={requiredNotionalUsd:0.########}.";
        validation.Add(message);
        logger.LogError(
            "Strategy live available balance is insufficient. StrategyId={StrategyId} Available={AvailableBalance} Reserved={ReservedNotionalUsd} Required={RequiredNotionalUsd}. Live stakes will be disabled for this strategy.",
            normalizedStrategyId,
            settings.LiveAvailableBalance,
            reservedNotionalUsd,
            requiredNotionalUsd);
        await repository.SetStrategyLiveStakesAsync(normalizedStrategyId, false, nowUtc, cancellationToken);
        await repository.AddLiveTradingEventAsync(
            new LiveTradingEvent(Guid.NewGuid(), "StrategyLiveBalance", "Error", message, nowUtc),
            cancellationToken);
    }

    private ClobV2OrderRequest CreateLiveRequest(
        Signal signal,
        BtcUpDown5mOutcomeQuote outcome,
        decimal price,
        decimal sizeShares,
        OrderBookSnapshot? orderBook,
        DateTimeOffset createdAtUtc,
        DateTimeOffset localExpiresAtUtc)
    {
        var gtdExpirationUtc = ResolveClobGtdExpirationUtc(localExpiresAtUtc);
        return new ClobV2OrderRequest(
            outcome.AssetId,
            TradeSide.Buy,
            price,
            sizeShares,
            orderBook?.TickSize ?? 0.01m,
            orderBook?.MinOrderSize ?? 1m,
            authOptions.FunderAddress,
            authOptions.SigningAddress,
            ParseSignatureType(authOptions.SignatureType),
            ClobV2OrderType.GTD,
            createdAtUtc,
            GtdExpirationUtc: gtdExpirationUtc,
            NegativeRisk: orderBook?.NegativeRisk ?? false,
            PostOnly: false);
    }

    private ClobV2OrderRequest CreatePaperLiveShadowRequest(
        BtcUpDown5mOutcomeQuote outcome,
        decimal price,
        decimal sizeShares,
        OrderBookSnapshot? orderBook,
        DateTimeOffset createdAtUtc,
        DateTimeOffset gtdExpirationUtc)
    {
        return new ClobV2OrderRequest(
            outcome.AssetId,
            TradeSide.Buy,
            price,
            sizeShares,
            orderBook?.TickSize ?? 0.01m,
            orderBook?.MinOrderSize ?? 1m,
            authOptions.FunderAddress,
            authOptions.SigningAddress,
            ParseSignatureType(authOptions.SignatureType),
            ClobV2OrderType.GTD,
            createdAtUtc,
            GtdExpirationUtc: gtdExpirationUtc,
            NegativeRisk: orderBook?.NegativeRisk ?? false,
            PostOnly: false);
    }

    private DateTimeOffset ResolveClobGtdExpirationUtc(DateTimeOffset localExpiresAtUtc)
    {
        return localExpiresAtUtc.AddSeconds(Math.Max(60, options.ClobGtdExpirationSecurityBufferSeconds));
    }

    private LiveOrder CreateLiveOrder(
        Signal signal,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        decimal price,
        decimal sizeShares,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        LiveOrderStatus status,
        string? orderId,
        string responseStatus,
        string validationSummary,
        string rawResponseJson)
    {
        var fallbackFillSummary = status == LiveOrderStatus.Matched
            ? new LiveOrderFillSummary(sizeShares, 0m, price, price * sizeShares, price * sizeShares)
            : new LiveOrderFillSummary(0m, sizeShares, null, 0m, 0m);
        return new LiveOrder(
            Guid.NewGuid(),
            signal.Id,
            status,
            orderId,
            TradeSide.Buy,
            outcome.AssetId,
            signal.LeaderTrade.ConditionId,
            outcome.Outcome,
            price,
            sizeShares,
            price * sizeShares,
            OpeningLimitOrderType,
            createdAtUtc,
            expiresAtUtc,
            status is LiveOrderStatus.Live or LiveOrderStatus.Delayed or LiveOrderStatus.Matched ? DateTimeOffset.UtcNow : null,
            responseStatus,
            fallbackFillSummary.FilledSize,
            fallbackFillSummary.RemainingSize,
            string.Empty,
            rawResponseJson,
            validationSummary,
            DateTimeOffset.UtcNow,
            StrategyId: variant.Id,
            AverageFillPrice: fallbackFillSummary.AverageFillPrice,
            FilledNotionalUsd: fallbackFillSummary.FilledNotionalUsd,
            CostBasisUsd: fallbackFillSummary.CostBasisUsd);
    }

    private static LiveOrder CreatePaperLiveShadowLiveOrder(
        Signal signal,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        decimal price,
        decimal sizeShares,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        LiveOrderStatus status,
        string? orderId,
        string responseStatus,
        string validationSummary,
        string rawResponseJson,
        Guid correlationId,
        Guid paperOrderId)
    {
        return new LiveOrder(
            Guid.NewGuid(),
            signal.Id,
            status,
            orderId,
            TradeSide.Buy,
            outcome.AssetId,
            signal.LeaderTrade.ConditionId,
            outcome.Outcome,
            price,
            sizeShares,
            price * sizeShares,
            OpeningLimitOrderType,
            createdAtUtc,
            expiresAtUtc,
            status is LiveOrderStatus.Live or LiveOrderStatus.Delayed or LiveOrderStatus.Matched or LiveOrderStatus.Unmatched or LiveOrderStatus.Submitted
                ? DateTimeOffset.UtcNow
                : null,
            responseStatus,
            status == LiveOrderStatus.Matched ? sizeShares : 0m,
            status == LiveOrderStatus.Matched ? 0m : sizeShares,
            string.Empty,
            rawResponseJson,
            validationSummary,
            DateTimeOffset.UtcNow,
            StrategyId: variant.Id,
            AverageFillPrice: status == LiveOrderStatus.Matched ? price : null,
            FilledNotionalUsd: status == LiveOrderStatus.Matched ? price * sizeShares : 0m,
            CostBasisUsd: status == LiveOrderStatus.Matched ? price * sizeShares : 0m,
            CorrelationId: correlationId,
            ExecutionSource: PaperLiveShadowTestSource,
            PostOnly: false,
            PaperOrderId: paperOrderId);
    }

    private async Task CancelPaperShadowOrderAsync(
        PaperOrder paperOrder,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (paperOrder.Status is not (PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled))
        {
            return;
        }

        var fills = await repository.GetPaperFillsForOrderAsync(paperOrder.Id, cancellationToken);
        if (fills.Count > 0)
        {
            return;
        }

        var cancelledOrder = paperOrder with
        {
            Status = PaperOrderStatus.Cancelled,
            CancelledAtUtc = nowUtc
        };
        await repository.UpdatePaperOrderAsync(cancelledOrder, cancellationToken);
        exposureCache.ApplyPaperOrder(cancelledOrder);
    }

    private async Task PersistLiveOrderAsync(
        LiveOrder order,
        string action,
        string status,
        string details,
        CancellationToken cancellationToken)
    {
        await repository.AddLiveOrderAsync(order, cancellationToken);
        exposureCache.ApplyLiveOrder(order);
        await repository.AddLiveTradingEventAsync(
            new LiveTradingEvent(Guid.NewGuid(), action, status, details, DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private static LiveOrderStatus MapPlacementStatus(LiveOrderPlacementResult result)
    {
        if (!result.Success)
        {
            return LiveOrderStatus.Rejected;
        }

        return (result.ResponseStatus ?? string.Empty).ToLowerInvariant() switch
        {
            "live" => LiveOrderStatus.Live,
            "matched" => LiveOrderStatus.Matched,
            "delayed" => LiveOrderStatus.Delayed,
            "unmatched" => LiveOrderStatus.Unmatched,
            _ => LiveOrderStatus.Submitted
        };
    }

    private static decimal RoundDown(decimal value, int decimals)
    {
        var factor = (decimal)Math.Pow(10, decimals);
        return Math.Floor(value * factor) / factor;
    }

    private static decimal RoundDownToTick(decimal value, decimal tickSize)
    {
        if (value <= 0m || tickSize <= 0m)
        {
            return 0m;
        }

        return Math.Floor(value / tickSize) * tickSize;
    }

    private static decimal RoundUp(decimal value, int decimals)
    {
        var factor = (decimal)Math.Pow(10, decimals);
        return Math.Ceiling(value * factor) / factor;
    }

    private static decimal RoundUpToClobLimitSizeShares(decimal targetNotionalUsd, decimal price)
    {
        if (targetNotionalUsd <= 0m || price <= 0m)
        {
            return 0m;
        }

        return RoundUp(targetNotionalUsd / price, 2);
    }

    private static decimal RoundStakeNotionalUsd(decimal value)
    {
        return value <= 0m ? 0m : RoundUp(value, 0);
    }

    private static ClobV2SignatureType ParseSignatureType(string value)
    {
        return Enum.TryParse<ClobV2SignatureType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : ClobV2SignatureType.EOA;
    }

    private async Task SkipRunAsync(
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        string reason,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken,
        string? diagnosticsJson = null)
    {
        diagnosticsJson = string.IsNullOrWhiteSpace(diagnosticsJson) ||
            string.Equals(diagnosticsJson, "{}", StringComparison.Ordinal)
            ? null
            : diagnosticsJson;

        await repository.UpdateStrategyMarketPaperRunAsync(
            run with
            {
                Status = StrategyMarketPaperRunStatuses.Skipped,
                SkipReason = reason,
                SkipDiagnosticsJson = diagnosticsJson,
                UpdatedAtUtc = nowUtc
            },
            cancellationToken);

        if (diagnosticsJson is null)
        {
            logger.LogInformation(
                "BTC Up or Down 5m paper run skipped. Strategy={StrategyCode} Market={MarketSlug} Reason={Reason}",
                variant.Code,
                run.MarketSlug,
                reason);
            return;
        }

        logger.LogInformation(
            "BTC Up or Down 5m paper run skipped. Strategy={StrategyCode} Market={MarketSlug} Reason={Reason} Diagnostics={Diagnostics}",
            variant.Code,
            run.MarketSlug,
            reason,
            diagnosticsJson);
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "BtcUpDown5mPaperStrategyProcessor", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist BTC Up or Down 5m paper strategy API error for {Operation}.", operation);
        }
    }

    private sealed record TakerOrderBookLookupResult(
        OrderBookSnapshot? OrderBook,
        string? RejectionReason,
        string Source,
        TimeSpan? Age,
        bool RestAttempted,
        OrderBookCacheLookupStatus? CacheStatus,
        OrderBookSnapshot? CacheOrderBook,
        TimeSpan? CacheAge)
    {
        public static TakerOrderBookLookupResult Found(
            OrderBookSnapshot orderBook,
            string source,
            TimeSpan? age,
            bool RestAttempted = false,
            OrderBookCacheLookupStatus? CacheStatus = null,
            OrderBookSnapshot? CacheOrderBook = null,
            TimeSpan? CacheAge = null)
        {
            return new TakerOrderBookLookupResult(
                orderBook,
                null,
                source,
                age,
                RestAttempted,
                CacheStatus,
                CacheOrderBook,
                CacheAge);
        }

        public static TakerOrderBookLookupResult Reject(
            string reason,
            OrderBookSnapshot? orderBook = null,
            string source = "",
            TimeSpan? age = null,
            bool RestAttempted = false,
            OrderBookCacheLookupStatus? CacheStatus = null,
            OrderBookSnapshot? CacheOrderBook = null,
            TimeSpan? CacheAge = null)
        {
            return new TakerOrderBookLookupResult(
                orderBook,
                reason,
                source,
                age,
                RestAttempted,
                CacheStatus,
                CacheOrderBook,
                CacheAge);
        }
    }

    private sealed record BtcPaperEntryPricingResult(
        bool Filled,
        string? RejectionReason,
        decimal AverageFillPrice,
        decimal SizeShares,
        decimal NotionalUsd,
        string Source,
        string Evidence,
        string RawDecisionJson,
        BtcTakerOutcomePricingSnapshot? Snapshot,
        TakerOrderBookLookupResult? OrderBookLookup,
        TakerBuyFillEstimate? Estimate,
        BtcMinimumStakeSizing? Sizing,
        decimal? ClobGammaDiff)
    {
        public static BtcPaperEntryPricingResult CreateFilled(
            decimal averageFillPrice,
            decimal sizeShares,
            decimal notionalUsd,
            string source,
            string evidence,
            string rawDecisionJson,
            BtcTakerOutcomePricingSnapshot? snapshot = null,
            TakerOrderBookLookupResult? orderBookLookup = null,
            TakerBuyFillEstimate? estimate = null,
            BtcMinimumStakeSizing? sizing = null,
            decimal? clobGammaDiff = null)
        {
            return new BtcPaperEntryPricingResult(
                true,
                null,
                averageFillPrice,
                sizeShares,
                notionalUsd,
                source,
                evidence,
                rawDecisionJson,
                snapshot,
                orderBookLookup,
                estimate,
                sizing,
                clobGammaDiff);
        }

        public static BtcPaperEntryPricingResult Reject(
            string reason,
            BtcTakerOutcomePricingSnapshot? snapshot = null,
            string? diagnosticsJson = null)
        {
            return new BtcPaperEntryPricingResult(
                false,
                reason,
                0m,
                0m,
                0m,
                string.Empty,
                string.Empty,
                string.IsNullOrWhiteSpace(diagnosticsJson) ? "{}" : diagnosticsJson,
                snapshot,
                null,
                null,
                null,
                null);
        }
    }

    private enum BtcPriceDirection
    {
        Up,
        Down
    }

    private sealed record BtcCleverFairValueEstimate(
        bool ShouldEnter,
        string? RejectionReason,
        int CandidateSamples,
        decimal? WeightSum,
        decimal? FairValuePrice,
        decimal? AdjustedFairValuePrice,
        decimal? RawLimitPrice,
        decimal? LimitPrice,
        decimal? CurrentTargetPrice,
        string? CurrentTargetPriceProxyKind,
        decimal? CurrentTargetSpread,
        string? CurrentTargetBookSource,
        decimal? CurrentTargetBookAgeMs,
        decimal? CurrentLiquidityDiscount,
        decimal? AverageDistance,
        decimal? CurrentAlignedMoveBps,
        decimal? CurrentSecondsToClose)
    {
        public static BtcCleverFairValueEstimate Enter(
            int CandidateSamples,
            decimal WeightSum,
            decimal FairValuePrice,
            decimal AdjustedFairValuePrice,
            decimal RawLimitPrice,
            decimal LimitPrice,
            decimal CurrentTargetPrice,
            string CurrentTargetPriceProxyKind,
            decimal? CurrentTargetSpread,
            string CurrentTargetBookSource,
            decimal? CurrentTargetBookAgeMs,
            decimal CurrentLiquidityDiscount,
            decimal AverageDistance,
            decimal CurrentAlignedMoveBps,
            decimal CurrentSecondsToClose)
        {
            return new BtcCleverFairValueEstimate(
                true,
                null,
                CandidateSamples,
                WeightSum,
                FairValuePrice,
                AdjustedFairValuePrice,
                RawLimitPrice,
                LimitPrice,
                CurrentTargetPrice,
                CurrentTargetPriceProxyKind,
                CurrentTargetSpread,
                CurrentTargetBookSource,
                CurrentTargetBookAgeMs,
                CurrentLiquidityDiscount,
                AverageDistance,
                CurrentAlignedMoveBps,
                CurrentSecondsToClose);
        }

        public static BtcCleverFairValueEstimate Reject(
            string RejectionReason,
            int CandidateSamples = 0,
            decimal? WeightSum = null,
            decimal? FairValuePrice = null,
            decimal? AdjustedFairValuePrice = null,
            decimal? RawLimitPrice = null,
            decimal? LimitPrice = null,
            decimal? CurrentTargetPrice = null,
            string? CurrentTargetPriceProxyKind = null,
            decimal? CurrentTargetSpread = null,
            string? CurrentTargetBookSource = null,
            decimal? CurrentTargetBookAgeMs = null,
            decimal? CurrentLiquidityDiscount = null,
            decimal? AverageDistance = null,
            decimal? CurrentAlignedMoveBps = null,
            decimal? CurrentSecondsToClose = null)
        {
            return new BtcCleverFairValueEstimate(
                false,
                RejectionReason,
                CandidateSamples,
                WeightSum,
                FairValuePrice,
                AdjustedFairValuePrice,
                RawLimitPrice,
                LimitPrice,
                CurrentTargetPrice,
                CurrentTargetPriceProxyKind,
                CurrentTargetSpread,
                CurrentTargetBookSource,
                CurrentTargetBookAgeMs,
                CurrentLiquidityDiscount,
                AverageDistance,
                CurrentAlignedMoveBps,
                CurrentSecondsToClose);
        }
    }

    private sealed record BtcCleverFairValueCandidate(
        decimal Price,
        decimal Weight,
        decimal Distance,
        decimal AlignedMoveBps,
        decimal SecondsToClose);

    private sealed record BtcOpeningLimitSignalVote(
        string StrategyCode,
        bool ShouldEnter,
        string? SkipReason,
        BtcPriceDirection? Direction,
        string? Outcome,
        string? AssetId,
        decimal? LimitPriceOverride);

    private sealed record BtcStrategySelectorCandidateStats(
        BtcUpDown5mStrategyVariant Variant,
        int SettledRuns,
        int Wins,
        decimal RealizedPnlUsd,
        decimal? Roi)
    {
        public decimal AveragePnlUsd => SettledRuns > 0 ? RealizedPnlUsd / SettledRuns : 0m;
    }

    private sealed record BtcOpeningLimitDecision(
        bool ShouldEnter,
        BtcUpDown5mOutcomeQuote? SelectedOutcome,
        string? SkipReason,
        string RawDecisionJson,
        decimal? LimitPriceOverride)
    {
        public static BtcOpeningLimitDecision Enter(
            BtcUpDown5mOutcomeQuote selectedOutcome,
            string rawDecisionJson,
            decimal? limitPriceOverride = null)
        {
            return new BtcOpeningLimitDecision(true, selectedOutcome, null, rawDecisionJson, limitPriceOverride);
        }

        public static BtcOpeningLimitDecision Reject(
            string reason,
            string? rawDecisionJson = null,
            decimal? limitPriceOverride = null)
        {
            return new BtcOpeningLimitDecision(false, null, reason, string.IsNullOrWhiteSpace(rawDecisionJson) ? "{}" : rawDecisionJson, limitPriceOverride);
        }
    }

    private sealed record BtcOpeningLimitPriceDecision(
        bool ShouldEnter,
        decimal LimitPrice,
        string? SkipReason,
        string RawDecisionJson)
    {
        public static BtcOpeningLimitPriceDecision Enter(
            decimal limitPrice,
            string rawDecisionJson)
        {
            return new BtcOpeningLimitPriceDecision(true, limitPrice, null, rawDecisionJson);
        }

        public static BtcOpeningLimitPriceDecision Reject(
            string reason,
            string rawDecisionJson)
        {
            return new BtcOpeningLimitPriceDecision(false, 0m, reason, rawDecisionJson);
        }
    }

    private sealed record BtcOpeningLimitBookBootstrapPriceDecision(
        bool Available,
        decimal LimitPrice,
        string? RejectionReason,
        string Source,
        TimeSpan? Age,
        OrderBookSnapshot? OrderBook,
        decimal? RawLimitPrice,
        decimal? TickSize,
        string? PriceSource,
        decimal? BestBid,
        decimal? BestAsk)
    {
        public static BtcOpeningLimitBookBootstrapPriceDecision Enter(
            decimal limitPrice,
            string source,
            TimeSpan? age,
            OrderBookSnapshot orderBook,
            decimal rawLimitPrice,
            decimal tickSize,
            string? priceSource,
            decimal? bestBid,
            decimal? bestAsk)
        {
            return new BtcOpeningLimitBookBootstrapPriceDecision(
                true,
                limitPrice,
                null,
                source,
                age,
                orderBook,
                rawLimitPrice,
                tickSize,
                priceSource,
                bestBid,
                bestAsk);
        }

        public static BtcOpeningLimitBookBootstrapPriceDecision Reject(
            string reason,
            string source,
            TimeSpan? Age,
            OrderBookSnapshot? OrderBook,
            decimal? RawLimitPrice = null,
            decimal? TickSize = null,
            string? PriceSource = null,
            decimal? bestBid = null,
            decimal? bestAsk = null)
        {
            return new BtcOpeningLimitBookBootstrapPriceDecision(
                false,
                0m,
                reason,
                source,
                Age,
                OrderBook,
                RawLimitPrice,
                TickSize,
                PriceSource,
                bestBid,
                bestAsk);
        }
    }

    private sealed record BtcCurrentPriceLookupResult(
        BtcUsdReferencePricePoint? Price,
        string? ErrorMessage)
    {
        public static BtcCurrentPriceLookupResult Success(BtcUsdReferencePricePoint price)
        {
            return new BtcCurrentPriceLookupResult(price, null);
        }

        public static BtcCurrentPriceLookupResult Failure(string errorMessage)
        {
            return new BtcCurrentPriceLookupResult(null, errorMessage);
        }
    }

    private sealed record CloseBookMidpoint(
        decimal BestBid,
        decimal BestAsk,
        decimal Midpoint);

    private sealed record CloseBookMidpointLookup(
        CloseBookMidpoint? Midpoint,
        string? RejectionReason,
        OrderBookSnapshot? OrderBook,
        string Source);

    private sealed record CloseBookInferenceCandidate(
        string WinningOutcome,
        decimal InferredUpPrice,
        string Source,
        int Priority);

    private sealed record BtcSkipCloseBookLookupResult(
        IReadOnlyList<BtcSkipMarketResult> Results,
        IReadOnlyList<BtcSkipCloseBookDiagnostic> Diagnostics)
    {
        public bool HasOrderBookUnavailable =>
            Diagnostics.Any(diagnostic => diagnostic.OrderBookUnavailable);
    }

    private sealed record BtcSkipCloseBookInferenceResult(
        BtcSkipMarketResult? Result,
        BtcSkipCloseBookDiagnostic? Diagnostic)
    {
        public static BtcSkipCloseBookInferenceResult Success(BtcSkipMarketResult result)
        {
            return new BtcSkipCloseBookInferenceResult(result, null);
        }

        public static BtcSkipCloseBookInferenceResult Missing(BtcSkipCloseBookDiagnostic diagnostic)
        {
            return new BtcSkipCloseBookInferenceResult(null, diagnostic);
        }
    }

    private sealed record BtcSkipCloseBookDiagnostic(
        DateTimeOffset ExpectedMarketStartUtc,
        string? MarketId,
        string? ConditionId,
        string? MarketSlug,
        DateTimeOffset? MarketEndUtc,
        string Reason,
        bool OrderBookUnavailable,
        string? UpAssetId,
        string? DownAssetId,
        string? UpLookupReason,
        decimal? UpBestBid,
        decimal? UpBestAsk,
        decimal? UpMidpoint,
        string? DownLookupReason,
        decimal? DownBestBid,
        decimal? DownBestAsk,
        decimal? DownMidpoint,
        string? UpQuoteSource = null,
        string? DownQuoteSource = null);

    private sealed record BtcSkipMarketResult(
        string MarketId,
        string ConditionId,
        string MarketSlug,
        DateTimeOffset? MarketStartUtc,
        DateTimeOffset? MarketEndUtc,
        string WinningOutcome,
        DateTimeOffset ResultAtUtc,
        string ResultSource,
        string UpAssetId,
        string? DownAssetId,
        decimal? UpBestBid,
        decimal? UpBestAsk,
        decimal? UpMidpoint,
        decimal? DownBestBid,
        decimal? DownBestAsk,
        decimal? DownMidpoint,
        decimal InferredUpMidpoint);

    private sealed record OpeningLimitExpirationDecision(
        bool Available,
        DateTimeOffset? LocalExpiresAtUtc,
        DateTimeOffset? ClobGtdExpirationUtc,
        int? LocalTtlSeconds,
        int ConfiguredTtlSeconds,
        int MarketEndExpireBeforeSeconds,
        int ClobSecurityBufferSeconds,
        string Mode,
        string? RejectionReason)
    {
        public static OpeningLimitExpirationDecision Enter(
            DateTimeOffset localExpiresAtUtc,
            DateTimeOffset clobGtdExpirationUtc,
            DateTimeOffset nowUtc,
            int configuredTtlSeconds,
            int marketEndExpireBeforeSeconds,
            int clobSecurityBufferSeconds,
            string mode)
        {
            return new OpeningLimitExpirationDecision(
                Available: true,
                localExpiresAtUtc,
                clobGtdExpirationUtc,
                Math.Max(1, (int)Math.Ceiling((localExpiresAtUtc - nowUtc).TotalSeconds)),
                configuredTtlSeconds,
                marketEndExpireBeforeSeconds,
                clobSecurityBufferSeconds,
                mode,
                RejectionReason: null);
        }

        public static OpeningLimitExpirationDecision Reject(
            string reason,
            int configuredTtlSeconds,
            int marketEndExpireBeforeSeconds,
            int clobSecurityBufferSeconds,
            DateTimeOffset? localExpiresAtUtc,
            string mode)
        {
            return new OpeningLimitExpirationDecision(
                Available: false,
                localExpiresAtUtc,
                localExpiresAtUtc?.AddSeconds(clobSecurityBufferSeconds),
                LocalTtlSeconds: null,
                configuredTtlSeconds,
                marketEndExpireBeforeSeconds,
                clobSecurityBufferSeconds,
                mode,
                reason);
        }
    }

    private sealed record OpeningLimitFillSummary(
        decimal SizeShares,
        decimal AverageFillPrice,
        decimal NotionalUsd,
        DateTimeOffset? LastFilledAtUtc);

    private sealed record BtcMinimumStakeSizing(
        bool Available,
        string? RejectionReason,
        string Source,
        decimal StakeMultiplier,
        decimal SafetyMultiplier,
        string RoundingMode,
        decimal? MinOrderSize,
        decimal MinimumNotionalUsd,
        decimal RawTargetNotionalUsd,
        decimal TargetNotionalUsd,
        decimal TargetSizeShares,
        decimal ReferencePrice,
        int LevelsUsed,
        DateTimeOffset? PaperGtdSnapshotAtUtc = null,
        decimal? PaperGtdBestBid = null,
        decimal? PaperGtdBestAsk = null,
        decimal? PaperGtdLastTradePrice = null,
        decimal? PaperGtdQueueAheadShares = null,
        decimal? PaperGtdImmediateExecutableAskShares = null,
        decimal? PaperGtdImmediateExecutableAskVwap = null)
    {
        public static BtcMinimumStakeSizing Reject(
            string reason,
            decimal stakeMultiplier,
            string Source = "")
        {
            return new BtcMinimumStakeSizing(
                Available: false,
                RejectionReason: reason,
                Source,
                stakeMultiplier,
                MinimumStakeSafetyMultiplier,
                RoundingMode: string.Empty,
                MinOrderSize: null,
                MinimumNotionalUsd: 0m,
                RawTargetNotionalUsd: 0m,
                TargetNotionalUsd: 0m,
                TargetSizeShares: 0m,
                ReferencePrice: 0m,
                LevelsUsed: 0);
        }

        public static BtcMinimumStakeSizing FallbackFixedStake(
            decimal stakeMultiplier,
            decimal referencePrice,
            string source)
        {
            return new BtcMinimumStakeSizing(
                Available: true,
                RejectionReason: null,
                source,
                stakeMultiplier,
                SafetyMultiplier: 1m,
                RoundingMode: string.Empty,
                MinOrderSize: null,
                MinimumNotionalUsd: stakeMultiplier,
                RawTargetNotionalUsd: stakeMultiplier,
                TargetNotionalUsd: stakeMultiplier,
                TargetSizeShares: referencePrice > 0m ? stakeMultiplier / referencePrice : 0m,
                ReferencePrice: referencePrice,
                LevelsUsed: 0);
        }
    }

    private sealed record BtcTakerOutcomeSelectionResult(
        bool Filled,
        BtcUpDown5mOutcomeQuote? SelectedOutcome,
        BtcPaperEntryPricingResult? EntryPricing,
        string? RejectionReason,
        bool CanRetryWithRest,
        string? SkipDiagnosticsJson)
    {
        public static BtcTakerOutcomeSelectionResult Fill(
            BtcUpDown5mOutcomeQuote selectedOutcome,
            BtcPaperEntryPricingResult entryPricing)
        {
            return new BtcTakerOutcomeSelectionResult(
                true,
                selectedOutcome,
                entryPricing,
                null,
                false,
                null);
        }

        public static BtcTakerOutcomeSelectionResult Reject(
            string reason,
            bool CanRetryWithRest = false,
            string? SkipDiagnosticsJson = null)
        {
            return new BtcTakerOutcomeSelectionResult(
                false,
                null,
                null,
                reason,
                CanRetryWithRest,
                SkipDiagnosticsJson);
        }
    }

    private sealed record BtcTakerOutcomePricingCandidate(
        BtcUpDown5mOutcomeQuote Outcome,
        BtcPaperEntryPricingResult EntryPricing);

    private sealed record BtcTakerOutcomePricingSnapshot(
        string AssetId,
        string Outcome,
        decimal GammaOutcomePrice,
        string Source,
        string? RejectionReason,
        bool RestAttempted,
        string? CacheStatus,
        DateTimeOffset? CacheQuoteExchangeTimestampUtc,
        double? CacheAgeMs,
        decimal? CacheBestBid,
        decimal? CacheBestAsk,
        bool CacheHasExecutableAskDepth,
        DateTimeOffset? QuoteExchangeTimestampUtc,
        double? QuoteAgeMs,
        decimal? BestBid,
        decimal? BestAsk,
        bool HasExecutableAskDepth,
        decimal? Spread,
        decimal? LastTradePrice,
        decimal? TickSize,
        decimal? MinOrderSize,
        decimal TargetNotionalUsd,
        decimal TargetSizeShares,
        decimal MaxAllowedPrice,
        decimal EstimatedFillPrice,
        decimal EstimatedFillShares,
        decimal EstimatedFillNotional,
        int LevelsUsed,
        IReadOnlyList<BtcOrderBookLevelSnapshot> Asks,
        IReadOnlyList<BtcOrderBookLevelSnapshot> Bids,
        IReadOnlyList<BtcOrderBookLevelSnapshot> CacheAsks,
        IReadOnlyList<BtcOrderBookLevelSnapshot> CacheBids);

    private sealed record BtcOrderBookLevelSnapshot(
        decimal Price,
        decimal Size);

    private sealed record BestAskExecutionPriceResult(
        decimal? Price,
        string? RejectionReason,
        string Source);

    private sealed record OrderBookFetchResult(
        OrderBookSnapshot? OrderBook,
        string? RejectionReason);

    private sealed record ObserveMarketsResult(
        int Observed,
        int Skipped,
        IReadOnlyList<PolymarketGammaMarket> Markets);

    private sealed record PaperLiveShadowOrderBookSnapshotResult(
        OrderBookSnapshot? OrderBook,
        string Source,
        TimeSpan? Age,
        string? RejectionReason)
    {
        public static PaperLiveShadowOrderBookSnapshotResult Found(
            OrderBookSnapshot orderBook,
            string source,
            TimeSpan? age)
        {
            return new PaperLiveShadowOrderBookSnapshotResult(orderBook, source, age, null);
        }

        public static PaperLiveShadowOrderBookSnapshotResult Reject(string reason, string source)
        {
            return new PaperLiveShadowOrderBookSnapshotResult(null, source, null, reason);
        }
    }

    private sealed record Less180MartinEntryDecision(
        bool ShouldEnter,
        decimal StakeUsd,
        string? SkipReason)
    {
        public static Less180MartinEntryDecision Enter(decimal stakeUsd)
        {
            return new Less180MartinEntryDecision(true, stakeUsd, null);
        }

        public static Less180MartinEntryDecision Skip(string reason)
        {
            return new Less180MartinEntryDecision(false, 0m, reason);
        }
    }
}
