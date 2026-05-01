namespace PolyCopyTrader.Domain.Configuration;

public static class AppOptionsValidator
{
    private const string Sha256SubjectPublicKeyInfoPinPrefix = "sha256/";

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
        ValidatePolymarket(configuration.Polymarket, configuration.MarketDataWebSocket, errors);
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
        ValidateTraderDiscovery(configuration.TraderDiscovery, errors);
        ValidateOnChainIngestion(configuration.OnChainIngestion, errors);
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
            $"Polymarket certificate pinned hosts: {configuration.Polymarket.CertificatePins?.Count ?? 0}",
            $"Auth enabled: {configuration.PolymarketAuth.Enabled}",
            $"Auth provider: {configuration.PolymarketAuth.SecretProvider}",
            $"Auth configured: {configuration.PolymarketAuth.Enabled && IsAddressLike(configuration.PolymarketAuth.SigningAddress)}",
            $"Dry-run signing enabled: {configuration.PolymarketAuth.DryRunSigningEnabled}",
            $"Live max order notional USD: {configuration.LiveTrading.MaxOrderNotionalUsd}",
            $"Live manual approval present: {!string.IsNullOrWhiteSpace(configuration.LiveTrading.ManualEnableCode)}",
            $"Market WebSocket enabled: {configuration.Bot.UseWebSockets && configuration.MarketDataWebSocket.Enabled}",
            $"Market WebSocket URL: {configuration.MarketDataWebSocket.MarketEndpointUrl}",
            $"Signal observe threshold: {configuration.Signal.ObserveBelowScore}",
            $"Signal requires market category: {configuration.Signal.RequireKnownMarketCategory}",
            $"Signal requires leader category performance: {configuration.Signal.RequireLeaderCategoryPerformance}",
            $"IPC enabled: {configuration.Ipc.Enabled}",
            $"IPC dashboard URL: {configuration.Ipc.DashboardBaseUrl}",
            $"Daily reports enabled: {configuration.Analytics.DailyReportGenerationEnabled}",
            $"Trader discovery enabled: {configuration.TraderDiscovery.Enabled}",
            $"Trader discovery category: {configuration.TraderDiscovery.Category}",
            $"Trader discovery time period: {configuration.TraderDiscovery.TimePeriod}",
            $"On-chain ingestion enabled: {configuration.OnChainIngestion.Enabled}",
            $"On-chain RPC configured: {IsOnChainRpcConfigured(configuration.OnChainIngestion)}",
            $"On-chain RPC host: {GetUriHost(ResolveOnChainRpcUrl(configuration.OnChainIngestion))}",
            $"On-chain lookback days: {configuration.OnChainIngestion.LookbackDays}",
            $"On-chain max block range: {configuration.OnChainIngestion.MaxBlockRange}",
            $"On-chain background sync enabled: {configuration.OnChainIngestion.BackgroundSyncEnabled}",
            $"On-chain background market enrichment enabled: {configuration.OnChainIngestion.BackgroundMarketEnrichmentEnabled}",
            $"On-chain background position refresh enabled: {configuration.OnChainIngestion.BackgroundPositionRefreshEnabled}",
            $"On-chain background activity refresh enabled: {configuration.OnChainIngestion.BackgroundActivityRefreshEnabled}",
            $"On-chain background performance refresh enabled: {configuration.OnChainIngestion.BackgroundPerformanceRefreshEnabled}",
            $"On-chain background category performance refresh enabled: {configuration.OnChainIngestion.BackgroundCategoryPerformanceRefreshEnabled}",
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

    private static void ValidatePolymarket(
        PolymarketOptions options,
        MarketDataWebSocketOptions marketDataWebSocketOptions,
        List<string> errors)
    {
        ValidateAbsoluteHttpsUrl(options.DataApiBaseUrl, "Polymarket.DataApiBaseUrl", errors);
        ValidateAbsoluteHttpsUrl(options.ClobBaseUrl, "Polymarket.ClobBaseUrl", errors);
        ValidateAbsoluteHttpsUrl(options.GammaBaseUrl, "Polymarket.GammaBaseUrl", errors);
        ValidateAbsoluteHttpsUrl(options.GeoblockUrl, "Polymarket.GeoblockUrl", errors);
        ValidateCertificatePins(options, marketDataWebSocketOptions, errors);

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

    private static void ValidateCertificatePins(
        PolymarketOptions options,
        MarketDataWebSocketOptions marketDataWebSocketOptions,
        List<string> errors)
    {
        if (options.CertificatePins is null)
        {
            errors.Add("Polymarket.CertificatePins must be an object keyed by endpoint host.");
            return;
        }

        var configuredHosts = GetConfiguredPolymarketHosts(options, marketDataWebSocketOptions);
        foreach (var entry in options.CertificatePins)
        {
            var host = entry.Key.Trim();
            if (string.IsNullOrWhiteSpace(host) ||
                host.Contains("://", StringComparison.Ordinal) ||
                Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                errors.Add("Polymarket.CertificatePins keys must be endpoint host names, for example data-api.polymarket.com.");
                continue;
            }

            if (!configuredHosts.Contains(host))
            {
                errors.Add($"Polymarket.CertificatePins host '{host}' must match a configured Polymarket endpoint host.");
            }

            if (entry.Value is null || entry.Value.Count == 0)
            {
                errors.Add($"Polymarket.CertificatePins for host '{host}' must contain at least one pin.");
                continue;
            }

            foreach (var pin in entry.Value)
            {
                if (!IsValidCertificatePin(pin))
                {
                    errors.Add($"Polymarket.CertificatePins for host '{host}' must use sha256/<base64-spki-hash> format.");
                }
            }
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

        if (options.MinLeaderCategoryResolvedPositions < 0)
        {
            errors.Add("Signal.MinLeaderCategoryResolvedPositions must not be negative.");
        }

        if (options.MinLeaderCategoryWinRatePct < 0m || options.MinLeaderCategoryWinRatePct > 100m)
        {
            errors.Add("Signal.MinLeaderCategoryWinRatePct must be between 0 and 100.");
        }

        if (!IsSupportedSampleQuality(options.MinLeaderCategorySampleQuality))
        {
            errors.Add("Signal.MinLeaderCategorySampleQuality must be Thin, Low, Medium, or High.");
        }

        if (options.LeaderCategoryPerformanceStaleAfterHours <= 0)
        {
            errors.Add("Signal.LeaderCategoryPerformanceStaleAfterHours must be greater than zero.");
        }

        if (options.LeaderCategoryPerformanceScore < 0)
        {
            errors.Add("Signal.LeaderCategoryPerformanceScore must not be negative.");
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

    private static void ValidateTraderDiscovery(TraderDiscoveryOptions options, List<string> errors)
    {
        if (!IsSupportedLeaderboardCategory(options.Category))
        {
            errors.Add("TraderDiscovery.Category is invalid.");
        }

        if (!IsSupportedLeaderboardTimePeriod(options.TimePeriod))
        {
            errors.Add("TraderDiscovery.TimePeriod must be DAY, WEEK, MONTH, or ALL.");
        }

        if (options.RefreshIntervalMinutes <= 0)
        {
            errors.Add("TraderDiscovery.RefreshIntervalMinutes must be greater than zero.");
        }

        if (options.LeaderboardPages <= 0 || options.LeaderboardPages > 21)
        {
            errors.Add("TraderDiscovery.LeaderboardPages must be between 1 and 21.");
        }

        if (options.CandidatesPerSide <= 0 || options.CandidatesPerSide > 50)
        {
            errors.Add("TraderDiscovery.CandidatesPerSide must be between 1 and 50.");
        }

        if (options.TradesPerCandidate <= 0 || options.TradesPerCandidate > 100)
        {
            errors.Add("TraderDiscovery.TradesPerCandidate must be between 1 and 100.");
        }

        if (options.PositionsPerCandidate <= 0 || options.PositionsPerCandidate > 500)
        {
            errors.Add("TraderDiscovery.PositionsPerCandidate must be between 1 and 500.");
        }

        if (options.RequestDelayMilliseconds < 0)
        {
            errors.Add("TraderDiscovery.RequestDelayMilliseconds must not be negative.");
        }
    }

    private static void ValidateOnChainIngestion(OnChainIngestionOptions options, List<string> errors)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.RpcUrlEnvironmentVariable) &&
            options.RpcUrlEnvironmentVariable.Any(char.IsWhiteSpace))
        {
            errors.Add("OnChainIngestion.RpcUrlEnvironmentVariable must not contain whitespace.");
        }

        var rpcUrl = ResolveOnChainRpcUrl(options);
        if (!Uri.TryCreate(rpcUrl, UriKind.Absolute, out var rpcUri) ||
            (rpcUri.Scheme != Uri.UriSchemeHttp && rpcUri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("OnChainIngestion.PolygonRpcUrl must be an absolute HTTP or HTTPS URL, or set the configured RPC URL environment variable.");
        }

        if (options.LookbackDays <= 0 || options.LookbackDays > 30)
        {
            errors.Add("OnChainIngestion.LookbackDays must be between 1 and 30 for the initial spike.");
        }

        if (options.MaxBlockRange <= 0 || options.MaxBlockRange > 10_000)
        {
            errors.Add("OnChainIngestion.MaxBlockRange must be between 1 and 10000 for the initial public-RPC spike.");
        }

        if (options.RequestDelayMilliseconds < 0)
        {
            errors.Add("OnChainIngestion.RequestDelayMilliseconds must not be negative.");
        }

        if (options.BackgroundSyncIdleDelaySeconds <= 0 || options.BackgroundSyncIdleDelaySeconds > 86_400)
        {
            errors.Add("OnChainIngestion.BackgroundSyncIdleDelaySeconds must be between 1 and 86400.");
        }

        if (options.BackgroundErrorDelaySeconds <= 0 || options.BackgroundErrorDelaySeconds > 86_400)
        {
            errors.Add("OnChainIngestion.BackgroundErrorDelaySeconds must be between 1 and 86400.");
        }

        if (options.BackgroundMaxErrorDelaySeconds < options.BackgroundErrorDelaySeconds ||
            options.BackgroundMaxErrorDelaySeconds > 86_400)
        {
            errors.Add("OnChainIngestion.BackgroundMaxErrorDelaySeconds must be at least BackgroundErrorDelaySeconds and at most 86400.");
        }

        if (options.MarketEnrichmentBatchSize <= 0 || options.MarketEnrichmentBatchSize > 1_000)
        {
            errors.Add("OnChainIngestion.MarketEnrichmentBatchSize must be between 1 and 1000.");
        }

        if (options.MarketEnrichmentMaxBatchesPerRun <= 0 || options.MarketEnrichmentMaxBatchesPerRun > 1_000)
        {
            errors.Add("OnChainIngestion.MarketEnrichmentMaxBatchesPerRun must be between 1 and 1000.");
        }

        if (options.MarketEnrichmentIntervalSeconds <= 0 || options.MarketEnrichmentIntervalSeconds > 86_400)
        {
            errors.Add("OnChainIngestion.MarketEnrichmentIntervalSeconds must be between 1 and 86400.");
        }

        if (options.PositionRefreshIntervalSeconds <= 0 || options.PositionRefreshIntervalSeconds > 86_400)
        {
            errors.Add("OnChainIngestion.PositionRefreshIntervalSeconds must be between 1 and 86400.");
        }

        if (options.PositionRefreshTokenBatchSize <= 0 || options.PositionRefreshTokenBatchSize > 10_000)
        {
            errors.Add("OnChainIngestion.PositionRefreshTokenBatchSize must be between 1 and 10000.");
        }

        if (options.PositionRefreshQueueSeedTokenBatchSize <= 0 || options.PositionRefreshQueueSeedTokenBatchSize > 100_000)
        {
            errors.Add("OnChainIngestion.PositionRefreshQueueSeedTokenBatchSize must be between 1 and 100000.");
        }

        if (options.ActivityRefreshIntervalSeconds <= 0 || options.ActivityRefreshIntervalSeconds > 86_400)
        {
            errors.Add("OnChainIngestion.ActivityRefreshIntervalSeconds must be between 1 and 86400.");
        }

        if (options.ActivityRefreshWalletBatchSize <= 0 || options.ActivityRefreshWalletBatchSize > 10_000)
        {
            errors.Add("OnChainIngestion.ActivityRefreshWalletBatchSize must be between 1 and 10000.");
        }

        if (options.ActivityRefreshQueueSeedWalletBatchSize <= 0 || options.ActivityRefreshQueueSeedWalletBatchSize > 100_000)
        {
            errors.Add("OnChainIngestion.ActivityRefreshQueueSeedWalletBatchSize must be between 1 and 100000.");
        }

        if (options.PerformanceRefreshIntervalSeconds <= 0 || options.PerformanceRefreshIntervalSeconds > 86_400)
        {
            errors.Add("OnChainIngestion.PerformanceRefreshIntervalSeconds must be between 1 and 86400.");
        }

        if (options.PerformanceRefreshWalletBatchSize <= 0 || options.PerformanceRefreshWalletBatchSize > 10_000)
        {
            errors.Add("OnChainIngestion.PerformanceRefreshWalletBatchSize must be between 1 and 10000.");
        }

        if (options.PerformanceRefreshQueueSeedWalletBatchSize <= 0 || options.PerformanceRefreshQueueSeedWalletBatchSize > 100_000)
        {
            errors.Add("OnChainIngestion.PerformanceRefreshQueueSeedWalletBatchSize must be between 1 and 100000.");
        }

        if (options.CategoryPerformanceRefreshIntervalSeconds <= 0 || options.CategoryPerformanceRefreshIntervalSeconds > 86_400)
        {
            errors.Add("OnChainIngestion.CategoryPerformanceRefreshIntervalSeconds must be between 1 and 86400.");
        }

        if (options.CategoryPerformancePairBatchSize <= 0 || options.CategoryPerformancePairBatchSize > 10_000)
        {
            errors.Add("OnChainIngestion.CategoryPerformancePairBatchSize must be between 1 and 10000.");
        }

        if (options.CategoryPerformanceQueueSeedPairBatchSize <= 0 || options.CategoryPerformanceQueueSeedPairBatchSize > 100_000)
        {
            errors.Add("OnChainIngestion.CategoryPerformanceQueueSeedPairBatchSize must be between 1 and 100000.");
        }

        if (options.ExchangeContracts.Count == 0)
        {
            errors.Add("OnChainIngestion.ExchangeContracts must contain at least one contract.");
            return;
        }

        foreach (var contract in options.ExchangeContracts)
        {
            if (string.IsNullOrWhiteSpace(contract.Name))
            {
                errors.Add("OnChainIngestion.ExchangeContracts entries require Name.");
            }

            if (!IsAddressLike(contract.Address))
            {
                errors.Add($"OnChainIngestion exchange contract '{contract.Name}' must have a 0x-prefixed address.");
            }

            if (!IsSupportedOnChainExchangeVersion(contract.Version))
            {
                errors.Add($"OnChainIngestion exchange contract '{contract.Name}' Version must be V1 or V2.");
            }
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

    private static HashSet<string> GetConfiguredPolymarketHosts(
        PolymarketOptions polymarket,
        MarketDataWebSocketOptions marketDataWebSocket)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddHost(polymarket.DataApiBaseUrl, hosts);
        AddHost(polymarket.ClobBaseUrl, hosts);
        AddHost(polymarket.GammaBaseUrl, hosts);
        AddHost(polymarket.GeoblockUrl, hosts);
        AddHost(marketDataWebSocket.MarketEndpointUrl, hosts);
        return hosts;
    }

    private static void AddHost(string value, HashSet<string> hosts)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            hosts.Add(uri.Host);
        }
    }

    private static bool IsValidCertificatePin(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith(Sha256SubjectPublicKeyInfoPinPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(value[Sha256SubjectPublicKeyInfoPinPrefix.Length..]);
            return bytes.Length == 32;
        }
        catch (FormatException)
        {
            return false;
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

    private static bool IsSupportedLeaderboardCategory(string value)
    {
        return string.Equals(value, "OVERALL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "POLITICS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "SPORTS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "CRYPTO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "CULTURE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "MENTIONS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "WEATHER", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "ECONOMICS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "TECH", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "FINANCE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedLeaderboardTimePeriod(string value)
    {
        return string.Equals(value, "DAY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "WEEK", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "MONTH", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "ALL", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedSampleQuality(string value)
    {
        return string.Equals(value, "Thin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Low", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Medium", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "High", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedOnChainExchangeVersion(string value)
    {
        return string.Equals(value, "V1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "V2", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOnChainRpcConfigured(OnChainIngestionOptions options)
    {
        return !string.IsNullOrWhiteSpace(ResolveOnChainRpcUrl(options));
    }

    private static string ResolveOnChainRpcUrl(OnChainIngestionOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.RpcUrlEnvironmentVariable))
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(options.RpcUrlEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment;
            }
        }

        return options.PolygonRpcUrl;
    }

    private static string GetUriHost(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.Host : "not configured";
    }

    private static bool IsAddressLike(string value)
    {
        return value.Length == 42 &&
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            value.Skip(2).All(Uri.IsHexDigit);
    }
}
