using CommunityToolkit.Mvvm.ComponentModel;

namespace PolyCopyTrader.Dashboard.Models;

public sealed record OverviewMetric(string Name, string Value);

public sealed record WatchlistRow(
    string TraderName,
    string Wallet,
    bool Enabled,
    string AllowedCategories,
    string LastSuccessfulScanUtc,
    string LastSeenTradeUtc,
    int TradesFetched,
    int NewTradesStored,
    string Status,
    string LastError);

public sealed record TraderDiscoveryRow(
    string SnapshotUtc,
    string Type,
    string Category,
    string TimePeriod,
    string Rank,
    string UserName,
    string Wallet,
    decimal Pnl,
    decimal Volume,
    decimal? AllTimePnl,
    decimal? AllTimeVolume,
    bool Verified,
    int TradesFetched,
    int BuyTrades,
    int SellTrades,
    decimal RecentTradeVolumeUsd,
    decimal AverageTradeUsd,
    string LastTradeUtc,
    int PositionsFetched,
    decimal OpenPositionValueUsd,
    decimal OpenPositionCashPnlUsd,
    string Notes);

public sealed record OnChainTraderRow(
    string Wallet,
    int Fills,
    int BuyFills,
    int SellFills,
    int MarketsTraded,
    decimal VolumeUsd,
    decimal AverageTradeUsd,
    decimal FeesUsd,
    decimal ActivityScore,
    string FirstTradeUtc,
    string LastTradeUtc);

public sealed record OnChainLeaderRow(
    string Wallet,
    decimal Score,
    string SampleQuality,
    decimal ResolvedPnlUsd,
    decimal ResolvedRoiPct,
    decimal WinRatePct,
    int ResolvedPositions,
    int OpenPositions,
    int MarketsTraded,
    decimal VolumeUsd,
    decimal OpenExposureUsd,
    decimal AveragePositionSizeUsd,
    string LastActiveUtc);

public sealed record OnChainFillRow(
    string TimestampUtc,
    string Wallet,
    string Side,
    string TokenId,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    string Contract,
    string Version,
    string TransactionHash);

public sealed record OnChainPositionRow(
    string Wallet,
    string Market,
    string Outcome,
    string Category,
    string Status,
    decimal NetShares,
    decimal NetCostUsd,
    decimal BuyShares,
    decimal SellShares,
    decimal AverageBuyPrice,
    decimal AverageSellPrice,
    decimal VolumeUsd,
    string ResolvedPnlUsd,
    string LastTradeUtc,
    string TokenId);

public sealed record OnChainTradeDetailRow(
    string TimestampUtc,
    string Market,
    string Outcome,
    string Category,
    string Maker,
    string Taker,
    string MakerSide,
    string TakerSide,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    decimal MakerAmount,
    decimal TakerAmount,
    decimal FeeAmount,
    string Status,
    string TokenId,
    string TransactionHash);

public sealed record OnChainParticipantDetailRow(
    string Wallet,
    int Executions,
    int BuyExecutions,
    int SellExecutions,
    int MarketsTraded,
    int PositionsCount,
    int OpenPositions,
    int ResolvedPositions,
    decimal VolumeUsd,
    decimal AverageTradeUsd,
    decimal FeesUsd,
    decimal OpenExposureUsd,
    decimal ResolvedPnlUsd,
    decimal ResolvedRoiPct,
    decimal WinRatePct,
    decimal Score,
    string SampleQuality,
    string FirstTradeUtc,
    string LastTradeUtc);

public sealed record LeaderTradeRow(
    string TimestampUtc,
    string Trader,
    string Market,
    string Outcome,
    string Side,
    decimal LeaderPrice,
    decimal Size,
    decimal CashValueUsd,
    string Category,
    string TransactionHash);

public sealed record SignalRow(
    string TimestampUtc,
    string Trader,
    string Market,
    string Outcome,
    int Score,
    bool Accepted,
    string DecisionCode,
    string ReasonCodes,
    decimal LeaderPrice,
    decimal? BestBid,
    decimal? BestAsk,
    decimal? SpreadAbs,
    decimal? SpreadPct,
    int? LagSeconds,
    decimal? ProposedPaperPrice,
    decimal? ProposedSizeShares,
    decimal? ProposedNotionalUsd);

public sealed record PaperOrderRow(
    string Status,
    string Side,
    string CopiedTraderWallet,
    string Market,
    string Outcome,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    string CreatedUtc,
    string ExpiresUtc,
    string FilledUtc,
    string TtlRemaining,
    string SignalId);

public sealed record PaperPositionRow(
    string Market,
    string Outcome,
    decimal SizeShares,
    decimal AveragePrice,
    string CurrentBid,
    string CurrentAsk,
    decimal EstimatedValueUsd,
    decimal UnrealizedPnlUsd,
    string RealizedPnlUsd,
    string SourceTrader);

public sealed record PaperCopiedTraderPerformanceRow(
    string Wallet,
    string Category,
    decimal Score,
    decimal TotalPnlUsd,
    decimal RoiPct,
    decimal WinRatePct,
    int OrdersCount,
    int FilledOrdersCount,
    int OpenPositionsCount,
    int SettledPositionsCount,
    int WonPositionsCount,
    int LostPositionsCount,
    decimal BuyCostUsd,
    decimal RealizedPnlUsd,
    decimal UnrealizedPnlUsd,
    string LastOrderUtc,
    string RefreshedUtc);

public sealed partial class StrategyPerformanceRow : ObservableObject
{
    public StrategyPerformanceRow(
        Guid strategyId,
        string name,
        bool enabled,
        bool liveStakes,
        decimal paperStakeAmount,
        decimal liveStakeAmount,
        decimal liveAvailableBalance,
        int ordersCount,
        int filledOrdersCount,
        int openOrdersCount,
        int openPositionsCount,
        int observedRunsCount,
        int enteredRunsCount,
        int skippedRunsCount,
        int settledRunsCount,
        int settledPositionsCount,
        int wonPositionsCount,
        int lostPositionsCount,
        decimal stakeUsd,
        decimal realizedPnlUsd,
        decimal unrealizedPnlUsd,
        decimal totalPnlUsd,
        decimal winRatePct,
        decimal lossRatePct,
        decimal avgWinPnlUsd,
        decimal avgLossPnlUsd,
        decimal? profitFactor,
        decimal expectancyPnlUsd,
        decimal roiPct,
        decimal closedRoiPct,
        decimal avgEntryDelaySeconds,
        decimal maxEntryDelaySeconds,
        int liveOrdersCount,
        int liveFilledOrdersCount,
        int liveOpenOrdersCount,
        int liveSettledOrdersCount,
        int liveSkippedOrdersCount,
        int liveWonOrdersCount,
        int liveLostOrdersCount,
        decimal liveStakeUsd,
        decimal liveRealizedPnlUsd,
        decimal liveWinRatePct,
        decimal liveLossRatePct,
        decimal liveAvgWinPnlUsd,
        decimal liveAvgLossPnlUsd,
        decimal? liveProfitFactor,
        decimal liveExpectancyPnlUsd,
        decimal liveRoiPct,
        string liveLastOrderUtc,
        string liveLastSettlementUtc,
        string lastOrderUtc,
        string lastRunUtc)
    {
        StrategyId = strategyId;
        Name = name;
        this.enabled = enabled;
        this.liveStakes = liveStakes;
        this.paperStakeAmount = paperStakeAmount;
        this.liveStakeAmount = liveStakeAmount;
        this.liveAvailableBalance = liveAvailableBalance;
        OrdersCount = ordersCount;
        FilledOrdersCount = filledOrdersCount;
        OpenOrdersCount = openOrdersCount;
        OpenPositionsCount = openPositionsCount;
        ObservedRunsCount = observedRunsCount;
        EnteredRunsCount = enteredRunsCount;
        SkippedRunsCount = skippedRunsCount;
        SettledRunsCount = settledRunsCount;
        SettledPositionsCount = settledPositionsCount;
        WonPositionsCount = wonPositionsCount;
        LostPositionsCount = lostPositionsCount;
        StakeUsd = stakeUsd;
        RealizedPnlUsd = realizedPnlUsd;
        UnrealizedPnlUsd = unrealizedPnlUsd;
        TotalPnlUsd = totalPnlUsd;
        WinRatePct = winRatePct;
        LossRatePct = lossRatePct;
        AvgWinPnlUsd = avgWinPnlUsd;
        AvgLossPnlUsd = avgLossPnlUsd;
        ProfitFactor = profitFactor;
        ExpectancyPnlUsd = expectancyPnlUsd;
        RoiPct = roiPct;
        ClosedRoiPct = closedRoiPct;
        AvgEntryDelaySeconds = avgEntryDelaySeconds;
        MaxEntryDelaySeconds = maxEntryDelaySeconds;
        LiveOrdersCount = liveOrdersCount;
        LiveFilledOrdersCount = liveFilledOrdersCount;
        LiveOpenOrdersCount = liveOpenOrdersCount;
        LiveSettledOrdersCount = liveSettledOrdersCount;
        LiveSkippedOrdersCount = liveSkippedOrdersCount;
        LiveWonOrdersCount = liveWonOrdersCount;
        LiveLostOrdersCount = liveLostOrdersCount;
        LiveStakeUsd = liveStakeUsd;
        LiveRealizedPnlUsd = liveRealizedPnlUsd;
        LiveWinRatePct = liveWinRatePct;
        LiveLossRatePct = liveLossRatePct;
        LiveAvgWinPnlUsd = liveAvgWinPnlUsd;
        LiveAvgLossPnlUsd = liveAvgLossPnlUsd;
        LiveProfitFactor = liveProfitFactor;
        LiveExpectancyPnlUsd = liveExpectancyPnlUsd;
        LiveRoiPct = liveRoiPct;
        LiveLastOrderUtc = liveLastOrderUtc;
        LiveLastSettlementUtc = liveLastSettlementUtc;
        LastOrderUtc = lastOrderUtc;
        LastRunUtc = lastRunUtc;
    }

    public Guid StrategyId { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool enabled;

    [ObservableProperty]
    private bool liveStakes;

    [ObservableProperty]
    private decimal paperStakeAmount;

    [ObservableProperty]
    private decimal liveStakeAmount;

    [ObservableProperty]
    private decimal liveAvailableBalance;

    public int OrdersCount { get; }

    public int FilledOrdersCount { get; }

    public int OpenOrdersCount { get; }

    public int OpenPositionsCount { get; }

    public int ObservedRunsCount { get; }

    public int EnteredRunsCount { get; }

    public int SkippedRunsCount { get; }

    public int SettledRunsCount { get; }

    public int SettledPositionsCount { get; }

    public int WonPositionsCount { get; }

    public int LostPositionsCount { get; }

    public decimal StakeUsd { get; }

    public decimal RealizedPnlUsd { get; }

    public decimal UnrealizedPnlUsd { get; }

    public decimal TotalPnlUsd { get; }

    public decimal WinRatePct { get; }

    public decimal LossRatePct { get; }

    public decimal AvgWinPnlUsd { get; }

    public decimal AvgLossPnlUsd { get; }

    public decimal? ProfitFactor { get; }

    public decimal ExpectancyPnlUsd { get; }

    public decimal RoiPct { get; }

    public decimal ClosedRoiPct { get; }

    public decimal AvgEntryDelaySeconds { get; }

    public decimal MaxEntryDelaySeconds { get; }

    public int LiveOrdersCount { get; }

    public int LiveFilledOrdersCount { get; }

    public int LiveOpenOrdersCount { get; }

    public int LiveSettledOrdersCount { get; }

    public int LiveSkippedOrdersCount { get; }

    public int LiveWonOrdersCount { get; }

    public int LiveLostOrdersCount { get; }

    public decimal LiveStakeUsd { get; }

    public decimal LiveRealizedPnlUsd { get; }

    public decimal LiveWinRatePct { get; }

    public decimal LiveLossRatePct { get; }

    public decimal LiveAvgWinPnlUsd { get; }

    public decimal LiveAvgLossPnlUsd { get; }

    public decimal? LiveProfitFactor { get; }

    public decimal LiveExpectancyPnlUsd { get; }

    public decimal LiveRoiPct { get; }

    public string LiveLastOrderUtc { get; }

    public string LiveLastSettlementUtc { get; }

    public string LastOrderUtc { get; }

    public string LastRunUtc { get; }
}

public sealed record StrategyRecentPerformanceRow(
    string Window,
    int WindowHours,
    string Name,
    int OrdersCount,
    int FilledOrdersCount,
    int ExpiredOrdersCount,
    int OpenOrdersCount,
    int EnteredRunsCount,
    int SkippedRunsCount,
    int SettledRunsCount,
    int WonRunsCount,
    int LostRunsCount,
    decimal WinRatePct,
    decimal RoiPct,
    int LiveSettledOrdersCount,
    int LiveSkippedOrdersCount,
    int LiveWonOrdersCount,
    int LiveLostOrdersCount,
    decimal LiveRealizedPnlUsd,
    decimal LiveRoiPct,
    decimal RealizedPnlUsd,
    decimal FilledCostUsd,
    decimal AvgFillPrice,
    decimal AvgEntryDelaySeconds,
    decimal MaxEntryDelaySeconds,
    string TopSkipReason,
    string LastOrderUtc,
    string LastRunUtc);

public sealed record DryRunOrderRow(
    string TimestampUtc,
    string Status,
    string Side,
    string Asset,
    string Outcome,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    string OrderType,
    string ValidationSummary,
    string SignalId);

public sealed record LiveOrderRow(
    string CreatedUtc,
    string Status,
    string OrderId,
    string Side,
    string Asset,
    string Outcome,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    string OrderType,
    string ExpiresUtc,
    string ResponseStatus,
    decimal FilledSize,
    decimal RemainingSize,
    decimal? AverageFillPrice,
    decimal FilledNotionalUsd,
    decimal CostBasisUsd,
    decimal FeeUsd,
    decimal? SettlementValueUsd,
    decimal? RealizedPnlUsd,
    string SettledUtc,
    string WinningOutcome,
    bool? Won,
    string SettlementSource,
    string CancelStatus,
    string ValidationSummary,
    string SignalId);

public sealed record LiveTradingEventRow(
    string TimestampUtc,
    string Action,
    string Status,
    string Details);

public sealed record LiveReadinessRow(
    string Gate,
    string Value,
    string Status,
    string Details);

public sealed record MarketDataRow(
    string AssetId,
    string ConditionId,
    string BestBid,
    string BestAsk,
    string Spread,
    string SnapshotUtc);

public sealed record DailyReportRow(
    string Date,
    int SignalsObserved,
    int SignalsAccepted,
    int SignalsRejected,
    int PaperOrdersCreated,
    int PaperFills,
    int PaperExpiredOrders,
    decimal PaperPnl,
    decimal OpenPaperExposure,
    string TopRejectionReasons,
    int ApiErrors,
    string GeneratedUtc);

public sealed record TraderPerformanceRow(
    string Trader,
    int Signals,
    decimal AcceptanceRatePct,
    decimal FillRatePct,
    string AverageLagSeconds,
    string AverageLeaderPrice,
    string AverageProposedPrice,
    string AveragePriceDifference,
    decimal PaperPnl,
    string PaperPnlByCategory,
    string RejectionReasons);

public sealed record CategoryPerformanceRow(
    string Category,
    int Signals,
    int Accepted,
    int Filled,
    decimal PaperPnl,
    string AverageSpread,
    string AverageLagSeconds);

public sealed record ExecutionQualityRow(
    string CreatedUtc,
    string Trader,
    string Asset,
    decimal LeaderPrice,
    string ProposedPrice,
    string PaperFillPrice,
    string ProposedMinusLeader,
    string FillMinusProposed,
    string LagSeconds,
    string SpreadAtSignal,
    string MidAfter1m,
    string MidAfter5m,
    string MidAfter30m);

public sealed record RejectionAnalysisRow(
    string ReasonCode,
    int Count,
    decimal RejectedPct,
    string LastRejectedUtc);

public sealed record RiskUsageRow(string Name, decimal LimitUsd, decimal UsedUsd, decimal UsedPct, string Status);

public sealed record LogRow(string TimestampUtc, string Severity, string Component, string Message, string Details);

public sealed record DashboardErrorRow(string TimestampUtc, string Source, string Message, string Details);

public sealed record DiagnosticRow(string Name, string Value, string Status);

public sealed record RunbookLinkRow(string Document, string Path, string Purpose);

public sealed record ServiceAvailability(
    string ServiceName,
    string Status,
    string Mode,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastHeartbeatUtc,
    TimeSpan? HeartbeatAge,
    string CurrentLoop,
    string? LastError,
    bool HasHeartbeat,
    bool IsFresh)
{
    public bool IsAvailable =>
        HasHeartbeat &&
        IsFresh &&
        !string.Equals(Status, "Stopped", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "Stopping", StringComparison.OrdinalIgnoreCase);
}

public sealed record DashboardSnapshot(
    ServiceAvailability ServiceAvailability,
    IReadOnlyList<OverviewMetric> Overview,
    IReadOnlyList<WatchlistRow> Watchlist,
    IReadOnlyList<TraderDiscoveryRow> TraderDiscovery,
    IReadOnlyList<OnChainLeaderRow> OnChainLeaders,
    IReadOnlyList<OnChainTraderRow> OnChainTraders,
    IReadOnlyList<OnChainPositionRow> OnChainPositions,
    IReadOnlyList<OnChainFillRow> OnChainFills,
    IReadOnlyList<OnChainTradeDetailRow> OnChainTradeDetails,
    IReadOnlyList<OnChainParticipantDetailRow> OnChainParticipantDetails,
    IReadOnlyList<LeaderTradeRow> LeaderTrades,
    IReadOnlyList<SignalRow> Signals,
    IReadOnlyList<PaperOrderRow> PaperOrders,
    IReadOnlyList<PaperPositionRow> PaperPositions,
    IReadOnlyList<StrategyPerformanceRow> Strategies,
    IReadOnlyList<StrategyRecentPerformanceRow> StrategyRecentPerformance,
    IReadOnlyList<PaperCopiedTraderPerformanceRow> PaperCopiedTraderPerformance,
    IReadOnlyList<DryRunOrderRow> DryRunOrders,
    IReadOnlyList<LiveOrderRow> LiveOrders,
    IReadOnlyList<LiveTradingEventRow> LiveTradingEvents,
    IReadOnlyList<LiveReadinessRow> LiveReadiness,
    IReadOnlyList<MarketDataRow> MarketData,
    IReadOnlyList<DailyReportRow> DailyReports,
    IReadOnlyList<TraderPerformanceRow> TraderPerformance,
    IReadOnlyList<CategoryPerformanceRow> CategoryPerformance,
    IReadOnlyList<ExecutionQualityRow> ExecutionQuality,
    IReadOnlyList<RejectionAnalysisRow> RejectionAnalysis,
    IReadOnlyList<RiskUsageRow> RiskUsage,
    IReadOnlyList<DiagnosticRow> Diagnostics,
    IReadOnlyList<RunbookLinkRow> RunbookLinks,
    IReadOnlyList<LogRow> Logs);
