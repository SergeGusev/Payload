namespace PolyCopyTrader.Domain;

public enum BotMode
{
    ReadOnly,
    Paper,
    DryRun,
    Live
}

public enum TradeSide
{
    Unknown,
    Buy,
    Sell
}

public enum PaperOrderStatus
{
    Pending,
    PartiallyFilled,
    Filled,
    Expired,
    Cancelled,
    Rejected
}

public sealed record TraderProfile(
    string Name,
    string Wallet,
    bool Enabled = true);

public sealed record TraderRule(
    string TraderWallet,
    IReadOnlyList<string> AllowedCategories,
    int MaxLagSeconds,
    decimal MaxSlippageCents,
    decimal MaxSpreadCents,
    decimal MaxSpreadPct,
    decimal MinLeaderTradeUsd);

public sealed record LeaderTrade(
    string TraderWallet,
    string TraderName,
    string ConditionId,
    string AssetId,
    string MarketSlug,
    string MarketTitle,
    string Outcome,
    TradeSide Side,
    decimal Price,
    decimal Size,
    decimal CashValueUsd,
    DateTimeOffset TimestampUtc,
    string? TransactionHash = null);

public sealed record LeaderPosition(
    string TraderWallet,
    string ConditionId,
    string AssetId,
    string Outcome,
    decimal Size,
    decimal AvgPrice,
    decimal CurrentValue,
    decimal CashPnl,
    decimal CurPrice,
    DateTimeOffset SnapshotAtUtc);

public sealed record MarketInfo(
    string ConditionId,
    string Slug,
    string Title,
    string? Category,
    DateTimeOffset? EndDateUtc);

public sealed record OrderBookLevel(decimal Price, decimal Size);

public sealed record OrderBookSnapshot(
    string AssetId,
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks,
    DateTimeOffset SnapshotAtUtc)
{
    public decimal? BestBid => Bids.Count == 0 ? null : Bids.Max(level => level.Price);

    public decimal? BestAsk => Asks.Count == 0 ? null : Asks.Min(level => level.Price);

    public decimal? SpreadAbs => BestBid is { } bid && BestAsk is { } ask ? ask - bid : null;

    public decimal? SpreadPct
    {
        get
        {
            if (BestBid is not { } bid || BestAsk is not { } ask)
            {
                return null;
            }

            var mid = (bid + ask) / 2m;
            return mid <= 0m ? null : (ask - bid) / mid * 100m;
        }
    }
}

public sealed record Signal(
    Guid Id,
    LeaderTrade LeaderTrade,
    int Score,
    bool Accepted,
    string DecisionCode,
    IReadOnlyList<string> Reasons,
    decimal? ProposedPaperPrice,
    decimal? ProposedNotionalUsd,
    DateTimeOffset CreatedAtUtc);

public sealed record RiskDecision(
    bool Allowed,
    IReadOnlyList<string> ReasonCodes,
    decimal AllowedNotionalUsd,
    decimal ExposureAfterOrderUsd);

public sealed record PaperOrder(
    Guid Id,
    Guid SignalId,
    PaperOrderStatus Status,
    TradeSide Side,
    string AssetId,
    string ConditionId,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record PaperPosition(
    string AssetId,
    string ConditionId,
    string Outcome,
    decimal SizeShares,
    decimal AveragePrice,
    decimal EstimatedValueUsd,
    decimal UnrealizedPnlUsd);

public sealed record SignalRejection(
    Guid Id,
    Guid SignalId,
    string ReasonCode,
    string ReasonDetails,
    DateTimeOffset CreatedAtUtc);

public sealed record PaperFill(
    Guid Id,
    Guid PaperOrderId,
    decimal Price,
    decimal SizeShares,
    DateTimeOffset FilledAtUtc,
    string Evidence);

public sealed record RiskEvent(
    Guid Id,
    string ReasonCode,
    string Details,
    DateTimeOffset CreatedAtUtc);

public sealed record ApiError(
    Guid Id,
    string Component,
    string Operation,
    string Message,
    DateTimeOffset CreatedAtUtc);

public sealed record ServiceHeartbeat(
    string ServiceName,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastHeartbeatUtc,
    string Version,
    BotMode Mode,
    string CurrentLoop,
    string? LastError);
