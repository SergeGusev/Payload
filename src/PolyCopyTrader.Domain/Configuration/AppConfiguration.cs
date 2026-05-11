namespace PolyCopyTrader.Domain.Configuration;

public sealed class AppConfiguration
{
    public BotOptions Bot { get; init; } = new();

    public RiskOptions Risk { get; init; } = new();

    public ExecutionOptions Execution { get; init; } = new();

    public SignalOptions Signal { get; init; } = new();

    public PolymarketOptions Polymarket { get; init; } = new();

    public PolymarketHttpLoggingOptions PolymarketHttpLogging { get; init; } = new();

    public PolymarketAuthOptions PolymarketAuth { get; init; } = new();

    public MarketDataWebSocketOptions MarketDataWebSocket { get; init; } = new();

    public MarketTradeDiagnosticsOptions MarketTradeDiagnostics { get; init; } = new();

    public DataApiTraderIngestionOptions DataApiTraderIngestion { get; init; } = new();

    public WatchlistOptions Watchlist { get; init; } = new();

    public PaperTradingOptions PaperTrading { get; init; } = new();

    public LiveTradingOptions LiveTrading { get; init; } = new();

    public DashboardOptions Dashboard { get; init; } = new();

    public AnalyticsOptions Analytics { get; init; } = new();

    public TraderDiscoveryOptions TraderDiscovery { get; init; } = new();

    public GammaMarketIngestionOptions GammaMarketIngestion { get; init; } = new();

    public BtcUpDown5mStrategyOptions BtcUpDown5mStrategy { get; init; } = new();

    public CoinbaseExchangeOptions CoinbaseExchange { get; init; } = new();

    public BinanceBtcUsdReferenceOptions BinanceBtcUsdReference { get; init; } = new();

    public BinanceCryptoReferenceOptions BinanceCryptoReference { get; init; } = new();

    public BtcUpDown5mOddsArchiveOptions BtcUpDown5mOddsArchive { get; init; } = new();

    public CryptoUpDown5mOddsArchiveOptions CryptoUpDown5mOddsArchive { get; init; } = new();

    public ChainlinkBtcUsdDiagnosticsOptions ChainlinkBtcUsdDiagnostics { get; init; } = new();

    public OnChainIngestionOptions OnChainIngestion { get; init; } = new();

    public IpcOptions Ipc { get; init; } = new();

    public StorageOptions Storage { get; init; } = new();
}

public sealed class BotOptions
{
    public BotMode Mode { get; init; } = BotMode.Paper;

    public int PollIntervalSeconds { get; init; } = 10;

    public int HeartbeatIntervalSeconds { get; init; } = 10;

    public bool UseWebSockets { get; init; }

    public bool EnableLiveTrading { get; init; }
}

public sealed class RiskOptions
{
    public decimal MaxTradeBankrollPct { get; init; } = 0.25m;

    public decimal MaxMarketBankrollPct { get; init; } = 1.0m;

    public decimal MaxTraderBankrollPct { get; init; } = 3.0m;

    public decimal MaxCategoryBankrollPct { get; init; } = 7.5m;

    public decimal MaxTotalDeployedPct { get; init; } = 25.0m;

    public decimal MaxDailyLossPct { get; init; } = 1.0m;

    public int MaxOpenOrders { get; init; } = 10;

    public int MaxOrderAgeSeconds { get; init; } = 300;
}

public sealed class ExecutionOptions
{
    public bool MakerOnly { get; init; } = true;

    public bool AllowTaker { get; init; }

    public decimal MaxSlippageCents { get; init; } = 1m;

    public decimal MaxSpreadCents { get; init; } = 2m;

    public decimal MaxSpreadPct { get; init; } = 3.0m;

    public decimal MinLeaderTradeUsd { get; init; } = 0.10m;
}

public sealed class SignalOptions
{
    public int IgnoreBelowScore { get; init; } = 60;

    public int ObserveBelowScore { get; init; } = 75;

    public int NormalPaperOrderScore { get; init; } = 90;

    public int CategoryAllowedScore { get; init; } = 30;

    public int AgeUnder10SecondsScore { get; init; } = 20;

    public int AgeUnder60SecondsScore { get; init; } = 12;

    public int AgeUnder5MinutesScore { get; init; } = 5;

    public int EntryWithinHalfCentScore { get; init; } = 20;

    public int EntryWithinOneCentScore { get; init; } = 15;

    public int EntryWithinTwoCentsScore { get; init; } = 5;

    public int LargeLeaderTradeScore { get; init; } = 15;

    public int DepthAcceptableScore { get; init; } = 10;

    public int SlowMarketScore { get; init; } = 5;

    public int BorderlineSpreadPenalty { get; init; } = 20;

    public decimal LargeLeaderTradeMultiplier { get; init; } = 2m;

    public int MarketCloseWindowMinutes { get; init; } = 15;

    public bool RequireKnownMarketCategory { get; init; }

    public bool RequireLeaderCategoryPerformance { get; init; }

    public int MinLeaderCategoryResolvedPositions { get; init; } = 3;

    public decimal MinLeaderCategoryResolvedRoiPct { get; init; }

    public decimal MinLeaderCategoryWinRatePct { get; init; } = 50m;

    public decimal MinLeaderCategoryScore { get; init; }

    public string MinLeaderCategorySampleQuality { get; init; } = "Low";

    public int LeaderCategoryPerformanceStaleAfterHours { get; init; } = 24;

    public int LeaderCategoryPerformanceScore { get; init; } = 15;

    public bool CopiedTraderPerformanceGuardEnabled { get; init; } = true;

    public int CopiedTraderPerformanceMinSettledPositions { get; init; } = 3;

    public decimal CopiedTraderPerformanceMinTotalPnlUsd { get; init; } = -2m;

    public decimal CopiedTraderPerformanceMinRoiPct { get; init; } = -10m;

    public decimal CopiedTraderPerformanceMinScore { get; init; } = 35m;

    public int CopiedTraderPerformanceScore { get; init; } = 10;
}

public sealed class PolymarketOptions
{
    public string DataApiBaseUrl { get; init; } = "https://data-api.polymarket.com";

    public string ClobBaseUrl { get; init; } = "https://clob.polymarket.com";

    public string GammaBaseUrl { get; init; } = "https://gamma-api.polymarket.com";

    public string GeoblockUrl { get; init; } = "https://polymarket.com/api/geoblock";

    public int TimeoutSeconds { get; init; } = 30;

    public int MaxRetries { get; init; } = 2;

    public int RetryBaseDelayMilliseconds { get; init; } = 1000;

    public Dictionary<string, List<string>> CertificatePins { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PolymarketHttpLoggingOptions
{
    public bool Enabled { get; init; } = true;

    public bool PersistSuccessfulRequests { get; init; }

    public int SuccessfulRequestSampleRate { get; init; }

    public bool PersistNetworkErrors { get; init; } = true;

    public bool PersistRateLimitedRequests { get; init; } = true;

    public bool PersistAuthFailures { get; init; } = true;

    public bool PersistServerErrors { get; init; } = true;

    public bool PersistOtherClientErrors { get; init; }

    public bool PersistNotFound { get; init; }

    public bool CleanupEnabled { get; init; } = true;

    public int CleanupIntervalMinutes { get; init; } = 10;

    public int CleanupBatchSize { get; init; } = 25_000;

    public int CleanupMaxBatchesPerCycle { get; init; } = 2;

    public int SuccessfulRetentionHours { get; init; } = 6;

    public int FailedRetentionDays { get; init; } = 14;
}

public sealed class PolymarketAuthOptions
{
    public bool Enabled { get; init; }

    public string SecretProvider { get; init; } = "Environment";

    public string SigningAddress { get; init; } = string.Empty;

    public string FunderAddress { get; init; } = string.Empty;

    public int ChainId { get; init; } = 137;

    public string SignatureType { get; init; } = "EOA";

    public bool DryRunSigningEnabled { get; init; }

    public string DryRunPrivateKeyName { get; init; } = "POLYCOPYTRADER_POLYMARKET_DRY_RUN_PRIVATE_KEY";

    public string OrderSigningPrivateKeyName { get; init; } = "POLYCOPYTRADER_POLYMARKET_ORDER_SIGNING_PRIVATE_KEY";

    public string ApiKeyName { get; init; } = "POLYCOPYTRADER_POLYMARKET_API_KEY";

    public string ApiKeyOwnerName { get; init; } = "POLYCOPYTRADER_POLYMARKET_API_KEY_OWNER";

    public string ApiSecretName { get; init; } = "POLYCOPYTRADER_POLYMARKET_API_SECRET";

    public string ApiPassphraseName { get; init; } = "POLYCOPYTRADER_POLYMARKET_API_PASSPHRASE";
}

public sealed class MarketDataWebSocketOptions
{
    public bool Enabled { get; init; } = true;

    public MarketDataWebSocketSubscriptionScope SubscriptionScope { get; init; } = MarketDataWebSocketSubscriptionScope.AllActiveMarkets;

    public string MarketEndpointUrl { get; init; } = "wss://ws-subscriptions-clob.polymarket.com/ws/market";

    public int HeartbeatSeconds { get; init; } = 10;

    public int ReconnectBaseDelaySeconds { get; init; } = 2;

    public int ReconnectMaxDelaySeconds { get; init; } = 60;

    public int SubscriptionRefreshSeconds { get; init; } = 15;

    public int StaleAfterSeconds { get; init; } = 30;

    public int ReceiveBufferBytes { get; init; } = 65_536;

    public int SubscriptionBatchSize { get; init; } = 1_000;

    public int ShardMaxAssets { get; init; } = 3_000;

    public int MaxShardConnections { get; init; } = 64;

    public int WatchdogIntervalSeconds { get; init; } = 10;

    public int WatchdogStaleSeconds { get; init; } = 90;

    public bool PersistOrderBookSnapshots { get; init; }

    public bool PersistMarketDataEvents { get; init; }

    public int StatusPersistIntervalSeconds { get; init; } = 60;

    public int StrongSignalMinimumScore { get; init; } = 90;

    public int StrongSignalLookbackMinutes { get; init; } = 30;

    public int MaxSubscribedAssets { get; init; }

    public List<string> PinnedAssetIds { get; init; } = [];
}

public enum MarketDataWebSocketSubscriptionScope
{
    AllActiveMarkets,
    BtcUpDown5mOnly
}

public sealed class MarketTradeDiagnosticsOptions
{
    public bool Enabled { get; init; }

    public int MarketTradesLimit { get; init; } = 1_000;

    public int MatchTimestampToleranceSeconds { get; init; } = 5;
}

public sealed class DataApiTraderIngestionOptions
{
    public bool Enabled { get; init; } = true;

    public int GlobalTradesLimit { get; init; } = 1_000;

    public int PollDelayMilliseconds { get; init; } = 0;

    public int UserTradesLimit { get; init; } = 1_000;

    public int MaxUserHistoricalOffset { get; init; } = 3_000;

    public bool TakerOnly { get; init; }

    public int MaxTradersPerCycle { get; init; } = 1_000;

    public int SyncBatchSize { get; init; } = 5;

    public int SyncPollDelayMilliseconds { get; init; } = 1_000;

    public int ExistingTraderRefreshIntervalSeconds { get; init; } = 3_600;

    public bool RefreshPositionsEnabled { get; init; }

    public bool RefreshPolymarketRatingsEnabled { get; init; } = true;

    public string PolymarketRatingTimePeriod { get; init; } = "ALL";

    public string PolymarketRatingOrderBy { get; init; } = "PNL";

    public int PolymarketRatingRefreshIntervalSeconds { get; init; } = 3_600;

    public int PolymarketRatingFailureDelaySeconds { get; init; } = 60;

    public int PolymarketRatingRequestDelayMilliseconds { get; init; }

    public bool PolymarketRatingPositionsEnabled { get; init; } = true;

    public int PolymarketRatingCurrentPositionsLimit { get; init; } = 500;

    public int PolymarketRatingMaxCurrentPositionsOffset { get; init; }

    public int PolymarketRatingClosedPositionsLimit { get; init; } = 50;

    public int PolymarketRatingMaxClosedPositionsOffset { get; init; }

    public int MaxPositionRefreshesPerCycle { get; init; } = 1_000;

    public int CurrentPositionsLimit { get; init; } = 500;

    public int MaxCurrentPositionsOffset { get; init; } = 10_000;

    public int ClosedPositionsLimit { get; init; } = 50;

    public int MaxClosedPositionsOffset { get; init; } = 100_000;

    public int ErrorDelayMilliseconds { get; init; } = 1_000;

    public int MaxErrorDelayMilliseconds { get; init; } = 30_000;
}

public sealed class WatchlistOptions
{
    public int MaxTradesPerTraderPerPoll { get; init; } = 100;

    public int MaxPositionsPerTraderPerPoll { get; init; } = 500;

    public List<TraderRuleOptions> Traders { get; init; } = [];
}

public sealed class TraderRuleOptions
{
    public string Name { get; init; } = string.Empty;

    public string Wallet { get; init; } = string.Empty;

    public List<string> AllowedCategories { get; init; } = [];

    public bool Enabled { get; init; } = true;

    public int MaxLagSeconds { get; init; } = 300;

    public decimal MaxSlippageCents { get; init; } = 1m;

    public decimal MaxSpreadCents { get; init; } = 2m;

    public decimal MaxSpreadPct { get; init; } = 3.0m;

    public decimal MinLeaderTradeUsd { get; init; } = 0.10m;
}

public sealed class PaperTradingOptions
{
    public bool RunInLiveMode { get; init; }

    public decimal InitialBankrollUsd { get; init; } = 10_000m;

    public int DefaultOrderTtlSeconds { get; init; } = 300;

    public int OpenOrderProcessingIntervalSeconds { get; init; } = 5;

    public int OpenOrderFillSimulationBatchSize { get; init; } = 100;

    public bool UseMinimumMarketOrderSize { get; init; }

    public bool SettlementEnabled { get; init; } = true;

    public int SettlementPollIntervalSeconds { get; init; } = 60;

    public int CopiedTraderPerformanceRefreshSeconds { get; init; } = 30;

    public bool LeaderActivityExitTrackingEnabled { get; init; } = true;

    public int LeaderActivityExitTrackingPollDelayMilliseconds { get; init; } = 1_000;

    public int LeaderActivityExitTrackingBatchSize { get; init; } = 100;

    public int LeaderActivityExitTrackingActivityLimit { get; init; } = 500;

    public int LeaderActivityExitTrackingRequestDelayMilliseconds { get; init; }

    public int LeaderActivityExitTrackingErrorDelayMilliseconds { get; init; } = 1_000;

    public int LeaderActivityExitTrackingMaxErrorDelayMilliseconds { get; init; } = 30_000;
}

public sealed class LiveTradingOptions
{
    public string ManualEnableCode { get; init; } = string.Empty;

    public decimal MaxOrderNotionalUsd { get; init; } = 1.00m;

    public decimal MaxTradeBankrollPct { get; init; } = 0.10m;

    public decimal MaxMarketBankrollPct { get; init; } = 0.50m;

    public decimal MaxDailyLossPct { get; init; } = 0.50m;

    public decimal MaxTotalDeployedPct { get; init; } = 5.00m;

    public int DefaultOrderTtlSeconds { get; init; } = 300;

    public int MaintenancePollIntervalSeconds { get; init; } = 5;

    public int MaxClockDriftSeconds { get; init; } = 5;

    public int ApiErrorLockoutCount { get; init; } = 5;

    public int ApiErrorLockoutWindowMinutes { get; init; } = 15;

    public int MaxOpenLiveOrders { get; init; } = 1;

    public bool CancelAllOnKillSwitch { get; init; } = true;
}

public sealed class DashboardOptions
{
    public int RefreshIntervalSeconds { get; init; } = 60;

    public int StrategyRefreshIntervalSeconds { get; init; } = 60;
}

public sealed class AnalyticsOptions
{
    public bool DailyReportGenerationEnabled { get; init; } = true;

    public int DailyReportRefreshMinutes { get; init; } = 15;

    public int DashboardReportLimit { get; init; } = 250;

    public string CsvExportDirectory { get; init; } = "exports";
}

public sealed class TraderDiscoveryOptions
{
    public bool Enabled { get; init; } = true;

    public string Category { get; init; } = "OVERALL";

    public string TimePeriod { get; init; } = "MONTH";

    public int RefreshIntervalMinutes { get; init; } = 360;

    public int LeaderboardPages { get; init; } = 21;

    public int CandidatesPerSide { get; init; } = 10;

    public int TradesPerCandidate { get; init; } = 50;

    public int PositionsPerCandidate { get; init; } = 50;

    public int RequestDelayMilliseconds { get; init; } = 500;
}

public sealed class GammaMarketIngestionOptions
{
    public bool Enabled { get; init; } = true;

    public int PollIntervalSeconds { get; init; } = 0;

    public int PageLimit { get; init; } = 500;
}

public sealed class BtcUpDown5mStrategyOptions
{
    public bool Enabled { get; init; } = true;

    public int PollIntervalSeconds { get; init; } = 5;

    public decimal StakeUsd { get; init; } = 1.00m;

    public int EntryGraceSeconds { get; init; } = 10;

    public int MaxMarketsPerCycle { get; init; } = 500;

    public int MaxEntriesPerCycle { get; init; } = 250;

    public int MaxConcurrentEntryDecisions { get; init; } = 1;

    public int MaxSettlementsPerCycle { get; init; } = 250;

    public int MartinTriggerLosses { get; init; } = 3;

    public int MartinStakeLevels { get; init; } = 1;

    public int MartinStateLookbackRuns { get; init; } = 50;

    public bool PaperTakerPricingEnabled { get; init; }

    public bool PaperTakerRestFallbackEnabled { get; init; } = true;

    public int PaperTakerMaxQuoteAgeMilliseconds { get; init; } = 1_500;

    public decimal PaperTakerMaxEntryPrice { get; init; } = 0.80m;

    public decimal PaperTakerMaxReferenceSlippage { get; init; } = 0.03m;

    public decimal PaperTakerMaxSpreadAbs { get; init; } = 0.10m;

    public decimal PaperTakerMaxGammaClobDiff { get; init; } = 0.15m;

    public bool OpeningLimitDynamicBreakEvenPricingEnabled { get; init; } = true;

    public int OpeningLimitBreakEvenLookbackRuns { get; init; } = 100;

    public int OpeningLimitBreakEvenMinSettledRuns { get; init; } = 30;

    public decimal OpeningLimitBreakEvenMargin { get; init; } = 0.10m;

    public decimal OpeningLimitMaxPrice { get; init; } = 0.50m;

    public decimal OpeningLimitPriceTickSize { get; init; } = 0.01m;

    public int OpeningLimitGtdTtlSeconds { get; init; } = 120;

    public bool PaperGtdConservativeFillEnabled { get; init; } = true;

    public decimal PaperGtdImmediateFillDepthMultiplier { get; init; } = 1.0m;

    public int PaperGtdMinLateFillEvidenceSeconds { get; init; } = 1;

    public int CloseBookCaptureLookbackSeconds { get; init; } = 60;

    public int CloseBookCaptureIntervalSeconds { get; init; } = 10;

    public bool OrderBookRefreshWorkerEnabled { get; init; } = true;

    public int OrderBookRefreshIntervalMilliseconds { get; init; } = 1_000;

    public int OrderBookRefreshMaxMarketsPerCycle { get; init; } = 4;

    public int OrderBookRefreshMarketLookaheadSeconds { get; init; } = 90;

    public int OrderBookRefreshMarketBehindSeconds { get; init; } = 30;

    public int OrderBookRefreshRequestTimeoutSeconds { get; init; } = 2;

    public List<string> EnabledVariantCodes { get; init; } = [];
}

public sealed class CoinbaseExchangeOptions
{
    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "https://api.exchange.coinbase.com";

    public string ProductId { get; init; } = "BTC-USD";

    public int PollIntervalSeconds { get; init; } = 60;

    public int WindowSize { get; init; } = 100;

    public int TimeoutSeconds { get; init; } = 15;

    public string UserAgent { get; init; } = "PolyCopyTrader/1.0 BTC-USD-reference";
}

public sealed class BinanceBtcUsdReferenceOptions
{
    public bool Enabled { get; init; } = true;

    public string StreamUrl { get; init; } = "wss://data-stream.binance.vision:443/ws/btcusdt@trade";

    public int SampleIntervalSeconds { get; init; } = 60;

    public int WindowSize { get; init; } = 100;

    public int StaleAfterSeconds { get; init; } = 5;

    public int ReconnectBaseDelaySeconds { get; init; } = 2;

    public int ReconnectMaxDelaySeconds { get; init; } = 60;

    public int ReceiveBufferBytes { get; init; } = 16_384;
}

public sealed class BinanceCryptoReferenceOptions
{
    public bool Enabled { get; init; } = true;

    public string CombinedStreamBaseUrl { get; init; } = "wss://data-stream.binance.vision:443/stream";

    public List<string> AssetSymbols { get; init; } = ["ETH", "SOL", "XRP"];

    public int StaleAfterSeconds { get; init; } = 5;

    public int ReconnectBaseDelaySeconds { get; init; } = 2;

    public int ReconnectMaxDelaySeconds { get; init; } = 60;

    public int ReceiveBufferBytes { get; init; } = 16_384;
}

public sealed class BtcUpDown5mOddsArchiveOptions
{
    public bool Enabled { get; init; } = true;

    public int PollIntervalSeconds { get; init; } = 5;

    public int MaxMarketsPerCycle { get; init; } = 500;

    public int MaxOrderBookAgeMilliseconds { get; init; } = 15_000;

    public bool RestFallbackEnabled { get; init; } = true;
}

public sealed class CryptoUpDown5mOddsArchiveOptions
{
    public bool Enabled { get; init; } = true;

    public List<string> AssetSymbols { get; init; } = ["ETH", "SOL", "XRP"];

    public int PollIntervalSeconds { get; init; } = 5;

    public int MaxMarketsPerCycle { get; init; } = 500;

    public int MaxOrderBookAgeMilliseconds { get; init; } = 15_000;

    public bool RestFallbackEnabled { get; init; } = true;
}

public sealed class ChainlinkBtcUsdDiagnosticsOptions
{
    public bool Enabled { get; init; } = true;

    public string BaseUrl { get; init; } = "https://data.chain.link";

    public string FeedId { get; init; } = "0x00039d9e45394f473ab1f050a1b963e6b05351e52d71e507509ada0c95ed75b8";

    public int PollIntervalSeconds { get; init; } = 10;

    public int TimeoutSeconds { get; init; } = 15;

    public int MaxNearestAgeSeconds { get; init; } = 30;

    public string QueryWindow { get; init; } = "1m";
}

public sealed class OnChainIngestionOptions
{
    public bool Enabled { get; init; } = true;

    public bool TradeCaptureEnabled { get; init; } = true;

    public bool TradeCapturePersistCaptures { get; init; } = true;

    public bool TradeCaptureSkipStaleCursor { get; init; }

    public int TradeCaptureMaxCursorLagBlocks { get; init; } = 2;

    public bool PaperSignalEnabled { get; init; }

    public bool PaperSignalBacklogEnabled { get; init; } = true;

    public bool PaperSignalHotPathEnabled { get; init; } = true;

    public int PaperSignalHotMaxAgeSeconds { get; init; } = 2;

    public int PaperSignalLatestCandidatesLimit { get; init; } = 100;

    public string PolygonRpcUrl { get; init; } = "https://polygon.drpc.org";

    public string RpcUrlEnvironmentVariable { get; init; } = "POLYCOPYTRADER_POLYGON_RPC_URL";

    public int LookbackDays { get; init; } = 7;

    public int MaxBlockRange { get; init; } = 500;

    public int RequestDelayMilliseconds { get; init; } = 100;

    public int TradeCapturePollDelayMilliseconds { get; init; } = 250;

    public int TradeCaptureRequestDelayMilliseconds { get; init; } = 0;

    public int TradeCaptureStartLookbackBlocks { get; init; } = 20;

    public int TradeCaptureConfirmations { get; init; } = 0;

    public int TradeCaptureErrorDelayMilliseconds { get; init; } = 1_000;

    public int TradeCaptureMaxErrorDelayMilliseconds { get; init; } = 30_000;

    public int PaperSignalPollDelayMilliseconds { get; init; } = 250;

    public int PaperSignalBatchSize { get; init; } = 250;

    public int PaperSignalMaxLagSeconds { get; init; } = 300;

    public int PaperSignalRatingStaleAfterHours { get; init; } = 24;

    public bool PaperSignalRequirePolymarketRatingFound { get; init; } = true;

    public decimal PaperSignalMinLeaderboardPnlUsd { get; init; }

    public decimal PaperSignalMinLeaderboardPnlToVolumePct { get; init; }

    public bool BackgroundSyncEnabled { get; init; } = true;

    public int BackgroundSyncIdleDelaySeconds { get; init; } = 30;

    public int BackgroundErrorDelaySeconds { get; init; } = 60;

    public int BackgroundMaxErrorDelaySeconds { get; init; } = 900;

    public int MarketEnrichmentBatchSize { get; init; } = 100;

    public int MarketEnrichmentMaxBatchesPerRun { get; init; } = 25;

    public bool BackgroundMarketEnrichmentEnabled { get; init; } = true;

    public int MarketEnrichmentIntervalSeconds { get; init; } = 120;

    public bool BackgroundPositionRefreshEnabled { get; init; } = true;

    public int PositionRefreshIntervalSeconds { get; init; } = 60;

    public int PositionRefreshTokenBatchSize { get; init; } = 25;

    public int PositionRefreshQueueSeedTokenBatchSize { get; init; } = 100;

    public bool BackgroundActivityRefreshEnabled { get; init; } = true;

    public int ActivityRefreshIntervalSeconds { get; init; } = 90;

    public int ActivityRefreshWalletBatchSize { get; init; } = 50;

    public int ActivityRefreshQueueSeedWalletBatchSize { get; init; } = 100;

    public bool BackgroundPerformanceRefreshEnabled { get; init; } = true;

    public int PerformanceRefreshIntervalSeconds { get; init; } = 120;

    public int PerformanceRefreshWalletBatchSize { get; init; } = 50;

    public int PerformanceRefreshQueueSeedWalletBatchSize { get; init; } = 100;

    public bool BackgroundCategoryPerformanceRefreshEnabled { get; init; } = true;

    public int CategoryPerformanceRefreshIntervalSeconds { get; init; } = 150;

    public int CategoryPerformancePairBatchSize { get; init; } = 250;

    public int CategoryPerformanceQueueSeedPairBatchSize { get; init; } = 250;

    public bool BackgroundSignalCandidateRefreshEnabled { get; init; } = true;

    public int SignalCandidateRefreshIntervalSeconds { get; init; } = 60;

    public int SignalCandidateBatchSize { get; init; } = 250;

    public int SignalCandidateQueueSeedBatchSize { get; init; } = 1_000;

    public int SignalCandidateRetryBatchSize { get; init; } = 250;

    public List<OnChainExchangeContractOptions> ExchangeContracts { get; init; } =
    [
        new("CTF Exchange V1", "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E", "V1"),
        new("Neg Risk CTF Exchange V1", "0xC5d563A36AE78145C45a50134d48A1215220f80a", "V1"),
        new("CTF Exchange V2", "0xE111180000d2663C0091e4f400237545B87B996B", "V2"),
        new("Neg Risk CTF Exchange V2", "0xe2222d279d744050d28e00520010520000310F59", "V2")
    ];
}

public sealed record OnChainExchangeContractOptions(
    string Name,
    string Address,
    string Version);

public sealed class IpcOptions
{
    public bool Enabled { get; init; } = true;

    public string ListenUrl { get; init; } = "http://127.0.0.1:5118/";

    public string DashboardBaseUrl { get; init; } = "http://127.0.0.1:5118/";
}

public sealed class StorageOptions
{
    public string Provider { get; init; } = "PostgreSQL";

    public string ConnectionString { get; init; } = string.Empty;

    public string ConnectionStringEnvironmentVariable { get; init; } = "POLYCOPYTRADER_POSTGRES_CONNECTION";

    public bool RequireConfiguredDatabase { get; init; }
}
