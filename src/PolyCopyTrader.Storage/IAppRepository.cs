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

    Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SignalSummary>> GetRecentSignalsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddSignalRejectionAsync(SignalRejection rejection, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SignalRejection>> GetRecentSignalRejectionsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddPaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default);

    Task UpdatePaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperOrder>> GetRecentPaperOrdersAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddPaperFillAsync(PaperFill fill, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperFill>> GetRecentPaperFillsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task UpsertPaperPositionAsync(PaperPosition position, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperPosition>> GetPaperPositionsAsync(CancellationToken cancellationToken = default);

    Task AddDryRunOrderAsync(DryRunOrder order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DryRunOrder>> GetRecentDryRunOrdersAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default);

    Task UpdateLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LiveOrder>> GetOpenLiveOrdersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LiveOrder>> GetRecentLiveOrdersAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddLiveTradingEventAsync(LiveTradingEvent liveEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LiveTradingEvent>> GetRecentLiveTradingEventsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddApiErrorAsync(ApiError error, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApiError>> GetRecentApiErrorsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddPolymarketHttpLogAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketHttpLogEntry>> GetRecentPolymarketHttpLogsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task AddPolymarketOnChainLogsAsync(IReadOnlyList<PolymarketOnChainLog> logs, CancellationToken cancellationToken = default);

    Task AddPolymarketOnChainFillsAsync(IReadOnlyList<PolymarketOnChainFill> fills, CancellationToken cancellationToken = default);

    Task UpsertOnChainIngestionCursorAsync(OnChainIngestionCursor cursor, CancellationToken cancellationToken = default);

    Task<OnChainIngestionCursor?> GetOnChainIngestionCursorAsync(string contractAddress, CancellationToken cancellationToken = default);

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
