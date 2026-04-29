using System.Globalization;

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

public sealed record TraderLeaderboardEntry(
    int? Rank,
    string Wallet,
    string UserName,
    decimal Volume,
    decimal Pnl,
    string? ProfileImage,
    string? XUsername,
    bool VerifiedBadge);

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

public static class LeaderTradeDeduplication
{
    public static string BuildKey(LeaderTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);

        var wallet = Normalize(trade.TraderWallet);
        var asset = Normalize(trade.AssetId);
        var side = trade.Side.ToString().ToLowerInvariant();
        var timestamp = trade.TimestampUtc.ToUniversalTime().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var transactionHash = Normalize(trade.TransactionHash);

        if (!string.IsNullOrWhiteSpace(transactionHash))
        {
            return $"wallet:{wallet}|tx:{transactionHash}|asset:{asset}|side:{side}|ts:{timestamp}";
        }

        var price = trade.Price.ToString("0.########", CultureInfo.InvariantCulture);
        var size = trade.Size.ToString("0.########", CultureInfo.InvariantCulture);
        return $"wallet:{wallet}|fallback|asset:{asset}|side:{side}|ts:{timestamp}|price:{price}|size:{size}";
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}

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
    DateTimeOffset SnapshotAtUtc,
    decimal InitialValue = 0m,
    decimal PercentPnl = 0m,
    decimal TotalBought = 0m,
    decimal RealizedPnl = 0m,
    string? Title = null,
    string? MarketSlug = null,
    string? OppositeAsset = null,
    DateTimeOffset? EndDateUtc = null,
    bool NegativeRisk = false);

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
    ExposureSnapshot Exposure);

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
    DateTimeOffset? CancelledAtUtc = null);

public sealed record PaperPosition(
    string AssetId,
    string ConditionId,
    string Outcome,
    decimal SizeShares,
    decimal AveragePrice,
    decimal EstimatedValueUsd,
    decimal UnrealizedPnlUsd,
    DateTimeOffset UpdatedAtUtc);

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

public sealed record ScannerStatusSnapshot(
    string ScannerName,
    DateTimeOffset? LastSuccessfulScanUtc,
    DateTimeOffset? LastErrorUtc,
    string? LastErrorMessage,
    int TradesFetched,
    int NewTradesStored,
    int PositionsFetched,
    string ScannerStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed record GeoblockStatus(
    bool Blocked,
    string? Ip,
    string? Country,
    string? Region);

public sealed record ServiceHeartbeat(
    string ServiceName,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastHeartbeatUtc,
    string Version,
    BotMode Mode,
    string CurrentLoop,
    string? LastError);
