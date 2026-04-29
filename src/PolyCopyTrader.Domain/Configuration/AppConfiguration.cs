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

    public DashboardOptions Dashboard { get; init; } = new();

    public AnalyticsOptions Analytics { get; init; } = new();

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
}

public sealed class PolymarketAuthOptions
{
    public bool Enabled { get; init; }

    public string SecretProvider { get; init; } = "Environment";

    public string SigningAddress { get; init; } = string.Empty;

    public string ApiKeyName { get; init; } = "POLYCOPYTRADER_POLYMARKET_API_KEY";

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
