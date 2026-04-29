namespace PolyCopyTrader.Domain.Configuration;

public sealed class AppConfiguration
{
    public BotOptions Bot { get; init; } = new();

    public RiskOptions Risk { get; init; } = new();

    public ExecutionOptions Execution { get; init; } = new();

    public PolymarketOptions Polymarket { get; init; } = new();

    public WatchlistOptions Watchlist { get; init; } = new();

    public PaperTradingOptions PaperTrading { get; init; } = new();

    public DashboardOptions Dashboard { get; init; } = new();

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

public sealed class PolymarketOptions
{
    public string DataApiBaseUrl { get; init; } = "https://data-api.polymarket.com";

    public string ClobBaseUrl { get; init; } = "https://clob.polymarket.com";

    public string GammaBaseUrl { get; init; } = "https://gamma-api.polymarket.com";

    public string GeoblockUrl { get; init; } = "https://polymarket.com/api/geoblock";
}

public sealed class WatchlistOptions
{
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

public sealed class StorageOptions
{
    public string DatabasePath { get; init; } = Path.Combine("data", "polycopytrader.db");
}
