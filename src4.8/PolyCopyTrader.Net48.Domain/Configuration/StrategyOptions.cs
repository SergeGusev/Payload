namespace PolyCopyTrader.Domain.Configuration;

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

public sealed class PaperTradingOptions
{
    public decimal InitialBankrollUsd { get; init; } = 100m;

    public bool UseMinimumMarketOrderSize { get; init; } = true;
}
