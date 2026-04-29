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
    string CancelStatus,
    string ValidationSummary,
    string SignalId);

public sealed record LiveTradingEventRow(
    string TimestampUtc,
    string Action,
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

public sealed record DiagnosticRow(string Name, string Value, string Status);

public sealed record DashboardSnapshot(
    IReadOnlyList<OverviewMetric> Overview,
    IReadOnlyList<WatchlistRow> Watchlist,
    IReadOnlyList<LeaderTradeRow> LeaderTrades,
    IReadOnlyList<SignalRow> Signals,
    IReadOnlyList<PaperOrderRow> PaperOrders,
    IReadOnlyList<PaperPositionRow> PaperPositions,
    IReadOnlyList<DryRunOrderRow> DryRunOrders,
    IReadOnlyList<LiveOrderRow> LiveOrders,
    IReadOnlyList<LiveTradingEventRow> LiveTradingEvents,
    IReadOnlyList<MarketDataRow> MarketData,
    IReadOnlyList<DailyReportRow> DailyReports,
    IReadOnlyList<TraderPerformanceRow> TraderPerformance,
    IReadOnlyList<CategoryPerformanceRow> CategoryPerformance,
    IReadOnlyList<ExecutionQualityRow> ExecutionQuality,
    IReadOnlyList<RejectionAnalysisRow> RejectionAnalysis,
    IReadOnlyList<RiskUsageRow> RiskUsage,
    IReadOnlyList<DiagnosticRow> Diagnostics,
    IReadOnlyList<LogRow> Logs);
