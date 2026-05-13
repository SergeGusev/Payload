using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed class NoOpAppRepository : IAppRepository
{
    public Task AddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<bool> TryAddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<LeaderTrade>> GetRecentLeaderTradesAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LeaderTrade>>([]);
    }

    public Task AddLeaderPositionAsync(LeaderPosition position, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task AddTraderLeaderboardSnapshotsAsync(
        IReadOnlyList<TraderLeaderboardSnapshot> snapshots,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task UpsertTraderDiscoveryCandidatesAsync(
        IReadOnlyList<TraderDiscoveryCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TraderDiscoveryCandidate>> GetRecentTraderDiscoveryCandidatesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<TraderDiscoveryCandidate>>([]);
    }

    public Task<PolymarketDataApiTrader?> GetPolymarketDataApiTraderAsync(
        string wallet,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PolymarketDataApiTrader?>(null);
    }

    public Task UpsertPolymarketDataApiTraderAsync(
        PolymarketDataApiTrader trader,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<int> UpsertPolymarketDataApiTradersAsync(
        IReadOnlyList<PolymarketDataApiTrader> traders,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    public Task<IReadOnlyList<PolymarketDataApiTrader>> GetPolymarketDataApiTradersForSyncAsync(
        int limit,
        DateTimeOffset incrementalSyncBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketDataApiTrader>>([]);
    }

    public Task MarkPolymarketDataApiTraderSyncedAsync(
        string wallet,
        bool fullSync,
        int tradesFetched,
        int tradesInserted,
        DateTimeOffset? latestTradeTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task UpsertPolymarketGammaMarketAsync(
        PolymarketGammaMarket market,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SignalSummary>> GetRecentSignalsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SignalSummary>>([]);
    }

    public Task AddSignalRejectionAsync(SignalRejection rejection, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SignalRejection>> GetRecentSignalRejectionsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SignalRejection>>([]);
    }

    public Task AddPaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task UpdatePaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperOrder>>([]);
    }

    public Task<IReadOnlyList<PaperOrder>> GetRecentPaperOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperOrder>>([]);
    }

    public Task AddPaperFillAsync(PaperFill fill, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperFill>> GetRecentPaperFillsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperFill>>([]);
    }

    public Task UpsertPaperPositionAsync(PaperPosition position, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperPosition>> GetPaperPositionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperPosition>>([]);
    }

    public Task<bool> TryAddPaperPositionSettlementAsync(PaperPositionSettlement settlement, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<PaperPositionSettlement>> GetRecentPaperPositionSettlementsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperPositionSettlement>>([]);
    }

    public Task<int> RefreshPaperCopiedTraderPerformanceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    public Task<IReadOnlyList<PaperCopiedTraderPerformance>> GetPaperCopiedTraderPerformanceAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperCopiedTraderPerformance>>([]);
    }

    public Task<PaperCopiedTraderPerformance?> GetPaperCopiedTraderPerformanceAsync(
        string copiedTraderWallet,
        string category,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PaperCopiedTraderPerformance?>(null);
    }

    public Task<IReadOnlyList<StrategyPerformance>> GetStrategyPerformanceAsync(int limit = 2000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyPerformance>>([]);
    }

    public Task<IReadOnlyList<StrategyRecentPerformance>> GetStrategyRecentPerformanceAsync(int limit = 3000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyRecentPerformance>>([]);
    }

    public Task<IReadOnlyDictionary<Guid, bool>> GetStrategyEnabledStatesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<Guid, bool>>(
            StrategyIds.AllStrategyIds.ToDictionary(strategyId => strategyId, _ => true));
    }

    public Task<IReadOnlyDictionary<Guid, StrategyRuntimeSettings>> GetStrategyRuntimeSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<Guid, StrategyRuntimeSettings>>(
            StrategyIds.AllStrategyIds.ToDictionary(StrategyIds.Normalize, StrategyRuntimeSettings.Default));
    }

    public Task<bool> SetStrategyEnabledAsync(
        Guid strategyId,
        bool enabled,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<bool> SetStrategyLiveStakesAsync(
        Guid strategyId,
        bool liveStakes,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<bool> SetStrategyStakeAmountsAsync(
        Guid strategyId,
        decimal paperStakeAmount,
        decimal liveStakeAmount,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<bool> SetStrategyLiveAvailableBalanceAsync(
        Guid strategyId,
        decimal liveAvailableBalance,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task AddDryRunOrderAsync(DryRunOrder order, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DryRunOrder>> GetRecentDryRunOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DryRunOrder>>([]);
    }

    public Task AddLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task UpdateLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LiveOrder>> GetOpenLiveOrdersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveOrder>>([]);
    }

    public Task<IReadOnlyList<LiveOrder>> GetMatchedLiveOrdersPendingBalanceSettlementAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveOrder>>([]);
    }

    public Task<IReadOnlyList<LiveOrder>> GetRecentLiveOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveOrder>>([]);
    }

    public Task<StrategyLiveBalanceAdjustmentResult> ApplyLiveOrderSettlementToStrategyBalanceAsync(
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

    public Task AddLiveTradingEventAsync(LiveTradingEvent liveEvent, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LiveTradingEvent>> GetRecentLiveTradingEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveTradingEvent>>([]);
    }

    public Task AddApiErrorAsync(ApiError error, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ApiError>> GetRecentApiErrorsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ApiError>>([]);
    }

    public Task AddPolymarketHttpLogAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketHttpLogEntry>> GetRecentPolymarketHttpLogsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketHttpLogEntry>>([]);
    }

    public Task<PolymarketHttpLogCleanupResult> CleanupPolymarketHttpLogsAsync(
        DateTimeOffset successfulBeforeUtc,
        DateTimeOffset failedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PolymarketHttpLogCleanupResult(0, 0, 0));
    }

    public Task AddPolymarketOnChainLogsAsync(IReadOnlyList<PolymarketOnChainLog> logs, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task AddPolymarketOnChainFillsAsync(IReadOnlyList<PolymarketOnChainFill> fills, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<int> AddPolymarketOnChainTradeCapturesAsync(IReadOnlyList<PolymarketOnChainTradeCapture> captures, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(captures.Count);
    }

    public Task UpsertOnChainIngestionCursorAsync(OnChainIngestionCursor cursor, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<OnChainIngestionCursor?> GetOnChainIngestionCursorAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<OnChainIngestionCursor?>(null);
    }

    public Task UpsertOnChainTradeCaptureCursorAsync(OnChainTradeCaptureCursor cursor, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<OnChainTradeCaptureCursor?> GetOnChainTradeCaptureCursorAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<OnChainTradeCaptureCursor?>(null);
    }

    public Task<long?> GetLatestPolymarketOnChainFillBlockAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<long?>(null);
    }

    public Task<OnChainBlockRange?> GetPolymarketOnChainFillBlockRangeAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<OnChainBlockRange?>(null);
    }

    public Task<OnChainBlockRange?> GetPolymarketOnChainWalletExecutionBlockRangeAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<OnChainBlockRange?>(null);
    }

    public Task<OnChainBlockRange?> GetPolymarketOnChainTradeDetailsBlockRangeAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<OnChainBlockRange?>(null);
    }

    public Task RefreshPolymarketOnChainWalletDerivedDataAsync(string contractAddress, long fromBlock, long toBlock, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketOnChainWalletExecution>> GetRecentPolymarketOnChainWalletExecutionsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainWalletExecution>>([]);
    }

    public Task<IReadOnlyList<string>> GetOnChainTokenIdsMissingMetadataAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<PolymarketOnChainTokenMetadata?> GetPolymarketOnChainTokenMetadataAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PolymarketOnChainTokenMetadata?>(null);
    }

    public Task UpsertPolymarketOnChainTokenMetadataAsync(IReadOnlyList<PolymarketOnChainTokenMetadata> metadata, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketOnChainFill>> GetRecentPolymarketOnChainFillsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainFill>>([]);
    }

    public Task<IReadOnlyList<TraderOnChainStats>> GetTraderOnChainStatsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<TraderOnChainStats>>([]);
    }

    public Task<OnChainActivityRefreshResult> RefreshPolymarketOnChainWalletActivityAsync(
        int walletLimit = 100,
        int queueSeedWalletLimit = 500,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OnChainActivityRefreshResult(0, 0, 0, 0));
    }

    public Task<IReadOnlyList<PolymarketOnChainWalletPosition>> GetPolymarketOnChainWalletPositionsAsync(int limit = 250, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainWalletPosition>>([]);
    }

    public Task<OnChainPositionRefreshResult> RefreshPolymarketOnChainWalletPositionsAsync(
        int tokenLimit = 50,
        int queueSeedTokenLimit = 500,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OnChainPositionRefreshResult(0, 0, 0, 0));
    }

    public Task<IReadOnlyList<PolymarketOnChainWalletPerformance>> GetPolymarketOnChainWalletPerformanceAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainWalletPerformance>>([]);
    }

    public Task<OnChainPerformanceRefreshResult> RefreshPolymarketOnChainWalletPerformanceAsync(
        int walletLimit = 100,
        int queueSeedWalletLimit = 500,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OnChainPerformanceRefreshResult(0, 0, 0, 0));
    }

    public Task<IReadOnlyList<PolymarketOnChainWalletCategoryPerformance>> GetPolymarketOnChainWalletCategoryPerformanceAsync(
        string? category = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainWalletCategoryPerformance>>([]);
    }

    public Task<PolymarketOnChainWalletCategoryPerformance?> GetPolymarketOnChainWalletCategoryPerformanceAsync(
        string wallet,
        string category,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PolymarketOnChainWalletCategoryPerformance?>(null);
    }

    public Task<OnChainCategoryPerformanceRefreshResult> RefreshPolymarketOnChainWalletCategoryPerformanceAsync(
        int pairLimit = 500,
        int queueSeedPairLimit = 1_000,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OnChainCategoryPerformanceRefreshResult(0, 0, 0, 0));
    }

    public Task<OnChainSignalCandidateQueueRefreshResult> RefreshPolymarketOnChainSignalCandidateQueueAsync(
        int queueSeedLimit = 1_000,
        int retryLimit = 250,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OnChainSignalCandidateQueueRefreshResult(0, 0, 0));
    }

    public Task<IReadOnlyList<PolymarketOnChainSignalCandidateSource>> GetPolymarketOnChainSignalCandidateSourcesAsync(
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainSignalCandidateSource>>([]);
    }

    public Task UpsertPolymarketOnChainSignalCandidateDecisionsAsync(
        IReadOnlyList<PolymarketOnChainSignalCandidateDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketOnChainSignalCandidate>> GetRecentPolymarketOnChainSignalCandidatesAsync(
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainSignalCandidate>>([]);
    }

    public Task<IReadOnlyList<PolymarketOnChainTradeDetails>> GetRecentPolymarketOnChainTradeDetailsAsync(int limit = 250, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainTradeDetails>>([]);
    }

    public Task<IReadOnlyList<PolymarketOnChainParticipantDetails>> GetPolymarketOnChainParticipantDetailsAsync(int limit = 250, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainParticipantDetails>>([]);
    }

    public Task<IReadOnlyList<RiskEvent>> GetRecentRiskEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<RiskEvent>>([]);
    }

    public Task AddOrderBookSnapshotAsync(OrderBookSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<OrderBookSnapshot?> GetLatestOrderBookSnapshotAsync(string assetId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<OrderBookSnapshot?>(null);
    }

    public Task<IReadOnlyList<OrderBookSnapshot>> GetLatestOrderBookSnapshotsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OrderBookSnapshot>>([]);
    }

    public Task AddMarketDataEventAsync(MarketDataEvent marketDataEvent, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MarketDataEvent>> GetRecentMarketDataEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MarketDataEvent>>([]);
    }

    public Task<bool> TryAddPolymarketWebSocketTradeTickAsync(PolymarketWebSocketTradeTick tradeTick, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task UpdatePolymarketWebSocketTradeTickMatchAsync(PolymarketWebSocketTradeTick tradeTick, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketWebSocketTradeTick>> GetPendingPolymarketWebSocketTradeTickMatchesAsync(
        DateTimeOffset dueBeforeUtc,
        int maxAttempts,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketWebSocketTradeTick>>([]);
    }

    public Task<IReadOnlyList<PolymarketWebSocketTradeTick>> GetRecentPolymarketWebSocketTradeTicksAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketWebSocketTradeTick>>([]);
    }

    public Task UpsertMarketDataStatusAsync(MarketDataStatusSnapshot status, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MarketDataStatusSnapshot>> GetMarketDataStatusesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MarketDataStatusSnapshot>>([]);
    }

    public Task AddPinnedMarketAssetAsync(PinnedMarketAsset asset, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task RemovePinnedMarketAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PinnedMarketAsset>> GetPinnedMarketAssetsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PinnedMarketAsset>>([]);
    }

    public Task<DailyReport> BuildDailyReportAsync(DateOnly reportDate, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new DailyReport(reportDate, 0, 0, 0, 0, 0, 0, 0m, 0m, string.Empty, 0, DateTimeOffset.UtcNow));
    }

    public Task UpsertDailyReportAsync(DailyReport report, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DailyReport>> GetDailyReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DailyReport>>([]);
    }

    public Task<IReadOnlyList<TraderPerformanceReport>> GetTraderPerformanceReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<TraderPerformanceReport>>([]);
    }

    public Task<IReadOnlyList<CategoryPerformanceReport>> GetCategoryPerformanceReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<CategoryPerformanceReport>>([]);
    }

    public Task<IReadOnlyList<ExecutionQualityReport>> GetExecutionQualityReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ExecutionQualityReport>>([]);
    }

    public Task<IReadOnlyList<RejectionAnalysisReport>> GetRejectionAnalysisReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<RejectionAnalysisReport>>([]);
    }

    public Task AddServiceCommandAuditAsync(ServiceCommandAudit audit, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServiceCommandAudit>> GetRecentServiceCommandAuditsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ServiceCommandAudit>>([]);
    }

    public Task UpsertScannerStatusAsync(ScannerStatusSnapshot status, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScannerStatusSnapshot>> GetScannerStatusesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ScannerStatusSnapshot>>([]);
    }

    public Task UpsertServiceHeartbeatAsync(ServiceHeartbeat heartbeat, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServiceHeartbeat>> GetServiceHeartbeatsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ServiceHeartbeat>>([]);
    }
}
