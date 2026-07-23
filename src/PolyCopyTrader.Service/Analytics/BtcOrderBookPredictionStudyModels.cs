using System.Text.Json.Serialization;

namespace PolyCopyTrader.Service.Analytics;

public enum BtcOrderBookPredictionEventType
{
    Book,
    Trade,
    Control
}

public sealed record BtcOrderBookPredictionRawEvent(
    int SchemaVersion,
    string RunId,
    int ConnectionId,
    long ReceiveSequence,
    long LogicalSequence,
    BtcOrderBookPredictionEventType EventType,
    DateTimeOffset? ExchangeEventUtc,
    DateTimeOffset? TransactUtc,
    DateTimeOffset ReceivedUtc,
    long ReceivedStopwatchTicks,
    long? BookUpdateId,
    long? TradeId,
    int? TradeIndex,
    decimal? Bid,
    decimal? BidQty,
    decimal? Ask,
    decimal? AskQty,
    decimal? TradePrice,
    decimal? TradeQty,
    bool? IsBuyerMaker,
    long? PreviousId,
    long? IdDelta,
    string Status,
    string? Detail);

public sealed record BtcOrderBookPredictionCollectionSummary(
    string RunId,
    string Source,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string Status,
    string EventsPath,
    long BookEvents,
    long TradeEvents,
    long ControlEvents,
    long DecodeErrors,
    long Reconnects,
    int QueueHighWaterMark,
    long StopwatchFrequency,
    string? FailureReason);

public sealed record BtcOrderBookPredictionGammaLabel(
    DateTimeOffset MarketStartUtc,
    DateTimeOffset MarketEndUtc,
    string Slug,
    string? MarketId,
    string? ConditionId,
    string? Outcome,
    string Status,
    DateTimeOffset FetchedAtUtc,
    string RequestUri,
    string? RawSha256,
    string? RawJson,
    string? Detail);

public sealed record BtcOrderBookPredictionEventSegment(
    int Sequence,
    string FileName,
    long EventCount,
    DateTimeOffset FirstReceivedUtc,
    DateTimeOffset LastReceivedUtc,
    string Sha256);

public sealed record BtcOrderBookPredictionEventIndex(
    int SchemaVersion,
    string Status,
    int SegmentDurationSeconds,
    long TotalEvents,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<BtcOrderBookPredictionEventSegment> Segments,
    string? RunId,
    string? AssetSymbol);

public sealed record BtcOrderBookPredictionFeatureRow(
    DateTimeOffset MarketStartUtc,
    DateTimeOffset MarketEndUtc,
    DateTimeOffset DecisionUtc,
    int DecisionLeadSeconds,
    int FeatureWindowSeconds,
    DateTimeOffset FeatureWindowStartUtc,
    string? GammaOutcome,
    string GammaLabelStatus,
    string? BinanceProxyOutcome,
    decimal? BinanceStartPrice,
    decimal? BinanceEndPrice,
    int QuoteEventCount,
    int TradeEventCount,
    decimal? QuoteCoverageRatio,
    decimal? LastQuoteAgeMilliseconds,
    decimal? LastBid,
    decimal? LastAsk,
    decimal? LastBidQty,
    decimal? LastAskQty,
    decimal? LastSpreadBps,
    decimal? LastImbalance,
    decimal? TimeWeightedImbalance,
    decimal? MinimumImbalance,
    decimal? MaximumImbalance,
    decimal? ImbalanceSlopePerSecond,
    decimal? LastMicropriceOffsetBps,
    decimal? TimeWeightedMicropriceOffsetBps,
    decimal? ObservedL1Ofi,
    decimal? ObservedL1OfiNormalized,
    decimal? SignedTradeQuantity,
    decimal? TotalTradeQuantity,
    decimal? TradeFlowImbalance,
    decimal? PremarketTradeReturnBps,
    bool HasQualityGap,
    bool IsValid,
    string? InvalidReason);

public sealed record BtcOrderBookPredictionSplit(
    IReadOnlyList<DateTimeOffset> TrainMarkets,
    IReadOnlyList<DateTimeOffset> ValidationMarkets,
    IReadOnlyList<DateTimeOffset> TestMarkets,
    int EmbargoMarkets);

public sealed record BtcOrderBookPredictionRule(
    int DecisionLeadSeconds,
    int FeatureWindowSeconds,
    string FeatureName,
    decimal Threshold,
    bool GreaterOrEqualPredictsUp,
    decimal TrainBalancedAccuracy,
    decimal ValidationBalancedAccuracy);

public sealed record BtcOrderBookPredictionMetrics(
    int Count,
    int UpCount,
    int DownCount,
    int TrueUp,
    int FalseUp,
    int TrueDown,
    int FalseDown,
    decimal Accuracy,
    decimal BalancedAccuracy,
    decimal UpPrecision,
    decimal UpRecall,
    decimal DownRecall,
    decimal BrierScore);

public sealed record BtcOrderBookPredictionMarketPrediction(
    DateTimeOffset MarketStartUtc,
    string ActualOutcome,
    string PredictedOutcome,
    decimal FeatureValue,
    string BaselinePrediction,
    bool ModelCorrect,
    bool BaselineCorrect);

public sealed record BtcOrderBookPredictionAnalysisResult(
    string Status,
    DateTimeOffset AnalyzedAtUtc,
    int FeatureRows,
    int UniqueMarkets,
    int LabeledMarkets,
    int ValidCommonMarkets,
    int DistinctUtcDays,
    int MinimumLabeledMarkets,
    int MinimumDistinctUtcDays,
    int MinimumMarketsPerClass,
    decimal TrainFraction,
    decimal ValidationFraction,
    decimal TestFraction,
    BtcOrderBookPredictionSplit? Split,
    BtcOrderBookPredictionRule? SelectedRule,
    BtcOrderBookPredictionMetrics? TestMetrics,
    BtcOrderBookPredictionMetrics? MajorityBaselineMetrics,
    BtcOrderBookPredictionMetrics? MomentumBaselineMetrics,
    decimal? AccuracyLiftVsMajority,
    decimal? BalancedAccuracyLiftVsMajority,
    decimal? AccuracyLiftVsMomentum,
    decimal? BalancedAccuracyLiftVsMomentum,
    int MomentumBaselineAvailableMarkets,
    decimal? GammaBinanceAgreement,
    IReadOnlyList<string> ExclusionReasons,
    IReadOnlyList<BtcOrderBookPredictionMarketPrediction> TestPredictions,
    string Conclusion,
    string? AssetSymbol = null);

public sealed record BtcOrderBookPredictionRunManifest(
    int SchemaVersion,
    string RunId,
    string CommandMode,
    string Source,
    string StreamUrl,
    string GammaBaseUrl,
    string StudyBuildVersion,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string OutputDirectory,
    int DurationSeconds,
    int SegmentDurationSeconds,
    IReadOnlyList<int> DecisionLeadSeconds,
    IReadOnlyList<int> FeatureWindowSeconds,
    int MaximumQuoteAgeMilliseconds,
    decimal MinimumQuoteCoverageRatio,
    int MinimumLabeledMarkets,
    int MinimumDistinctUtcDays,
    int MinimumMarketsPerClass,
    decimal TrainFraction,
    decimal ValidationFraction,
    decimal TestFraction,
    long StopwatchFrequency,
    DateTimeOffset StopwatchAnchorUtc,
    long StopwatchAnchorTicks,
    string? ApiKeySource,
    string? EventsFile,
    string? EventsSha256,
    long BookEvents,
    long TradeEvents,
    long ControlEvents,
    long DecodeErrors,
    long Reconnects,
    int QueueHighWaterMark,
    string? FailureReason,
    string? AssetSymbol)
{
    [JsonIgnore]
    public bool IsComplete => string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, "completed_with_gaps", StringComparison.OrdinalIgnoreCase);
}
