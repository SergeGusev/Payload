using System.Collections.Concurrent;
using System.Diagnostics;
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
    ICryptoReferencePriceClient cryptoReferencePriceClient,
    ICryptoReferencePriceAverageProvider cryptoReferencePriceAverageProvider,
    ICryptoReferencePriceExtremaProvider cryptoReferencePriceExtremaProvider,
    IExpiryFuturesReferencePriceClient expiryFuturesReferencePriceClient,
    IMarketDataCache marketDataCache,
    IActiveMarketAssetSubscriptionRegistry activeMarketAssetSubscriptionRegistry,
    IExposureSnapshotCache exposureCache,
    ServiceControlState controlState,
    IStrategyStateProvider strategyStateProvider,
    IAppRepository repository,
    TimeProvider? timeProvider = null,
    IPaperEntryPersistenceQueue? paperEntryPersistenceQueue = null) : IBtcUpDown5mPaperStrategyProcessor
{
    private const string GammaOutcomePriceSource = "gamma_outcome_price";
    private const string WebSocketCacheSource = "websocket_cache";
    private const string ClobBookSource = "clob_book";
    private const string CloseBookSnapshotSource = "order_book_snapshot";
    private const string PaperLiveShadowTestSource = "paper_live_shadow_test";
    private const string PaperLiveShadowActualFillExecutionSource = "paper_live_shadow_actual_fill";
    private const string PaperLiveShadowActualFillModel = "live_order_actual_fill_v1";
    private const string BtcGtdLimitExecutionSource = "btc_updown5m_gtd_limit";
    private const string BtcPreOpenSellExitExecutionSource = "btc_preopen_sell_exit";
    private const string BtcMakerExecutionSource = "btc_updown5m_maker_post_only";
    private const string BtcFakTakerPaperExecutionSource = "btc_updown5m_fak_taker_paper";
    private const string BtcChildMirrorPaperExecutionSource = "btc_updown5m_child_mirror_paper";
    private const string BtcChildMirrorFakPaperExecutionSource = "btc_updown5m_child_mirror_fak_paper";
    private const string PaperExecutableSnapshotEvidenceClass = "paper_executable_snapshot_model";
    private const string PaperFakExecutableSnapshotFillModel = "fak_taker_executable_snapshot_v2";
    private const string StrategyPausedSkipReason = "strategy_paused";
    private const string EthSkipUpDirectionTemporarilyDisabledReason = "eth_skip_up_direction_temporarily_disabled";
    private static readonly IReadOnlySet<string> CryptoReferenceAssetSymbols = StrategyIds.CryptoUpDown5mVariants
        .Select(GetReferenceAssetSymbol)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<Guid, BtcUpDown5mStrategyVariant> StrategyVariantsById =
        StrategyIds.UpDown5mStrategyVariants.ToDictionary(variant => StrategyIds.Normalize(variant.Id));
    private const string OpeningLimitPricingMode = "paper_gtd_limit";
    private const string OpeningLimitOrderType = "GTD";
    private const string FakOrderType = "FAK";
    private const decimal FakGuaranteedWorstPrice = 0.99m;
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
    private const decimal UncappedInstantOpeningLimitMaxPrice = 1.00m;
    private const int FuturesBasisRequiredExpiryCount = 3;
    private const int SkipPreviousResultEndPriceMaxAgeSeconds = 15;
    private const int SkipPreviousResultBpsMaxStreakMarkets = 100;
    private const int PremarketPreviousResultDefaultSampleSecondsBeforeEnd = 30;
    private static readonly TimeSpan PreviousScoreCounterTrendPremarketCarryoverWindow = TimeSpan.FromMinutes(1);
    private const int MakerDecisionIntervalSeconds = 30;
    private const int MakerMaxDecisionSlot = 9;
    private const string StakeNotionalRoundingMode = "ceil_usd";
    private const string DiffCounterWebSocketResultSource = "MarketWebSocket";
    private const string DiffCounterReferenceStartEndResultSource = "ReferenceStartEnd";
    private const string DiffCounterBinanceTimedCloseResultSource = "BinanceTimedClose";
    private const string DiffCounterTerminalOrderBookResultSource = "TerminalOrderBook";
    private const string DiffCounterGammaClosedMarketResultSource = "GammaClosedMarket";
    private const string PremarketPreviousResultSourcePrefix = "ReferencePricePremarketEndMinus";
    private const decimal DiffProgressMaxStakeMultiplier = 10m;
    private const int AdjustedDiffTrendZeroEmaPeriodPoints = 24;
    private const int AdjustedDiffTrendZeroWarmupPoints = 12;
    private const decimal AdjustedDiffTrendZeroDeadband = 1m;
    private const decimal AdjustedDiffTrendZeroMaxStep = 0.5m;
    private const string AdjustedDiffTrendZeroMode = "ema_24_slow_step_continuous";
    private static readonly TimeSpan DiffCounterHistoryFetchFailureBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MarketObserveAheadWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MarketObserveBehindWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ObservedRunCacheCleanupInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ObservedRunCacheExpirationBuffer = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CloseBookCaptureMaxDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CloseBookCaptureOrderBookTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SettlementMetadataTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LiveStrategyPriorityRefreshInterval = TimeSpan.FromMinutes(1);
    private const int ChildRoiMinimumSettledRuns = 10;
    private const decimal ChildRoiMinimumStakeUsd = 60m;
    private const decimal ChildRoiPriorStakeUsd = 120m;
    private static readonly TimeSpan LocalFinalizedEntryRunRetention = TimeSpan.FromMinutes(30);
    private static readonly IReadOnlyList<DiffReferenceAverageWindowSpec> DiffReferenceAverageWindows =
    [
        new("24h", TimeSpan.FromHours(24)),
        new("12h", TimeSpan.FromHours(12)),
        new("6h", TimeSpan.FromHours(6)),
        new("3h", TimeSpan.FromHours(3)),
        new("90m", TimeSpan.FromMinutes(90)),
        new("45m", TimeSpan.FromMinutes(45))
    ];
    private const long StrategyStageTimingMinDurationMs = 1_000;

    private readonly ConservativePaperGtdFillEstimator conservativeGtdFillEstimator = new(options);
    private readonly IPaperTradingEngine paperTradingEngine = new DefaultPaperTradingEngine();
    private readonly SemaphoreSlim entryPlacementLock = new(1, 1);
    private readonly SemaphoreSlim entryDecisionConcurrencyLock = new(
        options.MaxConcurrentEntryDecisions,
        options.MaxConcurrentEntryDecisions);
    private readonly SemaphoreSlim mainDueEntryProcessingLock = new(1, 1);
    private readonly SemaphoreSlim diffCounterStateLock = new(1, 1);
    private readonly SemaphoreSlim liveStrategyPriorityRefreshLock = new(1, 1);
    private readonly SemaphoreSlim childParentRefreshLock = new(1, 1);
    private readonly object diffProgressStateSync = new();
    private readonly Dictionary<string, DateTimeOffset> closingOrderBookCaptureAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BtcMakerHighWaterState> makerHighWaterStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DiffCounterState> diffCounterStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DiffCounterState> adjustedDiffCounterStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DiffCounterState> shiftDiffCounterStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DiffProgressRuntimeState> diffProgressStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> locallyFinalizedEntryRuns = new();
    private readonly ConcurrentDictionary<StrategyMarketRunCacheKey, DateTimeOffset> observedRunCache = new();
    private readonly object observedRunCacheCleanupSync = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private LiveStrategyPrioritySnapshot liveStrategyPrioritySnapshot = LiveStrategyPrioritySnapshot.Empty;
    private DateTimeOffset nextObservedRunCacheCleanupUtc = DateTimeOffset.MinValue;

    public async Task<BtcUpDown5mPaperStrategyResult> ProcessAsync(CancellationToken cancellationToken = default)
    {
        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0);
        }

        var cycleId = Guid.NewGuid();
        const string cycleKind = "main";
        var now = GetUtcNow();
        controlState.RecordLoop("BTC5mStrategy loading runtime settings", null);
        var configuredVariants = GetConfiguredVariants();
        var strategySettings = await strategyStateProvider.GetStrategySettingsAsync(cancellationToken);
        var entryVariants = configuredVariants
            .Where(variant => GetStrategySettings(strategySettings, variant.Id).Enabled)
            .Where(variant => !IsChildMirrorStrategy(variant))
            .Where(variant => !UsesPreviousResultEntryFlow(variant))
            .ToArray();
        entryVariants = OrderEntryVariantsForPlacement(entryVariants, strategySettings);

        if (entryVariants.Length == 0)
        {
            var settledRuns = await SettleDueRunsAsync(now, StrategyIds.UpDown5mStrategyVariants, cancellationToken);
            if (settledRuns > 0)
            {
                await strategyStateProvider.ForceRefreshAsync(cancellationToken);
                strategySettings = await strategyStateProvider.GetStrategySettingsAsync(cancellationToken);
            }

            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, settledRuns);
        }

        var liveEntryVariants = entryVariants
            .Where(variant => GetStrategySettings(strategySettings, variant.Id).EffectiveLiveStakes)
            .ToArray();
        var nonLiveEntryVariants = entryVariants
            .Where(variant => !GetStrategySettings(strategySettings, variant.Id).EffectiveLiveStakes)
            .ToArray();

        var liveFlow = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            "Live",
            "entry_variant_flow",
            detail: null,
            liveEntryVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await ProcessEntryVariantFlowAsync(
                cycleId,
                cycleKind,
                "Live",
                liveEntryVariants,
                strategySettings,
                previousResultReadyOnly: false,
                token,
                dueEntryLock: mainDueEntryProcessingLock),
            CreateStageOutcome,
            cancellationToken);
        strategySettings = liveFlow.StrategySettings;

        var nonLiveFlow = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            "NonLive",
            "entry_variant_flow",
            detail: null,
            nonLiveEntryVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await ProcessEntryVariantFlowAsync(
                cycleId,
                cycleKind,
                "NonLive",
                nonLiveEntryVariants,
                strategySettings,
                previousResultReadyOnly: false,
                token,
                dueEntryLock: mainDueEntryProcessingLock),
            CreateStageOutcome,
            cancellationToken);
        strategySettings = nonLiveFlow.StrategySettings;

        var entryVariantIds = entryVariants
            .Select(variant => StrategyIds.Normalize(variant.Id))
            .ToHashSet();
        var remainingSettlementVariants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => !entryVariantIds.Contains(StrategyIds.Normalize(variant.Id)))
            .ToArray();
        controlState.RecordLoop($"BTC5mStrategy settling remaining disabled/nonconfigured runs. Variants={remainingSettlementVariants.Length}", null);
        var settledRemainingRuns = await SettleDueRunsAsync(
            GetUtcNow(),
            remainingSettlementVariants,
            cancellationToken);
        if (settledRemainingRuns > 0)
        {
            await strategyStateProvider.ForceRefreshAsync(cancellationToken);
            strategySettings = await strategyStateProvider.GetStrategySettingsAsync(cancellationToken);
        }

        var observedMarkets = liveFlow.ObservedMarkets
            .Concat(nonLiveFlow.ObservedMarkets)
            .GroupBy(market => market.MarketId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        controlState.RecordLoop("BTC5mStrategy capturing close-book snapshots", null);
        await CaptureClosingOrderBookSnapshotsAsync(GetUtcNow(), observedMarkets, cancellationToken);
        await RefreshLiveStrategyPrioritySnapshotIfDueAsync(entryVariants, strategySettings, cancellationToken);
        return new BtcUpDown5mPaperStrategyResult(
            liveFlow.Result.MarketsObserved + nonLiveFlow.Result.MarketsObserved,
            liveFlow.Result.EntriesPlaced + nonLiveFlow.Result.EntriesPlaced,
            liveFlow.Result.RunsSkipped + nonLiveFlow.Result.RunsSkipped,
            liveFlow.Result.RunsSettled + nonLiveFlow.Result.RunsSettled + settledRemainingRuns);
    }

    public async Task<BtcUpDown5mPaperStrategyResult> ProcessDueEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0);
        }

        var cycleId = Guid.NewGuid();
        const string cycleKind = "main_due";
        controlState.RecordLoop("BTC5mStrategy due-entry cycle loading runtime settings", null);
        var configuredVariants = GetConfiguredVariants();
        var strategySettings = await strategyStateProvider.GetStrategySettingsAsync(cancellationToken);
        var entryVariants = configuredVariants
            .Where(variant => GetStrategySettings(strategySettings, variant.Id).Enabled)
            .Where(variant => !IsChildMirrorStrategy(variant))
            .Where(variant => !UsesPreviousResultEntryFlow(variant))
            .Where(variant => !IsDiffCounterTrendOpeningLimitEntry(variant))
            .ToArray();
        entryVariants = OrderEntryVariantsForPlacement(entryVariants, strategySettings);

        if (entryVariants.Length == 0)
        {
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0);
        }

        var liveEntryVariants = entryVariants
            .Where(variant => GetStrategySettings(strategySettings, variant.Id).EffectiveLiveStakes)
            .ToArray();
        var nonLiveEntryVariants = entryVariants
            .Where(variant => !GetStrategySettings(strategySettings, variant.Id).EffectiveLiveStakes)
            .ToArray();

        var liveFlow = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            "LiveDue",
            "due_entry_flow",
            detail: null,
            liveEntryVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await ProcessDueEntryVariantFlowAsync(
                cycleId,
                cycleKind,
                "LiveDue",
                liveEntryVariants,
                strategySettings,
                previousResultReadyOnly: false,
                token,
                mainDueEntryProcessingLock),
            CreateStageOutcome,
            cancellationToken);
        strategySettings = liveFlow.StrategySettings;

        var nonLiveFlow = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            "NonLiveDue",
            "due_entry_flow",
            detail: null,
            nonLiveEntryVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await ProcessDueEntryVariantFlowAsync(
                cycleId,
                cycleKind,
                "NonLiveDue",
                nonLiveEntryVariants,
                strategySettings,
                previousResultReadyOnly: false,
                token,
                mainDueEntryProcessingLock),
            CreateStageOutcome,
            cancellationToken);
        strategySettings = nonLiveFlow.StrategySettings;

        await RefreshLiveStrategyPrioritySnapshotIfDueAsync(entryVariants, strategySettings, cancellationToken);
        return new BtcUpDown5mPaperStrategyResult(
            liveFlow.Result.MarketsObserved + nonLiveFlow.Result.MarketsObserved,
            liveFlow.Result.EntriesPlaced + nonLiveFlow.Result.EntriesPlaced,
            liveFlow.Result.RunsSkipped + nonLiveFlow.Result.RunsSkipped,
            liveFlow.Result.RunsSettled + nonLiveFlow.Result.RunsSettled);
    }

    private async Task<EntryVariantFlowResult> ProcessEntryVariantFlowAsync(
        Guid cycleId,
        string cycleKind,
        string flowName,
        IReadOnlyList<BtcUpDown5mStrategyVariant> entryVariants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        bool previousResultReadyOnly,
        CancellationToken cancellationToken,
        SemaphoreSlim? dueEntryLock = null)
    {
        if (entryVariants.Count == 0)
        {
            return EntryVariantFlowResult.Empty(strategySettings);
        }

        var now = GetUtcNow();
        var diffCounterEntryVariants = entryVariants
            .Where(IsDiffCounterTrendOpeningLimitEntry)
            .ToArray();
        if (diffCounterEntryVariants.Length > 0)
        {
            controlState.RecordLoop($"BTC5mStrategy {flowName} initializing Diff counters. Variants={diffCounterEntryVariants.Length}", null);
            await EnsureDiffCounterStatesInitializedAsync(diffCounterEntryVariants, now, cancellationToken);
        }

        var makerEntryVariants = entryVariants.Where(IsFixedOutcomeMaker).ToArray();
        var nonMakerEntryVariants = entryVariants
            .Where(variant => !IsFixedOutcomeMaker(variant))
            .Where(variant => !IsDiffCounterTrendOpeningLimitEntry(variant))
            .ToArray();
        var preOpenEntryVariants = nonMakerEntryVariants
            .Where(IsPreOpenTimedOpeningLimitEntry)
            .ToArray();
        var preOpenSellExitVariants = nonMakerEntryVariants
            .Where(IsPreOpenFixedDirectionSellExit)
            .ToArray();
        var regularEntryVariants = nonMakerEntryVariants
            .Where(variant => !IsPreOpenTimedOpeningLimitEntry(variant))
            .ToArray();

        controlState.RecordLoop($"BTC5mStrategy {flowName} placing regular due entries before observe. Variants={regularEntryVariants.Length}", null);
        var (regularEntriesPlacedBeforeObserve, regularEntrySkippedBeforeObserve) = await PlaceDueEntriesAsync(
            GetUtcNow(),
            regularEntryVariants,
            strategySettings,
            previousResultReadyOnly,
            cycleId,
            cycleKind,
            flowName,
            "regular_due_entries_before_observe",
            cancellationToken,
            dueEntryLock);
        controlState.RecordLoop($"BTC5mStrategy {flowName} placing PreOpen due entries before observe. Variants={preOpenEntryVariants.Length}", null);
        var (preOpenEntriesPlacedBeforeObserve, preOpenEntrySkippedBeforeObserve) = await PlaceDuePreOpenEntriesAsync(
            GetUtcNow(),
            preOpenEntryVariants,
            strategySettings,
            cycleId,
            cycleKind,
            flowName,
            "preopen_due_entries_before_observe",
            cancellationToken,
            dueEntryLock);
        controlState.RecordLoop($"BTC5mStrategy {flowName} observing markets", null);
        var observeResult = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            flowName,
            "observe_markets",
            detail: null,
            nonMakerEntryVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await ObserveMarketsAsync(
                GetUtcNow(),
                nonMakerEntryVariants,
                strategySettings,
                token),
            CreateStageOutcome,
            cancellationToken);
        var observed = observeResult.Observed;
        var observeSkipped = observeResult.Skipped;
        controlState.RecordLoop($"BTC5mStrategy {flowName} processing maker maxima. Variants={makerEntryVariants.Length}", null);
        var makerResult = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            flowName,
            "maker_high_water",
            detail: null,
            makerEntryVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await ProcessMakerHighWaterOrdersAsync(
                GetUtcNow(),
                makerEntryVariants,
                strategySettings,
                token),
            CreateStageOutcome,
            cancellationToken);
        controlState.RecordLoop($"BTC5mStrategy {flowName} placing regular due entries after observe. Variants={regularEntryVariants.Length}", null);
        var (regularEntriesPlacedAfterObserve, regularEntrySkippedAfterObserve) = await PlaceDueEntriesAsync(
            GetUtcNow(),
            regularEntryVariants,
            strategySettings,
            previousResultReadyOnly,
            cycleId,
            cycleKind,
            flowName,
            "regular_due_entries_after_observe",
            cancellationToken,
            dueEntryLock);
        controlState.RecordLoop($"BTC5mStrategy {flowName} placing PreOpen due entries after observe. Variants={preOpenEntryVariants.Length}", null);
        var (preOpenEntriesPlacedAfterObserve, preOpenEntrySkippedAfterObserve) = await PlaceDuePreOpenEntriesAsync(
            GetUtcNow(),
            preOpenEntryVariants,
            strategySettings,
            cycleId,
            cycleKind,
            flowName,
            "preopen_due_entries_after_observe",
            cancellationToken,
            dueEntryLock);
        controlState.RecordLoop($"BTC5mStrategy {flowName} checking PreOpen Sell exits. Variants={preOpenSellExitVariants.Length}", null);
        await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            flowName,
            "preopen_sell_exits",
            detail: null,
            preOpenSellExitVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await PlaceDuePreOpenSellExitsAsync(
                GetUtcNow(),
                preOpenSellExitVariants,
                token,
                dueEntryLock),
            cancellationToken);
        controlState.RecordLoop($"BTC5mStrategy {flowName} settling due runs after entries. Variants={entryVariants.Count}", null);
        var settledRunsAfterEntries = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            flowName,
            "settle_due_runs_after_entries",
            detail: null,
            entryVariants.Count,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await SettleDueRunsAsync(GetUtcNow(), entryVariants, token),
            result => new StrategyStageOutcome(RunsSettled: result),
            cancellationToken);
        if (settledRunsAfterEntries > 0)
        {
            await strategyStateProvider.ForceRefreshAsync(cancellationToken);
            strategySettings = await strategyStateProvider.GetStrategySettingsAsync(cancellationToken);
        }

        var result = new BtcUpDown5mPaperStrategyResult(
            observed + makerResult.MarketsObserved,
            preOpenEntriesPlacedBeforeObserve + regularEntriesPlacedBeforeObserve +
            preOpenEntriesPlacedAfterObserve + regularEntriesPlacedAfterObserve +
            makerResult.EntriesPlaced,
            observeSkipped + preOpenEntrySkippedBeforeObserve + regularEntrySkippedBeforeObserve +
            preOpenEntrySkippedAfterObserve + regularEntrySkippedAfterObserve +
            makerResult.RunsSkipped,
            settledRunsAfterEntries);
        return new EntryVariantFlowResult(result, observeResult.Markets, strategySettings);
    }

    private async Task<EntryVariantFlowResult> ProcessDueEntryVariantFlowAsync(
        Guid cycleId,
        string cycleKind,
        string flowName,
        IReadOnlyList<BtcUpDown5mStrategyVariant> entryVariants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        bool previousResultReadyOnly,
        CancellationToken cancellationToken,
        SemaphoreSlim? dueEntryLock = null)
    {
        if (entryVariants.Count == 0)
        {
            return EntryVariantFlowResult.Empty(strategySettings);
        }

        var nonMakerEntryVariants = entryVariants
            .Where(variant => !IsFixedOutcomeMaker(variant))
            .Where(variant => !IsDiffCounterTrendOpeningLimitEntry(variant))
            .ToArray();
        var preOpenEntryVariants = nonMakerEntryVariants
            .Where(IsPreOpenTimedOpeningLimitEntry)
            .ToArray();
        var preOpenSellExitVariants = nonMakerEntryVariants
            .Where(IsPreOpenFixedDirectionSellExit)
            .ToArray();
        var regularEntryVariants = nonMakerEntryVariants
            .Where(variant => !IsPreOpenTimedOpeningLimitEntry(variant))
            .ToArray();

        controlState.RecordLoop($"BTC5mStrategy {flowName} fast placing PreOpen due entries. Variants={preOpenEntryVariants.Length}", null);
        var (preOpenEntriesPlaced, preOpenRunsSkipped) = await PlaceDuePreOpenEntriesAsync(
            GetUtcNow(),
            preOpenEntryVariants,
            strategySettings,
            cycleId,
            cycleKind,
            flowName,
            "preopen_due_entries",
            cancellationToken,
            dueEntryLock);

        controlState.RecordLoop($"BTC5mStrategy {flowName} fast placing regular due entries. Variants={regularEntryVariants.Length}", null);
        var (regularEntriesPlaced, regularRunsSkipped) = await PlaceDueEntriesAsync(
            GetUtcNow(),
            regularEntryVariants,
            strategySettings,
            previousResultReadyOnly,
            cycleId,
            cycleKind,
            flowName,
            "regular_due_entries",
            cancellationToken,
            dueEntryLock);

        controlState.RecordLoop($"BTC5mStrategy {flowName} fast checking PreOpen Sell exits. Variants={preOpenSellExitVariants.Length}", null);
        await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            flowName,
            "preopen_sell_exits",
            detail: null,
            preOpenSellExitVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await PlaceDuePreOpenSellExitsAsync(
                GetUtcNow(),
                preOpenSellExitVariants,
                token,
                dueEntryLock),
            cancellationToken);

        var result = new BtcUpDown5mPaperStrategyResult(
            MarketsObserved: 0,
            EntriesPlaced: regularEntriesPlaced + preOpenEntriesPlaced,
            RunsSkipped: regularRunsSkipped + preOpenRunsSkipped,
            RunsSettled: 0);
        return new EntryVariantFlowResult(result, [], strategySettings);
    }

    public async Task<BtcUpDown5mPaperStrategyResult> ProcessDiffCounterDueEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        var observeResult = await ProcessDiffCounterObserveAsync(cancellationToken);
        var dueResult = await ProcessDiffCounterFastDueEntriesAsync(cancellationToken);
        return new BtcUpDown5mPaperStrategyResult(
            observeResult.MarketsObserved + dueResult.MarketsObserved,
            observeResult.EntriesPlaced + dueResult.EntriesPlaced,
            observeResult.RunsSkipped + dueResult.RunsSkipped,
            observeResult.RunsSettled + dueResult.RunsSettled);
    }

    public async Task<BtcUpDown5mPaperStrategyResult> ProcessDiffCounterObserveAsync(
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0);
        }

        var cycleId = Guid.NewGuid();
        const string cycleKind = "fast_diff_observe";
        var now = GetUtcNow();
        controlState.RecordLoop("BTC5mStrategy fast Diff observe cycle loading runtime settings", null);
        var (diffCounterEntryVariants, strategySettings) = await GetEnabledDiffCounterEntryVariantsAsync(cancellationToken);
        if (diffCounterEntryVariants.Length == 0)
        {
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0);
        }

        controlState.RecordLoop($"BTC5mStrategy fast Diff initializing counters. Variants={diffCounterEntryVariants.Length}", null);
        await EnsureDiffCounterStatesInitializedAsync(diffCounterEntryVariants, now, cancellationToken);

        controlState.RecordLoop($"BTC5mStrategy fast Diff observing markets. Variants={diffCounterEntryVariants.Length}", null);
        var observeResult = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            "FastDiffObserve",
            "observe_markets",
            detail: null,
            diffCounterEntryVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await ObserveMarketsAsync(
                GetUtcNow(),
                diffCounterEntryVariants,
                strategySettings,
                token),
            CreateStageOutcome,
            cancellationToken);
        return new BtcUpDown5mPaperStrategyResult(
            observeResult.Observed,
            0,
            observeResult.Skipped,
            0);
    }

    public async Task<BtcUpDown5mPaperStrategyResult> ProcessDiffCounterFastDueEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0);
        }

        var cycleId = Guid.NewGuid();
        const string cycleKind = "fast_diff_due";
        var now = GetUtcNow();
        controlState.RecordLoop("BTC5mStrategy fast Diff due-entry cycle loading runtime settings", null);
        var (diffCounterEntryVariants, strategySettings) = await GetEnabledDiffCounterEntryVariantsAsync(cancellationToken);
        if (diffCounterEntryVariants.Length == 0)
        {
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0);
        }

        controlState.RecordLoop($"BTC5mStrategy fast Diff initializing counters. Variants={diffCounterEntryVariants.Length}", null);
        await EnsureDiffCounterStatesInitializedAsync(diffCounterEntryVariants, now, cancellationToken);

        controlState.RecordLoop($"BTC5mStrategy fast Diff placing due entries. Variants={diffCounterEntryVariants.Length}", null);
        var regularDiffCounterEntryVariants = diffCounterEntryVariants
            .Where(variant => !IsPreOpenTimedOpeningLimitEntry(variant))
            .ToArray();
        var preOpenDiffCounterEntryVariants = diffCounterEntryVariants
            .Where(IsPreOpenTimedOpeningLimitEntry)
            .ToArray();
        var (preOpenEntriesPlaced, preOpenRunsSkipped) = await PlaceDuePreOpenEntriesAsync(
            GetUtcNow(),
            preOpenDiffCounterEntryVariants,
            strategySettings,
            cycleId,
            cycleKind,
            "FastDiffDue",
            "preopen_due_entries",
            cancellationToken);
        var (regularEntriesPlaced, regularRunsSkipped) = await PlaceDueEntriesAsync(
            GetUtcNow(),
            regularDiffCounterEntryVariants,
            strategySettings,
            previousResultReadyOnly: false,
            cycleId,
            cycleKind,
            "FastDiffDue",
            "regular_due_entries",
            cancellationToken);
        await RefreshLiveStrategyPrioritySnapshotIfDueAsync(diffCounterEntryVariants, strategySettings, cancellationToken);
        return new BtcUpDown5mPaperStrategyResult(
            0,
            regularEntriesPlaced + preOpenEntriesPlaced,
            regularRunsSkipped + preOpenRunsSkipped,
            0);
    }

    private async Task<(BtcUpDown5mStrategyVariant[] EntryVariants, IReadOnlyDictionary<Guid, StrategyRuntimeSettings> StrategySettings)> GetEnabledDiffCounterEntryVariantsAsync(
        CancellationToken cancellationToken)
    {
        var configuredVariants = GetConfiguredVariants();
        var strategySettings = await strategyStateProvider.GetStrategySettingsAsync(cancellationToken);
        var diffCounterEntryVariants = configuredVariants
            .Where(variant => GetStrategySettings(strategySettings, variant.Id).Enabled)
            .Where(IsDiffCounterTrendOpeningLimitEntry)
            .ToArray();
        return (OrderEntryVariantsForPlacement(diffCounterEntryVariants, strategySettings), strategySettings);
    }

    public async Task<BtcUpDown5mPaperStrategyResult> ProcessPreviousResultDueEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        var observeResult = await ProcessPreviousResultObserveAsync(cancellationToken);
        var dueResult = await ProcessPreviousResultFastDueEntriesAsync(cancellationToken);
        return new BtcUpDown5mPaperStrategyResult(
            observeResult.MarketsObserved + dueResult.MarketsObserved,
            observeResult.EntriesPlaced + dueResult.EntriesPlaced,
            observeResult.RunsSkipped + dueResult.RunsSkipped,
            observeResult.RunsSettled + dueResult.RunsSettled);
    }

    public async Task<BtcUpDown5mPaperStrategyResult> ProcessPreviousResultFastDueEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0);
        }

        var cycleId = Guid.NewGuid();
        const string cycleKind = "previous_result_due";
        controlState.RecordLoop("BTC5mStrategy previous-result due-entry cycle loading runtime settings", null);
        var (entryVariants, strategySettings) = await GetEnabledPreviousResultEntryVariantsAsync(cancellationToken);
        if (entryVariants.Length == 0)
        {
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0);
        }

        var dueFlow = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            "PreviousResultDue",
            "due_entry_flow",
            detail: null,
            entryVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await ProcessDueEntryVariantFlowAsync(
                cycleId,
                cycleKind,
                "PreviousResultDue",
                entryVariants,
                strategySettings,
                previousResultReadyOnly: true,
                token),
            CreateStageOutcome,
            cancellationToken);
        strategySettings = dueFlow.StrategySettings;

        await RefreshLiveStrategyPrioritySnapshotIfDueAsync(entryVariants, strategySettings, cancellationToken);

        return dueFlow.Result;
    }

    public async Task<BtcUpDown5mPaperStrategyResult> ProcessPreviousResultObserveAsync(
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0);
        }

        var cycleId = Guid.NewGuid();
        const string cycleKind = "previous_result_observe";
        controlState.RecordLoop("BTC5mStrategy previous-result observe cycle loading runtime settings", null);
        var (entryVariants, strategySettings) = await GetEnabledPreviousResultEntryVariantsAsync(cancellationToken);
        if (entryVariants.Length == 0)
        {
            return new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0);
        }

        var liveEntryVariants = entryVariants
            .Where(variant => GetStrategySettings(strategySettings, variant.Id).EffectiveLiveStakes)
            .ToArray();
        var nonLiveEntryVariants = entryVariants
            .Where(variant => !GetStrategySettings(strategySettings, variant.Id).EffectiveLiveStakes)
            .ToArray();

        var liveFlow = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            "PreviousResultLiveObserve",
            "observe_flow",
            detail: null,
            liveEntryVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await ProcessObserveEntryVariantFlowAsync(
                cycleId,
                cycleKind,
                "PreviousResultLiveObserve",
                liveEntryVariants,
                strategySettings,
                token),
            CreateStageOutcome,
            cancellationToken);

        var nonLiveFlow = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            "PreviousResultNonLiveObserve",
            "observe_flow",
            detail: null,
            nonLiveEntryVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await ProcessObserveEntryVariantFlowAsync(
                cycleId,
                cycleKind,
                "PreviousResultNonLiveObserve",
                nonLiveEntryVariants,
                strategySettings,
                token),
            CreateStageOutcome,
            cancellationToken);

        var observedMarkets = liveFlow.ObservedMarkets
            .Concat(nonLiveFlow.ObservedMarkets)
            .GroupBy(market => market.MarketId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        controlState.RecordLoop("BTC5mStrategy previous-result observe capturing close-book snapshots", null);
        await CaptureClosingOrderBookSnapshotsAsync(GetUtcNow(), observedMarkets, cancellationToken);
        await RefreshLiveStrategyPrioritySnapshotIfDueAsync(entryVariants, strategySettings, cancellationToken);

        return new BtcUpDown5mPaperStrategyResult(
            liveFlow.Result.MarketsObserved + nonLiveFlow.Result.MarketsObserved,
            0,
            liveFlow.Result.RunsSkipped + nonLiveFlow.Result.RunsSkipped,
            0);
    }

    private async Task<EntryVariantFlowResult> ProcessObserveEntryVariantFlowAsync(
        Guid cycleId,
        string cycleKind,
        string flowName,
        IReadOnlyList<BtcUpDown5mStrategyVariant> entryVariants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        CancellationToken cancellationToken)
    {
        if (entryVariants.Count == 0)
        {
            return EntryVariantFlowResult.Empty(strategySettings);
        }

        var nonMakerEntryVariants = entryVariants
            .Where(variant => !IsFixedOutcomeMaker(variant))
            .Where(variant => !IsDiffCounterTrendOpeningLimitEntry(variant))
            .ToArray();
        controlState.RecordLoop($"BTC5mStrategy {flowName} observing markets. Variants={nonMakerEntryVariants.Length}", null);
        var observeResult = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            flowName,
            "observe_markets",
            detail: null,
            nonMakerEntryVariants.Length,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await ObserveMarketsAsync(
                GetUtcNow(),
                nonMakerEntryVariants,
                strategySettings,
                token),
            CreateStageOutcome,
            cancellationToken);
        var result = new BtcUpDown5mPaperStrategyResult(
            observeResult.Observed,
            0,
            observeResult.Skipped,
            0);
        return new EntryVariantFlowResult(result, observeResult.Markets, strategySettings);
    }

    private async Task<(BtcUpDown5mStrategyVariant[] EntryVariants, IReadOnlyDictionary<Guid, StrategyRuntimeSettings> StrategySettings)> GetEnabledPreviousResultEntryVariantsAsync(
        CancellationToken cancellationToken)
    {
        var configuredVariants = GetConfiguredVariants();
        var strategySettings = await strategyStateProvider.GetStrategySettingsAsync(cancellationToken);
        var entryVariants = configuredVariants
            .Where(variant => GetStrategySettings(strategySettings, variant.Id).Enabled)
            .Where(UsesPreviousResultEntryFlow)
            .ToArray();
        return (OrderEntryVariantsForPlacement(entryVariants, strategySettings), strategySettings);
    }

    private DateTimeOffset GetUtcNow()
    {
        return clock.GetUtcNow();
    }

    private IReadOnlyList<BtcUpDown5mStrategyVariant> GetConfiguredVariants()
    {
        if (options.EnabledVariantCodes is null || options.EnabledVariantCodes.Count == 0)
        {
            return StrategyIds.UpDown5mStrategyVariants;
        }

        var enabledCodes = options.EnabledVariantCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return StrategyIds.UpDown5mStrategyVariants
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
            : StrategyRuntimeSettings.Default(normalizedStrategyId) with { Enabled = false };
    }

    private BtcUpDown5mStrategyVariant[] OrderEntryVariantsForPlacement(
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings)
    {
        if (variants.Count <= 1)
        {
            return variants.ToArray();
        }

        var snapshot = liveStrategyPrioritySnapshot;
        return variants
            .Select((variant, index) =>
            {
                var settings = GetStrategySettings(strategySettings, variant.Id);
                var effectiveLiveStakes = settings.EffectiveLiveStakes;
                return new
                {
                    Variant = variant,
                    Index = index,
                    EffectiveLiveStakes = effectiveLiveStakes,
                    LiveRealizedPnlUsd = effectiveLiveStakes
                        ? GetCachedLiveRealizedPnlUsd(snapshot, variant.Id)
                        : 0m
                };
            })
            .OrderByDescending(item => item.EffectiveLiveStakes)
            .ThenByDescending(item => item.LiveRealizedPnlUsd)
            .ThenBy(item => item.Index)
            .Select(item => item.Variant)
            .ToArray();
    }

    private IReadOnlyList<StrategyMarketPaperRun> OrderDueEntryRunsForPlacement(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        IReadOnlyDictionary<Guid, BtcUpDown5mStrategyVariant> variantsById,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings)
    {
        if (runs.Count <= 1)
        {
            return runs;
        }

        var snapshot = liveStrategyPrioritySnapshot;
        return runs
            .Select((run, index) =>
            {
                var strategyId = StrategyIds.Normalize(run.StrategyId);
                var settings = GetStrategySettings(strategySettings, strategyId);
                var effectiveLiveStakes = settings.EffectiveLiveStakes && variantsById.ContainsKey(strategyId);
                return new
                {
                    Run = run,
                    Index = index,
                    EffectiveLiveStakes = effectiveLiveStakes,
                    LiveRealizedPnlUsd = effectiveLiveStakes
                        ? GetCachedLiveRealizedPnlUsd(snapshot, strategyId)
                        : 0m
                };
            })
            .OrderBy(item => item.Run.EntryDueAtUtc)
            .ThenByDescending(item => item.EffectiveLiveStakes)
            .ThenByDescending(item => item.LiveRealizedPnlUsd)
            .ThenBy(item => item.Run.DetectedAtUtc)
            .ThenBy(item => item.Run.StrategyId)
            .ThenBy(item => item.Index)
            .Select(item => item.Run)
            .ToArray();
    }

    private static decimal GetCachedLiveRealizedPnlUsd(LiveStrategyPrioritySnapshot snapshot, Guid strategyId)
    {
        return snapshot.LiveRealizedPnlByStrategy.TryGetValue(StrategyIds.Normalize(strategyId), out var value)
            ? value
            : 0m;
    }

    private async Task RefreshLiveStrategyPrioritySnapshotIfDueAsync(
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        CancellationToken cancellationToken)
    {
        var nowUtc = GetUtcNow();
        var liveStrategyIds = variants
            .Select(variant => StrategyIds.Normalize(variant.Id))
            .Where(strategyId => GetStrategySettings(strategySettings, strategyId).EffectiveLiveStakes)
            .Distinct()
            .ToArray();
        if (nowUtc - liveStrategyPrioritySnapshot.RefreshedAtUtc < LiveStrategyPriorityRefreshInterval &&
            HasCachedLiveStrategyIds(liveStrategyPrioritySnapshot, liveStrategyIds))
        {
            return;
        }

        await liveStrategyPriorityRefreshLock.WaitAsync(cancellationToken);
        try
        {
            nowUtc = GetUtcNow();
            if (nowUtc - liveStrategyPrioritySnapshot.RefreshedAtUtc < LiveStrategyPriorityRefreshInterval &&
                HasCachedLiveStrategyIds(liveStrategyPrioritySnapshot, liveStrategyIds))
            {
                return;
            }

            if (liveStrategyIds.Length == 0)
            {
                liveStrategyPrioritySnapshot = new LiveStrategyPrioritySnapshot(
                    new Dictionary<Guid, decimal>(),
                    nowUtc);
                return;
            }

            var liveRealizedByStrategy = await repository.GetLiveRealizedPnlByStrategyAsync(
                liveStrategyIds,
                cancellationToken);
            liveStrategyPrioritySnapshot = new LiveStrategyPrioritySnapshot(
                liveStrategyIds.ToDictionary(
                    strategyId => strategyId,
                    strategyId => liveRealizedByStrategy.TryGetValue(strategyId, out var realizedPnlUsd)
                        ? realizedPnlUsd
                        : 0m),
                nowUtc);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh BTC Up or Down 5m live strategy priority snapshot.");
        }
        finally
        {
            liveStrategyPriorityRefreshLock.Release();
        }
    }

    private static bool HasCachedLiveStrategyIds(
        LiveStrategyPrioritySnapshot snapshot,
        IReadOnlyCollection<Guid> liveStrategyIds)
    {
        foreach (var strategyId in liveStrategyIds)
        {
            if (!snapshot.LiveRealizedPnlByStrategy.ContainsKey(strategyId))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<ObserveMarketsResult> ObserveMarketsAsync(
        DateTimeOffset nowUtc,
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        CancellationToken cancellationToken)
    {
        var observed = 0;
        var skipped = 0;
        var observedMarkets = new List<PolymarketGammaMarket>();
        var btcVariants = variants
            .Where(IsBtcReferenceVariant)
            .ToArray();
        if (btcVariants.Length > 0)
        {
            var btcMarkets = await repository.GetBtcUpDownStrategyGammaMarketsAsync(
                options.MaxMarketsPerCycle,
                cancellationToken);
            observedMarkets.AddRange(btcMarkets);
            var result = await ObserveBtcMarketsAsync(nowUtc, btcMarkets, btcVariants, strategySettings, cancellationToken);
            observed += result.Observed;
            skipped += result.Skipped;
        }

        var cryptoVariants = variants
            .Where(IsCryptoReferenceVariant)
            .ToArray();
        if (cryptoVariants.Length > 0)
        {
            var assetSymbols = cryptoVariants
                .Select(GetReferenceAssetSymbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var cryptoMarkets = await repository.GetCryptoUpDown5mGammaMarketsAsync(
                assetSymbols,
                options.MaxMarketsPerCycle,
                cancellationToken);
            observedMarkets.AddRange(cryptoMarkets);
            var result = await ObserveCryptoMarketsAsync(
                nowUtc,
                cryptoMarkets,
                cryptoVariants,
                strategySettings,
                cancellationToken);
            observed += result.Observed;
            skipped += result.Skipped;
        }

        return new ObserveMarketsResult(observed, skipped, observedMarkets);
    }

    private async Task<ObserveCounters> ObserveBtcMarketsAsync(
        DateTimeOffset nowUtc,
        IReadOnlyList<PolymarketGammaMarket> markets,
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        CancellationToken cancellationToken)
    {
        var candidateRuns = new List<StrategyMarketPaperRun>();
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
                    !UsesPreviousCloseBookMarketResult(variant) &&
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

                candidateRuns.Add(run);
            }
        }

        return await PersistObservedRunsAsync(nowUtc, candidateRuns, cancellationToken);
    }

    private async Task<ObserveCounters> ObserveCryptoMarketsAsync(
        DateTimeOffset nowUtc,
        IReadOnlyList<PolymarketGammaMarket> markets,
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        CancellationToken cancellationToken)
    {
        var candidateRuns = new List<StrategyMarketPaperRun>();
        var assetSymbols = variants
            .Select(GetReferenceAssetSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var variantsByAsset = variants
            .GroupBy(GetReferenceAssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var market in markets)
        {
            if (!CryptoUpDown5mMarketAnalyzer.TryGetAssetSymbol(market, assetSymbols, out var assetSymbol) ||
                !variantsByAsset.TryGetValue(assetSymbol, out var marketVariants))
            {
                continue;
            }

            var marketInterval = CryptoUpDown5mMarketAnalyzer.GetMarketInterval(market);
            if (marketInterval is null)
            {
                continue;
            }

            var windowStart = CryptoUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
            if (!ShouldObserveMarketWindow(windowStart, market.EndDateUtc, nowUtc))
            {
                continue;
            }

            foreach (var variant in marketVariants)
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
                    !UsesPreviousCloseBookMarketResult(variant) &&
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

                candidateRuns.Add(run);
            }
        }

        return await PersistObservedRunsAsync(nowUtc, candidateRuns, cancellationToken);
    }

    private async Task<ObserveCounters> PersistObservedRunsAsync(
        DateTimeOffset nowUtc,
        IReadOnlyList<StrategyMarketPaperRun> candidateRuns,
        CancellationToken cancellationToken)
    {
        CleanupObservedRunCache(nowUtc);
        if (candidateRuns.Count == 0)
        {
            return new ObserveCounters(0, 0);
        }

        var reservations = new List<ObservedRunReservation>(candidateRuns.Count);
        foreach (var run in candidateRuns)
        {
            var key = new StrategyMarketRunCacheKey(StrategyIds.Normalize(run.StrategyId), run.MarketId);
            var expiresAtUtc = (run.MarketEndUtc ?? run.EntryDueAtUtc)
                .Add(MarketObserveBehindWindow)
                .Add(ObservedRunCacheExpirationBuffer);
            if (observedRunCache.TryAdd(key, expiresAtUtc))
            {
                reservations.Add(new ObservedRunReservation(key, run));
            }
        }

        if (reservations.Count == 0)
        {
            return new ObserveCounters(0, 0);
        }

        IReadOnlySet<Guid> insertedIds;
        try
        {
            insertedIds = await repository.TryAddStrategyMarketPaperRunsAsync(
                reservations.Select(reservation => reservation.Run).ToArray(),
                cancellationToken);
        }
        catch
        {
            foreach (var reservation in reservations)
            {
                observedRunCache.TryRemove(reservation.Key, out _);
            }

            throw;
        }

        var observed = 0;
        var skipped = 0;
        foreach (var reservation in reservations)
        {
            if (!insertedIds.Contains(reservation.Run.Id))
            {
                continue;
            }

            if (string.Equals(
                reservation.Run.Status,
                StrategyMarketPaperRunStatuses.Skipped,
                StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
            }
            else
            {
                observed++;
            }
        }

        return new ObserveCounters(observed, skipped);
    }

    private void CleanupObservedRunCache(DateTimeOffset nowUtc)
    {
        lock (observedRunCacheCleanupSync)
        {
            if (nowUtc < nextObservedRunCacheCleanupUtc)
            {
                return;
            }

            nextObservedRunCacheCleanupUtc = nowUtc.Add(ObservedRunCacheCleanupInterval);
        }

        foreach (var item in observedRunCache)
        {
            if (item.Value <= nowUtc)
            {
                observedRunCache.TryRemove(item.Key, out _);
            }
        }
    }

    private async Task<BtcMakerProcessResult> ProcessMakerHighWaterOrdersAsync(
        DateTimeOffset nowUtc,
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        CancellationToken cancellationToken)
    {
        if (variants.Count == 0)
        {
            return BtcMakerProcessResult.Empty;
        }

        CleanupMakerHighWaterStates(nowUtc);

        var markets = await repository.GetBtcUpDownStrategyGammaMarketsAsync(
            options.MaxMarketsPerCycle,
            cancellationToken);
        var observed = 0;
        var entriesPlaced = 0;
        var runsSkipped = 0;
        var orderedVariants = OrderEntryVariantsForPlacement(variants, strategySettings);

        foreach (var market in markets)
        {
            var marketInterval = BtcUpDown5mMarketAnalyzer.GetMarketInterval(market);
            if (marketInterval is null)
            {
                continue;
            }

            var windowStart = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
            if (!ShouldObserveMarketWindow(windowStart, market.EndDateUtc, nowUtc) ||
                windowStart is null ||
                windowStart.Value > nowUtc)
            {
                continue;
            }

            foreach (var variant in orderedVariants)
            {
                if (!DoesVariantApplyToMarket(variant, marketInterval.Value) ||
                    !TryResolveFixedOutcomeDirection(variant, out var direction))
                {
                    continue;
                }

                var selectedOutcome = TrySelectOutcomeForDirection(market, direction);
                if (selectedOutcome is null || string.IsNullOrWhiteSpace(selectedOutcome.AssetId))
                {
                    runsSkipped++;
                    logger.LogInformation(
                        "BTC Up or Down 5m maker skipped. Strategy={StrategyCode} Market={MarketSlug} Reason={Reason}",
                        variant.Code,
                        market.Slug,
                        "maker_outcome_missing");
                    continue;
                }

                var orderBookLookup = await GetFreshTakerOrderBookAsync(
                    selectedOutcome.AssetId,
                    nowUtc,
                    cancellationToken);
                if (orderBookLookup.OrderBook is null)
                {
                    runsSkipped++;
                    logger.LogInformation(
                        "BTC Up or Down 5m maker skipped. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} Reason={Reason}",
                        variant.Code,
                        market.Slug,
                        selectedOutcome.Outcome,
                        orderBookLookup.RejectionReason ?? SignalReasonCodes.MissingOrderBook);
                    continue;
                }

                var orderBook = ApplyMakerOrderBookFallbacks(orderBookLookup.OrderBook, market);
                var bestAsk = TryGetBestAskFromOrderBook(orderBook);
                if (bestAsk is not > 0m)
                {
                    runsSkipped++;
                    logger.LogInformation(
                        "BTC Up or Down 5m maker skipped. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} Reason={Reason}",
                        variant.Code,
                        market.Slug,
                        selectedOutcome.Outcome,
                        SignalReasonCodes.MissingOrderBookEmptySide);
                    continue;
                }

                observed++;
                var stateKey = GetMakerStateKey(variant, market, selectedOutcome);
                var decisionSlot = GetMakerDecisionSlot(windowStart.Value, market.EndDateUtc, nowUtc);
                if (!makerHighWaterStates.TryGetValue(stateKey, out var state) ||
                    state.MarketEndUtc != market.EndDateUtc)
                {
                    makerHighWaterStates[stateKey] = new BtcMakerHighWaterState(
                        bestAsk.Value,
                        OrderSequence: 0,
                        LastDecisionSlot: decisionSlot.CurrentSlot,
                        nowUtc,
                        market.EndDateUtc);
                    logger.LogInformation(
                        "BTC Up or Down 5m maker baseline recorded. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} BestAsk={BestAsk}",
                        variant.Code,
                        market.Slug,
                        selectedOutcome.Outcome,
                        bestAsk.Value);
                    continue;
                }

                if (!decisionSlot.Available ||
                    decisionSlot.CurrentSlot <= state.LastDecisionSlot)
                {
                    makerHighWaterStates[stateKey] = state with
                    {
                        UpdatedAtUtc = nowUtc,
                        MarketEndUtc = market.EndDateUtc
                    };
                    continue;
                }

                if (bestAsk.Value <= state.MaxBestAsk)
                {
                    makerHighWaterStates[stateKey] = state with
                    {
                        LastDecisionSlot = decisionSlot.CurrentSlot,
                        UpdatedAtUtc = nowUtc,
                        MarketEndUtc = market.EndDateUtc
                    };
                    continue;
                }

                if (variant.MakerMinBestAskExclusive is { } makerMinBestAskExclusive &&
                    bestAsk.Value <= makerMinBestAskExclusive)
                {
                    makerHighWaterStates[stateKey] = state with
                    {
                        LastDecisionSlot = decisionSlot.CurrentSlot,
                        UpdatedAtUtc = nowUtc,
                        MarketEndUtc = market.EndDateUtc
                    };
                    continue;
                }

                var orderSequence = state.OrderSequence + 1;
                var settings = GetStrategySettings(strategySettings, variant.Id);
                var orderResult = await TryPlaceMakerHighWaterOrderAsync(
                    nowUtc,
                    market,
                    variant,
                    selectedOutcome,
                    orderBook,
                    orderBookLookup,
                    previousMaxBestAsk: state.MaxBestAsk,
                    currentMaxBestAsk: bestAsk.Value,
                    orderSequence,
                    decisionSlot.CurrentSlot,
                    decisionSlot.MaxSlot,
                    settings,
                    cancellationToken);
                makerHighWaterStates[stateKey] = state with
                {
                    MaxBestAsk = orderResult.Placed ? bestAsk.Value : state.MaxBestAsk,
                    OrderSequence = orderResult.Placed ? orderSequence : state.OrderSequence,
                    LastDecisionSlot = decisionSlot.CurrentSlot,
                    UpdatedAtUtc = nowUtc,
                    MarketEndUtc = market.EndDateUtc
                };
                entriesPlaced += orderResult.Placed ? 1 : 0;
                runsSkipped += orderResult.Skipped ? 1 : 0;
            }
        }

        return new BtcMakerProcessResult(observed, entriesPlaced, runsSkipped);
    }

    private async Task<BtcMakerOrderResult> TryPlaceMakerHighWaterOrderAsync(
        DateTimeOffset nowUtc,
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        BtcUpDown5mOutcomeQuote selectedOutcome,
        OrderBookSnapshot orderBook,
        TakerOrderBookLookupResult orderBookLookup,
        decimal previousMaxBestAsk,
        decimal currentMaxBestAsk,
        int orderSequence,
        int decisionSlot,
        int maxDecisionSlot,
        StrategyRuntimeSettings settings,
        CancellationToken cancellationToken)
    {
        var syntheticMarketId = GetMakerSyntheticMarketId(market, selectedOutcome, currentMaxBestAsk, orderSequence, nowUtc);
        var rawDecisionJson = BuildMakerRawDecisionJson(
            market,
            variant,
            selectedOutcome,
            orderBook,
            orderBookLookup,
            previousMaxBestAsk,
            currentMaxBestAsk,
            orderSequence,
            decisionSlot,
            maxDecisionSlot,
            syntheticMarketId);
        if (settings.IsPausedAt(nowUtc))
        {
            await RecordSkippedMakerRunAsync(
                nowUtc,
                market,
                variant,
                selectedOutcome,
                syntheticMarketId,
                StrategyPausedSkipReason,
                AttachStrategyPausedJson(rawDecisionJson, settings, nowUtc),
                settings.PaperStakeAmount,
                cancellationToken);
            return BtcMakerOrderResult.SkippedResult;
        }

        var priceDecision = ResolveMakerPostOnlyLimitPrice(
            orderBook,
            currentMaxBestAsk,
            variant.FixedLimitPrice);
        rawDecisionJson = AttachMakerPostOnlyPriceJson(rawDecisionJson, priceDecision);
        if (!priceDecision.Available || priceDecision.LimitPrice is not > 0m)
        {
            await RecordSkippedMakerRunAsync(
                nowUtc,
                market,
                variant,
                selectedOutcome,
                syntheticMarketId,
                priceDecision.RejectionReason ?? "maker_post_only_price_unavailable",
                rawDecisionJson,
                settings.PaperStakeAmount,
                cancellationToken);
            return BtcMakerOrderResult.SkippedResult;
        }

        var expiration = ResolveMakerExpiration(market, nowUtc);
        rawDecisionJson = AttachMakerExpirationJson(rawDecisionJson, expiration);
        if (!expiration.Available || expiration.LocalExpiresAtUtc is null)
        {
            await RecordSkippedMakerRunAsync(
                nowUtc,
                market,
                variant,
                selectedOutcome,
                syntheticMarketId,
                expiration.RejectionReason ?? "maker_expiration_unavailable",
                rawDecisionJson,
                settings.PaperStakeAmount,
                cancellationToken);
            return BtcMakerOrderResult.SkippedResult;
        }

        var sizing = CreateLimitMinimumStakeSizing(
            orderBook,
            priceDecision.LimitPrice.Value,
            settings.PaperStakeAmount,
            orderBookLookup.Source);
        rawDecisionJson = AttachOpeningLimitStakeSizingJson(
            rawDecisionJson,
            settings.PaperStakeAmount,
            sizing,
            expiration);
        rawDecisionJson = AttachMakerPostOnlyPriceJson(rawDecisionJson, priceDecision);
        if (!sizing.Available)
        {
            await RecordSkippedMakerRunAsync(
                nowUtc,
                market,
                variant,
                selectedOutcome,
                syntheticMarketId,
                sizing.RejectionReason ?? "maker_minimum_stake_unavailable",
                rawDecisionJson,
                settings.PaperStakeAmount,
                cancellationToken);
            return BtcMakerOrderResult.SkippedResult;
        }

        var isPaperLiveShadowTest = ShouldRunPaperLiveShadowTest(settings);
        PaperLiveShadowOrderBookSnapshotResult? shadowSnapshot = null;
        TakerOrderBookLookupResult? shadowFakLookup = null;
        BtcMinimumStakeSizing? shadowFakSizing = null;
        TakerBuyFillEstimate? shadowFakEstimate = null;
        string? shadowFakFillEvidence = null;
        var paperEntryPrice = priceDecision.LimitPrice.Value;
        var paperEntryNotionalUsd = sizing.TargetNotionalUsd;
        var paperEntrySizeShares = sizing.TargetSizeShares;
        var shadowDecisionPrice = priceDecision.LimitPrice.Value;
        var shadowDecisionTargetNotionalUsd = sizing.TargetNotionalUsd;
        var shadowDecisionRequestedSizeShares = sizing.TargetSizeShares;
        var shadowDecisionMaxReservedNotionalUsd = sizing.TargetSizeShares * priceDecision.LimitPrice.Value;
        var paperLiveShadowStakeUsd = settings.LiveStakeAmount;
        if (isPaperLiveShadowTest)
        {
            paperLiveShadowStakeUsd = GetPaperLiveShadowStakeUsd(variant, settings);
            shadowSnapshot = await GetPaperLiveShadowOrderBookSnapshotAsync(
                selectedOutcome.AssetId,
                nowUtc,
                cancellationToken);
            if (shadowSnapshot.OrderBook is null)
            {
                var shadowRawDecisionJson = AttachPaperLiveShadowDecisionJson(
                    rawDecisionJson,
                    null,
                    null,
                    null,
                    "paper_live_shadow_snapshot_missing",
                    PaperLiveShadowTestSource,
                    expiration,
                    postOnly: false);
                await RecordSkippedMakerRunAsync(
                    nowUtc,
                    market,
                    variant,
                    selectedOutcome,
                    syntheticMarketId,
                    shadowSnapshot.RejectionReason ?? "paper_live_shadow_snapshot_missing",
                    shadowRawDecisionJson,
                    paperLiveShadowStakeUsd,
                    cancellationToken);
                return BtcMakerOrderResult.SkippedResult;
            }

            var shadowFakOrderBook = ApplyFallbackMinOrderSize(shadowSnapshot.OrderBook, market.OrderMinSize);
            shadowSnapshot = shadowSnapshot with { OrderBook = shadowFakOrderBook };
            shadowFakLookup = TakerOrderBookLookupResult.Found(
                shadowFakOrderBook,
                shadowSnapshot.Source,
                shadowSnapshot.Age);
            var fakWorstPrice = ResolveFakGuaranteedWorstPrice(shadowFakOrderBook);
            shadowFakSizing = CreateLimitMinimumStakeSizing(
                shadowFakOrderBook,
                fakWorstPrice,
                paperLiveShadowStakeUsd,
                shadowSnapshot.Source);
            var shadowFakRawDecisionJson = AttachOpeningLimitStakeSizingJson(
                rawDecisionJson,
                paperLiveShadowStakeUsd,
                shadowFakSizing,
                expiration);
            if (!shadowFakSizing.Available)
            {
                await RecordSkippedMakerRunAsync(
                    nowUtc,
                    market,
                    variant,
                    selectedOutcome,
                    syntheticMarketId,
                    shadowFakSizing.RejectionReason ?? "paper_live_shadow_fak_stake_sizing_rejected",
                    AttachFakPaperFillSimulationJson(
                        shadowFakRawDecisionJson,
                        shadowFakLookup,
                        shadowFakSizing,
                        null,
                        shadowFakSizing.RejectionReason ?? "paper_live_shadow_fak_stake_sizing_rejected",
                        nowUtc),
                    paperLiveShadowStakeUsd,
                    cancellationToken);
                return BtcMakerOrderResult.SkippedResult;
            }

            shadowFakEstimate = EstimatePaperFakFill(
                shadowFakOrderBook,
                shadowFakSizing.TargetNotionalUsd,
                fakWorstPrice);
            rawDecisionJson = AttachFakPaperFillSimulationJson(
                shadowFakRawDecisionJson,
                shadowFakLookup,
                shadowFakSizing,
                shadowFakEstimate,
                shadowFakEstimate.RejectionReason,
                nowUtc);
            if (!shadowFakEstimate.Filled)
            {
                await RecordSkippedMakerRunAsync(
                    nowUtc,
                    market,
                    variant,
                    selectedOutcome,
                    syntheticMarketId,
                    shadowFakEstimate.RejectionReason ?? "paper_live_shadow_fak_no_immediate_fill",
                    rawDecisionJson,
                    paperLiveShadowStakeUsd,
                    cancellationToken);
                return BtcMakerOrderResult.SkippedResult;
            }

            paperEntryPrice = shadowFakEstimate.AverageFillPrice;
            paperEntryNotionalUsd = shadowFakEstimate.NotionalUsd;
            paperEntrySizeShares = shadowFakEstimate.SizeShares;
            shadowDecisionPrice = fakWorstPrice;
            shadowDecisionTargetNotionalUsd = shadowFakSizing.TargetNotionalUsd;
            shadowDecisionRequestedSizeShares = shadowFakSizing.TargetSizeShares;
            shadowDecisionMaxReservedNotionalUsd = shadowFakSizing.TargetNotionalUsd;
            shadowFakFillEvidence = string.Concat(
                "BtcUpDown5mPaper:",
                variant.Code,
                ": FAK taker paper live-shadow fill from ",
                shadowFakLookup.Source,
                " ask depth. WorstPrice=",
                fakWorstPrice.ToString("0.########", CultureInfo.InvariantCulture),
                " AvgFillPrice=",
                shadowFakEstimate.AverageFillPrice.ToString("0.########", CultureInfo.InvariantCulture),
                " FilledSize=",
                shadowFakEstimate.SizeShares.ToString("0.########", CultureInfo.InvariantCulture),
                " FilledNotionalUsd=",
                shadowFakEstimate.NotionalUsd.ToString("0.########", CultureInfo.InvariantCulture),
                " RequestedNotionalUsd=",
                shadowFakSizing.TargetNotionalUsd.ToString("0.########", CultureInfo.InvariantCulture),
                " LevelsUsed=",
                shadowFakEstimate.LevelsUsed.ToString(CultureInfo.InvariantCulture),
                ".");
        }

        Guid? correlationId = null;
        Signal? signal = null;
        PaperOrder? order = null;
        StrategyMarketPaperRun? run = null;
        await entryPlacementLock.WaitAsync(cancellationToken);
        try
        {
            if (isPaperLiveShadowTest && shadowSnapshot?.OrderBook is { } shadowOrderBook)
            {
                correlationId = Guid.NewGuid();
                var quoteAgeMs = (int)Math.Round(GetSnapshotAge(shadowOrderBook.SnapshotAtUtc).TotalMilliseconds);
                var marketStartUtc = GetMarketWindowStartUtc(market, variant);
                var shadowDecision = new PaperLiveShadowDecision(
                    correlationId.Value,
                    variant.Id,
                    market.MarketId,
                    market.ConditionId,
                    selectedOutcome.AssetId,
                    selectedOutcome.Outcome,
                    TradeSide.Buy,
                    shadowDecisionPrice,
                    shadowDecisionTargetNotionalUsd,
                    shadowDecisionRequestedSizeShares,
                    shadowDecisionMaxReservedNotionalUsd,
                    FakOrderType,
                    false,
                    SerializePaperLiveShadowOrderBookSnapshot(shadowOrderBook, shadowSnapshot.Source, shadowSnapshot.Age),
                    quoteAgeMs,
                    PaperLiveShadowTestSource,
                    shadowOrderBook.SnapshotAtUtc,
                    nowUtc,
                    marketStartUtc,
                    market.EndDateUtc,
                    nowUtc.AddSeconds(Math.Min(10, Math.Max(1, options.EntryGraceSeconds))),
                    expiration.LocalExpiresAtUtc.Value,
                    Status: "decision_created",
                    UpdatedAtUtc: nowUtc);
                await repository.AddPaperLiveShadowDecisionAsync(shadowDecision, cancellationToken);
                rawDecisionJson = AttachPaperLiveShadowDecisionJson(
                    rawDecisionJson,
                    correlationId,
                    quoteAgeMs,
                    shadowOrderBook,
                    null,
                    PaperLiveShadowTestSource,
                    expiration,
                    postOnly: false);
            }

            signal = CreateSignal(
                market,
                selectedOutcome,
                variant,
                paperEntryPrice,
                paperEntrySizeShares,
                paperEntryNotionalUsd,
                nowUtc);
            order = isPaperLiveShadowTest
                ? CreatePendingOpeningLimitPaperOrder(
                    signal,
                    selectedOutcome,
                    variant,
                    shadowDecisionPrice,
                    shadowDecisionRequestedSizeShares,
                    shadowDecisionTargetNotionalUsd,
                    nowUtc,
                    expiration.LocalExpiresAtUtc.Value,
                    rawDecisionJson,
                    correlationId,
                    PaperLiveShadowTestSource)
                : shadowFakEstimate is not null
                ? CreateFilledPaperOrder(
                    signal,
                    selectedOutcome,
                    variant,
                    shadowFakEstimate.AverageFillPrice,
                    shadowFakEstimate.SizeShares,
                    shadowFakEstimate.NotionalUsd,
                    nowUtc,
                    rawDecisionJson,
                    BtcFakTakerPaperExecutionSource) with { CorrelationId = correlationId }
                : CreatePendingOpeningLimitPaperOrder(
                    signal,
                    selectedOutcome,
                    variant,
                    priceDecision.LimitPrice.Value,
                    sizing.TargetSizeShares,
                    sizing.TargetNotionalUsd,
                    nowUtc,
                    expiration.LocalExpiresAtUtc.Value,
                    rawDecisionJson,
                    correlationId,
                    isPaperLiveShadowTest ? PaperLiveShadowTestSource : BtcMakerExecutionSource);
            run = CreateMakerRun(
                nowUtc,
                market,
                variant,
                selectedOutcome,
                syntheticMarketId,
                StrategyMarketPaperRunStatuses.Entered,
                paperEntryNotionalUsd,
                paperEntryPrice,
                paperEntrySizeShares,
                signal.Id,
                order.Id,
                skipReason: null,
                diagnosticsJson: null);

            await repository.AddSignalAndPaperOrderAsync(signal, order, cancellationToken);
            if (shadowFakEstimate is not null && !isPaperLiveShadowTest)
            {
                var paperFakFill = new PaperFill(
                    Guid.NewGuid(),
                    order.Id,
                    shadowFakEstimate.AverageFillPrice,
                    shadowFakEstimate.SizeShares,
                    nowUtc,
                    shadowFakFillEvidence ?? string.Empty);
                await repository.AddPaperFillAsync(paperFakFill, cancellationToken);
                var positions = await repository.GetPaperPositionsAsync(cancellationToken);
                var currentPosition = FindPaperPosition(positions, order);
                var currentBid = shadowFakLookup?.OrderBook?.BestBid ?? shadowFakEstimate.AverageFillPrice;
                var updatedPosition = paperTradingEngine.ApplyBuyFill(
                    currentPosition,
                    order,
                    paperFakFill,
                    currentBid,
                    nowUtc);
                await repository.UpsertPaperPositionAsync(updatedPosition, cancellationToken);
                exposureCache.ApplyPaperPosition(updatedPosition);
                await repository.ActivatePaperCopiedLeaderPositionAsync(
                    order.Id,
                    paperFakFill.SizeShares,
                    paperFakFill.FilledAtUtc,
                    cancellationToken);
            }

            if (!await repository.TryAddStrategyMarketPaperRunAsync(run, cancellationToken))
            {
                return BtcMakerOrderResult.SkippedResult;
            }

            exposureCache.ApplyPaperOrder(order);
            if (isPaperLiveShadowTest && correlationId is { } shadowCorrelationId)
            {
                await repository.UpdatePaperLiveShadowDecisionLinksAsync(
                    shadowCorrelationId,
                    signal.Id,
                    order.Id,
                    null,
                    "paper_shadow_created",
                    nowUtc,
                    cancellationToken);
            }
        }
        finally
        {
            entryPlacementLock.Release();
        }

        if (isPaperLiveShadowTest && correlationId is { } paperLiveShadowCorrelationId && signal is not null && order is not null)
        {
            var placementResult = await TryPlacePaperLiveShadowOrderAsync(
                signal,
                selectedOutcome,
                variant,
                order,
                priceDecision.LimitPrice.Value,
                paperLiveShadowStakeUsd,
                expiration,
                paperLiveShadowCorrelationId,
                GetMarketWindowStartUtc(market, variant),
                market.EndDateUtc,
                nowUtc,
                cancellationToken,
                postOnly: false);
            if (placementResult.Placed && placementResult.LiveOrder is { } liveOrder && run is not null)
            {
                await ApplyActualLiveFillToPaperShadowAsync(
                    order,
                    run,
                    liveOrder,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
            }
            else if (placementResult.KeepPaperEntry && run is not null)
            {
                await ApplyPaperModeFillToPaperShadowAsync(
                    order,
                    run,
                    paperEntryPrice,
                    paperEntryNotionalUsd,
                    paperEntrySizeShares,
                    shadowFakLookup?.OrderBook?.BestBid ?? paperEntryPrice,
                    shadowFakFillEvidence ?? "Paper live-shadow skipped Live placement; applied paper-mode fill.",
                    nowUtc,
                    cancellationToken);
            }
            else if (run is not null && !placementResult.KeepPaperEntry)
            {
                await repository.UpdateStrategyMarketPaperRunAsync(
                    MarkPaperLiveShadowRunSkipped(run, placementResult, DateTimeOffset.UtcNow),
                    cancellationToken);
            }
        }

        logger.LogInformation(
            "BTC Up or Down 5m maker paper order placed. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} PreviousMaxBestAsk={PreviousMaxBestAsk} CurrentMaxBestAsk={CurrentMaxBestAsk} OrderSequence={OrderSequence} Price={Price} NotionalUsd={NotionalUsd} SizeShares={SizeShares}",
            variant.Code,
            market.Slug,
            selectedOutcome.Outcome,
            previousMaxBestAsk,
            currentMaxBestAsk,
            orderSequence,
            priceDecision.LimitPrice.Value,
            sizing.TargetNotionalUsd,
            sizing.TargetSizeShares);

        return BtcMakerOrderResult.PlacedResult;
    }

    private async Task RecordSkippedMakerRunAsync(
        DateTimeOffset nowUtc,
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        BtcUpDown5mOutcomeQuote selectedOutcome,
        string syntheticMarketId,
        string reason,
        string rawDecisionJson,
        decimal stakeUsd,
        CancellationToken cancellationToken)
    {
        rawDecisionJson = AttachMakerSkipReasonJson(rawDecisionJson, reason);
        var run = CreateMakerRun(
            nowUtc,
            market,
            variant,
            selectedOutcome,
            syntheticMarketId,
            StrategyMarketPaperRunStatuses.Skipped,
            stakeUsd,
            entryPrice: null,
            sizeShares: null,
            signalId: null,
            paperOrderId: null,
            reason,
            rawDecisionJson);

        await repository.TryAddStrategyMarketPaperRunAsync(run, cancellationToken);
        logger.LogInformation(
            "BTC Up or Down 5m maker run skipped. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} Reason={Reason}",
            variant.Code,
            market.Slug,
            selectedOutcome.Outcome,
            reason);
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
            if (!IsCloseBookCaptureCandidate(market) ||
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

    private static bool IsBtcReferenceVariant(BtcUpDown5mStrategyVariant variant)
    {
        return string.Equals(GetReferenceAssetSymbol(variant), "BTC", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCryptoReferenceVariant(BtcUpDown5mStrategyVariant variant)
    {
        return !IsBtcReferenceVariant(variant);
    }

    private static string GetReferenceAssetSymbol(BtcUpDown5mStrategyVariant variant)
    {
        return string.IsNullOrWhiteSpace(variant.ReferenceAssetSymbol)
            ? "BTC"
            : variant.ReferenceAssetSymbol.Trim().ToUpperInvariant();
    }

    private static bool IsChildMirrorStrategy(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.ChildMirror or
            BtcUpDown5mStrategyBehavior.ChildProgressMirror or
            BtcUpDown5mStrategyBehavior.ChildRoiMirror or
            BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror;
    }

    private static bool IsChildProgressMirrorStrategy(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.ChildProgressMirror or
            BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror;
    }

    private static bool IsChildRoiMirrorStrategy(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.ChildRoiMirror or
            BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror;
    }

    private static int GetChildLookbackHours(BtcUpDown5mStrategyVariant variant)
    {
        return Math.Clamp(variant.DecisionDepth, 1, 24);
    }

    private static string GetChildAssignmentMode(BtcUpDown5mStrategyVariant variant)
    {
        return (IsChildProgressMirrorStrategy(variant), IsChildRoiMirrorStrategy(variant)) switch
        {
            (false, false) => StrategyChildParentAssignmentModes.Child,
            (true, false) => StrategyChildParentAssignmentModes.ChildProgress,
            (false, true) => StrategyChildParentAssignmentModes.ChildRoi,
            _ => StrategyChildParentAssignmentModes.ChildProgressRoi
        };
    }

    private static bool HasProgressInName(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Name.Contains("Progress", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFuturesChildParentCandidate(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Code.Contains("futures", StringComparison.OrdinalIgnoreCase) ||
            variant.Name.Contains("Futures", StringComparison.OrdinalIgnoreCase) ||
            variant.Category.Contains("Futures", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStrategyActiveForChildSelection(
        BtcUpDown5mStrategyVariant variant,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        DateTimeOffset nowUtc)
    {
        var settings = GetStrategySettings(strategySettings, variant.Id);
        return settings.Enabled && !settings.IsPausedAt(nowUtc);
    }

    private static DateTimeOffset? GetMarketWindowStartUtc(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant)
    {
        return IsBtcReferenceVariant(variant)
            ? BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market)
            : CryptoUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
    }

    private static bool IsCloseBookCaptureCandidate(PolymarketGammaMarket market)
    {
        if (BtcUpDown5mMarketAnalyzer.IsStrategyCandidate(market))
        {
            return true;
        }

        return CryptoUpDown5mMarketAnalyzer.TryGetAssetSymbol(market, CryptoReferenceAssetSymbols, out _);
    }

    private static bool IsFixedOutcomeMaker(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomeMaker;
    }

    private static bool TryResolveFixedOutcomeDirection(
        BtcUpDown5mStrategyVariant variant,
        out BtcPriceDirection direction)
    {
        if (variant.FixedOutcome == BtcUpDownFixedOutcome.Up)
        {
            direction = BtcPriceDirection.Up;
            return true;
        }

        if (variant.FixedOutcome == BtcUpDownFixedOutcome.Down)
        {
            direction = BtcPriceDirection.Down;
            return true;
        }

        direction = default;
        return false;
    }

    private static BtcPriceDirection? ResolveDiffCounterTriggerDirection(BtcUpDown5mStrategyVariant variant)
    {
        if (variant.DiffCounterTriggerOutcome == BtcUpDownFixedOutcome.Up)
        {
            return BtcPriceDirection.Up;
        }

        if (variant.DiffCounterTriggerOutcome == BtcUpDownFixedOutcome.Down)
        {
            return BtcPriceDirection.Down;
        }

        return variant.FixedOutcome switch
        {
            BtcUpDownFixedOutcome.Up => BtcPriceDirection.Down,
            BtcUpDownFixedOutcome.Down => BtcPriceDirection.Up,
            _ => null
        };
    }

    private void CleanupMakerHighWaterStates(DateTimeOffset nowUtc)
    {
        if (makerHighWaterStates.Count == 0)
        {
            return;
        }

        var cutoffUtc = nowUtc.AddHours(-1);
        foreach (var item in makerHighWaterStates
            .Where(item => item.Value.MarketEndUtc is { } marketEndUtc && marketEndUtc < cutoffUtc)
            .ToArray())
        {
            makerHighWaterStates.Remove(item.Key);
        }
    }

    private static BtcMakerDecisionSlot GetMakerDecisionSlot(
        DateTimeOffset marketStartUtc,
        DateTimeOffset? marketEndUtc,
        DateTimeOffset nowUtc)
    {
        if (marketEndUtc is not { } endUtc ||
            endUtc <= marketStartUtc ||
            nowUtc >= endUtc)
        {
            return new BtcMakerDecisionSlot(false, 0, 0);
        }

        var totalSlots = (int)Math.Floor((endUtc - marketStartUtc).TotalSeconds / MakerDecisionIntervalSeconds);
        var maxSlot = Math.Min(MakerMaxDecisionSlot, Math.Max(0, totalSlots - 1));
        if (maxSlot == 0)
        {
            return new BtcMakerDecisionSlot(false, 0, 0);
        }

        var elapsedSeconds = Math.Max(0d, (nowUtc - marketStartUtc).TotalSeconds);
        var currentSlot = (int)Math.Floor(elapsedSeconds / MakerDecisionIntervalSeconds);
        currentSlot = Math.Min(Math.Max(0, currentSlot), maxSlot);
        return new BtcMakerDecisionSlot(currentSlot > 0, currentSlot, maxSlot);
    }

    private OrderBookSnapshot ApplyMakerOrderBookFallbacks(
        OrderBookSnapshot orderBook,
        PolymarketGammaMarket market)
    {
        var minOrderSize = orderBook.MinOrderSize is > 0m
            ? orderBook.MinOrderSize
            : market.OrderMinSize;
        var tickSize = orderBook.TickSize is > 0m
            ? orderBook.TickSize
            : market.OrderPriceMinTickSize is > 0m
                ? market.OrderPriceMinTickSize
                : options.OpeningLimitPriceTickSize;
        return orderBook with
        {
            MinOrderSize = minOrderSize,
            TickSize = tickSize
        };
    }

    private BtcMakerPostOnlyPriceDecision ResolveMakerPostOnlyLimitPrice(
        OrderBookSnapshot orderBook,
        decimal bestAsk,
        decimal? fixedLimitPrice)
    {
        var tickSize = orderBook.TickSize is > 0m
            ? orderBook.TickSize.Value
            : options.OpeningLimitPriceTickSize;
        var bestBid = TryGetBestBidFromOrderBook(orderBook);
        if (tickSize <= 0m)
        {
            return BtcMakerPostOnlyPriceDecision.Reject(
                "maker_tick_size_unavailable",
                tickSize,
                bestBid,
                bestAsk);
        }

        if (fixedLimitPrice is { } limitPriceOverride)
        {
            if (limitPriceOverride <= 0m)
            {
                return BtcMakerPostOnlyPriceDecision.Reject(
                    "maker_fixed_limit_price_non_positive",
                    tickSize,
                    bestBid,
                    bestAsk,
                    limitPriceOverride,
                    attempts: 1);
            }

            if (limitPriceOverride >= bestAsk)
            {
                return BtcMakerPostOnlyPriceDecision.Reject(
                    "maker_fixed_limit_price_crosses_best_ask",
                    tickSize,
                    bestBid,
                    bestAsk,
                    limitPriceOverride,
                    attempts: 1);
            }

            return BtcMakerPostOnlyPriceDecision.Enter(
                limitPriceOverride,
                tickSize,
                bestBid,
                bestAsk,
                limitPriceOverride,
                attempts: 1);
        }

        if (bestAsk <= tickSize)
        {
            return BtcMakerPostOnlyPriceDecision.Reject(
                "maker_best_ask_too_low_for_post_only_tick",
                tickSize,
                bestBid,
                bestAsk);
        }

        var rawLimitPrice = bestAsk - tickSize;
        var limitPrice = RoundDownToTick(rawLimitPrice, tickSize);
        var attempts = 1;
        while (limitPrice >= bestAsk && limitPrice > 0m)
        {
            limitPrice = RoundDownToTick(limitPrice - tickSize, tickSize);
            attempts++;
        }

        if (limitPrice <= 0m)
        {
            return BtcMakerPostOnlyPriceDecision.Reject(
                "maker_post_only_limit_price_non_positive",
                tickSize,
                bestBid,
                bestAsk,
                rawLimitPrice,
                attempts);
        }

        return BtcMakerPostOnlyPriceDecision.Enter(
            limitPrice,
            tickSize,
            bestBid,
            bestAsk,
            rawLimitPrice,
            attempts);
    }

    private OpeningLimitExpirationDecision ResolveMakerExpiration(
        PolymarketGammaMarket market,
        DateTimeOffset nowUtc)
    {
        var configuredTtlSeconds = Math.Max(1, options.OpeningLimitGtdTtlSeconds);
        var marketEndExpireBeforeSeconds = 0;
        var clobBufferSeconds = Math.Max(60, options.ClobGtdExpirationSecurityBufferSeconds);
        if (market.EndDateUtc is not { } marketEndUtc)
        {
            return OpeningLimitExpirationDecision.Reject(
                "maker_market_end_unknown",
                configuredTtlSeconds,
                marketEndExpireBeforeSeconds,
                clobBufferSeconds,
                null,
                "maker_market_end");
        }

        var localExpiresAtUtc = marketEndUtc;
        if (localExpiresAtUtc <= nowUtc)
        {
            return OpeningLimitExpirationDecision.Reject(
                "maker_expiration_elapsed",
                configuredTtlSeconds,
                marketEndExpireBeforeSeconds,
                clobBufferSeconds,
                localExpiresAtUtc,
                "maker_market_end");
        }

        return OpeningLimitExpirationDecision.Enter(
            localExpiresAtUtc,
            localExpiresAtUtc.AddSeconds(clobBufferSeconds),
            nowUtc,
            configuredTtlSeconds,
            marketEndExpireBeforeSeconds,
            clobBufferSeconds,
            "maker_market_end");
    }

    private static string GetMakerStateKey(
        BtcUpDown5mStrategyVariant variant,
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote selectedOutcome)
    {
        return string.Concat(
            StrategyIds.Normalize(variant.Id).ToString("D"),
            "|",
            market.MarketId,
            "|",
            selectedOutcome.AssetId);
    }

    private static string GetMakerSyntheticMarketId(
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote selectedOutcome,
        decimal bestAsk,
        int orderSequence,
        DateTimeOffset nowUtc)
    {
        return string.Concat(
            market.MarketId,
            ":maker:",
            selectedOutcome.Outcome.ToLowerInvariant(),
            ":",
            bestAsk.ToString("0.########", CultureInfo.InvariantCulture),
            ":",
            orderSequence.ToString(CultureInfo.InvariantCulture),
            ":",
            nowUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
    }

    private static StrategyMarketPaperRun CreateMakerRun(
        DateTimeOffset nowUtc,
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        BtcUpDown5mOutcomeQuote selectedOutcome,
        string syntheticMarketId,
        string status,
        decimal stakeUsd,
        decimal? entryPrice,
        decimal? sizeShares,
        Guid? signalId,
        Guid? paperOrderId,
        string? skipReason,
        string? diagnosticsJson)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        return new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            syntheticMarketId,
            market.ConditionId,
            market.Slug,
            market.Question,
            string.IsNullOrWhiteSpace(variant.Category) ? market.Category : variant.Category,
            marketStartUtc,
            market.EndDateUtc,
            nowUtc,
            nowUtc,
            status,
            selectedOutcome.AssetId,
            selectedOutcome.Outcome,
            entryPrice,
            stakeUsd,
            sizeShares,
            signalId,
            paperOrderId,
            string.Equals(status, StrategyMarketPaperRunStatuses.Entered, StringComparison.OrdinalIgnoreCase)
                ? nowUtc
                : null,
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: skipReason,
            CreatedAtUtc: nowUtc,
            UpdatedAtUtc: nowUtc,
            SkipDiagnosticsJson: diagnosticsJson);
    }

    private static string BuildMakerRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        BtcUpDown5mOutcomeQuote selectedOutcome,
        OrderBookSnapshot orderBook,
        TakerOrderBookLookupResult orderBookLookup,
        decimal previousMaxBestAsk,
        decimal currentMaxBestAsk,
        int orderSequence,
        int decisionSlot,
        int maxDecisionSlot,
        string syntheticMarketId)
    {
        var bestBid = TryGetBestBidFromOrderBook(orderBook);
        var bestAsk = TryGetBestAskFromOrderBook(orderBook);
        return JsonSerializer.Serialize(new
        {
            decision_source = "btc_updown_5m_fixed_outcome_maker_new_max",
            paper_only = true,
            strategy_code = variant.Code,
            market_id = market.MarketId,
            synthetic_market_id = syntheticMarketId,
            condition_id = market.ConditionId,
            market_slug = market.Slug,
            outcome = selectedOutcome.Outcome,
            asset_id = selectedOutcome.AssetId,
            maker_trend_mode = "new_best_ask_high_water",
            maker_trend_order_sequence = orderSequence,
            maker_decision_interval_seconds = MakerDecisionIntervalSeconds,
            maker_decision_slot = decisionSlot,
            maker_max_decision_slot = maxDecisionSlot,
            maker_fixed_limit_price = variant.FixedLimitPrice,
            maker_min_best_ask_exclusive = variant.MakerMinBestAskExclusive,
            previous_best_ask = previousMaxBestAsk,
            current_best_ask = currentMaxBestAsk,
            previous_max_best_ask = previousMaxBestAsk,
            current_max_best_ask = currentMaxBestAsk,
            order_book_source = orderBookLookup.Source,
            order_book_age_ms = orderBookLookup.Age is { } age
                ? (int)Math.Round(age.TotalMilliseconds)
                : (int?)null,
            order_book_rest_attempted = orderBookLookup.RestAttempted,
            order_book_snapshot_at_utc = orderBook.SnapshotAtUtc.ToString("O", CultureInfo.InvariantCulture),
            best_bid = bestBid,
            best_ask = bestAsk,
            spread_abs = orderBook.SpreadAbs,
            min_order_size = orderBook.MinOrderSize,
            tick_size = orderBook.TickSize,
            market_start_utc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market)?.ToString("O", CultureInfo.InvariantCulture),
            market_end_utc = market.EndDateUtc?.ToString("O", CultureInfo.InvariantCulture)
        });
    }

    private static string AttachMakerPostOnlyPriceJson(
        string rawDecisionJson,
        BtcMakerPostOnlyPriceDecision priceDecision)
    {
        var root = ParseJsonObject(rawDecisionJson);
        root["paper_only"] = true;
        root["post_only"] = true;
        root["maker_post_only"] = true;
        root["execution_source"] = BtcMakerExecutionSource;
        root["maker_best_bid"] = priceDecision.BestBid;
        root["maker_best_ask"] = priceDecision.BestAsk;
        root["maker_tick_size"] = priceDecision.TickSize;
        root["maker_raw_limit_price"] = priceDecision.RawLimitPrice;
        root["maker_limit_price"] = priceDecision.LimitPrice;
        root["maker_post_only_price_attempts"] = priceDecision.Attempts;
        root["maker_post_only_rejection_reason"] = priceDecision.RejectionReason;
        if (!string.IsNullOrWhiteSpace(priceDecision.RejectionReason))
        {
            root["skip_reason"] = priceDecision.RejectionReason;
        }

        return root.ToJsonString();
    }

    private static string AttachMakerExpirationJson(
        string rawDecisionJson,
        OpeningLimitExpirationDecision expiration)
    {
        var root = ParseJsonObject(rawDecisionJson);
        root["gtd_expiration_mode"] = expiration.Mode;
        root["market_end_expire_before_seconds"] = expiration.MarketEndExpireBeforeSeconds;
        root["clob_gtd_expiration_security_buffer_seconds"] = expiration.ClobSecurityBufferSeconds;
        root["gtd_expiration_utc"] = expiration.LocalExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture);
        root["cancel_deadline_utc"] = expiration.LocalExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture);
        root["clob_wire_gtd_expiration_utc"] = expiration.ClobGtdExpirationUtc?.ToString("O", CultureInfo.InvariantCulture);
        root["gtd_expiration_rejection_reason"] = expiration.RejectionReason;
        if (!string.IsNullOrWhiteSpace(expiration.RejectionReason))
        {
            root["skip_reason"] = expiration.RejectionReason;
        }

        return root.ToJsonString();
    }

    private static string AttachMakerSkipReasonJson(
        string rawDecisionJson,
        string reason)
    {
        var root = ParseJsonObject(rawDecisionJson);
        root["skip_reason"] = reason;
        return root.ToJsonString();
    }

    private static string AttachStrategyPausedJson(
        string rawDecisionJson,
        StrategyRuntimeSettings settings,
        DateTimeOffset nowUtc)
    {
        var root = ParseJsonObject(rawDecisionJson);
        AddStrategyPausedJson(root, settings, nowUtc);
        return root.ToJsonString();
    }

    private static string BuildStrategyPausedDiagnosticsJson(
        StrategyRuntimeSettings settings,
        DateTimeOffset nowUtc)
    {
        var root = new JsonObject();
        AddStrategyPausedJson(root, settings, nowUtc);
        return root.ToJsonString();
    }

    private static void AddStrategyPausedJson(
        JsonObject root,
        StrategyRuntimeSettings settings,
        DateTimeOffset nowUtc)
    {
        root["skip_reason"] = StrategyPausedSkipReason;
        root["strategy_paused"] = true;
        root["paused_until_utc"] = settings.PausedUntilUtc?.ToString("O", CultureInfo.InvariantCulture);
        root["decision_utc"] = nowUtc.ToString("O", CultureInfo.InvariantCulture);
    }

    private static JsonObject ParseJsonObject(string? rawDecisionJson)
    {
        try
        {
            return string.IsNullOrWhiteSpace(rawDecisionJson)
                ? new JsonObject()
                : JsonNode.Parse(rawDecisionJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
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

    private IReadOnlyList<StrategyMarketPaperRun> FilterLocallyFinalizedEntryRuns(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        DateTimeOffset nowUtc)
    {
        CleanupLocallyFinalizedEntryRuns(nowUtc);
        if (locallyFinalizedEntryRuns.IsEmpty)
        {
            return runs;
        }

        var filteredRuns = runs
            .Where(run => !locallyFinalizedEntryRuns.ContainsKey(run.Id))
            .ToArray();
        if (filteredRuns.Length != runs.Count)
        {
            logger.LogInformation(
                "BTC Up or Down 5m due-entry query skipped locally finalized runs. Original={OriginalCount} Remaining={RemainingCount}",
                runs.Count,
                filteredRuns.Length);
        }

        return filteredRuns;
    }

    private void MarkLocallyFinalizedEntryRuns(IReadOnlyCollection<StrategyMarketPaperRun> runs)
    {
        if (runs.Count == 0)
        {
            return;
        }

        var nowUtc = GetUtcNow();
        CleanupLocallyFinalizedEntryRuns(nowUtc);
        foreach (var run in runs)
        {
            locallyFinalizedEntryRuns[run.Id] = nowUtc;
        }
    }

    private void CleanupLocallyFinalizedEntryRuns(DateTimeOffset nowUtc)
    {
        if (locallyFinalizedEntryRuns.IsEmpty)
        {
            return;
        }

        var cutoffUtc = nowUtc.Subtract(LocalFinalizedEntryRunRetention);
        foreach (var item in locallyFinalizedEntryRuns.Where(item => item.Value < cutoffUtc).ToArray())
        {
            locallyFinalizedEntryRuns.TryRemove(item.Key, out _);
        }
    }

    private async Task<(int EntriesPlaced, int RunsSkipped)> PlaceDueEntriesAsync(
        DateTimeOffset nowUtc,
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        bool previousResultReadyOnly,
        Guid cycleId,
        string cycleKind,
        string flowName,
        string stageName,
        CancellationToken cancellationToken,
        SemaphoreSlim? dueEntryLock = null)
    {
        if (variants.Count == 0)
        {
            return (0, 0);
        }

        if (dueEntryLock is not null)
        {
            await dueEntryLock.WaitAsync(cancellationToken);
            try
            {
                return await PlaceDueEntriesAsync(
                    nowUtc,
                    variants,
                    strategySettings,
                    previousResultReadyOnly,
                    cycleId,
                    cycleKind,
                    flowName,
                    stageName,
                    cancellationToken);
            }
            finally
            {
                dueEntryLock.Release();
            }
        }

        var variantsById = variants
            .ToDictionary(variant => StrategyIds.Normalize(variant.Id));
        var strategyIds = variantsById.Keys.ToArray();
        var runs = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            flowName,
            stageName + ".query",
            previousResultReadyOnly ? "expanded_last_due previous_result_ready_only" : "expanded_last_due",
            variants.Count,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await repository.GetDueStrategyMarketPaperRunsWithExpandedLastDueAsync(
                strategyIds,
                StrategyMarketPaperRunStatuses.Observed,
                nowUtc,
                options.MaxEntriesPerCycle,
                token),
            CreateStageOutcome,
            cancellationToken);
        if (runs.Count == 0)
        {
            return (0, 0);
        }

        runs = FilterLocallyFinalizedEntryRuns(runs, nowUtc);
        if (runs.Count == 0)
        {
            return (0, 0);
        }

        if (previousResultReadyOnly)
        {
            var previousResultFilter = await TrackStrategyStageAsync(
                cycleId,
                cycleKind,
                flowName,
                stageName + ".previous_result_ready_filter",
                detail: null,
                variants.Count,
                runs.Count,
                GetEarliestEntryDueAtUtc(runs),
                GetLatestEntryDueAtUtc(runs),
                async token => await FilterPreviousResultReadyRunsAsync(runs, variantsById, token),
                CreateStageOutcome,
                cancellationToken);
            runs = previousResultFilter.ReadyRuns;
            if (runs.Count == 0)
            {
                return (0, previousResultFilter.RunsSkipped);
            }

            var placementResult = await PlaceDueEntryRunsAsync(
                OrderDueEntryRunsForPlacement(runs, variantsById, strategySettings),
                variantsById,
                strategySettings,
                cycleId,
                cycleKind,
                flowName,
                stageName,
                cancellationToken);
            return (
                placementResult.EntriesPlaced,
                previousResultFilter.RunsSkipped + placementResult.RunsSkipped);
        }

        var orderedRuns = OrderDueEntryRunsForPlacement(runs, variantsById, strategySettings);
        return await PlaceDueEntryRunsAsync(
            orderedRuns,
            variantsById,
            strategySettings,
            cycleId,
            cycleKind,
            flowName,
            stageName,
            cancellationToken);
    }

    private async Task<PreviousResultReadyFilterResult> FilterPreviousResultReadyRunsAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        IReadOnlyDictionary<Guid, BtcUpDown5mStrategyVariant> variantsById,
        CancellationToken cancellationToken)
    {
        var candidates = new List<PreviousResultReadyCandidate>();
        var passThrough = new HashSet<Guid>();
        foreach (var run in runs)
        {
            var strategyId = StrategyIds.Normalize(run.StrategyId);
            if (!variantsById.TryGetValue(strategyId, out var variant) ||
                !UsesPreviousResultEntryFlow(variant) ||
                variant.MarketInterval != BtcUpDownMarketInterval.FiveMinutes ||
                run.MarketStartUtc is null)
            {
                passThrough.Add(run.Id);
                continue;
            }

            var previousMarketStartUtc = run.MarketStartUtc.Value.Subtract(
                BtcUpDown5mMarketAnalyzer.GetIntervalDuration(variant.MarketInterval));
            candidates.Add(new PreviousResultReadyCandidate(
                run.Id,
                GetReferenceAssetSymbol(variant),
                previousMarketStartUtc));
        }

        if (candidates.Count == 0)
        {
            return new PreviousResultReadyFilterResult(runs, RunsSkipped: 0);
        }

        var candidatesByRunId = candidates.ToDictionary(candidate => candidate.RunId);
        var requestedKeys = candidates
            .Select(candidate => new AssetMarketStartKey(
                NormalizeAssetSymbol(candidate.AssetSymbol),
                candidate.PreviousMarketStartUtc))
            .ToHashSet();
        var readyKeys = new HashSet<AssetMarketStartKey>(
            await GetResolvedMarketLedgerKeysAsync(candidates, cancellationToken));

        var nowUtc = GetUtcNow();
        var readyRuns = new List<StrategyMarketPaperRun>(runs.Count);
        var runsSkipped = 0;
        foreach (var run in runs)
        {
            if (passThrough.Contains(run.Id))
            {
                readyRuns.Add(run);
                continue;
            }

            if (!candidatesByRunId.TryGetValue(run.Id, out var candidate))
            {
                continue;
            }

            if (readyKeys.Contains(new AssetMarketStartKey(candidate.AssetSymbol, candidate.PreviousMarketStartUtc)))
            {
                readyRuns.Add(run);
                continue;
            }

            if (IsEntryExpired(run.EntryDueAtUtc, nowUtc) &&
                variantsById.TryGetValue(StrategyIds.Normalize(run.StrategyId), out var variant))
            {
                await SkipRunAsync(
                    run,
                    variant,
                    "previous_result_not_ready_by_entry_grace",
                    nowUtc,
                    cancellationToken,
                    BuildPreviousResultNotReadyByEntryGraceDiagnosticsJson(run, candidate, nowUtc));
                runsSkipped++;
            }
        }

        return new PreviousResultReadyFilterResult(readyRuns, runsSkipped);
    }

    private string BuildPreviousResultNotReadyByEntryGraceDiagnosticsJson(
        StrategyMarketPaperRun run,
        PreviousResultReadyCandidate candidate,
        DateTimeOffset nowUtc)
    {
        var root = new JsonObject
        {
            ["skip_reason"] = "previous_result_not_ready_by_entry_grace",
            ["entry_due_at_utc"] = run.EntryDueAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ["decision_utc"] = nowUtc.ToString("O", CultureInfo.InvariantCulture),
            ["entry_grace_seconds"] = options.EntryGraceSeconds,
            ["reference_asset_symbol"] = candidate.AssetSymbol,
            ["previous_market_start_utc"] = candidate.PreviousMarketStartUtc.ToString("O", CultureInfo.InvariantCulture)
        };

        if (run.MarketStartUtc is { } marketStartUtc)
        {
            root["market_start_utc"] = marketStartUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        return root.ToJsonString();
    }

    private async Task<IReadOnlySet<AssetMarketStartKey>> GetResolvedMarketLedgerKeysAsync(
        IReadOnlyList<PreviousResultReadyCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return new HashSet<AssetMarketStartKey>();
        }

        try
        {
            var assetSymbols = candidates
                .Select(candidate => candidate.AssetSymbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var startUtc = candidates.Min(candidate => candidate.PreviousMarketStartUtc);
            var endUtc = candidates.Max(candidate => candidate.PreviousMarketStartUtc);
            var resolvedMarkets = await repository.GetCryptoUpDown5mWebSocketResolvedMarketsAsync(
                assetSymbols,
                startUtc,
                endUtc,
                cancellationToken);
            return resolvedMarkets
                .Where(IsAcceptedResolvedMarketLedgerResult)
                .Select(result => new AssetMarketStartKey(
                    NormalizeAssetSymbol(result.AssetSymbol),
                    result.MarketStartUtc))
                .ToHashSet();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load previous-result ready ledger keys.");
            await TryRecordApiErrorAsync("GetPreviousResultReadyLedger", ex.Message, cancellationToken);
            return new HashSet<AssetMarketStartKey>();
        }
    }

    private async Task<IReadOnlySet<AssetMarketStartKey>> GetPreviousResultReadyClosedMarketKeysAsync(
        IReadOnlyList<PreviousResultReadyCandidate> candidates,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return new HashSet<AssetMarketStartKey>();
        }

        var requestedKeys = candidates
            .Select(candidate => new AssetMarketStartKey(
                NormalizeAssetSymbol(candidate.AssetSymbol),
                candidate.PreviousMarketStartUtc))
            .ToHashSet();
        var readyKeys = new HashSet<AssetMarketStartKey>();
        var marketLimit = Math.Max(options.MaxMarketsPerCycle, candidates.Count * 4);
        var assetSymbols = candidates
            .Select(candidate => NormalizeAssetSymbol(candidate.AssetSymbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        try
        {
            if (assetSymbols.Contains("BTC", StringComparer.OrdinalIgnoreCase))
            {
                AddPreviousResultReadyClosedMarketKeys(
                    readyKeys,
                    requestedKeys,
                    await repository.GetBtcUpDownStrategyGammaMarketsAsync(marketLimit, cancellationToken),
                    ["BTC"],
                    nowUtc);
            }

            var cryptoAssetSymbols = assetSymbols
                .Where(assetSymbol => !string.Equals(assetSymbol, "BTC", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (cryptoAssetSymbols.Length > 0)
            {
                AddPreviousResultReadyClosedMarketKeys(
                    readyKeys,
                    requestedKeys,
                    await repository.GetCryptoUpDown5mGammaMarketsAsync(cryptoAssetSymbols, marketLimit, cancellationToken),
                    cryptoAssetSymbols,
                    nowUtc);
            }

            return readyKeys;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load previous-result ready closed Gamma markets.");
            await TryRecordApiErrorAsync("GetPreviousResultReadyGammaMarkets", ex.Message, cancellationToken);
            return new HashSet<AssetMarketStartKey>();
        }
    }

    private static void AddPreviousResultReadyClosedMarketKeys(
        ISet<AssetMarketStartKey> readyKeys,
        IReadOnlySet<AssetMarketStartKey> requestedKeys,
        IReadOnlyList<PolymarketGammaMarket> markets,
        IReadOnlyList<string> assetSymbols,
        DateTimeOffset nowUtc)
    {
        foreach (var market in markets)
        {
            if (market.EndDateUtc is not { } endDateUtc || endDateUtc > nowUtc)
            {
                continue;
            }

            foreach (var assetSymbol in assetSymbols)
            {
                var normalizedAssetSymbol = NormalizeAssetSymbol(assetSymbol);
                if (!IsReferenceMarketCandidate(
                    market,
                    normalizedAssetSymbol,
                    BtcUpDownMarketInterval.FiveMinutes))
                {
                    continue;
                }

                var marketStartUtc = string.Equals(normalizedAssetSymbol, "BTC", StringComparison.OrdinalIgnoreCase)
                    ? BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market)
                    : CryptoUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
                if (marketStartUtc is null)
                {
                    continue;
                }

                var key = new AssetMarketStartKey(normalizedAssetSymbol, marketStartUtc.Value);
                if (requestedKeys.Contains(key))
                {
                    readyKeys.Add(key);
                }
            }
        }
    }

    private async Task<(int EntriesPlaced, int RunsSkipped)> PlaceDuePreOpenEntriesAsync(
        DateTimeOffset nowUtc,
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        Guid cycleId,
        string cycleKind,
        string flowName,
        string stageName,
        CancellationToken cancellationToken,
        SemaphoreSlim? dueEntryLock = null)
    {
        if (variants.Count == 0)
        {
            return (0, 0);
        }

        if (dueEntryLock is not null)
        {
            await dueEntryLock.WaitAsync(cancellationToken);
            try
            {
                return await PlaceDuePreOpenEntriesAsync(
                    nowUtc,
                    variants,
                    strategySettings,
                    cycleId,
                    cycleKind,
                    flowName,
                    stageName,
                    cancellationToken);
            }
            finally
            {
                dueEntryLock.Release();
            }
        }

        var variantsById = variants
            .ToDictionary(variant => StrategyIds.Normalize(variant.Id));
        var strategyIds = variantsById.Keys.ToArray();
        var runs = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            flowName,
            stageName + ".query",
            "earliest_due",
            variants.Count,
            runCount: null,
            earliestEntryDueAtUtc: null,
            latestEntryDueAtUtc: null,
            async token => await repository.GetDueStrategyMarketPaperRunsAtEarliestDueAsync(
                strategyIds,
                StrategyMarketPaperRunStatuses.Observed,
                nowUtc,
                token),
            CreateStageOutcome,
            cancellationToken);
        if (runs.Count == 0)
        {
            return (0, 0);
        }

        runs = FilterLocallyFinalizedEntryRuns(runs, nowUtc);
        if (runs.Count == 0)
        {
            return (0, 0);
        }

        var orderedRuns = OrderDueEntryRunsForPlacement(runs, variantsById, strategySettings);
        return await PlaceDueEntryRunsAsync(
            orderedRuns,
            variantsById,
            strategySettings,
            cycleId,
            cycleKind,
            flowName,
            stageName,
            cancellationToken);
    }

    private async Task<int> PlaceDuePreOpenSellExitsAsync(
        DateTimeOffset nowUtc,
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        CancellationToken cancellationToken,
        SemaphoreSlim? dueEntryLock = null)
    {
        if (variants.Count == 0)
        {
            return 0;
        }

        if (dueEntryLock is not null)
        {
            await dueEntryLock.WaitAsync(cancellationToken);
            try
            {
                return await PlaceDuePreOpenSellExitsAsync(
                    nowUtc,
                    variants,
                    cancellationToken);
            }
            finally
            {
                dueEntryLock.Release();
            }
        }

        var variantsById = variants
            .ToDictionary(variant => StrategyIds.Normalize(variant.Id));
        var runs = await repository.GetPreOpenSellExitDueRunsAsync(
            variantsById.Keys.ToArray(),
            nowUtc,
            Math.Max(options.MaxEntriesPerCycle, variants.Count),
            cancellationToken);
        if (runs.Count == 0)
        {
            return 0;
        }

        var positions = await repository.GetPaperPositionsAsync(cancellationToken);
        var orderBookFetchTasks = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>>(
            StringComparer.OrdinalIgnoreCase);
        var tasks = runs.Select(async run =>
        {
            if (!variantsById.TryGetValue(StrategyIds.Normalize(run.StrategyId), out var variant))
            {
                return 0;
            }

            await entryDecisionConcurrencyLock.WaitAsync(cancellationToken);
            try
            {
                return await PlaceDuePreOpenSellExitRunAsync(
                    DateTimeOffset.UtcNow,
                    run,
                    variant,
                    positions,
                    orderBookFetchTasks,
                    cancellationToken);
            }
            finally
            {
                entryDecisionConcurrencyLock.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        return results.Sum();
    }

    private async Task<int> PlaceDuePreOpenSellExitRunAsync(
        DateTimeOffset nowUtc,
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        IReadOnlyList<PaperPosition> positions,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>> orderBookFetchTasks,
        CancellationToken cancellationToken)
    {
        if (run.PaperOrderId is not { } entryOrderId ||
            string.IsNullOrWhiteSpace(run.SelectedAssetId) ||
            string.IsNullOrWhiteSpace(run.SelectedOutcome))
        {
            return 0;
        }

        var selectedDirection = TryResolveDirectionFromOutcome(run.SelectedOutcome);
        if (selectedDirection is null)
        {
            return 0;
        }

        var entryOrder = await repository.GetPaperOrderAsync(entryOrderId, cancellationToken);
        if (entryOrder is null)
        {
            return 0;
        }

        var entryFills = await repository.GetPaperFillsForOrderAsync(entryOrderId, cancellationToken);
        var entryFillSummary = SummarizeOpeningLimitFills(entryOrder, entryFills);
        if (entryFillSummary is null)
        {
            return 0;
        }

        var position = positions.FirstOrDefault(item =>
            string.Equals(item.CopiedTraderWallet, variant.CopiedTraderWallet, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.AssetId, run.SelectedAssetId, StringComparison.OrdinalIgnoreCase));
        if (position is null || position.SizeShares <= 0m)
        {
            return 0;
        }

        var lastQuarterStartUtc = GetLastQuarterStartUtc(run.MarketStartUtc, run.MarketEndUtc);
        var existingOrders = await repository.GetPaperOrdersForStrategyAssetAsync(
            variant.Id,
            variant.CopiedTraderWallet,
            run.SelectedAssetId,
            lastQuarterStartUtc ?? run.EnteredAtUtc ?? run.CreatedAtUtc,
            limit: 20,
            cancellationToken);
        if (existingOrders.Any(order =>
                order.Side == TradeSide.Sell &&
                string.Equals(order.ConditionId, run.ConditionId, StringComparison.OrdinalIgnoreCase) &&
                order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled or PaperOrderStatus.Filled or PaperOrderStatus.PartiallyFilledExpired))
        {
            return 0;
        }

        var market = await repository.GetPolymarketGammaMarketAsync(run.MarketId, cancellationToken);
        if (market is null)
        {
            return 0;
        }

        var sellDecision = await GetPreOpenSellExitDecisionAsync(
            market,
            run,
            variant,
            selectedDirection.Value,
            position.SizeShares,
            entryFillSummary,
            nowUtc,
            orderBookFetchTasks,
            cancellationToken);
        if (!sellDecision.ShouldSell ||
            sellDecision.SellLimitPrice is not { } sellLimitPrice ||
            sellDecision.SelectedOutcome is null)
        {
            return 0;
        }

        var sellSizeShares = position.SizeShares;
        var sellNotionalUsd = sellLimitPrice * sellSizeShares;
        var expiresAtUtc = ResolvePreOpenSellExitExpiration(run, nowUtc);
        var sellSignal = CreateSellSignal(
            market,
            sellDecision.SelectedOutcome,
            variant,
            sellLimitPrice,
            sellSizeShares,
            sellNotionalUsd,
            nowUtc);
        var sellOrder = CreatePendingPreOpenSellPaperOrder(
            sellSignal,
            sellDecision.SelectedOutcome,
            variant,
            sellLimitPrice,
            sellSizeShares,
            sellNotionalUsd,
            nowUtc,
            expiresAtUtc,
            sellDecision.RawDecisionJson);

        await repository.AddSignalAndPaperOrderAsync(sellSignal, sellOrder, cancellationToken);
        exposureCache.ApplyPaperOrder(sellOrder);

        logger.LogInformation(
            "BTC PreOpen Sell exit paper order placed. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} Price={Price} SizeShares={SizeShares} CurrentDirection={CurrentDirection}",
            variant.Code,
            run.MarketSlug,
            run.SelectedOutcome,
            sellLimitPrice,
            sellSizeShares,
            sellDecision.CurrentDirection);

        return 1;
    }

    private async Task<PreOpenSellExitDecision> GetPreOpenSellExitDecisionAsync(
        PolymarketGammaMarket market,
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        BtcPriceDirection selectedDirection,
        decimal positionSizeShares,
        OpeningLimitFillSummary entryFillSummary,
        DateTimeOffset nowUtc,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>> orderBookFetchTasks,
        CancellationToken cancellationToken)
    {
        var upOutcome = TrySelectOutcomeForDirection(market, BtcPriceDirection.Up);
        var downOutcome = TrySelectOutcomeForDirection(market, BtcPriceDirection.Down);
        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection);
        if (upOutcome is null || downOutcome is null || selectedOutcome is null)
        {
            var reason = "preopen_sell_exit_outcome_missing";
            return new PreOpenSellExitDecision(
                false,
                reason,
                null,
                null,
                selectedOutcome,
                BuildPreOpenSellExitRawDecisionJson(
                    market,
                    run,
                    variant,
                    selectedDirection,
                    currentDirection: null,
                    selectedOutcome,
                    entryFillSummary,
                    positionSizeShares,
                    sellLimitPrice: null,
                    upSnapshot: null,
                    downSnapshot: null,
                    nowUtc,
                    reason));
        }

        var upSnapshot = await GetPreOpenSellExitOrderBookSnapshotAsync(
            upOutcome.AssetId,
            nowUtc,
            orderBookFetchTasks,
            cancellationToken);
        var downSnapshot = await GetPreOpenSellExitOrderBookSnapshotAsync(
            downOutcome.AssetId,
            nowUtc,
            orderBookFetchTasks,
            cancellationToken);

        var currentDirection = TryInferCurrentDirection(upSnapshot, downSnapshot);
        if (currentDirection is null)
        {
            var reason = upSnapshot.RejectionReason ?? downSnapshot.RejectionReason ?? "preopen_sell_exit_direction_unknown";
            return new PreOpenSellExitDecision(
                false,
                reason,
                null,
                null,
                selectedOutcome,
                BuildPreOpenSellExitRawDecisionJson(
                    market,
                    run,
                    variant,
                    selectedDirection,
                    currentDirection: null,
                    selectedOutcome,
                    entryFillSummary,
                    positionSizeShares,
                    sellLimitPrice: null,
                    upSnapshot,
                    downSnapshot,
                    nowUtc,
                    reason));
        }

        if (currentDirection.Value == selectedDirection)
        {
            var reason = "preopen_sell_exit_direction_matches";
            return new PreOpenSellExitDecision(
                false,
                reason,
                currentDirection,
                null,
                selectedOutcome,
                BuildPreOpenSellExitRawDecisionJson(
                    market,
                    run,
                    variant,
                    selectedDirection,
                    currentDirection,
                    selectedOutcome,
                    entryFillSummary,
                    positionSizeShares,
                    sellLimitPrice: null,
                    upSnapshot,
                    downSnapshot,
                    nowUtc,
                    reason));
        }

        var selectedSnapshot = selectedDirection == BtcPriceDirection.Up ? upSnapshot : downSnapshot;
        var sellLimitPrice = TryGetMarketableSellLimitPrice(selectedSnapshot.OrderBook, positionSizeShares);
        if (sellLimitPrice is null)
        {
            var reason = selectedSnapshot.RejectionReason ?? "preopen_sell_exit_bid_depth_missing";
            return new PreOpenSellExitDecision(
                false,
                reason,
                currentDirection,
                null,
                selectedOutcome,
                BuildPreOpenSellExitRawDecisionJson(
                    market,
                    run,
                    variant,
                    selectedDirection,
                    currentDirection,
                    selectedOutcome,
                    entryFillSummary,
                    positionSizeShares,
                    sellLimitPrice: null,
                    upSnapshot,
                    downSnapshot,
                    nowUtc,
                    reason));
        }

        return new PreOpenSellExitDecision(
            true,
            null,
            currentDirection,
            sellLimitPrice,
            selectedOutcome,
            BuildPreOpenSellExitRawDecisionJson(
                market,
                run,
                variant,
                selectedDirection,
                currentDirection,
                selectedOutcome,
                entryFillSummary,
                positionSizeShares,
                sellLimitPrice,
                upSnapshot,
                downSnapshot,
                nowUtc,
                reason: null));
    }

    private async Task<PreOpenSellOrderBookSnapshot> GetPreOpenSellExitOrderBookSnapshotAsync(
        string assetId,
        DateTimeOffset nowUtc,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>> orderBookFetchTasks,
        CancellationToken cancellationToken)
    {
        var maxAge = GetPaperTakerMaxQuoteAge();
        var lookup = marketDataCache.GetOrderBook(assetId, maxAge);
        if (lookup is { Status: OrderBookCacheLookupStatus.Fresh, Snapshot: { } cached })
        {
            return CreatePreOpenSellOrderBookSnapshot(cached, WebSocketCacheSource, lookup.Age, RejectionReason: null);
        }

        if (options.PaperTakerRestFallbackEnabled)
        {
            var fetched = await GetOrFetchOrderBookAsync(assetId, orderBookFetchTasks, cancellationToken);
            if (fetched.OrderBook is not null)
            {
                var fetchedAge = GetSnapshotAge(fetched.OrderBook.SnapshotAtUtc);
                return fetchedAge <= maxAge
                    ? CreatePreOpenSellOrderBookSnapshot(fetched.OrderBook, ClobBookSource, fetchedAge, RejectionReason: null)
                    : CreatePreOpenSellOrderBookSnapshot(
                        fetched.OrderBook,
                        ClobBookSource,
                        fetchedAge,
                        SignalReasonCodes.MissingOrderBookCacheStale);
            }

            return new PreOpenSellOrderBookSnapshot(
                null,
                ClobBookSource,
                Age: null,
                BestBid: null,
                BestAsk: null,
                Midpoint: null,
                fetched.RejectionReason ?? SignalReasonCodes.MissingOrderBookRestMissing);
        }

        return new PreOpenSellOrderBookSnapshot(
            lookup.Snapshot,
            WebSocketCacheSource,
            lookup.Age,
            lookup.Snapshot?.BestBid,
            lookup.Snapshot?.BestAsk,
            TryGetBookMidpoint(lookup.Snapshot),
            lookup.Status == OrderBookCacheLookupStatus.Stale
                ? SignalReasonCodes.MissingOrderBookCacheStale
                : SignalReasonCodes.MissingOrderBookCacheMiss);
    }

    private PreOpenSellOrderBookSnapshot CreatePreOpenSellOrderBookSnapshot(
        OrderBookSnapshot orderBook,
        string source,
        TimeSpan? age,
        string? RejectionReason)
    {
        var bestBid = TryGetBestBidFromOrderBook(orderBook);
        var bestAsk = TryGetBestAskFromOrderBook(orderBook);
        return new PreOpenSellOrderBookSnapshot(
            orderBook,
            source,
            age,
            bestBid,
            bestAsk,
            TryGetBookMidpoint(bestBid, bestAsk),
            RejectionReason);
    }

    private static BtcPriceDirection? TryInferCurrentDirection(
        PreOpenSellOrderBookSnapshot upSnapshot,
        PreOpenSellOrderBookSnapshot downSnapshot)
    {
        if (upSnapshot.Midpoint is { } upMidpoint && downSnapshot.Midpoint is { } downMidpoint)
        {
            return upMidpoint == downMidpoint
                ? null
                : upMidpoint > downMidpoint
                    ? BtcPriceDirection.Up
                    : BtcPriceDirection.Down;
        }

        if (upSnapshot.BestBid is { } upBestBid && downSnapshot.BestBid is { } downBestBid)
        {
            return upBestBid == downBestBid
                ? null
                : upBestBid > downBestBid
                    ? BtcPriceDirection.Up
                    : BtcPriceDirection.Down;
        }

        return null;
    }

    private decimal? TryGetMarketableSellLimitPrice(OrderBookSnapshot? orderBook, decimal sizeShares)
    {
        if (orderBook is null || sizeShares <= 0m)
        {
            return null;
        }

        var remainingShares = sizeShares;
        decimal? rawLimitPrice = null;
        foreach (var level in orderBook.Bids
            .Where(level => level is { Price: > 0m, Size: > 0m } && level.Price <= 1m)
            .OrderByDescending(level => level.Price))
        {
            rawLimitPrice = level.Price;
            remainingShares -= level.Size;
            if (remainingShares <= 0m)
            {
                break;
            }
        }

        if (rawLimitPrice is not { } price)
        {
            return null;
        }

        var tickSize = orderBook.TickSize is > 0m
            ? orderBook.TickSize.Value
            : options.OpeningLimitPriceTickSize;
        var limitPrice = RoundDownToTick(price, tickSize);
        return limitPrice > 0m ? limitPrice : null;
    }

    private DateTimeOffset ResolvePreOpenSellExitExpiration(
        StrategyMarketPaperRun run,
        DateTimeOffset nowUtc)
    {
        if (run.MarketEndUtc is { } marketEndUtc && marketEndUtc > nowUtc)
        {
            return marketEndUtc;
        }

        return nowUtc.AddSeconds(Math.Max(1, options.OpeningLimitGtdTtlSeconds));
    }

    private static DateTimeOffset? GetLastQuarterStartUtc(
        DateTimeOffset? marketStartUtc,
        DateTimeOffset? marketEndUtc)
    {
        if (marketStartUtc is not { } startUtc ||
            marketEndUtc is not { } endUtc ||
            endUtc <= startUtc)
        {
            return null;
        }

        return startUtc.AddTicks((endUtc - startUtc).Ticks * 3 / 4);
    }

    private static decimal? TryGetBookMidpoint(OrderBookSnapshot? orderBook)
    {
        return orderBook is null
            ? null
            : TryGetBookMidpoint(
                TryGetBestBidFromOrderBook(orderBook),
                TryGetBestAskFromOrderBook(orderBook));
    }

    private static decimal? TryGetBookMidpoint(decimal? bestBid, decimal? bestAsk)
    {
        return bestBid is { } bid && bestAsk is { } ask
            ? (bid + ask) / 2m
            : null;
    }

    private async Task<(int EntriesPlaced, int RunsSkipped)> PlaceDueEntryRunsAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        IReadOnlyDictionary<Guid, BtcUpDown5mStrategyVariant> variantsById,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        Guid cycleId,
        string cycleKind,
        string flowName,
        string stageName,
        CancellationToken cancellationToken)
    {
        var maxConcurrency = Math.Max(1, Math.Min(options.MaxConcurrentEntryDecisions, runs.Count));
        var btcCurrentPrices = new BtcCurrentPriceLookupCache();
        var marketLookupTasks = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<PolymarketGammaMarket?>>>(
            StringComparer.OrdinalIgnoreCase);
        var orderBookFetchTasks = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>>(
            StringComparer.OrdinalIgnoreCase);
        var skipBpsStreakMoveSignalTasks = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<BtcPreviousMarketMoveSignal>>>(
            StringComparer.OrdinalIgnoreCase);
        var diffReferenceAverageResultTasks = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<DiffReferenceAverageMarketResultsLookup>>>(
            StringComparer.OrdinalIgnoreCase);
        var middleFastPathResult = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            flowName,
            stageName + ".bulk_skip_middle_reference",
            detail: null,
            variantsById.Count,
            runs.Count,
            GetEarliestEntryDueAtUtc(runs),
            GetLatestEntryDueAtUtc(runs),
            async token => await SkipMiddleReferenceRunsInBulkAsync(
                runs,
                variantsById,
                strategySettings,
                btcCurrentPrices,
                marketLookupTasks,
                token),
            CreateStageOutcome,
            cancellationToken);
        var remainingRuns = middleFastPathResult.RemainingRuns;
        if (remainingRuns.Count == 0)
        {
            return (0, middleFastPathResult.RunsSkipped);
        }

        remainingRuns = OrderDueEntryRunsForPlacement(
            remainingRuns,
            variantsById,
            strategySettings);
        maxConcurrency = Math.Max(1, Math.Min(options.MaxConcurrentEntryDecisions, remainingRuns.Count));
        var distinctMarketCount = remainingRuns
            .Select(run => run.MarketId)
            .Where(marketId => !string.IsNullOrWhiteSpace(marketId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            flowName,
            stageName + ".market_warmup",
            $"DistinctMarkets={distinctMarketCount.ToString(CultureInfo.InvariantCulture)}",
            variantsById.Count,
            remainingRuns.Count,
            GetEarliestEntryDueAtUtc(remainingRuns),
            GetLatestEntryDueAtUtc(remainingRuns),
            async token => await WarmUpEntryMarketsAsync(remainingRuns, marketLookupTasks, token),
            cancellationToken);
        var deferredPersistence = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            flowName,
            stageName + ".deferred_persistence_prepare",
            detail: null,
            variantsById.Count,
            remainingRuns.Count,
            GetEarliestEntryDueAtUtc(remainingRuns),
            GetLatestEntryDueAtUtc(remainingRuns),
            async token => await CreateDeferredPaperEntryPersistenceAsync(
                remainingRuns,
                variantsById,
                strategySettings,
                token),
            outcomeFactory: null,
            cancellationToken);
        var childAssignmentsByParent = await TrackStrategyStageAsync(
            cycleId,
            cycleKind,
            flowName,
            stageName + ".child_parent_assignments",
            detail: null,
            variantsById.Count,
            remainingRuns.Count,
            GetEarliestEntryDueAtUtc(remainingRuns),
            GetLatestEntryDueAtUtc(remainingRuns),
            async token => await GetActiveChildAssignmentsByParentAsync(
                remainingRuns,
                variantsById,
                strategySettings,
                GetUtcNow(),
                token),
            outcomeFactory: null,
            cancellationToken);
        var latencyMetrics = new EntryBatchLatencyMetrics();
        var latencyStartedAtUtc = GetUtcNow();
        var tasks = remainingRuns.Select(async run =>
        {
            if (!variantsById.TryGetValue(StrategyIds.Normalize(run.StrategyId), out var variant))
            {
                return (EntriesPlaced: 0, RunsSkipped: 0);
            }

            var concurrencyWaitStarted = Stopwatch.GetTimestamp();
            try
            {
                await entryDecisionConcurrencyLock.WaitAsync(cancellationToken);
            }
            finally
            {
                latencyMetrics.Record(
                    EntryLatencyPhase.DecisionSemaphoreWait,
                    Stopwatch.GetElapsedTime(concurrencyWaitStarted));
            }

            try
            {
                return await PlaceDueEntryRunAsync(
                    GetUtcNow(),
                    run,
                    variant,
                    strategySettings,
                    btcCurrentPrices,
                    marketLookupTasks,
                    orderBookFetchTasks,
                    skipBpsStreakMoveSignalTasks,
                    diffReferenceAverageResultTasks,
                    deferredPersistence,
                    childAssignmentsByParent,
                    latencyMetrics,
                    cancellationToken);
            }
            finally
            {
                entryDecisionConcurrencyLock.Release();
            }
        }).ToArray();

        (int EntriesPlaced, int RunsSkipped)[] results;
        try
        {
            results = await TrackStrategyStageAsync(
                cycleId,
                cycleKind,
                flowName,
                stageName + ".decision_tasks",
                $"MaxConcurrency={maxConcurrency.ToString(CultureInfo.InvariantCulture)};SharedAcrossEntryFlows=true",
                variantsById.Count,
                remainingRuns.Count,
                GetEarliestEntryDueAtUtc(remainingRuns),
                GetLatestEntryDueAtUtc(remainingRuns),
                async _ => await Task.WhenAll(tasks),
                CreateStageOutcome,
                cancellationToken);
        }
        finally
        {
            try
            {
                await TrackStrategyStageAsync(
                    cycleId,
                    cycleKind,
                    flowName,
                    stageName + (paperEntryPersistenceQueue is null ? ".deferred_persistence_flush" : ".deferred_persistence_enqueue"),
                    detail: null,
                    variantsById.Count,
                    remainingRuns.Count,
                    GetEarliestEntryDueAtUtc(remainingRuns),
                    GetLatestEntryDueAtUtc(remainingRuns),
                    async _ => await PersistDeferredPaperEntryPersistenceAsync(deferredPersistence, CancellationToken.None),
                    CancellationToken.None);
            }
            finally
            {
                await RecordEntryBatchLatencyMetricsAsync(
                    cycleId,
                    cycleKind,
                    flowName,
                    stageName,
                    variantsById.Count,
                    remainingRuns,
                    latencyStartedAtUtc,
                    latencyMetrics,
                    CancellationToken.None);
            }
        }

        return (
            results.Sum(item => item.EntriesPlaced),
            middleFastPathResult.RunsSkipped + results.Sum(item => item.RunsSkipped));
    }

    private async Task RecordEntryBatchLatencyMetricsAsync(
        Guid cycleId,
        string cycleKind,
        string flowName,
        string stageName,
        int variantCount,
        IReadOnlyList<StrategyMarketPaperRun> runs,
        DateTimeOffset startedAtUtc,
        EntryBatchLatencyMetrics metrics,
        CancellationToken cancellationToken)
    {
        var snapshot = metrics.CreateSnapshot();
        await TryRecordStrategyStageTimingAsync(
            cycleId,
            cycleKind,
            flowName,
            stageName + ".wait_breakdown",
            snapshot.Detail,
            startedAtUtc,
            GetUtcNow(),
            snapshot.MaximumMilliseconds,
            variantCount,
            runs.Count,
            GetEarliestEntryDueAtUtc(runs),
            GetLatestEntryDueAtUtc(runs),
            outcome: null,
            succeeded: true,
            errorMessage: null,
            cancellationToken);
    }

    private static async Task<T> MeasureEntryLatencyAsync<T>(
        EntryBatchLatencyMetrics metrics,
        EntryLatencyPhase phase,
        Func<Task<T>> action)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await action();
        }
        finally
        {
            metrics.Record(phase, Stopwatch.GetElapsedTime(started));
        }
    }

    private async Task WaitForEntryPlacementLockAsync(
        EntryBatchLatencyMetrics metrics,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await entryPlacementLock.WaitAsync(cancellationToken);
        }
        finally
        {
            metrics.Record(EntryLatencyPhase.PlacementLockWait, Stopwatch.GetElapsedTime(started));
        }
    }

    private async Task WarmUpEntryMarketsAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<PolymarketGammaMarket?>>> marketLookupTasks,
        CancellationToken cancellationToken)
    {
        var marketIds = runs
            .Select(run => run.MarketId)
            .Where(marketId => !string.IsNullOrWhiteSpace(marketId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (marketIds.Length == 0)
        {
            return;
        }

        var tasks = marketIds
            .Select(marketId => GetPolymarketGammaMarketForEntryAsync(
                marketLookupTasks,
                marketId,
                cancellationToken))
            .ToArray();
        await Task.WhenAll(tasks);
    }

    private Task<DeferredPaperEntryPersistence> CreateDeferredPaperEntryPersistenceAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        IReadOnlyDictionary<Guid, BtcUpDown5mStrategyVariant> variantsById,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new DeferredPaperEntryPersistence());
    }

    private async Task PersistDeferredPaperEntryPersistenceAsync(
        DeferredPaperEntryPersistence deferredPersistence,
        CancellationToken cancellationToken)
    {
        var batch = deferredPersistence.CreateBatch();
        if (batch.IsEmpty)
        {
            return;
        }

        try
        {
            if (paperEntryPersistenceQueue is not null)
            {
                await paperEntryPersistenceQueue.EnqueueAsync(batch, cancellationToken);
                MarkLocallyFinalizedEntryRuns(batch.StrategyRuns);
                exposureCache.ApplyPaperOrders(batch.PaperOrders);
                logger.LogInformation(
                    "BTC Up or Down 5m deferred paper entry persistence queued. Signals={Signals} Orders={Orders} Fills={Fills} PositionMaterializations={PositionMaterializations} Positions={Positions} Runs={Runs}",
                    batch.Signals.Count,
                    batch.PaperOrders.Count,
                    batch.PaperFills.Count,
                    batch.PaperPositionMaterializations.Count,
                    batch.PaperPositions.Count,
                    batch.StrategyRuns.Count);
                return;
            }

            var materializedBatch = await PaperEntryPositionMaterializer.MaterializeAsync(
                batch,
                paperTradingEngine,
                exposureCache,
                cancellationToken);
            await repository.AddPaperEntryPersistenceBatchAsync(materializedBatch, cancellationToken);
            MarkLocallyFinalizedEntryRuns(materializedBatch.StrategyRuns);
            exposureCache.ApplyPaperOrders(materializedBatch.PaperOrders);
            exposureCache.ApplyPaperPositions(materializedBatch.PaperPositions);
            logger.LogInformation(
                "BTC Up or Down 5m deferred paper entry persistence flushed. Signals={Signals} Orders={Orders} Fills={Fills} PositionMaterializations={PositionMaterializations} Positions={Positions} Runs={Runs}",
                materializedBatch.Signals.Count,
                materializedBatch.PaperOrders.Count,
                materializedBatch.PaperFills.Count,
                batch.PaperPositionMaterializations.Count,
                materializedBatch.PaperPositions.Count,
                materializedBatch.StrategyRuns.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist BTC Up or Down 5m deferred paper entry persistence.");
            await TryRecordApiErrorAsync("PersistDeferredPaperEntryPersistence", ex.Message, CancellationToken.None);
            throw;
        }
    }

    public async Task ProcessChildParentRefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            return;
        }

        var cycleId = Guid.NewGuid();
        const string cycleKind = "child_parent_refresh";
        var configuredVariants = GetConfiguredVariants();
        if (!configuredVariants.Any(IsChildMirrorStrategy))
        {
            return;
        }

        await childParentRefreshLock.WaitAsync(cancellationToken);

        try
        {
            var strategySettings = await strategyStateProvider.GetStrategySettingsAsync(cancellationToken);
            var nowUtc = GetUtcNow();
            await TrackStrategyStageAsync(
                cycleId,
                cycleKind,
                "ChildParent",
                "refresh_assignments",
                detail: null,
                configuredVariants.Count,
                runCount: null,
                earliestEntryDueAtUtc: null,
                latestEntryDueAtUtc: null,
                async token => await RefreshChildParentAssignmentsAsync(
                    configuredVariants,
                    strategySettings,
                    nowUtc,
                    token),
                cancellationToken);
        }
        finally
        {
            childParentRefreshLock.Release();
        }
    }

    private async Task RefreshChildParentAssignmentsAsync(
        IReadOnlyList<BtcUpDown5mStrategyVariant> configuredVariants,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var childVariants = configuredVariants
            .Where(IsChildMirrorStrategy)
            .ToArray();
        if (childVariants.Length == 0)
        {
            return;
        }

        var parentVariants = configuredVariants
            .Where(variant => !IsChildMirrorStrategy(variant))
            .Where(variant => !IsFuturesChildParentCandidate(variant))
            .Where(variant => variant.MarketInterval == BtcUpDownMarketInterval.FiveMinutes)
            .Where(variant => IsStrategyActiveForChildSelection(variant, strategySettings, nowUtc))
            .ToArray();
        var parentIds = parentVariants
            .Select(variant => StrategyIds.Normalize(variant.Id))
            .Distinct()
            .ToArray();
        var maxLookbackHours = childVariants.Max(GetChildLookbackHours);
        IReadOnlyList<StrategyLookbackPnl> lookbackPnls = parentIds.Length == 0
            ? Array.Empty<StrategyLookbackPnl>()
            : await repository.GetStrategySettledPnlByLookbackHoursAsync(
                parentIds,
                nowUtc,
                maxLookbackHours,
                cancellationToken);
        var performanceByKey = lookbackPnls.ToDictionary(
            item => (StrategyIds.Normalize(item.StrategyId), item.LookbackHours),
            item => item);
        var selections = new List<StrategyChildParentSelection>(childVariants.Length);
        foreach (var childVariant in childVariants)
        {
            var assetSymbol = GetReferenceAssetSymbol(childVariant);
            var lookbackHours = GetChildLookbackHours(childVariant);
            var childMode = GetChildAssignmentMode(childVariant);
            var useRoiSelection = IsChildRoiMirrorStrategy(childVariant);
            if (!IsStrategyActiveForChildSelection(childVariant, strategySettings, nowUtc))
            {
                selections.Add(new StrategyChildParentSelection(
                    StrategyIds.Normalize(childVariant.Id),
                    null,
                    assetSymbol,
                    lookbackHours,
                    childMode,
                    null,
                    null));
                continue;
            }

            var includeProgressParents = IsChildProgressMirrorStrategy(childVariant);
            var parentSelection = parentVariants
                .Where(parent => string.Equals(GetReferenceAssetSymbol(parent), assetSymbol, StringComparison.OrdinalIgnoreCase))
                .Where(parent => includeProgressParents || !HasProgressInName(parent))
                .Select(parent => new
                {
                    Parent = parent,
                    Performance = performanceByKey.TryGetValue((StrategyIds.Normalize(parent.Id), lookbackHours), out var performance)
                        ? performance
                        : null
                })
                .Where(item => item.Performance is not null && IsEligibleChildParentPerformance(item.Performance, useRoiSelection))
                .OrderByDescending(item => useRoiSelection ? CalculateChildRoiParentScore(item.Performance!) : item.Performance!.RealizedPnlUsd)
                .ThenByDescending(item => useRoiSelection ? item.Performance!.RealizedPnlUsd : 0m)
                .ThenBy(item => item.Parent.Code, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            selections.Add(new StrategyChildParentSelection(
                StrategyIds.Normalize(childVariant.Id),
                parentSelection?.Parent.Id,
                assetSymbol,
                lookbackHours,
                childMode,
                parentSelection?.Performance?.RealizedPnlUsd,
                parentSelection?.Performance?.RoiPct));
        }

        await repository.UpsertStrategyChildParentSelectionsAsync(
            selections,
            nowUtc,
            cancellationToken);
        logger.LogInformation(
            "BTC Up or Down 5m child-parent assignments refreshed. Children={Children} ActiveParents={ActiveParents}",
            selections.Count,
            selections.Count(selection => selection.ParentStrategyId is not null));
    }

    private static bool IsEligibleChildParentPerformance(StrategyLookbackPnl performance, bool useRoiSelection)
    {
        if (performance.RealizedPnlUsd <= 0m)
        {
            return false;
        }

        return !useRoiSelection ||
            (performance.SettledRunsCount >= ChildRoiMinimumSettledRuns &&
             performance.StakeUsd >= ChildRoiMinimumStakeUsd);
    }

    private static decimal CalculateChildRoiParentScore(StrategyLookbackPnl performance)
    {
        return performance.StakeUsd <= 0m
            ? 0m
            : performance.RoiPct * performance.StakeUsd / (performance.StakeUsd + ChildRoiPriorStakeUsd);
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ActiveChildMirrorAssignment>>> GetActiveChildAssignmentsByParentAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        IReadOnlyDictionary<Guid, BtcUpDown5mStrategyVariant> variantsById,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<ActiveChildMirrorAssignment>>();
        }

        var parentIds = runs
            .Select(run => StrategyIds.Normalize(run.StrategyId))
            .Where(strategyId =>
                variantsById.TryGetValue(strategyId, out var variant) &&
                !IsFuturesChildParentCandidate(variant))
            .ToHashSet();
        if (parentIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<ActiveChildMirrorAssignment>>();
        }

        var childVariantsById = StrategyIds.UpDown5mStrategyVariants
            .Where(IsChildMirrorStrategy)
            .ToDictionary(variant => StrategyIds.Normalize(variant.Id));
        var assignments = await repository.GetActiveStrategyChildParentAssignmentsAsync(cancellationToken);
        var grouped = assignments
            .Where(assignment => parentIds.Contains(StrategyIds.Normalize(assignment.ParentStrategyId)))
            .Select(assignment =>
            {
                var childStrategyId = StrategyIds.Normalize(assignment.ChildStrategyId);
                return childVariantsById.TryGetValue(childStrategyId, out var childVariant)
                    ? new ActiveChildMirrorAssignment(assignment, childVariant)
                    : null;
            })
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(item => IsStrategyActiveForChildSelection(item.ChildVariant, strategySettings, nowUtc))
            .GroupBy(item => StrategyIds.Normalize(item.Assignment.ParentStrategyId))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ActiveChildMirrorAssignment>)group
                    .OrderBy(item => item.ChildVariant.Code, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        return grouped;
    }

    private int AddChildPendingPaperEntries(
        IReadOnlyDictionary<Guid, IReadOnlyList<ActiveChildMirrorAssignment>> childAssignmentsByParent,
        BtcUpDown5mStrategyVariant parentVariant,
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote selectedOutcome,
        StrategyMarketPaperRun parentRun,
        Guid parentSignalId,
        Guid parentOrderId,
        decimal orderPrice,
        decimal entryPrice,
        decimal stakeUsd,
        decimal sizeShares,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset nowUtc,
        string executionSource,
        DeferredPaperEntryPersistence deferredPersistence)
    {
        if (!childAssignmentsByParent.TryGetValue(StrategyIds.Normalize(parentVariant.Id), out var childAssignments) ||
            childAssignments.Count == 0)
        {
            return 0;
        }

        var entriesPlaced = 0;
        foreach (var childAssignment in childAssignments)
        {
            var childVariant = childAssignment.ChildVariant;
            var rawDecisionJson = BuildChildMirrorRawDecisionJson(
                parentVariant,
                childVariant,
                childAssignment.Assignment,
                parentRun,
                parentSignalId,
                parentOrderId,
                orderPrice,
                entryPrice,
                stakeUsd,
                sizeShares,
                executionSource,
                nowUtc);
            var childSignal = CreateSignal(
                market,
                selectedOutcome,
                childVariant,
                entryPrice,
                sizeShares,
                stakeUsd,
                nowUtc);
            var childOrder = CreatePendingOpeningLimitPaperOrder(
                childSignal,
                selectedOutcome,
                childVariant,
                orderPrice,
                sizeShares,
                stakeUsd,
                nowUtc,
                expiresAtUtc,
                rawDecisionJson,
                executionSource: executionSource);
            var childRun = CreateChildEnteredRun(
                parentRun,
                childVariant,
                entryPrice,
                stakeUsd,
                sizeShares,
                childSignal.Id,
                childOrder.Id,
                nowUtc);
            deferredPersistence.AddPendingPaperEntry(childSignal, childOrder, childRun);
            entriesPlaced++;
        }

        if (entriesPlaced > 0)
        {
            logger.LogInformation(
                "BTC Up or Down 5m child mirror pending entries created. ParentStrategy={ParentStrategyCode} Market={MarketSlug} Children={Children} Outcome={Outcome} Price={Price} StakeUsd={StakeUsd} SizeShares={SizeShares}",
                parentVariant.Code,
                market.Slug,
                entriesPlaced,
                selectedOutcome.Outcome,
                orderPrice,
                stakeUsd,
                sizeShares);
        }

        return entriesPlaced;
    }

    private int AddChildFilledPaperEntries(
        IReadOnlyDictionary<Guid, IReadOnlyList<ActiveChildMirrorAssignment>> childAssignmentsByParent,
        BtcUpDown5mStrategyVariant parentVariant,
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote selectedOutcome,
        StrategyMarketPaperRun parentRun,
        Guid parentSignalId,
        Guid parentOrderId,
        decimal fillPrice,
        decimal sizeShares,
        decimal stakeUsd,
        decimal currentBid,
        DateTimeOffset nowUtc,
        string executionSource,
        DeferredPaperEntryPersistence deferredPersistence)
    {
        if (!childAssignmentsByParent.TryGetValue(StrategyIds.Normalize(parentVariant.Id), out var childAssignments) ||
            childAssignments.Count == 0)
        {
            return 0;
        }

        var entriesPlaced = 0;
        foreach (var childAssignment in childAssignments)
        {
            var childVariant = childAssignment.ChildVariant;
            var rawDecisionJson = BuildChildMirrorRawDecisionJson(
                parentVariant,
                childVariant,
                childAssignment.Assignment,
                parentRun,
                parentSignalId,
                parentOrderId,
                fillPrice,
                fillPrice,
                stakeUsd,
                sizeShares,
                executionSource,
                nowUtc);
            var childSignal = CreateSignal(
                market,
                selectedOutcome,
                childVariant,
                fillPrice,
                sizeShares,
                stakeUsd,
                nowUtc);
            var childOrder = CreateFilledPaperOrder(
                childSignal,
                selectedOutcome,
                childVariant,
                fillPrice,
                sizeShares,
                stakeUsd,
                nowUtc,
                rawDecisionJson,
                executionSource);
            var childFill = new PaperFill(
                Guid.NewGuid(),
                childOrder.Id,
                fillPrice,
                sizeShares,
                nowUtc,
                string.Concat(
                    "Child mirror copied parent paper fill. ParentStrategy=",
                    parentVariant.Code,
                    " ParentRunId=",
                    parentRun.Id.ToString("D"),
                    " ParentPaperOrderId=",
                    parentOrderId.ToString("D"),
                    "."));
            var childRun = CreateChildEnteredRun(
                parentRun,
                childVariant,
                fillPrice,
                stakeUsd,
                sizeShares,
                childSignal.Id,
                childOrder.Id,
                nowUtc);
            deferredPersistence.AddFilledPaperEntry(
                childSignal,
                childOrder,
                childFill,
                childRun,
                currentBid,
                nowUtc);
            entriesPlaced++;
        }

        if (entriesPlaced > 0)
        {
            logger.LogInformation(
                "BTC Up or Down 5m child mirror filled entries created. ParentStrategy={ParentStrategyCode} Market={MarketSlug} Children={Children} Outcome={Outcome} FillPrice={FillPrice} StakeUsd={StakeUsd} SizeShares={SizeShares}",
                parentVariant.Code,
                market.Slug,
                entriesPlaced,
                selectedOutcome.Outcome,
                fillPrice,
                stakeUsd,
                sizeShares);
        }

        return entriesPlaced;
    }

    private static StrategyMarketPaperRun CreateChildEnteredRun(
        StrategyMarketPaperRun parentRun,
        BtcUpDown5mStrategyVariant childVariant,
        decimal entryPrice,
        decimal stakeUsd,
        decimal sizeShares,
        Guid signalId,
        Guid paperOrderId,
        DateTimeOffset nowUtc)
    {
        return parentRun with
        {
            Id = Guid.NewGuid(),
            StrategyId = StrategyIds.Normalize(childVariant.Id),
            Status = StrategyMarketPaperRunStatuses.Entered,
            EntryPrice = entryPrice,
            StakeUsd = stakeUsd,
            SizeShares = sizeShares,
            SignalId = signalId,
            PaperOrderId = paperOrderId,
            EnteredAtUtc = nowUtc,
            SettlementPrice = null,
            SettlementValueUsd = null,
            RealizedPnlUsd = null,
            SettledAtUtc = null,
            SkipReason = null,
            SkipDiagnosticsJson = null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    private static string BuildChildMirrorRawDecisionJson(
        BtcUpDown5mStrategyVariant parentVariant,
        BtcUpDown5mStrategyVariant childVariant,
        StrategyChildParentAssignment assignment,
        StrategyMarketPaperRun parentRun,
        Guid parentSignalId,
        Guid parentOrderId,
        decimal orderPrice,
        decimal entryPrice,
        decimal stakeUsd,
        decimal sizeShares,
        string executionSource,
        DateTimeOffset nowUtc)
    {
        return JsonSerializer.Serialize(new
        {
            pricing_mode = "child_parent_mirror",
            execution_source = executionSource,
            copied_at_utc = nowUtc,
            assignment_id = assignment.Id,
            child_strategy_id = childVariant.Id,
            child_strategy_code = childVariant.Code,
            child_strategy_name = childVariant.Name,
            child_mode = assignment.ChildMode,
            lookback_hours = assignment.LookbackHours,
            asset_symbol = assignment.AssetSymbol,
            parent_selection_metric = IsChildRoiMirrorStrategy(childVariant) ? "adjusted_roi" : "pnl",
            selected_parent_pnl_usd = assignment.ParentPnlUsd,
            selected_parent_roi_pct = assignment.ParentRoiPct,
            parent_strategy_id = parentVariant.Id,
            parent_strategy_code = parentVariant.Code,
            parent_strategy_name = parentVariant.Name,
            parent_run_id = parentRun.Id,
            parent_signal_id = parentSignalId,
            parent_paper_order_id = parentOrderId,
            market_id = parentRun.MarketId,
            condition_id = parentRun.ConditionId,
            market_slug = parentRun.MarketSlug,
            outcome = parentRun.SelectedOutcome,
            asset_id = parentRun.SelectedAssetId,
            order_price = orderPrice,
            entry_price = entryPrice,
            stake_usd = stakeUsd,
            size_shares = sizeShares
        });
    }

    private async Task<MiddleReferenceBulkSkipResult> SkipMiddleReferenceRunsInBulkAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        IReadOnlyDictionary<Guid, BtcUpDown5mStrategyVariant> variantsById,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        BtcCurrentPriceLookupCache currentPrices,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<PolymarketGammaMarket?>>> marketLookupTasks,
        CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return new MiddleReferenceBulkSkipResult(runs, 0);
        }

        var skippedRuns = new List<StrategyMarketPaperRun>();
        var remainingRuns = new List<StrategyMarketPaperRun>(runs.Count);
        foreach (var marketGroup in runs.GroupBy(run => run.MarketId, StringComparer.OrdinalIgnoreCase))
        {
            var market = (PolymarketGammaMarket?)null;
            foreach (var run in marketGroup)
            {
                var nowUtc = DateTimeOffset.UtcNow;
                if (!variantsById.TryGetValue(StrategyIds.Normalize(run.StrategyId), out var variant) ||
                    !IsMiddleReferenceOpeningLimitEntry(variant) ||
                    GetStrategySettings(strategySettings, variant.Id).IsPausedAt(nowUtc) ||
                    (IsEntryExpired(run.EntryDueAtUtc, nowUtc) &&
                        !IsOpeningLimitEntryAllowedAfterEntryGrace(variant, run.MarketStartUtc, nowUtc)))
                {
                    remainingRuns.Add(run);
                    continue;
                }

                market ??= await GetPolymarketGammaMarketForEntryAsync(
                    marketLookupTasks,
                    run.MarketId,
                    cancellationToken);
                nowUtc = DateTimeOffset.UtcNow;
                if (market is null ||
                    (market.EndDateUtc is { } endDate && endDate <= nowUtc) ||
                    market.Closed ||
                    market.Archived ||
                    IsPreOpenEntryWindowElapsed(variant, GetMarketWindowStartUtc(market, variant) ?? run.MarketStartUtc, nowUtc) ||
                    !market.AcceptingOrders ||
                    !market.EnableOrderBook)
                {
                    remainingRuns.Add(run);
                    continue;
                }

                var settings = GetStrategySettings(strategySettings, variant.Id);
                var decision = await GetMiddleReferenceEntryDecisionAsync(
                    market,
                    variant,
                    settings.PaperStakeAmount,
                    nowUtc,
                    currentPrices,
                    cancellationToken);
                if (decision.ShouldEnter && decision.SelectedOutcome is not null)
                {
                    remainingRuns.Add(run);
                    continue;
                }

                var diagnosticsJson = string.IsNullOrWhiteSpace(decision.RawDecisionJson) ||
                    string.Equals(decision.RawDecisionJson, "{}", StringComparison.Ordinal)
                    ? null
                    : decision.RawDecisionJson;
                skippedRuns.Add(run with
                {
                    ConditionId = market.ConditionId,
                    MarketSlug = market.Slug,
                    MarketTitle = market.Question,
                    Category = market.Category,
                    MarketStartUtc = GetMarketWindowStartUtc(market, variant) ?? run.MarketStartUtc,
                    MarketEndUtc = market.EndDateUtc,
                    Status = StrategyMarketPaperRunStatuses.Skipped,
                    SkipReason = decision.SkipReason ?? "gtd_limit_decision_rejected",
                    SkipDiagnosticsJson = diagnosticsJson,
                    UpdatedAtUtc = nowUtc
                });
            }
        }

        if (skippedRuns.Count == 0)
        {
            return new MiddleReferenceBulkSkipResult(remainingRuns, 0);
        }

        await repository.UpdateStrategyMarketPaperRunsAsync(skippedRuns, cancellationToken);
        foreach (var group in skippedRuns.GroupBy(run => run.SkipReason ?? "unknown", StringComparer.Ordinal))
        {
            logger.LogInformation(
                "BTC Up or Down 5m Middle paper runs bulk skipped. Reason={Reason} Count={Count}",
                group.Key,
                group.Count());
        }

        return new MiddleReferenceBulkSkipResult(remainingRuns, skippedRuns.Count);
    }

    private async Task<(int EntriesPlaced, int RunsSkipped)> PlaceDueEntryRunAsync(
        DateTimeOffset nowUtc,
        StrategyMarketPaperRun dueRun,
        BtcUpDown5mStrategyVariant variant,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings,
        BtcCurrentPriceLookupCache btcCurrentPrices,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<PolymarketGammaMarket?>>> marketLookupTasks,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>> orderBookFetchTasks,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<BtcPreviousMarketMoveSignal>>> skipBpsStreakMoveSignalTasks,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<DiffReferenceAverageMarketResultsLookup>>> diffReferenceAverageResultTasks,
        DeferredPaperEntryPersistence deferredPersistence,
        IReadOnlyDictionary<Guid, IReadOnlyList<ActiveChildMirrorAssignment>> childAssignmentsByParent,
        EntryBatchLatencyMetrics latencyMetrics,
        CancellationToken cancellationToken)
    {
        var entriesPlaced = 0;
        var runsSkipped = 0;

        foreach (var run in new[] { dueRun })
        {
                nowUtc = GetUtcNow();
                try
                {
                    var settings = GetStrategySettings(strategySettings, variant.Id);
                    if (settings.IsPausedAt(nowUtc))
                    {
                        await RecordEntryRunSkippedAsync(
                            run,
                            variant,
                            StrategyPausedSkipReason,
                            nowUtc,
                            deferredPersistence,
                            cancellationToken,
                            BuildStrategyPausedDiagnosticsJson(settings, nowUtc));
                        runsSkipped++;
                        continue;
                    }

                    if (IsEntryExpired(run.EntryDueAtUtc, nowUtc) &&
                        !UsesPreviousCloseBookMarketResult(variant) &&
                        !IsOpeningLimitEntryAllowedAfterEntryGrace(variant, run.MarketStartUtc, nowUtc))
                    {
                        await RecordEntryRunSkippedAsync(
                            run,
                            variant,
                            "entry_due_expired",
                            nowUtc,
                            deferredPersistence,
                            cancellationToken);
                        runsSkipped++;
                        continue;
                    }

                    var market = await MeasureEntryLatencyAsync(
                        latencyMetrics,
                        EntryLatencyPhase.MarketLookup,
                        () => GetPolymarketGammaMarketForEntryAsync(
                            marketLookupTasks,
                            run.MarketId,
                            cancellationToken));
                    if (market is null)
                    {
                        await RecordEntryRunSkippedAsync(
                            run,
                            variant,
                            "market_not_found",
                            nowUtc,
                            deferredPersistence,
                            cancellationToken);
                        runsSkipped++;
                        continue;
                    }

                    if (market.EndDateUtc is { } endDate && endDate <= nowUtc)
                    {
                        await RecordEntryRunSkippedAsync(
                            run,
                            variant,
                            "market_already_ended",
                            nowUtc,
                            deferredPersistence,
                            cancellationToken);
                        runsSkipped++;
                        continue;
                    }

                    if (market.Closed || market.Archived)
                    {
                        await RecordEntryRunSkippedAsync(
                            run,
                            variant,
                            "market_not_tradeable",
                            nowUtc,
                            deferredPersistence,
                            cancellationToken);
                        runsSkipped++;
                        continue;
                    }

                    if (IsPreOpenEntryWindowElapsed(variant, GetMarketWindowStartUtc(market, variant) ?? run.MarketStartUtc, nowUtc))
                    {
                        await RecordEntryRunSkippedAsync(
                            run,
                            variant,
                            "preopen_entry_window_elapsed",
                            nowUtc,
                            deferredPersistence,
                            cancellationToken);
                        runsSkipped++;
                        continue;
                    }

                    if (!market.AcceptingOrders || !market.EnableOrderBook)
                    {
                        if (ShouldDeferUntilTradingStarts(run, variant, nowUtc))
                        {
                            continue;
                        }

                        await RecordEntryRunSkippedAsync(
                            run,
                            variant,
                            "market_not_tradeable",
                            nowUtc,
                            deferredPersistence,
                            cancellationToken);
                        runsSkipped++;
                        continue;
                    }

                    var stakeMultiplier = settings.PaperStakeAmount;
                    var isPaperLiveShadowTest = UsesOpeningLimitEntry(variant) &&
                        ShouldRunPaperLiveShadowTest(settings);
                    var paperLostCounterAdjustment = ApplyPaperLostCounterStakeAdjustment(
                        variant,
                        settings,
                        stakeMultiplier);
                    stakeMultiplier = paperLostCounterAdjustment.EffectiveStakeUsd;

                    if (UsesOpeningLimitEntry(variant))
                    {
                        var limitDecision = await MeasureEntryLatencyAsync(
                            latencyMetrics,
                            EntryLatencyPhase.ReferenceDecision,
                            () => GetOpeningLimitEntryDecisionAsync(
                                market,
                                variant,
                                stakeMultiplier,
                                nowUtc,
                                btcCurrentPrices,
                                skipBpsStreakMoveSignalTasks,
                                diffReferenceAverageResultTasks,
                                cancellationToken));
                        if (!limitDecision.ShouldEnter || limitDecision.SelectedOutcome is null)
                        {
                            if (ShouldDeferOpeningLimitDecision(run, variant, limitDecision, nowUtc))
                            {
                                continue;
                            }

                            await RecordEntryRunSkippedAsync(
                                run,
                                variant,
                                limitDecision.SkipReason ?? "gtd_limit_decision_rejected",
                                nowUtc,
                                deferredPersistence,
                                cancellationToken,
                                limitDecision.RawDecisionJson);
                            runsSkipped++;
                            continue;
                        }

                        if (limitDecision.StakeUsdOverride is > 0m)
                        {
                            stakeMultiplier = limitDecision.StakeUsdOverride.Value;
                        }

                        var limitPricing = await MeasureEntryLatencyAsync(
                            latencyMetrics,
                            EntryLatencyPhase.OrderBook,
                            () => GetOpeningLimitPriceAsync(
                                variant,
                                limitDecision.SelectedOutcome.AssetId,
                                limitDecision.RawDecisionJson,
                                limitDecision.LimitPriceOverride,
                                market.OrderMinSize,
                                stakeMultiplier,
                                nowUtc,
                                orderBookFetchTasks,
                                cancellationToken));
                        if (!limitPricing.ShouldEnter)
                        {
                            await RecordEntryRunSkippedAsync(
                                run,
                                variant,
                                limitPricing.SkipReason ?? "opening_limit_price_rejected",
                                nowUtc,
                                deferredPersistence,
                                cancellationToken,
                                limitPricing.RawDecisionJson);
                            runsSkipped++;
                            continue;
                        }

                        var limitPrice = limitPricing.LimitPrice;
                        var orderPrice = IsFakOrderEntry(variant) || isPaperLiveShadowTest
                            ? ResolveFakGuaranteedWorstPrice(limitPricing.OrderBookLookup?.OrderBook)
                            : limitPrice;
                        var limitSelectedOutcome = limitDecision.SelectedOutcome;
                        var limitSizing = await MeasureEntryLatencyAsync(
                            latencyMetrics,
                            EntryLatencyPhase.OrderBook,
                            () => GetOpeningLimitStakeSizingAsync(
                                limitSelectedOutcome.AssetId,
                                orderPrice,
                                stakeMultiplier,
                                market.OrderMinSize,
                                nowUtc,
                                orderBookFetchTasks,
                                cancellationToken));
                        var expiration = ResolveOpeningLimitExpiration(market, variant, nowUtc);
                        if (!expiration.Available || expiration.LocalExpiresAtUtc is null)
                        {
                            await RecordEntryRunSkippedAsync(
                                run,
                                variant,
                                expiration.RejectionReason ?? "opening_limit_expiration_rejected",
                                nowUtc,
                                deferredPersistence,
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
                            expiration,
                            paperLostCounterAdjustment);
                        var usePaperFakFillModel = IsFakOrderEntry(variant) && !isPaperLiveShadowTest;
                        if (!limitSizing.Available && !usePaperFakFillModel)
                        {
                            if (ShouldDeferOpeningLimitStakeSizing(run, variant, limitSizing, nowUtc))
                            {
                                continue;
                            }

                            await RecordEntryRunSkippedAsync(
                                run,
                                variant,
                                limitSizing.RejectionReason ?? "opening_limit_stake_sizing_rejected",
                                nowUtc,
                                deferredPersistence,
                                cancellationToken,
                                limitRawDecisionJson);
                            runsSkipped++;
                            continue;
                        }

                        if (usePaperFakFillModel)
                        {
                            var paperFakLookup = await MeasureEntryLatencyAsync(
                                latencyMetrics,
                                EntryLatencyPhase.OrderBook,
                                () => GetFreshTakerOrderBookAsync(
                                    limitSelectedOutcome.AssetId,
                                    nowUtc,
                                    orderBookFetchTasks,
                                    cancellationToken));
                            if (paperFakLookup.RejectionReason is not null)
                            {
                                await RecordEntryRunSkippedAsync(
                                    run,
                                    variant,
                                    paperFakLookup.RejectionReason,
                                    nowUtc,
                                    deferredPersistence,
                                    cancellationToken,
                                    AttachFakPaperFillSimulationJson(
                                        limitRawDecisionJson,
                                        paperFakLookup,
                                        limitSizing,
                                        null,
                                        paperFakLookup.RejectionReason,
                                        nowUtc));
                                runsSkipped++;
                                continue;
                            }

                            if (paperFakLookup.OrderBook is not { } paperFakOrderBook)
                            {
                                await RecordEntryRunSkippedAsync(
                                    run,
                                    variant,
                                    "paper_fak_orderbook_missing",
                                    nowUtc,
                                    deferredPersistence,
                                    cancellationToken,
                                    AttachFakPaperFillSimulationJson(
                                        limitRawDecisionJson,
                                        paperFakLookup,
                                        limitSizing,
                                        null,
                                        "paper_fak_orderbook_missing",
                                        nowUtc));
                                runsSkipped++;
                                continue;
                            }

                            var fakOrderBook = ApplyFallbackMinOrderSize(paperFakOrderBook, market.OrderMinSize);
                            paperFakLookup = paperFakLookup with { OrderBook = fakOrderBook };
                            var paperFakSizing = CreateLimitMinimumStakeSizing(
                                fakOrderBook,
                                orderPrice,
                                stakeMultiplier,
                                paperFakLookup.Source);
                            var paperFakRawDecisionJson = AttachOpeningLimitStakeSizingJson(
                                limitPricing.RawDecisionJson,
                                stakeMultiplier,
                                paperFakSizing,
                                expiration,
                                paperLostCounterAdjustment);
                            if (!paperFakSizing.Available)
                            {
                                await RecordEntryRunSkippedAsync(
                                    run,
                                    variant,
                                    paperFakSizing.RejectionReason ?? "paper_fak_stake_sizing_rejected",
                                    nowUtc,
                                    deferredPersistence,
                                    cancellationToken,
                                    AttachFakPaperFillSimulationJson(
                                        paperFakRawDecisionJson,
                                        paperFakLookup,
                                        paperFakSizing,
                                        null,
                                        paperFakSizing.RejectionReason ?? "paper_fak_stake_sizing_rejected",
                                        nowUtc));
                                runsSkipped++;
                                continue;
                            }

                            var paperFakStakeUsd = paperFakSizing.TargetNotionalUsd;
                            var fakEstimate = EstimatePaperFakFill(
                                fakOrderBook,
                                paperFakStakeUsd,
                                orderPrice);
                            var fakRawDecisionJson = AttachFakPaperFillSimulationJson(
                                paperFakRawDecisionJson,
                                paperFakLookup,
                                paperFakSizing,
                                fakEstimate,
                                fakEstimate.RejectionReason,
                                nowUtc);
                            if (!fakEstimate.Filled)
                            {
                                await RecordEntryRunSkippedAsync(
                                    run,
                                    variant,
                                    fakEstimate.RejectionReason ?? "paper_fak_no_immediate_fill",
                                    nowUtc,
                                    deferredPersistence,
                                    cancellationToken,
                                    fakRawDecisionJson);
                                runsSkipped++;
                                continue;
                            }

                            var fillEvidence = string.Concat(
                                "BtcUpDown5mPaper:",
                                variant.Code,
                                ": FAK taker paper fill from ",
                                paperFakLookup.Source,
                                " ask depth. WorstPrice=",
                                orderPrice.ToString("0.########", CultureInfo.InvariantCulture),
                                " AvgFillPrice=",
                                fakEstimate.AverageFillPrice.ToString("0.########", CultureInfo.InvariantCulture),
                                " FilledSize=",
                                fakEstimate.SizeShares.ToString("0.########", CultureInfo.InvariantCulture),
                                " FilledNotionalUsd=",
                                fakEstimate.NotionalUsd.ToString("0.########", CultureInfo.InvariantCulture),
                                " RequestedNotionalUsd=",
                                paperFakStakeUsd.ToString("0.########", CultureInfo.InvariantCulture),
                                " LevelsUsed=",
                                fakEstimate.LevelsUsed.ToString(CultureInfo.InvariantCulture),
                                ".");

                            Signal? fakSignal = null;
                            PaperOrder? fakOrder = null;
                            PaperFill? fakFill = null;
                            var fakChildEntriesPlaced = 0;
                            await WaitForEntryPlacementLockAsync(latencyMetrics, cancellationToken);
                            try
                            {
                                fakSignal = CreateSignal(
                                    market,
                                    limitSelectedOutcome,
                                    variant,
                                    fakEstimate.AverageFillPrice,
                                    fakEstimate.SizeShares,
                                    fakEstimate.NotionalUsd,
                                    nowUtc);
                                fakOrder = CreateFilledPaperOrder(
                                    fakSignal,
                                    limitSelectedOutcome,
                                    variant,
                                    fakEstimate.AverageFillPrice,
                                    fakEstimate.SizeShares,
                                    fakEstimate.NotionalUsd,
                                    nowUtc,
                                    fakRawDecisionJson,
                                    BtcFakTakerPaperExecutionSource);
                                fakFill = new PaperFill(
                                    Guid.NewGuid(),
                                    fakOrder.Id,
                                    fakEstimate.AverageFillPrice,
                                    fakEstimate.SizeShares,
                                    nowUtc,
                                    fillEvidence);
                                var enteredRun = CreateEnteredRun(
                                    run,
                                    market,
                                    limitSelectedOutcome,
                                    fakEstimate.AverageFillPrice,
                                    fakEstimate.NotionalUsd,
                                    fakEstimate.SizeShares,
                                    fakSignal.Id,
                                    fakOrder.Id,
                                    nowUtc);
                                var currentBid = fakOrderBook.BestBid ?? fakEstimate.AverageFillPrice;
                                deferredPersistence.AddFilledPaperEntry(
                                    fakSignal,
                                    fakOrder,
                                    fakFill,
                                    enteredRun,
                                    currentBid,
                                    nowUtc);
                                fakChildEntriesPlaced = AddChildFilledPaperEntries(
                                    childAssignmentsByParent,
                                    variant,
                                    market,
                                    limitSelectedOutcome,
                                    enteredRun,
                                    fakSignal.Id,
                                    fakOrder.Id,
                                    fakEstimate.AverageFillPrice,
                                    fakEstimate.SizeShares,
                                    fakEstimate.NotionalUsd,
                                    currentBid,
                                    nowUtc,
                                    BtcChildMirrorFakPaperExecutionSource,
                                    deferredPersistence);
                            }
                            finally
                            {
                                entryPlacementLock.Release();
                            }

                            if (fakSignal is null || fakOrder is null || fakFill is null)
                            {
                                continue;
                            }

                            entriesPlaced += 1 + fakChildEntriesPlaced;

                            logger.LogInformation(
                                "BTC Up or Down 5m FAK taker paper order filled. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} AvgFillPrice={AvgFillPrice} FilledNotionalUsd={FilledNotionalUsd} FilledSize={FilledSize} WorstPrice={WorstPrice}",
                                variant.Code,
                                market.Slug,
                                limitSelectedOutcome.Outcome,
                                fakEstimate.AverageFillPrice,
                                fakEstimate.NotionalUsd,
                                fakEstimate.SizeShares,
                                orderPrice);
                            await RecordDiffShiftProgressPendingBetAsync(
                                limitDecision.DiffShiftProgressPendingBet,
                                fakEstimate.NotionalUsd,
                                nowUtc,
                                cancellationToken);
                            continue;
                        }

                        var stakeUsd = limitSizing.TargetNotionalUsd;
                        var limitSizeShares = limitSizing.TargetSizeShares;
                        var shadowDecisionTargetNotionalUsd = stakeUsd;
                        var shadowDecisionRequestedSizeShares = limitSizeShares;
                        var shadowDecisionMaxReservedNotionalUsd = limitSizeShares * orderPrice;
                        TakerOrderBookLookupResult? shadowFakLookup = null;
                        BtcMinimumStakeSizing? shadowFakSizing = null;
                        TakerBuyFillEstimate? shadowFakEstimate = null;
                        string? shadowFakFillEvidence = null;
                        PaperLiveShadowOrderBookSnapshotResult? shadowSnapshot = null;
                        var paperLiveShadowStakeUsd = settings.LiveStakeAmount;
                        if (isPaperLiveShadowTest)
                        {
                            paperLiveShadowStakeUsd = GetPaperLiveShadowStakeUsd(variant, settings);
                            shadowSnapshot = await MeasureEntryLatencyAsync(
                                latencyMetrics,
                                EntryLatencyPhase.OrderBook,
                                () => GetPaperLiveShadowOrderBookSnapshotAsync(
                                    limitSelectedOutcome.AssetId,
                                    nowUtc,
                                    cancellationToken));
                            if (shadowSnapshot.OrderBook is null)
                            {
                                var shadowRawDecisionJson = AttachPaperLiveShadowDecisionJson(
                                    limitRawDecisionJson,
                                    null,
                                    null,
                                    null,
                                    "paper_live_shadow_snapshot_missing",
                                    PaperLiveShadowTestSource,
                                    expiration,
                                    liveOrderType: GetPaperLiveShadowLiveOrderType(variant),
                                    fakStatsProbe: IsFakStatsProbeEntry(variant));
                                await RecordEntryRunSkippedAsync(
                                    run,
                                    variant,
                                    shadowSnapshot.RejectionReason ?? "paper_live_shadow_snapshot_missing",
                                    nowUtc,
                                    deferredPersistence,
                                    cancellationToken,
                                    shadowRawDecisionJson);
                                runsSkipped++;
                                continue;
                            }

                            var shadowFakOrderBook = ApplyFallbackMinOrderSize(
                                shadowSnapshot.OrderBook,
                                market.OrderMinSize);
                            shadowSnapshot = shadowSnapshot with { OrderBook = shadowFakOrderBook };
                            shadowFakLookup = TakerOrderBookLookupResult.Found(
                                shadowFakOrderBook,
                                shadowSnapshot.Source,
                                shadowSnapshot.Age);
                            shadowFakSizing = CreateLimitMinimumStakeSizing(
                                shadowFakOrderBook,
                                orderPrice,
                                paperLiveShadowStakeUsd,
                                shadowSnapshot.Source);
                            var shadowFakRawDecisionJson = AttachOpeningLimitStakeSizingJson(
                                limitPricing.RawDecisionJson,
                                paperLiveShadowStakeUsd,
                                shadowFakSizing,
                                expiration);
                            if (!shadowFakSizing.Available)
                            {
                                await RecordEntryRunSkippedAsync(
                                    run,
                                    variant,
                                    shadowFakSizing.RejectionReason ?? "paper_live_shadow_fak_stake_sizing_rejected",
                                    nowUtc,
                                    deferredPersistence,
                                    cancellationToken,
                                    AttachFakPaperFillSimulationJson(
                                        shadowFakRawDecisionJson,
                                        shadowFakLookup,
                                        shadowFakSizing,
                                        null,
                                        shadowFakSizing.RejectionReason ?? "paper_live_shadow_fak_stake_sizing_rejected",
                                        nowUtc));
                                runsSkipped++;
                                continue;
                            }

                            shadowFakEstimate = EstimatePaperFakFill(
                                shadowFakOrderBook,
                                shadowFakSizing.TargetNotionalUsd,
                                orderPrice);
                            limitRawDecisionJson = AttachFakPaperFillSimulationJson(
                                shadowFakRawDecisionJson,
                                shadowFakLookup,
                                shadowFakSizing,
                                shadowFakEstimate,
                                shadowFakEstimate.RejectionReason,
                                nowUtc);
                            if (!shadowFakEstimate.Filled)
                            {
                                await RecordEntryRunSkippedAsync(
                                    run,
                                    variant,
                                    shadowFakEstimate.RejectionReason ?? "paper_live_shadow_fak_no_immediate_fill",
                                    nowUtc,
                                    deferredPersistence,
                                    cancellationToken,
                                    limitRawDecisionJson);
                                runsSkipped++;
                                continue;
                            }

                            shadowDecisionTargetNotionalUsd = shadowFakSizing.TargetNotionalUsd;
                            shadowDecisionRequestedSizeShares = shadowFakSizing.TargetSizeShares;
                            shadowDecisionMaxReservedNotionalUsd = shadowFakSizing.TargetNotionalUsd;
                            stakeUsd = shadowFakEstimate.NotionalUsd;
                            limitSizeShares = shadowFakEstimate.SizeShares;
                            shadowFakFillEvidence = string.Concat(
                                "BtcUpDown5mPaper:",
                                variant.Code,
                                ": FAK taker paper live-shadow fill from ",
                                shadowFakLookup.Source,
                                " ask depth. WorstPrice=",
                                orderPrice.ToString("0.########", CultureInfo.InvariantCulture),
                                " AvgFillPrice=",
                                shadowFakEstimate.AverageFillPrice.ToString("0.########", CultureInfo.InvariantCulture),
                                " FilledSize=",
                                shadowFakEstimate.SizeShares.ToString("0.########", CultureInfo.InvariantCulture),
                                " FilledNotionalUsd=",
                                shadowFakEstimate.NotionalUsd.ToString("0.########", CultureInfo.InvariantCulture),
                                " RequestedNotionalUsd=",
                                shadowFakSizing.TargetNotionalUsd.ToString("0.########", CultureInfo.InvariantCulture),
                                " LevelsUsed=",
                                shadowFakEstimate.LevelsUsed.ToString(CultureInfo.InvariantCulture),
                                ".");
                        }

                        Guid? correlationId = null;
                        Signal? limitSignal = null;
                        PaperOrder? limitOrder = null;
                        var entryPrice = shadowFakEstimate?.AverageFillPrice ?? orderPrice;
                        var entryStakeUsd = stakeUsd;
                        var entrySizeShares = limitSizeShares;
                        var orderPersistedDeferred = false;
                        var openingChildEntriesPlaced = 0;
                        await WaitForEntryPlacementLockAsync(latencyMetrics, cancellationToken);
                        try
                        {
                            if (isPaperLiveShadowTest && shadowSnapshot?.OrderBook is { } shadowOrderBook)
                            {
                                correlationId = Guid.NewGuid();
                                var quoteAgeMs = (int)Math.Round(GetSnapshotAge(shadowOrderBook.SnapshotAtUtc).TotalMilliseconds);
                                var shadowDecision = new PaperLiveShadowDecision(
                                    correlationId.Value,
                                    variant.Id,
                                    market.MarketId,
                                    market.ConditionId,
                                    limitSelectedOutcome.AssetId,
                                    limitSelectedOutcome.Outcome,
                                    TradeSide.Buy,
                                    orderPrice,
                                    shadowDecisionTargetNotionalUsd,
                                    shadowDecisionRequestedSizeShares,
                                    shadowDecisionMaxReservedNotionalUsd,
                                    GetPaperLiveShadowLiveOrderType(variant),
                                    false,
                                    SerializePaperLiveShadowOrderBookSnapshot(shadowOrderBook, shadowSnapshot.Source, shadowSnapshot.Age),
                                    quoteAgeMs,
                                    PaperLiveShadowTestSource,
                                    shadowOrderBook.SnapshotAtUtc,
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
                                    shadowOrderBook,
                                    null,
                                    PaperLiveShadowTestSource,
                                    expiration,
                                    liveOrderType: GetPaperLiveShadowLiveOrderType(variant),
                                    fakStatsProbe: IsFakStatsProbeEntry(variant));
                            }

                            limitSignal = CreateSignal(
                                market,
                                limitSelectedOutcome,
                                variant,
                                entryPrice,
                                limitSizeShares,
                                stakeUsd,
                                nowUtc);
                            limitOrder = isPaperLiveShadowTest
                                ? CreatePendingOpeningLimitPaperOrder(
                                    limitSignal,
                                    limitSelectedOutcome,
                                    variant,
                                    orderPrice,
                                    shadowDecisionRequestedSizeShares,
                                    shadowDecisionTargetNotionalUsd,
                                    nowUtc,
                                    cancelDeadlineUtc,
                                    limitRawDecisionJson,
                                    correlationId,
                                    PaperLiveShadowTestSource)
                                : CreatePendingOpeningLimitPaperOrder(
                                    limitSignal,
                                    limitSelectedOutcome,
                                    variant,
                                    orderPrice,
                                    limitSizeShares,
                                    stakeUsd,
                                    nowUtc,
                                    cancelDeadlineUtc,
                                    limitRawDecisionJson,
                                    correlationId,
                                    isPaperLiveShadowTest ? PaperLiveShadowTestSource : string.Empty);

                            if (isPaperLiveShadowTest)
                            {
                                await repository.AddSignalAndPaperOrderAsync(limitSignal, limitOrder, cancellationToken);
                            }
                            else
                            {
                                var enteredRun = CreateEnteredRun(
                                    run,
                                    market,
                                    limitSelectedOutcome,
                                    entryPrice,
                                    entryStakeUsd,
                                    entrySizeShares,
                                    limitSignal.Id,
                                    limitOrder.Id,
                                    nowUtc);
                                deferredPersistence.AddPendingPaperEntry(limitSignal, limitOrder, enteredRun);
                                orderPersistedDeferred = true;
                                openingChildEntriesPlaced = AddChildPendingPaperEntries(
                                    childAssignmentsByParent,
                                    variant,
                                    market,
                                    limitSelectedOutcome,
                                    enteredRun,
                                    limitSignal.Id,
                                    limitOrder.Id,
                                    orderPrice,
                                    entryPrice,
                                    entryStakeUsd,
                                    entrySizeShares,
                                    cancelDeadlineUtc,
                                    nowUtc,
                                    BtcChildMirrorPaperExecutionSource,
                                    deferredPersistence);
                            }

                            if (isPaperLiveShadowTest)
                            {
                                exposureCache.ApplyPaperOrder(limitOrder);
                            }

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
                            }
                        }
                        finally
                        {
                            entryPlacementLock.Release();
                        }

                        if (limitSignal is null || limitOrder is null)
                        {
                            continue;
                        }

                        if (orderPersistedDeferred)
                        {
                            entriesPlaced += 1 + openingChildEntriesPlaced;

                            logger.LogInformation(
                                "BTC Up or Down 5m GTD limit paper order placed. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} Price={Price} StakeUsd={StakeUsd} SizeShares={SizeShares}",
                                variant.Code,
                                market.Slug,
                                limitSelectedOutcome.Outcome,
                                orderPrice,
                                stakeUsd,
                                limitSizeShares);
                            continue;
                        }

                        if (isPaperLiveShadowTest && correlationId is { } paperLiveShadowCorrelationId)
                        {
                            var placementResult = await TryPlacePaperLiveShadowOrderAsync(
                                limitSignal,
                                limitSelectedOutcome,
                                variant,
                                limitOrder,
                                orderPrice,
                                paperLiveShadowStakeUsd,
                                expiration,
                                paperLiveShadowCorrelationId,
                                BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market),
                                market.EndDateUtc,
                                nowUtc,
                                cancellationToken);
                            var enteredRun = CreateEnteredRun(
                                run,
                                market,
                                limitSelectedOutcome,
                                entryPrice,
                                entryStakeUsd,
                                entrySizeShares,
                                limitSignal.Id,
                                limitOrder.Id,
                                nowUtc);
                            if (placementResult.Placed && placementResult.LiveOrder is { } liveOrder)
                            {
                                await ApplyActualLiveFillToPaperShadowAsync(
                                    limitOrder,
                                    enteredRun,
                                    liveOrder,
                                    DateTimeOffset.UtcNow,
                                    cancellationToken);
                            }
                            else if (placementResult.KeepPaperEntry)
                            {
                                await ApplyPaperModeFillToPaperShadowAsync(
                                    limitOrder,
                                    enteredRun,
                                    entryPrice,
                                    entryStakeUsd,
                                    entrySizeShares,
                                    shadowFakLookup?.OrderBook?.BestBid ?? entryPrice,
                                    shadowFakFillEvidence ?? "Paper live-shadow skipped Live placement; applied paper-mode fill.",
                                    nowUtc,
                                    cancellationToken);
                            }
                            else
                            {
                                await repository.UpdateStrategyMarketPaperRunAsync(
                                    MarkPaperLiveShadowRunSkipped(enteredRun, placementResult, DateTimeOffset.UtcNow),
                                    cancellationToken);
                            }
                        }
                        else
                        {
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
                                    EntryPrice = entryPrice,
                                    StakeUsd = entryStakeUsd,
                                    SizeShares = entrySizeShares,
                                    SignalId = limitSignal.Id,
                                    PaperOrderId = limitOrder.Id,
                                    EnteredAtUtc = nowUtc,
                                    UpdatedAtUtc = nowUtc
                                },
                                cancellationToken);
                        }

                        entriesPlaced++;

                        if (IsFakOrderEntry(variant))
                        {
                            logger.LogInformation(
                                "BTC Up or Down 5m FAK paper live-shadow order processed. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} EntryPrice={EntryPrice} StakeUsd={StakeUsd} SizeShares={SizeShares}",
                                variant.Code,
                                market.Slug,
                                limitSelectedOutcome.Outcome,
                                entryPrice,
                                entryStakeUsd,
                                entrySizeShares);
                        }
                        else
                        {
                            logger.LogInformation(
                                "BTC Up or Down 5m GTD limit paper order placed. Strategy={StrategyCode} Market={MarketSlug} Outcome={Outcome} Price={Price} StakeUsd={StakeUsd} SizeShares={SizeShares}",
                                variant.Code,
                                market.Slug,
                                limitSelectedOutcome.Outcome,
                                limitPrice,
                                stakeUsd,
                                limitSizeShares);
                        }
                        continue;
                    }

                    BtcUpDown5mOutcomeQuote? selectedOutcome;
                    BtcPaperEntryPricingResult entryPricing;
                    if (options.PaperTakerPricingEnabled && !UsesGammaOutcomeSelection(variant))
                    {
                        var outcomeSelection = await MeasureEntryLatencyAsync(
                            latencyMetrics,
                            EntryLatencyPhase.OrderBook,
                            () => GetTakerPaperOutcomeSelectionAsync(
                                market,
                                variant,
                                stakeMultiplier,
                                nowUtc,
                                cancellationToken));
                        if (!outcomeSelection.Filled ||
                            outcomeSelection.SelectedOutcome is null ||
                            outcomeSelection.EntryPricing is null)
                        {
                            await RecordEntryRunSkippedAsync(
                                run,
                                variant,
                                outcomeSelection.RejectionReason ?? "paper_taker_outcome_selection_rejected",
                                nowUtc,
                                deferredPersistence,
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
                            await RecordEntryRunSkippedAsync(
                                run,
                                variant,
                                "target_outcome_not_available",
                                nowUtc,
                                deferredPersistence,
                                cancellationToken);
                            runsSkipped++;
                            continue;
                        }

                        if (!IsDirectionalPriceAllowedForVariant(selectedOutcome.Price, variant))
                        {
                            await RecordEntryRunSkippedAsync(
                                run,
                                variant,
                                SignalReasonCodes.OutcomePriceDirectionMismatch,
                                nowUtc,
                                deferredPersistence,
                                cancellationToken);
                            runsSkipped++;
                            continue;
                        }

                        entryPricing = await MeasureEntryLatencyAsync(
                            latencyMetrics,
                            EntryLatencyPhase.OrderBook,
                            () => GetPaperEntryPricingAsync(
                                market,
                                selectedOutcome,
                                variant,
                                stakeMultiplier,
                                nowUtc,
                                enforceTakerDirectionalPrice: !UsesGammaOutcomeSelection(variant),
                                cancellationToken));
                    }

                    if (!entryPricing.Filled)
                    {
                        await RecordEntryRunSkippedAsync(
                            run,
                            variant,
                            entryPricing.RejectionReason ?? "paper_entry_pricing_rejected",
                            nowUtc,
                            deferredPersistence,
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
                        await RecordEntryRunSkippedAsync(
                            run,
                            variant,
                            gtdSizing.RejectionReason ?? "paper_gtd_stake_sizing_rejected",
                            nowUtc,
                            deferredPersistence,
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
                        await RecordEntryRunSkippedAsync(
                            run,
                            variant,
                            gtdExpiration.RejectionReason ?? "paper_gtd_expiration_rejected",
                            nowUtc,
                            deferredPersistence,
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
                        gtdExpiration,
                        paperLostCounterAdjustment);
                    var shouldSubmitLegacyLiveOrder = settings.EffectiveLiveStakes &&
                        botOptions.Mode == BotMode.Live &&
                        !UsesGammaOutcomeSelection(variant) &&
                        !UsesOpeningLimitEntry(variant) &&
                        CanSubmitLegacyBtcLiveOrder(variant);
                    Signal? signal = null;
                    PaperOrder? order = null;
                    var gtdChildEntriesPlaced = 0;
                    await WaitForEntryPlacementLockAsync(latencyMetrics, cancellationToken);
                    try
                    {
                        signal = CreateSignal(market, selectedOutcome, variant, gtdLimitPrice, sizeShares, reservedNotionalUsd, nowUtc);
                        order = CreatePendingOpeningLimitPaperOrder(
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

                        if (shouldSubmitLegacyLiveOrder)
                        {
                            await repository.AddSignalAndPaperOrderAsync(signal, order, cancellationToken);
                        }
                        else
                        {
                            var enteredRun = CreateEnteredRun(
                                run,
                                market,
                                selectedOutcome,
                                gtdLimitPrice,
                                reservedNotionalUsd,
                                sizeShares,
                                signal.Id,
                                order.Id,
                                nowUtc);
                            deferredPersistence.AddPendingPaperEntry(signal, order, enteredRun);
                            gtdChildEntriesPlaced = AddChildPendingPaperEntries(
                                childAssignmentsByParent,
                                variant,
                                market,
                                selectedOutcome,
                                enteredRun,
                                signal.Id,
                                order.Id,
                                gtdLimitPrice,
                                gtdLimitPrice,
                                reservedNotionalUsd,
                                sizeShares,
                                gtdCancelDeadlineUtc,
                                nowUtc,
                                BtcChildMirrorPaperExecutionSource,
                                deferredPersistence);
                        }

                        if (shouldSubmitLegacyLiveOrder)
                        {
                            exposureCache.ApplyPaperOrder(order);
                        }
                    }
                    finally
                    {
                        entryPlacementLock.Release();
                    }

                    if (signal is null || order is null)
                    {
                        continue;
                    }

                    if (shouldSubmitLegacyLiveOrder)
                    {
                        await repository.UpdateStrategyMarketPaperRunAsync(
                            CreateEnteredRun(
                                run,
                                market,
                                selectedOutcome,
                                gtdLimitPrice,
                                reservedNotionalUsd,
                                sizeShares,
                                signal.Id,
                                order.Id,
                                nowUtc),
                            cancellationToken);

                        var liveLostCounterAdjustment = ApplyLiveLostCounterStakeAdjustment(
                            variant,
                            settings,
                            settings.LiveStakeAmount);
                        await TryPlaceLiveOrderAsync(
                            signal,
                            selectedOutcome,
                            variant,
                            gtdLimitPrice,
                            liveLostCounterAdjustment.EffectiveStakeUsd,
                            nowUtc,
                            cancellationToken);
                    }

                    entriesPlaced += 1 + gtdChildEntriesPlaced;

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

        PreOpenSellExitSummary? preOpenSellExitSummary = null;
        if (IsPreOpenFixedDirectionSellExit(runVariant) &&
            openingLimitFillSummary is not null)
        {
            preOpenSellExitSummary = await GetPreOpenSellExitSummaryAsync(
                run,
                runVariant,
                openingLimitFillSummary,
                cancellationToken);
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
            var entrySizeShares = openingLimitFillSummary?.SizeShares ?? run.SizeShares.Value;
            var entryPrice = openingLimitFillSummary?.AverageFillPrice ?? run.EntryPrice.Value;
            var costBasisUsd = openingLimitFillSummary?.NotionalUsd ?? run.StakeUsd;
            var soldSizeShares = Math.Min(entrySizeShares, preOpenSellExitSummary?.SoldSizeShares ?? 0m);
            var remainingSizeShares = Math.Max(0m, entrySizeShares - soldSizeShares);
            var soldProceedsUsd = preOpenSellExitSummary?.ProceedsUsd ?? 0m;
            var remainingCostBasisUsd = entrySizeShares > 0m
                ? costBasisUsd * (remainingSizeShares / entrySizeShares)
                : 0m;
            var settlementValue = remainingSizeShares * settlementPrice;
            var settlementRealizedPnl = settlementValue - remainingCostBasisUsd;
            var totalSettlementValue = soldProceedsUsd + settlementValue;
            var realizedPnl = totalSettlementValue - costBasisUsd;
            var category = metadata.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Category))?.Category ?? run.Category;

            if (!IsFixedOutcomeMaker(runVariant) && remainingSizeShares > 0m)
            {
                var settlement = new PaperPositionSettlement(
                    Guid.NewGuid(),
                    runVariant.CopiedTraderWallet,
                    run.SelectedAssetId,
                    run.ConditionId,
                    run.SelectedOutcome,
                    winningAssetId,
                    winningOutcome,
                    category,
                    remainingSizeShares,
                    entryPrice,
                    remainingCostBasisUsd,
                    settlementValue,
                    settlementRealizedPnl,
                    won,
                    "BtcUpDown5mGammaClosedMarket",
                    nowUtc,
                    nowUtc);

                await repository.TryAddPaperPositionSettlementAsync(settlement, cancellationToken);
            }

            if (!IsFixedOutcomeMaker(runVariant))
            {
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
            }

            await repository.UpdateStrategyMarketPaperRunAsync(
                run with
                {
                    Status = StrategyMarketPaperRunStatuses.Settled,
                    EntryPrice = entryPrice,
                    StakeUsd = costBasisUsd,
                    SizeShares = entrySizeShares,
                    SettlementPrice = settlementPrice,
                    SettlementValueUsd = totalSettlementValue,
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

            var settings = await strategyStateProvider.GetStrategySettingsAsync(runVariant.Id, cancellationToken);
            await UpdatePaperLostCounterAfterSettlementAsync(runVariant, settings, won, nowUtc, cancellationToken);

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

        var conservativeFillSummary = await TryFillOpeningLimitFromConservativeSnapshotAsync(
            order,
            fills,
            nowUtc,
            cancellationToken);
        if (conservativeFillSummary is not null)
        {
            return conservativeFillSummary;
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

    private async Task<OpeningLimitFillSummary?> TryFillOpeningLimitFromConservativeSnapshotAsync(
        PaperOrder order,
        IReadOnlyList<PaperFill> existingFills,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var previouslyFilledShares = GetFilledShares(existingFills, order.SizeShares);
        var evaluation = conservativeGtdFillEstimator.Evaluate(order, null, nowUtc, previouslyFilledShares);
        if (!evaluation.Handled)
        {
            return null;
        }

        if (evaluation.Fill is null)
        {
            if (evaluation.OrderChanged)
            {
                await repository.UpdatePaperOrderAsync(evaluation.Order, cancellationToken);
                exposureCache.ApplyPaperOrder(evaluation.Order);
            }

            return null;
        }

        var filledOrder = paperTradingEngine.ApplyFillStatus(
            evaluation.Order,
            evaluation.Fill,
            previouslyFilledShares);
        await repository.AddPaperFillAsync(evaluation.Fill, cancellationToken);
        await repository.UpdatePaperOrderAsync(filledOrder, cancellationToken);
        exposureCache.ApplyPaperOrder(filledOrder);

        if (evaluation.Order.Side == TradeSide.Buy)
        {
            var positions = await repository.GetPaperPositionsAsync(cancellationToken);
            var currentPosition = FindPaperPosition(positions, evaluation.Order);
            var updatedPosition = paperTradingEngine.ApplyBuyFill(
                currentPosition,
                evaluation.Order,
                evaluation.Fill,
                evaluation.Fill.Price,
                nowUtc);
            await repository.UpsertPaperPositionAsync(updatedPosition, cancellationToken);
            exposureCache.ApplyPaperPosition(updatedPosition);
            await repository.ActivatePaperCopiedLeaderPositionAsync(
                evaluation.Order.Id,
                evaluation.Fill.SizeShares,
                evaluation.Fill.FilledAtUtc,
                cancellationToken);
        }

        return SummarizeOpeningLimitFills(
            filledOrder,
            [.. existingFills, evaluation.Fill]);
    }

    private async Task<PreOpenSellExitSummary> GetPreOpenSellExitSummaryAsync(
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        OpeningLimitFillSummary entryFillSummary,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(run.SelectedAssetId))
        {
            return PreOpenSellExitSummary.Empty;
        }

        var createdAfterUtc = GetLastQuarterStartUtc(run.MarketStartUtc, run.MarketEndUtc) ??
            run.EnteredAtUtc ??
            run.CreatedAtUtc;
        var orders = await repository.GetPaperOrdersForStrategyAssetAsync(
            variant.Id,
            variant.CopiedTraderWallet,
            run.SelectedAssetId,
            createdAfterUtc,
            limit: 50,
            cancellationToken);

        var soldSizeShares = 0m;
        var proceedsUsd = 0m;
        DateTimeOffset? lastFilledAtUtc = null;
        foreach (var order in orders
            .Where(order => order.Side == TradeSide.Sell)
            .Where(order => string.Equals(order.ConditionId, run.ConditionId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(order => order.CreatedAtUtc)
            .ThenBy(order => order.Id))
        {
            if (soldSizeShares >= entryFillSummary.SizeShares)
            {
                break;
            }

            var fills = await repository.GetPaperFillsForOrderAsync(order.Id, cancellationToken);
            foreach (var fill in fills.OrderBy(fill => fill.FilledAtUtc).ThenBy(fill => fill.Id))
            {
                if (soldSizeShares >= entryFillSummary.SizeShares)
                {
                    break;
                }

                var fillSize = Math.Max(0m, fill.SizeShares);
                var takeShares = Math.Min(entryFillSummary.SizeShares - soldSizeShares, fillSize);
                if (takeShares <= 0m)
                {
                    continue;
                }

                soldSizeShares += takeShares;
                proceedsUsd += takeShares * fill.Price;
                lastFilledAtUtc = fill.FilledAtUtc;
            }
        }

        return new PreOpenSellExitSummary(soldSizeShares, proceedsUsd, lastFilledAtUtc);
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

    private static decimal GetFilledShares(IReadOnlyList<PaperFill> fills, decimal maxShares)
    {
        return Math.Min(maxShares, fills.Sum(fill => Math.Max(0m, fill.SizeShares)));
    }

    private static PaperPosition? FindPaperPosition(
        IEnumerable<PaperPosition> positions,
        PaperOrder order)
    {
        return positions.FirstOrDefault(position =>
            string.Equals(position.AssetId, order.AssetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(position.CopiedTraderWallet, order.CopiedTraderWallet, StringComparison.OrdinalIgnoreCase));
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
            BtcUpDown5mStrategyBehavior.MiddleReferenceInstant or
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant or
            BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults or
            BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert or
            BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThreshold or
            BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThresholdInstant or
            BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant or
            BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFak or
            BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket or
            BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket or
            BtcUpDown5mStrategyBehavior.FilteredReferenceAverageBpsThresholdFakPremarket or
            BtcUpDown5mStrategyBehavior.AbsoluteBpsThresholdFakPremarket or
            BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarket or
            BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarketRevert or
            BtcUpDown5mStrategyBehavior.SimpleFixedOutcomeInstant or
            BtcUpDown5mStrategyBehavior.ChildMirror or
            BtcUpDown5mStrategyBehavior.ChildProgressMirror or
            BtcUpDown5mStrategyBehavior.ChildRoiMirror or
            BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror or
            BtcUpDown5mStrategyBehavior.AlwaysUp or
            BtcUpDown5mStrategyBehavior.AlwaysDown or
            BtcUpDown5mStrategyBehavior.BinanceStartRelative or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThresholdInstant or
            BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThreshold or
            BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThresholdInstant or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeClever or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeCleverMargin or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeEdge or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeDelayed or
            BtcUpDown5mStrategyBehavior.EnsembleVote or
            BtcUpDown5mStrategyBehavior.DynamicMarkov or
            BtcUpDown5mStrategyBehavior.StrategySelector or
            BtcUpDown5mStrategyBehavior.StandardEntryPriceCap or
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelectionEntryPriceCap or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrend or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFak or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarket or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakRevert or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert or
            BtcUpDown5mStrategyBehavior.PreOpenFixedDirection or
            BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell or
            BtcUpDown5mStrategyBehavior.FixedOutcomeMaker or
            BtcUpDown5mStrategyBehavior.DiffCounterTrend or
            BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend or
            BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend or
            BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket or
            BtcUpDown5mStrategyBehavior.DiffProgress or
            BtcUpDown5mStrategyBehavior.DiffShiftProgress or
            BtcUpDown5mStrategyBehavior.DiffLimitProgressPremarket or
            BtcUpDown5mStrategyBehavior.DiffRealLimitProgressPremarket or
            BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket or
            BtcUpDown5mStrategyBehavior.BpsConfirmedAveragePremarket or
            BtcUpDown5mStrategyBehavior.DiffConfirmedAveragePremarket;
    }

    private static bool IsMiddleReferenceOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.MiddleReference or
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevert or
            BtcUpDown5mStrategyBehavior.MiddleReferenceInstant or
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant;
    }

    private static bool UsesConvertedTakerGtdPaperOrderSettlement(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.Standard or
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelection;
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
        return variant.Behavior is BtcUpDown5mStrategyBehavior.PreOpenFixedDirection or
            BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell;
    }

    private static bool IsFixedOutcomePreviousResultBpsFakPremarketEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket;
    }

    private static bool IsReferenceAverageBpsFakPremarketEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket or
            BtcUpDown5mStrategyBehavior.FilteredReferenceAverageBpsThresholdFakPremarket or
            BtcUpDown5mStrategyBehavior.BpsConfirmedAveragePremarket;
    }

    private static bool IsFilteredReferenceAverageBpsFakPremarketEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.FilteredReferenceAverageBpsThresholdFakPremarket;
    }

    private static bool IsAbsoluteBpsFakPremarketEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.AbsoluteBpsThresholdFakPremarket;
    }

    private static bool IsFuturesBasisBpsFakPremarketEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarket or
            BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarketRevert;
    }

    private static bool IsFuturesBasisBpsFakPremarketRevertEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarketRevert;
    }

    private static int GetPremarketPreviousResultSampleSecondsBeforeEnd(BtcUpDown5mStrategyVariant variant)
    {
        return IsFixedOutcomePreviousResultBpsFakPremarketEntry(variant) && variant.EntryDelaySeconds < 0
            ? -variant.EntryDelaySeconds
            : PremarketPreviousResultDefaultSampleSecondsBeforeEnd;
    }

    private static string GetPremarketPreviousResultSource(BtcUpDown5mStrategyVariant variant)
    {
        return PremarketPreviousResultSourcePrefix +
            GetPremarketPreviousResultSampleSecondsBeforeEnd(variant).ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsPreOpenTimedOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return IsPreOpenFixedDirectionOpeningLimitEntry(variant) ||
            IsFixedOutcomePreviousResultBpsFakPremarketEntry(variant) ||
            IsReferenceAverageBpsFakPremarketEntry(variant) ||
            IsAbsoluteBpsFakPremarketEntry(variant) ||
            IsFuturesBasisBpsFakPremarketEntry(variant) ||
            IsPreviousScoreCounterTrendFakPremarketEntry(variant) ||
            IsDiffCounterTrendFakPremarketEntry(variant) ||
            IsDiffShiftProgressPremarketEntry(variant) ||
            IsDiffLimitProgressPremarketEntry(variant) ||
            IsDiffReferenceAveragePremarketEntry(variant);
    }

    private static bool IsPreOpenFixedDirectionSellExit(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell;
    }

    private static bool IsPreviousScoreCounterTrendOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrend;
    }

    private static bool UsesPreviousScoreCounterTrendSignal(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrend or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFak or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarket or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakRevert or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert;
    }

    private static bool IsPreviousScoreCounterTrendFakEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFak;
    }

    private static bool IsPreviousScoreCounterTrendFakPremarketEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarket or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert;
    }

    private static bool IsPreviousScoreCounterTrendFakRevertEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakRevert or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert;
    }

    private static bool IsPreviousScoreCounterTrendFakStatsEntry(BtcUpDown5mStrategyVariant variant)
    {
        return IsPreviousScoreCounterTrendFakEntry(variant) ||
            IsPreviousScoreCounterTrendFakPremarketEntry(variant) ||
            IsPreviousScoreCounterTrendFakRevertEntry(variant);
    }

    private static string GetPreviousScoreCounterTrendReasonPrefix(BtcUpDown5mStrategyVariant variant)
    {
        return IsBtcReferenceVariant(variant)
            ? "btc_previous_score"
            : "crypto_previous_score";
    }

    private static bool IsDiffCounterTrendOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.DiffCounterTrend or
            BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend or
            BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend or
            BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket or
            BtcUpDown5mStrategyBehavior.DiffProgress or
            BtcUpDown5mStrategyBehavior.DiffShiftProgress or
            BtcUpDown5mStrategyBehavior.DiffLimitProgressPremarket or
            BtcUpDown5mStrategyBehavior.DiffRealLimitProgressPremarket or
            BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket or
            BtcUpDown5mStrategyBehavior.DiffConfirmedAveragePremarket;
    }

    private static bool IsSimpleFixedOutcomeInstantEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.SimpleFixedOutcomeInstant;
    }

    private static bool IsFixedOutcomePreviousResultBpsInstantEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant;
    }

    private static bool IsFixedOutcomePreviousResultBpsFakEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFak or
            BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket;
    }

    private static bool IsDiffCounterTrendFakPremarketEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket;
    }

    private static bool IsDiffProgressEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.DiffProgress;
    }

    private static bool IsDiffShiftProgressEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.DiffShiftProgress;
    }

    private static bool IsDiffLimitProgressPremarketEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.DiffLimitProgressPremarket or
            BtcUpDown5mStrategyBehavior.DiffRealLimitProgressPremarket;
    }

    private static bool IsDiffReferenceAveragePremarketEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket or
            BtcUpDown5mStrategyBehavior.DiffConfirmedAveragePremarket;
    }

    private static bool IsPersistentDiffProgressStateEntry(BtcUpDown5mStrategyVariant variant)
    {
        return IsDiffShiftProgressEntry(variant) ||
            IsDiffLimitProgressPremarketEntry(variant);
    }

    private static bool IsDiffShiftProgressPremarketEntry(BtcUpDown5mStrategyVariant variant)
    {
        return IsDiffShiftProgressEntry(variant) &&
            variant.EntryDelaySeconds < 0 &&
            variant.DecisionDepth > 0;
    }

    private static bool IsFakStatsProbeEntry(BtcUpDown5mStrategyVariant variant)
    {
        return IsFixedOutcomePreviousResultBpsFakEntry(variant) ||
            IsReferenceAverageBpsFakPremarketEntry(variant) ||
            IsAbsoluteBpsFakPremarketEntry(variant) ||
            IsFuturesBasisBpsFakPremarketEntry(variant) ||
            IsPreviousScoreCounterTrendFakStatsEntry(variant) ||
            IsDiffCounterTrendFakPremarketEntry(variant) ||
            IsDiffReferenceAveragePremarketEntry(variant);
    }

    private static bool IsFakOrderEntry(BtcUpDown5mStrategyVariant variant)
    {
        return IsFakStatsProbeEntry(variant) ||
            IsInstantOpeningLimitEntry(variant);
    }

    private static string GetPaperLiveShadowLiveOrderType(BtcUpDown5mStrategyVariant variant)
    {
        return FakOrderType;
    }

    private static bool IsAdjustedDiffCounterTrendOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend;
    }

    private static bool IsShiftDiffCounterTrendOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend;
    }

    private static string GetDiffCounterStateKey(BtcUpDown5mStrategyVariant variant)
    {
        return IsShiftDiffCounterTrendOpeningLimitEntry(variant)
            ? variant.Code
            : GetReferenceAssetSymbol(variant);
    }

    private static int GetShiftDiffCount(BtcUpDown5mStrategyVariant variant)
    {
        return IsShiftDiffCounterTrendOpeningLimitEntry(variant)
            ? Math.Max(1, variant.ShiftDiffCount)
            : 0;
    }

    private static bool IsBinanceStartRelativeOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.BinanceStartRelative or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThresholdInstant or
            BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThreshold or
            BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThresholdInstant or
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
            IsPreviousScoreCounterTrendOpeningLimitEntry(variant) ||
            variant.Behavior == BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThreshold ||
            variant.Behavior is BtcUpDown5mStrategyBehavior.BinanceStartRelative or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold or
            BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThreshold or
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeDelayed;
    }

    private static bool IsInstantOpeningLimitEntry(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThresholdInstant or
            BtcUpDown5mStrategyBehavior.MiddleReferenceInstant or
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant or
            BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThresholdInstant or
            BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant or
            BtcUpDown5mStrategyBehavior.SimpleFixedOutcomeInstant or
            BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThresholdInstant or
            BtcUpDown5mStrategyBehavior.DiffCounterTrend or
            BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend or
            BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend or
            BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket or
            BtcUpDown5mStrategyBehavior.DiffProgress or
            BtcUpDown5mStrategyBehavior.DiffShiftProgress or
            BtcUpDown5mStrategyBehavior.DiffLimitProgressPremarket or
            BtcUpDown5mStrategyBehavior.DiffRealLimitProgressPremarket;
    }

    internal static decimal GetEffectiveInstantOpeningLimitMaxPrice(
        BtcUpDown5mStrategyVariant variant,
        BtcUpDown5mStrategyOptions options)
    {
        if (IsSimpleFixedOutcomeInstantEntry(variant))
        {
            return 0.50m;
        }

        if (IsFixedOutcomePreviousResultBpsInstantEntry(variant))
        {
            return UncappedInstantOpeningLimitMaxPrice;
        }

        return IsDiffCounterTrendOpeningLimitEntry(variant)
            ? options.DiffCounterInstantMaxPrice
            : options.InstantOpeningLimitMaxPrice;
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
        if (variant.Behavior is not BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold and
            not BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThresholdInstant and
            not BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThreshold and
            not BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThresholdInstant)
        {
            return null;
        }

        if (variant.DecisionThresholdBps is > 0m)
        {
            return variant.DecisionThresholdBps;
        }

        return variant.DecisionDepth > 0 ? variant.DecisionDepth : null;
    }

    private static decimal? GetSkipPreviousResultMinMoveBps(BtcUpDown5mStrategyVariant variant)
    {
        if (!UsesPreviousResultBpsThresholdMoveSignal(variant))
        {
            return null;
        }

        if (variant.DecisionThresholdBps is > 0m)
        {
            return variant.DecisionThresholdBps;
        }

        return variant.DecisionDepth > 0 ? variant.DecisionDepth : null;
    }

    private static decimal GetReferenceAverageMinMoveBps(BtcUpDown5mStrategyVariant variant)
    {
        if (variant.DecisionThresholdBps is > 0m)
        {
            return variant.DecisionThresholdBps.Value;
        }

        return Math.Max(0m, variant.DecisionDepth);
    }

    private static decimal GetFuturesBasisMinMoveBps(BtcUpDown5mStrategyVariant variant)
    {
        if (variant.DecisionThresholdBps is > 0m)
        {
            return variant.DecisionThresholdBps.Value;
        }

        return Math.Max(0m, variant.DecisionDepth);
    }

    private static BtcPriceDirection? GetReferenceAverageTriggerDirection(BtcUpDown5mStrategyVariant variant)
    {
        if (!IsReferenceAverageBpsFakPremarketEntry(variant))
        {
            return null;
        }

        return variant.DiffCounterTriggerOutcome switch
        {
            BtcUpDownFixedOutcome.Up => BtcPriceDirection.Up,
            BtcUpDownFixedOutcome.Down => BtcPriceDirection.Down,
            _ => null
        };
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
            (UsesPreviousScoreCounterTrendSignal(variant) &&
                !IsPreviousScoreCounterTrendFakPremarketEntry(variant)) ||
            IsDiffCounterTrendOpeningLimitEntry(variant) ||
            IsPreOpenEntryWindowStillOpen(variant, marketStartUtc, nowUtc);
    }

    private static bool IsPreOpenEntryWindowStillOpen(
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset? marketStartUtc,
        DateTimeOffset nowUtc)
    {
        return IsPreOpenTimedOpeningLimitEntry(variant) &&
            marketStartUtc is { } startUtc &&
            nowUtc < startUtc;
    }

    private static bool IsPreOpenEntryWindowElapsed(
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset? marketStartUtc,
        DateTimeOffset nowUtc)
    {
        return IsPreOpenTimedOpeningLimitEntry(variant) &&
            marketStartUtc is { } startUtc &&
            nowUtc >= startUtc;
    }

    private static bool ShouldRunPaperLiveShadowTest(StrategyRuntimeSettings settings)
    {
        return settings.EffectiveLiveStakes;
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
        BtcCurrentPriceLookupCache middleReferenceCurrentPrices,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<BtcPreviousMarketMoveSignal>>> skipBpsStreakMoveSignalTasks,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<DiffReferenceAverageMarketResultsLookup>>> diffReferenceAverageResultTasks,
        CancellationToken cancellationToken)
    {
        return variant.Behavior switch
        {
            BtcUpDown5mStrategyBehavior.MiddleReference or
                BtcUpDown5mStrategyBehavior.MiddleReferenceRevert or
                BtcUpDown5mStrategyBehavior.MiddleReferenceInstant or
                BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant => await GetMiddleReferenceEntryDecisionAsync(
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
            BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThreshold or
                BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThresholdInstant or
                BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant or
                BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFak or
                BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket => await GetSkipPreviousResultBpsThresholdEntryDecisionAsync(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    skipBpsStreakMoveSignalTasks,
                    cancellationToken),
            BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket => await GetReferenceAverageBpsThresholdEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                middleReferenceCurrentPrices,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.FilteredReferenceAverageBpsThresholdFakPremarket => await GetReferenceAverageBpsThresholdEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                middleReferenceCurrentPrices,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.AbsoluteBpsThresholdFakPremarket => await GetAbsoluteBpsThresholdEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                middleReferenceCurrentPrices,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarket or
                BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarketRevert => await GetFuturesBasisBpsThresholdEntryDecisionAsync(
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
            BtcUpDown5mStrategyBehavior.PreOpenFixedDirection or
                BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell or
                BtcUpDown5mStrategyBehavior.SimpleFixedOutcomeInstant => GetPreOpenFixedDirectionEntryDecision(
                market,
                variant,
                stakeUsd,
                nowUtc),
            BtcUpDown5mStrategyBehavior.BinanceStartRelative or
                BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice or
                BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold or
                BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThresholdInstant or
                BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThreshold or
                BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThresholdInstant or
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
                skipBpsStreakMoveSignalTasks,
                diffReferenceAverageResultTasks,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.DynamicMarkov => await GetDynamicMarkovEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrend or
                BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFak or
                BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarket or
                BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakRevert or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert => await GetPreviousScoreCounterTrendEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.DiffProgress => await GetDiffProgressEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.DiffShiftProgress => await GetDiffShiftProgressEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.DiffLimitProgressPremarket => await GetDiffLimitProgressPremarketEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                freezeCountersAtLimit: false,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.DiffRealLimitProgressPremarket => await GetDiffLimitProgressPremarketEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                freezeCountersAtLimit: true,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket => await GetDiffReferenceAveragePremarketEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                skipBpsStreakMoveSignalTasks,
                diffReferenceAverageResultTasks,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.BpsConfirmedAveragePremarket or
                BtcUpDown5mStrategyBehavior.DiffConfirmedAveragePremarket => await GetConfirmedAveragePremarketEntryDecisionAsync(
                market,
                variant,
                stakeUsd,
                nowUtc,
                middleReferenceCurrentPrices,
                skipBpsStreakMoveSignalTasks,
                diffReferenceAverageResultTasks,
                cancellationToken),
            BtcUpDown5mStrategyBehavior.DiffCounterTrend or
                BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend or
                BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend or
                BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket => await GetDiffCounterTrendEntryDecisionAsync(
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
                skipBpsStreakMoveSignalTasks,
                diffReferenceAverageResultTasks,
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
        decimal? fallbackMinOrderSize,
        decimal stakeMultiplier,
        DateTimeOffset nowUtc,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>> orderBookFetchTasks,
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

        if (IsFakStatsProbeEntry(variant))
        {
            return await GetFakStatsProbeOpeningLimitPriceAsync(
                assetId,
                rawDecisionJson,
                nowUtc,
                orderBookFetchTasks,
                cancellationToken);
        }

        if (IsInstantOpeningLimitEntry(variant))
        {
            return await GetInstantOpeningLimitPriceAsync(
                variant,
                assetId,
                rawDecisionJson,
                fallbackMinOrderSize,
                stakeMultiplier,
                nowUtc,
                orderBookFetchTasks,
                cancellationToken);
        }

        if (IsPreviousScoreCounterTrendOpeningLimitEntry(variant))
        {
            var fixedLimitPrice = RoundDownToTick(
                Math.Min(1m, GetFixedDirectionLimitPrice(variant)),
                options.OpeningLimitPriceTickSize);
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

    private async Task<BtcOpeningLimitPriceDecision> GetInstantOpeningLimitPriceAsync(
        BtcUpDown5mStrategyVariant variant,
        string assetId,
        string rawDecisionJson,
        decimal? fallbackMinOrderSize,
        decimal stakeMultiplier,
        DateTimeOffset nowUtc,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>> orderBookFetchTasks,
        CancellationToken cancellationToken)
    {
        var lookup = await GetFreshTakerOrderBookAsync(
            assetId,
            nowUtc,
            orderBookFetchTasks,
            cancellationToken);
        if (lookup.RejectionReason is not null || lookup.OrderBook is null)
        {
            var rejected = BtcInstantOpeningLimitPriceDecision.Reject(
                lookup.RejectionReason ?? SignalReasonCodes.MissingOrderBook,
                lookup.Source,
                lookup.Age,
                lookup.OrderBook);
            return BtcOpeningLimitPriceDecision.Reject(
                rejected.RejectionReason ?? "instant_opening_limit_orderbook_rejected",
                AttachInstantOpeningLimitPricingJson(rawDecisionJson, rejected));
        }

        var orderBook = ApplyFallbackMinOrderSize(lookup.OrderBook, fallbackMinOrderSize);
        var maxAllowedPrice = GetEffectiveInstantOpeningLimitMaxPrice(variant, options);
        var pricing = CreateInstantOpeningLimitPriceDecision(
            orderBook,
            lookup.Source,
            lookup.Age,
            maxAllowedPrice,
            fallbackMinOrderSize: null,
            stakeMultiplier,
            allowRestingAtMaxWhenAboveMax: false,
            allowPartialFill: true);
        var pricingJson = AttachInstantOpeningLimitPricingJson(rawDecisionJson, pricing);
        return pricing.Available
            ? BtcOpeningLimitPriceDecision.Enter(
                pricing.LimitPrice,
                pricingJson,
                lookup with { OrderBook = orderBook })
            : BtcOpeningLimitPriceDecision.Reject(
                pricing.RejectionReason ?? "instant_opening_limit_price_rejected",
                pricingJson);
    }

    private async Task<BtcOpeningLimitPriceDecision> GetFakStatsProbeOpeningLimitPriceAsync(
        string assetId,
        string rawDecisionJson,
        DateTimeOffset nowUtc,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>> orderBookFetchTasks,
        CancellationToken cancellationToken)
    {
        var lookup = await GetFreshTakerOrderBookAsync(
            assetId,
            nowUtc,
            orderBookFetchTasks,
            cancellationToken);
        if (lookup.RejectionReason is not null || lookup.OrderBook is null)
        {
            var reason = lookup.RejectionReason ?? SignalReasonCodes.MissingOrderBook;
            return BtcOpeningLimitPriceDecision.Reject(
                reason,
                AttachFakStatsProbeOpeningLimitPricingJson(
                    rawDecisionJson,
                    lookup,
                    TickSize: null,
                    WorstPrice: null,
                    ExecutableAskShares: null,
                    ExecutableAskVwap: null,
                    RejectionReason: reason));
        }

        var orderBook = lookup.OrderBook;
        var tickSize = orderBook.TickSize is > 0m
            ? orderBook.TickSize.Value
            : options.OpeningLimitPriceTickSize;
        if (tickSize <= 0m)
        {
            return BtcOpeningLimitPriceDecision.Reject(
                "invalid_limit_price_tick_size",
                AttachFakStatsProbeOpeningLimitPricingJson(
                    rawDecisionJson,
                    lookup,
                    tickSize,
                    WorstPrice: null,
                    ExecutableAskShares: null,
                    ExecutableAskVwap: null,
                    RejectionReason: "invalid_limit_price_tick_size"));
        }

        var worstPrice = ResolveFakGuaranteedWorstPrice(orderBook);
        if (worstPrice <= 0m || worstPrice >= 1m)
        {
            return BtcOpeningLimitPriceDecision.Reject(
                "fak_worst_price_out_of_range",
                AttachFakStatsProbeOpeningLimitPricingJson(
                    rawDecisionJson,
                    lookup,
                    tickSize,
                    worstPrice,
                    ExecutableAskShares: null,
                    ExecutableAskVwap: null,
                    RejectionReason: "fak_worst_price_out_of_range"));
        }

        var executableAsk = GetBuyExecutableAskSummary(orderBook, worstPrice, targetSizeShares: 0m);
        return BtcOpeningLimitPriceDecision.Enter(
            worstPrice,
            AttachFakStatsProbeOpeningLimitPricingJson(
                rawDecisionJson,
                lookup,
                tickSize,
                worstPrice,
                executableAsk.Shares,
                executableAsk.Vwap,
                RejectionReason: null),
            lookup);
    }

    private BtcInstantOpeningLimitPriceDecision CreateInstantOpeningLimitPriceDecision(
        OrderBookSnapshot orderBook,
        string source,
        TimeSpan? age,
        decimal maxAllowedPrice,
        decimal? fallbackMinOrderSize,
        decimal stakeMultiplier,
        bool allowRestingAtMaxWhenAboveMax,
        bool allowPartialFill = false)
    {
        if (stakeMultiplier <= 0m)
        {
            return BtcInstantOpeningLimitPriceDecision.Reject(
                "invalid_stake_multiplier",
                source,
                age,
                orderBook);
        }

        var tickSize = orderBook.TickSize is > 0m
            ? orderBook.TickSize.Value
            : options.OpeningLimitPriceTickSize;
        if (tickSize <= 0m)
        {
            return BtcInstantOpeningLimitPriceDecision.Reject(
                "invalid_limit_price_tick_size",
                source,
                age,
                orderBook,
                TickSize: tickSize);
        }

        var executableAsks = orderBook.Asks
            .Where(level => level is { Price: > 0m and < 1m, Size: > 0m })
            .OrderBy(level => level.Price)
            .ToArray();
        if (executableAsks.Length == 0)
        {
            return BtcInstantOpeningLimitPriceDecision.Reject(
                SignalReasonCodes.MissingBestAsk,
                source,
                age,
                orderBook,
                TickSize: tickSize);
        }

        BtcInstantOpeningLimitPriceDecision? lastCandidate = null;
        foreach (var ask in executableAsks)
        {
            var limitPrice = RoundUpToTick(ask.Price, tickSize);
            if (limitPrice <= 0m || limitPrice >= 1m)
            {
                lastCandidate = BtcInstantOpeningLimitPriceDecision.Reject(
                    "instant_opening_limit_price_out_of_range",
                    source,
                    age,
                    orderBook,
                    RawLimitPrice: ask.Price,
                    TickSize: tickSize,
                    LimitPrice: limitPrice);
                continue;
            }

            if (limitPrice > maxAllowedPrice)
            {
                if (allowPartialFill && lastCandidate?.ExecutableAskShares is > 0m)
                {
                    return lastCandidate;
                }

                if (allowRestingAtMaxWhenAboveMax)
                {
                    var restingLimitPrice = RoundDownToTick(maxAllowedPrice, tickSize);
                    if (restingLimitPrice <= 0m || restingLimitPrice >= 1m)
                    {
                        return BtcInstantOpeningLimitPriceDecision.Reject(
                            "instant_opening_limit_resting_price_out_of_range",
                            source,
                            age,
                            orderBook,
                            RawLimitPrice: ask.Price,
                            TickSize: tickSize,
                            LimitPrice: restingLimitPrice,
                            MaxAllowedPrice: maxAllowedPrice);
                    }

                    var restingSizing = CreateOpeningLimitTargetSizingEstimate(
                        orderBook.MinOrderSize ?? fallbackMinOrderSize,
                        restingLimitPrice,
                        stakeMultiplier,
                        source);
                    if (!restingSizing.Available)
                    {
                        return BtcInstantOpeningLimitPriceDecision.Reject(
                            restingSizing.RejectionReason ?? "instant_opening_limit_resting_target_size_rejected",
                            source,
                            age,
                            orderBook,
                            RawLimitPrice: ask.Price,
                            TickSize: tickSize,
                            LimitPrice: restingLimitPrice,
                            MaxAllowedPrice: maxAllowedPrice);
                    }

                    return BtcInstantOpeningLimitPriceDecision.Enter(
                        restingLimitPrice,
                        source,
                        age,
                        orderBook,
                        ask.Price,
                        tickSize,
                        maxAllowedPrice,
                        restingSizing.TargetNotionalUsd,
                        restingSizing.TargetSizeShares,
                        executableAskShares: 0m,
                        executableAskVwap: null,
                        levelsUsed: 0);
                }

                return BtcInstantOpeningLimitPriceDecision.Reject(
                    SignalReasonCodes.InstantPriceAboveMax,
                    source,
                    age,
                    orderBook,
                    RawLimitPrice: ask.Price,
                    TickSize: tickSize,
                    LimitPrice: limitPrice,
                    MaxAllowedPrice: maxAllowedPrice,
                    TargetNotionalUsd: lastCandidate?.TargetNotionalUsd,
                    TargetSizeShares: lastCandidate?.TargetSizeShares,
                    ExecutableAskShares: lastCandidate?.ExecutableAskShares,
                    ExecutableAskVwap: lastCandidate?.ExecutableAskVwap,
                    LevelsUsed: lastCandidate?.LevelsUsed ?? 0);
            }

            var sizing = CreateOpeningLimitTargetSizingEstimate(
                orderBook.MinOrderSize ?? fallbackMinOrderSize,
                limitPrice,
                stakeMultiplier,
                source);
            if (!sizing.Available)
            {
                return BtcInstantOpeningLimitPriceDecision.Reject(
                    sizing.RejectionReason ?? "instant_opening_limit_target_size_rejected",
                    source,
                    age,
                    orderBook,
                    RawLimitPrice: ask.Price,
                    TickSize: tickSize,
                    LimitPrice: limitPrice);
            }

            var immediateExecutableAsk = GetBuyExecutableAskSummary(orderBook, limitPrice, sizing.TargetSizeShares);
            var levelsUsed = orderBook.Asks
                .Count(level => level is { Price: > 0m, Size: > 0m } && level.Price <= limitPrice);
            lastCandidate = BtcInstantOpeningLimitPriceDecision.Enter(
                limitPrice,
                source,
                age,
                orderBook,
                ask.Price,
                tickSize,
                maxAllowedPrice,
                sizing.TargetNotionalUsd,
                sizing.TargetSizeShares,
                immediateExecutableAsk.Shares,
                immediateExecutableAsk.Vwap,
                levelsUsed);
            if (immediateExecutableAsk.Shares + 0.00000001m >= sizing.TargetSizeShares)
            {
                return lastCandidate;
            }
        }

        if (allowPartialFill && lastCandidate?.ExecutableAskShares is > 0m)
        {
            return lastCandidate;
        }

        return BtcInstantOpeningLimitPriceDecision.Reject(
            "instant_opening_limit_insufficient_executable_ask_depth",
            source,
            age,
            orderBook,
            RawLimitPrice: lastCandidate?.RawLimitPrice,
            TickSize: lastCandidate?.TickSize,
            LimitPrice: lastCandidate?.LimitPrice,
            MaxAllowedPrice: maxAllowedPrice,
            TargetNotionalUsd: lastCandidate?.TargetNotionalUsd,
            TargetSizeShares: lastCandidate?.TargetSizeShares,
            ExecutableAskShares: lastCandidate?.ExecutableAskShares,
            ExecutableAskVwap: lastCandidate?.ExecutableAskVwap,
            LevelsUsed: lastCandidate?.LevelsUsed ?? 0);
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

    private async Task<BtcOpeningLimitDecision> GetPreviousScoreCounterTrendEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var reasonPrefix = GetPreviousScoreCounterTrendReasonPrefix(variant);
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        if (marketStartUtc is null)
        {
            var reason = reasonPrefix + "_current_market_start_missing";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildPreviousScoreCounterTrendRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    signal: null,
                    selectedOutcome: null,
                    reason: reason));
        }

        BtcPreviousScoreCounterTrendSignal signal;
        if (IsPreviousScoreCounterTrendFakPremarketEntry(variant))
        {
            signal = await CalculatePreviousScoreCounterTrendPremarketSignalAsync(
                variant,
                reasonPrefix,
                marketStartUtc.Value,
                useCounterTrend: !IsPreviousScoreCounterTrendFakRevertEntry(variant),
                cancellationToken);
        }
        else
        {
            var previousMarketStartUtc = marketStartUtc.Value.AddMinutes(-5);
            var ticks = await GetReferenceOddsTicksForMarketStartAsync(
                variant,
                previousMarketStartUtc,
                limit: 1_000,
                cancellationToken);
            if (ticks.Count == 0)
            {
                var reason = reasonPrefix + "_samples_missing";
                var missingSignal = BtcPreviousScoreCounterTrendSignal.Reject(
                    reason,
                    PreviousMarketStartUtc: previousMarketStartUtc,
                    PreviousMarketEndUtc: marketStartUtc.Value);
                return BtcOpeningLimitDecision.Reject(
                    reason,
                    BuildPreviousScoreCounterTrendRawDecisionJson(
                        market,
                        variant,
                        stakeUsd,
                        nowUtc,
                        missingSignal,
                        selectedOutcome: null,
                        reason: reason));
            }

            var previousTicks = SelectPreviousScoreCounterTrendTickGroup(ticks, marketStartUtc.Value);
            signal = CalculatePreviousScoreCounterTrendSignal(
                reasonPrefix,
                previousTicks,
                previousMarketStartUtc,
                marketStartUtc.Value,
                useCounterTrend: !IsPreviousScoreCounterTrendFakRevertEntry(variant));
        }
        if (!signal.ShouldEnter || signal.SelectedDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                signal.RejectionReason ?? "btc_previous_score_countertrend_rejected",
                BuildPreviousScoreCounterTrendRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    signal,
                    selectedOutcome: null,
                    signal.RejectionReason ?? "btc_previous_score_countertrend_rejected"));
        }

        var selectedOutcome = TrySelectOutcomeForDirection(market, signal.SelectedDirection.Value);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildPreviousScoreCounterTrendRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    signal,
                    selectedOutcome: null,
                    reason: "target_outcome_not_available"));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildPreviousScoreCounterTrendRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                signal,
                selectedOutcome,
                reason: null));
    }

    private async Task<BtcPreviousScoreCounterTrendSignal> CalculatePreviousScoreCounterTrendPremarketSignalAsync(
        BtcUpDown5mStrategyVariant variant,
        string reasonPrefix,
        DateTimeOffset targetMarketStartUtc,
        bool useCounterTrend,
        CancellationToken cancellationToken)
    {
        var intervalDuration = BtcUpDown5mMarketAnalyzer.GetIntervalDuration(variant.MarketInterval);
        var currentScoredMarketStartUtc = targetMarketStartUtc.Subtract(intervalDuration);
        var previousScoredMarketStartUtc = currentScoredMarketStartUtc.Subtract(intervalDuration);
        var scoreWindowStartUtc = currentScoredMarketStartUtc.Subtract(PreviousScoreCounterTrendPremarketCarryoverWindow);
        var scoreWindowEndUtc = targetMarketStartUtc.AddSeconds(variant.EntryDelaySeconds);
        var scoreWindowEndFallbackUtc = targetMarketStartUtc.AddSeconds(-PremarketPreviousResultDefaultSampleSecondsBeforeEnd);
        if (scoreWindowEndUtc <= currentScoredMarketStartUtc)
        {
            scoreWindowEndUtc = scoreWindowEndFallbackUtc;
        }

        var previousRawTicks = await GetReferenceOddsTicksForMarketStartAsync(
            variant,
            previousScoredMarketStartUtc,
            limit: 1_000,
            cancellationToken);
        var currentRawTicks = await GetReferenceOddsTicksForMarketStartAsync(
            variant,
            currentScoredMarketStartUtc,
            limit: 1_000,
            cancellationToken);
        var previousTicks = previousRawTicks.Count == 0
            ? []
            : SelectPreviousScoreCounterTrendTickGroup(previousRawTicks, currentScoredMarketStartUtc);
        var currentTicks = currentRawTicks.Count == 0
            ? []
            : SelectPreviousScoreCounterTrendTickGroup(currentRawTicks, targetMarketStartUtc);
        var previousWindowTicks = previousTicks
            .Where(tick => tick.SampledAtUtc >= scoreWindowStartUtc &&
                tick.SampledAtUtc <= currentScoredMarketStartUtc)
            .ToArray();
        var currentWindowTicks = currentTicks
            .Where(tick => tick.SampledAtUtc >= currentScoredMarketStartUtc &&
                tick.SampledAtUtc <= scoreWindowEndUtc)
            .ToArray();
        var combinedTicks = previousWindowTicks
            .Concat(currentWindowTicks)
            .OrderBy(tick => tick.SampledAtUtc)
            .ThenBy(tick => tick.CreatedAtUtc)
            .ToArray();
        var rawSampleCount = previousTicks.Count + currentTicks.Count;
        if (previousWindowTicks.Length == 0 || currentWindowTicks.Length == 0)
        {
            var reason = reasonPrefix + "_premarket_samples_missing";
            return BtcPreviousScoreCounterTrendSignal.Reject(
                reason,
                StartPriceUsd: combinedTicks.FirstOrDefault(tick => tick.BinancePriceUsd > 0m)?.BinancePriceUsd,
                RawSampleCount: rawSampleCount,
                ValidSampleCount: combinedTicks.Count(tick => tick.BinancePriceUsd > 0m),
                PreviousMarketId: combinedTicks.FirstOrDefault()?.MarketId,
                PreviousMarketSlug: combinedTicks.FirstOrDefault()?.MarketSlug,
                PreviousMarketStartUtc: scoreWindowStartUtc,
                PreviousMarketEndUtc: scoreWindowEndUtc);
        }

        var syntheticStartPrice = combinedTicks
            .Where(tick => tick.BinancePriceUsd > 0m)
            .OrderBy(tick => tick.SampledAtUtc)
            .ThenBy(tick => tick.CreatedAtUtc)
            .Select(tick => (decimal?)tick.BinancePriceUsd)
            .FirstOrDefault();
        return CalculatePreviousScoreCounterTrendSignal(
            reasonPrefix,
            combinedTicks,
            scoreWindowStartUtc,
            scoreWindowEndUtc,
            useCounterTrend: useCounterTrend,
            startPriceOverride: syntheticStartPrice,
            rawSampleCountOverride: rawSampleCount);
    }

    private async Task EnsureDiffCounterStatesInitializedAsync(
        IReadOnlyList<BtcUpDown5mStrategyVariant> variants,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var referenceMarketStartUtc = GetDiffCounterReferenceMarketStartUtc(nowUtc);
        var counterModes = variants
            .Where(variant => !IsPersistentDiffProgressStateEntry(variant))
            .Where(variant => !IsDiffReferenceAveragePremarketEntry(variant))
            .Select(variant => new
            {
                AssetSymbol = GetReferenceAssetSymbol(variant),
                ResetAtUtcDayStart = !IsAdjustedDiffCounterTrendOpeningLimitEntry(variant) &&
                    !IsShiftDiffCounterTrendOpeningLimitEntry(variant),
                StateKey = GetDiffCounterStateKey(variant),
                ShiftDiffCount = GetShiftDiffCount(variant),
                PersistSnapshot = variant.Behavior is BtcUpDown5mStrategyBehavior.DiffCounterTrend or
                    BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket or
                    BtcUpDown5mStrategyBehavior.DiffProgress
            })
            .DistinctBy(item => (
                item.AssetSymbol.ToUpperInvariant(),
                item.ResetAtUtcDayStart,
                item.StateKey,
                item.ShiftDiffCount))
            .ToArray();
        foreach (var counterMode in counterModes)
        {
            var snapshot = await GetDiffCounterStateAsync(
                counterMode.AssetSymbol,
                referenceMarketStartUtc,
                nowUtc,
                counterMode.ResetAtUtcDayStart,
                counterMode.StateKey,
                counterMode.ShiftDiffCount,
                cancellationToken);
            if (counterMode.PersistSnapshot)
            {
                await TryUpsertDiffCounterSnapshotAsync(snapshot, nowUtc, cancellationToken);
            }
        }
    }

    private async Task TryUpsertDiffCounterSnapshotAsync(
        DiffCounterSnapshot snapshot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.UpsertCryptoUpDown5mDiffSnapshotAsync(
                new CryptoUpDown5mDiffSnapshot(
                    Guid.NewGuid(),
                    snapshot.AssetSymbol,
                    snapshot.TargetMarketStartUtc,
                    nowUtc,
                    snapshot.CounterStartMarketStartUtc,
                    snapshot.LastIncludedMarketStartUtc,
                    snapshot.HighWaterMarketStartUtc,
                    snapshot.Initialized,
                    snapshot.UpCount,
                    snapshot.DownCount,
                    snapshot.DiffCount,
                    snapshot.Diff,
                    snapshot.ProcessedMarketCount,
                    snapshot.HistoryFetchFailedAtUtc,
                    snapshot.HistoryFetchRetryAfterUtc,
                    snapshot.HistoryFetchErrorMessage,
                    nowUtc,
                    nowUtc),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to persist Diff counter snapshot. Asset={AssetSymbol} MarketStartUtc={MarketStartUtc}",
                snapshot.AssetSymbol,
                snapshot.TargetMarketStartUtc);
            await TryRecordApiErrorAsync("UpsertDiffCounterSnapshot", ex.Message, cancellationToken);
        }
    }

    private async Task<BtcOpeningLimitDecision> GetDiffCounterTrendEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        if (marketStartUtc is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_counter_current_market_start_missing",
                BuildDiffCounterTrendRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot: null,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "diff_counter_current_market_start_missing"));
        }

        var selectedDirection = variant.FixedOutcome switch
        {
            BtcUpDownFixedOutcome.Up => BtcPriceDirection.Up,
            BtcUpDownFixedOutcome.Down => BtcPriceDirection.Down,
            _ => (BtcPriceDirection?)null
        };
        if (selectedDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_counter_fixed_outcome_not_configured",
                BuildDiffCounterTrendRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot: null,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason: "diff_counter_fixed_outcome_not_configured"));
        }

        var triggerDirection = ResolveDiffCounterTriggerDirection(variant);
        if (triggerDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_counter_trigger_outcome_not_configured",
                BuildDiffCounterTrendRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot: null,
                    triggerDirection: null,
                    selectedDirection: selectedDirection.Value,
                    selectedOutcome: null,
                    reason: "diff_counter_trigger_outcome_not_configured"));
        }

        var isAdjustedDiff = IsAdjustedDiffCounterTrendOpeningLimitEntry(variant);
        var isShiftDiff = IsShiftDiffCounterTrendOpeningLimitEntry(variant);
        var assetSymbol = GetReferenceAssetSymbol(variant);
        var snapshot = await GetDiffCounterStateAsync(
            assetSymbol,
            marketStartUtc.Value,
            nowUtc,
            resetAtUtcDayStart: !isAdjustedDiff && !isShiftDiff,
            stateKey: GetDiffCounterStateKey(variant),
            shiftDiffCount: GetShiftDiffCount(variant),
            cancellationToken);
        var historyUnavailableReason = GetDiffCounterHistoryUnavailableReason(snapshot, nowUtc);
        if (historyUnavailableReason is not null)
        {
            return BtcOpeningLimitDecision.Reject(
                historyUnavailableReason,
                BuildDiffCounterTrendRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot,
                    triggerDirection.Value,
                    selectedDirection.Value,
                    selectedOutcome: null,
                    reason: historyUnavailableReason));
        }

        var threshold = Math.Max(1, variant.DecisionDepth);
        var effectiveDiff = GetDiffCounterEffectiveDiff(snapshot, triggerDirection.Value, isAdjustedDiff)
            .GetValueOrDefault(decimal.MinValue);
        if (effectiveDiff < threshold)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_counter_threshold_not_reached",
                BuildDiffCounterTrendRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot,
                    triggerDirection.Value,
                    selectedDirection.Value,
                    selectedOutcome: null,
                    reason: "diff_counter_threshold_not_reached"));
        }

        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection.Value);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildDiffCounterTrendRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot,
                    triggerDirection.Value,
                    selectedDirection.Value,
                    selectedOutcome: null,
                    reason: "target_outcome_not_available"));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildDiffCounterTrendRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                snapshot,
                triggerDirection.Value,
                selectedDirection.Value,
                selectedOutcome,
                reason: null));
    }

    private async Task<BtcOpeningLimitDecision> GetDiffProgressEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal baseStakeUsd,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        if (marketStartUtc is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_progress_current_market_start_missing",
                BuildDiffProgressRawDecisionJson(
                    market,
                    variant,
                    baseStakeUsd,
                    nowUtc,
                    snapshot: null,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    modeBefore: DiffProgressMode.Waiting,
                    modeAfter: DiffProgressMode.Waiting,
                    currentDayCounterStartUtc: null,
                    resetPostponed: false,
                    progressStakeMultiplier: null,
                    progressStakeUsd: null,
                    reason: "diff_progress_current_market_start_missing"));
        }

        var selectedDirection = variant.FixedOutcome switch
        {
            BtcUpDownFixedOutcome.Up => BtcPriceDirection.Up,
            BtcUpDownFixedOutcome.Down => BtcPriceDirection.Down,
            _ => (BtcPriceDirection?)null
        };
        if (selectedDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_progress_fixed_outcome_not_configured",
                BuildDiffProgressRawDecisionJson(
                    market,
                    variant,
                    baseStakeUsd,
                    nowUtc,
                    snapshot: null,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    modeBefore: DiffProgressMode.Waiting,
                    modeAfter: DiffProgressMode.Waiting,
                    currentDayCounterStartUtc: null,
                    resetPostponed: false,
                    progressStakeMultiplier: null,
                    progressStakeUsd: null,
                    reason: "diff_progress_fixed_outcome_not_configured"));
        }

        var triggerDirection = ResolveDiffCounterTriggerDirection(variant);
        if (triggerDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_progress_trigger_outcome_not_configured",
                BuildDiffProgressRawDecisionJson(
                    market,
                    variant,
                    baseStakeUsd,
                    nowUtc,
                    snapshot: null,
                    triggerDirection: null,
                    selectedDirection: selectedDirection.Value,
                    selectedOutcome: null,
                    modeBefore: DiffProgressMode.Waiting,
                    modeAfter: DiffProgressMode.Waiting,
                    currentDayCounterStartUtc: null,
                    resetPostponed: false,
                    progressStakeMultiplier: null,
                    progressStakeUsd: null,
                    reason: "diff_progress_trigger_outcome_not_configured"));
        }

        var threshold = Math.Max(1, variant.DecisionDepth);
        var currentDayCounterStartUtc = GetDiffCounterUtcDayStartMarketStartUtc(marketStartUtc.Value);
        DiffProgressMode modeBefore;
        lock (diffProgressStateSync)
        {
            if (!diffProgressStates.TryGetValue(variant.Code, out var runtimeState))
            {
                runtimeState = new DiffProgressRuntimeState(currentDayCounterStartUtc, nowUtc);
                diffProgressStates[variant.Code] = runtimeState;
            }

            if (runtimeState.CounterStartMarketStartUtc < currentDayCounterStartUtc)
            {
                runtimeState.ResetCounter(currentDayCounterStartUtc, nowUtc);
            }

            modeBefore = runtimeState.Mode;
        }

        const bool resetPostponed = false;
        var assetSymbol = GetReferenceAssetSymbol(variant);
        var snapshot = await GetDiffCounterStateAsync(
            assetSymbol,
            marketStartUtc.Value,
            nowUtc,
            resetAtUtcDayStart: true,
            stateKey: GetDiffCounterStateKey(variant),
            shiftDiffCount: 0,
            cancellationToken);
        var historyUnavailableReason = GetDiffCounterHistoryUnavailableReason(snapshot, nowUtc);
        if (historyUnavailableReason is not null)
        {
            return BtcOpeningLimitDecision.Reject(
                historyUnavailableReason,
                BuildDiffProgressRawDecisionJson(
                    market,
                    variant,
                    baseStakeUsd,
                    nowUtc,
                    snapshot,
                    triggerDirection.Value,
                    selectedDirection.Value,
                    selectedOutcome: null,
                    modeBefore: modeBefore,
                    modeAfter: modeBefore,
                    currentDayCounterStartUtc,
                    resetPostponed,
                    progressStakeMultiplier: null,
                    progressStakeUsd: null,
                    reason: historyUnavailableReason));
        }

        var effectiveDiff = GetDiffCounterEffectiveDiff(snapshot, triggerDirection.Value, useAdjustedDiff: false)
            .GetValueOrDefault(decimal.MinValue);
        var modeAfter = modeBefore;
        if (modeBefore == DiffProgressMode.Waiting && effectiveDiff <= threshold)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_progress_waiting_threshold_not_reached",
                BuildDiffProgressRawDecisionJson(
                    market,
                    variant,
                    baseStakeUsd,
                    nowUtc,
                    snapshot,
                    triggerDirection.Value,
                    selectedDirection.Value,
                    selectedOutcome: null,
                    modeBefore: modeBefore,
                    modeAfter: modeAfter,
                    currentDayCounterStartUtc,
                    resetPostponed,
                    progressStakeMultiplier: null,
                    progressStakeUsd: null,
                    reason: "diff_progress_waiting_threshold_not_reached"));
        }

        if (modeBefore == DiffProgressMode.Waiting)
        {
            modeAfter = DiffProgressMode.Betting;
            lock (diffProgressStateSync)
            {
                if (diffProgressStates.TryGetValue(variant.Code, out var runtimeState))
                {
                    runtimeState.EnterBetting(snapshot.CounterStartMarketStartUtc ?? currentDayCounterStartUtc, nowUtc);
                }
            }
        }

        if (effectiveDiff <= threshold)
        {
            modeAfter = DiffProgressMode.Waiting;
            lock (diffProgressStateSync)
            {
                if (diffProgressStates.TryGetValue(variant.Code, out var runtimeState))
                {
                    runtimeState.ExitToWaiting(currentDayCounterStartUtc, nowUtc);
                }
            }

            return BtcOpeningLimitDecision.Reject(
                "diff_progress_returned_to_threshold",
                BuildDiffProgressRawDecisionJson(
                    market,
                    variant,
                    baseStakeUsd,
                    nowUtc,
                    snapshot,
                    triggerDirection.Value,
                    selectedDirection.Value,
                    selectedOutcome: null,
                    modeBefore: modeBefore,
                    modeAfter: modeAfter,
                    currentDayCounterStartUtc,
                    resetPostponed,
                    progressStakeMultiplier: null,
                    progressStakeUsd: null,
                    reason: "diff_progress_returned_to_threshold"));
        }

        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection.Value);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildDiffProgressRawDecisionJson(
                    market,
                    variant,
                    baseStakeUsd,
                    nowUtc,
                    snapshot,
                    triggerDirection.Value,
                    selectedDirection.Value,
                    selectedOutcome: null,
                    modeBefore: modeBefore,
                    modeAfter: modeAfter,
                    currentDayCounterStartUtc,
                    resetPostponed,
                    progressStakeMultiplier: null,
                    progressStakeUsd: null,
                    reason: "target_outcome_not_available"));
        }

        var progressStakeMultiplier = Math.Min(effectiveDiff - threshold, DiffProgressMaxStakeMultiplier);
        var progressStakeUsd = baseStakeUsd * progressStakeMultiplier;
        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildDiffProgressRawDecisionJson(
                market,
                variant,
                baseStakeUsd,
                nowUtc,
                snapshot,
                triggerDirection.Value,
                selectedDirection.Value,
                selectedOutcome,
                modeBefore,
                modeAfter,
                currentDayCounterStartUtc,
                resetPostponed,
                progressStakeMultiplier,
                progressStakeUsd,
                reason: null),
            stakeUsdOverride: progressStakeUsd);
    }

    private async Task<BtcOpeningLimitDecision> GetDiffShiftProgressEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal unitStakeUsd,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (IsDiffShiftProgressPremarketEntry(variant))
        {
            return await GetDiffShiftProgressPremarketEntryDecisionAsync(
                market,
                variant,
                unitStakeUsd,
                nowUtc,
                cancellationToken);
        }

        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        if (marketStartUtc is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_shift_progress_current_market_start_missing",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state: null,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    targetMarketStartUtc: null,
                    resultFetchStartUtc: null,
                    appliedResultCount: 0,
                    pendingSumDeltaUsd: null,
                    shiftCount: 0,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: "diff_shift_progress_current_market_start_missing"));
        }

        if (unitStakeUsd <= 0m)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_shift_progress_unit_stake_non_positive",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state: null,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    targetMarketStartUtc: null,
                    resultFetchStartUtc: null,
                    appliedResultCount: 0,
                    pendingSumDeltaUsd: null,
                    shiftCount: 0,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: "diff_shift_progress_unit_stake_non_positive"));
        }

        var selectedDirection = variant.FixedOutcome switch
        {
            BtcUpDownFixedOutcome.Up => BtcPriceDirection.Up,
            BtcUpDownFixedOutcome.Down => BtcPriceDirection.Down,
            _ => (BtcPriceDirection?)null
        };
        if (selectedDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_shift_progress_fixed_outcome_not_configured",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state: null,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    targetMarketStartUtc: null,
                    resultFetchStartUtc: null,
                    appliedResultCount: 0,
                    pendingSumDeltaUsd: null,
                    shiftCount: 0,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: "diff_shift_progress_fixed_outcome_not_configured"));
        }

        var triggerDirection = ResolveDiffCounterTriggerDirection(variant);
        if (triggerDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_shift_progress_trigger_outcome_not_configured",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state: null,
                    triggerDirection: null,
                    selectedDirection: selectedDirection.Value,
                    selectedOutcome: null,
                    targetMarketStartUtc: null,
                    resultFetchStartUtc: null,
                    appliedResultCount: 0,
                    pendingSumDeltaUsd: null,
                    shiftCount: 0,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: "diff_shift_progress_trigger_outcome_not_configured"));
        }

        var assetSymbol = GetReferenceAssetSymbol(variant);
        var triggerOutcome = triggerDirection == BtcPriceDirection.Up ? "Up" : "Down";
        var requestedTargetMarketStartUtc = marketStartUtc.Value.AddMinutes(-5);
        var latestTargetMarketStartUtc = GetDiffCounterLatestWebSocketTargetMarketStartUtc(nowUtc);
        var resolvedTargetMarketStartUtc = requestedTargetMarketStartUtc <= latestTargetMarketStartUtc
            ? requestedTargetMarketStartUtc
            : latestTargetMarketStartUtc;

        var state = await GetOrCreateDiffShiftProgressStateAsync(
            variant,
            assetSymbol,
            triggerOutcome,
            nowUtc,
            cancellationToken);

        var resultFetchStartUtc = GetDiffShiftProgressFetchStartUtc(state, marketStartUtc.Value, resolvedTargetMarketStartUtc);
        IReadOnlyList<DiffCounterMarketResult> results = [];
        if (resultFetchStartUtc is { } fetchStartUtc && fetchStartUtc <= resolvedTargetMarketStartUtc)
        {
            try
            {
                results = await FetchDiffCounterMarketResultsAsync(
                    assetSymbol,
                    fetchStartUtc,
                    resolvedTargetMarketStartUtc,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "BTC Up or Down 5m Diff Shift Progress result fetch failed. Strategy={StrategyCode} Asset={AssetSymbol} StartUtc={StartUtc} EndUtc={EndUtc}",
                    variant.Code,
                    assetSymbol,
                    fetchStartUtc,
                    resolvedTargetMarketStartUtc);
                await TryRecordApiErrorAsync("GetDiffShiftProgressResults", ex.Message, cancellationToken);
                return BtcOpeningLimitDecision.Reject(
                    "diff_shift_progress_history_fetch_failed",
                    BuildDiffShiftProgressRawDecisionJson(
                        market,
                        variant,
                        unitStakeUsd,
                        nowUtc,
                        state,
                        triggerDirection.Value,
                        selectedDirection.Value,
                        selectedOutcome: null,
                        resolvedTargetMarketStartUtc,
                        resultFetchStartUtc,
                        appliedResultCount: 0,
                        pendingSumDeltaUsd: null,
                        shiftCount: 0,
                        stakeMultiplier: null,
                        stakeUsd: null,
                        reason: "diff_shift_progress_history_fetch_failed"));
            }
        }

        var applied = ApplyDiffShiftProgressResults(state, results);
        state = applied.State with { UpdatedAtUtc = nowUtc };
        var shift = ApplyDiffShiftProgressShift(state, triggerDirection.Value, unitStakeUsd);
        state = shift.State with { UpdatedAtUtc = nowUtc };

        var requiresTargetResult = resultFetchStartUtc is { } requiredStartUtc &&
            requiredStartUtc <= resolvedTargetMarketStartUtc &&
            (state.LastProcessedMarketStartUtc is null ||
                state.LastProcessedMarketStartUtc < resolvedTargetMarketStartUtc);
        if (requiresTargetResult)
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
            return BtcOpeningLimitDecision.Reject(
                "diff_shift_progress_previous_market_resolved_event_missing",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state,
                    triggerDirection.Value,
                    selectedDirection.Value,
                    selectedOutcome: null,
                    resolvedTargetMarketStartUtc,
                    resultFetchStartUtc,
                    applied.AppliedResultCount,
                    applied.PendingSumDeltaUsd,
                    shift.ShiftCount,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: "diff_shift_progress_previous_market_resolved_event_missing"));
        }

        if (state.PendingMarketStartUtc is { } pendingMarketStartUtc && pendingMarketStartUtc > resolvedTargetMarketStartUtc)
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
            return BtcOpeningLimitDecision.Reject(
                pendingMarketStartUtc == marketStartUtc.Value
                    ? "diff_shift_progress_current_market_already_pending"
                    : "diff_shift_progress_pending_bet_unresolved",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state,
                    triggerDirection.Value,
                    selectedDirection.Value,
                    selectedOutcome: null,
                    resolvedTargetMarketStartUtc,
                    resultFetchStartUtc,
                    applied.AppliedResultCount,
                    applied.PendingSumDeltaUsd,
                    shift.ShiftCount,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: pendingMarketStartUtc == marketStartUtc.Value
                        ? "diff_shift_progress_current_market_already_pending"
                        : "diff_shift_progress_pending_bet_unresolved"));
        }

        var effectiveDiff = GetDiffShiftProgressEffectiveDiff(state, triggerDirection.Value);
        if (effectiveDiff <= 0)
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
            return BtcOpeningLimitDecision.Reject(
                "diff_shift_progress_non_positive_diff",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state,
                    triggerDirection.Value,
                    selectedDirection.Value,
                    selectedOutcome: null,
                    resolvedTargetMarketStartUtc,
                    resultFetchStartUtc,
                    applied.AppliedResultCount,
                    applied.PendingSumDeltaUsd,
                    shift.ShiftCount,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: "diff_shift_progress_non_positive_diff"));
        }

        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection.Value);
        if (selectedOutcome is null)
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state,
                    triggerDirection.Value,
                    selectedDirection.Value,
                    selectedOutcome: null,
                    resolvedTargetMarketStartUtc,
                    resultFetchStartUtc,
                    applied.AppliedResultCount,
                    applied.PendingSumDeltaUsd,
                    shift.ShiftCount,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: "target_outcome_not_available"));
        }

        var stakeMultiplier = effectiveDiff + 1m;
        var stakeUsd = unitStakeUsd * stakeMultiplier;
        await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildDiffShiftProgressRawDecisionJson(
                market,
                variant,
                unitStakeUsd,
                nowUtc,
                state,
                triggerDirection.Value,
                selectedDirection.Value,
                selectedOutcome,
                resolvedTargetMarketStartUtc,
                resultFetchStartUtc,
                applied.AppliedResultCount,
                applied.PendingSumDeltaUsd,
                shift.ShiftCount,
                stakeMultiplier,
                stakeUsd,
                reason: null),
            stakeUsdOverride: stakeUsd,
            diffShiftProgressPendingBet: new DiffShiftProgressPendingBet(
                state,
                marketStartUtc.Value,
                selectedOutcome.Outcome,
                stakeUsd));
    }

    private async Task<BtcOpeningLimitDecision> GetDiffShiftProgressPremarketEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal unitStakeUsd,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var threshold = Math.Max(1, variant.DecisionDepth);
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        if (marketStartUtc is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_shift_progress_current_market_start_missing",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state: null,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    targetMarketStartUtc: null,
                    resultFetchStartUtc: null,
                    appliedResultCount: 0,
                    pendingSumDeltaUsd: null,
                    shiftCount: 0,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: "diff_shift_progress_current_market_start_missing",
                    threshold: threshold,
                    progressMode: "Premarket"));
        }

        if (unitStakeUsd <= 0m)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_shift_progress_unit_stake_non_positive",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state: null,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    targetMarketStartUtc: null,
                    resultFetchStartUtc: null,
                    appliedResultCount: 0,
                    pendingSumDeltaUsd: null,
                    shiftCount: 0,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: "diff_shift_progress_unit_stake_non_positive",
                    threshold: threshold,
                    progressMode: "Premarket"));
        }

        var assetSymbol = GetReferenceAssetSymbol(variant);
        var intervalDuration = BtcUpDown5mMarketAnalyzer.GetIntervalDuration(variant.MarketInterval);
        var previousMarketStartUtc = marketStartUtc.Value.Subtract(intervalDuration);
        var historicalTargetMarketStartUtc = previousMarketStartUtc.Subtract(intervalDuration);
        var premarketResultSource = GetPremarketPreviousResultSource(variant);
        var state = await GetOrCreateDiffShiftProgressStateAsync(
            variant,
            assetSymbol,
            "Up",
            nowUtc,
            cancellationToken);

        var resultFetchStartUtc = GetDiffShiftProgressFetchStartUtc(state, marketStartUtc.Value, historicalTargetMarketStartUtc);
        IReadOnlyList<DiffCounterMarketResult> results = [];
        if (resultFetchStartUtc is { } fetchStartUtc && fetchStartUtc <= historicalTargetMarketStartUtc)
        {
            try
            {
                results = await FetchDiffCounterMarketResultsAsync(
                    assetSymbol,
                    fetchStartUtc,
                    historicalTargetMarketStartUtc,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "BTC Up or Down 5m Diff Shift Progress Premarket result fetch failed. Strategy={StrategyCode} Asset={AssetSymbol} StartUtc={StartUtc} EndUtc={EndUtc}",
                    variant.Code,
                    assetSymbol,
                    fetchStartUtc,
                    historicalTargetMarketStartUtc);
                await TryRecordApiErrorAsync("GetDiffShiftProgressPremarketResults", ex.Message, cancellationToken);
                return BtcOpeningLimitDecision.Reject(
                    "diff_shift_progress_history_fetch_failed",
                    BuildDiffShiftProgressRawDecisionJson(
                        market,
                        variant,
                        unitStakeUsd,
                        nowUtc,
                        state,
                        triggerDirection: null,
                        selectedDirection: null,
                        selectedOutcome: null,
                        historicalTargetMarketStartUtc,
                        resultFetchStartUtc,
                        appliedResultCount: 0,
                        pendingSumDeltaUsd: null,
                        shiftCount: 0,
                        stakeMultiplier: null,
                        stakeUsd: null,
                        reason: "diff_shift_progress_history_fetch_failed",
                        threshold: threshold,
                        progressMode: GetDiffShiftProgressPremarketMode(state)));
            }
        }

        var applied = ApplyDiffShiftProgressResults(state, results);
        state = applied.State with { UpdatedAtUtc = nowUtc };
        var appliedResultCount = applied.AppliedResultCount;
        var pendingSumDeltaUsd = applied.PendingSumDeltaUsd;

        var requiresHistoricalTargetResult = resultFetchStartUtc is { } requiredStartUtc &&
            requiredStartUtc <= historicalTargetMarketStartUtc &&
            (state.LastProcessedMarketStartUtc is null ||
                state.LastProcessedMarketStartUtc < historicalTargetMarketStartUtc);
        if (requiresHistoricalTargetResult)
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
            return BtcOpeningLimitDecision.Reject(
                "diff_shift_progress_previous_market_resolved_event_missing",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    historicalTargetMarketStartUtc,
                    resultFetchStartUtc,
                    appliedResultCount,
                    pendingSumDeltaUsd,
                    shiftCount: 0,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: "diff_shift_progress_previous_market_resolved_event_missing",
                    threshold: threshold,
                    progressMode: GetDiffShiftProgressPremarketMode(state)));
        }

        BtcPreviousMarketMoveSignal? premarketSignal = null;
        string? premarketResultOutcome = null;
        if (state.LastProcessedMarketStartUtc is null ||
            state.LastProcessedMarketStartUtc < previousMarketStartUtc ||
            state.PendingMarketStartUtc == previousMarketStartUtc)
        {
            premarketSignal = await CalculatePremarketPreviousResultBpsMoveSignalAsync(
                variant,
                marketStartUtc.Value,
                cancellationToken);
            premarketResultOutcome = NormalizeNullableUpDownOutcome(premarketSignal.StreakWinningOutcome);
            if (!premarketSignal.ShouldEnter || premarketResultOutcome is null)
            {
                await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
                var reason = premarketSignal.RejectionReason ?? "diff_shift_progress_premarket_result_missing";
                return BtcOpeningLimitDecision.Reject(
                    reason,
                    BuildDiffShiftProgressRawDecisionJson(
                        market,
                        variant,
                        unitStakeUsd,
                        nowUtc,
                        state,
                        triggerDirection: null,
                        selectedDirection: null,
                        selectedOutcome: null,
                        previousMarketStartUtc,
                        resultFetchStartUtc,
                        appliedResultCount,
                        pendingSumDeltaUsd,
                        shiftCount: 0,
                        stakeMultiplier: null,
                        stakeUsd: null,
                        reason,
                        threshold: threshold,
                        progressMode: GetDiffShiftProgressPremarketMode(state),
                        counterResultSource: premarketResultSource,
                        premarketResultOutcome: null,
                        premarketMoveBps: premarketSignal.MoveBps,
                        premarketSignalReason: reason));
            }

            var premarketResult = CreateDiffShiftProgressPremarketResult(
                premarketSignal,
                premarketResultOutcome,
                premarketResultSource);
            var premarketApplied = ApplyDiffShiftProgressResults(state, [premarketResult]);
            state = premarketApplied.State with { UpdatedAtUtc = nowUtc };
            appliedResultCount += premarketApplied.AppliedResultCount;
            pendingSumDeltaUsd = premarketApplied.PendingSumDeltaUsd ?? pendingSumDeltaUsd;
        }

        var shift = ApplyDiffShiftProgressPremarketDamping(state, threshold, unitStakeUsd);
        state = shift.State with { UpdatedAtUtc = nowUtc };

        if (state.PendingMarketStartUtc is { } pendingMarketStartUtc && pendingMarketStartUtc > previousMarketStartUtc)
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
            var reason = pendingMarketStartUtc == marketStartUtc.Value
                ? "diff_shift_progress_current_market_already_pending"
                : "diff_shift_progress_pending_bet_unresolved";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    previousMarketStartUtc,
                    resultFetchStartUtc,
                    appliedResultCount,
                    pendingSumDeltaUsd,
                    shift.ShiftCount,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason,
                    threshold: threshold,
                    progressMode: GetDiffShiftProgressPremarketMode(state),
                    counterResultSource: premarketResultSource,
                    premarketResultOutcome: premarketResultOutcome,
                    premarketMoveBps: premarketSignal?.MoveBps,
                    premarketSignalReason: premarketSignal?.RejectionReason));
        }

        var rawDiff = state.UpCount - state.DownCount;
        if (rawDiff == 0)
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
            return BtcOpeningLimitDecision.Reject(
                "diff_shift_progress_zero_diff",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    previousMarketStartUtc,
                    resultFetchStartUtc,
                    appliedResultCount,
                    pendingSumDeltaUsd,
                    shift.ShiftCount,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: "diff_shift_progress_zero_diff",
                    threshold: threshold,
                    progressMode: GetDiffShiftProgressPremarketMode(state),
                    counterResultSource: premarketResultSource,
                    premarketResultOutcome: premarketResultOutcome,
                    premarketMoveBps: premarketSignal?.MoveBps,
                    premarketSignalReason: premarketSignal?.RejectionReason));
        }

        var selectedDirection = rawDiff > 0 ? BtcPriceDirection.Down : BtcPriceDirection.Up;
        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection);
        if (selectedOutcome is null)
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildDiffShiftProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state,
                    triggerDirection: null,
                    selectedDirection,
                    selectedOutcome: null,
                    previousMarketStartUtc,
                    resultFetchStartUtc,
                    appliedResultCount,
                    pendingSumDeltaUsd,
                    shift.ShiftCount,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    reason: "target_outcome_not_available",
                    threshold: threshold,
                    progressMode: GetDiffShiftProgressPremarketMode(state),
                    counterResultSource: premarketResultSource,
                    premarketResultOutcome: premarketResultOutcome,
                    premarketMoveBps: premarketSignal?.MoveBps,
                    premarketSignalReason: premarketSignal?.RejectionReason));
        }

        var stakeMultiplier = Math.Abs(rawDiff);
        var stakeUsd = unitStakeUsd * stakeMultiplier;
        await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildDiffShiftProgressRawDecisionJson(
                market,
                variant,
                unitStakeUsd,
                nowUtc,
                state,
                triggerDirection: null,
                selectedDirection,
                selectedOutcome,
                previousMarketStartUtc,
                resultFetchStartUtc,
                appliedResultCount,
                pendingSumDeltaUsd,
                shift.ShiftCount,
                stakeMultiplier,
                stakeUsd,
                reason: null,
                threshold: threshold,
                progressMode: GetDiffShiftProgressPremarketMode(state),
                counterResultSource: premarketResultSource,
                premarketResultOutcome: premarketResultOutcome,
                premarketMoveBps: premarketSignal?.MoveBps,
                premarketSignalReason: premarketSignal?.RejectionReason),
            stakeUsdOverride: stakeUsd,
            diffShiftProgressPendingBet: new DiffShiftProgressPendingBet(
                state,
                marketStartUtc.Value,
                selectedOutcome.Outcome,
                stakeUsd));
    }

    private async Task<BtcOpeningLimitDecision> GetDiffLimitProgressPremarketEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal unitStakeUsd,
        DateTimeOffset nowUtc,
        bool freezeCountersAtLimit,
        CancellationToken cancellationToken)
    {
        var multiplierLimit = Math.Max(1, variant.DecisionDepth);
        var counterDiffLimit = freezeCountersAtLimit ? multiplierLimit : (int?)null;
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        if (marketStartUtc is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_limit_progress_current_market_start_missing",
                BuildDiffLimitProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    targetMarketStartUtc: null,
                    resultFetchStartUtc: null,
                    appliedResultCount: 0,
                    pendingSumDeltaUsd: null,
                    utcDayResetApplied: false,
                    multiplierLimit,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    counterResultSource: null,
                    premarketResultOutcome: null,
                    premarketMoveBps: null,
                    premarketSignalReason: null,
                    reason: "diff_limit_progress_current_market_start_missing"));
        }

        if (unitStakeUsd <= 0m)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_limit_progress_unit_stake_non_positive",
                BuildDiffLimitProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    targetMarketStartUtc: null,
                    resultFetchStartUtc: null,
                    appliedResultCount: 0,
                    pendingSumDeltaUsd: null,
                    utcDayResetApplied: false,
                    multiplierLimit,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    counterResultSource: null,
                    premarketResultOutcome: null,
                    premarketMoveBps: null,
                    premarketSignalReason: null,
                    reason: "diff_limit_progress_unit_stake_non_positive"));
        }

        var assetSymbol = GetReferenceAssetSymbol(variant);
        var intervalDuration = BtcUpDown5mMarketAnalyzer.GetIntervalDuration(variant.MarketInterval);
        var previousMarketStartUtc = marketStartUtc.Value.Subtract(intervalDuration);
        var historicalTargetMarketStartUtc = previousMarketStartUtc.Subtract(intervalDuration);
        var premarketResultSource = GetPremarketPreviousResultSource(variant);
        var state = await GetOrCreateDiffShiftProgressStateAsync(
            variant,
            assetSymbol,
            "Up",
            nowUtc,
            cancellationToken);

        var reset = ResetDiffLimitProgressStateForUtcDay(state, marketStartUtc.Value, nowUtc);
        state = reset.State;

        var resultFetchStartUtc = GetDiffShiftProgressFetchStartUtc(state, marketStartUtc.Value, historicalTargetMarketStartUtc);
        IReadOnlyList<DiffCounterMarketResult> results = [];
        if (resultFetchStartUtc is { } fetchStartUtc && fetchStartUtc <= historicalTargetMarketStartUtc)
        {
            try
            {
                results = await FetchDiffCounterMarketResultsAsync(
                    assetSymbol,
                    fetchStartUtc,
                    historicalTargetMarketStartUtc,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "BTC Up or Down 5m Diff Limit Progress Premarket result fetch failed. Strategy={StrategyCode} Asset={AssetSymbol} StartUtc={StartUtc} EndUtc={EndUtc}",
                    variant.Code,
                    assetSymbol,
                    fetchStartUtc,
                    historicalTargetMarketStartUtc);
                await TryRecordApiErrorAsync("GetDiffLimitProgressPremarketResults", ex.Message, cancellationToken);
                return BtcOpeningLimitDecision.Reject(
                    "diff_limit_progress_history_fetch_failed",
                    BuildDiffLimitProgressRawDecisionJson(
                        market,
                        variant,
                        unitStakeUsd,
                        nowUtc,
                        state,
                        selectedDirection: null,
                        selectedOutcome: null,
                        historicalTargetMarketStartUtc,
                        resultFetchStartUtc,
                        appliedResultCount: 0,
                        pendingSumDeltaUsd: null,
                        reset.ResetApplied,
                        multiplierLimit,
                        stakeMultiplier: null,
                        stakeUsd: null,
                        counterResultSource: "ResolvedMarketLedger",
                        premarketResultOutcome: null,
                        premarketMoveBps: null,
                        premarketSignalReason: null,
                        reason: "diff_limit_progress_history_fetch_failed"));
            }
        }

        var applied = ApplyDiffShiftProgressResults(state, results, counterDiffLimit);
        state = applied.State with { UpdatedAtUtc = nowUtc };
        var appliedResultCount = applied.AppliedResultCount;
        var pendingSumDeltaUsd = applied.PendingSumDeltaUsd;

        var requiresHistoricalTargetResult = resultFetchStartUtc is { } requiredStartUtc &&
            requiredStartUtc <= historicalTargetMarketStartUtc &&
            (state.LastProcessedMarketStartUtc is null ||
                state.LastProcessedMarketStartUtc < historicalTargetMarketStartUtc);
        if (requiresHistoricalTargetResult)
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
            return BtcOpeningLimitDecision.Reject(
                "diff_limit_progress_previous_market_resolved_event_missing",
                BuildDiffLimitProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state,
                    selectedDirection: null,
                    selectedOutcome: null,
                    historicalTargetMarketStartUtc,
                    resultFetchStartUtc,
                    appliedResultCount,
                    pendingSumDeltaUsd,
                    reset.ResetApplied,
                    multiplierLimit,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    counterResultSource: "ResolvedMarketLedger",
                    premarketResultOutcome: null,
                    premarketMoveBps: null,
                    premarketSignalReason: null,
                    reason: "diff_limit_progress_previous_market_resolved_event_missing"));
        }

        BtcPreviousMarketMoveSignal? premarketSignal = null;
        string? premarketResultOutcome = null;
        var counterStartUtc = GetDiffCounterUtcDayStartMarketStartUtc(marketStartUtc.Value);
        if (previousMarketStartUtc >= counterStartUtc &&
            (state.LastProcessedMarketStartUtc is null ||
                state.LastProcessedMarketStartUtc < previousMarketStartUtc ||
                state.PendingMarketStartUtc == previousMarketStartUtc))
        {
            premarketSignal = await CalculatePremarketPreviousResultBpsMoveSignalAsync(
                variant,
                marketStartUtc.Value,
                cancellationToken);
            premarketResultOutcome = NormalizeNullableUpDownOutcome(premarketSignal.StreakWinningOutcome);
            if (!premarketSignal.ShouldEnter || premarketResultOutcome is null)
            {
                await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
                var reason = premarketSignal.RejectionReason ?? "diff_limit_progress_premarket_result_missing";
                return BtcOpeningLimitDecision.Reject(
                    reason,
                    BuildDiffLimitProgressRawDecisionJson(
                        market,
                        variant,
                        unitStakeUsd,
                        nowUtc,
                        state,
                        selectedDirection: null,
                        selectedOutcome: null,
                        previousMarketStartUtc,
                        resultFetchStartUtc,
                        appliedResultCount,
                        pendingSumDeltaUsd,
                        reset.ResetApplied,
                        multiplierLimit,
                        stakeMultiplier: null,
                        stakeUsd: null,
                        counterResultSource: premarketResultSource,
                        premarketResultOutcome: null,
                        premarketMoveBps: premarketSignal.MoveBps,
                        premarketSignalReason: reason,
                        reason: reason));
            }

            var premarketResult = CreateDiffShiftProgressPremarketResult(
                premarketSignal,
                premarketResultOutcome,
                premarketResultSource);
            var premarketApplied = ApplyDiffShiftProgressResults(state, [premarketResult], counterDiffLimit);
            state = premarketApplied.State with { UpdatedAtUtc = nowUtc };
            appliedResultCount += premarketApplied.AppliedResultCount;
            pendingSumDeltaUsd = premarketApplied.PendingSumDeltaUsd ?? pendingSumDeltaUsd;
        }

        if (state.PendingMarketStartUtc is { } pendingMarketStartUtc && pendingMarketStartUtc > previousMarketStartUtc)
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
            var reason = pendingMarketStartUtc == marketStartUtc.Value
                ? "diff_limit_progress_current_market_already_pending"
                : "diff_limit_progress_pending_bet_unresolved";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildDiffLimitProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state,
                    selectedDirection: null,
                    selectedOutcome: null,
                    previousMarketStartUtc,
                    resultFetchStartUtc,
                    appliedResultCount,
                    pendingSumDeltaUsd,
                    reset.ResetApplied,
                    multiplierLimit,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    counterResultSource: premarketResultSource,
                    premarketResultOutcome,
                    premarketMoveBps: premarketSignal?.MoveBps,
                    premarketSignalReason: premarketSignal?.RejectionReason,
                    reason));
        }

        var rawDiff = state.UpCount - state.DownCount;
        if (rawDiff == 0)
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
            return BtcOpeningLimitDecision.Reject(
                "diff_limit_progress_zero_diff",
                BuildDiffLimitProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state,
                    selectedDirection: null,
                    selectedOutcome: null,
                    previousMarketStartUtc,
                    resultFetchStartUtc,
                    appliedResultCount,
                    pendingSumDeltaUsd,
                    reset.ResetApplied,
                    multiplierLimit,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    counterResultSource: premarketResultSource,
                    premarketResultOutcome,
                    premarketMoveBps: premarketSignal?.MoveBps,
                    premarketSignalReason: premarketSignal?.RejectionReason,
                    reason: "diff_limit_progress_zero_diff"));
        }

        var selectedDirection = rawDiff > 0 ? BtcPriceDirection.Down : BtcPriceDirection.Up;
        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection);
        if (selectedOutcome is null)
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildDiffLimitProgressRawDecisionJson(
                    market,
                    variant,
                    unitStakeUsd,
                    nowUtc,
                    state,
                    selectedDirection,
                    selectedOutcome: null,
                    previousMarketStartUtc,
                    resultFetchStartUtc,
                    appliedResultCount,
                    pendingSumDeltaUsd,
                    reset.ResetApplied,
                    multiplierLimit,
                    stakeMultiplier: null,
                    stakeUsd: null,
                    counterResultSource: premarketResultSource,
                    premarketResultOutcome,
                    premarketMoveBps: premarketSignal?.MoveBps,
                    premarketSignalReason: premarketSignal?.RejectionReason,
                    reason: "target_outcome_not_available"));
        }

        var uncappedStakeMultiplier = Math.Abs(rawDiff);
        var stakeMultiplier = Math.Min(uncappedStakeMultiplier, multiplierLimit);
        var stakeUsd = unitStakeUsd * stakeMultiplier;
        await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildDiffLimitProgressRawDecisionJson(
                market,
                variant,
                unitStakeUsd,
                nowUtc,
                state,
                selectedDirection,
                selectedOutcome,
                previousMarketStartUtc,
                resultFetchStartUtc,
                appliedResultCount,
                pendingSumDeltaUsd,
                reset.ResetApplied,
                multiplierLimit,
                stakeMultiplier,
                stakeUsd,
                counterResultSource: premarketResultSource,
                premarketResultOutcome,
                premarketMoveBps: premarketSignal?.MoveBps,
                premarketSignalReason: premarketSignal?.RejectionReason,
                reason: null),
            stakeUsdOverride: stakeUsd,
            diffShiftProgressPendingBet: new DiffShiftProgressPendingBet(
                state,
                marketStartUtc.Value,
                selectedOutcome.Outcome,
                stakeUsd));
    }

    private async Task<BtcOpeningLimitDecision> GetConfirmedAveragePremarketEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        BtcCurrentPriceLookupCache currentPrices,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<BtcPreviousMarketMoveSignal>>> premarketSignalTasks,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<DiffReferenceAverageMarketResultsLookup>>> resultTasks,
        CancellationToken cancellationToken)
    {
        var baseVariant = TryGetLinkedSignalVariant(variant.BaseSignalStrategyId);
        var confirmationVariant = TryGetLinkedSignalVariant(variant.ConfirmationSignalStrategyId);
        var configurationError = GetConfirmedAverageConfigurationError(
            variant,
            baseVariant,
            confirmationVariant);
        if (configurationError is not null)
        {
            const string reason = "confirmed_average_strategy_link_invalid";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildConfirmedAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    baseVariant,
                    confirmationVariant,
                    baseDecision: null,
                    confirmationDecision: null,
                    signalsAgree: false,
                    configurationError: configurationError,
                    selectedOutcome: null,
                    reason: reason));
        }

        var baseDecision = await GetOpeningLimitEntryDecisionAsync(
            market,
            baseVariant!,
            stakeUsd,
            nowUtc,
            currentPrices,
            premarketSignalTasks,
            resultTasks,
            cancellationToken);
        if (!baseDecision.ShouldEnter || baseDecision.SelectedOutcome is null)
        {
            var reason = baseDecision.SkipReason ?? "confirmed_average_base_signal_not_available";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildConfirmedAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    baseVariant,
                    confirmationVariant,
                    baseDecision,
                    confirmationDecision: null,
                    signalsAgree: false,
                    configurationError: null,
                    selectedOutcome: null,
                    reason: reason));
        }

        var confirmationDecision = await GetOpeningLimitEntryDecisionAsync(
            market,
            confirmationVariant!,
            stakeUsd,
            nowUtc,
            currentPrices,
            premarketSignalTasks,
            resultTasks,
            cancellationToken);
        if (!confirmationDecision.ShouldEnter || confirmationDecision.SelectedOutcome is null)
        {
            const string reason = "confirmed_average_confirmation_signal_not_available";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildConfirmedAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    baseVariant,
                    confirmationVariant,
                    baseDecision,
                    confirmationDecision,
                    signalsAgree: false,
                    configurationError: null,
                    selectedOutcome: null,
                    reason: reason));
        }

        var signalsAgree = string.Equals(
            baseDecision.SelectedOutcome.Outcome,
            confirmationDecision.SelectedOutcome.Outcome,
            StringComparison.OrdinalIgnoreCase);
        if (!signalsAgree)
        {
            const string reason = "confirmed_average_signal_mismatch";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildConfirmedAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    baseVariant,
                    confirmationVariant,
                    baseDecision,
                    confirmationDecision,
                    signalsAgree: false,
                    configurationError: null,
                    selectedOutcome: null,
                    reason: reason));
        }

        if (!string.Equals(
            baseDecision.SelectedOutcome.AssetId,
            confirmationDecision.SelectedOutcome.AssetId,
            StringComparison.OrdinalIgnoreCase))
        {
            const string reason = "confirmed_average_target_asset_mismatch";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildConfirmedAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    baseVariant,
                    confirmationVariant,
                    baseDecision,
                    confirmationDecision,
                    signalsAgree: true,
                    configurationError: null,
                    selectedOutcome: null,
                    reason: reason));
        }

        return BtcOpeningLimitDecision.Enter(
            baseDecision.SelectedOutcome,
            BuildConfirmedAverageRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                baseVariant,
                confirmationVariant,
                baseDecision,
                confirmationDecision,
                signalsAgree: true,
                configurationError: null,
                selectedOutcome: baseDecision.SelectedOutcome,
                reason: null),
            baseDecision.LimitPriceOverride,
            baseDecision.StakeUsdOverride,
            baseDecision.DiffShiftProgressPendingBet);
    }

    private static BtcUpDown5mStrategyVariant? TryGetLinkedSignalVariant(Guid? strategyId)
    {
        return strategyId is { } id &&
            StrategyVariantsById.TryGetValue(StrategyIds.Normalize(id), out var variant)
            ? variant
            : null;
    }

    private static string? GetConfirmedAverageConfigurationError(
        BtcUpDown5mStrategyVariant variant,
        BtcUpDown5mStrategyVariant? baseVariant,
        BtcUpDown5mStrategyVariant? confirmationVariant)
    {
        if (baseVariant is null)
        {
            return "base_signal_strategy_not_registered";
        }

        if (confirmationVariant is null)
        {
            return "confirmation_signal_strategy_not_registered";
        }

        var isBpsConfirmed = variant.Behavior == BtcUpDown5mStrategyBehavior.BpsConfirmedAveragePremarket;
        var expectedBaseBehavior = isBpsConfirmed
            ? BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket
            : BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket;
        var expectedConfirmationBehavior = isBpsConfirmed
            ? BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket
            : BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket;
        if (baseVariant.Behavior != expectedBaseBehavior)
        {
            return "base_signal_strategy_behavior_mismatch";
        }

        if (confirmationVariant.Behavior != expectedConfirmationBehavior)
        {
            return "confirmation_signal_strategy_behavior_mismatch";
        }

        if (!string.Equals(
                GetReferenceAssetSymbol(variant),
                GetReferenceAssetSymbol(baseVariant),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                GetReferenceAssetSymbol(variant),
                GetReferenceAssetSymbol(confirmationVariant),
                StringComparison.OrdinalIgnoreCase))
        {
            return "signal_strategy_asset_mismatch";
        }

        if (variant.MarketInterval != baseVariant.MarketInterval ||
            variant.MarketInterval != confirmationVariant.MarketInterval ||
            variant.EntryDelaySeconds != baseVariant.EntryDelaySeconds ||
            variant.EntryDelaySeconds != confirmationVariant.EntryDelaySeconds)
        {
            return "signal_strategy_timing_mismatch";
        }

        if (isBpsConfirmed && variant.DecisionThresholdBps != baseVariant.DecisionThresholdBps)
        {
            return "base_bps_threshold_mismatch";
        }

        if (!isBpsConfirmed && variant.DecisionDepth != baseVariant.DecisionDepth)
        {
            return "base_diff_threshold_mismatch";
        }

        var bpsSignalVariant = isBpsConfirmed ? baseVariant : confirmationVariant;
        if (bpsSignalVariant.FixedOutcome is not null || bpsSignalVariant.DiffCounterTriggerOutcome is not null)
        {
            return "bps_signal_strategy_is_not_neutral";
        }

        return null;
    }

    private static string BuildConfirmedAverageRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        BtcUpDown5mStrategyVariant? baseVariant,
        BtcUpDown5mStrategyVariant? confirmationVariant,
        BtcOpeningLimitDecision? baseDecision,
        BtcOpeningLimitDecision? confirmationDecision,
        bool signalsAgree,
        string? configurationError,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason)
    {
        var isBpsConfirmed = variant.Behavior == BtcUpDown5mStrategyBehavior.BpsConfirmedAveragePremarket;
        var root = new JsonObject
        {
            ["pricing_mode"] = OpeningLimitPricingMode,
            ["order_execution_mode"] = FakOrderType,
            ["order_type"] = FakOrderType,
            ["post_only"] = false,
            ["confirmed_average_premarket_enabled"] = true,
            ["confirmed_average_base_family"] = isBpsConfirmed ? "bps_reference_average" : "diff_reference_average",
            ["confirmed_average_confirmation_family"] = isBpsConfirmed ? "diff_reference_average" : "bps_reference_average",
            ["fak_stats_probe"] = true,
            ["strategy_code"] = variant.Code,
            ["strategy_category"] = variant.Category,
            ["decision_source"] = isBpsConfirmed
                ? "bps_reference_average_confirmed_by_diff_reference_average_premarket"
                : "diff_reference_average_confirmed_by_bps_reference_average_premarket",
            ["reference_asset_symbol"] = GetReferenceAssetSymbol(variant),
            ["quote_received_at_utc"] = nowUtc,
            ["condition_id"] = market.ConditionId,
            ["market_id"] = market.MarketId,
            ["market_slug"] = market.Slug,
            ["entry_delay_seconds"] = variant.EntryDelaySeconds,
            ["base_signal_strategy_id"] = baseVariant?.Id.ToString(),
            ["base_signal_strategy_code"] = baseVariant?.Code,
            ["base_signal_strategy_name"] = baseVariant?.Name,
            ["base_signal_should_enter"] = baseDecision?.ShouldEnter,
            ["base_signal_skip_reason"] = baseDecision?.SkipReason,
            ["base_signal_asset_id"] = baseDecision?.SelectedOutcome?.AssetId,
            ["base_signal_outcome"] = baseDecision?.SelectedOutcome?.Outcome,
            ["confirmation_signal_strategy_id"] = confirmationVariant?.Id.ToString(),
            ["confirmation_signal_strategy_code"] = confirmationVariant?.Code,
            ["confirmation_signal_strategy_name"] = confirmationVariant?.Name,
            ["confirmation_signal_should_enter"] = confirmationDecision?.ShouldEnter,
            ["confirmation_signal_skip_reason"] = confirmationDecision?.SkipReason,
            ["confirmation_signal_asset_id"] = confirmationDecision?.SelectedOutcome?.AssetId,
            ["confirmation_signal_outcome"] = confirmationDecision?.SelectedOutcome?.Outcome,
            ["signals_agree"] = signalsAgree,
            ["configuration_error"] = configurationError,
            ["selected_direction"] = selectedOutcome?.Outcome,
            ["asset_id"] = selectedOutcome?.AssetId,
            ["outcome"] = selectedOutcome?.Outcome,
            ["target_notional_usd"] = stakeUsd,
            ["skip_reason"] = reason
        };
        root["base_signal_decision"] = ParseNestedDecisionJson(baseDecision?.RawDecisionJson);
        root["confirmation_signal_decision"] = ParseNestedDecisionJson(confirmationDecision?.RawDecisionJson);
        return root.ToJsonString();
    }

    private static JsonNode? ParseNestedDecisionJson(string? rawDecisionJson)
    {
        if (string.IsNullOrWhiteSpace(rawDecisionJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(rawDecisionJson);
        }
        catch (JsonException)
        {
            return JsonValue.Create(rawDecisionJson);
        }
    }

    private async Task<BtcOpeningLimitDecision> GetDiffReferenceAveragePremarketEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<BtcPreviousMarketMoveSignal>>> premarketSignalTasks,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<DiffReferenceAverageMarketResultsLookup>>> resultTasks,
        CancellationToken cancellationToken)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        if (marketStartUtc is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_reference_average_current_market_start_missing",
                BuildDiffReferenceAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    rollingStartUtc: null,
                    rollingEndUtc: null,
                    resultFetchStartUtc: null,
                    resultFetchEndUtc: null,
                    historicalResultCount: 0,
                    samples: [],
                    averages: [],
                    selectedAverage: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    diffDeltaFromAverage: null,
                    premarketResultOutcome: null,
                    premarketMoveBps: null,
                    premarketSignalReason: null,
                    reason: "diff_reference_average_current_market_start_missing"));
        }

        if (stakeUsd <= 0m)
        {
            return BtcOpeningLimitDecision.Reject(
                "diff_reference_average_stake_non_positive",
                BuildDiffReferenceAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    rollingStartUtc: null,
                    rollingEndUtc: null,
                    resultFetchStartUtc: null,
                    resultFetchEndUtc: null,
                    historicalResultCount: 0,
                    samples: [],
                    averages: [],
                    selectedAverage: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    diffDeltaFromAverage: null,
                    premarketResultOutcome: null,
                    premarketMoveBps: null,
                    premarketSignalReason: null,
                    reason: "diff_reference_average_stake_non_positive"));
        }

        var assetSymbol = GetReferenceAssetSymbol(variant);
        var intervalDuration = BtcUpDown5mMarketAnalyzer.GetIntervalDuration(variant.MarketInterval);
        var previousMarketStartUtc = marketStartUtc.Value.Subtract(intervalDuration);
        var rollingStartUtc = previousMarketStartUtc.Subtract(TimeSpan.FromHours(24)).Add(intervalDuration);
        var historicalTargetMarketStartUtc = previousMarketStartUtc.Subtract(intervalDuration);
        var premarketResultSource = GetPremarketPreviousResultSource(variant);
        IReadOnlyList<DiffCounterMarketResult> historicalResults = [];
        if (rollingStartUtc <= historicalTargetMarketStartUtc)
        {
            var resultLookup = await GetCachedDiffReferenceAverageMarketResultsAsync(
                resultTasks,
                assetSymbol,
                rollingStartUtc,
                historicalTargetMarketStartUtc,
                cancellationToken);
            if (!resultLookup.Succeeded)
            {
                return BtcOpeningLimitDecision.Reject(
                    "diff_reference_average_history_fetch_failed",
                    BuildDiffReferenceAverageRawDecisionJson(
                        market,
                        variant,
                        stakeUsd,
                        nowUtc,
                        rollingStartUtc,
                        previousMarketStartUtc,
                        rollingStartUtc,
                        historicalTargetMarketStartUtc,
                        historicalResultCount: 0,
                        samples: [],
                        averages: [],
                        selectedAverage: null,
                        selectedDirection: null,
                        selectedOutcome: null,
                        diffDeltaFromAverage: null,
                        premarketResultOutcome: null,
                        premarketMoveBps: null,
                        premarketSignalReason: null,
                        reason: "diff_reference_average_history_fetch_failed"));
            }

            historicalResults = resultLookup.Results;
        }

        var premarketSignal = await GetCachedDiffReferenceAveragePremarketSignalAsync(
            premarketSignalTasks,
            variant,
            marketStartUtc.Value,
            cancellationToken);
        var premarketResultOutcome = NormalizeNullableUpDownOutcome(premarketSignal.StreakWinningOutcome);
        if (!premarketSignal.ShouldEnter || premarketResultOutcome is null)
        {
            var reason = premarketSignal.RejectionReason ?? "diff_reference_average_premarket_result_missing";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildDiffReferenceAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    rollingStartUtc,
                    previousMarketStartUtc,
                    rollingStartUtc,
                    historicalTargetMarketStartUtc,
                    historicalResults.Count,
                    samples: [],
                    averages: [],
                    selectedAverage: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    diffDeltaFromAverage: null,
                    premarketResultOutcome: null,
                    premarketMoveBps: premarketSignal.MoveBps,
                    premarketSignalReason: reason,
                    reason));
        }

        var premarketResult = CreateDiffShiftProgressPremarketResult(
            premarketSignal,
            premarketResultOutcome,
            premarketResultSource);
        var combinedResults = historicalResults
            .Concat(new[] { premarketResult })
            .ToArray();
        var samples = BuildDiffReferenceAverageSamples(combinedResults);
        var averages = BuildDiffReferenceAverageWindows(samples, previousMarketStartUtc, intervalDuration);
        var currentSample = samples.LastOrDefault(sample => sample.MarketStartUtc == previousMarketStartUtc);
        if (currentSample is null)
        {
            const string reason = "diff_reference_average_current_diff_missing";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildDiffReferenceAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    rollingStartUtc,
                    previousMarketStartUtc,
                    rollingStartUtc,
                    historicalTargetMarketStartUtc,
                    historicalResults.Count,
                    samples,
                    averages,
                    selectedAverage: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    diffDeltaFromAverage: null,
                    premarketResultOutcome,
                    premarketMoveBps: premarketSignal.MoveBps,
                    premarketSignalReason: premarketSignal.RejectionReason,
                    reason));
        }

        var rollingWindow = averages.FirstOrDefault(average =>
            string.Equals(average.WindowLabel, "24h", StringComparison.OrdinalIgnoreCase));
        if (rollingWindow is null || !rollingWindow.IsFullWindow)
        {
            const string reason = "diff_reference_average_rolling_window_missing";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildDiffReferenceAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    rollingStartUtc,
                    previousMarketStartUtc,
                    rollingStartUtc,
                    historicalTargetMarketStartUtc,
                    historicalResults.Count,
                    samples,
                    averages,
                    selectedAverage: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    diffDeltaFromAverage: null,
                    premarketResultOutcome,
                    premarketMoveBps: premarketSignal.MoveBps,
                    premarketSignalReason: premarketSignal.RejectionReason,
                    reason));
        }

        var selectedAverage = averages
            .Where(average => average.IsFullWindow && average.AverageDiff is not null)
            .OrderByDescending(average => Math.Abs(average.AverageDiff.GetValueOrDefault()))
            .ThenByDescending(average => average.WindowSeconds)
            .FirstOrDefault();
        if (selectedAverage is null)
        {
            const string reason = "diff_reference_average_full_window_missing";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildDiffReferenceAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    rollingStartUtc,
                    previousMarketStartUtc,
                    rollingStartUtc,
                    historicalTargetMarketStartUtc,
                    historicalResults.Count,
                    samples,
                    averages,
                    selectedAverage: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    diffDeltaFromAverage: null,
                    premarketResultOutcome,
                    premarketMoveBps: premarketSignal.MoveBps,
                    premarketSignalReason: premarketSignal.RejectionReason,
                    reason));
        }

        var diffDeltaFromAverage = currentSample.Diff - selectedAverage.AverageDiff.GetValueOrDefault();
        var threshold = GetDiffReferenceAverageMinDelta(variant);
        BtcPriceDirection? selectedDirection = diffDeltaFromAverage switch
        {
            var delta when delta >= threshold => BtcPriceDirection.Down,
            var delta when delta <= -threshold => BtcPriceDirection.Up,
            _ => null
        };
        if (selectedDirection is null)
        {
            const string reason = "diff_reference_average_delta_below_threshold";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildDiffReferenceAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    rollingStartUtc,
                    previousMarketStartUtc,
                    rollingStartUtc,
                    historicalTargetMarketStartUtc,
                    historicalResults.Count,
                    samples,
                    averages,
                    selectedAverage,
                    selectedDirection: null,
                    selectedOutcome: null,
                    diffDeltaFromAverage,
                    premarketResultOutcome,
                    premarketMoveBps: premarketSignal.MoveBps,
                    premarketSignalReason: premarketSignal.RejectionReason,
                    reason));
        }

        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection.Value);
        if (selectedOutcome is null)
        {
            const string reason = "target_outcome_not_available";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildDiffReferenceAverageRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    rollingStartUtc,
                    previousMarketStartUtc,
                    rollingStartUtc,
                    historicalTargetMarketStartUtc,
                    historicalResults.Count,
                    samples,
                    averages,
                    selectedAverage,
                    selectedDirection,
                    selectedOutcome: null,
                    diffDeltaFromAverage,
                    premarketResultOutcome,
                    premarketMoveBps: premarketSignal.MoveBps,
                    premarketSignalReason: premarketSignal.RejectionReason,
                    reason));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildDiffReferenceAverageRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                rollingStartUtc,
                previousMarketStartUtc,
                rollingStartUtc,
                historicalTargetMarketStartUtc,
                historicalResults.Count,
                samples,
                averages,
                selectedAverage,
                selectedDirection,
                selectedOutcome,
                diffDeltaFromAverage,
                premarketResultOutcome,
                premarketMoveBps: premarketSignal.MoveBps,
                premarketSignalReason: premarketSignal.RejectionReason,
                reason: null));
    }

    private Task<DiffReferenceAverageMarketResultsLookup> GetCachedDiffReferenceAverageMarketResultsAsync(
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<DiffReferenceAverageMarketResultsLookup>>> resultTasks,
        string assetSymbol,
        DateTimeOffset startTimeMinUtc,
        DateTimeOffset startTimeMaxUtc,
        CancellationToken cancellationToken)
    {
        var normalizedAsset = NormalizeAssetSymbol(assetSymbol);
        var cacheKey = string.Concat(
            normalizedAsset,
            ":",
            startTimeMinUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ":",
            startTimeMaxUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        var lazy = resultTasks.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<DiffReferenceAverageMarketResultsLookup>>(
                () => FetchDiffReferenceAverageMarketResultsAsync(
                    normalizedAsset,
                    startTimeMinUtc,
                    startTimeMaxUtc,
                    cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private async Task<DiffReferenceAverageMarketResultsLookup> FetchDiffReferenceAverageMarketResultsAsync(
        string assetSymbol,
        DateTimeOffset startTimeMinUtc,
        DateTimeOffset startTimeMaxUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await FetchDiffCounterMarketResultsAsync(
                assetSymbol,
                startTimeMinUtc,
                startTimeMaxUtc,
                cancellationToken);
            return DiffReferenceAverageMarketResultsLookup.Success(results);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "BTC Up or Down 5m Diff Reference Average Premarket result fetch failed. Asset={AssetSymbol} StartUtc={StartUtc} EndUtc={EndUtc}",
                assetSymbol,
                startTimeMinUtc,
                startTimeMaxUtc);
            await TryRecordApiErrorAsync("GetDiffReferenceAveragePremarketResults", ex.Message, cancellationToken);
            return DiffReferenceAverageMarketResultsLookup.Failure(ex.Message);
        }
    }

    private Task<BtcPreviousMarketMoveSignal> GetCachedDiffReferenceAveragePremarketSignalAsync(
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<BtcPreviousMarketMoveSignal>>> signalTasks,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset marketStartUtc,
        CancellationToken cancellationToken)
    {
        var cacheKey = string.Concat(
            GetReferenceAssetSymbol(variant),
            ":diff_reference_average_premarket:",
            variant.MarketInterval,
            ":",
            GetPremarketPreviousResultSampleSecondsBeforeEnd(variant).ToString(CultureInfo.InvariantCulture),
            ":",
            marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        var lazy = signalTasks.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<BtcPreviousMarketMoveSignal>>(
                () => CalculatePremarketPreviousResultBpsMoveSignalAsync(
                    variant,
                    marketStartUtc,
                    cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private async Task<CryptoUpDown5mDiffShiftProgressState> GetOrCreateDiffShiftProgressStateAsync(
        BtcUpDown5mStrategyVariant variant,
        string assetSymbol,
        string triggerOutcome,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetCryptoUpDown5mDiffShiftProgressStateAsync(variant.Id, cancellationToken);
        if (existing is not null)
        {
            return existing with
            {
                StrategyId = StrategyIds.Normalize(variant.Id),
                AssetSymbol = assetSymbol.Trim().ToUpperInvariant(),
                TriggerOutcome = NormalizeUpDownOutcome(triggerOutcome),
                UpCount = Math.Max(0, existing.UpCount),
                DownCount = Math.Max(0, existing.DownCount),
                DampingDirection = NormalizeNullableUpDownOutcome(existing.DampingDirection)
            };
        }

        return new CryptoUpDown5mDiffShiftProgressState(
            StrategyIds.Normalize(variant.Id),
            assetSymbol.Trim().ToUpperInvariant(),
            NormalizeUpDownOutcome(triggerOutcome),
            UpCount: 0,
            DownCount: 0,
            SumAmount: 0m,
            DampingActive: false,
            DampingDirection: null,
            LastProcessedMarketStartUtc: null,
            PendingMarketStartUtc: null,
            PendingTargetOutcome: null,
            PendingStakeUsd: null,
            PendingCreatedAtUtc: null,
            CreatedAtUtc: nowUtc,
            UpdatedAtUtc: nowUtc);
    }

    private static (CryptoUpDown5mDiffShiftProgressState State, bool ResetApplied) ResetDiffLimitProgressStateForUtcDay(
        CryptoUpDown5mDiffShiftProgressState state,
        DateTimeOffset currentMarketStartUtc,
        DateTimeOffset nowUtc)
    {
        var counterStartUtc = GetDiffCounterUtcDayStartMarketStartUtc(currentMarketStartUtc);
        var hasPreviousDayState = state.LastProcessedMarketStartUtc is { } lastProcessedMarketStartUtc
            ? lastProcessedMarketStartUtc < counterStartUtc
            : state.CreatedAtUtc < counterStartUtc;
        if (!hasPreviousDayState)
        {
            return (state, false);
        }

        return (state with
        {
            UpCount = 0,
            DownCount = 0,
            SumAmount = 0m,
            DampingActive = false,
            DampingDirection = null,
            LastProcessedMarketStartUtc = null,
            PendingMarketStartUtc = null,
            PendingTargetOutcome = null,
            PendingStakeUsd = null,
            PendingCreatedAtUtc = null,
            UpdatedAtUtc = nowUtc
        }, true);
    }

    private static DateTimeOffset? GetDiffShiftProgressFetchStartUtc(
        CryptoUpDown5mDiffShiftProgressState state,
        DateTimeOffset currentMarketStartUtc,
        DateTimeOffset targetMarketStartUtc)
    {
        var catchUpStartUtc = state.LastProcessedMarketStartUtc?.AddMinutes(5) ??
            GetDiffCounterUtcDayStartMarketStartUtc(currentMarketStartUtc);
        if (state.PendingMarketStartUtc is { } pendingMarketStartUtc &&
            pendingMarketStartUtc <= targetMarketStartUtc &&
            pendingMarketStartUtc < catchUpStartUtc)
        {
            catchUpStartUtc = pendingMarketStartUtc;
        }

        return catchUpStartUtc <= targetMarketStartUtc ? catchUpStartUtc : null;
    }

    private static DiffShiftProgressApplyResult ApplyDiffShiftProgressResults(
        CryptoUpDown5mDiffShiftProgressState state,
        IReadOnlyList<DiffCounterMarketResult> results,
        int? diffCounterLimit = null)
    {
        var upCount = Math.Max(0, state.UpCount);
        var downCount = Math.Max(0, state.DownCount);
        var sumAmount = state.SumAmount;
        var lastProcessedMarketStartUtc = state.LastProcessedMarketStartUtc;
        var pendingMarketStartUtc = state.PendingMarketStartUtc;
        var pendingTargetOutcome = NormalizeNullableUpDownOutcome(state.PendingTargetOutcome);
        var pendingStakeUsd = state.PendingStakeUsd;
        var pendingCreatedAtUtc = state.PendingCreatedAtUtc;
        decimal? pendingSumDeltaUsd = null;
        var appliedResultCount = 0;

        foreach (var result in results.OrderBy(item => item.MarketStartUtc))
        {
            var winningOutcome = NormalizeUpDownOutcome(result.WinningOutcome);
            if (pendingMarketStartUtc == result.MarketStartUtc && pendingStakeUsd is > 0m)
            {
                var won = string.Equals(winningOutcome, pendingTargetOutcome, StringComparison.OrdinalIgnoreCase);
                var delta = won ? pendingStakeUsd.Value : -pendingStakeUsd.Value;
                sumAmount += delta;
                pendingSumDeltaUsd = delta;
                pendingMarketStartUtc = null;
                pendingTargetOutcome = null;
                pendingStakeUsd = null;
                pendingCreatedAtUtc = null;
            }

            if (lastProcessedMarketStartUtc is { } lastProcessed && result.MarketStartUtc <= lastProcessed)
            {
                continue;
            }

            if (string.Equals(winningOutcome, "Up", StringComparison.OrdinalIgnoreCase))
            {
                if (diffCounterLimit is null || upCount - downCount < diffCounterLimit.Value)
                {
                    upCount++;
                }
            }
            else
            {
                if (diffCounterLimit is null || downCount - upCount < diffCounterLimit.Value)
                {
                    downCount++;
                }
            }

            lastProcessedMarketStartUtc = result.MarketStartUtc;
            appliedResultCount++;
        }

        return new DiffShiftProgressApplyResult(
            state with
            {
                UpCount = upCount,
                DownCount = downCount,
                SumAmount = sumAmount,
                LastProcessedMarketStartUtc = lastProcessedMarketStartUtc,
                PendingMarketStartUtc = pendingMarketStartUtc,
                PendingTargetOutcome = pendingTargetOutcome,
                PendingStakeUsd = pendingStakeUsd,
                PendingCreatedAtUtc = pendingCreatedAtUtc
            },
            appliedResultCount,
            pendingSumDeltaUsd);
    }

    private static DiffShiftProgressShiftResult ApplyDiffShiftProgressShift(
        CryptoUpDown5mDiffShiftProgressState state,
        BtcPriceDirection triggerDirection,
        decimal unitStakeUsd)
    {
        if (unitStakeUsd <= 0m)
        {
            return new DiffShiftProgressShiftResult(state, 0);
        }

        var upCount = Math.Max(0, state.UpCount);
        var downCount = Math.Max(0, state.DownCount);
        var sumAmount = state.SumAmount;
        var shiftCount = 0;
        var effectiveDiff = GetDiffShiftProgressEffectiveDiff(upCount, downCount, triggerDirection);
        while (sumAmount > unitStakeUsd && effectiveDiff > 1)
        {
            if (triggerDirection == BtcPriceDirection.Up)
            {
                upCount = Math.Max(0, upCount - 1);
            }
            else
            {
                downCount = Math.Max(0, downCount - 1);
            }

            sumAmount -= unitStakeUsd;
            shiftCount++;
            effectiveDiff = GetDiffShiftProgressEffectiveDiff(upCount, downCount, triggerDirection);
        }

        return new DiffShiftProgressShiftResult(
            state with
            {
                UpCount = upCount,
                DownCount = downCount,
                SumAmount = sumAmount
            },
            shiftCount);
    }

    private static DiffCounterMarketResult CreateDiffShiftProgressPremarketResult(
        BtcPreviousMarketMoveSignal signal,
        string winningOutcome,
        string source)
    {
        return new DiffCounterMarketResult(
            signal.PreviousMarketId ?? string.Empty,
            string.Empty,
            signal.PreviousMarketSlug ?? string.Empty,
            signal.PreviousMarketStartUtc,
            signal.PreviousMarketEndUtc,
            NormalizeUpDownOutcome(winningOutcome),
            source);
    }

    private static IReadOnlyList<DiffReferenceAverageSample> BuildDiffReferenceAverageSamples(
        IReadOnlyList<DiffCounterMarketResult> results)
    {
        var upCount = 0;
        var downCount = 0;
        var samples = new List<DiffReferenceAverageSample>(results.Count);
        foreach (var result in results
            .Where(result => string.Equals(result.WinningOutcome, "Up", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.WinningOutcome, "Down", StringComparison.OrdinalIgnoreCase))
            .GroupBy(result => result.MarketStartUtc)
            .Select(group => group
                .OrderByDescending(result => !string.IsNullOrWhiteSpace(result.MarketId))
                .ThenBy(result => result.MarketSlug, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(result => result.MarketStartUtc))
        {
            var winningOutcome = NormalizeUpDownOutcome(result.WinningOutcome);
            if (string.Equals(winningOutcome, "Up", StringComparison.OrdinalIgnoreCase))
            {
                upCount++;
            }
            else
            {
                downCount++;
            }

            samples.Add(new DiffReferenceAverageSample(
                result.MarketStartUtc,
                winningOutcome,
                result.Source,
                upCount,
                downCount,
                upCount - downCount));
        }

        return samples;
    }

    private static IReadOnlyList<DiffReferenceAverageWindow> BuildDiffReferenceAverageWindows(
        IReadOnlyList<DiffReferenceAverageSample> samples,
        DateTimeOffset rollingEndUtc,
        TimeSpan intervalDuration)
    {
        return DiffReferenceAverageWindows
            .Select(spec =>
            {
                var windowStartExclusiveUtc = rollingEndUtc.Subtract(spec.Duration);
                var windowSamples = samples
                    .Where(sample => sample.MarketStartUtc > windowStartExclusiveUtc &&
                        sample.MarketStartUtc <= rollingEndUtc)
                    .OrderBy(sample => sample.MarketStartUtc)
                    .ToArray();
                var expectedSampleCount = GetDiffReferenceAverageExpectedSampleCount(spec.Duration, intervalDuration);
                var isFullWindow = windowSamples.Length == expectedSampleCount &&
                    windowSamples.FirstOrDefault()?.MarketStartUtc == windowStartExclusiveUtc.Add(intervalDuration) &&
                    windowSamples.LastOrDefault()?.MarketStartUtc == rollingEndUtc &&
                    HasContiguousDiffReferenceAverageSamples(windowSamples, intervalDuration);
                var averageDiff = windowSamples.Length == 0
                    ? (decimal?)null
                    : windowSamples.Average(sample => (decimal)sample.Diff);
                return new DiffReferenceAverageWindow(
                    spec.Label,
                    (int)spec.Duration.TotalSeconds,
                    (int)intervalDuration.TotalSeconds,
                    windowSamples.Length,
                    expectedSampleCount,
                    isFullWindow,
                    averageDiff,
                    windowSamples.FirstOrDefault()?.MarketStartUtc,
                    windowSamples.LastOrDefault()?.MarketStartUtc);
            })
            .ToArray();
    }

    private static int GetDiffReferenceAverageExpectedSampleCount(
        TimeSpan windowDuration,
        TimeSpan intervalDuration)
    {
        return intervalDuration.Ticks <= 0
            ? 0
            : (int)(windowDuration.Ticks / intervalDuration.Ticks);
    }

    private static bool HasContiguousDiffReferenceAverageSamples(
        IReadOnlyList<DiffReferenceAverageSample> samples,
        TimeSpan intervalDuration)
    {
        for (var index = 1; index < samples.Count; index++)
        {
            if (samples[index].MarketStartUtc - samples[index - 1].MarketStartUtc != intervalDuration)
            {
                return false;
            }
        }

        return true;
    }

    private static decimal GetDiffReferenceAverageMinDelta(BtcUpDown5mStrategyVariant variant)
    {
        var threshold = variant.DecisionThresholdBps ?? variant.DecisionDepth;
        return threshold > 0m ? threshold : 1m;
    }

    private static string GetDiffShiftProgressPremarketMode(CryptoUpDown5mDiffShiftProgressState state)
    {
        return state.DampingActive ? "Damping" : "Simple";
    }

    private static DiffShiftProgressShiftResult ApplyDiffShiftProgressPremarketDamping(
        CryptoUpDown5mDiffShiftProgressState state,
        int threshold,
        decimal unitStakeUsd)
    {
        var boundedThreshold = Math.Max(1, threshold);
        var upCount = Math.Max(0, state.UpCount);
        var downCount = Math.Max(0, state.DownCount);
        var sumAmount = state.SumAmount;
        var dampingActive = state.DampingActive;
        var dampingDirection = NormalizeNullableUpDownOutcome(state.DampingDirection);
        var rawDiff = upCount - downCount;
        if (!dampingActive && Math.Abs(rawDiff) >= boundedThreshold)
        {
            dampingActive = true;
            dampingDirection = rawDiff > 0 ? "Up" : "Down";
            sumAmount = 0m;
        }

        var shiftCount = 0;
        while (dampingActive && unitStakeUsd > 0m && sumAmount > unitStakeUsd && rawDiff != 0)
        {
            if (rawDiff > 0)
            {
                upCount = Math.Max(0, upCount - 1);
            }
            else
            {
                downCount = Math.Max(0, downCount - 1);
            }

            sumAmount -= unitStakeUsd;
            shiftCount++;
            rawDiff = upCount - downCount;
        }

        if (dampingActive && rawDiff == 0)
        {
            dampingActive = false;
            dampingDirection = null;
            sumAmount = 0m;
        }
        else if (dampingActive && dampingDirection is null)
        {
            dampingDirection = rawDiff > 0 ? "Up" : "Down";
        }

        return new DiffShiftProgressShiftResult(
            state with
            {
                UpCount = upCount,
                DownCount = downCount,
                SumAmount = sumAmount,
                DampingActive = dampingActive,
                DampingDirection = dampingDirection
            },
            shiftCount);
    }

    private async Task RecordDiffShiftProgressPendingBetAsync(
        DiffShiftProgressPendingBet? pendingBet,
        decimal actualStakeUsd,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (pendingBet is null || actualStakeUsd <= 0m)
        {
            return;
        }

        var state = pendingBet.State with
        {
            PendingMarketStartUtc = pendingBet.MarketStartUtc,
            PendingTargetOutcome = NormalizeUpDownOutcome(pendingBet.TargetOutcome),
            PendingStakeUsd = actualStakeUsd,
            PendingCreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        try
        {
            await repository.UpsertCryptoUpDown5mDiffShiftProgressStateAsync(state, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to persist Diff Shift Progress pending bet. StrategyId={StrategyId} MarketStartUtc={MarketStartUtc}",
                state.StrategyId,
                pendingBet.MarketStartUtc);
            await TryRecordApiErrorAsync("UpsertDiffShiftProgressPendingBet", ex.Message, cancellationToken);
        }
    }

    private async Task<DiffCounterSnapshot> GetDiffCounterStateAsync(
        string assetSymbol,
        DateTimeOffset referenceMarketStartUtc,
        DateTimeOffset nowUtc,
        bool resetAtUtcDayStart,
        string stateKey,
        int shiftDiffCount,
        CancellationToken cancellationToken,
        DateTimeOffset? counterStartOverrideUtc = null)
    {
        var normalizedAssetSymbol = assetSymbol.Trim().ToUpperInvariant();
        var normalizedStateKey = string.IsNullOrWhiteSpace(stateKey)
            ? normalizedAssetSymbol
            : stateKey.Trim();
        var requestedTargetMarketStartUtc = referenceMarketStartUtc.AddMinutes(-5);
        var latestClosedTargetMarketStartUtc = GetDiffCounterLatestWebSocketTargetMarketStartUtc(nowUtc);
        var targetMarketStartUtc = requestedTargetMarketStartUtc <= latestClosedTargetMarketStartUtc
            ? requestedTargetMarketStartUtc
            : latestClosedTargetMarketStartUtc;
        var requestedCounterStartMarketStartUtc = counterStartOverrideUtc ?? (resetAtUtcDayStart
            ? GetDiffCounterUtcDayStartMarketStartUtc(referenceMarketStartUtc)
            : referenceMarketStartUtc);
        DiffCounterHistoryFetchFailure? fetchFailure = null;
        DiffCounterSnapshot snapshot;
        await diffCounterStateLock.WaitAsync(cancellationToken);
        try
        {
            var states = resetAtUtcDayStart
                ? diffCounterStates
                : shiftDiffCount > 0
                    ? shiftDiffCounterStates
                    : adjustedDiffCounterStates;
            if (!states.TryGetValue(normalizedStateKey, out var state))
            {
                state = new DiffCounterState(normalizedAssetSymbol);
                states[normalizedStateKey] = state;
            }

            if (resetAtUtcDayStart)
            {
                state.EnsureInitializedForCounterStart(requestedCounterStartMarketStartUtc, nowUtc);
            }
            else
            {
                state.EnsureInitializedWithoutReset(requestedCounterStartMarketStartUtc, nowUtc);
            }

            if (!state.IsHistoryFetchBackoffActive(nowUtc))
            {
                DateTimeOffset? fetchStartUtc = null;
                DateTimeOffset? fetchEndUtc = null;

                var counterStartMarketStartUtc = state.CounterStartMarketStartUtc ?? requestedCounterStartMarketStartUtc;
                var catchUpStartUtc = state.HighWaterMarketStartUtc.GetValueOrDefault(counterStartMarketStartUtc.AddMinutes(-5)).AddMinutes(5);
                if (catchUpStartUtc < counterStartMarketStartUtc)
                {
                    catchUpStartUtc = counterStartMarketStartUtc;
                }

                if (targetMarketStartUtc >= counterStartMarketStartUtc &&
                    catchUpStartUtc <= targetMarketStartUtc)
                {
                    fetchStartUtc = catchUpStartUtc;
                    fetchEndUtc = targetMarketStartUtc;
                }

                if (fetchStartUtc is { } startUtc && fetchEndUtc is { } endUtc)
                {
                    try
                    {
                        var results = await FetchDiffCounterMarketResultsAsync(
                            normalizedAssetSymbol,
                            startUtc,
                            endUtc,
                            cancellationToken);
                        state.Apply(results);
                        state.MarkHistoryFetchSucceeded();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var retryAfterUtc = nowUtc.Add(DiffCounterHistoryFetchFailureBackoff);
                        state.MarkHistoryFetchFailed(nowUtc, retryAfterUtc, ex.Message);
                        fetchFailure = new DiffCounterHistoryFetchFailure(
                            normalizedAssetSymbol,
                            startUtc,
                            endUtc,
                            retryAfterUtc,
                            ex.Message,
                            ex);
                    }
                }
            }

            snapshot = state.ToSnapshot(targetMarketStartUtc, nowUtc, shiftDiffCount);
        }
        finally
        {
            diffCounterStateLock.Release();
        }

        if (fetchFailure is not null)
        {
            logger.LogWarning(
                fetchFailure.Exception,
                "BTC Up or Down 5m Diff counter WebSocket result fetch failed. Asset={AssetSymbol} StartUtc={StartUtc} EndUtc={EndUtc} RetryAfterUtc={RetryAfterUtc}",
                fetchFailure.AssetSymbol,
                fetchFailure.StartTimeMinUtc,
                fetchFailure.StartTimeMaxUtc,
                fetchFailure.RetryAfterUtc);
            await TryRecordApiErrorAsync("GetDiffCounterWebSocketResults", fetchFailure.ErrorMessage, cancellationToken);
        }

        return snapshot;
    }

    private async Task<IReadOnlyList<DiffCounterMarketResult>> FetchDiffCounterMarketResultsAsync(
        string assetSymbol,
        DateTimeOffset startTimeMinUtc,
        DateTimeOffset startTimeMaxUtc,
        CancellationToken cancellationToken)
    {
        if (startTimeMaxUtc < startTimeMinUtc)
        {
            return [];
        }

        var resolvedMarkets = await repository.GetCryptoUpDown5mWebSocketResolvedMarketsAsync(
            [assetSymbol],
            startTimeMinUtc,
            startTimeMaxUtc,
            cancellationToken);

        return resolvedMarkets
            .Where(IsAcceptedResolvedMarketLedgerResult)
            .Where(result => string.Equals(result.WinningOutcome, "Up", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.WinningOutcome, "Down", StringComparison.OrdinalIgnoreCase))
            .GroupBy(result => result.MarketStartUtc)
            .Select(group => group
                .OrderByDescending(result => !string.IsNullOrWhiteSpace(result.MarketId))
                .ThenBy(result => result.MarketSlug, StringComparer.OrdinalIgnoreCase)
                .First())
            .Select(result => new DiffCounterMarketResult(
                result.MarketId,
                result.ConditionId,
                result.MarketSlug,
                result.MarketStartUtc,
                result.MarketEndUtc,
                string.Equals(result.WinningOutcome, "Up", StringComparison.OrdinalIgnoreCase) ? "Up" : "Down",
                result.Source))
            .OrderBy(result => result.MarketStartUtc)
            .ToArray();
    }

    private static bool IsDiffCounterAcceptedResultSource(string? source)
    {
        return IsAcceptedResolvedMarketLedgerSource(source);
    }

    private static bool IsAcceptedResolvedMarketLedgerResult(CryptoUpDown5mWebSocketResolvedMarket result)
    {
        return IsAcceptedResolvedMarketLedgerSource(result.Source) &&
            (string.Equals(result.WinningOutcome, "Up", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.WinningOutcome, "Down", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAcceptedResolvedMarketLedgerSource(string? source)
    {
        return string.Equals(source, DiffCounterWebSocketResultSource, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, DiffCounterReferenceStartEndResultSource, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, DiffCounterBinanceTimedCloseResultSource, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, DiffCounterTerminalOrderBookResultSource, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, DiffCounterGammaClosedMarketResultSource, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAssetSymbol(string assetSymbol)
    {
        return assetSymbol.Trim().ToUpperInvariant();
    }

    private static string NormalizeUpDownOutcome(string outcome)
    {
        return string.Equals(outcome, "Up", StringComparison.OrdinalIgnoreCase) ? "Up" : "Down";
    }

    private static string? NormalizeNullableUpDownOutcome(string? outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome))
        {
            return null;
        }

        return NormalizeUpDownOutcome(outcome);
    }

    private static string GetDiffCounterSeriesSlug(string assetSymbol)
    {
        var normalizedAssetSymbol = assetSymbol.Trim().ToLowerInvariant();
        return normalizedAssetSymbol + "-up-or-down-5m";
    }

    private static bool IsDiffCounterMarketForAsset(PolymarketGammaMarket market, string assetSymbol)
    {
        var normalizedAssetSymbol = assetSymbol.Trim().ToUpperInvariant();
        if (string.Equals(normalizedAssetSymbol, "BTC", StringComparison.OrdinalIgnoreCase))
        {
            return BtcUpDown5mMarketAnalyzer.GetMarketInterval(market) == BtcUpDownMarketInterval.FiveMinutes;
        }

        var allowedAssets = new HashSet<string>([normalizedAssetSymbol], StringComparer.OrdinalIgnoreCase);
        return CryptoUpDown5mMarketAnalyzer.TryGetAssetSymbol(market, allowedAssets, out var marketAssetSymbol) &&
            string.Equals(marketAssetSymbol, normalizedAssetSymbol, StringComparison.OrdinalIgnoreCase) &&
            CryptoUpDown5mMarketAnalyzer.GetMarketInterval(market) == BtcUpDownMarketInterval.FiveMinutes;
    }

    private static DateTimeOffset? GetDiffCounterMarketWindowStartUtc(PolymarketGammaMarket market, string assetSymbol)
    {
        return string.Equals(assetSymbol, "BTC", StringComparison.OrdinalIgnoreCase)
            ? BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market)
            : CryptoUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
    }

    private static DateTimeOffset GetDiffCounterReferenceMarketStartUtc(DateTimeOffset nowUtc)
    {
        var unixSeconds = nowUtc.ToUnixTimeSeconds();
        var floorUnixSeconds = unixSeconds - (unixSeconds % 300);
        return DateTimeOffset.FromUnixTimeSeconds(floorUnixSeconds);
    }

    private static DateTimeOffset GetDiffCounterUtcDayStartMarketStartUtc(DateTimeOffset referenceMarketStartUtc)
    {
        var utc = referenceMarketStartUtc.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset GetDiffCounterLatestWebSocketTargetMarketStartUtc(DateTimeOffset nowUtc)
    {
        return GetDiffCounterReferenceMarketStartUtc(nowUtc).AddMinutes(-5);
    }

    private static string? GetDiffCounterHistoryUnavailableReason(
        DiffCounterSnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        var fetchBackoffActive = snapshot.HistoryFetchRetryAfterUtc is { } retryAfterUtc &&
            retryAfterUtc > nowUtc;
        if (!snapshot.Initialized)
        {
            return fetchBackoffActive
                ? "diff_counter_history_fetch_backoff"
                : "diff_counter_history_missing";
        }

        if (snapshot.CounterStartMarketStartUtc is { } counterStartMarketStartUtc &&
            snapshot.TargetMarketStartUtc < counterStartMarketStartUtc)
        {
            return null;
        }

        if (snapshot.HighWaterMarketStartUtc is null ||
            snapshot.HighWaterMarketStartUtc < snapshot.TargetMarketStartUtc)
        {
            if (fetchBackoffActive)
            {
                return "diff_counter_history_fetch_backoff";
            }

            return !snapshot.TargetMarketResultReceived
                ? "diff_counter_previous_market_resolved_event_missing"
                : "diff_counter_history_stale";
        }

        if (!snapshot.TargetMarketResultReceived)
        {
            return "diff_counter_previous_market_resolved_event_missing";
        }

        return null;
    }

    private static bool TryGetDiffCounterWinningOutcome(PolymarketGammaMarket market, out string winningOutcome)
    {
        winningOutcome = string.Empty;
        var quotes = BtcUpDown5mMarketAnalyzer.GetOutcomeQuotes(market);
        if (quotes.Count != 2)
        {
            return false;
        }

        var maxPrice = quotes.Max(quote => quote.Price);
        if (maxPrice <= CloseBookResultThreshold)
        {
            return false;
        }

        var winners = quotes
            .Where(quote => quote.Price == maxPrice)
            .ToArray();
        if (winners.Length != 1)
        {
            return false;
        }

        if (string.Equals(winners[0].Outcome, "Up", StringComparison.OrdinalIgnoreCase))
        {
            winningOutcome = "Up";
            return true;
        }

        if (string.Equals(winners[0].Outcome, "Down", StringComparison.OrdinalIgnoreCase))
        {
            winningOutcome = "Down";
            return true;
        }

        return false;
    }

    private static IReadOnlyList<BtcUpDown5mOddsTick> SelectPreviousScoreCounterTrendTickGroup(
        IReadOnlyList<BtcUpDown5mOddsTick> ticks,
        DateTimeOffset currentMarketStartUtc)
    {
        var expectedPreviousEndUtc = currentMarketStartUtc;
        var matchingEndTicks = ticks
            .Where(tick => Math.Abs((tick.MarketEndUtc - expectedPreviousEndUtc).TotalSeconds) <= 2)
            .ToArray();
        var candidates = matchingEndTicks.Length > 0 ? matchingEndTicks : ticks;
        return candidates
            .GroupBy(tick => tick.MarketId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(tick => tick.SampledAtUtc))
            .First()
            .OrderBy(tick => tick.SampledAtUtc)
            .ThenBy(tick => tick.CreatedAtUtc)
            .ToArray();
    }

    private BtcPreviousScoreCounterTrendSignal CalculatePreviousScoreCounterTrendSignal(
        string reasonPrefix,
        IReadOnlyList<BtcUpDown5mOddsTick> ticks,
        DateTimeOffset previousMarketStartUtc,
        DateTimeOffset previousMarketEndUtc,
        bool useCounterTrend,
        decimal? startPriceOverride = null,
        int? rawSampleCountOverride = null)
    {
        var previousMarketId = ticks.FirstOrDefault()?.MarketId;
        var previousMarketSlug = ticks.FirstOrDefault()?.MarketSlug;
        var rawSampleCount = rawSampleCountOverride ?? ticks.Count;
        var samples = ticks
            .Where(tick => tick.SampledAtUtc >= previousMarketStartUtc &&
                tick.SampledAtUtc <= previousMarketEndUtc &&
                tick.BinancePriceUsd > 0m)
            .OrderBy(tick => tick.SampledAtUtc)
            .ThenBy(tick => tick.CreatedAtUtc)
            .ToArray();
        if (samples.Length < options.PreviousScoreCounterTrendMinSamples)
        {
            return BtcPreviousScoreCounterTrendSignal.Reject(
                reasonPrefix + "_samples_insufficient",
                PreviousMarketId: previousMarketId,
                PreviousMarketSlug: previousMarketSlug,
                PreviousMarketStartUtc: previousMarketStartUtc,
                PreviousMarketEndUtc: previousMarketEndUtc,
                RawSampleCount: rawSampleCount,
                ValidSampleCount: samples.Length);
        }

        var startPrice = startPriceOverride is > 0m
            ? startPriceOverride.Value
            : samples
                .Select(sample => sample.BinanceStartPriceUsd)
                .FirstOrDefault(price => price > 0m);
        if (startPrice <= 0m)
        {
            return BtcPreviousScoreCounterTrendSignal.Reject(
                reasonPrefix + "_start_price_missing",
                PreviousMarketId: previousMarketId,
                PreviousMarketSlug: previousMarketSlug,
                PreviousMarketStartUtc: previousMarketStartUtc,
                PreviousMarketEndUtc: previousMarketEndUtc,
                RawSampleCount: rawSampleCount,
                ValidSampleCount: samples.Length);
        }

        var segments = new List<BtcPreviousScoreCounterTrendSegment>(samples.Length);
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];
            var durationEndUtc = index + 1 < samples.Length
                ? samples[index + 1].SampledAtUtc
                : previousMarketEndUtc;
            var durationSeconds = ToDecimalSeconds(durationEndUtc - sample.SampledAtUtc);
            if (durationSeconds <= 0m)
            {
                continue;
            }

            var deviation = (sample.BinancePriceUsd - startPrice) / startPrice;
            segments.Add(new BtcPreviousScoreCounterTrendSegment(deviation, durationSeconds));
        }

        var totalDurationSeconds = segments.Sum(segment => segment.DurationSeconds);
        if (totalDurationSeconds <= 0m)
        {
            return BtcPreviousScoreCounterTrendSignal.Reject(
                reasonPrefix + "_duration_missing",
                PreviousMarketId: previousMarketId,
                PreviousMarketSlug: previousMarketSlug,
                PreviousMarketStartUtc: previousMarketStartUtc,
                PreviousMarketEndUtc: previousMarketEndUtc,
                StartPriceUsd: startPrice,
                RawSampleCount: rawSampleCount,
                ValidSampleCount: samples.Length,
                SegmentCount: segments.Count);
        }

        var sortedDeviations = segments
            .Select(segment => segment.Deviation)
            .OrderBy(deviation => deviation)
            .ToArray();
        var winsorPercent = Math.Min(0.499999m, Math.Max(0m, options.PreviousScoreCounterTrendWinsorPercent));
        var lowerBound = Percentile(sortedDeviations, winsorPercent);
        var upperBound = Percentile(sortedDeviations, 1m - winsorPercent);
        var weightedDeviationSum = segments.Sum(segment =>
            Clamp(segment.Deviation, lowerBound, upperBound) * segment.DurationSeconds);
        var score = weightedDeviationSum / totalDurationSeconds;
        var upDuration = segments
            .Where(segment => segment.Deviation > 0m)
            .Sum(segment => segment.DurationSeconds);
        var downDuration = segments
            .Where(segment => segment.Deviation < 0m)
            .Sum(segment => segment.DurationSeconds);
        var upTimeShare = upDuration / totalDurationSeconds;
        var downTimeShare = downDuration / totalDurationSeconds;
        var epsilon = Math.Max(0m, options.PreviousScoreCounterTrendEpsilonScore);

        BtcPriceDirection? previousBias = null;
        string? rejectionReason = null;
        if (score > epsilon)
        {
            previousBias = BtcPriceDirection.Up;
            if (options.PreviousScoreCounterTrendEnableTimeShareFilter &&
                upTimeShare < options.PreviousScoreCounterTrendMinUpTimeShare)
            {
                rejectionReason = reasonPrefix + "_up_time_share_below_threshold";
            }
        }
        else if (score < -epsilon)
        {
            previousBias = BtcPriceDirection.Down;
            if (options.PreviousScoreCounterTrendEnableTimeShareFilter &&
                downTimeShare < options.PreviousScoreCounterTrendMinDownTimeShare)
            {
                rejectionReason = reasonPrefix + "_down_time_share_below_threshold";
            }
        }
        else
        {
            rejectionReason = reasonPrefix + "_neutral";
        }

        var selectedDirection = rejectionReason is null && previousBias is { } bias
            ? (useCounterTrend ? InvertDirection(bias) : bias)
            : (BtcPriceDirection?)null;
        return new BtcPreviousScoreCounterTrendSignal(
            rejectionReason is null && selectedDirection is not null,
            rejectionReason,
            previousBias,
            selectedDirection,
            score,
            startPrice,
            rawSampleCount,
            samples.Length,
            segments.Count,
            totalDurationSeconds,
            lowerBound,
            upperBound,
            upTimeShare,
            downTimeShare,
            previousMarketId,
            previousMarketSlug,
            previousMarketStartUtc,
            previousMarketEndUtc);
    }

    private static BtcPreviousMarketMoveSignal CalculateSkipPreviousResultBpsSignal(
        IReadOnlyList<BtcUpDown5mOddsTick> ticks,
        DateTimeOffset previousMarketStartUtc,
        DateTimeOffset previousMarketEndUtc,
        decimal minMoveBps)
    {
        var previousMarketId = ticks.FirstOrDefault()?.MarketId;
        var previousMarketSlug = ticks.FirstOrDefault()?.MarketSlug;
        var samples = ticks
            .Where(tick => tick.SampledAtUtc >= previousMarketStartUtc &&
                tick.SampledAtUtc <= previousMarketEndUtc &&
                tick.BinancePriceUsd > 0m)
            .OrderBy(tick => tick.SampledAtUtc)
            .ThenBy(tick => tick.CreatedAtUtc)
            .ToArray();
        if (samples.Length == 0)
        {
            return BtcPreviousMarketMoveSignal.Reject(
                "btc_previous_market_btc_samples_missing",
                previousMarketStartUtc,
                previousMarketEndUtc,
                minMoveBps,
                PreviousMarketId: previousMarketId,
                PreviousMarketSlug: previousMarketSlug,
                RawSampleCount: ticks.Count);
        }

        var startPrice = samples
            .Select(sample => sample.BinanceStartPriceUsd)
            .FirstOrDefault(price => price > 0m);
        if (startPrice <= 0m)
        {
            return BtcPreviousMarketMoveSignal.Reject(
                "btc_previous_market_start_price_missing",
                previousMarketStartUtc,
                previousMarketEndUtc,
                minMoveBps,
                PreviousMarketId: previousMarketId,
                PreviousMarketSlug: previousMarketSlug,
                RawSampleCount: ticks.Count,
                ValidSampleCount: samples.Length);
        }

        var endSampleMinUtc = previousMarketEndUtc.AddSeconds(-SkipPreviousResultEndPriceMaxAgeSeconds);
        var endSample = samples
            .Where(sample => sample.SampledAtUtc >= endSampleMinUtc && sample.SampledAtUtc <= previousMarketEndUtc)
            .OrderByDescending(sample => sample.SampledAtUtc)
            .ThenByDescending(sample => sample.CreatedAtUtc)
            .FirstOrDefault();
        if (endSample is null)
        {
            return BtcPreviousMarketMoveSignal.Reject(
                "btc_previous_market_end_price_missing",
                previousMarketStartUtc,
                previousMarketEndUtc,
                minMoveBps,
                PreviousMarketId: previousMarketId,
                PreviousMarketSlug: previousMarketSlug,
                RawSampleCount: ticks.Count,
                ValidSampleCount: samples.Length,
                StartPriceUsd: startPrice);
        }

        var endPrice = endSample.BinancePriceUsd;
        var moveUsd = endPrice - startPrice;
        var moveBps = startPrice == 0m ? 0m : moveUsd / startPrice * 10_000m;
        var absMoveBps = Math.Abs(moveBps);
        var endSampleAgeSeconds = ToDecimalSeconds(previousMarketEndUtc - endSample.SampledAtUtc);
        if (absMoveBps < minMoveBps)
        {
            return BtcPreviousMarketMoveSignal.Reject(
                "btc_previous_market_move_below_bps_threshold",
                previousMarketStartUtc,
                previousMarketEndUtc,
                minMoveBps,
                PreviousMarketId: previousMarketId,
                PreviousMarketSlug: previousMarketSlug,
                RawSampleCount: ticks.Count,
                ValidSampleCount: samples.Length,
                EndSampledAtUtc: endSample.SampledAtUtc,
                EndSampleAgeSeconds: endSampleAgeSeconds,
                StartPriceUsd: startPrice,
                EndPriceUsd: endPrice,
                MoveUsd: moveUsd,
                MoveBps: moveBps,
                AbsMoveBps: absMoveBps);
        }

        return new BtcPreviousMarketMoveSignal(
            true,
            null,
            previousMarketId,
            previousMarketSlug,
            previousMarketStartUtc,
            previousMarketEndUtc,
            minMoveBps,
            ticks.Count,
            samples.Length,
            endSample.SampledAtUtc,
            endSampleAgeSeconds,
            startPrice,
            endPrice,
            moveUsd,
            moveBps,
            absMoveBps);
    }

    private static BtcPreviousMarketMoveSignal CalculatePremarketPreviousResultBpsSignal(
        IReadOnlyList<BtcUpDown5mOddsTick> ticks,
        DateTimeOffset previousMarketStartUtc,
        DateTimeOffset previousMarketEndUtc,
        DateTimeOffset resultSampleTargetUtc,
        decimal minMoveBps)
    {
        var previousMarketId = ticks.FirstOrDefault()?.MarketId;
        var previousMarketSlug = ticks.FirstOrDefault()?.MarketSlug;
        var samples = ticks
            .Where(tick => tick.SampledAtUtc >= previousMarketStartUtc &&
                tick.SampledAtUtc <= resultSampleTargetUtc &&
                tick.BinancePriceUsd > 0m)
            .OrderBy(tick => tick.SampledAtUtc)
            .ThenBy(tick => tick.CreatedAtUtc)
            .ToArray();
        if (samples.Length == 0)
        {
            return BtcPreviousMarketMoveSignal.Reject(
                "premarket_previous_market_reference_samples_missing",
                previousMarketStartUtc,
                previousMarketEndUtc,
                minMoveBps,
                PreviousMarketId: previousMarketId,
                PreviousMarketSlug: previousMarketSlug,
                RawSampleCount: ticks.Count);
        }

        var startPrice = samples
            .Select(sample => sample.BinanceStartPriceUsd)
            .FirstOrDefault(price => price > 0m);
        if (startPrice <= 0m)
        {
            return BtcPreviousMarketMoveSignal.Reject(
                "premarket_previous_market_start_price_missing",
                previousMarketStartUtc,
                previousMarketEndUtc,
                minMoveBps,
                PreviousMarketId: previousMarketId,
                PreviousMarketSlug: previousMarketSlug,
                RawSampleCount: ticks.Count,
                ValidSampleCount: samples.Length);
        }

        var endSampleMinUtc = resultSampleTargetUtc.AddSeconds(-SkipPreviousResultEndPriceMaxAgeSeconds);
        var endSample = samples
            .Where(sample => sample.SampledAtUtc >= endSampleMinUtc && sample.SampledAtUtc <= resultSampleTargetUtc)
            .OrderByDescending(sample => sample.SampledAtUtc)
            .ThenByDescending(sample => sample.CreatedAtUtc)
            .FirstOrDefault();
        if (endSample is null)
        {
            return BtcPreviousMarketMoveSignal.Reject(
                "premarket_previous_market_end_minus_30_price_missing",
                previousMarketStartUtc,
                previousMarketEndUtc,
                minMoveBps,
                PreviousMarketId: previousMarketId,
                PreviousMarketSlug: previousMarketSlug,
                RawSampleCount: ticks.Count,
                ValidSampleCount: samples.Length,
                StartPriceUsd: startPrice);
        }

        var endPrice = endSample.BinancePriceUsd;
        var moveUsd = endPrice - startPrice;
        var moveBps = startPrice == 0m ? 0m : moveUsd / startPrice * 10_000m;
        var absMoveBps = Math.Abs(moveBps);
        var endSampleAgeSeconds = ToDecimalSeconds(resultSampleTargetUtc - endSample.SampledAtUtc);
        if (absMoveBps < minMoveBps)
        {
            return BtcPreviousMarketMoveSignal.Reject(
                "btc_previous_market_move_below_bps_threshold",
                previousMarketStartUtc,
                previousMarketEndUtc,
                minMoveBps,
                PreviousMarketId: previousMarketId,
                PreviousMarketSlug: previousMarketSlug,
                RawSampleCount: ticks.Count,
                ValidSampleCount: samples.Length,
                EndSampledAtUtc: endSample.SampledAtUtc,
                EndSampleAgeSeconds: endSampleAgeSeconds,
                StartPriceUsd: startPrice,
                EndPriceUsd: endPrice,
                MoveUsd: moveUsd,
                MoveBps: moveBps,
                AbsMoveBps: absMoveBps);
        }

        return new BtcPreviousMarketMoveSignal(
            true,
            null,
            previousMarketId,
            previousMarketSlug,
            previousMarketStartUtc,
            previousMarketEndUtc,
            minMoveBps,
            ticks.Count,
            samples.Length,
            endSample.SampledAtUtc,
            endSampleAgeSeconds,
            startPrice,
            endPrice,
            moveUsd,
            moveBps,
            absMoveBps);
    }

    private static BtcSkipMarketResult CreatePremarketPreviousResult(
        BtcPreviousMarketMoveSignal signal,
        DateTimeOffset previousMarketStartUtc,
        DateTimeOffset previousMarketEndUtc,
        string winningOutcome,
        int sampleSecondsBeforeEnd,
        string source)
    {
        var upWon = string.Equals(winningOutcome, "Up", StringComparison.OrdinalIgnoreCase);
        return new BtcSkipMarketResult(
            signal.PreviousMarketId ?? string.Empty,
            string.Empty,
            signal.PreviousMarketSlug ?? string.Empty,
            previousMarketStartUtc,
            previousMarketEndUtc,
            upWon ? "Up" : "Down",
            signal.EndSampledAtUtc ?? previousMarketEndUtc.AddSeconds(-sampleSecondsBeforeEnd),
            source,
            string.Empty,
            null,
            null,
            null,
            upWon ? 1m : 0m,
            null,
            null,
            upWon ? 0m : 1m,
            upWon ? 1m : 0m);
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        return value < min ? min : value > max ? max : value;
    }

    private static decimal Percentile(IReadOnlyList<decimal> sortedValues, decimal percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0m;
        }

        var boundedPercentile = Math.Min(1m, Math.Max(0m, percentile));
        var position = (double)boundedPercentile * (sortedValues.Count - 1);
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);
        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        var fraction = (decimal)(position - lowerIndex);
        return sortedValues[lowerIndex] +
            ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction);
    }

    private async Task<BtcOpeningLimitDecision> GetBinanceStartRelativeEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        BtcCurrentPriceLookupCache currentPrices,
        CancellationToken cancellationToken)
    {
        var referenceAssetSymbol = GetReferenceAssetSymbol(variant);
        var startPrice = IsBtcReferenceVariant(variant)
            ? await repository.GetBtcUpDown5mOddsStartPriceAsync(market.MarketId, cancellationToken)
            : await repository.GetCryptoUpDown5mOddsStartPriceAsync(referenceAssetSymbol, market.MarketId, cancellationToken);
        var marketStartPriceMissingReason = IsBtcReferenceVariant(variant)
            ? "btc_market_start_price_missing"
            : "crypto_market_start_price_missing";
        var referenceFetchFailedReason = IsBtcReferenceVariant(variant)
            ? "btc_reference_fetch_failed"
            : "crypto_reference_fetch_failed";
        var referenceEqualStartReason = IsBtcReferenceVariant(variant)
            ? "btc_reference_equal_market_start"
            : "crypto_reference_equal_market_start";
        var referenceBelowThresholdReason = IsBtcReferenceVariant(variant)
            ? "btc_reference_move_below_bps_threshold"
            : "crypto_reference_move_below_bps_threshold";
        if (startPrice is not > 0m)
        {
            return BtcOpeningLimitDecision.Reject(
                marketStartPriceMissingReason,
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
                    reason: marketStartPriceMissingReason));
        }

        var currentPriceLookup = await GetStartRelativeCurrentPriceAsync(market, variant, currentPrices, cancellationToken);
        if (currentPriceLookup.Price is not { } currentPrice)
        {
            return BtcOpeningLimitDecision.Reject(
                referenceFetchFailedReason,
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
                    reason: referenceFetchFailedReason));
        }

        var baseSelectedDirection = ResolveStartRelativeDirection(currentPrice.PriceUsd, startPrice.Value);
        if (baseSelectedDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                referenceEqualStartReason,
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
                    reason: referenceEqualStartReason));
        }

        var selectedDirection = baseSelectedDirection.Value;
        if (GetBinanceStartRelativeMinMoveBps(variant) is { } minMoveBps)
        {
            var moveBps = Math.Abs((currentPrice.PriceUsd - startPrice.Value) / startPrice.Value * 10_000m);
            if (moveBps < minMoveBps)
            {
                return BtcOpeningLimitDecision.Reject(
                    referenceBelowThresholdReason,
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
                        reason: referenceBelowThresholdReason));
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
        BtcCurrentPriceLookupCache currentPrices,
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
        BtcCurrentPriceLookupCache currentPrices,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<BtcPreviousMarketMoveSignal>>> skipBpsStreakMoveSignalTasks,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<DiffReferenceAverageMarketResultsLookup>>> diffReferenceAverageResultTasks,
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
                skipBpsStreakMoveSignalTasks,
                diffReferenceAverageResultTasks,
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
        BtcCurrentPriceLookupCache currentPrices,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<BtcPreviousMarketMoveSignal>>> skipBpsStreakMoveSignalTasks,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<DiffReferenceAverageMarketResultsLookup>>> diffReferenceAverageResultTasks,
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
                skipBpsStreakMoveSignalTasks,
                diffReferenceAverageResultTasks,
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
        BtcCurrentPriceLookupCache currentPrices,
        CancellationToken cancellationToken)
    {
        var isBtcReference = IsBtcReferenceVariant(variant);
        var referenceReasonPrefix = isBtcReference ? "btc_reference" : "crypto_reference";
        var snapshot = isBtcReference
            ? btcUsdReferencePriceCache.Snapshot
            : cryptoReferencePriceClient.GetSnapshot(GetReferenceAssetSymbol(variant));
        var requiredReferenceSamples = Math.Max(1, variant.DecisionDepth);
        if (snapshot.Samples.Count < requiredReferenceSamples)
        {
            var reason = referenceReasonPrefix + "_samples_insufficient";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildMiddleReferenceRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot,
                    referenceMeanUsd: null,
                    currentPrice: null,
                    requiredReferenceSamples,
                    snapshot.Samples.Take(requiredReferenceSamples).ToArray(),
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason));
        }

        var referenceSamples = snapshot.Samples.Take(requiredReferenceSamples).ToArray();
        var meanUsd = referenceSamples.Sum(sample => sample.PriceUsd) / referenceSamples.Length;
        if (meanUsd <= 0m)
        {
            var reason = referenceReasonPrefix + "_mean_missing";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildMiddleReferenceRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot,
                    referenceMeanUsd: null,
                    currentPrice: null,
                    requiredReferenceSamples,
                    referenceSamples,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason));
        }

        var currentPriceLookup = isBtcReference
            ? await GetBtcCurrentPriceAsync(market, currentPrices, cancellationToken)
            : await GetCryptoCurrentPriceAsync(market, variant, currentPrices, cancellationToken);
        if (currentPriceLookup.Price is not { } currentPrice)
        {
            var reason = referenceReasonPrefix + "_fetch_failed";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildMiddleReferenceRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot,
                    meanUsd,
                    currentPrice: null,
                    requiredReferenceSamples,
                    referenceSamples,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason));
        }

        var comparedPrices = new[] { currentPrice.PriceUsd };
        var baseSelectedDirection = ResolveMeanReversionDirection(comparedPrices, meanUsd);
        if (baseSelectedDirection is null)
        {
            var reason = currentPrice.PriceUsd == meanUsd
                ? referenceReasonPrefix + "_equal_mean"
                : referenceReasonPrefix + "_mixed_around_mean";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildMiddleReferenceRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    snapshot,
                    meanUsd,
                    currentPrice,
                    requiredReferenceSamples,
                    referenceSamples,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason));
        }

        if (variant.DecisionThresholdBps is > 0m)
        {
            var minAbsMoveFromMeanBps = GetMinimumAbsMeanDeviationBps(comparedPrices, meanUsd);
            if (minAbsMoveFromMeanBps < variant.DecisionThresholdBps.Value)
            {
                var reason = referenceReasonPrefix + "_mean_deviation_below_threshold";
                return BtcOpeningLimitDecision.Reject(
                    reason,
                    BuildMiddleReferenceRawDecisionJson(
                        market,
                        variant,
                        stakeUsd,
                        nowUtc,
                        snapshot,
                        meanUsd,
                        currentPrice,
                        requiredReferenceSamples,
                        referenceSamples,
                        baseSelectedDirection,
                        selectedDirection: null,
                        selectedOutcome: null,
                        reason));
            }
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
                    meanUsd,
                    currentPrice,
                    requiredReferenceSamples,
                    referenceSamples,
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
                meanUsd,
                currentPrice,
                requiredReferenceSamples,
                referenceSamples,
                baseSelectedDirection,
                selectedDirection,
                selectedOutcome,
                reason: null));
    }

    private async Task<BtcOpeningLimitDecision> GetReferenceAverageBpsThresholdEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        BtcCurrentPriceLookupCache currentPrices,
        CancellationToken cancellationToken)
    {
        var referenceAssetSymbol = GetReferenceAssetSymbol(variant);
        var averages = cryptoReferencePriceAverageProvider
            .GetAssetAverages(referenceAssetSymbol)
            .Where(average => average.IsFullWindow && average.AveragePriceUsd is > 0m)
            .OrderByDescending(average => average.AveragePriceUsd.GetValueOrDefault())
            .ThenByDescending(average => average.WindowSeconds)
            .ToArray();
        if (averages.Length == 0)
        {
            const string reason = "reference_average_full_window_missing";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildReferenceAverageBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice: null,
                    averages,
                    selectedAverage: null,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveFromAverageBps: null,
                    reason));
        }

        var selectedAverage = averages[0];
        var currentPriceLookup = IsBtcReferenceVariant(variant)
            ? await GetBtcCurrentPriceAsync(market, currentPrices, cancellationToken)
            : await GetCryptoCurrentPriceAsync(market, variant, currentPrices, cancellationToken);
        if (currentPriceLookup.Price is not { } currentPrice)
        {
            var reason = IsBtcReferenceVariant(variant)
                ? "btc_reference_fetch_failed"
                : "crypto_reference_fetch_failed";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildReferenceAverageBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice: null,
                    averages,
                    selectedAverage,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveFromAverageBps: null,
                    reason));
        }

        var averagePriceUsd = selectedAverage.AveragePriceUsd.GetValueOrDefault();
        var moveFromAverageBps = GetMeanDeviationBps(currentPrice.PriceUsd, averagePriceUsd);
        var thresholdBps = GetReferenceAverageMinMoveBps(variant);
        var configuredTriggerDirection = GetReferenceAverageTriggerDirection(variant);
        var triggerDirection = configuredTriggerDirection ?? (moveFromAverageBps switch
        {
            > 0m => BtcPriceDirection.Up,
            < 0m => BtcPriceDirection.Down,
            _ => null
        });
        if (triggerDirection is null)
        {
            var reason = variant.DiffCounterTriggerOutcome is null
                ? "reference_average_move_below_bps_threshold"
                : "reference_average_trigger_direction_missing";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildReferenceAverageBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice,
                    averages,
                    selectedAverage,
                    triggerDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveFromAverageBps,
                    reason));
        }

        var thresholdMet = configuredTriggerDirection is null
            ? Math.Abs(moveFromAverageBps) >= thresholdBps
            : triggerDirection.Value == BtcPriceDirection.Up
            ? moveFromAverageBps >= thresholdBps
            : moveFromAverageBps <= -thresholdBps;
        if (!thresholdMet)
        {
            const string reason = "reference_average_move_below_bps_threshold";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildReferenceAverageBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice,
                    averages,
                    selectedAverage,
                    triggerDirection,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveFromAverageBps,
                    reason));
        }

        if (IsFilteredReferenceAverageBpsFakPremarketEntry(variant) &&
            IsFilteredReferenceAverageWindowSkipped(selectedAverage.WindowLabel))
        {
            const string reason = "reference_average_filtered_window_skipped";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildReferenceAverageBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice,
                    averages,
                    selectedAverage,
                    triggerDirection,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveFromAverageBps,
                    reason));
        }

        if (IsFilteredReferenceAverageBpsFakPremarketEntry(variant) &&
            IsFilteredReferenceAverageAbsMoveSkipped(Math.Abs(moveFromAverageBps)))
        {
            const string reason = "reference_average_filtered_abs_move_zone_skipped";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildReferenceAverageBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice,
                    averages,
                    selectedAverage,
                    triggerDirection,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveFromAverageBps,
                    reason));
        }

        var selectedDirection = InvertDirection(triggerDirection.Value);
        if (variant.FixedOutcome is { } fixedOutcome)
        {
            selectedDirection = fixedOutcome == BtcUpDownFixedOutcome.Up
                ? BtcPriceDirection.Up
                : BtcPriceDirection.Down;
        }

        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection);
        if (selectedOutcome is null)
        {
            const string reason = "target_outcome_not_available";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildReferenceAverageBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice,
                    averages,
                    selectedAverage,
                    triggerDirection,
                    selectedDirection,
                    selectedOutcome: null,
                    moveFromAverageBps,
                    reason));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildReferenceAverageBpsRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                currentPrice,
                averages,
                selectedAverage,
                triggerDirection,
                selectedDirection,
                selectedOutcome,
                moveFromAverageBps,
                reason: null));
    }

    private async Task<BtcOpeningLimitDecision> GetAbsoluteBpsThresholdEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        BtcCurrentPriceLookupCache currentPrices,
        CancellationToken cancellationToken)
    {
        var referenceAssetSymbol = GetReferenceAssetSymbol(variant);
        var lookbackHours = variant.DecisionDepth;
        if (lookbackHours is < 1 or > 24)
        {
            const string reason = "absolute_reference_lookback_invalid";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildAbsoluteBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice: null,
                    extrema: null,
                    selectedBoundary: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveAboveMaximumBps: null,
                    moveBelowMinimumBps: null,
                    reason));
        }

        if (variant.DecisionThresholdBps is not > 0m)
        {
            const string reason = "absolute_reference_threshold_invalid";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildAbsoluteBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice: null,
                    extrema: null,
                    selectedBoundary: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveAboveMaximumBps: null,
                    moveBelowMinimumBps: null,
                    reason));
        }

        var extrema = cryptoReferencePriceExtremaProvider.GetExtrema(referenceAssetSymbol, lookbackHours, nowUtc);
        if (extrema is null ||
            !extrema.IsFullWindow ||
            extrema.MinimumPriceUsd is not > 0m ||
            extrema.MaximumPriceUsd is not > 0m)
        {
            const string reason = "absolute_reference_full_window_missing";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildAbsoluteBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice: null,
                    extrema,
                    selectedBoundary: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveAboveMaximumBps: null,
                    moveBelowMinimumBps: null,
                    reason));
        }

        var minimumPriceUsd = extrema.MinimumPriceUsd.Value;
        var maximumPriceUsd = extrema.MaximumPriceUsd.Value;
        if (minimumPriceUsd > maximumPriceUsd)
        {
            const string reason = "absolute_reference_extrema_invalid";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildAbsoluteBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice: null,
                    extrema,
                    selectedBoundary: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveAboveMaximumBps: null,
                    moveBelowMinimumBps: null,
                    reason));
        }

        var currentPriceLookup = IsBtcReferenceVariant(variant)
            ? await GetBtcCurrentPriceAsync(market, currentPrices, cancellationToken)
            : await GetCryptoCurrentPriceAsync(market, variant, currentPrices, cancellationToken);
        if (currentPriceLookup.Price is not { } currentPrice)
        {
            var reason = IsBtcReferenceVariant(variant)
                ? "btc_reference_fetch_failed"
                : "crypto_reference_fetch_failed";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildAbsoluteBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice: null,
                    extrema,
                    selectedBoundary: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveAboveMaximumBps: null,
                    moveBelowMinimumBps: null,
                    reason));
        }

        var moveAboveMaximumBps = GetMeanDeviationBps(currentPrice.PriceUsd, maximumPriceUsd);
        var moveBelowMinimumBps = GetMeanDeviationBps(currentPrice.PriceUsd, minimumPriceUsd);
        var thresholdBps = variant.DecisionThresholdBps.Value;
        BtcPriceDirection? selectedDirection = null;
        string? selectedBoundary = null;
        if (moveAboveMaximumBps >= thresholdBps)
        {
            selectedDirection = BtcPriceDirection.Down;
            selectedBoundary = "maximum";
        }
        else if (moveBelowMinimumBps <= -thresholdBps)
        {
            selectedDirection = BtcPriceDirection.Up;
            selectedBoundary = "minimum";
        }

        if (selectedDirection is null)
        {
            const string reason = "absolute_reference_move_below_bps_threshold";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildAbsoluteBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice,
                    extrema,
                    selectedBoundary: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveAboveMaximumBps,
                    moveBelowMinimumBps,
                    reason));
        }

        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection.Value);
        if (selectedOutcome is null)
        {
            const string reason = "target_outcome_not_available";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildAbsoluteBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    currentPrice,
                    extrema,
                    selectedBoundary,
                    selectedDirection,
                    selectedOutcome: null,
                    moveAboveMaximumBps,
                    moveBelowMinimumBps,
                    reason));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildAbsoluteBpsRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                currentPrice,
                extrema,
                selectedBoundary,
                selectedDirection,
                selectedOutcome,
                moveAboveMaximumBps,
                moveBelowMinimumBps,
                reason: null));
    }

    private async Task<BtcOpeningLimitDecision> GetFuturesBasisBpsThresholdEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        var targetMarketEndUtc = GetEffectiveMarketEndUtc(market, variant, marketStartUtc);
        if (targetMarketEndUtc is null)
        {
            const string reason = "expiry_futures_target_market_end_not_available";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildFuturesBasisBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    futuresPrices: null,
                    basisBpsByExpiry: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason));
        }

        IReadOnlyList<ExpiryFuturesReferencePricePoint> futuresPrices;
        try
        {
            futuresPrices = await expiryFuturesReferencePriceClient.GetNearestExpiryPricesAsync(
                GetReferenceAssetSymbol(variant),
                targetMarketEndUtc.Value,
                FuturesBasisRequiredExpiryCount,
                cancellationToken);
            if (futuresPrices.Count != FuturesBasisRequiredExpiryCount)
            {
                throw new InvalidOperationException(
                    $"Expected exactly {FuturesBasisRequiredExpiryCount} OKX expiry futures price points but received {futuresPrices.Count}.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await TryRecordApiErrorAsync("GetNearestExpiryFuturesReferencePrices", ex.Message, cancellationToken);
            const string reason = "expiry_futures_reference_fetch_failed";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildFuturesBasisBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    futuresPrices: null,
                    basisBpsByExpiry: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason));
        }

        var basisBpsByExpiry = futuresPrices
            .Select(price => GetMeanDeviationBps(price.MidPriceUsd, price.IndexPriceUsd))
            .ToArray();
        var primaryBasisBps = basisBpsByExpiry[0];
        var thresholdBps = GetFuturesBasisMinMoveBps(variant);
        var triggerDirection = primaryBasisBps switch
        {
            var value when value >= thresholdBps => BtcPriceDirection.Up,
            var value when value <= -thresholdBps => BtcPriceDirection.Down,
            _ => (BtcPriceDirection?)null
        };
        if (triggerDirection is null)
        {
            const string reason = "futures_basis_move_below_bps_threshold";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildFuturesBasisBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    futuresPrices,
                    basisBpsByExpiry,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason));
        }

        if (!HaveMatchingFuturesBasisConfirmationSigns(basisBpsByExpiry))
        {
            const string reason = "futures_basis_confirmation_sign_mismatch";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildFuturesBasisBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    futuresPrices,
                    basisBpsByExpiry,
                    selectedDirection: null,
                    selectedOutcome: null,
                    reason));
        }

        var selectedDirection = IsFuturesBasisBpsFakPremarketRevertEntry(variant)
            ? InvertDirection(triggerDirection.Value)
            : triggerDirection.Value;
        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection);
        if (selectedOutcome is null)
        {
            const string reason = "target_outcome_not_available";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildFuturesBasisBpsRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    futuresPrices,
                    basisBpsByExpiry,
                    selectedDirection,
                    selectedOutcome: null,
                    reason));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildFuturesBasisBpsRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                futuresPrices,
                basisBpsByExpiry,
                selectedDirection,
                selectedOutcome,
                reason: null));
    }

    private async Task<BtcCurrentPriceLookupResult> GetStartRelativeCurrentPriceAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        BtcCurrentPriceLookupCache currentPrices,
        CancellationToken cancellationToken)
    {
        return IsBtcReferenceVariant(variant)
            ? await GetBtcStartRelativeCurrentPriceAsync(market, currentPrices, cancellationToken)
            : await GetCryptoCurrentPriceAsync(market, variant, currentPrices, cancellationToken);
    }

    private async Task<BtcCurrentPriceLookupResult> GetBtcStartRelativeCurrentPriceAsync(
        PolymarketGammaMarket market,
        BtcCurrentPriceLookupCache currentPrices,
        CancellationToken cancellationToken)
    {
        var cacheKey = "start_relative:" + market.MarketId;
        return await currentPrices.GetOrAddAsync(
            cacheKey,
            async lookupCancellationToken =>
            {
                var latestTick = await repository.GetLatestBtcUpDown5mOddsTickAsync(
                    market.MarketId,
                    lookupCancellationToken);
                if (latestTick is { SecondsAfterStart: > 0m })
                {
                    return BtcCurrentPriceLookupResult.Success(new BtcUsdReferencePricePoint(
                        latestTick.BinancePriceUsd,
                        latestTick.BinanceSourceUpdatedAtUtc,
                        latestTick.BinanceFetchedAtUtc,
                        "BinanceTradeWebSocketOddsArchive"));
                }

                return await GetBtcCurrentPriceAsync(market, currentPrices, lookupCancellationToken);
            },
            cancellationToken);
    }

    private async Task<BtcCurrentPriceLookupResult> GetBtcCurrentPriceAsync(
        PolymarketGammaMarket market,
        BtcCurrentPriceLookupCache currentPrices,
        CancellationToken cancellationToken)
    {
        return await currentPrices.GetOrAddAsync(
            market.MarketId,
            async lookupCancellationToken =>
            {
                try
                {
                    return BtcCurrentPriceLookupResult.Success(
                        await btcUsdReferencePriceClient.GetBtcUsdPriceAsync(lookupCancellationToken));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await TryRecordApiErrorAsync("GetBtcUsdReferencePrice", ex.Message, lookupCancellationToken);
                    return BtcCurrentPriceLookupResult.Failure(ex.Message);
                }
            },
            cancellationToken);
    }

    private async Task<BtcCurrentPriceLookupResult> GetCryptoCurrentPriceAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        BtcCurrentPriceLookupCache currentPrices,
        CancellationToken cancellationToken)
    {
        var assetSymbol = GetReferenceAssetSymbol(variant);
        var cacheKey = assetSymbol + ":" + market.MarketId;
        return await currentPrices.GetOrAddAsync(
            cacheKey,
            async lookupCancellationToken =>
            {
                try
                {
                    var price = await cryptoReferencePriceClient.GetPriceAsync(assetSymbol, lookupCancellationToken);
                    return BtcCurrentPriceLookupResult.Success(
                        new BtcUsdReferencePricePoint(
                            price.PriceUsd,
                            price.SourceUpdatedAtUtc,
                            price.FetchedAtUtc,
                            price.Source),
                        price.AssetSymbol,
                        price.BinanceSymbol);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await TryRecordApiErrorAsync("GetCryptoReferencePrice", ex.Message, lookupCancellationToken);
                    return BtcCurrentPriceLookupResult.Failure(ex.Message, assetSymbol, assetSymbol + "USDT");
                }
            },
            cancellationToken);
    }

    private async Task<IReadOnlyList<BtcUpDown5mOddsTick>> GetReferenceOddsTicksForMarketStartAsync(
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset marketStartUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        if (IsBtcReferenceVariant(variant))
        {
            return await repository.GetBtcUpDown5mOddsTicksForMarketStartAsync(
                marketStartUtc,
                limit,
                cancellationToken);
        }

        var assetSymbol = GetReferenceAssetSymbol(variant);
        var ticks = await repository.GetCryptoUpDown5mOddsTicksForMarketStartAsync(
            assetSymbol,
            marketStartUtc,
            limit,
            cancellationToken);
        return ticks
            .Select(ToReferenceOddsTick)
            .ToArray();
    }

    private async Task<IReadOnlyList<BtcUpDown5mOddsTick>> GetReferenceOddsTicksForMarketAsync(
        BtcUpDown5mStrategyVariant variant,
        string marketId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (IsBtcReferenceVariant(variant))
        {
            return await repository.GetBtcUpDown5mOddsTicksForMarketAsync(
                marketId,
                limit,
                cancellationToken);
        }

        var assetSymbol = GetReferenceAssetSymbol(variant);
        var ticks = await repository.GetCryptoUpDown5mOddsTicksForMarketAsync(
            assetSymbol,
            marketId,
            limit,
            cancellationToken);
        return ticks
            .Select(ToReferenceOddsTick)
            .ToArray();
    }

    private static BtcUpDown5mOddsTick ToReferenceOddsTick(CryptoUpDown5mOddsTick tick)
    {
        return new BtcUpDown5mOddsTick(
            tick.Id,
            tick.MarketId,
            tick.ConditionId,
            tick.MarketSlug,
            tick.MarketStartUtc,
            tick.MarketEndUtc,
            tick.SampledAtUtc,
            tick.SecondsAfterStart,
            tick.SecondsToClose,
            tick.BinancePriceUsd,
            tick.BinanceSourceUpdatedAtUtc,
            tick.BinanceFetchedAtUtc,
            tick.BinanceStartPriceUsd,
            tick.AssetMoveFromStartUsd,
            tick.AssetMoveFromStartBps,
            tick.UpAssetId,
            tick.UpBestBid,
            tick.UpBestAsk,
            tick.UpMid,
            tick.UpPriceProxy,
            tick.UpPriceProxyKind,
            tick.UpLastTradePrice,
            tick.UpBookSource,
            tick.UpBookAgeMs,
            tick.DownAssetId,
            tick.DownBestBid,
            tick.DownBestAsk,
            tick.DownMid,
            tick.DownPriceProxy,
            tick.DownPriceProxyKind,
            tick.DownLastTradePrice,
            tick.DownBookSource,
            tick.DownBookAgeMs,
            tick.DiagnosticsJson,
            tick.CreatedAtUtc);
    }

    private async Task<BtcOpeningLimitDecision> GetSkipConsecutiveMarketResultsEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var requiredResults = Math.Max(1, variant.DecisionDepth);
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
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

        var expectedMarketStarts = GetExpectedPreviousMarketStarts(
            marketStartUtc.Value,
            variant.MarketInterval,
            requiredResults);
        var closeBookLookup = await GetStrictPreviousCloseBookMarketResultsAsync(
            variant,
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
        if (GetTemporarySkipUpEntryReason(variant, selectedDirection) is { } temporarySkipUpReason)
        {
            return BtcOpeningLimitDecision.Reject(
                temporarySkipUpReason,
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
                    reason: temporarySkipUpReason));
        }

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

    private async Task<BtcOpeningLimitDecision> GetSkipPreviousResultBpsThresholdEntryDecisionAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal stakeUsd,
        DateTimeOffset nowUtc,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<BtcPreviousMarketMoveSignal>>> skipBpsStreakMoveSignalTasks,
        CancellationToken cancellationToken)
    {
        const int requiredResults = 1;
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        if (marketStartUtc is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_market_start_missing",
                BuildSkipBpsThresholdRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    requiredResults,
                    [],
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveSignal: null,
                    reason: "btc_market_start_missing"));
        }

        var minMoveBps = GetSkipPreviousResultMinMoveBps(variant) ?? 0m;
        var moveSignal = (await GetCachedSkipPreviousResultBpsStreakMoveSignalAsync(
                skipBpsStreakMoveSignalTasks,
                market,
                variant,
                marketStartUtc.Value,
                nowUtc,
                cancellationToken))
            .WithMinMoveThreshold(minMoveBps);
        var considered = moveSignal.StreakResults ?? [];
        var baseSelectedDirection = moveSignal.BaseSelectedDirection;
        if (!moveSignal.ShouldEnter)
        {
            var reason = moveSignal.RejectionReason ?? "btc_previous_market_move_rejected";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildSkipBpsThresholdRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    requiredResults,
                    considered,
                    baseSelectedDirection,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveSignal,
                    reason,
                    moveSignal.CloseBookDiagnostics));
        }

        if (baseSelectedDirection is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "btc_market_results_not_consecutive",
                BuildSkipBpsThresholdRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    requiredResults,
                    considered,
                    baseSelectedDirection: null,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveSignal,
                    reason: "btc_market_results_not_consecutive",
                    closeBookDiagnostics: moveSignal.CloseBookDiagnostics));
        }

        var fixedSelectedDirection = GetFixedOutcomePreviousResultBpsDirection(variant);
        if (fixedSelectedDirection is { } requiredDirection &&
            baseSelectedDirection.Value != requiredDirection)
        {
            const string reason = "btc_previous_market_move_fixed_outcome_mismatch";
            return BtcOpeningLimitDecision.Reject(
                reason,
                BuildSkipBpsThresholdRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    requiredResults,
                    considered,
                    baseSelectedDirection,
                    selectedDirection: null,
                    selectedOutcome: null,
                    moveSignal,
                    reason,
                    closeBookDiagnostics: moveSignal.CloseBookDiagnostics));
        }

        var selectedDirection = fixedSelectedDirection ?? baseSelectedDirection.Value;
        if (GetTemporarySkipUpEntryReason(variant, selectedDirection) is { } temporarySkipUpReason)
        {
            return BtcOpeningLimitDecision.Reject(
                temporarySkipUpReason,
                BuildSkipBpsThresholdRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    requiredResults,
                    considered,
                    baseSelectedDirection,
                    selectedDirection,
                    selectedOutcome: null,
                    moveSignal,
                    temporarySkipUpReason,
                    closeBookDiagnostics: moveSignal.CloseBookDiagnostics));
        }

        var selectedOutcome = TrySelectOutcomeForDirection(market, selectedDirection);
        if (selectedOutcome is null)
        {
            return BtcOpeningLimitDecision.Reject(
                "target_outcome_not_available",
                BuildSkipBpsThresholdRawDecisionJson(
                    market,
                    variant,
                    stakeUsd,
                    nowUtc,
                    requiredResults,
                    considered,
                    baseSelectedDirection,
                    selectedDirection,
                    selectedOutcome: null,
                    moveSignal,
                    reason: "target_outcome_not_available",
                    closeBookDiagnostics: moveSignal.CloseBookDiagnostics));
        }

        return BtcOpeningLimitDecision.Enter(
            selectedOutcome,
            BuildSkipBpsThresholdRawDecisionJson(
                market,
                variant,
                stakeUsd,
                nowUtc,
                requiredResults,
                considered,
                baseSelectedDirection,
                selectedDirection,
                selectedOutcome,
                moveSignal,
                reason: null,
                closeBookDiagnostics: moveSignal.CloseBookDiagnostics));
    }

    private Task<BtcPreviousMarketMoveSignal> GetCachedSkipPreviousResultBpsStreakMoveSignalAsync(
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<BtcPreviousMarketMoveSignal>>> signalTasks,
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset marketStartUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var cacheKey = string.Concat(
            GetReferenceAssetSymbol(variant),
            ":",
            IsFixedOutcomePreviousResultBpsFakPremarketEntry(variant) ? "premarket" : "close_book",
            ":",
            variant.MarketInterval,
            ":",
            marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        var lazy = signalTasks.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<BtcPreviousMarketMoveSignal>>(
                () => GetSkipPreviousResultBpsStreakMoveSignalAsync(market, variant, marketStartUtc, nowUtc, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private async Task<BtcPreviousMarketMoveSignal> GetSkipPreviousResultBpsStreakMoveSignalAsync(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset marketStartUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var signal = await CalculateSkipPreviousResultBpsStreakMoveSignalAsync(
            variant,
            marketStartUtc,
            nowUtc,
            cancellationToken);
        await TryRecordBtcUpDown5mResultStreakDiagnosticAsync(
            market,
            marketStartUtc,
            nowUtc,
            signal,
            cancellationToken);
        return signal;
    }

    private async Task<BtcPreviousMarketMoveSignal> CalculateSkipPreviousResultBpsStreakMoveSignalAsync(
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset marketStartUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (IsFixedOutcomePreviousResultBpsFakPremarketEntry(variant))
        {
            return await CalculatePremarketPreviousResultBpsMoveSignalAsync(
                variant,
                marketStartUtc,
                cancellationToken);
        }

        var expectedMarketStarts = GetExpectedPreviousMarketStarts(
            marketStartUtc,
            variant.MarketInterval,
            SkipPreviousResultBpsMaxStreakMarkets);
        var previousMarketStartUtc = expectedMarketStarts[0];
        var previousMarketEndUtc = marketStartUtc;
        var closeBookLookup = await GetStrictPreviousCloseBookMarketResultsAsync(
            variant,
            expectedMarketStarts,
            nowUtc,
            cancellationToken);
        if (closeBookLookup.Results.Count == 0)
        {
            var reason = closeBookLookup.HasOrderBookUnavailable
                ? "btc_previous_close_book_orderbook_unavailable"
                : "btc_previous_close_book_result_missing";
            return BtcPreviousMarketMoveSignal.Reject(
                reason,
                previousMarketStartUtc,
                previousMarketEndUtc,
                MinMoveBps: 0m,
                CloseBookDiagnostics: closeBookLookup.Diagnostics);
        }

        var firstOutcome = closeBookLookup.Results[0].WinningOutcome;
        var streakResults = closeBookLookup.Results
            .TakeWhile(result => string.Equals(result.WinningOutcome, firstOutcome, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var baseSelectedDirection = ResolveOppositeDirectionAfterConsecutiveResults(streakResults);
        if (baseSelectedDirection is null)
        {
            return BtcPreviousMarketMoveSignal.Reject(
                "btc_market_results_not_consecutive",
                previousMarketStartUtc,
                previousMarketEndUtc,
                MinMoveBps: 0m,
                StreakResults: streakResults,
                CloseBookDiagnostics: closeBookLookup.Diagnostics);
        }

        var components = new List<BtcPreviousMarketMoveComponent>(streakResults.Length);
        string? truncatedReason = null;
        foreach (var result in streakResults)
        {
            var componentStartUtc = result.MarketStartUtc ?? previousMarketStartUtc;
            var componentEndUtc = result.MarketEndUtc ??
                componentStartUtc.Add(BtcUpDown5mMarketAnalyzer.GetIntervalDuration(variant.MarketInterval));
            var ticks = await GetReferenceOddsTicksForMarketAsync(
                variant,
                result.MarketId,
                limit: 1_000,
                cancellationToken);
            if (ticks.Count == 0)
            {
                truncatedReason = "btc_previous_market_btc_samples_missing";
                if (components.Count == 0)
                {
                    return BtcPreviousMarketMoveSignal.Reject(
                        truncatedReason,
                        componentStartUtc,
                        componentEndUtc,
                        MinMoveBps: 0m,
                        PreviousMarketId: result.MarketId,
                        PreviousMarketSlug: result.MarketSlug,
                        StreakResults: streakResults,
                        CloseBookDiagnostics: closeBookLookup.Diagnostics,
                        StreakWinningOutcome: firstOutcome,
                        CloseBookStreakResultCount: streakResults.Length,
                        StreakTruncatedReason: truncatedReason,
                        BaseSelectedDirection: baseSelectedDirection);
                }

                break;
            }

            var selectedTicks = SelectPreviousScoreCounterTrendTickGroup(ticks, componentEndUtc);
            var componentSignal = CalculateSkipPreviousResultBpsSignal(
                selectedTicks,
                componentStartUtc,
                componentEndUtc,
                minMoveBps: 0m);
            if (!componentSignal.ShouldEnter)
            {
                truncatedReason = componentSignal.RejectionReason ?? "btc_previous_market_move_rejected";
                if (components.Count == 0)
                {
                    return componentSignal with
                    {
                        StreakResults = streakResults,
                        CloseBookDiagnostics = closeBookLookup.Diagnostics,
                        StreakWinningOutcome = firstOutcome,
                        CloseBookStreakResultCount = streakResults.Length,
                        StreakTruncatedReason = truncatedReason,
                        BaseSelectedDirection = baseSelectedDirection
                    };
                }

                break;
            }

            components.Add(BtcPreviousMarketMoveComponent.From(result, componentSignal));
        }

        if (components.Count == 0)
        {
            return BtcPreviousMarketMoveSignal.Reject(
                truncatedReason ?? "btc_previous_market_move_rejected",
                previousMarketStartUtc,
                previousMarketEndUtc,
                MinMoveBps: 0m,
                StreakResults: streakResults,
                CloseBookDiagnostics: closeBookLookup.Diagnostics,
                StreakWinningOutcome: firstOutcome,
                CloseBookStreakResultCount: streakResults.Length,
                StreakTruncatedReason: truncatedReason,
                BaseSelectedDirection: baseSelectedDirection);
        }

        var immediate = components[0];
        var cumulativeAbsMoveBps = components.Sum(component => component.AbsMoveBps ?? 0m);
        var cumulativeSignedMoveBps = string.Equals(firstOutcome, "Down", StringComparison.OrdinalIgnoreCase)
            ? -cumulativeAbsMoveBps
            : cumulativeAbsMoveBps;
        return new BtcPreviousMarketMoveSignal(
            true,
            null,
            immediate.MarketId,
            immediate.MarketSlug,
            immediate.MarketStartUtc,
            immediate.MarketEndUtc,
            0m,
            immediate.RawSampleCount,
            immediate.ValidSampleCount,
            immediate.EndSampledAtUtc,
            immediate.EndSampleAgeSeconds,
            immediate.StartPriceUsd,
            immediate.EndPriceUsd,
            immediate.MoveUsd,
            immediate.MoveBps,
            immediate.AbsMoveBps,
            firstOutcome,
            components.Count,
            streakResults.Length,
            cumulativeSignedMoveBps,
            cumulativeAbsMoveBps,
            components,
            streakResults,
            closeBookLookup.Diagnostics,
            truncatedReason,
            baseSelectedDirection);
    }

    private async Task<BtcPreviousMarketMoveSignal> CalculatePremarketPreviousResultBpsMoveSignalAsync(
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset marketStartUtc,
        CancellationToken cancellationToken)
    {
        var intervalDuration = BtcUpDown5mMarketAnalyzer.GetIntervalDuration(variant.MarketInterval);
        var previousMarketStartUtc = marketStartUtc.Subtract(intervalDuration);
        var previousMarketEndUtc = marketStartUtc;
        var sampleSecondsBeforeEnd = GetPremarketPreviousResultSampleSecondsBeforeEnd(variant);
        var resultSampleTargetUtc = previousMarketEndUtc.AddSeconds(-sampleSecondsBeforeEnd);
        var ticks = await GetReferenceOddsTicksForMarketStartAsync(
            variant,
            previousMarketStartUtc,
            limit: 1_000,
            cancellationToken);
        var componentSignal = CalculatePremarketPreviousResultBpsSignal(
            ticks,
            previousMarketStartUtc,
            previousMarketEndUtc,
            resultSampleTargetUtc,
            minMoveBps: 0m);
        if (!componentSignal.ShouldEnter)
        {
            return componentSignal;
        }

        var winningOutcome = componentSignal.MoveBps switch
        {
            > 0m => "Up",
            < 0m => "Down",
            _ => null
        };
        if (winningOutcome is null)
        {
            return BtcPreviousMarketMoveSignal.Reject(
                "premarket_previous_market_reference_price_unchanged",
                previousMarketStartUtc,
                previousMarketEndUtc,
                MinMoveBps: 0m,
                PreviousMarketId: componentSignal.PreviousMarketId,
                PreviousMarketSlug: componentSignal.PreviousMarketSlug,
                RawSampleCount: componentSignal.RawSampleCount,
                ValidSampleCount: componentSignal.ValidSampleCount,
                EndSampledAtUtc: componentSignal.EndSampledAtUtc,
                EndSampleAgeSeconds: componentSignal.EndSampleAgeSeconds,
                StartPriceUsd: componentSignal.StartPriceUsd,
                EndPriceUsd: componentSignal.EndPriceUsd,
                MoveUsd: componentSignal.MoveUsd,
                MoveBps: componentSignal.MoveBps,
                AbsMoveBps: componentSignal.AbsMoveBps);
        }

        var result = CreatePremarketPreviousResult(
            componentSignal,
            previousMarketStartUtc,
            previousMarketEndUtc,
            winningOutcome,
            sampleSecondsBeforeEnd,
            GetPremarketPreviousResultSource(variant));
        var streakResults = new[] { result };
        var baseSelectedDirection = ResolveOppositeDirectionAfterConsecutiveResults(streakResults);
        if (baseSelectedDirection is null)
        {
            return BtcPreviousMarketMoveSignal.Reject(
                "btc_market_results_not_consecutive",
                previousMarketStartUtc,
                previousMarketEndUtc,
                MinMoveBps: 0m,
                PreviousMarketId: componentSignal.PreviousMarketId,
                PreviousMarketSlug: componentSignal.PreviousMarketSlug,
                RawSampleCount: componentSignal.RawSampleCount,
                ValidSampleCount: componentSignal.ValidSampleCount,
                EndSampledAtUtc: componentSignal.EndSampledAtUtc,
                EndSampleAgeSeconds: componentSignal.EndSampleAgeSeconds,
                StartPriceUsd: componentSignal.StartPriceUsd,
                EndPriceUsd: componentSignal.EndPriceUsd,
                MoveUsd: componentSignal.MoveUsd,
                MoveBps: componentSignal.MoveBps,
                AbsMoveBps: componentSignal.AbsMoveBps,
                StreakResults: streakResults,
                StreakWinningOutcome: winningOutcome,
                CloseBookStreakResultCount: streakResults.Length);
        }

        var component = BtcPreviousMarketMoveComponent.From(result, componentSignal);
        return new BtcPreviousMarketMoveSignal(
            true,
            null,
            componentSignal.PreviousMarketId,
            componentSignal.PreviousMarketSlug,
            previousMarketStartUtc,
            previousMarketEndUtc,
            0m,
            componentSignal.RawSampleCount,
            componentSignal.ValidSampleCount,
            componentSignal.EndSampledAtUtc,
            componentSignal.EndSampleAgeSeconds,
            componentSignal.StartPriceUsd,
            componentSignal.EndPriceUsd,
            componentSignal.MoveUsd,
            componentSignal.MoveBps,
            componentSignal.AbsMoveBps,
            winningOutcome,
            1,
            1,
            componentSignal.MoveBps,
            componentSignal.AbsMoveBps,
            [component],
            streakResults,
            CloseBookDiagnostics: null,
            StreakTruncatedReason: null,
            baseSelectedDirection);
    }

    private async Task TryRecordBtcUpDown5mResultStreakDiagnosticAsync(
        PolymarketGammaMarket market,
        DateTimeOffset marketStartUtc,
        DateTimeOffset nowUtc,
        BtcPreviousMarketMoveSignal signal,
        CancellationToken cancellationToken)
    {
        try
        {
            var diagnostic = BuildBtcUpDown5mResultStreakDiagnostic(
                market,
                marketStartUtc,
                nowUtc,
                signal);
            await repository.UpsertBtcUpDown5mResultStreakDiagnosticAsync(diagnostic, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to record BTC Up/Down 5m result streak diagnostic for market {MarketId} at {MarketStartUtc}.",
                market.MarketId,
                marketStartUtc);
        }
    }

    private static BtcUpDown5mResultStreakDiagnostic BuildBtcUpDown5mResultStreakDiagnostic(
        PolymarketGammaMarket market,
        DateTimeOffset marketStartUtc,
        DateTimeOffset nowUtc,
        BtcPreviousMarketMoveSignal signal)
    {
        var baseSelectedDirection = signal.BaseSelectedDirection?.ToString();
        var selectedOutcome = signal.BaseSelectedDirection is { } direction
            ? TrySelectOutcomeForDirection(market, direction)?.Outcome
            : null;
        var diagnosticsJson = JsonSerializer.Serialize(new
        {
            decision_source = "skip_bps_cumulative_previous_result_streak",
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            sampled_at_utc = nowUtc,
            latest_previous_market_id = signal.PreviousMarketId,
            latest_previous_market_slug = signal.PreviousMarketSlug,
            latest_previous_market_start_utc = signal.PreviousMarketStartUtc,
            latest_previous_market_end_utc = signal.PreviousMarketEndUtc,
            streak_winning_outcome = signal.StreakWinningOutcome,
            base_selected_direction = baseSelectedDirection,
            selected_outcome = selectedOutcome,
            close_book_streak_result_count = signal.CloseBookStreakResultCount,
            cumulative_move_market_count = signal.StreakResultCount,
            latest_move_bps = signal.MoveBps,
            latest_abs_move_bps = signal.AbsMoveBps,
            cumulative_move_bps = signal.CumulativeMoveBps,
            cumulative_abs_move_bps = signal.CumulativeAbsMoveBps,
            rejection_reason = signal.RejectionReason,
            streak_truncated_reason = signal.StreakTruncatedReason,
            streak_moves = signal.StreakMoveComponents?.Select(component => new
            {
                market_id = component.MarketId,
                market_slug = component.MarketSlug,
                market_start_utc = component.MarketStartUtc,
                market_end_utc = component.MarketEndUtc,
                winning_outcome = component.WinningOutcome,
                move_bps = component.MoveBps,
                abs_move_bps = component.AbsMoveBps,
                start_price_usd = component.StartPriceUsd,
                end_price_usd = component.EndPriceUsd,
                raw_sample_count = component.RawSampleCount,
                valid_sample_count = component.ValidSampleCount
            }),
            close_book_results = signal.StreakResults?.Select(result => new
            {
                market_id = result.MarketId,
                market_slug = result.MarketSlug,
                market_start_utc = result.MarketStartUtc,
                market_end_utc = result.MarketEndUtc,
                winning_outcome = result.WinningOutcome,
                result_source = result.ResultSource,
                up_midpoint = result.UpMidpoint,
                down_midpoint = result.DownMidpoint
            }),
            close_book_diagnostics = signal.CloseBookDiagnostics?.Select(diagnostic => new
            {
                expected_market_start_utc = diagnostic.ExpectedMarketStartUtc,
                market_id = diagnostic.MarketId,
                market_slug = diagnostic.MarketSlug,
                market_end_utc = diagnostic.MarketEndUtc,
                reason = diagnostic.Reason,
                order_book_unavailable = diagnostic.OrderBookUnavailable,
                up_lookup_reason = diagnostic.UpLookupReason,
                down_lookup_reason = diagnostic.DownLookupReason
            })
        });

        return new BtcUpDown5mResultStreakDiagnostic(
            Guid.NewGuid(),
            market.MarketId,
            market.ConditionId,
            market.Slug,
            marketStartUtc,
            market.EndDateUtc,
            nowUtc,
            signal.PreviousMarketId,
            signal.PreviousMarketSlug,
            signal.PreviousMarketStartUtc,
            signal.PreviousMarketEndUtc,
            signal.StreakWinningOutcome,
            baseSelectedDirection,
            selectedOutcome,
            signal.CloseBookStreakResultCount,
            signal.StreakResultCount,
            signal.MoveBps,
            signal.AbsMoveBps,
            signal.CumulativeMoveBps,
            signal.CumulativeAbsMoveBps,
            signal.RejectionReason,
            signal.StreakTruncatedReason,
            diagnosticsJson,
            nowUtc,
            nowUtc);
    }

    private static IReadOnlyList<DateTimeOffset> GetExpectedPreviousMarketStarts(
        DateTimeOffset marketStartUtc,
        BtcUpDownMarketInterval marketInterval,
        int requiredResults)
    {
        var intervalDuration = BtcUpDown5mMarketAnalyzer.GetIntervalDuration(marketInterval);
        return Enumerable
            .Range(1, Math.Max(0, requiredResults))
            .Select(index => marketStartUtc.Subtract(TimeSpan.FromTicks(intervalDuration.Ticks * index)))
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<long, BtcSkipMarketResult>> GetPreviousCloseBookMarketResultsFromLedgerAsync(
        string referenceAssetSymbol,
        IReadOnlyList<DateTimeOffset> expectedMarketStarts,
        CancellationToken cancellationToken)
    {
        if (expectedMarketStarts.Count == 0)
        {
            return new Dictionary<long, BtcSkipMarketResult>();
        }

        try
        {
            var resolvedMarkets = await repository.GetCryptoUpDown5mWebSocketResolvedMarketsAsync(
                [referenceAssetSymbol],
                expectedMarketStarts.Min(),
                expectedMarketStarts.Max(),
                cancellationToken);
            return resolvedMarkets
                .Where(IsAcceptedResolvedMarketLedgerResult)
                .GroupBy(result => result.MarketStartUtc.ToUnixTimeSeconds())
                .ToDictionary(
                    group => group.Key,
                    group => ToSkipMarketResultFromResolvedLedger(
                        group
                            .OrderByDescending(result => string.Equals(result.Source, DiffCounterGammaClosedMarketResultSource, StringComparison.OrdinalIgnoreCase))
                            .ThenByDescending(result => result.UpdatedAtUtc)
                            .First()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to load previous close-book results from resolved-market ledger. Asset={AssetSymbol}",
                referenceAssetSymbol);
            await TryRecordApiErrorAsync("GetPreviousCloseBookLedgerResults", ex.Message, cancellationToken);
            return new Dictionary<long, BtcSkipMarketResult>();
        }
    }

    private static BtcSkipMarketResult ToSkipMarketResultFromResolvedLedger(
        CryptoUpDown5mWebSocketResolvedMarket result)
    {
        var upWon = string.Equals(result.WinningOutcome, "Up", StringComparison.OrdinalIgnoreCase);
        var winningOutcome = upWon ? "Up" : "Down";
        var upAssetId = upWon
            ? result.WinningAssetId ?? string.Empty
            : string.Empty;
        var downAssetId = upWon
            ? null
            : result.WinningAssetId;
        var inferredUpMidpoint = upWon ? 1m : 0m;
        return new BtcSkipMarketResult(
            result.MarketId,
            result.ConditionId,
            result.MarketSlug,
            result.MarketStartUtc,
            result.MarketEndUtc,
            winningOutcome,
            result.FirstReceivedAtUtc,
            "resolved_market_ledger_" + result.Source,
            upAssetId,
            downAssetId,
            upWon ? 1m : 0m,
            upWon ? 1m : 0m,
            upWon ? 1m : 0m,
            upWon ? 0m : 1m,
            upWon ? 0m : 1m,
            upWon ? 0m : 1m,
            inferredUpMidpoint);
    }

    private async Task<BtcSkipCloseBookLookupResult> GetStrictPreviousCloseBookMarketResultsAsync(
        BtcUpDown5mStrategyVariant variant,
        IReadOnlyList<DateTimeOffset> expectedMarketStarts,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (expectedMarketStarts.Count == 0)
        {
            return new BtcSkipCloseBookLookupResult([], []);
        }

        var referenceAssetSymbol = GetReferenceAssetSymbol(variant);
        var ledgerResultsByStart = variant.MarketInterval == BtcUpDownMarketInterval.FiveMinutes
            ? await GetPreviousCloseBookMarketResultsFromLedgerAsync(
                referenceAssetSymbol,
                expectedMarketStarts,
                cancellationToken)
            : new Dictionary<long, BtcSkipMarketResult>();
        var marketLimit = Math.Max(options.MaxMarketsPerCycle, expectedMarketStarts.Count * 4);
        var markets = IsBtcReferenceVariant(variant)
            ? await repository.GetBtcUpDownStrategyGammaMarketsAsync(
                marketLimit,
                cancellationToken)
            : await repository.GetCryptoUpDown5mGammaMarketsAsync(
                [referenceAssetSymbol],
                marketLimit,
                cancellationToken);
        var marketsByStart = markets
            .Where(market => IsReferenceMarketCandidate(market, referenceAssetSymbol, variant.MarketInterval))
            .Select(market => new
            {
                Market = market,
                WindowStart = GetMarketWindowStartUtc(market, variant)
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
            if (ledgerResultsByStart.TryGetValue(expectedMarketStart.ToUnixTimeSeconds(), out var ledgerResult))
            {
                selected.Add(ledgerResult);
                continue;
            }

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
                variant,
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

    private static bool IsReferenceMarketCandidate(
        PolymarketGammaMarket market,
        string referenceAssetSymbol,
        BtcUpDownMarketInterval marketInterval)
    {
        if (string.Equals(referenceAssetSymbol, "BTC", StringComparison.OrdinalIgnoreCase))
        {
            return BtcUpDown5mMarketAnalyzer.GetMarketInterval(market) == marketInterval;
        }

        return CryptoUpDown5mMarketAnalyzer.GetMarketInterval(market) == marketInterval &&
            CryptoUpDown5mMarketAnalyzer.TryGetAssetSymbol(
            market,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { referenceAssetSymbol },
            out _);
    }

    private async Task<BtcSkipCloseBookInferenceResult> TryInferBtcResultFromCloseBookMidpointAsync(
        BtcUpDown5mStrategyVariant variant,
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
            GetMarketWindowStartUtc(previousMarket, variant),
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

    private static decimal GetMinimumAbsMeanDeviationBps(
        IReadOnlyList<decimal> prices,
        decimal meanUsd)
    {
        return prices.Min(price => Math.Abs(GetMeanDeviationBps(price, meanUsd)));
    }

    private static decimal GetMeanDeviationBps(decimal priceUsd, decimal meanUsd)
    {
        return (priceUsd - meanUsd) / meanUsd * 10_000m;
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

    private static bool IsSkipPreviousResultBpsThreshold(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThreshold or
            BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThresholdInstant or
            BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant or
            BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFak;
    }

    private static bool UsesPreviousResultBpsThresholdMoveSignal(BtcUpDown5mStrategyVariant variant)
    {
        return IsSkipPreviousResultBpsThreshold(variant) ||
            IsFixedOutcomePreviousResultBpsFakPremarketEntry(variant);
    }

    private static string? GetTemporarySkipUpEntryReason(
        BtcUpDown5mStrategyVariant variant,
        BtcPriceDirection selectedDirection)
    {
        if (selectedDirection != BtcPriceDirection.Up)
        {
            return null;
        }

        if (string.Equals(GetReferenceAssetSymbol(variant), "ETH", StringComparison.OrdinalIgnoreCase) &&
            (variant.Behavior is BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults or
                BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert or
                BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThreshold or
                BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThresholdInstant))
        {
            return EthSkipUpDirectionTemporarilyDisabledReason;
        }

        return null;
    }

    private static bool UsesPreviousCloseBookMarketResult(BtcUpDown5mStrategyVariant variant)
    {
        return IsSkipConsecutiveMarketResults(variant) || IsSkipPreviousResultBpsThreshold(variant);
    }

    private static bool UsesPreviousResultEntryFlow(BtcUpDown5mStrategyVariant variant)
    {
        return UsesPreviousCloseBookMarketResult(variant);
    }

    private static BtcPriceDirection? GetFixedOutcomePreviousResultBpsDirection(BtcUpDown5mStrategyVariant variant)
    {
        if (variant.Behavior is not BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant and
            not BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFak and
            not BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket)
        {
            return null;
        }

        return variant.FixedOutcome switch
        {
            BtcUpDownFixedOutcome.Up => BtcPriceDirection.Up,
            BtcUpDownFixedOutcome.Down => BtcPriceDirection.Down,
            _ => null
        };
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
        var dependencyWaitExpired = IsOpeningLimitSignalWaitExpired(run.EntryDueAtUtc, nowUtc);
        if (IsBinanceStartRelativeOpeningLimitEntry(variant) &&
            !dependencyWaitExpired &&
            IsStartRelativeDeferredReason(decision.SkipReason))
        {
            return true;
        }

        if (UsesPreviousScoreCounterTrendSignal(variant) &&
            !dependencyWaitExpired &&
            IsPreviousScoreCounterTrendDeferredReason(decision.SkipReason))
        {
            return true;
        }

        if (IsDiffCounterTrendOpeningLimitEntry(variant) &&
            string.Equals(decision.SkipReason, "diff_counter_previous_market_resolved_event_missing", StringComparison.Ordinal) &&
            !dependencyWaitExpired)
        {
            return true;
        }

        if (UsesPreviousCloseBookMarketResult(variant) &&
            (string.Equals(decision.SkipReason, "btc_previous_market_results_missing", StringComparison.Ordinal) ||
                string.Equals(decision.SkipReason, "btc_previous_close_book_result_missing", StringComparison.Ordinal)))
        {
            return !dependencyWaitExpired;
        }

        return UsesPreviousResultBpsThresholdMoveSignal(variant) &&
            !dependencyWaitExpired &&
            IsSkipPreviousResultBpsThresholdDeferredReason(decision.SkipReason);
    }

    private static bool IsStartRelativeDeferredReason(string? reason)
    {
        return string.Equals(reason, "btc_market_start_price_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "btc_reference_equal_market_start", StringComparison.Ordinal) ||
            string.Equals(reason, "crypto_market_start_price_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "crypto_reference_equal_market_start", StringComparison.Ordinal);
    }

    private static bool IsPreviousScoreCounterTrendDeferredReason(string? reason)
    {
        return string.Equals(reason, "btc_previous_score_samples_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "btc_previous_score_samples_insufficient", StringComparison.Ordinal) ||
            string.Equals(reason, "btc_previous_score_premarket_samples_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "btc_previous_score_start_price_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "btc_previous_score_duration_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "crypto_previous_score_samples_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "crypto_previous_score_samples_insufficient", StringComparison.Ordinal) ||
            string.Equals(reason, "crypto_previous_score_premarket_samples_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "crypto_previous_score_start_price_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "crypto_previous_score_duration_missing", StringComparison.Ordinal);
    }

    private static bool IsSkipPreviousResultBpsThresholdDeferredReason(string? reason)
    {
        return string.Equals(reason, "btc_previous_market_btc_samples_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "btc_previous_market_start_price_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "btc_previous_market_end_price_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "premarket_previous_market_reference_samples_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "premarket_previous_market_start_price_missing", StringComparison.Ordinal) ||
            string.Equals(reason, "premarket_previous_market_end_minus_30_price_missing", StringComparison.Ordinal);
    }

    private bool IsOpeningLimitSignalWaitExpired(DateTimeOffset entryDueAtUtc, DateTimeOffset nowUtc)
    {
        return IsEntryExpired(entryDueAtUtc, nowUtc);
    }

    private bool ShouldDeferUntilTradingStarts(
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset nowUtc)
    {
        if (IsPreOpenTimedOpeningLimitEntry(variant) &&
            IsOpeningLimitSignalWaitExpired(run.EntryDueAtUtc, nowUtc))
        {
            return false;
        }

        return IsOpeningLimitEntryAllowedAfterEntryGrace(variant, run.MarketStartUtc, nowUtc);
    }

    private bool ShouldDeferOpeningLimitStakeSizing(
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        BtcMinimumStakeSizing sizing,
        DateTimeOffset nowUtc)
    {
        if (IsPreOpenTimedOpeningLimitEntry(variant) &&
            IsOpeningLimitSignalWaitExpired(run.EntryDueAtUtc, nowUtc))
        {
            return false;
        }

        return IsOpeningLimitEntryAllowedAfterEntryGrace(variant, run.MarketStartUtc, nowUtc) &&
            IsCloseBookOrderBookUnavailableReason(sizing.RejectionReason);
    }

    private static bool IsMiddleReferenceRevert(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is BtcUpDown5mStrategyBehavior.MiddleReferenceRevert or
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant;
    }

    private static BtcUpDown5mStrategyVariant? TryGetBaseOpeningLimitVariantForRevert(BtcUpDown5mStrategyVariant variant)
    {
        var baseBehavior = variant.Behavior switch
        {
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevert => BtcUpDown5mStrategyBehavior.MiddleReference,
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant => BtcUpDown5mStrategyBehavior.MiddleReferenceInstant,
            BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert => BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults,
            _ => (BtcUpDown5mStrategyBehavior?)null
        };
        if (baseBehavior is null)
        {
            return null;
        }

        var referenceAssetSymbol = GetReferenceAssetSymbol(variant);
        return StrategyIds.UpDown5mStrategyVariants.SingleOrDefault(candidate =>
            candidate.Behavior == baseBehavior.Value &&
            candidate.DecisionDepth == variant.DecisionDepth &&
            candidate.EntryDelaySeconds == variant.EntryDelaySeconds &&
            candidate.DecisionThresholdBps == variant.DecisionThresholdBps &&
            string.Equals(GetReferenceAssetSymbol(candidate), referenceAssetSymbol, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> GetEnsembleVoteCandidateVariants()
    {
        return [];
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> GetStrategySelectorCandidateVariants()
    {
        return [];
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
        else if (selectedPricing.OrderBookLookup is not null &&
            selectedPricing.Estimate is null &&
            selectedPricing.Sizing is not null &&
            selectedPricing.ClobGammaDiff is { } restingClobGammaDiff)
        {
            selectedPricing = selectedPricing with
            {
                RawDecisionJson = BuildRestingTakerPaperEntryRawDecisionJson(
                    market,
                    selected.Outcome,
                    variant,
                    selectedPricing.OrderBookLookup,
                    selectedPricing.AverageFillPrice,
                    selectedPricing.NotionalUsd,
                    selectedPricing.Sizing,
                    restingClobGammaDiff,
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
            if (string.Equals(orderBookLookup.RejectionReason, SignalReasonCodes.MissingOrderBookEmptySide, StringComparison.Ordinal) &&
                orderBookLookup.OrderBook is not null)
            {
                return CreateRestingTakerPaperEntryPricingResult(
                    market,
                    outcome,
                    variant,
                    orderBookLookup,
                    stakeMultiplier,
                    nowUtc,
                    enforceDirectionalPrice,
                    enforceStrategyEntryPriceCap,
                    outcomeSelectionSnapshots);
            }

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

    private BtcPaperEntryPricingResult CreateRestingTakerPaperEntryPricingResult(
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

        var limitPrice = GetPaperTakerMaxAllowedPrice(outcome.Price);
        if (enforceDirectionalPrice && !IsDirectionalPriceAllowedForVariant(limitPrice, variant))
        {
            var snapshot = CreateRestingTakerOutcomePricingSnapshot(
                outcome,
                stakeMultiplier,
                orderBookLookup,
                limitPrice,
                sizeShares: 0m,
                notionalUsd: 0m,
                SignalReasonCodes.ExecutionPriceDirectionMismatch);
            return BtcPaperEntryPricingResult.Reject(
                SignalReasonCodes.ExecutionPriceDirectionMismatch,
                snapshot,
                BuildTakerPaperRejectionDiagnosticsJson(
                    market,
                    variant,
                    SignalReasonCodes.ExecutionPriceDirectionMismatch,
                    stakeMultiplier,
                    nowUtc,
                    [snapshot]));
        }

        if (enforceStrategyEntryPriceCap &&
            TryGetStandardEntryPriceCap(variant) is { } entryPriceCap &&
            limitPrice >= entryPriceCap)
        {
            var snapshot = CreateRestingTakerOutcomePricingSnapshot(
                outcome,
                stakeMultiplier,
                orderBookLookup,
                limitPrice,
                sizeShares: 0m,
                notionalUsd: 0m,
                SignalReasonCodes.ExecutionPriceAboveStrategyCap);
            return BtcPaperEntryPricingResult.Reject(
                SignalReasonCodes.ExecutionPriceAboveStrategyCap,
                snapshot,
                BuildTakerPaperRejectionDiagnosticsJson(
                    market,
                    variant,
                    SignalReasonCodes.ExecutionPriceAboveStrategyCap,
                    stakeMultiplier,
                    nowUtc,
                    [snapshot]));
        }

        var sizing = CreateLimitMinimumStakeSizing(orderBook, limitPrice, stakeMultiplier, orderBookLookup.Source);
        if (!sizing.Available)
        {
            var reason = sizing.RejectionReason ?? "paper_taker_resting_limit_sizing_rejected";
            var snapshot = CreateRestingTakerOutcomePricingSnapshot(
                outcome,
                stakeMultiplier,
                orderBookLookup,
                limitPrice,
                sizeShares: 0m,
                notionalUsd: 0m,
                reason);
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
        var sizeShares = sizing.TargetSizeShares;
        if (targetNotionalUsd <= 0m || sizeShares <= 0m)
        {
            const string reason = "paper_taker_resting_limit_size_non_positive";
            var snapshot = CreateRestingTakerOutcomePricingSnapshot(
                outcome,
                stakeMultiplier,
                orderBookLookup,
                limitPrice,
                sizeShares,
                targetNotionalUsd,
                reason);
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

        var clobGammaDiff = Math.Abs(limitPrice - outcome.Price);
        var rawDecisionJson = BuildRestingTakerPaperEntryRawDecisionJson(
            market,
            outcome,
            variant,
            orderBookLookup,
            limitPrice,
            targetNotionalUsd,
            sizing,
            clobGammaDiff,
            nowUtc,
            outcomeSelectionSnapshots);
        var quoteAgeMs = orderBookLookup.Age?.TotalMilliseconds;
        var evidence = string.Concat(
            "BtcUpDown5mPaper:",
            variant.Code,
            ": GTD resting limit order placed despite empty ask side from ",
            orderBookLookup.Source,
            ". LimitPrice=",
            limitPrice.ToString("0.########", CultureInfo.InvariantCulture),
            " SizeShares=",
            sizeShares.ToString("0.########", CultureInfo.InvariantCulture),
            " NotionalUsd=",
            targetNotionalUsd.ToString("0.########", CultureInfo.InvariantCulture),
            " GammaPrice=",
            outcome.Price.ToString("0.########", CultureInfo.InvariantCulture),
            quoteAgeMs is null ? string.Empty : " QuoteAgeMs=" + quoteAgeMs.Value.ToString("0", CultureInfo.InvariantCulture));

        return BtcPaperEntryPricingResult.CreateFilled(
            limitPrice,
            sizeShares,
            targetNotionalUsd,
            orderBookLookup.Source,
            evidence,
            rawDecisionJson,
            CreateRestingTakerOutcomePricingSnapshot(
                outcome,
                targetNotionalUsd,
                orderBookLookup,
                limitPrice,
                sizeShares,
                targetNotionalUsd,
                rejectionReason: null),
            orderBookLookup,
            estimate: null,
            sizing: sizing,
            clobGammaDiff: clobGammaDiff);
    }

    private static BtcTakerOutcomePricingSnapshot CreateRestingTakerOutcomePricingSnapshot(
        BtcUpDown5mOutcomeQuote outcome,
        decimal targetNotionalUsd,
        TakerOrderBookLookupResult orderBookLookup,
        decimal limitPrice,
        decimal sizeShares,
        decimal notionalUsd,
        string? rejectionReason)
    {
        var orderBook = orderBookLookup.OrderBook;
        var syntheticEstimate = new TakerBuyFillEstimate(
            Filled: true,
            RejectionReason: null,
            AverageFillPrice: limitPrice,
            SizeShares: sizeShares,
            NotionalUsd: notionalUsd,
            TargetSizeShares: sizeShares,
            MaxAllowedPrice: limitPrice,
            BestBid: orderBook?.BestBid,
            BestAsk: orderBook?.BestAsk,
            SpreadAbs: orderBook?.SpreadAbs,
            LevelsUsed: 0);
        return CreateTakerOutcomePricingSnapshot(
            outcome,
            targetNotionalUsd,
            orderBookLookup,
            syntheticEstimate,
            rejectionReason);
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
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>> orderBookFetchTasks,
        CancellationToken cancellationToken)
    {
        var maxAge = GetPaperTakerMaxQuoteAge();
        var lookup = marketDataCache.GetOrderBook(assetId, maxAge);
        if (lookup is { Status: OrderBookCacheLookupStatus.Fresh, Snapshot: { } cached })
        {
            return CreateLimitMinimumStakeSizing(
                ApplyFallbackMinOrderSize(cached, fallbackMinOrderSize),
                limitPrice,
                stakeMultiplier,
                WebSocketCacheSource);
        }

        if (options.PaperTakerRestFallbackEnabled)
        {
            var fetched = await GetOrFetchOrderBookAsync(assetId, orderBookFetchTasks, cancellationToken);
            if (fetched.OrderBook is not null)
            {
                var fetchedAge = GetSnapshotAge(fetched.OrderBook.SnapshotAtUtc);
                if (fetchedAge <= maxAge)
                {
                    return CreateLimitMinimumStakeSizing(
                        ApplyFallbackMinOrderSize(fetched.OrderBook, fallbackMinOrderSize),
                        limitPrice,
                        stakeMultiplier,
                        ClobBookSource);
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

    private static OrderBookSnapshot ApplyFallbackMinOrderSize(
        OrderBookSnapshot orderBook,
        decimal? fallbackMinOrderSize)
    {
        return orderBook.MinOrderSize is > 0m ||
            fallbackMinOrderSize is not > 0m
            ? orderBook
            : orderBook with { MinOrderSize = fallbackMinOrderSize };
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

    private static BtcOpeningLimitTargetSizingEstimate CreateOpeningLimitTargetSizingEstimate(
        decimal? minOrderSize,
        decimal limitPrice,
        decimal stakeMultiplier,
        string source)
    {
        if (stakeMultiplier <= 0m)
        {
            return BtcOpeningLimitTargetSizingEstimate.Reject("invalid_stake_multiplier", source);
        }

        if (limitPrice <= 0m || limitPrice >= 1m)
        {
            return BtcOpeningLimitTargetSizingEstimate.Reject("invalid_limit_price", source);
        }

        if (minOrderSize is not > 0m)
        {
            return new BtcOpeningLimitTargetSizingEstimate(
                Available: true,
                RejectionReason: null,
                Source: source,
                SafetyMultiplier: 1m,
                RoundingMode: string.Empty,
                MinOrderSize: null,
                RawTargetNotionalUsd: stakeMultiplier,
                TargetNotionalUsd: stakeMultiplier,
                TargetSizeShares: stakeMultiplier / limitPrice);
        }

        var rawTargetNotionalUsd = minOrderSize.Value * limitPrice * MinimumStakeSafetyMultiplier * stakeMultiplier;
        var roundedTargetNotionalUsd = RoundStakeNotionalUsd(rawTargetNotionalUsd);
        var targetSizeShares = RoundUpToClobLimitSizeShares(roundedTargetNotionalUsd, limitPrice);
        return new BtcOpeningLimitTargetSizingEstimate(
            Available: true,
            RejectionReason: null,
            Source: source,
            SafetyMultiplier: MinimumStakeSafetyMultiplier,
            RoundingMode: StakeNotionalRoundingMode,
            MinOrderSize: minOrderSize,
            RawTargetNotionalUsd: rawTargetNotionalUsd,
            TargetNotionalUsd: targetSizeShares * limitPrice,
            TargetSizeShares: targetSizeShares);
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

    private TakerBuyFillEstimate EstimatePaperFakFill(
        OrderBookSnapshot orderBook,
        decimal targetNotionalUsd,
        decimal worstPrice)
    {
        return TakerBuyFillEstimator.Estimate(
            orderBook,
            targetNotionalUsd,
            worstPrice,
            orderBook.MinOrderSize,
            options.PaperTakerMaxSpreadAbs);
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

    private LostCounterStakeAdjustment ApplyPaperLostCounterStakeAdjustment(
        BtcUpDown5mStrategyVariant variant,
        StrategyRuntimeSettings settings,
        decimal baseStakeUsd)
    {
        return ApplyLostCounterStakeAdjustmentWithLogging(
            variant,
            "Paper",
            settings.PaperLostCoeff,
            settings.PaperLostCounter,
            baseStakeUsd);
    }

    private LostCounterStakeAdjustment ApplyLiveLostCounterStakeAdjustment(
        BtcUpDown5mStrategyVariant variant,
        StrategyRuntimeSettings settings,
        decimal baseStakeUsd)
    {
        return ApplyLostCounterStakeAdjustmentWithLogging(
            variant,
            "Live",
            settings.LiveLostCoeff,
            settings.LiveLostCounter,
            baseStakeUsd);
    }

    private decimal GetPaperLiveShadowStakeUsd(
        BtcUpDown5mStrategyVariant variant,
        StrategyRuntimeSettings settings)
    {
        if (IsFakStatsProbeEntry(variant))
        {
            return settings.LiveStakeAmount;
        }

        return ApplyLiveLostCounterStakeAdjustment(
            variant,
            settings,
            settings.LiveStakeAmount).EffectiveStakeUsd;
    }

    private LostCounterStakeAdjustment ApplyLostCounterStakeAdjustmentWithLogging(
        BtcUpDown5mStrategyVariant variant,
        string mode,
        decimal configuredCoeff,
        int lostCounter,
        decimal baseStakeUsd)
    {
        var adjustment = LostCounterStakeSizer.Calculate(configuredCoeff, lostCounter, baseStakeUsd);
        if (adjustment.LostCounterCoeff <= 0)
        {
            return adjustment;
        }

        logger.LogInformation(
            "{Mode} LostCounter stake adjustment applied. Strategy={StrategyCode} Counter={LostCounter} Coeff={LostCounterCoeff} BaseStakeUsd={BaseStakeUsd} AddStakeUsd={AddStakeUsd} EffectiveStakeUsd={EffectiveStakeUsd}",
            mode,
            variant.Code,
            adjustment.LostCounter,
            adjustment.LostCounterCoeff,
            adjustment.BaseStakeUsd,
            adjustment.AddStakeUsd,
            adjustment.EffectiveStakeUsd);
        return adjustment;
    }

    private async Task UpdatePaperLostCounterAfterSettlementAsync(
        BtcUpDown5mStrategyVariant variant,
        StrategyRuntimeSettings settings,
        bool won,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var normalizedStrategyId = StrategyIds.Normalize(variant.Id);
        var result = await repository.UpdateStrategyLostCounterAfterSettlementAsync(
            normalizedStrategyId,
            isLive: false,
            won,
            counterEnabled: settings.PaperLostCoeff > 1m,
            updatedAtUtc,
            cancellationToken);
        if (!result.Applied)
        {
            logger.LogWarning(
                "Paper LostCounter update skipped because strategy was not found. Strategy={StrategyCode}",
                variant.Code);
            return;
        }

        await strategyStateProvider.UpdateStrategyLostCountersAsync(
            normalizedStrategyId,
            result.PaperLostCounter,
            result.LiveLostCounter,
            cancellationToken);

        logger.LogInformation(
            "Paper LostCounter updated after settlement. Strategy={StrategyCode} Won={Won} Counter={LostCounter}",
            variant.Code,
            won,
            result.PaperLostCounter);
    }

    private static string AttachOpeningLimitStakeSizingJson(
        string rawDecisionJson,
        decimal stakeMultiplier,
        BtcMinimumStakeSizing sizing,
        OpeningLimitExpirationDecision expiration,
        LostCounterStakeAdjustment? lostCounterAdjustment = null)
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

        var orderExecutionMode = root.TryGetPropertyValue("order_execution_mode", out var existingOrderExecutionMode) &&
            string.Equals(existingOrderExecutionMode?.ToString(), FakOrderType, StringComparison.OrdinalIgnoreCase)
            ? FakOrderType
            : OpeningLimitOrderType;
        root["pricing_mode"] = OpeningLimitPricingMode;
        root["order_execution_mode"] = orderExecutionMode;
        root["order_type"] = orderExecutionMode;
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
        if (lostCounterAdjustment is { } adjustment)
        {
            root["paper_lost_coeff_configured"] = adjustment.ConfiguredCoeff;
            root["paper_lost_counter"] = adjustment.LostCounter;
            root["paper_lost_counter_coeff"] = adjustment.LostCounterCoeff;
            root["paper_lost_base_stake_usd"] = adjustment.BaseStakeUsd;
            root["paper_lost_add_stake_usd"] = adjustment.AddStakeUsd;
            root["paper_lost_effective_stake_usd"] = adjustment.EffectiveStakeUsd;
        }

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
        OpeningLimitExpirationDecision expiration,
        bool postOnly = false,
        string? liveOrderType = null,
        bool fakStatsProbe = false)
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
        var paperOrderType = root.TryGetPropertyValue("order_execution_mode", out var existingOrderExecutionMode) &&
            string.Equals(existingOrderExecutionMode?.ToString(), FakOrderType, StringComparison.OrdinalIgnoreCase)
            ? FakOrderType
            : OpeningLimitOrderType;
        root["paper_order_type"] = paperOrderType;
        root["paper_order_execution_mode"] = paperOrderType;
        root["order_type"] = paperOrderType;
        root["order_execution_mode"] = paperOrderType;
        root["post_only"] = postOnly;
        root["live_order_type"] = string.IsNullOrWhiteSpace(liveOrderType) ? FakOrderType : liveOrderType;
        root["live_order_execution_mode"] = string.IsNullOrWhiteSpace(liveOrderType) ? FakOrderType : liveOrderType;
        root["live_post_only"] = postOnly;
        root["fak_stats_probe"] = fakStatsProbe;
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

    private static string AttachPaperLiveShadowActualFillJson(
        string? rawDecisionJson,
        LiveOrder liveOrder,
        decimal fillPrice,
        decimal fillSize,
        decimal fillNotional)
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

        root["source"] = PaperLiveShadowActualFillExecutionSource;
        root["paper_live_shadow_test"] = true;
        root["paper_live_shadow_actual_fill"] = true;
        root["paper_fill_model"] = PaperLiveShadowActualFillModel;
        root["paper_fill_source"] = PaperLiveShadowActualFillModel;
        root["live_order_id"] = liveOrder.Id.ToString();
        root["live_order_status"] = liveOrder.Status.ToString();
        root["live_order_response_status"] = liveOrder.ResponseStatus;
        root["live_clob_order_id"] = liveOrder.OrderId;
        root["live_order_price"] = liveOrder.Price;
        root["live_order_notional_usd"] = liveOrder.NotionalUsd;
        root["live_order_size_shares"] = liveOrder.SizeShares;
        root["live_filled_size"] = liveOrder.FilledSize;
        root["live_filled_notional_usd"] = liveOrder.FilledNotionalUsd;
        root["live_cost_basis_usd"] = liveOrder.CostBasisUsd;
        root["live_average_fill_price"] = liveOrder.AverageFillPrice;
        root["actual_fill_price"] = fillPrice;
        root["actual_fill_size_shares"] = fillSize;
        root["actual_fill_notional_usd"] = fillNotional;
        root["actual_fill_copied_at_utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
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

        var isRestingLimit = root.TryGetPropertyValue("resting_limit_due_to_empty_ask_side", out var restingLimitNode) &&
            bool.TryParse(restingLimitNode?.ToString(), out var restingLimitValue) &&
            restingLimitValue;
        root["opening_limit_price_mode"] = isRestingLimit
            ? "resting_limit_no_executable_ask_depth"
            : "selected_entry_quote_price";
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

    private static string AttachInstantOpeningLimitPricingJson(
        string rawDecisionJson,
        BtcInstantOpeningLimitPriceDecision pricing)
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

        root["opening_limit_price_mode"] = "instant_executable_ask_depth";
        root["limit_pricing_mode"] = "instant_executable_ask_depth";
        root["order_type"] = FakOrderType;
        root["order_execution_mode"] = FakOrderType;
        root["post_only"] = false;
        root["paper_order_type"] = FakOrderType;
        root["paper_order_execution_mode"] = FakOrderType;
        root["instant_fak_enabled"] = true;
        root["fak_market_buy_amount_mode"] = "usd_amount";
        root["fak_worst_price"] = FakGuaranteedWorstPrice;
        root["paper_fak_worst_price"] = FakGuaranteedWorstPrice;
        root["live_fak_worst_price"] = FakGuaranteedWorstPrice;
        root["break_even_pricing_enabled"] = false;
        root["limit_price_rounding"] = "ceil_to_tick";
        root["limit_price_tick_size"] = pricing.TickSize;
        root["limit_price"] = pricing.Available ? pricing.LimitPrice : (decimal?)null;
        root["instant_pricing_source"] = pricing.Source;
        root["instant_quote_age_ms"] = pricing.Age?.TotalMilliseconds;
        root["instant_snapshot_at_utc"] = pricing.OrderBook?.SnapshotAtUtc.ToString("O", CultureInfo.InvariantCulture);
        root["instant_asset_id"] = pricing.OrderBook?.AssetId;
        root["instant_condition_id"] = pricing.OrderBook?.ConditionId;
        root["instant_best_bid"] = pricing.OrderBook?.BestBid;
        root["instant_best_ask"] = pricing.OrderBook?.BestAsk;
        root["instant_spread"] = pricing.OrderBook?.SpreadAbs;
        root["instant_tick_size"] = pricing.TickSize;
        root["instant_max_buy_price"] = pricing.MaxAllowedPrice;
        root["instant_min_order_size"] = pricing.OrderBook?.MinOrderSize;
        root["instant_raw_limit_price"] = pricing.RawLimitPrice;
        root["instant_limit_price"] = pricing.Available || pricing.LimitPrice > 0m ? pricing.LimitPrice : (decimal?)null;
        root["instant_resting_at_max_price"] = pricing.Available &&
            pricing.RawLimitPrice is { } rawLimitPrice &&
            pricing.MaxAllowedPrice is { } maxAllowedPrice &&
            rawLimitPrice > maxAllowedPrice &&
            pricing.LimitPrice <= maxAllowedPrice;
        root["instant_target_notional_usd"] = pricing.TargetNotionalUsd;
        root["instant_target_size_shares"] = pricing.TargetSizeShares;
        root["instant_executable_ask_shares"] = pricing.ExecutableAskShares;
        root["instant_executable_ask_vwap"] = pricing.ExecutableAskVwap;
        root["instant_levels_used"] = pricing.LevelsUsed;
        root["opening_limit_pricing_rejection_reason"] = pricing.RejectionReason;
        root["limit_pricing_rejection_reason"] = pricing.RejectionReason;
        if (!string.IsNullOrWhiteSpace(pricing.RejectionReason))
        {
            root["skip_reason"] = pricing.RejectionReason;
        }

        return root.ToJsonString();
    }

    private static string AttachFakStatsProbeOpeningLimitPricingJson(
        string rawDecisionJson,
        TakerOrderBookLookupResult lookup,
        decimal? TickSize,
        decimal? WorstPrice,
        decimal? ExecutableAskShares,
        decimal? ExecutableAskVwap,
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

        root["opening_limit_price_mode"] = "fak_stats_probe_worst_price";
        root["limit_pricing_mode"] = "fak_stats_probe_worst_price";
        root["fak_stats_probe"] = true;
        root["live_order_type"] = FakOrderType;
        root["live_order_execution_mode"] = FakOrderType;
        root["live_post_only"] = false;
        root["fak_market_buy_amount_mode"] = "usd_amount";
        root["limit_price_rounding"] = "floor_to_tick";
        root["limit_price_tick_size"] = TickSize;
        root["limit_price"] = WorstPrice;
        root["fak_worst_price"] = WorstPrice;
        root["fak_pricing_source"] = lookup.Source;
        root["fak_quote_age_ms"] = lookup.Age?.TotalMilliseconds;
        root["fak_snapshot_at_utc"] = lookup.OrderBook?.SnapshotAtUtc.ToString("O", CultureInfo.InvariantCulture);
        root["fak_asset_id"] = lookup.OrderBook?.AssetId;
        root["fak_condition_id"] = lookup.OrderBook?.ConditionId;
        root["fak_best_bid"] = lookup.OrderBook?.BestBid;
        root["fak_best_ask"] = lookup.OrderBook?.BestAsk;
        root["fak_spread"] = lookup.OrderBook?.SpreadAbs;
        root["fak_tick_size"] = TickSize;
        root["fak_min_order_size"] = lookup.OrderBook?.MinOrderSize;
        root["fak_executable_ask_shares_at_worst_price"] = ExecutableAskShares;
        root["fak_executable_ask_vwap_at_worst_price"] = ExecutableAskVwap;
        root["opening_limit_pricing_rejection_reason"] = RejectionReason;
        root["limit_pricing_rejection_reason"] = RejectionReason;
        if (!string.IsNullOrWhiteSpace(RejectionReason))
        {
            root["skip_reason"] = RejectionReason;
        }

        return root.ToJsonString();
    }

    private static string AttachFakPaperFillSimulationJson(
        string rawDecisionJson,
        TakerOrderBookLookupResult? lookup,
        BtcMinimumStakeSizing sizing,
        TakerBuyFillEstimate? estimate,
        string? rejectionReason,
        DateTimeOffset nowUtc)
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
        catch (InvalidOperationException)
        {
            root = new JsonObject();
        }

        root["order_type"] = FakOrderType;
        root["order_execution_mode"] = FakOrderType;
        root["post_only"] = false;
        root["paper_order_type"] = FakOrderType;
        root["paper_order_execution_mode"] = FakOrderType;
        root["paper_execution_source"] = BtcFakTakerPaperExecutionSource;
        root["paper_execution_evidence_class"] = PaperExecutableSnapshotEvidenceClass;
        root["paper_fak_fill_model"] = PaperFakExecutableSnapshotFillModel;
        root["paper_fak_evaluated_at_utc"] = nowUtc.ToString("O", CultureInfo.InvariantCulture);
        root["paper_fak_source"] = lookup?.Source;
        root["paper_fak_quote_age_ms"] = lookup?.Age?.TotalMilliseconds;
        root["paper_fak_snapshot_at_utc"] = lookup?.OrderBook?.SnapshotAtUtc.ToString("O", CultureInfo.InvariantCulture);
        root["paper_fak_best_bid"] = lookup?.OrderBook?.BestBid;
        root["paper_fak_best_ask"] = lookup?.OrderBook?.BestAsk;
        root["paper_fak_spread"] = lookup?.OrderBook?.SpreadAbs;
        root["paper_fak_requested_notional_usd"] = sizing.TargetNotionalUsd;
        root["paper_fak_requested_size_shares_at_cap"] = sizing.TargetSizeShares;
        root["paper_fak_worst_price"] = estimate?.MaxAllowedPrice ?? sizing.ReferencePrice;
        root["paper_fak_average_fill_price"] = estimate?.Filled == true ? estimate.AverageFillPrice : null;
        root["paper_fak_filled_size_shares"] = estimate?.Filled == true ? estimate.SizeShares : 0m;
        root["paper_fak_filled_notional_usd"] = estimate?.Filled == true ? estimate.NotionalUsd : 0m;
        root["paper_fak_target_size_shares"] = estimate?.TargetSizeShares;
        root["paper_fak_levels_used"] = estimate?.LevelsUsed;
        root["paper_fak_partial_fill"] = estimate?.Filled == true && estimate.NotionalUsd < sizing.TargetNotionalUsd;
        root["paper_fak_rejection_reason"] = rejectionReason;
        if (!string.IsNullOrWhiteSpace(rejectionReason))
        {
            root["skip_reason"] = rejectionReason;
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
        return await GetFreshTakerOrderBookAsync(
            assetId,
            nowUtc,
            orderBookFetchTasks: null,
            cancellationToken);
    }

    private async Task<TakerOrderBookLookupResult> GetFreshTakerOrderBookAsync(
        string assetId,
        DateTimeOffset nowUtc,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>>? orderBookFetchTasks,
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
            var restLookup = orderBookFetchTasks is null
                ? await GetFreshRestTakerOrderBookAsync(assetId, nowUtc, cancellationToken)
                : await GetFreshRestTakerOrderBookAsync(assetId, nowUtc, orderBookFetchTasks, cancellationToken);
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
        return CreateFreshRestTakerOrderBookLookupResult(fetched);
    }

    private async Task<TakerOrderBookLookupResult> GetFreshRestTakerOrderBookAsync(
        string assetId,
        DateTimeOffset nowUtc,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>> orderBookFetchTasks,
        CancellationToken cancellationToken)
    {
        var fetched = await GetOrFetchOrderBookAsync(
            assetId,
            orderBookFetchTasks,
            cancellationToken,
            stampWithLocalReceiveTime: true);
        return CreateFreshRestTakerOrderBookLookupResult(fetched);
    }

    private TakerOrderBookLookupResult CreateFreshRestTakerOrderBookLookupResult(
        OrderBookFetchResult fetched)
    {
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

    private Task<OrderBookFetchResult> GetOrFetchOrderBookAsync(
        string assetId,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<OrderBookFetchResult>>> orderBookFetchTasks,
        CancellationToken cancellationToken,
        bool stampWithLocalReceiveTime = false)
    {
        var cacheKey = stampWithLocalReceiveTime
            ? string.Concat(assetId, ":local_receive_time")
            : assetId;
        var fetchTask = orderBookFetchTasks.GetOrAdd(
            cacheKey,
            static (key, state) => new Lazy<Task<OrderBookFetchResult>>(
                () => state.Processor.FetchAndCacheOrderBookAsync(
                    state.AssetId,
                    state.CancellationToken,
                    state.StampWithLocalReceiveTime),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (Processor: this, AssetId: assetId, StampWithLocalReceiveTime: stampWithLocalReceiveTime, CancellationToken: cancellationToken));
        return fetchTask.Value;
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
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
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
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
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
        decimal? referenceMeanUsd,
        BtcUsdReferencePricePoint? currentPrice,
        int requiredReferenceSamples,
        IReadOnlyList<BtcUsdReferencePricePoint> referenceSamples,
        BtcPriceDirection? baseSelectedDirection,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason)
    {
        var limitPrice = GetBinanceStartRelativeLimitPrice(variant);
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var referenceAssetSymbol = GetReferenceAssetSymbol(variant);
        var referenceBinanceSymbol = referenceAssetSymbol + "USDT";
        var isBtcReference = IsBtcReferenceVariant(variant);
        var comparedPrices = currentPrice is null
            ? []
            : new[] { currentPrice.PriceUsd };
        var currentMoveFromMeanBps = currentPrice is not null && referenceMeanUsd is > 0m
            ? GetMeanDeviationBps(currentPrice.PriceUsd, referenceMeanUsd.Value)
            : (decimal?)null;
        var currentAbsMoveFromMeanBps = currentMoveFromMeanBps is { } currentMove
            ? Math.Abs(currentMove)
            : (decimal?)null;
        var minAbsMoveFromMeanBps = comparedPrices.Length > 0 && referenceMeanUsd is > 0m
            ? GetMinimumAbsMeanDeviationBps(comparedPrices, referenceMeanUsd.Value)
            : (decimal?)null;
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = OpeningLimitOrderType,
            post_only = false,
            strategy_code = variant.Code,
            reference_asset_symbol = referenceAssetSymbol,
            reference_binance_symbol = referenceBinanceSymbol,
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
            btc_current_price_usd = isBtcReference ? currentPrice?.PriceUsd : null,
            btc_current_source_updated_at_utc = isBtcReference ? currentPrice?.SourceUpdatedAtUtc : null,
            btc_current_fetched_at_utc = isBtcReference ? currentPrice?.FetchedAtUtc : null,
            crypto_asset_symbol = isBtcReference ? null : referenceAssetSymbol,
            crypto_current_price_usd = isBtcReference ? null : currentPrice?.PriceUsd,
            crypto_current_source_updated_at_utc = isBtcReference ? null : currentPrice?.SourceUpdatedAtUtc,
            crypto_current_fetched_at_utc = isBtcReference ? null : currentPrice?.FetchedAtUtc,
            reference_source = snapshot.Source,
            reference_window_size = snapshot.WindowSize,
            reference_sample_count = snapshot.SampleCount,
            reference_is_full_window = snapshot.IsFullWindow,
            reference_arithmetic_mean_usd = referenceMeanUsd,
            reference_full_window_arithmetic_mean_usd = snapshot.ArithmeticMeanUsd,
            btc_move_from_mean_bps = isBtcReference ? currentMoveFromMeanBps : null,
            btc_abs_move_from_mean_bps = isBtcReference ? currentAbsMoveFromMeanBps : null,
            btc_min_abs_move_from_mean_bps = isBtcReference ? minAbsMoveFromMeanBps : null,
            btc_min_move_from_mean_bps = isBtcReference ? variant.DecisionThresholdBps : null,
            crypto_move_from_mean_bps = isBtcReference ? null : currentMoveFromMeanBps,
            crypto_abs_move_from_mean_bps = isBtcReference ? null : currentAbsMoveFromMeanBps,
            crypto_min_abs_move_from_mean_bps = isBtcReference ? null : minAbsMoveFromMeanBps,
            crypto_min_move_from_mean_bps = isBtcReference ? null : variant.DecisionThresholdBps,
            required_cached_samples = requiredReferenceSamples,
            required_reference_samples = requiredReferenceSamples,
            cached_samples_used = referenceSamples
                .Select(sample => new
                {
                    price_usd = sample.PriceUsd,
                    source_updated_at_utc = sample.SourceUpdatedAtUtc,
                    fetched_at_utc = sample.FetchedAtUtc
                })
                .ToArray(),
            reference_samples_used = referenceSamples
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

    private static string BuildReferenceAverageBpsRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        BtcUsdReferencePricePoint? currentPrice,
        IReadOnlyList<CryptoReferencePriceAverage> averages,
        CryptoReferencePriceAverage? selectedAverage,
        BtcPriceDirection? triggerDirection,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        decimal? moveFromAverageBps,
        string? reason)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var referenceAssetSymbol = GetReferenceAssetSymbol(variant);
        var isBtcReference = IsBtcReferenceVariant(variant);
        var selectedAveragePriceUsd = selectedAverage?.AveragePriceUsd;
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = FakOrderType,
            post_only = false,
            strategy_code = variant.Code,
            reference_asset_symbol = referenceAssetSymbol,
            reference_binance_symbol = selectedAverage?.BinanceSymbol ?? referenceAssetSymbol + "USDT",
            decision_source = "reference_price_max_average_bps_premarket",
            reference_average_source = "crypto_reference_price_average_cache",
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            btc_current_price_usd = isBtcReference ? currentPrice?.PriceUsd : null,
            btc_current_source_updated_at_utc = isBtcReference ? currentPrice?.SourceUpdatedAtUtc : null,
            btc_current_fetched_at_utc = isBtcReference ? currentPrice?.FetchedAtUtc : null,
            crypto_asset_symbol = isBtcReference ? null : referenceAssetSymbol,
            crypto_current_price_usd = isBtcReference ? null : currentPrice?.PriceUsd,
            crypto_current_source_updated_at_utc = isBtcReference ? null : currentPrice?.SourceUpdatedAtUtc,
            crypto_current_fetched_at_utc = isBtcReference ? null : currentPrice?.FetchedAtUtc,
            current_price_usd = currentPrice?.PriceUsd,
            selected_reference_average_window = selectedAverage?.WindowLabel,
            selected_reference_average_window_seconds = selectedAverage?.WindowSeconds,
            selected_reference_average_sample_step_seconds = selectedAverage?.SampleStepSeconds,
            selected_reference_average_sample_count = selectedAverage?.SampleCount,
            selected_reference_average_expected_sample_count = selectedAverage?.ExpectedSampleCount,
            selected_reference_average_is_full_window = selectedAverage?.IsFullWindow,
            selected_reference_average_price_usd = selectedAveragePriceUsd,
            selected_reference_average_first_bucket_utc = selectedAverage?.FirstBucketStartUtc,
            selected_reference_average_last_bucket_utc = selectedAverage?.LastBucketStartUtc,
            selected_reference_average_updated_at_utc = selectedAverage?.UpdatedAtUtc,
            reference_average_count = averages.Count,
            reference_full_average_count = averages.Count(average => average.IsFullWindow && average.AveragePriceUsd is > 0m),
            reference_averages = averages
                .Select(average => new
                {
                    window = average.WindowLabel,
                    window_seconds = average.WindowSeconds,
                    sample_step_seconds = average.SampleStepSeconds,
                    sample_count = average.SampleCount,
                    expected_sample_count = average.ExpectedSampleCount,
                    is_full_window = average.IsFullWindow,
                    average_price_usd = average.AveragePriceUsd,
                    first_bucket_utc = average.FirstBucketStartUtc,
                    last_bucket_utc = average.LastBucketStartUtc,
                    updated_at_utc = average.UpdatedAtUtc
                })
                .ToArray(),
            reference_average_auto_direction_enabled = variant.DiffCounterTriggerOutcome is null,
            reference_average_direction_source = variant.DiffCounterTriggerOutcome is null
                ? "move_sign"
                : "configured_trigger",
            reference_average_trigger_direction = triggerDirection?.ToString(),
            reference_average_target_direction = selectedDirection?.ToString(),
            fixed_outcome = variant.FixedOutcome?.ToString(),
            reference_average_move_from_middle_bps = moveFromAverageBps,
            reference_average_abs_move_from_middle_bps = moveFromAverageBps is { } move ? Math.Abs(move) : (decimal?)null,
            reference_average_min_move_from_middle_bps = GetReferenceAverageMinMoveBps(variant),
            btc_reference_average_move_from_middle_bps = isBtcReference ? moveFromAverageBps : null,
            btc_reference_average_abs_move_from_middle_bps = isBtcReference && moveFromAverageBps is { } btcMove ? Math.Abs(btcMove) : (decimal?)null,
            btc_reference_average_min_move_from_middle_bps = isBtcReference ? GetReferenceAverageMinMoveBps(variant) : (decimal?)null,
            crypto_reference_average_move_from_middle_bps = isBtcReference ? null : moveFromAverageBps,
            crypto_reference_average_abs_move_from_middle_bps = !isBtcReference && moveFromAverageBps is { } cryptoMove ? Math.Abs(cryptoMove) : (decimal?)null,
            crypto_reference_average_min_move_from_middle_bps = isBtcReference ? (decimal?)null : GetReferenceAverageMinMoveBps(variant),
            reference_average_filtered_enabled = IsFilteredReferenceAverageBpsFakPremarketEntry(variant),
            reference_average_filtered_skipped_windows = IsFilteredReferenceAverageBpsFakPremarketEntry(variant)
                ? new[] { "6h", "12h" }
                : Array.Empty<string>(),
            reference_average_filtered_abs_move_skip_min_bps = IsFilteredReferenceAverageBpsFakPremarketEntry(variant) ? 20m : (decimal?)null,
            reference_average_filtered_abs_move_skip_max_exclusive_bps = IsFilteredReferenceAverageBpsFakPremarketEntry(variant) ? 80m : (decimal?)null,
            fak_stats_probe = true,
            premarket_reference_average_enabled = true,
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            target_notional_usd = targetNotionalUsd,
            skip_reason = reason
        });
    }

    private static string BuildAbsoluteBpsRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        BtcUsdReferencePricePoint? currentPrice,
        CryptoReferencePriceExtrema? extrema,
        string? selectedBoundary,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        decimal? moveAboveMaximumBps,
        decimal? moveBelowMinimumBps,
        string? reason)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var referenceAssetSymbol = GetReferenceAssetSymbol(variant);
        var isBtcReference = IsBtcReferenceVariant(variant);
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = FakOrderType,
            post_only = false,
            strategy_code = variant.Code,
            reference_asset_symbol = referenceAssetSymbol,
            reference_binance_symbol = extrema?.BinanceSymbol ?? referenceAssetSymbol + "USDT",
            decision_source = "reference_price_absolute_range_bps_premarket",
            absolute_reference_source = "crypto_reference_price_extrema_cache",
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            btc_current_price_usd = isBtcReference ? currentPrice?.PriceUsd : null,
            btc_current_source_updated_at_utc = isBtcReference ? currentPrice?.SourceUpdatedAtUtc : null,
            btc_current_fetched_at_utc = isBtcReference ? currentPrice?.FetchedAtUtc : null,
            crypto_asset_symbol = isBtcReference ? null : referenceAssetSymbol,
            crypto_current_price_usd = isBtcReference ? null : currentPrice?.PriceUsd,
            crypto_current_source_updated_at_utc = isBtcReference ? null : currentPrice?.SourceUpdatedAtUtc,
            crypto_current_fetched_at_utc = isBtcReference ? null : currentPrice?.FetchedAtUtc,
            current_price_usd = currentPrice?.PriceUsd,
            absolute_reference_lookback_hours = variant.DecisionDepth,
            absolute_reference_window_seconds = extrema?.WindowSeconds,
            absolute_reference_coverage_bucket_seconds = extrema?.CoverageBucketSeconds,
            absolute_reference_tick_count = extrema?.TickCount,
            absolute_reference_coverage_bucket_count = extrema?.CoverageBucketCount,
            absolute_reference_expected_coverage_bucket_count = extrema?.ExpectedCoverageBucketCount,
            absolute_reference_is_full_window = extrema?.IsFullWindow,
            absolute_reference_minimum_price_usd = extrema?.MinimumPriceUsd,
            absolute_reference_minimum_sampled_at_utc = extrema?.MinimumSampledAtUtc,
            absolute_reference_maximum_price_usd = extrema?.MaximumPriceUsd,
            absolute_reference_maximum_sampled_at_utc = extrema?.MaximumSampledAtUtc,
            absolute_reference_first_bucket_utc = extrema?.FirstBucketStartUtc,
            absolute_reference_last_bucket_utc = extrema?.LastBucketStartUtc,
            absolute_reference_updated_at_utc = extrema?.UpdatedAtUtc,
            absolute_reference_threshold_bps = variant.DecisionThresholdBps,
            absolute_reference_move_above_maximum_bps = moveAboveMaximumBps,
            absolute_reference_move_below_minimum_bps = moveBelowMinimumBps,
            absolute_reference_selected_boundary = selectedBoundary,
            absolute_reference_target_direction = selectedDirection?.ToString(),
            fak_stats_probe = true,
            premarket_absolute_enabled = true,
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            target_notional_usd = targetNotionalUsd,
            skip_reason = reason
        });
    }

    private static string BuildFuturesBasisBpsRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        IReadOnlyList<ExpiryFuturesReferencePricePoint>? futuresPrices,
        IReadOnlyList<decimal>? basisBpsByExpiry,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        var marketEndUtc = GetEffectiveMarketEndUtc(market, variant, marketStartUtc);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var referenceAssetSymbol = GetReferenceAssetSymbol(variant);
        var primaryFuturesPrice = futuresPrices?.FirstOrDefault();
        var primaryBasisBps = basisBpsByExpiry is { Count: > 0 } ? basisBpsByExpiry[0] : (decimal?)null;
        var confirmationSignsMatch = basisBpsByExpiry is { Count: FuturesBasisRequiredExpiryCount }
            ? HaveMatchingFuturesBasisConfirmationSigns(basisBpsByExpiry)
            : (bool?)null;
        var confirmationMatchingCount = basisBpsByExpiry is { Count: > 0 }
            ? CountMatchingFuturesBasisConfirmationSigns(basisBpsByExpiry)
            : (int?)null;
        var futuresExpiryDiagnostics = futuresPrices?
            .Select((price, index) => new
            {
                expiry_rank = index + 1,
                role = index == 0 ? "primary_threshold" : "sign_confirmation",
                instrument_id = price.InstrumentId,
                expiry_at_utc = price.ExpiryAtUtc,
                horizon_after_market_end_seconds = marketEndUtc is { } targetEndUtc
                    ? (price.ExpiryAtUtc - targetEndUtc).TotalSeconds
                    : (double?)null,
                bid_price_usd = price.BidPriceUsd,
                ask_price_usd = price.AskPriceUsd,
                mid_price_usd = price.MidPriceUsd,
                index_price_usd = price.IndexPriceUsd,
                futures_source_updated_at_utc = price.FuturesSourceUpdatedAtUtc,
                index_source_updated_at_utc = price.IndexSourceUpdatedAtUtc,
                fetched_at_utc = price.FetchedAtUtc,
                basis_bps = basisBpsByExpiry is not null && index < basisBpsByExpiry.Count
                    ? basisBpsByExpiry[index]
                    : (decimal?)null,
                basis_sign = basisBpsByExpiry is not null && index < basisBpsByExpiry.Count
                    ? GetFuturesBasisSignName(basisBpsByExpiry[index])
                    : null
            })
            .ToArray();
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = FakOrderType,
            post_only = false,
            strategy_code = variant.Code,
            reference_asset_symbol = referenceAssetSymbol,
            reference_index_instrument_id = referenceAssetSymbol + "-USD",
            futures_instrument_id = primaryFuturesPrice?.InstrumentId,
            decision_source = IsFuturesBasisBpsFakPremarketRevertEntry(variant)
                ? "okx_three_expiry_confirmed_futures_basis_bps_revert_premarket"
                : "okx_three_expiry_confirmed_futures_basis_bps_premarket",
            revert_decision = IsFuturesBasisBpsFakPremarketRevertEntry(variant),
            futures_basis_source = primaryFuturesPrice?.Source,
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = marketEndUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            current_price_usd = primaryFuturesPrice?.IndexPriceUsd,
            reference_index_price_usd = primaryFuturesPrice?.IndexPriceUsd,
            reference_index_source_updated_at_utc = primaryFuturesPrice?.IndexSourceUpdatedAtUtc,
            futures_bid_price_usd = primaryFuturesPrice?.BidPriceUsd,
            futures_ask_price_usd = primaryFuturesPrice?.AskPriceUsd,
            futures_mid_price_usd = primaryFuturesPrice?.MidPriceUsd,
            futures_source_updated_at_utc = primaryFuturesPrice?.FuturesSourceUpdatedAtUtc,
            futures_fetched_at_utc = primaryFuturesPrice?.FetchedAtUtc,
            futures_expiry_at_utc = primaryFuturesPrice?.ExpiryAtUtc,
            futures_horizon_after_market_end_seconds = primaryFuturesPrice is not null && marketEndUtc is { } targetEndUtc
                ? (primaryFuturesPrice.ExpiryAtUtc - targetEndUtc).TotalSeconds
                : (double?)null,
            futures_basis_bps = primaryBasisBps,
            futures_basis_abs_bps = primaryBasisBps is { } basis ? Math.Abs(basis) : (decimal?)null,
            futures_basis_min_move_bps = GetFuturesBasisMinMoveBps(variant),
            futures_basis_direction_source = "okx_three_fixed_expiry_mids_minus_okx_usd_index",
            futures_basis_trigger_direction = GetFuturesBasisTriggerDirectionName(variant, primaryBasisBps),
            futures_basis_target_direction = selectedDirection?.ToString(),
            futures_required_expiry_count = FuturesBasisRequiredExpiryCount,
            futures_expiry_count = futuresPrices?.Count,
            futures_confirmation_required_count = FuturesBasisRequiredExpiryCount - 1,
            futures_confirmation_matching_count = confirmationMatchingCount,
            futures_confirmation_signs_match = confirmationSignsMatch,
            futures_confirmation_rule = "second_and_third_basis_sign_match_primary",
            futures_expiries = futuresExpiryDiagnostics,
            fak_stats_probe = true,
            premarket_futures_basis_enabled = true,
            futures_basis_sign_confirmation_enabled = true,
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            target_notional_usd = targetNotionalUsd,
            skip_reason = reason
        });
    }

    private static bool HaveMatchingFuturesBasisConfirmationSigns(IReadOnlyList<decimal> basisBpsByExpiry)
    {
        if (basisBpsByExpiry.Count != FuturesBasisRequiredExpiryCount)
        {
            return false;
        }

        var primarySign = Math.Sign(basisBpsByExpiry[0]);
        return primarySign != 0 &&
            basisBpsByExpiry.Skip(1).All(value => Math.Sign(value) == primarySign);
    }

    private static int CountMatchingFuturesBasisConfirmationSigns(IReadOnlyList<decimal> basisBpsByExpiry)
    {
        if (basisBpsByExpiry.Count == 0)
        {
            return 0;
        }

        var primarySign = Math.Sign(basisBpsByExpiry[0]);
        return primarySign == 0
            ? 0
            : basisBpsByExpiry.Skip(1).Count(value => Math.Sign(value) == primarySign);
    }

    private static string GetFuturesBasisSignName(decimal basisBps)
    {
        return Math.Sign(basisBps) switch
        {
            > 0 => BtcPriceDirection.Up.ToString(),
            < 0 => BtcPriceDirection.Down.ToString(),
            _ => "Zero"
        };
    }

    private static string? GetFuturesBasisTriggerDirectionName(
        BtcUpDown5mStrategyVariant variant,
        decimal? basisBps)
    {
        if (basisBps is not { } value)
        {
            return null;
        }

        var thresholdBps = GetFuturesBasisMinMoveBps(variant);
        if (value >= thresholdBps)
        {
            return BtcPriceDirection.Up.ToString();
        }

        return value <= -thresholdBps
            ? BtcPriceDirection.Down.ToString()
            : null;
    }

    private static bool IsFilteredReferenceAverageWindowSkipped(string? windowLabel)
    {
        return string.Equals(windowLabel, "6h", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(windowLabel, "12h", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFilteredReferenceAverageAbsMoveSkipped(decimal absMoveFromAverageBps)
    {
        return absMoveFromAverageBps >= 20m && absMoveFromAverageBps < 80m;
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
        if (IsSimpleFixedOutcomeInstantEntry(variant))
        {
            return selectedDirection == BtcPriceDirection.Up
                ? "simple_up_instant"
                : "simple_down_instant";
        }

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

    private static string BuildDiffCounterTrendRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        DiffCounterSnapshot? snapshot,
        BtcPriceDirection? triggerDirection,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var limitPrice = GetFixedDirectionLimitPrice(variant);
        var threshold = Math.Max(1, variant.DecisionDepth);
        var isAdjustedDiff = IsAdjustedDiffCounterTrendOpeningLimitEntry(variant);
        var isShiftDiff = IsShiftDiffCounterTrendOpeningLimitEntry(variant);
        var isFakPremarket = IsDiffCounterTrendFakPremarketEntry(variant);
        var shiftDiffCount = GetShiftDiffCount(variant);
        var effectiveDiff = GetDiffCounterEffectiveDiff(snapshot, triggerDirection, isAdjustedDiff);
        var triggerSide = triggerDirection?.ToString();
        var decisionSource = isAdjustedDiff
            ? "continuous_trend_zero_adjusted_diff_countertrend"
            : isShiftDiff
                ? "continuous_shift_diff_countertrend"
                : isFakPremarket
                    ? "utc_day_start_resolved_market_diff_countertrend_premarket"
                    : "utc_day_start_resolved_market_diff_countertrend";
        var counterMode = isAdjustedDiff
            ? "continuous_trend_zero"
            : isShiftDiff
                ? "continuous_shift_diff"
                : "utc_day_start";
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = isFakPremarket ? FakOrderType : OpeningLimitOrderType,
            post_only = false,
            diff_counter_fak_premarket_enabled = isFakPremarket,
            strategy_code = variant.Code,
            strategy_category = variant.Category,
            decision_source = decisionSource,
            reference_asset_symbol = GetReferenceAssetSymbol(variant),
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            counter_mode = counterMode,
            counter_result_source = "ResolvedMarketLedger",
            counter_start_market_start_utc = snapshot?.CounterStartMarketStartUtc,
            counter_target_market_start_utc = snapshot?.TargetMarketStartUtc,
            counter_target_market_result_received = snapshot?.TargetMarketResultReceived,
            counter_target_market_result_source = snapshot?.TargetMarketResultSource,
            counter_last_market_start_utc = snapshot?.LastIncludedMarketStartUtc,
            counter_high_water_market_start_utc = snapshot?.HighWaterMarketStartUtc,
            counter_initialized = snapshot?.Initialized,
            counter_initialized_at_utc = snapshot?.InitializedAtUtc,
            counter_history_fetch_failed_at_utc = snapshot?.HistoryFetchFailedAtUtc,
            counter_history_fetch_retry_after_utc = snapshot?.HistoryFetchRetryAfterUtc,
            counter_history_fetch_error = snapshot?.HistoryFetchErrorMessage,
            up_count = snapshot?.UpCount,
            down_count = snapshot?.DownCount,
            diff_count = snapshot?.DiffCount,
            raw_diff = snapshot?.Diff,
            diff = snapshot?.Diff,
            trend_zero_mode = isAdjustedDiff ? AdjustedDiffTrendZeroMode : null,
            trend_zero_ema_period_points = isAdjustedDiff ? AdjustedDiffTrendZeroEmaPeriodPoints : (int?)null,
            trend_zero_warmup_points = isAdjustedDiff ? AdjustedDiffTrendZeroWarmupPoints : (int?)null,
            trend_zero_deadband = isAdjustedDiff ? AdjustedDiffTrendZeroDeadband : (decimal?)null,
            trend_zero_max_step = isAdjustedDiff ? AdjustedDiffTrendZeroMaxStep : (decimal?)null,
            trend_zero = snapshot?.TrendZero,
            trend_zero_ema = snapshot?.TrendZeroEma,
            adjusted_diff = snapshot?.AdjustedDiff,
            shift_diff_count = isShiftDiff ? shiftDiffCount : (int?)null,
            shift_diff_trigger_abs = isShiftDiff ? (shiftDiffCount * 2) + 1 : (int?)null,
            shift_diff_positive_adjustments = isShiftDiff ? snapshot?.ShiftDiffPositiveAdjustments : null,
            shift_diff_negative_adjustments = isShiftDiff ? snapshot?.ShiftDiffNegativeAdjustments : null,
            processed_market_count = snapshot?.ProcessedMarketCount,
            trigger_side = triggerSide,
            diff_counter_trigger_outcome = variant.DiffCounterTriggerOutcome?.ToString(),
            threshold = threshold,
            effective_diff = effectiveDiff,
            selected_direction = selectedDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            limit_price = limitPrice,
            target_notional_usd = targetNotionalUsd,
            target_size_shares = limitPrice > 0m ? targetNotionalUsd / limitPrice : (decimal?)null,
            skip_reason = reason
        });
    }

    private static string BuildDiffProgressRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal baseStakeUsd,
        DateTimeOffset nowUtc,
        DiffCounterSnapshot? snapshot,
        BtcPriceDirection? triggerDirection,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        DiffProgressMode modeBefore,
        DiffProgressMode modeAfter,
        DateTimeOffset? currentDayCounterStartUtc,
        bool resetPostponed,
        decimal? progressStakeMultiplier,
        decimal? progressStakeUsd,
        string? reason)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var threshold = Math.Max(1, variant.DecisionDepth);
        var effectiveDiff = GetDiffCounterEffectiveDiff(snapshot, triggerDirection, useAdjustedDiff: false);
        var uncappedProgressStakeMultiplier = effectiveDiff.HasValue && effectiveDiff.Value > threshold
            ? effectiveDiff.Value - threshold
            : (decimal?)null;
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = FakOrderType,
            order_type = FakOrderType,
            post_only = false,
            diff_progress_enabled = true,
            strategy_code = variant.Code,
            strategy_category = variant.Category,
            decision_source = "utc_day_start_resolved_market_diff_progress",
            reference_asset_symbol = GetReferenceAssetSymbol(variant),
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            progress_mode_before = modeBefore.ToString(),
            progress_mode_after = modeAfter.ToString(),
            counter_mode = "utc_day_start_progress",
            counter_result_source = "ResolvedMarketLedger",
            current_day_counter_start_utc = currentDayCounterStartUtc,
            counter_start_market_start_utc = snapshot?.CounterStartMarketStartUtc,
            counter_target_market_start_utc = snapshot?.TargetMarketStartUtc,
            counter_target_market_result_received = snapshot?.TargetMarketResultReceived,
            counter_target_market_result_source = snapshot?.TargetMarketResultSource,
            counter_last_market_start_utc = snapshot?.LastIncludedMarketStartUtc,
            counter_high_water_market_start_utc = snapshot?.HighWaterMarketStartUtc,
            counter_initialized = snapshot?.Initialized,
            counter_initialized_at_utc = snapshot?.InitializedAtUtc,
            counter_history_fetch_failed_at_utc = snapshot?.HistoryFetchFailedAtUtc,
            counter_history_fetch_retry_after_utc = snapshot?.HistoryFetchRetryAfterUtc,
            counter_history_fetch_error = snapshot?.HistoryFetchErrorMessage,
            reset_postponed = resetPostponed,
            up_count = snapshot?.UpCount,
            down_count = snapshot?.DownCount,
            diff_count = snapshot?.DiffCount,
            raw_diff = snapshot?.Diff,
            diff = snapshot?.Diff,
            processed_market_count = snapshot?.ProcessedMarketCount,
            trigger_side = triggerDirection?.ToString(),
            diff_counter_trigger_outcome = variant.DiffCounterTriggerOutcome?.ToString(),
            threshold = threshold,
            effective_diff = effectiveDiff,
            selected_direction = selectedDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            base_stake_usd = baseStakeUsd,
            uncapped_progress_stake_multiplier = uncappedProgressStakeMultiplier,
            progress_stake_multiplier_cap = DiffProgressMaxStakeMultiplier,
            progress_stake_multiplier = progressStakeMultiplier,
            progress_stake_multiplier_capped = progressStakeMultiplier is not null &&
                uncappedProgressStakeMultiplier is not null &&
                progressStakeMultiplier < uncappedProgressStakeMultiplier,
            progress_stake_usd = progressStakeUsd,
            target_notional_usd = progressStakeUsd ?? baseStakeUsd,
            skip_reason = reason
        });
    }

    private static string BuildDiffShiftProgressRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal unitStakeUsd,
        DateTimeOffset nowUtc,
        CryptoUpDown5mDiffShiftProgressState? state,
        BtcPriceDirection? triggerDirection,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        DateTimeOffset? targetMarketStartUtc,
        DateTimeOffset? resultFetchStartUtc,
        int appliedResultCount,
        decimal? pendingSumDeltaUsd,
        int shiftCount,
        decimal? stakeMultiplier,
        decimal? stakeUsd,
        string? reason,
        int? threshold = null,
        string? progressMode = null,
        string? counterResultSource = null,
        string? premarketResultOutcome = null,
        decimal? premarketMoveBps = null,
        string? premarketSignalReason = null)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var effectiveDiff = state is null || triggerDirection is null
            ? (int?)null
            : GetDiffShiftProgressEffectiveDiff(state, triggerDirection.Value);
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = FakOrderType,
            order_type = FakOrderType,
            post_only = false,
            diff_shift_progress_enabled = true,
            strategy_code = variant.Code,
            strategy_category = variant.Category,
            decision_source = "persistent_diff_shift_progress",
            reference_asset_symbol = GetReferenceAssetSymbol(variant),
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            counter_mode = "persistent_diff_shift_progress",
            counter_result_source = counterResultSource ?? "ResolvedMarketLedger",
            counter_target_market_start_utc = targetMarketStartUtc,
            result_fetch_start_utc = resultFetchStartUtc,
            applied_result_count = appliedResultCount,
            up_count = state?.UpCount,
            down_count = state?.DownCount,
            diff = state is null ? (int?)null : state.UpCount - state.DownCount,
            effective_diff = effectiveDiff,
            threshold = threshold,
            progress_mode = progressMode,
            damping_active = state?.DampingActive,
            damping_direction = state?.DampingDirection,
            sum_amount = state?.SumAmount,
            unit_stake_usd = unitStakeUsd,
            pending_market_start_utc = state?.PendingMarketStartUtc,
            pending_target_outcome = state?.PendingTargetOutcome,
            pending_stake_usd = state?.PendingStakeUsd,
            pending_sum_delta_usd = pendingSumDeltaUsd,
            shift_count = shiftCount,
            premarket_result_outcome = premarketResultOutcome,
            premarket_move_bps = premarketMoveBps,
            premarket_signal_rejection_reason = premarketSignalReason,
            trigger_side = triggerDirection?.ToString(),
            diff_counter_trigger_outcome = variant.DiffCounterTriggerOutcome?.ToString(),
            selected_direction = selectedDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            stake_multiplier = stakeMultiplier,
            stake_usd = stakeUsd,
            target_notional_usd = stakeUsd ?? unitStakeUsd,
            skip_reason = reason
        });
    }

    private static string BuildDiffLimitProgressRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal unitStakeUsd,
        DateTimeOffset nowUtc,
        CryptoUpDown5mDiffShiftProgressState? state,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        DateTimeOffset? targetMarketStartUtc,
        DateTimeOffset? resultFetchStartUtc,
        int appliedResultCount,
        decimal? pendingSumDeltaUsd,
        bool utcDayResetApplied,
        int multiplierLimit,
        decimal? stakeMultiplier,
        decimal? stakeUsd,
        string? counterResultSource,
        string? premarketResultOutcome,
        decimal? premarketMoveBps,
        string? premarketSignalReason,
        string? reason)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var rawDiff = state is null ? (int?)null : state.UpCount - state.DownCount;
        var uncappedStakeMultiplier = rawDiff.HasValue && rawDiff.Value != 0
            ? Math.Abs(rawDiff.Value)
            : (int?)null;
        var realLimitProgress = variant.Behavior == BtcUpDown5mStrategyBehavior.DiffRealLimitProgressPremarket;
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = FakOrderType,
            order_type = FakOrderType,
            post_only = false,
            diff_limit_progress_premarket_enabled = true,
            diff_real_limit_progress_premarket_enabled = realLimitProgress,
            strategy_code = variant.Code,
            strategy_category = variant.Category,
            decision_source = realLimitProgress
                ? "persistent_utc_day_diff_real_limit_progress_premarket"
                : "persistent_utc_day_diff_limit_progress_premarket",
            reference_asset_symbol = GetReferenceAssetSymbol(variant),
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            counter_mode = realLimitProgress
                ? "persistent_utc_day_diff_real_limit_progress"
                : "persistent_utc_day_diff_limit_progress",
            counter_real_limit_enabled = realLimitProgress,
            counter_real_limit = realLimitProgress ? multiplierLimit : (int?)null,
            counter_result_source = counterResultSource ?? "ResolvedMarketLedger",
            counter_day_start_utc = marketStartUtc is { } startUtc
                ? GetDiffCounterUtcDayStartMarketStartUtc(startUtc)
                : (DateTimeOffset?)null,
            counter_target_market_start_utc = targetMarketStartUtc,
            result_fetch_start_utc = resultFetchStartUtc,
            applied_result_count = appliedResultCount,
            utc_day_reset_applied = utcDayResetApplied,
            up_count = state?.UpCount,
            down_count = state?.DownCount,
            diff = rawDiff,
            raw_diff = rawDiff,
            sum_amount = state?.SumAmount,
            unit_stake_usd = unitStakeUsd,
            pending_market_start_utc = state?.PendingMarketStartUtc,
            pending_target_outcome = state?.PendingTargetOutcome,
            pending_stake_usd = state?.PendingStakeUsd,
            pending_sum_delta_usd = pendingSumDeltaUsd,
            premarket_result_outcome = premarketResultOutcome,
            premarket_move_bps = premarketMoveBps,
            premarket_signal_rejection_reason = premarketSignalReason,
            selected_direction = selectedDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            uncapped_stake_multiplier = uncappedStakeMultiplier,
            stake_multiplier_cap = multiplierLimit,
            stake_multiplier = stakeMultiplier,
            stake_multiplier_capped = stakeMultiplier is not null &&
                uncappedStakeMultiplier is not null &&
                stakeMultiplier < uncappedStakeMultiplier,
            stake_usd = stakeUsd,
            target_notional_usd = stakeUsd ?? unitStakeUsd,
            skip_reason = reason
        });
    }

    private static string BuildDiffReferenceAverageRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        DateTimeOffset? rollingStartUtc,
        DateTimeOffset? rollingEndUtc,
        DateTimeOffset? resultFetchStartUtc,
        DateTimeOffset? resultFetchEndUtc,
        int historicalResultCount,
        IReadOnlyList<DiffReferenceAverageSample> samples,
        IReadOnlyList<DiffReferenceAverageWindow> averages,
        DiffReferenceAverageWindow? selectedAverage,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        decimal? diffDeltaFromAverage,
        string? premarketResultOutcome,
        decimal? premarketMoveBps,
        string? premarketSignalReason,
        string? reason)
    {
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var currentSample = samples.LastOrDefault();
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = FakOrderType,
            order_type = FakOrderType,
            post_only = false,
            diff_reference_average_premarket_enabled = true,
            fak_stats_probe = true,
            strategy_code = variant.Code,
            strategy_category = variant.Category,
            decision_source = "rolling_24h_diff_reference_average_premarket",
            reference_asset_symbol = GetReferenceAssetSymbol(variant),
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            counter_mode = "rolling_24h_no_utc_day_reset",
            counter_result_source = "ResolvedMarketLedger+" + GetPremarketPreviousResultSource(variant),
            rolling_diff_window_hours = 24,
            rolling_start_market_start_utc = rollingStartUtc,
            rolling_end_market_start_utc = rollingEndUtc,
            result_fetch_start_utc = resultFetchStartUtc,
            result_fetch_end_utc = resultFetchEndUtc,
            historical_result_count = historicalResultCount,
            synthetic_premarket_result_included = premarketResultOutcome is not null,
            premarket_result_outcome = premarketResultOutcome,
            premarket_move_bps = premarketMoveBps,
            premarket_signal_rejection_reason = premarketSignalReason,
            diff_sample_count = samples.Count,
            current_diff_market_start_utc = currentSample?.MarketStartUtc,
            current_diff_result_outcome = currentSample?.WinningOutcome,
            current_diff_result_source = currentSample?.Source,
            up_count = currentSample?.UpCount,
            down_count = currentSample?.DownCount,
            diff = currentSample?.Diff,
            current_diff = currentSample?.Diff,
            selected_average_window = selectedAverage?.WindowLabel,
            selected_average_window_seconds = selectedAverage?.WindowSeconds,
            selected_average_sample_step_seconds = selectedAverage?.SampleStepSeconds,
            selected_average_sample_count = selectedAverage?.SampleCount,
            selected_average_expected_sample_count = selectedAverage?.ExpectedSampleCount,
            selected_average_is_full_window = selectedAverage?.IsFullWindow,
            selected_average_diff = selectedAverage?.AverageDiff,
            selected_average_abs_diff = selectedAverage?.AverageDiff is { } averageDiff ? Math.Abs(averageDiff) : (decimal?)null,
            selected_average_first_market_start_utc = selectedAverage?.FirstMarketStartUtc,
            selected_average_last_market_start_utc = selectedAverage?.LastMarketStartUtc,
            diff_average_count = averages.Count,
            diff_full_average_count = averages.Count(average => average.IsFullWindow),
            diff_averages = averages
                .Select(average => new
                {
                    window = average.WindowLabel,
                    window_seconds = average.WindowSeconds,
                    sample_step_seconds = average.SampleStepSeconds,
                    sample_count = average.SampleCount,
                    expected_sample_count = average.ExpectedSampleCount,
                    is_full_window = average.IsFullWindow,
                    average_diff = average.AverageDiff,
                    average_abs_diff = average.AverageDiff is { } itemAverageDiff ? Math.Abs(itemAverageDiff) : (decimal?)null,
                    first_market_start_utc = average.FirstMarketStartUtc,
                    last_market_start_utc = average.LastMarketStartUtc
                })
                .ToArray(),
            diff_delta_from_average = diffDeltaFromAverage,
            diff_abs_delta_from_average = diffDeltaFromAverage is { } delta ? Math.Abs(delta) : (decimal?)null,
            diff_reference_average_min_delta = GetDiffReferenceAverageMinDelta(variant),
            selected_direction = selectedDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            target_notional_usd = targetNotionalUsd,
            skip_reason = reason
        });
    }

    private static decimal? GetDiffCounterEffectiveDiff(
        DiffCounterSnapshot? snapshot,
        BtcPriceDirection? triggerDirection,
        bool useAdjustedDiff)
    {
        if (snapshot is null || triggerDirection is null)
        {
            return null;
        }

        var diff = useAdjustedDiff ? snapshot.AdjustedDiff : snapshot.Diff;
        return triggerDirection == BtcPriceDirection.Up
            ? diff
            : -diff;
    }

    private static int GetDiffShiftProgressEffectiveDiff(
        CryptoUpDown5mDiffShiftProgressState state,
        BtcPriceDirection triggerDirection)
    {
        return GetDiffShiftProgressEffectiveDiff(state.UpCount, state.DownCount, triggerDirection);
    }

    private static int GetDiffShiftProgressEffectiveDiff(
        int upCount,
        int downCount,
        BtcPriceDirection triggerDirection)
    {
        return triggerDirection == BtcPriceDirection.Up
            ? upCount - downCount
            : downCount - upCount;
    }

    private string BuildPreviousScoreCounterTrendRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        BtcPreviousScoreCounterTrendSignal? signal,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        string? reason)
    {
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var isFakCounterTrend = IsPreviousScoreCounterTrendFakEntry(variant);
        var isFakPremarket = IsPreviousScoreCounterTrendFakPremarketEntry(variant);
        var isFakRevert = IsPreviousScoreCounterTrendFakRevertEntry(variant);
        var isFakPreviousScore = isFakCounterTrend || isFakPremarket || isFakRevert;
        var orderExecutionMode = isFakPreviousScore ? FakOrderType : OpeningLimitOrderType;
        var limitPrice = isFakPreviousScore ? (decimal?)null : GetFixedDirectionLimitPrice(variant);
        var isFakPremarketRevert = isFakPremarket && isFakRevert;
        var directionMode = isFakPremarketRevert
            ? "previous_bias_same_direction_premarket_5_5m"
            : isFakRevert
            ? "previous_bias_same_direction"
            : isFakPremarket
                ? "countertrend_premarket_5_5m"
                : "countertrend";
        var referenceAssetSymbol = GetReferenceAssetSymbol(variant);
        var referenceAssetCode = referenceAssetSymbol.ToLowerInvariant();
        var decisionSourcePrefix = "previous_" + referenceAssetCode + "_market_time_weighted_winsor_score";
        var premarketScoredCurrentMarketStartUtc = marketStartUtc?.Subtract(
            BtcUpDown5mMarketAnalyzer.GetIntervalDuration(variant.MarketInterval));
        var premarketScoredPreviousMarketStartUtc = premarketScoredCurrentMarketStartUtc?.Subtract(
            BtcUpDown5mMarketAnalyzer.GetIntervalDuration(variant.MarketInterval));
        decimal? previousScoreBps = signal is null ? null : signal.Score * 10_000m;
        decimal? previousScoreAbsBps = previousScoreBps is { } scoreBps ? Math.Abs(scoreBps) : null;
        decimal? selectedSignalBps = signal?.SelectedDirection is null ? null : previousScoreAbsBps;
        return JsonSerializer.Serialize(new
        {
            pricing_mode = OpeningLimitPricingMode,
            order_execution_mode = orderExecutionMode,
            order_type = orderExecutionMode,
            post_only = false,
            strategy_code = variant.Code,
            decision_source = isFakPremarketRevert
                ? "previous_" + referenceAssetCode + "_5_5m_time_weighted_winsor_score_same_direction_premarket_fak"
                : isFakRevert
                ? decisionSourcePrefix + "_same_direction_fak"
                : isFakPremarket
                    ? "previous_" + referenceAssetCode + "_5_5m_time_weighted_winsor_score_countertrend_premarket_fak"
                    : isFakCounterTrend
                    ? decisionSourcePrefix + "_countertrend_fak"
                    : decisionSourcePrefix + "_countertrend",
            previous_score_direction_mode = directionMode,
            previous_score_countertrend_fak_enabled = isFakCounterTrend || (isFakPremarket && !isFakRevert),
            previous_score_countertrend_premarket_enabled = isFakPremarket && !isFakRevert,
            previous_score_same_direction_revert_enabled = isFakRevert,
            previous_score_same_direction_premarket_revert_enabled = isFakPremarketRevert,
            fak_stats_probe = isFakPreviousScore,
            quote_received_at_utc = nowUtc,
            condition_id = market.ConditionId,
            market_id = market.MarketId,
            market_slug = market.Slug,
            reference_asset_symbol = referenceAssetSymbol,
            reference_binance_symbol = referenceAssetSymbol + "USDT",
            market_start_utc = marketStartUtc,
            market_end_utc = market.EndDateUtc,
            entry_delay_seconds = variant.EntryDelaySeconds,
            entry_due_at_utc = entryDueAtUtc,
            decision_delay_ms = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            previous_market_id = signal?.PreviousMarketId,
            previous_market_slug = signal?.PreviousMarketSlug,
            previous_market_start_utc = signal?.PreviousMarketStartUtc,
            previous_market_end_utc = signal?.PreviousMarketEndUtc,
            previous_score_premarket_target_market_start_utc = isFakPremarket ? marketStartUtc : null,
            previous_score_premarket_scored_previous_market_start_utc = isFakPremarket ? premarketScoredPreviousMarketStartUtc : null,
            previous_score_premarket_scored_current_market_start_utc = isFakPremarket ? premarketScoredCurrentMarketStartUtc : null,
            previous_score_premarket_score_window_start_utc = isFakPremarket ? signal?.PreviousMarketStartUtc : null,
            previous_score_premarket_score_window_end_utc = isFakPremarket ? signal?.PreviousMarketEndUtc : null,
            previous_score_premarket_previous_market_seconds = isFakPremarket ? (decimal?)PreviousScoreCounterTrendPremarketCarryoverWindow.TotalSeconds : null,
            previous_score_premarket_current_market_seconds = isFakPremarket && marketStartUtc is not null && entryDueAtUtc is not null
                ? (decimal?)ToDecimalSeconds(entryDueAtUtc.Value - premarketScoredCurrentMarketStartUtc.GetValueOrDefault())
                : null,
            previous_score = signal?.Score,
            previous_score_bps = previousScoreBps,
            previous_score_abs_bps = previousScoreAbsBps,
            selected_signal_bps = selectedSignalBps,
            previous_bias = signal?.PreviousBias?.ToString() ?? "None",
            reference_start_price_usd = signal?.StartPriceUsd,
            reference_raw_sample_count = signal?.RawSampleCount,
            reference_valid_sample_count = signal?.ValidSampleCount,
            reference_segment_count = signal?.SegmentCount,
            reference_total_duration_seconds = signal?.TotalDurationSeconds,
            btc_start_price_usd = signal?.StartPriceUsd,
            btc_raw_sample_count = signal?.RawSampleCount,
            btc_valid_sample_count = signal?.ValidSampleCount,
            btc_segment_count = signal?.SegmentCount,
            btc_total_duration_seconds = signal?.TotalDurationSeconds,
            score_epsilon = options.PreviousScoreCounterTrendEpsilonScore,
            min_samples = options.PreviousScoreCounterTrendMinSamples,
            winsor_percent = options.PreviousScoreCounterTrendWinsorPercent,
            winsor_lower_bound = signal?.WinsorLowerBound,
            winsor_upper_bound = signal?.WinsorUpperBound,
            time_share_filter_enabled = options.PreviousScoreCounterTrendEnableTimeShareFilter,
            min_up_time_share = options.PreviousScoreCounterTrendMinUpTimeShare,
            min_down_time_share = options.PreviousScoreCounterTrendMinDownTimeShare,
            up_time_share = signal?.UpTimeShare,
            down_time_share = signal?.DownTimeShare,
            selected_direction = signal?.SelectedDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId,
            outcome = selectedOutcome?.Outcome,
            limit_price = limitPrice,
            target_notional_usd = targetNotionalUsd,
            target_size_shares = limitPrice is > 0m ? targetNotionalUsd / limitPrice.Value : (decimal?)null,
            skip_reason = reason
        });
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
        var referenceAssetSymbol = GetReferenceAssetSymbol(variant);
        var referenceBinanceSymbol = referenceAssetSymbol + "USDT";
        var moveUsd = currentPrice is not null && startPrice is { } start
            ? currentPrice.PriceUsd - start
            : (decimal?)null;
        var moveBps = moveUsd is { } move && startPrice is > 0m
            ? move / startPrice.Value * 10_000m
            : (decimal?)null;
        var absMoveBps = moveBps is { } actualMoveBps ? Math.Abs(actualMoveBps) : (decimal?)null;
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var root = new JsonObject
        {
            ["pricing_mode"] = OpeningLimitPricingMode,
            ["order_execution_mode"] = OpeningLimitOrderType,
            ["post_only"] = false,
            ["strategy_code"] = variant.Code,
            ["decision_source"] = "binance_trade_stream_market_start_relative",
            ["reference_asset_symbol"] = referenceAssetSymbol,
            ["reference_binance_symbol"] = referenceBinanceSymbol,
            ["quote_received_at_utc"] = nowUtc,
            ["condition_id"] = market.ConditionId,
            ["market_id"] = market.MarketId,
            ["market_slug"] = market.Slug,
            ["market_start_utc"] = marketStartUtc,
            ["market_end_utc"] = market.EndDateUtc,
            ["entry_delay_seconds"] = variant.EntryDelaySeconds,
            ["entry_due_at_utc"] = entryDueAtUtc,
            ["decision_delay_ms"] = GetDecisionDelayMilliseconds(entryDueAtUtc, nowUtc),
            ["reference_current_price_usd"] = currentPrice?.PriceUsd,
            ["reference_current_source_updated_at_utc"] = currentPrice?.SourceUpdatedAtUtc,
            ["reference_current_fetched_at_utc"] = currentPrice?.FetchedAtUtc,
            ["reference_current_source"] = currentPrice?.Source,
            ["reference_start_price_usd"] = startPrice,
            ["reference_move_from_start_usd"] = moveUsd,
            ["reference_move_from_start_bps"] = moveBps,
            ["reference_abs_move_from_start_bps"] = absMoveBps,
            ["reference_min_move_from_start_bps"] = GetBinanceStartRelativeMinMoveBps(variant),
            ["base_selected_direction"] = baseSelectedDirection?.ToString(),
            ["selected_direction"] = selectedDirection?.ToString(),
            ["asset_id"] = selectedOutcome?.AssetId,
            ["outcome"] = selectedOutcome?.Outcome,
            ["limit_price"] = limitPrice,
            ["target_notional_usd"] = targetNotionalUsd,
            ["target_size_shares"] = targetNotionalUsd / limitPrice,
            ["skip_reason"] = reason
        };

        if (string.Equals(referenceAssetSymbol, "BTC", StringComparison.OrdinalIgnoreCase))
        {
            root["btc_current_price_usd"] = currentPrice?.PriceUsd;
            root["btc_current_source_updated_at_utc"] = currentPrice?.SourceUpdatedAtUtc;
            root["btc_current_fetched_at_utc"] = currentPrice?.FetchedAtUtc;
            root["btc_current_source"] = currentPrice?.Source;
            root["btc_start_price_usd"] = startPrice;
            root["btc_move_from_start_usd"] = moveUsd;
            root["btc_move_from_start_bps"] = moveBps;
            root["btc_abs_move_from_start_bps"] = absMoveBps;
            root["btc_min_move_from_start_bps"] = GetBinanceStartRelativeMinMoveBps(variant);
        }
        else
        {
            root["crypto_asset_symbol"] = referenceAssetSymbol;
            root["crypto_current_price_usd"] = currentPrice?.PriceUsd;
            root["crypto_current_source_updated_at_utc"] = currentPrice?.SourceUpdatedAtUtc;
            root["crypto_current_fetched_at_utc"] = currentPrice?.FetchedAtUtc;
            root["crypto_current_source"] = currentPrice?.Source;
            root["crypto_start_price_usd"] = startPrice;
            root["crypto_move_from_start_usd"] = moveUsd;
            root["crypto_move_from_start_bps"] = moveBps;
            root["crypto_abs_move_from_start_bps"] = absMoveBps;
            root["crypto_min_move_from_start_bps"] = GetBinanceStartRelativeMinMoveBps(variant);
        }

        return root.ToJsonString();
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
        var marketStartUtc = GetMarketWindowStartUtc(market, variant);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var expectedPreviousMarketStarts = marketStartUtc is null
            ? Array.Empty<DateTimeOffset>()
            : GetExpectedPreviousMarketStarts(
                marketStartUtc.Value,
                variant.MarketInterval,
                requiredResults);
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
            reference_asset_symbol = GetReferenceAssetSymbol(variant),
            reference_binance_symbol = GetReferenceAssetSymbol(variant) + "USDT",
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

    private static string BuildSkipBpsThresholdRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        int requiredResults,
        IReadOnlyList<BtcSkipMarketResult> results,
        BtcPriceDirection? baseSelectedDirection,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        BtcPreviousMarketMoveSignal? moveSignal,
        string? reason)
    {
        return BuildSkipBpsThresholdRawDecisionJson(
            market,
            variant,
            targetNotionalUsd,
            nowUtc,
            requiredResults,
            results,
            baseSelectedDirection,
            selectedDirection,
            selectedOutcome,
            moveSignal,
            reason,
            closeBookDiagnostics: null);
    }

    private static string BuildSkipBpsThresholdRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        decimal targetNotionalUsd,
        DateTimeOffset nowUtc,
        int requiredResults,
        IReadOnlyList<BtcSkipMarketResult> results,
        BtcPriceDirection? baseSelectedDirection,
        BtcPriceDirection? selectedDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        BtcPreviousMarketMoveSignal? moveSignal,
        string? reason,
        IReadOnlyList<BtcSkipCloseBookDiagnostic>? closeBookDiagnostics)
    {
        var rawDecisionJson = BuildSkipConsecutiveResultsRawDecisionJson(
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
            closeBookDiagnostics);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(rawDecisionJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        var referenceAssetSymbol = GetReferenceAssetSymbol(variant);
        root["decision_source"] = IsFixedOutcomePreviousResultBpsFakPremarketEntry(variant)
            ? "reference_price_premarket_previous_crypto_move_threshold"
            : IsBtcReferenceVariant(variant)
                ? "clob_close_book_price_evidence_previous_btc_move_threshold"
                : "clob_close_book_price_evidence_previous_crypto_move_threshold";
        root["reference_asset_symbol"] = referenceAssetSymbol;
        root["reference_binance_symbol"] = referenceAssetSymbol + "USDT";
        root["previous_btc_move_threshold_enabled"] = true;
        root["previous_btc_min_move_from_start_bps"] = GetSkipPreviousResultMinMoveBps(variant);
        root["fixed_outcome_previous_result_bps_enabled"] =
            variant.Behavior is BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant or
                BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFak or
                BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket;
        root["fixed_outcome_previous_result_bps_fak_enabled"] =
            IsFixedOutcomePreviousResultBpsFakEntry(variant);
        root["premarket_previous_result_enabled"] = IsFixedOutcomePreviousResultBpsFakPremarketEntry(variant);
        root["premarket_previous_result_source"] = IsFixedOutcomePreviousResultBpsFakPremarketEntry(variant)
            ? GetPremarketPreviousResultSource(variant)
            : null;
        root["premarket_previous_result_sample_seconds_before_end"] = IsFixedOutcomePreviousResultBpsFakPremarketEntry(variant)
            ? GetPremarketPreviousResultSampleSecondsBeforeEnd(variant)
            : null;
        root["premarket_previous_result_sample_target_utc"] =
            IsFixedOutcomePreviousResultBpsFakPremarketEntry(variant) && moveSignal is not null
                ? moveSignal.PreviousMarketEndUtc.AddSeconds(-GetPremarketPreviousResultSampleSecondsBeforeEnd(variant))
                : null;
        root["fixed_outcome"] = variant.FixedOutcome?.ToString();
        root["previous_btc_move_rejection_reason"] = moveSignal?.RejectionReason;
        root["previous_btc_market_id"] = moveSignal?.PreviousMarketId;
        root["previous_btc_market_slug"] = moveSignal?.PreviousMarketSlug;
        root["previous_btc_market_start_utc"] = moveSignal?.PreviousMarketStartUtc;
        root["previous_btc_market_end_utc"] = moveSignal?.PreviousMarketEndUtc;
        root["previous_btc_raw_sample_count"] = moveSignal?.RawSampleCount;
        root["previous_btc_valid_sample_count"] = moveSignal?.ValidSampleCount;
        root["previous_btc_end_sampled_at_utc"] = moveSignal?.EndSampledAtUtc;
        root["previous_btc_end_sample_age_seconds"] = moveSignal?.EndSampleAgeSeconds;
        root["previous_btc_start_price_usd"] = moveSignal?.StartPriceUsd;
        root["previous_btc_end_price_usd"] = moveSignal?.EndPriceUsd;
        root["previous_btc_move_from_start_usd"] = moveSignal?.MoveUsd;
        root["previous_btc_move_from_start_bps"] = moveSignal?.MoveBps;
        root["previous_btc_abs_move_from_start_bps"] = moveSignal?.AbsMoveBps;
        root["previous_btc_streak_winning_outcome"] = moveSignal?.StreakWinningOutcome;
        root["previous_btc_streak_result_count"] = moveSignal?.StreakResultCount;
        root["previous_btc_close_book_streak_result_count"] = moveSignal?.CloseBookStreakResultCount;
        root["previous_btc_cumulative_move_from_start_bps"] = moveSignal?.CumulativeMoveBps;
        root["previous_btc_cumulative_abs_move_from_start_bps"] = moveSignal?.CumulativeAbsMoveBps;
        root["previous_btc_streak_truncated_reason"] = moveSignal?.StreakTruncatedReason;
        root["previous_btc_streak_moves"] = moveSignal?.StreakMoveComponents is null
            ? null
            : JsonSerializer.SerializeToNode(moveSignal.StreakMoveComponents
                .Select(component => new
                {
                    market_id = component.MarketId,
                    market_slug = component.MarketSlug,
                    market_start_utc = component.MarketStartUtc,
                    market_end_utc = component.MarketEndUtc,
                    winning_outcome = component.WinningOutcome,
                    raw_sample_count = component.RawSampleCount,
                    valid_sample_count = component.ValidSampleCount,
                    end_sampled_at_utc = component.EndSampledAtUtc,
                    end_sample_age_seconds = component.EndSampleAgeSeconds,
                    start_price_usd = component.StartPriceUsd,
                    end_price_usd = component.EndPriceUsd,
                    move_from_start_usd = component.MoveUsd,
                    move_from_start_bps = component.MoveBps,
                    abs_move_from_start_bps = component.AbsMoveBps
                })
                .ToArray());
        root["btc_previous_market_start_price_usd"] = moveSignal?.StartPriceUsd;
        root["btc_previous_market_end_price_usd"] = moveSignal?.EndPriceUsd;
        root["btc_previous_market_move_from_start_bps"] = moveSignal?.MoveBps;
        root["btc_previous_market_abs_move_from_start_bps"] = moveSignal?.AbsMoveBps;
        root["btc_previous_market_streak_result_count"] = moveSignal?.StreakResultCount;
        root["btc_previous_market_cumulative_move_from_start_bps"] = moveSignal?.CumulativeMoveBps;
        root["btc_previous_market_cumulative_abs_move_from_start_bps"] = moveSignal?.CumulativeAbsMoveBps;
        root["btc_previous_market_min_move_from_start_bps"] = GetSkipPreviousResultMinMoveBps(variant);
        root["previous_reference_asset_start_price_usd"] = moveSignal?.StartPriceUsd;
        root["previous_reference_asset_end_price_usd"] = moveSignal?.EndPriceUsd;
        root["previous_reference_asset_move_from_start_bps"] = moveSignal?.MoveBps;
        root["previous_reference_asset_abs_move_from_start_bps"] = moveSignal?.AbsMoveBps;
        root["previous_reference_asset_cumulative_move_from_start_bps"] = moveSignal?.CumulativeMoveBps;
        root["previous_reference_asset_cumulative_abs_move_from_start_bps"] = moveSignal?.CumulativeAbsMoveBps;
        root["previous_reference_asset_min_move_from_start_bps"] = GetSkipPreviousResultMinMoveBps(variant);
        root["skip_reason"] = reason;

        return root.ToJsonString();
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

    private static string BuildRestingTakerPaperEntryRawDecisionJson(
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        TakerOrderBookLookupResult orderBookLookup,
        decimal limitPrice,
        decimal targetNotionalUsd,
        BtcMinimumStakeSizing sizing,
        decimal clobGammaDiff,
        DateTimeOffset nowUtc,
        IReadOnlyList<BtcTakerOutcomePricingSnapshot>? outcomeSelectionSnapshots)
    {
        var orderBook = orderBookLookup.OrderBook;
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        var entryDueAtUtc = GetEntryDueAtUtc(marketStartUtc, variant);
        var cacheOrderBook = orderBookLookup.CacheOrderBook;
        decimal? noEstimatedFillPrice = null;
        decimal? noEstimatedFillShares = null;
        decimal? noEstimatedFillNotional = null;
        return JsonSerializer.Serialize(new
        {
            pricing_mode = "paper_taker_resting_limit",
            order_execution_mode = OpeningLimitOrderType,
            quote_pricing_mode = "resting_limit_no_executable_ask_depth",
            resting_limit_due_to_empty_ask_side = true,
            empty_side_reason = SignalReasonCodes.MissingOrderBookEmptySide,
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
            best_bid = orderBook?.BestBid,
            best_ask = orderBook?.BestAsk,
            spread = orderBook?.SpreadAbs,
            last_trade_price = orderBook?.LastTradePrice,
            tick_size = orderBook?.TickSize,
            min_order_size = orderBook?.MinOrderSize,
            has_executable_ask_depth = orderBook is not null && HasExecutableAskDepth(orderBook),
            strategy_entry_price_cap = TryGetStandardEntryPriceCap(variant),
            stake_multiplier = sizing.StakeMultiplier,
            minimum_stake_safety_multiplier = sizing.SafetyMultiplier,
            minimum_notional_usd = sizing.MinimumNotionalUsd,
            raw_target_notional_usd = sizing.RawTargetNotionalUsd,
            stake_notional_rounding = sizing.RoundingMode,
            target_notional_usd = targetNotionalUsd,
            target_size_shares = sizing.TargetSizeShares,
            max_allowed_price = limitPrice,
            limit_price = limitPrice,
            estimated_fill_price = noEstimatedFillPrice,
            estimated_fill_shares = noEstimatedFillShares,
            estimated_fill_notional = noEstimatedFillNotional,
            levels_used = 0,
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

        if (outcomeSelectionSnapshots is null)
        {
            return null;
        }

        return outcomeSelectionSnapshots.Any(snapshot => !snapshot.HasExecutableAskDepth)
            ? "clob_resting_limit"
            : "clob_executable_vwap";
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

    private static string BuildPreOpenSellExitRawDecisionJson(
        PolymarketGammaMarket market,
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        BtcPriceDirection selectedDirection,
        BtcPriceDirection? currentDirection,
        BtcUpDown5mOutcomeQuote? selectedOutcome,
        OpeningLimitFillSummary entryFillSummary,
        decimal positionSizeShares,
        decimal? sellLimitPrice,
        PreOpenSellOrderBookSnapshot? upSnapshot,
        PreOpenSellOrderBookSnapshot? downSnapshot,
        DateTimeOffset nowUtc,
        string? reason)
    {
        var selectedSnapshot = selectedDirection == BtcPriceDirection.Up ? upSnapshot : downSnapshot;
        var selectedBidDepthShares = selectedSnapshot?.OrderBook?.Bids
            .Where(level => level is { Price: > 0m, Size: > 0m } && level.Price <= 1m)
            .Sum(level => level.Size);

        return JsonSerializer.Serialize(new
        {
            pricing_mode = "preopen_sell_exit",
            order_execution_mode = OpeningLimitOrderType,
            post_only = false,
            execution_source = BtcPreOpenSellExitExecutionSource,
            strategy_code = variant.Code,
            strategy_category = variant.Category,
            market_interval = variant.MarketInterval.ToString(),
            preopen_lifetime_mode = variant.PreOpenLifetimeMode.ToString(),
            market_id = market.MarketId,
            market_slug = market.Slug,
            market_start_utc = run.MarketStartUtc,
            market_end_utc = run.MarketEndUtc,
            last_quarter_start_utc = GetLastQuarterStartUtc(run.MarketStartUtc, run.MarketEndUtc),
            sell_check_at_utc = nowUtc,
            entry_due_at_utc = run.EntryDueAtUtc,
            selected_direction = selectedDirection.ToString(),
            current_direction = currentDirection?.ToString(),
            asset_id = selectedOutcome?.AssetId ?? run.SelectedAssetId,
            outcome = selectedOutcome?.Outcome ?? run.SelectedOutcome,
            entry_order_id = run.PaperOrderId,
            entry_average_price = entryFillSummary.AverageFillPrice,
            entry_size_shares = entryFillSummary.SizeShares,
            entry_notional_usd = entryFillSummary.NotionalUsd,
            position_size_shares = positionSizeShares,
            sell_limit_price = sellLimitPrice,
            sell_notional_usd = sellLimitPrice is { } price ? price * positionSizeShares : (decimal?)null,
            selected_visible_bid_depth_shares = selectedBidDepthShares,
            up_book_source = upSnapshot?.Source,
            up_book_age_ms = upSnapshot?.Age is { } upAge ? (int)Math.Round(upAge.TotalMilliseconds) : (int?)null,
            up_book_rejection_reason = upSnapshot?.RejectionReason,
            up_snapshot_at_utc = upSnapshot?.OrderBook?.SnapshotAtUtc,
            up_best_bid = upSnapshot?.BestBid,
            up_best_ask = upSnapshot?.BestAsk,
            up_midpoint = upSnapshot?.Midpoint,
            down_book_source = downSnapshot?.Source,
            down_book_age_ms = downSnapshot?.Age is { } downAge ? (int)Math.Round(downAge.TotalMilliseconds) : (int?)null,
            down_book_rejection_reason = downSnapshot?.RejectionReason,
            down_snapshot_at_utc = downSnapshot?.OrderBook?.SnapshotAtUtc,
            down_best_bid = downSnapshot?.BestBid,
            down_best_ask = downSnapshot?.BestAsk,
            down_midpoint = downSnapshot?.Midpoint,
            skip_reason = reason
        });
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

    private Signal CreateSellSignal(
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        decimal executionPrice,
        decimal sizeShares,
        decimal notionalUsd,
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
            TradeSide.Sell,
            executionPrice,
            sizeShares,
            notionalUsd,
            nowUtc);

        return new Signal(
            Guid.NewGuid(),
            trade,
            Score: 100,
            Accepted: true,
            DecisionCode: variant.Code + "_sell_exit",
            Reasons: [],
            ProposedPaperPrice: executionPrice,
            ProposedSizeShares: sizeShares,
            ProposedNotionalUsd: notionalUsd,
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
        string? rawDecisionJson,
        string executionSource = "")
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
            RawDecisionJson: rawDecisionJson,
            ExecutionSource: executionSource);
    }

    private static PaperOrder CreatePendingPreOpenSellPaperOrder(
        Signal signal,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        decimal limitPrice,
        decimal sizeShares,
        decimal notionalUsd,
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc,
        string? rawDecisionJson)
    {
        return new PaperOrder(
            Guid.NewGuid(),
            signal.Id,
            variant.CopiedTraderWallet,
            PaperOrderStatus.Pending,
            TradeSide.Sell,
            outcome.AssetId,
            signal.LeaderTrade.ConditionId,
            outcome.Outcome,
            limitPrice,
            sizeShares,
            notionalUsd,
            nowUtc,
            expiresAtUtc,
            StrategyId: variant.Id,
            RawDecisionJson: rawDecisionJson,
            ExecutionSource: BtcPreOpenSellExitExecutionSource);
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

            var isPreOpenSellVariant = IsPreOpenFixedDirectionSellExit(variant);
            var localExpiresAtUtc = isPreOpenSellVariant
                ? effectiveMarketEndUtc.Value
                : effectiveMarketEndUtc.Value.AddSeconds(-Math.Max(0, options.OpeningLimitExpireBeforeMarketEndSeconds));
            var expirationMode = isPreOpenSellVariant
                ? "preopen_full_period_no_preclose_cancel"
                : "preopen_full_period";
            if (localExpiresAtUtc <= nowUtc)
            {
                return OpeningLimitExpirationDecision.Reject(
                    "opening_limit_full_period_expiration_elapsed",
                    configuredTtlSeconds,
                    options.OpeningLimitExpireBeforeMarketEndSeconds,
                    clobBufferSeconds,
                    localExpiresAtUtc,
                    expirationMode);
            }

            return OpeningLimitExpirationDecision.Enter(
                localExpiresAtUtc,
                localExpiresAtUtc.AddSeconds(clobBufferSeconds),
                nowUtc,
                configuredTtlSeconds,
                options.OpeningLimitExpireBeforeMarketEndSeconds,
                clobBufferSeconds,
                expirationMode);
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

    private sealed record PaperLiveShadowPlacementResult(bool Placed, LiveOrder? LiveOrder, bool KeepPaperEntry)
    {
        public static PaperLiveShadowPlacementResult NotPlaced(LiveOrder? liveOrder = null)
        {
            return new PaperLiveShadowPlacementResult(false, liveOrder, false);
        }

        public static PaperLiveShadowPlacementResult LiveSkippedKeepPaper()
        {
            return new PaperLiveShadowPlacementResult(false, null, true);
        }
    }

    private async Task<PaperLiveShadowPlacementResult> TryPlacePaperLiveShadowOrderAsync(
        Signal signal,
        BtcUpDown5mOutcomeQuote outcome,
        BtcUpDown5mStrategyVariant variant,
        PaperOrder paperOrder,
        decimal price,
        decimal liveStakeMultiplier,
        OpeningLimitExpirationDecision expiration,
        Guid correlationId,
        DateTimeOffset? marketStartUtc,
        DateTimeOffset? marketEndUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken,
        bool postOnly = false)
    {
        var isFakOrder = true;
        var liveOrderType = FakOrderType;
        price = ResolveFakGuaranteedWorstPrice();
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

        AddLiveMarketWindowValidation(validation, variant, marketStartUtc, marketEndUtc, nowUtc);

        if (price <= 0m || price >= 1m)
        {
            validation.Add("Live BUY limit price is invalid.");
        }

        if (liveStakeMultiplier <= 0m)
        {
            validation.Add("Strategy live stake multiplier must be greater than zero.");
        }

        var cancelDeadlineUtc = nowUtc;

        var exposureSnapshot = await exposureCache.GetSnapshotAsync(cancellationToken);
        var openLiveOrders = exposureSnapshot.OpenLiveOrders;
        if (OpenOrderDirectionGuard.FindOppositeLiveOutcomeOpenOrder(
                signal.LeaderTrade.ConditionId,
                outcome.Outcome,
                openLiveOrders) is { } oppositeBlock)
        {
            validation.Add(OpenOrderDirectionGuard.CreateValidationMessage(outcome.Outcome, oppositeBlock));
        }

        if (openLiveOrders.Any(order => nowUtc - order.CreatedAtUtc > TimeSpan.FromSeconds(liveTradingOptions.DefaultOrderTtlSeconds)))
        {
            validation.Add("A stale live order exists; live placement is locked until maintenance cancels it.");
        }

        var apiErrors = await repository.GetRecentApiErrorsAsync(cancellationToken: cancellationToken);
        var lockoutStart = nowUtc.AddMinutes(-liveTradingOptions.ApiErrorLockoutWindowMinutes);
        var recentPolymarketErrors = apiErrors.Count(error =>
            error.CreatedAtUtc >= lockoutStart &&
            LiveApiErrorLockoutPolicy.CountsForLiveOrderLockout(error));
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

        await LiveGeoblockPreflight.ValidateAsync(
            geoClient,
            liveTradingOptions,
            repository,
            validation,
            cancellationToken);

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

        var maxTradeNotional = liveTradingOptions.MaxTradeBankrollPct / 100m * paperTradingOptions.InitialBankrollUsd;
        var maxTotalDeployed = liveTradingOptions.MaxTotalDeployedPct / 100m * paperTradingOptions.InitialBankrollUsd;
        var maxMarketNotional = liveTradingOptions.MaxMarketBankrollPct / 100m * paperTradingOptions.InitialBankrollUsd;
        var maxNotional = Math.Min(liveTradingOptions.MaxOrderNotionalUsd, maxTradeNotional);
        var liveSizing = CreateLiveMinimumStakeSizing(orderBook, price, liveStakeMultiplier);
        if (!liveSizing.Available)
        {
            validation.Add("Live minimum stake sizing failed: " + (liveSizing.RejectionReason ?? "unknown"));
        }

        var sizeShares = liveSizing.TargetSizeShares;
        var liveNotional = liveSizing.TargetNotionalUsd;
        if (sizeShares <= 0m)
        {
            validation.Add("Live BUY size must be greater than zero.");
        }

        if (orderBook?.MinOrderSize is { } minOrderSize && sizeShares > 0m && sizeShares < minOrderSize)
        {
            validation.Add("Live order size is below the market minimum order size.");
        }

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

        AddLiveExposureValidation(
            validation,
            openLiveOrders,
            signal.LeaderTrade.ConditionId,
            liveNotional,
            maxMarketNotional,
            maxTotalDeployed);

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
                paperOrder.Id,
                postOnly,
                liveOrderType,
                liveNotional);
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
            return PaperLiveShadowPlacementResult.NotPlaced(rejectedOrder);
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
            paperOrder.Id,
            postOnly,
            liveOrderType,
            liveNotional);
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
            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(Guid.NewGuid(), "BtcUpDown5mPaperLiveShadowIntent", "Error", ex.Message, nowUtc),
                cancellationToken);
            return PaperLiveShadowPlacementResult.NotPlaced();
        }

        var submitUtc = DateTimeOffset.UtcNow;
        var request = CreateFakMarketBuyRequest(outcome, price, sizeShares, liveNotional, orderBook, submitUtc);
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
            return PaperLiveShadowPlacementResult.NotPlaced(errorOrder);
        }

        var placementStatus = MapPlacementStatus(result);
        var fillSummary = LiveOrderPlacementAccounting.FromPlacementResult(
            TradeSide.Buy,
            price,
            sizeShares,
            isFakOrder ? LiveOrderStatus.Matched : placementStatus,
            result,
            allowFilledSizeAboveRequested: isFakOrder);
        var status = placementStatus;
        var validationSummary = result.ErrorMessage ?? string.Empty;
        if (isFakOrder)
        {
            if (fillSummary.FilledSize > 0m)
            {
                status = LiveOrderStatus.Matched;
            }
            else if (result.Success)
            {
                status = LiveOrderStatus.Rejected;
                validationSummary = "FAK order reported no immediate fill.";
            }
        }

        var persistedSizeShares = isFakOrder && fillSummary.FilledSize > sizeShares
            ? fillSummary.FilledSize
            : sizeShares;
        var persistedRemainingSize = isFakOrder
            ? 0m
            : fillSummary.RemainingSize;
        var updatedLiveOrder = intent with
        {
            Status = status,
            OrderId = result.OrderId,
            SizeShares = persistedSizeShares,
            SubmittedAtUtc = status is LiveOrderStatus.Live or LiveOrderStatus.Delayed or LiveOrderStatus.Matched or LiveOrderStatus.Unmatched or LiveOrderStatus.Submitted
                ? DateTimeOffset.UtcNow
                : null,
            ResponseStatus = result.ResponseStatus,
            FilledSize = fillSummary.FilledSize,
            RemainingSize = persistedRemainingSize,
            AverageFillPrice = fillSummary.AverageFillPrice,
            FilledNotionalUsd = fillSummary.FilledNotionalUsd,
            CostBasisUsd = fillSummary.CostBasisUsd,
            RawResponseJson = string.IsNullOrWhiteSpace(result.RawResponseJson) ? "{}" : result.RawResponseJson,
            ValidationSummary = validationSummary,
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

            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(Guid.NewGuid(), "BtcUpDown5mPaperLiveShadowPersistSubmit", "Error", ex.Message, DateTimeOffset.UtcNow),
                cancellationToken);
            return PaperLiveShadowPlacementResult.NotPlaced(updatedLiveOrder);
        }

        exposureCache.ApplyLiveOrder(updatedLiveOrder);
        await repository.UpdatePaperLiveShadowDecisionLinksAsync(
            correlationId,
            signal.Id,
            paperOrder.Id,
            updatedLiveOrder.Id,
            result.Success && status != LiveOrderStatus.Rejected ? "live_submitted" : "live_rejected",
            DateTimeOffset.UtcNow,
            cancellationToken);
        await repository.AddLiveTradingEventAsync(
            new LiveTradingEvent(
                Guid.NewGuid(),
                "BtcUpDown5mPaperLiveShadowPlaceOrder",
                result.Success && status != LiveOrderStatus.Rejected ? "OK" : "Rejected",
                string.IsNullOrWhiteSpace(validationSummary) ? result.ResponseStatus : validationSummary,
                DateTimeOffset.UtcNow),
            cancellationToken);

        if (!result.Success || status is LiveOrderStatus.Rejected or LiveOrderStatus.Error)
        {
            await CancelPaperShadowOrderAsync(paperOrder, DateTimeOffset.UtcNow, cancellationToken);
            return PaperLiveShadowPlacementResult.NotPlaced(updatedLiveOrder);
        }

        return new PaperLiveShadowPlacementResult(
            status is LiveOrderStatus.Live or LiveOrderStatus.Delayed or LiveOrderStatus.Matched or LiveOrderStatus.Unmatched or LiveOrderStatus.Submitted,
            updatedLiveOrder,
            false);
    }

    private static void AddLiveMarketWindowValidation(
        ICollection<string> validation,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset? marketStartUtc,
        DateTimeOffset? marketEndUtc,
        DateTimeOffset nowUtc)
    {
        if (marketStartUtc is not { } startUtc)
        {
            validation.Add("5m market start time is unknown; live placement refused.");
        }
        else if (startUtc > nowUtc && !AllowsLivePremarketPlacement(variant))
        {
            validation.Add("5m market has not started yet; live placement refused.");
        }

        if (marketEndUtc is not { } endUtc)
        {
            validation.Add("5m market end time is unknown; live placement refused.");
        }
        else if (endUtc <= nowUtc)
        {
            validation.Add("5m market has already ended; live placement refused.");
        }
    }

    private static bool AllowsLivePremarketPlacement(BtcUpDown5mStrategyVariant variant)
    {
        return variant.EntryDelaySeconds < 0;
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
        price = ResolveFakGuaranteedWorstPrice();
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
        if (OpenOrderDirectionGuard.FindOppositeLiveOutcomeOpenOrder(
                signal.LeaderTrade.ConditionId,
                outcome.Outcome,
                openLiveOrders) is { } oppositeBlock)
        {
            validation.Add(OpenOrderDirectionGuard.CreateValidationMessage(outcome.Outcome, oppositeBlock));
        }

        if (openLiveOrders.Any(order => nowUtc - order.CreatedAtUtc > TimeSpan.FromSeconds(liveTradingOptions.DefaultOrderTtlSeconds)))
        {
            validation.Add("A stale live order exists; live placement is locked until maintenance cancels it.");
        }

        var apiErrors = await repository.GetRecentApiErrorsAsync(cancellationToken: cancellationToken);
        var lockoutStart = nowUtc.AddMinutes(-liveTradingOptions.ApiErrorLockoutWindowMinutes);
        var recentPolymarketErrors = apiErrors.Count(error =>
            error.CreatedAtUtc >= lockoutStart &&
            LiveApiErrorLockoutPolicy.CountsForLiveOrderLockout(error));
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

        await LiveGeoblockPreflight.ValidateAsync(
            geoClient,
            liveTradingOptions,
            repository,
            validation,
            cancellationToken);

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
            validation.Add("Live FAK BUY requires a fresh best ask.");
        }
        else if (price <= 0m || price > 1m)
        {
            validation.Add("Live FAK BUY worst price is invalid.");
        }
        else if (price < bestAsk)
        {
            validation.Add("Live FAK BUY worst price is below the current best ask.");
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

        AddLiveExposureValidation(
            validation,
            openLiveOrders,
            signal.LeaderTrade.ConditionId,
            liveNotional,
            maxMarketNotional,
            maxTotalDeployed);

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
                    nowUtc,
                    LiveOrderStatus.PreflightRejected,
                    null,
                    "preflight_rejected",
                    string.Join("; ", validation),
                    "{}",
                    FakOrderType,
                    liveNotional),
                "BtcUpDown5mLivePreflight",
                "Rejected",
                string.Join("; ", validation),
                cancellationToken);
            return false;
        }

        var submitUtc = DateTimeOffset.UtcNow;
        var liveOrderExpiresAtUtc = submitUtc;
        var request = CreateFakMarketBuyRequest(outcome, price, liveSizeShares, liveNotional, orderBook, submitUtc);
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
                        nowUtc,
                        LiveOrderStatus.Error,
                        null,
                        "error",
                        "Live order placement failed: " + ex.Message,
                        "{}",
                        FakOrderType,
                        liveNotional),
                "BtcUpDown5mLivePlaceOrder",
                "Error",
                ex.Message,
                cancellationToken);
            return false;
        }

        var placementStatus = MapPlacementStatus(result);
        var fillSummary = LiveOrderPlacementAccounting.FromPlacementResult(
            TradeSide.Buy,
            price,
            liveSizeShares,
            LiveOrderStatus.Matched,
            result,
            allowFilledSizeAboveRequested: true);
        var status = placementStatus;
        var validationSummary = result.ErrorMessage ?? string.Empty;
        if (fillSummary.FilledSize > 0m)
        {
            status = LiveOrderStatus.Matched;
        }
        else if (result.Success)
        {
            status = LiveOrderStatus.Rejected;
            validationSummary = "FAK order reported no immediate fill.";
        }

        var persistedSizeShares = fillSummary.FilledSize > liveSizeShares
            ? fillSummary.FilledSize
            : liveSizeShares;
        var liveOrder = CreateLiveOrder(
            signal,
            outcome,
            variant,
            price,
            persistedSizeShares,
            nowUtc,
            liveOrderExpiresAtUtc,
            status,
            result.OrderId,
            result.ResponseStatus,
            validationSummary,
            string.IsNullOrWhiteSpace(result.RawResponseJson) ? "{}" : result.RawResponseJson,
            FakOrderType,
            liveNotional) with
        {
            FilledSize = fillSummary.FilledSize,
            RemainingSize = 0m,
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
        return result.Success && liveOrder.Status == LiveOrderStatus.Matched;
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
            "Strategy live available balance is insufficient. StrategyId={StrategyId} Available={AvailableBalance} Reserved={ReservedNotionalUsd} Required={RequiredNotionalUsd}. Current live order will be rejected, but live stakes remain enabled.",
            normalizedStrategyId,
            settings.LiveAvailableBalance,
            reservedNotionalUsd,
            requiredNotionalUsd);
        await repository.AddLiveTradingEventAsync(
            new LiveTradingEvent(Guid.NewGuid(), "StrategyLiveBalance", "Error", message, nowUtc),
            cancellationToken);
    }

    private static void AddLiveExposureValidation(
        ICollection<string> validation,
        IReadOnlyList<LiveOrder> openLiveOrders,
        string conditionId,
        decimal liveNotional,
        decimal maxMarketNotional,
        decimal maxTotalDeployed)
    {
        var marketExposure = openLiveOrders
            .Where(order => string.Equals(order.ConditionId, conditionId, StringComparison.OrdinalIgnoreCase))
            .Sum(order => order.NotionalUsd);
        var totalExposure = openLiveOrders.Sum(order => order.NotionalUsd);

        if (marketExposure + liveNotional > maxMarketNotional)
        {
            validation.Add("Live market exposure would exceed configured limit.");
        }

        if (totalExposure + liveNotional > maxTotalDeployed)
        {
            validation.Add("Live total deployed exposure would exceed configured limit.");
        }
    }

    private ClobV2OrderRequest CreateFakMarketBuyRequest(
        BtcUpDown5mOutcomeQuote outcome,
        decimal worstPrice,
        decimal estimatedSizeShares,
        decimal marketBuyAmountUsd,
        OrderBookSnapshot? orderBook,
        DateTimeOffset createdAtUtc)
    {
        worstPrice = ResolveFakGuaranteedWorstPrice(orderBook);
        return new ClobV2OrderRequest(
            outcome.AssetId,
            TradeSide.Buy,
            worstPrice,
            estimatedSizeShares,
            orderBook?.TickSize ?? 0.01m,
            orderBook?.MinOrderSize ?? 1m,
            authOptions.FunderAddress,
            authOptions.SigningAddress,
            ParseSignatureType(authOptions.SignatureType),
            ClobV2OrderType.FAK,
            createdAtUtc,
            NegativeRisk: orderBook?.NegativeRisk ?? false,
            PostOnly: false,
            MarketBuyAmountUsd: marketBuyAmountUsd);
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
        string rawResponseJson,
        string orderType = FakOrderType,
        decimal? notionalUsd = null)
    {
        var orderNotionalUsd = notionalUsd ?? price * sizeShares;
        var fallbackFillSummary = status == LiveOrderStatus.Matched
            ? new LiveOrderFillSummary(sizeShares, 0m, price, orderNotionalUsd, orderNotionalUsd)
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
            orderNotionalUsd,
            orderType,
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
        Guid paperOrderId,
        bool postOnly = false,
        string orderType = FakOrderType,
        decimal? notionalUsd = null)
    {
        var orderNotionalUsd = notionalUsd ?? price * sizeShares;
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
            orderNotionalUsd,
            orderType,
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
            FilledNotionalUsd: status == LiveOrderStatus.Matched ? orderNotionalUsd : 0m,
            CostBasisUsd: status == LiveOrderStatus.Matched ? orderNotionalUsd : 0m,
            CorrelationId: correlationId,
            ExecutionSource: PaperLiveShadowTestSource,
            PostOnly: postOnly,
            PaperOrderId: paperOrderId);
    }

    private async Task<bool> ApplyPaperModeFillToPaperShadowAsync(
        PaperOrder paperOrder,
        StrategyMarketPaperRun run,
        decimal fillPrice,
        decimal fillNotional,
        decimal fillSize,
        decimal currentBid,
        string evidence,
        DateTimeOffset filledAtUtc,
        CancellationToken cancellationToken)
    {
        if (fillPrice <= 0m ||
            fillPrice > 1m ||
            fillNotional <= 0m ||
            fillSize <= 0m)
        {
            return false;
        }

        var actualPaperOrder = paperOrder with
        {
            Status = PaperOrderStatus.Filled,
            Price = fillPrice,
            SizeShares = fillSize,
            NotionalUsd = fillNotional,
            FilledAtUtc = filledAtUtc,
            CancelledAtUtc = null,
            ExecutionSource = BtcFakTakerPaperExecutionSource
        };

        var existingFills = await repository.GetPaperFillsForOrderAsync(paperOrder.Id, cancellationToken);
        if (existingFills.Count == 0)
        {
            var paperFill = new PaperFill(
                Guid.NewGuid(),
                paperOrder.Id,
                fillPrice,
                fillSize,
                filledAtUtc,
                evidence);
            await repository.AddPaperFillAsync(paperFill, cancellationToken);

            var positions = await repository.GetPaperPositionsAsync(cancellationToken);
            var currentPosition = FindPaperPosition(positions, actualPaperOrder);
            var updatedPosition = paperTradingEngine.ApplyBuyFill(
                currentPosition,
                actualPaperOrder,
                paperFill,
                currentBid,
                filledAtUtc);
            await repository.UpsertPaperPositionAsync(updatedPosition, cancellationToken);
            exposureCache.ApplyPaperPosition(updatedPosition);
            await repository.ActivatePaperCopiedLeaderPositionAsync(
                actualPaperOrder.Id,
                paperFill.SizeShares,
                paperFill.FilledAtUtc,
                cancellationToken);
        }

        await repository.UpdatePaperOrderAsync(actualPaperOrder, cancellationToken);
        exposureCache.ApplyPaperOrder(actualPaperOrder);
        await repository.UpdateStrategyMarketPaperRunAsync(
            run with
            {
                Status = StrategyMarketPaperRunStatuses.Entered,
                EntryPrice = fillPrice,
                StakeUsd = fillNotional,
                SizeShares = fillSize,
                EnteredAtUtc = filledAtUtc,
                SkipReason = null,
                SkipDiagnosticsJson = null,
                UpdatedAtUtc = filledAtUtc
            },
            cancellationToken);
        return true;
    }

    private async Task<bool> ApplyActualLiveFillToPaperShadowAsync(
        PaperOrder paperOrder,
        StrategyMarketPaperRun run,
        LiveOrder liveOrder,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var fillSize = liveOrder.FilledSize;
        var fillPrice = liveOrder.AverageFillPrice ??
            (fillSize > 0m && liveOrder.FilledNotionalUsd > 0m
                ? liveOrder.FilledNotionalUsd / fillSize
                : 0m);
        var fillNotional = liveOrder.FilledNotionalUsd > 0m
            ? liveOrder.FilledNotionalUsd
            : fillPrice * fillSize;
        if (liveOrder.Status != LiveOrderStatus.Matched ||
            fillSize <= 0m ||
            fillPrice <= 0m ||
            fillPrice > 1m ||
            fillNotional <= 0m)
        {
            await CancelPaperShadowOrderAsync(paperOrder, nowUtc, cancellationToken);
            return false;
        }

        var filledAtUtc = liveOrder.SubmittedAtUtc ?? liveOrder.UpdatedAtUtc;
        var rawDecisionJson = AttachPaperLiveShadowActualFillJson(
            paperOrder.RawDecisionJson,
            liveOrder,
            fillPrice,
            fillSize,
            fillNotional);
        var actualPaperOrder = paperOrder with
        {
            Status = PaperOrderStatus.Filled,
            Price = fillPrice,
            SizeShares = fillSize,
            NotionalUsd = fillNotional,
            FilledAtUtc = filledAtUtc,
            CancelledAtUtc = null,
            RawDecisionJson = rawDecisionJson,
            ExecutionSource = PaperLiveShadowActualFillExecutionSource
        };

        var existingFills = await repository.GetPaperFillsForOrderAsync(paperOrder.Id, cancellationToken);
        if (existingFills.Count == 0)
        {
            var paperFill = new PaperFill(
                Guid.NewGuid(),
                paperOrder.Id,
                fillPrice,
                fillSize,
                filledAtUtc,
                string.Concat(
                    "Paper live-shadow copied actual Live fill. LiveOrderId=",
                    liveOrder.Id.ToString("D"),
                    " Status=",
                    liveOrder.Status.ToString(),
                    " AvgFillPrice=",
                    fillPrice.ToString("0.########", CultureInfo.InvariantCulture),
                    " FilledSize=",
                    fillSize.ToString("0.########", CultureInfo.InvariantCulture),
                    " FilledNotionalUsd=",
                    fillNotional.ToString("0.########", CultureInfo.InvariantCulture),
                    "."));
            await repository.AddPaperFillAsync(paperFill, cancellationToken);

            var positions = await repository.GetPaperPositionsAsync(cancellationToken);
            var currentPosition = FindPaperPosition(positions, actualPaperOrder);
            var updatedPosition = paperTradingEngine.ApplyBuyFill(
                currentPosition,
                actualPaperOrder,
                paperFill,
                fillPrice,
                filledAtUtc);
            await repository.UpsertPaperPositionAsync(updatedPosition, cancellationToken);
            exposureCache.ApplyPaperPosition(updatedPosition);
            await repository.ActivatePaperCopiedLeaderPositionAsync(
                actualPaperOrder.Id,
                paperFill.SizeShares,
                paperFill.FilledAtUtc,
                cancellationToken);
        }

        await repository.UpdatePaperOrderAsync(actualPaperOrder, cancellationToken);
        exposureCache.ApplyPaperOrder(actualPaperOrder);
        await repository.UpdateStrategyMarketPaperRunAsync(
            run with
            {
                Status = StrategyMarketPaperRunStatuses.Entered,
                EntryPrice = fillPrice,
                StakeUsd = fillNotional,
                SizeShares = fillSize,
                EnteredAtUtc = filledAtUtc,
                SkipReason = null,
                SkipDiagnosticsJson = null,
                UpdatedAtUtc = filledAtUtc
            },
            cancellationToken);
        return true;
    }

    private static StrategyMarketPaperRun MarkPaperLiveShadowRunSkipped(
        StrategyMarketPaperRun run,
        PaperLiveShadowPlacementResult placementResult,
        DateTimeOffset nowUtc)
    {
        var reason = placementResult.LiveOrder?.Status switch
        {
            LiveOrderStatus.PreflightRejected => "paper_live_shadow_live_preflight_rejected",
            LiveOrderStatus.Rejected => "paper_live_shadow_live_rejected",
            LiveOrderStatus.Error => "paper_live_shadow_live_error",
            _ => "paper_live_shadow_live_not_filled"
        };
        return run with
        {
            Status = StrategyMarketPaperRunStatuses.Skipped,
            SkipReason = reason,
            UpdatedAtUtc = nowUtc
        };
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

    private static decimal ResolveFakGuaranteedWorstPrice(OrderBookSnapshot? _ = null)
    {
        return FakGuaranteedWorstPrice;
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

    private static decimal RoundUpToTick(decimal value, decimal tickSize)
    {
        if (value <= 0m || tickSize <= 0m)
        {
            return 0m;
        }

        return Math.Ceiling(value / tickSize) * tickSize;
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

    private async Task<T> TrackStrategyStageAsync<T>(
        Guid cycleId,
        string cycleKind,
        string? flowName,
        string stageName,
        string? detail,
        int? variantCount,
        int? runCount,
        DateTimeOffset? earliestEntryDueAtUtc,
        DateTimeOffset? latestEntryDueAtUtc,
        Func<CancellationToken, Task<T>> action,
        Func<T, StrategyStageOutcome?>? outcomeFactory,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await action(cancellationToken);
            stopwatch.Stop();
            await TryRecordStrategyStageTimingAsync(
                cycleId,
                cycleKind,
                flowName,
                stageName,
                detail,
                startedAtUtc,
                GetUtcNow(),
                stopwatch.ElapsedMilliseconds,
                variantCount,
                runCount,
                earliestEntryDueAtUtc,
                latestEntryDueAtUtc,
                outcomeFactory?.Invoke(result),
                succeeded: true,
                errorMessage: null,
                cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await TryRecordStrategyStageTimingAsync(
                cycleId,
                cycleKind,
                flowName,
                stageName,
                detail,
                startedAtUtc,
                GetUtcNow(),
                stopwatch.ElapsedMilliseconds,
                variantCount,
                runCount,
                earliestEntryDueAtUtc,
                latestEntryDueAtUtc,
                outcome: null,
                succeeded: false,
                errorMessage: ex.Message,
                cancellationToken);
            throw;
        }
    }

    private async Task TrackStrategyStageAsync(
        Guid cycleId,
        string cycleKind,
        string? flowName,
        string stageName,
        string? detail,
        int? variantCount,
        int? runCount,
        DateTimeOffset? earliestEntryDueAtUtc,
        DateTimeOffset? latestEntryDueAtUtc,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await TrackStrategyStageAsync<object?>(
            cycleId,
            cycleKind,
            flowName,
            stageName,
            detail,
            variantCount,
            runCount,
            earliestEntryDueAtUtc,
            latestEntryDueAtUtc,
            async token =>
            {
                await action(token);
                return null;
            },
            outcomeFactory: null,
            cancellationToken);
    }

    private async Task TryRecordStrategyStageTimingAsync(
        Guid cycleId,
        string cycleKind,
        string? flowName,
        string stageName,
        string? detail,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        long durationMilliseconds,
        int? variantCount,
        int? runCount,
        DateTimeOffset? earliestEntryDueAtUtc,
        DateTimeOffset? latestEntryDueAtUtc,
        StrategyStageOutcome? outcome,
        bool succeeded,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var timing = new BtcUpDown5mStrategyStageTiming(
            Guid.NewGuid(),
            cycleId,
            cycleKind,
            flowName,
            stageName,
            TrimDiagnosticText(detail, 500),
            startedAtUtc,
            completedAtUtc,
            Math.Max(0, durationMilliseconds),
            variantCount,
            outcome?.RunCount ?? runCount,
            outcome?.EntriesPlaced,
            outcome?.RunsSkipped,
            outcome?.RunsSettled,
            outcome?.MarketsObserved,
            outcome?.EarliestEntryDueAtUtc ?? earliestEntryDueAtUtc,
            outcome?.LatestEntryDueAtUtc ?? latestEntryDueAtUtc,
            succeeded,
            TrimDiagnosticText(errorMessage, 1_000),
            GetUtcNow());
        if (!ShouldPersistStrategyStageTiming(timing))
        {
            return;
        }

        try
        {
            await repository.AddBtcUpDown5mStrategyStageTimingAsync(timing, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist BTC Up/Down 5m strategy stage timing. CycleKind={CycleKind} Flow={FlowName} Stage={StageName}",
                cycleKind,
                flowName,
                stageName);
        }
    }

    private static bool ShouldPersistStrategyStageTiming(BtcUpDown5mStrategyStageTiming timing)
    {
        return !timing.Succeeded ||
            timing.DurationMilliseconds >= StrategyStageTimingMinDurationMs ||
            timing.RunCount.GetValueOrDefault() > 0 ||
            timing.EntriesPlaced.GetValueOrDefault() > 0 ||
            timing.RunsSkipped.GetValueOrDefault() > 0 ||
            timing.RunsSettled.GetValueOrDefault() > 0 ||
            timing.MarketsObserved.GetValueOrDefault() > 0;
    }

    private static StrategyStageOutcome CreateStageOutcome(BtcUpDown5mPaperStrategyResult result)
    {
        return new StrategyStageOutcome(
            RunCount: null,
            EntriesPlaced: result.EntriesPlaced,
            RunsSkipped: result.RunsSkipped,
            RunsSettled: result.RunsSettled,
            MarketsObserved: result.MarketsObserved);
    }

    private static StrategyStageOutcome CreateStageOutcome(ObserveMarketsResult result)
    {
        return new StrategyStageOutcome(
            RunCount: null,
            RunsSkipped: result.Skipped,
            MarketsObserved: result.Observed);
    }

    private static StrategyStageOutcome CreateStageOutcome(BtcMakerProcessResult result)
    {
        return new StrategyStageOutcome(
            RunCount: null,
            EntriesPlaced: result.EntriesPlaced,
            RunsSkipped: result.RunsSkipped,
            MarketsObserved: result.MarketsObserved);
    }

    private static StrategyStageOutcome CreateStageOutcome(EntryVariantFlowResult result)
    {
        return CreateStageOutcome(result.Result);
    }

    private static StrategyStageOutcome CreateStageOutcome((int EntriesPlaced, int RunsSkipped) result)
    {
        return new StrategyStageOutcome(
            RunCount: null,
            EntriesPlaced: result.EntriesPlaced,
            RunsSkipped: result.RunsSkipped);
    }

    private static StrategyStageOutcome CreateStageOutcome((int EntriesPlaced, int RunsSkipped)[] results)
    {
        return new StrategyStageOutcome(
            RunCount: results.Length,
            EntriesPlaced: results.Sum(item => item.EntriesPlaced),
            RunsSkipped: results.Sum(item => item.RunsSkipped));
    }

    private static StrategyStageOutcome CreateStageOutcome(IReadOnlyList<StrategyMarketPaperRun> runs)
    {
        return new StrategyStageOutcome(
            RunCount: runs.Count,
            EarliestEntryDueAtUtc: GetEarliestEntryDueAtUtc(runs),
            LatestEntryDueAtUtc: GetLatestEntryDueAtUtc(runs));
    }

    private static StrategyStageOutcome CreateStageOutcome(MiddleReferenceBulkSkipResult result)
    {
        return new StrategyStageOutcome(
            RunCount: result.RemainingRuns.Count,
            RunsSkipped: result.RunsSkipped,
            EarliestEntryDueAtUtc: GetEarliestEntryDueAtUtc(result.RemainingRuns),
            LatestEntryDueAtUtc: GetLatestEntryDueAtUtc(result.RemainingRuns));
    }

    private static StrategyStageOutcome CreateStageOutcome(PreviousResultReadyFilterResult result)
    {
        return new StrategyStageOutcome(
            RunCount: result.ReadyRuns.Count,
            RunsSkipped: result.RunsSkipped,
            EarliestEntryDueAtUtc: GetEarliestEntryDueAtUtc(result.ReadyRuns),
            LatestEntryDueAtUtc: GetLatestEntryDueAtUtc(result.ReadyRuns));
    }

    private static DateTimeOffset? GetEarliestEntryDueAtUtc(IReadOnlyList<StrategyMarketPaperRun> runs)
    {
        return runs.Count == 0 ? null : runs.Min(run => run.EntryDueAtUtc);
    }

    private static DateTimeOffset? GetLatestEntryDueAtUtc(IReadOnlyList<StrategyMarketPaperRun> runs)
    {
        return runs.Count == 0 ? null : runs.Max(run => run.EntryDueAtUtc);
    }

    private static string? TrimDiagnosticText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }

    private async Task SkipRunAsync(
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        string reason,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken,
        string? diagnosticsJson = null)
    {
        var skippedRun = CreateSkippedRun(run, reason, nowUtc, diagnosticsJson);
        await repository.UpdateStrategyMarketPaperRunAsync(skippedRun, cancellationToken);
        LogSkippedRun(skippedRun, variant);
    }

    private Task RecordEntryRunSkippedAsync(
        StrategyMarketPaperRun run,
        BtcUpDown5mStrategyVariant variant,
        string reason,
        DateTimeOffset nowUtc,
        DeferredPaperEntryPersistence deferredPersistence,
        CancellationToken cancellationToken,
        string? diagnosticsJson = null)
    {
        _ = cancellationToken;
        var skippedRun = CreateSkippedRun(run, reason, nowUtc, diagnosticsJson);
        deferredPersistence.AddStrategyRun(skippedRun);
        LogSkippedRun(skippedRun, variant);
        return Task.CompletedTask;
    }

    private void LogSkippedRun(StrategyMarketPaperRun run, BtcUpDown5mStrategyVariant variant)
    {
        if (run.SkipDiagnosticsJson is null)
        {
            logger.LogInformation(
                "BTC Up or Down 5m paper run skipped. Strategy={StrategyCode} Market={MarketSlug} Reason={Reason}",
                variant.Code,
                run.MarketSlug,
                run.SkipReason);
            return;
        }

        logger.LogInformation(
            "BTC Up or Down 5m paper run skipped. Strategy={StrategyCode} Market={MarketSlug} Reason={Reason} Diagnostics={Diagnostics}",
            variant.Code,
            run.MarketSlug,
            run.SkipReason,
            run.SkipDiagnosticsJson);
    }

    private static StrategyMarketPaperRun CreateSkippedRun(
        StrategyMarketPaperRun run,
        string reason,
        DateTimeOffset nowUtc,
        string? diagnosticsJson = null)
    {
        diagnosticsJson = string.IsNullOrWhiteSpace(diagnosticsJson) ||
            string.Equals(diagnosticsJson, "{}", StringComparison.Ordinal)
            ? null
            : diagnosticsJson;

        return run with
        {
            Status = StrategyMarketPaperRunStatuses.Skipped,
            SkipReason = reason,
            SkipDiagnosticsJson = diagnosticsJson,
            UpdatedAtUtc = nowUtc
        };
    }

    private static StrategyMarketPaperRun CreateEnteredRun(
        StrategyMarketPaperRun run,
        PolymarketGammaMarket market,
        BtcUpDown5mOutcomeQuote selectedOutcome,
        decimal entryPrice,
        decimal stakeUsd,
        decimal sizeShares,
        Guid signalId,
        Guid paperOrderId,
        DateTimeOffset nowUtc)
    {
        return run with
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
            EntryPrice = entryPrice,
            StakeUsd = stakeUsd,
            SizeShares = sizeShares,
            SignalId = signalId,
            PaperOrderId = paperOrderId,
            EnteredAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
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

    private sealed record BtcMakerProcessResult(
        int MarketsObserved,
        int EntriesPlaced,
        int RunsSkipped)
    {
        public static BtcMakerProcessResult Empty { get; } = new(0, 0, 0);
    }

    private sealed record EntryVariantFlowResult(
        BtcUpDown5mPaperStrategyResult Result,
        IReadOnlyList<PolymarketGammaMarket> ObservedMarkets,
        IReadOnlyDictionary<Guid, StrategyRuntimeSettings> StrategySettings)
    {
        public static EntryVariantFlowResult Empty(IReadOnlyDictionary<Guid, StrategyRuntimeSettings> strategySettings)
        {
            return new EntryVariantFlowResult(
                new BtcUpDown5mPaperStrategyResult(0, 0, 0, 0),
                [],
                strategySettings);
        }
    }

    private sealed record BtcMakerOrderResult(bool Placed, bool Skipped)
    {
        public static BtcMakerOrderResult PlacedResult { get; } = new(true, false);

        public static BtcMakerOrderResult SkippedResult { get; } = new(false, true);
    }

    private sealed record BtcMakerHighWaterState(
        decimal MaxBestAsk,
        int OrderSequence,
        int LastDecisionSlot,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? MarketEndUtc);

    private sealed record ActiveChildMirrorAssignment(
        StrategyChildParentAssignment Assignment,
        BtcUpDown5mStrategyVariant ChildVariant);

    private sealed record BtcMakerDecisionSlot(
        bool Available,
        int CurrentSlot,
        int MaxSlot);

    private sealed record LiveStrategyPrioritySnapshot(
        IReadOnlyDictionary<Guid, decimal> LiveRealizedPnlByStrategy,
        DateTimeOffset RefreshedAtUtc)
    {
        public static LiveStrategyPrioritySnapshot Empty { get; } = new(
            new Dictionary<Guid, decimal>(),
            DateTimeOffset.MinValue);
    }

    private sealed record BtcMakerPostOnlyPriceDecision(
        bool Available,
        decimal? LimitPrice,
        decimal TickSize,
        decimal? BestBid,
        decimal BestAsk,
        decimal? RawLimitPrice,
        int Attempts,
        string? RejectionReason)
    {
        public static BtcMakerPostOnlyPriceDecision Enter(
            decimal limitPrice,
            decimal tickSize,
            decimal? bestBid,
            decimal bestAsk,
            decimal rawLimitPrice,
            int attempts)
        {
            return new BtcMakerPostOnlyPriceDecision(
                true,
                limitPrice,
                tickSize,
                bestBid,
                bestAsk,
                rawLimitPrice,
                attempts,
                null);
        }

        public static BtcMakerPostOnlyPriceDecision Reject(
            string reason,
            decimal tickSize,
            decimal? bestBid,
            decimal bestAsk,
            decimal? rawLimitPrice = null,
            int attempts = 0)
        {
            return new BtcMakerPostOnlyPriceDecision(
                false,
                null,
                tickSize,
                bestBid,
                bestAsk,
                rawLimitPrice,
                attempts,
                reason);
        }
    }

    private sealed record PreOpenSellExitDecision(
        bool ShouldSell,
        string? RejectionReason,
        BtcPriceDirection? CurrentDirection,
        decimal? SellLimitPrice,
        BtcUpDown5mOutcomeQuote? SelectedOutcome,
        string RawDecisionJson);

    private sealed record PreOpenSellOrderBookSnapshot(
        OrderBookSnapshot? OrderBook,
        string Source,
        TimeSpan? Age,
        decimal? BestBid,
        decimal? BestAsk,
        decimal? Midpoint,
        string? RejectionReason);

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

    private sealed record BtcPreviousScoreCounterTrendSegment(
        decimal Deviation,
        decimal DurationSeconds);

    private sealed record BtcPreviousScoreCounterTrendSignal(
        bool ShouldEnter,
        string? RejectionReason,
        BtcPriceDirection? PreviousBias,
        BtcPriceDirection? SelectedDirection,
        decimal? Score,
        decimal? StartPriceUsd,
        int RawSampleCount,
        int ValidSampleCount,
        int SegmentCount,
        decimal TotalDurationSeconds,
        decimal? WinsorLowerBound,
        decimal? WinsorUpperBound,
        decimal UpTimeShare,
        decimal DownTimeShare,
        string? PreviousMarketId,
        string? PreviousMarketSlug,
        DateTimeOffset? PreviousMarketStartUtc,
        DateTimeOffset? PreviousMarketEndUtc)
    {
        public static BtcPreviousScoreCounterTrendSignal Reject(
            string RejectionReason,
            BtcPriceDirection? PreviousBias = null,
            decimal? Score = null,
            decimal? StartPriceUsd = null,
            int RawSampleCount = 0,
            int ValidSampleCount = 0,
            int SegmentCount = 0,
            decimal TotalDurationSeconds = 0m,
            decimal? WinsorLowerBound = null,
            decimal? WinsorUpperBound = null,
            decimal UpTimeShare = 0m,
            decimal DownTimeShare = 0m,
            string? PreviousMarketId = null,
            string? PreviousMarketSlug = null,
            DateTimeOffset? PreviousMarketStartUtc = null,
            DateTimeOffset? PreviousMarketEndUtc = null)
        {
            return new BtcPreviousScoreCounterTrendSignal(
                false,
                RejectionReason,
                PreviousBias,
                null,
                Score,
                StartPriceUsd,
                RawSampleCount,
                ValidSampleCount,
                SegmentCount,
                TotalDurationSeconds,
                WinsorLowerBound,
                WinsorUpperBound,
                UpTimeShare,
                DownTimeShare,
                PreviousMarketId,
                PreviousMarketSlug,
                PreviousMarketStartUtc,
                PreviousMarketEndUtc);
        }
    }

    private sealed record BtcPreviousMarketMoveSignal(
        bool ShouldEnter,
        string? RejectionReason,
        string? PreviousMarketId,
        string? PreviousMarketSlug,
        DateTimeOffset PreviousMarketStartUtc,
        DateTimeOffset PreviousMarketEndUtc,
        decimal MinMoveBps,
        int RawSampleCount,
        int ValidSampleCount,
        DateTimeOffset? EndSampledAtUtc,
        decimal? EndSampleAgeSeconds,
        decimal? StartPriceUsd,
        decimal? EndPriceUsd,
        decimal? MoveUsd,
        decimal? MoveBps,
        decimal? AbsMoveBps,
        string? StreakWinningOutcome = null,
        int StreakResultCount = 0,
        int CloseBookStreakResultCount = 0,
        decimal? CumulativeMoveBps = null,
        decimal? CumulativeAbsMoveBps = null,
        IReadOnlyList<BtcPreviousMarketMoveComponent>? StreakMoveComponents = null,
        IReadOnlyList<BtcSkipMarketResult>? StreakResults = null,
        IReadOnlyList<BtcSkipCloseBookDiagnostic>? CloseBookDiagnostics = null,
        string? StreakTruncatedReason = null,
        BtcPriceDirection? BaseSelectedDirection = null)
    {
        public BtcPreviousMarketMoveSignal WithMinMoveThreshold(decimal minMoveBps)
        {
            if (CumulativeAbsMoveBps is null)
            {
                return this with { MinMoveBps = minMoveBps };
            }

            return CumulativeAbsMoveBps < minMoveBps
                ? this with
                {
                    ShouldEnter = false,
                    RejectionReason = "btc_previous_market_move_below_bps_threshold",
                    MinMoveBps = minMoveBps
                }
                : this with
                {
                    ShouldEnter = true,
                    RejectionReason = null,
                    MinMoveBps = minMoveBps
                };
        }

        public static BtcPreviousMarketMoveSignal Reject(
            string RejectionReason,
            DateTimeOffset PreviousMarketStartUtc,
            DateTimeOffset PreviousMarketEndUtc,
            decimal MinMoveBps,
            string? PreviousMarketId = null,
            string? PreviousMarketSlug = null,
            int RawSampleCount = 0,
            int ValidSampleCount = 0,
            DateTimeOffset? EndSampledAtUtc = null,
            decimal? EndSampleAgeSeconds = null,
            decimal? StartPriceUsd = null,
            decimal? EndPriceUsd = null,
            decimal? MoveUsd = null,
            decimal? MoveBps = null,
            decimal? AbsMoveBps = null,
            string? StreakWinningOutcome = null,
            int StreakResultCount = 0,
            int CloseBookStreakResultCount = 0,
            decimal? CumulativeMoveBps = null,
            decimal? CumulativeAbsMoveBps = null,
            IReadOnlyList<BtcPreviousMarketMoveComponent>? StreakMoveComponents = null,
            IReadOnlyList<BtcSkipMarketResult>? StreakResults = null,
            IReadOnlyList<BtcSkipCloseBookDiagnostic>? CloseBookDiagnostics = null,
            string? StreakTruncatedReason = null,
            BtcPriceDirection? BaseSelectedDirection = null)
        {
            return new BtcPreviousMarketMoveSignal(
                false,
                RejectionReason,
                PreviousMarketId,
                PreviousMarketSlug,
                PreviousMarketStartUtc,
                PreviousMarketEndUtc,
                MinMoveBps,
                RawSampleCount,
                ValidSampleCount,
                EndSampledAtUtc,
                EndSampleAgeSeconds,
                StartPriceUsd,
                EndPriceUsd,
                MoveUsd,
                MoveBps,
                AbsMoveBps,
                StreakWinningOutcome,
                StreakResultCount,
                CloseBookStreakResultCount,
                CumulativeMoveBps,
                CumulativeAbsMoveBps,
                StreakMoveComponents,
                StreakResults,
                CloseBookDiagnostics,
                StreakTruncatedReason,
                BaseSelectedDirection);
        }
    }

    private sealed record BtcPreviousMarketMoveComponent(
        string MarketId,
        string MarketSlug,
        DateTimeOffset MarketStartUtc,
        DateTimeOffset MarketEndUtc,
        string WinningOutcome,
        int RawSampleCount,
        int ValidSampleCount,
        DateTimeOffset? EndSampledAtUtc,
        decimal? EndSampleAgeSeconds,
        decimal? StartPriceUsd,
        decimal? EndPriceUsd,
        decimal? MoveUsd,
        decimal? MoveBps,
        decimal? AbsMoveBps)
    {
        public static BtcPreviousMarketMoveComponent From(
            BtcSkipMarketResult result,
            BtcPreviousMarketMoveSignal signal)
        {
            return new BtcPreviousMarketMoveComponent(
                result.MarketId,
                result.MarketSlug,
                result.MarketStartUtc ?? signal.PreviousMarketStartUtc,
                result.MarketEndUtc ?? signal.PreviousMarketEndUtc,
                result.WinningOutcome,
                signal.RawSampleCount,
                signal.ValidSampleCount,
                signal.EndSampledAtUtc,
                signal.EndSampleAgeSeconds,
                signal.StartPriceUsd,
                signal.EndPriceUsd,
                signal.MoveUsd,
                signal.MoveBps,
                signal.AbsMoveBps);
        }
    }

    private sealed record BtcOpeningLimitSignalVote(
        string StrategyCode,
        bool ShouldEnter,
        string? SkipReason,
        BtcPriceDirection? Direction,
        string? Outcome,
        string? AssetId,
        decimal? LimitPriceOverride);

    private sealed record PreviousResultReadyCandidate(
        Guid RunId,
        string AssetSymbol,
        DateTimeOffset PreviousMarketStartUtc);

    private sealed record PreviousResultReadyFilterResult(
        IReadOnlyList<StrategyMarketPaperRun> ReadyRuns,
        int RunsSkipped);

    private readonly record struct AssetMarketStartKey(
        string AssetSymbol,
        DateTimeOffset MarketStartUtc);

    private enum DiffProgressMode
    {
        Waiting,
        Betting
    }

    private sealed class DiffProgressRuntimeState(
        DateTimeOffset counterStartMarketStartUtc,
        DateTimeOffset updatedAtUtc)
    {
        public DiffProgressMode Mode { get; private set; } = DiffProgressMode.Waiting;

        public DateTimeOffset CounterStartMarketStartUtc { get; private set; } = counterStartMarketStartUtc;

        public DateTimeOffset UpdatedAtUtc { get; private set; } = updatedAtUtc;

        public void ResetCounter(
            DateTimeOffset counterStartMarketStartUtc,
            DateTimeOffset updatedAtUtc)
        {
            CounterStartMarketStartUtc = counterStartMarketStartUtc;
            UpdatedAtUtc = updatedAtUtc;
        }

        public void EnterBetting(
            DateTimeOffset counterStartMarketStartUtc,
            DateTimeOffset updatedAtUtc)
        {
            Mode = DiffProgressMode.Betting;
            CounterStartMarketStartUtc = counterStartMarketStartUtc;
            UpdatedAtUtc = updatedAtUtc;
        }

        public void ExitToWaiting(
            DateTimeOffset counterStartMarketStartUtc,
            DateTimeOffset updatedAtUtc)
        {
            Mode = DiffProgressMode.Waiting;
            CounterStartMarketStartUtc = counterStartMarketStartUtc;
            UpdatedAtUtc = updatedAtUtc;
        }
    }

    private sealed record DiffCounterMarketResult(
        string MarketId,
        string ConditionId,
        string MarketSlug,
        DateTimeOffset MarketStartUtc,
        DateTimeOffset? MarketEndUtc,
        string WinningOutcome,
        string Source);

    private sealed record DiffReferenceAverageMarketResultsLookup(
        bool Succeeded,
        IReadOnlyList<DiffCounterMarketResult> Results,
        string? ErrorMessage)
    {
        public static DiffReferenceAverageMarketResultsLookup Success(
            IReadOnlyList<DiffCounterMarketResult> results)
        {
            return new DiffReferenceAverageMarketResultsLookup(true, results, null);
        }

        public static DiffReferenceAverageMarketResultsLookup Failure(string errorMessage)
        {
            return new DiffReferenceAverageMarketResultsLookup(false, [], errorMessage);
        }
    }

    private sealed record DiffReferenceAverageWindowSpec(
        string Label,
        TimeSpan Duration);

    private sealed record DiffReferenceAverageSample(
        DateTimeOffset MarketStartUtc,
        string WinningOutcome,
        string Source,
        int UpCount,
        int DownCount,
        int Diff);

    private sealed record DiffReferenceAverageWindow(
        string WindowLabel,
        int WindowSeconds,
        int SampleStepSeconds,
        int SampleCount,
        int ExpectedSampleCount,
        bool IsFullWindow,
        decimal? AverageDiff,
        DateTimeOffset? FirstMarketStartUtc,
        DateTimeOffset? LastMarketStartUtc);

    private sealed record DiffShiftProgressApplyResult(
        CryptoUpDown5mDiffShiftProgressState State,
        int AppliedResultCount,
        decimal? PendingSumDeltaUsd);

    private sealed record DiffShiftProgressShiftResult(
        CryptoUpDown5mDiffShiftProgressState State,
        int ShiftCount);

    private sealed record DiffCounterHistoryFetchFailure(
        string AssetSymbol,
        DateTimeOffset StartTimeMinUtc,
        DateTimeOffset StartTimeMaxUtc,
        DateTimeOffset RetryAfterUtc,
        string ErrorMessage,
        Exception Exception);

    private sealed record DiffCounterSnapshot(
        string AssetSymbol,
        bool Initialized,
        DateTimeOffset? InitializedAtUtc,
        DateTimeOffset? CounterStartMarketStartUtc,
        DateTimeOffset TargetMarketStartUtc,
        DateTimeOffset? LastIncludedMarketStartUtc,
        DateTimeOffset? HighWaterMarketStartUtc,
        DateTimeOffset? HistoryFetchFailedAtUtc,
        DateTimeOffset? HistoryFetchRetryAfterUtc,
        string? HistoryFetchErrorMessage,
        bool TargetMarketResultReceived,
        string? TargetMarketResultSource,
        int UpCount,
        int DownCount,
        int DiffCount,
        decimal TrendZero,
        decimal? TrendZeroEma,
        int ProcessedMarketCount,
        int ShiftDiffCount,
        int ShiftDiffPositiveAdjustments,
        int ShiftDiffNegativeAdjustments)
    {
        public int Diff => UpCount - DownCount;
        public decimal AdjustedDiff => Diff - TrendZero;
    }

    private sealed class DiffCounterState(string assetSymbol)
    {
        private readonly Dictionary<DateTimeOffset, DiffCounterMarketResult> resultsByMarketStartUtc = new();

        public string AssetSymbol { get; } = assetSymbol;

        public bool Initialized { get; private set; }

        public DateTimeOffset? InitializedAtUtc { get; private set; }

        public DateTimeOffset? CounterStartMarketStartUtc { get; private set; }

        public DateTimeOffset? HighWaterMarketStartUtc { get; private set; }

        public DateTimeOffset? HistoryFetchFailedAtUtc { get; private set; }

        public DateTimeOffset? HistoryFetchRetryAfterUtc { get; private set; }

        public string? HistoryFetchErrorMessage { get; private set; }

        public bool IsHistoryFetchBackoffActive(DateTimeOffset nowUtc)
        {
            return HistoryFetchRetryAfterUtc is { } retryAfterUtc && retryAfterUtc > nowUtc;
        }

        public void EnsureInitializedForCounterStart(
            DateTimeOffset counterStartMarketStartUtc,
            DateTimeOffset initializedAtUtc)
        {
            if (!Initialized)
            {
                Initialized = true;
                CounterStartMarketStartUtc = counterStartMarketStartUtc;
                HighWaterMarketStartUtc = counterStartMarketStartUtc.AddMinutes(-5);
                InitializedAtUtc = initializedAtUtc;
                return;
            }

            if (CounterStartMarketStartUtc is { } currentCounterStartMarketStartUtc &&
                currentCounterStartMarketStartUtc >= counterStartMarketStartUtc)
            {
                return;
            }

            foreach (var marketStartUtc in resultsByMarketStartUtc.Keys
                .Where(marketStartUtc => marketStartUtc < counterStartMarketStartUtc)
                .ToArray())
            {
                resultsByMarketStartUtc.Remove(marketStartUtc);
            }

            CounterStartMarketStartUtc = counterStartMarketStartUtc;
            HighWaterMarketStartUtc = counterStartMarketStartUtc.AddMinutes(-5);
            InitializedAtUtc = initializedAtUtc;
            MarkHistoryFetchSucceeded();
        }

        public void EnsureInitializedWithoutReset(
            DateTimeOffset counterStartMarketStartUtc,
            DateTimeOffset initializedAtUtc)
        {
            if (Initialized)
            {
                return;
            }

            Initialized = true;
            CounterStartMarketStartUtc = counterStartMarketStartUtc;
            HighWaterMarketStartUtc = counterStartMarketStartUtc.AddMinutes(-5);
            InitializedAtUtc = initializedAtUtc;
        }

        public void MarkHistoryFetchFailed(
            DateTimeOffset failedAtUtc,
            DateTimeOffset retryAfterUtc,
            string errorMessage)
        {
            HistoryFetchFailedAtUtc = failedAtUtc;
            HistoryFetchRetryAfterUtc = retryAfterUtc;
            HistoryFetchErrorMessage = errorMessage;
        }

        public void MarkHistoryFetchSucceeded()
        {
            HistoryFetchFailedAtUtc = null;
            HistoryFetchRetryAfterUtc = null;
            HistoryFetchErrorMessage = null;
        }

        public void Apply(IReadOnlyList<DiffCounterMarketResult> results)
        {
            foreach (var result in results)
            {
                resultsByMarketStartUtc[result.MarketStartUtc] = result;
                if (HighWaterMarketStartUtc is null || result.MarketStartUtc > HighWaterMarketStartUtc)
                {
                    HighWaterMarketStartUtc = result.MarketStartUtc;
                }
            }
        }

        public DiffCounterSnapshot ToSnapshot(
            DateTimeOffset targetMarketStartUtc,
            DateTimeOffset nowUtc,
            int shiftDiffCount)
        {
            _ = nowUtc;
            var counterStartMarketStartUtc = CounterStartMarketStartUtc;
            var included = resultsByMarketStartUtc.Values
                .Where(result =>
                    result.MarketStartUtc <= targetMarketStartUtc &&
                    (counterStartMarketStartUtc is null || result.MarketStartUtc >= counterStartMarketStartUtc))
                .OrderBy(result => result.MarketStartUtc)
                .ToArray();
            var normalizedShiftDiffCount = Math.Max(0, shiftDiffCount);
            var counts = normalizedShiftDiffCount > 0
                ? CalculateShiftDiffCounts(included, normalizedShiftDiffCount)
                : CalculateRawCounts(included);
            var lastIncludedMarketStartUtc = included.Length == 0
                ? (DateTimeOffset?)null
                : included.Max(result => result.MarketStartUtc);
            var targetMarketIsInsideCounterWindow = counterStartMarketStartUtc is null ||
                targetMarketStartUtc >= counterStartMarketStartUtc;
            DiffCounterMarketResult? targetResult = null;
            if (targetMarketIsInsideCounterWindow)
            {
                resultsByMarketStartUtc.TryGetValue(targetMarketStartUtc, out targetResult);
            }

            return new DiffCounterSnapshot(
                AssetSymbol,
                Initialized,
                InitializedAtUtc,
                counterStartMarketStartUtc,
                targetMarketStartUtc,
                lastIncludedMarketStartUtc,
                HighWaterMarketStartUtc,
                HistoryFetchFailedAtUtc,
                HistoryFetchRetryAfterUtc,
                HistoryFetchErrorMessage,
                targetResult is not null,
                targetResult?.Source,
                counts.UpCount,
                counts.DownCount,
                counts.DiffCount,
                counts.TrendZero,
                counts.TrendZeroEma,
                included.Length,
                normalizedShiftDiffCount,
                counts.ShiftDiffPositiveAdjustments,
                counts.ShiftDiffNegativeAdjustments);
        }

        private static DiffCounterCounts CalculateRawCounts(IReadOnlyList<DiffCounterMarketResult> included)
        {
            var upCount = 0;
            var downCount = 0;
            var diffCount = 0;
            var validResultCount = 0;
            decimal? trendZeroEma = null;
            var trendZero = 0m;
            var emaAlpha = 2m / (AdjustedDiffTrendZeroEmaPeriodPoints + 1m);

            foreach (var result in included)
            {
                if (string.Equals(result.WinningOutcome, "Up", StringComparison.OrdinalIgnoreCase))
                {
                    upCount++;
                }
                else if (string.Equals(result.WinningOutcome, "Down", StringComparison.OrdinalIgnoreCase))
                {
                    downCount++;
                }
                else
                {
                    continue;
                }

                validResultCount++;
                var diff = upCount - downCount;
                diffCount += diff;
                trendZeroEma = trendZeroEma is null
                    ? diff
                    : (emaAlpha * diff) + ((1m - emaAlpha) * trendZeroEma.Value);
                if (validResultCount >= AdjustedDiffTrendZeroWarmupPoints)
                {
                    trendZero = MoveAdjustedDiffTrendZero(trendZero, trendZeroEma.Value);
                }
            }

            return new DiffCounterCounts(
                upCount,
                downCount,
                diffCount,
                trendZero,
                trendZeroEma,
                ShiftDiffPositiveAdjustments: 0,
                ShiftDiffNegativeAdjustments: 0);
        }

        private static DiffCounterCounts CalculateShiftDiffCounts(
            IReadOnlyList<DiffCounterMarketResult> included,
            int shiftDiffCount)
        {
            var upCount = 0;
            var downCount = 0;
            var diffCount = 0;
            var positiveAdjustments = 0;
            var negativeAdjustments = 0;
            var shiftTrigger = (shiftDiffCount * 2) + 1;

            foreach (var result in included)
            {
                if (string.Equals(result.WinningOutcome, "Up", StringComparison.OrdinalIgnoreCase))
                {
                    upCount++;
                }
                else if (string.Equals(result.WinningOutcome, "Down", StringComparison.OrdinalIgnoreCase))
                {
                    downCount++;
                }
                else
                {
                    continue;
                }

                var diff = upCount - downCount;
                if (diff >= shiftTrigger)
                {
                    upCount -= shiftDiffCount;
                    positiveAdjustments++;
                    diff = upCount - downCount;
                }
                else if (diff <= -shiftTrigger)
                {
                    downCount -= shiftDiffCount;
                    negativeAdjustments++;
                    diff = upCount - downCount;
                }

                diffCount += diff;
            }

            return new DiffCounterCounts(
                upCount,
                downCount,
                diffCount,
                TrendZero: 0m,
                TrendZeroEma: null,
                positiveAdjustments,
                negativeAdjustments);
        }

        private static decimal MoveAdjustedDiffTrendZero(decimal currentTrendZero, decimal targetTrendZero)
        {
            var delta = targetTrendZero - currentTrendZero;
            if (Math.Abs(delta) < AdjustedDiffTrendZeroDeadband)
            {
                return currentTrendZero;
            }

            var step = Math.Min(Math.Abs(delta), AdjustedDiffTrendZeroMaxStep);
            var direction = delta > 0m ? 1m : -1m;
            return RoundToNearestHalf(currentTrendZero + (direction * step));
        }

        private static decimal RoundToNearestHalf(decimal value)
        {
            return Math.Round(value * 2m, 0, MidpointRounding.AwayFromZero) / 2m;
        }
    }

    private sealed record DiffCounterCounts(
        int UpCount,
        int DownCount,
        int DiffCount,
        decimal TrendZero,
        decimal? TrendZeroEma,
        int ShiftDiffPositiveAdjustments,
        int ShiftDiffNegativeAdjustments);

    private sealed record DiffShiftProgressPendingBet(
        CryptoUpDown5mDiffShiftProgressState State,
        DateTimeOffset MarketStartUtc,
        string TargetOutcome,
        decimal RequestedStakeUsd);

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
        decimal? LimitPriceOverride,
        decimal? StakeUsdOverride,
        DiffShiftProgressPendingBet? DiffShiftProgressPendingBet = null)
    {
        public static BtcOpeningLimitDecision Enter(
            BtcUpDown5mOutcomeQuote selectedOutcome,
            string rawDecisionJson,
            decimal? limitPriceOverride = null,
            decimal? stakeUsdOverride = null,
            DiffShiftProgressPendingBet? diffShiftProgressPendingBet = null)
        {
            return new BtcOpeningLimitDecision(true, selectedOutcome, null, rawDecisionJson, limitPriceOverride, stakeUsdOverride, diffShiftProgressPendingBet);
        }

        public static BtcOpeningLimitDecision Reject(
            string reason,
            string? rawDecisionJson = null,
            decimal? limitPriceOverride = null,
            decimal? stakeUsdOverride = null)
        {
            return new BtcOpeningLimitDecision(false, null, reason, string.IsNullOrWhiteSpace(rawDecisionJson) ? "{}" : rawDecisionJson, limitPriceOverride, stakeUsdOverride);
        }
    }

    private sealed record BtcOpeningLimitPriceDecision(
        bool ShouldEnter,
        decimal LimitPrice,
        string? SkipReason,
        string RawDecisionJson,
        TakerOrderBookLookupResult? OrderBookLookup)
    {
        public static BtcOpeningLimitPriceDecision Enter(
            decimal limitPrice,
            string rawDecisionJson,
            TakerOrderBookLookupResult? orderBookLookup = null)
        {
            return new BtcOpeningLimitPriceDecision(true, limitPrice, null, rawDecisionJson, orderBookLookup);
        }

        public static BtcOpeningLimitPriceDecision Reject(
            string reason,
            string rawDecisionJson)
        {
            return new BtcOpeningLimitPriceDecision(false, 0m, reason, rawDecisionJson, null);
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

    private sealed record BtcInstantOpeningLimitPriceDecision(
        bool Available,
        decimal LimitPrice,
        string? RejectionReason,
        string Source,
        TimeSpan? Age,
        OrderBookSnapshot? OrderBook,
        decimal? RawLimitPrice,
        decimal? TickSize,
        decimal? MaxAllowedPrice,
        decimal? TargetNotionalUsd,
        decimal? TargetSizeShares,
        decimal? ExecutableAskShares,
        decimal? ExecutableAskVwap,
        int LevelsUsed)
    {
        public static BtcInstantOpeningLimitPriceDecision Enter(
            decimal limitPrice,
            string source,
            TimeSpan? age,
            OrderBookSnapshot orderBook,
            decimal rawLimitPrice,
            decimal tickSize,
            decimal maxAllowedPrice,
            decimal targetNotionalUsd,
            decimal targetSizeShares,
            decimal executableAskShares,
            decimal? executableAskVwap,
            int levelsUsed)
        {
            return new BtcInstantOpeningLimitPriceDecision(
                true,
                limitPrice,
                null,
                source,
                age,
                orderBook,
                rawLimitPrice,
                tickSize,
                maxAllowedPrice,
                targetNotionalUsd,
                targetSizeShares,
                executableAskShares,
                executableAskVwap,
                levelsUsed);
        }

        public static BtcInstantOpeningLimitPriceDecision Reject(
            string reason,
            string source,
            TimeSpan? Age,
            OrderBookSnapshot? OrderBook,
            decimal? RawLimitPrice = null,
            decimal? TickSize = null,
            decimal? LimitPrice = null,
            decimal? MaxAllowedPrice = null,
            decimal? TargetNotionalUsd = null,
            decimal? TargetSizeShares = null,
            decimal? ExecutableAskShares = null,
            decimal? ExecutableAskVwap = null,
            int LevelsUsed = 0)
        {
            return new BtcInstantOpeningLimitPriceDecision(
                false,
                LimitPrice ?? 0m,
                reason,
                source,
                Age,
                OrderBook,
                RawLimitPrice,
                TickSize,
                MaxAllowedPrice,
                TargetNotionalUsd,
                TargetSizeShares,
                ExecutableAskShares,
                ExecutableAskVwap,
                LevelsUsed);
        }
    }

    private sealed class DeferredPaperEntryPersistence
    {
        private readonly object sync = new();
        private readonly List<Signal> signals = [];
        private readonly List<PaperOrder> paperOrders = [];
        private readonly List<PaperFill> paperFills = [];
        private readonly List<PaperPositionMaterialization> paperPositionMaterializations = [];
        private readonly List<PaperCopiedLeaderPositionActivation> copiedLeaderPositionActivations = [];
        private readonly List<StrategyMarketPaperRun> strategyRuns = [];

        public DeferredPaperEntryPersistence()
        {
        }

        public void AddPendingPaperEntry(
            Signal signal,
            PaperOrder order,
            StrategyMarketPaperRun run)
        {
            lock (sync)
            {
                signals.Add(signal);
                paperOrders.Add(order);
                strategyRuns.Add(run);
            }
        }

        public void AddFilledPaperEntry(
            Signal signal,
            PaperOrder order,
            PaperFill fill,
            StrategyMarketPaperRun run,
            decimal currentBid,
            DateTimeOffset nowUtc)
        {
            lock (sync)
            {
                signals.Add(signal);
                paperOrders.Add(order);
                paperFills.Add(fill);
                strategyRuns.Add(run);
                copiedLeaderPositionActivations.Add(new PaperCopiedLeaderPositionActivation(
                    order.Id,
                    fill.SizeShares,
                    fill.FilledAtUtc));
                paperPositionMaterializations.Add(new PaperPositionMaterialization(
                    order,
                    fill,
                    currentBid,
                    nowUtc));
            }
        }

        public void AddStrategyRun(StrategyMarketPaperRun run)
        {
            lock (sync)
            {
                strategyRuns.Add(run);
            }
        }

        public PaperEntryPersistenceBatch CreateBatch()
        {
            lock (sync)
            {
                return new PaperEntryPersistenceBatch(
                    signals.ToArray(),
                    paperOrders.ToArray(),
                    paperFills.ToArray(),
                    [],
                    copiedLeaderPositionActivations.ToArray(),
                    strategyRuns.ToArray())
                {
                    PaperPositionMaterializations = paperPositionMaterializations.ToArray()
                };
            }
        }
    }

    private sealed class BtcCurrentPriceLookupCache
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<BtcCurrentPriceLookupResult>>> lookups =
            new(StringComparer.OrdinalIgnoreCase);

        public Task<BtcCurrentPriceLookupResult> GetOrAddAsync(
            string key,
            Func<CancellationToken, Task<BtcCurrentPriceLookupResult>> lookupFactory,
            CancellationToken cancellationToken)
        {
            var lookup = lookups.GetOrAdd(
                key,
                static (cacheKey, state) => new Lazy<Task<BtcCurrentPriceLookupResult>>(
                    () => state.LookupFactory(state.CancellationToken),
                    LazyThreadSafetyMode.ExecutionAndPublication),
                (LookupFactory: lookupFactory, CancellationToken: cancellationToken));
            return lookup.Value;
        }
    }

    private sealed record BtcCurrentPriceLookupResult(
        BtcUsdReferencePricePoint? Price,
        string? ErrorMessage,
        string AssetSymbol = "BTC",
        string BinanceSymbol = "BTCUSDT")
    {
        public static BtcCurrentPriceLookupResult Success(
            BtcUsdReferencePricePoint price,
            string assetSymbol = "BTC",
            string binanceSymbol = "BTCUSDT")
        {
            return new BtcCurrentPriceLookupResult(price, null, assetSymbol, binanceSymbol);
        }

        public static BtcCurrentPriceLookupResult Failure(
            string errorMessage,
            string assetSymbol = "BTC",
            string binanceSymbol = "BTCUSDT")
        {
            return new BtcCurrentPriceLookupResult(null, errorMessage, assetSymbol, binanceSymbol);
        }
    }

    private sealed record StrategyStageOutcome(
        int? RunCount = null,
        int? EntriesPlaced = null,
        int? RunsSkipped = null,
        int? RunsSettled = null,
        int? MarketsObserved = null,
        DateTimeOffset? EarliestEntryDueAtUtc = null,
        DateTimeOffset? LatestEntryDueAtUtc = null);

    private sealed record MiddleReferenceBulkSkipResult(
        IReadOnlyList<StrategyMarketPaperRun> RemainingRuns,
        int RunsSkipped);

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

    private sealed record PreOpenSellExitSummary(
        decimal SoldSizeShares,
        decimal ProceedsUsd,
        DateTimeOffset? LastFilledAtUtc)
    {
        public static PreOpenSellExitSummary Empty { get; } = new(0m, 0m, null);
    }

    private sealed record BtcOpeningLimitTargetSizingEstimate(
        bool Available,
        string? RejectionReason,
        string Source,
        decimal SafetyMultiplier,
        string RoundingMode,
        decimal? MinOrderSize,
        decimal RawTargetNotionalUsd,
        decimal TargetNotionalUsd,
        decimal TargetSizeShares)
    {
        public static BtcOpeningLimitTargetSizingEstimate Reject(string reason, string source)
        {
            return new BtcOpeningLimitTargetSizingEstimate(
                Available: false,
                RejectionReason: reason,
                Source: source,
                SafetyMultiplier: MinimumStakeSafetyMultiplier,
                RoundingMode: string.Empty,
                MinOrderSize: null,
                RawTargetNotionalUsd: 0m,
                TargetNotionalUsd: 0m,
                TargetSizeShares: 0m);
        }
    }

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

    private sealed record ObserveCounters(
        int Observed,
        int Skipped);

    private readonly record struct StrategyMarketRunCacheKey(
        Guid StrategyId,
        string MarketId);

    private sealed record ObservedRunReservation(
        StrategyMarketRunCacheKey Key,
        StrategyMarketPaperRun Run);

    private enum EntryLatencyPhase
    {
        DecisionSemaphoreWait,
        MarketLookup,
        ReferenceDecision,
        OrderBook,
        PlacementLockWait
    }

    private sealed class EntryBatchLatencyMetrics
    {
        private readonly EntryLatencyAccumulator[] accumulators =
            Enumerable.Range(0, Enum.GetValues<EntryLatencyPhase>().Length)
                .Select(_ => new EntryLatencyAccumulator())
                .ToArray();

        public void Record(EntryLatencyPhase phase, TimeSpan elapsed)
        {
            accumulators[(int)phase].Record(elapsed);
        }

        public EntryBatchLatencySnapshot CreateSnapshot()
        {
            var decisionSemaphoreWait = accumulators[(int)EntryLatencyPhase.DecisionSemaphoreWait].CreateSnapshot();
            var marketLookup = accumulators[(int)EntryLatencyPhase.MarketLookup].CreateSnapshot();
            var referenceDecision = accumulators[(int)EntryLatencyPhase.ReferenceDecision].CreateSnapshot();
            var orderBook = accumulators[(int)EntryLatencyPhase.OrderBook].CreateSnapshot();
            var placementLockWait = accumulators[(int)EntryLatencyPhase.PlacementLockWait].CreateSnapshot();
            var detail = string.Join(
                '|',
                FormatPhase("decision_semaphore_wait", decisionSemaphoreWait),
                FormatPhase("market_lookup", marketLookup),
                FormatPhase("reference_decision", referenceDecision),
                FormatPhase("order_book", orderBook),
                FormatPhase("placement_lock_wait", placementLockWait));
            var maximumMilliseconds = new[]
                {
                    decisionSemaphoreWait.MaximumMilliseconds,
                    marketLookup.MaximumMilliseconds,
                    referenceDecision.MaximumMilliseconds,
                    orderBook.MaximumMilliseconds,
                    placementLockWait.MaximumMilliseconds
                }
                .Max();
            return new EntryBatchLatencySnapshot(detail, maximumMilliseconds);
        }

        private static string FormatPhase(string name, EntryLatencyPhaseSnapshot snapshot)
        {
            return string.Concat(
                name,
                "=count:",
                snapshot.Count.ToString(CultureInfo.InvariantCulture),
                ",total_ms:",
                snapshot.TotalMilliseconds.ToString(CultureInfo.InvariantCulture),
                ",max_ms:",
                snapshot.MaximumMilliseconds.ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class EntryLatencyAccumulator
    {
        private long count;
        private long totalTicks;
        private long maximumTicks;

        public void Record(TimeSpan elapsed)
        {
            var elapsedTicks = Math.Max(0, elapsed.Ticks);
            Interlocked.Increment(ref count);
            Interlocked.Add(ref totalTicks, elapsedTicks);

            var observedMaximum = Volatile.Read(ref maximumTicks);
            while (elapsedTicks > observedMaximum)
            {
                var previousMaximum = Interlocked.CompareExchange(
                    ref maximumTicks,
                    elapsedTicks,
                    observedMaximum);
                if (previousMaximum == observedMaximum)
                {
                    break;
                }

                observedMaximum = previousMaximum;
            }
        }

        public EntryLatencyPhaseSnapshot CreateSnapshot()
        {
            return new EntryLatencyPhaseSnapshot(
                Volatile.Read(ref count),
                ToMilliseconds(Volatile.Read(ref totalTicks)),
                ToMilliseconds(Volatile.Read(ref maximumTicks)));
        }

        private static long ToMilliseconds(long ticks)
        {
            return ticks <= 0
                ? 0
                : (long)Math.Ceiling(ticks / (double)TimeSpan.TicksPerMillisecond);
        }
    }

    private readonly record struct EntryLatencyPhaseSnapshot(
        long Count,
        long TotalMilliseconds,
        long MaximumMilliseconds);

    private readonly record struct EntryBatchLatencySnapshot(
        string Detail,
        long MaximumMilliseconds);

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

}
