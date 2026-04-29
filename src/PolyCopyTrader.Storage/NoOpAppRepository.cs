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

    public Task AddApiErrorAsync(ApiError error, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ApiError>> GetRecentApiErrorsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ApiError>>([]);
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
