using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

internal sealed class TestAppRepository : IAppRepository
{
    private readonly HashSet<string> leaderTradeKeys = [];

    public List<LeaderTrade> LeaderTrades { get; } = [];

    public List<LeaderPosition> LeaderPositions { get; } = [];

    public List<TraderLeaderboardSnapshot> TraderLeaderboardSnapshots { get; } = [];

    public List<TraderDiscoveryCandidate> TraderDiscoveryCandidates { get; } = [];

    public List<Signal> Signals { get; } = [];

    public List<SignalRejection> SignalRejections { get; } = [];

    public List<PaperOrder> PaperOrders { get; } = [];

    public List<PaperFill> PaperFills { get; } = [];

    public List<PaperPosition> PaperPositions { get; } = [];

    public List<DryRunOrder> DryRunOrders { get; } = [];

    public List<LiveOrder> LiveOrders { get; } = [];

    public List<LiveTradingEvent> LiveTradingEvents { get; } = [];

    public List<ApiError> ApiErrors { get; } = [];

    public List<PolymarketHttpLogEntry> PolymarketHttpLogs { get; } = [];

    public List<ScannerStatusSnapshot> ScannerStatuses { get; } = [];

    public bool ThrowOnTryAddLeaderTrade { get; set; }

    public bool ThrowOnAddApiError { get; set; }

    public Task AddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
    {
        return TryAddLeaderTradeAsync(trade, cancellationToken);
    }

    public Task<bool> TryAddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
    {
        if (ThrowOnTryAddLeaderTrade)
        {
            throw new InvalidOperationException("temporary database failure");
        }

        if (!leaderTradeKeys.Add(LeaderTradeDeduplication.BuildKey(trade)))
        {
            return Task.FromResult(false);
        }

        LeaderTrades.Add(trade);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<LeaderTrade>> GetRecentLeaderTradesAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LeaderTrade>>(LeaderTrades.OrderByDescending(item => item.TimestampUtc).Take(limit).ToArray());
    }

    public Task AddLeaderPositionAsync(LeaderPosition position, CancellationToken cancellationToken = default)
    {
        LeaderPositions.Add(position);
        return Task.CompletedTask;
    }

    public Task AddTraderLeaderboardSnapshotsAsync(
        IReadOnlyList<TraderLeaderboardSnapshot> snapshots,
        CancellationToken cancellationToken = default)
    {
        foreach (var snapshot in snapshots)
        {
            TraderLeaderboardSnapshots.RemoveAll(item =>
                string.Equals(item.Category, snapshot.Category, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.TimePeriod, snapshot.TimePeriod, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Wallet, snapshot.Wallet, StringComparison.OrdinalIgnoreCase));
            TraderLeaderboardSnapshots.Add(snapshot);
        }

        return Task.CompletedTask;
    }

    public Task UpsertTraderDiscoveryCandidatesAsync(
        IReadOnlyList<TraderDiscoveryCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        foreach (var candidate in candidates)
        {
            TraderDiscoveryCandidates.RemoveAll(item =>
                string.Equals(item.DiscoveryType, candidate.DiscoveryType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Category, candidate.Category, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.TimePeriod, candidate.TimePeriod, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Wallet, candidate.Wallet, StringComparison.OrdinalIgnoreCase));
            TraderDiscoveryCandidates.Add(candidate);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TraderDiscoveryCandidate>> GetRecentTraderDiscoveryCandidatesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<TraderDiscoveryCandidate>>(
            TraderDiscoveryCandidates
                .OrderByDescending(item => item.SnapshotAtUtc)
                .Take(limit)
                .ToArray());
    }

    public Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        Signals.Add(signal);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SignalSummary>> GetRecentSignalsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SignalSummary>>(Signals
            .OrderByDescending(signal => signal.CreatedAtUtc)
            .Take(limit)
            .Select(signal => new SignalSummary(
                signal.Id,
                signal.LeaderTrade.TraderWallet,
                signal.LeaderTrade.ConditionId,
                signal.LeaderTrade.AssetId,
                signal.LeaderTrade.Outcome,
                signal.LeaderTrade.Price,
                null,
                null,
                null,
                null,
                (int)Math.Max(0, (signal.CreatedAtUtc - signal.LeaderTrade.TimestampUtc).TotalSeconds),
                signal.Score,
                signal.Accepted,
                signal.DecisionCode,
                signal.Reasons,
                signal.ProposedPaperPrice,
                signal.ProposedSizeShares,
                signal.ProposedNotionalUsd,
                signal.CreatedAtUtc))
            .ToArray());
    }

    public Task AddSignalRejectionAsync(SignalRejection rejection, CancellationToken cancellationToken = default)
    {
        SignalRejections.Add(rejection);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SignalRejection>> GetRecentSignalRejectionsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SignalRejection>>(SignalRejections.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToArray());
    }

    public Task AddPaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
    {
        PaperOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task UpdatePaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
    {
        PaperOrders.RemoveAll(item => item.Id == order.Id);
        PaperOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperOrder>>(PaperOrders
            .Where(order => order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled)
            .ToArray());
    }

    public Task<IReadOnlyList<PaperOrder>> GetRecentPaperOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperOrder>>(PaperOrders.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToArray());
    }

    public Task AddPaperFillAsync(PaperFill fill, CancellationToken cancellationToken = default)
    {
        PaperFills.Add(fill);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperFill>> GetRecentPaperFillsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperFill>>(PaperFills.OrderByDescending(item => item.FilledAtUtc).Take(limit).ToArray());
    }

    public Task UpsertPaperPositionAsync(PaperPosition position, CancellationToken cancellationToken = default)
    {
        PaperPositions.RemoveAll(item => string.Equals(item.AssetId, position.AssetId, StringComparison.OrdinalIgnoreCase));
        PaperPositions.Add(position);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperPosition>> GetPaperPositionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperPosition>>(PaperPositions.ToArray());
    }

    public Task AddDryRunOrderAsync(DryRunOrder order, CancellationToken cancellationToken = default)
    {
        DryRunOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DryRunOrder>> GetRecentDryRunOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DryRunOrder>>(DryRunOrders.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToArray());
    }

    public Task AddLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default)
    {
        LiveOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task UpdateLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default)
    {
        LiveOrders.RemoveAll(item => item.Id == order.Id);
        LiveOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LiveOrder>> GetOpenLiveOrdersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveOrder>>(LiveOrders
            .Where(order => order.Status is LiveOrderStatus.Submitted or LiveOrderStatus.Live or LiveOrderStatus.Delayed or LiveOrderStatus.CancelRequested)
            .ToArray());
    }

    public Task<IReadOnlyList<LiveOrder>> GetRecentLiveOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveOrder>>(LiveOrders.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToArray());
    }

    public Task AddLiveTradingEventAsync(LiveTradingEvent liveEvent, CancellationToken cancellationToken = default)
    {
        LiveTradingEvents.Add(liveEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LiveTradingEvent>> GetRecentLiveTradingEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveTradingEvent>>(LiveTradingEvents.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToArray());
    }

    public Task AddApiErrorAsync(ApiError error, CancellationToken cancellationToken = default)
    {
        if (ThrowOnAddApiError)
        {
            throw new InvalidOperationException("temporary database failure while recording error");
        }

        ApiErrors.Add(error);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ApiError>> GetRecentApiErrorsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ApiError>>(ApiErrors.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToArray());
    }

    public Task AddPolymarketHttpLogAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default)
    {
        PolymarketHttpLogs.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketHttpLogEntry>> GetRecentPolymarketHttpLogsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketHttpLogEntry>>(
            PolymarketHttpLogs.OrderByDescending(item => item.RequestedAtUtc).Take(limit).ToArray());
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
        return Task.FromResult(new DailyReport(
            reportDate,
            Signals.Count,
            Signals.Count(signal => signal.Accepted),
            Signals.Count(signal => !signal.Accepted),
            PaperOrders.Count,
            PaperFills.Count,
            PaperOrders.Count(order => order.Status == PaperOrderStatus.Expired),
            PaperPositions.Sum(position => position.UnrealizedPnlUsd),
            PaperOrders.Where(order => order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled).Sum(order => order.NotionalUsd)
                + PaperPositions.Sum(position => position.EstimatedValueUsd),
            string.Empty,
            ApiErrors.Count,
            DateTimeOffset.UtcNow));
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
        ScannerStatuses.Add(status);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScannerStatusSnapshot>> GetScannerStatusesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ScannerStatusSnapshot>>(ScannerStatuses.ToArray());
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
