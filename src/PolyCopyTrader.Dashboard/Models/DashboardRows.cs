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

public sealed record RiskUsageRow(string Name, decimal LimitUsd, decimal UsedUsd, decimal UsedPct, string Status);

public sealed record LogRow(string TimestampUtc, string Severity, string Component, string Message, string Details);

public sealed record DashboardSnapshot(
    IReadOnlyList<OverviewMetric> Overview,
    IReadOnlyList<WatchlistRow> Watchlist,
    IReadOnlyList<LeaderTradeRow> LeaderTrades,
    IReadOnlyList<SignalRow> Signals,
    IReadOnlyList<PaperOrderRow> PaperOrders,
    IReadOnlyList<PaperPositionRow> PaperPositions,
    IReadOnlyList<RiskUsageRow> RiskUsage,
    IReadOnlyList<LogRow> Logs);
