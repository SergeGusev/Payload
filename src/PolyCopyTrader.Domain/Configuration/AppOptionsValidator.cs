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
        ValidateMarketTradeDiagnostics(configuration.MarketTradeDiagnostics, errors);
        ValidateBtcOrderBookLagDiagnostics(configuration.BtcOrderBookLagDiagnostics, errors);
        ValidateDataApiTraderIngestion(configuration.DataApiTraderIngestion, errors);
        ValidatePaperTrading(configuration.PaperTrading, errors);
        ValidateExecution(configuration.Execution, errors);
        ValidateSignal(configuration.Signal, errors);
        ValidateRisk(configuration.Risk, errors);
        ValidateWatchlist(configuration.Watchlist, errors);
        ValidatePolymarketHttpLogging(configuration.PolymarketHttpLogging, errors);
        ValidateLiveTrading(configuration.Bot, configuration.PolymarketAuth, configuration.LiveTrading, errors);
        ValidateDashboard(configuration.Dashboard, errors);
        ValidateAnalytics(configuration.Analytics, errors);
        ValidateTraderDiscovery(configuration.TraderDiscovery, errors);
        ValidateGammaMarketIngestion(configuration.GammaMarketIngestion, errors);
        ValidateBtcUpDown5mStrategy(configuration.BtcUpDown5mStrategy, errors);
        ValidateCoinbaseExchange(configuration.CoinbaseExchange, errors);
        ValidateBinanceBtcUsdReference(configuration.BinanceBtcUsdReference, errors);
        ValidateBinanceCryptoReference(configuration.BinanceCryptoReference, errors);
        ValidateBtcUpDown5mOddsArchive(configuration.BtcUpDown5mOddsArchive, errors);
        ValidateCryptoUpDown5mOddsArchive(configuration.CryptoUpDown5mOddsArchive, errors);
        ValidateChainlinkBtcUsdDiagnostics(configuration.ChainlinkBtcUsdDiagnostics, errors);
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
            $"Polymarket HTTP logging enabled: {configuration.PolymarketHttpLogging.Enabled}",
            $"Polymarket HTTP logging persists successes: {configuration.PolymarketHttpLogging.PersistSuccessfulRequests}",
            $"Polymarket HTTP logging success sample rate: {configuration.PolymarketHttpLogging.SuccessfulRequestSampleRate}",
            $"Polymarket HTTP log cleanup enabled: {configuration.PolymarketHttpLogging.CleanupEnabled}",
            $"Auth enabled: {configuration.PolymarketAuth.Enabled}",
            $"Auth provider: {configuration.PolymarketAuth.SecretProvider}",
            $"Auth configured: {configuration.PolymarketAuth.Enabled && IsAddressLike(configuration.PolymarketAuth.SigningAddress)}",
            $"Dry-run signing enabled: {configuration.PolymarketAuth.DryRunSigningEnabled}",
            $"Live max order notional USD: {configuration.LiveTrading.MaxOrderNotionalUsd}",
            $"Live manual approval present: {!string.IsNullOrWhiteSpace(configuration.LiveTrading.ManualEnableCode)}",
            $"Market WebSocket enabled: {configuration.Bot.UseWebSockets && configuration.MarketDataWebSocket.Enabled}",
            $"Market WebSocket subscription scope: {configuration.MarketDataWebSocket.SubscriptionScope}",
            $"Market WebSocket URL: {configuration.MarketDataWebSocket.MarketEndpointUrl}",
            $"Market WebSocket subscription batch size: {configuration.MarketDataWebSocket.SubscriptionBatchSize}",
            $"Market WebSocket shard max assets: {configuration.MarketDataWebSocket.ShardMaxAssets}",
            $"Market WebSocket max shard connections: {configuration.MarketDataWebSocket.MaxShardConnections}",
            $"Market WebSocket watchdog interval seconds: {configuration.MarketDataWebSocket.WatchdogIntervalSeconds}",
            $"Market WebSocket watchdog stale seconds: {configuration.MarketDataWebSocket.WatchdogStaleSeconds}",
            $"Market WebSocket persists order book snapshots: {configuration.MarketDataWebSocket.PersistOrderBookSnapshots}",
            $"Market WebSocket persists market data events: {configuration.MarketDataWebSocket.PersistMarketDataEvents}",
            $"Market trade diagnostics enabled: {configuration.MarketTradeDiagnostics.Enabled}",
            $"BTC/order-book lag diagnostics enabled: {configuration.BtcOrderBookLagDiagnostics.Enabled}",
            $"BTC/order-book lag diagnostics retention minutes: {configuration.BtcOrderBookLagDiagnostics.RetentionMinutes}",
            $"Data API trader ingestion enabled: {configuration.DataApiTraderIngestion.Enabled}",
            $"Data API trader ingestion global limit: {configuration.DataApiTraderIngestion.GlobalTradesLimit}",
            $"Data API trader ingestion poll delay milliseconds: {configuration.DataApiTraderIngestion.PollDelayMilliseconds}",
            $"Data API trader sync batch size: {configuration.DataApiTraderIngestion.SyncBatchSize}",
            $"Data API trader sync poll delay milliseconds: {configuration.DataApiTraderIngestion.SyncPollDelayMilliseconds}",
            $"Data API trader ingestion user max offset: {configuration.DataApiTraderIngestion.MaxUserHistoricalOffset}",
            $"Data API trader position refresh enabled: {configuration.DataApiTraderIngestion.RefreshPositionsEnabled}",
            $"Data API Polymarket-only ratings enabled: {configuration.DataApiTraderIngestion.RefreshPolymarketRatingsEnabled}",
            $"Data API Polymarket rating time period: {configuration.DataApiTraderIngestion.PolymarketRatingTimePeriod}",
            $"Data API Polymarket rating refresh interval seconds: {configuration.DataApiTraderIngestion.PolymarketRatingRefreshIntervalSeconds}",
            $"Data API Polymarket rating positions enabled: {configuration.DataApiTraderIngestion.PolymarketRatingPositionsEnabled}",
            $"Data API trader max position refreshes per cycle: {configuration.DataApiTraderIngestion.MaxPositionRefreshesPerCycle}",
            $"Signal observe threshold: {configuration.Signal.ObserveBelowScore}",
            $"Signal requires market category: {configuration.Signal.RequireKnownMarketCategory}",
            $"Signal requires leader category performance: {configuration.Signal.RequireLeaderCategoryPerformance}",
            $"Signal copied trader performance guard: {configuration.Signal.CopiedTraderPerformanceGuardEnabled}",
            $"Signal copied trader performance min settled positions: {configuration.Signal.CopiedTraderPerformanceMinSettledPositions}",
            $"Signal copied trader performance min score: {configuration.Signal.CopiedTraderPerformanceMinScore}",
            $"IPC enabled: {configuration.Ipc.Enabled}",
            $"IPC dashboard URL: {configuration.Ipc.DashboardBaseUrl}",
            $"Daily reports enabled: {configuration.Analytics.DailyReportGenerationEnabled}",
            $"Trader discovery enabled: {configuration.TraderDiscovery.Enabled}",
            $"Trader discovery category: {configuration.TraderDiscovery.Category}",
            $"Trader discovery time period: {configuration.TraderDiscovery.TimePeriod}",
            $"Gamma market ingestion enabled: {configuration.GammaMarketIngestion.Enabled}",
            $"Gamma market ingestion poll interval seconds: {configuration.GammaMarketIngestion.PollIntervalSeconds}",
            $"Gamma market ingestion page limit: {configuration.GammaMarketIngestion.PageLimit}",
            $"BTC Up or Down 5m strategy enabled: {configuration.BtcUpDown5mStrategy.Enabled}",
            $"BTC Up or Down 5m strategy poll interval seconds: {configuration.BtcUpDown5mStrategy.PollIntervalSeconds}",
            $"BTC Up or Down 5m strategy stake multiplier: {configuration.BtcUpDown5mStrategy.StakeUsd}",
            $"BTC Up or Down 5m strategy entry grace seconds: {configuration.BtcUpDown5mStrategy.EntryGraceSeconds}",
            $"BTC Up or Down 5m strategy max concurrent entry decisions: {configuration.BtcUpDown5mStrategy.MaxConcurrentEntryDecisions}",
            $"BTC Up or Down 5m Paper taker pricing enabled: {configuration.BtcUpDown5mStrategy.PaperTakerPricingEnabled}",
            $"BTC Up or Down 5m Paper taker max quote age ms: {configuration.BtcUpDown5mStrategy.PaperTakerMaxQuoteAgeMilliseconds}",
            $"BTC Up or Down 5m Paper taker max entry price: {configuration.BtcUpDown5mStrategy.PaperTakerMaxEntryPrice}",
            $"BTC Up or Down 5m Paper taker max reference slippage: {configuration.BtcUpDown5mStrategy.PaperTakerMaxReferenceSlippage}",
            $"BTC Up or Down 5m Paper taker max spread abs: {configuration.BtcUpDown5mStrategy.PaperTakerMaxSpreadAbs}",
            $"BTC Up or Down 5m Paper taker max Gamma/CLOB diff: {configuration.BtcUpDown5mStrategy.PaperTakerMaxGammaClobDiff}",
            $"BTC Up or Down 5m opening limit dynamic break-even pricing enabled: {configuration.BtcUpDown5mStrategy.OpeningLimitDynamicBreakEvenPricingEnabled}",
            $"BTC Up or Down 5m opening limit break-even lookback runs: {configuration.BtcUpDown5mStrategy.OpeningLimitBreakEvenLookbackRuns}",
            $"BTC Up or Down 5m opening limit break-even min settled runs: {configuration.BtcUpDown5mStrategy.OpeningLimitBreakEvenMinSettledRuns}",
            $"BTC Up or Down 5m opening limit break-even margin: {configuration.BtcUpDown5mStrategy.OpeningLimitBreakEvenMargin}",
            $"BTC Up or Down 5m opening limit max price: {configuration.BtcUpDown5mStrategy.OpeningLimitMaxPrice}",
            $"BTC Up or Down 5m opening limit GTD TTL seconds: {configuration.BtcUpDown5mStrategy.OpeningLimitGtdTtlSeconds}",
            $"BTC Up or Down 5m opening limit expire before market end seconds: {configuration.BtcUpDown5mStrategy.OpeningLimitExpireBeforeMarketEndSeconds}",
            $"BTC Up or Down 5m CLOB GTD expiration security buffer seconds: {configuration.BtcUpDown5mStrategy.ClobGtdExpirationSecurityBufferSeconds}",
            $"BTC Up or Down 5m order-book refresh worker enabled: {configuration.BtcUpDown5mStrategy.OrderBookRefreshWorkerEnabled}",
            $"BTC Up or Down 5m order-book refresh interval ms: {configuration.BtcUpDown5mStrategy.OrderBookRefreshIntervalMilliseconds}",
            $"BTC Up or Down 5m order-book refresh max markets: {configuration.BtcUpDown5mStrategy.OrderBookRefreshMaxMarketsPerCycle}",
            $"BTC Up or Down 5m strategy enabled variant codes: {GetBtcUpDown5mEnabledVariantCount(configuration.BtcUpDown5mStrategy)}",
            $"Coinbase Exchange BTC/USD reference enabled: {configuration.CoinbaseExchange.Enabled}",
            $"Coinbase Exchange base URL: {configuration.CoinbaseExchange.BaseUrl}",
            $"Coinbase Exchange product id: {configuration.CoinbaseExchange.ProductId}",
            $"Coinbase Exchange poll interval seconds: {configuration.CoinbaseExchange.PollIntervalSeconds}",
            $"Coinbase Exchange window size: {configuration.CoinbaseExchange.WindowSize}",
            $"Coinbase Exchange user agent configured: {!string.IsNullOrWhiteSpace(configuration.CoinbaseExchange.UserAgent)}",
            $"Binance BTC/USD reference enabled: {configuration.BinanceBtcUsdReference.Enabled}",
            $"Binance BTC/USD stream URL: {configuration.BinanceBtcUsdReference.StreamUrl}",
            $"Binance BTC/USD sample interval seconds: {configuration.BinanceBtcUsdReference.SampleIntervalSeconds}",
            $"Binance BTC/USD window size: {configuration.BinanceBtcUsdReference.WindowSize}",
            $"Binance BTC/USD stale after seconds: {configuration.BinanceBtcUsdReference.StaleAfterSeconds}",
            $"Binance crypto reference enabled: {configuration.BinanceCryptoReference.Enabled}",
            $"Binance crypto reference stream base URL: {configuration.BinanceCryptoReference.CombinedStreamBaseUrl}",
            $"Binance crypto reference assets: {string.Join(",", configuration.BinanceCryptoReference.AssetSymbols)}",
            $"Binance crypto reference stale after seconds: {configuration.BinanceCryptoReference.StaleAfterSeconds}",
            $"BTC Up or Down 5m odds archive enabled: {configuration.BtcUpDown5mOddsArchive.Enabled}",
            $"BTC Up or Down 5m odds archive poll interval seconds: {configuration.BtcUpDown5mOddsArchive.PollIntervalSeconds}",
            $"BTC Up or Down 5m odds archive max book age ms: {configuration.BtcUpDown5mOddsArchive.MaxOrderBookAgeMilliseconds}",
            $"Crypto Up or Down 5m odds archive enabled: {configuration.CryptoUpDown5mOddsArchive.Enabled}",
            $"Crypto Up or Down 5m odds archive assets: {string.Join(",", configuration.CryptoUpDown5mOddsArchive.AssetSymbols)}",
            $"Crypto Up or Down 5m odds archive poll interval seconds: {configuration.CryptoUpDown5mOddsArchive.PollIntervalSeconds}",
            $"Crypto Up or Down 5m odds archive max book age ms: {configuration.CryptoUpDown5mOddsArchive.MaxOrderBookAgeMilliseconds}",
            $"Chainlink BTC/USD diagnostics enabled: {configuration.ChainlinkBtcUsdDiagnostics.Enabled}",
            $"Chainlink BTC/USD diagnostics base URL: {configuration.ChainlinkBtcUsdDiagnostics.BaseUrl}",
            $"Chainlink BTC/USD diagnostics poll interval seconds: {configuration.ChainlinkBtcUsdDiagnostics.PollIntervalSeconds}",
            $"Chainlink BTC/USD diagnostics max nearest age seconds: {configuration.ChainlinkBtcUsdDiagnostics.MaxNearestAgeSeconds}",
            $"On-chain ingestion enabled: {configuration.OnChainIngestion.Enabled}",
            $"On-chain trade capture enabled: {configuration.OnChainIngestion.TradeCaptureEnabled}",
            $"On-chain trade capture persists captures: {configuration.OnChainIngestion.TradeCapturePersistCaptures}",
            $"On-chain trade capture skips stale cursor: {configuration.OnChainIngestion.TradeCaptureSkipStaleCursor}",
            $"On-chain paper signals enabled: {configuration.OnChainIngestion.PaperSignalEnabled}",
            $"On-chain paper signal backlog enabled: {configuration.OnChainIngestion.PaperSignalBacklogEnabled}",
            $"On-chain paper signal hot path enabled: {configuration.OnChainIngestion.PaperSignalHotPathEnabled}",
            $"On-chain RPC configured: {IsOnChainRpcConfigured(configuration.OnChainIngestion)}",
            $"On-chain RPC host: {GetUriHost(ResolveOnChainRpcUrl(configuration.OnChainIngestion))}",
            $"On-chain lookback days: {configuration.OnChainIngestion.LookbackDays}",
            $"On-chain max block range: {configuration.OnChainIngestion.MaxBlockRange}",
            $"On-chain trade capture poll delay milliseconds: {configuration.OnChainIngestion.TradeCapturePollDelayMilliseconds}",
            $"On-chain trade capture confirmations: {configuration.OnChainIngestion.TradeCaptureConfirmations}",
            $"On-chain paper signal poll delay milliseconds: {configuration.OnChainIngestion.PaperSignalPollDelayMilliseconds}",
            $"On-chain paper signal batch size: {configuration.OnChainIngestion.PaperSignalBatchSize}",
            $"On-chain paper signal hot max age seconds: {configuration.OnChainIngestion.PaperSignalHotMaxAgeSeconds}",
            $"On-chain paper signal latest candidates limit: {configuration.OnChainIngestion.PaperSignalLatestCandidatesLimit}",
            $"On-chain background sync enabled: {configuration.OnChainIngestion.BackgroundSyncEnabled}",
            $"On-chain background market enrichment enabled: {configuration.OnChainIngestion.BackgroundMarketEnrichmentEnabled}",
            $"On-chain background position refresh enabled: {configuration.OnChainIngestion.BackgroundPositionRefreshEnabled}",
            $"On-chain background activity refresh enabled: {configuration.OnChainIngestion.BackgroundActivityRefreshEnabled}",
            $"On-chain background performance refresh enabled: {configuration.OnChainIngestion.BackgroundPerformanceRefreshEnabled}",
            $"On-chain background category performance refresh enabled: {configuration.OnChainIngestion.BackgroundCategoryPerformanceRefreshEnabled}",
            $"On-chain background signal candidate refresh enabled: {configuration.OnChainIngestion.BackgroundSignalCandidateRefreshEnabled}",
            $"Watchlist traders: {configuration.Watchlist.Traders.Count}",
            $"Paper runs in live mode: {configuration.PaperTrading.RunInLiveMode}",
            $"Paper bankroll USD: {configuration.PaperTrading.InitialBankrollUsd}",
            $"Paper uses minimum market order size: {configuration.PaperTrading.UseMinimumMarketOrderSize}",
            $"Paper settlement enabled: {configuration.PaperTrading.SettlementEnabled}",
            $"Paper leader activity exit tracking enabled: {configuration.PaperTrading.LeaderActivityExitTrackingEnabled}",
            $"Paper leader activity exit tracking poll delay milliseconds: {configuration.PaperTrading.LeaderActivityExitTrackingPollDelayMilliseconds}",
            $"Paper copied trader performance refresh seconds: {configuration.PaperTrading.CopiedTraderPerformanceRefreshSeconds}");
    }

    private static int GetBtcUpDown5mEnabledVariantCount(BtcUpDown5mStrategyOptions options)
    {
        return options.EnabledVariantCodes is null || options.EnabledVariantCodes.Count == 0
            ? StrategyIds.BtcUpDown5mVariants.Count
            : options.EnabledVariantCodes.Count;
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

    private static void ValidatePolymarketHttpLogging(PolymarketHttpLoggingOptions options, List<string> errors)
    {
        if (options.SuccessfulRequestSampleRate < 0)
        {
            errors.Add("PolymarketHttpLogging.SuccessfulRequestSampleRate must not be negative.");
        }

        if (options.CleanupIntervalMinutes <= 0)
        {
            errors.Add("PolymarketHttpLogging.CleanupIntervalMinutes must be greater than zero.");
        }

        if (options.CleanupBatchSize <= 0)
        {
            errors.Add("PolymarketHttpLogging.CleanupBatchSize must be greater than zero.");
        }

        if (options.CleanupMaxBatchesPerCycle <= 0)
        {
            errors.Add("PolymarketHttpLogging.CleanupMaxBatchesPerCycle must be greater than zero.");
        }

        if (options.SuccessfulRetentionHours <= 0)
        {
            errors.Add("PolymarketHttpLogging.SuccessfulRetentionHours must be greater than zero.");
        }

        if (options.FailedRetentionDays <= 0)
        {
            errors.Add("PolymarketHttpLogging.FailedRetentionDays must be greater than zero.");
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
        if (!Enum.IsDefined(options.SubscriptionScope))
        {
            errors.Add("MarketDataWebSocket.SubscriptionScope must be AllActiveMarkets or BtcUpDown5mOnly.");
        }

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

        if (options.SubscriptionBatchSize <= 0)
        {
            errors.Add("MarketDataWebSocket.SubscriptionBatchSize must be greater than zero.");
        }

        if (options.ShardMaxAssets <= 0)
        {
            errors.Add("MarketDataWebSocket.ShardMaxAssets must be greater than zero.");
        }

        if (options.MaxShardConnections < 0)
        {
            errors.Add("MarketDataWebSocket.MaxShardConnections must not be negative. Use 0 for unlimited shard connections.");
        }

        if (options.WatchdogIntervalSeconds <= 0)
        {
            errors.Add("MarketDataWebSocket.WatchdogIntervalSeconds must be greater than zero.");
        }

        if (options.WatchdogStaleSeconds < 0)
        {
            errors.Add("MarketDataWebSocket.WatchdogStaleSeconds must not be negative. Use 0 to disable stale shard restarts.");
        }

        if (options.WatchdogStaleSeconds > 0 &&
            options.WatchdogStaleSeconds <= options.HeartbeatSeconds)
        {
            errors.Add("MarketDataWebSocket.WatchdogStaleSeconds must exceed HeartbeatSeconds when stale shard restarts are enabled.");
        }

        if (options.StatusPersistIntervalSeconds <= 0)
        {
            errors.Add("MarketDataWebSocket.StatusPersistIntervalSeconds must be greater than zero.");
        }

        if (options.StrongSignalMinimumScore < 0)
        {
            errors.Add("MarketDataWebSocket.StrongSignalMinimumScore must not be negative.");
        }

        if (options.StrongSignalLookbackMinutes <= 0)
        {
            errors.Add("MarketDataWebSocket.StrongSignalLookbackMinutes must be greater than zero.");
        }

        if (options.MaxSubscribedAssets < 0)
        {
            errors.Add("MarketDataWebSocket.MaxSubscribedAssets must not be negative. Use 0 for unlimited subscriptions.");
        }
    }

    private static void ValidateMarketTradeDiagnostics(MarketTradeDiagnosticsOptions options, List<string> errors)
    {
        if (options.MarketTradesLimit <= 0 || options.MarketTradesLimit > 10_000)
        {
            errors.Add("MarketTradeDiagnostics.MarketTradesLimit must be between 1 and 10000.");
        }

        if (options.MatchTimestampToleranceSeconds < 0 || options.MatchTimestampToleranceSeconds > 300)
        {
            errors.Add("MarketTradeDiagnostics.MatchTimestampToleranceSeconds must be between 0 and 300.");
        }
    }

    private static void ValidateBtcOrderBookLagDiagnostics(BtcOrderBookLagDiagnosticsOptions options, List<string> errors)
    {
        if (options.FlushIntervalMilliseconds <= 0 || options.FlushIntervalMilliseconds > 60_000)
        {
            errors.Add("BtcOrderBookLagDiagnostics.FlushIntervalMilliseconds must be between 1 and 60000.");
        }

        if (options.MaxBatchSize <= 0 || options.MaxBatchSize > 100_000)
        {
            errors.Add("BtcOrderBookLagDiagnostics.MaxBatchSize must be between 1 and 100000.");
        }

        if (options.MaxQueueSize <= 0 || options.MaxQueueSize > 1_000_000)
        {
            errors.Add("BtcOrderBookLagDiagnostics.MaxQueueSize must be between 1 and 1000000.");
        }

        if (options.RetentionMinutes <= 0 || options.RetentionMinutes > 10_080)
        {
            errors.Add("BtcOrderBookLagDiagnostics.RetentionMinutes must be between 1 and 10080.");
        }

        if (options.CleanupIntervalMinutes <= 0 || options.CleanupIntervalMinutes > 1_440)
        {
            errors.Add("BtcOrderBookLagDiagnostics.CleanupIntervalMinutes must be between 1 and 1440.");
        }

        if (options.CleanupBatchSize <= 0 || options.CleanupBatchSize > 1_000_000)
        {
            errors.Add("BtcOrderBookLagDiagnostics.CleanupBatchSize must be between 1 and 1000000.");
        }

        ValidateAbsoluteHttpsUrl(options.BinanceBookTickerUrl, "BtcOrderBookLagDiagnostics.BinanceBookTickerUrl", errors);

        if (options.BinanceBookTickerPollIntervalMilliseconds <= 0 || options.BinanceBookTickerPollIntervalMilliseconds > 60_000)
        {
            errors.Add("BtcOrderBookLagDiagnostics.BinanceBookTickerPollIntervalMilliseconds must be between 1 and 60000.");
        }

        if (options.BinanceBookTickerTimeoutMilliseconds <= 0 || options.BinanceBookTickerTimeoutMilliseconds > 60_000)
        {
            errors.Add("BtcOrderBookLagDiagnostics.BinanceBookTickerTimeoutMilliseconds must be between 1 and 60000.");
        }
    }

    private static void ValidateDataApiTraderIngestion(DataApiTraderIngestionOptions options, List<string> errors)
    {
        if (options.GlobalTradesLimit <= 0 || options.GlobalTradesLimit > 1_000)
        {
            errors.Add("DataApiTraderIngestion.GlobalTradesLimit must be between 1 and 1000.");
        }

        if (options.PollDelayMilliseconds < 0)
        {
            errors.Add("DataApiTraderIngestion.PollDelayMilliseconds must not be negative.");
        }

        if (options.UserTradesLimit <= 0 || options.UserTradesLimit > 1_000)
        {
            errors.Add("DataApiTraderIngestion.UserTradesLimit must be between 1 and 1000.");
        }

        if (options.MaxUserHistoricalOffset < 0 || options.MaxUserHistoricalOffset > 10_000)
        {
            errors.Add("DataApiTraderIngestion.MaxUserHistoricalOffset must be between 0 and 10000.");
        }

        if (options.MaxTradersPerCycle <= 0 || options.MaxTradersPerCycle > 10_000)
        {
            errors.Add("DataApiTraderIngestion.MaxTradersPerCycle must be between 1 and 10000.");
        }

        if (options.SyncBatchSize <= 0 || options.SyncBatchSize > 1_000)
        {
            errors.Add("DataApiTraderIngestion.SyncBatchSize must be between 1 and 1000.");
        }

        if (options.SyncPollDelayMilliseconds < 0)
        {
            errors.Add("DataApiTraderIngestion.SyncPollDelayMilliseconds must not be negative.");
        }

        if (options.ExistingTraderRefreshIntervalSeconds < 0)
        {
            errors.Add("DataApiTraderIngestion.ExistingTraderRefreshIntervalSeconds must not be negative.");
        }

        if (string.IsNullOrWhiteSpace(options.PolymarketRatingTimePeriod))
        {
            errors.Add("DataApiTraderIngestion.PolymarketRatingTimePeriod must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.PolymarketRatingOrderBy))
        {
            errors.Add("DataApiTraderIngestion.PolymarketRatingOrderBy must not be empty.");
        }

        if (options.PolymarketRatingRefreshIntervalSeconds <= 0)
        {
            errors.Add("DataApiTraderIngestion.PolymarketRatingRefreshIntervalSeconds must be greater than zero.");
        }

        if (options.PolymarketRatingFailureDelaySeconds <= 0)
        {
            errors.Add("DataApiTraderIngestion.PolymarketRatingFailureDelaySeconds must be greater than zero.");
        }

        if (options.PolymarketRatingRequestDelayMilliseconds < 0)
        {
            errors.Add("DataApiTraderIngestion.PolymarketRatingRequestDelayMilliseconds must not be negative.");
        }

        if (options.PolymarketRatingCurrentPositionsLimit <= 0 || options.PolymarketRatingCurrentPositionsLimit > 500)
        {
            errors.Add("DataApiTraderIngestion.PolymarketRatingCurrentPositionsLimit must be between 1 and 500.");
        }

        if (options.PolymarketRatingMaxCurrentPositionsOffset < 0 || options.PolymarketRatingMaxCurrentPositionsOffset > 10_000)
        {
            errors.Add("DataApiTraderIngestion.PolymarketRatingMaxCurrentPositionsOffset must be between 0 and 10000.");
        }

        if (options.PolymarketRatingClosedPositionsLimit <= 0 || options.PolymarketRatingClosedPositionsLimit > 50)
        {
            errors.Add("DataApiTraderIngestion.PolymarketRatingClosedPositionsLimit must be between 1 and 50.");
        }

        if (options.PolymarketRatingMaxClosedPositionsOffset < 0 || options.PolymarketRatingMaxClosedPositionsOffset > 100_000)
        {
            errors.Add("DataApiTraderIngestion.PolymarketRatingMaxClosedPositionsOffset must be between 0 and 100000.");
        }

        if (options.MaxPositionRefreshesPerCycle < 0 || options.MaxPositionRefreshesPerCycle > options.MaxTradersPerCycle)
        {
            errors.Add("DataApiTraderIngestion.MaxPositionRefreshesPerCycle must be between 0 and MaxTradersPerCycle.");
        }

        if (options.CurrentPositionsLimit <= 0 || options.CurrentPositionsLimit > 500)
        {
            errors.Add("DataApiTraderIngestion.CurrentPositionsLimit must be between 1 and 500.");
        }

        if (options.MaxCurrentPositionsOffset < 0 || options.MaxCurrentPositionsOffset > 10_000)
        {
            errors.Add("DataApiTraderIngestion.MaxCurrentPositionsOffset must be between 0 and 10000.");
        }

        if (options.ClosedPositionsLimit <= 0 || options.ClosedPositionsLimit > 50)
        {
            errors.Add("DataApiTraderIngestion.ClosedPositionsLimit must be between 1 and 50.");
        }

        if (options.MaxClosedPositionsOffset < 0 || options.MaxClosedPositionsOffset > 100_000)
        {
            errors.Add("DataApiTraderIngestion.MaxClosedPositionsOffset must be between 0 and 100000.");
        }

        if (options.ErrorDelayMilliseconds <= 0)
        {
            errors.Add("DataApiTraderIngestion.ErrorDelayMilliseconds must be greater than zero.");
        }

        if (options.MaxErrorDelayMilliseconds < options.ErrorDelayMilliseconds)
        {
            errors.Add("DataApiTraderIngestion.MaxErrorDelayMilliseconds must be greater than or equal to ErrorDelayMilliseconds.");
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

        if (options.OpenOrderProcessingIntervalSeconds <= 0)
        {
            errors.Add("PaperTrading.OpenOrderProcessingIntervalSeconds must be greater than zero.");
        }

        if (options.OpenOrderFillSimulationBatchSize <= 0)
        {
            errors.Add("PaperTrading.OpenOrderFillSimulationBatchSize must be greater than zero.");
        }

        if (options.SettlementPollIntervalSeconds <= 0)
        {
            errors.Add("PaperTrading.SettlementPollIntervalSeconds must be greater than zero.");
        }

        if (options.CopiedTraderPerformanceRefreshSeconds <= 0)
        {
            errors.Add("PaperTrading.CopiedTraderPerformanceRefreshSeconds must be greater than zero.");
        }

        if (options.LeaderActivityExitTrackingPollDelayMilliseconds < 0)
        {
            errors.Add("PaperTrading.LeaderActivityExitTrackingPollDelayMilliseconds must not be negative.");
        }

        if (options.LeaderActivityExitTrackingBatchSize <= 0)
        {
            errors.Add("PaperTrading.LeaderActivityExitTrackingBatchSize must be greater than zero.");
        }

        if (options.LeaderActivityExitTrackingActivityLimit <= 0 ||
            options.LeaderActivityExitTrackingActivityLimit > 500)
        {
            errors.Add("PaperTrading.LeaderActivityExitTrackingActivityLimit must be between 1 and 500.");
        }

        if (options.LeaderActivityExitTrackingRequestDelayMilliseconds < 0)
        {
            errors.Add("PaperTrading.LeaderActivityExitTrackingRequestDelayMilliseconds must not be negative.");
        }

        if (options.LeaderActivityExitTrackingErrorDelayMilliseconds <= 0)
        {
            errors.Add("PaperTrading.LeaderActivityExitTrackingErrorDelayMilliseconds must be greater than zero.");
        }

        if (options.LeaderActivityExitTrackingMaxErrorDelayMilliseconds < options.LeaderActivityExitTrackingErrorDelayMilliseconds)
        {
            errors.Add("PaperTrading.LeaderActivityExitTrackingMaxErrorDelayMilliseconds must be greater than or equal to LeaderActivityExitTrackingErrorDelayMilliseconds.");
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

        if (options.CopiedTraderPerformanceMinSettledPositions < 0)
        {
            errors.Add("Signal.CopiedTraderPerformanceMinSettledPositions must not be negative.");
        }

        if (options.CopiedTraderPerformanceMinRoiPct < -100m || options.CopiedTraderPerformanceMinRoiPct > 100m)
        {
            errors.Add("Signal.CopiedTraderPerformanceMinRoiPct must be between -100 and 100.");
        }

        if (options.CopiedTraderPerformanceMinScore < 0m || options.CopiedTraderPerformanceMinScore > 100m)
        {
            errors.Add("Signal.CopiedTraderPerformanceMinScore must be between 0 and 100.");
        }

        if (options.CopiedTraderPerformanceScore < 0)
        {
            errors.Add("Signal.CopiedTraderPerformanceScore must not be negative.");
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

        if (options.DefaultOrderTtlSeconds <= 60 || options.DefaultOrderTtlSeconds > 300)
        {
            errors.Add("LiveTrading.DefaultOrderTtlSeconds must be greater than 60 and at most 300 seconds.");
        }

        if (options.MaintenancePollIntervalSeconds <= 0 || options.MaintenancePollIntervalSeconds > 300)
        {
            errors.Add("LiveTrading.MaintenancePollIntervalSeconds must be between 1 and 300 seconds.");
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

        if (options.StrategyRefreshIntervalSeconds <= 0)
        {
            errors.Add("Dashboard.StrategyRefreshIntervalSeconds must be greater than zero.");
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

    private static void ValidateGammaMarketIngestion(GammaMarketIngestionOptions options, List<string> errors)
    {
        if (options.PollIntervalSeconds < 0 || options.PollIntervalSeconds > 86_400)
        {
            errors.Add("GammaMarketIngestion.PollIntervalSeconds must be between 0 and 86400.");
        }

        if (options.PageLimit <= 0 || options.PageLimit > 500)
        {
            errors.Add("GammaMarketIngestion.PageLimit must be between 1 and 500.");
        }
    }

    private static void ValidateBtcUpDown5mStrategy(BtcUpDown5mStrategyOptions options, List<string> errors)
    {
        if (options.PollIntervalSeconds <= 0 || options.PollIntervalSeconds > 86_400)
        {
            errors.Add("BtcUpDown5mStrategy.PollIntervalSeconds must be between 1 and 86400.");
        }

        if (options.StakeUsd <= 0m)
        {
            errors.Add("BtcUpDown5mStrategy.StakeUsd multiplier must be greater than zero.");
        }

        if (options.EntryGraceSeconds < 0 || options.EntryGraceSeconds > 60)
        {
            errors.Add("BtcUpDown5mStrategy.EntryGraceSeconds must be between 0 and 60.");
        }

        if (options.MaxMarketsPerCycle <= 0)
        {
            errors.Add("BtcUpDown5mStrategy.MaxMarketsPerCycle must be greater than zero.");
        }

        if (options.MaxEntriesPerCycle <= 0)
        {
            errors.Add("BtcUpDown5mStrategy.MaxEntriesPerCycle must be greater than zero.");
        }

        if (options.MaxConcurrentEntryDecisions <= 0 || options.MaxConcurrentEntryDecisions > 32)
        {
            errors.Add("BtcUpDown5mStrategy.MaxConcurrentEntryDecisions must be between 1 and 32.");
        }

        if (options.MaxSettlementsPerCycle <= 0)
        {
            errors.Add("BtcUpDown5mStrategy.MaxSettlementsPerCycle must be greater than zero.");
        }

        if (options.MaxConcurrentSettlements <= 0 || options.MaxConcurrentSettlements > 32)
        {
            errors.Add("BtcUpDown5mStrategy.MaxConcurrentSettlements must be between 1 and 32.");
        }

        if (options.MartinTriggerLosses <= 0 || options.MartinTriggerLosses > 100)
        {
            errors.Add("BtcUpDown5mStrategy.MartinTriggerLosses must be between 1 and 100.");
        }

        if (options.MartinStakeLevels <= 0 || options.MartinStakeLevels > 20)
        {
            errors.Add("BtcUpDown5mStrategy.MartinStakeLevels must be between 1 and 20.");
        }

        if (options.MartinStateLookbackRuns < options.MartinTriggerLosses ||
            options.MartinStateLookbackRuns > 10_000)
        {
            errors.Add("BtcUpDown5mStrategy.MartinStateLookbackRuns must be between MartinTriggerLosses and 10000.");
        }

        if (options.PaperTakerMaxQuoteAgeMilliseconds <= 0 || options.PaperTakerMaxQuoteAgeMilliseconds > 60_000)
        {
            errors.Add("BtcUpDown5mStrategy.PaperTakerMaxQuoteAgeMilliseconds must be between 1 and 60000.");
        }

        if (options.PaperTakerMaxEntryPrice <= 0m || options.PaperTakerMaxEntryPrice > 1m)
        {
            errors.Add("BtcUpDown5mStrategy.PaperTakerMaxEntryPrice must be greater than zero and at most one.");
        }

        if (options.PaperTakerMaxReferenceSlippage < 0m || options.PaperTakerMaxReferenceSlippage > 1m)
        {
            errors.Add("BtcUpDown5mStrategy.PaperTakerMaxReferenceSlippage must be between zero and one.");
        }

        if (options.PaperTakerMaxSpreadAbs < 0m || options.PaperTakerMaxSpreadAbs > 1m)
        {
            errors.Add("BtcUpDown5mStrategy.PaperTakerMaxSpreadAbs must be between zero and one.");
        }

        if (options.PaperTakerMaxGammaClobDiff < 0m || options.PaperTakerMaxGammaClobDiff > 1m)
        {
            errors.Add("BtcUpDown5mStrategy.PaperTakerMaxGammaClobDiff must be between zero and one.");
        }

        if (options.OpeningLimitBreakEvenLookbackRuns <= 0 || options.OpeningLimitBreakEvenLookbackRuns > 10_000)
        {
            errors.Add("BtcUpDown5mStrategy.OpeningLimitBreakEvenLookbackRuns must be between 1 and 10000.");
        }

        if (options.OpeningLimitBreakEvenMinSettledRuns <= 0 ||
            options.OpeningLimitBreakEvenMinSettledRuns > options.OpeningLimitBreakEvenLookbackRuns)
        {
            errors.Add("BtcUpDown5mStrategy.OpeningLimitBreakEvenMinSettledRuns must be between 1 and OpeningLimitBreakEvenLookbackRuns.");
        }

        if (options.OpeningLimitBreakEvenMargin < 0m || options.OpeningLimitBreakEvenMargin > 1m)
        {
            errors.Add("BtcUpDown5mStrategy.OpeningLimitBreakEvenMargin must be between zero and one.");
        }

        if (options.OpeningLimitMaxPrice <= 0m || options.OpeningLimitMaxPrice > 0.50m)
        {
            errors.Add("BtcUpDown5mStrategy.OpeningLimitMaxPrice must be greater than zero and at most 0.50.");
        }

        if (options.OpeningLimitPriceTickSize <= 0m || options.OpeningLimitPriceTickSize > 1m)
        {
            errors.Add("BtcUpDown5mStrategy.OpeningLimitPriceTickSize must be greater than zero and at most one.");
        }

        if (options.OpeningLimitGtdTtlSeconds < 30 || options.OpeningLimitGtdTtlSeconds > 300)
        {
            errors.Add("BtcUpDown5mStrategy.OpeningLimitGtdTtlSeconds must be between 30 and 300.");
        }

        if (options.OpeningLimitExpireBeforeMarketEndSeconds < 0 ||
            options.OpeningLimitExpireBeforeMarketEndSeconds > 300)
        {
            errors.Add("BtcUpDown5mStrategy.OpeningLimitExpireBeforeMarketEndSeconds must be between 0 and 300.");
        }

        if (options.ClobGtdExpirationSecurityBufferSeconds < 60 ||
            options.ClobGtdExpirationSecurityBufferSeconds > 300)
        {
            errors.Add("BtcUpDown5mStrategy.ClobGtdExpirationSecurityBufferSeconds must be between 60 and 300.");
        }

        if (options.PreviousScoreCounterTrendEpsilonScore < 0m ||
            options.PreviousScoreCounterTrendEpsilonScore > 1m)
        {
            errors.Add("BtcUpDown5mStrategy.PreviousScoreCounterTrendEpsilonScore must be between zero and one.");
        }

        if (options.PreviousScoreCounterTrendMinSamples < 2 ||
            options.PreviousScoreCounterTrendMinSamples > 10_000)
        {
            errors.Add("BtcUpDown5mStrategy.PreviousScoreCounterTrendMinSamples must be between 2 and 10000.");
        }

        if (options.PreviousScoreCounterTrendWinsorPercent < 0m ||
            options.PreviousScoreCounterTrendWinsorPercent >= 0.50m)
        {
            errors.Add("BtcUpDown5mStrategy.PreviousScoreCounterTrendWinsorPercent must be between zero and less than 0.50.");
        }

        if (options.PreviousScoreCounterTrendMinUpTimeShare < 0m ||
            options.PreviousScoreCounterTrendMinUpTimeShare > 1m)
        {
            errors.Add("BtcUpDown5mStrategy.PreviousScoreCounterTrendMinUpTimeShare must be between zero and one.");
        }

        if (options.PreviousScoreCounterTrendMinDownTimeShare < 0m ||
            options.PreviousScoreCounterTrendMinDownTimeShare > 1m)
        {
            errors.Add("BtcUpDown5mStrategy.PreviousScoreCounterTrendMinDownTimeShare must be between zero and one.");
        }

        if (options.PaperGtdImmediateFillDepthMultiplier <= 0m ||
            options.PaperGtdImmediateFillDepthMultiplier > 10m)
        {
            errors.Add("BtcUpDown5mStrategy.PaperGtdImmediateFillDepthMultiplier must be greater than zero and at most 10.");
        }

        if (options.PaperGtdMinLateFillEvidenceSeconds < 0 ||
            options.PaperGtdMinLateFillEvidenceSeconds > 300)
        {
            errors.Add("BtcUpDown5mStrategy.PaperGtdMinLateFillEvidenceSeconds must be between 0 and 300.");
        }

        if (options.CloseBookCaptureLookbackSeconds < 0 || options.CloseBookCaptureLookbackSeconds > 600)
        {
            errors.Add("BtcUpDown5mStrategy.CloseBookCaptureLookbackSeconds must be between 0 and 600.");
        }

        if (options.CloseBookCaptureIntervalSeconds <= 0 || options.CloseBookCaptureIntervalSeconds > 300)
        {
            errors.Add("BtcUpDown5mStrategy.CloseBookCaptureIntervalSeconds must be between 1 and 300.");
        }

        if (options.OrderBookRefreshIntervalMilliseconds < 100 ||
            options.OrderBookRefreshIntervalMilliseconds > 60_000)
        {
            errors.Add("BtcUpDown5mStrategy.OrderBookRefreshIntervalMilliseconds must be between 100 and 60000.");
        }

        if (options.OrderBookRefreshMaxMarketsPerCycle <= 0 ||
            options.OrderBookRefreshMaxMarketsPerCycle > 100)
        {
            errors.Add("BtcUpDown5mStrategy.OrderBookRefreshMaxMarketsPerCycle must be between 1 and 100.");
        }

        if (options.OrderBookRefreshMarketLookaheadSeconds < 0 ||
            options.OrderBookRefreshMarketLookaheadSeconds > 600)
        {
            errors.Add("BtcUpDown5mStrategy.OrderBookRefreshMarketLookaheadSeconds must be between 0 and 600.");
        }

        if (options.OrderBookRefreshMarketBehindSeconds < 0 ||
            options.OrderBookRefreshMarketBehindSeconds > 3_600)
        {
            errors.Add("BtcUpDown5mStrategy.OrderBookRefreshMarketBehindSeconds must be between 0 and 3600.");
        }

        if (options.OrderBookRefreshRequestTimeoutSeconds <= 0 ||
            options.OrderBookRefreshRequestTimeoutSeconds > 30)
        {
            errors.Add("BtcUpDown5mStrategy.OrderBookRefreshRequestTimeoutSeconds must be between 1 and 30.");
        }

        if (options.EnabledVariantCodes is null)
        {
            errors.Add("BtcUpDown5mStrategy.EnabledVariantCodes must be an array.");
            return;
        }

        var knownCodes = StrategyIds.BtcUpDown5mVariants
            .Select(variant => variant.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in options.EnabledVariantCodes)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                errors.Add("BtcUpDown5mStrategy.EnabledVariantCodes entries must not be empty.");
                continue;
            }

            var trimmed = code.Trim();
            if (!knownCodes.Contains(trimmed))
            {
                errors.Add($"BtcUpDown5mStrategy.EnabledVariantCodes contains unknown strategy code '{trimmed}'.");
            }

            if (!seenCodes.Add(trimmed))
            {
                errors.Add($"BtcUpDown5mStrategy.EnabledVariantCodes contains duplicate strategy code '{trimmed}'.");
            }
        }
    }

    private static void ValidateCoinbaseExchange(CoinbaseExchangeOptions options, List<string> errors)
    {
        ValidateAbsoluteHttpsUrl(options.BaseUrl, "CoinbaseExchange.BaseUrl", errors);

        if (string.IsNullOrWhiteSpace(options.ProductId))
        {
            errors.Add("CoinbaseExchange.ProductId is required.");
        }

        if (options.PollIntervalSeconds <= 0 || options.PollIntervalSeconds > 86_400)
        {
            errors.Add("CoinbaseExchange.PollIntervalSeconds must be between 1 and 86400.");
        }

        if (options.WindowSize <= 0 || options.WindowSize > 10_000)
        {
            errors.Add("CoinbaseExchange.WindowSize must be between 1 and 10000.");
        }

        if (options.TimeoutSeconds <= 0 || options.TimeoutSeconds > 120)
        {
            errors.Add("CoinbaseExchange.TimeoutSeconds must be between 1 and 120.");
        }

        if (string.IsNullOrWhiteSpace(options.UserAgent))
        {
            errors.Add("CoinbaseExchange.UserAgent is required.");
        }
    }

    private static void ValidateBinanceBtcUsdReference(BinanceBtcUsdReferenceOptions options, List<string> errors)
    {
        ValidateAbsoluteWebSocketUrl(options.StreamUrl, "BinanceBtcUsdReference.StreamUrl", errors);

        if (options.SampleIntervalSeconds <= 0 || options.SampleIntervalSeconds > 86_400)
        {
            errors.Add("BinanceBtcUsdReference.SampleIntervalSeconds must be between 1 and 86400.");
        }

        if (options.WindowSize <= 0 || options.WindowSize > 10_000)
        {
            errors.Add("BinanceBtcUsdReference.WindowSize must be between 1 and 10000.");
        }

        if (options.StaleAfterSeconds <= 0 || options.StaleAfterSeconds > 3_600)
        {
            errors.Add("BinanceBtcUsdReference.StaleAfterSeconds must be between 1 and 3600.");
        }

        if (options.ReconnectBaseDelaySeconds <= 0 || options.ReconnectBaseDelaySeconds > 3_600)
        {
            errors.Add("BinanceBtcUsdReference.ReconnectBaseDelaySeconds must be between 1 and 3600.");
        }

        if (options.ReconnectMaxDelaySeconds <= 0 ||
            options.ReconnectMaxDelaySeconds < options.ReconnectBaseDelaySeconds ||
            options.ReconnectMaxDelaySeconds > 3_600)
        {
            errors.Add("BinanceBtcUsdReference.ReconnectMaxDelaySeconds must be at least the base delay and at most 3600.");
        }

        if (options.ReceiveBufferBytes < 1_024 || options.ReceiveBufferBytes > 1_048_576)
        {
            errors.Add("BinanceBtcUsdReference.ReceiveBufferBytes must be between 1024 and 1048576.");
        }
    }

    private static void ValidateBinanceCryptoReference(BinanceCryptoReferenceOptions options, List<string> errors)
    {
        ValidateAbsoluteWebSocketUrl(options.CombinedStreamBaseUrl, "BinanceCryptoReference.CombinedStreamBaseUrl", errors);
        ValidateCryptoAssetSymbols(options.AssetSymbols, "BinanceCryptoReference.AssetSymbols", errors);

        if (options.StaleAfterSeconds <= 0 || options.StaleAfterSeconds > 3_600)
        {
            errors.Add("BinanceCryptoReference.StaleAfterSeconds must be between 1 and 3600.");
        }

        if (options.ReconnectBaseDelaySeconds <= 0 || options.ReconnectBaseDelaySeconds > 3_600)
        {
            errors.Add("BinanceCryptoReference.ReconnectBaseDelaySeconds must be between 1 and 3600.");
        }

        if (options.ReconnectMaxDelaySeconds <= 0 ||
            options.ReconnectMaxDelaySeconds < options.ReconnectBaseDelaySeconds ||
            options.ReconnectMaxDelaySeconds > 3_600)
        {
            errors.Add("BinanceCryptoReference.ReconnectMaxDelaySeconds must be at least the base delay and at most 3600.");
        }

        if (options.ReceiveBufferBytes < 1_024 || options.ReceiveBufferBytes > 1_048_576)
        {
            errors.Add("BinanceCryptoReference.ReceiveBufferBytes must be between 1024 and 1048576.");
        }
    }

    private static void ValidateBtcUpDown5mOddsArchive(BtcUpDown5mOddsArchiveOptions options, List<string> errors)
    {
        if (options.PollIntervalSeconds <= 0 || options.PollIntervalSeconds > 86_400)
        {
            errors.Add("BtcUpDown5mOddsArchive.PollIntervalSeconds must be between 1 and 86400.");
        }

        if (options.MaxMarketsPerCycle <= 0 || options.MaxMarketsPerCycle > 1_000)
        {
            errors.Add("BtcUpDown5mOddsArchive.MaxMarketsPerCycle must be between 1 and 1000.");
        }

        if (options.MaxOrderBookAgeMilliseconds <= 0 || options.MaxOrderBookAgeMilliseconds > 300_000)
        {
            errors.Add("BtcUpDown5mOddsArchive.MaxOrderBookAgeMilliseconds must be between 1 and 300000.");
        }
    }

    private static void ValidateCryptoUpDown5mOddsArchive(CryptoUpDown5mOddsArchiveOptions options, List<string> errors)
    {
        ValidateCryptoAssetSymbols(options.AssetSymbols, "CryptoUpDown5mOddsArchive.AssetSymbols", errors);

        if (options.PollIntervalSeconds <= 0 || options.PollIntervalSeconds > 86_400)
        {
            errors.Add("CryptoUpDown5mOddsArchive.PollIntervalSeconds must be between 1 and 86400.");
        }

        if (options.MaxMarketsPerCycle <= 0 || options.MaxMarketsPerCycle > 1_000)
        {
            errors.Add("CryptoUpDown5mOddsArchive.MaxMarketsPerCycle must be between 1 and 1000.");
        }

        if (options.MaxOrderBookAgeMilliseconds <= 0 || options.MaxOrderBookAgeMilliseconds > 300_000)
        {
            errors.Add("CryptoUpDown5mOddsArchive.MaxOrderBookAgeMilliseconds must be between 1 and 300000.");
        }
    }

    private static void ValidateCryptoAssetSymbols(IReadOnlyCollection<string> assetSymbols, string optionName, List<string> errors)
    {
        if (assetSymbols.Count == 0)
        {
            errors.Add(optionName + " must include at least one asset symbol.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in assetSymbols)
        {
            if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 16 || !symbol.All(char.IsAsciiLetterOrDigit))
            {
                errors.Add(optionName + " contains an invalid asset symbol.");
                continue;
            }

            if (!seen.Add(symbol.Trim()))
            {
                errors.Add(optionName + " must not contain duplicate asset symbols.");
            }
        }
    }

    private static void ValidateChainlinkBtcUsdDiagnostics(ChainlinkBtcUsdDiagnosticsOptions options, List<string> errors)
    {
        ValidateAbsoluteHttpsUrl(options.BaseUrl, "ChainlinkBtcUsdDiagnostics.BaseUrl", errors);

        if (string.IsNullOrWhiteSpace(options.FeedId))
        {
            errors.Add("ChainlinkBtcUsdDiagnostics.FeedId is required.");
        }

        if (options.PollIntervalSeconds <= 0 || options.PollIntervalSeconds > 86_400)
        {
            errors.Add("ChainlinkBtcUsdDiagnostics.PollIntervalSeconds must be between 1 and 86400.");
        }

        if (options.TimeoutSeconds <= 0 || options.TimeoutSeconds > 120)
        {
            errors.Add("ChainlinkBtcUsdDiagnostics.TimeoutSeconds must be between 1 and 120.");
        }

        if (options.MaxNearestAgeSeconds <= 0 || options.MaxNearestAgeSeconds > 3_600)
        {
            errors.Add("ChainlinkBtcUsdDiagnostics.MaxNearestAgeSeconds must be between 1 and 3600.");
        }

        if (string.IsNullOrWhiteSpace(options.QueryWindow))
        {
            errors.Add("ChainlinkBtcUsdDiagnostics.QueryWindow is required.");
        }
    }

    private static void ValidateOnChainIngestion(OnChainIngestionOptions options, List<string> errors)
    {
        if (!options.Enabled && !options.TradeCaptureEnabled)
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

        if (options.TradeCapturePollDelayMilliseconds < 0)
        {
            errors.Add("OnChainIngestion.TradeCapturePollDelayMilliseconds must not be negative.");
        }

        if (options.TradeCaptureRequestDelayMilliseconds < 0)
        {
            errors.Add("OnChainIngestion.TradeCaptureRequestDelayMilliseconds must not be negative.");
        }

        if (options.TradeCaptureStartLookbackBlocks < 0 || options.TradeCaptureStartLookbackBlocks > 100_000)
        {
            errors.Add("OnChainIngestion.TradeCaptureStartLookbackBlocks must be between 0 and 100000.");
        }

        if (options.TradeCaptureMaxCursorLagBlocks < 0 || options.TradeCaptureMaxCursorLagBlocks > 100_000)
        {
            errors.Add("OnChainIngestion.TradeCaptureMaxCursorLagBlocks must be between 0 and 100000.");
        }

        if (options.TradeCaptureConfirmations < 0 || options.TradeCaptureConfirmations > 1_000)
        {
            errors.Add("OnChainIngestion.TradeCaptureConfirmations must be between 0 and 1000.");
        }

        if (options.TradeCaptureErrorDelayMilliseconds <= 0 || options.TradeCaptureErrorDelayMilliseconds > 86_400_000)
        {
            errors.Add("OnChainIngestion.TradeCaptureErrorDelayMilliseconds must be between 1 and 86400000.");
        }

        if (options.TradeCaptureMaxErrorDelayMilliseconds < options.TradeCaptureErrorDelayMilliseconds ||
            options.TradeCaptureMaxErrorDelayMilliseconds > 86_400_000)
        {
            errors.Add("OnChainIngestion.TradeCaptureMaxErrorDelayMilliseconds must be at least TradeCaptureErrorDelayMilliseconds and at most 86400000.");
        }

        if (options.PaperSignalPollDelayMilliseconds < 0 || options.PaperSignalPollDelayMilliseconds > 86_400_000)
        {
            errors.Add("OnChainIngestion.PaperSignalPollDelayMilliseconds must be between 0 and 86400000.");
        }

        if (options.PaperSignalBatchSize <= 0 || options.PaperSignalBatchSize > 10_000)
        {
            errors.Add("OnChainIngestion.PaperSignalBatchSize must be between 1 and 10000.");
        }

        if (options.PaperSignalMaxLagSeconds <= 0 || options.PaperSignalMaxLagSeconds > 86_400)
        {
            errors.Add("OnChainIngestion.PaperSignalMaxLagSeconds must be between 1 and 86400.");
        }

        if (options.PaperSignalHotMaxAgeSeconds <= 0 || options.PaperSignalHotMaxAgeSeconds > 86_400)
        {
            errors.Add("OnChainIngestion.PaperSignalHotMaxAgeSeconds must be between 1 and 86400.");
        }

        if (options.PaperSignalLatestCandidatesLimit <= 0 || options.PaperSignalLatestCandidatesLimit > 10_000)
        {
            errors.Add("OnChainIngestion.PaperSignalLatestCandidatesLimit must be between 1 and 10000.");
        }

        if (options.PaperSignalRatingStaleAfterHours <= 0 || options.PaperSignalRatingStaleAfterHours > 720)
        {
            errors.Add("OnChainIngestion.PaperSignalRatingStaleAfterHours must be between 1 and 720.");
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

        if (options.SignalCandidateRefreshIntervalSeconds <= 0 || options.SignalCandidateRefreshIntervalSeconds > 86_400)
        {
            errors.Add("OnChainIngestion.SignalCandidateRefreshIntervalSeconds must be between 1 and 86400.");
        }

        if (options.SignalCandidateBatchSize <= 0 || options.SignalCandidateBatchSize > 10_000)
        {
            errors.Add("OnChainIngestion.SignalCandidateBatchSize must be between 1 and 10000.");
        }

        if (options.SignalCandidateQueueSeedBatchSize <= 0 || options.SignalCandidateQueueSeedBatchSize > 100_000)
        {
            errors.Add("OnChainIngestion.SignalCandidateQueueSeedBatchSize must be between 1 and 100000.");
        }

        if (options.SignalCandidateRetryBatchSize <= 0 || options.SignalCandidateRetryBatchSize > 10_000)
        {
            errors.Add("OnChainIngestion.SignalCandidateRetryBatchSize must be between 1 and 10000.");
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

    private static void ValidateAbsoluteWebSocketUrl(string value, string name, List<string> errors)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "wss" && uri.Scheme != "ws"))
        {
            errors.Add($"{name} must be an absolute WS/WSS URL.");
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
