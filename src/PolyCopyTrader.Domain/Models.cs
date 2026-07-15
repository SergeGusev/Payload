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

public sealed record ExpiryFuturesReferencePricePoint(
    string AssetSymbol,
    string InstrumentId,
    DateTimeOffset ExpiryAtUtc,
    decimal BidPriceUsd,
    decimal AskPriceUsd,
    decimal MidPriceUsd,
    decimal IndexPriceUsd,
    DateTimeOffset FuturesSourceUpdatedAtUtc,
    DateTimeOffset IndexSourceUpdatedAtUtc,
    DateTimeOffset FetchedAtUtc,
    string Source);

public sealed record CryptoReferencePriceTick(
    Guid Id,
    string AssetSymbol,
    string BinanceSymbol,
    DateTimeOffset SampledAtUtc,
    DateTimeOffset BucketStartUtc,
    decimal PriceUsd,
    DateTimeOffset SourceUpdatedAtUtc,
    DateTimeOffset FetchedAtUtc,
    string Source,
    DateTimeOffset CreatedAtUtc);

public sealed record CryptoReferencePriceAverage(
    string AssetSymbol,
    string BinanceSymbol,
    string WindowLabel,
    int WindowSeconds,
    int SampleStepSeconds,
    int SampleCount,
    int ExpectedSampleCount,
    bool IsFullWindow,
    decimal? AveragePriceUsd,
    DateTimeOffset? FirstBucketStartUtc,
    DateTimeOffset? LastBucketStartUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CryptoReferencePriceAveragesSnapshot(
    DateTimeOffset SnapshotAtUtc,
    IReadOnlyList<CryptoReferencePriceAverage> Averages);

public sealed record CryptoReferencePriceExtrema(
    string AssetSymbol,
    string BinanceSymbol,
    int LookbackHours,
    int WindowSeconds,
    int CoverageBucketSeconds,
    int TickCount,
    int CoverageBucketCount,
    int ExpectedCoverageBucketCount,
    bool IsFullWindow,
    decimal? MinimumPriceUsd,
    DateTimeOffset? MinimumSampledAtUtc,
    decimal? MaximumPriceUsd,
    DateTimeOffset? MaximumSampledAtUtc,
    DateTimeOffset? FirstBucketStartUtc,
    DateTimeOffset? LastBucketStartUtc,
    DateTimeOffset UpdatedAtUtc);

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

public sealed record BtcUpDown5mStrategyStageTiming(
    Guid Id,
    Guid CycleId,
    string CycleKind,
    string? FlowName,
    string StageName,
    string? Detail,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long DurationMilliseconds,
    int? VariantCount,
    int? RunCount,
    int? EntriesPlaced,
    int? RunsSkipped,
    int? RunsSettled,
    int? MarketsObserved,
    DateTimeOffset? EarliestEntryDueAtUtc,
    DateTimeOffset? LatestEntryDueAtUtc,
    bool Succeeded,
    string? ErrorMessage,
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

public sealed record Btc5mHistoryRow(
    int Seconds,
    int Cents,
    int Count,
    int UpCount,
    int DownCount);

public readonly record struct Btc5mHistoryKey(int Seconds, int Cents);

public sealed record Btc5mHistoryLiveObservation(
    Guid Id,
    string MarketId,
    string ConditionId,
    string MarketSlug,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset MarketEndUtc,
    DateTimeOffset SampledAtUtc,
    int Seconds,
    int Cents,
    decimal BinancePriceUsd,
    decimal BinanceStartPriceUsd,
    decimal BtcMoveFromStartUsd,
    string? Result,
    bool AppliedToHistory,
    DateTimeOffset? AppliedAtUtc,
    int ResultCheckAttempts,
    DateTimeOffset NextResultCheckUtc,
    string? LastResultError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BtcUpDown5mStatisticsTick(
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
    decimal? BinanceStartPriceUsd,
    decimal? BtcMoveFromStartUsd,
    decimal? BtcMoveFromStartCents,
    int? SecondsLower,
    int? SecondsUpper,
    int? CentsLower,
    int? CentsUpper,
    decimal? EffectiveCount,
    decimal? UpProbability,
    decimal? DownProbability,
    int SupportThreshold,
    int HistoryRowsFound,
    int MissingHistoryCorners,
    string InterpolationMethod,
    string UpAssetId,
    decimal? UpMarketPrice,
    string UpMarketPriceKind,
    string DownAssetId,
    decimal? DownMarketPrice,
    string DownMarketPriceKind,
    decimal? UpEdge,
    decimal? DownEdge,
    string DecisionCode,
    string? RecommendedOutcome,
    bool WouldBet,
    string DiagnosticsJson,
    DateTimeOffset CreatedAtUtc);

public sealed record BtcUpDown5mArbitrageScan(
    Guid Id,
    string MarketId,
    string ConditionId,
    string MarketSlug,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset MarketEndUtc,
    DateTimeOffset SampledAtUtc,
    decimal SecondsAfterStart,
    decimal SecondsToClose,
    string UpAssetId,
    decimal? UpBestBid,
    decimal? UpBestAsk,
    decimal? UpAskDepthShares,
    string UpBookSource,
    decimal? UpBookAgeMs,
    string DownAssetId,
    decimal? DownBestBid,
    decimal? DownBestAsk,
    decimal? DownAskDepthShares,
    string DownBookSource,
    decimal? DownBookAgeMs,
    decimal RequiredMinShares,
    decimal MaxCommonExecutableShares,
    decimal? BestExecutableShares,
    decimal? UpCostUsd,
    decimal? DownCostUsd,
    decimal? TotalCostUsd,
    decimal? GuaranteedPayoutUsd,
    decimal? GrossProfitUsd,
    decimal? SafetyBufferUsd,
    decimal? NetProfitUsd,
    decimal? AverageCostPerShare,
    decimal? EdgePerShare,
    decimal SafetyBufferPerShare,
    decimal MinNetProfitUsd,
    string DecisionCode,
    bool WouldArbitrage,
    string DiagnosticsJson,
    DateTimeOffset CreatedAtUtc);

public sealed record BtcUpDown5mResultStreakDiagnostic(
    Guid Id,
    string MarketId,
    string ConditionId,
    string MarketSlug,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset? MarketEndUtc,
    DateTimeOffset SampledAtUtc,
    string? LatestPreviousMarketId,
    string? LatestPreviousMarketSlug,
    DateTimeOffset? LatestPreviousMarketStartUtc,
    DateTimeOffset? LatestPreviousMarketEndUtc,
    string? StreakWinningOutcome,
    string? BaseSelectedDirection,
    string? SelectedOutcome,
    int CloseBookStreakResultCount,
    int CumulativeMoveMarketCount,
    decimal? LatestMoveBps,
    decimal? LatestAbsMoveBps,
    decimal? CumulativeMoveBps,
    decimal? CumulativeAbsMoveBps,
    string? RejectionReason,
    string? StreakTruncatedReason,
    string DiagnosticsJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CryptoUpDown5mDiffSnapshot(
    Guid Id,
    string AssetSymbol,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset SampledAtUtc,
    DateTimeOffset? CounterStartMarketStartUtc,
    DateTimeOffset? LastIncludedMarketStartUtc,
    DateTimeOffset? HighWaterMarketStartUtc,
    bool CounterInitialized,
    int UpCount,
    int DownCount,
    int DiffCount,
    int Diff,
    int ProcessedMarketCount,
    DateTimeOffset? HistoryFetchFailedAtUtc,
    DateTimeOffset? HistoryFetchRetryAfterUtc,
    string? HistoryFetchError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CryptoUpDown5mDiffShiftProgressState(
    Guid StrategyId,
    string AssetSymbol,
    string TriggerOutcome,
    int UpCount,
    int DownCount,
    decimal SumAmount,
    bool DampingActive,
    string? DampingDirection,
    DateTimeOffset? LastProcessedMarketStartUtc,
    DateTimeOffset? PendingMarketStartUtc,
    string? PendingTargetOutcome,
    decimal? PendingStakeUsd,
    DateTimeOffset? PendingCreatedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CryptoUpDown5mResultPollingObservation(
    Guid Id,
    string AssetSymbol,
    string MarketId,
    string ConditionId,
    string MarketSlug,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset MarketEndUtc,
    DateTimeOffset FirstObservedEndedAtUtc,
    DateTimeOffset PollingStartedAtUtc,
    DateTimeOffset? LastPollAtUtc,
    int PollAttempts,
    DateTimeOffset? FirstClosedAtUtc,
    DateTimeOffset? FirstWinnerAtUtc,
    string? WinningOutcome,
    decimal? ClosedDelaySeconds,
    decimal? ResultDelaySeconds,
    string Status,
    string LastResponseStatus,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CryptoUpDown5mWebSocketResolvedMarket(
    Guid Id,
    string AssetSymbol,
    string MarketId,
    string ConditionId,
    string MarketSlug,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset MarketEndUtc,
    string WinningOutcome,
    string? WinningAssetId,
    DateTimeOffset EventTimestampUtc,
    DateTimeOffset FirstReceivedAtUtc,
    DateTimeOffset LastReceivedAtUtc,
    int EventCount,
    decimal ResultDelaySeconds,
    string Source,
    string RawEventType,
    string RawJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record MarketResolvedEventDiagnostic(
    Guid Id,
    string Component,
    string RawEventType,
    string? AssetId,
    string? ConditionId,
    string? WinningAssetId,
    string? WinningOutcome,
    DateTimeOffset EventTimestampUtc,
    DateTimeOffset ReceivedAtUtc,
    bool ActiveSnapshotFound,
    string? SnapshotMarketId,
    string? SnapshotConditionId,
    string? SnapshotMarketSlug,
    string? SnapshotAssetSymbol,
    DateTimeOffset? SnapshotMarketStartUtc,
    bool SnapshotIsCryptoUpDown5m,
    string RecorderAction,
    string RawJson,
    DateTimeOffset CreatedAtUtc);

public sealed record MarketWebSocketFrameDiagnostic(
    Guid Id,
    string Component,
    DateTimeOffset ReceivedAtUtc,
    string FrameKind,
    int PayloadLengthChars,
    string PayloadSha256,
    int EventCount,
    string EventTypesJson,
    string AssetIdsJson,
    string MarketIdsJson,
    bool ContainsMarketResolvedText,
    bool ContainsResolvedText,
    bool ParseSucceeded,
    int ParsedUpdateCount,
    string? ParseError,
    string RawPayload,
    bool RawPayloadTruncated,
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

public static class PolymarketAutoRedeemStatuses
{
    public const string DryRunReady = "DryRunReady";

    public const string SubmitPending = "SubmitPending";

    public const string SubmitRetryPending = "SubmitRetryPending";

    public const string SkippedUnsupported = "SkippedUnsupported";

    public const string SubmitNotImplemented = "SubmitNotImplemented";

    public const string Submitted = "Submitted";

    public const string Confirmed = "Confirmed";

    public const string Failed = "Failed";
}

public sealed record PolymarketAutoRedeemAttempt(
    Guid Id,
    string Wallet,
    string? ProxyWallet,
    string ConditionId,
    string AssetId,
    string MarketSlug,
    string MarketTitle,
    string Outcome,
    int? OutcomeIndex,
    decimal? RedeemableValueUsd,
    decimal? Size,
    string Status,
    bool DryRun,
    bool AutoSubmitEnabled,
    string TargetContract,
    string Calldata,
    string CollateralToken,
    string ParentCollectionId,
    IReadOnlyList<int> IndexSets,
    string? RelayerTransactionId,
    string? TransactionHash,
    string? LastError,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string RawPositionJson);

public sealed record PolymarketAutoRedeemCycleResult(
    int PositionsFetched,
    int RedeemablePositions,
    int AttemptsRecorded,
    int Skipped,
    int Submitted);

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
    int OldestOpenOrderAgeSeconds = 0,
    bool HasOppositeOutcomeOpenOrder = false);

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
    public const string BtcUpDown5mUpSimpleIdValue = "b7c50005-0000-4000-8121-000000000001";
    public const string BtcUpDown5mDownSimpleIdValue = "b7c50005-0000-4000-8122-000000000001";
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
    public const string BtcUpDown5mStatisticsIdValue = "b7c50005-0000-4000-8050-000000000001";
    public const string BtcUpDown5mUpSimpleCode = "btc_up_down_5m_up_simple";
    public const string BtcUpDown5mDownSimpleCode = "btc_up_down_5m_down_simple";
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
    public const string BtcUpDown5mStatisticsCode = "btc_up_down_5m_statistics";
    public const string BtcUpDown5mStatisticsName = "BTC Up or Down 5m Statistics";
    public const string SolUpDown5mDown8BpsReferenceAveragePremarketCode = "sol_up_down_5m_down_bps_8_fak_premarket";

    public static readonly Guid FollowLeader = Guid.Parse(FollowLeaderIdValue);
    public static readonly Guid BtcUpDown5mUpSimple = Guid.Parse(BtcUpDown5mUpSimpleIdValue);
    public static readonly Guid BtcUpDown5mDownSimple = Guid.Parse(BtcUpDown5mDownSimpleIdValue);
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
    public static readonly Guid BtcUpDown5mStatistics = Guid.Parse(BtcUpDown5mStatisticsIdValue);

    public static readonly IReadOnlyList<BtcUpDown5mStrategyVariant> BtcUpDown5mVariants =
        CreateBtcUpDown5mVariants();
    public static readonly IReadOnlyList<BtcUpDown5mStrategyVariant> CryptoUpDown5mVariants =
        CreateCryptoUpDown5mVariants();
    public static readonly IReadOnlyList<BtcUpDown5mStrategyVariant> UpDown5mStrategyVariants =
        [.. BtcUpDown5mVariants, .. CryptoUpDown5mVariants];
    public static readonly IReadOnlyList<BtcUpDown5mStrategyVariant> DateDependentStrategyVariants =
        CreateDateDependentStrategyVariants();

    public static readonly IReadOnlyList<Guid> AllStrategyIds =
        [FollowLeader, .. UpDown5mStrategyVariants.Select(variant => variant.Id)];

    public static Guid Normalize(Guid strategyId)
    {
        return strategyId == Guid.Empty ? FollowLeader : strategyId;
    }

    public static Guid? TryGetStrategyIdByCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalizedCode = code.Trim();
        if (string.Equals(normalizedCode, FollowLeaderCode, StringComparison.OrdinalIgnoreCase))
        {
            return FollowLeader;
        }

        return UpDown5mStrategyVariants
            .FirstOrDefault(variant => string.Equals(variant.Code, normalizedCode, StringComparison.OrdinalIgnoreCase))
            ?.Id;
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

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateDateDependentStrategyVariants()
    {
        return
        [
            GetUpDown5mVariantByCode(SolUpDown5mDown8BpsReferenceAveragePremarketCode)
        ];
    }

    private static BtcUpDown5mStrategyVariant GetUpDown5mVariantByCode(string code)
    {
        return UpDown5mStrategyVariants.FirstOrDefault(variant =>
            string.Equals(variant.Code, code, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Strategy variant '{code}' is not registered.");
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateBtcUpDown5mVariants()
    {
        int[] delays = [30, 60, 90, 120, 150, 180, 210, 240, 270];
        var variants = new List<BtcUpDown5mStrategyVariant>(5244);

        for (var thresholdTenths = 1; thresholdTenths <= 50; thresholdTenths++)
        {
            var minMoveBps = (decimal)thresholdTenths;
            variants.Add(CreateBtcUpDown5mFixedOutcomeBpsThresholdInstantVariant(thresholdTenths, minMoveBps, isUp: true));
            variants.Add(CreateBtcUpDown5mFixedOutcomeBpsThresholdInstantVariant(thresholdTenths, minMoveBps, isUp: false));
        }

        foreach (var thresholdBps in CreateReferenceAverageBpsThresholdValues())
        {
            variants.Add(CreateReferenceAverageBpsThresholdFakPremarketVariant("BTC", 8135, thresholdBps, isUpTrigger: true));
            variants.Add(CreateReferenceAverageBpsThresholdFakPremarketVariant("BTC", 8136, thresholdBps, isUpTrigger: false));
            variants.Add(CreateReferenceAverageBpsThresholdNeutralFakPremarketVariant(
                "BTC",
                GetReferenceAverageBpsNeutralPremarketIdGroup("BTC"),
                thresholdBps));
        }

        variants.AddRange(CreateAbsoluteBpsThresholdPremarketVariants("BTC"));

        variants.AddRange(CreateFuturesBasisBpsThresholdPremarketVariants("BTC"));

        for (var thresholdTenths = 1; thresholdTenths <= 50; thresholdTenths++)
        {
            var minMoveBps = (decimal)thresholdTenths;
            variants.Add(CreateBtcUpDown5mFixedOutcomeBpsThresholdInstantVariant(
                thresholdTenths,
                minMoveBps,
                isUp: true,
                BtcUpDownMarketInterval.FifteenMinutes));
            variants.Add(CreateBtcUpDown5mFixedOutcomeBpsThresholdInstantVariant(
                thresholdTenths,
                minMoveBps,
                isUp: false,
                BtcUpDownMarketInterval.FifteenMinutes));
        }

        variants.AddRange(CreateDiffCounterTrendFakPremarketVariants("BTC"));
        variants.AddRange(CreateDiffProgressVariants("BTC"));
        variants.AddRange(CreateDiffShiftProgressVariants("BTC"));
        variants.AddRange(CreateDiffShiftProgressPremarketVariants("BTC"));
        variants.AddRange(CreateDiffLimitProgressPremarketVariants("BTC"));
        variants.AddRange(CreateDiffRealLimitProgressPremarketVariants("BTC"));
        variants.AddRange(CreateDiffReferenceAveragePremarketVariants("BTC"));
        variants.AddRange(CreateBpsConfirmedAveragePremarketVariants("BTC", variants));
        variants.AddRange(CreateDiffConfirmedAveragePremarketVariants("BTC", variants));
        variants.AddRange(CreateChildMirrorVariants("BTC"));
        variants.AddRange(CreateBtcPreOpenFixedDirectionVariants());

        return ExcludeRetiredProgressVariants(variants);
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
        var sampleDescription = GetMiddleReferenceMeanDescription("BTC", depth);
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-8006-000000000{GetMiddleReferenceDepthIdSuffix(depth):000}"),
            $"btc_up_down_5m_middle_{depth}",
            $"BTC Up or Down 5m Middle {depth}",
            $"Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price against the arithmetic mean of {sampleDescription}; above mean buys Down, below mean buys Up, otherwise skip until all {depth} sampled prices are available. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReference,
            depth);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mMiddleBpsThresholdVariant(int depth, decimal thresholdBps)
    {
        var sampleDescription = GetMiddleReferenceMeanDescription("BTC", depth);
        var thresholdName = thresholdBps.ToString("0.#", CultureInfo.InvariantCulture);
        var thresholdId = (int)thresholdBps;
        var idSuffix = GetMiddleReferenceBpsIdSuffix(depth, thresholdId);
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-8023-{idSuffix:000000000000}"),
            $"btc_up_down_5m_middle_{depth}_bps_{thresholdId}",
            $"BTC Up or Down 5m Middle {depth} {thresholdName} bps",
            $"Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price against the arithmetic mean of {sampleDescription}; above mean buys Down, below mean buys Up, otherwise skip until all {depth} sampled prices are available. Enter only when the current price is at least {thresholdName} bps away from the mean. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReference,
            depth,
            thresholdBps);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mMiddleBpsThresholdInstantVariant(int depth, decimal thresholdBps)
    {
        var sampleDescription = GetMiddleReferenceMeanDescription("BTC", depth);
        var thresholdName = thresholdBps.ToString("0.#", CultureInfo.InvariantCulture);
        var thresholdId = (int)thresholdBps;
        var idSuffix = GetMiddleReferenceBpsIdSuffix(depth, thresholdId);
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-8029-{idSuffix:000000000000}"),
            $"btc_up_down_5m_middle_{depth}_bps_{thresholdId}_instant",
            $"BTC Up or Down 5m Middle {depth} {thresholdName} bps Instant",
            $"Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price against the arithmetic mean of {sampleDescription}; above mean buys Down, below mean buys Up, otherwise skip until all {depth} sampled prices are available. Enter only when the current price is at least {thresholdName} bps away from the mean. Paper entry simulates a BUY FAK taker fill from current executable ask depth; available liquidity is taken immediately, any remainder is cancelled, and settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReferenceInstant,
            depth,
            thresholdBps);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mMiddleRevertVariant(int depth)
    {
        var sampleDescription = GetMiddleReferenceMeanDescription("BTC", depth);
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-8009-000000000{GetMiddleReferenceDepthIdSuffix(depth):000}"),
            $"btc_up_down_5m_middle_{depth}_revert",
            $"BTC Up or Down 5m Middle {depth} Revert",
            $"Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price against the arithmetic mean of {sampleDescription}, then invert the standard Middle {depth} decision; above mean buys Up, below mean buys Down, otherwise skip until all {depth} sampled prices are available. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevert,
            depth);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mMiddleRevertBpsThresholdVariant(int depth, decimal thresholdBps)
    {
        var sampleDescription = GetMiddleReferenceMeanDescription("BTC", depth);
        var thresholdName = thresholdBps.ToString("0.#", CultureInfo.InvariantCulture);
        var thresholdId = (int)thresholdBps;
        var idSuffix = GetMiddleReferenceBpsIdSuffix(depth, thresholdId);
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-8024-{idSuffix:000000000000}"),
            $"btc_up_down_5m_middle_{depth}_revert_bps_{thresholdId}",
            $"BTC Up or Down 5m Middle {depth} Revert {thresholdName} bps",
            $"Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price against the arithmetic mean of {sampleDescription}, then invert the standard Middle {depth} decision; above mean buys Up, below mean buys Down, otherwise skip until all {depth} sampled prices are available. Enter only when the current price is at least {thresholdName} bps away from the mean. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevert,
            depth,
            thresholdBps);
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mMiddleRevertBpsThresholdInstantVariant(int depth, decimal thresholdBps)
    {
        var sampleDescription = GetMiddleReferenceMeanDescription("BTC", depth);
        var thresholdName = thresholdBps.ToString("0.#", CultureInfo.InvariantCulture);
        var thresholdId = (int)thresholdBps;
        var idSuffix = GetMiddleReferenceBpsIdSuffix(depth, thresholdId);
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-8030-{idSuffix:000000000000}"),
            $"btc_up_down_5m_middle_{depth}_revert_bps_{thresholdId}_instant",
            $"BTC Up or Down 5m Middle {depth} Revert {thresholdName} bps Instant",
            $"Immediately after BTC 5m market open, compare the latest Binance BTC/USDT trade-stream price against the arithmetic mean of {sampleDescription}, then invert the standard Middle {depth} decision; above mean buys Up, below mean buys Down, otherwise skip until all {depth} sampled prices are available. Enter only when the current price is at least {thresholdName} bps away from the mean. Paper entry simulates a BUY FAK taker fill from current executable ask depth; available liquidity is taken immediately, any remainder is cancelled, and settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant,
            depth,
            thresholdBps);
    }

    private static IReadOnlyList<int> CreateMiddleReferenceSampleDepths()
    {
        return [100, 90, 80, 70, 60, 50, 40, 30, 20, 10];
    }

    private static IEnumerable<int> CreateMiddleReferenceThresholdBpsValues()
    {
        for (var thresholdBps = 5; thresholdBps <= 100; thresholdBps += 5)
        {
            yield return thresholdBps;
        }
    }

    private static int GetMiddleReferenceDepthIdSuffix(int depth)
    {
        return depth == 100 ? 1 : depth;
    }

    private static int GetMiddleReferenceBpsIdSuffix(int depth, int thresholdId)
    {
        return depth == 100 ? 100 + thresholdId : (depth * 100) + thresholdId;
    }

    private static string GetMiddleReferenceMeanDescription(string assetSymbol, int depth)
    {
        return $"the latest {depth} sampled Binance {assetSymbol}/USDT reference price(s)";
    }

    private static BtcUpDown5mStrategyVariant CreateBtcUpDown5mFixedOutcomeBpsThresholdInstantVariant(
        int thresholdTenths,
        decimal minMoveBps,
        bool isUp,
        BtcUpDownMarketInterval marketInterval = BtcUpDownMarketInterval.FiveMinutes)
    {
        var thresholdName = minMoveBps.ToString("0.###", CultureInfo.InvariantCulture);
        var directionName = isUp ? "Up" : "Down";
        var oppositeDirectionName = isUp ? "Down" : "Up";
        var intervalCode = GetUpDownIntervalCode(marketInterval);
        var intervalName = GetUpDownIntervalName(marketInterval);
        return new BtcUpDown5mStrategyVariant(
            GetBtcUpDownFixedOutcomeBpsThresholdInstantId(thresholdTenths, isUp, marketInterval),
            GetBtcUpDownFixedOutcomeBpsThresholdInstantCode(thresholdTenths, isUp, marketInterval),
            $"BTC Up or Down {intervalName} {directionName} {thresholdName} bps Instant",
            $"Immediately after BTC {intervalName} market open, use the previous BTC {intervalName} close-book result streak and archived Binance BTC start/end move gate; enter only when the cumulative streak move is at least {thresholdName} bps and the countertrend direction is {directionName}. If the countertrend direction is {oppositeDirectionName}, skip. Paper entry simulates a BUY FAK taker fill from current executable ask depth; available liquidity is taken immediately, any remainder is cancelled, and settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant,
            minMoveBps >= 1m && minMoveBps == decimal.Truncate(minMoveBps)
                ? (int)minMoveBps
                : 0,
            minMoveBps,
            MarketInterval: marketInterval,
            FixedOutcome: isUp ? BtcUpDownFixedOutcome.Up : BtcUpDownFixedOutcome.Down);
    }

    private static Guid GetBtcUpDownFixedOutcomeBpsThresholdInstantId(
        int thresholdTenths,
        bool isUp,
        BtcUpDownMarketInterval marketInterval)
    {
        var idGroup = marketInterval switch
        {
            BtcUpDownMarketInterval.FiveMinutes => isUp ? 8031 : 8032,
            BtcUpDownMarketInterval.FifteenMinutes => isUp ? 8051 : 8052,
            _ => throw new ArgumentOutOfRangeException(nameof(marketInterval), marketInterval, "Unsupported fixed bps Instant interval.")
        };
        return Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{100 + thresholdTenths:000000000000}");
    }

    private static string GetBtcUpDownFixedOutcomeBpsThresholdInstantCode(
        int thresholdTenths,
        bool isUp,
        BtcUpDownMarketInterval marketInterval)
    {
        var directionCode = isUp ? "up" : "down";
        return "btc_up_down_" + GetUpDownIntervalCode(marketInterval) + "_" + directionCode + "_bps_" + thresholdTenths.ToString(CultureInfo.InvariantCulture) + "_instant";
    }

    private static string GetUpDownIntervalCode(BtcUpDownMarketInterval marketInterval)
    {
        return marketInterval switch
        {
            BtcUpDownMarketInterval.FiveMinutes => "5m",
            BtcUpDownMarketInterval.FifteenMinutes => "15m",
            BtcUpDownMarketInterval.OneHour => "1h",
            BtcUpDownMarketInterval.FourHours => "4h",
            _ => throw new ArgumentOutOfRangeException(nameof(marketInterval), marketInterval, "Unsupported Up/Down interval.")
        };
    }

    private static string GetUpDownIntervalName(BtcUpDownMarketInterval marketInterval)
    {
        return GetUpDownIntervalCode(marketInterval);
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateCryptoUpDown5mVariants()
    {
        CryptoUpDown5mAssetSpec[] assets =
        [
            new("ETH", 8061, 8062, 8065, 8066, 8067, 8071, 8072, 8073, 8074, 8079, 8080, 8083, 8084, 8087, 8088, 8093, 8094, 8099, 8100, 8105, 8106, 8111, 8112, 8117, 8118),
            new("SOL", 8063, 8064, 8068, 8069, 8070, 8075, 8076, 8077, 8078, 8081, 8082, 8085, 8086, 8089, 8090, 8095, 8096, 8101, 8102, 8107, 8108, 8113, 8114, 8119, 8120)
        ];
        var variants = new List<BtcUpDown5mStrategyVariant>(assets.Length * 4815 + 64);
        foreach (var asset in assets)
        {
            for (var thresholdTenths = 1; thresholdTenths <= 50; thresholdTenths++)
            {
                var minMoveBps = (decimal)thresholdTenths;
                variants.Add(CreateCryptoUpDown5mFixedOutcomeBpsThresholdInstantVariant(asset, thresholdTenths, minMoveBps, isUp: true));
                variants.Add(CreateCryptoUpDown5mFixedOutcomeBpsThresholdInstantVariant(asset, thresholdTenths, minMoveBps, isUp: false));
                if (string.Equals(asset.Symbol, "ETH", StringComparison.OrdinalIgnoreCase))
                {
                    if (thresholdTenths == 9)
                    {
                        variants.Add(CreateCryptoUpDown5mFixedOutcomeBpsThresholdFakVariant(asset, thresholdTenths, minMoveBps, isUp: false));
                    }

                    variants.Add(CreateCryptoUpDown5mFixedOutcomeBpsThresholdFakPremarketVariant(
                        asset,
                        thresholdTenths,
                        minMoveBps,
                        isUp: false));

                    foreach (var spec in CreateEthDownFakPremarketBattleSpecs())
                    {
                        if (Array.IndexOf(spec.Thresholds, thresholdTenths) >= 0)
                        {
                            variants.Add(CreateCryptoUpDown5mFixedOutcomeBpsThresholdFakPremarketVariant(
                                asset,
                                thresholdTenths,
                                minMoveBps,
                                isUp: false,
                                spec.IdGroup,
                                spec.EntryDelaySeconds));
                        }
                    }
                }

                variants.Add(CreateCryptoUpDown5mFixedOutcomeBpsThresholdInstantVariant(
                    asset,
                    thresholdTenths,
                    minMoveBps,
                    isUp: true,
                    BtcUpDownMarketInterval.FifteenMinutes));
                variants.Add(CreateCryptoUpDown5mFixedOutcomeBpsThresholdInstantVariant(
                    asset,
                    thresholdTenths,
                    minMoveBps,
                    isUp: false,
                    BtcUpDownMarketInterval.FifteenMinutes));
            }

            foreach (var thresholdBps in CreateReferenceAverageBpsThresholdValues())
            {
                variants.Add(CreateReferenceAverageBpsThresholdFakPremarketVariant(
                    asset.Symbol,
                    GetReferenceAverageBpsPremarketIdGroup(asset.Symbol, isUpTrigger: true),
                    thresholdBps,
                    isUpTrigger: true));
                variants.Add(CreateReferenceAverageBpsThresholdFakPremarketVariant(
                    asset.Symbol,
                    GetReferenceAverageBpsPremarketIdGroup(asset.Symbol, isUpTrigger: false),
                    thresholdBps,
                    isUpTrigger: false));
                variants.Add(CreateReferenceAverageBpsThresholdNeutralFakPremarketVariant(
                    asset.Symbol,
                    GetReferenceAverageBpsNeutralPremarketIdGroup(asset.Symbol),
                    thresholdBps));
            }

            variants.AddRange(CreateAbsoluteBpsThresholdPremarketVariants(asset.Symbol));

            variants.AddRange(CreateFuturesBasisBpsThresholdPremarketVariants(asset.Symbol));

            variants.AddRange(CreateDiffCounterTrendFakPremarketVariants(asset.Symbol));
            variants.AddRange(CreateDiffProgressVariants(asset.Symbol));
            variants.AddRange(CreateDiffShiftProgressVariants(asset.Symbol));
            variants.AddRange(CreateDiffShiftProgressPremarketVariants(asset.Symbol));
            variants.AddRange(CreateDiffLimitProgressPremarketVariants(asset.Symbol));
            variants.AddRange(CreateDiffRealLimitProgressPremarketVariants(asset.Symbol));
            variants.AddRange(CreateDiffReferenceAveragePremarketVariants(asset.Symbol));
            variants.AddRange(CreateBpsConfirmedAveragePremarketVariants(asset.Symbol, variants));
            variants.AddRange(CreateDiffConfirmedAveragePremarketVariants(asset.Symbol, variants));
            variants.AddRange(CreateChildMirrorVariants(asset.Symbol));
        }

        return ExcludeRetiredProgressVariants(variants);
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> ExcludeRetiredProgressVariants(
        IEnumerable<BtcUpDown5mStrategyVariant> variants)
    {
        return variants
            .Where(variant => !IsRetiredProgressVariant(variant))
            .ToArray();
    }

    private static bool IsRetiredProgressVariant(BtcUpDown5mStrategyVariant variant)
    {
        var threshold = variant.DecisionDepth;
        if (string.Equals(variant.ReferenceAssetSymbol, "BTC", StringComparison.OrdinalIgnoreCase))
        {
            return variant.Behavior == BtcUpDown5mStrategyBehavior.DiffLimitProgressPremarket ||
                (variant.Behavior == BtcUpDown5mStrategyBehavior.DiffShiftProgress &&
                 variant.EntryDelaySeconds < 0 &&
                 threshold is 1 or 2 or 4 or 5);
        }

        if (string.Equals(variant.ReferenceAssetSymbol, "ETH", StringComparison.OrdinalIgnoreCase))
        {
            return (variant.Behavior == BtcUpDown5mStrategyBehavior.ChildProgressMirror &&
                    threshold is 1 or 2 or 3 or 4 or 5 or 6 or 8 or 9 or 10 or 11 or 13 or 14 or 19 or 21 or 24) ||
                (variant.Behavior == BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror &&
                 threshold is 3 or 5 or 7 or 8 or 9 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 21 or 22 or 23 or 24) ||
                (variant.Behavior == BtcUpDown5mStrategyBehavior.DiffShiftProgress &&
                 variant.EntryDelaySeconds < 0 &&
                 threshold == 4) ||
                (variant.Behavior == BtcUpDown5mStrategyBehavior.DiffProgress &&
                 variant.DiffCounterTriggerOutcome == BtcUpDownFixedOutcome.Up &&
                 threshold is 1 or 2 or 13 or 14 or 15 or 16);
        }

        return string.Equals(variant.ReferenceAssetSymbol, "SOL", StringComparison.OrdinalIgnoreCase) &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror &&
            threshold is 4 or 5 or 6 or 13 or 14 or 19 or 21 or 23;
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateDiffCounterTrendFakPremarketVariants(
        string assetSymbol)
    {
        var idGroups = GetDiffCounterTrendFakPremarketIdGroups(assetSymbol);
        var variants = new List<BtcUpDown5mStrategyVariant>(
            string.Equals(assetSymbol, "BTC", StringComparison.OrdinalIgnoreCase) ? 40 : 20);
        foreach (var threshold in CreateDiffCounterTrendFakPremarketThresholds(assetSymbol, isUpDiffGroup: true))
        {
            variants.Add(CreateDiffCounterTrendFakPremarketVariant(assetSymbol, idGroups.Up, threshold, isUpDiffGroup: true));
        }

        foreach (var threshold in CreateDiffCounterTrendFakPremarketThresholds(assetSymbol, isUpDiffGroup: false))
        {
            variants.Add(CreateDiffCounterTrendFakPremarketVariant(assetSymbol, idGroups.Down, threshold, isUpDiffGroup: false));
        }

        return variants;
    }

    private static (int Up, int Down) GetDiffCounterTrendFakPremarketIdGroups(
        string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => (8146, 8148),
            "ETH" => (8144, 8134),
            "SOL" => (8150, 8152),
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Diff Premarket asset.")
        };
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateDiffProgressVariants(
        string assetSymbol)
    {
        var idGroups = GetDiffProgressIdGroups(assetSymbol);
        var variants = new List<BtcUpDown5mStrategyVariant>(100);
        foreach (var threshold in CreateDiffProgressThresholds())
        {
            if (!IsRetiredDiffProgressVariant(assetSymbol, threshold, isUpDiffGroup: true))
            {
                variants.Add(CreateDiffProgressVariant(assetSymbol, idGroups.Up, threshold, isUpDiffGroup: true));
            }

            variants.Add(CreateDiffProgressVariant(assetSymbol, idGroups.Down, threshold, isUpDiffGroup: false));
        }

        return variants;
    }

    private static bool IsRetiredDiffProgressVariant(
        string assetSymbol,
        int threshold,
        bool isUpDiffGroup)
    {
        return isUpDiffGroup &&
            threshold is 1 or 2 &&
            string.Equals(assetSymbol, "SOL", StringComparison.OrdinalIgnoreCase);
    }

    private static (int Up, int Down) GetDiffProgressIdGroups(
        string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => (8154, 8155),
            "ETH" => (8156, 8157),
            "SOL" => (8158, 8159),
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Diff Progress asset.")
        };
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateDiffShiftProgressVariants(
        string assetSymbol)
    {
        var idGroups = GetDiffShiftProgressIdGroups(assetSymbol);
        return
        [
            CreateDiffShiftProgressVariant(assetSymbol, idGroups.Up, isUpDiffGroup: true),
            CreateDiffShiftProgressVariant(assetSymbol, idGroups.Down, isUpDiffGroup: false)
        ];
    }

    private static (int Up, int Down) GetDiffShiftProgressIdGroups(
        string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => (8160, 8161),
            "ETH" => (8162, 8163),
            "SOL" => (8164, 8165),
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Diff Shift Progress asset.")
        };
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateDiffShiftProgressPremarketVariants(
        string assetSymbol)
    {
        var variants = new List<BtcUpDown5mStrategyVariant>(5);
        var idGroup = GetDiffShiftProgressPremarketIdGroup(assetSymbol);
        for (var threshold = 1; threshold <= 5; threshold++)
        {
            variants.Add(CreateDiffShiftProgressPremarketVariant(assetSymbol, idGroup, threshold));
        }

        return variants;
    }

    private static int GetDiffShiftProgressPremarketIdGroup(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => 8166,
            "ETH" => 8167,
            "SOL" => 8168,
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Diff Shift Progress Premarket asset.")
        };
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateDiffLimitProgressPremarketVariants(
        string assetSymbol)
    {
        var variants = new List<BtcUpDown5mStrategyVariant>(5);
        var idGroup = GetDiffLimitProgressPremarketIdGroup(assetSymbol);
        for (var limit = 1; limit <= 5; limit++)
        {
            variants.Add(CreateDiffLimitProgressPremarketVariant(assetSymbol, idGroup, limit));
        }

        return variants;
    }

    private static int GetDiffLimitProgressPremarketIdGroup(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => 8169,
            "ETH" => 8170,
            "SOL" => 8171,
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Diff Limit Progress Premarket asset.")
        };
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateDiffRealLimitProgressPremarketVariants(
        string assetSymbol)
    {
        var variants = new List<BtcUpDown5mStrategyVariant>(5);
        var idGroup = GetDiffRealLimitProgressPremarketIdGroup(assetSymbol);
        for (var limit = 1; limit <= 5; limit++)
        {
            variants.Add(CreateDiffRealLimitProgressPremarketVariant(assetSymbol, idGroup, limit));
        }

        return variants;
    }

    private static int GetDiffRealLimitProgressPremarketIdGroup(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => 8172,
            "ETH" => 8173,
            "SOL" => 8174,
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Diff Real Limit Progress Premarket asset.")
        };
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateDiffReferenceAveragePremarketVariants(
        string assetSymbol)
    {
        var variants = new List<BtcUpDown5mStrategyVariant>(14);
        var idGroup = GetDiffReferenceAveragePremarketIdGroup(assetSymbol);
        foreach (var threshold in CreateDiffReferenceAveragePremarketThresholds())
        {
            variants.Add(CreateDiffReferenceAveragePremarketVariant(assetSymbol, idGroup, threshold));
        }

        return variants;
    }

    private static int GetDiffReferenceAveragePremarketIdGroup(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => 8175,
            "ETH" => 8176,
            "SOL" => 8177,
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Diff Reference Average Premarket asset.")
        };
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateBpsConfirmedAveragePremarketVariants(
        string assetSymbol,
        IReadOnlyCollection<BtcUpDown5mStrategyVariant> registeredVariants)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var confirmationThreshold = GetBpsConfirmedAverageDiffThreshold(normalizedAsset);
        var confirmationVariant = registeredVariants.Single(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket &&
            string.Equals(variant.ReferenceAssetSymbol, normalizedAsset, StringComparison.OrdinalIgnoreCase) &&
            variant.DecisionDepth == confirmationThreshold);
        var variants = new List<BtcUpDown5mStrategyVariant>(28);
        var idGroup = GetBpsConfirmedAveragePremarketIdGroup(normalizedAsset);
        foreach (var threshold in CreateReferenceAverageBpsThresholdValues())
        {
            var baseVariant = registeredVariants.Single(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket &&
                string.Equals(variant.ReferenceAssetSymbol, normalizedAsset, StringComparison.OrdinalIgnoreCase) &&
                variant.FixedOutcome is null &&
                variant.DiffCounterTriggerOutcome is null &&
                variant.DecisionThresholdBps == threshold);
            variants.Add(CreateBpsConfirmedAveragePremarketVariant(
                normalizedAsset,
                idGroup,
                threshold,
                baseVariant,
                confirmationVariant));
        }

        return variants;
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateDiffConfirmedAveragePremarketVariants(
        string assetSymbol,
        IReadOnlyCollection<BtcUpDown5mStrategyVariant> registeredVariants)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var confirmationThreshold = GetDiffConfirmedAverageBpsThreshold(normalizedAsset);
        var confirmationVariant = registeredVariants.Single(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket &&
            string.Equals(variant.ReferenceAssetSymbol, normalizedAsset, StringComparison.OrdinalIgnoreCase) &&
            variant.FixedOutcome is null &&
            variant.DiffCounterTriggerOutcome is null &&
            variant.DecisionThresholdBps == confirmationThreshold);
        var variants = new List<BtcUpDown5mStrategyVariant>(14);
        var idGroup = GetDiffConfirmedAveragePremarketIdGroup(normalizedAsset);
        foreach (var threshold in CreateDiffReferenceAveragePremarketThresholds())
        {
            var baseVariant = registeredVariants.Single(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket &&
                string.Equals(variant.ReferenceAssetSymbol, normalizedAsset, StringComparison.OrdinalIgnoreCase) &&
                variant.DecisionDepth == threshold);
            variants.Add(CreateDiffConfirmedAveragePremarketVariant(
                normalizedAsset,
                idGroup,
                threshold,
                baseVariant,
                confirmationVariant));
        }

        return variants;
    }

    private static int GetBpsConfirmedAverageDiffThreshold(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => 5,
            "ETH" => 3,
            "SOL" => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Bps Confirmed Average Premarket asset.")
        };
    }

    private static int GetDiffConfirmedAverageBpsThreshold(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => 45,
            "ETH" => 5,
            "SOL" => 35,
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Diff Confirmed Average Premarket asset.")
        };
    }

    private static int GetBpsConfirmedAveragePremarketIdGroup(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => 8200,
            "ETH" => 8201,
            "SOL" => 8202,
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Bps Confirmed Average Premarket asset.")
        };
    }

    private static int GetDiffConfirmedAveragePremarketIdGroup(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => 8203,
            "ETH" => 8204,
            "SOL" => 8205,
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Diff Confirmed Average Premarket asset.")
        };
    }

    private static IEnumerable<int> CreateDiffCounterTrendFakPremarketThresholds(
        string assetSymbol,
        bool isUpDiffGroup)
    {
        var maxThreshold = string.Equals(assetSymbol, "BTC", StringComparison.OrdinalIgnoreCase) && !isUpDiffGroup
            ? 30
            : 10;
        for (var threshold = 1; threshold <= maxThreshold; threshold++)
        {
            yield return threshold;
        }
    }

    private static IEnumerable<int> CreateDiffProgressThresholds()
    {
        for (var threshold = 1; threshold <= 50; threshold++)
        {
            yield return threshold;
        }
    }

    private static IEnumerable<int> CreateReferenceAverageBpsThresholdValues()
    {
        for (var threshold = 1; threshold <= 10; threshold++)
        {
            yield return threshold;
        }

        for (var threshold = 15; threshold <= 100; threshold += 5)
        {
            yield return threshold;
        }
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateAbsoluteBpsThresholdPremarketVariants(
        string assetSymbol)
    {
        const int minimumLookbackHours = 1;
        const int maximumLookbackHours = 24;
        const int minimumThresholdBps = 1;
        const int maximumThresholdBps = 5;
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var idGroup = GetAbsoluteBpsThresholdPremarketIdGroup(normalizedAsset);
        var variants = new List<BtcUpDown5mStrategyVariant>(
            (maximumLookbackHours - minimumLookbackHours + 1) *
            (maximumThresholdBps - minimumThresholdBps + 1));

        for (var lookbackHours = minimumLookbackHours; lookbackHours <= maximumLookbackHours; lookbackHours++)
        {
            for (var thresholdBps = minimumThresholdBps; thresholdBps <= maximumThresholdBps; thresholdBps++)
            {
                var idSuffix = lookbackHours * 100 + thresholdBps;
                var lookbackText = lookbackHours.ToString(CultureInfo.InvariantCulture) + "h";
                var thresholdText = thresholdBps.ToString(CultureInfo.InvariantCulture);
                variants.Add(new BtcUpDown5mStrategyVariant(
                    Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{idSuffix:000000000000}"),
                    $"{assetCode}_up_down_5m_{lookbackText}_absolute_bps_{thresholdText}_fak_premarket",
                    $"{normalizedAsset} Up or Down 5m {lookbackText} {thresholdText} bps Absolute Premarket",
                    $"30 seconds before {normalizedAsset} 5m market open, read the full {lookbackText} rolling extrema window built from persisted ten-second Binance {normalizedAsset}/USDT reference-price samples observed before the fresh decision price. If the current price is at least {thresholdText} bps above the historical maximum, BUY Down; if it is at least {thresholdText} bps below the historical minimum, BUY Up; otherwise skip. Paper entry simulates a FAK taker BUY from executable ask depth using the guaranteed worst-price cap, while Live-shadow remains disabled by default until manually enabled and normal live gates pass.",
                    BtcUpDown5mStrategyDirection.Dynamic,
                    -30,
                    BtcUpDown5mStrategyBehavior.AbsoluteBpsThresholdFakPremarket,
                    lookbackHours,
                    thresholdBps,
                    Category: $"{normalizedAsset} Up/Down 5m Absolute Premarket",
                    ReferenceAssetSymbol: normalizedAsset));
            }
        }

        return variants;
    }

    private static int GetAbsoluteBpsThresholdPremarketIdGroup(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => 8206,
            "ETH" => 8207,
            "SOL" => 8208,
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Absolute Premarket asset.")
        };
    }

    private static IEnumerable<int> CreateDiffReferenceAveragePremarketThresholds()
    {
        for (var threshold = 1; threshold <= 10; threshold++)
        {
            yield return threshold;
        }

        foreach (var threshold in new[] { 15, 20, 25, 30 })
        {
            yield return threshold;
        }
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateFuturesBasisBpsThresholdPremarketVariants(
        string assetSymbol)
    {
        var variants = new List<BtcUpDown5mStrategyVariant>(16);
        var idGroups = GetFuturesBasisBpsThresholdPremarketIdGroups(assetSymbol);
        foreach (var threshold in CreateFuturesBasisBpsThresholds())
        {
            variants.Add(CreateFuturesBasisBpsThresholdPremarketVariant(
                assetSymbol,
                idGroups.Standard,
                threshold,
                BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarket));
            variants.Add(CreateFuturesBasisBpsThresholdPremarketVariant(
                assetSymbol,
                idGroups.Revert,
                threshold,
                BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarketRevert));
        }

        return variants;
    }

    private static (int Standard, int Revert) GetFuturesBasisBpsThresholdPremarketIdGroups(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => (8182, 8191),
            "ETH" => (8183, 8192),
            "SOL" => (8184, 8193),
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Futures Basis Premarket asset.")
        };
    }

    private static IEnumerable<int> CreateFuturesBasisBpsThresholds()
    {
        foreach (var threshold in new[] { 1, 2, 3, 5, 8, 10, 15, 20 })
        {
            yield return threshold;
        }
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateChildMirrorVariants(
        string assetSymbol)
    {
        var variants = new List<BtcUpDown5mStrategyVariant>(96);
        var idGroups = GetChildMirrorIdGroups(assetSymbol);
        for (var lookbackHours = 1; lookbackHours <= 24; lookbackHours++)
        {
            variants.Add(CreateChildMirrorVariant(
                assetSymbol,
                idGroups.Child,
                lookbackHours,
                BtcUpDown5mStrategyBehavior.ChildMirror));
            variants.Add(CreateChildMirrorVariant(
                assetSymbol,
                idGroups.ChildProgress,
                lookbackHours,
                BtcUpDown5mStrategyBehavior.ChildProgressMirror));
            variants.Add(CreateChildMirrorVariant(
                assetSymbol,
                idGroups.ChildRoi,
                lookbackHours,
                BtcUpDown5mStrategyBehavior.ChildRoiMirror));
            variants.Add(CreateChildMirrorVariant(
                assetSymbol,
                idGroups.ChildProgressRoi,
                lookbackHours,
                BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror));
        }

        return variants;
    }

    private static (int Child, int ChildProgress, int ChildRoi, int ChildProgressRoi) GetChildMirrorIdGroups(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => (8185, 8188, 8194, 8197),
            "ETH" => (8186, 8189, 8195, 8198),
            "SOL" => (8187, 8190, 8196, 8199),
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported Child mirror asset.")
        };
    }

    private static BtcUpDown5mStrategyVariant CreateChildMirrorVariant(
        string assetSymbol,
        int idGroup,
        int lookbackHours,
        BtcUpDown5mStrategyBehavior behavior)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var isProgress = behavior is BtcUpDown5mStrategyBehavior.ChildProgressMirror or
            BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror;
        var isRoi = behavior is BtcUpDown5mStrategyBehavior.ChildRoiMirror or
            BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror;
        var nameSuffix = (isProgress, isRoi) switch
        {
            (false, false) => "Child",
            (true, false) => "Child Progress",
            (false, true) => "Child ROI",
            _ => "Child Progress ROI"
        };
        var codeSuffix = (isProgress, isRoi) switch
        {
            (false, false) => "child",
            (true, false) => "child_progress",
            (false, true) => "child_roi",
            _ => "child_progress_roi"
        };
        var progressDescription = isProgress
            ? "including Progress strategies"
            : "excluding strategies whose name contains Progress";
        var metricName = isRoi
            ? "sample-adjusted paper ROI after minimum sample gates"
            : "positive paper PnL";

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{lookbackHours:000000000000}"),
            $"{assetCode}_up_down_5m_{lookbackHours}_{codeSuffix}",
            $"{normalizedAsset} Up or Down 5m {lookbackHours.ToString(CultureInfo.InvariantCulture)} {nameSuffix}",
            $"After all market-opening entries and database writes are complete, select the enabled non-Child, non-Futures {normalizedAsset} strategy with the highest {metricName} over the last {lookbackHours.ToString(CultureInfo.InvariantCulture)} hour(s), {progressDescription}. While the parent link is active, copy each accepted parent entry in the same market, outcome, notional, and share size.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            behavior,
            lookbackHours,
            Category: $"{normalizedAsset} Up/Down 5m {nameSuffix}",
            ReferenceAssetSymbol: normalizedAsset);
    }

    private static BtcUpDown5mStrategyVariant CreateFuturesBasisBpsThresholdPremarketVariant(
        string assetSymbol,
        int idGroup,
        int thresholdBps,
        BtcUpDown5mStrategyBehavior behavior,
        int entryDelaySeconds = -30)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var secondsBeforeOpen = Math.Abs(entryDelaySeconds);
        var thresholdName = thresholdBps.ToString(CultureInfo.InvariantCulture);
        var isRevert = behavior == BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarketRevert;
        var revertCodeSuffix = isRevert ? "_revert" : string.Empty;
        var revertNameSuffix = isRevert ? " Revert" : string.Empty;
        var directionDescription = isRevert
            ? "If the nearest futures mid is above the index by at least " + thresholdName + " bps and both following expiries have positive basis, BUY Down; if the nearest futures mid is below the index by at least " + thresholdName + " bps and both following expiries have negative basis, BUY Up."
            : "If the nearest futures mid is above the index by at least " + thresholdName + " bps and both following expiries have positive basis, BUY Up; if the nearest futures mid is below the index by at least " + thresholdName + " bps and both following expiries have negative basis, BUY Down.";

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{100 + thresholdBps:000000000000}"),
            $"{assetCode}_up_down_5m_futures_basis_bps_{thresholdName}{revertCodeSuffix}_fak_premarket",
            $"{normalizedAsset} Up or Down 5m {thresholdName} bps Futures Basis{revertNameSuffix} Premarket",
            $"{secondsBeforeOpen.ToString(CultureInfo.InvariantCulture)} seconds before {normalizedAsset} 5m market open, select the three live OKX linear USD fixed-expiry contracts with the closest distinct expiries at or after the target market end and compare each best bid/ask mid with the simultaneous OKX {normalizedAsset}-USD index. Apply the {thresholdName} bps threshold only to the nearest expiry; require both following expiries to confirm its nonzero basis sign. {directionDescription} Otherwise skip. Require all three fresh contracts and never substitute a perpetual contract. Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap, while Live-shadow remains disabled by default until manually enabled and normal live gates pass.",
            BtcUpDown5mStrategyDirection.Dynamic,
            entryDelaySeconds,
            behavior,
            thresholdBps,
            thresholdBps,
            Category: $"{normalizedAsset} Up/Down 5m Bps Futures Basis{revertNameSuffix} Premarket",
            ReferenceAssetSymbol: normalizedAsset);
    }

    private static BtcUpDown5mStrategyVariant CreateDiffCounterTrendFakPremarketVariant(
        string assetSymbol,
        int idGroup,
        int threshold,
        bool isUpDiffGroup,
        int entryDelaySeconds = -30,
        bool isRevert = false)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var diffGroupName = isUpDiffGroup ? "Up" : "Down";
        var diffGroupCode = isUpDiffGroup ? "up" : "down";
        var triggerOutcome = isUpDiffGroup ? BtcUpDownFixedOutcome.Up : BtcUpDownFixedOutcome.Down;
        var targetOutcome = isRevert ? triggerOutcome : GetOppositeFixedOutcome(triggerOutcome);
        var targetOutcomeName = targetOutcome.ToString();
        var revertCodeSuffix = isRevert ? "_revert" : string.Empty;
        var revertNameSuffix = isRevert ? " Revert" : string.Empty;
        var strategyKindName = isRevert ? "revert" : "countertrend";
        var diffExpression = isUpDiffGroup
            ? "UpCount - DownCount"
            : "DownCount - UpCount";
        var secondsBeforeOpen = Math.Abs(entryDelaySeconds);

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{threshold:000000000000}"),
            $"{assetCode}_up_down_5m_{diffGroupCode}_diff_{threshold}{revertCodeSuffix}_fak_premarket",
            $"{normalizedAsset} Up or Down 5m {diffGroupName} {threshold} Diff{revertNameSuffix} Premarket",
            $"{secondsBeforeOpen.ToString(CultureInfo.InvariantCulture)} seconds before {normalizedAsset} 5m market open, use the in-memory UTC-day raw {diffExpression} counter reset at 00:00 UTC. Diff {strategyKindName} strategy: if the absolute Diff side is at least {threshold}, BUY {targetOutcomeName} from the current premarket executable ask depth using the worst-price cap. Otherwise skip. Paper entry simulates the same taker BUY, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.",
            BtcUpDown5mStrategyDirection.Dynamic,
            entryDelaySeconds,
            BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket,
            threshold,
            FixedOutcome: targetOutcome,
            Category: $"{normalizedAsset} Up/Down 5m Diff {diffGroupName}{revertNameSuffix} Premarket",
            ReferenceAssetSymbol: normalizedAsset,
            DiffCounterTriggerOutcome: triggerOutcome);
    }

    private static BtcUpDown5mStrategyVariant CreateDiffProgressVariant(
        string assetSymbol,
        int idGroup,
        int threshold,
        bool isUpDiffGroup)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var diffGroupName = isUpDiffGroup ? "Up" : "Down";
        var diffGroupCode = isUpDiffGroup ? "up" : "down";
        var triggerOutcome = isUpDiffGroup ? BtcUpDownFixedOutcome.Up : BtcUpDownFixedOutcome.Down;
        var targetOutcome = GetOppositeFixedOutcome(triggerOutcome);
        var targetOutcomeName = targetOutcome.ToString();
        var diffExpression = isUpDiffGroup
            ? "UpCount - DownCount"
            : "DownCount - UpCount";
        var category = string.Equals(normalizedAsset, "BTC", StringComparison.OrdinalIgnoreCase)
            ? $"{normalizedAsset} Up/Down 5m Diff {diffGroupName} Progress"
            : $"{normalizedAsset} Up/Down 5m Diff Progress";

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{threshold:000000000000}"),
            $"{assetCode}_up_down_5m_diff_{threshold}_{diffGroupCode}_progress",
            $"{normalizedAsset} Up or Down 5m {threshold} Diff {diffGroupName} Progress",
            $"Diff Progress strategy: in waiting mode, count the in-memory UTC-day raw {diffExpression} counter reset at 00:00 UTC and backfilled from the current UTC day on service restart. When Diff is greater than {threshold}, switch to betting mode and submit BUY FAK Paper entries on {targetOutcomeName}; the effective stake multiplier is Diff minus {threshold}. While betting, the 00:00 UTC reset is postponed until Diff returns to the threshold and the strategy switches back to waiting mode.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.DiffProgress,
            threshold,
            FixedOutcome: targetOutcome,
            Category: category,
            ReferenceAssetSymbol: normalizedAsset,
            DiffCounterTriggerOutcome: triggerOutcome);
    }

    private static BtcUpDown5mStrategyVariant CreateDiffShiftProgressVariant(
        string assetSymbol,
        int idGroup,
        bool isUpDiffGroup)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var diffGroupName = isUpDiffGroup ? "Up" : "Down";
        var diffGroupCode = isUpDiffGroup ? "up" : "down";
        var triggerOutcome = isUpDiffGroup ? BtcUpDownFixedOutcome.Up : BtcUpDownFixedOutcome.Down;
        var targetOutcome = GetOppositeFixedOutcome(triggerOutcome);
        var targetOutcomeName = targetOutcome.ToString();
        var diffExpression = isUpDiffGroup
            ? "UpCount - DownCount"
            : "DownCount - UpCount";

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-000000000001"),
            $"{assetCode}_up_down_5m_diff_{diffGroupCode}_shift_progress",
            $"{normalizedAsset} Up or Down 5m Diff {diffGroupName} Shift Progress",
            $"Diff Shift Progress strategy: use the persistent raw {diffExpression} counter and persistent Sum. Unit is this strategy's Paper stake amount. When Diff is greater than 0, each FAK Paper BUY on {targetOutcomeName} uses multiplier Diff + 1 at the Diff instant max price cap; Diff 0 or below skips. When a previous bet wins, Sum increases by the filled stake; when it loses, Sum decreases by the filled stake. After each processed result, while Sum is greater than Unit and Diff is greater than 1, reduce Diff by 1 and subtract Unit from Sum.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.DiffShiftProgress,
            0,
            FixedOutcome: targetOutcome,
            Category: $"{normalizedAsset} Up/Down 5m Diff Shift Progress",
            ReferenceAssetSymbol: normalizedAsset,
            DiffCounterTriggerOutcome: triggerOutcome);
    }

    private static BtcUpDown5mStrategyVariant CreateDiffShiftProgressPremarketVariant(
        string assetSymbol,
        int idGroup,
        int threshold)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{threshold:000000000000}"),
            $"{assetCode}_up_down_5m_{threshold}_diff_shift_progress_premarket",
            $"{normalizedAsset} Up or Down 5m {threshold} Diff Shift Progress Premarket",
            $"30 seconds before {normalizedAsset} 5m market open, use the persistent raw UpCount - DownCount counter and persistent Sum. Results before the latest 5-minute market use resolved market results; the latest market result is synthesized from the current {normalizedAsset} reference price. When Diff is greater than 0, BUY Down; when Diff is less than 0, BUY Up; Diff 0 skips. Unit is this strategy's Paper stake amount, and each FAK Paper BUY uses multiplier abs(Diff) at the Diff instant max price cap. When abs(Diff) reaches {threshold.ToString(CultureInfo.InvariantCulture)}, enter damping mode, reset Sum, then move Diff one step toward 0 each time Sum becomes greater than Unit. When Diff returns to 0, return to simple mode.",
            BtcUpDown5mStrategyDirection.Dynamic,
            -30,
            BtcUpDown5mStrategyBehavior.DiffShiftProgress,
            threshold,
            Category: $"{normalizedAsset} Up/Down 5m Diff Shift Progress Premarket",
            ReferenceAssetSymbol: normalizedAsset);
    }

    private static BtcUpDown5mStrategyVariant CreateDiffLimitProgressPremarketVariant(
        string assetSymbol,
        int idGroup,
        int limit)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var limitText = limit.ToString(CultureInfo.InvariantCulture);

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{limit:000000000000}"),
            $"{assetCode}_up_down_5m_{limit}_diff_limit_progress_premarket",
            $"{normalizedAsset} Up or Down 5m {limitText} Diff Limit Progress Premarket",
            $"30 seconds before {normalizedAsset} 5m market open, use persistent UTC-day UpCount, DownCount, and Sum. Counts reset at 00:00 UTC. Results before the latest 5-minute market use resolved market results; the latest market result is synthesized from the current {normalizedAsset} reference price. Diff is UpCount - DownCount: Diff > 0 buys Down, Diff < 0 buys Up, and Diff 0 skips. Unit is this strategy's Paper stake amount, and each BUY FAK Paper entry uses multiplier min(abs(Diff), {limitText}) at the Diff instant max price cap.",
            BtcUpDown5mStrategyDirection.Dynamic,
            -30,
            BtcUpDown5mStrategyBehavior.DiffLimitProgressPremarket,
            limit,
            Category: $"{normalizedAsset} Up/Down 5m Diff Limit Progress",
            ReferenceAssetSymbol: normalizedAsset);
    }

    private static BtcUpDown5mStrategyVariant CreateDiffRealLimitProgressPremarketVariant(
        string assetSymbol,
        int idGroup,
        int limit)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var limitText = limit.ToString(CultureInfo.InvariantCulture);

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{limit:000000000000}"),
            $"{assetCode}_up_down_5m_{limit}_diff_real_limit_progress_premarket",
            $"{normalizedAsset} Up or Down 5m {limitText} Diff Real Limit Progress Premarket",
            $"30 seconds before {normalizedAsset} 5m market open, use persistent UTC-day UpCount, DownCount, and Sum. Counts reset at 00:00 UTC. Results before the latest 5-minute market use resolved market results; the latest market result is synthesized from the current {normalizedAsset} reference price. Diff is UpCount - DownCount: Diff > 0 buys Down, Diff < 0 buys Up, and Diff 0 skips. UpCount and DownCount stop changing when the next result would move Diff outside [-{limitText}, {limitText}], while opposite results can move Diff back inside the range. Unit is this strategy's Paper stake amount, and each BUY FAK Paper entry uses multiplier abs(Diff) at the Diff instant max price cap.",
            BtcUpDown5mStrategyDirection.Dynamic,
            -30,
            BtcUpDown5mStrategyBehavior.DiffRealLimitProgressPremarket,
            limit,
            Category: $"{normalizedAsset} Up/Down 5m Diff Real Limit Progress",
            ReferenceAssetSymbol: normalizedAsset);
    }

    private static BtcUpDown5mStrategyVariant CreateDiffReferenceAveragePremarketVariant(
        string assetSymbol,
        int idGroup,
        int threshold)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var thresholdText = threshold.ToString(CultureInfo.InvariantCulture);

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{threshold:000000000000}"),
            $"{assetCode}_up_down_5m_{thresholdText}_diff_reference_average_premarket",
            $"{normalizedAsset} Up or Down 5m {thresholdText} Diff Reference Average Premarket",
            $"30 seconds before {normalizedAsset} 5m market open, compute the rolling 24-hour raw Diff = UpCount - DownCount without a UTC-day reset. Results before the latest 5-minute market use resolved market results; the latest market result is synthesized from the current {normalizedAsset} reference price. Average Diff is calculated over full 24h, 12h, 6h, 3h, 90m, and 45m windows, then the average farthest from zero is selected. If current Diff minus that selected Average Diff is at least {thresholdText}, BUY Down; if it is at most -{thresholdText}, BUY Up; otherwise skip. Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.",
            BtcUpDown5mStrategyDirection.Dynamic,
            -30,
            BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket,
            threshold,
            threshold,
            Category: $"{normalizedAsset} Up/Down 5m Diff Reference Average Premarket",
            ReferenceAssetSymbol: normalizedAsset);
    }

    private static BtcUpDown5mStrategyVariant CreateBpsConfirmedAveragePremarketVariant(
        string assetSymbol,
        int idGroup,
        int thresholdBps,
        BtcUpDown5mStrategyVariant baseVariant,
        BtcUpDown5mStrategyVariant confirmationVariant)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var thresholdText = thresholdBps.ToString(CultureInfo.InvariantCulture);
        var confirmationThresholdText = confirmationVariant.DecisionDepth.ToString(CultureInfo.InvariantCulture);

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{100 + thresholdBps:000000000000}"),
            $"{assetCode}_up_down_5m_{thresholdText}_bps_confirmed_average_premarket",
            $"{normalizedAsset} Up or Down 5m {thresholdText} bps Confirmed Average Premarket",
            $"30 seconds before {normalizedAsset} 5m market open, evaluate the exact {baseVariant.Name} signal and independently evaluate the exact {confirmationVariant.Name} signal. Enter only when both signals are present and select the same outcome; otherwise skip. The Bps signal keeps threshold {thresholdText}, while the confirming Diff Reference Average threshold is {confirmationThresholdText}. Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.",
            BtcUpDown5mStrategyDirection.Dynamic,
            -30,
            BtcUpDown5mStrategyBehavior.BpsConfirmedAveragePremarket,
            thresholdBps,
            thresholdBps,
            Category: $"{normalizedAsset} Up/Down 5m Bps Confirmed Average Premarket",
            ReferenceAssetSymbol: normalizedAsset,
            BaseSignalStrategyId: baseVariant.Id,
            ConfirmationSignalStrategyId: confirmationVariant.Id);
    }

    private static BtcUpDown5mStrategyVariant CreateDiffConfirmedAveragePremarketVariant(
        string assetSymbol,
        int idGroup,
        int threshold,
        BtcUpDown5mStrategyVariant baseVariant,
        BtcUpDown5mStrategyVariant confirmationVariant)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var thresholdText = threshold.ToString(CultureInfo.InvariantCulture);
        var confirmationThresholdText = confirmationVariant.DecisionThresholdBps.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{threshold:000000000000}"),
            $"{assetCode}_up_down_5m_{thresholdText}_diff_confirmed_average_premarket",
            $"{normalizedAsset} Up or Down 5m {thresholdText} Diff Confirmed Average Premarket",
            $"30 seconds before {normalizedAsset} 5m market open, evaluate the exact {baseVariant.Name} signal and independently evaluate the exact {confirmationVariant.Name} signal. Enter only when both signals are present and select the same outcome; otherwise skip. The Diff Reference Average signal keeps threshold {thresholdText}, while the confirming Bps Reference Average threshold is {confirmationThresholdText}. Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.",
            BtcUpDown5mStrategyDirection.Dynamic,
            -30,
            BtcUpDown5mStrategyBehavior.DiffConfirmedAveragePremarket,
            threshold,
            threshold,
            Category: $"{normalizedAsset} Up/Down 5m Diff Confirmed Average Premarket",
            ReferenceAssetSymbol: normalizedAsset,
            BaseSignalStrategyId: baseVariant.Id,
            ConfirmationSignalStrategyId: confirmationVariant.Id);
    }

    private static BtcUpDownFixedOutcome GetOppositeFixedOutcome(BtcUpDownFixedOutcome outcome)
    {
        return outcome == BtcUpDownFixedOutcome.Up
            ? BtcUpDownFixedOutcome.Down
            : BtcUpDownFixedOutcome.Up;
    }

    private static BtcUpDown5mStrategyVariant CreateReferenceAverageBpsThresholdFakPremarketVariant(
        string assetSymbol,
        int idGroup,
        int thresholdBps,
        bool isUpTrigger,
        int entryDelaySeconds = -30)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var triggerOutcome = isUpTrigger ? BtcUpDownFixedOutcome.Up : BtcUpDownFixedOutcome.Down;
        var targetOutcome = GetOppositeFixedOutcome(triggerOutcome);
        var triggerName = triggerOutcome.ToString();
        var triggerCode = triggerName.ToLowerInvariant();
        var targetOutcomeName = targetOutcome.ToString();
        var secondsBeforeOpen = Math.Abs(entryDelaySeconds);
        var thresholdName = thresholdBps.ToString(CultureInfo.InvariantCulture);
        var useReferenceAverageCodeMarker =
            string.Equals(normalizedAsset, "ETH", StringComparison.OrdinalIgnoreCase) &&
            triggerOutcome == BtcUpDownFixedOutcome.Down;
        var codeMarker = useReferenceAverageCodeMarker ? "_reference_average" : string.Empty;

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{100 + thresholdBps:000000000000}"),
            $"{assetCode}_up_down_5m_{triggerCode}{codeMarker}_bps_{thresholdName}_fak_premarket",
            $"{normalizedAsset} Up or Down 5m {triggerName} {thresholdName} bps Reference Average Premarket",
            $"{secondsBeforeOpen.ToString(CultureInfo.InvariantCulture)} seconds before {normalizedAsset} 5m market open, compare the latest Binance {normalizedAsset}/USDT reference price with the largest full in-memory reference average across 24h, 12h, 6h, 3h, 90m, 45m, 20m, and 10m windows. If the current price moves {triggerName} by at least {thresholdName} bps from that maximum average, BUY {targetOutcomeName} from current premarket executable ask depth using the worst-price cap. Otherwise skip. Paper entry simulates the same taker BUY, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.",
            BtcUpDown5mStrategyDirection.Dynamic,
            entryDelaySeconds,
            BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket,
            thresholdBps,
            thresholdBps,
            ReferenceAssetSymbol: normalizedAsset,
            FixedOutcome: targetOutcome,
            Category: $"{normalizedAsset} Up/Down 5m {triggerName} Bps Reference Average Premarket",
            DiffCounterTriggerOutcome: triggerOutcome);
    }

    private static BtcUpDown5mStrategyVariant CreateReferenceAverageBpsThresholdNeutralFakPremarketVariant(
        string assetSymbol,
        int idGroup,
        int thresholdBps,
        int entryDelaySeconds = -30)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        var assetCode = normalizedAsset.ToLowerInvariant();
        var secondsBeforeOpen = Math.Abs(entryDelaySeconds);
        var thresholdName = thresholdBps.ToString(CultureInfo.InvariantCulture);

        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{100 + thresholdBps:000000000000}"),
            $"{assetCode}_up_down_5m_reference_average_bps_{thresholdName}_fak_premarket",
            $"{normalizedAsset} Up or Down 5m {thresholdName} bps Reference Average Premarket",
            $"{secondsBeforeOpen.ToString(CultureInfo.InvariantCulture)} seconds before {normalizedAsset} 5m market open, compare the latest Binance {normalizedAsset}/USDT reference price with the largest full in-memory reference average across 24h, 12h, 6h, 3h, 90m, 45m, 20m, and 10m windows. If the current price is above that maximum average by at least {thresholdName} bps, BUY Down; if it is below that maximum average by at least {thresholdName} bps, BUY Up. Otherwise skip. Paper entry simulates the same taker BUY, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.",
            BtcUpDown5mStrategyDirection.Dynamic,
            entryDelaySeconds,
            BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket,
            thresholdBps,
            thresholdBps,
            ReferenceAssetSymbol: normalizedAsset,
            Category: $"{normalizedAsset} Up/Down 5m Bps Reference Average Premarket");
    }

    private static int GetReferenceAverageBpsPremarketIdGroup(string assetSymbol, bool isUpTrigger)
    {
        var normalizedAsset = assetSymbol.ToUpperInvariant();
        return normalizedAsset switch
        {
            "ETH" => isUpTrigger ? 8137 : 8140,
            "SOL" => isUpTrigger ? 8138 : 8139,
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported reference-average Premarket asset.")
        };
    }

    private static int GetReferenceAverageBpsNeutralPremarketIdGroup(string assetSymbol)
    {
        return assetSymbol.ToUpperInvariant() switch
        {
            "BTC" => 8178,
            "ETH" => 8179,
            "SOL" => 8180,
            _ => throw new ArgumentOutOfRangeException(nameof(assetSymbol), assetSymbol, "Unsupported neutral reference-average Premarket asset.")
        };
    }

    private static BtcUpDown5mStrategyVariant CreateCryptoUpDown5mMiddleVariant(
        CryptoUpDown5mAssetSpec asset,
        int depth)
    {
        var sampleDescription = GetCryptoMiddleSampleDescription(asset.Symbol, depth);
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{asset.MiddleIdGroup:0000}-000000000{GetMiddleReferenceDepthIdSuffix(depth):000}"),
            $"{asset.Symbol.ToLowerInvariant()}_up_down_5m_middle_{depth}",
            $"{asset.Symbol} Up or Down 5m Middle {depth}",
            $"Immediately after {asset.Symbol} 5m market open, compare the latest Binance {asset.Symbol}/USDT trade-stream price against the arithmetic mean of {sampleDescription}; above mean buys Down, below mean buys Up, otherwise skip until all {depth} sampled prices are available. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReference,
            depth,
            ReferenceAssetSymbol: asset.Symbol);
    }

    private static BtcUpDown5mStrategyVariant CreateCryptoUpDown5mMiddleBpsThresholdVariant(
        CryptoUpDown5mAssetSpec asset,
        int depth,
        decimal thresholdBps)
    {
        var sampleDescription = GetCryptoMiddleSampleDescription(asset.Symbol, depth);
        var thresholdName = thresholdBps.ToString("0.#", CultureInfo.InvariantCulture);
        var thresholdId = (int)thresholdBps;
        var idSuffix = GetMiddleReferenceBpsIdSuffix(depth, thresholdId);
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{asset.MiddleIdGroup:0000}-{idSuffix:000000000000}"),
            $"{asset.Symbol.ToLowerInvariant()}_up_down_5m_middle_{depth}_bps_{thresholdId}",
            $"{asset.Symbol} Up or Down 5m Middle {depth} {thresholdName} bps",
            $"Immediately after {asset.Symbol} 5m market open, compare the latest Binance {asset.Symbol}/USDT trade-stream price against the arithmetic mean of {sampleDescription}; above mean buys Down, below mean buys Up, otherwise skip until all {depth} sampled prices are available. Enter only when the current price is at least {thresholdName} bps away from the mean. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReference,
            depth,
            thresholdBps,
            ReferenceAssetSymbol: asset.Symbol);
    }

    private static BtcUpDown5mStrategyVariant CreateCryptoUpDown5mMiddleBpsThresholdInstantVariant(
        CryptoUpDown5mAssetSpec asset,
        int depth,
        decimal thresholdBps)
    {
        var sampleDescription = GetCryptoMiddleSampleDescription(asset.Symbol, depth);
        var thresholdName = thresholdBps.ToString("0.#", CultureInfo.InvariantCulture);
        var thresholdId = (int)thresholdBps;
        var idSuffix = GetMiddleReferenceBpsIdSuffix(depth, thresholdId);
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{asset.MiddleInstantIdGroup:0000}-{idSuffix:000000000000}"),
            $"{asset.Symbol.ToLowerInvariant()}_up_down_5m_middle_{depth}_bps_{thresholdId}_instant",
            $"{asset.Symbol} Up or Down 5m Middle {depth} {thresholdName} bps Instant",
            $"Immediately after {asset.Symbol} 5m market open, compare the latest Binance {asset.Symbol}/USDT trade-stream price against the arithmetic mean of {sampleDescription}; above mean buys Down, below mean buys Up, otherwise skip until all {depth} sampled prices are available. Enter only when the current price is at least {thresholdName} bps away from the mean. Paper entry simulates a BUY FAK taker fill from current executable ask depth; available liquidity is taken immediately, any remainder is cancelled, and settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReferenceInstant,
            depth,
            thresholdBps,
            ReferenceAssetSymbol: asset.Symbol);
    }

    private static BtcUpDown5mStrategyVariant CreateCryptoUpDown5mMiddleRevertVariant(
        CryptoUpDown5mAssetSpec asset,
        int depth)
    {
        var sampleDescription = GetCryptoMiddleSampleDescription(asset.Symbol, depth);
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{asset.MiddleRevertIdGroup:0000}-000000000{GetMiddleReferenceDepthIdSuffix(depth):000}"),
            $"{asset.Symbol.ToLowerInvariant()}_up_down_5m_middle_{depth}_revert",
            $"{asset.Symbol} Up or Down 5m Middle {depth} Revert",
            $"Immediately after {asset.Symbol} 5m market open, compare the latest Binance {asset.Symbol}/USDT trade-stream price against the arithmetic mean of {sampleDescription}, then invert the standard Middle {depth} decision; above mean buys Up, below mean buys Down, otherwise skip until all {depth} sampled prices are available. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevert,
            depth,
            ReferenceAssetSymbol: asset.Symbol);
    }

    private static BtcUpDown5mStrategyVariant CreateCryptoUpDown5mMiddleRevertBpsThresholdVariant(
        CryptoUpDown5mAssetSpec asset,
        int depth,
        decimal thresholdBps)
    {
        var sampleDescription = GetCryptoMiddleSampleDescription(asset.Symbol, depth);
        var thresholdName = thresholdBps.ToString("0.#", CultureInfo.InvariantCulture);
        var thresholdId = (int)thresholdBps;
        var idSuffix = GetMiddleReferenceBpsIdSuffix(depth, thresholdId);
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{asset.MiddleRevertIdGroup:0000}-{idSuffix:000000000000}"),
            $"{asset.Symbol.ToLowerInvariant()}_up_down_5m_middle_{depth}_revert_bps_{thresholdId}",
            $"{asset.Symbol} Up or Down 5m Middle {depth} Revert {thresholdName} bps",
            $"Immediately after {asset.Symbol} 5m market open, compare the latest Binance {asset.Symbol}/USDT trade-stream price against the arithmetic mean of {sampleDescription}, then invert the standard Middle {depth} decision; above mean buys Up, below mean buys Down, otherwise skip until all {depth} sampled prices are available. Enter only when the current price is at least {thresholdName} bps away from the mean. Paper entry is a GTD limit BUY with dynamic break-even pricing; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevert,
            depth,
            thresholdBps,
            ReferenceAssetSymbol: asset.Symbol);
    }

    private static BtcUpDown5mStrategyVariant CreateCryptoUpDown5mMiddleRevertBpsThresholdInstantVariant(
        CryptoUpDown5mAssetSpec asset,
        int depth,
        decimal thresholdBps)
    {
        var sampleDescription = GetCryptoMiddleSampleDescription(asset.Symbol, depth);
        var thresholdName = thresholdBps.ToString("0.#", CultureInfo.InvariantCulture);
        var thresholdId = (int)thresholdBps;
        var idSuffix = GetMiddleReferenceBpsIdSuffix(depth, thresholdId);
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{asset.MiddleRevertInstantIdGroup:0000}-{idSuffix:000000000000}"),
            $"{asset.Symbol.ToLowerInvariant()}_up_down_5m_middle_{depth}_revert_bps_{thresholdId}_instant",
            $"{asset.Symbol} Up or Down 5m Middle {depth} Revert {thresholdName} bps Instant",
            $"Immediately after {asset.Symbol} 5m market open, compare the latest Binance {asset.Symbol}/USDT trade-stream price against the arithmetic mean of {sampleDescription}, then invert the standard Middle {depth} decision; above mean buys Up, below mean buys Down, otherwise skip until all {depth} sampled prices are available. Enter only when the current price is at least {thresholdName} bps away from the mean. Paper entry simulates a BUY FAK taker fill from current executable ask depth; available liquidity is taken immediately, any remainder is cancelled, and settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant,
            depth,
            thresholdBps,
            ReferenceAssetSymbol: asset.Symbol);
    }

    private static string GetCryptoMiddleSampleDescription(string assetSymbol, int depth)
    {
        return GetMiddleReferenceMeanDescription(assetSymbol, depth);
    }

    private static BtcUpDown5mStrategyVariant CreateCryptoUpDown5mBinanceBpsThresholdVariant(
        CryptoUpDown5mAssetSpec asset,
        int thresholdTenths,
        decimal minMoveBps)
    {
        var thresholdName = minMoveBps.ToString("0.###", CultureInfo.InvariantCulture);
        return new BtcUpDown5mStrategyVariant(
            GetCryptoUpDown5mBinanceBpsThresholdId(asset.BpsIdGroup, thresholdTenths),
            GetCryptoUpDown5mBinanceBpsThresholdCode(asset.Symbol, thresholdTenths),
            $"{asset.Symbol} Up or Down 5m Binance {thresholdName} bps",
            $"After {asset.Symbol} 5m trading starts, compare the latest Binance {asset.Symbol}/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least {thresholdName} bps; above start buys Up, below start buys Down. Paper entry is a GTD limit BUY capped at 0.50 until the configured GTD deadline; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThreshold,
            minMoveBps >= 1m && minMoveBps == decimal.Truncate(minMoveBps)
                ? (int)minMoveBps
                : 0,
            minMoveBps,
            ReferenceAssetSymbol: asset.Symbol);
    }

    private static BtcUpDown5mStrategyVariant CreateCryptoUpDown5mBinanceBpsThresholdInstantVariant(
        CryptoUpDown5mAssetSpec asset,
        int thresholdTenths,
        decimal minMoveBps)
    {
        var thresholdName = minMoveBps.ToString("0.###", CultureInfo.InvariantCulture);
        return new BtcUpDown5mStrategyVariant(
            GetCryptoUpDown5mBinanceBpsThresholdId(asset.InstantIdGroup, thresholdTenths),
            GetCryptoUpDown5mBinanceBpsThresholdCode(asset.Symbol, thresholdTenths) + "_instant",
            $"{asset.Symbol} Up or Down 5m Binance {thresholdName} bps Instant",
            $"After {asset.Symbol} 5m trading starts, compare the latest Binance {asset.Symbol}/USDT trade-stream price with the archived market-start reference; skip unless the absolute move from start is at least {thresholdName} bps; above start buys Up, below start buys Down. Paper entry simulates a BUY FAK taker fill from current executable ask depth; available liquidity is taken immediately, any remainder is cancelled, and settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThresholdInstant,
            minMoveBps >= 1m && minMoveBps == decimal.Truncate(minMoveBps)
                ? (int)minMoveBps
                : 0,
            minMoveBps,
            ReferenceAssetSymbol: asset.Symbol);
    }

    private static BtcUpDown5mStrategyVariant CreateCryptoUpDown5mFixedOutcomeBpsThresholdInstantVariant(
        CryptoUpDown5mAssetSpec asset,
        int thresholdTenths,
        decimal minMoveBps,
        bool isUp,
        BtcUpDownMarketInterval marketInterval = BtcUpDownMarketInterval.FiveMinutes)
    {
        var thresholdName = minMoveBps.ToString("0.###", CultureInfo.InvariantCulture);
        var directionName = isUp ? "Up" : "Down";
        var oppositeDirectionName = isUp ? "Down" : "Up";
        var intervalName = GetUpDownIntervalName(marketInterval);
        var idGroup = marketInterval switch
        {
            BtcUpDownMarketInterval.FiveMinutes => isUp
                ? asset.FixedOutcomeUpBpsInstantIdGroup
                : asset.FixedOutcomeDownBpsInstantIdGroup,
            BtcUpDownMarketInterval.FifteenMinutes => isUp
                ? asset.FifteenMinuteFixedOutcomeUpBpsInstantIdGroup
                : asset.FifteenMinuteFixedOutcomeDownBpsInstantIdGroup,
            _ => throw new ArgumentOutOfRangeException(nameof(marketInterval), marketInterval, "Unsupported crypto fixed bps Instant interval.")
        };
        return new BtcUpDown5mStrategyVariant(
            GetCryptoUpDown5mBinanceBpsThresholdId(idGroup, thresholdTenths),
            GetCryptoUpDown5mFixedOutcomeBpsThresholdInstantCode(asset.Symbol, thresholdTenths, isUp, marketInterval),
            $"{asset.Symbol} Up or Down {intervalName} {directionName} {thresholdName} bps Instant",
            $"Immediately after {asset.Symbol} {intervalName} market open, use the previous {asset.Symbol} {intervalName} close-book result streak and archived Binance {asset.Symbol} start/end move gate; enter only when the cumulative streak move is at least {thresholdName} bps and the countertrend direction is {directionName}. If the countertrend direction is {oppositeDirectionName}, skip. Paper entry simulates a BUY FAK taker fill from current executable ask depth; available liquidity is taken immediately, any remainder is cancelled, and settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant,
            minMoveBps >= 1m && minMoveBps == decimal.Truncate(minMoveBps)
                ? (int)minMoveBps
                : 0,
            minMoveBps,
            MarketInterval: marketInterval,
            ReferenceAssetSymbol: asset.Symbol,
            FixedOutcome: isUp ? BtcUpDownFixedOutcome.Up : BtcUpDownFixedOutcome.Down);
    }

    private static BtcUpDown5mStrategyVariant CreateCryptoUpDown5mFixedOutcomeBpsThresholdFakVariant(
        CryptoUpDown5mAssetSpec asset,
        int thresholdTenths,
        decimal minMoveBps,
        bool isUp)
    {
        var thresholdName = minMoveBps.ToString("0.###", CultureInfo.InvariantCulture);
        var directionName = isUp ? "Up" : "Down";
        var oppositeDirectionName = isUp ? "Down" : "Up";
        return new BtcUpDown5mStrategyVariant(
            GetCryptoUpDown5mBinanceBpsThresholdId(8130, thresholdTenths),
            GetCryptoUpDown5mFixedOutcomeBpsThresholdFakCode(asset.Symbol, thresholdTenths, isUp),
            $"{asset.Symbol} Up or Down 5m {directionName} {thresholdName} bps",
            $"Immediately after {asset.Symbol} 5m market open, use the previous {asset.Symbol} 5m close-book result streak and archived Binance {asset.Symbol} start/end move gate; enter only when the cumulative streak move is at least {thresholdName} bps and the countertrend direction is {directionName}. If the countertrend direction is {oppositeDirectionName}, skip. Paper entry simulates the same taker BUY from executable ask depth using the worst-price cap, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFak,
            minMoveBps >= 1m && minMoveBps == decimal.Truncate(minMoveBps)
                ? (int)minMoveBps
                : 0,
            minMoveBps,
            ReferenceAssetSymbol: asset.Symbol,
            FixedOutcome: isUp ? BtcUpDownFixedOutcome.Up : BtcUpDownFixedOutcome.Down);
    }

    private static BtcUpDown5mStrategyVariant CreateCryptoUpDown5mFixedOutcomeBpsThresholdFakPremarketVariant(
        CryptoUpDown5mAssetSpec asset,
        int thresholdTenths,
        decimal minMoveBps,
        bool isUp,
        int idGroup = 8131,
        int entryDelaySeconds = -30)
    {
        var thresholdName = minMoveBps.ToString("0.###", CultureInfo.InvariantCulture);
        var directionName = isUp ? "Up" : "Down";
        var oppositeDirectionName = isUp ? "Down" : "Up";
        var secondsBeforeOpen = Math.Abs(entryDelaySeconds);
        var timeSuffix = entryDelaySeconds == -30 ? string.Empty : " -" + secondsBeforeOpen.ToString(CultureInfo.InvariantCulture) + "s";
        var codeSuffix = entryDelaySeconds == -30 ? "_premarket" : "_premarket_m" + secondsBeforeOpen.ToString(CultureInfo.InvariantCulture) + "s";
        return new BtcUpDown5mStrategyVariant(
            GetCryptoUpDown5mBinanceBpsThresholdId(idGroup, thresholdTenths),
            GetCryptoUpDown5mFixedOutcomeBpsThresholdFakCode(asset.Symbol, thresholdTenths, isUp) + codeSuffix,
            $"{asset.Symbol} Up or Down 5m {directionName} {thresholdName} bps Premarket{timeSuffix}",
            $"{secondsBeforeOpen.ToString(CultureInfo.InvariantCulture)} seconds before {asset.Symbol} 5m market open, infer the previous {asset.Symbol} 5m market result from archived Binance {asset.Symbol} start price versus the current reference price sampled {secondsBeforeOpen.ToString(CultureInfo.InvariantCulture)} seconds before previous market close; enter only when the inferred countertrend direction is {directionName} and the absolute move is at least {thresholdName} bps. If the inferred countertrend direction is {oppositeDirectionName}, skip. Paper entry simulates the same taker BUY from executable ask depth using the current premarket order book and worst-price cap, while Live-shadow submits a market BUY amount so available liquidity is taken immediately and any remainder is cancelled.",
            BtcUpDown5mStrategyDirection.Dynamic,
            entryDelaySeconds,
            BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket,
            minMoveBps >= 1m && minMoveBps == decimal.Truncate(minMoveBps)
                ? (int)minMoveBps
                : 0,
            minMoveBps,
            ReferenceAssetSymbol: asset.Symbol,
            FixedOutcome: isUp ? BtcUpDownFixedOutcome.Up : BtcUpDownFixedOutcome.Down);
    }

    private static IReadOnlyList<EthDownFakPremarketBattleSpec> CreateEthDownFakPremarketBattleSpecs()
    {
        return
        [
            new(8132, -10, [40, 41, 42]),
            new(8133, -5, [30, 31, 32, 33, 34, 35, 36, 37, 38])
        ];
    }

    private static Guid GetCryptoUpDown5mBinanceBpsThresholdId(int idGroup, int thresholdTenths)
    {
        return Guid.Parse($"b7c50005-0000-4000-{idGroup:0000}-{100 + thresholdTenths:000000000000}");
    }

    private static string GetCryptoUpDown5mBinanceBpsThresholdCode(string assetSymbol, int thresholdTenths)
    {
        return assetSymbol.ToLowerInvariant() + "_up_down_5m_binance_bps_" + thresholdTenths.ToString(CultureInfo.InvariantCulture);
    }

    private static string GetCryptoUpDown5mFixedOutcomeBpsThresholdInstantCode(
        string assetSymbol,
        int thresholdTenths,
        bool isUp,
        BtcUpDownMarketInterval marketInterval = BtcUpDownMarketInterval.FiveMinutes)
    {
        var directionCode = isUp ? "up" : "down";
        return assetSymbol.ToLowerInvariant() + "_up_down_" + GetUpDownIntervalCode(marketInterval) + "_" + directionCode + "_bps_" + thresholdTenths.ToString(CultureInfo.InvariantCulture) + "_instant";
    }

    private static string GetCryptoUpDown5mFixedOutcomeBpsThresholdFakCode(
        string assetSymbol,
        int thresholdTenths,
        bool isUp)
    {
        var directionCode = isUp ? "up" : "down";
        return assetSymbol.ToLowerInvariant() + "_up_down_5m_" + directionCode + "_bps_" + thresholdTenths.ToString(CultureInfo.InvariantCulture) + "_fak";
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
            $"Bet the configured Paper stake multiplier on the {directionDescription} BTC 5m outcome {entryDelaySeconds} seconds after window start using a GTD limit BUY at {maxEntryPrice.ToString("0.00", CultureInfo.InvariantCulture)} until the configured BTC GTD deadline.",
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
            $"Experimental Paper-only comparison strategy: choose the {directionDescription} BTC 5m outcome from Gamma outcomePrices {entryDelaySeconds} seconds after window start, then place a GTD limit BUY at {maxEntryPrice.ToString("0.00", CultureInfo.InvariantCulture)} until the configured BTC GTD deadline.",
            direction,
            entryDelaySeconds,
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelectionEntryPriceCap,
            maxEntryPriceCents);
    }

    private static IReadOnlyList<BtcUpDown5mStrategyVariant> CreateBtcPreOpenFixedDirectionVariants()
    {
        BtcPreOpenIntervalSpec[] intervals =
        [
            new(BtcUpDownMarketInterval.FiveMinutes, "5m", "5m", "5-minute", 1),
            new(BtcUpDownMarketInterval.FifteenMinutes, "15m", "15m", "15-minute", 2),
            new(BtcUpDownMarketInterval.OneHour, "1h", "1h", "hourly", 3),
            new(BtcUpDownMarketInterval.FourHours, "4h", "4h", "4-hour", 4)
        ];

        BtcPreOpenLifetimeSpec[] lifetimes =
        [
            new(BtcUpDownPreOpenLifetimeMode.HalfPeriod, "half", "Half", "until the half-period local cancel deadline", 1)
        ];

        BtcPreOpenOutcomeSpec[] outcomes =
        [
            new(BtcUpDownFixedOutcome.Up, "up", "Up", 1),
            new(BtcUpDownFixedOutcome.Down, "down", "Down", 2)
        ];

        var variants = new List<BtcUpDown5mStrategyVariant>(intervals.Length * lifetimes.Length * outcomes.Length * 40);
        foreach (var interval in intervals)
        {
            foreach (var lifetime in lifetimes)
            {
                foreach (var outcome in outcomes)
                {
                    for (var priceCents = 49; priceCents >= 10; priceCents--)
                    {
                        variants.Add(CreateBtcPreOpenFixedDirectionVariant(
                            interval,
                            lifetime,
                            outcome,
                            priceCents,
                            hasSellExit: false));
                    }
                }
            }
        }

        return variants;
    }

    private static BtcUpDown5mStrategyVariant CreateBtcPreOpenFixedDirectionVariant(
        BtcPreOpenIntervalSpec interval,
        BtcPreOpenLifetimeSpec lifetime,
        BtcPreOpenOutcomeSpec outcome,
        int limitPriceCents,
        bool hasSellExit)
    {
        var limitPrice = limitPriceCents / 100m;
        var sellSuffix = hasSellExit ? " Sell" : string.Empty;
        var category = $"BTC Up/Down {interval.Name} PreOpen {lifetime.Name}{sellSuffix}";
        var idLifetimeSuffix = hasSellExit ? 1 : lifetime.IdSuffix;
        var entryLifetimeDescription = hasSellExit
            ? "without a pre-close local cancel deadline"
            : lifetime.Description;
        return new BtcUpDown5mStrategyVariant(
            Guid.Parse($"b7c50005-0000-4000-{(hasSellExit ? "804" : "803")}{interval.IdSuffix}-0000000{idLifetimeSuffix}{outcome.IdSuffix}{limitPriceCents:000}"),
            $"btc_up_down_{interval.Code}_preopen_{lifetime.Code}_{outcome.Code}_{limitPriceCents}{(hasSellExit ? "_sell" : string.Empty)}",
            $"BTC Up or Down {interval.Name} PreOpen {lifetime.Name} {outcome.Name} {limitPriceCents}{sellSuffix}",
            hasSellExit
                ? $"Five minutes before the BTC {interval.Description} market opens, always place a Paper GTD limit BUY on {outcome.Name} at {limitPrice.ToString("0.00", CultureInfo.InvariantCulture)} and keep it {entryLifetimeDescription}; during the final quarter of the market, place a Paper SELL on filled shares if the current market direction no longer matches {outcome.Name}."
                : $"Five minutes before the BTC {interval.Description} market opens, always place a Paper GTD limit BUY on {outcome.Name} at {limitPrice.ToString("0.00", CultureInfo.InvariantCulture)} and keep it {lifetime.Description}; settlement uses only actually filled shares.",
            BtcUpDown5mStrategyDirection.Dynamic,
            -300,
            hasSellExit
                ? BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell
                : BtcUpDown5mStrategyBehavior.PreOpenFixedDirection,
            limitPriceCents,
            null,
            interval.Interval,
            lifetime.Mode,
            outcome.Outcome,
            limitPrice,
            category);
    }

    private sealed record BtcPreOpenIntervalSpec(
        BtcUpDownMarketInterval Interval,
        string Code,
        string Name,
        string Description,
        int IdSuffix);

    private sealed record BtcPreOpenLifetimeSpec(
        BtcUpDownPreOpenLifetimeMode Mode,
        string Code,
        string Name,
        string Description,
        int IdSuffix);

    private sealed record BtcPreOpenOutcomeSpec(
        BtcUpDownFixedOutcome Outcome,
        string Code,
        string Name,
        int IdSuffix);
}

public enum BtcUpDown5mStrategyDirection
{
    Less,
    More,
    Dynamic
}

public enum BtcUpDownMarketInterval
{
    FiveMinutes,
    FifteenMinutes,
    OneHour,
    FourHours
}

public enum BtcUpDownPreOpenLifetimeMode
{
    Default,
    HalfPeriod,
    FullPeriod
}

public enum BtcUpDownFixedOutcome
{
    Up,
    Down
}

public enum BtcUpDown5mStrategyBehavior
{
    Standard,
    GammaOutcomeSelection,
    MiddleReference,
    MiddleReferenceRevert,
    MiddleReferenceInstant,
    MiddleReferenceRevertInstant,
    SkipConsecutiveMarketResults,
    SkipConsecutiveMarketResultsRevert,
    SkipPreviousResultBpsThreshold,
    SkipPreviousResultBpsThresholdInstant,
    AlwaysUp,
    AlwaysDown,
    BinanceStartRelative,
    BinanceStartRelativeFixedPrice,
    BinanceStartRelativeBpsThreshold,
    BinanceStartRelativeBpsThresholdInstant,
    CryptoBinanceStartRelativeBpsThreshold,
    CryptoBinanceStartRelativeBpsThresholdInstant,
    BinanceStartRelativeClever,
    BinanceStartRelativeCleverMargin,
    BinanceStartRelativeEdge,
    BinanceStartRelativeDelayed,
    EnsembleVote,
    DynamicMarkov,
    StrategySelector,
    StandardEntryPriceCap,
    GammaOutcomeSelectionEntryPriceCap,
    PreviousScoreCounterTrend,
    PreviousScoreCounterTrendFak,
    PreviousScoreCounterTrendFakPremarket,
    PreviousScoreCounterTrendFakRevert,
    PreviousScoreCounterTrendFakPremarketRevert,
    PreOpenFixedDirection,
    PreOpenFixedDirectionSell,
    FixedOutcomePreviousResultBpsThresholdInstant,
    FixedOutcomePreviousResultBpsThresholdFak,
    FixedOutcomePreviousResultBpsThresholdFakPremarket,
    ReferenceAverageBpsThresholdFakPremarket,
    FilteredReferenceAverageBpsThresholdFakPremarket,
    FuturesBasisBpsThresholdFakPremarket,
    FuturesBasisBpsThresholdFakPremarketRevert,
    SimpleFixedOutcomeInstant,
    ChildMirror,
    ChildProgressMirror,
    ChildRoiMirror,
    ChildProgressRoiMirror,
    FixedOutcomeMaker,
    DiffCounterTrend,
    AdjustedDiffCounterTrend,
    ShiftDiffCounterTrend,
    DiffCounterTrendFakPremarket,
    DiffProgress,
    DiffShiftProgress,
    DiffLimitProgressPremarket,
    DiffRealLimitProgressPremarket,
    DiffReferenceAveragePremarket,
    BpsConfirmedAveragePremarket,
    DiffConfirmedAveragePremarket,
    AbsoluteBpsThresholdFakPremarket
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
    decimal? DecisionThresholdBps = null,
    BtcUpDownMarketInterval MarketInterval = BtcUpDownMarketInterval.FiveMinutes,
    BtcUpDownPreOpenLifetimeMode PreOpenLifetimeMode = BtcUpDownPreOpenLifetimeMode.Default,
    BtcUpDownFixedOutcome? FixedOutcome = null,
    decimal? FixedLimitPrice = null,
    string Category = "",
    string ReferenceAssetSymbol = "BTC",
    decimal? MakerMinBestAskExclusive = null,
    int ShiftDiffCount = 0,
    BtcUpDownFixedOutcome? DiffCounterTriggerOutcome = null,
    Guid? BaseSignalStrategyId = null,
    Guid? ConfirmationSignalStrategyId = null)
{
    public string CopiedTraderWallet => "strategy:" + Code;
}

internal sealed record CryptoUpDown5mAssetSpec(
    string Symbol,
    int BpsIdGroup,
    int InstantIdGroup,
    int SkipIdGroup,
    int SkipBpsIdGroup,
    int SkipBpsInstantIdGroup,
    int MiddleIdGroup,
    int MiddleInstantIdGroup,
    int MiddleRevertIdGroup,
    int MiddleRevertInstantIdGroup,
    int FixedOutcomeUpBpsInstantIdGroup,
    int FixedOutcomeDownBpsInstantIdGroup,
    int FifteenMinuteFixedOutcomeUpBpsInstantIdGroup,
    int FifteenMinuteFixedOutcomeDownBpsInstantIdGroup,
    int DiffUpIdGroup,
    int DiffDownIdGroup,
    int AdjustedDiffUpIdGroup,
    int AdjustedDiffDownIdGroup,
    int ShiftDiffUpIdGroup,
    int ShiftDiffDownIdGroup,
    int DiffRevertUpIdGroup,
    int DiffRevertDownIdGroup,
    int AdjustedDiffRevertUpIdGroup,
    int AdjustedDiffRevertDownIdGroup,
    int ShiftDiffRevertUpIdGroup,
    int ShiftDiffRevertDownIdGroup);

internal sealed record EthDownFakPremarketBattleSpec(
    int IdGroup,
    int EntryDelaySeconds,
    int[] Thresholds);

public sealed record TradingStrategy(
    Guid Id,
    string Code,
    string Name,
    string Description,
    bool Enabled,
    bool LiveStakes,
    bool Paused,
    DateTimeOffset? PausedUntilUtc,
    decimal PaperStakeAmount,
    decimal LiveStakeAmount,
    decimal PaperLostCoeff,
    decimal LiveLostCoeff,
    int PaperLostCounter,
    int LiveLostCounter,
    decimal LiveAvailableBalance,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record StrategyRuntimeSettings(
    Guid StrategyId,
    bool Enabled,
    bool LiveStakes,
    bool Paused,
    DateTimeOffset? PausedUntilUtc,
    decimal PaperStakeAmount,
    decimal LiveStakeAmount,
    decimal PaperLostCoeff,
    decimal LiveLostCoeff,
    int PaperLostCounter,
    int LiveLostCounter,
    decimal LiveAvailableBalance,
    DateTimeOffset? LiveEnabledAtUtc)
{
    public bool EffectiveLiveStakes => LiveStakes;

    public bool IsPausedAt(DateTimeOffset nowUtc)
    {
        return Paused && (PausedUntilUtc is null || PausedUntilUtc > nowUtc);
    }

    public static StrategyRuntimeSettings Default(Guid strategyId)
    {
        return new StrategyRuntimeSettings(
            StrategyIds.Normalize(strategyId),
            Enabled: true,
            LiveStakes: false,
            Paused: false,
            PausedUntilUtc: null,
            PaperStakeAmount: 1.00m,
            LiveStakeAmount: 1.00m,
            PaperLostCoeff: 1.00m,
            LiveLostCoeff: 1.00m,
            PaperLostCounter: 0,
            LiveLostCounter: 0,
            LiveAvailableBalance: 100.00m,
            LiveEnabledAtUtc: null);
    }
}

public sealed record StrategyLiveBalanceAdjustmentResult(
    bool Applied,
    decimal AvailableBalance,
    bool LiveStakesDisabled);

public sealed record StrategyLostCounterUpdateResult(
    bool Applied,
    int PaperLostCounter,
    int LiveLostCounter);

public sealed record StrategyPerformance(
    Guid StrategyId,
    string Code,
    string Name,
    bool Enabled,
    bool LiveStakes,
    bool Paused,
    DateTimeOffset? PausedUntilUtc,
    decimal PaperStakeAmount,
    decimal LiveStakeAmount,
    decimal PaperLostCoeff,
    decimal LiveLostCoeff,
    int PaperLostCounter,
    int LiveLostCounter,
    decimal LiveAvailableBalance,
    int OrdersCount,
    int FilledOrdersCount,
    int OpenOrdersCount,
    int OpenPositionsCount,
    int ObservedRunsCount,
    int EnteredRunsCount,
    int SkippedRunsCount,
    int PaperConditionSkippedRunsCount,
    int PaperNotAcceptedRunsCount,
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
    decimal AvgCountertrendScoreBps,
    decimal AvgCountertrendSignalBps,
    decimal? LastCountertrendSignalBps,
    int LiveOrdersCount,
    int LiveFilledOrdersCount,
    int LiveOpenOrdersCount,
    int LiveSettledOrdersCount,
    int LiveSkippedOrdersCount,
    int LiveConditionSkippedOrdersCount,
    int LiveTechnicalSkippedOrdersCount,
    int LiveIgnoredOrdersCount,
    int LiveIgnoredGtdUnfilledCount,
    int LiveIgnoredCancelledOrdersCount,
    int LiveIgnoredRejectedOrdersCount,
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
    bool LiveStakes,
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
    int PaperConditionSkippedRunsCount,
    int PaperNotAcceptedRunsCount,
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
    int LiveSettledOrdersCount,
    int LiveSkippedOrdersCount,
    int LiveConditionSkippedOrdersCount,
    int LiveTechnicalSkippedOrdersCount,
    int LiveIgnoredOrdersCount,
    int LiveIgnoredGtdUnfilledCount,
    int LiveIgnoredCancelledOrdersCount,
    int LiveIgnoredRejectedOrdersCount,
    int LiveWonOrdersCount,
    int LiveLostOrdersCount,
    decimal LiveRealizedPnlUsd,
    decimal LiveRoiPct,
    string TopSkipReason,
    DateTimeOffset? LastOrderUtc,
    DateTimeOffset? LastRunUtc);

public static class StrategyChildParentAssignmentModes
{
    public const string Child = "Child";
    public const string ChildProgress = "ChildProgress";
    public const string ChildRoi = "ChildRoi";
    public const string ChildProgressRoi = "ChildProgressRoi";
}

public sealed record StrategyLookbackPnl(
    Guid StrategyId,
    int LookbackHours,
    decimal RealizedPnlUsd,
    decimal StakeUsd,
    decimal RoiPct,
    int SettledRunsCount);

public sealed record StrategyChildParentSelection(
    Guid ChildStrategyId,
    Guid? ParentStrategyId,
    string AssetSymbol,
    int LookbackHours,
    string ChildMode,
    decimal? ParentPnlUsd,
    decimal? ParentRoiPct);

public sealed record StrategyChildParentAssignment(
    Guid Id,
    Guid ChildStrategyId,
    Guid ParentStrategyId,
    string AssetSymbol,
    int LookbackHours,
    string ChildMode,
    decimal ParentPnlUsd,
    decimal ParentRoiPct,
    DateTimeOffset AssignedAtUtc,
    DateTimeOffset? EndedAtUtc,
    DateTimeOffset UpdatedAtUtc);

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

public sealed record PaperCopiedTraderPerformanceRefreshResult(
    bool LockAcquired,
    int WalletsSeeded,
    int WalletsProcessed,
    int PerformanceRowsWritten,
    int QueueRemaining,
    bool ReconciliationCycleCompleted);

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
