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
    DateTimeOffset TimestampUtc);

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
    DateTimeOffset CreatedAtUtc);

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
    DateTimeOffset UpdatedAtUtc);

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
    string Evidence);

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

public sealed record OnChainIngestionResult(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    long FromBlock,
    long ToBlock,
    int ContractsScanned,
    int LogsFetched,
    int FillsStored);

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
