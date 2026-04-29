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
        ValidatePolymarketAuth(configuration.PolymarketAuth, errors);
        ValidateMarketDataWebSocket(configuration.MarketDataWebSocket, errors);
        ValidatePaperTrading(configuration.PaperTrading, errors);
        ValidateExecution(configuration.Execution, errors);
        ValidateSignal(configuration.Signal, errors);
        ValidateRisk(configuration.Risk, errors);
        ValidateWatchlist(configuration.Watchlist, errors);
        ValidateLiveTrading(configuration.Bot, configuration.PolymarketAuth, configuration.LiveTrading, errors);
        ValidateDashboard(configuration.Dashboard, errors);
        ValidateAnalytics(configuration.Analytics, errors);
        ValidateIpc(configuration.Ipc, errors);
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
            $"Storage provider: {configuration.Storage.Provider}",
            $"Storage configured: {StorageConnectionResolver.IsConfigured(configuration.Storage)}",
            $"Storage env var: {configuration.Storage.ConnectionStringEnvironmentVariable}",
            $"Polymarket data API: {configuration.Polymarket.DataApiBaseUrl}",
            $"Polymarket CLOB API: {configuration.Polymarket.ClobBaseUrl}",
            $"Auth enabled: {configuration.PolymarketAuth.Enabled}",
            $"Auth provider: {configuration.PolymarketAuth.SecretProvider}",
            $"Auth configured: {configuration.PolymarketAuth.Enabled && IsAddressLike(configuration.PolymarketAuth.SigningAddress)}",
            $"Dry-run signing enabled: {configuration.PolymarketAuth.DryRunSigningEnabled}",
            $"Live max order notional USD: {configuration.LiveTrading.MaxOrderNotionalUsd}",
            $"Live manual approval present: {!string.IsNullOrWhiteSpace(configuration.LiveTrading.ManualEnableCode)}",
            $"Market WebSocket enabled: {configuration.Bot.UseWebSockets && configuration.MarketDataWebSocket.Enabled}",
            $"Market WebSocket URL: {configuration.MarketDataWebSocket.MarketEndpointUrl}",
            $"Signal observe threshold: {configuration.Signal.ObserveBelowScore}",
            $"IPC enabled: {configuration.Ipc.Enabled}",
            $"IPC dashboard URL: {configuration.Ipc.DashboardBaseUrl}",
            $"Daily reports enabled: {configuration.Analytics.DailyReportGenerationEnabled}",
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

        if (options.EnableLiveTrading && options.Mode != BotMode.Live)
        {
            errors.Add("Bot.EnableLiveTrading requires Bot.Mode to be Live.");
        }
    }

    private static void ValidatePolymarket(PolymarketOptions options, List<string> errors)
    {
        ValidateAbsoluteHttpsUrl(options.DataApiBaseUrl, "Polymarket.DataApiBaseUrl", errors);
        ValidateAbsoluteHttpsUrl(options.ClobBaseUrl, "Polymarket.ClobBaseUrl", errors);
        ValidateAbsoluteHttpsUrl(options.GammaBaseUrl, "Polymarket.GammaBaseUrl", errors);
        ValidateAbsoluteHttpsUrl(options.GeoblockUrl, "Polymarket.GeoblockUrl", errors);

        if (options.TimeoutSeconds <= 0)
        {
            errors.Add("Polymarket.TimeoutSeconds must be greater than zero.");
        }

        if (options.MaxRetries < 0)
        {
            errors.Add("Polymarket.MaxRetries must not be negative.");
        }

        if (options.RetryBaseDelayMilliseconds < 0)
        {
            errors.Add("Polymarket.RetryBaseDelayMilliseconds must not be negative.");
        }
    }

    private static void ValidatePolymarketAuth(PolymarketAuthOptions options, List<string> errors)
    {
        if (!IsSupportedSecretProvider(options.SecretProvider))
        {
            errors.Add("PolymarketAuth.SecretProvider must be Environment or CredentialManager.");
        }

        if (options.ChainId <= 0)
        {
            errors.Add("PolymarketAuth.ChainId must be greater than zero.");
        }

        if (!IsSupportedSignatureType(options.SignatureType))
        {
            errors.Add("PolymarketAuth.SignatureType must be EOA, POLY_PROXY, POLY_GNOSIS_SAFE, or POLY_1271.");
        }

        if (!string.IsNullOrWhiteSpace(options.FunderAddress) && !IsAddressLike(options.FunderAddress))
        {
            errors.Add("PolymarketAuth.FunderAddress must be a 0x-prefixed Ethereum address when set.");
        }

        if (options.DryRunSigningEnabled && string.IsNullOrWhiteSpace(options.DryRunPrivateKeyName))
        {
            errors.Add("PolymarketAuth.DryRunPrivateKeyName is required when dry-run signing is enabled.");
        }

        if (options.DryRunSigningEnabled && !IsAddressLike(options.SigningAddress))
        {
            errors.Add("PolymarketAuth.SigningAddress must be a 0x-prefixed Ethereum address when dry-run signing is enabled.");
        }

        if (!options.Enabled)
        {
            return;
        }

        if (!IsAddressLike(options.SigningAddress))
        {
            errors.Add("PolymarketAuth.SigningAddress must be a 0x-prefixed Ethereum address when auth is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKeyName))
        {
            errors.Add("PolymarketAuth.ApiKeyName is required when auth is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKeyOwnerName))
        {
            errors.Add("PolymarketAuth.ApiKeyOwnerName is required when auth is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiSecretName))
        {
            errors.Add("PolymarketAuth.ApiSecretName is required when auth is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiPassphraseName))
        {
            errors.Add("PolymarketAuth.ApiPassphraseName is required when auth is enabled.");
        }
    }

    private static void ValidateMarketDataWebSocket(MarketDataWebSocketOptions options, List<string> errors)
    {
        if (!Uri.TryCreate(options.MarketEndpointUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != "wss")
        {
            errors.Add("MarketDataWebSocket.MarketEndpointUrl must be an absolute WSS URL.");
        }

        if (options.HeartbeatSeconds <= 0)
        {
            errors.Add("MarketDataWebSocket.HeartbeatSeconds must be greater than zero.");
        }

        if (options.ReconnectBaseDelaySeconds <= 0 || options.ReconnectMaxDelaySeconds <= 0)
        {
            errors.Add("MarketDataWebSocket reconnect delays must be greater than zero.");
        }

        if (options.ReconnectBaseDelaySeconds > options.ReconnectMaxDelaySeconds)
        {
            errors.Add("MarketDataWebSocket.ReconnectBaseDelaySeconds must not exceed ReconnectMaxDelaySeconds.");
        }

        if (options.SubscriptionRefreshSeconds <= 0)
        {
            errors.Add("MarketDataWebSocket.SubscriptionRefreshSeconds must be greater than zero.");
        }

        if (options.StaleAfterSeconds <= 0)
        {
            errors.Add("MarketDataWebSocket.StaleAfterSeconds must be greater than zero.");
        }

        if (options.ReceiveBufferBytes < 4096)
        {
            errors.Add("MarketDataWebSocket.ReceiveBufferBytes must be at least 4096.");
        }

        if (options.StrongSignalMinimumScore < 0)
        {
            errors.Add("MarketDataWebSocket.StrongSignalMinimumScore must not be negative.");
        }

        if (options.StrongSignalLookbackMinutes <= 0)
        {
            errors.Add("MarketDataWebSocket.StrongSignalLookbackMinutes must be greater than zero.");
        }

        if (options.MaxSubscribedAssets <= 0)
        {
            errors.Add("MarketDataWebSocket.MaxSubscribedAssets must be greater than zero.");
        }
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

    private static void ValidateSignal(SignalOptions options, List<string> errors)
    {
        if (options.IgnoreBelowScore < 0)
        {
            errors.Add("Signal.IgnoreBelowScore must not be negative.");
        }

        if (options.ObserveBelowScore <= options.IgnoreBelowScore)
        {
            errors.Add("Signal.ObserveBelowScore must be greater than Signal.IgnoreBelowScore.");
        }

        if (options.NormalPaperOrderScore <= options.ObserveBelowScore)
        {
            errors.Add("Signal.NormalPaperOrderScore must be greater than Signal.ObserveBelowScore.");
        }

        if (options.LargeLeaderTradeMultiplier <= 0m)
        {
            errors.Add("Signal.LargeLeaderTradeMultiplier must be greater than zero.");
        }

        if (options.MarketCloseWindowMinutes < 0)
        {
            errors.Add("Signal.MarketCloseWindowMinutes must not be negative.");
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

        if (options.MaxOpenOrders <= 0)
        {
            errors.Add("Risk.MaxOpenOrders must be greater than zero.");
        }

        if (options.MaxOrderAgeSeconds <= 0)
        {
            errors.Add("Risk.MaxOrderAgeSeconds must be greater than zero.");
        }
    }

    private static void ValidateWatchlist(WatchlistOptions options, List<string> errors)
    {
        if (options.MaxTradesPerTraderPerPoll <= 0)
        {
            errors.Add("Watchlist.MaxTradesPerTraderPerPoll must be greater than zero.");
        }

        if (options.MaxPositionsPerTraderPerPoll <= 0)
        {
            errors.Add("Watchlist.MaxPositionsPerTraderPerPoll must be greater than zero.");
        }

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

    private static void ValidateLiveTrading(
        BotOptions bot,
        PolymarketAuthOptions auth,
        LiveTradingOptions options,
        List<string> errors)
    {
        if (options.MaxOrderNotionalUsd <= 0m)
        {
            errors.Add("LiveTrading.MaxOrderNotionalUsd must be greater than zero.");
        }

        ValidatePct(options.MaxTradeBankrollPct, "LiveTrading.MaxTradeBankrollPct", errors);
        ValidatePct(options.MaxMarketBankrollPct, "LiveTrading.MaxMarketBankrollPct", errors);
        ValidatePct(options.MaxDailyLossPct, "LiveTrading.MaxDailyLossPct", errors);
        ValidatePct(options.MaxTotalDeployedPct, "LiveTrading.MaxTotalDeployedPct", errors);

        if (options.MaxTradeBankrollPct > options.MaxMarketBankrollPct)
        {
            errors.Add("LiveTrading.MaxTradeBankrollPct must not exceed LiveTrading.MaxMarketBankrollPct.");
        }

        if (options.DefaultOrderTtlSeconds <= 0 || options.DefaultOrderTtlSeconds > 300)
        {
            errors.Add("LiveTrading.DefaultOrderTtlSeconds must be between 1 and 300 seconds.");
        }

        if (options.MaxClockDriftSeconds <= 0)
        {
            errors.Add("LiveTrading.MaxClockDriftSeconds must be greater than zero.");
        }

        if (options.ApiErrorLockoutCount <= 0)
        {
            errors.Add("LiveTrading.ApiErrorLockoutCount must be greater than zero.");
        }

        if (options.ApiErrorLockoutWindowMinutes <= 0)
        {
            errors.Add("LiveTrading.ApiErrorLockoutWindowMinutes must be greater than zero.");
        }

        if (options.MaxOpenLiveOrders <= 0)
        {
            errors.Add("LiveTrading.MaxOpenLiveOrders must be greater than zero.");
        }

        if (!bot.EnableLiveTrading)
        {
            return;
        }

        if (!string.Equals(options.ManualEnableCode, "LIVE_TRADING_ENABLED", StringComparison.Ordinal))
        {
            errors.Add("LiveTrading.ManualEnableCode must be LIVE_TRADING_ENABLED when live trading is enabled.");
        }

        if (!auth.Enabled)
        {
            errors.Add("PolymarketAuth.Enabled must be true when live trading is enabled.");
        }

        if (!IsAddressLike(auth.SigningAddress))
        {
            errors.Add("PolymarketAuth.SigningAddress must be configured when live trading is enabled.");
        }

        if (!IsAddressLike(auth.FunderAddress))
        {
            errors.Add("PolymarketAuth.FunderAddress must be configured when live trading is enabled.");
        }

        if (string.IsNullOrWhiteSpace(auth.OrderSigningPrivateKeyName))
        {
            errors.Add("PolymarketAuth.OrderSigningPrivateKeyName is required when live trading is enabled.");
        }
    }

    private static void ValidateDashboard(DashboardOptions options, List<string> errors)
    {
        if (options.RefreshIntervalSeconds <= 0)
        {
            errors.Add("Dashboard.RefreshIntervalSeconds must be greater than zero.");
        }
    }

    private static void ValidateAnalytics(AnalyticsOptions options, List<string> errors)
    {
        if (options.DailyReportRefreshMinutes <= 0)
        {
            errors.Add("Analytics.DailyReportRefreshMinutes must be greater than zero.");
        }

        if (options.DashboardReportLimit <= 0)
        {
            errors.Add("Analytics.DashboardReportLimit must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.CsvExportDirectory))
        {
            errors.Add("Analytics.CsvExportDirectory is required.");
        }
    }

    private static void ValidateIpc(IpcOptions options, List<string> errors)
    {
        ValidateLoopbackHttpUrl(options.ListenUrl, "Ipc.ListenUrl", errors);
        ValidateLoopbackHttpUrl(options.DashboardBaseUrl, "Ipc.DashboardBaseUrl", errors);
    }

    private static void ValidateStorage(StorageOptions options, List<string> errors)
    {
        if (!string.Equals(options.Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Storage.Provider must be PostgreSQL.");
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionStringEnvironmentVariable))
        {
            errors.Add("Storage.ConnectionStringEnvironmentVariable is required.");
        }

        if (options.RequireConfiguredDatabase && !StorageConnectionResolver.IsConfigured(options))
        {
            errors.Add("Storage is required, but no PostgreSQL connection string is configured.");
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

    private static void ValidateLoopbackHttpUrl(string value, string name, List<string> errors)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            !uri.IsLoopback)
        {
            errors.Add($"{name} must be an absolute loopback HTTP URL.");
        }
    }

    private static bool IsSupportedSecretProvider(string value)
    {
        return string.Equals(value, "Environment", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "CredentialManager", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedSignatureType(string value)
    {
        return string.Equals(value, "EOA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "POLY_PROXY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "POLY_GNOSIS_SAFE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "POLY_1271", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAddressLike(string value)
    {
        return value.Length == 42 &&
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            value.Skip(2).All(Uri.IsHexDigit);
    }
}
