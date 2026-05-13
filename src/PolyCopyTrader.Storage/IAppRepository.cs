using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public interface IAppRepository
{
    Task AddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default);

    Task<bool> TryAddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaderTrade>> GetRecentLeaderTradesAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddLeaderPositionAsync(LeaderPosition position, CancellationToken cancellationToken = default);

    Task AddTraderLeaderboardSnapshotsAsync(
        IReadOnlyList<TraderLeaderboardSnapshot> snapshots,
        CancellationToken cancellationToken = default);

    Task UpsertTraderDiscoveryCandidatesAsync(
        IReadOnlyList<TraderDiscoveryCandidate> candidates,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TraderDiscoveryCandidate>> GetRecentTraderDiscoveryCandidatesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<PolymarketDataApiTrader?> GetPolymarketDataApiTraderAsync(
        string wallet,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PolymarketDataApiTrader?>(null);
    }

    Task UpsertPolymarketDataApiTraderAsync(
        PolymarketDataApiTrader trader,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task<int> UpsertPolymarketDataApiTradersAsync(
        IReadOnlyList<PolymarketDataApiTrader> traders,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    Task<IReadOnlyList<PolymarketDataApiTrader>> GetPolymarketDataApiTradersForSyncAsync(
        int limit,
        DateTimeOffset incrementalSyncBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketDataApiTrader>>([]);
    }

    Task<IReadOnlyList<PolymarketDataApiTrader>> GetPolymarketDataApiTradersForRatingRefreshAsync(
        int limit,
        DateTimeOffset dueBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketDataApiTrader>>([]);
    }

    Task MarkPolymarketDataApiTraderSyncedAsync(
        string wallet,
        bool fullSync,
        int tradesFetched,
        int tradesInserted,
        DateTimeOffset? latestTradeTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task<PolymarketDataApiPerformanceRefreshResult> RefreshPolymarketDataApiPositionsAndPerformanceAsync(
        string wallet,
        IReadOnlyList<PolymarketDataApiPosition> currentPositions,
        IReadOnlyList<PolymarketDataApiPosition> closedPositions,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PolymarketDataApiPerformanceRefreshResult(0, 0, 0, 0, 0));
    }

    Task<IReadOnlyList<string>> GetMissingPolymarketLeaderboardCategoryMappingsAsync(
        string wallet,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    Task<IReadOnlyList<PolymarketCategoryMapping>> GetEnabledPolymarketCategoryMappingsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketCategoryMapping>>([]);
    }

    Task<int> UpsertPolymarketDataApiWalletCategoryRatingsAsync(
        IReadOnlyList<PolymarketDataApiWalletCategoryRating> ratings,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    Task MarkPolymarketDataApiTraderRatingRefreshedAsync(
        string wallet,
        DateTimeOffset refreshedAtUtc,
        DateTimeOffset nextRefreshAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task MarkPolymarketDataApiTraderRatingRefreshFailedAsync(
        string wallet,
        string errorMessage,
        DateTimeOffset nextRefreshAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task UpsertPolymarketGammaMarketAsync(
        PolymarketGammaMarket market,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketGammaMarket>> GetBtcUpDown5mGammaMarketsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>([]);
    }

    Task<IReadOnlyList<PolymarketGammaMarket>> GetBtcUpDownStrategyGammaMarketsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return GetBtcUpDown5mGammaMarketsAsync(limit, cancellationToken);
    }

    Task<IReadOnlyList<PolymarketGammaMarket>> GetCryptoUpDown5mGammaMarketsAsync(
        IReadOnlyCollection<string> assetSymbols,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>([]);
    }

    Task<PolymarketGammaMarket?> GetPolymarketGammaMarketAsync(
        string marketId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PolymarketGammaMarket?>(null);
    }

    Task<bool> TryAddStrategyMarketPaperRunAsync(
        StrategyMarketPaperRun run,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    Task<IReadOnlyList<StrategyMarketPaperRun>> GetDueStrategyMarketPaperRunsAsync(
        Guid strategyId,
        string status,
        DateTimeOffset dueBeforeUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyMarketPaperRun>>([]);
    }

    Task<IReadOnlyList<StrategyMarketPaperRun>> GetDueStrategyMarketPaperRunsAsync(
        IReadOnlyCollection<Guid> strategyIds,
        string status,
        DateTimeOffset dueBeforeUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyMarketPaperRun>>([]);
    }

    Task<IReadOnlyList<StrategyMarketPaperRun>> GetDueStrategyMarketPaperRunsAtEarliestDueAsync(
        IReadOnlyCollection<Guid> strategyIds,
        string status,
        DateTimeOffset dueBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyMarketPaperRun>>([]);
    }

    Task<IReadOnlyList<StrategyMarketPaperRun>> GetStrategyMarketPaperRunsForSettlementAsync(
        Guid strategyId,
        DateTimeOffset marketEndedBeforeUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyMarketPaperRun>>([]);
    }

    Task<IReadOnlyList<StrategyMarketPaperRun>> GetStrategyMarketPaperRunsForSettlementAsync(
        IReadOnlyCollection<Guid> strategyIds,
        DateTimeOffset marketEndedBeforeUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyMarketPaperRun>>([]);
    }

    Task<IReadOnlyList<StrategyMarketPaperRun>> GetPreOpenSellExitDueRunsAsync(
        IReadOnlyCollection<Guid> strategyIds,
        DateTimeOffset dueBeforeUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyMarketPaperRun>>([]);
    }

    Task<IReadOnlyList<StrategyMarketPaperRun>> GetRecentStrategyMarketPaperRunsAsync(
        Guid strategyId,
        string status,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyMarketPaperRun>>([]);
    }

    Task<IReadOnlyList<BtcUpDown5mMarketResult>> GetRecentBtcUpDown5mMarketResultsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<BtcUpDown5mMarketResult>>([]);
    }

    Task UpdateStrategyMarketPaperRunAsync(
        StrategyMarketPaperRun run,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SignalSummary>> GetRecentSignalsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddSignalRejectionAsync(SignalRejection rejection, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SignalRejection>> GetRecentSignalRejectionsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddPaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default);

    Task UpdatePaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default);

    Task<PaperOrder?> GetPaperOrderAsync(Guid paperOrderId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PaperOrder?>(null);
    }

    Task<PaperOrder?> GetPaperOrderByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PaperOrder?>(null);
    }

    Task<IReadOnlyList<PaperOrder>> GetPaperOrdersForStrategyAssetAsync(
        Guid strategyId,
        string copiedTraderWallet,
        string assetId,
        DateTimeOffset createdAfterUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperOrder>>([]);
    }

    Task<IReadOnlyList<PaperOrder>> GetRecentPaperOrdersAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddPaperFillAsync(PaperFill fill, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperFill>> GetRecentPaperFillsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperFill>> GetPaperFillsForOrderAsync(Guid paperOrderId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperFill>>([]);
    }

    Task UpsertPaperPositionAsync(PaperPosition position, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperPosition>> GetPaperPositionsAsync(CancellationToken cancellationToken = default);

    Task<bool> TryAddPaperPositionSettlementAsync(PaperPositionSettlement settlement, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperPositionSettlement>> GetRecentPaperPositionSettlementsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<int> RefreshPaperCopiedTraderPerformanceAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperCopiedTraderPerformance>> GetPaperCopiedTraderPerformanceAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<PaperCopiedTraderPerformance?> GetPaperCopiedTraderPerformanceAsync(
        string copiedTraderWallet,
        string category,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PaperCopiedTraderPerformance?>(null);
    }

    Task<IReadOnlyList<StrategyPerformance>> GetStrategyPerformanceAsync(int limit = 2000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyPerformance>>([]);
    }

    Task<IReadOnlyList<StrategyRecentPerformance>> GetStrategyRecentPerformanceAsync(int limit = 3000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyRecentPerformance>>([]);
    }

    Task<IReadOnlyDictionary<Guid, bool>> GetStrategyEnabledStatesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<Guid, bool>>(
            StrategyIds.AllStrategyIds.ToDictionary(strategyId => strategyId, _ => true));
    }

    Task<IReadOnlyDictionary<Guid, StrategyRuntimeSettings>> GetStrategyRuntimeSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<Guid, StrategyRuntimeSettings>>(
            StrategyIds.AllStrategyIds.ToDictionary(strategyId => strategyId, StrategyRuntimeSettings.Default));
    }

    Task<bool> SetStrategyEnabledAsync(
        Guid strategyId,
        bool enabled,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    Task<bool> SetStrategyLiveStakesAsync(
        Guid strategyId,
        bool liveStakes,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    Task<bool> SetStrategyStakeAmountsAsync(
        Guid strategyId,
        decimal paperStakeAmount,
        decimal liveStakeAmount,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    Task<bool> SetStrategyLiveAvailableBalanceAsync(
        Guid strategyId,
        decimal liveAvailableBalance,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    Task<bool> TryAddPaperCopiedLeaderPositionAsync(
        PaperCopiedLeaderPosition position,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    Task ActivatePaperCopiedLeaderPositionAsync(
        Guid entryPaperOrderId,
        decimal copiedInitialSizeShares,
        DateTimeOffset filledAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task<IReadOnlyList<PaperCopiedLeaderPosition>> GetPaperCopiedLeaderPositionsForExitTrackingAsync(
        int limit,
        DateTimeOffset dueBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperCopiedLeaderPosition>>([]);
    }

    Task MarkPaperCopiedLeaderPositionsActivitySyncedAsync(
        string copiedTraderWallet,
        DateTimeOffset syncedAtUtc,
        DateTimeOffset nextSyncAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task<bool> ApplyPaperCopiedLeaderExitAsync(
        PaperCopiedLeaderActivityEvent activityEvent,
        IReadOnlyList<PaperCopiedLeaderPositionExitUpdate> positionUpdates,
        IReadOnlyList<Signal> signals,
        IReadOnlyList<PaperOrder> paperOrders,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    Task AddDryRunOrderAsync(DryRunOrder order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DryRunOrder>> GetRecentDryRunOrdersAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default);

    Task UpdateLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LiveOrder>> GetOpenLiveOrdersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LiveOrder>> GetOpenLiveOrdersForStrategyOrCorrelationAsync(
        Guid strategyId,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveOrder>>([]);
    }

    Task<IReadOnlyList<LiveOrder>> GetMatchedLiveOrdersPendingBalanceSettlementAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveOrder>>([]);
    }

    Task<IReadOnlyList<LiveOrder>> GetRecentLiveOrdersAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<StrategyLiveBalanceAdjustmentResult> ApplyLiveOrderSettlementToStrategyBalanceAsync(
        Guid liveOrderId,
        Guid strategyId,
        decimal settlementValueUsd,
        decimal realizedPnlUsd,
        string? winningAssetId,
        string winningOutcome,
        DateTimeOffset settledAtUtc,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new StrategyLiveBalanceAdjustmentResult(false, 0m, false));
    }

    Task AddLiveTradingEventAsync(LiveTradingEvent liveEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LiveTradingEvent>> GetRecentLiveTradingEventsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddPaperLiveShadowDecisionAsync(PaperLiveShadowDecision decision, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task UpdatePaperLiveShadowDecisionLinksAsync(
        Guid correlationId,
        Guid? signalId,
        Guid? paperOrderId,
        Guid? liveOrderId,
        string status,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task AddPaperLiveShadowDiscrepancyAsync(PaperLiveShadowDiscrepancy discrepancy, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task AddBtcUsdReferenceCorrelationSampleAsync(
        BtcUsdReferenceCorrelationSample sample,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task<IReadOnlyList<BtcUsdReferenceCorrelationSample>> GetRecentBtcUsdReferenceCorrelationSamplesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<BtcUsdReferenceCorrelationSample>>([]);
    }

    Task AddBtcOrderBookLagDiagnosticEventsAsync(
        IReadOnlyList<BtcOrderBookLagDiagnosticEvent> events,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task<int> CleanupBtcOrderBookLagDiagnosticEventsAsync(
        DateTimeOffset receivedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    Task AddBtcUpDown5mOddsTickAsync(
        BtcUpDown5mOddsTick tick,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task<decimal?> GetBtcUpDown5mOddsStartPriceAsync(
        string marketId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<decimal?>(null);
    }

    Task<BtcUpDown5mOddsTick?> GetLatestBtcUpDown5mOddsTickAsync(
        string marketId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<BtcUpDown5mOddsTick?>(null);
    }

    Task<IReadOnlyList<BtcUpDown5mOddsTick>> GetRecentBtcUpDown5mOddsTicksAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<BtcUpDown5mOddsTick>>([]);
    }

    Task AddCryptoUpDown5mOddsTickAsync(
        CryptoUpDown5mOddsTick tick,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task<decimal?> GetCryptoUpDown5mOddsStartPriceAsync(
        string assetSymbol,
        string marketId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<decimal?>(null);
    }

    Task<IReadOnlyList<CryptoUpDown5mOddsTick>> GetRecentCryptoUpDown5mOddsTicksAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<CryptoUpDown5mOddsTick>>([]);
    }

    Task AddApiErrorAsync(ApiError error, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApiError>> GetRecentApiErrorsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddPolymarketHttpLogAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketHttpLogEntry>> GetRecentPolymarketHttpLogsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<PolymarketHttpLogCleanupResult> CleanupPolymarketHttpLogsAsync(
        DateTimeOffset successfulBeforeUtc,
        DateTimeOffset failedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task AddPolymarketOnChainLogsAsync(IReadOnlyList<PolymarketOnChainLog> logs, CancellationToken cancellationToken = default);

    Task AddPolymarketOnChainFillsAsync(IReadOnlyList<PolymarketOnChainFill> fills, CancellationToken cancellationToken = default);

    Task<int> AddPolymarketOnChainTradeCapturesAsync(IReadOnlyList<PolymarketOnChainTradeCapture> captures, CancellationToken cancellationToken = default);

    Task UpsertOnChainIngestionCursorAsync(OnChainIngestionCursor cursor, CancellationToken cancellationToken = default);

    Task<OnChainIngestionCursor?> GetOnChainIngestionCursorAsync(string contractAddress, CancellationToken cancellationToken = default);

    Task UpsertOnChainTradeCaptureCursorAsync(OnChainTradeCaptureCursor cursor, CancellationToken cancellationToken = default);

    Task<OnChainTradeCaptureCursor?> GetOnChainTradeCaptureCursorAsync(string contractAddress, CancellationToken cancellationToken = default);

    Task<long?> GetLatestPolymarketOnChainFillBlockAsync(string contractAddress, CancellationToken cancellationToken = default);

    Task<OnChainBlockRange?> GetPolymarketOnChainFillBlockRangeAsync(string contractAddress, CancellationToken cancellationToken = default);

    Task<OnChainBlockRange?> GetPolymarketOnChainWalletExecutionBlockRangeAsync(string contractAddress, CancellationToken cancellationToken = default);

    Task<OnChainBlockRange?> GetPolymarketOnChainTradeDetailsBlockRangeAsync(string contractAddress, CancellationToken cancellationToken = default);

    Task RefreshPolymarketOnChainWalletDerivedDataAsync(string contractAddress, long fromBlock, long toBlock, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketOnChainWalletExecution>> GetRecentPolymarketOnChainWalletExecutionsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetOnChainTokenIdsMissingMetadataAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<PolymarketOnChainTokenMetadata?> GetPolymarketOnChainTokenMetadataAsync(string tokenId, CancellationToken cancellationToken = default);

    Task UpsertPolymarketOnChainTokenMetadataAsync(IReadOnlyList<PolymarketOnChainTokenMetadata> metadata, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketOnChainFill>> GetRecentPolymarketOnChainFillsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TraderOnChainStats>> GetTraderOnChainStatsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<OnChainActivityRefreshResult> RefreshPolymarketOnChainWalletActivityAsync(
        int walletLimit = 100,
        int queueSeedWalletLimit = 500,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketOnChainWalletPosition>> GetPolymarketOnChainWalletPositionsAsync(int limit = 250, CancellationToken cancellationToken = default);

    Task<OnChainPositionRefreshResult> RefreshPolymarketOnChainWalletPositionsAsync(
        int tokenLimit = 50,
        int queueSeedTokenLimit = 500,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketOnChainWalletPerformance>> GetPolymarketOnChainWalletPerformanceAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<OnChainPerformanceRefreshResult> RefreshPolymarketOnChainWalletPerformanceAsync(
        int walletLimit = 100,
        int queueSeedWalletLimit = 500,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketOnChainWalletCategoryPerformance>> GetPolymarketOnChainWalletCategoryPerformanceAsync(
        string? category = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<PolymarketOnChainWalletCategoryPerformance?> GetPolymarketOnChainWalletCategoryPerformanceAsync(
        string wallet,
        string category,
        CancellationToken cancellationToken = default);

    Task<OnChainCategoryPerformanceRefreshResult> RefreshPolymarketOnChainWalletCategoryPerformanceAsync(
        int pairLimit = 500,
        int queueSeedPairLimit = 1_000,
        CancellationToken cancellationToken = default);

    Task<OnChainSignalCandidateQueueRefreshResult> RefreshPolymarketOnChainSignalCandidateQueueAsync(
        int queueSeedLimit = 1_000,
        int retryLimit = 250,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OnChainPaperSignalCandidate>> GetPendingOnChainPaperSignalCandidatesAsync(
        string ratingTimePeriod,
        string ratingOrderBy,
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OnChainPaperSignalCandidate>>([]);
    }

    Task<IReadOnlyList<OnChainPaperSignalCandidate>> GetOnChainPaperSignalCandidatesForCapturesAsync(
        IReadOnlyList<PolymarketOnChainTradeCapture> captures,
        string ratingTimePeriod,
        string ratingOrderBy,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OnChainPaperSignalCandidate>>([]);
    }

    Task AddOnChainPaperSignalResultAsync(
        OnChainPaperSignalResult result,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    async Task AddAcceptedOnChainPaperOrderAsync(
        Signal signal,
        PaperOrder paperOrder,
        PaperCopiedLeaderPosition? copiedLeaderPosition,
        OnChainPaperSignalResult result,
        CancellationToken cancellationToken = default)
    {
        await AddSignalAsync(signal, cancellationToken);
        await AddPaperOrderAsync(paperOrder, cancellationToken);
        if (copiedLeaderPosition is not null)
        {
            await TryAddPaperCopiedLeaderPositionAsync(copiedLeaderPosition, cancellationToken);
        }

        await AddOnChainPaperSignalResultAsync(result, cancellationToken);
    }

    Task<IReadOnlyList<PolymarketOnChainSignalCandidateSource>> GetPolymarketOnChainSignalCandidateSourcesAsync(
        int limit = 250,
        CancellationToken cancellationToken = default);

    Task UpsertPolymarketOnChainSignalCandidateDecisionsAsync(
        IReadOnlyList<PolymarketOnChainSignalCandidateDecision> decisions,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketOnChainSignalCandidate>> GetRecentPolymarketOnChainSignalCandidatesAsync(
        int limit = 250,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketOnChainTradeDetails>> GetRecentPolymarketOnChainTradeDetailsAsync(int limit = 250, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketOnChainParticipantDetails>> GetPolymarketOnChainParticipantDetailsAsync(int limit = 250, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RiskEvent>> GetRecentRiskEventsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddOrderBookSnapshotAsync(OrderBookSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<OrderBookSnapshot?> GetLatestOrderBookSnapshotAsync(string assetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderBookSnapshot>> GetLatestOrderBookSnapshotsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddMarketDataEventAsync(MarketDataEvent marketDataEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketDataEvent>> GetRecentMarketDataEventsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<bool> TryAddPolymarketWebSocketTradeTickAsync(PolymarketWebSocketTradeTick tradeTick, CancellationToken cancellationToken = default);

    Task UpdatePolymarketWebSocketTradeTickMatchAsync(PolymarketWebSocketTradeTick tradeTick, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketWebSocketTradeTick>> GetPendingPolymarketWebSocketTradeTickMatchesAsync(
        DateTimeOffset dueBeforeUtc,
        int maxAttempts,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketWebSocketTradeTick>> GetRecentPolymarketWebSocketTradeTicksAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task UpsertMarketDataStatusAsync(MarketDataStatusSnapshot status, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketDataStatusSnapshot>> GetMarketDataStatusesAsync(CancellationToken cancellationToken = default);

    Task AddPinnedMarketAssetAsync(PinnedMarketAsset asset, CancellationToken cancellationToken = default);

    Task RemovePinnedMarketAssetAsync(string assetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PinnedMarketAsset>> GetPinnedMarketAssetsAsync(CancellationToken cancellationToken = default);

    Task<DailyReport> BuildDailyReportAsync(DateOnly reportDate, CancellationToken cancellationToken = default);

    Task UpsertDailyReportAsync(DailyReport report, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyReport>> GetDailyReportsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TraderPerformanceReport>> GetTraderPerformanceReportsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategoryPerformanceReport>> GetCategoryPerformanceReportsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionQualityReport>> GetExecutionQualityReportsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RejectionAnalysisReport>> GetRejectionAnalysisReportsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddServiceCommandAuditAsync(ServiceCommandAudit audit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceCommandAudit>> GetRecentServiceCommandAuditsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task UpsertScannerStatusAsync(ScannerStatusSnapshot status, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScannerStatusSnapshot>> GetScannerStatusesAsync(CancellationToken cancellationToken = default);

    Task UpsertServiceHeartbeatAsync(ServiceHeartbeat heartbeat, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceHeartbeat>> GetServiceHeartbeatsAsync(CancellationToken cancellationToken = default);
}
