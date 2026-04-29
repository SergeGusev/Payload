namespace PolyCopyTrader.Domain.Configuration;

public static class AppOptionsValidator
{
    public static void ValidateAndThrow(AppConfiguration configuration)
    {
        var errors = Validate(configuration);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid PolyCopyTrader configuration: " + string.Join("; ", errors));
        }
    }

    public static IReadOnlyList<string> Validate(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = new List<string>();
        ValidateBot(configuration.Bot, errors);
        ValidatePolymarket(configuration.Polymarket, errors);
        ValidatePaperTrading(configuration.PaperTrading, errors);
        ValidateExecution(configuration.Execution, errors);
        ValidateRisk(configuration.Risk, errors);
        ValidateWatchlist(configuration.Watchlist, errors);
        ValidateDashboard(configuration.Dashboard, errors);
        ValidateStorage(configuration.Storage, errors);
        return errors;
    }

    public static string ToSanitizedSummary(AppConfiguration configuration)
    {
        return string.Join(
            Environment.NewLine,
            $"Mode: {configuration.Bot.Mode}",
            $"Live trading enabled: {configuration.Bot.EnableLiveTrading}",
            $"Poll interval seconds: {configuration.Bot.PollIntervalSeconds}",
            $"Database path: {configuration.Storage.DatabasePath}",
            $"Polymarket data API: {configuration.Polymarket.DataApiBaseUrl}",
            $"Polymarket CLOB API: {configuration.Polymarket.ClobBaseUrl}",
            $"Watchlist traders: {configuration.Watchlist.Traders.Count}",
            $"Paper bankroll USD: {configuration.PaperTrading.InitialBankrollUsd}");
    }

    private static void ValidateBot(BotOptions options, List<string> errors)
    {
        if (!Enum.IsDefined(options.Mode))
        {
            errors.Add("Bot.Mode is invalid.");
        }

        if (options.PollIntervalSeconds <= 0)
        {
            errors.Add("Bot.PollIntervalSeconds must be greater than zero.");
        }

        if (options.HeartbeatIntervalSeconds <= 0)
        {
            errors.Add("Bot.HeartbeatIntervalSeconds must be greater than zero.");
        }

        if (options.EnableLiveTrading)
        {
            errors.Add("Bot.EnableLiveTrading must remain false before the live trading task.");
        }
    }

    private static void ValidatePolymarket(PolymarketOptions options, List<string> errors)
    {
        ValidateAbsoluteHttpsUrl(options.DataApiBaseUrl, "Polymarket.DataApiBaseUrl", errors);
        ValidateAbsoluteHttpsUrl(options.ClobBaseUrl, "Polymarket.ClobBaseUrl", errors);
        ValidateAbsoluteHttpsUrl(options.GammaBaseUrl, "Polymarket.GammaBaseUrl", errors);
        ValidateAbsoluteHttpsUrl(options.GeoblockUrl, "Polymarket.GeoblockUrl", errors);
    }

    private static void ValidatePaperTrading(PaperTradingOptions options, List<string> errors)
    {
        if (options.InitialBankrollUsd <= 0m)
        {
            errors.Add("PaperTrading.InitialBankrollUsd must be greater than zero.");
        }

        if (options.DefaultOrderTtlSeconds <= 0)
        {
            errors.Add("PaperTrading.DefaultOrderTtlSeconds must be greater than zero.");
        }
    }

    private static void ValidateExecution(ExecutionOptions options, List<string> errors)
    {
        if (!options.MakerOnly)
        {
            errors.Add("Execution.MakerOnly must be true for MVP.");
        }

        if (options.AllowTaker)
        {
            errors.Add("Execution.AllowTaker must be false for MVP.");
        }

        if (options.MaxSlippageCents < 0m)
        {
            errors.Add("Execution.MaxSlippageCents must not be negative.");
        }

        if (options.MaxSpreadCents <= 0m)
        {
            errors.Add("Execution.MaxSpreadCents must be greater than zero.");
        }

        if (options.MaxSpreadPct <= 0m)
        {
            errors.Add("Execution.MaxSpreadPct must be greater than zero.");
        }

        if (options.MinLeaderTradeUsd < 0m)
        {
            errors.Add("Execution.MinLeaderTradeUsd must not be negative.");
        }
    }

    private static void ValidateRisk(RiskOptions options, List<string> errors)
    {
        ValidatePct(options.MaxTradeBankrollPct, "Risk.MaxTradeBankrollPct", errors);
        ValidatePct(options.MaxMarketBankrollPct, "Risk.MaxMarketBankrollPct", errors);
        ValidatePct(options.MaxTraderBankrollPct, "Risk.MaxTraderBankrollPct", errors);
        ValidatePct(options.MaxCategoryBankrollPct, "Risk.MaxCategoryBankrollPct", errors);
        ValidatePct(options.MaxTotalDeployedPct, "Risk.MaxTotalDeployedPct", errors);
        ValidatePct(options.MaxDailyLossPct, "Risk.MaxDailyLossPct", errors);

        if (options.MaxTradeBankrollPct > options.MaxMarketBankrollPct)
        {
            errors.Add("Risk.MaxTradeBankrollPct must not exceed Risk.MaxMarketBankrollPct.");
        }
    }

    private static void ValidateWatchlist(WatchlistOptions options, List<string> errors)
    {
        foreach (var trader in options.Traders)
        {
            if (string.IsNullOrWhiteSpace(trader.Name))
            {
                errors.Add("Watchlist trader Name is required.");
            }

            if (string.IsNullOrWhiteSpace(trader.Wallet))
            {
                errors.Add($"Watchlist trader '{trader.Name}' Wallet is required.");
            }

            if (trader.Enabled && trader.AllowedCategories.Count == 0)
            {
                errors.Add($"Watchlist trader '{trader.Name}' must have at least one allowed category.");
            }

            if (trader.MaxLagSeconds <= 0)
            {
                errors.Add($"Watchlist trader '{trader.Name}' MaxLagSeconds must be greater than zero.");
            }

            if (trader.MaxSpreadCents <= 0m || trader.MaxSpreadPct <= 0m)
            {
                errors.Add($"Watchlist trader '{trader.Name}' spread limits must be greater than zero.");
            }
        }
    }

    private static void ValidateDashboard(DashboardOptions options, List<string> errors)
    {
        if (options.RefreshIntervalSeconds <= 0)
        {
            errors.Add("Dashboard.RefreshIntervalSeconds must be greater than zero.");
        }
    }

    private static void ValidateStorage(StorageOptions options, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            errors.Add("Storage.DatabasePath is required.");
        }
    }

    private static void ValidatePct(decimal value, string name, List<string> errors)
    {
        if (value <= 0m || value > 100m)
        {
            errors.Add($"{name} must be greater than zero and at most 100.");
        }
    }

    private static void ValidateAbsoluteHttpsUrl(string value, string name, List<string> errors)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{name} must be an absolute HTTPS URL.");
        }
    }
}
