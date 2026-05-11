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

public enum OnChainParticipantRole
{
    Maker,
    Taker
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

public enum DryRunOrderStatus
{
    DryRunUnsigned,
    DryRunSigned,
    DryRunRejected
}

public enum LiveOrderStatus
{
    PreflightRejected,
    Submitted,
    Live,
    Matched,
    Delayed,
    Unmatched,
    CancelRequested,
    Cancelled,
    CancelFailed,
    Rejected,
    Error
}

public enum ServiceRunState
{
    Starting,
    Running,
    Paused,
    Stopping,
    Stopped,
    Error
}

public enum MarketDataConnectionState
{
    Disabled,
    Idle,
    Connecting,
    Connected,
    Reconnecting,
    Disconnected,
    Stale,
    Error
}

public enum MarketDataEventType
{
    Unknown,
    Book,
    PriceChange,
    LastTradePrice,
    BestBidAsk,
    TickSizeChange,
    MarketResolved
}

public enum TradeTickTraderMatchStatus
{
    NotFound = 1,
    FoundByTransactionHash = 2,
    FoundByComposite = 3
}

public sealed record BtcUsdReferencePricePoint(
    decimal PriceUsd,
    DateTimeOffset SourceUpdatedAtUtc,
    DateTimeOffset FetchedAtUtc,
    string Source);

public sealed record BtcUsdReferencePriceSnapshot(
    string Source,
    int WindowSize,
    int SampleCount,
    bool IsFullWindow,
    decimal? ArithmeticMeanUsd,
    BtcUsdReferencePricePoint? Latest,
    IReadOnlyList<BtcUsdReferencePricePoint> Samples,
    DateTimeOffset SnapshotAtUtc);

public sealed record CryptoReferencePricePoint(
    string AssetSymbol,
    string BinanceSymbol,
    decimal PriceUsd,
    DateTimeOffset SourceUpdatedAtUtc,
    DateTimeOffset FetchedAtUtc,
    string Source);

public sealed record BtcUsdReferenceCorrelationSample(
    Guid Id,
    decimal BinancePriceUsd,
    DateTimeOffset BinanceSourceUpdatedAtUtc,
    DateTimeOffset BinanceFetchedAtUtc,
    decimal ChainlinkPriceUsd,
    DateTimeOffset ChainlinkValidAfterUtc,
    decimal TimeDeltaSeconds,
    decimal PriceDiffUsd,
    decimal PriceDiffBps,
    string ChainlinkFeedId,
    string ChainlinkQueryWindow,
    string RawJson,
    DateTimeOffset CreatedAtUtc);

public sealed record BtcOrderBookLagDiagnosticEvent(
    Guid Id,
    string Source,
    string EventType,
    string? AssetId,
    string? ConditionId,
    string? BinanceSymbol,
    decimal? BinancePriceUsd,
    decimal? BestBid,
    decimal? BestBidSize,
    decimal? BestAsk,
    decimal? BestAskSize,
    decimal? Mid,
    decimal? TradePrice,
    decimal? TradeSize,
    DateTimeOffset? SourceTimestampUtc,
    DateTimeOffset ReceivedAtUtc,
    decimal? LocalLagMilliseconds,
    string RawEventType,
    DateTimeOffset CreatedAtUtc);

public sealed record BtcUpDown5mOddsTick(
    Guid Id,
    string MarketId,
    string ConditionId,
    string MarketSlug,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset MarketEndUtc,
    DateTimeOffset SampledAtUtc,
    decimal SecondsAfterStart,
    decimal SecondsToClose,
    decimal BinancePriceUsd,
    DateTimeOffset BinanceSourceUpdatedAtUtc,
    DateTimeOffset BinanceFetchedAtUtc,
    decimal BinanceStartPriceUsd,
    decimal BtcMoveFromStartUsd,
    decimal BtcMoveFromStartBps,
    string UpAssetId,
    decimal? UpBestBid,
    decimal? UpBestAsk,
    decimal? UpMid,
    decimal? UpPriceProxy,
    string UpPriceProxyKind,
    decimal? UpLastTradePrice,
    string UpBookSource,
    decimal? UpBookAgeMs,
    string DownAssetId,
    decimal? DownBestBid,
    decimal? DownBestAsk,
    decimal? DownMid,
    decimal? DownPriceProxy,
    string DownPriceProxyKind,
    decimal? DownLastTradePrice,
    string DownBookSource,
    decimal? DownBookAgeMs,
    string DiagnosticsJson,
    DateTimeOffset CreatedAtUtc);

public sealed record CryptoUpDown5mOddsTick(
    Guid Id,
    string AssetSymbol,
    string BinanceSymbol,
    string MarketId,
    string ConditionId,
    string MarketSlug,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset MarketEndUtc,
    DateTimeOffset SampledAtUtc,
    decimal SecondsAfterStart,
    decimal SecondsToClose,
    decimal BinancePriceUsd,
    DateTimeOffset BinanceSourceUpdatedAtUtc,
    DateTimeOffset BinanceFetchedAtUtc,
    decimal BinanceStartPriceUsd,
    decimal AssetMoveFromStartUsd,
    decimal AssetMoveFromStartBps,
    string UpAssetId,
    decimal? UpBestBid,
    decimal? UpBestAsk,
    decimal? UpMid,
    decimal? UpPriceProxy,
    string UpPriceProxyKind,
    decimal? UpLastTradePrice,
    string UpBookSource,
    decimal? UpBookAgeMs,
    string DownAssetId,
    decimal? DownBestBid,
    decimal? DownBestAsk,
    decimal? DownMid,
    decimal? DownPriceProxy,
    string DownPriceProxyKind,
    decimal? DownLastTradePrice,
    string DownBookSource,
    decimal? DownBookAgeMs,
    string DiagnosticsJson,
    DateTimeOffset CreatedAtUtc);

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

public sealed record TraderLeaderboardSnapshot(
    Guid Id,
    Guid DiscoveryRunId,
    string Category,
    string TimePeriod,
    string Wallet,
    string UserName,
    string? XUsername,
    bool VerifiedBadge,
    int? PnlRank,
    int? PnlPageOffset,
    decimal? PnlLeaderboardPnl,
    decimal? PnlLeaderboardVolume,
    DateTimeOffset? PnlSnapshotAtUtc,
    int? VolumeRank,
    int? VolumePageOffset,
    decimal? VolumeLeaderboardPnl,
    decimal? VolumeLeaderboardVolume,
    DateTimeOffset? VolumeSnapshotAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record TraderDiscoveryCandidate(
    Guid Id,
    string DiscoveryType,
    string Category,
    string TimePeriod,
    int? Rank,
    string Wallet,
    string UserName,
    string? XUsername,
    decimal LeaderboardPnl,
    decimal LeaderboardVolume,
    decimal? AllTimePnl,
    decimal? AllTimeVolume,
    bool VerifiedBadge,
    int TradesFetched,
    int BuyTrades,
    int SellTrades,
    decimal RecentTradeVolumeUsd,
    decimal AverageTradeUsd,
    DateTimeOffset? LastTradeUtc,
    int PositionsFetched,
    decimal OpenPositionValueUsd,
    decimal OpenPositionCashPnlUsd,
    decimal OpenPositionRealizedPnlUsd,
    string Notes,
    DateTimeOffset SnapshotAtUtc);

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

public sealed record PolymarketDataApiTrade(
    string TraderWallet,
    TradeSide Side,
    string AssetId,
    string ConditionId,
    decimal Size,
    decimal Price,
    DateTimeOffset TimestampUtc,
    string MarketTitle,
    string MarketSlug,
    string? Icon,
    string? EventSlug,
    string Outcome,
    int? OutcomeIndex,
    string TraderName,
    string? Pseudonym,
    string? Bio,
    string? ProfileImage,
    string? ProfileImageOptimized,
    string? TransactionHash,
    string RawJson)
{
    public decimal CashValueUsd => Price * Size;

    public LeaderTrade ToLeaderTrade()
    {
        return new LeaderTrade(
            TraderWallet,
            string.IsNullOrWhiteSpace(TraderName) ? Pseudonym ?? string.Empty : TraderName,
            ConditionId,
            AssetId,
            MarketSlug,
            MarketTitle,
            Outcome,
            Side,
            Price,
            Size,
            CashValueUsd,
            TimestampUtc,
            TransactionHash);
    }
}

public enum PolymarketDataApiActivityType
{
    Unknown,
    Trade,
    Split,
    Merge,
    Redeem,
    Reward,
    Conversion,
    MakerRebate,
    ReferralReward
}

public sealed record PolymarketDataApiActivity(
    string Wallet,
    DateTimeOffset TimestampUtc,
    string ConditionId,
    PolymarketDataApiActivityType Type,
    decimal Size,
    decimal UsdcSize,
    string? TransactionHash,
    decimal Price,
    string AssetId,
    TradeSide Side,
    int? OutcomeIndex,
    string MarketTitle,
    string MarketSlug,
    string? Icon,
    string? EventSlug,
    string Outcome,
    string TraderName,
    string? Pseudonym,
    string? Bio,
    string? ProfileImage,
    string? ProfileImageOptimized,
    string RawJson);

public sealed record PolymarketDataApiTrader(
    string Wallet,
    string Name,
    string? Pseudonym,
    string? Bio,
    string? ProfileImage,
    string? ProfileImageOptimized,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset? LastGlobalSeenAtUtc,
    DateTimeOffset? LastFullSyncAtUtc,
    DateTimeOffset? LastIncrementalSyncAtUtc,
    DateTimeOffset? LastTradeTimestampUtc,
    bool FullSyncCompleted,
    int FullSyncTradesFetched,
    int FullSyncTradesInserted,
    int IncrementalSyncCount,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? PolymarketRatingRefreshedAtUtc = null,
    DateTimeOffset? PolymarketRatingNextRefreshAtUtc = null,
    int PolymarketRatingRefreshAttempts = 0,
    string? PolymarketRatingLastError = null);

public sealed record PolymarketCategoryMapping(
    string LocalCategory,
    string PolymarketLeaderboardCategory);

public sealed record PolymarketDataApiWalletCategoryRating(
    string Wallet,
    string LocalCategory,
    string PolymarketCategory,
    string TimePeriod,
    string OrderBy,
    bool Found,
    int? Rank,
    string? UserName,
    string? XUsername,
    string? ProfileImage,
    bool VerifiedBadge,
    decimal? LeaderboardPnlUsd,
    decimal? LeaderboardVolumeUsd,
    decimal? LeaderboardPnlToVolumePct,
    DateTimeOffset RefreshedAtUtc,
    string RawJson,
    int CurrentPositionsCount = 0,
    decimal CurrentPositionsInitialValueUsd = 0m,
    decimal CurrentPositionsCurrentValueUsd = 0m,
    decimal CurrentPositionsCashPnlUsd = 0m,
    decimal CurrentPositionsRealizedPnlUsd = 0m,
    decimal CurrentPositionsTotalPnlUsd = 0m,
    decimal? CurrentPositionsPercentPnl = null,
    decimal? CurrentPositionsPercentRealizedPnl = null,
    int ClosedPositionsCount = 0,
    decimal ClosedPositionsCostBasisUsd = 0m,
    decimal ClosedPositionsRealizedPnlUsd = 0m,
    decimal? ClosedPositionsPercentRealizedPnl = null,
    decimal PositionsTotalCostBasisUsd = 0m,
    decimal PositionsTotalPnlUsd = 0m,
    decimal? PositionsTotalPercentPnl = null,
    DateTimeOffset? PositionsRefreshedAtUtc = null);

public enum PolymarketDataApiPositionStatus
{
    Open,
    Closed
}

public sealed record PolymarketDataApiPosition(
    string Wallet,
    PolymarketDataApiPositionStatus Status,
    string AssetId,
    string ConditionId,
    decimal? Size,
    decimal AvgPrice,
    decimal? InitialValue,
    decimal? CurrentValue,
    decimal? CashPnl,
    decimal? PercentPnl,
    decimal TotalBought,
    decimal RealizedPnl,
    decimal? PercentRealizedPnl,
    decimal CurPrice,
    DateTimeOffset? TimestampUtc,
    string MarketTitle,
    string MarketSlug,
    string? Icon,
    string? EventId,
    string? EventSlug,
    string? Category,
    string Outcome,
    int? OutcomeIndex,
    string? OppositeOutcome,
    string? OppositeAsset,
    DateTimeOffset? EndDateUtc,
    bool? Redeemable,
    bool? Mergeable,
    bool? NegativeRisk,
    string RawJson)
{
    public decimal CostBasisUsd => Status == PolymarketDataApiPositionStatus.Open
        ? InitialValue ?? TotalBought * AvgPrice
        : TotalBought * AvgPrice;

    public decimal PositionPnlUsd => Status == PolymarketDataApiPositionStatus.Open
        ? (CashPnl ?? 0m) + RealizedPnl
        : RealizedPnl;
}

public sealed record PolymarketDataApiPerformanceRefreshResult(
    int CurrentPositionsFetched,
    int ClosedPositionsFetched,
    int PositionsUpserted,
    int WalletPerformanceRowsUpserted,
    int CategoryPerformanceRowsUpserted);

public static class LeaderTradeDeduplication
{
    public static string BuildKey(LeaderTrade trade)
    {
        if (trade is null)
        {
            throw new ArgumentNullException(nameof(trade));
        }

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

public sealed record PolymarketGammaMarket(
    string MarketId,
    string ConditionId,
    string QuestionId,
    string Slug,
    string Question,
    string? EventId,
    string? EventSlug,
    string? EventTitle,
    string? SeriesSlug,
    string? Category,
    bool Active,
    bool Closed,
    bool Archived,
    bool Restricted,
    bool AcceptingOrders,
    bool EnableOrderBook,
    bool NegativeRisk,
    decimal? Liquidity,
    decimal? LiquidityClob,
    decimal? Volume,
    decimal? Volume24Hr,
    decimal? BestBid,
    decimal? BestAsk,
    decimal? Spread,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? StartDateUtc,
    DateTimeOffset? EndDateUtc,
    DateTimeOffset? EventStartTimeUtc,
    IReadOnlyList<string> Outcomes,
    IReadOnlyList<string> ClobTokenIds,
    string RawJson,
    DateTimeOffset FetchedAtUtc,
    decimal? LastTradePrice = null,
    decimal? OrderMinSize = null,
    decimal? OrderPriceMinTickSize = null);

public sealed record ActiveMarketAssetSnapshot(
    string AssetId,
    string MarketId,
    string ConditionId,
    string QuestionId,
    string Slug,
    string Question,
    string? EventId,
    string? EventSlug,
    string? EventTitle,
    string? SeriesSlug,
    string? Category,
    string Outcome,
    int OutcomeIndex,
    IReadOnlyList<string> Outcomes,
    IReadOnlyList<string> ClobTokenIds,
    bool Active,
    bool Closed,
    bool Archived,
    bool Restricted,
    bool AcceptingOrders,
    bool EnableOrderBook,
    bool NegativeRisk,
    decimal? Liquidity,
    decimal? LiquidityClob,
    decimal? Volume,
    decimal? Volume24Hr,
    decimal? BestBid,
    decimal? BestAsk,
    decimal? Spread,
    decimal? LastTradePrice,
    decimal? OrderMinSize,
    decimal? OrderPriceMinTickSize,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? StartDateUtc,
    DateTimeOffset? EndDateUtc,
    DateTimeOffset? EventStartTimeUtc,
    DateTimeOffset MarketFetchedAtUtc,
    DateTimeOffset? OrderBookUpdatedAtUtc,
    DateTimeOffset SnapshotUpdatedAtUtc)
{
    public bool IsSubscribable => Active && !Closed;

    public bool AllowsOrders => Active && !Closed && !Archived && AcceptingOrders && EnableOrderBook;
}

public sealed record GammaMarketIngestionResult(
    int PagesFetched,
    int MarketsFetched,
    int MarketsUpserted,
    bool ReachedEmptyPage,
    int NextOffset);

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

public sealed record MarketDataUpdate(
    MarketDataEventType EventType,
    string RawEventType,
    string? AssetId,
    string? ConditionId,
    OrderBookSnapshot? OrderBookSnapshot,
    decimal? BestBid,
    decimal? BestAsk,
    decimal? Price,
    decimal? Size,
    TradeSide Side,
    bool MarketResolved,
    DateTimeOffset TimestampUtc,
    string? TransactionHash = null,
    string RawJson = "{}",
    string? WinningAssetId = null,
    string? WinningOutcome = null);

public sealed record PolymarketWebSocketTradeTick(
    Guid Id,
    string DedupKey,
    string AssetId,
    string? ConditionId,
    TradeSide Side,
    decimal? Price,
    decimal? Size,
    DateTimeOffset TradeTimestampUtc,
    string? TransactionHash,
    bool TransactionHashPresent,
    TradeTickTraderMatchStatus TraderMatchStatus,
    string? TraderWallet,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? MatchedAtUtc,
    int MatchAttempts,
    DateTimeOffset? LastMatchAttemptUtc,
    string? LastMatchError,
    string? MatchedTransactionHash,
    string? MatchDetails,
    string RawJson,
    DateTimeOffset UpdatedAtUtc);

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

public sealed record SignalSummary(
    Guid Id,
    string TraderWallet,
    string ConditionId,
    string AssetId,
    string Outcome,
    decimal LeaderPrice,
    decimal? BestBid,
    decimal? BestAsk,
    decimal? SpreadAbs,
    decimal? SpreadPct,
    int? LagSeconds,
    int Score,
    bool Accepted,
    string DecisionCode,
    IReadOnlyList<string> ReasonCodes,
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

public sealed record BtcUpDown5mMarketResult(
    string MarketId,
    string ConditionId,
    string MarketSlug,
    DateTimeOffset? MarketStartUtc,
    DateTimeOffset? MarketEndUtc,
    string WinningOutcome,
    DateTimeOffset SettledAtUtc);

public static class StrategyIds
{
    public const string FollowLeaderIdValue = "f0110a0d-1ead-4c00-8b01-000000000001";
    public const string FollowLeaderCode = "follow_leader";
    public const string FollowLeaderName = "Follow leader";
    public const string BtcUpDown5mLess180MartinIdValue = "b7c50005-0000-4000-8003-000000000180";
    public const string BtcUpDown5mLess180MartinCode = "btc_up_down_5m_less_180_martin";
    public const string BtcUpDown5mLess180MartinName = "BTC Less 180 Martin";
    public const string BtcUpDown5mAlwaysUpIdValue = "b7c50005-0000-4000-8010-000000000001";
    public const string BtcUpDown5mAlwaysDownIdValue = "b7c50005-0000-4000-8010-000000000002";
    public const string BtcUpDown5mBinanceIdValue = "b7c50005-0000-4000-8011-000000000001";
    public const string BtcUpDown5mBinanceCleverIdValue = "b7c50005-0000-4000-8011-000000000002";
    public const string BtcUpDown5mBinance45IdValue = "b7c50005-0000-4000-8011-000000000045";
    public const string BtcUpDown5mBinance47IdValue = "b7c50005-0000-4000-8011-000000000047";
    public const string BtcUpDown5mBinance49IdValue = "b7c50005-0000-4000-8011-000000000049";
    public const string BtcUpDown5mBinanceCleverAggressiveIdValue = "b7c50005-0000-4000-8011-000000000101";
    public const string BtcUpDown5mBinanceCleverConservativeIdValue = "b7c50005-0000-4000-8011-000000000105";
    public const string BtcUpDown5mBinanceBps01IdValue = "b7c50005-0000-4000-8013-000000000010";
    public const string BtcUpDown5mBinanceBps02IdValue = "b7c50005-0000-4000-8013-000000000020";
    public const string BtcUpDown5mBinanceBps03IdValue = "b7c50005-0000-4000-8013-000000000030";
    public const string BtcUpDown5mBinanceBps04IdValue = "b7c50005-0000-4000-8013-000000000040";
    public const string BtcUpDown5mBinanceBps05IdValue = "b7c50005-0000-4000-8013-000000000050";
    public const string BtcUpDown5mBinanceBps06IdValue = "b7c50005-0000-4000-8013-000000000060";
    public const string BtcUpDown5mBinanceBps07IdValue = "b7c50005-0000-4000-8013-000000000070";
    public const string BtcUpDown5mBinanceBps08IdValue = "b7c50005-0000-4000-8013-000000000080";
    public const string BtcUpDown5mBinanceBps09IdValue = "b7c50005-0000-4000-8013-000000000090";
    public const string BtcUpDown5mBinanceBps1IdValue = "b7c50005-0000-4000-8013-000000000001";
    public const string BtcUpDown5mBinanceBps2IdValue = "b7c50005-0000-4000-8013-000000000002";
    public const string BtcUpDown5mBinanceBps5IdValue = "b7c50005-0000-4000-8013-000000000005";
    public const string BtcUpDown5mMore90Below70IdValue = "b7c50005-0000-4000-8012-000000000070";
    public const string BtcUpDown5mMore90Below65IdValue = "b7c50005-0000-4000-8012-000000000065";
    public const string BtcUpDown5mMore90Below60IdValue = "b7c50005-0000-4000-8012-000000000060";
    public const string BtcUpDown5mMore90Below55IdValue = "b7c50005-0000-4000-8012-000000000055";
    public const string BtcUpDown5mMore60Below60IdValue = "b7c50005-0000-4000-8019-000000000060";
    public const string BtcUpDown5mMore60Below55IdValue = "b7c50005-0000-4000-8019-000000000055";
    public const string BtcUpDown5mMore30Below55IdValue = "b7c50005-0000-4000-8020-000000030055";
    public const string BtcUpDown5mMore120Below70IdValue = "b7c50005-0000-4000-8020-000000120070";
    public const string BtcUpDown5mMore150Below65IdValue = "b7c50005-0000-4000-8020-000000150065";
    public const string BtcUpDown5mMore270Below65IdValue = "b7c50005-0000-4000-8020-000000270065";
    public const string BtcUpDown5mMore270Below60IdValue = "b7c50005-0000-4000-8020-000000270060";
    public const string BtcUpDown5mLess120Below20IdValue = "b7c50005-0000-4000-8021-000000120020";
    public const string BtcUpDown5mLess120Below30IdValue = "b7c50005-0000-4000-8021-000000120030";
    public const string BtcUpDown5mLess90Below20IdValue = "b7c50005-0000-4000-8021-000000090020";
    public const string BtcUpDown5mLess60Below20IdValue = "b7c50005-0000-4000-8021-000000060020";
    public const string BtcUpDown5mMore60GammaBelow70IdValue = "b7c50005-0000-4000-8022-000000060070";
    public const string BtcUpDown5mMore60GammaBelow80IdValue = "b7c50005-0000-4000-8022-000000060080";
    public const string BtcUpDown5mMore90GammaBelow70IdValue = "b7c50005-0000-4000-8022-000000090070";
    public const string BtcUpDown5mMore120GammaBelow65IdValue = "b7c50005-0000-4000-8022-000000120065";
    public const string BtcUpDown5mMore120GammaBelow70IdValue = "b7c50005-0000-4000-8022-000000120070";
    public const string BtcUpDown5mMore150GammaBelow70IdValue = "b7c50005-0000-4000-8022-000000150070";
    public const string BtcUpDown5mMore150GammaBelow80IdValue = "b7c50005-0000-4000-8022-000000150080";
    public const string BtcUpDown5mBinanceEdge2IdValue = "b7c50005-0000-4000-8014-000000000002";
    public const string BtcUpDown5mBinanceEdge4IdValue = "b7c50005-0000-4000-8014-000000000004";
    public const string BtcUpDown5mBinanceEdge6IdValue = "b7c50005-0000-4000-8014-000000000006";
    public const string BtcUpDown5mBinanceDelayed15IdValue = "b7c50005-0000-4000-8015-000000000015";
    public const string BtcUpDown5mBinanceDelayed30IdValue = "b7c50005-0000-4000-8015-000000000030";
    public const string BtcUpDown5mBinanceDelayed45IdValue = "b7c50005-0000-4000-8015-000000000045";
    public const string BtcUpDown5mEnsemble2Of3IdValue = "b7c50005-0000-4000-8016-000000000002";
    public const string BtcUpDown5mDynamicMarkovIdValue = "b7c50005-0000-4000-8017-000000000050";
    public const string BtcUpDown5mStrategySelectorIdValue = "b7c50005-0000-4000-8018-000000000030";
    public const string BtcUpDown5mAlwaysUpCode = "btc_up_down_5m_up";
    public const string BtcUpDown5mAlwaysDownCode = "btc_up_down_5m_down";
    public const string BtcUpDown5mBinanceCode = "btc_up_down_5m_binance";
    public const string BtcUpDown5mBinanceCleverCode = "btc_up_down_5m_binance_clever";
    public const string BtcUpDown5mBinance45Code = "btc_up_down_5m_binance_45";
    public const string BtcUpDown5mBinance47Code = "btc_up_down_5m_binance_47";
    public const string BtcUpDown5mBinance49Code = "btc_up_down_5m_binance_49";
    public const string BtcUpDown5mBinanceCleverAggressiveCode = "btc_up_down_5m_binance_clever_aggressive";
    public const string BtcUpDown5mBinanceCleverConservativeCode = "btc_up_down_5m_binance_clever_conservative";
    public const string BtcUpDown5mBinanceBps01Code = "btc_up_down_5m_binance_bps_0_1";
    public const string BtcUpDown5mBinanceBps02Code = "btc_up_down_5m_binance_bps_0_2";
    public const string BtcUpDown5mBinanceBps03Code = "btc_up_down_5m_binance_bps_0_3";
    public const string BtcUpDown5mBinanceBps04Code = "btc_up_down_5m_binance_bps_0_4";
    public const string BtcUpDown5mBinanceBps05Code = "btc_up_down_5m_binance_bps_0_5";
    public const string BtcUpDown5mBinanceBps06Code = "btc_up_down_5m_binance_bps_0_6";
    public const string BtcUpDown5mBinanceBps07Code = "btc_up_down_5m_binance_bps_0_7";
    public const string BtcUpDown5mBinanceBps08Code = "btc_up_down_5m_binance_bps_0_8";
    public const string BtcUpDown5mBinanceBps09Code = "btc_up_down_5m_binance_bps_0_9";
    public const string BtcUpDown5mBinanceBps1Code = "btc_up_down_5m_binance_bps_1";
    public const string BtcUpDown5mBinanceBps2Code = "btc_up_down_5m_binance_bps_2";
    public const string BtcUpDown5mBinanceBps5Code = "btc_up_down_5m_binance_bps_5";
    public const string BtcUpDown5mMore90Below70Code = "btc_up_down_5m_more_90_below_70";
    public const string BtcUpDown5mMore90Below65Code = "btc_up_down_5m_more_90_below_65";
    public const string BtcUpDown5mMore90Below60Code = "btc_up_down_5m_more_90_below_60";
    public const string BtcUpDown5mMore90Below55Code = "btc_up_down_5m_more_90_below_55";
    public const string BtcUpDown5mMore60Below60Code = "btc_up_down_5m_more_60_below_60";
    public const string BtcUpDown5mMore60Below55Code = "btc_up_down_5m_more_60_below_55";
    public const string BtcUpDown5mMore30Below55Code = "btc_up_down_5m_more_30_below_55";
    public const string BtcUpDown5mMore120Below70Code = "btc_up_down_5m_more_120_below_70";
    public const string BtcUpDown5mMore150Below65Code = "btc_up_down_5m_more_150_below_65";
    public const string BtcUpDown5mMore270Below65Code = "btc_up_down_5m_more_270_below_65";
    public const string BtcUpDown5mMore270Below60Code = "btc_up_down_5m_more_270_below_60";
    public const string BtcUpDown5mLess120Below20Code = "btc_up_down_5m_less_120_below_20";
    public const string BtcUpDown5mLess120Below30Code = "btc_up_down_5m_less_120_below_30";
    public const string BtcUpDown5mLess90Below20Code = "btc_up_down_5m_less_90_below_20";
    public const string BtcUpDown5mLess60Below20Code = "btc_up_down_5m_less_60_below_20";
    public const string BtcUpDown5mMore60GammaBelow70Code = "btc_up_down_5m_more_60_gamma_below_70";
    public const string BtcUpDown5mMore60GammaBelow80Code = "btc_up_down_5m_more_60_gamma_below_80";
    public const string BtcUpDown5mMore90GammaBelow70Code = "btc_up_down_5m_more_90_gamma_below_70";
    public const string BtcUpDown5mMore120GammaBelow65Code = "btc_up_down_5m_more_120_gamma_below_65";
    public const string BtcUpDown5mMore120GammaBelow70Code = "btc_up_down_5m_more_120_gamma_below_70";
    public const string BtcUpDown5mMore150GammaBelow70Code = "btc_up_down_5m_more_150_gamma_below_70";
    public const string BtcUpDown5mMore150GammaBelow80Code = "btc_up_down_5m_more_150_gamma_below_80";
    public const string BtcUpDown5mBinanceEdge2Code = "btc_up_down_5m_binance_edge_2";
    public const string BtcUpDown5mBinanceEdge4Code = "btc_up_down_5m_binance_edge_4";
    public const string BtcUpDown5mBinanceEdge6Code = "btc_up_down_5m_binance_edge_6";
    public const string BtcUpDown5mBinanceDelayed15Code = "btc_up_down_5m_binance_15s";
    public const string BtcUpDown5mBinanceDelayed30Code = "btc_up_down_5m_binance_30s";
    public const string BtcUpDown5mBinanceDelayed45Code = "btc_up_down_5m_binance_45s";
    public const string BtcUpDown5mEnsemble2Of3Code = "btc_up_down_5m_ensemble_2_of_3";
    public const string BtcUpDown5mDynamicMarkovCode = "btc_up_down_5m_dynamic_markov";
    public const string BtcUpDown5mStrategySelectorCode = "btc_up_down_5m_strategy_selector";

    public static readonly Guid FollowLeader = Guid.Parse(FollowLeaderIdValue);
    public static readonly Guid BtcUpDown5mLess180Martin = Guid.Parse(BtcUpDown5mLess180MartinIdValue);
    public static readonly Guid BtcUpDown5mAlwaysUp = Guid.Parse(BtcUpDown5mAlwaysUpIdValue);
    public static readonly Guid BtcUpDown5mAlwaysDown = Guid.Parse(BtcUpDown5mAlwaysDownIdValue);
    public static readonly Guid BtcUpDown5mBinance = Guid.Parse(BtcUpDown5mBinanceIdValue);
    public static readonly Guid BtcUpDown5mBinanceClever = Guid.Parse(BtcUpDown5mBinanceCleverIdValue);
    public static readonly Guid BtcUpDown5mBinance45 = Guid.Parse(BtcUpDown5mBinance45IdValue);
    public static readonly Guid BtcUpDown5mBinance47 = Guid.Parse(BtcUpDown5mBinance47IdValue);
    public static readonly Guid BtcUpDown5mBinance49 = Guid.Parse(BtcUpDown5mBinance49IdValue);
    public static readonly Guid BtcUpDown5mBinanceCleverAggressive = Guid.Parse(BtcUpDown5mBinanceCleverAggressiveIdValue);
    public static readonly Guid BtcUpDown5mBinanceCleverConservative = Guid.Parse(BtcUpDown5mBinanceCleverConservativeIdValue);
    public static readonly Guid BtcUpDown5mBinanceBps01 = Guid.Parse(BtcUpDown5mBinanceBps01IdValue);
    public static readonly Guid BtcUpDown5mBinanceBps02 = Guid.Parse(BtcUpDown5mBinanceBps02IdValue);
    public static readonly Guid BtcUpDown5mBinanceBps03 = Guid.Parse(BtcUpDown5mBinanceBps03IdValue);
    public static readonly Guid BtcUpDown5mBinanceBps04 = Guid.Parse(BtcUpDown5mBinanceBps04IdValue);
    public static readonly Guid BtcUpDown5mBinanceBps05 = Guid.Parse(BtcUpDown5mBinanceBps05IdValue);
    public static readonly Guid BtcUpDown5mBinanceBps06 = Guid.Parse(BtcUpDown5mBinanceBps06IdValue);
    public static readonly Guid BtcUpDown5mBinanceBps07 = Guid.Parse(BtcUpDown5mBinanceBps07IdValue);
    public static readonly Guid BtcUpDown5mBinanceBps08 = Guid.Parse(BtcUpDown5mBinanceBps08IdValue);
    public static readonly Guid BtcUpDown5mBinanceBps09 = Guid.Parse(BtcUpDown5mBinanceBps09IdValue);
    public static readonly Guid BtcUpDown5mBinanceBps1 = Guid.Parse(BtcUpDown5mBinanceBps1IdValue);
    public static readonly Guid BtcUpDown5mBinanceBps2 = Guid.Parse(BtcUpDown5mBinanceBps2IdValue);
    public static readonly Guid BtcUpDown5mBinanceBps5 = Guid.Parse(BtcUpDown5mBinanceBps5IdValue);
    public static readonly Guid BtcUpDown5mMore90Below70 = Guid.Parse(BtcUpDown5mMore90Below70IdValue);
    public static readonly Guid BtcUpDown5mMore90Below65 = Guid.Parse(BtcUpDown5mMore90Below65IdValue);
    public static readonly Guid BtcUpDown5mMore90Below60 = Guid.Parse(BtcUpDown5mMore90Below60IdValue);
    public static readonly Guid BtcUpDown5mMore90Below55 = Guid.Parse(BtcUpDown5mMore90Below55IdValue);
    public static readonly Guid BtcUpDown5mMore60Below60 = Guid.Parse(BtcUpDown5mMore60Below60IdValue);
    public static readonly Guid BtcUpDown5mMore60Below55 = Guid.Parse(BtcUpDown5mMore60Below55IdValue);
    public static readonly Guid BtcUpDown5mMore30Below55 = Guid.Parse(BtcUpDown5mMore30Below55IdValue);
    public static readonly Guid BtcUpDown5mMore120Below70 = Guid.Parse(BtcUpDown5mMore120Below70IdValue);
    public static readonly Guid BtcUpDown5mMore150Below65 = Guid.Parse(BtcUpDown5mMore150Below65IdValue);
    public static readonly Guid BtcUpDown5mMore270Below65 = Guid.Parse(BtcUpDown5mMore270Below65IdValue);
    public static readonly Guid BtcUpDown5mMore270Below60 = Guid.Parse(BtcUpDown5mMore270Below60IdValue);
    public static readonly Guid BtcUpDown5mLess120Below20 = Guid.Parse(BtcUpDown5mLess120Below20IdValue);
    public static readonly Guid BtcUpDown5mLess120Below30 = Guid.Parse(BtcUpDown5mLess120Below30IdValue);
    public static readonly Guid BtcUpDown5mLess90Below20 = Guid.Parse(BtcUpDown5mLess90Below20IdValue);
    public static readonly Guid BtcUpDown5mLess60Below20 = Guid.Parse(BtcUpDown5mLess60Below20IdValue);
    public static readonly Guid BtcUpDown5mMore60GammaBelow70 = Guid.Parse(BtcUpDown5mMore60GammaBelow70IdValue);
    public static readonly Guid BtcUpDown5mMore60GammaBelow80 = Guid.Parse(BtcUpDown5mMore60GammaBelow80IdValue);
    public static readonly Guid BtcUpDown5mMore90GammaBelow70 = Guid.Parse(BtcUpDown5mMore90GammaBelow70IdValue);
    public static readonly Guid BtcUpDown5mMore120GammaBelow65 = Guid.Parse(BtcUpDown5mMore120GammaBelow65IdValue);
    public static readonly Guid BtcUpDown5mMore120GammaBelow70 = Guid.Parse(BtcUpDown5mMore120GammaBelow70IdValue);
    public static readonly Guid BtcUpDown5mMore150GammaBelow70 = Guid.Parse(BtcUpDown5mMore150GammaBelow70IdValue);
    public static readonly Guid BtcUpDown5mMore150GammaBelow80 = Guid.Parse(BtcUpDown5mMore150GammaBelow80IdValue);
    public static readonly Guid BtcUpDown5mBinanceEdge2 = Guid.Parse(BtcUpDown5mBinanceEdge2IdValue);
    public static readonly Guid BtcUpDown5mBinanceEdge4 = Guid.Parse(BtcUpDown5mBinanceEdge4IdValue);
    public static readonly Guid BtcUpDown5mBinanceEdge6 = Guid.Parse(BtcUpDown5mBinanceEdge6IdValue);
    public static readonly Guid BtcUpDown5mBinanceDelayed15 = Guid.Parse(BtcUpDown5mBinanceDelayed15IdValue);
    public static readonly Guid BtcUpDown5mBinanceDelayed30 = Guid.Parse(BtcUpDown5mBinanceDelayed30IdValue);
    public static readonly Guid BtcUpDown5mBinanceDelayed45 = Guid.Parse(BtcUpDown5mBinanceDelayed45IdValue);
    public static readonly Guid BtcUpDown5mEnsemble2Of3 = Guid.Parse(BtcUpDown5mEnsemble2Of3IdValue);
    public static readonly Guid BtcUpDown5mDynamicMarkov = Guid.Parse(BtcUpDown5mDynamicMarkovIdValue);
    public static readonly Guid BtcUpDown5mStrategySelector = Guid.Parse(BtcUpDown5mStrategySelectorIdValue);

    public static readonly IReadOnlyList<BtcUpDown5mStrategyVariant> BtcUpDown5mVariants =
        CreateBtcUpDown5mVariants();

    public static readonly IReadOnlyList<Guid> AllStrategyIds =
        [FollowLeader, .. BtcUpDown5mVariants.Select(variant => variant.Id)];

    public static Guid Normalize(Guid strategyId)
    {
        return strategyId == Guid.Empty ? FollowLeader : strategyId;
    }

    public static BtcUpDown5mStrategyVariant GetBtcUpDown5mVariant(
        BtcUpDown5mStrategyDirection direction,
        int entryDelaySeconds,
        BtcUpDown5mStrategyBehavior behavior = BtcUpDown5mStrategyBehavior.Standard)
    {
        return BtcUpDown5mVariants.First(variant =>
            variant.Direction == direction &&
            variant.EntryDelaySeconds == entryDelaySeconds &&
            variant.Behavior == behavior);
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateBtcUpDown5mVariants()
    {
        int[] delays = [30, 60, 90, 120, 150, 180, 210, 240, 270];
        var variants = new List<BtcUpDown5mStrategyVariant>(100);

        foreach (var delay in delays)
        {
            variants.Add(CreateBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.Less, delay, 1, BtcUpDown5mStrategyBehavior.Standard));
            if (delay == 180)
            {
                variants.Add(new BtcUpDown5mStrategyVariant(
                    BtcUpDown5mLess180Martin,
                    BtcUpDown5mLess180MartinCode,
                    BtcUpDown5mLess180MartinName,
                    "After BTC Less 180 loses three times in a row, bet on the lower-priced BTC 5m outcome 180 seconds after window start using a 1/2/4/8/16 USD paper stake progression until this strategy wins.",
                    BtcUpDown5mStrategyDirection.Less,
                    180,
                    BtcUpDown5mStrategyBehavior.Less180Martin));
            }
        }

        foreach (var delay in delays)
        {
            variants.Add(CreateBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.More, delay, 2, BtcUpDown5mStrategyBehavior.Standard));
        }

        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mMore30Below55, BtcUpDown5mMore30Below55Code, BtcUpDown5mStrategyDirection.More, 30, 55));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mMore60Below60, BtcUpDown5mMore60Below60Code, BtcUpDown5mStrategyDirection.More, 60, 60));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mMore60Below55, BtcUpDown5mMore60Below55Code, BtcUpDown5mStrategyDirection.More, 60, 55));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mMore90Below70, BtcUpDown5mMore90Below70Code, BtcUpDown5mStrategyDirection.More, 90, 70));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mMore90Below65, BtcUpDown5mMore90Below65Code, BtcUpDown5mStrategyDirection.More, 90, 65));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mMore90Below60, BtcUpDown5mMore90Below60Code, BtcUpDown5mStrategyDirection.More, 90, 60));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mMore90Below55, BtcUpDown5mMore90Below55Code, BtcUpDown5mStrategyDirection.More, 90, 55));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mMore120Below70, BtcUpDown5mMore120Below70Code, BtcUpDown5mStrategyDirection.More, 120, 70));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mMore150Below65, BtcUpDown5mMore150Below65Code, BtcUpDown5mStrategyDirection.More, 150, 65));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mMore270Below65, BtcUpDown5mMore270Below65Code, BtcUpDown5mStrategyDirection.More, 270, 65));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mMore270Below60, BtcUpDown5mMore270Below60Code, BtcUpDown5mStrategyDirection.More, 270, 60));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mLess60Below20, BtcUpDown5mLess60Below20Code, BtcUpDown5mStrategyDirection.Less, 60, 20));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mLess90Below20, BtcUpDown5mLess90Below20Code, BtcUpDown5mStrategyDirection.Less, 90, 20));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mLess120Below20, BtcUpDown5mLess120Below20Code, BtcUpDown5mStrategyDirection.Less, 120, 20));
        variants.Add(CreateBtcUpDown5mEntryPriceCapVariant(BtcUpDown5mLess120Below30, BtcUpDown5mLess120Below30Code, BtcUpDown5mStrategyDirection.Less, 120, 30));

        foreach (var delay in delays)
        {
            variants.Add(CreateBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.Less, delay, 4, BtcUpDown5mStrategyBehavior.GammaOutcomeSelection));
        }

        foreach (var delay in delays)
        {
            variants.Add(CreateBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.More, delay, 5, BtcUpDown5mStrategyBehavior.GammaOutcomeSelection));
        }

        variants.Add(CreateBtcUpDown5mGammaEntryPriceCapVariant(BtcUpDown5mMore60GammaBelow70, BtcUpDown5mMore60GammaBelow70Code, BtcUpDown5mStrategyDirection.More, 60, 70));
        variants.Add(CreateBtcUpDown5mGammaEntryPriceCapVariant(BtcUpDown5mMore60GammaBelow80, BtcUpDown5mMore60GammaBelow80Code, BtcUpDown5mStrategyDirection.More, 60, 80));
        variants.Add(CreateBtcUpDown5mGammaEntryPriceCapVariant(BtcUpDown5mMore90GammaBelow70, BtcUpDown5mMore90GammaBelow70Code, BtcUpDown5mStrategyDirection.More, 90, 70));
        variants.Add(CreateBtcUpDown5mGammaEntryPriceCapVariant(BtcUpDown5mMore120GammaBelow65, BtcUpDown5mMore120GammaBelow65Code, BtcUpDown5mStrategyDirection.More, 120, 65));
        variants.Add(CreateBtcUpDown5mGammaEntryPriceCapVariant(BtcUpDown5mMore120GammaBelow70, BtcUpDown5mMore120GammaBelow70Code, BtcUpDown5mStrategyDirection.More, 120, 70));
        variants.Add(CreateBtcUpDown5mGammaEntryPriceCapVariant(BtcUpDown5mMore150GammaBelow70, BtcUpDown5mMore150GammaBelow70Code, BtcUpDown5mStrategyDirection.More, 150, 70));
        variants.Add(CreateBtcUpDown5mGammaEntryPriceCapVariant(BtcUpDown5mMore150GammaBelow80, BtcUpDown5mMore150GammaBelow80Code, BtcUpDown5mStrategyDirection.More, 150, 80));

        for (var depth = 1; depth <= 5; depth++)
        {
            variants.Add(CreateBtcUpDown5mMiddleVariant(depth));
        }

        for (var depth = 1; depth <= 5; depth++)
        {
            variants.Add(CreateBtcUpDown5mMiddleRevertVariant(depth));
        }

        for (var depth = 1; depth <= 5; depth++)
        {
            variants.Add(CreateBtcUpDown5mSkipVariant(depth));
        }

        for (var depth = 1; depth <= 5; depth++)
        {
            variants.Add(CreateBtcUpDown5mSkipRevertVariant(depth));
        }

        variants.Add(CreateBtcUpDown5mAlwaysDirectionVariant(isUp: true));
        variants.Add(CreateBtcUpDown5mAlwaysDirectionVariant(isUp: false));
        variants.Add(CreateBtcUpDown5mBinanceVariant());
        variants.Add(CreateBtcUpDown5mBinanceBpsThresholdVariant(BtcUpDown5mBinanceBps01, BtcUpDown5mBinanceBps01Code, 0.1m));
        variants.Add(CreateBtcUpDown5mBinanceBpsThresholdVariant(BtcUpDown5mBinanceBps02, BtcUpDown5mBinanceBps02Code, 0.2m));
        variants.Add(CreateBtcUpDown5mBinanceBpsThresholdVariant(BtcUpDown5mBinanceBps03, BtcUpDown5mBinanceBps03Code, 0.3m));
        variants.Add(CreateBtcUpDown5mBinanceBpsThresholdVariant(BtcUpDown5mBinanceBps04, BtcUpDown5mBinanceBps04Code, 0.4m));
        variants.Add(CreateBtcUpDown5mBinanceBpsThresholdVariant(BtcUpDown5mBinanceBps05, BtcUpDown5mBinanceBps05Code, 0.5m));
        variants.Add(CreateBtcUpDown5mBinanceBpsThresholdVariant(BtcUpDown5mBinanceBps06, BtcUpDown5mBinanceBps06Code, 0.6m));
        variants.Add(CreateBtcUpDown5mBinanceBpsThresholdVariant(BtcUpDown5mBinanceBps07, BtcUpDown5mBinanceBps07Code, 0.7m));
        variants.Add(CreateBtcUpDown5mBinanceBpsThresholdVariant(BtcUpDown5mBinanceBps08, BtcUpDown5mBinanceBps08Code, 0.8m));
        variants.Add(CreateBtcUpDown5mBinanceBpsThresholdVariant(BtcUpDown5mBinanceBps09, BtcUpDown5mBinanceBps09Code, 0.9m));
        variants.Add(CreateBtcUpDown5mBinanceBpsThresholdVariant(BtcUpDown5mBinanceBps1, BtcUpDown5mBinanceBps1Code, 1));
        variants.Add(CreateBtcUpDown5mBinanceBpsThresholdVariant(BtcUpDown5mBinanceBps2, BtcUpDown5mBinanceBps2Code, 2));
        variants.Add(CreateBtcUpDown5mBinanceBpsThresholdVariant(BtcUpDown5mBinanceBps5, BtcUpDown5mBinanceBps5Code, 5));
        variants.Add(CreateBtcUpDown5mBinanceFixedPriceVariant(BtcUpDown5mBinance45, BtcUpDown5mBinance45Code, 45));
        variants.Add(CreateBtcUpDown5mBinanceFixedPriceVariant(BtcUpDown5mBinance47, BtcUpDown5mBinance47Code, 47));
        variants.Add(CreateBtcUpDown5mBinanceFixedPriceVariant(BtcUpDown5mBinance49, BtcUpDown5mBinance49Code, 49));
        variants.Add(CreateBtcUpDown5mBinanceCleverVariant());
        variants.Add(CreateBtcUpDown5mBinanceCleverMarginVariant(BtcUpDown5mBinanceCleverAggressive, BtcUpDown5mBinanceCleverAggressiveCode, "Aggressive", 1));
        variants.Add(CreateBtcUpDown5mBinanceCleverMarginVariant(BtcUpDown5mBinanceCleverConservative, BtcUpDown5mBinanceCleverConservativeCode, "Conservative", 5));
        variants.Add(CreateBtcUpDown5mBinanceEdgeVariant(BtcUpDown5mBinanceEdge2, BtcUpDown5mBinanceEdge2Code, 2));
        variants.Add(CreateBtcUpDown5mBinanceEdgeVariant(BtcUpDown5mBinanceEdge4, BtcUpDown5mBinanceEdge4Code, 4));
        variants.Add(CreateBtcUpDown5mBinanceEdgeVariant(BtcUpDown5mBinanceEdge6, BtcUpDown5mBinanceEdge6Code, 6));
        variants.Add(CreateBtcUpDown5mBinanceDelayedVariant(BtcUpDown5mBinanceDelayed15, BtcUpDown5mBinanceDelayed15Code, 15));
        variants.Add(CreateBtcUpDown5mBinanceDelayedVariant(BtcUpDown5mBinanceDelayed30, BtcUpDown5mBinanceDelayed30Code, 30));
        variants.Add(CreateBtcUpDown5mBinanceDelayedVariant(BtcUpDown5mBinanceDelayed45, BtcUpDown5mBinanceDelayed45Code, 45));
        variants.Add(CreateBtcUpDown5mEnsembleVariant());
        variants.Add(CreateBtcUpDown5mDynamicMarkovVariant());
        variants.Add(CreateBtcUpDown5mStrategySelectorVariant());

        return variants;
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mVariant(
        BtcUpDown5mStrategyDirection direction,
        int entryDelaySeconds,
        int idGroup,
        BtcUpDown5mStrategyBehavior behavior)
    {
        var directionCode = direction == BtcUpDown5mStrategyDirection.Less ? "less" : "more";
        var directionName = direction == BtcUpDown5mStrategyDirection.Less ? "Less" : "More";
        var directionDescription = direction == BtcUpDown5mStrategyDirection.Less ? "lower-priced" : "higher-priced";
        var gammaSuffix = behavior == BtcUpDown5mStrategyBehavior.GammaOutcomeSelection ? "_gamma" : string.Empty;
        var gammaNameSuffix = behavior == BtcUpDown5mStrategyBehavior.GammaOutcomeSelection ? " Gamma" : string.Empty;
        var description = behavior == BtcUpDown5mStrategyBehavior.GammaOutcomeSelection
            ? $"Experimental comparison strategy: choose the {directionDescription} BTC 5m outcome from Gamma outcomePrices {entryDelaySeconds} seconds after window start, then use taker Paper pricing for the selected asset."
            : $"Bet $1 on the {directionDescription} BTC 5m outcome {entryDelaySeconds} seconds after window start.";

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-800{idGroup}-000000000{entryDelaySeconds:000}"),
            $"btc_up_down_5m_{directionCode}_{entryDelaySeconds}{gammaSuffix}",
            $"BTC Up or Down 5m {directionName} {entryDelaySeconds}{gammaNameSuffix}",
            description,
            direction,
            entryDelaySeconds,
            behavior);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mMiddleVariant(int depth)
    {
        var cachedSamples = depth - 1;
        var sampleDescription = cachedSamples == 0
            ? "the latest Binance BTC/USDT trade-stream price"
            : $"the latest Binance BTC/USDT trade-stream price plus the latest {cachedSamples} cached reference sample(s)";
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-8006-000000000{depth:000}"),
            $"btc_up_down_5m_middle_{depth}",
            $"BTC Up or Down 5m Middle {depth}",
            $"Immediately after BTC 5m market open, compare {sampleDescription} against the cached arithmetic mean; above mean buys Down, below mean buys Up, otherwise skip. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReference,
            depth);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mMiddleRevertVariant(int depth)
    {
        var cachedSamples = depth - 1;
        var sampleDescription = cachedSamples == 0
            ? "the latest Binance BTC/USDT trade-stream price"
            : $"the latest Binance BTC/USDT trade-stream price plus the latest {cachedSamples} cached reference sample(s)";
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-8009-000000000{depth:000}"),
            $"btc_up_down_5m_middle_{depth}_revert",
            $"BTC Up or Down 5m Middle {depth} Revert",
            $"Immediately after BTC 5m market open, compare {sampleDescription} against the cached arithmetic mean, then invert the standard Middle {depth} decision; above mean buys Up, below mean buys Down, otherwise skip. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevert,
            depth);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mSkipVariant(int depth)
    {
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-8007-000000000{depth:000}"),
            $"btc_up_down_5m_skip_{depth}",
            $"BTC Up or Down 5m Skip {depth}",
            $"Immediately after BTC 5m market open, inspect the latest {depth} settled BTC 5m market result(s); after consecutive Up results buy Down, after consecutive Down results buy Up, otherwise skip. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults,
            depth);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mSkipRevertVariant(int depth)
    {
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-8008-000000000{depth:000}"),
            $"btc_up_down_5m_skip_{depth}_revert",
            $"BTC Up or Down 5m Skip {depth} Revert",
            $"Immediately after BTC 5m market open, inspect the latest {depth} settled BTC 5m market result(s), then invert the standard Skip {depth} decision; after consecutive Up results buy Up, after consecutive Down results buy Down, otherwise skip. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert,
            depth);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mAlwaysDirectionVariant(bool isUp)
    {
        return new BtcUpDown5mStrategyVariant(
            isUp ? BtcUpDown5mAlwaysUp : BtcUpDown5mAlwaysDown,
            isUp ? BtcUpDown5mAlwaysUpCode : BtcUpDown5mAlwaysDownCode,
            isUp ? "BTC Up or Down 5m Up" : "BTC Up or Down 5m Down",
            isUp
                ? "After BTC 5m trading starts, always place an Up GTD limit BUY at 0.45 for two minutes; settlement uses only actually filled shares."
                : "After BTC 5m trading starts, always place a Down GTD limit BUY at 0.45 for two minutes; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            isUp ? BtcUpDown5mStrategyBehavior.AlwaysUp : BtcUpDown5mStrategyBehavior.AlwaysDown);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mBinanceVariant()
    {
        return new BtcUpDown5mStrategyVariant(
            BtcUpDown5mBinance,
            BtcUpDown5mBinanceCode,
            "BTC Up or Down 5m Binance",
            "After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; above start buys Up, below start buys Down, equal skips. Paper entry is a GTD limit BUY capped at 0.50 for two minutes; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.BinanceStartRelative);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mBinanceFixedPriceVariant(
        Guid id,
        string code,
        int limitPriceCents)
    {
        var limitPrice = limitPriceCents / 100m;
        return new BtcUpDown5mStrategyVariant(
            id,
            code,
            $"BTC Up or Down 5m Binance {limitPriceCents}",
            $"After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; above start buys Up, below start buys Down, equal skips. Paper entry is a GTD limit BUY at fixed {limitPrice.ToString("0.00", CultureInfo.InvariantCulture)} for two minutes; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice,
            limitPriceCents);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mBinanceBpsThresholdVariant(
        Guid id,
        string code,
        decimal minMoveBps)
    {
        var thresholdName = minMoveBps.ToString("0.###", CultureInfo.InvariantCulture);
        return new BtcUpDown5mStrategyVariant(
            id,
            code,
            $"BTC Up or Down 5m Binance {thresholdName} bps",
            $"After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least {thresholdName} bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 for two minutes; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold,
            minMoveBps >= 1m && minMoveBps == decimal.Truncate(minMoveBps)
                ? (int)minMoveBps
                : 0,
            minMoveBps);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mBinanceCleverVariant()
    {
        return new BtcUpDown5mStrategyVariant(
            BtcUpDown5mBinanceClever,
            BtcUpDown5mBinanceCleverCode,
            "BTC Up or Down 5m Binance Clever",
            "After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference, estimate a fair target outcome price from recent odds archive samples with similar BTC move/time-to-close/book quality, and place a two-minute GTD limit BUY only below fair value with a safety margin.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeClever);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mBinanceCleverMarginVariant(
        Guid id,
        string code,
        string marginName,
        int marginCents)
    {
        var margin = marginCents / 100m;
        return new BtcUpDown5mStrategyVariant(
            id,
            code,
            $"BTC Up or Down 5m Binance Clever {marginName}",
            $"After BTC 5m trading starts, compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference, estimate a fair target outcome price from recent odds archive samples with similar BTC move/time-to-close/book quality, and place a two-minute GTD limit BUY only below fair value with a {margin.ToString("0.00", CultureInfo.InvariantCulture)} safety margin.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeCleverMargin,
            marginCents);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mBinanceEdgeVariant(
        Guid id,
        string code,
        int edgeCents)
    {
        var margin = edgeCents / 100m;
        return new BtcUpDown5mStrategyVariant(
            id,
            code,
            $"BTC Up or Down 5m Binance Edge {edgeCents}",
            $"After BTC 5m trading starts, use the Binance start-relative direction, estimate fair value from the BTC odds archive, and place a two-minute GTD limit BUY only when the safe price is at least {margin.ToString("0.00", CultureInfo.InvariantCulture)} below fair value.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeEdge,
            edgeCents);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mBinanceDelayedVariant(
        Guid id,
        string code,
        int entryDelaySeconds)
    {
        return new BtcUpDown5mStrategyVariant(
            id,
            code,
            $"BTC Up or Down 5m Binance {entryDelaySeconds}s",
            $"Wait {entryDelaySeconds} seconds after BTC 5m trading starts, then compare the latest Binance BTC/USDT trade-stream price with the archived market-start reference; above start buys Up, below start buys Down, equal skips. Paper entry is a GTD limit BUY capped at 0.50 for two minutes.",
            BtcUpDown5mStrategyDirection.Dynamic,
            entryDelaySeconds,
            BtcUpDown5mStrategyBehavior.BinanceStartRelativeDelayed,
            entryDelaySeconds);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mEnsembleVariant()
    {
        return new BtcUpDown5mStrategyVariant(
            BtcUpDown5mEnsemble2Of3,
            BtcUpDown5mEnsemble2Of3Code,
            "BTC Up or Down 5m Ensemble 2 of 3",
            "Immediately after BTC 5m market open, vote between Binance start-relative, Middle 1, and Skip 1 signals. Enter only when at least two available votes select the same outcome. Paper entry is a GTD limit BUY with dynamic break-even pricing.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.EnsembleVote,
            2);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mDynamicMarkovVariant()
    {
        return new BtcUpDown5mStrategyVariant(
            BtcUpDown5mDynamicMarkov,
            BtcUpDown5mDynamicMarkovCode,
            "BTC Up or Down 5m Dynamic Markov",
            "Immediately after BTC 5m market open, estimate the next result from recent BTC 5m result transitions and enter only when the transition edge is strong enough. Paper entry is a GTD limit BUY with dynamic break-even pricing.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.DynamicMarkov,
            50);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mStrategySelectorVariant()
    {
        return new BtcUpDown5mStrategyVariant(
            BtcUpDown5mStrategySelector,
            BtcUpDown5mStrategySelectorCode,
            "BTC Up or Down 5m Strategy Selector",
            "Immediately after BTC 5m market open, choose the best positive-expectancy opening BTC strategy from recent settled Paper history, then reuse that strategy's current direction signal for one GTD limit BUY.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.StrategySelector,
            30);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mEntryPriceCapVariant(
        Guid id,
        string code,
        BtcUpDown5mStrategyDirection direction,
        int entryDelaySeconds,
        int maxEntryPriceCents)
    {
        var maxEntryPrice = maxEntryPriceCents / 100m;
        var directionName = direction == BtcUpDown5mStrategyDirection.Less ? "Less" : "More";
        var directionDescription = direction == BtcUpDown5mStrategyDirection.Less
            ? "lower-priced"
            : "higher-priced";
        return new BtcUpDown5mStrategyVariant(
            id,
            code,
            $"BTC Up or Down 5m {directionName} {entryDelaySeconds} Below {maxEntryPriceCents}",
            $"Bet the configured Paper stake multiplier on the {directionDescription} BTC 5m outcome {entryDelaySeconds} seconds after window start using a two-minute GTD limit BUY at {maxEntryPrice.ToString("0.00", CultureInfo.InvariantCulture)}.",
            direction,
            entryDelaySeconds,
            BtcUpDown5mStrategyBehavior.StandardEntryPriceCap,
            maxEntryPriceCents);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mGammaEntryPriceCapVariant(
        Guid id,
        string code,
        BtcUpDown5mStrategyDirection direction,
        int entryDelaySeconds,
        int maxEntryPriceCents)
    {
        var maxEntryPrice = maxEntryPriceCents / 100m;
        var directionName = direction == BtcUpDown5mStrategyDirection.Less ? "Less" : "More";
        var directionDescription = direction == BtcUpDown5mStrategyDirection.Less
            ? "lower-priced"
            : "higher-priced";
        return new BtcUpDown5mStrategyVariant(
            id,
            code,
            $"BTC Up or Down 5m {directionName} {entryDelaySeconds} Gamma Below {maxEntryPriceCents}",
            $"Experimental Paper-only comparison strategy: choose the {directionDescription} BTC 5m outcome from Gamma outcomePrices {entryDelaySeconds} seconds after window start, then place a two-minute GTD limit BUY at {maxEntryPrice.ToString("0.00", CultureInfo.InvariantCulture)}.",
            direction,
            entryDelaySeconds,
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelectionEntryPriceCap,
            maxEntryPriceCents);
    }
}

public enum BtcUpDown5mStrategyDirection
{
    Less,
    More,
    Dynamic
}

public enum BtcUpDown5mStrategyBehavior
{
    Standard,
    GammaOutcomeSelection,
    Less180Martin,
    MiddleReference,
    MiddleReferenceRevert,
    SkipConsecutiveMarketResults,
    SkipConsecutiveMarketResultsRevert,
    AlwaysUp,
    AlwaysDown,
    BinanceStartRelative,
    BinanceStartRelativeFixedPrice,
    BinanceStartRelativeBpsThreshold,
    BinanceStartRelativeClever,
    BinanceStartRelativeCleverMargin,
    BinanceStartRelativeEdge,
    BinanceStartRelativeDelayed,
    EnsembleVote,
    DynamicMarkov,
    StrategySelector,
    StandardEntryPriceCap,
    GammaOutcomeSelectionEntryPriceCap
}

public sealed record BtcUpDown5mStrategyVariant(
    Guid Id,
    string Code,
    string Name,
    string Description,
    BtcUpDown5mStrategyDirection Direction,
    int EntryDelaySeconds,
    BtcUpDown5mStrategyBehavior Behavior = BtcUpDown5mStrategyBehavior.Standard,
    int DecisionDepth = 0,
    decimal? DecisionThresholdBps = null)
{
    public string CopiedTraderWallet => "strategy:" + Code;
}

public sealed record TradingStrategy(
    Guid Id,
    string Code,
    string Name,
    string Description,
    bool Enabled,
    bool LiveStakes,
    decimal PaperStakeAmount,
    decimal LiveStakeAmount,
    decimal LiveAvailableBalance,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record StrategyRuntimeSettings(
    Guid StrategyId,
    bool Enabled,
    bool LiveStakes,
    decimal PaperStakeAmount,
    decimal LiveStakeAmount,
    decimal LiveAvailableBalance)
{
    public static StrategyRuntimeSettings Default(Guid strategyId)
    {
        return new StrategyRuntimeSettings(
            StrategyIds.Normalize(strategyId),
            Enabled: true,
            LiveStakes: false,
            PaperStakeAmount: 1.00m,
            LiveStakeAmount: 1.00m,
            LiveAvailableBalance: 100.00m);
    }
}

public sealed record StrategyLiveBalanceAdjustmentResult(
    bool Applied,
    decimal AvailableBalance,
    bool LiveStakesDisabled);

public sealed record StrategyPerformance(
    Guid StrategyId,
    string Code,
    string Name,
    bool Enabled,
    bool LiveStakes,
    decimal PaperStakeAmount,
    decimal LiveStakeAmount,
    decimal LiveAvailableBalance,
    int OrdersCount,
    int FilledOrdersCount,
    int OpenOrdersCount,
    int OpenPositionsCount,
    int ObservedRunsCount,
    int EnteredRunsCount,
    int SkippedRunsCount,
    int SettledRunsCount,
    int SettledPositionsCount,
    int WonPositionsCount,
    int LostPositionsCount,
    decimal StakeUsd,
    decimal RealizedPnlUsd,
    decimal UnrealizedPnlUsd,
    decimal TotalPnlUsd,
    decimal WinRatePct,
    decimal LossRatePct,
    decimal AvgWinPnlUsd,
    decimal AvgLossPnlUsd,
    decimal? ProfitFactor,
    decimal ExpectancyPnlUsd,
    decimal RoiPct,
    decimal ClosedRoiPct,
    decimal AvgEntryDelaySeconds,
    decimal MaxEntryDelaySeconds,
    int LiveOrdersCount,
    int LiveFilledOrdersCount,
    int LiveOpenOrdersCount,
    int LiveSettledOrdersCount,
    int LiveWonOrdersCount,
    int LiveLostOrdersCount,
    decimal LiveStakeUsd,
    decimal LiveRealizedPnlUsd,
    decimal LiveWinRatePct,
    decimal LiveLossRatePct,
    decimal LiveAvgWinPnlUsd,
    decimal LiveAvgLossPnlUsd,
    decimal? LiveProfitFactor,
    decimal LiveExpectancyPnlUsd,
    decimal LiveRoiPct,
    DateTimeOffset? LiveLastOrderUtc,
    DateTimeOffset? LiveLastSettlementUtc,
    DateTimeOffset? LastOrderUtc,
    DateTimeOffset? LastRunUtc);

public sealed record StrategyRecentPerformance(
    Guid StrategyId,
    string Code,
    string Name,
    string Window,
    int WindowHours,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int OrdersCount,
    int FilledOrdersCount,
    int ExpiredOrdersCount,
    int OpenOrdersCount,
    int EnteredRunsCount,
    int SkippedRunsCount,
    int SettledRunsCount,
    int WonRunsCount,
    int LostRunsCount,
    decimal FilledCostUsd,
    decimal RealizedPnlUsd,
    decimal AvgFillPrice,
    decimal AvgEntryDelaySeconds,
    decimal MaxEntryDelaySeconds,
    decimal WinRatePct,
    decimal RoiPct,
    string TopSkipReason,
    DateTimeOffset? LastOrderUtc,
    DateTimeOffset? LastRunUtc);

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

public sealed record DryRunOrder(
    Guid Id,
    Guid SignalId,
    DryRunOrderStatus Status,
    TradeSide Side,
    string AssetId,
    string ConditionId,
    string Outcome,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    string OrderType,
    string PayloadJson,
    string ValidationSummary,
    DateTimeOffset CreatedAtUtc,
    Guid StrategyId = default);

public sealed record LiveOrder(
    Guid Id,
    Guid SignalId,
    LiveOrderStatus Status,
    string? OrderId,
    TradeSide Side,
    string AssetId,
    string ConditionId,
    string Outcome,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    string OrderType,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    string ResponseStatus,
    decimal FilledSize,
    decimal RemainingSize,
    string CancelStatus,
    string RawResponseJson,
    string ValidationSummary,
    DateTimeOffset UpdatedAtUtc,
    Guid StrategyId = default,
    bool BalanceEffectApplied = false,
    decimal? SettlementValueUsd = null,
    decimal? RealizedPnlUsd = null,
    DateTimeOffset? SettledAtUtc = null,
    string? WinningAssetId = null,
    string? WinningOutcome = null,
    decimal? AverageFillPrice = null,
    decimal FilledNotionalUsd = 0m,
    decimal CostBasisUsd = 0m,
    decimal FeeUsd = 0m,
    bool? Won = null,
    string SettlementSource = "",
    Guid? CorrelationId = null,
    string ExecutionSource = "",
    bool? PostOnly = null,
    Guid? PaperOrderId = null);

public sealed record PaperLiveShadowDecision(
    Guid CorrelationId,
    Guid StrategyId,
    string MarketId,
    string ConditionId,
    string AssetId,
    string Outcome,
    TradeSide Side,
    decimal LimitPrice,
    decimal TargetNotionalUsd,
    decimal RequestedSizeShares,
    decimal MaxReservedNotionalUsd,
    string OrderType,
    bool PostOnly,
    string OrderBookSnapshotJson,
    int? QuoteAgeMs,
    string Source,
    DateTimeOffset QuoteReceivedAtUtc,
    DateTimeOffset DecisionCreatedAtUtc,
    DateTimeOffset? MarketStartUtc,
    DateTimeOffset? MarketCloseUtc,
    DateTimeOffset SubmitDeadlineUtc,
    DateTimeOffset CancelDeadlineUtc,
    Guid? SignalId = null,
    Guid? PaperOrderId = null,
    Guid? LiveOrderId = null,
    string Status = "created",
    DateTimeOffset? UpdatedAtUtc = null);

public sealed record PaperLiveShadowDiscrepancy(
    Guid Id,
    Guid CorrelationId,
    Guid StrategyId,
    string Classification,
    string Severity,
    string Details,
    string RawJson,
    DateTimeOffset CreatedAtUtc);

public sealed record LiveTradingEvent(
    Guid Id,
    string Action,
    string Status,
    string Details,
    DateTimeOffset CreatedAtUtc);

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
    string Evidence,
    decimal RealizedPnlUsd = 0m);

public sealed record PaperPositionSettlement(
    Guid Id,
    string CopiedTraderWallet,
    string AssetId,
    string ConditionId,
    string Outcome,
    string? WinningAssetId,
    string WinningOutcome,
    string? Category,
    decimal SettledSizeShares,
    decimal AveragePrice,
    decimal CostBasisUsd,
    decimal SettlementValueUsd,
    decimal RealizedPnlUsd,
    bool Won,
    string SettlementSource,
    DateTimeOffset SettledAtUtc,
    DateTimeOffset CreatedAtUtc);

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

public static class StrategyMarketPaperRunStatuses
{
    public const string Observed = "Observed";
    public const string Entered = "Entered";
    public const string Skipped = "Skipped";
    public const string Settled = "Settled";
}

public sealed record StrategyMarketPaperRun(
    Guid Id,
    Guid StrategyId,
    string MarketId,
    string ConditionId,
    string MarketSlug,
    string MarketTitle,
    string? Category,
    DateTimeOffset? MarketStartUtc,
    DateTimeOffset? MarketEndUtc,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset EntryDueAtUtc,
    string Status,
    string? SelectedAssetId,
    string? SelectedOutcome,
    decimal? EntryPrice,
    decimal StakeUsd,
    decimal? SizeShares,
    Guid? SignalId,
    Guid? PaperOrderId,
    DateTimeOffset? EnteredAtUtc,
    decimal? SettlementPrice,
    decimal? SettlementValueUsd,
    decimal? RealizedPnlUsd,
    DateTimeOffset? SettledAtUtc,
    string? SkipReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? SkipDiagnosticsJson = null);

public enum PaperCopiedLeaderPositionStatus
{
    PendingEntry,
    Active,
    Closed
}

public sealed record PaperCopiedLeaderPosition(
    Guid Id,
    Guid EntrySignalId,
    Guid EntryPaperOrderId,
    string CopiedTraderWallet,
    string AssetId,
    string ConditionId,
    string Outcome,
    string? EntryTransactionHash,
    DateTimeOffset EntryTimestampUtc,
    decimal LeaderEntryPrice,
    decimal LeaderInitialSizeShares,
    decimal CopiedInitialSizeShares,
    decimal LeaderSoldSizeShares,
    decimal CopiedExitRequestedSizeShares,
    PaperCopiedLeaderPositionStatus Status,
    DateTimeOffset? LastActivityTimestampUtc,
    string? LastActivityTransactionHash,
    DateTimeOffset? LastActivitySyncAtUtc,
    DateTimeOffset NextActivitySyncAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PaperCopiedLeaderActivityEvent(
    Guid Id,
    string DedupKey,
    string CopiedTraderWallet,
    string AssetId,
    string ConditionId,
    TradeSide Side,
    decimal Price,
    decimal SizeShares,
    decimal UsdcSize,
    string? TransactionHash,
    DateTimeOffset ActivityTimestampUtc,
    string RawJson,
    DateTimeOffset ObservedAtUtc);

public sealed record PaperCopiedLeaderPositionExitUpdate(
    Guid PositionId,
    decimal LeaderSoldSizeShares,
    decimal CopiedExitRequestedSizeShares,
    PaperCopiedLeaderPositionStatus Status,
    DateTimeOffset LastActivityTimestampUtc,
    string? LastActivityTransactionHash,
    DateTimeOffset UpdatedAtUtc);

public sealed record RiskEvent(
    Guid Id,
    string ReasonCode,
    string Details,
    DateTimeOffset CreatedAtUtc);

public sealed record MarketDataEvent(
    Guid Id,
    MarketDataEventType EventType,
    string? AssetId,
    string? ConditionId,
    string Message,
    DateTimeOffset ReceivedAtUtc);

public sealed record MarketDataStatusSnapshot(
    string Component,
    MarketDataConnectionState ConnectionState,
    string Endpoint,
    int SubscribedAssetsCount,
    DateTimeOffset? LastMessageUtc,
    DateTimeOffset? LastConnectedUtc,
    DateTimeOffset? LastDisconnectedUtc,
    int ReconnectCount,
    bool Stale,
    string? LastError,
    DateTimeOffset UpdatedAtUtc);

public sealed record PinnedMarketAsset(
    string AssetId,
    string? Note,
    DateTimeOffset CreatedAtUtc);

public sealed record DailyReport(
    DateOnly ReportDate,
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
    DateTimeOffset GeneratedAtUtc);

public sealed record TraderPerformanceReport(
    string TraderWallet,
    int Signals,
    decimal AcceptanceRatePct,
    decimal FillRatePct,
    decimal? AverageLagSeconds,
    decimal? AverageLeaderPrice,
    decimal? AverageProposedPrice,
    decimal? AveragePriceDifference,
    decimal PaperPnl,
    string PaperPnlByCategory,
    string RejectionReasons);

public sealed record CategoryPerformanceReport(
    string Category,
    int Signals,
    int Accepted,
    int Filled,
    decimal PaperPnl,
    decimal? AverageSpread,
    decimal? AverageLagSeconds);

public sealed record ExecutionQualityReport(
    Guid SignalId,
    string TraderWallet,
    string AssetId,
    string ConditionId,
    DateTimeOffset CreatedAtUtc,
    decimal LeaderPrice,
    decimal? ProposedPrice,
    decimal? PaperFillPrice,
    decimal? ProposedMinusLeader,
    decimal? FillMinusProposed,
    int? LagSeconds,
    decimal? SpreadAtSignal,
    decimal? BidAfter1m,
    decimal? AskAfter1m,
    decimal? MidAfter1m,
    decimal? BidAfter5m,
    decimal? AskAfter5m,
    decimal? MidAfter5m,
    decimal? BidAfter30m,
    decimal? AskAfter30m,
    decimal? MidAfter30m);

public sealed record RejectionAnalysisReport(
    string ReasonCode,
    int Count,
    decimal RejectedPct,
    DateTimeOffset? LastRejectedAtUtc);

public sealed record ServiceCommandAudit(
    Guid Id,
    string Command,
    string Source,
    bool Accepted,
    string Message,
    DateTimeOffset CreatedAtUtc);

public sealed record ApiError(
    Guid Id,
    string Component,
    string Operation,
    string Message,
    DateTimeOffset CreatedAtUtc);

public sealed record PolymarketHttpLogEntry(
    Guid Id,
    string Component,
    string Operation,
    string HttpMethod,
    string RequestUrl,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ResponseAtUtc,
    long DurationMilliseconds,
    int Attempt,
    int? StatusCode,
    bool Succeeded,
    string ResponseBody,
    string? ErrorMessage);

public sealed record PolymarketHttpLogCleanupResult(
    int DeletedRows,
    int DeletedSuccessfulRows,
    int DeletedFailedRows);

public sealed record PolymarketOnChainLog(
    Guid Id,
    string ContractName,
    string ContractAddress,
    string ExchangeVersion,
    long BlockNumber,
    string BlockHash,
    string TransactionHash,
    long TransactionIndex,
    long LogIndex,
    string Topic0,
    IReadOnlyList<string> Topics,
    string Data,
    bool Removed,
    DateTimeOffset ObservedAtUtc);

public sealed record PolymarketOnChainFill(
    Guid Id,
    string ContractName,
    string ContractAddress,
    string ExchangeVersion,
    long BlockNumber,
    DateTimeOffset BlockTimestampUtc,
    string TransactionHash,
    long LogIndex,
    string OrderHash,
    string Maker,
    string Taker,
    string Wallet,
    TradeSide Side,
    string TokenId,
    string MakerAssetId,
    string TakerAssetId,
    string MakerAmountRaw,
    string TakerAmountRaw,
    decimal MakerAmount,
    decimal TakerAmount,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    string FeeRaw,
    decimal FeeAmount,
    string FeeAssetId,
    string? Builder,
    string? Metadata,
    DateTimeOffset ImportedAtUtc);

public sealed record PolymarketOnChainTradeCapture(
    Guid Id,
    string ContractName,
    string ContractAddress,
    string ExchangeVersion,
    long BlockNumber,
    DateTimeOffset BlockTimestampUtc,
    string BlockHash,
    string TransactionHash,
    long TransactionIndex,
    long LogIndex,
    string OrderHash,
    string Maker,
    string Taker,
    string Wallet,
    TradeSide Side,
    string TokenId,
    string MakerAssetId,
    string TakerAssetId,
    string MakerAmountRaw,
    string TakerAmountRaw,
    decimal MakerAmount,
    decimal TakerAmount,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    string FeeRaw,
    decimal FeeAmount,
    string FeeAssetId,
    string? Builder,
    string? Metadata,
    IReadOnlyList<string> RawTopics,
    string RawData,
    bool Removed,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset ImportedAtUtc);

public sealed record PolymarketOnChainWalletFill(
    Guid SourceFillId,
    string ContractName,
    string ContractAddress,
    string ExchangeVersion,
    long BlockNumber,
    DateTimeOffset BlockTimestampUtc,
    string TransactionHash,
    long LogIndex,
    string OrderHash,
    OnChainParticipantRole Role,
    string Wallet,
    string Counterparty,
    TradeSide Side,
    string TokenId,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    decimal FeeAmount,
    string FeeAssetId,
    DateTimeOffset ImportedAtUtc);

public sealed record PolymarketOnChainWalletExecution(
    string ContractName,
    string ContractAddress,
    string ExchangeVersion,
    long BlockNumber,
    DateTimeOffset BlockTimestampUtc,
    string TransactionHash,
    long FirstLogIndex,
    long LastLogIndex,
    string Wallet,
    TradeSide Side,
    string TokenId,
    int FillCount,
    int MakerFillCount,
    int TakerFillCount,
    decimal SizeShares,
    decimal NotionalUsd,
    decimal AveragePrice,
    decimal FeesUsd,
    DateTimeOffset ImportedAtUtc);

public sealed record PolymarketOnChainTokenMetadata(
    string TokenId,
    string ConditionId,
    string MarketId,
    string MarketSlug,
    string MarketTitle,
    string Outcome,
    int OutcomeIndex,
    string? Category,
    DateTimeOffset? EndDateUtc,
    bool Active,
    bool Closed,
    bool Archived,
    bool Resolved,
    string? WinningOutcome,
    IReadOnlyList<string> ClobTokenIds,
    IReadOnlyList<string> Outcomes,
    bool LookupSucceeded,
    string? LookupError,
    string RawJson,
    DateTimeOffset LastRefreshedUtc);

public sealed record PolymarketClobMarketByToken(
    string ConditionId,
    string PrimaryTokenId,
    string SecondaryTokenId);

public sealed record OnChainIngestionCursor(
    string ContractAddress,
    string ContractName,
    string ExchangeVersion,
    long FromBlock,
    long ToBlock,
    int LogsFetched,
    int FillsStored,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record OnChainTradeCaptureCursor(
    string ContractAddress,
    string ContractName,
    string ExchangeVersion,
    long NextBlock,
    long LastScannedBlock,
    long LastTargetBlock,
    int LogsFetched,
    int CapturesStored,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OnChainBlockRange(
    long FromBlock,
    long ToBlock);

public sealed record TraderOnChainStats(
    string Wallet,
    int Fills,
    int BuyFills,
    int SellFills,
    int MarketsTraded,
    decimal VolumeUsd,
    decimal AverageTradeUsd,
    decimal FeesUsd,
    decimal ActivityScore,
    DateTimeOffset FirstTradeUtc,
    DateTimeOffset LastTradeUtc);

public sealed record OnChainActivityRefreshResult(
    int WalletsQueued,
    int WalletsProcessed,
    int WalletsUpserted,
    int QueueRemaining);

public sealed record PolymarketOnChainWalletPosition(
    string Wallet,
    string TokenId,
    string ConditionId,
    string MarketId,
    string MarketSlug,
    string MarketTitle,
    string Outcome,
    string? Category,
    bool LookupSucceeded,
    bool MarketResolved,
    string? WinningOutcome,
    int Executions,
    int BuyExecutions,
    int SellExecutions,
    decimal BuyShares,
    decimal SellShares,
    decimal NetShares,
    decimal BuyNotionalUsd,
    decimal SellNotionalUsd,
    decimal NetCostUsd,
    decimal FeesUsd,
    decimal AverageBuyPrice,
    decimal AverageSellPrice,
    decimal VolumeUsd,
    decimal? ResolvedPnlUsd,
    string PositionStatus,
    DateTimeOffset FirstTradeUtc,
    DateTimeOffset LastTradeUtc);

public sealed record OnChainPositionRefreshResult(
    int TokensQueued,
    int TokensProcessed,
    int PositionsUpserted,
    int QueueRemaining);

public sealed record PolymarketOnChainWalletPerformance(
    string Wallet,
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

public sealed record PolymarketOnChainTradeDetails(
    string ContractName,
    string ContractAddress,
    string ExchangeVersion,
    long BlockNumber,
    DateTimeOffset BlockTimestampUtc,
    string TransactionHash,
    long LogIndex,
    string OrderHash,
    string Maker,
    string Taker,
    TradeSide MakerSide,
    TradeSide TakerSide,
    string TokenId,
    string MakerAssetId,
    string TakerAssetId,
    string MakerAmountRaw,
    string TakerAmountRaw,
    decimal MakerAmount,
    decimal TakerAmount,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    decimal FeeAmount,
    string FeeAssetId,
    string? Builder,
    string? OrderMetadata,
    string ConditionId,
    string MarketId,
    string MarketSlug,
    string MarketTitle,
    string Outcome,
    string? Category,
    bool LookupSucceeded,
    bool MarketActive,
    bool MarketClosed,
    bool MarketArchived,
    bool MarketResolved,
    string? WinningOutcome,
    DateTimeOffset ImportedAtUtc);

public sealed record PolymarketOnChainParticipantDetails(
    string Wallet,
    int Executions,
    int BuyExecutions,
    int SellExecutions,
    int MarketsTraded,
    decimal VolumeUsd,
    decimal AverageTradeUsd,
    decimal FeesUsd,
    decimal ActivityScore,
    int PositionsCount,
    int OpenPositions,
    int FlatPositions,
    int ResolvedPositions,
    int ProfitableResolvedPositions,
    int LosingResolvedPositions,
    decimal OpenExposureUsd,
    decimal ResolvedCostUsd,
    decimal ResolvedPnlUsd,
    decimal ResolvedRoiPct,
    decimal WinRatePct,
    decimal AveragePositionSizeUsd,
    decimal Score,
    string SampleQuality,
    DateTimeOffset FirstTradeUtc,
    DateTimeOffset LastTradeUtc,
    DateTimeOffset ActivityRefreshedAtUtc,
    DateTimeOffset? PerformanceRefreshedAtUtc);

public sealed record OnChainPerformanceRefreshResult(
    int WalletsQueued,
    int WalletsProcessed,
    int WalletsUpserted,
    int QueueRemaining);

public sealed record OnChainCategoryPerformanceRefreshResult(
    int PairsQueued,
    int PairsProcessed,
    int PairsUpserted,
    int QueueRemaining);

public sealed record PolymarketOnChainSignalCandidateSource(
    Guid SourceFillId,
    string ContractName,
    string ContractAddress,
    string ExchangeVersion,
    long BlockNumber,
    DateTimeOffset BlockTimestampUtc,
    string TransactionHash,
    long LogIndex,
    string OrderHash,
    OnChainParticipantRole ParticipantRole,
    string Wallet,
    string Counterparty,
    TradeSide Side,
    string TokenId,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    decimal FeeAmount,
    string FeeAssetId,
    DateTimeOffset ImportedAtUtc,
    PolymarketOnChainTokenMetadata? TokenMetadata,
    PolymarketOnChainWalletCategoryPerformance? WalletCategoryPerformance);

public sealed record PolymarketOnChainSignalCandidate(
    Guid Id,
    Guid SourceFillId,
    string ContractName,
    string ContractAddress,
    string ExchangeVersion,
    long BlockNumber,
    DateTimeOffset BlockTimestampUtc,
    string TransactionHash,
    long LogIndex,
    string OrderHash,
    OnChainParticipantRole ParticipantRole,
    string Wallet,
    string Counterparty,
    TradeSide Side,
    string TokenId,
    string ConditionId,
    string MarketId,
    string MarketSlug,
    string MarketTitle,
    string Outcome,
    string? Category,
    bool LookupSucceeded,
    bool MarketActive,
    bool MarketClosed,
    bool MarketArchived,
    bool MarketResolved,
    string? WinningOutcome,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    decimal FeeAmount,
    string FeeAssetId,
    int? LeaderPositionsCount,
    int? LeaderResolvedPositions,
    int? LeaderMarketsTraded,
    decimal? LeaderVolumeUsd,
    decimal? LeaderResolvedPnlUsd,
    decimal? LeaderResolvedRoiPct,
    decimal? LeaderWinRatePct,
    decimal? LeaderCategoryScore,
    string? LeaderSampleQuality,
    DateTimeOffset? LeaderPerformanceRefreshedAtUtc,
    string DecisionStatus,
    string DecisionCode,
    decimal CandidateScore,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PolymarketOnChainSignalCandidateReason(
    Guid Id,
    Guid CandidateId,
    string ReasonCode,
    string ReasonDetails,
    DateTimeOffset CreatedAtUtc);

public sealed record PolymarketOnChainSignalCandidateDecision(
    PolymarketOnChainSignalCandidate Candidate,
    IReadOnlyList<PolymarketOnChainSignalCandidateReason> Reasons);

public sealed record OnChainSignalCandidateRefreshResult(
    int SourcesQueued,
    int RetriesQueued,
    int SourcesFetched,
    int CandidatesUpserted,
    int Accepted,
    int Rejected,
    int QueueRemaining);

public sealed record OnChainSignalCandidateQueueRefreshResult(
    int SourcesQueued,
    int RetriesQueued,
    int QueueRemaining);

public sealed record OnChainIngestionResult(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    long FromBlock,
    long ToBlock,
    int ContractsScanned,
    int LogsFetched,
    int FillsStored);

public sealed record OnChainTradeCaptureResult(
    long LatestBlock,
    long TargetBlock,
    int ContractsScanned,
    int RangesScanned,
    int LogsFetched,
    int CapturesStored,
    int HotCandidatesProcessed = 0,
    int HotPaperOrdersCreated = 0);

public sealed record OnChainPaperSignalCandidate(
    Guid CaptureId,
    string ContractName,
    string ContractAddress,
    string ExchangeVersion,
    long BlockNumber,
    DateTimeOffset BlockTimestampUtc,
    string TransactionHash,
    long LogIndex,
    string OrderHash,
    OnChainParticipantRole ParticipantRole,
    string Wallet,
    string CounterpartyWallet,
    TradeSide Side,
    string TokenId,
    decimal Price,
    decimal SizeShares,
    decimal NotionalUsd,
    string ConditionId,
    string MarketId,
    string MarketSlug,
    string MarketTitle,
    string Outcome,
    string? LocalCategory,
    bool MarketFound,
    bool MarketActive,
    bool MarketClosed,
    bool MarketArchived,
    bool MarketRestricted,
    bool MarketAcceptingOrders,
    bool MarketEnableOrderBook,
    DateTimeOffset? MarketEndDateUtc,
    string? PolymarketCategory,
    bool? RatingFound,
    int? LeaderboardRank,
    string? RatingUserName,
    decimal? LeaderboardPnlUsd,
    decimal? LeaderboardVolumeUsd,
    decimal? LeaderboardPnlToVolumePct,
    int CurrentPositionsCount,
    int ClosedPositionsCount,
    decimal PositionsTotalPnlUsd,
    decimal? PositionsTotalPercentPnl,
    DateTimeOffset? RatingRefreshedAtUtc);

public sealed record OnChainPaperSignalResult(
    Guid Id,
    Guid CaptureId,
    string TransactionHash,
    long LogIndex,
    OnChainParticipantRole ParticipantRole,
    string CopiedTraderWallet,
    string CounterpartyWallet,
    TradeSide Side,
    string TokenId,
    string ConditionId,
    string MarketSlug,
    string Outcome,
    string? LocalCategory,
    string? PolymarketCategory,
    bool? RatingFound,
    int? LeaderboardRank,
    decimal? LeaderboardPnlUsd,
    decimal? LeaderboardVolumeUsd,
    decimal? LeaderboardPnlToVolumePct,
    Guid? SignalId,
    Guid? PaperOrderId,
    string Status,
    string DecisionCode,
    string ReasonDetails,
    DateTimeOffset ProcessedAtUtc);

public sealed record OnChainPaperSignalProcessingResult(
    int CandidatesFetched,
    int SignalsCreated,
    int SignalsAccepted,
    int SignalsRejected,
    int PaperOrdersCreated,
    int Errors);

public sealed record OnChainMarketEnrichmentResult(
    int TokensRequested,
    int TokensResolved,
    int TokensNotFound,
    int MetadataRowsStored,
    int BatchesRun,
    bool ReachedBatchLimit);

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
