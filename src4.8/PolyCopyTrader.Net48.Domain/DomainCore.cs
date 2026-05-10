namespace PolyCopyTrader.Domain;

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
    PartiallyFilledExpired,
    Filled,
    Expired,
    Cancelled,
    Rejected
}

public sealed record TraderRule(
    string TraderWallet,
    IReadOnlyList<string> AllowedCategories,
    int MaxLagSeconds,
    decimal MaxSlippageCents,
    decimal MaxSpreadCents,
    decimal MaxSpreadPct,
    decimal MinLeaderTradeUsd,
    bool Enabled = true);

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
    DateTimeOffset SnapshotAtUtc,
    string? ConditionId = null,
    decimal? MinOrderSize = null,
    decimal? TickSize = null,
    bool NegativeRisk = false,
    decimal? LastTradePrice = null)
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

    public bool IsCrossed => BestBid is { } bid && BestAsk is { } ask && bid >= ask;

    public bool HasEnoughDepth => Bids.Any(level => level.Size > 0m) && Asks.Any(level => level.Size > 0m);
}

public sealed record SignalEvaluationContext(
    LeaderTrade LeaderTrade,
    TraderRule TraderRule,
    MarketInfo? MarketInfo,
    OrderBookSnapshot? OrderBookSnapshot,
    ExposureSnapshot Exposure,
    PolymarketOnChainWalletCategoryPerformance? LeaderCategoryPerformance = null,
    PaperCopiedTraderPerformance? CopiedTraderOverallPerformance = null,
    PaperCopiedTraderPerformance? CopiedTraderCategoryPerformance = null,
    decimal? AvailablePositionSizeShares = null);

public sealed record SignalDecision(
    bool Accepted,
    int Score,
    string DecisionCode,
    IReadOnlyList<string> Reasons,
    decimal? ProposedPrice,
    decimal? ProposedSizeShares,
    decimal? ProposedNotionalUsd,
    DateTimeOffset CreatedAtUtc);

public sealed record ProposedOrderIntent(
    string TraderWallet,
    string ConditionId,
    string AssetId,
    string? Category,
    TradeSide Side,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd);

public sealed record ExposureSnapshot(
    decimal MarketExposureUsd,
    decimal TraderExposureUsd,
    decimal CategoryExposureUsd,
    decimal TotalDeployedUsd,
    decimal DailyLossUsd,
    int OpenOrdersCount,
    int OldestOpenOrderAgeSeconds = 0);

public sealed record Signal(
    Guid Id,
    LeaderTrade LeaderTrade,
    int Score,
    bool Accepted,
    string DecisionCode,
    IReadOnlyList<string> Reasons,
    decimal? ProposedPaperPrice,
    decimal? ProposedSizeShares,
    decimal? ProposedNotionalUsd,
    DateTimeOffset CreatedAtUtc);

public sealed record RiskDecision(
    bool Allowed,
    IReadOnlyList<string> ReasonCodes,
    decimal AllowedNotionalUsd,
    decimal ExposureAfterOrderUsd,
    decimal AllowedSizeShares = 0m);

public sealed record PaperOrder(
    Guid Id,
    Guid SignalId,
    string CopiedTraderWallet,
    PaperOrderStatus Status,
    TradeSide Side,
    string AssetId,
    string ConditionId,
    string Outcome,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? FilledAtUtc = null,
    DateTimeOffset? CancelledAtUtc = null,
    Guid StrategyId = default,
    string? RawDecisionJson = null,
    Guid? CorrelationId = null,
    string ExecutionSource = "");

public sealed record PaperPosition(
    string AssetId,
    string ConditionId,
    string Outcome,
    decimal SizeShares,
    decimal AveragePrice,
    decimal EstimatedValueUsd,
    decimal UnrealizedPnlUsd,
    DateTimeOffset UpdatedAtUtc,
    string CopiedTraderWallet = "");

public sealed record PaperFill(
    Guid Id,
    Guid PaperOrderId,
    decimal Price,
    decimal SizeShares,
    DateTimeOffset FilledAtUtc,
    string Evidence);

public sealed record PaperCopiedTraderPerformance(
    string CopiedTraderWallet,
    string Category,
    int OrdersCount,
    int FilledOrdersCount,
    int BuyFillsCount,
    int SellFillsCount,
    int OpenPositionsCount,
    int SettledPositionsCount,
    int WonPositionsCount,
    int LostPositionsCount,
    decimal BuyCostUsd,
    decimal SellProceedsUsd,
    decimal SettlementValueUsd,
    decimal RealizedPnlUsd,
    decimal UnrealizedPnlUsd,
    decimal TotalPnlUsd,
    decimal RoiPct,
    decimal WinRatePct,
    decimal Score,
    DateTimeOffset? FirstOrderUtc,
    DateTimeOffset? LastOrderUtc,
    DateTimeOffset RefreshedAtUtc);

public sealed record PolymarketOnChainWalletCategoryPerformance(
    string Wallet,
    string Category,
    int PositionsCount,
    int OpenPositions,
    int FlatPositions,
    int ResolvedPositions,
    int ProfitableResolvedPositions,
    int LosingResolvedPositions,
    int MarketsTraded,
    decimal VolumeUsd,
    decimal ResolvedVolumeUsd,
    decimal OpenExposureUsd,
    decimal ResolvedCostUsd,
    decimal ResolvedPnlUsd,
    decimal ResolvedRoiPct,
    decimal WinRatePct,
    decimal AveragePositionSizeUsd,
    decimal Score,
    string SampleQuality,
    DateTimeOffset FirstActiveUtc,
    DateTimeOffset LastActiveUtc,
    DateTimeOffset RefreshedAtUtc);

public static class StrategyIds
{
    public const string FollowLeaderIdValue = "f0110a0d-1ead-4c00-8b01-000000000001";
    public const string FollowLeaderCode = "follow_leader";
    public const string FollowLeaderName = "Follow leader";

    public static readonly Guid FollowLeader = Guid.Parse(FollowLeaderIdValue);
}
