namespace PolyCopyTrader.Domain.Configuration;

public sealed class AppConfiguration
{
    public BotOptions Bot { get; init; } = new();

    public RiskOptions Risk { get; init; } = new();

    public ExecutionOptions Execution { get; init; } = new();

    public SignalOptions Signal { get; init; } = new();

    public PolymarketOptions Polymarket { get; init; } = new();

    public PolymarketAuthOptions PolymarketAuth { get; init; } = new();

    public MarketDataWebSocketOptions MarketDataWebSocket { get; init; } = new();

    public WatchlistOptions Watchlist { get; init; } = new();

    public PaperTradingOptions PaperTrading { get; init; } = new();

    public LiveTradingOptions LiveTrading { get; init; } = new();

    public DashboardOptions Dashboard { get; init; } = new();

    public AnalyticsOptions Analytics { get; init; } = new();

    public TraderDiscoveryOptions TraderDiscovery { get; init; } = new();

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

    public decimal MinLeaderTradeUsd { get; init; } = 500m;
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
}

public sealed class PolymarketOptions
{
    public string DataApiBaseUrl { get; init; } = "https://data-api.polymarket.com";

    public string ClobBaseUrl { get; init; } = "https://clob.polymarket.com";

    public string GammaBaseUrl { get; init; } = "https://gamma-api.polymarket.com";

    public string GeoblockUrl { get; init; } = "https://polymarket.com/api/geoblock";

    public int TimeoutSeconds { get; init; } = 30;

    public int MaxRetries { get; init; } = 2;

    public int RetryBaseDelayMilliseconds { get; init; } = 250;

    public Dictionary<string, List<string>> CertificatePins { get; init; } = new(StringComparer.OrdinalIgnoreCase);
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

    public string MarketEndpointUrl { get; init; } = "wss://ws-subscriptions-clob.polymarket.com/ws/market";

    public int HeartbeatSeconds { get; init; } = 10;

    public int ReconnectBaseDelaySeconds { get; init; } = 2;

    public int ReconnectMaxDelaySeconds { get; init; } = 60;

    public int SubscriptionRefreshSeconds { get; init; } = 15;

    public int StaleAfterSeconds { get; init; } = 30;

    public int ReceiveBufferBytes { get; init; } = 65_536;

    public int StrongSignalMinimumScore { get; init; } = 90;

    public int StrongSignalLookbackMinutes { get; init; } = 30;

    public int MaxSubscribedAssets { get; init; } = 100;

    public List<string> PinnedAssetIds { get; init; } = [];
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

    public decimal MinLeaderTradeUsd { get; init; } = 500m;
}

public sealed class PaperTradingOptions
{
    public decimal InitialBankrollUsd { get; init; } = 10_000m;

    public int DefaultOrderTtlSeconds { get; init; } = 300;
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

    public int MaxClockDriftSeconds { get; init; } = 5;

    public int ApiErrorLockoutCount { get; init; } = 5;

    public int ApiErrorLockoutWindowMinutes { get; init; } = 15;

    public int MaxOpenLiveOrders { get; init; } = 1;

    public bool CancelAllOnKillSwitch { get; init; } = true;
}

public sealed class DashboardOptions
{
    public int RefreshIntervalSeconds { get; init; } = 3;
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

public sealed class OnChainIngestionOptions
{
    public bool Enabled { get; init; } = true;

    public string PolygonRpcUrl { get; init; } = "https://polygon.drpc.org";

    public string RpcUrlEnvironmentVariable { get; init; } = "POLYCOPYTRADER_POLYGON_RPC_URL";

    public int LookbackDays { get; init; } = 7;

    public DateTimeOffset HistoricalBackfillStartUtc { get; init; } = new(2025, 10, 30, 0, 0, 0, TimeSpan.Zero);

    public int MaxBlockRange { get; init; } = 500;

    public int RequestDelayMilliseconds { get; init; } = 100;

    public bool BackgroundSyncEnabled { get; init; } = true;

    public int BackgroundSyncIdleDelaySeconds { get; init; } = 30;

    public int BackgroundErrorDelaySeconds { get; init; } = 60;

    public int BackgroundMaxErrorDelaySeconds { get; init; } = 900;

    public int BackgroundHistoricalBatchesPerCycle { get; init; } = 8;

    public int MarketEnrichmentBatchSize { get; init; } = 100;

    public int MarketEnrichmentMaxBatchesPerRun { get; init; } = 25;

    public bool BackgroundMarketEnrichmentEnabled { get; init; } = true;

    public int MarketEnrichmentIntervalSeconds { get; init; } = 120;

    public bool BackgroundPositionRefreshEnabled { get; init; } = true;

    public int PositionRefreshIntervalSeconds { get; init; } = 30;

    public int PositionRefreshTokenBatchSize { get; init; } = 50;

    public int PositionRefreshQueueSeedTokenBatchSize { get; init; } = 500;

    public bool BackgroundActivityRefreshEnabled { get; init; } = true;

    public int ActivityRefreshIntervalSeconds { get; init; } = 30;

    public int ActivityRefreshWalletBatchSize { get; init; } = 100;

    public int ActivityRefreshQueueSeedWalletBatchSize { get; init; } = 500;

    public bool BackgroundPerformanceRefreshEnabled { get; init; } = true;

    public int PerformanceRefreshIntervalSeconds { get; init; } = 30;

    public int PerformanceRefreshWalletBatchSize { get; init; } = 100;

    public int PerformanceRefreshQueueSeedWalletBatchSize { get; init; } = 500;

    public bool BackgroundCategoryPerformanceRefreshEnabled { get; init; } = true;

    public int CategoryPerformanceRefreshIntervalSeconds { get; init; } = 30;

    public int CategoryPerformancePairBatchSize { get; init; } = 500;

    public int CategoryPerformanceQueueSeedPairBatchSize { get; init; } = 1_000;

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
